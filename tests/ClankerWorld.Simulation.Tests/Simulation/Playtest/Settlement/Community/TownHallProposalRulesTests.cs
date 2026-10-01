using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Fact]
    public async Task AdultResidentCanProposeWithoutImplicitApprovalAfterRepresentationAcrossReload()
    {
        var state = RegisteredElection();
        var adults = CivicAdults(state.Towns!.Single(town => town.Id == Town).ResidentIds);
        var members = adults.Take(3).ToArray();
        var proposer = adults.Last();
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        Assert.True(world.VoteTownElection(adults[0], Town, members).Applied);
        await AdvanceTo(world, 24);
        Assert.True(world.ProposeTownLaw(proposer, Town, "quiet_meetings", "Let each speaker finish.").Applied);
        Assert.Empty(world.TownCouncils.Single().Ballot!.Approvals);
        Assert.False(world.VoteTownLaw(proposer, Town, true).Applied);
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new CivicChooser(false, false));
        Assert.True(restored.VoteTownLaw(members[0], Town, true).Applied);
        Assert.Empty(restored.TownCouncils.Single().Laws!);
        Assert.True(restored.VoteTownLaw(members[1], Town, true).Applied);
        Assert.Null(restored.TownCouncils.Single().Ballot);
        Assert.Equal(proposer, Assert.Single(restored.TownCouncils.Single().Laws!).ProposerId);
        restored.Validate();
    }

    [Fact]
    public void CouncilAuthorMustCastAnExplicitVoteBeforeItsProposalCanPass()
    {
        using var world = PrivateWorldRuntime.Restore(AtHall(WithHall(Initial())), _ => new CivicChooser(false, false));
        var adults = world.Towns.Single(town => town.Id == Town).ResidentIds.ToArray();
        Assert.True(world.ProposeTownLaw(adults[0], Town, "quiet_meetings", "Let each speaker finish.").Applied);
        Assert.Empty(world.TownCouncils.Single().Ballot!.Approvals);
        Assert.True(world.VoteTownLaw(adults[1], Town, true).Applied);
        Assert.True(world.VoteTownLaw(adults[2], Town, true).Applied);
        Assert.Empty(world.TownCouncils.Single().Laws!);
        Assert.True(world.VoteTownLaw(adults[0], Town, true).Applied);
        Assert.Null(world.TownCouncils.Single().Ballot);
        Assert.Single(world.TownCouncils.Single().Laws!);
        world.Validate();
    }
}
