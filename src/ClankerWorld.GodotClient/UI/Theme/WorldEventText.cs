using System.Globalization;
using System.Text.Json;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Player event descriptions, derived without rewriting accepted history.</summary>
public static class WorldEventText
{
    public const string ContinuityRisk = "The world is at risk of dying out.";

    private static string DescribeDeveloperEdit(string detail, OwnerWorldSnapshot? snapshot)
    {
        try
        {
            var edit = JsonSerializer.Deserialize<OwnerDeveloperEditAction>(detail);
            if (edit is null || string.IsNullOrWhiteSpace(edit.AgentId) || string.IsNullOrWhiteSpace(edit.Operation) ||
                string.IsNullOrWhiteSpace(edit.Value)) return "Developer edit.";
            var name = Name(snapshot, edit.AgentId);
            var value = GameUiText.HumanizeIdentifier(edit.Value).ToLowerInvariant();
            var change = edit.Operation switch
            {
                "set_need" => $"{name}'s {value} set to {edit.Amount}%",
                "give_goods" => $"{name} received {edit.Amount} {value}",
                "remove_goods" => $"removed {edit.Amount} {value} from {name}",
                "add_skill" => $"added {value} skill to {name}",
                "remove_skill" => $"removed {value} skill from {name}",
                "add_animal" => $"added a {value.Replace(':', ' ')} to {name}'s household animal yard",
                "start_partnership" => $"started a partnership between {name} and {Name(snapshot, edit.OtherAgentId ?? "")}",
                "end_partnership" => $"ended the partnership between {name} and {Name(snapshot, edit.OtherAgentId ?? "")}",
                _ => "saved a change",
            };
            return "Developer edit: " + change + ".";
        }
        catch (JsonException) { return "Developer edit."; }
    }

    public static bool OffersNewcomer(OwnerWorldSnapshot? snapshot) =>
        snapshot is { ContinuityRuleActive: true, FounderSetup.Started: true };

    public static string Describe(OwnerWorldEvent worldEvent, OwnerWorldSnapshot? snapshot)
    {
        var parts = worldEvent.Detail.Split(':', StringSplitOptions.RemoveEmptyEntries);
        // The host records the marriage sentence itself; older saves keep its first wording.
        if (worldEvent.Kind == "marriage_accepted")
            return worldEvent.Detail.Replace("; their shared surname is still undecided.",
                ". They haven't chosen a shared surname yet.", StringComparison.Ordinal);
        if (worldEvent.Kind is "marriage_surname_agreed" or "marriage_surname_draw")
            return worldEvent.Detail;
        if (worldEvent.Kind == "marriage_surname_blocked")
            return "The shared surname would make a name too long. Shorten the agent's name and resume the surname conversation; both names are unchanged.";
        string ThingAt(int index) => index >= 0 && index < parts.Length
            ? GameUiText.HumanizeIdentifier(parts[index])
            : "something new";
        var buildingName = snapshot?.PlacedBuildings.FirstOrDefault(building =>
            IsLeadingId(worldEvent.Detail, building.InstanceId))?.DisplayName ?? "building";
        var guestName = snapshot?.Inhabitants.OrderByDescending(person => person.Id.Length).FirstOrDefault(person =>
            worldEvent.Detail.EndsWith(":" + person.Id, StringComparison.Ordinal))?.DisplayName ?? "The guest";
        var civicTownId = worldEvent.Detail.Split('|', 3)[0];
        var civicTownName = snapshot?.Towns.FirstOrDefault(town => town.Id == civicTownId)?.Name ?? "A Town";
        var townProjectName = worldEvent.Kind.StartsWith("town_project_", StringComparison.Ordinal)
            ? TownProjectForEvent(snapshot, worldEvent.Detail)?.Name ?? "a Town project" : "a Town project";
        var townProjectSubject = townProjectName == "a Town project" ? "A Town project" : townProjectName;
        var marketFields = worldEvent.Kind.StartsWith("market_", StringComparison.Ordinal)
            ? worldEvent.Detail.Split('|') : Array.Empty<string>();
        var marketSeller = marketFields.Length > 3 ? Name(snapshot, marketFields[3]) : "Someone";
        var marketBuyer = marketFields.Length > 4 ? Name(snapshot, marketFields[4]) : "a customer";
        var marketBuyerSubject = marketFields.Length > 4 ? marketBuyer : "A customer";
        var animalName = snapshot?.Animals.Where(animal => worldEvent.Detail.Split(':').Contains(animal.Id, StringComparer.Ordinal))
            .OrderBy(animal => worldEvent.Detail.IndexOf(animal.Id, StringComparison.Ordinal)).FirstOrDefault()?.Name ?? "An animal";

        return worldEvent.Kind switch
        {
            "developer_edit" => DescribeDeveloperEdit(worldEvent.Detail, snapshot),
            "animals_arrived" => "Wild animals have appeared in the area.",
            "animal_tamed" => $"{animalName} was tamed and joined {LeadingName(snapshot, worldEvent.Detail)}'s household.",
            "animal_cared" => $"{animalName} received a day's physical feed and water.",
            "animal_product_ready" => $"{animalName}'s product is ready for local collection.",
            "animal_product_collected" => $"{animalName}'s product was collected for its household.",
            "animal_hide_collected" => $"An adult collected {animalName}'s wild old-age hide.",
            "animal_breeding_started" => $"{animalName} is expecting one young animal; a place is reserved.",
            "animal_born" => $"A {YoungAnimal(snapshot, worldEvent.Detail)}, {animalName}, was born.",
            "animal_died" => $"{animalName} died of old age.",
            "animal_saddled" => $"{animalName} has a household saddle fitted.",
            "horse_mounted" => $"An adult mounted {animalName}.",
            "horse_dismounted" => $"An adult dismounted {animalName}; spare cargo stays at the actual position.",
            "animal_permission_changed" => $"{animalName}'s named care or riding permission changed.",
            "animal_trade_offered" => $"A household offered {animalName}; the receiving adult must agree.",
            "animal_transferred" => $"{animalName} now belongs to {GameUiText.PartyName(snapshot, Field(worldEvent.Detail.Replace(':', '|'), 2))}.",
            "animal_yard_supplied" => "Feed or jug water reached an animal yard.",
            "milk_drunk" => $"{Name(snapshot, worldEvent.Detail)} drank milk and kept the reusable jug.",
            "spoiled_milk_emptied" => "An adult poured away spoiled milk and kept the reusable jug.",
            "milk_stock_picked_up" => "An adult collected a household milk jug for sale stock.",
            "milk_stock_delivered" => "A household milk jug reached the Store or borrowed Market stall.",
            "milk_sale_offered" => "An adult offered one milk for the buyer's exact carried payment; the buyer must agree.",
            "milk_sale_completed" => "Milk was poured into the buyer's jug and the agreed payment changed owners; both jugs stayed with their owners.",
            "milk_sale_cancelled" or "milk_sale_declined" => "The milk exchange ended; held milk is available again and no payment was taken.",
            "world_created" => "A new world has begun.",
            "world_started" => "Time started. Your agents now live on their own.",
            "weather_changed" when parts.Length >= 2 => $"The weather turned to {ThingAt(1).ToLowerInvariant()}.",
            "building_placed" => $"A new {ThingAt(1)} was built.",
            "building_expansion_started" => $"Work began on more storage for the {buildingName}.",
            "building_expanded" => $"The {buildingName} now has more storage.",
            "building_expansion_cancelled" => $"Work on the {buildingName}'s storage stopped. Its materials are free to use again.",
            "expansion_order_paused" => "The expansion you ordered is paused while the worker sees to food or warmth. Its materials stay set aside.",
            "expansion_order_resumed" => "The expansion you ordered has started again.",
            "house_guest_invited" => $"{guestName} may shelter in the {buildingName} during storms.",
            "house_guest_revoked" => $"{guestName} can no longer shelter in the {buildingName} during storms.",
            "build_started" => $"Work began on a new {ThingAt(1)}.",
            "build_completed" => $"The new {ThingAt(1)} is finished.",
            "recipe_started" => $"Work began on {ThingAt(1).ToLowerInvariant()}.",
            "recipe_completed" => $"A batch of {ThingAt(1).ToLowerInvariant()} was made.",
            "house_tool_made" => $"{Name(snapshot, Field(worldEvent.Detail, 0))} made a {GameUiText.ItemName(Field(worldEvent.Detail, 1)).ToLowerInvariant()}.",
            "field_work_started" => $"{LeadingName(snapshot, worldEvent.Detail)} started work on a field.",
            "field_prepared" => $"{LeadingName(snapshot, worldEvent.Detail)} tilled a field.",
            "field_planted" => $"{LeadingName(snapshot, worldEvent.Detail)} planted {ThingAt(parts.Length - 1).ToLowerInvariant()}.",
            "field_tended" => $"{LeadingName(snapshot, worldEvent.Detail)} tended {ThingAt(parts.Length - 1).ToLowerInvariant()}.",
            "field_harvested" => $"{LeadingName(snapshot, worldEvent.Detail)} harvested {ThingAt(parts.Length - 1).ToLowerInvariant()}.",
            "field_ready" => "A field is ready to harvest.",
            "field_work_interrupted" => "Work on a field stopped before it was finished.",
            "crop_weather_loss" => $"{(parts[^1] == "snow" ? "Snow" : "A storm")} damaged a crop, so its harvest will be smaller.",
            "crop_moisture_effect" when parts.Length >= 3 => parts[^2] == "wet"
                ? "Damp soil gave a crop a bigger harvest."
                : "Dry soil gave a crop a smaller harvest.",
            "food_harvested" => $"{Name(snapshot, BeforeLastField(worldEvent.Detail))} gathered food.",
            "food_consumed" => $"{Name(snapshot, worldEvent.Detail)} ate.",
            "tree_planted" => $"{LeadingName(snapshot, worldEvent.Detail)} planted a tree.",
            "tree_replanted" => $"{LeadingName(snapshot, worldEvent.Detail)} replanted a tree.",
            "inhabitant_slept" => $"{Name(snapshot, worldEvent.Detail)} slept.",
            "child_born" => DescribeBirth(snapshot, worldEvent.Detail),
            "inhabitant_removed" => $"{Name(snapshot, worldEvent.Detail)} died.",
            "estate_will_accepted" => $"{EstateOwner(snapshot, worldEvent.Detail)}'s will was carried out, and their belongings went to the people it names.",
            "estate_will_default" => $"{EstateOwner(snapshot, worldEvent.Detail)} left no usable will, so their belongings went to their household.",
            "partnership_accepted" => Partner(snapshot, worldEvent.Detail, worldEvent.WorldTick) is { } partner
                ? $"{Name(snapshot, worldEvent.Detail)} and {partner} became partners."
                : $"{Name(snapshot, worldEvent.Detail)} formed a partnership.",
            "partnership_ended" => $"{Name(snapshot, worldEvent.Detail)}'s partnership ended.",
            "continuity_rule_on" => "Fewer than eight people who aren't elders are alive, so couples must now have children. They can wait up to two days.",
            "continuity_rule_off" => "Eight or more people who aren't elders are alive again, so couples can choose not to have children.",
            "caregiver_assigned" => Carer(snapshot, worldEvent.Detail, worldEvent.WorldTick) is { } carer
                ? $"{carer} is now caring for {Name(snapshot, worldEvent.Detail)}."
                : $"{Name(snapshot, worldEvent.Detail)} has a new caregiver.",
            "guardian_needed" => $"{Name(snapshot, worldEvent.Detail)} needs a guardian. No adult has offered to look after them yet.",
            "guardian_assigned" => Carer(snapshot, worldEvent.Detail, worldEvent.WorldTick) is { } guardian
                ? $"{guardian} agreed to look after {Name(snapshot, worldEvent.Detail)}."
                : $"An adult agreed to look after {Name(snapshot, worldEvent.Detail)}.",
            "guardian_placement_pending" => $"{Name(snapshot, worldEvent.Detail)} has a guardian and will move into their House soon.",
            "guardian_placement_completed" => $"{LeadingName(snapshot, worldEvent.Detail)} moved into their guardian's House.",
            "guardian_placement_cancelled" => $"{Name(snapshot, worldEvent.Detail)}'s move to their guardian's House was called off.",
            "medical_care_allowed" => $"{LeadingName(snapshot, worldEvent.Detail)} agreed to let {AfterMarker(snapshot, worldEvent.Detail, ":medical_caregiver:", "someone")} treat them.",
            "medical_care_revoked" => $"{LeadingName(snapshot, worldEvent.Detail)} no longer wants medical care.",
            "medical_treatment_started" => $"{LeadingName(snapshot, worldEvent.Detail)} began a course of medicine.",
            "medical_treatment_completed" => $"{Name(snapshot, worldEvent.Detail)} finished a course of medicine.",
            "medical_treatment_interrupted" => $"{Name(snapshot, worldEvent.Detail)}'s treatment stopped partway. Medicine already taken is used up.",
            "empty_vessel_picked_up" => $"{LeadingName(snapshot, worldEvent.Detail)} picked up an empty container to bring home.",
            "ornament_worn" => $"{LeadingName(snapshot, worldEvent.Detail)} put on an ornament.",
            "ornament_removed" => $"{LeadingName(snapshot, worldEvent.Detail)} took off an ornament.",
            "ornament_given" => $"{LeadingName(snapshot, worldEvent.Detail)} gave an ornament to {OrnamentGiftRecipient(snapshot, worldEvent.Detail)}.",
            "council_policy_adopted" => $"The council adopted a new policy: {GameUiText.HumanizeIdentifier(worldEvent.Detail).ToLowerInvariant()}.",
            "settlement_trade_completed" => $"{Name(snapshot, worldEvent.Detail)} completed a trade.",
            "tool_request_placed" => $"{LeadingName(snapshot, worldEvent.Detail)} asked the Blacksmith for a tool.",
            "tool_request_accepted" => $"{LeadingName(snapshot, worldEvent.Detail)} agreed to make the tool.",
            "tool_request_refused" => $"{LeadingName(snapshot, worldEvent.Detail)} turned down a request for a tool.",
            "tool_request_withdrawn" => $"{LeadingName(snapshot, worldEvent.Detail)} no longer wants the tool they asked for.",
            "tool_request_ready" => "A requested tool is ready. It's handed over once it's paid for.",
            "tool_request_completed" => "A requested tool was paid for and handed over.",
            "tool_request_interrupted" => "Work on a requested tool stopped. Nothing was lost.",
            "business_trade_offered" => $"{LeadingName(snapshot, worldEvent.Detail)} offered to buy goods at a shop. The goods are held for them.",
            "business_trade_completed" => $"{LeadingName(snapshot, worldEvent.Detail)} bought goods at a shop.",
            "business_trade_cancelled" => $"{LeadingName(snapshot, worldEvent.Detail)}'s purchase at a shop was called off. Nothing changed hands.",
            "store_stock_collected" => $"{LeadingName(snapshot, worldEvent.Detail)} is carrying goods to their Store.",
            "store_stock_delivered" => $"{LeadingName(snapshot, worldEvent.Detail)} brought goods to their Store to sell.",
            "handcart_attached" => $"{LeadingName(snapshot, worldEvent.Detail)} attached their handcart.",
            "handcart_parked" => $"{LeadingName(snapshot, worldEvent.Detail)} parked their handcart.",
            "handcart_loaded" => $"{LeadingName(snapshot, worldEvent.Detail)} loaded goods into their handcart.",
            "handcart_unloaded" => $"{LeadingName(snapshot, worldEvent.Detail)} unloaded goods from their handcart.",
            "handcart_repaired" => $"{LeadingName(snapshot, worldEvent.Detail)} repaired their handcart.",
            "handcart_transferred" => $"{LeadingName(snapshot, worldEvent.Detail)} gave their handcart and its load to {LastPerson(snapshot, worldEvent.Detail, "someone nearby")}.",
            "handcart_blocked" => $"{LeadingName(snapshot, worldEvent.Detail)}'s handcart can't go this way. Park it or choose another route; its load is safe.",
            "owner_stock_picked_up" => $"{LeadingName(snapshot, worldEvent.Detail)} picked up goods for the delivery you ordered.",
            "owner_stock_delivered" => $"{LeadingName(snapshot, worldEvent.Detail)} delivered the goods you ordered.",
            "carrying_full" => $"{Name(snapshot, worldEvent.Detail)} cannot carry more; a load needs to be stored or set down.",
            "spare_cargo_stored" => $"{LeadingName(snapshot, worldEvent.Detail)} set down spare supplies for their household to make room in their load.",
            "household_delivery_recovered" => $"{LeadingName(snapshot, worldEvent.Detail)} brought back supplies they couldn't deliver and left them at their household's pile.",
            "equipment_equipped" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} equipped an item.",
            "equipment_repair_started" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} began repairing an item.",
            "equipment_repaired" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} repaired an item.",
            "equipment_repair_interrupted" => "Repair stopped; its unused materials are available again.",
            "agent_knowledge_artifact_created" or "agent_knowledge_artifact_read" or "agent_knowledge_shared" or
                "agent_knowledge_writing_started" or "agent_knowledge_writing_cancelled" or "agent_knowledge_material_collected" or
                "agent_knowledge_artifact_collected" or "agent_knowledge_artifact_stored" =>
                DescribeWrittenKnowledge(worldEvent, snapshot),
            "skill_learned" => DescribeSkill(worldEvent.Detail, snapshot),
            "inhabitant_building_proposed" => $"{LeadingName(snapshot, worldEvent.Detail)} suggested a new building design.",
            "instruction_not_understood" => $"{Name(snapshot, BeforeLastField(worldEvent.Detail))} didn't understand your order. " +
                "Try one of the listed tasks, or use Suggest instead.",
            "town_civic_law" => $"{civicTownName} recorded a law decision. See the Towns page for its wording and scope.",
            "town_civic_government" => $"{civicTownName} recorded a resident government decision. See the Towns page for the vote or handover.",
            "town_civic_mayor" => $"{civicTownName} recorded a mayoral election or office change. See the Towns page for its result.",
            "market_built" => $"{civicTownName}'s Market was built. Adults can take a stall there to sell goods.",
            "market_stall_built" => $"{civicTownName}'s Market gained another stall.",
            "market_stall_borrowed" => $"{marketSeller} took a free stall at the Market.",
            "market_stock_loaded" => $"{marketSeller} is carrying household goods to the Market.",
            "market_stock_delivered" => $"{marketSeller} set out goods at their Market stall.",
            "market_stock_collected" => $"{marketSeller} took their goods back from the Market stall.",
            "market_stall_left" => $"{marketSeller} gave up their Market stall.",
            "market_trade_offered" => $"{marketBuyerSubject} offered {marketSeller} a trade at the Market.",
            "market_trade_completed" => $"{marketBuyerSubject} bought goods from {marketSeller} at the Market.",
            "market_trade_cancelled" => $"{marketSeller} and {marketBuyer}'s Market trade was called off. Nothing changed hands.",
            "town_civic_council" => $"{civicTownName}'s council changed.",
            "town_civic_election" => $"{civicTownName}'s council election opened.",
            "town_civic_runoff" => $"{civicTownName}'s council election was tied, so there will be a runoff.",
            "town_civic_proposal" => $"A proposal was submitted to {civicTownName}'s council.",
            // Rulings and household transfers post their outcome as a result notice, but no council decided them.
            "town_civic_result" when Field(worldEvent.Detail, 1).StartsWith("land-ruling:", StringComparison.Ordinal) =>
                $"{civicTownName} posted the result of a land ruling. See the Towns page for what changed and why.",
            "town_civic_result" when Field(worldEvent.Detail, 1).StartsWith("land-transfer:", StringComparison.Ordinal) =>
                $"{civicTownName} posted the outcome of a land handover between households. See the Towns page.",
            "town_civic_result" => $"{civicTownName}'s council made a decision. See the Towns page for the result.",
            "town_civic_land_use" => $"A household land request in {civicTownName} has new information. See its plot for approval progress.",
            "land_use_granted" => $"{LandHousehold(snapshot, worldEvent.Detail)} may now use a plot in {LandTown(snapshot, worldEvent.Detail)}. Turn on the household use filter to see it.",
            "land_use_requested" => $"{LandHousehold(snapshot, worldEvent.Detail)} asked to use a plot in {LandTown(snapshot, worldEvent.Detail)}. The council must approve it first.",
            "town_civic_nonviolent_hearing" => $"{civicTownName}'s vote on who will judge a case has news. See the Towns page.",
            "town_civic_law_case" => $"{civicTownName} posted notice of a case. See the Towns page for the complaint and the deadline to answer.",
            "town_civic_remedy" => $"{civicTownName} posted an offer to settle a case. Nobody has agreed to it yet.",
            "law_case_opened" or "law_case_evidence" or "law_case_response" or "law_case_inspected" or
            "law_case_relayed" or "law_case_judge_consent" or "law_case_judge_election" or "law_case_judge_assigned" or
            "law_case_finding" or "law_case_reopen_requested" or "law_case_reopened" or "law_case_rejected" or
            "law_case_offer" or "law_case_offer_response" or "law_case_remedy_effect" => DescribeNonviolentHearing(worldEvent, snapshot),
            "town_civic_land_hearing" => $"{civicTownName} posted notice of a land case. See the Towns page for the plot and the deadline.",
            "land_case_opened" or "land_case_notice" or "land_case_evidence" or "land_case_response" or
            "land_case_judge_consent" or "land_case_judge_election" or "land_case_judge_assigned" or
            "land_case_ruling" or "land_case_reopen_requested" or "land_case_reopened" or
            "land_case_inspected" or "land_case_relayed" or "land_case_rejected" => DescribeLandHearing(worldEvent, snapshot),
            "land_transfer_proposed" or "land_transfer_read" or "land_transfer_consent" or "land_transfer_withdrawn" or
            "land_transfer_settled" or "land_transfer_blocked" => DescribeLandTransfer(worldEvent, snapshot),
            "town_land_claimed" => $"{civicTownName}'s council claimed the land next to the Town. Turn on the Town title filter to see it.",
            "town_civic_cancelled" => $"An unfinished election in {civicTownName} was cancelled.",
            "town_project_approved" => $"The council approved {townProjectName}. It still needs materials and builders.",
            "town_project_blocked" => $"Work on {townProjectName} is blocked. See the Towns page for what is needed.",
            "town_project_resumed" => $"Work on {townProjectName} can continue.",
            "town_project_cancelled" => $"{townProjectSubject} can't be built where it was approved, so the Town stopped it. " +
                "Its materials stay where they are; see the Towns page for why.",
            "town_project_donated" => $"{LeadingName(snapshot, worldEvent.Detail)} donated materials to {townProjectName}.",
            "town_project_material_picked_up" => $"{LeadingName(snapshot, worldEvent.Detail)} picked up materials for {townProjectName}.",
            "town_project_material_recovered" => $"{LeadingName(snapshot, worldEvent.Detail)} is taking unused materials from {townProjectName} back to the Town Warehouse.",
            "town_project_material_delivered" => $"{LeadingName(snapshot, worldEvent.Detail)} brought materials to the site for {townProjectName}.",
            "town_project_material_returned" => worldEvent.Detail.EndsWith(":ground", StringComparison.Ordinal)
                ? $"{LeadingName(snapshot, worldEvent.Detail)} set down unused Town materials from {townProjectName}."
                : $"{LeadingName(snapshot, worldEvent.Detail)} returned unused materials from {townProjectName} to the Town Warehouse.",
            "town_project_worked" => $"{LeadingName(snapshot, worldEvent.Detail)} worked on {townProjectName}.",
            "town_project_completed" => $"{townProjectSubject} is finished.",
            "town_founding_started" => "Your first Town is being set up.",
            "town_resident_joined" => $"{ResidentName(snapshot, worldEvent)} joined {ResidentTownName(snapshot, worldEvent)}.",
            "town_resident_left" => $"{ResidentName(snapshot, worldEvent)} left {ResidentTownName(snapshot, worldEvent)}.",
            "town_membership_evaluated" => $"{LeadingName(snapshot, worldEvent.Detail)} is now an adult but doesn't belong to a Town yet.",
            "town_admission_accepted" => DescribeAdmission(worldEvent.Detail, snapshot),
            "town_abandoned" => $"{civicTownName} has no living residents and is abandoned. Its buildings, border and Roads remain.",
            "town_revived" => $"People live in {civicTownName} again. Its shared stock is theirs to use.",
            "town_resettled" => $"{Name(snapshot, Field(worldEvent.Detail, 1))} moved into abandoned {civicTownName}. Its laws and everyone's property stay as they were.",
            "town_stock_salvaged" => $"{Name(snapshot, Field(worldEvent.Detail, 1))} salvaged {Field(worldEvent.Detail, 3)} {GameUiText.ItemName(Field(worldEvent.Detail, 2)).ToLowerInvariant()} from abandoned {civicTownName}.",
            "town_admission_approved" => $"{civicTownName}'s council agreed to let {Name(snapshot, Field(worldEvent.Detail, 1))} join. They have one game day to accept.",
            "town_admission_lapsed" when Field(worldEvent.Detail, 3) == "acceptance_expired" => $"{Name(snapshot, Field(worldEvent.Detail, 1))} didn't accept in time, so their place in {civicTownName} lapsed. They can ask the council again.",
            "town_admission_lapsed" => $"{Name(snapshot, Field(worldEvent.Detail, 1))} didn't join {civicTownName}: the approval no longer fits their situation.",
            "town_building_assigned" => DescribeTownBuilding(snapshot, worldEvent.Detail),
            "town_border_expanded" => $"{Sentence(TownNamed(snapshot, worldEvent.Detail, null) ?? "the Town")}'s border grew.",
            "town_founded" => TownNamed(snapshot, worldEvent.Detail, null) is { } founded
                ? $"{founded}, your first Town, was founded." : "Your first Town was founded.",
            "bridge_built" when parts.Length > 0 && parts[0] == "road" => "A bridge was built where a new Road crosses the river.",
            "bridge_built" => "Agents crossed a river here so often that a bridge was built.",
            "household_work_resumed" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} picked up household work that had been left unfinished.",
            "household_left" => DescribeHouseholdDeparture(snapshot, worldEvent.Detail),
            "household_founded" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} started a new household. They'll need to build a House.",
            "personal_goods_collected" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} collected their personal belongings.",
            "personal_goods_stored" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} stored their belongings. They still own them.",
            "borrowed_goods_returned" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} returned household goods.",
            "replacement_care_accepted" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} agreed to take over caring for {Name(snapshot, Field(worldEvent.Detail, 1))}.",
            "housing_request_made" => $"{LeadingName(snapshot, worldEvent.Detail)} asked {HouseholdAfterAgent(snapshot, worldEvent.Detail)} for a place to live in their House.",
            "household_joined" => $"{LeadingName(snapshot, worldEvent.Detail)} now lives with {HouseholdAfterAgent(snapshot, worldEvent.Detail)}.",
            "housing_request_refused" => $"{HouseholdAfterAgent(snapshot, worldEvent.Detail)} did not agree to let {LeadingName(snapshot, worldEvent.Detail)} move in.",
            "housing_request_expired" => $"{HouseholdAfterAgent(snapshot, worldEvent.Detail)} did not answer {LeadingName(snapshot, worldEvent.Detail)}'s request to move in.",
            "relocation_notice" => DescribeRelocationNotice(snapshot, worldEvent.Detail),
            "relocation_cancelled" => DescribeRelocationCancellation(snapshot, worldEvent.Detail),
            "housing_blocked" when worldEvent.Detail.EndsWith(":overcrowded", StringComparison.Ordinal) =>
                $"{LeadingName(snapshot, worldEvent.Detail)}'s House is overcrowded: it has more residents than places.",
            "housing_blocked" when worldEvent.Detail.EndsWith(":awaiting_answer", StringComparison.Ordinal) =>
                $"{LeadingName(snapshot, worldEvent.Detail)} is waiting for every adult in the other household to agree to the move.",
            "housing_blocked" => $"{LeadingName(snapshot, worldEvent.Detail)} has no home: {HousingReason(worldEvent.Detail)}.",
            "exploration_started" or "exploration_return_started" or "exploration_completed" or "exploration_aborted" =>
                DescribeScouting(worldEvent, snapshot),
            "paused" => "The world was paused.",
            "resumed" => "The world resumed.",
            "model_call_warning" => DescribeModelCallWarning(parts),
            _ => $"{GameUiText.HumanizeIdentifier(worldEvent.Kind)}.",
        };
    }

    private static string DescribeNonviolentHearing(OwnerWorldEvent worldEvent, OwnerWorldSnapshot? snapshot)
    {
        var fields = worldEvent.Detail.Split('|', 3);
        var town = snapshot?.Towns.FirstOrDefault(item => item.Id == fields[0])?.Name ?? "A Town";
        var actor = fields.Length == 3 ? snapshot?.Inhabitants.FirstOrDefault(person => person.Id == fields[2])?.DisplayName : null;
        return worldEvent.Kind switch
        {
            "law_case_opened" => $"{town} opened a case about someone's conduct. Nothing has been decided yet.",
            "law_case_evidence" => $"New evidence was added to a case in {town}.",
            "law_case_response" => $"{actor ?? "Someone"} answered a case in {town}.",
            "law_case_inspected" => $"{actor ?? "Someone"} read the file for a case in {town}.",
            "law_case_relayed" => $"{actor ?? "Someone"} told someone nearby about a case.",
            "law_case_judge_consent" => $"An adult in {town} offered to judge a case.",
            "law_case_judge_election" => $"{town}'s vote on who will judge a case has news. See the Towns page.",
            "law_case_judge_assigned" => $"{town} chose a judge for a case.",
            "law_case_finding" => $"{town} reached a decision in a case. See the Towns page for the reasons.",
            "law_case_reopen_requested" => $"Someone asked {town} to hear a case again. Earlier decisions stand for now.",
            "law_case_reopened" => $"{town} reopened a case.",
            "law_case_offer" => $"Someone offered to settle a case in {town}. Everyone who would contribute must agree.",
            "law_case_offer_response" => $"{actor ?? "Someone"} answered an offer to settle a case.",
            "law_case_remedy_effect" => $"{actor ?? "Someone"} did their part of an agreed settlement in {town}.",
            _ => $"{town} turned down a request in a case. Nothing was imposed.",
        };
    }

    private static string DescribeLandTransfer(OwnerWorldEvent worldEvent, OwnerWorldSnapshot? snapshot)
    {
        var fields = worldEvent.Detail.Split('|', 5);
        var town = fields.Length == 5 ? snapshot?.Towns.FirstOrDefault(item => item.Id == fields[0]) : null;
        var transfer = fields.Length == 5 ? town?.LandTransfers.FirstOrDefault(item => item.Id == fields[1]) : null;
        var subject = "the land handover";
        if (transfer is not null) subject += " from " + string.Join(" and ", transfer.Parties.Where(party => party.Kind == "source")
            .Select(party => party.HouseholdName)) + " to " + transfer.TargetHouseholdName;
        var actor = fields.Length == 5 ? snapshot?.Inhabitants.FirstOrDefault(item => item.Id == fields[3])?.DisplayName : null;
        return worldEvent.Kind switch
        {
            "land_transfer_proposed" => $"{actor ?? "An adult"} proposed {subject} in {town?.Name ?? "a Town"}. Every adult involved must agree.",
            "land_transfer_read" => $"{actor ?? "An adult"} read the terms of {subject}.",
            "land_transfer_consent" => $"{actor ?? "An adult"} " + (fields.Length == 5 && fields[4] == "decline"
                ? "turned down" : "agreed to") + $" {subject}.",
            "land_transfer_withdrawn" => $"{actor ?? "The proposer"} withdrew {subject}. Nothing changed.",
            "land_transfer_settled" => $"{Sentence(subject)} went ahead after everyone involved agreed.",
            _ => $"{Sentence(subject)} stopped because its terms no longer apply. Nothing changed.",
        };
    }

    private static string Sentence(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static string DescribeLandHearing(OwnerWorldEvent worldEvent, OwnerWorldSnapshot? snapshot)
    {
        var fields = worldEvent.Detail.Split('|', 5);
        var town = fields.Length >= 2 ? snapshot?.Towns.FirstOrDefault(item => item.Id == fields[0]) : null;
        var caseId = fields.Length >= 2 ? fields[1] : "";
        var number = caseId.Length > 0 ? caseId[(caseId.LastIndexOf(':') + 1)..] : "";
        var subject = number.Length > 0 ? "land case " + number : "a land case";
        var townName = town?.Name ?? "A Town";
        var actor = fields.Length == 5 ? snapshot?.Inhabitants.FirstOrDefault(item => item.Id == fields[3])?.DisplayName : null;
        var action = fields.Length == 5 ? fields[4] : "";
        return worldEvent.Kind switch
        {
            "land_case_opened" => $"{townName} opened {subject}. Current land rights stay in place until it's decided.",
            "land_case_notice" => $"{townName} posted notice for {subject}. See the Towns page for the deadline.",
            "land_case_evidence" => $"New evidence was added to {subject}.",
            "land_case_response" => action == "waive" ? $"{actor ?? "Someone"} chose not to answer {subject}." : $"{actor ?? "Someone"} answered {subject}.",
            "land_case_judge_consent" => $"{actor ?? "An adult resident"} " + (action switch
            {
                "judge_withdraw" => "withdrew their offer to act as mayor",
                "judge_resign" => "resigned as acting mayor",
                _ => "agreed to act as mayor",
            }) + $" for {subject}.",
            "land_case_judge_election" => $"{townName}'s vote for {subject} has news. See the Towns page.",
            "land_case_judge_assigned" => $"{actor ?? "A judge"} will judge {subject}.",
            "land_case_ruling" => $"{townName} ruled on {subject}. See the Towns page for what changed and why.",
            "land_case_reopen_requested" => $"Someone asked {townName} to rehear {subject}. Current rights still stand.",
            "land_case_reopened" => $"{townName} reopened {subject}.",
            "land_case_inspected" => $"{actor ?? "Someone"} read the file for {subject}.",
            "land_case_relayed" => $"{actor ?? "Someone"} told someone nearby about {subject}.",
            _ => $"{townName} turned down a request in {subject}. No property changed hands.",
        };
    }

    private static OwnerWorldTownProject? TownProjectForEvent(OwnerWorldSnapshot? snapshot, string detail)
    {
        if (snapshot is null) return null;
        // Town, agent and proposal IDs contain colons; match their complete known identities.
        var town = snapshot.Towns.OrderByDescending(item => item.Id.Length)
            .FirstOrDefault(item => IsLeadingId(detail, item.Id));
        if (town is not null)
            return town.Projects.FirstOrDefault(project => IsLeadingId(detail, town.Id + ":" + project.Id));
        var person = snapshot.Inhabitants.OrderByDescending(item => item.Id.Length)
            .FirstOrDefault(item => IsLeadingId(detail, item.Id));
        return person is null ? null : snapshot.Towns.SelectMany(item => item.Projects)
            .FirstOrDefault(project => IsLeadingId(detail, person.Id + ":" + project.Id));
    }

    private static string DescribeHouseholdDeparture(OwnerWorldSnapshot? snapshot, string detail)
    {
        var fields = detail.Split('|');
        var actor = Name(snapshot, fields[0]);
        return fields.Length > 2 && fields[2] == "displaced"
            ? $"{actor} moved out of their overcrowded House. They can go back for their belongings."
            : $"{actor} left their household. They can go back for their belongings.";
    }

    private static string DescribeRelocationNotice(OwnerWorldSnapshot? snapshot, string detail)
    {
        var fields = detail.Split('|');
        var actor = Name(snapshot, fields[0]);
        return (fields.Length > 2 ? fields[2] : string.Empty) switch
        {
            "volunteer" => $"{actor} volunteered to move out of their overcrowded House.",
            "latest_unrelated_arrival" => $"{actor} must move out of their overcrowded House. They're the newest arrival who isn't family.",
            "latest_arrival" => $"{actor} must move out of their overcrowded House. They're its newest arrival.",
            _ => $"{actor} has notice to move out of their overcrowded House.",
        };
    }

    private static string DescribeRelocationCancellation(OwnerWorldSnapshot? snapshot, string detail)
    {
        var fields = detail.Split('|');
        var reason = (fields.Length > 2 ? fields[2] : string.Empty) switch
        {
            "room" => "the House now has enough places",
            "care" => "their dependent children still need their care",
            "family" => "the household's family arrangements changed",
            "replaced" => "another adult volunteered to move instead",
            "no_house" => "the household no longer holds that House",
            _ => "the household's housing needs changed",
        };
        return $"{Name(snapshot, fields[0])}'s move-out notice was cancelled: {reason}.";
    }

    /// <summary>A recorded Town admission: who joined, any Town they left and whether dependent children came too.</summary>
    private static string DescribeAdmission(string detail, OwnerWorldSnapshot? snapshot)
    {
        var fields = detail.Split('|');
        if (fields.Length != 4) return "Someone became a Town resident.";
        string TownName(string id) => snapshot?.Towns.FirstOrDefault(town => town.Id == id)?.Name ?? "a Town";
        var text = $"{Name(snapshot, fields[1])} became a resident of {TownName(fields[0])}.";
        if (fields[2] != "none") text += $" They are no longer a resident of {TownName(fields[2])}.";
        if (int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var members) && members > 1)
            text += " Their dependent children moved with them.";
        return text;
    }

    /// <summary>A newborn agent, with the parents their record names when it names any.</summary>
    private static string DescribeBirth(OwnerWorldSnapshot? snapshot, string childId)
    {
        var parents = snapshot?.Inhabitants.FirstOrDefault(person => person.Id == childId)?.Relationships
            .Where(item => item.Type == "biological_parentage" && item.Direction == "child")
            .Select(item => Name(snapshot, item.OtherPartyId)).Where(name => name != "Someone").ToArray() ?? [];
        return parents.Length == 0 ? $"{Name(snapshot, childId)} was born."
            : $"{Name(snapshot, childId)} was born to {string.Join(" and ", parents)}.";
    }

    /// <summary>The partner whose partnership with this agent began at the event's tick, so a later partner never stands in.</summary>
    private static string? Partner(OwnerWorldSnapshot? snapshot, string agentId, long tick) =>
        RelatedAt(snapshot, agentId, tick, "partnership");

    /// <summary>The adult who began caring for a child at the event's tick, as a caregiver or legal guardian.</summary>
    private static string? Carer(OwnerWorldSnapshot? snapshot, string childId, long tick) =>
        RelatedAt(snapshot, childId, tick, "caregiver", "legal_guardian");

    private static string? RelatedAt(OwnerWorldSnapshot? snapshot, string agentId, long tick, params string[] types)
    {
        var other = snapshot?.Inhabitants.FirstOrDefault(person => person.Id == agentId)?.Relationships
            .FirstOrDefault(item => types.Contains(item.Type) && item.EffectiveTick == tick)?.OtherPartyId;
        return other is null ? null : snapshot?.Inhabitants.FirstOrDefault(person => person.Id == other)?.DisplayName;
    }

    /// <summary>The agent whose estate an event settles. Estate IDs are <c>estate:&lt;agent&gt;:&lt;tick&gt;</c>.</summary>
    private static string EstateOwner(OwnerWorldSnapshot? snapshot, string detail)
    {
        const string prefix = "estate:";
        return detail.StartsWith(prefix, StringComparison.Ordinal) ? LeadingName(snapshot, detail[prefix.Length..]) : "Someone";
    }

    /// <summary>The agent named after a fixed marker in an event detail, such as a medical caregiver.</summary>
    private static string AfterMarker(OwnerWorldSnapshot? snapshot, string detail, string marker, string fallback)
    {
        var at = detail.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return fallback;
        var name = LeadingName(snapshot, detail[(at + marker.Length)..]);
        return name == "Someone" ? fallback : name;
    }

    /// <summary>The agent whose complete ID ends an event detail, such as a handcart's new keeper.</summary>
    private static string LastPerson(OwnerWorldSnapshot? snapshot, string detail, string fallback) =>
        snapshot?.Inhabitants.OrderByDescending(person => person.Id.Length)
            .FirstOrDefault(person => detail.EndsWith(":" + person.Id, StringComparison.Ordinal))?.DisplayName ?? fallback;

    /// <summary>The household named inside a land-use event, by its stores name.</summary>
    private static string LandHousehold(OwnerWorldSnapshot? snapshot, string detail) =>
        snapshot?.Stockpiles.FirstOrDefault(stockpile => detail.Contains(":" + stockpile.OwnerId + ":", StringComparison.Ordinal))?.Name
        ?? "A household";

    /// <summary>The Town named inside a land-use event.</summary>
    private static string LandTown(OwnerWorldSnapshot? snapshot, string detail) =>
        snapshot?.Towns.FirstOrDefault(town => detail.Contains(":" + town.Id + ":", StringComparison.Ordinal))?.Name ?? "the Town";

    /// <summary>The Town whose complete ID leads an event detail.</summary>
    private static string? TownNamed(OwnerWorldSnapshot? snapshot, string detail, string? fallback) =>
        snapshot?.Towns.OrderByDescending(town => town.Id.Length)
            .FirstOrDefault(town => IsLeadingId(detail, town.Id))?.Name ?? fallback;

    /// <summary>A building joining a Town; the detail is <c>&lt;town&gt;:&lt;building&gt;:buildings:&lt;count&gt;</c>.</summary>
    private static string DescribeTownBuilding(OwnerWorldSnapshot? snapshot, string detail)
    {
        var town = snapshot?.Towns.OrderByDescending(item => item.Id.Length).FirstOrDefault(item => IsLeadingId(detail, item.Id));
        var rest = town is null || detail.Length <= town.Id.Length ? string.Empty : detail[(town.Id.Length + 1)..];
        var building = snapshot?.PlacedBuildings.FirstOrDefault(item => IsLeadingId(rest, item.InstanceId))?.DisplayName;
        var townName = town?.Name ?? "the Town";
        return building is null ? $"A building is now part of {townName}." : $"The {building} is now part of {townName}.";
    }

    /// <summary>The word for a young animal of the newborn's species; the newborn's ID leads the detail.</summary>
    private static string YoungAnimal(OwnerWorldSnapshot? snapshot, string detail)
    {
        var newborn = snapshot?.Animals.FirstOrDefault(animal => IsLeadingId(detail, animal.Id));
        return newborn?.Species switch
        {
            "cow" => "calf",
            "sheep" => "lamb",
            "horse" => "foal",
            "chicken" => "chick",
            _ => "young animal",
        };
    }

    /// <summary>A scouting trip: setting out, turning back, coming home or ending early.</summary>
    private static string DescribeScouting(OwnerWorldEvent worldEvent, OwnerWorldSnapshot? snapshot)
    {
        var scout = LeadingName(snapshot, worldEvent.Detail);
        return worldEvent.Kind switch
        {
            "exploration_started" => $"{scout} set out to scout.",
            "exploration_return_started" => $"{scout} is heading back from scouting.",
            "exploration_completed" => $"{scout} came back from scouting.",
            _ when worldEvent.Detail.EndsWith(":return_blocked", StringComparison.Ordinal) =>
                $"{scout}'s scouting trip ended before they got back: the way home was blocked.",
            _ => $"{scout}'s scouting trip was cut short.",
        };
    }

    private static string Field(string detail, int index)
    {
        var fields = detail.Split('|');
        return index < fields.Length ? fields[index] : string.Empty;
    }

    /// <summary>The installation's one warning at 80% of its model-call limit.</summary>
    private static string DescribeModelCallWarning(string[] parts)
    {
        var count = parts.Length == 4 && parts[0] == "used" && parts[2] == "limit" &&
            long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var used) &&
            long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
            ? string.Create(CultureInfo.InvariantCulture, $"{used:N0} of {limit:N0}")
            : "80% of the limit";
        return $"Model calls: {count} used across all worlds. Your worlds pause at the limit; raise it in Settings → Game.";
    }

    private static string DescribeSkill(string detail, OwnerWorldSnapshot? snapshot)
    {
        var fields = detail.Split('|', 3);
        if (fields.Length != 3 || fields[1] is not ("building" or "farming" or "crafting" or "smithing"))
            return "An agent learned a skill.";
        var source = fields[2] == "work" ? "by doing the work" : "from " + Name(snapshot, fields[2]);
        return $"{Name(snapshot, fields[0])} learned {fields[1]} {source}.";
    }

    private static string DescribeWrittenKnowledge(OwnerWorldEvent worldEvent, OwnerWorldSnapshot? snapshot)
    {
        var fields = worldEvent.Detail.Split('|');
        var author = Name(snapshot, fields[0]);
        var recipient = Name(snapshot, fields.ElementAtOrDefault(1) ?? string.Empty);
        return worldEvent.Kind switch
        {
            "agent_knowledge_artifact_read" => $"{recipient} read a written work and learned about new places.",
            "agent_knowledge_shared" => $"{author} shared written knowledge with {recipient}.",
            "agent_knowledge_artifact_collected" => $"{author} picked up a written work.",
            "agent_knowledge_artifact_stored" => $"{author} stored a written work.",
            "agent_knowledge_writing_started" => $"{author} started work on a {GameUiText.ItemName(fields.ElementAtOrDefault(1) ?? "record").ToLowerInvariant()}.",
            "agent_knowledge_writing_cancelled" => $"{author} stopped writing. The unused supplies are free again.",
            "agent_knowledge_material_collected" => $"{author} collected writing supplies.",
            _ => fields.ElementAtOrDefault(2) switch
            {
                "field_map" => $"{author} finished drawing a field map.",
                "field_record" => $"{author} finished writing a field record.",
                "book" => $"{author} finished writing a book.",
                _ => $"{author} finished a written work.",
            },
        };
    }

    private static string Name(OwnerWorldSnapshot? snapshot, string id) =>
        snapshot?.Inhabitants.FirstOrDefault(person => person.Id == id)?.DisplayName
        ?? (string.IsNullOrEmpty(id) || id.Contains(':', StringComparison.Ordinal)
            ? "Someone" : GameUiText.HumanizeIdentifier(id));

    private static string LeadingName(OwnerWorldSnapshot? snapshot, string detail)
    {
        // Descendant IDs can start with another agent's complete ID. Prefer
        // the longest complete identity rather than the first matching parent.
        var person = snapshot?.Inhabitants.OrderByDescending(item => item.Id.Length)
            .FirstOrDefault(item => IsLeadingId(detail, item.Id));
        return person?.DisplayName ?? Name(snapshot, detail.Split(':', 2)[0]);
    }

    private static string OrnamentGiftRecipient(OwnerWorldSnapshot? snapshot, string detail)
    {
        const string marker = ":ornament_gift:";
        var actor = snapshot?.Inhabitants.OrderByDescending(person => person.Id.Length)
            .FirstOrDefault(person => detail.StartsWith(person.Id + marker, StringComparison.Ordinal));
        if (actor is null) return "someone";
        var recipientDetail = detail[(actor.Id.Length + marker.Length)..];
        return snapshot!.Inhabitants.OrderByDescending(person => person.Id.Length)
            .FirstOrDefault(person => IsLeadingId(recipientDetail, person.Id))?.DisplayName ?? "someone";
    }

    /// <summary>The Town a resident joined or left, named as the player sees it; agents now move between Towns.</summary>
    private static string ResidentTownName(OwnerWorldSnapshot? snapshot, OwnerWorldEvent worldEvent) =>
        ResidentTown(snapshot, worldEvent)?.Name ?? "a Town";

    private static OwnerWorldTown? ResidentTown(OwnerWorldSnapshot? snapshot, OwnerWorldEvent worldEvent) =>
        snapshot?.Towns.OrderByDescending(item => item.Id.Length)
            .FirstOrDefault(item => IsLeadingId(worldEvent.Detail, item.Id));

    private static string ResidentName(OwnerWorldSnapshot? snapshot, OwnerWorldEvent worldEvent)
    {
        var town = ResidentTown(snapshot, worldEvent);
        // Older delimiter-free Town IDs use a single field. Current IDs must
        // be matched against the snapshot before reading the resident field.
        var townId = town?.Id ?? worldEvent.Detail.Split(':', 2)[0];
        if (worldEvent.Detail.Length <= townId.Length) return "Someone";
        var residentDetail = worldEvent.Detail[(townId.Length + 1)..];
        var residentsMarker = residentDetail.LastIndexOf(":residents:", StringComparison.Ordinal);
        if (residentsMarker < 0) return LeadingName(snapshot, residentDetail);
        var residentId = residentDetail[..residentsMarker];
        if (worldEvent.Kind == "town_resident_joined") residentId = BeforeLastField(residentId);
        return Name(snapshot, residentId);
    }

    /// <summary>The household named after the leading agent ID, as the player sees it.</summary>
    private static string HouseholdAfterAgent(OwnerWorldSnapshot? snapshot, string detail)
    {
        var person = snapshot?.Inhabitants.OrderByDescending(item => item.Id.Length)
            .FirstOrDefault(item => IsLeadingId(detail, item.Id));
        string rest;
        if (person is not null)
        {
            rest = detail.Length > person.Id.Length ? detail[(person.Id.Length + 1)..] : string.Empty;
        }
        else
        {
            var marker = detail.IndexOf(":household:", StringComparison.Ordinal);
            rest = marker < 0 ? string.Empty : detail[(marker + 1)..];
        }
        return rest.Length == 0 ? "a household" : GameUiText.PartyName(snapshot, rest);
    }

    private static string HousingReason(string detail) => detail[(detail.LastIndexOf(':') + 1)..] switch
    {
        "no_household" => "they belong to no household, so no House can be planned for them",
        "no_authorized_home" => "their household holds no House yet",
        "missing_materials" => "their household lacks the materials for a House",
        "no_legal_site" => "their household has no legal site for a House",
        _ => "no House is available to them yet",
    };

    private static string BeforeLastField(string detail)
    {
        var delimiter = detail.LastIndexOf(':');
        return delimiter < 0 ? detail : detail[..delimiter];
    }

    private static bool IsLeadingId(string detail, string id) =>
        detail == id || detail.StartsWith(id + ":", StringComparison.Ordinal);
}
