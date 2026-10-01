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
        var candidateId = "collect_tool:" + kind;
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
        var candidateId = itemKind == "wooden_axe" ? "collect_wooden_axe" : "collect_tool:" + itemKind;
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
