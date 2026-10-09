using System.Globalization;
using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// The world's calendar as the timeline's season bar draws it. An older host
/// sends no season lengths, so its year is split into four equal seasons.
/// </summary>
public sealed record SaveTimelineCalendar(int TicksPerDay, int DaysPerYear, IReadOnlyList<int> SeasonDays,
    int CalendarOffsetTicks = 0)
{
    private static readonly string[] SeasonNames = ["spring", "summer", "autumn", "winter"];

    public static SaveTimelineCalendar From(OwnerWorldCalendarPace? pace)
    {
        var ticksPerDay = Math.Max(1, pace?.TicksPerDay ?? 1_440);
        var daysPerYear = Math.Max(1, pace?.DaysPerYear ?? 365);
        int[] lengths = pace is null ? [] : [pace.SpringDays, pace.SummerDays, pace.AutumnDays, pace.WinterDays];
        if (lengths.Length != 4 || lengths.Any(days => days <= 0) || lengths.Sum(days => (long)days) != daysPerYear)
        {
            var quarter = daysPerYear / 4;
            lengths = quarter == 0 ? [daysPerYear, 0, 0, 0] : [quarter, quarter, quarter, daysPerYear - 3 * quarter];
        }
        return new SaveTimelineCalendar(ticksPerDay, daysPerYear, lengths,
            Math.Clamp(pace?.CalendarOffsetTicks ?? 0, 0, ticksPerDay - 1));
    }

    /// <summary>Calendar days since the first midnight; a world that starts in the morning starts part-way through day 0.</summary>
    public float Day(long worldTick) => worldTick / (float)TicksPerDay + CalendarOffsetTicks / (float)TicksPerDay;

    /// <summary>The season (0 is spring), the day within it counted from 0, its length and the year counted from 1.</summary>
    public (int Season, int Day, int Length, long Year) DateOf(long day)
    {
        var year = day / DaysPerYear + 1;
        var rest = (int)(day % DaysPerYear);
        var season = 0;
        while (season < 3 && rest >= SeasonDays[season])
        {
            rest -= SeasonDays[season];
            season++;
        }
        return (season, rest, SeasonDays[season], year);
    }

    public static string SeasonName(int season) => SeasonNames[season % 4];
}

/// <summary>
/// One row of the timeline: a branch's saves in order, or the running world's
/// new branch before its first save. <see cref="ForkSave"/> is the save the
/// branch grew from, drawn on <see cref="Parent"/>'s row.
/// </summary>
public sealed record SaveTimelineLane(string Key, int Index, SaveBranch? Branch, IReadOnlyList<ManualWorldSave> Points,
    SaveTimelineLane? Parent, ManualWorldSave? ForkSave, int ColorNumber, long? OriginTick = null, bool NumberKnown = true)
{
    public bool IsUnsaved => Key == SaveTimelineLayout.UnsavedKey;

    public string Title => IsUnsaved ? "New branch"
        : Branch is { } branch ? $"Branch {branch.Number.ToString(CultureInfo.InvariantCulture)}" : "Earlier saves";

    /// <summary>Whether playing on from this save continues its branch rather than starting a new one.</summary>
    public bool IsLatest(ManualWorldSave save) =>
        Branch is not null && !Points.Any(other => other.Id != save.Id && other.BranchPosition > save.BranchPosition);
}

/// <summary>Arranges saves into the timeline's rows.</summary>
public static class SaveTimelineLayout
{
    /// <summary>The row of a new branch the running world will start with its next save.</summary>
    public const string UnsavedKey = "unsaved";

    public static string Key(ManualWorldSave save) => save.Branch?.Id ?? string.Empty;

    /// <summary>
    /// Saves from before branches on top, then branch 1. Each branch sits under
    /// the branch it grew from, the latest fork closest, so fork lines never
    /// cross another branch. When the host says the next save starts a new
    /// branch, that branch gets a row of its own under the save it grows from.
    /// </summary>
    public static SaveTimelineLane[] Lanes(IReadOnlyList<ManualWorldSave> saves, SaveTimelinePosition? position)
    {
        ArgumentNullException.ThrowIfNull(saves);
        var groups = saves.GroupBy(Key, StringComparer.Ordinal).ToDictionary(group => group.Key,
            group => group.OrderBy(save => save.BranchPosition).ThenBy(save => save.WorldTick)
                .ThenBy(save => save.CreatedUtc).ThenBy(save => save.Id, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        var byId = new Dictionary<string, ManualWorldSave>(StringComparer.Ordinal);
        foreach (var save in saves) byId.TryAdd(save.Id, save);
        // A slot can be overwritten. Only attach a branch to the original
        // version witnessed by its saved provenance, rather than its replacement.
        ManualWorldSave? ForkOf(string key)
        {
            if (key.Length == 0 || groups[key][0].Branch is not { StartedFromId: { } from } branch ||
                !byId.TryGetValue(from, out var fork) || Key(fork) == key ||
                (fork.Branch?.Number ?? 0) >= branch.Number ||
                branch.StartedFromTick is { } tick && fork.WorldTick != tick)
                return null;
            return groups[key].Any(point => point.ContinuedFromId == from &&
                point.ContinuedFromCreatedUtc == fork.CreatedUtc) ? fork : null;
        }
        var forks = groups.Keys.ToDictionary(key => key, ForkOf, StringComparer.Ordinal);
        var unsavedFork = position is { StartsNewBranch: true, ContinuedFromId: { } continued } &&
            byId.TryGetValue(continued, out var found) ? found : null;
        if (unsavedFork is not null && position is { } running &&
            (running.ContinuedFromTick is { } tick && unsavedFork.WorldTick != tick ||
                Key(unsavedFork) != (running.BranchId ?? string.Empty)))
            unsavedFork = null;
        var unsavedOriginTick = position?.ContinuedFromTick ?? unsavedFork?.WorldTick;
        var unsavedParentKey = position?.BranchId ?? (unsavedFork is null ? null : Key(unsavedFork));
        var nextNumber = position?.NextBranchNumber is { } authoritativeNumber && authoritativeNumber > 0 ? authoritativeNumber
            : saves.Select(save => save.Branch?.Number ?? 0).DefaultIfEmpty(0).Max() + 1;
        int PointIndex(ManualWorldSave save) => Array.FindIndex(groups[Key(save)], point => point.Id == save.Id);

        var lanes = new List<SaveTimelineLane>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        void Place(string key, SaveTimelineLane? parent, ManualWorldSave? fork)
        {
            if (!placed.Add(key)) return;
            if (key == UnsavedKey)
            {
                lanes.Add(new SaveTimelineLane(key, lanes.Count, null, [], parent, fork, nextNumber, unsavedOriginTick,
                    position?.NextBranchNumber is > 0));
                return;
            }
            var points = groups[key];
            var lane = new SaveTimelineLane(key, lanes.Count, points[0].Branch, points, parent, fork,
                points[0].Branch?.Number ?? 0, points[0].Branch?.StartedFromTick ?? fork?.WorldTick);
            lanes.Add(lane);
            var children = new List<(string Key, ManualWorldSave? Fork, int Number, int Order)>();
            foreach (var child in groups.Keys)
                if (forks[child] is { } childFork && Key(childFork) == key)
                    children.Add((child, childFork, groups[child][0].Branch?.Number ?? 0, PointIndex(childFork)));
            if (position is { StartsNewBranch: true } && unsavedParentKey == key)
                children.Add((UnsavedKey, unsavedFork, int.MaxValue, unsavedFork is not null ? PointIndex(unsavedFork)
                    : points.Count(point => unsavedOriginTick is { } origin && point.WorldTick <= origin) - 1));
            foreach (var child in children.OrderByDescending(child => child.Order)
                .ThenByDescending(child => child.Number))
                Place(child.Key, lane, child.Fork);
        }
        var order = groups.Keys.OrderBy(key => key.Length == 0 ? 0 : 1)
            .ThenBy(key => groups[key][0].Branch?.Number ?? 0).ThenBy(key => key, StringComparer.Ordinal).ToArray();
        foreach (var key in order.Where(key => forks[key] is null)) Place(key, null, null);
        // Damaged records could make branches start from each other; they still get rows.
        foreach (var key in order) Place(key, null, null);
        if (position is { StartsNewBranch: true }) Place(UnsavedKey, null, unsavedFork);
        else if (position is { BranchId: { } branchId, NextBranchNumber: { } continuingNumber } continuing &&
            continuingNumber > 0 && !groups.ContainsKey(branchId))
            lanes.Add(new SaveTimelineLane(branchId, lanes.Count, new SaveBranch(branchId, continuingNumber),
                [], null, null, continuingNumber, continuing.ContinuedFromTick));
        return [.. lanes];
    }

    /// <summary>
    /// The row the running world is on: the new branch's row, or the branch it
    /// continues when nothing later has been saved on it.
    /// </summary>
    public static SaveTimelineLane? NowLane(IReadOnlyList<SaveTimelineLane> lanes, SaveTimelinePosition? position)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        if (position is null) return null;
        return position.StartsNewBranch
            ? lanes.FirstOrDefault(lane => lane.IsUnsaved)
            : lanes.FirstOrDefault(lane => !lane.IsUnsaved && lane.Branch?.Id == position.BranchId);
    }

    /// <summary>How many other branches grew from this save.</summary>
    public static int BranchesFrom(IReadOnlyList<SaveTimelineLane> lanes, ManualWorldSave save) =>
        lanes.Count(lane => !lane.IsUnsaved && lane.ForkSave?.Id == save.Id);

    public static Color BranchColor(int number) => BranchColor(UiTheme.Current, number);

    public static Color BranchColor(UiPalette palette, int number)
    {
        if (number <= 0) return palette.InkFaint;
        Color[] light = [new("4A7033"), new("9C5F2E"), new("3F6F96"), new("85467A")];
        Color[] dark = [new("8DBA6A"), new("D69A5E"), new("7FA9CF"), new("C48AB8")];
        return (palette.Name == "dark" ? dark : light)[(number - 1) % 4];
    }

    // Old saves have no branch number; their faint line color is unsuitable behind text.
    public static Color BranchLabelFill(UiPalette palette, int number) =>
        number <= 0 ? palette.Paper : BranchColor(palette, number);

    public static Color SeasonColor(int season) => (season % 4) switch
    {
        0 => new Color("8DBA6A"),
        1 => new Color("E3C25B"),
        2 => new Color("D4874A"),
        _ => new Color("A9C4D9"),
    };

    /// <summary>The longest start of <paramref name="text"/> that fits with "..." after it, or the whole text if it fits.</summary>
    public static string Shorten(string text, float width, Func<string, float> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        if (measure(text) <= width) return text;
        int low = 0, high = text.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (measure(text[..middle].TrimEnd() + "...") <= width) low = middle;
            else high = middle - 1;
        }
        return text[..low].TrimEnd() + "...";
    }
}

/// <summary>
/// A world's saves as a timeline: a row per branch over world days, with the
/// season bar on top and branch names on the left. The names and the season bar
/// stay in view while the rows scroll. Clicking a save chooses it; double-click
/// or Enter asks to load it.
/// </summary>
public partial class SaveTimeline : PanelContainer
{
    public const float NamesWidth = 128;

    private readonly SaveTimelineNames names = new();
    private readonly ScrollContainer scroll = new();
    private readonly SaveTimelineRows rows = new();
    private float maximumHeight = float.MaxValue;
    private bool scrollsSideways;
    // Until the player scrolls or chooses a save, this point stays in view as the layout settles:
    // You are here when the host said where the world is, otherwise the newest end.
    private Vector2? opening;

    public event Action<string>? SaveChosen;
    public event Action<string>? SaveActivated;

    public SaveTimeline()
    {
        ThemeTypeVariation = "InsetPanel";
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        names.CustomMinimumSize = new Vector2(NamesWidth, 0);
        names.ClipContents = true;
        row.AddChild(names);
        row.AddChild(new VSeparator());
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // The rows fill the view, so the seasons run to its edge when the history is short.
        rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rows.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.AddChild(rows);
        row.AddChild(scroll);
        AddChild(row);
        void Follow()
        {
            rows.ViewLeft = scroll.ScrollHorizontal;
            rows.ViewTop = names.ViewTop = scroll.ScrollVertical;
            rows.QueueRedraw();
            names.QueueRedraw();
        }
        scroll.GetHScrollBar().ValueChanged += _ => Follow();
        scroll.GetVScrollBar().ValueChanged += _ => Follow();
        scroll.GetHScrollBar().Changed += ShowOpening;
        scroll.GetVScrollBar().Changed += ShowOpening;
        void Scrolled(InputEvent input)
        {
            if (input is InputEventMouseButton { Pressed: true }) opening = null;
        }
        scroll.GuiInput += Scrolled;
        scroll.GetHScrollBar().GuiInput += Scrolled;
        scroll.GetVScrollBar().GuiInput += Scrolled;
        rows.Chosen += (id, activate, fromKeyboard) =>
        {
            opening = null;
            if (fromKeyboard) Reveal(id);
            SaveChosen?.Invoke(id);
            if (activate) SaveActivated?.Invoke(id);
        };
        rows.Resized += () => rows.ViewWidth = ViewSize().X;
        scroll.Resized += () => rows.ViewWidth = ViewSize().X;
    }

    /// <summary>The drawn rows, for the smoke checks to click on.</summary>
    internal SaveTimelineRows Rows => rows;
    public IReadOnlyList<SaveTimelineLane> Lanes => rows.Lanes;
    public string? SelectedId => rows.SelectedId;
    public SaveTimelineLane? NowLane => rows.NowLane;
    public float PixelsPerDay => rows.PixelsPerDay;
    public float ContentHeight => rows.CustomMinimumSize.Y;

    /// <summary>
    /// Draws these saves. The running world is marked when the host said where it
    /// continues. Saves <paramref name="canChoose"/> refuses are drawn but cannot be chosen.
    /// </summary>
    public void Show(IReadOnlyList<ManualWorldSave> saves, SaveTimelinePosition? position,
        SaveTimelineCalendar calendar, long nowTick, float width, Func<ManualWorldSave, bool>? canChoose = null)
    {
        ArgumentNullException.ThrowIfNull(saves);
        ArgumentNullException.ThrowIfNull(calendar);
        var lanes = SaveTimelineLayout.Lanes(saves, position);
        rows.Lanes = names.Lanes = lanes;
        rows.NowLane = SaveTimelineLayout.NowLane(lanes, position);
        rows.Calendar = calendar;
        rows.NowTick = nowTick;
        rows.CanChoose = canChoose;
        rows.SelectedId = null;
        // Fit the whole history when it is short; a long one scrolls, newest in view.
        var days = saves.Select(save => calendar.Day(save.WorldTick))
            .Concat(lanes.Where(lane => lane.OriginTick is not null).Select(lane => calendar.Day(lane.OriginTick!.Value)))
            .Append(calendar.Day(nowTick)).ToArray();
        var span = Math.Max(1, MathF.Ceiling(days.Max()) - MathF.Floor(days.Min()) + 1);
        // The rows' share of the width: less the names, the divider and this panel's frame.
        var visible = width - NamesWidth - 8 - GetThemeStylebox("panel").GetMinimumSize().X;
        rows.PixelsPerDay = Math.Clamp((visible - 28 - SaveTimelineRows.EndRoom) / span, 14, 66);
        rows.Refresh();
        scrollsSideways = rows.CustomMinimumSize.X > visible;
        names.QueueRedraw();
        SetMaximumHeight(maximumHeight);
        opening = rows.NowPosition() ?? new Vector2(rows.CustomMinimumSize.X, 0);
        Callable.From(ShowOpening).CallDeferred();
    }

    private void ShowOpening()
    {
        var view = ViewSize();
        rows.ViewWidth = view.X;
        if (opening is not { } point) return;
        scroll.ScrollHorizontal = (int)Math.Max(0, point.X - view.X * 0.6f);
        scroll.ScrollVertical = (int)Math.Max(0, point.Y - (view.Y + SaveTimelineRows.Ruler + 3) / 2);
    }

    private Vector2 ViewSize() => scroll.Size - new Vector2(
        scroll.GetVScrollBar().Visible ? scroll.GetVScrollBar().Size.X : 0,
        scroll.GetHScrollBar().Visible ? scroll.GetHScrollBar().Size.Y : 0);

    /// <summary>Limits the whole timeline's height, frame included; more branches than fit scroll.</summary>
    public void SetMaximumHeight(float height)
    {
        maximumHeight = height;
        // Room for the sideways scroll bar only when the history is wider than the view.
        if (scroll.Size.X > 0) scrollsSideways = rows.CustomMinimumSize.X > scroll.Size.X;
        var wanted = rows.CustomMinimumSize.Y + (scrollsSideways ? 14 : 0);
        var least = Math.Min(wanted, 120);
        var room = height - GetThemeStylebox("panel").GetMinimumSize().Y;
        scroll.CustomMinimumSize = new Vector2(0, MathF.Floor(Math.Clamp(wanted, least, Math.Max(least, room))));
    }

    /// <summary>Chooses a save without reporting it, scrolling it into view.</summary>
    public void Select(string? id)
    {
        rows.SelectedId = id;
        rows.QueueRedraw();
        if (id is null) return;
        opening = null;
        Reveal(id);
    }

    private void Reveal(string id)
    {
        if (rows.PointPosition(id) is not { } point) return;
        Callable.From(() =>
        {
            var view = ViewSize();
            var left = point.X - 40;
            var right = point.X + 40;
            var top = point.Y - 32;
            var bottom = point.Y + 18;
            if (rows.TagArea(id) is { } tag)
            {
                left = Math.Min(left, tag.Position.X - 8);
                right = Math.Max(right, tag.End.X + 8);
                top = Math.Min(top, tag.Position.Y - 2);
                bottom = Math.Max(bottom, tag.End.Y + 2);
                // Crowded saves can have a tag well past their shared point.
                // When both cannot fit, keep the selected save's name in view.
                if (right - left > view.X)
                {
                    left = tag.Position.X - 8;
                    right = tag.End.X + 8;
                }
            }
            if (left < scroll.ScrollHorizontal || right > scroll.ScrollHorizontal + view.X)
                scroll.ScrollHorizontal = (int)Math.Max(0, (left + right - view.X) / 2);
            if (top < scroll.ScrollVertical + SaveTimelineRows.Ruler + 3 || bottom > scroll.ScrollVertical + view.Y)
                scroll.ScrollVertical = (int)Math.Max(0, (top + bottom - view.Y - SaveTimelineRows.Ruler - 3) / 2);
        }).CallDeferred();
    }
}

/// <summary>Each branch's numbered badge and save count, beside its row.</summary>
public partial class SaveTimelineNames : Control
{
    public IReadOnlyList<SaveTimelineLane> Lanes { get; set; } = [];
    public float ViewTop { get; set; }

    public SaveTimelineNames()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var p = UiTheme.Current;
        var font = GetThemeFont("font", "Label");
        var size = GetThemeFontSize("font_size", "Label");
        var textWidth = Size.X - 36;
        foreach (var lane in Lanes)
        {
            var y = SaveTimelineRows.LaneY(lane.Index) - ViewTop;
            if (y < SaveTimelineRows.Ruler) continue;
            var color = SaveTimelineLayout.BranchColor(p, lane.ColorNumber);
            var badge = new Rect2(6, y - 9, 18, 18);
            if (lane.IsUnsaved)
            {
                // Not saved yet: a dashed outline in the next branch's colour.
                for (var step = 0; step < 18; step += 4)
                {
                    DrawRect(new Rect2(badge.Position.X + step, badge.Position.Y, 2, 1), color);
                    DrawRect(new Rect2(badge.Position.X + step, badge.End.Y - 1, 2, 1), color);
                    DrawRect(new Rect2(badge.Position.X, badge.Position.Y + step, 1, 2), color);
                    DrawRect(new Rect2(badge.End.X - 1, badge.Position.Y + step, 1, 2), color);
                }
            }
            else
            {
                DrawRect(badge, color);
                DrawRect(badge, color.Darkened(0.35f), filled: false, width: 1);
            }
            if (lane.Branch is not null || lane.IsUnsaved && lane.NumberKnown)
            {
                var digit = lane.ColorNumber.ToString(CultureInfo.InvariantCulture);
                var w = font.GetStringSize(digit, HorizontalAlignment.Left, -1, size).X;
                DrawString(font, new Vector2(badge.Position.X + MathF.Floor((18 - w) / 2), y + 4), digit,
                    HorizontalAlignment.Left, -1, size, UiTheme.ReadableInk(p, lane.IsUnsaved ? color : p.Paper, lane.IsUnsaved ? p.Inset : color));
            }
            float Measure(string text) => font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            DrawString(font, new Vector2(32, y - 2), SaveTimelineLayout.Shorten(lane.Title, textWidth, Measure),
                HorizontalAlignment.Left, -1, size, p.Ink);
            DrawString(font, new Vector2(32, y + 12), SaveTimelineLayout.Shorten(CountText(lane), textWidth, Measure),
                HorizontalAlignment.Left, -1, size, p.InkMuted);
        }
    }

    private static string CountText(SaveTimelineLane lane)
    {
        if (lane.IsUnsaved) return "Not saved yet";
        if (lane.Points.Count == 0) return "No saves";
        var saves = lane.Points.Count(save => !save.IsAutosave);
        var autosaves = lane.Points.Count - saves;
        return saves > 0
            ? saves == 1 ? "1 save" : $"{saves.ToString(CultureInfo.InvariantCulture)} saves"
            : autosaves == 1 ? "1 autosave" : $"{autosaves.ToString(CultureInfo.InvariantCulture)} autosaves";
    }
}

/// <summary>The season bar, branch lines, saves and the running world.</summary>
public partial class SaveTimelineRows : Control
{
    public const float Ruler = 30;
    public const float LaneHeight = 58;
    public const float EndRoom = 150;
    private const int Radius = 10;
    private const float TagTextWidth = 170;
    private readonly List<(Rect2 Area, string Id)> hits = [];
    private readonly Dictionary<string, Vector2> points = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Rect2 Area, string Text)> tags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageTexture> sprites = new(StringComparer.Ordinal);
    private float firstDay;

    public IReadOnlyList<SaveTimelineLane> Lanes { get; set; } = [];
    public SaveTimelineLane? NowLane { get; set; }
    public SaveTimelineCalendar Calendar { get; set; } = SaveTimelineCalendar.From(null);
    public long NowTick { get; set; }
    public string? SelectedId { get; set; }
    public float PixelsPerDay { get; set; } = 60;
    public float ViewLeft { get; set; }
    public float ViewTop { get; set; }
    public float ViewWidth { get; set; }
    public Func<ManualWorldSave, bool>? CanChoose { get; set; }

    /// <summary>A save was chosen, whether it should load, and whether the keyboard chose it.</summary>
    public event Action<string, bool, bool>? Chosen;

    public SaveTimelineRows()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
        ThemeChanged += Refresh;
    }

    public static float LaneY(int lane) => MathF.Floor(Ruler + 8 + lane * LaneHeight + LaneHeight / 2);

    public void Refresh()
    {
        var days = Lanes.SelectMany(lane => lane.Points).Select(save => Calendar.Day(save.WorldTick))
            .Concat(Lanes.Where(lane => lane.OriginTick is not null).Select(lane => Calendar.Day(lane.OriginTick!.Value)))
            .Append(Calendar.Day(NowTick)).ToArray();
        firstDay = MathF.Floor(days.Min()) - 0.5f;
        var lastDay = MathF.Ceiling(days.Max());
        LayoutPoints();
        var tagEnd = LayoutTags();
        CustomMinimumSize = new Vector2(MathF.Ceiling(Math.Max(24 + (lastDay - firstDay) * PixelsPerDay + EndRoom, tagEnd + 8)),
            Ruler + 8 + Lanes.Count * LaneHeight);
        QueueRedraw();
    }

    /// <summary>Where a save's point is drawn, or null when it is not on the timeline.</summary>
    public Vector2? PointPosition(string id) => points.TryGetValue(id, out var point) ? point : null;

    /// <summary>The name tag used by drawing and by scrolling the selected save into view.</summary>
    internal Rect2? TagArea(string id) => tags.TryGetValue(id, out var tag) ? tag.Area : null;

    private float X(float day) => MathF.Floor(16 + (day - firstDay) * PixelsPerDay);

    private void LayoutPoints()
    {
        points.Clear();
        foreach (var lane in Lanes)
            foreach (var save in lane.Points)
                points[save.Id] = new Vector2(X(Calendar.Day(save.WorldTick)), LaneY(lane.Index));
    }

    private float LayoutTags()
    {
        tags.Clear();
        var font = GetThemeFont("font", "Label");
        var size = GetThemeFontSize("font_size", "Label");
        var forkIds = Lanes.Where(lane => lane.ForkSave is not null).Select(lane => lane.ForkSave!.Id)
            .ToHashSet(StringComparer.Ordinal);
        float Measure(string text) => font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var end = 0f;
        foreach (var lane in Lanes)
        {
            var aboveEnd = float.MinValue;
            var belowEnd = float.MinValue;
            foreach (var save in lane.Points.Where(save => !save.IsAutosave))
            {
                var point = points[save.Id];
                var latest = lane.IsLatest(save);
                var tagX = forkIds.Contains(save.Id) ? point.X + 8 : point.X - 6;
                var below = tagX > belowEnd + 4;
                var aboveX = latest ? point.X + 12 : tagX;
                var above = !below && aboveX > aboveEnd + 4;
                if (above) tagX = aboveX;
                if (!below && !above)
                {
                    below = true;
                    tagX = belowEnd + 6;
                }
                var text = SaveTimelineLayout.Shorten(save.Name, TagTextWidth, Measure);
                var area = TagRect(font, size, text, new Vector2(tagX, below ? point.Y + 9 : point.Y - 31));
                tags[save.Id] = (area, text);
                if (below) belowEnd = area.End.X;
                else aboveEnd = area.End.X;
                end = Math.Max(end, area.End.X);
            }
        }
        return end;
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (Hit(atPosition) is not { } id) return string.Empty;
        var save = Lanes.SelectMany(lane => lane.Points).First(point => point.Id == id);
        return save.IsAutosave ? "Autosave" : save.Name;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            GrabFocus();
            if (Hit(click.Position) is { } id) Choose(id, click.DoubleClick, fromKeyboard: false);
            AcceptEvent();
            return;
        }
        var ordered = Lanes.SelectMany(lane => lane.Points).Where(save => CanChoose?.Invoke(save) ?? true)
            .OrderBy(save => points[save.Id].X)
            .ThenBy(save => points[save.Id].Y).ToArray();
        if (ordered.Length == 0) return;
        var current = Array.FindIndex(ordered, save => save.Id == SelectedId);
        if (@event.IsActionPressed("ui_right") || @event.IsActionPressed("ui_left"))
        {
            var step = @event.IsActionPressed("ui_right") ? 1 : -1;
            Choose(ordered[Math.Clamp(current < 0 ? (step > 0 ? 0 : ordered.Length - 1) : current + step, 0, ordered.Length - 1)].Id, false, fromKeyboard: true);
            AcceptEvent();
        }
        else if ((@event.IsActionPressed("ui_down") || @event.IsActionPressed("ui_up")) && current >= 0)
        {
            // The nearest save on the row above or below.
            var from = points[ordered[current].Id];
            var y = from.Y + (@event.IsActionPressed("ui_down") ? LaneHeight : -LaneHeight);
            var next = ordered.Where(save => Math.Abs(points[save.Id].Y - y) < 1)
                .OrderBy(save => Math.Abs(points[save.Id].X - from.X)).FirstOrDefault();
            if (next is not null) Choose(next.Id, false, fromKeyboard: true);
            AcceptEvent();
        }
        else if (@event.IsActionPressed("ui_accept") && current >= 0)
        {
            Choose(ordered[current].Id, true, fromKeyboard: true);
            AcceptEvent();
        }
    }

    private void Choose(string id, bool activate, bool fromKeyboard)
    {
        SelectedId = id;
        QueueRedraw();
        Chosen?.Invoke(id, activate, fromKeyboard);
    }

    // A click chooses the nearest point or the tag it falls on.
    private string? Hit(Vector2 at)
    {
        // The calendar is painted over the scrolling rows and owns this area.
        if (at.Y < ViewTop + Ruler + 3) return null;
        string? best = null;
        var bestDistance = float.MaxValue;
        foreach (var (area, id) in hits)
        {
            if (!area.HasPoint(at)) continue;
            var distance = area.GetCenter().DistanceSquaredTo(at);
            // Equal-time points overlap. The last drawn point is on top, so it
            // must also win an equal-distance hit; the tags still expose each save.
            if (distance <= bestDistance)
            {
                best = id;
                bestDistance = distance;
            }
        }
        return best;
    }

    public override void _Draw()
    {
        hits.Clear();
        var p = UiTheme.Current;
        var font = GetThemeFont("font", "Label");
        var size = GetThemeFontSize("font_size", "Label");
        DrawSeasonWash(p);
        // The new branch's dotted bend goes under any branch that left the same save.
        if (NowLane is { IsUnsaved: true, OriginTick: { } originTick, Parent: { } parent } lane)
            Corner(SaveTimelineLayout.BranchColor(lane.ColorNumber), X(Calendar.Day(originTick)), LaneY(parent.Index), LaneY(lane.Index), dotted: true);
        DrawBranches();
        DrawNow(p, font, size);
        DrawSaves(p, font, size);
        DrawSeasonBar(p, font, size);
        if (HasFocus() && SelectedId is null)
            DrawRect(new Rect2(ViewLeft + 1, ViewTop + Ruler + 4, Size.X - ViewLeft - 2, Size.Y - ViewTop - Ruler - 5), p.Ember with { A = 0.5f }, filled: false, width: 1);
    }

    // ---- seasons ----------------------------------------------------------

    /// <summary>The days in view, a day either side.</summary>
    private IEnumerable<(long Day, int Season, int DayOfSeason, int Length, long Year)> Days()
    {
        var width = ViewWidth > 0 ? ViewWidth : Size.X;
        var start = (long)Math.Max(0, MathF.Floor(firstDay + (ViewLeft - 16) / PixelsPerDay) - 1);
        var end = (long)MathF.Ceiling(firstDay + (ViewLeft + width) / PixelsPerDay) + 1;
        for (var day = start; day <= end; day++)
        {
            var (season, dayOfSeason, length, year) = Calendar.DateOf(day);
            yield return (day, season, dayOfSeason, length, year);
        }
    }

    /// <summary>A faint wash of each season behind the rows, with a line where each season starts.</summary>
    private void DrawSeasonWash(UiPalette p)
    {
        var dark = p.Name == "dark";
        var top = Ruler + 4;
        foreach (var (day, season, dayOfSeason, _, _) in Days())
        {
            var left = X(day);
            DrawRect(new Rect2(left, top, X(day + 1) - left, Size.Y - top), SaveTimelineLayout.SeasonColor(season) with { A = dark ? 0.07f : 0.12f });
            // A line where each season starts, solid at a new year, but not at the timeline's left edge.
            if (dayOfSeason != 0 || day <= MathF.Floor(firstDay) + 1) continue;
            if (day % Calendar.DaysPerYear == 0) DrawRect(new Rect2(left, top, 2, Size.Y - top), p.InkFaint);
            else for (var y = top; y < Size.Y; y += 6) DrawRect(new Rect2(left, y, 1, 3), p.InkFaint with { A = 0.6f });
        }
    }

    /// <summary>
    /// The thin season band with a tick per day, longer where a season starts,
    /// each season's icon and name, and day numbers where there is room. It
    /// stays at the top while the rows scroll, and the season in view keeps its name.
    /// </summary>
    private void DrawSeasonBar(UiPalette p, Font font, int size)
    {
        var dark = p.Name == "dark";
        DrawRect(new Rect2(ViewLeft, ViewTop, Size.X - ViewLeft, Ruler + 3), p.Inset);
        if (PixelsPerDay <= 0) return;
        var every = PixelsPerDay >= 26 ? 1 : PixelsPerDay >= 10 ? 5 : 10;
        var days = Days().ToArray();
        var nameEnd = float.MinValue;
        foreach (var (day, season, dayOfSeason, length, year) in days)
        {
            var left = X(day);
            DrawRect(new Rect2(left, ViewTop, X(day + 1) - left, 6), SaveTimelineLayout.SeasonColor(season) with { A = dark ? 0.6f : 0.75f });
            DrawRect(new Rect2(left, ViewTop + 6, 1, dayOfSeason == 0 ? 8 : 4), dayOfSeason == 0 ? p.InkMuted : p.InkFaint);
            if (dayOfSeason == 0 || day == days[0].Day)
            {
                var name = SaveTimelineCalendar.SeasonName(season);
                var label = char.ToUpperInvariant(name[0]) + name[1..] +
                    (year > 1 ? $" · year {year.ToString(CultureInfo.InvariantCulture)}" : string.Empty);
                var width = 18 + font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X;
                // The season in view keeps its name at the left edge until the next season pushes it along.
                var seasonEnd = X(day - dayOfSeason + length);
                var at = Math.Min(Math.Max(left, ViewLeft), seasonEnd - width - 4);
                if (at >= left - 0.5f && at > nameEnd)
                {
                    DrawTexture(PixelIcons.Season(name, 1), new Vector2(at + 2, ViewTop + 13));
                    DrawString(font, new Vector2(at + 16, ViewTop + 24), label, HorizontalAlignment.Left, -1, size, p.Ink);
                    nameEnd = at + width + 2;
                }
            }
            // A day number keeps clear of the season's name, so it never reads as part of a date.
            else if ((dayOfSeason + 1) % every == 0 && left > nameEnd + 10 && (length - dayOfSeason) * PixelsPerDay > 34)
                DrawString(font, new Vector2(left + 2, ViewTop + 24), (dayOfSeason + 1).ToString(CultureInfo.InvariantCulture),
                    HorizontalAlignment.Left, -1, size, p.InkMuted);
        }
        DrawRect(new Rect2(ViewLeft, ViewTop + Ruler + 2, Size.X - ViewLeft, 1), p.Separator);
    }

    // ---- branch lines -------------------------------------------------------

    private void DrawBranches()
    {
        foreach (var lane in Lanes)
        {
            if (lane.IsUnsaved || lane.Points.Count == 0) continue;
            var color = SaveTimelineLayout.BranchColor(lane.ColorNumber);
            var y = LaneY(lane.Index);
            var startX = X(Calendar.Day(lane.Points[0].WorldTick));
            var endX = X(Calendar.Day(lane.Points[^1].WorldTick));
            if (lane.OriginTick is { } originTick)
            {
                var forkX = X(Calendar.Day(originTick));
                if (lane.Parent is { } parent)
                {
                    Corner(color, forkX, LaneY(parent.Index), y, dotted: false);
                    startX = forkX + Radius;
                }
                else startX = forkX;
            }
            if (endX > startX)
            {
                DrawRect(new Rect2(startX, y - 2, endX - startX, 4), color);
                DrawRect(new Rect2(startX, y + 2, endX - startX, 1), color.Darkened(0.3f));
            }
        }
    }

    /// <summary>A branch leaving its parent: straight down, then a stepped quarter-turn onto its own row.</summary>
    private void Corner(Color color, float forkX, float parentY, float y, bool dotted)
    {
        const int Thick = 4;
        const int Half = Thick / 2;
        var bottom = y - Radius;
        if (!dotted) DrawRect(new Rect2(forkX - Half, parentY, Thick, bottom - parentY), color);
        for (var top = parentY + 8; dotted && top < bottom; top += 7)
            DrawRect(new Rect2(forkX - Half, top, Thick, Math.Min(4, bottom - top)), color);
        var centerX = forkX + Radius;
        var centerY = y - Radius;
        for (var dy = 0; dy <= Radius + Half; dy++)
            for (var dx = -Radius - Half; dx <= 0; dx++)
            {
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance >= Radius - Half && distance < Radius + Half && (!dotted || (dy - dx) / 4 % 2 == 0))
                    DrawRect(new Rect2(centerX + dx, centerY + dy, 1, 1), color);
            }
    }

    // ---- the running world ----------------------------------------------

    /// <summary>Where the dotted line toward You are here starts.</summary>
    private float? NowLineStart()
    {
        if (NowLane is not { } lane) return null;
        if (lane.IsUnsaved)
            return X(Calendar.Day(lane.OriginTick ?? NowTick)) + (lane.Parent is not null ? Radius : 0);
        return lane.Points.Count > 0 ? X(Calendar.Day(lane.Points[^1].WorldTick)) + 10
            : X(Calendar.Day(lane.OriginTick ?? NowTick));
    }

    /// <summary>The You are here marker's centre, or null when the timeline marks nothing.</summary>
    public Vector2? NowPosition() => NowLineStart() is { } fromX && NowLane is { } lane
        ? new Vector2(Math.Max(X(Calendar.Day(NowTick)), fromX + 22), LaneY(lane.Index))
        : null;

    /// <summary>An orange camp marker at the end of a dotted line from where the world continues.</summary>
    private void DrawNow(UiPalette p, Font font, int size)
    {
        if (NowLane is not { } lane || NowLineStart() is not { } fromX || NowPosition() is not { } now) return;
        var color = SaveTimelineLayout.BranchColor(p, lane.ColorNumber);
        var y = now.Y;
        var nowX = now.X;
        for (var x = fromX; x < nowX - 9; x += 7) DrawRect(new Rect2(x, y - 1, 4, 3), color);
        var marker = Sprite("now", () =>
        {
            var image = Disc(17, p.EmberDark, p.Ember);
            var person = PixelIcons.Texture(PixelGlyph.Person, p.EmberInk, p.EmberInk, 1).GetImage();
            person.Convert(Image.Format.Rgba8);
            image.BlendRect(person, new Rect2I(0, 0, 12, 12), new Vector2I(3, 2));
            return image;
        });
        DrawTexture(marker, new Vector2(nowX - 8, y - 8));
        Tag(font, size, "You are here", new Vector2(nowX + 13, y - 8), p.Ember, p.EmberInk, p.EmberDark);
    }

    // ---- saves ------------------------------------------------------------

    private void DrawSaves(UiPalette p, Font font, int size)
    {
        (float X, float Y, int Half)? chosen = null;
        foreach (var lane in Lanes)
        {
            var color = SaveTimelineLayout.BranchColor(p, lane.ColorNumber);
            var y = LaneY(lane.Index);
            foreach (var save in lane.Points)
            {
                var x = X(Calendar.Day(save.WorldTick));
                var latest = lane.IsLatest(save);
                var selected = save.Id == SelectedId;
                var choosable = CanChoose?.Invoke(save) ?? true;
                if (choosable) hits.Add((new Rect2(x - 10, y - 12, 20, 24), save.Id));
                if (save.IsAutosave)
                {
                    DrawTexture(Sprite("autosave", () => Diamond(7, p.Ink, p.Paper)), new Vector2(x - 3, y - 3));
                    if (selected) chosen = (x, y, 7);
                    continue;
                }
                var point = Sprite($"point:{color.ToHtml()}:{latest}", () => latest
                    ? Disc(15, p.Ink, color, highlight: true)
                    : Ring(13, p.Ink, p.Paper, color));
                DrawTexture(point, new Vector2(x - point.GetWidth() / 2, y - point.GetHeight() / 2));
                if (latest) DrawBanner(this, p, color, x, y, Sprite);
                if (selected) chosen = (x, y, 12);

                var tag = tags[save.Id];
                var rect = Tag(font, size, tag.Text, tag.Area.Position,
                    selected ? p.Ember : p.Paper, selected ? p.EmberInk : p.Ink, selected ? p.EmberDark : p.PaperEdge);
                if (choosable) hits.Add((rect, save.Id));
            }
        }
        if (chosen is { } mark) Brackets(p, mark.X, mark.Y, mark.Half);
    }

    // ---- pieces -------------------------------------------------------------

    private Rect2 Tag(Font font, int size, string text, Vector2 at, Color fill, Color ink, Color edge)
    {
        var rect = TagRect(font, size, text, at);
        DrawRect(rect, fill);
        DrawRect(rect, edge, filled: false, width: 1);
        DrawRect(new Rect2(rect.Position.X + 1, rect.End.Y, rect.Size.X - 1, 1), edge with { A = 0.5f });
        DrawString(font, new Vector2(rect.Position.X + 5, rect.Position.Y + 13), text, HorizontalAlignment.Left, -1, size, ink);
        return rect;
    }

    private static Rect2 TagRect(Font font, int size, string text, Vector2 at) =>
        new(MathF.Floor(at.X), MathF.Floor(at.Y), MathF.Ceiling(font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X + 10), 17);

    /// <summary>Pixel corner marks around the chosen save.</summary>
    private void Brackets(UiPalette p, float x, float y, int half)
    {
        var reach = half + 3;
        foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var cx = x + sx * reach;
            var cy = y + sy * reach;
            DrawRect(new Rect2(sx < 0 ? cx : cx - 4, cy - (sy < 0 ? 0 : 1), 5, 2), p.Ember);
            DrawRect(new Rect2(cx - (sx < 0 ? 0 : 1), sy < 0 ? cy : cy - 4, 2, 5), p.Ember);
        }
    }

    private Texture2D Sprite(string key, Func<Image> make)
    {
        key = UiTheme.Current.Name + ":" + key;
        if (!sprites.TryGetValue(key, out var texture))
            sprites[key] = texture = ImageTexture.CreateFromImage(make());
        return texture;
    }

    /// <summary>The small hanging banner over each branch's newest save.</summary>
    internal static void DrawBanner(CanvasItem target, UiPalette p, Color color, float x, float y,
        Func<string, Func<Image>, Texture2D> sprite)
    {
        var banner = sprite($"banner:{color.ToHtml()}", () => Banner(p, color));
        target.DrawTexture(banner, new Vector2(x - 7, y - 7 - banner.GetHeight()));
    }

    /// <summary>A hanging standard: cloth on a crossbar with a forked hem, on a wooden pole with a brass knob.</summary>
    internal static Image Banner(UiPalette p, Color color)
    {
        const int Width = 14;
        const int Height = 21;
        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        var brass = new Color("E0B04E");
        var brassDark = new Color("8A6420");
        // Pole, lit from the left, with a brass knob on top; lighter wood on the dark theme so it shows.
        var dark = p.Name == "dark";
        var lit = dark ? p.WoodLight : p.Wood;
        var shade = dark ? p.Wood : p.WoodDark;
        for (var y = 2; y < Height; y++)
        {
            image.SetPixel(6, y, lit);
            image.SetPixel(7, y, shade);
        }
        image.SetPixel(6, 0, brass);
        image.SetPixel(7, 0, brass);
        image.SetPixel(6, 1, brass);
        image.SetPixel(7, 1, brassDark);
        // Crossbar with brass ends.
        for (var x = 1; x < Width - 1; x++) image.SetPixel(x, 3, shade);
        image.SetPixel(0, 3, brass);
        image.SetPixel(Width - 1, 3, brass);
        // Cloth: ten columns hanging eleven rows, its hem cut into two points.
        static bool Cloth(int column, int row)
        {
            if (column < 0 || column > 9 || row < 0 || row > 10) return false;
            var cut = row - 7; // the notch opens over the last three rows
            return cut < 1 || Math.Abs(column * 2 - 9) > cut * 2 - 1;
        }
        var edge = color.Darkened(0.45f);
        for (var row = 0; row <= 10; row++)
            for (var column = 0; column <= 9; column++)
            {
                if (!Cloth(column, row)) continue;
                var outline = !Cloth(column - 1, row) || !Cloth(column + 1, row) || !Cloth(column, row + 1);
                var fill = row == 0 ? color.Lightened(0.3f) : column == 1 ? color.Lightened(0.15f) : color;
                image.SetPixel(2 + column, 4 + row, outline ? edge : fill);
            }
        return image;
    }

    internal static Image Disc(int size, Color edge, Color fill, bool highlight = false)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var c = (size - 1) / 2f;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = MathF.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                if (d <= c + 0.2f) image.SetPixel(x, y, d > c - 1.4f ? edge : fill);
            }
        if (highlight)
        {
            image.SetPixel((int)c - 2, (int)c - 3, fill.Lightened(0.55f));
            image.SetPixel((int)c - 3, (int)c - 2, fill.Lightened(0.55f));
            image.SetPixel((int)c - 2, (int)c - 2, fill.Lightened(0.35f));
        }
        return image;
    }

    /// <summary>An open point with a dot of the branch's colour, so older saves still say which branch.</summary>
    internal static Image Ring(int size, Color edge, Color fill, Color accent)
    {
        var image = Disc(size, edge, fill);
        var c = (size - 1) / 2;
        for (var y = c - 1; y <= c + 1; y++)
            for (var x = c - 1; x <= c + 1; x++)
                if (Math.Abs(x - c) + Math.Abs(y - c) < 2) image.SetPixel(x, y, accent);
        return image;
    }

    internal static Image Diamond(int size, Color edge, Color fill)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var c = size / 2;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Math.Abs(x - c) + Math.Abs(y - c);
                if (d <= c) image.SetPixel(x, y, d == c ? edge : fill);
            }
        return image;
    }
}

/// <summary>Which point a key entry shows.</summary>
public enum SaveTimelineKeyKind
{
    Newest,
    Save,
    Autosave,
}

/// <summary>One point from the timeline, drawn alone for the key above it.</summary>
public partial class SaveTimelineKeyIcon : Control
{
    private readonly Dictionary<string, ImageTexture> sprites = new(StringComparer.Ordinal);

    private SaveTimelineKeyKind kind;

    public SaveTimelineKeyIcon()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        Kind = SaveTimelineKeyKind.Save;
    }

    public SaveTimelineKeyKind Kind
    {
        get => kind;
        set
        {
            kind = value;
            CustomMinimumSize = value switch
            {
                SaveTimelineKeyKind.Newest => new Vector2(16, 34),
                SaveTimelineKeyKind.Save => new Vector2(14, 16),
                _ => new Vector2(10, 16),
            };
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var p = UiTheme.Current;
        var color = SaveTimelineLayout.BranchColor(1);
        Texture2D Make(string key, Func<Image> make)
        {
            key = p.Name + ":" + key;
            if (!sprites.TryGetValue(key, out var texture)) sprites[key] = texture = ImageTexture.CreateFromImage(make());
            return texture;
        }
        var x = MathF.Floor(Size.X / 2);
        var y = Kind == SaveTimelineKeyKind.Newest ? Size.Y - 9 : MathF.Floor(Size.Y / 2);
        switch (Kind)
        {
            case SaveTimelineKeyKind.Autosave:
                DrawTexture(Make("autosave", () => SaveTimelineRows.Diamond(7, p.Ink, p.Paper)), new Vector2(x - 3, y - 3));
                break;
            case SaveTimelineKeyKind.Save:
                DrawTexture(Make("save", () => SaveTimelineRows.Ring(13, p.Ink, p.Paper, color)), new Vector2(x - 6, y - 6));
                break;
            default:
                DrawTexture(Make("newest", () => SaveTimelineRows.Disc(15, p.Ink, color, highlight: true)), new Vector2(x - 7, y - 7));
                SaveTimelineRows.DrawBanner(this, p, color, x, y, Make);
                break;
        }
    }
}
