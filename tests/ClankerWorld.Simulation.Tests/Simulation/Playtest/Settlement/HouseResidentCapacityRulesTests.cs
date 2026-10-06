using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class HouseResidentCapacityRulesTests
{
    [Theory]
    [InlineData(1, 1, 3, 4)]
    [InlineData(1, 2, 6, 8)]
    [InlineData(2, 1, 6, 8)]
    [InlineData(2, 2, 12, 16)]
    public void CompletedHouseFootprintScalesOrdinaryAndFamilyPlaces(
        int width, int height, int ordinaryPlaces, int familyPlaces)
    {
        var unrelated = new[] { Resident("a", "family-a"), Resident("b", "family-b") };
        var family = new[] { Resident("a", "family-a"), Resident("b", "family-a") };

        var ordinary = HouseResidentCapacityRules.Calculate(unrelated, width, height);
        var dominant = HouseResidentCapacityRules.Calculate(family, width, height);

        Assert.Equal(ordinaryPlaces, ordinary.Limit);
        Assert.False(ordinary.HasDominantFamily);
        Assert.Equal(familyPlaces, dominant.Limit);
        Assert.True(dominant.HasDominantFamily);
    }

    [Fact]
    public void ActiveTravelersCountAndDeadPeopleDoNot()
    {
        var residents = new[]
        {
            Resident("home", "family-a"),
            Resident("infant-traveler", "family-a") with { AgeBand = SocietyAgeBand.Infant },
            Resident("deceased", "family-b") with { Status = SocietyInhabitantStatus.Dead },
        };

        var capacity = HouseResidentCapacityRules.Calculate(residents, 1, 1);

        Assert.Equal(2, capacity.ResidentCount);
        Assert.True(capacity.HasDominantFamily);
    }

    private static SocietyInhabitant Resident(string id, string familyId) =>
        SocietyFixture.CreateFounder(id, id) with
        {
            HouseholdId = "home",
            DomesticFamilyUnitId = familyId,
        };
}
