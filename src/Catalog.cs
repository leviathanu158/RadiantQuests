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

    /// <summary>Factions that run bars in their towns: they have guild clerks and take part in the guild.</summary>
    public IReadOnlySet<string> GuildFactions { get; }

    /// <summary>A character or animal record's race (for matching hunt kills).</summary>
    public IReadOnlyDictionary<string, string> RaceOf { get; }

    /// <summary>Factions the game data marks as bandits (fundamental type Bandit).</summary>
    public IReadOnlySet<string> Bandits { get; private init; } = new HashSet<string>();

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

    public static Catalog Build(GameDatabase db, Action<string> log)
    {
        var factions = FactionData.LoadAll(db);
        var names = factions.ToDictionary(f => f.Id, f => f.Name, StringComparer.Ordinal);

        // The relations every faction starts the game with (the game's own rule: an explicit entry, else the lower of the two defaults).
        // Nothing here reads your relations: the game data only. Whether a clerk deals with you is checked against the live relations in play.
        var start = FactionRelations.Build(factions);

        // Owners of a town or village with a bar (its TOWN record lists bar squads).
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in db.OfType(FcsRecordType.Town))
        {
            if (TownDataOf(r) is not { Faction: { } owner } town || town.BarSquads.Count == 0) continue;
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
        var guild = new HashSet<string>(owners.Where(o => !bandits.Contains(o) && !HostileToMost(o, ownerIdx)), StringComparer.Ordinal);
        int[] guildIdx = [.. guild.Select(start.IndexOf).Where(i => i >= 0)];

        // Cull targets: outlaws to the guild factions (bandits, cannibals, fogmen...), never a guild faction. General quests check the live
        // relations again when a board is made.
        bool Outlaw(string id) => !guild.Contains(id) && (bandits.Contains(id) || HostileToMost(id, guildIdx));

        var raceOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var animalScores = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var culls = new List<(string Id, double Unit)>();

        foreach (var f in factions)
        {
            if (f.Id == FactionData.PlayerId || f.NotReal || !Outlaw(f.Id)) continue;
            var people = new List<double>();
            foreach (var link in f.Squads.Concat(f.SpecialSquads))
            {
                if (TemplateOf(db, link.Id) is not { IsUnique: false } t) continue;
                foreach (var m in Members(t))
                    if (db.Find(m.Id) is { Type: FcsRecordType.Character } member && !member.GetBool("unique")) people.Add(PersonScore(db, member));
            }
            if (people.Count > 0) culls.Add((f.Id, people.Average()));
        }

        // Hunt targets: the animals of every non-unique squad (wildlife comes from the regions' spawn lists, nests and factions' roaming
        // squads alike), except a guild faction's own (pack animals, guard dogs, pets).
        foreach (var rec in db.OfType(FcsRecordType.SquadTemplate))
        {
            if (TemplateOf(db, rec.StringId) is not { IsUnique: false } t) continue;
            if (t.Faction is { } owner && (guild.Contains(owner) || owner == FactionData.PlayerId)) continue;
            foreach (var m in Members(t).Concat(t.Animals).Concat(t.Animals2))
                if (db.Find(m.Id) is { Type: FcsRecordType.AnimalCharacter } animal && !animal.GetBool("unique"))
                    AddAnimal(db, animal, raceOf, animalScores);
        }
        log($"catalog: {guild.Count} guild factions: " + string.Join(", ", guild.Select(g => names.GetValueOrDefault(g, g)).Order()));

        // Every other non-unique animal still gets its race known, so a kill of it counts for a hunt of that race.
        foreach (var animal in db.OfType(FcsRecordType.AnimalCharacter))
            if (!raceOf.ContainsKey(animal.StringId) && FirstRef(animal, "race") is { } race) raceOf[animal.StringId] = race;
        foreach (var person in db.OfType(FcsRecordType.Character))
            if (FirstRef(person, "race") is { } race) raceOf.TryAdd(person.StringId, race);

        var hunts = animalScores.Where(kv => kv.Value.Count > 0)
            .Select(kv => (Id: kv.Key, Unit: kv.Value.Average()))
            .ToList();

        var huntTargets = Ranked(hunts, id => db.Find(id)?.Name is { Length: > 0 } n ? n : id, Hunt);
        var cullTargets = Ranked(culls, id => names.GetValueOrDefault(id, id), Cull);
        return new Catalog(huntTargets, cullTargets, guild, raceOf, names) { Bandits = bandits };
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
        list.Add(AnimalScore(animal));
    }

    static string? FirstRef(GameRecord r, string list) => r.GetReferences(list) is { Count: > 0 } refs ? refs[0].TargetStringId : null;

    /// <summary>A person's fighting strength.</summary>
    static double PersonScore(GameDatabase db, GameRecord r)
    {
        double combat = r.GetInt("combat stats");
        if (FirstRef(r, "stats") is { } statsId && db.Find(statsId) is { } stats)
        {
            double fromStats = (stats.GetFloat("attack") + stats.GetFloat("defence")) / 2;
            combat = Math.Max(combat, fromStats);
        }
        double armour = Math.Clamp(r.GetInt("armour grade"), 0, 6);
        double strength = r.GetInt("strength");
        return Math.Max(1, combat * (1 + 0.15 * armour) + strength * 0.2);
    }

    /// <summary>An animal's fighting strength.</summary>
    static double AnimalScore(GameRecord r)
    {
        double combat = r.GetInt("combat stats");
        double hp = r.GetFloat("HP mult", 1);
        if (hp <= 0) hp = 1;
        return Math.Max(1, combat * Math.Sqrt(hp));
    }

    /// <summary>Sorts <paramref name="units"/> and gives each a rank by where it falls (30/25/20/15/10 per cent, D to S).</summary>
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
