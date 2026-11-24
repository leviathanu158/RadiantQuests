// Radiant Quests for Meitou. GPLv3 or later.
// The fighting quest types: "Hunt N <animal>", "Defeat N <faction>" (any group counts) and "Wipe out a group" (one real group).
//
// Reliability rules shared by them:
//   - only targets that live around the town are offered (standing there now, or naturally spawning in its regions);
//   - a count never asks for more than is really there: with a margin (default 25%) so not every last one must be found;
//   - when taken, if too few are standing, more ordinary groups are spawned in the town's own regions (never uniques), else the count drops;
//   - while running, if the marked groups can't cover what's still needed: other real ones are marked, a shortfall within the margin is
//     forgiven, a top-up spawn that fits what is missing is tried, else the count drops to what can be done; targets someone else beat
//     (seen, not guessed) are not paid for (Quest.PayShare); nothing done and nothing left: called off, no penalty.
using System.Numerics;
using Meitou.Simulation;

namespace RadiantQuests;

/// <summary>Shared by "Hunt N" and "Defeat N": any group of the target counts.</summary>
public abstract class CountQuest : IQuestType
{
    public abstract string Kind { get; }

    public abstract bool Offered(SettingsCopy set, Tab tab);

    /// <summary>The targets this type may offer on the context's tab.</summary>
    protected abstract IEnumerable<Target> Targets(BoardContext ctx);

    protected static Catalog Cat => RadiantQuestsMod.Catalog;

    public IEnumerable<Candidate> Candidates(BoardContext ctx, int rank)
    {
        var set = ctx.Guild.Settings;
        foreach (var t in Targets(ctx))
        {
            int standing = ctx.Survey.StandingOf(t);
            bool spawns = set.SpawnTargets && ctx.Survey.SpawnsHere(Cat, t);
            bool stray = !spawns && standing == 0 && set.SpawnTargets && set.StrayChance > 0 && Survey.SpawnsAnywhere(Cat, t);
            if (standing == 0 && !spawns && !stray) continue;   // nothing of it around this town (and it can't be brought in)
            // Counts the ones really around can fill (with the margin) come first; only if none fits this rank, counts that a group the mod
            // can bring in really holds: a group of N asks for N less the margin (a pack of 6 antelope: 5), never a part of a bigger group.
            var counts = CountsFor(t, rank, Math.Min(12, Usable(standing, set)));
            bool present = counts.Count > 0;
            if (!present && (spawns || stray)) counts = GroupCounts(t, rank, spawns ? ctx.Survey.LocalTemplates(Cat, t) : Survey.AnyTemplates(Cat, t), set);
            if (counts.Count == 0) continue;
            // The smaller counts: a long list of the same thing is tedious, and a strong target reaches the rank with few.
            int need = counts[ctx.Dice.Int(0, Math.Min(1, counts.Count - 1))];
            var offer = new Offer { Kind = Kind, TargetId = t.Id, TargetName = t.Name.Trim(), Rank = rank, Need = need,
                Score = Catalog.QuestScore(Unit(t), need, Typical(t)), Stray = !present && !spawns };
            // Where the nearest of them stand (a quest that brings its targets in has no place yet).
            if (present)
            {
                var key = Survey.Key(t.Kind, t.Id);
                if (ctx.Survey.Groups.FirstOrDefault(g => g.Standing > 0 && Survey.KeysOf(Cat, g).Contains(key)) is { } nearest) offer.SetAt(nearest.At);
            }
            yield return new Candidate(offer, present ? 3 : 1, present);
        }
    }

    /// <summary>For a quest that brings its group in: per group <paramref name="t"/> can come in (its size N), the count N less the margin,
    /// kept when that makes <paramref name="rank"/> (judged against that group's size).</summary>
    static List<int> GroupCounts(Target t, int rank, List<string> templates, SettingsCopy set)
    {
        var counts = new SortedSet<int>();
        foreach (var id in templates)
        {
            // The count comes from the fewest the group can hold (whatever it rolls covers it, the margin kept); the difficulty from its
            // usual size (the fight you face).
            int least = Cat.TemplateMinSize.GetValueOrDefault(id, 0);
            double size = Cat.TemplateSize.GetValueOrDefault(id, 0);
            if (least < 1 || size < 1) continue;
            int n = Math.Max(1, least - (int)Math.Floor(least * set.CountMargin));
            if (Cat.QuestRank(t.Kind, Unit(t), n, size) == rank) counts.Add(n);
        }
        return [.. counts];
    }

    /// <summary>The counts of <paramref name="t"/> from 2 to <paramref name="cap"/> that make <paramref name="rank"/>; else a single one if that does
    /// (a strong target past the rank even at two: so low ranks get work too).</summary>
    static List<int> CountsFor(Target t, int rank, int cap)
    {
        var counts = new List<int>();
        for (int n = 2; n <= cap; n++)
            if (Cat.QuestRank(t.Kind, Unit(t), n, Typical(t)) == rank) counts.Add(n);
        if (counts.Count == 0 && cap >= 1 && Cat.QuestRank(t.Kind, Unit(t), 1, Typical(t)) == rank) counts.Add(1);
        return counts;
    }

    static double Unit(Target t) => t.Kind == Catalog.Cull ? Cat.UnitOf.GetValueOrDefault(t.Id, t.Unit) : t.Unit;

    static double Typical(Target t) => t.Kind == Catalog.Cull ? Cat.SizeOf.GetValueOrDefault(t.Id, 1) : Cat.PackOf.GetValueOrDefault(t.Id, 1);

    /// <summary>How many of <paramref name="standing"/> a quest may ask for (the margin: not every last one must be found).</summary>
    public static int Usable(int standing, SettingsCopy set) => (int)Math.Floor(standing * (1 - set.CountMargin));

    protected static Target? TargetOf(Quest q) => Cat.FindTarget(q.Kind, q.TargetId);

    public bool Accept(World world, GuildSystem guild, Quest q, Offer offer, Vector2 at)
    {
        var set = guild.Settings;
        if (TargetOf(q) is not { } t) return false;
        var survey = Survey.Of(world, guild.Access, Cat, at, set.TargetRange);
        int usable = Usable(survey.StandingOf(t), set);
        for (int i = 0; i < 4 && usable < q.Need && set.SpawnTargets; i++)
        {
            int added = guild.SpawnNear(world, q, t, survey);
            if (added <= 0) break;
            usable += added;
        }
        if (usable <= 0)
        {
            RadiantQuestsMod.Toast($"No {q.TargetName} are around any more. Pick another quest.");
            return false;
        }
        if (usable < q.Need)
        {
            RadiantQuestsMod.Toast($"Only {usable} {q.TargetName} could be found near enough: the quest asks for {usable}.");
            q.Need = usable;
        }
        return true;
    }

    public bool Counts(Quest q, PersonFacts person, string race, string familyHead) =>
        Kind == Catalog.Hunt ? q.TargetId == race : q.TargetId == familyHead;

    public TrackResult Track(World world, GuildSystem guild, Quest q, List<Vector2> markers)
    {
        var set = guild.Settings;
        var anchor = new Vector2(q.AnchorX, q.AnchorZ);
        var key = Survey.Key(Kind, q.TargetId);
        // The quest's groups: its own (tagged) ones, and groups of its target within the quest area (nearest first).
        var groups = guild.Access.Groups(world)
            .Where(g => g.Tag == q.Tag || (Vector2.Distance(g.At, anchor) <= set.TargetRange && Survey.KeysOf(Cat, g).Contains(key)))
            .OrderBy(g => Vector2.Distance(g.At, anchor))
            .ThenBy(g => g.Id)
            .ToList();
        var marked = groups.Where(g => g.Standing > 0 && !q.Skipped.Contains(g.Key)).ToList();
        int standing = marked.Sum(g => g.Standing);
        markers.AddRange(marked.Take(5).Select(g => g.At));

        // Knocked out by someone else (down, with no blow of yours): seen on the quest's groups that are loaded.
        foreach (var g in groups)
            foreach (var m in guild.Access.Members(world, g.Id))
                if (m.Down) guild.LostToOthers(world, m);

        // Failsafe: targets you stay near but can never reach (water, cliffs, stuck) are given up on; refills or the end below take over.
        if (q.Have < q.Need)
        {
            if (guild.OutOfReach(world, q, marked) is { } lost)
            {
                standing -= lost.Standing;
                markers.Remove(lost.At);
                q.LastRefill = -1;   // look for others at once
                RadiantQuestsMod.Toast($"{q.Describe()}: those {q.TargetName} can't be reached. Others will be marked if any are around.");
            }
        }
        int still = q.Need - q.Have;
        // Its brought-in group was beaten by someone else before you fought: called off, no penalty (the group is cleared up later).
        if (q.Spawned && q.Have == 0 && !groups.Any(g => g.Tag == q.Tag && g.Standing > 0))
        {
            RadiantQuestsMod.Toast($"The {q.TargetName} for \"{q.Describe()}\" were beaten by someone else before you got there.");
            return TrackResult.CallOff;
        }
        if (still <= 0 || standing >= still) return TrackResult.Going;

        // The quest can't be finished with what is around (his rules, 2026-11-23). In order:
        //   1. other real targets nearby are already marked and counted in "standing" above;
        //   2. close enough: what can't be found is within the margin (25% of the count, at least 1): the count drops to what there is,
        //      full reward;
        //   3. a top-up spawn, only with a group that FITS what is missing (no bigger than the shortfall, +1 for E and D; so never
        //      harder than the part that is missing), a few times, at most once a game hour;
        //   4. nothing fits: the count drops to what you did plus what is still around; nothing done and nothing around: called off,
        //      no penalty. (The pay leaves out only targets someone else beat: Quest.PayShare.)
        // Never in the middle of a fight: those just knocked down are counted a moment later.
        if (guild.Fighting(world, q)) return TrackResult.Going;
        int missing = still - standing;
        int grace = Math.Max(1, (int)Math.Floor(q.Need * set.CountMargin));
        if (missing <= grace) return Shorten(q, standing, $"{missing} {q.TargetName} can't be found; that's close enough");
        if (set.SpawnTargets && q.Refills < 4 && TargetOf(q) is { } t)
        {
            if (world.GameHours - q.LastRefill < 1) return TrackResult.Going;   // tried within the hour: wait
            q.LastRefill = world.GameHours;
            int fits = q.Rank <= 1 ? missing + 1 : missing;
            var survey = Survey.Of(world, guild.Access, Cat, anchor, set.TargetRange);
            int added = guild.SpawnNear(world, q, t, survey, fits);
            RadiantQuestsMod.Detail($"quest #{q.Id} short by {missing}: top-up with a group of at most {fits}: {(added > 0 ? $"{added} brought in" : "none fits")}");
            if (added > 0)
            {
                q.Refills++;
                RadiantQuestsMod.Toast($"More {q.TargetName} were seen near {guild.TownName(q.Site)}. They're marked on your map.");
                return TrackResult.Going;
            }
        }
        if (q.Have == 0 && standing == 0) return TrackResult.CallOff;
        return Shorten(q, standing, $"no more {q.TargetName} can be found");
    }

    /// <summary>Lowers the count to what you did plus what still stands: done at once when nothing stands.</summary>
    static TrackResult Shorten(Quest q, int standing, string why)
    {
        q.Need = Math.Max(1, q.Have + standing);
        if (q.Have >= q.Need)
        {
            q.Need = q.Have;
            RadiantQuestsMod.Toast($"{q.Describe()}: {why}, so what you did counts. Hand it in.");
            return TrackResult.Done;
        }
        RadiantQuestsMod.Toast($"{q.Describe()}: {why}. The quest now asks for {q.Need}.");
        return TrackResult.Going;
    }

    public void End(World world, GuildSystem guild, Quest q) => guild.RemoveSpawned(world, q);
}

/// <summary>"Hunt N &lt;animal&gt;" (General tab): wild animals of a race, from any group.</summary>
public sealed class HuntQuest : CountQuest
{
    public override string Kind => Catalog.Hunt;

    public override bool Offered(SettingsCopy set, Tab tab) => set.Hunts && tab == Tab.General;

    protected override IEnumerable<Target> Targets(BoardContext ctx) => Cat.Hunts;
}

/// <summary>"Defeat N &lt;faction&gt;": General tab - outlaws; Faction tab - the town owner's enemies now.</summary>
public sealed class DefeatQuest : CountQuest
{
    public override string Kind => Catalog.Cull;

    public override bool Offered(SettingsCopy set, Tab tab) => tab == Tab.General ? set.Culls : set.FactionQuests;

    protected override IEnumerable<Target> Targets(BoardContext ctx) => ctx.Tab == Tab.General
        ? Cat.Culls.Where(t => ctx.Guild.GeneralCull(t, ctx.Giver))
        : Cat.WarTargets.Where(t => ctx.Guild.FactionEnemy(t, ctx.Giver));
}

/// <summary>"Wipe out a group of N &lt;faction&gt; (M needed)": one real group near the town, done at the set share defeated.</summary>
public sealed class GroupQuest : IQuestType
{
    public const string KindId = "group";

    public string Kind => KindId;

    static Catalog Cat => RadiantQuestsMod.Catalog;

    public bool Offered(SettingsCopy set, Tab tab) => set.GroupQuests && (tab == Tab.General ? set.Culls : set.FactionQuests);

    public IEnumerable<Candidate> Candidates(BoardContext ctx, int rank)
    {
        var allowed = ctx.Tab == Tab.General
            ? Cat.Culls.Where(t => ctx.Guild.GeneralCull(t, ctx.Giver))
            : Cat.WarTargets.Where(t => ctx.Guild.FactionEnemy(t, ctx.Giver));
        var byId = allowed.ToDictionary(t => t.Id, StringComparer.Ordinal);
        foreach (var g in ctx.Survey.Groups)
        {
            if (g.ModSpawned || g.Tag.Length > 0 || g.Standing < 3 || g.FactionId is not { } f || !byId.TryGetValue(Cat.Head(f), out var t)) continue;
            if (Cat.GroupRank(Cat.UnitOf.GetValueOrDefault(t.Id, t.Unit), g.Standing) != rank) continue;
            var offer = new Offer
            {
                Kind = Kind, TargetId = t.Id, TargetName = t.Name.Trim(), Rank = rank, Group = true, GroupSize = g.Standing, PlatoonKey = g.Key,
                Score = Catalog.Fight(Cat.UnitOf.GetValueOrDefault(t.Id, t.Unit), g.Standing), HasAt = true, AtX = g.At.X, AtZ = g.At.Y,
                Need = Math.Max(1, (int)Math.Ceiling(g.Standing * ctx.Guild.Settings.GroupShare)),
            };
            yield return new Candidate(offer, 2);
        }
    }

    public bool Accept(World world, GuildSystem guild, Quest q, Offer offer, Vector2 at)
    {
        if (guild.Access.TagGroup(world, offer.PlatoonKey, q.Tag)) return true;
        RadiantQuestsMod.Toast("That group has moved on or been broken since the board was put up. Pick another quest.");
        return false;
    }

    public bool Counts(Quest q, PersonFacts person, string race, string familyHead) => person.GroupTag == q.Tag;

    public TrackResult Track(World world, GuildSystem guild, Quest q, List<Vector2> markers)
    {
        var g = guild.Access.Groups(world).FirstOrDefault(x => x.Tag == q.Tag);
        if (g is not null && g.Standing > 0 && !q.Skipped.Contains(g.Key))
        {
            // Failsafe: a group you stay near but can never reach ends the quest (counted if you fought it, else no penalty).
            if (guild.OutOfReach(world, q, [g]) is null)
            {
                markers.Add(g.At);
                return TrackResult.Going;
            }
            RadiantQuestsMod.Toast($"The group for \"{q.Describe()}\" can't be reached.");
        }
        // The group is broken (gone, or nobody left standing): done if your side fought it, else called off with no penalty.
        if (q.Done) return TrackResult.Going;
        if (q.Hit)
        {
            q.Have = q.Need;
            RadiantQuestsMod.Toast($"{q.Describe()}: the group is broken. Hand it in at a guild clerk.");
            return TrackResult.Done;
        }
        RadiantQuestsMod.Toast($"The group for \"{q.Describe()}\" broke up before you fought it: the quest is called off, no penalty.");
        return TrackResult.CallOff;
    }

    public void End(World world, GuildSystem guild, Quest q) => guild.RemoveSpawned(world, q);
}
