using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ToolProgressionRuntimeTests
{
    [Theory]
    [InlineData("wood", false, false)]
    [InlineData("wood", true, false)]
    [InlineData("iron_ore", false, false)]
    [InlineData("iron_ore", true, false)]
    [InlineData("wood", false, true)]
    public async Task BlacksmithMakesRoomForAWholeHarvestWithoutRecollectingItsSpareToolAcrossReload(
        string itemKind, bool reservedPick, bool largerAxeAvailable)
    {
        using var setup = NormalPathWorld.CreateGenerated("smith-whole-load", _ => new CandidateProvider("safe_idle"));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var smith = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var household = smith.HouseholdId!;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        var house = setup.WorldSimulation.Buildings.Single(building => building.HouseholdId == household &&
            setup.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("house"));
        var tree = state.Map.Resources.Where(resource => (itemKind == "wood"
                    ? TreeGrowthRules.IsWoodTree(resource.TreeKind) : resource.Kind == itemKind) &&
                state.Map.IsReachableOnFoot(house.Position, resource.Position))
            .OrderBy(resource => state.Map.FootDistance(house.Position, resource.Position)).First();
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [] };
        inventory = InventoryFixture.AddLot(inventory, "working-axe", itemKind == "wood" ? "wooden_axe" : "stone_pickaxe", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "spare-pick", itemKind == "wood" ? "wooden_pickaxe" : "wooden_axe", actor, 1);
        // Other workshop recipes have no on-site inputs. Ore work has its wood
        // waiting at the House, so gathering the missing ore is the first choice.
        if (itemKind == "iron_ore")
        {
            inventory = InventoryFixture.AddLot(inventory, "waiting-smith-wood", "wood", household, 6,
                storageBuildingId: house.InstanceId);
            inventory = InventoryFixture.Reserve(inventory, "held-smith-wood", household, "waiting-smith-wood", 6,
                "pending_trade", state.Society.Society.WorldTick + 1_000);
        }
        if (largerAxeAvailable)
            inventory = InventoryFixture.AddLot(inventory, "larger-axe", "stone_axe", household, 1,
                storageBuildingId: house.InstanceId);
        if (reservedPick)
            inventory = InventoryFixture.Reserve(inventory, "held-spare-pick", actor, "spare-pick", 1,
                "pending_trade", state.Society.Society.WorldTick + 1_000);
        var ecology = state.WorldSystems!.Ecology;
        ecology = ecology with
        {
            Resources = ecology.Resources.Select(resource => resource.Id == tree.Id
                ? resource with { Quantity = 1, State = EcologyResourceState.Available }
                : resource with { Quantity = 0, State = EcologyResourceState.Depleted }).ToArray(),
        };
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            WorldSystems = state.WorldSystems with { Ecology = ecology },
            Resources = state.Resources.Select(resource => resource.ResourceId == tree.Id
                ? resource with { State = ResourceState.Available }
                : ecology.Resources.Any(item => item.Id == resource.ResourceId && item.State == EcologyResourceState.Depleted)
                    ? resource with { State = ResourceState.Depleted } : resource).ToArray(),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = house.Position,
                    HungerBasisPoints = 10_000,
                    Equipment = null,
                    Project = null,
                    LastDecisionContext = null
                } : person).ToArray(),
        };
        var chooser = new WorkChoiceRecorder();
        using var preparing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 12 && preparing.Society.Inventory.GetLot("spare-pick").OwnerId == actor; tick++)
            Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);
        if (reservedPick)
        {
            Assert.Equal(actor, preparing.Society.Inventory.GetLot("spare-pick").OwnerId);
            Assert.Equal(actor, preparing.Society.Inventory.GetLot("working-axe").OwnerId);
            Assert.Equal(10_000, preparing.Society.Inventory.GetLot("working-axe").ConditionBasisPoints);
            Assert.Equal(1, preparing.WorldSystems.Ecology.GetResource(tree.Id).Quantity);
            Assert.Equal(InventoryReservationState.Reserved, preparing.Society.Inventory.GetReservation("held-spare-pick").State);
            Assert.DoesNotContain(preparing.ExportState().Events, item => item.Kind == "spare_cargo_stored");
            preparing.Validate();
            return;
        }
        Assert.True(preparing.Society.Inventory.GetLot("spare-pick").OwnerId == household,
            string.Join(" | ", chooser.Choices) + "; events: " + string.Join(" | ", preparing.ExportState().Events
                .Where(item => item.Detail.Contains(actor, StringComparison.Ordinal)).TakeLast(12)
                .Select(item => item.Kind + ":" + item.Detail)));
        Assert.Equal((household, house.InstanceId), (preparing.Society.Inventory.GetLot("spare-pick").OwnerId,
            preparing.Society.Inventory.GetLot("spare-pick").StorageBuildingId));
        Assert.Equal(actor, preparing.Society.Inventory.GetLot("working-axe").OwnerId);
        var bytes = PrivateWorldRuntimeCodec.Encode(preparing.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        var deliveryEvent = itemKind == "wood" ? "smith_input_delivered" : "smith_ore_delivered";
        var gatheredQuantity = itemKind == "wood" ? 6 : 7;
        for (var tick = 0; tick < 240 && !resumed.ExportState().Events.Any(item => item.Kind == "material_gathered" &&
                 item.Detail == actor + ":" + itemKind + ":" + gatheredQuantity); tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        // Check the harvest before ordinary work can repair the newly worn axe.
        Assert.Equal(itemKind == "wood" ? 8_000 : 8_750,
            resumed.Society.Inventory.GetLot("working-axe").ConditionBasisPoints);
        Assert.Equal(gatheredQuantity,
            resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == itemKind).Sum(lot => lot.Quantity));
        Assert.Equal(household, resumed.Society.Inventory.GetLot("spare-pick").OwnerId);
        Assert.Equal(itemKind == "wood" ? 1 : 0,
            resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == "tree_seed").Sum(lot => lot.Quantity));
        if (largerAxeAvailable)
        {
            Assert.Equal((household, house.InstanceId, 10_000),
                (resumed.Society.Inventory.GetLot("larger-axe").OwnerId,
                    resumed.Society.Inventory.GetLot("larger-axe").StorageBuildingId,
                    resumed.Society.Inventory.GetLot("larger-axe").ConditionBasisPoints));
            Assert.DoesNotContain(resumed.ExportState().Events,
                item => item.Kind == "equipment_collected" && item.Detail == actor + ":stone_axe");
        }
        resumed.Validate();
        for (var tick = 0; tick < 240 && !resumed.ExportState().Events.Any(item => item.Kind == deliveryEvent &&
                 item.Detail.StartsWith(actor + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(resumed.ExportState().Events, item => item.Kind == "material_gathered" &&
            item.Detail == actor + ":" + itemKind + ":" + gatheredQuantity);
        if (itemKind == "wood")
            Assert.Contains(resumed.ExportState().Events, item => item.Kind == "tree_seed_collected" && item.Detail == actor + ":" + tree.Id + ":1");
        Assert.True(resumed.ExportState().Events.Any(item => item.Kind == deliveryEvent &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal)), string.Join(" | ", chooser.Choices.TakeLast(5)) +
            "; events: " + string.Join(" | ", resumed.ExportState().Events
                .Where(item => item.Detail.Contains(actor, StringComparison.Ordinal)).TakeLast(20)
                .Select(item => item.Kind + ":" + item.Detail)));
        Assert.Equal((0, tree.IsRenewable ? EcologyResourceState.Regenerating : EcologyResourceState.Depleted),
            (resumed.WorldSystems.Ecology.GetResource(tree.Id).Quantity,
            resumed.WorldSystems.Ecology.GetResource(tree.Id).State));
        var repairs = resumed.ExportState().Events.Count(item => item.Kind == "tool_repaired" &&
            item.Detail == actor + ":working-axe:" + smith.InstanceId);
        Assert.InRange(repairs, 0, itemKind == "wood" ? 1 : 0);
        Assert.Equal(repairs == 0 ? itemKind == "wood" ? 8_000 : 8_750 : 10_000,
            resumed.Society.Inventory.GetLot("working-axe").ConditionBasisPoints);
        Assert.Equal(1, resumed.Society.Inventory.Lots.Where(lot =>
            lot.ItemKind == (itemKind == "wood" ? "wooden_pickaxe" : "wooden_axe")).Sum(lot => lot.Quantity));
        Assert.Equal(gatheredQuantity - repairs,
            resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == itemKind).Sum(lot => lot.Quantity));
        var plantedSeeds = resumed.ExportState().Events.Count(item =>
            item.Kind is "tree_planted" or "tree_replanted" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        Assert.Equal(itemKind == "wood" ? 1 : 0, plantedSeeds +
            resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == "tree_seed").Sum(lot => lot.Quantity));
        Assert.Contains(resumed.Society.Inventory.Lots, lot => lot.ItemKind == itemKind && lot.OwnerId == household &&
            lot.StorageBuildingId == smith.InstanceId && lot.Quantity > 0);
        resumed.Validate();
    }

    [Theory]
    [InlineData("gold_ore", "iron")]
    [InlineData("diamond", "iron")]
    [InlineData("gold_ore", "stone")]
    [InlineData("diamond", "stone")]
    [InlineData("gold_ore", "reserved")]
    [InlineData("diamond", "reserved")]
    [InlineData("gold_ore", "full")]
    [InlineData("diamond", "full")]
    public async Task NormalRareMiningRequiresAUsableIronPickAndRoomAndDepletesRealStockAcrossReload(
        string itemKind, string scenario)
    {
        using var setup = NormalPathWorld.CreateGenerated("tool-rare-mining", _ => new CandidateProvider("safe_idle"));
        var state = setup.ExportState();
        var actor = state.Society.Society.Inhabitants[0].Id;
        var source = state.Map.Resources.Where(resource => resource.Kind == itemKind && !resource.IsRenewable &&
                state.Map.IsPassable(resource.Position) &&
                !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == resource.Position))
            .OrderBy(resource => resource.Id, StringComparer.Ordinal).First();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "mining-pick", scenario == "stone" ? "stone_pickaxe" : "iron_pickaxe", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "mining-basket", "basket", actor, 1);
        if (scenario == "full")
            inventory = InventoryFixture.AddLot(inventory, "mining-ballast", "fiber", actor, 8);
        if (scenario == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "held-mining-pick", actor, "mining-pick", 1,
                "pending_trade", state.Society.Society.WorldTick + 120);
        var systems = state.WorldSystems!;
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            WorldSystems = systems with
            {
                Ecology = systems.Ecology with
                {
                    Resources = systems.Ecology.Resources.Select(resource => resource.Id == source.Id
                        ? resource with { Quantity = 1 } : resource).ToArray(),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = source.Position,
                    HungerBasisPoints = 10_000,
                    LastDecisionContext = null,
                    Project = null,
                    Equipment = new(CarryAidLotId: "mining-basket"),
                } : person).ToArray(),
        };
        var candidate = "gather_rare_material:" + itemKind;
        var chooser = new CandidateProvider(candidate);
        using var mining = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 20 && (scenario == "iron"
                 ? !mining.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ItemKind == itemKind)
                 : chooser.ObservedCandidateSets.Count == 0); tick++)
        {
            Assert.True((await mining.AdvanceOneTickAsync()).Advanced);
            mining.Validate();
        }
        Assert.NotEmpty(chooser.ObservedCandidateSets);
        var extracted = mining.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == itemKind).ToArray();
        if (scenario == "iron")
        {
            Assert.Contains(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidate));
            var goods = Assert.Single(extracted);
            Assert.Equal(8, goods.Quantity);
            Assert.Null(goods.StorageBuildingId);
            Assert.Null(goods.DeliveryBuildingId);
            Assert.Null(goods.GroundPosition);
            var depleted = mining.WorldSystems.Ecology.GetResource(source.Id);
            Assert.Equal((0, EcologyResourceState.Depleted), (depleted.Quantity, depleted.State));
            Assert.Equal(9_000, mining.Society.Inventory.GetLot("mining-pick").ConditionBasisPoints);
        }
        else
        {
            Assert.DoesNotContain(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidate));
            Assert.Empty(extracted);
            Assert.Equal(1, mining.WorldSystems.Ecology.GetResource(source.Id).Quantity);
            Assert.Equal(10_000, mining.Society.Inventory.GetLot("mining-pick").ConditionBasisPoints);
            if (scenario == "full")
                Assert.Equal(8, mining.Society.Inventory.GetLot("mining-ballast").Quantity);
            if (scenario == "reserved")
                Assert.Equal(InventoryReservationState.Reserved,
                    mining.Society.Inventory.GetReservation("held-mining-pick").State);
        }
        var bytes = PrivateWorldRuntimeCodec.Encode(mining.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new CandidateProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        reloaded.Validate();
        Assert.Equal(extracted.Select(lot => lot with { LastProcessedTick = reloaded.Society.WorldTick }),
            reloaded.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == itemKind));
    }

    [Theory]
    [InlineData("wooden_axe", "collect_wooden_axe")]
    [InlineData("wooden_pickaxe", "collect_wooden_pickaxe")]
    public async Task LegacyHouseholdStarterToolsArePhysicalStockAndCanBeCollected(
        string itemKind, string candidateId)
    {
        const string actor = "founder-scout";
        var chooser = new CandidateProvider(candidateId);
        using var world = new PrivateWorldRuntime("legacy-tool-stock-" + itemKind,
            id => id == actor ? chooser : new CandidateProvider("safe_idle"));

        var stock = Assert.Single(world.Society.Inventory.Lots, lot =>
            lot.OwnerId == "household:camp-alpha" && lot.ItemKind == itemKind);
        Assert.Equal(1, stock.Quantity);
        Assert.Equal(10_000, stock.ConditionBasisPoints);
        Assert.Null(stock.StorageBuildingId);
        Assert.Null(stock.GroundPosition);

        for (var tick = 0; tick < 120 && !world.ExportState().Events.Any(item =>
                 item.Kind == "equipment_collected" && item.Detail == $"{actor}:{itemKind}"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidateId));
        Assert.Contains(world.ExportState().Events, item =>
            item.Kind == "equipment_collected" && item.Detail == $"{actor}:{itemKind}");
        var collected = Assert.Single(world.Society.Inventory.Lots, lot =>
            lot.OwnerId == actor && lot.ItemKind == itemKind);
        Assert.Equal(1, collected.Quantity);
        Assert.Null(collected.StorageBuildingId);
        Assert.Null(collected.GroundPosition);
    }

    [Theory]
    [InlineData("wooden_hoe", true, true)]
    [InlineData("wooden_hoe", false, false)]
    [InlineData("tool", true, false)]
    [InlineData("tool", false, false)]
    public async Task SharedHoeGoesToAFarmHouseholdAndObsoleteGenericToolIsNotCollectedAcrossReload(
        string kind, bool farmHousehold, bool offered)
    {
        using var setup = NormalPathWorld.CreateGenerated("tool-work-eligibility", _ => new CandidateProvider("safe_idle"));
        var state = setup.ExportState();
        var farmhouse = setup.WorldSimulation.Buildings.Single(building => setup.WorldContent.Buildings
            .Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("farmhouse"));
        var actor = state.Society.Society.Inhabitants.First(person =>
            (person.HouseholdId == farmhouse.HouseholdId) == farmHousehold &&
            person.HouseholdId is not null && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var warehouse = setup.WorldSimulation.Buildings.Single(building => setup.WorldContent.Buildings
            .Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("warehouse"));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId != warehouse.InstanceId &&
                lot.OwnerId != actor).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "eligible-work-tool", kind, warehouse.TownId!, 1,
            storageBuildingId: warehouse.InstanceId);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = warehouse.Position,
                    HungerBasisPoints = 10_000,
                    Equipment = null,
                    LastDecisionContext = null,
                    Project = null,
                } : person).ToArray(),
        };
        var total = inventory.Lots.Sum(lot => lot.Quantity);
        var candidateId = kind == "wooden_hoe" ? "collect_wooden_hoe" : "collect_tool:" + kind;
        var chooser = new CandidateProvider(candidateId);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 10 && (offered ? world.Society.Inventory.GetLot("eligible-work-tool").OwnerId != actor
                 : chooser.ObservedCandidateSets.Count == 0); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(chooser.ObservedCandidateSets);
        Assert.Equal(offered, chooser.ObservedCandidateSets.Any(candidates => candidates.Contains(candidateId)));
        var stock = world.Society.Inventory.GetLot("eligible-work-tool");
        Assert.Equal(offered ? actor : warehouse.TownId, stock.OwnerId);
        Assert.Equal(offered ? null : warehouse.InstanceId, stock.StorageBuildingId);
        Assert.Equal(1, stock.Quantity);
        Assert.Equal(total, world.Society.Inventory.Lots.Sum(lot => lot.Quantity));
        Assert.Equal(offered, world.ExportState().Events.Any(item => item.Kind == "equipment_collected" &&
            item.Detail == actor + ":" + kind));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new CandidateProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        resumed.Validate();
    }

    [Theory]
    [InlineData("wooden_axe", "own")]
    [InlineData("wooden_hoe", "own")]
    [InlineData("wooden_axe", "empty")]
    [InlineData("wooden_hoe", "empty")]
    [InlineData("wooden_axe", "inhabited")]
    [InlineData("wooden_hoe", "inhabited")]
    [InlineData("wooden_axe", "reserved")]
    [InlineData("wooden_hoe", "reserved")]
    public async Task SharedWorkToolsRespectTownWarehouseAccessAndReservationsAcrossReload(
        string itemKind, string scenario)
    {
        using var setup = NormalPathWorld.CreateGenerated("tool-warehouse-access",
            _ => new CandidateProvider("safe_idle"));
        var state = setup.ExportState();
        var actor = state.Society.Society.Inhabitants[0].Id;
        var warehouse = Assert.Single(setup.WorldSimulation.Buildings, building =>
            setup.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("warehouse", StringComparer.Ordinal));
        var warehouseLots = state.Society.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == warehouse.InstanceId).Select(lot => lot.Id)
            .ToHashSet(StringComparer.Ordinal);
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => !warehouseLots.Contains(lot.Id)).ToArray(),
            Reservations = state.Society.Society.Inventory.Reservations
                .Where(reservation => !warehouseLots.Contains(reservation.LotId)).ToArray(),
        };
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        if (scenario != "own")
        {
            var firstTown = Assert.Single(state.Towns!);
            var outside = state.Map.Tiles.Select(tile => tile.Position)
                .First(point => state.Map.IsLand(point) && !firstTown.BorderTiles.Contains(point));
            var otherResidents = scenario == "inhabited"
                ? state.Society.Society.Inhabitants.Where(person => person.HouseholdId == "household:camp-beta")
                    .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray()
                : [];
            state = state with
            {
                Towns =
                [
                    firstTown with { ResidentIds = firstTown.ResidentIds.Except(otherResidents).ToArray() },
                    new TownRuntimeState("town:tool-yard", "Tool Yard", "founded",
                        state.Society.Society.WorldTick, otherResidents, [], [outside]),
                ],
            };
            using var reassigned = PrivateWorldRuntime.Restore(state, _ => new CandidateProvider("safe_idle"));
            var result = reassigned.ReassignBuilding(warehouse.InstanceId, warehouse.TownId, warehouse.HouseholdId,
                targetTownId: "town:tool-yard", targetHouseholdId: null);
            Assert.True(result.Applied, result.Failure);
            state = reassigned.ExportState();
            warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == warehouse.InstanceId);
        }
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "warehouse-work-tool", itemKind, warehouse.TownId!, 1, storageBuildingId: warehouse.InstanceId);
        if (scenario == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "held-work-tool", warehouse.TownId!,
                "warehouse-work-tool", 1, "pending_trade", state.Society.Society.WorldTick + 120);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = warehouse.Position, HungerBasisPoints = 10_000, LastDecisionContext = null }
                : person).ToArray(),
        };
        var candidateId = itemKind == "wooden_axe" ? "collect_wooden_axe" : "collect_wooden_hoe";
        var chooser = new CandidateProvider(candidateId);
        using var collecting = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 20 && chooser.ObservedCandidateSets.Count == 0; tick++)
        {
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
            collecting.Validate();
        }
        Assert.NotEmpty(chooser.ObservedCandidateSets);
        var tool = collecting.Society.Inventory.GetLot("warehouse-work-tool");
        Assert.Equal((1, 10_000), (tool.Quantity, tool.ConditionBasisPoints));
        Assert.Null(tool.DeliveryBuildingId);
        Assert.Null(tool.GroundPosition);
        if (scenario is "own" or "empty")
        {
            Assert.Contains(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidateId));
            Assert.Equal(actor, tool.OwnerId);
            Assert.Null(tool.StorageBuildingId);
            Assert.Contains(collecting.ExportState().Events, item =>
                item.Kind == "equipment_collected" && item.Detail == actor + ":" + itemKind);
        }
        else
        {
            Assert.DoesNotContain(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidateId));
            Assert.Equal(warehouse.TownId, tool.OwnerId);
            Assert.Equal(warehouse.InstanceId, tool.StorageBuildingId);
            if (scenario == "reserved")
                Assert.Equal(InventoryReservationState.Reserved,
                    collecting.Society.Inventory.GetReservation("held-work-tool").State);
        }
        Assert.Equal(1, collecting.Society.Inventory.Lots.Where(lot => lot.ItemKind == itemKind).Sum(lot => lot.Quantity));
        var bytes = PrivateWorldRuntimeCodec.Encode(collecting.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new CandidateProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        reloaded.Validate();
        Assert.Equal(tool with { LastProcessedTick = reloaded.Society.WorldTick },
            reloaded.Society.Inventory.GetLot("warehouse-work-tool"));
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("shared")]
    public async Task ToolRepairMakesRoomWithoutStowingTheRepairOrHarvestToolAcrossReload(string materialSource)
    {
        using var setup = NormalPathWorld.CreateGenerated("tool-repair-capacity-" + materialSource,
            _ => new CandidateProvider("safe_idle"));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var household = smith.HouseholdId!;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var house = state.WorldSimulation.Buildings.Single(building => building.HouseholdId == household &&
            setup.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("house"));
        var tree = materialSource == "tree"
            ? state.Map.Resources.Where(resource => TreeGrowthRules.IsWoodTree(resource.TreeKind) &&
                    state.Map.IsReachableOnFoot(house.Position, resource.Position))
                .OrderBy(resource => state.Map.FootDistance(house.Position, resource.Position)).First()
            : null;
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [] };
        inventory = InventoryFixture.AddLot(inventory, "repair-target", "wooden_axe", actor, 1,
            conditionBasisPoints: 5_000);
        inventory = InventoryFixture.AddLot(inventory, "harvest-axe", "wooden_axe", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "repair-ballast", "fiber", actor, 14);
        inventory = InventoryFixture.AddLot(inventory, "repair-basket", "basket", actor, 1);
        if (materialSource == "shared")
            inventory = InventoryFixture.AddLot(inventory, "repair-shared-wood", "wood", household, 1,
                storageBuildingId: house.InstanceId);

        var ecology = state.WorldSystems!.Ecology with
        {
            Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == tree?.Id
                ? resource with { Quantity = 1, State = EcologyResourceState.Available }
                : resource with { Quantity = 0, State = EcologyResourceState.Depleted }).ToArray(),
        };
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            WorldSystems = state.WorldSystems with { Ecology = ecology },
            Resources = state.Resources.Select(resource => resource.ResourceId == tree?.Id
                ? resource with { State = ResourceState.Available }
                : resource with { State = ResourceState.Depleted }).ToArray(),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = house.Position,
                    HungerBasisPoints = 9_000,
                    Equipment = new(CarryAidLotId: "repair-basket"),
                    Project = null,
                    LastDecisionContext = null,
                } : person).ToArray(),
        };
        var candidateId = "repair_tool:repair-target";
        var chooser = new CandidateProvider(candidateId);
        using var preparing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        bool HasRepairMaterialProgress() => materialSource == "tree"
            ? preparing.ExportState().Events.Any(item => item.Kind == "material_gathered" &&
                item.Detail == actor + ":wood:6")
            : preparing.ExportState().Events.Any(item => item.Kind == "equipment_collected" &&
                item.Detail == actor + ":wood");
        for (var tick = 0; tick < 240 && !HasRepairMaterialProgress(); tick++)
            Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidateId));
        Assert.Contains(preparing.ExportState().Events, item => item.Kind == "spare_cargo_stored");
        Assert.Equal(actor, preparing.Society.Inventory.GetLot("repair-target").OwnerId);
        Assert.Equal(5_000, preparing.Society.Inventory.GetLot("repair-target").ConditionBasisPoints);
        Assert.Equal(actor, preparing.Society.Inventory.GetLot("harvest-axe").OwnerId);
        Assert.Equal(materialSource == "tree" ? 8_000 : 10_000,
            preparing.Society.Inventory.GetLot("harvest-axe").ConditionBasisPoints);
        Assert.Equal(14, preparing.Society.Inventory.Lots.Where(lot => lot.ItemKind == "fiber").Sum(lot => lot.Quantity));
        Assert.Equal(materialSource == "tree" ? 7 : 13, preparing.Society.Inventory.Lots
            .Where(lot => lot.ItemKind == "fiber" && lot.OwnerId == actor).Sum(lot => lot.Quantity));
        Assert.Equal(materialSource == "tree" ? 7 : 1, preparing.Society.Inventory.Lots
            .Where(lot => lot.ItemKind == "fiber" && lot.OwnerId == household && lot.StorageBuildingId == house.InstanceId)
            .Sum(lot => lot.Quantity));
        if (materialSource == "tree")
        {
            Assert.Equal(6, preparing.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" && lot.OwnerId == actor)
                .Sum(lot => lot.Quantity));
            Assert.Equal(1, preparing.Society.Inventory.Lots.Where(lot => lot.ItemKind == "tree_seed" && lot.OwnerId == actor)
                .Sum(lot => lot.Quantity));
        }
        else
        {
            Assert.Equal(actor, preparing.Society.Inventory.GetLot("repair-shared-wood").OwnerId);
            Assert.Equal(1, preparing.Society.Inventory.GetLot("repair-shared-wood").Quantity);
        }
        preparing.Validate();

        var bytes = PrivateWorldRuntimeCodec.Encode(preparing.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        for (var tick = 0; tick < 240 && !resumed.ExportState().Events.Any(item => item.Kind == "tool_repaired" &&
                 item.Detail == actor + ":repair-target:" + smith.InstanceId); tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(resumed.ExportState().Events, item => item.Kind == "tool_repaired" &&
            item.Detail == actor + ":repair-target:" + smith.InstanceId);
        Assert.Equal(10_000, resumed.Society.Inventory.GetLot("repair-target").ConditionBasisPoints);
        Assert.Equal(materialSource == "tree" ? 8_000 : 10_000,
            resumed.Society.Inventory.GetLot("harvest-axe").ConditionBasisPoints);
        Assert.Equal(materialSource == "tree" ? 5 : 0,
            resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" && lot.OwnerId == actor)
                .Sum(lot => lot.Quantity));
        Assert.Equal(materialSource == "tree" ? 1 : 0,
            resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == "tree_seed" && lot.OwnerId == actor)
                .Sum(lot => lot.Quantity));
        Assert.Equal(14, resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == "fiber").Sum(lot => lot.Quantity));
        resumed.Validate();
    }

    [Fact]
    public async Task BlacksmithDeliversAlreadyCarriedRecipeInputAtFullCapacityAcrossReload()
    {
        using var setup = NormalPathWorld.CreateGenerated("smith-full-carried-input",
            _ => new CandidateProvider("safe_idle"));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building =>
            building.InstanceId == "first-town-blacksmith");
        var household = smith.HouseholdId!;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory with { Lots = [], Reservations = [] },
            "personal-blacksmith-wood", "wood", actor, 8);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = smith.Position,
                    HungerBasisPoints = 9_000,
                    Equipment = null,
                    Project = null,
                    LastDecisionContext = null,
                    TravelCooldownTicks = 0,
                } : person).ToArray(),
        };
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(inventory, actor, null));

        var chooser = new CandidateProvider("haul_smith_input");
        using var hauling = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        Assert.True((await hauling.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(chooser.ObservedCandidateSets, candidates => candidates.Contains("haul_smith_input"));
        var remaining = hauling.Society.Inventory.GetLot("personal-blacksmith-wood");
        Assert.Equal((actor, (string?)null, 4),
            (remaining.OwnerId, remaining.StorageBuildingId, remaining.Quantity));
        Assert.Equal(4, hauling.Society.Inventory.Lots.Where(lot => lot.OwnerId == household &&
            lot.StorageBuildingId == smith.InstanceId && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Contains(hauling.ExportState().Events, item => item.Kind == "smith_input_delivered" &&
            item.Detail == $"{actor}:personal-blacksmith-wood:4:{smith.InstanceId}");
        Assert.Equal(8, hauling.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));

        var bytes = PrivateWorldRuntimeCodec.Encode(hauling.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new CandidateProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        resumed.Validate();
    }

    [Fact]
    public async Task MultiMaterialToolRepairDoesNotCollectAnInputWhenTheCompleteLoadCannotFit()
    {
        using var setup = NormalPathWorld.CreateGenerated("tool-repair-multi-capacity-blocked",
            _ => new CandidateProvider("safe_idle"));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building =>
            building.InstanceId == "first-town-blacksmith");
        var household = smith.HouseholdId!;
        var house = state.WorldSimulation.Buildings.Single(building => building.HouseholdId == household &&
            setup.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("house"));
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [] };
        inventory = InventoryFixture.AddLot(inventory, "repair-target", "iron_pickaxe", actor, 1,
            conditionBasisPoints: 5_000);
        inventory = InventoryFixture.AddLot(inventory, "reserved-repair-ballast", "fiber", actor, 6);
        inventory = InventoryFixture.AddLot(inventory, "shared-repair-wood", "wood", household, 1,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "shared-repair-iron", "iron", household, 1,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.Reserve(inventory, "held-repair-ballast", actor,
            "reserved-repair-ballast", 6, "pending_trade", state.Society.Society.WorldTick + 1_000);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = house.Position,
                    HungerBasisPoints = 9_000,
                    Equipment = null,
                    Project = null,
                    LastDecisionContext = null,
                    TravelCooldownTicks = 0,
                } : person).ToArray(),
        };
        Assert.Equal(1, PersonalEquipmentRules.FreeCapacity(inventory, actor, null));

        var candidateId = "repair_tool:repair-target";
        var chooser = new CandidateProvider(candidateId);
        using var preparing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 4; tick++)
            Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);

        Assert.NotEmpty(chooser.ObservedCandidateSets);
        Assert.DoesNotContain(chooser.ObservedCandidateSets, candidates => candidates.Contains(candidateId));
        Assert.Equal((actor, 5_000), (preparing.Society.Inventory.GetLot("repair-target").OwnerId,
            preparing.Society.Inventory.GetLot("repair-target").ConditionBasisPoints));
        Assert.Equal((household, house.InstanceId, 1),
            (preparing.Society.Inventory.GetLot("shared-repair-wood").OwnerId,
                preparing.Society.Inventory.GetLot("shared-repair-wood").StorageBuildingId,
                preparing.Society.Inventory.GetLot("shared-repair-wood").Quantity));
        Assert.Equal((household, house.InstanceId, 1),
            (preparing.Society.Inventory.GetLot("shared-repair-iron").OwnerId,
                preparing.Society.Inventory.GetLot("shared-repair-iron").StorageBuildingId,
                preparing.Society.Inventory.GetLot("shared-repair-iron").Quantity));
        Assert.Equal(InventoryReservationState.Reserved,
            preparing.Society.Inventory.GetReservation("held-repair-ballast").State);
        Assert.DoesNotContain(preparing.ExportState().Events, item => item.Kind == "equipment_collected" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));

        var bytes = PrivateWorldRuntimeCodec.Encode(preparing.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new CandidateProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        resumed.Validate();
    }

    [Fact]
    public async Task MultiMaterialToolRepairCollectsBothInputsAndCompletesAcrossReload()
    {
        using var setup = NormalPathWorld.CreateGenerated("tool-repair-multi-capacity-fit",
            _ => new CandidateProvider("safe_idle"));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building =>
            building.InstanceId == "first-town-blacksmith");
        var household = smith.HouseholdId!;
        var house = state.WorldSimulation.Buildings.Single(building => building.HouseholdId == household &&
            setup.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId)
                .Tags.Contains("house"));
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var inventory = state.Society.Society.Inventory with { Lots = [], Reservations = [] };
        inventory = InventoryFixture.AddLot(inventory, "repair-target", "iron_pickaxe", actor, 1,
            conditionBasisPoints: 5_000);
        inventory = InventoryFixture.AddLot(inventory, "shared-repair-wood", "wood", household, 1,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "shared-repair-iron", "iron", household, 1,
            storageBuildingId: house.InstanceId);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = house.Position,
                    HungerBasisPoints = 9_000,
                    Equipment = null,
                    Project = null,
                    LastDecisionContext = null,
                    TravelCooldownTicks = 0,
                } : person).ToArray(),
        };

        var candidateId = "repair_tool:repair-target";
        var chooser = new CandidateProvider(candidateId);
        using var preparing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 20 && !preparing.ExportState().Events.Any(item =>
                 item.Kind == "equipment_collected" && item.Detail == actor + ":wood"); tick++)
            Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(preparing.ExportState().Events, item => item.Kind == "equipment_collected" &&
            item.Detail == actor + ":wood");
        Assert.Equal((actor, 1), (preparing.Society.Inventory.GetLot("shared-repair-wood").OwnerId,
            preparing.Society.Inventory.GetLot("shared-repair-wood").Quantity));
        Assert.Equal((household, house.InstanceId),
            (preparing.Society.Inventory.GetLot("shared-repair-iron").OwnerId,
                preparing.Society.Inventory.GetLot("shared-repair-iron").StorageBuildingId));
        Assert.Equal(5_000, preparing.Society.Inventory.GetLot("repair-target").ConditionBasisPoints);

        var bytes = PrivateWorldRuntimeCodec.Encode(preparing.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == actor ? chooser : new CandidateProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        for (var tick = 0; tick < 240 && !resumed.ExportState().Events.Any(item => item.Kind == "tool_repaired" &&
                 item.Detail == actor + ":repair-target:" + smith.InstanceId); tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(resumed.ExportState().Events, item => item.Kind == "equipment_collected" &&
            item.Detail == actor + ":iron");
        Assert.Contains(resumed.ExportState().Events, item => item.Kind == "tool_repaired" &&
            item.Detail == actor + ":repair-target:" + smith.InstanceId);
        Assert.Equal(10_000, resumed.Society.Inventory.GetLot("repair-target").ConditionBasisPoints);
        var repairReservations = resumed.Society.Inventory.Reservations
            .Where(item => item.Purpose == "equipment_repair").ToArray();
        Assert.Equal(2, repairReservations.Length);
        Assert.All(repairReservations, item => Assert.Equal(InventoryReservationState.Completed, item.State));
        resumed.Validate();

        var completedBytes = PrivateWorldRuntimeCodec.Encode(resumed.ExportState());
        using var completedReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(completedBytes),
            _ => new CandidateProvider("safe_idle"));
        Assert.Equal(completedBytes, PrivateWorldRuntimeCodec.Encode(completedReload.ExportState()));
        completedReload.Validate();
    }

    private sealed class WorkChoiceRecorder : IDecisionProvider
    {
        private readonly DeterministicDecisionProvider chooser = new();
        public List<string> Choices { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = await chooser.DecideAsync(request, cancellationToken);
            Choices.Add(response.SelectedCandidateId + " [" + string.Join(",", request.Observation.Candidates
                .Select(candidate => candidate.Id + "=" + candidate.DeterministicPriority)) + "]");
            return response;
        }
    }

    private sealed class CandidateProvider(string candidateId) : IDecisionProvider
    {
        public List<string[]> ObservedCandidateSets { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates;
            ObservedCandidateSets.Add(candidates.Select(item => item.Id).ToArray());
            var selected = candidates.FirstOrDefault(item => item.Id == candidateId) ??
                candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d)));
        }
    }
}
