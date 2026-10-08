using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>An exclusive physical attachment; position, property, condition and cargo remain inventory facts.</summary>
public sealed record HandcartHitch(string CartLotId, string PullerId);

public sealed partial class PrivateWorldRuntime
{
    private static readonly string[] CartRepairKinds = ["wood", "iron_fittings", "rope"];
    private const string AttachCartPrefix = "attach_handcart:";
    private const string LoadCartPrefix = "load_handcart:";
    private const string UnloadCartPrefix = "unload_handcart:";
    private const string UnloadCartGroundPrefix = "unload_handcart_ground:";
    private const string CollectCartMaterialPrefix = "collect_handcart_material:";
    private const string CollectCartRepairPrefix = "collect_handcart_repair:";
    private const string RepairCartPrefix = "repair_handcart:";
    private const string TransferCartPrefix = "give_handcart:";
    private const string PullCartPrefix = "pull_handcart:";
    // Generic cart controls have no delivery intention. Keep them selectable by models without
    // letting the built-in chooser shuffle goods or pull between arbitrary buildings ahead of safe idle.
    private const int UnassignedCartPriority = 110;

    private static bool IsHandcartRecipe(RecipeDefinition recipe) => recipe.Outputs.Any(output => output.ResourceId == InventoryContainerRules.Handcart);

    private RecipeDefinition? HouseholdHandcartRecipe(string? household) =>
        household is not null && BlacksmithForHousehold(household) is { } blacksmith
            ? worldContent.Recipes.FirstOrDefault(recipe =>
                recipe.WorkstationBuildingId == blacksmith.DefinitionId && IsHandcartRecipe(recipe))
            : null;

    /// <summary>
    /// The cart recipe while the actor may build their own cart: no cart yet, none being built, and every
    /// material either carried already or in stock they may collect, so they do not gather part of a set that
    /// nothing in the world could complete.
    /// </summary>
    private RecipeDefinition? HandcartToBuild(string actor)
    {
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        var recipe = HouseholdHandcartRecipe(household);
        if (recipe is null || household is null || !AdultResident(actor) ||
            society.Checkpoint.Inventory.Lots.Any(lot => lot.ItemKind == InventoryContainerRules.Handcart && lot.OwnerId == actor) ||
            worldSimulation.ProductionJobs.Any(job => job.WorkerId == actor && job.RecipeId == recipe.CanonicalId &&
                job.State == WorldProductionJobState.Running))
            return null;
        var shortfall = recipe.Inputs.Select(input => (Kind: input.ResourceId, Missing: input.Amount -
                CarriedMaterialQuantity(actor, input.ResourceId) - HouseholdMaterialQuantity(household, input.ResourceId)))
            .Where(item => item.Missing > 0).ToArray();
        // Town Warehouse stock needs a route, so it is counted last: first without routes, then one route per Warehouse.
        return shortfall.All(item => WarehouseStockLots(actor, item.Kind).Sum(AvailableLotQuantity) >= item.Missing) &&
            shortfall.All(item => ReachableWarehouseStockCovers(actor, item.Kind, item.Missing)) ? recipe : null;
    }

    private int CarriedMaterialQuantity(string actor, string kind) => society.Checkpoint.Inventory.Lots
        .Where(lot => ToolProgressionRules.IsTopLevelCarriedLot(lot, actor) && lot.OwnerId == actor && lot.ItemKind == kind)
        .Sum(AvailableLotQuantity);

    private int HouseholdMaterialQuantity(string household, string kind) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == household && lot.CarrierId is null && lot.ContainerLotId is null && lot.ItemKind == kind)
        .Sum(AvailableLotQuantity);

    private bool ReachableWarehouseStockCovers(string actor, string kind, int missing)
    {
        var found = 0;
        foreach (var warehouse in WarehouseStockLots(actor, kind).GroupBy(lot => lot.StorageBuildingId, StringComparer.Ordinal))
        {
            if (!CanReachSharedItem(actor, warehouse.First())) continue;
            found += warehouse.Sum(AvailableLotQuantity);
            if (found >= missing) return true;
        }
        return false;
    }

    // A would-be cart builder keeps the materials they carry for it. Household hauls take only what is
    // beyond that, or they would return it to stock as fast as the builder collects it from there.
    private int SpareCarriedQuantity(string actor, InventoryLot lot)
    {
        var kept = lot.OwnerId == actor && ToolProgressionRules.IsTopLevelCarriedLot(lot, actor) &&
            HouseholdHandcartRecipe(society.Checkpoint.GetInhabitant(actor).HouseholdId) is { } cart
            ? cart.Inputs.Where(input => input.ResourceId == lot.ItemKind).Sum(input => input.Amount) : 0;
        if (kept == 0 || HandcartToBuild(actor) is null) return AvailableLotQuantity(lot);
        return Math.Min(AvailableLotQuantity(lot), Math.Max(0, CarriedMaterialQuantity(actor, lot.ItemKind) - kept));
    }

    private static bool IsHandcartCargo(InventoryCheckpoint inventory, InventoryLot lot) => lot.ContainerLotId is { } id &&
        inventory.Lots.Any(container => container.Id == id && container.ItemKind == InventoryContainerRules.Handcart);

    private InventoryLot? AttachedHandcart(string actor) => handcartHitches
        .FirstOrDefault(hitch => hitch.PullerId == actor) is { } hitch
        ? society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == hitch.CartLotId) : null;

    private IEnumerable<InventoryLot> NearbyOwnedHandcarts(string actor, GridPoint position) =>
        society.Checkpoint.Inventory.Lots.Where(lot =>
                lot.ItemKind == InventoryContainerRules.Handcart && lot.OwnerId == actor &&
                lot.GroundPosition == new InventoryGroundPosition(position.X, position.Y))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal);

    private InventoryLot? NearbyOwnedHandcart(string actor, GridPoint position) =>
        AttachedHandcart(actor) ?? NearbyOwnedHandcarts(actor, position).FirstOrDefault();

    private InventoryLot? HandcartForCargoAt(string actor, GridPoint position, string cargoId)
    {
        var cargo = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == cargoId);
        return cargo?.ContainerLotId is { } cartId
            ? NearbyOwnedHandcarts(actor, position).FirstOrDefault(cart => cart.Id == cartId)
            : null;
    }

    private bool CanPullHandcart(InventoryLot cart) => cart.ConditionBasisPoints > 0 &&
        !HasActiveContainerReservation(society.Checkpoint.Inventory, cart.Id);

    private bool LegalHandcartStep(GridPoint from, GridPoint to) => !map.IsDiagonalFootStep(from, to) &&
        (IsRoadSurface(to) || (map.ElevationAt(to) ?? 0) < SeededMap.MountainElevationThreshold &&
            map.Tiles[to.Y * map.Width + to.X].Terrain is not (TerrainKind.Mountain or TerrainKind.Peak));

    private int TravelStepCost(string actor, GridPoint from, GridPoint to) => AttachedHandcart(actor) is null
        ? RoadStepCost(from, to)
        : IsRoadSurface(from) && IsRoadSurface(to)
            ? RoadStepCost(from, to) : checked(RoadStepCost(from, to) + 100);

    private void MoveAttachedHandcart(string actor, GridPoint from, GridPoint to)
    {
        if (AttachedHandcart(actor) is not { } cart) return;
        if (cart.GroundPosition != new InventoryGroundPosition(from.X, from.Y) || !LegalHandcartStep(from, to))
            throw new InvalidOperationException("The cart and puller must share a legal physical step.");
        ApplyInventoryTransition(inventory => InventoryFixture.MoveHandcart(inventory, actor, cart.Id,
            new(to.X, to.Y), IsRoadSurface(from) && IsRoadSurface(to) ? 5 : 20));
        AppendEvent("handcart_moved", $"{actor}:{cart.Id}");
        if (society.Checkpoint.Inventory.GetLot(cart.Id).ConditionBasisPoints == 0)
            ParkHandcart(actor, "broken");
    }

    private void ParkHandcart(string actor, string reason = "parked")
    {
        if (handcartHitches.FirstOrDefault(hitch => hitch.PullerId == actor) is not { } hitch) return;
        handcartHitches.Remove(hitch);
        AppendEvent("handcart_parked", $"{actor}:{hitch.CartLotId}:{reason}");
    }

    private void ReconcileHandcartHitches()
    {
        foreach (var hitch in handcartHitches.ToArray())
        {
            var cart = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == hitch.CartLotId);
            if (!inhabitants.ContainsKey(hitch.PullerId) || cart?.OwnerId != hitch.PullerId ||
                cart.ConditionBasisPoints == 0)
                ParkHandcart(hitch.PullerId, "ownership_changed");
        }
    }

    private bool CanLoadCartLot(string actor, PlaytestInhabitantState person, InventoryLot lot) =>
        lot.ContainerLotId is null && lot.DeliveryBuildingId is null &&
        !OnBorrowedMarketStall(lot) && (lot.OwnerId != actor || !OnMarketStall(lot)) &&
        !InventoryContainerRules.IsContainer(lot.ItemKind) && AvailableLotQuantity(lot) > 0 &&
        // Worn clothing, a carry aid, a worn ornament or an item under repair stays on the agent.
        !PersonalEquipmentRules.IsSelected(person.Equipment, lot.Id) &&
        // Goods in anyone's custody, even the owner's, must be put down before loading.
        (lot.OwnerId == actor && lot.CarrierId is null && (ToolProgressionRules.IsTopLevelCarriedLot(lot, actor) ||
             lot.GroundPosition == new InventoryGroundPosition(person.Position.X, person.Position.Y)) ||
         // Household stock someone is borrowing stays the household's.
         lot.OwnerId == society.Checkpoint.GetInhabitant(actor).HouseholdId && lot.CarrierId is null &&
             (!IsEdibleFood(lot.ItemKind) || MayCollectSharedFood(actor)) &&
             person.Position == HouseholdStockPosition(lot));

    private void AddHandcartCandidates(List<CognitionCandidate> candidates, string actor,
        PlaytestInhabitantState person)
    {
        var inventory = society.Checkpoint.Inventory;
        if (HandcartToBuild(actor) is { } craftRecipe)
        {
            foreach (var input in craftRecipe.Inputs)
            {
                if (CarriedMaterialQuantity(actor, input.ResourceId) < input.Amount && FreeCarryCapacity(actor) > 0 &&
                    SharedItem(input.ResourceId, actor) is not null)
                    candidates.Add(new(CollectCartMaterialPrefix + input.ResourceId,
                        $"Collect nearby {input.ResourceId.Replace('_', ' ')} and carry it to build a handcart at your household Blacksmith.", 25));
            }
        }
        if (AttachedHandcart(actor) is null && !animalWorld.Animals.Any(animal => animal.RiderId == actor || animal.LeaderId == actor))
        {
            foreach (var cart in inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.Handcart &&
                         lot.OwnerId == actor && CanPullHandcart(lot)).OrderBy(lot => lot.Id, StringComparer.Ordinal))
            {
                var position = new GridPoint(cart.GroundPosition!.Value.X, cart.GroundPosition.Value.Y);
                if (position == person.Position || FindUnoccupiedRoute(actor, person.Position, position, 0).Count > 0)
                    candidates.Add(new(AttachCartPrefix + cart.Id,
                        "Reach and attach your parked handcart to carry a larger load.", UnassignedCartPriority));
            }
        }
        else if (AttachedHandcart(actor) is not null)
        {
            candidates.Add(new("park_handcart", "Park the handcart here with its cargo kept inside.", UnassignedCartPriority));
            foreach (var building in worldSimulation.Buildings.OrderBy(item => item.InstanceId, StringComparer.Ordinal))
            {
                if (building.Position == person.Position) continue;
                var reachable = FindUnoccupiedRoute(actor, person.Position, building.Position, 0).Count > 0;
                candidates.Add(new(PullCartPrefix + building.InstanceId,
                    reachable ? "Pull the handcart and its cargo to this building along a legal route." :
                        "The cart's route to this building is blocked. Wait for a route or park the cart here.",
                    UnassignedCartPriority, building.InstanceId));
            }
        }
        var worn = inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.Handcart &&
            lot.OwnerId == actor && lot.ConditionBasisPoints < 10_000 &&
            !HasActiveContainerReservation(inventory, lot.Id)).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        if (worn.Length > 0)
        {
            foreach (var kind in CartRepairKinds)
                if (!HasCarriedMaterial(actor, kind, 1) && FreeCarryCapacity(actor) > 0 && SharedItem(kind, actor) is not null)
                    candidates.Add(new(CollectCartRepairPrefix + kind,
                        $"Collect physically accessible {kind.Replace('_', ' ')} to repair your handcart.", 21));
            if (CartRepairKinds.All(kind => HasCarriedMaterial(actor, kind, 1)))
                foreach (var cart in worn)
                {
                    var site = new GridPoint(cart.GroundPosition!.Value.X, cart.GroundPosition.Value.Y);
                    if (site == person.Position || FindUnoccupiedRoute(actor, person.Position, site, 0).Count > 0)
                        candidates.Add(new(RepairCartPrefix + cart.Id,
                            "Reach and repair your handcart using one carried wood, iron fittings and rope.", 18));
                }
        }
        if (NearbyOwnedHandcart(actor, person.Position) is not { } nearby) return;
        var cargo = inventory.Lots.Where(lot => lot.ContainerLotId == nearby.Id).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        var room = InventoryContainerRules.HandcartCapacity - cargo.Sum(lot => lot.Quantity);
        if (room > 0 && CanPullHandcart(nearby))
            foreach (var lot in inventory.Lots.Where(lot => CanLoadCartLot(actor, person, lot)).OrderBy(lot => lot.Id, StringComparer.Ordinal))
                candidates.Add(new(LoadCartPrefix + lot.Id,
                    $"Load up to {Math.Min(room, AvailableLotQuantity(lot))} nearby {lot.ItemKind.Replace('_', ' ')} into your handcart.", UnassignedCartPriority));
        foreach (var cart in NearbyOwnedHandcarts(actor, person.Position))
        {
            if (HasActiveContainerReservation(inventory, cart.Id)) continue;
            foreach (var lot in inventory.Lots.Where(lot => lot.ContainerLotId == cart.Id).OrderBy(lot => lot.Id, StringComparer.Ordinal))
            {
                if (FreeCarryCapacity(actor) > 0)
                    candidates.Add(new(UnloadCartPrefix + lot.Id, "Unload cart goods into your carried load, within your carrying limit.", UnassignedCartPriority));
                candidates.Add(new(UnloadCartGroundPrefix + lot.Id, "Unload cart goods onto the ground here, keeping your ownership.", UnassignedCartPriority));
            }
        }
        if (AttachedHandcart(actor) is null && !HasActiveContainerReservation(inventory, nearby.Id))
            foreach (var recipient in inhabitants.Values.Where(item => item.InhabitantId != actor &&
                         AdultResident(item.InhabitantId) && IsWithinInteractionRange(person.Position, item.Position, 1))
                         .OrderBy(item => item.InhabitantId, StringComparer.Ordinal))
                candidates.Add(new(TransferCartPrefix + recipient.InhabitantId,
                    $"Give your parked handcart and its cargo to {society.Checkpoint.GetInhabitant(recipient.InhabitantId).Name} here.", UnassignedCartPriority));
    }

    private bool ApplyHandcartCandidate(string actor, PlaytestInhabitantState person, string candidateId)
    {
        if (candidateId.StartsWith(CollectCartMaterialPrefix, StringComparison.Ordinal))
        {
            var kind = candidateId[CollectCartMaterialPrefix.Length..];
            var input = HandcartToBuild(actor)?.Inputs.FirstOrDefault(item => item.ResourceId == kind);
            if (input is null || input.Value.Amount <= 0 || SharedItem(kind, actor) is not { } stock) return true;
            var source = HouseholdStockPosition(stock);
            if (!IsWithinInteractionRange(person.Position, source, HouseholdStockInteractionRange(stock)))
            { MoveToward(actor, person, source, "handcart_material", HouseholdStockInteractionRange(stock)); return true; }
            var quantity = Math.Min(input.Value.Amount - CarriedMaterialQuantity(actor, kind),
                Math.Min(AvailableLotQuantity(stock), FreeCarryCapacity(actor)));
            if (quantity <= 0) return true;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"cart-material:{WorldTick}:{actor}",
                stock.OwnerId, actor, stock.Id, quantity, "handcart_material_collected"));
            return true;
        }
        if (candidateId.StartsWith(CollectCartRepairPrefix, StringComparison.Ordinal))
        {
            var kind = candidateId[CollectCartRepairPrefix.Length..];
            if (CartRepairKinds.Contains(kind, StringComparer.Ordinal) && !HasCarriedMaterial(actor, kind, 1) &&
                society.Checkpoint.Inventory.Lots.Any(lot => lot.ItemKind == InventoryContainerRules.Handcart &&
                    lot.OwnerId == actor && lot.ConditionBasisPoints < 10_000))
                CollectEquipment(actor, person, kind);
            return true;
        }
        if (candidateId.StartsWith(AttachCartPrefix, StringComparison.Ordinal))
        {
            var id = candidateId[AttachCartPrefix.Length..];
            var cart = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == id &&
                lot.ItemKind == InventoryContainerRules.Handcart && lot.OwnerId == actor);
            if (cart is null || AttachedHandcart(actor) is not null || !CanPullHandcart(cart) ||
                animalWorld.Animals.Any(animal => animal.RiderId == actor || animal.LeaderId == actor)) return true;
            var position = new GridPoint(cart.GroundPosition!.Value.X, cart.GroundPosition.Value.Y);
            if (person.Position != position) { MoveToward(actor, person, position, "attach_handcart"); return true; }
            if (handcartHitches.Any(hitch => hitch.CartLotId == id)) return true;
            handcartHitches.Add(new(id, actor));
            AppendEvent("handcart_attached", $"{actor}:{id}");
            return true;
        }
        if (candidateId == "park_handcart") { ParkHandcart(actor); return true; }
        if (candidateId.StartsWith(PullCartPrefix, StringComparison.Ordinal))
        {
            var building = worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == candidateId[PullCartPrefix.Length..]);
            if (AttachedHandcart(actor) is not null && building is not null)
                MoveToward(actor, person, building.Position, "pull_handcart");
            return true;
        }
        var isLoad = candidateId.StartsWith(LoadCartPrefix, StringComparison.Ordinal);
        var ontoGround = candidateId.StartsWith(UnloadCartGroundPrefix, StringComparison.Ordinal);
        var isUnload = ontoGround || candidateId.StartsWith(UnloadCartPrefix, StringComparison.Ordinal);
        var isRepair = candidateId.StartsWith(RepairCartPrefix, StringComparison.Ordinal);
        var isTransfer = candidateId.StartsWith(TransferCartPrefix, StringComparison.Ordinal);
        if (!isLoad && !isUnload && !isRepair && !isTransfer) return false;
        var cartHere = isRepair ? society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
            lot.Id == candidateId[RepairCartPrefix.Length..] && lot.OwnerId == actor &&
            lot.ItemKind == InventoryContainerRules.Handcart) : isUnload
            ? HandcartForCargoAt(actor, person.Position, candidateId[(ontoGround ? UnloadCartGroundPrefix : UnloadCartPrefix).Length..])
            : NearbyOwnedHandcart(actor, person.Position);
        if (isRepair && cartHere is { GroundPosition: { } repairSite } &&
            person.Position != new GridPoint(repairSite.X, repairSite.Y))
        {
            MoveToward(actor, person, new(repairSite.X, repairSite.Y), "repair_handcart");
            return true;
        }
        if (cartHere is null || HasActiveContainerReservation(society.Checkpoint.Inventory, cartHere.Id))
            return true;
        if (isLoad)
        {
            var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == candidateId[LoadCartPrefix.Length..]);
            if (lot is null || !CanLoadCartLot(actor, person, lot) || !CanPullHandcart(cartHere)) return true;
            var room = InventoryContainerRules.HandcartCapacity - society.Checkpoint.Inventory.Lots
                .Where(item => item.ContainerLotId == cartHere.Id).Sum(item => item.Quantity);
            var available = AvailableLotQuantity(lot);
            if (lot.OwnerId != actor && IsEdibleFood(lot.ItemKind))
                available = Math.Min(available, SharedFoodCollectionAllowance(actor));
            var quantity = Math.Min(room, available);
            if (quantity <= 0) return true;
            ApplyInventoryTransition(inventory =>
            {
                var updated = inventory;
                var cargoId = lot.Id;
                if (lot.OwnerId != actor)
                {
                    var operation = $"cart-pickup:{WorldTick}:{actor}";
                    updated = InventoryFixture.Transfer(updated, operation, lot.OwnerId, actor, lot.Id, quantity,
                        "handcart_pickup", destinationGroundPosition: cartHere.GroundPosition);
                    cargoId = quantity == lot.Quantity ? lot.Id : $"{lot.Id}#transfer:{operation}";
                }
                return InventoryFixture.LoadHandcart(updated, $"load:{WorldTick}:{actor}", actor, cartHere.Id, cargoId, quantity);
            });
            AppendEvent("handcart_loaded", $"{actor}:{cartHere.Id}:{lot.ItemKind}:{quantity}");
        }
        else if (isUnload)
        {
            var prefix = ontoGround ? UnloadCartGroundPrefix : UnloadCartPrefix;
            var cargo = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == candidateId[prefix.Length..] && lot.ContainerLotId == cartHere.Id);
            if (cargo is null) return true;
            var quantity = ontoGround ? cargo.Quantity : Math.Min(cargo.Quantity, FreeCarryCapacity(actor));
            if (quantity <= 0) return true;
            ApplyInventoryTransition(inventory => InventoryFixture.UnloadHandcart(inventory, $"unload:{WorldTick}:{actor}",
                actor, cartHere.Id, cargo.Id, quantity, ontoGround));
            AppendEvent("handcart_unloaded", $"{actor}:{cartHere.Id}:{cargo.ItemKind}:{quantity}");
        }
        else if (isRepair)
        {
            if (cartHere.Id != candidateId[RepairCartPrefix.Length..] || cartHere.ConditionBasisPoints >= 10_000 ||
                !HasCarriedMaterial(actor, "wood", 1) || !HasCarriedMaterial(actor, "iron_fittings", 1) || !HasCarriedMaterial(actor, "rope", 1)) return true;
            var reservations = new List<string>();
            ApplyInventoryTransition(inventory =>
            {
                var updated = inventory;
                foreach (var kind in CartRepairKinds)
                {
                    var material = updated.Lots.Where(lot => lot.ItemKind == kind && ToolProgressionRules.IsTopLevelCarriedLot(lot, actor) &&
                        lot.OwnerId == actor && PersonalEquipmentRules.AvailableQuantity(updated, lot) > 0).OrderBy(lot => lot.Id, StringComparer.Ordinal).First();
                    var id = $"cart-repair:{WorldTick}:{actor}:{kind}";
                    updated = InventoryFixture.Reserve(updated, id, actor, material.Id, 1, "equipment_repair", WorldTick + 1);
                    reservations.Add(id);
                }
                return InventoryFixture.RepairSingleUnit(updated, cartHere.Id, 10_000, reservations);
            });
            RecordNonviolentRepairCompletion(actor, cartHere, cartHere.Id,
                $"repair-handcart:{WorldTick}:{actor}:{cartHere.Id}", person.Position, reservations);
            AppendEvent("handcart_repaired", $"{actor}:{cartHere.Id}");
        }
        else if (isTransfer && AttachedHandcart(actor) is null)
        {
            var recipient = candidateId[TransferCartPrefix.Length..];
            if (!inhabitants.TryGetValue(recipient, out var other) || !AdultResident(recipient) ||
                !IsWithinInteractionRange(person.Position, other.Position, 1)) return true;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"cart-gift:{WorldTick}:{actor}",
                actor, recipient, cartHere.Id, 1, "handcart_given", destinationGroundPosition: cartHere.GroundPosition));
            AppendEvent("handcart_transferred", $"{actor}:{cartHere.Id}:{recipient}");
        }
        return true;
    }

    private static void ValidateHandcarts(IReadOnlyList<HandcartHitch> hitches, InventoryCheckpoint inventory,
        IReadOnlyList<PlaytestInhabitantState> people, SeededMap travelMap)
    {
        if (hitches.Any(item => item is null) || hitches.Select(item => item.CartLotId).Distinct(StringComparer.Ordinal).Count() != hitches.Count ||
            hitches.Select(item => item.PullerId).Distinct(StringComparer.Ordinal).Count() != hitches.Count)
            throw new InvalidDataException("Cart attachments must be distinct for both carts and pullers.");
        foreach (var cart in inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.Handcart))
            if (cart.GroundPosition is not { } ground || !travelMap.IsPassable(new(ground.X, ground.Y)))
                throw new InvalidDataException("A cart must remain on a legal ground tile.");
        foreach (var hitch in hitches)
        {
            var cart = inventory.Lots.FirstOrDefault(lot => lot.Id == hitch.CartLotId);
            var person = people.FirstOrDefault(item => item.InhabitantId == hitch.PullerId);
            if (cart is null || cart.ItemKind != InventoryContainerRules.Handcart || cart.OwnerId != hitch.PullerId ||
                cart.ConditionBasisPoints <= 0 || person is null ||
                cart.GroundPosition != new InventoryGroundPosition(person.Position.X, person.Position.Y))
                throw new InvalidDataException("A cart attachment needs its living owner at the cart's physical position.");
        }
    }
}
