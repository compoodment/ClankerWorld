using System.Text.Json;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Fact]
    public async Task ACurrentRunoffCannotReplaceItsActualMainWinnersWithSupportedCutoffCandidates()
    {
        using var world = await ActualRankedRunoffAsync();
        AssertCivicRoundTrip(world);
        var state = world.ExportState();
        var council = state.TownCouncils!.Single();
        var runoff = council.Election!;
        var winners = runoff.SelectedMemberIds.ToArray();
        var tied = runoff.Candidates.ToArray();
        Assert.Equal(2, winners.Length);
        Assert.Equal(2, tied.Length);
        Assert.Equal(1, runoff.AvailableSeats);
        Assert.Empty(runoff.Votes);
        Assert.All(winners.Concat(tied), candidate =>
            Assert.Contains(runoff.MainVotes, vote => vote.CandidateIds.Contains(candidate, StringComparer.Ordinal)));

        // All four are real willing candidates with actual main support. Only the
        // main ranking distinguishes the settled winners from the cutoff slate.
        var changed = runoff with { SelectedMemberIds = tied, Candidates = winners };
        Assert.Equal(JsonSerializer.Serialize(runoff.MainVotes), JsonSerializer.Serialize(changed.MainVotes));
        Assert.Equal(runoff.MainSupportedCandidateIds, changed.MainSupportedCandidateIds);
        Assert.Equal(runoff.Candidacies, changed.Candidacies);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        { TownCouncils = [council with { Election = changed }] }));
        AssertCivicRoundTrip(world);
    }

    [Fact]
    public async Task ARecordedRunoffCannotClaimLowerMainRanksWereAlreadyElectedBeforeTheTie()
    {
        using var world = await ActualRankedRunoffAsync();
        await AdvanceTo(world, 47);
        using var replay = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new CivicChooser(false, false));
        await AdvanceTo(world, 48);
        await AdvanceTo(replay, 48);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
            PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        AssertCivicRoundTrip(world);

        var state = world.ExportState();
        var council = state.TownCouncils!.Single();
        var outcome = council.LastElectionOutcome!;
        var winners = outcome.BeforeRunoffSelectedIds.Order(StringComparer.Ordinal).ToArray();
        var tied = outcome.RunoffCandidateIds.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(2, winners.Length);
        Assert.Equal(2, tied.Length);
        Assert.Empty(outcome.RunoffVotes);
        Assert.Equal(tied, outcome.DrawSlate);
        var actuallyDrawn = Assert.Single(outcome.DrawnMemberIds);
        var drawnIndex = Array.IndexOf(tied, actuallyDrawn);
        Assert.InRange(drawnIndex, 0, 1);
        Assert.All(winners.Concat(tied), candidate => Assert.Contains(candidate, outcome.SupportedCandidateIds));

        // Keep the same actual draw index, stream identity and two-entry sorted
        // slate. This avoids a rejection caused merely by an inconsistent draw.
        // The forged result instead contradicts the untouched real main votes.
        var substitutedDraw = winners[drawnIndex];
        var changed = outcome with
        {
            BeforeRunoffSelectedIds = tied,
            RunoffCandidateIds = winners,
            DrawSlate = winners,
            DrawnMemberIds = [substitutedDraw],
            SelectedMemberIds = [.. tied, substitutedDraw]
        };
        Assert.Equal(JsonSerializer.Serialize(outcome.MainVotes), JsonSerializer.Serialize(changed.MainVotes));
        Assert.Equal(outcome.SupportedCandidateIds, changed.SupportedCandidateIds);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        {
            TownCouncils = [council with
            {
                MemberIds = changed.SelectedMemberIds,
                LastElectionOutcome = changed
            }]
        }));
        AssertCivicRoundTrip(world);
    }

    [Fact]
    public async Task AReplacementWinnerWhoResignsDuringItsRunoffStaysResignedAcrossReloadAndResolution()
    {
        using var first = PrivateWorldRuntime.Restore(RegisteredElection(), _ => new CivicChooser(false, false));
        var adults = CivicAdults(first.Towns.Single().ResidentIds);
        Assert.True(first.VoteTownElection(adults[0], Town, adults.Take(3).ToArray()).Applied);
        await AdvanceTo(first, 24);
        var original = first.TownCouncils.Single();
        Assert.Equal("representative", original.GoverningForm);
        Assert.Equal(24, original.TermStartedTick);
        Assert.Equal(264, original.TermExpiryTick);
        foreach (var nominee in adults.Skip(3).Take(3))
            Assert.True(first.VolunteerTownCouncil(nominee, Town, replacement: true).Applied);
        Assert.True(first.ResignTownCouncil(adults[0], Town).Applied);
        Assert.True(first.ResignTownCouncil(adults[1], Town).Applied);
        var replacement = first.TownCouncils.Single().Election!;
        Assert.Equal("replacement", replacement.Kind);
        Assert.Equal([adults[2]], replacement.RetainedMemberIds);
        Assert.Equal(2, replacement.AvailableSeats);
        for (var index = 0; index < 4; index++)
            Assert.True(first.VoteTownElection(adults[index], Town, [adults[3], adults[4 + index % 2]]).Applied);
        Assert.True(first.VoteTownElection(adults[4], Town, [adults[3]]).Applied);
        await AdvanceTo(first, 48);
        var pending = first.TownCouncils.Single();
        Assert.True(pending.Election!.IsRunoff);
        Assert.Equal([adults[3]], pending.Election.SelectedMemberIds);
        Assert.Equal(adults.Skip(4).Take(2), pending.Election.Candidates);
        Assert.Equal([adults[2], adults[3]], pending.MemberIds);
        Assert.Equal(72, pending.Election.ExpiryTick);
        AssertCivicRoundTrip(first);

        Assert.True(first.ResignTownCouncil(adults[3], Town).Applied);
        Assert.Contains(adults[3], first.Towns.Single().ResidentIds);
        Assert.Contains(first.TownCouncils.Single().CandidateRegister,
            item => item.CandidateId == adults[3] && item.ReplacementWilling);
        Assert.DoesNotContain(adults[3], first.TownCouncils.Single().MemberIds);
        Assert.DoesNotContain(adults[3], first.TownCouncils.Single().Election!.SelectedMemberIds);
        AssertCivicRoundTrip(first);
        using var world = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(first.ExportState())),
            _ => new CivicChooser(false, false));
        Assert.DoesNotContain(adults[3], world.TownCouncils.Single().Election!.SelectedMemberIds);
        await AdvanceTo(world, 72);
        var completed = world.TownCouncils.Single();
        Assert.Equal("representative", completed.GoverningForm);
        Assert.Equal(adults.Skip(2).Where(id => id != adults[3]).Take(3).Order(StringComparer.Ordinal),
            completed.MemberIds.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(adults[3], completed.MemberIds);
        Assert.DoesNotContain(adults[3], completed.LastElectionOutcome!.SelectedMemberIds);
        Assert.Equal(original.TermStartedTick, completed.TermStartedTick);
        Assert.Equal(original.TermExpiryTick, completed.TermExpiryTick);
        AssertCivicRoundTrip(world);
    }

    private static async Task<PrivateWorldRuntime> ActualRankedRunoffAsync()
    {
        var world = PrivateWorldRuntime.Restore(RegisteredElection(nomineeIndexes: [0, 1, 2, 3]),
            _ => new CivicChooser(false, false));
        try
        {
            var adults = CivicAdults(world.Towns.Single().ResidentIds);
            Assert.Equal(8, adults.Length);
            Assert.Equal(36, world.Society.Inventory.Reservations
                .Where(item => item.Purpose == "building:test-town-hall").Sum(item => item.Quantity));
            for (var index = 0; index < 4; index++)
                Assert.True(world.VoteTownElection(adults[index], Town, [adults[0], adults[1], adults[2 + index % 2]]).Applied);
            Assert.True(world.VoteTownElection(adults[4], Town, [adults[0]]).Applied);
            var main = world.TownCouncils.Single().Election!;
            Assert.Equal(5, main.Votes.Count(vote => vote.CandidateIds.Contains(adults[0], StringComparer.Ordinal)));
            Assert.Equal(4, main.Votes.Count(vote => vote.CandidateIds.Contains(adults[1], StringComparer.Ordinal)));
            Assert.Equal(2, main.Votes.Count(vote => vote.CandidateIds.Contains(adults[2], StringComparer.Ordinal)));
            Assert.Equal(2, main.Votes.Count(vote => vote.CandidateIds.Contains(adults[3], StringComparer.Ordinal)));
            await AdvanceTo(world, 24);
            Assert.True(world.TownCouncils.Single().Election!.IsRunoff);
            Assert.Equal(adults.Take(2), world.TownCouncils.Single().Election!.SelectedMemberIds);
            Assert.Equal(adults.Skip(2).Take(2), world.TownCouncils.Single().Election!.Candidates);
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }
}
