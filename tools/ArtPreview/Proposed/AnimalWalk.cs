using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Animals;

/// <summary>How a walking animal shows its steps. computment chose <see cref="Legs"/> on October 7; the game draws it.</summary>
public enum WalkStyle
{
    /// <summary>A: no legs, as in the approved drawings; the head nods, the body sways and the tail swings.</summary>
    Nod,
    /// <summary>B: leg tips step out from under the body, front and back on opposite sides, and the tail swings.</summary>
    Legs,
}

public sealed partial class AnimalsProposal
{
    /// <summary>
    /// Two walking frames for every animal, drawn by the approved routines:
    /// step 0 is the approved standing drawing, pixel for pixel, and steps 1
    /// and 2 alternate while the animal moves. The gait moves parts placed
    /// ahead of the neck forward or back (a nod), shifts the whole animal a
    /// little sideways (a sway), swings the tail tip, and for style B draws
    /// the leg tips that step out from under the body.
    /// </summary>
    public static Image Walk(string animal, int step, WalkStyle style, int size, Func<Image> draw)
    {
        var previous = Walking.Current;
        Walking.Current = Walking.For(animal, step, style, size);
        try { return draw(); }
        finally { Walking.Current = previous; }
    }

    /// <summary>The walking pose the drawings in progress use; the default is the approved standing pose.</summary>
    private static class Walking
    {
        [ThreadStatic] public static Gait Current;

        public readonly record struct Leg(float Along, float Across);

        /// <summary>One frame of a gait, in 32 px units along and across the animal.</summary>
        public readonly record struct Gait(float NeckAt, float TailAt, float Nod, float Sway, float Swish,
            Leg[] Out, float HalfAlong, float HalfAcross, Color Hoof)
        {
            public float Along(float along) => along >= NeckAt ? along + Nod : along;
            public float Across(float along, float across) => across + Sway + (along <= TailAt ? Swish : 0);
        }

        public static float Along(float along) => Current.Out is null ? along : Current.Along(along);

        public static float Across(float along, float across) => Current.Out is null ? across : Current.Across(along, across);

        /// <summary>Leg tips stepping out from under the body, drawn under it so only the tips show.</summary>
        public static void Legs(Painter paint, Func<float, float, Vector2> at, Vector2 forward, float unit)
        {
            if (Current.Out is not { Length: > 0 } legs) return;
            foreach (var leg in legs)
            {
                // The leg sits outside the sway and nod, so it stays planted while the body moves over it.
                var point = at(leg.Along, leg.Across - Current.Sway);
                paint.Oval(point, forward, Current.HalfAlong * unit + 0.85f, Current.HalfAcross * unit + 0.85f, Outline);
                paint.Oval(point, forward, Current.HalfAlong * unit, Current.HalfAcross * unit, Current.Hoof);
            }
        }

        /// <summary>
        /// One animal's gait: where parts start nodding (the neck) and swinging
        /// (the tail tip), how far, and for style B the two leg tips that show
        /// at a step, just ahead of the chest and just behind the rump, on
        /// opposite sides, with their size and hoof or foot colour.
        /// </summary>
        private readonly record struct Build(float NeckAt, float TailAt, float Nod, float Sway, float Swish,
            Leg Front, Leg Hind, float HalfAlong, float HalfAcross, string Hoof, bool Bird = false);

        private static Build Of(string animal) => animal switch
        {
            "cow" => new(5.0f, -11.0f, 1.0f, 0.6f, 1.4f, new(7.0f, 3.9f), new(-10.8f, 3.4f), 1.3f, 1.1f, "2E2420"),
            "horse" => new(5.6f, -11.5f, 1.0f, 0.5f, 1.4f, new(7.2f, 3.3f), new(-10.8f, 2.9f), 1.3f, 1.0f, "3F2A1A"),
            "sheep" or "shorn" => new(4.2f, -99f, 0.8f, 0.5f, 0f, new(4.6f, 3.9f), new(-8.4f, 2.8f), 1.1f, 1.0f, "524C48"),
            "chicken" => new(2.8f, -3.8f, 1.3f, 0.4f, 0.8f, default, new(-3.6f, 2.6f), 0.9f, 0.7f, "F2CC5E", Bird: true),
            "chick" => new(1.0f, -99f, 1.0f, 0.3f, 0f, default, new(-2.4f, 1.8f), 0.7f, 0.6f, "E0893F", Bird: true),
            "lamb" => new(2.2f, -99f, 0.8f, 0.4f, 0f, new(2.8f, 2.8f), new(-5.4f, 2.0f), 0.9f, 0.8f, "524C48"),
            "calf" => new(3.4f, -7.6f, 0.8f, 0.4f, 1.0f, new(4.6f, 2.8f), new(-7.2f, 2.4f), 1.0f, 0.9f, "2E2420"),
            "foal" => new(4.0f, -7.4f, 0.8f, 0.4f, 1.0f, new(4.8f, 2.4f), new(-7.2f, 2.2f), 1.0f, 0.8f, "6E4E31"),
            _ => throw new ArgumentOutOfRangeException(nameof(animal), animal, "Unknown animal."),
        };

        public static Gait For(string animal, int step, WalkStyle style, int size)
        {
            if (step == 0) return default;
            var b = Of(animal);
            // At 16 px a unit is half a pixel, so every movement doubles to still show.
            var scale = size < 24 ? 2f : 1f;
            var sign = step == 1 ? 1f : -1f;
            // A forward nod on one step and a smaller backward one on the other, as a head bobs at a walk.
            var nod = (step == 1 ? b.Nod : -b.Nod * 0.6f) * scale;
            var legs = style == WalkStyle.Nod ? [] : b.Bird
                // A bird's feet show behind its body, one at a time.
                ? new[] { b.Hind with { Across = b.Hind.Across * -sign } }
                // A diagonal pair: a front hoof ahead of the chest on one side, the hind hoof behind the rump on the other.
                : new[] { b.Front with { Across = b.Front.Across * -sign }, b.Hind with { Across = b.Hind.Across * sign } };
            var nods = style == WalkStyle.Nod || b.Bird;
            return new Gait(b.NeckAt, b.TailAt, nods ? nod : 0, style == WalkStyle.Nod ? b.Sway * sign * scale : 0,
                b.Swish * -sign * scale, legs, b.HalfAlong, b.HalfAcross, new Color(b.Hoof));
        }
    }
}

/// <summary>
/// Owner request (October 7): animals step while they move, as agents do.
/// Two choices, each shown in all eight facings for every adult and young
/// animal: A keeps the approved legless drawings and gives a head nod, a
/// slight sway and a tail swing; B, which computment chose, shows leg tips
/// stepping out from under the body. Each strip's top row is the approved
/// standing drawing, then the two walking frames. See <c>AnimalWalk.md</c>.
/// </summary>
public sealed class AnimalWalkProposal : IArtProposal
{
    public string Family => "animal-walk";

    /// <summary>Every animal the game draws, with the routine that draws it at a facing and size.</summary>
    public static readonly (string Id, string Gait, string Name, Func<int, int, Image> Draw)[] Animals =
    [
        ("horse", "horse", "Horse, saddled", (f, size) => AnimalsProposal.Horse(f, false, size)),
        ("horse.bare", "horse", "Horse, bare back", (f, size) => AnimalsProposal.Horse(f, false, size, saddled: false)),
        ("horse.ridden", "horse", "Horse with a rider", (f, size) => AnimalsProposal.Horse(f, true, size)),
        ("cow", "cow", "Cow", (f, size) => AnimalsProposal.Cow(f, size)),
        ("sheep", "sheep", "Sheep", (f, size) => AnimalsProposal.Sheep(f, false, size)),
        ("sheep.shorn", "shorn", "Shorn sheep", (f, size) => AnimalsProposal.Sheep(f, true, size)),
        ("chicken", "chicken", "Hen", (f, size) => AnimalsProposal.Chicken(f, size)),
        ("foal", "foal", "Foal", (f, size) => AnimalsProposal.Foal(f, size)),
        ("calf", "calf", "Calf", (f, size) => AnimalsProposal.Calf(f, size)),
        ("lamb", "lamb", "Lamb", (f, size) => AnimalsProposal.Lamb(f, size)),
        ("chick", "chick", "Chick", (f, size) => AnimalsProposal.Chick(f, size)),
    ];

    /// <summary>One frame of an animal walking, at a facing and size.</summary>
    public static Image Frame(string id, int facing, int step, WalkStyle style, int size)
    {
        var animal = Animals.Single(item => item.Id == id);
        return AnimalsProposal.Walk(animal.Gait, step, style, size, () => animal.Draw(facing, size));
    }

    public IEnumerable<Entry> Render()
    {
        foreach (var (id, _, name, _) in Animals)
            foreach (var style in new[] { WalkStyle.Nod, WalkStyle.Legs })
            {
                var label = style == WalkStyle.Nod ? "A" : "B";
                yield return new(Family, $"{id}.{label}", Strip(id, style, 32),
                    $"{name}, {label}: standing (approved), then walking steps 1 and 2, in S, SW, W, NW, N, NE, E, SE.");
                yield return new(Family, $"{id}.{label}.16", Strip(id, style, 16), $"{name}, {label}, at 16 px.");
            }
    }

    /// <summary>Three rows (standing, step 1, step 2) by eight facings, on grass.</summary>
    public static Image Strip(string id, WalkStyle style, int size)
    {
        var image = Bitmap.Empty(size * 8, size * 3);
        for (var step = 0; step < 3; step++)
            for (var facing = 0; facing < 8; facing++)
            {
                image.BlitRect(TerrainTextures.Tile(TerrainStyle.Grass, TerrainTextures.VariantAt(facing, step), size),
                    new Rect2I(0, 0, size, size), new Vector2I(facing * size, step * size));
                Sheet.Blend(image, Frame(id, facing, step, style, size), facing * size, step * size);
            }
        return image;
    }
}
