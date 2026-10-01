using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record FarmStockChoice(InventoryLot Carrier, InventoryLot Resource);

    private sealed record FarmStockHaulPlan(
        InventoryLot Carrier,
        InventoryLot Resource,
        int TransferQuantity,
        int ResourceQuantity,
        bool MoveContainerFamily);

    private PlacedBuilding? FarmhouseForHousehold(string householdId) =>
        HouseholdBuildingWithTag(householdId, "farmhouse");

    private FarmStockChoice? FarmGrainForDelivery(string householdId, string farmhouseId) =>
        FarmStockForDelivery(householdId, "grain", lot => lot.StorageBuildingId != farmhouseId);

    private FarmStockChoice? FarmFlourForHouse(string householdId, string farmhouseId) =>
        FarmStockForDelivery(householdId, "flour", lot => lot.StorageBuildingId == farmhouseId);

    private FarmStockChoice? FarmStockForDelivery(
        string householdId,
        string itemKind,
        Func<InventoryLot, bool> locationMatches)
    {
        var inventory = society.Checkpoint.Inventory;
        foreach (var carrier in inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                     lot.ContainerLotId is null && lot.DeliveryBuildingId is null && locationMatches(lot))
                     .OrderBy(lot => lot.Id, StringComparer.Ordinal))
        {
            if (carrier.ItemKind == itemKind && AvailableLotQuantity(carrier) > 0)
                return new FarmStockChoice(carrier, carrier);

            if (!InventoryContainerRules.IsContainer(carrier.ItemKind) ||
                HasActiveContainerReservation(inventory, carrier.Id))
                continue;
            var resource = inventory.Lots.Where(lot => lot.ContainerLotId == carrier.Id &&
                    lot.ItemKind == itemKind && AvailableLotQuantity(lot) > 0)
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
            if (resource is not null)
                return new FarmStockChoice(carrier, resource);
        }
        return null;
    }

    private FarmStockHaulPlan? PlanFarmStockHaul(string actor, string destinationId, FarmStockChoice choice)
    {
        var inventory = society.Checkpoint.Inventory;
        var capacity = Math.Min(HouseHaulLoadQuantity,
            Math.Min(FreeCarryCapacity(actor), RemainingDeliveryRoom(inventory, destinationId)));
        if (capacity <= 0)
            return null;

        if (!InventoryContainerRules.IsContainer(choice.Carrier.ItemKind))
        {
            var quantity = Math.Min(capacity, AvailableLotQuantity(choice.Resource));
            return quantity > 0
                ? new FarmStockHaulPlan(choice.Carrier, choice.Resource, quantity, quantity, MoveContainerFamily: false)
                : null;
        }

        if (HasActiveContainerReservation(inventory, choice.Carrier.Id))
            return null;
        var familyQuantity = ContainerFamilyQuantity(inventory, choice.Carrier.Id);
        if (familyQuantity <= capacity)
            return new FarmStockHaulPlan(choice.Carrier, choice.Resource, 1,
                choice.Resource.Quantity, MoveContainerFamily: true);

        // A too-large family stays together. Explicitly take only the allowed
        // resource portion so the vessel itself remains at its current site.
        if (choice.Resource.ContainerLotId != choice.Carrier.Id || choice.Carrier.ConditionBasisPoints == 0)
            return null;
        var takenQuantity = Math.Min(capacity, AvailableLotQuantity(choice.Resource));
        return takenQuantity > 0
            ? new FarmStockHaulPlan(choice.Carrier, choice.Resource, takenQuantity, takenQuantity,
                MoveContainerFamily: false)
            : null;
    }

    private int RemainingDeliveryRoom(InventoryCheckpoint inventory, string buildingId) =>
        Math.Max(0, StorageRoom(buildingId) - InboundDeliveryQuantity(inventory, buildingId));

    private void AddFarmGrainCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarriedHouseDelivery(actor) is not null ||
            FarmhouseForHousehold(householdId) is not { } farmhouse ||
            FarmGrainForDelivery(householdId, farmhouse.InstanceId) is not { } grain ||
            PlanFarmStockHaul(actor, farmhouse.InstanceId, grain) is null)
            return;
        var source = HouseholdStockPosition(grain.Carrier);
        var range = HouseholdStockInteractionRange(grain.Carrier);
        if ((!IsWithinInteractionRange(state.Position, source, range) &&
             FindUnoccupiedRoute(actor, state.Position, source, range).Count == 0) ||
            FindUnoccupiedRoute(actor, source, farmhouse.Position, 0).Count == 0)
            return;
        candidates.Add(new CognitionCandidate("haul_farm_grain",
            "Carry household grain to its Farmhouse for on-site processing.", 24, farmhouse.InstanceId));
    }

    private void HaulFarmGrain(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            FarmhouseForHousehold(householdId) is not { } farmhouse ||
            FarmGrainForDelivery(householdId, farmhouse.InstanceId) is not { } grain)
            return;
        var source = HouseholdStockPosition(grain.Carrier);
        var range = HouseholdStockInteractionRange(grain.Carrier);
        if (!IsWithinInteractionRange(state.Position, source, range))
        {
            MoveToward(actor, state, source, "farm_grain", range);
            return;
        }
        if (PlanFarmStockHaul(actor, farmhouse.InstanceId, grain) is not { } plan)
            return;
        ApplyFarmStockHaul(actor, householdId, farmhouse.InstanceId, plan,
            $"farm-grain-pickup:{WorldTick}:{actor}", "farm_grain_picked_up");
    }

    private void AddFarmFlourCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarriedHouseDelivery(actor) is not null ||
            FarmhouseForHousehold(householdId) is not { } farmhouse ||
            HouseForHousehold(householdId) is not { } house ||
            FarmFlourForHouse(householdId, farmhouse.InstanceId) is not { } flour ||
            PlanFarmStockHaul(actor, house.InstanceId, flour) is null)
            return;
        if ((!IsWithinInteractionRange(state.Position, farmhouse.Position, 0) &&
             FindUnoccupiedRoute(actor, state.Position, farmhouse.Position, 0).Count == 0) ||
            FindUnoccupiedRoute(actor, farmhouse.Position, house.Position, 0).Count == 0)
            return;
        candidates.Add(new CognitionCandidate("haul_farm_flour",
            "Carry household flour from its Farmhouse to its House.", 25, house.InstanceId));
    }

    private void HaulFarmFlour(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarriedHouseDelivery(actor) is not null ||
            FarmhouseForHousehold(householdId) is not { } farmhouse ||
            HouseForHousehold(householdId) is not { } house ||
            FarmFlourForHouse(householdId, farmhouse.InstanceId) is not { } flour)
            return;
        if (state.Position != farmhouse.Position)
        {
            MoveToward(actor, state, farmhouse.Position, "farm_flour", 0);
            return;
        }
        if (PlanFarmStockHaul(actor, house.InstanceId, flour) is not { } plan)
            return;
        ApplyFarmStockHaul(actor, householdId, house.InstanceId, plan,
            $"farm-flour-pickup:{WorldTick}:{actor}", "farm_flour_picked_up");
    }

    private void ApplyFarmStockHaul(string actor, string householdId, string destinationId,
        FarmStockHaulPlan plan, string operationId, string purpose)
    {
        ApplyInventoryTransition(inventory => plan.MoveContainerFamily
            ? InventoryFixture.Transfer(inventory, operationId, householdId, actor,
                plan.Carrier.Id, plan.TransferQuantity, purpose, destinationDeliveryBuildingId: destinationId)
            : plan.Resource.ContainerLotId == plan.Carrier.Id
                ? InventoryFixture.TakeFromContainer(inventory, operationId, householdId, actor,
                    plan.Carrier.Id, plan.Resource.Id, plan.TransferQuantity,
                    destinationDeliveryBuildingId: destinationId)
                : InventoryFixture.Transfer(inventory, operationId, householdId, actor,
                    plan.Resource.Id, plan.TransferQuantity, purpose, destinationDeliveryBuildingId: destinationId));
        AppendEvent(purpose, $"{actor}:{plan.Resource.Id}:{plan.ResourceQuantity}:{destinationId}");
    }
}
