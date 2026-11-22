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
//   accept <slot>          the Accept button of that offer on the open board
//   handin <quest id>      the Hand in button
//   abandon <quest id>     the Abandon button
//   kill <target id>       counts a kill of that faction or race for your quests (as if your character made it)
//   rep <n>                adds guild rep
//   catalog                every hunt and cull target with its strength and rank, written to <dir>\rq.catalog
//   report                 rep, rank, quests and the open board, one line each
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
            case "accept":
                if (s_openIndex < 0 || s_openIndex >= clerks.Count) return "open a clerk first";
                Send(new AcceptOffer(clerks[s_openIndex].Id, clerks[s_openIndex].Site, Arg(1)) { Tick = tick });
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
                        $"[{o.Slot}] {Ranks.Name(o.Rank)} {o.Text} {o.Cats}c {o.Rep}r{(o.Taken ? " taken" : "")}{(o.Locked ? " locked" : "")}")));
                return r.ToString();
            default:
                return "unknown command";
        }
    }
}
