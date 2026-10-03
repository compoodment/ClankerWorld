using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class HouseRelocationRulesTests
{
    [Fact]
    public void VolunteerTakesTheLatestArrivalsPlaceAndSelectionStopsWhenResidentsFit()
    {
        var residents = new[] { Resident("volunteer"), Resident("latest"), Resident("middle"), Resident("oldest") };
        HouseRelocationRules.Adult[] adults =
        [
            new("latest", 100, true),
            new("middle", 50, true),
            new("oldest", 0, true),
            new("volunteer", 1, true, HouseRelocationRules.Volunteer, 200),
        ];

        var selection = HouseRelocationRules.Select(residents, adults, 1, 1);

        Assert.Equal(new HouseRelocationRules.SelectedAdult("volunteer", HouseRelocationRules.Volunteer),
            Assert.Single(selection.Chosen));
        Assert.True(selection.Fits);
        Assert.Equal(3, selection.Remaining.ResidentCount);
    }

    [Fact]
    public void VolunteersUseTheirNoticeOrderAndStableOrdinalIdsBeforeForcedNotices()
    {
        var residents = new[] { Resident("z"), Resident("a"), Resident("first"), Resident("forced"), Resident("other") };
        HouseRelocationRules.Adult[] adults =
        [
            new("forced", 500, true, HouseRelocationRules.LatestArrival, 1),
            new("z", 300, true, HouseRelocationRules.Volunteer, 20),
            new("a", 100, true, HouseRelocationRules.Volunteer, 20),
            new("first", 0, true, HouseRelocationRules.Volunteer, 10),
            new("other", 400, true),
        ];

        var selection = HouseRelocationRules.Select(residents, adults, 1, 1);

        Assert.Collection(selection.Chosen,
            item => Assert.Equal("first", item.Id), item => Assert.Equal("a", item.Id));
        Assert.All(selection.Chosen, item => Assert.Equal(HouseRelocationRules.Volunteer, item.Reason));
        Assert.True(selection.Fits);
    }

    [Fact]
    public void ExistingNoticeKeepsItsPlaceAheadOfANewerArrival()
    {
        var residents = new[] { Resident("notified"), Resident("newcomer"), Resident("third"), Resident("fourth") };
        HouseRelocationRules.Adult[] adults =
        [
            new("newcomer", 100, true),
            new("third", 50, true),
            new("fourth", 25, true),
            new("notified", 0, true, HouseRelocationRules.LatestArrival, 20),
        ];

        var selection = HouseRelocationRules.Select(residents, adults, 1, 1);

        Assert.Equal("notified", Assert.Single(selection.Chosen).Id);
        Assert.True(selection.Fits);
    }

    [Fact]
    public void EqualArrivalTimesUseOrdinalIdsRegardlessOfInputOrder()
    {
        var residents = new[] { Resident("a"), Resident("b"), Resident("old"), Resident("older"), Resident("oldest") };
        HouseRelocationRules.Adult[] adults =
        [new("b", 100, true), new("a", 100, true), new("old", 30, true), new("older", 20, true), new("oldest", 10, true)];

        var forward = HouseRelocationRules.Select(residents, adults, 1, 1);
        var reversed = HouseRelocationRules.Select(residents.Reverse(), adults.Reverse(), 1, 1);

        Assert.Collection(forward.Chosen,
            item => Assert.Equal("a", item.Id), item => Assert.Equal("b", item.Id));
        Assert.Equal(forward.Chosen, reversed.Chosen);
        Assert.All(forward.Chosen, item => Assert.Equal(HouseRelocationRules.LatestArrival, item.Reason));
    }

    [Fact]
    public void DominantFamilyIsProtectedEvenWhenItsMembersArrivedLast()
    {
        var residents = new[]
        {
            Resident("parent-a", "family"), Resident("parent-b", "family"),
            Resident("child", "family") with { AgeBand = SocietyAgeBand.Child },
            Resident("older-outsider"), Resident("newer-outsider"),
        };
        HouseRelocationRules.Adult[] adults =
        [new("parent-a", 100, true), new("parent-b", 90, true), new("older-outsider", 1, true), new("newer-outsider", 2, true)];

        var selection = HouseRelocationRules.Select(residents, adults, 1, 1);

        Assert.Equal(new HouseRelocationRules.SelectedAdult("newer-outsider", HouseRelocationRules.LatestUnrelatedArrival),
            Assert.Single(selection.Chosen));
        Assert.True(selection.Fits);
        Assert.True(selection.Remaining.HasDominantFamily);
        Assert.Equal(4, selection.Remaining.ResidentCount);
    }

    [Fact]
    public void FirstChildFitsWithAnUnrelatedAdultAndSecondChildRelocatesOnlyThatAdult()
    {
        var residents = new[]
        {
            Resident("parent-a", "family"), Resident("parent-b", "family"),
            Resident("first-child", "family") with
            {
                AgeBand = SocietyAgeBand.Child,
                PrimaryCaregiverId = "parent-a",
            },
            Resident("unrelated"),
        };
        HouseRelocationRules.Adult[] adults =
        [new("parent-a", 100, false), new("parent-b", 90, true), new("unrelated", 1, true)];

        var firstChild = HouseRelocationRules.Select(residents, adults, 1, 1);
        var secondChild = HouseRelocationRules.Select(residents.Append(Resident("second-child", "family") with
        {
            AgeBand = SocietyAgeBand.Infant,
            PrimaryCaregiverId = "parent-a",
        }), adults, 1, 1);

        Assert.Empty(firstChild.Chosen);
        Assert.True(firstChild.Fits);
        Assert.Equal(4, firstChild.Remaining.ResidentCount);
        Assert.Equal(4, firstChild.Remaining.Limit);
        Assert.Equal(new HouseRelocationRules.SelectedAdult("unrelated", HouseRelocationRules.LatestUnrelatedArrival),
            Assert.Single(secondChild.Chosen));
        Assert.True(secondChild.Fits);
        Assert.Equal(4, secondChild.Remaining.ResidentCount);
        Assert.Equal(4, secondChild.Remaining.Limit);
    }

    [Fact]
    public void TiedFamiliesHaveNoPriorityAndOneDepartureCanCreateTheFamilyBonus()
    {
        var residents = new[]
        {
            Resident("a1", "family-a"), Resident("a2", "family-a"),
            Resident("b1", "family-b"), Resident("b2", "family-b"),
        };
        HouseRelocationRules.Adult[] adults =
        [new("a1", 1, true), new("a2", 2, true), new("b1", 3, true), new("b2", 4, true)];

        Assert.Null(HouseRelocationRules.DominantFamily(residents));
        var selection = HouseRelocationRules.Select(residents, adults, 1, 1);

        Assert.Equal(new HouseRelocationRules.SelectedAdult("b2", HouseRelocationRules.LatestArrival),
            Assert.Single(selection.Chosen));
        Assert.True(selection.Fits);
        Assert.Equal(4, selection.Remaining.Limit);
    }

    [Fact]
    public void CreatingAFamilyMajorityCanRemoveMoreThanOneExcessPlace()
    {
        var residents = Enumerable.Range(1, 4).Select(index => Resident("a" + index, "family-a"))
            .Concat(Enumerable.Range(1, 4).Select(index => Resident("b" + index, "family-b"))).ToArray();
        var adults = residents.Select((person, index) => new HouseRelocationRules.Adult(person.Id, index, true));

        var selection = HouseRelocationRules.Select(residents, adults, 1, 2);

        Assert.Equal("b4", Assert.Single(selection.Chosen).Id);
        Assert.True(selection.Fits);
        Assert.Equal(7, selection.Remaining.ResidentCount);
        Assert.Equal(8, selection.Remaining.Limit);
    }

    [Fact]
    public void VolunteerIsSkippedWhenLeavingWouldEraseTheNewFamilyBonusWithoutReducingCrowding()
    {
        var residents = new[]
        {
            Resident("a1", "family-a"), Resident("a2", "family-a"), Resident("a3", "family-a"),
            Resident("b1", "family-b"), Resident("b2", "family-b"), Resident("b3", "family-b"),
        };
        HouseRelocationRules.Adult[] adults =
        [
            new("b1", 1, true, HouseRelocationRules.Volunteer, 10),
            new("a1", 100, true, HouseRelocationRules.Volunteer, 20),
            new("a2", 90, true), new("a3", 80, true), new("b2", 2, true), new("b3", 3, true),
        ];

        var selection = HouseRelocationRules.Select(residents, adults, 1, 1);

        Assert.Collection(selection.Chosen,
            item => Assert.Equal("b1", item.Id), item => Assert.Equal("b3", item.Id));
        Assert.True(selection.Fits);
        Assert.True(selection.Remaining.HasDominantFamily);
        Assert.Equal(4, selection.Remaining.ResidentCount);
    }

    [Fact]
    public void AllFamilyOvercrowdingWaitsForAVoluntarySplitOrExpansion()
    {
        var residents = Enumerable.Range(1, 5).Select(index => Resident("person" + index, "family")).ToArray();
        var adults = residents.Select((person, index) => new HouseRelocationRules.Adult(person.Id, index, true)).ToArray();

        var forced = HouseRelocationRules.Select(residents, adults, 1, 1);
        var volunteered = HouseRelocationRules.Select(residents,
            adults.Select(adult => adult.Id == "person1"
                ? adult with { NoticeReason = HouseRelocationRules.Volunteer, NoticeTick = 10 } : adult), 1, 1);

        Assert.Empty(forced.Chosen);
        Assert.False(forced.Fits);
        Assert.Equal(5, forced.Remaining.ResidentCount);
        Assert.Equal("person1", Assert.Single(volunteered.Chosen).Id);
        Assert.True(volunteered.Fits);
    }

    [Theory]
    [InlineData(1, 2, 8)]
    [InlineData(2, 2, 16)]
    public void BirthBeyondALargerHousesFamilyLimitKeepsTheFamilyTogetherAndOvercrowdingVisible(
        int width, int height, int familyLimit)
    {
        var residents = new[] { Resident("parent-a", "family"), Resident("parent-b", "family") }
            .Concat(Enumerable.Range(1, familyLimit - 2).Select(index => Resident("child" + index, "family") with
            {
                AgeBand = SocietyAgeBand.Child,
                PrimaryCaregiverId = "parent-a",
            })).ToArray();
        HouseRelocationRules.Adult[] adults = [new("parent-a", 100, false), new("parent-b", 90, true)];

        var beforeBirth = HouseRelocationRules.Select(residents, adults, width, height);
        var afterBirth = HouseRelocationRules.Select(residents.Append(Resident("newborn", "family") with
        {
            AgeBand = SocietyAgeBand.Infant,
            PrimaryCaregiverId = "parent-a",
        }), adults, width, height);

        Assert.Empty(beforeBirth.Chosen);
        Assert.True(beforeBirth.Fits);
        Assert.Equal(familyLimit, beforeBirth.Remaining.ResidentCount);
        Assert.Equal(familyLimit, beforeBirth.Remaining.Limit);
        Assert.Empty(afterBirth.Chosen);
        Assert.False(afterBirth.Fits);
        Assert.True(afterBirth.Remaining.IsOvercrowded);
        Assert.Equal(familyLimit + 1, afterBirth.Remaining.ResidentCount);
        Assert.Equal(familyLimit, afterBirth.Remaining.Limit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(HouseRelocationRules.LatestArrival)]
    [InlineData(HouseRelocationRules.Volunteer)]
    public void SoleCaregiversNeverReceiveOrKeepATimedMoveOutNotice(string? previousReason)
    {
        var residents = new[]
        {
            Resident("caregiver-a", "family-a"),
            Resident("child-a", "family-a") with { AgeBand = SocietyAgeBand.Child },
            Resident("caregiver-b", "family-b"),
            Resident("child-b", "family-b") with { AgeBand = SocietyAgeBand.Infant },
        };
        HouseRelocationRules.Adult[] adults =
        [new("caregiver-a", 100, false, previousReason, previousReason is null ? null : 10), new("caregiver-b", 1, false)];

        var selection = HouseRelocationRules.Select(residents, adults, 1, 1);

        Assert.Empty(selection.Chosen);
        Assert.False(selection.Fits);
        Assert.Equal(4, selection.Remaining.ResidentCount);
        Assert.Equal(3, selection.Remaining.Limit);
    }

    [Fact]
    public void AnAdultWhoBecomesACaregiverIsReplacedByTheNextEligibleAdult()
    {
        var residents = new[]
        {
            Resident("caregiver", "family"), Resident("child", "family") with { AgeBand = SocietyAgeBand.Child },
            Resident("older"), Resident("newer"),
        };
        HouseRelocationRules.Adult[] adults =
        [new("caregiver", 100, false, HouseRelocationRules.LatestArrival, 10), new("older", 1, true), new("newer", 2, true)];

        var selection = HouseRelocationRules.Select(residents, adults, 1, 1);

        Assert.Equal("newer", Assert.Single(selection.Chosen).Id);
        Assert.True(selection.Fits);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void CompletedExpansionMakesExistingNoticesUnnecessary(int width, int height)
    {
        var residents = Enumerable.Range(1, 5).Select(index => Resident("person" + index)).ToArray();
        var adults = residents.Select((person, index) => new HouseRelocationRules.Adult(person.Id, index, true,
            HouseRelocationRules.LatestArrival, 10));

        var selection = HouseRelocationRules.Select(residents, adults, width, height);

        Assert.Empty(selection.Chosen);
        Assert.True(selection.Fits);
        Assert.Equal(5, selection.Remaining.ResidentCount);
    }

    [Fact]
    public void DeathMakesAnExistingNoticeUnnecessary()
    {
        var residents = new[]
        {
            Resident("first"), Resident("second"), Resident("third"),
            Resident("dead") with { Status = SocietyInhabitantStatus.Dead },
        };
        HouseRelocationRules.Adult[] adults =
        [new("first", 1, true), new("second", 2, true),
            new("third", 3, true, HouseRelocationRules.LatestArrival, 10), new("dead", 100, true)];

        var selection = HouseRelocationRules.Select(residents, adults, 1, 1);

        Assert.Empty(selection.Chosen);
        Assert.True(selection.Fits);
        Assert.Equal(3, selection.Remaining.ResidentCount);
    }

    [Fact]
    public void InactiveAndAbsentAdultsCannotDisplaceAResidentInTheSelection()
    {
        var residents = new[]
        {
            Resident("first"), Resident("second"), Resident("third"), Resident("fourth"),
            Resident("dead") with { Status = SocietyInhabitantStatus.Dead },
        };
        HouseRelocationRules.Adult[] adults =
        [new("first", 1, true), new("second", 2, true), new("third", 3, true), new("fourth", 4, true),
            new("dead", 100, true), new("elsewhere", 200, true, HouseRelocationRules.Volunteer, 10)];

        var selection = HouseRelocationRules.Select(residents, adults, 1, 1);

        Assert.Equal("fourth", Assert.Single(selection.Chosen).Id);
        Assert.True(selection.Fits);
        Assert.Equal(3, selection.Remaining.ResidentCount);
    }

    private static SocietyInhabitant Resident(string id, string? familyId = null) =>
        SocietyFixture.CreateFounder(id, id) with
        {
            HouseholdId = "home",
            DomesticFamilyUnitId = familyId ?? "family:" + id,
        };
}
