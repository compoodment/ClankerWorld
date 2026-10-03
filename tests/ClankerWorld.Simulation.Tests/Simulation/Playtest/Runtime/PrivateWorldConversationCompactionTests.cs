using System.Collections;
using System.Reflection;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingDuringAHeldTurnPreservesItsAdmissionIdentity(bool compact)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-conversation-compaction-");
        try
        {
            var provider = new CompactionConversationProvider();
            using var world = NewWorld("conversation-compaction", _ => provider);
            world.StartWorld();
            if (compact) AddCompactionHistory(world);
            await StartHeldCompactionTurn(world, provider);
            var before = Assert.Single(world.Conversations);
            Assert.Equal(AgentConversationStatus.AwaitingSpeaker, before.Status);
            var pending = PendingCompactionTurn(world);
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));

            Assert.Equal(compact, file.Save(world));

            Assert.Equal(before, Assert.Single(world.Conversations));
            Assert.Equal(File.ReadAllBytes(file.Path), PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            Assert.False(pending.IsCompleted);
            Assert.False(file.Save(world));
            Assert.Equal(before, Assert.Single(world.Conversations));

            provider.ReleaseFirstTurn();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var admitted = Assert.Single(Assert.Single(world.Conversations).Turns);
            Assert.Equal("The held turn survived saving.", admitted.Text);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(admitted, Assert.Single(Assert.Single(world.Conversations).Turns));
            Assert.Single(world.ExportState().Events, item => item.Kind == "conversation_turn_admitted");
            world.Validate();

            // Process restore still suspends an accepted conversation. No
            // provider task is serialized or automatically reissued by loading.
            using var restored = file.LoadOrCreate("conversation-compaction");
            var suspended = Assert.Single(restored.Conversations);
            Assert.Equal(AgentConversationStatus.Suspended, suspended.Status);
            Assert.Equal(AgentConversationInterruption.Restored, suspended.Interruption);
            Assert.Equal(before.Revision + 1, suspended.Revision);
            Assert.Empty(suspended.ResumeAcceptedBy);
            Assert.Empty(suspended.Turns);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ResumeWithCompactionPreservesThePausedConversationAndReplaysAfterReload()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-resume-compaction-");
        try
        {
            var provider = new CompactionConversationProvider();
            using var world = NewWorld("resume-conversation-compaction", _ => provider);
            world.StartWorld();
            AddCompactionHistory(world);
            await StartHeldCompactionTurn(world, provider);
            world.Pause();
            var paused = world.ExportState();
            var conversation = Assert.Single(paused.Conversations!);
            Assert.Equal(AgentConversationInterruption.OwnerPaused, conversation.Interruption);

            Assert.Throws<IOException>(() => world.PersistCheckpoint(_ =>
                throw new IOException("Fixture checkpoint write failure"), resumeOnSuccess: true));
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(paused), PrivateWorldRuntimeCodec.Encode(world.ExportState()));

            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            Assert.True(file.Save(world, resumeOnSuccess: true));
            Assert.False(world.Society.IsPaused);
            Assert.Equal(conversation, Assert.Single(world.Conversations));
            var persisted = File.ReadAllBytes(file.Path);
            Assert.Equal(persisted, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

            using var restored = file.LoadOrCreate("resume-conversation-compaction");
            using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(persisted));
            var loaded = Assert.Single(restored.Conversations);
            Assert.Equal(AgentConversationStatus.Suspended, loaded.Status);
            Assert.Equal(AgentConversationInterruption.Restored, loaded.Interruption);
            Assert.Empty(loaded.ResumeAcceptedBy);
            for (var tick = 0; tick < 3; tick++)
            {
                Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()),
                    PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
            Assert.Empty(Assert.Single(restored.Conversations).Turns);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidCompactedStateCannotReplaceTheLiveWorld(bool resume)
    {
        using var world = new PrivateWorldRuntime("invalid-compaction");
        if (resume) world.Pause();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Throws<InvalidDataException>(() => world.PersistCheckpoint(state => state with
        {
            HistoryArchiveHead = new string('a', 64),
            SchemaVersion = -1,
        }, resumeOnSuccess: resume));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static void AddCompactionHistory(PrivateWorldRuntime world)
    {
        // Use real owner events instead of fabricating an expanded checkpoint.
        for (var i = 0; i <= PrivateWorldHistory.CompactionThreshold / 2; i++)
        {
            world.Pause();
            world.Resume();
        }
        Assert.True(world.ExportState().Events.Count > PrivateWorldHistory.CompactionThreshold);
    }

    private static async Task StartHeldCompactionTurn(PrivateWorldRuntime world, CompactionConversationProvider provider)
    {
        for (var tick = 0; tick < 10 && provider.Turns.Count == 0; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Single(provider.Turns);
    }

    private static Task PendingCompactionTurn(PrivateWorldRuntime world)
    {
        // Await the runtime's admission task, not just the provider response:
        // its continuation must finish before the next controlled test tick.
        var field = typeof(PrivateWorldRuntime).GetField("pendingConversationTurns",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pending = Assert.Single(Assert.IsAssignableFrom<IDictionary>(field.GetValue(world)).Values.Cast<object>());
        return Assert.IsAssignableFrom<Task>(pending.GetType().GetProperty("Task")!.GetValue(pending));
    }

    private sealed class CompactionConversationProvider : IDecisionProvider, IAgentConversationProvider
    {
        private readonly ConversationProvider decisions = ConsentReviewProvider();
        public DecisionProviderKind Kind => decisions.Kind;
        public long ProviderEpoch => decisions.ProviderEpoch;
        public List<(AgentConversationTurnRequest Request, TaskCompletionSource<AgentConversationTurnResponse> Response)> Turns { get; } = [];

        public bool CanSpeakAs(string agentId) => decisions.CanSpeakAs(agentId);
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) => decisions.DecideAsync(request, cancellationToken);

        public ValueTask<AgentConversationTurnResponse> SpeakAsync(AgentConversationTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            var response = new TaskCompletionSource<AgentConversationTurnResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            Turns.Add((request, response));
            return new(response.Task.WaitAsync(cancellationToken));
        }

        public void ReleaseFirstTurn()
        {
            var (request, response) = Turns[0];
            response.SetResult(new(request.RequestId, request.ConversationId, request.Revision,
                request.RunEpoch, request.SpeakerId, "The held turn survived saving.", AgentConversationDisposition.Continue));
        }
    }
}
