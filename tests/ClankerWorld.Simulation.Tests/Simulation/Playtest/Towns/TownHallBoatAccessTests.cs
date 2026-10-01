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
        using var docked = await DockedWorldAsync();
        var state = WithJug(docked.ExportState());
        var passenger = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(passenger).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            BoatHallWood, "wood", household, 24, storageBuildingId: "first-town-house-a");
        inventory = InventoryFixture.AddLot(inventory,
            BoatHallStone, "stone", household, 12, storageBuildingId: "first-town-house-a");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var placing = PrivateWorldRuntime.Restore(state, _ => new Pick());
        var boat = Assert.Single(placing.Boats);
        Assert.True(placing.StartBoatJourney(boat.Id, passenger, "port-two").Applied);
        boat = Assert.Single(placing.Boats);
        var journeyPath = boat.Journey!.WaterPath.ToArray();
        GridPoint? alongsideWater = null;
        foreach (var water in journeyPath)
            if (TryPlaceBoatHall(placing, water))
            {
                alongsideWater = water;
                break;
            }
        Assert.True(alongsideWater.HasValue,
            "The generated Port fixture needs one legally buildable paid Hall adjacent to its actual water journey, including intermediate shore tiles.");
        state = placing.ExportState();
        var hall = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == BoatHallId);
        Assert.Equal(boat.TownId, hall.TownId);
        Assert.Null(hall.HouseholdId);
        inventory = state.Society.Society.Inventory;
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
        var destination = Geometry(state, "port-two");
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
            for (var tick = 0; tick < journeyPath.Length * 3 && world.Boats[0].Position != alongsideWater!.Value; tick++)
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(alongsideWater!.Value, world.Boats[0].Position);
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
