using ClankerWorld.Simulation.Harness;
using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.Playtest;

public sealed record AnimalTamingWork(string ActorId, int WorkTicks);

public sealed record AnimalPregnancy(string FatherId, int ProgressTicks, long StartedTick);

/// <summary>One physical animal. Ownership, care, offspring places and permissions survive checkpoints.</summary>
public sealed record AnimalState(string Id, string Name, string Species, string Sex, long BornTick,
    GridPoint Position, string HerdId, string? HouseholdId = null, string? YardId = null,
    long CareUntilTick = 0, int ProductProgressTicks = 0, string? ReadyProductLotId = null, string? ReadyProductReservationId = null,
    AnimalPregnancy? Pregnancy = null, long BreedingReadyTick = 0, long? DiedTick = null,
    string? RiderId = null, string? LeaderId = null, string? SaddleLotId = null,
    GridPoint? LeadDestination = null, AnimalTamingWork? TamingWork = null,
    long WildFedUntilTick = 0, long WildWaterUntilTick = 0)
{
    public IReadOnlyList<string> CarePermissions { get; init; } = [];
    public IReadOnlyList<string> RidingPermissions { get; init; } = [];
    public string? SaddleReservationId { get; init; }
}

public sealed record AnimalTradeOffer(string Id, string AnimalId, string SellerId, string BuyerId,
    string ReceivingHouseholdId, string ReceivingYardId, string? PaymentKind, int PaymentQuantity,
    long OfferedTick)
{
    public string? PaymentLotId { get; init; }
}

public sealed record AnimalWorldState(bool Seeded, IReadOnlyList<AnimalState> Animals,
    IReadOnlyList<AnimalTradeOffer> Offers)
{
    public static AnimalWorldState Empty { get; } = new(false, [], []);
    [JsonRequired] public IReadOnlyList<AnimalSupplyTrip> SupplyTrips { get; init; } = [];
}

public sealed record AnimalSupplyTrip(string ActorId, string LotId, string YardId, string? AnimalId = null, string? Action = null);

public sealed record AnimalSpecies(string Id, int DailyFeed, int DailyWater, int GestationDays,
    int AdultDays, int LifespanDays, string? Product, int ProductQuantity, int ProductDays);

/// <summary>Owner-approved trial balance. Only world ticks advance these rules.</summary>
public static class AnimalRules
{
    public const int PopulationCap = 8;
    public const int RecoveryDays = 2;
    public const int RidingCargo = 8;
    public const int TamingWorkTicks = 8;
    public static IReadOnlyList<AnimalSpecies> Species { get; } =
    [
        new("chicken", 1, 1, 2, 3, 30, "eggs", 1, 1),
        new("sheep", 2, 2, 4, 5, 45, "wool", 2, 3),
        new("cow", 2, 2, 6, 7, 60, "milk", 2, 1),
        new("horse", 2, 2, 6, 7, 60, null, 0, 0),
    ];
    public static AnimalSpecies Definition(string species) => Species.Single(item => item.Id == species);
    public static bool IsFeed(string kind) => kind is "grain" or "wild_greens" or "cultivated_greens";
    public static bool IsAdult(AnimalState animal, long tick, int ticksPerDay) =>
        tick - animal.BornTick >= (long)Definition(animal.Species).AdultDays * ticksPerDay;
    public static bool HasCare(AnimalState animal, long tick) => animal.DiedTick is null && tick < animal.CareUntilTick;
    public static bool HasProduct(AnimalState animal) => Definition(animal.Species).Product is not null &&
        (animal.Species == "sheep" || animal.Sex == "female");
}
