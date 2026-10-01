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
    private TownLayoutContext CreateTownLayoutContext(string actor, GridPoint? selectedSite = null,
        BuildingDefinition? building = null)
    {
        var origin = inhabitants[actor].Position;
        var town = towns.SingleOrDefault(item => item.ResidentIds.Contains(actor, StringComparer.Ordinal));
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var occupied = map.CampObjects.Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .Concat(RoadAndBridgeTiles())
            .Concat(MarketReservedTiles())
            .Concat(LooseStockTiles())
            .Concat(fields.Select(field => field.Position))
            .Concat((worldSimulation.BuildingExpansions ?? []).Where(job => job.State == WorldProductionJobState.Running).SelectMany(ExpansionTiles))
            .Concat(Carts.Select(cart => cart.Position))
            .Concat(worldSimulation.Buildings.Where(placed => PortNavigationRules.IsPort(definitions[placed.DefinitionId]))
                .SelectMany(placed => PortGeometryFor(placed).DockingTiles))
            .Concat(worldSimulation.Buildings.SelectMany(building =>
            {
                if (!definitions.TryGetValue(building.DefinitionId, out var definition))
                    throw new InvalidDataException("A placed building has no active definition.");
                return WorldContentSimulationRules.Footprint(definition, building);
            }))
            .Concat(inhabitants.Values.Where(person => person.InhabitantId != actor)
                .Select(person => person.Position))
            .ToHashSet();
        var resourcesForLayout = map.Resources.Select(resource => new TownLayoutResource(
            resource,
            resources.GetValueOrDefault(resource.Id) == ResourceState.Available));
        var buildingsForLayout = worldSimulation.Buildings
            .Where(building => definitions.ContainsKey(building.DefinitionId))
            .Select(building => new TownLayoutBuilding(building, BuildingStorageRules.EffectiveDefinition(definitions[building.DefinitionId], building)));
        return new TownLayoutContext(
            map,
            town,
            occupied,
            FindUnoccupiedFootCosts(actor, origin, town, occupied,
                selectedSite is { } selected && building is not null ? BuildingWorkPosition(building, selected) : selectedSite),
            resourcesForLayout,
            buildingsForLayout,
            roadTiles: roadTiles,
            requiredNeighborTiles: building is not null && HouseholdBuildingKind(building) == "silo" ? SiloNeighborTiles(actor, definitions) : null);
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
            anchors = [chosenSite];
        var pendingAnchors = anchors?.Where(point => map.IsBuildable(point) && !occupiedSites.Contains(point))
            .ToHashSet();
        var open = new PriorityQueue<GridPoint, (int Cost, int Y, int X, int Order)>();
        var best = new Dictionary<GridPoint, int> { [origin] = 0 };
        var settled = new Dictionary<GridPoint, int>();
        var viableLegacyAnchors = 0;
        var selectedSiteReached = selectedSite is null ||
            pendingAnchors is not null && pendingAnchors.Count == 0;
        var order = 0;
        open.Enqueue(origin, (0, origin.Y, origin.X, order++));
        while (open.TryDequeue(out var current, out var priority))
        {
            if (priority.Cost != best[current])
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

                var cost = checked(priority.Cost + RoadStepCost(current, next));
                if (best.TryGetValue(next, out var previous) && previous <= cost)
                    continue;
                best[next] = cost;
                open.Enqueue(next, (cost, next.Y, next.X, order++));
            }
        }

        return settled;
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

        foreach (var placed in worldSimulation.Buildings.OrderBy(item => item.InstanceId, StringComparer.Ordinal))
        {
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
                item.BuildingInstanceId == placed.InstanceId && item.State == WorldProductionJobState.Running);
            var workPosition = BuildingWorkPosition(placed);
            if (activeJobs < definition.Capacity &&
                (actorId is null || FindUnoccupiedRoute(actorId, inhabitants[actorId].Position, workPosition, 0).Count > 0))
            {
                siteId = placed.InstanceId;
                position = workPosition;
                return true;
            }
        }

        siteId = string.Empty;
        position = default;
        return false;
    }

    private bool HasAvailableQuantities(IReadOnlyList<ContentQuantity> quantities, string? ownerId = null)
    {
        var inventory = society.Checkpoint.Inventory;
        foreach (var requested in quantities)
        {
            var available = inventory.Lots
                .Where(lot => lot.OwnerId == (ownerId ?? HouseholdId) && lot.ItemKind == requested.ResourceId && lot.GroundPosition is null)
                .Sum(AvailableLotQuantity);
            if (available < requested.Amount)
            {
                return false;
            }
        }

        return true;
    }

    private string BuildingConstructionOwner(string actor, BuildingDefinition definition)
    {
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (household is null) return actor;
        if (definition.Tags.Contains("market", StringComparer.Ordinal) || definition.Tags.Contains("town_hall", StringComparer.Ordinal)) return household;
        if (!definition.Tags.Any(IsHouseholdBuildingTag) && !PortNavigationRules.IsPort(definition)) return HouseholdId;
        // Existing household supplies remain usable; new supplies stay personally
        // carried until a household has a physical House to receive them.
        return HouseForHousehold(household) is not null || HasAvailableQuantities(definition.BuildCosts, household)
            ? household : actor;
    }

    private static string BuildInstanceId(string inhabitantId, BuildingDefinition definition)
    {
        // Preserve valid legacy IDs; descendant identities contain separators
        // that are legal society IDs but invalid content instance IDs.
        if (inhabitantId.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-' or '_'))
            return $"build-{inhabitantId}-{definition.PackageDigest[7..15]}-{definition.LocalId}";
        return "build-v2-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inhabitantId + "\n" + definition.CanonicalId)));
    }

    private string ConstructionInstanceId(string actor, BuildingDefinition definition, GridPoint position) =>
        PortNavigationRules.IsPort(definition)
            ? $"port-{TownForResident(actor)}-{definition.LocalId}-{position.X}-{position.Y}"
            : BuildInstanceId(actor, definition);

    private bool CanPlaceBuilding(
        BuildingDefinition definition,
        GridPoint position,
        out string failure)
    {
        var footprint = WorldContentSimulationRules.Footprint(definition, position).ToArray();
        if (!PortNavigationRules.IsPort(definition) && footprint.Any(point => !map.IsBuildable(point)))
        {
            failure = "Every building footprint tile must be on buildable ground; mountains and peaks cannot hold buildings.";
            return false;
        }

        var occupied = map.CampObjects
            .Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .Concat(RoadAndBridgeTiles())
            .Concat(fields.Select(field => field.Position))
            .Concat((worldSimulation.BuildingExpansions ?? []).Where(job => job.State == WorldProductionJobState.Running).SelectMany(ExpansionTiles))
            .Concat(Carts.Select(cart => cart.Position))
            .ToHashSet();
        var buildingDefinitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        foreach (var placed in worldSimulation.Buildings)
        {
            if (!buildingDefinitions.TryGetValue(placed.DefinitionId, out var existingDefinition))
            {
                failure = $"Placed building '{placed.InstanceId}' references an unavailable definition.";
                return false;
            }

            foreach (var existingPoint in WorldContentSimulationRules.Footprint(existingDefinition, placed))
            {
                occupied.Add(existingPoint);
            }
            if (PortNavigationRules.IsPort(existingDefinition))
                foreach (var dock in PortNavigationRules.Geometry(map, existingDefinition, placed.Position).DockingTiles)
                    occupied.Add(dock);
        }

        if (PortNavigationRules.IsPort(definition))
        {
            occupied.UnionWith(Bridges.SelectMany(bridge => bridge.Span));
            occupied.UnionWith(boatTransport.Boats.Select(boat => boat.Position));
            var valid = PortNavigationRules.Fits(map, definition, position, occupied, out var portFailure, roadTiles);
            failure = portFailure ?? string.Empty;
            return valid;
        }

        if (footprint.Any(occupied.Contains))
        {
            failure = "The building footprint overlaps an existing object, resource, Road, bridge end, or building.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private void ApplyInventoryTransition(Func<InventoryCheckpoint, InventoryCheckpoint> transition,
        string? committedBusinessOfferId = null)
    {
        ArgumentNullException.ThrowIfNull(transition);
        society.Apply(checkpoint =>
        {
            var updated = transition(checkpoint.Inventory);
            ValidateCarryingTransition(checkpoint.Inventory, updated, committedBusinessOfferId);
            foreach (var building in worldSimulation.Buildings)
            {
                var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
                if (BuildingStorageRules.Capacity(definition, building) is not { } capacity) continue;
                var before = checkpoint.Inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId).Sum(lot => (long)lot.Quantity);
                var after = updated.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId).Sum(lot => (long)lot.Quantity);
                if (after > capacity - ReservedBusinessStorageSpace(building.InstanceId, committedBusinessOfferId) && after > before)
                    throw new InvalidOperationException("The building's storage is full; carry the remaining stock or expand it first.");
            }
            return new SocietyOperationResult(checkpoint with { Inventory = updated }, null, []);
        });
        SynchronizeBusinessListingStock();
    }

    private static InventoryCheckpoint ConsumeQuantities(
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
            var lots = current.Lots
                .Where(lot => lot.OwnerId == ownerId && lot.ItemKind == requested.ResourceId && lot.GroundPosition is null &&
                    lot.FreshnessBasisPoints > 0 && lot.ConditionBasisPoints > 0)
                .OrderBy(lot => lot.Id, StringComparer.Ordinal)
                .ToArray();
            foreach (var lot in lots)
            {
                if (remaining == 0)
                {
                    break;
                }

                var reserved = current.Reservations
                    .Where(reservation => reservation.LotId == lot.Id &&
                        reservation.State is InventoryReservationState.Reserved or
                            InventoryReservationState.PartiallyConsumed or
                            InventoryReservationState.Committed)
                    .Sum(reservation => reservation.Quantity);
                var available = lot.Quantity - reserved;
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

    private bool HasIngredientsAtBuilding(IReadOnlyList<ContentQuantity> inputs,
        string ownerId, string buildingId) => inputs.All(input =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == ownerId &&
                lot.StorageBuildingId == buildingId && lot.ItemKind == input.ResourceId &&
                lot.FreshnessBasisPoints > 0 && lot.ConditionBasisPoints > 0)
            .Sum(lot => (long)AvailableLotQuantity(lot)) >= input.Amount);

    private static InventoryCheckpoint ReserveQuantities(
        InventoryCheckpoint inventory,
        IReadOnlyList<ContentQuantity> quantities,
        string purpose,
        long expiryTick,
        string ownerId,
        out IReadOnlyList<string> reservationIds,
        string? requiredStorageBuildingId = null)
    {
        var current = inventory;
        var created = new List<string>();
        for (var quantityIndex = 0; quantityIndex < quantities.Count; quantityIndex++)
        {
            var requested = quantities[quantityIndex];
            var remaining = requested.Amount;
            var lots = current.Lots
                .Where(lot => lot.OwnerId == ownerId && lot.ItemKind == requested.ResourceId && lot.GroundPosition is null &&
                    lot.FreshnessBasisPoints > 0 && lot.ConditionBasisPoints > 0 &&
                    (requiredStorageBuildingId is null || lot.StorageBuildingId == requiredStorageBuildingId))
                .OrderBy(lot => lot.Id, StringComparer.Ordinal)
                .ToArray();
            foreach (var lot in lots)
            {
                if (remaining == 0)
                {
                    break;
                }

                var reserved = current.Reservations
                    .Where(reservation => reservation.LotId == lot.Id &&
                        reservation.State is InventoryReservationState.Reserved or
                            InventoryReservationState.PartiallyConsumed or
                            InventoryReservationState.Committed)
                    .Sum(reservation => reservation.Quantity);
                var available = lot.Quantity - reserved;
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
                worldSimulation.CropBuilds, worldSimulation.BuildingExpansions, worldSimulation.GuestInvitations, worldSimulation.Carts);
            AppendEvent(completed ? "recipe_completed" : "recipe_cancelled", $"{job.JobId}:{recipe.CanonicalId}");
        }
    }

    private bool CompleteProductionJob(
        WorldProductionJob job,
        RecipeDefinition recipe,
        long targetTick)
    {
        var inventoryState = society.Checkpoint.Inventory;
        var inputs = job.InputReservationIds.Select(inventoryState.GetReservation).ToArray();
        if (inputs.Any(reservation => reservation.State is not (InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed) ||
            reservation.ExpiryTick < targetTick || inventoryState.Lots.FirstOrDefault(lot => lot.Id == reservation.LotId) is not { FreshnessBasisPoints: > 0, ConditionBasisPoints: > 0 }))
        {
            ApplyInventoryTransition(inventory =>
            {
                foreach (var reservation in inputs.Where(reservation => reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed))
                {
                    inventory = InventoryFixture.ReleaseReservation(inventory, reservation.Id, "production_input_unusable");
                }
                return inventory;
            });
            AppendEvent("production_input_unusable", job.JobId);
            return false;
        }
        var productionBuilding = worldSimulation.Buildings
            .FirstOrDefault(building => building.InstanceId == job.BuildingInstanceId);
        var productionOwner = ProductionOwnerFor(productionBuilding, job.WorkerId);
        if (inhabitants.ContainsKey(productionOwner))
        {
            var personalInputs = inputs.Where(input => input.OwnerId == productionOwner &&
                    inventoryState.GetLot(input.LotId).StorageBuildingId is null).Sum(input => input.Quantity);
            var produced = recipe.Outputs.Sum(output => output.Amount);
            if (Math.Max(0, produced - personalInputs) > CarryingRoom(productionOwner))
            {
                ApplyInventoryTransition(inventory =>
                {
                    foreach (var input in inputs)
                        inventory = InventoryFixture.ReleaseReservation(inventory, input.Id, "production_output_full");
                    return inventory;
                });
                AppendEvent("production_output_full", job.WorkerId);
                return false;
            }
        }
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
                current = InventoryFixture.AddLot(
                    current,
                    $"{job.JobId}:output:{outputIndex.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}",
                    output.ResourceId,
                    productionOwner,
                    output.Amount,
                    targetTick,
                    storageBuildingId: productionBuilding?.HouseholdId is not null || recipe.Tags.Contains("boat", StringComparer.Ordinal) ? productionBuilding!.InstanceId
                        : null,
                    containerCapacity: VesselRules.Capacity(output.ResourceId));
            }

            return current;
        });
        CreditCompletedWork(job.WorkerId, "crafting", SkillForRecipe(recipe));
        return true;
    }

}
