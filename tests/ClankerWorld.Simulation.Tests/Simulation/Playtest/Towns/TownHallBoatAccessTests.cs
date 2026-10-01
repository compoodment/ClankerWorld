using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

// Reuses PortBoatTests' genuinely paid Ports, completed boat, geometry and idle provider.
public sealed partial class PortBoatTests
{
    private const string BoatHallId = "test-boat-town-hall";
    private const string BoatHallWood = "000-boat-hall-wood";
    private const string BoatHallStone = "000-boat-hall-stone";
    private const string BoatHallPortId = "port-for-town-hall";
    private const string BoatHallPortWood = "000-boat-aaa-port-wood";
    private const string BoatHallPortStone = "000-boat-aaa-port-stone";
    private static readonly Lazy<Task<HallBoatFixture>> PaidBoatHall = new(BuildBoatHallAsync);

    private sealed record HallBoatFixture(byte[] State, GridPoint AlongsideWater, string DestinationPortId);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALivePassengerCannotProposeOrVoteOnAHallRuleFromAdjacentWater(bool reload)
    {
        using var prepared = await AdjacentHallPassengerAsync(election: false);
        using var world = PrivateWorldRuntime.Restore(reload
            ? PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(prepared.ExportState()))
            : prepared.ExportState(), _ => new Pick());
        var (passenger, landResident, townId) = AssertAdjacentHallPassenger(world);
        var council = world.TownCouncils.Single(item => item.TownId == townId);
        Assert.Null(council.Election);
        Assert.Null(council.Ballot);
        Assert.Contains(passenger, council.MemberIds);
        Assert.Contains(landResident, council.MemberIds);

        Assert.False(world.ProposeTownLaw(passenger, townId, "quiet_dock", "Let each speaker finish.").Applied);
        Assert.Null(world.TownCouncils.Single(item => item.TownId == townId).Ballot);
        Assert.True(world.ProposeTownLaw(landResident, townId, "quiet_dock", "Let each speaker finish.").Applied);
        var ballot = world.TownCouncils.Single(item => item.TownId == townId).Ballot!;
        Assert.Empty(ballot.Approvals);
        Assert.Contains(passenger, ballot.Electorate);
        var inventoryBefore = InventoryDigest.State(world.Society.Inventory);
        Assert.False(world.VoteTownLaw(passenger, townId, true).Applied);
        Assert.Equal(ballot, world.TownCouncils.Single(item => item.TownId == townId).Ballot);
        Assert.Equal(inventoryBefore, InventoryDigest.State(world.Society.Inventory));
        Assert.True(world.VoteTownLaw(landResident, townId, true).Applied);
        Assert.Equal([landResident], world.TownCouncils.Single(item => item.TownId == townId).Ballot!.Approvals);
        AssertAdjacentHallPassenger(world);
        world.Validate();
        AssertHallBoatRoundTrip(world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALivePassengerCannotVolunteerOrVoteInAnElectionFromAdjacentWater(bool reload)
    {
        using var prepared = await AdjacentHallPassengerAsync(election: true);
        using var world = PrivateWorldRuntime.Restore(reload
            ? PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(prepared.ExportState()))
            : prepared.ExportState(), _ => new Pick());
        var (passenger, landResident, townId) = AssertAdjacentHallPassenger(world);

        Assert.Contains(world.TownCouncils.Single(item => item.TownId == townId).CandidateRegister,
            item => item.CandidateId == landResident);
        var election = world.TownCouncils.Single(item => item.TownId == townId).Election!;
        Assert.False(election.IsRunoff);
        Assert.Contains(passenger, election.Electorate);
        Assert.Contains(landResident, election.Candidates);
        Assert.DoesNotContain(passenger, election.Candidates);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.VolunteerTownCouncil(passenger, townId).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.False(world.VoteTownElection(passenger, townId, [landResident]).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.VoteTownElection(landResident, townId, [landResident]).Applied);
        var vote = Assert.Single(world.TownCouncils.Single(item => item.TownId == townId).Election!.Votes);
        Assert.Equal(landResident, vote.VoterId);
        Assert.Equal([landResident], vote.CandidateIds);
        AssertAdjacentHallPassenger(world);
        world.Validate();
        AssertHallBoatRoundTrip(world);
    }

    private static async Task<PrivateWorldRuntime> AdjacentHallPassengerAsync(bool election)
    {
        var fixture = await PaidBoatHall.Value;
        var state = PrivateWorldRuntimeCodec.Decode(fixture.State);
        var passenger = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(passenger).HouseholdId!;
        var boat = Assert.Single(state.BoatTransport!.Boats);
        var journeyPath = boat.Journey!.WaterPath.ToArray();
        var alongsideWater = fixture.AlongsideWater;
        var hall = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == BoatHallId);
        Assert.Equal(boat.TownId, hall.TownId);
        Assert.Null(hall.HouseholdId);
        var inventory = state.Society.Society.Inventory;
        Assert.DoesNotContain(inventory.Lots, item => item.Id == BoatHallWood);
        Assert.DoesNotContain(inventory.Lots, item => item.Id == BoatHallStone);
        var cost = inventory.Reservations.Where(item => item.Purpose == "building:" + BoatHallId).ToArray();
        Assert.Equal(36, cost.Sum(item => item.Quantity));
        Assert.All(cost, item =>
        {
            Assert.Equal(household, item.OwnerId);
            Assert.Equal(InventoryReservationState.Completed, item.State);
        });
        Assert.Contains(BoatHallWood, cost.Select(item => item.LotId));
        Assert.Contains(BoatHallStone, cost.Select(item => item.LotId));

        var others = state.Inhabitants.Where(person => person.InhabitantId != passenger)
            .Select(person => person.InhabitantId).ToArray();
        var landResident = others[2];
        var destination = Geometry(state, fixture.DestinationPortId);
        var hallTiles = WorldContentSimulationRules.Footprint(TownHallContent.TownHall(), hall).ToArray();
        var landPosition = hallTiles.First(point => state.Map.IsPassable(point) &&
            !destination.LandTiles.Contains(point) && state.Inhabitants.All(person => person.Position != point));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person =>
                person.InhabitantId == landResident ? person with { Position = landPosition }
                : person.InhabitantId == others[0] ? person with { Position = destination.LandTiles[0] }
                : person.InhabitantId == others[1] ? person with { Position = destination.LandTiles[1] }
                : person).ToArray()
        };
        var world = PrivateWorldRuntime.Restore(state, _ => new Pick());
        try
        {
            if (election)
            {
                // Register at the actual Hall before the eighth resident opens the frozen contest.
                Assert.True(world.VolunteerTownCouncil(landResident, boat.TownId).Applied);
                for (var index = 0; index < 4; index++)
                {
                    var id = "agent:" + (9_100 + index).ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
                    var current = world.ExportState();
                    var point = current.Towns!.Single(town => town.Id == boat.TownId).BorderTiles.First(position =>
                        current.Map.IsBuildable(position) && current.Inhabitants.All(person => person.Position != position) &&
                        current.Map.Resources.All(resource => resource.Position != position) && current.Map.CampObjects.All(item => item.Position != position) &&
                        current.WorldSimulation!.Buildings.All(building => !WorldContentSimulationRules.Footprint(
                            current.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building).Contains(position)));
                    world.AddAgent(id, point);
                    Assert.Contains(id, world.Towns.Single(town => town.Id == boat.TownId).ResidentIds);
                }
            }
            // The physical journey advances normally. Its blocked destination prevents accidental
            // landing if the chosen adjacent-water point is the final berth.
            for (var tick = 0; tick < journeyPath.Length * 3 && world.Boats[0].Position != alongsideWater; tick++)
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(alongsideWater, world.Boats[0].Position);
            Assert.NotNull(world.Boats[0].Journey);
            AssertAdjacentHallPassenger(world);
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    private static async Task<HallBoatFixture> BuildBoatHallAsync()
    {
        using var docked = await DockedWorldAsync();
        var state = WithJug(docked.ExportState());
        var passenger = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(passenger).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            BoatHallWood, "wood", household, 24, storageBuildingId: "first-town-house-a");
        inventory = InventoryFixture.AddLot(inventory,
            BoatHallStone, "stone", household, 12, storageBuildingId: "first-town-house-a");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using (var original = PrivateWorldRuntime.Restore(state, _ => new Pick()))
            if (TryStartHallJourney(original, passenger, "port-two") is { } existing) return existing;

        // A fixed generated route need not pass any clear 3 by 4 Town land. Build a
        // further real Port where that footprint exists; its paid construction grows
        // the authoritative Town border through the ordinary placement operation.
        var map = state.Map;
        var boat = Assert.Single(state.BoatTransport!.Boats);
        var town = state.Towns!.Single(item => item.Id == boat.TownId);
        var content = state.WorldContent!;
        var simulation = state.WorldSimulation!;
        var hallDefinition = content.Buildings.Single(item => item.CanonicalId == TownHallContent.TownHall().CanonicalId);
        var buildings = simulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            content.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)).ToArray();
        var roads = (state.RoadTiles ?? []).Concat((state.Bridges ?? []).SelectMany(bridge => bridge.Span)).ToHashSet();
        var occupied = map.Resources.Select(item => item.Position).Concat(map.CampObjects.Select(item => item.Position))
            .Concat(roads).Concat(buildings).Concat((state.Fields ?? []).Select(item => item.Position))
            .Concat((simulation.Carts ?? []).Select(item => item.Position)).ToHashSet();
        var waterObstacles = occupied.Where(point => !map.IsLand(point)).ToHashSet();
        occupied.UnionWith(state.BoatTransport.Boats.Select(item => item.Position));
        foreach (var port in simulation.Buildings.Where(building => content.Buildings.Any(definition =>
                     definition.CanonicalId == building.DefinitionId && PortNavigationRules.IsPort(definition))))
            occupied.UnionWith(Geometry(state, port.InstanceId).DockingTiles);
        var sites = map.Tiles.SelectMany(tile => content.Buildings.Where(PortNavigationRules.IsPort)
                .Select(definition => (Definition: definition, Position: tile.Position)))
            .Where(site => PortNavigationRules.Fits(map, site.Definition, site.Position, occupied, out _, roads))
            .OrderBy(site => map.FootDistance(boat.Position, site.Position)).ThenBy(site => site.Position.Y).ThenBy(site => site.Position.X);
        foreach (var site in sites)
        {
            var portGeometry = PortNavigationRules.Geometry(map, site.Definition, site.Position);
            var portFootprint = WorldContentSimulationRules.Footprint(site.Definition, site.Position).ToArray();
            var projectedTown = town with
            {
                BorderTiles = TownBorderRules.ExpandForBuilding(map, town, site.Position, site.Definition.Width, site.Definition.Height)
            };
            var route = PortNavigationRules.WaterRoute(map, boat.Position,
                portGeometry.DockingTiles.Where(water => portGeometry.LandTiles.Any(land => map.FootDistance(water, land) == 1)),
                waterObstacles.Concat(portFootprint.Where(point => !map.IsLand(point))).ToHashSet());
            if (route.Count == 0 || route.Count * 3 >= state.Society.Society.Config.TicksPerWorldDay) continue;
            var reachableShore = route.Where(water => portGeometry.DockingTiles.Contains(water)).ToArray();
            var nearShoreAnchors = reachableShore.SelectMany(water =>
                Enumerable.Range(-hallDefinition.Height, hallDefinition.Height + 2).SelectMany(dy =>
                    Enumerable.Range(-hallDefinition.Width, hallDefinition.Width + 2).Select(dx => new GridPoint(water.X + dx, water.Y + dy)))).Distinct();
            if (!nearShoreAnchors.Any(anchor =>
                TownBorderRules.IsWithinOrAdjacent(projectedTown, anchor, hallDefinition.Width, hallDefinition.Height) &&
                WorldContentSimulationRules.Footprint(hallDefinition, anchor).All(point => map.IsBuildable(point) &&
                    !occupied.Contains(point) && !portFootprint.Contains(point) && state.Inhabitants.All(person => person.Position != point)) &&
                WorldContentSimulationRules.Footprint(hallDefinition, anchor).Any(point => reachableShore.Any(water => map.FootDistance(water, point) == 1)))) continue;

            inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
                BoatHallPortWood, "wood", household, 16, storageBuildingId: "first-town-house-a");
            inventory = InventoryFixture.AddLot(inventory,
                BoatHallPortStone, "stone", household, 4, storageBuildingId: "first-town-house-a");
            using var trial = PrivateWorldRuntime.Restore(state with
            {
                Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } }
            }, _ => new Pick());
            if (!trial.PlacePort(BoatHallPortId, site.Definition.CanonicalId, site.Position, passenger).Applied) continue;
            if (TryStartHallJourney(trial, passenger, BoatHallPortId) is not { } fixture) continue;
            var paidPort = trial.Society.Inventory.Reservations.Where(item => item.Purpose == "building:" + BoatHallPortId).ToArray();
            Assert.Equal(20, paidPort.Sum(item => item.Quantity));
            Assert.All(paidPort, item =>
            {
                Assert.Equal(household, item.OwnerId);
                Assert.Equal(InventoryReservationState.Completed, item.State);
            });
            Assert.Contains(BoatHallPortWood, paidPort.Select(item => item.LotId));
            Assert.Contains(BoatHallPortStone, paidPort.Select(item => item.LotId));
            Assert.DoesNotContain(trial.Society.Inventory.Lots, item => item.Id is BoatHallPortWood or BoatHallPortStone);
            Assert.Equal(boat.Id, Assert.Single(trial.Boats).Id);
            Assert.Equal(boat.BuildJobId, Assert.Single(trial.Boats).BuildJobId);
            return fixture;
        }
        throw new InvalidOperationException("No real generated shore supports the paid Port, paid Hall and connected passenger journey fixture.");
    }

    private static HallBoatFixture? TryStartHallJourney(PrivateWorldRuntime world, string passenger, string destination)
    {
        var boat = Assert.Single(world.Boats);
        if (!world.StartBoatJourney(boat.Id, passenger, destination).Applied) return null;
        foreach (var water in Assert.Single(world.Boats).Journey!.WaterPath)
            if (TryPlaceBoatHall(world, water))
            {
                world.Validate();
                return new(PrivateWorldRuntimeCodec.Encode(world.ExportState()), water, destination);
            }
        return null;
    }

    private static bool TryPlaceBoatHall(PrivateWorldRuntime world, GridPoint boatPosition)
    {
        var state = world.ExportState();
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == TownHallContent.TownHall().CanonicalId);
        var positions = state.Map.Tiles.Select(tile => tile.Position).Where(position =>
        {
            var footprint = WorldContentSimulationRules.Footprint(definition, position).ToArray();
            return footprint.All(state.Map.IsBuildable) &&
                footprint.All(point => state.Inhabitants.All(person => person.Position != point)) &&
                footprint.Any(point => state.Map.FootDistance(boatPosition, point) <= 1);
        }).OrderBy(point => point.Y).ThenBy(point => point.X);
        foreach (var position in positions)
            if (world.PlaceBuilding(BoatHallId, definition.CanonicalId, position).Applied) return true;
        return false;
    }

    private static (string Passenger, string LandResident, string TownId) AssertAdjacentHallPassenger(PrivateWorldRuntime world)
    {
        var state = world.ExportState();
        var boat = Assert.Single(world.Boats);
        var passenger = boat.Journey!.PassengerId;
        Assert.Null(boat.DockedPortId);
        Assert.Equal(boat.Position, state.Inhabitants.Single(person => person.InhabitantId == passenger).Position);
        Assert.False(state.Map.IsPassable(boat.Position));
        var hall = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == BoatHallId);
        var footprint = WorldContentSimulationRules.Footprint(TownHallContent.TownHall(), hall).ToArray();
        Assert.Contains(footprint, point => state.Map.FootDistance(boat.Position, point) == 1);
        var town = state.Towns!.Single(item => item.Id == boat.TownId);
        Assert.Contains(passenger, town.ResidentIds);
        var landResident = Assert.Single(state.Inhabitants, person => person.InhabitantId != passenger &&
            footprint.Contains(person.Position)).InhabitantId;
        Assert.Contains(landResident, town.ResidentIds);
        Assert.True(state.Map.IsPassable(state.Inhabitants.Single(person => person.InhabitantId == landResident).Position));
        var inventory = state.Society.Society.Inventory;
        Assert.Equal((passenger, 1, (InventoryGroundPosition?)null),
            (inventory.GetLot("aboard-jug").OwnerId, inventory.GetLot("aboard-jug").Quantity, inventory.GetLot("aboard-jug").GroundPosition));
        Assert.Equal((passenger, 2, "aboard-jug", (InventoryGroundPosition?)null),
            (inventory.GetLot("aboard-water").OwnerId, inventory.GetLot("aboard-water").Quantity,
                inventory.GetLot("aboard-water").ContainerLotId, inventory.GetLot("aboard-water").GroundPosition));
        return (passenger, landResident, boat.TownId);
    }

    private static void AssertHallBoatRoundTrip(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new Pick());
        AssertAdjacentHallPassenger(restored);
        restored.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}
