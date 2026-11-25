// Radiant Quests for Meitou. GPLv3 or later.
// Clerk rumours (his design, 2026-11-24): no map markers for where to get a quest's goods (Kenshi gives little help); instead the clerk tells
// what they have heard, from the game data. The free rumour is very general; you may pay the clerk for a better one (twice). A rumour is
// kept with the quest, so the journal shows it again whenever you look.
//   0 (free): what kind of place, and near or far ("Said to turn up in ruins, some way from here.")
//   1:        + the direction and the region ("...to the north-east, in the Bonefields.")
//   2:        the nearest place by name ("Try the Lost Library, to the north-east, in the Bonefields.")
// Goods sold in shops get the same steps about the shops; animal parts name the beasts.
using System.Numerics;
using Meitou.Simulation;

namespace RadiantQuests;

public sealed partial class GuildSystem
{
    /// <summary>The best rumour a clerk can give.</summary>
    const int MostRumour = 2;

    /// <summary>Whether a quest's goods need finding (supply, gear, find): those get rumours.</summary>
    static bool HasRumours(Quest q) => q.Kind is ItemQuestKinds.Supply or ItemQuestKinds.Gear or ItemQuestKinds.Find || IsFight(q);

    /// <summary>Hunts and kills (found by rumour, his rule 2026-11-25); a camp to clear keeps its marker.</summary>
    public static bool IsFight(Quest q) => q.Kind is Catalog.Hunt or Catalog.Cull or GroupQuest.KindId;

    /// <summary>The day of the game (1 = the first).</summary>
    static int Day(World world) => (int)Math.Floor(world.GameHours / 24) + 1;

    /// <summary>Adds a line to the quest's "heard" log (day-stamped; the newest 6 are kept) and makes it the rumour shown.</summary>
    static void Heard(Quest q, string line)
    {
        q.Rumour = line;
        if (q.RumourLog.Count > 0 && q.RumourLog[^1] == line) return;
        q.RumourLog.Add(line);
        if (q.RumourLog.Count > 6) q.RumourLog.RemoveAt(0);
    }

    /// <summary>What the next better rumour costs: a tenth of the quest's pay, then a quarter (at least 20 cats, in tens).</summary>
    static long RumourCost(Quest q) => Math.Max(20, (long)Math.Round(q.CatsNow * (q.RumourLevel == 0 ? 0.10 : 0.25) / 10) * 10);

    void BuyBetterRumour(World world, BuyRumour b)
    {
        if (_s.Quests.FirstOrDefault(x => x.Id == b.QuestId) is not { } q || !HasRumours(q) || q.Done) return;
        if (ClerkInReach(world, b.Clerk) is null) return;
        if (q.RumourLevel >= MostRumour)
        {
            RadiantQuestsMod.Toast("The clerk has told you all they know.");
            return;
        }
        long cost = RumourCost(q);
        if (world.Player.Money < cost)
        {
            RadiantQuestsMod.Toast($"The clerk wants {cost} cats for what else they have heard.");
            return;
        }
        Access.Pay(world, -cost);
        q.RumourLevel++;
        Heard(q, RumourOf(world, q, q.RumourLevel));
        _dirty = true;
        RadiantQuestsMod.Toast($"-{cost} cats. {q.Rumour}");
    }

    /// <summary>The rumour of level <paramref name="level"/> for a quest's goods, heard at the clerk where it was taken.</summary>
    string RumourOf(World world, Quest q, int level)
    {
        if (IsFight(q)) return FightRumour(world, q, level);
        var from = new Vector2(q.AnchorX, q.AnchorZ);
        var members = ItemCat.MembersOf(q.TargetId);
        if (q.MinWorth > 0)
        {
            int least = (int)(q.MinWorth * ItemQuestKinds.WorthGrace);
            members = members.Where(id => ItemCat.Items.TryGetValue(id, out var f) && f.ValueHi >= least).ToHashSet(StringComparer.Ordinal);
        }
        var ids = members.OrderBy(m => m, StringComparer.Ordinal).ToList();
        // Only shops you can really use (his report: a rumour named New Kralia, where the Kral's Chosen attack you): a town or village with
        // a market, owned by a faction not hostile to you now. Checked when the rumour is told, so mods that change places are followed.
        var shops = ids.SelectMany(id => SitesSelling(id, from)).DistinctBy(s => s.Index).Where(Usable)
            .OrderBy(s => Vector2.Distance(s.At, from)).ThenBy(s => s.Index).ToList();
        var loot = ids.SelectMany(id => SitesHolding(id, from)).DistinctBy(s => s.Index).OrderBy(s => Vector2.Distance(s.At, from)).ThenBy(s => s.Index).ToList();
        // A place with a shop that sells it is named as a shop, not again as a place to search.
        if (shops.Count > 0) loot = [.. loot.Where(l => shops.All(sh => sh.Index != l.Index))];
        var beasts = ids.SelectMany(id => ItemCat.Items.TryGetValue(id, out var f) && f.DroppedBy is { } by ? by : [])
            .Distinct(StringComparer.Ordinal).Select(r => _data.Db.Find(r)?.Name?.Trim()).Where(n => !string.IsNullOrEmpty(n)).Take(3).ToList();

        var parts = new List<string>();
        q.HasHint = false;
        // Animal parts: who to hunt and where they live (the regions their groups spawn in), not loot places.
        if (beasts.Count > 0)
        {
            var homes = BeastHomes(ids, from);
            parts.Add(level == 0 ? "Cut from the bodies of certain beasts."
                : $"Cut from the bodies of {Join(beasts!)}" + (homes.Count > 0 ? $", which live in {Join(homes)}." : "."));
            loot = [];
        }
        // Gear: what counts, when the kind has only a few items (so a near miss is never a mystery), and who wears it here.
        if (q.Kind == ItemQuestKinds.Gear)
        {
            var kinds = ItemCat.MembersOf(q.TargetId).Select(id => ItemCat.Items.TryGetValue(id, out var f) ? f.Name : "").Where(n => n.Length > 0)
                .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (kinds.Count <= 4) parts.Add($"Counts: {Join(kinds)}.");
            var wearers = ids.SelectMany(id => ItemCat.Items.TryGetValue(id, out var f) ? f.WornBy : [])
                .Distinct(StringComparer.Ordinal).Select(t => Access.SpawnFaction(t)).Where(f => f >= 0 && f != _data.PlayerFaction)
                .Distinct().Select(f => Cat.FactionNames.GetValueOrDefault(_data.FactionId(f) ?? "", "")).Where(n => n.Length > 0)
                .OrderBy(n => n, StringComparer.Ordinal).Take(3).ToList();
            if (wearers.Count > 0) parts.Add(level == 0 ? "Some people around here wear them." : $"{Join(wearers)} wear them.");
        }
        if (shops.Count > 0)
        {
            var near = shops[0];
            parts.Add(level switch
            {
                0 => $"Some shops sell them, {Far(near.At, from)}.",
                1 => $"Some shops sell them, {Far(near.At, from)} to the {Way(near.At, from)}{InRegion(near.At)}.",
                _ => $"Shops in {Join([.. shops.Take(2).Select(s => s.Name.Trim())])} sell them ({Way(near.At, from)}).",
            });
            if (level >= MostRumour) Hint(q, near.At);
        }
        if (loot.Count > 0 && (shops.Count == 0 || level == MostRumour || q.Kind == ItemQuestKinds.Find))
        {
            var near = loot[0];
            parts.Add(level switch
            {
                0 => $"Said to turn up in {Places(loot)}, {Far(near.At, from)}.",
                1 => $"Said to turn up in {Places(loot)}, {Far(near.At, from)} to the {Way(near.At, from)}{InRegion(near.At)}.",
                _ => $"Try {near.Name.Trim()}, {Far(near.At, from)} to the {Way(near.At, from)}{InRegion(near.At)}.",
            });
            if (level >= MostRumour && !q.HasHint) Hint(q, near.At);
        }
        if (parts.Count == 0) parts.Add("The clerk has heard nothing of where to get them.");
        return string.Join(" ", parts);
    }

    /// <summary>The place the best rumour names, marked on the map.</summary>
    static void Hint(Quest q, Vector2 at)
    {
        q.HasHint = true;
        (q.HintX, q.HintZ) = (at.X, at.Y);
    }

    /// <summary>Whether a shop at <paramref name="site"/> can be used: a town or village, owned by a faction not hostile to you now.</summary>
    bool Usable(SiteInfo site)
    {
        var town = _data.Sites[site.Index].Town;
        if (town.Type is not (Meitou.Data.Gameplay.TownType.Town or Meitou.Data.Gameplay.TownType.Village)) return false;
        int owner = town.Faction is { } f ? _data.FactionIndex(f) : -1;
        return owner < 0 || owner == _data.PlayerFaction || !Access.Hostile(owner, _data.PlayerFaction);
    }

    /// <summary>The names of the regions where the beasts whose bodies give <paramref name="ids"/> live (local ones first, at most two).</summary>
    List<string> BeastHomes(List<string> ids, Vector2 from)
    {
        string? hereRegion = Access.Region(from);
        var races = ids.SelectMany(id => ItemCat.Items.TryGetValue(id, out var f) && f.DroppedBy is { } by ? by : []).Distinct(StringComparer.Ordinal);
        var regions = new List<string>();
        foreach (var race in races)
            if (Cat.FindTarget(Catalog.Hunt, race) is { } t)
                foreach (var template in Survey.AnyTemplates(Cat, t))
                    if (Cat.TemplateRegions.GetValueOrDefault(template) is { } rs)
                        regions.AddRange(rs);
        return [.. regions.Distinct(StringComparer.Ordinal).OrderBy(r => r == hereRegion ? 0 : 1).ThenBy(r => r, StringComparer.Ordinal)
            .Select(r => _data.Db.Find(r)?.Name?.Trim() ?? "").Where(n => n.Length > 0).Take(2)];
    }

    /// <summary>What a fight quest is after, as people say it: "Fell Hyena", "Dust Bandits", "the Dust Bandit Loner", "a group of 7 Dust Bandits".</summary>
    static string Who(Quest q) => CountQuest.IsLoner(q) ? "the " + Quest.LonerName(q.TargetName)
        : q.Kind == GroupQuest.KindId ? $"a group of {q.GroupSize} {q.TargetName}" : q.TargetName;

    /// <summary>
    /// A fight quest's rumour (his rules, 2026-11-25), from where its targets are NOW (the news is fresh when told; it doesn't follow them
    /// after): 0 (free, townsfolk): roughly where; 1: the way and the region; 2: the nearest named place. Day-stamped.
    /// </summary>
    string FightRumour(World world, Quest q, int level)
    {
        var from = new Vector2(q.AnchorX, q.AnchorZ);
        string day = $"Day {Day(world)}: ";
        if (!_markers.TryGetValue(q.Id, out var seen) || seen.Count == 0) return day + $"nobody has seen {Who(q)} lately.";
        var at = seen.OrderBy(m => Vector2.Distance(m, from)).First();
        string town = TownName(q.Site).Trim();
        string near = Landmark(at, q.Site);
        return level switch
        {
            0 => day + $"{Who(q)} were seen {FarFrom(at, from, town)}.",
            1 => day + $"{Who(q)} were seen {FarFrom(at, from, town)}, to the {Way(at, from)}{InRegion(at)}.",
            _ => day + $"{Who(q)} were seen {FarFrom(at, from, town)}, to the {Way(at, from)}{InRegion(at)}{near}.",
        };
    }

    /// <summary>", near Shark" (the nearest named place within 8000 units, never <paramref name="skip"/>: the quest's own town), or "".</summary>
    string Landmark(Vector2 at, int skip = -1) => Access.SitesNear(at, 8000).Where(s => s.Index != skip)
        .OrderBy(s => Vector2.Distance(s.At, at)).FirstOrDefault() is { } site ? $", near {site.Name.Trim()}" : "";

    /// <summary>"close to Squin", "some way from Squin", "far from Squin", by the straight distance.</summary>
    static string FarFrom(Vector2 at, Vector2 from, string town) => Vector2.Distance(at, from) switch
    {
        < 25000 => $"close to {town}",
        < 70000 => $"some way from {town}",
        _ => $"far from {town}",
    };

    /// <summary>
    /// Fight quests (each look): the first rumour once the targets are known; and when one of yours sees a target (within sight), a fresh
    /// line "your squad saw them" (logged at most every two game hours).
    /// </summary>
    void FightNews(World world, Quest q, List<Vector2> markers)
    {
        if (!IsFight(q) || q.Done) return;
        if (q.Rumour.Length == 0 && markers.Count > 0) Heard(q, FightRumour(world, q, 0));
        var near = markers.FirstOrDefault(m => Access.YoursNearPoint(world, m, SightReach));
        if (near == default) return;
        bool news = q.SightHours < 0 || world.GameHours - q.SightHours > 2;
        q.SightHours = world.GameHours;
        (q.SeenX, q.SeenZ) = (near.X, near.Y);
        if (news) Heard(q, $"Day {Day(world)}: your squad saw {Who(q)}{InRegion(near)}{Landmark(near, q.Site)}.");
    }

    /// <summary>
    /// A townsperson asked about your guild work (his rule, 2026-11-25: free but vague): for each running quest they may know something
    /// (half the time; one answer per quest per 6 game hours): a rough fresh rumour of a fight's targets, or the general word on goods.
    /// </summary>
    void AskTown(World world, AskTownsfolk a)
    {
        string name = world.Characters.NameOf(a.Npc);
        if (name.Length == 0) name = "They";
        var dice = new Dice(world.Seed ^ Rng.Mix((ulong)world.Tick * 0x9E3779B97F4A7C15UL + (ulong)a.Npc.Slot));
        var told = new List<string>();
        foreach (var q in _s.Quests.Where(q => !q.Done && HasRumours(q)).OrderBy(q => q.Id))
        {
            if (q.TownAskHours >= 0 && world.GameHours - q.TownAskHours < 6) continue;
            q.TownAskHours = world.GameHours;
            if (!dice.Chance(0.5)) continue;
            string line = RumourOf(world, q, 0);
            if (IsFight(q) || q.RumourLevel == 0) Heard(q, line);
            told.Add(line.Contains(": ") ? line[(line.IndexOf(": ", StringComparison.Ordinal) + 2)..] : line);
        }
        _dirty = true;
        RadiantQuestsMod.Toast(told.Count == 0 ? $"{name} hasn't heard anything about your work."
            : $"{name}: \"{string.Join(" ", told.Take(2))}\"");
    }

    /// <summary>"near here", "some way off", "far from here", by the straight distance.</summary>
    static string Far(Vector2 at, Vector2 from) => Vector2.Distance(at, from) switch
    {
        < 25000 => "not far from here",
        < 70000 => "some way off",
        _ => "far from here",
    };

    /// <summary>The compass way from <paramref name="from"/> to <paramref name="at"/> (the map's north is -Z, east +X).</summary>
    static string Way(Vector2 at, Vector2 from)
    {
        var d = at - from;
        double deg = (Math.Atan2(d.X, -d.Y) * 180 / Math.PI + 360) % 360;
        string[] names = ["north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"];
        return names[(int)Math.Round(deg / 45) % 8];
    }

    /// <summary>", in the Bonefields" (the region's name), or "".</summary>
    string InRegion(Vector2 at) => Access.Region(at) is { } id && _data.Db.Find(id)?.Name?.Trim() is { Length: > 0 } name ? $", in {name}" : "";

    /// <summary>What kind of places hold it: "ruins", "camps and nests", ... from the nearest few.</summary>
    string Places(List<SiteInfo> sites)
    {
        var kinds = sites.Take(5).Select(s => _data.Sites[s.Index].Town.Type switch
        {
            Meitou.Data.Gameplay.TownType.Nest or Meitou.Data.Gameplay.TownType.NestMarker => "nests",
            Meitou.Data.Gameplay.TownType.Town or Meitou.Data.Gameplay.TownType.Village => "towns",
            Meitou.Data.Gameplay.TownType.PointOfInterest => "ruins",
            _ => "camps and outposts",
        }).Distinct().Take(2).ToList();
        return Join(kinds);
    }

    static string Join(List<string> words) => words.Count switch
    {
        0 => "",
        1 => words[0],
        _ => string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1],
    };
}
