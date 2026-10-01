using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class FarmGrainCookingReserveTests
{
    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public async Task MillingHaulsOnlyGrainAboveTheHousesActualCookingSupply(int stocked, int surplus)
    {
        var (state, actor, household, _) = FarmFieldTests.PreparedFarmer("field-grain-cooking-reserve");
        state = FarmFieldTests.FeedHouseholdFromAvailableStock(state, household);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "house-cooking-grain", "grain", household,
            stocked, storageBuildingId: house.InstanceId);
        // Keep the fixture's existing planting stock at its own Farmhouse so
        // this boundary observes grain, rather than an unrelated first seed haul.
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.OwnerId == household && lot.ItemKind != "grain" &&
                FarmFieldRules.IsFarmStock(lot.ItemKind) && lot.StorageBuildingId is null && lot.GroundPosition is null
                    ? lot with { StorageBuildingId = "first-town-farmhouse" } : lot).ToArray(),
        };
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position, LastDecisionContext = null } : person).ToArray(),
        };
        var provider = new GrainProvider(actor);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        for (var tick = 0; tick < 12 && !world.ExportState().Events.Any(item => item.Kind == "farm_grain_picked_up"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(surplus > 0, provider.Offered.Contains("haul_farm_grain"));
        Assert.Equal(2, world.Society.Inventory.GetLot("house-cooking-grain").Quantity);
        Assert.Equal(house.InstanceId, world.Society.Inventory.GetLot("house-cooking-grain").StorageBuildingId);
        if (surplus == 0)
        {
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "farm_grain_picked_up");
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "house-cooking-grain");
        }
        else
        {
            var carried = Assert.Single(world.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "house-cooking-grain");
            Assert.Equal(actor, carried.OwnerId);
            Assert.Equal(surplus, carried.Quantity);
            Assert.Equal("first-town-farmhouse", carried.DeliveryBuildingId);
            Assert.Null(carried.GroundPosition);
        }
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = FarmFieldTests.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    private sealed class GrainProvider(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Observation.InhabitantId == actor)
                foreach (var candidate in request.Observation.Candidates) Offered.Add(candidate.Id);
            var selected = request.Observation.InhabitantId == actor
                ? request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "haul_farm_grain") : null;
            selected ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}
