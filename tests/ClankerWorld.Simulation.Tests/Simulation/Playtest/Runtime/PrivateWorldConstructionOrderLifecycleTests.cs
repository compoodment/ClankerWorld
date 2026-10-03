using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConstructionOrderTests
{
    [Fact]
    public async Task CancellingPartlyBuiltWorkPreservesMaterialsAndOnlyTheQueuedProjectsPlacementEarnsCredit()
    {
        var setup = await Baseline.Value;
        using var world = Restore(Decode(setup));
        var first = Submit(world, setup.Actor, "cancelled-clinic", BuildClinic(setup.Site));
        var queued = Submit(world, setup.Actor, "queued-clinic", BuildClinic(setup.Site), queue: true);
        await ReachWork(world, setup.Actor);
        var firstOrder = Order(world, first);
        Assert.Equal(("queued", 0), (Order(world, queued).Status, Order(world, queued).CompletedUnits));
        Assert.True(world.CancelOrder(new("stop-clinic", "owner:test", world.Society.WorldId, setup.Actor, first.InstructionId)).Changed);
        Assert.Equal("cancelled", Person(world, setup.Actor).Project!.Stage);
        AssertInputs(world, 10, 4);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        using var replay = Reload(world);
        await FinishTogether(world, replay, queued);
        Assert.Equal(("cancelled", 0), (Order(world, first).Status, Order(world, first).CompletedUnits));
        Assert.Null(Order(world, first).LastEffectId);
        Assert.Equal(("finished", 1), (Order(world, queued).Status, Order(world, queued).CompletedUnits));
        Assert.NotEqual(firstOrder.ConstructionInstanceId, Order(world, queued).ConstructionInstanceId);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.InstanceId == firstOrder.ConstructionInstanceId);
        Assert.Equal(queued.InstructionId, Assert.Single(world.WorldSimulation.ConstructionReceipts!).InstructionId);
        AssertInputs(world, 0, 0);
    }

    [Fact]
    public async Task UrgentFoodPausesRealConstructionWorkAndResumesItsExactProjectAfterReload()
    {
        var setup = await Baseline.Value;
        using var initial = Restore(Decode(setup));
        var receipt = Submit(initial, setup.Actor, "hungry-builder", BuildClinic(setup.Site));
        await ReachWork(initial, setup.Actor);
        var project = Person(initial, setup.Actor).Project!;
        var state = initial.ExportState();
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "builders-meal", "berries", setup.Actor, 1)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == setup.Actor
                ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
        };
        using var world = Restore(state);
        await Tick(world);
        Assert.Equal(("interrupted", 0), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal((project.StartedTick, project.WorkDone, receipt.InstructionId),
            (Person(world, setup.Actor).Project!.StartedTick, Person(world, setup.Actor).Project!.WorkDone,
                Person(world, setup.Actor).Project!.OrderInstructionId));
        AssertInputs(world, 10, 4);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        using var replay = Reload(world);
        await FinishTogether(world, replay, receipt);
        Assert.Equal(("finished", 1), (Order(world, receipt).Status, Order(world, receipt).CompletedUnits));
        Assert.Equal(project.StartedTick, Assert.Single(world.WorldSimulation.ConstructionReceipts!).StartedTick);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "builders-meal");
        AssertInputs(world, 0, 0);
    }

    [Fact]
    public async Task PublicDepartureImmediatelyLeavesAValidUnpaidBlockedConstructionOrder()
    {
        var setup = await Baseline.Value;
        using var world = Restore(Decode(setup));
        var receipt = Submit(world, setup.Actor, "departing-builder", BuildClinic(setup.Site));
        await ReachWork(world, setup.Actor);
        var instance = Order(world, receipt).ConstructionInstanceId;
        Assert.True(world.DisplaceAdult(setup.Actor));
        using var replay = Reload(world);
        Assert.Equal(("blocked", 0, Alpha),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ConstructionOwnerId));
        Assert.NotEqual(Alpha, world.Society.GetInhabitant(setup.Actor).HouseholdId);
        for (var tick = 0; tick < 3; tick++) await TickTogether(world, replay);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.InstanceId == instance);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        AssertInputs(world, 10, 4);
    }

    [Fact]
    public async Task ABuildersNaturalDeathCannotLeaveAnOwnedProjectRunningOrEarnAPlacement()
    {
        var setup = await Baseline.Value;
        using var initial = Restore(Decode(setup));
        var receipt = Submit(initial, setup.Actor, "dying-builder", BuildClinic(setup.Site));
        await ReachWork(initial, setup.Actor);
        var state = initial.ExportState();
        var society = state.Society.Society;
        var nextTick = society.WorldTick + 1;
        var maximumDay = society.Config.DayLifecycle!.MaximumDay;
        state = state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == setup.Actor ? person with
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
        Assert.Equal(SocietyInhabitantStatus.Active, world.Society.GetInhabitant(setup.Actor).Status);
        await TickTogether(world, replay);
        Assert.Equal((SocietyInhabitantStatus.Dead, SocietyDeathCause.NaturalAge, nextTick),
            (world.Society.GetInhabitant(setup.Actor).Status, world.Society.GetInhabitant(setup.Actor).DeathCause,
                world.Society.GetInhabitant(setup.Actor).DeathTick));
        Assert.DoesNotContain(world.Inhabitants, person => person.InhabitantId == setup.Actor);
        Assert.Equal(0, Order(world, receipt).CompletedUnits);
        Assert.Null(Order(world, receipt).LastEffectId);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.DefinitionId == setup.DefinitionId);
        AssertInputs(world, 10, 4);
        using var afterDeath = Reload(world);
    }

    [Fact]
    public async Task AnotherPaidPlacementAtTheBoundSiteIsNotTheOrdersCompletion()
    {
        var setup = await Baseline.Value;
        using var world = Restore(Decode(setup));
        var receipt = Submit(world, setup.Actor, "independent-placement", BuildClinic(setup.Site));
        Assert.Null(Order(world, receipt).ConstructionInstanceId);
        var unbound = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        string futureInstance;
        using (var started = Reload(world))
        {
            await Tick(started);
            futureInstance = Assert.IsType<string>(Order(started, receipt).ConstructionInstanceId);
        }
        Assert.Null(Order(world, receipt).ConstructionInstanceId);
        Assert.Equal(unbound, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var premature = world.PlaceBuilding(futureInstance, setup.DefinitionId, setup.Site, Alpha);
        Assert.False(premature.Applied);
        Assert.NotEmpty(premature.Failure!);
        Assert.Equal(unbound, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        await ReachWork(world, setup.Actor);
        var orderedInstance = Order(world, receipt).ConstructionInstanceId;
        Assert.Equal(futureInstance, orderedInstance);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var stolenIdentity = world.PlaceBuilding(orderedInstance!, setup.DefinitionId, setup.Site, Alpha);
        Assert.False(stolenIdentity.Applied);
        Assert.NotEmpty(stolenIdentity.Failure!);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using (var unchanged = Reload(world))
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(unchanged.ExportState()));
        AssertInputs(world, 10, 4);
        var manual = world.PlaceBuilding("independent-clinic", setup.DefinitionId, setup.Site, Alpha);
        Assert.True(manual.Applied, manual.Failure);
        AssertInputs(world, 0, 0);
        using var replay = Reload(world);
        await TickTogether(world, replay);
        Assert.Equal(("blocked", 0, orderedInstance),
            (Order(world, receipt).Status, Order(world, receipt).CompletedUnits, Order(world, receipt).ConstructionInstanceId));
        Assert.Null(Order(world, receipt).LastEffectId);
        Assert.Empty(world.WorldSimulation.ConstructionReceipts ?? []);
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.InstanceId == orderedInstance);
        Assert.Single(world.WorldSimulation.Buildings, item => item.InstanceId == "independent-clinic");
    }

    [Fact]
    public async Task StrictReloadRejectsForgedCompletionWithoutPaidProofAndAMismatchedProjectBinding()
    {
        var setup = await Baseline.Value;
        using var world = Restore(Decode(setup));
        var receipt = Submit(world, setup.Actor, "unfinished-proof", BuildClinic(setup.Site));
        await ReachWork(world, setup.Actor);
        var state = world.ExportState();
        var order = Order(world, receipt);
        var forged = order with
        {
            Status = "finished",
            CompletedUnits = 1,
            LastEffectId = "construction:building:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(order.ConstructionInstanceId!))),
        };
        Assert.Throws<InvalidDataException>(() => Restore(state with
        {
            Instructions = state.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                ? item with { Order = forged } : item).ToArray(),
            CompletedInstructionIds = state.CompletedInstructionIds!.Append(receipt.InstructionId).ToArray(),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == setup.Actor
                ? person with { Project = person.Project! with { Stage = "completed", WorkDone = 10 } } : person).ToArray(),
        }));
        foreach (var bad in new[]
        {
            order with { ConstructionStartedTick = order.ConstructionStartedTick + 1 },
            order with { ConstructionInstanceId = "a-different-building" },
            order with { ConstructionOwnerId = null },
        })
            Assert.Throws<InvalidDataException>(() => Restore(state with
            {
                Instructions = state.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                    ? item with { Order = bad } : item).ToArray(),
            }));
        using var replay = Reload(world);
        Assert.Equal(0, Order(replay, receipt).CompletedUnits);
        AssertInputs(replay, 10, 4);
    }

    private static async Task ReachWork(PrivateWorldRuntime world, string actor)
    {
        for (var tick = 0; tick < 45 && (Person(world, actor).Project?.WorkDone ?? 0) < 3; tick++) await Tick(world);
        Assert.InRange(Person(world, actor).Project!.WorkDone, 3, 9);
        Assert.Equal("working", Person(world, actor).Project!.Stage);
    }
}
