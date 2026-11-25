// Radiant Quests for Meitou. GPLv3 or later.
// The fighting quest types: "Hunt N <animal>", "Defeat N <faction>" (any group counts) and "Wipe out a group" (one real group).
//
// Reliability rules shared by them (his rules, 2026-11-23):
//   - a hunt or defeat is about ONE whole group: a real one around the town (tagged when taken, so it stays marked wherever it wanders), or
//     one the mod brings in when taken. The count is the WHOLE group (his rule, 2026-11-23: no grace margin any more; the failsafes below
//     cover lost targets), so you never arrive to find twice what the board said; at most 15 (bigger crowds are "wipe out the group" work);
//   - its difficulty is the whole group you face, with the animals' real ages (pups are weaker, elders stronger);
//   - a brought-in group is spawned at the planned size where Meitou can cap it; where it can't and the group comes out bigger, the count
//     and the pay follow the real group (and a group past the rank is passed over for another first);
//   - brought-in animals get adult ages if their real ages would put the quest at another rank;
//   - while running, if the marked groups can't cover what's still needed: a top-up group that fits what is missing is tried first, else
//     the count drops to what can be done (full pay); nothing done and nothing left: called off, no penalty. Targets someone
//     else beat (seen, not guessed) are not paid for (Quest.PayShare).
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

    /// <summary>How near one of yours a group of the target must be to be marked and counted, outside the quest area (you followed them).</summary>
    const float NearYou = 2500;

    public IEnumerable<Candidate> Candidates(BoardContext ctx, int rank)
    {
        var set = ctx.Guild.Settings;
        foreach (var t in Targets(ctx))
        {
            var key = Survey.Key(t.Kind, t.Id);
            double unit = Unit(t);
            // A real group around the town that makes this rank, whole (nearest first).
            GroupInfo? real = null;
            double realScore = 0;
            foreach (var g in ctx.Survey.Groups)
            {
                if (g.Standing <= 0 || g.Standing > Catalog.MostCount || g.Tag.Length > 0 || g.ModSpawned || !Survey.KeysOf(Cat, g).Contains(key)) continue;
                double score = Catalog.Fight(unit * Strength(t, g), g.Standing);
                if (Difficulty.RankOf(score) != rank) continue;
                real = g;
                realScore = score;
                break;
            }
            if (real is not null)
            {
                var offer = new Offer { Kind = Kind, TargetId = t.Id, TargetName = t.Name.Trim(), Rank = rank, Need = Keep(real.Standing, set),
                    GroupSize = real.Standing, PlatoonKey = real.Key, Score = realScore, Age = t.Kind == Catalog.Hunt ? real.Age : 1 };
                offer.SetAt(real.At);
                yield return new Candidate(offer, 3, true);
                continue;
            }
            // Else a group the mod can bring in when the quest is taken (from the town's regions; now and then from anywhere).
            bool spawns = set.SpawnTargets && ctx.Survey.SpawnsHere(Cat, t);
            bool stray = !spawns && ctx.Survey.StandingOf(t) == 0 && set.SpawnTargets && set.StrayChance > 0 && Survey.SpawnsAnywhere(Cat, t);
            if (!spawns && !stray) continue;
            var templates = spawns ? ctx.Survey.LocalTemplates(Cat, t) : Survey.AnyTemplates(Cat, t);
            var sizes = BroughtSizes(t, unit, rank, templates, 1, Catalog.MostCount);
            // Strays (his idea, 2026-11-23): at E and D, a few animals wandered off from a pack (2-4, smaller than any whole pack of theirs),
            // so pack animals too strong in a herd still give low-rank work. Needs Meitou's spawn cap (a part of a pack can't be made without it).
            var strays = rank <= 1 && WorldAccess.CanCap ? StraySizes(t, unit, rank, templates).Except(sizes).ToList() : [];
            if (sizes.Count + strays.Count == 0) continue;
            // A random size among those that fit (his rule: lots of variety); rolled with the board, so a reload shows the same.
            int pick = ctx.Dice.Int(0, sizes.Count + strays.Count - 1);
            bool isStrays = pick >= sizes.Count;
            int size = isStrays ? strays[pick - sizes.Count] : sizes[pick];
            yield return new Candidate(new Offer { Kind = Kind, TargetId = t.Id, TargetName = t.Name.Trim(), Rank = rank,
                Need = isStrays ? size : Keep(size, set), GroupSize = size, Score = Catalog.Fight(unit, size), Stray = !spawns, Strays = isStrays }, 1, false);
        }
    }

    /// <summary>
    /// The sizes (between <paramref name="min"/> and <paramref name="max"/>) a brought-in group of <paramref name="t"/> may come at that make
    /// <paramref name="rank"/> (whole groups, adults): every size in any of its templates' spawn ranges (Catalog.SpawnRange).
    /// </summary>
    public static List<int> BroughtSizes(Target t, double unit, int rank, List<string> templates, int min, int max)
    {
        var sizes = new SortedSet<int>();
        foreach (var id in templates)
        {
            var (lo, hi) = Cat.SpawnRange(id, t.Kind == Catalog.Hunt);
            for (int n = Math.Max(lo, min); n <= Math.Min(hi, max); n++)
                if (Difficulty.RankOf(Catalog.Fight(unit, n)) == rank) sizes.Add(n);
        }
        return [.. sizes];
    }

    /// <summary>
    /// Stray sizes for a pack animal at <paramref name="rank"/>: 2 up to 4, below the smallest whole pack of that template (its spawn range's
    /// low end), when the difficulty fits. Never for people or animals that live alone.
    /// </summary>
    static List<int> StraySizes(Target t, double unit, int rank, List<string> templates)
    {
        var sizes = new SortedSet<int>();
        if (t.Kind != Catalog.Hunt) return [];
        foreach (var id in templates)
        {
            if (Cat.TemplateSize.GetValueOrDefault(id, 0) <= 2) continue;
            var (lo, _) = Cat.SpawnRange(id, true);
            for (int n = 2; n <= Math.Min(4, lo - 1); n++)
                if (Difficulty.RankOf(Catalog.Fight(unit, n)) == rank) sizes.Add(n);
        }
        return [.. sizes];
    }

    /// <summary>How many of a group of <paramref name="size"/> a quest asks for: all but the margin (25%: 6 asks for 5, 8 for 6), at least 1.</summary>
    public static int Keep(int size, SettingsCopy set) => Math.Max(1, size);

    /// <summary>A group's strength from its animals' ages (hunts); people are as the data says.</summary>
    static double Strength(Target t, GroupInfo g) => t.Kind == Catalog.Hunt ? g.Strength : 1;

    public static double Unit(Target t) => t.Kind == Catalog.Cull ? Cat.UnitOf.GetValueOrDefault(t.Id, t.Unit) : t.Unit;

    /// <summary>How many of <paramref name="standing"/> a quest may ask for (the margin: not every last one must be found).</summary>
    public static int Usable(int standing, SettingsCopy set) => standing;

    protected static Target? TargetOf(Quest q) => Cat.FindTarget(q.Kind, q.TargetId);

    public bool Accept(World world, GuildSystem guild, Quest q, Offer offer, Vector2 at)
    {
        var set = guild.Settings;
        if (TargetOf(q) is not { } t) return false;
        // A real group: tagged for the quest, so it stays marked wherever it wanders.
        if (offer.PlatoonKey != 0 && guild.Access.TagGroup(world, offer.PlatoonKey, q.Tag)) return true;
        var survey = Survey.Of(world, guild.Access, Cat, at, set.TargetRange);
        int planned = offer.GroupSize > 0 ? offer.GroupSize : q.Need;
        // Bring a group in. One that came out bigger than the rank (a Meitou that can't cap a spawn) is passed over for another; the last try
        // is kept as it is, its count and pay fitted to it.
        for (int attempt = 0; attempt < 3 && set.SpawnTargets; attempt++)
        {
            if (guild.SpawnNear(world, q, t, survey, want: planned) is not { } g) break;
            if (g.Size == planned) return true;
            double score = Catalog.Fight(Unit(t), g.Size);
            // Past the rank, or (his report: "Hunt 2 Goat" became 1) smaller than planned: another try first.
            if ((Difficulty.RankOf(score) > q.Rank || g.Size < planned) && attempt < 2)
            {
                q.Skipped.Add(g.Key);   // not needed: cleared away once you are not near
                RadiantQuestsMod.Detail($"quest #{q.Id}: brought-in group of {g.Size} is past rank {Ranks.Name(q.Rank)}, trying another");
                continue;
            }
            Refit(q, g.Size, score, set);
            RadiantQuestsMod.Toast($"The group is {g.Size} strong: the quest asks for {q.Need}, pay {q.Cats} cats.");
            return true;
        }
        // Nothing could be brought in: the real ones around, if any.
        int usable = Keep(survey.StandingOf(t), set);
        if (survey.StandingOf(t) <= 0)
        {
            RadiantQuestsMod.Toast($"No {q.TargetName} are around any more. Pick another quest.");
            return false;
        }
        if (usable < q.Need)
        {
            RadiantQuestsMod.Toast($"Only {survey.StandingOf(t)} {q.TargetName} are near: the quest asks for {usable}.");
            Refit(q, survey.StandingOf(t), Catalog.Fight(Unit(t), survey.StandingOf(t)), set);
        }
        return true;
    }

    /// <summary>Fits a quest to a group of <paramref name="size"/> (score <paramref name="score"/>): its count, and the pay by the change in difficulty.</summary>
    static void Refit(Quest q, int size, double score, SettingsCopy set)
    {
        if (q.Score > 0 && score > 0)
        {
            q.Cats = (long)Math.Round(q.Cats * Difficulty.CatsAt(score, q.Rank) / Difficulty.CatsAt(q.Score, q.Rank) / 10) * 10;
            q.Rep = (int)Math.Round(q.Rep * Difficulty.RepAt(score, q.Rank) / Difficulty.RepAt(q.Score, q.Rank));
        }
        q.Need = q.Strays ? size : Keep(size, set);
        q.GroupSize = size;
        q.Score = score;
    }

    public bool Counts(Quest q, PersonFacts person, string race, string familyHead) =>
        IsLoner(q) ? person.GroupTag == q.Tag
        : Kind == Catalog.Hunt ? q.TargetId == race : q.TargetId == familyHead;

    /// <summary>
    /// "Defeat a Dust Bandit Loner": one person alone (his rule, 2026-11-24): only that person counts and is marked, never a member of
    /// another group of the faction; beaten by someone else first, the quest is called off.
    /// </summary>
    public static bool IsLoner(Quest q) => q.Kind == Catalog.Cull && q.Need == 1 && q.GroupSize == 1;

    public TrackResult Track(World world, GuildSystem guild, Quest q, List<Vector2> markers)
    {
        var set = guild.Settings;
        var anchor = new Vector2(q.AnchorX, q.AnchorZ);
        var key = Survey.Key(Kind, q.TargetId);
        // The quest's groups: its own (tagged) ones wherever they are, and groups of its target within the quest area or near one of yours
        // (you followed them out; any of them counts), nearest the area first.
        var groups = guild.Access.Groups(world)
            .Where(g => g.Tag == q.Tag || (g.Tag.Length == 0 && Survey.KeysOf(Cat, g).Contains(key)
                && (Vector2.Distance(g.At, anchor) <= set.TargetRange || guild.Access.YoursNearPoint(world, g.At, NearYou))))
            .OrderBy(g => g.Tag == q.Tag ? 0 : 1)
            .ThenBy(g => Vector2.Distance(g.At, anchor))
            .ThenBy(g => g.Id)
            .ToList();
        if (IsLoner(q))
        {
            if (q.Done) return TrackResult.Going;
            var loner = groups.FirstOrDefault(g => g.Tag == q.Tag && !q.Skipped.Contains(g.Key));
            if (loner is { Standing: > 0 })
            {
                markers.Add(loner.At);
                return TrackResult.Going;
            }
            if (guild.Fighting(world, q)) return TrackResult.Going;
            RadiantQuestsMod.Toast($"Someone else got to the {Quest.LonerName(q.TargetName)} before you. Called off, no penalty.");
            return TrackResult.CallOff;
        }
        var marked = groups.Where(g => g.Standing > 0 && !q.Skipped.Contains(g.Key)).ToList();
        int standing = marked.Sum(g => g.Standing);
        // The map shows the quest's own group; other groups of the kind (any of them counts) only when its own can't cover what is still
        // needed, so a loner quest isn't five markers (his worry, 2026-11-23: clutter).
        var own = marked.Where(g => g.Tag == q.Tag).ToList();
        markers.AddRange((own.Sum(g => g.Standing) >= q.Need - q.Have && own.Count > 0 ? own : marked).Take(5).Select(g => g.At));

        // Knocked out by someone else (down, with no blow of yours): seen on the quest's groups that are loaded.
        foreach (var g in groups)
            foreach (var m in guild.Access.Members(world, g.Id))
                if (m.Down) guild.LostToOthers(world, m);

        // Brought-in animals whose ages would put the quest at another rank are made adults (the rank was set for adults), once per group.
        if (Kind == Catalog.Hunt && TargetOf(q) is { } target)
            foreach (var g in groups.Where(g => g.Tag == q.Tag && g.ModSpawned && !q.Aged.Contains(g.Key) && guild.Access.Loaded(world, g.Id)))
            {
                q.Aged.Add(g.Key);
                if (Difficulty.RankOf(Catalog.Fight(target.Unit * g.Strength, g.Standing)) == Difficulty.RankOf(Catalog.Fight(target.Unit, g.Standing))) continue;
                int set1 = guild.Access.SetAges(world, g.Id, 1f);
                RadiantQuestsMod.Detail($"quest #{q.Id}: brought-in {g.TemplateId} (mean age {g.Age:0.00}) made adult ({set1} animals)");
            }

        // Failsafe: targets you stay near but can never reach (water, cliffs, stuck) are given up on; refills or the end below take over.
        if (q.Have < q.Need)
        {
            if (guild.OutOfReach(world, q, marked) is { } lost)
            {
                standing -= lost.Standing;
                markers.Remove(lost.At);
                q.LastRefill = -1;   // look for others at once
                RadiantQuestsMod.Toast($"Those {q.TargetName} can't be reached. Others will be marked if any are around.");
            }
        }
        int still = q.Need - q.Have;
        // Its brought-in group was beaten by someone else before you fought: called off, no penalty (the group is cleared up later).
        // (A group given up on - broken or out of reach - is not "beaten": the shortfall below brings a replacement.)
        if (q.Spawned && q.Have == 0 && groups.Any(g => g.Tag == q.Tag && g.Standing == 0 && !q.Skipped.Contains(g.Key))
            && !groups.Any(g => g.Tag == q.Tag && g.Standing > 0 && !q.Skipped.Contains(g.Key)))
        {
            RadiantQuestsMod.Toast($"Someone else beat the {q.TargetName} before you got there.");
            return TrackResult.CallOff;
        }
        if (still <= 0 || standing >= still) return TrackResult.Going;

        // The quest can't be finished with what is around (his rules, 2026-11-23). In order:
        //   1. other real targets nearby are already marked and counted in "standing" above;
        //   2. a top-up group that FITS what is missing (no bigger than the shortfall, +1 for E and D; so never harder than the part that is
        //      missing), a few times, at most once a game hour;
        //   3. nothing could come: the count drops to what you did plus what is still around, full pay; nothing done and nothing around:
        //      called off, no penalty. (The pay leaves out only targets someone else beat.)
        // Never in the middle of a fight: those just knocked down are counted a moment later.
        if (guild.Fighting(world, q)) return TrackResult.Going;
        int missing = still - standing;
        // (Replacements of broken spawns don't use up the top-ups: up to Quest.MostRetries of them on top.)
        if (set.SpawnTargets && q.Refills < 4 + Math.Min(q.Retries, Quest.MostRetries) && TargetOf(q) is { } t)
        {
            if (world.GameHours - q.LastRefill < 1) return TrackResult.Going;   // tried within the hour: wait
            q.LastRefill = world.GameHours;
            // What is missing (only that is needed), but never a lone pack animal: at least 2 of a kind that lives in groups, sometimes one
            // more (his rule, 2026-11-23). Animals that live alone or in pairs, and people, come as exactly what is missing.
            int fits = missing;
            if (t.Kind == Catalog.Hunt && Cat.PackOf.GetValueOrDefault(t.Id, 1) > 2)
            {
                var dice = new Dice(world.Seed ^ Rng.Mix((ulong)q.Id * 0x9E3779B97F4A7C15UL + (ulong)q.Refills + 0x7470UL));
                fits = Math.Max(2, missing) + (dice.Chance(0.5) ? 1 : 0);
            }
            // A broken brought-in group (nothing done yet) is replaced whole, as the board described it, not just by the count.
            if (q.Retries > 0 && q.Have == 0 && q.GroupSize > fits) fits = q.GroupSize;
            var survey = Survey.Of(world, guild.Access, Cat, anchor, set.TargetRange);
            var added = guild.SpawnNear(world, q, t, survey, most: fits, want: fits);
            RadiantQuestsMod.Detail($"quest #{q.Id} short by {missing}: top-up with a group of {fits}: {(added is { } a ? $"{a.Size} brought in" : "none fits")}");
            if (added is not null)
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
            RadiantQuestsMod.Toast($"{q.TargetName}: {why}. What you did counts: hand it in.");
            return TrackResult.Done;
        }
        RadiantQuestsMod.Toast($"{q.TargetName}: {why}. The quest now asks for {q.Need}.");
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

/// <summary>
/// "Wipe out a group of N &lt;target&gt;": one group, done when all of it is defeated (his rule, 2026-11-23: no share any more). A real group near the town (people of 3 or more;
/// animal herds only when too big for a hunt, over 15), or one brought in when taken (his rule, 2026-11-23: any size from 3, by the same
/// size rules as the other fights; animals only above 15).
/// </summary>
public sealed class GroupQuest : IQuestType
{
    public const string KindId = "group";

    public string Kind => KindId;

    static Catalog Cat => RadiantQuestsMod.Catalog;

    public bool Offered(SettingsCopy set, Tab tab) => set.GroupQuests && (tab == Tab.General ? set.Culls || set.Hunts : set.FactionQuests);

    /// <summary>The target a group quest is about: a faction's people, or (General tab) an animal race.</summary>
    static Target? TargetOf(string id) => Cat.FindTarget(Catalog.Cull, id) ?? Cat.FindTarget(Catalog.Hunt, id);

    public IEnumerable<Candidate> Candidates(BoardContext ctx, int rank)
    {
        var set = ctx.Guild.Settings;
        var people = new List<Target>();
        if (ctx.Tab == Tab.Faction) people.AddRange(Cat.WarTargets.Where(t => ctx.Guild.FactionEnemy(t, ctx.Giver)));
        else if (set.Culls) people.AddRange(Cat.Culls.Where(t => ctx.Guild.GeneralCull(t, ctx.Giver)));
        var animals = new List<Target>();
        if (ctx.Tab == Tab.General && set.Hunts) animals.AddRange(Cat.Hunts);
        var byId = people.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        // Real groups first.
        foreach (var g in ctx.Survey.Groups)
        {
            if (g.ModSpawned || g.Tag.Length > 0 || g.Standing < 3) continue;
            Target? t = g.FactionId is { } f && byId.TryGetValue(Cat.Head(f), out var person) ? person : null;
            if (t is null && g.Standing > Catalog.MostCount)
                t = animals.FirstOrDefault(a => Survey.KeysOf(Cat, g).Contains(Survey.Key(Catalog.Hunt, a.Id)));
            if (t is null) continue;
            double score = Catalog.Fight(CountQuest.Unit(t) * (t.Kind == Catalog.Hunt ? g.Strength : 1), g.Standing);
            if (Difficulty.RankOf(score) != rank) continue;
            used.Add(t.Id);
            yield return new Candidate(new Offer
            {
                Kind = Kind, TargetId = t.Id, TargetName = t.Name.Trim(), Rank = rank, Group = true, GroupSize = g.Standing, PlatoonKey = g.Key,
                Score = score, HasAt = true, AtX = g.At.X, AtZ = g.At.Y, Need = Share(g.Standing, set),
            }, 2);
        }
        // Brought in: per target with none real at this rank, a random size that fits.
        if (!set.SpawnTargets) yield break;
        foreach (var t in people.Concat(animals))
        {
            if (used.Contains(t.Id)) continue;
            bool spawns = ctx.Survey.SpawnsHere(Cat, t);
            bool stray = !spawns && ctx.Survey.StandingOf(t) == 0 && set.StrayChance > 0 && Survey.SpawnsAnywhere(Cat, t);
            if (!spawns && !stray) continue;
            int min = t.Kind == Catalog.Hunt ? Catalog.MostCount + 1 : 3;
            var sizes = CountQuest.BroughtSizes(t, CountQuest.Unit(t), rank, spawns ? ctx.Survey.LocalTemplates(Cat, t) : Survey.AnyTemplates(Cat, t),
                min, int.MaxValue);
            if (sizes.Count == 0) continue;
            int size = sizes[ctx.Dice.Int(0, sizes.Count - 1)];
            yield return new Candidate(new Offer
            {
                Kind = Kind, TargetId = t.Id, TargetName = t.Name.Trim(), Rank = rank, Group = true, GroupSize = size,
                Score = Catalog.Fight(CountQuest.Unit(t), size), Need = Share(size, set), Stray = !spawns,
            }, 1, false);
        }
    }

    /// <summary>How many of a group of <paramref name="size"/> must fall (the set share, rounded up).</summary>
    static int Share(int size, SettingsCopy set) => Math.Max(1, size);

    public bool Accept(World world, GuildSystem guild, Quest q, Offer offer, Vector2 at)
    {
        if (offer.PlatoonKey != 0)
        {
            if (guild.Access.TagGroup(world, offer.PlatoonKey, q.Tag)) return true;
            RadiantQuestsMod.Toast("That group has moved on or been broken since the board was put up. Pick another quest.");
            return false;
        }
        // Brought in: one group at the planned size (the cap), its count and pay fitted to what really came.
        if (TargetOf(q.TargetId) is not { } t) return false;
        var survey = Survey.Of(world, guild.Access, Cat, at, guild.Settings.TargetRange);
        if (guild.SpawnNear(world, q, t, survey, want: offer.GroupSize) is not { } g)
        {
            RadiantQuestsMod.Toast($"No {q.TargetName} group could be found. Pick another quest.");
            return false;
        }
        if (g.Size != q.GroupSize)
        {
            double score = Catalog.Fight(CountQuest.Unit(t), g.Size);
            if (q.Score > 0)
            {
                q.Cats = (long)Math.Round(q.Cats * Difficulty.CatsAt(score, q.Rank) / Difficulty.CatsAt(q.Score, q.Rank) / 10) * 10;
                q.Rep = (int)Math.Round(q.Rep * Difficulty.RepAt(score, q.Rank) / Difficulty.RepAt(q.Score, q.Rank));
            }
            q.GroupSize = g.Size;
            q.Need = Share(g.Size, guild.Settings);
            q.Score = score;
            RadiantQuestsMod.Toast($"The group is {g.Size} strong: {q.Need} must fall, pay {q.Cats} cats.");
        }
        return true;
    }

    public bool Counts(Quest q, PersonFacts person, string race, string familyHead) => person.GroupTag == q.Tag;

    public TrackResult Track(World world, GuildSystem guild, Quest q, List<Vector2> markers)
    {
        var g = guild.Access.Groups(world).FirstOrDefault(x => x.Tag == q.Tag && !q.Skipped.Contains(x.Key));
        // Its brought-in group came out broken (the first-spawn check gave it up): a replacement at once, the same group at a new spot first
        // (GuildSystem.SpawnNear), at most Quest.MostRetries times.
        if (g is null && q.Spawned && !q.Hit && q.Retries > 0 && q.Refills < Math.Min(q.Retries, Quest.MostRetries) && TargetOf(q.TargetId) is { } t)
        {
            q.Refills++;
            var survey = Survey.Of(world, guild.Access, Cat, new Vector2(q.AnchorX, q.AnchorZ), guild.Settings.TargetRange);
            if (guild.SpawnNear(world, q, t, survey, want: q.GroupSize) is { } again)
            {
                if (again.Size != q.GroupSize)
                {
                    q.GroupSize = again.Size;
                    q.Need = Share(again.Size, guild.Settings);
                }
                RadiantQuestsMod.Toast($"The {q.TargetName} group was seen elsewhere near {guild.TownName(q.Site)}. It's marked on your map.");
                return TrackResult.Going;
            }
        }
        if (g is not null && g.Standing > 0)
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
        // Broken with your side in the fight: what YOU defeated counts (his report, 2026-11-24: 1 of 9 beaten by you, the rest by Dust
        // Bandits, was paid as 9/9). The count drops to yours and the pay with it; none of yours: called off, no penalty.
        if (q.Hit && q.Have > 0)
        {
            if (q.Have < q.Need)
            {
                q.Cats = (long)Math.Round(q.Cats * (double)q.Have / q.Need / 10) * 10;
                q.Rep = (int)Math.Round(q.Rep * (double)q.Have / q.Need);
                RadiantQuestsMod.Toast($"{q.Describe()}: the group is broken; you defeated {q.Have} of {q.Need}, others the rest. Paid for yours.");
                q.Need = q.Have;
                q.FullNeed = q.Have;
            }
            else RadiantQuestsMod.Toast($"{q.Describe()}: the group is broken. Hand it in at a guild clerk.");
            return TrackResult.Done;
        }
        RadiantQuestsMod.Toast($"The group for \"{q.Describe()}\" broke up before you fought it: the quest is called off, no penalty.");
        return TrackResult.CallOff;
    }

    public void End(World world, GuildSystem guild, Quest q) => guild.RemoveSpawned(world, q);
}
