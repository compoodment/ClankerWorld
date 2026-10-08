using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class ReachableHouseHaulingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOccupiedPileDoesNotHideReachableHouseholdSupplies(bool includeBlocked)
    {
        using var setup = NormalPathWorld.CreateGenerated("house-haul-route-audit", _ => new HaulChooser());
        for (var tick = 0; tick < 8; tick++) Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        const string home = "household:camp-alpha";
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == home && person.AgeBand == SocietyAgeBand.Adult).Id;
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var blocked = new GridPoint(house.Position.X, house.Position.Y - 1);
        var reachable = new GridPoint(house.Position.X + 1, house.Position.Y);
        Assert.True(state.Map.IsPassable(blocked));
        Assert.True(state.Map.IsPassable(reachable));
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => lot.OwnerId != actor &&
            (lot.OwnerId != home || lot.StorageBuildingId is not null)).ToArray()
        };
        if (includeBlocked) inventory = InventoryFixture.AddLot(inventory, "a-blocked-haul-stock", "wood", home, 4,
            groundPosition: new(blocked.X, blocked.Y));
        inventory = InventoryFixture.AddLot(inventory, "b-reachable-haul-stock", "wood", home, 4,
            groundPosition: new(reachable.X, reachable.Y));
        var others = state.Inhabitants.Where(person => person.InhabitantId != actor).ToArray();
        var positions = state.Map.FootNeighbors(house.Position).Where(point => state.Map.IsPassable(point) &&
            point != blocked && point != reachable).Take(others.Length - 1).Prepend(blocked).ToArray();
        var occupied = positions.ToHashSet();
        using (var routes = new UnoccupiedRouteSearch(state.Map, house.Position, occupied, state.Map.FootStepCost))
        {
            Assert.Empty(routes.RouteTo(blocked, 0));
            Assert.True(routes.RouteTo(reachable, 0).Count > 0);
        }
        using (var routes = new UnoccupiedRouteSearch(state.Map, reachable, occupied, state.Map.FootStepCost))
            Assert.True(routes.RouteTo(house.Position, 0).Count > 0);
        var assignments = others.Select((person, index) => (person.InhabitantId, Position: positions[index]))
            .ToDictionary(item => item.InhabitantId, item => item.Position, StringComparer.Ordinal);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? house.Position : assignments[person.InhabitantId],
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
                Project = null,
                Equipment = person.InhabitantId == actor ? null : person.Equipment
            }).ToArray()
        };
        var wood = inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        var chooser = new HaulChooser(true);
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), id => id == actor ? chooser : new HaulChooser());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), id => new HaulChooser(id == actor));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 30; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.True(chooser.Offered);
        var delivered = world.Society.Inventory.GetLot("b-reachable-haul-stock");
        Assert.Equal((home, house.InstanceId, 4), (delivered.OwnerId, delivered.StorageBuildingId, delivered.Quantity));
        Assert.Null(delivered.CarrierId);
        Assert.Null(delivered.GroundPosition);
        if (includeBlocked)
        {
            var untouched = world.Society.Inventory.GetLot("a-blocked-haul-stock");
            Assert.Equal((home, 4, new InventoryGroundPosition(blocked.X, blocked.Y)), (untouched.OwnerId, untouched.Quantity, untouched.GroundPosition));
            Assert.Null(untouched.CarrierId);
        }
        Assert.Equal(wood, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.All(assignments, item => Assert.Equal(item.Value, world.Inhabitants.Single(person => person.InhabitantId == item.Key).Position));
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), id => new HaulChooser(id == actor));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        world.Validate();
        reload.Validate();
    }

    private sealed class HaulChooser(bool haul = false) : IDecisionProvider
    {
        public bool Offered { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered |= request.Observation.Candidates.Any(candidate => candidate.Id == "haul_household_stock");
            var choice = request.Observation.Candidates.FirstOrDefault(candidate => haul && candidate.Id == "haul_household_stock") ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
