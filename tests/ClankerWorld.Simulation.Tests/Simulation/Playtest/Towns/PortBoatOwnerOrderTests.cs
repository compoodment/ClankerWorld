using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PortBoatRuntimeTests
{
    private static readonly Lazy<Task<byte[]>> PaidPermissionBoat = new(() => BuildPaidBoatAsync(ticksPerDay: 96));
    [Fact]
    public async Task OwnerBoatOrderUsesPaidBoatAndFinishesOnlyAtItsNamedPortAcrossReload()
    {
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), new());
        var boat = Assert.Single(scenario.World.Boats);
        var destination = scenario.World.WorldSimulation.Buildings.Single(port =>
            port.InstanceId != boat.DockedPortId && PortNavigationRules.IsPort(
                scenario.World.WorldContent.Buildings.Single(definition => definition.CanonicalId == port.DefinitionId)));
        var receipt = SubmitBoatOrder(scenario.World, "owner-voyage", destination.InstanceId);
        Assert.Equal("travel_by_boat", BoatOrder(scenario.World, receipt).Action);
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is not null, 120);
        Assert.Equal(0, BoatOrder(scenario.World, receipt).CompletedUnits);
        Assert.Equal(destination.InstanceId, scenario.World.Boats[0].Journey!.DestinationPortId);
        var cargo = scenario.World.Society.Inventory.GetLot("travel-jug");
        var bytes = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        Assert.False((await scenario.World.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
        using var replay = new BoatScenario(PrivateWorldRuntimeCodec.Decode(bytes), new());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
        for (var step = 0; BoatOrder(scenario.World, receipt).Status != "finished" && step < 160; step++)
        {
            Assert.True((await scenario.World.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.World.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
            if (scenario.World.Boats[0].Journey is not null)
                Assert.Equal(0, BoatOrder(scenario.World, receipt).CompletedUnits);
        }
        Assert.Equal("finished", BoatOrder(scenario.World, receipt).Status);
        Assert.Equal(1, BoatOrder(scenario.World, receipt).CompletedUnits);
        Assert.Equal(destination.InstanceId, Assert.Single(scenario.World.Boats).DockedPortId);
        Assert.Equal("arrived", Assert.Single(scenario.World.BoatRequests).Status);
        AssertBoatCargoUnchanged(scenario.World, cargo.Id, cargo.OwnerId, cargo.Quantity);
        Assert.Single(scenario.World.ExportState().Events, item => item.Kind == "instruction_order_finished");
        scenario.World.Validate();
        replay.World.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingOrReplacingAWaitingBoatOrderReleasesItsRequest(bool replace)
    {
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), new());
        var boat = Assert.Single(scenario.World.Boats);
        var destination = scenario.World.Towns[0].Projects.Single(project =>
            project.CompletedBuildingId is not null && project.CompletedBuildingId != boat.DockedPortId).CompletedBuildingId!;
        AddLandingBlockers(scenario, destination);
        var receipt = SubmitBoatOrder(scenario.World, "wait-before-cancel", destination);
        Assert.Equal("travel_by_boat", BoatOrder(scenario.World, receipt).Action);
        await scenario.UntilAsync(() => scenario.World.BoatRequests.Any(request => request.Status == "waiting"), 120);
        Assert.Null(Assert.Single(scenario.World.BoatRequests).BoatId);
        if (replace)
        {
            var person = scenario.World.Inhabitants.Single(person => person.InhabitantId == BoatPolicy.Author);
            scenario.World.SubmitInstruction(new("replace-voyage", "owner:test", BoatPolicy.Author,
                OwnerInstructionKind.MustDo, FormattableString.Invariant($"move to ({person.Position.X}, {person.Position.Y})")));
        }
        else
            scenario.World.CancelOrder(new("cancel-voyage", "owner:test", scenario.World.Society.WorldId,
                BoatPolicy.Author, receipt.InstructionId));
        Assert.Equal("cancelled", BoatOrder(scenario.World, receipt).Status);
        Assert.Equal("cancelled", Assert.Single(scenario.World.BoatRequests).Status);
        Assert.Equal(0, BoatOrder(scenario.World, receipt).CompletedUnits);
        Assert.Equal(boat, Assert.Single(scenario.World.Boats));
        using var restored = new BoatScenario(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState())), new());
        Assert.True((await restored.World.AdvanceOneTickAsync()).Advanced);
        Assert.Null(Assert.Single(restored.World.Boats).Journey);
        restored.World.Validate();
    }

    [Fact]
    public async Task QueuedBoatOrdersUseTwoRealRequestsAndOnePhysicalBoat()
    {
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), new());
        var boat = Assert.Single(scenario.World.Boats);
        var destination = BoatOrderDestination(scenario.World);
        var outward = SubmitBoatOrder(scenario.World, "outward", destination);
        var homeward = SubmitBoatOrder(scenario.World, "homeward", boat.DockedPortId!, queue: true);
        Assert.Equal("queued", BoatOrder(scenario.World, homeward).Status);
        await scenario.UntilAsync(() => BoatOrder(scenario.World, outward).Status == "finished", 180);
        Assert.Equal(destination, Assert.Single(scenario.World.Boats).DockedPortId);
        var saved = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        using var restored = new BoatScenario(PrivateWorldRuntimeCodec.Decode(saved), new());
        await restored.UntilAsync(() => BoatOrder(restored.World, homeward).Status == "finished", 180);
        Assert.Equal(boat.DockedPortId, Assert.Single(restored.World.Boats).DockedPortId);
        Assert.Equal(2, restored.World.BoatRequests.Count);
        Assert.All(restored.World.BoatRequests, request => Assert.Equal("arrived", request.Status));
        Assert.Equal(new[] { outward.InstructionId, homeward.InstructionId },
            restored.World.BoatRequests.Select(request => request.OrderInstructionId));
        AssertBoatCargoUnchanged(restored.World, "travel-jug", BoatPolicy.Author, 1);
        restored.World.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingOrReplacingAnUnderwayBoatOrderPreservesItsActualJourney(bool replace)
    {
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), new());
        var receipt = SubmitBoatOrder(scenario.World, "cancel-underway", BoatOrderDestination(scenario.World));
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is not null, 120);
        var boat = Assert.Single(scenario.World.Boats);
        if (replace)
        {
            var origin = scenario.World.WorldSimulation.Buildings.Single(port => port.InstanceId == boat.Journey!.OriginPortId);
            scenario.World.SubmitInstruction(new("replacement-on-shore", "owner:test", BoatPolicy.Author,
                OwnerInstructionKind.MustDo, FormattableString.Invariant($"move to ({origin.Entrance!.Value.X}, {origin.Entrance.Value.Y})")));
        }
        else
            scenario.World.CancelOrder(new("cancel-aboard", "owner:test", scenario.World.Society.WorldId,
                BoatPolicy.Author, receipt.InstructionId));
        Assert.Equal(boat, Assert.Single(scenario.World.Boats));
        Assert.Equal("underway", Assert.Single(scenario.World.BoatRequests).Status);
        var saved = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        using var restored = new BoatScenario(PrivateWorldRuntimeCodec.Decode(saved), new());
        await restored.UntilAsync(() => restored.World.Boats[0].Journey is null, 160);
        Assert.Equal(boat.Journey!.DestinationPortId, Assert.Single(restored.World.Boats).DockedPortId);
        Assert.Equal("cancelled", BoatOrder(restored.World, receipt).Status);
        Assert.Equal(0, BoatOrder(restored.World, receipt).CompletedUnits);
        Assert.DoesNotContain(restored.World.ExportState().Events, item => item.Kind == "instruction_order_finished" &&
            item.Detail.Contains(receipt.InstructionId, StringComparison.Ordinal));
        AssertBoatCargoUnchanged(restored.World, "travel-jug", BoatPolicy.Author, 1);
        restored.World.Validate();
    }

    [Fact]
    public async Task AnOrderedBoatReturnsSafelyWithoutCreditingItsBlockedDestinationAcrossReplay()
    {
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), new());
        var receipt = SubmitBoatOrder(scenario.World, "blocked-return", BoatOrderDestination(scenario.World));
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is not null, 120);
        var journey = scenario.World.Boats[0].Journey!;
        AddLandingBlockers(scenario, journey.DestinationPortId);
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey?.WaitingSinceTick is not null, 100);
        Assert.Equal("blocked", BoatOrder(scenario.World, receipt).Status);
        var saved = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        var replayPolicy = new BoatPolicy();
        replayPolicy.IdleActors.UnionWith(Blockers);
        using var replay = new BoatScenario(PrivateWorldRuntimeCodec.Decode(saved), replayPolicy);
        var sawReturn = false;
        for (var step = 0; scenario.World.Boats[0].Journey is not null && step < 160; step++)
        {
            Assert.True((await scenario.World.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.World.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
            Assert.Equal(0, BoatOrder(scenario.World, receipt).CompletedUnits);
            sawReturn |= scenario.World.Boats[0].Journey?.Returning == true;
        }
        Assert.True(sawReturn);
        Assert.Null(scenario.World.Boats[0].Journey);
        Assert.Equal(journey.OriginPortId, scenario.World.Boats[0].DockedPortId);
        Assert.Equal("returned", Assert.Single(scenario.World.BoatRequests).Status);
        Assert.NotEqual("finished", BoatOrder(scenario.World, receipt).Status);
        AssertBoatCargoUnchanged(scenario.World, "travel-jug", BoatPolicy.Author, 1);
        scenario.World.Validate();
        replay.World.Validate();
    }

    [Fact]
    public async Task SavedBoatOrdersRefuseInventedArrivalsAndMismatchedPassengersDestinationsOrRequests()
    {
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), new());
        var receipt = SubmitBoatOrder(scenario.World, "validate-voyage", BoatOrderDestination(scenario.World));
        var initial = scenario.World.ExportState();
        var instruction = initial.Instructions!.Single(item => item.InstructionId == receipt.InstructionId);
        var order = instruction.Order!;
        foreach (var damaged in new[]
        {
            order with { BoatTravel = null },
            order with { BoatTravel = new("unknown-port") },
            order with { BoatTravel = order.BoatTravel! with { RequestId = "boat-request:999" } },
            order with { CompletedUnits = 1, Status = "finished" },
            order with { TargetPosition = null },
            order with { TargetResourceId = "invented-food" },
            order with { RepeatUntilCancelled = true },
        })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(initial with
            { Instructions = [instruction with { Order = damaged }] }));
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is not null, 120);
        var state = scenario.World.ExportState();
        var request = Assert.Single(state.BoatTransport.Requests);
        var underwayInstruction = state.Instructions!.Single(item => item.InstructionId == receipt.InstructionId);
        var inventedArrival = underwayInstruction.Order! with
        {
            Status = "finished",
            CompletedUnits = 1,
            LastEffectId = "boat-arrival:" + request.Id
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        {
            Instructions = [underwayInstruction with { Order = inventedArrival }],
            CompletedInstructionIds = state.CompletedInstructionIds!.Append(receipt.InstructionId).ToArray(),
        }));
        foreach (var damaged in new[]
        {
            request with { OrderInstructionId = "missing-instruction" },
            request with { OrderInstructionId = null },
            request with { PassengerId = Follower },
            request with { DestinationPortId = request.OriginPortId },
        })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
            { BoatTransport = state.BoatTransport with { Requests = [damaged] } }));
        scenario.World.Validate();
    }

    [Fact]
    public async Task VisitorBoatOrderWaitsForActualCouncilPermissionAndLosingItCancelsTheUnusedRequest()
    {
        var policy = new BoatPolicy();
        // A 24-tick day ends before the proposer can read and vote through
        // their ordinary 30-tick personal-decision cadence. Keep this native
        // civic window long enough for all four required resident votes.
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidPermissionBoat.Value), policy);
        var origin = scenario.World.WorldSimulation.Buildings.Single(port => port.InstanceId == scenario.World.Boats[0].DockedPortId);
        var state = scenario.World.ExportState();
        var outside = state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) &&
                !scenario.World.Towns[0].BorderTiles.Contains(tile.Position) &&
                !state.TownLandTitles!.Any(title => title.Tiles.Contains(tile.Position)) &&
                !state.Map.Resources.Any(resource => resource.Position == tile.Position) &&
                !state.Map.CampObjects.Any(item => item.Position == tile.Position))
            .OrderBy(tile => state.Map.FootDistance(tile.Position, origin.Entrance!.Value))
            .First(tile => DeterministicRouteFinder.TryFind(state.Map, tile.Position, origin.Entrance!.Value, out _)).Position;
        scenario.World.Pause();
        Assert.Equal("household:" + Visitor, scenario.World.AddAgent(Visitor, outside));
        scenario.World.Resume();
        var destinationId = BoatOrderDestination(scenario.World);
        var destination = scenario.World.WorldSimulation.Buildings.Single(port => port.InstanceId == destinationId);
        var receipt = scenario.World.SubmitInstruction(new("visitor-boat-order", "owner:test", Visitor,
            OwnerInstructionKind.MustDo, FormattableString.Invariant($"Travel by boat to Port at ({destination.Position.X}, {destination.Position.Y})")));
        for (var tick = 0; tick < 4; tick++) Assert.True((await scenario.World.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", BoatOrder(scenario.World, receipt).Status);
        Assert.Contains("permission", BoatOrder(scenario.World, receipt).BlockedReason, StringComparison.Ordinal);
        Assert.Empty(scenario.World.BoatRequests);
        var noticePlace = scenario.World.Towns[0].OriginSite!.Value;
        var move = scenario.World.SubmitInstruction(new("proposer-at-notice-place", "owner:test", BoatPolicy.Author,
            OwnerInstructionKind.MustDo, FormattableString.Invariant($"move to ({noticePlace.X}, {noticePlace.Y})")));
        await scenario.UntilAsync(() => BoatOrder(scenario.World, move).Status == "finished", 40);
        policy.GrantAll = true;
        await scenario.UntilAsync(() => TownBoatAccessRules.Allows(scenario.World.Towns[0], Visitor, scenario.World.WorldTick), 120);
        policy.GrantAll = false;
        // Keep the initial Council intact until the real grant passes. These
        // blockers become Town residents and could join its next election.
        AddLandingBlockers(scenario, destinationId);
        await scenario.UntilAsync(() => scenario.World.BoatRequests.Any(request => request.PassengerId == Visitor), 120);
        var request = Assert.Single(scenario.World.BoatRequests);
        Assert.Equal("waiting", request.Status);
        Assert.Null(request.BoatId);
        Assert.Equal(receipt.InstructionId, request.OrderInstructionId);
        policy.RevokeBoatAccess = true;
        await scenario.UntilAsync(() => !TownBoatAccessRules.Allows(scenario.World.Towns[0], Visitor, scenario.World.WorldTick), 120);
        await scenario.UntilAsync(() => scenario.World.BoatRequests[0].Status == "cancelled", 4);
        Assert.Null(scenario.World.Boats[0].Journey);
        Assert.Equal(0, BoatOrder(scenario.World, receipt).CompletedUnits);
        Assert.DoesNotContain(Visitor, scenario.World.Towns[0].ResidentIds);
        Assert.Equal(TownBorderRules.FirstTownId, scenario.World.Boats[0].TownId);
        var saved = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        var replayPolicy = new BoatPolicy();
        replayPolicy.IdleActors.UnionWith(Blockers);
        using var replay = new BoatScenario(PrivateWorldRuntimeCodec.Decode(saved), replayPolicy);
        for (var tick = 0; tick < 4; tick++) Assert.True((await replay.World.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("cancelled", Assert.Single(replay.World.BoatRequests).Status);
        Assert.Null(replay.World.Boats[0].Journey);
        Assert.Equal(0, BoatOrder(replay.World, receipt).CompletedUnits);
        replay.World.Validate();
    }

    [Theory]
    [InlineData("arrived")]
    [InlineData("cancelled")]
    [InlineData("returned")]
    public async Task BoatOrderKeepsItsNativeOutcomeWhenOlderRequestsAreCompacted(string outcome)
    {
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), new());
        var destination = BoatOrderDestination(scenario.World);
        if (outcome == "cancelled") AddLandingBlockers(scenario, destination);
        var receipt = SubmitBoatOrder(scenario.World, "compact-ordered-voyage", destination);
        if (outcome == "cancelled")
        {
            await scenario.UntilAsync(() => scenario.World.BoatRequests.Any(request => request.Status == "waiting"), 120);
            scenario.World.CancelOrder(new("cancel-before-compaction", "owner:test", scenario.World.Society.WorldId,
                BoatPolicy.Author, receipt.InstructionId));
        }
        else
        {
            await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is not null, 120);
            if (outcome == "returned")
            {
                AddLandingBlockers(scenario, destination);
                await scenario.UntilAsync(() => scenario.World.Boats[0].Journey?.WaitingSinceTick is not null, 100);
            }
            await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is null, 160);
        }
        var native = scenario.World.ExportState();
        var bound = Assert.Single(native.BoatTransport.Requests);
        Assert.Equal(outcome, bound.Status);
        Assert.Equal(receipt.InstructionId, bound.OrderInstructionId);
        var originalOrder = BoatOrder(scenario.World, receipt);
        Assert.Equal(outcome == "arrived" ? 1 : 0, originalOrder.CompletedUnits);
        var cargo = scenario.World.Society.Inventory.GetLot("travel-jug");
        // These later closed records isolate the archive window. The retained
        // arrival/cancellation/return above was produced by the real journey.
        var later = Enumerable.Range(1, PrivateWorldHistory.RecentBoatRequestLimit + 2).Select(offset => bound with
        {
            Id = "boat-request:" + (bound.Sequence + offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Sequence = bound.Sequence + offset,
            RequestedTick = native.Society.Society.WorldTick,
            SettledTick = native.Society.Society.WorldTick,
            Status = "cancelled", BoatId = null, OrderInstructionId = null,
        }).ToArray();
        var state = native with { BoatTransport = native.BoatTransport with
        { Sequence = later[^1].Sequence, Requests = native.BoatTransport.Requests.Concat(later).ToArray() } };
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        var policy = new BoatPolicy();
        policy.IdleActors.UnionWith(Blockers);
        using var compacting = new BoatScenario(PrivateWorldRuntimeCodec.Decode(bytes), policy);
        var plan = PrivateWorldHistory.Prepare(compacting.World.ExportState());
        Assert.Equal(bound, Assert.Single(plan.State.BoatTransport.Requests, request => request.Id == bound.Id));
        Assert.Equal(later.Take(2), plan.Segment.ClosedBoatRequests!);
        Assert.Equal(later.TakeLast(40), plan.State.BoatTransport.Requests.Where(request => request.Id != bound.Id));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(compacting.World.ExportState()));
        var directory = Directory.CreateTempSubdirectory("clankerworld-ordered-boat-history-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), policy.CreateProvider);
            Assert.True(file.Save(compacting.World));
            Assert.Equal(later.Take(2), ReadArchivedBoatRequests(file));
            var saved = PrivateWorldRuntimeCodec.Encode(compacting.World.ExportState());
            using var loaded = file.LoadOrCreate(state.WorldSeed);
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
            Assert.Equal(originalOrder, BoatOrder(loaded, receipt));
            AssertBoatCargoUnchanged(loaded, cargo.Id, cargo.OwnerId, cargo.Quantity);
            Assert.False((await compacting.World.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(compacting.World.ExportState()));
            var replayPolicy = new BoatPolicy();
            replayPolicy.IdleActors.UnionWith(Blockers);
            using var replay = new BoatScenario(PrivateWorldRuntimeCodec.Decode(saved), replayPolicy);
            for (var tick = 0; tick < 2; tick++)
            {
                Assert.True((await compacting.World.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.World.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(compacting.World.ExportState()),
                    PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
                Assert.Equal(originalOrder.CompletedUnits, BoatOrder(compacting.World, receipt).CompletedUnits);
            }
            AssertBoatCargoUnchanged(compacting.World, cargo.Id, cargo.OwnerId, cargo.Quantity);
            compacting.World.Validate();
            replay.World.Validate();
        }
        finally { directory.Delete(recursive: true); }
    }

    private static string BoatOrderDestination(PrivateWorldRuntime world) =>
        world.Towns[0].Projects.Single(project => project.CompletedBuildingId is not null &&
            project.CompletedBuildingId != world.Boats[0].DockedPortId).CompletedBuildingId!;

    private static void AssertBoatCargoUnchanged(PrivateWorldRuntime world, string id, string owner, int quantity)
    {
        var cargo = world.Society.Inventory.GetLot(id);
        Assert.Equal((owner, quantity, "water_jug"), (cargo.OwnerId, cargo.Quantity, cargo.ItemKind));
        Assert.Null(cargo.GroundPosition);
        Assert.Null(cargo.StorageBuildingId);
        Assert.Null(cargo.DeliveryBuildingId);
        Assert.Null(cargo.ContainerLotId);
        Assert.True(PersonalEquipmentRules.IsCarried(cargo, owner));
    }

    private static OwnerInstructionReceipt SubmitBoatOrder(PrivateWorldRuntime world, string key, string destinationId, bool queue = false)
    {
        var destination = world.WorldSimulation.Buildings.Single(port => port.InstanceId == destinationId);
        return world.SubmitInstruction(new(key, "owner:test", BoatPolicy.Author, OwnerInstructionKind.MustDo,
            FormattableString.Invariant($"Travel by boat to Port at ({destination.Position.X}, {destination.Position.Y})"), queue));
    }

    private static OwnerInstructionOrder BoatOrder(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(instruction => instruction.InstructionId == receipt.InstructionId).Order!;
}
