using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void Render(OwnerWorldSnapshot snapshot, IReadOnlyList<OwnerWorldEvent> appendedEvents)
    {
        if (usagePauseWorldId != snapshot.WorldId)
        {
            usagePauseWorldId = snapshot.WorldId;
            wasObservedPaused = false;
        }
        var isPaused = snapshot.Authoring?.IsPaused == true;
        var checkUsagePause = isPaused && !wasObservedPaused;
        wasObservedPaused = isPaused;
        observedCalendarPace = snapshot.CalendarPace;
        jevAssistanceToggle.SetPressedNoSignal(snapshot.JevEnabled == true);
        if (cameraWorldId is not null && cameraWorldId != snapshot.WorldId)
        {
            knownEvents.Clear();
            familyTreePanel.Hide();
            memoriesPanel.Hide();
            ClearTileSelection();
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
        RenderInhabitantDetails(snapshot);
        RenderSelectedInhabitantCard(snapshot);
        if (familyTreePanel.Visible && selectedInhabitantId is { } center)
        {
            familyTreeView.SetPeople(snapshot.WorldId, snapshot.Inhabitants, center);
            UpdateFamilyTreeStatus();
        }
        RenderWorldDetails(snapshot);
        RenderModLibrary(snapshot);
        RenderEventLog();
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
        renderedMapSnapshot = snapshot;
        var objectIds = snapshot.Resources.Where(resource => resource.TreeKind is null)
            .Select(resource => "resource:" + resource.Id)
            .Concat(snapshot.Objects.Select(item => "object:" + item.Id))
            .Concat(snapshot.PlacedBuildings.Select(item => "building:" + item.InstanceId)).ToHashSet(StringComparer.Ordinal);
        foreach (var id in mapObjectVisuals.Keys.Where(id => !objectIds.Contains(id)).ToArray())
        {
            mapObjectVisuals[id].QueueFree();
            mapObjectVisuals.Remove(id);
            mapObjectCanonicalXs.Remove(id);
        }

        if (!HasMap(snapshot))
        {
            foreach (var visual in inhabitantVisuals.Values) visual.QueueFree();
            inhabitantVisuals.Clear();
            inhabitantCanonicalXs.Clear();
            terrainLayer.SetHoveredTile(null);
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
                ? WorldTerrainMap.FromPacked(packed, snapshot.PackedMapLayers)
                : WorldTerrainMap.FromTiles(snapshot.Tiles, width, height, snapshot.PackedMapLayers);
            terrainWorldId = snapshot.WorldId;
            terrainManifestDigest = manifest;
            terrainLayersDigest = snapshot.MapLayersDigest;
            terrainLayer.SetWorld(terrainMap);
            worldOverview.SetWorld(terrainMap);
        }
        terrainLayer.SetTrees(snapshot.Resources);
        terrainLayer.SetNaturalObjects(snapshot.Resources);
        terrainLayer.SetWeatherRegions(snapshot.WeatherRegionSize, snapshot.WeatherRegions);
        terrainLayer.SetRoads(snapshot.RoadTiles);
        terrainLayer.SetBuildings(snapshot.PlacedBuildings, snapshot.Objects);
        worldOverview.SetRoads(snapshot.RoadTiles);
        ApplyMapFilters(snapshot);
        var mapWidth = terrainMap.Width;
        var mapHeight = terrainMap.Height;
        worldOverview.WrapsEastWest = snapshot.WrapsEastWest;
        if (!string.Equals(cameraWorldId, snapshot.WorldId, StringComparison.Ordinal))
        {
            cameraWorldId = snapshot.WorldId;
            cameraZoom = 1;
            cameraCenterTiles = InitialCameraCenter(snapshot, terrainMap);
        }
        UpdateMapGeometry(snapshot);

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
                ? string.Join(" · ", building.StoredItems.Select(item => $"{Pretty(item.Kind)} {item.Quantity}"))
                : "none recorded";
            // The terrain layer draws the roof; the marker keeps the name and hover help.
            AddMapObjectVisual("building:" + building.InstanceId, building.Position, string.Empty, name,
                $"{name}\nBuilt · {building.Width} × {building.Height} tiles" +
                (assignedTown is null ? "\nNo Town assignment" : $"\nTown · {assignedTown}") +
                (household is null ? "" : $"\nHousehold · {household.Name}\nStored here · {stored}"),
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
                    actorMarker.MouseEntered += RefreshTileHoverAtMouse;
                    actorMarker.MouseExited += RefreshTileHoverAtMouse;
                    entityLayer.AddChild(actorMarker);
                    inhabitantVisuals.Add(inhabitant.Id, actorMarker);
                }
                actorMarker.Caption = $"{GameUiText.ActivityMapGlyph(inhabitant.PublicIntention?.CandidateId)} {GameUiText.ActorMapLabel(inhabitant.DisplayName)}";
                actorMarker.Variant = AgentSprites.VariantFor(inhabitant.Id);
                actorMarker.Stage = AgentSprites.StageIndex(
                    inhabitant.DecisionFactors.FirstOrDefault(factor => factor.Key == "age-band")?.Detail);
                actorMarker.ShowNameTag = occupants.Length == 1;
                var actorTooltip = $"{inhabitant.DisplayName} · {Pretty(inhabitant.Lifecycle)} · " +
                    (inhabitant.PublicIntention?.Summary ?? "taking in the world");
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
        PositionSelectedInhabitantCard(snapshot);
        RefreshTileHoverAtMouse();
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
            visual.AddThemeFontSizeOverride("font_size", UiFonts.Body);
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
        var (width, height) = MapDimensions(snapshot);
        var localWeather = snapshot.Authoring is { } authoring
            ? $"{Pretty(authoring.Season)} · {Pretty(WeatherAtCamera(snapshot))}"
            : "Not reported";
        RenderTownList(snapshot);
        worldInfoText.Text =
            $"Date and time: {DisplayWorldClock(snapshot.WorldTick)}\n" +
            (snapshot.CalendarPace is { } pace ? $"Year length: {pace.DaysPerYear} days\n" : "") +
            $"Living agents: {LivingPopulation(snapshot)}\n" +
            $"Map size: {width} × {height}\n" +
            $"Buildings: {snapshot.PlacedBuildings.Count}\n" +
            $"Roads: {snapshot.RoadTiles.Count} tiles\n" +
            $"Towns: {snapshot.Towns.Count}\n" +
            $"Resource locations: {snapshot.Resources.Count}\n" +
            $"Season and weather here: {localWeather}" +
            (WeatherRegionAtCamera(snapshot)?.SoilMoisture is { } moisture
                ? $"\nSoil moisture here: {moisture}%"
                : "") +
            "\n\nPress F1 for keyboard and mouse controls.";
    }

}
