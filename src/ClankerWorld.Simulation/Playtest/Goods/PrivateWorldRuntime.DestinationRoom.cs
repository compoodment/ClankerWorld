using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    /// <summary>Free physical storage after production, trade and inbound delivery promises.</summary>
    public int DestinationRoom(string buildingId, InventoryLot? movingRoot = null) =>
        DestinationRoom(society.Checkpoint.Inventory, buildingId, movingRoot);

    private int DestinationRoom(InventoryCheckpoint inventory, string buildingId, InventoryLot? movingRoot = null,
        int outgoingQuantity = 0, int releasedBusinessReservation = 0)
    {
        var building = worldSimulation.Buildings.Single(item => item.InstanceId == buildingId);
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        if (BuildingStorageRules.Capacity(definition, building) is not { } capacity) return int.MaxValue;
        var index = InventoryIndex.For(inventory);
        // Resolve the current family; caller-supplied location or quantities never release space.
        var moving = movingRoot is not null && index.Find(movingRoot.Id) is { } current ? index.Root(current) : null;
        // Stock-supply trips retain their destination outside the inventory delivery marker.
        // Care and saddling trips consume or equip their inputs rather than storing them.
        var yardInbound = animalWorld.SupplyTrips.Where(trip => trip.YardId == buildingId && trip.Action is null)
            .Select(trip => index.Find(trip.LotId)).OfType<InventoryLot>()
            .Where(root => root.StorageBuildingId != buildingId)
            .SelectMany(root => new[] { root }.Concat(index.ContentsOf(root.Id)));
        var inbound = index.InboundTo(buildingId).Concat(yardInbound).DistinctBy(lot => lot.Id)
            .Where(lot => moving is null || lot.Id != moving.Id && lot.ContainerLotId != moving.Id)
            .Sum(lot => (long)lot.Quantity);
        var used = (long)StoredQuantity(buildingId, inventory) + ReservedStorageGrowth(buildingId, inventory) +
            ReservedBusinessStorageSpace(buildingId, inventory) + inbound;
        return (int)Math.Clamp(capacity - used + outgoingQuantity + releasedBusinessReservation, 0, int.MaxValue);
    }
}
