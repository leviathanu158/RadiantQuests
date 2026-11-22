// Radiant Quests for Meitou. GPLv3 or later.
// Repeatable guild quests: talk to a bartender of a faction (right click > Adventurers' Guild), take a quest from the board of your rank, do it,
// hand it in for cats and guild rep. Guild rep sets your rank (D to S) and is spent at the rep store (a later step).
using System.Collections.Concurrent;
using Meitou.Modding;
using Meitou.Simulation;

namespace RadiantQuests;

public sealed class RadiantQuestsMod : IMod
{
    static ModContext? s_ctx;

    /// <summary>The player's settings (the page writes them; the world copies what it needs each tick).</summary>
    public static GuildSettings Settings { get; } = new();

    /// <summary>The quest targets read from the game data at start (every faction, animal and squad of the load order).</summary>
    public static Catalog Catalog { get; private set; } = Catalog.Empty;

    public static ModContext? Context => s_ctx;

    public void Initialize(ModContext c)
    {
        s_ctx = c;
        c.AddSettings(Settings);

        Catalog = Catalog.Build(c.Data, Log, Overrides.Load(c.Folder, Log));
        if (Settings.Debug) foreach (var note in Catalog.Checks) Log("check: " + note);
        Log($"catalog: {Catalog.Hunts.Count} animal kinds to hunt, {Catalog.Culls.Count} groups to cull, {Catalog.GuildFactions.Count} guild factions");

        c.AddSystem("guild", ctx => new GuildSystem(ctx.Data, ctx.Find<Meitou.Simulation.Population.PopulationSystem>()), SystemAnchor.End);
        c.AddUi(new GuildUi());
        Log(PausedCommands.Patch(c.Harmony));
        Log("ready: right click a faction's bartender and choose Adventurers' Guild");
    }

    static readonly ConcurrentQueue<string> s_toasts = new();

    /// <summary>Queues a message for the screen (any thread); the interface shows it for a few seconds.</summary>
    public static void Toast(string text)
    {
        if (s_toasts.Count < 16) s_toasts.Enqueue(text);
        if (Settings.Debug || TestChannel.On) Log("message: " + text);
    }

    /// <summary>The next queued message, or null.</summary>
    public static string? NextToast() => s_toasts.TryDequeue(out var text) ? text : null;

    public static void Log(string line)
    {
        if (s_ctx is { } c) c.Log(line);
        else Console.WriteLine($"mod       radiantquests: {line}");
    }

    public static void Detail(string line)
    {
        if (Settings.Debug || TestChannel.On) Log(line);
    }

    static long s_lastErrorTick = long.MinValue;

    /// <summary>A failure in interface code: logged (at most about once a second) and never passed to the game.</summary>
    public static void LogException(string where, Exception e)
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref s_lastErrorTick) < 1000) return;
        Interlocked.Exchange(ref s_lastErrorTick, now);
        string line = $"{where} failed: {e}";
        if (s_ctx is { } c) c.LogError(line);
        else Console.Error.WriteLine($"mod       radiantquests: {line}");
    }
}
