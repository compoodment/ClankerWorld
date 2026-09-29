namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Converts simulation identifiers into the deliberately small, player-facing
/// vocabulary used by the game HUD. Protocol diagnostics remain available in
/// developer tools instead of leaking into ordinary play.
/// </summary>
public static class GameUiText
{
    private const int MinutesPerDay = 1_440;

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
        var renewal = !resource.IsRenewable ? "Finite — no natural regrowth." :
            resource.RegenerationAmount is > 0 && resource.RegenerationIntervalDays is > 0 && resource.RegenerationSeason is not null
                ? $"Regrowth: +{resource.RegenerationAmount} every {resource.RegenerationIntervalDays} world day(s) in {HumanizeIdentifier(resource.RegenerationSeason)}."
                : "Renewable — regrowth details unavailable from this host.";
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
        return kind switch
        {
            "tick_advanced" or
            "inhabitant_moved" or
            "actor_moved" or
            "destination_reached" or
            "movement_blocked" or
            "inhabitant_idle" => false,
            "estate_will_started" => false,
            _ when kind.StartsWith("cognition_", StringComparison.Ordinal) => false,
            _ when kind.StartsWith("hosted_decision_", StringComparison.Ordinal) => false,
            _ when kind.StartsWith("instruction_", StringComparison.Ordinal) => false,
            _ when kind.StartsWith("content_", StringComparison.Ordinal) => false,
            _ when kind.StartsWith("initial_content_", StringComparison.Ordinal) => false,
            _ when kind.StartsWith("owner_", StringComparison.Ordinal) => false,
            _ when kind.EndsWith("_failed", StringComparison.Ordinal) => false,
            _ when kind.EndsWith("_rejected", StringComparison.Ordinal) => false,
            _ => true,
        };
    }

    public static string HumanizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "something";
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("guardian_tend:", StringComparison.Ordinal)) return "care for an ill dependent";
        if (normalized.StartsWith("care:", StringComparison.Ordinal)) return "care for a child";
        if (normalized.StartsWith("guardian_offer:", StringComparison.Ordinal)) return "offer to care for a dependent";
        if (normalized.StartsWith("guardian_accept:", StringComparison.Ordinal)) return "accept a caregiver";
        if (normalized.StartsWith("guardian_refuse:", StringComparison.Ordinal)) return "refuse a caregiver proposal";
        if (normalized.StartsWith("guardian_end:", StringComparison.Ordinal)) return "withdraw from caregiving";
        if (normalized.StartsWith("parent_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("parent_propose:", StringComparison.Ordinal) ? "discuss parenthood"
                : normalized.StartsWith("parent_accept:", StringComparison.Ordinal) ? "agree to parenthood" : "decline or withdraw parenthood";
        }
        if (normalized.StartsWith("partner_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("partner_propose:", StringComparison.Ordinal) ? "propose a partnership"
                : normalized.StartsWith("partner_accept:", StringComparison.Ordinal) ? "accept a partnership"
                : normalized.StartsWith("partner_refuse:", StringComparison.Ordinal) ? "refuse a partnership" : "leave a partnership";
        }
        if (normalized.StartsWith("learn:", StringComparison.Ordinal))
        {
            return "request practical training";
        }
        if (normalized.StartsWith("lesson_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("lesson_decline:", StringComparison.Ordinal) || normalized == "lesson_cancel"
                ? "decline or stop training" : "take part in training";
        }
        if (normalized.StartsWith("council_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("council_propose:", StringComparison.Ordinal) ? "propose a food policy"
                : normalized == "council_vote_yes" ? "support a food policy" : "oppose a food policy";
        }
        if (normalized.StartsWith("trade_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("trade_propose:", StringComparison.Ordinal) ? "offer an exchange"
                : normalized.StartsWith("trade_accept:", StringComparison.Ordinal) ? "accept an exchange" : "decline an exchange";
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
