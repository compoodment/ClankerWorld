using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateMemorySummaryTests
{
    [Theory]
    [InlineData(DecisionProviderKind.Jev)]
    [InlineData(DecisionProviderKind.OpenAiDecisions)]
    public async Task AcceptedHelperSummarizesOnlyOwnArchiveAndRecallPreservesEvidenceAcrossReplay(DecisionProviderKind kind)
    {
        var state = await ArchivedState();
        var owner = state.Inhabitants[0].InhabitantId;
        var other = state.Inhabitants[1].InhabitantId;
        var observed = new List<InhabitantObservation>();
        using var world = PrivateWorldRuntime.Restore(state with { JevEnabled = true, RoutineHelper = new("jev", "jev-1.13.0") },
            id => id == owner ? new SummaryProvider(kind, observed) : new SummaryProvider(DecisionProviderKind.Deterministic, observed));
        for (var tick = 0; tick < 8 && world.Society.MemorySummaries.Count == 0; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var summary = Assert.Single(world.Society.MemorySummaries);
        Assert.Equal(owner, summary.OwnerId);
        Assert.Equal(5, summary.Sources.Count);
        Assert.DoesNotContain(summary.Sources, source => source.Id == "foreign");
        Assert.Contains("Hearsay:", summary.Text, StringComparison.Ordinal);
        Assert.True(summary.Text.Length < state.Society.Society.ArchivedMemories.Where(item => item.Memory.OwnerId == owner)
            .Sum(item => item.Memory.Summary.Length) + state.Society.Society.ArchivedBeliefs.Sum(item => item.Belief.Statement.Length));
        Assert.Equal(state.Society.Society.ArchivedMemories, world.Society.ArchivedMemories);
        Assert.Equal(state.Society.Society.ArchivedBeliefs, world.Society.ArchivedBeliefs);
        Assert.All(observed.Where(item => item.InhabitantId == other), observation => Assert.Empty(observation.MemorySummaryOptions ?? []));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Detail.Contains("secret", StringComparison.Ordinal));
        Assert.Contains(world.Society.Memories, memory => memory.Id == "permanent");

        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var loadedState = PrivateWorldRuntimeCodec.Decode(bytes);
        using var loaded = PrivateWorldRuntime.Restore(loadedState);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        var recall = new List<InhabitantObservation>();
        using var replay = PrivateWorldRuntime.Restore(loadedState, _ => new SummaryProvider(DecisionProviderKind.Deterministic, recall));
        using var paired = PrivateWorldRuntime.Restore(loadedState, _ => new SummaryProvider(DecisionProviderKind.Deterministic));
        replay.Pause(); paired.Pause();
        replay.SetJevEnabled(false); paired.SetJevEnabled(false);
        replay.Resume(); paired.Resume();
        replay.SubmitInstruction(new("summary-recall", "owner:test", owner, OwnerInstructionKind.Suggestive, "Remember the familiar path."));
        paired.SubmitInstruction(new("summary-recall", "owner:test", owner, OwnerInstructionKind.Suggestive, "Remember the familiar path."));
        for (var tick = 0; tick < 8 && !recall.Any(item => item.InhabitantId == owner); tick++)
        {
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.True((await paired.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(replay.ExportState()), PrivateWorldRuntimeCodec.Encode(paired.ExportState()));
        }
        var memory = Assert.Single(recall.First(item => item.InhabitantId == owner).RetrievedMemories!, item => item.Kind == "summary");
        var rumor = Assert.Single(memory.SummarySources!, source => source.Kind == "belief");
        Assert.Equal(("hearsay", 4_200, other), (rumor.Provenance, rumor.ConfidenceBasisPoints, rumor.SourceAgentId));
        Assert.Equal(summary.Text, memory.Summary);
        Assert.Single(replay.Society.MemorySummaries);
        var correction = replay.CorrectAgentBelief(owner, rumor.Id,
            new("corrected", owner, "I saw the path was dry.", SocietyBeliefProvenance.Firsthand, 9_000, replay.WorldTick));
        Assert.Equal(rumor.Id, correction.SupersedesBeliefId);
        var correctedBytes = PrivateWorldRuntimeCodec.Encode(replay.ExportState());
        using var corrected = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(correctedBytes));
        Assert.Equal(correctedBytes, PrivateWorldRuntimeCodec.Encode(corrected.ExportState()));
        recall.Clear();
        replay.SubmitInstruction(new("summary-correction-recall", "owner:test", owner, OwnerInstructionKind.Suggestive, "Remember the path and its correction."));
        for (var tick = 0; tick < 8 && !recall.Any(item => item.InhabitantId == owner); tick++)
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        var correctedMemory = Assert.Single(recall.First(item => item.InhabitantId == owner).RetrievedMemories!, item => item.Kind == "summary");
        Assert.True(Assert.Single(correctedMemory.SummarySources!, source => source.Id == rumor.Id).IsCorrected);
        Assert.Equal(summary.Text, correctedMemory.Summary);
    }

    [Fact]
    public async Task HelperOffDiscardsHeldSummaryAndRefusedTickPreservesCheckpoint()
    {
        var state = await ArchivedState();
        var owner = state.Inhabitants[0].InhabitantId;
        var held = new HeldSummaryProvider();
        using var world = PrivateWorldRuntime.Restore(state with { JevEnabled = true, RoutineHelper = new("jev", "jev-1.13.0") },
            id => id == owner ? held : new SummaryProvider(DecisionProviderKind.Deterministic));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickNonBlockingAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        var observation = await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotEmpty(observation.MemorySummaryOptions!);
        world.Pause();
        Assert.True(world.SetRoutineHelper(RoutineHelperSettings.Off));
        var switched = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        held.Release.SetResult(true);
        await held.Returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(switched, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Empty(world.Society.MemorySummaries);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("failure")]
    [InlineData("decline")]
    [InlineData("unrequested")]
    public async Task OptionalOrUnusableHelperCannotWriteSummaryOrChangeTheArchive(string mode)
    {
        var state = await ArchivedState();
        var owner = state.Inhabitants[0].InhabitantId;
        var observed = new List<InhabitantObservation>();
        using var world = PrivateWorldRuntime.Restore(state with { JevEnabled = mode != "off", RoutineHelper = mode == "off" ? RoutineHelperSettings.Off : new("jev", "jev-1.13.0") },
            id => new SummaryProvider(mode == "off" || id != owner ? DecisionProviderKind.Deterministic : DecisionProviderKind.Jev, observed, mode));
        for (var tick = 0; tick < 5; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(world.Society.MemorySummaries);
        Assert.Equal(state.Society.Society.ArchivedMemories, world.Society.ArchivedMemories);
        Assert.Equal(state.Society.Society.ArchivedBeliefs, world.Society.ArchivedBeliefs);
        if (mode == "off") Assert.All(observed, item => Assert.Null(item.MemorySummaryOptions));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("text")]
    [InlineData("tick")]
    [InlineData("duplicate")]
    [InlineData("source")]
    public async Task StrictLoadingRejectsAlteredSummarySourcesWithoutChangingOriginalBytes(string fault)
    {
        var state = await ArchivedState();
        var owner = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state with { JevEnabled = true, RoutineHelper = new("jev", "jev-1.13.0") },
            id => new SummaryProvider(id == owner ? DecisionProviderKind.Jev : DecisionProviderKind.Deterministic));
        for (var tick = 0; tick < 8 && world.Society.MemorySummaries.Count == 0; tick++) await world.AdvanceOneTickAsync();
        Assert.Single(world.Society.MemorySummaries);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var json = JsonNode.Parse(bytes)!;
        var society = json["state"]!["society"]!["society"]!;
        var summary = society["memorySummaries"]![0]!;
        switch (fault)
        {
            case "missing": society.AsObject().Remove("memorySummaries"); break;
            case "foreign": summary["ownerId"] = state.Inhabitants[1].InhabitantId; break;
            case "text": summary["text"] = "A made-up authoritative fact."; break;
            case "tick": summary["createdTick"] = world.WorldTick + 1; break;
            case "duplicate": society["memorySummaries"]!.AsArray().Add(summary.DeepClone()); break;
            case "source": summary["sources"]![0]!["id"] = "foreign"; break;
        }
        Assert.ThrowsAny<Exception>(() => PrivateWorldRuntimeCodec.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static async Task<PrivateWorldRuntimeState> ArchivedState()
    {
        using var seed = new PrivateWorldRuntime("summary-private");
        var state = seed.ExportState();
        var owner = state.Inhabitants[0].InhabitantId;
        var other = state.Inhabitants[1].InhabitantId;
        var config = state.Society.Society.Config with { TicksPerWorldDay = 2, BaseNaturalMortalityBasisPoints = 0 };
        var society = state.Society.Society with
        {
            Config = config,
            WorldTick = 5,
            Inventory = state.Society.Society.Inventory with { WorldTick = 5 },
            Inhabitants = state.Society.Society.Inhabitants.Select(item => item with
            {
                BirthTick = item.BirthTick * 2 / state.Society.Society.Config.TicksPerWorldDay,
                BirthLifeTick = item.BirthLifeTick * 2 / state.Society.Society.Config.TicksPerWorldDay,
            }).ToArray(),
            Memories = Enumerable.Range(0, 4).Select(index => new SocietySocialMemory("old-" + index, owner, other,
                "I walked along a familiar path and remembered the weather that afternoon " + index, "private", 0))
                .Append(new("foreign", other, owner, "Another person's secret supply location.", "private", 0))
                .Append(new("permanent", owner, other, "My learned skill remains dependable.", "private", 0) { Kind = SocietyMemoryKind.Skill })
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Beliefs = [new("rumor", owner, "Someone told me the familiar path was muddy; I have not seen it myself.",
                SocietyBeliefProvenance.Hearsay, 4_200, 0, SourceAgentId: other)],
        };
        var systems = WorldSystemsRules.CreateGenesis(state.WorldSeed, state.WorldSystems!.Config with { TicksPerDay = 2, CalendarOffsetTicks = 0 },
            state.WorldSystems.Ecology.Resources, state.WorldSystems.Factions, state.WorldSystems.Currency, state.WorldSystems.Culture, state.WorldSystems.Chunks);
        for (var tick = 0; tick < 5; tick++) systems = WorldSystemsRules.AdvanceOneTick(systems);
        using var world = PrivateWorldRuntime.Restore(state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = society },
            WorldSystems = systems,
            Inhabitants = state.Inhabitants.Select(item => item with { LastDecisionContext = null, HungerBasisPoints = 10_000 }).ToArray(),
        }, _ => new SummaryProvider(DecisionProviderKind.Deterministic));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(5, world.Society.ArchivedMemories.Count);
        return world.ExportState() with
        {
            Inhabitants = world.ExportState().Inhabitants.Select(item => item with { LastDecisionContext = null }).ToArray(),
        };
    }

    private sealed class HeldSummaryProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Jev;
        public long ProviderEpoch => 1;
        public TaskCompletionSource<InhabitantObservation> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(request.Observation);
            await Release.Task;
            Returned.TrySetResult(true);
            return new(request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, "safe_idle", 1,
                new Dictionary<string, double> { ["safe_idle"] = 1 }, MemorySummaryChoice: request.Observation.MemorySummaryOptions![0].Choice);
        }
    }

    private sealed class SummaryProvider(DecisionProviderKind kind, List<InhabitantObservation>? observations = null, string mode = "accepted") : IDecisionProvider
    {
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            observations?.Add(request.Observation);
            if (mode == "failure" && kind == DecisionProviderKind.Jev) throw new InvalidOperationException("Helper unavailable.");
            const string selected = "safe_idle";
            var choice = mode == "unrequested" ? "unknown" : mode == "accepted" ? request.Observation.MemorySummaryOptions is { Count: > 0 } options ? options[0].Choice : null : null;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected, 1,
                new Dictionary<string, double> { [selected] = 1 }, MemorySummaryChoice: choice));
        }
    }
}
