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

    /// <summary>The cats a quest of <paramref name="rank"/> pays, from the lowest to the highest of the range.</summary>
    public static (int Min, int Max) Cats(int rank) => rank switch
    {
        0 => (200, 600),
        1 => (500, 1500),
        2 => (1500, 4000),
        3 => (4000, 10000),
        4 => (10000, 20000),
        5 => (20000, 50000),
        _ => (50000, 120000),
    };

    /// <summary>The guild rep a quest of <paramref name="rank"/> pays, before the random spread.</summary>
    public static int Rep(int rank) => rank switch { 0 => 5, 1 => 10, 2 => 20, 3 => 35, 4 => 55, 5 => 80, _ => 120 };

    /// <summary>The real relation a Faction quest of <paramref name="rank"/> adds when handed in (kept small on purpose).</summary>
    public static int Relation(int rank) => rank switch { <= 2 => 1, 3 => 2, 4 => 3, 5 => 4, _ => 5 };

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

    public string Describe() => Quest.Describe(Kind, Need, TargetName, Group, GroupSize, StartsWar);
}

/// <summary>One town's board for one day.</summary>
public sealed class Board
{
    public int Site;
    public long Day;
    public string GiverFaction = "";
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
    /// <summary>The people already counted (lasting ids): each counts once, whatever happens to them after.</summary>
    public List<ulong> Defeated = [];

    /// <summary>The platoon tag of this quest's group or spawned group.</summary>
    public string Tag => $"radiantquests:{Id}";
    /// <summary>Where it was taken (the clerk, world X and Z): target groups count within the target range of here.</summary>
    public float AnchorX, AnchorZ;
    /// <summary>The look for target groups in range (and the spawn when there are none) is done.</summary>
    public bool Placed;

    public bool Done => Have >= Need;

    public string Describe() => Describe(Kind, Need, TargetName, Group, GroupSize, StartsWar);

    public static string Describe(string kind, int need, string target, bool group = false, int groupSize = 0, bool startsWar = false)
    {
        string text = kind switch
        {
            _ when group => $"Wipe out a group of {groupSize} {target} ({need} needed)",
            Catalog.Hunt => $"Hunt {need} {target}",
            Catalog.Cull => $"Defeat {need} {target}",
            _ => $"{kind} {need} {target}",
        };
        return startsWar ? $"{text} - makes {target} hostile to you" : text;
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
        h.Add(Placed);
        h.Add(Group);
        h.Add(GroupSize);
        h.Add(Hit);
        h.Add(StartsWar);
        h.Add(Defeated.Count);
        foreach (var d in Defeated) h.Add(d);
    }
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
        h.Add(NextId);
        h.Add(Quests.Count);
        foreach (var q in Quests) q.Hash(ref h);
        h.Add(Boards.Count);
        foreach (var b in Boards)
        {
            h.Add(b.Site);
            h.Add(b.Day);
            h.Add(Rng.StableHash(b.GiverFaction));
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

/// <summary>Hand in a finished quest at <paramref name="Clerk"/>.</summary>
public sealed record HandIn(CharacterId Clerk, int QuestId) : GuildCommand;

/// <summary>Give up a quest.</summary>
public sealed record Abandon(int QuestId) : GuildCommand;

/// <summary>Test channel only: count a kill of <paramref name="TargetId"/> for your quests as if one of your characters made it.</summary>
public sealed record TestKill(string TargetId) : GuildCommand;

/// <summary>Test channel only: add guild rep.</summary>
public sealed record TestRep(int Amount) : GuildCommand;

/// <summary>Test channel only: brings every quest's deadline <paramref name="Hours"/> game hours closer, as if that much time had passed.</summary>
public sealed record TestSkip(double Hours) : GuildCommand;

/// <summary>Test channel only: counts kills for quest <paramref name="QuestId"/> until it is done.</summary>
public sealed record TestFinish(int QuestId) : GuildCommand;
