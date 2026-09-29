using Godot;

namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Draws generated pixel art in 32-units-per-tile coordinates, scaled to the
/// target resolution, with alpha blending and clipping to one image cell.
/// </summary>
internal readonly struct PixelCanvas(Image image, Rect2I cell, float unit)
{
    public float Unit => unit;

    public void Dot(float x, float y, Color color) => Blend((int)(x * unit), (int)(y * unit), color);

    public void Disc(float centerX, float centerY, float radius, Color color) =>
        Ellipse(centerX, centerY, radius, radius, color);

    public void Ellipse(float centerX, float centerY, float radiusX, float radiusY, Color color)
    {
        var cx = centerX * unit;
        var cy = centerY * unit;
        var rx = Math.Max(0.6f, radiusX * unit);
        var ry = Math.Max(0.6f, radiusY * unit);
        for (var y = (int)(cy - ry - 1); y <= (int)(cy + ry + 1); y++)
            for (var x = (int)(cx - rx - 1); x <= (int)(cx + rx + 1); x++)
            {
                var dx = (x + 0.5f - cx) / rx;
                var dy = (y + 0.5f - cy) / ry;
                if (dx * dx + dy * dy <= 1f) Blend(x, y, color);
            }
    }

    public void Ring(float centerX, float centerY, float radius, Color color)
    {
        var cx = centerX * unit;
        var cy = centerY * unit;
        var r = radius * unit;
        for (var y = (int)(cy - r - 1); y <= (int)(cy + r + 1); y++)
            for (var x = (int)(cx - r - 1); x <= (int)(cx + r + 1); x++)
            {
                var distance = new Vector2(x + 0.5f - cx, y + 0.5f - cy).Length();
                if (Math.Abs(distance - r) <= 0.5f) Blend(x, y, color);
            }
    }

    /// <summary>A leafy canopy: a disc whose edge bulges in <paramref name="lobes"/> soft lumps.</summary>
    public void Lumpy(float centerX, float centerY, float radius, Color color, int lobes, int phase)
    {
        var cx = centerX * unit;
        var cy = centerY * unit;
        var r = radius * unit;
        for (var y = (int)(cy - r - 2); y <= (int)(cy + r + 2); y++)
            for (var x = (int)(cx - r - 2); x <= (int)(cx + r + 2); x++)
            {
                var offset = new Vector2(x + 0.5f - cx, y + 0.5f - cy);
                var edge = r * (0.9f + 0.1f * Mathf.Sin(offset.Angle() * lobes + phase));
                if (offset.Length() <= edge) Blend(x, y, color);
            }
    }

    /// <summary>A top-down conifer layer: a star with <paramref name="points"/> soft points.</summary>
    public void Star(float centerX, float centerY, float outer, float inner, Color color, int points)
    {
        var cx = centerX * unit;
        var cy = centerY * unit;
        var ro = outer * unit;
        var ri = inner * unit;
        for (var y = (int)(cy - ro - 1); y <= (int)(cy + ro + 1); y++)
            for (var x = (int)(cx - ro - 1); x <= (int)(cx + ro + 1); x++)
            {
                var offset = new Vector2(x + 0.5f - cx, y + 0.5f - cy);
                var wave = (Mathf.Cos(offset.Angle() * points) + 1) / 2;
                if (offset.Length() <= Mathf.Lerp(ri, ro, wave)) Blend(x, y, color);
            }
    }

    public void Line(float fromX, float fromY, float toX, float toY, Color color)
    {
        var from = new Vector2(fromX, fromY) * unit;
        var to = new Vector2(toX, toY) * unit;
        var steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) * 1.5f));
        for (var step = 0; step <= steps; step++)
        {
            var point = from.Lerp(to, step / (float)steps);
            Blend((int)point.X, (int)point.Y, color);
        }
    }

    public void Leaf(float centerX, float centerY, float length, float width, float angle, Color color, Color vein)
    {
        var cx = centerX * unit;
        var cy = centerY * unit;
        var halfLength = length * unit;
        var halfWidth = Math.Max(0.8f, width * unit);
        var axis = Vector2.FromAngle(angle);
        for (var y = (int)(cy - halfLength - 1); y <= (int)(cy + halfLength + 1); y++)
            for (var x = (int)(cx - halfLength - 1); x <= (int)(cx + halfLength + 1); x++)
            {
                var offset = new Vector2(x + 0.5f - cx, y + 0.5f - cy);
                var along = offset.Dot(axis) / halfLength;
                var across = offset.Dot(axis.Orthogonal()) / halfWidth;
                if (along * along + across * across <= 1f)
                    Blend(x, y, Math.Abs(offset.Dot(axis.Orthogonal())) < 0.5f && unit >= 1 ? vein : color);
            }
    }

    public void Fruit(float x, float y, Color color, Color highlight)
    {
        Disc(x, y, 1.3f, color);
        if (unit >= 1) Dot(x - 0.6f, y - 0.6f, highlight);
    }

    public void Boulder(float centerX, float centerY, float radius, Color dark, Color mid, Color light)
    {
        Disc(centerX, centerY, radius, dark);
        Disc(centerX - 0.5f, centerY - 0.5f, radius - 1, mid);
        Disc(centerX - radius * 0.35f, centerY - radius * 0.35f, radius * 0.4f, light);
    }

    public void Crystal(float x, float y, Color color, Color highlight)
    {
        Line(x - 1.5f, y, x, y - 3, highlight);
        Line(x, y - 3, x + 1.5f, y, color);
        Line(x + 1.5f, y, x, y + 1.5f, color);
        Line(x, y + 1.5f, x - 1.5f, y, highlight);
        Dot(x, y - 1, highlight);
    }

    /// <summary>A filled rectangle in sprite units, for roofs, planks and doors.</summary>
    public void Rect(float x, float y, float width, float height, Color color)
    {
        var left = (int)MathF.Round(x * unit);
        var top = (int)MathF.Round(y * unit);
        var right = (int)MathF.Round((x + width) * unit);
        var bottom = (int)MathF.Round((y + height) * unit);
        for (var py = top; py < Math.Max(bottom, top + 1); py++)
            for (var px = left; px < Math.Max(right, left + 1); px++)
                Blend(px, py, color);
    }

    /// <summary>
    /// A hipped roof or tent over a rectangle: each pixel takes the color of
    /// the nearest edge's face, so the ridge follows the long side.
    /// </summary>
    public void Hipped(float x, float y, float width, float height, Color north, Color east, Color south, Color west)
    {
        var left = (int)MathF.Round(x * unit);
        var top = (int)MathF.Round(y * unit);
        var right = (int)MathF.Round((x + width) * unit);
        var bottom = (int)MathF.Round((y + height) * unit);
        for (var py = top; py < bottom; py++)
            for (var px = left; px < right; px++)
            {
                var toNorth = py + 0.5f - top;
                var toSouth = bottom - py - 0.5f;
                var toWest = px + 0.5f - left;
                var toEast = right - px - 0.5f;
                var nearest = Math.Min(Math.Min(toNorth, toSouth), Math.Min(toWest, toEast));
                Blend(px, py, nearest == toNorth ? north : nearest == toSouth ? south : nearest == toWest ? west : east);
            }
    }

    private void Blend(int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= cell.Size.X || y >= cell.Size.Y) return;
        var target = cell.Position + new Vector2I(x, y);
        image.SetPixelv(target, image.GetPixelv(target).Blend(color));
    }
}
