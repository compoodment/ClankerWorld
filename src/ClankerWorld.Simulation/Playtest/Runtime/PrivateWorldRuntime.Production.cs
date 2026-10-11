using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    // The occupied tiles and foot costs a layout reads. Building them gathers everything in the world and
    // walks the map from the actor, so a caller laying out several buildings of one kind builds them once.
    // A household building avoids only other households' land; any other building avoids all of it.
    private sealed record TownLayoutBasis(string Actor, GridPoint? SelectedSite, bool HouseholdBuilding, string? TownProjectId,
        HashSet<GridPoint> Occupied, Dictionary<GridPoint, int> FootCosts);

    private static bool IsHouseholdLayout(BuildingDefinition? building) =>
        building is null || building.Tags.Any(IsHouseholdBuildingTag);

    private TownLayoutBasis CreateTownLayoutBasis(string actor, GridPoint? selectedSite = null,
        BuildingDefinition? building = null, string? townProjectId = null)
    {
        var origin = inhabitants[actor].Position;
        var town = towns.SingleOrDefault(item => item.ResidentIds.Contains(actor, StringComparer.Ordinal));
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var occupied = map.CampObjects.Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .Concat(RoadAndBridgeTiles())
            .Concat(fields.Select(field => field.Position))
            .Concat(TownProjectProtectedSites(townProjectId))
            .Concat(MarketSiteTiles())
            .Concat((worldSimulation.BuildingExpansions ?? []).Where(job => job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused).SelectMany(ExpansionTiles))
            .Concat(worldSimulation.Buildings.SelectMany(building =>
            {
                if (!definitions.TryGetValue(building.DefinitionId, out var definition))
                    throw new InvalidDataException("A placed building has no active definition.");
                return WorldContentSimulationRules.Footprint(definition, building);
            }))
            .Concat(inhabitants.Values.Where(person => person.InhabitantId != actor)
                .Select(person => person.Position))
            // A household builds only on land no other household holds or has asked for; Town buildings avoid it all.
            .Concat(HouseholdLandHeldByOthers(IsHouseholdLayout(building) ? HouseholdFor(actor) : null))
            .ToHashSet();
        return new(actor, selectedSite, IsHouseholdLayout(building), townProjectId,
            occupied, FindUnoccupiedFootCosts(actor, origin, town, occupied, selectedSite));
    }

    private TownLayoutContext CreateTownLayoutContext(string actor, GridPoint? selectedSite = null,
        BuildingDefinition? building = null, bool forTownProject = false, string? townProjectId = null,
        TownLayoutBasis? basis = null)
    {
        if (basis is not null && (basis.Actor != actor || basis.SelectedSite != selectedSite ||
                basis.HouseholdBuilding != IsHouseholdLayout(building) || basis.TownProjectId != townProjectId))
            throw new InvalidOperationException("A Town layout was given a basis built for another layout.");
        basis ??= CreateTownLayoutBasis(actor, selectedSite, building, townProjectId);
        var town = towns.SingleOrDefault(item => item.ResidentIds.Contains(actor, StringComparer.Ordinal));
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var resourcesForLayout = map.Resources.Select(resource => new TownLayoutResource(
            resource,
            resources.GetValueOrDefault(resource.Id) == ResourceState.Available));
        var buildingsForLayout = worldSimulation.Buildings
            .Where(building => definitions.ContainsKey(building.DefinitionId))
            .Select(building => new TownLayoutBuilding(building, BuildingStorageRules.EffectiveDefinition(definitions[building.DefinitionId], building)));
        return new TownLayoutContext(
            map,
            town,
            basis.Occupied,
            basis.FootCosts,
            resourcesForLayout,
            buildingsForLayout,
            roadTiles: roadTiles,
            requiredNeighborTiles: building is not null && HouseholdBuildingKind(building) == "silo" ? SiloNeighborTiles(actor, definitions) : null,
            requiredLandTiles: forTownProject && town is not null ? TownProjectLandTiles(town) : null,
            requiredEntranceOffset: forTownProject
                ? building?.Tags.Contains(MarketContent.HallTag, StringComparer.Ordinal) == true ? new GridPoint(1, 2) : new GridPoint(1, 4)
                : null,
            requiredFootprintOffsets: forTownProject && building?.Tags.Contains(MarketContent.HallTag, StringComparer.Ordinal) == true
                ? MarketContent.SiteTiles(new(0, 0)) : null,
            permittedRoadOffsets: forTownProject && building?.Tags.Contains(MarketContent.HallTag, StringComparer.Ordinal) == true
                ? MarketContent.PlazaTiles(new(0, 0)).Except(Enumerable.Range(0, MarketContent.MaximumStalls)
                    .Select(slot => MarketContent.StallSite(new(0, 0), slot))) : null,
            protectedTiles: TownProjectProtectedSites(townProjectId).Concat(bridges.SelectMany(item => item.Entrances)));
    }

    /// <summary>A Silo stands near its household's Farmhouse; no Farmhouse means no legal Silo site.</summary>
    private GridPoint[] SiloNeighborTiles(string actor,
        Dictionary<string, BuildingDefinition> definitions) =>
        society.Checkpoint.GetInhabitant(actor).HouseholdId is { } householdId &&
        FarmhouseForHousehold(householdId) is { } farmhouse
            ? WorldContentSimulationRules.Footprint(definitions[farmhouse.DefinitionId], farmhouse.Position).ToArray()
            : [];

    private Dictionary<GridPoint, int> FindUnoccupiedFootCosts(
        string inhabitantId, GridPoint origin, TownRuntimeState? town,
        HashSet<GridPoint> occupiedSites, GridPoint? selectedSite)
    {
        if (selectedSite is { } invalidSite &&
            (!map.IsBuildable(invalidSite) || occupiedSites.Contains(invalidSite)))
            return new Dictionary<GridPoint, int> { [origin] = 0 };
        var occupied = inhabitants.Values
            .Where(item => item.InhabitantId != inhabitantId)
            .Select(item => item.Position)
            .ToHashSet();
        // A Town has bounded construction anchors. Legacy worlds without a
        // Town consider the nearest 32 viable anchors; an already chosen site
        // remains a mandatory route target, however far away it was saved.
        IReadOnlyList<GridPoint>? anchors = town is null ? null : TownLayoutContext.CandidateBounds(map, town);
        if (selectedSite is { } chosenSite && anchors is not null)
            anchors = anchors.Contains(chosenSite) ? [chosenSite] : [];
        var pendingAnchors = anchors?.Where(point => map.IsBuildable(point) && !occupiedSites.Contains(point))
            .ToHashSet();
        var open = new PriorityQueue<GridPoint, (int Cost, int Y, int X, int Order)>();
        // Layout searches can visit most of a generated map. Keep tentative
        // costs in a rented tile array rather than allocating a second map-sized
        // dictionary alongside the settled costs every time a layout is built.
        var tileCount = checked(map.Width * map.Height);
        var settled = new Dictionary<GridPoint, int>();
        var viableLegacyAnchors = 0;
        var selectedSiteReached = selectedSite is null ||
            pendingAnchors is not null && pendingAnchors.Count == 0;
        var order = 0;
        var best = ArrayPool<int>.Shared.Rent(tileCount);
        try
        {
            Array.Fill(best, -1, 0, tileCount);
            best[origin.Y * map.Width + origin.X] = 0;
            open.Enqueue(origin, (0, origin.Y, origin.X, order++));
            while (open.TryDequeue(out var current, out var priority))
            {
                if (priority.Cost != best[current.Y * map.Width + current.X])
                    continue;
                settled[current] = priority.Cost;
                if (current == selectedSite)
                    selectedSiteReached = true;
                if (pendingAnchors is null)
                {
                    if (map.IsBuildable(current) && !occupiedSites.Contains(current))
                        viableLegacyAnchors++;
                }
                else
                {
                    pendingAnchors.Remove(current);
                }
                if (selectedSite is not null ? selectedSiteReached :
                    pendingAnchors?.Count == 0 || pendingAnchors is null && viableLegacyAnchors >= 32)
                    break;
                foreach (var next in map.FootNeighbors(current))
                {
                    if (occupied.Contains(next) ||
                        map.IsDiagonalFootStep(current, next) &&
                        (occupied.Contains(new GridPoint(next.X, current.Y)) ||
                         occupied.Contains(new GridPoint(current.X, next.Y))))
                        continue;

                    var cost = checked(priority.Cost + LegalRoadStepCost(current, next));
                    var index = next.Y * map.Width + next.X;
                    if (best[index] >= 0 && best[index] <= cost)
                        continue;
                    best[index] = cost;
                    open.Enqueue(next, (cost, next.Y, next.X, order++));
                }
            }
            return settled;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(best);
        }
    }

    private bool TryFindRecipeSite(
        RecipeDefinition recipe,
        out string siteId,
        out GridPoint position,
        string? actorId = null)
    {
        if (recipe.WorkstationBuildingId is null)
        {
            siteId = string.Empty;
            position = default;
            return false;
        }

        var productionOrder = actorId is null ? null : PendingInstructionFor(actorId)?.Order;
        if (productionOrder?.Action != "produce_item" || productionOrder.TargetRecipeId != recipe.CanonicalId)
            productionOrder = null;
        foreach (var placed in worldSimulation.Buildings.OrderBy(item => item.InstanceId, StringComparer.Ordinal))
        {
            if (productionOrder is not null &&
                (productionOrder.ProductionBuildingId is { } requiredBuilding && placed.InstanceId != requiredBuilding ||
                 productionOrder.TargetPosition is { } requestedPosition && placed.Position != requestedPosition))
                continue;
            if (placed.DefinitionId != recipe.WorkstationBuildingId ||
                placed.HouseholdId is not null && (actorId is null ||
                    placed.HouseholdId != society.Checkpoint.GetInhabitant(actorId).HouseholdId))
            {
                continue;
            }

            var definition = worldContent.Buildings.Single(item => item.CanonicalId == placed.DefinitionId);
            // A household building nobody holds is not anyone's to use.
            if (placed.HouseholdId is null && definition.Tags.Any(IsHouseholdBuildingTag))
                continue;
            var activeJobs = worldSimulation.ProductionJobs.Count(item =>
                item.BuildingInstanceId == placed.InstanceId && item.State is WorldProductionJobState.Running or WorldProductionJobState.Paused);
            if (activeJobs < definition.Capacity &&
                (actorId is null || FindUnoccupiedRoute(actorId, inhabitants[actorId].Position, placed.Position, 0).Count > 0))
            {
                siteId = placed.InstanceId;
                position = placed.Position;
                return true;
            }
        }

        siteId = string.Empty;
        position = default;
        return false;
    }

    private static bool IsGenericFoodRecipe(RecipeDefinition recipe) =>
        recipe.Inputs.Any(input => input.ResourceId == "food") &&
        recipe.Outputs.Any(output => output.ResourceId == "food");

    // Workstation output has no destination vessel; fresh water cannot be a loose lot.
    private static bool HasUnsupportedWaterOutput(RecipeDefinition recipe) =>
        recipe.Outputs.Any(output => output.ResourceId == InventoryContainerRules.FreshWater);

    private bool HasAvailableQuantities(IReadOnlyList<ContentQuantity> quantities, string? ownerId = null)
    {
        var inventory = society.Checkpoint.Inventory;
        foreach (var requested in quantities)
        {
            var available = FindGoods(inventory, new(GoodsUse.Holdings, null,
                    GoodsOwners.One(ownerId ?? HouseholdId), GoodsKinds.One(requested.ResourceId)))
                .Matches.Where(match => !IsHandcartCargo(inventory, match.Lot)).Sum(match => (long)match.Quantity);
            if (available < requested.Amount)
            {
                return false;
            }
        }

        return true;
    }

    private bool HasCarriedUnreservedQuantities(string actor, IReadOnlyList<ContentQuantity> quantities)
    {
        var inventory = society.Checkpoint.Inventory;
        foreach (var requested in quantities.GroupBy(item => item.ResourceId, StringComparer.Ordinal))
        {
            var available = inventory.Lots
                .Where(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsPhysicallyCarried(inventory, lot, actor) &&
                    lot.DeliveryBuildingId is null && lot.ItemKind == requested.Key)
                .Sum(lot => (long)AvailableLotQuantity(lot));
            if (available < requested.Sum(item => (long)item.Amount))
                return false;
        }

        return true;
    }

    private string BuildingConstructionOwner(string actor, BuildingDefinition definition)
    {
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (household is null) return actor;
        if (!definition.Tags.Any(IsHouseholdBuildingTag)) return HouseholdId;
        if (definition.Tags.Contains("house", StringComparer.Ordinal) && HouseForHousehold(household) is null &&
            HasCarriedUnreservedQuantities(actor, definition.BuildCosts))
            return actor;

        // The household stages a multi-load first House at camp. When its
        // complete cost is already on the builder, preserve direct delivery.
        return household;
    }

    private string BuildInstanceId(string inhabitantId, BuildingDefinition definition)
    {
        // Preserve valid legacy IDs; descendant identities contain separators
        // that are legal society IDs but invalid content instance IDs.
        var original = inhabitantId.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-' or '_')
            ? $"build-{inhabitantId}-{definition.PackageDigest[7..15]}-{definition.LocalId}"
            : "build-v2-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inhabitantId + "\n" + definition.CanonicalId)));
        var candidate = original;
        // Historical order bindings keep their identity. Ordinary paid rebuilding
        // selects a stable replacement without changing those bindings or payments.
        for (var replacement = 1; BuildingIdentityIsReserved(candidate); replacement++)
            candidate = "build-replacement-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                original + "\n" + replacement.ToString(CultureInfo.InvariantCulture))));
        return candidate;
    }

    // A synchronous proposal query checks many sites against the same objects.
    // Keep Market tiles separate: only an extra stall may use its own slot,
    // and that exception never opens a tile occupied by another kind of object.
    private sealed record BuildingPlacementTiles(HashSet<GridPoint> Occupied, HashSet<GridPoint> Market,
        string? Failure = null);

    private sealed record BuildingPlacementWorld(string? ProjectId, Lazy<BuildingPlacementTiles> Tiles);

    private BuildingPlacementWorld CreateBuildingPlacementWorld(string? townProjectId) =>
        new(townProjectId, new(() => GatherBuildingPlacementTiles(townProjectId), LazyThreadSafetyMode.None));

    private BuildingPlacementTiles GatherBuildingPlacementTiles(string? townProjectId)
    {
        var occupied = map.CampObjects
            .Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .Concat(RoadAndBridgeTiles())
            .Concat(fields.Select(field => field.Position))
            .Concat(TownProjectProtectedSites(townProjectId))
            .Concat((worldSimulation.BuildingExpansions ?? []).Where(job => job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused).SelectMany(ExpansionTiles))
            .ToHashSet();
        var market = MarketSiteTiles().ToHashSet();
        var buildingDefinitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        foreach (var placed in worldSimulation.Buildings)
        {
            if (!buildingDefinitions.TryGetValue(placed.DefinitionId, out var existingDefinition))
            {
                return new(occupied, market, $"Placed building '{placed.InstanceId}' references an unavailable definition.");
            }

            foreach (var existingPoint in WorldContentSimulationRules.Footprint(existingDefinition, placed))
            {
                occupied.Add(existingPoint);
            }
        }

        occupied.UnionWith(worldSimulation.Buildings.Where(building => Port(building.InstanceId) is not null)
            .Select(PortGeometryFor).SelectMany(geometry => geometry.DockingTiles));
        return new(occupied, market);
    }

    private bool CanPlaceBuilding(
        BuildingDefinition definition,
        GridPoint position,
        out string failure,
        string? townProjectId = null,
        BuildingPlacementWorld? world = null)
    {
        var footprint = WorldContentSimulationRules.Footprint(definition, position).ToArray();
        if (!PortNavigationRules.IsPort(definition) && footprint.Any(point => !map.IsBuildable(point)))
        {
            failure = "Every building footprint tile must be on buildable ground; mountains and peaks cannot hold buildings.";
            return false;
        }
        if (world is not null && world.ProjectId != townProjectId)
            throw new InvalidOperationException("A building placement check was given tiles gathered for another project.");
        world ??= CreateBuildingPlacementWorld(townProjectId);
        var tiles = world.Tiles.Value;
        if (tiles.Failure is { } contentFailure)
        {
            failure = contentFailure;
            return false;
        }
        if (PortNavigationRules.IsPort(definition))
        {
            var occupied = tiles.Occupied.Concat(tiles.Market).ToHashSet();
            var fits = PortNavigationRules.Fits(map, definition, position, occupied, out var portFailure, roadTiles);
            failure = portFailure ?? string.Empty;
            return fits;
        }
        if (footprint.Any(point => tiles.Occupied.Contains(point) || tiles.Market.Contains(point) &&
                (point != position || !definition.Tags.Contains(MarketContent.StallTag, StringComparer.Ordinal))))
        {
            failure = "The building footprint overlaps an existing object, resource, Road, bridge end, or building.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private void ApplyInventoryTransition(Func<InventoryCheckpoint, InventoryCheckpoint> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        society.Apply(checkpoint =>
        {
            var equipmentBefore = inhabitants.Values.ToDictionary(person => person.InhabitantId,
                person => person.Equipment, StringComparer.Ordinal);
            var updated = InventoryStorageHistory.RecordTransition(checkpoint.Inventory, transition(checkpoint.Inventory));
            foreach (var building in worldSimulation.Buildings)
            {
                var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
                if (BuildingStorageRules.Capacity(definition, building) is not { } capacity) continue;
                var before = checkpoint.Inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId).Sum(lot => lot.Quantity) +
                    ReservedBusinessStorageSpace(building.InstanceId, checkpoint.Inventory);
                var after = updated.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId).Sum(lot => lot.Quantity) +
                    ReservedBusinessStorageSpace(building.InstanceId, updated);
                if (after > capacity && after > before)
                    throw new InvalidOperationException("The building's storage is full; carry the remaining stock or expand it first.");
            }
            foreach (var person in inhabitants.Values)
            {
                var before = PersonalEquipmentRules.CarriedQuantity(checkpoint.Inventory, person.InhabitantId, equipmentBefore[person.InhabitantId]) +
                    ReservedBusinessCarrySpace(person.InhabitantId, checkpoint.Inventory);
                var after = PersonalEquipmentRules.CarriedQuantity(updated, person.InhabitantId, person.Equipment) +
                    ReservedBusinessCarrySpace(person.InhabitantId, updated);
                if (after > before && after > PersonalEquipmentRules.Capacity(updated, person.InhabitantId, person.Equipment) + HorseCargoCapacity(person.InhabitantId))
                    throw new InvalidOperationException("The person is carrying as much as they can; store or set down a load first.");
            }
            return new SocietyOperationResult(checkpoint with { Inventory = updated }, null, []);
        });
    }

    private InventoryCheckpoint ConsumeQuantities(
        InventoryCheckpoint inventory,
        IReadOnlyList<ContentQuantity> quantities,
        string purpose,
        string ownerId)
    {
        var current = inventory;
        for (var quantityIndex = 0; quantityIndex < quantities.Count; quantityIndex++)
        {
            var requested = quantities[quantityIndex];
            var remaining = requested.Amount;
            var request = new GoodsRequest(GoodsUse.Holdings, null,
                GoodsOwners.One(ownerId), GoodsKinds.One(requested.ResourceId));
            var matches = ProductionGoods(current, request).ToArray();
            foreach (var match in matches)
            {
                if (remaining == 0)
                {
                    break;
                }

                var lot = match.Lot;
                var available = RecheckGoods(current, request, lot.Id)?.Quantity ?? 0;
                if (available <= 0)
                {
                    continue;
                }

                var amount = Math.Min(remaining, available);
                var reservationId = $"{purpose}:quantity:{quantityIndex}:lot:{lot.Id}";
                current = InventoryFixture.Reserve(
                    current,
                    reservationId,
                    ownerId,
                    lot.Id,
                    amount,
                    purpose,
                    current.WorldTick);
                current = InventoryFixture.ConsumeReservation(current, reservationId);
                remaining -= amount;
            }

            if (remaining > 0)
            {
                throw new InvalidOperationException(
                    $"Insufficient '{requested.ResourceId}' for {purpose}; missing {remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
            }
        }

        return current;
    }

    private const string MissingHouseholdIngredientsPrefix = "The household building needs ";

    private static bool IsIngredientBlocker(string? blocker) =>
        blocker == "Waiting for ingredients at this household building" ||
        blocker?.StartsWith(MissingHouseholdIngredientsPrefix, StringComparison.Ordinal) == true;

    private string MissingProductionIngredients(RecipeDefinition recipe, string owner, string buildingId)
    {
        var missing = recipe.Inputs.First(input => !HasIngredientsAtBuilding([input], owner, buildingId));
        var available = ProductionGoods(society.Checkpoint.Inventory,
            ProductionGoodsRequest(owner, missing.ResourceId, buildingId)).Sum(match => (long)match.Quantity);
        return $"{MissingHouseholdIngredientsPrefix}{missing.Amount - available} {missing.ResourceId.Replace('_', ' ')} in its on-site stock. Bring it here before starting work.";
    }

    private bool HasIngredientsAtBuilding(IReadOnlyList<ContentQuantity> inputs,
        string ownerId, string buildingId) => inputs.All(input =>
        ProductionGoods(society.Checkpoint.Inventory, ProductionGoodsRequest(ownerId, input.ResourceId, buildingId))
            .Sum(match => (long)match.Quantity) >= input.Amount);

    private static GoodsRequest ProductionGoodsRequest(string owner, string kind, string? building = null) =>
        new(GoodsUse.ConsumeAt, null, GoodsOwners.One(owner), GoodsKinds.One(kind), AtBuilding: building);

    private IEnumerable<GoodsMatch> ProductionGoods(InventoryCheckpoint inventory, GoodsRequest request,
        bool requireCarried = false) => FindGoods(inventory, request).Matches.Where(match =>
        !IsHandcartCargo(inventory, match.Lot) && !OnBorrowedMarketStall(match.Lot) &&
        (!requireCarried || ToolProgressionRules.IsTopLevelCarriedLot(match.Lot, match.Lot.OwnerId)));

    private InventoryCheckpoint ReserveQuantities(
        InventoryCheckpoint inventory,
        IReadOnlyList<ContentQuantity> quantities,
        string purpose,
        long expiryTick,
        string ownerId,
        out IReadOnlyList<string> reservationIds,
        string? requiredStorageBuildingId = null,
        bool requireCarried = false)
    {
        var current = inventory;
        var created = new List<string>();
        for (var quantityIndex = 0; quantityIndex < quantities.Count; quantityIndex++)
        {
            var requested = quantities[quantityIndex];
            var remaining = requested.Amount;
            var request = ProductionGoodsRequest(ownerId, requested.ResourceId, requiredStorageBuildingId);
            var matches = ProductionGoods(current, request, requireCarried).ToArray();
            foreach (var match in matches)
            {
                if (remaining == 0)
                {
                    break;
                }

                var lot = match.Lot;
                var available = RecheckGoods(current, request, lot.Id)?.Quantity ?? 0;
                if (available <= 0)
                {
                    continue;
                }

                var amount = Math.Min(remaining, available);
                var reservationId = $"{purpose}:quantity:{quantityIndex}:lot:{lot.Id}";
                current = InventoryFixture.Reserve(
                    current,
                    reservationId,
                    ownerId,
                    lot.Id,
                    amount,
                    purpose,
                    expiryTick);
                created.Add(reservationId);
                remaining -= amount;
            }

            if (remaining > 0)
            {
                throw new InvalidOperationException(
                    $"Insufficient '{requested.ResourceId}' for production; missing {remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
            }
        }

        reservationIds = created.ToArray();
        return current;
    }

    private void ProcessProduction(long targetTick)
    {
        var due = worldSimulation.ProductionJobs
            .Where(job => job.State == WorldProductionJobState.Running && job.CompletionTick <= targetTick)
            .OrderBy(job => job.CompletionTick)
            .ThenBy(job => job.JobId, StringComparer.Ordinal)
            .ToArray();
        foreach (var job in due)
        {
            var recipe = worldContent.Recipes.SingleOrDefault(item => item.CanonicalId == job.RecipeId);
            if (recipe is null)
            {
                throw new InvalidDataException($"Production job '{job.JobId}' references a recipe that is no longer active.");
            }

            var completed = CompleteProductionJob(job, recipe, targetTick);

            worldSimulation = new WorldContentSimulationState(
                worldSimulation.Buildings,
                worldSimulation.ProductionJobs
                    .Select(candidate => candidate.JobId == job.JobId
                        ? candidate with { State = completed ? WorldProductionJobState.Completed : WorldProductionJobState.Cancelled }
                        : candidate)
                    .OrderBy(candidate => candidate.JobId, StringComparer.Ordinal)
                    .ToArray(),
                worldSimulation.NextProductionJobSequence,
                worldSimulation.CropBuilds, worldSimulation.BuildingExpansions, worldSimulation.GuestInvitations,
                worldSimulation.ConstructionReceipts);
            if (completed && HouseToolsContent.IsCrudeToolRecipe(recipe))
                AppendEvent("house_tool_made", $"{job.WorkerId}|{recipe.Outputs.Single().ResourceId}",
                    worldSimulation.Buildings.FirstOrDefault(building => building.InstanceId == job.BuildingInstanceId)?.Position);
            else
                AppendEvent(completed ? "recipe_completed" : "recipe_cancelled", $"{job.JobId}:{recipe.CanonicalId}");
            if (completed)
            {
                LearnProducedRecipe(job, recipe, targetTick);
                CreditProductionOrderJob(job, recipe);
            }
        }
    }

    private bool CompleteProductionJob(
        WorldProductionJob job,
        RecipeDefinition recipe,
        long targetTick)
    {
        var inventoryState = society.Checkpoint.Inventory;
        var inputs = job.InputReservationIds.Select(inventoryState.GetReservation).ToArray();
        var knifePlan = job.ToolLotId is null ? null : ToolProgressionRules.PlanWorkForLot(inventoryState,
            job.WorkerId, ToolFamily.Knife, job.ToolLotId);
        var inputUnusable = inputs.Any(reservation =>
            reservation.State is not (InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed) ||
            reservation.ExpiryTick < targetTick || inventoryState.Lots.FirstOrDefault(lot => lot.Id == reservation.LotId) is not { FreshnessBasisPoints: > 0, ConditionBasisPoints: > 0 });
        var unsupportedOutput = HasUnsupportedWaterOutput(recipe);
        if (unsupportedOutput || inputUnusable || job.ToolLotId is not null && knifePlan is null)
        {
            ApplyInventoryTransition(inventory =>
            {
                foreach (var reservation in inputs.Where(reservation => reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed))
                {
                    inventory = InventoryFixture.ReleaseReservation(inventory, reservation.Id,
                        unsupportedOutput ? "production_output_unsupported" : "production_input_unusable");
                }
                return inventory;
            });
            AppendEvent(unsupportedOutput ? "production_output_unsupported" :
                inputUnusable ? "production_input_unusable" : "production_tool_unusable", job.JobId);
            return false;
        }
        var productionBuilding = worldSimulation.Buildings
            .FirstOrDefault(building => building.InstanceId == job.BuildingInstanceId);
        var productionOwner = job.OwnerId ?? throw new InvalidDataException("A production job has no recorded owner.");
        ApplyInventoryTransition(inventory =>
        {
            var current = inventory;
            foreach (var reservationId in job.InputReservationIds.Order(StringComparer.Ordinal))
            {
                current = InventoryFixture.ConsumeReservation(current, reservationId);
            }

            for (var outputIndex = 0; outputIndex < recipe.Outputs.Count; outputIndex++)
            {
                var output = recipe.Outputs[outputIndex];
                var outputLotId = $"{job.JobId}:output:{outputIndex.ToString("D2", CultureInfo.InvariantCulture)}";
                var vessel = InventoryContainerRules.IsContainer(output.ResourceId);
                for (var unitIndex = 0; unitIndex < (vessel ? output.Amount : 1); unitIndex++)
                {
                    current = InventoryFixture.AddLot(
                        current,
                        unitIndex == 0 ? outputLotId : $"{outputLotId}:unit:{unitIndex.ToString("D2", CultureInfo.InvariantCulture)}",
                        output.ResourceId,
                        output.ResourceId == InventoryContainerRules.Handcart ? job.WorkerId : productionOwner,
                        vessel ? 1 : output.Amount,
                        targetTick,
                        storageBuildingId: output.ResourceId != InventoryContainerRules.Handcart && productionBuilding?.HouseholdId is not null
                            ? productionBuilding.InstanceId : null,
                        groundPosition: output.ResourceId == InventoryContainerRules.Handcart
                            ? new InventoryGroundPosition(productionBuilding!.Position.X, productionBuilding.Position.Y) : null);
                }
            }

            return knifePlan is null ? current : ApplyToolWorkToInventory(current, job.WorkerId,
                targetTick, [knifePlan]);
        });
        CreditCompletedWork(job.WorkerId, "crafting", SkillForRecipe(recipe));
        return true;
    }

}
