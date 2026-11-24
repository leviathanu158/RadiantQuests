// Radiant Quests for Meitou. GPLv3 or later.
// Where the player put the guild's windows (tracker, guild window, journal), how big, and their text size. Interface state only: kept in
// window-layout.json in the mod's folder, read once, written when a move, resize or text-size change ends. Never part of the world or the save,
// so it costs nothing to the simulation and is the same in every save. A bad or missing file just means the defaults.
using System.Numerics;
using System.Text.Json;
using Meitou.Data.Ui;

namespace RadiantQuests;

public static class WindowLayout
{
    /// <summary>One window: top-left corner (screen pixels; -1 = not placed yet), size, and text scale.</summary>
    public sealed class Window
    {
        public float X { get; set; } = -1;
        public float Y { get; set; } = -1;
        public float W { get; set; }
        public float H { get; set; }
        /// <summary>The text size: Kenshi's standard font in 0 Small, 1 MediumSmall, 2 Medium, 3 Large (the game's own sizes: a scale
        /// factor on one font barely changes it, Meitou snaps text to whole sizes).</summary>
        public int Font { get; set; } = 2;

        /// <summary>How much taller rows get at this text size.</summary>
        public float Scale => Font switch { 0 => 0.8f, 1 => 0.9f, 3 => 1.25f, _ => 1f };
    }

    /// <summary>The font of each step.</summary>
    public static readonly string[] Fonts = ["Kenshi_StandardFont_Small", "Kenshi_StandardFont_MediumSmall", "Kenshi_StandardFont_Medium", "Kenshi_StandardFont_Large"];

    static Dictionary<string, Window>? s_windows;

    /// <summary>The defaults: the tracker at the right a fifth down the screen, small text; the windows centred.</summary>
    static Dictionary<string, Window> Defaults() => new(StringComparer.Ordinal)
    {
        ["tracker"] = new() { W = 420, H = 230, Font = 1 },
        ["guild"] = new() { W = 940, H = 790 },
        ["journal"] = new() { W = 940, H = 640 },
    };

    static string? Path => RadiantQuestsMod.Context?.Folder is { } folder ? System.IO.Path.Combine(folder, "window-layout.json") : null;

    static Dictionary<string, Window> All
    {
        get
        {
            if (s_windows is not null) return s_windows;
            s_windows = Defaults();
            try
            {
                if (Path is { } p && File.Exists(p) && JsonSerializer.Deserialize<Dictionary<string, Window>>(File.ReadAllText(p)) is { } saved)
                    foreach (var (name, w) in saved)
                        if (s_windows.ContainsKey(name) && float.IsFinite(w.X) && float.IsFinite(w.Y) && float.IsFinite(w.W) && float.IsFinite(w.H))
                            s_windows[name] = w;
            }
            catch (Exception e) { RadiantQuestsMod.Log($"window layout not read (defaults used): {e.Message}"); }
            return s_windows;
        }
    }

    public static Window Get(string name) => All.TryGetValue(name, out var w) ? w : All[name] = new Window { W = 600, H = 400 };

    /// <summary>
    /// The window <paramref name="name"/> placed on <paramref name="screen"/>: first time at its default spot; always kept on screen (at least
    /// its title bar), no smaller than <paramref name="minW"/> x <paramref name="minH"/> and no bigger than the screen.
    /// </summary>
    public static Window Place(string name, UiRect screen, float minW, float minH)
    {
        var w = Get(name);
        w.W = Math.Clamp(w.W, minW, Math.Max(minW, screen.Width));
        w.H = Math.Clamp(w.H, minH, Math.Max(minH, screen.Height));
        w.Font = Math.Clamp(w.Font, 0, Fonts.Length - 1);
        if (w.X < 0 || w.Y < 0)
        {
            w.X = name == "tracker" ? screen.Width - w.W - 12 : (screen.Width - w.W) / 2;
            w.Y = name == "tracker" ? screen.Height * 0.22f : (screen.Height - w.H) / 2;
        }
        w.X = Math.Clamp(w.X, 0, Math.Max(0, screen.Width - Math.Min(w.W, 120)));
        w.Y = Math.Clamp(w.Y, 0, Math.Max(0, screen.Height - 40));
        return w;
    }

    /// <summary>Writes the layout (after a move, resize or text-size change); a failure is logged, the game goes on.</summary>
    public static void Save()
    {
        try
        {
            if (Path is { } p) File.WriteAllText(p, JsonSerializer.Serialize(All, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { RadiantQuestsMod.Log($"window layout not saved: {e.Message}"); }
    }
}
