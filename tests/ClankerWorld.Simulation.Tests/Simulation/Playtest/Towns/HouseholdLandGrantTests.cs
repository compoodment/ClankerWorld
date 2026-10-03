using System.Reflection;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class HouseholdLandGrantTests
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

    [Fact]
    public void FilingRefusesAnUnrecordedBuildingAndLandTheHouseholdAlreadyHolds()
    {
        using var world = Create("household-grant-existing", new Choices());
        var town = world.Towns[0];
        var actor = town.ResidentIds[0];
        var household = world.Society.GetInhabitant(actor).HouseholdId;
        var warehouse = world.WorldSimulation.Buildings.First(b => b.TownId == town.Id && b.HouseholdId is null);
        var refused = world.RequestHouseholdLandUse("warehouse", actor, town.Id, [warehouse.Position]);
        Assert.False(refused.Applied);
        Assert.Contains("building", refused.Failure, StringComparison.Ordinal);
        var own = world.HouseholdLandUseRights.First(r => r.HouseholdId == household).Tiles[0];
        var neighbour = new[] { new GridPoint(own.X + 1, own.Y), new GridPoint(own.X - 1, own.Y), new GridPoint(own.X, own.Y + 1), new GridPoint(own.X, own.Y - 1) }
            .Select(world.ExportState().Map.WrapColumn).First(tile => world.TownLandTitles.Any(t => t.Tiles.Contains(tile)) &&
                !world.HouseholdLandUseRights.Any(r => r.Tiles.Contains(tile)));
        var overlapping = world.RequestHouseholdLandUse("overlap", actor, town.Id, [own, neighbour]);
        Assert.False(overlapping.Applied);
        Assert.Contains("already", overlapping.Failure, StringComparison.Ordinal);
        Assert.Empty(world.HouseholdLandUseRequests);
    }

    [Fact]
    public async Task TheLandRequestChoiceNamesFreeTownLandAModelCanRequest()
    {
        using var source = NormalPathWorld.CreateGenerated("household-grant-listed", _ => new Provider(new Choices()));
        var initial = source.ExportState();
        var origin = initial.Towns![0].OriginSite!.Value;
        var actor = initial.Towns[0].ResidentIds[0];
        var reader = new ListedLandProvider(actor);
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Inhabitants = initial.Inhabitants.Select(p => p with { Position = origin }).ToArray(),
        }, _ => reader);
        await Until(world, () => world.HouseholdLandUseRequests.Count == 1);
        Assert.Contains(FormattableString.Invariant($"You stand at ({origin.X}, {origin.Y})"), reader.Description, StringComparison.Ordinal);
        var listed = ListedLandProvider.ListedTiles(reader.Description!);
        Assert.InRange(listed.Length, 1, 6);
        var definitions = initial.WorldContent!.Buildings.ToDictionary(d => d.CanonicalId, StringComparer.Ordinal);
        var buildings = initial.WorldSimulation!.Buildings.SelectMany(b => WorldContentSimulationRules.Footprint(definitions[b.DefinitionId], b)).ToHashSet();
        Assert.All(listed, tile =>
        {
            Assert.Contains(initial.TownLandTitles!, t => t.TownId == initial.Towns[0].Id && t.Tiles.Contains(tile));
            Assert.DoesNotContain(initial.HouseholdLandUseRights!, r => r.Tiles.Contains(tile));
            Assert.DoesNotContain(tile, buildings);
        });
        var request = Assert.Single(world.HouseholdLandUseRequests);
        Assert.Equal([listed[0]], request.Tiles);
        Assert.NotNull(request.CouncilProposalId);
    }

    [Fact]
    public void LandAHouseholdHoldsIsNotBuiltOnByAnotherHouseholdOrTheTown()
    {
        using var world = Create("household-grant-existing", new Choices());
        var state = world.ExportState();
        var town = state.Towns![0];
        var actor = town.ResidentIds[0];
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var other = state.Society.Society.Households.First(h => h.Id != household && h.MemberIds.Count > 0).Id;
        var warehouse = state.WorldSimulation!.Buildings.First(b => b.TownId == town.Id && b.HouseholdId is null);
        var definitions = state.WorldContent!.Buildings.ToDictionary(d => d.CanonicalId, StringComparer.Ordinal);
        var houseDefinition = state.WorldContent.Buildings.First(d => d.Tags.Contains("house"));
        var layout = typeof(PrivateWorldRuntime).GetMethod("CreateTownLayoutContext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IReadOnlySet<GridPoint> Occupied(PrivateWorldRuntime runtime, BuildingDefinition building) =>
            ((TownLayoutContext)layout.Invoke(runtime, [actor, null, building])!).OccupiedTiles;
        var free = Occupied(world, definitions[warehouse.DefinitionId]);
        var beside = WorldContentSimulationRules.Footprint(definitions[warehouse.DefinitionId], warehouse)
            .SelectMany(t => new[] { new GridPoint(t.X + 1, t.Y), new GridPoint(t.X - 1, t.Y), new GridPoint(t.X, t.Y + 1), new GridPoint(t.X, t.Y - 1) })
            .Select(state.Map.WrapColumn).Where(tile => state.Map.IsLand(tile) && !free.Contains(tile) &&
                state.TownLandTitles!.Any(t => t.Tiles.Contains(tile)) && !state.HouseholdLandUseRights!.Any(r => r.Tiles.Contains(tile)))
            .Distinct().OrderBy(tile => tile.Y).ThenBy(tile => tile.X).First();
        var elsewhere = state.TownLandTitles!.Where(t => t.TownId == town.Id).SelectMany(t => t.Tiles)
            .Where(tile => tile != beside && state.Map.IsLand(tile) && !free.Contains(tile) && !state.HouseholdLandUseRights!.Any(r => r.Tiles.Contains(tile)))
            .OrderBy(tile => tile.Y).ThenBy(tile => tile.X).First();
        var mayExpand = typeof(PrivateWorldRuntime).GetMethod("MayExpandOntoLand", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True((bool)mayExpand.Invoke(world, [warehouse, beside])!);
        using var granted = PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRights = [.. state.HouseholdLandUseRights!,
                new HouseholdLandUseRight("use:test-other", town.Id, other, [beside], state.Society.Society.WorldTick, "test_grant"),
                new HouseholdLandUseRight("use:test-own", town.Id, household, [elsewhere], state.Society.Society.WorldTick, "test_grant")],
        });
        Assert.False((bool)mayExpand.Invoke(granted, [warehouse, beside])!);
        var house = Occupied(granted, houseDefinition);
        Assert.Contains(beside, house);
        Assert.DoesNotContain(elsewhere, house);
        var townBuilding = Occupied(granted, definitions[warehouse.DefinitionId]);
        Assert.Contains(beside, townBuilding);
        Assert.Contains(elsewhere, townBuilding);
    }

    // Reads the request choice's text the way a model must: it has no map grid, only the listed coordinates.
    private sealed class ListedLandProvider(string author) : IDecisionProvider
    {
        public string? Description { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public static GridPoint[] ListedTiles(string description) => System.Text.RegularExpressions.Regex
            .Matches(description[description.IndexOf("nearest you:", StringComparison.Ordinal)..], @"\((-?\d+), (-?\d+)\)")
            .Select(match => new GridPoint(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))).ToArray();

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var o = request.Observation;
            var choice = o.InhabitantId == author && Description is null
                ? o.Candidates.FirstOrDefault(c => c.Id.Contains("|request_land_use|", StringComparison.Ordinal)) : null;
            var selected = choice ?? o.Candidates.Single(c => c.Id == "safe_idle");
            Description ??= choice?.Description;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, o.InhabitantId, Kind, ProviderEpoch,
                o.RunEpoch, o.DecisionGeneration, o.ObservationDigest, selected.Id, 1,
                o.Candidates.ToDictionary(c => c.Id, c => c.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandTiles: choice is null ? null : [.. ListedTiles(choice.Description).Take(1).Select(tile => new CognitionLandTile(tile.X, tile.Y))]));
        }
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
