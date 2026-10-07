using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StoreStockingPreservesOrdinaryWorkAndItsRealProductionPaymentAcrossReload(
        bool issueOrder, bool paidJob)
    {
        var (state, actor, household, house, store) = CreateStoreStockFixture();
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != household && lot.OwnerId != actor).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "interrupt-fiber", "fiber", household, 3, storageBuildingId: house);
        inventory = InventoryFixture.AddLot(inventory, "interrupt-cloth", "cloth", actor, 2);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 10_000, LastDecisionContext = null }).ToArray(),
        };
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "twist-rope");
        var candidate = "build:recipe:" + recipe.CanonicalId;
        var choose = new ShopProvider(candidate);
        using var ordinary = PrivateWorldRuntime.Restore(state, id => id == actor ? choose : new ShopProvider("safe_idle"));
        Assert.True((await ordinary.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(candidate, choose.Seen);
        Assert.Equal(1, ordinary.Inhabitants.Single(person => person.InhabitantId == actor).Project!.WorkDone);
        if (paidJob)
        {
            for (var tick = 0; tick < 20 && ordinary.Inhabitants.Single(person => person.InhabitantId == actor).Project!.JobId is null; tick++)
                Assert.True((await ordinary.AdvanceOneTickAsync()).Advanced);
            var jobId = ordinary.Inhabitants.Single(person => person.InhabitantId == actor).Project!.JobId!;
            var job = Assert.Single(ordinary.WorldSimulation.ProductionJobs, item => item.JobId == jobId);
            Assert.Equal(WorldProductionJobState.Running, job.State);
            Assert.NotEmpty(job.InputReservationIds);
        }
        var original = ordinary.Inhabitants.Single(person => person.InhabitantId == actor).Project!;
        ordinary.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(ordinary.ExportState());
        IDecisionProvider ChoicesFor(string id) => id == actor ? new ShopProvider("deliver_stock", candidate) : new ShopProvider("safe_idle");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), ChoicesFor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var receipt = issueOrder ? world.SubmitInstruction(new("interrupt-store", "owner:test", actor,
            OwnerInstructionKind.MustDo, "stock two cloth in my Store")) : null;
        world.Resume();
        for (var tick = 0; tick < 16; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Pause();
        bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), ChoicesFor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        world.Resume();
        replay.Resume();
        for (var tick = 0; tick < 48; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(1, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind == "rope").Sum(lot => lot.Quantity));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == household && lot.ItemKind == "fiber");
        var finished = world.Inhabitants.Single(person => person.InhabitantId == actor).Project!;
        Assert.Equal((original.CandidateId, original.StartedTick), (finished.CandidateId, finished.StartedTick));
        Assert.Equal("completed", finished.Stage);
        if (receipt is not null)
        {
            var order = world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
            Assert.Equal(("finished", 2), (order.Status, order.CompletedUnits));
            Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == store && lot.ItemKind == "cloth").Sum(lot => lot.Quantity));
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "cloth");
        }
        else
        {
            Assert.Equal(2, world.Society.Inventory.GetLot("interrupt-cloth").Quantity);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.StorageBuildingId == store && lot.ItemKind == "cloth");
        }
        var completedJob = Assert.Single(world.WorldSimulation.ProductionJobs, item => item.RecipeId == recipe.CanonicalId && item.WorkerId == actor);
        Assert.Equal(WorldProductionJobState.Completed, completedJob.State);
        Assert.All(completedJob.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(id).State));
        world.Validate();
    }
}
