using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class MemoryUnicodeBoundaryTests
{
    private static readonly Lazy<Task<byte[]>> Baseline = new(CreateBaseline);

    [Theory]
    [InlineData(158, 160, false)]
    [InlineData(158, 160, true)]
    [InlineData(159, 159, false)]
    [InlineData(159, 159, true)]
    [InlineData(160, 160, false)]
    [InlineData(160, 160, true)]
    public async Task NativeRecallAndSalienceKeepWholeCharactersAndSavedSources(int prefixLength, int expectedLength, bool helper)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Baseline.Value);
        var owner = state.Society.Society.Inhabitants[0].Id;
        var subject = state.Society.Society.Inhabitants[1].Id;
        var text = new string('a', prefixLength) + "😀tail";
        var memory = new SocietySocialMemory("unicode-experience", owner, subject, text, "private", state.Society.Society.WorldTick);
        var society = SocietyFixture.RecordSocialMemory(state.Society.Society, memory).Checkpoint;
        var belief = new SocietyAgentBelief("unicode-belief", owner, text, SocietyBeliefProvenance.Hearsay,
            4_200, state.Society.Society.WorldTick, SourceAgentId: subject, AboutInhabitantId: subject);
        society = SocietyFixture.RecordAgentBelief(society, belief);
        for (var index = 0; index < 2; index++)
            society = SocietyFixture.RecordSocialMemory(society, new("unicode-padding-" + index, owner, subject,
                "A quiet day.", "private", state.Society.Society.WorldTick)).Checkpoint;
        state = state with
        {
            JevEnabled = helper,
            RoutineHelper = helper ? RoutineHelperSettings.Jev : RoutineHelperSettings.Off,
            Society = state.Society with { Society = society },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                Survival = person.Survival is { } survival ? survival with
                { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000, IllnessBasisPoints = 0 } : null,
                LastDecisionContext = null,
                Project = null,
                TravelCooldownTicks = 0,
            }).ToArray(),
        };
        var observed = new ConcurrentQueue<InhabitantObservation>();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new CapturingIdleProvider(observed, helper));
        for (var tick = 0; tick < 4 && !observed.Any(item => item.InhabitantId == owner); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var observation = observed.First(item => item.InhabitantId == owner);
        foreach (var id in new[] { memory.Id, belief.Id })
        {
            var recalled = Assert.Single(observation.RetrievedMemories!, item => item.Id == id);
            if (helper)
            {
                var salience = Assert.Single(observation.MemoryCompactionCandidates!, item => item.Id == id);
                Assert.Equal(recalled.Summary, salience.Summary);
            }
            else Assert.Null(observation.MemoryCompactionCandidates);
            Assert.Equal(expectedLength, recalled.Summary.Length);
            Assert.Equal(recalled.Summary, JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(recalled.Summary)));
            _ = new UTF8Encoding(false, true).GetBytes(recalled.Summary);
            Assert.Equal(owner, recalled.OwnerId);
            Assert.Equal(subject, recalled.SubjectId);
        }
        var recalledBelief = observation.RetrievedMemories!.Single(item => item.Id == belief.Id);
        Assert.Equal("hearsay", recalledBelief.Provenance);
        Assert.Equal(4_200, recalledBelief.ConfidenceBasisPoints);
        Assert.Equal(subject, recalledBelief.SourceAgentId);
        Assert.Equal(memory, world.Society.Memories.Single(item => item.Id == memory.Id));
        Assert.Equal(belief, world.Society.Beliefs!.Single(item => item.Id == belief.Id));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(memory, restored.Society.Memories.Single(item => item.Id == memory.Id));
        Assert.Equal(belief, restored.Society.Beliefs!.Single(item => item.Id == belief.Id));
    }

    private static async Task<byte[]> CreateBaseline()
    {
        using var world = NormalPathWorld.CreateGenerated("memory-unicode-audit-198c2830", _ => new CapturingIdleProvider(new()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private sealed class CapturingIdleProvider(ConcurrentQueue<InhabitantObservation> observed, bool helper = false) : IDecisionProvider
    {
        public DecisionProviderKind Kind => helper ? DecisionProviderKind.Jev : DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            observed.Enqueue(request.Observation);
            var selected = request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle").Id;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                selected, 1, new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}
