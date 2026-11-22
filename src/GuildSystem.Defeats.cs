// Radiant Quests for Meitou. GPLv3 or later.
// Defeats and group quests.
//
// In Kenshi most fights end with people knocked out, not dead, so quests count *defeats*: someone your side hit (in the last two minutes) who is
// knocked out, dead, carried off by one of yours or locked up. Each person counts once per quest (by their lasting id), whatever happens to them
// after. A "wipe out this group" quest follows one real group (its platoon, tagged when the quest is taken) and is done when the set share of it
// (0.75 by default) is defeated, so a member who can't be found never blocks it. If others wipe the group out before your side lands a blow, the
// quest is called off with no penalty; once you have fought it, the group's end finishes the quest.
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Factions;

namespace RadiantQuests;

public sealed partial class GuildSystem : ICombatHitObserver
{
    /// <summary>How long after your side's last blow a knockout or death still counts as your defeat (ticks; two minutes at 30 a second).</summary>
    const long DefeatWindow = 3600;

    /// <summary>The characters your side hit, with the tick of the last blow (hashed: it decides what counts).</summary>
    readonly Dictionary<CharacterId, long> _hits = [];

    long _lastDefeats;

    /// <summary>The defeat and group feature failed once this session (a Meitou change): off until the game restarts.</summary>
    static bool s_defeatsBroken;

    /// <summary>A test kill's made-up person (never a real lasting id).</summary>
    ulong _testPerson = ulong.MaxValue;

    #region Blows and defeats

    public void HitResolved(World world, in CombatHit hit)
    {
        if (s_defeatsBroken || _s.Quests.Count == 0 || !IsYours(world, hit.Attacker)) return;
        try
        {
            _hits[hit.Victim] = world.Tick;
            // A lasting id now, while the victim is in the world, so the defeat can be counted once even after a load.
            world.LastingIds.GetOrGive(hit.Victim);
            if (TagOf(world, hit.Victim) is { } tag)
                foreach (var q in _s.Quests)
                    if (q.Group && !q.Hit && q.Tag == tag)
                    {
                        q.Hit = true;
                        _dirty = true;
                    }
        }
        catch (Exception e)
        {
            DefeatsOff(e);
        }
    }

    bool IsYours(World world, CharacterId c) =>
        world.Characters.TryResolveNext(c, out int s) && world.Characters.Cold(s) is { } cold
        && (cold.IsPlayer || (_data.PlayerFaction >= 0 && cold.Faction == _data.PlayerFaction));

    /// <summary>The quest tag of the group <paramref name="c"/> belongs to, if any.</summary>
    string? TagOf(World world, CharacterId c)
    {
        if (!world.Characters.TryResolveNext(c, out int s) || world.Characters.Cold(s) is not { SquadId: >= 0 } cold) return null;
        if (world.Squads.Find(cold.SquadId) is not { PlatoonId: >= 0 } squad || world.Platoons.Find(squad.PlatoonId) is not { } platoon) return null;
        return platoon.ModTag.StartsWith("radiantquests:", StringComparison.Ordinal) ? platoon.ModTag : null;
    }

    /// <summary>Every second: those your side hit lately who are now down, dead, carried off by yours or locked up are defeated.</summary>
    void CheckDefeats(World world)
    {
        _lastDefeats = world.Tick;
        if (_hits.Count == 0) return;
        var table = world.Characters;
        foreach (var (c, tick) in _hits.OrderBy(kv => kv.Key.Slot).ThenBy(kv => kv.Key.Generation).ToList())
        {
            if (world.Tick - tick > DefeatWindow || !table.TryResolveNext(c, out int s) || table.Cold(s) is not { } cold)
            {
                _hits.Remove(c);
                continue;
            }
            bool down = cold.Medical is { Incapacitated: true } || !table.Next[s].Alive;
            bool taken = cold.IsImprisoned || (cold.CarriedBy != CharacterId.None && IsYours(world, cold.CarriedBy));
            if (down || taken) Defeat(world, c, cold);
        }
    }

    /// <summary><paramref name="c"/> is defeated by your side: counts for every quest it matches, once per quest.</summary>
    void Defeat(World world, CharacterId c, CharacterCold cold)
    {
        ulong person = cold.LastingId.Value;
        if (person == 0) return;
        string faction = _data.FactionId(cold.Faction) ?? "";
        string race = Cat.RaceOf.GetValueOrDefault(cold.RecordId, "");
        // Someone's animal (a guild faction's pack beast or a town's herd) is no hunt kill, whatever its race.
        if (Array.BinarySearch(_guildFactions, cold.Faction) >= 0) race = "";
        Count(world, faction, race, person, TagOf(world, c));
    }

    /// <summary>
    /// A defeat of <paramref name="person"/> (of <paramref name="faction"/> and <paramref name="race"/>, in the group tagged <paramref name="tag"/>):
    /// counted for each quest it matches that has not counted them yet.
    /// </summary>
    void Count(World world, string faction, string race, ulong person, string? tag)
    {
        foreach (var q in _s.Quests)
        {
            if (q.Done || q.Defeated.Contains(person)) continue;
            bool match = q.Group
                ? tag == q.Tag
                : q.Kind switch
                {
                    Catalog.Cull => q.TargetId == Cat.Head(faction),
                    Catalog.Hunt => q.TargetId == race,
                    _ => false,
                };
            if (!match) continue;
            q.Defeated.Add(person);
            q.Have++;
            if (q.Group) q.Hit = true;
            q.Deadline = Math.Min(q.Deadline + _set.KillRefreshHours, world.GameHours + _set.KillHours);
            _dirty = true;
            RadiantQuestsMod.Toast(q.Done ? $"{q.Describe()}: done! Hand it in at a guild clerk." : $"{q.Describe()}: {q.Have}/{q.Need}");
        }
    }

    /// <summary>A test kill: counts as a defeat of a made-up person of <paramref name="target"/> (a faction or race id).</summary>
    void TestCount(World world, string target) => Count(world, target, target, _testPerson--, null);

    void DefeatsOff(Exception e)
    {
        if (s_defeatsBroken) return;
        s_defeatsBroken = true;
        RadiantQuestsMod.Log($"defeat counting turned OFF (this Meitou changed what it uses): {e.GetType().Name}: {e.Message}");
    }

    void HashDefeats(ref StateHasher h)
    {
        h.Add(_hits.Count);
        foreach (var (c, tick) in _hits.OrderBy(kv => kv.Key.Slot).ThenBy(kv => kv.Key.Generation))
        {
            h.Add(c);
            h.Add(tick);
        }
    }

    #endregion

    #region Group quests

    /// <summary>
    /// Up to three "wipe out this group" offers from the real groups near the town: target factions as <paramref name="allowed"/> says, ranked by
    /// their real strength (size times the faction's fighter strength) against the load order's typical squads. A group already on another
    /// quest, or one another mod spawned or tagged, is left alone.
    /// </summary>
    void AddGroupOffers(World world, List<Offer> offers, ref Roll rng, Vector2 at, int myRank, Func<Target, bool> allowed, IReadOnlyList<Target> targets)
    {
        if (!_set.GroupQuests || s_defeatsBroken) return;
        var byId = targets.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var taken = new HashSet<ulong>(offers.Where(o => o.Group).Select(o => o.PlatoonKey));
        var candidates = new List<(Meitou.Simulation.Factions.Platoon P, Target T, int Rank)>();
        foreach (var p in world.Platoons.All)
        {
            if (p.Dead || p.ModSpawned || p.ModTag.Length > 0 || p.Faction < 0 || p.Size < 3 || taken.Contains(p.Key)) continue;
            if (_data.FactionId(p.Faction) is not { } f || !byId.TryGetValue(Cat.Head(f), out var t) || !allowed(t)) continue;
            if (Vector2.Distance(PlatoonPosition(world, p), at) > _set.TargetRange) continue;
            int rank = Cat.GroupRank(Cat.UnitOf.GetValueOrDefault(t.Id, t.Unit), p.Size);
            // Your rank and one above; higher ones rarely, as on the other boards.
            if (rank > myRank + 1 && !rng.Chance(0.15)) continue;
            candidates.Add((p, t, rank));
        }
        // Three at random from the nearest dozen (a seeded shuffle, so every machine picks the same).
        var pool = candidates.OrderBy(c => Vector2.Distance(PlatoonPosition(world, c.P), at)).ThenBy(c => c.P.Id).Take(12).ToList();
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Int(0, i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        foreach (var (p, t, rank) in pool.Take(3))
        {
            var (cMin, cMax) = Ranks.Cats(rank);
            offers.Add(new Offer
            {
                Slot = offers.Count,
                Kind = Catalog.Cull,
                TargetId = t.Id,
                TargetName = t.Name,
                Rank = rank,
                Group = true,
                GroupSize = p.Size,
                PlatoonKey = p.Key,
                Need = Math.Max(1, (int)Math.Ceiling(p.Size * _set.GroupShare)),
                Cats = (long)Math.Round((cMin + rng.Double() * (cMax - cMin)) / 10) * 10,
                Rep = (int)Math.Round(Ranks.Rep(rank) * (0.85 + 0.3 * rng.Double())),
            });
        }
    }

    /// <summary>Takes a group offer: tags its platoon for the quest. False when the group is gone since the board was made.</summary>
    bool TagGroup(World world, Offer offer, Quest q)
    {
        var p = world.Platoons.All.FirstOrDefault(x => x.Key == offer.PlatoonKey && !x.Dead && x.ModTag.Length == 0);
        if (p is null) return false;
        p.ModTag = q.Tag;
        return true;
    }

    /// <summary>
    /// A group quest's group is gone (dead, despawned): finished if your side fought it, else called off with no penalty. True when the quest
    /// ended.
    /// </summary>
    bool GroupGone(World world, Quest q)
    {
        if (q.Done) return false;
        _dirty = true;
        if (q.Hit)
        {
            q.Have = q.Need;
            RadiantQuestsMod.Toast($"{q.Describe()}: the group is broken. Hand it in at a guild clerk.");
            return false;
        }
        _s.Quests.Remove(q);
        RadiantQuestsMod.Toast($"The group for \"{q.Describe()}\" is gone before you fought it: the quest is called off, no penalty.");
        return true;
    }

    #endregion
}
