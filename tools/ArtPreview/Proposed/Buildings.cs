using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ArtPreview.Proposed.Buildings;

/// <summary>
/// Building exteriors (STYLE.md section 9), rounds 1 and 2, plus the two
/// vehicles that park beside them: the handcart and the rowing boat.
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
/// Store, Market, Market stall, Town Hall, Port, Restaurant and Clinic are
/// agreed but have no <see cref="BuildingKind"/> yet, so they are drawn
/// through the private <see cref="Design"/> list and only yielded for review.
/// </para>
/// <para>
/// Round 2 (after the owner's review of October 1) keeps every approved
/// drawing pixel for pixel, replaces the Farmhouse's painted hay cart with
/// sheaves and grain sacks (carts become a real, usable vehicle), adds the
/// remaining footprints and door sides, Workshop and Generic in the new
/// style, the Restaurant, the Port in four rotations, a full Market plot,
/// and the handcart and rowing boat as their own sprites.
/// </para>
/// <para>
/// Round 3 (after the owner's second review) again keeps every approved
/// drawing pixel for pixel, except the Market: it becomes a market hall with
/// no stalls inside, and the Market plot becomes a small plaza of packed
/// earth with the stalls standing on it. It adds a Port with six boats
/// moored in each rotation, and the four diagonal facings of the handcart
/// and the boat, with a turnaround strip for each.
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
    private static readonly Ramp GreenPlank = Ramp.Of("30372D", "566150", "6F7C6A", "8E9B88", "A8B4A2");
    /// <summary>
    /// A plain building with no recognised tags: the first four steps are
    /// the game's current Generic palette; the highlight is one step on,
    /// toward Cloth highlight, because the style guide has no row for it.
    /// </summary>
    private static readonly Ramp Plain = Ramp.Of("3F3A33", "6E675C", "8D8577", "AAA293", "C2BBAE");
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

    /// <summary>
    /// Every design this proposal draws: the current kinds plus the agreed new
    /// ones. New designs are appended, because each design's number seeds its
    /// roof texture and the approved drawings must not change.
    /// </summary>
    private enum Design { House, Warehouse, Farmhouse, Blacksmith, Silo, TailorShop, Store, Market, MarketStall, TownHall, Port, Clinic, Workshop, Generic, Restaurant }

    /// <summary>B3 roof materials; each has its own course pattern in <see cref="Surface"/>.</summary>
    private enum Material { Clay, Slate, Thatch, Shingle, Plank }

    /// <summary>B4: a gable has two faces along the ridge; a hip adds triangular end faces.</summary>
    private enum RoofShape { Gable, Hip }

    /// <summary>The roof face a pixel lies on, named by the eave it slopes down to.</summary>
    private enum Face { North, West, East, South }

    /// <summary>
    /// How a roofed design is built: its roof ramp and material, gabled or
    /// hipped, the depth of a side yard (0 for none) and how far the roof
    /// stands back from the door side, so door, doorstep and path fit.
    /// </summary>
    private readonly record struct Recipe(Ramp Roof, Material Material, RoofShape Shape, float Yard = 0, float Clearance = 5);

    private static Recipe RecipeFor(Design design) => design switch
    {
        Design.House => new(Clay, Material.Clay, RoofShape.Hip),
        // The roof stands back eight units on the door side so the grain sacks fit beside the doorstep.
        Design.Farmhouse => new(Thatch, Material.Thatch, RoofShape.Hip, Yard: 18, Clearance: 8),
        Design.Blacksmith => new(Slate, Material.Slate, RoofShape.Gable, Yard: 20),
        Design.Warehouse => new(GreyTimber, Material.Plank, RoofShape.Gable),
        Design.TailorShop => new(DyedShingle, Material.Shingle, RoofShape.Hip),
        Design.Store => new(Timber, Material.Shingle, RoofShape.Hip, Clearance: 10),
        Design.TownHall => new(Slate, Material.Slate, RoofShape.Hip, Clearance: 22),
        Design.Clinic => new(Cloth, Material.Shingle, RoofShape.Hip),
        Design.Workshop => new(GreenPlank, Material.Plank, RoofShape.Gable, Yard: 16),
        // Round 3: the Market is a timber hall; its planks run along the eaves of a hipped roof.
        Design.Market => new(Timber, Material.Plank, RoofShape.Hip),
        Design.Generic => new(Plain, Material.Shingle, RoofShape.Hip),
        // A gable tells the Restaurant's clay roof apart from the hipped House; the yard is a dining terrace.
        Design.Restaurant => new(Clay, Material.Clay, RoofShape.Gable, Yard: 20),
        _ => new(Timber, Material.Plank, RoofShape.Gable),
    };

    /// <summary>One review picture of a building: design, footprint, door, Id and the note shown with it.</summary>
    /// <param name="PaintedBoat">Only the approved round-1 Port: it keeps the rowing boat painted beside its pier.</param>
    private readonly record struct Sample(Design Design, int W, int H, DoorSide Side, int Tile, string Id, string Note, bool PaintedBoat = false);

    /// <summary>Round-1 review set at 32 px. Everything here was approved on October 1 except the Farmhouse.</summary>
    private static readonly Sample[] Catalogue =
    [
        new(Design.House, 1, 1, DoorSide.South, 0, "House.1x1", "Hipped clay-tile roof, capped chimney on the shaded east face, lintel, door and doorstep."),
        new(Design.House, 1, 2, DoorSide.South, 0, "House.1x2", "Long hipped clay roof: ridge along the long side, hips at both ends, chimney on the shaded half."),
        new(Design.House, 1, 2, DoorSide.East, 0, "House.1x2.door_East", "Same House facing a Road to the east: door, doorstep and path start move with it."),
        new(Design.Farmhouse, 1, 2, DoorSide.South, 0, "Farmhouse.1x2", "Round 2: hipped thatch and no painted cart. Bound sheaves of grain laid out in the yard behind, grain sacks by the door."),
        new(Design.Blacksmith, 2, 2, DoorSide.South, 1, "Blacksmith.2x2", "Slate roof in 6 x 3 slabs; open forge yard with the hearth's ember glow, anvil and quench barrel."),
        new(Design.Warehouse, 2, 2, DoorSide.South, 1, "Warehouse.2x2", "Grey timber planks along the ridge; wide split loading doors over a stone apron."),
        new(Design.Silo, 1, 1, DoorSide.South, 0, "Silo.1x1", "Conical board roof in lit and shaded facets with seams, an iron band and a capped vent."),
        new(Design.TailorShop, 1, 1, DoorSide.South, 0, "TailorShop.1x1", "Dyed purple shingles; a spool sign above the door."),
        new(Design.Store, 1, 1, DoorSide.South, 0, "Store.1x1", "New: brown shingle roof with a red-and-cream striped awning over the door."),
        new(Design.Market, 2, 2, DoorSide.South, 1, "Market.2x2", "Round 3: the Market hall, with no stalls inside. Hipped timber-plank roof, a louvred lantern on the ridge flying the gold pennant, and a lean-to over an open arcade along the front with a sign of trading scales."),
        new(Design.MarketStall, 1, 1, DoorSide.South, 0, "MarketStall.1x1", "New: one striped stall awning with crates of produce facing the Road."),
        new(Design.TownHall, 3, 4, DoorSide.South, 1, "TownHall.3x4", "New: large hipped slate roof, open bell tower with a bronze bell, paved forecourt and wide steps."),
        new(Design.Port, 2, 4, DoorSide.North, 1, "Port.2x4", "New: plank shed on the shore facing the Road; a plank pier on piles with bollards and a moored boat.", PaintedBoat: true),
        new(Design.Clinic, 1, 1, DoorSide.South, 0, "Clinic.1x1", "New: pale shingle roof, a green cross sign and a herb planter by the door."),
    ];

    /// <summary>Round-2 set at 32 px: the remaining footprints, kinds, door sides and the Port's four rotations.</summary>
    private static readonly Sample[] Round2 =
    [
        new(Design.Farmhouse, 1, 1, DoorSide.South, 0, "Farmhouse.1x1", "Round 2: no room for a yard, so the one-tile Farmhouse is known by its thatch and the grain sacks by the door."),
        new(Design.Farmhouse, 1, 2, DoorSide.East, 0, "Farmhouse.1x2.door_East", "Round 2: the same Farmhouse facing a Road to the east; sacks, door and yard move with it."),
        new(Design.House, 2, 2, DoorSide.South, 1, "House.2x2", "Square hipped clay roof with four hips meeting at a point; chimney on the shaded east face."),
        new(Design.House, 1, 2, DoorSide.North, 0, "House.1x2.door_North", "Door on the north: the roof stands back so door and doorstep fit; the chimney moves to the far end."),
        new(Design.House, 1, 2, DoorSide.West, 0, "House.1x2.door_West", "Door on the west, toward a Road on that side."),
        new(Design.Warehouse, 2, 3, DoorSide.South, 1, "Warehouse.2x3", "The long Warehouse: grey planks along a north-south ridge, loading doors to the Road."),
        new(Design.Warehouse, 2, 2, DoorSide.North, 1, "Warehouse.2x2.door_North", "Loading doors and apron on the north side."),
        new(Design.Blacksmith, 1, 2, DoorSide.South, 0, "Blacksmith.1x2", "The narrow Blacksmith: forge yard behind the slate roof with hearth and anvil."),
        new(Design.Blacksmith, 2, 2, DoorSide.West, 1, "Blacksmith.2x2.door_West", "Door on the west; the forge yard takes the end away from the door."),
        new(Design.TailorShop, 2, 2, DoorSide.South, 1, "TailorShop.2x2", "Square hipped roof of dyed shingles with the spool sign above the door."),
        new(Design.Workshop, 2, 2, DoorSide.South, 1, "Workshop.2x2", "Redrawn: green plank gable, hammer sign over the door and a small work yard with a trestle bench, a saw and a plank stack."),
        new(Design.Generic, 1, 1, DoorSide.South, 0, "Generic.1x1", "Redrawn: any building the game cannot name; plain grey-brown shingles, a door and nothing else."),
        new(Design.Store, 1, 2, DoorSide.South, 0, "Store.1x2", "The long Store: the striped awning runs along the shop front."),
        new(Design.Store, 1, 1, DoorSide.East, 0, "Store.1x1.door_East", "The Store facing a Road to the east: the awning moves to that side."),
        new(Design.Restaurant, 1, 2, DoorSide.South, 0, "Restaurant.1x2", "New: clay-tile gable roof, a chimney and a sign with a pot of stew over the door."),
        new(Design.Restaurant, 2, 2, DoorSide.South, 1, "Restaurant.2x2", "New: the larger Restaurant adds a paved terrace with two tables and benches."),
        new(Design.Clinic, 1, 2, DoorSide.South, 0, "Clinic.1x2", "The long Clinic: pale shingles, the green cross sign and the herb planter by the door."),
        new(Design.Port, 2, 4, DoorSide.North, 1, "Port.2x4.N", "Port with its land row to the north. The painted boat is gone: the real boat moors beside the pier."),
        new(Design.Port, 4, 2, DoorSide.East, 1, "Port.4x2.E", "Port with its land column to the east and the pier reaching west over the water."),
        new(Design.Port, 2, 4, DoorSide.South, 1, "Port.2x4.S", "Port with its land row to the south and the pier reaching north."),
        new(Design.Port, 4, 2, DoorSide.West, 1, "Port.4x2.W", "Port with its land column to the west and the pier reaching east."),
    ];

    /// <summary>The 16 px mid-zoom set: design, footprint, Id and note. The door is on the south, on the middle tile.</summary>
    private static readonly (Design Design, int W, int H, string Id, string Note)[] Catalogue16 =
    [
        (Design.House, 1, 1, "House.1x1.16", "16 px: two tones per face, hip lines, chimney, door and step."),
        (Design.Blacksmith, 2, 2, "Blacksmith.2x2.16", "16 px: slate in two tones, ember and anvil kept in the yard."),
        (Design.House, 2, 2, "House.2x2.16", "16 px: square hipped roof in two tones, chimney, door and step."),
        (Design.Warehouse, 2, 3, "Warehouse.2x3.16", "16 px: two-tone planks along the ridge and the split loading doors."),
        (Design.Market, 2, 2, "Market.2x2.16", "Round 3, 16 px: the hall in two-tone planks, the grey lantern and its pennant, the arcade roof, the sign, door and step."),
    ];

    public IEnumerable<Entry> Render()
    {
        foreach (var sample in Catalogue.Concat(Round2))
        {
            var sprite = Draw(sample.Design, sample.W, sample.H, 32, new BuildingDoor(sample.Side, sample.Tile), sample.PaintedBoat);
            var ground = sample.Design == Design.Port ? Shore(sample.W, sample.H, PortLandSide(sample.W, sample.H, sample.Side)) : Grass(sample.W, sample.H, 32);
            yield return new(Family, sample.Id, Bitmap.Over(ground, sprite, 0, 0), sample.Note);
            yield return new(Family, sample.Id + ".sprite", sprite, sample.Note);
        }
        foreach (var (design, w, h, id, note) in Catalogue16)
        {
            var sprite = Draw(design, w, h, 16, new BuildingDoor(DoorSide.South, w / 2));
            yield return new(Family, id, Bitmap.Over(Grass(w, h, 16), sprite, 0, 0), note);
            yield return new(Family, id + ".sprite", sprite, note);
        }
        foreach (var entry in VehicleEntries()) yield return entry;
        foreach (var (id, w, h, side, water, note) in MooredPorts)
            yield return new(Family, id, MooredPort(w, h, side, water), note);
        yield return new(Family, "Market.plaza", MarketPlaza(32),
            "Round 3: the market hall at the head of a small plaza of packed earth (Road tiles drawn as one area), eight approved stalls standing on it and a Road leading in.");
        yield return new(Family, "Market.plaza.16", MarketPlaza(16),
            "Round 3, 16 px: the same Market plaza at mid zoom.");
    }

    /// <summary>
    /// Draws the redrawn kinds at any footprint and door side, Workshop and
    /// Generic included; only the retired kinds (Shelter, Storehouse, Hearth,
    /// Path, Bedroll) keep the game's current drawing.
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
        BuildingKind.Workshop => Design.Workshop,
        BuildingKind.Generic => Design.Generic,
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

    /// <summary>Grass on the Port's land row or column and River tiles everywhere else.</summary>
    private static Image Shore(int tilesWide, int tilesHigh, DoorSide land)
    {
        var ground = Grass(tilesWide, tilesHigh, 32);
        var atlas = WaterTextures.Atlas(32).GetImage();
        for (var y = 0; y < tilesHigh; y++)
            for (var x = 0; x < tilesWide; x++)
            {
                var onLand = land switch
                {
                    DoorSide.North => y == 0,
                    DoorSide.South => y == tilesHigh - 1,
                    DoorSide.West => x == 0,
                    _ => x == tilesWide - 1,
                };
                if (!onLand) ground.BlitRect(atlas, (Rect2I)WaterTextures.Region(TerrainStyle.River, x, y, 32), new Vector2I(x * 32, y * 32));
            }
        return ground;
    }

    /// <summary>One River tile at 32 px, for the boat.</summary>
    private static Image RiverTile(int x, int y) =>
        WaterTextures.Atlas(32).GetImage().GetRegion((Rect2I)WaterTextures.Region(TerrainStyle.River, x, y, 32));

    // ----------------------------------------------------------------------
    // Entry point: the same shape as BuildingSprites.Render.
    // ----------------------------------------------------------------------

    /// <summary>
    /// Draws one design over its footprint, transparent outside the building.
    /// <paramref name="paintedBoat"/> keeps the approved round-1 Port's painted boat.
    /// </summary>
    private static Image Draw(Design design, int tilesWide, int tilesHigh, int tilePixels, BuildingDoor door, bool paintedBoat = false)
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
                PaintPort(plate, w, h, door, paintedBoat);
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
        // The Restaurant's dining terrace needs a footprint at least two tiles each way.
        if (design == Design.Restaurant && (w <= 32 || h <= 32)) recipe = recipe with { Yard = 0 };
        var doorHalf = design switch { Design.Warehouse => 7f, Design.TownHall => 5f, _ => 3f };
        var plan = Lay(w, h, door, recipe, doorHalf);
        var roof = p.Px(plan.Roof);
        var middle = p.P(plan.DoorMiddle);

        if (design == Design.TownHall) Forecourt(p, roof, door.Side);
        if (plan.Yard is { } yardUnits)
        {
            var yardPixels = p.Px(yardUnits);
            var inset = p.P(2);
            var patch = new Rect2I(yardPixels.Position.X + inset, yardPixels.Position.Y + inset, yardPixels.Size.X - inset * 2, yardPixels.Size.Y - inset * 2);
            switch (design)
            {
                case Design.Blacksmith:
                    YardFloor(p, yardPixels, Dirt.Shade, Dirt.Edge, 113);
                    break;
                case Design.Restaurant:
                    Flagstones(p, patch, 167);
                    break;
                default:
                    // The farmyard and the work yard are smaller worn patches, not the whole end.
                    YardFloor(p, patch, Dirt.Base, Dirt.Shade, design == Design.Farmhouse ? 117 : 131);
                    break;
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
                if (plan.Yard is { } farmYard) Sheaves(p, p.Px(farmYard));
                nextRow = Door(p, roof, door.Side, middle, 3, 3);
                GrainSacks(p, roof, door.Side, middle);
                break;
            case Design.Workshop:
                if (plan.Yard is { } workYard) WorkYard(p, p.Px(workYard));
                var hammer = Inward(p, roof, door.Side, middle, 9);
                HammerSign(p, hammer.X, hammer.Y);
                nextRow = Door(p, roof, door.Side, middle, 3, 3);
                break;
            case Design.Generic:
                nextRow = Door(p, roof, door.Side, middle, 3, 3);
                break;
            case Design.Restaurant:
                if (plan.Yard is { } terrace) Terrace(p, p.Px(terrace));
                var (kx, ky) = ChimneySpot(roof, p.Small, door.Side, middle);
                Chimney(p, kx, ky, false);
                var pot = Inward(p, roof, door.Side, middle, 8);
                PotSign(p, pot.X, pot.Y);
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
            _ => Straw(tones, along, across, small, salt),
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
        // A short yard (the 1 x 2 Blacksmith) has room for the hearth and anvil only.
        var compact = (tall ? yard.Size.Y : yard.Size.X) < p.P(40);
        var hearth = At(compact ? 0.27f : 0.22f);
        var anvil = At(compact ? 0.76f : 0.52f);
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
        if (compact) return;
        int bx = barrel.X, by = barrel.Y;
        p.Disc(bx + 1.5f, by + 2.5f, 3.5f, SmallShadow);
        p.Disc(bx + 0.5f, by + 0.5f, 3.5f, Timber.Edge);
        p.Disc(bx + 0.5f, by + 0.5f, 2.6f, Timber.Light);
        p.Disc(bx + 0.5f, by + 0.5f, 1.8f, River.Shade);
        p.Put(bx - 1, by - 1, River.Highlight);
    }

    /// <summary>
    /// B6 Farmhouse (round 2): bound sheaves of grain laid out to dry in the
    /// yard, every other one turned the other way, as a stack is laid. Each
    /// sheaf has the shape of the grain item icon: three ears on stalks,
    /// tied at the waist. The painted hay cart is gone because carts are now
    /// a real vehicle (<see cref="Handcart"/>).
    /// </summary>
    private static void Sheaves(Plate p, Rect2I yard)
    {
        int sheafWide = p.Small ? 4 : 11, sheafHigh = p.Small ? 6 : 13, gap = 1;
        var wide = yard.Size.X >= yard.Size.Y;
        var room = wide ? yard.Size.X - 2 : yard.Size.Y - 2;
        var step = wide ? sheafWide + gap : sheafHigh + gap;
        var count = Math.Clamp(room / step, 1, 3);
        var span = count * step - gap;
        for (var i = 0; i < count; i++)
        {
            var flip = i % 2 == 1;
            var left = wide ? yard.Position.X + (yard.Size.X - span) / 2 + i * step : yard.Position.X + (yard.Size.X - sheafWide) / 2;
            var top = wide ? yard.Position.Y + (yard.Size.Y - sheafHigh) / 2 : yard.Position.Y + (yard.Size.Y - span) / 2 + i * step;
            WithShadow(p, 1, p.Small ? 1 : 2, q => Sheaf(q, left, top, flip));
        }
    }

    /// <summary>
    /// One bound sheaf lying on the ground, its top-left at (left, top), ears
    /// to the north (south when <paramref name="flip"/>): three ears in Thatch
    /// light with an edge outline and a lit tip, stalks gathering to a twine
    /// band, and the cut stalk ends splayed at the foot.
    /// </summary>
    private static void Sheaf(Plate p, int left, int top, bool flip)
    {
        if (p.Small)
        {
            // Four by six: two ears, the band, the stalk foot.
            string[] rows = ["lh.h", "bllb", ".bs.", ".ts.", ".bs.", "b..s"];
            for (var j = 0; j < rows.Length; j++)
            {
                var row = rows[flip ? rows.Length - 1 - j : j];
                for (var i = 0; i < row.Length; i++)
                {
                    Color? c = row[i] switch { 'l' => Thatch.Light, 'h' => Thatch.Highlight, 'b' => Thatch.Base, 's' => Thatch.Shade, 't' => Timber.Shade, _ => null };
                    if (c is { } colour) p.Put(left + i, top + j, colour);
                }
            }
            return;
        }
        const float height = 13;
        var middle = left + 5.5f;
        float Y(float down) => flip ? top + height - down : top + down;
        var canvas = p.Canvas;
        // Stalks: from each ear down to the waist, then splayed out to the cut ends.
        foreach (var x in new[] { -3f, 0f, 3f })
        {
            canvas.Line(middle + x, Y(6), middle, Y(8.5f), Thatch.Shade);
            canvas.Line(middle, Y(9.5f), middle + x * 0.9f, Y(12.5f), Thatch.Base);
        }
        foreach (var x in new[] { -3f, 3f })
            p.Put((int)(middle + x * 0.9f), (int)Y(12.5f), Thatch.Edge);
        // Ears: the side ears first, the middle one standing a pixel further out over them.
        foreach (var (x, down) in new[] { (-3f, 4f), (3f, 4f), (0f, 3f) })
        {
            canvas.Ellipse(middle + x, Y(down), 2.1f, 3.3f, Thatch.Edge);
            canvas.Ellipse(middle + x, Y(down), 1.2f, 2.4f, Thatch.Light);
            p.Put((int)(middle + x) - 1, (int)Y(down - 1), Thatch.Highlight);
            p.Put((int)(middle + x), (int)Y(down + 1), Thatch.Base);
        }
        // The twine band at the waist.
        p.Fill((int)middle - 1, (int)Y(9), 3, 1, Timber.Shade);
    }

    /// <summary>
    /// Draws something onto a scratch plate, then lays an L2 shadow of its
    /// whole silhouette on <paramref name="p"/> (offset by dx, dy and painted
    /// once, so overlapping parts never darken twice) and the drawing over it.
    /// </summary>
    private static void WithShadow(Plate p, int dx, int dy, Action<Plate> draw)
    {
        var scratch = new Plate(p.Width, p.Height, p.Scale);
        draw(scratch);
        var shadow = new ShadowMask(p.Width, p.Height);
        for (var y = 0; y < p.Height; y++)
            for (var x = 0; x < p.Width; x++)
                if (scratch.Image.GetPixel(x, y).A > 0) shadow.Add(x + dx, y + dy);
        shadow.Paint(p, SmallShadow);
        for (var y = 0; y < p.Height; y++)
            for (var x = 0; x < p.Width; x++)
                p.Put(x, y, scratch.Image.GetPixel(x, y));
    }

    /// <summary>
    /// B6 Farmhouse: grain sacks by the doorstep, one lying on the west or
    /// north side of the step with its tied neck pointing away, one standing
    /// on the other side, and a few spilled grains.
    /// </summary>
    private static void GrainSacks(Plate p, Rect2I roof, DoorSide side, int middle)
    {
        var alongX = side is DoorSide.South or DoorSide.North;
        if (p.Small)
        {
            foreach (var t in new[] { middle - 4, middle + 3 })
            {
                var (sx, sy) = OutPoint(roof, side, 2, t);
                p.Fill((int)sx, (int)sy, 2, 2, SmallShadow);
                p.Put((int)sx - 1, (int)sy - 1, Cloth.Light);
                p.Put((int)sx, (int)sy - 1, Cloth.Base);
                p.Put((int)sx - 1, (int)sy, Cloth.Base);
                p.Put((int)sx, (int)sy, Cloth.Shade);
            }
            return;
        }
        var (lx, ly) = OutPoint(roof, side, 4, middle - 8);
        LyingSack(p, lx, ly, alongX);
        var (sx2, sy2) = OutPoint(roof, side, 4, middle + 7);
        StandingSack(p, sx2, sy2);
        foreach (var (k, t, colour) in new[] { (6, middle + 5, Thatch.Light), (7, middle + 6, Thatch.Base), (7, middle + 9, Thatch.Light) })
        {
            var (gx, gy) = OutPoint(roof, side, k, t);
            p.Put((int)gx, (int)gy, colour);
        }
    }

    /// <summary>
    /// The middle of the pixel <paramref name="k"/> rows out from the roof edge
    /// on the door side, <paramref name="t"/> along it (as <see cref="Out"/>).
    /// </summary>
    private static (float X, float Y) OutPoint(Rect2I roof, DoorSide side, int k, int t) => side switch
    {
        DoorSide.North => (t + 0.5f, roof.Position.Y - k + 0.5f),
        DoorSide.East => (roof.End.X - 1 + k + 0.5f, t + 0.5f),
        DoorSide.West => (roof.Position.X - k + 0.5f, t + 0.5f),
        _ => (t + 0.5f, roof.End.Y - 1 + k + 0.5f),
    };

    /// <summary>
    /// A cloth sack standing on its base, seen from above: a round body lit
    /// north-west with a shaded south-east crescent, and the gathered neck in
    /// the middle, a pale tuft tied round with twine.
    /// </summary>
    private static void StandingSack(Plate p, float cx, float cy)
    {
        p.Disc(cx + 1, cy + 1.5f, 2.6f, SmallShadow);
        p.Disc(cx, cy, 2.75f, Cloth.Edge);
        p.Disc(cx + 0.4f, cy + 0.4f, 1.9f, Cloth.Shade);
        p.Disc(cx - 0.3f, cy - 0.3f, 1.6f, Cloth.Base);
        var x = (int)cx;
        var y = (int)cy;
        p.Put(x - 1, y - 1, Cloth.Light);
        p.Put(x - 1, y, Cloth.Light);
        p.Put(x, y, Cloth.Highlight);
        p.Put(x + 1, y, Timber.Light);
        p.Put(x, y + 1, Timber.Light);
    }

    /// <summary>
    /// A cloth sack lying on its side along the x or y axis: a long rounded
    /// body, lit north-west, with its neck tied off in twine at the far end
    /// and the tuft flaring past it.
    /// </summary>
    private static void LyingSack(Plate p, float cx, float cy, bool alongX)
    {
        var c = p.Canvas;
        var (rx, ry) = alongX ? (3.5f, 2.5f) : (2.5f, 3.5f);
        c.Ellipse(cx + 1, cy + 1.5f, rx, ry, SmallShadow);
        c.Ellipse(cx, cy, rx, ry, Cloth.Edge);
        c.Ellipse(cx + 0.4f, cy + 0.4f, rx - 0.9f, ry - 0.9f, Cloth.Shade);
        c.Ellipse(cx - 0.3f, cy - 0.3f, rx - 1.2f, ry - 1.2f, Cloth.Base);
        c.Ellipse(cx - 1, cy - 1, 0.9f, 0.9f, Cloth.Light);
        // A fold across the body where the cloth sags, and the neck tied off at the west or north end.
        var (x, y) = ((int)cx, (int)cy);
        if (alongX) p.Put(x + 1, y, Cloth.Shade);
        else p.Put(x, y + 1, Cloth.Shade);
        if (alongX)
        {
            var end = (int)(cx - rx);
            p.Put(end, y, Timber.Shade);
            p.Put(end - 1, y, Cloth.Light);
            p.Put(end - 2, y - 1, Cloth.Edge);
            p.Put(end - 2, y, Cloth.Base);
            p.Put(end - 2, y + 1, Cloth.Edge);
        }
        else
        {
            var end = (int)(cy - ry);
            p.Put(x, end, Timber.Shade);
            p.Put(x, end - 1, Cloth.Light);
            p.Put(x - 1, end - 2, Cloth.Edge);
            p.Put(x, end - 2, Cloth.Base);
            p.Put(x + 1, end - 2, Cloth.Edge);
        }
    }

    /// <summary>
    /// Workshop yard: a trestle bench (a long board on two trestles) with a
    /// saw on it and shavings around, and a neat stack of planks, on a worn
    /// patch of earth.
    /// </summary>
    private static void WorkYard(Plate p, Rect2I yard)
    {
        var tall = yard.Size.Y >= yard.Size.X;
        (int X, int Y) At(float f) => tall
            ? (yard.Position.X + yard.Size.X / 2, yard.Position.Y + (int)(yard.Size.Y * f))
            : (yard.Position.X + (int)(yard.Size.X * f), yard.Position.Y + yard.Size.Y / 2);
        var bench = At(0.3f);
        var stack = At(0.72f);
        // Rectangles written along the yard (a) and across it (b), from a centre.
        Rect2I Box((int X, int Y) c, int a, int b, int along, int across) => tall
            ? new Rect2I(c.X + b, c.Y + a, across, along)
            : new Rect2I(c.X + a, c.Y + b, along, across);
        if (p.Small)
        {
            p.Fill(Box(bench, -4, -1, 8, 3), SmallShadow);
            p.Fill(Box(bench, -4, -1, 8, 2), Timber.Light);
            p.Fill(Box(stack, -3, -2, 6, 4), Timber.Edge);
            p.Fill(Box(stack, -2, -1, 4, 2), Timber.Base);
            return;
        }
        var shadow = new ShadowMask(p.Width, p.Height);
        foreach (var r in new[] { Box(bench, -8, -2, 16, 5), Box(bench, -6, -5, 2, 11), Box(bench, 4, -5, 2, 11), Box(stack, -7, -5, 15, 11) })
            shadow.Add(r, 1, 2);
        shadow.Paint(p, SmallShadow);
        // Trestles: short beams across the bench, their splayed feet just showing at the ends.
        foreach (var a in new[] { -6, 4 })
        {
            p.Fill(Box(bench, a, -5, 2, 11), Timber.Edge);
            p.Fill(Box(bench, a, -4, 1, 9), Timber.Shade);
        }
        // The board: pale, lit along its north or west edge, with a saw lying on it.
        p.Fill(Box(bench, -8, -2, 16, 5), Timber.Edge);
        p.Fill(Box(bench, -7, -1, 14, 3), Timber.Light);
        p.Fill(Box(bench, -7, -1, 14, 1), Timber.Highlight);
        p.Fill(Box(bench, -5, 0, 5, 1), Iron.Light);
        p.Fill(Box(bench, -5, 1, 4, 1), Iron.Base);
        p.Fill(Box(bench, 0, 0, 2, 2), Timber.Shade);
        foreach (var (a, b) in new[] { (-9, 4), (7, -4), (9, 3), (-3, 5) })
        {
            var spot = Box(bench, a, b, 1, 1);
            p.Put(spot.Position.X, spot.Position.Y, Timber.Highlight);
        }
        // Plank stack: boards laid side by side, ends a little ragged, seams one step darker.
        p.Fill(Box(stack, -7, -5, 15, 11), Timber.Edge);
        for (var i = 0; i < 3; i++)
        {
            var offset = i == 1 ? 1 : 0;
            var board = Box(stack, -6 + offset, -4 + i * 3, 13 - offset, 3);
            p.Fill(board, Timber.Base);
            p.Fill(Box(stack, -6 + offset, -4 + i * 3, 13 - offset, 1), Timber.Light);
            p.Fill(Box(stack, -6 + offset, -2 + i * 3, 13 - offset, 1), Toward(Timber, 2, 1, 0.6f));
        }
    }

    /// <summary>B6 Workshop: a cream sign board with an iron-headed hammer, as the current game's sign.</summary>
    private static void HammerSign(Plate p, int cx, int cy)
    {
        if (p.Small)
        {
            p.Fill(cx - 1, cy - 1, 4, 4, SmallShadow);
            p.Fill(cx - 2, cy - 2, 4, 4, Timber.Edge);
            p.Fill(cx - 1, cy - 1, 2, 2, Cloth.Light);
            p.Put(cx, cy - 1, Iron.Base);
            return;
        }
        p.Fill(cx - 3, cy - 3, 9, 8, SmallShadow);
        p.Fill(cx - 4, cy - 4, 9, 8, Timber.Edge);
        p.Fill(cx - 3, cy - 3, 7, 6, Cloth.Light);
        p.Fill(cx - 3, cy - 3, 7, 1, Cloth.Highlight);
        // Handle running down to the south-west, head across its top.
        p.Put(cx - 2, cy + 2, Timber.Shade);
        p.Put(cx - 1, cy + 1, Timber.Base);
        p.Put(cx, cy, Timber.Base);
        p.Put(cx - 1, cy + 2, Timber.Edge);
        p.Fill(cx - 1, cy - 3, 4, 2, Iron.Base);
        p.Fill(cx - 1, cy - 3, 4, 1, Iron.Light);
        p.Put(cx + 2, cy - 2, Iron.Edge);
        p.Put(cx + 1, cy - 1, Iron.Edge);
        p.Put(cx - 1, cy - 3, Iron.Highlight);
    }

    /// <summary>B6 Restaurant: a cream sign board with an iron pot of stew seen from above, handles to the sides.</summary>
    private static void PotSign(Plate p, int cx, int cy)
    {
        if (p.Small)
        {
            p.Fill(cx - 1, cy - 1, 4, 4, SmallShadow);
            p.Fill(cx - 2, cy - 2, 4, 4, Timber.Edge);
            p.Fill(cx - 1, cy - 1, 2, 2, Fruit.Base);
            p.Put(cx - 1, cy - 1, Fruit.Light);
            return;
        }
        p.Fill(cx - 3, cy - 3, 9, 8, SmallShadow);
        p.Fill(cx - 4, cy - 4, 9, 8, Timber.Edge);
        p.Fill(cx - 3, cy - 3, 7, 6, Cloth.Light);
        p.Fill(cx - 3, cy - 3, 7, 1, Cloth.Highlight);
        // Rim (iron, lit on the north-west), stew inside, a handle on each side.
        string[] pot =
        [
            ".ree...",
            "rFLFe..",
            "eFFSeh.",
            "heSSe..",
            ".eee...",
        ];
        for (var j = 0; j < pot.Length; j++)
            for (var i = 0; i < pot[j].Length; i++)
            {
                Color? c = pot[j][i] switch
                {
                    'r' => Iron.Light,
                    'e' => Iron.Edge,
                    'h' => Iron.Shade,
                    'F' => Fruit.Base,
                    'L' => Fruit.Light,
                    'S' => Fruit.Shade,
                    _ => null,
                };
                if (c is { } colour) p.Put(cx - 2 + i, cy - 3 + j, colour);
            }
    }

    /// <summary>
    /// Restaurant terrace (2 x 2 only): two plank tables with a bench along
    /// each long side and a bowl on each table, on the flagstones laid by
    /// <see cref="PaintRoofed"/>.
    /// </summary>
    private static void Terrace(Plate p, Rect2I yard)
    {
        var tall = yard.Size.Y >= yard.Size.X;
        foreach (var f in new[] { 0.3f, 0.72f })
        {
            int tx = tall ? yard.Position.X + yard.Size.X / 2 : yard.Position.X + (int)(yard.Size.X * f);
            int ty = tall ? yard.Position.Y + (int)(yard.Size.Y * f) : yard.Position.Y + yard.Size.Y / 2;
            if (p.Small)
            {
                p.Fill(tx - 1, ty, 4, 2, SmallShadow);
                p.Fill(tx - 2, ty - 1, 4, 2, Timber.Light);
                continue;
            }
            // Benches first, then the table between them and its shadow on the flagstones.
            foreach (var by in new[] { ty - 5, ty + 4 })
            {
                p.Fill(tx - 4, by + 1, 9, 2, SmallShadow);
                p.Fill(tx - 5, by, 9, 2, Timber.Edge);
                p.Fill(tx - 4, by, 7, 1, Timber.Base);
            }
            p.Fill(tx - 4, ty - 1, 10, 5, SmallShadow);
            p.Fill(tx - 5, ty - 2, 10, 5, Timber.Edge);
            p.Fill(tx - 4, ty - 1, 8, 3, Timber.Light);
            p.Fill(tx - 4, ty - 1, 8, 1, Timber.Highlight);
            p.Fill(tx - 4, ty + 1, 8, 1, Toward(Timber, 3, 2, 0.6f));
            // A bowl of stew on the table.
            p.Put(tx - 2, ty, Cloth.Highlight);
            p.Put(tx - 1, ty, Fruit.Base);
            p.Put(tx - 1, ty - 1, Cloth.Highlight);
            p.Put(tx, ty, Cloth.Light);
            p.Put(tx + 2, ty, Cloth.Highlight);
        }
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
    // Market hall and stalls.
    // ----------------------------------------------------------------------

    /// <summary>
    /// Round 3 Market: a timber market hall with no stalls inside it; the
    /// stalls stand outside on the plaza (<see cref="MarketPlaza"/>). A
    /// hipped roof of timber planks carries a louvred lantern along its ridge
    /// that flies the approved Market's gold pennant. Along the door side a
    /// lower lean-to roof covers an open arcade, with a sign of trading
    /// scales over the way in and the doorstep and path beyond its eave. It
    /// is laid out once with the door "down" and turned to the door side by
    /// <see cref="PortFrame"/>; every piece is lit from the north-west.
    /// </summary>
    private static void PaintMarket(Plate p, int w, int h, BuildingDoor door)
    {
        var recipe = RecipeFor(Design.Market);
        var side = door.Side;
        var frame = new PortFrame(Opposite(side), w, h);
        float breadth = frame.Breadth, length = frame.Length;
        // The arcade's eave stands seven units in from the footprint edge, leaving room for the doorstep and path.
        const float front = 7, arcadeDepth = 12;
        var arcade = p.Px(frame.Map(4, length - front - arcadeDepth, breadth - 8, arcadeDepth));
        var hall = p.Px(frame.Map(3, 3, breadth - 6, length - front - arcadeDepth - 2));
        var middle = p.P(Fit(door.Tile is { } t ? t * 32 + 16 : breadth / 2f, 14, breadth - 14));
        // The hall roof's eave shadow falls across the lean-to; where both shadows land outside, only one is laid.
        var hallShadow = new Rect2I(hall.Position.X + p.P(2), hall.Position.Y + p.P(3), hall.Size.X, hall.Size.Y);
        LeanTo(p, arcade, side, recipe, (int)Design.Market * 31 + 8, hallShadow);
        ArcadePosts(p, arcade, side, middle);
        PaintRoof(p, hall, recipe, (int)Design.Market * 31 + 7);
        Lantern(p, hall);
        var sign = Inward(p, arcade, side, middle, 6);
        ScalesSign(p, sign.X, sign.Y);
        var (from, to) = Span(p, middle, 3);
        var next = Step(p, arcade, side, from, to, 1, p.Small ? 1 : 3);
        if (door.Tile is not null) Path(p, arcade, side, middle, next);
    }

    /// <summary>The side across the footprint from <paramref name="side"/>.</summary>
    private static DoorSide Opposite(DoorSide side) => side switch
    {
        DoorSide.North => DoorSide.South,
        DoorSide.East => DoorSide.West,
        DoorSide.West => DoorSide.East,
        _ => DoorSide.North,
    };

    /// <summary>
    /// A lean-to roof over <paramref name="area"/> (pixels): one face sloping
    /// down toward <paramref name="front"/>, laid in the recipe's courses
    /// along that eave, with the eave shadow and the one-pixel edge. The main
    /// roof, painted after it, lays its own eave shadow across it, so the
    /// lean-to leaves out its shadow where <paramref name="shadowed"/> already has one.
    /// </summary>
    private static void LeanTo(Plate p, Rect2I area, DoorSide front, Recipe recipe, int salt, Rect2I shadowed)
    {
        int x0 = area.Position.X, y0 = area.Position.Y, w = area.Size.X, h = area.Size.Y;
        if (w < 4 || h < 4) return;
        var shadow = new Rect2I(x0 + p.P(2), y0 + p.P(3), w, h);
        for (var y = shadow.Position.Y; y < shadow.End.Y; y++)
            for (var x = shadow.Position.X; x < shadow.End.X; x++)
                if (!shadowed.HasPoint(new Vector2I(x, y))) p.Put(x, y, Shadow);
        var face = front switch
        {
            DoorSide.North => Face.North,
            DoorSide.East => Face.East,
            DoorSide.West => Face.West,
            _ => Face.South,
        };
        for (var j = 0; j < h; j++)
            for (var i = 0; i < w; i++)
            {
                if (i == 0 || j == 0 || i == w - 1 || j == h - 1)
                {
                    p.Put(x0 + i, y0 + j, recipe.Roof.Edge);
                    continue;
                }
                // "along" follows the eave, "across" counts up the slope from it, as on a main roof face.
                var (along, across) = front switch
                {
                    DoorSide.North => (i - 1, j - 1),
                    DoorSide.East => (j - 1, w - 2 - i),
                    DoorSide.West => (j - 1, i - 1),
                    _ => (i - 1, h - 2 - j),
                };
                p.Put(x0 + i, y0 + j, Surface(recipe, face, along, across, p.Small, salt));
            }
    }

    /// <summary>
    /// The Market's ridge lantern: a raised, louvred vent along the middle of
    /// the ridge that lets the air out of the hall. From above it is a small
    /// hipped roof of grey slate shingles sitting astride the main ridge, its
    /// shadow falling south-east across the planks, with the approved
    /// Market's gold pennant flying from its peak.
    /// </summary>
    private static void Lantern(Plate p, Rect2I hall)
    {
        int iw = hall.Size.X - 2, ih = hall.Size.Y - 2;
        var horizontal = iw >= ih;
        var length = horizontal ? iw : ih;
        var breadth = horizontal ? ih : iw;
        var ridge = (breadth + 1) / 2 - 1;
        var along = Math.Max(p.P(12), length * 2 / 5);
        var across = p.Small ? 5 : 9;
        int centreAlong = length / 2, top = ridge - across / 2;
        var roof = horizontal
            ? new Rect2I(hall.Position.X + 1 + centreAlong - along / 2, hall.Position.Y + 1 + top, along, across)
            : new Rect2I(hall.Position.X + 1 + top, hall.Position.Y + 1 + centreAlong - along / 2, across, along);
        PaintRoof(p, roof, new Recipe(Slate, Material.Shingle, RoofShape.Hip), (int)Design.Market * 31 + 9);
        var peak = RidgeEnds(roof.Position.X + 1, roof.Position.Y + 1, roof.Size.X - 2, roof.Size.Y - 2);
        var (px, py) = peak[0];
        Pennant(p, px + (peak[1].X - px) / 2 + (p.Small ? 0 : 1), py + (peak[1].Y - py) / 2);
    }

    /// <summary>
    /// The arcade's posts along the lean-to's eave, two flanking the way in
    /// and the rest spaced evenly to the corners: each a timber post head lit
    /// on its north-west pixel, with a small shadow.
    /// </summary>
    private static void ArcadePosts(Plate p, Rect2I arcade, DoorSide side, int middle)
    {
        // At 16 px a post would be a single edge-coloured pixel on the eave, so the arcade shows by its roof alone (B7).
        if (p.Small) return;
        const int size = 3;
        var horizontal = side is DoorSide.South or DoorSide.North;
        var start = (horizontal ? arcade.Position.X : arcade.Position.Y) + 1;
        var end = (horizontal ? arcade.End.X : arcade.End.Y) - 1 - size;
        var gap = p.P(6);
        var spots = new List<int>();
        // Posts from a door-side post out to a corner; a short run keeps only its corner post.
        void Run(int corner, int flank)
        {
            if (Math.Abs(flank - corner) < p.P(8))
            {
                spots.Add(corner);
                return;
            }
            var bays = Math.Max(1, (int)MathF.Round(Math.Abs(flank - corner) / (float)p.P(11)));
            for (var k = 0; k <= bays; k++) spots.Add(corner + (flank - corner) * k / bays);
        }
        Run(start, middle - gap - size);
        Run(end, middle + gap);
        foreach (var t in spots.Distinct())
        {
            var (x, y) = side switch
            {
                DoorSide.North => (t, arcade.Position.Y - size / 2),
                DoorSide.East => (arcade.End.X - 1 - size / 2, t),
                DoorSide.West => (arcade.Position.X - size / 2, t),
                _ => (t, arcade.End.Y - 1 - size / 2),
            };
            p.Fill(x + 1, y + 1, size, size, SmallShadow);
            p.Fill(x, y, size, size, Timber.Edge);
            p.Put(x + 1, y + 1, Timber.Light);
        }
    }

    /// <summary>B6 Market: a cream sign board with gold trading scales: a beam on a post, a pan hanging from each end.</summary>
    private static void ScalesSign(Plate p, int cx, int cy)
    {
        if (p.Small)
        {
            p.Fill(cx - 1, cy - 1, 4, 4, SmallShadow);
            p.Fill(cx - 2, cy - 2, 4, 4, Timber.Edge);
            p.Fill(cx - 1, cy - 1, 2, 2, Cloth.Light);
            p.Fill(cx - 1, cy - 1, 2, 1, Gold.Base);
            p.Put(cx, cy, Gold.Shade);
            return;
        }
        p.Fill(cx - 3, cy - 3, 9, 8, SmallShadow);
        p.Fill(cx - 4, cy - 4, 9, 8, Timber.Edge);
        p.Fill(cx - 3, cy - 3, 7, 6, Cloth.Light);
        p.Fill(cx - 3, cy - 3, 7, 1, Cloth.Highlight);
        string[] scales =
        [
            "...h...",
            "lllllll",
            "s..b..s",
            "bb.b.bb",
            "...b...",
            "..sbs..",
        ];
        for (var j = 0; j < scales.Length; j++)
            for (var i = 0; i < scales[j].Length; i++)
            {
                Color? c = scales[j][i] switch
                {
                    'h' => Gold.Highlight,
                    'l' => Gold.Light,
                    'b' => Gold.Base,
                    's' => Gold.Shade,
                    _ => null,
                };
                if (c is { } colour) p.Put(cx - 3 + i, cy - 3 + j, colour);
            }
    }

    /// <summary>A market stall on its own tile, facing the door side, with a gap for the path.</summary>
    private static void PaintStallLot(Plate p, int w, int h, BuildingDoor door)
    {
        var area = new Rect2(4, 4, w - 8, h - 9);
        Stall(p, area, door.Side, Berry, Fruit, 5);
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
    /// counter in front of it, in two pairs with the middle clear for the path.
    /// </summary>
    private static void Stall(Plate p, Rect2 area, DoorSide front, Ramp stripe, Ramp produce, int salt)
    {
        var across = front is DoorSide.South or DoorSide.North;
        var alongSize = across ? area.Size.X : area.Size.Y;
        var depthSize = across ? area.Size.Y : area.Size.X;
        var awningDepth = MathF.Round(depthSize * 0.6f);
        Awning(p, p.Px(Orient(area, front, 0, 0, alongSize, awningDepth)), front, stripe);
        var crateDepth = Math.Min(5f, depthSize - awningDepth);
        var crates = new[] { 1f, 6f, alongSize - 11, alongSize - 6 };
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

    /// <summary>The Market's finial, on the hall's lantern: a pole top with a little gold pennant flying east.</summary>
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

    // ----------------------------------------------------------------------
    // Port.
    // ----------------------------------------------------------------------

    /// <summary>
    /// The side of a Port that stands on land, where its shed and door face
    /// the Road: the door side when it is one of the short ends, otherwise
    /// north for a tall footprint and east for a wide one.
    /// </summary>
    private static DoorSide PortLandSide(int tilesWide, int tilesHigh, DoorSide door) => tilesHigh >= tilesWide
        ? door == DoorSide.South ? DoorSide.South : DoorSide.North
        : door == DoorSide.West ? DoorSide.West : DoorSide.East;

    /// <summary>
    /// The Port's layout frame. The Port is laid out once with the land at
    /// the top ("across" runs along the shore, "out" from the land over the
    /// water) and mapped into the footprint for each of its four rotations.
    /// Only positions are mapped; every piece is drawn in place with the
    /// north-west light, so a rotated Port is lit like any other building.
    /// </summary>
    private readonly record struct PortFrame(DoorSide Land, float Width, float Height)
    {
        /// <summary>The length of the Port from the land end to the sea end, in units.</summary>
        public float Length => Land is DoorSide.North or DoorSide.South ? Height : Width;

        /// <summary>The width of the Port along the shore, in units.</summary>
        public float Breadth => Land is DoorSide.North or DoorSide.South ? Width : Height;

        /// <summary>A rectangle in the frame (across, out, size across, size out) to footprint units.</summary>
        public Rect2 Map(float across, float outward, float sizeAcross, float sizeOut) => Land switch
        {
            DoorSide.North => new Rect2(across, outward, sizeAcross, sizeOut),
            DoorSide.South => new Rect2(across, Height - outward - sizeOut, sizeAcross, sizeOut),
            DoorSide.West => new Rect2(outward, across, sizeOut, sizeAcross),
            _ => new Rect2(Width - outward - sizeOut, across, sizeOut, sizeAcross),
        };

        public Rect2 Map(Rect2 r) => Map(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y);

        /// <summary>A point in the frame to footprint units.</summary>
        public Vector2 Point(float across, float outward) => Land switch
        {
            DoorSide.North => new Vector2(across, outward),
            DoorSide.South => new Vector2(across, Height - outward),
            DoorSide.West => new Vector2(outward, across),
            _ => new Vector2(Width - outward, across),
        };

        /// <summary>The top-left corner of a small square of <paramref name="size"/> units placed at (across, out).</summary>
        public Vector2 Corner(float across, float outward, float size) => Map(across, outward, size, size).Position;
    }

    /// <summary>
    /// B3/B6 Port: one row on land, a plank shed whose door faces the Road;
    /// the other three rows are a plank pier on piles over the water,
    /// widening to a T-head with iron bollards and goods waiting. The pier
    /// keeps open water on both long sides, where boats tie up. Drawn in any
    /// of the four rotations (<see cref="PortLandSide"/>).
    /// <paramref name="paintedBoat"/> adds the moored boat of the approved
    /// round-1 drawing; round 2 leaves the water free for the real boat.
    /// </summary>
    private static void PaintPort(Plate p, int w, int h, BuildingDoor door, bool paintedBoat)
    {
        var frame = new PortFrame(PortLandSide(w / 32, h / 32, door.Side), w, h);
        float breadth = frame.Breadth, length = frame.Length;
        const float shedBottom = 27;
        var pier = new Rect2(breadth / 2f - 12, shedBottom - 3, 24, length - 30 - (shedBottom - 3));
        var head = new Rect2(6, length - 30, breadth - 12, 24);
        foreach (var deck in new[] { pier, head })
        {
            var px = p.Px(frame.Map(deck));
            p.Fill(new Rect2I(px.Position.X + p.P(2), px.Position.Y + p.P(3), px.Size.X, px.Size.Y), Shadow);
        }
        if (paintedBoat) Boat(p, frame.Map(new Rect2(pier.End.X + 4, 46, 9, 28)));
        Deck(p, p.Px(frame.Map(pier)), 201);
        Deck(p, p.Px(frame.Map(head)), 202);
        // Piles standing proud of the deck edges.
        void PostAt(float across, float outward)
        {
            var corner = frame.Corner(across, outward, 3);
            Post(p, corner.X, corner.Y);
        }
        for (var y = 38f; y < head.Position.Y - 4; y += 16)
        {
            PostAt(pier.Position.X - 1, y);
            PostAt(pier.End.X - 2, y);
        }
        foreach (var (x, y) in new[] { (head.Position.X - 1, head.Position.Y - 1), (head.End.X - 2, head.Position.Y - 1), (head.Position.X - 1, head.End.Y - 2), (head.End.X - 2, head.End.Y - 2) })
            PostAt(x, y);
        // Bollards along the outer edge of the T-head, and one on the pier where a boat ties up.
        foreach (var (x, y) in new[] { (head.Position.X + 6, head.End.Y - 4), (head.End.X - 6, head.End.Y - 4), (breadth / 2f, head.End.Y - 4), (pier.End.X - 3, 48f) })
        {
            var at = frame.Point(x, y);
            Bollard(p, at.X, at.Y);
        }
        // Mooring line from the pier bollard to the painted boat's bow.
        if (paintedBoat)
        {
            var (from, to) = (frame.Point(pier.End.X - 2, 48), frame.Point(pier.End.X + 7.5f, 47.5f));
            p.Canvas.Line(from.X, from.Y, to.X, to.Y, Cloth.Shade);
        }
        // Goods waiting on the T-head.
        foreach (var (x, y) in new[] { (head.Position.X + 9, head.Position.Y + 6), (head.Position.X + 16, head.Position.Y + 8) })
        {
            var corner = frame.Corner(x, y, 6);
            DeckCrate(p, corner.X, corner.Y);
        }
        var coil = frame.Point(head.End.X - 12, head.Position.Y + 9);
        p.Canvas.Ring(coil.X, coil.Y, 2.5f, Cloth.Shade);
        p.Canvas.Disc(coil.X, coil.Y, 1.2f, Cloth.Base);
        // The shed on the shore.
        var recipe = new Recipe(Timber, Material.Shingle, RoofShape.Hip);
        var shed = new Rect2(9, 5, breadth - 18, shedBottom - 5);
        var roof = p.Px(frame.Map(shed));
        PaintRoof(p, roof, recipe, 203);
        var middle = p.P(Fit(door.Tile is { } t ? t * 32 + 16 : breadth / 2f, shed.Position.X + 6, shed.End.X - 6));
        var next = Door(p, roof, frame.Land, middle, 3, 2);
        if (door.Tile is not null) Path(p, roof, frame.Land, middle, next);
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
    // Vehicles: the handcart (content list 4.8) and the rowing boat (7.3).
    // They are their own sprites, one tile each, so the game can draw them
    // where they really are: parked by a Farmhouse, pulled along a Road,
    // tied up at a Port. Nothing in a building sprite pretends to be one.
    // ----------------------------------------------------------------------

    /// <summary>
    /// The way a vehicle points: the handcart's shafts or the boat's bow. The
    /// diagonals were added in round 3 (appended, so the cardinal values stay)
    /// because agents walk diagonally, so a cart they pull and a boat they
    /// row travel diagonally too.
    /// </summary>
    private enum Facing { South, East, North, West, SouthEast, NorthEast, NorthWest, SouthWest }

    /// <summary>Whether a facing is one of the four diagonals.</summary>
    private static bool IsDiagonal(Facing facing) => facing >= Facing.SouthEast;

    /// <summary>All eight facings clockwise from north, the order of the turnaround strips.</summary>
    private static readonly Facing[] Compass =
        [Facing.North, Facing.NorthEast, Facing.East, Facing.SouthEast, Facing.South, Facing.SouthWest, Facing.West, Facing.NorthWest];

    /// <summary>The letter used in asset Ids for a facing.</summary>
    private static string Letter(Facing facing) => facing switch
    {
        Facing.South => "S",
        Facing.East => "E",
        Facing.North => "N",
        Facing.West => "W",
        Facing.SouthEast => "SE",
        Facing.NorthEast => "NE",
        Facing.NorthWest => "NW",
        _ => "SW",
    };

    /// <summary>
    /// A vehicle's own frame on its 32 px tile: u runs forward, v to its
    /// right, in pixels from the tile centre. Vehicles are laid out once in
    /// this frame and turned by quarter turns; colours are chosen afterwards
    /// from the tile's north-west light, so every facing is lit the same way.
    /// </summary>
    private readonly record struct Heading(Facing Facing)
    {
        private const float Centre = 16;
        private const float Half = 0.70710678f;

        /// <summary>The unit vector the vehicle points along, in tile pixels (y down).</summary>
        public Vector2 Forward => Facing switch
        {
            Facing.East => new Vector2(1, 0),
            Facing.West => new Vector2(-1, 0),
            Facing.South => new Vector2(0, 1),
            Facing.North => new Vector2(0, -1),
            Facing.SouthEast => new Vector2(Half, Half),
            Facing.NorthEast => new Vector2(Half, -Half),
            Facing.NorthWest => new Vector2(-Half, -Half),
            _ => new Vector2(-Half, Half),
        };

        /// <summary>The unit vector to the vehicle's right: a quarter turn clockwise from <see cref="Forward"/>.</summary>
        public Vector2 Right => new(-Forward.Y, Forward.X);

        /// <summary>A point of the frame in tile pixels.</summary>
        public Vector2 Map(float u, float v) => Facing switch
        {
            Facing.East => new Vector2(Centre + u, Centre + v),
            Facing.West => new Vector2(Centre - u, Centre - v),
            Facing.South => new Vector2(Centre - v, Centre + u),
            Facing.North => new Vector2(Centre + v, Centre - u),
            _ => new Vector2(Centre, Centre) + Forward * u + Right * v,
        };

        /// <summary>A tile pixel position in the frame (the inverse of <see cref="Map"/>).</summary>
        public Vector2 Local(float x, float y) => Facing switch
        {
            Facing.East => new Vector2(x - Centre, y - Centre),
            Facing.West => new Vector2(Centre - x, Centre - y),
            Facing.South => new Vector2(y - Centre, Centre - x),
            Facing.North => new Vector2(Centre - y, x - Centre),
            _ => new Vector2(new Vector2(x - Centre, y - Centre).Dot(Forward), new Vector2(x - Centre, y - Centre).Dot(Right)),
        };

        /// <summary>
        /// The diagonal lattice of a 45° facing: steps along and across the
        /// vehicle in units of half a pixel's diagonal, so a pixel centre always
        /// lands on whole numbers and seams and bands fall on clean 45° stairs.
        /// </summary>
        public (int Along, int Across) Lattice(int x, int y)
        {
            var local = Local(x + 0.5f, y + 0.5f);
            return ((int)MathF.Round(local.X * 1.41421356f), (int)MathF.Round(local.Y * 1.41421356f));
        }

        /// <summary>A direction of the frame in tile pixels.</summary>
        public Vector2 Direction(float u, float v) => Map(u, v) - Map(0, 0);

        /// <summary>The pixel rectangle covering [u, u + du) × [v, v + dv) of the frame.</summary>
        public Rect2I Box(float u, float v, float du, float dv)
        {
            var a = Map(u, v);
            var b = Map(u + du, v + dv);
            var left = (int)MathF.Round(MathF.Min(a.X, b.X));
            var top = (int)MathF.Round(MathF.Min(a.Y, b.Y));
            return new Rect2I(left, top, (int)MathF.Round(MathF.Max(a.X, b.X)) - left, (int)MathF.Round(MathF.Max(a.Y, b.Y)) - top);
        }
    }

    /// <summary>
    /// Collects the pixels a sprite's shadow covers and paints each once, so
    /// overlapping parts (bed, wheels, shafts) do not darken the ground twice.
    /// </summary>
    private sealed class ShadowMask(int width, int height)
    {
        private readonly bool[] covered = new bool[width * height];

        public void Add(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < width && y < height) covered[y * width + x] = true;
        }

        public void Add(Rect2I area, int dx, int dy)
        {
            for (var y = area.Position.Y; y < area.End.Y; y++)
                for (var x = area.Position.X; x < area.End.X; x++)
                    Add(x + dx, y + dy);
        }

        public void Paint(Plate p, Color color)
        {
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (covered[y * width + x]) p.Put(x, y, color);
        }
    }

    /// <summary>
    /// One part of a vehicle on its 32 px tile: the pixels whose centres fall
    /// inside it. The diagonal facings are drawn from these masks rather than
    /// by turning a finished picture: a part's outline is its outer ring of
    /// pixels and its light comes from which open sides each ring pixel has on
    /// screen, so a 45° part keeps crisp one-pixel stairs and the north-west
    /// light, with nothing blurred or resampled.
    /// </summary>
    private sealed class Mask
    {
        private const int Size = 32;
        private readonly bool[] on = new bool[Size * Size];

        public Mask(Func<int, int, bool> covers)
        {
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    on[y * Size + x] = covers(x, y);
        }

        /// <summary>The pixels of a part written in the vehicle's frame (u forward, v to its right).</summary>
        public static Mask Of(Heading f, Func<float, float, bool> inside) => new((x, y) =>
        {
            var local = f.Local(x + 0.5f, y + 0.5f);
            return inside(local.X, local.Y);
        });

        public bool this[int x, int y] => x >= 0 && y >= 0 && x < Size && y < Size && on[y * Size + x];

        /// <summary>Every covered pixel, row by row.</summary>
        public IEnumerable<(int X, int Y)> Pixels()
        {
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (on[y * Size + x]) yield return (x, y);
        }

        /// <summary>Whether a covered pixel has an open pixel above, below or beside it.</summary>
        public bool Rim(int x, int y) => this[x, y] && (!this[x - 1, y] || !this[x + 1, y] || !this[x, y - 1] || !this[x, y + 1]);

        /// <summary>The part less its outer ring.</summary>
        public Mask Inner() => new((x, y) => this[x, y] && !Rim(x, y));

        /// <summary>The part and another together.</summary>
        public Mask Or(Mask other) => new((x, y) => this[x, y] || other[x, y]);

        /// <summary>
        /// How squarely a ring pixel faces the north-west light, from its open
        /// sides: 1 facing it, −1 facing away, 0 side-on (or not on the ring).
        /// </summary>
        public float Light(int x, int y)
        {
            float nx = 0, ny = 0;
            if (!this[x - 1, y]) nx -= 1;
            if (!this[x + 1, y]) nx += 1;
            if (!this[x, y - 1]) ny -= 1;
            if (!this[x, y + 1]) ny += 1;
            var length = MathF.Sqrt(nx * nx + ny * ny);
            return length == 0 ? 0 : -(nx + ny) / (length * 1.41421356f);
        }

        /// <summary>
        /// Whether a ring pixel takes the lit tone of a two-tone part: it faces
        /// the light, or it is side-on and open to the north (the way the
        /// cardinal sprites light the north row of an east-west pole).
        /// </summary>
        public bool Lit(int x, int y)
        {
            var light = Light(x, y);
            return light > 0.3f || (light > -0.3f && !this[x, y - 1]);
        }
    }

    /// <summary>The review pictures of both vehicles: every facing on its ground, and each bare sprite.</summary>
    private IEnumerable<Entry> VehicleEntries()
    {
        var facings = new[] { Facing.South, Facing.East, Facing.North, Facing.West };
        foreach (var loaded in new[] { false, true })
            foreach (var facing in facings)
            {
                var id = $"handcart.{(loaded ? "loaded" : "empty")}.{Letter(facing)}";
                var note = facing != Facing.South ? null : loaded
                    ? "Handcart parked with a load: three logs lashed with rope and a grain sack. Shafts point the way it faces."
                    : "Handcart parked: plank bed with iron corner fittings, iron-rimmed wheels, shafts resting on the ground, a coil of rope.";
                var sprite = Handcart(facing, loaded, pulled: false);
                yield return new(Family, id, Bitmap.Over(Grass(1, 1, 32), sprite, 0, 0), note);
                yield return new(Family, id + ".sprite", sprite, note);
            }
        // Round 3: the four diagonals, because agents walk diagonally and pull the cart with them.
        var diagonals = new[] { Facing.NorthEast, Facing.NorthWest, Facing.SouthEast, Facing.SouthWest };
        foreach (var loaded in new[] { false, true })
            foreach (var facing in diagonals)
            {
                var id = $"handcart.{(loaded ? "loaded" : "empty")}.{Letter(facing)}";
                var note = facing != Facing.NorthEast ? null : loaded
                    ? "Round 3: the loaded handcart on a diagonal, the same cart turned 45° and redrawn in clean pixel stairs, still lit from the north-west."
                    : "Round 3: agents walk diagonally, so a cart they pull goes diagonally too. The same cart, part for part, turned 45° and redrawn in clean pixel stairs.";
                var sprite = Handcart(facing, loaded, pulled: false);
                yield return new(Family, id, Bitmap.Over(Grass(1, 1, 32), sprite, 0, 0), note);
                yield return new(Family, id + ".sprite", sprite, note);
            }
        const string pulledNote = "Handcart being pulled east: shafts lifted to hand height, so they look shorter and their shadow falls away from them.";
        var pulled = Handcart(Facing.East, loaded: true, pulled: true);
        yield return new(Family, "handcart.pulled.E", Bitmap.Over(Grass(1, 1, 32), pulled, 0, 0), pulledNote);
        yield return new(Family, "handcart.pulled.E.sprite", pulled, pulledNote);
        const string pulledDiagonalNote = "Round 3: being pulled south-east, shafts lifted, their shadow falling away from them.";
        var pulledDiagonal = Handcart(Facing.SouthEast, loaded: true, pulled: true);
        yield return new(Family, "handcart.pulled.SE", Bitmap.Over(Grass(1, 1, 32), pulledDiagonal, 0, 0), pulledDiagonalNote);
        yield return new(Family, "handcart.pulled.SE.sprite", pulledDiagonal, pulledDiagonalNote);
        yield return new(Family, "handcart.turnaround", Turnaround(facing => Bitmap.Over(Grass(1, 1, 32), Handcart(facing, loaded: false, pulled: false), 0, 0)),
            "Round 3: the empty handcart in all eight facings, clockwise from north: N, NE, E, SE, S, SW, W, NW.");
        foreach (var facing in facings)
        {
            var note = facing == Facing.South
                ? "Rowing boat for one agent and their cargo: planked hull, two thwarts, oars resting in iron oarlocks, a ripple at the waterline."
                : null;
            var sprite = Boat(facing, oarsOut: true);
            yield return new(Family, $"boat.{Letter(facing)}", Bitmap.Over(RiverTile(0, 0), sprite, 0, 0), note);
            yield return new(Family, $"boat.{Letter(facing)}.sprite", sprite, note);
        }
        // Round 3: boats get the diagonals too, because they will travel diagonally across the water.
        foreach (var facing in diagonals)
        {
            var note = facing == Facing.NorthEast
                ? "Round 3: boats will travel diagonally, so they get the four diagonals too: the same hull and oars turned 45° in clean pixel stairs, lit from the north-west."
                : null;
            var sprite = Boat(facing, oarsOut: true);
            yield return new(Family, $"boat.{Letter(facing)}", Bitmap.Over(RiverTile(0, 0), sprite, 0, 0), note);
            yield return new(Family, $"boat.{Letter(facing)}.sprite", sprite, note);
        }
        yield return new(Family, "boat.turnaround", Turnaround(facing => Bitmap.Over(RiverTile(0, 0), Boat(facing, oarsOut: true), 0, 0)),
            "Round 3: the rowing boat in all eight facings, clockwise from north: N, NE, E, SE, S, SW, W, NW.");
        yield return new(Family, "boat.moored.E", MooredBoat(),
            "The boat tied up on the east side of the Port pier, oars shipped, bow line to the pier bollard. The clear water beside the pier is the docking space.");
    }

    /// <summary>A strip of eight tiles, one per facing clockwise from north.</summary>
    private static Image Turnaround(Func<Facing, Image> tile)
    {
        var strip = Image.CreateEmpty(32 * Compass.Length, 32, false, Image.Format.Rgba8);
        for (var i = 0; i < Compass.Length; i++)
            strip.BlitRect(tile(Compass[i]), new Rect2I(0, 0, 32, 32), new Vector2I(i * 32, 0));
        return strip;
    }

    /// <summary>
    /// A handcart (content list 4.8), Blacksmith-made from wood, iron fittings
    /// and rope: a plank bed with side boards, two iron-rimmed wheels on an
    /// axle under its middle, and two shafts with rope-wrapped grips pointing
    /// the way it faces. Parked, the shafts rest on the ground; pulled, they
    /// are lifted to hand height, so they look shorter and their shadow falls
    /// clear of them. Loaded, it carries logs lashed with rope and a sack.
    /// </summary>
    private static Image Handcart(Facing facing, bool loaded, bool pulled)
    {
        if (IsDiagonal(facing)) return HandcartDiagonal(facing, loaded, pulled);
        var p = new Plate(32, 32, 1);
        var f = new Heading(facing);
        var bed = f.Box(-13, -6, 16, 12);
        Rect2I[] wheels = [f.Box(-10, -10, 10, 4), f.Box(-10, 6, 10, 4)];
        Rect2I[] hubs = [f.Box(-6, -11, 2, 1), f.Box(-6, 10, 2, 1)];
        var tip = pulled ? 10 : 12;
        Rect2I[] shafts = [f.Box(-1, -5, tip + 1, 2), f.Box(-1, 3, tip + 1, 2)];

        // L2 shadow in one pass. Lifted shafts throw theirs further from them.
        var shadow = new ShadowMask(32, 32);
        shadow.Add(bed, 1, 2);
        foreach (var wheel in wheels) shadow.Add(wheel, 1, 2);
        var (liftX, liftY) = pulled ? (3, 5) : (1, 1);
        foreach (var shaft in shafts) shadow.Add(shaft, liftX, liftY);
        shadow.Paint(p, SmallShadow);

        foreach (var shaft in shafts) Pole(p, shaft, Timber);
        // Rope wrapped round the last three pixels of each shaft, for the puller's hands.
        foreach (var v in new[] { -5, 3 })
            for (var u = tip - 3; u < tip; u++)
                p.Fill(f.Box(u, v, 1, 2), (u - tip) % 2 == 0 ? Timber.Shade : Timber.Highlight);
        foreach (var wheel in wheels) Tyre(p, wheel);
        foreach (var hub in hubs) p.Fill(hub, Iron.Shade);
        CartBed(p, bed, facing is Facing.East or Facing.West);
        if (loaded) CartLoad(p, f, facing);
        else
        {
            // A coil of rope in the back corner, ready to lash a load.
            var coil = f.Map(-6.5f, 0.5f);
            RopeCoil(p, coil.X, coil.Y);
        }
        return p.Image;
    }

    /// <summary>
    /// A two-pixel timber pole (a handcart shaft): its north or west row
    /// catches the light, the other row is the edge step.
    /// </summary>
    private static void Pole(Plate p, Rect2I pole, Ramp ramp)
    {
        p.Fill(pole, ramp.Edge);
        if (pole.Size.X >= pole.Size.Y) p.Fill(pole.Position.X, pole.Position.Y, pole.Size.X, 1, ramp.Light);
        else p.Fill(pole.Position.X, pole.Position.Y, 1, pole.Size.Y, ramp.Light);
    }

    /// <summary>
    /// A wheel seen from straight above is its iron tyre: a strip with
    /// rounded ends, a lit tread on the north or west half, and darker ends
    /// where the rim curves away down to the ground.
    /// </summary>
    private static void Tyre(Plate p, Rect2I tyre)
    {
        int left = tyre.Position.X, top = tyre.Position.Y, right = tyre.End.X - 1, bottom = tyre.End.Y - 1;
        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                if ((x == left || x == right) == false || (y != top && y != bottom))
                    p.Put(x, y, Iron.Edge);
        int ix = left + 1, iy = top + 1, w = tyre.Size.X - 2, h = tyre.Size.Y - 2;
        if (w <= 0 || h <= 0) return;
        var flat = w >= h;
        p.Fill(ix, iy, w, h, Iron.Base);
        if (flat) p.Fill(ix, iy, w, 1, Iron.Light);
        else p.Fill(ix, iy, 1, h, Iron.Light);
        p.Put(ix + (flat ? 2 : 0), iy + (flat ? 0 : 2), Iron.Highlight);
        if (flat)
        {
            p.Fill(ix, iy, 1, h, Iron.Shade);
            p.Fill(ix + w - 1, iy, 1, h, Iron.Shade);
        }
        else
        {
            p.Fill(ix, iy, w, 1, Iron.Shade);
            p.Fill(ix, iy + h - 1, w, 1, Iron.Shade);
        }
    }

    /// <summary>
    /// The cart bed: an edge outline, side boards lit on the north and west
    /// and shaded on the south and east, a floor of planks laid across the
    /// cart with the boards' shadow on it, and an iron fitting at each corner.
    /// </summary>
    private static void CartBed(Plate p, Rect2I bed, bool eastWest)
    {
        p.Fill(bed, Timber.Edge);
        int x = bed.Position.X + 1, y = bed.Position.Y + 1, w = bed.Size.X - 2, h = bed.Size.Y - 2;
        p.Fill(x, y, w, h, Timber.Base);
        p.Fill(x, y + h - 1, w, 1, Timber.Shade);
        p.Fill(x + w - 1, y, 1, h, Timber.Shade);
        p.Fill(x, y, w, 1, Timber.Light);
        p.Fill(x, y, 1, h, Timber.Light);
        // Floor: planks laid across the cart inside the boards; the north and west boards shade the planks beside them.
        int fx = x + 1, fy = y + 1, fw = w - 2, fh = h - 2;
        var seam = Toward(Timber, 2, 1, 0.65f);
        if (eastWest)
            for (var column = 3; column < fw - 1; column += 4) p.Fill(fx + column, fy, 1, fh, seam);
        else
            for (var row = 3; row < fh - 1; row += 4) p.Fill(fx, fy + row, fw, 1, seam);
        p.Fill(fx, fy, fw, 1, Toward(Timber, 2, 1, 0.5f));
        p.Fill(fx, fy, 1, fh, Toward(Timber, 2, 1, 0.5f));
        // Iron corner fittings, lit at the north-west corner.
        p.Put(x, y, Iron.Highlight);
        p.Put(x + w - 1, y, Iron.Light);
        p.Put(x, y + h - 1, Iron.Light);
        p.Put(x + w - 1, y + h - 1, Iron.Base);
    }

    /// <summary>
    /// A load: two logs side by side with a third resting on them, sticking
    /// out past the back of the bed so their end grain shows, lashed with a
    /// rope, and a tied grain sack lying across the front of the bed.
    /// </summary>
    private static void CartLoad(Plate p, Heading f, Facing facing)
    {
        Log(p, f, -15, -6, 12, 5);
        Log(p, f, -15, 1, 12, 5);
        Log(p, f, -14, -3, 10, 6);
        p.Fill(f.Box(-8, -6, 1, 12), Timber.Light);
        foreach (var v in new[] { -6, 5 }) p.Fill(f.Box(-8, v, 1, 1), Timber.Edge);
        var sack = f.Map(-0.5f, 0.5f);
        LyingSack(p, sack.X, sack.Y, alongX: facing is Facing.North or Facing.South);
    }

    /// <summary>
    /// One log lying along the cart, [u, u + length) × [v, v + width) in the
    /// cart's frame: bark lit on its north or west side, and pale end grain
    /// with a darker heart at the back end.
    /// </summary>
    private static void Log(Plate p, Heading f, int u, int v, int length, int width)
    {
        var box = f.Box(u, v, length, width);
        p.Fill(box, Timber.Edge);
        int x = box.Position.X + 1, y = box.Position.Y + 1, w = box.Size.X - 2, h = box.Size.Y - 2;
        var flat = w >= h;
        p.Fill(x, y, w, h, Timber.Shade);
        if (flat) p.Fill(x, y, w, 1, Timber.Base);
        else p.Fill(x, y, 1, h, Timber.Base);
        if (flat && h > 2) p.Fill(x, y + h - 1, w, 1, Toward(Timber, 1, 0, 0.45f));
        if (!flat && w > 2) p.Fill(x + w - 1, y, 1, h, Toward(Timber, 1, 0, 0.45f));
        // End grain: the back pixel row of the interior, with the heart in the middle.
        p.Fill(f.Box(u + 1, v + 1, 1, width - 2), Timber.Highlight);
        p.Fill(f.Box(u + 1, v + width / 2, 1, 1), Timber.Light);
    }

    /// <summary>A coil of rope seen from above: two turns round a dark middle, lit north-west.</summary>
    private static void RopeCoil(Plate p, float cx, float cy)
    {
        p.Disc(cx + 0.6f, cy + 0.8f, 3.2f, SmallShadow);
        p.Disc(cx, cy, 3.2f, Timber.Shade);
        p.Disc(cx, cy, 2.5f, Timber.Highlight);
        p.Disc(cx + 0.3f, cy + 0.3f, 1.6f, Timber.Shade);
        p.Disc(cx, cy, 1.0f, Timber.Light);
        var x = (int)cx;
        var y = (int)cy;
        p.Put(x, y, Timber.Edge);
        p.Put(x - 2, y - 1, Cloth.Light);
        p.Put(x + 2, y + 2, Timber.Light);
    }

    /// <summary>
    /// The rowing boat's hull: half its beam at <paramref name="u"/> pixels
    /// from the middle (negative toward the stern), or a negative number
    /// outside the hull. A narrow transom at the stern, widest a third of the
    /// way along, then a full curve to a pointed bow.
    /// </summary>
    private static float HalfBeam(float u)
    {
        const float half = 14;
        var t = (u + half) / (half * 2);
        if (t < 0 || t > 1) return -1;
        if (t < 0.36f)
        {
            var s = (0.36f - t) / 0.36f;
            return 4.4f + 1.8f * MathF.Sqrt(1 - s * s);
        }
        var bow = (t - 0.36f) / 0.64f;
        return 6.2f * (1 - MathF.Pow(bow, 2.3f));
    }

    /// <summary>
    /// A rowing boat (content list 7.3), made of wood, rope and iron
    /// fittings, for one agent and their cargo, drawn afloat. The hull is a
    /// planked shell: an edge outline, a gunwale lit on the north-west side,
    /// the inner wall shaded under it, floorboards, two thwarts, a short
    /// foredeck and a stern seat. With <paramref name="oarsOut"/> the oars
    /// rest in iron oarlocks with their blades trailing on the water;
    /// otherwise they are shipped inside, as when tied up.
    /// </summary>
    private static Image Boat(Facing facing, bool oarsOut)
    {
        if (IsDiagonal(facing)) return BoatDiagonal(facing, oarsOut);
        var p = new Plate(32, 32, 1);
        var f = new Heading(facing);
        // How far a pixel lies inside the hull's edge (negative outside), and its place in the frame.
        (float Depth, Vector2 Local) Inside(int x, int y)
        {
            var local = f.Local(x + 0.5f, y + 0.5f);
            var beam = HalfBeam(local.X);
            if (beam < 0) return (-9, local);
            return (MathF.Min(beam - MathF.Abs(local.Y), local.X + 14), local);
        }
        bool Thwart(float u) => u is >= -6 and < -4 or >= 3 and < 5;
        var light = new Vector2(-0.7071f, -0.7071f);

        // L2 shadow on the water, and a broken ripple of light where the hull meets it.
        var shadow = new ShadowMask(32, 32);
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
                if (Inside(x, y).Depth > 0) shadow.Add(x + 2, y + 3);
        shadow.Paint(p, SmallShadow);
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var (depth, _) = Inside(x, y);
                if (depth is <= -1.3f or > 0) continue;
                if (PixelArt.Hash(x, y, 233) % 2 == 0) continue;
                p.Put(x, y, River.Highlight with { A = 0.45f });
            }

        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var (depth, local) = Inside(x, y);
                if (depth <= 0) continue;
                var (u, v) = (local.X, local.Y);
                // Which way this part of the hull faces, for the north-west light.
                var sternward = local.X + 14 < HalfBeam(u) - MathF.Abs(v);
                var outward = sternward ? f.Direction(-1, 0) : f.Direction(u > 4 ? 0.8f : 0, MathF.Sign(v));
                var facingLight = outward.Normalized().Dot(light);
                Color c;
                if (depth < 1) c = Timber.Edge;
                else if (depth < 2) c = facingLight > 0.15f ? Timber.Light : facingLight < -0.15f ? Timber.Base : Toward(Timber, 2, 3, 0.5f);
                else if (depth < 2.9f) c = facingLight > 0.15f ? Toward(Timber, 1, 0, 0.55f) : Timber.Base;
                else if (u > 6 || u < -10) c = Timber.Base;
                else if (Thwart(u)) c = Timber.Base;
                else c = MathF.Abs(MathF.Abs(v) - 2) < 0.5f ? Toward(Timber, 1, 0, 0.45f) : Timber.Shade;
                p.Put(x, y, c);
            }
        // Thwarts and the decks: their north and west edges catch the light.
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var (depth, local) = Inside(x, y);
                if (depth < 2.9f) continue;
                var plank = Thwart(local.X) || local.X > 6 || local.X < -10;
                if (!plank) continue;
                bool PlankAt(int px, int py)
                {
                    var (d, l) = Inside(px, py);
                    return d >= 2.9f && (Thwart(l.X) || l.X > 6 || l.X < -10);
                }
                if (!PlankAt(x, y - 1) || !PlankAt(x - 1, y)) p.Put(x, y, Timber.Light);
            }

        if (oarsOut)
            foreach (var side in new[] { -1, 1 })
            {
                var handle = f.Map(3.5f, side * 2.5f);
                var rowlock = f.Map(-0.5f, side * 6.2f);
                var blade = f.Map(-9.5f, side * 10.5f);
                var c = p.Canvas;
                // The blade lies flat on the water with a little ripple round it.
                var (rx, ry) = facing is Facing.East or Facing.West ? (2.6f, 1.4f) : (1.4f, 2.6f);
                c.Ellipse(blade.X + 1, blade.Y + 1.5f, rx, ry, SmallShadow);
                c.Ellipse(blade.X, blade.Y, rx + 0.9f, ry + 0.9f, River.Highlight with { A = 0.35f });
                c.Line(handle.X, handle.Y, rowlock.X, rowlock.Y, Timber.Light);
                c.Line(rowlock.X, rowlock.Y, blade.X, blade.Y, Timber.Light);
                c.Ellipse(blade.X, blade.Y, rx, ry, Timber.Edge);
                c.Ellipse(blade.X - 0.3f, blade.Y - 0.3f, rx - 0.8f, ry - 0.8f, Timber.Light);
                p.Put((int)rowlock.X, (int)rowlock.Y, Iron.Light);
            }
        else
            foreach (var side in new[] { -1, 1 })
            {
                // Shipped oars lie along the floor on the thwarts, blades toward the stern.
                var from = f.Map(-9.5f, side * 2.5f);
                var to = f.Map(6.5f, side * 2.5f);
                p.Canvas.Line(from.X, from.Y, to.X, to.Y, Timber.Light);
                var blade = f.Box(-10, side < 0 ? -3.5f : 1.5f, 4, 2);
                p.Fill(blade, Timber.Light);
                var oarlock = f.Map(-0.5f, side * 5.5f);
                p.Put((int)oarlock.X, (int)oarlock.Y, Iron.Light);
            }
        // An iron ring at the bow for the mooring line.
        var ring = f.Map(9.5f, 0.5f);
        p.Put((int)ring.X, (int)ring.Y, Iron.Light);
        return p.Image;
    }

    // ----------------------------------------------------------------------
    // Diagonal vehicles (round 3).
    // ----------------------------------------------------------------------

    /// <summary>
    /// The handcart facing a diagonal: the same cart as the cardinal sprites,
    /// part for part and pixel for pixel in size (bed 16 × 12, iron-rimmed
    /// wheels, two-pixel shafts with rope grips, the rope coil or the lashed
    /// logs and sack), laid out in the cart's own frame and turned 45°. Each
    /// part is rasterised as a mask and shaded from its open sides, so the
    /// edges are clean 45° stairs and the light stays in the north-west: the
    /// side boards facing the light are lit, the far ones shaded, the ones
    /// side-on take the middle tone. Floor seams fall on the diagonal lattice.
    /// </summary>
    private static Image HandcartDiagonal(Facing facing, bool loaded, bool pulled)
    {
        var p = new Plate(32, 32, 1);
        var f = new Heading(facing);
        var tip = pulled ? 10f : 12f;
        var bed = Mask.Of(f, (u, v) => u >= -13 && u < 3 && MathF.Abs(v) < 6);
        // Three pixels a row, as heavy as the cardinal two-pixel shafts, four pixels either side of the middle.
        var shafts = new[] { -4f, 4f }.Select(side => Mask.Of(f, (u, v) => u >= -1 && u < tip && MathF.Abs(v - side) < 1.05f)).ToArray();
        var wheels = new[] { -8f, 8f }.Select(side => Mask.Of(f, (u, v) => Stadium(u, v, -8, -2, side) <= 2.05f)).ToArray();
        var hubs = new[] { -10.5f, 10.5f }.Select(side => Mask.Of(f, (u, v) => MathF.Abs(u + 5) < 1.05f && MathF.Abs(v - side) < 0.75f)).ToArray();

        // L2 shadow in one pass, as the cardinal cart; lifted shafts throw theirs further from them.
        var shadow = new ShadowMask(32, 32);
        foreach (var (x, y) in bed.Pixels()) shadow.Add(x + 1, y + 2);
        foreach (var wheel in wheels)
            foreach (var (x, y) in wheel.Pixels()) shadow.Add(x + 1, y + 2);
        var (liftX, liftY) = pulled ? (3, 5) : (1, 1);
        foreach (var shaft in shafts)
            foreach (var (x, y) in shaft.Pixels()) shadow.Add(x + liftX, y + liftY);
        shadow.Paint(p, SmallShadow);

        // Shafts: lit on top and the edge step along the side away from the light, then rope round the
        // last three pixels, wound in alternating turns.
        foreach (var shaft in shafts)
            foreach (var (x, y) in shaft.Pixels())
            {
                var local = f.Local(x + 0.5f, y + 0.5f);
                Color c = shaft.Rim(x, y) && !shaft.Lit(x, y) ? Timber.Edge : Timber.Light;
                if (local.X >= tip - 3) c = f.Lattice(x, y).Along % 2 == 0 ? Timber.Shade : Timber.Highlight;
                p.Put(x, y, c);
            }
        foreach (var wheel in wheels) TyreDiagonal(p, f, wheel);
        foreach (var hub in hubs)
            foreach (var (x, y) in hub.Pixels()) p.Put(x, y, Iron.Shade);
        CartBedDiagonal(p, f, bed);
        if (loaded) CartLoadDiagonal(p, f);
        else
        {
            var coil = f.Map(-6.5f, 0.5f);
            RopeCoil(p, coil.X, coil.Y);
        }
        return p.Image;
    }

    /// <summary>Distance from (u, v) to the segment from (from, at) to (to, at) along u: a stadium's field.</summary>
    private static float Stadium(float u, float v, float from, float to, float at)
    {
        var along = Math.Clamp(u, from, to);
        return MathF.Sqrt((u - along) * (u - along) + (v - at) * (v - at));
    }

    /// <summary>
    /// A wheel's iron tyre seen from above at 45°: an edge outline, the tread
    /// lit on its sunny side and plain on the other, and darker ends where
    /// the rim curves down to the ground, with one bright glint.
    /// </summary>
    private static void TyreDiagonal(Plate p, Heading f, Mask tyre)
    {
        var tread = tyre.Inner();
        (int X, int Y)? glint = null;
        foreach (var (x, y) in tyre.Pixels())
        {
            if (!tread[x, y])
            {
                p.Put(x, y, Iron.Edge);
                continue;
            }
            var u = f.Local(x + 0.5f, y + 0.5f).X;
            var lit = tread.Lit(x, y);
            p.Put(x, y, u < -8.6f || u > -1.4f ? Iron.Shade : lit ? Iron.Light : Iron.Base);
            if (lit && u is > -7.5f and < -2.5f && (glint is null || x + y < glint.Value.X + glint.Value.Y)) glint = (x, y);
        }
        if (glint is { } g) p.Put(g.X, g.Y, Iron.Highlight);
    }

    /// <summary>
    /// The cart bed at 45°: edge outline, side boards lit where they face the
    /// light, shaded where they face away and in the middle tone side-on, a
    /// plank floor with seams across the cart on the diagonal lattice and the
    /// back board's shadow on it, and an iron fitting at each corner.
    /// </summary>
    private static void CartBedDiagonal(Plate p, Heading f, Mask bed)
    {
        var boards = bed.Inner();
        var floor = boards.Inner();
        var seam = Toward(Timber, 2, 1, 0.65f);
        foreach (var (x, y) in bed.Pixels())
        {
            Color c;
            if (!boards[x, y]) c = Timber.Edge;
            else if (!floor[x, y])
            {
                var light = boards.Light(x, y);
                c = light > 0.3f ? Timber.Light : light < -0.3f ? Timber.Shade : Toward(Timber, 2, 3, 0.5f);
            }
            else if (floor.Light(x, y) > 0.3f) c = Toward(Timber, 2, 1, 0.5f);
            else c = f.Lattice(x, y).Along is -11 or -5 ? seam : Timber.Base;
            p.Put(x, y, c);
        }
        // Iron corner fittings on the boards: the pixel furthest into each corner, the one nearest the light brightest.
        var corners = new List<(int X, int Y)>();
        foreach (var (cu, cv) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
        {
            (int X, int Y) best = (0, 0);
            var reach = float.MinValue;
            foreach (var (x, y) in boards.Pixels())
            {
                if (floor[x, y]) continue;
                var local = f.Local(x + 0.5f, y + 0.5f);
                var score = cu * local.X + cv * local.Y;
                if (score > reach) (reach, best) = (score, (x, y));
            }
            corners.Add(best);
        }
        var ordered = corners.OrderBy(c => c.X + c.Y).ToList();
        for (var i = 0; i < ordered.Count; i++)
            p.Put(ordered[i].X, ordered[i].Y, i == 0 ? Iron.Highlight : i == ordered.Count - 1 ? Iron.Base : Iron.Light);
    }

    /// <summary>
    /// The load at 45°: two logs side by side with a third resting on them,
    /// their pale end grain showing past the back of the bed, a rope lashed
    /// across them, and a grain sack lying across the front of the bed.
    /// </summary>
    private static void CartLoadDiagonal(Plate p, Heading f)
    {
        // The top log's cut end lines up with the others here: set one pixel in, as on the cardinal cart,
        // its end would turn into a notch on the 45° stairs.
        var logs = new[]
        {
            Mask.Of(f, (u, v) => u >= -15 && u < -3 && v >= -6 && v < -1),
            Mask.Of(f, (u, v) => u >= -15 && u < -3 && v >= 1 && v < 6),
            Mask.Of(f, (u, v) => u >= -15 && u < -4 && v >= -3 && v < 3),
        };
        foreach (var log in logs)
        {
            var wood = log.Inner();
            var back = wood.Pixels().Min(px => f.Local(px.X + 0.5f, px.Y + 0.5f).X);
            var heart = wood.Pixels().Where(px => f.Local(px.X + 0.5f, px.Y + 0.5f).X < back + 0.8f)
                .Select(px => (px, f.Local(px.X + 0.5f, px.Y + 0.5f).Y)).ToList();
            var middle = heart.Count == 0 ? 0 : heart.Average(h => h.Item2);
            var core = heart.Count == 0 ? (-1, -1) : heart.OrderBy(h => MathF.Abs(h.Item2 - (float)middle)).First().px;
            foreach (var (x, y) in log.Pixels())
            {
                Color c;
                if (!wood[x, y]) c = Timber.Edge;
                else if (f.Local(x + 0.5f, y + 0.5f).X < back + 0.8f) c = (x, y) == core ? Timber.Light : Timber.Highlight;
                else if (wood.Rim(x, y)) c = wood.Lit(x, y) ? Timber.Base : Toward(Timber, 1, 0, 0.45f);
                else c = Timber.Shade;
                p.Put(x, y, c);
            }
        }
        // The lashing: a rope across all three logs, its ends tucked under in the edge step.
        var load = logs[0].Or(logs[1]).Or(logs[2]);
        var rope = load.Pixels().Where(px => MathF.Abs(f.Local(px.X + 0.5f, px.Y + 0.5f).X + 7.5f) < 0.55f).ToList();
        if (rope.Count > 0)
        {
            var across = rope.Select(px => f.Local(px.X + 0.5f, px.Y + 0.5f).Y).ToList();
            foreach (var (x, y) in rope)
            {
                var v = f.Local(x + 0.5f, y + 0.5f).Y;
                p.Put(x, y, v <= across.Min() + 0.1f || v >= across.Max() - 0.1f ? Timber.Edge : Timber.Light);
            }
        }
        SackDiagonal(p, f, -0.5f, 0.5f);
    }

    /// <summary>
    /// A cloth sack lying across the cart at 45°, as <see cref="LyingSack"/>:
    /// a long rounded body lit north-west with a shaded underside and a lit
    /// spot, a fold where it sags, and its neck tied off in twine at the end
    /// nearer the light, the tuft flaring past the tie.
    /// </summary>
    private static void SackDiagonal(Plate p, Heading f, float cu, float cv)
    {
        // Radii along the cart (u) and across it (v): the sack lies across the bed.
        Mask Body(float ru, float rv, float dx, float dy) => new((x, y) =>
        {
            var local = f.Local(x + 0.5f - dx, y + 0.5f - dy);
            var a = (local.X - cu) / ru;
            var b = (local.Y - cv) / rv;
            return a * a + b * b <= 1;
        });
        var centre = f.Map(cu, cv);
        var ground = new ShadowMask(32, 32);
        foreach (var (x, y) in Body(2.5f, 3.5f, 0, 0).Pixels()) ground.Add(x + 1, y + 1);
        ground.Paint(p, SmallShadow);
        foreach (var (x, y) in Body(2.5f, 3.5f, 0, 0).Pixels()) p.Put(x, y, Cloth.Edge);
        foreach (var (x, y) in Body(1.6f, 2.6f, 0.4f, 0.4f).Pixels()) p.Put(x, y, Cloth.Shade);
        foreach (var (x, y) in Body(1.3f, 2.3f, -0.3f, -0.3f).Pixels()) p.Put(x, y, Cloth.Base);
        p.Put((int)(centre.X - 1), (int)(centre.Y - 1), Cloth.Light);
        var fold = f.Map(cu, cv + 1);
        p.Put((int)fold.X, (int)fold.Y, Cloth.Shade);
        // The neck at the end of the long axis nearer the north-west.
        var ends = new[] { -1f, 1f }.Select(side => (side, at: f.Map(cu, cv + side * 3.5f))).OrderBy(e => e.at.X + e.at.Y).First();
        var outward = f.Right * ends.side;
        void PutAt(Vector2 at, Color c) => p.Put((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), c);
        var tie = f.Map(cu, cv + ends.side * 3.0f);
        PutAt(tie, Timber.Shade);
        PutAt(tie + outward * 1.2f, Cloth.Light);
        var flare = tie + outward * 2.4f;
        PutAt(flare, Cloth.Base);
        PutAt(flare + f.Forward * 1.1f, Cloth.Edge);
        PutAt(flare - f.Forward * 1.1f, Cloth.Edge);
    }

    /// <summary>
    /// The rowing boat facing a diagonal: the same hull, outline, gunwale,
    /// thwarts, decks, oars and ripple as the cardinal boat, laid out in the
    /// boat's frame and turned 45°. The hull is rasterised as a mask whose
    /// rings give the outline, gunwale and inner wall one pixel each, lit by
    /// which way that part of the hull faces; the floorboards and thwarts
    /// fall on the diagonal lattice, and each oar blade is a small oval
    /// turned with the boat.
    /// </summary>
    private static Image BoatDiagonal(Facing facing, bool oarsOut)
    {
        var p = new Plate(32, 32, 1);
        var f = new Heading(facing);
        // The boat sits one pixel toward its bow, so the oars trailing behind it stay inside the tile; the
        // blades lie a little nearer the hull than on the cardinal boat for the same reason.
        var shift = new Vector2(MathF.Sign(f.Forward.X), MathF.Sign(f.Forward.Y));
        Vector2 Local(float x, float y) => f.Local(x - shift.X, y - shift.Y);
        Vector2 Map(float u, float v) => f.Map(u, v) + shift;
        int Across(int x, int y) => f.Lattice(x - (int)shift.X, y - (int)shift.Y).Across;
        float Depth(float x, float y)
        {
            var local = Local(x, y);
            var beam = HalfBeam(local.X);
            return beam < 0 ? -9 : MathF.Min(beam - MathF.Abs(local.Y), local.X + 14);
        }
        bool Thwart(float u) => u is >= -6 and < -4 or >= 3 and < 5;
        bool Plank(float u) => Thwart(u) || u > 6 || u < -10;
        var light = new Vector2(-0.7071f, -0.7071f);
        var hull = new Mask((x, y) => Depth(x + 0.5f, y + 0.5f) > 0);
        var gunwale = hull.Inner();
        var wall = gunwale.Inner();
        var floor = wall.Inner();

        var shadow = new ShadowMask(32, 32);
        foreach (var (x, y) in hull.Pixels()) shadow.Add(x + 2, y + 3);
        shadow.Paint(p, SmallShadow);
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var depth = Depth(x + 0.5f, y + 0.5f);
                if (depth is <= -1.3f or > 0 || hull[x, y]) continue;
                if (PixelArt.Hash(x, y, 233) % 2 == 0) continue;
                p.Put(x, y, River.Highlight with { A = 0.45f });
            }

        foreach (var (x, y) in hull.Pixels())
        {
            var local = Local(x + 0.5f, y + 0.5f);
            var (u, v) = (local.X, local.Y);
            var sternward = u + 14 < HalfBeam(u) - MathF.Abs(v);
            var outward = sternward ? f.Direction(-1, 0) : f.Direction(u > 4 ? 0.8f : 0, MathF.Sign(v));
            var facingLight = outward.Normalized().Dot(light);
            Color c;
            if (!gunwale[x, y]) c = Timber.Edge;
            else if (!wall[x, y]) c = facingLight > 0.15f ? Timber.Light : facingLight < -0.15f ? Timber.Base : Toward(Timber, 2, 3, 0.5f);
            else if (!floor[x, y]) c = facingLight > 0.15f ? Toward(Timber, 1, 0, 0.55f) : Timber.Base;
            else if (Plank(u)) c = Timber.Base;
            else c = Math.Abs(Across(x, y)) == 3 ? Toward(Timber, 1, 0, 0.45f) : Timber.Shade;
            p.Put(x, y, c);
        }
        // Thwarts and decks: their north and west edges catch the light, as on the cardinal boat.
        bool PlankAt(int x, int y) => floor[x, y] && Plank(Local(x + 0.5f, y + 0.5f).X);
        foreach (var (x, y) in floor.Pixels())
            if (PlankAt(x, y) && (!PlankAt(x, y - 1) || !PlankAt(x - 1, y))) p.Put(x, y, Timber.Light);

        if (oarsOut)
            foreach (var side in new[] { -1, 1 })
            {
                var handle = Map(3.5f, side * 2.5f);
                var rowlock = Map(-0.5f, side * 6.2f);
                var blade = Map(-8.8f, side * 9.6f);
                var c = p.Canvas;
                Oval(p, f, blade + new Vector2(1, 1.5f), 2.6f, 1.4f, SmallShadow);
                Oval(p, f, blade, 3.5f, 2.3f, River.Highlight with { A = 0.35f });
                c.Line(handle.X, handle.Y, rowlock.X, rowlock.Y, Timber.Light);
                c.Line(rowlock.X, rowlock.Y, blade.X, blade.Y, Timber.Light);
                Oval(p, f, blade, 2.6f, 1.4f, Timber.Edge);
                Oval(p, f, blade - new Vector2(0.3f, 0.3f), 1.8f, 0.6f, Timber.Light);
                p.Put((int)rowlock.X, (int)rowlock.Y, Iron.Light);
            }
        else
            foreach (var side in new[] { -1, 1 })
            {
                var from = Map(-9.5f, side * 2.5f);
                var to = Map(6.5f, side * 2.5f);
                p.Canvas.Line(from.X, from.Y, to.X, to.Y, Timber.Light);
                var blade = new Mask((x, y) =>
                {
                    var local = Local(x + 0.5f, y + 0.5f);
                    return local.X >= -10 && local.X < -6 && MathF.Abs(local.Y - side * 2.5f) < 1.05f;
                });
                foreach (var (x, y) in blade.Pixels()) p.Put(x, y, Timber.Light);
                var oarlock = Map(-0.5f, side * 5.5f);
                p.Put((int)oarlock.X, (int)oarlock.Y, Iron.Light);
            }
        var ring = Map(9.5f, 0.5f);
        p.Put((int)ring.X, (int)ring.Y, Iron.Light);
        return p.Image;
    }

    /// <summary>An oval of radii <paramref name="ru"/> along the vehicle and <paramref name="rv"/> across it, centred on a tile point.</summary>
    private static void Oval(Plate p, Heading f, Vector2 centre, float ru, float rv, Color color)
    {
        for (var y = (int)(centre.Y - ru - 1); y <= (int)(centre.Y + ru + 1); y++)
            for (var x = (int)(centre.X - ru - 1); x <= (int)(centre.X + ru + 1); x++)
            {
                var offset = new Vector2(x + 0.5f - centre.X, y + 0.5f - centre.Y);
                var a = offset.Dot(f.Forward) / ru;
                var b = offset.Dot(f.Right) / rv;
                if (a * a + b * b <= 1) p.Put(x, y, color);
            }
    }

    /// <summary>
    /// The boat tied up beside a Port: the round-2 Port with its land row to
    /// the north, one column of open docking water to its east, and the
    /// boat alongside the pier with its oars shipped and a bow line to the
    /// pier's bollard.
    /// </summary>
    private static Image MooredBoat()
    {
        var image = Bitmap.Over(Shore(3, 4, DoorSide.North), Draw(Design.Port, 2, 4, 32, new BuildingDoor(DoorSide.North, 1)), 0, 0);
        // Hull two pixels clear of the pier's piles (the pier's east edge is at x = 44).
        const int boatX = 53 - 16, boatY = 40;
        Sheet.Blend(image, Boat(Facing.North, oarsOut: false), boatX, boatY);
        var line = new PixelCanvas(image, new Rect2I(0, 0, image.GetWidth(), image.GetHeight()), 1);
        line.Line(42.5f, 48.5f, boatX + 16.5f, boatY + 16 - 9.5f, Cloth.Shade);
        return image;
    }

    // ----------------------------------------------------------------------
    // A Port with its boats moored (round 3).
    // ----------------------------------------------------------------------

    /// <summary>The review set of moored Ports: Id, footprint, door side, water and note.</summary>
    private static readonly (string Id, int W, int H, DoorSide Side, TerrainStyle Water, string Note)[] MooredPorts =
    [
        ("Port.2x4.moored", 2, 4, DoorSide.North, TerrainStyle.Ocean,
            "Round 3: the Port on the open sea with six boats moored, three along each side of the pier, each tied by its bow to its own bollard. The painted boat makes way for them."),
        ("Port.2x4.N.moored", 2, 4, DoorSide.North, TerrainStyle.River,
            "Round 3: six boats moored bow-in along the pier, with a lane of open water kept clear on each side for docking."),
        ("Port.4x2.E.moored", 4, 2, DoorSide.East, TerrainStyle.River, "Round 3: the east-facing Port with its six boats."),
        ("Port.2x4.S.moored", 2, 4, DoorSide.South, TerrainStyle.River, "Round 3: the south-facing Port with its six boats."),
        ("Port.4x2.W.moored", 4, 2, DoorSide.West, TerrainStyle.River, "Round 3: the west-facing Port with its six boats."),
    ];

    /// <summary>
    /// A Port with six boats moored: three along each long side of its pier,
    /// between the shore and the T-head, each lying bow-in with its oars
    /// shipped and a bow line to a bollard on the pier's edge. The picture
    /// adds a column (or row) of open water on both long sides, the docking
    /// lanes: the boats' sterns reach a third of the way into them and the
    /// rest stays clear for a boat coming in. The Port itself is the approved
    /// drawing for that rotation, without the round-1 painted boat.
    /// </summary>
    private static Image MooredPort(int tilesWide, int tilesHigh, DoorSide side, TerrainStyle water)
    {
        var land = PortLandSide(tilesWide, tilesHigh, side);
        var lengthwise = land is DoorSide.North or DoorSide.South;
        var (offsetX, offsetY) = lengthwise ? (32, 0) : (0, 32);
        var image = HarbourGround(tilesWide + (lengthwise ? 2 : 0), tilesHigh + (lengthwise ? 0 : 2), land, water);
        Sheet.Blend(image, Draw(Design.Port, tilesWide, tilesHigh, 32, new BuildingDoor(side, 1)), offsetX, offsetY);
        var frame = new PortFrame(land, tilesWide * 32, tilesHigh * 32);
        var pierFrom = frame.Breadth / 2f - 12;
        var pierTo = frame.Breadth / 2f + 12;
        Vector2 At(float across, float outward) => frame.Point(across, outward) + new Vector2(offsetX, offsetY);
        // The bow lies two pixels clear of the pier's piles, as in the approved boat.moored.E.
        const float bowGap = 3, halfLength = 14;
        var berths = new List<(Vector2 Bollard, Vector2 Hull, Facing Facing)>();
        foreach (var outward in new[] { 48f, 64f, 80f })
            foreach (var high in new[] { false, true })
            {
                var edge = high ? pierTo : pierFrom;
                var sign = high ? 1 : -1;
                var hull = At(edge + sign * (bowGap + halfLength), outward);
                // The bow points back across the water at the pier.
                var facing = (lengthwise, high) switch
                {
                    (true, true) => Facing.West,
                    (true, false) => Facing.East,
                    (false, true) => Facing.North,
                    _ => Facing.South,
                };
                berths.Add((At(edge - sign * 3, outward), hull, facing));
            }
        // A bollard for each boat on the pier's edge; the approved Port already has one at the first east berth.
        var deck = new Plate(image.GetWidth(), image.GetHeight(), 1);
        var existing = At(pierTo - 3, 48);
        foreach (var (bollard, _, _) in berths)
            if (bollard.DistanceTo(existing) > 0.5f) Bollard(deck, bollard.X, bollard.Y);
        Sheet.Blend(image, deck.Image, 0, 0);
        var lines = new PixelCanvas(image, new Rect2I(0, 0, image.GetWidth(), image.GetHeight()), 1);
        foreach (var (bollard, hull, facing) in berths)
        {
            var corner = new Vector2I((int)MathF.Round(hull.X) - 16, (int)MathF.Round(hull.Y) - 16);
            Sheet.Blend(image, Boat(facing, oarsOut: false), corner.X, corner.Y);
            var ring = new Heading(facing).Map(9.5f, 0.5f) + new Vector2(corner.X, corner.Y);
            var toward = (ring - bollard).Normalized();
            var from = bollard + toward * 1.5f;
            lines.Line(from.X + 0.5f, from.Y + 0.5f, ring.X + 0.5f, ring.Y + 0.5f, Cloth.Shade);
        }
        return image;
    }

    /// <summary>Grass on the land row or column and the given water everywhere else, for a harbour picture.</summary>
    private static Image HarbourGround(int tilesWide, int tilesHigh, DoorSide land, TerrainStyle water)
    {
        var ground = Grass(tilesWide, tilesHigh, 32);
        var atlas = WaterTextures.Atlas(32).GetImage();
        for (var y = 0; y < tilesHigh; y++)
            for (var x = 0; x < tilesWide; x++)
            {
                var onLand = land switch
                {
                    DoorSide.North => y == 0,
                    DoorSide.South => y == tilesHigh - 1,
                    DoorSide.West => x == 0,
                    _ => x == tilesWide - 1,
                };
                if (!onLand) ground.BlitRect(atlas, (Rect2I)WaterTextures.Region(water, x, y, 32), new Vector2I(x * 32, y * 32));
            }
        return ground;
    }

    // ----------------------------------------------------------------------
    // Market plaza (round 3).
    // ----------------------------------------------------------------------

    /// <summary>
    /// The Market as a whole: the market hall at the head of a small plaza of
    /// packed earth with stalls standing on it, and a Road leading in. The
    /// plaza is a block of ordinary Road tiles (7 × 5) drawn by the game's
    /// <see cref="RoadSprites"/>: where every neighbour is Road the pieces
    /// join into one continuous area, so it shows no lanes and no tile grid,
    /// only the soft worn edge round the outside. The stall tiles count as
    /// plaza ground, so the earth runs under them. Eight approved stalls
    /// stand in two rows of four facing each other across the middle, with
    /// an open way from the hall's door to the Road.
    /// </summary>
    private static Image MarketPlaza(int tilePixels)
    {
        const int tilesWide = 9, tilesHigh = 9;
        var image = Grass(tilesWide, tilesHigh, tilePixels);
        static bool Plaza(int x, int y) => x is >= 1 and <= 7 && y is >= 2 and <= 6;
        // The Road leads in from the south on the middle column and carries on past the picture's edge.
        static bool Road(int x, int y) => Plaza(x, y) || (x == 4 && y > 6);
        // The hall's door asks the plaza tile in front of it for a doorstep path, as any building does.
        var doorTile = new Vector2I(4, 2);
        for (var y = 0; y < tilesHigh; y++)
            for (var x = 0; x < tilesWide; x++)
            {
                var links = RoadLinks.None;
                if (Road(x, y)) links |= RoadLinks.Road;
                if (Road(x, y - 1)) links |= RoadLinks.North;
                if (Road(x + 1, y)) links |= RoadLinks.East;
                if (Road(x, y + 1)) links |= RoadLinks.South;
                if (Road(x - 1, y)) links |= RoadLinks.West;
                if (Road(x + 1, y - 1)) links |= RoadLinks.NorthEast;
                if (Road(x + 1, y + 1)) links |= RoadLinks.SouthEast;
                if (Road(x - 1, y + 1)) links |= RoadLinks.SouthWest;
                if (Road(x - 1, y - 1)) links |= RoadLinks.NorthWest;
                if (!RoadSprites.Draws(links)) continue;
                if (new Vector2I(x, y) == doorTile) links |= RoadLinks.DoorNorth;
                var variant = (int)(PixelArt.Hash(x, y, 7) % RoadSprites.VariantCount);
                Sheet.Blend(image, RoadSprites.Render(links, variant, tilePixels, false), x * tilePixels, y * tilePixels);
            }
        Sheet.Blend(image, Draw(Design.Market, 2, 2, tilePixels, new BuildingDoor(DoorSide.South, 1)), 3 * tilePixels, 0);
        // Two rows of four stalls face each other across the plaza, leaving the middle column open.
        foreach (var (x, y, side) in MarketStalls)
            Sheet.Blend(image, Draw(Design.MarketStall, 1, 1, tilePixels, new BuildingDoor(side)), x * tilePixels, y * tilePixels);
        return image;
    }

    /// <summary>Where the stalls stand on the plaza, in tiles, and the way each faces.</summary>
    private static readonly (int X, int Y, DoorSide Side)[] MarketStalls =
    [
        (2, 4, DoorSide.North), (3, 4, DoorSide.North), (5, 4, DoorSide.North), (6, 4, DoorSide.North),
        (2, 5, DoorSide.South), (3, 5, DoorSide.South), (5, 5, DoorSide.South), (6, 5, DoorSide.South),
    ];

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
