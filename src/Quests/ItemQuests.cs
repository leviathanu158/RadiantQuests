// Radiant Quests for Meitou. GPLv3 or later.
// The item quest types (his design, 2026-11-24), built on the item framework (ItemCatalog, WorldAccess's item layer, GuildSystem.Items):
//   - SUPPLY  "Bring 15 Hemp": trade goods or animal parts the town's own shops don't sell (legal here). Any matching goods count, bought,
//     looted or made, stolen ones too unless stolen from the faction you hand them to (it knows its own). Marked: the nearest towns selling it.
//   - GEAR    "Bring a katana worth about 2,400 cats": a weapon kind (skill category) or an armour class and slot, judged by the game's own price
//     of the item (grade and maker included), with a 15% grace. Rank and pay come from the worth ASKED, never from what is handed in; a better
//     item earns a small tip. Only asked when several such items are sold around here. B and up: long or no time limits.
//   - FIND    "Find an Ancient Science Book": rare items (never sold, only found), B and up, scored by worth x rarity. No time limit at S and up.
//   - COURIER "Deliver a guild parcel to Stack": the clerk hands one of yours the Guild Parcel (the mod's own item, tagged for the quest).
//     Fails only if its carrier dies or time runs out, or if an ambush steals it and gets away (see CourierQuest). Pay from distance + danger.
//   - RECOVER "Recover the stolen Ancient Science Book": the item lies in a hostile camp, nest or ruin near the town, marked from the start;
//     any approach works. Picked up and then abandoned or sold: a harsh penalty.
// Pay: like every other quest (his rule): the rank's pay by difficulty score. Supply sizes the goods from that pay.
using System.Numerics;
using Meitou.Simulation;

namespace RadiantQuests;

/// <summary>The item quests' kind ids and their shared rules.</summary>
public static class ItemQuestKinds
{
    public const string Supply = "supply", Find = "find", Courier = "courier", Recover = "recover", Gear = "gear";

    /// <summary>The penalty multiplier once a recovered item was in your hands (abandoning or selling it then costs this many times more).</summary>
    public const double HarshPenalty = 3;

    /// <summary>Worth targets: an item counts from this share of the worth asked (his 15% grace: the window says "about").</summary>
    public const double WorthGrace = 0.85;

    /// <summary>The tip for handing in more worth than asked: this share of the extra, at most <see cref="TipCap"/> of the quest's pay.</summary>
    public const double TipShare = 0.1, TipCap = 0.25;

    /// <summary>How far around the clerk (units) shops count as "sold around here" for a gear quest.</summary>
    public const float ShopReach = 50000;

    // Courier ambushes (his rules v3, 2026-11-24).
    /// <summary>A roll every this much progress toward the destination (the first at this much: none in the first 3000).</summary>
    public const float RollEvery = 4000;
    /// <summary>No rolls this near the destination.</summary>
    public const float RollEnd = 6000;
    /// <summary>The chance of an ambush per roll, by the run's rank (E to SS).</summary>
    static readonly double[] s_ambushChance = [0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8];
    /// <summary>An ambush stops chasing once the courier is this far further along the way than it, or after this many game hours.</summary>
    public const float LeftBehind = 4000;
    public const double ChaseHours = 2;
    /// <summary>A stolen parcel can be won back within this many game hours while one of yours stays within this reach of it.</summary>
    public const double WinBackHours = 1;
    public const float WinBackReach = 5000;

    public static double AmbushChance(int rank) => s_ambushChance[Math.Clamp(rank, 0, s_ambushChance.Length - 1)];

    /// <summary>At most 2 ambushes a run, 3 at S and SS.</summary>
    public static int MostAmbushes(int rank) => rank >= 5 ? 3 : 2;

    /// <summary>An ambush is one rank below the run (two below at S and SS).</summary>
    public static int AmbushRank(int rank) => Math.Max(0, rank - (rank >= 5 ? 2 : 1));

    /// <summary>How many rolls a run of <paramref name="distance"/> gets.</summary>
    public static int Rolls(float distance) => distance <= RollEnd + RollEvery ? 0 : (int)((distance - RollEnd) / RollEvery);

    /// <summary>Rank <paramref name="rank"/>'s score band (low, high).</summary>
    public static (double Lo, double Hi) Band(int rank) =>
        (rank == 0 ? Difficulty.Lines[0] / 8 : Difficulty.Lines[Math.Min(rank, Difficulty.Lines.Length) - 1],
         rank < Difficulty.Lines.Length ? Difficulty.Lines[rank] : Difficulty.Lines[^1] * 4);

    /// <summary>
    /// The strongest ambush a courier run of <paramref name="score"/> (rank <paramref name="rank"/>) may meet (his rule, 2026-11-24): two
    /// difficulty bands (fifths of a rank) below the run. A Hard C run meets at most an Easy C fight; a Very Easy C run at most a Hard D.
    /// </summary>
    public static double AmbushCap(double score, int rank)
    {
        double p = rank + Difficulty.Within(score, rank) - 0.4;
        if (p <= 0) return Band(0).Lo;
        int r = (int)Math.Floor(p);
        var (lo, hi) = Band(r);
        return Math.Exp(Math.Log(lo) + (p - r) * (Math.Log(hi) - Math.Log(lo)));
    }

    /// <summary>The straight distances (units) a courier run of each rank may cover: short runs at E, long ones from B up (any town in it).</summary>
    public static (float Lo, float Hi) CourierReach(int rank) => rank switch
    {
        0 => (3000, 30000), 1 => (5000, 50000), 2 => (10000, 80000), 3 => (15000, 120000), 4 => (20000, 170000), _ => (30000, 400000),
    };

    /// <summary>A random score inside rank <paramref name="rank"/>'s band (log scale), so quests spread from Very Easy to Very Hard.</summary>
    public static double ScoreIn(int rank, Dice dice)
    {
        var (lo, hi) = Band(rank);
        return Math.Exp(Math.Log(lo) + dice.Double() * (Math.Log(hi) - Math.Log(lo))) * 0.999;
    }

    /// <summary>
    /// What item work pays over the goods' real price (his rule, 2026-11-24): 1.3 to 1.6 times, so buying and handing in always pays a little
    /// and finding or looting it yourself pays a lot.
    /// </summary>
    public static double PayOver(Dice dice) => 1.3 + 0.3 * dice.Double();

    /// <summary>The time limit of gear and find quests (his rule: B and up long or none): 0 the usual, 240 game hours at B and A, none above.</summary>
    public static double HoursFor(int rank) => rank >= 5 ? -1 : rank >= 3 ? 240 : 0;
}

/// <summary>Shared by Supply, Gear and Find: bring a number of an item (or of a group, by worth) to a clerk.</summary>
public abstract class BringItemsQuest : IQuestType
{
    public abstract string Kind { get; }

    protected static ItemCatalog Items => RadiantQuestsMod.Items;

    public virtual bool Offered(SettingsCopy set, Tab tab) => set.ItemQuests;

    public abstract IEnumerable<Candidate> Candidates(BoardContext ctx, int rank);

    public bool Accept(World world, GuildSystem guild, Quest q, Offer offer, Vector2 at) => true;

    public bool Counts(Quest q, PersonFacts person, string race, string familyHead) => false;

    /// <summary>What counts: the item (or group), worth at least the asked worth less the grace, not stolen from the quest's own faction.</summary>
    protected static WorldAccess.ItemNeed Need(GuildSystem guild, Quest q) => new(Items.MembersOf(q.TargetId),
        q.MinWorth > 0 ? (int)Math.Floor(q.MinWorth * ItemQuestKinds.WorthGrace) : 0, "", guild.Access.Data.FactionIndex(q.GiverFaction));

    public TrackResult Track(World world, GuildSystem guild, Quest q, List<Vector2> markers)
    {
        var need = Need(guild, q);
        q.Have = Math.Min(q.Need, q.Delivered + guild.Access.CarriedAnywhere(world, need));
        if (q.MinWorth > 0) q.Best = guild.Access.BestCarried(world, need);
        // No markers for where to get goods (his rule, 2026-11-24): the clerk's rumour tells (GuildSystem.Rumours); the best rumour's place
        // is marked once bought.
        if (q.HasHint && !q.Done) markers.Add(new Vector2(q.HintX, q.HintZ));
        return TrackResult.Going;
    }

    public bool HandIn(World world, GuildSystem guild, Quest q, ClerkInfo clerk)
    {
        var at = new Vector2(clerk.Position.X, clerk.Position.Z);
        // The pick window's choice when there is one, else the least worth first (never what is worn).
        int still = Math.Max(1, q.Need - q.Delivered);
        var worths = guild.Picked is { } picked ? guild.Access.TakePicked(world, picked, still)
            : guild.Access.TakeCarried(world, at, GuildSystem.ClerkReachUnits, Need(guild, q), still);
        if (worths.Count == 0)
        {
            RadiantQuestsMod.Toast($"Bring it to the clerk: {q.Describe()} (worn gear must be taken off first; goods stolen from {clerk.FactionName} don't count).");
            return false;
        }
        // Over-delivering (his rule): a tip of a tenth of the extra worth, at most a quarter of the pay. Never from what was asked.
        if (q.MinWorth > 0)
        {
            long extra = worths.Sum(w => (long)w) - (long)q.MinWorth * worths.Count;
            long tip = (long)Math.Round(Math.Min(extra * ItemQuestKinds.TipShare, q.CatsNow * ItemQuestKinds.TipCap) / 10) * 10;
            if (tip >= 10)
            {
                guild.Access.Pay(world, tip);
                RadiantQuestsMod.Toast($"Fine work, better than asked: a tip of {tip} cats.");
            }
        }
        return true;
    }

    public void End(World world, GuildSystem guild, Quest q) { }

}

/// <summary>"Bring 15 Hemp": trade goods or animal parts the town lacks (legal here).</summary>
public sealed class SupplyQuest : BringItemsQuest
{
    public override string Kind => ItemQuestKinds.Supply;

    /// <summary>The most units one quest asks for, and the most kilograms (so a squad can carry it).</summary>
    const int MostUnits = 60, MostLoose = 12;
    const float MostWeight = 100;

    /// <summary>
    /// The themes (his rule: variety like the kill quests): trade goods (goods, food, medicine, drugs, raw meat that is sold somewhere) and
    /// animal parts (what animals' bodies give, from the data: modded animals' parts join by themselves).
    /// </summary>
    static bool Suits(ItemFacts i) => (i.AnimalPart
        || i.Rarity is ItemRarity.Common or ItemRarity.Uncommon && i.Kind is ItemKind.Goods or ItemKind.Food or ItemKind.Medical or ItemKind.Drug or ItemKind.RawMeat);

    public override IEnumerable<Candidate> Candidates(BoardContext ctx, int rank)
    {
        if (Items.Items.Count == 0) yield break;
        var (townId, owner) = ctx.Guild.TownFacts(ctx.Site);
        var soldHere = townId is not null && Items.SoldAtTown.TryGetValue(townId, out var s) ? s : null;
        var pool = Items.Items.Values.Where(i => Suits(i) && i.Value >= 20 && !(soldHere?.Contains(i.Id) ?? false)
            && !Items.IllegalIn(i.Id, townId, owner)).OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        if (pool.Count == 0) yield break;
        // A few offers per rank: a theme, then a random item of it sized to a random difficulty within the rank.
        var parts = pool.Where(i => i.AnimalPart).ToList();
        var goods = pool.Where(i => !i.AnimalPart).ToList();
        for (int tries = 0, made = 0; tries < 12 && made < 3; tries++)
        {
            var theme = parts.Count > 0 && (goods.Count == 0 || ctx.Dice.Chance(0.35)) ? parts : goods;
            var item = theme[ctx.Dice.Int(0, theme.Count - 1)];
            // The pay is the goods' real price times 1.3-1.6 (his rule); the count is sized so that pay lands in the rank.
            double over = ItemQuestKinds.PayOver(ctx.Dice);
            double target = Difficulty.CatsAt(ItemQuestKinds.ScoreIn(rank, ctx.Dice), rank);
            int units = (int)Math.Round(target / (item.Price * over));
            // (Items that don't stack fill a square each: fewer of them.)
            if (units < 1 || units > (item.Stack > 1 ? MostUnits : MostLoose) || units * item.Weight > MostWeight) continue;
            double score = Difficulty.ScoreForCats(units * item.Price * over);
            if (Difficulty.RankOf(score) != rank) continue;
            made++;
            yield return new Candidate(new Offer
            {
                Kind = Kind, TargetId = item.Id, TargetName = item.Name, Rank = rank, Need = units, Score = score,
            }, 1, true);
        }
    }

}

/// <summary>"Bring a katana worth about 2,400 cats": a weapon kind or an armour class and slot, judged by the item's own price.</summary>
public sealed class GearQuest : BringItemsQuest
{
    public override string Kind => ItemQuestKinds.Gear;

    /// <summary>How many items of the group at or above the worth must be sold around here (or found, at B and up) for it to be asked.</summary>
    const int Achievable = 3;

    /// <summary>A kind with fewer items than that is asked for only when local groups wearing them number at least this (extremely common).</summary>
    const int Abundant = 8;

    /// <summary>
    /// His rules (2026-11-24): gear quests from rank C, for gear that local enemies wear or local camps hold (so it really drops around here),
    /// worth what a looted piece can be (at most a third of the way up its grades); the pay is its worth times 1.3-1.6, two pieces when cheap.
    /// </summary>
    public override IEnumerable<Candidate> Candidates(BoardContext ctx, int rank)
    {
        if (rank < 2) yield break;
        var groups = Items.Groups.Values.OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        if (groups.Count == 0) yield break;
        var here = ctx.Survey.At;
        var local = new HashSet<string>(Cat.Culls.SelectMany(t => ctx.Survey.LocalTemplates(Cat, t)), StringComparer.Ordinal);
        bool Drops(ItemFacts f) => f.WornBy.Any(local.Contains)
            || ctx.Guild.SitesHolding(f.Id, here).Any(s => System.Numerics.Vector2.Distance(s.At, here) <= ItemQuestKinds.ShopReach);
        for (int tries = 0, made = 0; tries < 10 && made < 2; tries++)
        {
            var g = groups[ctx.Dice.Int(0, groups.Count - 1)];
            double over = ItemQuestKinds.PayOver(ctx.Dice);
            double target = Difficulty.CatsAt(ItemQuestKinds.ScoreIn(rank, ctx.Dice), rank);
            int count = target / over < 1000 ? 2 : 1;
            int each = (int)Math.Max(50, Math.Round(target / over / count / 50) * 50);
            // Achievable (his rules): several kinds of item that count drop around here, or (a kind with few items, his report: the only "light
            // boots" were Drifter's Boots) one that is extremely common here: worn by many local groups.
            int able = 0, wearers = 0;
            foreach (var id in g.Members.OrderBy(m => m, StringComparer.Ordinal))
            {
                if (!Items.Items.TryGetValue(id, out var f) || f.ValueLo + (f.ValueHi - f.ValueLo) / 3 < each || !Drops(f)) continue;
                able++;
                wearers += f.WornBy.Count(local.Contains);
            }
            if (able < Achievable && wearers < Abundant) continue;
            double score = Difficulty.ScoreForCats(each * count * over);
            if (Difficulty.RankOf(score) != rank) continue;
            made++;
            yield return new Candidate(new Offer
            {
                Kind = Kind, TargetId = g.Key, TargetName = g.Name, Rank = rank, Need = count, MinWorth = each, Score = score,
                Hours = ItemQuestKinds.HoursFor(rank),
            }, 1, true);
        }
    }

    static Catalog Cat => RadiantQuestsMod.Catalog;
}

/// <summary>
/// "Find a Book of Grace": an item that is HARD TO GET (his rules, 2026-11-24): sold in at most 3 towns, by travelling traders only, or never
/// sold, and still obtainable (at least 2 places on the map have it: shops or loot). Ranked by its worth times how scarce it is: the places
/// that have it, a shop counting double (buying is easier than looting). C and up, always one.
/// </summary>
public sealed class FindQuest : BringItemsQuest
{
    public override string Kind => ItemQuestKinds.Find;

    /// <summary>The fewest places (a shop counting double) that must have an item for it to be asked for.</summary>
    const int FewestPlaces = 2;

    static bool Suits(ItemFacts i) => i.Rarity is ItemRarity.Rare or ItemRarity.Uncommon && i.Value > 0 && i.Kind is not (ItemKind.RawMeat or ItemKind.Map);

    /// <summary>
    /// What finding it pays (his rule, 2026-11-24): its real price times 1.3-1.6, plus up to half again for scarcity (2 places: +50%, 8 or
    /// more: +12%). The rank comes from that.
    /// </summary>
    public static double Cats(ItemFacts item, int places, double over) => item.Price * (over + Math.Min(0.5, 1.0 / Math.Max(1, places)));

    /// <summary>How easy it is to get: the places holding it as loot, plus twice the places selling it.</summary>
    public static int Availability(GuildSystem guild, string item) => guild.PlacesHolding(item) + 2 * guild.PlacesSelling(item);

    public override IEnumerable<Candidate> Candidates(BoardContext ctx, int rank)
    {
        if (rank < 2) yield break;
        var pool = new List<(ItemFacts Item, double Score)>();
        double over = ItemQuestKinds.PayOver(ctx.Dice);
        foreach (var i in Items.Items.Values.Where(Suits).OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            int places = Availability(ctx.Guild, i.Id);
            if (places < FewestPlaces) continue;
            double score = Difficulty.ScoreForCats(Cats(i, places, over));
            if (Difficulty.RankOf(score) == rank) pool.Add((i, score));
        }
        for (int n = 0; n < Math.Min(2, pool.Count); n++)
        {
            var (item, score) = pool[ctx.Dice.Int(0, pool.Count - 1)];
            pool.RemoveAll(p => p.Item.Id == item.Id);
            yield return new Candidate(new Offer
            {
                Kind = Kind, TargetId = item.Id, TargetName = item.Name, Rank = rank, Need = 1, Score = score,
                Hours = ItemQuestKinds.HoursFor(rank),
            }, 1, true);
        }
    }
}

/// <summary>
/// "Deliver a guild parcel to Stack": carry the mod's Guild Parcel to another town's clerk. Ambushes (his rules v3, 2026-11-24): a roll every
/// 4000 units of progress toward the destination (none in the first 3000 or the last 6000), at the rank's chance, at most 2 (3 at S and SS), a
/// rank below the run (two at S and SS), spawned ahead out of sight and sent after the courier; a sneaking courier nobody sees is searched for
/// where last seen. It stops chasing once it fights, loses the courier, falls 4000 behind, the courier reaches a town, or 2 game hours pass.
/// If the ambush downs or carries off the courier, they loot the parcel; it can be won back within the hour while one of yours stays near it.
/// </summary>
public sealed class CourierQuest : IQuestType
{
    public string Kind => ItemQuestKinds.Courier;

    public bool Offered(SettingsCopy set, Tab tab) => set.ItemQuests && RadiantQuestsMod.Items.HasParcel;

    /// <summary>Distance (units) to difficulty score: a longer road is harder work (log steps between the points).</summary>
    static readonly (double Distance, double Score)[] s_scale =
    [
        (0, 25), (15000, 200), (30000, 1500), (50000, 4000), (80000, 15000), (120000, 40000), (170000, 140000), (250000, 560000),
    ];

    public static double DistanceScore(double distance)
    {
        for (int i = 1; i < s_scale.Length; i++)
            if (distance <= s_scale[i].Distance)
            {
                var (d0, s0) = s_scale[i - 1];
                var (d1, s1) = s_scale[i];
                double t = (distance - d0) / (d1 - d0);
                return Math.Exp(Math.Log(s0) + t * (Math.Log(s1) - Math.Log(s0)));
            }
        return s_scale[^1].Score;
    }

    /// <summary>
    /// The run's score (his rule: distance + danger): the distance's, plus the ambushes to expect (rolls x chance, capped) times half a
    /// middling fight of the ambush rank.
    /// </summary>
    public static double ScoreAt(double distance)
    {
        double s = DistanceScore(distance);
        int r = Difficulty.RankOf(s);
        double expected = Math.Min(ItemQuestKinds.MostAmbushes(r), ItemQuestKinds.Rolls((float)distance) * ItemQuestKinds.AmbushChance(r));
        var (lo, hi) = ItemQuestKinds.Band(ItemQuestKinds.AmbushRank(r));
        return s + expected * Math.Sqrt(lo * hi) * 0.5;
    }

    /// <summary>
    /// Couriers at every rank (his rule, 2026-11-24): any friendly town within the rank's reach; the score is the distance's or a random one in
    /// the rank's band, whichever is higher (kept in the band). The rank sets the ambush chance, count and strength (AmbushCap).
    /// </summary>
    public IEnumerable<Candidate> Candidates(BoardContext ctx, int rank)
    {
        var here = ctx.Survey.At;
        var (near, far) = ItemQuestKinds.CourierReach(rank);
        var towns = ctx.Guild.FriendlyTowns().Where(t => t.Index != ctx.Site && Vector2.Distance(t.At, here) is var d && d >= near && d <= far)
            .OrderBy(t => t.Index).ToList();
        var (lo, hi) = ItemQuestKinds.Band(rank);
        for (int n = 0; n < Math.Min(2, towns.Count); n++)
        {
            var town = towns[ctx.Dice.Int(0, towns.Count - 1)];
            towns.Remove(town);
            double score = Math.Clamp(Math.Max(DistanceScore(Vector2.Distance(town.At, here)), ItemQuestKinds.ScoreIn(rank, ctx.Dice)), lo, hi * 0.999);
            yield return new Candidate(new Offer
            {
                Kind = Kind, TargetId = ItemCatalog.ParcelRecord, TargetName = town.Name.Trim(), Rank = rank, Need = 1, DestSite = town.Index,
                Score = score,
            }, 1, true);
        }
    }

    public bool Accept(World world, GuildSystem guild, Quest q, Offer offer, Vector2 at)
    {
        var carrier = guild.Access.NearestYours(world, at, GuildSystem.ClerkReachUnits);
        if (guild.Access.Site(q.DestSite) is not { } dest || carrier == CharacterId.None
            || !guild.Access.Give(world, carrier, ItemCatalog.ParcelRecord, 1, q.Tag))
        {
            RadiantQuestsMod.Toast("The clerk has no parcel ready (no one of yours to hand it to). Pick another quest.");
            return false;
        }
        q.ItemPlaced = true;
        q.ItemHeld = true;
        q.StartDist = Vector2.Distance(at, dest.At);
        q.NextRoll = ItemQuestKinds.RollEvery;
        q.AmbushSince = -1;
        RadiantQuestsMod.Toast($"The clerk hands over a sealed guild parcel for {q.TargetName}.");
        return true;
    }

    public bool Counts(Quest q, PersonFacts person, string race, string familyHead) => false;

    public TrackResult Track(World world, GuildSystem guild, Quest q, List<Vector2> markers)
    {
        var dest = guild.Access.Site(q.DestSite);
        if (dest is null)
        {
            RadiantQuestsMod.Toast($"\"{q.Describe()}\": the town is gone. Called off, no penalty.");
            return TrackResult.CallOff;
        }
        var parcel = guild.Access.FindTagged(world, q.Tag);
        if (parcel is not { } p)
        {
            guild.FailWhy = q.StolenSince >= 0 ? "the ambush got away with the parcel" : "the parcel was lost";
            return TrackResult.Failed;
        }
        if (p.Yours && !p.HolderAlive)
        {
            guild.FailWhy = "the courier died";
            return TrackResult.Failed;
        }
        if (q.SaleSeen && !p.Yours)
        {
            q.PenaltyMult = ItemQuestKinds.HarshPenalty;
            guild.FailWhy = "you sold the guild's parcel";
            return TrackResult.Failed;
        }
        q.SaleSeen = false;
        markers.Add(dest.At);
        // Stolen by the ambush (his rule): win it back within the hour while one of yours keeps within reach of it, or it fails.
        if (q.StolenSince >= 0)
        {
            if (p.Yours)
            {
                q.StolenSince = -1;
                RadiantQuestsMod.Toast("You won the guild parcel back.");
            }
            else if (world.GameHours - q.StolenSince > ItemQuestKinds.WinBackHours || !guild.Access.YoursNearPoint(world, p.At, ItemQuestKinds.WinBackReach))
            {
                guild.FailWhy = "the ambush got away with the parcel";
                return TrackResult.Failed;
            }
            else
            {
                markers.Add(p.At);
                q.Have = 0;
                return TrackResult.Going;
            }
        }
        if (!p.Yours) markers.Add(p.At);   // dropped or taken by others: no fail (his rule), it is marked to win back
        q.Have = p.Yours && Vector2.Distance(p.At, dest.At) <= Math.Max(1500, dest.Radius + 500) ? 1 : 0;
        if (p.Yours) Ambush(world, guild, q, p.Holder, p.At, dest.At);
        return TrackResult.Going;
    }

    /// <summary>The ambush part of tracking: progress, the chase, the theft, the rolls. Called while one of yours carries the parcel.</summary>
    static void Ambush(World world, GuildSystem guild, Quest q, CharacterId courier, Vector2 at, Vector2 destAt)
    {
        var access = guild.Access;
        float dist = Vector2.Distance(at, destAt);
        q.Progress = Math.Max(q.Progress, q.StartDist - dist);

        // The theft: this quest's ambush (people) downed or carried off the courier: one of them loots the parcel.
        if (q.AmbushKey != 0 && access.DownOrCarried(world, courier) && guild.GroupByKey(world, q, q.AmbushKey) is { } robbers)
        {
            var thief = access.Standing(world, robbers.Id)
                .Select(m => (Id: m, At: access.At(world, m))).Where(m => m.At is { } a && Vector2.Distance(a, at) <= 600)
                .OrderBy(m => Vector2.Distance(m.At!.Value, at)).Select(m => m.Id).FirstOrDefault();
            if (thief != CharacterId.None && access.MoveTagged(world, q.Tag, thief))
            {
                access.OrderLoot(world, thief, courier);
                q.StolenSince = world.GameHours;
                StopChase(world, guild, q, robbers, "stole the parcel");
                RadiantQuestsMod.Toast("The ambush took the guild parcel! Win it back within the hour, before they get away.");
                return;
            }
        }

        // The chase.
        if (q.AmbushSince >= 0)
        {
            var g = guild.GroupByKey(world, q, q.AmbushKey);
            string? end = g is null || g.Standing == 0 ? "beaten" : null;
            // (Against where the courier is NOW, not the best progress: a courier who turned back is not "ahead" of anyone.)
            if (end is null && Vector2.Distance(g!.At, destAt) - dist >= ItemQuestKinds.LeftBehind) end = "left behind";
            if (end is null && world.GameHours - q.AmbushSince >= ItemQuestKinds.ChaseHours) end = "gave up";
            if (end is null && guild.InTown(at)) end = "the courier reached a town";
            if (end is null)
            {
                bool hidden = access.Hidden(world, courier);
                if (!hidden) (q.SeenX, q.SeenZ) = (at.X, at.Y);
                var seen = new Vector2(q.SeenX, q.SeenZ);
                if (access.Loaded(world, g!.Id))
                {
                    var members = access.Standing(world, g.Id);
                    if (members.Any(m => access.Fighting(world, m) && access.At(world, m) is { } a && Vector2.Distance(a, at) <= 1500))
                        end = "fighting";
                    else if (hidden && members.Any(m => access.At(world, m) is { } a && Vector2.Distance(a, seen) <= 300))
                        end = "lost the courier";
                    else if (world.Tick - q.AmbushOrdered >= 300)
                    {
                        q.AmbushOrdered = world.Tick;
                        // Far off: run to where the courier is (or was last seen); close: attack (Meitou's attack order alone did not
                        // bring them in from 3500 away, test 2026-11-24).
                        var said = new List<string>();
                        foreach (var m in members)
                        {
                            var ma = access.At(world, m) ?? seen;
                            var goal = hidden ? seen : at;
                            said.Add(!hidden && Vector2.Distance(ma, at) <= AttackReach ? "attack " + access.OrderAttack(world, m, courier)
                                : "move " + access.OrderMove(world, m, goal));
                        }
                        RadiantQuestsMod.Detail($"quest #{q.Id}: ambush orders: {string.Join("; ", said)}");
                    }
                }
                else if (world.Tick - q.AmbushOrdered >= 600)
                {
                    q.AmbushOrdered = world.Tick;
                    access.Hold(world, g.Id, true);
                    access.SendAfter(world, g.Id, courier);
                }
            }
            if (end is not null) StopChase(world, guild, q, g, end);
            return;
        }

        // The rolls: one per 4000 of progress, none in the last 6000; an ambush still chasing holds the next roll back.
        // (A roll waits while the courier is in a town: an ambush there would stop at once.)
        if (guild.InTown(at)) return;
        bool forced = q.ForceAmbush;
        q.ForceAmbush = false;
        if (!forced)
        {
            if (q.Ambushes >= ItemQuestKinds.MostAmbushes(q.Rank) || q.Progress < q.NextRoll) return;
            if (q.StartDist - q.NextRoll < ItemQuestKinds.RollEnd || dist < ItemQuestKinds.RollEnd)
            {
                q.NextRoll = float.MaxValue;
                return;
            }
        }
        var dice = new Dice(world.Seed ^ Rng.Mix((ulong)q.Id * 0x9E3779B97F4A7C15UL + (ulong)Math.Min(q.NextRoll, 1e9f)));
        q.NextRoll = forced ? Math.Max(q.NextRoll, q.Progress) + ItemQuestKinds.RollEvery : q.NextRoll + ItemQuestKinds.RollEvery;
        if (!forced && !dice.Chance(ItemQuestKinds.AmbushChance(q.Rank))) return;
        ulong key = guild.SpawnAmbush(world, q, at, destAt, ItemQuestKinds.AmbushCap(q.Score, q.Rank));
        RadiantQuestsMod.Detail($"quest #{q.Id}: ambush roll at progress {q.Progress:0}{(forced ? " (test)" : "")}: {(key == 0 ? "nothing could spawn" : "spawned")}");
        if (key == 0) return;
        q.Ambushes++;
        q.AmbushKey = key;
        q.AmbushSince = world.GameHours;
        q.AmbushOrdered = world.Tick;
        (q.SeenX, q.SeenZ) = (at.X, at.Y);
        if (guild.GroupByKey(world, q, key) is { } fresh)
        {
            access.Hold(world, fresh.Id, true);
            access.SendAfter(world, fresh.Id, courier);
        }
    }

    /// <summary>How near an ambush member must be to be ordered to attack the courier (farther: ordered to run there).</summary>
    const float AttackReach = 1200;

    /// <summary>The ambush stops chasing: the mod lets go of it (it lives on as an ordinary group until the quest ends).</summary>
    static void StopChase(World world, GuildSystem guild, Quest q, GroupInfo? g, string why)
    {
        if (q.AmbushSince < 0 && g is null) return;
        if (g is not null) guild.Access.Hold(world, g.Id, false);
        q.AmbushSince = -1;
        RadiantQuestsMod.Detail($"quest #{q.Id}: ambush {q.Ambushes} stops chasing ({why})");
    }

    public bool HandIn(World world, GuildSystem guild, Quest q, ClerkInfo clerk)
    {
        if (clerk.Site != q.DestSite)
        {
            RadiantQuestsMod.Toast($"This parcel is for the clerk in {q.TargetName}.");
            return false;
        }
        if (guild.Access.TakeCarried(world, new Vector2(clerk.Position.X, clerk.Position.Z), GuildSystem.ClerkReachUnits, ItemCatalog.ParcelRecord, 1, q.Tag))
            return true;
        RadiantQuestsMod.Toast("The one carrying the parcel must hand it over at the clerk.");
        return false;
    }

    public void Sold(World world, GuildSystem guild, Quest q, string record)
    {
        if (record == ItemCatalog.ParcelRecord) q.SaleSeen = true;
    }

    public void End(World world, GuildSystem guild, Quest q)
    {
        if (guild.GroupByKey(world, q, q.AmbushKey) is { } g) guild.Access.Hold(world, g.Id, false);
        guild.Access.RemoveTagged(world, q.Tag);
        guild.RemoveSpawned(world, q);
    }
}

/// <summary>
/// "Recover the stolen Ancient Science Book": the item lies in a hostile camp, nest or ruin near the town (his design, 2026-11-24), marked from
/// the start; take it however you like and bring it to a clerk. Ranked by the camp's people, as Clear the camp is. Picked up and then
/// abandoned or sold: a harsh penalty.
/// </summary>
public sealed class RecoverQuest : IQuestType
{
    public string Kind => ItemQuestKinds.Recover;

    static Catalog Cat => RadiantQuestsMod.Catalog;

    static ItemCatalog Items => RadiantQuestsMod.Items;

    public bool Offered(SettingsCopy set, Tab tab) => set.ItemQuests;

    public IEnumerable<Candidate> Candidates(BoardContext ctx, int rank)
    {
        var items = Items.Items.Values.Where(i => i.Rarity is ItemRarity.Rare or ItemRarity.Uncommon && i.Value >= 100
            && i.Kind is not (ItemKind.RawMeat or ItemKind.Food or ItemKind.Map)).OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        if (items.Count == 0) yield break;
        var set = ctx.Guild.Settings;
        var camps = ctx.Guild.Access.SitesNear(ctx.Survey.At, Math.Max(set.TargetRange, set.SpawnLimit))
            .Where(s => Cat.Lairs.TryGetValue(s.TownId, out var l) && LairQuest.Rank(l) == rank && LairQuest.Allowed(ctx.Guild, l, ctx.Tab, ctx.Giver))
            .OrderBy(s => s.Index).ToList();
        if (camps.Count == 0) yield break;
        var camp = camps[ctx.Dice.Int(0, camps.Count - 1)];
        var item = items[ctx.Dice.Int(0, items.Count - 1)];
        var offer = new Offer
        {
            Kind = Kind, TargetId = item.Id, TargetName = item.Name, Rank = rank, Need = 1, DestSite = camp.Index, Carrier = camp.TownId,
            Score = Cat.Lairs[camp.TownId].Score,
        };
        offer.SetAt(camp.At);
        yield return new Candidate(offer, 1, true);
    }

    public bool Accept(World world, GuildSystem guild, Quest q, Offer offer, Vector2 at)
    {
        if (guild.Access.Site(q.DestSite) is not { } camp || camp.TownId != q.Carrier || !guild.Access.PlaceItem(world, q.TargetId, camp.At, q.Tag))
        {
            RadiantQuestsMod.Toast("That place has changed since the board was put up. Pick another quest.");
            return false;
        }
        q.ItemPlaced = true;
        RadiantQuestsMod.Toast($"The stolen {q.TargetName} is at {camp.Name.Trim()} (marked on your map).");
        return true;
    }

    public bool Counts(Quest q, PersonFacts person, string race, string familyHead) => false;

    public TrackResult Track(World world, GuildSystem guild, Quest q, List<Vector2> markers)
    {
        guild.GuardCamp(world, q);
        var found = guild.Access.FindTagged(world, q.Tag);
        if (found is not { } f)
        {
            if (q.ItemHeld)
            {
                guild.FailWhy = "the item was lost";
                return TrackResult.Failed;
            }
            // Gone before it was ever yours (the ground tidied away, someone took it out of sight): it is put back at the camp.
            if (guild.Access.Site(q.DestSite) is { } camp)
            {
                guild.Access.PlaceItem(world, q.TargetId, camp.At, q.Tag);
                markers.Add(camp.At);
            }
            return TrackResult.Going;
        }
        if (f.Yours)
        {
            // In your hands: from now on abandoning or selling it costs dearly (his rule).
            q.ItemHeld = true;
            q.PenaltyMult = ItemQuestKinds.HarshPenalty;
            q.Have = 1;
            q.SaleSeen = false;
            return TrackResult.Going;
        }
        if (q.SaleSeen && q.ItemHeld)
        {
            guild.FailWhy = "you sold the item";
            return TrackResult.Failed;
        }
        q.SaleSeen = false;
        q.Have = 0;
        markers.Add(f.At);
        return TrackResult.Going;
    }

    public bool HandIn(World world, GuildSystem guild, Quest q, ClerkInfo clerk)
    {
        if (guild.Access.TakeCarried(world, new Vector2(clerk.Position.X, clerk.Position.Z), GuildSystem.ClerkReachUnits, q.TargetId, 1, q.Tag))
            return true;
        RadiantQuestsMod.Toast($"Bring the recovered {q.TargetName} to the clerk.");
        return false;
    }

    public void Sold(World world, GuildSystem guild, Quest q, string record)
    {
        if (record == q.TargetId) q.SaleSeen = true;
    }

    public void End(World world, GuildSystem guild, Quest q)
    {
        guild.Access.RemoveTagged(world, q.Tag);
        guild.RemoveSpawned(world, q);   // guards brought into an empty camp
    }
}
