// Radiant Quests for Meitou. GPLv3 or later.
// Defeats. Most Kenshi fights end with people knocked out, not dead, so quests count DEFEATS: someone your side hit while they were STANDING
// (a blow on someone already down doesn't count, so finishing off the downed, or butchering them, never does) in the last two minutes, and
// who is now knocked out, dead, carried off by one of yours or locked up. Each person counts once per quest, and only for quests taken
// before that blow, so a group left knocked out by an earlier quest can't be farmed for the next one.
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace RadiantQuests;

public sealed partial class GuildSystem : ICombatHitObserver
{
    /// <summary>How long after your side's last blow a knockout or death still counts as your defeat (ticks; two minutes at 30 a second).</summary>
    const long DefeatWindow = 3600;

    /// <summary>The characters your side hit while they were standing, with the tick of the last such blow (hashed: it decides what counts).</summary>
    readonly Dictionary<CharacterId, long> _hits = [];

    long _lastDefeats;

    /// <summary>A test kill's made-up person (never a real lasting id).</summary>
    ulong _testPerson = ulong.MaxValue;

    public void HitResolved(World world, in CombatHit hit)
    {
        if (_s.Quests.Count == 0 || WorldAccess.IsOff(WorldAccess.Area.People)) return;
        // Only a blow on someone standing: hitting the downed never earns a defeat.
        if (!Access.IsYours(world, hit.Attacker)) return;
        if (!hit.VictimWasAlive || hit.VictimWasIncapacitated)
        {
            RadiantQuestsMod.Detail($"blow on {hit.Victim} not counted: {(hit.VictimWasAlive ? "already down" : "dead")}");
            return;
        }
        _hits[hit.Victim] = world.Tick;
        Access.Remember(world, hit.Victim);
        if (Access.Person(world, hit.Victim) is { } victim)
            foreach (var q in _s.Quests)
                if (!q.Hit && ((q.Kind == GroupQuest.KindId && q.Tag == victim.GroupTag) || (q.Kind == LairQuest.KindId && q.Lair == victim.HomeSite)))
                {
                    q.Hit = true;
                    _dirty = true;
                }
    }

    /// <summary>Every second: those your side hit lately (standing) who are now down, dead, carried off by yours or locked up are defeated.</summary>
    void CheckDefeats(World world)
    {
        _lastDefeats = world.Tick;
        if (_hits.Count == 0) return;
        foreach (var (c, tick) in _hits.OrderBy(kv => kv.Key.Slot).ThenBy(kv => kv.Key.Generation).ToList())
        {
            if (world.Tick - tick > DefeatWindow || Access.Person(world, c) is not { } p)
            {
                _hits.Remove(c);
                continue;
            }
            if (p.Down || p.Taken) Defeat(world, p, tick);
        }
    }

    public void Died(World world, in CharacterDeath death)
    {
        if (death.Faction == _data.PlayerFaction) return;
        if (_s.Quests.Count == 0) return;
        // A death counts only for someone your side hit while standing (a kill of the downed, or butchering, does not).
        if (!_hits.TryGetValue(death.Victim, out long tick) || world.Tick - tick > DefeatWindow)
        {
            if (Access.Person(world, death.Victim) is { } other) LostToOthers(world, other);
            return;
        }
        if (Access.Person(world, death.Victim) is { } p) Defeat(world, p, tick);
    }

    /// <summary><paramref name="p"/> is defeated by your side (last standing blow at <paramref name="hitTick"/>): counts once per quest it matches.</summary>
    void Defeat(World world, PersonFacts p, long hitTick)
    {
        if (p.Lasting == 0) return;
        if (p.GroupKey != 0) _s.SpawnDefeats[p.GroupKey] = world.GameHours;
        string faction = _data.FactionId(p.Faction) ?? "";
        string race = Cat.RaceOf.GetValueOrDefault(p.Record, "");
        // Someone's animal (a guild faction's pack beast or a town's herd) is no hunt kill, whatever its race.
        if (Array.BinarySearch(_guildFactions, p.Faction) >= 0) race = "";
        Count(world, p, race, Cat.Head(faction), p.Lasting, hitTick);
    }

    /// <summary>
    /// Someone of a running quest's targets went down or died with no blow of yours: marked lost to others on each quest they would count for
    /// (only ones near the quest's place or in its own groups, and not already counted for it). Evidence only: what is not seen is a bug.
    /// </summary>
    public void LostToOthers(World world, PersonFacts p)
    {
        if (_hits.TryGetValue(p.Id, out long tick) && world.Tick - tick <= DefeatWindow) return;   // yours: counted as a defeat
        string faction = _data.FactionId(p.Faction) ?? "";
        string race = Array.BinarySearch(_guildFactions, p.Faction) >= 0 ? "" : Cat.RaceOf.GetValueOrDefault(p.Record, "");
        foreach (var q in _s.Quests)
        {
            if (q.Done || q.Kind is not (Catalog.Hunt or Catalog.Cull) || QuestTypes.Of(q.Kind) is not { } type) continue;
            if (!type.Counts(q, p, race, Cat.Head(faction))) continue;
            ulong id = p.Lasting != 0 ? p.Lasting : Access.LastingOf(world, p.Id);
            if (id == 0 || q.Defeated.Contains(id) || q.LostToOthers.Contains(id)) continue;
            if (p.GroupTag != q.Tag && !Access.Within(world, p.Id, new System.Numerics.Vector2(q.AnchorX, q.AnchorZ), _set.TargetRange)) continue;
            q.LostToOthers.Add(id);
            _dirty = true;
            RadiantQuestsMod.Detail($"quest #{q.Id}: {p.Record} beaten by someone else");
        }
    }

    /// <summary>
    /// Whether your side struck one of <paramref name="q"/>'s targets in the last <paramref name="ticks"/> (that fight is going on: its count may
    /// still change). Blows on anyone else (a passing group you fight) don't hold the quest up.
    /// </summary>
    public bool Fighting(World world, Quest q, long ticks = 300)
    {
        if (QuestTypes.Of(q.Kind) is not { } type) return false;
        foreach (var (c, tick) in _hits)
        {
            if (world.Tick - tick > ticks || Access.Person(world, c) is not { } p) continue;
            string race = Array.BinarySearch(_guildFactions, p.Faction) >= 0 ? "" : Cat.RaceOf.GetValueOrDefault(p.Record, "");
            if (type.Counts(q, p, race, Cat.Head(_data.FactionId(p.Faction) ?? ""))) return true;
        }
        return false;
    }

    void Count(World world, PersonFacts p, string race, string head, ulong person, long hitTick)
    {
        foreach (var q in _s.Quests)
        {
            if (q.Done || q.Defeated.Contains(person) || hitTick < q.AcceptedTick) continue;
            if (QuestTypes.Of(q.Kind) is not { } type || !type.Counts(q, p, race, head)) continue;
            q.Defeated.Add(person);
            q.Have++;
            if (q.Kind == LairQuest.KindId && !q.Rostered) q.Have = 0;   // counted once the roster is taken
            if (q.Kind is GroupQuest.KindId or LairQuest.KindId) q.Hit = true;
            // A camp is cleared by who is left, not by a count (the data's count is only about right): checked after every defeat.
            if (q.Kind == LairQuest.KindId)
            {
                bool cleared = LairQuest.Check(world, this, q);
                if (cleared) q.Have = q.Need;
                else if (q.Have >= q.Need) q.Have = q.Need - 1;   // the roster decides, not the data's count
            }
            q.Deadline = Math.Min(q.Deadline + _set.KillRefreshHours, world.GameHours + _set.KillHours);
            _dirty = true;
            RadiantQuestsMod.Toast(q.Done ? $"{q.Describe()}: done! Hand it in at a guild clerk." : $"{q.Describe()}: {q.Have}/{q.Need}");
        }
    }

    /// <summary>A test kill: counts as a defeat of a made-up person of <paramref name="target"/> (a faction or race id).</summary>
    void TestCount(World world, string target) =>
        Count(world, new PersonFacts(CharacterId.None, _testPerson, -1, "", true, false, null), target, target, _testPerson--, world.Tick);

    void TestFinishQuest(World world, int id)
    {
        if (_s.Quests.FirstOrDefault(x => x.Id == id) is not { } q) return;
        if (q.Kind is GroupQuest.KindId or LairQuest.KindId)
        {
            q.Hit = true;
            q.Have = q.Need;
            _dirty = true;
            return;
        }
        for (int i = 0; i < 100 && !q.Done; i++) TestCount(world, q.TargetId);
    }

    void HashDefeats(ref StateHasher h)
    {
        h.Add(_siteSeen.Count);
        foreach (var (site, tick) in _siteSeen)
        {
            h.Add(site);
            h.Add(tick);
        }
        h.Add(_hits.Count);
        foreach (var (c, tick) in _hits.OrderBy(kv => kv.Key.Slot).ThenBy(kv => kv.Key.Generation))
        {
            h.Add(c);
            h.Add(tick);
        }
    }
}
