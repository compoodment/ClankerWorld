using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed record PhysicalCart(string Id, string LotId, GridPoint Position, string? PullerId = null);
public sealed record CartActionResult(bool Applied, string? Failure = null);

public sealed partial class PrivateWorldRuntime
{
    private static readonly string[] CartRepairMaterials = ["wood", "iron"];
    private IReadOnlyList<PhysicalCart> Carts => worldSimulation.Carts ?? [];
    private void SetCart(PhysicalCart cart) => worldSimulation = worldSimulation with
    {
        Carts = Carts.Where(item => item.Id != cart.Id).Append(cart).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
    };
    private bool MayUseCart(string actor, PhysicalCart cart) => society.Checkpoint.Inventory.GetLot(cart.LotId).OwnerId is { } owner &&
        (owner == actor || owner == HouseholdFor(actor));
    private bool MayMoveCargo(string actor, InventoryLot lot) => lot.OwnerId == actor || lot.OwnerId == HouseholdFor(actor) ||
        lot.OwnerId == TownForResident(actor);
    private long CartLoad(string cartId) => society.Checkpoint.Inventory.Lots.Where(lot => lot.CartId == cartId).Sum(lot => (long)lot.Quantity);
    private PhysicalCart? PulledCart(string actor) => Carts.FirstOrDefault(cart => cart.PullerId == actor);
    private bool NeedsCartOutput(string? household) => household is not null && !society.Checkpoint.Inventory.Lots.Any(lot =>
        lot.ItemKind == "handcart" && (lot.OwnerId == household || inhabitants.ContainsKey(lot.OwnerId) && HouseholdFor(lot.OwnerId) == household));

    private CartActionResult CartOperation(Func<CartActionResult> action)
    {
        gate.Wait();
        try { return action(); }
        finally { gate.Release(); }
    }

    public CartActionResult DeployCart(string actor, string lotId) => CartOperation(() => DeployCartCore(actor, lotId));
    public CartActionResult PullCart(string actor, string cartId) => CartOperation(() => PullCartCore(actor, cartId));
    public CartActionResult ParkCart(string actor) => CartOperation(() => ParkCartCore(actor));
    public CartActionResult LoadCartGoods(string actor, string cartId, string lotId, int quantity) =>
        CartOperation(() => LoadCartGoodsCore(actor, cartId, lotId, quantity));
    public CartActionResult UnloadCartGoods(string actor, string cartId, string lotId, int quantity, string? buildingId = null) =>
        CartOperation(() => UnloadCartGoodsCore(actor, cartId, lotId, quantity, buildingId));
    public CartActionResult RepairCart(string actor, string cartId) => CartOperation(() => RepairCartCore(actor, cartId));
    public CartActionResult GiveCart(string actor, string cartId, string recipient) => CartOperation(() => GiveCartCore(actor, cartId, recipient));
    public CartActionResult PutDownCartCargo(string actor, string cartId, string lotId) =>
        CartOperation(() => PutDownCartCargoCore(actor, cartId, lotId));

    private CartActionResult PutDownCartCargoCore(string actor, string cartId, string lotId)
    {
        var cart = Carts.FirstOrDefault(item => item.Id == cartId);
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId);
        if (!AdultResident(actor) || cart is null || cart.Position != inhabitants[actor].Position ||
            lot is null || lot.CartId != cartId || lot.ContainerLotId is not null || !MayMoveCargo(actor, lot))
            return new(false, "Stand beside accessible cargo in its actual cart.");
        if (society.Checkpoint.Inventory.Reservations.Any(reservation =>
            (reservation.LotId == lotId || society.Checkpoint.Inventory.Lots.Any(child =>
                child.Id == reservation.LotId && child.ContainerLotId == lotId)) &&
            reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed))
            return new(false, "Finish the cargo's reserved use before putting it down.");
        var position = map.FootNeighbors(cart.Position).Where(point => map.IsPassable(point) &&
            !Carts.Any(item => item.Position == point) && !FarmFields.Any(field => field.Position == point) &&
            !map.Resources.Any(item => item.Position == point) && !map.CampObjects.Any(item => item.Position == point) &&
            !worldSimulation.Buildings.Any(building => WorldContentSimulationRules.Footprint(
                worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building).Contains(point)))
            .OrderBy(point => point.Y).ThenBy(point => point.X).Select(point => (GridPoint?)point).FirstOrDefault();
        if (position is not { } ground) return new(false, "There is no free ground beside the cart for this cargo.");
        ApplyInventoryTransition(inventory => InventoryFixture.PutDownCartCargo(inventory, lotId, lot.OwnerId,
            cartId, new(ground.X, ground.Y)));
        AppendEvent("cart_cargo_put_down", $"{actor}|{cartId}|{lotId}|{ground.X},{ground.Y}");
        return new(true);
    }

    private CartActionResult DeployCartCore(string actor, string lotId)
    {
        if (!AdultResident(actor)) return new(false, "An adult must collect the finished cart.");
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId && item.ItemKind == "handcart" &&
            item.ContainerLotId is null && item.CartId is null && item.GroundPosition is null && item.DeliveryBuildingId is null &&
            (item.OwnerId == actor || item.OwnerId == HouseholdFor(actor)) && AvailableLotQuantity(item) > 0);
        if (lot is null) return new(false, "Collect an owned, usable, unreserved cart from its workshop.");
        var position = lot.StorageBuildingId is null ? inhabitants[actor].Position : HouseholdStockPosition(lot);
        if (inhabitants[actor].Position != position) return new(false, "Stand at the cart's actual stock location first.");
        if (Carts.Any(cart => cart.Position == position)) return new(false, "Move the existing cart before deploying another here.");
        var id = lot.Id;
        if (lot.Quantity > 1)
        {
            id += $"#deployed:{WorldTick}:{nextEventId}";
            ApplyInventoryTransition(inventory => InventoryFixture.SplitLot(inventory, lot.Id, 1, id));
        }
        ApplyInventoryTransition(inventory => inventory with
        {
            Lots = inventory.Lots.Select(item => item.Id == id ? item with
            { StorageBuildingId = null, GroundPosition = new(position.X, position.Y) } : item).ToArray(),
        });
        SetCart(new("cart:" + id, id, position));
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("cart_deployed", $"{actor}|cart:{id}|{position.X},{position.Y}");
        return new(true);
    }

    private CartActionResult PullCartCore(string actor, string cartId)
    {
        var cart = Carts.FirstOrDefault(item => item.Id == cartId);
        if (!AdultResident(actor) || cart is null || !MayUseCart(actor, cart)) return new(false, "This adult cannot use that cart.");
        if (PassengerBoat(actor) is not null || livestock.Any(animal => animal.RiderId == actor))
            return new(false, "Leave the boat or dismount the horse before pulling a cart.");
        if (PulledCart(actor) is not null || cart.PullerId is not null) return new(false, "Only one person can pull one cart at a time.");
        if (cart.Position != inhabitants[actor].Position) return new(false, "Walk to the parked cart first.");
        if (AvailableLotQuantity(society.Checkpoint.Inventory.GetLot(cart.LotId)) != 1) return new(false, "Repair the cart and finish its reserved uses first.");
        SetCart(cart with { PullerId = actor });
        AppendEvent("cart_attached", $"{actor}|{cart.Id}");
        return new(true);
    }

    private CartActionResult ParkCartCore(string actor)
    {
        if (PulledCart(actor) is not { } cart) return new(false, "This agent is not pulling a cart.");
        SetCart(cart with { PullerId = null });
        AppendEvent("cart_parked", $"{actor}|{cart.Id}|{cart.Position.X},{cart.Position.Y}");
        return new(true);
    }

    private CartActionResult LoadCartGoodsCore(string actor, string cartId, string lotId, int quantity)
    {
        var cart = Carts.FirstOrDefault(item => item.Id == cartId);
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId);
        if (!AdultResident(actor) || cart is null || !MayUseCart(actor, cart) || cart.Position != inhabitants[actor].Position ||
            cart.PullerId is not null && cart.PullerId != actor) return new(false, "Bring an available owned cart to the goods first.");
        if (lot is null || !MayMoveCargo(actor, lot) || lot.ContainerLotId is not null || lot.CartId is not null ||
            lot.ItemKind == "handcart" || lot.DeliveryBuildingId is not null || IsEquippedLot(actor, lot.Id) ||
            quantity <= 0 || quantity > AvailableLotQuantity(lot)) return new(false, "Load unreserved owned goods or a whole vessel.");
        var sourcePosition = lot.GroundPosition is { } ground ? new GridPoint(ground.X, ground.Y)
            : lot.StorageBuildingId is not null ? HouseholdStockPosition(lot)
            : inhabitants.TryGetValue(lot.OwnerId, out var carrier) ? carrier.Position : SettlementStoragePosition;
        if (sourcePosition != cart.Position) return new(false, "The cart must stand at the goods' actual location.");
        var load = InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, quantity);
        if (load + CartLoad(cartId) > CartContent.Capacity) return new(false, "The cart has no room for that load.");
        ApplyInventoryTransition(inventory => InventoryFixture.LoadCart(inventory, $"{WorldTick}:{nextEventId}:{actor}",
            lot.OwnerId, lot.Id, quantity, cart.Id, new(cart.Position.X, cart.Position.Y)));
        AppendEvent("cart_loaded", $"{actor}|{cart.Id}|{lot.ItemKind}|{quantity}");
        return new(true);
    }

    private CartActionResult UnloadCartGoodsCore(string actor, string cartId, string lotId, int quantity, string? buildingId)
    {
        var cart = Carts.FirstOrDefault(item => item.Id == cartId);
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == lotId);
        if (!AdultResident(actor) || cart is null || cart.Position != inhabitants[actor].Position || lot is null ||
            !MayMoveCargo(actor, lot) || lot.CartId != cartId || lot.ContainerLotId is not null ||
            quantity <= 0 || quantity > AvailableLotQuantity(lot)) return new(false, "Stand beside the cart and select accessible unreserved cargo.");
        var recipient = actor;
        if (buildingId is not null)
        {
            var building = worldSimulation.Buildings.FirstOrDefault(item => item.InstanceId == buildingId);
            if (building is null || building.Position != cart.Position || building.HouseholdId != HouseholdFor(actor) &&
                !(building.HouseholdId is null && building.TownId == TownForResident(actor) && !WarehouseFoodKinds.Contains(lot.ItemKind) &&
                    worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("warehouse", StringComparer.Ordinal)))
                return new(false, "Bring the cart to accessible household storage or the Town Warehouse for suitable goods.");
            recipient = building.HouseholdId ?? building.TownId!;
        }
        var load = InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, quantity);
        if (load > (buildingId is null ? CarryingRoom(actor) : StorageRoom(buildingId))) return new(false, "The destination needs room for the goods and vessel contents.");
        ApplyInventoryTransition(inventory => InventoryFixture.UnloadCart(inventory, $"{WorldTick}:{nextEventId}:{actor}",
            lot.OwnerId, recipient, lot.Id, quantity, cartId, buildingId));
        AppendEvent("cart_unloaded", $"{actor}|{cartId}|{lot.ItemKind}|{quantity}|{buildingId ?? "carried"}");
        return new(true);
    }

    private CartActionResult RepairCartCore(string actor, string cartId)
    {
        var cart = Carts.FirstOrDefault(item => item.Id == cartId);
        if (!AdultResident(actor) || cart is null || !MayUseCart(actor, cart) || cart.Position != inhabitants[actor].Position ||
            cart.PullerId is not null && cart.PullerId != actor) return new(false, "Bring repair materials to this owned cart.");
        var body = society.Checkpoint.Inventory.GetLot(cart.LotId);
        if (body.ConditionBasisPoints == 10_000) return new(false, "This cart does not need repair.");
        if (society.Checkpoint.Inventory.Reservations.Any(reservation => reservation.LotId == body.Id &&
            reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed))
            return new(false, "Finish the cart's reserved use before repairing it.");
        var materials = CartRepairMaterials.Select(kind => society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
            lot.ItemKind == kind && lot.OwnerId == actor && lot.StorageBuildingId is null && lot.GroundPosition is null &&
            lot.CartId is null && lot.ContainerLotId is null && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0)).ToArray();
        if (materials.Any(lot => lot is null)) return new(false, "Carry one wood and one iron to repair the cart.");
        ApplyInventoryTransition(inventory =>
        {
            foreach (var material in materials)
            {
                var id = $"cart-repair:{WorldTick}:{nextEventId}:{actor}:{material!.ItemKind}";
                inventory = InventoryFixture.ConsumeReservation(InventoryFixture.Reserve(inventory, id, actor,
                    material.Id, 1, "cart_repair", WorldTick), id);
            }
            return InventoryFixture.RestoreCartCondition(inventory, body.Id, body.OwnerId);
        });
        AppendEvent("cart_repaired", $"{actor}|{cart.Id}");
        return new(true);
    }

    private CartActionResult GiveCartCore(string actor, string cartId, string recipient)
    {
        var cart = Carts.FirstOrDefault(item => item.Id == cartId);
        if (!AdultResident(actor) || cart is null || !MayUseCart(actor, cart) || cart.Position != inhabitants[actor].Position ||
            cart.PullerId is not null || !inhabitants.TryGetValue(recipient, out var person) ||
            society.Checkpoint.GetInhabitant(recipient).Status != SocietyInhabitantStatus.Active ||
            !IsWithinInteractionRange(cart.Position, person.Position, 1)) return new(false, "Park the owned cart beside its living recipient first.");
        var body = society.Checkpoint.Inventory.GetLot(cart.LotId);
        var contents = society.Checkpoint.Inventory.Lots.Where(lot => lot.CartId == cart.Id).ToArray();
        if (contents.Any(lot => lot.OwnerId != body.OwnerId || AvailableLotQuantity(lot) != lot.Quantity) || AvailableLotQuantity(body) != 1)
            return new(false, "Withdraw other owners' goods and finish reserved uses before giving the cart.");
        ApplyInventoryTransition(inventory => inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == cart.LotId || lot.CartId == cart.Id ? lot with { OwnerId = recipient } : lot).ToArray(),
        });
        AppendEvent("cart_given", $"{actor}|{recipient}|{cart.Id}");
        return new(true);
    }

    private bool MayPullCartStep(string actor, GridPoint origin, GridPoint next) => PulledCart(actor) is not { } cart ||
        cart.Position == origin && MayUseCart(actor, cart) && AvailableLotQuantity(society.Checkpoint.Inventory.GetLot(cart.LotId)) == 1 &&
        map.IsPassable(next) && !Carts.Any(item => item.Id != cart.Id && item.Position == next);
    private int CartTravelDelay(string actor, GridPoint origin, GridPoint next) => PulledCart(actor) is not null && RoadStepCost(origin, next) >= 100 ? 1 : 0;
    private void MovePulledCart(string actor, GridPoint next)
    {
        if (PulledCart(actor) is not { } cart) return;
        ApplyInventoryTransition(inventory => inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == cart.LotId || lot.CartId == cart.Id ? lot with
            {
                GroundPosition = new(next.X, next.Y),
                ConditionBasisPoints = lot.Id == cart.LotId ? Math.Max(0, lot.ConditionBasisPoints - CartContent.WearPerStep) : lot.ConditionBasisPoints,
            } : lot).ToArray(),
        });
        SetCart(cart with { Position = next });
        AppendEvent("cart_moved", $"{actor}|{cart.Id}|{next.X},{next.Y}");
    }
    private void ReleaseDeadCartPullers()
    {
        foreach (var cart in Carts.Where(item => item.PullerId is { } actor && (!AdultResident(actor) || !MayUseCart(actor, item))).ToArray())
            SetCart(cart with { PullerId = null });
    }

    private void AddCartCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor)) return;
        var finished = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.ItemKind == "handcart" &&
            lot.GroundPosition is null && (lot.OwnerId == actor || lot.OwnerId == HouseholdFor(actor)) && AvailableLotQuantity(lot) > 0);
        if (finished is not null) candidates.Add(new("cart_deploy:" + finished.Id, "Collect the finished household cart from its workshop.", 28, finished.StorageBuildingId));
        if (PulledCart(actor) is { } cart)
        {
            var unusable = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.CartId == cart.Id &&
                lot.ContainerLotId is null && MayMoveCargo(actor, lot) && (lot.FreshnessBasisPoints == 0 || lot.ConditionBasisPoints == 0));
            if (unusable is not null) candidates.Add(new("cart_put_down:" + unusable.Id, "Put spoiled or broken cargo on nearby ground with its ownership intact.", 19));
            candidates.Add(new("cart_park", "Park the cart here with its cargo aboard.", 110));
            if (society.Checkpoint.Inventory.GetLot(cart.LotId).ConditionBasisPoints <= 2_500)
                candidates.Add(new("cart_repair:" + cart.Id, "Bring wood and iron to repair the worn cart.", 18));
            var cargo = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.CartId == cart.Id && lot.ContainerLotId is null && MayMoveCargo(actor, lot) && AvailableLotQuantity(lot) > 0);
            if (cargo is not null && CartDeliveryBuilding(actor, cargo) is { } destination)
                candidates.Add(new("cart_deliver:" + cargo.Id, "Pull the cart's real cargo into accessible household storage.", 20, destination.InstanceId));
            var goods = CartLoadableGoods(actor, cart);
            if (goods is not null) candidates.Add(new("cart_load:" + goods.Id, "Load the goods here into the cart without losing their ownership.", 21));
        }
        else
        {
            foreach (var worn in Carts.Where(cart => cart.PullerId is null && MayUseCart(actor, cart) &&
                society.Checkpoint.Inventory.GetLot(cart.LotId).ConditionBasisPoints <= 2_500))
                candidates.Add(new("cart_repair:" + worn.Id, "Bring wood and iron to the parked cart for repair.", 18));
            var parked = Carts.Where(cart => cart.PullerId is null && MayUseCart(actor, cart) &&
                society.Checkpoint.Inventory.GetLot(cart.LotId).ConditionBasisPoints > 0 &&
                FindUnoccupiedRoute(actor, inhabitants[actor].Position, cart.Position, 0).Count > 0)
                .OrderBy(cart => map.FootDistance(inhabitants[actor].Position, cart.Position)).FirstOrDefault();
            if (parked is not null) candidates.Add(new("cart_pull:" + parked.Id, "Walk to the household cart and start pulling it.", 29));
        }
    }

    private InventoryLot? CartLoadableGoods(string actor, PhysicalCart cart) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == actor && lot.StorageBuildingId is null && lot.GroundPosition is null &&
            lot.ContainerLotId is null && lot.CartId is null && lot.DeliveryBuildingId is null && !IsEquippedLot(actor, lot.Id) &&
            ToolCapabilities.ForItem(lot.ItemKind) is null && !CarryEquipmentRules.IsClothing(lot.ItemKind) &&
            !CarryEquipmentRules.IsCarryAid(lot.ItemKind) && lot.ItemKind != "handcart" &&
            AvailableLotQuantity(lot) >= (VesselRules.IsVessel(lot.ItemKind) ? 1 : 2) &&
            CartLoad(cart.Id) < CartContent.Capacity).OrderByDescending(lot => lot.Quantity).ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
    private PlacedBuilding? CartDeliveryBuilding(string actor, InventoryLot lot)
    {
        if (HouseholdFor(actor) is not { } household) return null;
        var preferred = worldSimulation.Buildings.Where(building => building.HouseholdId == household && StorageRoom(building.InstanceId) > 0 &&
            worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains(
                FoodItems.IsFarmStock(lot.ItemKind) ? "silo" : "house", StringComparer.Ordinal))
            .OrderBy(building => map.FootDistance(inhabitants[actor].Position, building.Position)).FirstOrDefault();
        if (preferred is not null) return preferred;
        return HouseForHousehold(household) is { } house && StorageRoom(house.InstanceId) > 0 ? house : null;
    }

    private void ApplyCartCandidate(string actor, PlaytestInhabitantState person, string candidate)
    {
        if (candidate == "cart_park") { _ = ParkCartCore(actor); return; }
        var target = candidate[(candidate.IndexOf(':') + 1)..];
        if (candidate.StartsWith("cart_put_down:", StringComparison.Ordinal) && PulledCart(actor) is { } cargoCart)
            _ = PutDownCartCargoCore(actor, cargoCart.Id, target);
        else if (candidate.StartsWith("cart_deploy:", StringComparison.Ordinal))
        {
            var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == target);
            if (lot is null) return;
            var position = lot.StorageBuildingId is null ? person.Position : HouseholdStockPosition(lot);
            if (person.Position != position) MoveToward(actor, person, position, "cart_collect", 0);
            else _ = DeployCartCore(actor, lot.Id);
        }
        else if (candidate.StartsWith("cart_pull:", StringComparison.Ordinal))
        {
            var cart = Carts.FirstOrDefault(item => item.Id == target);
            if (cart is null) return;
            if (person.Position != cart.Position) MoveToward(actor, person, cart.Position, "cart_collect", 0);
            else _ = PullCartCore(actor, target);
        }
        else if (candidate.StartsWith("cart_load:", StringComparison.Ordinal) && PulledCart(actor) is { } cart)
        {
            var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == target);
            if (lot is null) return;
            var quantity = Math.Min(AvailableLotQuantity(lot) - 1, checked((int)(CartContent.Capacity - CartLoad(cart.Id))));
            if (VesselRules.IsVessel(lot.ItemKind)) quantity = 1;
            if (quantity > 0) _ = LoadCartGoodsCore(actor, cart.Id, lot.Id, quantity);
        }
        else if (candidate.StartsWith("cart_deliver:", StringComparison.Ordinal) && PulledCart(actor) is { } pulled)
        {
            var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == target && item.CartId == pulled.Id);
            if (lot is null || CartDeliveryBuilding(actor, lot) is not { } building) return;
            if (person.Position != building.Position) MoveToward(actor, person, building.Position, "cart_delivery", 0);
            else
            {
                var quantity = Math.Min(AvailableLotQuantity(lot), StorageRoom(building.InstanceId));
                if (VesselRules.IsVessel(lot.ItemKind)) quantity = 1;
                if (quantity > 0) _ = UnloadCartGoodsCore(actor, pulled.Id, lot.Id, quantity, building.InstanceId);
            }
        }
        else if (candidate.StartsWith("cart_repair:", StringComparison.Ordinal))
        {
            foreach (var kind in CartRepairMaterials)
                if (!HasCarriedItem(actor, kind))
                {
                    if (PulledCart(actor) is not null) _ = ParkCartCore(actor);
                    CollectEquipment(actor, inhabitants[actor], kind);
                    return;
                }
            if (Carts.FirstOrDefault(cart => cart.Id == target) is { } repairCart && person.Position != repairCart.Position)
                MoveToward(actor, person, repairCart.Position, "cart_repair", 0);
            else _ = RepairCartCore(actor, target);
        }
    }

    private static void ValidateCarts(PrivateWorldRuntimeState state)
    {
        var carts = state.WorldSimulation?.Carts ?? [];
        if (carts.Any(cart => cart.PullerId is { } actor &&
            ((state.Livestock ?? []).Any(animal => animal.RiderId == actor) ||
             (state.BoatTransport?.Boats ?? []).Any(boat => boat.Journey?.PassengerId == actor))))
            throw new InvalidDataException("A cart puller cannot also ride a horse or board a boat.");
        var inventory = state.Society.Society.Inventory;
        var people = state.Society.Society.Inhabitants.ToDictionary(person => person.Id, StringComparer.Ordinal);
        var pullers = new HashSet<string>(StringComparer.Ordinal);
        if (carts.Select(cart => cart.Id).Distinct(StringComparer.Ordinal).Count() != carts.Count ||
            !carts.Select(cart => cart.Id).SequenceEqual(carts.Select(cart => cart.Id).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Physical carts must have distinct canonical identities.");
        foreach (var cart in carts)
        {
            var body = inventory.Lots.FirstOrDefault(lot => lot.Id == cart.LotId);
            if (body is null || body.ItemKind != "handcart" || body.Quantity != 1 || body.ContainerLotId is not null ||
                body.CartId is not null || body.StorageBuildingId is not null || body.DeliveryBuildingId is not null ||
                body.GroundPosition != new InventoryGroundPosition(cart.Position.X, cart.Position.Y) ||
                cart.Id != "cart:" + body.Id || !state.Map.IsPassable(cart.Position) ||
                cart.PullerId is { } actor && (!pullers.Add(actor) || !state.Inhabitants.Any(person => person.InhabitantId == actor && person.Position == cart.Position) ||
                    !people.TryGetValue(actor, out var puller) || puller.Status != SocietyInhabitantStatus.Active ||
                    puller.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
                    body.OwnerId != actor && body.OwnerId != puller.HouseholdId))
                throw new InvalidDataException("A physical cart has invalid body, location or puller.");
            if (inventory.Lots.Where(lot => lot.CartId == cart.Id).Sum(lot => (long)lot.Quantity) > CartContent.Capacity)
                throw new InvalidDataException("The cart exceeds its cargo capacity.");
        }
        foreach (var lot in inventory.Lots.Where(lot => lot.CartId is not null))
            if (!carts.Any(cart => cart.Id == lot.CartId && lot.GroundPosition == new InventoryGroundPosition(cart.Position.X, cart.Position.Y)))
                throw new InvalidDataException("Cart cargo must remain at its actual cart's saved position.");
        if (inventory.Lots.Any(lot => lot.ItemKind == "handcart" && lot.GroundPosition is not null &&
            !carts.Any(cart => cart.LotId == lot.Id)))
            throw new InvalidDataException("Every deployed cart body requires its actual cart state.");
        if (state.SchemaVersion < 32 && (carts.Count > 0 || inventory.Lots.Any(lot => lot.CartId is not null)))
            throw new InvalidDataException("Physical carts require private-world schema 32.");
    }
}
