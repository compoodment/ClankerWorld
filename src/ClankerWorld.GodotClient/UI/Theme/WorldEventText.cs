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

        return worldEvent.Kind switch
        {
            "world_created" => "A new world has begun.",
            "world_started" => "Time has started in this world.",
            "weather_changed" when parts.Length >= 2 => $"The weather changed to {ThingAt(1)}.",
            "building_placed" => $"{ThingAt(1)} was built.",
            "build_started" => $"Work began on {ThingAt(1)}.",
            "build_completed" => $"{ThingAt(1)} is ready.",
            "recipe_started" => $"Work began on {ThingAt(1)}.",
            "recipe_completed" => $"{ThingAt(1)} was finished.",
            "crop_moisture_effect" when parts.Length >= 3 => parts[1] == "wet"
                ? "Moist soil improved a crop harvest."
                : "Dry soil reduced a crop harvest.",
            "food_harvested" => $"{Name(snapshot, BeforeLastField(worldEvent.Detail))} gathered food.",
            "food_consumed" => $"{Name(snapshot, worldEvent.Detail)} ate.",
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
            "paused" => "The world was paused.",
            "resumed" => "The world resumed.",
            _ => $"{GameUiText.HumanizeIdentifier(worldEvent.Kind)}.",
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

    private static string BeforeLastField(string detail)
    {
        var delimiter = detail.LastIndexOf(':');
        return delimiter < 0 ? detail : detail[..delimiter];
    }

    private static bool IsLeadingId(string detail, string id) =>
        detail == id || detail.StartsWith(id + ":", StringComparison.Ordinal);
}
