using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string TownSalvagePrefix = "town_salvage:";

    private bool MayResettleTown(string actor, TownRuntimeState town) =>
        town.IsAbandoned && AdultResident(actor) && ReadyForBriefInteraction(actor) &&
        town.BorderTiles.Contains(inhabitants[actor].Position);

    private string ResettlementText(string actor, TownRuntimeState town)
    {
        var previous = TownForResident(actor);
        var leaving = previous is not null ? $" You would leave {TownName(previous)}." : string.Empty;
        var care = TownCareGroup(actor, previous).Length > 1 ? " Your dependent children join with you without moving their bodies or changing care." : string.Empty;
        return $"Explicitly resettle abandoned {town.Name} where you stand and become its first resident. Its council restarts from living adult residents; existing laws remain.{leaving}{care} This gives no House, private property or household membership.";
    }

    private void ResettleTown(string actor, TownRuntimeState town)
    {
        // Recheck immediately before committing: another accepted choice may already have revived it.
        town = towns.Single(item => item.Id == town.Id);
        if (!MayResettleTown(actor, town)) return;
        var previous = TownForResident(actor);
        var group = TownCareGroup(actor, previous);
        var moving = group.ToHashSet(StringComparer.Ordinal);
        foreach (var other in towns.Where(item => item.Id != town.Id && item.ResidentIds.Any(moving.Contains)).ToArray())
            SetTown(other with { ResidentIds = other.ResidentIds.Where(id => !moving.Contains(id)).ToArray() });
        SetTown(town with { ResidentIds = group });
        AdvanceTownGovernance();
        SettleTownAdmissions();
        AppendEvent("town_resettled", $"{town.Id}|{actor}|{previous ?? "none"}|{group.Length}", inhabitants[actor].Position);
    }

    private static string TownSalvageId(string townId, string lotId) => TownSalvagePrefix +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(townId + "|" + lotId)));

    private IEnumerable<(TownRuntimeState Town, InventoryLot Lot, int Quantity)> AbandonedStockForPickup(string actor)
    {
        if (!inhabitants.TryGetValue(actor, out var person) || !ReadyForBriefInteraction(actor) ||
            society.Checkpoint.GetInhabitant(actor).AgeBand == SocietyAgeBand.Infant)
            yield break;
        var abandoned = towns.Where(town => town.IsAbandoned).ToDictionary(town => town.Id, StringComparer.Ordinal);
        if (abandoned.Count == 0) yield break;
        var room = FreeCarryCapacity(actor);
        foreach (var lot in society.Checkpoint.Inventory.Lots.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!abandoned.TryGetValue(lot.OwnerId, out var town) || lot.CarrierId is not null ||
                lot.ContainerLotId is not null || lot.DeliveryBuildingId is not null || PhysicalUnreservedQuantity(lot) <= 0)
                continue;
            // Town ownership never opens a private building or supplies a location for missing goods.
            if (lot.GroundPosition is null && (lot.StorageBuildingId is not { } storage ||
                !WarehousesForTown(town.Id).Any(building => building.InstanceId == storage && building.HouseholdId is null)))
                continue;
            var range = lot.GroundPosition is not null ? ResourceInteractionRange : 0;
            if (!IsWithinInteractionRange(person.Position, HouseholdStockPosition(lot), range)) continue;
            var cart = lot.ItemKind == InventoryContainerRules.Handcart;
            var quantity = InventoryContainerRules.IsContainer(lot.ItemKind)
                ? !HasActiveContainerReservation(society.Checkpoint.Inventory, lot.Id) && (cart || VesselFits(lot, room)) ? 1 : 0
                : Math.Min(PhysicalUnreservedQuantity(lot), room);
            if (quantity > 0) yield return (town, lot, quantity);
        }
    }

    private void AddTownSalvageCandidates(List<CognitionCandidate> candidates, string actor)
    {
        foreach (var (town, lot, quantity) in AbandonedStockForPickup(actor))
            candidates.Add(new(TownSalvageId(town.Id, lot.Id),
                $"Salvage {quantity} {lot.ItemKind.Replace('_', ' ')} from abandoned {town.Name} here. Only unreserved communal goods move; this grants no Town membership or private access." +
                (lot.ItemKind == InventoryContainerRules.Handcart ? " The handcart and its cargo stay on the ground until you pull it." : string.Empty), 120));
    }

    private void SalvageTownStock(string actor, string candidate)
    {
        // Exact physical stock, current abandonment, reservations and capacity all matter at pickup.
        foreach (var (town, lot, quantity) in AbandonedStockForPickup(actor))
        {
            if (TownSalvageId(town.Id, lot.Id) != candidate) continue;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"salvage:{WorldTick}:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(actor + "|" + candidate))),
                town.Id, actor, lot.Id, quantity,
                "town_stock_salvaged"));
            AppendEvent("town_stock_salvaged", $"{town.Id}|{actor}|{lot.ItemKind}|{quantity}", inhabitants[actor].Position);
            return;
        }
    }
}
