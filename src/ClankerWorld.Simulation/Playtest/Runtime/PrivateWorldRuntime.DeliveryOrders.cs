using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record DeliveryOrderPlan(string Route, PlacedBuilding Destination, string DestinationOwnerId,
        InventoryLot Carrier, InventoryLot Resource, int TransferQuantity, int ResourceQuantity,
        bool TakeFromContainer = false, bool DirectDelivery = false);

    private sealed record DeliveryNativeEffect(string OperationId, string LotId, int Quantity, bool Delivered);

    private int DeliveryResourceQuantity(InventoryLot carrier, string kind) =>
        (carrier.ItemKind == kind ? carrier.Quantity : 0) +
        (InventoryContainerRules.IsContainer(carrier.ItemKind)
            ? society.Checkpoint.Inventory.Lots.Where(lot => lot.ContainerLotId == carrier.Id && lot.ItemKind == kind)
                .Sum(lot => lot.Quantity) : 0);

    private bool DeliveryDestinationMatches(OwnerInstructionOrder order, PlacedBuilding building) =>
        worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
            definition.Tags.Contains(order.TargetBuildingKind!, StringComparer.Ordinal)) &&
        (order.TargetPosition is null || building.Position == order.TargetPosition) &&
        (order.TargetStorageBuildingId is null || building.InstanceId == order.TargetStorageBuildingId &&
            building.Position == order.TargetStoragePosition &&
            (order.DeliveryPurpose == "town_surplus" ? building.TownId : building.HouseholdId) == order.TargetStorageOwnerId);

    private static int DeliveryOrderRemaining(OwnerInstructionOrder order) =>
        order.QuantityIsExplicit && !order.RepeatUntilCancelled ? order.RequestedUnits - order.CompletedUnits : int.MaxValue;

    private bool DeliveryCanReach(string actor, GridPoint from, GridPoint destination, int range = 0) =>
        IsWithinInteractionRange(from, destination, range) || FindUnoccupiedRoute(actor, from, destination, range).Count > 0;

    private DeliveryOrderPlan? HouseholdDeliveryOrderPlan(string actor, PlaytestInhabitantState person,
        OwnerInstructionOrder order, int maximumQuantity)
    {
        if (HouseholdFor(actor) is not { } householdId) return null;
        if (order.DeliveryPurpose == "household_food")
        {
            if (person.HungerBasisPoints < 6_000 || HouseForHousehold(householdId) is not { } foodHouse ||
                !DeliveryDestinationMatches(order, foodHouse) || PersonalSpareFood(actor, order.TargetItemKind, order.DeliveryLotId) is not { } food)
                return null;
            var quantity = Math.Min(maximumQuantity, Math.Min(StorageRoomAfterInboundDeliveries(foodHouse.InstanceId),
                Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(food) - 1)));
            return quantity > 0 ? new("household_food", foodHouse, householdId, food, food, quantity, quantity,
                DirectDelivery: true) : null;
        }
        if (order.TargetBuildingKind is "farmhouse" or "silo")
        {
            if (FarmhouseForHousehold(householdId) is null ||
                HouseholdBuildingWithTag(householdId, order.TargetBuildingKind!) is not { } requestedFarm ||
                FarmStorageFor(householdId, order.TargetItemKind!, requestedFarm.InstanceId) is not { } destination ||
                !DeliveryDestinationMatches(order, destination) ||
                FarmGrainForDelivery(householdId, actor, order.TargetItemKind, destination.InstanceId, maximumQuantity) is not { } choice ||
                PlanFarmStockHaul(actor, destination.InstanceId, choice, maximumQuantity) is not { } plan) return null;
            return new("farm_stock", destination, householdId, plan.Carrier, plan.Resource,
                plan.TransferQuantity, plan.ResourceQuantity,
                TakeFromContainer: !plan.MoveContainerFamily && plan.Resource.ContainerLotId == plan.Carrier.Id);
        }
        if (HouseForHousehold(householdId) is not { } house || !DeliveryDestinationMatches(order, house)) return null;
        if (order.TargetItemKind == "flour" && FarmhouseForHousehold(householdId) is { } farmhouse &&
            FarmFlourForHouse(householdId, farmhouse.InstanceId) is { } flour &&
            PlanFarmStockHaul(actor, house.InstanceId, flour, maximumQuantity) is { } flourPlan &&
            order.DeliveryRoute is null or "farm_flour")
            return new("farm_flour", house, householdId, flourPlan.Carrier, flourPlan.Resource,
                flourPlan.TransferQuantity, flourPlan.ResourceQuantity,
                TakeFromContainer: !flourPlan.MoveContainerFamily && flourPlan.Resource.ContainerLotId == flourPlan.Carrier.Id);
        if (UnlocatedHouseholdStock(householdId, actor, house.InstanceId, order.TargetItemKind, maximumQuantity) is not { } stock)
            return null;
        var moved = Math.Min(maximumQuantity, HouseHaulPickupQuantity(actor, stock, house.InstanceId));
        var resourcesMoved = InventoryContainerRules.IsContainer(stock.ItemKind)
            ? DeliveryResourceQuantity(stock, order.TargetItemKind!) : moved;
        return moved > 0 ? new("house_stock", house, householdId, stock, stock, moved, resourcesMoved) : null;
    }

    private string? DeliveryRouteForCarried(OwnerInstructionOrder order, PlacedBuilding destination) => order.DeliveryPurpose switch
    {
        "household_stock" when order.TargetBuildingKind is "farmhouse" or "silo" && FarmFieldRules.IsFarmStock(order.TargetItemKind!) => "farm_stock",
        "household_stock" when order.TargetBuildingKind == "house" => order.DeliveryRoute ?? "house_stock",
        "workstation_input" when order.TargetBuildingKind == "blacksmith" &&
            BlacksmithInputTarget(destination.InstanceId, order.TargetItemKind!) > 0 => order.DeliveryRoute ?? "blacksmith_input",
        "workstation_input" when worldContent.Recipes.Any(recipe => recipe.WorkstationBuildingId == destination.DefinitionId &&
            recipe.Inputs.Any(input => input.ResourceId == order.TargetItemKind)) => "workstation_input",
        "store_stock" when BusinessRules.MaySell("store", order.TargetItemKind!) => "store_stock",
        _ => null,
    };

    private DeliveryOrderPlan? CarriedDeliveryOrderPlan(string actor, OwnerInstructionOrder order)
    {
        var inventory = society.Checkpoint.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) &&
                     lot.ContainerLotId is null && lot.DeliveryBuildingId is not null &&
                     (order.DeliveryLotId is null || lot.Id == order.DeliveryLotId))
                     .OrderBy(lot => lot.Id, StringComparer.Ordinal))
        {
            if (worldSimulation.Buildings.SingleOrDefault(building => building.InstanceId == lot.DeliveryBuildingId) is not { } destination ||
                destination.HouseholdId != HouseholdFor(actor) || !DeliveryDestinationMatches(order, destination) ||
                DeliveryRouteForCarried(order, destination) is not { } route ||
                order.DeliveryRoute is not null && order.DeliveryRoute != route ||
                PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id)) continue;
            var vessel = InventoryContainerRules.IsContainer(lot.ItemKind);
            if (vessel && HasActiveContainerReservation(inventory, lot.Id)) continue;
            var available = vessel ? DeliveryResourceQuantity(lot, order.TargetItemKind!)
                : lot.ItemKind == order.TargetItemKind ? AvailableLotQuantity(lot) : 0;
            var quantity = order.DeliveryQuantity ?? Math.Min(available, DeliveryOrderRemaining(order));
            if (quantity <= 0 || quantity > available || quantity > DeliveryOrderRemaining(order) ||
                vessel && quantity != available) continue;
            var physicalQuantity = vessel ? HouseDeliveryPhysicalQuantity(inventory, lot) : quantity;
            if (physicalQuantity > HouseDeliveryRoom(lot)) continue;
            return new(route, destination, destination.HouseholdId!, lot, lot, vessel ? 1 : quantity, quantity, DirectDelivery: true);
        }
        return null;
    }

    private DeliveryOrderPlan? DeliveryPlanFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var order = instruction.Order!;
        if (!AdultResident(actor) || !ReadyForBriefInteraction(actor) || DeliveryOrderRemaining(order) <= 0) return null;
        var boundLot = order.DeliveryLotId is { } boundId
            ? society.Checkpoint.Inventory.Lots.SingleOrDefault(lot => lot.Id == boundId) : null;
        if (order.DeliveryLotId is not null && (boundLot is null || boundLot.OwnerId != actor ||
            !PersonalEquipmentRules.IsCarried(boundLot, actor))) return null;
        if (boundLot?.DeliveryBuildingId is not null)
            return CarriedDeliveryOrderPlan(actor, order) is { } boundShipment &&
                DeliveryCanReach(actor, person.Position, boundShipment.Destination.Position) ? boundShipment : null;
        var otherPromise = order.DeliveryLotId is null && CarriedHouseDelivery(actor) is not null;
        if (otherPromise && CarriedDeliveryOrderPlan(actor, order) is { } carried)
            return DeliveryCanReach(actor, person.Position, carried.Destination.Position) ? carried : null;
        var maximum = Math.Min(DeliveryOrderRemaining(order), order.DeliveryQuantity ?? int.MaxValue);
        var plan = order.DeliveryPurpose switch
        {
            "household_stock" or "household_food" => HouseholdDeliveryOrderPlan(actor, person, order, maximum),
            "workstation_input" => GetBlacksmithOrderPlan(actor, person, order, maximum) ??
                GetWorkstationOrderPlan(actor, person, order, maximum),
            "town_surplus" => GetTownOrderPlan(actor, person, order, maximum),
            "store_stock" => GetStoreOrderPlan(actor, person, order, maximum),
            _ => null,
        };
        if (plan is null || otherPromise && !plan.DirectDelivery ||
            order.DeliveryRoute is not null && order.DeliveryRoute != plan.Route ||
            order.DeliveryLotId is not null && (plan.Carrier.Id != order.DeliveryLotId ||
                plan.ResourceQuantity != order.DeliveryQuantity)) return null;
        var source = plan.DirectDelivery ? person.Position : HouseholdStockPosition(plan.Carrier);
        var range = plan.DirectDelivery ? 0 : HouseholdStockInteractionRange(plan.Carrier);
        return DeliveryCanReach(actor, person.Position, source, range) && DeliveryCanReach(actor, source, plan.Destination.Position)
            ? plan : null;
    }

    private (PlacedBuilding Building, string Route, MapResource Source)? DeliveryGatherFor(
        OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var order = instruction.Order!;
        var actor = instruction.TargetInhabitantId;
        if (order.DeliveryPurpose != "workstation_input" || order.DeliveryLotId is not null ||
            CarriedHouseDelivery(actor) is not null || !AdultResident(actor) || !ReadyForBriefInteraction(actor)) return null;
        if (order.DeliveryRoute is null or "blacksmith_input" && HouseholdFor(actor) is { } householdId &&
            BlacksmithForHousehold(householdId) is { } smith && DeliveryDestinationMatches(order, smith) &&
            BlacksmithInputToGather(householdId, smith.InstanceId, actor, order.TargetItemKind) is { } missing &&
            FreeCarryCapacity(actor) >= ProjectMaterialCarryUnits(actor, missing.ItemKind, missing.Source) &&
            DeliveryCanReach(actor, person.Position, missing.Source.Position, ResourceInteractionRange))
            return (smith, "blacksmith_input", missing.Source);
        if (order.DeliveryRoute is null or "blacksmith_input" && order.TargetItemKind == "iron_ore" &&
            HouseholdFor(actor) is { } oreHousehold && BlacksmithForHousehold(oreHousehold) is { } oreSmith &&
            DeliveryDestinationMatches(order, oreSmith) &&
            BlacksmithOreStocked(oreHousehold, oreSmith.InstanceId) < BlacksmithInputTarget(oreSmith.InstanceId, "iron_ore") &&
            PersonalSmithOre(actor) is null && !society.Checkpoint.Inventory.Lots.Any(lot => lot.OwnerId == oreHousehold &&
                lot.ItemKind == "iron_ore" && AvailableLotQuantity(lot) > 0) && MaterialSource("iron_ore", actor) is { } oreSource &&
            FreeCarryCapacity(actor) >= ProjectMaterialCarryUnits(actor, "iron_ore", oreSource) &&
            DeliveryCanReach(actor, person.Position, oreSource.Position, ResourceInteractionRange))
            return (oreSmith, "blacksmith_input", oreSource);
        if (order.DeliveryRoute is not (null or "workstation_input")) return null;
        var need = WorkstationSupplyNeeds(actor).FirstOrDefault(need => need.ItemKind == order.TargetItemKind &&
            DeliveryDestinationMatches(order, need.Building) && need.Source is not null &&
            DeliveryCanReach(actor, person.Position, need.Source.Position, ResourceInteractionRange));
        return need?.Source is { } source ? (need.Building, "workstation_input", source) : null;
    }

    private CognitionCandidate? DeliveryOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person) =>
        DeliveryPlanFor(instruction, person) is { } plan
            ? new("deliver_stock", "Carry the requested goods to the selected building under its normal stocking rules.", 0, plan.Destination.InstanceId)
            : DeliveryGatherFor(instruction, person) is { } gather
                ? new("deliver_stock", "Gather the requested input for the selected workstation.", 0, gather.Building.InstanceId) : null;

    private OwnerQueuedInstruction BindDeliveryOrder(OwnerQueuedInstruction instruction, PlacedBuilding destination,
        string ownerId, string route, string? lotId = null, int? quantity = null)
    {
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        var order = current.Order!;
        current = current with
        {
            Order = order with
            {
                TargetStorageBuildingId = order.TargetStorageBuildingId ?? destination.InstanceId,
                TargetStorageOwnerId = order.TargetStorageOwnerId ?? ownerId,
                TargetStoragePosition = order.TargetStoragePosition ?? destination.Position,
                DeliveryRoute = order.DeliveryRoute ?? route,
                DeliveryLotId = lotId ?? order.DeliveryLotId,
                DeliveryQuantity = quantity ?? order.DeliveryQuantity,
            }
        };
        instructionsByIdempotency[current.IdempotencyKey] = current;
        checkpointSchemaVersion = StateSchemaVersion;
        return current;
    }

    private DeliveryNativeEffect? ApplyDeliveryOrderPlan(string actor, PlaytestInhabitantState person,
        DeliveryOrderPlan plan)
    {
        var destination = plan.DirectDelivery ? plan.Destination.Position : HouseholdStockPosition(plan.Carrier);
        var range = plan.DirectDelivery ? 0 : HouseholdStockInteractionRange(plan.Carrier);
        // Moving a selected field tool ends only the ordinary work that used it.
        InterruptOrdinaryFieldWorkForCustody(actor, plan.Carrier.Id);
        if (!IsWithinInteractionRange(person.Position, destination, range))
        {
            MoveToward(actor, person, destination, "household_stock", range);
            return null;
        }
        var operation = $"order-delivery:{WorldTick}:{actor}:{plan.Carrier.Id}";
        var movedSource = plan.TakeFromContainer ? plan.Resource : plan.Carrier;
        var movedId = plan.TransferQuantity == movedSource.Quantity ? movedSource.Id :
            $"{movedSource.Id}#{(plan.TakeFromContainer ? "taken" : "transfer")}:{operation}";
        ApplyInventoryTransition(inventory => plan.TakeFromContainer
            ? InventoryFixture.TakeFromContainer(inventory, operation, plan.Carrier.OwnerId, actor,
                plan.Carrier.Id, plan.Resource.Id, plan.TransferQuantity, destinationDeliveryBuildingId: plan.Destination.InstanceId)
            : InventoryFixture.Transfer(inventory, operation, plan.Carrier.OwnerId,
                plan.DirectDelivery ? plan.DestinationOwnerId : actor, plan.Carrier.Id, plan.TransferQuantity,
                plan.DirectDelivery ? "owner_stock_delivered" : "owner_stock_picked_up",
                destinationStorageBuildingId: plan.DirectDelivery ? plan.Destination.InstanceId : null,
                destinationDeliveryBuildingId: plan.DirectDelivery ? null : plan.Destination.InstanceId));
        AppendEvent(plan.DirectDelivery ? "owner_stock_delivered" : "owner_stock_picked_up",
            $"{actor}:{movedSource.Id}:{plan.ResourceQuantity}:{plan.Destination.InstanceId}");
        return new(operation, movedId, plan.ResourceQuantity, plan.DirectDelivery);
    }

    private void ExecuteDeliveryOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        if (DeliveryPlanFor(instruction, person) is not { } plan)
        {
            if (DeliveryGatherFor(instruction, person) is { } gather)
            {
                instruction = BindDeliveryOrder(instruction, gather.Building, gather.Building.HouseholdId!, gather.Route);
                GatherProjectMaterial(actor, person, instruction.Order!.TargetItemKind!, gather.Source);
                return;
            }
            SetOrderStatus(instruction, "blocked", DeliveryOrderBlockedReason(instruction, person));
            return;
        }
        instruction = BindDeliveryOrder(instruction, plan.Destination, plan.DestinationOwnerId, plan.Route,
            plan.DirectDelivery ? plan.Carrier.Id : null,
            plan.DirectDelivery ? plan.ResourceQuantity : null);
        if (ApplyDeliveryOrderPlan(actor, person, plan) is not { } effect) return;
        if (!effect.Delivered)
        {
            BindDeliveryOrder(instruction, plan.Destination, plan.DestinationOwnerId, plan.Route, effect.LotId, effect.Quantity);
            return;
        }
        CreditOrderEffect(instruction, "delivery:stock:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(effect.OperationId))),
            instruction.Order!.ProgressUnit == "delivery_loads" ? 1 : effect.Quantity);
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        instructionsByIdempotency[current.IdempotencyKey] = current with
        {
            Order = current.Order! with { DeliveryLotId = null, DeliveryQuantity = null },
        };
    }

    private string DeliveryOrderBlockedReason(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var order = instruction.Order!;
        var actor = instruction.TargetInhabitantId;
        if (!AdultResident(actor)) return "An adult resident is needed to deliver goods.";
        if (order.TargetStorageBuildingId is { } bound &&
            (worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == bound) is not { } building ||
             !DeliveryDestinationMatches(order, building) ||
             (order.DeliveryPurpose == "town_surplus" ? TownForResident(actor) != building.TownId : HouseholdFor(actor) != building.HouseholdId)))
            return "The selected destination moved, changed owner or is no longer available; the order keeps its original destination.";
        if (order.DeliveryLotId is not null)
            return "The selected shipment must remain carried and unreserved, with room and an open route for its whole load at the destination.";
        if (!ReadyForBriefInteraction(actor)) return "The agent needs warmth before delivering goods.";
        return "No matching delivery is available under this building's normal stock, reserve, carrying and access limits.";
    }

    private static void ValidateDeliveryOrderBindings(WorldContentSimulationState? simulation, SocietyCheckpoint society,
        IReadOnlyList<TownRuntimeState>? savedTowns, IEnumerable<OwnerQueuedInstruction> instructions)
    {
        foreach (var instruction in instructions.Where(instruction => instruction.Order?.Action == "deliver_stock"))
        {
            var order = instruction.Order!;
            if (order.TargetStorageOwnerId is { } owner &&
                !(order.DeliveryPurpose == "town_surplus" ? savedTowns?.Any(town => town.Id == owner) == true :
                    society.Households.Any(household => household.Id == owner)))
                throw new InvalidDataException("A delivery order references an unknown destination owner.");
            // A removed building or changed physical shipment is a loadable blocker,
            // including after departure or death. Never substitute a new destination.
            if (simulation is null && order.TargetStorageBuildingId is not null)
                throw new InvalidDataException("A delivery order requires a saved settlement.");
        }
    }
}
