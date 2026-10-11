using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldCollectionOrderTests
{
    [Theory]
    [InlineData(false, "wood", 0, 10_000)]
    [InlineData(true, "wood", 0, 10_000)]
    [InlineData(false, "fruit", 10_000, 0)]
    [InlineData(true, "fruit", 10_000, 0)]
    [InlineData(false, "storage_pot", 0, 10_000)]
    [InlineData(true, "storage_pot", 0, 10_000)]
    public async Task RoutineAndOrdersRecoverDamagedOrSpoiledPropertyWithPhysicalLimits(bool order, string kind,
        int condition, int freshness)
    {
        var state = Prepared();
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "recover-property", kind, actor,
            kind == "storage_pot" ? 1 : 2, storageBuildingId: House);
        if (kind == "storage_pot")
            inventory = InventoryFixture.AddLot(inventory, "recover-contents", "fruit", actor, 1,
                storageBuildingId: House, containerLotId: "recover-property");
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "recover-property" ? lot with
            { ConditionBasisPoints = condition, FreshnessBasisPoints = freshness } : lot).ToArray(),
        };
        state = WithInventory(state, inventory) with { JevEnabled = false, RoutineHelper = RoutineHelperSettings.Off };
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor && !order
            ? new RecoveryMigrationChoice() : new CollectionChoices());
        OwnerInstructionReceipt? receipt = order ? Submit(world, actor, "recover", "collect my " + kind.Replace('_', ' ')) : null;
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), id => id == actor && !order
            ? new RecoveryMigrationChoice() : new CollectionChoices());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var moved = world.Society.Inventory.GetLot("recover-property");
        Assert.Equal(actor, moved.OwnerId);
        Assert.True(PersonalEquipmentRules.IsCarried(moved, actor));
        Assert.Null(moved.StorageBuildingId);
        if (kind == "storage_pot")
            Assert.True(PersonalEquipmentRules.IsPhysicallyCarried(world.Society.Inventory,
                world.Society.Inventory.GetLot("recover-contents"), actor));
        Assert.Single(world.ExportState().Events, item => item.Kind == "personal_goods_collected");
        if (receipt is not null) Assert.Equal("finished", Order(world, receipt).Status);
        world.Validate();
        var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(final));
        Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }

    private sealed class RecoveryMigrationChoice : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.Any(candidate => candidate.Id == "household_collect:recover-property")
                ? "household_collect:recover-property" : "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}
