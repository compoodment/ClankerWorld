using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class ChildInitialIdentityTests
{
    private static readonly Lazy<Task<byte[]>> Born = new(CreateBornAsync);
    private static readonly Lazy<Task<byte[]>> InfancyEnds = new(CreateBeforeChildhoodAsync);

    [Fact]
    public async Task NativeBirthKeepsInitialIdentityPendingThroughInfancyAndReload()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Born.Value);
        var birth = Assert.Single(state.Society.Society.Births);
        var child = Assert.Single(state.Inhabitants, person => person.InhabitantId == birth.ChildId);
        Assert.Equal(SocietyAgeBand.Infant, state.Society.Society.GetInhabitant(birth.ChildId).AgeBand);
        Assert.True(child.IdentityChoicePending);
        Assert.Equal(("undecided", "find a purpose"), (child.Personality, child.Aspiration));
        using var restored = PrivateWorldRuntime.Restore(state);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.True(restored.Inhabitants.Single(person => person.InhabitantId == birth.ChildId).IdentityChoicePending);
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind.StartsWith("agent_identity_", StringComparison.Ordinal) && item.Detail == birth.ChildId);
        var saved = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Theory]
    [InlineData(ModelNeedFormat.Numbers)]
    [InlineData(ModelNeedFormat.Words)]
    public async Task FirstChildDecisionChoosesItsOwnIdentityAtDayThreeWithOnlyFamilyBackground(ModelNeedFormat format)
    {
        var state = await BeforeChildhoodAsync();
        var childId = Assert.Single(state.Society.Society.Births).ChildId;
        var parents = state.Society.Society.Relationships.Where(edge => edge.Type == SocietyRelationshipType.BiologicalParentage && edge.TargetId == childId)
            .Select(edge => edge.ProposerId).Order(StringComparer.Ordinal).ToArray();
        using var handler = new ChildReplyHandler();
        using var replayHandler = new ChildReplyHandler();
        using var client = new HttpClient(handler);
        using var replayClient = new HttpClient(replayHandler);
        var model = PersonalModel(client, format);
        var replayModel = PersonalModel(replayClient, format);
        using var world = PrivateWorldRuntime.Restore(state, id => id == childId ? model : new QuietProvider());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == childId ? replayModel : new QuietProvider());
        Assert.Equal(SocietyAgeBand.Infant, world.Society.GetInhabitant(childId).AgeBand);
        Assert.Empty(handler.Bodies);
        for (var tick = 0; tick < 8 && world.Inhabitants.Single(person => person.InhabitantId == childId).IdentityChoicePending; tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForActualRequests(world);
            Assert.True((await replay.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForActualRequests(replay);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(3, world.Society.AgeAt(world.Society.GetInhabitant(childId), world.WorldTick));
        var child = world.Inhabitants.Single(person => person.InhabitantId == childId);
        Assert.False(child.IdentityChoicePending);
        Assert.Equal(("Inventive and independent", "Study the hills"), (child.Personality, child.Aspiration));
        var body = Assert.Single(handler.Bodies);
        using var question = JsonDocument.Parse(body);
        var self = question.RootElement.GetProperty("self");
        Assert.Equal("Child", self.GetProperty("life_stage").GetString());
        Assert.True(question.RootElement.GetProperty("needs_personality").GetBoolean());
        Assert.True(question.RootElement.GetProperty("needs_aspiration").GetBoolean());
        Assert.Equal(state.Society.Society.GetHousehold(world.Society.GetInhabitant(childId).HouseholdId!).Name,
            self.GetProperty("household").GetString());
        Assert.Equal(Assert.Single(state.Towns!, town => town.ResidentIds.Contains(childId)).Name, self.GetProperty("town").GetString());
        var background = self.GetProperty("family_background").EnumerateArray().ToArray();
        Assert.Equal(2, background.Length);
        for (var index = 0; index < parents.Length; index++)
        {
            var parent = state.Inhabitants.Single(person => person.InhabitantId == parents[index]);
            Assert.Equal(state.Society.Society.GetInhabitant(parents[index]).Name, background[index].GetProperty("name").GetString());
            Assert.Equal(parent.Personality, background[index].GetProperty("personality").GetString());
            Assert.Equal(parent.Aspiration, background[index].GetProperty("aspiration").GetString());
        }
        Assert.DoesNotContain("parent-private-sentinel", body, StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated-identity-sentinel", body, StringComparison.Ordinal);
        Assert.Single(world.ExportState().Events, item => item.Kind == "agent_identity_chosen" && item.Detail == childId);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            id => id == childId ? model : new QuietProvider());
        Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
        await WaitForActualRequests(restored);
        Assert.False(restored.Inhabitants.Single(person => person.InhabitantId == childId).IdentityChoicePending);
        Assert.Single(handler.Bodies);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("partial")]
    [InlineData("refused")]
    public async Task UnusableInitialChildReplyStaysPendingAndRetriesOnAnOrdinaryDecisionAfterReload(string reply)
    {
        var state = await BeforeChildhoodAsync();
        var childId = Assert.Single(state.Society.Society.Births).ChildId;
        using var handler = new ChildReplyHandler { Reply = reply };
        using var client = new HttpClient(handler);
        var model = PersonalModel(client);
        using var world = PrivateWorldRuntime.Restore(state, id => id == childId ? model : new QuietProvider());
        await CompleteFirstChildReply(world, childId);
        var child = world.Inhabitants.Single(person => person.InhabitantId == childId);
        Assert.True(child.IdentityChoicePending);
        Assert.Equal(("undecided", "find a purpose"), (child.Personality, child.Aspiration));
        Assert.Single(handler.Bodies);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind.StartsWith("agent_identity_", StringComparison.Ordinal) && item.Detail == childId);
        world.Pause();
        world.SetLifePace(1);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), id => id == childId ? model : new QuietProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.True(restored.Inhabitants.Single(person => person.InhabitantId == childId).IdentityChoicePending);
        handler.Reply = "valid";
        restored.Resume();
        // Safe idle uses the normal 300-step reevaluation, not an immediate
        // identity retry. Other ordinary triggers may still prompt earlier.
        for (var tick = 0; tick < 310 && restored.Inhabitants.Single(person => person.InhabitantId == childId).IdentityChoicePending; tick++)
        {
            Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
            await WaitForActualRequests(restored);
        }
        Assert.False(restored.Inhabitants.Single(person => person.InhabitantId == childId).IdentityChoicePending);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "agent_identity_chosen" && item.Detail == childId);
    }

    private static OpenAiCompatibleDecisionProvider PersonalModel(HttpClient client, ModelNeedFormat format = ModelNeedFormat.Numbers) =>
        new(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-child-model", needFormat: format);

    [Fact]
    public async Task PausedChildRequestCannotAdmitLateIdentityAndReloadKeepsTheOpportunity()
    {
        var state = await BeforeChildhoodAsync();
        var childId = Assert.Single(state.Society.Society.Births).ChildId;
        using var held = new ChildReplyHandler { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var client = new HttpClient(held);
        var model = PersonalModel(client);
        using var world = PrivateWorldRuntime.Restore(state, id => id == childId ? model : new QuietProvider());
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        held.Release.TrySetResult(true);
        await held.Returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(world.Inhabitants.Single(person => person.InhabitantId == childId).IdentityChoicePending);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "agent_identity_chosen" && item.Detail == childId);
        using var reply = new ChildReplyHandler();
        using var retryClient = new HttpClient(reply);
        var retryModel = PersonalModel(retryClient);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), id => id == childId ? retryModel : new QuietProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Resume();
        await CompleteFirstChildReply(restored, childId);
        Assert.False(restored.Inhabitants.Single(person => person.InhabitantId == childId).IdentityChoicePending);
        Assert.Single(held.Bodies);
        Assert.Single(reply.Bodies);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "agent_identity_chosen" && item.Detail == childId);
    }

    private static async Task CompleteFirstChildReply(PrivateWorldRuntime world, string childId)
    {
        for (var tick = 0; tick < 8; tick++)
        {
            var step = await world.AdvanceOneTickNonBlockingAsync();
            Assert.True(step.Advanced);
            await WaitForActualRequests(world);
            if (step.Decisions.Any(decision => decision.InhabitantId == childId)) return;
        }
        throw new TimeoutException("The first child decision did not finish.");
    }

    private static async Task WaitForActualRequests(PrivateWorldRuntime world)
    {
        var field = typeof(PrivateWorldRuntime).GetField("pendingHosted", BindingFlags.Instance | BindingFlags.NonPublic);
        var pending = Assert.IsAssignableFrom<System.Collections.IDictionary>(field!.GetValue(world));
        var tasks = pending.Values.Cast<object>().Select(item => Assert.IsAssignableFrom<Task>(item.GetType().GetProperty("Task")!.GetValue(item))).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task<PrivateWorldRuntimeState> BeforeChildhoodAsync() => PrivateWorldRuntimeCodec.Decode(await InfancyEnds.Value);

    private static async Task<byte[]> CreateBeforeChildhoodAsync()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Born.Value);
        var childId = Assert.Single(state.Society.Society.Births).ChildId;
        var parents = state.Society.Society.Relationships.Where(edge => edge.Type == SocietyRelationshipType.BiologicalParentage && edge.TargetId == childId)
            .Select(edge => edge.ProposerId).Order(StringComparer.Ordinal).ToArray();
        var society = state.Society.Society;
        foreach (var parent in parents) society = ChosenBirthNameTestFixture.NameParent(society, parent);
        state = state with
        {
            Society = state.Society with { Society = society },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Personality = person.InhabitantId == childId ? person.Personality : parents.Contains(person.InhabitantId)
                    ? "Patient grower " + Array.IndexOf(parents, person.InhabitantId) : "unrelated-identity-sentinel",
                Aspiration = person.InhabitantId == childId ? person.Aspiration : "Supply the Town",
                IdentityChoicePending = person.InhabitantId == childId,
                RecentThoughts = parents.Contains(person.InhabitantId) ? [new(state.Society.Society.WorldTick, "parent-private-sentinel")] : person.RecentThoughts,
            }).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new QuietProvider());
        world.Pause();
        world.SetLifePace(365);
        world.Resume();
        while (world.Society.AgeAt(world.Society.GetInhabitant(childId), world.WorldTick + 1) < 3)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(SocietyAgeBand.Infant, world.Society.GetInhabitant(childId).AgeBand);
        }
        // Finish the boundary at the ordinary rate so completing the reply
        // does not age the child another whole day.
        world.Pause();
        world.SetLifePace(1);
        world.Resume();
        while (world.Society.AgeAt(world.Society.GetInhabitant(childId), world.WorldTick + 1) < 3)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
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

    private sealed class ChildReplyHandler : HttpMessageHandler
    {
        public ConcurrentQueue<string> Bodies { get; } = new();
        public string Reply { get; set; } = "valid";
        public TaskCompletionSource<bool>? Release { get; init; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var envelope = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = envelope.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            Bodies.Enqueue(body);
            Started.TrySetResult(true);
            if (Release is not null) await Release.Task;
            if (Reply == "refused") return new(HttpStatusCode.Forbidden);
            using var question = JsonDocument.Parse(body);
            var surname = question.RootElement.GetProperty("needs_name").GetBoolean()
                ? question.RootElement.GetProperty("self").GetProperty("allowed_child_surnames").EnumerateArray().First().GetString() : null;
            var content = JsonSerializer.Serialize(new
            {
                selected_candidate_id = "safe_idle",
                confidence = 1d,
                chosen_name = surname is null ? null : "Zuri " + surname,
                chosen_personality = Reply == "missing" ? null : Reply == "invalid" ? "bad\ntext" : "Inventive and independent",
                chosen_aspiration = Reply is "missing" or "partial" ? null : "Study the hills",
            });
            Returned.TrySetResult(true);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }), Encoding.UTF8, "application/json")
            };
        }
    }

    private static async Task<byte[]> CreateBornAsync()
    {
        var prepared = await CookedBirthFixture.PrepareAsync(inPot: true);
        using var world = CookedBirthFixture.Restore(prepared);
        for (var tick = 0; tick < 650 && world.Society.Births.Count == 0; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var birth = Assert.Single(world.Society.Births);
        Assert.Equal(prepared.Household, birth.HouseholdId);
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => prepared.OutputIds.Contains(lot.Id)).Sum(lot => lot.Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "child_born" && item.Detail == birth.ChildId);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }
}
