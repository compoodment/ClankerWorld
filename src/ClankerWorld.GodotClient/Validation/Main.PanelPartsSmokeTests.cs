using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private static readonly PixelGlyph[] PanelGlyphs =
    [
        PixelGlyph.Hammer, PixelGlyph.Heart, PixelGlyph.Check, PixelGlyph.Warning, PixelGlyph.Basket, PixelGlyph.Grave,
        PixelGlyph.Flag, PixelGlyph.Mouse, PixelGlyph.Road, PixelGlyph.Bridge, PixelGlyph.Thought, PixelGlyph.Bulb,
    ];

    /// <summary>
    /// The shared panel pictures: icons stay inside the live area, portraits and
    /// tile swatches are framed squares, keycaps centre their letter, and compact
    /// list rows are shorter than ordinary ones.
    /// </summary>
    private void VerifyPanelParts()
    {
        foreach (var glyph in PanelGlyphs)
        {
            var image = PixelIcons.Texture(glyph, Colors.Black, Colors.Red, 1).GetImage();
            for (var i = 0; i < PixelIcons.Grid; i++)
                foreach (var (x, y) in new[] { (i, 0), (i, PixelIcons.Grid - 1), (0, i), (PixelIcons.Grid - 1, i) })
                    if (image.GetPixel(x, y).A > 0)
                        throw new InvalidOperationException($"The {glyph} icon must leave its outer pixel empty, like the other icons.");
        }

        if (renderedMapSnapshot is not { } shown || terrainMap is null)
            throw new InvalidOperationException("The panel picture checks need a world on screen.");
        var person = PanelSmokeAgent("panel-smoke-mira", "Mira", new OwnerWorldPosition(0, 0));
        var snapshot = shown with { Inhabitants = [person] };
        RenderMap(snapshot);
        try
        {
            var map = terrainMap!;
            var portrait = AgentPortrait(person, living: true).GetImage();
            var faded = AgentPortrait(person, living: false).GetImage();
            if (portrait.GetSize() != new Vector2I(20, 20) || portrait.GetPixel(0, 0) != UiTheme.Current.WoodEdge ||
                portrait.GetPixel(1, 1) == faded.GetPixel(1, 1))
                throw new InvalidOperationException("An agent portrait must be a framed 20 px square that fades once the agent has died.");
            var swatch = TileSwatch(map, 0, 0, 10).GetImage();
            if (swatch.GetSize() != new Vector2I(12, 12) || swatch.GetPixel(0, 0) != UiTheme.Current.WoodEdge || swatch.GetPixel(5, 5).A < 1)
                throw new InvalidOperationException("A tile swatch must show the ground in a one-pixel frame.");
            if (snapshot.Resources.FirstOrDefault(resource => resource.TreeKind is not null) is { } tree && ResourceSprite(tree) is null)
                throw new InvalidOperationException("Trees must have a map sprite for panels to show.");
        }
        finally
        {
            RenderMap(shown);
        }

        // The key's letter sits centred on the face above its shadow edge.
        var key = Keycap("H");
        var style = (StyleBoxFlat)((PanelContainer)key).GetThemeStylebox("panel");
        if (style.BorderWidthBottom - style.BorderWidthTop != style.ContentMarginBottom - style.ContentMarginTop)
            throw new InvalidOperationException("A keycap's letter must sit in the middle of the key face, not its shadow.");
        key.Free();
        var mouse = Keycap("Wheel");
        if (mouse.GetChildCount() != 2 || mouse.GetChild(0) is not TextureRect)
            throw new InvalidOperationException("Mouse actions must show the mouse icon instead of a keycap.");
        mouse.Free();

        var list = new SlotList();
        AddChild(list);
        float RowHeight()
        {
            var cards = list.GetChild(0);
            return ((Control)cards.GetChild(cards.GetChildCount() - 1)).GetCombinedMinimumSize().Y;
        }
        var icon = AgentPortrait(person, living: true);
        list.AddItem("Mira", "Gathering clay", icon);
        var ordinaryHeight = RowHeight();
        list.Clear();
        list.Compact = true;
        list.AddItem("Mira", "Gathering clay", icon);
        var compactHeight = RowHeight();
        RemoveChild(list);
        list.QueueFree();
        if (compactHeight >= ordinaryHeight)
            throw new InvalidOperationException($"Compact list rows must be shorter than ordinary ones: {compactHeight} vs {ordinaryHeight}.");
    }

    /// <summary>A living adult standing on a tile, for checks that need an agent in the world.</summary>
    private static OwnerWorldInhabitant PanelSmokeAgent(string id, string name, OwnerWorldPosition position) =>
        new(id, name, "active", position, 8_000, [], [], new OwnerWorldRoute("idle", null, null, [], string.Empty),
            new OwnerWorldSpatialKnowledge(position, [position], [position]), false)
        {
            Survival = new OwnerWorldSurvival(8_200, 300, true, false, 7_400, null),
        };
}
