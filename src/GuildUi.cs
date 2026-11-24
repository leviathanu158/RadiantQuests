// Radiant Quests for Meitou. GPLv3 or later.
// The interface: the "Adventurers' Guild" row in a clerk's right-click menu, the guild window, the quest tracker and the guild's messages.
// Everything here runs on the interface's thread and only reads GuildSystem.View; a button queues a command for the world.
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

    static readonly List<(string Text, long Until)> s_toasts = [];

    string _close = "", _tab = "", _rank = "", _accept = "", _handIn = "", _abandon = "", _reroll = "";
    string _window = "", _title = "", _text = "", _dim = "", _button = "", _buttonOn = "", _row = "", _toast = "", _tracker = "";

    public void Register(UiRegistration r)
    {
        var gold = new Vector4(0.93f, 0.80f, 0.45f, 1);
        _window = r.RegisterStyle("window", new UiStyle { Background = new(0.07f, 0.065f, 0.055f, 0.95f) });
        _title = r.RegisterStyle("title", new UiStyle { Text = gold, TextScale = 1.25f });
        _text = r.RegisterStyle("text", new UiStyle());
        _dim = r.RegisterStyle("dim", new UiStyle { Text = new(0.65f, 0.63f, 0.58f, 1) });
        _row = r.RegisterStyle("row", new UiStyle { Background = new(1, 1, 1, 0.04f) });
        _button = r.RegisterStyle("button", new UiStyle
        {
            Background = new(0.24f, 0.21f, 0.17f, 1),
            Hover = new(0.38f, 0.32f, 0.22f, 1),
            Active = new(0.48f, 0.40f, 0.26f, 1),
            Disabled = new(0.13f, 0.12f, 0.11f, 1),
            TextAlign = "Center",
        });
        _buttonOn = r.RegisterStyle("button-on", new UiStyle
        {
            Background = new(0.55f, 0.44f, 0.22f, 1),
            Hover = new(0.62f, 0.50f, 0.26f, 1),
            Text = new(1, 0.97f, 0.88f, 1),
            TextAlign = "Center",
        });
        _toast = r.RegisterStyle("toast", new UiStyle { Background = new(0, 0, 0, 0.72f), Text = gold, TextAlign = "Center" });
        _tracker = r.RegisterStyle("tracker", new UiStyle { Background = new(0, 0, 0, 0.55f) });

        _close = r.RegisterAction("close", _ => s_open = false);
        _tab = r.RegisterAction("tab", a => s_tab = a.Argument);
        _rank = r.RegisterAction("rank", a => s_rankTab = int.TryParse(a.Argument, out int v) ? Math.Clamp(v, 0, Ranks.Top) : 0);
        _accept = r.RegisterAction("accept", a =>
        {
            if (int.TryParse(a.Argument, out int slot)) Send(new AcceptOffer(s_clerk, s_site, slot, s_tab == "faction") { Tick = Tick });
        });
        _reroll = r.RegisterAction("reroll", _ => Send(new RerollBoard(s_clerk) { Tick = Tick }));
        _handIn = r.RegisterAction("handin", a =>
        {
            if (int.TryParse(a.Argument, out int id)) Send(new HandIn(s_clerk, id) { Tick = Tick });
        });
        _abandon = r.RegisterAction("abandon", a =>
        {
            if (int.TryParse(a.Argument, out int id)) Send(new Abandon(id) { Tick = Tick });
        });

        r.AddCharacterMenuVerbs(Verbs);
        r.AddMapMarkers(Markers);
        r.Contribute(UiSlots.Overlay, Overlay, order: 50);
    }

    static long Tick => RadiantQuestsMod.Context?.Tick ?? 0;

    static void Send(SimCommand c) => RadiantQuestsMod.Context?.Enqueue(c);

    /// <summary>Opens the window at <paramref name="clerk"/> (the menu row; the test channel).</summary>
    internal static void OpenAt(ClerkInfo clerk)
    {
        s_open = true;
        s_clerk = clerk.Id;
        s_site = clerk.Site;
        s_tab = "general";
        s_rankTab = GuildSystem.View?.Rank ?? 0;
        Send(new OpenBoard(clerk.Id) { Tick = Tick });
    }

    internal static void Close() => s_open = false;

    internal static bool IsOpen => s_open;

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext m)
    {
        if (GuildSystem.View is not { } view || !view.Clerks.TryGetValue(m.Target.Id, out var clerk)) return [];
        return [new UiMenuVerb("Adventurers' Guild", () => OpenAt(clerk))];
    }

    /// <summary>The nearest groups of each hunt and kill quest's target, red, with the quest as label and tooltip.</summary>
    static IEnumerable<UiMapMarker> Markers(UiMapContext m)
    {
        if (!RadiantQuestsMod.Settings.MapMarkers || GuildSystem.View is not { } view) yield break;
        var colour = new Vector4(0.95f, 0.30f, 0.20f, 1);
        foreach (var q in view.Quests)
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
            if (view is not null && RadiantQuestsMod.Settings.Tracker && view.Quests.Count > 0 && !s_open) root.Children.Add(Tracker(view));
            if (s_open && view is not null)
            {
                if (!view.Clerks.ContainsKey(s_clerk)) s_open = false;
                else root.Children.Add(Window(f, view));
            }
            if (s_toasts.Count > 0) root.Children.Add(Toasts());
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
            Placement = new UiPlacement(620, s_toasts.Count * 28, Left: new UiLength(-310, 0.5f), Top: 90),
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

    UiNode Tracker(GuildView view)
    {
        int rows = Math.Min(view.Quests.Count, 8);
        var box = new UiNode("rq-tracker")
        {
            Placement = new UiPlacement(340, 30 + rows * 22, Right: 12, Top: new UiLength(0, 0.22f)),
            Style = _tracker,
            Flow = UiFlow.Column,
            Padding = 6,
            Pickable = false,
        };
        box.Children.Add(Label("rq-tr-h", $"Guild quests (rank {Ranks.Name(view.Rank)})", _title, 22));
        foreach (var q in view.Quests.Take(rows))
            box.Children.Add(Label($"rq-tr{q.Id}", $"{q.Text}  {q.Have}/{q.Need}  {(q.Done ? "hand in" : Left(q.HoursLeft))}", q.Done ? _title : _text, 22));
        return box;
    }

    UiNode Window(UiFrameContext f, GuildView view)
    {
        view.Clerks.TryGetValue(s_clerk, out var clerk);
        var w = new UiNode("rq-window")
        {
            Placement = new UiPlacement(840, 660, Left: new UiLength(-420, 0.5f), Top: new UiLength(-330, 0.5f)),
            Style = _window,
            Flow = UiFlow.Column,
            Padding = 14,
            Gap = 6,
        };

        // Title and close.
        var head = Row("rq-head", 30);
        head.Children.Add(Label("rq-title", $"Adventurers' Guild  -  {clerk?.FactionName}{(clerk is { TownName.Length: > 0 } ? ", " + clerk.TownName : "")}",
            _title, 30, new UiLength(-110, 1)));
        head.Children.Add(Button("rq-close", "Close", _close, "", true, 100));
        w.Children.Add(head);

        string next = view.NextRankAt < 0 ? "top rank" : $"next rank at {view.NextRankAt}";
        w.Children.Add(Label("rq-rep", $"Guild rank {Ranks.Name(view.Rank)}   ·   Rep to spend {view.Balance}   ·   Lifetime rep {view.Lifetime} ({next})",
            _text, 24));

        // The faction tab ranks by your points with the clerk's faction.
        bool factionTab = s_tab == "faction" && RadiantQuestsMod.Settings.FactionQuests;
        var standing = clerk is not null && view.Factions.TryGetValue(clerk.Faction, out var st) ? st : new FactionStanding(0, 0, RadiantQuestsMod.Settings.RankC);
        if (factionTab)
            w.Children.Add(Label("rq-frep", $"{clerk?.FactionName}: rank {Ranks.Name(standing.Rank)}   ·   {standing.Points} points"
                + (standing.NextRankAt < 0 ? " (top rank)" : $" (next rank at {standing.NextRankAt})"), _text, 24));

        var tabs = Row("rq-tabs", 30);
        tabs.Children.Add(Button("rq-tab-g", "General", _tab, "general", true, 150, !factionTab));
        if (RadiantQuestsMod.Settings.FactionQuests)
            tabs.Children.Add(Button("rq-tab-f", $"{clerk?.FactionName} work", _tab, "faction", true, 260, factionTab));
        if (view.Boards.TryGetValue(s_site, out var bv))
            tabs.Children.Add(Button("rq-reroll", $"New quests ({bv.RerollCost} rep)", _reroll, "", bv.RerollCost <= view.Balance, 220));
        w.Children.Add(tabs);

        // Rank boards.
        view.Boards.TryGetValue(s_site, out var board);
        var tabOffers = factionTab ? board?.FactionOffers : board?.Offers;
        int myRank = factionTab ? standing.Rank : view.Rank;
        var ranks = Row("rq-ranks", 30);
        for (int b = 0; b <= Ranks.Top; b++)
        {
            int n = tabOffers?.Count(o => o.Rank == b && !o.Taken) ?? 0;
            string caption = $"{Ranks.Name(b)} ({n})" + (b > myRank ? " locked" : "");
            ranks.Children.Add(Button($"rq-rank{b}", caption, _rank, b.ToString(), true, 110, s_rankTab == b));
        }
        w.Children.Add(ranks);

        // The selected board's quests.
        var offers = tabOffers?.Where(o => o.Rank == s_rankTab).ToList() ?? [];
        var list = new UiNode("rq-offers") { Placement = new UiPlacement(UiLength.Fill, 210), Flow = UiFlow.Column, Gap = 4 };
        if (board is null) list.Children.Add(Label("rq-wait", "The clerk is fetching today's board...", _dim, 28));
        else if (offers.Count == 0)
            list.Children.Add(Label("rq-none", s_rankTab > myRank
                ? $"No rank {Ranks.Name(s_rankTab)} work today. You need rank {Ranks.Name(s_rankTab)} for these."
                : $"No rank {Ranks.Name(s_rankTab)} work today. The board changes every day.", _dim, 28));
        foreach (var o in offers.Take(6))
        {
            var row = Row($"rq-o{o.Slot}", 32, _row);
            string pay = o.Rep > 0 ? $"{o.Cats} cats, {o.Rep} rep" + (factionTab ? $" + {o.Rep} points" + (o.Relation > 0 ? $", relations +{o.Relation}" : "") : "") : $"{o.Cats} cats, no rep (below your rank)";
            row.Children.Add(Label($"rq-ot{o.Slot}", $"  {o.Text}   ·   {pay}", o.Taken ? _dim : _text, 32, new UiLength(-130, 1)));
            string caption = o.Taken ? "Taken" : o.Locked ? "Locked" : "Accept";
            row.Children.Add(Button($"rq-oa{o.Slot}", caption, _accept, o.Slot.ToString(), !o.Taken && !o.Locked, 120));
            list.Children.Add(row);
        }
        w.Children.Add(list);

        // Your quests.
        w.Children.Add(Label("rq-mine-h", $"Your quests ({view.Quests.Count})", _title, 28));
        var mine = new UiNode("rq-mine") { Placement = new UiPlacement(UiLength.Fill, 230), Flow = UiFlow.Column, Gap = 4 };
        if (view.Quests.Count == 0) mine.Children.Add(Label("rq-mine-none", "None yet. Accept one above.", _dim, 28));
        foreach (var q in view.Quests.Take(6))
        {
            var row = Row($"rq-q{q.Id}", 32, _row);
            string state = q.Done ? "done, hand it in" : $"{q.Have}/{q.Need}, {Left(q.HoursLeft)}";
            row.Children.Add(Label($"rq-qt{q.Id}", $"  {Ranks.Name(q.Rank)}  {q.Text}   ·   {state}   ·   {q.Cats} cats, {q.Rep} rep",
                q.Done ? _title : _text, 32, new UiLength(-250, 1)));
            row.Children.Add(Button($"rq-qh{q.Id}", "Hand in", _handIn, q.Id.ToString(), q.Done, 120));
            row.Children.Add(Button($"rq-qx{q.Id}", "Abandon", _abandon, q.Id.ToString(), true, 120));
            mine.Children.Add(row);
        }
        if (view.Quests.Count > 6) mine.Children.Add(Label("rq-more", $"...and {view.Quests.Count - 6} more", _dim, 24));
        w.Children.Add(mine);
        return w;
    }

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

    static UiNode Label(string id, string text, string style, float height, UiLength? width = null) => new(id, UiNodeKind.Label)
    {
        Placement = new UiPlacement(width ?? UiLength.Fill, height),
        Style = style,
        Text = text,
    };

    UiNode Button(string id, string text, string action, string argument, bool enabled, float width, bool on = false) => new(id, UiNodeKind.Button)
    {
        Placement = new UiPlacement(width, 30),
        Style = on ? _buttonOn : _button,
        Text = text,
        Action = enabled ? action : "",
        Argument = argument,
        Enabled = enabled,
    };

    #endregion
}
