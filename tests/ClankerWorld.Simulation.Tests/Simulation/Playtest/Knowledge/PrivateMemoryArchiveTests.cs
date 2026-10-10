using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateMemoryArchiveTests
{
    [Fact]
    public async Task NativeRenameRemainsLiveAcrossTheDailyArchiveBoundaryAndReload()
    {
        using var renamed = Restore(PrivateWorldRuntimeCodec.Encode(NearBoundary(generated: true)));
        var owner = renamed.Society.Inhabitants[0].Id;
        Assert.True(renamed.RenameAgent(owner, "Archiveproof Vale"));
        var original = Assert.Single(renamed.Society.Memories, item => item.Id.StartsWith("player-rename:", StringComparison.Ordinal));
        Assert.True(original.Permanent);
        Assert.Equal((owner, owner, 5L, SocietyMemoryKind.Experience), (original.OwnerId, original.SubjectId, original.SourceTick, original.Kind));
        var ordinary = new SocietySocialMemory("ordinary-rename-age-control", owner, owner, "An ordinary private experience.", "private", original.SourceTick);
        var state = renamed.ExportState();
        state = WithSources(state, state.Society.Society.Memories.Append(ordinary).ToArray(), state.Society.Society.Beliefs ?? []);
        using var world = Restore(PrivateWorldRuntimeCodec.Encode(state));
        for (var tick = 0; tick < 6; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(11, world.WorldTick);
        Assert.Contains(ordinary, world.Society.Memories);
        Assert.Contains(original, world.Society.Memories);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = Restore(bytes);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(12, world.WorldTick);
        Assert.Contains(world.Society.ArchivedMemories, item => item.Memory == ordinary && item.ArchivedTick == 12);
        Assert.DoesNotContain(world.Society.Memories, item => item.Id == ordinary.Id);
        Assert.Contains(original, world.Society.Memories);
        Assert.DoesNotContain(world.Society.ArchivedMemories, item => item.Memory.Id == original.Id);
        var after = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using var loaded = Restore(after);
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        Assert.Contains(original, loaded.Society.Memories);
        var observations = new List<InhabitantObservation>();
        using var recalling = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(after), _ => new IdleProvider(observations));
        recalling.SubmitInstruction(new("recall-rename", "owner:test", owner, OwnerInstructionKind.Suggestive, "Remember my accepted name."));
        for (var tick = 0; tick < 4 && observations.All(item => item.InhabitantId != owner); tick++)
            Assert.True((await recalling.AdvanceOneTickAsync()).Advanced);
        var observed = Assert.Single(observations.Where(item => item.InhabitantId == owner).Take(1));
        Assert.Contains(observed.RetrievedMemories ?? [], item => item.Id == original.Id && item.Summary == original.Summary);
        Assert.DoesNotContain(observed.RetrievedMemories ?? [], item => item.Id == ordinary.Id);
        Assert.All(observations.Where(item => item.InhabitantId != owner), item =>
            Assert.DoesNotContain(item.RetrievedMemories ?? [], memory => memory.Id == original.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StrictLoadingRefusesAnArchivedPlayerRenameWithEitherPermanentMarker(bool permanent)
    {
        using var world = Restore(PrivateWorldRuntimeCodec.Encode(NearBoundary(generated: true)));
        Assert.True(world.RenameAgent(world.Society.Inhabitants[0].Id, "Archiveproof Vale"));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var json = JsonNode.Parse(bytes)!;
        var society = json["state"]!["society"]!["society"]!;
        var live = society["memories"]!.AsArray();
        var memory = live.Single(item => item!["id"]!.GetValue<string>().StartsWith("player-rename:", StringComparison.Ordinal))!;
        var archived = memory.DeepClone();
        Assert.True(live.Remove(memory));
        archived["permanent"] = permanent;
        society["archivedMemories"]!.AsArray().Add(new JsonObject { ["memory"] = archived, ["archivedTick"] = world.WorldTick });
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task DailyNativeRuleWorksWithoutHelpersAndPreservesProtectedRecordsAcrossReplay()
    {
        var state = NearBoundary(generated: true);
        var owner = state.Inhabitants[0].InhabitantId;
        var other = state.Inhabitants[1].InhabitantId;
        var memories = new List<SocietySocialMemory>
        {
            new("old-low", owner, other, "An old private conversation.", "private", 0),
            new("old-cutoff", owner, other, "A useful remembered path.", "private", 0),
            new("recent", owner, other, "A recent ordinary experience.", "private", 4),
            new("foreign", other, owner, "Someone else's hidden location.", "private", 0),
        };
        foreach (var kind in new[] { SocietyMemoryKind.LifeEvent, SocietyMemoryKind.Relationship, SocietyMemoryKind.Skill, SocietyMemoryKind.Commitment })
            memories.Add(new("protected-" + kind, owner, other, "Permanent " + kind, "private", 0) { Kind = kind });
        var rumor = new SocietyAgentBelief("old-rumor", owner, "Someone said the old path was muddy.",
            SocietyBeliefProvenance.Hearsay, 4_200, 0, SourceAgentId: other);
        var protectedBeliefs = memories.Where(item => item.Kind != SocietyMemoryKind.Experience).Select(item =>
            new SocietyAgentBelief("belief-" + item.Kind, owner, item.Summary, SocietyBeliefProvenance.Firsthand, 7_000, 0) { Kind = item.Kind }).ToArray();
        state = WithSources(state, memories, protectedBeliefs.Append(rumor).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(), [new(owner,
            [new("old-cutoff", SocietyMemorySourceKind.Experience, 0, 2_500, 8_000, 0)])]);
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = Restore(bytes);
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = Restore(bytes);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var saved = world.ExportState();
        Assert.Equal(new[] { memories[3].Id, memories[0].Id }, saved.Society.Society.ArchivedMemories.Select(item => item.Memory.Id));
        Assert.Equal(memories.Single(item => item.Id == "old-low"), saved.Society.Society.ArchivedMemories.Single(item => item.Memory.Id == "old-low").Memory);
        Assert.Equal(rumor, Assert.Single(saved.Society.Society.ArchivedBeliefs).Belief);
        Assert.All(saved.Society.Society.ArchivedMemories, item => Assert.Equal(6, item.ArchivedTick));
        Assert.Contains(saved.Society.Society.Memories, item => item.Id == "recent");
        Assert.Contains(saved.Society.Society.Memories, item => item.Id == "old-cutoff");
        Assert.All(memories.Where(item => item.Kind != SocietyMemoryKind.Experience), item => Assert.Contains(item, saved.Society.Society.Memories));
        Assert.Equal(JsonSerializer.Serialize(state.Society.Society.Relationships), JsonSerializer.Serialize(saved.Society.Society.Relationships));
        Assert.All(protectedBeliefs, item => Assert.Contains(item, saved.Society.Society.Beliefs!));
        Assert.Equal(state.Inhabitants.Select(item => item.Skills), saved.Inhabitants.Select(item => item.Skills));
        Assert.Contains(saved.Events, item => item.Kind == "agent_memories_archived" && item.Detail == "2:1");
        Assert.DoesNotContain(saved.Events, item => item.Detail.Contains(rumor.Statement, StringComparison.Ordinal));
        var after = PrivateWorldRuntimeCodec.Encode(saved);
        using var loaded = Restore(after);
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        var observations = new List<InhabitantObservation>();
        using var recalling = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(after), _ => new IdleProvider(observations));
        recalling.SubmitInstruction(new("recall-now", "owner:test", owner, OwnerInstructionKind.Suggestive, "Remember the path."));
        for (var tick = 0; tick < 4 && observations.All(item => item.InhabitantId != owner); tick++)
            Assert.True((await recalling.AdvanceOneTickAsync()).Advanced);
        var observed = Assert.Single(observations.Where(item => item.InhabitantId == owner).Take(1));
        Assert.DoesNotContain(observed.RetrievedMemories ?? [], item => item.Id is "old-low" or "old-rumor" or "foreign");
        Assert.Contains(observed.RetrievedMemories ?? [], item => item.Id == "old-cutoff");
        Assert.Equal(2, recalling.Society.ArchivedMemories.Count);
        Assert.Single(recalling.Society.ArchivedBeliefs);
    }

    [Fact]
    public async Task AnOpenOwnerOrderPostponesArchivingWithoutResettingTheSavedAge()
    {
        var state = NearBoundary();
        var owner = state.Inhabitants[0].InhabitantId;
        state = WithSources(state, [new("open-task-memory", owner, state.Inhabitants[1].InhabitantId, "An unfinished task.", "private", 0)], []);
        using var world = Restore(PrivateWorldRuntimeCodec.Encode(state));
        var receipt = world.SubmitInstruction(new("blocked-task", "owner:test", owner, OwnerInstructionKind.MustDo, "keep gathering wood"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.Society.Memories, item => item.Id == "open-task-memory");
        Assert.Empty(world.Society.ArchivedMemories);
        var open = world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId);
        Assert.True(open.Order!.Status is "queued" or "waiting" or "doing" or "interrupted" or "blocked", open.Order.Status);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = Restore(bytes);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        reloaded.CancelOrder(new("cancel-blocked-task", "owner:test", reloaded.Society.WorldId, owner, open.InstructionId));
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        var archived = Assert.Single(reloaded.Society.ArchivedMemories);
        Assert.Equal((0L, 8L), (archived.Memory.SourceTick, archived.ArchivedTick));
    }

    [Fact]
    public async Task AnArchivedHearsaySourceCanBeCorrectedWithoutLosingItsOriginalEvidence()
    {
        var state = NearBoundary();
        var owner = state.Inhabitants[0].InhabitantId;
        var other = state.Inhabitants[1].InhabitantId;
        var rumor = new SocietyAgentBelief("old-rumor", owner, "Mira said the tool was missing.", SocietyBeliefProvenance.Hearsay, 4_200, 0, SourceAgentId: other);
        state = WithSources(state, [], [rumor]);
        using var world = Restore(PrivateWorldRuntimeCodec.Encode(state));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var corrected = world.CorrectAgentBelief(owner, rumor.Id,
            new("seen-tool", owner, "I saw the tool beside the House.", SocietyBeliefProvenance.Firsthand, 9_600, world.WorldTick));
        var source = Assert.Single(world.Society.ArchivedBeliefs).Belief;
        Assert.Equal((rumor.Statement, rumor.SourceAgentId, rumor.ConfidenceBasisPoints, corrected.Id),
            (source.Statement, source.SourceAgentId, source.ConfidenceBasisPoints, source.SupersededByBeliefId));
        Assert.Equal(rumor.Id, corrected.SupersedesBeliefId);
        Assert.Equal(corrected, Assert.Single(world.Society.Beliefs!));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Throws<InvalidOperationException>(() => world.RecordAgentBelief(rumor));
        Assert.Throws<InvalidOperationException>(() => world.CorrectAgentBelief(other, corrected.Id,
            new("stolen-correction", other, "My correction.", SocietyBeliefProvenance.Inference, 5_000, world.WorldTick)));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var loaded = Restore(bytes);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        for (var tick = 0; tick < 6; tick++) Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(loaded.Society.Beliefs!);
        Assert.Equal(2, loaded.Society.ArchivedBeliefs.Count);
        var coldBytes = PrivateWorldRuntimeCodec.Encode(loaded.ExportState());
        using var coldReload = Restore(coldBytes);
        Assert.Equal(coldBytes, PrivateWorldRuntimeCodec.Encode(coldReload.ExportState()));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("protected")]
    [InlineData("owner")]
    [InlineData("future")]
    [InlineData("kind")]
    public async Task StrictLoadingRefusesMalformedArchivesAndPreservesTheOriginalCheckpoint(string fault)
    {
        var state = NearBoundary();
        var owner = state.Inhabitants[0].InhabitantId;
        state = WithSources(state, [new("old-low", owner, state.Inhabitants[1].InhabitantId, "Private memory.", "private", 0)], []);
        using var world = Restore(PrivateWorldRuntimeCodec.Encode(state));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var json = JsonNode.Parse(bytes)!;
        var society = json["state"]!["society"]!["society"]!;
        var memory = society["archivedMemories"]![0]!["memory"]!;
        switch (fault)
        {
            case "missing": society.AsObject().Remove("archivedBeliefs"); break;
            case "duplicate": society["memories"]!.AsArray().Add(memory.DeepClone()); break;
            case "protected": memory["kind"] = (int)SocietyMemoryKind.LifeEvent; break;
            case "owner": memory["ownerId"] = "unknown-owner"; break;
            case "future": society["archivedMemories"]![0]!["archivedTick"] = 7; break;
            case "kind": memory.AsObject().Remove("kind"); break;
        }
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static PrivateWorldRuntimeState WithSources(PrivateWorldRuntimeState state,
        IReadOnlyList<SocietySocialMemory> memories, IReadOnlyList<SocietyAgentBelief> beliefs,
        IReadOnlyList<SocietyAgentMemoryCompaction>? compactions = null) => state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                { Memories = memories.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(), Beliefs = beliefs, MemoryCompactions = compactions }
            },
        };

    private static PrivateWorldRuntimeState NearBoundary(bool generated = false)
    {
        using var seed = generated ? NormalPathWorld.CreateGenerated("memory-archive-native", _ => new IdleProvider()) :
            new PrivateWorldRuntime("memory-archive-native", _ => new IdleProvider());
        var state = seed.ExportState();
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
        };
        var systems = WorldSystemsRules.CreateGenesis(state.WorldSeed, state.WorldSystems!.Config with { TicksPerDay = 2, CalendarOffsetTicks = 0 },
            state.WorldSystems.Ecology.Resources, state.WorldSystems.Factions, state.WorldSystems.Currency, state.WorldSystems.Culture, state.WorldSystems.Chunks);
        for (var tick = 0; tick < 5; tick++) systems = WorldSystemsRules.AdvanceOneTick(systems);
        return state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = society },
            WorldSystems = systems,
            Inhabitants = state.Inhabitants.Select(item => item with { LastDecisionContext = null, HungerBasisPoints = 10_000 }).ToArray(),
        };
    }

    private static PrivateWorldRuntime Restore(byte[] bytes) => PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new IdleProvider());

    private sealed class IdleProvider(List<InhabitantObservation>? observations = null) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            observations?.Add(request.Observation);
            const string selected = "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected, 1,
                new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}
