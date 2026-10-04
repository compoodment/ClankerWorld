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
        var night = map with
        {
            WorldTick = 30,
            DarknessBasisPoints = 10_000,
            Inhabitants = [ada],
            PlacedBuildings =
            [
                Building("house-lived", "house", 11, 10, 1, 1, new(11, 11)),
                Building("house-empty", "house", 12, 10, 1, 1, new(12, 11)),
                Building("smithy", "blacksmith", 17, 9, 1, 2, new(17, 11)),
                Building("warehouse", "warehouse", 20, 9, 2, 2, new(21, 11)),
                Building("silo", "silo", 24, 10, 1, 1, null),
            ],
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
        foreach (var (tag, width, height) in new[] { ("clinic", 1, 2), ("restaurant", 1, 2), ("restaurant", 2, 2), ("town_hall", 3, 4) })
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
        }

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
            !nightLightsLayer.DrawnCells.Any(cell => cell.Kind == LightCellKind.Light))
            throw new InvalidOperationException("Night lights must draw just over the night wash, under labels and agents, and let clicks through.");

        // A lit neighbour must not warm an empty House's roof.
        var roofUnit = terrainLayer.Stride / 32f;
        var neighbourRoof = At(12).Plan.Roof;
        var blockedRoof = new Rect2(new Vector2(12, 10) * terrainLayer.Stride + neighbourRoof.Position * roofUnit,
            neighbourRoof.Size * roofUnit);
        if (nightLightsLayer.DrawnCells.Any(cell => cell.Kind == LightCellKind.Light && cell.Area.Intersects(blockedRoof)))
            throw new InvalidOperationException("Light from a neighbouring building must leave an empty House's roof dark.");

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
        (cameraZoom, cameraCenterTiles) = (zoomBefore, centerBefore);
        RenderMap(map);
        nightLayer.Settle();
    }
}
