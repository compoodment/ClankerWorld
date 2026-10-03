using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Construction authority and physical input receipts survive reload together.</summary>
public static class TownProjectValidation
{
    public static void Validate(IReadOnlyList<TownRuntimeState> towns, SocietyCheckpoint society,
        SeededMap map, WorldContentSimulationState simulation, DeclarativeWorldContentState content,
        IReadOnlyList<TownLandTitleRecord> titles, IReadOnlyList<HouseholdLandUseRight> rights,
        IReadOnlyList<HouseholdLandUseRequest> requests, IReadOnlyList<FarmFieldState> fields,
        IReadOnlyList<GridPoint> roads, IReadOnlyList<BridgeState> bridges)
    {
        var inventory = society.Inventory;
        var known = society.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var projects = new HashSet<string>(StringComparer.Ordinal);
        var deliveries = new HashSet<string>(StringComparer.Ordinal);
        var reservations = new HashSet<string>(StringComparer.Ordinal);
        var liveLots = new HashSet<string>(StringComparer.Ordinal);
        var completedBuildings = new HashSet<string>(StringComparer.Ordinal);
        var hall = TownHallContent.Hall3x4();
        foreach (var town in towns)
        {
            if (town.Projects is null) throw Invalid("A Town is missing its construction ledger.");
            foreach (var project in town.Projects)
            {
                if (project is null || !projects.Add(project.Id)) throw Invalid("Town project identities must be unique.");
                TownProjectRules.ValidatePayload(project.Plan);
                var definition = TownProjectRules.Definition(project.Plan.DefinitionId) ??
                    throw Invalid("A Town project must use supported paid content.");
                var lantern = StreetLanternContent.IsLantern(project.Plan.DefinitionId);
                var proposal = town.Governance?.Proposals.SingleOrDefault(item => item.Id == project.ProposalId);
                if (proposal is not { Kind: "project", Status: "passed", Project: not null } ||
                    project.Id != TownProjectRules.ProjectId(proposal) ||
                    !TownProjectRules.SameScope(project.Plan, proposal.Project) ||
                    project.ApprovedTick != proposal.SettledTick || project.ApprovedTick > society.WorldTick ||
                    project.LastTransitionTick < project.ApprovedTick || project.LastTransitionTick > society.WorldTick ||
                    project.Stage is not ("supplying" or "working" or "blocked" or "completed" or "cancelled") ||
                    project.WorkDone is < 0 or > TownProjectRules.WorkTicks || project.Deliveries is null ||
                    project.Blocker is { Length: > 512 } ||
                    project.Stage == "blocked" && string.IsNullOrWhiteSpace(project.Blocker) ||
                    project.Stage is "supplying" or "working" or "completed" && project.Blocker is not null)
                    throw Invalid("A Town project disagrees with its Council approval or construction stage.");
                if (content.Buildings.All(item => item.CanonicalId != project.Plan.DefinitionId) ||
                    !FootprintIsBuildable(map, project.Plan.Site, definition.Width, definition.Height) ||
                    !map.IsBuildable(project.Plan.Entrance))
                    throw Invalid("A Town project's approved footprint or doorway is invalid.");
                if (lantern && project.RemovedTick is null && project.Stage is ("supplying" or "working" or "completed") &&
                    !roads.Contains(project.Plan.Entrance))
                    throw Invalid("A live street lantern must retain its approved adjacent Road.");
                if (project.Stage is "supplying" or "working")
                    ValidateLiveSite(towns, town, project, map, simulation, content, titles, rights, requests, fields, roads, bridges);
                var liveQuantities = new Dictionary<string, long>(StringComparer.Ordinal);
                var ordinal = 0;
                foreach (var delivery in project.Deliveries)
                {
                    var cost = delivery is null ? default : project.Plan.Budget.SingleOrDefault(item => item.ResourceId == delivery.ItemKind);
                    if (delivery is null || delivery.Id != TownProjectRules.DeliveryId(project.Id, delivery.ContributorId,
                            delivery.SourceLotId, delivery.PickedUpTick, ordinal) || !deliveries.Add(delivery.Id) ||
                        !known.Contains(delivery.ContributorId) || string.IsNullOrWhiteSpace(delivery.SourceLotId) ||
                        string.IsNullOrWhiteSpace(delivery.LotId) || cost.Amount <= 0 || delivery.Quantity <= 0 ||
                        delivery.Quantity > cost.Amount || delivery.PickedUpTick < project.ApprovedTick ||
                        delivery.PickedUpTick > society.WorldTick ||
                        (delivery.DeliveredTick is null) != (delivery.ReservationId is null) ||
                        delivery.DeliveredTick is { } deliveredAt && (deliveredAt < delivery.PickedUpTick || deliveredAt > society.WorldTick) ||
                        delivery.ReleasedTick is { } releasedAt && (releasedAt < (delivery.DeliveredTick ?? delivery.PickedUpTick) || releasedAt > society.WorldTick) ||
                        (delivery.ReleasedTick is null) != (delivery.ReleaseReason is null) ||
                        delivery.ReleaseReason is { Length: > 512 } || delivery.ReleaseReason is { } reason && string.IsNullOrWhiteSpace(reason))
                        throw Invalid("A Town project's retained material delivery is invalid.");
                    ordinal++;
                    if (delivery.ReservationId is { } receiptId)
                    {
                        var receipt = inventory.Reservations.SingleOrDefault(item => item.Id == receiptId);
                        var expected = delivery.ReleasedTick is not null ? InventoryReservationState.Released :
                            project.Stage == "completed" ? InventoryReservationState.Completed : InventoryReservationState.Reserved;
                        if (receiptId != delivery.Id + ":input" || !reservations.Add(receiptId) ||
                            receipt is null || receipt.OwnerId != town.Id ||
                            receipt.LotId != delivery.LotId || receipt.Quantity != delivery.Quantity ||
                            receipt.Purpose != TownProjectRules.ReservationPurpose(project.Id) ||
                            receipt.ExpiryTick != long.MaxValue || receipt.State != expected)
                            throw Invalid("A Town project's exact material receipt is missing or changed.");
                    }
                    if (delivery.ReleasedTick is not null) continue;
                    if (project.Stage is "blocked" or "cancelled")
                        throw Invalid("A blocked or cancelled Town project must release unspent claims and incoming bindings.");
                    liveQuantities[delivery.ItemKind] = liveQuantities.GetValueOrDefault(delivery.ItemKind) + delivery.Quantity;
                    if (liveQuantities[delivery.ItemKind] > cost.Amount)
                        throw Invalid("Town project inputs exceed the approved material budget.");
                    if (project.Stage == "completed")
                    {
                        if (delivery.DeliveredTick is null) throw Invalid("A completed Town project cannot retain undelivered cargo.");
                        continue;
                    }
                    var lot = inventory.Lots.SingleOrDefault(item => item.Id == delivery.LotId);
                    if (!liveLots.Add(delivery.LotId) || lot is null || lot.OwnerId != town.Id ||
                        lot.ItemKind != delivery.ItemKind || lot.Quantity != delivery.Quantity ||
                        lot.ContainerLotId is not null || lot.StorageBuildingId is not null || lot.DeliveryBuildingId is not null ||
                        lot.FreshnessBasisPoints <= 0 || lot.ConditionBasisPoints <= 0 ||
                        lot.Id != delivery.SourceLotId && lot.ProvenanceLotId != delivery.SourceLotId)
                        throw Invalid("Town project materials need an exact, usable, Town-owned physical lot.");
                    if (delivery.DeliveredTick is not null)
                    {
                        if (lot.CarrierId is not null || lot.GroundPosition != new InventoryGroundPosition(project.Plan.Site.X, project.Plan.Site.Y))
                            throw Invalid("Delivered Town materials must actually be at the approved site.");
                    }
                    else if (lot.CarrierId != delivery.ContributorId || lot.GroundPosition is not null ||
                        inventory.Reservations.Any(receipt => receipt.LotId == lot.Id && receipt.State is
                            InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed) ||
                        !town.ResidentIds.Contains(delivery.ContributorId, StringComparer.Ordinal) ||
                        !society.Inhabitants.Any(person => person.Id == delivery.ContributorId &&
                            person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder))
                        throw Invalid("Incoming Town materials need actual adult resident custody.");
                }
                var allDelivered = project.Plan.Budget.All(cost => TownProjectRules.DeliveredQuantity(project, town.Id, inventory, cost.ResourceId) == cost.Amount);
                if (project.Stage == "working" && !allDelivered || project.Stage == "completed" &&
                    (project.WorkDone != TownProjectRules.WorkTicks || !allDelivered ||
                     project.CompletedBuildingId != TownProjectRules.BuildingId(project.Id) ||
                     !completedBuildings.Add(project.CompletedBuildingId)))
                    throw Invalid("A working or completed Town project lacks its exact delivered material budget.");
                if (project.Stage != "completed" && (project.CompletedBuildingId is not null || project.RemovedTick is not null))
                    throw Invalid("An unfinished Town project cannot own a completed building.");
                if (project.Stage == "completed")
                {
                    var building = simulation.Buildings.SingleOrDefault(item => item.InstanceId == project.CompletedBuildingId);
                    if (project.RemovedTick is { } removed && (removed < project.LastTransitionTick || removed > society.WorldTick || building is not null) ||
                        project.RemovedTick is null && (building is null || building.DefinitionId != project.Plan.DefinitionId ||
                            building.Position != project.Plan.Site || building.Entrance != project.Plan.Entrance ||
                            building.TownId != town.Id || building.HouseholdId is not null || building.PlacedTick != project.LastTransitionTick ||
                            lantern && building.Footprint is not null))
                        throw Invalid("A completed Town building disagrees with its paid approval or retained removal.");
                }
            }
            foreach (var proposal in town.Governance?.Proposals.Where(item => item.Kind == "project" && item.Status == "passed") ?? [])
                if (town.Projects.Count(project => project.ProposalId == proposal.Id) != 1)
                    throw Invalid("Each passed Town construction proposal requires one retained project.");
        }
        if (inventory.Reservations.Any(receipt => receipt.Purpose.StartsWith("town-project:", StringComparison.Ordinal) && !reservations.Contains(receipt.Id)) ||
            simulation.Buildings.Any(building => building.DefinitionId == hall.CanonicalId && !completedBuildings.Contains(building.InstanceId)))
            throw Invalid("A Town construction receipt or Hall has no matching paid project.");
        if (simulation.Buildings.Any(building => StreetLanternContent.IsLantern(building.DefinitionId) &&
                !completedBuildings.Contains(building.InstanceId)))
            throw Invalid("A street lantern has no matching paid Town project.");
    }

    private static void ValidateLiveSite(IReadOnlyList<TownRuntimeState> towns, TownRuntimeState town,
        TownConstructionProject project, SeededMap map, WorldContentSimulationState simulation,
        DeclarativeWorldContentState content, IReadOnlyList<TownLandTitleRecord> titles,
        IReadOnlyList<HouseholdLandUseRight> rights, IReadOnlyList<HouseholdLandUseRequest> requests,
        IReadOnlyList<FarmFieldState> fields, IReadOnlyList<GridPoint> roads, IReadOnlyList<BridgeState> bridges)
    {
        var definition = TownProjectRules.Definition(project.Plan.DefinitionId) ??
            throw Invalid("An active Town project must use supported paid content.");
        var footprint = WorldContentSimulationRules.Footprint(definition, project.Plan.Site).ToHashSet();
        var lantern = StreetLanternContent.IsLantern(project.Plan.DefinitionId);
        var title = titles.Where(item => item.TownId == town.Id).SelectMany(item => item.Tiles).ToHashSet();
        var claimed = rights.SelectMany(item => item.Tiles).Concat(requests.SelectMany(item => item.Tiles))
            .Concat(titles.Where(item => item.TownId != town.Id).SelectMany(item => item.Tiles)).ToHashSet();
        var occupied = map.Resources.Select(item => item.Position).Concat(map.CampObjects.Select(item => item.Position))
            .Concat(fields.Select(item => item.Position))
            .Concat(simulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                content.Buildings.Single(candidate => candidate.CanonicalId == building.DefinitionId), building)))
            .Concat((simulation.BuildingExpansions ?? []).Where(job => job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused)
                .SelectMany(job => Enumerable.Range(0, job.TargetFootprint.Height).SelectMany(y =>
                    Enumerable.Range(0, job.TargetFootprint.Width).Select(x => new GridPoint(job.TargetPosition.X + x, job.TargetPosition.Y + y)))))
            .Concat(towns.SelectMany(item => item.Projects).Where(other => other.Id != project.Id && other.Stage is not ("completed" or "cancelled"))
                .SelectMany(other => WorldContentSimulationRules.Footprint(
                    TownProjectRules.Definition(other.Plan.DefinitionId) ?? throw Invalid("An active Town project has unsupported content."), other.Plan.Site)))
            .ToHashSet();
        var reservedEntrances = towns.SelectMany(item => item.Projects)
            .Where(other => other.Id != project.Id && other.Stage is not ("completed" or "cancelled"))
            .Select(other => other.Plan.Entrance).ToHashSet();
        if (footprint.Any(point => !title.Contains(point) || claimed.Contains(point) || occupied.Contains(point) || reservedEntrances.Contains(point)) ||
            footprint.Any(roads.Contains) || bridges.SelectMany(bridge => bridge.Entrances).Any(footprint.Contains) ||
            occupied.Contains(project.Plan.Entrance) ||
            !lantern && reservedEntrances.Contains(project.Plan.Entrance) ||
            lantern && (!title.Contains(project.Plan.Entrance) || claimed.Contains(project.Plan.Entrance) || !roads.Contains(project.Plan.Entrance)))
            throw Invalid("An active Town construction site must remain clear and uncontested Town-titled land.");
    }

    private static bool FootprintIsBuildable(SeededMap map, GridPoint site, int width, int height)
    {
        if (site.X < 0 || site.Y < 0 || (long)site.X + width > map.Width || (long)site.Y + height > map.Height) return false;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (!map.IsBuildable(new GridPoint(site.X + x, site.Y + y))) return false;
        return true;
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
