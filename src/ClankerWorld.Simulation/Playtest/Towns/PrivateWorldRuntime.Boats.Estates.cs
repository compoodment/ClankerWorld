using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private string? RecordBoatDeath(string actor)
    {
        if (PassengerBoat(actor) is not { } boat) return null;
        var estate = society.Checkpoint.Estates.First(item => item.DeceasedId == actor);
        var roots = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == estate.Id && lot.ContainerLotId is null &&
            lot.GroundPosition is null && lot.StorageBuildingId is null && lot.Quantity > 0)
            .Select(lot => lot.Id).Order(StringComparer.Ordinal).ToArray();
        boat = boat with
        {
            EstateCargoLotIds = (boat.EstateCargoLotIds ?? []).Concat(roots).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray()
        };
        SetBoat(boat);
        ApplyInventoryTransition(inventory => inventory with
        {
            Lots = inventory.Lots.Select(lot => roots.Contains(lot.Id, StringComparer.Ordinal) ||
                lot.ContainerLotId is { } vessel && roots.Contains(vessel, StringComparer.Ordinal)
                ? lot with { GroundPosition = new(boat.Position.X, boat.Position.Y) } : lot).ToArray(),
        });
        AppendEvent("boat_estate_retained", $"{actor}:{boat.Id}:{estate.Id}");
        return boat.Id;
    }

    private void MoveBoatEstateCargo(BoatState boat)
    {
        if (boat.EstateCargoLotIds is not { Count: > 0 } roots) return;
        var remaining = society.Checkpoint.Inventory.Lots.Where(lot => lot.ContainerLotId is null && lot.Quantity > 0 &&
            (roots.Contains(lot.Id, StringComparer.Ordinal) || lot.ProvenanceLotId is { } source && roots.Contains(source, StringComparer.Ordinal)) &&
            lot.GroundPosition is not null).Select(lot => lot.Id).Order(StringComparer.Ordinal).ToArray();
        SetBoat(boat with { EstateCargoLotIds = remaining.Length == 0 ? null : remaining });
        if (remaining.Length == 0) return;
        ApplyInventoryTransition(inventory => inventory with
        {
            Lots = inventory.Lots.Select(lot => remaining.Contains(lot.Id, StringComparer.Ordinal) ||
                lot.ContainerLotId is { } vessel && remaining.Contains(vessel, StringComparer.Ordinal)
                ? lot with { GroundPosition = new(boat.Position.X, boat.Position.Y) } : lot).ToArray(),
        });
    }

    private static bool IsBoatEstateCargo(BoatTransportState? transport, InventoryLot lot) =>
        transport?.Boats.Any(boat => (boat.EstateCargoLotIds?.Contains(lot.Id, StringComparer.Ordinal) == true ||
            lot.ContainerLotId is { } vessel && boat.EstateCargoLotIds?.Contains(vessel, StringComparer.Ordinal) == true) &&
            lot.GroundPosition == new InventoryGroundPosition(boat.Position.X, boat.Position.Y)) == true;
}
