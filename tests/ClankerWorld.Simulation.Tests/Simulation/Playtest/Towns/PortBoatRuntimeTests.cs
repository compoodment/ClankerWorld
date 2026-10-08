using System.Collections.Concurrent;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;
using Client = ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PortBoatRuntimeTests
{
    private static readonly Lazy<Task<byte[]>> PaidBoat = new(() => BuildPaidBoatAsync());
    private static readonly Lazy<Task<byte[]>> Underway = new(StartVoyageAsync);
    private static readonly string[] ReservedKinds = ["wood", "stone", "rope", "iron"];
    private const string Follower = "founder:00000000000000000000000000000002";
    private const string Visitor = "agent:00000000000000000000000000000010";
    private static readonly string[] Blockers = ["agent:00000000000000000000000000000011", "agent:00000000000000000000000000000012",
        "agent:00000000000000000000000000000013", "agent:00000000000000000000000000000014"];

    [Fact]
    public async Task CouncilProjectsBuildPortsAndOnePhysicalBoatUsingUnreservedTownStock()
    {
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value));
        var town = Assert.Single(world.Towns);
        var boat = Assert.Single(world.Boats);
        Assert.Equal(town.Id, boat.TownId);
        Assert.Equal(3, town.Projects.Count);
        Assert.All(town.Projects, project =>
        {
            Assert.Equal("completed", project.Stage);
            var proposal = town.Governance!.Proposals.Single(proposal => proposal.Id == project.ProposalId);
            Assert.Equal("passed", proposal.Status);
            Assert.True(proposal.Votes.Count(vote => vote.Yes) >= proposal.RequiredYes);
            Assert.All(project.Deliveries, delivery => Assert.Equal(InventoryReservationState.Completed,
                world.Society.Inventory.GetReservation(delivery.ReservationId!).State));
        });
        Assert.Equal(boat.Id, town.Projects.Single(project => project.Plan.BoatPortId is not null).CompletedBoatId);
        Assert.All(ReservedKinds, kind =>
        {
            var reservation = world.Society.Inventory.GetReservation("unrelated-" + kind);
            Assert.Equal(InventoryReservationState.Reserved, reservation.State);
            Assert.Equal(reservation.Quantity, world.Society.Inventory.GetLot(reservation.LotId).Quantity);
        });
        Assert.False(world.PlaceBuilding("unpaid-port", PortContent.Definitions[0].CanonicalId, new(1, 1)).Applied);
        world.Validate();
    }

    [Fact]
    public async Task PersonalTripMovesOneBoatPassengerAndCargoAcrossMidVoyageReload()
    {
        var policy = new BoatPolicy();
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await Underway.Value), policy);
        var traveler = BoatPolicy.Author;
        var before = scenario.World.Society.Inventory.Lots.Where(lot => PersonalEquipmentRules.IsCarried(lot, traveler))
            .Select(lot => (lot.Id, lot.OwnerId, lot.ItemKind, lot.Quantity)).ToArray();
        var membership = scenario.World.Towns.Select(town => (town.Id, Members: town.ResidentIds.ToArray())).ToArray();
        var boat = scenario.World.Boats[0];
        Assert.Equal(traveler, boat.Journey!.PassengerId);
        Assert.Equal(boat.Position, scenario.World.Inhabitants.Single(person => person.InhabitantId == traveler).Position);
        Assert.Single(scenario.World.BoatRequests, request => request.Status == "underway");
        Assert.Contains(boat.Journey.ReservedDock, PortNavigationRules.Geometry(scenario.World.ExportState().Map,
            scenario.World.WorldContent.Buildings.Single(definition => definition.CanonicalId == scenario.World.WorldSimulation.Buildings
                .Single(port => port.InstanceId == boat.Journey.DestinationPortId).DefinitionId),
            scenario.World.WorldSimulation.Buildings.Single(port => port.InstanceId == boat.Journey.DestinationPortId).Position).DockingTiles);
        var snapshot = new OwnerWorldObservationStore(scenario.World).GetSnapshot();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<Client.OwnerWorldSnapshot>(JsonSerializer.Serialize(snapshot, options), options)!;
        var displayed = Assert.Single(client.Boats);
        Assert.Equal((boat.Id, boat.TownId, boat.Position.X, boat.Position.Y, traveler),
            (displayed.Id, displayed.TownId, displayed.Position.X, displayed.Position.Y, displayed.PassengerId));
        Assert.Equal("underway", displayed.Status);
        Assert.Equal(boat.Journey.ReservedDock.X, displayed.ReservedDock!.X);
        Assert.Equal("boat_travel", client.Inhabitants.Single(person => person.Id == traveler).Route.Status);
        Assert.Equal("underway", Assert.Single(client.BoatRequests).Status);
        Assert.Contains("Passenger:", Client.GameUiText.BoatDescription(displayed), StringComparison.Ordinal);
        policy.Trips = false;
        var saved = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        Assert.False((await scenario.World.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), policy.CreateProvider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var step = 0; scenario.World.Boats[0].Journey is not null && step < 160; step++)
        {
            await scenario.World.AdvanceOneTickAsync();
            await replay.AdvanceOneTickAsync();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Null(scenario.World.Boats[0].Journey);
        Assert.Equal("arrived", Assert.Single(scenario.World.BoatRequests).Status);
        foreach (var (id, members) in membership) Assert.Equal(members, scenario.World.Towns.Single(town => town.Id == id).ResidentIds);
        // Meals may be consumed aboard; other carried ownership and quantities stay intact.
        foreach (var expected in before.Where(lot => lot.ItemKind != "food"))
        {
            var actual = scenario.World.Society.Inventory.GetLot(expected.Id);
            Assert.Equal((expected.OwnerId, expected.ItemKind, expected.Quantity), (actual.OwnerId, actual.ItemKind, actual.Quantity));
            Assert.True(PersonalEquipmentRules.IsCarried(actual, traveler));
        }
        scenario.World.Validate();
    }

    [Theory]
    [InlineData(PortFacing.North)]
    [InlineData(PortFacing.East)]
    [InlineData(PortFacing.South)]
    [InlineData(PortFacing.West)]
    public void EveryRotationRequiresLandApproachAndSixClearWaterDocks(PortFacing facing)
    {
        using var world = new PrivateWorldRuntime("probe-a", startPace: WorldStartPace.FounderSetup,
            geographyOptions: new GeographyOptions("probe-a", WorldSizePreset.Small));
        var map = world.ExportState().Map;
        var definition = PortContent.Definitions.Single(definition => PortNavigationRules.Facing(definition) == facing);
        var site = map.Tiles.Select(tile => tile.Position).First(site => PortNavigationRules.Fits(map, definition, site, new HashSet<GridPoint>(), out _));
        var geometry = PortNavigationRules.Geometry(map, definition, site);
        Assert.Equal(2, geometry.LandTiles.Count);
        Assert.Equal(6, geometry.WaterTiles.Count);
        Assert.Equal(6, geometry.DockingTiles.Distinct().Count());
        foreach (var dock in geometry.DockingTiles)
            Assert.False(PortNavigationRules.Fits(map, definition, site, new HashSet<GridPoint> { dock }, out _));
        Assert.False(PortNavigationRules.Fits(map, definition, site, geometry.ApproachTiles.ToHashSet(), out _));
    }

    [Fact]
    public async Task PaidPortCompletionKeepsTheCouncilApprovedApproachWhenAnotherEdgeHasARoad()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await BuildPaidBoatAsync(stopBeforeFirstPort: true));
        var project = Assert.Single(state.Towns![0].Projects);
        var definition = PortContent.Definitions.Single(item => item.CanonicalId == project.Plan.DefinitionId);
        var otherApproach = PortNavigationRules.Geometry(state.Map, definition, project.Plan.Site).ApproachTiles
            .Single(point => point != project.Plan.Entrance);
        state = state with
        {
            RoadTiles = state.RoadTiles!.Where(point => point != project.Plan.Entrance)
            .Append(otherApproach).Distinct().OrderBy(point => point.Y).ThenBy(point => point.X).ToArray()
        };
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), new() { Build = true });
        await scenario.UntilAsync(() => scenario.World.Towns[0].Projects.Single().Stage == "completed", 40);
        var completed = scenario.World.Towns[0].Projects.Single();
        Assert.Equal(project.Plan.Entrance, scenario.World.WorldSimulation.Buildings.Single(building => building.InstanceId == completed.CompletedBuildingId).Entrance);
        scenario.World.Validate();
    }

    [Fact]
    public void APortFootprintCannotCoverACompletedPortsDockingSpace()
    {
        var land = new HashSet<GridPoint> { new(10, 13), new(11, 13), new(10, 14), new(11, 14),
            new(15, 11), new(15, 12), new(16, 11), new(16, 12) };
        var map = new SeededMap(32, 32, 0, Enumerable.Range(0, 32).SelectMany(y => Enumerable.Range(0, 32)
            .Select(x => new TerrainTile(new(x, y), land.Contains(new(x, y)) ? TerrainKind.Meadow : TerrainKind.Water))).ToArray(), [], [], "port-dock-geometry");
        var existingDefinition = PortContent.Definitions.Single(item => PortNavigationRules.Facing(item) == PortFacing.North);
        var existing = new PlacedBuilding("completed-port", existingDefinition.CanonicalId, new(10, 10), 0);
        var proposedDefinition = PortContent.Definitions.Single(item => PortNavigationRules.Facing(item) == PortFacing.West);
        var proposed = new GridPoint(12, 11);
        Assert.True(PortNavigationRules.Fits(map, existingDefinition, existing.Position, new HashSet<GridPoint>(), out _));
        Assert.True(PortNavigationRules.Fits(map, proposedDefinition, proposed,
            WorldContentSimulationRules.Footprint(existingDefinition, existing).ToHashSet(), out _));
        var protectedTiles = PortNavigationRules.ProtectedBuildingTiles(map, existingDefinition, existing).ToHashSet();
        Assert.False(PortNavigationRules.Fits(map, proposedDefinition, proposed, protectedTiles, out _));
        Assert.All(PortNavigationRules.Geometry(map, existingDefinition, existing.Position).DockingTiles,
            dock => Assert.Contains(dock, protectedTiles));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AContinuingOwnerOrderWaitsAboardWhileTheHostedReplyIsHeld(bool urgentlyHungry)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Underway.Value);
        var boat = state.BoatTransport.Boats[0];
        var shore = state.Map.Tiles.First(tile => state.Map.IsBuildable(tile.Position) &&
            state.Map.FootDistance(tile.Position, boat.Position) <= 1).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == BoatPolicy.Author && urgentlyHungry
                ? person with { HungerBasisPoints = 1000 } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "waiting-shore-rope", "rope",
                        BoatPolicy.Author, 2, groundPosition: new(shore.X, shore.Y)),
                },
            },
        };
        var policy = new BoatPolicy { HoldActor = BoatPolicy.Author };
        using var scenario = new BoatScenario(state, policy);
        var foodBefore = scenario.World.Society.Inventory.Lots.Where(lot => lot.OwnerId == BoatPolicy.Author && lot.ItemKind == "food").Sum(lot => lot.Quantity);
        var receipt = scenario.World.SubmitInstruction(new("aboard-collect", "owner:test", BoatPolicy.Author,
            OwnerInstructionKind.MustDo, "collect my rope"));
        Assert.True((await scenario.World.AdvanceOneTickNonBlockingAsync()).Advanced);
        await policy.HeldStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(policy.Observations, observation => observation.InhabitantId == BoatPolicy.Author);
        var lot = scenario.World.Society.Inventory.GetLot("waiting-shore-rope");
        Assert.Null(lot.CarrierId);
        Assert.Equal(new InventoryGroundPosition(shore.X, shore.Y), lot.GroundPosition);
        Assert.Equal(0, scenario.World.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!.CompletedUnits);
        if (urgentlyHungry)
            Assert.Equal(foodBefore - 1, scenario.World.Society.Inventory.Lots.Where(lot => lot.OwnerId == BoatPolicy.Author && lot.ItemKind == "food").Sum(lot => lot.Quantity));
        Assert.Equal(scenario.World.Boats[0].Position,
            scenario.World.Inhabitants.Single(person => person.InhabitantId == BoatPolicy.Author).Position);
        scenario.World.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeathAboardKeepsAContainerAndItsContentsTogetherUntilSafeLanding(bool settleTownWill)
    {
        var policy = new BoatPolicy { LeaveToTown = settleTownWill };
        policy.IdleActors.Add(BoatPolicy.Author);
        var state = PrivateWorldRuntimeCodec.Decode(await Underway.Value);
        var checkpoint = state.Society.Society;
        var maximum = checkpoint.Config.DayLifecycle!.MaximumDay;
        var birth = checkpoint.LifeTickAt(checkpoint.WorldTick + 1) - maximum * checkpoint.Config.TicksPerLifecycleAge;
        // Start from a genuinely paid and boarded voyage, with an elder's next
        // age boundary due. Native mortality creates the death and estate.
        state = state with
        {
            Society = state.Society with
            {
                Society = checkpoint with
                {
                    Inventory = InventoryFixture.AddLot(checkpoint.Inventory, "aboard-bequest-wood", "wood", BoatPolicy.Author, 2),
                    Config = checkpoint.Config with { BaseNaturalMortalityBasisPoints = 0, NaturalMortalitySlopeBasisPoints = 0 },
                    Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == BoatPolicy.Author ? person with
                    {
                        BirthTick = checkpoint.LifeClock is null ? birth : person.BirthTick,
                        BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                        AgeBand = SocietyAgeBand.Elder,
                        LastLifecycleYearChecked = maximum - 1,
                    } : person).ToArray(),
                },
            },
        };
        using var scenario = new BoatScenario(state, policy);
        var boatId = scenario.World.Boats[0].Id;
        await scenario.UntilAsync(() => (scenario.World.ExportState().DeceasedInhabitants ?? [])
            .Any(person => person.InhabitantId == BoatPolicy.Author), 4);
        Assert.Equal(boatId, scenario.World.ExportState().DeceasedInhabitants!
            .Single(person => person.InhabitantId == BoatPolicy.Author).BoatIdAtDeath);
        Assert.Contains("travel-jug", scenario.World.Boats[0].GroundCargoLotIds!);
        if (settleTownWill)
        {
            AddLandingBlockers(scenario, scenario.World.Boats[0].Journey!.DestinationPortId);
            AddLandingBlockers(scenario, scenario.World.Boats[0].Journey!.OriginPortId, 2);
            for (var attempt = 0; attempt < 30 && scenario.World.Society.Estates.Single().WillStatus != "accepted"; attempt++)
            {
                Assert.True((await scenario.World.AdvanceOneTickNonBlockingAsync()).Advanced);
                await Task.Delay(10);
            }
            var estate = Assert.Single(scenario.World.Society.Estates);
            Assert.Equal("accepted", estate.WillStatus);
            var saved = scenario.World.ExportState();
            var due = saved with
            {
                Society = saved.Society with
                {
                    Society = saved.Society.Society with
                    {
                        Estates = saved.Society.Society.Estates.Select(item => item.Id == estate.Id
                            ? item with { ExpiryTick = saved.Society.Society.WorldTick + 1 } : item).ToArray(),
                    },
                },
            };
            using var settling = new BoatScenario(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(due)), policy);
            Assert.True((await settling.World.AdvanceOneTickAsync()).Advanced);
            Assert.True(settling.World.Society.GetEstate(estate.Id).Settled);
            var inherited = Assert.Single(settling.World.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "aboard-bequest-wood");
            Assert.Equal(TownBorderRules.FirstTownId, inherited.OwnerId);
            Assert.Null(inherited.StorageBuildingId);
            Assert.Equal(new InventoryGroundPosition(settling.World.Boats[0].Position.X, settling.World.Boats[0].Position.Y), inherited.GroundPosition);
            var inheritedJug = settling.World.Society.Inventory.GetLot("travel-jug");
            Assert.Equal(TownBorderRules.FirstTownId, inheritedJug.OwnerId);
            Assert.Null(inheritedJug.StorageBuildingId);
            Assert.Contains(inherited.Id, settling.World.Boats[0].GroundCargoLotIds!);
            settling.World.Validate();
            var settledBytes = PrivateWorldRuntimeCodec.Encode(settling.World.ExportState());
            using var settledReplay = new BoatScenario(PrivateWorldRuntimeCodec.Decode(settledBytes), new());
            for (var tick = 0; tick < 3; tick++)
            {
                Assert.True((await settling.World.AdvanceOneTickAsync()).Advanced);
                Assert.True((await settledReplay.World.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(settling.World.ExportState()), PrivateWorldRuntimeCodec.Encode(settledReplay.World.ExportState()));
            }
            settling.World.SubmitInstruction(new("estate-clear-landing", "owner:test", Blockers[0],
                OwnerInstructionKind.MustDo, "move to 194,10"));
            policy.IdleActors.Remove(Blockers[0]);
            await settling.UntilAsync(() => settling.World.Boats[0].Journey is null, 100);
            var landedJug = settling.World.Society.Inventory.GetLot("travel-jug");
            var landedWater = settling.World.Society.Inventory.GetLot("travel-water");
            Assert.Equal(landedJug.Id, landedWater.ContainerLotId);
            Assert.Equal(landedJug.OwnerId, landedWater.OwnerId);
            Assert.Equal(1, landedWater.Quantity);
            Assert.Null(landedWater.GroundPosition);
            Assert.True(settling.World.ExportState().Map.IsBuildable(new(landedJug.GroundPosition!.Value.X, landedJug.GroundPosition.Value.Y)));
            Assert.Equal(landedJug.GroundPosition, settling.World.Society.Inventory.GetLot(inherited.Id).GroundPosition);
            settling.World.Validate();
            return;
        }
        var aboardState = scenario.World.ExportState();
        var cargoBoat = aboardState.BoatTransport.Boats[0];
        foreach (var missing in new IReadOnlyList<string>?[]
        {
            null,
            [],
            cargoBoat.GroundCargoLotIds!.Where(id => id != "travel-jug").ToArray(),
        })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(aboardState with
            {
                BoatTransport = aboardState.BoatTransport with { Boats = [cargoBoat with { GroundCargoLotIds = missing }] },
            }));
        var aboardBytes = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        using (var replay = new BoatScenario(PrivateWorldRuntimeCodec.Decode(aboardBytes), new()))
        {
            Assert.Equal(aboardBytes, PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
            for (var tick = 0; tick < 3; tick++)
            {
                Assert.True((await scenario.World.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.World.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()),
                    PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
            }
        }
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is null, 100);
        var jug = scenario.World.Society.Inventory.GetLot("travel-jug");
        var water = scenario.World.Society.Inventory.GetLot("travel-water");
        Assert.Equal(jug.Id, water.ContainerLotId);
        Assert.Equal(jug.OwnerId, water.OwnerId);
        Assert.Equal(1, water.Quantity);
        Assert.Null(water.GroundPosition);
        Assert.True(scenario.World.ExportState().Map.IsBuildable(new(jug.GroundPosition!.Value.X, jug.GroundPosition.Value.Y)));
        var bytes = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task BlockedArrivalKeepsItsBoatPassengerAndReservationForAWorldDayThenReturns()
    {
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await Underway.Value), new());
        var boat = scenario.World.Boats[0];
        AddLandingBlockers(scenario, boat.Journey!.DestinationPortId);
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey?.WaitingSinceTick is not null, 100);
        var waiting = scenario.World.Boats[0];
        var since = waiting.Journey!.WaitingSinceTick!.Value;
        var day = scenario.World.ExportState().WorldSystems!.Config.TicksPerDay;
        while (scenario.World.WorldTick + 1 < since + day)
        {
            await scenario.World.AdvanceOneTickAsync();
            var current = scenario.World.Boats[0];
            Assert.Equal(waiting.Position, current.Position);
            Assert.False(current.Journey!.Returning);
            Assert.Equal(waiting.Journey.ReservedDock, current.Journey.ReservedDock);
            Assert.Equal(current.Position, scenario.World.Inhabitants.Single(person => person.InhabitantId == BoatPolicy.Author).Position);
        }
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey?.Returning == true, 4);
        Assert.True(scenario.World.WorldTick >= since + day);
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is null, 100);
        Assert.Equal(boat.Journey.OriginPortId, scenario.World.Boats[0].DockedPortId);
        Assert.Equal("returned", Assert.Single(scenario.World.BoatRequests).Status);
        scenario.World.Validate();
    }

    [Fact]
    public async Task ABlockedRequestRetainsItsOrderWithoutHoldingABoatAndCanBeCancelled()
    {
        var policy = new BoatPolicy { Trips = true };
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), policy);
        var boat = scenario.World.Boats[0];
        var destination = scenario.World.Towns[0].Projects.Single(project => project.CompletedBuildingId is not null && project.CompletedBuildingId != boat.DockedPortId);
        AddLandingBlockers(scenario, destination.CompletedBuildingId!);
        await scenario.UntilAsync(() => scenario.World.BoatRequests.Any(request => request.Status == "waiting"), 120);
        var request = Assert.Single(scenario.World.BoatRequests);
        Assert.Null(request.BoatId);
        Assert.Equal(boat, Assert.Single(scenario.World.Boats));
        policy.Trips = false;
        policy.CancelWaiting = true;
        await scenario.UntilAsync(() => scenario.World.BoatRequests[0].Status == "cancelled", 8);
        Assert.Equal((request.Id, request.Sequence), (scenario.World.BoatRequests[0].Id, scenario.World.BoatRequests[0].Sequence));
        Assert.Equal(boat, Assert.Single(scenario.World.Boats));
        scenario.World.Validate();
    }

    [Fact]
    public async Task AnOlderAbsentTravelerDoesNotBlockTheNextUsableRequest()
    {
        var policy = new BoatPolicy { Trips = true };
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), policy);
        var boat = scenario.World.Boats[0];
        var destination = scenario.World.Towns[0].Projects.Single(project => project.CompletedBuildingId is not null && project.CompletedBuildingId != boat.DockedPortId);
        AddLandingBlockers(scenario, destination.CompletedBuildingId!);
        await scenario.UntilAsync(() => scenario.World.BoatRequests.Count == 1, 120);
        var first = scenario.World.BoatRequests[0];
        policy.TripActor = Follower;
        await scenario.UntilAsync(() => scenario.World.BoatRequests.Count == 2, 120);
        Assert.All(scenario.World.BoatRequests, request => Assert.Equal("waiting", request.Status));
        Assert.Null(scenario.World.Boats[0].Journey);
        // Clear one landing through an ordinary owner movement instruction.
        // Scouting can legitimately be unavailable in the current weather.
        scenario.World.SubmitInstruction(new("clear-landing", "owner:test", Blockers[0],
            OwnerInstructionKind.MustDo, "move to 194,10"));
        policy.IdleActors.Remove(Blockers[0]);
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is not null, 30);
        Assert.Equal(Follower, scenario.World.Boats[0].Journey!.PassengerId);
        Assert.Equal(first, scenario.World.BoatRequests[0]);
        Assert.Equal("underway", scenario.World.BoatRequests[1].Status);
        scenario.World.Validate();
    }

    [Fact]
    public async Task CouncilPermissionAllowsAVisitorToTravelWithoutJoiningTheTownOrOwningItsBoat()
    {
        var policy = new BoatPolicy { Trips = true, TripActor = Visitor };
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), policy);
        var map = scenario.World.ExportState().Map;
        var origin = scenario.World.WorldSimulation.Buildings.Single(port => port.InstanceId == scenario.World.Boats[0].DockedPortId);
        var titles = scenario.World.ExportState().TownLandTitles!;
        var outside = map.Tiles.Where(tile => map.IsBuildable(tile.Position) && !scenario.World.Towns[0].BorderTiles.Contains(tile.Position) &&
                !titles.Any(title => title.Tiles.Contains(tile.Position)) &&
                !map.Resources.Any(resource => resource.Position == tile.Position) && !map.CampObjects.Any(item => item.Position == tile.Position))
            .OrderBy(tile => map.FootDistance(tile.Position, origin.Entrance!.Value))
            .First(tile => DeterministicRouteFinder.TryFind(map, tile.Position, origin.Entrance!.Value, out _)).Position;
        scenario.World.Pause();
        Assert.Equal("household:" + Visitor, scenario.World.AddAgent(Visitor, outside));
        scenario.World.Resume();
        await scenario.World.AdvanceOneTickAsync();
        Assert.DoesNotContain(policy.Observations.Where(observation => observation.InhabitantId == Visitor).SelectMany(observation => observation.Candidates),
            candidate => candidate.Id.StartsWith("boat_trip:", StringComparison.Ordinal));
        Assert.Empty(scenario.World.BoatRequests);
        policy.GrantAll = true;
        await scenario.UntilAsync(() => TownBoatAccessRules.Allows(scenario.World.Towns[0], Visitor, scenario.World.WorldTick), 100);
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey?.PassengerId == Visitor, 120);
        Assert.DoesNotContain(Visitor, scenario.World.Towns[0].ResidentIds);
        Assert.Equal(TownBorderRules.FirstTownId, scenario.World.Boats[0].TownId);
        var bytes = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACompletedPaidBoatCannotDisappearOrAcquireAnInventedRemoval(bool removed)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value);
        var boat = Assert.Single(state.BoatTransport.Boats);
        state = removed ? state with
        {
            Towns = state.Towns!.Select(town => town with
            {
                Projects = town.Projects.Select(project => project.Id == boat.ProjectId
                    ? project with { RemovedTick = state.Society.Society.WorldTick } : project).ToArray(),
            }).ToArray(),
        } : state with { BoatTransport = state.BoatTransport with { Boats = [] } };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("payment")]
    [InlineData("passenger")]
    [InlineData("dock")]
    [InlineData("route")]
    [InlineData("request")]
    public async Task DamagedBoatAuthorityPassengerReservationAndRouteAreRefused(string damage)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Underway.Value);
        var boat = state.BoatTransport.Boats[0];
        var journey = boat.Journey!;
        var changed = damage switch
        {
            "owner" => boat with { TownId = "town:missing" },
            "payment" => boat with { ProjectId = state.Towns![0].Projects.First(project => project.Plan.BoatPortId is null).Id },
            "passenger" => boat with { Journey = journey with { PassengerId = Follower } },
            "dock" => boat with { Journey = journey with { ReservedDock = state.Inhabitants[0].Position } },
            "route" => boat with { Journey = journey with { WaterPath = [boat.Position, journey.ReservedDock] } },
            "request" => boat with { Journey = journey with { RequestId = "boat-request:missing" } },
            _ => throw new InvalidOperationException(),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            BoatTransport = state.BoatTransport with { Boats = [changed] },
        }));
    }

    private static void AddLandingBlockers(BoatScenario scenario, string portId, int blockerOffset = 0)
    {
        var port = scenario.World.WorldSimulation.Buildings.Single(building => building.InstanceId == portId);
        var definition = scenario.World.WorldContent.Buildings.Single(definition => definition.CanonicalId == port.DefinitionId);
        var land = PortNavigationRules.Geometry(scenario.World.ExportState().Map, definition, port.Position).LandTiles;
        scenario.World.Pause();
        for (var index = 0; index < land.Count; index++)
        {
            Assert.Null(scenario.World.AddAgent(Blockers[index + blockerOffset], land[index]));
            scenario.Policy.IdleActors.Add(Blockers[index + blockerOffset]);
        }
        scenario.World.Resume();
    }

    private static async Task<byte[]> BuildPaidBoatAsync(bool stopBeforeFirstPort = false)
    {
        var policy = new BoatPolicy { Build = true };
        using var created = new PrivateWorldRuntime("probe-a", policy.CreateProvider,
            startPace: WorldStartPace.FounderSetup,
            geographyOptions: new GeographyOptions("probe-a", WorldSizePreset.Small));
        created.InitializeFirstTownContent();
        created.AcceptFirstTownLayout(new(194, 12));
        // These are genuine paused founder placements beside the notice place.
        // Keep voters in the same land component as the coastal Town.
        var map = created.ExportState().Map;
        var board = created.Towns[0].OriginSite!.Value;
        var placements = map.Tiles.Where(tile => map.IsBuildable(tile.Position) &&
                !map.Resources.Any(resource => resource.Position == tile.Position) &&
                !map.CampObjects.Any(item => item.Position == tile.Position))
            .OrderBy(tile => map.FootDistance(tile.Position, board)).ThenBy(tile => tile.Position.Y).ThenBy(tile => tile.Position.X)
            .Take(PrivateWorldRuntime.RequiredFounders).ToArray();
        for (var index = 0; index < placements.Length; index++)
            created.PlaceFounder($"founder:{index + 1:D32}", placements[index].Position);
        created.StartWorld();
        var state = created.ExportState();
        Assert.Equal(0, created.WorldTick);
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var inventory = state.Society.Society.Inventory;
        var oldDay = state.Society.Society.Config.TicksPerWorldDay;
        foreach (var (kind, quantity, held) in new[] { ("wood", 44, 4), ("stone", 12, 4), ("rope", 3, 1), ("iron", 3, 1) })
        {
            inventory = InventoryFixture.AddLot(inventory, "boat-stock-" + kind, kind, TownBorderRules.FirstTownId,
                held, storageBuildingId: warehouse.InstanceId);
            inventory = InventoryFixture.Reserve(inventory, "unrelated-" + kind, TownBorderRules.FirstTownId,
                "boat-stock-" + kind, held, "unrelated-town-work", long.MaxValue);
            inventory = InventoryFixture.AddLot(inventory, "available-boat-stock-" + kind, kind, TownBorderRules.FirstTownId,
                quantity - held - (kind == "wood" ? 16 : kind == "stone" ? 4 : 0), groundPosition: new(189, 14));
            if (kind is "wood" or "stone")
                inventory = InventoryFixture.AddLot(inventory, "second-port-stock-" + kind, kind, TownBorderRules.FirstTownId,
                    kind == "wood" ? 16 : 4, groundPosition: new(196, 13));
        }
        foreach (var person in state.Inhabitants)
        {
            inventory = InventoryFixture.AddLot(inventory, "travel-food:" + person.InhabitantId, "food", person.InhabitantId,
                person.InhabitantId == BoatPolicy.Author ? 2 : 4);
            inventory = InventoryFixture.AddLot(inventory, "travel-clothing:" + person.InhabitantId, "clothing", person.InhabitantId, 1);
        }
        inventory = InventoryFixture.AddLot(inventory, "travel-jug", InventoryContainerRules.WaterJug, BoatPolicy.Author, 1);
        inventory = InventoryFixture.AddLot(inventory, "travel-water", InventoryContainerRules.FreshWater, BoatPolicy.Author, 1,
            containerLotId: "travel-jug");
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = inventory,
                    Config = state.Society.Society.Config with { TicksPerWorldDay = 24 },
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person with
                    {
                        BirthTick = person.BirthTick / oldDay * 24,
                        BirthLifeTick = person.BirthLifeTick is { } birth ? birth / oldDay * 24 : null,
                    }).ToArray(),
                }
            },
            // Arrange the initial time scale; every vote, payment and journey remains native.
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            { Config = state.WorldSystems.Config with { TicksPerDay = 24, CalendarOffsetTicks = 0 }, RegionalWeather = null }, state.Map),
        };
        using var scenario = new BoatScenario(state, policy);
        await scenario.UntilAsync(() => scenario.World.Towns[0].Projects.Count > 0, 80);
        if (stopBeforeFirstPort)
        {
            await scenario.UntilAsync(() => scenario.World.Towns[0].Projects.Single() is { Stage: "working", WorkDone: > 0 }, 300);
            return PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        }
        await scenario.UntilAsync(() => scenario.World.Boats.Count == 1, 600);
        policy.Build = false;
        return PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
    }

    private static async Task<byte[]> StartVoyageAsync()
    {
        var policy = new BoatPolicy { Trips = true };
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), policy);
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is not null, 120);
        return PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
    }

    private sealed class BoatScenario : IDisposable
    {
        internal PrivateWorldRuntime World { get; }
        internal BoatPolicy Policy { get; }
        internal BoatScenario(PrivateWorldRuntimeState state, BoatPolicy policy)
        {
            Policy = policy;
            World = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
            policy.World = World;
        }
        internal async Task UntilAsync(Func<bool> done, int limit)
        {
            for (var step = 0; !done() && step < limit; step++) Assert.True((await World.AdvanceOneTickAsync()).Advanced);
            Assert.True(done(), $"Expected boat phase was not reached at {World.WorldTick}; " +
                string.Join("; ", World.Towns[0].Projects.Select(project => project.Plan.Name + ":" + project.Stage + ":" + project.Blocker)) +
                "; civic events: " + string.Join("; ", World.ExportState().Events.Where(item => item.Kind != "tick_advanced").TakeLast(30).Select(item => item.Kind + ":" + item.Detail)) +
                "; first Port offers: " + string.Join("; ", Policy.Observations.Take(4).Select(observation => observation.InhabitantId + ":" +
                    string.Join(",", observation.Candidates.Where(candidate => candidate.Id.Contains("|project|port-", StringComparison.Ordinal)).Select(candidate => candidate.Id)))) +
                "; positions: " + string.Join("; ", World.Inhabitants.Select(person => person.InhabitantId + ":" + person.Position)) +
                "; recent choices: " + string.Join("; ", Policy.Choices.TakeLast(16)));
        }
        public void Dispose() => World.Dispose();
    }

    private sealed class BoatPolicy
    {
        internal const string Author = "founder:00000000000000000000000000000001";
        internal PrivateWorldRuntime? World { get; set; }
        internal bool Build { get; set; }
        internal bool Trips { get; set; }
        internal string TripActor { get; set; } = Author;
        internal bool CancelWaiting { get; set; }
        internal bool GrantAll { get; set; }
        internal bool LeaveToTown { get; set; }
        internal string? HoldActor { get; set; }
        internal TaskCompletionSource<CognitionDecisionResponse> HeldReply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> HeldStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HashSet<string> IdleActors { get; } = new(StringComparer.Ordinal);
        internal ConcurrentQueue<string> Choices { get; } = new();
        internal ConcurrentQueue<InhabitantObservation> Observations { get; } = new();
        internal IDecisionProvider CreateProvider(string actor) => new Provider(this, actor);
        private sealed class Provider(BoatPolicy policy, string actor) : IDecisionProvider
        {
            public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
            public long ProviderEpoch => 1;
            public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
            {
                var candidates = request.Observation.Candidates;
                policy.Observations.Enqueue(request.Observation);
                if (actor == policy.HoldActor)
                {
                    policy.HeldStarted.TrySetResult(true);
                    return new(policy.HeldReply.Task.WaitAsync(cancellationToken));
                }
                var selected = policy.LeaveToTown && request.Observation.Will is not null ? candidates.Single(candidate => candidate.Id == CognitionWillContext.HeirsCandidateId) :
                    policy.IdleActors.Contains(actor) ? candidates.Single(candidate => candidate.Id == "safe_idle") :
                    Blockers.Contains(actor, StringComparer.Ordinal) ? candidates.FirstOrDefault(candidate => candidate.Id == "move_to") : null;
                selected ??= candidates.Where(candidate => candidate.DeterministicPriority <= 5 && candidate.Id is
                        "consume_food" or "collect_shared_food" or "take_food_from_pot" or "harvest_food" or "seek_food" or "seek_warmth" or "wear_clothing")
                    .OrderBy(candidate => candidate.DeterministicPriority).FirstOrDefault() ??
                    candidates.FirstOrDefault(candidate => candidate.Id.Contains("|yes|", StringComparison.Ordinal)) ??
                    candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal));
                string? text = null;
                var projects = policy.World?.Towns[0].Projects ?? [];
                if (selected is null && policy.CancelWaiting)
                    selected = candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("boat_cancel:", StringComparison.Ordinal));
                if (selected is null && policy.GrantAll && actor == Author)
                    selected = candidates.FirstOrDefault(candidate => candidate.Id.Contains("|boat_access|all|", StringComparison.Ordinal));
                if (selected is null && policy.Build && actor == Author &&
                    !projects.Any(project => project.Stage is not ("completed" or "cancelled")))
                {
                    var prefix = projects.Count(project => project.Stage == "completed" && project.Plan.BoatPortId is null) switch
                    {
                        0 => "|project|port-south|189,14",
                        1 => "|project|port-south|196,13",
                        _ => "|boat_project|",
                    };
                    selected = candidates.FirstOrDefault(candidate => candidate.Id.Contains(prefix, StringComparison.Ordinal));
                    if (prefix == "|boat_project|")
                    {
                        var launchPort = projects.Single(project => project.Plan.Site == new GridPoint(189, 14) && project.Plan.BoatPortId is null).CompletedBuildingId!;
                        selected = candidates.FirstOrDefault(candidate => candidate.Id.Contains(prefix + launchPort + "|", StringComparison.Ordinal));
                    }
                    text = prefix.Contains("boat_project", StringComparison.Ordinal) ? "Passage boat" : "Port " + (projects.Count + 1);
                }
                if (selected is null && policy.Build && actor == Author)
                    foreach (var prefix in new[] { "town_project_deliver:", "town_project_supply:", "town_project_work:" })
                    {
                        selected = candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal));
                        if (selected is not null) break;
                    }
                if (selected is null && policy.Trips && actor == policy.TripActor)
                    selected = candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("boat_trip:", StringComparison.Ordinal)) ??
                        candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("boat_queue:", StringComparison.Ordinal)) ??
                        (policy.World?.BoatRequests.Any(trip => trip.PassengerId == actor && trip.Status == "waiting") == true
                            ? candidates.Single(candidate => candidate.Id == "safe_idle") : null);
                selected ??= candidates.FirstOrDefault(candidate => candidate.Id.Contains("|visit|", StringComparison.Ordinal));
                selected ??= candidates.Single(candidate => candidate.Id == "safe_idle");
                policy.Choices.Enqueue(actor + ":" + selected.Id);
                return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, actor, Kind, ProviderEpoch,
                    request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                    selected.Id, 1, candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                    ChosenPersonality: request.Observation.NeedsPersonality ? "Patient and curious" : null,
                    ChosenAspiration: request.Observation.NeedsAspiration ? "Explore the coast" : null,
                    Will: policy.LeaveToTown && request.Observation.Will is { } will
                        ? new([will.Heirs.Single(heir => heir.Key.StartsWith("will:town:", StringComparison.Ordinal)).Key], CognitionWillContext.EqualSplit) : null,
                    CivicProposal: text));
            }
        }
    }
}
