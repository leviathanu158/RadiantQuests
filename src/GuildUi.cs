// Radiant Quests for Meitou. GPLv3 or later.
// The interface: the "Adventurers' Guild" row in a clerk's right-click menu, the guild window, the quest journal, the quest tracker and the
// guild's messages. Everything here runs on the interface's thread and only reads GuildSystem.View; a button queues a command for the world.
//
// Windows (his requests, 2026-11-23/24): the tracker, the guild window and the journal can each be moved (drag the title bar; the tracker by
// its "::" grip), resized (drag the "//" corner grip) and have their text made smaller or larger (A- / A+ step through Kenshi's own four
// standard font sizes). Lists scroll. Backgrounds can be switched off (setting). The layout is the player's own and is kept in
// window-layout.json in the mod's folder (WindowLayout), never in the world or the save.
using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace RadiantQuests;

public sealed class GuildUi : IUiExtension
{
    public string Id => "radiantquests";

    // What the window shows (interface state only, never the world's).
    static bool s_open;
    static CharacterId s_clerk;
    static int s_site = -1;
    static int s_rankTab;
    static string s_tab = "general";
    /// <summary>The quest journal (key G, or the tracker's title): every quest you hold, with its details, from anywhere.</summary>
    static bool s_journal;
    /// <summary>A clerk your characters were sent walking to (the menu row chosen from afar): its window opens when they arrive.</summary>
    static CharacterId s_walkingTo;
    static long s_walkUntil;

    static readonly List<(string Text, long Until)> s_toasts = [];

    string _close = "", _tab = "", _rank = "", _accept = "", _handIn = "", _abandon = "", _reroll = "", _journal = "", _rumour = "", _hide = "", _pickOpen = "", _pickToggle = "", _pickGive = "", _pickCancel = "", _cellPick = "", _cellOk = "", _cellOn = "", _cellBad = "", _cellMark = "";

    /// <summary>The pick window's ticked lines (keys), for the quest it was opened for; a quest id whose window was closed here.</summary>
    static readonly HashSet<int> s_picks = [];
    static int s_pickQuest = -1, s_pickClosed = -1;
    string _move = "", _resize = "", _scale = "", _hint = "";
    /// <summary>A hint shown beside the pointer while it is over a control that has one (Meitou has no tooltips for mod controls).</summary>
    static (string Text, Vector2 At)? s_hint;
    string _window = "", _row = "", _toast = "", _tracker = "", _grip = "";
    /// <summary>Per font step (WindowLayout.Fonts): text, dim text, titles (one step bigger), buttons, pressed buttons.</summary>
    readonly string[] _text = new string[4], _dim = new string[4], _title = new string[4], _button = new string[4], _buttonOn = new string[4],
        _buttonGrey = new string[4];

    public void Register(UiRegistration r)
    {
        var gold = new Vector4(0.93f, 0.80f, 0.45f, 1);
        _window = r.RegisterStyle("window", new UiStyle { Background = new(0.07f, 0.065f, 0.055f, 0.95f) });
        _row = r.RegisterStyle("row", new UiStyle { Background = new(1, 1, 1, 0.04f) });
        // The inventory cells of a hand-in (his idea, 2026-11-25): gold tint = counts (click to pick), bright gold = picked, red = doesn't count.
        _cellOk = r.RegisterStyle("cell-ok", new UiStyle { Background = new(0.95f, 0.75f, 0.2f, 0.22f), Hover = new(0.95f, 0.75f, 0.2f, 0.4f) });
        _cellOn = r.RegisterStyle("cell-on", new UiStyle { Background = new(0.95f, 0.75f, 0.2f, 0.55f), Hover = new(0.95f, 0.75f, 0.2f, 0.65f),
            Text = new(0.1f, 0.08f, 0.02f, 1) });
        _cellBad = r.RegisterStyle("cell-bad", new UiStyle { Background = new(0.85f, 0.2f, 0.15f, 0.28f), Hover = new(0.85f, 0.2f, 0.15f, 0.42f) });
        _cellMark = r.RegisterStyle("cell-mark", new UiStyle { Text = new(0.1f, 0.08f, 0.02f, 1) });
        for (int i = 0; i < WindowLayout.Fonts.Length; i++)
        {
            string font = WindowLayout.Fonts[i], bigger = WindowLayout.Fonts[Math.Min(i + 1, WindowLayout.Fonts.Length - 1)];
            _text[i] = r.RegisterStyle($"text{i}", new UiStyle { FontName = font });
            _dim[i] = r.RegisterStyle($"dim{i}", new UiStyle { FontName = font, Text = new(0.65f, 0.63f, 0.58f, 1) });
            _title[i] = r.RegisterStyle($"title{i}", new UiStyle { FontName = bigger, Text = gold });
            _button[i] = r.RegisterStyle($"button{i}", new UiStyle
            {
                FontName = font,
                Background = new(0.24f, 0.21f, 0.17f, 1),
                Hover = new(0.38f, 0.32f, 0.22f, 1),
                Active = new(0.48f, 0.40f, 0.26f, 1),
                Disabled = new(0.13f, 0.12f, 0.11f, 1),
                TextAlign = "Center",
            });
            _buttonGrey[i] = r.RegisterStyle($"button-grey{i}", new UiStyle
            {
                FontName = font,
                Background = new(0.13f, 0.12f, 0.11f, 1),
                Hover = new(0.16f, 0.15f, 0.13f, 1),
                Text = new(0.45f, 0.43f, 0.40f, 1),
                TextAlign = "Center",
            });
            _buttonOn[i] = r.RegisterStyle($"button-on{i}", new UiStyle
            {
                FontName = font,
                Background = new(0.55f, 0.44f, 0.22f, 1),
                Hover = new(0.62f, 0.50f, 0.26f, 1),
                Text = new(1, 0.97f, 0.88f, 1),
                TextAlign = "Center",
            });
        }
        _grip = r.RegisterStyle("grip", new UiStyle
        {
            FontName = WindowLayout.Fonts[0],
            Background = new(1, 1, 1, 0.08f),
            Hover = new(0.62f, 0.50f, 0.26f, 0.8f),
            Text = new(0.75f, 0.73f, 0.68f, 1),
            TextAlign = "Center",
        });
        _toast = r.RegisterStyle("toast", new UiStyle { Background = new(0, 0, 0, 0.72f), Text = gold, TextAlign = "Center" });
        _tracker = r.RegisterStyle("tracker", new UiStyle { Background = new(0, 0, 0, 0.55f) });

        _close = r.RegisterAction("close", _ => s_open = false);
        _tab = r.RegisterAction("tab", a => s_tab = a.Argument);
        _rank = r.RegisterAction("rank", a => s_rankTab = int.TryParse(a.Argument, out int v) ? Math.Clamp(v, 0, Ranks.Top) : 0);
        _journal = r.RegisterAction("journal", _ => ToggleJournal());
        // A greyed-out control's reason: shown beside the pointer while over it, and as a message when clicked.
        _hint = r.RegisterAction("hint", a =>
        {
            if (a.Kind == UiEventKind.Enter) s_hint = (a.Argument, a.Position);
            else if (a.Kind == UiEventKind.Leave) s_hint = null;
            else if (a.Kind == UiEventKind.Press) RadiantQuestsMod.Toast(a.Argument);
        });
        _accept = r.RegisterAction("accept", a =>
        {
            if (int.TryParse(a.Argument, out int slot)) Send(new AcceptOffer(s_clerk, s_site, slot, s_tab == "faction") { Tick = Tick });
        });
        _reroll = r.RegisterAction("reroll", _ => Send(new RerollBoard(s_clerk) { Tick = Tick }));
        _handIn = r.RegisterAction("handin", a =>
        {
            if (int.TryParse(a.Argument, out int id)) Send(new HandIn(s_clerk, id) { Tick = Tick });
        });
        _rumour = r.RegisterAction("rumour", a =>
        {
            if (int.TryParse(a.Argument, out int id)) Send(new BuyRumour(s_clerk, id) { Tick = Tick });
        });
        _pickOpen = r.RegisterAction("pickopen", a =>
        {
            if (!int.TryParse(a.Argument, out int id)) return;
            s_picks.Clear();
            s_pickQuest = id;
            s_pickClosed = -1;
            Send(new ShowHandIn(s_clerk, id) { Tick = Tick });
        });
        _pickToggle = r.RegisterAction("picktoggle", a =>
        {
            if (!int.TryParse(a.Argument, out int key)) return;
            if (!s_picks.Remove(key)) s_picks.Add(key);
        });
        _pickGive = r.RegisterAction("pickgive", _ =>
        {
            if (s_pickQuest >= 0) Send(new HandInPicked(s_clerk, s_pickQuest, [.. s_picks.Order()]) { Tick = Tick });
        });
        _pickCancel = r.RegisterAction("pickcancel", _ =>
        {
            s_pickClosed = s_pickQuest;
            s_picks.Clear();
        });
        // A click on a marked inventory cell picks or unpicks its line; hovering a red one shows why it doesn't count.
        _cellPick = r.RegisterAction("cellpick", a =>
        {
            if (a.Kind == UiEventKind.Enter && a.Argument.StartsWith('!')) s_hint = (a.Argument[1..], a.Position);
            else if (a.Kind == UiEventKind.Leave) s_hint = null;
            else if (a.Kind == UiEventKind.Press && int.TryParse(a.Argument, out int key) && !s_picks.Remove(key)) s_picks.Add(key);
        });
        foreach (var screen in new[] { "inventory", "bag" })
            r.AddScreenControls(screen, UiScreens.ItemCells, UiAnchorPlacement.Inside, Cell);
        _hide = r.RegisterAction("hide", a =>
        {
            if (int.TryParse(a.Argument, out int id)) Send(new ToggleMarkers(id) { Tick = Tick });
        });
        _abandon = r.RegisterAction("abandon", a =>
        {
            if (int.TryParse(a.Argument, out int id)) Send(new Abandon(id) { Tick = Tick });
        });
        // Moving and resizing (argument: the window's name), and text size ("tracker+" / "tracker-"). Kept on letting go.
        _move = r.RegisterAction("move", a => Pointer(a, (w, d) => { w.X += d.X; w.Y += d.Y; }));
        _resize = r.RegisterAction("resize", a => Pointer(a, (w, d) => { w.W += d.X; w.H += d.Y; }));
        _scale = r.RegisterAction("scale", a =>
        {
            if (a.Argument.Length < 2 || a.Kind != UiEventKind.Press) return;
            var w = WindowLayout.Get(a.Argument[..^1]);
            w.Font = Math.Clamp(w.Font + (a.Argument[^1] == '+' ? 1 : -1), 0, WindowLayout.Fonts.Length - 1);
            WindowLayout.Save();
        });

        r.AddCharacterMenuVerbs(Verbs);
        r.AddDialogueReplies(Replies);
        r.AddMapMarkers(Markers);
        r.Contribute(UiSlots.Overlay, Overlay, order: 50);
        // No button of its own on the HUD (his rule, 2026-11-23: many mods adding buttons would clutter it): the journal opens with its key
        // (G) or from the quest tracker's title, which shows only while you hold quests.
    }

    /// <summary>A drag on a title bar or grip changes the window; letting go keeps the layout.</summary>
    static void Pointer(UiActionContext a, Action<WindowLayout.Window, Vector2> change)
    {
        var w = WindowLayout.Get(a.Argument);
        if (a.Kind == UiEventKind.Drag) change(w, a.Delta);
        else if (a.Kind == UiEventKind.Release) WindowLayout.Save();
    }

    static long Tick => RadiantQuestsMod.Context?.Tick ?? 0;

    static void Send(SimCommand c) => RadiantQuestsMod.Context?.Enqueue(c);

    /// <summary>Opens the window at <paramref name="clerk"/> (the menu row; the test channel).</summary>
    internal static void OpenAt(ClerkInfo clerk)
    {
        s_open = true;
        s_journal = false;
        s_walkingTo = default;
        s_clerk = clerk.Id;
        s_site = clerk.Site;
        s_tab = "general";
        s_rankTab = GuildSystem.View?.Rank ?? 0;
        Send(new OpenBoard(clerk.Id) { Tick = Tick });
    }

    internal static void Close() => s_open = false;

    /// <summary>Opens or closes the quest journal (the key, the tracker's title).</summary>
    internal static void ToggleJournal() => s_journal = !s_journal;

    internal static bool IsOpen => s_open;

    /// <summary>
    /// "Adventurers' Guild" on every guild clerk, at any distance, acting as talking to him (his rule, 2026-11-24): the game's own Talk order
    /// for the first selected character, so it walks up as for any conversation (counters included); when the conversation opens, the guild
    /// window opens over it (DialogueReplies).
    /// </summary>
    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext m)
    {
        if (GuildSystem.View is not { } view || !view.Clerks.TryGetValue(m.Target.Id, out var clerk)) return [];
        var selected = m.Selected.ToList();
        return [new UiMenuVerb("Adventurers' Guild", () =>
        {
            if (selected.Count == 0)
            {
                RadiantQuestsMod.Toast("Select a character to talk to the guild clerk.");
                return;
            }
            m.Enqueue(new Meitou.Simulation.Dialogue.TalkOrder(selected[0], clerk.Id) { Tick = m.Tick });
            s_walkingTo = clerk.Id;
            s_walkUntil = Environment.TickCount64 + 5 * 60 * 1000;
        })];
    }

    /// <summary>
    /// In a conversation with a guild clerk (his request, 2026-11-24): a reply line that ends the conversation (Meitou's Leave, as closing the
    /// talk window does) and opens the guild window in its place; and when the talk came from the menu row, the same happens by itself as the
    /// conversation starts.
    /// </summary>
    static IEnumerable<UiDialogueReply> Replies(UiDialogueContext d)
    {
        if (GuildSystem.View is not { } view || !view.Clerks.TryGetValue(d.Conversation.Npc, out var clerk)) yield break;
        if (s_walkingTo == clerk.Id && !s_open)
        {
            s_walkingTo = default;
            d.Leave();
            OpenAt(clerk);
            yield break;
        }
        yield return new UiDialogueReply("I'm looking for guild work. (Adventurers' Guild)", () =>
        {
            d.Leave();
            OpenAt(clerk);
        });
    }

    /// <summary>The nearest groups of each hunt and kill quest's target, red, with the quest as label and tooltip.</summary>
    static IEnumerable<UiMapMarker> Markers(UiMapContext m)
    {
        if (!RadiantQuestsMod.Settings.MapMarkers || GuildSystem.View is not { } view) yield break;
        var colour = new Vector4(0.95f, 0.30f, 0.20f, 1);
        foreach (var q in view.Quests.Where(q => !q.MarkersHidden))
            foreach (var at in q.Targets)
                yield return new UiMapMarker(at, "Character", colour, q.Text, $"Guild quest\t{q.Text}\nProgress\t{q.Have}/{q.Need}", 0, 1.2f);
    }

    #region Overlay

    UiNode Overlay(UiFrameContext f)
    {
        try
        {
            TestChannel.Poll();
            while (RadiantQuestsMod.NextToast() is { } t)
            {
                s_toasts.Add((t, Environment.TickCount64 + 6000));
                if (s_toasts.Count > 4) s_toasts.RemoveAt(0);
            }
            s_toasts.RemoveAll(t => t.Until < Environment.TickCount64);

            var root = new UiNode("rq-root") { Placement = UiPlacement.Fill, Pickable = false };
            var view = GuildSystem.View;
            // Sent to talk to a clerk from the menu row: given up after a few minutes (another order, or the talk never started).
            if (s_walkingTo != default && Environment.TickCount64 > s_walkUntil) s_walkingTo = default;
            if (view is not null && RadiantQuestsMod.Settings.Tracker && view.Quests.Count > 0 && !s_open && !s_journal) root.Children.Add(Tracker(f, view));
            if (s_open && view is not null)
            {
                if (!view.Clerks.ContainsKey(s_clerk)) s_open = false;
                else
                {
                    root.Children.Add(Window(f, view));
                    if (PickShown(view)) root.Children.Add(PickWindow(f, view));
                }
            }
            else if (s_journal && view is not null) root.Children.Add(Journal(f, view));
            if (s_toasts.Count > 0) root.Children.Add(Toasts());
            if (s_hint is { } hint && (s_open || s_journal))
                root.Children.Add(new UiNode("rq-hint", UiNodeKind.Label)
                {
                    Placement = new UiPlacement(Math.Min(620, 14 + hint.Text.Length * 8), 30, Left: hint.At.X + 18, Top: hint.At.Y + 20),
                    Style = _toast,
                    Text = hint.Text,
                    Pickable = false,
                });
            else if (!s_open && !s_journal) s_hint = null;
            return root;
        }
        catch (Exception e)
        {
            RadiantQuestsMod.LogException("guild window", e);
            return new UiNode("rq-root") { Placement = UiPlacement.Fill, Pickable = false };
        }
    }

    UiNode Toasts()
    {
        var box = new UiNode("rq-toasts")
        {
            Placement = new UiPlacement(1000, s_toasts.Count * 28, Left: new UiLength(-500, 0.5f), Top: 90),
            Flow = UiFlow.Column,
            Pickable = false,
        };
        int i = 0;
        foreach (var (text, _) in s_toasts)
            box.Children.Add(new UiNode($"rq-toast{i++}", UiNodeKind.Label)
            {
                Placement = new UiPlacement(UiLength.Fill, 28),
                Style = _toast,
                Text = text,
            });
        return box;
    }

    /// <summary>The background of a window (none when the player switched backgrounds off).</summary>
    string Back(string style) => RadiantQuestsMod.Settings.WindowBackgrounds ? style : "";

    /// <summary>
    /// The quest tracker: a "::" grip to move it, the title (opens the journal), A- / A+, then as many quests as fit its height (the rest
    /// counted), and a corner grip to resize it. Only its bars take the pointer, so it never blocks the world under it.
    /// </summary>
    UiNode Tracker(UiFrameContext f, GuildView view)
    {
        var win = WindowLayout.Place("tracker", f.Viewport, 230, 100);
        int lv = win.Font;
        float line = 22 * win.Scale;
        var wrap = new UiNode("rq-tracker-wrap") { Placement = new UiPlacement(win.W, win.H, Left: win.X, Top: win.Y), Pickable = false };
        var box = new UiNode("rq-tracker")
        {
            Placement = UiPlacement.Fill,
            Style = Back(_tracker),
            Flow = UiFlow.Column,
            Padding = 6,
            Pickable = false,
        };
        float headH = Math.Max(26, line + 4);
        var head = Row("rq-tr-head", headH);
        head.Children.Add(Grip("rq-tr-move", "::", _move, "tracker", 26, headH, drag: true));
        head.Children.Add(new UiNode("rq-tr-h", UiNodeKind.Button)
        {
            Placement = new UiPlacement(new UiLength(-26 - 2 * 32 - 3 * 6, 1), headH),
            Style = _title[lv],
            Text = $"Guild quests ({Ranks.Name(view.Rank)})",
            Action = _journal,
        });
        head.Children.Add(Grip("rq-tr-s-", "A-", _scale, "tracker-", 32, headH));
        head.Children.Add(Grip("rq-tr-s+", "A+", _scale, "tracker+", 32, headH));
        box.Children.Add(head);
        int fits = Math.Max(1, (int)((win.H - 12 - headH - 18) / line));
        int rows = Math.Min(view.Quests.Count, view.Quests.Count > fits ? fits - 1 : fits);
        foreach (var q in view.Quests.Take(rows))
            box.Children.Add(Label($"rq-tr{q.Id}", $"{q.Text}  {q.Have}/{q.Need}  {(q.Done ? "hand in" : Left(q.HoursLeft))}", q.Done ? _title[Math.Max(0, lv - 1)] : _text[lv], line));
        if (view.Quests.Count > rows) box.Children.Add(Label("rq-tr-more", $"...and {view.Quests.Count - rows} more (journal: G)", _dim[lv], line));
        wrap.Children.Add(box);
        // The corner grip, pinned to the bottom right.
        wrap.Children.Add(Grip("rq-tr-size", "//", _resize, "tracker", 18, 18, drag: true, at: new UiPlacement(18, 18, Right: 2, Bottom: 2)));
        return wrap;
    }

    /// <summary>A window's frame: a title bar to drag (with A- / A+ and Close), the body, and a corner grip to resize.</summary>
    UiNode Frame(string id, string name, WindowLayout.Window win, string title, string closeAction, out UiNode body)
    {
        int lv = win.Font;
        float headH = Math.Max(32, 30 * win.Scale);
        var w = new UiNode(id)
        {
            Placement = new UiPlacement(win.W, win.H, Left: win.X, Top: win.Y),
            Style = Back(_window),
            Flow = UiFlow.Column,
            Padding = 12,
            Gap = 6,
        };
        // The title bar moves the window (its label doesn't take the pointer, so a press on it goes to the bar).
        var head = new UiNode(id + "-head")
        {
            Placement = new UiPlacement(UiLength.Fill, headH),
            Flow = UiFlow.Row,
            Gap = 6,
            Style = _row,
            Action = _move,
            Argument = name,
            Pointer = UiPointerEvents.Drag | UiPointerEvents.Release,
        };
        head.Children.Add(Label(id + "-title", title, _title[lv], headH, new UiLength(-(100 + 2 * 40 + 3 * 6), 1)));
        head.Children.Add(Button(id + "-s-", "A-", _scale, name + "-", true, 40, lv, height: headH));
        head.Children.Add(Button(id + "-s+", "A+", _scale, name + "+", true, 40, lv, height: headH));
        head.Children.Add(Button(id + "-close", "Close", closeAction, "", true, 100, lv, height: headH));
        w.Children.Add(head);
        body = new UiNode(id + "-body") { Placement = new UiPlacement(UiLength.Fill, new UiLength(-headH - 6 - 6 - 18, 1)), Flow = UiFlow.Column, Gap = 6 };
        w.Children.Add(body);
        var foot = Row(id + "-foot", 18);
        foot.Children.Add(new UiNode(id + "-gap", UiNodeKind.Panel) { Placement = new UiPlacement(new UiLength(-24, 1), 18), Pickable = false });
        foot.Children.Add(Grip(id + "-size", "//", _resize, name, 18, 18, drag: true));
        w.Children.Add(foot);
        return w;
    }

    UiNode Window(UiFrameContext f, GuildView view)
    {
        view.Clerks.TryGetValue(s_clerk, out var clerk);
        var win = WindowLayout.Place("guild", f.Viewport, 640, 420);
        int lv = win.Font;
        float s = win.Scale, line = Math.Max(30, 30 * s), small = 24 * s;
        var w = Frame("rq-window", "guild", win,
            $"Adventurers' Guild  -  {clerk?.FactionName}{(clerk is { TownName.Length: > 0 } ? ", " + clerk.TownName : "")}", _close, out var body);

        string next = view.NextRankAt < 0 ? "top rank" : $"next rank at {view.NextRankAt}";
        body.Children.Add(Label("rq-rep", $"Guild rank {Ranks.Name(view.Rank)}   ·   Rep to spend {view.Balance}   ·   Lifetime rep {view.Lifetime} ({next})",
            _text[lv], small));

        // The faction tab ranks by your points with the clerk's faction.
        // A faction's own work opens at a guild rank (his rule, 2026-11-24): before it, its tab is greyed out with the reason on hover; its
        // rank buttons are only those its work comes at (C up by default).
        int factionFrom = Math.Clamp(RadiantQuestsMod.Settings.FactionFromRank, 0, Ranks.Top);
        bool factionOpen = view.Rank >= factionFrom;
        if (!factionOpen && s_tab == "faction") s_tab = "general";
        bool factionTab = s_tab == "faction" && RadiantQuestsMod.Settings.FactionQuests;
        int firstRank = factionTab ? factionFrom : 0;
        if (s_rankTab < firstRank) s_rankTab = firstRank;
        var standing = clerk is not null && view.Factions.TryGetValue(clerk.Faction, out var st) ? st : new FactionStanding(0, 0, RadiantQuestsMod.Settings.RankC);
        if (factionTab)
            body.Children.Add(Label("rq-frep", $"{clerk?.FactionName}: rank {Ranks.Name(standing.Rank)}   ·   {standing.Points} points"
                + (standing.NextRankAt < 0 ? " (top rank)" : $" (next rank at {standing.NextRankAt})"), _text[lv], small));

        var tabs = Row("rq-tabs", line);
        tabs.Children.Add(Button("rq-tab-g", "General", _tab, "general", true, 150 * s, lv, !factionTab, line));
        if (RadiantQuestsMod.Settings.FactionQuests)
            tabs.Children.Add(factionOpen
                ? Button("rq-tab-f", $"{clerk?.FactionName} work", _tab, "faction", true, 260 * s, lv, factionTab, line)
                : new UiNode("rq-tab-f", UiNodeKind.Button)
                {
                    Placement = new UiPlacement(260 * s, line),
                    Style = _buttonGrey[lv],
                    Text = $"{clerk?.FactionName} work",
                    Action = _hint,
                    Argument = $"Rank up first: {clerk?.FactionName} gives its own work from guild rank {Ranks.Name(factionFrom)} (you are {Ranks.Name(view.Rank)}).",
                    Pointer = UiPointerEvents.Hover,
                });
        if (view.Boards.TryGetValue(s_site, out var bv))
            tabs.Children.Add(Button("rq-reroll", $"New quests ({bv.RerollCost} rep)", _reroll, "", bv.RerollCost <= view.Balance, 220 * s, lv, false, line));
        body.Children.Add(tabs);

        // Rank boards.
        view.Boards.TryGetValue(s_site, out var board);
        var tabOffers = factionTab ? board?.FactionOffers : board?.Offers;
        int myRank = factionTab ? standing.Rank : view.Rank;
        var ranks = Row("rq-ranks", line);
        for (int b = firstRank; b <= Ranks.Top; b++)
        {
            int n = tabOffers?.Count(o => o.Rank == b && !o.Taken) ?? 0;
            string caption = $"{Ranks.Name(b)} ({n})" + (b > myRank ? " locked" : "");
            ranks.Children.Add(Button($"rq-rank{b}", caption, _rank, b.ToString(), true, 110 * s, lv, s_rankTab == b, line));
        }
        body.Children.Add(ranks);

        // The two lists share what is left of the window's height and scroll.
        float headH = Math.Max(32, 30 * s);
        float fixedH = (small + 6) * (factionTab ? 2 : 1) + 2 * (line + 6) + (28 * s + 6);
        float listH = Math.Max(60, (win.H - 12 * 2 - headH - 6 - 18 - 6 - fixedH - 6) / 2);

        // The selected board's quests (taken ones leave the board).
        var offers = tabOffers?.Where(o => o.Rank == s_rankTab && !o.Taken).ToList() ?? [];
        var list = Scroll("rq-offers", listH, line);
        // (Faction offers below its first rank, from a board made before this rule, are never shown.)
        if (factionTab) offers = [.. offers.Where(o => o.Rank >= factionFrom)];
        if (board is null) list.Children.Add(Label("rq-wait", "The clerk is fetching today's board...", _dim[lv], line));
        else if (offers.Count == 0)
            list.Children.Add(Label("rq-none", s_rankTab > myRank
                ? $"No rank {Ranks.Name(s_rankTab)} work today. You need rank {Ranks.Name(s_rankTab)} for these."
                : $"No rank {Ranks.Name(s_rankTab)} work today. The board changes every day.", _dim[lv], line));
        foreach (var o in offers)
        {
            var row = Row($"rq-o{o.Slot}", line, _row);
            string pay = o.Rep > 0 ? $"{o.Cats} cats, {o.Rep} rep" + (factionTab ? $" + {o.Rep} points" + (o.Relation > 0 ? $", relations +{o.Relation}" : "") : "") : $"{o.Cats} cats, no rep (below your rank)";
            string band = o.Band.Length > 0 ? $"{o.Band}:  " : "";
            row.Children.Add(Label($"rq-ot{o.Slot}", $"  {band}{o.Text}   ·   {pay}", _text[lv], line, new UiLength(-130 * s, 1)));
            row.Children.Add(Button($"rq-oa{o.Slot}", o.Locked ? "Locked" : "Accept", _accept, o.Slot.ToString(), !o.Locked, 120 * s, lv, false, line));
            list.Children.Add(row);
        }
        body.Children.Add(list);

        // Your quests.
        body.Children.Add(Label("rq-mine-h", $"Your quests ({view.Quests.Count})", _title[lv], 28 * s));
        var mine = Scroll("rq-mine", listH, line);
        if (view.Quests.Count == 0) mine.Children.Add(Label("rq-mine-none", "None yet. Accept one above.", _dim[lv], line));
        foreach (var q in view.Quests)
        {
            var row = Row($"rq-q{q.Id}", line, _row);
            // Handed in where it was taken (his rule); a courier at its destination.
            bool here = q.Kind == ItemQuestKinds.Courier || q.Site == s_site;
            string state = q.Done ? (here ? "done, hand it in" : $"done, hand it in at {q.Town}") : $"{q.Have}/{q.Need}, {Left(q.HoursLeft)}"
                + (here ? "" : $", taken in {q.Town}");
            row.Children.Add(Label($"rq-qt{q.Id}", $"  {RankBand(q.Rank, q.Band)}  {q.Text}   ·   {state}   ·   {q.Cats} cats, {q.Rep} rep",
                q.Done ? _title[Math.Max(0, lv - 1)] : _text[lv], line, new UiLength(-(q.RumourCost > 0 ? 440 : 250) * s, 1)));
            // A better rumour of where to get the goods, for cats (the clerk's; read again in the journal).
            if (q.RumourCost > 0)
                row.Children.Add(Button($"rq-qr{q.Id}", $"Ask more ({q.RumourCost}c)", _rumour, q.Id.ToString(), true, 184 * s, lv, false, line));
            bool goods = q.Kind is ItemQuestKinds.Supply or ItemQuestKinds.Gear or ItemQuestKinds.Find;
            row.Children.Add(Button($"rq-qh{q.Id}", "Hand in", goods ? _pickOpen : _handIn, q.Id.ToString(), q.Done && here, 120 * s, lv, false, line));
            row.Children.Add(Button($"rq-qx{q.Id}", "Abandon", _abandon, q.Id.ToString(), true, 120 * s, lv, false, line));
            mine.Children.Add(row);
        }
        body.Children.Add(mine);

        return w;
    }

    /// <summary>
    /// The mark on one inventory cell while a hand-in's goods are being picked (his idea, 2026-11-25: pick them in the inventory itself):
    /// nothing for items that don't matter; gold for one that counts (a click picks it, again unpicks), bright gold with a check once picked; red
    /// for one that matches but can't go (worn, stolen from this faction, worth too little: hover for why).
    /// </summary>
    UiNode? Cell(UiScreenContext ctx)
    {
        if (!s_open || ctx.Item is not { } cell || GuildSystem.View is not { } view || !PickShown(view)) return null;
        var choice = view.HandIn!.Choices.FirstOrDefault(c => c.Who == cell.Owner.Character && c.Cells.Contains(cell.Item.Index));
        if (choice is null) return null;
        bool ok = choice.Problem.Length == 0, on = ok && s_picks.Contains(choice.Key);
        var node = new UiNode($"rq-cell-{choice.Key}-{cell.Item.Index}")
        {
            Style = !ok ? _cellBad : on ? _cellOn : _cellOk,
            Action = _cellPick,
            Argument = ok ? choice.Key.ToString() : "!" + choice.Problem,
        };
        if (on) node.Children.Add(new UiNode($"rq-cellv-{choice.Key}-{cell.Item.Index}", UiNodeKind.Label) { Text = "x", Style = _cellMark });
        return node;
    }

    /// <summary>Whether the pick window is open now: its list came for the quest it was opened for, and it was not closed here.</summary>
    static bool PickShown(GuildView view) => view.HandIn is { } hv && hv.QuestId == s_pickQuest && s_pickClosed != hv.QuestId
        && view.Quests.Any(q => q.Id == hv.QuestId);

    /// <summary>
    /// The pick window (his rule, 2026-11-24): its own window beside the clerk's (his report: drawn inside the clerk window it fell below the
    /// window's edge and never showed). Choose which goods go; worn ones and ones that don't count say why.
    /// </summary>
    UiNode PickWindow(UiFrameContext f, GuildView view)
    {
        var hv = view.HandIn!;
        var win = WindowLayout.Place("pick", f.Viewport, 520, 240);
        int lv = win.Font;
        float s = win.Scale, line = Math.Max(30, 30 * s), headH = Math.Max(32, 30 * s);
        var w = Frame("rq-pick-win", "pick", win, "Hand in goods", _pickCancel, out var body);
        // The pick window (his rule, 2026-11-24): choose which goods go; worn ones and ones that don't count say why.
        {
            int units = hv.Choices.Where(c => s_picks.Contains(c.Key) && c.Problem.Length == 0).Sum(c => c.Units);
            var head = Row("rq-pick-h", line);
            head.Children.Add(Label("rq-pick-t", $"Hand in: {hv.Quest}   ·   picked {units} of {hv.Need}", _title[lv], line, new UiLength(-160 * s, 1)));
            head.Children.Add(Button("rq-pick-give", "Hand in picked", _pickGive, "", units >= hv.Need, 150 * s, lv, false, line));
            body.Children.Add(head);
            body.Children.Add(Label("rq-pick-help", "Click the gold items in your inventory (or tick them below). Red ones can't go: point at them to see why.",
                _dim[lv], Math.Max(24, 24 * s)));
            var picks = Scroll("rq-pick", Math.Max(line * 2, win.H - 12 * 2 - headH - line - 18), line);
            if (hv.Choices.Count == 0) picks.Children.Add(Label("rq-pick-none", "Nothing that fits is carried by your characters at the clerk.", _dim[lv], line));
            foreach (var c in hv.Choices)
            {
                var prow = Row($"rq-pk{c.Key}", line, _row);
                bool ok = c.Problem.Length == 0, on = ok && s_picks.Contains(c.Key);
                prow.Children.Add(Button($"rq-pkb{c.Key}", on ? "[x]" : "[ ]", _pickToggle, c.Key.ToString(), ok, 60 * s, lv, on, line));
                string count = c.Units > 1 ? $"{c.Units} x " : "";
                prow.Children.Add(Label($"rq-pkt{c.Key}", $"  {count}{c.Name}   ·   {c.Holder}   ·   worth {c.Worth:N0}" + (ok ? "" : $"   ·   {c.Problem}"),
                    ok ? _text[lv] : _dim[lv], line));
                picks.Children.Add(prow);
            }
            body.Children.Add(picks);
        }
        return w;
    }

    /// <summary>
    /// The quest journal (key G, or click the tracker's title): every quest you hold, two lines each (what, how far, time; for whom, where,
    /// pay), with Abandon, in a list that scrolls. Handing in still needs a clerk.
    /// </summary>
    UiNode Journal(UiFrameContext f, GuildView view)
    {
        var win = WindowLayout.Place("journal", f.Viewport, 560, 300);
        int lv = win.Font;
        float s = win.Scale, line = Math.Max(30, 30 * s), small = 26 * s;
        var w = Frame("rq-journal", "journal", win, $"Guild quests  -  rank {Ranks.Name(view.Rank)}   ·   {view.Quests.Count} taken", _journal, out var body);
        float headH = Math.Max(32, 30 * s);
        var list = Scroll("rq-j-list", Math.Max(60, win.H - 12 * 2 - headH - 6 - 18 - 6), line + small + 12);
        if (view.Quests.Count == 0) list.Children.Add(Label("rq-j-none", "No quests. Talk to a bartender (right click > Adventurers' Guild).", _dim[lv], line));
        foreach (var q in view.Quests)
        {
            bool rumour = q.Rumour.Length > 0 && !q.Done;
            var box = new UiNode($"rq-j{q.Id}") { Placement = new UiPlacement(UiLength.Fill, line + small * (rumour ? 2 : 1) + 10), Flow = UiFlow.Column, Style = _row, Padding = 4 };
            string state = q.Done ? "done: hand it in at a guild clerk" : $"{q.Have}/{q.Need}   ·   {Left(q.HoursLeft)}";
            var top = Row($"rq-j{q.Id}a", line);
            bool marked = q.Targets.Count > 0 && !q.Done;
            top.Children.Add(Label($"rq-j{q.Id}t", $"  {RankBand(q.Rank, q.Band)}   {q.Text}   ·   {state}", q.Done ? _title[Math.Max(0, lv - 1)] : _text[lv], line,
                new UiLength(-(marked ? 256 : 130) * s, 1)));
            if (marked)
                top.Children.Add(Button($"rq-j{q.Id}h", q.MarkersHidden ? "Show" : "Hide", _hide, q.Id.ToString(), true, 120 * s, lv, q.MarkersHidden, line));
            top.Children.Add(Button($"rq-j{q.Id}x", "Abandon", _abandon, q.Id.ToString(), true, 120 * s, lv, false, line));
            box.Children.Add(top);
            string who = q.General ? $"Guild work, taken in {q.Town}" : $"For {q.GiverName}, taken in {q.Town}";
            string pay = $"{q.Cats} cats, {q.Rep} rep" + (q.General ? "" : $" + {q.Rep} points" + (q.Relation > 0 ? $", relations +{q.Relation}" : ""));
            bool items = q.Kind is ItemQuestKinds.Supply or ItemQuestKinds.Gear or ItemQuestKinds.Find;
            string where = q.Done || items ? "" : q.Targets.Count > 0 ? (q.MarkersHidden ? "   ·   markers hidden" : "   ·   marked on the map")
                : "   ·   none found nearby (abandoning is free)";
            box.Children.Add(Label($"rq-j{q.Id}d", $"      {who}   ·   {pay}{where}", _dim[lv], small));
            if (rumour) box.Children.Add(Label($"rq-j{q.Id}r", $"      Rumour: {q.Rumour}" + (q.RumourCost > 0 ? "  (a clerk can tell you more)" : ""), _dim[lv], small));
            list.Children.Add(box);
        }
        body.Children.Add(list);
        return w;
    }

    /// <summary>"D, Hard" (just the rank for an older quest with no difficulty).</summary>
    static string RankBand(int rank, string band) => band.Length > 0 ? $"{Ranks.Name(rank)}, {band}" : Ranks.Name(rank);

    #endregion

    #region Helpers

    static string Left(double hours)
    {
        if (double.IsInfinity(hours)) return "no time limit";
        if (hours <= 0) return "out of time";
        int h = (int)Math.Ceiling(hours);
        return h >= 24 ? $"{h / 24}d {h % 24}h left" : $"{h}h left";
    }

    static UiNode Row(string id, float height, string style = "") =>
        new(id) { Placement = new UiPlacement(UiLength.Fill, height), Flow = UiFlow.Row, Gap = 6, Style = style };

    static UiNode Scroll(string id, float height, float step) =>
        new(id, UiNodeKind.ScrollView) { Placement = new UiPlacement(UiLength.Fill, height), Flow = UiFlow.Column, Gap = 4, ScrollStep = Math.Max(8, step) };

    static UiNode Label(string id, string text, string style, float height, UiLength? width = null) => new(id, UiNodeKind.Label)
    {
        Placement = new UiPlacement(width ?? UiLength.Fill, height),
        Style = style,
        Text = text,
    };

    UiNode Button(string id, string text, string action, string argument, bool enabled, float width, int font, bool on = false, float height = 30) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new UiPlacement(width, height),
            Style = on ? _buttonOn[font] : _button[font],
            Text = text,
            Action = enabled ? action : "",
            Argument = argument,
            Enabled = enabled,
        };

    /// <summary>A small handle: drags (moving, resizing) with <paramref name="drag"/>, else a plain click (text size).</summary>
    UiNode Grip(string id, string text, string action, string argument, float width, float height, bool drag = false, UiPlacement? at = null) =>
        new(id, UiNodeKind.Button)
        {
            Placement = at ?? new UiPlacement(width, height),
            Style = _grip,
            Text = text,
            Action = action,
            Argument = argument,
            Pointer = drag ? UiPointerEvents.Drag | UiPointerEvents.Release : UiPointerEvents.None,
        };

    #endregion
}
