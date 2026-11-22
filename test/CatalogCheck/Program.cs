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

Console.WriteLine($"== {cat.Checks.Count} notes for review:");
foreach (var note in cat.Checks) Console.WriteLine("  - " + note);
return 0;
