// Radiant Quests for Meitou. GPLv3 or later.
// The pick window (his rule, 2026-11-24, after a hand-in took the armour off his character's chest): handing in goods lists every matching
// item your characters at the clerk carry, with who has it and what it is worth; you choose which go. Worn gear is never taken (taking it
// off is the game's own unequip, so the model stays right): it is listed as "worn: take it off first". Items that don't count say why
// (stolen from this faction, worth too little), so a missing one is never a mystery.
using Meitou.Simulation;

namespace RadiantQuests;

/// <summary>One line of the pick window: its key, the item, who carries it, its worth (one), how many, and why it can't go ("" when it can).</summary>
public sealed record HandInChoice(int Key, string Name, string Holder, int Worth, int Units, string Problem);

/// <summary>The pick window: the quest, how many it needs, and what can be picked.</summary>
public sealed record HandInView(int QuestId, string Quest, int Need, IReadOnlyList<HandInChoice> Choices);

public sealed partial class GuildSystem
{
    HandInView? _handInView;
    List<WorldAccess.Offered> _handInItems = [];

    /// <summary>The items picked for the hand-in being made now (null: none; the quest takes its own way).</summary>
    public IReadOnlyList<WorldAccess.Offered>? Picked { get; private set; }

    void OpenHandIn(World world, CharacterId clerkId, int questId)
    {
        if (_s.Quests.FirstOrDefault(x => x.Id == questId) is not { } q || ClerkInReach(world, clerkId) is not { } clerk) return;
        if (q.Kind is not (ItemQuestKinds.Supply or ItemQuestKinds.Gear or ItemQuestKinds.Find))
        {
            HandInQuest(world, new HandIn(clerkId, questId) { Tick = world.Tick });
            return;
        }
        if (clerk.Site != q.Site)
        {
            RadiantQuestsMod.Toast($"Hand this one in at {TownName(q.Site)}, where you took it.");
            return;
        }
        var at = new System.Numerics.Vector2(clerk.Position.X, clerk.Position.Z);
        int least = q.MinWorth > 0 ? (int)Math.Floor(q.MinWorth * ItemQuestKinds.WorthGrace) : 0;
        int refuse = _data.FactionIndex(q.GiverFaction);
        _handInItems = Access.Offer(world, at, ClerkReach, ItemCat.MembersOf(q.TargetId));
        var choices = new List<HandInChoice>();
        for (int k = 0; k < _handInItems.Count; k++)
        {
            var o = _handInItems[k];
            string problem = o.Worn ? "worn: take it off first"
                : refuse >= 0 && o.StolenFrom == refuse ? $"stolen from {clerk.FactionName}"
                : least > 0 && o.Worth < least ? $"worth too little (needs about {q.MinWorth:N0})" : "";
            choices.Add(new HandInChoice(k, o.Name, o.Holder, o.Worth, o.Units, problem));
        }
        _handInView = new HandInView(q.Id, q.Describe(), q.Need, choices);
        _dirty = true;
        if (choices.Count == 0) RadiantQuestsMod.Toast($"None of your characters at the clerk carry anything for: {q.Describe()}.");
    }

    void HandInPick(World world, HandInPicked p)
    {
        if (_handInView is not { } view || view.QuestId != p.QuestId) return;
        var picked = p.Keys.Distinct().Where(k => k >= 0 && k < _handInItems.Count && view.Choices[k].Problem.Length == 0)
            .Select(k => _handInItems[k]).ToList();
        int units = picked.Sum(o => o.Units);
        if (units < view.Need)
        {
            RadiantQuestsMod.Toast($"Pick {view.Need} in all (you picked {units}).");
            return;
        }
        Picked = picked;
        try { HandInQuest(world, new HandIn(p.Clerk, p.QuestId) { Tick = world.Tick }); }
        finally { Picked = null; }
        if (!_s.Quests.Any(x => x.Id == p.QuestId)) CloseHandIn();
        _dirty = true;
    }

    /// <summary>Closes the pick window.</summary>
    void CloseHandIn()
    {
        _handInView = null;
        _handInItems = [];
    }
}
