namespace ClankerWorld.GodotClient.UI;

/// <summary>Player event descriptions, derived without rewriting accepted history.</summary>
public static class WorldEventText
{
    public static string Describe(OwnerWorldEvent worldEvent, OwnerWorldSnapshot? snapshot)
    {
        var parts = worldEvent.Detail.Split(':', StringSplitOptions.RemoveEmptyEntries);
        string ThingAt(int index) => index < parts.Length
            ? GameUiText.HumanizeIdentifier(parts[index])
            : "something new";
        var buildingName = snapshot?.PlacedBuildings.FirstOrDefault(building =>
            IsLeadingId(worldEvent.Detail, building.InstanceId))?.DisplayName ?? "Building";
        var guestName = snapshot?.Inhabitants.OrderByDescending(person => person.Id.Length).FirstOrDefault(person =>
            worldEvent.Detail.EndsWith(":" + person.Id, StringComparison.Ordinal))?.DisplayName ?? "The guest";
        var fields = worldEvent.Detail.Split('|');
        string Field(int index) => index < fields.Length ? fields[index] : string.Empty;
        string Party(int index) => snapshot?.Towns.FirstOrDefault(town => town.Id == Field(index))?.Name ??
            snapshot?.Stockpiles.FirstOrDefault(stockpile => stockpile.OwnerId == Field(index))?.Name ??
            (Field(index).StartsWith("town:", StringComparison.Ordinal) ? "The Town" :
                Field(index).StartsWith("household:", StringComparison.Ordinal) ? "A household" : Name(snapshot, Field(index)));
        string Business(int index) => snapshot?.PlacedBuildings.FirstOrDefault(building => building.InstanceId == Field(index))?.DisplayName ?? "the business";

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
            "crop_moisture_effect" when parts.Length >= 3 => parts[1] == "wet"
                ? "Moist soil improved a crop harvest."
                : "Dry soil reduced a crop harvest.",
            "food_harvested" => $"{Name(snapshot, BeforeLastField(worldEvent.Detail))} gathered food.",
            "food_consumed" => $"{Name(snapshot, worldEvent.Detail)} ate.",
            "tree_planted" => $"{LeadingName(snapshot, worldEvent.Detail)} planted a tree.",
            "tree_replanted" => $"{LeadingName(snapshot, worldEvent.Detail)} replanted a tree.",
            "water_jug_collected" => $"{LeadingName(snapshot, worldEvent.Detail)} collected an empty water jug.",
            "water_collected" => $"{LeadingName(snapshot, worldEvent.Detail)} filled a jug with fresh water.",
            "water_delivered" => $"{LeadingName(snapshot, worldEvent.Detail)} brought a water jug home.",
            "pottery_supplied" => $"{LeadingName(snapshot, worldEvent.Detail)} brought pottery materials home.",
            "food_potted" => $"{LeadingName(snapshot, worldEvent.Detail)} put food in a storage pot.",
            "inhabitant_slept" => $"{Name(snapshot, worldEvent.Detail)} slept.",
            "child_born" => $"{Name(snapshot, worldEvent.Detail)} was born.",
            "inhabitant_removed" => $"{Name(snapshot, worldEvent.Detail)} died.",
            "estate_will_accepted" => "A final will decided who gets their belongings.",
            "estate_will_default" => "Their belongings went to their household.",
            "partnership_accepted" => "Two agents formed a partnership.",
            "partnership_ended" => "A partnership ended.",
            "caregiver_assigned" => "A child has a new caregiver.",
            "council_policy_adopted" => "The Town adopted a new policy.",
            "settlement_trade_completed" => "A trade was completed.",
            "business_goods_listed" => $"{Party(0)} offered {Field(3)} {GameUiText.HumanizeIdentifier(Field(2))} for {Field(5)} {GameUiText.HumanizeIdentifier(Field(4))} at {Business(1)}.",
            "business_offer_accepted" => $"{Party(0)} agreed to a business exchange and will bring the payment.",
            "business_exchange_completed" => $"{Party(0)} and {Party(1)} exchanged their goods at {Business(3)}.",
            "business_offer_cancelled" => $"{Party(0)}'s business exchange ended; its goods were released without a trade.",
            "business_stock_withdrawn" => $"{Party(0)} collected {Field(3)} {GameUiText.HumanizeIdentifier(Field(2))} from {Business(1)}.",
            "business_stock_cleared" => $"{Party(0)} moved unusable stock from {Business(1)} onto nearby ground.",
            "business_tool_requested" => $"{Party(0)} requested {GameUiText.HumanizeIdentifier(Field(2))} from {Business(1)}.",
            "market_plot_reserved" => "The shared Market is ready with a clear area for household stalls.",
            "market_stall_stocked" => $"{Party(0)} brought {Field(4)} {GameUiText.HumanizeIdentifier(Field(3))} to a Market stall.",
            "market_stall_released" => $"{Party(0)} withdrew its last stall goods; the Market stall is free.",
            "town_election_started" => $"{Party(0)} began an election for three representatives.",
            "town_council_elected" => $"{Party(0)} elected three representatives for one game year.",
            "town_law_proposed" => $"{Party(2)} proposed a rule at {Party(0)}'s Hall.",
            "town_law_adopted" => Field(2) == "True" ? $"{Party(0)} repealed a rule by council majority." : $"{Party(0)} adopted a rule by council majority.",
            "town_law_rejected" => $"{Party(0)} kept its current rule after the council vote.",
            "equipment_worn" => $"{Party(0)} equipped {GameUiText.HumanizeIdentifier(Field(1))}.",
            "equipment_repaired" => $"{Party(0)} repaired {GameUiText.HumanizeIdentifier(Field(1))}.",
            "carry_aid_removed" => $"{Name(snapshot, worldEvent.Detail)} removed their carry aid.",
            "carrying_full" => $"{Name(snapshot, worldEvent.Detail)} needs to unload before carrying more goods.",
            "cart_deployed" => $"{Party(0)} put a cart into use.",
            "cart_loaded" => $"{Party(0)} loaded {Field(3)} {GameUiText.HumanizeIdentifier(Field(2))} into a cart.",
            "cart_unloaded" => $"{Party(0)} unloaded {Field(3)} {GameUiText.HumanizeIdentifier(Field(2))} from a cart.",
            "cart_repaired" => $"{Party(0)} repaired a cart.",
            "cart_given" => $"{Party(0)} gave a cart to {Party(1)}.",
            "cart_cargo_put_down" => $"{Party(0)} put cart cargo onto nearby ground without discarding it.",
            "medical_treatment_started" => $"{Party(1)} began {GameUiText.HumanizeIdentifier(Field(2))} care for {Party(0)}.",
            "medical_treatment_completed" => $"{Name(snapshot, worldEvent.Detail)} finished receiving care.",
            "medical_treatment_interrupted" => $"{Name(snapshot, worldEvent.Detail)}'s treatment stopped; unused supplies were released.",
            "medical_care_allowed" => $"{Party(0)} agreed to receive care from {Party(1)}.",
            "medical_care_revoked" => $"{Party(0)} ended permission for care from {Party(1)}.",
            "communal_boat_launched" => $"{LeadingName(snapshot, worldEvent.Detail)} finished a shared boat at the Port.",
            "boat_departed" => $"{LeadingName(snapshot, worldEvent.Detail)} departed by boat for another Port.",
            "boat_arrived" => $"{LeadingName(snapshot, worldEvent.Detail)} reached the destination Port by boat.",
            "boat_returned" => $"{LeadingName(snapshot, worldEvent.Detail)} returned to the original Port by boat.",
            "boat_waiting" => $"{LeadingName(snapshot, worldEvent.Detail)} is waiting aboard a boat for a safe route or landing.",
            "boat_return_started" => $"{LeadingName(snapshot, worldEvent.Detail)}'s boat is returning to its original Port.",
            "boat_departure_blocked" => $"{LeadingName(snapshot, worldEvent.Detail)} could not begin the boat trip safely.",
            "livestock_acquired" => $"{Party(1)} acquired {GameUiText.HumanizeIdentifier(Field(2)).ToLowerInvariant()} livestock.",
            "livestock_natural_death" => "A farm animal died of natural causes.",
            "livestock_product_ready" => $"A farm animal has {GameUiText.HumanizeIdentifier(parts[^1]).ToLowerInvariant()} ready to collect.",
            "livestock_product_spoiled" => "Uncollected farm produce spoiled.",
            "livestock_product_collected" => $"{LeadingName(snapshot, worldEvent.Detail)} collected produce from a farm animal.",
            "field_prepared" => $"{LeadingName(snapshot, worldEvent.Detail)} prepared a farm field with a hoe.",
            "field_harvested" => $"{LeadingName(snapshot, worldEvent.Detail)} harvested a planted field.",
            "field_harvest_collected" => $"{LeadingName(snapshot, worldEvent.Detail)} carried a field harvest into storage.",
            "crop_ready" => "A planted field is ready to harvest.",
            "crop_weather_loss" => "Bad weather destroyed a planted crop.",
            "skill_learned" => DescribeSkill(worldEvent.Detail, snapshot),
            "inhabitant_building_proposed" => $"{LeadingName(snapshot, worldEvent.Detail)} suggested a new building design.",
            "instruction_not_understood" => $"{Name(snapshot, BeforeLastField(worldEvent.Detail))} didn't understand your order. " +
                "For now, orders can only ask them to gather food, eat or find food.",
            "settlement_founded" => "A new Town was founded.",
            "town_founding_started" => "Your first Town is being set up.",
            "town_resident_joined" => $"{ResidentName(snapshot, worldEvent)} joined the first Town.",
            "town_resident_left" => $"{ResidentName(snapshot, worldEvent)} left the first Town.",
            "town_membership_evaluated" => "The new adult is not part of a Town yet.",
            "town_building_assigned" => "A building joined the first Town.",
            "town_border_expanded" => "The first Town border expanded.",
            "town_founded" => "Your first Town is founded.",
            "bridge_built" when parts.Length > 0 && parts[0] == "road" => "A new Road crosses a river on a new bridge.",
            "bridge_built" => "Agents crossed a river here so often that a bridge was built.",
            "housing_request_made" => $"{LeadingName(snapshot, worldEvent.Detail)} asked {HouseholdAfterAgent(snapshot, worldEvent.Detail)} for a place to live in their House.",
            "household_joined" => $"{LeadingName(snapshot, worldEvent.Detail)} now lives with {HouseholdAfterAgent(snapshot, worldEvent.Detail)}.",
            "housing_request_refused" => $"{HouseholdAfterAgent(snapshot, worldEvent.Detail)} did not agree to let {LeadingName(snapshot, worldEvent.Detail)} move in.",
            "housing_request_expired" => $"{HouseholdAfterAgent(snapshot, worldEvent.Detail)} did not answer {LeadingName(snapshot, worldEvent.Detail)}'s request to move in.",
            "housing_blocked" => $"{LeadingName(snapshot, worldEvent.Detail)} has no home: {HousingReason(worldEvent.Detail)}.",
            "paused" => "The world was paused.",
            "resumed" => "The world resumed.",
            _ => $"{GameUiText.HumanizeIdentifier(worldEvent.Kind)}.",
        };
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
        detail == id || detail.StartsWith(id + ":", StringComparison.Ordinal) || detail.StartsWith(id + "|", StringComparison.Ordinal);
}
