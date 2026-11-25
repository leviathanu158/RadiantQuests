// Radiant Quests for Meitou. GPLv3 or later.
// The rows on Settings > Mods > Radiant Quests. The page changes them on the interface's thread; GuildSystem copies what the world needs in its
// serial Inputs step each tick, so every machine plays the same (docs/modding.md "Settings").
using Meitou.Modding;

namespace RadiantQuests;

public sealed class GuildSettings
{
    [Setting("Hunt quests", SubGroup = "Features", Hint = "Boards offer quests to hunt animals.")]
    public bool Hunts { get; set; } = true;

    [Setting("Kill quests", SubGroup = "Features", Hint = "Boards offer quests to kill members of bandit-type groups.")]
    public bool Culls { get; set; } = true;

    [Setting("Faction quests", SubGroup = "Features",
        Hint = "The Faction tab: quests against the clerk's faction's enemies right now (its wars and the outlaws). They pay faction points "
            + "(your rank with that faction) and a little real relation, and are handed in to that faction only.")]
    public bool FactionQuests { get; set; } = true;

    [Setting("Group quests", SubGroup = "Features",
        Hint = "\"Wipe out this group\" quests: one real group near the town, marked on the map, ranked by its real size and gear. Done when "
            + "the set share of it has been defeated (knocked out, killed or captured) and your side has fought it.")]
    public bool GroupQuests { get; set; } = true;

    [Setting("Clear the lair quests", SubGroup = "Features",
        Hint = "Quests to clear an existing camp, nest or outpost near the town (never one the mod makes): done when the set share of the people "
            + "or animals living there is defeated, and your side fought them.")]
    public bool LairQuests { get; set; } = true;

    [Setting("Spawn targets when none are near", SubGroup = "Features",
        Hint = "When you take a hunt or kill quest and no group of the target is within the target range, one ordinary group of them is spawned "
            + "in that range (never a unique character). Off: the quest uses only groups already in the world.")]
    public bool SpawnTargets { get; set; } = true;

    [Setting("Map markers", SubGroup = "Features", Hint = "Marks the nearest groups of each quest's target on the world map.")]
    public bool MapMarkers { get; set; } = true;

    // (His rule, 2026-11-25: fight quests are found by rumour; their marker shows once one of yours is close. This shows them always.)
    [Setting("Fight markers always", SubGroup = "Features",
        Hint = "Off (default): hunt and kill targets are found by rumour, and marked only once one of your characters is close. On: always marked (for testing, or if you prefer).")]
    public bool FightMarkersAlways { get; set; }

    [Setting("Guild trials", SubGroup = "Ranks",
        Hint = "On (default): a newcomer joins the guild by an entry trial, and each new rank is won by a trial fight once your rep has earned it. Off: rep alone sets your rank.")]
    public bool Trials { get; set; } = true;

    [Setting("Rep for rank D", Min = 5, Max = 2000, Step = 5, SubGroup = "Ranks",
        // (His defaults: 2026-11-24 two thirds of the earlier ones, his call after play: ~10 quests of a rank for D and C, ~7-10 of the rank below later.)
        Hint = "Lifetime rep needed for rank D (you start at E; about 10 E quests). Each later rank needs about 7 to 10 quests of the rank below. Spending rep at the store never lowers your rank.")]
    public int RankD { get; set; } = 500;

    [Setting("Rep for rank C", Min = 10, Max = 5000, Step = 10, SubGroup = "Ranks")]
    public int RankC { get; set; } = 1500;

    [Setting("Rep for rank B", Min = 20, Max = 10000, Step = 10, SubGroup = "Ranks")]
    public int RankB { get; set; } = 3330;

    [Setting("Rep for rank A", Min = 30, Max = 20000, Step = 10, SubGroup = "Ranks")]
    public int RankA { get; set; } = 6000;

    [Setting("Rep for rank S", Min = 40, Max = 50000, Step = 10, SubGroup = "Ranks")]
    public int RankS { get; set; } = 10000;

    [Setting("Rep for rank SS", Min = 50, Max = 100000, Step = 50, SubGroup = "Ranks")]
    public int RankSS { get; set; } = 16650;

    [Setting("Faction work from guild rank", Min = 0, Max = 6, Step = 1, SubGroup = "Ranks",
        Hint = "A faction gives you its own work (its tab at the clerk) once your guild rank reaches this: 0 E, 1 D, 2 C (default), 3 B... "
            + "Prove yourself to the guild first. Your rank with a faction starts here.")]
    public int FactionFromRank { get; set; } = 2;

    [Setting("Faction rank steps", Min = 0.1, Max = 2, Step = 0.05, SubGroup = "Ranks",
        Hint = "How many points a faction's ranks need compared with the guild's (a faction has fewer towns and less work): 0.5 is half.")]
    public double FactionRankScale { get; set; } = 0.5;

    [Setting("Guild rep from faction work", Min = 0, Max = 1, Step = 0.05, SubGroup = "Rewards",
        Hint = "Faction work pays full points with that faction (and relations) but only this share of guild rep: 0.5 is half. "
            + "General work climbs the guild, faction work the faction.")]
    public double FactionGuildRep { get; set; } = 0.5;

    [Setting("Cats reward", Min = 0.1, Max = 5, Step = 0.05, SubGroup = "Rewards", Hint = "Multiplies the cats every quest pays.")]
    public double CatsMult { get; set; } = 1;

    [Setting("Rep reward", Min = 0.1, Max = 5, Step = 0.05, SubGroup = "Rewards", Hint = "Multiplies the guild rep every quest pays.")]
    public double RepMult { get; set; } = 1;

    [Setting("Kill quest time (hours)", Min = 12, Max = 480, Step = 6, SubGroup = "Time limits",
        Hint = "Game hours to finish a hunt or cull. Each kill gives some time back.")]
    public int KillHours { get; set; } = 72;

    [Setting("Time back per kill (hours)", Min = 0, Max = 72, Step = 1, SubGroup = "Time limits")]
    public int KillRefreshHours { get; set; } = 12;

    [Setting("Fail cost (share of the quest's rep)", Min = 0, Max = 2, Step = 0.05, SubGroup = "Penalties",
        Hint = "Running out of time takes this share of the quest's rep off your lifetime and spendable rep (never below 0).")]
    public double FailCost { get; set; } = 0.5;

    [Setting("Abandon cost (share of the quest's rep)", Min = 0, Max = 2, Step = 0.05, SubGroup = "Penalties")]
    public double AbandonCost { get; set; } = 0.25;

    [Setting("Quest area", Min = 5000, Max = 60000, Step = 1000, SubGroup = "Targets",
        Hint = "How far around a town its quests reach (world units; about 10 per metre): a town only offers work against what lives within "
            + "this distance or naturally spawns in its surrounding regions, and that's where its targets are marked and spawned.")]
    public int TargetRange { get; set; } = 15000;

    [Setting("Board refresh (days)", Min = 1, Max = 7, Step = 1, SubGroup = "Boards",
        Hint = "How long a town's board lasts. Taken quests stay taken: once a rank is cleared, nothing new comes until the board refreshes.")]
    public int RefreshDays { get; set; } = 1;

    [Setting("New quests cost (rep)", Min = 0, Max = 2000, Step = 10, SubGroup = "Boards",
        Hint = "What the clerk charges to replace the quests not taken yet (a little more at higher ranks). Paid from rep you can spend.")]
    public int RerollCost { get; set; } = 50;

    [Setting("Most quests per rank", Min = 1, Max = 12, Step = 1, SubGroup = "Boards",
        Hint = "Targets really around a town come first, each once per rank, filling a rank up to 5 to this many quests. Then sometimes one new kind of enemy is brought in.")]
    public int MostPerRank { get; set; } = 7;

    [Setting("Out-of-region quests", Min = 0, Max = 1, Step = 0.05, SubGroup = "Boards",
        Hint = "The chance that a quest bringing in new enemies picks ones that don't live in the region (a rare sight). 0: only local kinds.")]
    public double StrayChance { get; set; } = 0.15;

    // The board's fight and item mix (his rule, 2026-11-24: variety, nothing locked).
    [Setting("Fights over item work", Min = 0, Max = 10, Step = 0.5, SubGroup = "Boards",
        Hint = "Where real fights are around, how many times more often a board slot is a fight than item work. 2: two fights for each item quest, on average.")]
    public double RealFightWeight { get; set; } = 2;

    [Setting("Empty slots become item work", Min = 0, Max = 1, Step = 0.05, SubGroup = "Boards",
        Hint = "When a slot's roll to bring new enemies in fails, the chance it becomes item work instead of staying empty.")]
    public double ItemFallback { get; set; } = 0.5;

    [Setting("Area danger share", Min = 0, Max = 1, Step = 0.05, SubGroup = "Boards",
        Hint = "How much of the strength of other things hostile to you close to a quest's place (groups and camps) adds to its difficulty. A quest rises at most one rank. 0: off.")]
    public double DangerShare { get; set; } = 0.25;

    [Setting("Camp stragglers (hours)", Min = 0.5, Max = 24, Step = 0.5, SubGroup = "Boards",
        Hint = "Clearing a camp: once most are defeated, if none left come within reach of your squad at the camp for this many game hours, it counts as cleared.")]
    public double StragglerHours { get; set; } = 2;

    [Setting("Out of reach after (hours)", Min = 0.25, Max = 12, Step = 0.25, SubGroup = "Boards",
        Hint = "Targets you stay near for this long without ever reaching them (in water, up a cliff, stuck) are given up: others are found or brought in, or the quest ends with no penalty.")]
    public double StuckHours { get; set; } = 1;

    [Setting("Defeated spawns leave after (hours)", Min = 0.5, Max = 24, Step = 0.5, SubGroup = "Boards",
        Hint = "Groups a quest brought in are removed this long after their last defeat, once none of your characters is near (never in front of you).")]
    public double DefeatedHours { get; set; } = 3.5;

    [Setting("Pay less below your rank", SubGroup = "Rewards",
        Hint = "Off: every quest pays in full whatever your rank. On: quests below your rank pay less (and no rep from three ranks down).")]
    public bool ReduceBelowRank { get; set; }

    [Setting("Furthest spawn", Min = 10000, Max = 200000, Step = 5000, SubGroup = "Targets",
        Hint = "How far from the town a quest may spawn its targets (world units; about 10 per metre). Groups only spawn in regions where they "
            + "normally live, which may be further than the target range, but never further than this.")]
    public int SpawnLimit { get; set; } = 30000;

    [Setting("Extra pay when a quest starts a war", Min = 0, Max = 2, Step = 0.05, SubGroup = "Rewards",
        Hint = "A quest against a faction that isn't hostile to you yet (it will be) pays this much more cats: 0.5 is +50%.")]
    public double WarBonus { get; set; } = 0.5;

    [Setting("Item quests", Hint = "Supply, find, courier and recover quests (the item framework). Off: only the fighting quests.")]
    public bool ItemQuests { get; set; } = true;

    [Setting("Quest tracker", Hint = "A small list of your guild quests on screen. Drag its :: grip to move it, its corner to resize it; A- / A+ change the text size.")]
    public bool Tracker { get; set; } = true;

    [Setting("Window backgrounds", Hint = "Off: the tracker, guild window and journal have no dark background behind their text.")]
    public bool WindowBackgrounds { get; set; } = true;

    [Setting("Debug log", Hint = "Writes extra lines about quests and kills to log.txt. Leave off for play: it costs a little performance.")]
    public bool Debug { get; set; }

    /// <summary>The settings the world reads, copied in a serial step (one value per tick on every machine).</summary>
    public SettingsCopy Copy() => new(RankD, RankC, RankB, RankA, RankS, RankSS, CatsMult, RepMult, KillHours, KillRefreshHours, FailCost, AbandonCost, TargetRange, Hunts, Culls, SpawnTargets, FactionQuests,
        GroupQuests, WarBonus, SpawnLimit, RefreshDays, RerollCost, ReduceBelowRank, LairQuests, MostPerRank, StrayChance, StragglerHours, DangerShare, StuckHours, DefeatedHours,
        Math.Clamp(FactionFromRank, 0, 6), FactionRankScale, FactionGuildRep, ItemQuests);
}

/// <summary>The settings as the world saw them this tick.</summary>
public sealed record SettingsCopy(int RankD, int RankC, int RankB, int RankA, int RankS, int RankSS, double CatsMult, double RepMult, int KillHours,
    int KillRefreshHours, double FailCost, double AbandonCost, int TargetRange, bool Hunts, bool Culls,
    bool SpawnTargets, bool FactionQuests, bool GroupQuests, double WarBonus, int SpawnLimit,
    int RefreshDays, int RerollCost, bool ReduceBelowRank, bool LairQuests, int MostPerRank = 7, double StrayChance = 0.15,
    double StragglerHours = 2, double DangerShare = 0.25, double StuckHours = 1, double DefeatedHours = 3.5, int FactionFromRank = 2,
    double FactionRankScale = 0.5, double FactionGuildRep = 0.5, bool ItemQuests = true)
{
    public static SettingsCopy Defaults { get; } = new GuildSettings().Copy();

    /// <summary>
    /// Your rank with a faction (his rules, 2026-11-24): it starts at the rank faction work opens at (C by default) and climbs with that
    /// faction's points, each step the guild's step from there times <see cref="FactionRankScale"/> (a faction has fewer towns).
    /// </summary>
    public int FactionRankOf(long points)
    {
        int rank = FactionFromRank;
        while (rank < 6 && FactionNextAt(rank) is var next && next >= 0 && points >= next) rank++;
        return rank;
    }

    /// <summary>The faction points rank <paramref name="rank"/>'s next step needs, or -1 at the top.</summary>
    public long FactionNextAt(int rank)
    {
        if (rank >= 6) return -1;
        long baseAt = FactionFromRank <= 0 ? 0 : NextRankAt(FactionFromRank - 1);
        return (long)Math.Round((NextRankAt(Math.Max(rank, FactionFromRank)) - baseAt) * Math.Max(0.01, FactionRankScale));
    }

    /// <summary>The rank (0 D .. 4 S) for <paramref name="lifetime"/> rep.</summary>
    public int RankOf(long lifetime) => lifetime >= RankSS ? 6 : lifetime >= RankS ? 5 : lifetime >= RankA ? 4 : lifetime >= RankB ? 3
        : lifetime >= RankC ? 2 : lifetime >= RankD ? 1 : 0;

    /// <summary>The lifetime rep the next rank needs, or -1 at S.</summary>
    public long NextRankAt(int rank) => rank switch { 0 => RankD, 1 => RankC, 2 => RankB, 3 => RankA, 4 => RankS, 5 => RankSS, _ => -1 };
}
