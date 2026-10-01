#pragma warning disable CA1859, CA1822, CA1861, CA1305, CA1307, CA1310, CA1002, CA1805, CA2227
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

// LOCAL ONLY - never commit. Save-branch timeline mockups for issue #680.

/// <summary>Colours, lanes and positions shared by the horizontal and list-side timelines.</summary>
public static class TimelineModel
{
    public static long TicksPerDay { get; set; } = 1_440;
    public static int DaysPerYear { get; set; } = 40;
    public static int DaysPerSeason => Math.Max(1, DaysPerYear / 4);

    public sealed record Lane(string Key, int Index, SaveBranch? Branch, ManualWorldSave[] Points, Lane? Parent, ManualWorldSave? ForkSave);

    public static string Key(ManualWorldSave save) => save.Branch?.Id ?? string.Empty;

    public static float Day(long tick) => tick / (float)TicksPerDay;

    /// <summary>
    /// Branch 1 on top. Each branch sits under the branch it grew from, the
    /// most recent fork closest, so fork lines never cross another branch.
    /// </summary>
    public static Lane[] Lanes(ManualWorldSave[] saves)
    {
        var groups = saves.GroupBy(Key).ToDictionary(group => group.Key, group => group.OrderBy(save => save.WorldTick).ToArray());
        ManualWorldSave? ForkOf(string key) =>
            groups[key][0].Branch?.StartedFromId is { } from ? saves.FirstOrDefault(save => save.Id == from) : null;
        var roots = groups.Keys.Where(key => ForkOf(key) is null).OrderBy(key => groups[key][0].Branch?.Number ?? 0).ToList();
        var lanes = new List<Lane>();
        void Place(string key, Lane? parent)
        {
            var points = groups[key];
            var lane = new Lane(key, lanes.Count, points[0].Branch, points, parent, ForkOf(key));
            lanes.Add(lane);
            var children = groups.Keys.Where(child => ForkOf(child) is { } fork && Key(fork) == key)
                .OrderByDescending(child => ForkOf(child)!.WorldTick).ToArray();
            foreach (var child in children) Place(child, lane);
        }
        foreach (var root in roots) Place(root, null);
        return [.. lanes];
    }

    public static bool IsLatest(Lane lane, ManualWorldSave save) => lane.Points[^1].Id == save.Id;

    public static Color BranchColor(int number)
    {
        var dark = UiTheme.Current.Name == "dark";
        Color[] light = [new("4A7033"), new("9C5F2E"), new("3F6F96"), new("85467A")];
        Color[] night = [new("8DBA6A"), new("D69A5E"), new("7FA9CF"), new("C48AB8")];
        return (dark ? night : light)[Math.Max(0, number - 1) % 4];
    }

    public static Color LaneColor(Lane lane) => BranchColor(lane.Branch?.Number ?? 1);

    public static Color SeasonColor(int season) => (season % 4) switch
    {
        0 => new Color("8DBA6A"),
        1 => new Color("E3C25B"),
        2 => new Color("D4874A"),
        _ => new Color("A9C4D9"),
    };

    public static string SeasonName(int season) => (season % 4) switch { 0 => "spring", 1 => "summer", 2 => "autumn", _ => "winter" };

    public static string LaneTitle(Lane lane) => lane.Branch is { } branch ? $"Branch {branch.Number}" : "Earlier saves";

    public static int BranchesFrom(ManualWorldSave[] saves, ManualWorldSave save) =>
        saves.Where(other => other.Branch?.StartedFromId == save.Id).Select(Key).Distinct().Count();

    public static string Fit(Font font, int size, string text, float width)
    {
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= width) return text;
        for (var length = text.Length - 1; length > 1; length--)
        {
            var cut = text[..length].TrimEnd() + "…";
            if (font.GetStringSize(cut, HorizontalAlignment.Left, -1, size).X <= width) return cut;
        }
        return "…";
    }
}

public enum TimelinePart { Whole, Names, Lines }

/// <summary>Branches as lanes over world days, with saves as points (options A and C).</summary>
public partial class SaveTimelineView : Control
{
    public ManualWorldSave[] Saves { get; set; } = [];
    public string? SelectedId { get; set; }
    public string? PlayingBranchId { get; set; }
    public long NowTick { get; set; }
    public float LaneHeight { get; set; } = 40;
    public float PixelsPerDay { get; set; } = 34;
    public float GutterWidth { get; set; } = 112;
    public TimelinePart Part { get; set; } = TimelinePart.Whole;
    public event Action<string>? SaveChosen;

    private const float RulerHeight = 30;
    private const float PillRoom = 110;
    private TimelineModel.Lane[] lanes = [];
    private float firstDay;
    private float lastDay;
    private readonly List<(Rect2 Area, string Id)> hitAreas = [];

    private float Gutter => Part == TimelinePart.Whole ? GutterWidth : 0;

    public void Refresh()
    {
        lanes = TimelineModel.Lanes(Saves);
        var days = Saves.Select(save => TimelineModel.Day(save.WorldTick)).Append(TimelineModel.Day(NowTick)).ToArray();
        firstDay = MathF.Floor(days.Min());
        lastDay = MathF.Ceiling(days.Max());
        var height = RulerHeight + 10 + lanes.Length * LaneHeight + 4;
        CustomMinimumSize = Part == TimelinePart.Names
            ? new Vector2(GutterWidth, height)
            : new Vector2(Gutter + 16 + (lastDay - firstDay) * PixelsPerDay + PillRoom, height);
        QueueRedraw();
    }

    private float X(float day) => MathF.Floor(Gutter + 16 + (day - firstDay) * PixelsPerDay);
    private float Y(int lane) => MathF.Floor(RulerHeight + 10 + lane * LaneHeight + LaneHeight * 0.45f);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            foreach (var (area, id) in hitAreas)
                if (area.HasPoint(click.Position)) { SelectedId = id; SaveChosen?.Invoke(id); QueueRedraw(); return; }
    }

    public override void _Draw()
    {
        hitAreas.Clear();
        var p = UiTheme.Current;
        var font = GetThemeFont("font", "Label");
        var size = GetThemeFontSize("font_size", "Label");
        var width = Size.X;

        foreach (var lane in lanes)
            if (lane.Index % 2 == 1)
                DrawRect(new Rect2(0, RulerHeight + 10 + lane.Index * LaneHeight, width, LaneHeight), p.Inset with { A = 0.5f });

        if (Part != TimelinePart.Lines) DrawNames(p, font, size);
        if (Part == TimelinePart.Names) return;
        DrawRuler(p, font, size, width);
        DrawLines(p, font, size);
        DrawPoints(p, font, size, width);
    }

    private void DrawNames(UiPalette p, Font font, int size)
    {
        foreach (var lane in lanes)
        {
            var y = Y(lane.Index);
            DrawRect(new Rect2(4, y - 9, 3, 24), TimelineModel.LaneColor(lane));
            DrawString(font, new Vector2(12, y + 1), TimelineModel.LaneTitle(lane), HorizontalAlignment.Left, -1, size, p.Ink);
            var note = lane.ForkSave is { } fork ? "from " + fork.Name : "the beginning";
            DrawString(font, new Vector2(12, y + 14), TimelineModel.Fit(font, size, note, GutterWidth - 18), HorizontalAlignment.Left, -1, size, p.InkMuted);
        }
    }

    // One tinted band per season, named where it starts, with the day of the season under each tick.
    private void DrawRuler(UiPalette p, Font font, int size, float width)
    {
        var every = PixelsPerDay >= 26 ? 1 : PixelsPerDay >= 10 ? 5 : 10;
        var nameEnd = float.MinValue;
        for (var day = (int)firstDay; day <= lastDay; day++)
        {
            var dayOfYear = day % TimelineModel.DaysPerYear;
            var season = dayOfYear / TimelineModel.DaysPerSeason;
            var dayOfSeason = dayOfYear % TimelineModel.DaysPerSeason + 1;
            var left = X(day);
            DrawRect(new Rect2(left, 0, PixelsPerDay, 6), TimelineModel.SeasonColor(season) with { A = p.Name == "dark" ? 0.6f : 0.75f });
            DrawLine(new Vector2(left, 6), new Vector2(left, dayOfSeason == 1 ? 14 : 10), dayOfSeason == 1 ? p.InkMuted : p.InkFaint, 1);
            if (dayOfSeason == 1 || day == (int)firstDay)
            {
                var year = day / TimelineModel.DaysPerYear + 1;
                var name = char.ToUpperInvariant(TimelineModel.SeasonName(season)[0]) + TimelineModel.SeasonName(season)[1..] + (year > 1 ? $" · year {year}" : string.Empty);
                DrawTexture(PixelIcons.Season(TimelineModel.SeasonName(season), 1), new Vector2(left + 2, 13));
                DrawString(font, new Vector2(left + 16, 24), name, HorizontalAlignment.Left, -1, size, p.Ink);
                nameEnd = left + 20 + font.GetStringSize(name, HorizontalAlignment.Left, -1, size).X;
            }
            else if (dayOfSeason % every == 0 && left + 2 > nameEnd &&
                (TimelineModel.DaysPerSeason - dayOfSeason + 1) * PixelsPerDay > 34)
                DrawString(font, new Vector2(left + 2, 24), dayOfSeason.ToString(), HorizontalAlignment.Left, -1, size, p.InkMuted);
        }
        DrawLine(new Vector2(Gutter, RulerHeight + 2), new Vector2(width, RulerHeight + 2), p.Separator, 1);
    }

    private void DrawLines(UiPalette p, Font font, int size)
    {
        foreach (var lane in lanes)
        {
            var color = TimelineModel.LaneColor(lane);
            var y = Y(lane.Index);
            var startX = X(TimelineModel.Day(lane.Points[0].WorldTick));
            if (lane.ForkSave is { } fork && lane.Parent is { } parent)
            {
                var fx = X(TimelineModel.Day(fork.WorldTick));
                var py = Y(parent.Index);
                const float Radius = 8;
                DrawLine(new Vector2(fx, py), new Vector2(fx, y - Radius), color, 3);
                DrawArc(new Vector2(fx + Radius, y - Radius), Radius, MathF.PI / 2, MathF.PI, 8, color, 3);
                startX = fx + Radius;
            }
            var endX = X(TimelineModel.Day(lane.Points[^1].WorldTick));
            DrawLine(new Vector2(startX, y), new Vector2(endX, y), color, 3);
            if (lane.Key != PlayingBranchId) continue;

            // Where the running world is now: a dashed run on to a "You are here" tag.
            var nowX = X(TimelineModel.Day(NowTick));
            for (var x = endX + 6; x < nowX; x += 8) DrawLine(new Vector2(x, y), new Vector2(Math.Min(x + 4, nowX), y), color, 3);
            const string Text = "You are here";
            var textWidth = font.GetStringSize(Text, HorizontalAlignment.Left, -1, size).X;
            var pill = new Rect2(nowX + 2, y - 9, textWidth + 24, 18);
            DrawRect(pill, p.Ember);
            DrawRect(pill, p.EmberDark, filled: false, width: 1);
            DrawTexture(PixelIcons.Texture(PixelGlyph.Play, p.EmberInk, p.EmberInk, 1), new Vector2(pill.Position.X + 4, y - 6));
            DrawString(font, new Vector2(pill.Position.X + 18, y + 4), Text, HorizontalAlignment.Left, -1, size, p.EmberInk);
        }
    }

    private void DrawPoints(UiPalette p, Font font, int size, float width)
    {
        var forkIds = lanes.Where(lane => lane.ForkSave is not null).Select(lane => lane.ForkSave!.Id).ToHashSet();
        foreach (var lane in lanes)
        {
            var color = TimelineModel.LaneColor(lane);
            var y = Y(lane.Index);
            var named = lane.Points.Where(save => !save.IsAutosave).ToArray();
            var previousBelowEnd = float.MinValue;
            foreach (var save in lane.Points)
            {
                var x = X(TimelineModel.Day(save.WorldTick));
                var latest = TimelineModel.IsLatest(lane, save);
                var selected = save.Id == SelectedId;
                if (selected) DrawCircle(new Vector2(x, y), 10, p.Ember with { A = 0.35f });
                if (save.IsAutosave)
                {
                    DrawCircle(new Vector2(x, y), 4, p.Paper);
                    DrawArc(new Vector2(x, y), 4, 0, MathF.Tau, 12, color, 2);
                }
                else
                {
                    DrawCircle(new Vector2(x, y), latest ? 7 : 6, p.Ink);
                    DrawCircle(new Vector2(x, y), latest ? 5 : 4, latest ? color : p.Paper);
                }
                if (latest && !save.IsAutosave)
                {
                    DrawLine(new Vector2(x, y - 7), new Vector2(x, y - 19), p.Ink, 1);
                    DrawColoredPolygon([new Vector2(x + 1, y - 19), new Vector2(x + 9, y - 16), new Vector2(x + 1, y - 13)], color);
                }
                if (selected) DrawArc(new Vector2(x, y), 10, 0, MathF.Tau, 20, p.Ember, 2);
                hitAreas.Add((new Rect2(x - 10, y - 12, 20, 24), save.Id));
                if (save.IsAutosave) continue;

                // Names sit under their point. A branch growing down from the point,
                // or a name still running from the left, sends it above instead.
                var at = Array.IndexOf(named, save);
                var next = at + 1 < named.Length ? X(TimelineModel.Day(named[at + 1].WorldTick)) : width - 8;
                // A branch growing down from the point: start the name just past its line.
                var labelX = forkIds.Contains(save.Id) ? x + 7 : x - 4;
                var label = TimelineModel.Fit(font, size, save.Name, Math.Max(40, Math.Min(next - labelX - 8, 160)));
                var labelWidth = font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X;
                var above = labelX < previousBelowEnd + 6 && !latest;
                var baseline = above ? y - 12 : y + 18;
                DrawString(font, new Vector2(labelX, baseline), label, HorizontalAlignment.Left, -1, size, selected ? p.Ink : p.InkMuted);
                if (selected) DrawLine(new Vector2(labelX, baseline + 2), new Vector2(labelX + labelWidth, baseline + 2), p.Ember, 1);
                if (!above) previousBelowEnd = labelX + labelWidth;
            }
        }
    }
}

/// <summary>Branch lines drawn in the list's left edge, one lane per branch (option B).</summary>
public partial class SaveRailGutter : Control
{
    public SlotList? List { get; set; }
    public ManualWorldSave[] Ordered { get; set; } = [];
    public ManualWorldSave[] All { get; set; } = [];
    public string? PlayingBranchId { get; set; }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (List is null || List.GetChildCount() == 0) return;
        var cards = List.GetChild(0) as Control;
        if (cards is null) return;
        var rows = cards.GetChildren().OfType<Control>().Where(row => row.Visible).ToArray();
        if (rows.Length < Ordered.Length) return;
        var p = UiTheme.Current;
        var lanes = TimelineModel.Lanes(All);
        var laneOf = lanes.ToDictionary(lane => lane.Key, lane => lane.Index);
        var offset = List.GetGlobalRect().Position.Y - GetGlobalRect().Position.Y - List.ScrollVertical;
        float RowY(int index) => MathF.Floor(offset + rows[index].Position.Y + rows[index].Size.Y / 2);
        float LaneX(int lane) => 10 + lane * 12;
        var rowOf = Ordered.Select((save, index) => (save.Id, index)).ToDictionary(item => item.Id, item => item.index);

        foreach (var lane in lanes)
        {
            var color = TimelineModel.LaneColor(lane);
            var listed = lane.Points.Where(save => rowOf.ContainsKey(save.Id)).Select(save => rowOf[save.Id]).ToArray();
            if (listed.Length == 0) continue;
            var x = LaneX(lane.Index);
            var top = RowY(listed.Min());
            var bottom = RowY(listed.Max());
            if (lane.Key == PlayingBranchId)
            {
                // The running world continues above this branch's newest save.
                for (var y = top - 4; y > top - 22; y -= 6) DrawLine(new Vector2(x, y), new Vector2(x, y - 3), color, 2);
            }
            DrawLine(new Vector2(x, top), new Vector2(x, bottom), color, 2);
            if (lane.ForkSave is { } fork && rowOf.TryGetValue(fork.Id, out var forkRow) && lane.Parent is { } parent)
            {
                // Down (or up) to the save this branch grew from, then across to its lane.
                var fy = RowY(forkRow);
                var px = LaneX(parent.Index);
                const float Radius = 5;
                var down = fy > bottom;
                DrawLine(new Vector2(x, bottom), new Vector2(x, fy + (down ? -Radius : Radius)), color, 2);
                DrawArc(new Vector2(x - Radius, fy + (down ? -Radius : Radius)), Radius, down ? 0 : -MathF.PI / 2, down ? MathF.PI / 2 : 0, 6, color, 2);
                DrawLine(new Vector2(x - Radius, fy), new Vector2(px, fy), color, 2);
            }
        }
        foreach (var lane in lanes)
        {
            var color = TimelineModel.LaneColor(lane);
            foreach (var save in lane.Points)
            {
                if (!rowOf.TryGetValue(save.Id, out var row)) continue;
                var center = new Vector2(LaneX(lane.Index), RowY(row));
                var latest = TimelineModel.IsLatest(lane, save);
                if (save.IsAutosave)
                {
                    DrawCircle(center, 3, p.Paper);
                    DrawArc(center, 3, 0, MathF.Tau, 10, color, 2);
                }
                else
                {
                    DrawCircle(center, latest ? 5 : 4, p.Ink);
                    DrawCircle(center, latest ? 3 : 2, latest ? color : p.Paper);
                }
            }
        }
    }
}

public partial class Main
{
    private static ManualWorldSave[] TimelineSample(out string playing, out long now)
    {
        var at = DateTimeOffset.Now;
        long T(float day) => (long)(day * TimelineModel.TicksPerDay);
        var b1 = new SaveBranch("b1", 1);
        var b2 = new SaveBranch("b2", 2, "flood", "Before the flood", T(2.77f));
        var b3 = new SaveBranch("b3", 3, "kiln", "The new kiln", T(6.4f));
        playing = "b2";
        now = T(7.6f);
        return
        [
            new("camp", "First camp", at.AddHours(-30), T(1.33f), Branch: b1),
            new("flood", "Before the flood", at.AddHours(-26), T(2.77f), Branch: b1),
            new("a1", "Autosave", at.AddHours(-24), T(4.1f), IsAutosave: true, Branch: b1, ContinuedFromId: "flood"),
            new("kiln", "The new kiln", at.AddHours(-20), T(6.4f), Branch: b1, ContinuedFromId: "flood"),
            new("harvest", "Big harvest", at.AddHours(-16), T(9.5f), Branch: b1, ContinuedFromId: "kiln"),
            new("house", "A second House", at.AddHours(-9), T(8.6f), Branch: b3, ContinuedFromId: "kiln"),
            new("hill", "Rebuilt on the hill", at.AddHours(-3), T(4.6f), Branch: b2, ContinuedFromId: "flood"),
            new("a2", "Autosave", at.AddMinutes(-90), T(5.4f), IsAutosave: true, Branch: b2, ContinuedFromId: "hill"),
            new("winter", "Hungry winter", at.AddMinutes(-20), T(6.8f), Branch: b2, ContinuedFromId: "hill"),
        ];
    }

    /// <summary>A long-lived world: four branches over a year and a half, for the scrolling check.</summary>
    private static ManualWorldSave[] TimelineBusy(out string playing, out long now)
    {
        var at = DateTimeOffset.Now;
        long T(float day) => (long)(day * TimelineModel.TicksPerDay);
        var b1 = new SaveBranch("b1", 1);
        var b2 = new SaveBranch("b2", 2, "s4", "The long drought", T(14));
        var b3 = new SaveBranch("b3", 3, "s7", "Market day", T(27));
        var b4 = new SaveBranch("b4", 4, "c3", "Second winter", T(38));
        playing = "b4";
        now = T(53);
        var list = new List<ManualWorldSave>();
        void Add(string id, string name, float day, SaveBranch branch, int hoursAgo, bool auto = false) =>
            list.Add(new(id, name, at.AddHours(-hoursAgo), T(day), IsAutosave: auto, Branch: branch));
        Add("s1", "First camp", 1.2f, b1, 90); Add("s2", "Spring planting", 4.5f, b1, 88); Add("s3", "First House", 9, b1, 86);
        Add("s4", "The long drought", 14, b1, 84); Add("s5", "Rain at last", 19, b1, 82); Add("s6", "Kiln fired", 23, b1, 80);
        Add("s7", "Market day", 27, b1, 78); Add("s8", "Harvest feast", 33, b1, 76); Add("s9", "Year one", 40, b1, 74);
        Add("a1", "Autosave", 17, b2, 60, true); Add("b2a", "Moved to the coast", 18.5f, b2, 58); Add("b2b", "Boats", 24, b2, 56); Add("b2c", "Storm season", 31, b2, 54);
        Add("c1", "Two Towns", 30, b3, 40); Add("c2", "Trade road", 35, b3, 38); Add("c3", "Second winter", 38, b3, 36); Add("c4", "Bridge built", 44, b3, 30);
        Add("d1", "A warm winter", 41, b4, 12); Add("a2", "Autosave", 45, b4, 6, true); Add("d2", "Spring again", 48.5f, b4, 3); Add("d3", "New families", 51, b4, 1);
        return [.. list];
    }

    private async Task RunTimeline(OwnerWorldSnapshot snapshot, string output)
    {
        var only = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--only=", StringComparison.Ordinal))?["--only=".Length..].Split(',');
        observedCalendarPace = snapshot.CalendarPace;
        TimelineModel.TicksPerDay = Math.Max(1, snapshot.CalendarPace?.TicksPerDay ?? 1_440);
        TimelineModel.DaysPerYear = Math.Max(4, snapshot.CalendarPace?.DaysPerYear ?? 40);
        selectedInhabitantId = null;
        Render(snapshot, []);
        statusToast.Hide();
        eventsPanel.Hide();
        var body = (VBoxContainer)manualSaveList.GetParent();
        var savesLabel = (Control)body.GetChild(manualSaveList.GetIndex() - 1);
        var added = new List<Node>();

        void Reset()
        {
            foreach (var node in added) { node.GetParent()?.RemoveChild(node); node.QueueFree(); }
            added.Clear();
            if (manualSaveList.GetParent() != body)
            {
                manualSaveList.GetParent().RemoveChild(manualSaveList);
                body.AddChild(manualSaveList);
                body.MoveChild(manualSaveList, savesLabel.GetIndex() + 1);
            }
            manualSaveCard.CustomMinimumSize = new Vector2(520, 0);
            manualSaveList.CustomMinimumSize = new Vector2(0, 250);
            manualSaveList.Visible = true;
            savesLabel.Visible = true;
            manualSaveStatus.Visible = true;
        }

        void OpenList(ManualWorldSave[] saves, string selectId)
        {
            ShowManualSavePanel(true);
            CancelManualSaveListRead();
            allListedManualSaves = saves;
            listedManualSaves = OrderSavesByBranch(saves);
            RenderManualSaveList();
            var index = Array.FindIndex(listedManualSaves, save => save.Id == selectId);
            if (index >= 0) manualSaveList.Select(index);
            manualSaveStatus.Text = "Choose a save to load. Your current world is saved first.";
            RefreshManualSaveAvailability();
        }

        // Lane names stay put on the left while a long timeline scrolls beside them.
        PanelContainer TimelineFrame(ManualWorldSave[] saves, string playing, long now, string selected, float laneHeight, float pixelsPerDay, float gutter, bool scrollToEnd, float scrollShare = 1)
        {
            SaveTimelineView Make(TimelinePart part) => new()
            {
                Saves = saves, PlayingBranchId = playing, NowTick = now, SelectedId = selected,
                LaneHeight = laneHeight, PixelsPerDay = pixelsPerDay, GutterWidth = gutter, Part = part,
            };
            var names = Make(TimelinePart.Names);
            var lines = Make(TimelinePart.Lines);
            lines.SaveChosen += id =>
            {
                var row = Array.FindIndex(listedManualSaves, save => save.Id == id);
                if (row >= 0) manualSaveList.Select(row);
                RefreshManualSaveAvailability();
            };
            names.Refresh();
            lines.Refresh();
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 0);
            row.AddChild(names);
            row.AddChild(new VSeparator());
            var scroll = new ScrollContainer
            {
                VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, lines.CustomMinimumSize.Y + 12),
            };
            scroll.AddChild(lines);
            row.AddChild(scroll);
            var frame = new PanelContainer { ThemeTypeVariation = "InsetPanel" };
            frame.AddChild(row);
            if (scrollToEnd) Callable.From(() => scroll.ScrollHorizontal = (int)(lines.CustomMinimumSize.X * scrollShare)).CallDeferred();
            return frame;
        }

        void Insert(params Control[] nodes)
        {
            var at = savesLabel.GetIndex();
            foreach (var node in nodes)
            {
                body.AddChild(node);
                body.MoveChild(node, at++);
                added.Add(node);
            }
        }

        async Task Shot(string name, Action open)
        {
            if (only is not null && !only.Contains(name)) return;
            Reset();
            try { open(); }
            catch (Exception exception) { GD.Print($"TIMELINE {name} failed: {exception}"); }
            await MockFrames(10);
            GetViewport().GetTexture().GetImage().SavePng($"{output}-tl-{name}.png");
        }

        // Today, with #682: the list groups saves by branch.
        await Shot("now", () => OpenList(TimelineSample(out _, out _), "winter"));

        // A: a timeline above the list.
        foreach (var (name, busy) in new[] { ("a", false), ("a-busy", true) })
        {
            await Shot(name, () =>
            {
                string playing; long now;
                var saves = busy ? TimelineBusy(out playing, out now) : TimelineSample(out playing, out now);
                var selected = busy ? "d3" : "winter";
                OpenList(saves, selected);
                manualSaveCard.CustomMinimumSize = new Vector2(780, 0);
                manualSaveStatus.Visible = false;
                var heading = new HBoxContainer();
                heading.AddChild(new Label { Text = "TIMELINE", ThemeTypeVariation = "SectionLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
                heading.AddChild(new Label { Text = "Click a point to choose that save", ThemeTypeVariation = "DimLabel" });
                Insert(heading, TimelineFrame(saves, playing, now, selected, 36, busy ? 14 : 54, 112, busy));
                manualSaveList.CustomMinimumSize = new Vector2(0, busy ? 104 : 150);
            });
        }

        // B: branch lines in the list's left edge.
        foreach (var (name, scrolled) in new[] { ("b", false), ("b-scrolled", true) })
        await Shot(name, () =>
        {
            var saves = TimelineSample(out var playing, out _);
            OpenList(saves, "winter");
            manualSaveList.CustomMinimumSize = new Vector2(0, 300);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 0);
            var gutter = new SaveRailGutter
            {
                List = manualSaveList,
                Ordered = listedManualSaves,
                All = saves,
                PlayingBranchId = playing,
                ClipContents = true,
                CustomMinimumSize = new Vector2(48, 0),
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            var at = manualSaveList.GetIndex();
            body.RemoveChild(manualSaveList);
            manualSaveList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(gutter);
            row.AddChild(manualSaveList);
            body.AddChild(row);
            body.MoveChild(row, at);
            added.Add(row);
            manualSaveCard.CustomMinimumSize = new Vector2(580, 0);
            if (scrolled) Callable.From(() => manualSaveList.ScrollVertical = 300).CallDeferred();
        });

        // C: the timeline is the main picker, with a list behind a switch.
        foreach (var (name, busy) in new[] { ("c", false), ("c-busy", true) })
        {
            await Shot(name, () =>
            {
                string playing; long now;
                var saves = busy ? TimelineBusy(out playing, out now) : TimelineSample(out playing, out now);
                var selectedId = busy ? "c3" : "flood";
                OpenList(saves, selectedId);
                manualSaveCard.CustomMinimumSize = new Vector2(900, 0);
                manualSaveStatus.Visible = false;
                manualSaveList.Visible = false;
                savesLabel.Visible = false;
                var lanes = TimelineModel.Lanes(saves);
                var switchRow = new HBoxContainer();
                switchRow.AddChild(new Label
                {
                    Text = $"WILLOWMERE · {lanes.Length} BRANCHES · {saves.Length} SAVES",
                    ThemeTypeVariation = "SectionLabel",
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                });
                var views = new SegmentedChoice();
                views.AddItem("Timeline");
                views.AddItem("List");
                views.Select(0);
                switchRow.AddChild(views);
                var frame = TimelineFrame(saves, playing, now, selectedId, 44, busy ? 17 : 66, 128, busy, 0.35f);

                // The chosen save, with everything the list would say about it.
                var chosen = saves.First(save => save.Id == selectedId);
                var lane = lanes.First(item => item.Key == TimelineModel.Key(chosen));
                var latest = TimelineModel.IsLatest(lane, chosen);
                var details = new PanelContainer { ThemeTypeVariation = "InsetRow" };
                var detailRow = new HBoxContainer();
                detailRow.AddThemeConstantOverride("separation", 10);
                detailRow.AddChild(new ColorRect { Color = TimelineModel.LaneColor(lane), CustomMinimumSize = new Vector2(4, 0) });
                var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                text.AddThemeConstantOverride("separation", 2);
                var titleRow = new HBoxContainer();
                titleRow.AddThemeConstantOverride("separation", 8);
                titleRow.AddChild(new Label { Text = chosen.Name, ThemeTypeVariation = "HeadingLabel" });
                titleRow.AddChild(new Label { Text = TimelineModel.LaneTitle(lane).ToUpperInvariant(), ThemeTypeVariation = "TagLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
                if (latest) titleRow.AddChild(new Label { Text = "LATEST", ThemeTypeVariation = "TagLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
                text.AddChild(titleRow);
                text.AddChild(new Label { Text = $"{DisplayWorldClock(chosen.WorldTick)} · Saved {GameUiText.SavedAgo(chosen.CreatedUtc, DateTimeOffset.Now)}", ThemeTypeVariation = "DimLabel" });
                var grown = TimelineModel.BranchesFrom(saves, chosen);
                var grew = grown switch { 0 => "", 1 => "Another branch grew from here. ", _ => $"{grown} other branches grew from here. " };
                text.AddChild(new Label
                {
                    Text = latest
                        ? $"{grew}Playing on from here continues {TimelineModel.LaneTitle(lane)}."
                        : $"{grew}Playing on from it starts a new branch; your later saves stay as they are.",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    CustomMinimumSize = new Vector2(420, 0),
                });
                detailRow.AddChild(text);
                details.AddChild(detailRow);
                Insert(switchRow, frame, details);
            });
        }
        manualSaveOverlay.Hide();
    }
}
