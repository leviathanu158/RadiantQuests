// Radiant Quests for Meitou. GPLv3 or later.
// While the game is paused Meitou applies only its own windows' commands between ticks (World.ApplyPausedInterfaceCommands). This postfix lets
// the guild window work paused too: it takes the guild's due commands and gives them to the guild system the same way, then publishes the view.
// If a future Meitou renames the method, the patch is skipped with a log line and the buttons simply apply on the next tick.
using HarmonyLib;
using Meitou.Simulation;

namespace RadiantQuests;

public static class PausedCommands
{
    public static string Patch(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(World), "ApplyPausedInterfaceCommands", Type.EmptyTypes);
        if (target is null) return "paused commands: World.ApplyPausedInterfaceCommands not found; guild buttons apply when unpaused";
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(PausedCommands), nameof(Postfix)));
        return "paused commands: on";
    }

    static void Postfix(World __instance)
    {
        try
        {
            var world = __instance;
            if (world.Systems.OfType<GuildSystem>().FirstOrDefault() is not { } guild) return;
            var due = world.Commands.TakeDue(world.Tick, c => c is GuildCommand);
            // An open hand-in list is refreshed after the paused interface's own commands too (items moved while paused keep their marks).
            if (due.Count == 0)
            {
                if (guild.PickOpen) guild.PublishNow(world);
                return;
            }
            guild.Inputs(world, due);
            guild.PublishNow(world);
        }
        catch (Exception e)
        {
            RadiantQuestsMod.LogException("paused guild commands", e);
        }
    }
}
