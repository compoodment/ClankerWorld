using System.Text.Json;
using ArtPreview;
using ClankerWorld.GodotClient.UI;
using Godot;

var command = args.Length > 0 ? args[0] : "baseline";
var outRoot = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "out");
switch (command)
{
    case "baseline":
        Baseline.Run(Path.Combine(outRoot, "baseline"));
        break;
    case "proposed":
        Proposals.Run(Path.Combine(outRoot, "proposed"));
        break;
    case "scene":
        SceneRunner.Run(Path.Combine(outRoot, "scene"));
        break;
    default:
        Console.Error.WriteLine("usage: baseline|proposed|scene <out dir>");
        return 2;
}
return 0;

/// <summary>Writes every current texture as 1× PNGs plus captioned 4× sheets and an index.</summary>
static class Baseline
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var families = new List<(string Family, List<Entry> Entries, Color? Backdrop, int Columns)>();

        // Ground tiles: both variants at 32 px and at the 16 px mid-zoom atlas.
        var terrain = new List<Entry>();
        foreach (var style in Enum.GetValues<TerrainStyle>())
            for (var variant = 0; variant < TerrainTextures.VariantCount; variant++)
                terrain.Add(new("terrain", $"{style}.v{variant}", TerrainTextures.Tile(style, variant, 32)));
        families.Add(("terrain", terrain, new Color("2B2B2B"), 8));
        var terrain16 = new List<Entry>();
        foreach (var style in Enum.GetValues<TerrainStyle>())
            for (var variant = 0; variant < TerrainTextures.VariantCount; variant++)
                terrain16.Add(new("terrain16", $"{style}.v{variant}.16", TerrainTextures.Tile(style, variant, 16)));
        families.Add(("terrain16", terrain16, new Color("2B2B2B"), 8));

        // Hill relief overlays, shown over grass so the transparency reads.
        var hills = new List<Entry>();
        for (var variant = 0; variant < TerrainTextures.VariantCount; variant++)
        {
            var over = TerrainTextures.Tile(TerrainStyle.Grass, 0, 32);
            Sheet.Blend(over, TerrainTextures.HillOverlay(variant, 32), 0, 0);
            hills.Add(new("hills", $"hill.v{variant}.on_grass", over));
            hills.Add(new("hills", $"hill.v{variant}.mask", TerrainTextures.HillOverlay(variant, 32)));
        }
        families.Add(("hills", hills, null, 4));

        // Water: one repeating block per style, shown whole (16×16 tiles).
        var water = new List<Entry>();
        foreach (var style in new[] { TerrainStyle.Ocean, TerrainStyle.Lake, TerrainStyle.River, TerrainStyle.ShallowWater })
            water.Add(new("water", $"{style}.block", WaterTextures.Block(style, 32)));
        families.Add(("water", water, new Color("2B2B2B"), 2));

        // Land-to-land transition pieces for a few surfaces, and the coast masks.
        var edges = new List<Entry>();
        foreach (var style in new[] { TerrainStyle.Grass, TerrainStyle.ForestFloor, TerrainStyle.Snow, TerrainStyle.Sand })
            for (var piece = 0; piece < TerrainTransitions.PieceCount; piece++)
                edges.Add(new("edges", $"{style}.p{piece}", TerrainTransitions.Piece(style, piece, 32)));
        families.Add(("edges", edges, new Color("2B2B2B"), TerrainTransitions.PieceCount));
        var coasts = new List<Entry>();
        foreach (var (row, name) in new[] { (CoastEdges.LandRow, "land"), (CoastEdges.ShallowRow, "shallow"), (CoastEdges.FoamRow, "foam"), (CoastEdges.RiverShallowRow, "river_shallow") })
            for (var piece = 0; piece < TerrainTransitions.PieceCount; piece++)
                coasts.Add(new("coasts", $"{name}.p{piece}", CoastEdges.Piece(row, piece, 32)));
        families.Add(("coasts", coasts, new Color("325F89"), TerrainTransitions.PieceCount));

        // Roads: representative pieces on grass, all four variants of a straight.
        var roads = new List<Entry>();
        var samples = new (string Id, RoadLinks Links)[]
        {
            ("straight_ns", RoadLinks.Road | RoadLinks.North | RoadLinks.South),
            ("straight_ew", RoadLinks.Road | RoadLinks.East | RoadLinks.West),
            ("corner_ne", RoadLinks.Road | RoadLinks.North | RoadLinks.East),
            ("corner_sw", RoadLinks.Road | RoadLinks.South | RoadLinks.West),
            ("tee_nes", RoadLinks.Road | RoadLinks.North | RoadLinks.East | RoadLinks.South),
            ("cross", RoadLinks.Road | RoadLinks.North | RoadLinks.East | RoadLinks.South | RoadLinks.West),
            ("end_n", RoadLinks.Road | RoadLinks.North),
            ("lone", RoadLinks.Road),
            ("diag_ne_sw", RoadLinks.Road | RoadLinks.NorthEast | RoadLinks.SouthWest),
            ("diag_nw_se", RoadLinks.Road | RoadLinks.NorthWest | RoadLinks.SouthEast),
            ("beside_diag", RoadLinks.NorthEast),
            ("doorstep_s", RoadLinks.Road | RoadLinks.East | RoadLinks.West | RoadLinks.DoorNorth),
            ("doorstep_e", RoadLinks.Road | RoadLinks.North | RoadLinks.South | RoadLinks.DoorWest),
        };
        foreach (var (id, links) in samples)
        {
            var tile = TerrainTextures.Tile(TerrainStyle.Grass, 0, 32);
            Sheet.Blend(tile, RoadSprites.Render(links, 0, 32, false), 0, 0);
            roads.Add(new("roads", id, tile));
        }
        for (var variant = 0; variant < RoadSprites.VariantCount; variant++)
        {
            var tile = TerrainTextures.Tile(TerrainStyle.Grass, 0, 32);
            Sheet.Blend(tile, RoadSprites.Render(RoadLinks.Road | RoadLinks.North | RoadLinks.South, variant, 32, false), 0, 0);
            roads.Add(new("roads", $"straight_ns.v{variant}", tile));
        }
        foreach (var style in new[] { TerrainStyle.Sand, TerrainStyle.Snow })
        {
            var tile = TerrainTextures.Tile(style, 0, 32);
            Sheet.Blend(tile, RoadSprites.Render(RoadLinks.Road | RoadLinks.East | RoadLinks.West, 0, 32, RoadSprites.NeedsDarkEdge(style)), 0, 0);
            roads.Add(new("roads", $"straight_ew.on_{style}", tile));
        }
        families.Add(("roads", roads, null, 7));

        // Trees and natural sites at 32 and 16 px, over grass.
        var nature = new List<Entry>();
        foreach (var sprite in Enum.GetValues<NatureSprite>())
        {
            var tile = TerrainTextures.Tile(TerrainStyle.Grass, 0, 32);
            Sheet.Blend(tile, NatureSprites.Sprite(sprite, 32), 0, 0);
            nature.Add(new("nature", $"{sprite}", tile));
        }
        families.Add(("nature", nature, null, 8));
        var nature16 = new List<Entry>();
        foreach (var sprite in Enum.GetValues<NatureSprite>())
        {
            var tile = TerrainTextures.Tile(TerrainStyle.Grass, 0, 16);
            Sheet.Blend(tile, NatureSprites.Sprite(sprite, 16), 0, 0);
            nature16.Add(new("nature16", $"{sprite}.16", tile));
        }
        families.Add(("nature16", nature16, null, 8));

        // Buildings at the agreed footprints, door south, plus a few other door sides.
        var buildings = new List<Entry>();
        var footprints = new (BuildingKind Kind, int W, int H)[]
        {
            (BuildingKind.House, 1, 1), (BuildingKind.House, 1, 2), (BuildingKind.House, 2, 2),
            (BuildingKind.Warehouse, 2, 2), (BuildingKind.Warehouse, 2, 3),
            (BuildingKind.Farmhouse, 1, 1), (BuildingKind.Farmhouse, 1, 2),
            (BuildingKind.Blacksmith, 1, 2), (BuildingKind.Blacksmith, 2, 2),
            (BuildingKind.Silo, 1, 1),
            (BuildingKind.TailorShop, 1, 1), (BuildingKind.TailorShop, 2, 2),
            (BuildingKind.Workshop, 2, 2),
            (BuildingKind.Generic, 1, 1),
        };
        foreach (var (kind, w, h) in footprints)
            buildings.Add(new("buildings", $"{kind}.{w}x{h}", OnGrass(BuildingSprites.Render(kind, w, h, 32, new BuildingDoor(DoorSide.South, w / 2)), w, h)));
        foreach (var side in new[] { DoorSide.North, DoorSide.East, DoorSide.West })
            buildings.Add(new("buildings", $"House.1x2.door_{side}", OnGrass(BuildingSprites.Render(BuildingKind.House, 1, 2, 32, new BuildingDoor(side, 0)), 1, 2)));
        families.Add(("buildings", buildings, null, 6));
        var retired = new List<Entry>();
        foreach (var kind in new[] { BuildingKind.Shelter, BuildingKind.Storehouse, BuildingKind.Hearth, BuildingKind.Path, BuildingKind.Bedroll })
            retired.Add(new("retired", $"{kind}.1x1", OnGrass(BuildingSprites.Render(kind, 1, 1, 32), 1, 1)));
        families.Add(("retired", retired, null, 5));

        // Agents: every variant and life stage, over grass.
        var agents = new List<Entry>();
        foreach (var stage in new[] { "infant", "child", "adult", "elder" })
            for (var variant = 0; variant < AgentSprites.VariantCount; variant++)
            {
                var tile = TerrainTextures.Tile(TerrainStyle.Grass, 0, 32);
                Sheet.Blend(tile, AgentSprites.Sprite(variant, AgentSprites.StageIndex(stage), 32), 0, 0);
                agents.Add(new("agents", $"{stage}.v{variant}", tile));
            }
        families.Add(("agents", agents, null, AgentSprites.VariantCount));

        // Item icons at their native 16 px, on parchment like the panels.
        var items = new List<Entry>();
        foreach (var kind in ItemIcons.Kinds.Append("crate"))
            items.Add(new("items", kind, ItemIcons.Render(kind, 16)));
        families.Add(("items", items, new Color("E9DCC0"), 7));

        // Interface glyphs in the Light theme's ink, on parchment.
        var glyphs = new List<Entry>();
        foreach (var glyph in Enum.GetValues<PixelGlyph>())
            glyphs.Add(new("glyphs", $"{glyph}", PixelIcons.Texture(glyph, new Color("2A1A10"), new Color("3E7D3A"), 1).GetImage()));
        families.Add(("glyphs", glyphs, new Color("E9DCC0"), 8));

        // Logo and program icon, and the Main Menu valley by day and at dusk.
        var brand = new List<Entry> { new("brand", "logo", MenuLogo.Create()) };
        foreach (var size in MenuLogo.IconSizes.Where(s => s <= 48))
            brand.Add(new("brand", $"icon.{size}", MenuLogo.Icon(size)));
        families.Add(("brand", brand, new Color("5F8F5B"), 4));
        var menu = new List<Entry>();
        foreach (var night in new[] { false, true })
        {
            var scene = MenuScene.Create(night);
            var composed = scene.Sky.Duplicate();
            Sheet.Blend(composed, scene.Land, 0, 0);
            foreach (var cloud in scene.Clouds) Sheet.Blend(composed, cloud.Image, (int)cloud.X, (int)cloud.Y);
            menu.Add(new("menu", night ? "valley.dusk" : "valley.day", composed));
        }
        families.Add(("menu", menu, new Color("2B2B2B"), 1));

        var index = new List<object>();
        foreach (var (family, entries, backdrop, columns) in families)
        {
            var dir = Path.Combine(root, family);
            Directory.CreateDirectory(dir);
            foreach (var entry in entries)
            {
                var path = Path.Combine(dir, entry.Id + ".png");
                File.WriteAllBytes(path, entry.Image.SavePngToBuffer());
                index.Add(new { family, id = entry.Id, path = Path.GetRelativePath(root, path), width = entry.Image.GetWidth(), height = entry.Image.GetHeight() });
            }
            var scale = family is "menu" or "water" ? 2 : family == "brand" ? 3 : 4;
            var sheet = Sheet.Grid(entries, scale, Math.Min(columns, entries.Count), backdrop);
            File.WriteAllBytes(Path.Combine(root, $"sheet-{family}.png"), sheet.SavePngToBuffer());
            Console.WriteLine($"{family}: {entries.Count} entries");
        }
        File.WriteAllText(Path.Combine(root, "index.json"), JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static Image OnGrass(Image sprite, int tilesWide, int tilesHigh)
    {
        var ground = Image.CreateEmpty(tilesWide * 32, tilesHigh * 32, false, Image.Format.Rgba8);
        for (var y = 0; y < tilesHigh; y++)
            for (var x = 0; x < tilesWide; x++)
                ground.BlitRect(TerrainTextures.Tile(TerrainStyle.Grass, TerrainTextures.VariantAt(x, y), 32), new Rect2I(0, 0, 32, 32), new Vector2I(x * 32, y * 32));
        Sheet.Blend(ground, sprite, 0, 0);
        return ground;
    }
}

/// <summary>Proposed art: any class implementing <see cref="IArtProposal"/> under Proposed/ is rendered.</summary>
public interface IArtProposal
{
    string Family { get; }
    IEnumerable<Entry> Render();
}

static class Proposals
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var proposals = typeof(IArtProposal).Assembly.GetTypes()
            .Where(type => typeof(IArtProposal).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
            .Select(type => (IArtProposal)Activator.CreateInstance(type)!)
            .OrderBy(proposal => proposal.Family)
            .ToList();
        var index = new List<object>();
        foreach (var proposal in proposals)
        {
            var entries = proposal.Render().ToList();
            var dir = Path.Combine(root, proposal.Family);
            Directory.CreateDirectory(dir);
            foreach (var entry in entries)
            {
                var path = Path.Combine(dir, entry.Id + ".png");
                File.WriteAllBytes(path, entry.Image.SavePngToBuffer());
                index.Add(new { family = proposal.Family, id = entry.Id, path = Path.GetRelativePath(root, path), width = entry.Image.GetWidth(), height = entry.Image.GetHeight(), note = entry.Note });
            }
            var sheet = Sheet.Grid(entries, 4, Math.Min(8, Math.Max(1, entries.Count)), null);
            File.WriteAllBytes(Path.Combine(root, $"sheet-{proposal.Family}.png"), sheet.SavePngToBuffer());
            Console.WriteLine($"{proposal.Family}: {entries.Count} entries");
        }
        File.WriteAllText(Path.Combine(root, "index.json"), JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
    }
}

static class SceneRunner
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var spec = SceneSpec.TownCorner();
        var sets = new List<ArtSet> { new() };
        foreach (var type in typeof(IArtSetProvider).Assembly.GetTypes()
                     .Where(type => typeof(IArtSetProvider).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface))
        {
            var provider = (IArtSetProvider)Activator.CreateInstance(type)!;
            var set = new ArtSet { Name = provider.Name };
            provider.Apply(set);
            sets.Add(set);
        }
        // All proposals together: each delegate comes from the family that
        // redraws it. Crops and nature both set Nature, so orchard stages come
        // from crops and every other sprite from nature.
        var defaults = new ArtSet();
        var combined = new ArtSet { Name = "proposed" };
        Func<NatureSprite, int, Image>? cropsNature = null;
        foreach (var set in sets.Skip(1).OrderBy(set => set.Name))
        {
            if (set.Tile != defaults.Tile) combined.Tile = set.Tile;
            if (set.Hill != defaults.Hill) combined.Hill = set.Hill;
            if (set.WaterTile != defaults.WaterTile) combined.WaterTile = set.WaterTile;
            if (set.EdgePiece != defaults.EdgePiece) combined.EdgePiece = set.EdgePiece;
            if (set.CoastPiece != defaults.CoastPiece) combined.CoastPiece = set.CoastPiece;
            if (set.Road != defaults.Road) combined.Road = set.Road;
            if (set.Building != defaults.Building) combined.Building = set.Building;
            if (set.Agent != defaults.Agent) combined.Agent = set.Agent;
            if (set.Bridge is not null) combined.Bridge = set.Bridge;
            if (set.Relief is not null) combined.Relief = set.Relief;
            if (set.Nature != defaults.Nature)
            {
                if (set.Name == "crops") cropsNature = set.Nature;
                else combined.Nature = set.Nature;
            }
        }
        if (cropsNature is { } orchard)
        {
            var others = combined.Nature;
            combined.Nature = (sprite, size) => sprite is NatureSprite.OrchardGrowing or NatureSprite.OrchardFruiting or NatureSprite.OrchardPicked
                ? orchard(sprite, size) : others(sprite, size);
        }
        sets.Add(combined);
        var range = SceneSpec.MountainRange();
        foreach (var set in sets)
            foreach (var tileSize in new[] { 32, 16 })
            {
                var rangeImage = SceneComposer.Render(range, set, tileSize);
                File.WriteAllBytes(Path.Combine(root, $"range-{set.Name}-{tileSize}.png"), rangeImage.SavePngToBuffer());
                File.WriteAllBytes(Path.Combine(root, $"range-{set.Name}-{tileSize}.x{(tileSize == 32 ? 2 : 4)}.png"),
                    Sheet.Upscale(rangeImage, tileSize == 32 ? 2 : 4).SavePngToBuffer());
                var image = SceneComposer.Render(spec, set, tileSize);
                File.WriteAllBytes(Path.Combine(root, $"scene-{set.Name}-{tileSize}.png"), image.SavePngToBuffer());
                File.WriteAllBytes(Path.Combine(root, $"scene-{set.Name}-{tileSize}.x{(tileSize == 32 ? 3 : 4)}.png"),
                    Sheet.Upscale(image, tileSize == 32 ? 3 : 4).SavePngToBuffer());
                Console.WriteLine($"scene {set.Name} at {tileSize} px: {image.GetWidth()}x{image.GetHeight()}");
            }
    }
}
