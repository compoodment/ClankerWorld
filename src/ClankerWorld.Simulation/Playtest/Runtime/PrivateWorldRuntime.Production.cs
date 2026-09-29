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
    private TownLayoutContext CreateTownLayoutContext(string actor, GridPoint? selectedSite = null)
    {
        var origin = inhabitants[actor].Position;
        var town = towns.SingleOrDefault(item => item.ResidentIds.Contains(actor, StringComparer.Ordinal));
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var occupied = map.CampObjects.Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .Concat(roadTiles)
            .Concat(worldSimulation.Buildings.SelectMany(building =>
            {
                if (!definitions.TryGetValue(building.DefinitionId, out var definition))
                    throw new InvalidDataException("A placed building has no active definition.");
                return WorldContentSimulationRules.Footprint(definition, building.Position);
            }))
            .Concat(inhabitants.Values.Where(person => person.InhabitantId != actor)
                .Select(person => person.Position))
            .ToHashSet();
        var resourcesForLayout = map.Resources.Select(resource => new TownLayoutResource(
            resource,
            resources.GetValueOrDefault(resource.Id) == ResourceState.Available));
        var buildingsForLayout = worldSimulation.Buildings
            .Where(building => definitions.ContainsKey(building.DefinitionId))
            .Select(building => new TownLayoutBuilding(building, definitions[building.DefinitionId]));
        return new TownLayoutContext(
            map,
            town,
            occupied,
            FindUnoccupiedFootCosts(actor, origin, town, occupied, selectedSite),
            resourcesForLayout,
            buildingsForLayout);
    }

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
        if (recipe.IsCrop)
        {
            foreach (var resource in map.Resources
                         .Where(item => item.Id == SeededMapGenerator.FertileLandResourceId &&
                             item.Kind == "fertile_land")
                         .OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                if (resources.TryGetValue(resource.Id, out var resourceState) &&
                    resourceState == ResourceState.Available &&
                    !(worldSimulation.CropBuilds ?? []).Any(job =>
                        job.State == WorldProductionJobState.Running &&
                        job.BuildingInstanceId == WorldBuildSiteRules.FertileLandSiteId(resource.Position)))
                {
                    siteId = WorldBuildSiteRules.FertileLandSiteId(resource.Position);
                    position = resource.Position;
                    return true;
                }
            }

            siteId = string.Empty;
            position = default;
            return false;
        }

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
            var activeJobs = worldSimulation.ProductionJobs.Count(item =>
                item.BuildingInstanceId == placed.InstanceId && item.State == WorldProductionJobState.Running);
            if (activeJobs < definition.Capacity)
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

    private bool HasAvailableQuantities(IReadOnlyList<ContentQuantity> quantities, string? ownerId = null)
    {
        var inventory = society.Checkpoint.Inventory;
        foreach (var requested in quantities)
        {
            var available = inventory.Lots
                .Where(lot => lot.OwnerId == (ownerId ?? HouseholdId) && lot.ItemKind == requested.ResourceId)
                .Sum(AvailableLotQuantity);
            if (available < requested.Amount)
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildInstanceId(string inhabitantId, BuildingDefinition definition)
    {
        // Preserve valid legacy IDs; descendant identities contain separators
        // that are legal society IDs but invalid content instance IDs.
        if (inhabitantId.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-' or '_'))
            return $"build-{inhabitantId}-{definition.PackageDigest[7..15]}-{definition.LocalId}";
        return "build-v2-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inhabitantId + "\n" + definition.CanonicalId)));
    }

    private bool CanPlaceBuilding(
        BuildingDefinition definition,
        GridPoint position,
        out string failure)
    {
        var footprint = WorldContentSimulationRules.Footprint(definition, position).ToArray();
        if (footprint.Any(point => !map.IsBuildable(point)))
        {
            failure = "Every building footprint tile must be on buildable ground; mountains and peaks cannot hold buildings.";
            return false;
        }

        var occupied = map.CampObjects
            .Select(item => item.Position)
            .Concat(map.Resources.Select(item => item.Position))
            .Concat(roadTiles)
            .ToHashSet();
        var buildingDefinitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        foreach (var placed in worldSimulation.Buildings)
        {
            if (!buildingDefinitions.TryGetValue(placed.DefinitionId, out var existingDefinition))
            {
                failure = $"Placed building '{placed.InstanceId}' references an unavailable definition.";
                return false;
            }

            foreach (var existingPoint in WorldContentSimulationRules.Footprint(existingDefinition, placed.Position))
            {
                occupied.Add(existingPoint);
            }
        }

        if (footprint.Any(occupied.Contains))
        {
            failure = "The building footprint overlaps an existing object, resource, Road, or building.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private void ApplyInventoryTransition(Func<InventoryCheckpoint, InventoryCheckpoint> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        society.Apply(checkpoint => new SocietyOperationResult(
            checkpoint with { Inventory = transition(checkpoint.Inventory) },
            null,
            []));
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
                .Where(lot => lot.OwnerId == ownerId && lot.ItemKind == requested.ResourceId && lot.FreshnessBasisPoints > 0 && lot.ConditionBasisPoints > 0)
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
                .Where(lot => lot.OwnerId == ownerId && lot.ItemKind == requested.ResourceId &&
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
                worldSimulation.CropBuilds);
            AppendEvent(completed ? "recipe_completed" : "recipe_cancelled", $"{job.JobId}:{recipe.CanonicalId}");
        }
    }

    private void ProcessCropBuilds(long targetTick)
    {
        var due = (worldSimulation.CropBuilds ?? [])
            .Where(job => job.State == WorldProductionJobState.Running && job.CompletionTick <= targetTick)
            .OrderBy(job => job.CompletionTick)
            .ThenBy(job => job.JobId, StringComparer.Ordinal)
            .ToArray();
        foreach (var job in due)
        {
            var recipe = worldContent.Recipes.SingleOrDefault(item => item.CanonicalId == job.RecipeId);
            if (recipe is null || !recipe.IsCrop)
            {
                throw new InvalidDataException($"Crop build '{job.JobId}' references a recipe that is no longer active.");
            }

            var completed = CompleteProductionJob(job, recipe, targetTick);
            worldSimulation = new WorldContentSimulationState(
                worldSimulation.Buildings,
                worldSimulation.ProductionJobs,
                worldSimulation.NextProductionJobSequence,
                (worldSimulation.CropBuilds ?? [])
                    .Select(candidate => candidate.JobId == job.JobId
                        ? candidate with { State = completed ? WorldProductionJobState.Completed : WorldProductionJobState.Cancelled }
                        : candidate)
                    .OrderBy(candidate => candidate.JobId, StringComparer.Ordinal)
                    .ToArray());
            AppendEvent(completed ? "build_completed" : "build_cancelled", $"{job.JobId}:{recipe.CanonicalId}");
        }
    }

    private GridPoint CropSite(WorldProductionJob job) =>
        WorldBuildSiteRules.TryGetFertileLandPosition(job.BuildingInstanceId, out var position)
            ? position
            : worldSimulation.Buildings.Single(building => building.InstanceId == job.BuildingInstanceId).Position;

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
        var cropSite = recipe.IsCrop && survivalState is not null ? CropSite(job) : default;
        var cropWeather = recipe.IsCrop && survivalState is not null ? WeatherAt(cropSite) : WeatherKind.Clear;
        var soilMoisture = recipe.IsCrop && survivalState is not null
            ? WeatherRules.SoilMoistureAt(worldSystems, cropSite, map.Height,
                WeatherRules.RegionClimate(map, cropSite))
            : 35;
        var productionBuilding = worldSimulation.Buildings
            .FirstOrDefault(building => building.InstanceId == job.BuildingInstanceId);
        var productionOwner = ProductionOwnerFor(productionBuilding, job.WorkerId);
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
                    CropOutputQuantity(recipe, output, cropWeather, soilMoisture),
                    targetTick,
                    storageBuildingId: productionBuilding?.HouseholdId is null ? null : productionBuilding.InstanceId);
            }

            return current;
        });
        if (recipe.IsCrop && survivalState is not null && cropWeather is WeatherKind.Snow or WeatherKind.Storm)
        {
            AppendEvent("crop_weather_loss", $"{job.JobId}:{cropWeather.ToString().ToLowerInvariant()}");
        }
        if (recipe.IsCrop && survivalState is not null &&
            recipe.Outputs.Any(output => output.ResourceId == "food") &&
            cropWeather is not (WeatherKind.Snow or WeatherKind.Storm) &&
            (soilMoisture < 15 || soilMoisture >= 50))
        {
            AppendEvent("crop_moisture_effect", $"{job.JobId}:{(soilMoisture < 15 ? "dry" : "wet")}:{soilMoisture}");
        }
        CreditCompletedWork(job.WorkerId, recipe.IsCrop ? "farming" : "crafting");
        return true;
    }

}
