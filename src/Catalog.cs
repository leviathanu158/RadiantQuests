// Radiant Quests for Meitou. GPLv3 or later.
// What quests can ask for, read from the merged game data once at start: no fixed lists, so the factions and animals of UWE, Kaizo or any other
// mod in the load order take part as the base game's do.
//
// Difficulty is worked out from the records alone:
//   - a person: their combat stats (or their STATS record's attack and defence when they have one), raised by their armour grade and strength;
//   - an animal: its combat stats times its HP multiplier.
// A cull target is a faction's typical roaming member (the average over its roaming squads' non-unique people); a hunt target is an animal race
// (the average over the roaming and nest animals of that race). Each kind is then split into ranks D..S by where it falls among all of its kind
// (the weakest 30% are D, then 25% C, 20% B, 15% A and the strongest 10% S), so a load order with tougher creatures moves the lines with it.
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;

namespace RadiantQuests;

/// <summary>A lair: a camp, nest or outpost (its TOWN record), how strong and how many its residents are, and its owner (null: wild nest).</summary>
public sealed record Lair(string Id, string Name, double Unit, int Residents, string? Owner);

/// <summary>Something a quest can ask you to kill: an animal race (hunt) or a faction's people (cull).</summary>
public sealed record Target(string Kind, string Id, string Name, double Unit, int Rank);

public sealed class Catalog
{
    public const string Hunt = "hunt", Cull = "cull";

    public static Catalog Empty { get; } = new([], [], new HashSet<string>(), new Dictionary<string, string>(), new Dictionary<string, string>());

    /// <summary>Animal races to hunt, weakest first.</summary>
    public IReadOnlyList<Target> Hunts { get; }

    /// <summary>Factions whose people can be culled, weakest first (General quests keep only those hostile to almost every guild faction).</summary>
    public IReadOnlyList<Target> Culls { get; }

    /// <summary>
    /// Every faction's people as Faction-quest targets, weakest first, ranked among all of them (a faction's clerk offers those it is hostile to
    /// now: its wars and the outlaws). Variants count as their family head.
    /// </summary>
    public IReadOnlyList<Target> WarTargets { get; private init; } = [];

    /// <summary>A faction's (family head's) typical fighter strength, for a real group's difficulty.</summary>
    public IReadOnlyDictionary<string, double> UnitOf { get; private init; } = new Dictionary<string, double>();

    /// <summary>How many people a faction (family head) usually roams in a group with.</summary>
    public IReadOnlyDictionary<string, double> SizeOf { get; private init; } = new Dictionary<string, double>();

    /// <summary>How many of an animal race usually come together (its packs, herds and nests).</summary>
    public IReadOnlyDictionary<string, double> PackOf { get; private init; } = new Dictionary<string, double>();

    /// <summary>
    /// The rank (0 D .. 4 S) of a quest to defeat <paramref name="need"/> of a target with fighters of <paramref name="unit"/> strength that
    /// usually come <paramref name="typical"/> together: you face the ones you must defeat plus half of the rest of their group (you rarely get
    /// them alone), and that strength is placed on the scale of the load order's typical squads (or packs, for a hunt).
    /// </summary>
    public int QuestRank(string kind, double unit, int need, double typical) => Difficulty.RankOf(QuestScore(unit, need, typical));

    /// <summary>The fight score behind <see cref="QuestRank"/>.</summary>
    public static double QuestScore(double unit, int need, double typical) => Fight(unit, need + 0.5 * Math.Max(0, typical - need));

    /// <summary>
    /// How hard a fight against <paramref name="count"/> fighters of <paramref name="unit"/> strength is: strength squared times the count, so quality
    /// weighs more than numbers (a herd of ten goats is easy; one beak thing, or three elite fighters, is not).
    /// </summary>
    public static double Fight(double unit, double count) => unit * unit * count;

    /// <summary>The rank (0 D .. 4 S) of a group of <paramref name="size"/> fighters of <paramref name="unit"/> strength.</summary>
    public int GroupRank(double unit, int size)
    {
        return Difficulty.RankOf(Fight(unit, size));
    }

    /// <summary>Factions that run bars in their towns: they have guild clerks and take part in the guild.</summary>
    public IReadOnlySet<string> GuildFactions { get; }

    /// <summary>A character or animal record's race (for matching hunt kills).</summary>
    public IReadOnlyDictionary<string, string> RaceOf { get; }

    /// <summary>Factions the game data marks as bandits (fundamental type Bandit).</summary>
    public IReadOnlySet<string> Bandits { get; private init; } = new HashSet<string>();

    /// <summary>
    /// A faction's family head: a variant faction whose squads roam on another faction's spawn list (Bandit Variety's Dust Raiders, Dust Reapers
    /// and Dust Lords on the Dust Bandits' list) counts as that faction for quests. A faction with no head is its own.
    /// </summary>
    public IReadOnlyDictionary<string, string> FamilyHead { get; private init; } = new Dictionary<string, string>();

    /// <summary>The squad templates a cull of a family head can spawn (its own and its variants' roaming squads, non-unique).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CullTemplates { get; private init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>The squad templates holding animals of a race (non-unique, not a guild faction's).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> HuntTemplates { get; private init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>The animal races in a squad template.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> TemplateRaces { get; private init; } = new Dictionary<string, IReadOnlySet<string>>();

    /// <summary>
    /// The regions (BIOME_GROUP ids) a squad template naturally spawns in: the regions whose wild spawn lists or nests name it, and the home
    /// regions of the factions that roam with it. A quest only ever spawns a group there; a template with none listed is never spawned.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> TemplateRegions { get; private init; } = new Dictionary<string, IReadOnlySet<string>>();

    HashSet<string>? _wildRegions;

    /// <summary>How many a squad template usually holds (people and animals), for spawning a group that fits a quest's count.</summary>
    public IReadOnlyDictionary<string, double> TemplateSize { get; private init; } = new Dictionary<string, double>();

    /// <summary>The fewest a squad template can hold (the low end of every range in its data): a board's count never asks for more.</summary>
    public IReadOnlyDictionary<string, int> TemplateMinSize { get; private init; } = new Dictionary<string, int>();

    /// <summary>The most a squad template can hold (the high end of every range): a top-up never brings more than what is missing.</summary>
    public IReadOnlyDictionary<string, int> TemplateMaxSize { get; private init; } = new Dictionary<string, int>();

    /// <summary>
    /// Each region's extra danger: how far the median fight score of the hostile groups its spawn lists and nests hold (not guild factions'
    /// groups: traders, patrols) is above that of the median region (0 for a typical or calm one). Built from the data, so modded regions and
    /// creatures get one the same way.
    /// </summary>
    public IReadOnlyDictionary<string, double> RegionDanger { get; private init; } = new Dictionary<string, double>();

    /// <summary>Every region some group naturally spawns in (land where things live).</summary>
    public IReadOnlySet<string> WildRegions => _wildRegions ??= new HashSet<string>(TemplateRegions.Values.SelectMany(r => r), StringComparer.Ordinal);

    /// <summary>
    /// Lairs: every TOWN record that is not a town or village (camps, nests, outposts, ruins...) whose residents include fighters, with how strong
    /// a resident is (people and animals on one scale), how many live there, and its owner's family head (null: wild animals' nest).
    /// </summary>
    public IReadOnlyDictionary<string, Lair> Lairs { get; private init; } = new Dictionary<string, Lair>();

    /// <summary>Whether a quest for <paramref name="t"/> could spawn more of it (one of its groups has a natural region).</summary>
    public bool CanSpawn(Target t) =>
        (t.Kind == Hunt ? HuntTemplates.GetValueOrDefault(t.Id, []) : CullTemplates.GetValueOrDefault(t.Id, [])).Any(TemplateRegions.ContainsKey);

    /// <summary>The family head of <paramref name="faction"/> (itself when it has none).</summary>
    public string Head(string faction) => FamilyHead.GetValueOrDefault(faction, faction);

    Dictionary<string, Target>? _hunts, _factions;
    HashSet<string>? _outlaws;

    /// <summary>Whether <paramref name="id"/> is an outlaw faction (a General cull target).</summary>
    public bool IsOutlaw(string id) => (_outlaws ??= new HashSet<string>(Culls.Select(t => t.Id), StringComparer.Ordinal)).Contains(id);

    /// <summary>
    /// The target a quest names: an animal race for a hunt, else a faction (war targets first, then the outlaws). Looked up, not searched.
    /// </summary>
    public Target? FindTarget(string kind, string id)
    {
        _hunts ??= Hunts.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _factions ??= WarTargets.Concat(Culls).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return kind == Hunt ? _hunts.GetValueOrDefault(id) ?? _factions.GetValueOrDefault(id) : _factions.GetValueOrDefault(id);
    }

    /// <summary>Faction names by id.</summary>
    public IReadOnlyDictionary<string, string> FactionNames { get; }

    Catalog(IReadOnlyList<Target> hunts, IReadOnlyList<Target> culls, IReadOnlySet<string> guild, IReadOnlyDictionary<string, string> raceOf,
        IReadOnlyDictionary<string, string> factionNames)
    {
        Hunts = hunts;
        Culls = culls;
        GuildFactions = guild;
        RaceOf = raceOf;
        FactionNames = factionNames;
    }

    /// <summary>Notes for review (things that may be wrong in this load order), from <see cref="Check"/>.</summary>
    public IReadOnlyList<string> Checks { get; private set; } = [];

    public static Catalog Build(GameDatabase db, Action<string> log, Overrides? ov = null)
    {
        ov ??= Overrides.None;
        int missing = ov.AllIds.Distinct().Count(id => db.Find(id) is null);
        if (missing > 0) log($"overrides: {missing} id(s) are not in this load order and were skipped");
        double Strength(string id) => ov.Strength.GetValueOrDefault(id, 1);
        var factions = FactionData.LoadAll(db);
        var names = factions.ToDictionary(f => f.Id, f => f.Name, StringComparer.Ordinal);

        // The relations every faction starts the game with (the game's own rule: an explicit entry, else the lower of the two defaults).
        // Nothing here reads your relations: the game data only. Whether a clerk deals with you is checked against the live relations in play.
        var start = FactionRelations.Build(factions);

        // Owners of a town or village where someone can give work: a bar (its TOWN record lists bar squads), else a trader's shop or a
        // headquarters among its residents (hive villages and other places without a bar).
        var designationOf = new Dictionary<string, int>(StringComparer.Ordinal);
        int Designation(string id)
        {
            if (designationOf.TryGetValue(id, out int d)) return d;
            d = 0;
            if (db.Find(id) is { Type: FcsRecordType.SquadTemplate } rec)
                try { d = SquadTemplate.From(rec).BuildingDesignation; } catch { }
            return designationOf[id] = d;
        }
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.Town))
        {
            if (TownDataOf(r) is not { Faction: { } owner } town) continue;
            if (town.BarSquads.Count == 0 && !town.Residents.Any(l => Designation(l.Id) is 1 or 3 or 8)) continue;
            if (town.Type is not (TownType.Town or TownType.Village)) continue;
            if (owner == FactionData.PlayerId || !names.ContainsKey(owner) || factions.First(f => f.Id == owner).NotReal) continue;
            owners.Add(owner);
        }

        // Hostile to at least three in four of <paramref name="among"/> at the start of a game: an outlaw to them.
        bool HostileToMost(string id, IReadOnlyCollection<int> among)
        {
            int f = start.IndexOf(id);
            if (f < 0) return false;
            int others = among.Count(g => g != f);
            return others > 0 && among.Count(g => g != f && start.IsHostile(g, f)) * 4 >= others * 3;
        }

        // Guild factions: those owners that are not outlaws to the other owners (fogmen, cannibals and bandit camps own places with bars too,
        // but nobody walks in there to take work).
        int[] ownerIdx = [.. owners.Select(start.IndexOf).Where(i => i >= 0)];
        var bandits = new HashSet<string>(factions.Where(f => f.FundamentalType == FundamentalType.Bandit).Select(f => f.Id), StringComparer.Ordinal);
        bandits.UnionWith(ov.Outlaw.Where(names.ContainsKey));
        var guild = new HashSet<string>(owners.Where(o => !bandits.Contains(o) && !HostileToMost(o, ownerIdx)), StringComparer.Ordinal);
        guild.UnionWith(ov.Guild.Add.Where(names.ContainsKey));
        guild.ExceptWith(ov.Guild.Remove);
        int[] guildIdx = [.. guild.Select(start.IndexOf).Where(i => i >= 0)];

        // Cull targets: outlaws to the guild factions (bandits, cannibals, fogmen...), never a guild faction. General quests check the live
        // relations again when a board is made.
        bool Outlaw(string id) => !guild.Contains(id) && (bandits.Contains(id) || HostileToMost(id, guildIdx));

        // Families: a faction's roaming list naming another faction's squads makes that faction its variant. Followed to the top, so a chain of
        // variants shares one head; a faction never heads itself.
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in factions)
        {
            if (f.Id == FactionData.PlayerId || f.NotReal || guild.Contains(f.Id)) continue;
            foreach (var link in f.Squads)
                if (TemplateOf(db, link.Id) is { IsUnique: false, Faction: { } g } && g != f.Id && names.ContainsKey(g) && !guild.Contains(g)
                    && !parent.ContainsKey(g) && !parent.ContainsKey(f.Id))
                    parent[g] = f.Id;
        }
        var head = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var g in parent.Keys)
        {
            string h = g;
            for (int i = 0; i < 8 && parent.TryGetValue(h, out var up) && up != g; i++) h = up;
            if (h != g) head[g] = h;
        }
        // Fallback by name, for variant mods that spawn their factions from the regions' lists instead (Bandit Variety): a bandit-type faction
        // whose name starts with the first word of a bandit-type "<word> Bandits" faction joins that faction's family (Dust Raiders, Dust Reapers
        // and Dust Lords join Dust Bandits).
        var byFirstWord = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in factions)
        {
            if (!bandits.Contains(f.Id) || guild.Contains(f.Id) || head.ContainsKey(f.Id)) continue;
            var words = f.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 2 && words[1].Equals("Bandits", StringComparison.OrdinalIgnoreCase)) byFirstWord.TryAdd(words[0], f.Id);
        }
        foreach (var f in factions)
        {
            if (!bandits.Contains(f.Id) || guild.Contains(f.Id) || head.ContainsKey(f.Id) || byFirstWord.ContainsValue(f.Id)) continue;
            var words = f.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2 && byFirstWord.TryGetValue(words[0], out var h) && h != f.Id && !head.ContainsValue(f.Id)) head[f.Id] = h;
        }

        if (head.Count > 0)
            log("catalog: families: " + string.Join("; ", head.GroupBy(kv => kv.Value).Select(gr =>
                $"{names.GetValueOrDefault(gr.Key, gr.Key)} <- {string.Join(", ", gr.Select(kv => names.GetValueOrDefault(kv.Key, kv.Key)))}")));

        var raceOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var animalScores = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var culls = new List<(string Id, double Unit)>();

        // Where each template spawns naturally.
        var regions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Region(string template, string group)
        {
            if (!regions.TryGetValue(template, out var set)) regions[template] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(group);
        }
        foreach (var g in BiomeGroupData.LoadAll(db))
        {
            foreach (var l in g.HomelessSpawns) Region(l.Id, g.Id);
            foreach (var l in g.Nests)
            {
                if (db.Find(l.Id) is { Type: FcsRecordType.SquadTemplate }) Region(l.Id, g.Id);
                else if (db.Find(l.Id) is { Type: FcsRecordType.Town } nestRec && TownDataOf(nestRec) is { } nest)
                    foreach (var res in nest.Residents.Concat(nest.RoamingSquads)) Region(res.Id, g.Id);
            }
        }
        foreach (var f in factions)
            foreach (var b in f.Biomes)
                foreach (var l in f.Squads.Concat(f.SpecialSquads)) Region(l.Id, b.Id);


        // Each faction's own groups that spawn wild in some region (a patrol listed under the regions rather than on the faction's roaming
        // list: Cannibal Hunters, White Eyebrow Clan, United Heroes League...). Town-bound squads (residents, bar patrons) are not among them.
        var wildByFaction = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var id in regions.Keys)
            if (TemplateOf(db, id) is { IsUnique: false, Faction: { } owner })
            {
                if (!wildByFaction.TryGetValue(owner, out var l)) wildByFaction[owner] = l = [];
                l.Add(id);
            }

        var cullTemplates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var war = new List<(string Id, double Unit)>();
        var sizeOf = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var f in factions)
        {
            if (f.Id == FactionData.PlayerId || f.NotReal || head.ContainsKey(f.Id) || ov.Blocked(f.Id)) continue;
            bool familyOutlaw = Outlaw(f.Id) || head.Any(kv => kv.Value == f.Id && Outlaw(kv.Key));
            var people = new List<double>();
            var templates = new List<string>();
            var sizes = new List<double>();
            // The head's own roaming squads and those of its variants.
            var family = factions.Where(x => x.Id == f.Id || head.GetValueOrDefault(x.Id) == f.Id).ToList();
            var lists = family.SelectMany(x => x.Squads.Concat(x.SpecialSquads).Select(l => l.Id))
                .Concat(family.SelectMany(x => wildByFaction.GetValueOrDefault(x.Id, [])));
            foreach (var linkId in lists)
            {
                if (TemplateOf(db, linkId) is not { IsUnique: false } t || templates.Contains(t.Id) || ov.Blocked(t.Id)) continue;
                bool any = false;
                foreach (var m in Members(t))
                    if (db.Find(m.Id) is { Type: FcsRecordType.Character } member && !member.GetBool("unique") && !ov.Blocked(member.StringId))
                    {
                        people.Add(PersonScore(db, member));
                        any = true;
                    }
                if (any)
                {
                    templates.Add(t.Id);
                    sizes.Add(PlannedPeople(db, t));
                }
            }
            if (people.Count > 0)
            {
                double unit = people.Average() * Strength(f.Id);
                if (familyOutlaw) culls.Add((f.Id, unit));
                war.Add((f.Id, unit));
                sizeOf[f.Id] = sizes.Count > 0 ? sizes.Average() : 1;
                cullTemplates[f.Id] = templates;
            }
        }

        // Hunt targets: the animals of every non-unique squad (wildlife comes from the regions' spawn lists, nests and factions' roaming
        // squads alike), except a guild faction's own (pack animals, guard dogs, pets).
        // Kept animals: a race that a guild faction (or you) keeps in any of its squads, its towns' residents or its shops (pack beasts, ranch
        // herds, guard animals, pets) is tame: hunting it would turn its owners on you, so it is never a hunt target. Read from who keeps what,
        // not from names, so it holds for any mod's animals.
        var keptTemplates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in factions.Where(f => guild.Contains(f.Id) || f.Id == FactionData.PlayerId))
            foreach (var l in f.Squads.Concat(f.SpecialSquads).Concat(f.SquadDefault).Concat(f.Residents).Concat(f.BarSquads).Concat(f.DefaultResident))
                keptTemplates.Add(l.Id);
        foreach (var r in db.OfType(FcsRecordType.Town))
            if (TownDataOf(r) is { Faction: { } owner } town && (guild.Contains(owner) || owner == FactionData.PlayerId))
                foreach (var l in town.Residents.Concat(town.RoamingSquads).Concat(town.BarSquads)) keptTemplates.Add(l.Id);
        var kept = RacesIn(db, keptTemplates);

        // ...unless it also spawns wild: the regions' wild spawn lists and nests (a town that keeps goats does not make wild goats tame).
        var wildTemplates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.Biomes).Concat(db.OfType(FcsRecordType.BiomeGroup)))
            foreach (var list in new[] { "homeless spawns", "nests", "spawns" })
                foreach (var l in r.GetReferences(list))
                {
                    if (db.Find(l.TargetStringId) is not { } target) continue;
                    if (target.Type == FcsRecordType.SquadTemplate) wildTemplates.Add(target.StringId);
                    else if (target.Type == FcsRecordType.Town && TownDataOf(target) is { } nest)
                        foreach (var res in nest.Residents.Concat(nest.RoamingSquads)) wildTemplates.Add(res.Id);
                }
        foreach (var r in db.OfType(FcsRecordType.Town))
            if (TownDataOf(r) is { Type: TownType.Nest or TownType.NestMarker } nest)
                foreach (var res in nest.Residents.Concat(nest.RoamingSquads)) wildTemplates.Add(res.Id);
        kept.ExceptWith(RacesIn(db, wildTemplates));

        // Shop and new-game animals (an animal trader's stock, a start's pets) are never hunt targets: a race all of whose groups sit in a town that
        // keeps a trader, or in a new-game start, is left out (unless it also spawns wild).
        var shopOrStart = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.NewGameStartoff))
            foreach (var l in r.GetReferences("squad")) shopOrStart.Add(l.TargetStringId);
        foreach (var r in db.OfType(FcsRecordType.Town))
            if (TownDataOf(r) is { } town)
            {
                var all = town.Residents.Concat(town.BarSquads).Concat(town.RoamingSquads).Select(l => l.Id).ToList();
                if (all.Any(id => TemplateOf(db, id) is { IsTrader: true })) shopOrStart.UnionWith(all);
            }
        var placedElsewhere = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.SquadTemplate))
            if (!shopOrStart.Contains(r.StringId)) placedElsewhere.UnionWith(RacesIn(db, [r.StringId]));
        var shopRaces = RacesIn(db, shopOrStart);
        shopRaces.ExceptWith(placedElsewhere);
        shopRaces.ExceptWith(RacesIn(db, wildTemplates));
        kept.UnionWith(shopRaces);
        kept.UnionWith(ov.Tame);
        kept.ExceptWith(ov.Wild);

        var huntTemplates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var packs = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var templateRaces = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var rec in db.OfType(FcsRecordType.SquadTemplate))
        {
            if (TemplateOf(db, rec.StringId) is not { IsUnique: false } t) continue;
            if (t.Faction is { } owner && (guild.Contains(owner) || owner == FactionData.PlayerId)) continue;
            if (keptTemplates.Contains(t.Id) || ov.Blocked(t.Id)) continue;
            // Animal-only groups (wild packs, herds, nests): a squad with people in it (a patrol with its dogs, a caravan with its pack
            // beasts, slaves or prisoners) is neither a hunt nor a spawn for one, or a hunt for "K9 Unit" brings 20 armed people with it.
            bool people = t.Slaves.Count > 0 || t.Prisoners.Count > 0
                || Members(t).Any(m => db.Find(m.Id) is { Type: FcsRecordType.Character });
            if (people) continue;
            var races = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in Members(t).Concat(t.Animals).Concat(t.Animals2))
                if (db.Find(m.Id) is { Type: FcsRecordType.AnimalCharacter } animal && !animal.GetBool("unique") && !ov.Blocked(animal.StringId)
                    && !(FirstRef(animal, "race") is { } r0 && (kept.Contains(r0) || ov.Blocked(r0))))
                {
                    AddAnimal(db, animal, raceOf, animalScores);
                    if (FirstRef(animal, "race") is { } race) races.Add(race);
                }
            if (races.Count == 0) continue;
            templateRaces[t.Id] = races;
            foreach (var race in races)
            {
                double pack = Members(t).Concat(t.Animals).Concat(t.Animals2)
                    .Where(m => db.Find(m.Id) is { Type: FcsRecordType.AnimalCharacter } a && FirstRef(a, "race") == race)
                    .Sum(m => m.V1 == 0 || m.V1 == 100 ? Math.Max(1, m.V0) : (Math.Max(1, m.V0) + Math.Max(1, m.V1)) / 2.0);
                if (!packs.TryGetValue(race, out var list)) packs[race] = list = [];
                list.Add(Math.Max(1, pack));
            }
            foreach (var race in races)
            {
                if (!huntTemplates.TryGetValue(race, out var list)) huntTemplates[race] = list = [];
                list.Add(t.Id);
            }
        }
        log($"catalog: {kept.Count} kept (tame) animal races left out of hunts");
        log($"catalog: {guild.Count} guild factions: " + string.Join(", ", guild.Select(g => names.GetValueOrDefault(g, g)).Order()));

        // Every other non-unique animal still gets its race known, so a kill of it counts for a hunt of that race.
        foreach (var animal in db.OfType(FcsRecordType.AnimalCharacter))
            if (!raceOf.ContainsKey(animal.StringId) && FirstRef(animal, "race") is { } race) raceOf[animal.StringId] = race;
        foreach (var person in db.OfType(FcsRecordType.Character))
            if (FirstRef(person, "race") is { } race) raceOf.TryAdd(person.StringId, race);

        var hunts = animalScores.Where(kv => kv.Value.Count > 0)
            .Select(kv => (Id: kv.Key, Unit: (kv.Value.Average() + kv.Value.Max()) / 2 * Strength(kv.Key)))
            .ToList();

        var huntTargets = Ranked(hunts, id => db.Find(id)?.Name is { Length: > 0 } n ? n : id, Hunt);
        var packOf = packs.ToDictionary(kv => kv.Key, kv => kv.Value.Average(), StringComparer.Ordinal);
        // The hunt scale is built from small fights (at most three animals): a big herd of weak animals is no hard hunt.
        var cullTargets = Ranked(culls, id => names.GetValueOrDefault(id, id), Cull);
        // Lairs (Clear the lair quests): camps, nests and outposts with fighters living in them.
        var lairs = new Dictionary<string, Lair>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.Town))
        {
            if (ov.Blocked(r.StringId) || TownDataOf(r) is not { } town || town.Type is TownType.Town or TownType.Village) continue;
            if (town.Faction is { } owner && (owner == FactionData.PlayerId || guild.Contains(owner) || ov.Blocked(owner))) continue;
            var scores = new List<double>();
            double count = 0;
            foreach (var l in town.Residents)
            {
                if (TemplateOf(db, l.Id) is not { IsUnique: false } t) continue;
                foreach (var m in Members(t).Concat(t.Animals).Concat(t.Animals2))
                {
                    if (db.Find(m.Id) is not { } rec || rec.GetBool("unique")) continue;
                    double n = m.V1 == 0 || m.V1 == 100 ? Math.Max(1, m.V0) : (Math.Max(1, m.V0) + Math.Max(1, m.V1)) / 2.0;
                    if (rec.Type == FcsRecordType.Character) scores.Add(PersonScore(db, rec));
                    else if (rec.Type == FcsRecordType.AnimalCharacter) scores.Add(AnimalScore(db, rec));
                    else continue;
                    count += n;
                }
            }
            if (scores.Count == 0 || count < 3) continue;
            string? headOwner = town.Faction is { } o ? (head.TryGetValue(o, out var h) ? h : o) : null;
            lairs[r.StringId] = new Lair(r.StringId, town.Name.Trim(), scores.Average(), (int)Math.Round(count), headOwner);
        }

        // Region danger: per region, the median strength of the hostile groups that spawn there.
        var regionScores = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var (templateId, regs) in regions)
        {
            if (TemplateOf(db, templateId) is not { IsUnique: false } t || (t.Faction is { } tf && (guild.Contains(tf) || tf == FactionData.PlayerId))) continue;
            var scores = new List<double>();
            double count = 0;
            foreach (var m in Members(t).Concat(t.Animals).Concat(t.Animals2))
            {
                if (db.Find(m.Id) is not { } rec || rec.GetBool("unique")) continue;
                double n = m.V1 == 0 || m.V1 == 100 ? Math.Max(1, m.V0) : (Math.Max(1, m.V0) + Math.Max(1, m.V1)) / 2.0;
                if (rec.Type == FcsRecordType.Character) scores.Add(PersonScore(db, rec));
                else if (rec.Type == FcsRecordType.AnimalCharacter) scores.Add(AnimalScore(db, rec));
                else continue;
                count += n;
            }
            if (scores.Count == 0) continue;
            double score = Fight(scores.Average(), count);
            foreach (var r in regs)
            {
                if (!regionScores.TryGetValue(r, out var list)) regionScores[r] = list = [];
                list.Add(score);
            }
        }
        // Only what is above a typical region counts (every region has some middling groups roaming): the excess over the median region.
        var regionMedians = regionScores.ToDictionary(kv => kv.Key, kv => Median(kv.Value), StringComparer.Ordinal);
        double typicalRegion = Median([.. regionMedians.Values]);
        var regionDanger = regionMedians.ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value - typicalRegion), StringComparer.Ordinal);

        // Each spawnable template's size: usual, fewest and most (one pass over the data).
        var templateSizes = regions.Keys.Select(id => (Id: id, T: TemplateOf(db, id))).Where(x => x.T is not null)
            .Select(x => (x.Id, Usual: AllPlanned(db, x.T!), Least: (int)Math.Round(AllPlanned(db, x.T!, -1)), Most: (int)Math.Round(AllPlanned(db, x.T!, 1))))
            .ToList();

        var catalog = new Catalog(huntTargets, cullTargets, guild, raceOf, names)
        {
            RegionDanger = regionDanger,
            TemplateSize = templateSizes.ToDictionary(x => x.Id, x => x.Usual, StringComparer.Ordinal),
            TemplateMinSize = templateSizes.ToDictionary(x => x.Id, x => x.Least, StringComparer.Ordinal),
            TemplateMaxSize = templateSizes.ToDictionary(x => x.Id, x => x.Most, StringComparer.Ordinal),
            TemplateRegions = regions.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value, StringComparer.Ordinal),
            Lairs = lairs,
            Bandits = bandits,
            WarTargets = Ranked(war, id => names.GetValueOrDefault(id, id), Cull),
            UnitOf = war.ToDictionary(w => w.Id, w => w.Unit, StringComparer.Ordinal),
            SizeOf = sizeOf,
            PackOf = packOf,
            FamilyHead = head,
            CullTemplates = cullTemplates.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal),
            HuntTemplates = huntTemplates.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal),
            TemplateRaces = templateRaces,
        };
        catalog.Checks = catalog.Check(db);
        log($"catalog: {catalog.Checks.Count} note(s) for review" + (catalog.Checks.Count > 0 ? " (test channel \"checks\", or the Debug log)" : ""));
        return catalog;
    }

    /// <summary>How many people a squad template usually brings (the game's count rule: v0, or v0..v1 unless v1 is 0 or 100).</summary>
    /// <summary>Everyone a template plans: its people and its animals.</summary>
    /// <summary>Everyone a template plans, people and animals: the usual number (<paramref name="end"/> 0), the fewest (-1) or the most (1).</summary>
    static double AllPlanned(GameDatabase db, SquadTemplate t, int end = 0)
    {
        double Pick(double lo, double hi) => end < 0 ? lo : end > 0 ? hi : (lo + hi) / 2.0;
        double Count(RecordLink l) => l.V1 == 0 || l.V1 == 100 ? Math.Max(1, l.V0) : Pick(Math.Max(1, l.V0), Math.Max(1, l.V1));
        bool Person(RecordLink l) => db.Find(l.Id) is { Type: FcsRecordType.Character };
        double n = t.Leader is { } leader && Person(leader) ? 1 : 0;
        n += t.Squad.Where(Person).Sum(Count) + t.Squad2.Where(Person).Sum(Count);
        if (t.ChooseFrom.Any(Person)) n += Pick(t.NumRandomChars, Math.Max(t.NumRandomChars, t.NumRandomCharsMax));
        n += t.Animals.Sum(Count) + t.Animals2.Sum(Count);
        return Math.Max(1, n);
    }

    static double PlannedPeople(GameDatabase db, SquadTemplate t)
    {
        static double Count(RecordLink l) => l.V1 == 0 || l.V1 == 100 ? Math.Max(1, l.V0) : (Math.Max(1, l.V0) + Math.Max(1, l.V1)) / 2.0;
        bool Person(RecordLink l) => db.Find(l.Id) is { Type: FcsRecordType.Character };
        double n = t.Leader is { } leader && Person(leader) ? 1 : 0;
        n += t.Squad.Where(Person).Sum(Count) + t.Squad2.Where(Person).Sum(Count);
        if (t.ChooseFrom.Any(Person)) n += (t.NumRandomChars + Math.Max(t.NumRandomChars, t.NumRandomCharsMax)) / 2.0;
        return Math.Max(1, n);
    }

    /// <summary>The animal races in <paramref name="templates"/>.</summary>
    static HashSet<string> RacesIn(GameDatabase db, IEnumerable<string> templates)
    {
        var races = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in templates)
        {
            if (TemplateOf(db, id) is not { } t) continue;
            foreach (var m in Members(t).Concat(t.Animals).Concat(t.Animals2))
                if (db.Find(m.Id) is { Type: FcsRecordType.AnimalCharacter } a && FirstRef(a, "race") is { } race) races.Add(race);
        }
        return races;
    }

    /// <summary>
    /// Notes for review: what may be wrong in this load order, so a load order nobody tested can be looked over (the test channel's "checks",
    /// the offline catalog check). Names are used here only to point a person at something, never to decide anything.
    /// </summary>
    List<string> Check(GameDatabase db)
    {
        var notes = new List<string>();
        foreach (var h in Hunts)
        {
            if (h.Name.Contains("domestic", StringComparison.OrdinalIgnoreCase) || h.Name.Contains("tame", StringComparison.OrdinalIgnoreCase)
                || h.Name.Contains("pet", StringComparison.OrdinalIgnoreCase))
                notes.Add($"hunt '{h.Name.Trim()}' ({h.Id}) is named like a tame animal: it spawns somewhere wild with no friendly keeper; add it to \"tame\" if wrong");
            if (!HuntTemplates.GetValueOrDefault(h.Id, []).Any(t => TemplateRegions.ContainsKey(t)))
                notes.Add($"hunt '{h.Name.Trim()}' ({h.Id}) has no region it naturally spawns in: offered only when enough are already near the town");
        }
        foreach (var c in WarTargets)
            if (!CullTemplates.GetValueOrDefault(c.Id, []).Any(t => TemplateRegions.ContainsKey(t)))
                notes.Add($"kill target '{c.Name.Trim()}' ({c.Id}) has no region it naturally spawns in: offered only when enough are already near the town");
        // Rank coverage: each rank should have something to offer.
        for (int r = 0; r <= Ranks.Top; r++)
        {
            int hunts = Hunts.Count(h => Enumerable.Range(2, 11).Any(n => QuestRank(Hunt, h.Unit, n, PackOf.GetValueOrDefault(h.Id, 1)) == r));
            int culls = Culls.Count(c => Enumerable.Range(2, 11).Any(n => QuestRank(Cull, UnitOf.GetValueOrDefault(c.Id, c.Unit), n, SizeOf.GetValueOrDefault(c.Id, 1)) == r));
            if (hunts + culls == 0) notes.Add($"rank {Ranks.Name(r)}: no hunt or General kill target reaches it in this load order");
        }
        if (GuildFactions.Count == 0) notes.Add("no guild factions found: no clerks will offer work");
        return notes;
    }

    static SquadTemplate? TemplateOf(GameDatabase db, string id)
    {
        if (db.Find(id) is not { Type: FcsRecordType.SquadTemplate } rec) return null;
        try { return SquadTemplate.From(rec); }
        catch { return null; }
    }

    static TownData? TownDataOf(GameRecord r)
    {
        try { return TownData.From(r); }
        catch { return null; }
    }

    static IEnumerable<RecordLink> Members(SquadTemplate t)
    {
        if (t.Leader is { } leader) yield return leader;
        foreach (var m in t.Squad) yield return m;
        foreach (var m in t.Squad2) yield return m;
        foreach (var m in t.ChooseFrom) yield return m;
    }

    static void AddAnimal(GameDatabase db, GameRecord animal, Dictionary<string, string> raceOf, Dictionary<string, List<double>> scores)
    {
        if (FirstRef(animal, "race") is not { } race) return;
        raceOf[animal.StringId] = race;
        if (!scores.TryGetValue(race, out var list)) scores[race] = list = [];
        list.Add(AnimalScore(db, animal));
    }

    static string? FirstRef(GameRecord r, string list) => r.GetReferences(list) is { Count: > 0 } refs ? refs[0].TargetStringId : null;

    // Fighting strength (one scale for people and animals): damage times toughness.
    //   damage    = combat skill (animals: the average of their combat skill and "animal strength", the weight behind their blows), raised
    //               softly by the natural weapon's best damage multiplier (a bonedog's bite 4x cut, a gorillo's fist 2x blunt) and the weapon
    //               make's quality (its best damage mod);
    //   toughness = how much they can take: the race's max blood (a goat 80, a beak thing 400, a leviathan 5000) times the HP multiplier,
    //               and for people their armour grade, measured against a human's (250). Damage weighs more (power 1.5) than toughness (power 0.3): Kenshi
    //               fights are lost to damage more than to a long fight; a natural weapon's multiplier counts only a little (power 0.25).
    // A fight then weighs strength squared times how many you face (Fight), so quality counts for more than numbers.

    /// <summary>A person's fighting strength.</summary>
    static double PersonScore(GameDatabase db, GameRecord r)
    {
        double combat = r.GetInt("combat stats");
        if (FirstRef(r, "stats") is { } statsId && db.Find(statsId) is { } stats)
            combat = Math.Max(combat, (stats.GetFloat("attack") + stats.GetFloat("defence")) / 2);
        combat += r.GetInt("strength") * 0.1;
        double armour = Math.Clamp(r.GetInt("armour grade"), 0, 6);
        double damage = Math.Max(1, combat) * Math.Sqrt(MakeQuality(db, r));
        double toughness = Math.Pow(Blood(db, r, 250) / 250.0 * (1 + 0.2 * armour), 0.3);
        return Math.Max(0.1, Math.Pow(damage, 1.5) * toughness / 10);
    }

    /// <summary>An animal's fighting strength.</summary>
    static double AnimalScore(GameDatabase db, GameRecord r)
    {
        double combat = r.GetInt("combat stats");
        double power = Math.Max(r.GetInt("animal strength"), combat);
        double hp = r.GetFloat("HP mult", 1);
        if (hp <= 0) hp = 1;
        double bite = 1;
        if (FirstRef(r, "weapon") is { } w && db.Find(w) is { } weapon)
            bite = Math.Clamp(Math.Max(weapon.GetFloat("cut damage multiplier", 1), weapon.GetFloat("blunt damage multiplier", 1)), 0.5, 5);
        double damage = Math.Max(1, (combat + power) / 2) * Math.Pow(bite, 0.25) * Math.Sqrt(MakeQuality(db, r));
        double toughness = Math.Pow(Blood(db, r, 150) * hp / 250.0, 0.3);
        return Math.Max(0.1, Math.Pow(damage, 1.5) * toughness / 10);
    }

    /// <summary>The race's max blood (its total health), or <paramref name="fallback"/> when the data has none.</summary>
    static double Blood(GameDatabase db, GameRecord r, double fallback)
    {
        double blood = FirstRef(r, "race") is { } race && db.Find(race) is { } rec ? rec.GetFloat("max blood") : 0;
        return blood > 0 ? blood : fallback;
    }

    /// <summary>The weapon make's quality (the record's "weapon level": its best cut or blunt damage mod), 1 when there is none.</summary>
    static double MakeQuality(GameDatabase db, GameRecord r)
    {
        if (FirstRef(r, "weapon level") is not { } id || db.Find(id) is not { } make) return 1;
        double q = Math.Max(make.GetFloat("cut damage mod", 1), make.GetFloat("blunt damage mod", 1));
        return q > 0 ? Math.Clamp(q, 0.3, 3) : 1;
    }

    /// <summary>Sorts <paramref name="units"/> and gives each a rank by where it falls (30/25/20/15/10 per cent, D to S).</summary>
    static double Median(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count == 0 ? 0 : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    static List<Target> Ranked(List<(string Id, double Unit)> units, Func<string, string> name, string kind)
    {
        var sorted = units.OrderBy(u => u.Unit).ThenBy(u => u.Id, StringComparer.Ordinal).ToList();
        var result = new List<Target>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            double p = sorted.Count == 1 ? 0 : (double)i / (sorted.Count - 1);
            int rank = p < 0.30 ? 0 : p < 0.55 ? 1 : p < 0.75 ? 2 : p < 0.90 ? 3 : 4;
            result.Add(new Target(kind, sorted[i].Id, name(sorted[i].Id), Math.Round(sorted[i].Unit, 2), rank));
        }
        return result;
    }
}
