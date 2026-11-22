// Radiant Quests for Meitou. GPLv3 or later.
// The guild in the world: finds the clerks (each guild faction's bartenders), makes each town's board once a day, takes, counts, times out and
// pays quests. Everything here runs in the world's serial steps; the interface only reads the published GuildView and queues commands.
using System.Numerics;
using System.Text.Json;
using Meitou.Data.Gameplay;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Population;

namespace RadiantQuests;

/// <summary>A guild clerk the interface can open the window at.</summary>
public sealed record ClerkInfo(CharacterId Id, int Site, string Faction, string FactionName, string TownName, Vector3 Position);

public sealed record QuestView(int Id, string Text, int Rank, int Have, int Need, double HoursLeft, long Cats, int Rep, bool Done, bool General,
    string GiverName, IReadOnlyList<Vector2> Targets, string TargetId = "");

public sealed record OfferView(int Slot, string Text, int Rank, long Cats, int Rep, bool Taken, bool Locked);

public sealed record BoardView(int Site, long Day, IReadOnlyList<OfferView> Offers, IReadOnlyList<OfferView> FactionOffers);

/// <summary>Your standing with one faction's guild: its Faction board rank and lifetime points.</summary>
public sealed record FactionStanding(int Rank, long Points, long NextRankAt, float Relation = 0);

/// <summary>What the interface shows, made by the world after a change (never touched by the world again).</summary>
public sealed record GuildView(long Tick, double Hours, long Lifetime, long Balance, int Rank, long NextRankAt, IReadOnlyList<QuestView> Quests,
    IReadOnlyDictionary<int, BoardView> Boards, IReadOnlyDictionary<CharacterId, ClerkInfo> Clerks,
    IReadOnlyDictionary<string, FactionStanding> Factions);

public sealed partial class GuildSystem : ITickSystem, IStateHashed, IModSaveData, ICharacterDeathObserver, IWorldReady
{
    /// <summary>The newest view of the running world (the interface's thread reads it).</summary>
    static GuildView? s_view;

    public static GuildView? View => Volatile.Read(ref s_view);

    /// <summary>How close (world units) one of your characters must stand to the clerk to take or hand in a quest.</summary>
    const float ClerkReach = 300;

    readonly PopulationData _data;
    readonly PopulationSystem? _population;
    /// <summary>Where each quest's target groups are now (quest id; refreshed every few seconds; read from the world, not saved).</summary>
    readonly Dictionary<int, List<Vector2>> _targets = [];
    long _lastTargets = long.MinValue / 2;
    /// <summary>The target-group feature failed once this session: off until the game restarts.</summary>
    static bool s_targetsBroken;
    readonly int[] _guildFactions;
    readonly Dictionary<string, bool> _barTemplates = new(StringComparer.Ordinal);
    GuildState _s = new();
    SettingsCopy _set = SettingsCopy.Defaults;
    Dictionary<CharacterId, ClerkInfo> _clerks = [];
    long _lastScan = long.MinValue / 2;
    long _lastPublish = long.MinValue / 2;
    bool _dirty = true;

    public GuildSystem(PopulationData data, PopulationSystem? population)
    {
        _data = data;
        _population = population;
        _guildFactions = [.. RadiantQuestsMod.Catalog.GuildFactions.Select(data.FactionIndex).Where(i => i >= 0).Order()];
    }

    Catalog Cat => RadiantQuestsMod.Catalog;

    int Rank => _set.RankOf(_s.Lifetime);

    /// <summary>Your rank on <paramref name="faction"/>'s Faction board (from your lifetime points with it).</summary>
    int RankWith(string faction) => _set.RankOf(_s.PointsWith(faction));

    static long Day(World world) => (long)Math.Floor(world.GameHours / 24);

    #region Tick

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _set = RadiantQuestsMod.Settings.Copy();
        foreach (var c in commands)
        {
            if (c is not GuildCommand) continue;
            RadiantQuestsMod.Detail($"command at tick {world.Tick}: {c}");
            try
            {
                switch (c)
                {
                    case OpenBoard o: Open(world, o.Clerk); break;
                    case AcceptOffer a: Accept(world, a); break;
                    case HandIn h: HandInQuest(world, h); break;
                    case Abandon a: AbandonQuest(world, a.QuestId); break;
                    case TestKill k: TestCount(world, k.TargetId); break;
                    case TestRep r: GainRep(r.Amount); break;
                    case TestFinish f:
                        if (_s.Quests.FirstOrDefault(x => x.Id == f.QuestId) is { } fq)
                        {
                            if (fq.Group)
                            {
                                fq.Hit = true;
                                fq.Have = fq.Need;
                                _dirty = true;
                            }
                            else
                                for (int i = 0; i < 100 && !fq.Done; i++) TestCount(world, fq.TargetId);
                        }
                        break;
                    case TestSkip k:
                        foreach (var q in _s.Quests) if (q.Deadline < double.MaxValue / 2) q.Deadline -= k.Hours;
                        _dirty = true;
                        break;
                }
            }
            catch (Exception e)
            {
                RadiantQuestsMod.Log($"{c.GetType().Name} failed: {e.Message}");
            }
        }
    }

    public void Schedule(World world)
    {
        if (world.Tick - _lastScan >= 120) ScanClerks(world);
        if (world.Tick % 60 == 0) Expire(world);
        if (!s_defeatsBroken && world.Tick - _lastDefeats >= 30)
        {
            try { CheckDefeats(world); }
            catch (Exception e) { DefeatsOff(e); }
        }
        if (!s_targetsBroken && (_s.Quests.Any(q => !q.Placed) || world.Tick - _lastTargets >= 150))
        {
            // Finding and spawning groups uses Meitou's population API; if a Meitou update changes it, this feature turns itself off and the
            // quests carry on without markers or spawns.
            try { Targets(world); }
            catch (Exception e)
            {
                s_targetsBroken = true;
                foreach (var q in _s.Quests) q.Placed = true;
                RadiantQuestsMod.Log($"target groups turned OFF (this Meitou changed what they use): {e.GetType().Name}: {e.Message}");
            }
        }
        if (_dirty || world.Tick - _lastPublish >= 60) Publish(world);
    }

    public void Ready(World world, bool fromSave)
    {
        ScanClerks(world);
        Publish(world);
        RadiantQuestsMod.Detail($"world ready ({(fromSave ? "save" : "new game")}): {_s.Quests.Count} quests, rep {_s.Lifetime}/{_s.Balance}, "
            + $"{_clerks.Count} clerks loaded");
    }

    #endregion

    #region Clerks

    /// <summary>
    /// The guild's contacts: the barman (the leader of the squad living in a bar) of every town owned by a guild faction, whoever runs the bar
    /// (Kenshi often has the Trade Ninjas run other factions' bars, as in The Hub). The work is for the TOWN'S OWNER, read live (a town that
    /// changed hands counts for its new owner): the Faction tab, faction points and relations are the owner's, and Faction quests are handed in
    /// in the owner's towns. Neither the barman's faction nor the owner may be hostile to you now.
    /// </summary>
    void ScanClerks(World world)
    {
        _lastScan = world.Tick;
        var table = world.Characters;
        var found = new Dictionary<CharacterId, ClerkInfo>();
        foreach (var squad in world.Squads.All)
        {
            if (squad.Faction < 0) continue;
            if (squad.HomeBuilding?.IsBar != true && !IsBarTemplate(squad.TemplateId)) continue;
            if (!table.TryResolveNext(squad.Leader, out int slot) || !table.Next[slot].Alive || table.Cold(slot) is not { } cold) continue;
            if (cold.Faction != squad.Faction) continue;

            var pos = table.Next[slot].Position;
            int site = squad.Town >= 0 ? squad.Town : _data.TownAt(pos);
            if (site < 0 || site >= _data.Sites.Count) continue;
            var townData = _data.Sites[site].Town;
            if (townData.Faction is not { } owner || _data.FactionIndex(owner) is var ownerIndex && ownerIndex < 0) continue;
            if (Array.BinarySearch(_guildFactions, ownerIndex) < 0) continue;
            // Nobody hostile to you now (live relations) deals with you: not the barman's faction, not the town's owner.
            if (_data.PlayerFaction >= 0 && (_data.Relations.IsHostile(squad.Faction, _data.PlayerFaction)
                || _data.Relations.IsHostile(ownerIndex, _data.PlayerFaction))) continue;

            found[squad.Leader] = new ClerkInfo(squad.Leader, site, owner, Cat.FactionNames.GetValueOrDefault(owner, owner), townData.Name, pos);
        }

        if (found.Count != _clerks.Count || found.Keys.Any(k => !_clerks.ContainsKey(k))) _dirty = true;
        _clerks = found;
    }

    bool IsBarTemplate(string id)
    {
        if (_barTemplates.TryGetValue(id, out bool bar)) return bar;
        bar = false;
        if (_data.Db.Find(id) is { Type: FcsRecordType.SquadTemplate } rec)
        {
            try { bar = SquadTemplate.From(rec).BuildingDesignation == 3; }
            catch { }
        }
        return _barTemplates[id] = bar;
    }

    /// <summary>The clerk, when one of your characters stands near it; else null with the reason told.</summary>
    ClerkInfo? ClerkInReach(World world, CharacterId clerk)
    {
        if (!_clerks.TryGetValue(clerk, out var info))
        {
            RadiantQuestsMod.Toast("The guild clerk is gone.");
            return null;
        }
        var table = world.Characters;
        if (!table.TryResolveNext(clerk, out int cs)) return null;
        var at = table.Next[cs].Position;
        var next = table.Next;
        for (int s = 0; s < next.Length; s++)
        {
            if (!next[s].Alive || table.Cold(s) is not { IsPlayer: true }) continue;
            var d = new Vector2(next[s].Position.X - at.X, next[s].Position.Z - at.Z);
            if (d.LengthSquared() <= ClerkReach * ClerkReach) return info;
        }
        RadiantQuestsMod.Toast("Walk one of your characters up to the clerk first.");
        return null;
    }

    #endregion

    #region Boards

    void Open(World world, CharacterId clerk)
    {
        if (!_clerks.TryGetValue(clerk, out var info)) return;
        BoardFor(world, info);
    }

    Board BoardFor(World world, ClerkInfo clerk)
    {
        long day = Day(world);
        _s.Boards.RemoveAll(b => b.Day != day);
        if (_s.Boards.FirstOrDefault(b => b.Site == clerk.Site) is { } have) return have;

        var board = MakeBoard(world, clerk.Site, clerk.Faction, day, new Vector2(clerk.Position.X, clerk.Position.Z));
        _s.Boards.Add(board);
        _dirty = true;
        RadiantQuestsMod.Detail($"board for {clerk.TownName} (site {clerk.Site}), day {day}: "
            + string.Join("; ", board.Offers.Select(o => $"{Ranks.Name(o.Rank)} {o.Describe()} {o.Cats}c {o.Rep}r"))
            + " | faction: " + string.Join("; ", board.FactionOffers.Select(o => $"{Ranks.Name(o.Rank)} {o.Describe()} {o.Cats}c {o.Rep}r")));
        return board;
    }

    /// <summary>
    /// Today's General board of a town: D always has 2-4 quests (up to 5 while you are D or C); a rank below yours 1-3; your own rank almost
    /// always 1-2; one above sometimes; higher ones rarely.
    /// </summary>
    Board MakeBoard(World world, int site, string giver, long day, Vector2 at)
    {
        var rng = new Roll(world.Seed ^ Rng.Mix((ulong)day * 0x9E3779B97F4A7C15UL) ^ Rng.Mix((ulong)(site + 1) * 0xC2B2AE3D27D4EB4FUL));
        int rank = Rank;
        int giverIndex = _data.FactionIndex(giver);
        var board = new Board { Site = site, Day = day, GiverFaction = giver };
        var used = new HashSet<string>(StringComparer.Ordinal);
        _near = NearTown(world, at);

        for (int b = 0; b <= Ranks.Top; b++)
        {
            int count = BoardCount(ref rng, b, rank);
            // Any target with a count (2 to 12) that lands on this rank: strong fighters need few, weak ones many.
            var hunts = _set.Hunts ? Cat.Hunts.Where(t => Counts(t, b).Count > 0).ToList() : [];
            var culls = _set.Culls ? Cat.Culls.Where(t => GeneralCull(t, giverIndex) && Counts(t, b).Count > 0).ToList() : [];
            for (int i = 0; i < count; i++)
            {
                // Never the same target twice on one board: pick a kind that still has a fresh target, then one of those.
                var freshHunts = hunts.Where(t => !used.Contains(t.Kind + t.Id)).ToList();
                var freshCulls = culls.Where(t => !used.Contains(t.Kind + t.Id)).ToList();
                var fresh = (freshHunts.Count, freshCulls.Count) switch
                {
                    (0, 0) => null,
                    (_, 0) => freshHunts,
                    (0, _) => freshCulls,
                    _ => rng.Chance(0.5) ? freshHunts : freshCulls,
                };
                if (fresh is null) break;
                var target = fresh[rng.Int(0, fresh.Count - 1)];
                used.Add(target.Kind + target.Id);

                var counts = Counts(target, b);
                var (cMin, cMax) = Ranks.Cats(b);
                board.Offers.Add(new Offer
                {
                    Slot = board.Offers.Count,
                    Kind = target.Kind,
                    TargetId = target.Id,
                    TargetName = target.Name,
                    Rank = b,
                    Need = counts[rng.Int(0, counts.Count - 1)],
                    Cats = (long)Math.Round((cMin + rng.Double() * (cMax - cMin)) / 10) * 10,
                    Rep = (int)Math.Round(Ranks.Rep(b) * (0.85 + 0.3 * rng.Double())),
                });
            }
        }
        if (_set.Culls) AddGroupOffers(world, board.Offers, ref rng, at, rank, t => GeneralCull(t, giverIndex), Cat.Culls);

        // The Faction tab: kill quests against the giver's enemies now (live relations: its wars and the outlaws), by your rank with the giver.
        if (_set.FactionQuests && giverIndex >= 0)
        {
            int frank = RankWith(giver);
            var fused = new HashSet<string>(StringComparer.Ordinal);
            for (int b = 0; b <= Ranks.Top; b++)
            {
                int count = BoardCount(ref rng, b, frank);
                var enemies = Cat.WarTargets.Where(t => FactionEnemy(t, giverIndex) && Counts(t, b).Count > 0).ToList();
                for (int i = 0; i < count; i++)
                {
                    var fresh = enemies.Where(t => !fused.Contains(t.Id)).ToList();
                    if (fresh.Count == 0) break;
                    var target = fresh[rng.Int(0, fresh.Count - 1)];
                    fused.Add(target.Id);
                    var counts = Counts(target, b);
                    var (cMin, cMax) = Ranks.Cats(b);
                    board.FactionOffers.Add(new Offer
                    {
                        Slot = board.FactionOffers.Count,
                        Kind = target.Kind,
                        TargetId = target.Id,
                        TargetName = target.Name,
                        Rank = b,
                        Need = counts[rng.Int(0, counts.Count - 1)],
                        Cats = (long)Math.Round((cMin + rng.Double() * (cMax - cMin)) / 10) * 10,
                        Rep = (int)Math.Round(Ranks.Rep(b) * (0.85 + 0.3 * rng.Double())),
                    });
                }
            }
            AddGroupOffers(world, board.FactionOffers, ref rng, at, frank, t => FactionEnemy(t, giverIndex), Cat.WarTargets);
        }

        // A quest against a faction that isn't hostile to you yet starts a war with it: labelled, and pays more.
        foreach (var o in board.Offers.Concat(board.FactionOffers))
        {
            if (o.Kind != Catalog.Cull || _data.PlayerFaction < 0 || _data.FactionIndex(o.TargetId) is var f && f < 0) continue;
            // Bandits and outlaws attack on sight whatever their recorded relation says: fighting them starts no war.
            if (Cat.Bandits.Contains(o.TargetId) || Cat.Culls.Any(t => t.Id == o.TargetId)) continue;
            if (_data.Relations.IsHostile(f, _data.PlayerFaction)) continue;
            o.StartsWar = true;
            o.Cats = (long)Math.Round(o.Cats * (1 + _set.WarBonus) / 10) * 10;
        }
        return board;
    }

    /// <summary>
    /// How many quests a board of rank <paramref name="b"/> gets for someone of <paramref name="rank"/>: E and D always 2-4 (up to 5 while you are E or D);
    /// a rank below yours 1-3; your own almost always 1-2; one above sometimes; higher ones rarely.
    /// </summary>
    static int BoardCount(ref Roll rng, int b, int rank)
    {
        // E and D always have work (2-4; up to 5 while you are E or D).
        if (b <= 1) return rng.Int(2, rank <= 1 ? 5 : 4);
        if (b < rank) return rng.Int(1, 3);
        if (b == rank) return rng.Chance(0.9) ? rng.Int(1, 2) : 0;
        if (b == rank + 1) return rng.Chance(0.4) ? 1 : 0;
        return rng.Chance(0.15) ? 1 : 0;
    }

    /// <summary>How many of each target live near the town now (hunt race or kill family head; set while a board is made).</summary>
    Dictionary<string, int> _near = new(StringComparer.Ordinal);

    /// <summary>How many of each target are within the target range of <paramref name="at"/> (the world's groups, by size).</summary>
    Dictionary<string, int> NearTown(World world, Vector2 at)
    {
        var near = new Dictionary<string, int>(StringComparer.Ordinal);
        if (s_targetsBroken) return near;
        try
        {
            foreach (var p in world.Platoons.All)
            {
                if (p.Dead || Vector2.Distance(PlatoonPosition(world, p), at) > _set.TargetRange) continue;
                int size = Math.Max(1, p.Size);
                if (p.Faction >= 0 && _data.FactionId(p.Faction) is { } f) near[Catalog.Cull + Cat.Head(f)] = near.GetValueOrDefault(Catalog.Cull + Cat.Head(f)) + size;
                if (Cat.TemplateRaces.TryGetValue(p.TemplateId, out var races))
                    foreach (var r in races) near[Catalog.Hunt + r] = near.GetValueOrDefault(Catalog.Hunt + r) + size;
            }
        }
        catch (Exception e)
        {
            RadiantQuestsMod.Log($"counting groups near town skipped: {e.GetType().Name}: {e.Message}");
        }
        return near;
    }

    /// <summary>
    /// The counts (2 to 12) of <paramref name="t"/> whose quest lands on rank <paramref name="rank"/>. A target the mod can't spawn (no region
    /// it naturally lives in) only gets counts that are already near the town, so its quest can always be finished.
    /// </summary>
    List<int> Counts(Target t, int rank)
    {
        int cap = Cat.CanSpawn(t) && _set.SpawnTargets ? 12 : Math.Min(12, _near.GetValueOrDefault(t.Kind + t.Id));
        double unit = t.Kind == Catalog.Cull ? Cat.UnitOf.GetValueOrDefault(t.Id, t.Unit) : t.Unit;
        double typical = t.Kind == Catalog.Cull ? Cat.SizeOf.GetValueOrDefault(t.Id, 1) : Cat.PackOf.GetValueOrDefault(t.Id, 1);
        var list = new List<int>();
        for (int n = 2; n <= cap; n++)
            if (Cat.QuestRank(t.Kind, unit, n, typical) == rank) list.Add(n);
        return list;
    }

    /// <summary>A faction the giver is hostile to now (its wars and the outlaws), never yours or itself.</summary>
    bool FactionEnemy(Target t, int giver)
    {
        int f = _data.FactionIndex(t.Id);
        return f >= 0 && f != giver && f != _data.PlayerFaction && _data.Relations.IsHostile(giver, f);
    }

    /// <summary>
    /// A faction General quests may ask to cull: not a guild faction, hostile to the giver and to at least three in four guild factions, so
    /// bandits and cannibals qualify but a faction you are only at war with does not.
    /// </summary>
    bool GeneralCull(Target t, int giver)
    {
        int f = _data.FactionIndex(t.Id);
        if (f < 0 || f == _data.PlayerFaction || Array.BinarySearch(_guildFactions, f) >= 0) return false;
        // A faction the data marks as bandits is fair game unless it is friendly with the giver now.
        if (Cat.Bandits.Contains(t.Id)) return giver < 0 || _data.Relations.Get(giver, f) < 0;
        if (giver >= 0 && !_data.Relations.IsHostile(giver, f)) return false;
        if (_guildFactions.Length == 0) return true;
        int hostile = _guildFactions.Count(g => _data.Relations.IsHostile(g, f));
        return hostile * 4 >= _guildFactions.Length * 3;
    }

    #endregion

    #region Quests

    void Accept(World world, AcceptOffer a)
    {
        if (ClerkInReach(world, a.Clerk) is not { } clerk) return;
        var board = BoardFor(world, clerk);
        var offers = a.Faction ? board.FactionOffers : board.Offers;
        if (board.Site != a.Site || a.Slot < 0 || a.Slot >= offers.Count) return;
        var offer = offers[a.Slot];
        int rank = a.Faction ? RankWith(clerk.Faction) : Rank;
        if (offer.Taken) return;
        if (offer.Rank > rank)
        {
            RadiantQuestsMod.Toast(a.Faction
                ? $"You need rank {Ranks.Name(offer.Rank)} with {clerk.FactionName} for that quest."
                : $"You need guild rank {Ranks.Name(offer.Rank)} for that quest.");
            return;
        }

        var (catsShare, repShare) = Ranks.BelowRankPay(rank - offer.Rank);
        int id = _s.NextId;
        var q = new Quest
        {
            Id = id,
            Kind = offer.Kind,
            TargetId = offer.TargetId,
            TargetName = offer.TargetName,
            Rank = offer.Rank,
            Need = offer.Need,
            Cats = (long)Math.Round(offer.Cats * catsShare * _set.CatsMult),
            Rep = (int)Math.Round(offer.Rep * repShare * _set.RepMult),
            GiverFaction = clerk.Faction,
            Site = clerk.Site,
            General = !a.Faction,
            Deadline = world.GameHours + _set.KillHours,
            AnchorX = clerk.Position.X,
            AnchorZ = clerk.Position.Z,
            Group = offer.Group,
            GroupSize = offer.GroupSize,
            StartsWar = offer.StartsWar,
        };
        if (q.Group && !TagGroup(world, offer, q))
        {
            offer.Taken = true;
            _dirty = true;
            RadiantQuestsMod.Toast("That group has moved on or been broken since the board was put up. Pick another quest.");
            return;
        }
        _s.NextId++;
        offer.Taken = true;
        _s.Quests.Add(q);
        _dirty = true;
        RadiantQuestsMod.Toast($"Quest taken{(q.General ? "" : $" for {clerk.FactionName}")}: {q.Describe()} ({Ranks.Name(q.Rank)}). "
            + $"{_set.KillHours / 24.0:0.#} days.");
    }

    void HandInQuest(World world, HandIn h)
    {
        var q = _s.Quests.FirstOrDefault(x => x.Id == h.QuestId);
        if (q is null) return;
        if (!q.Done)
        {
            RadiantQuestsMod.Toast($"Not done yet: {q.Have}/{q.Need}.");
            return;
        }
        if (ClerkInReach(world, h.Clerk) is not { } clerk) return;
        if (!q.General && clerk.Faction != q.GiverFaction)
        {
            RadiantQuestsMod.Toast("Hand this one in to the faction that gave it.");
            return;
        }

        _s.Quests.Remove(q);
        Release(world, q);
        world.Player.Money += q.Cats;
        if (q.General)
            RadiantQuestsMod.Toast($"Quest done: {q.Describe()}. +{q.Cats} cats, +{q.Rep} guild rep.");
        else
        {
            int relation = Ranks.Relation(q.Rank);
            RadiantQuestsMod.Toast($"Quest done for {clerk.FactionName}: {q.Describe()}. +{q.Cats} cats, +{q.Rep} guild rep, +{q.Rep} "
                + $"{clerk.FactionName} points" + (relation > 0 ? $", relations +{relation}." : "."));
            GainPoints(q.GiverFaction, q.Rep);
            ChangeRelation(world, q.GiverFaction, relation, "Guild quest");
        }
        GainRep(q.Rep);
        _dirty = true;
    }

    void AbandonQuest(World world, int id)
    {
        var q = _s.Quests.FirstOrDefault(x => x.Id == id);
        if (q is null) return;
        _s.Quests.Remove(q);
        Release(world, q);
        int cost = (int)Math.Round(q.Rep * _set.AbandonCost);
        RadiantQuestsMod.Toast($"Quest abandoned: {q.Describe()}." + (cost > 0 ? $" -{cost} guild rep" + (q.General ? "." : " and faction points.") : ""));
        LoseRep(cost);
        if (!q.General) LosePoints(q.GiverFaction, cost);
        _dirty = true;
    }

    void Expire(World world)
    {
        for (int i = _s.Quests.Count - 1; i >= 0; i--)
        {
            var q = _s.Quests[i];
            // A faction that turned hostile to you takes back its quests, with no cost to you.
            if (!q.General && _data.PlayerFaction >= 0 && _data.FactionIndex(q.GiverFaction) is var g && g >= 0
                && _data.Relations.IsHostile(g, _data.PlayerFaction))
            {
                _s.Quests.RemoveAt(i);
                Release(world, q);
                RadiantQuestsMod.Toast($"{Cat.FactionNames.GetValueOrDefault(q.GiverFaction, q.GiverFaction)} is hostile to you now: "
                    + $"their quest is called off ({q.Describe()}), no penalty.");
                _dirty = true;
                continue;
            }
            if (q.Done || world.GameHours <= q.Deadline) continue;
            _s.Quests.RemoveAt(i);
            Release(world, q);
            int cost = (int)Math.Round(q.Rep * _set.FailCost);
            int relation = q.General ? 0 : Ranks.FailRelation(q.Rank);
            RadiantQuestsMod.Toast($"Quest failed (out of time): {q.Describe()}." + (cost > 0 ? $" -{cost} guild rep" + (q.General ? "." : " and faction points.") : "")
                + (relation < 0 ? $" Relations {relation}." : ""));
            LoseRep(cost);
            if (!q.General)
            {
                LosePoints(q.GiverFaction, cost);
                ChangeRelation(world, q.GiverFaction, relation, "Guild quest failed");
            }
            _dirty = true;
        }
    }

    void GainPoints(string faction, int amount)
    {
        if (amount <= 0) return;
        int before = RankWith(faction);
        _s.FactionPoints[faction] = _s.PointsWith(faction) + amount;
        if (RankWith(faction) > before)
            RadiantQuestsMod.Toast($"Rank up with {Cat.FactionNames.GetValueOrDefault(faction, faction)}! You are now their rank {Ranks.Name(RankWith(faction))}.");
    }

    void LosePoints(string faction, int amount)
    {
        if (amount <= 0 || !_s.FactionPoints.ContainsKey(faction)) return;
        int before = RankWith(faction);
        _s.FactionPoints[faction] = Math.Max(0, _s.PointsWith(faction) - amount);
        if (RankWith(faction) < before)
            RadiantQuestsMod.Toast($"Your rank with {Cat.FactionNames.GetValueOrDefault(faction, faction)} dropped to {Ranks.Name(RankWith(faction))}.");
    }

    /// <summary>The faction's real relation with you, through the game's own path (its "now friendly" style messages included).</summary>
    void ChangeRelation(World world, string faction, int delta, string why)
    {
        int f = _data.FactionIndex(faction), player = _data.PlayerFaction;
        if (delta == 0 || f < 0 || player < 0) return;
        try
        {
            Meitou.Simulation.Factions.PlayerRelations.Add(world, _data.Relations, f, player, delta,
                new Meitou.Simulation.Factions.RelationCause(Meitou.Simulation.Factions.RelationCauseType.Dialogue, CharacterId.None), why);
        }
        catch (Exception e)
        {
            RadiantQuestsMod.Log($"relation change skipped (this Meitou changed it): {e.GetType().Name}: {e.Message}");
        }
    }

    public void Died(World world, in CharacterDeath death)
    {
        if (_s.Quests.Count == 0 || _data.PlayerFaction < 0) return;
        if (death.Faction == _data.PlayerFaction) return;
        if (s_defeatsBroken) return;
        try
        {
            bool yours = death.KillerFaction == _data.PlayerFaction
                || (_hits.TryGetValue(death.Victim, out long t) && world.Tick - t <= DefeatWindow);
            if (!yours || !world.Characters.TryResolveNext(death.Victim, out int s) || world.Characters.Cold(s) is not { } cold) return;
            RadiantQuestsMod.Detail($"kill: {death.Record} (faction {_data.FactionId(death.Faction)})");
            if (cold.LastingId.Value == 0) world.LastingIds.GetOrGive(death.Victim);
            Defeat(world, death.Victim, cold);
        }
        catch (Exception e)
        {
            DefeatsOff(e);
        }
    }


    void GainRep(int amount)
    {
        if (amount <= 0) return;
        int before = Rank;
        _s.Lifetime += amount;
        _s.Balance += amount;
        _dirty = true;
        if (Rank > before) RadiantQuestsMod.Toast($"Guild rank up! You are now rank {Ranks.Name(Rank)}: harder, better-paid quests are open.");
    }

    void LoseRep(int amount)
    {
        if (amount <= 0) return;
        int before = Rank;
        _s.Lifetime = Math.Max(0, _s.Lifetime - amount);
        _s.Balance = Math.Max(0, _s.Balance - amount);
        _dirty = true;
        if (Rank < before) RadiantQuestsMod.Toast($"Your guild rank dropped to {Ranks.Name(Rank)}.");
    }

    #endregion

    #region Target groups

    /// <summary>
    /// Finds each hunt and kill quest's target groups within the target range of where it was taken, for the map. A quest just taken with none in
    /// range gets one ordinary group of them spawned there (a roaming squad of the target's own spawn list, never a unique).
    /// </summary>
    void Targets(World world)
    {
        _lastTargets = world.Tick;
        float range = _set.TargetRange;
        _targets.Clear();
        foreach (var q in _s.Quests.ToList())
        {
            if (q.Kind is not (Catalog.Cull or Catalog.Hunt)) continue;
            if (q.Group)
            {
                q.Placed = true;
                var group = world.Platoons.All.FirstOrDefault(p => p.ModTag == q.Tag && !p.Dead);
                if (group is null)
                {
                    if (GroupGone(world, q)) Release(world, q);
                    continue;
                }
                _targets[q.Id] = [PlatoonPosition(world, group)];
                continue;
            }
            var anchor = new Vector2(q.AnchorX, q.AnchorZ);
            var found = new List<(float D, Vector2 At, int Size)>();
            foreach (var p in world.Platoons.All)
            {
                if (p.Dead || !Matches(q, p)) continue;
                var at = PlatoonPosition(world, p);
                float d = Vector2.Distance(at, anchor);
                // The quest's own spawns count wherever they wander (they may live further out than the target range).
                if (d <= range || p.ModTag == q.Tag) found.Add((d, at, Math.Max(1, p.Size)));
            }
            _targets[q.Id] = [.. found.OrderBy(f => f.D).Take(5).Select(f => f.At)];

            if (!q.Placed)
            {
                q.Placed = true;
                _dirty = true;
                // Enough of them must be within reach: spawn groups until there are (at most four), else lower the count to what is there,
                // so a quest never asks for two of an animal that roams alone with only one around.
                int there = found.Sum(f => f.Size);
                for (int i = 0; i < 4 && there < q.Need - q.Have && !q.Done && _set.SpawnTargets; i++)
                {
                    int added = Spawn(world, q, anchor, range);
                    if (added <= 0) break;
                    there += added;
                }
                if (there < q.Need - q.Have && !q.Done)
                {
                    int need = q.Have + Math.Max(1, there);
                    RadiantQuestsMod.Toast($"Only {Math.Max(1, there)} {q.TargetName} could be found near enough: the quest now asks for {need - q.Have}.");
                    q.Need = need;
                }
            }
        }
        _dirty = true;
    }

    bool Matches(Quest q, Meitou.Simulation.Factions.Platoon p) => q.Kind switch
    {
        Catalog.Cull => p.Faction >= 0 && _data.FactionId(p.Faction) is { } f && Cat.Head(f) == q.TargetId,
        Catalog.Hunt => Cat.TemplateRaces.TryGetValue(p.TemplateId, out var races) && races.Contains(q.TargetId),
        _ => false,
    };

    /// <summary>A platoon's place: its squad leader's while it is loaded, else where it stands off screen.</summary>
    static Vector2 PlatoonPosition(World world, Meitou.Simulation.Factions.Platoon p)
    {
        if (p.SquadId >= 0 && world.Squads.Find(p.SquadId) is { } squad && world.Characters.TryResolveNext(squad.Leader, out int slot))
        {
            var at = world.Characters.Next[slot].Position;
            return new Vector2(at.X, at.Z);
        }
        return p.Position;
    }

    /// <summary>
    /// A quest is over: the group spawned for it (if any) goes back to being an ordinary roaming group, which Meitou despawns as usual, so the mod
    /// leaves nothing behind.
    /// </summary>
    void Release(World world, Quest q)
    {
        if (s_targetsBroken) return;
        try
        {
            string tag = $"radiantquests:{q.Id}";
            foreach (var p in world.Platoons.All)
                if (p.ModTag == tag)
                {
                    p.ModSpawned = false;
                    p.ModTag = "";
                    RadiantQuestsMod.Detail($"quest #{q.Id}: released its spawned group (platoon {p.Id})");
                }
        }
        catch (Exception e)
        {
            RadiantQuestsMod.Log($"releasing spawned groups skipped (this Meitou changed it): {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// A point where <paramref name="template"/> naturally spawns (one of its regions), as near the town as can be: rings from a third of the
    /// target range out to the furthest-spawn setting, sixteen points each in a seeded order. Null when none is found, or when the template has
    /// no regions in the data, or the world has no region map: then nothing is spawned rather than something out of place.
    /// </summary>
    Vector2? NaturalSpot(World world, string template, Vector2 anchor, float range, ref Roll rng)
    {
        if (!Cat.TemplateRegions.TryGetValue(template, out var regions) || regions.Count == 0) return null;
        float limit = Math.Max(range, _set.SpawnLimit);
        for (float r = range * 0.35f; r <= limit; r += Math.Max(2000, range * 0.2f))
        {
            double start = rng.Double() * Math.PI * 2;
            for (int i = 0; i < 16; i++)
            {
                double a = start + i * Math.PI / 8;
                var p = anchor + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * r;
                if (_data.BiomeGroupAt(p) is { } g && regions.Contains(g)) return p;
            }
        }
        return null;
    }

    /// <summary>Spawns one group of the quest's target in range; returns how many it brings (0: none spawned).</summary>
    int Spawn(World world, Quest q, Vector2 anchor, float range)
    {
        var list = q.Kind == Catalog.Cull ? Cat.CullTemplates.GetValueOrDefault(q.TargetId) : Cat.HuntTemplates.GetValueOrDefault(q.TargetId);
        if (_population is null || list is null || list.Count == 0 || q.Site < 0)
        {
            RadiantQuestsMod.Detail($"quest #{q.Id}: no {q.TargetName} in range and nothing to spawn");
            return 0;
        }
        var rng = new Roll(world.Seed ^ Rng.Mix((ulong)q.Id * 0x9E3779B97F4A7C15UL) ^ 0x5241444951554553UL);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string template = list[rng.Int(0, list.Count - 1)];
            if (_data.Db.Find(template) is not { Type: FcsRecordType.SquadTemplate } rec) continue;
            int faction;
            try { faction = _data.SpawnFactionOf(SquadTemplate.From(rec)); }
            catch { continue; }
            // Never spawn a group that belongs to a guild faction or to you.
            if (faction == _data.PlayerFaction || Array.BinarySearch(_guildFactions, faction) >= 0) continue;
            if (NaturalSpot(world, template, anchor, range, ref rng) is not { } spot) continue;
            int id = _population.SpawnPlatoon(world, faction, template, q.Site, spot, 0, 1500);
            if (id < 0) continue;
            int size = 1;
            if (world.Platoons.Find(id) is { } platoon)
            {
                // Several spawns for one quest share its tag; each is released when the quest ends.
                platoon.ModTag = $"radiantquests:{q.Id}";
                size = Math.Max(1, platoon.Size);
                if (!_targets.TryGetValue(q.Id, out var spots)) _targets[q.Id] = spots = [];
                spots.Add(PlatoonPosition(world, platoon));
            }
            RadiantQuestsMod.Detail($"quest #{q.Id}: none in range, spawned {template} ({_data.FactionId(faction)}) as platoon {id}");
            RadiantQuestsMod.Toast($"Word is a group of {q.TargetName} was seen nearby. It's marked on your map.");
            return size;
        }
        RadiantQuestsMod.Detail($"quest #{q.Id}: none in range and the spawn found no place");
        return 0;
    }

    #endregion

    #region View, hash, save

    /// <summary>Publishes the view at once (between ticks, after the paused interface's commands).</summary>
    public void PublishNow(World world) => Publish(world);

    void Publish(World world)
    {
        _dirty = false;
        _lastPublish = world.Tick;
        int rank = Rank;
        var quests = _s.Quests.Select(q => new QuestView(q.Id, q.Describe(), q.Rank, q.Have, q.Need,
            q.Deadline >= double.MaxValue / 2 ? double.PositiveInfinity : q.Deadline - world.GameHours, q.Cats, q.Rep, q.Done, q.General,
            Cat.FactionNames.GetValueOrDefault(q.GiverFaction, q.GiverFaction),
            q.Done ? [] : _targets.TryGetValue(q.Id, out var at) ? [.. at] : [], q.TargetId)).ToList();
        var boards = new Dictionary<int, BoardView>();
        OfferView View(Offer o, int myRank)
        {
            var (c, r) = Ranks.BelowRankPay(myRank - o.Rank);
            return new OfferView(o.Slot, o.Describe(), o.Rank, (long)Math.Round(o.Cats * c * _set.CatsMult),
                (int)Math.Round(o.Rep * r * _set.RepMult), o.Taken, o.Rank > myRank);
        }
        foreach (var b in _s.Boards)
        {
            int frank = RankWith(b.GiverFaction);
            boards[b.Site] = new BoardView(b.Site, b.Day, [.. b.Offers.Select(o => View(o, rank))], [.. b.FactionOffers.Select(o => View(o, frank))]);
        }
        var standings = new Dictionary<string, FactionStanding>(StringComparer.Ordinal);
        foreach (var f in _clerks.Values.Select(c => c.Faction).Concat(_s.FactionPoints.Keys).Distinct())
        {
            int fr = RankWith(f);
            int fi = _data.FactionIndex(f);
            float rel = fi >= 0 && _data.PlayerFaction >= 0 ? _data.Relations.Get(fi, _data.PlayerFaction) : 0;
            standings[f] = new FactionStanding(fr, _s.PointsWith(f), _set.NextRankAt(fr), rel);
        }
        Volatile.Write(ref s_view, new GuildView(world.Tick, world.GameHours, _s.Lifetime, _s.Balance, rank, _set.NextRankAt(rank), quests, boards,
            new Dictionary<CharacterId, ClerkInfo>(_clerks), standings));
    }

    public void Hash(ref StateHasher hasher)
    {
        _s.Hash(ref hasher);
        HashDefeats(ref hasher);
    }

    static readonly JsonSerializerOptions s_json = new() { IncludeFields = true };

    public void Write(Stream stream) => JsonSerializer.Serialize(stream, _s, s_json);

    public void Read(Stream stream)
    {
        try
        {
            _s = JsonSerializer.Deserialize<GuildState>(stream, s_json) ?? new GuildState();
            _s.Quests ??= [];
            _s.Boards ??= [];
            _s.FactionPoints = new Dictionary<string, long>(_s.FactionPoints ?? [], StringComparer.Ordinal);
            foreach (var b in _s.Boards) b.FactionOffers ??= [];
            foreach (var q in _s.Quests) q.Defeated ??= [];
            _dirty = true;
        }
        catch (Exception e)
        {
            RadiantQuestsMod.Log($"could not read the saved guild state, starting fresh: {e.Message}");
            _s = new GuildState();
        }
    }

    #endregion

    /// <summary>A small seeded random sequence (same seed, same rolls on every machine).</summary>
    struct Roll(ulong seed)
    {
        ulong _state = seed;

        public ulong Next() => Rng.Mix(_state += 0x9E3779B97F4A7C15UL);

        public double Double() => (Next() >> 11) * (1.0 / (1UL << 53));

        public int Int(int min, int max) => max <= min ? min : min + (int)(Next() % (ulong)(max - min + 1));

        public bool Chance(double p) => Double() < p;
    }
}
