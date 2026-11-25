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
    static MethodInfo? s_notice, s_uiAction, s_isOpen;
    static FieldInfo? s_screens;
    static bool s_on;

    /// <summary>
    /// A request to open the game's inventory window (the hand-in pick, his idea 2026-11-25), done on the next frame through the player
    /// interface's own "inventory" action (as its key does), only when it is not open already. Skipped if a future Meitou renames the parts.
    /// </summary>
    static volatile bool s_openInventory;

    public static void OpenInventory() => s_openInventory = true;

    /// <summary>A request to close the game's inventory window (after a hand-in), the same way: its own action, only when it is open.</summary>
    static volatile bool s_closeInventory;

    public static void CloseInventory() => s_closeInventory = true;

    public static string Patch(Harmony harmony)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
        var steer = type is null ? null : AccessTools.Method(type, "Steer", [typeof(bool)]);
        s_notice = type is null ? null : AccessTools.Method(type, "Notice", [typeof(string), typeof(bool), typeof(string)]);
        if (steer is null || s_notice is null) return "message log: PlayerInterface.Steer/Notice not found; quest notices show as toasts only";
        s_uiAction = AccessTools.Method(type, "ExecuteUiAction", [typeof(string), typeof(string)]);
        s_screens = AccessTools.Field(type, "FaithfulScreens");
        s_isOpen = s_screens is null ? null : AccessTools.Method(s_screens.FieldType, "IsOpen", [typeof(string)]);
        if (s_uiAction is null || s_isOpen is null) RadiantQuestsMod.Log("hand-in: the inventory can't be opened for you in this Meitou (open it yourself)");
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
            if (s_closeInventory)
            {
                s_closeInventory = false;
                if (s_uiAction is not null && s_isOpen is not null && s_screens?.GetValue(__instance) is { } open
                    && s_isOpen.Invoke(open, ["inventory"]) is true)
                    s_uiAction.Invoke(__instance, ["inventory", ""]);
            }
            if (s_openInventory)
            {
                s_openInventory = false;
                if (s_uiAction is not null && s_isOpen is not null && s_screens?.GetValue(__instance) is { } screens
                    && s_isOpen.Invoke(screens, ["inventory"]) is false)
                    s_uiAction.Invoke(__instance, ["inventory", ""]);
            }
        }
        catch (Exception e)
        {
            s_on = false;
            RadiantQuestsMod.LogException("message log", e);
        }
    }
}
