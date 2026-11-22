// Radiant Quests for Meitou. GPLv3 or later.
// The guild's state: your rep, your quests and the day's boards. All of it is saved with the game (GuildSystem: IModSaveData) and hashed
// (IStateHashed), and changed only inside the world's serial steps, so every machine plays the same.
using Meitou.Simulation;

namespace RadiantQuests;

public static class Ranks
{
    public static readonly string[] Names = ["D", "C", "B", "A", "S"];

    public static string Name(int rank) => Names[Math.Clamp(rank, 0, 4)];

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
        0 => (500, 1500),
        1 => (1500, 4000),
        2 => (4000, 10000),
        3 => (10000, 20000),
        _ => (20000, 50000),
    };

    /// <summary>The guild rep a quest of <paramref name="rank"/> pays, before the random spread.</summary>
    public static int Rep(int rank) => rank switch { 0 => 10, 1 => 20, 2 => 35, 3 => 55, _ => 80 };

    /// <summary>How many to kill for a quest of <paramref name="rank"/>.</summary>
    public static (int Min, int Max) KillCount(int rank) => rank switch
    {
        0 => (2, 4),
        1 => (3, 6),
        2 => (4, 8),
        3 => (5, 10),
        _ => (6, 12),
    };
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

    public string Describe() => Quest.Describe(Kind, Need, TargetName);
}

/// <summary>One town's board for one day.</summary>
public sealed class Board
{
    public int Site;
    public long Day;
    public string GiverFaction = "";
    public List<Offer> Offers = [];
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

    public bool Done => Have >= Need;

    public string Describe() => Describe(Kind, Need, TargetName);

    public static string Describe(string kind, int need, string target) => kind switch
    {
        Catalog.Hunt => $"Hunt {need} {target}",
        Catalog.Cull => $"Kill {need} {target}",
        _ => $"{kind} {need} {target}",
    };

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

    public void Hash(ref StateHasher h)
    {
        h.Add(Lifetime);
        h.Add(Balance);
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
            foreach (var o in b.Offers)
            {
                h.Add(o.Slot);
                h.Add(Rng.StableHash(o.TargetId));
                h.Add(o.Rank);
                h.Add(o.Need);
                h.Add(o.Cats);
                h.Add(o.Rep);
                h.Add(o.Taken);
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
public sealed record AcceptOffer(CharacterId Clerk, int Site, int Slot) : GuildCommand;

/// <summary>Hand in a finished quest at <paramref name="Clerk"/>.</summary>
public sealed record HandIn(CharacterId Clerk, int QuestId) : GuildCommand;

/// <summary>Give up a quest.</summary>
public sealed record Abandon(int QuestId) : GuildCommand;

/// <summary>Test channel only: count a kill of <paramref name="TargetId"/> for your quests as if one of your characters made it.</summary>
public sealed record TestKill(string TargetId) : GuildCommand;

/// <summary>Test channel only: add guild rep.</summary>
public sealed record TestRep(int Amount) : GuildCommand;
