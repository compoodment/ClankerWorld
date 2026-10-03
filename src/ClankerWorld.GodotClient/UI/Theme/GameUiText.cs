namespace ClankerWorld.GodotClient.UI;

/// <summary>
/// Converts simulation identifiers into the deliberately small, player-facing
/// vocabulary used by the game HUD. Protocol diagnostics remain available in
/// developer tools instead of leaking into ordinary play.
/// </summary>
public static class GameUiText
{
    private const int MinutesPerDay = 1_440;

    public static string ModelStatus(string? status) => status switch
    {
        "waiting" => "Waiting for the model",
        "canceled" => "Canceled by pause or disconnect",
        "missing_key" => "Missing key",
        "usage_limit" => "Usage limit reached",
        "unusable_reply" => "Unusable reply",
        "timed_out" => "Timed out",
        "model_unavailable" => "Model unavailable",
        _ => "Ready",
    };

    public static string ActorMapLabel(string displayName)
    {
        var words = displayName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        var given = words[0];
        return new System.Globalization.StringInfo(given).LengthInTextElements <= 12
            ? given : System.Globalization.StringInfo.GetNextTextElement(given) + ".";
    }

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

    /// <summary>
    /// Text from an agent's model or the host with each ellipsis character
    /// (U+2026) spelled as three full stops. The body font draws that character
    /// at mid-height, as Chinese text does, so game text never uses it.
    /// </summary>
    public static string PlainEllipses(string text) => text.Replace(Ellipsis, "...", StringComparison.Ordinal);

    /// <summary>The ellipsis character, written as an escape so searches for it find only mistakes.</summary>
    public const string Ellipsis = "\u2026";

    public static string ItemName(string kind) => kind switch
    {
        "storage_pot" => "Storage pot",
        "water_jug" => "Water jug",
        "fresh_water" => "Fresh water",
        "gold_ore" => "Gold ore",
        "gold" => "Refined gold",
        "gold_ornament" => "Gold ornament",
        "diamond_ornament" => "Diamond ornament",
        _ => HumanizeIdentifier(kind),
    };

    public static string FriendlyFailure(Exception exception) => exception switch
    {
        Pairing.OwnerAgentNameTakenException =>
            "that full name belongs to another agent. Choose a different name",
        Pairing.OwnerActionCompatibilityException =>
            "this client and world server need matching updates before making this change. Update both; your device pairing can stay as it is",
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
        "fallen_wood" => "Fallen wood",
        "iron_outcrop" => "Iron outcrop",
        "gold_outcrop" => "Gold outcrop",
        "diamond_outcrop" => "Diamond outcrop",
        "clay_bank" => "Clay bank",
        "wild_seed_patch" => "Wild seed patch",
        "fertile_soil" => "Fertile soil",
        _ => null,
    };

    /// <summary>
    /// The default date style: the season and its day, such as Autumn 2, Year 1.
    /// The numeric styles are "dmy", "mdy" and "ymd".
    /// </summary>
    public const string SeasonDates = "season";

    private static readonly string[] SeasonNames = ["Spring", "Summer", "Autumn", "Winter"];

    /// <summary>
    /// Whether dates in this style name the season. That needs the world's
    /// season lengths; without them, such as from an older host, a season-style
    /// date falls back to DD-MM-YYYY.
    /// </summary>
    public static bool ShowsSeasonDates(OwnerWorldCalendarPace? calendarPace, string dateFormat) =>
        SeasonDateLengths(calendarPace, dateFormat) is not null;

    /// <summary>
    /// The one place a world tick becomes the date and time the player reads:
    /// the top bar, Event Log, saves and every other full date use it.
    /// </summary>
    public static string FormatWorldClock(long worldTick, bool useTwelveHourClock = false,
        OwnerWorldCalendarPace? calendarPace = null, string dateFormat = SeasonDates)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);
        var ticksPerDay = calendarPace?.TicksPerDay ?? MinutesPerDay;
        var daysPerYear = calendarPace?.DaysPerYear ?? 365;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(daysPerYear);
        var dayIndex = worldTick / ticksPerDay;
        var date = SeasonDateLengths(calendarPace, dateFormat) is { } seasonLengths
            ? FormatSeasonDate(dayIndex, daysPerYear, seasonLengths)
            : FormatWorldDate(dayIndex, daysPerYear, dateFormat);
        var minuteOfDay = (int)(((worldTick % ticksPerDay) * MinutesPerDay) / ticksPerDay);
        var hour = minuteOfDay / 60;
        var minute = minuteOfDay % 60;
        if (!useTwelveHourClock) return $"{date} · {hour:00}:{minute:00}";
        var twelveHour = hour % 12;
        if (twelveHour == 0) twelveHour = 12;
        return $"{date} · {twelveHour}:{minute:00} {(hour < 12 ? "AM" : "PM")}";
    }

    /// <summary>
    /// Spring, Summer, Autumn and Winter lengths in days when dates in this
    /// style name the season; null for a numeric style, or unless the
    /// calendar's season lengths fill its year exactly.
    /// </summary>
    private static int[]? SeasonDateLengths(OwnerWorldCalendarPace? pace, string dateFormat)
    {
        if (pace is null || dateFormat is "dmy" or "mdy" or "ymd") return null;
        int[] lengths = [pace.SpringDays, pace.SummerDays, pace.AutumnDays, pace.WinterDays];
        return lengths.All(days => days > 0) && lengths.Sum(days => (long)days) == pace.DaysPerYear ? lengths : null;
    }

    /// <summary>The season and its day, counted the way the world counts seasons, such as Autumn 2, Year 1.</summary>
    private static string FormatSeasonDate(long dayIndex, int daysPerYear, int[] seasonLengths)
    {
        var year = (dayIndex / daysPerYear) + 1;
        var day = (int)(dayIndex % daysPerYear);
        var season = 0;
        while (day >= seasonLengths[season])
        {
            day -= seasonLengths[season];
            season++;
        }
        return $"{SeasonNames[season]} {day + 1}, Year {year}";
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
            "building_expansion_started" or "building_expanded" or "building_expansion_cancelled" or "house_guest_invited" or "house_guest_revoked" or
            "build_started" or "build_completed" or "recipe_started" or "recipe_completed" or
            "field_work_started" or "field_prepared" or "field_planted" or "field_tended" or "field_harvested" or
            "field_ready" or "field_work_interrupted" or "crop_weather_loss" or
            "crop_moisture_effect" or "food_harvested" or "food_consumed" or "tree_planted" or "tree_replanted" or "child_born" or
            "inhabitant_removed" or "estate_will_accepted" or "estate_will_default" or
            "partnership_accepted" or "partnership_ended" or "caregiver_assigned" or
            "continuity_rule_on" or "continuity_rule_off" or
            "medical_care_allowed" or "medical_care_revoked" or
            "medical_treatment_started" or "medical_treatment_completed" or "medical_treatment_interrupted" or
            "empty_vessel_picked_up" or
            "council_policy_adopted" or "settlement_trade_completed" or
            "business_trade_offered" or "business_trade_completed" or "business_trade_cancelled" or
            "store_stock_collected" or "store_stock_delivered" or
            "ornament_worn" or "ornament_removed" or "ornament_given" or
            "inhabitant_building_proposed" or "instruction_not_understood" or "settlement_founded" or "town_founding_started" or
            "town_civic_council" or "town_civic_election" or "town_civic_runoff" or "town_civic_proposal" or "town_civic_result" or "town_civic_cancelled" or
            "town_resident_joined" or "town_resident_left" or "town_membership_evaluated" or
            "town_building_assigned" or "town_border_expanded" or "town_founded" or "bridge_built" or
            "housing_request_made" or "household_joined" or "housing_request_refused" or "housing_request_expired" or
            "housing_blocked" or "household_left" or "household_founded" or "personal_goods_collected" or
            "household_work_resumed" or "personal_goods_stored" or "borrowed_goods_returned" or "replacement_care_accepted" or "paused" or "resumed" or "model_call_warning";
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
    /// <summary>
    /// When a world or save was last written, the way a person would say it:
    /// "just now", "12 minutes ago", "yesterday", or a date once it is a week old.
    /// </summary>
    public static string SavedAgo(DateTimeOffset saved, DateTimeOffset now)
    {
        var age = now - saved;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return Plural((int)age.TotalMinutes, "minute") + " ago";
        var savedDay = saved.ToLocalTime().Date;
        var today = now.ToLocalTime().Date;
        if (savedDay == today) return Plural((int)age.TotalHours, "hour") + " ago";
        if (savedDay == today.AddDays(-1)) return "yesterday";
        if (age < TimeSpan.FromDays(7)) return Plural((int)Math.Max(2, (today - savedDay).TotalDays), "day") + " ago";
        return saved.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Plural(int count, string unit) => count == 1 ? $"1 {unit}" : $"{count} {unit}s";

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
        if (candidateId?.StartsWith("wear_ornament:", StringComparison.Ordinal) == true) return "putting on an ornament";
        if (candidateId == "remove_ornament") return "taking off an ornament";
        if (candidateId?.StartsWith("gift_ornament:", StringComparison.Ordinal) == true) return "giving an ornament";
        if (candidateId?.StartsWith("return_empty_vessel:", StringComparison.Ordinal) == true) return "bringing an empty vessel home";
        if (candidateId?.StartsWith("medical_allow:", StringComparison.Ordinal) == true) return "allowing medical care";
        if (candidateId?.StartsWith("medical_revoke:", StringComparison.Ordinal) == true) return "withdrawing medical permission";
        if (candidateId?.StartsWith("medical_collect:", StringComparison.Ordinal) == true) return "collecting medicine";
        if (candidateId?.StartsWith("medical_treat:", StringComparison.Ordinal) == true) return "giving medicine";
        if (!string.IsNullOrWhiteSpace(summary) && !summary.Contains(':', StringComparison.Ordinal))
            return summary.Trim();
        return string.IsNullOrWhiteSpace(candidateId) ? "taking in the surroundings" : HumanizeIdentifier(candidateId);
    }

    public static string HumanizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "something";
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("return_empty_vessel:", StringComparison.Ordinal)) return "bring an empty vessel home";
        if (normalized.StartsWith("guardian_tend:", StringComparison.Ordinal)) return "look after someone who is ill";
        if (normalized.StartsWith("care:", StringComparison.Ordinal)) return "look after a child";
        if (normalized.StartsWith("guardian_offer:", StringComparison.Ordinal)) return "offer to look after someone";
        if (normalized.StartsWith("guardian_accept:", StringComparison.Ordinal)) return "accept someone's care";
        if (normalized.StartsWith("guardian_refuse:", StringComparison.Ordinal)) return "turn down an offer of care";
        if (normalized.StartsWith("guardian_end:", StringComparison.Ordinal)) return "stop looking after someone";
        if (normalized.StartsWith("parent_", StringComparison.Ordinal))
        {
            return normalized.StartsWith("parent_propose:", StringComparison.Ordinal) ? "talk about having a child"
                : normalized.StartsWith("parent_accept:", StringComparison.Ordinal) ? "agree to have a child"
                : normalized.StartsWith("parent_postpone:", StringComparison.Ordinal) ? "put off having a child" : "decide against having a child";
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
        if (normalized.StartsWith("civic|", StringComparison.Ordinal))
            return normalized.Split('|').ElementAtOrDefault(2) switch
            {
                "visit" => "visit the Town notice place",
                "read" => "read Town notices",
                "relay" => "relay Town notices",
                "nominate" => "nominate a council candidate",
                "request_admission" => "propose a newcomer's admission",
                "register" or "remainder" => "agree to stand for council",
                "withdraw_candidate" => "withdraw a candidacy",
                "propose" => "propose a Town rule",
                "admission" => "request Town admission",
                "yes" or "no" => "vote on a Town proposal",
                "withdraw_proposal" => "withdraw a proposal",
                "ballot" or "single" => "cast a council election ballot",
                _ => "take part in Town affairs",
            };
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
            "collect_material" => "collect personal materials",
            "store_material" => "store personal materials",
            "gather_material" => "gather materials",
            "inspect_material_site" => "look for the requested material",
            "storage_pot" => "storage pot",
            "water_jug" => "water jug",
            "fresh_water" => "fresh water",
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
