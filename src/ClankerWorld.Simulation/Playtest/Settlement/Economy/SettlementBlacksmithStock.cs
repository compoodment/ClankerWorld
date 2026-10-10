using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string GatherBlacksmithInputPrefix = "gather_smith_input:";
    private const string CollectToolPrefix = "collect_tool:";

    private bool MayCollectToolFamily(string actor, ToolFamily family) =>
        family is not (ToolFamily.Hoe or ToolFamily.Sickle) ||
        HouseholdFor(actor) is { } householdId && FarmhouseForHousehold(householdId) is not null;

    private void AddCraftToolCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor)) return;
        AddToolRepairCandidates(candidates, actor);
        if (FreeCarryCapacity(actor) <= 0) return;
        var inventory = society.Checkpoint.Inventory;
        var pendingHarvest = HouseholdFor(actor) is { } householdId &&
            BlacksmithForHousehold(householdId) is { } smith &&
            BlacksmithHarvestToPrepare(householdId, smith.InstanceId, actor) is { } missing
            ? ProjectMaterialHarvest(actor, missing.ItemKind, missing.Source) : null;
        var pendingToolFamily = pendingHarvest?.ToolLotId is { } requiredToolId
            ? ToolProgressionRules.Find(inventory.GetLot(requiredToolId).ItemKind)?.Family : null;
        foreach (var family in ToolProgressionRules.All.GroupBy(tool => tool.Family)
                     .OrderBy(group => group.Key))
        {
            // Field tools are useful only to a household that can work its fields.
            if (!MayCollectToolFamily(actor, family.Key)) continue;
            var bestShared = family.Where(tool => SharedItem(tool.ItemKind, actor) is { ContainerLotId: null })
                .OrderByDescending(tool => tool.Tier)
                .ThenBy(tool => tool.ItemKind, StringComparer.Ordinal)
                .FirstOrDefault();
            if (bestShared is null) continue;
            // Leave room for the pending whole harvest. Otherwise a spare tool
            // set down for that work would be immediately collected again.
            if (pendingHarvest is { } harvest && bestShared.Family != pendingToolFamily &&
                FreeCarryCapacity(actor) - 1 < checked(harvest.Quantity + harvest.TreeSeedQuantity))
                continue;
            if (pendingHarvest is { } upgradedHarvest && bestShared.Family == pendingToolFamily)
            {
                var roomMissing = checked(bestShared.GatherQuantity + upgradedHarvest.TreeSeedQuantity) -
                    (FreeCarryCapacity(actor) - 1);
                if (roomMissing > 0)
                {
                    // The old tool becomes spare after the upgrade. The new
                    // shared tool is not carried yet and cannot be set down.
                    var cargo = SpareCargoForFood(actor, roomMissing);
                    if (cargo.Count == 0 || SpareCargoDestination(actor, inhabitants[actor],
                            cargo.Sum(move => move.PhysicalQuantity)) is null)
                        continue;
                }
            }

            var carried = ToolProgressionRules.BestUsableTool(inventory, actor, family.Key);
            if (carried is not null && ToolProgressionRules.Find(carried.ItemKind)!.Tier >= bestShared.Tier)
                continue;

            var candidateId = bestShared.ItemKind switch
            {
                "wooden_axe" => "collect_wooden_axe",
                "wooden_pickaxe" => "collect_wooden_pickaxe",
                "wooden_hoe" => "collect_wooden_hoe",
                _ => CollectToolPrefix + bestShared.ItemKind,
            };
            candidates.Add(new CognitionCandidate(candidateId,
                $"Collect an accessible {bestShared.ItemKind.Replace('_', ' ')} for work.", 18));
        }
    }

    private PlacedBuilding? BlacksmithForHousehold(string householdId) =>
        HouseholdBuildingWithTag(householdId, "blacksmith");

    private int BlacksmithOreStocked(string householdId, string blacksmithId) =>
        BlacksmithInputStocked(householdId, blacksmithId, "iron_ore");

    private int BlacksmithInputStocked(string householdId, string blacksmithId, string itemKind) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId &&
            lot.StorageBuildingId == blacksmithId && lot.ItemKind == itemKind).Sum(AvailableLotQuantity);

    private (string ItemKind, int Target)[] BlacksmithInputTargets(string blacksmithId)
    {
        var placed = worldSimulation.Buildings.SingleOrDefault(building => building.InstanceId == blacksmithId);
        if (placed is null) return [];
        // A handcart is built only from what its builder carries, so the Blacksmith keeps no stock for it.
        return worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == placed.DefinitionId && !IsHandcartRecipe(recipe))
            .SelectMany(recipe => recipe.Inputs)
            .GroupBy(input => input.ResourceId, StringComparer.Ordinal)
            .Select(group => (ItemKind: group.Key, Target: checked(group.Max(input => input.Amount) * 2)))
            .OrderBy(item => item.ItemKind, StringComparer.Ordinal)
            .ToArray();
    }

    private int BlacksmithInputTarget(string blacksmithId, string itemKind) =>
        BlacksmithInputTargets(blacksmithId).FirstOrDefault(item => item.ItemKind == itemKind).Target;

    private InventoryLot? PersonalSmithOre(string actor) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) && lot.ItemKind == "iron_ore" &&
            lot.DeliveryBuildingId is null && lot.ContainerLotId is null &&
            AvailableLotQuantity(lot) > 0)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private void AddBlacksmithOreCandidates(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarriedHouseDelivery(actor) is not null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            StorageRoomAfterInboundDeliveries(blacksmith.InstanceId) == 0 ||
            BlacksmithOreStocked(householdId, blacksmith.InstanceId) >= BlacksmithInputTarget(blacksmith.InstanceId, "iron_ore"))
            return;
        if (PersonalSmithOre(actor) is not null)
        {
            if (state.Position == blacksmith.Position ||
                FindUnoccupiedRoute(actor, state.Position, blacksmith.Position, 0).Count > 0)
                candidates.Add(new CognitionCandidate("deliver_smith_ore",
                    "Carry mined iron ore into the household Blacksmith.", 22, blacksmith.InstanceId));
            return;
        }
        if (BlacksmithHasDeliverableInput(householdId, blacksmith, actor, "iron_ore"))
            return;
        if (MaterialSource("iron_ore", actor, blacksmith.Position) is not { } source ||
            FreeCarryCapacity(actor) < ProjectMaterialCarryUnits(actor, "iron_ore", source) &&
                !CanMakeRoomForBlacksmithHarvest(actor, state, "iron_ore", source) ||
            !IsWithinInteractionRange(state.Position, source.Position, ResourceInteractionRange) &&
            FindUnoccupiedRoute(actor, state.Position, source.Position, ResourceInteractionRange).Count == 0)
            return;
        candidates.Add(new CognitionCandidate("gather_smith_ore",
            "Mine iron ore for the household Blacksmith.", 30, source.Id));
    }

    private void GatherBlacksmithOre(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            BlacksmithOreStocked(householdId, blacksmith.InstanceId) >= BlacksmithInputTarget(blacksmith.InstanceId, "iron_ore") ||
            PersonalSmithOre(actor) is not null ||
            MaterialSource("iron_ore", actor, blacksmith.Position) is not { } source)
            return;
        if (!MakeRoomForBlacksmithHarvest(actor, state, householdId, "iron_ore", source))
            GatherProjectMaterial(actor, state, "iron_ore", source, returnTo: blacksmith.Position);
    }

    private void DeliverBlacksmithOre(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            PersonalSmithOre(actor) is not { } ore)
            return;
        var stocked = BlacksmithOreStocked(householdId, blacksmith.InstanceId);
        var target = BlacksmithInputTarget(blacksmith.InstanceId, "iron_ore");
        if (stocked >= target) return;
        var quantity = Math.Min(StorageRoomAfterInboundDeliveries(blacksmith.InstanceId),
            Math.Min(target - stocked, AvailableLotQuantity(ore)));
        if (quantity <= 0) return;
        if (state.Position != blacksmith.Position)
        {
            MoveToward(actor, state, blacksmith.Position, "smith_ore", 0);
            return;
        }
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"smith-ore-delivery:{WorldTick}:{actor}", actor, householdId, ore.Id,
            quantity, "smith_ore_delivered", blacksmith.InstanceId));
        AppendEvent("smith_ore_delivered", $"{actor}:{ore.Id}:{quantity}:{blacksmith.InstanceId}");
    }

    private InventoryLot? BlacksmithInputForDelivery(string householdId, PlacedBuilding blacksmith, string actor,
        string? itemKind = null, string? sourceLotId = null)
    {
        var blacksmithId = blacksmith.InstanceId;
        var inventory = society.Checkpoint.Inventory;
        foreach (var (kind, target) in BlacksmithInputTargets(blacksmithId))
        {
            if (itemKind is not null && kind != itemKind) continue;
            var stocked = inventory.Lots
                .Where(lot => lot.OwnerId == householdId && lot.StorageBuildingId == blacksmithId &&
                    lot.ItemKind == kind).Sum(AvailableLotQuantity);
            var incoming = inventory.Lots.Where(lot => lot.DeliveryBuildingId == blacksmithId &&
                    lot.ItemKind == kind).Sum(AvailableLotQuantity);
            if (stocked + incoming >= target) continue;
            var personal = inventory.Lots
                .Where(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null &&
                    (sourceLotId is null || lot.Id == sourceLotId) &&
                    lot.ContainerLotId is null && lot.ItemKind == kind && SpareCarriedQuantity(actor, lot) > 0 &&
                    !PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id))
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
            if (personal is not null) return personal;
            var source = inventory.Lots
                .Where(lot => lot.OwnerId == householdId && lot.CarrierId is null && !OnBorrowedMarketStall(lot) && lot.StorageBuildingId != blacksmithId &&
                    (sourceLotId is null || lot.Id == sourceLotId) &&
                    lot.DeliveryBuildingId != blacksmithId && lot.ContainerLotId is null && lot.ItemKind == kind &&
                    AvailableLotQuantity(lot) > 0)
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault(CanHaulSharedInput);
            source ??= AvailableWarehouseStock(actor, kind)
                .FirstOrDefault(lot => (sourceLotId is null || lot.Id == sourceLotId) && CanHaulSharedInput(lot));
            if (source is not null) return source;
        }
        return null;

        // A blocked earlier lot must not hide stock the actor can actually deliver.
        bool CanHaulSharedInput(InventoryLot lot)
        {
            var position = inhabitants[actor].Position;
            var source = HouseholdStockPosition(lot);
            var range = HouseholdStockInteractionRange(lot);
            return (IsWithinInteractionRange(position, source, range) ||
                    FindUnoccupiedRoute(actor, position, source, range).Count > 0) &&
                PickupCarryCapacity(actor, lot, blacksmith.Position) > 0;
        }
    }

    private bool BlacksmithHasDeliverableInput(string householdId, PlacedBuilding blacksmith, string actor, string itemKind)
    {
        if (StorageRoomAfterInboundDeliveries(blacksmith.InstanceId) <= 0 ||
            BlacksmithInputForDelivery(householdId, blacksmith, actor, itemKind) is not { } input)
            return false;
        if (input.OwnerId != actor) return FreeCarryCapacity(actor) > 0;
        var position = inhabitants[actor].Position;
        return position == blacksmith.Position || FindUnoccupiedRoute(actor, position, blacksmith.Position, 0).Count > 0;
    }

    private (string ItemKind, MapResource Source)? BlacksmithInputToGather(string householdId, string blacksmithId,
        string actor, string? itemKind = null)
    {
        var blacksmith = worldSimulation.Buildings.SingleOrDefault(building => building.InstanceId == blacksmithId);
        if (blacksmith is null || StorageRoomAfterInboundDeliveries(blacksmithId) <= 0) return null;
        var inventory = society.Checkpoint.Inventory;
        foreach (var (kind, target) in BlacksmithInputTargets(blacksmithId))
        {
            if (itemKind is not null && kind != itemKind) continue;
            if (kind == "iron_ore") continue;
            var stocked = inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                    lot.StorageBuildingId == blacksmithId && lot.ItemKind == kind)
                .Sum(AvailableLotQuantity);
            var incoming = inventory.Lots.Where(lot => lot.DeliveryBuildingId == blacksmithId && lot.ItemKind == kind)
                .Sum(AvailableLotQuantity);
            if (stocked + incoming >= target || BlacksmithHasDeliverableInput(householdId, blacksmith, actor, kind))
                continue;
            if (MaterialSource(kind, actor, blacksmith.Position) is { } source)
                return (kind, source);
        }
        return null;
    }

    private void GatherBlacksmithInput(string actor, PlaytestInhabitantState state, string itemKind)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            BlacksmithInputToGather(householdId, blacksmith.InstanceId, actor) is not { } missing ||
            missing.ItemKind != itemKind)
            return;
        if (!MakeRoomForBlacksmithHarvest(actor, state, householdId, itemKind, missing.Source))
            GatherProjectMaterial(actor, state, itemKind, missing.Source, returnTo: blacksmith.Position);
    }

    private bool MakeRoomForBlacksmithHarvest(string actor, PlaytestInhabitantState state,
        string householdId, string itemKind, MapResource source)
    {
        if (ProjectMaterialHarvest(actor, itemKind, source) is not { } plan)
            return false;
        var missing = checked(plan.Quantity + plan.TreeSeedQuantity) - FreeCarryCapacity(actor);
        return missing > 0 && StoreSpareCargo(actor, state, householdId,
            SpareCargoForFood(actor, missing, plan.ToolLotId));
    }

    private bool CanMakeRoomForBlacksmithHarvest(string actor, PlaytestInhabitantState state,
        string itemKind, MapResource source)
    {
        if (ProjectMaterialHarvest(actor, itemKind, source) is not { } plan)
            return false;
        var cargo = SpareCargoForFood(actor,
            checked(plan.Quantity + plan.TreeSeedQuantity) - FreeCarryCapacity(actor), plan.ToolLotId);
        return cargo.Count > 0 && SpareCargoDestination(actor, state,
            cargo.Sum(move => move.PhysicalQuantity)) is not null;
    }

    private (string ItemKind, MapResource Source)? BlacksmithHarvestToPrepare(string householdId,
        string blacksmithId, string actor)
    {
        var blacksmith = worldSimulation.Buildings.SingleOrDefault(building => building.InstanceId == blacksmithId);
        if (blacksmith is null || StorageRoomAfterInboundDeliveries(blacksmithId) <= 0) return null;
        if (BlacksmithInputToGather(householdId, blacksmithId, actor) is { } missing)
            return missing;
        if (PersonalSmithOre(actor) is not null ||
            BlacksmithOreStocked(householdId, blacksmithId) >= BlacksmithInputTarget(blacksmithId, "iron_ore") ||
            BlacksmithHasDeliverableInput(householdId, blacksmith, actor, "iron_ore"))
            return null;
        return MaterialSource("iron_ore", actor, blacksmith.Position) is { } source ? ("iron_ore", source) : null;
    }

    private void AddBlacksmithStockCandidate(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null || CarriedHouseDelivery(actor) is not null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith)
        {
            if (AdultResident(actor) && householdId is not null && CarriedHouseDelivery(actor) is null &&
                BlacksmithForHousehold(householdId) is { } gatheringSmith &&
                BlacksmithInputToGather(householdId, gatheringSmith.InstanceId, actor) is { } missing)
            {
                var candidateId = missing.ItemKind == "iron_ore"
                    ? "gather_smith_ore" : GatherBlacksmithInputPrefix + missing.ItemKind;
                candidates.Add(new CognitionCandidate(candidateId,
                    $"Gather {missing.ItemKind.Replace('_', ' ')} for the household Blacksmith.", 30,
                    missing.Source.Id));
            }
            return;
        }
        if (StorageRoomAfterInboundDeliveries(blacksmith.InstanceId) == 0)
            return;
        var input = BlacksmithInputForDelivery(householdId, blacksmith, actor);
        if (input is null)
        {
            if (BlacksmithInputToGather(householdId, blacksmith.InstanceId, actor) is { } missing)
            {
                var candidateId = missing.ItemKind == "iron_ore"
                    ? "gather_smith_ore" : GatherBlacksmithInputPrefix + missing.ItemKind;
                candidates.Add(new CognitionCandidate(candidateId,
                    $"Gather {missing.ItemKind.Replace('_', ' ')} for the household Blacksmith.", 30,
                    missing.Source.Id));
            }
            return;
        }
        if (input.OwnerId != actor && FreeCarryCapacity(actor) == 0)
            return;

        if (input.OwnerId == actor && state.Position != blacksmith.Position &&
            FindUnoccupiedRoute(actor, state.Position, blacksmith.Position, 0).Count == 0)
            return;
        candidates.Add(new CognitionCandidate("haul_smith_input",
            $"Carry {input.ItemKind.Replace('_', ' ')} into the household Blacksmith for on-site work.",
            24, blacksmith.InstanceId));
    }

    private void HaulBlacksmithInput(string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (!AdultResident(actor) || householdId is null ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            BlacksmithInputForDelivery(householdId, blacksmith, actor) is not { } input)
            return;
        var room = StorageRoomAfterInboundDeliveries(blacksmith.InstanceId);
        if (room == 0) return;
        if (input.OwnerId == actor)
        {
            if (state.Position != blacksmith.Position)
            {
                MoveToward(actor, state, blacksmith.Position, "smith_input", 0);
                return;
            }
            var stockedPersonal = BlacksmithInputStocked(householdId, blacksmith.InstanceId, input.ItemKind);
            var incomingPersonal = society.Checkpoint.Inventory.Lots.Where(lot =>
                    lot.DeliveryBuildingId == blacksmith.InstanceId && lot.ItemKind == input.ItemKind)
                .Sum(AvailableLotQuantity);
            var targetPersonal = BlacksmithInputTarget(blacksmith.InstanceId, input.ItemKind);
            var personalQuantity = Math.Min(targetPersonal - stockedPersonal - incomingPersonal,
                Math.Min(room, Math.Min(HouseHaulLoadQuantity, SpareCarriedQuantity(actor, input))));
            if (personalQuantity <= 0) return;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
                $"smith-input-delivery:{WorldTick}:{actor}", actor, householdId, input.Id,
                personalQuantity, "smith_input_delivered", destinationStorageBuildingId: blacksmith.InstanceId));
            AppendEvent("smith_input_delivered", $"{actor}:{input.Id}:{personalQuantity}:{blacksmith.InstanceId}");
            return;
        }
        var source = HouseholdStockPosition(input);
        var range = HouseholdStockInteractionRange(input);
        if (!IsWithinInteractionRange(state.Position, source, range))
        {
            MoveToward(actor, state, source, "smith_input", range);
            return;
        }
        var stocked = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId &&
                lot.StorageBuildingId == blacksmith.InstanceId && lot.ItemKind == input.ItemKind)
            .Sum(AvailableLotQuantity);
        var incoming = society.Checkpoint.Inventory.Lots.Where(lot =>
                lot.DeliveryBuildingId == blacksmith.InstanceId && lot.ItemKind == input.ItemKind)
            .Sum(AvailableLotQuantity);
        var target = BlacksmithInputTarget(blacksmith.InstanceId, input.ItemKind);
        var quantity = Math.Min(HouseHaulLoadQuantity,
            Math.Min(target - stocked - incoming, AvailableLotQuantity(input)));
        quantity = Math.Min(quantity, Math.Min(room, PickupCarryCapacity(actor, input, blacksmith.Position)));
        if (quantity <= 0) return;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory,
            $"smith-input-pickup:{WorldTick}:{actor}", input.OwnerId, actor, input.Id,
            quantity, "smith_input_picked_up", destinationDeliveryBuildingId: blacksmith.InstanceId));
        AppendEvent("smith_input_picked_up", $"{actor}:{input.Id}:{quantity}:{blacksmith.InstanceId}");
    }

    private DeliveryOrderPlan? GetBlacksmithOrderPlan(string actor, PlaytestInhabitantState person,
        OwnerInstructionOrder order, int maximumQuantity)
    {
        if (!AdultResident(actor) || HouseholdFor(actor) is not { } householdId ||
            BlacksmithForHousehold(householdId) is not { } blacksmith ||
            !DeliveryDestinationMatches(order, blacksmith) ||
            BlacksmithInputForDelivery(householdId, blacksmith, actor,
                order.TargetItemKind, order.DeliveryLotId) is not { } input)
            return null;
        var direct = input.OwnerId == actor;
        var stocked = BlacksmithInputStocked(householdId, blacksmith.InstanceId, input.ItemKind);
        var incoming = society.Checkpoint.Inventory.Lots.Where(lot =>
                lot.DeliveryBuildingId == blacksmith.InstanceId && lot.ItemKind == input.ItemKind)
            .Sum(AvailableLotQuantity);
        var missing = BlacksmithInputTarget(blacksmith.InstanceId, input.ItemKind) - stocked - incoming;
        var quantity = Math.Min(maximumQuantity, Math.Min(missing,
            Math.Min(StorageRoomAfterInboundDeliveries(blacksmith.InstanceId),
                direct ? SpareCarriedQuantity(actor, input) : AvailableLotQuantity(input))));
        // Personal mined ore has a dedicated native delivery without the loose-input load limit.
        if (!direct || input.ItemKind != "iron_ore") quantity = Math.Min(quantity, HouseHaulLoadQuantity);
        if (!direct) quantity = Math.Min(quantity, PickupCarryCapacity(actor, input, blacksmith.Position));
        return quantity <= 0 ? null : new DeliveryOrderPlan("blacksmith_input", blacksmith, householdId,
            input, input, quantity, quantity, DirectDelivery: direct);
    }
}
