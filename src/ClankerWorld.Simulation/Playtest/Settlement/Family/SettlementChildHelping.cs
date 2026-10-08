using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    // Initial small-load and short-trip limits, independent of adult equipment upgrades.
    private const int ChildHelpingLoad = 4;
    private const int ChildHelpingRoute = 8;

    private PlacedBuilding? ChildHelpingHouse(string actor, PlaytestInhabitantState state) =>
        ChildResident(actor) && state.HungerBasisPoints >= 6_000 && !NeedsUrgentWarmth(state) &&
        society.Checkpoint.GetInhabitant(actor).HouseholdId is { } household &&
        HouseForHousehold(household) is { } house &&
        map.FootDistance(state.Position, house.Position) <= ChildHelpingRoute ? house : null;

    private int ChildHelpingRoom(string actor) => Math.Min(FreeCarryCapacity(actor), Math.Max(0,
        ChildHelpingLoad - PersonalEquipmentRules.CarriedQuantity(society.Checkpoint.Inventory, actor,
            inhabitants[actor].Equipment)));

    private bool ChildHelpingReachable(string actor, GridPoint source, int range, PlacedBuilding house)
    {
        bool ShortRoute(GridPoint from, GridPoint to, int interactionRange)
        {
            if (IsWithinInteractionRange(from, to, interactionRange)) return true;
            var route = FindUnoccupiedRoute(actor, from, to, interactionRange);
            return route.Count is > 0 and <= ChildHelpingRoute &&
                route.All(point => map.FootDistance(point, house.Position) <= ChildHelpingRoute);
        }
        return map.FootDistance(source, house.Position) <= ChildHelpingRoute &&
            ShortRoute(inhabitants[actor].Position, source, range) && ShortRoute(source, house.Position, 0);
    }

    private static bool ChildHelpingItem(string kind) => kind == "wood" || IsEdibleFood(kind);

    private InventoryLot? ChildHelpingDelivery(string actor, PlacedBuilding house) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
            PersonalEquipmentRules.IsCarried(lot, actor) && lot.ContainerLotId is null &&
            lot.DeliveryBuildingId == house.InstanceId && ChildHelpingItem(lot.ItemKind) &&
            lot.Quantity <= ChildHelpingLoad &&
            PersonalEquipmentRules.CarriedQuantity(society.Checkpoint.Inventory, actor, inhabitants[actor].Equipment) <= ChildHelpingLoad && AvailableLotQuantity(lot) == lot.Quantity &&
            CanDeliverHouseDelivery(lot)).OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private InventoryLot? ChildHelpingStock(string actor, PlacedBuilding house) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == house.HouseholdId &&
            lot.CarrierId is null && lot.ContainerLotId is null && lot.StorageBuildingId is null &&
            lot.DeliveryBuildingId is null && ChildHelpingItem(lot.ItemKind) && !OnBorrowedMarketStall(lot) &&
            AvailableLotQuantity(lot) > 0 && ChildHelpingReachable(actor, HouseholdStockPosition(lot),
                HouseholdStockInteractionRange(lot), house))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private MapResource? ChildHelpingSource(string actor, PlacedBuilding house, string kind) => map.Resources
        .Where(source => resources.GetValueOrDefault(source.Id) == ResourceState.Available &&
            (kind == "food" ? source.Kind is "food" or "fruit" && source.TreeKind is null &&
                FoodHarvestCarryUnits(source) <= ChildHelpingRoom(actor) &&
                StorageRoomAfterInboundDeliveries(house.InstanceId) >= FoodHarvestQuantity(source)
                : source.TreeKind is null && (source.Kind == "wood" || source.NaturalObjectKind == "fallen_wood")) &&
            ChildHelpingReachable(actor, source.Position, ResourceInteractionRange, house))
        .OrderBy(source => map.FootDistance(inhabitants[actor].Position, source.Position))
        .ThenBy(source => source.Id, StringComparer.Ordinal).FirstOrDefault();

    private void AddChildHelpingCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState state)
    {
        if (ChildHelpingHouse(actor, state) is not { } house) return;
        if (ChildHelpingDelivery(actor, house) is { } delivery)
        {
            if (ChildHelpingReachable(actor, state.Position, 0, house))
                candidates.Add(new("child_carry:" + delivery.Id, "Return your small delivery to your household's House.", 20));
            return;
        }
        if (CarriedHouseDelivery(actor) is not null || ChildHelpingRoom(actor) == 0 ||
            StorageRoomAfterInboundDeliveries(house.InstanceId) == 0) return;
        if (ChildHelpingStock(actor, house) is { } stock)
            candidates.Add(new("child_carry:" + stock.Id, "Carry a small loose household food or wood load to your House.", 28));
        foreach (var kind in new[] { "food", "wood" })
            if (ChildHelpingSource(actor, house, kind) is { } source)
                candidates.Add(new("child_gather:" + kind + ":" + source.Id,
                    kind == "food" ? "Gather nearby wild food for your household, then carry it home." :
                        "Pick up nearby fallen wood for your household, then carry it home.", 30));
    }

    private void ApplyChildHelpingCandidate(string actor, PlaytestInhabitantState state, string candidate)
    {
        if (ChildHelpingHouse(actor, state) is not { } house) return;
        var legal = new List<CognitionCandidate>();
        AddChildHelpingCandidates(legal, actor, state);
        if (!legal.Any(item => item.Id == candidate)) return;
        if (candidate.StartsWith("child_carry:", StringComparison.Ordinal))
        {
            if (ChildHelpingDelivery(actor, house) is { } delivery && ("child_carry:" + delivery.Id) == candidate)
            {
                if (state.Position != house.Position)
                {
                    MoveToward(actor, state, house.Position, "child_help", 0);
                    return;
                }
                ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                    $"child-delivery:{WorldTick}:{actor}", actor, house.HouseholdId!, delivery.Id, delivery.Quantity,
                    "child_household_delivery", house.InstanceId));
                AppendEvent("child_delivered_household", $"{actor}:{delivery.ItemKind}:{delivery.Quantity}:{house.InstanceId}");
                return;
            }
            if (ChildHelpingStock(actor, house) is not { } stock || ("child_carry:" + stock.Id) != candidate) return;
            var source = HouseholdStockPosition(stock);
            var range = HouseholdStockInteractionRange(stock);
            if (!IsWithinInteractionRange(state.Position, source, range))
            {
                MoveToward(actor, state, source, "child_help", range);
                return;
            }
            var quantity = Math.Min(ChildHelpingRoom(actor), Math.Min(AvailableLotQuantity(stock),
                StorageRoomAfterInboundDeliveries(house.InstanceId)));
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"child-pickup:{WorldTick}:{actor}", house.HouseholdId!, actor, stock.Id, quantity,
                "child_household_pickup", destinationDeliveryBuildingId: house.InstanceId));
            AppendEvent("child_collected_household", $"{actor}:{stock.ItemKind}:{quantity}");
            return;
        }
        var kind = candidate["child_gather:".Length..].Split(':', 2)[0];
        if (ChildHelpingSource(actor, house, kind) is not { } resource ||
            candidate != ("child_gather:" + kind + ":" + resource.Id)) return;
        if (!IsWithinInteractionRange(state.Position, resource.Position, ResourceInteractionRange))
        {
            MoveToward(actor, state, resource.Position, "child_help", ResourceInteractionRange);
            return;
        }
        if (kind == "wood")
            GatherProjectMaterial(actor, state, "wood", resource, deliveryBuildingId: house.InstanceId);
        else if (HarvestFood(actor, state, resource) is { } food)
            ApplyInventoryTransition(inventory => MarkBuildingMaterialDelivery(inventory, food.LotId, house.InstanceId));
    }
}
