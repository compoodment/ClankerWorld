using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldProductionOrderTests
{
    private const string Household = "household:camp-alpha";
    private const string House = "first-town-house-a";
    private const string Shop = "alpha-tailor";
    private static readonly Lazy<byte[]> Baseline = new(() =>
        PrivateWorldRuntimeCodec.Encode(TailorTestWorld.Create("equipment-repair-orders", 0).State));

    [Fact]
    public async Task TwoSacksRequireTwoCompletedJobsAndReplayBeforeAndAfterReservation()
    {
        var state = Prepared(Shop);
        state = Stock(state, Shop, "cloth", 4);
        state = Stock(state, Shop, "rope", 2);
        using var world = Restore(state);
        var actor = Actor(state);
        var receipt = Submit(world, actor, "two-sacks", "make two sacks");
        await Tick(world);
        Assert.Equal(("doing", 0, 2, "output_items"),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits,
                Order(world, receipt).RequestedUnits, Order(world, receipt).ProgressUnit));
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        Assert.Equal("working", Person(world, actor).Project!.Stage);
        using var midProject = Reload(world);
        for (var tick = 0; tick < 20 && world.WorldSimulation.ProductionJobs.Count == 0; tick++)
            await TickTogether(world, midProject);
        var first = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(WorldProductionJobState.Running, first.State);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(0, Produced(world, "sack"));
        Assert.Equal(4, Quantity(world, Household, "cloth"));
        Assert.All(first.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(id).State));
        using var midJob = Reload(world);
        for (var tick = 0; tick < 90 && Order(world, receipt).Status != "finished"; tick++)
            await TickTogether(world, midJob);

        Assert.Equal(("finished", 2), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        var jobs = world.WorldSimulation.ProductionJobs;
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, job =>
        {
            Assert.Equal(WorldProductionJobState.Completed, job.State);
            Assert.Equal(actor, job.WorkerId);
            Assert.Equal(receipt.InstructionId, job.OrderInstructionId);
            Assert.Equal(Shop, job.BuildingInstanceId);
            Assert.Equal(Household, job.OwnerId);
            var output = world.Society.Inventory.GetLot(job.JobId + ":output:00");
            Assert.Equal(("sack", 1, Household, Shop),
                (output.ItemKind, output.Quantity, output.OwnerId, output.StorageBuildingId));
            Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
                world.Society.Inventory.GetReservation(id).State));
        });
        Assert.Equal(0, Quantity(world, Household, "cloth"));
        Assert.Equal(0, Quantity(world, Household, "rope"));
        Assert.Equal(2, Produced(world, "sack"));
        Assert.Equal(2, world.ExportState().Events.Count(item => item.Kind == "recipe_completed"));
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId).Order!;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldInstructionOrder>(
            JsonSerializer.Serialize(projected, json), json)!;
        Assert.Equal(("sack", "output_items", 2, 2),
            (client.TargetOutputKind, client.ProgressUnit, client.RequestedUnits, client.CompletedUnits));
        await TickTogether(world, midJob);
        Assert.Equal(2, Produced(world, "sack"));
        world.Validate();
    }

    [Fact]
    public async Task UncountedRopeMakesOneRealOutputAndStopsWithSpareMaterials()
    {
        var state = Stock(Prepared(House), House, "fiber", 6);
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "rope", "make rope");
        await Finish(world, receipt);
        Assert.Equal(("finished", 1, "production_batches"),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ProgressUnit));
        Assert.Equal(1, Produced(world, "rope"));
        Assert.Equal(3, Quantity(world, Household, "fiber"));
        for (var tick = 0; tick < 15; tick++) await Tick(world);
        Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(1, Produced(world, "rope"));
    }

    [Theory]
    [InlineData("make four house bandages", "output_items", 4, 2)]
    [InlineData("make two batches of house bandages", "production_batches", 2, 2)]
    [InlineData("make house bandages", "production_batches", 1, 1)]
    public async Task MultiOutputRecipesKeepItemAndBatchProgressDistinct(string text, string unit, int count, int batches)
    {
        var state = Stock(Prepared(House), House, "cloth", batches);
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "bandages", text);
        await Finish(world, receipt);
        Assert.Equal(("finished", unit, count),
            (Order(world, receipt).Status, Order(world, receipt).ProgressUnit, Order(world, receipt).CompletedUnits));
        Assert.Equal(batches, world.WorldSimulation.ProductionJobs.Count);
        Assert.Equal(2 * batches, Produced(world, "bandage"));
        Assert.Equal(0, Quantity(world, Household, "cloth"));
        Assert.All(world.WorldSimulation.ProductionJobs, job => Assert.Equal(2,
            world.Society.Inventory.GetLot(job.JobId + ":output:00").Quantity));
    }

    [Theory]
    [InlineData("off-site")]
    [InlineData("foreign-owner")]
    [InlineData("foreign-workstation")]
    [InlineData("reserved")]
    [InlineData("wrong-site")]
    public async Task ProductionCannotSubstituteUnavailableInputsOrAnotherWorkstation(string boundary)
    {
        var state = Prepared(House);
        var other = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-beta").Id;
        state = Stock(state, boundary == "off-site" ? Shop : House, "fiber", 3,
            boundary == "foreign-owner" ? other : Household);
        if (boundary == "reserved")
            state = WithInventory(state, InventoryFixture.Reserve(state.Society.Society.Inventory,
                "other-work", Household, "order-input-fiber", 3, "other_work", 1_000));
        using var world = Restore(state);
        var target = state.WorldSimulation!.Buildings.Single(item => item.InstanceId ==
            (boundary == "foreign-workstation" ? House : Shop)).Position;
        var text = boundary is "wrong-site" or "foreign-workstation"
            ? string.Create(CultureInfo.InvariantCulture, $"make rope at ({target.X}, {target.Y})") : "make rope";
        var receipt = Submit(world, boundary == "foreign-workstation" ? other : Actor(state), "blocked", text);
        for (var tick = 0; tick < 3; tick++) await Tick(world);
        Assert.Equal(("blocked", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.False(string.IsNullOrWhiteSpace(Order(world, receipt).BlockedReason));
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        Assert.Equal(0, Produced(world, "rope"));
        Assert.Equal(3, world.Society.Inventory.GetLot("order-input-fiber").Quantity);
        if (boundary == "reserved")
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("other-work").State);
        using var restored = Reload(world);
        await TickTogether(world, restored);
    }

    [Fact]
    public async Task ARecipeOrderWalksToItsPinnedHouseBeforeStartingProduction()
    {
        var state = Stock(Prepared(Shop), House, "fiber", 3);
        var actor = Actor(state);
        var start = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var destination = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
        Assert.NotEqual(start, destination);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "walk-to-house",
            string.Create(CultureInfo.InvariantCulture, $"make rope at ({destination.X}, {destination.Y})"));
        await Tick(world);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        Assert.Contains(Person(world, actor).Position, state.Map.FootNeighbors(start).Append(start));
        await Finish(world, receipt);
        var job = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(House, job.BuildingInstanceId);
        Assert.Equal(destination, Person(world, actor).Position);
        Assert.Equal(1, Produced(world, "rope"));
        Assert.Equal(0, Quantity(world, Household, "fiber"));
    }

    [Fact]
    public async Task UrgentFoodAtTheCompletionBoundaryPausesTheJobAndResumesAfterReload()
    {
        var state = Stock(Prepared(House), House, "fiber", 3);
        var actor = Actor(state);
        using var initial = Restore(state);
        var receipt = Submit(initial, actor, "urgent", "make rope");
        var job = await StartJob(initial);
        while (initial.WorldTick < job.CompletionTick - 1) await Tick(initial);
        state = initial.ExportState();
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "urgent-food", "berries", actor, 1)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
        };
        using var world = Restore(state);
        await Tick(world);
        Assert.Equal(job.CompletionTick, world.WorldTick);
        Assert.Equal(("interrupted", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(WorldProductionJobState.Paused, Job(world, job.JobId).State);
        Assert.Equal(0, Produced(world, "rope"));
        Assert.Equal(3, Quantity(world, Household, "fiber"));
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(id).State));
        using var restored = Reload(world);
        for (var tick = 0; tick < 20 && Order(world, receipt).Status != "finished"; tick++)
            await TickTogether(world, restored);
        Assert.Equal(("finished", 1), (Order(restored, receipt).Status, Order(restored, receipt).CompletedUnits));
        Assert.Equal(WorldProductionJobState.Completed, Job(restored, job.JobId).State);
        Assert.True(Job(restored, job.JobId).CompletionTick > job.CompletionTick);
        Assert.Equal(1, Produced(restored, "rope"));
        Assert.Equal(0, Quantity(restored, Household, "fiber"));
        Assert.DoesNotContain(restored.Society.Inventory.Lots, lot => lot.Id == "urgent-food");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrReplacementReleasesOnlyItsJobAndNeverProducesLater(bool replace)
    {
        var state = Stock(Prepared(House), House, "fiber", 3);
        state = Stock(state, House, "cloth", 1);
        state = WithInventory(state, InventoryFixture.Reserve(state.Society.Society.Inventory,
            "unrelated-claim", Household, "order-input-cloth", 1, "other_work", 1_000));
        var actor = Actor(state);
        using var world = Restore(state);
        var receipt = Submit(world, actor, "cancelled-production", "make rope");
        var job = await StartJob(world);
        if (replace) _ = Submit(world, actor, "replacement", "store wood");
        else Assert.True(world.CancelOrder(new("stop-production", "owner:test", world.Society.WorldId,
            actor, receipt.InstructionId)).Changed);
        Assert.Equal(("cancelled", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(WorldProductionJobState.Cancelled, Job(world, job.JobId).State);
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Released,
            world.Society.Inventory.GetReservation(id).State));
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("unrelated-claim").State);
        Assert.Equal(3, Quantity(world, Household, "fiber"));
        using var restored = Reload(world);
        while (restored.WorldTick <= job.CompletionTick + 2) await Tick(restored);
        Assert.Equal(0, Produced(restored, "rope"));
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind == "recipe_completed");
        Assert.Equal(InventoryReservationState.Reserved, restored.Society.Inventory.GetReservation("unrelated-claim").State);
    }

    [Fact]
    public async Task AnExistingOrdinaryJobIsNotAdoptedAsTheNewOrdersOutput()
    {
        var state = Stock(Prepared(House), House, "fiber", 6);
        var actor = Actor(state);
        using var world = Restore(state);
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "twist-rope");
        var started = world.StartProduction(recipe.CanonicalId, House, actor);
        Assert.True(started.Applied, started.Failure);
        await Tick(world);
        var receipt = Submit(world, actor, "new-work", "make rope");
        for (var tick = 0; tick < 20 && Job(world, started.JobId!).State == WorldProductionJobState.Running; tick++) await Tick(world);
        Assert.Equal(WorldProductionJobState.Completed, Job(world, started.JobId!).State);
        Assert.Null(Job(world, started.JobId!).OrderInstructionId);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(1, Produced(world, "rope"));
        await Finish(world, receipt);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(2, Produced(world, "rope"));
        Assert.Equal(2, world.WorldSimulation.ProductionJobs.Count);
        Assert.Equal(0, Quantity(world, Household, "fiber"));
    }

    [Fact]
    public async Task DisplacementImmediatelyReleasesOrderedWorkButKeepsOrdinaryHouseholdWorkPaused()
    {
        var state = Stock(Stock(Stock(Prepared(Shop), Shop, "cloth", 2), Shop, "rope", 1), House, "fiber", 3);
        var actor = Actor(state);
        using var initial = Restore(state);
        var recipe = initial.WorldContent.Recipes.Single(item => item.LocalId == "sew-sack");
        var started = initial.StartProduction(recipe.CanonicalId, Shop, actor);
        Assert.True(started.Applied, started.Failure);
        state = initial.ExportState();
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position } : person).ToArray(),
        };
        using var world = Restore(state);
        var receipt = Submit(world, actor, "displaced-production", "make rope");
        for (var tick = 0; tick < 20 && Order(world, receipt).ProductionJobId is null; tick++) await Tick(world);
        var ordered = Job(world, Assert.IsType<string>(Order(world, receipt).ProductionJobId));
        Assert.Equal(WorldProductionJobState.Running, ordered.State);
        Assert.Equal(WorldProductionJobState.Running, Job(world, started.JobId!).State);

        Assert.True(world.DisplaceAdult(actor));
        Assert.Equal(WorldProductionJobState.Cancelled, Job(world, ordered.JobId).State);
        Assert.All(ordered.InputReservationIds, id => Assert.Equal(InventoryReservationState.Released,
            world.Society.Inventory.GetReservation(id).State));
        var ordinary = Job(world, started.JobId!);
        Assert.Equal(WorldProductionJobState.Paused, ordinary.State);
        Assert.All(ordinary.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            world.Society.Inventory.GetReservation(id).State));
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(0, Produced(world, "rope"));
        Assert.Equal(0, Produced(world, "sack"));
        using var restored = Reload(world);
        await TickTogether(world, restored);
        Assert.Equal(WorldProductionJobState.Paused, Job(restored, ordinary.JobId).State);
        Assert.Equal(WorldProductionJobState.Cancelled, Job(restored, ordered.JobId).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWorkersNaturalDeathCancelsRunningOrPausedOrdersWithoutProducingGoods(bool pauseFirst)
    {
        var state = Stock(Prepared(House), House, "fiber", 3);
        var actor = Actor(state);
        using var initial = Restore(state);
        var receipt = Submit(initial, actor, "dying-worker", "make rope");
        var job = await StartJob(initial);
        state = initial.ExportState();
        if (pauseFirst)
        {
            state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
                "last-meal", "berries", actor, 1)) with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
            };
            using var interrupted = Restore(state);
            await Tick(interrupted);
            Assert.Equal(WorldProductionJobState.Paused, Job(interrupted, job.JobId).State);
            state = interrupted.ExportState();
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
        Assert.Equal(SocietyInhabitantStatus.Active, world.Society.GetInhabitant(actor).Status);
        await TickTogether(world, replay);
        Assert.Equal(SocietyInhabitantStatus.Dead, world.Society.GetInhabitant(actor).Status);
        Assert.Equal(SocietyDeathCause.NaturalAge, world.Society.GetInhabitant(actor).DeathCause);
        Assert.Equal(nextTick, world.Society.GetInhabitant(actor).DeathTick);
        Assert.Equal(WorldProductionJobState.Cancelled, Job(world, job.JobId).State);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Equal(0, Produced(world, "rope"));
        Assert.Equal(3, Quantity(world, Household, "fiber"));
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Released,
            world.Society.Inventory.GetReservation(id).State));
        using var restored = Reload(world);
        Assert.Equal(WorldProductionJobState.Cancelled, Job(restored, job.JobId).State);
    }

    [Fact]
    public async Task RepeatingProductionRetainsAggregateProgressAndCancellationStartsTheQueuedRecipe()
    {
        var state = Stock(Prepared(House), House, "fiber", 9);
        var actor = Actor(state);
        using var initial = Restore(state);
        var repeating = Submit(initial, actor, "repeat-rope", "keep making rope until cancelled");
        var queued = initial.SubmitInstruction(new("queued-basket", "owner:test", actor,
            OwnerInstructionKind.MustDo, "make a basket", Queue: true));
        for (var tick = 0; tick < 80 && Order(initial, repeating).CompletedUnits < 2; tick++) await Tick(initial);
        Assert.Equal(2, Order(initial, repeating).CompletedUnits);
        Assert.True(Order(initial, repeating).RepeatUntilCancelled);
        Assert.NotEqual("finished", Order(initial, repeating).Status);
        Assert.Equal(("queued", 0), (Order(initial, queued).Status, Order(initial, queued).CompletedUnits));
        Assert.Equal(2, Produced(initial, "rope"));
        Assert.Equal(0, Produced(initial, "basket"));
        using var world = Reload(initial);
        Assert.True(world.CancelOrder(new("end-repeat", "owner:test", world.Society.WorldId,
            actor, repeating.InstructionId)).Changed);
        await Finish(world, queued);
        Assert.Equal(("cancelled", 2), (Order(world, repeating).Status, Order(world, repeating).CompletedUnits));
        Assert.Equal(("finished", 1), (Order(world, queued).Status, Order(world, queued).CompletedUnits));
        Assert.Equal(1, Produced(world, "basket"));
        Assert.Equal(1, Produced(world, "rope"));
        Assert.Equal(0, Quantity(world, Household, "fiber"));
        Assert.Equal(2, world.WorldSimulation.ProductionJobs.Count(job =>
            job.OrderInstructionId == repeating.InstructionId && job.State == WorldProductionJobState.Completed));
        Assert.Single(world.WorldSimulation.ProductionJobs, job =>
            job.OrderInstructionId == queued.InstructionId && job.State == WorldProductionJobState.Completed);
        using var restored = Reload(world);
        await TickTogether(world, restored);
    }

    [Fact]
    public async Task HeldModelReplyCannotReviveACancelledProductionJob()
    {
        var state = Stock(Prepared(House), House, "fiber", 3);
        var actor = Actor(state);
        var provider = new ProductionChoices(DecisionProviderKind.LargeLanguageModel, hold: true);
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == actor ? provider : new ProductionChoices());
        var receipt = Submit(world, actor, "held-production", "make rope");
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var tick = 0; tick < 20 && world.WorldSimulation.ProductionJobs.Count == 0; tick++)
                Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            var job = Assert.Single(world.WorldSimulation.ProductionJobs);
            Assert.True(world.CancelOrder(new("stop-held-production", "owner:test", world.Society.WorldId,
                actor, receipt.InstructionId)).Changed);
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            while (world.WorldTick <= job.CompletionTick + 2)
                Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(("cancelled", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
            Assert.Equal(WorldProductionJobState.Cancelled, Job(world, job.JobId).State);
            Assert.Equal(0, Produced(world, "rope"));
            Assert.Equal(3, Quantity(world, Household, "fiber"));
            Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Released,
                world.Society.Inventory.GetReservation(id).State));
            world.Validate();
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Fact]
    public async Task SaveValidationRejectsUnearnedProgressAndAnUnrelatedJobBinding()
    {
        var state = Stock(Prepared(House), House, "fiber", 3);
        using var world = Restore(state);
        var receipt = Submit(world, Actor(state), "saved-production", "make two ropes");
        var job = await StartJob(world);
        var saved = world.ExportState();
        var order = Order(world, receipt);
        foreach (var invalid in new[]
        {
            order with
            {
                CompletedUnits = 1,
                LastEffectId = "produce:job:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(job.JobId))),
            },
            order with { ProductionJobId = "production-9999999999" },
            order with { ProductionBuildingId = Shop },
            order with { TargetOutputKind = "sack" },
        })
        {
            var corrupt = saved with
            {
                Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                    ? item with { Order = invalid } : item).ToArray(),
            };
            Assert.Throws<InvalidDataException>(() => Restore(corrupt));
        }
        var missingReceipt = saved with
        {
            WorldSimulation = saved.WorldSimulation! with
            {
                ProductionJobs = saved.WorldSimulation.ProductionJobs.Select(item => item.JobId == job.JobId
                    ? item with { OrderInstructionId = null } : item).ToArray(),
            },
        };
        Assert.Throws<InvalidDataException>(() => Restore(missingReceipt));
        var project = Person(world, Actor(saved)).Project!;
        foreach (var invalid in new[] { project with { WorkDone = 0 }, project with { Stage = "cancelled" } })
        {
            var corrupt = saved with
            {
                Inhabitants = saved.Inhabitants.Select(person => person.InhabitantId == Actor(saved)
                    ? person with { Project = invalid } : person).ToArray(),
            };
            Assert.Throws<InvalidDataException>(() => Restore(corrupt));
        }
        using var restored = Reload(world);
        await TickTogether(world, restored);
    }

    [Fact]
    public async Task UnsupportedProductionClosesWithoutSendingTheUnknownOrderToTheModel()
    {
        var state = Prepared(House);
        var actor = Actor(state);
        var provider = new ProductionChoices(DecisionProviderKind.LargeLanguageModel);
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == actor ? provider : new ProductionChoices());
        var receipt = Submit(world, actor, "unknown-production", "make a sword");
        Assert.Equal("not_understood", Order(world, receipt).Status);
        Assert.Contains(receipt.InstructionId, world.ExportState().CompletedInstructionIds!);
        Assert.Empty(provider.Requests);
        for (var tick = 0; tick < 4; tick++) await Tick(world);
        Assert.DoesNotContain(provider.Requests, request => request.OperativeOrderInstructionId == receipt.InstructionId ||
            request.ObserverGuidance?.Any(message => message.InstructionId == receipt.InstructionId) == true);
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        Assert.Single(world.ExportState().Events, item => item.Kind == "instruction_not_understood" &&
            item.Detail.EndsWith(":" + receipt.InstructionId, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cook camp meal")]
    [InlineData("cook household meal")]
    [InlineData("cook hearty meal")]
    public async Task RetiredCookingDoesNotReplaceOrStallExecutableProduction(string text)
    {
        var state = Stock(Prepared(House), House, "fiber", 3);
        var actor = Actor(state);
        var provider = new ProductionChoices();
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == actor ? provider : new ProductionChoices());
        var current = Submit(world, actor, "real-rope", "make rope");
        var retained = Order(world, current);
        var rejected = Submit(world, actor, "retired-cooking", text);
        Assert.Equal(("unknown", "not_understood"), (Order(world, rejected).Action, Order(world, rejected).Status));
        Assert.Equal(retained, Order(world, current));
        await Finish(world, current);
        Assert.DoesNotContain(provider.Requests, request => request.OperativeOrderInstructionId == rejected.InstructionId ||
            request.ObserverGuidance?.Any(message => message.InstructionId == rejected.InstructionId) == true);
        var job = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal((WorldProductionJobState.Completed, current.InstructionId), (job.State, job.OrderInstructionId));
        Assert.Equal(1, Produced(world, "rope"));
        Assert.Equal(0, Quantity(world, Household, "fiber"));
        using var restored = Reload(world);
        world.Validate();
    }

    private static PrivateWorldRuntimeState Prepared(string buildingId)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Baseline.Value);
        var actor = Actor(state);
        var position = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == buildingId).Position;
        // Exact materials are supplied by each test; starter household stock and tools must not mask a missing input.
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot =>
                lot.OwnerId != actor && !(lot.OwnerId == Household && lot.ItemKind is "fiber" or "cloth" or "rope" or "sack" or "bandage")).ToArray(),
        });
        return state with
        {
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? position : person.Position,
                Equipment = person.InhabitantId == actor ? null : person.Equipment,
                Survival = new(),
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
            }).ToArray(),
        };
    }

    private static PrivateWorldRuntimeState Stock(PrivateWorldRuntimeState state, string building, string kind, int count, string owner = Household) =>
        WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "order-input-" + kind,
            kind, owner, count, storageBuildingId: building));
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static string Actor(PrivateWorldRuntimeState state) => state.Society.Society.Inhabitants.First(person => person.HouseholdId == Household).Id;
    private static PlaytestInhabitantState Person(PrivateWorldRuntime world, string actor) => world.Inhabitants.Single(person => person.InhabitantId == actor);
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new ProductionChoices());
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var restored = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        return restored;
    }
    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string actor, string key, string text) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text));
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    private static WorldProductionJob Job(PrivateWorldRuntime world, string id) => world.WorldSimulation.ProductionJobs.Single(job => job.JobId == id);
    private static int Quantity(PrivateWorldRuntime world, string owner, string kind) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == owner && lot.ItemKind == kind).Sum(lot => lot.Quantity);
    private static int Produced(PrivateWorldRuntime world, string kind) =>
        world.Society.Inventory.Lots.Where(lot => lot.ItemKind == kind && lot.Id.StartsWith("production-", StringComparison.Ordinal)).Sum(lot => lot.Quantity);
    private static async Task Tick(PrivateWorldRuntime world) => Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    private static async Task TickTogether(PrivateWorldRuntime world, PrivateWorldRuntime restored)
    {
        await Tick(world);
        await Tick(restored);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
    private static async Task<WorldProductionJob> StartJob(PrivateWorldRuntime world)
    {
        for (var tick = 0; tick < 20 && world.WorldSimulation.ProductionJobs.Count == 0; tick++) await Tick(world);
        var job = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(WorldProductionJobState.Running, job.State);
        return job;
    }
    private static async Task Finish(PrivateWorldRuntime world, OwnerInstructionReceipt receipt)
    {
        for (var tick = 0; tick < 90 && Order(world, receipt).Status != "finished"; tick++) await Tick(world);
        Assert.Equal("finished", Order(world, receipt).Status);
    }

    private sealed class ProductionChoices(DecisionProviderKind kind = DecisionProviderKind.Deterministic, bool hold = false) : IDecisionProvider
    {
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 0;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var observation = request.Observation;
            Requests.Enqueue(observation);
            if (hold && !Started.Task.IsCompleted && observation.OperativeOrderInstructionId is not null)
            {
                Started.TrySetResult(true);
                await Release.Task;
                Returned.TrySetResult(true);
            }
            var selected = observation.OperativeOrderInstructionId is not null && observation.Candidates.Any(candidate => candidate.Id == "produce_item")
                ? "produce_item" : "safe_idle";
            return new(request.RequestId, observation.InhabitantId, Kind, request.ProviderEpoch, observation.RunEpoch,
                observation.DecisionGeneration, observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 });
        }
    }
}
