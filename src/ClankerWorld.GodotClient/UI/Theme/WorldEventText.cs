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
        string ThingAt(int index) => index >= 0 && index < parts.Length
            ? GameUiText.HumanizeIdentifier(parts[index])
            : "something new";
        var buildingName = snapshot?.PlacedBuildings.FirstOrDefault(building =>
            IsLeadingId(worldEvent.Detail, building.InstanceId))?.DisplayName ?? "Building";
        var guestName = snapshot?.Inhabitants.OrderByDescending(person => person.Id.Length).FirstOrDefault(person =>
            worldEvent.Detail.EndsWith(":" + person.Id, StringComparison.Ordinal))?.DisplayName ?? "The guest";
        var civicTownId = worldEvent.Detail.Split('|', 3)[0];
        var civicTownName = snapshot?.Towns.FirstOrDefault(town => town.Id == civicTownId)?.Name ?? "A Town";
        var townProjectName = worldEvent.Kind.StartsWith("town_project_", StringComparison.Ordinal)
            ? TownProjectForEvent(snapshot, worldEvent.Detail)?.Name ?? "a Town project" : "a Town project";

        return worldEvent.Kind switch
        {
            "developer_edit" => DescribeDeveloperEdit(worldEvent.Detail, snapshot),
            "world_created" => "A new world has begun.",
            "world_started" => "Time has started in this world.",
            "weather_changed" when parts.Length >= 2 => $"The weather changed to {ThingAt(1)}.",
            "building_placed" => $"{ThingAt(1)} was built.",
            "building_expansion_started" => $"{buildingName} expansion has begun.",
            "building_expanded" => $"{buildingName} storage was expanded.",
            "building_expansion_cancelled" => $"{buildingName} expansion stopped; reserved materials were released.",
            "house_guest_invited" => $"{guestName} may shelter in the {buildingName} during storms.",
            "house_guest_revoked" => $"{guestName}'s storm shelter invitation ended.",
            "build_started" => $"Work began on {ThingAt(1)}.",
            "build_completed" => $"{ThingAt(1)} is ready.",
            "recipe_started" => $"Work began on {ThingAt(1)}.",
            "recipe_completed" => $"{ThingAt(1)} was finished.",
            "field_work_started" => $"{LeadingName(snapshot, worldEvent.Detail)} started work on a field.",
            "field_prepared" => $"{LeadingName(snapshot, worldEvent.Detail)} prepared a field.",
            "field_planted" => $"{LeadingName(snapshot, worldEvent.Detail)} planted {ThingAt(parts.Length - 1).ToLowerInvariant()}.",
            "field_tended" => $"{LeadingName(snapshot, worldEvent.Detail)} tended {ThingAt(parts.Length - 1).ToLowerInvariant()}.",
            "field_harvested" => $"{LeadingName(snapshot, worldEvent.Detail)} harvested {ThingAt(parts.Length - 1).ToLowerInvariant()}.",
            "field_ready" => "A field is ready to harvest.",
            "field_work_interrupted" => "Work on a field stopped.",
            "crop_weather_loss" => $"{ThingAt(parts.Length - 1)} reduced a crop harvest.",
            "crop_moisture_effect" when parts.Length >= 3 => parts[^2] == "wet"
                ? "Moist soil improved a crop harvest."
                : "Dry soil reduced a crop harvest.",
            "food_harvested" => $"{Name(snapshot, BeforeLastField(worldEvent.Detail))} gathered food.",
            "food_consumed" => $"{Name(snapshot, worldEvent.Detail)} ate.",
            "tree_planted" => $"{LeadingName(snapshot, worldEvent.Detail)} planted a tree.",
            "tree_replanted" => $"{LeadingName(snapshot, worldEvent.Detail)} replanted a tree.",
            "inhabitant_slept" => $"{Name(snapshot, worldEvent.Detail)} slept.",
            "child_born" => $"{Name(snapshot, worldEvent.Detail)} was born.",
            "inhabitant_removed" => $"{Name(snapshot, worldEvent.Detail)} died.",
            "estate_will_accepted" => "A final will decided who gets their belongings.",
            "estate_will_default" => "Their belongings went to their household.",
            "partnership_accepted" => "Two agents formed a partnership.",
            "partnership_ended" => "A partnership ended.",
            "continuity_rule_on" => "The continuity rule is on because fewer than eight people who are not elders are alive. " +
                "Couples may put off having a child for up to two days but cannot refuse.",
            "continuity_rule_off" => "The continuity rule is off because eight or more people who are not elders are alive. " +
                "Couples may decide against having a child again.",
            "caregiver_assigned" => "A child has a new caregiver.",
            "guardian_needed" => "Needs a guardian. No adult has accepted care yet.",
            "guardian_assigned" => "An adult accepted care for a child.",
            "medical_care_allowed" => $"{LeadingName(snapshot, worldEvent.Detail)} allowed someone to provide medical care.",
            "medical_care_revoked" => $"{LeadingName(snapshot, worldEvent.Detail)} withdrew permission for medical care.",
            "medical_treatment_started" => $"{LeadingName(snapshot, worldEvent.Detail)} began a course of medicine.",
            "medical_treatment_completed" => $"{Name(snapshot, worldEvent.Detail)} finished a course of medicine.",
            "medical_treatment_interrupted" => $"{Name(snapshot, worldEvent.Detail)} stopped treatment; the used dose was not returned.",
            "empty_vessel_picked_up" => $"{LeadingName(snapshot, worldEvent.Detail)} collected an empty household vessel to bring home.",
            "ornament_worn" => $"{LeadingName(snapshot, worldEvent.Detail)} put on an ornament.",
            "ornament_removed" => $"{LeadingName(snapshot, worldEvent.Detail)} took off an ornament.",
            "ornament_given" => $"{LeadingName(snapshot, worldEvent.Detail)} gave an ornament to {OrnamentGiftRecipient(snapshot, worldEvent.Detail)}.",
            "council_policy_adopted" => "The Town adopted a new policy.",
            "settlement_trade_completed" => "A trade was completed.",
            "tool_request_placed" => $"{LeadingName(snapshot, worldEvent.Detail)} asked a Blacksmith household to make a tool; no payment was taken.",
            "tool_request_accepted" => $"{LeadingName(snapshot, worldEvent.Detail)} agreed to make the requested tool using household supplies.",
            "tool_request_refused" => $"{LeadingName(snapshot, worldEvent.Detail)} turned down a tool request; nothing was taken.",
            "tool_request_withdrawn" => $"{LeadingName(snapshot, worldEvent.Detail)} withdrew a tool request; household work and goods keep their owners.",
            "tool_request_ready" => "A requested tool is finished; its price still needs an agreed exchange.",
            "tool_request_completed" => "A tool request was fulfilled through the completed shop exchange.",
            "tool_request_interrupted" => "A tool request stopped; materials and goods keep their owners.",
            "business_trade_offered" => "A customer offered an exchange at a shop; the goods are set aside while both traders meet there.",
            "business_trade_completed" => "A shop exchange finished; the buyer carries the purchase and payment is stored at the shop.",
            "business_trade_cancelled" => "A shop exchange stopped; its goods and receiving space are available again.",
            "store_stock_collected" => "An agent collected a load for their household Store; it is still being carried there.",
            "store_stock_delivered" => "A load reached the household Store and is now available to sell.",
            "handcart_attached" => $"{LeadingName(snapshot, worldEvent.Detail)} attached their handcart.",
            "handcart_parked" => $"{LeadingName(snapshot, worldEvent.Detail)} parked their handcart; its cargo stays inside.",
            "handcart_loaded" => $"{LeadingName(snapshot, worldEvent.Detail)} loaded nearby goods into their handcart.",
            "handcart_unloaded" => $"{LeadingName(snapshot, worldEvent.Detail)} unloaded goods from their handcart.",
            "handcart_repaired" => $"{LeadingName(snapshot, worldEvent.Detail)} repaired their handcart using carried materials.",
            "handcart_transferred" => $"{LeadingName(snapshot, worldEvent.Detail)} gave their handcart and its cargo to a nearby agent.",
            "handcart_blocked" => "The handcart cannot travel here; park it or choose another route. Its cargo is safe.",
            "carrying_full" => $"{Name(snapshot, worldEvent.Detail)} cannot carry more; a load needs to be stored or set down.",
            "spare_cargo_stored" => $"{LeadingName(snapshot, worldEvent.Detail)} set down spare supplies for their household to make room in their load.",
            "household_delivery_recovered" => $"{LeadingName(snapshot, worldEvent.Detail)} returned unusable delivery supplies to their household's pile at camp.",
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
                "For now, orders can only ask them to gather food, eat, find food, go to a tile or become a child's guardian.",
            "settlement_founded" => "A new Town was founded.",
            "town_civic_law" => $"{civicTownName} recorded a law decision. See the Towns page for its wording and scope.",
            "town_civic_government" => $"{civicTownName} recorded a resident government decision. See the Towns page for the vote or handover.",
            "town_civic_mayor" => $"{civicTownName} recorded a mayoral election or office change. See the Towns page for its result.",
            "town_civic_council" => $"{civicTownName}'s council changed.",
            "town_civic_election" => $"{civicTownName}'s council election opened.",
            "town_civic_runoff" => $"{civicTownName}'s council election needs a runoff for tied seats.",
            "town_civic_proposal" => $"A proposal was submitted to {civicTownName}'s council.",
            "town_civic_result" => $"{civicTownName}'s council recorded a decision. See the Towns page for its result.",
            "town_civic_land_use" => $"A household land request in {civicTownName} has new information. See its plot for approval progress.",
            "land_use_granted" => "A household received an approved land-use right. The household use filter shows its plot.",
            "land_use_requested" => "A household requested a land-use right. Filing grants no permission.",
            "town_land_claimed" => $"{civicTownName}'s council approved a claim to adjoining land. The Town title filter shows the new plot.",
            "town_civic_cancelled" => $"An unfinished election in {civicTownName} was cancelled.",
            "town_project_approved" => $"The Council approved {townProjectName}; real materials and construction work are still needed.",
            "town_project_blocked" => $"Work on {townProjectName} is blocked. See the Towns page for what is needed.",
            "town_project_resumed" => $"{townProjectName} can continue.",
            "town_project_donated" => $"{LeadingName(snapshot, worldEvent.Detail)} donated personal materials to {townProjectName}.",
            "town_project_material_picked_up" => $"{LeadingName(snapshot, worldEvent.Detail)} picked up Town materials for {townProjectName}; the load is still being carried.",
            "town_project_material_delivered" => $"{LeadingName(snapshot, worldEvent.Detail)} delivered materials to the approved site for {townProjectName}.",
            "town_project_material_returned" => worldEvent.Detail.EndsWith(":ground", StringComparison.Ordinal)
                ? $"{LeadingName(snapshot, worldEvent.Detail)} set down unused Town materials from {townProjectName}."
                : $"{LeadingName(snapshot, worldEvent.Detail)} returned unused materials from {townProjectName} to the Town Warehouse.",
            "town_project_worked" => $"{LeadingName(snapshot, worldEvent.Detail)} worked on {townProjectName}.",
            "town_project_completed" => $"{townProjectName} was built with its approved materials.",
            "town_founding_started" => "Your first Town is being set up.",
            "town_resident_joined" => $"{ResidentName(snapshot, worldEvent)} joined {ResidentTownName(snapshot, worldEvent)}.",
            "town_resident_left" => $"{ResidentName(snapshot, worldEvent)} left {ResidentTownName(snapshot, worldEvent)}.",
            "town_membership_evaluated" => "The new adult is not part of a Town yet.",
            "town_admission_accepted" => DescribeAdmission(worldEvent.Detail, snapshot),
            "town_admission_approved" => $"{civicTownName}'s council approved {Name(snapshot, Field(worldEvent.Detail, 1))}'s admission. It takes effect only if they accept.",
            "town_admission_lapsed" => $"{Name(snapshot, Field(worldEvent.Detail, 1))} did not join {civicTownName}: the approval no longer fits their circumstances.",
            "town_building_assigned" => "A building joined the first Town.",
            "town_border_expanded" => "The first Town border expanded.",
            "town_founded" => "Your first Town is founded.",
            "bridge_built" when parts.Length > 0 && parts[0] == "road" => "A new Road crosses a river on a new bridge.",
            "bridge_built" => "Agents crossed a river here so often that a bridge was built.",
            "household_work_resumed" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} took over paused household work at its building.",
            "household_left" => DescribeHouseholdDeparture(snapshot, worldEvent.Detail),
            "household_founded" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} started a household; a House still needs materials and work.",
            "personal_goods_collected" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} collected their personal belongings.",
            "personal_goods_stored" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} stored personal belongings while keeping ownership.",
            "borrowed_goods_returned" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} returned borrowed household goods.",
            "replacement_care_accepted" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} explicitly accepted primary care of a dependent.",
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
            "paused" => "The world was paused.",
            "resumed" => "The world resumed.",
            "model_call_warning" => DescribeModelCallWarning(parts),
            _ => $"{GameUiText.HumanizeIdentifier(worldEvent.Kind)}.",
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
            ? $"{actor} moved out because their House was overcrowded and may collect their personal belongings."
            : $"{actor} left their household and may collect their personal belongings.";
    }

    private static string DescribeRelocationNotice(OwnerWorldSnapshot? snapshot, string detail)
    {
        var fields = detail.Split('|');
        var actor = Name(snapshot, fields[0]);
        return (fields.Length > 2 ? fields[2] : string.Empty) switch
        {
            "volunteer" => $"{actor} volunteered to move out of their overcrowded House.",
            "latest_unrelated_arrival" => $"{actor} has notice to move out of their overcrowded House as its most recent arrival outside the main family.",
            "latest_arrival" => $"{actor} has notice to move out of their overcrowded House as its most recent arrival; no family has a majority.",
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
            "agent_knowledge_artifact_read" => $"{recipient} learned about places from a written work.",
            "agent_knowledge_shared" => $"{author} shared written knowledge with {recipient}.",
            "agent_knowledge_artifact_collected" => $"{author} picked up a written work.",
            "agent_knowledge_artifact_stored" => $"{author} stored a written work.",
            "agent_knowledge_writing_started" => $"{author} started work on a {GameUiText.ItemName(fields.ElementAtOrDefault(1) ?? "record").ToLowerInvariant()}.",
            "agent_knowledge_writing_cancelled" => $"{author} stopped writing; the unused materials are available again.",
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
