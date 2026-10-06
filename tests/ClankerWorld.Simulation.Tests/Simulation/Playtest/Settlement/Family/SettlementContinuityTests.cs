using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;
using GodotOwnerWorldEvent = ClankerWorld.GodotClient.UI.OwnerWorldEvent;
using GodotOwnerWorldSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    private const string ContinuityOnText = "The continuity rule is on because fewer than eight people who are not elders are alive. " +
        "Couples may put off having a child for up to two days but cannot refuse.";
    private const string ContinuityOffText = "The continuity rule is off because eight or more people who are not elders are alive. " +
        "Couples may decide against having a child again.";
    private static readonly JsonSerializerOptions ContinuityGodotJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task NewFounderWorldStartsWithTheContinuityRuleOnAndSaysSoOnce()
    {
        using var world = NormalPathWorld.CreateGenerated("continuity-founders", _ => new ParentProvider("safe_idle"));
        Assert.Equal(PrivateWorldRuntime.RequiredFounders, world.Inhabitants.Count);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ParentProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Resume();
        for (var tick = 0; tick < 3; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);

        var state = restored.ExportState();
        Assert.True(state.Continuity!.Active);
        Assert.Empty(state.Continuity.Couples);
        var on = Assert.Single(state.Events, item => item.Kind.StartsWith("continuity_rule_", StringComparison.Ordinal));
        Assert.Equal("continuity_rule_on", on.Kind);
        Assert.True(on.EventId < state.Events.Single(item => item.Kind == "world_started").EventId);
        Assert.Equal(ContinuityOnText, DescribeForPlayer(restored, on.Kind));
        Assert.True(ClientSnapshot(restored).ContinuityRuleActive);
    }

    [Fact]
    public async Task ContinuityRuleTurnsOffAtEightNonEldersAndBackOnBelowIt()
    {
        using var world = NormalPathWorld.CreateGenerated("continuity-newcomers", _ => new ParentProvider("safe_idle"));
        var map = world.ExportState().Map;
        var taken = world.Inhabitants.Select(person => person.Position).ToHashSet();
        var sites = map.Tiles.Select(tile => tile.Position).Where(point => map.IsBuildable(point) && !taken.Contains(point) &&
            !map.Resources.Any(item => item.Position == point) && !map.CampObjects.Any(item => item.Position == point) &&
            !world.RoadTiles.Contains(point)).ToArray();
        var added = new List<string>();
        foreach (var site in sites)
        {
            if (added.Count == 4) break;
            var id = "agent:" + (added.Count + 1).ToString("D32", System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                world.AddAgent(id, site);
                added.Add(id);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                // Sites where membership would be ambiguous are refused; try the next one.
            }
        }
        Assert.Equal(4, added.Count);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "continuity_rule_off");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var off = Assert.Single(world.ExportState().Events, item => item.Kind == "continuity_rule_off");
        Assert.Equal("non_elders:8", off.Detail);
        Assert.False(world.ExportState().Continuity!.Active);
        Assert.False(ClientSnapshot(world).ContinuityRuleActive);
        Assert.Equal(ContinuityOffText, DescribeForPlayer(world, off.Kind));
        Assert.True(GameUiText.IsPlayerFacingEvent("continuity_rule_off"));
        Assert.True(GameUiText.IsPlayerFacingEvent("continuity_rule_on"));
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ParentProvider("safe_idle"));
        restored.Resume();
        for (var tick = 0; tick < 3; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "continuity_rule_off");
    }

    [Fact]
    public async Task ContinuityCoupleMayPostponeButThePlanProceedsAfterTwoDays()
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        var seen = new ConcurrentDictionary<string, ConcurrentQueue<InhabitantObservation>>(StringComparer.Ordinal);
        Func<string, IDecisionProvider> providers = actor => new RecordingParentProvider(
            actor == first ? "parent_propose:" : actor == second ? "parent_postpone:" : "safe_idle", seen);
        using var world = PrivateWorldRuntime.Restore(state, providers);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var couple = Assert.Single(world.ExportState().Continuity!.Couples);
        var ordered = new[] { first, second }.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal((ordered[0], ordered[1]), (couple.FirstPartnerId, couple.SecondPartnerId));
        Assert.Equal(world.WorldTick + 2L * world.WorldSystems.Config.TicksPerDay, couple.DeadlineTick);
        Assert.Equal("requested", world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("postponed", world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);

        // A postponed plan is ordinary saved state, and replay from it is deterministic.
        // Exercise both sides of the deadline without simulating the idle days.
        PositionFamilyFixtureAt(world, couple.DeadlineTick - 2);
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), providers);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Resume();
        while (restored.WorldTick < couple.DeadlineTick - 1)
        {
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.NotEqual("preparing", restored.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        }
        Assert.Contains(restored.ExportState().Events, item => item.Kind == "parenthood_postponed" && item.Detail == first);
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind is "parenthood_cancelled" or "continuity_plan_proceeded");

        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(couple.DeadlineTick, restored.WorldTick);
        var plan = restored.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!;
        Assert.Equal(("preparing", second, restored.WorldTick), (plan.Stage, plan.PartnerId, plan.LastTransitionTick));
        Assert.Contains(restored.ExportState().Events, item => item.Kind == "continuity_plan_proceeded" && item.Detail == first);
        Assert.Contains("under the continuity rule", Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants,
            person => person.Id == second).SocialNotes.Single(note => note.StartsWith("Preparing", StringComparison.Ordinal)), StringComparison.Ordinal);
        for (var tick = 0; tick < 5; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("preparing", restored.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);

        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), providers);
        replay.Resume();
        while (replay.WorldTick < restored.WorldTick) Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));

        // Neither partner was ever offered a refusal; once the deadline passed, not even "not yet".
        var asked = seen[first].Concat(seen[second]).ToArray();
        Assert.Contains(asked, observation => observation.Candidates.Any(item => item.Id == "parent_postpone:" + first));
        Assert.DoesNotContain(asked, observation => observation.Candidates.Any(item =>
            item.Id.StartsWith("parent_decline:", StringComparison.Ordinal) || item.Id.StartsWith("parent_cancel:", StringComparison.Ordinal)));
        Assert.DoesNotContain(asked, observation => observation.WorldTick >= couple.DeadlineTick &&
            observation.Candidates.Any(item => item.Id.StartsWith("parent_", StringComparison.Ordinal)));

        // The rule only works through the existing partnership; the other two adults stay single.
        Assert.Single(restored.Society.Relationships, item => item.Type == SocietyRelationshipType.Partnership);
        Assert.All(restored.Inhabitants.Where(person => person.InhabitantId != first && person.InhabitantId != second),
            person => Assert.Null(person.Parenthood));
        Assert.Single(restored.ExportState().Continuity!.Couples);

        // A fresh request after the deadline says the plan is going ahead.
        restored.Pause();
        var due = restored.ExportState();
        var after = new ConcurrentDictionary<string, ConcurrentQueue<InhabitantObservation>>(StringComparer.Ordinal);
        using var asking = PrivateWorldRuntime.Restore(due with
        {
            Inhabitants = due.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
        }, _ => new RecordingParentProvider("safe_idle", after));
        asking.Resume();
        Assert.True((await asking.AdvanceOneTickAsync()).Advanced);
        Assert.Contains("Your two days are up", Assert.Single(after[second]).Self!.ContinuityNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContinuityCoupleCanPutOffPreparationButCannotWithdraw()
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == first ? "parent_propose:" : "parent_accept:"));
        await world.AdvanceOneTickAsync();
        await world.AdvanceOneTickAsync();
        Assert.Equal("preparing", world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        using var withdrawing = PrivateWorldRuntime.Restore(world.ExportState(), _ => new ParentProvider("parent_cancel:"));
        for (var tick = 0; tick < 40; tick++) await withdrawing.AdvanceOneTickAsync();
        Assert.Equal("preparing", withdrawing.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        using var postponing = PrivateWorldRuntime.Restore(world.ExportState(), _ => new ParentProvider("parent_postpone:"));
        for (var tick = 0; tick < 40; tick++) await postponing.AdvanceOneTickAsync();
        Assert.Equal("postponed", postponing.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        Assert.Contains(postponing.ExportState().Events, item => item.Kind == "parenthood_postponed" && item.Detail == first);
        Assert.Contains("Parenthood put off for now.", new OwnerWorldObservationStore(postponing).GetSnapshot().Inhabitants
            .Single(person => person.Id == first).SocialNotes);
        Assert.Empty(postponing.Society.Births);
    }

    [Fact]
    public async Task CouplesModelRequestsStateTheContinuityRule()
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        var seen = new ConcurrentDictionary<string, ConcurrentQueue<InhabitantObservation>>(StringComparer.Ordinal);
        using var world = PrivateWorldRuntime.Restore(state, actor => new RecordingParentProvider(
            actor == first ? "parent_propose:" : "safe_idle", seen));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var asking = seen[first].First();
        Assert.Contains("while fewer than eight non-elders are alive", asking.Self!.ContinuityNote, StringComparison.Ordinal);
        Assert.Contains("may put off having a child for up to two days but may not refuse", asking.Self.ContinuityNote, StringComparison.Ordinal);
        Assert.Contains("Your two days end in about 48 hours", asking.Self.ContinuityNote, StringComparison.Ordinal);
        Assert.Contains("may say not yet but not refuse", Assert.Single(asking.Candidates, item =>
            item.Id == "parent_propose:" + second).Description, StringComparison.Ordinal);
        var answering = seen[second].Last();
        Assert.NotNull(answering.Self!.ContinuityNote);
        Assert.Contains("you may not refuse", Assert.Single(answering.Candidates, item =>
            item.Id == "parent_postpone:" + first).Description, StringComparison.Ordinal);
        Assert.DoesNotContain(answering.Candidates, item => item.Id.StartsWith("parent_decline:", StringComparison.Ordinal));
        foreach (var single in state.Inhabitants.Skip(2))
            Assert.All(seen.GetValueOrDefault(single.InhabitantId) ?? [], observation => Assert.Null(observation.Self!.ContinuityNote));

        var handler = new ContinuityModelHandler();
        using var client = new HttpClient(handler);
        using var hosted = PrivateWorldRuntime.Restore(state, _ => new OpenAiCompatibleDecisionProvider(client,
            () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model"));
        await hosted.AdvanceOneTickNonBlockingAsync();
        for (var attempt = 0; attempt < 100 && handler.Bodies.Count < state.Inhabitants.Count; attempt++) await Task.Delay(10);
        var requests = handler.Bodies.Select(body =>
        {
            using var payload = JsonDocument.Parse(body);
            var messages = payload.RootElement.GetProperty("messages");
            using var user = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
            return (System: messages[0].GetProperty("content").GetString()!, Agent: user.RootElement.GetProperty("agent_id").GetString()!,
                Continuity: user.RootElement.GetProperty("self").GetProperty("continuity").GetString());
        }).ToArray();
        var rendered = Assert.Single(requests, item => item.Agent == first);
        Assert.Contains("Continuity, when present", rendered.System, StringComparison.Ordinal);
        Assert.Contains("may put off having a child for up to two days but may not refuse", rendered.Continuity, StringComparison.Ordinal);
        Assert.Null(RetiredWording.Find(rendered.System + rendered.Continuity));
        Assert.All(requests.Where(item => item.Agent != first && item.Agent != second), item => Assert.Null(item.Continuity));
    }

    [Fact]
    public async Task OrdinaryRefusalReturnsOnceEightNonEldersLive()
    {
        var state = WithEightNonElders(await PreparedState());
        var first = state.Inhabitants[0].InhabitantId;
        var seen = new ConcurrentDictionary<string, ConcurrentQueue<InhabitantObservation>>(StringComparer.Ordinal);
        using var world = PrivateWorldRuntime.Restore(state, actor => new RecordingParentProvider(
            actor == first ? "parent_propose:" : "parent_decline:", seen));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "continuity_rule_off" && item.Detail == "non_elders:8");
        Assert.False(world.ExportState().Continuity!.Active);
        Assert.Empty(world.ExportState().Continuity!.Couples);
        // Eight agents share four decisions a tick, so the answer may take a tick or two.
        for (var tick = 0; tick < 20 && world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood?.Stage != "cancelled"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("cancelled", world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        var second = state.Inhabitants[1].InhabitantId;
        Assert.Contains(seen[second], observation => observation.Candidates.Any(item => item.Id == "parent_decline:" + first));
        Assert.DoesNotContain(seen.Values.SelectMany(item => item), observation =>
            observation.Self?.ContinuityNote is not null ||
            observation.Candidates.Any(item => item.Id.StartsWith("parent_postpone:", StringComparison.Ordinal)));

        // Losing a newcomer drops the world below eight again, and the rule comes back for the couple.
        var later = world.ExportState();
        var newcomer = later.Inhabitants.First(person => person.InhabitantId.StartsWith("agent:", StringComparison.Ordinal)).InhabitantId;
        using var society = SocietyWorldRuntime.Restore(later.Society);
        society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, newcomer, SocietyDeathCause.Accident, checkpoint.WorldTick));
        using var fewer = PrivateWorldRuntime.Restore(later with
        {
            Society = society.ExportState(),
            Inhabitants = later.Inhabitants.Where(person => person.InhabitantId != newcomer).ToArray(),
        }, _ => new ParentProvider("safe_idle"));
        Assert.True((await fewer.AdvanceOneTickAsync()).Advanced);
        var transitions = fewer.ExportState().Events.Where(item => item.Kind.StartsWith("continuity_rule_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["continuity_rule_on", "continuity_rule_off", "continuity_rule_on"], transitions.Select(item => item.Kind));
        Assert.Equal("non_elders:7", transitions[^1].Detail);
        Assert.Equal(ContinuityOnText, WorldEventText.Describe(
            new GodotOwnerWorldEvent(transitions[^1].EventId, transitions[^1].WorldTick, transitions[^1].Kind, transitions[^1].Detail), null));
        var couple = Assert.Single(fewer.ExportState().Continuity!.Couples);
        Assert.Contains(first, new[] { couple.FirstPartnerId, couple.SecondPartnerId });
    }

    [Fact]
    public async Task ContinuityTransitionsAreLoggedWithoutPrivateNames()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-continuity-log-");
        try
        {
            var state = WithEightNonElders(await PreparedState());
            state = state with
            {
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Inhabitants = state.Society.Society.Inhabitants.Select(person => person with { Name = $"private-continuity-secret-{person.Id}" }).ToArray(),
                    }
                }
            };
            using var world = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("safe_idle"));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world, new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Contains(logger.Messages, message => message.Contains("settlement_family", StringComparison.Ordinal) &&
                message.Contains("event=continuity_rule_off", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("private-continuity-secret", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SavedContinuityStateIsCheckedOnLoad()
    {
        using var world = PrivateWorldRuntime.Restore(await PreparedState(), _ => new ParentProvider("safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var state = world.ExportState();
        var couple = Assert.Single(state.Continuity!.Couples);
        using (var roundTrip = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state))))
            Assert.Equal(state.Continuity.Couples, roundTrip.ExportState().Continuity!.Couples);
        SettlementContinuity?[] forged =
        [
            null,
            state.Continuity with { Active = false },
            state.Continuity with { Couples = [couple with { DeadlineTick = couple.DeadlineTick + 1 }] },
            state.Continuity with { Couples = [couple with { FirstPartnerId = couple.SecondPartnerId, SecondPartnerId = couple.FirstPartnerId }] },
            state.Continuity with { Couples = [couple with { SecondPartnerId = "agent:missing" }] },
            state.Continuity with { Couples = [couple, couple] },
        ];
        foreach (var continuity in forged)
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with { Continuity = continuity }));
    }

    private static string DescribeForPlayer(PrivateWorldRuntime world, string kind)
    {
        var store = new OwnerWorldObservationStore(world);
        var snapshot = ClientSnapshot(world);
        var accepted = store.GetEventsAfter(0).Events.Last(item => item.Kind == kind);
        var clientEvent = JsonSerializer.Deserialize<GodotOwnerWorldEvent>(
            JsonSerializer.Serialize(accepted, ContinuityGodotJson), ContinuityGodotJson);
        Assert.True(GameUiText.IsPlayerFacingEvent(clientEvent!.Kind));
        return WorldEventText.Describe(clientEvent, snapshot);
    }

    private static GodotOwnerWorldSnapshot ClientSnapshot(PrivateWorldRuntime world) =>
        JsonSerializer.Deserialize<GodotOwnerWorldSnapshot>(JsonSerializer.Serialize(
            new OwnerWorldObservationStore(world).GetSnapshot(), ContinuityGodotJson), ContinuityGodotJson)!;

    /// <summary>Adds unrelated adults without a household until eight non-elders live, which turns the rule off.</summary>
    private static PrivateWorldRuntimeState WithEightNonElders(PrivateWorldRuntimeState state)
    {
        using var society = SocietyWorldRuntime.Restore(state.Society);
        var taken = state.Inhabitants.Select(person => person.Position).ToHashSet();
        var sites = state.Map.Tiles.Select(tile => tile.Position).Where(point => state.Map.IsPassable(point) &&
            !taken.Contains(point) && !state.Map.Resources.Any(item => item.Position == point)).ToArray();
        var added = new List<PlaytestInhabitantState>();
        for (var index = 0; state.Inhabitants.Count + added.Count < 8; index++)
        {
            var id = "agent:" + (index + 1).ToString("D32", System.Globalization.CultureInfo.InvariantCulture);
            society.Apply(checkpoint => SocietyFixture.AddAdult(checkpoint, id, null));
            added.Add(new(id, sites[index], 9_000, 0, "steady", "help where needed", Survival: new SurvivalCondition()));
        }
        return state with { Society = society.ExportState(), Inhabitants = [.. state.Inhabitants, .. added] };
    }

    private sealed class RecordingParentProvider(string prefix,
        ConcurrentDictionary<string, ConcurrentQueue<InhabitantObservation>> seen) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            seen.GetOrAdd(request.Observation.InhabitantId, _ => new()).Enqueue(request.Observation);
            // Hungry agents eat, so a long wait for the deadline does not starve them.
            var candidate = request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? (request.Observation.HungerBasisPoints < 3_500
                    ? request.Observation.Candidates.FirstOrDefault(item => item.Id is "consume_food" or "collect_shared_food")
                    : null)
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [candidate] },
            }, cancellationToken);
        }
    }

    private sealed class ContinuityModelHandler : HttpMessageHandler
    {
        public ConcurrentQueue<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
            var answer = "{\"selected_candidate_id\":\"safe_idle\",\"confidence\":1}";
            var body = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = answer } } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
