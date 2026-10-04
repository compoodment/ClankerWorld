using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>
    /// Buildings in use light the ground beside their windows and door at
    /// night: a House only while someone is inside, the forge while a job
    /// runs, a Warehouse only through its door lantern, and a Silo never.
    /// Light never falls on a roof or behind the back wall, never looks like
    /// a circle, drifts only slightly, and only lantern fittings show by day.
    /// </summary>
    private async Task VerifyNightLightsAsync(OwnerWorldSnapshot map)
    {
        var (zoomBefore, centerBefore) = (cameraZoom, cameraCenterTiles);
        var ada = new OwnerWorldInhabitant("agent:night-lights-ui", "Ada", "active", new(11, 10), 8_000, [], [],
            new OwnerWorldRoute("idle", null, null, [], string.Empty),
            new OwnerWorldSpatialKnowledge(new(11, 10), [new(11, 10)], [new(11, 10)]), false);
        OwnerWorldPlacedBuilding Building(string id, string tag, int x, int y, int width, int height, OwnerWorldPosition? entrance) =>
            new($"night-{id}", $"test/{tag}", new(x, y), 0, id, [tag], width, height, Entrance: entrance);
        OwnerWorldPlacedBuilding Lantern(string id, string tag, int x, int y, int roadX, int roadY) =>
            new($"night-{id}", $"test/{tag}", new(x, y), 0, id, ["street_lantern", tag], 1, 1,
                Entrance: new(roadX, roadY));
        var night = map with
        {
            WorldTick = 30,
            DarknessBasisPoints = 10_000,
            Inhabitants = [ada],
            Objects = [],
            PlacedBuildings =
            [
                Building("house-lived", "house", 11, 10, 1, 1, new(11, 11)),
                Building("house-empty", "house", 12, 10, 1, 1, new(12, 11)),
                Building("smithy", "blacksmith", 17, 9, 1, 2, new(17, 11)),
                Building("warehouse", "warehouse", 20, 9, 2, 2, new(21, 11)),
                Building("silo", "silo", 24, 10, 1, 1, null),
                Lantern("stone-north", "stone_lantern", 12, 8, 12, 9),
                Lantern("hanging-east", "hanging_lantern", 16, 8, 15, 8),
                Lantern("stone-west", "stone_lantern", 20, 8, 21, 8),
                Lantern("hanging-south", "hanging_lantern", 24, 8, 24, 7),
            ],
            RoadTiles = [new(12, 9), new(15, 8), new(21, 8), new(24, 7)],
            ProductionJobs = [new("night-job", "test/axe", "night-smithy", ada.Id, 10, 90, "running")],
        };

        // Who is inside and which jobs run decide what may be lit.
        var lights = BuildingLights(night);
        BuildingLight At(int x) => lights.Single(light => light.Footprint.Position.X == x);
        if (lights.Count != 5 || !At(11).Occupied || At(12).Occupied || At(12).Working ||
            !At(17).Working || At(20).Occupied || At(24).Plan.Design != LitDesign.Silo ||
            At(11).Plan.Design != LitDesign.House || At(20).Plan.Design != LitDesign.Warehouse)
            throw new InvalidOperationException("Night lights must follow who is inside each building and which jobs run there.");

        // Newly integrated building families must reach the map's real lighting path.
        foreach (var (tag, width, height) in new[] { ("clinic", 1, 2), ("restaurant", 1, 2), ("restaurant", 2, 2), ("town_hall", 3, 4), ("market", 2, 2) })
        {
            var inUse = night with
            {
                PlacedBuildings = [Building(tag, tag, 11, 10, width, height, new(11, 10 + height))],
                ProductionJobs = [],
            };
            var current = BuildingLights(inUse);
            if (current.Count != 1 || !current[0].Occupied ||
                !NightLightShapes.Building(current[0].Plan, true, false, true, 0, 1)
                    .Any(cell => cell.Kind == LightCellKind.Light))
                throw new InvalidOperationException($"The occupied {tag} must light its actual map footprint at night.");
            var plan = current[0].Plan;
            if (tag == "restaurant" && width == 1 && plan.Yard is not null ||
                tag == "town_hall" && (plan.Wing is null || plan.Roof.Size.X >= width * 32 - 6))
                throw new InvalidOperationException("Compact Restaurants and cross-shaped Town Halls must use their actual roof geometry.");
            var roofs = plan.Wing is { } wing ? new[] { plan.Roof, wing } : [plan.Roof];
            if (NightLightShapes.Building(plan, true, false, true, 0, 1)
                .Any(cell => cell.Kind == LightCellKind.Light && roofs.Any(roof => cell.Area.Intersects(roof))))
                throw new InvalidOperationException("Restaurant lanterns and Town Hall windows must leave every part of their roof dark.");
            if (tag == "market")
            {
                // Measured from the approved hall and arcade, including the 16px painter rounding.
                if (plan.Design != LitDesign.MarketHall || plan.Roof != new Rect2(3, 3, 58, 54) ||
                    current[0].AtAtlas(16).Plan.Roof != new Rect2(4, 4, 56, 52))
                    throw new InvalidOperationException("Market lights must follow the actual hall and arcade at both atlas sizes.");
                var stallOnly = inUse with { PlacedBuildings = [Building("stall", "market_stall", 11, 10, 1, 1, new(11, 11))] };
                if (BuildingLights(stallOnly).Count != 0)
                    throw new InvalidOperationException("Market stalls must stay dark.");
            }
            if (tag == "town_hall")
            {
                // Measured from the 16px atlas: its integer cross differs from scaling the 32px plan.
                var actualMain = new Rect2(20, 4, 56, 102);
                var actualWing = new Rect2(4, 34, 88, 38);
                var mid = current[0].AtAtlas(16).Plan;
                if (mid.Roof != actualMain || mid.Wing != actualWing ||
                    NightLightShapes.Building(mid, true, false, true, 0, 1, snap: 2)
                        .Any(cell => cell.Kind == LightCellKind.Light &&
                            (cell.Area.Intersects(actualMain) || cell.Area.Intersects(actualWing))))
                    throw new InvalidOperationException("Mid-zoom Town Hall lights must stay outside the actual 16px roof pixels.");
            }
        }

        // The fitting follows its saved Road neighbour, never a nearby Road search or a fallback door.
        var lanterns = StreetLanterns(night);
        if (lanterns.Count != 4 ||
            !lanterns.Contains(new StreetLanternLight(new(12, 9), DoorSide.North, LanternStyle.Stone)) ||
            !lanterns.Contains(new StreetLanternLight(new(15, 8), DoorSide.East, LanternStyle.Hanging)) ||
            !lanterns.Contains(new StreetLanternLight(new(21, 8), DoorSide.West, LanternStyle.Stone)) ||
            !lanterns.Contains(new StreetLanternLight(new(24, 7), DoorSide.South, LanternStyle.Hanging)) ||
            StreetLanternLight.FromBuilding(Lantern("invalid", "stone_lantern", 12, 8, 14, 8), 256, false) is not null ||
            StreetLanternLight.FromBuilding(Lantern("seam", "hanging_lantern", 0, 8, 255, 8), 256, true)
                is not { Edge: DoorSide.East, RoadTile.X: 255 })
            throw new InvalidOperationException("Street fittings must follow the saved cardinal Road edge, including the wrapped seam, and reject a non-adjacent Road.");

        // A lived-in House: windows on both sides and the door, never the roof or the back wall.
        static bool Overlaps(Rect2 a, Rect2 b) =>
            a.Position.X < b.End.X && b.Position.X < a.End.X && a.Position.Y < b.End.Y && b.Position.Y < a.End.Y;
        static IEnumerable<Rect2> Light(IEnumerable<LightCell> cells) =>
            cells.Where(cell => cell.Kind == LightCellKind.Light).Select(cell => cell.Area);
        var house = At(11).Plan;
        var homeLight = Light(NightLightShapes.Building(house, true, false, true, 0, 1)).ToArray();
        if (homeLight.Any(area => Overlaps(area, house.Roof)) ||
            homeLight.Any(area => area.End.Y <= house.Roof.Position.Y) ||
            !homeLight.Any(area => area.Position.X >= house.Roof.End.X) ||
            !homeLight.Any(area => area.End.X <= house.Roof.Position.X) ||
            !homeLight.Any(area => area.Position.Y >= house.Roof.End.Y))
            throw new InvalidOperationException("A lived-in House must light the ground at its sides and door, not its roof or back wall.");
        if (NightLightShapes.Building(At(12).Plan, false, false, true, 0, 1).Count != 0 ||
            NightLightShapes.Building(At(12).Plan, false, true, true, 0, 1).Count != 0 ||
            (At(12) with { Working = true }).Shines ||
            NightLightShapes.Building(At(24).Plan, true, true, true, 0, 1).Count != 0)
            throw new InvalidOperationException("An empty House and a Silo must stay dark.");

        // The Warehouse: a lantern fitting by day, lit only at night while someone is in it.
        var store = At(20).Plan;
        var byDay = NightLightShapes.Building(store, true, false, false, 0, 1);
        var empty = NightLightShapes.Building(store, false, false, true, 0, 1);
        var used = NightLightShapes.Building(store, true, false, true, 0, 1);
        if (byDay.Any(cell => cell.Kind != LightCellKind.Paint) || byDay.Count == 0 ||
            empty.Any(cell => cell.Kind != LightCellKind.Paint) ||
            !used.Any(cell => cell.Kind == LightCellKind.Glow) || !Light(used).Any() ||
            Light(used).Any(area => Overlaps(area, store.Roof)))
            throw new InvalidOperationException("A Warehouse must show only its door lantern, lit at night while someone fetches goods, never on its roof.");

        // The forge glows in its yard, off the roof, ragged rather than round, and it moves.
        var smithy = At(17).Plan;
        var forge = Light(NightLightShapes.Building(smithy, false, true, true, 0, 1)).ToArray();
        var later = Light(NightLightShapes.Building(smithy, false, true, true, 0.5f, 1)).ToArray();
        var hearth = NightLightShapes.ForgeHearth(smithy.Yard!.Value);
        var lopsided = forge.Any(area => MathF.Abs(hearth.X - area.Position.X - (area.End.X - hearth.X)) >= 2);
        if (forge.Length == 0 || forge.Any(area => Overlaps(area, smithy.Roof)) || !lopsided ||
            forge.SequenceEqual(later))
            throw new InvalidOperationException("The forge must glow in its yard, off the roof, with a ragged, flickering edge.");

        // Window light drifts by at most a couple of art pixels.
        static Rect2 Bounds(IEnumerable<Rect2> areas) => areas.Aggregate((a, b) => a.Merge(b));
        var drift = Light(NightLightShapes.Building(house, true, false, true, 3.7f, 1)).ToArray();
        var (now, then) = (Bounds(homeLight), Bounds(drift));
        if ((now.Position - then.Position).Abs().X > 3 || (now.Position - then.Position).Abs().Y > 3 ||
            (now.End - then.End).Abs().X > 3 || (now.End - then.End).Abs().Y > 3)
            throw new InvalidOperationException($"Window light must move only slightly: {now} then {then}.");

        // On the map the lights lie just over the night wash, under labels and agents, and let clicks through.
        cameraZoom = maximumCameraZoom;
        cameraCenterTiles = new Vector2(18, 10);
        RenderMap(night);
        nightLayer.Settle();
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var layers = mapStage.GetChildren();
        if (layers.IndexOf(nightLightsLayer) != layers.IndexOf(nightLayer) + 1 ||
            layers.IndexOf(objectLayer) < layers.IndexOf(nightLightsLayer) ||
            layers.IndexOf(entityLayer) < layers.IndexOf(nightLightsLayer) ||
            nightLightsLayer.MouseFilter != Control.MouseFilterEnum.Ignore ||
            nightLightsLayer.Buildings.Count != 5 ||
            nightLightsLayer.Lanterns.Count != 4 || terrainLayer.BuildingSpriteCount != 5 ||
            !nightLightsLayer.DrawnCells.Any(cell => cell.Kind == LightCellKind.Light))
            throw new InvalidOperationException("Night lights must draw just over the night wash, under labels and agents, and let clicks through.");

        // A lit neighbour must not warm an empty House's roof.
        var roofUnit = terrainLayer.Stride / 32f;
        var neighbourRoof = At(12).Plan.Roof;
        var blockedRoof = new Rect2(new Vector2(12, 10) * terrainLayer.Stride + neighbourRoof.Position * roofUnit,
            neighbourRoof.Size * roofUnit);
        if (nightLightsLayer.DrawnCells.Any(cell => cell.Kind == LightCellKind.Light && cell.Area.Intersects(blockedRoof)))
            throw new InvalidOperationException("Light from a neighbouring building must leave an empty House's roof dark.");

        // Each post is painted at the approved edge of its actual Road tile, and a click there selects it.
        foreach (var building in night.PlacedBuildings.Where(building => StreetLanternLight.IsLantern(building.Tags)))
        {
            var fitting = StreetLanternLight.FromBuilding(building, 256, false)!.Value;
            var point = new Vector2(fitting.RoadTile.X, fitting.RoadTile.Y) * terrainLayer.Stride +
                fitting.Post * (terrainLayer.Stride / 32f);
            if (!nightLightsLayer.DrawnCells.Any(cell => cell.Kind == LightCellKind.Paint && cell.Area.HasPoint(point)) ||
                BuildingAt(night, fitting.RoadTile, mapStage.Position + point)?.InstanceId != building.InstanceId)
                throw new InvalidOperationException("Each visible street fitting must draw and select on its saved Road edge.");
        }
        var glowOnlyPoint = new Vector2(12, 9) * terrainLayer.Stride + new Vector2(28, 16) * (terrainLayer.Stride / 32f);
        if (BuildingAt(night, new(12, 9), mapStage.Position + glowOnlyPoint) is not null)
            throw new InvalidOperationException("Street-light spill on the ground must not act as a building hit target.");
        var emptyPostTilePoint = new Vector2(12.5f, 8.5f) * terrainLayer.Stride;
        if (BuildingAt(night, new(12, 8), mapStage.Position + emptyPostTilePoint) is not null)
            throw new InvalidOperationException("The unpainted part of a street lamp's plot must not replace its visible fitting hit target.");

        // By day only the lantern fitting is drawn.
        RenderMap(night with { DarknessBasisPoints = 0 });
        nightLayer.Settle();
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (nightLightsLayer.DrawnCells.Any(cell => cell.Kind != LightCellKind.Paint) ||
            !nightLightsLayer.DrawnCells.Any(cell => cell.Kind == LightCellKind.Paint))
            throw new InvalidOperationException("By day only lantern fittings may show, unlit.");

        // The same visible building copy must light up across the world seam.
        var east = terrainMap!.Width - 1;
        var wrapped = night with
        {
            WorldId = night.WorldId + ":night-seam",
            WrapsEastWest = true,
            Inhabitants = [ada with { Position = new(east, 10) }],
            PlacedBuildings = [Building("wrapped-house", "house", east, 10, 1, 1, new(east, 11))],
            ProductionJobs = [],
        };
        RenderMap(wrapped);
        cameraZoom = maximumCameraZoom;
        cameraCenterTiles = new Vector2(0, 10);
        RenderMap(wrapped);
        nightLayer.Settle();
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!nightLightsLayer.DrawnCells.Any(cell => cell.Kind == LightCellKind.Light && cell.Area.Position.X < 0))
            throw new InvalidOperationException("A wrapped building copy must keep its light at the visible world seam.");

        RenderMap(map);
        // A completed street fitting works without inhabitants or production jobs.
        var street = night with
        {
            Inhabitants = [],
            ProductionJobs = [],
            PlacedBuildings = night.PlacedBuildings.Where(building => StreetLanternLight.IsLantern(building.Tags)).ToArray(),
        };
        foreach (var darkness in new[] { 0, 2_000, 10_000, 0 })
        {
            RenderMap(street with { DarknessBasisPoints = darkness });
            nightLayer.Settle();
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var cells = nightLightsLayer.DrawnCells;
            if (nightLightsLayer.Buildings.Count != 0 || terrainLayer.BuildingSpriteCount != 0 || cells.Count == 0 ||
                (darkness == 0 ? cells.Any(cell => cell.Kind != LightCellKind.Paint) :
                    !cells.Any(cell => cell.Kind == LightCellKind.Light) || !cells.Any(cell => cell.Kind == LightCellKind.Glow)))
                throw new InvalidOperationException("Completed street fittings must show by day, light at dusk without people or jobs, and go dark again at dawn without drawing roofs.");
        }

        // At daylight, only camera/zoom changes can refresh this fitting-only layer.
        var fullSize = terrainLayer.TileSize;
        var fullCells = nightLightsLayer.DrawnCells.Select(cell => cell.Area).ToArray();
        cameraZoom = Math.Clamp(minimumCameraZoom * 2, minimumCameraZoom, maximumCameraZoom);
        UpdateMapGeometry(street with { DarknessBasisPoints = 0 });
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (terrainLayer.TileSize == fullSize || fullCells.SequenceEqual(nightLightsLayer.DrawnCells.Select(cell => cell.Area)))
            throw new InvalidOperationException("Daytime street fittings must redraw when a lantern-only map changes zoom.");
        var zoomedPoint = new Vector2(12, 9) * terrainLayer.Stride + new Vector2(16, 4) * (terrainLayer.Stride / 32f);
        if (BuildingAt(street, new(12, 9), mapStage.Position + zoomedPoint)?.InstanceId != "night-stone-north")
            throw new InvalidOperationException("Visible fitting hit targets must follow the map's current zoom and camera position.");
        var visibleBefore = terrainLayer.VisibleTiles;
        CenterCameraAt(new Vector2(180, 64));
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (terrainLayer.VisibleTiles == visibleBefore || nightLightsLayer.DrawnCells.Count != 0)
            throw new InvalidOperationException("Daytime street fittings must leave the drawing when a lantern-only map pans away.");

        cameraZoom = minimumCameraZoom;
        cameraCenterTiles = new Vector2(18, 8);
        RenderMap(street);
        nightLayer.Settle();
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (terrainLayer.TileSize >= WorldTerrainLayer.SpriteTileMinimum ||
            !nightLightsLayer.DrawnCells.Any(cell => cell.Kind == LightCellKind.Light))
            throw new InvalidOperationException("Street lamps must remain visible as warm lights at overview zoom.");
        var overviewPoint = new Vector2(12, 9) * terrainLayer.Stride + new Vector2(16, 4) * (terrainLayer.Stride / 32f);
        if (BuildingAt(street, new(12, 9), mapStage.Position + overviewPoint)?.InstanceId != "night-stone-north")
            throw new InvalidOperationException("A street fitting must remain selectable at overview zoom.");

        (cameraZoom, cameraCenterTiles) = (zoomBefore, centerBefore);
        RenderMap(map);
        nightLayer.Settle();
    }
}
