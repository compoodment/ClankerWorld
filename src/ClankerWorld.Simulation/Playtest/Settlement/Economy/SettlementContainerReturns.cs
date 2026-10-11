using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Returns empty reusable household vessels from held workplaces to the House.</summary>
public sealed partial class PrivateWorldRuntime
{
    private const string ReturnEmptyVesselPrefix = "return_empty_vessel:";

    private InventoryLot? EmptyVesselToReturn(string actor, PlaytestInhabitantState person,
        string? selectedLotId = null)
    {
        if (!AdultResident(actor) || CarriedHouseDelivery(actor) is not null || FreeCarryCapacity(actor) < 1 ||
            society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            HouseForHousehold(householdId) is not { } house ||
            DestinationRoom(house.InstanceId) < 1)
            return null;

        var inventory = society.Checkpoint.Inventory;
        foreach (var vessel in inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                     (selectedLotId is null || lot.Id == selectedLotId) &&
                     InventoryContainerRules.IsContainer(lot.ItemKind) && lot.ConditionBasisPoints > 0 &&
                     lot.ContainerLotId is null && lot.GroundPosition is null && lot.DeliveryBuildingId is null &&
                     lot.StorageBuildingId is not null && lot.StorageBuildingId != house.InstanceId &&
                     ContainerContentsQuantity(inventory, lot.Id) == 0 && !HasActiveContainerReservation(inventory, lot.Id))
                     .OrderBy(lot => lot.Id, StringComparer.Ordinal))
        {
            // A household knows its own held workplace; foreign or unclaimed buildings grant no access.
            var source = worldSimulation.Buildings.SingleOrDefault(building =>
                building.InstanceId == vessel.StorageBuildingId && building.HouseholdId == householdId &&
                worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                    HouseholdBuildingKind(definition) is not null));
            if (source is not null &&
                (person.Position == source.Position || FindUnoccupiedRoute(actor, person.Position, source.Position, 0).Count > 0) &&
                PickupCarryCapacity(actor, vessel, house.Position) >= ContainerFamilyQuantity(inventory, vessel.Id))
                return vessel;
        }
        return null;
    }

    private void AddEmptyVesselReturnCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState person)
    {
        if (EmptyVesselToReturn(actor, person) is not { } vessel) return;
        candidates.Add(new(ReturnEmptyVesselPrefix + vessel.Id,
            "Bring an empty household vessel back to the House for reuse.", 22, vessel.StorageBuildingId));
    }

    private void ReturnEmptyVessel(string actor, PlaytestInhabitantState person, string lotId)
    {
        if (EmptyVesselToReturn(actor, person, lotId) is not { } vessel) return;
        var source = worldSimulation.Buildings.Single(building => building.InstanceId == vessel.StorageBuildingId);
        if (person.Position != source.Position)
        {
            MoveToward(actor, person, source.Position, "return_empty_vessel", 0);
            return;
        }
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId!;
        var house = HouseForHousehold(householdId)!;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"empty-vessel-pickup:{WorldTick}:{actor}", householdId, actor, vessel.Id, 1,
            "empty_vessel_picked_up", destinationDeliveryBuildingId: house.InstanceId));
        AppendEvent("empty_vessel_picked_up", $"{actor}:{vessel.Id}:{source.InstanceId}:{house.InstanceId}");
    }
}
