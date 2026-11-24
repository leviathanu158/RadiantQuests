// Radiant Quests for Meitou. GPLv3 or later.
// The guild engine: everything shared by all quest types. It finds the clerks, keeps the boards (GuildSystem.Boards.cs), takes, tracks, times
// out and pays quests, keeps rep, ranks and faction points, saves and publishes the view. What a quest IS (what to offer, what counts, where
// its targets are) lives in the quest types (Quests\*.cs); every read or change of Meitou's world goes through WorldAccess.
// Everything here runs in the world's serial steps; the interface only reads the published GuildView and queues commands.
using System.Numerics;
using System.Text.Json;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Population;

namespace RadiantQuests;

/// <summary>A guild clerk the interface can open the window at.</summary>
public sealed record ClerkInfo(CharacterId Id, int Site, string Faction, string FactionName, string TownName, Vector3 Position);

public sealed record QuestView(int Id, string Text, int Rank, int Have, int Need, double HoursLeft, long Cats, int Rep, bool Done, bool General,
    string GiverName, IReadOnlyList<Vector2> Targets, string TargetId = "", string Band = "", string Town = "", int Relation = 0);

public sealed record OfferView(int Slot, string Text, int Rank, long Cats, int Rep, bool Taken, bool Locked, int Relation = 0, string Band = "");

public sealed record BoardView(int Site, long Day, IReadOnlyList<OfferView> Offers, IReadOnlyList<OfferView> FactionOffers, int RerollCost = 0);

/// <summary>Your standing with one faction's guild: its Faction board rank and lifetime points.</summary>
public sealed record FactionStanding(int Rank, long Points, long NextRankAt, float Relation = 0);

/// <summary>What the interface shows, made by the world after a change (never touched by the world again).</summary>
public sealed record GuildView(long Tick, double Hours, long Lifetime, long Balance, int Rank, long NextRankAt, IReadOnlyList<QuestView> Quests,
    IReadOnlyDictionary<int, BoardView> Boards, IReadOnlyDictionary<CharacterId, ClerkInfo> Clerks,
    IReadOnlyDictionary<string, FactionStanding> Factions);

public sealed partial class GuildSystem : ITickSystem, IStateHashed, IModSaveData, ICharacterDeathObserver, IWorldReady
{
    /// <summary>The newest view of the running world (the interface's thread reads it).</summary>
    static GuildView? s_view;

    public static GuildView? View => Volatile.Read(ref s_view);

    /// <summary>How close (world units) one of your characters must stand to the clerk to take or hand in a quest.</summary>
    const float ClerkReach = 300;

    readonly PopulationData _data;
    readonly int[] _guildFactions;
    readonly Dictionary<string, int> _designations = new(StringComparer.Ordinal);
    /// <summary>Where each quest's targets are now (quest id; refreshed every few seconds; read from the world, not saved).</summary>
    readonly Dictionary<int, List<Vector2>> _markers = [];
    GuildState _s = new();
    SettingsCopy _set = SettingsCopy.Defaults;
    Dictionary<CharacterId, ClerkInfo> _clerks = [];
    /// <summary>When each town's clerk was first seen in this stretch (ticks; hashed): its board waits for the place around to load.</summary>
    readonly SortedDictionary<int, long> _siteSeen = [];

    long _lastScan = long.MinValue / 2, _lastPublish = long.MinValue / 2, _lastTrack = long.MinValue / 2;
    bool _dirty = true;

    public GuildSystem(PopulationData data, PopulationSystem? population)
    {
        _data = data;
        Access = new WorldAccess(data, population);
        _guildFactions = [.. RadiantQuestsMod.Catalog.GuildFactions.Select(data.FactionIndex).Where(i => i >= 0).Order()];
    }

    /// <summary>The one layer that talks to Meitou.</summary>
    public WorldAccess Access { get; }

    /// <summary>The settings as this tick sees them.</summary>
    public SettingsCopy Settings => _set;

    static Catalog Cat => RadiantQuestsMod.Catalog;

    int Rank => _set.RankOf(_s.Lifetime);

    /// <summary>Your rank on <paramref name="faction"/>'s Faction board (from your lifetime points with it).</summary>
    int RankWith(string faction) => _set.RankOf(_s.PointsWith(faction));

    public string TownName(int site) => site >= 0 && site < _data.Sites.Count ? _data.Sites[site].Town.Name : "the town";

    #region Tick

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _set = RadiantQuestsMod.Settings.Copy();
        foreach (var c in commands)
        {
            if (c is not GuildCommand) continue;
            RadiantQuestsMod.Detail($"command at tick {world.Tick}: {c}");
            try
            {
                switch (c)
                {
                    case OpenBoard o: Open(world, o.Clerk); break;
                    case AcceptOffer a: Accept(world, a); break;
                    case RerollBoard r: Reroll(world, r.Clerk); break;
                    case HandIn h: HandInQuest(world, h); break;
                    case Abandon a: AbandonQuest(world, a.QuestId); break;
                    case TestKill k: TestCount(world, k.TargetId); break;
                    case TestRep r: GainRep(r.Amount); break;
                    case TestFinish f: TestFinishQuest(world, f.QuestId); break;
                    case TestHerd th: TestHerdReport(world, th.QuestId); break;
                    case TestSetAges ta:
                        if (_s.Quests.FirstOrDefault(x => x.Id == ta.QuestId) is { } aq
                            && Access.Groups(world).FirstOrDefault(g => g.Tag == aq.Tag) is { } ag)
                            RadiantQuestsMod.Log($"test: set {Access.SetAgesEach(world, ag.Id, ta.Ages)} ages in quest #{aq.Id}'s group");
                        break;
                    case TestBreakSpawn b:
                        // As if the quest's brought-in group came out broken (test channel only).
                        if (_s.SpawnChecks.Concat(Access.Groups(world).Where(g => g.ModSpawned && g.Tag == $"radiantquests:{b.QuestId}")
                                .Select(g => KeyValuePair.Create(g.Key, new SpawnCheck { Template = g.TemplateId, Quest = b.QuestId })))
                            .FirstOrDefault(kv => kv.Value.Quest == b.QuestId) is { Value: not null } hit)
                        {
                            _s.SpawnChecks.Remove(hit.Key);
                            SpawnBroke(hit.Key, hit.Value, "test");
                            _dirty = true;
                        }
                        break;
                    case TestSkip k:
                        // As if that many game hours had passed for the mod's own clocks (test channel only): quest deadlines, the
                        // brought-in groups' removal timers, the out-of-reach and straggler timers, the top-up wait.
                        foreach (var q in _s.Quests)
                        {
                            if (q.Deadline < double.MaxValue / 2) q.Deadline -= k.Hours;
                            if (q.NearSince >= 0) q.NearSince -= k.Hours;
                            if (q.LastContact >= 0) q.LastContact -= k.Hours;
                            q.LastRefill -= k.Hours;
                        }
                        foreach (var key in _s.SpawnDefeats.Keys.ToList()) _s.SpawnDefeats[key] -= k.Hours;
                        _dirty = true;
                        break;
                }
            }
            catch (Exception e)
            {
                RadiantQuestsMod.Log($"{c.GetType().Name} failed: {e.Message}");
            }
        }
    }

    public void Schedule(World world)
    {
        if (world.Tick - _lastScan >= 120) Guarded("clerks", () => ScanClerks(world));
        if (world.Tick % 60 == 0) Guarded("time limits", () => Expire(world));
        if (world.Tick - _lastDefeats >= 30) Guarded("defeats", () => CheckDefeats(world));
        if (world.Tick - _lastTrack >= 150) Guarded("tracking", () => Track(world));
        if (_dirty || world.Tick - _lastPublish >= 60) Publish(world);
    }

    /// <summary>Runs one engine part; a failure is logged (at most once a minute) and the rest of the tick goes on.</summary>
    void Guarded(string part, Action a)
    {
        try { a(); }
        catch (Exception e)
        {
            long now = Environment.TickCount64;
            if (now - _lastFailLog > 60000)
            {
                _lastFailLog = now;
                RadiantQuestsMod.Log($"{part} failed this tick (skipped): {e.GetType().Name}: {e.Message}");
            }
        }
    }

    long _lastFailLog = long.MinValue / 2;

    public void Ready(World world, bool fromSave)
    {
        ScanClerks(world);
        Publish(world);
        RadiantQuestsMod.Detail($"world ready ({(fromSave ? "save" : "new game")}): {_s.Quests.Count} quests, rep {_s.Lifetime}/{_s.Balance}, "
            + $"{_clerks.Count} clerks loaded");
    }

    #endregion

    #region Clerks

    /// <summary>
    /// The guild's contacts: the barman (the leader of the squad living in a bar) of every town owned by a guild faction, whoever runs the bar
    /// (Kenshi often has the Trade Ninjas run other factions' bars, as in The Hub). The work is for the TOWN'S OWNER, read live. Neither the
    /// barman's faction nor the owner may be hostile to you now.
    /// </summary>
    void ScanClerks(World world)
    {
        _lastScan = world.Tick;
        var table = world.Characters;
        var found = new Dictionary<CharacterId, ClerkInfo>();
        // Clerks: the bartenders; in a town with no bar (hive villages, lagoons...) its traders; with neither, its headquarters' people.
        // Best kind per site wins (0 bar, 1 shop, 2 headquarters).
        var bestAt = new Dictionary<int, int>();
        var candidates = new List<(int Kind, ClerkInfo Info)>();
        foreach (var squad in world.Squads.All)
        {
            if (squad.Faction < 0) continue;
            int designation = DesignationOf(squad.TemplateId);
            int kind = squad.HomeBuilding?.IsBar == true || designation == DesignationBar ? 0
                : squad.HomeBuilding?.IsShop == true || designation == DesignationShop ? 1
                : designation == DesignationHq ? 2 : -1;
            if (kind < 0) continue;
            if (!table.TryResolveNext(squad.Leader, out int slot) || !table.Next[slot].Alive || table.Cold(slot) is not { } cold) continue;
            if (cold.Faction != squad.Faction) continue;
            var pos = table.Next[slot].Position;
            int site = squad.Town >= 0 ? squad.Town : _data.TownAt(pos);
            if (site < 0 || site >= _data.Sites.Count) continue;
            // The town as it is NOW (a taken-over or rebuilt town is a new TOWN record with its new owner): a town or village, owned by a real
            // faction (not you) that isn't hostile to you now, and a barman whose faction isn't either (his rule, 2026-11-23: clerks follow the
            // world, not a list made at start - the Hub's outlaws or a mod's new owner get a clerk while they deal with you).
            var townData = _data.Sites[site].Town;
            if (townData.Type is not (Meitou.Data.Gameplay.TownType.Town or Meitou.Data.Gameplay.TownType.Village)) continue;
            if (townData.Faction is not { } owner || _data.FactionIndex(owner) is var ownerIndex && ownerIndex < 0) continue;
            if (ownerIndex == _data.PlayerFaction) continue;
            if (Access.Hostile(squad.Faction, _data.PlayerFaction) || Access.Hostile(ownerIndex, _data.PlayerFaction)) continue;
            candidates.Add((kind, new ClerkInfo(squad.Leader, site, owner, Cat.FactionNames.GetValueOrDefault(owner, owner), townData.Name, pos)));
            bestAt[site] = Math.Min(bestAt.GetValueOrDefault(site, int.MaxValue), kind);
        }
        foreach (var (kind, info) in candidates)
            if (bestAt[info.Site] == kind) found[info.Id] = info;
        if (found.Count != _clerks.Count || found.Keys.Any(k => !_clerks.ContainsKey(k))) _dirty = true;
        _clerks = found;
        // Boards are put up as soon as a town's clerk is around in a new period, not when the window is first opened, and kept in the save:
        // reloading and opening the board at another moment can't change what is on it (no reload-rolling).
        // Not before the place around has loaded (right after you arrive the groups around aren't there yet, and the board would hold only
        // brought-in work): wait until groups stand near, at most a minute after the clerk was first seen (a truly empty place still gets one).
        foreach (var site in _siteSeen.Keys.Where(s => !found.Values.Any(c => c.Site == s)).ToList()) _siteSeen.Remove(site);
        long period = Period(world);
        foreach (var info in found.Values.GroupBy(c => c.Site).Select(g => g.OrderBy(c => c.Id.Slot).First()).OrderBy(c => c.Site))
        {
            if (!_siteSeen.ContainsKey(info.Site)) _siteSeen[info.Site] = world.Tick;
            if (_s.Boards.Any(b => b.Site == info.Site && b.Day == period)) continue;
            var at = new Vector2(info.Position.X, info.Position.Z);
            bool loaded = Access.Groups(world).Any(g => Vector2.Distance(g.At, at) <= _set.TargetRange);
            if (loaded || world.Tick - _siteSeen[info.Site] >= 1800) BoardFor(world, info);
        }
    }

    /// <summary>The game's BuildingDesignation values: shop 1, bar 3, headquarters 8.</summary>
    const int DesignationShop = 1, DesignationBar = 3, DesignationHq = 8;

    /// <summary>A squad template's building designation (cached; 0 when the data can't say).</summary>
    int DesignationOf(string id)
    {
        if (_designations.TryGetValue(id, out int d)) return d;
        d = 0;
        if (_data.Db.Find(id) is { Type: FcsRecordType.SquadTemplate } rec)
        {
            try { d = SquadTemplate.From(rec).BuildingDesignation; }
            catch { }
        }
        return _designations[id] = d;
    }

    /// <summary>The clerk, when one of your characters stands near it; else null with the reason told.</summary>
    ClerkInfo? ClerkInReach(World world, CharacterId clerk)
    {
        if (!_clerks.TryGetValue(clerk, out var info))
        {
            RadiantQuestsMod.Toast("The guild clerk is gone.");
            return null;
        }
        if (Access.YoursNear(world, clerk, ClerkReach)) return info;
        RadiantQuestsMod.Toast("Walk one of your characters up to the clerk first.");
        return null;
    }

    #endregion

    #region Quests

    void Accept(World world, AcceptOffer a)
    {
        if (ClerkInReach(world, a.Clerk) is not { } clerk) return;
        var board = BoardFor(world, clerk);
        var offers = a.Faction ? board.FactionOffers : board.Offers;
        if (board.Site != a.Site || a.Slot < 0 || a.Slot >= offers.Count) return;
        var offer = offers[a.Slot];
        int rank = a.Faction ? RankWith(clerk.Faction) : Rank;
        if (offer.Taken) return;
        if (offer.Rank > rank)
        {
            RadiantQuestsMod.Toast(a.Faction
                ? $"You need rank {Ranks.Name(offer.Rank)} with {clerk.FactionName} for that quest."
                : $"You need guild rank {Ranks.Name(offer.Rank)} for that quest.");
            return;
        }
        if (QuestTypes.Of(offer.Kind) is not { } type) return;

        var (catsShare, repShare) = _set.ReduceBelowRank ? Ranks.BelowRankPay(rank - offer.Rank) : (1.0, 1.0);
        var q = new Quest
        {
            Id = _s.NextId,
            Kind = offer.Kind,
            TargetId = offer.TargetId,
            TargetName = offer.TargetName,
            Rank = offer.Rank,
            Need = offer.Need,
            Cats = (long)Math.Round(offer.Cats * catsShare * _set.CatsMult),
            Rep = (int)Math.Round(offer.Rep * repShare * _set.RepMult),
            GiverFaction = clerk.Faction,
            Site = clerk.Site,
            General = !a.Faction,
            Deadline = world.GameHours + _set.KillHours,
            AnchorX = clerk.Position.X,
            AnchorZ = clerk.Position.Z,
            Group = offer.Group,
            GroupSize = offer.GroupSize,
            StartsWar = offer.StartsWar,
            Relation = offer.Relation,
            Stray = offer.Stray,
            Dangerous = offer.Dangerous,
            AcceptedTick = world.Tick,
            Score = offer.Score,
            Age = offer.Age,
            Strays = offer.Strays,
        };
        // Taken or not, the offer is used up: a cleared board stays cleared until it refreshes.
        offer.Taken = true;
        _dirty = true;
        if (!type.Accept(world, this, q, offer, new Vector2(clerk.Position.X, clerk.Position.Z))) return;
        q.FullNeed = q.Need;
        _s.NextId++;
        _s.Quests.Add(q);
        RadiantQuestsMod.Toast($"Quest taken{(q.General ? "" : $" for {clerk.FactionName}")}: {q.Describe()} ({Ranks.Name(q.Rank)}). "
            + $"{_set.KillHours / 24.0:0.#} days.");
    }

    void HandInQuest(World world, HandIn h)
    {
        var q = _s.Quests.FirstOrDefault(x => x.Id == h.QuestId);
        if (q is null) return;
        if (!q.Done)
        {
            RadiantQuestsMod.Toast($"Not done yet: {q.Have}/{q.Need}.");
            return;
        }
        if (ClerkInReach(world, h.Clerk) is not { } clerk) return;
        if (!q.General && clerk.Faction != q.GiverFaction)
        {
            RadiantQuestsMod.Toast("Hand this one in to the faction that gave it.");
            return;
        }
        End(world, q);
        // Targets someone else beat are not paid for (bugs and stuck ones are): cats, rep, points and relations by the share you did.
        double share = q.PayShare;
        long cats = q.CatsNow;
        int rep = q.RepNow;
        string cut = share < 1 ? $" ({q.LostToOthers.Count(l => !q.Defeated.Contains(l))} were beaten by others: {share:P0} of the pay)" : "";
        Access.Pay(world, cats);
        if (q.General)
            RadiantQuestsMod.Toast($"Quest done: {q.Describe()}. +{cats} cats, +{rep} guild rep.{cut}");
        else
        {
            int relation = (int)Math.Floor((q.Relation >= 0 ? q.Relation : Ranks.Relation(q.Rank, 0)) * share);
            RadiantQuestsMod.Toast($"Quest done for {clerk.FactionName}: {q.Describe()}. +{cats} cats, +{rep} guild rep, +{rep} "
                + $"{clerk.FactionName} points" + (relation > 0 ? $", relations +{relation}." : ".") + cut);
            GainPoints(q.GiverFaction, rep);
            Access.ChangeRelation(world, _data.FactionIndex(q.GiverFaction), relation, "Guild quest");
        }
        GainRep(rep);
    }

    void AbandonQuest(World world, int id)
    {
        var q = _s.Quests.FirstOrDefault(x => x.Id == id);
        if (q is null) return;
        // Nothing of it can be found now (no target marked, none defeated): not your fault, so giving it up is free.
        bool nothing = !q.Done && q.Have == 0 && _markers.TryGetValue(q.Id, out var at) && at.Count == 0;
        End(world, q);
        int cost = nothing ? 0 : (int)Math.Round(q.Rep * _set.AbandonCost);
        RadiantQuestsMod.Toast($"Quest abandoned: {q.Describe()}." + (cost > 0 ? $" -{cost} guild rep" + (q.General ? "." : " and faction points.")
            : nothing ? " None could be found: no penalty." : ""));
        LoseRep(cost);
        if (!q.General) LosePoints(q.GiverFaction, cost);
    }

    /// <summary>A quest leaves the list (any way): its type gives back what it held.</summary>
    void End(World world, Quest q)
    {
        _s.Quests.Remove(q);
        _markers.Remove(q.Id);
        try { QuestTypes.Of(q.Kind)?.End(world, this, q); }
        catch (Exception e) { RadiantQuestsMod.Log($"ending quest #{q.Id} ({q.Kind}) skipped a step: {e.Message}"); }
        _dirty = true;
    }

    void Expire(World world)
    {
        foreach (var q in _s.Quests.ToList())
        {
            // A faction that turned hostile to you takes back its quests, with no cost to you.
            if (!q.General && Access.Hostile(_data.FactionIndex(q.GiverFaction), _data.PlayerFaction))
            {
                End(world, q);
                Notices.Post($"{Cat.FactionNames.GetValueOrDefault(q.GiverFaction, q.GiverFaction)} is hostile to you now: "
                    + $"their quest is called off ({q.Describe()}), no penalty.");
                continue;
            }
            if (q.Done || world.GameHours <= q.Deadline) continue;
            End(world, q);
            int cost = (int)Math.Round(q.Rep * _set.FailCost);
            int relation = q.General ? 0 : Ranks.FailRelation(q.Rank);
            Notices.Post($"Quest failed (out of time): {q.Describe()}." + (cost > 0 ? $" -{cost} guild rep" + (q.General ? "." : " and faction points.") : "")
                + (relation < 0 ? $" Relations {relation}." : ""));
            LoseRep(cost);
            if (!q.General)
            {
                LosePoints(q.GiverFaction, cost);
                Access.ChangeRelation(world, _data.FactionIndex(q.GiverFaction), relation, "Guild quest failed");
            }
        }
    }

    /// <summary>Every 5 s: each quest type marks its targets and refills or ends a quest that has nothing left to find.</summary>
    void Track(World world)
    {
        _lastTrack = world.Tick;
        foreach (var q in _s.Quests.ToList())
        {
            if (QuestTypes.Of(q.Kind) is not { } type) continue;
            var markers = new List<Vector2>();
            var result = type.Track(world, this, q, markers);
            _markers[q.Id] = markers;
            if (result == TrackResult.CallOff)
            {
                End(world, q);
                Notices.Post($"Quest called off, no penalty: {q.Describe()}.");
            }
        }
        _dirty = true;
    }

    void GainPoints(string faction, int amount)
    {
        if (amount <= 0) return;
        int before = RankWith(faction);
        _s.FactionPoints[faction] = _s.PointsWith(faction) + amount;
        if (RankWith(faction) > before)
            RadiantQuestsMod.Toast($"Rank up with {Cat.FactionNames.GetValueOrDefault(faction, faction)}! You are now their rank {Ranks.Name(RankWith(faction))}.");
    }

    void LosePoints(string faction, int amount)
    {
        if (amount <= 0 || !_s.FactionPoints.ContainsKey(faction)) return;
        int before = RankWith(faction);
        _s.FactionPoints[faction] = Math.Max(0, _s.PointsWith(faction) - amount);
        if (RankWith(faction) < before)
            RadiantQuestsMod.Toast($"Your rank with {Cat.FactionNames.GetValueOrDefault(faction, faction)} dropped to {Ranks.Name(RankWith(faction))}.");
    }

    void GainRep(int amount)
    {
        if (amount <= 0) return;
        int before = Rank;
        _s.Lifetime += amount;
        _s.Balance += amount;
        _dirty = true;
        if (Rank > before) RadiantQuestsMod.Toast($"Guild rank up! You are now rank {Ranks.Name(Rank)}: harder, better-paid quests are open.");
    }

    void LoseRep(int amount)
    {
        if (amount <= 0) return;
        int before = Rank;
        _s.Lifetime = Math.Max(0, _s.Lifetime - amount);
        _s.Balance = Math.Max(0, _s.Balance - amount);
        _dirty = true;
        if (Rank < before) RadiantQuestsMod.Toast($"Your guild rank dropped to {Ranks.Name(Rank)}.");
    }

    /// <summary>Spends <paramref name="amount"/> rep you can spend (lifetime rep and rank stay); false if you don't have it.</summary>
    bool SpendRep(long amount)
    {
        if (_s.Balance < amount) return false;
        _s.Balance -= amount;
        _dirty = true;
        return true;
    }

    #endregion

    #region Spawning near a town

    /// <summary>How near one of yours keeps a brought-in group from being removed (roughly what you can see).</summary>
    const float SeenReach = 3000;

    /// <summary>
    /// A quest ended (any way): groups the game made and the quest only tagged are given back at once; those it brought in keep the tag and
    /// leave the world later by <see cref="Cleanup"/> (his rule, 2026-11-23: spawns exist only for a taken quest).
    /// </summary>
    public void RemoveSpawned(World world, Quest q) => Access.ReleaseNatural(world, q.Tag);

    long _lastCleanup = long.MinValue / 2;

    /// <summary>The world's slow step: the only serial step where groups may be removed (Meitou's rule).</summary>
    public void SlowWorld(World world)
    {
        if (world.Tick - _lastCleanup >= 300)
        {
            Guarded("spawn checks", () => CheckSpawns(world));
            Guarded("cleanup", () => Cleanup(world));
        }
    }

    /// <summary>Broken spawns in a row after which a spawn group is not brought in again in this save.</summary>
    const int StrikesOut = 3;

    /// <summary>
    /// Every ten seconds: the first-spawn check of brought-in groups (his rules, 2026-11-23). A group is looked at once it is loaded and has
    /// settled (the second look). If it came out broken (WorldAccess.GroupProblem) it is given up on for its quest at once (marked skipped:
    /// the quest's tracking brings a replacement, from another spawn group where there is one) and is cleared away like any group no longer
    /// needed; its spawn group gets a strike, and after 3 in a row it is not brought in again in this save. A good spawn clears the strikes.
    /// </summary>
    void CheckSpawns(World world)
    {
        if (_s.SpawnChecks.Count == 0) return;
        var groups = Access.Groups(world);
        foreach (var (key, check) in _s.SpawnChecks.OrderBy(kv => kv.Key).ToList())
        {
            var g = groups.FirstOrDefault(x => x.Key == key);
            if (g is null || world.GameHours - check.Hours > 48) { _s.SpawnChecks.Remove(key); continue; }
            if (!Access.Loaded(world, g.Id)) continue;
            if (!check.LoadedSeen) { check.LoadedSeen = true; continue; }
            _s.SpawnChecks.Remove(key);
            _dirty = true;
            if (Access.GroupProblem(world, g.Id) is not { } problem)
            {
                _s.SpawnStrikes.Remove(check.Template);
                continue;
            }
            SpawnBroke(key, check, problem);
        }
    }

    /// <summary>A brought-in group came out broken: a strike for its spawn group, and its quest gives it up and brings a replacement.</summary>
    void SpawnBroke(ulong key, SpawnCheck check, string problem)
    {
        {
            int strikes = _s.SpawnStrikes[check.Template] = _s.SpawnStrikes.GetValueOrDefault(check.Template) + 1;
            RadiantQuestsMod.Log($"brought-in group {check.Template} came out broken ({problem}): strike {strikes} of {StrikesOut}"
                + (strikes >= StrikesOut ? "; not brought in again in this save (overrides \"allowspawn\" lets it back)" : ""));
            if (_s.Quests.FirstOrDefault(x => x.Id == check.Quest) is { } q)
            {
                if (!q.Skipped.Contains(key)) q.Skipped.Add(key);
                q.Broken.Remove(check.Template);
                q.Broken.Add(check.Template);   // last = the one to retry
                q.Retries++;
                q.LastRefill = -1;   // the replacement comes at once
            }
        }
    }

    /// <summary>
    /// Every ten seconds (his rule, 2026-11-23): a group the mod brought in leaves the world once it is no longer needed - its quest ended or
    /// gave up on it, or nobody in it stands - at the set hours after its last defeat (at once if it was never fought), and only when none of
    /// your characters is near (else as soon as you have all gone). The dead are the game's to clear as usual.
    /// </summary>
    void Cleanup(World world)
    {
        _lastCleanup = world.Tick;
        var groups = Access.Groups(world);
        var live = new HashSet<ulong>(groups.Select(g => g.Key));
        foreach (var key in _s.SpawnDefeats.Keys.Where(k => !live.Contains(k)).ToList()) _s.SpawnDefeats.Remove(key);
        foreach (var g in groups)
        {
            if (!g.ModSpawned || !g.Tag.StartsWith("radiantquests:", StringComparison.Ordinal)) continue;
            var q = _s.Quests.FirstOrDefault(x => x.Tag == g.Tag);
            bool needed = q is not null && !q.Skipped.Contains(g.Key) && g.Standing > 0;
            if (needed) continue;
            // Beaten by someone else (no defeat of yours on record): its timer starts when it is first seen with nobody standing.
            if (g.Standing == 0 && !_s.SpawnDefeats.ContainsKey(g.Key))
            {
                _s.SpawnDefeats[g.Key] = world.GameHours;
                continue;
            }
            double due = _s.SpawnDefeats.TryGetValue(g.Key, out double last) ? last + _set.DefeatedHours : 0;
            if (world.GameHours < due || Access.YoursNearPoint(world, g.At, SeenReach)) continue;
            if (Access.Despawn(world, g.Id))
            {
                _s.SpawnDefeats.Remove(g.Key);
                _dirty = true;
                RadiantQuestsMod.Detail($"brought-in group {g.TemplateId} ({g.Tag}) left the world");
            }
        }
    }

    /// <summary>
    /// The out-of-reach failsafe (any quest type can use it): when one of yours has been near <paramref name="groups"/> for the set hours with
    /// none of them reached (no one of yours in the fight) and no progress, the nearest such group is returned to be given up on. Never while
    /// you fight or progress.
    /// </summary>
    public GroupInfo? OutOfReach(World world, Quest q, IEnumerable<GroupInfo> groups)
    {
        GroupInfo? nearOne = null;
        bool contact = false;
        foreach (var g in groups)
        {
            var (isNear, inFight) = Access.Contact(world, g.Id, StuckNear, StuckReach);
            if (inFight) contact = true;
            if (isNear && nearOne is null) nearOne = g;
        }
        if (contact || nearOne is null || q.Have != q.NearHave)
        {
            q.NearSince = nearOne is null || contact ? -1 : world.GameHours;
            q.NearHave = q.Have;
            return null;
        }
        if (q.NearSince < 0) q.NearSince = world.GameHours;
        if (world.GameHours - q.NearSince < _set.StuckHours) return null;
        q.NearSince = -1;
        q.Skipped.Add(nearOne.Key);
        _dirty = true;
        RadiantQuestsMod.Detail($"quest #{q.Id}: group {nearOne.TemplateId} given up as out of reach");
        return nearOne;
    }

    /// <summary>How near one of yours must be to count as trying to reach a target, and how near is "reached" (in the fight).</summary>
    const float StuckNear = 1500, StuckReach = 400;

    /// <summary>
    /// Spawns one ordinary group of <paramref name="t"/> for <paramref name="q"/> in a region around the town where it naturally lives (any
    /// region where wild things live, for an out-of-region quest), from a third of the quest area out to the furthest-spawn setting; never a
    /// guild faction's or your own group. The group that fits <paramref name="want"/> best (default: what is still needed): where Meitou can
    /// cap a spawn, one whose sizes include it, spawned at exactly that many; else the one whose usual size is nearest. With
    /// <paramref name="most"/>, never more than that (a top-up never brings more than what is missing). Returns the group, null when none.
    /// </summary>
    public (int Size, ulong Key)? SpawnNear(World world, Quest q, Target t, Survey survey, int most = int.MaxValue, int want = 0)
    {
        if (want <= 0) want = Math.Max(1, q.Need - q.Have);
        want = Math.Min(want, most);
        bool cap = WorldAccess.CanCap;
        var templates = q.Stray ? Survey.AnyTemplates(Cat, t) : survey.LocalTemplates(Cat, t);
        // Groups set aside after 3 broken spawns in a row are not used (unless the overrides allow them). A group that just came out broken
        // for this quest is tried again first, at a new spot (his idea, 2026-11-23): a bad spot clears on the retry, a truly broken group
        // strikes out on this one quest and is never used again; then another group is used.
        templates = [.. templates.Where(id => _s.SpawnStrikes.GetValueOrDefault(id) < StrikesOut || Cat.AllowSpawnIds.Contains(id) || Cat.AllowSpawnIds.Contains(t.Id))];
        if (q.Broken.Count > 0 && templates.Contains(q.Broken[^1])) templates = [q.Broken[^1]];
        if (most < int.MaxValue && !cap)
            templates = [.. templates.Where(id => Cat.TemplateMaxSize.TryGetValue(id, out int size) && size <= most)];
        if (templates.Count == 0 || q.Site < 0) return null;
        // How far a template is from the size wanted: with the cap, 0 when its spawn range (Catalog.SpawnRange) holds it; else its usual
        // size's distance. A top-up (with "most") may be smaller than a range's low end: then the nearest.
        bool animals = t.Kind == Catalog.Hunt;
        double Off(string id)
        {
            if (!cap) return Math.Abs(Cat.TemplateSize.GetValueOrDefault(id, want) - want);
            var (lo, hi) = Cat.SpawnRange(id, animals);
            return hi < 1 ? 1000 : want < lo ? lo - want : want > hi ? want - hi : 0;
        }
        // (Retries and given-up groups change the seed, so a retry looks at a new spot.)
        // (Every spawn of the quest so far changes the seed, so a replacement or top-up never lands on the spot of the one before.)
        var dice = new Dice(world.Seed ^ Rng.Mix((ulong)q.Id * 0x9E3779B97F4A7C15UL + (ulong)q.Refills + ((ulong)q.Retries << 20) + ((ulong)q.Skipped.Count << 40)
            + ((ulong)q.Spawns << 50)) ^ 0x5241444951554553UL);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            double best = templates.Min(Off);
            var choice = templates.Where(id => Off(id) - best < 0.01).ToList();
            string template = choice[dice.Int(0, choice.Count - 1)];
            int faction = Access.SpawnFaction(template);
            if (faction < 0 || faction == _data.PlayerFaction || Array.BinarySearch(_guildFactions, faction) >= 0) continue;
            var regions = q.Stray ? Cat.WildRegions : Cat.TemplateRegions.GetValueOrDefault(template);
            if (regions is null || RingSpot(world, survey.At, regions, dice) is not { } spot) continue;
            var (size, key) = Access.Spawn(world, faction, template, q.Site, spot, q.Tag, cap ? want : 0);
            if (size <= 0) continue;
            q.Spawned = true;
            q.Spawns++;
            _s.SpawnChecks[key] = new SpawnCheck { Template = template, Quest = q.Id, Hours = world.GameHours };
            RadiantQuestsMod.Detail($"quest #{q.Id}: spawned {template} ({_data.FactionId(faction)}) at {spot.X:0},{spot.Y:0}, {size} strong (wanted {want})");
            return (size, key);
        }
        return null;
    }

    /// <summary>
    /// A spot for a group (his rules, 2026-11-23), on rings out from <paramref name="at"/>, as near as can be (seeded order): in one of
    /// <paramref name="regions"/> (the kind's own; any wild region for a rare out-of-region quest), out of sight of all your characters (it is
    /// still marked on the map at once), and good ground (WorldAccess.SpotProblem: dry, not steep, reachable from the town's side). Null when
    /// there is none out to the furthest-spawn setting.
    /// </summary>
    Vector2? RingSpot(World world, Vector2 at, IReadOnlySet<string> regions, Dice dice)
    {
        if (regions.Count == 0) return null;
        float range = _set.TargetRange, limit = Math.Max(range, _set.SpawnLimit);
        int seen = 0, region = 0, ground = 0, crowded = 0;
        string lastProblem = "";
        // Never on top of another group the mod brought in (his test, 2026-11-24: five quests' spawns landed on one spot and fought each other).
        var others = Access.Groups(world).Where(g => g.ModSpawned && g.Tag.StartsWith("radiantquests:", StringComparison.Ordinal)).Select(g => g.At).ToList();
        for (float r = range * 0.3f; r <= limit; r += Math.Max(1000, range * 0.1f))
        {
            double start = dice.Double() * Math.PI * 2;
            for (int i = 0; i < 16; i++)
            {
                double a = start + i * Math.PI / 8;
                var p = at + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * r;
                if (Access.Region(p) is not { } g || !regions.Contains(g)) { region++; continue; }
                if (Access.YoursNearPoint(world, p, SightReach)) { seen++; continue; }
                if (others.Any(o => Vector2.Distance(o, p) < SpawnSpacing)) { crowded++; continue; }
                if (Access.SpotProblem(world, p, at) is { } problem) { ground++; lastProblem = problem; continue; }
                return p;
            }
        }
        RadiantQuestsMod.Detail($"no spawn spot near {at.X:0},{at.Y:0}: {region} outside the regions, {seen} in your sight, {crowded} near another "
            + $"brought-in group, {ground} bad ground (last: {lastProblem})");
        return null;
    }

    /// <summary>How far your characters see (units): nothing is spawned nearer than this to any of them.</summary>
    const float SightReach = 3000;

    /// <summary>How far apart groups the mod brings in are kept (units), so two quests' spawns never meet at once.</summary>
    const float SpawnSpacing = 1500;

    #endregion

    #region View, hash, save

    /// <summary>Publishes the view at once (between ticks, after the paused interface's commands).</summary>
    public void PublishNow(World world) => Publish(world);

    void Publish(World world)
    {
        _dirty = false;
        _lastPublish = world.Tick;
        int rank = Rank;
        var quests = _s.Quests.Select(q => new QuestView(q.Id, q.Describe(), q.Rank, q.Have, q.Need,
            q.Deadline >= double.MaxValue / 2 ? double.PositiveInfinity : q.Deadline - world.GameHours, q.CatsNow, q.RepNow, q.Done, q.General,
            Cat.FactionNames.GetValueOrDefault(q.GiverFaction, q.GiverFaction),
            q.Done ? [] : _markers.TryGetValue(q.Id, out var at) ? [.. at] : [], q.TargetId, Difficulty.Band(q.Score, q.Rank), TownName(q.Site),
            q.General ? 0 : (int)Math.Floor((q.Relation >= 0 ? q.Relation : Ranks.Relation(q.Rank, 0)) * q.PayShare))).ToList();
        var boards = new Dictionary<int, BoardView>();
        OfferView View(Offer o, int myRank)
        {
            var (c, r) = _set.ReduceBelowRank ? Ranks.BelowRankPay(myRank - o.Rank) : (1.0, 1.0);
            return new OfferView(o.Slot, o.Describe(), o.Rank, (long)Math.Round(o.Cats * c * _set.CatsMult),
                (int)Math.Round(o.Rep * r * _set.RepMult), o.Taken, o.Rank > myRank, o.Relation, Difficulty.Band(o.Score, o.Rank));
        }
        foreach (var b in _s.Boards)
        {
            int frank = RankWith(b.GiverFaction);
            boards[b.Site] = new BoardView(b.Site, b.Day, [.. b.Offers.Select(o => View(o, rank))], [.. b.FactionOffers.Select(o => View(o, frank))],
                RerollCost(rank));
        }
        var standings = new Dictionary<string, FactionStanding>(StringComparer.Ordinal);
        foreach (var f in _clerks.Values.Select(c => c.Faction).Concat(_s.FactionPoints.Keys).Distinct())
        {
            int fr = RankWith(f);
            standings[f] = new FactionStanding(fr, _s.PointsWith(f), _set.NextRankAt(fr), Access.Relation(_data.FactionIndex(f), _data.PlayerFaction));
        }
        Volatile.Write(ref s_view, new GuildView(world.Tick, world.GameHours, _s.Lifetime, _s.Balance, rank, _set.NextRankAt(rank), quests, boards,
            new Dictionary<CharacterId, ClerkInfo>(_clerks), standings));
    }

    public void Hash(ref StateHasher hasher)
    {
        _s.Hash(ref hasher);
        HashDefeats(ref hasher);
    }

    static readonly JsonSerializerOptions s_json = new() { IncludeFields = true };

    public void Write(Stream stream) => JsonSerializer.Serialize(stream, _s, s_json);

    public void Read(Stream stream)
    {
        try
        {
            _s = JsonSerializer.Deserialize<GuildState>(stream, s_json) ?? new GuildState();
            _s.Quests ??= [];
            _s.Boards ??= [];
            _s.FactionPoints = new Dictionary<string, long>(_s.FactionPoints ?? [], StringComparer.Ordinal);
            _s.SpawnDefeats ??= [];
            _s.SpawnChecks ??= [];
            _s.SpawnStrikes = new Dictionary<string, int>(_s.SpawnStrikes ?? [], StringComparer.Ordinal);
            foreach (var b in _s.Boards) b.FactionOffers ??= [];
            foreach (var q in _s.Quests)
            {
                q.Defeated ??= [];
                q.Aged ??= [];
                q.Broken ??= [];
                q.Skipped ??= [];
                q.LostToOthers ??= [];
                q.Roster ??= [];
                // Before 0.7.0 a group quest was a "cull" with Group set.
                if (q.Group && q.Kind == Catalog.Cull) q.Kind = GroupQuest.KindId;
            }
            _dirty = true;
        }
        catch (Exception e)
        {
            RadiantQuestsMod.Log($"could not read the saved guild state, starting fresh: {e.Message}");
            _s = new GuildState();
        }
    }

    #endregion
}
