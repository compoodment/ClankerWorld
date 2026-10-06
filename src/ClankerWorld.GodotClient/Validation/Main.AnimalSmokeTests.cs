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
            sheet.BlitRect(rider, new Rect2I(0, 0, 32, 32), new Vector2I(facing * 32, 256));
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
        var map = new OwnerWorldSnapshot("animal-smoke", 1, "animal-map", [], [], [], null, 0) { Animals = [animal] };
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
        selectedTile = new(120, 60);
        RenderTileInspection(map);
        if (!TileCardText().Contains("Moss", StringComparison.Ordinal))
            throw new InvalidOperationException("An animal must be inspectable at its actual tile.");
        var description = WorldEventText.Describe(new(1, 1, "animal_cared", "rider:smoke-horse"), map);
        if (!description.Contains("Moss", StringComparison.Ordinal) || description.Contains("smoke-horse", StringComparison.Ordinal))
            throw new InvalidOperationException("Animal events must show the animal's name.");
    }
}
