// Radiant Quests for Meitou. GPLv3 or later.
// The engine's part of the item framework (his design, 2026-11-24), shared by every item quest type: the sale watch (one of yours selling an
// item a quest cares about), where an item is sold (the towns now on the map whose shops stock it), and an ambush along a route (a hostile
// group brought in near a point, out of sight, for the harder courier work).
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Trading;

namespace RadiantQuests;

public sealed partial class GuildSystem : ITradeObserver
{
    static ItemCatalog ItemCat => RadiantQuestsMod.Items;

    /// <summary>How near one of yours must stand to a clerk to deal with it (units).</summary>
    public static float ClerkReachUnits => ClerkReach;

    /// <summary>
    /// A deal was made: when one of yours sold an item to a trader, each running quest is told (an item quest checks whether the item it gave
    /// or wants is gone with it). Only this mod's own state changes here (Meitou's rule for trade observers).
    /// </summary>
    public void Traded(World world, in TradeSale sale)
    {
        if (sale.Kind != TradeSaleKind.WindowSell || sale.Buyback || _s.Quests.Count == 0) return;
        if (sale.Seller.Faction != _data.PlayerFaction && !Access.IsYours(world, sale.Seller.Character)) return;
        foreach (var q in _s.Quests)
            if (QuestTypes.Of(q.Kind) is { } type)
                try { type.Sold(world, this, q, sale.Record); }
                catch (Exception e) { RadiantQuestsMod.Log($"sale check of quest #{q.Id} skipped: {e.Message}"); }
        _dirty = true;
    }

    /// <summary>The towns on the map now (sites) whose shops stock <paramref name="item"/>, nearest to <paramref name="from"/> first.</summary>
    public List<SiteInfo> SitesSelling(string item, Vector2 from)
    {
        if (!ItemCat.TownsSelling.TryGetValue(item, out var towns)) return [];
        var set = new HashSet<string>(towns, StringComparer.Ordinal);
        var list = new List<SiteInfo>();
        for (int i = 0; i < _data.Sites.Count; i++)
            if (set.Contains(_data.Sites[i].Town.Id) && Access.Site(i) is { } site) list.Add(site);
        return [.. list.OrderBy(s => Vector2.Distance(s.At, from)).ThenBy(s => s.Index)];
    }

    Dictionary<string, List<int>>? _townSites;

    /// <summary>The sites (map places) made from each TOWN record.</summary>
    Dictionary<string, List<int>> TownSites()
    {
        if (_townSites is not null) return _townSites;
        _townSites = new(StringComparer.Ordinal);
        for (int i = 0; i < _data.Sites.Count; i++)
        {
            string id = _data.Sites[i].Town.Id;
            if (!_townSites.TryGetValue(id, out var l)) _townSites[id] = l = [];
            l.Add(i);
        }
        return _townSites;
    }

    /// <summary>How many places on the map hold <paramref name="item"/> as loot or storage (its Find scarcity; 0: none).</summary>
    public int PlacesHolding(string item) =>
        ItemCat.FoundAtTowns.TryGetValue(item, out var towns) ? towns.Sum(t => TownSites().TryGetValue(t, out var l) ? l.Count : 0) : 0;

    /// <summary>How many places on the map have shops selling <paramref name="item"/>.</summary>
    public int PlacesSelling(string item) =>
        ItemCat.TownsSelling.TryGetValue(item, out var towns) ? towns.Sum(t => TownSites().TryGetValue(t, out var l) ? l.Count : 0) : 0;

    /// <summary>The places on the map now that hold <paramref name="item"/> as loot or storage, nearest to <paramref name="from"/> first.</summary>
    public List<SiteInfo> SitesHolding(string item, Vector2 from)
    {
        if (!ItemCat.FoundAtTowns.TryGetValue(item, out var towns)) return [];
        var list = new List<SiteInfo>();
        foreach (var t in towns)
            if (TownSites().TryGetValue(t, out var sites))
                foreach (int i in sites)
                    if (Access.Site(i) is { } site) list.Add(site);
        return [.. list.OrderBy(s => Vector2.Distance(s.At, from)).ThenBy(s => s.Index)];
    }

    /// <summary>The town owned now by a faction that would deal with you (a delivery's possible ends), with where it is.</summary>
    public IEnumerable<SiteInfo> FriendlyTowns()
    {
        for (int i = 0; i < _data.Sites.Count; i++)
        {
            var town = _data.Sites[i].Town;
            if (town.Type is not (Meitou.Data.Gameplay.TownType.Town or Meitou.Data.Gameplay.TownType.Village)) continue;
            if (town.Faction is not { } owner || _data.FactionIndex(owner) is var f && f < 0 || f == _data.PlayerFaction) continue;
            if (Access.Hostile(f, _data.PlayerFaction)) continue;
            if (Access.Site(i) is { } site) yield return site;
        }
    }

    /// <summary>The town owner's faction id and the TOWN record now of site <paramref name="site"/> (for contraband checks).</summary>
    public (string? TownId, string? Owner) TownFacts(int site) =>
        site >= 0 && site < _data.Sites.Count ? (_data.Sites[site].Town.Id, _data.Sites[site].Town.Faction) : (null, null);

    /// <summary>
    /// A courier's ambush (his rules, 2026-11-24): one group that makes sense to waylay a traveller (people or predators: a group whose
    /// faction is hostile to you, never grazers), no stronger than <paramref name="cap"/> (the run's score two bands down) and not trivially
    /// weak (at least a quarter of it), brought in AHEAD of the courier at <paramref name="at"/> toward <paramref name="toward"/>, out of
    /// sight, by the kill quests' rules: kinds living around there, or (the out-of-region roll) from anywhere. Returns its platoon key, 0 none.
    /// </summary>
    public ulong SpawnAmbush(World world, Quest q, Vector2 at, Vector2 toward, double cap)
    {
        if (!_set.SpawnTargets) return 0;
        var survey = Survey.Of(world, Access, Cat, at, 8000);
        var dice = new Dice(world.Seed ^ Rng.Mix((ulong)q.Id * 0x9E3779B97F4A7C15UL + 0x616D6275UL + ((ulong)q.Ambushes << 32)));
        bool stray = dice.Chance(_set.StrayChance);
        bool Hostile(string template) => Access.SpawnFaction(template) is var f && f >= 0 && f != _data.PlayerFaction && Access.Hostile(f, _data.PlayerFaction);
        // Variety (his rule, 2026-11-25): about two in three ambushes are a group (a kind of which 2 or more fit the cap); the rest anything
        // that fits, a single strong beast included.
        bool group = dice.Chance(AmbushGroupChance);
        var choices = new List<(Target T, int Size, List<string> Templates)>();
        foreach (var t in Cat.Culls.Concat(Cat.Hunts))
        {
            if (t.Kind == Catalog.Cull && !GeneralCull(t, -1)) continue;
            var templates = (stray ? Survey.AnyTemplates(Cat, t) : survey.LocalTemplates(Cat, t)).Where(Hostile).ToList();
            if (templates.Count == 0) continue;
            double unit = CountQuest.Unit(t);
            var sizes = new SortedSet<int>();
            foreach (var id in templates)
            {
                var (lo, hi) = Cat.SpawnRange(id, t.Kind == Catalog.Hunt);
                for (int n = Math.Max(1, lo); n <= Math.Min(hi, Catalog.MostCount); n++)
                    if (Catalog.Fight(unit, n) is var f && f <= cap && f >= cap * 0.25) sizes.Add(n);
            }
            var fit = group ? sizes.Where(n => n >= 2).ToList() : [.. sizes];
            if (fit.Count > 0) choices.Add((t, fit[dice.Int(0, fit.Count - 1)], templates));
        }
        // No kind makes a group here: whatever fits.
        if (choices.Count == 0 && group) return SpawnAmbushAny(world, q, at, toward, cap, survey, dice, stray);
        if (choices.Count == 0) return 0;
        var (target, size, ok) = choices[dice.Int(0, choices.Count - 1)];
        if (SpawnNear(world, q, target, survey, want: size, most: size, ahead: toward, stray: stray, only: ok) is not { } g) return 0;
        RadiantQuestsMod.Detail($"quest #{q.Id}: ambush {q.Ambushes + 1}: {g.Size} {target.Name.Trim()}{(stray ? " (from afar)" : "")} ahead of {at.X:0},{at.Y:0} (cap {cap:0})");
        return g.Key;
    }

    /// <summary>How often an ambush is a group rather than whatever fits (a lone strong beast included).</summary>
    const double AmbushGroupChance = 0.65;

    /// <summary>The ambush when no kind around makes a group under the cap: any size that fits.</summary>
    ulong SpawnAmbushAny(World world, Quest q, Vector2 at, Vector2 toward, double cap, Survey survey, Dice dice, bool stray)
    {
        bool Hostile(string template) => Access.SpawnFaction(template) is var f && f >= 0 && f != _data.PlayerFaction && Access.Hostile(f, _data.PlayerFaction);
        var choices = new List<(Target T, int Size, List<string> Templates)>();
        foreach (var t in Cat.Culls.Concat(Cat.Hunts))
        {
            if (t.Kind == Catalog.Cull && !GeneralCull(t, -1)) continue;
            var templates = (stray ? Survey.AnyTemplates(Cat, t) : survey.LocalTemplates(Cat, t)).Where(Hostile).ToList();
            if (templates.Count == 0) continue;
            double unit = CountQuest.Unit(t);
            var sizes = new List<int>();
            foreach (var id in templates)
            {
                var (lo, hi) = Cat.SpawnRange(id, t.Kind == Catalog.Hunt);
                for (int n = Math.Max(1, lo); n <= Math.Min(hi, Catalog.MostCount); n++)
                    if (Catalog.Fight(unit, n) is var f && f <= cap && f >= cap * 0.25 && !sizes.Contains(n)) sizes.Add(n);
            }
            if (sizes.Count > 0) choices.Add((t, sizes[dice.Int(0, sizes.Count - 1)], templates));
        }
        if (choices.Count == 0) return 0;
        var (target, size, ok) = choices[dice.Int(0, choices.Count - 1)];
        if (SpawnNear(world, q, target, survey, want: size, most: size, ahead: toward, stray: stray, only: ok) is not { } g) return 0;
        RadiantQuestsMod.Detail($"quest #{q.Id}: ambush {q.Ambushes + 1}: {g.Size} {target.Name.Trim()} ahead of {at.X:0},{at.Y:0} (cap {cap:0}, no group fits)");
        return g.Key;
    }

    /// <summary>Whether <paramref name="at"/> is inside a town or village (a courier there has escaped an ambush).</summary>
    public bool InTown(Vector2 at)
    {
        for (int i = 0; i < _data.Sites.Count; i++)
        {
            if (_data.Sites[i].Town.Type is not (Meitou.Data.Gameplay.TownType.Town or Meitou.Data.Gameplay.TownType.Village)) continue;
            if (Access.Site(i) is { } site && Vector2.Distance(site.At, at) <= Math.Max(800, site.Radius + 300)) return true;
        }
        return false;
    }

    /// <summary>A brought-in group of the quest's by platoon key, or null when it is gone.</summary>
    public GroupInfo? GroupByKey(World world, Quest q, ulong key) =>
        key == 0 ? null : Access.Groups(world).FirstOrDefault(g => g.Key == key && g.Tag == q.Tag);
}
