using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class HouseholdLandGrantTests
{
    [Fact]
    public async Task CouncilMajorityAndEveryAdultsSeparateAcceptanceSurviveRollbackAndReload()
    {
        var choices = new Choices();
        using var world = Create("household-grant", choices);
        var initial = world.ExportState();
        var town = world.Towns[0];
        choices.Author = town.ResidentIds[0];
        choices.Plot = OpenPlot(initial);
        var household = world.Society.GetInhabitant(choices.Author).HouseholdId!;
        var adults = world.Society.Inhabitants.Where(p => p.HouseholdId == household).Select(p => p.Id).Order().ToArray();
        Assert.Equal(2, adults.Length);
        await Until(world, () => world.HouseholdLandUseRequests.Count == 1);
        choices.Plot = null;
        var request = Assert.Single(world.HouseholdLandUseRequests);
        Assert.Empty(request.Consents);
        Assert.Empty(world.Towns[0].Governance!.Proposals[0].Votes);
        Assert.Equal(initial.HouseholdLandUseRights, world.HouseholdLandUseRights);
        choices.Voters.UnionWith(town.ResidentIds.Take(3));
        Refresh(world, town.ResidentIds);
        await Until(world, () => world.Towns[0].Governance!.Proposals[0].Status == "passed");
        Assert.Equal("pending", world.HouseholdLandUseRequests[0].Status);
        Assert.Empty(world.HouseholdLandUseRequests[0].Consents);
        choices.Acceptors.Add(adults[0]);
        Refresh(world, adults);
        await Until(world, () => world.HouseholdLandUseRequests[0].Consents.Count == 1);
        Assert.Equal("pending", world.HouseholdLandUseRequests[0].Status);
        Assert.Equal(initial.HouseholdLandUseRights, world.HouseholdLandUseRights);
        Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().HouseholdLandUseRequests);

        world.Pause();
        var pending = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(pending), _ => new Provider(choices));
        Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        choices.Acceptors.Add(adults[1]);
        Refresh(world, adults);
        Refresh(reloaded, adults);
        world.Resume();
        reloaded.Resume();
        for (var attempt = 0; attempt < 60 && world.HouseholdLandUseRequests[0].Status == "pending"; attempt++)
        {
            var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            await world.AdvanceOneTickAsync();
            await reloaded.AdvanceOneTickAsync();
        }
        var granted = world.HouseholdLandUseRequests[0];
        Assert.Equal("granted", granted.Status);
        Assert.Equal(adults, granted.GrantAdults);
        var right = Assert.Single(world.HouseholdLandUseRights, r => r.Id == HouseholdLandGrantRules.RightId(request.Id));
        Assert.Equal(request.Tiles, right.Tiles);
        Assert.Equal(household, right.HouseholdId);
        Assert.Equal(initial.TownLandTitles, world.TownLandTitles);
        Assert.Equal(initial.WorldSimulation!.Buildings, world.WorldSimulation.Buildings);
        Assert.Empty(new OwnerWorldObservationStore(world).GetSnapshot().HouseholdLandUseRequests);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        var approved = world.ExportState();
        using var after = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(approved)));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(approved), PrivateWorldRuntimeCodec.Encode(after.ExportState()));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(approved with { HouseholdLandUseRequests = [] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(approved with
        {
            HouseholdLandUseRequests = [granted with { Consents = granted.Consents.Take(1).ToArray() }],
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(approved with
        {
            HouseholdLandUseRights = initial.HouseholdLandUseRights,
        }));
    }

    [Fact]
    public async Task ConflictingClaimBlocksGrantUntilItsFilerExplicitlyWithdraws()
    {
        var choices = new Choices();
        using var world = Create("household-grant-conflict", choices);
        var initial = world.ExportState();
        var plot = OpenPlot(initial);
        var alpha = world.Towns[0].ResidentIds[0];
        var household = world.Society.GetInhabitant(alpha).HouseholdId;
        var beta = world.Society.Inhabitants.First(p => p.HouseholdId != household).Id;
        var first = world.RequestHouseholdLandUse("first", alpha, world.Towns[0].Id, plot);
        var rival = world.RequestHouseholdLandUse("rival", beta, world.Towns[0].Id, plot);
        Assert.True(first.Applied, first.Failure);
        Assert.True(rival.Applied, rival.Failure);
        Assert.True(rival.IsDisputed);
        Assert.Null(rival.Request!.CouncilProposalId);
        choices.Voters.UnionWith(world.Towns[0].ResidentIds);
        choices.Acceptors.UnionWith(world.Society.Inhabitants.Where(p => p.HouseholdId == household).Select(p => p.Id));
        await Until(world, () => world.Towns[0].Governance!.Proposals[0].Status == "passed" &&
            world.HouseholdLandUseRequests.Single(r => r.Id == "first").Consents.Count == 2);
        Assert.All(world.HouseholdLandUseRequests, r => Assert.Equal("pending", r.Status));
        Assert.Equal(initial.HouseholdLandUseRights, world.HouseholdLandUseRights);
        choices.WithdrawRequest = "rival";
        Refresh(world, [beta]);
        await Until(world, () => world.HouseholdLandUseRequests.Single(r => r.Id == "first").Status == "granted");
        Assert.Equal("withdrawn", world.HouseholdLandUseRequests.Single(r => r.Id == "rival").Status);
        Assert.False(TownLandRightsRules.IsDisputed(plot[0], world.HouseholdLandUseRights, world.HouseholdLandUseRequests));
        world.Validate();
    }

    [Fact]
    public async Task AnAdultWhoJoinsWhileApprovalIsPendingMustAlsoAccept()
    {
        var choices = new Choices();
        using var world = Create("household-grant-new-member", choices);
        var actor = world.Towns[0].ResidentIds[0];
        var household = world.Society.GetInhabitant(actor).HouseholdId!;
        var adults = world.Society.Inhabitants.Where(p => p.HouseholdId == household).Select(p => p.Id).ToArray();
        Assert.True(world.RequestHouseholdLandUse("growing-household", actor, world.Towns[0].Id, OpenPlot(world.ExportState())).Applied);
        choices.Voters.UnionWith(world.Towns[0].ResidentIds);
        choices.Acceptors.Add(adults[0]);
        await Until(world, () => world.Towns[0].Governance!.Proposals[0].Status == "passed" && world.HouseholdLandUseRequests[0].Consents.Count == 1);
        var house = world.WorldSimulation.Buildings.First(b => b.HouseholdId == household && b.InstanceId.Contains("house-", StringComparison.Ordinal));
        const string newcomer = "agent:00000000000000000000000000000093";
        Assert.Equal(household, world.AddAgent(newcomer, house.Position));
        choices.Acceptors.Add(adults[1]);
        Refresh(world, adults);
        await Until(world, () => world.HouseholdLandUseRequests[0].Consents.Count == 2);
        Assert.Equal("pending", world.HouseholdLandUseRequests[0].Status);
        Assert.DoesNotContain(world.HouseholdLandUseRights, r => r.Id == HouseholdLandGrantRules.RightId("growing-household"));
        Assert.Contains("2/3", Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().HouseholdLandUseRequests).ApprovalDetail, StringComparison.Ordinal);
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeclineOrElapsedRequestedTermNeverCreatesARight(bool elapsed)
    {
        var choices = new Choices();
        using var world = Create("household-grant-refusal", choices);
        var initial = world.ExportState();
        var actor = world.Towns[0].ResidentIds[0];
        var result = world.RequestHouseholdLandUse("refusal", actor, world.Towns[0].Id, OpenPlot(initial), elapsed ? 1 : null);
        Assert.True(result.Applied, result.Failure);
        if (!elapsed) choices.Decliners.Add(actor);
        await Until(world, () => world.HouseholdLandUseRequests[0].Status == "rejected");
        Assert.Equal(initial.HouseholdLandUseRights, world.HouseholdLandUseRights);
        Assert.Empty(new OwnerWorldObservationStore(world).GetSnapshot().HouseholdLandUseRequests);
        world.Validate();
    }

    [Fact]
    public async Task ARequestAgainstAnExistingRightCannotReplaceItWithOrdinaryCouncilVotes()
    {
        var choices = new Choices();
        using var world = Create("household-grant-existing", choices);
        var initial = world.ExportState();
        var actor = world.Towns[0].ResidentIds[0];
        var household = world.Society.GetInhabitant(actor).HouseholdId;
        var other = world.HouseholdLandUseRights.First(r => r.HouseholdId != household);
        var result = world.RequestHouseholdLandUse("occupied", actor, world.Towns[0].Id, other.Tiles);
        Assert.True(result.Applied, result.Failure);
        Assert.True(result.IsDisputed);
        Assert.Null(result.Request!.CouncilProposalId);
        choices.Voters.UnionWith(world.Towns[0].ResidentIds);
        choices.Acceptors.UnionWith(world.Society.Inhabitants.Where(p => p.HouseholdId == household).Select(p => p.Id));
        await Until(world, () => world.HouseholdLandUseRequests[0].Consents.Count == 2);
        Assert.Equal("pending", world.HouseholdLandUseRequests[0].Status);
        Assert.Empty(world.Towns[0].Governance!.Proposals);
        Assert.Equal(initial.HouseholdLandUseRights, world.HouseholdLandUseRights);
        world.Validate();
    }

    private static PrivateWorldRuntime Create(string seed, Choices choices)
    {
        using var source = NormalPathWorld.CreateGenerated(seed, _ => new Provider(choices));
        var initial = source.ExportState();
        return PrivateWorldRuntime.Restore(initial with
        {
            Inhabitants = initial.Inhabitants.Select(p => p with { Position = initial.Towns![0].OriginSite!.Value }).ToArray(),
        }, _ => new Provider(choices));
    }

    private static GridPoint[] OpenPlot(PrivateWorldRuntimeState state) =>
        [state.TownLandTitles!.SelectMany(t => t.Tiles).First(tile =>
            !state.HouseholdLandUseRights!.Any(right => right.Tiles.Contains(tile)))];

    private static void Refresh(PrivateWorldRuntime world, IEnumerable<string> actors)
    {
        foreach (var actor in actors)
            world.SubmitInstruction(new OwnerInstructionRequest("consider-land-" + actor + "-" + world.WorldTick,
                "owner:test", actor, OwnerInstructionKind.Suggestive, "Consider the pending land request."));
    }

    private static async Task Until(PrivateWorldRuntime world, Func<bool> reached)
    {
        for (var tick = 0; tick < 80 && !reached(); tick++) await world.AdvanceOneTickAsync();
        Assert.True(reached());
    }

    private sealed class Choices
    {
        public string? Author { get; set; }
        public GridPoint[]? Plot { get; set; }
        public string? WithdrawRequest { get; set; }
        public HashSet<string> Voters { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Acceptors { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Decliners { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Provider(Choices choices) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var o = request.Observation;
            CognitionCandidate? Pick(string action) => o.Candidates.FirstOrDefault(c => c.Id.Contains("|" + action + "|", StringComparison.Ordinal));
            var selected = Pick("read") ??
                o.Candidates.FirstOrDefault(c => choices.WithdrawRequest is not null && c.Id.Contains("|withdraw_land_use|" + choices.WithdrawRequest + "|", StringComparison.Ordinal)) ??
                (choices.Voters.Contains(o.InhabitantId) ? Pick("yes") : null) ??
                (choices.Acceptors.Contains(o.InhabitantId) ? Pick("accept_land_use") : null) ??
                (choices.Decliners.Contains(o.InhabitantId) ? Pick("decline_land_use") : null) ??
                (choices.Author == o.InhabitantId && choices.Plot is not null ? Pick("request_land_use") : null) ??
                o.Candidates.Single(c => c.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, o.InhabitantId, Kind, ProviderEpoch,
                o.RunEpoch, o.DecisionGeneration, o.ObservationDigest, selected.Id, 1,
                o.Candidates.ToDictionary(c => c.Id, c => c.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandTiles: selected.Id.Contains("|request_land_use|", StringComparison.Ordinal)
                    ? choices.Plot!.Select(tile => new CognitionLandTile(tile.X, tile.Y)).ToArray() : null));
        }
    }
}
