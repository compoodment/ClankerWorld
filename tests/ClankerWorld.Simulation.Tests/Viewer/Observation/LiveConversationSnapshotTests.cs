using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using Client = ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed class LiveConversationSnapshotTests
{
    private const string Seed = "native-live-conversation-projection-audit";
    private static readonly Lazy<Task<byte[]>> Invitations = new(CreateInvitationsAsync);
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SeventeenthNativeInvitationKeepsEveryPendingConversationInTheClientAndFileReload()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Invitations.Value);
        using var world = PrivateWorldRuntime.Restore(state, _ => new QuietConversationProvider());
        Assert.Equal(17, world.Conversations.Count);
        Assert.All(world.Conversations, item => Assert.Equal(AgentConversationStatus.Proposed, item.Status));
        var expected = world.Conversations.OrderByDescending(item => item.LastUpdatedTick)
            .ThenBy(item => item.Id, StringComparer.Ordinal).Select(item => item.Id).ToArray();
        Assert.Equal(expected, Snapshot(world).Conversations.Select(item => item.Id));
        var directory = Directory.CreateTempSubdirectory("clankerworld-live-conversations-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), _ => new QuietConversationProvider());
            file.Save(world);
            var saved = File.ReadAllBytes(file.Path);
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            using var reloaded = file.LoadOrCreate(Seed);
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
            Assert.Equal(expected, Snapshot(reloaded).Conversations.Select(item => item.Id));
            Assert.All(Snapshot(reloaded).Conversations, item => Assert.Equal("proposed", item.Status));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task ExpiredNativeInvitationsKeepOnlySixteenRecentClosedConversations()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Invitations.Value);
        using var world = PrivateWorldRuntime.Restore(state, _ => new QuietConversationProvider());
        var expiry = world.Conversations.Max(item => item.CreatedTick) + AgentConversationRules.ProposalLifetimeTicks;
        while (world.WorldTick <= expiry)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(world);
        }
        Assert.Equal(17, world.Conversations.Count);
        Assert.All(world.Conversations, item => Assert.Equal(AgentConversationStatus.Closed, item.Status));
        var expected = world.Conversations.OrderByDescending(item => item.LastUpdatedTick)
            .ThenBy(item => item.Id, StringComparer.Ordinal).Take(16).Select(item => item.Id).ToArray();
        Assert.Equal(expected, Snapshot(world).Conversations.Select(item => item.Id));
        Assert.All(Snapshot(world).Conversations, item => Assert.Equal("closed", item.Status));
        var pairs = world.Conversations.ToDictionary(item => item.InitiatorId, item => item.InviteeId, StringComparer.Ordinal);
        var sent = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        using var next = PrivateWorldRuntime.Restore(world.ExportState(), actor => new InvitationProvider(actor, pairs, sent));
        foreach (var actor in pairs.Keys)
            next.SubmitInstruction(new("next-invitation:" + actor, "owner:test", actor,
                OwnerInstructionKind.Suggestive, "Talk with your nearby neighbor."));
        for (var tick = 0; tick < 20 && next.Conversations.Count < 34; tick++)
        {
            Assert.True((await next.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(next);
        }
        Assert.Equal(17, next.Conversations.Count(item => item.Status == AgentConversationStatus.Proposed));
        Assert.Equal(17, next.Conversations.Count(item => item.Status == AgentConversationStatus.Closed));
        var mixed = Snapshot(next).Conversations;
        Assert.Equal(17, mixed.Count(item => item.Status == "proposed"));
        Assert.Equal(expected, mixed.Where(item => item.Status == "closed").Select(item => item.Id));
    }

    private static Client.OwnerWorldSnapshot Snapshot(PrivateWorldRuntime world)
    {
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var host = new OwnerWorldObservationStore(world).GetSnapshot();
        var client = JsonSerializer.Deserialize<Client.OwnerWorldSnapshot>(JsonSerializer.Serialize(host, WireJson), WireJson)!;
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(host.Conversations.Select(item => item.Id), client.Conversations.Select(item => item.Id));
        foreach (var conversation in world.Conversations.Where(item => item.Status != AgentConversationStatus.Closed))
        {
            var shown = Assert.Single(client.Conversations, item => item.Id == conversation.Id);
            Assert.Equal(conversation.InitiatorId, shown.InitiatorId);
            Assert.Equal(conversation.InviteeId, shown.InviteeId);
            Assert.Equal(conversation.Turns.Select(item => item.Text), shown.Turns.Select(item => item.Text));
        }
        return client;
    }

    private static async Task<byte[]> CreateInvitationsAsync()
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        var sent = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        using var world = NormalPathWorld.CreateGenerated(Seed, actor => new InvitationProvider(actor, pairs, sent));
        var state = world.ExportState();
        var occupied = state.Map.CampObjects.Select(item => item.Position)
            .Concat(state.Map.Resources.Select(item => item.Position))
            .Concat(state.Inhabitants.Select(item => item.Position)).ToHashSet();
        var borders = state.Towns!.SelectMany(item => item.BorderTiles).ToHashSet();
        var chosen = new List<GridPoint>();
        world.Pause();
        foreach (var tile in state.Map.Tiles)
        {
            var point = tile.Position;
            var neighbor = new GridPoint(point.X + 1, point.Y);
            if (!state.Map.Contains(neighbor) || !state.Map.IsBuildable(point) || !state.Map.IsBuildable(neighbor) ||
                borders.Contains(point) || borders.Contains(neighbor) || occupied.Contains(point) || occupied.Contains(neighbor) ||
                chosen.Any(previous => state.Map.FootDistance(previous, point) < 5)) continue;
            var index = pairs.Count;
            var first = $"agent:{1_000 + index * 2:D32}";
            var second = $"agent:{1_001 + index * 2:D32}";
            world.AddAgent(first, point);
            world.AddAgent(second, neighbor);
            pairs.Add(first, second);
            chosen.Add(point);
            occupied.Add(point);
            occupied.Add(neighbor);
            if (pairs.Count == 17) break;
        }
        Assert.Equal(17, pairs.Count);
        world.Resume();
        for (var tick = 0; tick < 25 && world.Conversations.Count < 17; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(world);
        }
        Assert.Equal(17, world.Conversations.Count);
        Assert.All(world.Conversations, item => Assert.Equal(AgentConversationStatus.Proposed, item.Status));
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private static async Task WaitForRequests(PrivateWorldRuntime world)
    {
        var field = typeof(PrivateWorldRuntime).GetField("pendingHosted", BindingFlags.Instance | BindingFlags.NonPublic);
        var pending = Assert.IsAssignableFrom<System.Collections.IDictionary>(field!.GetValue(world));
        var tasks = pending.Values.Cast<object>().Select(item => Assert.IsAssignableFrom<Task>(item.GetType().GetProperty("Task")!.GetValue(item))).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private class QuietConversationProvider : IDecisionProvider, IAgentConversationProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public bool CanSpeakAs(string agentId) => true;
        protected virtual string Selected(InhabitantObservation observation) => "safe_idle";
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = Selected(observation);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId,
                Kind, ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                selected, 1, observation.Candidates.ToDictionary(item => item.Id, item => item.Id == selected ? 1d : 0d, StringComparer.Ordinal),
                ChosenPersonality: observation.NeedsPersonality ? "patient and curious" : null,
                ChosenAspiration: observation.NeedsAspiration ? "learn the valley" : null));
        }
        public ValueTask<AgentConversationTurnResponse> SpeakAsync(AgentConversationTurnRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No invitee has accepted a turn.");
    }

    private sealed class InvitationProvider(string actor, IReadOnlyDictionary<string, string> pairs,
        ConcurrentDictionary<string, byte> sent) : QuietConversationProvider
    {
        protected override string Selected(InhabitantObservation observation)
        {
            if (pairs.TryGetValue(actor, out var partner) && observation.Candidates.Any(item => item.Id == "talk:" + partner) &&
                sent.TryAdd(actor, 0)) return "talk:" + partner;
            return "safe_idle";
        }
    }
}
