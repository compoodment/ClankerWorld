using System.Reflection;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class GrownAgentHelperMemoryTests
{
    private static readonly Lazy<Task<byte[]>> Born = new(CreateBornAsync);

    [Theory]
    [InlineData(3, true)]
    [InlineData(15, true)]
    [InlineData(15, false)]
    public async Task NativeBornResidentOnlyOffersOwnUnassessedMemoriesAfterAdulthoodWithHelperOn(int age, bool helperOn)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Born.Value);
        var childId = Assert.Single(state.Society.Society.Births).ChildId;
        using (var aging = PrivateWorldRuntime.Restore(state, _ => new QuietProvider()))
        {
            aging.Pause();
            aging.SetLifePace(365);
            aging.Resume();
            while (aging.Society.AgeAt(aging.Society.GetInhabitant(childId), aging.WorldTick + 1) < age)
                Assert.True((await aging.AdvanceOneTickAsync()).Advanced);
            state = aging.ExportState();
        }
        var sourceTick = state.Society.Society.WorldTick;
        var memories = Enumerable.Range(0, 4).Select(index => new SocietySocialMemory(
            $"grown-memory-{index}", childId, childId, $"A private remembered promise {index}.", "private", sourceTick)).ToArray();
        var foreign = new SocietySocialMemory("foreign-memory", state.Society.Society.Births[0].PrimaryCaregiverId, childId,
            "Another person's private promise.", "private", sourceTick);
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with { Memories = state.Society.Society.Memories.Concat(memories).Append(foreign).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray() },
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
        for (var tick = 0; tick < 8 && provider.Observations.Count == 0; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(world);
            Assert.True((await replay.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(replay);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var observation = Assert.Single(provider.Observations, item => item.Self?.LifeStage == (age == 3 ? "Child" : "Adult"));
        Assert.True(observation.RequiresPersonalProvider);
        var sources = observation.MemoryCompactionCandidates ?? [];
        if (age == 15 && helperOn)
        {
            Assert.All(memories, memory => Assert.Contains(sources, item => item.Id == memory.Id));
            Assert.All(sources, item => Assert.Equal(childId, item.OwnerId));
            // Complete and admit the actual pending helper response.
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(world);
            Assert.True((await replay.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(replay);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var index = Assert.Single(world.Society.MemoryCompactions!, item => item.OwnerId == childId);
            Assert.All(memories, memory => Assert.Contains(index.Sources, item => item.SourceId == memory.Id && item.ImportanceBasisPoints == 9_000));
        }
        else
        {
            Assert.Empty(sources);
            Assert.DoesNotContain(world.Society.MemoryCompactions ?? [], item => item.OwnerId == childId);
            Assert.Contains(observation.RetrievedMemories!, item => memories.Any(memory => memory.Id == item.Id));
        }
        Assert.DoesNotContain(sources, item => item.Id == foreign.Id);
        Assert.All(memories.Append(foreign), memory => Assert.Contains(world.Society.Memories, item => item == memory));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
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
