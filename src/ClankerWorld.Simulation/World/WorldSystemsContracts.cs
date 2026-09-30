using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.World;

/// <summary>
/// The four bounded first-world seasons. Season boundaries are calendar data,
/// not host-clock time.
/// </summary>
public enum SeasonKind
{
    Spring,
    Summer,
    Autumn,
    Winter,
}

/// <summary>
/// The intentionally small weather vocabulary used by the richer-world
/// contract. More effects can be data additions without adding executable
/// content.
/// </summary>
public enum WeatherKind
{
    Clear,
    Cloudy,
    Rain,
    Storm,
    Snow,
}

public enum EcologyResourceState
{
    Available,
    Depleted,
    Regenerating,
    Transformed,
}

public enum LawActionKind
{
    Theft,
    Trespass,
    Violence,
    UnpermittedHarvest,
    Vandalism,
    Counterfeit,
    TaxEvasion,
}

public enum LawSeverity
{
    Warning,
    Minor,
    Major,
    Severe,
}

/// <summary>
/// A season's deterministic weather distribution. The weights are data, and
/// the weather rule draws from a seed-derived stream once per world day.
/// </summary>
public sealed record WeatherProfile(
    SeasonKind Season,
    int ClearWeight,
    int CloudyWeight,
    int RainWeight,
    int StormWeight,
    int SnowWeight)
{
    [JsonIgnore]
    public int TotalWeight => checked(ClearWeight + CloudyWeight + RainWeight + StormWeight + SnowWeight);

    public int WeightFor(WeatherKind weather) => weather switch
    {
        WeatherKind.Clear => ClearWeight,
        WeatherKind.Cloudy => CloudyWeight,
        WeatherKind.Rain => RainWeight,
        WeatherKind.Storm => StormWeight,
        WeatherKind.Snow => SnowWeight,
        _ => throw new ArgumentOutOfRangeException(nameof(weather)),
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Season) || ClearWeight < 0 || CloudyWeight < 0 ||
            RainWeight < 0 || StormWeight < 0 || SnowWeight < 0 ||
            (long)ClearWeight + CloudyWeight + RainWeight + StormWeight + SnowWeight > int.MaxValue ||
            TotalWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(WeatherProfile));
        }
    }
}

/// <summary>
/// Versioned bounds and calendar/weather data for the richer systems. The
/// contract deliberately has finite collection limits and no code hooks.
/// </summary>
public sealed record WorldSystemsConfig(
    int ContractVersion = 1,
    int TicksPerDay = KernelClock.TicksPerDay,
    int DaysPerYear = KernelClock.DaysPerYear,
    int SpringDays = 91,
    int SummerDays = 91,
    int AutumnDays = 91,
    int WinterDays = 92,
    int MaxChunkCount = 256,
    int MaxResourcesPerChunk = 64,
    int MaxCultureTags = 16,
    IReadOnlyList<WeatherProfile>? WeatherProfiles = null)
{
    public const int MaximumChunkCount = 1_024;
    public const int MaximumResourcesPerChunk = 128;
    public const int MaximumCultureTags = 32;

    private static readonly WeatherProfile[] BuiltInWeatherProfiles =
    [
        new(SeasonKind.Spring, 45, 25, 25, 5, 0),
        new(SeasonKind.Summer, 55, 20, 15, 10, 0),
        new(SeasonKind.Autumn, 35, 30, 20, 5, 10),
        new(SeasonKind.Winter, 30, 30, 5, 5, 30),
    ];

    public static WorldSystemsConfig Default { get; } = new(WeatherProfiles: BuiltInWeatherProfiles);

    [JsonIgnore]
    public IReadOnlyList<WeatherProfile> EffectiveWeatherProfiles =>
        WeatherProfiles ?? BuiltInWeatherProfiles;

    public WeatherProfile GetWeatherProfile(SeasonKind season) =>
        EffectiveWeatherProfiles.Single(profile => profile.Season == season);

    public void Validate()
    {
        if (ContractVersion <= 0 || TicksPerDay <= 0 || DaysPerYear <= 0 ||
            SpringDays <= 0 || SummerDays <= 0 || AutumnDays <= 0 || WinterDays <= 0 ||
            SpringDays + SummerDays + AutumnDays + WinterDays != DaysPerYear ||
            MaxChunkCount <= 0 || MaxChunkCount > MaximumChunkCount ||
            MaxResourcesPerChunk <= 0 || MaxResourcesPerChunk > MaximumResourcesPerChunk ||
            MaxCultureTags <= 0 || MaxCultureTags > MaximumCultureTags)
        {
            throw new ArgumentOutOfRangeException(nameof(WorldSystemsConfig));
        }

        var profiles = EffectiveWeatherProfiles;
        if (profiles.Count != 4 || profiles.Select(profile => profile.Season).Distinct().Count() != 4)
        {
            throw new ArgumentException("World weather must declare exactly one profile for each season.", nameof(WeatherProfiles));
        }

        foreach (var profile in profiles)
        {
            profile.Validate();
        }
    }
}

public sealed record WorldCalendar(
    long WorldTick,
    long DayIndex,
    int DayOfYear,
    int DayOfSeason,
    int TickOfDay,
    SeasonKind Season);

public static class WorldCalendarRules
{
    public static WorldCalendar FromTick(long worldTick, WorldSystemsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);

        var dayIndex = worldTick / config.TicksPerDay;
        var dayOfYear = (int)(dayIndex % config.DaysPerYear);
        var (season, firstDayOfSeason) = SeasonAtDay(dayOfYear, config);
        return new(
            worldTick,
            dayIndex,
            dayOfYear,
            dayOfYear - firstDayOfSeason,
            (int)(worldTick % config.TicksPerDay),
            season);
    }

    public static SeasonKind GetSeason(long dayOfYear, WorldSystemsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        if (dayOfYear < 0 || dayOfYear >= config.DaysPerYear)
        {
            throw new ArgumentOutOfRangeException(nameof(dayOfYear));
        }

        return SeasonAtDay((int)dayOfYear, config).Season;
    }

    private static (SeasonKind Season, int FirstDay) SeasonAtDay(int dayOfYear, WorldSystemsConfig config)
    {
        if (dayOfYear < config.SpringDays)
        {
            return (SeasonKind.Spring, 0);
        }

        if (dayOfYear < config.SpringDays + config.SummerDays)
        {
            return (SeasonKind.Summer, config.SpringDays);
        }

        if (dayOfYear < config.SpringDays + config.SummerDays + config.AutumnDays)
        {
            return (SeasonKind.Autumn, config.SpringDays + config.SummerDays);
        }

        return (
            SeasonKind.Winter,
            config.SpringDays + config.SummerDays + config.AutumnDays);
    }
}

public sealed record WorldClimate(long WorldTick, SeasonKind Season, WeatherKind Weather);

/// <summary>
/// Pure seasonal weather transitions. A transition is stable during a day and
/// chooses the next day's weather from a stream derived only from world seed,
/// stream name, and day index.
/// </summary>
public static class WeatherRules
{
    // Weather is a local world condition, not one planet-wide roll. Regions
    // are deliberately smaller than terrain chunks so even Small worlds have
    // northern/southern climate bands.
    public const int RegionSize = 32;

    public static WeatherKind At(WorldSystemsState state, GridPoint position, int mapHeight,
        ClimateZone? climate = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegative(position.X);
        ArgumentOutOfRangeException.ThrowIfNegative(position.Y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapHeight);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position.Y, mapHeight, nameof(position));
        if (state.RegionalWeather is { } regions)
        {
            var episode = regions.Episodes.FirstOrDefault(item =>
                item.X == position.X / RegionSize && item.Y == position.Y / RegionSize);
            if (episode is not null) return episode.Weather;
        }
        var calendar = WorldCalendarRules.FromTick(state.WorldTick, state.Config);
        var weather = mapHeight <= RegionSize ? state.Climate.Weather : WeatherForRegion(state.WorldSeed,
            calendar.DayIndex, state.Climate.Season, state.Config,
            position.X / RegionSize, position.Y / RegionSize, (mapHeight + RegionSize - 1) / RegionSize,
            climate);
        // A daily roll can select a storm, but the severe event itself must
        // end within three quarters of that saved world's day. The remaining
        // quarter stays wet without continuing the storm. Round down so short
        // fixture days never exceed the same three-quarter limit.
        var severeTicks = (int)((long)state.Config.TicksPerDay * 3 / 4);
        return weather == WeatherKind.Storm && calendar.TickOfDay >= severeTicks
            ? WeatherKind.Rain
            : weather;
    }

    /// <summary>
    /// A bounded, reproducible moisture estimate from the three most recent
    /// local weather days. It needs no per-tick save churn; detailed soil and
    /// drainage state can replace this estimate when farming is expanded.
    /// </summary>
    public static int SoilMoistureAt(WorldSystemsState state, GridPoint position, int mapHeight,
        ClimateZone? climate = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegative(position.X);
        ArgumentOutOfRangeException.ThrowIfNegative(position.Y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapHeight);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position.Y, mapHeight, nameof(position));
        var day = WorldCalendarRules.FromTick(state.WorldTick, state.Config).DayIndex;
        var rows = (mapHeight + RegionSize - 1) / RegionSize;
        var moisture = 35;
        for (var sample = Math.Max(0, day - 2); sample <= day; sample++)
        {
            var season = WorldCalendarRules.GetSeason(sample % state.Config.DaysPerYear, state.Config);
            var weather = mapHeight <= RegionSize
                ? WeatherForDay(state.WorldSeed, sample, season, state.Config)
                : WeatherForRegion(state.WorldSeed, sample, season, state.Config,
                    position.X / RegionSize, position.Y / RegionSize, rows, climate);
            moisture = Math.Clamp(moisture + (weather switch
            {
                WeatherKind.Clear => -8,
                WeatherKind.Cloudy => -3,
                WeatherKind.Rain => 22,
                WeatherKind.Storm => 28,
                WeatherKind.Snow => 6,
                _ => throw new InvalidOperationException("The regional weather value is invalid."),
            }), 0, 100);
        }
        return moisture;
    }

    public static WeatherKind WeatherForRegion(string worldSeed, long dayIndex, SeasonKind season,
        WorldSystemsConfig config, int regionX, int regionY, int regionRows,
        ClimateZone? climate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(regionX);
        ArgumentOutOfRangeException.ThrowIfNegative(regionY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(regionRows);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(regionY, regionRows);
        if (climate is { } selected && !Enum.IsDefined(selected))
            throw new ArgumentOutOfRangeException(nameof(climate));

        var weights = RegionalWeights(season, config, regionY, regionRows, climate);
        var random = Pcg32XshRrV1.Create(worldSeed,
            $"weather/day:{dayIndex.ToString(CultureInfo.InvariantCulture)}/region:{regionX.ToString(CultureInfo.InvariantCulture)},{regionY.ToString(CultureInfo.InvariantCulture)}");
        var roll = (int)(random.NextUInt() % (uint)weights.Sum());
        foreach (var weather in Enum.GetValues<WeatherKind>())
        {
            roll -= weights[(int)weather];
            if (roll < 0) return weather;
        }
        throw new InvalidOperationException("The regional weather profile did not select a weather value.");
    }

    internal static int[] RegionalWeights(SeasonKind season, WorldSystemsConfig config,
        int regionY, int regionRows, ClimateZone? climate)
    {
        var profile = config.GetWeatherProfile(season);
        // Snow is confined to cold latitudes. This is a coarse first climate
        // rule; long-run rainfall and individual weather events remain distinct.
        var latitude = Math.Abs(((regionY + 0.5) / regionRows) - 0.5) * 2;
        var snowWeight = latitude >= 0.65 ? profile.SnowWeight : 0;
        var rainWeight = profile.RainWeight + profile.SnowWeight - snowWeight;
        var clearWeight = profile.ClearWeight;
        var stormWeight = profile.StormWeight;
        if (climate is ClimateZone.Dry)
        {
            var removedRain = rainWeight / 2;
            var removedStorm = stormWeight / 2;
            rainWeight -= removedRain;
            stormWeight -= removedStorm;
            clearWeight += removedRain + removedStorm;
            clearWeight += snowWeight;
            snowWeight = 0;
        }
        else if (climate is ClimateZone.Tropical)
        {
            rainWeight += snowWeight;
            snowWeight = 0;
            var extraRain = clearWeight / 4;
            clearWeight -= extraRain;
            rainWeight += extraRain;
        }
        else if (climate is ClimateZone.Cold or ClimateZone.Polar)
        {
            var shifted = rainWeight * (climate == ClimateZone.Polar ? 4 : 2) / 5;
            rainWeight -= shifted;
            snowWeight += shifted;
        }
        return [clearWeight, profile.CloudyWeight, rainWeight, stormWeight, snowWeight];
    }

    public static ClimateZone? RegionClimate(SeededMap map, GridPoint position)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.Contains(position)) return null;
        var x = Math.Min(map.Width - 1, (position.X / RegionSize) * RegionSize + RegionSize / 2);
        var y = Math.Min(map.Height - 1, (position.Y / RegionSize) * RegionSize + RegionSize / 2);
        return map.ClimateAt(new GridPoint(x, y));
    }

    public static WorldClimate CreateGenesis(string worldSeed, WorldSystemsConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        var calendar = WorldCalendarRules.FromTick(0, config);
        return new(0, calendar.Season, WeatherForDay(worldSeed, calendar.DayIndex, calendar.Season, config));
    }

    public static WorldClimate Advance(
        WorldClimate current,
        long targetTick,
        string worldSeed,
        WorldSystemsConfig config)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(targetTick);
        if (targetTick < current.WorldTick)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTick), "Weather cannot move backward in world time.");
        }

        var targetCalendar = WorldCalendarRules.FromTick(targetTick, config);
        var currentCalendar = WorldCalendarRules.FromTick(current.WorldTick, config);
        var crossesDay = targetCalendar.DayIndex != currentCalendar.DayIndex;
        var crossesSeason = targetCalendar.Season != current.Season;
        var weather = crossesDay || crossesSeason
            ? WeatherForDay(worldSeed, targetCalendar.DayIndex, targetCalendar.Season, config)
            : current.Weather;
        return new(targetTick, targetCalendar.Season, weather);
    }

    public static WeatherKind WeatherForDay(
        string worldSeed,
        long dayIndex,
        SeasonKind season,
        WorldSystemsConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);

        var profile = config.GetWeatherProfile(season);
        var random = Pcg32XshRrV1.Create(
            worldSeed,
            $"weather/day:{dayIndex.ToString(CultureInfo.InvariantCulture)}");
        var roll = (int)(random.NextUInt() % (uint)profile.TotalWeight);
        foreach (var weather in Enum.GetValues<WeatherKind>())
        {
            roll -= profile.WeightFor(weather);
            if (roll < 0)
            {
                return weather;
            }
        }

        throw new InvalidOperationException("The weather profile did not select a weather value.");
    }
}

public sealed record EcologyResource(
    string Id,
    string Kind,
    GridPoint Position,
    bool IsRenewable,
    int Quantity,
    int Capacity,
    int RegenerationAmount,
    int RegenerationIntervalDays,
    SeasonKind RegenerationSeason,
    long NextRegenerationDay,
    EcologyResourceState State,
    bool IsPlanted = false)
{
    public void Validate()
    {
        WorldSystemsRules.RequireIdentifier(Id, nameof(Id));
        WorldSystemsRules.RequireIdentifier(Kind, nameof(Kind));
        if (!Enum.IsDefined(RegenerationSeason) || Quantity < 0 || Capacity <= 0 ||
            Quantity > Capacity || NextRegenerationDay < 0 ||
            (IsRenewable && (RegenerationAmount <= 0 || RegenerationIntervalDays <= 0)) ||
            (!IsRenewable && (RegenerationAmount != 0 || RegenerationIntervalDays != 0)) ||
            !Enum.IsDefined(State) || (!IsRenewable && State == EcologyResourceState.Regenerating) ||
            (IsPlanted && (!IsRenewable || Quantity != 0 || State != EcologyResourceState.Regenerating)))
        {
            throw new ArgumentOutOfRangeException(nameof(EcologyResource));
        }
    }
}

public sealed record EcologyState(IReadOnlyList<EcologyResource> Resources)
{
    public EcologyResource GetResource(string id) =>
        Resources.Single(resource => string.Equals(resource.Id, id, StringComparison.Ordinal));
}

public sealed record EcologyHarvestResult(
    bool IsValid,
    string? Failure,
    int Yield,
    EcologyResource? Resource);

/// <summary>
/// Data-only ecology rules. Regeneration is an explicit, idempotent function
/// of committed calendar state and a resource's recorded schedule.
/// </summary>
public static class EcologyRules
{
    public static void Validate(EcologyState state, WorldSystemsConfig config)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        if (state.Resources.Count > config.MaxResourcesPerChunk * config.MaxChunkCount)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "The bounded ecology collection is too large.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in state.Resources)
        {
            ArgumentNullException.ThrowIfNull(resource);
            resource.Validate();
            if (!ids.Add(resource.Id))
            {
                throw new ArgumentException($"Duplicate ecology resource '{resource.Id}'.", nameof(state));
            }
        }
    }

    public static EcologyResource Regenerate(
        EcologyResource resource,
        WorldCalendar calendar,
        WorldSystemsConfig config)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        resource.Validate();

        if (!resource.IsRenewable || resource.State == EcologyResourceState.Transformed)
        {
            return resource;
        }

        if (resource.IsPlanted)
        {
            if (calendar.DayIndex < resource.NextRegenerationDay)
                return resource;
            return resource with
            {
                Quantity = 1,
                NextRegenerationDay = checked(calendar.DayIndex + resource.RegenerationIntervalDays),
                State = EcologyResourceState.Available,
                IsPlanted = false,
            };
        }

        if (resource.Quantity >= resource.Capacity)
        {
            return resource with { State = EcologyResourceState.Available };
        }

        if (calendar.DayIndex < resource.NextRegenerationDay ||
            resource.Kind != "fruit" && calendar.Season != resource.RegenerationSeason)
        {
            return resource with
            {
                State = resource.Quantity == 0
                    ? EcologyResourceState.Regenerating
                    : EcologyResourceState.Available,
            };
        }

        var quantity = Math.Min(
            resource.Capacity,
            checked(resource.Quantity + resource.RegenerationAmount));
        return resource with
        {
            Quantity = quantity,
            NextRegenerationDay = checked(calendar.DayIndex + resource.RegenerationIntervalDays),
            State = quantity == 0
                ? EcologyResourceState.Regenerating
                : EcologyResourceState.Available,
        };
    }

    public static EcologyState Advance(EcologyState state, WorldCalendar calendar, WorldSystemsConfig config)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(config);
        Validate(state, config);

        var resources = state.Resources
            .OrderBy(resource => resource.Id, StringComparer.Ordinal)
            .Select(resource => Regenerate(resource, calendar, config))
            .ToArray();
        return state with { Resources = resources };
    }

    public static EcologyHarvestResult Harvest(EcologyResource resource, int amount)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resource.Validate();
        if (amount <= 0)
        {
            return new(false, "Harvest amount must be positive.", 0, null);
        }

        if (resource.State is EcologyResourceState.Depleted or EcologyResourceState.Transformed ||
            amount > resource.Quantity)
        {
            return new(false, "The resource cannot supply the requested harvest.", 0, null);
        }

        var remaining = resource.Quantity - amount;
        var updated = resource with
        {
            Quantity = remaining,
            State = remaining == 0
                ? EcologyResourceState.Depleted
                : EcologyResourceState.Available,
        };
        return new(true, null, amount, updated);
    }
}

public sealed record FactionDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> CultureIds);

public sealed record FactionStanding(
    string ActorId,
    string FactionId,
    int StandingBasisPoints);

public sealed record LawRule(
    string Id,
    string JurisdictionFactionId,
    LawActionKind ProhibitedAction,
    LawSeverity Severity,
    int StandingPenaltyBasisPoints,
    long FineMinorUnits,
    bool RequiresPermit = false);

public sealed record LawAction(
    string ActionId,
    string ActorId,
    string JurisdictionFactionId,
    LawActionKind Kind,
    long WorldTick,
    GridPoint? Location = null,
    bool HasPermit = false,
    string? SubjectId = null);

public sealed record LawViolation(
    string ViolationId,
    string LawId,
    string ActionId,
    string ActorId,
    string FactionId,
    LawActionKind Kind,
    LawSeverity Severity,
    int StandingPenaltyBasisPoints,
    long FineMinorUnits,
    long WorldTick,
    GridPoint? Location);

public sealed record LawEvaluation(IReadOnlyList<LawViolation> Violations)
{
    [JsonIgnore]
    public bool IsViolation => Violations.Count != 0;
}

public sealed record FactionState(
    IReadOnlyList<FactionDefinition> Factions,
    IReadOnlyList<FactionStanding> Standings,
    IReadOnlyList<LawRule> Laws,
    IReadOnlyList<LawViolation> Violations)
{
    public FactionDefinition GetFaction(string id) =>
        Factions.Single(faction => string.Equals(faction.Id, id, StringComparison.Ordinal));

    public FactionStanding? FindStanding(string actorId, string factionId) => Standings
        .SingleOrDefault(standing => string.Equals(standing.ActorId, actorId, StringComparison.Ordinal) &&
            string.Equals(standing.FactionId, factionId, StringComparison.Ordinal));
}

/// <summary>
/// Faction and law evaluation is declarative: a law names the action it
/// prohibits, and evaluation produces an immutable violation record.
/// </summary>
public static class FactionRules
{
    public const int MinimumStandingBasisPoints = -10_000;
    public const int MaximumStandingBasisPoints = 10_000;
    public const int MaximumFactionCount = 256;
    public const int MaximumStandingCount = 4_096;
    public const int MaximumLawCount = 1_024;
    public const int MaximumViolationCount = 8_192;

    public static void Validate(FactionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Factions.Count > MaximumFactionCount ||
            state.Standings.Count > MaximumStandingCount ||
            state.Laws.Count > MaximumLawCount ||
            state.Violations.Count > MaximumViolationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "The bounded faction and law collections are too large.");
        }

        var factionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var faction in state.Factions)
        {
            ArgumentNullException.ThrowIfNull(faction);
            WorldSystemsRules.RequireIdentifier(faction.Id, nameof(faction.Id));
            WorldSystemsRules.RequireText(faction.Name, nameof(faction.Name));
            if (!factionIds.Add(faction.Id) ||
                faction.CultureIds.Any(string.IsNullOrWhiteSpace) ||
                faction.CultureIds.Distinct(StringComparer.Ordinal).Count() != faction.CultureIds.Count)
            {
                throw new ArgumentException("Faction IDs and culture references must be unique and non-empty.", nameof(state));
            }
        }

        var standingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var standing in state.Standings)
        {
            WorldSystemsRules.RequireIdentifier(standing.ActorId, nameof(standing.ActorId));
            WorldSystemsRules.RequireIdentifier(standing.FactionId, nameof(standing.FactionId));
            if (standing.StandingBasisPoints is < MinimumStandingBasisPoints or > MaximumStandingBasisPoints ||
                !factionIds.Contains(standing.FactionId) ||
                !standingKeys.Add($"{standing.ActorId}\u001f{standing.FactionId}"))
            {
                throw new ArgumentOutOfRangeException(nameof(state), "Faction standing is invalid or duplicated.");
            }
        }

        var lawIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var law in state.Laws)
        {
            ArgumentNullException.ThrowIfNull(law);
            WorldSystemsRules.RequireIdentifier(law.Id, nameof(law.Id));
            WorldSystemsRules.RequireIdentifier(law.JurisdictionFactionId, nameof(law.JurisdictionFactionId));
            if (!Enum.IsDefined(law.ProhibitedAction) || !Enum.IsDefined(law.Severity) ||
                !factionIds.Contains(law.JurisdictionFactionId) ||
                law.StandingPenaltyBasisPoints < 0 || law.StandingPenaltyBasisPoints > MaximumStandingBasisPoints ||
                law.FineMinorUnits < 0 ||
                !lawIds.Add(law.Id))
            {
                throw new ArgumentOutOfRangeException(nameof(state), "A law rule is invalid or duplicated.");
            }
        }

        var violationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var violation in state.Violations)
        {
            ValidateViolation(violation);
            if (!violationIds.Add(violation.ViolationId))
            {
                throw new ArgumentException("Duplicate law violation.", nameof(state));
            }
        }
    }

    public static LawEvaluation Evaluate(FactionState state, LawAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        Validate(state);
        ValidateAction(action);
        if (!state.Factions.Any(faction =>
                string.Equals(faction.Id, action.JurisdictionFactionId, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The law action must name a known faction jurisdiction.", nameof(action));
        }

        var violations = state.Laws
            .Where(law => string.Equals(law.JurisdictionFactionId, action.JurisdictionFactionId, StringComparison.Ordinal))
            .Where(law => law.ProhibitedAction == action.Kind)
            .Where(law => !law.RequiresPermit || !action.HasPermit)
            .OrderBy(law => law.Id, StringComparer.Ordinal)
            .Select(law => new LawViolation(
                $"violation:{action.ActionId}:{law.Id}",
                law.Id,
                action.ActionId,
                action.ActorId,
                action.JurisdictionFactionId,
                action.Kind,
                law.Severity,
                law.StandingPenaltyBasisPoints,
                law.FineMinorUnits,
                action.WorldTick,
                action.Location))
            .ToArray();
        return new(violations);
    }

    public static FactionState ApplyViolations(FactionState state, LawEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(evaluation);
        Validate(state);
        foreach (var violation in evaluation.Violations)
        {
            ValidateViolation(violation);
            var law = state.Laws.SingleOrDefault(item =>
                string.Equals(item.Id, violation.LawId, StringComparison.Ordinal));
            if (law is null ||
                !string.Equals(law.JurisdictionFactionId, violation.FactionId, StringComparison.Ordinal) ||
                law.ProhibitedAction != violation.Kind || law.Severity != violation.Severity ||
                law.StandingPenaltyBasisPoints != violation.StandingPenaltyBasisPoints ||
                law.FineMinorUnits != violation.FineMinorUnits)
            {
                throw new ArgumentException("The law violation does not match a declared law rule.", nameof(evaluation));
            }
        }

        var newViolations = state.Violations.ToDictionary(item => item.ViolationId, StringComparer.Ordinal);
        var standings = state.Standings.ToDictionary(
            item => $"{item.ActorId}\u001f{item.FactionId}",
            StringComparer.Ordinal);
        foreach (var violation in evaluation.Violations.OrderBy(item => item.ViolationId, StringComparer.Ordinal))
        {
            if (!newViolations.TryAdd(violation.ViolationId, violation))
            {
                continue;
            }

            var key = $"{violation.ActorId}\u001f{violation.FactionId}";
            if (standings.TryGetValue(key, out var current))
            {
                standings[key] = current with
                {
                    StandingBasisPoints = ClampStanding(
                        current.StandingBasisPoints - violation.StandingPenaltyBasisPoints),
                };
            }
            else
            {
                standings[key] = new FactionStanding(
                    violation.ActorId,
                    violation.FactionId,
                    ClampStanding(-violation.StandingPenaltyBasisPoints));
            }
        }

        return state with
        {
            Standings = standings.Values
                .OrderBy(item => item.ActorId, StringComparer.Ordinal)
                .ThenBy(item => item.FactionId, StringComparer.Ordinal)
                .ToArray(),
            Violations = newViolations.Values
                .OrderBy(item => item.ViolationId, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    public static FactionStanding AdjustStanding(
        FactionStanding standing,
        int deltaBasisPoints)
    {
        ArgumentNullException.ThrowIfNull(standing);
        WorldSystemsRules.RequireIdentifier(standing.ActorId, nameof(standing.ActorId));
        WorldSystemsRules.RequireIdentifier(standing.FactionId, nameof(standing.FactionId));
        if (standing.StandingBasisPoints is < MinimumStandingBasisPoints or > MaximumStandingBasisPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(standing));
        }

        return standing with
        {
            StandingBasisPoints = ClampStanding((long)standing.StandingBasisPoints + deltaBasisPoints),
        };
    }

    private static int ClampStanding(long value) =>
        (int)Math.Clamp(value, MinimumStandingBasisPoints, MaximumStandingBasisPoints);

    private static void ValidateAction(LawAction action)
    {
        WorldSystemsRules.RequireIdentifier(action.ActionId, nameof(action.ActionId));
        WorldSystemsRules.RequireIdentifier(action.ActorId, nameof(action.ActorId));
        WorldSystemsRules.RequireIdentifier(action.JurisdictionFactionId, nameof(action.JurisdictionFactionId));
        if (!Enum.IsDefined(action.Kind) || action.WorldTick < 0 ||
            (action.SubjectId is not null && string.IsNullOrWhiteSpace(action.SubjectId)))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private static void ValidateViolation(LawViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);
        WorldSystemsRules.RequireIdentifier(violation.ViolationId, nameof(violation.ViolationId));
        WorldSystemsRules.RequireIdentifier(violation.LawId, nameof(violation.LawId));
        WorldSystemsRules.RequireIdentifier(violation.ActionId, nameof(violation.ActionId));
        WorldSystemsRules.RequireIdentifier(violation.ActorId, nameof(violation.ActorId));
        WorldSystemsRules.RequireIdentifier(violation.FactionId, nameof(violation.FactionId));
        if (!Enum.IsDefined(violation.Kind) || !Enum.IsDefined(violation.Severity) ||
            violation.StandingPenaltyBasisPoints < 0 ||
            violation.StandingPenaltyBasisPoints > MaximumStandingBasisPoints ||
            violation.FineMinorUnits < 0 || violation.WorldTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(violation));
        }
    }
}

public sealed record CurrencyDefinition(
    string Id,
    string Name,
    string Symbol,
    int MinorUnitsPerUnit = 100);

public sealed record CurrencyAccount(
    string Id,
    string OwnerId,
    string CurrencyId,
    long BalanceMinorUnits);

public sealed record CurrencyTransfer(
    string TransferId,
    string InitiatorId,
    string FromAccountId,
    string ToAccountId,
    string CurrencyId,
    long AmountMinorUnits,
    long SubmittedTick);

public sealed record CurrencyState(
    IReadOnlyList<CurrencyDefinition> Currencies,
    IReadOnlyList<CurrencyAccount> Accounts,
    IReadOnlyList<CurrencyTransfer> Transfers)
{
    public CurrencyAccount GetAccount(string id) =>
        Accounts.Single(account => string.Equals(account.Id, id, StringComparison.Ordinal));
}

public sealed record CurrencyTransferValidation(bool IsValid, string? Failure);

public sealed record CurrencyTransferResult(
    bool IsValid,
    string? Failure,
    CurrencyState? State);

/// <summary>
/// Currency is integer minor-unit data. Validation and settlement are pure
/// functions over a checkpoint; there is no wallet, network, or external
/// payment provider hidden behind this contract.
/// </summary>
public static class CurrencyRules
{
    public const int MaximumCurrencyCount = 32;
    public const int MaximumAccountCount = 4_096;
    public const int MaximumTransferCount = 16_384;

    public static void Validate(CurrencyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Currencies.Count > MaximumCurrencyCount ||
            state.Accounts.Count > MaximumAccountCount ||
            state.Transfers.Count > MaximumTransferCount)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "The bounded currency collections are too large.");
        }

        var currencyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var currency in state.Currencies)
        {
            ArgumentNullException.ThrowIfNull(currency);
            WorldSystemsRules.RequireIdentifier(currency.Id, nameof(currency.Id));
            WorldSystemsRules.RequireText(currency.Name, nameof(currency.Name));
            WorldSystemsRules.RequireText(currency.Symbol, nameof(currency.Symbol));
            if (currency.MinorUnitsPerUnit <= 0 || currency.MinorUnitsPerUnit > 1_000_000 ||
                !currencyIds.Add(currency.Id))
            {
                throw new ArgumentOutOfRangeException(nameof(state), "Currency definitions are invalid or duplicated.");
            }
        }

        var accountIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in state.Accounts)
        {
            ArgumentNullException.ThrowIfNull(account);
            WorldSystemsRules.RequireIdentifier(account.Id, nameof(account.Id));
            WorldSystemsRules.RequireIdentifier(account.OwnerId, nameof(account.OwnerId));
            WorldSystemsRules.RequireIdentifier(account.CurrencyId, nameof(account.CurrencyId));
            if (account.BalanceMinorUnits < 0 ||
                !currencyIds.Contains(account.CurrencyId) ||
                !accountIds.Add(account.Id))
            {
                throw new ArgumentOutOfRangeException(nameof(state), "Currency accounts are invalid or duplicated.");
            }
        }

        var transferIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transfer in state.Transfers)
        {
            ValidateTransferShape(transfer);
            if (!transferIds.Add(transfer.TransferId))
            {
                throw new ArgumentException("Duplicate currency transfer.", nameof(state));
            }
        }
    }

    public static CurrencyTransferValidation ValidateTransfer(
        CurrencyState state,
        CurrencyTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(transfer);
        Validate(state);
        if (!TryValidateTransferShape(transfer, out var shapeFailure))
        {
            return new(false, shapeFailure);
        }

        if (state.Transfers.Any(item => string.Equals(item.TransferId, transfer.TransferId, StringComparison.Ordinal)))
        {
            return new(false, "The transfer ID has already been committed.");
        }

        var source = state.Accounts.SingleOrDefault(account =>
            string.Equals(account.Id, transfer.FromAccountId, StringComparison.Ordinal));
        var destination = state.Accounts.SingleOrDefault(account =>
            string.Equals(account.Id, transfer.ToAccountId, StringComparison.Ordinal));
        if (source is null || destination is null)
        {
            return new(false, "Both currency accounts must exist.");
        }

        if (!state.Currencies.Any(currency =>
                string.Equals(currency.Id, transfer.CurrencyId, StringComparison.Ordinal)))
        {
            return new(false, "The transfer currency must be declared by the world.");
        }

        if (string.Equals(source.Id, destination.Id, StringComparison.Ordinal))
        {
            return new(false, "A currency transfer needs distinct source and destination accounts.");
        }

        if (!string.Equals(source.OwnerId, transfer.InitiatorId, StringComparison.Ordinal))
        {
            return new(false, "Only the source account owner may initiate the transfer.");
        }

        if (!string.Equals(source.CurrencyId, transfer.CurrencyId, StringComparison.Ordinal) ||
            !string.Equals(destination.CurrencyId, transfer.CurrencyId, StringComparison.Ordinal))
        {
            return new(false, "Both accounts must use the transfer currency.");
        }

        if (source.BalanceMinorUnits < transfer.AmountMinorUnits)
        {
            return new(false, "The source account has insufficient funds.");
        }

        if (destination.BalanceMinorUnits > long.MaxValue - transfer.AmountMinorUnits)
        {
            return new(false, "The destination balance would overflow.");
        }

        return new(true, null);
    }

    public static CurrencyTransferResult ApplyTransfer(
        CurrencyState state,
        CurrencyTransfer transfer)
    {
        var validation = ValidateTransfer(state, transfer);
        if (!validation.IsValid)
        {
            return new(false, validation.Failure, null);
        }

        var accounts = state.Accounts
            .Select(account => account.Id == transfer.FromAccountId
                ? account with { BalanceMinorUnits = account.BalanceMinorUnits - transfer.AmountMinorUnits }
                : account.Id == transfer.ToAccountId
                    ? account with { BalanceMinorUnits = checked(account.BalanceMinorUnits + transfer.AmountMinorUnits) }
                    : account)
            .OrderBy(account => account.Id, StringComparer.Ordinal)
            .ToArray();
        var transfers = state.Transfers
            .Append(transfer)
            .OrderBy(item => item.TransferId, StringComparer.Ordinal)
            .ToArray();
        return new(true, null, state with { Accounts = accounts, Transfers = transfers });
    }

    private static void ValidateTransferShape(CurrencyTransfer transfer)
    {
        if (!TryValidateTransferShape(transfer, out var failure))
        {
            throw new ArgumentOutOfRangeException(nameof(transfer), failure);
        }
    }

    private static bool TryValidateTransferShape(CurrencyTransfer transfer, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        WorldSystemsRules.RequireIdentifier(transfer.TransferId, nameof(transfer.TransferId));
        WorldSystemsRules.RequireIdentifier(transfer.InitiatorId, nameof(transfer.InitiatorId));
        WorldSystemsRules.RequireIdentifier(transfer.FromAccountId, nameof(transfer.FromAccountId));
        WorldSystemsRules.RequireIdentifier(transfer.ToAccountId, nameof(transfer.ToAccountId));
        WorldSystemsRules.RequireIdentifier(transfer.CurrencyId, nameof(transfer.CurrencyId));
        if (transfer.AmountMinorUnits <= 0 || transfer.SubmittedTick < 0)
        {
            failure = "The transfer amount must be positive and the submitted tick cannot be negative.";
            return false;
        }

        failure = null;
        return true;
    }
}

public sealed record CultureDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    string? ParentCultureId = null);

public sealed record CultureAssignment(
    string SubjectId,
    string CultureId,
    IReadOnlyList<string> Tags);

public sealed record CultureState(
    IReadOnlyList<CultureDefinition> Cultures,
    IReadOnlyList<CultureAssignment> Assignments);

/// <summary>
/// Culture is bounded normalized tag data. Tags carry no executable behaviour;
/// later rules may interpret approved tags through explicit host code.
/// </summary>
public static class CultureRules
{
    public const int MaximumCultureCount = 128;
    public const int MaximumAssignmentCount = 4_096;

    public static string NormalizeTag(string tag)
    {
        WorldSystemsRules.RequireText(tag, nameof(tag));
        var normalized = tag.Trim().ToLowerInvariant();
        if (normalized.Length > 32 || normalized.Any(character =>
                !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
        {
            throw new ArgumentException("Culture tags must be ASCII lowercase token values.", nameof(tag));
        }

        return normalized;
    }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags, int maxTags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (maxTags <= 0 || maxTags > WorldSystemsConfig.MaximumCultureTags)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTags));
        }

        var normalized = tags
            .Select(NormalizeTag)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length > maxTags)
        {
            throw new ArgumentOutOfRangeException(nameof(tags), "The culture tag set exceeds the world bound.");
        }

        return normalized;
    }

    public static CultureDefinition AddTag(CultureDefinition culture, string tag, int maxTags)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ValidateDefinition(culture, maxTags);
        return culture with { Tags = NormalizeTags(culture.Tags.Append(tag), maxTags) };
    }

    public static CultureAssignment Assign(
        CultureState state,
        string subjectId,
        string cultureId,
        IEnumerable<string>? tags,
        WorldSystemsConfig config)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        Validate(state, config);
        WorldSystemsRules.RequireIdentifier(subjectId, nameof(subjectId));
        WorldSystemsRules.RequireIdentifier(cultureId, nameof(cultureId));
        if (!state.Cultures.Any(culture => string.Equals(culture.Id, cultureId, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Unknown culture '{cultureId}'.", nameof(cultureId));
        }

        return new(
            subjectId,
            cultureId,
            NormalizeTags(tags ?? Array.Empty<string>(), config.MaxCultureTags));
    }

    public static void Validate(CultureState state, WorldSystemsConfig config)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        if (state.Cultures.Count > MaximumCultureCount || state.Assignments.Count > MaximumAssignmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "The bounded culture collections are too large.");
        }

        var cultureIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in state.Cultures)
        {
            ArgumentNullException.ThrowIfNull(culture);
            ValidateDefinition(culture, config.MaxCultureTags);
            if (!cultureIds.Add(culture.Id))
            {
                throw new ArgumentException("Duplicate culture ID.", nameof(state));
            }
        }

        foreach (var culture in state.Cultures)
        {
            if (culture.ParentCultureId is not null && !cultureIds.Contains(culture.ParentCultureId))
            {
                throw new ArgumentException("A culture parent must reference a known culture.", nameof(state));
            }
        }

        var subjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in state.Assignments)
        {
            ArgumentNullException.ThrowIfNull(assignment);
            WorldSystemsRules.RequireIdentifier(assignment.SubjectId, nameof(assignment.SubjectId));
            WorldSystemsRules.RequireIdentifier(assignment.CultureId, nameof(assignment.CultureId));
            var normalizedTags = NormalizeTags(assignment.Tags, config.MaxCultureTags);
            if (!cultureIds.Contains(assignment.CultureId) ||
                !assignment.Tags.SequenceEqual(normalizedTags, StringComparer.Ordinal) ||
                !subjects.Add(assignment.SubjectId))
            {
                throw new ArgumentException("Culture assignments must reference a known culture and unique subject.", nameof(state));
            }
        }
    }

    private static void ValidateDefinition(CultureDefinition culture, int maxTags)
    {
        WorldSystemsRules.RequireIdentifier(culture.Id, nameof(culture.Id));
        WorldSystemsRules.RequireText(culture.Name, nameof(culture.Name));
        var normalizedTags = NormalizeTags(culture.Tags, maxTags);
        if (!culture.Tags.SequenceEqual(normalizedTags, StringComparer.Ordinal))
        {
            throw new ArgumentException("Culture tags must already be normalized and sorted.", nameof(culture));
        }
        if (culture.ParentCultureId is not null)
        {
            WorldSystemsRules.RequireIdentifier(culture.ParentCultureId, nameof(culture.ParentCultureId));
        }
    }
}

public readonly record struct ChunkCoordinate(int X, int Y)
{
    public GridPoint Origin(int chunkSize = ChunkRules.DefaultChunkSize)
    {
        ChunkRules.ValidateChunkSize(chunkSize);
        return new(checked(X * chunkSize), checked(Y * chunkSize));
    }
}

public sealed record ChunkResourceMetadata(
    string ResourceId,
    string Kind,
    GridPoint LocalPosition,
    bool IsRenewable);

public sealed record ChunkManifest(
    ChunkCoordinate Coordinate,
    int ChunkSize,
    int Width,
    int Height,
    string GeneratorId,
    string GeneratorVersion,
    IReadOnlyList<ChunkResourceMetadata> Resources,
    string ManifestDigest = "");

/// <summary>
/// Chunk coordinates and manifests are intentionally metadata-only. The
/// manifest digest excludes itself, following the existing seeded-map pattern.
/// </summary>
public static class ChunkRules
{
    public const int DefaultChunkSize = 16;
    public const int MaximumChunkSize = 64;

    public static ChunkCoordinate ToChunkCoordinate(GridPoint point, int chunkSize = DefaultChunkSize)
    {
        ValidateChunkSize(chunkSize);
        return new(FloorDivide(point.X, chunkSize), FloorDivide(point.Y, chunkSize));
    }

    public static GridPoint ToLocalPoint(GridPoint point, int chunkSize = DefaultChunkSize)
    {
        var coordinate = ToChunkCoordinate(point, chunkSize);
        var origin = coordinate.Origin(chunkSize);
        return new(point.X - origin.X, point.Y - origin.Y);
    }

    public static void Validate(ChunkManifest manifest, int maxResourcesPerChunk = WorldSystemsConfig.MaximumResourcesPerChunk)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateChunkSize(manifest.ChunkSize);
        if (manifest.Width <= 0 || manifest.Width > manifest.ChunkSize ||
            manifest.Height <= 0 || manifest.Height > manifest.ChunkSize ||
            string.IsNullOrWhiteSpace(manifest.GeneratorId) ||
            string.IsNullOrWhiteSpace(manifest.GeneratorVersion) ||
            manifest.Resources.Count > maxResourcesPerChunk ||
            manifest.Resources.Any(resource => resource is null))
        {
            throw new ArgumentOutOfRangeException(nameof(manifest));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in manifest.Resources)
        {
            WorldSystemsRules.RequireIdentifier(resource.ResourceId, nameof(resource.ResourceId));
            WorldSystemsRules.RequireIdentifier(resource.Kind, nameof(resource.Kind));
            if (resource.LocalPosition.X < 0 || resource.LocalPosition.X >= manifest.Width ||
                resource.LocalPosition.Y < 0 || resource.LocalPosition.Y >= manifest.Height ||
                !ids.Add(resource.ResourceId))
            {
                throw new ArgumentException("Chunk resource metadata is outside the chunk or duplicated.", nameof(manifest));
            }
        }
    }

    internal static void ValidateChunkSize(int chunkSize)
    {
        if (chunkSize <= 0 || chunkSize > MaximumChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }
    }

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = Math.DivRem((long)value, divisor, out var remainder);
        if (remainder < 0)
        {
            quotient--;
        }

        return checked((int)quotient);
    }
}

public static class ChunkManifestCodec
{
    private const string Header = "clankerworld.world-chunk-manifest/v1";

    public static byte[] Encode(ChunkManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ChunkRules.Validate(manifest);

        var builder = new StringBuilder();
        builder.Append(Header).Append('\n');
        builder.Append("coordinate=")
            .Append(manifest.Coordinate.X.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(manifest.Coordinate.Y.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("chunk_size=").Append(manifest.ChunkSize.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("dimensions=")
            .Append(manifest.Width.ToString(CultureInfo.InvariantCulture)).Append('x')
            .Append(manifest.Height.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("generator_id=").Append(EncodeText(manifest.GeneratorId)).Append('\n');
        builder.Append("generator_version=").Append(EncodeText(manifest.GeneratorVersion)).Append('\n');
        foreach (var resource in manifest.Resources
                     .OrderBy(item => item.ResourceId, StringComparer.Ordinal)
                     .ThenBy(item => item.LocalPosition.Y)
                     .ThenBy(item => item.LocalPosition.X))
        {
            builder.Append("resource=")
                .Append(EncodeText(resource.ResourceId)).Append('|')
                .Append(EncodeText(resource.Kind)).Append('|')
                .Append(resource.LocalPosition.X.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(resource.LocalPosition.Y.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(resource.IsRenewable ? '1' : '0').Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static string Digest(ChunkManifest manifest) =>
        Convert.ToHexStringLower(SHA256.HashData(Encode(manifest)));

    public static ChunkManifest WithDigest(ChunkManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest with { ManifestDigest = Digest(manifest) };
    }

    private static string EncodeText(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}

public sealed record WorldSystemsState(
    int SchemaVersion,
    string WorldSeed,
    long WorldTick,
    WorldSystemsConfig Config,
    WorldClimate Climate,
    EcologyState Ecology,
    FactionState Factions,
    CurrencyState Currency,
    CultureState Culture,
    IReadOnlyList<ChunkManifest> Chunks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegionalWeatherState? RegionalWeather = null);

/// <summary>
/// Pure composition boundary for a later PrivateWorldRuntime integration.
/// This state contains only serializable data and applies only deterministic,
/// local rules; it does not own transport, providers, mods, or multiplayer.
/// </summary>
public static class WorldSystemsRules
{
    public const int SchemaVersion = 2;

    public static WorldSystemsState CreateGenesis(
        string worldSeed,
        WorldSystemsConfig? config = null,
        IReadOnlyList<EcologyResource>? resources = null,
        FactionState? factions = null,
        CurrencyState? currency = null,
        CultureState? culture = null,
        IReadOnlyList<ChunkManifest>? chunks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        var effectiveConfig = config ?? WorldSystemsConfig.Default;
        effectiveConfig.Validate();
        var effectiveFactions = factions ?? new FactionState([], [], [], []);
        var effectiveCurrency = currency ?? new CurrencyState([], [], []);
        var effectiveCulture = culture ?? new CultureState([], []);
        var state = new WorldSystemsState(
            SchemaVersion,
            worldSeed.Trim(),
            0,
            effectiveConfig,
            WeatherRules.CreateGenesis(worldSeed.Trim(), effectiveConfig),
            new EcologyState(resources ?? []),
            effectiveFactions,
            effectiveCurrency,
            effectiveCulture,
            chunks ?? []);
        Validate(state);
        return state;
    }

    public static WorldSystemsState AdvanceOneTick(WorldSystemsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Validate(state);
        var nextTick = checked(state.WorldTick + 1);
        var calendar = WorldCalendarRules.FromTick(nextTick, state.Config);
        var next = state with
        {
            WorldTick = nextTick,
            Climate = WeatherRules.Advance(state.Climate, nextTick, state.WorldSeed, state.Config),
            RegionalWeather = RegionalWeatherRules.Advance(state, nextTick),
            Ecology = EcologyRules.Advance(state.Ecology, calendar, state.Config),
        };
        Validate(next);
        return next;
    }

    public static void Validate(WorldSystemsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion is < 1 or > SchemaVersion ||
            state.SchemaVersion < 2 && state.RegionalWeather is not null ||
            string.IsNullOrWhiteSpace(state.WorldSeed) || state.WorldTick < 0)
        {
            throw new InvalidDataException("The world-systems schema, seed, or tick is invalid.");
        }

        ArgumentNullException.ThrowIfNull(state.Config);
        state.Config.Validate();
        RegionalWeatherRules.Validate(state.RegionalWeather, state.WorldTick, state.Config);
        ArgumentNullException.ThrowIfNull(state.Climate);
        var calendar = WorldCalendarRules.FromTick(state.WorldTick, state.Config);
        if (state.Climate.WorldTick != state.WorldTick || state.Climate.Season != calendar.Season ||
            !Enum.IsDefined(state.Climate.Weather) ||
            state.Climate.Weather != WeatherRules.WeatherForDay(
                state.WorldSeed,
                calendar.DayIndex,
                calendar.Season,
                state.Config))
        {
            throw new InvalidDataException("The saved climate does not match the saved world tick.");
        }

        EcologyRules.Validate(state.Ecology, state.Config);
        FactionRules.Validate(state.Factions);
        CurrencyRules.Validate(state.Currency);
        CultureRules.Validate(state.Culture, state.Config);
        if (state.Chunks.Count > state.Config.MaxChunkCount)
        {
            throw new InvalidDataException("The saved chunk set exceeds the bounded world limit.");
        }

        var chunkKeys = new HashSet<ChunkCoordinate>();
        foreach (var chunk in state.Chunks)
        {
            ChunkRules.Validate(chunk, state.Config.MaxResourcesPerChunk);
            if (!chunkKeys.Add(chunk.Coordinate) ||
                !string.Equals(chunk.ManifestDigest, ChunkManifestCodec.Digest(chunk), StringComparison.Ordinal))
            {
                throw new InvalidDataException("The saved chunk manifests are duplicated or have invalid digests.");
            }
        }
    }

    internal static void RequireIdentifier(string value, string parameterName)
    {
        RequireText(value, parameterName);
        if (value.Any(character =>
                !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or ':' or '.' or '/')))
        {
            throw new ArgumentException("Identifiers may contain letters, digits, '-', '_', ':', '.', and '/'.", parameterName);
        }
    }

    internal static void RequireText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
    }
}

/// <summary>
/// Compact JSON persistence for the disjoint systems checkpoint. It is a
/// local codec only; it performs no I/O and has no external dependencies.
/// </summary>
public static class WorldSystemsCodec
{
    private const string Header = "clankerworld.world-systems/v1";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static byte[] Encode(WorldSystemsState state)
    {
        WorldSystemsRules.Validate(state);
        return JsonSerializer.SerializeToUtf8Bytes(new Document(Header, state), Options);
    }

    public static WorldSystemsState Decode(ReadOnlyMemory<byte> bytes)
    {
        var document = JsonSerializer.Deserialize<Document>(bytes.Span, Options)
            ?? throw new InvalidDataException("The world-systems checkpoint is empty.");
        if (!string.Equals(document.Format, Header, StringComparison.Ordinal) || document.State is null)
        {
            throw new InvalidDataException("The world-systems checkpoint format is unsupported.");
        }

        WorldSystemsRules.Validate(document.State);
        return document.State;
    }

    private sealed record Document(string Format, WorldSystemsState State);
}
