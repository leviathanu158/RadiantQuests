// Radiant Quests for Meitou. GPLv3 or later.
// The pick window (his rule, 2026-11-24, after a hand-in took the armour off his character's chest): handing in goods lists every matching
// item your characters at the clerk carry, with who has it and what it is worth; you choose which go. Worn gear is never taken (taking it
// off is the game's own unequip, so the model stays right): it is listed as "worn: take it off first". Items that don't count say why
// (stolen from this faction, worth too little), so a missing one is never a mystery.
using Meitou.Simulation;

namespace RadiantQuests;

/// <summary>One line of the pick window: its key, the item, who carries it, its worth (one), how many, and why it can't go ("" when it can).</summary>
public sealed record HandInChoice(int Key, string Name, string Holder, int Worth, int Units, string Problem)
{
    /// <summary>Who carries it, and where its items sit in their inventory window (for marking the cells there).</summary>
    public CharacterId Who { get; init; }
    public IReadOnlyList<int> Cells { get; init; } = [];
    /// <summary>Where each of its items lies (section, square), to mark the right cells; and its lasting key (picks keep to it).</summary>
    public IReadOnlyList<(string Section, int X, int Y)> Spots { get; init; } = [];
    public string Id { get; init; } = "";
    public string Record { get; init; } = "";
}

/// <summary>The pick window: the quest, how many it needs, and what can be picked.</summary>
public sealed record HandInView(int QuestId, string Quest, int Need, IReadOnlyList<HandInChoice> Choices);

public sealed partial class GuildSystem
{
    HandInView? _handInView;

    /// <summary>Whether a hand-in's pick list is open.</summary>
    public bool PickOpen => _handInView is not null;
    List<WorldAccess.Offered> _handInItems = [];

    /// <summary>The items picked for the hand-in being made now (null: none; the quest takes its own way).</summary>
    public IReadOnlyList<WorldAccess.Offered>? Picked { get; private set; }

    /// <summary>The clerk the pick window was opened at (its list is refreshed while it is open, so moved items stay marked right).</summary>
    CharacterId _handInClerk;

    /// <summary>Refreshes an open pick window's list (items moved, picked up or used since): at each publish.</summary>
    void RefreshHandIn(World world)
    {
        if (_handInView is not { } hv) return;
        // (A quiet check: walking away from the clerk just closes the list, no message.)
        if (!_s.Quests.Any(x => x.Id == hv.QuestId) || !_clerks.ContainsKey(_handInClerk) || !Access.YoursNear(world, _handInClerk, ClerkReach))
        {
            CloseHandIn();
            return;
        }
        OpenHandIn(world, _handInClerk, hv.QuestId, quiet: true);
    }

    void OpenHandIn(World world, CharacterId clerkId, int questId, bool quiet = false)
    {
        _handInClerk = clerkId;
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
            choices.Add(new HandInChoice(k, o.Name, o.Holder, o.Worth, o.Units, problem)
                { Who = o.Who, Cells = o.Cells, Spots = o.Spots, Id = o.Id, Record = o.Items[0].Record });
        }
        _handInView = new HandInView(q.Id, q.Describe(), Math.Max(1, q.Need - q.Delivered), choices);
        _dirty = true;
        if (choices.Count == 0 && !quiet) RadiantQuestsMod.Toast($"None of your characters at the clerk carry anything for: {q.Describe()}.");
    }

    void HandInPick(World world, HandInPicked p)
    {
        if (_handInView is not { } view || view.QuestId != p.QuestId) return;
        var ids = p.Ids.ToHashSet(StringComparer.Ordinal);
        var picked = _handInItems.Where((o, k) => ids.Contains(o.Id) && view.Choices[k].Problem.Length == 0).ToList();
        int units = picked.Sum(o => o.Units);
        if (units < 1 || _s.Quests.FirstOrDefault(x => x.Id == p.QuestId) is not { } q)
        {
            RadiantQuestsMod.Toast("Pick what to hand in first.");
            return;
        }
        // Fewer than are still needed: a partial hand-in (his rule, 2026-11-25): those go now and count; the reward comes when it's complete
        // (or its share, if it's given up or runs out of time).
        int still = q.Need - q.Delivered;
        if (units < still)
        {
            if (ClerkInReach(world, p.Clerk) is not { } clerk || clerk.Site != q.Site) return;
            var taken = Access.TakePicked(world, picked, units);
            if (taken.Count == 0) return;
            q.Delivered += taken.Count;
            q.Have = Math.Min(q.Need, q.Delivered);
            RadiantQuestsMod.Toast($"Handed in {taken.Count}: {q.Delivered} of {q.Need} delivered for \"{q.Describe()}\". It can't fail now; "
                + "bring the rest for the full reward.");
            _dirty = true;
            return;
        }
        Picked = picked;
        try { HandInQuest(world, new HandIn(p.Clerk, p.QuestId) { Tick = world.Tick }); }
        finally { Picked = null; }
        if (!_s.Quests.Any(x => x.Id == p.QuestId)) CloseHandIn();
        _dirty = true;
    }

    /// <summary>
    /// A quest with goods already delivered that is given up or runs out of time (his rule, 2026-11-25): no penalty, the share of the reward
    /// for what was delivered. False when nothing was delivered (the usual rules then).
    /// </summary>
    bool SettleDelivered(World world, Quest q, string why)
    {
        if (q.Delivered <= 0 || q.Need <= 0) return false;
        double share = Math.Clamp((double)q.Delivered / q.Need, 0, 1);
        long cats = (long)Math.Round(q.CatsNow * share / 10) * 10;
        int rep = (int)Math.Round(q.RepNow * share);
        End(world, q);
        Access.Pay(world, cats);
        if (q.General) GainRep(rep);
        else
        {
            GainPoints(q.GiverFaction, rep);
            GainRep((int)Math.Round(rep * Math.Clamp(_set.FactionGuildRep, 0, 1)));
        }
        Notices.Post($"Quest {why}: {q.Describe()}. You delivered {q.Delivered} of {q.Need}: +{cats} cats, +{rep} rep for those, no penalty.");
        return true;
    }

    /// <summary>Closes the pick window.</summary>
    void CloseHandIn()
    {
        _handInView = null;
        _handInItems = [];
    }
}
