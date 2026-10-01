using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PotteryContentTests
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void PotteryManifestDefinesHouseMadeStoragePotAndWaterJug()
    {
        var preview = PrivateWorldRuntime.PreviewWorldContent(
            [StarterContent.Create(), SettlementContent.Create(), HouseContent.Create(), PotteryContent.Create()],
            [PotteryContent.PackageId]);

        Assert.True(preview.IsValid, preview.Diagnostic);
        var pot = Assert.Single(preview.WorldContent.Recipes, recipe => recipe.LocalId == "storage-pot");
        var jug = Assert.Single(preview.WorldContent.Recipes, recipe => recipe.LocalId == "water-jug");
        Assert.Equal(HouseContent.House1x1().CanonicalId, pot.WorkstationBuildingId);
        Assert.Equal(HouseContent.House1x1().CanonicalId, jug.WorkstationBuildingId);
        Assert.Equal([new ContentQuantity("clay", 2), new ContentQuantity("wood", 1)], pot.Inputs);
        Assert.Equal([new ContentQuantity("clay", 2), new ContentQuantity("wood", 1)], jug.Inputs);
        Assert.Equal([new ContentQuantity(InventoryContainerRules.StoragePot, 1)], pot.Outputs);
        Assert.Equal([new ContentQuantity(InventoryContainerRules.WaterJug, 1)], jug.Outputs);
    }

    [Fact]
    public async Task GeneratedFirstTownDigsClayAtReachableFreshwaterBankAndMakesBothVesselsAtItsHouse()
    {
        using var setup = NormalPathWorld.CreateGenerated("pottery-bank-to-house", _ => new IdleProvider());
        for (var tick = 0; tick < 8; tick++)
            Assert.True((await setup.AdvanceOneTickAsync()).Advanced);

        var initial = setup.ExportState();
        var clay = Assert.Single(initial.Map.Resources, resource => resource.Id == "settlement-clay");
        Assert.Equal("clay", clay.Kind);
        Assert.False(clay.IsRenewable);
        Assert.True(initial.Map.IsReachableFromCampOnFoot(clay.Position));
        Assert.Equal(WaterKind.Land, initial.Map.HydrologyAt(clay.Position));
        Assert.Contains(CardinalNeighbors(initial.Map, clay.Position), point =>
            initial.Map.HydrologyAt(point) is WaterKind.River or WaterKind.Lake);
        Assert.Equal(16, initial.WorldSystems!.Ecology.GetResource(clay.Id).Quantity);

        var householdId = initial.Society.Society.Inhabitants.First(person => person.HouseholdId is not null).HouseholdId!;
        var actor = initial.Society.Society.Inhabitants.First(person =>
            person.HouseholdId == householdId && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var houseDefinition = setup.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        var house = setup.WorldSimulation.Buildings.SingleOrDefault(building =>
            building.DefinitionId == houseDefinition.CanonicalId && building.HouseholdId == householdId);
        if (house is null)
        {
            var buildingTiles = setup.WorldSimulation.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(setup.WorldContent.Buildings.Single(definition =>
                    definition.CanonicalId == building.DefinitionId), building.Position)).ToHashSet();
            var site = initial.Map.Tiles.Select(tile => tile.Position).First(point =>
                initial.Map.IsBuildable(point) && initial.Map.IsReachableFromCampOnFoot(point) &&
                !buildingTiles.Contains(point) && !setup.RoadTiles.Contains(point) &&
                !initial.Map.CampObjects.Any(item => item.Position == point) &&
                !initial.Map.Resources.Any(item => item.Position == point) &&
                !initial.Inhabitants.Any(person => person.Position == point));
            var placement = setup.PlaceBuilding("pottery-house", houseDefinition.CanonicalId, site, householdId);
            Assert.True(placement.Applied, placement.Failure);
            house = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "pottery-house");
        }

        var supplier = new PrefixCandidateProvider("supply_workstation:");
        var supplyState = setup.ExportState();
        supplyState = supplyState with
        {
            Inhabitants = supplyState.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    HungerBasisPoints = 10_000,
                    Survival = person.Survival is { } condition
                        ? condition with { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000 }
                        : null,
                }
                : person).ToArray(),
        };
        using var supplying = PrivateWorldRuntime.Restore(supplyState, id => id == actor
            ? supplier : new IdleProvider());
        for (var tick = 0; tick < 160 &&
             HouseQuantity(supplying, householdId, house.InstanceId, "clay") < 4; tick++)
            Assert.True((await supplying.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(supplier.OfferedWorkstationCandidates, id => id == "supply_workstation:clay");
        Assert.Contains(supplying.ExportState().Events, item => item.Kind == "material_gathered" &&
            item.Detail.StartsWith(actor + ":clay:", StringComparison.Ordinal));
        Assert.Contains(supplying.ExportState().Events, item => item.Kind == "workstation_supplied" &&
            item.Detail.EndsWith(":" + house.InstanceId, StringComparison.Ordinal));
        var supplyEvents = supplying.ExportState().Events.Where(item =>
            item.Kind is "material_gathered" or "workstation_supplied").Select(item => item.Detail).ToArray();
        Assert.True(HouseQuantity(supplying, householdId, house.InstanceId, "clay") == 4,
            string.Join(" | ", supplyEvents));
        Assert.Equal(15, supplying.WorldSystems!.Ecology.GetResource(clay.Id).Quantity);

        var productionState = supplying.ExportState();
        var woodStock = InventoryFixture.AddLot(productionState.Society.Society.Inventory,
            "test-house-pottery-wood", "wood", householdId, 4,
            productionState.Society.Society.WorldTick, storageBuildingId: house.InstanceId);
        productionState = productionState with
        {
            Society = productionState.Society with
            {
                Society = productionState.Society.Society with { Inventory = woodStock },
            },
        };
        foreach (var recipeLocalId in new[] { "storage-pot", "water-jug" })
        {
            using var current = PrivateWorldRuntime.Restore(productionState, _ => new IdleProvider());
            var recipe = current.WorldContent.Recipes.Single(item => item.LocalId == recipeLocalId);
            var started = current.StartProduction(recipe.CanonicalId, house.InstanceId, actor);
            Assert.True(started.Applied, started.Failure);
            var checkpoint = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(current.ExportState()));
            using var resumed = PrivateWorldRuntime.Restore(checkpoint, _ => new IdleProvider());
            for (var tick = 0; tick < recipe.DurationTicks; tick++)
                Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(WorldProductionJobState.Completed,
                resumed.WorldSimulation.ProductionJobs.Single(item => item.JobId == started.JobId).State);
            Assert.Contains(resumed.Society.Inventory.Lots, lot => lot.OwnerId == householdId &&
                lot.StorageBuildingId == house.InstanceId && lot.ItemKind == recipe.Outputs[0].ResourceId &&
                lot.Quantity == 1);
            productionState = resumed.ExportState();
        }

        var jugId = productionState.Society.Society.Inventory.Lots.Single(lot =>
            lot.ItemKind == InventoryContainerRules.WaterJug).Id;
        var collector = new PrefixCandidateProvider("collect_water_jug");
        using var collecting = PrivateWorldRuntime.Restore(productionState, id => id == actor
            ? collector : new IdleProvider());
        for (var tick = 0; tick < 80 && collecting.Society.Inventory.GetLot(jugId).OwnerId != actor; tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(collector.OfferedCandidates, id => id == "collect_water_jug");
        Assert.Equal(actor, collecting.Society.Inventory.GetLot(jugId).OwnerId);
        Assert.Null(collecting.Society.Inventory.GetLot(jugId).StorageBuildingId);
        Assert.Contains(collecting.ExportState().Events, item => item.Kind == "water_jug_collected" &&
            item.Detail.StartsWith(actor + ":" + jugId, StringComparison.Ordinal));

        var heldJugState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collecting.ExportState()));
        var waterLoop = new WaterLoopProvider();
        using var filling = PrivateWorldRuntime.Restore(heldJugState, id => id == actor
            ? waterLoop : new IdleProvider());
        for (var tick = 0; tick < 500 && filling.Society.Inventory.GetLot(jugId).StorageBuildingId != house.InstanceId; tick++)
            Assert.True((await filling.AdvanceOneTickAsync()).Advanced);
        var filledJug = filling.Society.Inventory.GetLot(jugId);
        var water = Assert.Single(filling.Society.Inventory.Lots, lot => lot.ContainerLotId == jugId);
        Assert.Equal(householdId, filledJug.OwnerId);
        Assert.Equal(house.InstanceId, filledJug.StorageBuildingId);
        Assert.Equal(InventoryContainerRules.FreshWater, water.ItemKind);
        Assert.Equal(InventoryContainerRules.WaterJugCapacity, water.Quantity);
        Assert.Equal(house.InstanceId, water.StorageBuildingId);
        var fillEvent = Assert.Single(filling.ExportState().Events, item => item.Kind == "water_jug_filled");
        var shoreParts = fillEvent.Detail[(fillEvent.Detail.LastIndexOf(':') + 1)..].Split(',');
        var fillPoint = new GridPoint(int.Parse(shoreParts[0], CultureInfo.InvariantCulture),
            int.Parse(shoreParts[1], CultureInfo.InvariantCulture));
        var filledMap = filling.ExportState().Map;
        Assert.Equal(WaterKind.Land, filledMap.HydrologyAt(fillPoint));
        Assert.Contains(CardinalNeighbors(filledMap, fillPoint), point =>
            filledMap.HydrologyAt(point) is WaterKind.River or WaterKind.Lake);

        using var waterConsumer = PrivateWorldRuntime.Restore(filling.ExportState(), _ => new IdleProvider());
        var waterRecipePackage = WaterConsumerFixture();
        waterConsumer.ProposeContent(waterRecipePackage);
        var resolution = waterConsumer.ResolveContent(waterRecipePackage.PackageId);
        Assert.True(resolution.IsSuccess, resolution.Diagnostic);
        waterConsumer.ValidateContent(waterRecipePackage.PackageId, resolution);
        waterConsumer.ApproveContent(waterRecipePackage.PackageId);
        waterConsumer.StageContent(waterRecipePackage.PackageId);
        Assert.True((await waterConsumer.AdvanceOneTickAsync()).Advanced);
        var waterRecipe = waterConsumer.WorldContent.Recipes.Single(recipe => recipe.LocalId == "water-input-fixture");
        var waterLotId = Assert.Single(waterConsumer.Society.Inventory.Lots,
            lot => lot.ContainerLotId == jugId).Id;
        var waterJob = waterConsumer.StartProduction(waterRecipe.CanonicalId, house.InstanceId, actor);
        Assert.True(waterJob.Applied, waterJob.Failure);
        var reservedState = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(waterConsumer.ExportState()));
        Assert.Contains(reservedState.WorldSimulation!.ProductionJobs.Single(job => job.JobId == waterJob.JobId)
            .InputReservationIds, reservationId =>
                reservedState.Society.Society.Inventory.GetReservation(reservationId).LotId == waterLotId);
        using var waterResumed = PrivateWorldRuntime.Restore(reservedState, _ => new IdleProvider());
        for (var tick = 0; tick < waterRecipe.DurationTicks; tick++)
            Assert.True((await waterResumed.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(waterResumed.Society.Inventory.Lots, lot => lot.Id == waterLotId);
        Assert.Equal(householdId, waterResumed.Society.Inventory.GetLot(jugId).OwnerId);
        Assert.Equal(house.InstanceId, waterResumed.Society.Inventory.GetLot(jugId).StorageBuildingId);
        Assert.DoesNotContain(waterResumed.Society.Inventory.Lots, lot => lot.ContainerLotId == jugId);

        var reloadEmptyJug = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(waterResumed.ExportState()));
        var refillProvider = new WaterLoopProvider();
        using var refilling = PrivateWorldRuntime.Restore(reloadEmptyJug, id => id == actor
            ? refillProvider : new IdleProvider());
        for (var tick = 0; tick < 500 &&
             (!refilling.Society.Inventory.Lots.Any(lot => lot.ContainerLotId == jugId) ||
              refilling.Society.Inventory.GetLot(jugId).StorageBuildingId != house.InstanceId); tick++)
            Assert.True((await refilling.AdvanceOneTickAsync()).Advanced);
        var refillCandidates = refillProvider.OfferedCandidates.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var refillProgress = refilling.ExportState();
        Assert.True(refillCandidates.Any(id => id.StartsWith("fill_water_jug:", StringComparison.Ordinal)),
            $"Offered [{string.Join(", ", refillCandidates)}] at tick {refillProgress.Society.Society.WorldTick}; " +
            $"actor status {refillProgress.Society.Society.Inhabitants.Single(person => person.Id == actor).Status}, " +
            $"position {refillProgress.Inhabitants.Single(person => person.InhabitantId == actor).Position}");
        Assert.Contains(refilling.Society.Inventory.Lots, lot => lot.ContainerLotId == jugId &&
            lot.ItemKind == InventoryContainerRules.FreshWater && lot.Quantity == InventoryContainerRules.WaterJugCapacity);
        Assert.Equal(householdId, refilling.Society.Inventory.GetLot(jugId).OwnerId);

        var potId = refilling.Society.Inventory.Lots.Single(lot =>
            lot.ItemKind == InventoryContainerRules.StoragePot).Id;
        var foodState = refilling.ExportState();
        var foodInventory = foodState.Society.Society.Inventory with
        {
            Lots = foodState.Society.Society.Inventory.Lots.Where(lot =>
                    !(lot.OwnerId == householdId && lot.StorageBuildingId == house.InstanceId &&
                      lot.ContainerLotId is null && InventoryContainerRules.IsFood(lot.ItemKind)))
                .Concat([new InventoryLot("test-pot-food", "berries", householdId, 3, 10_000, 10_000,
                    foodState.Society.Society.WorldTick, StorageBuildingId: house.InstanceId)])
                .OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray(),
        };
        foodState = foodState with
        {
            Society = foodState.Society with
            {
                Society = foodState.Society.Society with { Inventory = foodInventory },
            },
        };
        foodState = SetActorCondition(foodState, actor, 10_000, house.Position);
        using var storingFood = PrivateWorldRuntime.Restore(foodState, id => id == actor
            ? new PrefixCandidateProvider("store_food_in_pot") : new IdleProvider());
        for (var tick = 0; tick < 5 &&
             !storingFood.Society.Inventory.Lots.Any(lot => lot.ContainerLotId == potId); tick++)
            Assert.True((await storingFood.AdvanceOneTickAsync()).Advanced);
        var storedFood = Assert.Single(storingFood.Society.Inventory.Lots,
            lot => lot.ContainerLotId == potId);
        Assert.Equal("berries", storedFood.ItemKind);
        Assert.Equal(3, storedFood.Quantity);
        Assert.Equal(householdId, storingFood.Society.Inventory.GetLot(potId).OwnerId);

        var foodReload = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(storingFood.ExportState()));
        foodReload = SetActorCondition(foodReload, actor, 0, house.Position);
        var inventoryWithoutCarriedFood = foodReload.Society.Society.Inventory with
        {
            Lots = foodReload.Society.Society.Inventory.Lots.Where(lot =>
                    !(lot.OwnerId == actor && lot.ContainerLotId is null && lot.DeliveryBuildingId is null &&
                      InventoryContainerRules.IsFood(lot.ItemKind)))
                .ToArray(),
        };
        foodReload = foodReload with
        {
            Society = foodReload.Society with
            {
                Society = foodReload.Society.Society with { Inventory = inventoryWithoutCarriedFood },
            },
        };
        var foodTaker = new PrefixCandidateProvider("take_food_from_pot");
        using var takingFood = PrivateWorldRuntime.Restore(foodReload, id => id == actor
            ? foodTaker : new IdleProvider());
        for (var tick = 0; tick < 30 &&
             !takingFood.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ItemKind == "berries" &&
                 lot.Quantity == 1 && lot.ContainerLotId is null); tick++)
            Assert.True((await takingFood.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(foodTaker.OfferedCandidates, id => id == "take_food_from_pot");
        Assert.DoesNotContain(foodTaker.OfferedCandidates, id => id == "store_food_in_pot" ||
            id == "collect_water_jug" || id == "return_water_jug" ||
            id.StartsWith("fill_water_jug:", StringComparison.Ordinal));
        var remainingPotFood = Assert.Single(takingFood.Society.Inventory.Lots,
            lot => lot.ContainerLotId == potId);
        Assert.Equal(2, remainingPotFood.Quantity);
        Assert.Equal(householdId, takingFood.Society.Inventory.GetLot(potId).OwnerId);
        Assert.Contains(takingFood.Society.Inventory.Lots, lot => lot.OwnerId == actor &&
            lot.ItemKind == "berries" && lot.Quantity == 1 && lot.ContainerLotId is null);
    }

    private static int HouseQuantity(PrivateWorldRuntime world, string ownerId, string buildingId, string itemKind) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == ownerId && lot.StorageBuildingId == buildingId &&
            lot.ItemKind == itemKind).Sum(lot => lot.Quantity);

    private static PrivateWorldRuntimeState SetActorCondition(PrivateWorldRuntimeState state, string actor,
        int hunger, GridPoint position) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with
            {
                Position = position,
                HungerBasisPoints = hunger,
                Survival = person.Survival is { } survival
                    ? survival with { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000 }
                    : null,
            }
            : person).ToArray(),
    };

    private static ContentPackageManifest WaterConsumerFixture()
    {
        const string packageId = "test-water-consumer-v1";
        const string packageIdentity = "test-water-consumer-v1:1.0.0:contained-water-input";
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(packageIdentity)));
        var version = ContentVersion.Parse("1.0.0");
        var recipe = new RecipeDefinition(digest, "water-input-fixture", version, "Use water in a test recipe",
            [new(InventoryContainerRules.FreshWater, InventoryContainerRules.WaterJugCapacity)],
            [new("test-water-product", 1)], 2, HouseContent.House1x1().CanonicalId, ["test", "water-input"]);
        var definition = new ContentDefinition(RecipeDefinition.SchemaKind, recipe.LocalId, version,
            recipe.DisplayName, recipe.PayloadDigest, JsonSerializer.Serialize(new
            {
                schema = "recipe/v1",
                recipe.Inputs,
                recipe.Outputs,
                recipe.DurationTicks,
                recipe.WorkstationBuildingId,
                recipe.Tags,
            }, PayloadOptions));
        return new ContentPackageManifest(packageId, version, digest,
            [new ContentDependency(HouseContent.PackageId,
                new ContentVersionRange(ContentVersion.Parse("1.0.0"), ContentVersion.Parse("2.0.0")))],
            [definition], []);
    }

    private static IEnumerable<GridPoint> CardinalNeighbors(SeededMap map, GridPoint point)
    {
        foreach (var (dx, dy) in new (int X, int Y)[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
        {
            var neighbor = map.WrapColumn(new GridPoint(point.X + dx, point.Y + dy));
            if (map.Contains(neighbor))
                yield return neighbor;
        }
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new CognitionDecisionResponse(
                request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == "safe_idle" ? 1d : 0d)));
    }

    private sealed class PrefixCandidateProvider(string prefix) : IDecisionProvider
    {
        public ConcurrentBag<string> OfferedWorkstationCandidates { get; } = [];
        public ConcurrentBag<string> OfferedCandidates { get; } = [];

        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates)
                OfferedCandidates.Add(candidate.Id);
            foreach (var candidate in request.Observation.Candidates.Where(candidate =>
                         candidate.Id.StartsWith("supply_workstation:", StringComparison.Ordinal)))
                OfferedWorkstationCandidates.Add(candidate.Id);
            var selected = request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith(prefix, StringComparison.Ordinal)) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }

    private sealed class WaterLoopProvider : IDecisionProvider
    {
        public ConcurrentBag<string> OfferedCandidates { get; } = [];

        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates)
                OfferedCandidates.Add(candidate.Id);
            var selected = request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith("fill_water_jug:", StringComparison.Ordinal)) ??
                request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "collect_water_jug") ??
                request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "return_water_jug") ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }
}
