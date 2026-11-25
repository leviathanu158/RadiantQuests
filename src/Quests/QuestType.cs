// Radiant Quests for Meitou. GPLv3 or later.
// The quest-type plug-in contract. A quest type (hunt, defeat, group, and later lair, fetch, courier...) answers a few questions; everything
// else (boards, ranks, pay, rep, timers, penalties, window, tracker, markers, saving) is shared by the engine (GuildSystem). Adding a quest
// type means one new class registered in QuestTypes.All.
using System.Numerics;
using Meitou.Simulation;

namespace RadiantQuests;

/// <summary>Which tab of the guild window an offer is for.</summary>
public enum Tab { General, Faction }

/// <summary>What a quest type sees while a town's board is made.</summary>
public sealed class BoardContext(World world, GuildSystem guild, Survey survey, Tab tab, int giver, string giverId, Dice dice, int site = -1)
{
    public World World { get; } = world;
    public GuildSystem Guild { get; } = guild;
    /// <summary>What lives around the town now.</summary>
    public Survey Survey { get; } = survey;
    public Tab Tab { get; } = tab;
    /// <summary>The town owner's faction index (the Faction tab's work is for it).</summary>
    public int Giver { get; } = giver;
    public string GiverId { get; } = giverId;
    public Dice Dice { get; } = dice;
    /// <summary>The town (site index) the board is for; -1 unknown.</summary>
    public int Site { get; } = site;
}

/// <summary>
/// One possible offer and how much the board should favour it. <paramref name="Present"/>: its targets are really around now (no spawning
/// needed). A rank's board shows every present target first; offers that bring their targets in are used only for a rank with none.
/// </summary>
public readonly record struct Candidate(Offer Offer, double Weight, bool Present = true);

/// <summary>What a quest type is told about a quest's progress check.</summary>
/// <summary>Going on; done (hand it in); called off (no penalty: not your doing); failed (the penalty, as running out of time).</summary>
public enum TrackResult { Going, Done, CallOff, Failed }

public interface IQuestType
{
    /// <summary>The kind stored in Offer.Kind / Quest.Kind.</summary>
    string Kind { get; }

    /// <summary>Whether this type is switched on (settings) and offered on <paramref name="tab"/>.</summary>
    bool Offered(SettingsCopy set, Tab tab);

    /// <summary>The offers this type could put on the board at <paramref name="rank"/> (the engine picks among them for variety).</summary>
    IEnumerable<Candidate> Candidates(BoardContext ctx, int rank);

    /// <summary>Sets a taken quest up (tag a group, spawn what's missing, adjust the count). False refuses it (the type tells the player why).</summary>
    bool Accept(World world, GuildSystem guild, Quest q, Offer offer, Vector2 at);

    /// <summary>Whether a defeat of <paramref name="person"/> counts for <paramref name="q"/>.</summary>
    bool Counts(Quest q, PersonFacts person, string race, string familyHead);

    /// <summary>Every few seconds: where the quest's targets are (markers), refilling or calling it off when nothing is left to find.</summary>
    TrackResult Track(World world, GuildSystem guild, Quest q, List<Vector2> markers);

    /// <summary>The quest ended (any way): give back what it holds.</summary>
    void End(World world, GuildSystem guild, Quest q);

    /// <summary>
    /// Handing a done quest in at <paramref name="clerk"/>: a type that wants something (items, a parcel, a certain town) takes or checks it
    /// here; false refuses the hand-in (the type tells the player why) and nothing is paid. Default: nothing to hand over.
    /// </summary>
    bool HandIn(World world, GuildSystem guild, Quest q, ClerkInfo clerk) => true;

    /// <summary>One of your characters sold an item of <paramref name="record"/> to a trader (item quests check whether it was theirs).</summary>
    void Sold(World world, GuildSystem guild, Quest q, string record) { }
}

public static class QuestTypes
{
    /// <summary>Every quest type, in board order.</summary>
    public static readonly IQuestType[] All = [new HuntQuest(), new DefeatQuest(), new GroupQuest(), new LairQuest(),
        new SupplyQuest(), new GearQuest(), new FindQuest(), new CourierQuest(), new RecoverQuest()];

    public static IQuestType? Of(string kind) => All.FirstOrDefault(t => t.Kind == kind);
}

/// <summary>A small seeded random sequence (same seed, same rolls on every machine).</summary>
public sealed class Dice(ulong seed)
{
    ulong _state = seed;

    public ulong Next() => Meitou.Simulation.Rng.Mix(_state += 0x9E3779B97F4A7C15UL);

    public double Double() => (Next() >> 11) * (1.0 / (1UL << 53));

    public int Int(int min, int max) => max <= min ? min : min + (int)(Next() % (ulong)(max - min + 1));

    public bool Chance(double p) => Double() < p;

    /// <summary>An index picked with chances in proportion to <paramref name="weights"/> (-1 when all are 0).</summary>
    public int Weighted(IReadOnlyList<double> weights)
    {
        double total = weights.Sum();
        if (total <= 0) return -1;
        double roll = Double() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0) return i;
        }
        return weights.Count - 1;
    }
}
