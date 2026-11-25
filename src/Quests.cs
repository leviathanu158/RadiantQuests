// Radiant Quests for Meitou. GPLv3 or later.
// The guild's state: your rep, your quests and the day's boards. All of it is saved with the game (GuildSystem: IModSaveData) and hashed
// (IStateHashed), and changed only inside the world's serial steps, so every machine plays the same.
using Meitou.Simulation;

namespace RadiantQuests;

public static class Ranks
{
    /// <summary>The seven ranks, easiest first: E (very easy) up to SS (the deadliest fights, one-of-a-kind finds).</summary>
    public static readonly string[] Names = ["E", "D", "C", "B", "A", "S", "SS"];

    /// <summary>The highest rank's index.</summary>
    public const int Top = 6;

    public static string Name(int rank) => Names[Math.Clamp(rank, 0, Top)];

    /// <summary>The share of cats and rep a quest <paramref name="below"/> ranks under yours still pays.</summary>
    public static (double Cats, double Rep) BelowRankPay(int below) => below switch
    {
        <= 0 => (1, 1),
        1 => (0.9, 0.9),
        2 => (0.6, 0.5),
        3 => (0.35, 0),
        _ => (0.15, 0),
    };

    /// <summary>
    /// The cats a quest of <paramref name="rank"/> pays, from its very easiest to its very hardest. The ranges never overlap (his rule,
    /// 2026-11-23): the easiest D always pays more than the hardest E.
    /// </summary>
    public static (int Min, int Max) Cats(int rank) => rank switch
    {
        0 => (250, 600),
        1 => (650, 1800),
        2 => (1900, 4800),
        3 => (5000, 12000),
        4 => (12500, 24000),
        5 => (25000, 60000),
        _ => (62000, 140000),
    };

    /// <summary>The guild rep a quest of <paramref name="rank"/> pays, before the random spread.</summary>
    public static int Rep(int rank) => rank switch { 0 => 50, 1 => 100, 2 => 200, 3 => 350, 4 => 550, 5 => 800, _ => 1200 };

    /// <summary>
    /// The real relation a Faction quest adds when handed in (his rule, 2026-11-23: no easy way to 100). E and D: none, faction points only.
    /// C 1-2, B 2-3, A 3-4, S 4-5, SS 5-6: the higher number for a quest in the harder half of its rank (<paramref name="within"/> from
    /// <see cref="Difficulty.Within"/>). Climbing a faction to rank A at its own rank's quests comes to roughly +50 relations.
    /// </summary>
    public static int Relation(int rank, double within) => rank < 2 ? 0 : rank - 1 + (within >= 0.5 ? 1 : 0);

    /// <summary>The relation a failed Faction quest of <paramref name="rank"/> costs (none below B).</summary>
    public static int FailRelation(int rank) => rank switch { <= 2 => 0, 3 => -1, 4 => -2, 5 => -3, _ => -4 };
}

/// <summary>A quest on a board, not taken yet.</summary>
public sealed class Offer
{
    public int Slot;
    public string Kind = "";
    public string TargetId = "";
    public string TargetName = "";
    public int Rank;
    public int Need;
    public long Cats;
    public int Rep;
    public bool Taken;
    /// <summary>A "wipe out this group" quest: one real group (found by <see cref="PlatoonKey"/> when taken).</summary>
    public bool Group;
    public int GroupSize;
    public ulong PlatoonKey;
    /// <summary>The target is not hostile to you yet: the quest makes it so (labelled, and pays more).</summary>
    public bool StartsWar;
    /// <summary>The fight score behind its rank (for the relation a Faction quest gives); not saved.</summary>
    public double Score;
    /// <summary>The relation it gives when handed in (Faction tab).</summary>
    public int Relation;
    /// <summary>Its targets are brought in from outside the region (a rare out-of-region quest).</summary>
    public bool Stray;
    /// <summary>Where its targets are when the board is made (world X, Z), for the area danger; not set for quests that bring theirs in.</summary>
    public bool HasAt;
    public float AtX, AtZ;
    /// <summary>The place is dangerous: the rank was raised for what else lives there.</summary>
    public bool Dangerous;
    /// <summary>The mean age of its animals (1 adult, or not animals); counts in the difficulty only.</summary>
    public float Age = 1;
    /// <summary>A few pack animals wandered off from their pack (E and D): "Hunt 3 Goat (strays)".</summary>
    public bool Strays;
    /// <summary>Item quests: the town (site) the goods go to (courier), or -1.</summary>
    public int DestSite = -1;
    /// <summary>Recover quests: who carries the item (a faction id).</summary>
    public string Carrier = "";
    /// <summary>Gear quests: the least worth (cats, the game's own price) an item must have (0: any).</summary>
    public int MinWorth;
    /// <summary>Its time limit in game hours (0: the usual; below 0: none).</summary>
    public double Hours;

    public void SetAt(System.Numerics.Vector2 at)
    {
        HasAt = true;
        AtX = at.X;
        AtZ = at.Y;
    }

    public string Describe() => Quest.Describe(Kind, Need, TargetName, Group, GroupSize, StartsWar, Dangerous, Age, Strays, MinWorth);
}

/// <summary>One town's board for one day.</summary>
public sealed class Board
{
    public int Site;
    public long Day;
    public string GiverFaction = "";
    /// <summary>How often the untaken offers were replaced (it changes the seed).</summary>
    public int Rerolls;
    /// <summary>The General tab's quests.</summary>
    public List<Offer> Offers = [];
    /// <summary>The Faction tab's quests: the giver's enemies now (its wars and the outlaws).</summary>
    public List<Offer> FactionOffers = [];
}

/// <summary>A quest you took.</summary>
public sealed class Quest
{
    public int Id;
    public string Kind = "";
    public string TargetId = "";
    public string TargetName = "";
    public int Rank;
    public int Need;
    public int Have;
    public long Cats;
    public int Rep;
    /// <summary>The faction whose clerk gave it (General quests are handed in to any guild faction).</summary>
    public string GiverFaction = "";
    public int Site;
    public bool General = true;
    /// <summary>Game hours when it runs out (double.MaxValue: never).</summary>
    public double Deadline;
    /// <summary>A "wipe out this group" quest: its group is the platoon tagged <see cref="Tag"/>; done at <see cref="Need"/> of
    /// <see cref="GroupSize"/> defeated.</summary>
    public bool Group;
    public int GroupSize;
    /// <summary>Your side landed a blow on the group (else, if others wipe it out, the quest is called off).</summary>
    public bool Hit;
    public bool StartsWar;
    /// <summary>The relation with the giver it adds when handed in (Faction quests); -1 for a quest from before 0.7.3 (the rank's low end).</summary>
    public int Relation = -1;
    /// <summary>Its targets are brought in from outside the region (spawns use any of their natural kinds).</summary>
    public bool Stray;
    /// <summary>Ranked up for a dangerous area.</summary>
    public bool Dangerous;
    /// <summary>A camp quest: the last game hour one of the camp's people still standing was within reach of your squad there.</summary>
    public double LastContact = -1;
    /// <summary>A camp quest's roster: the camp's people who must fall (lasting ids). Taken when you first come near; grows when more of the
    /// camp's own people come home.</summary>
    public List<ulong> Roster = [];
    public bool Rostered;
    /// <summary>Groups given up on as out of reach (platoon keys): no longer marked or counted as standing.</summary>
    public List<ulong> Skipped = [];
    /// <summary>Since when (game hours) one of yours has been near its targets without reaching any (-1: not now), and the count then.</summary>
    public double NearSince = -1;
    public int NearHave;
    /// <summary>The mod brought targets in for it.</summary>
    public bool Spawned;
    /// <summary>The count when it was taken (the share paid is worked out against it).</summary>
    public int FullNeed;
    /// <summary>Its targets someone else beat (knocked out or killed with no blow of yours: seen, not guessed), by lasting id.</summary>
    public List<ulong> LostToOthers = [];
    /// <summary>The people already counted (lasting ids): each counts once, whatever happens to them after.</summary>
    public List<ulong> Defeated = [];
    /// <summary>The fight score behind its rank (Easy / Medium / Hard within the rank); 0 for a quest from before 0.8.0.</summary>
    public double Score;
    /// <summary>The mean age of its animals when taken (1 adult or not animals).</summary>
    public float Age = 1;
    /// <summary>Brought-in groups whose animals' ages were already checked against the rank (platoon keys).</summary>
    public List<ulong> Aged = [];
    /// <summary>Spawn groups (template ids) that came out broken for this quest: its replacement uses another where there is one.</summary>
    public List<string> Broken = [];
    /// <summary>How many of its brought-in groups came out broken and were replaced (at most <see cref="MostRetries"/>).</summary>
    public int Retries;
    /// <summary>How many groups were brought in for it so far (each new one is placed with a new roll).</summary>
    public int Spawns;
    /// <summary>Item quests: the town (site) the goods go to (courier), or -1.</summary>
    public int DestSite = -1;
    /// <summary>Recover quests: who carries the item (a faction id).</summary>
    public string Carrier = "";
    /// <summary>Item quests: one of yours has held the quest's own item (a parcel, a recovered item).</summary>
    public bool ItemHeld;
    /// <summary>Item quests: the quest's own item has been placed in the world (given to its carrier).</summary>
    public bool ItemPlaced;
    /// <summary>Item quests: one of yours sold an item of the quest's record; the next check looks whether it was the quest's own.</summary>
    public bool SaleSeen;
    /// <summary>
    /// What failing or abandoning costs, times the usual (his rule, 2026-11-24: a harsh penalty for a rare item picked up and then abandoned
    /// or sold). 1 normally.
    /// </summary>
    public double PenaltyMult = 1;
    /// <summary>Gear quests: the least worth an item must have (0: any).</summary>
    public int MinWorth;
    /// <summary>Courier: the straight distance from the clerk to the destination when taken, and the best progress made toward it (units).</summary>
    public float StartDist, Progress;
    /// <summary>Courier: the progress at which the next ambush roll comes, and how many ambushes came so far.</summary>
    public float NextRoll = ItemQuestKinds.RollEvery;
    public int Ambushes;
    /// <summary>Courier: the ambush group chasing now (its platoon key; 0 none), since when (game hours), and the tick it was last given orders.</summary>
    public ulong AmbushKey;
    public double AmbushSince;
    public long AmbushOrdered;
    /// <summary>Courier: where the ambush last knew the courier to be (a sneaking courier unseen is searched for there).</summary>
    public float SeenX, SeenZ;
    /// <summary>Courier: when the ambush stole the parcel (game hours; below 0: not stolen). Won back within the hour, or it fails.</summary>
    public double StolenSince = -1;
    /// <summary>Gear quests: the worth of the best matching item your characters carry (the journal's "your best").</summary>
    public int Best;
    /// <summary>
    /// Item quests: how many units were already handed in (his rule, 2026-11-25: a partial hand-in counts; heavy or big goods can come in
    /// parts). Once any are in, the quest can't fail: abandoned or out of time it pays the share delivered.
    /// </summary>
    public int Delivered;
    /// <summary>What you have heard of the quest, newest last (day-stamped lines; at most 6): the journal's "heard" log.</summary>
    public List<string> RumourLog = [];
    /// <summary>Fight quests: when one of yours last saw a target (game hours; below 0: never). The place is SeenX/SeenZ.</summary>
    public double SightHours = -1;
    /// <summary>When townsfolk last told something of this quest (game hours; one answer per quest per few hours).</summary>
    public double TownAskHours = -1;
    /// <summary>Test channel only: the next ambush roll hits, wherever the courier is.</summary>
    public bool ForceAmbush;
    /// <summary>Item quests: what the clerk told you of where to get it (kept, so it can be read again), and how good it is (0 free, 1, 2).</summary>
    public string Rumour = "";
    public int RumourLevel;
    /// <summary>Its markers are hidden on the map (the journal's Hide).</summary>
    public bool MarkersHidden;
    /// <summary>The place the best rumour names (marked on the map once bought, his rule 2026-11-24), if any.</summary>
    public bool HasHint;
    public float HintX, HintZ;

    /// <summary>The most broken spawns a quest replaces; after that the usual fallbacks (count drops, or called off with no penalty).</summary>
    public const int MostRetries = 5;

    /// <summary>The platoon tag of this quest's group or spawned group.</summary>
    public string Tag => $"radiantquests:{Id}";
    /// <summary>Where it was taken (the clerk, world X and Z): target groups count within the target range of here.</summary>
    public float AnchorX, AnchorZ;
    /// <summary>The tick it was taken: only blows after it count.</summary>
    public long AcceptedTick;
    /// <summary>How often more targets were spawned while it ran, and when last (game hours).</summary>
    public int Refills;
    public double LastRefill;
    /// <summary>A Clear the lair quest's site (index), -1 for other quests.</summary>
    public int Lair = -1;

    public bool Done => Have >= Need;

    /// <summary>The cats it pays now (its pay share applied, rounded to tens): what the window shows and the hand-in pays.</summary>
    public long CatsNow => (long)Math.Round(Cats * PayShare / 10) * 10;

    /// <summary>The guild rep (and faction points) it gives now, its pay share applied.</summary>
    public int RepNow => (int)Math.Round(Rep * PayShare);

    /// <summary>
    /// The share of the reward a finished quest pays (his rule, 2026-11-23): targets lost to a bug, or stuck, are forgiven (full pay); those
    /// someone else beat are not paid for, so leading a group into the guards earns nothing. 1 when none were.
    /// </summary>
    public double PayShare
    {
        get
        {
            if (FullNeed <= 0) return 1;
            int lost = Math.Min(LostToOthers.Count(l => !Defeated.Contains(l)), Math.Max(0, FullNeed - Have));
            return Math.Clamp(1 - (double)lost / FullNeed, 0, 1);
        }
    }

    public string Describe() => Describe(Kind, Need, TargetName, Group, GroupSize, StartsWar, Dangerous, Age, Strays, MinWorth);

    /// <summary>For the windows: a worth target also says the best matching item you carry.</summary>
    public string DescribeWithBest() => Describe() + (Delivered > 0 ? $" ({Delivered} delivered)" : "")
        + (MinWorth > 0 && !Done ? Best > 0 ? $" (your best: {Best:N0})" : " (you carry none)" : "");

    /// <summary>A few pack animals that wandered off (see <see cref="Offer.Strays"/>).</summary>
    public bool Strays;

    public static string Describe(string kind, int need, string target, bool group = false, int groupSize = 0, bool startsWar = false,
        bool dangerous = false, float age = 1, bool strays = false, int minWorth = 0)
    {
        // The count is the whole group (his rule, 2026-11-23: no margin), so no "(group of N)" / "(N needed)" any more.
        string of = strays ? " (strays)" : "";
        string text = kind switch
        {
            _ when group || kind == GroupQuest.KindId => $"Wipe out a group of {groupSize} {target}",
            LairQuest.KindId => $"Clear {target}: defeat everyone at the camp (about {groupSize})",
            // No age words (his call, 2026-11-23: herds are mixed); the ages count in the difficulty and pay only.
            // Item quests (ItemQuests.cs): the target is the item (courier: the destination town).
            ItemQuestKinds.Gear => need == 1 ? $"Bring {GearOne(target)} worth about {minWorth:N0} cats"
                : $"Bring {need} {GearMany(target)} worth about {minWorth:N0} cats each",
            ItemQuestKinds.Supply => $"Bring {need} {target}",
            ItemQuestKinds.Find => need == 1 ? $"Find {Article(target)} {target}" : $"Find {need} {target}",
            ItemQuestKinds.Courier => $"Deliver a guild parcel to {target}",
            ItemQuestKinds.Recover => $"Recover the stolen {target}",
            Catalog.Hunt => $"Hunt {need} {target}{of}",
            // One person alone (brought in, or a real one wandering): a loner (his rule, 2026-11-23: no set chance, just a roll of one).
            Catalog.Cull when need == 1 && groupSize == 1 => $"Defeat {(("AEIOU".Contains(char.ToUpperInvariant(Loner(target)[0]))) ? "an" : "a")} {Loner(target)} Loner",
            Catalog.Cull => $"Defeat {need} {target}{of}",
            _ => $"{kind} {need} {target}",
        };
        if (dangerous) text += " (dangerous area)";
        return startsWar ? $"{text} - makes {target} hostile to you" : text;
    }

    /// <summary>One piece of a gear group: "a katana", "a pair of light boots", "a piece of heavy body armour".</summary>
    static string GearOne(string g) => g.EndsWith('s') ? $"a pair of {g}" : g.EndsWith("armour") ? $"a piece of {g}" : $"{Article(g)} {g}";

    /// <summary>Several of a gear group: "katanas", "pairs of light boots", "pieces of heavy body armour".</summary>
    static string GearMany(string g) => g.EndsWith('s') ? $"pairs of {g}" : g.EndsWith("armour") ? $"pieces of {g}" : g + "s";

    /// <summary>"a" or "an" for <paramref name="word"/>.</summary>
    static string Article(string word) => word.Length > 0 && "AEIOUaeiou".Contains(word[0]) ? "an" : "a";

    /// <summary>A faction's name as one of its people: "Dust Bandits" -> "Dust Bandit", "The Holy Nation" -> "Holy Nation".</summary>
    public static string LonerName(string faction) => Loner(faction) + " Loner";

    static string Loner(string faction)
    {
        string s = faction.Trim();
        if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        if (s.Length > 3 && s.EndsWith('s') && !s.EndsWith("ss", StringComparison.Ordinal) && !s.EndsWith("us", StringComparison.Ordinal)) s = s[..^1];
        return s;
    }


    public void Hash(ref StateHasher h)
    {
        h.Add(Id);
        h.Add(Rng.StableHash(Kind));
        h.Add(Rng.StableHash(TargetId));
        h.Add(Rank);
        h.Add(Need);
        h.Add(Have);
        h.Add(Cats);
        h.Add(Rep);
        h.Add(Rng.StableHash(GiverFaction));
        h.Add(Site);
        h.Add(General);
        h.Add(Deadline);
        h.Add(AnchorX);
        h.Add(AnchorZ);
        h.Add(Group);
        h.Add(GroupSize);
        h.Add(Hit);
        h.Add(StartsWar);
        h.Add(AcceptedTick);
        h.Add(Refills);
        h.Add(LastRefill);
        h.Add(Lair);
        h.Add(Relation);
        h.Add(Stray);
        h.Add(Dangerous);
        h.Add(LastContact);
        h.Add(Rostered);
        h.Add(NearSince);
        h.Add(NearHave);
        h.Add(Spawned);
        h.Add(FullNeed);
        h.Add(LostToOthers.Count);
        foreach (var l in LostToOthers) h.Add(l);
        h.Add(Skipped.Count);
        foreach (var k in Skipped) h.Add(k);
        h.Add(Roster.Count);
        foreach (var r in Roster) h.Add(r);
        h.Add(Defeated.Count);
        foreach (var d in Defeated) h.Add(d);
        h.Add(Score);
        h.Add(Age);
        h.Add(Aged.Count);
        foreach (var a in Aged) h.Add(a);
        h.Add(Broken.Count);
        foreach (var b in Broken) h.Add(Rng.StableHash(b));
        h.Add(Retries);
        h.Add(Spawns);
        h.Add(DestSite);
        h.Add(Rng.StableHash(Carrier));
        h.Add(ItemHeld);
        h.Add(ItemPlaced);
        h.Add(SaleSeen);
        h.Add(PenaltyMult);
        h.Add(Strays);
        h.Add(MinWorth);
        h.Add(StartDist);
        h.Add(Progress);
        h.Add(NextRoll);
        h.Add(Ambushes);
        h.Add(AmbushKey);
        h.Add(AmbushSince);
        h.Add(AmbushOrdered);
        h.Add(SeenX);
        h.Add(SeenZ);
        h.Add(StolenSince);
        h.Add(Best);
        h.Add(ForceAmbush);
        h.Add(Delivered);
        h.Add(RumourLog.Count);
        foreach (var line in RumourLog) h.Add(Rng.StableHash(line));
        h.Add(SightHours);
        h.Add(TownAskHours);
        h.Add(Rng.StableHash(Rumour));
        h.Add(RumourLevel);
        h.Add(MarkersHidden);
        h.Add(HasHint);
        h.Add(HintX);
        h.Add(HintZ);
    }
}

/// <summary>A brought-in group waiting for its first-spawn check: its template, its quest, when it came (game hours), and whether it was
/// already seen loaded once (it is checked on the next look, after it has settled).</summary>
public sealed class SpawnCheck
{
    public string Template = "";
    public int Quest;
    public double Hours;
    public bool LoadedSeen;
}

/// <summary>Everything the guild keeps.</summary>
public sealed class GuildState
{
    /// <summary>All guild rep ever earned, less penalties: sets your rank.</summary>
    public long Lifetime;
    /// <summary>Guild rep you can spend.</summary>
    public long Balance;
    public int NextId = 1;
    public List<Quest> Quests = [];
    public List<Board> Boards = [];
    /// <summary>Lifetime points with each faction (its Faction board's rank); never spent.</summary>
    public Dictionary<string, long> FactionPoints = new(StringComparer.Ordinal);
    /// <summary>Groups the mod brought in, by platoon key: the game hour of their last defeat (they leave some hours after it).</summary>
    public Dictionary<ulong, double> SpawnDefeats = [];
    /// <summary>
    /// Brought-in groups waiting for their first-spawn check (platoon key): checked once they are loaded (his rule, 2026-11-23: a spawn that
    /// comes out broken is replaced for its quest at once).
    /// </summary>
    public Dictionary<ulong, SpawnCheck> SpawnChecks = [];
    /// <summary>Spawn groups (template ids) that came out broken, and how often in a row: 3 strikes and it is not brought in again in this
    /// save; one good spawn clears them.</summary>
    public Dictionary<string, int> SpawnStrikes = new(StringComparer.Ordinal);

    public long PointsWith(string faction) => FactionPoints.GetValueOrDefault(faction);

    public void Hash(ref StateHasher h)
    {
        h.Add(Lifetime);
        h.Add(Balance);
        h.Add(FactionPoints.Count);
        foreach (var kv in FactionPoints.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            h.Add(Rng.StableHash(kv.Key));
            h.Add(kv.Value);
        }
        h.Add(SpawnDefeats.Count);
        foreach (var kv in SpawnDefeats.OrderBy(kv => kv.Key))
        {
            h.Add(kv.Key);
            h.Add(kv.Value);
        }
        h.Add(SpawnChecks.Count);
        foreach (var kv in SpawnChecks.OrderBy(kv => kv.Key))
        {
            h.Add(kv.Key);
            h.Add(Rng.StableHash(kv.Value.Template));
            h.Add(kv.Value.Quest);
            h.Add(kv.Value.Hours);
            h.Add(kv.Value.LoadedSeen);
        }
        h.Add(SpawnStrikes.Count);
        foreach (var kv in SpawnStrikes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            h.Add(Rng.StableHash(kv.Key));
            h.Add(kv.Value);
        }
        h.Add(NextId);
        h.Add(Quests.Count);
        foreach (var q in Quests) q.Hash(ref h);
        h.Add(Boards.Count);
        foreach (var b in Boards)
        {
            h.Add(b.Site);
            h.Add(b.Day);
            h.Add(Rng.StableHash(b.GiverFaction));
            h.Add(b.Rerolls);
            h.Add(b.Offers.Count);
            h.Add(b.FactionOffers.Count);
            foreach (var o in b.Offers.Concat(b.FactionOffers))
            {
                h.Add(o.Slot);
                h.Add(Rng.StableHash(o.TargetId));
                h.Add(o.Rank);
                h.Add(o.Need);
                h.Add(o.Cats);
                h.Add(o.Rep);
                h.Add(o.Taken);
                h.Add(o.Group);
                h.Add(o.GroupSize);
                h.Add(o.PlatoonKey);
                h.Add(o.StartsWar);
                h.Add(o.Age);
                h.Add(o.Strays);
                h.Add(o.DestSite);
                h.Add(Rng.StableHash(o.Carrier));
                h.Add(o.MinWorth);
                h.Add(o.Hours);
            }
        }
    }
}

// The interface's requests. The window only queues these; GuildSystem.Inputs applies them at the start of a tick, or between ticks while the
// game is paused (PausedCommands), as the game's own windows' commands are.

/// <summary>Every guild command.</summary>
public abstract record GuildCommand : SimCommand;

/// <summary>Opened the guild window at <paramref name="Clerk"/>: make today's board for its town if there is none yet.</summary>
public sealed record OpenBoard(CharacterId Clerk) : GuildCommand;

/// <summary>Take offer <paramref name="Slot"/> of the board of <paramref name="Site"/>.</summary>
public sealed record AcceptOffer(CharacterId Clerk, int Site, int Slot, bool Faction = false) : GuildCommand;

/// <summary>Replace the untaken offers of the board at <paramref name="Clerk"/> (costs a little rep).</summary>
public sealed record RerollBoard(CharacterId Clerk) : GuildCommand;

/// <summary>Hand in a finished quest at <paramref name="Clerk"/>.</summary>
public sealed record HandIn(CharacterId Clerk, int QuestId) : GuildCommand;

/// <summary>Give up a quest.</summary>
public sealed record Abandon(int QuestId) : GuildCommand;

/// <summary>Pay the clerk for a better rumour of where a quest's item is.</summary>
public sealed record BuyRumour(CharacterId Clerk, int QuestId) : GuildCommand;

/// <summary>Open the pick window for handing in a quest's goods (the clerk lists what your characters there carry).</summary>
public sealed record ShowHandIn(CharacterId Clerk, int QuestId) : GuildCommand;

/// <summary>Hand in the goods picked in the pick window (their keys in its list).</summary>
public sealed record HandInPicked(CharacterId Clerk, int QuestId, IReadOnlyList<string> Ids) : GuildCommand;

/// <summary>Ask a townsperson what they have heard of your guild work (free, vague; his rule 2026-11-25).</summary>
public sealed record AskTownsfolk(CharacterId Npc) : GuildCommand;

/// <summary>Hide or show a quest's markers on the map.</summary>
public sealed record ToggleMarkers(int QuestId) : GuildCommand;

/// <summary>Test channel only: count a kill of <paramref name="TargetId"/> for your quests as if one of your characters made it.</summary>
public sealed record TestKill(string TargetId) : GuildCommand;

/// <summary>Test channel only: add guild rep.</summary>
public sealed record TestRep(int Amount) : GuildCommand;

/// <summary>Test channel only: brings every quest's deadline <paramref name="Hours"/> game hours closer, as if that much time had passed.</summary>
public sealed record TestSkip(double Hours) : GuildCommand;

/// <summary>Test channel only: logs each animal of quest <paramref name="QuestId"/>'s groups with its age, and the scores the mod reads.</summary>
public sealed record TestHerd(int QuestId) : GuildCommand;

/// <summary>Test channel only: sets the ages of quest <paramref name="QuestId"/>'s first group's animals, in order (the last age repeats).</summary>
public sealed record TestSetAges(int QuestId, IReadOnlyList<float> Ages) : GuildCommand;

/// <summary>Test channel only: treats quest <paramref name="QuestId"/>'s brought-in group as broken (strike, replacement).</summary>
public sealed record TestBreakSpawn(int QuestId) : GuildCommand;

/// <summary>Test channel only: counts kills for quest <paramref name="QuestId"/> until it is done.</summary>
public sealed record TestFinish(int QuestId) : GuildCommand;

/// <summary>Test channel only: one offer of a kind and rank on the clerk's board.</summary>
public sealed record TestOfferCmd(CharacterId Clerk, string Kind, int Rank, bool Take = false, string Filter = "") : GuildCommand;

/// <summary>Test channel only: a courier quest's next ambush roll comes now and always hits.</summary>
public sealed record TestAmbush(int QuestId) : GuildCommand;

/// <summary>Test channel only: logs a courier quest's parcel, courier and ambush.</summary>
public sealed record TestCourier(int QuestId) : GuildCommand;

/// <summary>Test channel only: gives the nearest of yours at the quest's clerk <paramref name="Count"/> of the quest's item (a group: its first member).</summary>
public sealed record TestGiveItem(int QuestId, int Count) : GuildCommand;
