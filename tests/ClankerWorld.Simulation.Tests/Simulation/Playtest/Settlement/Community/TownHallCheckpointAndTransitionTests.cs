using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Theory]
    [InlineData("councils")]
    [InlineData("laws")]
    [InlineData("candidacies")]
    [InlineData("votes")]
    public void NativeCodecRefusesNullCivicEntriesWithoutLeakingNullReferenceErrors(string defect)
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(nomineeIndexes: [0]));
        var actor = world.TownCouncils.Single().Election!.Candidates[0];
        Assert.True(world.VoteTownElection(actor, Town, [actor]).Applied);
        var state = world.ExportState();
        var council = state.TownCouncils!.Single();
        var invalid = state with
        {
            TownCouncils = defect switch
            {
                "councils" => [null!],
                "laws" => [council with { Laws = [null!] }],
                "candidacies" => [council with { Election = council.Election! with { Candidacies = [null!] } }],
                _ => [council with { Election = council.Election! with { Votes = [null!] } }]
            }
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalid));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalid));
        var raw = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!;
        var saved = raw["state"]!;
        if (defect == "councils") saved["townCouncils"] = JsonNode.Parse("[null]");
        else if (defect == "laws") saved["townCouncils"]![0]!["laws"] = JsonNode.Parse("[null]");
        else saved["townCouncils"]![0]!["election"]![defect] = JsonNode.Parse("[null]");
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(raw.ToJsonString())));
        world.Validate();
    }

    [Fact]
    public async Task RegularElectionKeepsAnExistingLawBallotUnderTheUnchangedIncumbents()
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(day: 12), _ => new CivicChooser(false, false));
        var adults = CivicAdults(world.Towns.Single().ResidentIds);
        var selected = adults.Take(3).ToArray();
        foreach (var voter in adults) Assert.True(world.VoteTownElection(voter, Town, selected).Applied);
        await AdvanceTo(world, 12);
        Assert.Equal(132, world.TownCouncils.Single().TermExpiryTick);
        await AdvanceTo(world, 119);
        Assert.True(world.ProposeTownLaw(adults.Last(), Town, "quiet_meetings", "Let speakers finish.").Applied);
        var pending = world.TownCouncils.Single().Ballot!;
        Assert.Empty(pending.Approvals);
        await AdvanceTo(world, 120);
        Assert.NotNull(world.TownCouncils.Single().Election);
        Assert.Equal(selected, world.TownCouncils.Single().MemberIds);
        Assert.Equal(pending, world.TownCouncils.Single().Ballot);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "town_law_cancelled");
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new CivicChooser(false, false));
        Assert.False(loaded.ProposeTownLaw(adults.Last(), Town, "another_rule", "Do not overlap proposals.").Applied);
        Assert.True(loaded.VoteTownLaw(selected[0], Town, true).Applied);
        Assert.Empty(loaded.TownCouncils.Single().Laws!);
        Assert.True(loaded.VoteTownLaw(selected[1], Town, true).Applied);
        Assert.Null(loaded.TownCouncils.Single().Ballot);
        Assert.NotNull(loaded.TownCouncils.Single().Election);
        Assert.Equal("quiet_meetings", Assert.Single(loaded.TownCouncils.Single().Laws!).Key);
        loaded.Validate();
    }

    [Fact]
    public void AWithdrawnNomineeLosesAllBallotSupportAcrossReloadAndMayVolunteerAgain()
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(), _ => new CivicChooser(false, false));
        var adults = CivicAdults(world.Towns.Single().ResidentIds);
        Assert.True(world.VoteTownElection(adults[0], Town, adults.Take(3).ToArray()).Applied);
        Assert.True(world.VolunteerTownCouncil(adults[2], Town, false).Applied);
        var election = world.TownCouncils.Single().Election!;
        Assert.DoesNotContain(adults[2], election.Candidates);
        Assert.DoesNotContain(election.Candidacies, item => item.CandidateId == adults[2]);
        Assert.Equal(adults.Take(2).Order(StringComparer.Ordinal), Assert.Single(election.Votes).CandidateIds);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.False(loaded.VoteTownElection(adults[0], Town, [adults[2]]).Applied);
        Assert.True(loaded.VolunteerTownCouncil(adults[2], Town).Applied);
        Assert.DoesNotContain(adults[2], loaded.TownCouncils.Single().Election!.Candidates);
        Assert.Contains(loaded.TownCouncils.Single().CandidateRegister, item => item.CandidateId == adults[2]);
        Assert.Equal(adults.Take(2).Order(StringComparer.Ordinal), Assert.Single(loaded.TownCouncils.Single().Election!.Votes).CandidateIds);
        loaded.Validate();
    }
}
