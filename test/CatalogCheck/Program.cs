// Radiant Quests offline catalog check. GPLv3 or later.
// Usage: CatalogCheck <Kenshi folder> <base|full> [overrides folder]
// Loads the game data as the game would (the base game alone, or the install's mod list) and prints what the mod would make of it: counts,
// families, tame animals left out, how many targets reach each rank, sample ranks for known fights, and every note for review.
using Meitou.Content;
using Meitou.Data;
using RadiantQuests;

if (args.Length < 2) { Console.WriteLine("usage: CatalogCheck <Kenshi folder> <base|full> [overrides folder]"); return 1; }
var install = GameInstall.Open(args[0]);
var order = args[1] == "base" ? LoadOrder.BaseGame(install) : LoadOrder.FromInstall(install);
var db = GameDatabase.Load(order);
var ov = args.Length > 2 ? Overrides.Load(args[2], Console.WriteLine) : null;
var cat = Catalog.Build(db, line => Console.WriteLine("  " + line), ov);

Console.WriteLine($"== {args[1]}: {cat.Hunts.Count} hunt races, {cat.Culls.Count} General kill targets, {cat.WarTargets.Count} faction kill targets, "
    + $"{cat.GuildFactions.Count} guild factions, {cat.FamilyHead.Count} family variants");

Console.WriteLine("== targets that can be offered at each rank (any count 2-12):");
for (int r = 0; r <= Ranks.Top; r++)
{
    var hunts = cat.Hunts.Where(h => Enumerable.Range(2, 11).Any(n => cat.QuestRank(Catalog.Hunt, h.Unit, n, cat.PackOf.GetValueOrDefault(h.Id, 1)) == r)).ToList();
    var culls = cat.Culls.Where(c => Enumerable.Range(2, 11).Any(n => cat.QuestRank(Catalog.Cull, cat.UnitOf.GetValueOrDefault(c.Id, c.Unit), n, cat.SizeOf.GetValueOrDefault(c.Id, 1)) == r)).ToList();
    Console.WriteLine($"  {Ranks.Name(r),-2}: {hunts.Count,3} hunts, {culls.Count,3} kills   e.g. {string.Join(", ", hunts.Take(3).Select(h => h.Name.Trim()).Concat(culls.Take(2).Select(c => c.Name.Trim())))}");
}

Console.WriteLine("== known fights (anchors):");
foreach (var name in new[] { "Goat", "Naked Beak Thing", "Swamp Lurker", "Beak Thing", "Leviathan", "Great Beak Thing" })
    foreach (var h in cat.Hunts.Where(h => h.Name.Trim() == name))
        Console.WriteLine($"  hunt {name,-18} n=1..4: " + string.Join(" ", Enumerable.Range(1, 4).Select(n => Ranks.Name(cat.QuestRank(Catalog.Hunt, h.Unit, n, cat.PackOf.GetValueOrDefault(h.Id, 1))))));
foreach (var name in new[] { "Starving Bandits", "Dust Bandits", "The Holy Nation", "Cannibal Hunters" })
    foreach (var c in cat.WarTargets.Where(c => c.Name.Trim() == name))
        Console.WriteLine($"  defeat {name,-16} n=2,4,8: " + string.Join(" ", new[] { 2, 4, 8 }.Select(n => Ranks.Name(cat.QuestRank(Catalog.Cull, cat.UnitOf.GetValueOrDefault(c.Id, c.Unit), n, cat.SizeOf.GetValueOrDefault(c.Id, 1))))));

Console.WriteLine("== fighter strength and scores (unit; score at n = 1, 3, 8, 15, 32):");
foreach (var name in new[] { "Starving Bandits", "Dust Bandits", "Hill Marauders", "The Holy Nation", "Cannibals", "Cannibal Hunters" })
    foreach (var c in cat.WarTargets.Where(c => c.Name.Trim() == name))
    {
        double u = cat.UnitOf.GetValueOrDefault(c.Id, c.Unit);
        Console.WriteLine($"  {name,-18} unit {u,7:0.0}  " + string.Join("  ", new[] { 1, 3, 8, 15, 32 }.Select(n => $"{n}:{Catalog.Fight(u, n),8:0} {Ranks.Name(Difficulty.RankOf(Catalog.Fight(u, n)))}")));
    }
foreach (var name in new[] { "Goat", "Garru", "Swamp Turtle", "Bonedog", "Beak Thing", "Crop Louse", "Skimmer", "Iron Wasp" })
    foreach (var h in cat.Hunts.Where(h => h.Name.Trim() == name))
        Console.WriteLine($"  {name,-18} unit {h.Unit,7:0.0}  pack {cat.PackOf.GetValueOrDefault(h.Id, 1),4:0.0}  " + string.Join("  ", new[] { 1, 3, 8, 15 }.Select(n => $"{n}:{Catalog.Fight(h.Unit, n),8:0} {Ranks.Name(Difficulty.RankOf(Catalog.Fight(h.Unit, n)))}")));

Console.WriteLine("== spawnable: kinds with a group that has spawn regions, and those without (only placed by the game's towns/camps, or never):");
foreach (var (label, list, tmpl) in new[] { ("hunts", cat.Hunts, cat.HuntTemplates), ("defeats", cat.WarTargets, cat.CullTemplates) })
{
    var none = list.Where(t => !(tmpl.GetValueOrDefault(t.Id) ?? []).Any(id => cat.TemplateRegions.TryGetValue(id, out var r) && r.Count > 0)).ToList();
    var noTemplates = none.Where(t => (tmpl.GetValueOrDefault(t.Id) ?? []).Count == 0).ToList();
    Console.WriteLine($"  {label}: {list.Count} kinds, {list.Count - none.Count} spawnable, {none.Count} not ({noTemplates.Count} with no group at all)");
    Console.WriteLine("    e.g. with groups but no regions: " + string.Join(", ", none.Except(noTemplates).Take(25).Select(t => t.Name.Trim())));
    Console.WriteLine("    e.g. no group at all: " + string.Join(", ", noTemplates.Take(25).Select(t => t.Name.Trim())));
}

if (Environment.GetEnvironmentVariable("RQ_TOWNS") is { Length: > 0 } townNames)
{
    var factionsAll = Meitou.Data.Gameplay.FactionData.LoadAll(db);
    var rel = Meitou.Data.Gameplay.FactionRelations.Build(factionsAll);
    foreach (var r in db.OfType(Meitou.Data.Fcs.FcsRecordType.Town).Where(r => townNames.Split(',').Any(n => (r.Name ?? "").Contains(n.Trim(), StringComparison.OrdinalIgnoreCase))))
    {
        Meitou.Data.Gameplay.TownData? town = null;
        try { town = Meitou.Data.Gameplay.TownData.From(r); } catch { }
        if (town is null) continue;
        var owner = town.Faction;
        var of = factionsAll.FirstOrDefault(f => f.Id == owner);
        string bars = string.Join(",", town.BarSquads.Select(l => db.Find(l.Id) is { } s ? $"{s.Name}" : l.Id));
        int hostile = 0, total = 0;
        if (rel.IndexOf(owner ?? "") is var oi && oi >= 0)
            foreach (var g in cat.GuildFactions) { int gi = rel.IndexOf(g); if (gi < 0 || gi == oi) continue; total++; if (rel.IsHostile(gi, oi)) hostile++; }
        if (cat.Lairs.TryGetValue(r.StringId, out var lair))
            Console.WriteLine($"camp '{lair.Name}': {lair.Residents} residents, unit {lair.Unit:0.0}, named {lair.Named:0}, score {lair.Score:0} -> rank {Ranks.Name(Difficulty.RankOf(lair.Score))}");
        Console.WriteLine($"town '{r.Name}' ({r.StringId}) type {town.Type}: owner {owner} '{of?.Name}' type {of?.FundamentalType} guild={cat.GuildFactions.Contains(owner ?? "")} "
            + $"hostile to {hostile}/{total} guild factions; bars [{bars}]");
    }
}

if (Environment.GetEnvironmentVariable("RQ_TEMPLATE") is { Length: > 0 } tid)
{
    Console.WriteLine($"== template {tid}: size {cat.TemplateSize.GetValueOrDefault(tid)} races {string.Join(", ", cat.TemplateRaces.GetValueOrDefault(tid) ?? new HashSet<string>())}");
    foreach (var h in cat.Hunts.Where(h => h.Name.Contains("Antelope"))) Console.WriteLine($"  hunt race {h.Id} = {h.Name} templates {string.Join(", ", cat.HuntTemplates.GetValueOrDefault(h.Id) ?? [])}");
}
Console.WriteLine("== region danger (median hostile group above the typical region; 0.25 of it is what a quest there gains):");
foreach (var (r, d) in cat.RegionDanger.OrderBy(kv => kv.Value))
    Console.WriteLine($"  {r,-40} {d,8:0}  as a group: {Ranks.Name(Difficulty.RankOf(d)),-2}  quarter adds {d * 0.25,7:0}");
Console.WriteLine($"== {cat.Checks.Count} notes for review:");
foreach (var note in cat.Checks) Console.WriteLine("  - " + note);
return 0;
