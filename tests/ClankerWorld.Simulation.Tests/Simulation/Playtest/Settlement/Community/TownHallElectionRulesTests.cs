using System.Collections.Concurrent;
using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FewerThanThreeSupportedWillingNomineesKeepsTheAdultCouncilAndSchedulesAOneDayRetry(int winners)
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(nomineeIndexes: Enumerable.Range(0, winners + 1).ToArray()), _ => new CivicChooser(false, false));
        var adults = CivicAdults(world.Towns.Single().ResidentIds);
        Assert.False(world.VoteTownElection(adults[0], Town, [adults[3]]).Applied);
        Assert.True(world.VoteTownElection(adults[0], Town, adults.Take(winners).ToArray()).Applied);
        await AdvanceTo(world, 23);
        Assert.NotNull(world.TownCouncils.Single().Election);
        Assert.Null(world.TownCouncils.Single().TermStartedTick);
        await AdvanceTo(world, 24);
        var elected = world.TownCouncils.Single();
        Assert.Equal(adults.Order(StringComparer.Ordinal), elected.MemberIds.Order(StringComparer.Ordinal));
        Assert.Equal(adults.Take(winners).Order(StringComparer.Ordinal), elected.LastElectionOutcome!.SelectedMemberIds.Order(StringComparer.Ordinal));
        Assert.Equal("collective", elected.GoverningForm);
        Assert.Equal("candidate", elected.FallbackReason);
        Assert.Null(elected.Election);
        Assert.Null(elected.TermStartedTick);
        Assert.Null(elected.TermExpiryTick);
        Assert.Equal(48, elected.ElectionRetryAfterTick);
        await AdvanceTo(world, 25);
        Assert.Null(world.TownCouncils.Single().Election);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(8, loaded.TownCouncils.Single().MemberIds.Count);
        loaded.Validate();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CutoffTiePreservesSelectedSeatsAndUsesARecordedFairDrawAfterOneDayRunoffAcrossReload(bool resolved)
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(nomineeIndexes: [0, 1, 2, 3]), _ => new CivicChooser(false, false));
        var adults = CivicAdults(world.Towns.Single().ResidentIds);
        var nominees = adults.Take(4).ToArray();
        for (var index = 0; index < 4; index++)
            Assert.True(world.VoteTownElection(adults[index], Town, [nominees[0], nominees[1], nominees[2 + index % 2]]).Applied);
        Assert.True(world.VoteTownElection(adults[4], Town, [nominees[0]]).Applied);
        await AdvanceTo(world, 24);
        var pending = world.TownCouncils.Single().Election!;
        Assert.True(pending.IsRunoff);
        Assert.Equal(nominees.Take(2), pending.SelectedMemberIds);
        Assert.Equal(nominees.Skip(2), pending.Candidates);
        Assert.Equal(1, pending.AvailableSeats);
        Assert.Empty(pending.Votes);
        Assert.Equal(48, pending.ExpiryTick);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<OwnerWorldSnapshot>(JsonSerializer.Serialize(new OwnerWorldObservationStore(world).GetSnapshot(), options), options)!;
        var visible = Assert.Single(client.TownCouncils);
        Assert.True(visible.ElectionIsRunoff);
        Assert.Equal(1, visible.ElectionAvailableSeats);
        Assert.Equal(nominees.Skip(2).Select(id => world.Society.GetInhabitant(id).Name), visible.ElectionCandidateNames);
        Assert.Equal(nominees.Take(2).Select(id => world.Society.GetInhabitant(id).Name), visible.ElectionSelectedMemberNames);
        Assert.Equal("Runoff: 0/8 ballots \u00b7 1 seat available", GameUiText.TownElectionSummary(visible));
        Assert.Contains("one-day runoff", WorldEventText.Describe(new(1, 24, "town_election_runoff_started", Town), client));
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new CivicChooser(false, false));
        Assert.True(loaded.VolunteerTownCouncil(adults[5], Town).Applied);
        Assert.DoesNotContain(adults[5], loaded.TownCouncils.Single().Election!.Candidates);
        Assert.False(loaded.VoteTownElection(adults[0], Town, [nominees[0]]).Applied);
        Assert.False(loaded.VoteTownElection(adults[0], Town, nominees.Skip(2).ToArray()).Applied);
        Assert.True(loaded.VoteTownElection(adults[0], Town, [nominees[2]]).Applied);
        Assert.True(loaded.VoteTownElection(adults[1], Town, [nominees[3]]).Applied);
        if (resolved) Assert.True(loaded.VoteTownElection(adults[2], Town, [nominees[2]]).Applied);
        await AdvanceTo(loaded, 47);
        Assert.NotNull(loaded.TownCouncils.Single().Election);
        var beforeDraw = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(beforeDraw, _ => new CivicChooser(false, false));
        await AdvanceTo(loaded, 48);
        await AdvanceTo(replay, 48);
        var elected = loaded.TownCouncils.Single();
        Assert.Equal(3, elected.MemberIds.Count);
        Assert.Equal(nominees.Take(2), elected.MemberIds.Take(2));
        Assert.Contains(elected.MemberIds[2], nominees.Skip(2));
        if (resolved) Assert.Equal(nominees[2], elected.MemberIds[2]);
        Assert.Equal(resolved ? 0 : 1, elected.LastElectionOutcome!.DrawnMemberIds.Count);
        Assert.Equal(JsonSerializer.Serialize(elected.LastElectionOutcome), JsonSerializer.Serialize(replay.TownCouncils.Single().LastElectionOutcome));
        Assert.Equal(loaded.ExportState().Events, replay.ExportState().Events);
        Assert.Null(elected.Election);
        Assert.Equal(48, elected.TermStartedTick);
        Assert.Equal(288, elected.TermExpiryTick);
        loaded.Validate();
    }

    [Fact]
    public async Task BallotsAreRevisableDistinctAndSelfVotesAreAllowedUntilTheExactDeadline()
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(), _ => new CivicChooser(false, false));
        var adults = CivicAdults(world.Towns.Single().ResidentIds);
        Assert.True(world.VoteTownElection(adults[0], Town, [adults[0]]).Applied);
        Assert.True(world.VoteTownElection(adults[0], Town, [adults[1], adults[2]]).Applied);
        Assert.Single(world.TownCouncils.Single().Election!.Votes);
        Assert.True(world.VoteTownElection(adults[0], Town, []).Applied);
        Assert.Empty(world.TownCouncils.Single().Election!.Votes.Single().CandidateIds);
        Assert.True(world.VoteTownElection(adults[0], Town, [adults[0]]).Applied);
        Assert.False(world.VoteTownElection(adults[1], Town, [adults[0], adults[0]]).Applied);
        foreach (var actor in adults.Skip(1)) Assert.True(world.VoteTownElection(actor, Town, [adults[0]]).Applied);
        Assert.NotNull(world.TownCouncils.Single().Election);
        await AdvanceTo(world, 24);
        Assert.Equal([adults[0]], world.TownCouncils.Single().LastElectionOutcome!.SelectedMemberIds);
        Assert.Equal("candidate", world.TownCouncils.Single().FallbackReason);
        Assert.False(world.VoteTownElection(adults[0], Town, [adults[1]]).Applied);
        Assert.False(world.VolunteerTownCouncil(adults[1], Town).Applied);
        world.Validate();
    }

    [Fact]
    public async Task ElectionKeepsStartingVotersExcludesNewVotersAndRemovesActualDeadCandidatesAndTheirVotes()
    {
        var state = RegisteredElection(extra: 5);
        var adults = CivicAdults(state.Towns!.Single().ResidentIds);
        using (var setup = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false)))
        {
            Assert.True(setup.VoteTownElection(adults[0], Town, adults.Take(3).ToArray()).Applied);
            Assert.True(setup.VoteTownElection(adults[1], Town, adults.Take(3).ToArray()).Applied);
            var house = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
            Assert.Equal(Alpha, setup.AddAgent(ExtraAdultId(9), house.Position));
            state = AtHall(setup.ExportState());
        }
        using (var setup = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false)))
        {
            Assert.DoesNotContain(ExtraAdultId(9), setup.TownCouncils.Single().Election!.Electorate);
            Assert.True(setup.VolunteerTownCouncil(ExtraAdultId(9), Town).Applied);
            Assert.DoesNotContain(ExtraAdultId(9), setup.TownCouncils.Single().Election!.Candidates);
            Assert.False(setup.VoteTownElection(ExtraAdultId(9), Town, [ExtraAdultId(9)]).Applied);
            state = setup.ExportState();
        }
        using var world = PrivateWorldRuntime.Restore(DiesNextTick(state, adults[0]), _ => new CivicChooser(false, false));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var pending = world.TownCouncils.Single().Election!;
        Assert.DoesNotContain(adults[0], pending.Electorate);
        Assert.DoesNotContain(adults[0], pending.Candidates);
        Assert.DoesNotContain(pending.Candidacies, item => item.CandidateId == adults[0]);
        var retained = Assert.Single(pending.Votes);
        Assert.Equal(adults[1], retained.VoterId);
        Assert.Equal(adults.Skip(1).Take(2).Order(StringComparer.Ordinal), retained.CandidateIds);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new CivicChooser(false, false));
        Assert.DoesNotContain(ExtraAdultId(9), loaded.TownCouncils.Single().Election!.Electorate);
        await AdvanceTo(loaded, 24);
        Assert.Equal(adults.Skip(1).Take(2).Order(StringComparer.Ordinal), loaded.TownCouncils.Single().LastElectionOutcome!.SelectedMemberIds.Order(StringComparer.Ordinal));
        Assert.Equal("candidate", loaded.TownCouncils.Single().FallbackReason);
        Assert.Equal(9, loaded.TownCouncils.Single().MemberIds.Count);
        loaded.Validate();
    }

    [Fact]
    public async Task AChangedCouncilCancelsItsPendingLawBallotAndAllowsAnExplicitResubmission()
    {
        var state = AtHall(WithHall(Initial()));
        var adults = CivicAdults(state.Towns!.Single().ResidentIds);
        using (var setup = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false)))
        {
            Assert.True(setup.ProposeTownLaw(adults[0], Town, "quiet_meetings", "Let speakers finish.").Applied);
            Assert.True(setup.VoteTownLaw(adults[1], Town, true).Applied);
            state = setup.ExportState();
            var ballot = state.TownCouncils!.Single().Ballot!;
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
            { TownCouncils = [state.TownCouncils!.Single() with { Ballot = ballot with { Electorate = adults.Take(3).ToArray() } }] }));
        }
        using var world = PrivateWorldRuntime.Restore(DiesNextTick(state, adults[3]), _ => new CivicChooser(false, false));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Null(world.TownCouncils.Single().Ballot);
        Assert.Empty(world.TownCouncils.Single().Laws!);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "town_law_cancelled");
        Assert.True(world.ProposeTownLaw(adults[0], Town, "quiet_meetings", "Let speakers finish.").Applied);
        Assert.Empty(world.TownCouncils.Single().Ballot!.Approvals);
        Assert.True(world.VoteTownLaw(adults[0], Town, true).Applied);
        Assert.True(world.VoteTownLaw(adults[1], Town, true).Applied);
        Assert.Single(world.TownCouncils.Single().Laws!);
        world.Validate();
    }

    [Fact]
    public void InventedCandidaciesRunoffSeatsDeadlinesAndSupportAreRefusedByTheCodec()
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(nomineeIndexes: [0]));
        var actor = world.TownCouncils.Single().Election!.Candidates[0];
        var state = world.ExportState();
        var council = state.TownCouncils!.Single();
        var election = council.Election!;
        var forged = new[]
        {
            election with { Candidacies = [] },
            election with { Candidates = election.Candidates.Append("absent-person").ToArray(), Candidacies = election.Candidacies.Append(new("absent-person", 0)).ToArray() },
            election with { AvailableSeats = 4 },
            election with { ExpiryTick = election.ExpiryTick + 1 },
            election with { IsRunoff = true, ExpiryTick = 48 },
            election with { Candidacies = [new(actor, 24)] },
            election with { Votes = [new(actor, [world.Towns.Single().ResidentIds[1]])] }
        };
        foreach (var invalid in forged) Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        { TownCouncils = [council with { Election = invalid }] }));
        world.Validate();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ModelReadsOnlyAtTheHallAndSupportsOneActualNamedWillingCandidate(bool atHall, bool invented)
    {
        var state = RegisteredElection();
        var adults = CivicAdults(state.Towns!.Single().ResidentIds);
        using (var setup = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false)))
        {
            state = setup.ExportState();
        }
        // This voter is first in the real saved scheduler's four-person dispatch.
        var actor = state.Towns!.Single().ResidentIds[0];
        if (!atHall)
        {
            var footprint = WorldContentSimulationRules.Footprint(TownHallContent.TownHall(), state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "test-town-hall")).ToArray();
            var occupied = state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position).ToHashSet();
            var remote = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsPassable(point) && !occupied.Contains(point) &&
                !state.Map.Resources.Any(resource => resource.Position == point) && footprint.All(tile => state.Map.FootDistance(point, tile) > 1) && footprint.Any(tile => state.Map.FootDistance(point, tile) <= 4));
            state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = remote } : person).ToArray() };
        }
        var chosen = invented ? "absent-person" : state.TownCouncils!.Single().Election!.Candidates[2];
        var provider = new ElectionModelRecorder("council_town_support:" + chosen);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new CivicChooser(false, false));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var request = Assert.Single(provider.Requests);
        Assert.DoesNotContain(request.Observation.Candidates, item => item.Id == "council_town_elect");
        var support = request.Observation.Candidates.Where(item => item.Id.StartsWith("council_town_support:", StringComparison.Ordinal)).ToArray();
        if (atHall)
        {
            Assert.Equal(3, support.Length);
            Assert.All(support, item => Assert.Contains(world.Society.GetInhabitant(item.Id["council_town_support:".Length..]).Name, item.Description));
        }
        else
        {
            Assert.Empty(support);
            Assert.Contains(request.Observation.Candidates, item => item.Id == "council_town_visit");
        }
        var vote = world.TownCouncils.Single().Election!.Votes.FirstOrDefault(item => item.VoterId == actor);
        if (atHall && !invented) Assert.Equal([chosen], Assert.IsType<TownElectionVote>(vote).CandidateIds);
        else Assert.Null(vote);
        Assert.Single(provider.Requests);
        world.Validate();
    }

    private sealed class ElectionModelRecorder(string candidate) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ConcurrentQueue<CognitionDecisionRequest> Requests { get; } = new();
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, candidate, 1, new Dictionary<string, double> { [candidate] = 1 }));
        }
    }
}
