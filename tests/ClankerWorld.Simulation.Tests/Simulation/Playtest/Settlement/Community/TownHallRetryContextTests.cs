using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Fact]
    public void AFailedNamedLawCannotRetryEarlyBecauseAnUnrelatedLawOrPrivateFoodChanged()
    {
        using var original = PrivateWorldRuntime.Restore(AtHall(WithHall(Initial())), _ => new CivicChooser(false, false));
        var adults = CivicAdults(original.Towns.Single().ResidentIds);
        Assert.False(original.VolunteerTownCouncil(adults[0], Town, replacement: true).Applied);
        Assert.True(original.ProposeTownLaw(adults[0], Town, "quiet_meetings", "Let each speaker finish.").Applied);
        Assert.True(original.VoteTownLaw(adults[0], Town, false).Applied);
        Assert.True(original.VoteTownLaw(adults[1], Town, false).Applied);
        Assert.Null(original.TownCouncils.Single().Ballot);
        Assert.True(original.ProposeTownLaw(adults[2], Town, "path_names", "Name the existing paths.").Applied);
        foreach (var adult in adults.Take(3)) Assert.True(original.VoteTownLaw(adult, Town, true).Applied);
        var state = original.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "unrelated-private-food", "berries", Alpha, 1,
            storageBuildingId: "first-town-house-a");
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            TownCouncils = state.TownCouncils!.Select(council => council with
            {
                ProposalOutcomes = council.ProposalOutcomes.Select(closed => closed with
                { Circumstances = closed.Circumstances with { MemberIds = closed.Circumstances.MemberIds.Reverse().ToArray() } }).ToArray()
            }).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new CivicChooser(false, false));
        Assert.False(world.ProposeTownLaw(adults[3], Town, "quiet_meetings", "Please let people finish talking.").Applied);
        Assert.Null(world.TownCouncils.Single().Ballot);
        var council = world.TownCouncils.Single();
        var closed = Assert.Single(council.ProposalOutcomes);
        Assert.Equal("quiet_meetings||False", closed.RequestKey);
        Assert.Null(closed.Circumstances.AdultPopulation);
        Assert.Null(closed.Circumstances.CommunalFoodQuantity);
        foreach (var damaged in new[]
        {
            closed with { Circumstances = null! },
            closed with { Circumstances = closed.Circumstances with { GoverningForm = "unrelated" } },
            closed with { Circumstances = closed.Circumstances with { MemberIds = ["absent-person"] } },
            closed with { Circumstances = closed.Circumstances with { CommunalFoodQuantity = 0 } }
        })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(world.ExportState() with
            { TownCouncils = [council with { ProposalOutcomes = [damaged] }] }));
        AssertCivicRoundTrip(world);
    }
}
