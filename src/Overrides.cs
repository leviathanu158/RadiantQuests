// Radiant Quests for Meitou. GPLv3 or later.
// The override files: the escape hatch for the rare thing the general rules get wrong in some load order. Read once at start from the mod's
// folder: overrides.json, then every overrides\*.json in name order (a patch for another mod can ship its own file there). Comments and trailing
// commas are allowed. An id the load order does not have is skipped (counted in the log), so a patch for a mod you don't run does nothing.
//
// {
//   "block":    ["<id>", ...],          never a quest target: a faction, an animal race, a character or animal record, or a squad template
//   "tame":     ["<race id>", ...],     an animal race to treat as tame (never hunted)
//   "wild":     ["<race id>", ...],     an animal race to treat as wild even if a friendly faction keeps it
//   "outlaw":   ["<faction id>", ...],  a faction to treat as bandits (General kill quests may target it)
//   "guild":    { "add": [...], "remove": [...] },   factions that do or don't have guild clerks
//   "strength": { "<faction or race id>": 1.5 }      multiplies that target's fighting strength (so its rank) by this
// }
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadiantQuests;

public sealed class Overrides
{
    public List<string> Block { get; set; } = [];
    public List<string> Tame { get; set; } = [];
    public List<string> Wild { get; set; } = [];
    public List<string> Outlaw { get; set; } = [];
    public GuildChange Guild { get; set; } = new();
    public Dictionary<string, double> Strength { get; set; } = [];

    public sealed class GuildChange
    {
        public List<string> Add { get; set; } = [];
        public List<string> Remove { get; set; } = [];
    }

    public static Overrides None { get; } = new();

    /// <summary>All the ids the files name (for counting the ones the load order lacks).</summary>
    public IEnumerable<string> AllIds => Block.Concat(Tame).Concat(Wild).Concat(Outlaw).Concat(Guild.Add).Concat(Guild.Remove).Concat(Strength.Keys);

    public bool Blocked(string id) => _block.Contains(id);

    HashSet<string> _block = new(StringComparer.Ordinal);

    static readonly JsonSerializerOptions s_json = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Reads and merges the override files of <paramref name="folder"/>; a file that can't be read is logged and skipped.</summary>
    public static Overrides Load(string folder, Action<string> log)
    {
        var all = new Overrides();
        var files = new List<string>();
        string main = Path.Combine(folder, "overrides.json");
        if (File.Exists(main)) files.Add(main);
        string dir = Path.Combine(folder, "overrides");
        if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase));
        foreach (var file in files)
        {
            try
            {
                var o = JsonSerializer.Deserialize<Overrides>(File.ReadAllText(file), s_json);
                if (o is null) continue;
                all.Block.AddRange(o.Block ?? []);
                all.Tame.AddRange(o.Tame ?? []);
                all.Wild.AddRange(o.Wild ?? []);
                all.Outlaw.AddRange(o.Outlaw ?? []);
                all.Guild.Add.AddRange(o.Guild?.Add ?? []);
                all.Guild.Remove.AddRange(o.Guild?.Remove ?? []);
                foreach (var (id, m) in o.Strength ?? []) if (double.IsFinite(m) && m > 0) all.Strength[id] = m;
            }
            catch (Exception e)
            {
                log($"override file {Path.GetFileName(file)} skipped (not valid): {e.Message}");
            }
        }
        all._block = new HashSet<string>(all.Block, StringComparer.Ordinal);
        if (files.Count > 0) log($"overrides: {files.Count} file(s), {all.AllIds.Count()} entries");
        return all;
    }
}
