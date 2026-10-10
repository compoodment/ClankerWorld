using Godot;

namespace ClankerWorld.GodotClient.UI;

internal readonly record struct AutumnLeaf(int X, int Y, int Size, Color Color);

/// <summary>The approved sparse leaves A from the October 9 visual polish review.</summary>
internal static class AutumnLeaves
{
    public const int Count = 14;
    private static readonly Color[] Colors = [new("C9762E"), new("A8552A"), new("D9A23F"), new("8A5A2B")];

    public static bool FallsFrom(NatureSprite? sprite) =>
        sprite is NatureSprite.Broadleaf or NatureSprite.OrchardFruiting or NatureSprite.OrchardPicked;

    public static AutumnLeaf At(int treeX, int treeY, int index, int size)
    {
        var angle = Hash01(treeX * 97 + index, treeY, 17) * MathF.Tau;
        var radius = MathF.Sqrt(Hash01(treeX, treeY * 97 + index, 19)) * 0.75f * size;
        var x = (int)((treeX + 0.55f) * size + MathF.Cos(angle) * radius * 1.15f + size * 0.08f);
        var y = (int)((treeY + 0.6f) * size + MathF.Sin(angle) * radius * 0.8f);
        return new(x, y, Math.Max(1, size / 16), Colors[(int)(Hash01(x, y, 23) * Colors.Length)] with { A = 0.85f });
    }

    private static float Hash01(int x, int y, int salt) => PixelArt.Hash(x, y, salt) % 10_000 / 10_000f;
}
