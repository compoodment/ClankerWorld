using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;
using GodotSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownGovernanceProjectionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task OwnerAndClientReceiveLatestEightProposalsWithoutChangingSavedHistory()
    {
        using var generated = NormalPathWorld.CreateGenerated("council-projection-proposal-history",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        for (var tick = 0; tick < 12; tick++) await generated.AdvanceOneTickAsync();
        var initial = generated.ExportState();
        var town = initial.Towns![0];
        var adults = town.ResidentIds.ToArray();
        Assert.Equal(4, adults.Length);
        var day = initial.WorldSystems!.Config.TicksPerDay;
        var governance = TownGovernanceState.Create(adults);
        for (var index = 0; index < 12; index++)
        {
            governance = TownGovernanceRules.SubmitProposal(governance, town.Id, adults[0], "law", null,
                $"Publish harvest report number {index + 1}.", "unchanged", adults, index, day);
            var proposalId = governance.Proposals[^1].Id;
            var yes = index % 3;
            foreach (var voter in adults.Take(yes))
                governance = TownGovernanceRules.VoteProposal(governance, proposalId, voter, true, index);
            if (index % 2 == 1)
                governance = TownGovernanceRules.VoteProposal(governance, proposalId, adults[yes], false, index);
            governance = TownGovernanceRules.WithdrawProposal(governance, proposalId, adults[0], index);
        }
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Towns = [town with { Governance = governance }],
        });
        world.Validate();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());

        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var client = JsonSerializer.Deserialize<GodotSnapshot>(JsonSerializer.Serialize(snapshot, JsonOptions), JsonOptions)!;

        var expected = governance.Proposals.Skip(4).Select(proposal =>
            (proposal.Id, proposal.Kind, proposal.Text, proposal.Status,
                Yes: proposal.Votes.Count(vote => vote.Yes), No: proposal.Votes.Count(vote => !vote.Yes),
                proposal.RequiredYes, proposal.DeadlineTick)).ToArray();
        var projected = Assert.Single(snapshot.Towns).Governance!.Proposals;
        var received = Assert.Single(client.Towns).Governance!.Proposals;
        Assert.Equal(8, projected.Count);
        Assert.Equal(8, received.Count);
        Assert.Equal(expected, projected.Select(proposal =>
            (proposal.Id, proposal.Kind, proposal.Text, proposal.Status, proposal.Yes, proposal.No,
                proposal.RequiredYes, proposal.DeadlineTick)));
        Assert.Equal(expected, received.Select(proposal =>
            (proposal.Id, proposal.Kind, proposal.Text, proposal.Status, proposal.Yes, proposal.No,
                proposal.RequiredYes, proposal.DeadlineTick)));
        Assert.Equal(12, world.ExportState().Towns![0].Governance!.Proposals.Count);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public async Task ActiveAndLatestArchivedElectionRemainSeparateAcrossOwnerClientJson(string archivedStage)
    {
        using var generated = NormalPathWorld.CreateGenerated("council-projection-election-history",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        for (var ordinal = 5; ordinal <= 8; ordinal++)
        {
            var setup = generated.ExportState();
            var site = setup.Towns![0].BorderTiles.First(point => setup.Map.IsBuildable(point) &&
                !setup.Map.Resources.Any(resource => resource.Position == point) &&
                !setup.Map.CampObjects.Any(item => item.Position == point) &&
                !setup.Inhabitants.Any(person => person.Position == point));
            generated.AddAgent($"agent:{ordinal:D32}", site);
        }
        var initial = generated.ExportState();
        const int day = 10;
        var oldDay = initial.Society.Society.Config.TicksPerWorldDay;
        var initialTown = initial.Towns![0];
        using var clock = PrivateWorldRuntime.Restore(initial with
        {
            // AddAgent can record an unsuccessful election at the original day
            // length. The shorter clock starts with its own civic history.
            Towns = [initialTown with { Governance = TownGovernanceState.Create(initialTown.ResidentIds) }],
            WorldSystems = RegionalWeatherRules.Initialize(initial.WorldSystems! with
            {
                Config = initial.WorldSystems.Config with { TicksPerDay = day, CalendarOffsetTicks = 0 },
                RegionalWeather = null,
            }, initial.Map),
            Society = initial.Society with
            {
                Society = initial.Society.Society with
                {
                    Config = initial.Society.Society.Config with { TicksPerWorldDay = day },
                    Inhabitants = initial.Society.Society.Inhabitants.Select(person => person with
                    {
                        BirthTick = person.BirthTick / oldDay * day,
                        BirthLifeTick = person.BirthLifeTick is { } birth ? birth / oldDay * day : null,
                    }).ToArray(),
                },
            },
        }, _ => new ActionCoverageRecorder(chooseIdle: true));
        for (var tick = 0; tick < 22; tick++) await clock.AdvanceOneTickAsync();
        var checkpoint = clock.ExportState();
        var town = checkpoint.Towns![0];
        var adults = town.ResidentIds.ToArray();
        Assert.Equal(8, adults.Length);
        var governance = TownGovernanceState.Create(adults);
        foreach (var candidate in adults.Take(3))
            governance = TownGovernanceRules.Register(governance, candidate, true, null, adults, 0);
        governance = Advance(governance, adults, 0);
        if (archivedStage == "completed")
            governance = TownGovernanceRules.VoteElection(governance, governance.Election!.Id,
                adults[0], adults.Take(3).ToArray(), 0);
        governance = Advance(governance, adults, day);
        if (archivedStage == "completed")
        {
            // The recorded demographic fallback ends representation, then a return
            // to eight adults opens a fresh initial election with the same register.
            governance = Advance(governance, adults.Take(3).ToArray(), 11);
            governance = Advance(governance, adults, 22);
        }
        else
        {
            governance = Advance(governance, adults, 20);
            if (archivedStage == "cancelled")
            {
                governance = Advance(governance, adults.Take(3).ToArray(), 21);
                governance = Advance(governance, adults, 22);
            }
        }
        governance = TownGovernanceRules.VoteElection(governance, governance.Election!.Id,
            adults[0], adults.Take(2).ToArray(), 22);
        governance = TownGovernanceRules.VoteElection(governance, governance.Election!.Id,
            adults[1], [adults[0]], 22);
        using var world = PrivateWorldRuntime.Restore(checkpoint with
        {
            Towns = [town with { Governance = governance }],
        });
        world.Validate();
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());

        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var client = JsonSerializer.Deserialize<GodotSnapshot>(JsonSerializer.Serialize(snapshot, JsonOptions), JsonOptions)!;

        var archived = governance.ElectionHistory[^1];
        var active = governance.Election!;
        Assert.Equal(archivedStage, archived.Stage);
        Assert.NotEqual(active.Id, archived.Id);
        var projected = Assert.Single(snapshot.Towns).Governance!;
        var received = Assert.Single(client.Towns).Governance!;
        Assert.NotNull(projected.Election);
        Assert.NotNull(projected.LatestElection);
        Assert.NotNull(received.Election);
        Assert.NotNull(received.LatestElection);
        var people = checkpoint.Society.Society.Inhabitants.ToDictionary(person => person.Id, person => person.Name);
        Assert.Equal((active.Id, active.Kind, active.Stage, active.Seats, active.DeadlineTick),
            (projected.Election.Id, projected.Election.Kind, projected.Election.Stage,
                projected.Election.Seats, projected.Election.DeadlineTick));
        Assert.Equal((active.Id, active.Kind, active.Stage, active.Seats, active.DeadlineTick),
            (received.Election.Id, received.Election.Kind, received.Election.Stage,
                received.Election.Seats, received.Election.DeadlineTick));
        Assert.Equal((archived.Id, archived.Kind, archived.Stage, archived.Seats, archived.DeadlineTick),
            (projected.LatestElection.Id, projected.LatestElection.Kind, projected.LatestElection.Stage,
                projected.LatestElection.Seats, projected.LatestElection.DeadlineTick));
        Assert.Equal((archived.Id, archived.Kind, archived.Stage, archived.Seats, archived.DeadlineTick),
            (received.LatestElection.Id, received.LatestElection.Kind, received.LatestElection.Stage,
                received.LatestElection.Seats, received.LatestElection.DeadlineTick));
        var activeCandidates = active.Candidates.Select(id => (id, people[id],
            Votes: active.Ballots.Count(ballot => ballot.Choices.Contains(id, StringComparer.Ordinal)))).ToArray();
        var archivedCandidates = archived.Candidates.Select(id => (id, people[id],
            Votes: archived.Ballots.Count(ballot => ballot.Choices.Contains(id, StringComparer.Ordinal)))).ToArray();
        Assert.Equal(activeCandidates, projected.Election.Candidates.Select(candidate => (candidate.Id, candidate.Name, candidate.Votes)));
        Assert.Equal(activeCandidates, received.Election.Candidates.Select(candidate => (candidate.Id, candidate.Name, candidate.Votes)));
        Assert.Equal(archivedCandidates, projected.LatestElection.Candidates.Select(candidate => (candidate.Id, candidate.Name, candidate.Votes)));
        Assert.Equal(archivedCandidates, received.LatestElection.Candidates.Select(candidate => (candidate.Id, candidate.Name, candidate.Votes)));
        Assert.Equal(archived.SettledSeats.Select(id => people[id]), projected.LatestElection.SettledNames);
        Assert.Equal(archived.SettledSeats.Select(id => people[id]), received.LatestElection.SettledNames);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        TownGovernanceState Advance(TownGovernanceState state, string[] residents, long tick) =>
            TownGovernanceRules.Advance(state, town.Id, checkpoint.WorldSeed, residents, tick, day);
    }
}
