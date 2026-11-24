// Radiant Quests for Meitou. GPLv3 or later.
// The local survey: what lives around one town right now. Boards are built from it, so a town only offers work that fits its surroundings
// (skimmers near Sho-Battai, fogmen near Mongrel, starving bandits only where they roam), and quests never ask for more than is really there.
//   - groups within the local range, with how many of each target are STANDING (down, dead, carried or caged members don't count);
//   - the regions around the town (sampled on a grid), so a target that naturally spawns there can be offered and spawned locally.
using System.Numerics;
using Meitou.Simulation;

namespace RadiantQuests;

public sealed class Survey
{
    /// <summary>Standing members per target key ("hunt:" + race, "cull:" + faction family head).</summary>
    public Dictionary<string, int> Standing { get; } = new(StringComparer.Ordinal);

    /// <summary>The groups within range (nearest first).</summary>
    public List<GroupInfo> Groups { get; } = [];

    /// <summary>The regions (BIOME_GROUP ids) around the town.</summary>
    public HashSet<string> Regions { get; } = new(StringComparer.Ordinal);

    public Vector2 At { get; }

    public float Range { get; }

    Survey(Vector2 at, float range)
    {
        At = at;
        Range = range;
    }

    public static string Key(string kind, string id) => (kind == Catalog.Hunt ? Catalog.Hunt : Catalog.Cull) + ":" + id;

    /// <summary>Surveys the surroundings of <paramref name="at"/> within <paramref name="range"/>.</summary>
    public static Survey Of(World world, WorldAccess wa, Catalog cat, Vector2 at, float range)
    {
        var s = new Survey(at, range);
        foreach (var g in wa.Groups(world).Where(g => Vector2.Distance(g.At, at) <= range).OrderBy(g => Vector2.Distance(g.At, at)).ThenBy(g => g.Id))
        {
            s.Groups.Add(g);
            foreach (var key in KeysOf(cat, g)) s.Standing[key] = s.Standing.GetValueOrDefault(key) + g.Standing;
        }
        // The regions around the town: a 9 x 9 grid over the range.
        for (int i = -4; i <= 4; i++)
            for (int j = -4; j <= 4; j++)
                if (wa.Region(at + new Vector2(i, j) * (range / 4)) is { } r) s.Regions.Add(r);
        return s;
    }

    /// <summary>The target keys a group counts for: its faction's family head, and the animal races it holds.</summary>
    public static IEnumerable<string> KeysOf(Catalog cat, GroupInfo g)
    {
        if (g.FactionId is { } f) yield return Key(Catalog.Cull, cat.Head(f));
        if (cat.TemplateRaces.TryGetValue(g.TemplateId, out var races))
            foreach (var r in races) yield return Key(Catalog.Hunt, r);
    }

    /// <summary>How many of <paramref name="t"/> stand within range now.</summary>
    public int StandingOf(Target t) => Standing.GetValueOrDefault(Key(t.Kind, t.Id));

    /// <summary>Whether <paramref name="t"/> naturally spawns in a region around the town (so a quest may spawn more of it here).</summary>
    public bool SpawnsHere(Catalog cat, Target t) =>
        (t.Kind == Catalog.Hunt ? cat.HuntTemplates.GetValueOrDefault(t.Id, []) : cat.CullTemplates.GetValueOrDefault(t.Id, []))
        .Any(id => cat.TemplateRegions.TryGetValue(id, out var regions) && regions.Overlaps(Regions));

    /// <summary>Whether <paramref name="t"/> naturally spawns anywhere (an out-of-region quest can bring it in).</summary>
    public static bool SpawnsAnywhere(Catalog cat, Target t) => AnyTemplates(cat, t).Count > 0;

    /// <summary>
    /// The templates of <paramref name="t"/> an out-of-region quest may bring in: every one that passed the catalog's broken-group checks,
    /// with spawn regions or not (his rule, 2026-11-23: modded creatures with no spawn region still get a chance, through this rare roll only;
    /// they are placed in a wild region).
    /// </summary>
    public static List<string> AnyTemplates(Catalog cat, Target t) =>
        [.. (t.Kind == Catalog.Hunt ? cat.HuntTemplates.GetValueOrDefault(t.Id, []) : cat.CullTemplates.GetValueOrDefault(t.Id, []))
            .Where(id => cat.TemplateMaxSize.GetValueOrDefault(id, 0) > 0)];

    /// <summary>The templates of <paramref name="t"/> that naturally spawn around the town.</summary>
    public List<string> LocalTemplates(Catalog cat, Target t) =>
        [.. (t.Kind == Catalog.Hunt ? cat.HuntTemplates.GetValueOrDefault(t.Id, []) : cat.CullTemplates.GetValueOrDefault(t.Id, []))
            .Where(id => cat.TemplateRegions.TryGetValue(id, out var regions) && regions.Overlaps(Regions))];
}
