// Radiant Quests for Meitou. GPLv3 or later.
// The ONE place the mod reads or changes Meitou's world (groups, people, spawning, regions, relations, money). Every other part goes through
// here, so a Meitou update that renames something touches this file only. Each area is guarded on its own: the first exception in it is logged
// once and that area switches itself off for the session (its methods then answer "nothing"), while the rest of the mod keeps running.
using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Economy;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace RadiantQuests;

/// <summary>One roaming group (a platoon) as the mod sees it.</summary>
/// <param name="Standing">Members able to fight now: for a loaded group, those alive, not down, not carried and not locked up; for one off screen,
/// its size.</param>
/// <param name="Age">The mean age of its standing animals (Kenshi's age is their stat multiplier: under 0.4 a pup, up to 1.1 adult, above that
/// an elder); 1 when it has none or isn't loaded.</param>
/// <param name="Strength">What their ages do to their fighter strength: the square root of the mean of age^1.5, so a fight's score (strength
/// squared) is the SUM of each animal's age^1.5 share - adding a pup never makes a herd easier; 1 as above.</param>
public sealed record GroupInfo(int Id, ulong Key, string TemplateId, int Faction, string? FactionId, Vector2 At, int Size, int Standing, string Tag,
    bool ModSpawned, float Age = 1, float Strength = 1);

/// <summary>What the mod needs to know about one person.</summary>
public readonly record struct PersonFacts(CharacterId Id, ulong Lasting, int Faction, string Record, bool Down, bool Taken, string? GroupTag,
    int HomeSite = -1, ulong GroupKey = 0);

/// <summary>A place on the map (a town site) as it is now: its current TOWN record (a camp wiped out is replaced by another version).</summary>
public sealed record SiteInfo(int Index, string TownId, string Name, Vector2 At, float Radius = 0);

/// <summary>One person living at a camp: lasting id, still standing, distance from the camp, distance to the nearest of yours.</summary>
public readonly record struct CampPerson(ulong Id, bool Standing, float FromCamp, float FromYou);


public sealed class WorldAccess(PopulationData data, PopulationSystem? population)
{
    /// <summary>The areas, each switched off on its first failure.</summary>
    public enum Area { Groups, Spawning, People, Regions, Relations, Ground, Items, Orders }

    static readonly HashSet<Area> s_off = [];

    public static bool IsOff(Area a) => s_off.Contains(a);

    public PopulationData Data => data;

    /// <summary>Runs <paramref name="f"/> for <paramref name="area"/>; on its first failure the area is switched off for the session.</summary>
    T Guard<T>(Area area, T fallback, Func<T> f)
    {
        if (s_off.Contains(area)) return fallback;
        try { return f(); }
        catch (Exception e)
        {
            s_off.Add(area);
            RadiantQuestsMod.Log($"{area} turned OFF (this Meitou changed what it uses; the rest keeps running): {e.GetType().Name}: {e.Message}");
            return fallback;
        }
    }

    #region Groups

    /// <summary>This tick's group list (built once per tick: it walks every platoon and every loaded member), and its tick.</summary>
    List<GroupInfo>? _groups;
    long _groupsTick = long.MinValue;

    /// <summary>
    /// Every living roaming group in the world (loaded or off screen), as of this tick. Built once per tick and shared by every caller (read
    /// only); spawning, tagging or removing a group renews it.
    /// </summary>
    public IReadOnlyList<GroupInfo> Groups(World world)
    {
        if (_groups is not null && _groupsTick == world.Tick) return _groups;
        _groups = BuildGroups(world);
        _groupsTick = world.Tick;
        return _groups;
    }

    /// <summary>The group list must be built again (a group was added, tagged or removed).</summary>
    void Changed() => _groups = null;

    List<GroupInfo> BuildGroups(World world) => Guard(Area.Groups, [], () =>
    {
        var list = new List<GroupInfo>();
        foreach (var p in world.Platoons.All)
        {
            if (p.Dead) continue;
            int standing = Math.Max(1, p.Size);
            var at = p.Position;
            float ages = 0, strength = 0;
            int animals = 0;
            if (p.SquadId >= 0 && world.Squads.Find(p.SquadId) is { } squad)
            {
                standing = 0;
                foreach (var m in squad.Members)
                {
                    if (Person(world, m) is not { Down: false, Taken: false }) continue;
                    standing++;
                    if (AgeOf(world, m) is { } age)
                    {
                        animals++;
                        ages += age;
                        // Each animal's share of the fight (the score goes with strength squared): age^1.5 (his Garru report, 2026-11-24: the
                        // mean of age^1.5, squared, let two pups pull two adults down a rank; a young adult still bites hard).
                        strength += MathF.Pow(Math.Max(0, age), 1.5f);
                    }
                }
                if (world.Characters.TryResolveNext(squad.Leader, out int slot))
                    at = new Vector2(world.Characters.Next[slot].Position.X, world.Characters.Next[slot].Position.Z);
            }
            list.Add(new GroupInfo(p.Id, p.Key, p.TemplateId, p.Faction, p.Faction >= 0 ? data.FactionId(p.Faction) : null, at, Math.Max(1, p.Size),
                standing, p.ModTag, p.ModSpawned, animals > 0 ? ages / animals : 1, animals > 0 ? MathF.Sqrt(strength / animals) : 1));
        }
        return list;
    });

    /// <summary>Tags the living group with key <paramref name="key"/> for a quest (only an untagged group the game made); false if gone.</summary>
    public bool TagGroup(World world, ulong key, string tag) => Guard(Area.Groups, false, () =>
    {
        var p = world.Platoons.All.FirstOrDefault(x => x.Key == key && !x.Dead && x.ModTag.Length == 0 && !x.ModSpawned);
        if (p is null) return false;
        p.ModTag = tag;
        Changed();
        return true;
    });

    /// <summary>
    /// How one of your characters stands to group <paramref name="platoonId"/>'s standing members: one within <paramref name="near"/> of any, one
    /// within <paramref name="reach"/> of any (in the fight). False, false when the group isn't loaded.
    /// </summary>
    public (bool Near, bool Contact) Contact(World world, int platoonId, float near, float reach) => Guard(Area.Groups, (false, false), () =>
    {
        if (world.Platoons.Find(platoonId) is not { } p || p.SquadId < 0 || world.Squads.Find(p.SquadId) is not { } squad) return (false, false);
        var table = world.Characters;
        var yours = new List<Vector2>();
        for (int s = 0; s < table.Next.Length; s++)
            if (table.Next[s].Alive && table.Cold(s) is { IsPlayer: true }) yours.Add(new Vector2(table.Next[s].Position.X, table.Next[s].Position.Z));
        bool isNear = false, contact = false;
        foreach (var m in squad.Members)
        {
            if (Person(world, m) is not { Down: false, Taken: false } || !table.TryResolveNext(m, out int slot)) continue;
            var at = new Vector2(table.Next[slot].Position.X, table.Next[slot].Position.Z);
            foreach (var y in yours)
            {
                float d = Vector2.Distance(y, at);
                if (d <= near) isNear = true;
                if (d <= reach) contact = true;
            }
        }
        return (isNear, contact);
    });

    /// <summary>The people of group <paramref name="platoonId"/> in the world now (empty when it isn't loaded).</summary>
    public List<PersonFacts> Members(World world, int platoonId) => Guard(Area.People, new List<PersonFacts>(), () =>
    {
        var list = new List<PersonFacts>();
        if (world.Platoons.Find(platoonId) is not { } p || p.SquadId < 0 || world.Squads.Find(p.SquadId) is not { } squad) return list;
        foreach (var m in squad.Members)
            if (Person(world, m) is { } person) list.Add(person);
        return list;
    });

    /// <summary>Gives back the groups the game made that were only tagged for a quest (those the mod spawned keep the tag: they are cleared up later).</summary>
    public void ReleaseNatural(World world, string tag) => Guard(Area.Groups, 0, () =>
    {
        foreach (var p in world.Platoons.All)
            if (p.ModTag == tag && !p.ModSpawned) p.ModTag = "";
        Changed();
        return 0;
    });

    /// <summary>
    /// Removes a group the mod spawned from the world. One with a member carried off or locked up by you (or being made) is given back to the
    /// game instead, so nothing you hold vanishes. True when it is gone or given back.
    /// </summary>
    public bool Despawn(World world, int platoonId) => Guard(Area.Spawning, false, () =>
    {
        if (population is null || world.Platoons.Find(platoonId) is not { ModSpawned: true } p) return false;
        bool held = p.SquadId >= 0 && world.Squads.Find(p.SquadId) is { } squad && squad.Members.Any(m => Person(world, m) is { Taken: true });
        if (!held)
        {
            // The game may refuse a removal at this moment (its phase rules): not a broken Meitou, just try again later.
            try
            {
                if (population.DespawnPlatoon(world, p.Id))
                {
                    Changed();
                    return true;
                }
            }
            catch (InvalidOperationException) { return false; }
        }
        if (!held) return false;   // being made, or a fight is being settled: try again later
        p.ModSpawned = false;
        p.ModTag = "";
        Changed();
        return true;
    });


    #endregion

    #region Spawning

    /// <summary>The faction a template spawns for (its own, a roaming list's, else wildlife); -1 when the data can't say.</summary>
    public int SpawnFaction(string template) => Guard(Area.Spawning, -1, () =>
        data.Db.Find(template) is { Type: FcsRecordType.SquadTemplate } rec ? data.SpawnFactionOf(SquadTemplate.From(rec)) : -1);

    /// <summary>
    /// Spawns one ordinary group of <paramref name="template"/> at <paramref name="at"/>, tagged; returns its size (0: not spawned). With
    /// <paramref name="most"/> &gt; 0 and a Meitou that has the size cap (its SpawnPlatoon's maxMembers, merged 2026-11-24), the group holds at
    /// most that many; an older Meitou spawns the group whole (the quest then fits its count to the group).
    /// </summary>
    public (int Size, ulong Key) Spawn(World world, int faction, string template, int homeSite, Vector2 at, string tag, int most = 0) =>
        Guard(Area.Spawning, (0, 0UL), () =>
    {
        if (population is null || homeSite < 0) return (0, 0UL);
        // A tight ring round the spot the mod checked (GoodSpot): the group stands where it was judged dry, open and reachable.
        int id = most > 0 && CapSpawn is { } capped
            ? (int)capped.Invoke(population, [world, faction, template, homeSite, at, 0f, SpawnSpread, 0L, most])!
            : population.SpawnPlatoon(world, faction, template, homeSite, at, 0, SpawnSpread);
        if (id < 0 || world.Platoons.Find(id) is not { } p) return (0, 0UL);
        p.ModTag = tag;
        Changed();
        return (Math.Max(1, p.Size), p.Key);
    });

    /// <summary>How far from the checked spot the game may place the group (units).</summary>
    const float SpawnSpread = 120;

    /// <summary>
    /// Whether a group can be put at <paramref name="at"/> (his rules, 2026-11-23): walkable; dry ground, at the spot and all round it (no
    /// water, no island or shore); not steep; and an unbroken walkable surface from it toward <paramref name="toward"/> (the town you come from),
    /// so it can be reached and fought, not stuck on a cliff top or in a closed-off pocket. Cheap: ground heights and Meitou's navmesh straight
    /// line test, the one fights use every tick (a route search may not run inside a tick, Meitou's rule). Null when good, else why not.
    /// </summary>
    public string? SpotProblem(World world, Vector2 at, Vector2 toward) => Guard<string?>(Area.Ground, null, () =>
    {
        var w = world.Walkability;
        if (!w.IsWalkable(at.X, at.Y)) return "not walkable";
        const float dry = Meitou.Data.World.WorldWater.Height + 3;
        float h0 = w.TerrainHeight(at.X, at.Y);
        if (h0 <= dry) return "water";
        float lo = h0, hi = h0;
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.PI / 4;
            float h = w.TerrainHeight(at.X + MathF.Cos(a) * SpotReach, at.Y + MathF.Sin(a) * SpotReach);
            if (h <= dry) return "water near";
            lo = MathF.Min(lo, h);
            hi = MathF.Max(hi, h);
        }
        if (hi - lo > SteepRise) return "steep";
        var dir = toward - at;
        if (dir.LengthSquared() > 1)
        {
            var to = at + Vector2.Normalize(dir) * MathF.Min(ReachTest, dir.Length());
            if (!w.IsWalkable(to.X, to.Y)) return "cut off";
            if (!w.LineOfSight(new Vector3(at.X, w.GroundHeight(at.X, at.Y), at.Y), new Vector3(to.X, w.GroundHeight(to.X, to.Y), to.Y))) return "cut off";
        }
        return null;
    });

    /// <summary>How far round a spot the ground must be dry and not steep, the most it may rise within that, and how far toward the town the
    /// surface must run unbroken (units; mod choices).</summary>
    const float SpotReach = 150, SteepRise = 110, ReachTest = 800;

    static System.Reflection.MethodInfo? s_capSpawn;
    static bool s_capLooked;

    /// <summary>Meitou's SpawnPlatoon with a member cap (9 parameters: ..., long money, int maxMembers), or null in a Meitou that hasn't got it.</summary>
    static System.Reflection.MethodInfo? CapSpawn
    {
        get
        {
            if (s_capLooked) return s_capSpawn;
            s_capLooked = true;
            s_capSpawn = typeof(PopulationSystem).GetMethods().FirstOrDefault(m => m.Name == "SpawnPlatoon" && m.GetParameters() is { Length: 9 } ps
                && ps[^1].ParameterType == typeof(int) && ps[^2].ParameterType == typeof(long) && ps[0].ParameterType == typeof(World));
            RadiantQuestsMod.Log(s_capSpawn is null
                ? "group size cap: not in this Meitou yet (brought-in groups come whole; quests fit their count to them)"
                : "group size cap: on (brought-in groups are spawned at the size the quest asks for)");
            return s_capSpawn;
        }
    }

    /// <summary>Whether this Meitou can spawn a group capped to a size.</summary>
    public static bool CanCap => CapSpawn is not null;

    /// <summary>
    /// The age of <paramref name="c"/> if it is an animal (its stat multiplier), else null. Read only.
    /// </summary>
    public static float? AgeOf(World world, CharacterId c) =>
        world.Characters.TryResolveNext(c, out int s) && world.Characters.Cold(s) is { Animal: not null } cold ? cold.Age : null;

    /// <summary>
    /// Sets every animal of group <paramref name="platoonId"/> to <paramref name="age"/> through the game's own growth path (stats and body size
    /// follow). Returns how many were set; 0 when the group isn't loaded.
    /// </summary>
    public int SetAges(World world, int platoonId, float age) => Guard(Area.Spawning, 0, () =>
    {
        if (world.Platoons.Find(platoonId) is not { } p || p.SquadId < 0 || world.Squads.Find(p.SquadId) is not { } squad) return 0;
        int n = 0;
        foreach (var m in squad.Members)
            if (world.Characters.TryResolveNext(m, out int s) && world.Characters.Cold(s) is { Animal: not null } cold
                && Meitou.Simulation.Bodies.BodySystem.SetAnimalAge(cold, age, refreshLook: true)) n++;
        return n;
    });

    /// <summary>
    /// What is wrong with a freshly brought-in, loaded group (null: nothing): none of its people in the world, most of them missing, or one
    /// under the ground or high in the air. Read only.
    /// </summary>
    public string? GroupProblem(World world, int platoonId) => Guard<string?>(Area.Ground, null, () =>
    {
        if (world.Platoons.Find(platoonId) is not { } p || p.SquadId < 0 || world.Squads.Find(p.SquadId) is not { } squad) return null;
        var table = world.Characters;
        var w = world.Walkability;
        int there = 0;
        foreach (var m in squad.Members)
        {
            if (!table.TryResolveNext(m, out int s)) continue;
            there++;
            var at = table.Next[s].Position;
            float ground = w.TerrainHeight(at.X, at.Z);
            if (at.Y < ground - 30) return "a member is under the ground";
            if (at.Y > w.GroundHeight(at.X, at.Z) + 200) return "a member is high in the air";
        }
        if (there == 0) return "none of its members is in the world";
        if (p.Size > 0 && there * 2 < p.Size) return $"only {there} of {p.Size} members are in the world";
        return null;
    });

    /// <summary>Test channel: sets group <paramref name="platoonId"/>'s animals to <paramref name="ages"/> in order (the last repeats); how many.</summary>
    public int SetAgesEach(World world, int platoonId, IReadOnlyList<float> ages) => Guard(Area.Spawning, 0, () =>
    {
        if (ages.Count == 0 || world.Platoons.Find(platoonId) is not { } p || p.SquadId < 0 || world.Squads.Find(p.SquadId) is not { } squad) return 0;
        int n = 0;
        foreach (var m in squad.Members)
            if (world.Characters.TryResolveNext(m, out int s) && world.Characters.Cold(s) is { Animal: not null } cold
                && Meitou.Simulation.Bodies.BodySystem.SetAnimalAge(cold, ages[Math.Min(n, ages.Count - 1)], refreshLook: true)) n++;
        Changed();
        return n;
    });

    /// <summary>Whether group <paramref name="platoonId"/>'s members are in the world now (loaded), so their ages can be read.</summary>
    public bool Loaded(World world, int platoonId) => Guard(Area.Groups, false, () =>
        world.Platoons.Find(platoonId) is { SquadId: >= 0 } p && world.Squads.Find(p.SquadId) is { Members.Count: > 0 });

    #endregion

    #region People

    /// <summary>What the mod needs to know about <paramref name="c"/>, or null when they are not in the world.</summary>
    public PersonFacts? Person(World world, CharacterId c)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(c, out int s) || table.Cold(s) is not { } cold) return null;
        bool down = cold.Medical is { Incapacitated: true } || !table.Next[s].Alive;
        bool taken = cold.IsImprisoned || (cold.CarriedBy != CharacterId.None && IsYours(world, cold.CarriedBy));
        string? tag = null;
        int home = -1;
        ulong groupKey = 0;
        if (cold.SquadId >= 0 && world.Squads.Find(cold.SquadId) is { } squad)
        {
            home = squad.Town;
            if (squad.PlatoonId >= 0 && world.Platoons.Find(squad.PlatoonId) is { } platoon && platoon.ModTag.StartsWith("radiantquests:", StringComparison.Ordinal))
            {
                tag = platoon.ModTag;
                groupKey = platoon.Key;
            }
        }
        return new PersonFacts(c, cold.LastingId.Value, cold.Faction, cold.RecordId, down, taken, tag, home, groupKey);
    }

    /// <summary>One of yours: a player character or a member of your faction (your animals too).</summary>
    public bool IsYours(World world, CharacterId c) =>
        world.Characters.TryResolveNext(c, out int s) && world.Characters.Cold(s) is { } cold
        && (cold.IsPlayer || (data.PlayerFaction >= 0 && cold.Faction == data.PlayerFaction));

    /// <summary>Gives <paramref name="c"/> a lasting id (serial phases only), so a defeat counts once even after a load.</summary>
    public void Remember(World world, CharacterId c) => Guard(Area.People, 0, () => { world.LastingIds.GetOrGive(c); return 0; });

    /// <summary>A person's lasting id, given now if they have none (serial phases only); 0 when not in the world.</summary>
    public ulong LastingOf(World world, CharacterId c) => Guard(Area.People, 0UL, () =>
        world.Characters.TryResolveNext(c, out _) ? world.LastingIds.GetOrGive(c).Value : 0UL);

    /// <summary>Whether <paramref name="c"/> stands within <paramref name="range"/> of <paramref name="at"/>.</summary>
    public bool Within(World world, CharacterId c, Vector2 at, float range) => Guard(Area.People, false, () =>
        world.Characters.TryResolveNext(c, out int s)
        && Vector2.Distance(new Vector2(world.Characters.Next[s].Position.X, world.Characters.Next[s].Position.Z), at) <= range);

    /// <summary>Whether one of your characters stands within <paramref name="reach"/> of <paramref name="at"/> (ground distance).</summary>
    public bool YoursNearPoint(World world, Vector2 at, float reach) => Guard(Area.People, true, () =>
    {
        var table = world.Characters;
        for (int s = 0; s < table.Next.Length; s++)
            if (table.Next[s].Alive && table.Cold(s) is { IsPlayer: true }
                && Vector2.Distance(new Vector2(table.Next[s].Position.X, table.Next[s].Position.Z), at) <= reach) return true;
        return false;
    });

    /// <summary>Whether one of your characters stands within <paramref name="reach"/> of <paramref name="c"/> (ground distance).</summary>
    public bool YoursNear(World world, CharacterId c, float reach) => Guard(Area.People, false, () =>
    {
        var table = world.Characters;
        if (!table.TryResolveNext(c, out int cs)) return false;
        var at = table.Next[cs].Position;
        var next = table.Next;
        for (int s = 0; s < next.Length; s++)
        {
            if (!next[s].Alive || table.Cold(s) is not { IsPlayer: true }) continue;
            var d = new Vector2(next[s].Position.X - at.X, next[s].Position.Z - at.Z);
            if (d.LengthSquared() <= reach * reach) return true;
        }
        return false;
    });

    #endregion

    #region Sites

    /// <summary>The sites within <paramref name="range"/> of <paramref name="at"/>, as they are now.</summary>
    public List<SiteInfo> SitesNear(Vector2 at, float range) => Guard<List<SiteInfo>>(Area.Groups, [], () =>
        [.. data.Sites.Select(s => new SiteInfo(s.Index, s.Town.Id, s.Town.Name, new Vector2(s.Position.X, s.Position.Z), s.Town.Radius))
            .Where(s => Vector2.Distance(s.At, at) <= range)]);

    /// <summary>The site <paramref name="index"/> as it is now, or null.</summary>
    public SiteInfo? Site(int index) => Guard<SiteInfo?>(Area.Groups, null, () =>
        index >= 0 && index < data.Sites.Count && data.Sites[index] is { } s
            ? new SiteInfo(s.Index, s.Town.Id, s.Town.Name, new Vector2(s.Position.X, s.Position.Z), s.Town.Radius) : null);

    /// <summary>
    /// The people who live at site <paramref name="index"/> (their squads' home is that site) and are in the world now: lasting id (given here,
    /// serial phases only), standing or not, and distance from <paramref name="at"/>; plus the distances of your characters from it.
    /// </summary>
    public (List<CampPerson> People, List<float> Yours) CampPeople(World world, int index, Vector2 at) => Guard(Area.Groups, (new List<CampPerson>(), new List<float>()), () =>
    {
        var table = world.Characters;
        var yours = new List<(Vector2 P, float D)>();
        var next = table.Next;
        for (int s = 0; s < next.Length; s++)
            if (next[s].Alive && table.Cold(s) is { IsPlayer: true })
            {
                var p = new Vector2(next[s].Position.X, next[s].Position.Z);
                yours.Add((p, Vector2.Distance(p, at)));
            }
        var people = new List<CampPerson>();
        foreach (var squad in world.Squads.All)
        {
            if (squad.Town != index) continue;
            foreach (var m in squad.Members)
            {
                if (!table.TryResolveNext(m, out int slot) || Person(world, m) is not { } person) continue;
                ulong id = person.Lasting != 0 ? person.Lasting : world.LastingIds.GetOrGive(m).Value;
                var p = new Vector2(table.Next[slot].Position.X, table.Next[slot].Position.Z);
                float nearest = yours.Count == 0 ? float.MaxValue : yours.Min(y => Vector2.Distance(y.P, p));
                people.Add(new CampPerson(id, !person.Down && !person.Taken, Vector2.Distance(p, at), nearest));
            }
        }
        return (people, yours.Select(y => y.D).ToList());
    });


    #endregion

    #region Items

    // The item layer (his framework, 2026-11-24): every item quest counts, takes, gives and finds items through these, never by reading
    // inventories itself. Items a quest hands out (a parcel, a recover quest's item) carry the quest's tag as their ModTag: saved with the item,
    // copied with it, never stacked with untagged ones, and ignored by the game if the mod is removed (the item stays an ordinary item).

    /// <summary>Your characters (player characters, alive) within <paramref name="reach"/> of <paramref name="at"/>, by slot.</summary>
    List<(int Slot, CharacterId Id)> YoursWithin(World world, Vector2 at, float reach)
    {
        var table = world.Characters;
        var list = new List<(int, CharacterId)>();
        for (int s = 0; s < table.Next.Length; s++)
            if (table.Next[s].Alive && table.Cold(s) is { IsPlayer: true }
                && Vector2.Distance(new Vector2(table.Next[s].Position.X, table.Next[s].Position.Z), at) <= reach)
                list.Add((s, table.IdOf(s)));
        return list;
    }

    /// <summary>Whether <paramref name="item"/> counts for a hand-in: the record and tag, and not stolen from <paramref name="refuseStolenFrom"/>.</summary>
    static bool Counts(Meitou.Simulation.Items.ItemInstance item, string record, string tag, int refuseStolenFrom) =>
        item.Record == record && item.ModTag == tag && (refuseStolenFrom < 0 || item.StolenFrom != refuseStolenFrom);

    /// <summary>
    /// How many units of <paramref name="record"/> (with tag <paramref name="tag"/>, "" for ordinary ones) your characters within
    /// <paramref name="reach"/> of <paramref name="at"/> carry, bags included; units stolen from <paramref name="refuseStolenFrom"/> (a faction
    /// index; -1 none) don't count (that faction knows them as its own; goods stolen from anyone else are fine, his rule 2026-11-24).
    /// </summary>
    public int Carried(World world, Vector2 at, float reach, string record, string tag = "", int refuseStolenFrom = -1) => Guard(Area.Items, 0, () =>
    {
        int n = 0;
        foreach (var (s, _) in YoursWithin(world, at, reach))
            if (world.Characters.Cold(s)?.Inventory is { } inv)
                foreach (var item in inv.All())
                    if (Counts(item, record, tag, refuseStolenFrom)) n += Math.Max(1, item.Quantity);
        return n;
    });

    /// <summary>
    /// How many units of <paramref name="record"/> (with <paramref name="tag"/>) all your characters carry, wherever they are (a quest's
    /// progress); the same stolen rule.
    /// </summary>
    public int CarriedAnywhere(World world, string record, string tag = "", int refuseStolenFrom = -1) =>
        Carried(world, Vector2.Zero, float.MaxValue, record, tag, refuseStolenFrom);

    /// <summary>
    /// Takes <paramref name="count"/> units of <paramref name="record"/> (with <paramref name="tag"/>) from your characters within
    /// <paramref name="reach"/> of <paramref name="at"/> (a hand-in), never more; nothing at all unless all of them are there. True when taken.
    /// </summary>
    public bool TakeCarried(World world, Vector2 at, float reach, string record, int count, string tag = "", int refuseStolenFrom = -1) =>
        Guard(Area.Items, false, () =>
    {
        if (Carried(world, at, reach, record, tag, refuseStolenFrom) < count) return false;
        int left = count;
        foreach (var (s, _) in YoursWithin(world, at, reach))
        {
            if (world.Characters.Cold(s)?.Inventory is not { } inv) continue;
            foreach (var item in inv.All().Where(i => Counts(i, record, tag, refuseStolenFrom)).ToList())
                while (left > 0 && inv.RemoveOne(item)) left--;
            if (left == 0) break;
        }
        return left == 0;
    });

    /// <summary>
    /// Gives <paramref name="quantity"/> of <paramref name="record"/> to <paramref name="to"/>, tagged <paramref name="tag"/>: into the inventory,
    /// and what does not fit on the ground at the character's feet (never lost). False when the record or the character is not there.
    /// </summary>
    public bool Give(World world, CharacterId to, string record, int quantity, string tag = "") => Guard(Area.Items, false, () =>
    {
        var table = world.Characters;
        if (!table.TryResolveNext(to, out int s) || table.Cold(s)?.Inventory is not { } inv) return false;
        if (data.Items.Create(record, Math.Max(1, quantity)) is not { } item) return false;
        item.ModTag = tag;
        if (!inv.Add(item)) world.GroundItems.Drop(item, table.Next[s].Position, 0);
        return true;
    });

    /// <summary>
    /// Lays one <paramref name="record"/> tagged <paramref name="tag"/> on the ground at or near <paramref name="at"/> (the first walkable spot on
    /// small rings out from it: a camp's middle may be a building). False when the record is not there.
    /// </summary>
    public bool PlaceItem(World world, string record, Vector2 at, string tag) => Guard(Area.Items, false, () =>
    {
        if (data.Items.Create(record, 1) is not { } item) return false;
        item.ModTag = tag;
        var w = world.Walkability;
        var spot = at;
        for (float r = 0; r <= 400; r += 50)
        {
            bool found = false;
            for (int i = 0; i < 8 && !found; i++)
            {
                var p = at + new Vector2(MathF.Cos(i * MathF.PI / 4), MathF.Sin(i * MathF.PI / 4)) * r;
                if (w.IsWalkable(p.X, p.Y)) { spot = p; found = true; }
            }
            if (found) break;
        }
        world.GroundItems.Drop(item, new Vector3(spot.X, w.TerrainHeight(spot.X, spot.Y) + 2, spot.Y), 0);
        return true;
    });

    /// <summary>One of your characters within <paramref name="reach"/> of <paramref name="at"/> (the nearest), or none.</summary>
    public CharacterId NearestYours(World world, Vector2 at, float reach) => Guard(Area.Items, CharacterId.None, () =>
    {
        var table = world.Characters;
        return YoursWithin(world, at, reach)
            .OrderBy(y => Vector2.Distance(new Vector2(table.Next[y.Slot].Position.X, table.Next[y.Slot].Position.Z), at))
            .Select(y => y.Id).DefaultIfEmpty(CharacterId.None).First();
    });

    /// <summary>Who in group <paramref name="platoonId"/> carries a quest's item: its leader if standing, else its first standing member.</summary>
    public CharacterId GroupHolder(World world, int platoonId) => Guard(Area.Items, CharacterId.None, () =>
    {
        if (world.Platoons.Find(platoonId) is not { } p || p.SquadId < 0 || world.Squads.Find(p.SquadId) is not { } squad) return CharacterId.None;
        if (Person(world, squad.Leader) is { Down: false, Taken: false }) return squad.Leader;
        return squad.Members.FirstOrDefault(m => Person(world, m) is { Down: false, Taken: false });
    });

    /// <summary>Where an item tagged for a quest is now: who holds it (none on the ground), where, whether that is one of yours and alive.</summary>
    public readonly record struct TaggedItem(CharacterId Holder, Vector2 At, bool Yours, bool HolderAlive, bool OnGround);

    /// <summary>
    /// Finds the item tagged <paramref name="tag"/>: on your characters first, then on anyone in the world, then on the ground. Null when it is
    /// nowhere (destroyed, or on someone not in the world now).
    /// </summary>
    public TaggedItem? FindTagged(World world, string tag) => Guard<TaggedItem?>(Area.Items, null, () =>
    {
        var table = world.Characters;
        TaggedItem? other = null;
        for (int s = 0; s < table.Next.Length; s++)
        {
            if (table.Cold(s) is not { Inventory: { } inv } cold || !inv.All().Any(i => i.ModTag == tag)) continue;
            var at = new Vector2(table.Next[s].Position.X, table.Next[s].Position.Z);
            bool yours = cold.IsPlayer || (data.PlayerFaction >= 0 && cold.Faction == data.PlayerFaction);
            var found = new TaggedItem(table.IdOf(s), at, yours, table.Next[s].Alive, false);
            if (yours) return found;
            other ??= found;
        }
        if (other is not null) return other;
        foreach (var g in world.GroundItems.All)
            if (g.Item.ModTag == tag) return new TaggedItem(CharacterId.None, new Vector2(g.Position.X, g.Position.Z), false, false, true);
        return null;
    });

    /// <summary>Removes every item tagged <paramref name="tag"/> from everyone in the world and the ground (a quest's items when it ends).</summary>
    public int RemoveTagged(World world, string tag) => Guard(Area.Items, 0, () =>
    {
        var table = world.Characters;
        int n = 0;
        for (int s = 0; s < table.Next.Length; s++)
            if (table.Cold(s)?.Inventory is { } inv)
                foreach (var item in inv.All().Where(i => i.ModTag == tag).ToList())
                    if (inv.Remove(item)) n++;
        foreach (var g in world.GroundItems.All.Where(g => g.Item.ModTag == tag).ToList())
            if (world.GroundItems.Take(g.Id) is not null) n++;
        return n;
    });

    // Targets judged by worth (his rule, 2026-11-24: "a katana worth at least X", judged by the game's own price for the item: its grade and,
    // for a weapon, its maker). The same price the game shows in an item's tooltip (Market.BaseValue: ItemValue at the item's gear level).

    PriceConstants? _prices;

    /// <summary>What the game says an item is worth (one of it): its record at its gear level, a weapon's maker included. 0 if unknown.</summary>
    public int WorthOf(Meitou.Simulation.Items.ItemInstance item) => Guard(Area.Items, 0, () =>
    {
        if (data.Db.Find(item.Record) is not { } record) return 0;
        _prices ??= PriceConstants.FromDatabase(data.Db);
        var maker = item.CompanyId.Length > 0 && data.Db.Find(item.CompanyId) is { Type: FcsRecordType.WeaponManufacturer } m ? m : null;
        return ItemValue.Of(data.Db, record, item.GearLevel, true, _prices, maker);
    });

    /// <summary>
    /// What counts for a hand-in: one of <paramref name="Records"/>, worth at least <paramref name="MinWorth"/> (0: any), tagged
    /// <paramref name="Tag"/> ("" ordinary), and not stolen from <paramref name="RefuseStolenFrom"/> (a faction index, -1 none).
    /// </summary>
    public sealed record ItemNeed(IReadOnlySet<string> Records, int MinWorth = 0, string Tag = "", int RefuseStolenFrom = -1);

    bool Fits(Meitou.Simulation.Items.ItemInstance item, ItemNeed need) =>
        need.Records.Contains(item.Record) && item.ModTag == need.Tag && (need.RefuseStolenFrom < 0 || item.StolenFrom != need.RefuseStolenFrom)
        && (need.MinWorth <= 0 || WorthOf(item) >= need.MinWorth);

    /// <summary>How many units that fit <paramref name="need"/> your characters within <paramref name="reach"/> of <paramref name="at"/> carry.</summary>
    public int Carried(World world, Vector2 at, float reach, ItemNeed need) => Guard(Area.Items, 0, () =>
    {
        int n = 0;
        foreach (var (s, _) in YoursWithin(world, at, reach))
            if (world.Characters.Cold(s)?.Inventory is { } inv)
                foreach (var item in inv.All())
                    if (Fits(item, need)) n += Math.Max(1, item.Quantity);
        return n;
    });

    /// <summary>The same, wherever your characters are.</summary>
    public int CarriedAnywhere(World world, ItemNeed need) => Carried(world, Vector2.Zero, float.MaxValue, need);

    /// <summary>
    /// The worth of the best item of <paramref name="records"/> your characters carry (worth ignored in the match), 0 for none: the journal's
    /// "your best: X" for a worth target.
    /// </summary>
    public int BestCarried(World world, ItemNeed need) => Guard(Area.Items, 0, () =>
    {
        int best = 0;
        var any = need with { MinWorth = 0 };
        foreach (var (s, _) in YoursWithin(world, Vector2.Zero, float.MaxValue))
            if (world.Characters.Cold(s)?.Inventory is { } inv)
                foreach (var item in inv.All())
                    if (Fits(item, any)) best = Math.Max(best, WorthOf(item));
        return best;
    });

    /// <summary>
    /// Takes <paramref name="count"/> units that fit <paramref name="need"/> from your characters within <paramref name="reach"/> of
    /// <paramref name="at"/>, the least worth first (so a better one you carry is kept), all or nothing. Returns the worth of each unit taken
    /// (empty when nothing was taken).
    /// </summary>
    public List<int> TakeCarried(World world, Vector2 at, float reach, ItemNeed need, int count) => Guard(Area.Items, new List<int>(), () =>
    {
        var worths = new List<int>();
        var found = new List<(Meitou.Simulation.Items.Inventory Inv, Meitou.Simulation.Items.ItemInstance Item, int Worth)>();
        foreach (var (s, _) in YoursWithin(world, at, reach))
            if (world.Characters.Cold(s)?.Inventory is { } inv)
                foreach (var item in inv.All().Where(i => Fits(i, need) && !Worn(inv, i)).ToList())
                    found.Add((inv, item, WorthOf(item)));
        if (found.Sum(f => Math.Max(1, f.Item.Quantity)) < count) return worths;
        foreach (var (inv, item, worth) in found.OrderBy(f => f.Worth))
            while (worths.Count < count && inv.RemoveOne(item)) worths.Add(worth);
        return worths;
    });

    /// <summary>
    /// Items one of yours carries that a hand-in could take, alike ones together (the same item, holder, worth and marks: one line of the pick
    /// window): who has them, the items, their worth (one), units in all, worn, stolen mark.
    /// </summary>
    public sealed record Offered(CharacterId Who, IReadOnlyList<Meitou.Simulation.Items.ItemInstance> Items, string Name, string Holder, int Worth,
        int Units, bool Worn, int StolenFrom);

    /// <summary>Every item of <paramref name="records"/> (untagged) your characters within <paramref name="reach"/> of <paramref name="at"/> carry, bags included, alike ones together.</summary>
    public List<Offered> Offer(World world, Vector2 at, float reach, IReadOnlySet<string> records) => Guard(Area.Items, new List<Offered>(), () =>
    {
        var list = new List<Offered>();
        foreach (var (s, id) in YoursWithin(world, at, reach))
            if (world.Characters.Cold(s) is { Inventory: { } inv } cold)
                foreach (var g in inv.All().Where(i => records.Contains(i.Record) && i.ModTag.Length == 0)
                    .GroupBy(i => (i.Record, Worth: WorthOf(i), Worn: Worn(inv, i), i.StolenFrom)))
                    list.Add(new Offered(id, [.. g], data.Db.Find(g.Key.Record)?.Name?.Trim() ?? g.Key.Record, cold.Name, g.Key.Worth,
                        g.Sum(i => Math.Max(1, i.Quantity)), g.Key.Worn, g.Key.StolenFrom));
        return list;
    });


    /// <summary>Whether <paramref name="item"/> is worn (in one of <paramref name="inv"/>'s equipment slots, the backpack itself aside).</summary>
    static bool Worn(Meitou.Simulation.Items.Inventory inv, Meitou.Simulation.Items.ItemInstance item) =>
        inv.Items.Contains(item) && item.Section != "backpack_attach" && Meitou.Simulation.Items.InventoryCommands.IsEquipmentSection(item.Section);

    /// <summary>
    /// Takes <paramref name="count"/> units from the picked items, in the order picked (never a worn one), all or nothing. Returns the worth
    /// of each unit taken (empty when not all could be).
    /// </summary>
    public List<int> TakePicked(World world, IReadOnlyList<Offered> picked, int count) => Guard(Area.Items, new List<int>(), () =>
    {
        var worths = new List<int>();
        var live = new List<(Meitou.Simulation.Items.Inventory Inv, Meitou.Simulation.Items.ItemInstance Item, int Worth)>();
        foreach (var o in picked)
            if (world.Characters.TryResolveNext(o.Who, out int s) && world.Characters.Cold(s)?.Inventory is { } inv)
            {
                var all = inv.All().ToHashSet();
                foreach (var item in o.Items)
                    if (all.Contains(item) && !Worn(inv, item)) live.Add((inv, item, o.Worth));
            }
        if (live.Sum(l => Math.Max(1, l.Item.Quantity)) < count) return worths;
        foreach (var (inv, item, worth) in live)
            while (worths.Count < count && inv.RemoveOne(item)) worths.Add(worth);
        return worths;
    });

    #endregion

    #region Regions, relations, money

    /// <summary>The region (BIOME_GROUP id) at a point, or null.</summary>
    public string? Region(Vector2 at) => Guard(Area.Regions, null, () => data.BiomeGroupAt(at));

    public bool Hostile(int a, int b) => Guard(Area.Relations, false, () => a >= 0 && b >= 0 && data.Relations.IsHostile(a, b));

    public float Relation(int a, int b) => Guard(Area.Relations, 0f, () => a >= 0 && b >= 0 ? data.Relations.Get(a, b) : 0f);

    /// <summary>Changes <paramref name="faction"/>'s relation with you through the game's own path (its threshold messages included).</summary>
    public void ChangeRelation(World world, int faction, int delta, string why) => Guard(Area.Relations, 0, () =>
    {
        if (delta == 0 || faction < 0 || data.PlayerFaction < 0) return 0;
        Meitou.Simulation.Factions.PlayerRelations.Add(world, data.Relations, faction, data.PlayerFaction, delta,
            new Meitou.Simulation.Factions.RelationCause(Meitou.Simulation.Factions.RelationCauseType.Dialogue, CharacterId.None), why);
        return 0;
    });

    public void Pay(World world, long cats) => world.Player.Money += cats;

    #endregion

    #region Ambush orders

    // A courier's ambush (his rules, 2026-11-24): the group is held in the mod's name (saved with its goal; released by Meitou if the mod is
    // gone), its stand-in follows the courier off screen, and once loaded its members are ordered to run to the courier and attack, or to
    // search the last place they knew of a sneaking courier they cannot see. Serial phases only (the guild's Schedule).

    const string ModId = "radiantquests";

    Meitou.Simulation.Aggro.AggroSystem? _aggro;

    /// <summary>Takes or gives back the mod's hold on a group. True when done.</summary>
    public bool Hold(World world, int platoonId, bool held) => Guard(Area.Orders, false, () =>
        PopulationSystem.HoldPlatoon(world, platoonId, ModId, held));

    /// <summary>Sends a held group's stand-in after <paramref name="c"/> (off screen; Meitou follows the character's last position).</summary>
    public bool SendAfter(World world, int platoonId, CharacterId c) => Guard(Area.Orders, false, () =>
        PopulationSystem.SendPlatoon(world, platoonId, ModId, c));

    /// <summary>Sends a held group's stand-in to a point (off screen).</summary>
    public bool SendTo(World world, int platoonId, Vector2 point) => Guard(Area.Orders, false, () =>
        PopulationSystem.SendPlatoon(world, platoonId, ModId, point));

    /// <summary>The standing members of a group (not down, not carried), none when it is not loaded.</summary>
    public List<CharacterId> Standing(World world, int platoonId) => Guard(Area.Orders, new List<CharacterId>(), () =>
    {
        var list = new List<CharacterId>();
        if (world.Platoons.Find(platoonId) is not { SquadId: >= 0 } p || world.Squads.Find(p.SquadId) is not { } squad) return list;
        foreach (var m in squad.Members)
            if (world.Characters.TryResolveNext(m, out int s) && world.Characters.Next[s].Alive && world.Characters.Cold(s) is { } cold
                && cold.Medical is not { Incapacitated: true } && cold.CarriedBy == CharacterId.None)
                list.Add(m);
        return list;
    });

    /// <summary>Whether <paramref name="c"/> is fighting now.</summary>
    public bool Fighting(World world, CharacterId c) =>
        world.Characters.TryResolveNext(c, out int s) && world.Characters.Cold(s) is { InCombat: true };

    /// <summary>Where <paramref name="c"/> stands (ground X, Z), or null.</summary>
    public Vector2? At(World world, CharacterId c) =>
        world.Characters.TryResolveNext(c, out int s) ? new Vector2(world.Characters.Next[s].Position.X, world.Characters.Next[s].Position.Z) : null;

    /// <summary>
    /// Whether <paramref name="c"/> is sneaking and no one has spotted them (the game's own stealth: no looker's spot meter on them), so an
    /// ambush has lost sight of them.
    /// </summary>
    public bool Hidden(World world, CharacterId c) => Guard(Area.Orders, false, () =>
    {
        if (!world.Characters.TryResolveNext(c, out int s) || world.Characters.Cold(s) is not { } cold) return false;
        if ((cold.Orders & Meitou.Simulation.Player.CharacterOrders.Sneak) == 0) return false;
        _aggro ??= world.Systems.OfType<Meitou.Simulation.Aggro.AggroSystem>().FirstOrDefault();
        return _aggro?.Unseen(world.Characters, c) ?? false;
    });

    /// <summary>Whether <paramref name="c"/> is down (knocked out, dying, dead) or being carried off.</summary>
    public bool DownOrCarried(World world, CharacterId c) =>
        world.Characters.TryResolveNext(c, out int s) && world.Characters.Cold(s) is { } cold
        && (cold.Medical is { Incapacitated: true } || !world.Characters.Next[s].Alive || cold.CarriedBy != CharacterId.None);

    /// <summary>Orders <paramref name="who"/> to attack <paramref name="target"/> (the game's own attack order); returns Meitou's answer.</summary>
    public string OrderAttack(World world, CharacterId who, CharacterId target) => Guard(Area.Orders, "off", () =>
        world.GiveOrder(who, Meitou.Data.Gameplay.Ai.TaskType.FocusedMeleeAttack, target).ToString());

    /// <summary>Orders <paramref name="who"/> to walk to a point (closing in, or a search); returns Meitou's answer.</summary>
    public string OrderMove(World world, CharacterId who, Vector2 point) => Guard(Area.Orders, "off", () =>
    {
        float y = world.Characters.TryResolveNext(who, out int s) ? world.Characters.Next[s].Position.Y : 0;
        return world.GiveOrder(who, Meitou.Data.Gameplay.Ai.TaskType.MoveCusOrdered, point: new Vector3(point.X, y, point.Y)).ToString();
    });

    /// <summary>Orders <paramref name="who"/> to loot <paramref name="target"/> (the game's own loot: the animation).</summary>
    public void OrderLoot(World world, CharacterId who, CharacterId target) => Guard(Area.Orders, 0, () =>
    {
        world.GiveOrder(who, Meitou.Data.Gameplay.Ai.TaskType.LootTarget, target);
        return 0;
    });

    /// <summary>Moves every item tagged <paramref name="tag"/> into <paramref name="to"/>'s inventory (the ground if it does not fit).</summary>
    public bool MoveTagged(World world, string tag, CharacterId to) => Guard(Area.Items, false, () =>
    {
        var table = world.Characters;
        if (!table.TryResolveNext(to, out int dest) || table.Cold(dest)?.Inventory is not { } into) return false;
        var moved = new List<Meitou.Simulation.Items.ItemInstance>();
        for (int s = 0; s < table.Next.Length; s++)
            if (s != dest && table.Cold(s)?.Inventory is { } inv)
                foreach (var item in inv.All().Where(i => i.ModTag == tag).ToList())
                    if (inv.Remove(item)) moved.Add(item);
        foreach (var item in moved)
            if (!into.Add(item)) world.GroundItems.Drop(item, table.Next[dest].Position, 0);
        return moved.Count > 0;
    });

    #endregion
}
