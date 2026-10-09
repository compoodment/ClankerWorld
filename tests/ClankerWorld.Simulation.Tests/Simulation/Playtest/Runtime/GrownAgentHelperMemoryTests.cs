using System.Reflection;
using System.Collections.Concurrent;
using System.Text.Json;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class GrownAgentHelperMemoryTests
{
    private static readonly Lazy<Task<byte[]>> Born = new(CreateBornAsync);
    private static readonly Lazy<Task<byte[]>> Adult = new(CreateAdultAsync);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeHouseholdFormationKeepsSavedIdentityAndContinuesAfterReload(bool nativeBorn)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Adult.Value);
        var birth = Assert.Single(state.Society.Society.Births);
        var actor = nativeBorn ? birth.ChildId : birth.PrimaryCaregiverId;
        var choices = new HouseholdChoices(actor);
        using var displaced = PrivateWorldRuntime.Restore(state, _ => choices);
        Assert.True(displaced.DisplaceAdult(actor));
        state = displaced.ExportState();
        // Match the existing solo-formation fixture's explicit refusal setup;
        // birth, adulthood, departure and the offered formation action are native.
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    LastDecisionContext = null,
                    Housing = new(Refusals: state.Society.Society.Households.Select(household =>
                        new SettlementHousingRefusal(household.Id, state.Society.Society.WorldTick)).ToArray()),
                } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => choices);
        world.Resume();
        for (var tick = 0; tick < 12 && world.Society.GetInhabitant(actor).HouseholdId is null; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var householdId = Assert.IsType<string>(world.Society.GetInhabitant(actor).HouseholdId);
        Assert.Equal(nativeBorn, householdId.Length > 128);
        Assert.StartsWith("household:solo:" + actor + ":", householdId, StringComparison.Ordinal);
        Assert.Equal([actor], world.Society.GetHousehold(householdId).MemberIds);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "household_founded" &&
            item.Detail == actor + "|" + householdId);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var next = new HouseholdChoices(actor);
        var repeated = new HouseholdChoices(actor);
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => next);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => repeated);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await reload.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(reload.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var observation = Assert.Single(next.Observations.Where(item => item.Self?.HouseholdId is not null)
            .Take(1));
        observation.Validate();
        Assert.InRange(observation.Self!.HouseholdId!.Length, 1, 128);
        if (nativeBorn)
            Assert.StartsWith("household-sha256:", observation.Self.HouseholdId, StringComparison.Ordinal);
        else
            Assert.Equal(householdId, observation.Self.HouseholdId);
        Assert.Equal(householdId, reload.Society.GetInhabitant(actor).HouseholdId);
        Assert.Equal(JsonSerializer.Serialize(world.Society.GetHousehold(householdId)),
            JsonSerializer.Serialize(reload.Society.GetHousehold(householdId)));
        await CheckWillHouseholdContext(reload, actor, householdId, observation.Self.HouseholdId);
    }

    private static async Task CheckWillHouseholdContext(PrivateWorldRuntime world, string actor,
        string householdId, string modelHouseholdId)
    {
        // Archive an explicit fixture death with one real frozen lot, following
        // PostDeathWillTests. The household itself was formed by the native runtime.
        var state = world.ExportState();
        var physical = state.Inhabitants.Single(person => person.InhabitantId == actor);
        var checkpoint = state.Society.Society;
        checkpoint = checkpoint with
        {
            Inventory = InventoryFixture.AddLot(checkpoint.Inventory, "household-context-will-lot", "stone", actor, 1,
                groundPosition: new InventoryGroundPosition(physical.Position.X, physical.Position.Y)),
        };
        checkpoint = SocietyFixture.Kill(checkpoint, actor, SocietyDeathCause.Accident).Checkpoint;
        var inventory = checkpoint.Inventory;
        if (inventory.Lots.Any(lot => lot.CarrierId == actor))
            inventory = InventoryFixture.DropCarrierGoods(inventory, actor,
                new InventoryGroundPosition(physical.Position.X, physical.Position.Y));
        checkpoint = checkpoint with { Inventory = inventory };
        state = state with
        {
            Society = state.Society with { Society = checkpoint },
            Towns = state.Towns!.Select(town =>
            {
                var residents = town.ResidentIds.Where(id => id != actor).ToArray();
                var adults = residents.Where(id => checkpoint.GetInhabitant(id).Status == SocietyInhabitantStatus.Active &&
                    checkpoint.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder);
                var advanced = TownGovernmentRules.Advance(town.Governance!, town.Government!, town.Id,
                    town.Name, state.WorldSeed, adults, checkpoint.WorldTick, state.WorldSystems!.Config.TicksPerDay);
                return town with { ResidentIds = residents, Governance = advanced.Council, Government = advanced.Government };
            }).ToArray(),
            Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != actor).ToArray(),
            DeceasedInhabitants = (state.DeceasedInhabitants ?? []).Append(new PlaytestDeceasedInhabitantState(
                actor, world.WorldTick, checkpoint.AgeAt(checkpoint.GetInhabitant(actor), world.WorldTick), physical)).ToArray(),
        };
        var provider = new HouseholdChoices(actor);
        using var willWorld = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        Assert.True((await willWorld.AdvanceOneTickNonBlockingAsync()).Advanced);
        var will = await provider.WillCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        will.Validate();
        Assert.Equal(modelHouseholdId, will.Self!.HouseholdId);
        Assert.Equal(householdId, willWorld.Society.GetInhabitant(actor).HouseholdId);
        Assert.Equal(world.Society.GetHousehold(householdId).Name,
            willWorld.Society.GetHousehold(householdId).Name);
    }

    private static async Task<byte[]> CreateAdultAsync()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Born.Value);
        var child = Assert.Single(state.Society.Society.Births).ChildId;
        var checkpoint = state.Society.Society;
        foreach (var parent in checkpoint.Relationships.Where(edge =>
                     edge.Type == SocietyRelationshipType.BiologicalParentage && edge.TargetId == child)
                     .Select(edge => edge.ProposerId))
            checkpoint = ChosenBirthNameTestFixture.NameParent(checkpoint, parent);
        state = state with { Society = state.Society with { Society = checkpoint } };
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == child ? new InitialIdentityProvider() : new QuietProvider());
        world.Pause();
        world.SetJevEnabled(false);
        world.SetLifePace(365);
        world.Resume();
        while (world.Society.AgeAt(world.Society.GetInhabitant(child), world.WorldTick) < 15)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(world);
        }
        world.Pause();
        world.SetLifePace(1);
        world.Resume();
        for (var tick = 0; tick < 40 && (world.Society.GetInhabitant(child).NeedsName ||
             world.Inhabitants.Single(person => person.InhabitantId == child).IdentityChoicePending); tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(world);
        }
        Assert.Equal(SocietyAgeBand.Adult, world.Society.GetInhabitant(child).AgeBand);
        Assert.False(world.Society.GetInhabitant(child).NeedsName);
        Assert.False(world.Inhabitants.Single(person => person.InhabitantId == child).IdentityChoicePending);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private sealed class HouseholdChoices(string actor) : IDecisionProvider
    {
        public ConcurrentQueue<InhabitantObservation> Observations { get; } = new();
        public TaskCompletionSource<InhabitantObservation> WillCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Observation.InhabitantId == actor) Observations.Enqueue(request.Observation);
            if (request.Observation.Will is not null)
            {
                WillCalled.TrySetResult(request.Observation);
                return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                    request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                    request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                    CognitionWillContext.HouseholdCandidateId, 1,
                    request.Observation.Candidates.ToDictionary(item => item.Id,
                        item => item.Id == CognitionWillContext.HouseholdCandidateId ? 1d : 0d)));
            }
            var candidate = request.Observation.InhabitantId == actor
                ? request.Observation.Candidates.FirstOrDefault(item => item.Id == "household_found") : null;
            candidate ??= request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, candidate.Id, 1,
                request.Observation.Candidates.ToDictionary(item => item.Id, item => item.Id == candidate.Id ? 1d : 0d)));
        }
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(15, true)]
    [InlineData(15, false)]
    public async Task NativeBornResidentOnlyOffersOwnUnassessedMemoriesAfterAdulthoodWithHelperOn(int age, bool helperOn)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Born.Value);
        var childId = Assert.Single(state.Society.Society.Births).ChildId;
        Assert.True(childId.Length > 128, "The native opaque-world birth must exercise a long identity.");
        if (age == 15)
        {
            var society = state.Society.Society;
            foreach (var parent in society.Relationships.Where(edge => edge.Type == SocietyRelationshipType.BiologicalParentage && edge.TargetId == childId)
                         .Select(edge => edge.ProposerId))
                society = ChosenBirthNameTestFixture.NameParent(society, parent);
            state = state with { Society = state.Society with { Society = society } };
        }
        using (var aging = PrivateWorldRuntime.Restore(state,
            id => id == childId && age == 15 ? new InitialIdentityProvider() : new QuietProvider()))
        {
            aging.Pause();
            aging.SetLifePace(365);
            aging.Resume();
            while (aging.Society.AgeAt(aging.Society.GetInhabitant(childId), aging.WorldTick + 1) < age)
            {
                Assert.True((await aging.AdvanceOneTickNonBlockingAsync()).Advanced);
                await WaitForRequests(aging);
            }
            if (age == 15)
            {
                // Finish the actual adult boundary before slowing the clock;
                // the accelerated next-tick forecast can still be a child now.
                while (aging.Society.AgeAt(aging.Society.GetInhabitant(childId), aging.WorldTick) < age)
                {
                    Assert.True((await aging.AdvanceOneTickNonBlockingAsync()).Advanced);
                    await WaitForRequests(aging);
                }
                Assert.Equal(SocietyAgeBand.Adult, aging.Society.GetInhabitant(childId).AgeBand);
                // Accelerated aging can cross several identity boundaries
                // before a hosted reply is admitted. Finish the ordinary
                // request at the normal rate before swapping to the helper.
                aging.Pause();
                aging.SetLifePace(1);
                aging.Resume();
                for (var tick = 0; tick < 40 &&
                    (aging.Inhabitants.Single(item => item.InhabitantId == childId).IdentityChoicePending ||
                     aging.Society.GetInhabitant(childId).NeedsName); tick++)
                {
                    Assert.True((await aging.AdvanceOneTickNonBlockingAsync()).Advanced);
                    await WaitForRequests(aging);
                }
                Assert.False(aging.Inhabitants.Single(item => item.InhabitantId == childId).IdentityChoicePending);
                Assert.False(aging.Society.GetInhabitant(childId).NeedsName);
            }
            aging.Pause();
            state = aging.ExportState();
        }
        var sourceTick = state.Society.Society.WorldTick;
        var memories = Enumerable.Range(0, 3).Select(index => new SocietySocialMemory(
            $"grown-memory-{index}", childId, childId, $"A private remembered promise {index}.", "private", sourceTick)).ToArray();
        var foreign = new SocietySocialMemory("foreign-memory", state.Society.Society.Births[0].PrimaryCaregiverId, childId,
            "Another person's private promise.", "private", sourceTick);
        var belief = new SocietyAgentBelief("grown-birth-belief", childId, "I remember my own birth.",
            SocietyBeliefProvenance.Firsthand, 8_000, sourceTick, SourceAgentId: childId,
            SourceEventId: Assert.Single(state.Events, item => item.Kind == "child_born").EventId,
            AboutInhabitantId: childId);
        var withBelief = SocietyFixture.RecordAgentBelief(state.Society.Society, belief);
        state = state with
        {
            Society = state.Society with
            {
                Society = withBelief with { Memories = state.Society.Society.Memories.Concat(memories).Append(foreign).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray() },
            },
        };
        // A controlled helper exercises the runtime's age and memory boundary.
        // Real HTTP routing for both helpers is tested separately.
        var provider = new RecordingHelper();
        var replayProvider = new RecordingHelper();
        using var world = PrivateWorldRuntime.Restore(state, id => id == childId ? provider : new QuietProvider());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == childId ? replayProvider : new QuietProvider());
        foreach (var runtime in new[] { world, replay })
        {
            runtime.Pause();
            runtime.SetJevEnabled(helperOn);
            runtime.Resume();
        }
        // The native identity reply was just admitted. Wait through the
        // ordinary decision cadence rather than requiring a second call early.
        for (var tick = 0; tick < 310 && provider.Observations.Count == 0; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(world);
            Assert.True((await replay.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(replay);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var observation = Assert.Single(provider.Observations, item => item.Self?.LifeStage == (age == 3 ? "Child" : "Adult"));
        observation.Validate();
        foreach (var subject in (observation.RetrievedMemories ?? []).Select(item => item.SubjectId)
                     .Concat((observation.MemoryCompactionCandidates ?? []).Select(item => item.SubjectId)))
        {
            Assert.InRange(subject.Length, 1, 128);
            Assert.StartsWith("agent-sha256:", subject, StringComparison.Ordinal);
        }
        var recalledBelief = Assert.Single(observation.RetrievedMemories!, item => item.Id == belief.Id);
        Assert.Equal(recalledBelief.SubjectId, recalledBelief.SourceAgentId);
        Assert.StartsWith("agent-sha256:", recalledBelief.SourceAgentId, StringComparison.Ordinal);
        Assert.Equal(("firsthand", 8_000, belief.SourceEventId),
            (recalledBelief.Provenance,
                recalledBelief.ConfidenceBasisPoints, recalledBelief.SourceEventId));
        Assert.True(observation.RequiresPersonalProvider);
        if (age == 15)
        {
            Assert.False(observation.NeedsName);
            Assert.False(observation.IsNameRetry);
            Assert.False(observation.NeedsPersonality);
            Assert.False(observation.NeedsAspiration);
        }
        var sources = observation.MemoryCompactionCandidates ?? [];
        if (age == 15 && helperOn)
        {
            Assert.All(memories, memory => Assert.Contains(sources, item => item.Id == memory.Id));
            Assert.All(sources, item => Assert.Equal(childId, item.OwnerId));
            var candidate = Assert.Single(sources, item => item.Id == belief.Id);
            Assert.Equal(recalledBelief.SourceAgentId, candidate.SourceAgentId);
            Assert.Equal((recalledBelief.SubjectId, "firsthand", 8_000, belief.SourceEventId),
                (candidate.SubjectId, candidate.Provenance, candidate.ConfidenceBasisPoints, candidate.SourceEventId));
            // Complete and admit the actual pending helper response.
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(world);
            Assert.True((await replay.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(replay);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var index = Assert.Single(world.Society.MemoryCompactions!, item => item.OwnerId == childId);
            Assert.All(memories, memory => Assert.Contains(index.Sources, item => item.SourceId == memory.Id && item.ImportanceBasisPoints == 9_000));
            Assert.Contains(index.Sources, item => item.SourceId == belief.Id && item.Kind == SocietyMemorySourceKind.Belief && item.ImportanceBasisPoints == 9_000);
        }
        else
        {
            Assert.Empty(sources);
            Assert.DoesNotContain(world.Society.MemoryCompactions ?? [], item => item.OwnerId == childId);
            Assert.Contains(observation.RetrievedMemories!, item => memories.Any(memory => memory.Id == item.Id));
        }
        Assert.Equal(belief, Assert.Single(world.Society.Beliefs!, item => item.Id == belief.Id));
        Assert.DoesNotContain(sources, item => item.Id == foreign.Id);
        Assert.All(memories.Append(foreign), memory => Assert.Contains(world.Society.Memories, item => item == memory));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeReporterBeliefAllowsPersonalDecisionAfterExactReload(bool nativeReporter)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Born.Value);
        var birth = Assert.Single(state.Society.Society.Births);
        Assert.True(birth.ChildId.Length > 128);
        var actor = birth.PrimaryCaregiverId;
        var reporter = nativeReporter ? birth.ChildId : state.Society.Society.Inhabitants
            .First(person => person.Id != actor && person.Id.Length <= 128).Id;
        var belief = new SocietyAgentBelief("reporter-belief", actor, "The family remembers a promise.",
            SocietyBeliefProvenance.Hearsay, 6_500, state.Society.Society.WorldTick,
            SourceAgentId: reporter, AboutInhabitantId: reporter,
            SourceEventId: Assert.Single(state.Events, item => item.Kind == "child_born").EventId);
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = SocietyFixture.RecordAgentBelief(state.Society.Society, belief) },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
                Project = null,
                Survival = person.Survival! with { WarmthBasisPoints = 10_000, IllnessBasisPoints = 0 },
            }).ToArray(),
        };
        var saved = PrivateWorldRuntimeCodec.Encode(state);
        var provider = new RecordingPersonal();
        var replayProvider = new RecordingPersonal();
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => replayProvider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        world.Resume();
        replay.Resume();
        for (var tick = 0; tick < 4 && !provider.Observations.Any(item => item.InhabitantId == actor); tick++)
        {
            var step = await world.AdvanceOneTickAsync();
            Assert.True(step.Advanced);
            Assert.All(step.Decisions.Where(item => item.InhabitantId == actor), item => Assert.False(item.Admission.FellBack));
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var observation = Assert.Single(provider.Observations, item => item.InhabitantId == actor);
        observation.Validate();
        var recalled = Assert.Single(observation.RetrievedMemories!, item => item.Id == belief.Id);
        Assert.Equal(recalled.SubjectId, recalled.SourceAgentId);
        if (nativeReporter)
        {
            Assert.StartsWith("agent-sha256:", recalled.SourceAgentId, StringComparison.Ordinal);
            Assert.InRange(recalled.SourceAgentId!.Length, 1, 128);
        }
        else Assert.Equal(reporter, recalled.SourceAgentId);
        Assert.Equal((actor, "hearsay", 6_500, belief.SourceEventId),
            (recalled.OwnerId, recalled.Provenance, recalled.ConfidenceBasisPoints, recalled.SourceEventId));
        Assert.Equal(belief, Assert.Single(world.Society.Beliefs!, item => item.Id == belief.Id));
        var result = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(result));
        Assert.Equal(result, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private sealed class RecordingPersonal : IDecisionProvider
    {
        public List<InhabitantObservation> Observations { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Observations.Add(request.Observation);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1, new Dictionary<string, double> { ["safe_idle"] = 1 }));
        }
    }

    private static async Task WaitForRequests(PrivateWorldRuntime world)
    {
        var field = typeof(PrivateWorldRuntime).GetField("pendingHosted", BindingFlags.Instance | BindingFlags.NonPublic);
        var pending = Assert.IsAssignableFrom<System.Collections.IDictionary>(field!.GetValue(world));
        var tasks = pending.Values.Cast<object>().Select(item => Assert.IsAssignableFrom<Task>(item.GetType().GetProperty("Task")!.GetValue(item))).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task<byte[]> CreateBornAsync()
    {
        var prepared = await CookedBirthFixture.PrepareAsync(inPot: true);
        using var world = CookedBirthFixture.Restore(prepared);
        for (var tick = 0; tick < 650 && world.Society.Births.Count == 0; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var birth = Assert.Single(world.Society.Births);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "child_born" && item.Detail == birth.ChildId);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private sealed class QuietProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")] } }, cancellationToken);
    }

    private sealed class InitialIdentityProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public DecisionProviderKind KindFor(InhabitantObservation observation) =>
            observation.IdentityMoment is null &&
            (observation.NeedsName || observation.IsNameRetry || observation.NeedsPersonality || observation.NeedsAspiration)
                ? DecisionProviderKind.LargeLanguageModel : DecisionProviderKind.Deterministic;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var probabilities = request.Observation.Candidates.ToDictionary(item => item.Id,
                item => item.Id == "safe_idle" ? 1d : 0d, StringComparer.Ordinal);
            var name = request.Observation.NeedsName
                ? "Zuri " + Assert.Single(request.Observation.Self!.AllowedChildSurnames!) : null;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                KindFor(request.Observation), ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1, probabilities, ChosenName: name,
                ChosenPersonality: "Inventive and independent", ChosenAspiration: "Study the hills"));
        }
    }

    private sealed class RecordingHelper : IDecisionProvider
    {
        public List<InhabitantObservation> Observations { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Jev;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Observations.Add(request.Observation);
            var probabilities = request.Observation.Candidates.ToDictionary(item => item.Id,
                item => item.Id == "safe_idle" ? 1d : 0d, StringComparer.Ordinal);
            var scores = request.Observation.MemoryCompactionCandidates?.Select(item =>
                new CognitionMemoryCompactionScore(item.Id, item.OwnerId, item.Kind, item.SourceTick, 9_000, 8_000)).ToArray();
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1, probabilities, MemoryCompactionScores: scores));
        }
    }
}
