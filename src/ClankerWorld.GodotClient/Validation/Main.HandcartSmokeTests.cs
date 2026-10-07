using ClankerWorld.GodotClient.UI;
using Godot;
using System.Security.Cryptography;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyHandcartPresentationAsync()
    {
        var window = GetWindow();
        var previousWindowSize = window.Size;
        var previousRenderSize = window.ContentScaleSize;
        var previousScaleMode = window.ContentScaleMode;
        var previousScaleAspect = window.ContentScaleAspect;
        var previousFactor = uiLayer.Factor;
        var previousMap = renderedMapSnapshot;
        var previousZoom = cameraZoom;
        var previousCenter = cameraCenterTiles;
        var previousRefreshing = isRefreshing;
        var previousTile = selectedTile;
        var previousSelection = selectedInhabitantId;
        var previousCard = agentCardSnapshot;
        var previousProfileRequested = agentProfileRequested;
        var previousPanels = new[] { selectedTilePanel, selectedInhabitantCard, agentProfilePanel }
            .Select(panel => (Panel: panel, panel.Visible)).ToArray();
        try
        {
            isRefreshing = true;
            VerifyHandcartApprovedPixels();
            VerifyHandcartInspection();
            window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            window.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            window.Size = new(1280, 720);
            window.ContentScaleSize = new(1280, 720);
            for (var frame = 0; frame < 4; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (GetViewport().GetVisibleRect().Size != new Vector2(1280, 720))
                throw new InvalidOperationException("Handcart zoom checks must run in a real 720p viewport.");
            VerifyHandcartMotionAndZoom();
            VerifyBoatPresentation();
            VerifyAnimalPresentation();
        }
        finally
        {
            window.Size = previousWindowSize;
            window.ContentScaleMode = previousScaleMode;
            window.ContentScaleAspect = previousScaleAspect;
            window.ContentScaleSize = previousRenderSize;
            for (var frame = 0; frame < 4; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            SetUiFactor(previousFactor);
            if (previousMap is not null)
            {
                RenderMap(previousMap);
                cameraZoom = previousZoom;
                cameraCenterTiles = previousCenter;
                RenderMap(previousMap);
            }
            else
            {
                RenderMap(new("cart-smoke-cleanup", 0, "no-map", [], [], [], null, 0));
                renderedMapSnapshot = null;
                cameraZoom = previousZoom;
                cameraCenterTiles = previousCenter;
            }
            selectedTile = previousTile;
            selectedInhabitantId = previousSelection;
            agentProfileRequested = previousProfileRequested;
            if (previousMap is not null) RenderTileInspection(previousMap);
            if (previousCard is not null) RenderSelectedInhabitantCard(previousCard);
            agentCardSnapshot = previousCard;
            foreach (var (panel, visible) in previousPanels) panel.Visible = visible;
            isRefreshing = previousRefreshing;
        }
    }

    // SHA-256 of RGBA8 from the independently rendered, approved #629 proposal.
    // Keep the reference constants independent of the runtime HandcartSprites API.
    private static readonly (int Facing, bool Loaded, bool Pulled, string Digest)[] ApprovedHandcartPixels =
    [
        (6, false, false, "16ABEFF7EC2F964B5CA5AC168160316B0DB9B35BD76FF96C28070778F923936A"),
        (4, false, false, "FE2969175E63FE1BF078F6BEC3624B8F3B823B43FD843C62DD7888740A1F27F9"),
        (5, false, false, "8D4172F97F7C60518B87CBA7118FBA03A25C04F9A95CE3AF66975171BB8048CC"),
        (3, false, false, "297E49ADD47573717070AC1DF7489EFBFA5575A37D7A4FC70FCC185F360E7AC2"),
        (0, false, false, "920458F02E399AB70F82FC35C46A6199481C7379C6D67BE84ECCF591CC74749C"),
        (7, false, false, "AAC994386A84EA1023804913BFBCE3DDFD3414E64D26FD751186FF393B2706AF"),
        (1, false, false, "C15A2BD3EAB73BE8C3740858B7E78653A77E1715FC707BFCD77B9368BD839694"),
        (2, false, false, "28F87E3689B7072D241BF3990D53E13C7A72CE852031C7C6730C2BC9A43560D3"),
        (6, true, false, "F0A220DB7C90D2BB5D65EFA3D590B6EB2B7833CDD2ADA21435C43243C906333B"),
        (4, true, false, "CFC066AA4A5A1892F00BCD870C1876A992D93573B875D6DB22FD797E8A7F4195"),
        (5, true, false, "366FC711422886AE2A4F53EAEC5C70E4D557B78CC7BC274E40A1C44C2C440D00"),
        (3, true, false, "A8AD270BC7A19F3AE31853CC1E7C1955EF59F556D1B46D8E2ED9FDF02A98E0D8"),
        (0, true, false, "AE5AD19EB26848180DB457DFB4B321E025278D90F162A6B60DC52208B65CAAD0"),
        (7, true, false, "D0A8FA94704863F3ABD8946207332A4511B53A33DEFB8CCA899B5BD0B96AA28C"),
        (1, true, false, "EDCE88B48157232C5B707769AAE8337D91194E469EB76D9ECA2CB55429B07091"),
        (2, true, false, "6C0E5CB5FD790537F1BA05B41C240DEAFDE3C3E22C3A97669181B1A86ABF7811"),
        (6, true, true, "BA48B7A390E6744053057422BE11F86330ACB6DC2BCDD954F81969BA28AA9E68"),
        (7, true, true, "A0769AC51FE1C25A489860FEB86CE8B49B02A5D7249E9670C5A84C84A2568D89"),
    ];

    private static string HandcartPixelDigest(Image image) => Convert.ToHexString(SHA256.HashData(image.GetData()));

    private static string ApprovedHandcartDigest(int facing, bool loaded, bool pulled) =>
        ApprovedHandcartPixels.Single(reference => reference.Facing == facing && reference.Loaded == loaded &&
            reference.Pulled == (loaded && pulled && facing is 6 or 7)).Digest;

    private static void VerifyHandcartApprovedPixels()
    {
        foreach (var (facing, loaded, pulled, digest) in ApprovedHandcartPixels)
        {
            using var image = HandcartSprites.Sprite(facing, loaded, pulled);
            using var textureImage = HandcartSprites.Texture(facing, loaded, pulled).GetImage();
            if (image.GetWidth() != 32 || image.GetHeight() != 32 || HandcartPixelDigest(image) != digest ||
                textureImage.GetWidth() != 32 || textureImage.GetHeight() != 32 || HandcartPixelDigest(textureImage) != digest)
                throw new InvalidOperationException($"Native handcart art must match approved pixels: facing {facing}, loaded {loaded}, pulled {pulled}.");
        }
        // Only the two loaded eastward poses were approved as separate pulled art.
        for (var facing = 0; facing < 8; facing++)
        {
            using var emptyPulled = HandcartSprites.Sprite(facing, loaded: false, pulled: true);
            using var loadedPulled = HandcartSprites.Sprite(facing, loaded: true, pulled: true);
            if (HandcartPixelDigest(emptyPulled) != ApprovedHandcartDigest(facing, false, false) ||
                HandcartPixelDigest(loadedPulled) != ApprovedHandcartDigest(facing, true, true))
                throw new InvalidOperationException("Unsupported pulled poses must use the approved parked heading.");
        }
    }

    private void VerifyHandcartMotionAndZoom()
    {
        var at = new OwnerWorldPosition(120, 60);
        var actor = new OwnerWorldInhabitant("cart-owner", "Rowan", "active", at, 10_000, [], [],
            new("idle", null, null, [], ""), new(at, [], []), false);
        var cart = new OwnerWorldHandcart("moving-cart", actor.Id, actor.DisplayName, at, 32, 83,
            actor.Id, actor.DisplayName, [new("wood", 16)]);
        var map = new OwnerWorldSnapshot("cart-motion-smoke", 1, "cart-motion-map", [], [], [], null, 0)
        {
            PackedTerrain = new(256, 128, "terrain-kind-v1", Convert.ToBase64String(new byte[256 * 128])),
            Inhabitants = [actor],
            Handcarts = [cart],
        };
        RenderMap(map);
        cameraZoom = maximumCameraZoom;
        RenderMap(map);
        if (currentTileSize < 40 || currentTileSize >= 64)
            throw new InvalidOperationException($"A generated map at 720p must reach the approved 32px handcart view below the old 64px threshold: {currentTileSize}px tiles.");
        var firstMarker = mapObjectVisuals["handcart:" + cart.Id];

        TextureRect Expect(int facing, bool loaded, bool pulled, string phase, bool sameMarker = true)
        {
            var marker = mapObjectVisuals["handcart:" + cart.Id];
            var sprite = marker.GetNode<TextureRect>("HandcartSprite");
            using var image = sprite.Texture.GetImage();
            if (image.GetWidth() != 32 || image.GetHeight() != 32 ||
                HandcartPixelDigest(image) != ApprovedHandcartDigest(facing, loaded, pulled) ||
                sprite.TextureFilter != CanvasItem.TextureFilterEnum.Nearest || sprite.Size != new Vector2(32, 32) ||
                !new Rect2(Vector2.Zero, marker.Size).Encloses(new Rect2(sprite.Position, sprite.Size)) ||
                (sameMarker && !ReferenceEquals(marker, firstMarker)))
                throw new InvalidOperationException($"The actual handcart marker must show the approved current pose, unscaled and unclipped {phase}.");
            var expectedY = map.Handcarts[0].Position.Y * (currentTileSize + TileGap) + 4;
            var canonicalX = map.Handcarts[0].Position.X * (currentTileSize + TileGap) + 4;
            var dx = marker.Position.X - canonicalX;
            var width = MapDimensions(map).Width * (currentTileSize + TileGap);
            if (Math.Abs(marker.Position.Y - expectedY) > 0.01f ||
                Math.Abs(map.WrapsEastWest ? dx - MathF.Round(dx / width) * width : dx) > 0.01f ||
                mapObjectVisuals.Keys.Count(id => id.StartsWith("handcart:", StringComparison.Ordinal)) != 1)
                throw new InvalidOperationException("A cart has one marker on its authoritative tile, including across the world seam.");
            return sprite;
        }

        void Move(int x, int y, bool visiblePuller = true)
        {
            at = new(x, y);
            cart = cart with { Position = at };
            map = map with
            {
                WorldTick = map.WorldTick + 1,
                Handcarts = [cart],
                Inhabitants = visiblePuller ? [actor with { Position = at }] : []
            };
            RenderMap(map);
        }

        Expect(0, true, true, "on its first observation");
        Move(121, 60);
        Expect(6, true, true, "on the first eastward observation");
        Move(121, 59);
        Expect(4, true, true, "on the first northward observation");
        Move(122, 60);
        Expect(7, true, true, "on the first southeastward observation");
        cart = cart with { PullerId = null, PullerName = null };
        Move(122, 60);
        Expect(7, true, false, "when detached without moving");
        cart = cart with { ConditionPercent = 0 };
        Move(122, 60);
        if (Expect(7, true, false, "when broken with cargo").Modulate != new Color("A89279"))
            throw new InvalidOperationException("A broken cart must retain its pose and cargo with the existing condition tint.");
        cart = cart with { Cargo = [new("wood", 0)], ConditionPercent = 83, PullerId = actor.Id, PullerName = actor.DisplayName };
        Move(123, 60);
        if (Expect(6, false, true, "when empty but attached").Modulate != Colors.White)
            throw new InvalidOperationException("A repaired empty cart must remove the broken tint and show no cargo.");
        cart = cart with { Cargo = [new("wood", 16)] };
        Move(124, 60, visiblePuller: false);
        Expect(6, true, true, "with an authoritative puller absent from visible agents");

        cameraZoom = 1;
        RenderMap(map);
        var baseTile = currentTileSize;
        cameraZoom = 39f / baseTile;
        RenderMap(map);
        var medium = mapObjectVisuals["handcart:" + cart.Id].GetNode<TextureRect>("HandcartSprite");
        using (var image = medium.Texture.GetImage())
        using (var reference = HandcartSprites.Sprite(6, true, true, 16))
        using (var icon = ItemIcons.Texture("handcart", 16).GetImage())
        {
            if (currentTileSize != 39 || image.GetWidth() != 16 || medium.Size != new Vector2(16, 16) ||
                !image.GetData().AsSpan().SequenceEqual(reference.GetData()) || image.GetData().AsSpan().SequenceEqual(icon.GetData()))
                throw new InvalidOperationException("At an actual 39px tile, the cart must use its approved 16px vehicle drawing, not the item icon.");
        }
        cameraZoom = 40f / baseTile;
        RenderMap(map);
        if (currentTileSize != 40) throw new InvalidOperationException("The real camera must reach the 40px cart-art boundary.");
        Expect(6, true, true, "at the exact 40px tile boundary");

        map = map with { WrapsEastWest = true, WorldTick = map.WorldTick + 1 };
        RenderMap(map);
        Expect(0, true, true, "after wrap behavior changes");
        Move(255, 60);
        Expect(0, true, true, "after a distant relocation");
        Move(0, 60);
        Expect(6, true, true, "moving east across the seam");
        Move(255, 60);
        Expect(2, true, true, "moving west across the seam");

        void ResetMap(Func<OwnerWorldSnapshot, OwnerWorldSnapshot> replace, string phase)
        {
            // Establish an east-facing pose before every independently observable reset.
            Move(0, 60);
            Expect(6, true, true, "before " + phase);
            map = replace(map) with { Handcarts = [cart], Inhabitants = [] };
            RenderMap(map);
            cameraZoom = 40f / baseTile;
            RenderMap(map);
            Expect(0, true, true, phase);
            Move(255, 60);
        }
        ResetMap(current => current with { WorldId = "cart-motion-next-world" }, "after changing worlds");
        ResetMap(current => current with { MapManifestDigest = "cart-next-manifest" }, "after changing the map manifest");
        ResetMap(current => current with { PackedTerrain = new(256, 129, "terrain-kind-v1", Convert.ToBase64String(new byte[256 * 129])) },
            "after changing map dimensions");
        var emptyLayer = Convert.ToBase64String(new byte[256 * 129]);
        ResetMap(current => current with
        {
            MapLayersDigest = "cart-next-layers",
            PackedMapLayers = new(256, 129, "map-layers-v2", emptyLayer, emptyLayer, emptyLayer, emptyLayer, emptyLayer)
        },
            "after changing map layers");
        ResetMap(current => current with { WorldTick = 0 }, "after an observed rewind");
        Move(0, 60);
        RenderMap(map with { Handcarts = [] });
        if (mapObjectVisuals.ContainsKey("handcart:" + cart.Id))
            throw new InvalidOperationException("Removing a cart must remove its marker and motion history.");
        RenderMap(map);
        Expect(0, true, true, "after removal and reappearance", sameMarker: false);
        Move(1, 60);
        RenderMap(map with { PackedTerrain = null, Tiles = [], Handcarts = [] });
        RenderMap(map);
        Expect(0, true, true, "after losing and restoring the map", sameMarker: false);
    }

    private void VerifyHandcartInspection()
    {
        var position = new OwnerWorldPosition(1, 1);
        var snapshot = new OwnerWorldSnapshot("cart-ui-smoke", 1, "cart-ui-map",
            Enumerable.Range(0, 16).Select(index => new OwnerWorldTile(index % 4, index / 4, "meadow")).ToArray(), [], [], null, 0)
        {
            Inhabitants = [new("cart-owner", "Rowan", "active", position, 10_000, [], [],
                new("idle", null, null, [], ""), new(position, [], []), false)],
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
