// Radiant Quests for Meitou. GPLv3 or later.
//
// The test channel: lets a test script do what the player does in the guild window, so the mod can be tested without a mouse. Off for players:
// it runs only when the environment variable RADIANTQUESTS_TEST_DIR names a folder. The script writes command lines to <dir>\rq.cmd; each frame
// the mod reads and deletes that file, does what it says, and appends one result line per command to <dir>\rq.out.
//
// Commands (one per line):
//   clerks                 the clerks loaded now: index, faction, town, position
//   open <index>           opens the guild window at that clerk (as the right-click row does)
//   close                  closes it
//   accept <slot>          the Accept button of that offer on the open board (General tab)
//   faccept <slot>         the same on the Faction tab
//   factions               your standing with each faction: rank, points, live relation
//   handin <quest id>      the Hand in button
//   abandon <quest id>     the Abandon button
//   kill <target id>       counts a kill of that faction or race for your quests (as if your character made it)
//   rep <n>                adds guild rep
//   catalog                every hunt and cull target with its strength and rank, written to <dir>\rq.catalog
//   report                 rep, rank, quests and the open board, one line each
//   targets                each quest's target groups as the map shows them: id, text, and x,z of each group
//   families               the faction families (variant -> head)
//   skip <hours>           every quest's deadline comes that many game hours closer (to test running out of time)
//   finish <quest id>      counts kills for that quest until it is done
//   quests                 the quests only: id kind target have/need done hours-left
using System.Globalization;
using System.Text;

namespace RadiantQuests;

internal static class TestChannel
{
    static readonly string? s_dir = Environment.GetEnvironmentVariable("RADIANTQUESTS_TEST_DIR") is { Length: > 0 } d && Directory.Exists(d) ? d : null;
    static bool s_told;
    static int s_openIndex = -1;

    internal static bool On => s_dir is not null;

    public static void Poll()
    {
        if (s_dir is null) return;
        if (!s_told)
        {
            s_told = true;
            RadiantQuestsMod.Log($"test channel on: reading {Path.Combine(s_dir, "rq.cmd")}");
        }
        string cmd = Path.Combine(s_dir, "rq.cmd");
        if (!File.Exists(cmd)) return;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(cmd);
            File.Delete(cmd);
        }
        catch (IOException)
        {
            return;
        }

        var results = new List<string>();
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            string result;
            try { result = Run(line); }
            catch (Exception e) { result = "error: " + e.Message; }
            results.Add($"{line} => {result}");
        }
        try { File.AppendAllLines(Path.Combine(s_dir, "rq.out"), results); }
        catch (IOException) { }
    }

    static string Run(string line)
    {
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var view = GuildSystem.View;
        long tick = RadiantQuestsMod.Context?.Tick ?? 0;
        var clerks = view?.Clerks.Values.OrderBy(c => c.Site).ThenBy(c => c.Id.Slot).ToList() ?? [];
        int Arg(int i) => p.Length > i && int.TryParse(p[i], out int v) ? v : -1;
        void Send(Meitou.Simulation.SimCommand c) => RadiantQuestsMod.Context?.Enqueue(c);

        switch (p[0])
        {
            case "clerks":
                return clerks.Count == 0 ? "none loaded" : string.Join(" | ", clerks.Select((c, i) =>
                    $"{i}: {c.FactionName} / {c.TownName} (site {c.Site}) at {c.Position.X:0},{c.Position.Z:0}"));
            case "open":
                if (Arg(1) is var n && (n < 0 || n >= clerks.Count)) return "no such clerk";
                s_openIndex = n;
                GuildUi.OpenAt(clerks[n]);
                return $"opened at {clerks[n].FactionName} / {clerks[n].TownName}";
            case "close":
                GuildUi.Close();
                return "closed";
            case "journal":
                GuildUi.Close();
                GuildUi.ToggleJournal();
                return "journal toggled";
            case "accept":
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                Send(new AcceptOffer(clerks[s_openIndex].Id, clerks[s_openIndex].Site, Arg(1)) { Tick = tick });
                return "sent";
            case "faccept":
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                Send(new AcceptOffer(clerks[s_openIndex].Id, clerks[s_openIndex].Site, Arg(1), true) { Tick = tick });
                return "sent";
            case "factions":
                if (view is null) return "no view yet";
                return string.Join(" | ", view.Factions.Select(kv => string.Create(CultureInfo.InvariantCulture,
                    $"{RadiantQuestsMod.Catalog.FactionNames.GetValueOrDefault(kv.Key, kv.Key)}: rank {Ranks.Name(kv.Value.Rank)} {kv.Value.Points} pts relation {kv.Value.Relation:0.##}")));
            case "reroll":
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                Send(new RerollBoard(clerks[s_openIndex].Id) { Tick = tick });
                return "sent";
            case "handin":
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                Send(new HandIn(clerks[s_openIndex].Id, Arg(1)) { Tick = tick });
                return "sent";
            case "abandon":
                Send(new Abandon(Arg(1)) { Tick = tick });
                return "sent";
            case "kill":
                if (p.Length < 2) return "kill <target id>";
                Send(new TestKill(p[1]) { Tick = tick });
                return "sent";
            case "rep":
                Send(new TestRep(Arg(1)) { Tick = tick });
                return "sent";
            case "sites":
                var data = RadiantQuestsMod.Context?.Population;
                if (data is null) return "no population";
                var guild = RadiantQuestsMod.Catalog.GuildFactions;
                return string.Join(" | ", data.Sites.Where(s => s.Town.Faction is { } f && guild.Contains(f) && s.Town.BarSquads.Count > 0).Take(40)
                    .Select(s => $"{s.Index}: {s.Town.Name} ({RadiantQuestsMod.Catalog.FactionNames.GetValueOrDefault(s.Town.Faction!, "")}) at {s.Position.X:0},{s.Position.Z:0}"));
            case "targets":
                if (view is null) return "no view yet";
                return view.Quests.Count == 0 ? "no quests" : string.Join(" | ", view.Quests.Select(q =>
                    $"#{q.Id} {q.Text}: {q.Targets.Count} groups" + (q.Targets.Count > 0
                        ? " at " + string.Join(" ", q.Targets.Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.X:0},{t.Y:0}")))
                        : "")));
            case "families":
                var cat2 = RadiantQuestsMod.Catalog;
                return cat2.FamilyHead.Count == 0 ? "none" : string.Join(" | ", cat2.FamilyHead.Select(kv =>
                    $"{cat2.FactionNames.GetValueOrDefault(kv.Key, kv.Key)} -> {cat2.FactionNames.GetValueOrDefault(kv.Value, kv.Value)}"));
            case "skip":
                if (p.Length < 2 || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double hours)) return "skip <hours>";
                Send(new TestSkip(hours) { Tick = tick });
                return "sent";
            case "offer":
            case "take":
                // offer <kind> <rank 0-6>: one offer of that kind on the open clerk's board (see the log for its slot).
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                if (p.Length < 3) return "offer <kind> <rank>";
                Send(new TestOfferCmd(clerks[s_openIndex].Id, p[1], Arg(2), p[0] == "take", string.Join(" ", p.Skip(3))) { Tick = tick });
                return "sent (see log: test offer)";
            case "asktown":
                // A townsperson's answer (the open clerk stands in for one).
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                Send(new AskTownsfolk(clerks[s_openIndex].Id) { Tick = tick });
                return "sent";
            case "giveq":
                Send(new TestGiveItem(Arg(1), Math.Max(1, Arg(2))) { Tick = tick });
                return "sent";
            case "courier":
                Send(new TestCourier(Arg(1)) { Tick = tick });
                return "sent (see log: test courier)";
            case "rumour":
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                Send(new BuyRumour(clerks[s_openIndex].Id, Arg(1)) { Tick = tick });
                return "sent";
            case "pick":
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                Send(new ShowHandIn(clerks[s_openIndex].Id, Arg(1)) { Tick = tick });
                return "sent (then: picks)";
            case "picks":
                return view?.HandIn is { } hv ? $"#{hv.QuestId} need {hv.Need}: " + string.Join(" | ", hv.Choices.Select(c =>
                    $"[{c.Key}] {c.Units}x {c.Name} ({c.Holder} {c.Who}, cells {string.Join(",", c.Cells)}) worth {c.Worth}{(c.Problem.Length > 0 ? " - " + c.Problem : "")}")) : "no pick list";
            case "give":
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                var keys = p.Skip(2).Select(k => int.TryParse(k, out int v) ? v : -1).ToHashSet();
                Send(new HandInPicked(clerks[s_openIndex].Id, Arg(1), [.. (view?.HandIn?.Choices ?? []).Where(c => keys.Contains(c.Key)).Select(c => c.Id)]) { Tick = tick });
                return "sent";
            case "hide":
                Send(new ToggleMarkers(Arg(1)) { Tick = tick });
                return "sent";
            case "ambush":
                Send(new TestAmbush(Arg(1)) { Tick = tick });
                return "sent";
            case "finish":
                Send(new TestFinish(Arg(1)) { Tick = tick });
                return "sent";
            case "herd":
                Send(new TestHerd(Arg(1)) { Tick = tick });
                return "sent (see log: herd #)";
            case "setages":
                Send(new TestSetAges(Arg(1), [.. p.Skip(2).Select(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 1f)])
                    { Tick = tick });
                return "sent";
            case "breakspawn":
                Send(new TestBreakSpawn(Arg(1)) { Tick = tick });
                return "sent";
            case "quests":
                if (view is null) return "no view yet";
                return view.Quests.Count == 0 ? "no quests" : string.Join(" | ", view.Quests.Select(q =>
                    string.Create(CultureInfo.InvariantCulture, $"#{q.Id} {q.Text} {q.Have}/{q.Need} {(q.Done ? "DONE" : $"{q.HoursLeft:0.0}h")}{(q.Rumour.Length > 0 ? $" [rumour: {q.Rumour}]" : "")}{(q.MarkersHidden ? " [hidden]" : "")}")));
            case "member":
                // A CHARACTER record of a kill target's squads (one without spaces in its id, for the agent channel's spawn-at), or of a quest's target.
                if (p.Length < 2) return "member <target id or #quest id>";
                string target = p[1];
                if (target.StartsWith('#') && int.TryParse(target[1..], out int qid) && view?.Quests.FirstOrDefault(x => x.Id == qid) is { } qv)
                    target = qv.TargetId;
                var data2 = RadiantQuestsMod.Context?.Data;
                if (data2 is null) return "no data";
                foreach (var tid in RadiantQuestsMod.Catalog.CullTemplates.GetValueOrDefault(target, []))
                    if (data2.Find(tid) is { } trec)
                        foreach (var l in trec.GetReferences("squad").Concat(trec.GetReferences("leader")))
                            if (!l.TargetStringId.Contains(' ') && data2.Find(l.TargetStringId) is { Type: Meitou.Data.Fcs.FcsRecordType.Character, } c && !c.GetBool("unique"))
                                return target.Contains(' ') ? "none" : $"{l.TargetStringId} --faction {target}";
                return "none";
            case "lairs":
                // The lairs near the open clerk's town and what the catalog knows of each (a lair is offered only at its rank, when allowed).
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                var ck = clerks[s_openIndex];
                var data3 = RadiantQuestsMod.Context?.Population;
                if (data3 is null) return "no population";
                var at3 = new System.Numerics.Vector2(ck.Position.X, ck.Position.Z);
                var near3 = data3.Sites.Select(s => (s, d: System.Numerics.Vector2.Distance(new System.Numerics.Vector2(s.Position.X, s.Position.Z), at3)))
                    .Where(x => x.d <= RadiantQuestsMod.Settings.SpawnLimit).OrderBy(x => x.d).Take(25)
                    .Select(x => RadiantQuestsMod.Catalog.Lairs.TryGetValue(x.s.Town.Id, out var lr)
                        ? $"{x.s.Town.Name} ({x.d:0}): lair, {lr.Residents} x {lr.Unit:0.0}, owner {lr.Owner ?? "wild"}, rank {Ranks.Name(LairQuest.Rank(lr))}"
                        : $"{x.s.Town.Name} ({x.d:0}): {x.s.Town.Type}, not a lair");
                return string.Join(" | ", near3);
            case "checks":
                File.WriteAllLines(Path.Combine(s_dir!, "rq.checks"), RadiantQuestsMod.Catalog.Checks);
                return $"{RadiantQuestsMod.Catalog.Checks.Count} notes written to rq.checks";
            case "catalog":
                var sb = new StringBuilder();
                var cat = RadiantQuestsMod.Catalog;
                foreach (var t in cat.Hunts.Concat(cat.Culls))
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{t.Kind}\t{Ranks.Name(t.Rank)}\t{t.Unit:0.0}\t{t.Id}\t{t.Name}"));
                sb.AppendLine("guild factions: " + string.Join(", ", cat.GuildFactions.Select(f => cat.FactionNames.GetValueOrDefault(f, f))));
                File.WriteAllText(Path.Combine(s_dir!, "rq.catalog"), sb.ToString());
                return $"{cat.Hunts.Count} hunts, {cat.Culls.Count} culls, {cat.GuildFactions.Count} guild factions";
            case "report":
                if (view is null) return "no view yet";
                var r = new StringBuilder();
                r.Append($"tick {view.Tick} hours {view.Hours:0.0} rank {Ranks.Name(view.Rank)} lifetime {view.Lifetime} balance {view.Balance}; ");
                r.Append($"window {(GuildUi.IsOpen ? "open" : "closed")}; quests: ");
                r.Append(string.Join(" | ", view.Quests.Select(q => $"#{q.Id} {Ranks.Name(q.Rank)} {q.Text} {q.Have}/{q.Need} {q.Cats}c {q.Rep}r "
                    + $"{(q.Done ? "DONE" : q.HoursLeft.ToString("0.0", CultureInfo.InvariantCulture) + "h")}")));
                if (s_openIndex >= 0 && s_openIndex < clerks.Count && view.Boards.TryGetValue(clerks[s_openIndex].Site, out var b))
                    r.Append("; board: " + string.Join(" | ", b.Offers.Select(o =>
                        $"[{o.Slot}] {Ranks.Name(o.Rank)} {o.Text} {o.Cats}c {o.Rep}r{(o.Taken ? " taken" : "")}{(o.Locked ? " locked" : "")}"))
                        + "; faction board: " + string.Join(" | ", b.FactionOffers.Select(o =>
                        $"[{o.Slot}] {Ranks.Name(o.Rank)} {o.Text} {o.Cats}c {o.Rep}r{(o.Relation > 0 ? $" +{o.Relation}rel" : "")}{(o.Taken ? " taken" : "")}{(o.Locked ? " locked" : "")}")));
                return r.ToString();
            default:
                return "unknown command";
        }
    }
}
