using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public enum LivestockKind { Chicken, Cow, Sheep, Horse }

public sealed record HouseholdAnimal(string Id, LivestockKind Kind, string HouseholdId, GridPoint Position,
    string AcquisitionId, long AcquiredTick, long FedUntilTick, long WateredUntilTick, long CaredUntilTick,
    long LastProductTick, int PendingProductQuantity = 0, string? RiderId = null,
    IReadOnlyList<string>? PermittedRiderIds = null, long? NaturalDeathTick = null, bool HideCollected = false);

public sealed record LivestockActionResult(bool Applied, string? Failure = null);

/// <summary>Trial husbandry balance. Breeding, acquisition and mortality schedules are not defined here.</summary>
public static class LivestockRules
{
    public const int CareTicks = 96;
    public const int ProductTicks = 48;
    public const int HorseCargoCapacity = 64;

    public static bool HasCare(HouseholdAnimal animal, long tick) => animal.NaturalDeathTick is null &&
        animal.FedUntilTick > tick && animal.WateredUntilTick > tick && animal.CaredUntilTick > tick;

    public static string? Product(LivestockKind kind) => kind switch
    {
        LivestockKind.Chicken => "eggs",
        LivestockKind.Cow => "milk",
        LivestockKind.Sheep => "wool",
        _ => null,
    };

    public static int ProductQuantity(LivestockKind kind) => kind == LivestockKind.Chicken ? 1 : 2;

    // Only an authoritative natural-death event calls this transition. Missed care never calls it.
    public static HouseholdAnimal RecordNaturalDeath(HouseholdAnimal animal, long tick)
    {
        if (tick < animal.AcquiredTick || animal.NaturalDeathTick is not null)
            throw new InvalidOperationException("A living animal can record one natural death after acquisition.");
        return animal with { NaturalDeathTick = tick, RiderId = null, PendingProductQuantity = 0 };
    }
}
