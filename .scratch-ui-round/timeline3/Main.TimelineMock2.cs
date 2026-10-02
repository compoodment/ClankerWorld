#pragma warning disable CA1859, CA1822, CA1861, CA1305, CA1307, CA1310, CA1002, CA1805, CA2227, CA1854, CA1866, CA1865
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

// LOCAL ONLY - never commit. Second round of option C drawings for issue #680.

public enum TimelineStyle { Clean, Trails }
public enum FlagStyle { Pennant, Banner, Badge, Arrow }
public enum HeaderStyle { Original, None, Name, Key }

/// <summary>
/// Option C's timeline drawn again: crisp pixel points, thicker branch lines,
/// season shading behind the lanes, and names in small tags that move above
/// or below so they rarely need shortening.
/// </summary>
public partial class SaveTimelineView2 : Control
{
    public ManualWorldSave[] Saves { get; set; } = [];
    public string? SelectedId { get; set; }
    public string? PlayingBranchId { get; set; }
    public long NowTick { get; set; }
    public float PixelsPerDay { get; set; } = 60;
    public TimelineStyle Style { get; set; }
    public FlagStyle Flag { get; set; } = FlagStyle.Banner;
    public bool NamesOnly { get; set; }
    public float GutterWidth { get; set; } = 104;
    public event Action<string>? SaveChosen;

    private const float Ruler = 30;
    private const float LaneHeight = 58;
    private const float EndRoom = 150;
    private TimelineModel.Lane[] lanes = [];
    private float firstDay;
    private float lastDay;
    private readonly List<(Rect2 Area, string Id)> hits = [];
    private readonly Dictionary<string, ImageTexture> sprites = [];

    public void Refresh()
    {
        lanes = TimelineModel.Lanes(Saves);
        var days = Saves.Select(save => TimelineModel.Day(save.WorldTick)).Append(TimelineModel.Day(NowTick)).ToArray();
        firstDay = MathF.Floor(days.Min()) - 0.5f;
        lastDay = MathF.Ceiling(days.Max());
        var height = Ruler + 8 + lanes.Length * LaneHeight;
        CustomMinimumSize = NamesOnly ? new Vector2(GutterWidth, height) : new Vector2(24 + (lastDay - firstDay) * PixelsPerDay + EndRoom, height);
        sprites.Clear();
        QueueRedraw();
    }

    private float X(float day) => MathF.Floor(16 + (day - firstDay) * PixelsPerDay);
    private float Y(int lane) => MathF.Floor(Ruler + 8 + lane * LaneHeight + LaneHeight / 2);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            foreach (var (area, id) in hits)
                if (area.HasPoint(click.Position)) { SelectedId = id; SaveChosen?.Invoke(id); QueueRedraw(); return; }
    }

    public override void _Draw()
    {
        hits.Clear();
        var p = UiTheme.Current;
        var font = GetThemeFont("font", "Label");
        var size = GetThemeFontSize("font_size", "Label");
        if (NamesOnly)
        {
            DrawNames(p, font, size);
            return;
        }
        DrawSeasons(p, font, size);
        DrawBranches(p);
        DrawNow(p, font, size);
        DrawSaves(p, font, size);
    }

    // ---- names column -------------------------------------------------------

    private void DrawNames(UiPalette p, Font font, int size)
    {
        foreach (var lane in lanes)
        {
            var y = Y(lane.Index);
            var color = TimelineModel.LaneColor(lane);
            var number = lane.Branch?.Number ?? 1;
            if (Style == TimelineStyle.Trails)
            {
                // A pennant in the branch's colour on a short pole.
                DrawRect(new Rect2(8, y - 12, 2, 22), p.WoodDark);
                DrawColoredPolygon([new Vector2(10, y - 12), new Vector2(26, y - 8), new Vector2(10, y - 3)], color);
                DrawRect(new Rect2(10, y - 12, 1, 9), color.Lightened(0.3f));
            }
            else
            {
                var badge = new Rect2(6, y - 9, 18, 18);
                DrawRect(badge, color);
                DrawRect(badge, color.Darkened(0.35f), filled: false, width: 1);
                var digit = number.ToString();
                var w = font.GetStringSize(digit, HorizontalAlignment.Left, -1, size).X;
                DrawString(font, new Vector2(badge.Position.X + MathF.Floor((18 - w) / 2), y + 4), digit, HorizontalAlignment.Left, -1, size, p.Paper);
            }
            DrawString(font, new Vector2(32, y - 2), TimelineModel.LaneTitle(lane), HorizontalAlignment.Left, -1, size, p.Ink);
            var count = lane.Points.Count(save => !save.IsAutosave);
            DrawString(font, new Vector2(32, y + 12), count == 1 ? "1 save" : $"{count} saves", HorizontalAlignment.Left, -1, size, p.InkMuted);
        }
    }

    // ---- seasons --------------------------------------------------------------

    private void DrawSeasons(UiPalette p, Font font, int size)
    {
        var dark = p.Name == "dark";
        var top = Ruler + 4;
        var bottom = Size.Y;
        var start = (int)MathF.Floor(firstDay);
        var end = (int)MathF.Ceiling(lastDay + EndRoom / PixelsPerDay);
        var every = PixelsPerDay >= 26 ? 1 : PixelsPerDay >= 10 ? 5 : 10;
        var nameEnd = float.MinValue;
        for (var day = start; day <= end; day++)
        {
            var dayOfYear = ((day % TimelineModel.DaysPerYear) + TimelineModel.DaysPerYear) % TimelineModel.DaysPerYear;
            var season = dayOfYear / TimelineModel.DaysPerSeason;
            var dayOfSeason = dayOfYear % TimelineModel.DaysPerSeason + 1;
            var left = X(day);
            var right = X(day + 1);
            var tint = TimelineModel.SeasonColor(season);
            // The first drawing's ruler: a thin band in the season's colour with a tick per day,
            // longer where a season starts. A faint wash of the season sits behind the lanes.
            DrawRect(new Rect2(left, 0, right - left, 6), tint with { A = dark ? 0.6f : 0.75f });
            DrawRect(new Rect2(left, 6, 1, dayOfSeason == 1 ? 8 : 4), dayOfSeason == 1 ? p.InkMuted : p.InkFaint);
            DrawRect(new Rect2(left, top, right - left, bottom - top), tint with { A = dark ? 0.07f : 0.12f });
            if (dayOfSeason == 1 || day == start)
            {
                var year = day / TimelineModel.DaysPerYear + 1;
                if (day > start)
                {
                    if (dayOfYear == 0) DrawRect(new Rect2(left, top, 2, bottom - top), p.InkFaint);
                    else for (var y = top; y < bottom; y += 6) DrawRect(new Rect2(left, y, 1, 3), p.InkFaint with { A = 0.6f });
                }
                var name = char.ToUpperInvariant(TimelineModel.SeasonName(season)[0]) + TimelineModel.SeasonName(season)[1..] +
                    (year > 1 ? $" · year {year}" : string.Empty);
                // The first season's name stays in view even when its first day is off the left edge.
                var at = Math.Max(left, 0);
                DrawTexture(PixelIcons.Season(TimelineModel.SeasonName(season), 1), new Vector2(at + 2, 13));
                DrawString(font, new Vector2(at + 16, 24), name, HorizontalAlignment.Left, -1, size, p.Ink);
                nameEnd = at + 20 + font.GetStringSize(name, HorizontalAlignment.Left, -1, size).X;
            }
            else if (dayOfSeason % every == 0 && left + 2 > nameEnd &&
                (TimelineModel.DaysPerSeason - dayOfSeason + 1) * PixelsPerDay > 34)
                DrawString(font, new Vector2(left + 2, 24), dayOfSeason.ToString(), HorizontalAlignment.Left, -1, size, p.InkMuted);
        }
        DrawRect(new Rect2(0, Ruler + 2, Size.X, 1), p.Separator);
    }

    // ---- branch lines ---------------------------------------------------------

    private void DrawBranches(UiPalette p)
    {
        foreach (var lane in lanes)
        {
            var color = TimelineModel.LaneColor(lane);
            var y = Y(lane.Index);
            var startX = X(TimelineModel.Day(lane.Points[0].WorldTick));
            var endX = X(TimelineModel.Day(lane.Points[^1].WorldTick));
            if (lane.ForkSave is { } fork && lane.Parent is { } parent)
            {
                var fx = X(TimelineModel.Day(fork.WorldTick));
                var py = Y(parent.Index);
                Corner(p, color, fx, py, y);
                startX = fx + Radius;
            }
            Segment(p, color, startX, endX, y);
        }
    }

    private const int Radius = 10;

    /// <summary>One branch's run between two points.</summary>
    private void Segment(UiPalette p, Color color, float x0, float x1, float y)
    {
        if (x1 <= x0) return;
        if (Style == TimelineStyle.Trails)
        {
            // A packed-dirt trail like the map's roads, edged a shade darker, flecked with stones.
            var dirt = p.Name == "dark" ? new Color("8C7048") : new Color("CDAE78");
            var edge = p.Name == "dark" ? new Color("5E4A30") : new Color("9C7D4C");
            DrawRect(new Rect2(x0, y - 4, x1 - x0, 8), dirt);
            DrawRect(new Rect2(x0, y - 4, x1 - x0, 1), edge);
            DrawRect(new Rect2(x0, y + 3, x1 - x0, 1), edge);
            for (var x = x0 + 3; x < x1 - 2; x += 7)
            {
                var seed = (int)(x * 13 + y * 7) % 5;
                DrawRect(new Rect2(x, y - 2 + seed % 3, 1, 1), edge);
            }
            // The branch's colour runs along the trail's lower edge.
            DrawRect(new Rect2(x0, y + 4, x1 - x0, 2), color);
            return;
        }
        DrawRect(new Rect2(x0, y - 2, x1 - x0, 4), color);
        DrawRect(new Rect2(x0, y + 2, x1 - x0, 1), color.Darkened(0.3f));
    }

    /// <summary>A branch leaving its parent: straight down, then a stepped quarter-turn onto its own lane.</summary>
    private void Corner(UiPalette p, Color color, float fx, float py, float y)
    {
        var thick = Style == TimelineStyle.Trails ? 8 : 4;
        var half = thick / 2;
        var fill = Style == TimelineStyle.Trails ? (p.Name == "dark" ? new Color("8C7048") : new Color("CDAE78")) : color;
        DrawRect(new Rect2(fx - half, py, thick, y - py - Radius), fill);
        if (Style == TimelineStyle.Trails)
        {
            var edge = p.Name == "dark" ? new Color("5E4A30") : new Color("9C7D4C");
            DrawRect(new Rect2(fx - half, py + 4, 1, y - py - Radius - 4), edge);
            DrawRect(new Rect2(fx + half - 1, py + 4, 1, y - py - Radius - 4), edge);
            DrawRect(new Rect2(fx + half, py + 6, 2, y - py - Radius - 6), color);
        }
        // Pixel quarter ring from straight down to straight right.
        var cx = fx + Radius;
        var cy = y - Radius;
        for (var dy = 0; dy <= Radius + half; dy++)
            for (var dx = -Radius - half; dx <= 0; dx++)
            {
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance >= Radius - half && distance < Radius + half)
                    DrawRect(new Rect2(cx + dx, cy + dy, 1, 1), fill);
                else if (Style == TimelineStyle.Trails && distance >= Radius + half && distance < Radius + half + 2)
                    DrawRect(new Rect2(cx + dx, cy + dy, 1, 1), color);
            }
    }

    // ---- now ------------------------------------------------------------------

    private void DrawNow(UiPalette p, Font font, int size)
    {
        var lane = lanes.FirstOrDefault(item => item.Key == PlayingBranchId);
        if (lane is null) return;
        var color = TimelineModel.LaneColor(lane);
        var y = Y(lane.Index);
        var endX = X(TimelineModel.Day(lane.Points[^1].WorldTick));
        var nowX = X(TimelineModel.Day(NowTick));
        for (var x = endX + (Flag == FlagStyle.Arrow ? 22 : 10); x < nowX - 8; x += 7) DrawRect(new Rect2(x, y - 1, 4, 3), color);
        // The running world: a camp marker, with a tag above it.
        var marker = Sprite("now", () =>
        {
            var image = Disc(17, p.EmberDark, p.Ember);
            var person = PixelIcons.Texture(PixelGlyph.Person, p.EmberInk, p.EmberInk, 1).GetImage();
            person.Convert(Image.Format.Rgba8);
            image.BlendRect(person, new Rect2I(0, 0, 12, 12), new Vector2I(3, 2));
            return image;
        });
        DrawTexture(marker, new Vector2(nowX - 8, y - 8));
        Tag(p, font, size, "You are here", new Vector2(nowX + 13, y - 8), p.Ember, p.EmberInk, p.EmberDark);
    }

    // ---- saves ----------------------------------------------------------------

    private void DrawSaves(UiPalette p, Font font, int size)
    {
        var forkIds = lanes.Where(lane => lane.ForkSave is not null).Select(lane => lane.ForkSave!.Id).ToHashSet();
        (float X, float Y, int Half)? chosen = null;
        foreach (var lane in lanes)
        {
            var color = TimelineModel.LaneColor(lane);
            var y = Y(lane.Index);
            var aboveEnd = float.MinValue;
            var belowEnd = float.MinValue;
            foreach (var save in lane.Points)
            {
                var x = X(TimelineModel.Day(save.WorldTick));
                var latest = TimelineModel.IsLatest(lane, save);
                var selected = save.Id == SelectedId;
                hits.Add((new Rect2(x - 10, y - 12, 20, 24), save.Id));
                if (save.IsAutosave)
                {
                    var auto = Sprite($"auto:{color.ToHtml()}", () => Diamond(7, p.Ink, p.Paper));
                    DrawTexture(auto, new Vector2(x - 3, y - 3));
                    if (selected) chosen = (x, y, 7);
                    continue;
                }
                if (Style == TimelineStyle.Trails)
                {
                    // A signpost: a wooden post with a board capped in the branch's colour.
                    var post = Sprite($"post:{color.ToHtml()}:{latest}", () => Signpost(p, color, latest));
                    DrawTexture(post, new Vector2(x - 7, y - 16));
                }
                else
                {
                    var point = Sprite($"point:{color.ToHtml()}:{latest}", () => latest ? Disc(15, p.Ink, color, highlight: true) : Ring(13, p.Ink, p.Paper, color));
                    DrawTexture(point, new Vector2(x - point.GetWidth() / 2, y - point.GetHeight() / 2));
                    if (latest) DrawFlag(this, Flag, p, font, size, lane.Branch?.Number ?? 1, color, x, y, Sprite);
                }
                if (selected) chosen = (x, y, 12);

                // The name in a small tag: below the line first, above when that side is taken,
                // starting just past a branch that grows down from this point.
                var tagX = forkIds.Contains(save.Id) ? x + 8 : x - 6;
                bool Fits(float end, float from) => from > end + 4;
                var below = Fits(belowEnd, tagX);
                // Above the line, the newest save's name starts past its flag.
                var aboveX = latest && Flag != FlagStyle.Arrow ? x + (Flag == FlagStyle.Pennant ? 15 : 12) : tagX;
                var above = !below && Fits(aboveEnd, aboveX);
                if (above) tagX = aboveX;
                var text = save.Name;
                if (!below && !above)
                {
                    // Both sides taken: sit just after the tag below, still whole.
                    below = true;
                    tagX = belowEnd + 6;
                }
                var tagY = below ? y + (Style == TimelineStyle.Trails ? 10 : 9) : y - (Style == TimelineStyle.Trails ? 38 : 31);
                var rect = Tag(p, font, size, text, new Vector2(tagX, tagY),
                    selected ? p.Ember : p.Paper, selected ? p.EmberInk : p.Ink, selected ? p.EmberDark : p.PaperEdge);
                if (below) belowEnd = rect.End.X; else aboveEnd = rect.End.X;
            }
        }
        if (chosen is { } mark) Brackets(p, mark.X, mark.Y, mark.Half);
    }

    // ---- newest-save flags ------------------------------------------------------

    /// <summary>What marks each branch's newest save, in the style being compared.</summary>
    internal static void DrawFlag(CanvasItem target, FlagStyle flag, UiPalette p, Font font, int size, int number, Color color, float x, float y,
        Func<string, Func<Image>, Texture2D> sprite)
    {
        switch (flag)
        {
            case FlagStyle.Pennant:
                target.DrawRect(new Rect2(x, y - 22, 1, 15), p.Ink);
                target.DrawColoredPolygon([new Vector2(x + 1, y - 22), new Vector2(x + 11, y - 18), new Vector2(x + 1, y - 14)], color);
                break;
            case FlagStyle.Banner:
            {
                var banner = sprite($"banner:{color.ToHtml()}", () => Banner(p, color));
                target.DrawTexture(banner, new Vector2(x - 7, y - 7 - banner.GetHeight()));
                break;
            }
            case FlagStyle.Badge:
            {
                // The branch's numbered badge from the names column, on a short post.
                var badge = new Rect2(x - 6, y - 25, 13, 14);
                target.DrawRect(new Rect2(x, y - 11, 1, 4), p.Ink);
                target.DrawRect(badge, color.Darkened(0.4f));
                target.DrawRect(badge.Grow(-1), color);
                target.DrawRect(new Rect2(badge.Position.X + 1, badge.Position.Y + 1, badge.Size.X - 2, 1), color.Lightened(0.25f));
                var digit = number.ToString();
                var w = font.GetStringSize(digit, HorizontalAlignment.Left, -1, size).X;
                target.DrawString(font, new Vector2(badge.Position.X + MathF.Floor((badge.Size.X - w) / 2), badge.Position.Y + 12), digit, HorizontalAlignment.Left, -1, size, p.Paper);
                break;
            }
            case FlagStyle.Arrow:
            {
                // No flag: the branch line runs on past its newest save and ends in an arrowhead.
                target.DrawRect(new Rect2(x + 7, y - 2, 5, 4), color);
                target.DrawRect(new Rect2(x + 7, y + 2, 5, 1), color.Darkened(0.3f));
                var arrow = sprite($"arrow:{color.ToHtml()}", () => Arrow(color));
                target.DrawTexture(arrow, new Vector2(x + 11, y - 6));
                break;
            }
        }
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
        for (var y = 2; y < Height; y++) { image.SetPixel(6, y, lit); image.SetPixel(7, y, shade); }
        image.SetPixel(6, 0, brass); image.SetPixel(7, 0, brass);
        image.SetPixel(6, 1, brass); image.SetPixel(7, 1, brassDark);
        // Crossbar with brass ends.
        for (var x = 1; x < Width - 1; x++) image.SetPixel(x, 3, shade);
        image.SetPixel(0, 3, brass); image.SetPixel(Width - 1, 3, brass);
        // Cloth: ten columns hanging eleven rows, its hem cut into two points.
        bool Cloth(int c, int r)
        {
            if (c < 0 || c > 9 || r < 0 || r > 10) return false;
            var cut = r - 7; // the notch opens over the last three rows
            return cut < 1 || Math.Abs(c * 2 - 9) > cut * 2 - 1;
        }
        var edge = color.Darkened(0.45f);
        for (var r = 0; r <= 10; r++)
            for (var c = 0; c <= 9; c++)
            {
                if (!Cloth(c, r)) continue;
                var outline = !Cloth(c - 1, r) || !Cloth(c + 1, r) || !Cloth(c, r + 1);
                var fill = r == 0 ? color.Lightened(0.3f) : c == 1 ? color.Lightened(0.15f) : color;
                image.SetPixel(2 + c, 4 + r, outline ? edge : fill);
            }
        return image;
    }

    /// <summary>A pixel arrowhead pointing right, 12 pixels tall.</summary>
    internal static Image Arrow(Color color)
    {
        var image = Image.CreateEmpty(8, 12, false, Image.Format.Rgba8);
        var edge = color.Darkened(0.4f);
        for (var x = 0; x < 7; x++)
            for (var y = 0; y < 12; y++)
            {
                var reach = 6 - x;
                var inside = y >= 6 - reach - 1 && y <= 5 + reach;
                if (!inside) continue;
                var outline = y == 6 - reach - 1 || y == 5 + reach || x == 6;
                image.SetPixel(x, y, outline ? edge : color);
            }
        return image;
    }

    // ---- helpers --------------------------------------------------------------

    private Rect2 Tag(UiPalette p, Font font, int size, string text, Vector2 at, Color fill, Color ink, Color edge)
    {
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X + 10;
        var rect = new Rect2(MathF.Floor(at.X), MathF.Floor(at.Y), MathF.Ceiling(width), 17);
        DrawRect(rect, fill);
        DrawRect(rect, edge, filled: false, width: 1);
        DrawRect(new Rect2(rect.Position.X + 1, rect.End.Y, rect.Size.X - 1, 1), edge with { A = 0.5f });
        DrawString(font, new Vector2(rect.Position.X + 5, rect.Position.Y + 13), text, HorizontalAlignment.Left, -1, size, ink);
        return rect;
    }

    /// <summary>Pixel corner marks around the chosen save.</summary>
    private void Brackets(UiPalette p, float x, float y, int half)
    {
        var c = p.Ember;
        var r = half + 3;
        foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var cx = x + sx * r;
            var cy = y + sy * r;
            DrawRect(new Rect2(sx < 0 ? cx : cx - 4, cy - (sy < 0 ? 0 : 1), 5, 2), c);
            DrawRect(new Rect2(cx - (sx < 0 ? 0 : 1), sy < 0 ? cy : cy - 4, 2, 5), c);
        }
    }

    private Texture2D Sprite(string key, Func<Image> make)
    {
        key = UiTheme.Current.Name + ":" + key;
        if (!sprites.TryGetValue(key, out var texture))
            sprites[key] = texture = ImageTexture.CreateFromImage(make());
        return texture;
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

    internal static Image Ring(int size, Color edge, Color fill, Color accent)
    {
        var image = Disc(size, edge, fill);
        var c = (size - 1) / 2;
        // A dot of the branch's colour in the middle, so open points still say which branch.
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

    private static Image Signpost(UiPalette p, Color color, bool latest)
    {
        var image = Image.CreateEmpty(15, 22, false, Image.Format.Rgba8);
        // Post.
        for (var y = 8; y < 22; y++) { image.SetPixel(7, y, p.WoodDark); image.SetPixel(6, y, p.Wood); }
        // Board with a coloured cap.
        for (var y = 2; y < 9; y++)
            for (var x = 1; x < 14; x++)
            {
                var edge = x == 1 || x == 13 || y == 2 || y == 8;
                image.SetPixel(x, y, edge ? p.WoodEdge : y <= 4 ? color : p.WoodLight);
            }
        if (latest)
        {
            // A little flag on top of the newest post.
            for (var y = 0; y < 3; y++) image.SetPixel(7, y, p.WoodDark);
            image.SetPixel(8, 0, color); image.SetPixel(9, 0, color); image.SetPixel(8, 1, color);
        }
        return image;
    }
}

public enum KeyKind { Newest, Save, Autosave }

/// <summary>One point from the timeline, drawn alone for the key above it.</summary>
public partial class TimelineKeyIcon : Control
{
    public KeyKind Kind { get; set; }
    public FlagStyle Flag { get; set; }
    public Color Color { get; set; }

    public override void _Draw()
    {
        var p = UiTheme.Current;
        Texture2D Make(string key, Func<Image> make) => ImageTexture.CreateFromImage(make());
        var x = Kind == KeyKind.Newest && Flag == FlagStyle.Arrow ? 8 : MathF.Floor(Size.X / 2);
        var y = Kind == KeyKind.Newest && Flag is FlagStyle.Banner or FlagStyle.Badge or FlagStyle.Pennant ? Size.Y - 9 : MathF.Floor(Size.Y / 2);
        switch (Kind)
        {
            case KeyKind.Autosave:
                DrawTexture(Make("", () => SaveTimelineView2.Diamond(7, p.Ink, p.Paper)), new Vector2(x - 3, y - 3));
                break;
            case KeyKind.Save:
                DrawTexture(Make("", () => SaveTimelineView2.Ring(13, p.Ink, p.Paper, Color)), new Vector2(x - 6, y - 6));
                break;
            default:
                DrawTexture(Make("", () => SaveTimelineView2.Disc(15, p.Ink, Color, highlight: true)), new Vector2(x - 7, y - 7));
                SaveTimelineView2.DrawFlag(this, Flag, p, GetThemeFont("font", "Label"), GetThemeFontSize("font_size", "Label"), 1, Color, x, y, Make);
                break;
        }
    }
}

public partial class Main
{
    private async Task RunTimeline2(OwnerWorldSnapshot snapshot, string output)
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
            manualSaveCard.CustomMinimumSize = new Vector2(520, 0);
            manualSaveList.Visible = true;
            savesLabel.Visible = true;
            manualSaveStatus.Visible = true;
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

        var shots = new List<(string Name, FlagStyle Flag, HeaderStyle Header, bool Busy)>();
        foreach (var flag in new[] { FlagStyle.Banner, FlagStyle.Badge, FlagStyle.Arrow, FlagStyle.Pennant })
            foreach (var busy in new[] { false, true })
                shots.Add(($"flag-{flag.ToString().ToLowerInvariant()}{(busy ? "-busy" : "")}", flag, HeaderStyle.Key, busy));
        foreach (var header in new[] { HeaderStyle.Original, HeaderStyle.None, HeaderStyle.Name, HeaderStyle.Key })
            shots.Add(($"head-{header.ToString().ToLowerInvariant()}", FlagStyle.Banner, header, false));
        foreach (var (name, flag, header, busy) in shots)
            {
                await Shot(name, () =>
                {
                    string playing; long now;
                    var saves = busy ? TimelineBusy(out playing, out now) : TimelineSample(out playing, out now);
                    var selectedId = busy ? "c3" : "flood";
                    ShowManualSavePanel(true);
                    CancelManualSaveListRead();
                    allListedManualSaves = saves;
                    listedManualSaves = OrderSavesByBranch(saves);
                    RenderManualSaveList();
                    manualSaveCard.CustomMinimumSize = new Vector2(900, 0);
                    manualSaveStatus.Visible = false;
                    manualSaveList.Visible = false;
                    savesLabel.Visible = false;
                    var lanes = TimelineModel.Lanes(saves);
                    var switchRow = TimelineHeader(header, flag, lanes, saves);
                    SaveTimelineView2 Make(bool names) => new()
                    {
                        Saves = saves, PlayingBranchId = playing, NowTick = now, SelectedId = selectedId,
                        PixelsPerDay = busy ? 22 : 66, Style = TimelineStyle.Clean, Flag = flag, NamesOnly = names,
                    };
                    var namesView = Make(true);
                    var lines = Make(false);
                    namesView.Refresh();
                    lines.Refresh();
                    var row = new HBoxContainer();
                    row.AddThemeConstantOverride("separation", 0);
                    row.AddChild(namesView);
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
                    if (busy) Callable.From(() => scroll.ScrollHorizontal = (int)(lines.CustomMinimumSize.X * 0.3f)).CallDeferred();

                    // The chosen save, with a branch tag in its own colour and where its branch began.
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
                    var branchTag = new Label { Text = TimelineModel.LaneTitle(lane).ToUpperInvariant(), ThemeTypeVariation = "TagLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
                    var tagStyle = new StyleBoxFlat { BgColor = TimelineModel.LaneColor(lane), ContentMarginLeft = 5, ContentMarginRight = 5, ContentMarginTop = 2, ContentMarginBottom = 2 };
                    branchTag.AddThemeStyleboxOverride("normal", tagStyle);
                    branchTag.AddThemeColorOverride("font_color", UiTheme.Current.Paper);
                    titleRow.AddChild(branchTag);
                    if (latest) titleRow.AddChild(new Label { Text = "LATEST", ThemeTypeVariation = "TagNoteLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
                    text.AddChild(titleRow);
                    var origin = lane.ForkSave is { } fork ? $" · {TimelineModel.LaneTitle(lane)} began at {fork.Name}" : string.Empty;
                    text.AddChild(new Label { Text = $"{DisplayWorldClock(chosen.WorldTick)} · Saved {GameUiText.SavedAgo(chosen.CreatedUtc, DateTimeOffset.Now)}{origin}", ThemeTypeVariation = "DimLabel" });
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
                    if (header == HeaderStyle.None)
                    {
                        // No header row: the switch moves up beside the panel's title.
                        var views = (Control)switchRow.GetChild(switchRow.GetChildCount() - 1);
                        switchRow.RemoveChild(views);
                        switchRow.QueueFree();
                        views.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                        var headingRow = manualSaveHeading.GetParent();
                        var gap = new Control { CustomMinimumSize = new Vector2(10, 0) };
                        headingRow.AddChild(views);
                        headingRow.MoveChild(views, manualSaveBackButton.GetIndex());
                        headingRow.AddChild(gap);
                        headingRow.MoveChild(gap, manualSaveBackButton.GetIndex());
                        added.Add(views);
                        added.Add(gap);
                        Insert(frame, details);
                    }
                    else Insert(switchRow, frame, details);
                });
            }
        manualSaveOverlay.Hide();
    }

    /// <summary>The row above the timeline: the Timeline / List switch and one of the header choices.</summary>
    private static HBoxContainer TimelineHeader(HeaderStyle header, FlagStyle flag, TimelineModel.Lane[] lanes, ManualWorldSave[] saves)
    {
        var p = UiTheme.Current;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var green = TimelineModel.LaneColor(lanes[0]);
        switch (header)
        {
            case HeaderStyle.Original:
                row.AddChild(new Label
                {
                    Text = $"WILLOWMERE · {lanes.Length} BRANCHES · {saves.Count(save => !save.IsAutosave)} SAVES",
                    ThemeTypeVariation = "SectionLabel",
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                });
                break;
            case HeaderStyle.Name:
                row.AddChild(new TextureRect
                {
                    Texture = PixelIcons.Themed(PixelGlyph.Globe, green, 1),
                    StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                });
                row.AddChild(new Label { Text = "Willowmere", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
                break;
            case HeaderStyle.Key:
                void Item(KeyKind kind, string text, Vector2 size, bool gap = true)
                {
                    if (gap) row.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
                    row.AddChild(new TimelineKeyIcon { Kind = kind, Flag = flag, Color = green, CustomMinimumSize = size, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
                    row.AddChild(new Label { Text = text, ThemeTypeVariation = "DimLabel", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
                }
                var newest = flag switch
                {
                    FlagStyle.Arrow => new Vector2(28, 16),
                    FlagStyle.Badge => new Vector2(16, 34),
                    FlagStyle.Banner => new Vector2(16, 38),
                    _ => new Vector2(18, 34),
                };
                Item(KeyKind.Newest, "Newest on its branch", newest, gap: false);
                Item(KeyKind.Save, "Save", new Vector2(14, 16));
                Item(KeyKind.Autosave, "Autosave", new Vector2(10, 16));
                break;
        }
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var views = new SegmentedChoice { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        views.AddItem("Timeline");
        views.AddItem("List");
        views.Select(0);
        row.AddChild(views);
        return row;
    }
}
