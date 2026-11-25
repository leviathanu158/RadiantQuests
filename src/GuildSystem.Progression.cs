// Radiant Quests for Meitou. GPLv3 or later.
// Progression (his design, 2026-11-25):
//   - ENTRY: a newcomer gets one entry trial (an easy E fight brought in); finish it and you are a member: the boards open.
//   - RANK TRIALS: the rep for the next rank no longer promotes by itself: the clerk offers a trial, a Hard fight of the new rank
//     (brought in). Win it and you are promoted. Failing or giving a trial up costs nothing; take a new one later.
//   - REWARDS: each promotion gives a reward you pick at a clerk, from a wide list so anyone finds value: a weapon of any kind, armour of
//     any class and slot, a backpack, medical supplies, or a purse of cats; gear comes at a grade fitting the new rank.
//   - TITLES: a wasteland title per rank.
using System.Numerics;
using Meitou.Simulation;

namespace RadiantQuests;

/// <summary>A rank reward a player can pick: its key, what the window shows, and the group of the choice ("Weapon", "Armour", "Other").</summary>
public sealed record RewardChoice(string Key, string Label, string Group);

public sealed partial class GuildSystem
{
    /// <summary>The titles, E to SS (his call: wasteland flavour).</summary>
    public static readonly string[] Titles =
        ["Dust Walker", "Sellsword", "Road Hound", "Wasteland Stalker", "Iron Warden", "Scourge of the Sands", "Legend of the Wastes"];

    public static string Title(int rank) => Titles[Math.Clamp(rank, 0, Titles.Length - 1)];

    /// <summary>The gear grade a rank's reward comes at (weapon and armour level, 0..100): D 25 ... SS 100.</summary>
    static int RewardGrade(int rank) => rank switch { <= 1 => 25, 2 => 40, 3 => 55, 4 => 70, 5 => 85, _ => 100 };

    /// <summary>Whether a trial quest is running now.</summary>
    bool TrialRunning => _s.Quests.Any(q => q.Trial > 0);

    /// <summary>The rank your rep has earned (a trial still stands between it and your rank).</summary>
    int EarnedRank => _set.RankOf(_s.Lifetime);

    /// <summary>
    /// Saves from before 0.11.0: whoever already had rep or quests is a member, at the rank their rep gives (no trials owed for it).
    /// </summary>
    void MigrateProgression()
    {
        if (_s.ProgressionSet) return;
        _s.ProgressionSet = true;
        _s.Member = _s.Lifetime > 0 || _s.Quests.Count > 0;
        _s.Promoted = _set.RankOf(_s.Lifetime);
    }

    /// <summary>
    /// The trial the clerk would give now: the entry trial for a newcomer, a rank trial when your rep has earned the next rank; null when
    /// none is due (or one is running).
    /// </summary>
    (int Kind, int Rank)? TrialDue()
    {
        if (!RadiantQuestsMod.Settings.Trials || TrialRunning) return null;
        if (!_s.Member) return (1, 0);
        if (EarnedRank > _s.Promoted && _s.Promoted < Ranks.Top) return (2, _s.Promoted + 1);
        return null;
    }

    /// <summary>
    /// Gives the trial now due (his rules): a fight brought in (Hunt or Defeat), the entry trial an easy E one, a rank trial the hardest
    /// one the area can make at the new rank. Put on the clerk's board as a taken offer and accepted at once.
    /// </summary>
    void TakeTrial(World world, CharacterId clerkId)
    {
        if (TrialDue() is not { } due || ClerkInReach(world, clerkId) is not { } clerk) return;
        var at = new Vector2(clerk.Position.X, clerk.Position.Z);
        var survey = Survey.Of(world, Access, Cat, at, _set.TargetRange);
        var picks = new List<Candidate>();
        foreach (var kind in new[] { Catalog.Cull, Catalog.Hunt })
        {
            if (QuestTypes.Of(kind) is not { } type) continue;
            for (int roll = 0; roll < 4; roll++)
            {
                var ctx = new BoardContext(world, this, survey, Tab.General, _data.FactionIndex(clerk.Faction), clerk.Faction,
                    new Dice(world.Seed ^ Rng.Mix((ulong)world.Tick * 31 + (ulong)roll * 977 + (ulong)due.Rank + 0x747269UL)), clerk.Site);
                try { picks.AddRange(type.Candidates(ctx, due.Rank).Where(c => !c.Offer.Stray && c.Offer.Need > 0)); }
                catch (Exception e) { RadiantQuestsMod.Log($"trial candidates skipped: {e.Message}"); }
            }
        }
        if (picks.Count == 0)
        {
            RadiantQuestsMod.Toast("The clerk has no trial for you here today. Try another town's clerk.");
            return;
        }
        // Entry: the easiest; rank trial: the hardest (a Hard fight of the new rank).
        var pick = due.Kind == 1 ? picks.OrderBy(c => c.Offer.Score).First() : picks.OrderByDescending(c => c.Offer.Score).First();
        var board = BoardFor(world, clerk);
        var o = Clone(pick.Offer);
        o.Trial = due.Kind;
        (o.Cats, o.Rep) = Difficulty.Pay(o.Score, o.Rank, 0.5, 0.5);
        o.Slot = board.Offers.Count;
        board.Offers.Add(o);
        Accept(world, new AcceptOffer(clerkId, clerk.Site, o.Slot) { Tick = world.Tick });
    }

    /// <summary>A trial was handed in: membership or the promotion, the title and the reward to pick.</summary>
    void TrialDone(Quest q)
    {
        if (q.Trial == 1)
        {
            _s.Member = true;
            Notices.Post($"Welcome to the Adventurers' Guild, {Title(0)}. The boards are open to you.");
        }
        else if (q.Trial == 2)
        {
            _s.Promoted = Math.Max(_s.Promoted, q.Rank);
            if (!_s.RewardsDue.Contains(_s.Promoted)) _s.RewardsDue.Add(_s.Promoted);
            Notices.Post($"Promoted to guild rank {Ranks.Name(_s.Promoted)}: {Title(_s.Promoted)}. Pick your reward at any guild clerk.");
        }
        _dirty = true;
    }

    /// <summary>A trial failed or given up: no cost (his rule); a new one can be taken.</summary>
    bool TrialEnded(World world, Quest q, string why)
    {
        if (q.Trial == 0) return false;
        End(world, q);
        Notices.Post($"Trial {why}: {q.Describe()}. No penalty: a guild clerk can give you a new trial.");
        return true;
    }

    /// <summary>
    /// The rewards to pick from (his rule: a wide selection so anyone finds value): every weapon kind, every armour class and slot, a
    /// backpack, medical supplies, a purse of cats.
    /// </summary>
    public List<RewardChoice> RewardChoices()
    {
        var list = new List<RewardChoice>();
        foreach (var g in ItemCat.Groups.Values.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (RewardItem(g.Key) is null) continue;
            list.Add(new RewardChoice(g.Key, g.Name, g.Key.StartsWith("group:weapon:", StringComparison.Ordinal) ? "Weapon" : "Armour"));
        }
        if (BestOf(ItemKind.Backpack) is not null) list.Add(new RewardChoice("pack", "a backpack", "Other"));
        if (BestOf(ItemKind.Medical) is not null) list.Add(new RewardChoice("med", "medical supplies", "Other"));
        list.Add(new RewardChoice("cats", "a purse of cats", "Other"));
        return list;
    }

    /// <summary>The item a gear group's reward gives: the group's best-known member (sold in the most towns, then the best at its top grade).</summary>
    string? RewardItem(string group) => ItemCat.Groups.TryGetValue(group, out var g)
        ? g.Members.Select(id => ItemCat.Items.GetValueOrDefault(id)).Where(f => f is not null && f.Rarity != ItemRarity.Unobtainable)
            .OrderByDescending(f => f!.ShopTowns).ThenByDescending(f => f!.ValueHi).ThenBy(f => f!.Id, StringComparer.Ordinal)
            .Select(f => f!.Id).FirstOrDefault()
        : null;

    /// <summary>The best sold item of a kind (a backpack, medicine): the most valuable one found in at least 2 towns' shops.</summary>
    ItemFacts? BestOf(ItemKind kind) => ItemCat.Items.Values.Where(i => i.Kind == kind && i.ShopTowns >= 2)
        .OrderByDescending(i => i.Value).ThenBy(i => i.Id, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>The purse a rank's reward in cats holds: one and a half times the rank's best quest pay.</summary>
    static long RewardPurse(int rank) => (long)Math.Round(Ranks.Cats(rank).Max * 1.5 / 10) * 10;

    void ClaimReward(World world, ClaimRankReward c)
    {
        if (!_s.RewardsDue.Contains(c.Rank) || ClerkInReach(world, c.Clerk) is not { } clerk) return;
        var at = new Vector2(clerk.Position.X, clerk.Position.Z);
        var who = Access.NearestYours(world, at, ClerkReach);
        if (who == CharacterId.None) return;
        string what;
        if (c.Choice == "cats")
        {
            long purse = RewardPurse(c.Rank);
            Access.Pay(world, purse);
            what = $"{purse} cats";
        }
        else if (c.Choice == "pack" && BestOf(ItemKind.Backpack) is { } pack)
        {
            Access.Give(world, who, pack.Id, 1);
            what = pack.Name;
        }
        else if (c.Choice == "med" && BestOf(ItemKind.Medical) is { } med)
        {
            int n = 2 + c.Rank;
            for (int i = 0; i < n; i++) Access.Give(world, who, med.Id, 1);
            what = $"{n} x {med.Name}";
        }
        else if (RewardItem(c.Choice) is { } record && ItemCat.Items.TryGetValue(record, out var item))
        {
            Access.Give(world, who, record, 1, level: RewardGrade(c.Rank));
            what = $"{item.Name} (grade {RewardGrade(c.Rank)})";
        }
        else return;
        _s.RewardsDue.Remove(c.Rank);
        _dirty = true;
        RadiantQuestsMod.Toast($"Rank {Ranks.Name(c.Rank)} reward: {what}.");
    }
}
