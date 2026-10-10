using System.Text.Json;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;
using GodotOwnerWorldEvent = ClankerWorld.GodotClient.UI.OwnerWorldEvent;
using GodotOwnerWorldSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Bridges on the normal private-world path, using generated maps. The seeds
/// and tiles were chosen so that a Town beside a river can grow across it,
/// and so that an agent wades a one-tile or a two-tile river to reach food.
/// </summary>
public sealed class TownBridgeRuntimeTests
{
    private const string GrowthSeed = "town-bridge-8";
    private static readonly GridPoint GrowthTownSite = new(45, 42);
    private static readonly GridPoint GrowthBuildingSite = new(53, 41);
    private const string GrowthBridgeId = "bridge-51-42-ew-2";
    private static readonly GridPoint RunOnTownSite = new(45, 42);
    private static readonly GridPoint RunOnBuildingSite = new(50, 41);
    private const string RunOnBridgeId = "bridge-51-42-ew-2";
    private const string GrowthBuildingId = "bridge-growth";
    private static readonly JsonSerializerOptions GodotJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task TownGrowthAcrossATwoTileRiverSavesOneRoadBridgeThatMovementDrawingAndInspectionShare()
    {
        using var world = await GrowthWorldAsync();
        var before = world.ExportState();
        var roadsBefore = world.RoadTiles.ToHashSet();
        Assert.Empty(world.Bridges);
        var workshop = world.WorldContent.Buildings.Single(item => item.LocalId == "workshop");

        var placed = world.PlaceBuilding(GrowthBuildingId, workshop.CanonicalId, GrowthBuildingSite);

        Assert.True(placed.Applied, placed.Failure);
        var bridge = Assert.Single(world.Bridges);
        Assert.Equal(GrowthBridgeId, bridge.Id);
        Assert.Equal(BridgeTriggers.Road, bridge.Trigger);
        Assert.Equal(BridgeDesigns.PlankSpanTwo, bridge.Design);
        Assert.Equal($"road:{TownBorderRules.FirstTownId}:{GrowthBuildingId}", bridge.RouteId);
        Assert.Equal(2, bridge.Span.Count);
        var state = world.ExportState();
        var map = state.Map;
        Assert.All(bridge.Entrances, entrance => Assert.Contains(entrance, world.RoadTiles));
        Assert.True(roadsBefore.IsSubsetOf(world.RoadTiles));
        Assert.All(world.RoadTiles, tile => Assert.True(map.IsBuildable(tile)));
        Assert.DoesNotContain(world.RoadTiles, tile => bridge.Span.Contains(tile));
        // Only the workshop's own cost is charged: the Road and bridge cost nothing.
        var spent = Totals(before).ToDictionary(item => item.Key, item => item.Value - Totals(state).GetValueOrDefault(item.Key));
        Assert.Equal(workshop.BuildCosts.ToDictionary(item => item.ResourceId, item => item.Amount),
            spent.Where(item => item.Value != 0).ToDictionary());
        Assert.Contains(state.Events, item => item.Kind == "bridge_built" &&
            item.Detail == $"road:{GrowthBridgeId}:{bridge.RouteId}");

        // The two-tile river could only be waded slowly before; movement now
        // walks the saved deck at dry-ground speed.
        Assert.All(bridge.Span, tile => Assert.Equal(SeededMap.TwoTileWadingFootCost, before.Map.FootTravelCost(tile)));
        Assert.All(bridge.Span, tile => Assert.Equal(100, map.FootTravelCost(tile)));
        Assert.True(map.CanFootStep(bridge.Entrances[0], bridge.Span[0]));
        Assert.True(map.CanFootStep(bridge.Span[0], bridge.Span[1]));
        Assert.True(map.CanFootStep(bridge.Span[1], bridge.Entrances[1]));
        Assert.False(map.CanFootStep(bridge.Span[0], new GridPoint(bridge.Span[0].X, bridge.Span[0].Y - 1)));
        Assert.False(map.CanFootStep(bridge.Span[1], new GridPoint(bridge.Span[1].X, bridge.Span[1].Y + 1)));
        Assert.False(map.CanFootStep(new GridPoint(bridge.Entrances[0].X, bridge.Entrances[0].Y - 1), bridge.Span[0]));
        Assert.True(map.IsReachableOnFoot(bridge.Entrances[0], bridge.Entrances[1]));

        var saved = PrivateWorldRuntimeCodec.Encode(state);
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal([GrowthBridgeId], reloaded.Bridges.Select(item => item.Id));
        Assert.Equal(world.RoadTiles, reloaded.RoadTiles);
        Assert.True(reloaded.ExportState().Map.IsReachableOnFoot(bridge.Entrances[0], bridge.Entrances[1]));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));

        // An agent standing on the deck, and its memory of that tile, are
        // valid. Without the bridge it would be wading the two-tile river
        // there, which is valid too; water too deep to wade is refused.
        var walker = state.Inhabitants[0].InhabitantId;
        var onDeck = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == walker
                ? person with { Position = bridge.Span[1] } : person).ToArray(),
            Knowledge = state.Knowledge! with
            {
                Facts = [.. state.Knowledge.Facts, new AgentKnowledgeFact("fact:bridge-deck", walker, walker,
                    bridge.Span[1], nameof(TerrainKind.River), [], state.Society.Society.WorldTick, "firsthand")],
            },
        };
        using (var standing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(onDeck))))
            Assert.Equal(bridge.Span[1], standing.Inhabitants.Single(person => person.InhabitantId == walker).Position);
        using (var wading = PrivateWorldRuntime.Restore(onDeck with { Bridges = [] }))
            Assert.Equal(SeededMap.TwoTileWadingFootCost, wading.ExportState().Map.FootTravelCost(bridge.Span[1]));
        var deepWater = map.Tiles.Select(tile => tile.Position).First(point => !map.IsLand(point) && !map.IsPassable(point));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(onDeck with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == walker
                ? person with { Position = deepWater } : person).ToArray(),
        }));

        var snapshot = new OwnerWorldObservationStore(reloaded).GetSnapshot();
        var projected = Assert.Single(snapshot.Bridges);
        Assert.Equal((bridge.Id, bridge.Design, bridge.Trigger, "east_west"),
            (projected.Id, projected.Design, projected.Trigger, projected.Axis));
        Assert.Equal(bridge.Span.Select(point => (point.X, point.Y)), projected.Span.Select(point => (point.X, point.Y)));
        Assert.Equal(bridge.Entrances.Select(point => (point.X, point.Y)), projected.Entrances.Select(point => (point.X, point.Y)));
        var godot = JsonSerializer.Deserialize<GodotOwnerWorldSnapshot>(
            JsonSerializer.Serialize(snapshot, GodotJsonOptions), GodotJsonOptions)!;
        var godotBridge = Assert.Single(godot.Bridges);
        Assert.Equal(bridge.Span.Select(point => (point.X, point.Y)), godotBridge.Span.Select(point => (point.X, point.Y)));
        var built = new OwnerWorldObservationStore(reloaded).GetEventsAfter(0).Events.Single(item => item.Kind == "bridge_built");
        var godotEvent = JsonSerializer.Deserialize<GodotOwnerWorldEvent>(JsonSerializer.Serialize(built, GodotJsonOptions), GodotJsonOptions)!;
        Assert.Equal("A bridge was built where a new Road crosses the river.", ClankerWorld.GodotClient.UI.WorldEventText.Describe(godotEvent, godot));

        // Replay identity: the saved world and the live one keep advancing identically.
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Fact]
    public async Task RepeatingOrReloadingBeforeTheProposalGivesTheSameRoadsAndBridgeWithoutDuplicates()
    {
        using var first = await GrowthWorldAsync();
        var beforeProposal = first.ExportState();
        var workshop = first.WorldContent.Buildings.Single(item => item.LocalId == "workshop").CanonicalId;
        Assert.True(first.PlaceBuilding(GrowthBuildingId, workshop, GrowthBuildingSite).Applied);

        // Nothing of a proposal is saved before it commits; restoring the
        // checkpoint taken just before it and growing again gives the same result.
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(beforeProposal)));
        Assert.Empty(reloaded.Bridges);
        Assert.True(reloaded.PlaceBuilding(GrowthBuildingId, workshop, GrowthBuildingSite).Applied);
        using var repeated = await GrowthWorldAsync();
        Assert.True(repeated.PlaceBuilding(GrowthBuildingId, workshop, GrowthBuildingSite).Applied);
        foreach (var other in new[] { reloaded, repeated })
        {
            Assert.Equal(first.RoadTiles, other.RoadTiles);
            Assert.Equal(first.Bridges.Select(item => (item.Id, item.RouteId)), other.Bridges.Select(item => (item.Id, item.RouteId)));
        }

        // A second building across the same river reuses the bridge.
        var bridges = first.Bridges;
        var roads = first.RoadTiles.ToHashSet();
        var map = first.ExportState().Map;
        var defs = first.WorldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var occupied = map.Resources.Select(item => item.Position).Concat(map.CampObjects.Select(item => item.Position))
            .Concat(first.WorldSimulation.Buildings.SelectMany(item => WorldContentSimulationRules.Footprint(defs[item.DefinitionId], item.Position)))
            .ToHashSet();
        var town = Assert.Single(first.Towns);
        var farBank = bridges.Single().Entrances[1];
        var neighbor = map.Tiles.Select(tile => tile.Position)
            .Where(point => point.X >= farBank.X && map.FootDistance(point, GrowthBuildingSite) <= 3)
            .OrderBy(point => map.FootDistance(point, GrowthBuildingSite)).ThenBy(point => point.Y).ThenBy(point => point.X)
            .First(point => map.IsBuildable(point) && !occupied.Contains(point) && !roads.Contains(point) &&
                TownBorderRules.IsWithinOrAdjacent(town, point, 1, 1));
        Assert.True(first.PlaceBuilding("bridge-growth-second", workshop, neighbor).Applied);
        Assert.Equal(bridges.Select(item => item.Id), first.Bridges.Select(item => item.Id));
        Assert.Single(first.ExportState().Events, item => item.Kind == "bridge_built");
    }

    [Fact]
    public async Task AStreetRunningOnPastANewDoorCrossesANarrowRiverOnASavedBridge()
    {
        // The workshop faces an existing street, so no new side street is
        // needed; a nearby dead end then runs on and meets a two-tile river.
        using var world = await GrowthWorldAsync(townSite: RunOnTownSite);
        var workshop = world.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
        var roadsBefore = world.RoadTiles.ToHashSet();
        var eventsBefore = world.ExportState().Events.Count;
        Assert.Contains(roadsBefore, tile => WorldContentSimulationRules.IsEntrance(workshop, RunOnBuildingSite, tile));

        var placed = world.PlaceBuilding("bridge-run-on", workshop.CanonicalId, RunOnBuildingSite);
        Assert.True(placed.Applied, placed.Failure);
        Assert.Contains(Assert.IsType<GridPoint>(world.WorldSimulation.Buildings.Single(
            building => building.InstanceId == "bridge-run-on").Entrance), roadsBefore);

        var bridge = Assert.Single(world.Bridges);
        Assert.Equal((RunOnBridgeId, BridgeTriggers.Road, $"road:{TownBorderRules.FirstTownId}:bridge-run-on"),
            (bridge.Id, bridge.Trigger, bridge.RouteId));
        var events = world.ExportState().Events.Skip(eventsBefore).Select(item => item.Kind).ToArray();
        Assert.DoesNotContain("town_road_generated", events);
        Assert.Contains("town_road_extended", events);
        Assert.Contains("bridge_built", events);
        var map = world.ExportState().Map;
        Assert.True(roadsBefore.IsSubsetOf(world.RoadTiles));
        Assert.All(bridge.Entrances, entrance => Assert.Contains(entrance, world.RoadTiles));
        Assert.All(world.RoadTiles, tile => Assert.True(map.IsBuildable(tile)));
        Assert.True(map.CanFootStep(bridge.Entrances[0], bridge.Span[0]));
        Assert.Equal(2, bridge.Span.Count);
        Assert.True(map.CanFootStep(bridge.Span[0], bridge.Span[1]));
        Assert.True(map.CanFootStep(bridge.Span[^1], bridge.Entrances[1]));
        // The far bank is inside the Town border, which grew around the new street.
        Assert.Contains(bridge.Entrances[1], Assert.Single(world.Towns).BorderTiles);
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal([bridge.Id], reloaded.Bridges.Select(item => item.Id));
    }

    [Fact]
    public async Task RoadsAndBridgesRemainWhenTheirBuildingIsGoneAndDamagedBridgeSavesAreRefused()
    {
        using var world = await GrowthWorldAsync();
        var workshop = world.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
        Assert.True(world.PlaceBuilding(GrowthBuildingId, workshop.CanonicalId, GrowthBuildingSite).Applied);
        var state = world.ExportState();
        var bridge = Assert.Single(state.Bridges!);

        // Remove the building that caused the Road and bridge.
        var buildings = state.WorldSimulation!.Buildings.Where(item => item.InstanceId != GrowthBuildingId).ToArray();
        // The saved border is authoritative and stays as it grew.
        var town = Assert.Single(state.Towns!);
        var shrunk = town with { AssignedBuildingIds = town.AssignedBuildingIds.Where(id => id != GrowthBuildingId).ToArray() };
        using var withoutBuilding = PrivateWorldRuntime.Restore(state with
        {
            WorldSimulation = state.WorldSimulation with { Buildings = buildings },
            Towns = [shrunk],
        });
        Assert.Equal([bridge.Id], withoutBuilding.Bridges.Select(item => item.Id));
        Assert.Equal(world.RoadTiles, withoutBuilding.RoadTiles);
        Assert.True(withoutBuilding.ExportState().Map.IsReachableOnFoot(bridge.Entrances[0], bridge.Entrances[1]));

        // A Road bridge whose Road ends were lost, a missing bridge list, an
        // or a deck over dry land is refused.
        PrivateWorldRuntimeState[] damaged =
        [
            state with { RoadTiles = state.RoadTiles!.Where(tile => tile != bridge.Entrances[1]).ToArray() },
            state with { Bridges = null },
            state with { BridgeTraffic = null },
            state with { Bridges = [bridge with { Span = [bridge.Entrances[0], bridge.Span[1]] }] },
        ];
        foreach (var item in damaged)
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(item));
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        var tampered = System.Text.Encoding.UTF8.GetString(bytes).Replace(bridge.Id, "bridge-221-4-ew-1", StringComparison.Ordinal);
        var tamperedBytes = System.Text.Encoding.UTF8.GetBytes(tampered);
        Assert.False(bytes.AsSpan().SequenceEqual(tamperedBytes));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(tamperedBytes));
    }

    [Fact]
    public async Task RealWadesBuildATrafficBridgeWithoutAnyRoadAndWaitingDoesNotCount()
    {
        const string crossingId = "bridge-102-19-ns-1";
        var start = new GridPoint(102, 18);
        using var control = await TrafficWorldAsync(seedEvidence: false);
        var roads = control.RoadTiles;

        var wadingSeen = false;
        for (var tick = 0; tick < 20 && control.BridgeTraffic.Completed.All(item => item.AgentId != TrafficAgentId); tick++)
        {
            Assert.True((await control.AdvanceOneTickAsync()).Advanced);
            var position = control.Inhabitants.Single(item => item.InhabitantId == TrafficAgentId).Position;
            if (position == new GridPoint(102, 19))
            {
                wadingSeen = true;
                Assert.Equal(new BridgeTrafficWade(TrafficAgentId, crossingId, start), Assert.Single(control.BridgeTraffic.InProgress));
                Assert.DoesNotContain(control.BridgeTraffic.Completed, item => item.AgentId == TrafficAgentId);
            }
        }
        Assert.True(wadingSeen);
        var crossing = Assert.Single(control.BridgeTraffic.Completed);
        Assert.Equal((crossingId, TrafficAgentId), (crossing.CrossingId, crossing.AgentId));
        Assert.Empty(control.Bridges);
        using (var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
                   PrivateWorldRuntimeCodec.Encode(control.ExportState()))))
            Assert.Equal(control.BridgeTraffic.Completed, reloaded.BridgeTraffic.Completed);

        using var busy = await TrafficWorldAsync(seedEvidence: true);
        var directory = Directory.CreateTempSubdirectory("clankerworld-traffic-bridge-");
        var logger = new RecordingLogger<PrivateWorldRuntimeService>();
        try
        {
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(1));
            presence.RecordAuthenticatedReconnect("owner");
            using var service = new PrivateWorldRuntimeService(busy,
                new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            for (var tick = 0; tick < 20 && busy.Bridges.Count == 0; tick++)
                Assert.True(await service.TryAdvanceOnceAsync());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
        Assert.Contains(logger.Messages, message => message.Contains(
            $"bridge outcome=built world_tick={busy.WorldTick} bridge={crossingId} trigger=traffic reason=none",
            StringComparison.Ordinal));
        var bridge = Assert.Single(busy.Bridges);
        Assert.Equal((crossingId, BridgeTriggers.Traffic, BridgeDesigns.PlankSpanOne, (string?)null),
            (bridge.Id, bridge.Trigger, bridge.Design, bridge.RouteId));
        Assert.Equal(roads, busy.RoadTiles);
        Assert.DoesNotContain(busy.BridgeTraffic.Completed, item => item.CrossingId == crossingId);
        Assert.Contains(busy.ExportState().Events, item => item.Kind == "bridge_built" && item.Detail == $"traffic:{crossingId}");
        Assert.Equal(100, busy.ExportState().Map.FootTravelCost(bridge.Span[0]));
    }

    [Fact]
    public async Task SlowRealWadesAcrossATwoTileRiverSurviveReloadAndBuildATwoTileTrafficBridge()
    {
        const string crossingId = "bridge-124-62-ew-2";
        var start = new GridPoint(123, 62);
        var farBank = new GridPoint(126, 62);
        GridPoint[] water = [new(124, 62), new(125, 62)];
        using var control = await TrafficWorldAsync(seedEvidence: false, crossingId, start);
        Assert.All(water, tile => Assert.Equal(SeededMap.TwoTileWadingFootCost, control.ExportState().Map.FootTravelCost(tile)));

        // The agent wades straight across to food on the far bank. Each water
        // tile holds it for three ticks (a one-tile river holds it for two),
        // and the wade stays open while it steps from one water tile to the next.
        var ticksAt = new Dictionary<GridPoint, int>();
        PrivateWorldRuntimeState? midstream = null;
        for (var tick = 0; tick < 20 && control.BridgeTraffic.Completed.All(item => item.AgentId != TrafficAgentId); tick++)
        {
            Assert.True((await control.AdvanceOneTickAsync()).Advanced);
            var position = control.Inhabitants.Single(item => item.InhabitantId == TrafficAgentId).Position;
            ticksAt[position] = ticksAt.GetValueOrDefault(position) + 1;
            if (!water.Contains(position)) continue;
            Assert.Equal(new BridgeTrafficWade(TrafficAgentId, crossingId, start),
                Assert.Single(control.BridgeTraffic.InProgress, item => item.AgentId == TrafficAgentId));
            Assert.DoesNotContain(control.BridgeTraffic.Completed, item => item.AgentId == TrafficAgentId);
            if (position == water[1]) midstream ??= control.ExportState();
        }
        Assert.Equal([3, 3], water.Select(tile => ticksAt.GetValueOrDefault(tile)));
        Assert.Equal(farBank, control.Inhabitants.Single(item => item.InhabitantId == TrafficAgentId).Position);
        Assert.Equal(crossingId, Assert.Single(control.BridgeTraffic.Completed, item => item.AgentId == TrafficAgentId).CrossingId);
        Assert.Empty(control.Bridges);

        // An open wade saved midstream survives save and load, and the
        // reloaded world finishes the crossing exactly as the live one did.
        Assert.NotNull(midstream);
        var saved = PrivateWorldRuntimeCodec.Encode(midstream);
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(midstream.BridgeTraffic!.InProgress, reloaded.BridgeTraffic.InProgress);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        while (reloaded.WorldTick < control.WorldTick)
            Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(control.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));

        // With five earlier crossings by the founders, this sixth wade by a
        // second agent bridges both water tiles at once, adding no Road.
        using var busy = await TrafficWorldAsync(seedEvidence: true, crossingId, start);
        var roads = busy.RoadTiles;
        for (var tick = 0; tick < 20 && busy.Bridges.Count == 0; tick++)
            Assert.True((await busy.AdvanceOneTickAsync()).Advanced);
        var bridge = Assert.Single(busy.Bridges);
        Assert.Equal((crossingId, BridgeTriggers.Traffic, BridgeDesigns.PlankSpanTwo, (string?)null),
            (bridge.Id, bridge.Trigger, bridge.Design, bridge.RouteId));
        Assert.Equal(water, bridge.Span);
        Assert.Equal([start, farBank], bridge.Entrances);
        Assert.Equal(roads, busy.RoadTiles);
        Assert.DoesNotContain(busy.BridgeTraffic.Completed, item => item.CrossingId == crossingId);
        Assert.Contains(busy.ExportState().Events, item => item.Kind == "bridge_built" && item.Detail == $"traffic:{crossingId}");

        // The bridge is walked end to end at dry-ground speed, also after a reload.
        using var bridged = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(busy.ExportState())));
        var map = bridged.ExportState().Map;
        Assert.Equal([crossingId], bridged.Bridges.Select(item => item.Id));
        Assert.All(water, tile => Assert.Equal(100, map.FootTravelCost(tile)));
        Assert.Equal(3 * 100, map.FootStepCost(start, water[0]) + map.FootStepCost(water[0], water[1]) +
            map.FootStepCost(water[1], farBank));
    }

    private const string TrafficAgentId = "agent:00000000000000000000000000000099";

    [Fact]
    public async Task PlantingAtATrafficBridgeEntranceKeepsTheSeedAndTheCheckpoint()
    {
        using var setup = await TrafficWorldAsync(seedEvidence: false);
        var state = setup.ExportState();
        var blocked = state.Map.Resources.Select(resource => resource.Position)
            .Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(setup.RoadTiles)
            .Concat(setup.WorldSimulation.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(setup.WorldContent.Buildings.Single(
                    definition => definition.CanonicalId == building.DefinitionId), building.Position)))
            .ToHashSet();
        RiverCrossing? crossing = null;
        foreach (var tile in state.Map.Tiles)
        {
            foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
            {
                if (!RiverBridgeRules.TryFindCrossing(state.Map, tile.Position, dx, dy, out var candidate) ||
                    candidate!.Span.Count != 1 || candidate.Entrances.Any(blocked.Contains) ||
                    TreeGrowthRules.GroundRefusal(state.Map, candidate.EntranceA) is not null) continue;
                crossing = candidate;
                break;
            }
            if (crossing is not null) break;
        }
        Assert.NotNull(crossing);
        var bridge = RiverBridgeRules.ToBridge(crossing, BridgeTriggers.Traffic, setup.WorldTick, null);
        Assert.DoesNotContain(crossing.EntranceA, setup.RoadTiles);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "bridge-tree-seed", TreeGrowthRules.TreeSeedItem, TrafficAgentId, 1);
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Bridges = [bridge],
            BridgeTraffic = BridgeTrafficState.Empty,
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == TrafficAgentId
                ? person with { Position = crossing.EntranceA } : person).ToArray(),
        });
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());

        var result = world.PlantTree(TrafficAgentId, TreeGrowthRules.Broadleaf,
            "bridge-tree-seed", crossing.EntranceA);

        Assert.False(result.Planted);
        Assert.Equal(TreePlantingRefusal.Road, result.Refusal);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.Validate();
    }

    private static async Task<PrivateWorldRuntime> TrafficWorldAsync(bool seedEvidence,
        string crossingId = "bridge-102-19-ns-1", GridPoint? start = null)
    {
        var geography = new GeographyOptions("traffic-bridge-0", WorldSizePreset.Small);
        using var setup = new PrivateWorldRuntime(geography.Seed, startPace: WorldStartPace.FounderSetup,
            geographyOptions: geography);
        setup.InitializeFirstTownContent();
        setup.AcceptFirstTownLayout(setup.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position);
        var initialMap = setup.ExportState().Map;
        var definitions = setup.WorldContent.Buildings.ToDictionary(item => item.CanonicalId);
        var occupied = initialMap.Resources.Select(item => item.Position)
            .Concat(initialMap.CampObjects.Select(item => item.Position))
            .Concat(setup.RoadTiles)
            .Concat(setup.WorldSimulation.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(definitions[building.DefinitionId], building.Position)))
            .ToHashSet();
        var founders = initialMap.Tiles.Select(tile => tile.Position)
            .Where(point => initialMap.IsBuildable(point) && !occupied.Contains(point))
            .OrderBy(point => Math.Abs(point.X - 128) + Math.Abs(point.Y - 60))
            .ThenBy(point => point.Y).ThenBy(point => point.X)
            .Take(PrivateWorldRuntime.RequiredFounders).ToArray();
        Assert.Equal(PrivateWorldRuntime.RequiredFounders, founders.Length);
        for (var index = 0; index < founders.Length; index++)
            setup.PlaceFounder($"founder:0000000000000000000000000000000{index + 1}", founders[index]);
        setup.StartWorld();
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var map = setup.ExportState().Map;
        start ??= new GridPoint(102, 18);
        Assert.True(RiverBridgeRules.TryResolve(map, crossingId, out var crossing));
        Assert.Contains(start.Value, crossing!.Entrances);
        setup.AddAgent(TrafficAgentId, start.Value);
        var state = setup.ExportState();
        // Five earlier crossings by the founders at this crossing; the added
        // agent's real wade is the sixth, by a second distinct agent.
        BridgeTrafficCrossing[] earlier =
        [
            new(crossing.Id, "founder:00000000000000000000000000000001", 0),
            new(crossing.Id, "founder:00000000000000000000000000000001", 1),
            new(crossing.Id, "founder:00000000000000000000000000000002", 0),
            new(crossing.Id, "founder:00000000000000000000000000000003", 0),
            new(crossing.Id, "founder:00000000000000000000000000000004", 1),
        ];
        return PrivateWorldRuntime.Restore(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == TrafficAgentId
                ? person with { HungerBasisPoints = 3_000 } : person).ToArray(),
            BridgeTraffic = seedEvidence ? new BridgeTrafficState([], earlier) : state.BridgeTraffic,
        });
    }

    internal static async Task<PrivateWorldRuntime> GrowthWorldAsync(string seed = GrowthSeed, GridPoint? townSite = null)
    {
        var geography = new GeographyOptions(seed, WorldSizePreset.Small);
        var world = new PrivateWorldRuntime(geography.Seed, startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        world.AcceptFirstTownLayout(townSite ?? GrowthTownSite);
        var map = world.ExportState().Map;
        var origin = Assert.Single(world.Towns).OriginSite!.Value;
        var buildingTiles = world.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(
                world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building.Position))
            .ToHashSet();
        var positions = map.Tiles.Select(tile => tile.Position).Where(point =>
                Math.Abs(point.X - origin.X) <= 5 && Math.Abs(point.Y - origin.Y) <= 5 &&
                map.IsBuildable(point) && !buildingTiles.Contains(point) &&
                !map.Resources.Any(item => item.Position == point))
            .Take(PrivateWorldRuntime.RequiredFounders).ToArray();
        for (var index = 0; index < positions.Length; index++)
            world.PlaceFounder($"founder:0000000000000000000000000000000{index + 1}", positions[index]);
        world.StartWorld();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return world;
    }

    private static Dictionary<string, int> Totals(PrivateWorldRuntimeState state) =>
        state.Society.Society.Inventory.Lots.GroupBy(lot => lot.ItemKind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(lot => lot.Quantity), StringComparer.Ordinal);
}
