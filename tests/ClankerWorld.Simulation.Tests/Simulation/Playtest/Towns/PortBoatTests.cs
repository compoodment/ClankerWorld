using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PortBoatTests
{
    private static readonly Lazy<Task<byte[]>> CompletedBoat = new(BuildBoatAsync);

    [Theory]
    [InlineData(PortFacing.North, 4, 7, 5, 7, 4, 8, 5, 8)]
    [InlineData(PortFacing.East, 4, 4, 4, 5, 3, 4, 3, 5)]
    [InlineData(PortFacing.South, 4, 4, 5, 4, 4, 3, 5, 3)]
    [InlineData(PortFacing.West, 7, 4, 7, 5, 8, 4, 8, 5)]
    public void EachOrientationRequiresItsLandEndAndClearSideWater(PortFacing facing,
        int ax, int ay, int bx, int by, int cx, int cy, int dx, int dy)
    {
        GridPoint[] land = [new(ax, ay), new(bx, by)];
        GridPoint[] approach = [new(cx, cy), new(dx, dy)];
        var map = SmallMap(12, 12, land.Concat(approach).ToHashSet());
        var definition = PortDefinitions().Single(item => PortNavigationRules.Facing(item) == facing);
        var geometry = PortNavigationRules.Geometry(map, definition, new(4, 4));
        Assert.Equal(land, geometry.LandTiles);
        Assert.Equal(land[0], geometry.WorkPosition);
        Assert.Equal(6, geometry.WaterTiles.Count);
        Assert.Equal(6, geometry.DockingTiles.Count);
        Assert.Empty(geometry.DockingTiles.Intersect(WorldContentSimulationRules.Footprint(definition, new GridPoint(4, 4))));
        Assert.True(PortNavigationRules.Fits(map, definition, new(4, 4), new HashSet<GridPoint>(), out _));
        Assert.False(PortNavigationRules.Fits(map, definition, new(4, 4), new HashSet<GridPoint> { geometry.DockingTiles[0] }, out _));
        Assert.False(PortNavigationRules.Fits(map, definition, new(4, 4), approach.ToHashSet(), out _));
        Assert.True(PortNavigationRules.Fits(map, definition, new(4, 4), approach.ToHashSet(), out _, new HashSet<GridPoint> { approach[0] }));
    }

    [Fact]
    public void ConnectedWaterUsesTheOptionalSeamButCannotJumpDiagonalLand()
    {
        var seam = SmallMap(8, 5, Enumerable.Range(0, 8).SelectMany(x => Enumerable.Range(0, 5)
            .Select(y => new GridPoint(x, y))).Where(point => point != new GridPoint(0, 2) && point != new GridPoint(7, 2)).ToHashSet());
        Assert.Empty(PortNavigationRules.WaterRoute(seam, new(0, 2), [new(7, 2)], new HashSet<GridPoint>()));
        Assert.Equal([new GridPoint(0, 2), new(7, 2)], PortNavigationRules.WaterRoute(seam with { WrapsEastWest = true },
            new(0, 2), [new(7, 2)], new HashSet<GridPoint>()));
        var diagonal = SmallMap(4, 4, Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 4)
            .Select(y => new GridPoint(x, y))).Where(point => point != new GridPoint(1, 1) && point != new GridPoint(2, 2)).ToHashSet());
        Assert.Empty(PortNavigationRules.WaterRoute(diagonal, new(1, 1), [new(2, 2)], new HashSet<GridPoint>()));
    }

    [Fact]
    public async Task NormalDeliveryAndProductionCreateOneTownAssetWithConsumedPhysicalInputs()
    {
        using var world = await DockedWorldAsync();
        var boat = Assert.Single(world.Boats);
        var state = world.ExportState();
        var job = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(WorldProductionJobState.Completed, job.State);
        Assert.Equal(job.JobId, boat.BuildJobId);
        Assert.Equal("port-one", boat.DockedPortId);
        var reservations = job.InputReservationIds.Select(state.Society.Society.Inventory.GetReservation).ToArray();
        Assert.Equal(12, reservations.Sum(item => item.Quantity));
        Assert.All(reservations, item => { Assert.Equal(boat.TownId, item.OwnerId); Assert.Equal(InventoryReservationState.Completed, item.State); });
        Assert.Equal(InventoryReservationState.Completed, state.Society.Society.Inventory.GetReservation(job.JobId + ":launch").State);
        Assert.Contains(state.Events, item => item.Kind == "communal_port_supplied");
        Assert.Contains(state.Events, item => item.Kind == "communal_boat_launched");
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(boat.Id, Assert.Single(world.Boats).Id);
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Boats);
        Assert.Equal((boat.Id, boat.Position.X, boat.Position.Y), (projected.Id, projected.Position.X, projected.Position.Y));
    }

    [Fact]
    public async Task SavedPassengerAndWholeFilledJugReachTheSamePortOnReplay()
    {
        using var docked = await DockedWorldAsync();
        var state = WithJug(docked.ExportState());
        var actor = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, _ => new Pick());
        var boat = Assert.Single(world.Boats);
        Assert.True(world.StartBoatJourney(boat.Id, actor, "port-two").Applied);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new Pick());
        for (var tick = 0; tick < 100 && world.Boats[0].Journey is not null; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal("port-two", world.Boats[0].DockedPortId);
        Assert.Null(world.Boats[0].Journey);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var inventory = restored.ExportState().Society.Society.Inventory;
        Assert.Equal((actor, 1, (InventoryGroundPosition?)null), (inventory.GetLot("aboard-jug").OwnerId, inventory.GetLot("aboard-jug").Quantity, inventory.GetLot("aboard-jug").GroundPosition));
        Assert.Equal((actor, 2, "aboard-jug"), (inventory.GetLot("aboard-water").OwnerId, inventory.GetLot("aboard-water").Quantity, inventory.GetLot("aboard-water").ContainerLotId));
        restored.Validate();
    }

    [Fact]
    public async Task VisitorPermissionIsRequiredAndRevocationWaitsForTheNextDeparture()
    {
        using var docked = await DockedWorldAsync();
        var state = docked.ExportState();
        const string visitor = "agent:00000000000000000000000000000999";
        var outside = state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) &&
            state.Towns!.All(town => !town.BorderTiles.Contains(tile.Position)) && state.Inhabitants.All(person => person.Position != tile.Position))
            .OrderBy(tile => state.Map.FootDistance(state.Inhabitants[0].Position, tile.Position)).First().Position;
        docked.AddAgent(visitor, outside);
        state = docked.ExportState();
        var boat = Assert.Single(state.BoatTransport!.Boats);
        var origin = Geometry(state, "port-one");
        var other = state.Map.FootNeighbors(origin.WorkPosition).First(point => state.Map.IsPassable(point) &&
            !origin.LandTiles.Contains(point) && state.Inhabitants.All(person => person.Position != point));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == visitor
            ? person with { Position = origin.WorkPosition, HungerBasisPoints = 10_000 }
            : person.InhabitantId == state.Inhabitants[0].InhabitantId ? person with { Position = other } : person).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new Pick());
        Assert.False(world.StartBoatJourney(boat.Id, visitor, "port-two").Applied);
        Assert.True(world.SetBoatGuestPermission(boat.TownId, visitor, true).Applied);
        Assert.True(world.StartBoatJourney(boat.Id, visitor, "port-two").Applied);
        Assert.True(world.SetBoatGuestPermission(boat.TownId, visitor, false).Applied);
        for (var tick = 0; tick < 100 && world.Boats[0].Journey is not null; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("port-two", world.Boats[0].DockedPortId);
        Assert.False(world.StartBoatJourney(boat.Id, visitor, "port-one").Applied);
        world.Validate();
    }

    [Fact]
    public async Task ActualLifecycleDeathKeepsItsFilledVesselEstateWithTheMovingBoat()
    {
        using var docked = await DockedWorldAsync();
        var state = WithJug(docked.ExportState());
        var actor = state.Inhabitants[0].InhabitantId;
        var society = state.Society.Society;
        // Retain an elder's valid birthday, placing their sixtieth day on the next tick.
        society = society with
        {
            Inhabitants = society.Inhabitants.Select(person => person.Id == actor ? person with
            {
                BirthTick = society.WorldTick - 60L * society.Config.TicksPerWorldDay + 1,
                BirthLifeTick = person.BirthLifeTick is null ? null : society.LifeTickAt(society.WorldTick) - 60L * society.Config.TicksPerWorldDay + 1,
                AgeBand = SocietyAgeBand.Elder,
                LastLifecycleYearChecked = 59
            } : person).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = society } }, _ => new Pick());
        var boat = Assert.Single(world.Boats);
        Assert.True(world.StartBoatJourney(boat.Id, actor, "port-two").Applied);
        for (var tick = 0; tick < 5; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        state = world.ExportState();
        var estate = Assert.Single(state.Society.Society.Estates, item => item.DeceasedId == actor);
        Assert.Equal(boat.Id, Assert.Single(state.DeceasedInhabitants!, item => item.InhabitantId == actor).BoatIdAtDeath);
        Assert.Equal(estate.Id, state.Society.Society.Inventory.GetLot("aboard-jug").OwnerId);
        Assert.Equal(estate.Id, state.Society.Society.Inventory.GetLot("aboard-water").OwnerId);
        Assert.Equal(new InventoryGroundPosition(world.Boats[0].Position.X, world.Boats[0].Position.Y), state.Society.Society.Inventory.GetLot("aboard-jug").GroundPosition);
        Assert.Equal(state.Society.Society.Inventory.GetLot("aboard-jug").GroundPosition, state.Society.Society.Inventory.GetLot("aboard-water").GroundPosition);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new Pick());
        restored.Validate();
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(state), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var heir = state.Society.Society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active).Id;
        var inheritance = SocietyFixture.MarkWillStarted(state.Society.Society, estate.Id).Checkpoint;
        inheritance = SocietyFixture.ResolveWill(inheritance, estate.Id, heir, "accepted").Checkpoint;
        // Accelerate only the genuine estate's escrow deadline, preserving its will and stock.
        inheritance = inheritance with
        {
            Estates = inheritance.Estates.Select(item => item.Id == estate.Id
            ? item with { ExpiryTick = inheritance.WorldTick + 1 } : item).ToArray()
        };
        using var collecting = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = inheritance } },
            id => new Pick(id == heir ? "boat_estate_pickup:aboard-jug" : null));
        for (var tick = 0; tick < 160 && collecting.Society.Inventory.GetLot("aboard-jug").GroundPosition is not null; tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        Assert.Equal((heir, (InventoryGroundPosition?)null), (collecting.Society.Inventory.GetLot("aboard-jug").OwnerId,
            collecting.Society.Inventory.GetLot("aboard-jug").GroundPosition));
        var water = collecting.Society.Inventory.GetLot("aboard-water");
        Assert.Equal((heir, 2, "aboard-jug", (InventoryGroundPosition?)null), (water.OwnerId, water.Quantity, water.ContainerLotId, water.GroundPosition));
        Assert.Equal("port-two", collecting.Boats[0].DockedPortId);
        Assert.Contains(Geometry(collecting.ExportState(), "port-two").LandTiles,
            point => collecting.ExportState().Map.FootDistance(collecting.Inhabitants.Single(person => person.InhabitantId == heir).Position, point) <= 1);
        collecting.Validate();
        _ = PrivateWorldRuntimeCodec.Encode(collecting.ExportState());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedLandingWaitsOneDayAndReturnsOnlyToAFreeOrigin(bool originBlocked)
    {
        using var docked = await DockedWorldAsync();
        var state = ShortDays(WithJug(docked.ExportState()));
        var actor = state.Inhabitants[0].InhabitantId;
        using var departure = PrivateWorldRuntime.Restore(state, _ => new Pick());
        var boat = Assert.Single(departure.Boats);
        Assert.True(departure.StartBoatJourney(boat.Id, actor, "port-two").Applied);
        var origin = Geometry(state, "port-one");
        const string extraBlocker = "agent:00000000000000000000000000000888";
        if (originBlocked) departure.AddAgent(extraBlocker, origin.LandTiles[1]);
        state = departure.ExportState();
        var destination = Geometry(state, "port-two");
        var blockers = state.Inhabitants.Where(person => person.InhabitantId != actor && person.InhabitantId != extraBlocker)
            .Select(person => person.InhabitantId).ToArray();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person =>
            {
                var index = Array.IndexOf(blockers, person.InhabitantId);
                return index is 0 or 1 ? person with { Position = destination.LandTiles[index] }
                    : index == 2 && originBlocked ? person with { Position = origin.LandTiles[0] } : person;
            }).ToArray()
        };
        using var waiting = PrivateWorldRuntime.Restore(state, _ => new Pick());
        for (var tick = 0; tick < 100 && waiting.Boats[0].Journey!.WaitingSinceTick is null; tick++)
            Assert.True((await waiting.AdvanceOneTickAsync()).Advanced);
        var since = Assert.IsType<long>(waiting.Boats[0].Journey!.WaitingSinceTick);
        var day = waiting.Society.Config.TicksPerWorldDay;
        var waitingPosition = waiting.Boats[0].Position;
        while (waiting.WorldTick < since + day - 1)
        {
            Assert.True((await waiting.AdvanceOneTickAsync()).Advanced);
            Assert.False(waiting.Boats[0].Journey!.Returning);
            Assert.Equal(waitingPosition, waiting.Boats[0].Position);
        }
        if (originBlocked)
        {
            for (var tick = 0; tick < day + 3; tick++) Assert.True((await waiting.AdvanceOneTickAsync()).Advanced);
            Assert.False(waiting.Boats[0].Journey!.Returning);
            Assert.Equal(waitingPosition, waiting.Boats[0].Position);
            Assert.Equal(actor, waiting.Boats[0].Journey!.PassengerId);
            state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(waiting.ExportState()));
            var free = state.Map.Tiles.Where(tile => state.Map.IsPassable(tile.Position) && state.Inhabitants.All(person => person.Position != tile.Position) &&
                !origin.LandTiles.Contains(tile.Position) && !destination.LandTiles.Contains(tile.Position))
                .OrderBy(tile => state.Map.FootDistance(origin.WorkPosition, tile.Position)).Take(2).Select(tile => tile.Position).ToArray();
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == blockers[2]
                ? person with { Position = free[0] } : person.InhabitantId == extraBlocker ? person with { Position = free[1] } : person).ToArray()
            };
        }
        else state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(waiting.ExportState()));
        using var returning = PrivateWorldRuntime.Restore(state, _ => new Pick());
        for (var tick = 0; tick < 100 && returning.Boats[0].Journey is not null; tick++)
            Assert.True((await returning.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("port-one", returning.Boats[0].DockedPortId);
        Assert.Contains(returning.ExportState().Events, item => item.Kind == "boat_return_started" && item.WorldTick >= since + day);
        Assert.Contains(returning.ExportState().Events, item => item.Kind == "boat_returned");
        var inventory = returning.Society.Inventory;
        Assert.Equal(actor, inventory.GetLot("aboard-jug").OwnerId);
        Assert.Equal((actor, 2, "aboard-jug"), (inventory.GetLot("aboard-water").OwnerId, inventory.GetLot("aboard-water").Quantity,
            inventory.GetLot("aboard-water").ContainerLotId));
        returning.Validate();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("job")]
    [InlineData("launch")]
    [InlineData("passenger")]
    public async Task CorruptBoatRecordsAreRefused(string defect)
    {
        using var world = await DockedWorldAsync();
        var state = world.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var boat = Assert.Single(world.Boats);
        if (defect == "passenger")
        {
            Assert.True(world.StartBoatJourney(boat.Id, actor, "port-two").Applied);
            state = world.ExportState();
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = Geometry(state, "port-one").WorkPosition } : person).ToArray()
            };
        }
        else if (defect == "launch")
        {
            state = state with
            {
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Inventory = state.Society.Society.Inventory with
                        {
                            Reservations = state.Society.Society.Inventory.Reservations
                    .Where(item => item.Id != boat.BuildJobId + ":launch").ToArray()
                        }
                    }
                }
            };
        }
        else state = state with
        {
            BoatTransport = state.BoatTransport! with
            {
                Boats = defect switch
                {
                    "duplicate" => [boat, boat],
                    "missing" => [null!],
                    _ => [boat with { BuildJobId = "absent" }]
                }
            }
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state));
    }

    private static PrivateWorldRuntimeState ShortDays(PrivateWorldRuntimeState state)
    {
        var society = state.Society.Society;
        var config = society.Config with { TicksPerWorldDay = 12 };
        var inhabitants = society.Inhabitants.Select(person =>
        {
            var age = society.Config.AgeAt(person.BirthLifeTick ?? person.BirthTick,
                person.BirthLifeTick is null ? society.WorldTick : society.LifeTickAt(society.WorldTick));
            return person with
            {
                BirthTick = society.WorldTick - (long)age * config.TicksPerLifecycleAge,
                BirthLifeTick = person.BirthLifeTick is null ? null : society.LifeTickAt(society.WorldTick) - (long)age * config.TicksPerLifecycleAge,
                LastLifecycleYearChecked = age
            };
        }).ToArray();
        var worldConfig = state.WorldSystems!.Config with { TicksPerDay = config.TicksPerWorldDay };
        var calendar = WorldCalendarRules.FromTick(society.WorldTick, worldConfig);
        return state with
        {
            Society = state.Society with { Society = society with { Config = config, Inhabitants = inhabitants } },
            WorldSystems = state.WorldSystems with
            {
                Config = worldConfig,
                RegionalWeather = null,
                Climate = new(society.WorldTick, calendar.Season, WeatherRules.WeatherForDay(state.WorldSeed, calendar.DayIndex, calendar.Season, worldConfig))
            }
        };
    }

    private static SeededMap SmallMap(int width, int height, HashSet<GridPoint> land) => new(width, height, 0,
        Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width).Select(x => new GridPoint(x, y)))
            .Select(point => new TerrainTile(point, land.Contains(point) ? TerrainKind.Meadow : TerrainKind.Ocean)).ToArray(), [], [], "toy-map");

    private static BuildingDefinition[] PortDefinitions() => ContentDefinitionPayloadCodec.ApplyPackage(
        new DeclarativeWorldContentState([], []), PortContent.Create()).Buildings.ToArray();

    private static PortGeometry Geometry(PrivateWorldRuntimeState state, string id)
    {
        var building = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == id);
        return PortNavigationRules.Geometry(state.Map, state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building.Position);
    }

    private static PrivateWorldRuntimeState WithJug(PrivateWorldRuntimeState state)
    {
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "aboard-jug", "water_jug", actor, 1, containerCapacity: 8);
        inventory = InventoryFixture.AddLot(inventory, "aboard-water", "water", actor, 2, containerLotId: "aboard-jug");
        return state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    }

    private static async Task<PrivateWorldRuntime> DockedWorldAsync() => PrivateWorldRuntime.Restore(
        PrivateWorldRuntimeCodec.Decode(await CompletedBoat.Value), _ => new Pick());

    private static async Task<byte[]> BuildBoatAsync()
    {
        using var seed = new PrivateWorldRuntime("probe-a", _ => new Pick(), startPace: WorldStartPace.FounderSetup,
            geographyOptions: new GeographyOptions("probe-a", WorldSizePreset.Small));
        var map = seed.ExportState().Map;
        var anchor = map.Resources.Single(item => item.Id == "berry-patch").Position;
        seed.InitializeFirstTownContent(); seed.AcceptFirstTownLayout(anchor);
        var occupied = map.Resources.Select(item => item.Position).Concat(seed.RoadTiles).Concat(seed.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(seed.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))).ToHashSet();
        var founders = map.Tiles.Where(tile => Math.Abs(tile.Position.X - anchor.X) <= 5 && Math.Abs(tile.Position.Y - anchor.Y) <= 5 &&
            map.IsBuildable(tile.Position) && !occupied.Contains(tile.Position)).Take(4).Select(tile => tile.Position).ToArray();
        for (var index = 0; index < 4; index++) seed.PlaceFounder($"founder:{index + 1:D32}", founders[index]);
        seed.StartWorld();
        var definitions = seed.WorldContent.Buildings.Where(PortNavigationRules.IsPort).ToArray();
        var sites = map.Tiles.SelectMany(tile => definitions.Select(definition => (Definition: definition, Position: tile.Position)))
            .Where(site => PortNavigationRules.Fits(map, site.Definition, site.Position, occupied, out _))
            .OrderBy(site => map.FootDistance(anchor, site.Position)).Take(80).ToArray();
        var first = sites[0];
        var firstGeometry = PortNavigationRules.Geometry(map, first.Definition, first.Position);
        occupied.UnionWith(firstGeometry.LandTiles.Concat(firstGeometry.WaterTiles).Concat(firstGeometry.DockingTiles));
        var firstBerth = firstGeometry.DockingTiles.First(dock => firstGeometry.LandTiles.Any(land => map.FootDistance(land, dock) == 1));
        var second = sites.First(site => PortNavigationRules.Fits(map, site.Definition, site.Position, occupied, out _) &&
            PortNavigationRules.WaterRoute(map, firstBerth, PortNavigationRules.Geometry(map, site.Definition, site.Position).DockingTiles,
                firstGeometry.LandTiles.Concat(firstGeometry.WaterTiles).ToHashSet()).Count > 8);
        var state = seed.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "port-wood", "wood", household, 32);
        inventory = InventoryFixture.AddLot(inventory, "port-stone", "stone", household, 8);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var placing = PrivateWorldRuntime.Restore(state, _ => new Pick());
        Assert.True(placing.PlacePort("port-one", first.Definition.CanonicalId, first.Position, actor).Applied);
        Assert.True(placing.PlacePort("port-two", second.Definition.CanonicalId, second.Position, actor).Applied);
        state = placing.ExportState(); inventory = state.Society.Society.Inventory;
        foreach (var item in new[] { ("wood", 8), ("rope", 2), ("iron", 2) }) inventory = InventoryFixture.AddLot(inventory, "boat-" + item.Item1, item.Item1, actor, item.Item2);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = firstGeometry.WorkPosition,
                HungerBasisPoints = 10_000,
                LastDecisionContext = null
            } : person).ToArray()
        };
        using var building = PrivateWorldRuntime.Restore(state, id => new Pick(id == actor ? "port_supply:port-one" : null));
        for (var tick = 0; tick < 70 && building.Boats.Count == 0; tick++) Assert.True((await building.AdvanceOneTickAsync()).Advanced);
        Assert.Single(building.Boats);
        return PrivateWorldRuntimeCodec.Encode(building.ExportState());
    }

    private sealed class Pick(string? prefix = null) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = prefix is not null ? request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal)) ??
                request.Observation.Candidates.FirstOrDefault(item => item.Id == "port_build_boat:port-one") : null;
            selected ??= request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with { Observation = request.Observation with { Candidates = [selected] } }, cancellationToken);
        }
    }
}
