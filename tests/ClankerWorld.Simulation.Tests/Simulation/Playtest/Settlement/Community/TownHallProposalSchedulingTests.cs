using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Fact]
    public async Task AnEquivalentPendingRequestPreservesItsOriginalDeadlineProposerAndVotesAcrossReload()
    {
        var state = CivicCalendar(AtHall(WithHall(Initial())));
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        AssertPaidCollectiveProposalHall(world);
        var adults = world.Towns.Single(item => item.Id == Town).ResidentIds.ToArray();
        const string text = "Let each speaker finish before replying.";
        Assert.True(world.ProposeTownLaw(adults[0], Town, "quiet_meetings", text).Applied);
        Assert.Empty(world.TownCouncils.Single(item => item.TownId == Town).Ballot!.Approvals);
        Assert.True(world.VoteTownLaw(adults[1], Town, true).Applied);
        var original = world.TownCouncils.Single(item => item.TownId == Town).Ballot!;
        await AdvanceTo(world, original.ProposedTick + 5);
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new CivicChooser(false, false));

        var beforeDuplicate = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        Assert.True(restored.ProposeTownLaw(adults[2], Town, "quiet_meetings", text).Applied);
        var pending = restored.TownCouncils.Single(item => item.TownId == Town).Ballot!;
        Assert.Equal(original.ProposedTick, pending.ProposedTick);
        Assert.Equal(original.ExpiryTick, pending.ExpiryTick);
        Assert.Equal(adults[0], pending.ProposerId);
        Assert.Equal(text, pending.Text);
        Assert.Equal(original.Electorate, pending.Electorate);
        Assert.Equal([adults[1]], pending.Approvals);
        Assert.Empty(pending.Rejections);
        Assert.Equal(beforeDuplicate, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Single(restored.ExportState().Events, item => item.Kind == "town_law_proposed" &&
            item.Detail == $"{Town}|quiet_meetings|{adults[0]}");

        Assert.True(restored.VoteTownLaw(adults[0], Town, true).Applied);
        Assert.NotNull(restored.TownCouncils.Single(item => item.TownId == Town).Ballot);
        Assert.True(restored.VoteTownLaw(adults[2], Town, true).Applied);
        Assert.Null(restored.TownCouncils.Single(item => item.TownId == Town).Ballot);
        var law = Assert.Single(restored.TownCouncils.Single(item => item.TownId == Town).Laws!,
            item => item.Key == "quiet_meetings");
        Assert.Equal(adults[0], law.ProposerId);
        Assert.Equal(text, law.Text);
        Assert.True(law.AdoptedTick < original.ExpiryTick);
        AssertProposalSaveRoundTrip(restored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AProposalVoteIsFinalAcrossReloadEvenWhileItsWindowRemainsOpen(bool firstApproval)
    {
        var state = CivicCalendar(AtHall(WithHall(Initial())));
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        AssertPaidCollectiveProposalHall(world);
        var adults = world.Towns.Single(item => item.Id == Town).ResidentIds.ToArray();
        Assert.True(world.ProposeTownLaw(adults[0], Town, "quiet_meetings", "Let each speaker finish.").Applied);
        Assert.True(world.VoteTownLaw(adults[1], Town, firstApproval).Applied);
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new CivicChooser(false, false));
        var ballot = restored.TownCouncils.Single(item => item.TownId == Town).Ballot!;
        Assert.True(restored.WorldTick < ballot.ExpiryTick);
        var beforeRevision = PrivateWorldRuntimeCodec.Encode(restored.ExportState());

        Assert.False(restored.VoteTownLaw(adults[1], Town, !firstApproval).Applied);
        Assert.False(restored.VoteTownLaw(adults[1], Town, firstApproval).Applied);
        Assert.Equal(beforeRevision, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var unchanged = restored.TownCouncils.Single(item => item.TownId == Town).Ballot!;
        Assert.Equal(firstApproval ? new[] { adults[1] } : [], unchanged.Approvals);
        Assert.Equal(firstApproval ? [] : new[] { adults[1] }, unchanged.Rejections);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "town_law_vote_recorded" &&
            item.Detail.StartsWith($"{Town}|{adults[1]}|", StringComparison.Ordinal));
        AssertProposalSaveRoundTrip(restored);
    }

    [Fact]
    public async Task RewordingAFailedRequestCannotBypassItsOneDayRetryWaitAcrossReload()
    {
        var state = CivicCalendar(AtHall(WithHall(Initial())));
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        AssertPaidCollectiveProposalHall(world);
        var adults = world.Towns.Single(item => item.Id == Town).ResidentIds.ToArray();
        Assert.True(world.ProposeTownLaw(adults[0], Town, "quiet_meetings", "Let each speaker finish.").Applied);
        Assert.True(world.VoteTownLaw(adults[1], Town, false).Applied);
        Assert.NotNull(world.TownCouncils.Single(item => item.TownId == Town).Ballot);
        Assert.True(world.VoteTownLaw(adults[2], Town, false).Applied);
        Assert.Null(world.TownCouncils.Single(item => item.TownId == Town).Ballot);
        Assert.DoesNotContain(world.TownCouncils.Single(item => item.TownId == Town).Laws!, item => item.Key == "quiet_meetings");
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_law_rejected" && item.Detail == $"{Town}|quiet_meetings");
        var closedTick = world.WorldTick;
        var day = world.WorldSystems.Config.TicksPerDay;
        const string reworded = "Wait until the current speaker is done before responding.";
        Assert.False(world.ProposeTownLaw(adults[3], Town, "quiet_meetings", reworded).Applied);
        Assert.Null(world.TownCouncils.Single(item => item.TownId == Town).Ballot);
        await AdvanceTo(world, closedTick + day - 1);

        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new CivicChooser(false, false));
        Assert.Equal(closedTick + day - 1, restored.WorldTick);
        Assert.False(restored.ProposeTownLaw(adults[3], Town, "quiet_meetings", reworded).Applied);
        Assert.Null(restored.TownCouncils.Single(item => item.TownId == Town).Ballot);
        await AdvanceTo(restored, closedTick + day);
        Assert.True(restored.ProposeTownLaw(adults[3], Town, "quiet_meetings", reworded).Applied);
        var retry = restored.TownCouncils.Single(item => item.TownId == Town).Ballot!;
        Assert.Equal(closedTick + day, retry.ProposedTick);
        Assert.Equal(closedTick + 2L * day, retry.ExpiryTick);
        Assert.Equal(adults[3], retry.ProposerId);
        Assert.Equal(reworded, retry.Text);
        Assert.Empty(retry.Approvals);
        Assert.Empty(retry.Rejections);
        Assert.Equal(2, restored.ExportState().Events.Count(item => item.Kind == "town_law_proposed" &&
            item.Detail.StartsWith($"{Town}|quiet_meetings|", StringComparison.Ordinal)));
        AssertProposalSaveRoundTrip(restored);
    }

    private static void AssertPaidCollectiveProposalHall(PrivateWorldRuntime world)
    {
        var council = world.TownCouncils.Single(item => item.TownId == Town);
        Assert.Equal(4, council.MemberIds.Count);
        Assert.Null(council.Election);
        Assert.Null(council.Ballot);
        var hall = Assert.Single(world.WorldSimulation.Buildings,
            item => item.DefinitionId == TownHallContent.TownHall().CanonicalId);
        Assert.Equal(Town, hall.TownId);
        Assert.Null(hall.HouseholdId);
        var state = world.ExportState();
        var footprint = WorldContentSimulationRules.Footprint(TownHallContent.TownHall(), hall).ToArray();
        Assert.All(council.MemberIds, id => Assert.Contains(footprint, point =>
            state.Map.FootDistance(state.Inhabitants.Single(person => person.InhabitantId == id).Position, point) <= 1));
        var cost = world.Society.Inventory.Reservations.Where(item => item.Purpose == "building:test-town-hall").ToArray();
        Assert.Equal(36, cost.Sum(item => item.Quantity));
        Assert.All(cost, item =>
        {
            Assert.Equal(Alpha, item.OwnerId);
            Assert.Equal(InventoryReservationState.Completed, item.State);
        });
        Assert.Equal(12, cost.Where(item => item.LotId == "hall-stone").Sum(item => item.Quantity));
    }

    private static void AssertProposalSaveRoundTrip(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new CivicChooser(false, false));
        restored.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}
