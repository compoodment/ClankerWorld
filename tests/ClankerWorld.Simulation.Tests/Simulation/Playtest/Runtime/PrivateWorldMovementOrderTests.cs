using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Theory]
    [InlineData("Go to tile (12, 4)", 12, 4)]
    [InlineData("please move to -12/4!", -12, 4)]
    [InlineData("travel to (12,-4) please", 12, -4)]
    [InlineData("move to 12 4 now", 12, 4)]
    public void MovementOrdersParseOneExactDestination(string text, int x, int y)
    {
        using var world = new PrivateWorldRuntime("movement-grammar");
        var receipt = world.SubmitInstruction(new("move", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, text));
        Assert.Equal(receipt, world.SubmitInstruction(new("move", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, text)));
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal("move_to", order.Action);
        Assert.Equal(new GridPoint(x, y), order.TargetPosition);
        Assert.Equal(1, order.RequestedUnits);
        Assert.False(order.QuantityIsExplicit);
        Assert.False(order.RepeatUntilCancelled);
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions).Order!;
        Assert.Equal(x, projected.TargetX);
        Assert.Equal(y, projected.TargetY);
        world.Validate();
    }

    [Theory]
    [InlineData("move to 12,4 and eat berries")]
    [InlineData("move to 12,4 until cancelled")]
    [InlineData("repeat move to 12,4")]
    [InlineData("do not move to 12,4")]
    [InlineData("move to 12,")]
    [InlineData("move to (12,4")]
    [InlineData("move to 10000001,4")]
    [InlineData("move to the unknown place")]
    public void UnsupportedMovementOrdersLeaveExistingWorkUntouched(string text)
    {
        using var world = new PrivateWorldRuntime("movement-unsupported");
        var active = world.SubmitInstruction(new("food", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "gather berries"));
        var rejected = world.SubmitInstruction(new("move", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, text));
        Assert.Equal("waiting", CancellationOrder(world.ExportState(), active.InstructionId).Status);
        Assert.Equal("not_understood", CancellationOrder(world.ExportState(), rejected.InstructionId).Status);
        world.Validate();
    }

    [Fact]
    public async Task MovementOrderCompletesOnArrivalAfterCooldownAndKeepsItsQueueAcrossSaveAndReplay()
    {
        var initial = MovementOrderState();
        var origin = CancellationActorPosition(initial);
        var destination = MovementOrderDestination(initial);
        initial = initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                ? person with { TravelCooldownTicks = 2 } : person).ToArray(),
        };
        using var world = RestoreMovementWorld(initial);
        var knowledge = world.ExportState().Knowledge!;
        var first = SubmitMovementOrder(world, "move-there", destination);
        var second = SubmitMovementOrder(world, "move-back", origin, queue: true);
        Assert.Equal(knowledge, world.ExportState().Knowledge);
        Assert.Equal("queued", CancellationOrder(world.ExportState(), second.InstructionId).Status);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(origin, CancellationActorPosition(world.ExportState()));
        Assert.Equal(0, CancellationOrder(world.ExportState(), first.InstructionId).CompletedUnits);
        Assert.DoesNotContain(world.ExportState().Knowledge!.Facts, fact =>
            fact.OwnerId == HarvestInstructionActor && fact.Position == destination);
        world.Pause();
        var directory = Directory.CreateTempSubdirectory("clankerworld-movement-order-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), MovementIdleProvider);
            file.Save(world);
            using var restored = file.LoadOrCreate(initial.WorldSeed);
            Assert.Equal(File.ReadAllBytes(file.Path), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            Assert.False((await restored.AdvanceOneTickAsync()).Advanced);
            world.Resume();
            restored.Resume();
            var sawFirstArrival = false;
            for (var tick = 0; tick < 80 && CancellationOrder(restored.ExportState(), second.InstructionId).Status != "finished"; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
                var after = restored.ExportState();
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(after));
                if (!sawFirstArrival && CancellationOrder(after, first.InstructionId).Status == "finished")
                {
                    Assert.Equal(destination, CancellationActorPosition(after));
                    Assert.Contains(after.Knowledge!.Facts, fact => fact.OwnerId == HarvestInstructionActor &&
                        fact.Position == destination && fact.Acquisition == "firsthand");
                    // The walk itself adds nothing to the agent's small map memory; only the tile reached does.
                    Assert.True(initial.Map.FootDistance(origin, destination) > 1);
                    Assert.Equal(knowledge.Facts.Count(fact => fact.OwnerId == HarvestInstructionActor) + 1,
                        after.Knowledge.Facts.Count(fact => fact.OwnerId == HarvestInstructionActor));
                    sawFirstArrival = true;
                }
            }
            Assert.True(sawFirstArrival);
            Assert.Equal("finished", CancellationOrder(restored.ExportState(), second.InstructionId).Status);
            Assert.Equal(origin, CancellationActorPosition(restored.ExportState()));
            Assert.All(restored.ExportState().Instructions!, instruction => Assert.Equal(1, instruction.Order!.CompletedUnits));
            Assert.Equal(2, restored.ExportState().Events.Count(item => item.Kind == "instruction_order_finished"));
            restored.Validate();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("impassable")]
    [InlineData("outside")]
    public async Task MovementOrderNeverSubstitutesAnotherTileWhenItsDestinationIsBlocked(string scenario)
    {
        var initial = MovementOrderState();
        var destination = scenario switch
        {
            "occupied" => initial.Inhabitants.First(person => person.InhabitantId != HarvestInstructionActor).Position,
            "impassable" => initial.Map.Tiles.First(tile => tile.Terrain is TerrainKind.Ocean or TerrainKind.Peak).Position,
            _ => new GridPoint(1_000_000, 1_000_000),
        };
        using var world = RestoreMovementWorld(initial);
        var receipt = SubmitMovementOrder(world, "blocked-move", destination);
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(CancellationActorPosition(initial), CancellationActorPosition(world.ExportState()));
        var order = CancellationOrder(world.ExportState(), receipt.InstructionId);
        Assert.Equal("blocked", order.Status);
        Assert.NotEmpty(order.BlockedReason!);
        Assert.Equal(destination, order.TargetPosition);
        Assert.Equal(0, order.CompletedUnits);
        using var restored = RestoreMovementWorld(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(order, CancellationOrder(restored.ExportState(), receipt.InstructionId));
        restored.Validate();
    }

    [Fact]
    public async Task MovementOrderResumesAfterAnOccupiedDestinationBecomesFree()
    {
        var initial = MovementOrderState();
        var destination = MovementOrderDestination(initial);
        var blocker = initial.Inhabitants.First(person => person.InhabitantId != HarvestInstructionActor);
        initial = initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == blocker.InhabitantId
                ? person with { Position = destination } : person).ToArray(),
        };
        using var world = RestoreMovementWorld(initial);
        var receipt = SubmitMovementOrder(world, "wait-for-tile", destination);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        var waiting = world.ExportState();
        waiting = waiting with
        {
            Inhabitants = waiting.Inhabitants.Select(person => person.InhabitantId == blocker.InhabitantId
                ? person with { Position = blocker.Position } : person).ToArray(),
        };
        using var restored = RestoreMovementWorld(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(waiting)));
        for (var tick = 0; tick < 40 && CancellationOrder(restored.ExportState(), receipt.InstructionId).Status != "finished"; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", CancellationOrder(restored.ExportState(), receipt.InstructionId).Status);
        Assert.Equal(destination, CancellationActorPosition(restored.ExportState()));
        restored.Validate();
    }

    [Fact]
    public async Task MovementOrderResumesItsExactDestinationAfterUrgentEating()
    {
        var initial = MovementOrderState();
        var destination = MovementOrderDestination(initial);
        var inventory = InventoryFixture.AddLot(initial.Society.Society.Inventory,
            "movement-urgent-fruit", "fruit", HarvestInstructionActor, 2);
        initial = initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
            Society = initial.Society with { Society = initial.Society.Society with { Inventory = inventory } },
        };
        using var world = RestoreMovementWorld(initial);
        var receipt = SubmitMovementOrder(world, "interrupted-move", destination);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("interrupted", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        Assert.Equal(CancellationActorPosition(initial), CancellationActorPosition(world.ExportState()));
        Assert.Equal(0, CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "food_consumed" && item.Detail == HarvestInstructionActor);
        using var restored = RestoreMovementWorld(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 40 && CancellationOrder(restored.ExportState(), receipt.InstructionId).Status != "finished"; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", CancellationOrder(restored.ExportState(), receipt.InstructionId).Status);
        Assert.Equal(destination, CancellationActorPosition(restored.ExportState()));
        restored.Validate();
    }

    [Fact]
    public async Task MovementOrderUsesOnePersonalDecisionThroughoutTheTrip()
    {
        var provider = new CancellationPlanningProvider(orderCandidate: "move_to");
        using var world = CreateCancellationTravelWorld(provider);
        var destination = MovementOrderDestination(world.ExportState());
        var receipt = SubmitMovementOrder(world, "personal-move", destination);
        for (var tick = 0; tick < 40 && CancellationOrder(world.ExportState(), receipt.InstructionId).Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        Assert.Equal(destination, CancellationActorPosition(world.ExportState()));
        var request = Assert.Single(provider.Requests, item => item.OperativeOrderInstructionId == receipt.InstructionId);
        Assert.Contains(request.ObserverGuidance!, item => item.InstructionId == receipt.InstructionId &&
            item.UnderstoodTask == "travel to the exact tile named in this order");
        world.Validate();
    }

    [Fact]
    public async Task MovementOrderWaitsForAHouseInvitationAndRetainsItAcrossReload()
    {
        var initial = MovementOrderState();
        var actorHousehold = initial.Society.Society.GetInhabitant(HarvestInstructionActor).HouseholdId;
        var house = initial.WorldSimulation!.Buildings.First(building =>
            building.InstanceId.StartsWith("first-town-house-", StringComparison.Ordinal) && building.HouseholdId != actorHousehold);
        var inviter = initial.Society.Society.GetHousehold(house.HouseholdId!).MemberIds[0];
        var otherBuildings = initial.WorldSimulation.Buildings.Select(building => building.Position).ToHashSet();
        var origin = initial.Map.FootNeighbors(house.Position).First(point =>
            initial.Map.IsPassable(point) && !otherBuildings.Contains(point));
        var free = initial.Map.Tiles.Select(tile => tile.Position).Where(point =>
            initial.Map.IsPassable(point) && !otherBuildings.Contains(point) && point != origin &&
            initial.Map.FootDistance(point, house.Position) > 3).Take(initial.Inhabitants.Count).ToArray();
        initial = initial with
        {
            Inhabitants = initial.Inhabitants.Select((person, index) => person with
            {
                Position = person.InhabitantId == HarvestInstructionActor ? origin : free[index],
            }).ToArray(),
        };
        using var world = RestoreMovementWorld(initial);
        var receipt = SubmitMovementOrder(world, "visit-house", house.Position);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(origin, CancellationActorPosition(world.ExportState()));
        var order = CancellationOrder(world.ExportState(), receipt.InstructionId);
        Assert.Equal("blocked", order.Status);
        Assert.Contains("invitation", order.BlockedReason, StringComparison.Ordinal);
        Assert.True(world.SetHouseGuestInvitation(inviter, house.InstanceId, HarvestInstructionActor, invited: true).Applied);
        using var restored = RestoreMovementWorld(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 10 && CancellationOrder(restored.ExportState(), receipt.InstructionId).Status != "finished"; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", CancellationOrder(restored.ExportState(), receipt.InstructionId).Status);
        Assert.Equal(house.Position, CancellationActorPosition(restored.ExportState()));
        Assert.Equal(actorHousehold, restored.Society.GetInhabitant(HarvestInstructionActor).HouseholdId);
        restored.Validate();
    }

    [Fact]
    public async Task MovementOrdersAllowChildrenWhoCanAlreadyWalk()
    {
        const int age = 4;
        var initial = MovementOrderState();
        var checkpoint = initial.Society.Society;
        var birth = checkpoint.LifeTickAt(checkpoint.WorldTick) - age * checkpoint.Config.TicksPerLifecycleAge;
        initial = initial with
        {
            Society = initial.Society with
            {
                Society = checkpoint with
                {
                    Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == HarvestInstructionActor
                        ? person with
                        {
                            BirthTick = birth,
                            BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                            AgeBand = SocietyAgeBand.Child,
                            LastLifecycleYearChecked = age,
                            CurrentRole = SocietyWorkRole.Unassigned,
                        } : person).ToArray(),
                },
            },
            // The fixture changes an adult into a child, so the all-adult
            // Council must exclude them before this checkpoint can load.
            Towns = initial.Towns!.Select(town => town with
            {
                Governance = TownGovernanceState.Create(town.ResidentIds.Where(id =>
                    id != HarvestInstructionActor && checkpoint.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)),
            }).ToArray(),
        };
        using var world = RestoreMovementWorld(initial);
        var destination = MovementOrderDestination(initial);
        var receipt = SubmitMovementOrder(world, "child-move", destination);
        for (var tick = 0; tick < 40 && CancellationOrder(world.ExportState(), receipt.InstructionId).Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        Assert.Equal(destination, CancellationActorPosition(world.ExportState()));
        world.Validate();
    }

    [Fact]
    public async Task CancelledMovementOrderCannotBeResurrectedByItsHeldPersonalReply()
    {
        var provider = new CancellationPlanningProvider(holdFirstOrder: true, orderCandidate: "move_to");
        using var world = CreateCancellationTravelWorld(provider);
        var receipt = SubmitMovementOrder(world, "held-move", MovementOrderDestination(world.ExportState()));
        try
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var position = CancellationActorPosition(world.ExportState());
            CancelTravelOrder(world, receipt.InstructionId, "cancel-move");
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var tick = 0; tick < 8 || tick < 60 &&
                     !provider.Requests.Any(request => request.OperativeOrderInstructionId is null); tick++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
                Assert.Equal(position, CancellationActorPosition(world.ExportState()));
                if (tick >= 8) await Task.Delay(10);
            }
            Assert.Single(provider.Requests, request => request.OperativeOrderInstructionId is null);
            Assert.Equal("cancelled", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
            Assert.Equal(0, CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits);
            world.Validate();
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Fact]
    public void SavedMovementOrdersRejectMissingTargetsMixedTasksAndInventedProgress()
    {
        using var world = new PrivateWorldRuntime("movement-validation");
        world.SubmitInstruction(new("move", "owner:test", OrderedAgent, OwnerInstructionKind.MustDo, "move to 12,4"));
        var state = world.ExportState();
        var instruction = Assert.Single(state.Instructions!);
        var order = instruction.Order!;
        Assert.Equal("move_to", order.Action);
        foreach (var damaged in new[]
        {
            order with { TargetPosition = null },
            order with { TargetFoodKind = "berries" },
            order with { TargetMaterialKind = "wood" },
            order with { TargetResourceId = "berry-patch" },
            order with { TargetAgentId = OrderedAgent },
            order with { RequestedUnits = 2 },
            order with { RepeatUntilCancelled = true },
            order with { CompletedUnits = 1 },
            order with { LastEffectId = "invented" },
        })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
            {
                Instructions = [instruction with { Order = damaged }],
            }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        {
            SchemaVersion = PrivateWorldRuntime.StateSchemaVersion - 1,
        }));
    }

    private static PrivateWorldRuntimeState MovementOrderState()
    {
        using var world = CreateCancellationTravelWorld(new CancellationPlanningProvider());
        return world.ExportState();
    }

    private static IDecisionProvider MovementIdleProvider(string _) =>
        new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true);

    private static PrivateWorldRuntime RestoreMovementWorld(PrivateWorldRuntimeState state) =>
        PrivateWorldRuntime.Restore(state, MovementIdleProvider);

    private static OwnerInstructionReceipt SubmitMovementOrder(PrivateWorldRuntime world, string key, GridPoint point, bool queue = false) =>
        world.SubmitInstruction(new(key, "owner:test", HarvestInstructionActor, OwnerInstructionKind.MustDo,
            FormattableString.Invariant($"Move to tile ({point.X}, {point.Y})"), queue));

    private static GridPoint MovementOrderDestination(PrivateWorldRuntimeState state)
    {
        var origin = CancellationActorPosition(state);
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != HarvestInstructionActor)
            .Select(person => person.Position).ToHashSet();
        occupied.UnionWith(state.WorldSimulation!.Buildings.Select(building => building.Position));
        return state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => state.Map.IsPassable(point) && state.Map.FootDistance(origin, point) is >= 4 and <= 6 &&
                !occupied.Contains(point))
            .OrderBy(point => point.Y).ThenBy(point => point.X)
            .First(point => DeterministicRouteFinder.TryFind(state.Map, origin, point, out var route) &&
                route.All(step => !occupied.Contains(step)));
    }
}
