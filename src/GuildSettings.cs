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

    [Setting("Spawn targets when none are near", SubGroup = "Features",
        Hint = "When you take a hunt or kill quest and no group of the target is within the target range, one ordinary group of them is spawned "
            + "in that range (never a unique character). Off: the quest uses only groups already in the world.")]
    public bool SpawnTargets { get; set; } = true;

    [Setting("Map markers", SubGroup = "Features", Hint = "Marks the nearest groups of each quest's target on the world map.")]
    public bool MapMarkers { get; set; } = true;

    [Setting("Rep for rank D", Min = 5, Max = 2000, Step = 5, SubGroup = "Ranks",
        Hint = "Lifetime rep needed for rank D (you start at E). Spending rep at the store never lowers your rank.")]
    public int RankD { get; set; } = 50;

    [Setting("Rep for rank C", Min = 10, Max = 5000, Step = 10, SubGroup = "Ranks")]
    public int RankC { get; set; } = 150;

    [Setting("Rep for rank B", Min = 20, Max = 10000, Step = 10, SubGroup = "Ranks")]
    public int RankB { get; set; } = 400;

    [Setting("Rep for rank A", Min = 30, Max = 20000, Step = 10, SubGroup = "Ranks")]
    public int RankA { get; set; } = 900;

    [Setting("Rep for rank S", Min = 40, Max = 50000, Step = 10, SubGroup = "Ranks")]
    public int RankS { get; set; } = 1800;

    [Setting("Rep for rank SS", Min = 50, Max = 100000, Step = 50, SubGroup = "Ranks")]
    public int RankSS { get; set; } = 3500;

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

    [Setting("Target range", Min = 5000, Max = 100000, Step = 1000, SubGroup = "Targets",
        Hint = "How far from the town a hunt or kill quest looks for its target groups (world units; about 10 per metre). They are marked on "
            + "the map. With none in range when you take the quest, one ordinary group of them is spawned somewhere in that range.")]
    public int TargetRange { get; set; } = 30000;

    [Setting("Furthest spawn", Min = 10000, Max = 200000, Step = 5000, SubGroup = "Targets",
        Hint = "How far from the town a quest may spawn its targets (world units; about 10 per metre). Groups only spawn in regions where they "
            + "normally live, which may be further than the target range, but never further than this.")]
    public int SpawnLimit { get; set; } = 60000;

    [Setting("Group share to defeat", Min = 0.5, Max = 1, Step = 0.05, SubGroup = "Targets",
        Hint = "How much of a group must be defeated for a group quest: 0.75 means 12 of 15. Below 1, a member you can't find never blocks it.")]
    public double GroupShare { get; set; } = 0.75;

    [Setting("Extra pay when a quest starts a war", Min = 0, Max = 2, Step = 0.05, SubGroup = "Rewards",
        Hint = "A quest against a faction that isn't hostile to you yet (it will be) pays this much more cats: 0.5 is +50%.")]
    public double WarBonus { get; set; } = 0.5;

    [Setting("Quest tracker", Hint = "A small list of your guild quests at the top right of the screen.")]
    public bool Tracker { get; set; } = true;

    [Setting("Debug log", Hint = "Writes extra lines about quests and kills to log.txt. Leave off for play: it costs a little performance.")]
    public bool Debug { get; set; }

    /// <summary>The settings the world reads, copied in a serial step (one value per tick on every machine).</summary>
    public SettingsCopy Copy() => new(RankD, RankC, RankB, RankA, RankS, RankSS, CatsMult, RepMult, KillHours, KillRefreshHours, FailCost, AbandonCost, TargetRange, Hunts, Culls, SpawnTargets, FactionQuests,
        GroupQuests, GroupShare, WarBonus, SpawnLimit);
}

/// <summary>The settings as the world saw them this tick.</summary>
public sealed record SettingsCopy(int RankD, int RankC, int RankB, int RankA, int RankS, int RankSS, double CatsMult, double RepMult, int KillHours,
    int KillRefreshHours, double FailCost, double AbandonCost, int TargetRange, bool Hunts, bool Culls,
    bool SpawnTargets, bool FactionQuests, bool GroupQuests, double GroupShare, double WarBonus, int SpawnLimit)
{
    public static SettingsCopy Defaults { get; } = new GuildSettings().Copy();

    /// <summary>The rank (0 D .. 4 S) for <paramref name="lifetime"/> rep.</summary>
    public int RankOf(long lifetime) => lifetime >= RankSS ? 6 : lifetime >= RankS ? 5 : lifetime >= RankA ? 4 : lifetime >= RankB ? 3
        : lifetime >= RankC ? 2 : lifetime >= RankD ? 1 : 0;

    /// <summary>The lifetime rep the next rank needs, or -1 at S.</summary>
    public long NextRankAt(int rank) => rank switch { 0 => RankD, 1 => RankC, 2 => RankB, 3 => RankA, 4 => RankS, 5 => RankSS, _ => -1 };
}
