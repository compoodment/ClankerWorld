using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>Real observation refreshes keep the map and visible tile-card art in agreement.</summary>
    private void VerifyPlayableNaturalArt(OwnerWorldSnapshot original)
    {
        var site = new OwnerWorldResource("approved-art-site", "food", new(1, 1), true,
            "available", 3, 3, NaturalObjectKind: "berry_bush");
        var cases = new (string Kind, string Name, string Item, bool Renewable, NatureSprite Available, NatureSprite Spent)[]
        {
            ("berry_bush", "Berry bush", "food", true, NatureSprite.BerryBush, NatureSprite.BerryBushPicked),
            ("wild_greens", "Wild greens", "food", true, NatureSprite.WildGreens, NatureSprite.WildGreensPicked),
            ("fiber_plant", "Fiber plant", "fiber", true, NatureSprite.FiberPlant, NatureSprite.FiberPlantHarvested),
            ("reeds", "Reeds", "fiber", true, NatureSprite.Reeds, NatureSprite.ReedsHarvested),
            ("medicinal_herb_patch", "Medicinal herb patch", "medicinal_herbs", true, NatureSprite.HerbPatch, NatureSprite.HerbPatchPicked),
            ("stone_outcrop", "Stone outcrop", "stone", false, NatureSprite.StoneOutcrop, NatureSprite.StoneOutcropDepleted),
            ("iron_outcrop", "Iron outcrop", "iron_ore", false, NatureSprite.IronOutcrop, NatureSprite.IronOutcropDepleted),
            ("gold_outcrop", "Gold outcrop", "gold_ore", false, NatureSprite.GoldOutcrop, NatureSprite.GoldOutcropDepleted),
            ("diamond_outcrop", "Diamond outcrop", "diamond", false, NatureSprite.DiamondOutcrop, NatureSprite.DiamondOutcropDepleted),
            ("clay_bank", "Clay bank", "clay", false, NatureSprite.ClayBank, NatureSprite.ClayBankDepleted),
            ("fallen_wood", "Fallen wood", "wood", false, NatureSprite.WoodPile, NatureSprite.Depleted),
        };
        var clean = original with { Resources = [], PlacedBuildings = [], Objects = [], GroundStocks = [], Fields = [] };
        void Show(OwnerWorldResource resource, string expectedName, NatureSprite expectedArt, string expectedStage)
        {
            var shown = clean with { Resources = [resource] };
            RenderMap(shown);
            selectedTile = new Vector2I(1, 1);
            RenderTileInspection(shown);
            var expected = NatureSprites.Sprite(expectedArt, 16).GetData();
            var icons = tileThings.FindChildren("*", nameof(TextureRect), recursive: true, owned: false).OfType<TextureRect>();
            if (terrainLayer.NaturalObjectNameAt(1, 1) != expectedName ||
                terrainLayer.NaturalObjectStageAt(1, 1) != expectedStage ||
                !mapObjectVisuals.TryGetValue("resource:" + resource.Id, out var marker) ||
                !marker.TooltipText.Contains(expectedName, StringComparison.Ordinal) ||
                !TileCardText().Contains(expectedName, StringComparison.Ordinal) ||
                !icons.Any(icon => icon.Texture is { } texture && texture.GetImage().GetData().SequenceEqual(expected)))
                throw new InvalidOperationException($"The map and visible tile card must show {expectedName} as {expectedArt} after an observation refresh.");
        }

        try
        {
            foreach (var entry in cases)
            {
                var resource = site with { Kind = entry.Item, NaturalObjectKind = entry.Kind, IsRenewable = entry.Renewable };
                Show(resource, entry.Name, entry.Available, "available");
                Show(resource with { Quantity = 0, State = "depleted" }, entry.Name, entry.Spent,
                    entry.Renewable ? "regrowing" : "depleted");
                // The quantity and state each independently establish that a patch is spent.
                if (entry.Kind == "medicinal_herb_patch")
                {
                    Show(resource with { Quantity = 0 }, entry.Name, entry.Spent, "regrowing");
                    Show(resource with { State = "depleted" }, entry.Name, entry.Spent, "regrowing");
                    Show(resource, entry.Name, entry.Available, "available");
                }
            }
            var kindOnly = site with { Kind = "medicinal_herbs", NaturalObjectKind = null };
            foreach (var spent in new[] { false, true })
            {
                var observed = kindOnly with { Quantity = spent ? 0 : 3 };
                RenderMap(clean with { Resources = [observed] });
                var expected = NatureSprites.Sprite(spent ? NatureSprite.HerbPatchPicked : NatureSprite.HerbPatch, 16).GetData();
                if (terrainLayer.CampResourceSpriteCount != 1 || ResourceSpriteImage(observed) is not { } image ||
                    !image.GetData().SequenceEqual(expected))
                    throw new InvalidOperationException("A kind-only herb site must use the same approved available/picked art on the map and card.");
            }
            if (ResourceSpriteImage(site with { NaturalObjectKind = "unknown_site" }) is not null ||
                ResourceSpriteImage(site with { NaturalObjectKind = null, Kind = "unknown_item" }) is not null)
                throw new InvalidOperationException("Unknown sites must retain their ordinary fallback instead of borrowing known art.");
        }
        finally
        {
            ClearTileSelection();
            RenderMap(original);
        }
    }
}
