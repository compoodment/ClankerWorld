using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class GrownAgentHelperMemoryTests
{
    private static readonly Lazy<Task<byte[]>> FirstHearingHousehold = new(() => CreateHearingHouseholdAsync(false));
    private static readonly Lazy<Task<byte[]>> ThirdHearingHousehold = new(() => CreateHearingHouseholdAsync(true));
    private static readonly Lazy<Task<byte[]>> ShortExpiryHearing = new(() => CreateHouseholdExpiryAsync(null));
    private static readonly Lazy<Task<byte[]>> FirstExpiryHearing = new(() => CreateHouseholdExpiryAsync(false));
    private static readonly Lazy<Task<byte[]>> ThirdExpiryHearing = new(() => CreateHouseholdExpiryAsync(true));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeFoundedHouseholdReceivesAnActuallyRecoveredHouse(bool later)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await (later ? ThirdHearingHousehold : FirstHearingHousehold).Value);
        var actor = HearingHouseholdActor(state, later);
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var choices = new NativeLandHearingChoices { Request = true, Consent = true, Rule = true };
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        world.Resume();
        using var host = new NativeConversationCheckpointHost(world);
        await HouseholdHearingUntil(world, host, () => world.Towns[0].LandHearings.Cases.Any(item =>
            item.Property?.Transfer is { ResultBuilding.HouseholdId: null }), NativeHearingDay * 3);
        choices.Target = household;
        choices.Rule = false;
        await HouseholdHearingUntil(world, host, () => world.Towns[0].LandHearings.Cases.Any(item =>
            item.Property?.Request.TargetHouseholdId == household), NativeHearingDay * 2);
        var pending = world.Towns[0].LandHearings.Cases.Single(item => item.Property?.Request.TargetHouseholdId == household);
        Assert.Null(pending.Property!.Transfer);
        Assert.Contains(choices.Selected, choice => choice.Choice.Contains("|hearing_property_request|", StringComparison.Ordinal) &&
            choice.Choice.EndsWith("|agent-sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(household))), StringComparison.Ordinal));
        AssertInvalidHouseholdProperty(world.ExportState(), pending);
        var before = host.Saved();
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        choices.Request = false;
        choices.Rule = true;
        // Reload restarts external calls. Start both continuations after the
        // public cancellation boundary, with identical fresh provider inputs.
        await AwaitConversationRequestsAsync(world);
        world.CancelPendingHostedDecisions();
        var bytes = host.Checkpoint();
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new NativeLandHearingChoices
        { Target = household, Consent = true, Rule = true });
        replay.Resume();
        using var replayHost = new NativeConversationCheckpointHost(replay);
        await HouseholdHearingPair(world, host, replay, replayHost, () => world.Towns[0].LandHearings.Cases
            .Single(item => item.Id == pending.Id).Property!.Transfer is not null, NativeHearingDay * 2);
        var granted = world.Towns[0].LandHearings.Cases.Single(item => item.Id == pending.Id);
        var transfer = granted.Property!.Transfer!;
        Assert.Equal(household, transfer.ResultBuilding.HouseholdId);
        Assert.Equal(household, world.WorldSimulation.Buildings.Single(item => item.InstanceId == NativeHearingHouse).HouseholdId);
        Assert.Contains(granted.Property.Consents, consent => consent.AgentId == actor && consent.Agreed);
        Assert.Equal((NativeHearingJudge, "grant", household), (Assert.Single(granted.Rulings).Judge.AgentId,
            granted.Rulings[0].Outcome.Kind, granted.Rulings[0].Outcome.HouseholdId));
        Assert.Equal(transfer.PriorLots.Select(lot => (lot.Id, lot.Quantity, lot.StorageBuildingId)),
            transfer.ResultLots.Select(lot => (lot.Id, lot.Quantity, lot.StorageBuildingId)));
        Assert.All(transfer.ResultLots, lot => Assert.Equal(household, lot.OwnerId));
        AssertInvalidHouseholdProperty(world.ExportState(), granted);
    }

    [Theory]
    [InlineData(null, "end")]
    [InlineData(false, "end")]
    [InlineData(false, "renew")]
    [InlineData(false, "amend")]
    [InlineData(true, "end")]
    [InlineData(true, "renew")]
    [InlineData(true, "amend")]
    public async Task NativeFoundedHouseholdExpiryAcceptsTheActualJudgeRuling(bool? later, string outcome)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await (later is null ? ShortExpiryHearing : later.Value ? ThirdExpiryHearing : FirstExpiryHearing).Value);
        var household = later is null ? "household:camp-alpha" : state.Society.Society.GetInhabitant(HearingHouseholdActor(state, later.Value)).HouseholdId!;
        var actor = later is null ? state.Society.Society.GetHousehold(household).MemberIds[0] : HearingHouseholdActor(state, later.Value);
        var choices = new NativeHouseholdExpiryChoices(household, outcome);
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        world.Resume();
        using var host = new NativeConversationCheckpointHost(world);
        var item = Assert.Single(world.Towns[0].LandHearings.Cases, item => item.Kind == "expiry");
        await HouseholdHearingUntil(world, host, () => world.Towns[0].LandHearings.Cases.Single(saved => saved.Id == item.Id).Status == "settled", 20);
        var settled = world.Towns[0].LandHearings.Cases.Single(saved => saved.Id == item.Id);
        var ruling = Assert.Single(settled.Rulings);
        Assert.Equal((NativeHearingJudge, outcome, household), (ruling.Judge.AgentId, ruling.Outcome.Kind, ruling.Outcome.HouseholdId));
        Assert.Contains(choices.Selected, choice => choice.Contains("|hearing_rule|", StringComparison.Ordinal) &&
            choice.EndsWith("|" + outcome, StringComparison.Ordinal));
        var tiles = TownLandHearingRules.CurrentRevision(settled).Tiles;
        var rights = world.ExportState().HouseholdLandUseRights!.Where(right => right.Tiles.Any(tiles.Contains)).ToArray();
        if (outcome == "end") Assert.Empty(rights);
        else Assert.All(rights, right => { Assert.Equal(household, right.HouseholdId); Assert.True(right.AgreedEndTick > world.WorldTick); });
        Assert.NotEmpty(ruling.EvidenceIds);
        Assert.Contains(settled.Responses, response => response.AgentId == actor);
        await AwaitConversationRequestsAsync(world);
        world.CancelPendingHostedDecisions();
        var bytes = host.Checkpoint();
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new NativeHouseholdExpiryChoices(household, outcome));
        replay.Resume();
        using var replayHost = new NativeConversationCheckpointHost(replay);
        await HouseholdHearingPair(world, host, replay, replayHost, () => true, 0);
    }

    private static string HearingHouseholdActor(PrivateWorldRuntimeState state, bool later) =>
        state.Society.Society.Births.OrderBy(birth => birth.CommittedTick).ElementAt(later ? 2 : 0).ChildId;

    private static async Task<byte[]> CreateHearingHouseholdAsync(bool later)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await LaterHearingGovernment.Value);
        var actor = HearingHouseholdActor(state, later);
        var choices = new NativeHouseholdFormationChoices(actor);
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        // Match the reported preparation: clear the two original homes through
        // actual adult departures, so another available home does not postpone founding.
        var housed = world.Society.Inhabitants.Where(person => person.HouseholdId is not null &&
            world.WorldSimulation.Buildings.Any(building => building.HouseholdId == person.HouseholdId &&
                world.WorldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house"))))
            .Select(person => person.Id).ToArray();
        foreach (var member in housed) Assert.True(world.DisplaceAdult(member));
        world.Resume();
        using var host = new NativeConversationCheckpointHost(world);
        await HouseholdHearingUntil(world, host, () => world.Society.GetInhabitant(actor).HouseholdId is { } home &&
            home.StartsWith("household:solo:" + actor + ":", StringComparison.Ordinal), 40);
        var household = world.Society.GetInhabitant(actor).HouseholdId!;
        Assert.Equal(later ? 351 : 156, household.Length);
        Assert.Contains("household_found", choices.Selected);
        Assert.Equal([actor], world.Society.GetHousehold(household).MemberIds);
        return host.Saved();
    }

    private static async Task<byte[]> CreateHouseholdExpiryAsync(bool? later)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await (later is null ? LaterHearingGovernment : later.Value ? ThirdHearingHousehold : FirstHearingHousehold).Value);
        var household = later is null ? "household:camp-alpha" : state.Society.Society.GetInhabitant(HearingHouseholdActor(state, later.Value)).HouseholdId!;
        var farm = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var definition = state.WorldContent!.Buildings.Single(building => building.CanonicalId == farm.DefinitionId);
        var plot = WorldContentSimulationRules.Footprint(definition, farm).ToHashSet();
        state = state with { HouseholdLandUseRights = state.HouseholdLandUseRights!.Select(right =>
            right.Tiles.Any(plot.Contains) ? right with { AgreedEndTick = state.Society.Society.WorldTick + 8 } : right).ToArray() };
        var choices = new NativeHouseholdExpiryChoices(household, null);
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        if (farm.HouseholdId != household)
        {
            var reassigned = world.ReassignBuilding(farm.InstanceId, farm.TownId, farm.HouseholdId, null, household);
            Assert.True(reassigned.Applied, reassigned.Failure);
        }
        world.Resume();
        using var host = new NativeConversationCheckpointHost(world);
        await HouseholdHearingUntil(world, host, () => choices.Offered.Any(id => id.Contains("|hearing_rule|", StringComparison.Ordinal)), NativeHearingDay * 2);
        Assert.Single(world.Towns[0].LandHearings.Cases, item => item.Kind == "expiry");
        return host.Saved();
    }

    private static async Task HouseholdHearingStep(PrivateWorldRuntime world, NativeConversationCheckpointHost host)
    {
        await AwaitConversationRequestsAsync(world);
        NativeHearingPromptAll(world);
        await host.Advance();
        await AwaitConversationRequestsAsync(world);
        var bytes = host.Saved();
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new NativeLandHearingChoices());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static async Task HouseholdHearingUntil(PrivateWorldRuntime world, NativeConversationCheckpointHost host, Func<bool> done, int limit)
    {
        for (var tick = 0; tick < limit && !done(); tick++) await HouseholdHearingStep(world, host);
        Assert.True(done(), "The actual native household hearing boundary was not reached: " + string.Join("; ", world.ExportState().Events.TakeLast(5).Select(item => item.Kind + ":" + item.Detail)));
    }

    private static async Task HouseholdHearingPair(PrivateWorldRuntime world, NativeConversationCheckpointHost host,
        PrivateWorldRuntime replay, NativeConversationCheckpointHost replayHost, Func<bool> done, int limit)
    {
        for (var tick = 0; tick < limit && !done(); tick++)
        {
            await HouseholdHearingStep(world, host);
            await HouseholdHearingStep(replay, replayHost);
            AssertHouseholdHearingBytes(host, replayHost);
        }
        if (!done())
        {
            var evidence = Directory.CreateTempSubdirectory("household-hearing-timeout-");
            File.WriteAllBytes(Path.Combine(evidence.FullName, "live.json"), host.Saved());
            Assert.Fail("Actual hearing did not complete: " + evidence.FullName);
        }
        for (var tick = 0; tick < 2; tick++)
        {
            await HouseholdHearingStep(world, host);
            await HouseholdHearingStep(replay, replayHost);
            AssertHouseholdHearingBytes(host, replayHost);
        }
    }

    private static void AssertHouseholdHearingBytes(NativeConversationCheckpointHost host, NativeConversationCheckpointHost replayHost)
    {
        var expected = host.Saved(); var actual = replayHost.Saved();
        if (!expected.SequenceEqual(actual))
        {
            var evidence = Directory.CreateTempSubdirectory("household-hearing-mismatch-");
            File.WriteAllBytes(Path.Combine(evidence.FullName, "live.json"), expected);
            File.WriteAllBytes(Path.Combine(evidence.FullName, "replay.json"), actual);
            Assert.Fail("Complete checkpoint bytes differ; generated-fixture evidence: " + evidence.FullName);
        }
        Assert.Equal(expected, actual);
    }

    private static void AssertInvalidHouseholdProperty(PrivateWorldRuntimeState state, TownLandCase item)
    {
        var household = item.Property!.Request.TargetHouseholdId!;
        foreach (var invalid in new[] { household + ":unknown", " " + household, household + "\0" })
        {
            var damaged = item with { Property = item.Property with { Request = item.Property.Request with { TargetHouseholdId = invalid } } };
            var changed = state with { Towns = state.Towns!.Select(town => town with
            { LandHearings = town.LandHearings with { Cases = town.LandHearings.Cases.Select(saved => saved.Id == item.Id ? damaged : saved).ToArray() } }).ToArray() };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(changed));
        }
    }

    private sealed class NativeHouseholdFormationChoices(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentQueue<string> Selected { get; } = new();
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = observation.Candidates.FirstOrDefault(candidate => observation.InhabitantId == actor && candidate.Id == "household_found") ??
                observation.Candidates.FirstOrDefault(candidate => observation.InhabitantId == actor && candidate.Id.StartsWith("household_ask:", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => observation.InhabitantId != actor && candidate.Id == "household_refuse:" + actor) ??
                observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            Selected.Enqueue(selected.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                new Dictionary<string, double> { [selected.Id] = 1 }));
        }
    }

    private sealed class NativeHouseholdExpiryChoices(string household, string? outcome) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ConcurrentQueue<string> Selected { get; } = new();
        public ConcurrentQueue<string> Offered { get; } = new();
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            foreach (var candidate in observation.Candidates) if (observation.InhabitantId == NativeHearingJudge) Offered.Enqueue(candidate.Id);
            CognitionCandidate? Find(string action) => observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|" + action + "|", StringComparison.Ordinal));
            var selected = Find("read") ?? Find("hearing_inspect") ?? Find("hearing_answer");
            if (outcome is not null && observation.InhabitantId == NativeHearingJudge)
                selected ??= observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_rule|", StringComparison.Ordinal) && candidate.Id.EndsWith("|" + outcome, StringComparison.Ordinal));
            selected ??= observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            Selected.Enqueue(selected.Id);
            var evidence = selected.Description.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.StartsWith("land-evidence:", StringComparison.Ordinal)).Select(token => token.Split('=')[0]).Distinct(StringComparer.Ordinal).ToArray();
            var ruling = selected.Id.Contains("|hearing_rule|", StringComparison.Ordinal);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                new Dictionary<string, double> { [selected.Id] = 1 }, CivicLandHearing: new(
                    Statement: "Resolve this exact expired permission using its inspected record and actual personal responses.",
                    HouseholdId: ruling ? household : null, AgreedEndTick: ruling && outcome != "end" ? observation.WorldTick + NativeHearingDay * 4 : null,
                    EvidenceIds: evidence, RequestedOutcome: ruling ? outcome : null)));
        }
    }
}
