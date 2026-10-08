using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Fact]
    public async Task APendingPersonalReplyKeepsItsPreviouslyChosenTripWithoutCommittingARejectedTick()
    {
        var provider = new WaitingRoutineProvider("seek_food", holdFromCall: 2);
        using var setup = CreateCancellationTravelWorld(new CancellationPlanningProvider());
        using var world = RestoreWaitingWorld(setup.ExportState(), HarvestInstructionActor, provider);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("seek_food", world.ExportState().Society.Cognition.Runtimes.Single(item => item.InhabitantId == HarvestInstructionActor).CurrentIntention!.CandidateId);
        world.SubmitInstruction(new("waiting-trip-suggestion", "owner:test", HarvestInstructionActor,
            OwnerInstructionKind.Suggestive, "Think about your next task."));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickNonBlockingAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var position = CancellationActorPosition(world.ExportState());
        for (var tick = 0; tick < 5; tick++)
        {
            var step = await world.AdvanceOneTickNonBlockingAsync();
            Assert.True(step.Advanced);
            Assert.DoesNotContain(step.Decisions, item => item.InhabitantId == HarvestInstructionActor);
        }
        Assert.NotEqual(position, CancellationActorPosition(world.ExportState()));
        Assert.Equal(2, provider.Requests.Count);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "hosted_decision_completed");
        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(paused));
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        using var replay = RestoreWaitingWorld(PrivateWorldRuntimeCodec.Decode(paused), HarvestInstructionActor,
            new WaitingRoutineProvider("seek_food"));
        using var duplicate = RestoreWaitingWorld(PrivateWorldRuntimeCodec.Decode(paused), HarvestInstructionActor,
            new WaitingRoutineProvider("seek_food"));
        replay.Resume();
        duplicate.Resume();
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await replay.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.True((await duplicate.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(replay.ExportState()), PrivateWorldRuntimeCodec.Encode(duplicate.ExportState()));
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task APendingPersonalReplyFeedsOnlyANearbyDependentAndMakesNoNewCareAgreement(bool nearby, bool ordered)
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("waiting-dependent");
        var checkpoint = ChosenBirthNameTestFixture.NameParent(state.Society.Society, actor);
        var partner = checkpoint.Inhabitants.First(person => person.HouseholdId == household && person.Id != actor).Id;
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint, new("waiting-care-parents", 1,
            SocietyRelationshipType.Partnership, actor, partner, checkpoint.WorldTick)).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "waiting-care-parents", 1, partner).Checkpoint;
        var food = checkpoint.Inventory.Lots.First(lot => lot.OwnerId == household && lot.ItemKind == "food" && lot.Quantity >= 4);
        var birth = SocietyFixture.CommitBirth(checkpoint, new("waiting-care-birth", 1, actor, partner,
            household, [actor, partner], [actor, partner], food.Id, 4, checkpoint.WorldTick,
            ChildName: ChosenBirthNameTestFixture.ChildName(checkpoint, actor, "Ari"), PrimaryCaregiverId: actor));
        var child = Assert.IsType<string>(birth.CreatedId);
        checkpoint = birth.Checkpoint with
        {
            Inventory = InventoryFixture.AddLot(birth.Checkpoint.Inventory,
            "waiting-child-serving", "berries", actor, 1)
        };
        var childPosition = state.Map.Tiles.Select(tile => tile.Position).First(tile => state.Map.IsBuildable(tile) &&
            !state.Inhabitants.Any(person => person.Position == tile) &&
            (nearby ? state.Map.FootDistance(point, tile) == 1 : state.Map.FootDistance(point, tile) is >= 8 and <= 10));
        state = state with
        {
            Society = state.Society with { Society = checkpoint },
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_000,
                Survival = new SurvivalCondition(10_000),
                LastDecisionContext = null
            }).Append(
                new PlaytestInhabitantState(child, childPosition, 1_000, 0, "curious", "grow",
                    Survival: new SurvivalCondition(10_000))).OrderBy(person => person.InhabitantId, StringComparer.Ordinal).ToArray(),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(actor) ? town with
            { ResidentIds = town.ResidentIds.Append(child).Order(StringComparer.Ordinal).ToArray() } : town).ToArray(),
        };
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);
        var provider = new WaitingRoutineProvider("safe_idle");
        using var world = RestoreWaitingWorld(state, actor, provider);
        if (ordered) world.SubmitInstruction(new("waiting-care-order", "owner:test", actor,
            OwnerInstructionKind.MustDo, "gather food"));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var relationships = world.Society.Relationships.ToArray();
        var position = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(relationships, world.Society.Relationships);
        if (nearby)
        {
            Assert.Contains(world.ExportState().Events, item => item.Kind == "child_cared_for" && item.Detail == child);
            Assert.True(world.Inhabitants.Single(person => person.InhabitantId == child).HungerBasisPoints > 2_000);
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "waiting-child-serving");
        }
        else
        {
            Assert.Equal(position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
            Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "child_cared_for");
            Assert.Equal(1, world.Society.Inventory.GetLot("waiting-child-serving").Quantity);
        }
        Assert.Single(provider.Requests);
        world.Validate();
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(world.Inhabitants.Single(person => person.InhabitantId == child).HungerBasisPoints,
            restored.Inhabitants.Single(person => person.InhabitantId == child).HungerBasisPoints);
    }

    [Theory]
    [InlineData(WeatherKind.Clear, true)]
    [InlineData(WeatherKind.Rain, false)]
    [InlineData(WeatherKind.Storm, false)]
    public async Task APendingPersonalReplyUsesLiveUrgentFoodOrWeatherProtectionWithoutStartingAProject(WeatherKind weather, bool hungry)
    {
        using var setup = new PrivateWorldRuntime("waiting-survival", _ =>
            new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        setup.StageStarterContent();
        for (var tick = 0; tick < 6; tick++) Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        const string actor = "founder-scout";
        var state = setup.ExportState();
        var house = setup.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            !state.Map.Resources.Any(resource => resource.Position == point) && !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var placement = setup.PlaceBuilding("waiting-warm-home", house.CanonicalId, site, "household:camp-alpha");
        Assert.True(placement.Applied, placement.Failure);
        state = setup.ExportState();
        var start = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            state.Map.FootDistance(point, site) is >= 4 and <= 7 && state.Map.VegetationAt(point) != VegetationCover.Forest &&
            !state.Map.Resources.Any(resource => resource.Position == point) && !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point) && state.Map.IsReachableOnFoot(point, site));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "waiting-own-food", "food", actor, 1);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = start,
                HungerBasisPoints = hungry ? 2_050 : 9_000,
                Survival = new SurvivalCondition(hungry ? 10_000 : 4_000),
                LastDecisionContext = null,
                TravelCooldownTicks = 0
            } : person).ToArray(),
        };
        state = SettlementWeatherTestFixture.WithWeather(state, weather);
        var provider = new WaitingRoutineProvider("safe_idle");
        using var world = RestoreWaitingWorld(state, actor, provider);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains(provider.Requests.Single().Candidates, candidate => candidate.Id == "household_leave");
        var buildings = world.WorldSimulation.Buildings.ToArray();
        var relationships = world.Society.Relationships.ToArray();
        for (var tick = 0; tick < 20; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        var person = world.Inhabitants.Single(person => person.InhabitantId == actor);
        if (hungry)
        {
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "waiting-own-food");
            Assert.True(person.HungerBasisPoints > 2_100);
        }
        else
        {
            Assert.NotEqual(start, person.Position);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
                item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        }
        Assert.Null(person.Project);
        Assert.Equal(buildings, world.WorldSimulation.Buildings);
        Assert.Equal(relationships, world.Society.Relationships);
        Assert.Single(provider.Requests);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "hosted_decision_completed");
        world.Validate();
    }

    private static PrivateWorldRuntime RestoreWaitingWorld(PrivateWorldRuntimeState state, string actor, WaitingRoutineProvider provider) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? provider :
            new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));

    private sealed class WaitingRoutineProvider(string firstChoice, int holdFromCall = 1) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentQueue<InhabitantObservation> Requests { get; } = new();
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request.Observation);
            if (Requests.Count >= holdFromCall)
            {
                Started.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            }
            Assert.Contains(request.Observation.Candidates, candidate => candidate.Id == firstChoice);
            return HostedResponse(request, Kind, ProviderEpoch, firstChoice);
        }
    }
}
