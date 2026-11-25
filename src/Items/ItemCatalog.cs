// Radiant Quests for Meitou. GPLv3 or later.
// The item catalog: every item of the load order as item quests see it, read once at start from the game data only (so modded items join
// the same way as the base game's). The shared base of every item quest type (supply, courier, recover, rare finds, smuggling) and of item
// rewards and the rep store (his rule, 2026-11-24: one framework for all of them).
//   - kind (weapon, armour, food, drug, medical, goods, book, ...), value, weight, size, stack;
//   - where it comes from, which sets its rarity: COMMON sold in many towns' shops, UNCOMMON in a few (or only by roaming traders), RARE
//     never sold but found (loot lists, people's inventories, items placed in the world), UNOBTAINABLE none of those (never asked for);
//   - which towns sell it (their TOWN records' trader squads' vendor lists), and where it is illegal (the towns' and factions' trade cultures);
//   - left out: test or placeholder entries, nameless or worthless items, and the overrides' "noitem" (the overrides' "allowitem" lets one in).
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Economy;

namespace RadiantQuests;

/// <summary>What kind of thing an item is, for picking items that suit a quest.</summary>
public enum ItemKind { Goods, Food, RawMeat, Drug, Medical, Book, Weapon, Armour, Crossbow, Robotics, Backpack, Map, Blueprint }

/// <summary>How hard an item is to come by.</summary>
public enum ItemRarity { Common, Uncommon, Rare, Unobtainable }

/// <summary>One item as the catalog knows it.</summary>
/// <param name="ShopTowns">How many towns' shops stock it.</param>
/// <param name="Traders">Whether any trader at all (a town's or a roaming one) stocks it.</param>
/// <param name="Group">The item group it counts for (a weapon category, an armour class and slot), or null.</param>
/// <param name="ValueLo">Its worth at the lowest grade (weapons and armour; else its value).</param>
/// <param name="ValueHi">Its worth at the highest grade.</param>
/// <param name="DroppedBy">The animal races whose bodies give it (an animal part), empty for others.</param>
public sealed record ItemFacts(string Id, string Name, FcsRecordType Type, ItemKind Kind, int Value, float Weight, int Width, int Height, int Stack,
    ItemRarity Rarity, int ShopTowns, bool Traders, string? Group = null, int ValueLo = 0, int ValueHi = 0, string[]? DroppedBy = null)
{
    /// <summary>
    /// What one costs to buy in a shop (his rule, 2026-11-24: item pay comes from the real price, not the record's value): the game's own
    /// value (ItemValue with its category rows; gear at its lowest grade) times the average "buy mult" of the traders that sell it (1 if none).
    /// </summary>
    public int Price { get; init; } = Value;

    /// <summary>Gear: the squad templates whose people wear or carry it (it drops where they live), empty for others.</summary>
    public string[] WornBy { get; init; } = [];

    public bool AnimalPart => DroppedBy is { Length: > 0 };
}

/// <summary>
/// Items that count alike for a quest (his rule, 2026-11-24: "any katana", "any heavy body armour"), made from the data: a weapon's skill
/// category, an armour's class and slot. Modded items join by their data.
/// </summary>
public sealed record ItemGroup(string Key, string Name, IReadOnlySet<string> Members);

public sealed class ItemCatalog
{
    /// <summary>The Guild Parcel's record (the mod's own data file, test\MakeData): courier quests' sealed parcel.</summary>
    public const string ParcelRecord = "1-RadiantQuests.mod";

    /// <summary>Every item quests may use, by record id.</summary>
    public IReadOnlyDictionary<string, ItemFacts> Items { get; private init; } = new Dictionary<string, ItemFacts>();

    /// <summary>The items each TOWN record's shops stock (record id to item ids).</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> SoldAtTown { get; private init; } = new Dictionary<string, IReadOnlySet<string>>();

    /// <summary>The towns (TOWN record ids) whose shops stock each item.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> TownsSelling { get; private init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Contraband in each TOWN record's own trade culture.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> IllegalAtTown { get; private init; } = new Dictionary<string, IReadOnlySet<string>>();

    /// <summary>Contraband in each faction's trade culture (faction id).</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> IllegalForFaction { get; private init; } = new Dictionary<string, IReadOnlySet<string>>();

    /// <summary>
    /// Where an item is found, not bought: the TOWN records (ruins, camps, nests, towns) whose own non-trading squads keep it in their loot or
    /// storage lists, or carry it, by item id. A clerk's rumour names the nearest; Find quests ask only for items found in several places.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> FoundAtTowns { get; private init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>The item groups (weapon categories, armour classes and slots), by key ("group:...").</summary>
    public IReadOnlyDictionary<string, ItemGroup> Groups { get; private init; } = new Dictionary<string, ItemGroup>();

    /// <summary>The items a quest target means: a group's members, or the one item.</summary>
    public IReadOnlySet<string> MembersOf(string target) =>
        Groups.TryGetValue(target, out var g) ? g.Members : new HashSet<string>(StringComparer.Ordinal) { target };

    /// <summary>Whether the Guild Parcel is in the load order (the mod's data file ticked): courier quests need it.</summary>
    public bool HasParcel { get; private init; }

    /// <summary>Notes for review (items left out and why), like the fight catalog's.</summary>
    public List<string> Checks { get; private init; } = [];

    public static ItemCatalog Empty { get; } = new();

    /// <summary>Whether <paramref name="item"/> is contraband in the town <paramref name="townId"/> owned by <paramref name="factionId"/>.</summary>
    public bool IllegalIn(string item, string? townId, string? factionId) =>
        (townId is not null && IllegalAtTown.TryGetValue(townId, out var t) && t.Contains(item))
        || (factionId is not null && IllegalForFaction.TryGetValue(factionId, out var f) && f.Contains(item));

    /// <summary>Words that mark a modder's test or leftover entry (whole words, any case).</summary>
    static readonly System.Text.RegularExpressions.Regex s_testWords = new(
        @"\b(test|testing|debug|dummy|placeholder|unused|deprecated|wip|template|reference|do ?not ?use|don'?t ?use)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    static readonly FcsRecordType[] s_itemTypes =
    [
        FcsRecordType.Item, FcsRecordType.Weapon, FcsRecordType.Armour, FcsRecordType.Crossbow, FcsRecordType.LimbReplacement,
        FcsRecordType.Container, FcsRecordType.MapItem,
    ];

    public static ItemCatalog Build(GameDatabase db, Action<string> log, Overrides? ov = null)
    {
        ov ??= Overrides.None;
        var notes = new List<string>();

        // Shops: the trader squads (with vendor lists) living in each town, and every trader anywhere (roaming ones too).
        var vendorOfTemplate = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var buyMultOf = new Dictionary<string, float>(StringComparer.Ordinal);
        var traderLists = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.SquadTemplate))
        {
            SquadTemplate? t = null;
            try { t = SquadTemplate.From(r); } catch { }
            if (t is null || !t.IsTrader || t.Vendors.Count == 0) continue;
            vendorOfTemplate[t.Id] = [.. t.Vendors.Select(v => v.Id)];
            buyMultOf[t.Id] = t.BuyMult;
            traderLists.UnionWith(t.Vendors.Select(v => v.Id));
        }
        var listItems = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> ItemsOf(string listId)
        {
            if (listItems.TryGetValue(listId, out var set)) return set;
            set = new HashSet<string>(StringComparer.Ordinal);
            if (db.Find(listId) is { Type: FcsRecordType.VendorList } rec)
                try { foreach (var e in VendorList.From(db, rec).Entries) set.Add(e.Id); } catch { }
            return listItems[listId] = set;
        }
        var soldAtTown = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var illegalAtTown = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.Town))
        {
            TownData? town = null;
            try { town = TownData.From(r); } catch { }
            if (town is null) continue;
            var sold = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in town.Residents.Concat(town.BarSquads))
                if (vendorOfTemplate.TryGetValue(link.Id, out var lists))
                    foreach (var l in lists) sold.UnionWith(ItemsOf(l));
            if (sold.Count > 0) soldAtTown[r.StringId] = sold;
            if (TradeCulture.From(db, town.TradeCulture) is { Illegal.Count: > 0 } culture)
                illegalAtTown[r.StringId] = new HashSet<string>(culture.Illegal, StringComparer.Ordinal);
        }
        var illegalForFaction = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var f in FactionData.LoadAll(db))
            if (TradeCulture.From(db, f.TradeCulture) is { Illegal.Count: > 0 } culture)
                illegalForFaction[f.Id] = new HashSet<string>(culture.Illegal, StringComparer.Ordinal);
        var anyTrader = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in traderLists) anyTrader.UnionWith(ItemsOf(l));
        // The traders' markup per item: the mean "buy mult" of the trader squads whose lists hold it.
        var markup = new Dictionary<string, (double Sum, int N)>(StringComparer.Ordinal);
        foreach (var (template, lists) in vendorOfTemplate)
            foreach (var item in lists.SelectMany(ItemsOf).Distinct())
            {
                var m = markup.GetValueOrDefault(item);
                markup[item] = (m.Sum + buyMultOf.GetValueOrDefault(template, 1), m.N + 1);
            }
        // Gear worn or carried by each squad's people (the squad templates, by item).
        var wornBy = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.SquadTemplate))
            foreach (var m in r.GetReferences("squad").Concat(r.GetReferences("leader")))
                if (db.Find(m.TargetStringId) is { Type: FcsRecordType.Character } c)
                    foreach (var list in new[] { "clothing", "weapons" })
                        foreach (var e in c.GetReferences(list))
                        {
                            if (!wornBy.TryGetValue(e.TargetStringId, out var set)) wornBy[e.TargetStringId] = set = new SortedSet<string>(StringComparer.Ordinal);
                            set.Add(r.StringId);
                        }

        // Found, not bought: every other vendor list (loot of containers, nests, bodies), items placed in the world, people's own gear.
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.VendorList))
            if (!traderLists.Contains(r.StringId)) found.UnionWith(ItemsOf(r.StringId));
        foreach (var r in db.OfType(FcsRecordType.ItemPlacementGroup))
            try { foreach (var e in ItemPlacementGroup.From(db, r).Items.Entries) found.Add(e.Id); } catch { }
        foreach (var type in new[] { FcsRecordType.Character, FcsRecordType.AnimalCharacter })
            foreach (var r in db.OfType(type))
                foreach (var list in new[] { "inventory", "weapons", "clothing", "backpack", "items" })
                    foreach (var e in r.GetReferences(list))
                        if (db.Find(e.TargetStringId) is { } target && s_itemTypes.Contains(target.Type)) found.Add(target.StringId);

        // Where found: each squad that does not trade, with what its loot or storage lists and its members hold; then the towns it lives in.
        var squadFinds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.SquadTemplate))
        {
            SquadTemplate? t = null;
            try { t = SquadTemplate.From(r); } catch { }
            if (t is null || t.IsTrader) continue;
            var held = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in t.Vendors) held.UnionWith(ItemsOf(v.Id));
            foreach (var m in r.GetReferences("squad").Concat(r.GetReferences("leader")))
                if (db.Find(m.TargetStringId) is { Type: FcsRecordType.Character } c)
                    foreach (var list in new[] { "inventory", "backpack", "items" })
                        foreach (var e in c.GetReferences(list))
                            if (db.Find(e.TargetStringId) is { } target && s_itemTypes.Contains(target.Type)) held.Add(target.StringId);
            if (held.Count > 0) squadFinds[r.StringId] = held;
        }
        var foundAtTowns = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.Town))
        {
            TownData? town = null;
            try { town = TownData.From(r); } catch { }
            if (town is null) continue;
            var here = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in town.Residents.Concat(town.BarSquads))
                if (squadFinds.TryGetValue(link.Id, out var held)) here.UnionWith(held);
            foreach (var item in here)
            {
                if (!foundAtTowns.TryGetValue(item, out var l)) foundAtTowns[item] = l = [];
                l.Add(r.StringId);
            }
        }

        var townsSelling = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (town, sold) in soldAtTown)
            foreach (var item in sold)
            {
                if (!townsSelling.TryGetValue(item, out var l)) townsSelling[item] = l = [];
                l.Add(town);
            }

        // Animal parts: what animals' bodies give (their records' death items), by item, with the races that give it.
        var droppedBy = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.AnimalCharacter))
        {
            if (r.GetReferences("race") is not { Count: > 0 } races) continue;
            foreach (var e in r.GetReferences("death items"))
                if (e.Values.Value0 > 0 && db.Find(e.TargetStringId) is { Type: FcsRecordType.Item } part)
                {
                    if (!droppedBy.TryGetValue(part.StringId, out var set)) droppedBy[part.StringId] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(races[0].TargetStringId);
                }
        }

        var prices = PriceConstants.FromDatabase(db);
        var groups = new Dictionary<string, (string Name, HashSet<string> Members)>(StringComparer.Ordinal);
        var items = new Dictionary<string, ItemFacts>(StringComparer.Ordinal);
        int leftOut = 0;
        foreach (var type in s_itemTypes)
            foreach (var r in db.OfType(type))
            {
                if (r.StringId == ParcelRecord) continue;
                string name = (r.Name ?? "").Trim();
                string? why = ov.ItemAllowed(r.StringId) ? null
                    : ov.ItemNever(r.StringId) ? "listed under \"noitem\""
                    : name.Length == 0 || name.Contains('_') ? "no proper name"
                    : s_testWords.IsMatch(name) ? $"its name \"{name}\" marks a test entry"
                    : r.GetInt("value") <= 0 ? "it has no value" : null;
                if (why is not null)
                {
                    leftOut++;
                    if (notes.Count < 200) notes.Add($"item {name} ({r.StringId}) left out of item quests: {why}");
                    continue;
                }
                int shopTowns = townsSelling.TryGetValue(r.StringId, out var ts) ? ts.Count : 0;
                bool traders = anyTrader.Contains(r.StringId);
                var rarity = shopTowns >= 4 ? ItemRarity.Common
                    : shopTowns > 0 || traders ? ItemRarity.Uncommon
                    : found.Contains(r.StringId) ? ItemRarity.Rare : ItemRarity.Unobtainable;
                // Gear: its group, and its worth from the lowest to the highest grade (the game's own ItemValue; a weapon's maker changes it
                // further, which the hand-in judges by the item itself).
                var (group, groupName) = GroupOf(r);
                int lo = r.GetInt("value"), hi = lo;
                if (group is not null)
                {
                    try
                    {
                        lo = ItemValue.Of(db, r, 0, true, prices);
                        hi = Math.Max(lo, ItemValue.Of(db, r, 100, true, prices));
                    }
                    catch { lo = hi = r.GetInt("value"); }
                    if (!groups.TryGetValue(group, out var g)) groups[group] = g = (groupName!, new HashSet<string>(StringComparer.Ordinal));
                    g.Members.Add(r.StringId);
                }
                double mult = markup.TryGetValue(r.StringId, out var mk) && mk.N > 0 ? mk.Sum / mk.N : 1;
                int baseValue = group is not null ? lo : SafeValue(db, r, prices);
                items[r.StringId] = new ItemFacts(r.StringId, name, r.Type, KindOf(r), r.GetInt("value"), r.GetFloat("weight kg"),
                    Math.Max(1, r.GetInt("inventory footprint width", 1)), Math.Max(1, r.GetInt("inventory footprint height", 1)),
                    r.Type is FcsRecordType.Item ? Math.Max(1, r.GetInt("stackable", 1)) : 1, rarity, shopTowns, traders, group, lo, hi,
                    droppedBy.TryGetValue(r.StringId, out var by) ? [.. by] : null)
                {
                    Price = Math.Max(1, (int)Math.Round(baseValue * Math.Clamp(mult, 0.5, 5))),
                    WornBy = wornBy.TryGetValue(r.StringId, out var wb) ? [.. wb] : [],
                };
            }

        var catalog = new ItemCatalog
        {
            Items = items,
            SoldAtTown = soldAtTown.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value, StringComparer.Ordinal),
            TownsSelling = townsSelling.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal),
            FoundAtTowns = foundAtTowns.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal),
            IllegalAtTown = illegalAtTown,
            IllegalForFaction = illegalForFaction,
            HasParcel = db.Find(ParcelRecord) is { Type: FcsRecordType.Item },
            Checks = notes,
            Groups = groups.ToDictionary(kv => kv.Key, kv => new ItemGroup(kv.Key, kv.Value.Name, kv.Value.Members), StringComparer.Ordinal),
        };
        if (!catalog.HasParcel) notes.Add("the Guild Parcel (RadiantQuests.mod) is not in the load order: no courier quests (tick RadiantQuests in the mod list)");
        int Count(ItemRarity r) => items.Values.Count(i => i.Rarity == r);
        log($"items: {items.Count} usable ({Count(ItemRarity.Common)} common, {Count(ItemRarity.Uncommon)} uncommon, {Count(ItemRarity.Rare)} rare, "
            + $"{Count(ItemRarity.Unobtainable)} unobtainable), {leftOut} left out, {soldAtTown.Count} towns with shops, "
            + $"contraband in {illegalAtTown.Count} towns and {illegalForFaction.Count} factions; parcel {(catalog.HasParcel ? "ready" : "MISSING")}");
        return catalog;
    }

    /// <summary>
    /// The group a weapon or armour record counts for, and the group's name: a weapon by its skill category (katana, sabre, blunt, heavy,
    /// hacker, polearm), an armour by class (cloth, light, medium, heavy) and slot (hat, body, shirt, legs, boots, gloves). Null for others.
    /// </summary>
    static (string? Key, string? Name) GroupOf(GameRecord r)
    {
        if (r.Type == FcsRecordType.Weapon)
        {
            string? kind = r.GetInt("skill category", 5) switch
            {
                0 => "katana", 1 => "sabre", 2 => "blunt weapon", 3 => "heavy weapon", 4 => "hacker", 8 => "polearm", _ => null,
            };
            return kind is null ? (null, null) : ($"group:weapon:{kind}", kind);
        }
        if (r.Type == FcsRecordType.Armour)
        {
            string cls = r.GetInt("class", 0) switch { 1 => "light", 2 => "medium", 3 => "heavy", _ => "cloth" };
            // The slot numbers are the game's ATTACH slots (Meitou's AttachSlot): 3 hat, 5 body (also no slot), 6 legs, 8 shirt, 9 boots, 10 gloves.
            string? slot = (r.Ints.ContainsKey("slot") ? r.GetInt("slot") : 5) switch
            {
                3 => "hat", 5 => "body armour", 6 => "leg armour", 8 => "shirt", 9 => "boots", 10 => "gloves", _ => null,
            };
            return slot is null ? (null, null) : ($"group:armour:{cls}:{slot}", cls == "cloth" ? slot : $"{cls} {slot}");
        }
        return (null, null);
    }

    /// <summary>The game's own value of an item record (ItemValue with its category rows, lowest grade), its record value if that fails.</summary>
    static int SafeValue(GameDatabase db, GameRecord r, PriceConstants prices)
    {
        try { return Math.Max(r.GetInt("value"), ItemValue.Of(db, r, 0, true, prices)); }
        catch { return r.GetInt("value"); }
    }

    /// <summary>An item record's kind, from its type and its item function.</summary>
    static ItemKind KindOf(GameRecord r) => r.Type switch
    {
        FcsRecordType.Weapon => ItemKind.Weapon,
        FcsRecordType.Armour => ItemKind.Armour,
        FcsRecordType.Crossbow => ItemKind.Crossbow,
        FcsRecordType.LimbReplacement => ItemKind.Robotics,
        FcsRecordType.Container => ItemKind.Backpack,
        FcsRecordType.MapItem => ItemKind.Map,
        _ => r.GetInt("item function") switch
        {
            3 => ItemKind.Food,
            15 => ItemKind.RawMeat,
            8 => ItemKind.Drug,
            1 or 2 or 10 or 12 => ItemKind.Medical,
            13 => ItemKind.Book,
            _ => ItemKind.Goods,
        },
    };
}
