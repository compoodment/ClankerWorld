using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandClaimTests
{
    [Fact]
    public async Task PersonalProposalNeedsMajorityAndCommitsTitleWithVotesAcrossRollbackAndReload()
    {
        var choices = new ClaimChoices();
        using var source = NormalPathWorld.CreateGenerated("council-land-claim", _ => new ClaimProvider(choices));
        var initial = source.ExportState();
        var town = initial.Towns![0];
        var plot = FindPlot(initial);
        choices.Author = town.ResidentIds[0];
        choices.Plot = plot;
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Inhabitants = initial.Inhabitants.Select(p => p with { Position = town.OriginSite!.Value }).ToArray(),
        }, _ => new ClaimProvider(choices));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        await Until(world, () => world.Towns[0].Governance!.Proposals.Count == 1);
        choices.Plot = null;
        var proposal = Assert.Single(world.Towns[0].Governance!.Proposals);
        Assert.Equal("land_claim", proposal.Kind);
        Assert.Equal(TownLandRightsRules.OrderTiles(plot), proposal.LandClaimTiles);
        Assert.Equal(3, proposal.RequiredYes);
        Assert.Equal(initial.TownLandTitles, world.TownLandTitles);
        Assert.Equal(initial.HouseholdLandUseRights, world.HouseholdLandUseRights);

        choices.Voters.UnionWith(town.ResidentIds.Take(2));
        await Until(world, () => world.Towns[0].Governance!.Proposals[0].Votes.Count == 2);
        Assert.Equal("pending", world.Towns[0].Governance!.Proposals[0].Status);
        Assert.Equal(initial.TownLandTitles, world.TownLandTitles);
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var projected = Assert.Single(snapshot.Towns[0].Governance!.Proposals);
        Assert.Contains(TownLandClaimRules.DescribeTiles(plot), projected.Text, StringComparison.Ordinal);

        world.Pause();
        var pending = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(pending), _ => new ClaimProvider(choices));
        Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        choices.Voters.Add(town.ResidentIds[2]);
        world.SubmitInstruction(new OwnerInstructionRequest("land-vote", "owner:test", town.ResidentIds[2],
            OwnerInstructionKind.Suggestive, "Consider voting on the land claim."));
        reloaded.SubmitInstruction(new OwnerInstructionRequest("land-vote", "owner:test", town.ResidentIds[2],
            OwnerInstructionKind.Suggestive, "Consider voting on the land claim."));
        world.Resume();
        reloaded.Resume();
        for (var tick = 0; tick < 60 && world.Towns[0].Governance!.Proposals[0].Status == "pending"; tick++)
        {
            var previous = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(previous, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            await world.AdvanceOneTickAsync();
            await reloaded.AdvanceOneTickAsync();
        }
        Assert.True(world.Towns[0].Governance!.Proposals[0].Status == "passed", string.Join("\n", choices.Selected.TakeLast(50)));
        var title = Assert.Single(world.TownLandTitles, t => t.Id == TownLandClaimRules.TitleId(proposal.Id));
        Assert.Equal(TownLandRightsRules.OrderTiles(plot), title.Tiles);
        Assert.All(plot, tile => Assert.Contains(tile, world.Towns[0].BorderTiles));
        Assert.Equal(initial.HouseholdLandUseRights, world.HouseholdLandUseRights);
        Assert.Equal(initial.WorldSimulation!.Buildings, world.WorldSimulation.Buildings);
        Assert.Equal(initial.Society.Society.Inventory.Lots, world.Society.Inventory.Lots);
        Assert.Equal(initial.Society.Society.Inventory.Reservations, world.Society.Inventory.Reservations);
        Assert.Single(world.ExportState().Events, e => e.Kind == "town_land_claimed");
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        var approved = world.ExportState();
        using var after = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(approved)));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(approved), PrivateWorldRuntimeCodec.Encode(after.ExportState()));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(approved with
        {
            TownLandTitles = approved.TownLandTitles!.Where(t => t.Id != title.Id).ToArray(),
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(approved with
        {
            Towns = [approved.Towns![0] with { Governance = approved.Towns[0].Governance! with { Proposals = [] } }],
        }));
    }

    [Fact]
    public async Task AnOverlappingPendingClaimIsCancelledAfterApprovalWhileUnrelatedClaimsContinue()
    {
        using var source = NormalPathWorld.CreateGenerated("council-land-overlap", _ => new ActionCoverageRecorder(chooseIdle: true));
        var initial = source.ExportState();
        var town = initial.Towns![0];
        var plot = FindPlot(initial);
        var state = Submit(town.Governance!, plot);
        state = Submit(state, [plot[0]]);
        var other = initial.Map.Tiles.Select(t => t.Position).First(tile => !plot.Contains(tile) &&
            TownLandClaimRules.CanClaim(initial.Map, town, [tile], initial.TownLandTitles!));
        state = Submit(state, [other]);
        var first = state.Proposals[0];
        foreach (var voter in town.ResidentIds.Take(2))
            state = TownGovernanceRules.VoteProposal(state, first.Id, voter, true, 0);
        foreach (var voter in town.ResidentIds)
            state = TownGovernanceRules.LearnNotices(state, voter, state.Notices.Select(n => n.Id), 0);
        var choices = new ClaimChoices { ProposalId = first.Id };
        choices.Voters.Add(town.ResidentIds[2]);
        using var world = PrivateWorldRuntime.Restore(initial with { Towns = [town with { Governance = state }] }, _ => new ClaimProvider(choices));
        await Until(world, () => world.Towns[0].Governance!.Proposals[0].Status == "passed");
        Assert.Equal("cancelled", world.Towns[0].Governance!.Proposals[1].Status);
        Assert.Equal("pending", world.Towns[0].Governance!.Proposals[2].Status);
        Assert.Single(world.ExportState().Events, e => e.Kind == "town_land_claimed");
        world.Validate();

        TownGovernanceState Submit(TownGovernanceState governance, GridPoint[] tiles) =>
            TownGovernanceRules.SubmitProposal(governance, town.Id, town.ResidentIds[0], "land_claim", null,
                "Claim adjoining land.", "same", town.ResidentIds, 0, initial.WorldSystems!.Config.TicksPerDay, tiles);
    }

    [Fact]
    public void ClaimsNeedConnectedUntitledLandTouchingTitleAndEquivalentPlotsShareOneBallot()
    {
        using var world = NormalPathWorld.CreateGenerated("council-land-boundaries", _ => new ActionCoverageRecorder(chooseIdle: true));
        var initial = world.ExportState();
        var town = initial.Towns![0];
        var titles = initial.TownLandTitles!;
        var plot = FindPlot(initial);
        bool Can(params GridPoint[] tiles) => TownLandClaimRules.CanClaim(initial.Map, town, tiles, titles);
        Assert.True(Can(plot));
        Assert.False(Can([]));
        Assert.False(Can(plot[0], plot[0]));
        Assert.False(Can(new GridPoint(-1, 0)));
        Assert.False(Can(titles[0].Tiles[0]));
        var far = initial.Map.Tiles.Select(t => t.Position).First(tile => initial.Map.IsLand(tile) &&
            !titles.Any(t => t.Tiles.Contains(tile)) && !Can(tile));
        Assert.False(Can(far));
        Assert.False(Can(plot[0], far));
        Assert.False(TownLandClaimRules.CanClaim(initial.Map, town, plot,
            titles.Append(new("foreign", "town:other", [plot[0]], 0)).ToArray()));
        var governance = Submit(town.Governance!, plot);
        governance = Submit(governance, plot.Reverse().ToArray());
        Assert.Single(governance.Proposals);
        var proposal = governance.Proposals[0];
        governance = TownGovernanceRules.Advance(governance, town.Id, initial.WorldSeed, town.ResidentIds,
            proposal.DeadlineTick, initial.WorldSystems!.Config.TicksPerDay);
        Assert.Equal("rejected", governance.Proposals[0].Status);
        Assert.Throws<InvalidOperationException>(() => TownGovernanceRules.SubmitProposal(town.Governance!, town.Id,
            "outsider", "land_claim", null, "Claim adjoining land.", "same", town.ResidentIds, 0, 10, plot));

        TownGovernanceState Submit(TownGovernanceState state, GridPoint[] tiles) =>
            TownGovernanceRules.SubmitProposal(state, town.Id, town.ResidentIds[0], "land_claim", null,
                "Claim adjoining land.", "same", town.ResidentIds, 0, initial.WorldSystems!.Config.TicksPerDay, tiles);
    }

    [Fact]
    public void ClaimAdjacencyAndConnectivityWrapEastWestButNotNorthSouth()
    {
        var tiles = Enumerable.Range(0, 3).SelectMany(y => Enumerable.Range(0, 5)
            .Select(x => new TerrainTile(new(x, y), x == 2 ? TerrainKind.Water : TerrainKind.Meadow))).ToArray();
        var map = new SeededMap(5, 3, 0, tiles, [], [], "test") { WrapsEastWest = true };
        var town = new TownRuntimeState("town:test", "Test Town", "founded", 0, [], [], [new(0, 1)], new(0, 1));
        TownLandTitleRecord[] titles = [new("title", town.Id, [new(0, 1)], 0)];
        Assert.True(TownLandClaimRules.CanClaim(map, town, [new(4, 1), new(3, 1)], titles));
        Assert.False(TownLandClaimRules.CanClaim(map with { WrapsEastWest = false }, town, [new(4, 1), new(3, 1)], titles));
        Assert.False(TownLandClaimRules.CanClaim(map, town, [new(4, 0)], titles));
        Assert.False(TownLandClaimRules.CanClaim(map, town, [new(1, 1), new(2, 1)], titles));
        Assert.False(TownLandClaimRules.CanClaim(map, town, [new(0, -1)], titles));
    }

    private static GridPoint[] FindPlot(PrivateWorldRuntimeState state)
    {
        var town = state.Towns![0];
        foreach (var tile in state.Map.Tiles.Select(t => t.Position))
        {
            if (!TownLandClaimRules.CanClaim(state.Map, town, [tile], state.TownLandTitles!)) continue;
            foreach (var next in new[] { new GridPoint(tile.X + 1, tile.Y), new GridPoint(tile.X, tile.Y + 1),
                         new GridPoint(tile.X - 1, tile.Y), new GridPoint(tile.X, tile.Y - 1) }.Select(state.Map.WrapColumn))
                if (TownLandClaimRules.CanClaim(state.Map, town, [tile, next], state.TownLandTitles!)) return [tile, next];
        }
        throw new InvalidOperationException("The test Town needs an adjoining unclaimed plot.");
    }

    private static async Task Until(PrivateWorldRuntime world, Func<bool> reached)
    {
        for (var tick = 0; tick < 60 && !reached(); tick++) await world.AdvanceOneTickAsync();
        Assert.True(reached());
    }

    private sealed class ClaimChoices
    {
        public ConcurrentQueue<string> Selected { get; } = new();
        public string? Author { get; set; }
        public GridPoint[]? Plot { get; set; }
        public string? ProposalId { get; init; }
        public HashSet<string> Voters { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ClaimProvider(ClaimChoices choices) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var o = request.Observation;
            var selected = o.Candidates.FirstOrDefault(c => c.Id.Contains("|read|", StringComparison.Ordinal)) ??
                (choices.Voters.Contains(o.InhabitantId) ? o.Candidates.FirstOrDefault(c =>
                    c.Id.Contains("|yes|", StringComparison.Ordinal) && (choices.ProposalId is null || c.Id.Contains(choices.ProposalId, StringComparison.Ordinal))) : null) ??
                (choices.Author == o.InhabitantId && choices.Plot is not null
                    ? o.Candidates.FirstOrDefault(c => c.Id.Contains("|claim_land|", StringComparison.Ordinal)) : null) ??
                o.Candidates.Single(c => c.Id == "safe_idle");
            choices.Selected.Enqueue(o.WorldTick + "|" + o.InhabitantId + "|" + selected.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, o.InhabitantId, Kind, ProviderEpoch,
                o.RunEpoch, o.DecisionGeneration, o.ObservationDigest, selected.Id, 1,
                o.Candidates.ToDictionary(c => c.Id, c => c.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandTiles: selected.Id.Contains("|claim_land|", StringComparison.Ordinal)
                    ? choices.Plot!.Select(tile => new CognitionLandTile(tile.X, tile.Y)).ToArray() : null));
        }
    }
}
