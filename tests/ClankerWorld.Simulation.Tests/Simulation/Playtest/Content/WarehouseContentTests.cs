using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class WarehouseContentTests
{
    [Fact]
    public async Task OrdinaryDonationsRetainOnePersonalReserveAcrossSplitLotsAndReload()
    {
        var state = ShelterOrderTestFixture.WithClearWeather(ShelterOrderTestFixture.Prepared());
        var actor = ShelterOrderTestFixture.Actor(state);
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        state = ShelterOrderTestFixture.At(state, actor, warehouse.Position);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "donation-a", "wood", actor, 4);
        inventory = InventoryFixture.AddLot(inventory, "donation-b", "wood", actor, 4);
        state = ShelterOrderTestFixture.WithInventory(state, inventory);
        IDecisionProvider Provider(string id) => new CandidateProvider(id == actor ? "store_town_resources" : "safe_idle");
        using var world = PrivateWorldRuntime.Restore(state, Provider);
        for (var tick = 0; tick < 10 && !world.ExportState().Events.Any(item => item.Kind == "town_resources_stored"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_resources_stored");
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), Provider);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" && lot.OwnerId == actor &&
            PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" && lot.OwnerId == warehouse.TownId &&
            lot.StorageBuildingId == warehouse.InstanceId).Sum(lot => lot.Quantity));
        Assert.Equal(8, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_resources_stored");
        world.Validate();
    }

    [Fact]
    public async Task ResidentsStoreAndCollectNonFoodAtTheWarehouseAcrossReload()
    {
        using var seed = new PrivateWorldRuntime("warehouse-stock", _ => new CandidateProvider("safe_idle"),
            startPace: WorldStartPace.FounderSetup);
        var founders = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < founders.Length; index++)
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founders[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 9; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var definition = seed.WorldContent.Buildings.Single(item => item.LocalId == "warehouse-2x2");
        Assert.Equal((2, 2), (definition.Width, definition.Height));
        var initial = seed.ExportState();
        var town = Assert.Single(initial.Towns!);
        BuildingPlacementResult? placed = null;
        foreach (var point in initial.Map.Tiles.Select(tile => tile.Position)
                     .Where(point => TownBorderRules.IsWithinOrAdjacent(town, point, 2, 2)))
        {
            var attempt = seed.PlaceBuilding("town-warehouse", definition.CanonicalId, point);
            if (!attempt.Applied) continue;
            placed = attempt;
            break;
        }
        Assert.NotNull(placed);
        var warehouse = seed.WorldSimulation.Buildings.Single(item => item.InstanceId == placed.InstanceId);
        Assert.Equal(town.Id, warehouse.TownId);
        var duplicate = seed.PlaceBuilding("second-town-warehouse", definition.CanonicalId, warehouse.Position);
        Assert.False(duplicate.Applied);
        Assert.Contains("already", duplicate.Failure, StringComparison.OrdinalIgnoreCase);

        var state = seed.ExportState();
        var alpha = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-alpha").Id;
        var beta = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-beta").Id;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == alpha
                ? person with { Position = warehouse.Position, HungerBasisPoints = 9_000 } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
                        "personal-warehouse-wood", "wood", alpha, 8),
                },
            },
        };
        using var depositing = PrivateWorldRuntime.Restore(state,
            id => new CandidateProvider(id == alpha ? "store_town_resources" : "safe_idle"));
        for (var tick = 0; tick < 20 && !depositing.ExportState().Events.Any(item =>
                 item.Kind == "town_resources_stored" && item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await depositing.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(depositing.ExportState().Events, item => item.Kind == "town_resources_stored" &&
            item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal));
        Assert.Equal(4, depositing.Society.Inventory.GetLot("personal-warehouse-wood").Quantity);
        var stored = new OwnerWorldObservationStore(depositing).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == warehouse.InstanceId).StoredItems!;
        Assert.Contains(stored, item => item.Kind == "wood" && item.Quantity == 4);

        var reloaded = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(depositing.ExportState()));
        var invalidFood = reloaded with
        {
            Society = reloaded.Society with
            {
                Society = reloaded.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(reloaded.Society.Society.Inventory,
                        "invalid-warehouse-food", "food", town.Id, 1, storageBuildingId: warehouse.InstanceId),
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalidFood));
        var invalidGrain = invalidFood with
        {
            Society = invalidFood.Society with
            {
                Society = invalidFood.Society.Society with
                {
                    Inventory = invalidFood.Society.Society.Inventory with
                    {
                        Lots = invalidFood.Society.Society.Inventory.Lots.Select(lot =>
                            lot.Id == "invalid-warehouse-food" ? lot with { ItemKind = "grain" } : lot).ToArray(),
                    },
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalidGrain));

        var house = seed.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
        var currentTown = reloaded.Towns!.Single(item => item.Id == town.Id);
        var buildingDefinitions = seed.WorldContent.Buildings.ToDictionary(item => item.CanonicalId,
            StringComparer.Ordinal);
        var worldSimulation = reloaded.WorldSimulation!;
        var occupied = reloaded.Map.CampObjects.Select(item => item.Position)
            .Concat(reloaded.Map.Resources.Select(item => item.Position))
            .Concat(reloaded.Inhabitants.Select(item => item.Position))
            .Concat(reloaded.RoadTiles ?? [])
            .Concat((reloaded.Bridges ?? []).SelectMany(item => item.Entrances))
            .Concat((reloaded.Fields ?? []).Select(item => item.Position))
            .Concat((worldSimulation.BuildingExpansions ?? []).Where(job => job.State == WorldProductionJobState.Running)
                .SelectMany(job => Enumerable.Range(0, job.TargetFootprint.Height).SelectMany(dy =>
                    Enumerable.Range(0, job.TargetFootprint.Width).Select(dx =>
                        new GridPoint(job.TargetPosition.X + dx, job.TargetPosition.Y + dy)))))
            .Concat(worldSimulation.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(buildingDefinitions[building.DefinitionId], building)))
            .ToHashSet();
        GridPoint FindOpenSite(BuildingDefinition definition) => reloaded.Map.Tiles.Select(tile => tile.Position)
            .OrderBy(point => reloaded.Map.FootDistance(warehouse.Position, point))
            .ThenBy(point => point.Y).ThenBy(point => point.X)
            .First(point => TownBorderRules.IsWithinOrAdjacent(currentTown, point, definition.Width, definition.Height) &&
                WorldContentSimulationRules.Footprint(definition, point)
                    .All(tile => reloaded.Map.IsBuildable(tile) && !occupied.Contains(tile)));
        var houseSite = FindOpenSite(house);
        var materialPickup = reloaded with
        {
            Inhabitants = reloaded.Inhabitants.Select(person => person.InhabitantId == beta
                ? person with
                {
                    Position = warehouse.Position,
                    HungerBasisPoints = 9_000,
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(house.CanonicalId,
                            houseSite), house.DisplayName, reloaded.Society.Society.WorldTick,
                        "acquiring", LastTransitionTick: reloaded.Society.Society.WorldTick),
                } : person).ToArray(),
            Society = reloaded.Society with
            {
                Society = reloaded.Society.Society with
                {
                    Inventory = reloaded.Society.Society.Inventory with
                    {
                        Lots = reloaded.Society.Society.Inventory.Lots.Where(lot =>
                            lot.OwnerId != "household:camp-beta" || lot.ItemKind != "wood").ToArray(),
                    },
                },
            },
        };
        using (var collectingMaterials = PrivateWorldRuntime.Restore(materialPickup, _ => new CandidateProvider("safe_idle")))
        {
            for (var tick = 0; tick < 20 && !collectingMaterials.ExportState().Events.Any(item =>
                     item.Kind == "town_resource_collected" && item.Detail.StartsWith(beta + ":", StringComparison.Ordinal)); tick++)
                Assert.True((await collectingMaterials.AdvanceOneTickAsync()).Advanced);
            Assert.Contains(collectingMaterials.ExportState().Events, item => item.Kind == "town_resource_collected" &&
                item.Detail.StartsWith(beta + ":", StringComparison.Ordinal));
            Assert.Contains(collectingMaterials.Society.Inventory.Lots, lot => lot.OwnerId == beta &&
                lot.ItemKind == "wood" && lot.StorageBuildingId is null);
        }

        var inventory = InventoryFixture.AddLot(reloaded.Society.Society.Inventory,
            "warehouse-tool", "wooden_hammer", town.Id, 1, storageBuildingId: warehouse.InstanceId);
        var farmWorkers = (reloaded.Fields ?? []).Where(field => field.Work is not null)
            .Select(field => field.Work!.WorkerId).ToHashSet(StringComparer.Ordinal);
        var toolCollectorId = reloaded.Society.Society.Inhabitants
            .Where(person => person.Status == SocietyInhabitantStatus.Active &&
                person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder &&
                currentTown.ResidentIds.Contains(person.Id, StringComparer.Ordinal) &&
                !farmWorkers.Contains(person.Id))
            .OrderBy(person => person.Id, StringComparer.Ordinal)
            .Select(person => person.Id)
            .First();
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot =>
                !lot.OwnerId.StartsWith("household:", StringComparison.Ordinal) || lot.ItemKind != "wooden_hammer").ToArray()
        };
        reloaded = reloaded with
        {
            Inhabitants = reloaded.Inhabitants.Select(person => person.InhabitantId == toolCollectorId
                ? person with
                {
                    Position = warehouse.Position,
                    HungerBasisPoints = 10_000,
                    Project = null,
                    LastDecisionContext = null,
                    TravelCooldownTicks = 0,
                } : person).ToArray(),
            Society = reloaded.Society with { Society = reloaded.Society.Society with { Inventory = inventory } },
        };
        var toolCollector = new CandidateProvider("collect_tool:wooden_hammer", requireCandidate: true);
        using var collecting = PrivateWorldRuntime.Restore(reloaded,
            id => id == toolCollectorId ? toolCollector : new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 20 && !collecting.ExportState().Events.Any(item =>
                 item.Kind == "equipment_collected" && item.Detail == toolCollectorId + ":wooden_hammer"); tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        var finalCollectionState = collecting.ExportState();
        var finalCollector = finalCollectionState.Inhabitants.Single(person => person.InhabitantId == toolCollectorId);
        var finalInventory = finalCollectionState.Society.Society.Inventory;
        var carried = finalInventory.Lots.Where(lot => PersonalEquipmentRules.IsPhysicallyCarried(
                finalInventory, lot, toolCollectorId))
            .Select(lot => $"{lot.Id}={lot.ItemKind}x{lot.Quantity}").ToArray();
        Assert.True(finalCollectionState.Events.Any(item => item.Kind == "equipment_collected" &&
                item.Detail == toolCollectorId + ":wooden_hammer"),
            $"The warehouse's physical hammer should be collected by its available pickup action; " +
            $"collector={toolCollectorId}, free capacity={PersonalEquipmentRules.FreeCapacity(finalInventory, toolCollectorId, finalCollector.Equipment)}, " +
            $"carried=[{string.Join(",", carried)}], position={finalCollector.Position}, blocker={finalCollector.Project?.Blocker}, " +
            $"choices=[{string.Join(",", toolCollector.SelectedCandidates)}], " +
            $"observed=[{string.Join(";", toolCollector.ObservedCandidates)}].");
        Assert.Equal(toolCollectorId, collecting.Society.Inventory.GetLot("warehouse-tool").OwnerId);
        Assert.Null(collecting.Society.Inventory.GetLot("warehouse-tool").StorageBuildingId);
    }

    private sealed class CandidateProvider(string candidateId, bool requireCandidate = false) : IDecisionProvider
    {
        public List<string> SelectedCandidates { get; } = [];
        public List<string> ObservedCandidates { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            ObservedCandidates.Add(request.Observation.InhabitantId + "=[" +
                string.Join(",", request.Observation.Candidates.Select(candidate => candidate.Id)) + "]");
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == candidateId);
            if (selected is null && requireCandidate)
                throw new InvalidOperationException($"Expected '{candidateId}' candidate; available: " +
                    string.Join(",", request.Observation.Candidates.Select(candidate => candidate.Id)));
            selected ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            SelectedCandidates.Add(request.Observation.InhabitantId + "=" + selected.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }
}
