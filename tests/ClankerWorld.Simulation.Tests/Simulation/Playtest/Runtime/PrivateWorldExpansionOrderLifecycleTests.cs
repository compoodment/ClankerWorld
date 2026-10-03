using System.Globalization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldExpansionOrderTests
{
    [Fact]
    public async Task UrgentFoodAtExpansionCompletionPausesPaidWorkUntilTheSameJobResumesAcrossReplay()
    {
        var state = Prepared();
        var actor = Actor(state);
        var original = Building(state, House);
        using var initial = Restore(state);
        var receipt = Submit(initial, actor, "urgent-expansion", "expand my House");
        var job = await StartJob(initial, receipt);
        while (initial.WorldTick < job.CompletionTick - 1) await Tick(initial);
        state = HungryWithMeal(initial.ExportState(), actor);
        using var world = Restore(state);
        await Tick(world);
        Assert.Equal(job.CompletionTick, world.WorldTick);
        Assert.Equal(("interrupted", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(WorldProductionJobState.Paused, Job(world, job.JobId).State);
        Assert.Equal(original, Building(world.ExportState(), House));
        Assert.Equal(52, Quantity(world, "wood"));
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(id).State));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "urgent-food");
        using var replay = Reload(world);
        for (var step = 0; step < 12 && Order(world, receipt).Status != "finished"; step++) await TickTogether(world, replay);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(WorldProductionJobState.Completed, Job(world, job.JobId).State);
        Assert.True(Job(world, job.JobId).CompletionTick > job.CompletionTick);
        Assert.Equal(job.JobId, Order(world, receipt).ExpansionBinding!.JobId);
        Assert.Equal(48, Quantity(world, "wood"));
        Assert.Single(world.WorldSimulation.BuildingExpansions!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrReplacementReleasesOnlyTheOrderedExpansionAndLeavesOrdinaryWork(bool replace)
    {
        var state = WithOtherProductionWorker(Prepared(), out var other);
        var actor = Actor(state);
        using var world = Restore(state);
        var ordinary = StartOrdinaryRope(world, other);
        var receipt = Submit(world, actor, "cancel-expansion", "expand my House");
        var job = await StartJob(world, receipt);
        if (replace) _ = Submit(world, actor, "replacement-order", "store wood");
        else Assert.True(world.CancelOrder(new("stop-expansion", "owner:test", world.Society.WorldId, actor, receipt.InstructionId)).Changed);
        Assert.Equal(("cancelled", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(WorldProductionJobState.Cancelled, Job(world, job.JobId).State);
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Released,
            world.Society.Inventory.GetReservation(id).State));
        Assert.Equal(WorldProductionJobState.Running, world.WorldSimulation.ProductionJobs.Single(item => item.JobId == ordinary.JobId).State);
        Assert.All(ordinary.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(id).State));
        using var replay = Reload(world);
        while (world.WorldTick <= job.CompletionTick + 1) await TickTogether(world, replay);
        Assert.Null(Building(world.ExportState(), House).Footprint);
        Assert.Equal(52, Quantity(world, "wood"));
        Assert.Equal(WorldProductionJobState.Completed, world.WorldSimulation.ProductionJobs.Single(item => item.JobId == ordinary.JobId).State);
        Assert.Equal(1, Quantity(world, "rope"));
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "building_expanded");
    }

    [Fact]
    public async Task DepartureCancelsOnlyTheDepartingWorkersOrderedExpansion()
    {
        var state = WithOtherProductionWorker(Prepared(), out var other);
        var actor = Actor(state);
        using var world = Restore(state);
        var ordinary = StartOrdinaryRope(world, other);
        var receipt = Submit(world, actor, "departure-expansion", "expand my House");
        var job = await StartJob(world, receipt);
        Assert.True(world.DisplaceAdult(actor));
        Assert.Equal(WorldProductionJobState.Cancelled, Job(world, job.JobId).State);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Released,
            world.Society.Inventory.GetReservation(id).State));
        Assert.Equal(WorldProductionJobState.Running, world.WorldSimulation.ProductionJobs.Single(item => item.JobId == ordinary.JobId).State);
        Assert.All(ordinary.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(id).State));
        Assert.Equal((Household, 52), (world.Society.Inventory.GetLot("expansion-wood").OwnerId,
            world.Society.Inventory.GetLot("expansion-wood").Quantity));
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(WorldProductionJobState.Cancelled, Job(world, job.JobId).State);
        Assert.Null(Building(world.ExportState(), House).Footprint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalDeathReleasesRunningOrPausedOrderedExpansionWithoutTouchingAnotherWorkersJob(bool pauseFirst)
    {
        var state = WithOtherProductionWorker(Prepared(), out var other);
        var actor = Actor(state);
        using var initial = Restore(state);
        var ordinary = StartOrdinaryRope(initial, other);
        var receipt = Submit(initial, actor, "last-expansion", "expand my House");
        var job = await StartJob(initial, receipt);
        state = initial.ExportState();
        if (pauseFirst)
        {
            using var hungry = Restore(HungryWithMeal(state, actor));
            await Tick(hungry);
            Assert.Equal(WorldProductionJobState.Paused, Job(hungry, job.JobId).State);
            state = hungry.ExportState();
        }
        var society = state.Society.Society;
        var nextTick = society.WorldTick + 1;
        var maximumDay = society.Config.DayLifecycle!.MaximumDay;
        state = state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == actor ? person with
                    {
                        BirthTick = nextTick - maximumDay * society.Config.TicksPerLifecycleAge,
                        BirthLifeTick = society.LifeClock is null ? null :
                            society.LifeTickAt(nextTick) - maximumDay * society.Config.TicksPerLifecycleAge,
                        AgeBand = SocietyAgeBand.Elder,
                        LastLifecycleYearChecked = maximumDay - 1,
                    } : person).ToArray(),
                },
            },
        };
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(SocietyInhabitantStatus.Dead, world.Society.GetInhabitant(actor).Status);
        Assert.Equal(SocietyDeathCause.NaturalAge, world.Society.GetInhabitant(actor).DeathCause);
        Assert.Equal(nextTick, world.Society.GetInhabitant(actor).DeathTick);
        Assert.Equal(WorldProductionJobState.Cancelled, Job(world, job.JobId).State);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(Building(world.ExportState(), House).Footprint);
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Released,
            world.Society.Inventory.GetReservation(id).State));
        Assert.Equal(WorldProductionJobState.Running, world.WorldSimulation.ProductionJobs.Single(item => item.JobId == ordinary.JobId).State);
        Assert.All(ordinary.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(id).State));
        Assert.Equal(52, Quantity(world, "wood"));
        using var completedDeath = Reload(world);
        Assert.Equal(WorldProductionJobState.Cancelled, Job(completedDeath, job.JobId).State);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("replace")]
    [InlineData("owner")]
    public async Task PreparationKeepsTheOriginalBuildingIdentityAnchorAndOwner(string change)
    {
        var state = Prepared();
        var actor = Actor(state);
        var original = Building(state, House);
        using var walking = Restore(AtAdjacent(state, actor, original));
        var receipt = Submit(walking, actor, "bound-expansion", string.Create(CultureInfo.InvariantCulture,
            $"expand my House at ({original.Position.X}, {original.Position.Y})"));
        await Tick(walking);
        var binding = Assert.IsType<OwnerBuildingExpansionBinding>(Order(walking, receipt).ExpansionBinding);
        Assert.Null(binding.JobId);
        state = walking.ExportState();
        // The intervening world change retains a valid building and stock; an old order may not adopt it.
        var position = change == "move" ? new GridPoint(original.Position.X + 1, original.Position.Y) : original.Position;
        var id = change == "replace" ? "replacement-expansion-house" : House;
        var owner = change == "owner" ? OtherHousehold : Household;
        var buildings = state.WorldSimulation!.Buildings.Where(item => change != "owner" || item.InstanceId != OtherHouse)
            .Select(item => item.InstanceId == House ? item with
            {
                InstanceId = id,
                Position = position,
                HouseholdId = owner,
                Entrance = change == "move" ? null : item.Entrance,
            } : item).OrderBy(item => item.InstanceId, StringComparer.Ordinal).ToArray();
        state = state with
        {
            WorldSimulation = state.WorldSimulation with { Buildings = buildings },
            Towns = state.Towns!.Select(town => town with
            {
                AssignedBuildingIds = town.AssignedBuildingIds.Where(building => change != "owner" || building != OtherHouse)
                    .Select(building => building == House ? id : building).Order(StringComparer.Ordinal).ToArray(),
            }).ToArray(),
        };
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.StorageBuildingId == House
                ? lot with { StorageBuildingId = id, OwnerId = owner } : lot).ToArray(),
        });
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(binding, Order(world, receipt).ExpansionBinding);
        Assert.Empty(world.WorldSimulation.BuildingExpansions ?? []);
        Assert.Equal((id, position, owner, (BuildingFootprintRevision?)null),
            (Building(world.ExportState(), id).InstanceId, Building(world.ExportState(), id).Position,
                Building(world.ExportState(), id).HouseholdId, Building(world.ExportState(), id).Footprint));
    }

    [Fact]
    public async Task ARemovedHouseCannotReuseAnIdentityAlreadyBoundByAnExpansionOrder()
    {
        var state = Prepared();
        var actor = Actor(state);
        var original = Building(state, House);
        using var walking = Restore(AtAdjacent(state, actor, original));
        var receipt = Submit(walking, actor, "bound-house-identity", "expand my House");
        await Tick(walking);
        var binding = Assert.IsType<OwnerBuildingExpansionBinding>(Order(walking, receipt).ExpansionBinding);
        Assert.Null(binding.JobId);
        state = walking.ExportState();
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.StorageBuildingId == House).ToArray())
            inventory = InventoryFixture.Relocate(inventory, "empty-bound-" + lot.Id, lot.Id, lot.OwnerId, lot.Quantity,
                groundPosition: new InventoryGroundPosition(original.Position.X, original.Position.Y));
        using var world = Restore(WithInventory(state, inventory));
        var removal = world.RemoveBuilding(House, original.TownId, original.HouseholdId);
        Assert.True(removal.Applied, removal.Failure);
        Assert.Empty(world.WorldSimulation.BuildingExpansions ?? []);
        using (var control = Reload(world))
        {
            var placement = control.PlaceBuilding("replacement-house-control", original.DefinitionId,
                original.Position, Household);
            Assert.True(placement.Applied, placement.Failure);
            var woodCost = control.WorldContent.Buildings.Single(item => item.CanonicalId == original.DefinitionId)
                .BuildCosts.Where(item => item.ResourceId == "wood").Sum(item => item.Amount);
            Assert.Equal(52 - woodCost, Quantity(control, "wood"));
            using var replacementReplay = Reload(control);
            await TickTogether(control, replacementReplay);
            Assert.Equal(("blocked", 0), (Order(control, receipt).Status, Order(control, receipt).CompletedUnits));
            Assert.Equal(binding, Order(control, receipt).ExpansionBinding);
        }
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var reused = world.PlaceBuilding(House, original.DefinitionId, original.Position, Household);
        Assert.False(reused.Applied);
        Assert.NotEmpty(reused.Failure!);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(52, Quantity(world, "wood"));
        using var replay = Reload(world);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(binding, Order(world, receipt).ExpansionBinding);
        Assert.Empty(world.WorldSimulation.BuildingExpansions ?? []);
    }

    [Fact]
    public async Task CompletedExpansionProgressRemainsValidAfterTheEmptyBuildingIsRemoved()
    {
        var state = Prepared();
        var actor = Actor(state);
        using var initial = Restore(state);
        var receipt = Submit(initial, actor, "historical-expansion", "expand my House");
        await Finish(initial, receipt);
        var binding = Assert.IsType<OwnerBuildingExpansionBinding>(Order(initial, receipt).ExpansionBinding);
        var job = Job(initial, binding.JobId!);
        state = initial.ExportState();
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.StorageBuildingId == House).ToArray())
            inventory = InventoryFixture.Relocate(inventory, "empty-completed-" + lot.Id, lot.Id, lot.OwnerId, lot.Quantity,
                groundPosition: new InventoryGroundPosition(binding.ExpectedPosition.X, binding.ExpectedPosition.Y));
        using var world = Restore(WithInventory(state, inventory));
        var expanded = Building(world.ExportState(), House);
        var removal = world.RemoveBuilding(House, expanded.TownId, expanded.HouseholdId);
        Assert.True(removal.Applied, removal.Failure);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(binding, Order(world, receipt).ExpansionBinding);
        Assert.Equal(WorldProductionJobState.Completed, Job(world, job.JobId).State);
        Assert.Equal(job.DefinitionId, Job(world, job.JobId).DefinitionId);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.InstanceId == House);
        Assert.Single(world.ExportState().Events, item => item.Kind == "building_expanded");
        Assert.StartsWith("expand:job:", Order(world, receipt).LastEffectId, StringComparison.Ordinal);
    }

    private static PrivateWorldRuntimeState HungryWithMeal(PrivateWorldRuntimeState state, string actor) =>
        WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "urgent-food", "berries", actor, 1)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
        };
    private static PrivateWorldRuntimeState WithOtherProductionWorker(PrivateWorldRuntimeState state, out string other)
    {
        var actor = Actor(state);
        other = state.Society.Society.GetHousehold(Household).MemberIds.Single(id => id != actor);
        var worker = other;
        var position = Building(state, House).Position;
        return WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "ordinary-fiber", "fiber", Household, 3, storageBuildingId: House)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker
                ? person with { Position = position } : person).ToArray(),
        };
    }
    private static WorldProductionJob StartOrdinaryRope(PrivateWorldRuntime world, string actor)
    {
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "twist-rope");
        var result = world.StartProduction(recipe.CanonicalId, House, actor);
        Assert.True(result.Applied, result.Failure);
        return world.WorldSimulation.ProductionJobs.Single(item => item.JobId == result.JobId);
    }
}
