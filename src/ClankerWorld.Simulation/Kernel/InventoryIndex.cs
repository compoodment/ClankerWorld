using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace ClankerWorld.Simulation.Kernel;

/// <summary>Derived facts for one committed inventory. A new checkpoint gets a new index.</summary>
public sealed class InventoryIndex
{
    private static readonly ConditionalWeakTable<InventoryCheckpoint, InventoryIndex> Cache = new();
    private readonly FrozenDictionary<string, InventoryLot> byId;
    private readonly FrozenDictionary<string, IReadOnlyList<InventoryLot>> byOwner;
    private readonly FrozenDictionary<string, IReadOnlyList<InventoryLot>> byBuilding;
    private readonly FrozenDictionary<string, IReadOnlyList<InventoryLot>> byDestination;
    private readonly FrozenDictionary<string, IReadOnlyList<InventoryLot>> byContainer;
    private readonly FrozenDictionary<string, int> reserved;
    private readonly FrozenSet<string> reservedFamilies;

    private InventoryIndex(InventoryCheckpoint checkpoint)
    {
        Lots = Array.AsReadOnly(checkpoint.Lots.OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray());
        byId = Lots.ToFrozenDictionary(lot => lot.Id, StringComparer.Ordinal);
        byOwner = Group(Lots, lot => lot.OwnerId);
        byBuilding = Group(Lots, lot => lot.StorageBuildingId);
        byDestination = Group(Lots, lot => lot.DeliveryBuildingId);
        byContainer = Group(Lots, lot => lot.ContainerLotId);
        var active = checkpoint.Reservations.Where(reservation => InventoryRules.IsActive(reservation.State)).ToArray();
        reserved = active.GroupBy(reservation => reservation.LotId, StringComparer.Ordinal)
            .ToFrozenDictionary(group => group.Key, group => group.Sum(reservation => reservation.Quantity), StringComparer.Ordinal);
        reservedFamilies = active.Select(reservation => byId.TryGetValue(reservation.LotId, out var lot)
                ? lot.ContainerLotId ?? lot.Id : reservation.LotId)
            .ToFrozenSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<InventoryLot> Lots { get; }

    public static InventoryIndex For(InventoryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return Cache.GetValue(checkpoint, static inventory => new(inventory));
    }

    public InventoryLot? Find(string id) => byId.GetValueOrDefault(id);
    public InventoryLot Root(InventoryLot lot) => lot.ContainerLotId is { } id ? byId[id] : lot;
    public IReadOnlyList<InventoryLot> OwnedBy(string id) => byOwner.GetValueOrDefault(id) ?? [];
    public IReadOnlyList<InventoryLot> StoredAt(string id) => byBuilding.GetValueOrDefault(id) ?? [];
    public IReadOnlyList<InventoryLot> InboundTo(string id) => byDestination.GetValueOrDefault(id) ?? [];
    public IReadOnlyList<InventoryLot> ContentsOf(string id) => byContainer.GetValueOrDefault(id) ?? [];
    public int ReservedQuantity(string id) => reserved.GetValueOrDefault(id);
    public bool HasActiveReservation(string id) => reserved.ContainsKey(id);
    public bool HasReservedFamily(string rootId) => reservedFamilies.Contains(rootId);
    public int ContentsQuantity(string id) => ContentsOf(id).Sum(lot => lot.Quantity);
    public int FamilyQuantity(string id) => checked((Find(id)?.Quantity ?? 0) + ContentsQuantity(id));
    public int InboundQuantity(string id) => InboundTo(id).Sum(lot => lot.Quantity);

    private static FrozenDictionary<string, IReadOnlyList<InventoryLot>> Group(
        IReadOnlyList<InventoryLot> lots, Func<InventoryLot, string?> key) => lots
        .Where(lot => key(lot) is not null).GroupBy(lot => key(lot)!, StringComparer.Ordinal)
        .ToFrozenDictionary(group => group.Key,
            group => (IReadOnlyList<InventoryLot>)Array.AsReadOnly(group.ToArray()), StringComparer.Ordinal);
}
