using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class GrownAgentHelperMemoryTests
{
    private static readonly string[] ConversationModels = ["initiator-model", "invitee-model"];
    private static readonly Lazy<Task<byte[]>> ConversationAdult = new(CreateConversationAdultAsync);

    [Theory]
    [InlineData("initiator")]
    [InlineData("invitee")]
    [InlineData("founders")]
    public async Task NativeBornAdultCanTakeEitherConversationRoleThroughOwnConfiguredModel(string role)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await ConversationAdult.Value);
        var birth = Assert.Single(state.Society.Society.Births);
        var child = birth.ChildId;
        Assert.True(child.Length > 128);
        var actor = role == "initiator" ? child : birth.PrimaryCaregiverId;
        var partner = role == "initiator" ? birth.PrimaryCaregiverId : role == "invitee" ? child :
            state.Society.Society.Inhabitants.First(person => person.Id != child && person.Id != actor).Id;
        var occupied = state.Map.CampObjects.Select(item => item.Position)
            .Concat(state.Map.Resources.Select(item => item.Position)).ToHashSet();
        var land = state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => state.Map.IsBuildable(point) && !occupied.Contains(point)).ToArray();
        var sites = (from a in land
                     from b in land
                     where b != a && state.Map.FootDistance(a, b) == 1
                     let far = land.Where(point => state.Map.FootDistance(a, point) > 3 &&
                         state.Map.FootDistance(b, point) > 3).ToArray()
                     where far.Length >= state.Inhabitants.Count
                     select (First: a, Second: b, Far: far)).First();
        var distantIndex = 0;
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? sites.First :
                    person.InhabitantId == partner ? sites.Second : sites.Far[distantIndex++],
                LastDecisionContext = null,
                Project = null,
                HungerBasisPoints = 9_500,
                Survival = person.Survival! with { WarmthBasisPoints = 10_000, IllnessBasisPoints = 0 },
            }).ToArray(),
        };
        var directory = Directory.CreateTempSubdirectory("native-conversation-participants-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new("deterministic", null, null, null, null, null, null));
            _ = store.Configure(new("personal", "openai", "initiator-model", "synthetic-initiator-key", false,
                actor, Guid.NewGuid().ToString("N"), "Initiator test key"));
            _ = store.Configure(new("personal", "openai", "invitee-model", "synthetic-invitee-key", false,
                partner, Guid.NewGuid().ToString("N"), "Invitee test key"));
            var usage = new ProviderUsageStore(Path.Combine(directory.FullName, "usage.json"));
            using var handler = new NativeConversationHandler();
            var router = new ConfigurableDecisionProvider(store, new NativeConversationHttpFactory(handler), usageStore: usage);
            Assert.True(router.CanSpeakAs(actor));
            Assert.True(router.CanSpeakAs(partner));
            Assert.False(router.CanSpeakAs(child + ":unknown"));
            Assert.False(router.CanSpeakAs(actor + "\n"));
            var choices = new NativeConversationChoices(actor, partner, router);
            var encoded = PrivateWorldRuntimeCodec.Encode(state);
            using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded), _ => choices);
            Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            using var replayHandler = new NativeConversationHandler();
            var replayUsage = new ProviderUsageStore(Path.Combine(directory.FullName, "replay-usage.json"));
            var replayRouter = new ConfigurableDecisionProvider(store, new NativeConversationHttpFactory(replayHandler), usageStore: replayUsage);
            using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded),
                _ => new NativeConversationChoices(actor, partner, replayRouter));
            world.Resume();
            replay.Resume();
            var beforeRefusal = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(beforeRefusal, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            for (var tick = 0; tick < 40 && !world.Conversations.Any(item => item.Status == AgentConversationStatus.Closed); tick++)
            {
                await AwaitConversationRequestsAsync(world);
                await AwaitConversationRequestsAsync(replay);
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
            var conversation = Assert.Single(world.Conversations);
            Assert.Equal(AgentConversationStatus.Closed, conversation.Status);
            Assert.Equal("withdrawn", conversation.Outcome);
            Assert.Equal(actor, conversation.InitiatorId);
            Assert.Equal(partner, conversation.InviteeId);
            Assert.Equal(new[] { actor, partner }, conversation.Turns.Select(turn => turn.SpeakerId));
            Assert.Equal(new[] { actor, partner }, handler.SpeakerIds);
            Assert.Equal(new[] { partner, actor }, handler.OtherIds);
            Assert.Equal(handler.SpeakerIds, replayHandler.SpeakerIds);
            Assert.Equal(handler.Models, replayHandler.Models);
            Assert.Equal(2, replayUsage.Capture().Completed);
            Assert.Equal(ConversationModels, handler.Models);
            Assert.Throws<ArgumentException>(() => (choices.Requests[0] with { SpeakerId = " " + actor }).Validate());
            Assert.Throws<ArgumentException>(() => (choices.Requests[0] with { OtherParticipantId = partner + "\0" }).Validate());
            Assert.Equal(2, usage.Capture().Completed);
            Assert.All(usage.Capture().Rows, row => Assert.Equal("conversation", row.Role));
            foreach (var turn in conversation.Turns)
            {
                var listener = turn.SpeakerId == actor ? partner : actor;
                Assert.Equal(new[] { listener }, turn.ListenerIds);
                var belief = Assert.Single(world.Society.Beliefs!, item => item.OwnerId == listener && item.SourceTurnId == turn.Id);
                Assert.Equal(turn.SpeakerId, belief.SourceAgentId);
            }
            world.Validate();
            var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => choices);
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            Assert.Equal(conversation.Id, Assert.Single(restored.Conversations).Id);
            var invalid = world.ExportState() with
            {
                Conversations = [conversation with { InitiatorId = "missing-native-participant" }],
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalid));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task<byte[]> CreateConversationAdultAsync()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Born.Value);
        var child = Assert.Single(state.Society.Society.Births).ChildId;
        var society = state.Society.Society;
        foreach (var parent in society.Relationships.Where(edge => edge.Type == SocietyRelationshipType.BiologicalParentage && edge.TargetId == child)
                     .Select(edge => edge.ProposerId))
            society = ChosenBirthNameTestFixture.NameParent(society, parent);
        state = state with { Society = state.Society with { Society = society } };
        using var world = PrivateWorldRuntime.Restore(state, id => id == child ? new InitialIdentityProvider() : new QuietProvider());
        world.Pause();
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
        for (var tick = 0; tick < 40 && (world.Inhabitants.Single(person => person.InhabitantId == child).IdentityChoicePending ||
                 world.Society.GetInhabitant(child).NeedsName); tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForRequests(world);
        }
        Assert.Equal(SocietyAgeBand.Adult, world.Society.GetInhabitant(child).AgeBand);
        Assert.False(world.Society.GetInhabitant(child).NeedsName);
        Assert.False(world.Inhabitants.Single(person => person.InhabitantId == child).IdentityChoicePending);
        world.Pause();
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private static async Task AwaitConversationRequestsAsync(PrivateWorldRuntime world)
    {
        await WaitForRequests(world);
        var field = typeof(PrivateWorldRuntime).GetField("pendingConversationTurns",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var pending = (System.Collections.IDictionary)field.GetValue(world)!;
        await Task.WhenAll(pending.Values.Cast<object>().Select(item => (Task)item.GetType().GetProperty("Task")!.GetValue(item)!))
            .WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class NativeConversationChoices(string actor, string partner, IAgentConversationProvider router)
        : IDecisionProvider, IAgentConversationProvider
    {
        public List<AgentConversationTurnRequest> Requests { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => router.ProviderEpoch;
        public bool CanSpeakAs(string id) => router.CanSpeakAs(id);
        public ValueTask<AgentConversationTurnResponse> SpeakAsync(AgentConversationTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return router.SpeakAsync(request, cancellationToken);
        }
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            var observation = request.Observation;
            var selected = observation.Candidates.FirstOrDefault(item => observation.InhabitantId == actor && item.Id == "talk:" + partner) ??
                observation.Candidates.FirstOrDefault(item => observation.InhabitantId == partner && item.Id.StartsWith("conversation_accept:", StringComparison.Ordinal)) ??
                observation.Candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind,
                ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                observation.Candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d),
                ChosenName: observation.NeedsName ? observation.Self!.Name : null,
                ChosenPersonality: observation.NeedsPersonality ? "patient" : null,
                ChosenAspiration: observation.NeedsAspiration ? "listen" : null));
        }
    }

    private sealed class NativeConversationHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class NativeConversationHandler : HttpMessageHandler
    {
        public List<string> SpeakerIds { get; } = [];
        public List<string> OtherIds { get; } = [];
        public List<string> Models { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Models.Add(body.RootElement.GetProperty("model").GetString()!);
            using var context = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            SpeakerIds.Add(context.RootElement.GetProperty("speaker").GetProperty("id").GetString()!);
            OtherIds.Add(context.RootElement.GetProperty("other_participant").GetProperty("id").GetString()!);
            var answer = JsonSerializer.Serialize(new
            {
                utterance = "A bounded public reply.",
                disposition = SpeakerIds.Count == 2 ? "withdraw" : "continue",
                effect = "none",
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = answer } } },
                    usage = new { prompt_tokens = 7, completion_tokens = 3 },
                }), Encoding.UTF8, "application/json"),
            };
        }
    }
}
