using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    // Facing is presentation inferred from accepted positions, never saved world state.
    private readonly Dictionary<string, (OwnerWorldPosition Position, int Facing)> handcartFacings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (OwnerWorldPosition Position, int Facing)> animalFacings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (OwnerWorldPosition Position, int Facing)> boatFacings = new(StringComparer.Ordinal);

    private void ResetDisplayedWorldContext()
    {
        knownEvents.Clear();
        eventsWorldId = null;
        lastSeenEventId = long.MinValue;
        newEventsAfter = long.MaxValue;
        unreadEvents = 0;
        renderedEventLog = null;
        conversationReadWorldId = null;
        locallyReadConversationTurns.Clear();
        openConversationId = null;
        openConversationAgentId = null;
        conversationPanel.Hide();
        familyTreePanel.Hide();
        memoriesPanel.Hide();
        thoughtsPanel.Hide();
        selectedInhabitantId = null;
        renamingAgentId = null;
        refusedAgentRename.Forget();
        renameRow.Hide();
        ClearTileSelection();
        ClearBuildingSelection();
        CancelBuildingRemoval();
        CancelAutosaveSettingsRead();
        CancelManualSaveListRead();
        worldListRequest.Cancel();
        manualSaveOverlay.Hide();
        pendingOverwriteSaveId = null;
        pendingDeletion = null;
        choosingFirstTownSite = false;
        movingFounderId = null;
        placingAddedAgent = false;
        founderSetupPanel.Hide();
        providerConfiguration = null;
        observedRoutineHelperContext = null;
        _ = routineHelperModelPicker.BeginLoading(string.Empty);
        cognitionModelContext = null;
        cognitionModelLookup = null;
        founderKeyEdits++;
        cognitionKeyEdits++;
        ClearFounderModelSetupCheck();
        ClearCognitionModelSetupCheck();
        menuPauseConfirmed = false;
        menuPausedWorld = false;
        renderedMapSnapshot = null;
        terrainMap = null;
        terrainWorldId = null;
        cameraWorldId = null;
        usagePauseWorldId = null;
        lastLifePaceWorldId = null;
        renderedTownList = null;
        foreach (var marker in inhabitantVisuals.Values) marker.QueueFree();
        inhabitantVisuals.Clear();
        inhabitantCanonicalXs.Clear();
        foreach (var visual in mapObjectVisuals.Values) visual.QueueFree();
        mapObjectVisuals.Clear();
        mapObjectCanonicalXs.Clear();
        handcartFacings.Clear();
        animalFacings.Clear();
        boatFacings.Clear();
    }

    private void Render(OwnerWorldSnapshot snapshot, IReadOnlyList<OwnerWorldEvent> appendedEvents)
    {
        var (mapWidth, mapHeight) = MapDimensions(snapshot);
        authoringX.MaxValue = Math.Max(0, mapWidth - 1);
        authoringY.MaxValue = Math.Max(0, mapHeight - 1);
        if (usagePauseWorldId != snapshot.WorldId)
        {
            usagePauseWorldId = snapshot.WorldId;
            wasObservedPaused = false;
        }
        var isPaused = snapshot.Authoring?.IsPaused == true;
        var checkUsagePause = isPaused && !wasObservedPaused;
        wasObservedPaused = isPaused;
        observedCalendarPace = snapshot.CalendarPace;
        RenderRoutineHelperSettings(snapshot);
        if (cameraWorldId is not null && cameraWorldId != snapshot.WorldId)
        {
            knownEvents.Clear();
            familyTreePanel.Hide();
            memoriesPanel.Hide();
            thoughtsPanel.Hide();
            ordersPanel.Hide();
            ClearTileSelection();
            ClearBuildingSelection();
        }
        foreach (var worldEvent in appendedEvents)
        {
            knownEvents[worldEvent.EventId] = worldEvent;
        }
        foreach (var expiredId in knownEvents.Keys.OrderByDescending(id => id).Skip(2048).ToArray())
        {
            knownEvents.Remove(expiredId);
        }

        RenderInhabitantList(snapshot);
        RenderMap(snapshot);
        RenderWorldHud(snapshot);
        if (checkUsagePause && registration is not null) _ = ObserveUsagePauseAsync();
        RenderFounderSetup(snapshot);
        RenderWorldInfo(snapshot);
        RenderSelectedInhabitantCard(snapshot);
        RenderBuildingCard(snapshot);
        if (familyTreePanel.Visible && selectedInhabitantId is { } center)
        {
            familyTreeView.SetPeople(snapshot.WorldId, snapshot.Inhabitants, center);
            UpdateFamilyTreeStatus();
        }
        RenderTownExtras(snapshot);
        RenderModLibrary(snapshot);
        RenderEventLog();
        RenderDeveloperTools(snapshot);
        RefreshControlAvailability();
    }

    private static bool HasMap(OwnerWorldSnapshot snapshot) =>
        snapshot.PackedTerrain is not null || snapshot.Tiles.Count > 0;

    private static (int Width, int Height) MapDimensions(OwnerWorldSnapshot snapshot) =>
        snapshot.PackedTerrain is { } packed ? (packed.Width, packed.Height) :
        snapshot.Tiles.Count == 0 ? (0, 0) :
        (snapshot.Tiles.Max(tile => tile.X) + 1, snapshot.Tiles.Max(tile => tile.Y) + 1);

    private static bool MapContains(OwnerWorldSnapshot snapshot, int x, int y)
    {
        var (width, height) = MapDimensions(snapshot);
        return x >= 0 && y >= 0 && x < width && y < height;
    }

    private void RenderMap(OwnerWorldSnapshot snapshot)
    {
        if (renderedMapSnapshot is not { } previous ||
            previous.WorldId != snapshot.WorldId || snapshot.WorldTick < previous.WorldTick ||
            (previous.Authoring?.CurrentMapManifestDigest ?? previous.MapManifestDigest) !=
                (snapshot.Authoring?.CurrentMapManifestDigest ?? snapshot.MapManifestDigest) ||
            previous.MapLayersDigest != snapshot.MapLayersDigest ||
            previous.WrapsEastWest != snapshot.WrapsEastWest || MapDimensions(previous) != MapDimensions(snapshot))
        {
            handcartFacings.Clear();
            animalFacings.Clear();
            boatFacings.Clear();
        }
        renderedMapSnapshot = snapshot;
        var objectIds = snapshot.Resources.Where(resource => resource.TreeKind is null)
            .Select(resource => "resource:" + resource.Id)
            .Concat(snapshot.Objects.Select(item => "object:" + item.Id))
            .Concat(snapshot.Handcarts.Select(item => "handcart:" + item.Id))
            .Concat(snapshot.Boats.Select(item => "boat:" + item.Id))
            .Concat(snapshot.Animals.Select(item => "animal:" + item.Id))
            .Concat(snapshot.PlacedBuildings.Select(item => "building:" + item.InstanceId)).ToHashSet(StringComparer.Ordinal);
        foreach (var id in mapObjectVisuals.Keys.Where(id => !objectIds.Contains(id)).ToArray())
        {
            mapObjectVisuals[id].QueueFree();
            mapObjectVisuals.Remove(id);
            mapObjectCanonicalXs.Remove(id);
        }
        foreach (var id in handcartFacings.Keys.Where(id => !objectIds.Contains("handcart:" + id)).ToArray())
            handcartFacings.Remove(id);
        foreach (var id in animalFacings.Keys.Where(id => !objectIds.Contains("animal:" + id)).ToArray())
            animalFacings.Remove(id);
        foreach (var id in boatFacings.Keys.Where(id => !objectIds.Contains("boat:" + id)).ToArray())
            boatFacings.Remove(id);

        if (!HasMap(snapshot))
        {
            handcartFacings.Clear();
            animalFacings.Clear();
            boatFacings.Clear();
            foreach (var visual in inhabitantVisuals.Values) visual.QueueFree();
            inhabitantVisuals.Clear();
            inhabitantCanonicalXs.Clear();
            terrainLayer.SetHoveredTile(null);
            UpdateTownSiteGuidance(null);
            return;
        }

        var manifest = snapshot.Authoring?.CurrentMapManifestDigest ?? snapshot.MapManifestDigest;
        if (terrainMap is null || !string.Equals(terrainWorldId, snapshot.WorldId, StringComparison.Ordinal) ||
            !string.Equals(terrainManifestDigest, manifest, StringComparison.Ordinal) ||
            !string.Equals(terrainLayersDigest, snapshot.MapLayersDigest, StringComparison.Ordinal) ||
            (!terrainMap.HasMapLayers && snapshot.PackedMapLayers is not null))
        {
            var (width, height) = MapDimensions(snapshot);
            terrainMap = snapshot.PackedTerrain is { } packed
                ? WorldTerrainMap.FromPacked(packed, snapshot.PackedMapLayers, snapshot.WrapsEastWest)
                : WorldTerrainMap.FromTiles(snapshot.Tiles, width, height, snapshot.PackedMapLayers, snapshot.WrapsEastWest);
            terrainWorldId = snapshot.WorldId;
            terrainManifestDigest = manifest;
            terrainLayersDigest = snapshot.MapLayersDigest;
            terrainLayer.SetWorld(terrainMap);
            worldOverview.SetWorld(terrainMap);
        }
        terrainLayer.SetSeason(snapshot.Authoring?.Season ?? snapshot.WorldSystems?.Season);
        terrainLayer.SetTrees(snapshot.Resources);
        terrainLayer.SetNaturalObjects(snapshot.Resources);
        terrainLayer.SetWeatherRegions(snapshot.WeatherRegionSize, snapshot.WeatherRegions);
        terrainLayer.SetRoads(snapshot.RoadTiles);
        terrainLayer.SetBridges(snapshot.Bridges, snapshot.Towns);
        terrainLayer.SetFields(snapshot.Fields);
        worldOverview.SetFields(snapshot.Fields);
        terrainLayer.SetMarkets(snapshot.Towns);
        terrainLayer.SetBuildings(snapshot.PlacedBuildings, snapshot.Objects, snapshot.Towns);
        nightLightsLayer.SetBuildings(BuildingLights(snapshot));
        nightLightsLayer.SetLanterns(StreetLanterns(snapshot), snapshot.WrapsEastWest);
        terrainLayer.SetConstructionSites(snapshot.ConstructionSites);
        nightLightsLayer.SetLanternSites(StreetLanternSites(snapshot));
        worldOverview.SetRoads([.. snapshot.RoadTiles, .. snapshot.Bridges.SelectMany(bridge => bridge.Span)]);
        ApplyMapFilters(snapshot);
        var mapWidth = terrainMap.Width;
        var mapHeight = terrainMap.Height;
        worldOverview.WrapsEastWest = snapshot.WrapsEastWest;
        worldOverview.Backdrop = UiTheme.Current.Inset;
        worldOverview.AtlasEdge = UiTheme.Current.WoodEdge;
        worldOverview.SetMarkers(
            snapshot.Towns.Where(town => town.BorderTiles.Count > 0).Select(town => TownMarkerTile(town, mapWidth, snapshot.WrapsEastWest)),
            snapshot.Inhabitants.Where(person => !person.IsDraft && IsLiving(person))
                .Select(person => new Vector2(person.Position.X + 0.5f, person.Position.Y + 0.5f)));
        nightLayer.Darkness = NightLayer.FromBasisPoints(snapshot.DarknessBasisPoints);
        if (!string.Equals(cameraWorldId, snapshot.WorldId, StringComparison.Ordinal))
        {
            cameraWorldId = snapshot.WorldId;
            cameraZoom = 1;
            cameraCenterTiles = InitialCameraCenter(snapshot, terrainMap);
            nightLayer.Settle();
        }
        UpdateMapGeometry(snapshot);
        UpdateTownSiteGuidance(snapshot);

        foreach (var resource in snapshot.Resources)
        {
            if (resource.TreeKind is not null) continue;
            AddMapObjectVisual(
                "resource:" + resource.Id,
                resource.Position,
                // Natural sites are drawn as terrain sprites; their marker only
                // adds hover help and a caption, not a second symbol.
                WorldTerrainMap.NaturalObjectName(resource.NaturalObjectKind) is null &&
                    (resource.NaturalObjectKind is not null || NatureSprites.ForCampResource(resource.Kind) is null)
                    ? ResourceGlyph(resource.Kind, resource.NaturalObjectKind) : string.Empty,
                GameUiText.ResourceMapCaption(resource, currentTileSize),
                GameUiText.ResourceTooltip(resource));
        }

        foreach (var boat in snapshot.Boats)
        {
            var id = "boat:" + boat.Id;
            AddMapObjectVisual(id, boat.Position, string.Empty, string.Empty, GameUiText.BoatDescription(boat));
            var marker = mapObjectVisuals[id];
            var sprite = marker.GetNodeOrNull<TextureRect>("BoatSprite");
            if (sprite is null)
            {
                sprite = new TextureRect
                {
                    Name = "BoatSprite",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                };
                marker.AddChild(sprite);
            }
            var facing = ObserveBoatFacing(boat, mapWidth, snapshot.WrapsEastWest);
            sprite.Texture = BoatSprites.Texture(facing, boat.Status is "underway" or "returning");
            // The approved boat is 32 px. Smaller views scale that drawing until #914 supplies approved 16 px art.
            var size = currentTileSize >= 40 ? 32 : Math.Max(1, currentTileSize - 8);
            sprite.Size = new(size, size);
            sprite.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            sprite.Position = new(0, Math.Max(0, marker.Size.Y - size));
        }

        foreach (var animal in snapshot.Animals)
        {
            var id = "animal:" + animal.Id;
            AddMapObjectVisual(id, animal.Position, string.Empty, string.Empty, GameUiText.AnimalDescription(animal));
            var marker = mapObjectVisuals[id];
            var sprite = marker.GetNodeOrNull<TextureRect>("AnimalSprite");
            if (sprite is null)
            {
                sprite = new TextureRect
                {
                    Name = "AnimalSprite",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest
                };
                marker.AddChild(sprite);
            }
            var facing = AgentSprites.South;
            if (animalFacings.TryGetValue(animal.Id, out var previousAnimal))
            {
                facing = previousAnimal.Facing;
                var dx = animal.Position.X - previousAnimal.Position.X;
                if (snapshot.WrapsEastWest && mapWidth > 0) dx -= (int)Math.Round(dx / (double)mapWidth) * mapWidth;
                var dy = animal.Position.Y - previousAnimal.Position.Y;
                if (dx != 0 || dy != 0) facing = AgentSprites.FacingToward(dx, dy);
            }
            animalFacings[animal.Id] = (animal.Position, facing);
            var size = currentTileSize >= 40 ? 32 : 16;
            sprite.Texture = AnimalSprites.Texture(animal.Species, facing, animal.LifeStage == "young", animal.RiderId is not null,
                animal.Saddled, animal.LooksShorn, size);
            sprite.Size = new(size, size);
            sprite.Position = new(0, Math.Max(0, marker.Size.Y - size));
            sprite.Modulate = animal.LifeStage == "deceased" ? new Color("A89279") : Colors.White;
        }

        foreach (var cart in snapshot.Handcarts)
        {
            var id = "handcart:" + cart.Id;
            AddMapObjectVisual(id, cart.Position, string.Empty, string.Empty, GameUiText.HandcartDescription(cart));
            var marker = mapObjectVisuals[id];
            var sprite = marker.GetNodeOrNull<TextureRect>("HandcartSprite");
            if (sprite is null)
            {
                sprite = new TextureRect
                {
                    Name = "HandcartSprite",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest
                };
                marker.AddChild(sprite);
            }
            var facing = ObserveHandcartFacing(cart, mapWidth, snapshot.WrapsEastWest);
            // The approved vehicle is 32px; the marker has a 4px inset on each
            // side. Smaller views retain the item icon until 16px art is approved.
            var size = currentTileSize >= 40 ? 32 : 16;
            sprite.Texture = size == 32
                ? HandcartSprites.Texture(facing, cart.Cargo.Any(item => item.Quantity > 0), cart.PullerId is not null)
                : ItemIcons.Texture("handcart", 16);
            sprite.Size = new(size, size);
            sprite.Position = new(0, Math.Max(0, marker.Size.Y - size));
            sprite.Modulate = cart.ConditionPercent == 0 ? new Color("A89279") : Colors.White;
        }

        foreach (var mapObject in snapshot.Objects)
        {
            AddMapObjectVisual(
                "object:" + mapObject.Id,
                mapObject.Position,
                BuildingSprites.KindForObject(mapObject.Kind) is null ? ObjectGlyph(mapObject.Kind) : string.Empty,
                ObjectMarker(mapObject.Kind),
                Pretty(mapObject.Kind));
        }

        foreach (var building in snapshot.PlacedBuildings)
        {
            var name = building.DisplayName ?? "Building";
            var assignedTown = snapshot.Towns.FirstOrDefault(item => item.Id == building.TownId)?.Name;
            var household = snapshot.Stockpiles.FirstOrDefault(item => item.OwnerId == building.HouseholdId);
            var stored = building.StoredItems is { Count: > 0 }
                ? string.Join(" · ", building.StoredItems.Select(item => $"{GameUiText.ItemName(item.Kind)} {item.Quantity}"))
                : "none recorded";
            // The terrain or night-light layer draws the roof or fitting. Buildings show no name on the map;
            // the marker keeps the hover help that names them.
            AddMapObjectVisual("building:" + building.InstanceId, building.Position, string.Empty, string.Empty,
                $"{name}\nBuilt · {building.Width} × {building.Height} tiles" +
                (StreetLanternLight.IsLantern(building.Tags) && building.Entrance is { } road
                    ? $"\nRoad beside the post · ({road.X}, {road.Y})\nLights at dusk · no fuel" : "") +
                (assignedTown is null ? "\nNo Town assignment" : $"\nTown · {assignedTown}") +
                (household is null ? "" : $"\nHousehold · {household.Name}") +
                (building.StoredItems is null ? "" : $"\nStored here · {stored}") +
                (building.StorageCapacity is { } capacity ? $"\nStorage · {building.StoredQuantity} / {capacity}" : "") +
                (building.InvitedGuests is { Count: > 0 } guests ? $"\nStorm guests · {string.Join(", ", guests)}" : "") +
                (building.ExpansionState == "running"
                    ? building.Tags?.Contains("house", StringComparer.Ordinal) == true
                        ? "\nHouse expansion underway · more storage and resident places when finished"
                        : "\nExpanding storage"
                    : "") +
                (building.ExpansionFailure is { } failure ? $"\nExpansion stopped · {failure}" : ""),
                building.Width, building.Height);
        }

        foreach (var group in snapshot.Inhabitants
            .Where(inhabitant => !inhabitant.IsDraft && string.Equals(inhabitant.Lifecycle, "active", StringComparison.OrdinalIgnoreCase))
            .GroupBy(inhabitant => PositionKey(inhabitant.Position)))
        {
            var occupants = group.ToArray();
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(occupants.Length)));
            var rows = (int)Math.Ceiling((double)occupants.Length / columns);
            var cellWidth = (float)currentTileSize / columns;
            var cellHeight = (float)currentTileSize / rows;
            for (var index = 0; index < occupants.Length; index++)
            {
                var inhabitant = occupants[index];
                var stride = currentTileSize + TileGap;
                var inset = Math.Min(3f, Math.Min(cellWidth, cellHeight) / 8f);
                var visualLimit = Math.Min(78f, currentTileSize * 0.55f);
                var markerSize = new Vector2(Math.Min(cellWidth - 2 * inset, visualLimit),
                    Math.Min(cellHeight - 2 * inset, visualLimit));
                var offsetX = (index % columns) * cellWidth + (cellWidth - markerSize.X) / 2;
                var offsetY = (index / columns) * cellHeight + (cellHeight - markerSize.Y) / 2;
                var targetPosition = new Vector2(
                    inhabitant.Position.X * stride + offsetX,
                    inhabitant.Position.Y * stride + offsetY);
                if (!inhabitantVisuals.TryGetValue(inhabitant.Id, out var actorMarker))
                {
                    actorMarker = new AgentMarker { Position = targetPosition };
                    actorMarker.Activated += () =>
                    {
                        if (placingAddedAgent && founderSetupPanel.Visible)
                        {
                            var currentPosition = renderedMapSnapshot?.Inhabitants
                                .FirstOrDefault(item => item.Id == inhabitant.Id)?.Position;
                            if (currentPosition is { } position)
                                _ = PlaceAgentAtAsync(new Vector2I(position.X, position.Y));
                        }
                        else
                            SelectInhabitant(inhabitant.Id);
                    };
                    actorMarker.ConversationActivated += () => OpenAgentConversation(inhabitant.Id);
                    actorMarker.MouseEntered += RefreshTileHoverAtMouse;
                    actorMarker.MouseExited += RefreshTileHoverAtMouse;
                    entityLayer.AddChild(actorMarker);
                    inhabitantVisuals.Add(inhabitant.Id, actorMarker);
                }
                actorMarker.Caption = GameUiText.ActorMapLabel(inhabitant.DisplayName);
                actorMarker.Variant = AgentSprites.VariantFor(inhabitant.Id);
                actorMarker.Visible = !snapshot.Animals.Any(animal => animal.RiderId == inhabitant.Id);
                actorMarker.Stage = AgentSprites.StageIndex(
                    inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail);
                // Facing and frame only present what the observation says:
                // the tile the host reports and what the agent is doing there.
                actorMarker.ObserveTile(snapshot.WorldId, new Vector2I(inhabitant.Position.X, inhabitant.Position.Y),
                    mapWidth, snapshot.WrapsEastWest);
                actorMarker.Activity = AgentMarker.ActivityFor(inhabitant);
                var actorTooltip = $"{inhabitant.DisplayName} · {Pretty(inhabitant.Lifecycle)} · " +
                    (inhabitant.PublicIntention?.Summary ?? "taking in the world");
                var conversation = LatestConversationFor(snapshot, inhabitant.Id);
                actorMarker.ConversationBadgeVisible = conversation is not null;
                actorMarker.ConversationUnread = conversation is not null &&
                    ConversationUnreadCount(snapshot.WorldId, conversation, inhabitant.Id) > 0;
                if (conversation is not null)
                    actorTooltip += "\n" + ConversationTooltipSummary(inhabitant.Id, conversation);
                if (actorMarker.TooltipText != actorTooltip) actorMarker.TooltipText = actorTooltip;
                actorMarker.Selected = string.Equals(inhabitant.Id, selectedInhabitantId, StringComparison.Ordinal);
                inhabitantCanonicalXs[inhabitant.Id] = targetPosition.X;
                actorMarker.Position = new Vector2(
                    WrappedMarkerX(targetPosition.X, mapWidth, stride, snapshot.WrapsEastWest),
                    targetPosition.Y);
                actorMarker.Size = markerSize;

            }
        }

        var visibleInhabitantIds = snapshot.Inhabitants
            .Where(inhabitant => !inhabitant.IsDraft && string.Equals(inhabitant.Lifecycle, "active", StringComparison.OrdinalIgnoreCase))
            .Select(inhabitant => inhabitant.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var removedId in inhabitantVisuals.Keys.Where(id => !visibleInhabitantIds.Contains(id)).ToArray())
        {
            inhabitantVisuals[removedId].QueueFree();
            inhabitantVisuals.Remove(removedId);
            inhabitantCanonicalXs.Remove(removedId);
        }

        RenderTileInspection(snapshot);
        RenderAgentConversationReader(snapshot);
        PositionSelectedInhabitantCard(snapshot);
        PositionBuildingQuickCard(snapshot);
        RefreshTileHoverAtMouse();
    }

    private int ObserveHandcartFacing(OwnerWorldHandcart cart, int mapWidth, bool wrapsEastWest)
    {
        var facing = AgentSprites.South;
        if (handcartFacings.TryGetValue(cart.Id, out var previous))
        {
            facing = previous.Facing;
            var dx = cart.Position.X - previous.Position.X;
            var dy = cart.Position.Y - previous.Position.Y;
            if (wrapsEastWest && mapWidth > 0)
                dx -= (int)Math.Round(dx / (double)mapWidth) * mapWidth;
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > AgentMarker.MaxStepTiles)
                facing = AgentSprites.South;
            else if (dx != 0 || dy != 0)
                facing = AgentSprites.FacingToward(dx, dy);
        }
        handcartFacings[cart.Id] = (cart.Position, facing);
        return facing;
    }

    private int ObserveBoatFacing(OwnerWorldBoat boat, int mapWidth, bool wrapsEastWest)
    {
        var facing = AgentSprites.South;
        if (boatFacings.TryGetValue(boat.Id, out var previous))
        {
            facing = previous.Facing;
            var dx = boat.Position.X - previous.Position.X;
            var dy = boat.Position.Y - previous.Position.Y;
            if (wrapsEastWest && mapWidth > 0) dx -= (int)Math.Round(dx / (double)mapWidth) * mapWidth;
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > AgentMarker.MaxStepTiles) facing = AgentSprites.South;
            else if (dx != 0 || dy != 0) facing = AgentSprites.FacingToward(dx, dy);
        }
        boatFacings[boat.Id] = (boat.Position, facing);
        return facing;
    }

    // Clipped captions degrade into unreadable fragments such as "rehou", so a
    // marker shows its name only when the whole caption fits; the glyph and
    // tooltip still identify it when zoomed out.
    private static bool MapObjectLabelFits(Label visual, string label)
    {
        var font = visual.GetThemeFont("font");
        var fontSize = visual.GetThemeFontSize("font_size");
        return visual.Size.Y >= font.GetHeight(fontSize) * 2 &&
            font.GetStringSize(label, HorizontalAlignment.Left, -1, fontSize).X <= visual.Size.X;
    }

    /// <summary>
    /// Opens a world on its settlement rather than the geometric map center,
    /// which on generated maps is often open water: the first Town, then the
    /// living agents, then camp objects. A new world has none of these yet,
    /// so it opens near dry land instead of possibly over open water.
    /// </summary>
    private static Vector2 InitialCameraCenter(OwnerWorldSnapshot snapshot, WorldTerrainMap terrain)
    {
        var mapWidth = terrain.Width;
        var mapHeight = terrain.Height;
        IReadOnlyList<OwnerWorldPosition> focus =
            snapshot.Towns.FirstOrDefault(town => town.BorderTiles.Count > 0)?.BorderTiles ?? [];
        if (focus.Count == 0)
        {
            focus = snapshot.Inhabitants
                .Where(person => !person.IsDraft &&
                    string.Equals(person.Lifecycle, "active", StringComparison.OrdinalIgnoreCase))
                .Select(person => person.Position).ToArray();
        }
        if (focus.Count == 0) focus = snapshot.Objects.Select(item => item.Position).ToArray();
        if (focus.Count == 0)
        {
            // Prefer a little room around the cursor for Town-site selection.
            // This is a camera hint, not a claim that the host will accept a
            // five-building layout at that tile.
            static bool Dry(byte kind) => kind is 1 or 7 or 8 or 9;
            Vector2? nearestDry = null;
            Vector2? nearestWithRoom = null;
            var dryDistance = float.MaxValue;
            var roomDistance = float.MaxValue;
            for (var y = 1; y < mapHeight - 1; y++)
            {
                for (var x = 1; x < mapWidth - 1; x++)
                {
                    if (!Dry(terrain.At(x, y))) continue;
                    var deltaX = x - mapWidth / 2f;
                    var deltaY = y - mapHeight / 2f;
                    var distance = deltaX * deltaX + deltaY * deltaY;
                    if (distance < dryDistance)
                    {
                        dryDistance = distance;
                        nearestDry = new Vector2(x + 0.5f, y + 0.5f);
                    }
                    var hasRoom = true;
                    for (var dy = -1; dy <= 1 && hasRoom; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                            if (!Dry(terrain.At(x + dx, y + dy))) hasRoom = false;
                    }
                    if (!hasRoom) continue;
                    if (distance >= roomDistance) continue;
                    roomDistance = distance;
                    nearestWithRoom = new Vector2(x + 0.5f, y + 0.5f);
                }
            }
            return nearestWithRoom ?? nearestDry ?? new Vector2(mapWidth / 2f, mapHeight / 2f);
        }
        // Measure east/west offsets from one member so a group straddling a
        // wrapped seam is framed together instead of averaging to the far side.
        var reference = focus[0].X;
        var offset = focus.Average(position =>
        {
            var dx = position.X - reference;
            return snapshot.WrapsEastWest ? dx - MathF.Round(dx / (float)mapWidth) * mapWidth : dx;
        });
        return new Vector2(reference + (float)offset + 0.5f, (float)focus.Average(position => position.Y) + 0.5f);
    }

    private void AddMapObjectVisual(
        string id,
        OwnerWorldPosition position,
        string glyph,
        string label,
        string tooltip,
        int width = 1,
        int height = 1)
    {
        var stride = currentTileSize + TileGap;
        if (!mapObjectVisuals.TryGetValue(id, out var visual))
        {
            visual = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Pass,
                ZIndex = 5,
                ClipText = true,
            };
            visual.AddThemeFontSizeOverride("font_size", UiFonts.Body * uiLayer.Factor);
            visual.AddThemeColorOverride("font_color", new Color("E8F0D8"));
            visual.AddThemeColorOverride("font_shadow_color", new Color("18211D"));
            visual.AddThemeConstantOverride("shadow_offset_x", 1);
            visual.AddThemeConstantOverride("shadow_offset_y", 1);
            objectLayer.AddChild(visual);
            mapObjectVisuals.Add(id, visual);
        }
        var canonicalX = position.X * stride + 4;
        mapObjectCanonicalXs[id] = canonicalX;
        visual.Position = new Vector2(WrappedMarkerX(canonicalX, terrainMap!.Width, stride,
            renderedMapSnapshot?.WrapsEastWest == true), position.Y * stride + 4);
        visual.Size = new Vector2(stride * Math.Clamp(width, 1, 32) - TileGap - 8,
            stride * Math.Clamp(height, 1, 32) - TileGap - 8);
        visual.Text = !MapObjectLabelFits(visual, label) ? glyph :
            glyph.Length == 0 ? label : $"{glyph}\n{label}";
        visual.TooltipText = tooltip;
    }

    private void RenderWorldHud(OwnerWorldSnapshot snapshot)
    {
        clockLabel.Text = DisplayWorldClock(snapshot.WorldTick);
        RenderHudState(snapshot);
        weatherLayer.Paused = snapshot.Authoring?.IsPaused == true;
        menuResumeButton.Text = menuPausedWorld ? "Resume" : "Close menu";
    }

    private static int LivingPopulation(OwnerWorldSnapshot snapshot) => snapshot.Inhabitants.Count(inhabitant =>
        !inhabitant.IsDraft && string.Equals(inhabitant.Lifecycle, "active", StringComparison.OrdinalIgnoreCase));

    private OwnerWeatherRegion? WeatherRegionAtCamera(OwnerWorldSnapshot snapshot)
    {
        var size = Math.Max(1, snapshot.WeatherRegionSize);
        var x = Math.Max(0, (int)MathF.Floor(cameraCenterTiles.X / size));
        var y = Math.Max(0, (int)MathF.Floor(cameraCenterTiles.Y / size));
        return snapshot.WeatherRegions.FirstOrDefault(region => region.X == x && region.Y == y);
    }

    private string WeatherAtCamera(OwnerWorldSnapshot snapshot) =>
        WeatherRegionAtCamera(snapshot)?.Weather ?? snapshot.Authoring?.Weather ?? "unknown";

    private void RenderWorldInfo(OwnerWorldSnapshot snapshot)
    {
        RenderTownList(snapshot);
        RenderWorldStats(snapshot);
    }

}
