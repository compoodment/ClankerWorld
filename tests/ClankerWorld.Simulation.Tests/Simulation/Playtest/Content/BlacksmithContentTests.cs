using System.Globalization;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class BlacksmithContentTests
{
    [Fact]
    public void BlacksmithManifestMakesEveryAgreedToolFromRealMaterials()
    {
        var manifest = BlacksmithContent.Create();
        var recipes = manifest.Definitions.Where(definition => definition.Kind == RecipeDefinition.SchemaKind).ToArray();
        var outputs = recipes
            .SelectMany(recipe => ReadRecipeQuantities(recipe, "outputs")
                .Select(output => (recipe.LocalId, output.ResourceId)))
            .ToHashSet();
        foreach (var kind in new[]
                 {
                     "wooden_axe", "stone_axe", "iron_axe", "wooden_pickaxe", "stone_pickaxe", "iron_pickaxe",
                     "wooden_hoe", "iron_hoe", "wooden_hammer", "stone_hammer", "wooden_sickle", "iron_sickle",
                     "iron_knife", "iron",
                 })
            Assert.Contains(outputs, output => output.ResourceId == kind);

        var refinement = recipes.Single(recipe => recipe.LocalId == "refine-iron");
        Assert.Equal([new ContentQuantity("iron_ore", 2), new ContentQuantity("wood", 1)],
            ReadRecipeQuantities(refinement, "inputs"));
        Assert.Equal([new ContentQuantity("iron", 1)], ReadRecipeQuantities(refinement, "outputs"));
    }

    private static ContentQuantity[] ReadRecipeQuantities(ContentDefinition recipe, string propertyName)
    {
        using var payload = JsonDocument.Parse(recipe.PayloadJson!);
        return payload.RootElement.GetProperty(propertyName).EnumerateArray()
            .Select(quantity => new ContentQuantity(
                quantity.GetProperty("resourceId").GetString()!,
                quantity.GetProperty("amount").GetInt32()))
            .ToArray();
    }

    [Fact]
    public async Task HouseholdMustDeliverWoodBeforeMakingAndCollectingWoodenAxe()
    {
        using var seed = new PrivateWorldRuntime("blacksmith-stock", _ => new CandidateProvider("safe_idle"),
            startPace: WorldStartPace.FounderSetup);
        var founders = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < founders.Length; index++)
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founders[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 10; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var state = seed.ExportState();
        var alpha = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-alpha").Id;
        var beta = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-beta").Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "smith-stone", "stone", "household:camp-alpha", 4);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        using var placing = PrivateWorldRuntime.Restore(state, _ => new CandidateProvider("safe_idle"));
        var blacksmith = placing.WorldContent.Buildings.Single(item => item.LocalId == "blacksmith-1x2");
        var town = Assert.Single(state.Towns!);
        BuildingPlacementResult? placed = null;
        foreach (var point in state.Map.Tiles.Select(tile => tile.Position)
                     .Where(point => TownBorderRules.IsWithinOrAdjacent(town, point, 1, 2)))
        {
            var attempt = placing.PlaceBuilding("blacksmith-alpha", blacksmith.CanonicalId, point,
                "household:camp-alpha");
            if (!attempt.Applied) continue;
            placed = attempt;
            break;
        }
        Assert.NotNull(placed);
        Assert.Equal("household:camp-alpha", placing.WorldSimulation.Buildings
            .Single(item => item.InstanceId == placed.InstanceId).HouseholdId);
        var axeRecipe = placing.WorldContent.Recipes.Single(item => item.LocalId == "wooden-axe");
        var unstocked = placing.StartProduction(axeRecipe.CanonicalId, placed.InstanceId, alpha);
        Assert.False(unstocked.Applied);
        Assert.Contains("on-site", unstocked.Failure, StringComparison.Ordinal);

        state = placing.ExportState() with
        {
            Inhabitants = placing.ExportState().Inhabitants.Select(person => person.InhabitantId == alpha
                ? person with { Position = state.Map.GetObject("storage").Position, HungerBasisPoints = 9_000 }
                : person).ToArray(),
        };
        using var hauling = PrivateWorldRuntime.Restore(state,
            id => new CandidateProvider(id == alpha ? "haul_smith_input" : "safe_idle"));
        for (var tick = 0; tick < 25 && !hauling.ExportState().Events.Any(item =>
                 item.Kind == "smith_input_picked_up" && item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await hauling.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(hauling.ExportState().Events, item => item.Kind == "smith_input_picked_up" &&
            item.Detail.StartsWith(alpha + ":", StringComparison.Ordinal));
        var carried = hauling.Society.Inventory.Lots.Single(lot => lot.OwnerId == alpha &&
            lot.DeliveryBuildingId == placed.InstanceId);
        Assert.Equal("wood", carried.ItemKind);
        Assert.Empty(new OwnerWorldObservationStore(hauling).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == placed.InstanceId).StoredItems!);

        using var delivering = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(hauling.ExportState())),
            id => new CandidateProvider(id == alpha ? "haul_household_stock" : "safe_idle"));
        for (var tick = 0; tick < 25 && delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId != placed.InstanceId; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(placed.InstanceId, delivering.Society.Inventory.GetLot(carried.Id).StorageBuildingId);
        var outsider = delivering.StartProduction(axeRecipe.CanonicalId, placed.InstanceId, beta);
        Assert.False(outsider.Applied);
        Assert.Contains("Only a member", outsider.Failure, StringComparison.Ordinal);
        var started = delivering.StartProduction(axeRecipe.CanonicalId, placed.InstanceId, alpha);
        Assert.True(started.Applied, started.Failure);
        for (var tick = 0; tick < axeRecipe.DurationTicks; tick++)
            Assert.True((await delivering.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(delivering.Inhabitants.Single(person => person.InhabitantId == alpha).Skills!,
            skill => skill.Kind == SettlementSkillKind.Smithing && skill.TeacherId is null);
        Assert.Contains(new OwnerWorldObservationStore(delivering).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == placed.InstanceId).StoredItems!,
            item => item.Kind == "wooden_axe" && item.Quantity == 1);

        var directDeliveryState = delivering.ExportState();
        var woodBeforeDirectDelivery = directDeliveryState.Society.Society.Inventory.Lots
            .Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        var personalWood = InventoryFixture.AddLot(directDeliveryState.Society.Society.Inventory,
            "personal-blacksmith-wood", "wood", alpha, 2);
        var smithBuilding = delivering.WorldSimulation.Buildings.Single(item => item.InstanceId == placed.InstanceId);
        var smithDefinition = delivering.WorldContent.Buildings.Single(item => item.CanonicalId == smithBuilding.DefinitionId);
        var smithPosition = smithBuilding.Position;
        var buildingTiles = delivering.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(
                delivering.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building));
        var otherOccupied = directDeliveryState.Map.CampObjects.Select(item => item.Position)
            .Concat(directDeliveryState.Map.Resources.Select(item => item.Position))
            .Concat(directDeliveryState.Inhabitants.Where(person => person.InhabitantId != alpha)
                .Select(person => person.Position))
            .Concat(buildingTiles).ToHashSet();
        var remoteStart = directDeliveryState.Map.Tiles.Select(tile => tile.Position)
            .Where(point => directDeliveryState.Map.IsBuildable(point) && !otherOccupied.Contains(point) &&
                !WorldContentSimulationRules.Footprint(smithDefinition, smithBuilding).Contains(point) &&
                directDeliveryState.Map.IsReachableOnFoot(point, smithPosition) &&
                directDeliveryState.Map.FootDistance(point, smithPosition) >= 4)
            .OrderByDescending(point => directDeliveryState.Map.FootDistance(point, smithPosition))
            .ThenBy(point => point.Y).ThenBy(point => point.X).First();
        directDeliveryState = directDeliveryState with
        {
            Society = directDeliveryState.Society with
            {
                Society = directDeliveryState.Society.Society with { Inventory = personalWood },
            },
            Inhabitants = directDeliveryState.Inhabitants.Select(person => person.InhabitantId == alpha
                ? person with
                {
                    Position = remoteStart,
                    HungerBasisPoints = 9_000,
                    LastDecisionContext = null,
                    TravelCooldownTicks = 0,
                    Project = null,
                } : person).ToArray(),
        };
        var directProvider = new CandidateProvider("haul_smith_input", requireCandidate: true);
        using var direct = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(directDeliveryState)),
            id => id == alpha ? directProvider : new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 20 && directProvider.SelectedCandidates.Count == 0; tick++)
            Assert.True((await direct.AdvanceOneTickAsync()).Advanced);
        Assert.Contains("haul_smith_input", directProvider.SelectedCandidates);
        var inTransitWood = direct.Society.Inventory.GetLot("personal-blacksmith-wood");
        Assert.Equal(alpha, inTransitWood.OwnerId);
        Assert.Null(inTransitWood.StorageBuildingId);
        Assert.Null(inTransitWood.DeliveryBuildingId);
        Assert.Null(inTransitWood.GroundPosition);
        Assert.NotEqual(smithPosition, direct.Inhabitants.Single(person => person.InhabitantId == alpha).Position);
        Assert.DoesNotContain(direct.ExportState().Events, item => item.Kind == "smith_input_delivered" &&
            item.Detail.Contains("personal-blacksmith-wood", StringComparison.Ordinal));
        var inTransitBytes = PrivateWorldRuntimeCodec.Encode(direct.ExportState());
        using var directReloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(inTransitBytes),
            _ => new CandidateProvider("safe_idle"));
        Assert.NotEqual(remoteStart, directReloaded.Inhabitants.Single(person => person.InhabitantId == alpha).Position);
        Assert.Equal(inTransitBytes, PrivateWorldRuntimeCodec.Encode(directReloaded.ExportState()));
        for (var tick = 0; tick < 30 && directReloaded.Society.Inventory.GetLot("personal-blacksmith-wood").StorageBuildingId != placed.InstanceId; tick++)
            Assert.True((await directReloaded.AdvanceOneTickAsync()).Advanced);
        var directlyStoredWood = directReloaded.Society.Inventory.GetLot("personal-blacksmith-wood");
        Assert.Equal(("household:camp-alpha", placed.InstanceId, (string?)null, 2),
            (directlyStoredWood.OwnerId, directlyStoredWood.StorageBuildingId,
                directlyStoredWood.DeliveryBuildingId, directlyStoredWood.Quantity));
        Assert.Equal(woodBeforeDirectDelivery + 2,
            directReloaded.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Contains(directReloaded.ExportState().Events, item => item.Kind == "smith_input_delivered" &&
            item.Detail == $"{alpha}:personal-blacksmith-wood:2:{placed.InstanceId}");
        var directBytes = PrivateWorldRuntimeCodec.Encode(directReloaded.ExportState());
        using var directStoredReloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(directBytes),
            _ => new CandidateProvider("safe_idle"));
        Assert.Equal(directBytes, PrivateWorldRuntimeCodec.Encode(directStoredReloaded.ExportState()));

        // Keep the original ore-delivery scenario isolated from the separate
        // direct-carried-input route exercised above.
        var collectingState = delivering.ExportState();
        var brokenAxe = InventoryFixture.AddLot(collectingState.Society.Society.Inventory,
            "owner-broken-axe", "wooden_axe", alpha, 1, conditionBasisPoints: 0);
        collectingState = collectingState with
        {
            Society = collectingState.Society with
            {
                Society = collectingState.Society.Society with { Inventory = brokenAxe },
            },
            Inhabitants = collectingState.Inhabitants.Select(person => person.InhabitantId == alpha
                ? person with { LastDecisionContext = null } : person).ToArray(),
        };
        using var collecting = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(collectingState)),
            id => new CandidateProvider(id == alpha ? "collect_wooden_axe" : "safe_idle"));
        for (var tick = 0; tick < 20 && ToolProgressionRules.BestUsableTool(collecting.Society.Inventory, alpha,
                 ToolFamily.Axe) is null; tick++)
            Assert.True((await collecting.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(collecting.Society.Inventory.Lots, lot => lot.Id == "owner-broken-axe" &&
            lot.ConditionBasisPoints == 0);
        Assert.Contains(collecting.Society.Inventory.Lots, lot => lot.OwnerId == "household:camp-alpha" &&
            lot.CarrierId == alpha && lot.ItemKind == "wooden_axe" && lot.ConditionBasisPoints > 0 &&
            lot.StorageBuildingId is null);

        var refiningRecipe = collecting.WorldContent.Recipes.Single(item => item.LocalId == "refine-iron");
        var noOre = collecting.StartProduction(refiningRecipe.CanonicalId, placed.InstanceId, alpha);
        Assert.False(noOre.Applied);
        Assert.Contains("on-site", noOre.Failure, StringComparison.Ordinal);
        var oreState = collecting.ExportState();
        oreState = oreState with
        {
            Society = oreState.Society with
            {
                Society = oreState.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(oreState.Society.Society.Inventory,
                        "smith-test-ore", "iron_ore", alpha, 2),
                },
            },
            Inhabitants = oreState.Inhabitants.Select(person => person.InhabitantId == alpha
                ? person with
                {
                    Position = collecting.WorldSimulation.Buildings.Single(item => item.InstanceId == placed.InstanceId).Position,
                    HungerBasisPoints = 9_000,
                    LastDecisionContext = null,
                    TravelCooldownTicks = 0,
                }
                : person).ToArray(),
        };
        var oreProvider = new CandidateProvider("deliver_smith_ore", requireCandidate: true);
        using var deliveringOre = PrivateWorldRuntime.Restore(oreState,
            id => id == alpha ? oreProvider : new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 20 && deliveringOre.Society.Inventory.Lots.All(lot =>
                 lot.ItemKind != "iron_ore" || lot.StorageBuildingId != placed.InstanceId); tick++)
            Assert.True((await deliveringOre.AdvanceOneTickAsync()).Advanced);
        Assert.True(deliveringOre.Society.Inventory.Lots.Any(lot => lot.OwnerId == "household:camp-alpha" &&
            lot.ItemKind == "iron_ore" && lot.StorageBuildingId == placed.InstanceId && lot.Quantity == 2),
            $"position={deliveringOre.Inhabitants.Single(person => person.InhabitantId == alpha).Position}; " +
            $"selected={string.Join(',', oreProvider.SelectedCandidates)}; " +
            $"ore={string.Join(';', deliveringOre.Society.Inventory.Lots.Where(lot => lot.ItemKind == "iron_ore"))}; " +
            $"events={string.Join(';', deliveringOre.ExportState().Events.Where(item => item.Kind.Contains("smith", StringComparison.Ordinal)))}");
        using var refining = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(deliveringOre.ExportState())),
            _ => new CandidateProvider("safe_idle"));
        var refiningStart = refining.StartProduction(refiningRecipe.CanonicalId, placed.InstanceId, alpha);
        Assert.True(refiningStart.Applied, refiningStart.Failure);
        for (var tick = 0; tick < refiningRecipe.DurationTicks; tick++)
            Assert.True((await refining.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(new OwnerWorldObservationStore(refining).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == placed.InstanceId).StoredItems!,
            item => item.Kind == "iron" && item.Quantity == 1);
    }

    [Fact]
    public async Task HouseholdCanHandGatherFiniteFallenWoodWhenNoAxeIsUsableAndSaveIt()
    {
        using var seed = new PrivateWorldRuntime("hand-gather-fallen-wood",
            _ => new CandidateProvider("safe_idle"), startPace: WorldStartPace.FounderSetup);
        var founders = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < founders.Length; index++)
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founders[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 10; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var state = seed.ExportState();
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-alpha").Id;
        var householdId = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var source = Assert.Single(state.Map.Resources, resource => resource.NaturalObjectKind == "fallen_wood");
        Assert.True(state.Map.IsReachableFromCampOnFoot(source.Position));
        var building = seed.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot =>
                lot.OwnerId != householdId && lot.OwnerId != actor || lot.ItemKind != "wood" &&
                ToolProgressionRules.Find(lot.ItemKind)?.Family != ToolFamily.Axe).ToArray(),
        };
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = source.Position,
                    HungerBasisPoints = 9_000,
                    LastDecisionContext = null,
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(
                            building.CanonicalId, source.Position), building.DisplayName,
                        state.Society.Society.WorldTick, "acquiring",
                        LastTransitionTick: state.Society.Society.WorldTick),
                }
                : person).ToArray(),
        };
        using var gathering = PrivateWorldRuntime.Restore(state, _ => new CandidateProvider("safe_idle"));
        for (var tick = 0; tick < 10 && !gathering.ExportState().Events.Any(item =>
                 item.Kind == "material_gathered" && item.Detail == $"{actor}:wood:1"); tick++)
            Assert.True((await gathering.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(gathering.ExportState().Events, item => item.Kind == "material_gathered" &&
            item.Detail == $"{actor}:wood:1");
        Assert.Contains(gathering.Society.Inventory.Lots, lot => lot.OwnerId == actor &&
            lot.ItemKind == "wood" && lot.Quantity == 1);
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(gathering.ExportState()));
        using var reloaded = PrivateWorldRuntime.Restore(saved, _ => new CandidateProvider("safe_idle"));
        Assert.Contains(reloaded.Society.Inventory.Lots, lot => lot.OwnerId == actor &&
            lot.ItemKind == "wood" && lot.Quantity == 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlacksmithRepairsOneBrokenUnitUsingTheOwnersCarriedMaterialAndRoundTripsIt(bool borrowed)
    {
        using var seed = new PrivateWorldRuntime("repair-broken-tool", _ => new CandidateProvider("safe_idle"),
            startPace: WorldStartPace.FounderSetup);
        var founders = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < founders.Length; index++)
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founders[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 10; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var state = seed.ExportState();
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-alpha").Id;
        var householdId = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "smith-wood", "wood", householdId, 12);
        inventory = InventoryFixture.AddLot(inventory, "smith-stone", "stone", householdId, 4);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        using var placing = PrivateWorldRuntime.Restore(state, _ => new CandidateProvider("safe_idle"));
        var definition = placing.WorldContent.Buildings.Single(item => item.LocalId == "blacksmith-1x2");
        var town = Assert.Single(state.Towns!);
        BuildingPlacementResult? placed = null;
        foreach (var point in state.Map.Tiles.Select(tile => tile.Position)
                     .Where(point => TownBorderRules.IsWithinOrAdjacent(town, point, 1, 2)))
        {
            var attempt = placing.PlaceBuilding("repair-blacksmith", definition.CanonicalId, point, householdId);
            if (!attempt.Applied) continue;
            placed = attempt;
            break;
        }
        Assert.NotNull(placed);

        state = placing.ExportState();
        inventory = state.Society.Society.Inventory;
        // A borrowed household axe is repaired for the household with the member's own wood.
        var toolOwner = borrowed ? householdId : actor;
        inventory = InventoryFixture.AddLot(inventory, "broken-axes", "wooden_axe", toolOwner, 2,
            conditionBasisPoints: 0);
        if (borrowed)
            inventory = InventoryFixture.Relocate(inventory, "borrow-axes", "broken-axes", householdId, 2, actor);
        inventory = InventoryFixture.AddLot(inventory, "repair-wood", "wood", actor, 1);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = placing.WorldSimulation.Buildings.Single(item => item.InstanceId == placed.InstanceId).Position,
                    HungerBasisPoints = 9_000,
                    LastDecisionContext = null,
                }
                : person).ToArray(),
        };

        using var repairing = PrivateWorldRuntime.Restore(state,
            _ => new CandidateProvider("repair_tool:broken-axes"));
        for (var tick = 0; tick < 10 && !repairing.ExportState().Events.Any(item => item.Kind == "tool_repaired"); tick++)
            Assert.True((await repairing.AdvanceOneTickAsync()).Advanced);
        var repaired = repairing.Society.Inventory.Lots.Single(lot => lot.Id.StartsWith("broken-axes:repair:",
            StringComparison.Ordinal) && lot.OwnerId == toolOwner);
        Assert.True(PersonalEquipmentRules.IsCarried(repaired, actor));
        Assert.Equal((1, 10_000), (repaired.Quantity, repaired.ConditionBasisPoints));
        Assert.Contains(repairing.Society.Inventory.Lots, lot => lot.Id == "broken-axes" &&
            lot.Quantity == 1 && lot.ConditionBasisPoints == 0);
        Assert.DoesNotContain(repairing.Society.Inventory.Lots, lot => lot.Id.StartsWith("repair-wood", StringComparison.Ordinal));
        Assert.Contains(repairing.ExportState().Events, item => item.Kind == "tool_repaired" &&
            item.Detail == $"{actor}:{repaired.Id}:{placed.InstanceId}");

        var restored = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(repairing.ExportState()));
        Assert.Equal(repairing.Society.Inventory.Lots, restored.Society.Society.Inventory.Lots);
        using var reloaded = PrivateWorldRuntime.Restore(restored, _ => new CandidateProvider("safe_idle"));
        Assert.Equal(repaired.ConditionBasisPoints, reloaded.Society.Inventory.GetLot(repaired.Id).ConditionBasisPoints);
    }

    private sealed class CandidateProvider(string candidateId, bool requireCandidate = false) : IDecisionProvider
    {
        public List<string> SelectedCandidates { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == candidateId) ??
                (requireCandidate
                    ? throw new InvalidOperationException($"Expected candidate '{candidateId}' was unavailable.")
                    : request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle"));
            SelectedCandidates.Add(selected.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }
}
