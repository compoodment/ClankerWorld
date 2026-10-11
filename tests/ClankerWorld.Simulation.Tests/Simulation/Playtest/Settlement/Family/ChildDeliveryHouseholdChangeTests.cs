using System.Collections;
using System.Reflection;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ChildDeliveryHouseholdChangeTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task CaregiverDeparturePreservesChildCargoAndNativeCheckpoints(bool leave, bool deliverFirst)
    {
        var (state, child, house, source) = await ChildHouseholdHelpingTests.Prepared("wood");
        var household = state.Society.Society.GetInhabitant(child).HouseholdId!;
        var caregiver = state.Society.Society.GetInhabitant(child).PrimaryCaregiverId!;
        state = AddStock(state, child, household, house, source.Position);
        var choices = new DeliveryChoices(child, caregiver);
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        using var host = new CheckpointHost(world, choices);
        await host.Until(() => world.Society.Inventory.GetLot("child-moving-wood").DeliveryBuildingId == house);
        Assert.Equal(child, world.Society.Inventory.GetLot("child-moving-wood").OwnerId);
        Assert.Contains((child, "child_carry:child-moving-wood"), choices.Selected);
        if (deliverFirst)
        {
            choices.Deliver = true;
            Refresh(world, child);
            await host.Until(() => world.ExportState().Events.Any(item => item.Kind == "child_delivered_household"));
        }
        choices.ChangeHousehold = leave;
        choices.Deliver = false;
        Refresh(world, child);
        Refresh(world, caregiver);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var replayChoices = choices.Copy();
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => replayChoices);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using var replayHost = new CheckpointHost(replay, replayChoices);
        using var continuing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => choices);
        using var continuingHost = new CheckpointHost(continuing, choices);
        await ContinuePair(continuingHost, replayHost, () => !leave || continuing.Society.GetInhabitant(child).HouseholdId is null);
        Assert.Equal(leave ? null : household, continuing.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(leave ? null : household, continuing.Society.GetInhabitant(caregiver).HouseholdId);
        if (leave) Assert.Contains((caregiver, "household_leave"), choices.Selected);
        AssertCargo(continuing, child, household, house, deliverFirst);
        Assert.Equal(leave ? 1 : 0, continuing.ExportState().Events.Count(item => item.Kind == "household_left"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task GuardianEscortPreservesFormerHouseholdCargoAndNativeCheckpoints(bool pickup, bool deliverFirst)
    {
        var scenario = GuardianPlacementTestFixture.Prepared(1, 0);
        var child = scenario.Children[0];
        var caregiver = scenario.Guardians[0];
        var state = scenario.State;
        var society = state.Society.Society;
        // Retain the real birth and recorded parental deaths, controlling only school age and pickup arrival.
        var age = Assert.IsType<SocietyDayLifecycle>(society.Config.DayLifecycle).ChildStartDay;
        var born = society.LifeTickAt(society.WorldTick) - age * society.Config.TicksPerLifecycleAge;
        society = society with
        {
            Inhabitants = society.Inhabitants.Select(person => person.Id == child ? person with
            {
                BirthTick = born,
                BirthLifeTick = society.LifeClock is null ? null : born,
                AgeBand = SocietyAgeBand.Child,
                LastLifecycleYearChecked = age,
            } : person).ToArray(),
        };
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = society },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == child
                ? person with { Position = scenario.SourceHouse.Position } : person).ToArray(),
        };
        state = AddStock(state, child, GuardianPlacementTestFixture.SourceHousehold,
            scenario.SourceHouse.InstanceId, scenario.SourceHouse.Position);
        var choices = new DeliveryChoices(child, caregiver, guardian: true) { Pickup = pickup };
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        using var host = new CheckpointHost(world, choices);
        if (pickup)
        {
            await host.Until(() => world.Society.Inventory.GetLot("child-moving-wood").DeliveryBuildingId == scenario.SourceHouse.InstanceId);
            Assert.Contains((child, "child_carry:child-moving-wood"), choices.Selected);
        }
        if (deliverFirst)
        {
            choices.Deliver = true;
            Refresh(world, child);
            await host.Until(() => world.ExportState().Events.Any(item => item.Kind == "child_delivered_household"));
        }
        choices.ChangeHousehold = true;
        choices.Deliver = false;
        Refresh(world, child);
        Refresh(world, caregiver);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var replayChoices = choices.Copy();
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => replayChoices);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using var replayHost = new CheckpointHost(replay, replayChoices, refuseArrival: true);
        using var continuing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => choices);
        using var continuingHost = new CheckpointHost(continuing, choices, refuseArrival: true);
        await ContinuePair(continuingHost, replayHost, () => GuardianPlacementTestFixture.Placed(continuing, scenario, child));
        Assert.True(continuingHost.RefusedArrival);
        Assert.True(replayHost.RefusedArrival);
        Assert.Equal(scenario.House.Position, continuing.Inhabitants.Single(person => person.InhabitantId == child).Position);
        Assert.Equal(GuardianPlacementTestFixture.DestinationHousehold, continuing.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(scenario.DestinationTownId, GuardianPlacementTestFixture.TownOf(continuing, child));
        Assert.Contains((caregiver, "guardian_accept:" + child), choices.Selected);
        Assert.Contains((caregiver, "guardian_relocate:" + child), choices.Selected);
        if (pickup) AssertCargo(continuing, child, GuardianPlacementTestFixture.SourceHousehold, scenario.SourceHouse.InstanceId, deliverFirst);
        else
        {
            var stock = continuing.Society.Inventory.GetLot("child-moving-wood");
            Assert.Equal((GuardianPlacementTestFixture.SourceHousehold, 2, new InventoryGroundPosition(scenario.SourceHouse.Position.X, scenario.SourceHouse.Position.Y)),
                (stock.OwnerId, stock.Quantity, stock.GroundPosition));
            Assert.DoesNotContain(continuing.ExportState().Events, item => item.Kind == "child_collected_household" || item.Kind == "child_delivered_household");
        }
        Assert.Single(continuing.ExportState().Events, item => item.Kind == "guardian_placement_completed");
    }

    private static PrivateWorldRuntimeState AddStock(PrivateWorldRuntimeState state, string child, string household, string house, GridPoint point)
    {
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "child-moving-wood", "wood", household, 2,
            groundPosition: new(point.X, point.Y));
        inventory = InventoryFixture.AddLot(inventory, "child-personal-stone", "stone", child, 1, storageBuildingId: house);
        inventory = InventoryFixture.AddLot(inventory, "stationary-household-wood", "wood", household, 3, storageBuildingId: house);
        return state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    }

    private static void AssertCargo(PrivateWorldRuntime world, string child, string household, string house, bool delivered)
    {
        var lot = world.Society.Inventory.GetLot("child-moving-wood");
        Assert.Equal((household, 2, delivered ? house : null, delivered ? null : child, (string?)null, (InventoryGroundPosition?)null),
            (lot.OwnerId, lot.Quantity, lot.StorageBuildingId, lot.CarrierId, lot.DeliveryBuildingId, lot.GroundPosition));
        Assert.Equal(delivered ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "child_delivered_household"));
        Assert.Equal((child, 1, house), (world.Society.Inventory.GetLot("child-personal-stone").OwnerId,
            world.Society.Inventory.GetLot("child-personal-stone").Quantity, world.Society.Inventory.GetLot("child-personal-stone").StorageBuildingId));
        Assert.Equal((household, 3, house), (world.Society.Inventory.GetLot("stationary-household-wood").OwnerId,
            world.Society.Inventory.GetLot("stationary-household-wood").Quantity, world.Society.Inventory.GetLot("stationary-household-wood").StorageBuildingId));
    }

    private static void Refresh(PrivateWorldRuntime world, string actor) => world.SubmitInstruction(new(
        "child-house-change:" + world.WorldTick + ":" + Array.IndexOf(world.Inhabitants.Select(person => person.InhabitantId).ToArray(), actor), "owner:test", actor, OwnerInstructionKind.Suggestive, "Consider the next household task."));

    private static async Task ContinuePair(CheckpointHost host, CheckpointHost replay, Func<bool> completed)
    {
        for (var tick = 0; tick < 80 && !completed(); tick++)
        {
            await host.Step();
            await replay.Step();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(host.World.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
        }
        Assert.True(completed());
        for (var tick = 0; tick < 3; tick++)
        {
            await host.Step();
            await replay.Step();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(host.World.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
        }
    }

    private sealed class CheckpointHost : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("child-house-change-");
        private readonly PrivateWorldStateFile file;
        private readonly PrivateWorldRuntimeService service;
        private readonly DeliveryChoices choices;
        private readonly bool refuseArrival;
        public bool RefusedArrival { get; private set; }
        public PrivateWorldRuntime World { get; }
        public CheckpointHost(PrivateWorldRuntime world, DeliveryChoices provider, bool refuseArrival = false)
        {
            World = world;
            choices = provider;
            this.refuseArrival = refuseArrival;
            file = new(Path.Combine(directory.FullName, "world.json"));
            file.Save(world);
            world.Resume();
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            service = new(world, file, presence);
        }
        public async Task Step()
        {
            if (refuseArrival && !RefusedArrival && World.Inhabitants.Any(person =>
                    person.GuardianPlacement is { Stage: "escorting", HousePosition: { } home } placement &&
                    person.TravelCooldownTicks == 0 && World.ExportState().Map.FootDistance(person.Position, home) == 1 &&
                    World.Inhabitants.Single(guardian => guardian.InhabitantId == placement.CaregiverId).Position == home))
            {
                var before = PrivateWorldRuntimeCodec.Encode(World.ExportState());
                Assert.False((await World.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
                Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(World.ExportState()));
                RefusedArrival = true;
            }
            Assert.True(await service.TryAdvanceOnceAsync(), $"Native host halted at tick {World.WorldTick}.");
            var pending = (IDictionary)typeof(PrivateWorldRuntime).GetField("pendingHosted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(World)!;
            await Task.WhenAll(pending.Values.Cast<object>().Select(item => (Task)item.GetType().GetProperty("Task")!.GetValue(item)!))
                .WaitAsync(TimeSpan.FromSeconds(30));
            var bytes = File.ReadAllBytes(file.Path);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(World.ExportState()));
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => choices.Copy());
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        public async Task Until(Func<bool> completed)
        {
            for (var tick = 0; tick < 30 && !completed(); tick++) await Step();
            Assert.True(completed());
        }
        public void Dispose()
        {
            service.Dispose();
            directory.Delete(true);
        }
    }

    private sealed class DeliveryChoices(string child, string caregiver, bool guardian = false) : IDecisionProvider
    {
        public bool Pickup { get; init; } = true;
        public bool Deliver { get; set; }
        public bool ChangeHousehold { get; set; }
        public List<(string Actor, string Candidate)> Selected { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public DeliveryChoices Copy() => new(child, caregiver, guardian) { Pickup = Pickup, Deliver = Deliver, ChangeHousehold = ChangeHousehold };
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var observation = request.Observation;
            string[] wanted = observation.InhabitantId == child
                ? Deliver ? ["child_carry:"] : Pickup && !ChangeHousehold ? ["child_carry:child-moving-wood"] : []
                : observation.InhabitantId == caregiver && ChangeHousehold
                    ? guardian ? ["guardian_accept:" + child, "guardian_relocate:" + child] : ["household_leave"] : [];
            var candidate = wanted.Select(prefix => observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .FirstOrDefault(item => item is not null) ?? observation.Candidates.Single(item => item.Id == "safe_idle");
            Selected.Add((observation.InhabitantId, candidate.Id));
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, candidate.Id, 1,
                new Dictionary<string, double> { [candidate.Id] = 1 },
                ChosenName: observation.NeedsName ? observation.Self!.Name : null,
                ChosenPersonality: observation.NeedsPersonality ? "patient" : null,
                ChosenAspiration: observation.NeedsAspiration ? "help at home" : null));
        }
    }
}
