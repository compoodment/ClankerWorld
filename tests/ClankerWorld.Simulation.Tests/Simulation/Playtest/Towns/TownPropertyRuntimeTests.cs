using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownLandHearingRuntimeTests
{
    [Fact]
    public async Task AnEmptyHouseholdsPaidHouseIsOfferedForATownPropertyCaseAndDepartureDoesNotTransferIt()
    {
        var provider = new HearingProvider { SeekMayor = false };
        using var world = NewWorld(provider);
        var source = world.ExportState().Society.Society.GetInhabitant(Filer).HouseholdId!;
        var house = Assert.Single(world.ExportState().WorldSimulation!.Buildings,
            building => building.HouseholdId == source && world.ExportState().WorldContent!.Buildings.Any(definition =>
                definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house", StringComparer.Ordinal)));
        Assert.True(world.DisplaceAdult(Filer));
        Assert.True(world.DisplaceAdult(Waiver));
        Assert.Empty(world.ExportState().Society.Society.Households.Single(household => household.Id == source).MemberIds);
        Assert.Equal(source, world.ExportState().WorldSimulation!.Buildings.Single(building => building.InstanceId == house.InstanceId).HouseholdId);
        for (var step = 0; step < 5; step++)
        {
            Prompt(world, Judge, "Consider a Town property hearing for the empty household's House.");
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Contains(provider.Offered[Judge], choice => choice.Contains("|hearing_property_request|", StringComparison.Ordinal));
    }
}
