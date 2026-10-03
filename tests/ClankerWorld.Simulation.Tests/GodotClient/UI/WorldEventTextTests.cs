using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldEventTextTests
{
    private const string FounderId = "founder:00000000000000000000000000000001";
    private const string AgentId = "agent:00000000000000000000000000000099";
    private const string ChildId = "world:inhabitant:birth:" + FounderId + ":" + AgentId + ":1";

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(null, true, false)]
    public void NewcomerOfferRequiresTheCurrentRuleAndAStartedWorld(bool? ruleActive, bool started, bool offered)
    {
        var snapshot = Snapshot() with { ContinuityRuleActive = ruleActive, FounderSetup = new(4, 4, started) };
        Assert.Equal(offered, WorldEventText.OffersNewcomer(snapshot));
        Assert.False(WorldEventText.OffersNewcomer(null));
        Assert.False(WorldEventText.OffersNewcomer(snapshot with { FounderSetup = null }));
    }

    [Fact]
    public void SkillEventsShowLearnerAndTeacherWithoutSplittingTheirIds()
    {
        var snapshot = Snapshot(Person(ChildId, "Aster"), Person(FounderId, "Mira", "dead"));
        Assert.Equal("Aster learned farming from Mira.", WorldEventText.Describe(
            new(1, 1, "skill_learned", $"{ChildId}|farming|{FounderId}"), snapshot));
        Assert.Equal("Aster learned smithing by doing the work.", WorldEventText.Describe(
            new(2, 2, "skill_learned", $"{ChildId}|smithing|work"), snapshot));
    }

    [Theory]
    [InlineData(ChildId)]
    [InlineData("founder-scout")]
    public void ActorEventsKeepCompleteIdsAndUseCurrentLivingOrDeceasedNames(string id)
    {
        var snapshot = Snapshot(Person(id, "Aster"));
        var cases = new[]
        {
            ("food_harvested", id + ":4", "gathered food"),
            ("food_consumed", id, "ate"),
            ("tree_planted", id + ":planted-tree-12-7:broadleaf", "planted a tree"),
            ("tree_replanted", id + ":tree-8-16:conifer", "replanted a tree"),
            ("inhabitant_slept", id, "slept"),
            ("child_born", id, "was born"),
            ("inhabitant_removed", id, "died"),
            ("inhabitant_building_proposed", id + ":house", "suggested a new building design"),
            ("household_delivery_recovered", id + ":spoiled-greens:4:camp-alpha",
                "returned unusable delivery supplies to their household's pile at camp"),
            ("instruction_not_understood", id + ":private-instruction-0000000001",
                "didn't understand your order. For now, orders can only ask them to gather food, eat, find food, go to a tile or become a child's guardian"),
        };
        foreach (var (kind, detail, action) in cases)
        {
            var worldEvent = new OwnerWorldEvent(1, 1, kind, detail);
            Assert.Equal($"Aster {action}.", WorldEventText.Describe(worldEvent, snapshot));
            var renamed = snapshot with { Inhabitants = [Person(id, "Rowan", "dead")] };
            Assert.Equal($"Rowan {action}.", WorldEventText.Describe(worldEvent, renamed));
            Assert.Equal(detail, worldEvent.Detail);
        }
    }

    [Fact]
    public void FieldEventsUseCompleteWorkerNamesAndReadHarvestConditionsFromTheEnd()
    {
        var snapshot = Snapshot(Person(ChildId, "Aster"));
        var cases = new[]
        {
            ("field_work_started", ChildId + ":field-12-7:Till", "Aster started work on a field."),
            ("field_prepared", ChildId + ":field-12-7:", "Aster prepared a field."),
            ("field_planted", ChildId + ":field-12-7:cultivated_greens", "Aster planted cultivated greens."),
            ("field_tended", ChildId + ":field-12-7:grain", "Aster tended grain."),
            ("field_harvested", ChildId + ":field-12-7:potatoes", "Aster harvested potatoes."),
            ("field_ready", "field-12-7", "A field is ready to harvest."),
            ("field_work_interrupted", "field-12-7", "Work on a field stopped."),
            ("crop_weather_loss", "field-12-7:harvest:2:snow", "Snow reduced a crop harvest."),
            ("crop_moisture_effect", "field-12-7:harvest:2:wet:70", "Moist soil improved a crop harvest."),
            ("crop_moisture_effect", "field-12-7:harvest:2:dry:10", "Dry soil reduced a crop harvest."),
        };
        foreach (var (kind, detail, expected) in cases)
        {
            Assert.True(GameUiText.IsPlayerFacingEvent(kind));
            var worldEvent = new OwnerWorldEvent(1, 1, kind, detail);
            Assert.Equal(expected, WorldEventText.Describe(worldEvent, snapshot));
            Assert.Equal(detail, worldEvent.Detail);
        }
    }

    [Theory]
    [InlineData("town:first", ChildId)]
    [InlineData("legacy-town", "founder-scout")]
    public void TownResidentEventsReadTownAndResidentAsCompleteIdentities(string townId, string actorId)
    {
        var snapshot = Snapshot(Person(actorId, "Aster", "dead")) with
        {
            Towns = [new(townId, "First Town", "founded", 0, [], [], [])],
        };
        Assert.Equal("Aster joined First Town.", WorldEventText.Describe(
            new(1, 0, "town_resident_joined", $"{townId}:{actorId}:child_joined:residents:4"), snapshot));
        Assert.Equal("Aster left First Town.", WorldEventText.Describe(
            new(2, 1, "town_resident_left", $"{townId}:{actorId}:residents:3"), snapshot));
    }

    [Fact]
    public void TownAdmissionEventsNameTheNewcomerTheirTownsAndAnyChildrenWhoMoved()
    {
        var snapshot = Snapshot(Person(AgentId, "Aster")) with
        {
            Towns = [new("town:first", "First Town", "founded", 0, [], [], []),
                new("town:second", "Second Town", "founded", 0, [], [], [])],
        };
        var cases = new[]
        {
            ("town_admission_accepted", $"town:second|{AgentId}|none|1", "Aster became a resident of Second Town."),
            ("town_admission_accepted", $"town:second|{AgentId}|town:first|1",
                "Aster became a resident of Second Town. They are no longer a resident of First Town."),
            ("town_admission_accepted", $"town:second|{AgentId}|town:first|3",
                "Aster became a resident of Second Town. They are no longer a resident of First Town. Their dependent children moved with them."),
            ("town_admission_accepted", $"town:second|{AgentId}|none|2",
                "Aster became a resident of Second Town. Their dependent children moved with them."),
            ("town_admission_approved", $"town:second|{AgentId}|town:second:proposal:4",
                "Second Town's council approved Aster's admission. It takes effect only if they accept."),
            ("town_admission_lapsed", $"town:first|{AgentId}|town:first:proposal:2|joined_elsewhere",
                "Aster did not join First Town: the approval no longer fits their circumstances."),
            ("town_admission_accepted", "town:gone|agent:missing|none|1", "Someone became a resident of a Town."),
            ("town_admission_accepted", "town:second", "Someone became a Town resident."),
        };
        foreach (var (kind, detail, expected) in cases)
        {
            Assert.True(GameUiText.IsPlayerFacingEvent(kind));
            var worldEvent = new OwnerWorldEvent(1, 0, kind, detail);
            Assert.Equal(expected, WorldEventText.Describe(worldEvent, snapshot));
            Assert.Equal(detail, worldEvent.Detail);
        }
    }

    [Fact]
    public void KnownPrefixesNeverReplaceAnExactDifferentActorIdentity()
    {
        var snapshot = Snapshot(Person("agent", "Wrong prefix"), Person(AgentId, "Aster"),
            Person("legacy-parent", "Parent"), Person("legacy-parent:child", "Child")) with
        {
            Towns = [new("town", "Wrong Town", "founded", 0, [], [], []),
                new("town:first", "First Town", "founded", 0, [], [], [])],
        };
        Assert.Equal("Aster gathered food.", WorldEventText.Describe(new(1, 0, "food_harvested", AgentId + ":4"), snapshot));
        Assert.Equal("Someone died.", WorldEventText.Describe(new(2, 0, "inhabitant_removed", AgentId + ":unknown-child"), snapshot));
        Assert.Equal("Child suggested a new building design.", WorldEventText.Describe(
            new(3, 0, "inhabitant_building_proposed", "legacy-parent:child:house"), snapshot));
        Assert.Equal("Child joined First Town.", WorldEventText.Describe(
            new(4, 0, "town_resident_joined", "town:first:legacy-parent:child:child_joined:residents:1"), snapshot));
    }

    [Fact]
    public void HousingEventsNameTheAdultAndTheHouseholdAsThePlayerSeesThem()
    {
        var snapshot = Snapshot(Person(AgentId, "Aster")) with
        {
            Stockpiles = [new("household:camp-alpha", "Alpha stores", [])],
        };
        Assert.Equal("Aster asked Alpha stores for a place to live in their House.", WorldEventText.Describe(
            new(1, 0, "housing_request_made", AgentId + ":household:camp-alpha"), snapshot));
        Assert.Equal("Aster now lives with Alpha stores.", WorldEventText.Describe(
            new(2, 0, "household_joined", AgentId + ":household:camp-alpha"), snapshot));
        Assert.Equal("Alpha stores did not agree to let Aster move in.", WorldEventText.Describe(
            new(3, 0, "housing_request_refused", AgentId + ":household:camp-alpha"), snapshot));
        Assert.Equal("a household did not answer Aster's request to move in.", WorldEventText.Describe(
            new(4, 0, "housing_request_expired", AgentId + ":household:camp-beta"), snapshot));
        Assert.Equal("Aster has no home: they belong to no household, so no House can be planned for them.",
            WorldEventText.Describe(new(5, 0, "housing_blocked", AgentId + ":no_household"), snapshot));
        Assert.Equal("Aster has no home: their household has no legal site for a House.",
            WorldEventText.Describe(new(6, 0, "housing_blocked", AgentId + ":no_legal_site"), snapshot));
    }

    [Fact]
    public void MissingSnapshotStillSupportsDelimiterFreeLegacyNamesAndSafeUnknownActors()
    {
        Assert.Equal("Scout ate.", WorldEventText.Describe(new(1, 0, "food_consumed", "scout"), null));
        Assert.Equal("Someone died.", WorldEventText.Describe(new(2, 0, "inhabitant_removed", AgentId), null));
        Assert.Equal("Someone ate.", WorldEventText.Describe(new(3, 0, "food_consumed", ""), null));
        Assert.Equal("Someone planted something new.", WorldEventText.Describe(new(4, 0, "field_planted", ""), null));
    }

    [Theory]
    [InlineData("used:812:limit:1000", "Model calls: 812 of 1,000 used across all worlds.")]
    [InlineData("used:8:limit:10", "Model calls: 8 of 10 used across all worlds.")]
    [InlineData("used:8", "Model calls: 80% of the limit used across all worlds.")]
    public void ModelCallWarningNamesTheInstallationCountAndWhereToRaiseTheLimit(string detail, string count)
    {
        Assert.True(GameUiText.IsPlayerFacingEvent("model_call_warning"));
        Assert.Equal(count + " Your worlds pause at the limit; raise it in Settings → Game.",
            WorldEventText.Describe(new(1, 0, "model_call_warning", detail), null));
    }

    private static OwnerWorldSnapshot Snapshot(params OwnerWorldInhabitant[] people) =>
        new("event-names", 1, "map", [], [], [], null, 1) { Inhabitants = people };

    private static OwnerWorldInhabitant Person(string id, string name, string lifecycle = "active") =>
        new(id, name, lifecycle, new(0, 0), 8_000, [], [],
            new("idle", null, null, [], ""), new(new(0, 0), [], []), false);
}
