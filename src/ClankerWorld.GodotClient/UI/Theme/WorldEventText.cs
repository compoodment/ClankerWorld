using System.Globalization;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Player event descriptions, derived without rewriting accepted history.</summary>
public static class WorldEventText
{
    public const string ContinuityRisk = "The world is at risk of dying out.";

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

        return worldEvent.Kind switch
        {
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
            "council_policy_adopted" => "The Town adopted a new policy.",
            "settlement_trade_completed" => "A trade was completed.",
            "business_trade_offered" => "A customer offered an exchange at a shop; the goods are set aside while both traders meet there.",
            "business_trade_completed" => "A shop exchange finished; the buyer carries the purchase and payment is stored at the shop.",
            "business_trade_cancelled" => "A shop exchange stopped; its goods and receiving space are available again.",
            "store_stock_collected" => "An agent collected a load for their household Store; it is still being carried there.",
            "store_stock_delivered" => "A load reached the household Store and is now available to sell.",
            "carrying_full" => $"{Name(snapshot, worldEvent.Detail)} cannot carry more; a load needs to be stored or set down.",
            "spare_cargo_stored" => $"{LeadingName(snapshot, worldEvent.Detail)} set down spare supplies for their household to make room in their load.",
            "household_delivery_recovered" => $"{LeadingName(snapshot, worldEvent.Detail)} returned unusable delivery supplies to their household's pile at camp.",
            "equipment_equipped" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} equipped an item.",
            "equipment_repair_started" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} began repairing an item.",
            "equipment_repaired" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} repaired an item.",
            "equipment_repair_interrupted" => "Repair stopped; its unused materials are available again.",
            "skill_learned" => DescribeSkill(worldEvent.Detail, snapshot),
            "inhabitant_building_proposed" => $"{LeadingName(snapshot, worldEvent.Detail)} suggested a new building design.",
            "instruction_not_understood" => $"{Name(snapshot, BeforeLastField(worldEvent.Detail))} didn't understand your order. " +
                "For now, orders can only ask them to gather food, eat or find food.",
            "settlement_founded" => "A new Town was founded.",
            "town_civic_council" => $"{civicTownName}'s council changed.",
            "town_civic_election" => $"{civicTownName}'s council election opened.",
            "town_civic_runoff" => $"{civicTownName}'s council election needs a runoff for tied seats.",
            "town_civic_proposal" => $"A proposal was submitted to {civicTownName}'s council.",
            "town_civic_result" => $"{civicTownName}'s council recorded a decision. See the Towns page for its result.",
            "town_civic_cancelled" => $"An unfinished election in {civicTownName} was cancelled.",
            "town_founding_started" => "Your first Town is being set up.",
            "town_resident_joined" => $"{ResidentName(snapshot, worldEvent)} joined the first Town.",
            "town_resident_left" => $"{ResidentName(snapshot, worldEvent)} left the first Town.",
            "town_membership_evaluated" => "The new adult is not part of a Town yet.",
            "town_building_assigned" => "A building joined the first Town.",
            "town_border_expanded" => "The first Town border expanded.",
            "town_founded" => "Your first Town is founded.",
            "bridge_built" when parts.Length > 0 && parts[0] == "road" => "A new Road crosses a river on a new bridge.",
            "bridge_built" => "Agents crossed a river here so often that a bridge was built.",
            "household_work_resumed" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} took over paused household work at its building.",
            "household_left" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} left their household and may collect their personal belongings.",
            "household_founded" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} started a household; a House still needs materials and work.",
            "personal_goods_collected" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} collected their personal belongings.",
            "personal_goods_stored" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} stored personal belongings while keeping ownership.",
            "borrowed_goods_returned" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} returned borrowed household goods.",
            "replacement_care_accepted" => $"{Name(snapshot, worldEvent.Detail.Split('|')[0])} explicitly accepted primary care of a dependent.",
            "housing_request_made" => $"{LeadingName(snapshot, worldEvent.Detail)} asked {HouseholdAfterAgent(snapshot, worldEvent.Detail)} for a place to live in their House.",
            "household_joined" => $"{LeadingName(snapshot, worldEvent.Detail)} now lives with {HouseholdAfterAgent(snapshot, worldEvent.Detail)}.",
            "housing_request_refused" => $"{HouseholdAfterAgent(snapshot, worldEvent.Detail)} did not agree to let {LeadingName(snapshot, worldEvent.Detail)} move in.",
            "housing_request_expired" => $"{HouseholdAfterAgent(snapshot, worldEvent.Detail)} did not answer {LeadingName(snapshot, worldEvent.Detail)}'s request to move in.",
            "housing_blocked" => $"{LeadingName(snapshot, worldEvent.Detail)} has no home: {HousingReason(worldEvent.Detail)}.",
            "paused" => "The world was paused.",
            "resumed" => "The world resumed.",
            "model_call_warning" => DescribeModelCallWarning(parts),
            _ => $"{GameUiText.HumanizeIdentifier(worldEvent.Kind)}.",
        };
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

    private static string ResidentName(OwnerWorldSnapshot? snapshot, OwnerWorldEvent worldEvent)
    {
        var town = snapshot?.Towns.OrderByDescending(item => item.Id.Length)
            .FirstOrDefault(item => IsLeadingId(worldEvent.Detail, item.Id));
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
