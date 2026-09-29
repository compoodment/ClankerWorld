namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Converts simulation identifiers into the deliberately small, player-facing
/// vocabulary used by the game HUD. Protocol diagnostics remain available in
/// developer tools instead of leaking into ordinary play.
/// </summary>
public static class GameUiText
{
    private const int MinutesPerDay = 1_440;

    public static string ActorMapLabel(string displayName)
    {
        var words = displayName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        var given = words[0];
        return new System.Globalization.StringInfo(given).LengthInTextElements <= 12
            ? given : System.Globalization.StringInfo.GetNextTextElement(given) + ".";
    }

    public static string ActivityMapGlyph(string? candidateId) => candidateId switch
    {
        "seek_food" => "→",
        "harvest_food" or "gather_smith_ore" => "✦",
        "consume_food" or "collect_shared_food" => "♥",
        "seek_warmth" or "wear_clothing" or "tend_fire" => "♨",
        "explore" => "⌖",
        "child_help_food" => "+",
        not null when candidateId.StartsWith("build:", StringComparison.Ordinal) => "◆",
        not null when candidateId.StartsWith("trade_", StringComparison.Ordinal) => "⇄",
        not null when candidateId.StartsWith("child_converse:", StringComparison.Ordinal) ||
            candidateId.StartsWith("knowledge_share:", StringComparison.Ordinal) => "…",
        not null when candidateId.StartsWith("child_play:", StringComparison.Ordinal) => "☆",
        not null when candidateId.StartsWith("child_learn:", StringComparison.Ordinal) => "?",
        not null when candidateId.StartsWith("care:", StringComparison.Ordinal) => "+",
        "safe_idle" => "·",
        _ => "○",
    };

    public static string ResourceMapCaption(OwnerWorldResource resource, float tileSize)
    {
        // At overview scale the existing terrain sprite/glyph carries the site;
        // full facts stay in hover and selected-tile inspection.
        if (tileSize < 32) return string.Empty;
        var title = NaturalObjectName(resource.NaturalObjectKind) ?? resource.Kind switch
        {
            "construction" => "Wood",
            "fertile_land" => "Plot",
            _ => HumanizeIdentifier(resource.Kind),
        };
        var quantity = ResourceQuantity(resource.Kind, resource.Quantity, resource.Capacity);
        return quantity.Length == 0 ? title : title + " " + quantity;
    }

    public static string FriendlyFailure(Exception exception) => exception switch
    {
        System.Net.Http.HttpRequestException { StatusCode: { } code } => code switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                "this device is not allowed in. Try connecting it again",
            System.Net.HttpStatusCode.NotFound => "the server does not know about that",
            System.Net.HttpStatusCode.Conflict => "the server's state changed. Try again",
            System.Net.HttpStatusCode.TooManyRequests => "the server is busy. Try again later",
            >= System.Net.HttpStatusCode.InternalServerError => "the server had a problem. Try again",
            _ => "the server could not accept that request",
        },
        System.Net.Http.HttpRequestException => "cannot reach the world server",
        OperationCanceledException => "the server took too long to answer",
        System.Text.Json.JsonException or InvalidDataException => "the server sent something unexpected",
        UnauthorizedAccessException => "the game could not access a needed file. Check its access permissions",
        System.Security.Cryptography.CryptographicException => "the device key could not be used. Try connecting this device again",
        IOException => "the game could not read or save a needed file. Check storage and try again",
        ArgumentException => "a setting could not be used. Check the entered values",
        _ => "the action could not be completed. Try again",
    };

    public static string ResourceQuantity(string kind, int? quantity, int? capacity) => quantity is null ? "" :
        (kind == "fertile_land" ? "Soil " : "") + quantity + (capacity is null ? "" : "/" + capacity);

    public static string ResourceTooltip(OwnerWorldResource resource)
    {
        var title = NaturalObjectName(resource.NaturalObjectKind) ?? resource.Kind switch
        {
            "construction" => "Wild timber",
            "food" => "Wild food",
            "fertile_land" => "Growing plot",
            _ => HumanizeIdentifier(resource.Kind),
        };
        var stock = ResourceQuantity(resource.Kind, resource.Quantity, resource.Capacity);
        var renewal = !resource.IsRenewable ? "Does not grow back." :
            resource.RegenerationAmount is > 0 && resource.RegenerationIntervalDays is > 0 && resource.RegenerationSeason is not null
                ? $"Grows back: +{resource.RegenerationAmount} every {resource.RegenerationIntervalDays} {(resource.RegenerationIntervalDays == 1 ? "day" : "days")} in {HumanizeIdentifier(resource.RegenerationSeason)}."
                : "Grows back.";
        return title + (stock.Length == 0 ? "" : " · " + stock) + "\n" + HumanizeIdentifier(resource.State) + "\n" + renewal;
    }

    private static string? NaturalObjectName(string? value) => value switch
    {
        "berry_bush" => "Berry bush",
        "wild_greens" => "Wild greens",
        "fiber_plant" => "Fiber plant",
        "reeds" => "Reeds",
        "stone_outcrop" => "Stone outcrop",
        "iron_outcrop" => "Iron outcrop",
        "gold_outcrop" => "Gold outcrop",
        "diamond_outcrop" => "Diamond outcrop",
        "clay_bank" => "Clay bank",
        "wild_seed_patch" => "Wild seed patch",
        "fertile_soil" => "Fertile soil",
        _ => null,
    };

    public static string FormatWorldClock(long worldTick, bool useTwelveHourClock = false,
        OwnerWorldCalendarPace? calendarPace = null, string dateFormat = "dmy")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);
        var ticksPerDay = calendarPace?.TicksPerDay ?? MinutesPerDay;
        var daysPerYear = calendarPace?.DaysPerYear ?? 365;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(daysPerYear);
        var dayIndex = worldTick / ticksPerDay;
        var date = FormatWorldDate(dayIndex, daysPerYear, dateFormat);
        var minuteOfDay = (int)(((worldTick % ticksPerDay) * MinutesPerDay) / ticksPerDay);
        var hour = minuteOfDay / 60;
        var minute = minuteOfDay % 60;
        if (!useTwelveHourClock) return $"{date} · {hour:00}:{minute:00}";
        var twelveHour = hour % 12;
        if (twelveHour == 0) twelveHour = 12;
        return $"{date} · {twelveHour}:{minute:00} {(hour < 12 ? "AM" : "PM")}";
    }

    private static string FormatWorldDate(long dayIndex, int daysPerYear, string dateFormat)
    {
        var year = (dayIndex / daysPerYear) + 1;
        var dayOfYear = (int)(dayIndex % daysPerYear);
        int month;
        int day;
        if (daysPerYear == 40)
        {
            month = (dayOfYear / 10) + 1;
            day = (dayOfYear % 10) + 1;
        }
        else if (daysPerYear == 365)
        {
            var monthLengths = new[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            month = 1;
            while (dayOfYear >= monthLengths[month - 1])
            {
                dayOfYear -= monthLengths[month - 1];
                month++;
            }
            day = dayOfYear + 1;
        }
        else
        {
            return $"Year {year} · Day {dayOfYear + 1}";
        }

        return dateFormat switch
        {
            "mdy" => $"{month:00}-{day:00}-{year:0000}",
            "ymd" => $"{year:0000}-{month:00}-{day:00}",
            _ => $"{day:00}-{month:00}-{year:0000}",
        };
    }

    public static bool IsPlayerFacingEvent(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return kind is "world_created" or "world_started" or "weather_changed" or "building_placed" or
            "build_started" or "build_completed" or "recipe_started" or "recipe_completed" or
            "crop_moisture_effect" or "food_harvested" or "food_consumed" or "child_born" or
            "inhabitant_removed" or "estate_will_accepted" or "estate_will_default" or
            "partnership_accepted" or "partnership_ended" or "caregiver_assigned" or
            "council_policy_adopted" or "settlement_trade_completed" or
            "inhabitant_building_proposed" or "settlement_founded" or "town_founding_started" or
            "town_resident_joined" or "town_resident_left" or "town_membership_evaluated" or
            "town_building_assigned" or "town_border_expanded" or "town_founded" or "paused" or "resumed";
    }

    /// <summary>
    /// Names another party the way the player sees it: an agent, a household
    /// by its stores name, or a Town, instead of a raw ID such as
    /// <c>household:camp-alpha</c>.
    /// </summary>
    public static string PartyName(OwnerWorldSnapshot? snapshot, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return snapshot?.Inhabitants.FirstOrDefault(person => person.Id == id)?.DisplayName
            ?? snapshot?.Stockpiles.FirstOrDefault(stockpile => stockpile.OwnerId == id)?.Name
            ?? snapshot?.Towns.FirstOrDefault(town => town.Id == id)?.Name
            ?? (id.StartsWith("household:", StringComparison.Ordinal) ? "a household" : HumanizeIdentifier(id));
    }

    /// <summary>
    /// Names how fed an agent is. The host reports fullness, so a low value
    /// means hungry; the bands follow the host's food-seeking thresholds.
    /// </summary>
    public static string FullnessState(int fullnessBasisPoints) => fullnessBasisPoints switch
    {
        >= 7_000 => "well fed",
        >= 3_500 => "fed",
        >= 2_500 => "hungry",
        _ => "very hungry",
    };

    /// <summary>
    /// Short present-tense activity for the roster. The host summary already
    /// reads naturally ("looking for food"); structured candidates such as
    /// building projects fall back to the readable candidate phrase.
    /// </summary>
    public static string ActivityPhrase(string? candidateId, string? summary)
    {
        if (!string.IsNullOrWhiteSpace(summary) && !summary.Contains(':', StringComparison.Ordinal))
            return summary.Trim();
        return string.IsNullOrWhiteSpace(candidateId) ? "taking in the surroundings" : HumanizeIdentifier(candidateId);
    }

    /// <summary>Describes one relationship in plain words for the agent card.</summary>
    public static string RelationshipSummary(string type, string state, string otherName, string? direction = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var summary = type switch
        {
            "household_membership" => $"Member of {otherName}",
            "biological_parentage" when direction == "parent" => $"Parent of {otherName}",
            "biological_parentage" when direction == "child" => $"Child of {otherName}",
            "partnership" => $"Partnership with {otherName}",
            _ => $"{char.ToUpperInvariant(type[0])}{type[1..].Replace('_', ' ')} with {otherName}",
        };
        return string.Equals(state, "accepted", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(state)
            ? summary
            : $"{summary} · {state.Replace('_', ' ')}";
    }

    public static string HumanizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "something";
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("guardian_tend:", StringComparison.Ordinal)) return "look after someone who is ill";
        if (normalized.StartsWith("care:", StringComparison.Ordinal)) return "look after a child";
        if (normalized.StartsWith("guardian_offer:", StringComparison.Ordinal)) return "offer to look after someone";
        if (normalized.StartsWith("guardian_accept:", StringComparison.Ordinal)) return "accept someone's care";
        if (normalized.StartsWith("guardian_refuse:", StringComparison.Ordinal)) return "turn down an offer of care";
        if (normalized.StartsWith("guardian_end:", StringComparison.Ordinal)) return "stop looking after someone";
        if (normalized.StartsWith("parent_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("parent_propose:", StringComparison.Ordinal) ? "talk about having a child"
                : normalized.StartsWith("parent_accept:", StringComparison.Ordinal) ? "agree to have a child" : "decide against having a child";
        }
        if (normalized.StartsWith("partner_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("partner_propose:", StringComparison.Ordinal) ? "propose a partnership"
                : normalized.StartsWith("partner_accept:", StringComparison.Ordinal) ? "accept a partnership"
                : normalized.StartsWith("partner_refuse:", StringComparison.Ordinal) ? "refuse a partnership" : "leave a partnership";
        }
        if (normalized.StartsWith("learn:", StringComparison.Ordinal))
        {
            return "ask to be taught a skill";
        }
        if (normalized.StartsWith("lesson_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("lesson_decline:", StringComparison.Ordinal) || normalized == "lesson_cancel"
                ? "turn down or stop a lesson" : "take a lesson";
        }
        if (normalized.StartsWith("council_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("council_propose:", StringComparison.Ordinal) ? "suggest a food rule"
                : normalized == "council_vote_yes" ? "vote for a food rule" : "vote against a food rule";
        }
        if (normalized.StartsWith("trade_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("trade_propose:", StringComparison.Ordinal) ? "offer a trade"
                : normalized.StartsWith("trade_accept:", StringComparison.Ordinal) ? "accept a trade" : "turn down a trade";
        }
        if (normalized.StartsWith("build:recipe:", StringComparison.Ordinal) ||
            normalized.StartsWith("build:building:", StringComparison.Ordinal))
        {
            var siteMarker = normalized.LastIndexOf(":site:", StringComparison.Ordinal);
            if (siteMarker >= 0)
                normalized = normalized[..siteMarker];
            var localId = normalized[(Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf(':')) + 1)..];
            var versionSeparator = localId.IndexOf('@');
            if (versionSeparator >= 0)
            {
                localId = localId[..versionSeparator];
            }
            return $"build {HumanizeIdentifier(localId)}";
        }

        var known = normalized switch
        {
            "safe_idle" => "take it easy",
            "seek_food" => "find food",
            "eat_food" => "eat",
            "consume_food" => "eat",
            "collect_shared_food" => "collect food from camp",
            "harvest_food" => "gather food",
            _ => null,
        };
        if (known is not null)
        {
            return known;
        }

        var words = normalized
            .Replace(':', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace('/', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "something";
        }

        var phrase = string.Join(' ', words.Select(word => word.ToLowerInvariant()));
        return char.ToUpperInvariant(phrase[0]) + phrase[1..];
    }
}
