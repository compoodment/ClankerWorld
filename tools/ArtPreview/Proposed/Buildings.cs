using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Buildings;

/// <summary>
/// Round-1 proposal for building exteriors (STYLE.md section 9).
/// <para>
/// It keeps the contract of the game's <see cref="BuildingSprites.Render"/>
/// (kind, footprint in tiles, 32 or 16 px per tile, door side and tile) and
/// its layout: the roof inset three pixels from the footprint, the door on
/// the side that faces the Road, and the start of the doorstep path. What
/// changes is the drawing: roofs are laid in a real material (clay tiles,
/// thatch, slate, planks, shingles) in courses that follow the eaves, lit
/// from the north-west, with ridge and hip lines, an eave shadow, a capped
/// chimney, a visible door over a stone doorstep, and one identifying
/// feature per kind (B1 to B7).
/// </para>
/// <para>
/// Store, Market, Market stall, Town Hall, Port and Clinic are agreed but
/// have no <see cref="BuildingKind"/> yet, so they are drawn through the
/// private <see cref="Design"/> list and only yielded for review.
/// </para>
/// </summary>
public sealed class BuildingsProposal : IArtProposal, IArtSetProvider
{
    public string Family => "buildings";
    public string Name => "buildings";

    // ----------------------------------------------------------------------
    // Palette: every colour is a step of a STYLE.md section 2 ramp.
    // ----------------------------------------------------------------------

    private static readonly Ramp Clay = Ramp.Of("5E2E22", "9E4E34", "C66A45", "E08E64", "EFA982");
    private static readonly Ramp Thatch = Ramp.Of("6B5528", "A98A45", "D2AE5E", "E6C77B", "F0DA9A");
    private static readonly Ramp Slate = Ramp.Of("2B2E33", "4A4E55", "62666E", "80858E", "9A9FA7");
    private static readonly Ramp GreyTimber = Ramp.Of("343C43", "59656F", "758390", "97A5B0", "AEBBC4");
    private static readonly Ramp SiloWood = Ramp.Of("54462F", "8E7A58", "B7A07A", "D3C09A", "E4D4B4");
    private static readonly Ramp DyedShingle = Ramp.Of("3E2B47", "6E4F7C", "8F6A9E", "B08CBE", "C8A8D4");
    private static readonly Ramp Timber = Ramp.Of("3F2A1A", "6E4E31", "8A6440", "A77C52", "D2AC77");
    private static readonly Ramp Doorstep = Ramp.Of("5F5848", "8C7F66", "B9AB8E", "C9BDA2", "DED3BC");
    private static readonly Ramp Rock = Ramp.Of("4A4542", "625B56", "756D68", "8B837D", "A49C95");
    private static readonly Ramp Iron = Ramp.Of("3E3A37", "524C48", "6C6560", "8A827C", "A69E98");
    private static readonly Ramp Gold = Ramp.Of("8A6A1E", "B8902E", "D9AE3C", "F2CC5E", "FFE28A");
    private static readonly Ramp Cloth = Ramp.Of("75674D", "A09170", "CABC99", "E8DCC0", "FFF5DF");
    private static readonly Ramp Berry = Ramp.Of("7A2A2E", "A33A3F", "C4474B", "F08A8A", "FFC2C2");
    private static readonly Ramp Leaf = Ramp.Of("3C5F2E", "4C7A3A", "5E8C45", "79A657", "9BC66F");
    private static readonly Ramp Fruit = Ramp.Of("9A4E1E", "C8702E", "E0893F", "F6C27A", "FFE0A8");
    private static readonly Ramp Dirt = Ramp.Of("6E5538", "977852", "B99A6B", "C9AC7C", "D9C08F");
    private static readonly Ramp Lake = Ramp.Of("3A5F7A", "4A7B9D", "598FB3", "6A9FC0", "8ABBD6");
    private static readonly Ramp River = Ramp.Of("2F5A75", "3B7294", "4786AB", "5695B8", "7FB4CF");

    /// <summary>Soot in a chimney flue or an open belfry.</summary>
    private static readonly Color Soot = new("2A2622");
    /// <summary>Forge embers: the glow, the hot middle and the white-hot core.</summary>
    private static readonly Color Ember = new("E0662A");
    private static readonly Color EmberHot = new("F5A742");
    private static readonly Color EmberCore = new("FFE08A");
    /// <summary>L2 building shadow: (0.04, 0.06, 0.05) at 30%, as the game uses today.</summary>
    private static readonly Color Shadow = new(0.04f, 0.06f, 0.05f, 0.30f);
    /// <summary>L2 shadow of a small object (cart, anvil, crate) on the ground, 28%.</summary>
    private static readonly Color SmallShadow = new(0.05f, 0.08f, 0.05f, 0.28f);
    /// <summary>A chimney or tower's shadow on a roof: the L2 colour a little stronger, so it reads on dark slate.</summary>
    private static readonly Color RoofShadow = new(0.04f, 0.06f, 0.05f, 0.38f);

    // ----------------------------------------------------------------------
    // Designs and the review catalogue.
    // ----------------------------------------------------------------------

    /// <summary>Every design this proposal draws: the six current kinds plus the agreed new ones.</summary>
    private enum Design { House, Warehouse, Farmhouse, Blacksmith, Silo, TailorShop, Store, Market, MarketStall, TownHall, Port, Clinic }

    /// <summary>B3 roof materials; each has its own course pattern in <see cref="Surface"/>.</summary>
    private enum Material { Clay, Slate, Thatch, Shingle, Plank, Canvas }

    /// <summary>B4: a gable has two faces along the ridge; a hip adds triangular end faces.</summary>
    private enum RoofShape { Gable, Hip }

    /// <summary>The roof face a pixel lies on, named by the eave it slopes down to.</summary>
    private enum Face { North, West, East, South }

    /// <summary>
    /// How a roofed design is built: its roof ramp and material, gabled or
    /// hipped, the depth of a side yard (0 for none) and how far the roof
    /// stands back from the door side, so door, doorstep and path fit.
    /// </summary>
    private readonly record struct Recipe(Ramp Roof, Material Material, RoofShape Shape, float Yard = 0, float Clearance = 5, Ramp? Stripe = null);

    private static Recipe RecipeFor(Design design) => design switch
    {
        Design.House => new(Clay, Material.Clay, RoofShape.Hip),
        Design.Farmhouse => new(Thatch, Material.Thatch, RoofShape.Hip, Yard: 18),
        Design.Blacksmith => new(Slate, Material.Slate, RoofShape.Gable, Yard: 20),
        Design.Warehouse => new(GreyTimber, Material.Plank, RoofShape.Gable),
        Design.TailorShop => new(DyedShingle, Material.Shingle, RoofShape.Hip),
        Design.Store => new(Timber, Material.Shingle, RoofShape.Hip, Clearance: 10),
        Design.TownHall => new(Slate, Material.Slate, RoofShape.Hip, Clearance: 22),
        Design.Clinic => new(Cloth, Material.Shingle, RoofShape.Hip),
        _ => new(Timber, Material.Plank, RoofShape.Gable),
    };

    /// <summary>Round-1 review set at 32 px: design, footprint, door, Id and the note shown with it.</summary>
    private static readonly (Design Design, int W, int H, DoorSide Side, int Tile, string Id, string Note)[] Catalogue =
    [
        (Design.House, 1, 1, DoorSide.South, 0, "House.1x1", "Hipped clay-tile roof, capped chimney on the shaded east face, lintel, door and doorstep."),
        (Design.House, 1, 2, DoorSide.South, 0, "House.1x2", "Long hipped clay roof: ridge along the long side, hips at both ends, chimney on the shaded half."),
        (Design.House, 1, 2, DoorSide.East, 0, "House.1x2.door_East", "Same House facing a Road to the east: door, doorstep and path start move with it."),
        (Design.Farmhouse, 1, 2, DoorSide.South, 0, "Farmhouse.1x2", "Hipped thatch with a bound ridge and trimmed eaves; a hay cart in the yard behind."),
        (Design.Blacksmith, 2, 2, DoorSide.South, 1, "Blacksmith.2x2", "Slate roof in 6 x 3 slabs; open forge yard with the hearth's ember glow, anvil and quench barrel."),
        (Design.Warehouse, 2, 2, DoorSide.South, 1, "Warehouse.2x2", "Grey timber planks along the ridge; wide split loading doors over a stone apron."),
        (Design.Silo, 1, 1, DoorSide.South, 0, "Silo.1x1", "Conical board roof in lit and shaded facets with seams, an iron band and a capped vent."),
        (Design.TailorShop, 1, 1, DoorSide.South, 0, "TailorShop.1x1", "Dyed purple shingles; a spool sign above the door."),
        (Design.Store, 1, 1, DoorSide.South, 0, "Store.1x1", "New: brown shingle roof with a red-and-cream striped awning over the door."),
        (Design.Market, 2, 2, DoorSide.South, 1, "Market.2x2", "New: paved square with a striped canvas pavilion and colourful stall awnings with produce."),
        (Design.MarketStall, 1, 1, DoorSide.South, 0, "MarketStall.1x1", "New: one striped stall awning with crates of produce facing the Road."),
        (Design.TownHall, 3, 4, DoorSide.South, 1, "TownHall.3x4", "New: large hipped slate roof, open bell tower with a bronze bell, paved forecourt and wide steps."),
        (Design.Port, 2, 4, DoorSide.North, 1, "Port.2x4", "New: plank shed on the shore facing the Road; a plank pier on piles with bollards and a moored boat."),
        (Design.Clinic, 1, 1, DoorSide.South, 0, "Clinic.1x1", "New: pale shingle roof, a green cross sign and a herb planter by the door."),
    ];

    /// <summary>The 16 px mid-zoom set: design, footprint, Id and note.</summary>
    private static readonly (Design Design, int W, int H, string Id, string Note)[] Catalogue16 =
    [
        (Design.House, 1, 1, "House.1x1.16", "16 px: two tones per face, hip lines, chimney, door and step."),
        (Design.Blacksmith, 2, 2, "Blacksmith.2x2.16", "16 px: slate in two tones, ember and anvil kept in the yard."),
    ];

    public IEnumerable<Entry> Render()
    {
        foreach (var (design, w, h, side, tile, id, note) in Catalogue)
        {
            var sprite = Draw(design, w, h, 32, new BuildingDoor(side, tile));
            var ground = design == Design.Port ? Shore(w, h) : Grass(w, h, 32);
            yield return new(Family, id, Bitmap.Over(ground, sprite, 0, 0), note);
            yield return new(Family, id + ".sprite", sprite, note);
        }
        foreach (var (design, w, h, id, note) in Catalogue16)
        {
            var sprite = Draw(design, w, h, 16, new BuildingDoor(DoorSide.South, w / 2));
            yield return new(Family, id, Bitmap.Over(Grass(w, h, 16), sprite, 0, 0), note);
            yield return new(Family, id + ".sprite", sprite, note);
        }
    }

    /// <summary>
    /// Draws the redrawn kinds at any footprint and door side; Workshop,
    /// Generic and the retired kinds keep the game's current drawing.
    /// </summary>
    public void Apply(ArtSet set)
    {
        set.Building = (kind, width, height, tilePixels, door) => DesignFor(kind) is { } design
            ? Draw(design, width, height, tilePixels, door)
            : BuildingSprites.Render(kind, width, height, tilePixels, door);
    }

    private static Design? DesignFor(BuildingKind kind) => kind switch
    {
        BuildingKind.House => Design.House,
        BuildingKind.Warehouse => Design.Warehouse,
        BuildingKind.Farmhouse => Design.Farmhouse,
        BuildingKind.Blacksmith => Design.Blacksmith,
        BuildingKind.Silo => Design.Silo,
        BuildingKind.TailorShop => Design.TailorShop,
        _ => null,
    };

    /// <summary>Grass under a footprint, the way the baseline sheet shows buildings.</summary>
    private static Image Grass(int tilesWide, int tilesHigh, int tilePixels)
    {
        var ground = Image.CreateEmpty(tilesWide * tilePixels, tilesHigh * tilePixels, false, Image.Format.Rgba8);
        for (var y = 0; y < tilesHigh; y++)
            for (var x = 0; x < tilesWide; x++)
                ground.BlitRect(TerrainTextures.Tile(TerrainStyle.Grass, TerrainTextures.VariantAt(x, y), tilePixels),
                    new Rect2I(0, 0, tilePixels, tilePixels), new Vector2I(x * tilePixels, y * tilePixels));
        return ground;
    }

    /// <summary>One row of grass with River tiles south of it, for the Port.</summary>
    private static Image Shore(int tilesWide, int tilesHigh)
    {
        var ground = Grass(tilesWide, tilesHigh, 32);
        var atlas = WaterTextures.Atlas(32).GetImage();
        for (var y = 1; y < tilesHigh; y++)
            for (var x = 0; x < tilesWide; x++)
                ground.BlitRect(atlas, (Rect2I)WaterTextures.Region(TerrainStyle.River, x, y, 32), new Vector2I(x * 32, y * 32));
        return ground;
    }

    // ----------------------------------------------------------------------
    // Entry point: the same shape as BuildingSprites.Render.
    // ----------------------------------------------------------------------

    /// <summary>Draws one design over its footprint, transparent outside the building.</summary>
    private static Image Draw(Design design, int tilesWide, int tilesHigh, int tilePixels, BuildingDoor door)
    {
        tilesWide = Math.Clamp(tilesWide, 1, 8);
        tilesHigh = Math.Clamp(tilesHigh, 1, 8);
        var plate = new Plate(tilesWide * tilePixels, tilesHigh * tilePixels, tilePixels / 32f);
        var w = tilesWide * 32;
        var h = tilesHigh * 32;
        switch (design)
        {
            case Design.Silo:
                PaintSilo(plate, w, h);
                break;
            case Design.Market:
                PaintMarket(plate, w, h, door);
                break;
            case Design.MarketStall:
                PaintStallLot(plate, w, h, door);
                break;
            case Design.Port:
                PaintPort(plate, w, h, door);
                break;
            default:
                PaintRoofed(plate, design, w, h, door);
                break;
        }
        return plate.Image;
    }

    // ----------------------------------------------------------------------
    // Roofed buildings.
    // ----------------------------------------------------------------------

    /// <summary>Where the roof, the yard and the door go, in 32-unit tile space.</summary>
    private readonly record struct Plan(Rect2 Roof, Rect2? Yard, DoorSide YardSide, float DoorMiddle);

    /// <summary>
    /// B1: the roof is inset three units, with two more on the south for the
    /// eave shadow, and pulled back on the door side to the recipe's
    /// clearance. A yard, if any, takes one end of the footprint away from
    /// the door: across the long side of a wide footprint, or behind a
    /// one-tile-wide one.
    /// </summary>
    private static Plan Lay(int w, int h, BuildingDoor door, Recipe recipe, float doorHalf)
    {
        float left = 3, top = 3, right = w - 3, bottom = h - 5;
        switch (door.Side)
        {
            case DoorSide.North: top = Math.Max(top, recipe.Clearance); break;
            case DoorSide.East: right = Math.Min(right, w - recipe.Clearance); break;
            case DoorSide.West: left = Math.Max(left, recipe.Clearance); break;
            default: bottom = Math.Min(bottom, h - recipe.Clearance); break;
        }
        Rect2? yard = null;
        var yardSide = DoorSide.East;
        if (recipe.Yard > 0 && (w > 32 || h > 32))
        {
            yardSide = YardSide(w, h, door);
            var depth = recipe.Yard;
            switch (yardSide)
            {
                case DoorSide.East:
                    right -= depth;
                    yard = new Rect2(right + 2, 2, w - 4 - right, h - 4);
                    break;
                case DoorSide.West:
                    left += depth;
                    yard = new Rect2(2, 2, left - 4, h - 4);
                    break;
                case DoorSide.South:
                    bottom -= depth;
                    yard = new Rect2(2, bottom + 2, w - 4, h - 4 - bottom);
                    break;
                default:
                    top += depth;
                    yard = new Rect2(2, 2, w - 4, top - 4);
                    break;
            }
        }
        var roof = new Rect2(left, top, right - left, bottom - top);
        var horizontal = door.Side is DoorSide.South or DoorSide.North;
        var from = horizontal ? roof.Position.X : roof.Position.Y;
        var length = horizontal ? roof.Size.X : roof.Size.Y;
        var middle = door.Tile is { } tile ? tile * 32 + 16 : from + length / 2;
        middle = Fit(middle, from + doorHalf + 3, from + length - doorHalf - 3);
        return new Plan(roof, yard, yardSide, middle);
    }

    /// <summary>The end of the footprint farthest from the door, for a yard.</summary>
    private static DoorSide YardSide(int w, int h, BuildingDoor door)
    {
        var opposite = door.Side switch
        {
            DoorSide.North => DoorSide.South,
            DoorSide.East => DoorSide.West,
            DoorSide.West => DoorSide.East,
            _ => DoorSide.North,
        };
        if (door.Side is DoorSide.South or DoorSide.North)
        {
            if (w <= 32) return opposite;
            var middle = door.Tile is { } t ? t * 32 + 16 : w / 2f;
            return middle <= w / 2f ? DoorSide.East : DoorSide.West;
        }
        if (h <= 32) return opposite;
        var along = door.Tile is { } u ? u * 32 + 16 : h / 2f;
        return along <= h / 2f ? DoorSide.South : DoorSide.North;
    }

    /// <summary>
    /// Paints a roofed design: yard ground, then the roof with its eave
    /// shadow, then the identifying feature, the door and the path start.
    /// </summary>
    private static void PaintRoofed(Plate p, Design design, int w, int h, BuildingDoor door)
    {
        var recipe = RecipeFor(design);
        var doorHalf = design switch { Design.Warehouse => 7f, Design.TownHall => 5f, _ => 3f };
        var plan = Lay(w, h, door, recipe, doorHalf);
        var roof = p.Px(plan.Roof);
        var middle = p.P(plan.DoorMiddle);

        if (design == Design.TownHall) Forecourt(p, roof, door.Side);
        if (plan.Yard is { } yardUnits)
        {
            if (design == Design.Blacksmith) YardFloor(p, p.Px(yardUnits), Dirt.Shade, Dirt.Edge, 113);
            else
            {
                // The farmyard is a smaller worn patch under the cart, not the whole end.
                var patch = p.Px(yardUnits);
                var inset = p.P(2);
                YardFloor(p, new Rect2I(patch.Position.X + inset, patch.Position.Y + inset, patch.Size.X - inset * 2, patch.Size.Y - inset * 2), Dirt.Base, Dirt.Shade, 117);
            }
        }

        var main = roof;
        if (design == Design.TownHall)
        {
            main = HallRoofs(p, roof, recipe, door.Side, (int)design * 31 + 7);
            middle = door.Side is DoorSide.South or DoorSide.North
                ? Fit(middle, main.Position.X + p.P(10), main.End.X - 1 - p.P(10))
                : Fit(middle, main.Position.Y + p.P(10), main.End.Y - 1 - p.P(10));
        }
        else PaintRoof(p, roof, recipe, (int)design * 31 + 7);

        var nextRow = 0;
        switch (design)
        {
            case Design.House:
                var (cx, cy) = ChimneySpot(roof, p.Small, door.Side, middle);
                Chimney(p, cx, cy, false);
                nextRow = Door(p, roof, door.Side, middle, 3, 3);
                break;
            case Design.Farmhouse:
                if (plan.Yard is { } farmYard) HayCart(p, p.Px(farmYard));
                nextRow = Door(p, roof, door.Side, middle, 3, 3);
                break;
            case Design.Blacksmith:
                if (plan.Yard is { } forgeYard) ForgeYard(p, p.Px(forgeYard));
                nextRow = Door(p, roof, door.Side, middle, 3, 3);
                break;
            case Design.Warehouse:
                nextRow = LoadingDoors(p, roof, door.Side, middle);
                break;
            case Design.TailorShop:
                var spool = Inward(p, roof, door.Side, middle, 8);
                SpoolSign(p, spool.X, spool.Y);
                nextRow = Door(p, roof, door.Side, middle, 3, 3);
                break;
            case Design.Store:
                StoreAwning(p, roof, door.Side);
                nextRow = StepOnly(p, roof, door.Side, middle, 6, 3);
                break;
            case Design.TownHall:
                var tower = Inward(p, main, door.Side, middle, 15);
                BellTower(p, tower.X, tower.Y);
                Door(p, main, door.Side, middle, 5, 3);
                Planters(p, main, door.Side, middle);
                nextRow = int.MaxValue; // the forecourt already reaches the edge
                break;
            case Design.Clinic:
                var sign = Inward(p, roof, door.Side, middle, 8);
                CrossSign(p, sign.X, sign.Y);
                nextRow = Door(p, roof, door.Side, middle, 3, 3);
                HerbPlanter(p, roof, door.Side, middle);
                break;
        }
        // The doorstep path the Road's own doorstep piece continues (R3).
        if (door.Tile is not null && nextRow != int.MaxValue) Path(p, roof, door.Side, middle, nextRow);
    }

    /// <summary>
    /// B3 Town Hall: a cross plan. Lower hipped wings run parallel to the
    /// door side; the main hall's hipped roof runs toward the door over them,
    /// casting its eave shadow on the wings, with gold finials at its ridge
    /// ends. Returns the main roof, which carries the bell tower and door.
    /// </summary>
    private static Rect2I HallRoofs(Plate p, Rect2I roof, Recipe recipe, DoorSide door, int salt)
    {
        var towardDoor = door is DoorSide.South or DoorSide.North;
        int x = roof.Position.X, y = roof.Position.Y, w = roof.Size.X, h = roof.Size.Y;
        var main = towardDoor
            ? new Rect2I(x + (int)(w * 0.2f), y, w - 2 * (int)(w * 0.2f), h)
            : new Rect2I(x, y + (int)(h * 0.2f), w, h - 2 * (int)(h * 0.2f));
        var wings = towardDoor
            ? new Rect2I(x, y + (int)(h * 0.3f), w, (int)(h * 0.38f))
            : new Rect2I(x + (int)(w * 0.3f), y, (int)(w * 0.38f), h);
        PaintRoof(p, wings, recipe, salt + 1);
        PaintRoof(p, main, recipe, salt);
        // Gold finials where the main ridge ends.
        int iw = main.Size.X - 2, ih = main.Size.Y - 2;
        var size = p.Small ? 1 : 2;
        foreach (var (fx, fy) in RidgeEnds(main.Position.X + 1, main.Position.Y + 1, iw, ih))
        {
            p.Fill(fx - size / 2 + 1, fy - size / 2 + 1, size, size, RoofShadow);
            p.Fill(fx - size / 2, fy - size / 2, size, size, Gold.Base);
            p.Put(fx - size / 2, fy - size / 2, Gold.Highlight);
        }
        return main;
    }

    /// <summary>The two ends of a hipped roof's ridge, in pixels.</summary>
    private static (int X, int Y)[] RidgeEnds(int left, int top, int iw, int ih)
    {
        if (iw >= ih)
        {
            var ridge = (ih + 1) / 2 - 1;
            return [(left + ridge, top + ridge), (left + iw - 1 - ridge, top + ridge)];
        }
        var column = (iw + 1) / 2 - 1;
        return [(left + column, top + column), (left + column, top + ih - 1 - column)];
    }

    /// <summary>
    /// Paints a roof over <paramref name="roof"/> (output pixels): eave
    /// shadow (B2), material courses per face (B3), the one-pixel edge (L5),
    /// then ridge and hip lines with a crease on the shaded side (B4).
    /// </summary>
    private static void PaintRoof(Plate p, Rect2I roof, Recipe recipe, int salt)
    {
        int x0 = roof.Position.X, y0 = roof.Position.Y, w = roof.Size.X, h = roof.Size.Y;
        if (w < 5 || h < 5) return;
        var ramp = recipe.Roof;
        var thatch = recipe.Material == Material.Thatch;
        p.Fill(new Rect2I(x0 + p.P(2), y0 + p.P(3), w, h), Shadow);

        int iw = w - 2, ih = h - 2;
        for (var v = 0; v < ih; v++)
            for (var u = 0; u < iw; u++)
            {
                var face = FaceAt(u, v, iw, ih, recipe.Shape);
                var (along, across) = face switch
                {
                    Face.North => (u, v),
                    Face.South => (u, ih - 1 - v),
                    Face.West => (v, u),
                    _ => (v, iw - 1 - u),
                };
                p.Put(x0 + 1 + u, y0 + 1 + v, Surface(recipe, face, along, across, p.Small, salt));
            }

        // L5: the roof's one-pixel edge; thatch eaves have rounded corners.
        for (var x = 0; x < w; x++)
            for (var y = 0; y < h; y++)
            {
                if (x != 0 && y != 0 && x != w - 1 && y != h - 1) continue;
                var corner = (x == 0 || x == w - 1) && (y == 0 || y == h - 1);
                if (corner && thatch) continue;
                p.Put(x0 + x, y0 + y, ramp.Edge);
            }
        if (thatch)
            foreach (var (cx, cy) in new[] { (1, 1), (w - 2, 1), (1, h - 2), (w - 2, h - 2) })
                p.Put(x0 + cx, y0 + cy, ramp.Edge);

        RidgeAndHips(p, x0 + 1, y0 + 1, iw, ih, recipe, salt);
    }

    /// <summary>
    /// Which face a roof pixel is on. A gable splits along the long side;
    /// a hip adds triangular end faces whose 45° hips meet the ridge.
    /// </summary>
    private static Face FaceAt(int u, int v, int iw, int ih, RoofShape shape)
    {
        if (iw >= ih)
        {
            var ridge = (ih + 1) / 2 - 1;
            var north = v <= ridge;
            if (shape == RoofShape.Gable) return north ? Face.North : Face.South;
            var eave = north ? v : ih - 1 - v;
            if (eave <= u && eave <= iw - 1 - u) return north ? Face.North : Face.South;
            return u < iw - 1 - u ? Face.West : Face.East;
        }
        else
        {
            var ridge = (iw + 1) / 2 - 1;
            var west = u <= ridge;
            if (shape == RoofShape.Gable) return west ? Face.West : Face.East;
            var eave = west ? u : iw - 1 - u;
            if (eave <= v && eave <= ih - 1 - v) return west ? Face.West : Face.East;
            return v < ih - 1 - v ? Face.North : Face.South;
        }
    }

    /// <summary>
    /// B4: the ridge as a one-pixel light line along the long side with a
    /// crease just below it on the shaded half, and on hipped roofs the four
    /// hips: soft light where they touch a lit face, a lifted shade on the
    /// south-east hip. Thatch gets a bound ridge band instead of lines.
    /// </summary>
    private static void RidgeAndHips(Plate p, int left, int top, int iw, int ih, Recipe recipe, int salt)
    {
        var r = recipe.Roof;
        var hip = recipe.Shape == RoofShape.Hip;
        var thatch = recipe.Material == Material.Thatch;
        var horizontal = iw >= ih;
        // One routine for both directions: "along" follows the ridge, "across" spans the roof.
        void Put(int along, int across, Color c)
        {
            if (horizontal) p.Put(left + along, top + across, c);
            else p.Put(left + across, top + along, c);
        }
        var length = horizontal ? iw : ih;
        var breadth = horizontal ? ih : iw;
        var ridge = (breadth + 1) / 2 - 1;
        var from = hip ? ridge : 0;
        var to = hip ? length - 1 - ridge : length - 1;
        var litHip = Toward(r, 2, 3, thatch ? 0.45f : 0.8f);
        var shadedHip = Toward(r, 1, 2, 0.7f);

        if (hip)
        {
            for (var t = 0; t <= ridge; t++)
            {
                Put(t, t, litHip);
                Put(length - 1 - t, t, litHip);
            }
            for (var t = 0; breadth - 1 - t > ridge; t++)
            {
                Put(t, breadth - 1 - t, litHip);
                if (!thatch) Put(length - 1 - t, breadth - 1 - t, shadedHip);
            }
        }
        if (thatch)
        {
            // A bound ridge: a raised roll of straw tied down with spars every few pixels.
            var start = Math.Max(0, from - 1);
            var end = Math.Min(length - 1, to + 1);
            for (var a = start; a <= end; a++)
            {
                if (ridge - 1 >= 0) Put(a, ridge - 1, Toward(r, 3, 4, 0.3f));
                Put(a, ridge, (a + salt) % 4 == 0 && !p.Small ? r[1] : r[3]);
                if (ridge + 1 < breadth) Put(a, ridge + 1, r[2]);
                if (ridge + 2 < breadth && !p.Small) Put(a, ridge + 2, Toward(r, 1, 0, 0.6f));
            }
            return;
        }
        for (var a = from; a <= to && ridge + 1 < breadth; a++) Put(a, ridge + 1, Toward(r, 1, 0, 0.7f));
        for (var a = from; a <= to; a++) Put(a, ridge, r[3]);
    }

    /// <summary>
    /// Part of the way from one ramp step toward another. Course lines and
    /// joints use this, as the current roofs draw their lines in the edge
    /// step at partial strength, so the texture stays calm at 1×.
    /// </summary>
    private static Color Toward(Ramp r, int step, int toward, float amount) => r[step].Lerp(r[toward], amount);

    /// <summary>The tones of one roof face: body, course line, soft joint, lit accent and a faint grain.</summary>
    private readonly record struct Tones(Color Body, Color Line, Color Joint, Color Accent, Color Grain);

    /// <summary>
    /// L1: north and west faces take the base step and darken toward shade;
    /// south and east faces take the shade step and darken toward the edge.
    /// </summary>
    private static Tones TonesFor(Ramp r, bool lit) => lit
        ? new(r[2], Toward(r, 2, 1, 0.85f), Toward(r, 2, 1, 0.5f), Toward(r, 2, 3, 0.7f), Toward(r, 2, 1, 0.25f))
        : new(r[1], Toward(r, 1, 0, 0.6f), Toward(r, 1, 0, 0.32f), Toward(r, 1, 2, 0.4f), Toward(r, 1, 0, 0.15f));

    /// <summary>Colour of one roof pixel from its material, face and position in the courses.</summary>
    private static Color Surface(Recipe recipe, Face face, int along, int across, bool small, int salt)
    {
        var lit = face is Face.North or Face.West;
        var tones = TonesFor(recipe.Roof, lit);
        return recipe.Material switch
        {
            Material.Clay => ClayTiles(tones, along, across, small),
            Material.Slate => Slates(tones, along, across, small, salt),
            Material.Shingle => Shingles(tones, lit, along, across, small, salt),
            Material.Plank => Planks(tones, along, across, small, salt),
            Material.Thatch => Straw(tones, along, across, small, salt),
            _ => Canvas(recipe.Stripe ?? Berry, lit, along, across, small),
        };
    }

    /// <summary>
    /// B3 House: clay tiles in courses three rows deep, offset by half a tile
    /// from course to course. Each four-pixel tile shows a rounded butt at
    /// the eave side (a lit crown between two soft gaps) and the course line
    /// above it is the shadow of the next course. At 16 px: body and line only.
    /// </summary>
    private static Color ClayTiles(Tones t, int along, int across, bool small)
    {
        const int depth = 3;
        var course = across / depth;
        var row = across % depth;
        if (row == depth - 1) return t.Line;
        if (small) return t.Body;
        if (row == 1) return t.Body;
        var u = (along + course % 2 * 2) % 4;
        return u switch { 0 => t.Joint, 1 => t.Accent, 2 => t.Body.Lerp(t.Accent, 0.4f), _ => t.Body };
    }

    /// <summary>
    /// B3 Blacksmith and Town Hall: slate in 6 × 3 slabs (three face rows
    /// and a line), joints staggered by half a slab, the eave-side row lit on
    /// sunny faces and one slab in ten a touch lighter or darker.
    /// </summary>
    private static Color Slates(Tones t, int along, int across, bool small, int salt)
    {
        var depth = small ? 3 : 4;
        var length = small ? 4 : 6;
        var course = across / depth;
        var row = across % depth;
        if (row == depth - 1) return t.Line;
        var position = along + course % 2 * (length / 2);
        if (position % length == 0) return t.Joint;
        if (small) return t.Body;
        var roll = PixelArt.Hash(position / length, course, salt) % 10;
        var body = roll == 0 ? t.Body.Lerp(t.Accent, 0.45f) : roll == 1 ? t.Grain : t.Body;
        return row == 0 ? body.Lerp(t.Accent, 0.35f) : body;
    }

    /// <summary>
    /// B3 Tailor, Store, Clinic: shingles in courses three rows deep with
    /// uneven widths and a mix of tones, like split and dyed wood.
    /// </summary>
    private static Color Shingles(Tones t, bool lit, int along, int across, bool small, int salt)
    {
        const int depth = 3;
        var course = across / depth;
        var row = across % depth;
        if (row == depth - 1) return t.Line;
        if (small) return t.Body;
        var length = 3 + (int)(PixelArt.Hash(course, 2, salt) % 3);
        var position = along + (int)(PixelArt.Hash(course, 1, salt) % (uint)length);
        if (position % length == 0) return t.Joint;
        var roll = PixelArt.Hash(position / length, course, salt + 5) % 6;
        var body = roll == 0 ? t.Body.Lerp(t.Accent, 0.6f) : roll == 1 ? t.Body.Lerp(t.Line, 0.35f) : t.Body;
        return row == 0 && lit ? body.Lerp(t.Accent, 0.4f) : body;
    }

    /// <summary>
    /// B3 Warehouse and Port: long planks along the ridge, three pixels with
    /// a one-pixel seam, butt joints far apart, a few lighter or darker
    /// boards and a faint grain.
    /// </summary>
    private static Color Planks(Tones t, int along, int across, bool small, int salt)
    {
        var depth = small ? 3 : 4;
        var course = across / depth;
        var row = across % depth;
        if (row == depth - 1) return t.Line;
        if (small) return t.Body;
        var length = 18 + (int)(PixelArt.Hash(course, 2, salt) % 13);
        var position = along + (int)(PixelArt.Hash(course, 1, salt) % (uint)length);
        if (position % length == 0) return t.Joint;
        var roll = PixelArt.Hash(position / length, course, salt + 3) % 5;
        var body = roll == 0 ? t.Body.Lerp(t.Accent, 0.35f) : roll == 1 ? t.Grain : t.Body;
        if (row == 1 && PixelArt.Hash(along / 3, course, salt + 9) % 7 == 0) body = t.Grain;
        return body;
    }

    /// <summary>
    /// B3 Farmhouse: thatch. Trimmed straw ends along the eave with a soft
    /// shadow under the lip, faint layer lines and short strands running
    /// down the slope in light and dark.
    /// </summary>
    private static Color Straw(Tones t, int along, int across, bool small, int salt)
    {
        if (across == 0) return t.Accent;
        if (across == 1 && !small) return along % 2 == 0 ? t.Joint : t.Body;
        if (!small && across % 7 == 0 && PixelArt.Hash(along, across, salt) % 3 != 0) return t.Grain;
        var phase = (int)(PixelArt.Hash(along, 0, salt) % 3);
        var strand = PixelArt.Hash(along, (across + phase) / 3, salt + 1) % (small ? 8u : 5u);
        if (strand == 0) return t.Joint;
        if (strand == 1 && !small) return t.Body.Lerp(t.Accent, 0.6f);
        return t.Body;
    }

    /// <summary>B3 Market: canvas in stripes running down the slope, with a darker hem at the eave.</summary>
    private static Color Canvas(Ramp stripe, bool lit, int along, int across, bool small)
    {
        var width = small ? 2 : 4;
        var striped = along / width % 2 == 0;
        if (across == 0) return striped ? stripe[lit ? 1 : 0] : Cloth[lit ? 2 : 1];
        return striped ? stripe[lit ? 2 : 1] : Cloth[lit ? 3 : 2];
    }

    // ----------------------------------------------------------------------
    // Doors, doorsteps and paths.
    // ----------------------------------------------------------------------

    /// <summary>Puts a pixel <paramref name="k"/> rows out from the roof edge on the door side, <paramref name="t"/> along it.</summary>
    private static void Out(Plate p, Rect2I roof, DoorSide side, int k, int t, Color c)
    {
        switch (side)
        {
            case DoorSide.North: p.Put(t, roof.Position.Y - k, c); break;
            case DoorSide.East: p.Put(roof.End.X - 1 + k, t, c); break;
            case DoorSide.West: p.Put(roof.Position.X - k, t, c); break;
            default: p.Put(t, roof.End.Y - 1 + k, c); break;
        }
    }

    /// <summary>How many pixel rows lie between the roof edge and the footprint edge on a side.</summary>
    private static int Reach(Plate p, Rect2I roof, DoorSide side) => side switch
    {
        DoorSide.North => roof.Position.Y,
        DoorSide.East => p.Width - roof.End.X,
        DoorSide.West => roof.Position.X,
        _ => p.Height - roof.End.Y,
    };

    /// <summary>The pixel span [from, to) of something <paramref name="half"/> units either side of the door middle.</summary>
    private static (int From, int To) Span(Plate p, int middle, int half) =>
        p.Small ? (middle - half / 2, middle + (half + 1) / 2) : (middle - half, middle + half);

    /// <summary>
    /// B5: a door in Timber edge under a one-pixel Timber lintel, over a
    /// doorstep <paramref name="stepRows"/> deep in Doorstep stone (lit at
    /// the north-west corner, shaded on its outer row). At 16 px the lintel
    /// drops and the step is one row. Returns the first row free for the path.
    /// </summary>
    private static int Door(Plate p, Rect2I roof, DoorSide side, int middle, int half, int stepRows)
    {
        var (from, to) = Span(p, middle, half);
        if (!p.Small)
            for (var t = from - 1; t < to + 1; t++) Out(p, roof, side, 0, t, Timber.Light);
        for (var t = from; t < to; t++) Out(p, roof, side, 1, t, Timber.Edge);
        return Step(p, roof, side, from, to, 2, p.Small ? 1 : stepRows);
    }

    /// <summary>A doorstep alone, beyond an awning that hides the door; returns the first free row.</summary>
    private static int StepOnly(Plate p, Rect2I roof, DoorSide side, int middle, int startRow, int stepRows)
    {
        var (from, to) = Span(p, middle, 3);
        return Step(p, roof, side, from, to, p.Small ? startRow / 2 + 1 : startRow, p.Small ? 1 : stepRows);
    }

    /// <summary>
    /// B5: a doorstep across [<paramref name="from"/>, <paramref name="to"/>)
    /// from <paramref name="firstRow"/> outward, lit at its north-west corner and
    /// shaded on its outer row, clipped to the footprint. Returns the first free row.
    /// </summary>
    private static int Step(Plate p, Rect2I roof, DoorSide side, int from, int to, int firstRow, int rows)
    {
        var last = Math.Min(Reach(p, roof, side), firstRow + rows - 1);
        for (var k = firstRow; k <= last; k++)
            for (var t = from; t < to; t++)
            {
                var c = k == last && rows > 1 ? Doorstep.Shade : Doorstep.Base;
                if (k == firstRow && t == from && rows > 1) c = Doorstep.Light;
                Out(p, roof, side, k, t, c);
            }
        return last + 1;
    }

    /// <summary>
    /// R1/R3: the doorstep path from the step to the footprint edge, five
    /// pixels of Road dirt with a feathered worn edge, on the same axis as
    /// the Road's doorstep piece so the two join at the tile edge.
    /// </summary>
    private static void Path(Plate p, Rect2I roof, DoorSide side, int middle, int fromRow)
    {
        var reach = Reach(p, roof, side);
        for (var k = fromRow; k <= reach; k++)
        {
            if (p.Small)
            {
                for (var t = middle - 1; t < middle + 2; t++) Out(p, roof, side, k, t, Dirt.Base);
                continue;
            }
            for (var t = middle - 2; t < middle + 3; t++) Out(p, roof, side, k, t, Dirt.Base);
            foreach (var t in new[] { middle - 3, middle + 3 })
                if (PixelArt.Hash(t, k + (int)side * 7, 91) % 10 >= 3) Out(p, roof, side, k, t, Dirt.Shade);
        }
    }

    /// <summary>
    /// B6 Warehouse: wide split loading doors, a Timber lintel beam, two
    /// plank leaves with a dark split, over a broad stone loading apron.
    /// </summary>
    private static int LoadingDoors(Plate p, Rect2I roof, DoorSide side, int middle)
    {
        var (from, to) = Span(p, middle, 7);
        var split = (from + to) / 2;
        if (!p.Small)
            for (var t = from - 1; t < to + 1; t++) Out(p, roof, side, 0, t, Timber.Light);
        var leafRows = p.Small ? 1 : 2;
        for (var k = 1; k <= leafRows; k++)
            for (var t = from; t < to; t++)
            {
                var c = t == from || t == to - 1 || t == split ? Timber.Edge
                    : (t - from) % 3 == 0 && !p.Small ? Timber.Shade : k == 1 ? Timber.Base : Timber.Shade;
                Out(p, roof, side, k, t, c);
            }
        return Step(p, roof, side, from, to, leafRows + 1, p.Small ? 1 : 2);
    }

    /// <summary>A point <paramref name="distance"/> units inside the roof from the door.</summary>
    private static (int X, int Y) Inward(Plate p, Rect2I roof, DoorSide side, int middle, float distance)
    {
        var d = p.P(distance);
        return side switch
        {
            DoorSide.North => (middle, roof.Position.Y + d),
            DoorSide.East => (roof.End.X - 1 - d, middle),
            DoorSide.West => (roof.Position.X + d, middle),
            _ => (middle, roof.End.Y - 1 - d),
        };
    }

    // ----------------------------------------------------------------------
    // Identifying features.
    // ----------------------------------------------------------------------

    /// <summary>
    /// B6 House: the chimney sits on the shaded half near the ridge, at the
    /// end away from the door so it never crowds the doorway.
    /// </summary>
    private static (int X, int Y) ChimneySpot(Rect2I roof, bool small, DoorSide side, int middle)
    {
        int iw = roof.Size.X - 2, ih = roof.Size.Y - 2;
        int left = roof.Position.X + 1, top = roof.Position.Y + 1;
        var margin = small ? 3 : 6;
        if (iw < ih)
        {
            // A long north-south roof: the east face, away from an east or west door.
            var nearDoor = side is DoorSide.East or DoorSide.West && middle < top + ih / 2;
            var along = Math.Min(ih / 4, small ? 6 : 14);
            return (left + iw - margin, nearDoor ? top + ih - 1 - along : top + along);
        }
        var ridge = (ih + 1) / 2 - 1;
        if (iw - ih <= 8)
        {
            // A square roof: the east face, or the south face when the door is on the east.
            if (side == DoorSide.East) return (left + iw / 2 + (small ? 1 : 3), top + ih - margin + (small ? 0 : 1));
            return (left + iw - Math.Max(small ? 3 : 5, iw / 5), top + ridge + 1);
        }
        // A long east-west roof: the south face, away from a north or south door.
        var awayWest = side is DoorSide.North or DoorSide.South && middle > left + iw / 2;
        return (left + (int)(iw * (awayWest ? 0.3f : 0.7f)), top + ih - margin);
    }

    /// <summary>
    /// A stone chimney stack seen from above: a two-pixel cap (dark edge and
    /// a rim lit north-west, shaded south-east) around a sooty flue, casting
    /// a short shadow on the roof. A forge chimney shows its ember.
    /// </summary>
    private static void Chimney(Plate p, int cx, int cy, bool ember)
    {
        if (p.Small)
        {
            // Three pixels: a lit north-west corner, a dark flue, the edge on the south-east.
            p.Fill(cx, cy, 3, 3, RoofShadow);
            p.Fill(cx - 1, cy - 1, 3, 3, Rock.Edge);
            p.Put(cx - 1, cy - 1, Rock.Highlight);
            p.Put(cx, cy - 1, Rock.Light);
            p.Put(cx - 1, cy, Rock.Light);
            p.Put(cx, cy, ember ? Ember : Soot);
            return;
        }
        int x = cx - 3, y = cy - 3;
        p.Fill(x + 2, y + 2, 6, 6, RoofShadow);
        p.Fill(x, y, 6, 6, Rock.Edge);
        p.Fill(x + 1, y + 1, 4, 4, Rock.Shade);
        p.Fill(x + 1, y + 1, 4, 1, Rock.Highlight);
        p.Fill(x + 1, y + 1, 1, 4, Rock.Highlight);
        p.Put(x + 4, y + 1, Rock.Light);
        p.Put(x + 1, y + 4, Rock.Light);
        p.Fill(x + 2, y + 2, 2, 2, Soot);
        if (ember) p.Put(x + 3, y + 3, Ember);
    }

    /// <summary>B6 Tailor: a cream sign board with a red thread spool between timber flanges.</summary>
    private static void SpoolSign(Plate p, int cx, int cy)
    {
        if (p.Small)
        {
            p.Fill(cx - 1, cy - 1, 4, 4, SmallShadow);
            p.Fill(cx - 2, cy - 2, 4, 4, Timber.Edge);
            p.Fill(cx - 1, cy - 1, 2, 2, Berry.Base);
            p.Put(cx - 1, cy - 1, Berry.Light);
            return;
        }
        p.Fill(cx - 3, cy - 3, 9, 8, SmallShadow);
        p.Fill(cx - 4, cy - 4, 9, 8, Timber.Edge);
        p.Fill(cx - 3, cy - 3, 7, 6, Cloth.Light);
        p.Fill(cx - 3, cy - 3, 7, 1, Cloth.Highlight);
        p.Fill(cx - 2, cy - 2, 5, 1, Timber.Base);
        p.Fill(cx - 2, cy + 2, 5, 1, Timber.Shade);
        p.Fill(cx - 1, cy - 1, 3, 3, Berry.Base);
        p.Put(cx - 1, cy - 1, Berry.Light);
        p.Put(cx + 1, cy + 1, Berry.Shade);
        p.Put(cx + 2, cy, Berry.Shade);
        p.Put(cx + 3, cy + 1, Iron.Light);
    }

    /// <summary>B6 Clinic: a cream sign board with a green cross.</summary>
    private static void CrossSign(Plate p, int cx, int cy)
    {
        if (p.Small)
        {
            p.Fill(cx - 1, cy - 1, 5, 5, SmallShadow);
            p.Fill(cx - 2, cy - 2, 5, 5, Timber.Edge);
            p.Fill(cx - 1, cy - 1, 3, 3, Cloth.Highlight);
            p.Fill(cx - 1, cy, 3, 1, Leaf.Base);
            p.Fill(cx, cy - 1, 1, 3, Leaf.Base);
            return;
        }
        p.Fill(cx - 3, cy - 3, 9, 9, SmallShadow);
        p.Fill(cx - 4, cy - 4, 9, 9, Timber.Edge);
        p.Fill(cx - 3, cy - 3, 7, 7, Cloth.Highlight);
        p.Fill(cx - 1, cy - 3, 3, 7, Leaf.Base);
        p.Fill(cx - 3, cy - 1, 7, 3, Leaf.Base);
        p.Fill(cx - 1, cy - 3, 1, 2, Leaf.Light);
        p.Fill(cx - 3, cy - 1, 2, 1, Leaf.Light);
        p.Put(cx + 1, cy + 1, Leaf.Shade);
    }

    /// <summary>Clinic: a small planter of herbs beside the doorstep, leaves with a few lavender flowers.</summary>
    private static void HerbPlanter(Plate p, Rect2I roof, DoorSide side, int middle)
    {
        if (p.Small) return;
        var start = middle + 5;
        for (var k = 2; k <= 4; k++)
            for (var t = start; t < start + 6; t++)
            {
                var edge = k == 2 || k == 4 || t == start || t == start + 5;
                var c = edge ? Timber.Shade : (t + k) % 3 == 0 ? Leaf.Light : Leaf.Base;
                if (!edge && (t * 7 + k) % 5 == 0) c = DyedShingle.Light;
                Out(p, roof, side, k, t, c);
            }
    }

    /// <summary>
    /// B3 Store: a striped awning over the door across the shop front,
    /// sloping down toward the Road with a scalloped hem.
    /// </summary>
    private static void StoreAwning(Plate p, Rect2I roof, DoorSide side)
    {
        var depth = p.Small ? 3 : 6;
        var inset = p.Small ? 1 : 2;
        var area = side switch
        {
            DoorSide.North => new Rect2I(roof.Position.X + inset, roof.Position.Y - depth + 1, roof.Size.X - inset * 2, depth),
            DoorSide.East => new Rect2I(roof.End.X - 1, roof.Position.Y + inset, depth, roof.Size.Y - inset * 2),
            DoorSide.West => new Rect2I(roof.Position.X - depth + 1, roof.Position.Y + inset, depth, roof.Size.Y - inset * 2),
            _ => new Rect2I(roof.Position.X + inset, roof.End.Y - 1, roof.Size.X - inset * 2, depth),
        };
        Awning(p, area, side, Berry);
    }

    /// <summary>
    /// A striped canvas awning in <paramref name="area"/> (pixels), sloping
    /// down toward <paramref name="front"/>. Stripes run down the slope; the
    /// hem is a darker band whose stripe middles hang one pixel lower.
    /// </summary>
    private static void Awning(Plate p, Rect2I area, DoorSide front, Ramp stripe)
    {
        p.Fill(new Rect2I(area.Position.X + p.P(2), area.Position.Y + p.P(2), area.Size.X, area.Size.Y), Shadow);
        var across = front is DoorSide.South or DoorSide.North;
        var lit = front is DoorSide.North or DoorSide.West;
        var width = p.Small ? 2 : 3;
        int length = across ? area.Size.X : area.Size.Y, depth = across ? area.Size.Y : area.Size.X;
        for (var i = 0; i < length; i++)
            for (var j = 0; j < depth; j++)
            {
                var striped = i / width % 2 == 0;
                var ramp = striped ? stripe : Cloth;
                var step = striped ? 2 : lit ? 4 : 3;
                Color c;
                if (j == depth - 1)
                {
                    if (i % width != width / 2) continue;
                    c = ramp[step - 1];
                }
                else if (j == depth - 2) c = ramp[step - 1];
                else if (j == 0 || i == 0 || i == length - 1) c = stripe.Edge;
                else c = ramp[step];
                var (x, y) = front switch
                {
                    DoorSide.North => (area.Position.X + i, area.End.Y - 1 - j),
                    DoorSide.East => (area.Position.X + j, area.Position.Y + i),
                    DoorSide.West => (area.End.X - 1 - j, area.Position.Y + i),
                    _ => (area.Position.X + i, area.Position.Y + j),
                };
                p.Put(x, y, c);
            }
    }

    /// <summary>
    /// Yard ground: packed earth in <paramref name="ground"/> with soft
    /// patches a little toward <paramref name="worn"/>, a few specks and a
    /// feathered edge, so it reads like the Roads and stays calm. The forge
    /// yard uses the darker step, with cinders.
    /// </summary>
    private static void YardFloor(Plate p, Rect2I yard, Color ground, Color worn, int salt)
    {
        var patch = ground.Lerp(worn, 0.35f);
        for (var y = yard.Position.Y; y < yard.End.Y; y++)
            for (var x = yard.Position.X; x < yard.End.X; x++)
            {
                var fromX = Math.Min(x - yard.Position.X, yard.End.X - 1 - x);
                var fromY = Math.Min(y - yard.Position.Y, yard.End.Y - 1 - y);
                if (fromX + fromY < (p.Small ? 1 : 3)) continue;
                var border = fromX == 0 || fromY == 0 || fromX + fromY == (p.Small ? 1 : 3);
                var roll = PixelArt.Hash(x, y, salt) % 100;
                if (border)
                {
                    if (roll < 40) continue;
                    p.Put(x, y, worn with { A = 0.55f });
                    continue;
                }
                var blob = PixelArt.Hash((x + (y / 3 % 2) * 2) / 5, y / 3, salt + 1) % 4 == 0;
                var c = roll < 3 && !p.Small ? (salt == 113 ? Iron.Shade : Dirt.Shade) : blob ? patch : ground;
                p.Put(x, y, c);
            }
    }

    /// <summary>
    /// B6 Blacksmith: in the forge yard, a stone hearth with its ember glow
    /// against the wall, an anvil on a stump and a quench barrel of water.
    /// </summary>
    private static void ForgeYard(Plate p, Rect2I yard)
    {
        var tall = yard.Size.Y >= yard.Size.X;
        (int X, int Y) At(float f) => tall
            ? (yard.Position.X + yard.Size.X / 2, yard.Position.Y + (int)(yard.Size.Y * f))
            : (yard.Position.X + (int)(yard.Size.X * f), yard.Position.Y + yard.Size.Y / 2);
        var hearth = At(0.22f);
        var anvil = At(0.52f);
        var barrel = At(0.8f);
        if (p.Small)
        {
            p.Fill(hearth.X - 2 + 1, hearth.Y - 2 + 1, 5, 4, SmallShadow);
            p.Fill(hearth.X - 2, hearth.Y - 2, 5, 4, Rock.Edge);
            p.Fill(hearth.X - 1, hearth.Y - 1, 3, 2, Rock.Light);
            p.Put(hearth.X, hearth.Y - 1, Ember);
            p.Put(hearth.X, hearth.Y, EmberHot);
            p.Fill(anvil.X - 1, anvil.Y, 3, 2, SmallShadow);
            p.Fill(anvil.X - 2, anvil.Y - 1, 3, 2, Iron.Edge);
            p.Put(anvil.X - 1, anvil.Y - 1, Iron.Highlight);
            return;
        }
        // Hearth: a stone box with a firepit of embers and a warm glow on the stone.
        int hx = hearth.X - 5, hy = hearth.Y - 4;
        p.Fill(hx + 2, hy + 3, 10, 9, SmallShadow);
        p.Fill(hx, hy, 10, 9, Rock.Edge);
        p.Fill(hx + 1, hy + 1, 8, 7, Rock.Shade);
        p.Fill(hx + 1, hy + 1, 7, 6, Rock.Base);
        p.Fill(hx + 1, hy + 1, 7, 1, Rock.Highlight);
        p.Fill(hx + 1, hy + 1, 1, 6, Rock.Light);
        p.Fill(hx + 3, hy + 3, 4, 3, Soot);
        p.Fill(hx + 3, hy + 3, 4, 3, Ember);
        p.Put(hx + 4, hy + 4, EmberHot);
        p.Put(hx + 5, hy + 4, EmberCore);
        p.Put(hx + 5, hy + 3, EmberHot);
        p.Fill(hx + 2, hy + 2, 6, 5, Ember with { A = 0.22f });
        // Anvil on a timber stump: body, horn toward the west, lit top edge.
        int ax = anvil.X, ay = anvil.Y;
        p.Disc(ax + 1.5f, ay + 2.5f, 4, SmallShadow);
        p.Disc(ax + 0.5f, ay + 0.5f, 4, Timber.Edge);
        p.Disc(ax + 0.5f, ay + 0.5f, 3, Timber.Base);
        p.Disc(ax + 0.5f, ay + 0.5f, 1.5f, Timber.Light);
        p.Fill(ax - 3, ay - 2, 7, 4, Iron.Edge);
        p.Fill(ax - 5, ay - 1, 2, 2, Iron.Edge);
        p.Put(ax - 6, ay - 1, Iron.Edge);
        p.Fill(ax - 2, ay - 1, 5, 2, Iron.Base);
        p.Fill(ax - 4, ay - 1, 2, 1, Iron.Light);
        p.Fill(ax - 2, ay - 1, 5, 1, Iron.Highlight);
        // Quench barrel: a timber ring holding dark water with a glint.
        int bx = barrel.X, by = barrel.Y;
        p.Disc(bx + 1.5f, by + 2.5f, 3.5f, SmallShadow);
        p.Disc(bx + 0.5f, by + 0.5f, 3.5f, Timber.Edge);
        p.Disc(bx + 0.5f, by + 0.5f, 2.6f, Timber.Light);
        p.Disc(bx + 0.5f, by + 0.5f, 1.8f, River.Shade);
        p.Put(bx - 1, by - 1, River.Highlight);
    }

    /// <summary>
    /// B6 Farmhouse: a hay cart in the yard, a plank bed heaped with hay,
    /// two wheels showing at its sides and shafts toward the open end.
    /// </summary>
    private static void HayCart(Plate p, Rect2I yard)
    {
        var wide = yard.Size.X >= yard.Size.Y;
        int bedLong = p.Small ? 6 : 13, bedShort = p.Small ? 5 : 9, shaft = p.Small ? 2 : 5;
        int cx = yard.Position.X + yard.Size.X / 2 - (wide ? shaft / 2 : 0);
        int cy = yard.Position.Y + yard.Size.Y / 2 - (wide ? 0 : shaft / 2);
        // Lay the cart out along a local axis: u along the bed toward the shafts, v across.
        void Rect(int u, int v, int lu, int lv, Color c)
        {
            if (wide) p.Fill(cx - bedLong / 2 + u, cy - bedShort / 2 + v, lu, lv, c);
            else p.Fill(cx - bedShort / 2 + v, cy - bedLong / 2 + u, lv, lu, c);
        }
        if (p.Small)
        {
            Rect(1, 1, bedLong, bedShort, SmallShadow);
            Rect(0, 0, bedLong, bedShort, Timber.Edge);
            Rect(1, 1, bedLong - 2, bedShort - 2, Thatch.Light);
            Rect(2, 1, 1, 1, Thatch.Highlight);
            Rect(bedLong, 1, shaft, 1, Timber.Shade);
            Rect(bedLong, bedShort - 2, shaft, 1, Timber.Shade);
            return;
        }
        Rect(2, 3, bedLong + shaft, bedShort, SmallShadow);
        // Wheels peeking out on both long sides.
        Rect(3, -1, 5, 2, Timber.Edge);
        Rect(3, bedShort - 1, 5, 2, Timber.Edge);
        Rect(4, -1, 3, 1, Timber.Shade);
        Rect(4, bedShort, 3, 1, Timber.Shade);
        // Shafts and their crossbar.
        Rect(bedLong, 2, shaft, 1, Timber.Shade);
        Rect(bedLong, bedShort - 3, shaft, 1, Timber.Shade);
        Rect(bedLong + shaft - 1, 2, 1, bedShort - 4, Timber.Edge);
        // Bed and a heap of hay, lit from the north-west, with strands.
        Rect(0, 0, bedLong, bedShort, Timber.Edge);
        Rect(1, 1, bedLong - 2, bedShort - 2, Timber.Base);
        Rect(1, 1, bedLong - 2, 1, Timber.Light);
        Rect(2, 2, bedLong - 4, bedShort - 4, Thatch.Base);
        Rect(2, 2, bedLong - 5, 2, Thatch.Light);
        Rect(3, 2, 3, 1, Thatch.Highlight);
        Rect(bedLong - 4, bedShort - 4, 2, 1, Thatch.Shade);
        Rect(5, bedShort - 3, 1, 1, Thatch.Shade);
        Rect(8, 3, 1, 1, Thatch.Shade);
    }

    /// <summary>Town Hall forecourt: flagstones from the roof edge to the footprint edge on the door side.</summary>
    private static void Forecourt(Plate p, Rect2I roof, DoorSide side)
    {
        var inset = p.P(2);
        var area = side switch
        {
            DoorSide.North => new Rect2I(inset, 0, p.Width - inset * 2, roof.Position.Y + 1),
            DoorSide.East => new Rect2I(roof.End.X - 1, inset, p.Width - roof.End.X + 1, p.Height - inset * 2),
            DoorSide.West => new Rect2I(0, inset, roof.Position.X + 1, p.Height - inset * 2),
            _ => new Rect2I(inset, roof.End.Y - 1, p.Width - inset * 2, p.Height - roof.End.Y + 1),
        };
        Flagstones(p, area, 151);
    }

    /// <summary>Town Hall: two stone planters of flowers on the forecourt, one either side of the steps.</summary>
    private static void Planters(Plate p, Rect2I roof, DoorSide side, int middle)
    {
        var size = p.Small ? 3 : 6;
        var gap = p.Small ? 6 : 12;
        var firstRow = p.Small ? 3 : 6;
        foreach (var start in new[] { middle - gap - size, middle + gap })
            for (var k = firstRow; k < firstRow + size; k++)
                for (var t = start; t < start + size; t++)
                {
                    var rim = k == firstRow || k == firstRow + size - 1 || t == start || t == start + size - 1;
                    var c = rim ? (p.Small ? Doorstep.Shade : k == firstRow || t == start ? Doorstep.Light : Doorstep.Edge)
                        : (t + k) % 3 == 0 ? Leaf.Light : Leaf.Base;
                    if (!rim && (t * 5 + k * 3) % 7 == 0) c = (t + k) % 2 == 0 ? Berry.Light : Gold.Light;
                    Out(p, roof, side, k, t, c);
                }
    }

    /// <summary>Calm flagstones: offset rows of stones in the Doorstep ramp, joints one step darker, worn border.</summary>
    private static void Flagstones(Plate p, Rect2I area, int salt)
    {
        int rowHeight = p.Small ? 3 : 5, stoneWidth = p.Small ? 4 : 7;
        for (var y = area.Position.Y; y < area.End.Y; y++)
        {
            var row = (y - area.Position.Y) / rowHeight;
            var inRow = (y - area.Position.Y) % rowHeight;
            var shift = (int)(PixelArt.Hash(row, 0, salt) % (uint)stoneWidth);
            for (var x = area.Position.X; x < area.End.X; x++)
            {
                var position = x - area.Position.X + shift;
                var border = x == area.Position.X || y == area.Position.Y || x == area.End.X - 1 || y == area.End.Y - 1;
                Color c;
                if (border) c = Doorstep.Shade;
                else if (inRow == rowHeight - 1 || position % stoneWidth == stoneWidth - 1) c = Toward(Doorstep, 2, 1, 0.7f);
                else
                {
                    var roll = PixelArt.Hash(position / stoneWidth, row, salt + 1) % 6;
                    c = roll == 0 ? Toward(Doorstep, 2, 3, 0.6f) : roll == 1 ? Toward(Doorstep, 2, 1, 0.25f) : Doorstep.Base;
                    if (inRow == 0 && !p.Small) c = c.Lerp(Doorstep.Light, 0.3f);
                }
                p.Put(x, y, c);
            }
        }
    }

    /// <summary>
    /// B3 Town Hall: an open bell tower on the roof, seen from above. Stone
    /// walls lit north-west with corner piers, the dark belfry inside, a
    /// timber headstock across it and the bronze bell hanging from it.
    /// </summary>
    private static void BellTower(Plate p, int cx, int cy)
    {
        if (p.Small)
        {
            p.Fill(cx - 3, cy - 3, 8, 8, RoofShadow);
            p.Fill(cx - 4, cy - 4, 8, 8, Doorstep.Edge);
            p.Fill(cx - 3, cy - 3, 6, 6, Doorstep.Light);
            p.Fill(cx - 2, cy - 2, 4, 4, Soot);
            p.Fill(cx - 1, cy - 1, 2, 2, Gold.Base);
            p.Put(cx - 1, cy - 1, Gold.Highlight);
            return;
        }
        const int half = 9;
        int x = cx - half, y = cy - half, size = half * 2;
        p.Fill(x + 3, y + 3, size, size, RoofShadow);
        p.Fill(x, y, size, size, Doorstep.Edge);
        p.Fill(x + 1, y + 1, size - 2, size - 2, Doorstep.Shade);
        p.Fill(x + 1, y + 1, size - 3, size - 3, Doorstep.Base);
        p.Fill(x + 1, y + 1, size - 3, 1, Doorstep.Light);
        p.Fill(x + 1, y + 1, 1, size - 3, Doorstep.Light);
        p.Fill(x + 3, y + 3, size - 6, size - 6, Doorstep.Edge);
        p.Fill(x + 4, y + 4, size - 8, size - 8, Soot);
        // Corner piers, lit on their north-west faces.
        foreach (var (px, py) in new[] { (x, y), (x + size - 5, y), (x, y + size - 5), (x + size - 5, y + size - 5) })
        {
            p.Fill(px, py, 5, 5, Doorstep.Edge);
            p.Fill(px + 1, py + 1, 3, 3, Doorstep.Light);
            p.Put(px + 1, py + 1, Doorstep.Highlight);
            p.Put(px + 3, py + 3, Doorstep.Shade);
        }
        // Headstock beam and the bell: a bronze dome lit from the north-west.
        p.Fill(x + 4, cy - 1, size - 8, 2, Timber.Shade);
        p.Disc(cx, cy, 4.2f, Gold.Edge);
        p.Disc(cx, cy, 3.4f, Gold.Shade);
        p.Disc(cx - 0.6f, cy - 0.6f, 2.5f, Gold.Base);
        p.Disc(cx - 1.2f, cy - 1.2f, 1.2f, Gold.Light);
        p.Put(cx - 2, cy - 2, Gold.Highlight);
        p.Fill(cx - 1, cy - 1, 2, 2, Timber.Edge);
    }

    // ----------------------------------------------------------------------
    // Silo.
    // ----------------------------------------------------------------------

    /// <summary>
    /// B3/B6 Silo: a round silo seen from straight above. Its conical board
    /// roof is a set of facets between faint seams; each facet takes a tone
    /// from how much it faces the north-west light, so the cone reads lit on
    /// one side and shaded on the other. A one-pixel edge, a darker eave ring
    /// and a small capped vent at the top finish it.
    /// </summary>
    private static void PaintSilo(Plate p, int w, int h)
    {
        var scale = p.Scale;
        var cx = w / 2f * scale;
        var cy = h / 2f * scale;
        var radius = (Math.Min(w, h) / 2f - 3) * scale;
        var seams = p.Small ? 8 : 16;
        var light = MathF.Atan2(-1, -1); // north-west
        p.Disc(cx + p.P(2), cy + p.P(3), radius, Shadow);
        for (var y = (int)(cy - radius - 1); y <= (int)(cy + radius + 1); y++)
            for (var x = (int)(cx - radius - 1); x <= (int)(cx + radius + 1); x++)
            {
                var dx = x + 0.5f - cx;
                var dy = y + 0.5f - cy;
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance > radius) continue;
                var angle = MathF.Atan2(dy, dx);
                var sector = MathF.Floor((angle + MathF.PI) / MathF.Tau * seams);
                var centre = (sector + 0.5f) / seams * MathF.Tau - MathF.PI;
                // 0 = facing away from the light, 1 = facing it; spread over shade..light.
                var facing = (MathF.Cos(centre - light) + 1) / 2;
                var tone = Tone(SiloWood, 1 + facing * 2.2f);
                Color c;
                if (distance > radius - 1) c = SiloWood.Edge;
                else if (distance > radius - 2) c = tone.Lerp(SiloWood.Edge, 0.45f);
                else
                {
                    var boundary = (angle + MathF.PI) / MathF.Tau * seams - sector;
                    var nearSeam = MathF.Min(boundary, 1 - boundary) * MathF.Tau / seams * distance < 0.5f;
                    c = nearSeam && distance > (p.Small ? 1.5f : 3.5f) ? tone.Lerp(SiloWood.Edge, 0.28f) : tone;
                }
                p.Put(x, y, c);
            }
        // The vent cap at the apex: a dark rim around a lit lid.
        if (p.Small)
        {
            p.Put((int)cx, (int)cy, SiloWood.Edge);
            p.Put((int)cx - 1, (int)cy - 1, SiloWood.Highlight);
            return;
        }
        p.Disc(cx + 1, cy + 1, 2.4f, RoofShadow);
        p.Disc(cx, cy, 2.4f, SiloWood.Edge);
        p.Disc(cx - 0.3f, cy - 0.3f, 1.5f, SiloWood.Light);
        p.Put((int)cx - 1, (int)cy - 1, SiloWood.Highlight);
    }

    /// <summary>A tone between ramp steps: 1.5 is halfway from shade to base.</summary>
    private static Color Tone(Ramp r, float step)
    {
        var low = (int)MathF.Floor(step);
        return r[low].Lerp(r[low + 1], step - low);
    }

    // ----------------------------------------------------------------------
    // Market and stalls.
    // ----------------------------------------------------------------------

    /// <summary>
    /// B3 Market: a paved square with a striped canvas pavilion in the middle
    /// and stalls in the corners under awnings of different colours, each
    /// with crates of produce. The corner on the way in from the door stays open.
    /// </summary>
    private static void PaintMarket(Plate p, int w, int h, BuildingDoor door)
    {
        Flagstones(p, p.Px(new Rect2(2, 2, w - 4, h - 4)), 77);
        var middle = door.Tile is { } t ? t * 32 + 16 : door.Side is DoorSide.South or DoorSide.North ? w / 2f : h / 2f;
        var corridor = door.Side switch
        {
            DoorSide.North => new Rect2(middle - 7, 0, 14, h / 2f),
            DoorSide.East => new Rect2(w / 2f, middle - 7, w / 2f, 14),
            DoorSide.West => new Rect2(0, middle - 7, w / 2f, 14),
            _ => new Rect2(middle - 7, h / 2f, 14, h / 2f),
        };
        var pavilion = new Rect2(w / 2f - 15, h / 2f - 12, 30, 24);
        var roof = p.Px(pavilion);
        PaintRoof(p, roof, new Recipe(Cloth, Material.Canvas, RoofShape.Hip, Stripe: Berry), 70);
        Pennant(p, roof.Position.X + roof.Size.X / 2, roof.Position.Y + roof.Size.Y / 2 - 1);
        var stalls = new (Rect2 Area, DoorSide Front, Ramp Stripe, Ramp Produce)[]
        {
            (new Rect2(4, 4, 17, 13), DoorSide.South, Leaf, Fruit),
            (new Rect2(w - 21, 4, 17, 13), DoorSide.South, Gold, Berry),
            (new Rect2(4, h - 17, 17, 13), DoorSide.North, Lake, Leaf),
            (new Rect2(w - 21, h - 17, 17, 13), DoorSide.North, Fruit, Gold),
        };
        var index = 0;
        foreach (var (area, front, stripe, produce) in stalls)
        {
            index++;
            if (Overlaps(area, corridor)) continue;
            Stall(p, area, front, stripe, produce, index * 13, false);
        }
    }

    /// <summary>A market stall on its own tile, facing the door side, with a gap for the path.</summary>
    private static void PaintStallLot(Plate p, int w, int h, BuildingDoor door)
    {
        var area = new Rect2(4, 4, w - 8, h - 9);
        Stall(p, area, door.Side, Berry, Fruit, 5, true);
        if (door.Tile is null) return;
        // The path runs from the crates to the footprint edge along the door axis.
        var middle = p.P(door.Tile.Value * 32 + 16);
        var lot = p.Px(area);
        var front = door.Side switch
        {
            DoorSide.North => new Rect2I(lot.Position.X, lot.Position.Y, lot.Size.X, 1),
            DoorSide.East => new Rect2I(lot.End.X - 1, lot.Position.Y, 1, lot.Size.Y),
            DoorSide.West => new Rect2I(lot.Position.X, lot.Position.Y, 1, lot.Size.Y),
            _ => new Rect2I(lot.Position.X, lot.End.Y - 1, lot.Size.X, 1),
        };
        Path(p, front, door.Side, middle, 1);
    }

    /// <summary>
    /// One stall in <paramref name="area"/> (units): an awning at the back
    /// sloping toward <paramref name="front"/>, and crates of produce on the
    /// counter in front of it. With <paramref name="gap"/> the middle stays clear.
    /// </summary>
    private static void Stall(Plate p, Rect2 area, DoorSide front, Ramp stripe, Ramp produce, int salt, bool gap)
    {
        var across = front is DoorSide.South or DoorSide.North;
        var alongSize = across ? area.Size.X : area.Size.Y;
        var depthSize = across ? area.Size.Y : area.Size.X;
        var awningDepth = MathF.Round(depthSize * 0.6f);
        Awning(p, p.Px(Orient(area, front, 0, 0, alongSize, awningDepth)), front, stripe);
        var crateDepth = Math.Min(5f, depthSize - awningDepth);
        var crates = gap ? new[] { 1f, 6f, alongSize - 11, alongSize - 6 } : new[] { 1f, 6f, 11f };
        var ramps = new[] { produce, Leaf, Berry, Gold };
        for (var i = 0; i < crates.Length; i++)
        {
            if (crates[i] + 5 > alongSize) continue;
            Crate(p, p.Px(Orient(area, front, crates[i], awningDepth, 5, crateDepth)), ramps[(i + salt) % ramps.Length]);
        }
    }

    /// <summary>
    /// Maps a rectangle written in a frame where <paramref name="front"/>
    /// is "down" (depth runs from the back toward the front) into units.
    /// </summary>
    private static Rect2 Orient(Rect2 area, DoorSide front, float along, float depth, float alongSize, float depthSize) => front switch
    {
        DoorSide.North => new Rect2(area.Position.X + along, area.End.Y - depth - depthSize, alongSize, depthSize),
        DoorSide.East => new Rect2(area.Position.X + depth, area.Position.Y + along, depthSize, alongSize),
        DoorSide.West => new Rect2(area.End.X - depth - depthSize, area.Position.Y + along, depthSize, alongSize),
        _ => new Rect2(area.Position.X + along, area.Position.Y + depth, alongSize, depthSize),
    };

    /// <summary>A small open crate of produce: timber rim, produce heaped inside with a lit top-left.</summary>
    private static void Crate(Plate p, Rect2I box, Ramp produce)
    {
        if (box.Size.X < 2 || box.Size.Y < 2) return;
        p.Fill(new Rect2I(box.Position.X + 1, box.Position.Y + 1, box.Size.X, box.Size.Y), SmallShadow);
        p.Fill(box, Timber.Edge);
        var inner = new Rect2I(box.Position.X + 1, box.Position.Y + 1, box.Size.X - 2, box.Size.Y - 2);
        if (inner.Size.X <= 0 || inner.Size.Y <= 0)
        {
            p.Fill(box.Position.X, box.Position.Y, 1, 1, produce.Base);
            return;
        }
        p.Fill(inner, produce.Base);
        p.Put(inner.Position.X, inner.Position.Y, produce.Light);
        if (inner.Size.X > 2) p.Put(inner.Position.X + 2, inner.Position.Y, produce.Light);
        if (inner.Size.Y > 1) p.Put(inner.End.X - 1, inner.End.Y - 1, produce.Shade);
    }

    /// <summary>The pavilion's finial: a pole top with a little gold pennant flying east.</summary>
    private static void Pennant(Plate p, int x, int y)
    {
        if (p.Small)
        {
            p.Put(x, y, Timber.Edge);
            p.Put(x + 1, y, Gold.Light);
            return;
        }
        p.Fill(x - 1, y - 1, 2, 2, Timber.Edge);
        p.Fill(x + 1, y - 2, 4, 1, Gold.Light);
        p.Fill(x + 1, y - 1, 3, 1, Gold.Base);
        p.Put(x + 1, y, Gold.Shade);
    }

    /// <summary>Clamps to [min, max], or takes the middle when a small footprint leaves no room.</summary>
    private static float Fit(float value, float min, float max) => max < min ? (min + max) / 2 : Math.Clamp(value, min, max);

    /// <summary>Clamps to [min, max], or takes the middle when a small footprint leaves no room.</summary>
    private static int Fit(int value, int min, int max) => max < min ? (min + max) / 2 : Math.Clamp(value, min, max);

    /// <summary>Whether two rectangles in units share any area.</summary>
    private static bool Overlaps(Rect2 a, Rect2 b) =>
        a.Position.X < b.End.X && b.Position.X < a.End.X && a.Position.Y < b.End.Y && b.Position.Y < a.End.Y;

    // ----------------------------------------------------------------------
    // Port.
    // ----------------------------------------------------------------------

    /// <summary>
    /// B3/B6 Port: the north row is on land, a plank shed whose door faces
    /// the Road; the other rows are a plank pier on piles over the water,
    /// widening to a T-head with iron bollards, crates and a moored boat.
    /// </summary>
    private static void PaintPort(Plate p, int w, int h, BuildingDoor door)
    {
        const float shedBottom = 27;
        var pier = new Rect2(w / 2f - 12, shedBottom - 3, 24, h - 30 - (shedBottom - 3));
        var head = new Rect2(6, h - 30, w - 12, 24);
        foreach (var deck in new[] { pier, head })
        {
            var px = p.Px(deck);
            p.Fill(new Rect2I(px.Position.X + p.P(2), px.Position.Y + p.P(3), px.Size.X, px.Size.Y), Shadow);
        }
        Boat(p, new Rect2(pier.End.X + 4, 46, 9, 28));
        Deck(p, p.Px(pier), 201);
        Deck(p, p.Px(head), 202);
        // Piles standing proud of the deck edges.
        for (var y = 38f; y < head.Position.Y - 4; y += 16)
        {
            Post(p, pier.Position.X - 1, y);
            Post(p, pier.End.X - 2, y);
        }
        foreach (var (x, y) in new[] { (head.Position.X - 1, head.Position.Y - 1), (head.End.X - 2, head.Position.Y - 1), (head.Position.X - 1, head.End.Y - 2), (head.End.X - 2, head.End.Y - 2) })
            Post(p, x, y);
        // Bollards along the outer edge of the T-head, and one for the boat.
        foreach (var (x, y) in new[] { (head.Position.X + 6, head.End.Y - 4), (head.End.X - 6, head.End.Y - 4), (w / 2f, head.End.Y - 4), (pier.End.X - 3, 48f) })
            Bollard(p, x, y);
        // Mooring line from the pier bollard to the boat's bow.
        p.Canvas.Line(pier.End.X - 2, 48, pier.End.X + 7.5f, 47.5f, Cloth.Shade);
        // Goods waiting on the T-head.
        DeckCrate(p, head.Position.X + 9, head.Position.Y + 6);
        DeckCrate(p, head.Position.X + 16, head.Position.Y + 8);
        p.Canvas.Ring(head.End.X - 12, head.Position.Y + 9, 2.5f, Cloth.Shade);
        p.Canvas.Disc(head.End.X - 12, head.Position.Y + 9, 1.2f, Cloth.Base);
        // The shed on the shore.
        var recipe = new Recipe(Timber, Material.Shingle, RoofShape.Hip);
        var roofUnits = new Rect2(9, 5, w - 18, shedBottom - 5);
        var roof = p.Px(roofUnits);
        PaintRoof(p, roof, recipe, 203);
        var middle = p.P(Fit(door.Tile is { } t ? t * 32 + 16 : w / 2f, roofUnits.Position.X + 6, roofUnits.End.X - 6));
        var next = Door(p, roof, DoorSide.North, middle, 3, 2);
        if (door.Tile is not null) Path(p, roof, DoorSide.North, middle, next);
    }

    /// <summary>A plank deck: planks laid across it, seams one step darker, ragged plank ends and edge stringers.</summary>
    private static void Deck(Plate p, Rect2I deck, int salt)
    {
        var depth = p.Small ? 2 : 4;
        var eastWest = deck.Size.Y >= deck.Size.X;
        int length = eastWest ? deck.Size.X : deck.Size.Y, breadth = eastWest ? deck.Size.Y : deck.Size.X;
        for (var j = 0; j < breadth; j++)
        {
            var course = j / depth;
            var row = j % depth;
            var shortStart = PixelArt.Hash(course, 0, salt) % 2 == 0;
            var shortEnd = PixelArt.Hash(course, 1, salt) % 2 == 0;
            var tone = PixelArt.Hash(course, 2, salt) % 4 == 0 ? 3 : 2;
            for (var i = 0; i < length; i++)
            {
                if ((i == 0 && shortStart) || (i == length - 1 && shortEnd)) continue;
                Color c;
                if (row == depth - 1) c = Timber.Shade;
                else if (i <= 1 || i >= length - 2) c = Timber.Shade;
                else if (row == 0 && !p.Small) c = Timber[tone + 1];
                else c = Timber[tone];
                if (!p.Small && row != depth - 1 && PixelArt.Hash(i, j, salt + 4) % 37 == 0) c = Timber.Shade;
                if (eastWest) p.Put(deck.Position.X + i, deck.Position.Y + j, c);
                else p.Put(deck.Position.X + j, deck.Position.Y + i, c);
            }
        }
    }

    /// <summary>A pile head standing at a deck edge: timber, lit north-west, with a small shadow.</summary>
    private static void Post(Plate p, float x, float y)
    {
        var px = p.P(x);
        var py = p.P(y);
        var size = p.Small ? 2 : 3;
        p.Fill(px + 1, py + 1, size, size, SmallShadow);
        p.Fill(px, py, size, size, Timber.Edge);
        if (!p.Small) p.Put(px + 1, py + 1, Timber.Light);
    }

    /// <summary>B6 Port: an iron mooring bollard, a dark ring with a lit cap.</summary>
    private static void Bollard(Plate p, float x, float y)
    {
        var c = p.Canvas;
        c.Disc(x + 1, y + 1.5f, 2.4f, SmallShadow);
        c.Disc(x, y, 2.4f, Iron.Edge);
        c.Disc(x - 0.3f, y - 0.3f, 1.5f, Iron.Base);
        c.Dot(x - 1, y - 1, Iron.Highlight);
    }

    /// <summary>A closed crate on the deck: lid boards with a seam, lit top edge.</summary>
    private static void DeckCrate(Plate p, float x, float y)
    {
        var c = p.Canvas;
        c.Rect(x + 1, y + 2, 6, 6, SmallShadow);
        c.Rect(x, y, 6, 6, Timber.Edge);
        c.Rect(x + 1, y + 1, 4, 4, Timber.Light);
        c.Rect(x + 1, y + 1, 4, 1, Timber.Highlight);
        c.Rect(x + 1, y + 3, 4, 1, Timber.Base);
    }

    /// <summary>A rowing boat moored beside the pier: hull, darker inside, two thwarts, shadow on the water.</summary>
    private static void Boat(Plate p, Rect2 area)
    {
        var c = p.Canvas;
        var cx = area.Position.X + area.Size.X / 2;
        var cy = area.Position.Y + area.Size.Y / 2;
        var rx = area.Size.X / 2;
        var ry = area.Size.Y / 2;
        c.Ellipse(cx + 2, cy + 3, rx, ry, Shadow);
        c.Ellipse(cx, cy, rx, ry, Timber.Edge);
        c.Ellipse(cx - 0.3f, cy, rx - 1, ry - 1, Timber.Light);
        c.Ellipse(cx + 0.2f, cy + 0.5f, rx - 2.2f, ry - 3, Timber.Shade);
        c.Rect(cx - rx + 2, cy - 5, rx * 2 - 4, 1, Timber.Base);
        c.Rect(cx - rx + 2, cy + 4, rx * 2 - 4, 1, Timber.Base);
    }

    // ----------------------------------------------------------------------
    // Small helpers.
    // ----------------------------------------------------------------------

    /// <summary>A five-step colour ramp (STYLE.md section 2): edge, shade, base, light, highlight.</summary>
    private readonly record struct Ramp(Color Edge, Color Shade, Color Base, Color Light, Color Highlight)
    {
        public static Ramp Of(string edge, string shade, string @base, string light, string highlight) =>
            new(new Color(edge), new Color(shade), new Color(@base), new Color(light), new Color(highlight));

        /// <summary>Step 0 is the edge, 4 the highlight; out-of-range steps clamp.</summary>
        public Color this[int step] => step switch
        {
            <= 0 => Edge,
            1 => Shade,
            2 => Base,
            3 => Light,
            _ => Highlight,
        };
    }

    /// <summary>
    /// The image a building is painted on, with the scale from the 32-unit
    /// tile space layouts are written in to output pixels (1 at 32 px, 0.5
    /// at 16 px). Painting blends, so shadows and soft edges compose.
    /// </summary>
    private sealed class Plate
    {
        public Plate(int width, int height, float scale)
        {
            Width = width;
            Height = height;
            Scale = scale;
            Image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            Image.Fill(Colors.Transparent);
        }

        public Image Image { get; }
        public int Width { get; }
        public int Height { get; }
        public float Scale { get; }
        public bool Small => Scale < 1;

        /// <summary>A canvas over the whole plate, for discs and lines in units.</summary>
        public PixelCanvas Canvas => new(Image, new Rect2I(0, 0, Width, Height), Scale);

        /// <summary>Units to pixels, rounded.</summary>
        public int P(float units) => (int)MathF.Round(units * Scale);

        public Rect2I Px(Rect2 units)
        {
            var left = P(units.Position.X);
            var top = P(units.Position.Y);
            return new Rect2I(left, top, P(units.End.X) - left, P(units.End.Y) - top);
        }

        public void Put(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || color.A <= 0) return;
            Image.SetPixel(x, y, Image.GetPixel(x, y).Blend(color));
        }

        public void Fill(Rect2I area, Color color)
        {
            for (var y = area.Position.Y; y < area.End.Y; y++)
                for (var x = area.Position.X; x < area.End.X; x++)
                    Put(x, y, color);
        }

        public void Fill(int x, int y, int width, int height, Color color) => Fill(new Rect2I(x, y, width, height), color);

        /// <summary>A disc centred on a pixel position, radius in pixels.</summary>
        public void Disc(float x, float y, float radius, Color color) =>
            Canvas.Disc(x / Scale, y / Scale, radius / Scale, color);
    }
}
