// Radiant Quests for Meitou. GPLv3 or later.
// Lasting messages: a quest called off, failed or taken back goes to the game's own message panel AND its message log (the overview's
// messages), so the player notices it even when the short on-screen toast was missed. Meitou's PlayerInterface.Notice is internal, so the
// player interface is caught in a prefix on PlayerInterface.Steer (run every frame) and the queue is drained there. If a future Meitou renames
// either, the patch is skipped with a log line and the messages show as toasts only.
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;

namespace RadiantQuests;

public static class Notices
{
    static readonly ConcurrentQueue<string> s_queue = new();
    static MethodInfo? s_notice;
    static bool s_on;

    public static string Patch(Harmony harmony)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
        var steer = type is null ? null : AccessTools.Method(type, "Steer", [typeof(bool)]);
        s_notice = type is null ? null : AccessTools.Method(type, "Notice", [typeof(string), typeof(bool), typeof(string)]);
        if (steer is null || s_notice is null) return "message log: PlayerInterface.Steer/Notice not found; quest notices show as toasts only";
        harmony.Patch(steer, prefix: new HarmonyMethod(typeof(Notices), nameof(SteerPrefix)));
        s_on = true;
        return "message log: on";
    }

    /// <summary>Queues a lasting message (any thread). It is also shown as a toast.</summary>
    public static void Post(string text)
    {
        RadiantQuestsMod.Toast(text);
        if (s_on && s_queue.Count < 32) s_queue.Enqueue("Guild: " + text);
    }

    static void SteerPrefix(object __instance)
    {
        try
        {
            while (s_queue.TryDequeue(out var text)) s_notice!.Invoke(__instance, [text, false, text]);
        }
        catch (Exception e)
        {
            s_on = false;
            RadiantQuestsMod.LogException("message log", e);
        }
    }
}
