using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConstructionOrderTests
{
    [Fact]
    public async Task ConstructionWaitsForARealPaidOrdinaryJobThenStartsItsOwnWorkAfterReload()
    {
        var setup = await Baseline.Value;
        var state = Decode(setup);
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "twist-rope");
        var candidate = "build:recipe:" + recipe.CanonicalId;
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "construction-prior-fiber", "fiber", Alpha, 3, storageBuildingId: House)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
        };
        var choose = new ConstructionChoices(candidate);
        using var ordinary = PrivateWorldRuntime.Restore(state, id => id == setup.Actor ? choose : new ConstructionChoices());
        for (var tick = 0; tick < 30 && Person(ordinary, setup.Actor).Project?.JobId is null; tick++) await Tick(ordinary);
        Assert.Contains(choose.Observations, observation => observation.Candidates.Any(item => item.Id == candidate));
        var jobId = Person(ordinary, setup.Actor).Project!.JobId!;
        Assert.Equal(WorldProductionJobState.Running, Assert.Single(ordinary.WorldSimulation.ProductionJobs, item => item.JobId == jobId).State);
        ordinary.Pause();
        using var world = Reload(ordinary);
        var receipt = Submit(world, setup.Actor, "after-paid-work", BuildClinic(setup.Site));
        world.Resume();
        await Tick(world);
        Assert.Equal(jobId, Person(world, setup.Actor).Project!.JobId);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        AssertInputs(world, 10, 4);
        world.Pause();
        using var replay = Reload(world);
        world.Resume();
        replay.Resume();
        await FinishTogether(world, replay, receipt);
        AssertInputs(world, 0, 0);
        Assert.Equal(1, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == Alpha && lot.ItemKind == "rope").Sum(lot => lot.Quantity));
        var paid = Assert.Single(world.WorldSimulation.ProductionJobs, item => item.JobId == jobId);
        Assert.Equal(WorldProductionJobState.Completed, paid.State);
        Assert.All(paid.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(id).State));
        var proof = Assert.Single(world.WorldSimulation.ConstructionReceipts!);
        Assert.Equal(receipt.InstructionId, proof.InstructionId);
        Assert.True(proof.StartedTick >= paid.CompletionTick);
        world.Validate();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ConstructionCanReplaceAnUnpaidOrdinaryPlanAndEarnsOnlyItsOwnPlacementProof(
        bool issueOrder, bool partlyWorked)
    {
        var setup = await Baseline.Value;
        var choices = new ConstructionChoices(setup.CandidateId);
        var state = Decode(setup);
        state = state with { Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray() };
        using var ordinary = PrivateWorldRuntime.Restore(state, id => id == setup.Actor ? choices : new ConstructionChoices());
        await Tick(ordinary);
        Assert.Contains(choices.Observations, observation => observation.Candidates.Any(candidate => candidate.Id == setup.CandidateId));
        Assert.Equal(setup.CandidateId, Person(ordinary, setup.Actor).Project!.CandidateId);
        Assert.Equal("travelling", Person(ordinary, setup.Actor).Project!.Stage);
        if (partlyWorked)
        {
            for (var tick = 0; tick < 30 && Person(ordinary, setup.Actor).Project!.WorkDone < 3; tick++)
                await Tick(ordinary);
            Assert.InRange(Person(ordinary, setup.Actor).Project!.WorkDone, 3, 9);
        }
        var originalProject = Person(ordinary, setup.Actor).Project!;
        Assert.Null(originalProject.JobId);
        Assert.Null(originalProject.OrderInstructionId);
        AssertInputs(ordinary, 10, 4);
        Assert.Empty(ordinary.WorldSimulation.ConstructionReceipts ?? []);
        ordinary.Pause();
        using var world = Reload(ordinary);
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        var receipt = issueOrder ? Submit(world, setup.Actor, "interrupt-clinic", BuildClinic(setup.Site)) : null;
        world.Resume();
        for (var tick = 0; tick < 32; tick++) await Tick(world);
        world.Pause();
        using var replay = Reload(world);
        world.Resume();
        replay.Resume();
        for (var tick = 0; tick < 16; tick++) await TickTogether(world, replay);
        Assert.Single(world.WorldSimulation.Buildings, building => building.DefinitionId == setup.DefinitionId && building.HouseholdId == Alpha);
        AssertInputs(world, 0, 0);
        if (receipt is not null)
        {
            Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            var proof = Assert.Single(world.WorldSimulation.ConstructionReceipts!);
            Assert.Equal(receipt.InstructionId, proof.InstructionId);
            Assert.True(proof.StartedTick > originalProject.StartedTick);
            Assert.Equal(proof.BuildingInstanceId, Order(world, receipt).ConstructionInstanceId);
            Assert.NotNull(Order(world, receipt).LastEffectId);
            Assert.Equal(receipt.InstructionId, Person(world, setup.Actor).Project!.OrderInstructionId);
        }
        else
        {
            Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
            Assert.Equal(originalProject.StartedTick, Person(world, setup.Actor).Project!.StartedTick);
            Assert.Null(Person(world, setup.Actor).Project!.OrderInstructionId);
        }
        world.Validate();
    }
}
