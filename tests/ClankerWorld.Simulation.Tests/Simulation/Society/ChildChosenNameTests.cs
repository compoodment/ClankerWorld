using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class ChildChosenNameTests
{
    [Theory]
    [InlineData("Nori Vale")]
    [InlineData("Nori Reed")]
    public void ExplicitBirthNameCanUseEitherBiologicalParentsSurname(string name)
    {
        var checkpoint = BirthCheckpoint();
        var result = SocietyFixture.CommitBirth(checkpoint, BirthRequest(checkpoint) with { ChildName = name });
        var child = result.Checkpoint.GetInhabitant(Assert.IsType<string>(result.CreatedId));
        Assert.Equal(name, child.Name);
        Assert.True(child.HasChosenName);
        Assert.False(child.NeedsName);
        Assert.Equal("first", child.PrimaryCaregiverId);
        Assert.Equal("home", child.HouseholdId);
        Assert.Equal(2, result.Checkpoint.Inventory.GetLot("birth-food").Quantity);
        Assert.Equal(InventoryReservationState.Completed,
            result.Checkpoint.Inventory.GetReservation("birth:naming-birth:food").State);

        // An already admitted name remains valid when a parent later changes theirs.
        var renamedParent = SocietyFixture.RenameInhabitant(result.Checkpoint, "first", "Morgan Dune").Checkpoint;
        renamedParent = SocietyFixture.RenameInhabitant(renamedParent, "second", "Élodie Brook").Checkpoint;
        var restored = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(renamedParent));
        Assert.Equal(name, restored.GetInhabitant(child.Id).Name);
        Assert.True(restored.GetInhabitant(child.Id).HasChosenName);
    }

    [Theory]
    [InlineData("e\u0301LODIE\u00a0Vale", true)]
    [InlineData("Nori Ridge", false)]
    public void InvalidExplicitBirthNameIsRefusedBeforeAnyFoodOrFamilyMutation(string name, bool duplicate)
    {
        var checkpoint = BirthCheckpoint();
        var saved = SocietyCheckpointCodec.Encode(checkpoint);
        var request = BirthRequest(checkpoint) with { ChildName = name };
        if (duplicate)
            Assert.Throws<InhabitantNameTakenException>(() => SocietyFixture.CommitBirth(checkpoint, request));
        else
            Assert.Throws<ArgumentException>(() => SocietyFixture.CommitBirth(checkpoint, request));
        Assert.Equal(saved, SocietyCheckpointCodec.Encode(checkpoint));
        Assert.Empty(checkpoint.Births);
        Assert.Empty(checkpoint.Inventory.Reservations);
        Assert.Equal(4, checkpoint.Inventory.GetLot("birth-food").Quantity);

        // Even an unavailable food source cannot mask the earlier name refusal.
        request = request with { FoodLotId = "missing-food" };
        if (duplicate)
            Assert.Throws<InhabitantNameTakenException>(() => SocietyFixture.CommitBirth(checkpoint, request));
        else
            Assert.Throws<ArgumentException>(() => SocietyFixture.CommitBirth(checkpoint, request));
    }

    [Fact]
    public void OmittedBirthNamesStayUnchosenAndDoNotReserveThePlaceholderFirstName()
    {
        var checkpoint = BirthCheckpoint();
        var first = SocietyFixture.CommitBirth(checkpoint, BirthRequest(checkpoint));
        var second = SocietyFixture.CommitBirth(first.Checkpoint, BirthRequest(first.Checkpoint) with { Id = "second-birth" });
        var children = second.Checkpoint.Births.Select(birth => second.Checkpoint.GetInhabitant(birth.ChildId)).ToArray();
        Assert.Equal(2, children.Length);
        Assert.All(children, child =>
        {
            Assert.Equal("Child", child.Name);
            Assert.False(child.HasChosenName);
            Assert.True(child.NeedsName);
        });

        var named = SocietyFixture.RenameInhabitant(second.Checkpoint, "first", "Child Vale").Checkpoint;
        Assert.True(named.GetInhabitant("first").HasChosenName);
        var restored = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(named));
        Assert.All(children, child =>
        {
            Assert.Equal(child.Name, restored.GetInhabitant(child.Id).Name);
            Assert.False(restored.GetInhabitant(child.Id).HasChosenName);
            Assert.True(restored.GetInhabitant(child.Id).NeedsName);
        });
    }

    [Fact]
    public void ChildCanChooseTheSurnameOfABiologicalParentWhoHasDied()
    {
        var checkpoint = BirthCheckpoint();
        var birth = SocietyFixture.CommitBirth(checkpoint, BirthRequest(checkpoint));
        var child = Assert.IsType<string>(birth.CreatedId);
        checkpoint = SocietyFixture.Kill(birth.Checkpoint, "second", SocietyDeathCause.Accident).Checkpoint;
        Assert.Contains(checkpoint.Relationships, edge => edge.TargetId == child && edge.ProposerId == "second" &&
            edge.Type == SocietyRelationshipType.BiologicalParentage && edge.State == SocietyRelationshipState.EndedByDeath);
        var named = SocietyFixture.RenameInhabitant(checkpoint, child, "Nori Reed").Checkpoint;
        var restored = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(named));
        Assert.Equal("Nori Reed", restored.GetInhabitant(child).Name);
        Assert.True(restored.GetInhabitant(child).HasChosenName);
        Assert.False(restored.GetInhabitant(child).NeedsName);
    }

    [Fact]
    public void OwnerCanNameAnInfantWithEitherParentsSurnameButARefusalChangesNothing()
    {
        var (state, childId) = RuntimeBirth(ownerWorld: true);
        using var world = PrivateWorldRuntime.Restore(state, _ => new QuietProvider());
        Assert.Equal(SocietyAgeBand.Infant, world.Society.GetInhabitant(childId).AgeBand);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Throws<ArgumentException>(() => world.RenameAgent(childId, "Nori Ridge"));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.RenameAgent(childId, "Nori Vale"));
        Assert.True(world.RenameAgent(childId, "Nori Reed"));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new QuietProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var child = restored.Society.GetInhabitant(childId);
        Assert.Equal("Nori Reed", child.Name);
        Assert.True(child.HasChosenName);
        Assert.False(child.NeedsName);
        Assert.Equal(SocietyAgeBand.Infant, child.AgeBand);
    }

    [Fact]
    public async Task InfantWaitsForChildhoodThenRetriesATakenFirstNameAcrossSaveAndReload()
    {
        var (state, childId) = RuntimeBirth(birthRequestId: "naming-birth:" + new string('x', 160));
        Assert.True(childId.Length > 128);
        var provider = new ChildNameProvider("e\u0301LODIE Vale");
        using var world = PrivateWorldRuntime.Restore(state, id => id == childId ? provider : new QuietProvider());
        world.Resume();
        var placeholder = world.Society.GetInhabitant(childId).Name;
        Assert.Equal("Child", placeholder);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(SocietyAgeBand.Infant, world.Society.GetInhabitant(childId).AgeBand);
        Assert.Empty(provider.Observations);
        Assert.True(world.Society.GetInhabitant(childId).NeedsName);

        await AdvanceUntilAsync(world, () => world.ExportState().Events.Any(item =>
            item.Kind == "agent_name_retry_requested" && item.Detail == childId));
        Assert.Equal(SocietyAgeBand.Child, world.Society.GetInhabitant(childId).AgeBand);
        Assert.Equal(placeholder, world.Society.GetInhabitant(childId).Name);
        Assert.False(world.Society.GetInhabitant(childId).HasChosenName);
        var firstRequest = Assert.Single(provider.Observations);
        Assert.True(firstRequest.NeedsName);
        Assert.True(firstRequest.RequiresPersonalProvider);
        Assert.False(firstRequest.IsNameRetry);
        Assert.Equal(placeholder, firstRequest.Self!.Name);
        Assert.Equal(["Reed", "Vale"], firstRequest.Self!.AllowedChildSurnames);

        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var decoded = PrivateWorldRuntimeCodec.Decode(saved);
        Assert.Contains(SocietyCognitionScheduler.NameRetryTriggerId,
            Assert.Single(decoded.Society.Cognition.Queue, item => item.InhabitantId == childId).TriggerIds);
        var retryProvider = new ChildNameProvider("Nori Reed");
        using var restored = PrivateWorldRuntime.Restore(decoded, id => id == childId ? retryProvider : new QuietProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Resume();
        await AdvanceUntilAsync(restored, () => !restored.Society.GetInhabitant(childId).NeedsName);
        var retry = Assert.Single(retryProvider.Observations);
        Assert.True(retry.IsNameRetry);
        Assert.True(retry.RequiresPersonalProvider);
        Assert.Equal(["Reed", "Vale"], retry.Self!.AllowedChildSurnames);
        Assert.Equal("Nori Reed", restored.Society.GetInhabitant(childId).Name);
        Assert.True(restored.Society.GetInhabitant(childId).HasChosenName);
        Assert.Equal(state.Society.Society.GetInhabitant(childId).PrimaryCaregiverId,
            restored.Society.GetInhabitant(childId).PrimaryCaregiverId);
        Assert.Equal(state.Society.Society.GetInhabitant(childId).HouseholdId,
            restored.Society.GetInhabitant(childId).HouseholdId);
        restored.Pause();
        var namedSave = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal("Nori Reed", namedSave.Society.Society.GetInhabitant(childId).Name);
        Assert.True(namedSave.Society.Society.GetInhabitant(childId).HasChosenName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableParentalSurnameClosesTheRequestWithoutAnotherNamingCall(bool unnamedParents)
    {
        var (state, childId) = RuntimeBirth(unnamedParents);
        var provider = new ChildNameProvider("Nori Ridge");
        using var world = PrivateWorldRuntime.Restore(state, id => id == childId ? provider : new QuietProvider());
        var placeholder = world.Society.GetInhabitant(childId).Name;
        world.Resume();
        await AdvanceUntilAsync(world, () => !world.Society.GetInhabitant(childId).NeedsName);
        var request = Assert.Single(provider.Observations);
        Assert.False(request.IsNameRetry);
        Assert.Equal(unnamedParents ? Array.Empty<string>() : ["Reed", "Vale"], request.Self!.AllowedChildSurnames);
        Assert.Equal(placeholder, world.Society.GetInhabitant(childId).Name);
        Assert.False(world.Society.GetInhabitant(childId).HasChosenName);
        Assert.DoesNotContain(world.ExportState().Society.Cognition.Queue, item => item.InhabitantId == childId &&
            item.TriggerIds.Contains(SocietyCognitionScheduler.NameRetryTriggerId, StringComparer.Ordinal));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "agent_name_retry_requested" && item.Detail == childId);
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Single(provider.Observations);
    }

    private static SocietyCheckpoint BirthCheckpoint()
    {
        var checkpoint = SocietyFixture.CreateGenesis("child-names",
            [SocietyFixture.CreateFounder("first", "Morgan Vale"), SocietyFixture.CreateFounder("second", "Élodie Reed")],
            [new InventoryLot("birth-food", "food", "first", 4, 10_000, 10_000, 0)]);
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "Home", ["first", "second"]).Checkpoint;
        return Partner(checkpoint, "first", "second");
    }

    private static SocietyCheckpoint Partner(SocietyCheckpoint checkpoint, string first, string second)
    {
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint, new SocietyRelationshipProposal("parents", 1,
            SocietyRelationshipType.Partnership, first, second, checkpoint.WorldTick)).Checkpoint;
        return SocietyFixture.AcceptRelationship(checkpoint, "parents", 1, second).Checkpoint;
    }

    private static SocietyBirthRequest BirthRequest(SocietyCheckpoint checkpoint) => new("naming-birth", 1,
        "first", "second", "home", ["first", "second"], ["first", "second"], "birth-food", 2,
        checkpoint.WorldTick, PrimaryCaregiverId: "first");

    private static (PrivateWorldRuntimeState State, string ChildId) RuntimeBirth(
        bool unnamedParents = false, string birthRequestId = "naming-birth", bool ownerWorld = false)
    {
        using var original = new PrivateWorldRuntime("child-naming-runtime", _ => new QuietProvider(),
            maxCognitionDispatchPerCycle: 8, startPace: ownerWorld ? WorldStartPace.FounderSetup : WorldStartPace.Legacy);
        var first = ownerWorld ? "founder:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" : "founder-scout";
        var second = ownerWorld ? "founder:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" : "founder-mira";
        if (ownerWorld)
        {
            original.PlaceFounder(first, new GridPoint(0, 0));
            original.PlaceFounder(second, new GridPoint(1, 2));
            original.PlaceFounder("founder:cccccccccccccccccccccccccccccccc", new GridPoint(2, 2));
            original.PlaceFounder("founder:dddddddddddddddddddddddddddddddd", new GridPoint(3, 2));
            original.StartWorld(resume: false);
        }
        original.Pause();
        var state = original.ExportState();
        var checkpoint = SocietyFixture.RenameInhabitant(state.Society.Society, first, "Morgan Vale").Checkpoint;
        checkpoint = SocietyFixture.RenameInhabitant(checkpoint, second, "Élodie Reed").Checkpoint;
        if (unnamedParents)
            checkpoint = checkpoint with
            {
                Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == first || person.Id == second
                    ? person with { HasChosenName = false } : person).ToArray(),
            };
        checkpoint = Partner(checkpoint, first, second);
        var household = checkpoint.GetInhabitant(first).HouseholdId!;
        var birth = SocietyFixture.CommitBirth(checkpoint, new SocietyBirthRequest(birthRequestId, 1, first, second,
            household, [first, second], [first, second], "food:camp-alpha", 2, checkpoint.WorldTick,
            PrimaryCaregiverId: first));
        var childId = Assert.IsType<string>(birth.CreatedId);
        checkpoint = birth.Checkpoint;
        Assert.Null(checkpoint.LifeClock);
        // Start two real ticks before the infant/child boundary, without simulating years of unrelated work.
        var childStartAge = checkpoint.Config.DayLifecycle?.ChildStartDay ?? checkpoint.Config.InfantYears;
        var birthTick = checkpoint.WorldTick + 2 - childStartAge * checkpoint.Config.TicksPerLifecycleAge;
        checkpoint = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == childId
                ? person with { BirthTick = birthTick, LastLifecycleYearChecked = childStartAge - 1 }
                : person).ToArray(),
        };
        var parent = state.Inhabitants.Single(person => person.InhabitantId == first);
        var childPosition = state.Map.Tiles.Select(tile => tile.Position)
            .Where(position => state.Map.IsBuildable(position) && !state.Inhabitants.Any(person => person.Position == position))
            .OrderBy(position => Math.Abs(position.X - parent.Position.X) + Math.Abs(position.Y - parent.Position.Y)).First();
        return (state with
        {
            Society = state.Society with { Society = checkpoint },
            Survival = state.Survival ?? new SettlementSurvivalState(checkpoint.WorldTick, []),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(first, StringComparer.Ordinal)
                ? town with { ResidentIds = town.ResidentIds.Append(childId).Order(StringComparer.Ordinal).ToArray() }
                : town).ToArray(),
            Inhabitants = state.Inhabitants.Append(new PlaytestInhabitantState(childId, childPosition,
                10_000, 0, "curious", "grow with the household", Survival: new SurvivalCondition(WarmthBasisPoints: 10_000))).ToArray(),
        }, childId);
    }

    private static async Task AdvanceUntilAsync(PrivateWorldRuntime world, Func<bool> completed)
    {
        for (var tick = 0; tick < 12 && !completed(); tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            if (!completed())
            {
                // As in the will/conversation fixtures, await the complete runtime handoff between
                // controlled ticks; a provider-return signal alone can precede task completion.
                var field = typeof(PrivateWorldRuntime).GetField("pendingHosted", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var pending = Assert.IsAssignableFrom<IDictionary>(field.GetValue(world)).Values.Cast<object>();
                var tasks = pending.Select(item => Assert.IsAssignableFrom<Task>(item.GetType().GetProperty("Task")!.GetValue(item)));
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        Assert.True(completed(), "The child's completed naming response was not admitted within twelve ticks.");
    }

    private sealed class QuietProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Reply(request, Kind, null));
    }

    private sealed class ChildNameProvider(string name) : IDecisionProvider
    {
        public ConcurrentQueue<InhabitantObservation> Observations { get; } = new();
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Observations.Enqueue(request.Observation);
            return ValueTask.FromResult(Reply(request, Kind, name));
        }
    }

    private static CognitionDecisionResponse Reply(CognitionDecisionRequest request, DecisionProviderKind kind, string? name)
    {
        var observation = request.Observation;
        return new(request.RequestId, observation.InhabitantId, kind, 1, observation.RunEpoch,
            observation.DecisionGeneration, observation.ObservationDigest, "safe_idle", 1,
            observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == "safe_idle" ? 1d : 0d,
                StringComparer.Ordinal), ChosenName: name);
    }
}
