// Radiant Quests for Meitou. GPLv3 or later.
// "Clear <camp>": an existing camp, nest or outpost near the town (never one the mod makes), ranked by everyone the game data says lives
// there. Clearing means everyone who is AT the camp (his rule, 2026-11-23), judged by a ROSTER:
//   - The roster is taken when one of yours first comes near: the camp's own people (their squads' home is this site) standing inside the
//     camp area. Passers-by of other groups are never on it. Until then the quest can't finish or fail by people wandering about.
//   - It adapts: the camp's own people who come home into the area later join the roster; roster members who wander off beyond the leash
//     (twice the area) are not needed while away, and are needed again if they come back.
//   - Cleared when your side fought there and no roster member stands within the leash.
//   - Stragglers (stuck, hidden, bugged): once the Group share of the roster is down, if no standing roster member comes within reach of
//     your squad while you are at the camp for the set game hours (Camp stragglers), it counts as cleared.
//   - Called off (no penalty) if the place is wiped out or changes hands before you fought there.
// Area, leash and reach come from the camp's own size in the game data (clamped), so modded camps of any size work.
using System.Numerics;
using Meitou.Simulation;

namespace RadiantQuests;

public sealed class LairQuest : IQuestType
{
    public const string KindId = "lair";

    public string Kind => KindId;

    static Catalog Cat => RadiantQuestsMod.Catalog;

    public bool Offered(SettingsCopy set, Tab tab) => set.LairQuests;

    public IEnumerable<Candidate> Candidates(BoardContext ctx, int rank)
    {
        var set = ctx.Guild.Settings;
        var near = ctx.Guild.Access.SitesNear(ctx.Survey.At, Math.Max(set.TargetRange, set.SpawnLimit));
        if (rank == 0 && (RadiantQuestsMod.Settings.Debug || TestChannel.On))
            RadiantQuestsMod.Detail($"lairs near ({ctx.Tab}): " + string.Join("; ", near.Where(s => Cat.Lairs.ContainsKey(s.TownId))
                .Select(s => Cat.Lairs[s.TownId] is var l ? $"{s.Name.Trim()} {Ranks.Name(Rank(l))} {(Allowed(ctx, l) ? "offered" : "not this tab's enemy")}" : "")));
        // Lairs stay put, so they may be a little further out than roaming targets: up to the furthest-spawn distance.
        foreach (var site in near)
        {
            if (!Cat.Lairs.TryGetValue(site.TownId, out var lair) || !Allowed(ctx, lair) || Rank(lair) != rank) continue;
            var offer = new Offer
            {
                Kind = Kind, TargetId = lair.Id, TargetName = site.Name.Trim(), Rank = rank, Group = false, GroupSize = lair.Residents,
                PlatoonKey = (ulong)site.Index, Score = Score(lair), Need = lair.Residents,
            };
            offer.SetAt(site.At);
            yield return new Candidate(offer, 1.5);
        }
    }

    static double Score(Lair lair) => Catalog.Fight(lair.Unit, lair.Residents);

    /// <summary>A camp's rank: everyone living there.</summary>
    public static int Rank(Lair lair) => Difficulty.RankOf(Score(lair));

    static bool Allowed(BoardContext ctx, Lair lair) => Allowed(ctx.Guild, lair, ctx.Tab, ctx.Giver);

    /// <summary>
    /// General: wild nests and outlaws' camps. Faction: camps of the town owner's enemies now. An owner that only holds camps (no roaming
    /// squads, so no quest target of its own: common with mods) is judged by its faction the same way.
    /// </summary>
    public static bool Allowed(GuildSystem guild, Lair lair, Tab tab, int giver)
    {
        if (lair.Owner is null) return tab == Tab.General && guild.Settings.Hunts;
        var t = Cat.FindTarget(Catalog.Cull, lair.Owner)
            ?? new Target(Catalog.Cull, lair.Owner, lair.Name, lair.Unit, 0);
        return tab == Tab.General ? guild.GeneralCull(t, giver) : guild.FactionEnemy(t, giver);
    }

    public bool Accept(World world, GuildSystem guild, Quest q, Offer offer, Vector2 at)
    {
        int site = (int)offer.PlatoonKey;
        if (guild.Access.Site(site) is not { } s || s.TownId != offer.TargetId)
        {
            RadiantQuestsMod.Toast("That place has changed since the board was put up. Pick another quest.");
            return false;
        }
        q.Lair = site;
        return true;
    }

    /// <summary>Before the roster: anyone living there (a blow on them marks the fight). After: roster members only.</summary>
    public bool Counts(Quest q, PersonFacts person, string race, string familyHead) =>
        q.Lair >= 0 && person.HomeSite == q.Lair && (!q.Rostered || q.Roster.Contains(person.Lasting));

    /// <summary>The camp area (people inside it are "at the camp"), the leash (beyond it they have left), and the reach that counts as meeting
    /// one: from the camp's own size, clamped so a huge town-sized record doesn't sweep in the countryside.</summary>
    public static (float Area, float Leash, float Reach) Sizes(SiteInfo site)
    {
        float area = Math.Clamp(site.Radius, 400f, 1000f);
        return (area, area * 2, Math.Clamp(area * 0.6f, 300f, 600f));
    }

    /// <summary>
    /// Updates the roster and the count from what is at the camp now, and says whether it is cleared. Called after every defeat of one of its
    /// people and from <see cref="Track"/>. Needs the camp loaded (nobody to see otherwise: nothing changes).
    /// </summary>
    public static bool Check(World world, GuildSystem guild, Quest q)
    {
        if (guild.Access.Site(q.Lair) is not { } site || site.TownId != q.TargetId) return false;
        var (area, leash, reach) = Sizes(site);
        var (people, yours) = guild.Access.CampPeople(world, q.Lair, site.At);
        if (people.Count == 0) return false;   // not loaded
        bool youNear = yours.Any(d => d <= leash);
        // The roster: taken when one of yours first comes near; the camp's own people standing in the area (later: those who come home).
        if (!q.Rostered && youNear && people.Any(p => p.Standing && p.FromCamp <= area)) q.Rostered = true;
        if (!q.Rostered) return false;
        foreach (var p in people)
            if (p.Standing && p.FromCamp <= area && !q.Roster.Contains(p.Id) && !q.Defeated.Contains(p.Id)) q.Roster.Add(p.Id);
        q.Need = Math.Max(1, q.Roster.Count);
        q.Have = Math.Min(q.Need, q.Roster.Count(q.Defeated.Contains));
        // Who must still fall: roster members standing within the leash (one who wandered off further is not needed while away).
        var needed = people.Where(p => p.Standing && p.FromCamp <= leash && q.Roster.Contains(p.Id)).ToList();
        if (!q.Hit) return false;
        if (needed.Count == 0) return true;
        // Stragglers: you are at the camp, most are down, and none of those left has come near you for a while.
        bool youThere = yours.Any(d => d <= area);
        if (!youThere || needed.Any(p => p.FromYou <= reach) || q.LastContact < 0) q.LastContact = world.GameHours;
        bool most = q.Have >= Math.Ceiling(q.Need * guild.Settings.GroupShare);
        return most && world.GameHours - q.LastContact >= guild.Settings.StragglerHours;
    }

    public TrackResult Track(World world, GuildSystem guild, Quest q, List<Vector2> markers)
    {
        if (q.Done) return TrackResult.Going;
        var site = guild.Access.Site(q.Lair);
        if (site is null || site.TownId != q.TargetId)
            return q.Hit ? Finish(q) : CallOff(q);   // wiped out or changed hands
        if (Check(world, guild, q)) return Finish(q);
        markers.Add(site.At);
        return TrackResult.Going;
    }

    static TrackResult Finish(Quest q)
    {
        q.Have = q.Need;
        RadiantQuestsMod.Toast($"{q.Describe()}: the place is cleared. Hand it in at a guild clerk.");
        return TrackResult.Done;
    }

    static TrackResult CallOff(Quest q)
    {
        RadiantQuestsMod.Toast($"\"{q.Describe()}\": the place was cleared before you fought there. Called off, no penalty.");
        return TrackResult.CallOff;
    }

    public void End(World world, GuildSystem guild, Quest q) { }
}
