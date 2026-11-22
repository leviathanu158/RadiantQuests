// Radiant Quests for Meitou. GPLv3 or later.
// The rows on Settings > Mods > Radiant Quests. The page changes them on the interface's thread; GuildSystem copies what the world needs in its
// serial Inputs step each tick, so every machine plays the same (docs/modding.md "Settings").
using Meitou.Modding;

namespace RadiantQuests;

public sealed class GuildSettings
{
    [Setting("Rep for rank C", Min = 10, Max = 5000, Step = 10, SubGroup = "Ranks",
        Hint = "Lifetime guild rep needed for rank C. Spending rep at the store never lowers your rank.")]
    public int RankC { get; set; } = 100;

    [Setting("Rep for rank B", Min = 20, Max = 10000, Step = 10, SubGroup = "Ranks")]
    public int RankB { get; set; } = 300;

    [Setting("Rep for rank A", Min = 30, Max = 20000, Step = 10, SubGroup = "Ranks")]
    public int RankA { get; set; } = 700;

    [Setting("Rep for rank S", Min = 40, Max = 50000, Step = 10, SubGroup = "Ranks")]
    public int RankS { get; set; } = 1500;

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

    [Setting("Quest tracker", Hint = "A small list of your guild quests at the top right of the screen.")]
    public bool Tracker { get; set; } = true;

    [Setting("Debug log", Hint = "Writes extra lines about quests and kills to log.txt. Leave off for play: it costs a little performance.")]
    public bool Debug { get; set; }

    /// <summary>The settings the world reads, copied in a serial step (one value per tick on every machine).</summary>
    public SettingsCopy Copy() => new(RankC, RankB, RankA, RankS, CatsMult, RepMult, KillHours, KillRefreshHours, FailCost, AbandonCost);
}

/// <summary>The settings as the world saw them this tick.</summary>
public sealed record SettingsCopy(int RankC, int RankB, int RankA, int RankS, double CatsMult, double RepMult, int KillHours,
    int KillRefreshHours, double FailCost, double AbandonCost)
{
    public static SettingsCopy Defaults { get; } = new GuildSettings().Copy();

    /// <summary>The rank (0 D .. 4 S) for <paramref name="lifetime"/> rep.</summary>
    public int RankOf(long lifetime) => lifetime >= RankS ? 4 : lifetime >= RankA ? 3 : lifetime >= RankB ? 2 : lifetime >= RankC ? 1 : 0;

    /// <summary>The lifetime rep the next rank needs, or -1 at S.</summary>
    public long NextRankAt(int rank) => rank switch { 0 => RankC, 1 => RankB, 2 => RankA, 3 => RankS, _ => -1 };
}
