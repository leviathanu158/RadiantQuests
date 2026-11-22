// Radiant Quests for Meitou. GPLv3 or later.
// The guild in the world: finds the clerks (each guild faction's bartenders), makes each town's board once a day, takes, counts, times out and
// pays quests. Everything here runs in the world's serial steps; the interface only reads the published GuildView and queues commands.
using System.Numerics;
using System.Text.Json;
using Meitou.Data.Gameplay;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Population;

namespace RadiantQuests;

/// <summary>A guild clerk the interface can open the window at.</summary>
public sealed record ClerkInfo(CharacterId Id, int Site, string Faction, string FactionName, string TownName, Vector3 Position);

public sealed record QuestView(int Id, string Text, int Rank, int Have, int Need, double HoursLeft, long Cats, int Rep, bool Done, bool General,
    string GiverName);

public sealed record OfferView(int Slot, string Text, int Rank, long Cats, int Rep, bool Taken, bool Locked);

public sealed record BoardView(int Site, long Day, IReadOnlyList<OfferView> Offers);

/// <summary>What the interface shows, made by the world after a change (never touched by the world again).</summary>
public sealed record GuildView(long Tick, double Hours, long Lifetime, long Balance, int Rank, long NextRankAt, IReadOnlyList<QuestView> Quests,
    IReadOnlyDictionary<int, BoardView> Boards, IReadOnlyDictionary<CharacterId, ClerkInfo> Clerks);

public sealed class GuildSystem : ITickSystem, IStateHashed, IModSaveData, ICharacterDeathObserver, IWorldReady
{
    /// <summary>The newest view of the running world (the interface's thread reads it).</summary>
    static GuildView? s_view;

    public static GuildView? View => Volatile.Read(ref s_view);

    /// <summary>How close (world units) one of your characters must stand to the clerk to take or hand in a quest.</summary>
    const float ClerkReach = 300;

    readonly PopulationData _data;
    readonly int[] _guildFactions;
    readonly Dictionary<string, bool> _barTemplates = new(StringComparer.Ordinal);
    GuildState _s = new();
    SettingsCopy _set = SettingsCopy.Defaults;
    Dictionary<CharacterId, ClerkInfo> _clerks = [];
    long _lastScan = long.MinValue / 2;
    long _lastPublish = long.MinValue / 2;
    bool _dirty = true;

    public GuildSystem(PopulationData data)
    {
        _data = data;
        _guildFactions = [.. RadiantQuestsMod.Catalog.GuildFactions.Select(data.FactionIndex).Where(i => i >= 0).Order()];
    }

    Catalog Cat => RadiantQuestsMod.Catalog;

    int Rank => _set.RankOf(_s.Lifetime);

    static long Day(World world) => (long)Math.Floor(world.GameHours / 24);

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
                    case HandIn h: HandInQuest(world, h); break;
                    case Abandon a: AbandonQuest(world, a.QuestId); break;
                    case TestKill k: Count(world, k.TargetId, k.TargetId); break;
                    case TestRep r: GainRep(r.Amount); break;
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
        if (world.Tick - _lastScan >= 120) ScanClerks(world);
        if (world.Tick % 60 == 0) Expire(world);
        if (_dirty || world.Tick - _lastPublish >= 60) Publish(world);
    }

    public void Ready(World world, bool fromSave)
    {
        ScanClerks(world);
        Publish(world);
        RadiantQuestsMod.Detail($"world ready ({(fromSave ? "save" : "new game")}): {_s.Quests.Count} quests, rep {_s.Lifetime}/{_s.Balance}, "
            + $"{_clerks.Count} clerks loaded");
    }

    #endregion

    #region Clerks

    /// <summary>The leader of every bar squad of a guild faction: the barman (docs/game/economy.md: the bar's goods are his).</summary>
    void ScanClerks(World world)
    {
        _lastScan = world.Tick;
        var table = world.Characters;
        var found = new Dictionary<CharacterId, ClerkInfo>();
        foreach (var squad in world.Squads.All)
        {
            if (squad.Faction < 0 || Array.BinarySearch(_guildFactions, squad.Faction) < 0) continue;
            if (squad.HomeBuilding?.IsBar != true && !IsBarTemplate(squad.TemplateId)) continue;
            if (!table.TryResolveNext(squad.Leader, out int slot) || !table.Next[slot].Alive || table.Cold(slot) is not { } cold) continue;
            if (cold.Faction != squad.Faction) continue;
            // A clerk of a faction hostile to you now (live relations) won't deal with you.
            if (_data.PlayerFaction >= 0 && _data.Relations.IsHostile(squad.Faction, _data.PlayerFaction)) continue;

            var pos = table.Next[slot].Position;
            int site = squad.Town >= 0 ? squad.Town : _data.TownAt(pos);
            string faction = _data.FactionId(squad.Faction) ?? "";
            string town = site >= 0 && site < _data.Sites.Count ? _data.Sites[site].Town.Name : "";
            found[squad.Leader] = new ClerkInfo(squad.Leader, site, faction, Cat.FactionNames.GetValueOrDefault(faction, faction), town, pos);
        }

        if (found.Count != _clerks.Count || found.Keys.Any(k => !_clerks.ContainsKey(k))) _dirty = true;
        _clerks = found;
    }

    bool IsBarTemplate(string id)
    {
        if (_barTemplates.TryGetValue(id, out bool bar)) return bar;
        bar = false;
        if (_data.Db.Find(id) is { Type: FcsRecordType.SquadTemplate } rec)
        {
            try { bar = SquadTemplate.From(rec).BuildingDesignation == 3; }
            catch { }
        }
        return _barTemplates[id] = bar;
    }

    /// <summary>The clerk, when one of your characters stands near it; else null with the reason told.</summary>
    ClerkInfo? ClerkInReach(World world, CharacterId clerk)
    {
        if (!_clerks.TryGetValue(clerk, out var info))
        {
            RadiantQuestsMod.Toast("The guild clerk is gone.");
            return null;
        }
        var table = world.Characters;
        if (!table.TryResolveNext(clerk, out int cs)) return null;
        var at = table.Next[cs].Position;
        var next = table.Next;
        for (int s = 0; s < next.Length; s++)
        {
            if (!next[s].Alive || table.Cold(s) is not { IsPlayer: true }) continue;
            var d = new Vector2(next[s].Position.X - at.X, next[s].Position.Z - at.Z);
            if (d.LengthSquared() <= ClerkReach * ClerkReach) return info;
        }
        RadiantQuestsMod.Toast("Walk one of your characters up to the clerk first.");
        return null;
    }

    #endregion

    #region Boards

    void Open(World world, CharacterId clerk)
    {
        if (!_clerks.TryGetValue(clerk, out var info)) return;
        BoardFor(world, info);
    }

    Board BoardFor(World world, ClerkInfo clerk)
    {
        long day = Day(world);
        _s.Boards.RemoveAll(b => b.Day != day);
        if (_s.Boards.FirstOrDefault(b => b.Site == clerk.Site) is { } have) return have;

        var board = MakeBoard(world, clerk.Site, clerk.Faction, day);
        _s.Boards.Add(board);
        _dirty = true;
        RadiantQuestsMod.Detail($"board for {clerk.TownName} (site {clerk.Site}), day {day}: "
            + string.Join("; ", board.Offers.Select(o => $"{Ranks.Name(o.Rank)} {o.Describe()} {o.Cats}c {o.Rep}r")));
        return board;
    }

    /// <summary>
    /// Today's General board of a town: D always has 2-4 quests (up to 5 while you are D or C); a rank below yours 1-3; your own rank almost
    /// always 1-2; one above sometimes; higher ones rarely.
    /// </summary>
    Board MakeBoard(World world, int site, string giver, long day)
    {
        var rng = new Roll(world.Seed ^ Rng.Mix((ulong)day * 0x9E3779B97F4A7C15UL) ^ Rng.Mix((ulong)(site + 1) * 0xC2B2AE3D27D4EB4FUL));
        int rank = Rank;
        int giverIndex = _data.FactionIndex(giver);
        var board = new Board { Site = site, Day = day, GiverFaction = giver };
        var used = new HashSet<string>(StringComparer.Ordinal);

        for (int b = 0; b <= 4; b++)
        {
            int count;
            if (b == 0) count = rng.Int(2, rank <= 1 ? 5 : 4);
            else if (b < rank) count = rng.Int(1, 3);
            else if (b == rank) count = rng.Chance(0.9) ? rng.Int(1, 2) : 0;
            else if (b == rank + 1) count = rng.Chance(0.4) ? 1 : 0;
            else count = rng.Chance(0.15) ? 1 : 0;

            var hunts = Cat.Hunts.Where(t => t.Rank == b).ToList();
            var culls = Cat.Culls.Where(t => t.Rank == b && GeneralCull(t, giverIndex)).ToList();
            for (int i = 0; i < count; i++)
            {
                // Never the same target twice on one board: pick a kind that still has a fresh target, then one of those.
                var freshHunts = hunts.Where(t => !used.Contains(t.Kind + t.Id)).ToList();
                var freshCulls = culls.Where(t => !used.Contains(t.Kind + t.Id)).ToList();
                var fresh = (freshHunts.Count, freshCulls.Count) switch
                {
                    (0, 0) => null,
                    (_, 0) => freshHunts,
                    (0, _) => freshCulls,
                    _ => rng.Chance(0.5) ? freshHunts : freshCulls,
                };
                if (fresh is null) break;
                var target = fresh[rng.Int(0, fresh.Count - 1)];
                used.Add(target.Kind + target.Id);

                var (kMin, kMax) = Ranks.KillCount(b);
                var (cMin, cMax) = Ranks.Cats(b);
                board.Offers.Add(new Offer
                {
                    Slot = board.Offers.Count,
                    Kind = target.Kind,
                    TargetId = target.Id,
                    TargetName = target.Name,
                    Rank = b,
                    Need = rng.Int(kMin, kMax),
                    Cats = (long)Math.Round((cMin + rng.Double() * (cMax - cMin)) / 10) * 10,
                    Rep = (int)Math.Round(Ranks.Rep(b) * (0.85 + 0.3 * rng.Double())),
                });
            }
        }
        return board;
    }

    /// <summary>
    /// A faction General quests may ask to cull: not a guild faction, hostile to the giver and to at least three in four guild factions, so
    /// bandits and cannibals qualify but a faction you are only at war with does not.
    /// </summary>
    bool GeneralCull(Target t, int giver)
    {
        int f = _data.FactionIndex(t.Id);
        if (f < 0 || f == _data.PlayerFaction || Array.BinarySearch(_guildFactions, f) >= 0) return false;
        // A faction the data marks as bandits is fair game unless it is friendly with the giver now.
        if (Cat.Bandits.Contains(t.Id)) return giver < 0 || _data.Relations.Get(giver, f) < 0;
        if (giver >= 0 && !_data.Relations.IsHostile(giver, f)) return false;
        if (_guildFactions.Length == 0) return true;
        int hostile = _guildFactions.Count(g => _data.Relations.IsHostile(g, f));
        return hostile * 4 >= _guildFactions.Length * 3;
    }

    #endregion

    #region Quests

    void Accept(World world, AcceptOffer a)
    {
        if (ClerkInReach(world, a.Clerk) is not { } clerk) return;
        var board = BoardFor(world, clerk);
        if (board.Site != a.Site || a.Slot < 0 || a.Slot >= board.Offers.Count) return;
        var offer = board.Offers[a.Slot];
        int rank = Rank;
        if (offer.Taken) return;
        if (offer.Rank > rank)
        {
            RadiantQuestsMod.Toast($"You need guild rank {Ranks.Name(offer.Rank)} for that quest.");
            return;
        }

        var (catsShare, repShare) = Ranks.BelowRankPay(rank - offer.Rank);
        var q = new Quest
        {
            Id = _s.NextId++,
            Kind = offer.Kind,
            TargetId = offer.TargetId,
            TargetName = offer.TargetName,
            Rank = offer.Rank,
            Need = offer.Need,
            Cats = (long)Math.Round(offer.Cats * catsShare * _set.CatsMult),
            Rep = (int)Math.Round(offer.Rep * repShare * _set.RepMult),
            GiverFaction = clerk.Faction,
            Site = clerk.Site,
            General = true,
            Deadline = world.GameHours + _set.KillHours,
        };
        offer.Taken = true;
        _s.Quests.Add(q);
        _dirty = true;
        RadiantQuestsMod.Toast($"Quest taken: {q.Describe()} ({Ranks.Name(q.Rank)}). {_set.KillHours / 24.0:0.#} days.");
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

        _s.Quests.Remove(q);
        world.Player.Money += q.Cats;
        RadiantQuestsMod.Toast($"Quest done: {q.Describe()}. +{q.Cats} cats, +{q.Rep} guild rep.");
        GainRep(q.Rep);
        _dirty = true;
    }

    void AbandonQuest(World world, int id)
    {
        var q = _s.Quests.FirstOrDefault(x => x.Id == id);
        if (q is null) return;
        _s.Quests.Remove(q);
        int cost = (int)Math.Round(q.Rep * _set.AbandonCost);
        RadiantQuestsMod.Toast($"Quest abandoned: {q.Describe()}." + (cost > 0 ? $" -{cost} guild rep." : ""));
        LoseRep(cost);
        _dirty = true;
    }

    void Expire(World world)
    {
        for (int i = _s.Quests.Count - 1; i >= 0; i--)
        {
            var q = _s.Quests[i];
            if (q.Done || world.GameHours <= q.Deadline) continue;
            _s.Quests.RemoveAt(i);
            int cost = (int)Math.Round(q.Rep * _set.FailCost);
            RadiantQuestsMod.Toast($"Quest failed (out of time): {q.Describe()}." + (cost > 0 ? $" -{cost} guild rep." : ""));
            LoseRep(cost);
            _dirty = true;
        }
    }

    public void Died(World world, in CharacterDeath death)
    {
        if (_s.Quests.Count == 0 || _data.PlayerFaction < 0 || death.KillerFaction != _data.PlayerFaction) return;
        if (death.Faction == _data.PlayerFaction) return;
        string faction = _data.FactionId(death.Faction) ?? "";
        string race = Cat.RaceOf.GetValueOrDefault(death.Record, "");
        RadiantQuestsMod.Detail($"kill: {death.Record} (faction {faction}, race {race})");
        Count(world, faction, race);
    }

    /// <summary>A kill by your side of someone of <paramref name="faction"/> and <paramref name="race"/>.</summary>
    void Count(World world, string faction, string race)
    {
        foreach (var q in _s.Quests)
        {
            if (q.Done) continue;
            bool match = q.Kind switch
            {
                Catalog.Cull => q.TargetId == faction,
                Catalog.Hunt => q.TargetId == race,
                _ => false,
            };
            if (!match) continue;
            q.Have++;
            q.Deadline = Math.Min(q.Deadline + _set.KillRefreshHours, world.GameHours + _set.KillHours);
            _dirty = true;
            RadiantQuestsMod.Toast(q.Done ? $"{q.Describe()}: done! Hand it in at any guild clerk." : $"{q.Describe()}: {q.Have}/{q.Need}");
        }
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
            q.Deadline >= double.MaxValue / 2 ? double.PositiveInfinity : q.Deadline - world.GameHours, q.Cats, q.Rep, q.Done, q.General,
            Cat.FactionNames.GetValueOrDefault(q.GiverFaction, q.GiverFaction))).ToList();
        var boards = new Dictionary<int, BoardView>();
        foreach (var b in _s.Boards)
        {
            boards[b.Site] = new BoardView(b.Site, b.Day, [.. b.Offers.Select(o =>
            {
                var (c, r) = Ranks.BelowRankPay(rank - o.Rank);
                return new OfferView(o.Slot, o.Describe(), o.Rank, (long)Math.Round(o.Cats * c * _set.CatsMult),
                    (int)Math.Round(o.Rep * r * _set.RepMult), o.Taken, o.Rank > rank);
            })]);
        }
        Volatile.Write(ref s_view, new GuildView(world.Tick, world.GameHours, _s.Lifetime, _s.Balance, rank, _set.NextRankAt(rank), quests, boards,
            new Dictionary<CharacterId, ClerkInfo>(_clerks)));
    }

    public void Hash(ref StateHasher hasher) => _s.Hash(ref hasher);

    static readonly JsonSerializerOptions s_json = new() { IncludeFields = true };

    public void Write(Stream stream) => JsonSerializer.Serialize(stream, _s, s_json);

    public void Read(Stream stream)
    {
        try
        {
            _s = JsonSerializer.Deserialize<GuildState>(stream, s_json) ?? new GuildState();
            _s.Quests ??= [];
            _s.Boards ??= [];
            _dirty = true;
        }
        catch (Exception e)
        {
            RadiantQuestsMod.Log($"could not read the saved guild state, starting fresh: {e.Message}");
            _s = new GuildState();
        }
    }

    #endregion

    /// <summary>A small seeded random sequence (same seed, same rolls on every machine).</summary>
    struct Roll(ulong seed)
    {
        ulong _state = seed;

        public ulong Next() => Rng.Mix(_state += 0x9E3779B97F4A7C15UL);

        public double Double() => (Next() >> 11) * (1.0 / (1UL << 53));

        public int Int(int min, int max) => max <= min ? min : min + (int)(Next() % (ulong)(max - min + 1));

        public bool Chance(double p) => Double() < p;
    }
}
