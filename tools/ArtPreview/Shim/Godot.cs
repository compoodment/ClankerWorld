// A small stand-in for the parts of the Godot .NET API that the game's
// code-drawn art uses, so the real generator files compile and run in a plain
// .NET console program. Behaviour follows Godot 4's C# API: colours are
// floats, images store 8-bit RGBA, Blend is ordinary "over" compositing.
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Godot;

public enum Error { Ok, Failed }

public static class Mathf
{
    public const float Pi = MathF.PI;
    public const float Tau = MathF.PI * 2f;
    public const float Epsilon = 1e-6f;
    public static float Sin(float s) => MathF.Sin(s);
    public static float Cos(float s) => MathF.Cos(s);
    public static float Tan(float s) => MathF.Tan(s);
    public static float Atan(float s) => MathF.Atan(s);
    public static float Atan2(float y, float x) => MathF.Atan2(y, x);
    public static float Sqrt(float s) => MathF.Sqrt(s);
    public static float Pow(float x, float y) => MathF.Pow(x, y);
    public static float Exp(float s) => MathF.Exp(s);
    public static float Log(float s) => MathF.Log(s);
    public static float Abs(float s) => MathF.Abs(s);
    public static int Abs(int s) => Math.Abs(s);
    public static float Min(float a, float b) => MathF.Min(a, b);
    public static int Min(int a, int b) => Math.Min(a, b);
    public static float Max(float a, float b) => MathF.Max(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
    public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
    public static int Clamp(int v, int min, int max) => Math.Clamp(v, min, max);
    public static float Floor(float s) => MathF.Floor(s);
    public static float Ceil(float s) => MathF.Ceiling(s);
    public static float Round(float s) => MathF.Round(s);
    public static int FloorToInt(float s) => (int)MathF.Floor(s);
    public static int CeilToInt(float s) => (int)MathF.Ceiling(s);
    public static int RoundToInt(float s) => (int)MathF.Round(s);
    public static float Lerp(float from, float to, float weight) => from + (to - from) * weight;
    public static float InverseLerp(float from, float to, float weight) => (weight - from) / (to - from);
    public static float Remap(float value, float inFrom, float inTo, float outFrom, float outTo) =>
        Lerp(outFrom, outTo, InverseLerp(inFrom, inTo, value));
    public static float Sign(float s) => MathF.Sign(s);
    public static int Sign(int s) => Math.Sign(s);
    public static float SmoothStep(float from, float to, float weight)
    {
        if (IsEqualApprox(from, to)) return from;
        var x = Math.Clamp((weight - from) / (to - from), 0f, 1f);
        return x * x * (3f - 2f * x);
    }
    public static bool IsEqualApprox(float a, float b) => a == b || MathF.Abs(a - b) < Epsilon;
    public static bool IsZeroApprox(float s) => MathF.Abs(s) < Epsilon;
    public static float DegToRad(float deg) => deg * Pi / 180f;
    public static float RadToDeg(float rad) => rad * 180f / Pi;
    public static float Snapped(float s, float step) => step != 0 ? MathF.Floor(s / step + 0.5f) * step : s;
    public static float Wrap(float value, float min, float max)
    {
        var range = max - min;
        return IsZeroApprox(range) ? min : min + ((value - min) % range + range) % range;
    }
    public static int Wrap(int value, int min, int max)
    {
        var range = max - min;
        return range == 0 ? min : min + ((value - min) % range + range) % range;
    }
    public static float PosMod(float a, float b)
    {
        var c = a % b;
        if ((c < 0 && b > 0) || (c > 0 && b < 0)) c += b;
        return c;
    }
    public static int PosMod(int a, int b)
    {
        var c = a % b;
        if ((c < 0 && b > 0) || (c > 0 && b < 0)) c += b;
        return c;
    }
}

public struct Vector2 : IEquatable<Vector2>
{
    public float X;
    public float Y;
    public Vector2(float x, float y) { X = x; Y = y; }
    public static Vector2 Zero => new(0, 0);
    public static Vector2 One => new(1, 1);
    public static Vector2 Up => new(0, -1);
    public static Vector2 Down => new(0, 1);
    public static Vector2 Left => new(-1, 0);
    public static Vector2 Right => new(1, 0);
    public readonly float Length() => MathF.Sqrt(X * X + Y * Y);
    public readonly float LengthSquared() => X * X + Y * Y;
    public readonly float DistanceTo(Vector2 to) => (to - this).Length();
    public readonly float DistanceSquaredTo(Vector2 to) => (to - this).LengthSquared();
    public readonly Vector2 Lerp(Vector2 to, float weight) => new(Mathf.Lerp(X, to.X, weight), Mathf.Lerp(Y, to.Y, weight));
    public readonly float Angle() => MathF.Atan2(Y, X);
    public readonly float AngleTo(Vector2 to) => MathF.Atan2(Cross(to), Dot(to));
    public readonly float Dot(Vector2 with) => X * with.X + Y * with.Y;
    public readonly float Cross(Vector2 with) => X * with.Y - Y * with.X;
    public readonly Vector2 Orthogonal() => new(Y, -X);
    public readonly Vector2 Normalized()
    {
        var length = Length();
        return length == 0 ? Zero : new(X / length, Y / length);
    }
    public readonly Vector2 Rotated(float angle)
    {
        var (sin, cos) = MathF.SinCos(angle);
        return new(X * cos - Y * sin, X * sin + Y * cos);
    }
    public readonly Vector2 Round() => new(MathF.Round(X), MathF.Round(Y));
    public readonly Vector2 Floor() => new(MathF.Floor(X), MathF.Floor(Y));
    public readonly Vector2 Ceil() => new(MathF.Ceiling(X), MathF.Ceiling(Y));
    public readonly Vector2 Abs() => new(MathF.Abs(X), MathF.Abs(Y));
    public readonly Vector2 Clamp(Vector2 min, Vector2 max) => new(Math.Clamp(X, min.X, max.X), Math.Clamp(Y, min.Y, max.Y));
    public static Vector2 FromAngle(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator -(Vector2 a) => new(-a.X, -a.Y);
    public static Vector2 operator *(Vector2 a, float s) => new(a.X * s, a.Y * s);
    public static Vector2 operator *(float s, Vector2 a) => new(a.X * s, a.Y * s);
    public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.X * b.X, a.Y * b.Y);
    public static Vector2 operator /(Vector2 a, float s) => new(a.X / s, a.Y / s);
    public static Vector2 operator /(Vector2 a, Vector2 b) => new(a.X / b.X, a.Y / b.Y);
    public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
    public static explicit operator Vector2I(Vector2 v) => new((int)v.X, (int)v.Y);
    public readonly bool Equals(Vector2 other) => this == other;
    public override readonly bool Equals(object? obj) => obj is Vector2 v && Equals(v);
    public override readonly int GetHashCode() => HashCode.Combine(X, Y);
    public override readonly string ToString() => $"({X}, {Y})";
}

public struct Vector2I : IEquatable<Vector2I>
{
    public int X;
    public int Y;
    public Vector2I(int x, int y) { X = x; Y = y; }
    public static Vector2I Zero => new(0, 0);
    public static Vector2I One => new(1, 1);
    public readonly float Length() => MathF.Sqrt(X * X + Y * Y);
    public readonly int LengthSquared() => X * X + Y * Y;
    public readonly Vector2I Abs() => new(Math.Abs(X), Math.Abs(Y));
    public readonly Vector2I Sign() => new(Math.Sign(X), Math.Sign(Y));
    public readonly Vector2I Clamp(Vector2I min, Vector2I max) => new(Math.Clamp(X, min.X, max.X), Math.Clamp(Y, min.Y, max.Y));
    public static Vector2I operator +(Vector2I a, Vector2I b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2I operator -(Vector2I a, Vector2I b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2I operator -(Vector2I a) => new(-a.X, -a.Y);
    public static Vector2I operator *(Vector2I a, int s) => new(a.X * s, a.Y * s);
    public static Vector2I operator *(int s, Vector2I a) => new(a.X * s, a.Y * s);
    public static Vector2I operator *(Vector2I a, Vector2I b) => new(a.X * b.X, a.Y * b.Y);
    public static Vector2I operator /(Vector2I a, int s) => new(a.X / s, a.Y / s);
    public static Vector2I operator %(Vector2I a, int s) => new(a.X % s, a.Y % s);
    public static bool operator ==(Vector2I a, Vector2I b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Vector2I a, Vector2I b) => !(a == b);
    public static implicit operator Vector2(Vector2I v) => new(v.X, v.Y);
    public readonly bool Equals(Vector2I other) => this == other;
    public override readonly bool Equals(object? obj) => obj is Vector2I v && Equals(v);
    public override readonly int GetHashCode() => HashCode.Combine(X, Y);
    public override readonly string ToString() => $"({X}, {Y})";
}

public struct Rect2 : IEquatable<Rect2>
{
    public Vector2 Position;
    public Vector2 Size;
    public Rect2(Vector2 position, Vector2 size) { Position = position; Size = size; }
    public Rect2(float x, float y, float width, float height) : this(new Vector2(x, y), new Vector2(width, height)) { }
    public readonly Vector2 End => Position + Size;
    public readonly float Area => Size.X * Size.Y;
    public readonly bool HasPoint(Vector2 point) =>
        point.X >= Position.X && point.Y >= Position.Y && point.X < End.X && point.Y < End.Y;
    public readonly Rect2 Grow(float by) => new(Position - new Vector2(by, by), Size + new Vector2(by * 2, by * 2));
    public readonly Rect2 Intersection(Rect2 other)
    {
        var left = Math.Max(Position.X, other.Position.X);
        var top = Math.Max(Position.Y, other.Position.Y);
        var right = Math.Min(End.X, other.End.X);
        var bottom = Math.Min(End.Y, other.End.Y);
        return new Rect2(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
    public readonly bool Intersects(Rect2 other) =>
        Position.X < other.End.X && End.X > other.Position.X && Position.Y < other.End.Y && End.Y > other.Position.Y;
    public readonly bool Equals(Rect2 other) => Position == other.Position && Size == other.Size;
    public override readonly bool Equals(object? obj) => obj is Rect2 r && Equals(r);
    public override readonly int GetHashCode() => HashCode.Combine(Position, Size);
    public static bool operator ==(Rect2 a, Rect2 b) => a.Equals(b);
    public static bool operator !=(Rect2 a, Rect2 b) => !a.Equals(b);
    public static explicit operator Rect2I(Rect2 r) => new((Vector2I)r.Position, (Vector2I)r.Size);
}

public struct Rect2I : IEquatable<Rect2I>
{
    public Vector2I Position;
    public Vector2I Size;
    public Rect2I(Vector2I position, Vector2I size) { Position = position; Size = size; }
    public Rect2I(int x, int y, int width, int height) : this(new Vector2I(x, y), new Vector2I(width, height)) { }
    public readonly Vector2I End => Position + Size;
    public readonly int Area => Size.X * Size.Y;
    public readonly bool HasPoint(Vector2I point) =>
        point.X >= Position.X && point.Y >= Position.Y && point.X < End.X && point.Y < End.Y;
    public readonly bool Intersects(Rect2I other, bool includeBorders = false) => includeBorders
        ? !(Position.X > other.End.X || End.X < other.Position.X || Position.Y > other.End.Y || End.Y < other.Position.Y)
        : !(Position.X >= other.End.X || End.X <= other.Position.X || Position.Y >= other.End.Y || End.Y <= other.Position.Y);
    public readonly bool Encloses(Rect2I other) =>
        other.Position.X >= Position.X && other.Position.Y >= Position.Y && other.End.X <= End.X && other.End.Y <= End.Y;
    public readonly Rect2I Intersection(Rect2I other)
    {
        var left = Math.Max(Position.X, other.Position.X);
        var top = Math.Max(Position.Y, other.Position.Y);
        var right = Math.Min(End.X, other.End.X);
        var bottom = Math.Min(End.Y, other.End.Y);
        return right <= left || bottom <= top ? new(0, 0, 0, 0) : new(left, top, right - left, bottom - top);
    }
    public readonly Rect2I Grow(int by) => new(Position - new Vector2I(by, by), Size + new Vector2I(by * 2, by * 2));
    public readonly bool Equals(Rect2I other) => Position == other.Position && Size == other.Size;
    public override readonly bool Equals(object? obj) => obj is Rect2I r && Equals(r);
    public override readonly int GetHashCode() => HashCode.Combine(Position, Size);
    public static bool operator ==(Rect2I a, Rect2I b) => a.Equals(b);
    public static bool operator !=(Rect2I a, Rect2I b) => !a.Equals(b);
    public static implicit operator Rect2(Rect2I r) => new(r.Position, r.Size);
}

public struct Color : IEquatable<Color>
{
    public float R;
    public float G;
    public float B;
    public float A;

    public Color(float r, float g, float b, float a = 1f) { R = r; G = g; B = b; A = a; }
    public Color(Color c, float alpha) { R = c.R; G = c.G; B = c.B; A = alpha; }
    public Color(string code) { this = FromHtml(code); }
    public Color(string code, float alpha) { this = FromHtml(code); A = alpha; }

    public static Color FromHtml(string code)
    {
        var hex = code.StartsWith('#') ? code[1..] : code;
        if (hex.Length == 3 || hex.Length == 4)
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length is not (6 or 8)) throw new ArgumentException($"Invalid colour code '{code}'.");
        float Channel(int index) => int.Parse(hex.AsSpan(index, 2), NumberStyles.HexNumber) / 255f;
        return new(Channel(0), Channel(2), Channel(4), hex.Length == 8 ? Channel(6) : 1f);
    }

    public static Color Color8(byte r, byte g, byte b, byte a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    public static Color FromHsv(float hue, float saturation, float value, float alpha = 1f)
    {
        if (saturation == 0) return new(value, value, value, alpha);
        hue *= 6f;
        hue %= 6f;
        var i = (int)hue;
        var f = hue - i;
        var p = value * (1 - saturation);
        var q = value * (1 - saturation * f);
        var t = value * (1 - saturation * (1 - f));
        return i switch
        {
            0 => new(value, t, p, alpha),
            1 => new(q, value, p, alpha),
            2 => new(p, value, t, alpha),
            3 => new(p, q, value, alpha),
            4 => new(t, p, value, alpha),
            _ => new(value, p, q, alpha),
        };
    }

    public readonly float Luminance => 0.2126f * R + 0.7152f * G + 0.0722f * B;

    public readonly Color Lightened(float amount) =>
        new(R + (1f - R) * amount, G + (1f - G) * amount, B + (1f - B) * amount, A);

    public readonly Color Darkened(float amount) =>
        new(R * (1f - amount), G * (1f - amount), B * (1f - amount), A);

    public readonly Color Lerp(Color to, float weight) => new(
        Mathf.Lerp(R, to.R, weight), Mathf.Lerp(G, to.G, weight), Mathf.Lerp(B, to.B, weight), Mathf.Lerp(A, to.A, weight));

    public readonly Color Blend(Color over)
    {
        var sa = 1f - over.A;
        var alpha = A * sa + over.A;
        if (alpha == 0) return new(0, 0, 0, 0);
        return new(
            (R * A * sa + over.R * over.A) / alpha,
            (G * A * sa + over.G * over.A) / alpha,
            (B * A * sa + over.B * over.A) / alpha,
            alpha);
    }

    public readonly Color Inverted() => new(1f - R, 1f - G, 1f - B, A);
    public readonly Color Clamp() => new(Math.Clamp(R, 0, 1), Math.Clamp(G, 0, 1), Math.Clamp(B, 0, 1), Math.Clamp(A, 0, 1));

    public readonly string ToHtml(bool includeAlpha = true)
    {
        static string Hex(float v) => ((int)Math.Round(Math.Clamp(v, 0f, 1f) * 255f)).ToString("x2");
        return Hex(R) + Hex(G) + Hex(B) + (includeAlpha ? Hex(A) : string.Empty);
    }

    public static Color operator *(Color c, float s) => new(c.R * s, c.G * s, c.B * s, c.A * s);
    public static Color operator *(float s, Color c) => c * s;
    public static Color operator *(Color a, Color b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);
    public static Color operator /(Color c, float s) => new(c.R / s, c.G / s, c.B / s, c.A / s);
    public static Color operator +(Color a, Color b) => new(a.R + b.R, a.G + b.G, a.B + b.B, a.A + b.A);
    public static Color operator -(Color a, Color b) => new(a.R - b.R, a.G - b.G, a.B - b.B, a.A - b.A);
    public static bool operator ==(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
    public static bool operator !=(Color a, Color b) => !(a == b);
    public readonly bool Equals(Color other) => this == other;
    public override readonly bool Equals(object? obj) => obj is Color c && Equals(c);
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);
    public override readonly string ToString() => $"({R}, {G}, {B}, {A})";
}

public static class Colors
{
    public static Color Transparent => new(1, 1, 1, 0);
    public static Color White => new(1, 1, 1, 1);
    public static Color Black => new(0, 0, 0, 1);
    public static Color Red => new(1, 0, 0, 1);
    public static Color Green => new(0, 1, 0, 1);
    public static Color Blue => new(0, 0, 1, 1);
    public static Color Yellow => new(1, 1, 0, 1);
    public static Color Gray => new(0.745f, 0.745f, 0.745f, 1);
    public static Color DimGray => new(0.412f, 0.412f, 0.412f, 1);
}

/// <summary>8-bit RGBA raster with Godot's Image surface; other formats are stored as RGBA too.</summary>
public class Image : IDisposable
{
    // Native Godot images own unmanaged data; this stand-in owns only managed bytes.
    public void Dispose() { }

    public enum Format { L8, La8, R8, Rg8, Rgb8, Rgba8, Rgba4444, Rgb565, Rf, Rgf, Rgbf, Rgbaf, Rh, Rgh, Rgbh, Rgbah, Rgbe9995 }
    public enum Interpolation { Nearest, Bilinear, Cubic, Trilinear, Lanczos }

    private int width;
    private int height;
    private Format format;
    private byte[] data;

    private Image(int width, int height, Format format, byte[] data)
    {
        this.width = width;
        this.height = height;
        this.format = format;
        this.data = data;
    }

    public static Image CreateEmpty(int width, int height, bool useMipmaps, Format format) =>
        new(width, height, format, new byte[width * height * 4]);

    public static Image Create(int width, int height, bool useMipmaps, Format format) =>
        CreateEmpty(width, height, useMipmaps, format);

    /// <summary>Accepts RGBA8, RGB8, LA8 and L8 data, converting each to RGBA.</summary>
    public static Image CreateFromData(int width, int height, bool useMipmaps, Format format, byte[] source)
    {
        var image = CreateEmpty(width, height, useMipmaps, format);
        var channels = format switch { Format.Rgba8 => 4, Format.Rgb8 => 3, Format.La8 => 2, Format.L8 => 1, _ => throw new NotSupportedException(format.ToString()) };
        for (var i = 0; i < width * height; i++)
        {
            var o = i * channels;
            var (r, g, b, a) = channels switch
            {
                4 => (source[o], source[o + 1], source[o + 2], source[o + 3]),
                3 => (source[o], source[o + 1], source[o + 2], (byte)255),
                2 => (source[o], source[o], source[o], source[o + 1]),
                _ => (source[o], source[o], source[o], (byte)255),
            };
            image.data[i * 4] = r;
            image.data[i * 4 + 1] = g;
            image.data[i * 4 + 2] = b;
            image.data[i * 4 + 3] = a;
        }
        return image;
    }

    public int GetWidth() => width;
    public int GetHeight() => height;
    public Vector2I GetSize() => new(width, height);
    public Format GetFormat() => format;
    public bool IsEmpty() => width == 0 || height == 0;
    public byte[] GetData() => (byte[])data.Clone();
    public bool HasMipmaps() => false;

    private static byte Quantize(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    public void SetPixel(int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) throw new ArgumentOutOfRangeException($"({x}, {y}) outside {width}x{height}");
        var o = (y * width + x) * 4;
        data[o] = Quantize(color.R);
        data[o + 1] = Quantize(color.G);
        data[o + 2] = Quantize(color.B);
        data[o + 3] = Quantize(color.A);
    }

    public void SetPixelv(Vector2I point, Color color) => SetPixel(point.X, point.Y, color);

    public Color GetPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) throw new ArgumentOutOfRangeException($"({x}, {y}) outside {width}x{height}");
        var o = (y * width + x) * 4;
        return new(data[o] / 255f, data[o + 1] / 255f, data[o + 2] / 255f, data[o + 3] / 255f);
    }

    public Color GetPixelv(Vector2I point) => GetPixel(point.X, point.Y);

    public void Fill(Color color)
    {
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                SetPixel(x, y, color);
    }

    public void FillRect(Rect2I rect, Color color)
    {
        var area = rect.Intersection(new Rect2I(0, 0, width, height));
        for (var y = area.Position.Y; y < area.End.Y; y++)
            for (var x = area.Position.X; x < area.End.X; x++)
                SetPixel(x, y, color);
    }

    public Image GetRegion(Rect2I region)
    {
        var result = CreateEmpty(region.Size.X, region.Size.Y, false, format);
        for (var y = 0; y < region.Size.Y; y++)
            for (var x = 0; x < region.Size.X; x++)
            {
                var sx = region.Position.X + x;
                var sy = region.Position.Y + y;
                if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                Array.Copy(data, (sy * width + sx) * 4, result.data, (y * region.Size.X + x) * 4, 4);
            }
        return result;
    }

    public void BlitRect(Image source, Rect2I sourceRect, Vector2I destination)
    {
        for (var y = 0; y < sourceRect.Size.Y; y++)
            for (var x = 0; x < sourceRect.Size.X; x++)
            {
                var sx = sourceRect.Position.X + x;
                var sy = sourceRect.Position.Y + y;
                var dx = destination.X + x;
                var dy = destination.Y + y;
                if (sx < 0 || sy < 0 || sx >= source.width || sy >= source.height) continue;
                if (dx < 0 || dy < 0 || dx >= width || dy >= height) continue;
                SetPixel(dx, dy, source.GetPixel(sx, sy));
            }
    }

    public void BlendRect(Image source, Rect2I sourceRect, Vector2I destination)
    {
        for (var y = 0; y < sourceRect.Size.Y; y++)
            for (var x = 0; x < sourceRect.Size.X; x++)
            {
                var sx = sourceRect.Position.X + x;
                var sy = sourceRect.Position.Y + y;
                var dx = destination.X + x;
                var dy = destination.Y + y;
                if (sx < 0 || sy < 0 || sx >= source.width || sy >= source.height) continue;
                if (dx < 0 || dy < 0 || dx >= width || dy >= height) continue;
                SetPixel(dx, dy, GetPixel(dx, dy).Blend(source.GetPixel(sx, sy)));
            }
    }

    /// <summary>Nearest-neighbour resize; bilinear is approximated by nearest so pixel art stays crisp.</summary>
    public void Resize(int newWidth, int newHeight, Interpolation interpolation = Interpolation.Bilinear)
    {
        var result = new byte[newWidth * newHeight * 4];
        for (var y = 0; y < newHeight; y++)
            for (var x = 0; x < newWidth; x++)
            {
                var sx = Math.Min(width - 1, x * width / newWidth);
                var sy = Math.Min(height - 1, y * height / newHeight);
                Array.Copy(data, (sy * width + sx) * 4, result, (y * newWidth + x) * 4, 4);
            }
        data = result;
        width = newWidth;
        height = newHeight;
    }

    public void Crop(int newWidth, int newHeight)
    {
        var region = GetRegion(new Rect2I(0, 0, newWidth, newHeight));
        data = region.data;
        width = newWidth;
        height = newHeight;
    }

    public void FlipX()
    {
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width / 2; x++)
            {
                var a = GetPixel(x, y);
                var b = GetPixel(width - 1 - x, y);
                SetPixel(x, y, b);
                SetPixel(width - 1 - x, y, a);
            }
    }

    public void FlipY()
    {
        for (var y = 0; y < height / 2; y++)
            for (var x = 0; x < width; x++)
            {
                var a = GetPixel(x, y);
                var b = GetPixel(x, height - 1 - y);
                SetPixel(x, y, b);
                SetPixel(x, height - 1 - y, a);
            }
    }

    public Image Duplicate() => new(width, height, format, (byte[])data.Clone());

    public byte[] SavePngToBuffer() => PngCodec.Encode(this);

    public Error SavePng(string path)
    {
        File.WriteAllBytes(path, SavePngToBuffer());
        return Error.Ok;
    }
}

public abstract class Texture2D
{
    public abstract int GetWidth();
    public abstract int GetHeight();
}

public class ImageTexture : Texture2D
{
    private Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
    public static ImageTexture CreateFromImage(Image image) => new() { image = image };
    public Image GetImage() => image;
    public void Update(Image image) => this.image = image;
    public override int GetWidth() => image.GetWidth();
    public override int GetHeight() => image.GetHeight();
}

/// <summary>Minimal PNG writer: 8-bit RGBA, no filtering, zlib via the framework.</summary>
public static class PngCodec
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in bytes) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    public static byte[] Encode(Image image)
    {
        var width = image.GetWidth();
        var height = image.GetHeight();
        var data = image.GetData();
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            raw[y * (width * 4 + 1)] = 0;
            Array.Copy(data, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        }
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, true))
            zlib.Write(raw);
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream output, string type, byte[] payload)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)payload.Length);
        output.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var body = new byte[typeBytes.Length + payload.Length];
        typeBytes.CopyTo(body, 0);
        payload.CopyTo(body, typeBytes.Length);
        output.Write(body);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc(body));
        output.Write(crc);
    }
}
