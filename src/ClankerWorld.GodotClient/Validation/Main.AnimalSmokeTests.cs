using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void VerifyAnimalPresentation()
    {
        using var sheet = Image.CreateEmpty(256, 288, false, Image.Format.Rgba8);
        sheet.Fill(new Color("66765A"));
        var row = 0;
        foreach (var species in new[] { "chicken", "sheep", "cow", "horse" })
        {
            var headings = new HashSet<string>(StringComparer.Ordinal);
            for (var facing = 0; facing < 8; facing++)
            {
                using var adult = AnimalSprites.Sprite(species, facing, false, false);
                using var young = AnimalSprites.Sprite(species, facing, true, false);
                sheet.BlitRect(adult, new Rect2I(0, 0, 32, 32), new Vector2I(facing * 32, row * 64));
                sheet.BlitRect(young, new Rect2I(0, 0, 32, 32), new Vector2I(facing * 32, row * 64 + 32));
                headings.Add(HandcartPixelDigest(adult));
                if (HandcartPixelDigest(adult) == HandcartPixelDigest(young) || adult.GetWidth() != 32)
                    throw new InvalidOperationException("Animals must distinguish their young and adult silhouettes.");
            }
            if (headings.Count != 8) throw new InvalidOperationException("Animals must have all eight visible headings.");
            row++;
        }
        for (var facing = 0; facing < 8; facing++)
        {
            using var rider = AnimalSprites.Sprite("horse", facing, false, true);
            using var bare = AnimalSprites.Sprite("horse", facing, false, false);
            using var saddled = AnimalSprites.Sprite("horse", facing, false, false, saddled: true);
            using var woolly = AnimalSprites.Sprite("sheep", facing, false, false);
            using var shorn = AnimalSprites.Sprite("sheep", facing, false, false, shorn: true);
            using var small = AnimalSprites.Sprite("cow", facing, false, false, size: 16);
            using var foal = AnimalSprites.Sprite("horse", facing, true, true, saddled: true);
            sheet.BlitRect(rider, new Rect2I(0, 0, 32, 32), new Vector2I(facing * 32, 256));
            if (HandcartPixelDigest(bare) == HandcartPixelDigest(saddled) || HandcartPixelDigest(woolly) == HandcartPixelDigest(shorn) ||
                small.GetWidth() != 16 || HandcartPixelDigest(foal) == HandcartPixelDigest(bare))
                throw new InvalidOperationException("Animals must show a saddle, a shorn fleece, a 16 px drawing and an untacked foal.");
        }
        foreach (var (width, height) in new[] { (2, 2), (2, 4), (4, 2) })
        {
            using var yard = BuildingSprites.Render(BuildingKind.AnimalYard, width, height, 32, new BuildingDoor(DoorSide.South));
            using var halved = BuildingSprites.Render(BuildingKind.AnimalYard, width, height, 16, new BuildingDoor(DoorSide.South));
            // Trampled earth fills the middle of the approved pen.
            if (yard.GetWidth() != width * 32 || halved.GetHeight() != height * 16 || yard.GetPixel(width * 16, height * 16).A < 1f)
                throw new InvalidOperationException("The animal yard must draw its approved pen at both tile sizes.");
        }
        var evidence = OS.GetEnvironment("CLANKERWORLD_ANIMAL_ART_EVIDENCE");
        if (evidence.Length > 0 && sheet.SavePng(evidence) != Error.Ok)
            throw new InvalidOperationException("Animal art evidence could not be saved.");
        foreach (var kind in new[] { "eggs", "milk", "wool", "hide", "leather", "cooked_eggs", "milk_porridge", "rich_meal", "leather_sack", "saddle" })
            if (!ItemIcons.Has(kind)) throw new InvalidOperationException("Animal goods need visible inventory icons: " + kind);
        if (BuildingSprites.KindFor(["animal-yard", "animal-care"]) != BuildingKind.AnimalYard)
            throw new InvalidOperationException("An animal yard must use its fenced yard drawing.");
        var animal = new OwnerWorldAnimal("smoke-horse", "Moss", "horse", "female", 9, "adult", new(120, 60),
            "home", "Cedar household", "cared", null, 0, null, "rider", "Rowan", null, true, ["Linden"], ["Rowan"]);
        var ewe = new OwnerWorldAnimal("smoke-ewe", "Clover", "sheep", "female", 9, "adult", new(122, 60),
            "home", "Cedar household", "cared", null, 0, null, null, null, null, false, [], [], ProductProgressPercent: 20);
        var wild = ewe with { Id = "smoke-wild", Position = new(124, 60), HouseholdId = null, HouseholdName = null, ProductProgressPercent = null };
        if (!ewe.LooksShorn || wild.LooksShorn || (ewe with { ProductProgressPercent = 50 }).LooksShorn)
            throw new InvalidOperationException("Only a household sheep early in its wool cycle may look shorn.");
        var map = new OwnerWorldSnapshot("animal-smoke", 1, "animal-map", [], [], [], null, 0) { Animals = [animal, ewe, wild], PackedTerrain = new(256, 128, "terrain-kind-v1", Convert.ToBase64String(new byte[256 * 128])) };
        cameraZoom = 3;
        cameraCenterTiles = new(120, 60);
        RenderMap(map);
        var marker = mapObjectVisuals["animal:" + animal.Id];
        var sprite = marker.GetNode<TextureRect>("AnimalSprite");
        using var mounted = sprite.Texture.GetImage();
        using var plain = AnimalSprites.Sprite("horse", 0, false, false);
        if (HandcartPixelDigest(mounted) == HandcartPixelDigest(plain) || sprite.TextureFilter != CanvasItem.TextureFilterEnum.Nearest ||
            !marker.TooltipText.Contains("Moss", StringComparison.Ordinal) || !marker.TooltipText.Contains("Rowan", StringComparison.Ordinal) ||
            !marker.TooltipText.Contains("Linden", StringComparison.Ordinal))
            throw new InvalidOperationException("Mounted horse art and hover help must disclose its rider and named permissions.");
        var eweSprite = mapObjectVisuals["animal:" + ewe.Id].GetNode<TextureRect>("AnimalSprite");
        using var shornOnMap = eweSprite.Texture.GetImage();
        using var woollyOnMap = mapObjectVisuals["animal:" + wild.Id].GetNode<TextureRect>("AnimalSprite").Texture.GetImage();
        // The marker draws the drawing made for its size: 32 px at close zoom, 16 px at mid zoom.
        using var shornArt = AnimalSprites.Sprite("sheep", AgentSprites.South, false, false, shorn: true, size: (int)eweSprite.Size.X);
        if (HandcartPixelDigest(shornOnMap) != HandcartPixelDigest(shornArt) || HandcartPixelDigest(woollyOnMap) == HandcartPixelDigest(shornOnMap))
            throw new InvalidOperationException("The map must show the household ewe shorn and the wild sheep woolly.");
        selectedTile = new(120, 60);
        RenderTileInspection(map);
        if (!TileCardText().Contains("Moss", StringComparison.Ordinal))
            throw new InvalidOperationException("An animal must be inspectable at its actual tile.");
        var description = WorldEventText.Describe(new(1, 1, "animal_cared", "rider:smoke-horse"), map);
        if (!description.Contains("Moss", StringComparison.Ordinal) || description.Contains("smoke-horse", StringComparison.Ordinal))
            throw new InvalidOperationException("Animal events must show the animal's name.");
    }
}
