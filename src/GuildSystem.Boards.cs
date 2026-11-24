// Radiant Quests for Meitou. GPLv3 or later.
// Boards: each town's offers, built from what lives around it (the local survey) by asking every quest type for candidates.
//   - Variety: a target is used once per board while others are left (doubles only when the town has nothing else); targets actually
//     standing around the town are favoured over ones that would have to be spawned.
//   - Each tab always has work you can take at your own rank when anything around can make one.
//   - A board lasts the refresh period (setting, days). Taken offers stay taken: a cleared board stays cleared until it refreshes.
//   - Reroll: for a little guild rep, the offers not taken yet are replaced.
using System.Numerics;
using Meitou.Simulation;

namespace RadiantQuests;

public sealed partial class GuildSystem
{
    /// <summary>The refresh period a moment falls in (boards are remade when it changes).</summary>
    long Period(World world) => (long)Math.Floor(world.GameHours / (24.0 * Math.Max(1, _set.RefreshDays)));

    /// <summary>What a reroll costs at <paramref name="rank"/>: the setting, a little more at higher ranks.</summary>
    int RerollCost(int rank) => _set.RerollCost * (1 + rank / 2);

    void Open(World world, CharacterId clerk)
    {
        if (_clerks.TryGetValue(clerk, out var info)) BoardFor(world, info);
    }

    Board BoardFor(World world, ClerkInfo clerk)
    {
        long period = Period(world);
        _s.Boards.RemoveAll(b => b.Day != period);
        if (_s.Boards.FirstOrDefault(b => b.Site == clerk.Site) is { } have) return have;
        var board = new Board { Site = clerk.Site, Day = period, GiverFaction = clerk.Faction };
        Fill(world, board, new Vector2(clerk.Position.X, clerk.Position.Z));
        _s.Boards.Add(board);
        _dirty = true;
        return board;
    }

    /// <summary>Replaces the offers not taken yet, for a little rep.</summary>
    void Reroll(World world, CharacterId clerk)
    {
        if (ClerkInReach(world, clerk) is not { } info) return;
        var board = BoardFor(world, info);
        if (board.Offers.Concat(board.FactionOffers).All(o => o.Taken))
        {
            RadiantQuestsMod.Toast("Nothing left on this board to replace. New work comes when the board refreshes.");
            return;
        }
        int cost = RerollCost(Rank);
        if (!SpendRep(cost))
        {
            RadiantQuestsMod.Toast($"New quests cost {cost} rep to spend, and you have {_s.Balance}.");
            return;
        }
        board.Rerolls++;
        var takenGeneral = board.Offers.Where(o => o.Taken).ToList();
        var takenFaction = board.FactionOffers.Where(o => o.Taken).ToList();
        Fill(world, board, new Vector2(info.Position.X, info.Position.Z));
        // The taken ones stay (as taken), so a cleared rank stays cleared.
        board.Offers.InsertRange(0, takenGeneral);
        board.FactionOffers.InsertRange(0, takenFaction);
        for (int i = 0; i < board.Offers.Count; i++) board.Offers[i].Slot = i;
        for (int i = 0; i < board.FactionOffers.Count; i++) board.FactionOffers[i].Slot = i;
        RadiantQuestsMod.Toast($"The clerk puts up new work. -{cost} rep.");
        _dirty = true;
    }

    /// <summary>Makes the board's offers for both tabs (replacing the untaken ones).</summary>
    void Fill(World world, Board board, Vector2 at)
    {
        var dice = new Dice(world.Seed ^ Rng.Mix((ulong)board.Day * 0x9E3779B97F4A7C15UL) ^ Rng.Mix((ulong)(board.Site + 1) * 0xC2B2AE3D27D4EB4FUL)
            ^ Rng.Mix((ulong)board.Rerolls + 0x51));
        var survey = Survey.Of(world, Access, Cat, at, _set.TargetRange);
        int giver = _data.FactionIndex(board.GiverFaction);
        // One quest is never on both tabs (his rule): a quest that fits both goes to one by a roll (General 60%, Faction 40%), and the tabs
        // share what is used, so nothing is ever on the board twice.
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in board.Offers.Concat(board.FactionOffers).Where(o => o.Taken)) used.Add(KeyOf(o));
        var allGroups = _set.DangerShare > 0 ? Access.Groups(world) : [];
        bool faction = giver >= 0 && _set.FactionQuests;
        var generalPools = Pools(world, survey, Tab.General, giver, board.GiverFaction, dice, allGroups);
        var factionPools = faction ? Pools(world, survey, Tab.Faction, giver, board.GiverFaction, dice, allGroups) : null;
        var notGeneral = new HashSet<string>(StringComparer.Ordinal);
        var notFaction = new HashSet<string>(StringComparer.Ordinal);
        if (factionPools is not null)
        {
            var inFaction = new HashSet<string>(factionPools.SelectMany(p => p).Select(c => KeyOf(c.Offer)), StringComparer.Ordinal);
            foreach (var key in generalPools.SelectMany(p => p).Select(c => KeyOf(c.Offer)).Where(inFaction.Contains).Distinct().OrderBy(k => k, StringComparer.Ordinal))
                (dice.Chance(GeneralShare) ? notFaction : notGeneral).Add(key);
        }
        var brought = new int[Ranks.Top + 1];   // brought-in (spawned) offers per rank on this board, both tabs
        board.FactionOffers = factionPools is not null
            ? MakeTab(world, survey, Tab.Faction, giver, board.GiverFaction, RankWith(board.GiverFaction), dice, used, factionPools, notFaction, brought)
            : [];
        board.Offers = MakeTab(world, survey, Tab.General, giver, board.GiverFaction, Rank, dice, used, generalPools, notGeneral, brought);
        RadiantQuestsMod.Detail($"board for {TownName(board.Site)} (period {board.Day}, reroll {board.Rerolls}): {survey.Groups.Count} groups near, "
            + $"{survey.Regions.Count} regions | " + string.Join("; ", board.Offers.Select(o => $"{Ranks.Name(o.Rank)} {o.Describe()}"))
            + " | faction: " + string.Join("; ", board.FactionOffers.Select(o => $"{Ranks.Name(o.Rank)} {o.Describe()}")));
    }

    /// <summary>A quest that fits both tabs goes to the General tab this often (else the Faction tab).</summary>
    const double GeneralShare = 0.6;

    /// <summary>Every quest type's candidates for <paramref name="tab"/>, by rank (after the area's danger, which may lift one a rank).</summary>
    List<Candidate>[] Pools(World world, Survey survey, Tab tab, int giver, string giverId, Dice dice, IReadOnlyList<GroupInfo> allGroups)
    {
        var ctx = new BoardContext(world, this, survey, tab, giver, giverId, dice);
        var byRank = new List<Candidate>[Ranks.Top + 1];
        for (int b = 0; b <= Ranks.Top; b++) byRank[b] = [];
        foreach (var type in QuestTypes.All.Where(t => t.Offered(_set, tab)))
            for (int b = 0; b <= Ranks.Top; b++)
            {
                try
                {
                    foreach (var c in type.Candidates(ctx, b)) byRank[WithDanger(c.Offer, allGroups)].Add(c);
                }
                catch (Exception e) { RadiantQuestsMod.Log($"{type.Kind} offers skipped: {e.GetType().Name}: {e.Message}"); }
            }
        return byRank;
    }

    List<Offer> MakeTab(World world, Survey survey, Tab tab, int giver, string giverId, int myRank, Dice dice, HashSet<string> used,
        List<Candidate>[] pools, HashSet<string> banned, int[] brought)
    {
        var offers = new List<Offer>();
        var byRank = pools.Select(p => p.Where(c => !banned.Contains(KeyOf(c.Offer))).ToList()).ToArray();
        // Each rank (his rules, 2026-11-23): a cap of 5..7 (setting). Every target really around ALWAYS goes up first, to the cap. Then the
        // slots left may get new kinds of enemy brought in (spawned at accept; local kinds, each now and then one from outside the region:
        // setting), each slot by its own roll. A high rank you have reached with nothing real around gets brought-in work only rarely (A
        // under 50%, S under 20%, SS under 10%), so strong quests stay rare.
        for (int b = 0; b <= Ranks.Top; b++)
        {
            int most = Math.Max(1, _set.MostPerRank);
            int cap = dice.Int(Math.Min(5, most), most);
            var present = byRank[b].Where(c => c.Present).ToList();
            var local = byRank[b].Where(c => !c.Present && !c.Offer.Stray).ToList();
            var stray = byRank[b].Where(c => c.Offer.Stray).ToList();
            int have = offers.Count(o => o.Rank == b);
            while (have < cap && Pick(present, used, offers, dice)) have++;
            // After the real ones: each slot left up to the cap rolls to bring a new kind in (60% at or below your rank, 40% one above, 15%
            // higher). A rank with nothing real: above yours, rarely one or two (BoardCount); at yours, only by the high-rank odds.
            // At ranks you have reached, a minimum (E and D 3, C 2, B 1) is filled with brought-in work if the real ones fall short; the
            // slots above it roll (60% at or below your rank, 40% one above, 15% higher). A rank with nothing real: above yours, rarely one
            // or two (BoardCount); at yours (A and up), only by the high-rank odds. Brought-in A, S and SS work is capped per board (3, 2, 1).
            int must = b <= myRank ? Math.Max(0, MinimumAt(b) - have) : 0;
            int bring = must;
            if (have == 0 && b > myRank) bring = BoardCount(dice, b, myRank);
            else if (have > 0 || must > 0 || dice.Chance(EmptyRankChance(b, myRank)))
                for (int slot = have + must; slot < cap; slot++)
                    if (dice.Chance(BringChance(b, myRank))) bring++;
            bring = Math.Min(bring, Math.Max(0, BroughtCap(b) - brought[b]));
            int before = have;
            for (int i = 0; i < bring && have < cap; i++)
            {
                // The roll within the roll: now and then the new kind comes from outside the region.
                var pool = stray.Count > 0 && dice.Chance(_set.StrayChance) ? stray : local;
                if (!Pick(pool, used, offers, dice) && !Pick(pool == stray ? local : stray, used, offers, dice)) break;
                have++;
            }
            brought[b] += have - before;
            RadiantQuestsMod.Detail($"{tab} {Ranks.Name(b)} (you {Ranks.Name(myRank)}): cap {cap}, real {present.Count} -> {before} up, "
                + $"brought in {have - before} (minimum {must}, local {local.Count}, other regions {stray.Count})");
        }
        // Always something you can take at your own rank, when anything around (or local) can make it.
        int own = Math.Min(myRank, Ranks.Top);
        if (!offers.Any(o => o.Rank == own)) Pick([.. byRank[own].Where(c => !c.Offer.Stray)], used, offers, dice);
        // If nothing makes one at your rank either, the nearest rank below that can (always takeable, full pay) stands in.
        if (!offers.Any(o => o.Rank <= own))
            for (int b = own - 1; b >= 0; b--)
                if (Pick([.. byRank[b].Where(c => !c.Offer.Stray)], used, offers, dice)) break;
        // A Faction tab whose town's enemies around are all too strong for you: the town also wants its pests and outlaws dealt with, so the
        // General kinds of work (at your rank or below) are posted as its own work.
        if (tab == Tab.Faction && !offers.Any(o => o.Rank <= own))
        {
            var local = new BoardContext(world, this, survey, Tab.General, giver, giverId, dice);
            var generalTypes = QuestTypes.All.Where(t => t.Offered(_set, Tab.General)).ToList();
            for (int b = own; b >= 0; b--)
            {
                var pool = new List<Candidate>();
                foreach (var type in generalTypes)
                {
                    try { pool.AddRange(type.Candidates(local, b)); }
                    catch (Exception e) { RadiantQuestsMod.Log($"{type.Kind} offers skipped: {e.GetType().Name}: {e.Message}"); }
                }
                if (Pick([.. pool.Where(c => !c.Offer.Stray)], used, offers, dice)) break;
            }
        }

        foreach (var o in offers)
        {
            var (cMin, cMax) = Ranks.Cats(o.Rank);
            o.Cats = (long)Math.Round((cMin + dice.Double() * (cMax - cMin)) / 10) * 10;
            o.Rep = (int)Math.Round(Ranks.Rep(o.Rank) * (0.85 + 0.3 * dice.Double()));
            o.Relation = tab == Tab.Faction ? Ranks.Relation(o.Rank, Difficulty.Within(o.Score)) : 0;
            // A quest against a faction that isn't hostile to you yet starts a war with it: labelled, and pays more. Bandits and outlaws
            // attack on sight whatever their recorded relation says, so they never count.
            if (o.Kind != Catalog.Hunt && !Cat.Bandits.Contains(o.TargetId) && !Cat.IsOutlaw(o.TargetId)
                && _data.FactionIndex(o.TargetId) is var f && f >= 0 && !Access.Hostile(f, _data.PlayerFaction))
            {
                o.StartsWar = true;
                o.Cats = (long)Math.Round(o.Cats * (1 + _set.WarBonus) / 10) * 10;
            }
        }
        offers = [.. offers.OrderBy(o => o.Rank)];
        for (int i = 0; i < offers.Count; i++) offers[i].Slot = i;
        return offers;
    }

    /// <summary>
    /// Area danger (his idea #9, his rules 2026-11-23): what ELSE that would attack you is close to the offer's place makes it harder. Only
    /// things hostile to you now count (groups whose faction is hostile to yours: animals that attack on sight included, grazers and neutral or
    /// friendly people never), within a small reach (the camp reach), and never the quest's own targets: roaming groups standing there and
    /// camps and nests. A share of their strength (setting, 1/4) is added to the quest's score; the rank rises at most one step, so a big
    /// group passing at the moment the board is made can't push a quest far. Any quest type gets this by saying where its targets are.
    /// Returns the rank.
    /// </summary>
    int WithDanger(Offer o, IReadOnlyList<GroupInfo> groups)
    {
        if (!o.HasAt || _set.DangerShare <= 0 || o.Rank >= Ranks.Top || _data.PlayerFaction < 0) return o.Rank;
        var at = new Vector2(o.AtX, o.AtZ);
        double danger = 0;
        foreach (var g in groups)
        {
            if (g.Standing <= 0 || Vector2.Distance(g.At, at) > DangerReach || !Access.Hostile(g.Faction, _data.PlayerFaction)) continue;
            if (IsOwnTarget(o, g)) continue;
            danger += Catalog.Fight(GroupUnit(g), g.Standing);
        }
        int ownSite = o.Kind == LairQuest.KindId ? (int)o.PlatoonKey : -1;
        foreach (var site in Access.SitesNear(at, DangerReach))
            if (site.Index != ownSite && Cat.Lairs.TryGetValue(site.TownId, out var lair) && HostileCamp(lair))
                danger += Catalog.Fight(lair.Unit, lair.Residents);
        double score = o.Score + _set.DangerShare * danger;
        int rank = Math.Min(o.Rank + 1, Math.Min(Ranks.Top, Difficulty.RankOf(score)));
        if (rank > o.Rank)
        {
            RadiantQuestsMod.Detail($"dangerous area: {o.Describe()} {Ranks.Name(o.Rank)} -> {Ranks.Name(rank)} (danger {danger:0}, score {o.Score:0} -> {score:0})");
            o.Rank = rank;
            o.Dangerous = true;
        }
        o.Score = score;
        return o.Rank;
    }

    /// <summary>A group the quest itself is about (its group, or one of its kind of target): not extra danger.</summary>
    bool IsOwnTarget(Offer o, GroupInfo g)
    {
        if (o.Group || o.Kind == GroupQuest.KindId) return g.Key == o.PlatoonKey || (g.FactionId is { } f && Cat.Head(f) == o.TargetId);
        if (o.Kind == Catalog.Hunt) return Survey.KeysOf(Cat, g).Contains(Survey.Key(Catalog.Hunt, o.TargetId));
        if (o.Kind == Catalog.Cull) return g.FactionId is { } f2 && Cat.Head(f2) == o.TargetId;
        return false;
    }

    /// <summary>A roaming group's fighter strength: its animals' (the strongest race in it), else its faction's typical fighter.</summary>
    double GroupUnit(GroupInfo g)
    {
        double unit = 0;
        if (Cat.TemplateRaces.TryGetValue(g.TemplateId, out var races))
            foreach (var r in races)
                if (Cat.FindTarget(Catalog.Hunt, r) is { Kind: Catalog.Hunt } h) unit = Math.Max(unit, h.Unit);
        if (unit <= 0 && g.FactionId is { } f) unit = Cat.UnitOf.GetValueOrDefault(Cat.Head(f), 0);
        return unit;
    }

    /// <summary>How near a group or camp must be to a quest's place to make it dangerous (the camp reach).</summary>
    const float DangerReach = 1500;

    /// <summary>A camp that would fight you: a wild nest, an outlaw camp, or one of a faction hostile to you.</summary>
    bool HostileCamp(Lair lair) => lair.Owner is null || Cat.Bandits.Contains(lair.Owner)
        || (_data.FactionIndex(lair.Owner) is var f && f >= 0 && Access.Hostile(f, _data.PlayerFaction));

    /// <summary>The fewest quests a rank you have reached shows (his numbers): E and D 3, C 2, B 1, higher none (they stay rare).</summary>
    static int MinimumAt(int b) => b switch { 0 or 1 => 3, 2 => 2, 3 => 1, _ => 0 };

    /// <summary>The most brought-in (spawned) quests one board may hold at a rank (his numbers): A 3, S 2, SS 1. Real ones never count.</summary>
    static int BroughtCap(int b) => b switch { 4 => 3, 5 => 2, 6 => 1, _ => int.MaxValue };

    /// <summary>The chance a rank that has work from what is around also gets one new kind brought in.</summary>
    static double BringChance(int b, int rank) => b <= rank ? 0.6 : b == rank + 1 ? 0.4 : 0.15;

    /// <summary>
    /// A rank with nothing real around: the chance it gets brought-in quests at all, for a rank you have reached (his rule, 2026-11-23): strong
    /// enemies are rare, so A under 50%, S under 20%, SS under 10%. Lower ranks always do; ranks above yours keep their own rarity (BoardCount).
    /// </summary>
    static double EmptyRankChance(int b, int rank) => b > rank ? 1 : b switch { 4 => 0.45, 5 => 0.18, 6 => 0.09, _ => 1 };

    /// <summary>Adds one candidate whose target isn't on this rank of the board yet (weighted). False when there is none.</summary>
    static bool Pick(List<Candidate> pool, HashSet<string> used, List<Offer> offers, Dice dice)
    {
        if (pool.Count == 0) return false;
        var from = pool.Where(c => !used.Contains(KeyOf(c.Offer))).ToList();
        // Each kind of quest gets a fair turn (a town with thirty kinds of animal around still shows its camps and groups): a kind first,
        // evenly among those with something to offer, then one of its offers by weight.
        var kinds = from.Select(c => c.Offer.Kind).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (kinds.Count == 0) return false;
        string kind = kinds[dice.Int(0, kinds.Count - 1)];
        var within = from.Where(c => c.Offer.Kind == kind).ToList();
        int i = dice.Weighted([.. within.Select(c => c.Weight)]);
        if (i < 0) return false;
        var o = within[i].Offer;
        used.Add(KeyOf(o));
        offers.Add(Clone(o));
        return true;
    }

    /// <summary>
    /// What makes two offers the SAME quest (his rules, 2026-11-23): never twice on a board, within a tab or across both tabs. A quest on one
    /// specific group or camp is that group or camp (two "Wipe out" quests on different Dust Bandit groups are fine); any other quest is its
    /// kind and target at its rank (Defeat Starving Bandits at E once; at E and at D is fine). New quest types get this for free.
    /// </summary>
    static string KeyOf(Offer o) => o.Group || o.Kind == GroupQuest.KindId ? "group:" + o.PlatoonKey
        : o.Kind == LairQuest.KindId ? "camp:" + o.PlatoonKey
        : o.Rank + ":" + o.Kind + ":" + o.TargetId;

    static Offer Clone(Offer o) => new()
    {
        Kind = o.Kind, TargetId = o.TargetId, TargetName = o.TargetName, Rank = o.Rank, Need = o.Need, Group = o.Group, GroupSize = o.GroupSize,
        PlatoonKey = o.PlatoonKey, Score = o.Score, Stray = o.Stray, HasAt = o.HasAt, AtX = o.AtX, AtZ = o.AtZ, Dangerous = o.Dangerous,
    };

    /// <summary>
    /// How many quests a board of rank <paramref name="b"/> gets for someone of <paramref name="rank"/>: E and D always 2-4 (up to 5 while you are
    /// E or D); a rank below yours 1-3; your own almost always 1-2; one above sometimes; higher ones rarely.
    /// </summary>
    static int BoardCount(Dice dice, int b, int rank)
    {
        if (b <= 1) return dice.Int(2, rank <= 1 ? 5 : 4);
        if (b < rank) return dice.Int(1, 3);
        if (b == rank) return dice.Chance(0.9) ? dice.Int(1, 2) : 0;
        if (b == rank + 1) return dice.Chance(0.4) ? 1 : 0;
        return dice.Chance(0.15) ? 1 : 0;
    }

    /// <summary>A faction the giver is hostile to now (its wars and the outlaws), never yours or itself.</summary>
    public bool FactionEnemy(Target t, int giver)
    {
        int f = _data.FactionIndex(t.Id);
        if (f < 0 || f == giver || f == _data.PlayerFaction) return false;
        // Bandit-type factions attack on sight whatever their recorded relation says: the town's enemies too, unless friendly with it.
        if (Cat.Bandits.Contains(t.Id)) return Access.Relation(giver, f) < 0;
        return Access.Hostile(giver, f);
    }

    /// <summary>
    /// A faction General quests may ask to cull: not a guild faction, hostile to the giver and to at least three in four guild factions (or marked
    /// as bandits and not friendly with the giver), so bandits and cannibals qualify but a faction you are only at war with does not.
    /// </summary>
    public bool GeneralCull(Target t, int giver)
    {
        int f = _data.FactionIndex(t.Id);
        if (f < 0 || f == _data.PlayerFaction || Array.BinarySearch(_guildFactions, f) >= 0) return false;
        if (Cat.Bandits.Contains(t.Id)) return giver < 0 || Access.Relation(giver, f) < 0;
        if (giver >= 0 && !Access.Hostile(giver, f)) return false;
        if (_guildFactions.Length == 0) return true;
        int hostile = _guildFactions.Count(g => Access.Hostile(g, f));
        return hostile * 4 >= _guildFactions.Length * 3;
    }
}
