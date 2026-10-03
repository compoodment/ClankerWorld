namespace ClankerWorld.Simulation.Playtest;

public sealed record BuildingManagementResult(
    bool Applied,
    string InstanceId,
    string? Failure,
    string? TownId = null,
    string? HouseholdId = null)
{
    public static BuildingManagementResult Rejected(string? instanceId, string failure) =>
        new(false, instanceId?.Trim() ?? string.Empty, failure);
}

public sealed partial class PrivateWorldRuntime
{
    public BuildingManagementResult RemoveBuilding(
        string instanceId,
        string? expectedTownId,
        string? expectedHouseholdId,
        string? expectedWorldId = null)
    {
        gate.Wait();
        try
        {
            if (expectedWorldId is not null && expectedWorldId != society.Checkpoint.WorldId)
                return BuildingManagementResult.Rejected(instanceId, "The active world changed. Select this building again before changing it.");
            var building = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == instanceId);
            if (building is null) return BuildingManagementResult.Rejected(instanceId, "That building is no longer placed.");
            if (building.TownId != expectedTownId || building.HouseholdId != expectedHouseholdId)
                return BuildingManagementResult.Rejected(instanceId, "The building's owner changed. Refresh the world before removing it.");
            if (BuildingMutationBlocker(building) is { } blocker)
                return BuildingManagementResult.Rejected(instanceId, blocker);

            var definitionId = building.DefinitionId;
            var townId = building.TownId;
            if (townId is not null) RemoveTownBuildingAssignment(townId, instanceId);

            worldSimulation = worldSimulation with
            {
                Buildings = worldSimulation.Buildings.Where(item => item.InstanceId != instanceId).ToArray(),
                BuildingExpansions = PreserveExpansionDefinitionIdentity(instanceId, definitionId),
                GuestInvitations = RemoveHouseInvitations(instanceId),
            };
            if (survivalState is { } survival && survival.Fires.Any(item => item.BuildingId == instanceId))
            {
                survivalState = survival with { Fires = survival.Fires.Where(item => item.BuildingId != instanceId).ToArray() };
                AppendEvent("fire_extinguished", instanceId);
            }

            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("building_removed", $"{instanceId}:{definitionId}");
            return new BuildingManagementResult(true, instanceId, null, townId, building.HouseholdId);
        }
        finally { gate.Release(); }
    }

    public BuildingManagementResult ReassignBuilding(
        string instanceId,
        string? expectedTownId,
        string? expectedHouseholdId,
        string? targetTownId,
        string? targetHouseholdId,
        string? expectedWorldId = null)
    {
        gate.Wait();
        try
        {
            if (expectedWorldId is not null && expectedWorldId != society.Checkpoint.WorldId)
                return BuildingManagementResult.Rejected(instanceId, "The active world changed. Select this building again before changing it.");
            var building = worldSimulation.Buildings.SingleOrDefault(item => item.InstanceId == instanceId);
            if (building is null) return BuildingManagementResult.Rejected(instanceId, "That building is no longer placed.");
            if (building.TownId != expectedTownId || building.HouseholdId != expectedHouseholdId)
                return BuildingManagementResult.Rejected(instanceId, "The building's owner changed. Refresh the world before reassigning it.");
            if (BuildingMutationBlocker(building) is { } blocker)
                return BuildingManagementResult.Rejected(instanceId, blocker);

            var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
            var isWarehouse = definition.Tags.Contains("warehouse", StringComparer.Ordinal);
            string? nextTownId;
            string? nextHouseholdId;
            if (isWarehouse)
            {
                if (targetTownId is null || targetHouseholdId is not null || !towns.Any(item => item.Id == targetTownId))
                    return BuildingManagementResult.Rejected(instanceId, "Choose a recorded Town for this Warehouse.");
                if (worldSimulation.Buildings.Any(item => item.InstanceId != instanceId && item.TownId == targetTownId &&
                    worldContent.Buildings.Any(candidate => candidate.CanonicalId == item.DefinitionId &&
                        candidate.Tags.Contains("warehouse", StringComparer.Ordinal))))
                    return BuildingManagementResult.Rejected(instanceId, "That Town already has a Warehouse.");
                nextTownId = targetTownId;
                nextHouseholdId = null;
            }
            else
            {
                if (targetTownId is not null)
                    return BuildingManagementResult.Rejected(instanceId, "Only a Warehouse can be reassigned to another Town.");
                var acceptsHouseholdOwner = definition.Tags.Any(IsHouseholdBuildingTag);
                var isHouse = definition.Tags.Contains("house", StringComparer.Ordinal);
                if (targetHouseholdId is not null && (!acceptsHouseholdOwner ||
                    !society.Checkpoint.Households.Any(item => item.Id == targetHouseholdId)))
                    return BuildingManagementResult.Rejected(instanceId, "Choose an existing household that can own this building.");
                if (isHouse && targetHouseholdId is null)
                    return BuildingManagementResult.Rejected(instanceId, "A House must remain assigned to a household.");
                nextTownId = building.TownId;
                nextHouseholdId = targetHouseholdId;
            }

            if (building.TownId == nextTownId && building.HouseholdId == nextHouseholdId)
                return BuildingManagementResult.Rejected(instanceId, "That building already has this owner.");
            if (nextHouseholdId is { } householdId && HouseholdBuildingKinds.KindOf(definition) is { } kind &&
                worldSimulation.Buildings.Any(item => item.InstanceId != instanceId && item.HouseholdId == householdId &&
                    HouseholdBuildingKinds.KindOf(worldContent.Buildings.Single(candidate => candidate.CanonicalId == item.DefinitionId)) == kind))
            {
                var verb = kind == "house" ? "has" : "holds";
                return BuildingManagementResult.Rejected(instanceId,
                    $"That household already {verb} a {definition.DisplayName}.");
            }

            var footprint = WorldContentSimulationRules.Footprint(definition, building).ToHashSet();
            var affectedRights = householdLandUseRights.Where(right => right.Tiles.Any(footprint.Contains)).ToArray();
            if (!isWarehouse && nextHouseholdId != building.HouseholdId && affectedRights.Length > 0)
            {
                if (footprint.Any(tile => TownLandRightsRules.IsDisputed(tile, householdLandUseRights, householdLandUseRequests)))
                    return BuildingManagementResult.Rejected(instanceId, "Resolve the land dispute before reassigning this building.");
                if (affectedRights.Any(right => right.AgreedEndTick is { } end && end <= WorldTick))
                    return BuildingManagementResult.Rejected(instanceId, "Resolve the expired land-use right before reassigning this building.");
                if (nextHouseholdId is null)
                    return BuildingManagementResult.Rejected(instanceId, "Choose a household to receive this building and its land-use right.");
                if (affectedRights.Any(right => right.HouseholdId != building.HouseholdId || right.TownId != building.TownId))
                    return BuildingManagementResult.Rejected(instanceId, "This building's footprint includes another owner's land-use right.");
            }
            var reassignedRights = !isWarehouse && nextHouseholdId is not null && affectedRights.Length > 0
                ? TownLandRightsRules.ReassignFootprintRights(map, householdLandUseRights, footprint, nextHouseholdId, WorldTick)
                : householdLandUseRights;

            if (isWarehouse && nextTownId is { } reassignedTownId)
            {
                if (building.TownId is { } previousTownId)
                    RemoveTownBuildingAssignment(previousTownId, instanceId);
                AddTownBuildingAssignment(reassignedTownId, instanceId);
            }
            worldSimulation = worldSimulation with
            {
                Buildings = worldSimulation.Buildings.Select(item => item.InstanceId == instanceId
                    ? item with { TownId = nextTownId, HouseholdId = nextHouseholdId }
                    : item).ToArray(),
                GuestInvitations = nextHouseholdId != building.HouseholdId
                    ? RemoveHouseInvitations(instanceId)
                    : worldSimulation.GuestInvitations,
            };
            householdLandUseRights = reassignedRights.ToList();
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("building_reassigned", $"{instanceId}:town={nextTownId ?? "none"}:household={nextHouseholdId ?? "none"}");
            return new BuildingManagementResult(true, instanceId, null, nextTownId, nextHouseholdId);
        }
        finally { gate.Release(); }
    }

    private string? BuildingMutationBlocker(PlacedBuilding building)
    {
        var id = building.InstanceId;
        if (toolMakingRequests.Any(request => request.BuildingInstanceId == id && !ToolMakingRequestRules.IsTerminal(request.Status)))
            return "Finish, refuse or withdraw the active tool request before changing this Blacksmith's owner or removing it.";
        if (society.Checkpoint.Inventory.Lots.Any(lot =>
                lot.StorageBuildingId == id || lot.DeliveryBuildingId == id))
            return "Empty this building and wait for all deliveries before changing its owner or removing it.";
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        if (building.HouseholdId is { } householdId && definition.Tags.Contains("farmhouse", StringComparer.Ordinal) &&
            fields.Any(field => field.HouseholdId == householdId && field.Work is not null) &&
            !worldSimulation.Buildings.Any(other => other.InstanceId != id && other.HouseholdId == householdId &&
                worldContent.Buildings.Any(candidate => candidate.CanonicalId == other.DefinitionId &&
                    candidate.Tags.Contains("farmhouse", StringComparer.Ordinal))))
            return "Let active field work finish before removing or reassigning this household's last Farmhouse.";
        // Work paused by a departure keeps its inputs and site, so it blocks changes like running work.
        static bool Active(WorldProductionJobState state) =>
            state is WorldProductionJobState.Running or WorldProductionJobState.Paused;
        if (worldSimulation.ProductionJobs.Any(job => job.BuildingInstanceId == id && Active(job.State)) ||
            (worldSimulation.CropBuilds ?? []).Any(job => job.BuildingInstanceId == id && Active(job.State)) ||
            (worldSimulation.BuildingExpansions ?? []).Any(job => job.BuildingInstanceId == id && Active(job.State)))
            return "Wait for the active work at this building to finish before changing its owner or removing it.";
        if (inhabitants.Values.Any(person => person.Equipment?.Repair?.BuildingId == id))
            return "Finish or cancel the active equipment repair before changing this building's owner or removing it.";
        return null;
    }

    private BuildingExpansionJob[] PreserveExpansionDefinitionIdentity(string buildingId, string definitionId) =>
        (worldSimulation.BuildingExpansions ?? []).Select(job =>
            job.BuildingInstanceId == buildingId && job.DefinitionId is null
                ? job with { DefinitionId = definitionId }
                : job).ToArray();

    private HouseGuestInvitation[] RemoveHouseInvitations(string buildingId) =>
        (worldSimulation.GuestInvitations ?? []).Where(item => item.HouseInstanceId != buildingId).ToArray();

    private void RemoveTownBuildingAssignment(string townId, string buildingId)
    {
        var town = towns.Single(item => item.Id == townId);
        SetTown(town with
        {
            AssignedBuildingIds = town.AssignedBuildingIds.Where(item => item != buildingId).ToArray(),
        });
    }

    private void AddTownBuildingAssignment(string townId, string buildingId)
    {
        var town = towns.Single(item => item.Id == townId);
        SetTown(town with
        {
            AssignedBuildingIds = town.AssignedBuildingIds.Append(buildingId).Order(StringComparer.Ordinal).ToArray(),
        });
    }
}
