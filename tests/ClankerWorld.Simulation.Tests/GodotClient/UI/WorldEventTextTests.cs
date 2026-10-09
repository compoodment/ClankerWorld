using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldEventTextTests
{
    private const string FounderId = "founder:00000000000000000000000000000001";
    private const string AgentId = "agent:00000000000000000000000000000099";
    private const string ChildId = "world:inhabitant:birth:" + FounderId + ":" + AgentId + ":1";

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    public void IncompleteDeveloperEditDetailsHaveASafeDescription(string detail)
    {
        Assert.Equal("Developer edit.", WorldEventText.Describe(new(1, 0, "developer_edit", detail), null));
    }

    [Theory]
    [InlineData("set_need", "fullness", 62, "Aster's fullness set to 62%")]
    [InlineData("give_goods", "wood", 2, "Aster received 2 wood")]
    [InlineData("remove_goods", "wood", 1, "removed 1 wood from Aster")]
    [InlineData("add_skill", "farming", 0, "added farming skill to Aster")]
    [InlineData("remove_skill", "smithing", 0, "removed smithing skill from Aster")]
    [InlineData("start_partnership", "partnership", 0, "started a partnership between Aster and Mira")]
    [InlineData("end_partnership", "partnership", 0, "ended the partnership between Aster and Mira")]
    public void DeveloperEditEventsDescribeTheActualChange(string operation, string value, int amount, string expected)
    {
        var action = new ClankerWorld.Simulation.Playtest.PrivateWorldDeveloperEdit("world", 12, ChildId, operation, value, amount, FounderId);
        var detail = System.Text.Json.JsonSerializer.Serialize(action);
        Assert.True(GameUiText.IsPlayerFacingEvent("developer_edit"));
        Assert.Equal("Developer edit: " + expected + ".", WorldEventText.Describe(new(13, 1, "developer_edit", detail),
            Snapshot(Person(ChildId, "Aster"), Person(FounderId, "Mira"))));
    }

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
                "brought back supplies they couldn't deliver and left them at their household's pile"),
            ("instruction_not_understood", id + ":private-instruction-0000000001",
                "didn't understand your order. Try one of the listed tasks, or use Suggest instead"),
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
    public void RecoveringUnusedTownMaterialsNamesTheCarrierAndWarehouseDestination()
    {
        var worldEvent = new OwnerWorldEvent(1, 1, "town_project_material_recovered", ChildId + ":unknown-project:unused-load:2:wood");
        // Picking materials up is hidden from the log; the returned line reports the move.
        Assert.False(GameUiText.IsPlayerFacingEvent(worldEvent.Kind));
        Assert.Equal("Aster is taking unused materials from a Town project back to the Town Warehouse.",
            WorldEventText.Describe(worldEvent, Snapshot(Person(ChildId, "Aster"))));
    }

    [Fact]
    public void FieldEventsUseCompleteWorkerNamesAndReadHarvestConditionsFromTheEnd()
    {
        var snapshot = Snapshot(Person(ChildId, "Aster"));
        var cases = new[]
        {
            ("field_work_started", ChildId + ":field-12-7:Till", "Aster started work on a field."),
            ("field_prepared", ChildId + ":field-12-7:", "Aster tilled a field."),
            ("field_planted", ChildId + ":field-12-7:cultivated_greens", "Aster planted cultivated greens."),
            ("field_tended", ChildId + ":field-12-7:grain", "Aster tended grain."),
            ("field_harvested", ChildId + ":field-12-7:potatoes", "Aster harvested potatoes."),
            ("field_ready", "field-12-7", "A field is ready to harvest."),
            ("field_work_interrupted", "field-12-7", "Work on a field stopped before it was finished."),
            ("crop_weather_loss", "field-12-7:harvest:2:snow", "Snow damaged a crop, so its harvest will be smaller."),
            ("crop_moisture_effect", "field-12-7:harvest:2:wet:70", "Damp soil gave a crop a bigger harvest."),
            ("crop_moisture_effect", "field-12-7:harvest:2:dry:10", "Dry soil gave a crop a smaller harvest."),
        };
        foreach (var (kind, detail, expected) in cases)
        {
            // Starting and tending repeat what the tilled, planted and harvested lines report.
            Assert.Equal(kind is not ("field_work_started" or "field_tended"), GameUiText.IsPlayerFacingEvent(kind));
            var worldEvent = new OwnerWorldEvent(1, 1, kind, detail);
            Assert.Equal(expected, WorldEventText.Describe(worldEvent, snapshot));
            Assert.Equal(detail, worldEvent.Detail);
        }
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
                "Second Town's council agreed to let Aster join. They have one game day to accept."),
            ("town_admission_lapsed", $"town:first|{AgentId}|town:first:proposal:2|joined_elsewhere",
                "Aster didn't join First Town: the approval no longer fits their situation."),
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
        Assert.Equal("Aster's House is overcrowded: it has more residents than places.",
            WorldEventText.Describe(new(7, 0, "housing_blocked", AgentId + ":overcrowded"), snapshot));
        Assert.Equal("Aster is waiting for every adult in the other household to agree to the move.",
            WorldEventText.Describe(new(8, 0, "housing_blocked", AgentId + ":awaiting_answer"), snapshot));
    }

    [Theory]
    [InlineData("volunteer", "Aster volunteered to move out of their overcrowded House.")]
    [InlineData("latest_unrelated_arrival", "Aster must move out of their overcrowded House. They're the newest arrival who isn't family.")]
    [InlineData("latest_arrival", "Aster must move out of their overcrowded House. They're its newest arrival.")]
    public void RelocationNoticeExplainsTheSelectionWithoutSplittingAnAgentIdentity(string reason, string expected)
    {
        var snapshot = Snapshot(Person(FounderId, "Wrong parent"), Person(ChildId, "Aster"));
        var worldEvent = new OwnerWorldEvent(1, 4, "relocation_notice", $"{ChildId}|household:camp-alpha|{reason}|100");
        Assert.True(GameUiText.IsPlayerFacingEvent(worldEvent.Kind));
        Assert.Equal(expected, WorldEventText.Describe(worldEvent, snapshot));
        Assert.Equal($"{ChildId}|household:camp-alpha|{reason}|100", worldEvent.Detail);
    }

    [Theory]
    [InlineData("room", "the House now has enough places")]
    [InlineData("care", "their dependent children still need their care")]
    [InlineData("family", "the household's family arrangements changed")]
    [InlineData("replaced", "another adult volunteered to move instead")]
    [InlineData("no_house", "the household no longer holds that House")]
    [InlineData("not_needed", "the household's housing needs changed")]
    public void CancelledRelocationExplainsWhyTheNoticeEnded(string reason, string expected)
    {
        var snapshot = Snapshot(Person(AgentId, "Aster"));
        Assert.True(GameUiText.IsPlayerFacingEvent("relocation_cancelled"));
        Assert.Equal($"Aster's move-out notice was cancelled: {expected}.", WorldEventText.Describe(
            new(1, 4, "relocation_cancelled", $"{AgentId}|household:camp-alpha|{reason}"), snapshot));
    }

    [Theory]
    [InlineData("market_built", "town:first|town:first:proposal:1:construction:market|hall|2", "Riverbend's Market was built. Adults can take a stall there to sell goods.")]
    [InlineData("market_stall_built", "town:first|market|stall|1", "Riverbend's Market gained another stall.")]
    [InlineData("market_stock_collected", "town:first|market|stall|" + AgentId + "|lot", "Aster took their goods back from the Market stall.")]
    [InlineData("market_trade_offered", "town:first|market|stall|" + AgentId + "|" + FounderId + "|offer", "Mira offered Aster a trade at the Market.")]
    [InlineData("market_trade_completed", "town:first|market|stall|" + AgentId + "|" + FounderId + "|offer", "Mira bought goods from Aster at the Market.")]
    public void MarketEventsNameTheTownAndTheRightTrader(string kind, string detail, string expected)
    {
        Assert.True(GameUiText.IsPlayerFacingEvent(kind));
        var snapshot = Snapshot(Person(AgentId, "Aster"), Person(FounderId, "Mira")) with
        {
            Towns = [new("town:first", "Riverbend", "founded", 0, [], [], [])],
        };
        Assert.Equal(expected, WorldEventText.Describe(new(1, 4, kind, detail), snapshot));
    }

    [Theory]
    [InlineData("voluntary", "Aster left their household. They can go back for their belongings.")]
    [InlineData("displaced", "Aster moved out of their overcrowded House. They can go back for their belongings.")]
    public void DepartureDistinguishesDisplacementFromAnOrdinaryMove(string reason, string expected)
    {
        Assert.Equal(expected, WorldEventText.Describe(
            new(1, 4, "household_left", $"{AgentId}|household:camp-alpha|{reason}|2"), Snapshot(Person(AgentId, "Aster"))));
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

    [Theory]
    [InlineData("land-ruling:town:first:4", "Land hearing decided: confirm.", "A Town posted the result of a land ruling. See the Towns page for what changed and why.")]
    [InlineData("land-transfer:town:first:5", "Voluntary household permission transfer completed.", "A Town posted the outcome of a land handover between households. See the Towns page.")]
    [InlineData("town:first:proposal:6", "land_use proposal passed: Grant household use.", "A Town's council made a decision. See the Towns page for the result.")]
    public void OnlyCouncilResultNoticesAreDescribedAsCouncilDecisions(string subject, string notice, string expected)
    {
        Assert.Equal(expected, WorldEventText.Describe(new(1, 0, "town_civic_result", $"town:first|{subject}|{notice}"), Snapshot()));
        Assert.Equal(expected, WorldEventText.Describe(new(1, 0, "town_civic_result", $"town:first|{subject}|{notice}"), null));
    }

    [Theory]
    [InlineData("land_transfer_settled", "The land handover went ahead after everyone involved agreed.")]
    [InlineData("land_transfer_blocked", "The land handover stopped because its terms no longer apply. ")]
    public void TransferOutcomeLinesStartWithACapital(string kind, string start)
    {
        Assert.StartsWith(start, WorldEventText.Describe(new(1, 0, kind, "town:first|land-transfer:town:first:5|1||done"), Snapshot()), StringComparison.Ordinal);
    }

    [Fact]
    public void FamilyLinesNameThePeopleTheRecordsHoldAtThatMoment()
    {
        var child = Person(ChildId, "Corin") with
        {
            Relationships =
            [
                new("parent-1", FounderId, "biological_parentage", "accepted", "family", 5, "child"),
                new("parent-2", AgentId, "biological_parentage", "accepted", "family", 5, "child"),
                new("care-1", AgentId, "caregiver", "accepted", "family", 30),
            ],
        };
        var aster = Person(AgentId, "Aster") with
        {
            Relationships = [new("partner-1", FounderId, "partnership", "accepted", "private", 20, "partner")],
        };
        var snapshot = Snapshot(aster, Person(FounderId, "Mira"), child);
        string Line(long tick, string kind, string detail) => WorldEventText.Describe(new(1, tick, kind, detail), snapshot);

        Assert.Equal("Corin was born to Mira and Aster.", Line(5, "child_born", ChildId));
        Assert.Equal("Aster and Mira became partners.", Line(20, "partnership_accepted", AgentId));
        // A partnership that began at another moment never names a later partner.
        Assert.Equal("Aster formed a partnership.", Line(21, "partnership_accepted", AgentId));
        Assert.Equal("Aster's partnership ended.", Line(40, "partnership_ended", AgentId));
        Assert.Equal("Aster is now caring for Corin.", Line(30, "caregiver_assigned", ChildId));
        Assert.Equal("Corin has a new caregiver.", Line(31, "caregiver_assigned", ChildId));
        Assert.Equal("Aster agreed to look after Corin.", Line(30, "guardian_assigned", ChildId));
        Assert.Equal("Corin needs a guardian. No adult has offered to look after them yet.", Line(1, "guardian_needed", ChildId));
        Assert.Equal("Aster agreed to take over caring for Corin.", Line(1, "replacement_care_accepted", AgentId + "|" + ChildId));
        Assert.Equal("Aster's will was carried out, and their belongings went to the people it names.",
            Line(1, "estate_will_accepted", "estate:" + AgentId + ":44:even:2"));
        Assert.Equal("Aster left no usable will, so their belongings went to their household.",
            Line(1, "estate_will_default", "estate:" + AgentId + ":44:invalid_estate_or_heir"));
        Assert.Equal("Aster agreed to let Mira treat them.", Line(1, "medical_care_allowed", AgentId + ":medical_caregiver:" + FounderId));
        Assert.Equal("Aster gave their handcart and its load to Mira.", Line(1, "handcart_transferred", AgentId + ":cart-1:" + FounderId));
        Assert.Equal("Aster's handcart can't go this way. Park it or choose another route; its load is safe.",
            Line(1, "handcart_blocked", AgentId + ":steep"));
    }

    [Theory]
    [InlineData("exploration_started", AgentId + ":10,12", "Aster set out to scout.")]
    [InlineData("exploration_return_started", AgentId, "Aster is heading back from scouting.")]
    [InlineData("exploration_completed", AgentId + ":visited=42", "Aster came back from scouting.")]
    [InlineData("exploration_aborted", AgentId + ":return_blocked", "Aster's scouting trip ended before they got back: the way home was blocked.")]
    [InlineData("exploration_aborted", AgentId + ":interrupted_movement", "Aster's scouting trip was cut short.")]
    public void ScoutingTripsReachTheLog(string kind, string detail, string expected)
    {
        Assert.True(GameUiText.IsPlayerFacingEvent(kind));
        Assert.Equal(expected, WorldEventText.Describe(new(1, 1, kind, detail), Snapshot(Person(AgentId, "Aster"))));
    }

    [Theory]
    // Life events the log used to leave out.
    [InlineData("marriage_accepted", true)]
    [InlineData("marriage_surname_agreed", true)]
    [InlineData("skill_learned", true)]
    [InlineData("animal_tamed", true)]
    [InlineData("animal_born", true)]
    [InlineData("animal_died", true)]
    [InlineData("animal_transferred", true)]
    // Every new tile a scout steps on would flood the log.
    [InlineData("exploration_discovered", false)]
    // Routine steps that the next line already reports.
    [InlineData("food_consumed", false)]
    [InlineData("food_harvested", false)]
    [InlineData("recipe_started", false)]
    [InlineData("handcart_loaded", false)]
    [InlineData("land_transfer_read", false)]
    [InlineData("settlement_founded", false)]
    public void EventLogShowsLifeEventsAndLeavesOutRoutineSteps(string kind, bool shown) =>
        Assert.Equal(shown, GameUiText.IsPlayerFacingEvent(kind));

    [Fact]
    public void MarriageLineUsesThePlayerWordingForTheRecordedSentence()
    {
        var worldEvent = new OwnerWorldEvent(1, 1, "marriage_accepted",
            "Aster and Mira agreed to marry; their shared surname is still undecided.");
        Assert.Equal("Aster and Mira agreed to marry. They haven't chosen a shared surname yet.",
            WorldEventText.Describe(worldEvent, Snapshot()));
    }

    private static OwnerWorldSnapshot Snapshot(params OwnerWorldInhabitant[] people) =>
        new("event-names", 1, "map", [], [], [], null, 1) { Inhabitants = people };

    private static OwnerWorldInhabitant Person(string id, string name, string lifecycle = "active") =>
        new(id, name, lifecycle, new(0, 0), 8_000, [], [],
            new("idle", null, null, [], ""), new(new(0, 0), [], []), false);
}
