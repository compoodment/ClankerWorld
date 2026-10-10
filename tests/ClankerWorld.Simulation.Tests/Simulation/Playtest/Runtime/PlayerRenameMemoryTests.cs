using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PlayerRenameTests
{
    [Fact]
    public async Task PermanentRenameHistorySurvivesCrowdingAndZeroImportanceAndReachesOnlyItsOwnModel()
    {
        using var initial = NewWorld();
        var names = new[] { "Rowan Vale", "Rowan Lake", "Aster Pine", "Mira Moss", "Lark Birch", "Sage Harbor" };
        foreach (var name in names) Assert.True(initial.RenameAgent(First, name));
        var state = initial.ExportState();
        var history = state.Society.Society.Memories.Where(item => item.OwnerId == First).ToArray();
        Assert.Equal(names.Length, history.Length);
        Assert.Equal(names.Length, history.Select(item => item.Id).Distinct().Count());
        var ordinary = Enumerable.Range(0, 300).Select(index => new SocietySocialMemory(
            $"a-new-experience-{index:D3}", First, Second, "Gather food and build shelter near camp.", "private", 0)).ToArray();
        // A saved zero assessment cannot make the permanent rename fade; recent ordinary sources can be highly ranked.
        var assessments = ordinary.Take(255).Select(item => new SocietyAgentMemoryImportance(
            item.Id, SocietyMemorySourceKind.Experience, 0, 10_000, 10_000, 0)).Append(new(
                history[^1].Id, SocietyMemorySourceKind.Experience, 0, 0, 0, 0)).OrderBy(item => item.SourceId, StringComparer.Ordinal).ToArray();
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Memories = history.Concat(ordinary).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                    MemoryCompactions = [new(First, assessments)],
                },
            },
        };
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        var observed = new List<InhabitantObservation>();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new RenameMemoryProvider(observed));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new RenameMemoryProvider([]));
        var visible = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(item => item.Id == First).RecentMemories;
        Assert.Equal(16, visible.Count);
        Assert.Equal(names.Length, visible.Count(item => item.Permanent));
        Assert.Contains(visible, item => item.Summary == "I was renamed on day 1 to Rowan Vale." && item.Permanent);
        Assert.Equal("I was renamed on day 1 to Sage Harbor.", visible[0].Summary);
        world.Resume();
        replay.Resume();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var own = Assert.Single(observed, item => item.InhabitantId == First);
        Assert.Equal("Sage Harbor", own.Self!.Name);
        Assert.Equal(4, own.RetrievedMemories!.Count);
        Assert.Equal("I was renamed on day 1 to Sage Harbor.", own.RetrievedMemories[0].Summary);
        Assert.All(own.RetrievedMemories, item => Assert.Equal(First, item.OwnerId));
        Assert.All(observed.Where(item => item.InhabitantId != First), item =>
            Assert.DoesNotContain(item.RetrievedMemories ?? [], memory => memory.Id.StartsWith("player-rename:", StringComparison.Ordinal)));
        Assert.Equal(history, world.Society.Memories.Where(item => item.Permanent).ToArray());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(history, reloaded.Society.Memories.Where(item => item.Permanent).ToArray());
    }

    [Fact]
    public void RenameUsesTheCurrentWorldDayAndRetainsTheEarlierDate()
    {
        using var world = NewWorld();
        Assert.True(world.RenameAgent(First, "Rowan Vale"));
        var state = world.ExportState();
        var tick = state.WorldSystems!.Config.TicksPerDay;
        var society = SocietyFixture.AdvanceTo(SocietyFixture.Resume(state.Society.Society).Checkpoint, tick).Checkpoint;
        var systems = state.WorldSystems;
        world.LoadPausedCheckpoint(state with
        {
            Society = state.Society with { Society = SocietyFixture.Pause(society).Checkpoint },
            WorldSystems = systems with
            {
                WorldTick = tick,
                RegionalWeather = RegionalWeatherRules.Advance(systems, tick),
                Climate = WeatherRules.Advance(systems.Climate, tick, state.WorldSeed, systems.Config),
            },
            Inhabitants = state.Inhabitants.Select(item => item with { LastDecisionContext = null }).ToArray(),
        });
        Assert.True(world.RenameAgent(First, "Rowan Lake"));
        var history = world.Society.Memories.Where(item => item.OwnerId == First).ToArray();
        Assert.Contains(history, item => item.SourceTick == 0 && item.Summary == "I was renamed on day 1 to Rowan Vale.");
        Assert.Contains(history, item => item.SourceTick == tick && item.Summary == "I was renamed on day 2 to Rowan Lake.");
        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(history, restored.Society.Memories.Where(item => item.OwnerId == First).ToArray());
    }

    [Theory]
    [InlineData("unpinned")]
    [InlineData("tombstoned")]
    [InlineData("public")]
    [InlineData("other-owner")]
    [InlineData("future")]
    public void CurrentSaveRefusesAChangedPermanentMemoryPolicy(string damage)
    {
        using var world = NewWorld();
        Assert.True(world.RenameAgent(First, "Rowan Vale"));
        var state = world.ExportState();
        var memory = Assert.Single(state.Society.Society.Memories);
        memory = damage switch
        {
            "unpinned" => memory with { Permanent = false },
            "tombstoned" => memory with { TombstonedTick = 0 },
            "public" => memory with { Visibility = "public" },
            "other-owner" => memory with { OwnerId = Second },
            "future" => memory with { SourceTick = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(damage)),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Memories = [memory] } },
        }));
    }

    [Fact]
    public void UndoingANotYetStartedFounderRemovesTheirSetupHistory()
    {
        using var world = new PrivateWorldRuntime("rename-undo", startPace: WorldStartPace.FounderSetup);
        world.PlaceFounder(First, new GridPoint(0, 0));
        Assert.True(world.RenameAgent(First, "Rowan Vale"));
        var removedId = Assert.Single(world.Society.Memories).Id;
        Assert.Equal(0, world.UndoLastFounder(First));
        Assert.Empty(world.Society.Memories);
        world.PlaceFounder(First, new GridPoint(0, 0));
        Assert.True(world.RenameAgent(First, "Rowan Lake"));
        Assert.NotEqual(removedId, Assert.Single(world.Society.Memories).Id);
        world.Validate();
    }

    private sealed class RenameMemoryProvider(List<InhabitantObservation> observed) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 1;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            lock (observed) observed.Add(request.Observation);
            var selected = request.Observation.Candidates.FirstOrDefault(item => item.Id == "safe_idle") ?? request.Observation.Candidates[0];
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                new Dictionary<string, double> { [selected.Id] = 1 }));
        }
    }
}
