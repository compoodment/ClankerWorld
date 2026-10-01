using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Fact]
    public async Task OneActualVoterMaySeatThreeSupportedVolunteersWithoutAMinimumTurnout()
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(), _ => new CivicChooser(false, false));
        var adults = CivicAdults(world.Towns.Single().ResidentIds);
        Assert.True(world.VoteTownElection(adults[0], Town, adults.Take(3).ToArray()).Applied);
        await AdvanceTo(world, 24);
        var council = world.TownCouncils.Single();
        Assert.Equal("representative", council.GoverningForm);
        Assert.Equal(adults.Take(3), council.MemberIds);
        Assert.Single(council.LastElectionOutcome!.MainVotes);
        Assert.Equal(24, council.TermStartedTick);
        Assert.Equal(264, council.TermExpiryTick);
        AssertCivicRoundTrip(world);
    }

    [Fact]
    public async Task ARunoffUsesFreshVotersButTheOriginalSupportedSlateAndZeroBallotsStillProduceOneRecordedDraw()
    {
        using var first = PrivateWorldRuntime.Restore(RegisteredElection(nomineeIndexes: [0, 1, 2, 3]), _ => new CivicChooser(false, false));
        var adults = CivicAdults(first.Towns.Single().ResidentIds);
        for (var index = 0; index < 4; index++)
            Assert.True(first.VoteTownElection(adults[index], Town, [adults[0], adults[1], adults[2 + index % 2]]).Applied);
        Assert.True(first.VoteTownElection(adults[4], Town, [adults[0]]).Applied);
        await AdvanceTo(first, 23);
        var newcomer = ExtraAdultId(99);
        var house = first.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        Assert.Equal(Alpha, first.AddAgent(newcomer, house.Position));
        using var world = PrivateWorldRuntime.Restore(AtHall(first.ExportState()), _ => new CivicChooser(false, false));
        Assert.True(world.VolunteerTownCouncil(newcomer, Town).Applied);
        Assert.DoesNotContain(newcomer, world.TownCouncils.Single().Election!.Candidates);
        Assert.DoesNotContain(newcomer, world.TownCouncils.Single().Election!.Electorate);
        await AdvanceTo(world, 24);
        var runoff = world.TownCouncils.Single().Election!;
        Assert.Contains(newcomer, runoff.Electorate);
        Assert.Equal(adults.Skip(2).Take(2), runoff.Candidates);
        Assert.DoesNotContain(newcomer, runoff.Candidates);
        Assert.Empty(runoff.Votes);
        await AdvanceTo(world, 47);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new CivicChooser(false, false));
        await AdvanceTo(world, 48);
        await AdvanceTo(replay, 48);
        var council = world.TownCouncils.Single();
        var outcome = council.LastElectionOutcome!;
        Assert.Empty(outcome.RunoffVotes);
        var drawn = Assert.Single(outcome.DrawnMemberIds);
        Assert.Contains(drawn, adults.Skip(2).Take(2));
        Assert.Contains(drawn, outcome.SupportedCandidateIds);
        Assert.Equal(adults.Take(2), council.MemberIds.Take(2));
        Assert.Equal(3, council.MemberIds.Count);
        Assert.Equal(48, council.TermStartedTick);
        Assert.Equal(288, council.TermExpiryTick);
        Assert.Equal(JsonSerializer.Serialize(outcome), JsonSerializer.Serialize(replay.TownCouncils.Single().LastElectionOutcome));
        Assert.Equal(world.ExportState().Events, replay.ExportState().Events);
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_election_draw_recorded");
        var state = world.ExportState();
        foreach (var invalid in new[]
                 {
                     outcome with { DrawnMemberIds = [newcomer] },
                     outcome with { MainVotes = [] },
                     outcome with { DrawSlate = [drawn] },
                     outcome with { RunoffCandidateIds = [newcomer] },
                     outcome with { AdultIdsAtResolution = [] }
                 })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
            { TownCouncils = [council with { LastElectionOutcome = invalid }] }));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<OwnerWorldSnapshot>(JsonSerializer.Serialize(new OwnerWorldObservationStore(world).GetSnapshot(), options), options)!;
        Assert.Equal("representative", client.TownCouncils.Single().GoverningForm);
        Assert.Equal([world.Society.GetInhabitant(drawn).Name], client.TownCouncils.Single().LastDrawMemberNames);
        Assert.Contains(client.TownCouncils.Single().CandidateRegister, item => item.Name == world.Society.GetInhabitant(newcomer).Name && item.FullTermWilling);
        AssertCivicRoundTrip(world);
    }

    [Fact]
    public async Task AReplacementNeedsItsOwnConsentAndServesOnlyTheOriginalTermRemainder()
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(), _ => new CivicChooser(false, false));
        var adults = CivicAdults(world.Towns.Single().ResidentIds);
        Assert.True(world.VoteTownElection(adults[0], Town, adults.Take(3).ToArray()).Applied);
        await AdvanceTo(world, 24);
        Assert.True(world.VolunteerTownCouncil(adults[3], Town, replacement: true).Applied);
        Assert.True(world.ResignTownCouncil(adults[0], Town).Applied);
        var pending = world.TownCouncils.Single().Election!;
        Assert.Equal("replacement", pending.Kind);
        Assert.Equal(adults.Skip(1).Take(2), pending.RetainedMemberIds);
        Assert.Equal(1, pending.MainAvailableSeats);
        Assert.Equal([adults[3]], pending.Candidates);
        Assert.False(world.VoteTownElection(adults[0], Town, [adults[3], adults[1]]).Applied);
        Assert.True(world.VoteTownElection(adults[0], Town, [adults[3]]).Applied);
        await AdvanceTo(world, 47);
        Assert.DoesNotContain(adults[3], world.TownCouncils.Single().MemberIds);
        await AdvanceTo(world, 48);
        Assert.Equal(adults.Skip(1).Take(3), world.TownCouncils.Single().MemberIds);
        Assert.Equal(24, world.TownCouncils.Single().TermStartedTick);
        Assert.Equal(264, world.TownCouncils.Single().TermExpiryTick);
        AssertCivicRoundTrip(world);
        await AdvanceTo(world, 240);
        Assert.Equal("regular", world.TownCouncils.Single().Election!.Kind);
        Assert.DoesNotContain(adults[3], world.TownCouncils.Single().Election!.Candidates);
        Assert.True(world.TownCouncils.Single().CandidateRegister.Single(item => item.CandidateId == adults[3]).ReplacementWilling);
        Assert.False(world.TownCouncils.Single().CandidateRegister.Single(item => item.CandidateId == adults[3]).FullTermWilling);
        var regular = world.TownCouncils.Single().Election!;
        Assert.True(world.VoteTownElection(adults[0], Town, regular.Candidates.Take(3).ToArray()).Applied);
        await AdvanceTo(world, 264);
        Assert.DoesNotContain(world.TownCouncils.Single().CandidateRegister, item => item.CandidateId == adults[3]);
        Assert.True(world.VolunteerTownCouncil(adults[3], Town, replacement: true).Applied);
        var fresh = world.TownCouncils.Single().CandidateRegister.Single(item => item.CandidateId == adults[3]);
        Assert.Equal(264, fresh.ReplacementTermStartedTick);
        Assert.Equal(504, fresh.ReplacementTermExpiryTick);
        Assert.Equal(264, fresh.ReplacementDeclaredTick);
        var state = world.ExportState();
        var current = state.TownCouncils!.Single();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        {
            TownCouncils = [current with { CandidateRegister = current.CandidateRegister.Select(item => item.CandidateId == adults[3]
            ? item with { ReplacementTermStartedTick = 24, ReplacementTermExpiryTick = 264 } : item).ToArray() }]
        }));
        AssertCivicRoundTrip(world);
    }

    [Fact]
    public async Task AStillResidentIncumbentWhoResignsDuringReplacementVotingCannotBeReappointedAsARetainedSeat()
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(), _ => new CivicChooser(false, false));
        var adults = CivicAdults(world.Towns.Single().ResidentIds);
        Assert.True(world.VoteTownElection(adults[0], Town, adults.Take(3).ToArray()).Applied);
        await AdvanceTo(world, 24);
        foreach (var nominee in adults.Skip(3).Take(2)) Assert.True(world.VolunteerTownCouncil(nominee, Town, replacement: true).Applied);
        Assert.True(world.ResignTownCouncil(adults[0], Town).Applied);
        Assert.True(world.ResignTownCouncil(adults[1], Town).Applied);
        var council = world.TownCouncils.Single();
        Assert.Equal([adults[2]], council.Election!.RetainedMemberIds);
        Assert.Equal(2, council.Election.MainAvailableSeats);
        var state = world.ExportState();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        { TownCouncils = [council with { Election = council.Election with { RetainedMemberIds = adults.Skip(1).Take(2).ToArray(), AvailableSeats = 1, MainAvailableSeats = 1 } }] }));
        Assert.True(world.VoteTownElection(adults[0], Town, adults.Skip(3).Take(2).ToArray()).Applied);
        await AdvanceTo(world, 48);
        Assert.Equal(adults.Skip(2).Take(3).Order(StringComparer.Ordinal), world.TownCouncils.Single().MemberIds.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(adults[0], world.TownCouncils.Single().MemberIds);
        Assert.DoesNotContain(adults[1], world.TownCouncils.Single().MemberIds);
        AssertCivicRoundTrip(world);
    }

    [Fact]
    public async Task RegularVotingCancelsOnlyAnUnresolvedReplacementRunoffAndKeepsItsAlreadySelectedOldTermSeat()
    {
        using var world = PrivateWorldRuntime.Restore(RegisteredElection(day: 12), _ => new CivicChooser(false, false));
        var adults = CivicAdults(world.Towns.Single().ResidentIds);
        Assert.True(world.VoteTownElection(adults[0], Town, adults.Take(3).ToArray()).Applied);
        await AdvanceTo(world, 12);
        foreach (var nominee in adults.Skip(3).Take(3)) Assert.True(world.VolunteerTownCouncil(nominee, Town, replacement: true).Applied);
        await AdvanceTo(world, 107);
        Assert.True(world.ResignTownCouncil(adults[0], Town).Applied);
        Assert.True(world.ResignTownCouncil(adults[1], Town).Applied);
        for (var index = 0; index < 4; index++)
            Assert.True(world.VoteTownElection(adults[index], Town, [adults[3], adults[4 + index % 2]]).Applied);
        Assert.True(world.VoteTownElection(adults[4], Town, [adults[3]]).Applied);
        await AdvanceTo(world, 119);
        var replacement = world.TownCouncils.Single();
        Assert.True(replacement.Election!.IsRunoff);
        Assert.Equal([adults[3]], replacement.Election.SelectedMemberIds);
        Assert.Equal([adults[2], adults[3]], replacement.MemberIds);
        AssertCivicRoundTrip(world);
        await AdvanceTo(world, 120);
        var regular = world.TownCouncils.Single();
        Assert.Equal("regular", regular.Election!.Kind);
        Assert.False(regular.Election.IsRunoff);
        Assert.Empty(regular.Election.Votes);
        Assert.Equal(replacement.MemberIds, regular.MemberIds);
        Assert.Equal(132, regular.TermExpiryTick);
        Assert.DoesNotContain(adults[3], regular.Election.Candidates);
        Assert.Equal(3, regular.Election.AvailableSeats);
        AssertCivicRoundTrip(world);
    }

    [Fact]
    public async Task CandidateFallbackMayRetryBelowEightWhileAPopulationExitMustWaitForEightAgain()
    {
        using var first = PrivateWorldRuntime.Restore(RegisteredElection(), _ => new CivicChooser(false, false));
        var adults = CivicAdults(first.Towns.Single().ResidentIds);
        await AdvanceTo(first, 24);
        Assert.Equal("candidate", first.TownCouncils.Single().FallbackReason);
        Assert.Equal(48, first.TownCouncils.Single().ElectionRetryAfterTick);
        using var smaller = PrivateWorldRuntime.Restore(DiesNextTick(first.ExportState(), adults[7]), _ => new CivicChooser(false, false));
        Assert.True((await smaller.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(7, smaller.TownCouncils.Single().MemberIds.Count);
        Assert.Null(smaller.TownCouncils.Single().Election);
        AssertCivicRoundTrip(smaller);
        await AdvanceTo(smaller, 48);
        Assert.Equal(7, smaller.TownCouncils.Single().Election!.Electorate.Count);
        Assert.Equal("initial", smaller.TownCouncils.Single().Election!.Kind);
        var state = smaller.ExportState();
        foreach (var actor in adults.Skip(3).Take(4))
        {
            if (actor == adults[7]) continue;
            using var mortality = PrivateWorldRuntime.Restore(DiesNextTick(state, actor), _ => new CivicChooser(false, false));
            Assert.True((await mortality.AdvanceOneTickAsync()).Advanced);
            state = mortality.ExportState();
        }
        using var reduced = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        Assert.Equal(3, reduced.TownCouncils.Single().MemberIds.Count);
        Assert.Equal("population", reduced.TownCouncils.Single().FallbackReason);
        Assert.Null(reduced.TownCouncils.Single().Election);
        var house = reduced.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        for (var index = 0; index < 4; index++) reduced.AddAgent(ExtraAdultId(120 + index), house.Position);
        Assert.Equal(7, reduced.TownCouncils.Single().MemberIds.Count);
        Assert.Null(reduced.TownCouncils.Single().Election);
        reduced.AddAgent(ExtraAdultId(124), house.Position);
        Assert.Equal(8, reduced.TownCouncils.Single().Election!.Electorate.Count);
        AssertCivicRoundTrip(reduced);
    }

    private static void AssertCivicRoundTrip(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new CivicChooser(false, false));
        loaded.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }
}
