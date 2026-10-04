using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ReachableFarmStockTests
{
    [Fact]
    public Task RemovingOnlyTheBlockedRootAllowsTheSameGrainDelivery() =>
        AssertDeliveryAsync(inVessel: false, includeBlockedRoot: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AnEarlierBlockedRootDoesNotHideReachableFarmStock(bool inVessel) =>
        AssertDeliveryAsync(inVessel, includeBlockedRoot: true);

    private static async Task AssertDeliveryAsync(bool inVessel, bool includeBlockedRoot)
    {
        using var generated = NormalPathWorld.CreateGenerated("audit-town-invariants", _ => new HaulChoices(false));
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var original = generated.ExportState();
        const string actor = "founder:00000000000000000000000000000001";
        var blocker = original.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        var household = Assert.IsType<string>(original.Society.Society.GetInhabitant(actor).HouseholdId);
        var farmhouse = Assert.Single(original.WorldSimulation!.Buildings, building =>
            building.HouseholdId == household && generated.WorldContent.Buildings.Single(definition =>
                definition.CanonicalId == building.DefinitionId).Tags.Contains("farmhouse"));
        var source = new GridPoint(125, 57);
        var blockedSource = new GridPoint(126, 57);
        var tick = original.Society.Society.WorldTick;
        var quantity = inVessel ? 2 : 4;
        const string blockedRoot = "a-blocked-stock";
        const string reachableRoot = "b-reachable-stock";
        const string blockedGrain = "a-blocked-grain";
        const string reachableGrain = "b-reachable-grain";
        var inventory = original.Society.Society.Inventory;
        if (inVessel)
        {
            inventory = InventoryFixture.AddLot(inventory, blockedRoot, InventoryContainerRules.StoragePot,
                household, 1, tick, groundPosition: new(blockedSource.X, blockedSource.Y));
            inventory = InventoryFixture.AddLot(inventory, blockedGrain, "grain", household, quantity,
                tick, containerLotId: blockedRoot);
            inventory = InventoryFixture.AddLot(inventory, reachableRoot, InventoryContainerRules.StoragePot,
                household, 1, tick, groundPosition: new(source.X, source.Y));
            inventory = InventoryFixture.AddLot(inventory, reachableGrain, "grain", household, quantity,
                tick, containerLotId: reachableRoot);
        }
        else
        {
            inventory = InventoryFixture.AddLot(inventory, blockedGrain, "grain", household, quantity,
                tick, groundPosition: new(blockedSource.X, blockedSource.Y));
            inventory = InventoryFixture.AddLot(inventory, reachableGrain, "grain", household, quantity,
                tick, groundPosition: new(source.X, source.Y));
        }
        if (!includeBlockedRoot)
            inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.Id != blockedGrain).ToArray() };
        var initial = original with
        {
            Society = original.Society with { Society = original.Society.Society with { Inventory = inventory } },
            Inhabitants = original.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = source, HungerBasisPoints = 9_000 }
                : person.InhabitantId == blocker ? person with { Position = blockedSource } : person).ToArray(),
        };
        var initialGrain = inventory.Lots.Where(lot => lot.ItemKind == "grain").Sum(lot => lot.Quantity);
        var movedRoot = inVessel ? reachableRoot : reachableGrain;
        var blockedFamily = inventory.Lots.Where(lot => lot.Id == blockedGrain || lot.Id == blockedRoot).ToArray();
        using var world = Restore(initial, actor);
        for (var count = 0; count < 12 && !world.ExportState().Events.Any(item =>
                 item.Kind == "farm_grain_picked_up" && item.Detail ==
                 $"{actor}:{reachableGrain}:{quantity}:{farmhouse.InstanceId}"); count++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(world.ExportState().Events, item => item.Kind == "farm_grain_picked_up" &&
            item.Detail == $"{actor}:{reachableGrain}:{quantity}:{farmhouse.InstanceId}");
        Assert.Equal(source, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        foreach (var lot in world.Society.Inventory.Lots.Where(lot => lot.Id == movedRoot || lot.Id == reachableGrain))
        {
            Assert.Equal(actor, lot.OwnerId);
            Assert.Equal(farmhouse.InstanceId, lot.DeliveryBuildingId);
            Assert.Null(lot.StorageBuildingId);
            Assert.Null(lot.GroundPosition);
        }
        Assert.Equal(inVessel ? reachableRoot : null, world.Society.Inventory.GetLot(reachableGrain).ContainerLotId);
        AssertBlockedFamilyUnchanged(world, blockedFamily);

        // Continue the real delivery intention in both worlds, without resetting cognition.
        using var replay = Restore(world.ExportState(), actor);
        for (var count = 0; count < 40 && world.Society.Inventory.GetLot(movedRoot).StorageBuildingId != farmhouse.InstanceId; count++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(farmhouse.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.NotEqual(source, farmhouse.Position);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal) &&
            item.Detail.EndsWith(":household_stock", StringComparison.Ordinal));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "household_stock_delivered" &&
            item.Detail == $"{actor}:{movedRoot}:{(inVessel ? 1 : quantity)}:{farmhouse.InstanceId}");
        foreach (var lot in world.Society.Inventory.Lots.Where(lot => lot.Id == movedRoot || lot.Id == reachableGrain))
        {
            Assert.Equal(household, lot.OwnerId);
            Assert.Equal(farmhouse.InstanceId, lot.StorageBuildingId);
            Assert.Null(lot.DeliveryBuildingId);
            Assert.Null(lot.GroundPosition);
        }
        Assert.Equal(quantity, world.Society.Inventory.GetLot(reachableGrain).Quantity);
        Assert.Equal(inVessel ? reachableRoot : null, world.Society.Inventory.GetLot(reachableGrain).ContainerLotId);
        if (inVessel) Assert.Equal(1, world.Society.Inventory.GetLot(reachableRoot).Quantity);
        Assert.Equal(initialGrain, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "grain").Sum(lot => lot.Quantity));
        Assert.Equal(inventory.Reservations, world.Society.Inventory.Reservations);
        Assert.Equal(blockedSource, world.Inhabitants.Single(person => person.InhabitantId == blocker).Position);
        AssertBlockedFamilyUnchanged(world, blockedFamily);
        using var final = Restore(world.ExportState(), actor);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(final.ExportState()));
    }

    private static void AssertBlockedFamilyUnchanged(PrivateWorldRuntime world, InventoryLot[] family)
    {
        foreach (var expected in family)
        {
            var actual = world.Society.Inventory.GetLot(expected.Id);
            Assert.Equal(expected with { LastProcessedTick = actual.LastProcessedTick }, actual);
        }
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => new HaulChoices(id == actor));

    private sealed class HaulChoices(bool haul) : IDecisionProvider
    {
        private readonly DeterministicDecisionProvider chooser = new();
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => haul &&
                candidate.Id == "haul_household_stock" && candidate.DestinationId == "first-town-farmhouse")
                ?? request.Observation.Candidates.FirstOrDefault(candidate => haul && candidate.Id == "haul_farm_grain")
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return chooser.DecideAsync(request with { Observation = request.Observation with { Candidates = [selected] } },
                cancellationToken);
        }
    }
}
