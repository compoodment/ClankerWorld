using System.Reflection;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class AbandonedWarehouseConsumerTests
{
    private const string QuietTown = "town:quiet-yard";
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";

    [Theory]
    [InlineData("wooden_axe")]
    [InlineData("wooden_pickaxe")]
    [InlineData("wooden_hoe")]
    public void OrdinaryToolChoicesCollectOnlyTheUnreservedUnitAndPreserveItsCondition(string kind)
    {
        var (state, actor, _) = PreparedStock(kind, 2);
        state = WithReservation(state, "salvage-stock", 1);
        using var world = Reload(state);
        Assert.Contains(Candidates(world, actor), item => item.Id == "collect_" + kind);
        Choose(world, actor, "collect_" + kind);
        var carried = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == kind);
        Assert.Equal(1, carried.Quantity);
        Assert.Equal(7_600, carried.ConditionBasisPoints);
        Assert.Equal(8_300, carried.FreshnessBasisPoints);
        Assert.Null(carried.StorageBuildingId);
        Assert.Null(carried.DeliveryBuildingId);
        Assert.Null(carried.GroundPosition);
        Assert.Equal(1, world.Society.Inventory.GetLot("salvage-stock").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("keep-stock").State);
        Assert.Equal(1, world.Society.Inventory.GetReservation("keep-stock").Quantity);
        Assert.DoesNotContain(world.Towns.Single(item => item.Id == QuietTown).ResidentIds, id => id == actor);
        AssertRoundTrip(world);
    }

    [Theory]
    [InlineData("sack", "equip_carry_aid")]
    [InlineData("padded_coat", "wear_clothing")]
    public void OrdinaryEquipmentChoicesCollectAndEquipCommunalStockWithoutTakingPrivateGoods(string kind, string choice)
    {
        var (state, actor, _) = PreparedStock(kind, 1);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "private-alternative", kind, Beta, 1,
            storageBuildingId: "first-town-house-b");
        state = WithInventory(state, inventory) with
        {
            Survival = new SettlementSurvivalState(state.Society.Society.WorldTick, []),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Storm },
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 1, 0)).ToArray(),
                },
            },
        };
        using var world = Reload(state);
        Assert.Contains(Candidates(world, actor), item => item.Id == choice);
        Choose(world, actor, choice);
        var person = world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor);
        Assert.Equal("salvage-stock", kind == "sack" ? person.Equipment!.CarryAidLotId : person.Equipment!.ClothingLotId);
        var equipped = world.Society.Inventory.GetLot("salvage-stock");
        Assert.Equal(actor, equipped.OwnerId);
        Assert.Equal(7_600, equipped.ConditionBasisPoints);
        Assert.Equal(8_300, equipped.FreshnessBasisPoints);
        Assert.Null(equipped.StorageBuildingId);
        Assert.Equal(Beta, world.Society.Inventory.GetLot("private-alternative").OwnerId);
        AssertRoundTrip(world);
    }

    [Fact]
    public void WorkstationSupplyCollectsTownClothUnderTheActualOwnerAndPreservesReservedStock()
    {
        var (state, actor, _) = PreparedStock("cloth", 4);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "tailor-fiber", "fiber", Alpha, 6,
            storageBuildingId: "alpha-tailor");
        state = WithReservation(WithInventory(state, inventory), "salvage-stock", 1);
        using var world = Reload(state);
        Assert.Contains(Candidates(world, actor), item => item.Id == "supply_workstation:cloth");
        Choose(world, actor, "supply_workstation:cloth");
        var carried = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "cloth");
        Assert.Equal(3, carried.Quantity);
        Assert.Equal("alpha-tailor", carried.DeliveryBuildingId);
        Assert.Null(carried.StorageBuildingId);
        Assert.Equal(7_600, carried.ConditionBasisPoints);
        Assert.Equal(8_300, carried.FreshnessBasisPoints);
        Assert.Equal(1, world.Society.Inventory.GetLot("salvage-stock").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("keep-stock").State);
        AssertRoundTrip(world);
    }

    [Fact]
    public void BlacksmithSupplyCollectsTownOreUnderTheActualOwner()
    {
        var (state, actor, _) = PreparedStock("iron_ore", 2, Beta);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "smith-wood", "wood", Beta, 6,
            storageBuildingId: "first-town-blacksmith");
        state = WithInventory(state, inventory);
        using var world = Reload(state);
        Assert.Contains(Candidates(world, actor), item => item.Id == "haul_smith_input");
        Choose(world, actor, "haul_smith_input");
        var carried = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "iron_ore");
        Assert.Equal(2, carried.Quantity);
        Assert.Equal("first-town-blacksmith", carried.DeliveryBuildingId);
        Assert.Null(carried.StorageBuildingId);
        Assert.Equal(7_600, carried.ConditionBasisPoints);
        AssertRoundTrip(world);
    }

    [Fact]
    public void RepairChoiceCollectsUnreservedTownClothInsteadOfStallingBeforeTheTailor()
    {
        var (state, actor, _) = PreparedStock("cloth", 2);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "worn-clothing", "clothing", actor, 1,
            conditionBasisPoints: 3_000);
        state = WithReservation(WithInventory(state, inventory), "salvage-stock", 1) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Equipment = new PersonalEquipment(ClothingLotId: "worn-clothing") } : person).ToArray(),
        };
        using var world = Reload(state);
        Assert.Contains(Candidates(world, actor), item => item.Id == "repair_equipment");
        Choose(world, actor, "repair_equipment");
        Assert.Equal(1, Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "cloth").Quantity);
        Assert.Equal(3_000, world.Society.Inventory.GetLot("worn-clothing").ConditionBasisPoints);
        Assert.Equal(1, world.Society.Inventory.GetLot("salvage-stock").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("keep-stock").State);
        AssertRoundTrip(world);
    }

    [Fact]
    public void ExpansionChoicePhysicallyCollectsTheOnlyAvailableTownWood()
    {
        var (state, actor, warehouse) = PreparedStock("wood", 8);
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        var occupied = state.WorldSimulation.Buildings.Where(item => item.InstanceId != house.InstanceId)
            .SelectMany(item => WorldContentSimulationRules.Footprint(state.WorldContent.Buildings.Single(value => value.CanonicalId == item.DefinitionId), item))
            .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(state.RoadTiles!)
            .Concat(state.Towns!.Single(town => town.Id == QuietTown).BorderTiles).ToHashSet();
        var site = state.Map.Tiles.Select(tile => tile.Position).First(position =>
            state.Map.IsReachableOnFoot(warehouse.Position, position) &&
            Enumerable.Range(-1, 4).SelectMany(dy => Enumerable.Range(-1, 4).Select(dx => new GridPoint(position.X + dx, position.Y + dy)))
                .All(point => state.Map.IsBuildable(point) && !occupied.Contains(point)));
        var inventory = state.Society.Society.Inventory;
        var stored = inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
        inventory = InventoryFixture.AddLot(inventory, "house-filler", "fiber", Alpha, 52 - stored,
            storageBuildingId: house.InstanceId);
        var woodSites = state.Map.Resources.Where(resource => resource.Kind is "wood" or "construction")
            .Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
        state = WithInventory(state, inventory) with
        {
            WorldSimulation = state.WorldSimulation with
            {
                Buildings = state.WorldSimulation.Buildings.Select(item => item.InstanceId == house.InstanceId
                    ? item with { Position = site, Entrance = null } : item).ToArray(),
            },
            Towns = state.Towns!.Select(town => town.Id == house.TownId
                ? town with { BorderTiles = TownBorderRules.ExpandForBuilding(state.Map, town, site, definition.Width + 2, definition.Height + 2) }
                : town).ToArray(),
            Resources = state.Resources.Select(resource => woodSites.Contains(resource.ResourceId)
                ? resource with { State = ResourceState.Depleted } : resource).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => woodSites.Contains(resource.Id)
                        ? resource with { Quantity = 0, State = EcologyResourceState.Depleted } : resource).ToArray(),
                },
            },
        };
        using var world = Reload(state);
        var choice = "expand_building:" + house.InstanceId;
        Assert.Contains(Candidates(world, actor), item => item.Id == choice);
        Choose(world, actor, choice);
        // The expansion collects one carried load for delivery to the House.
        var carried = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "wood");
        Assert.Equal(house.InstanceId, carried.DeliveryBuildingId);
        Assert.InRange(carried.Quantity, 1, 4);
        Assert.Equal(8 - carried.Quantity, world.Society.Inventory.GetLot("salvage-stock").Quantity);
        Assert.Empty(world.WorldSimulation.BuildingExpansions ?? []);
        AssertRoundTrip(world);
    }

    [Theory]
    [InlineData("revived")]
    [InlineData("reserved")]
    [InlineData("occupied")]
    [InlineData("full")]
    public void APreviouslyOfferedToolChoiceRechecksAccessAvailabilityReachabilityAndCarrySpace(string change)
    {
        var (state, actor, warehouse) = PreparedStock("wooden_axe", 1);
        using (var planning = Reload(state))
            Assert.Contains(Candidates(planning, actor), item => item.Id == "collect_wooden_axe");
        if (change == "reserved") state = WithReservation(state, "salvage-stock", 1);
        else if (change == "revived")
        {
            var returning = state.Society.Society.Inhabitants.First(item => item.Id != actor).Id;
            state = state with
            {
                Towns = state.Towns!.Select(town => town.Id == QuietTown
                    ? town with { ResidentIds = [returning] }
                    : town with { ResidentIds = town.ResidentIds.Where(id => id != returning).ToArray() }).ToArray(),
            };
        }
        else if (change == "occupied")
        {
            var blocker = state.Inhabitants.First(item => item.InhabitantId != actor);
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { Position = blocker.Position }
                    : person.InhabitantId == blocker.InhabitantId ? person with { Position = warehouse.Position } : person).ToArray(),
            };
        }
        else
        {
            var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "full-load", "wood", actor,
                PersonalEquipmentRules.BaseCapacity);
            state = WithInventory(state, inventory);
        }
        using var world = Reload(state);
        Assert.DoesNotContain(Candidates(world, actor), item => item.Id == "collect_wooden_axe");
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Choose(world, actor, "collect_wooden_axe");
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(QuietTown, world.Society.Inventory.GetLot("salvage-stock").OwnerId);
    }

    private static (PrivateWorldRuntimeState State, string Actor, PlacedBuilding Warehouse) PreparedStock(
        string kind, int quantity, string household = Alpha)
    {
        var (state, _) = TailorTestWorld.Create("town-salvage-consumers", fiberInHouse: 0);
        var warehouse = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-warehouse");
        var firstTown = state.Towns!.Single(item => item.Id == TownBorderRules.FirstTownId);
        var border = state.Map.Tiles.Select(tile => tile.Position)
            .First(point => state.Map.IsLand(point) && !firstTown.BorderTiles.Contains(point));
        var quiet = new TownRuntimeState(QuietTown, "Quiet Yard", "founded", state.Society.Society.WorldTick, [], [], [border]);
        var inventory = state.Society.Society.Inventory;
        var removed = inventory.Lots.Where(lot => lot.StorageBuildingId == warehouse.InstanceId ||
                lot.OwnerId == household && (lot.ItemKind == kind || PersonalEquipmentRules.IsGarment(lot.ItemKind) ||
                    PersonalEquipmentRules.IsCarryAid(lot.ItemKind)))
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => !removed.Contains(lot.Id)).ToArray(),
            Reservations = inventory.Reservations.Where(item => !removed.Contains(item.LotId)).ToArray(),
        };
        state = WithInventory(state, inventory) with { Towns = [firstTown, quiet] };
        using (var setup = Reload(state))
        {
            var reassigned = setup.ReassignBuilding(warehouse.InstanceId, warehouse.TownId, warehouse.HouseholdId,
                targetTownId: QuietTown, targetHouseholdId: null);
            Assert.True(reassigned.Applied, reassigned.Failure);
            state = setup.ExportState();
        }
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == household).Id;
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "salvage-stock", kind, QuietTown, quantity,
            conditionBasisPoints: 7_600, freshnessBasisPoints: 8_300, storageBuildingId: warehouse.InstanceId);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = warehouse.Position, HungerBasisPoints = 10_000, Project = null, Equipment = null }
                : person).ToArray(),
        };
        return (state, actor, warehouse);
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntimeState WithReservation(PrivateWorldRuntimeState state, string lot, int quantity) =>
        WithInventory(state, InventoryFixture.Reserve(state.Society.Society.Inventory, "keep-stock", QuietTown,
            lot, quantity, "saved-town-work", state.Society.Society.WorldTick + 100));

    private static PrivateWorldRuntime Reload(PrivateWorldRuntimeState state) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new IdleProvider());

    private static List<CognitionCandidate> Candidates(PrivateWorldRuntime world, string actor) =>
        (List<CognitionCandidate>)typeof(PrivateWorldRuntime).GetMethod("CreateCandidates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(world, [actor, world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor)])!;

    private static void Choose(PrivateWorldRuntime world, string actor, string choice) =>
        typeof(PrivateWorldRuntime).GetMethod("ApplyCandidate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(world, [actor, world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor), choice, false]);

    private static void AssertRoundTrip(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = Reload(world.ExportState());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) => new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = request.Observation.Candidates.Where(item => item.Id == "safe_idle").ToArray() },
            }, cancellationToken);
    }
}
