using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public enum FarmFieldStage { Preparing, Prepared, Planted, Growing, Ready, Harvested }
public enum FarmWorkKind { Till, Plant, Tend, Harvest }

public sealed record FarmFieldWork(string WorkerId, FarmWorkKind Kind, int RemainingTicks,
    long LastWorkedTick, string? SeedReservationId = null, string? Crop = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HoeLotId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SickleLotId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OrderInstructionId = null);

public sealed record FarmFieldState(GridPoint Position, string HouseholdId, FarmFieldStage Stage,
    string? Crop = null, long PlantedTick = 0, long ReadyTick = 0, bool Tended = false, int Cycle = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FarmFieldWork? Work = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReplantingReservationId = null,
    long LastWorkedTick = 0);

public sealed record FarmWorkResult(bool Accepted, string Message);

/// <summary>Provisional crop quantities and physical work times, kept together for playtesting.</summary>
public static class FarmFieldRules
{
    public const string Hoe = "wooden_hoe";
    public const string Grain = "grain";
    public const string Potatoes = "potatoes";
    public const string Greens = "cultivated_greens";
    public const string GrainSeed = "grain_seed";
    public const string GreensSeed = "cultivated_green_seed";
    public const string OrchardSeed = "orchard_seed";
    public const int FarmStorageCapacity = 96;
    public const int MealsPerPersonPerDay = 2;

    public static bool IsCrop(string? crop) => crop is Grain or Potatoes or Greens;
    public static string PlantingItem(string crop) => crop switch
    {
        Grain => GrainSeed,
        Potatoes => Potatoes,
        Greens => GreensSeed,
        _ => throw new ArgumentOutOfRangeException(nameof(crop)),
    };
    public static bool IsFarmStock(string item) => item is Grain or Potatoes or GrainSeed or GreensSeed or OrchardSeed or "flour";
    public static int WorkTicks(FarmWorkKind kind) => kind switch
    {
        FarmWorkKind.Till => 8,
        FarmWorkKind.Tend => 5,
        _ => 4,
    };
    public static int HarvestQuantity(string crop, int fertility) =>
        (crop == Potatoes ? 5 : 4) + fertility / 25;
    public static long GrowthTicks(int ticksPerDay, int fertility) => Math.Max(12, ticksPerDay * (150L - fertility) / 200);
    public static string FieldId(GridPoint point) => $"field-{point.X}-{point.Y}";

    /// <summary>
    /// A field nobody works for a full season, a quarter of the world's year,
    /// goes back to grass (agreed October 8, #1248). The land keeps its fertility.
    /// </summary>
    public static long IdleTicksBeforeGrass(WorldSystemsConfig config) => (long)config.TicksPerDay * config.DaysPerYear / 4;
}
