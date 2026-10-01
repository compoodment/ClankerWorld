using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void VerifyHandcartInspection()
    {
        var position = new OwnerWorldPosition(1, 1);
        var snapshot = new OwnerWorldSnapshot("cart-ui-smoke", 1, "cart-ui-map",
            Enumerable.Range(0, 16).Select(index => new OwnerWorldTile(index % 4, index / 4, "meadow")).ToArray(), [], [], null, 0)
        {
            Inhabitants = [new("cart-owner", "Rowan", "active", position, 10_000, [], [],
                new("idle", null, null, [], ""), new(position, [], []))],
            Handcarts = [new("cart", "cart-owner", "Rowan", position, 32, 83,
                "cart-owner", "Rowan", [new("wood", 16)])],
        };
        // Exercise the client wire contract before rendering the physical object.
        snapshot = System.Text.Json.JsonSerializer.Deserialize<OwnerWorldSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(snapshot, CompatibilitySmokeJsonOptions), CompatibilitySmokeJsonOptions)!;
        RenderMap(snapshot);
        if (!mapObjectVisuals.TryGetValue("handcart:cart", out var marker) ||
            marker.GetNodeOrNull<TextureRect>("HandcartSprite")?.Texture is null ||
            !marker.TooltipText.Contains("Cargo: 16/32", StringComparison.Ordinal) ||
            !marker.TooltipText.Contains("Owner: Rowan", StringComparison.Ordinal))
            throw new InvalidOperationException("A handcart must have a visible map sprite and actual ownership/cargo hover help.");
        selectedTile = new(1, 1);
        RenderTileInspection(snapshot);
        selectedInhabitantId = "cart-owner";
        RenderSelectedInhabitantCard(snapshot);
        if (!selectedTileText.Text.Contains("Pulled by Rowan", StringComparison.Ordinal) ||
            !selectedTileText.Text.Contains("Condition: 83%", StringComparison.Ordinal) ||
            !inhabitantDetails.Text.Contains("Cargo: 16/32", StringComparison.Ordinal))
            throw new InvalidOperationException("Tile and agent inspection must show a cart's pull state, condition and separate cargo.");
        snapshot = snapshot with { Handcarts = [snapshot.Handcarts[0] with { PullerId = null, PullerName = null, ConditionPercent = 0 }] };
        RenderMap(snapshot);
        RenderTileInspection(snapshot);
        if (!selectedTileText.Text.Contains("Broken", StringComparison.Ordinal) ||
            !selectedTileText.Text.Contains("unload here or repair", StringComparison.Ordinal) ||
            !selectedTileText.Text.Contains("Cargo: 16/32", StringComparison.Ordinal))
            throw new InvalidOperationException("A broken parked cart must keep displaying its cargo and useful recovery choices.");
        RenderMap(snapshot with { Handcarts = [] });
        if (mapObjectVisuals.ContainsKey("handcart:cart"))
            throw new InvalidOperationException("A cart absent from the accepted snapshot must lose its map marker.");
        ClearTileSelection();
        selectedInhabitantId = null;
        selectedInhabitantCard.Hide();
        agentProfilePanel.Hide();
    }
}
