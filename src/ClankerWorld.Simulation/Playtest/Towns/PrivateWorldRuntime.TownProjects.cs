using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string TownProjectSupplyPrefix = "town_project_supply:";
    private const string TownProjectDeliverPrefix = "town_project_deliver:";
    private const string TownProjectWorkPrefix = "town_project_work:";
    private const string TownProjectGatherPrefix = "town_project_gather:";
    private const string TownProjectReturnPrefix = "town_project_return:";
    private const string TownProjectDonatePrefix = "town_project_donate:";

    private sealed record TownProjectChoice(string Id, string Kind, TownRuntimeState Town,
        TownConstructionProject Project, string ItemKind = "", InventoryLot? Lot = null,
        int Quantity = 0, TownProjectDelivery? Delivery = null, MapResource? Resource = null,
        PlacedBuilding? Warehouse = null);

    private static string TownProjectChoiceId(string prefix, params string[] identity) => prefix +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identity))));

    private static bool IsLiveTownProject(TownConstructionProject project) =>
        project.Stage is "supplying" or "working" or "blocked";

    private static bool IsTownProjectDonationCandidate(string id) =>
        id.StartsWith(TownProjectDonatePrefix, StringComparison.Ordinal);

    private static bool IsTownProjectCandidate(string id) =>
        id.StartsWith(TownProjectSupplyPrefix, StringComparison.Ordinal) ||
        id.StartsWith(TownProjectDeliverPrefix, StringComparison.Ordinal) ||
        id.StartsWith(TownProjectWorkPrefix, StringComparison.Ordinal) ||
        id.StartsWith(TownProjectGatherPrefix, StringComparison.Ordinal) ||
        id.StartsWith(TownProjectReturnPrefix, StringComparison.Ordinal);

    private bool IsActiveTownProjectDelivery(string lotId) => towns.Any(town => town.Projects.Any(project =>
        IsLiveTownProject(project) && project.Deliveries.Any(delivery => delivery.LotId == lotId &&
            delivery.ReleasedTick is null && delivery.DeliveredTick is null)));

    private bool ReadyForTownProject(string actor) => AdultResident(actor) &&
        !NeedsUrgentFood(inhabitants[actor]) && !NeedsUrgentWarmth(inhabitants[actor]) &&
        !IsConversationBusy(actor) && inhabitants[actor].Equipment?.Repair is null &&
        inhabitants[actor].Project is not { Stage: not ("completed" or "cancelled") } &&
        !fields.Any(field => field.Work?.WorkerId == actor);

    private void SetTownProject(string townId, TownConstructionProject project)
    {
        var town = towns.Single(item => item.Id == townId);
        SetTown(town with
        {
            Projects = town.Projects.Where(item => item.Id != project.Id).Append(project)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        });
    }

    private bool TownProjectIncomingIsLive(string townId, TownProjectDelivery delivery)
    {
        var lot = society.Checkpoint.Inventory.Lots.SingleOrDefault(item => item.Id == delivery.LotId);
        return delivery.ReleasedTick is null && delivery.DeliveredTick is null && lot is not null &&
            lot.OwnerId == townId && lot.ItemKind == delivery.ItemKind && lot.Quantity == delivery.Quantity &&
            lot.ContainerLotId is null && lot.StorageBuildingId is null && lot.GroundPosition is null &&
            lot.DeliveryBuildingId is null && lot.CarrierId == delivery.ContributorId &&
            AdultResident(delivery.ContributorId) && TownForResident(delivery.ContributorId) == townId &&
            AvailableLotQuantity(lot) == delivery.Quantity;
    }

    private int TownProjectMissing(TownRuntimeState town, TownConstructionProject project, string itemKind)
    {
        var budget = project.Plan.Budget.Single(item => item.ResourceId == itemKind).Amount;
        var delivered = TownProjectRules.DeliveredQuantity(project, town.Id, society.Checkpoint.Inventory, itemKind);
        var incoming = project.Deliveries.Where(delivery => delivery.ItemKind == itemKind &&
            TownProjectIncomingIsLive(town.Id, delivery)).Sum(delivery => delivery.Quantity);
        return Math.Max(0, budget - delivered - incoming);
    }

    private bool TownProjectHasAllMaterials(TownRuntimeState town, TownConstructionProject project) =>
        project.Plan.Budget.All(cost => TownProjectRules.DeliveredQuantity(project, town.Id,
            society.Checkpoint.Inventory, cost.ResourceId) == cost.Amount);

    private TownConstructionProject ReleaseTownProjectClaims(TownConstructionProject project, string reason)
    {
        var deliveries = project.Deliveries.Select(delivery => delivery.ReleasedTick is null
            ? delivery with { ReleasedTick = WorldTick, ReleaseReason = reason } : delivery).ToArray();
        var claims = project.Deliveries.Where(delivery => delivery.ReleasedTick is null &&
                delivery.ReservationId is not null)
            .Select(delivery => delivery.ReservationId!).ToHashSet(StringComparer.Ordinal);
        if (claims.Count > 0)
            ApplyInventoryTransition(inventory =>
            {
                foreach (var reservation in inventory.Reservations.Where(item => claims.Contains(item.Id) &&
                             item.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed).ToArray())
                    inventory = InventoryFixture.ReleaseReservation(inventory, reservation.Id, "town_project_material_released");
                return inventory;
            });
        return project with { Deliveries = deliveries, LastTransitionTick = WorldTick };
    }

    private void BlockTownProject(TownRuntimeState town, TownConstructionProject project, string failure)
    {
        if (project.Stage == "blocked" && project.Blocker == failure) return;
        project = ReleaseTownProjectClaims(project, failure) with { Stage = "blocked", Blocker = failure };
        SetTownProject(town.Id, project);
        AppendEvent("town_project_blocked", $"{town.Id}:{project.Id}:{failure}", project.Plan.Site);
    }

    private void MaintainTownProjects()
    {
        foreach (var originalTown in towns.ToArray())
        {
            foreach (var proposal in originalTown.Governance?.Proposals.Where(item => item.Kind == "project" &&
                         item.Status == "passed" && item.Project is not null) ?? [])
            {
                var town = towns.Single(item => item.Id == originalTown.Id);
                var id = TownProjectRules.ProjectId(proposal);
                if (town.Projects.Any(project => project.Id == id)) continue;
                var project = new TownConstructionProject(id, proposal.Id, proposal.Project!,
                    proposal.SettledTick!.Value, "supplying", 0, WorldTick, []);
                SetTownProject(town.Id, project);
                AppendEvent("town_project_approved", $"{town.Id}:{project.Id}:{project.Plan.Name}", project.Plan.Site);
            }
            foreach (var originalProject in towns.Single(item => item.Id == originalTown.Id).Projects.Where(IsLiveTownProject).ToArray())
            {
                var town = towns.Single(item => item.Id == originalTown.Id);
                var project = town.Projects.Single(item => item.Id == originalProject.Id);
                if (TownProjectSiteFailure(town, project.Plan, project.Id) is { } failure)
                {
                    BlockTownProject(town, project, failure);
                    continue;
                }
                if (project.Stage == "blocked")
                {
                    project = project with { Stage = "supplying", Blocker = null, LastTransitionTick = WorldTick };
                    SetTownProject(town.Id, project);
                    AppendEvent("town_project_resumed", $"{town.Id}:{project.Id}", project.Plan.Site);
                }
                var staleIncoming = project.Deliveries.Where(delivery => delivery.ReleasedTick is null &&
                    delivery.DeliveredTick is null && !TownProjectIncomingIsLive(town.Id, delivery)).Select(delivery => delivery.Id)
                    .ToHashSet(StringComparer.Ordinal);
                if (staleIncoming.Count > 0)
                {
                    project = project with
                    {
                        Deliveries = project.Deliveries.Select(delivery => staleIncoming.Contains(delivery.Id)
                            ? delivery with { ReleasedTick = WorldTick, ReleaseReason = "The load is no longer in resident custody." }
                            : delivery).ToArray(),
                        LastTransitionTick = WorldTick,
                    };
                    SetTownProject(town.Id, project);
                }
                var delivered = project.Deliveries.Where(delivery => delivery.ReleasedTick is null && delivery.DeliveredTick is not null).ToArray();
                if (delivered.GroupBy(delivery => delivery.ItemKind).Any(group =>
                        TownProjectRules.DeliveredQuantity(project, town.Id, society.Checkpoint.Inventory, group.Key) != group.Sum(delivery => delivery.Quantity)))
                {
                    BlockTownProject(town, project, "Delivered materials are no longer usable at the approved site.");
                    continue;
                }
                var stage = TownProjectHasAllMaterials(town, project) ? "working" : "supplying";
                if (project.Stage != stage)
                    SetTownProject(town.Id, project with { Stage = stage, LastTransitionTick = WorldTick });
            }
        }
    }

    private IEnumerable<(TownRuntimeState Town, TownConstructionProject Project)> KnownTownProjects(string actor)
    {
        if (!ReadyForTownProject(actor) || TownForResident(actor) is not { } townId) yield break;
        var town = towns.Single(item => item.Id == townId);
        if (town.Governance is null) yield break;
        foreach (var project in town.Projects.Where(item => item.Stage is "supplying" or "working")
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
            if (CivicHistory(town).Knows(actor, project.ProposalId) &&
                TownProjectSiteFailure(town, project.Plan, project.Id, actor) is null)
                yield return (town, project);
    }

    private bool CanWalkForTownProject(string actor, GridPoint from, GridPoint target, int range = 0) =>
        IsWithinInteractionRange(from, target, range) || FindUnoccupiedRoute(actor, from, target, range).Count > 0;

    private IEnumerable<InventoryLot> TownProjectMaterialSources(TownRuntimeState town,
        TownConstructionProject project, string actor, string itemKind)
    {
        var warehouses = WarehousesForTown(town.Id).Select(item => item.InstanceId).ToHashSet(StringComparer.Ordinal);
        var site = new InventoryGroundPosition(project.Plan.Site.X, project.Plan.Site.Y);
        var recoverable = town.Projects.SelectMany(item => item.Deliveries).Where(delivery => delivery.ReleasedTick is not null)
            .Select(delivery => delivery.LotId).ToHashSet(StringComparer.Ordinal);
        return society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == town.Id && lot.ItemKind == itemKind &&
                lot.ContainerLotId is null && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0 &&
                !IsActiveTownProjectDelivery(lot.Id) &&
                (lot.CarrierId == actor || lot.CarrierId is null &&
                    (lot.StorageBuildingId is { } storage && warehouses.Contains(storage) ||
                     lot.GroundPosition == site || lot.GroundPosition is not null && recoverable.Contains(lot.Id))))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal);
    }

    private IEnumerable<TownProjectChoice> TownProjectChoices(string actor)
    {
        if (!ReadyForTownProject(actor)) yield break;
        var person = inhabitants[actor];
        foreach (var (town, project) in KnownTownProjects(actor))
        {
            foreach (var delivery in project.Deliveries.Where(item => item.ContributorId == actor &&
                         TownProjectIncomingIsLive(town.Id, item)))
                if (CanWalkForTownProject(actor, person.Position, project.Plan.Site))
                    yield return new(TownProjectChoiceId(TownProjectDeliverPrefix, project.Id, delivery.Id), "deliver",
                        town, project, delivery.ItemKind, society.Checkpoint.Inventory.GetLot(delivery.LotId),
                        delivery.Quantity, delivery);
            if (TownProjectHasAllMaterials(town, project))
            {
                if (CanWalkForTownProject(actor, person.Position, project.Plan.Site))
                    yield return new(TownProjectChoiceId(TownProjectWorkPrefix, project.Id), "work", town, project);
                continue;
            }
            foreach (var cost in project.Plan.Budget)
            {
                var missing = TownProjectMissing(town, project, cost.ResourceId);
                if (missing <= 0) continue;
                foreach (var lot in TownProjectMaterialSources(town, project, actor, cost.ResourceId))
                {
                    var alreadyCarried = lot.CarrierId == actor;
                    var quantity = Math.Min(missing, Math.Min(AvailableLotQuantity(lot),
                        alreadyCarried ? lot.Quantity : Math.Min(WarehouseLoadQuantity, FreeCarryCapacity(actor))));
                    if (quantity <= 0) continue;
                    var position = HouseholdStockPosition(lot);
                    var range = HouseholdStockInteractionRange(lot);
                    if (!alreadyCarried && !CanWalkForTownProject(actor, person.Position, position, range) ||
                        !CanWalkForTownProject(actor, alreadyCarried ? person.Position : position, project.Plan.Site)) continue;
                    yield return new(TownProjectChoiceId(TownProjectSupplyPrefix, project.Id, lot.Id,
                        quantity.ToString(CultureInfo.InvariantCulture)), "supply", town, project, cost.ResourceId, lot, quantity);
                    break;
                }
                // A personal harvest never promises or donates the resulting lot automatically.
                if (PersonalTownProjectDonationLots(actor, cost.ResourceId).Any()) continue;
                var source = MaterialSource(cost.ResourceId, actor);
                if (source is null) continue;
                var harvest = ProjectMaterialHarvest(actor, cost.ResourceId, source);
                if (FreeCarryCapacity(actor) < (harvest is null ? 1 : checked(harvest.Quantity + harvest.TreeSeedQuantity))) continue;
                yield return new(TownProjectChoiceId(TownProjectGatherPrefix, project.Id, cost.ResourceId), "gather",
                    town, project, cost.ResourceId, Resource: source);
            }
        }
        foreach (var choice in TownProjectReturnChoices(actor)) yield return choice;
    }

    private IEnumerable<InventoryLot> PersonalTownProjectDonationLots(string actor, string itemKind) =>
        society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == itemKind &&
                PersonalEquipmentRules.IsCarried(lot, actor) && lot.ContainerLotId is null &&
                lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) > 0 &&
                !PersonalEquipmentRules.IsSelected(inhabitants[actor].Equipment, lot.Id))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal);

    private IEnumerable<TownProjectChoice> TownProjectDonationChoices(string actor)
    {
        foreach (var (town, project) in KnownTownProjects(actor))
        {
            if (!CanWalkForTownProject(actor, inhabitants[actor].Position, project.Plan.Site)) continue;
            foreach (var cost in project.Plan.Budget)
            {
                var missing = TownProjectMissing(town, project, cost.ResourceId);
                if (missing <= 0) continue;
                foreach (var lot in PersonalTownProjectDonationLots(actor, cost.ResourceId).Take(4))
                {
                    var quantity = Math.Min(missing, AvailableLotQuantity(lot));
                    yield return new(TownProjectChoiceId(TownProjectDonatePrefix, project.Id, lot.Id,
                        quantity.ToString(CultureInfo.InvariantCulture),
                        inhabitants[actor].Position == project.Plan.Site ? "at-site" : "travel"),
                        "donate", town, project, cost.ResourceId, lot, quantity);
                }
            }
        }
    }

    private IEnumerable<TownProjectChoice> TownProjectReturnChoices(string actor)
    {
        foreach (var town in towns.OrderBy(item => item.Id, StringComparer.Ordinal))
            foreach (var project in town.Projects.OrderBy(item => item.Id, StringComparer.Ordinal))
                foreach (var delivery in project.Deliveries.Where(item => item.ReleasedTick is not null && item.ContributorId == actor)
                             .DistinctBy(item => item.LotId))
                {
                    var lot = society.Checkpoint.Inventory.Lots.SingleOrDefault(item => item.Id == delivery.LotId);
                    if (lot is null || lot.OwnerId != town.Id || lot.CarrierId != actor || lot.ContainerLotId is not null ||
                        lot.DeliveryBuildingId is not null || PhysicalUnreservedQuantity(lot) <= 0 || IsActiveTownProjectDelivery(lot.Id)) continue;
                    var warehouse = WarehousesForTown(town.Id).FirstOrDefault(item => StorageRoomAfterInboundDeliveries(item.InstanceId) > 0 &&
                        CanWalkForTownProject(actor, inhabitants[actor].Position, item.Position));
                    if (warehouse is null)
                    {
                        yield return new(TownProjectChoiceId(TownProjectReturnPrefix, project.Id, lot.Id, "ground"),
                            "return", town, project, lot.ItemKind, lot, PhysicalUnreservedQuantity(lot), delivery);
                        continue;
                    }
                    var quantity = Math.Min(PhysicalUnreservedQuantity(lot), StorageRoomAfterInboundDeliveries(warehouse.InstanceId));
                    if (quantity <= 0) continue;
                    yield return new(TownProjectChoiceId(TownProjectReturnPrefix, project.Id, lot.Id, warehouse.InstanceId),
                        "return", town, project, lot.ItemKind, lot, quantity, delivery, Warehouse: warehouse);
                }
    }

    private void AddTownProjectCandidates(List<CognitionCandidate> candidates, string actor)
    {
        foreach (var choice in TownProjectChoices(actor).Take(16))
        {
            var description = choice.Kind switch
            {
                "supply" => $"Carry {choice.Quantity} Town-owned {choice.ItemKind} to the approved {choice.Project.Plan.Name} site.",
                "deliver" => $"Deliver your actual Town-owned {choice.ItemKind} load to {choice.Project.Plan.Name}.",
                "work" => $"Help build {choice.Project.Plan.Name} at its approved site using the delivered Town materials.",
                "gather" => $"Gather personal {choice.ItemKind} near {choice.Project.Plan.Name}; donating it is a separate choice.",
                _ => choice.Warehouse is null
                    ? $"Set down the released Town-owned {choice.ItemKind} load at your actual position. It stays Town property."
                    : $"Return the released Town-owned {choice.ItemKind} load to its Town Warehouse.",
            };
            candidates.Add(new(choice.Id, description, choice.Kind is "deliver" or "return" ? 24 : 36,
                choice.Warehouse?.InstanceId ?? choice.Project.Id, choice.Project.Plan.Name));
        }
    }

    private void AddTownProjectDonationCandidates(List<CognitionCandidate> candidates, string actor)
    {
        foreach (var choice in TownProjectDonationChoices(actor).Take(16))
            candidates.Add(new(choice.Id,
                $"Choose to donate {choice.Quantity} of your own carried {choice.ItemKind} to {choice.Project.Plan.Name}. " +
                "The Town will own that exact contribution at the approved site.", 170, choice.Project.Id, choice.Project.Plan.Name));
    }

    private bool ApplyTownProjectCandidate(string actor, PlaytestInhabitantState state, string id)
    {
        if (!IsTownProjectCandidate(id)) return false;
        var choice = TownProjectChoices(actor).FirstOrDefault(item => item.Id == id);
        if (choice is null) return true;
        switch (choice.Kind)
        {
            case "supply": PickUpTownProjectLoad(actor, state, choice); break;
            case "deliver": DeliverTownProjectLoad(actor, state, choice); break;
            case "work": WorkOnTownProject(actor, state, choice); break;
            case "gather": GatherProjectMaterial(actor, state, choice.ItemKind, choice.Resource!, deliveryBuildingId: null); break;
            case "return": ReturnTownProjectLoad(actor, state, choice); break;
        }
        return true;
    }

    // Only a fresh, exact personal-model admission may call this final donation path.
    private bool ApplyTownProjectDonation(string actor, string id)
    {
        if (!IsTownProjectDonationCandidate(id)) return false;
        var choice = TownProjectDonationChoices(actor).FirstOrDefault(item => item.Id == id);
        if (choice is null) return true;
        var person = inhabitants[actor];
        if (person.Position != choice.Project.Plan.Site)
        {
            MoveToward(actor, person, choice.Project.Plan.Site, "town_project_donation", 0);
            return true;
        }
        var deliveryId = NextTownProjectDeliveryId(choice.Project, actor, choice.Lot!.Id);
        var operation = deliveryId + ":donation";
        var lotId = choice.Quantity == choice.Lot.Quantity ? choice.Lot.Id : choice.Lot.Id + "#transfer:" + operation;
        var delivery = new TownProjectDelivery(deliveryId, actor, choice.Lot.Id, lotId, choice.ItemKind,
            choice.Quantity, WorldTick, WorldTick, deliveryId + ":input");
        ApplyInventoryTransition(inventory => ReserveTownProjectDelivery(InventoryFixture.Transfer(inventory,
            operation, actor, choice.Town.Id, choice.Lot.Id, choice.Quantity, "town_project_donated",
            destinationGroundPosition: new(choice.Project.Plan.Site.X, choice.Project.Plan.Site.Y)), choice.Town.Id, choice.Project, delivery));
        SetTownProject(choice.Town.Id, choice.Project with
        {
            Deliveries = choice.Project.Deliveries.Append(delivery).ToArray(),
            LastTransitionTick = WorldTick,
        });
        AppendEvent("town_project_donated", $"{actor}:{choice.Project.Id}:{lotId}:{choice.Quantity}:{choice.ItemKind}", choice.Project.Plan.Site);
        return true;
    }

    private void ContinueTownProjectDonationWalk(string actor, string id)
    {
        if (!IsTownProjectDonationCandidate(id)) return;
        var choice = TownProjectDonationChoices(actor).FirstOrDefault(item => item.Id == id);
        if (choice is not null && inhabitants[actor].Position != choice.Project.Plan.Site)
            MoveToward(actor, inhabitants[actor], choice.Project.Plan.Site, "town_project_donation", 0);
    }

    private string NextTownProjectDeliveryId(TownConstructionProject project, string actor, string lotId) =>
        TownProjectRules.DeliveryId(project.Id, actor, lotId, WorldTick, project.Deliveries.Count);

    private static InventoryCheckpoint ReserveTownProjectDelivery(InventoryCheckpoint inventory, string townId,
        TownConstructionProject project, TownProjectDelivery delivery) => InventoryFixture.Reserve(inventory,
        delivery.ReservationId!, townId, delivery.LotId, delivery.Quantity,
        TownProjectRules.ReservationPurpose(project.Id), long.MaxValue);

    private void PickUpTownProjectLoad(string actor, PlaytestInhabitantState state, TownProjectChoice choice)
    {
        var lot = choice.Lot!;
        var position = HouseholdStockPosition(lot);
        var range = HouseholdStockInteractionRange(lot);
        if (lot.CarrierId != actor && !IsWithinInteractionRange(state.Position, position, range))
        {
            MoveToward(actor, state, position, "town_project_materials", range);
            return;
        }
        var id = NextTownProjectDeliveryId(choice.Project, actor, lot.Id);
        var operation = id + ":pickup";
        var movedId = choice.Quantity == lot.Quantity ? lot.Id : lot.Id + "#move:" + operation;
        ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory, operation, lot.Id,
            choice.Town.Id, choice.Quantity, carrierId: actor));
        var delivery = new TownProjectDelivery(id, actor, lot.Id, movedId, lot.ItemKind, choice.Quantity, WorldTick);
        SetTownProject(choice.Town.Id, choice.Project with
        {
            Deliveries = choice.Project.Deliveries.Append(delivery).ToArray(),
            LastTransitionTick = WorldTick,
        });
        AppendEvent("town_project_material_picked_up", $"{actor}:{choice.Project.Id}:{movedId}:{choice.Quantity}:{choice.ItemKind}", choice.Project.Plan.Site);
    }

    private void DeliverTownProjectLoad(string actor, PlaytestInhabitantState state, TownProjectChoice choice)
    {
        if (state.Position != choice.Project.Plan.Site)
        {
            MoveToward(actor, state, choice.Project.Plan.Site, "town_project_materials", 0);
            return;
        }
        var delivery = choice.Delivery! with { DeliveredTick = WorldTick, ReservationId = choice.Delivery.Id + ":input" };
        ApplyInventoryTransition(inventory => ReserveTownProjectDelivery(InventoryFixture.Relocate(inventory,
            delivery.Id + ":deliver", delivery.LotId, choice.Town.Id, delivery.Quantity,
            groundPosition: new(choice.Project.Plan.Site.X, choice.Project.Plan.Site.Y)), choice.Town.Id, choice.Project, delivery));
        SetTownProject(choice.Town.Id, choice.Project with
        {
            Deliveries = choice.Project.Deliveries.Select(item => item.Id == delivery.Id ? delivery : item).ToArray(),
            LastTransitionTick = WorldTick,
        });
        AppendEvent("town_project_material_delivered", $"{actor}:{choice.Project.Id}:{delivery.LotId}:{delivery.Quantity}:{delivery.ItemKind}", choice.Project.Plan.Site);
    }

    private void ReturnTownProjectLoad(string actor, PlaytestInhabitantState state, TownProjectChoice choice)
    {
        if (choice.Warehouse is not { } warehouse)
        {
            ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory,
                $"{choice.Id}:{WorldTick}", choice.Lot!.Id, choice.Town.Id, choice.Quantity,
                groundPosition: new(state.Position.X, state.Position.Y)));
            AppendEvent("town_project_material_returned",
                $"{actor}:{choice.Project.Id}:{choice.Lot!.Id}:{choice.Quantity}:ground", choice.Project.Plan.Site);
            return;
        }
        if (state.Position != warehouse.Position)
        {
            MoveToward(actor, state, warehouse.Position, "town_project_return", 0);
            return;
        }
        ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory,
            $"{choice.Id}:{WorldTick}", choice.Lot!.Id, choice.Town.Id, choice.Quantity,
            storageBuildingId: warehouse.InstanceId));
        AppendEvent("town_project_material_returned", $"{actor}:{choice.Project.Id}:{choice.Lot!.Id}:{choice.Quantity}:{warehouse.InstanceId}", choice.Project.Plan.Site);
    }

    private void WorkOnTownProject(string actor, PlaytestInhabitantState state, TownProjectChoice choice)
    {
        if (state.Position != choice.Project.Plan.Site)
        {
            MoveToward(actor, state, choice.Project.Plan.Site, "town_project_work", 0);
            return;
        }
        var workNeeded = TownProjectRules.RequiredWork(choice.Project.Plan);
        if (choice.Project.WorkDone == workNeeded)
        {
            CompletePaidTownProject(actor, choice.Town.Id, choice.Project);
            return;
        }
        if (!SettlementIllnessRules.AllowsWork(actor, WorldTick, state.Survival?.IllnessBasisPoints ?? 0)) return;
        var hammer = ToolProgressionRules.PlanWork(society.Checkpoint.Inventory, actor, ToolFamily.Hammer);
        var done = Math.Min(workNeeded, choice.Project.WorkDone + (hammer?.WorkUnits ?? 1));
        if (hammer is not null) ApplyToolWork(actor, hammer);
        var project = choice.Project with { Stage = "working", WorkDone = done, LastTransitionTick = WorldTick };
        SetTownProject(choice.Town.Id, project);
        AppendEvent("town_project_worked", $"{actor}:{project.Id}:{done}", project.Plan.Site);
        if (done == workNeeded) CompletePaidTownProject(actor, choice.Town.Id, project);
    }

    private void CompletePaidTownProject(string actor, string townId, TownConstructionProject project)
    {
        var town = towns.Single(item => item.Id == townId);
        if (TownProjectSiteFailure(town, project.Plan, project.Id, actor) is { } failure)
        {
            BlockTownProject(town, project, failure);
            return;
        }
        if (!TownProjectHasAllMaterials(town, project))
        {
            BlockTownProject(town, project, "The exact delivered Town material budget is unavailable.");
            return;
        }
        var id = TownProjectRules.BuildingId(project.Id);
        if (worldSimulation.Buildings.Any(item => item.InstanceId == id))
            throw new InvalidOperationException("The approved Town project already has a building.");
        ApplyInventoryTransition(inventory =>
        {
            foreach (var delivery in project.Deliveries.Where(item => item.ReleasedTick is null &&
                         item.DeliveredTick is not null && item.ReservationId is not null))
                inventory = InventoryFixture.ConsumeReservation(inventory, delivery.ReservationId!);
            return inventory;
        });
        var placed = new PlacedBuilding(id, project.Plan.DefinitionId, project.Plan.Site, WorldTick,
            townId, Entrance: project.Plan.Entrance);
        worldSimulation = worldSimulation with
        {
            Buildings = worldSimulation.Buildings.Append(placed).OrderBy(item => item.InstanceId, StringComparer.Ordinal).ToArray(),
        };
        SetTownProject(townId, project with
        {
            Stage = "completed",
            CompletedBuildingId = id,
            Blocker = null,
            LastTransitionTick = WorldTick,
        });
        CompletePaidMarketConstruction(townId, project, placed);
        AssignBuildingToTown(placed, worldContent.Buildings.Single(item => item.CanonicalId == placed.DefinitionId));
        CreditCompletedWork(actor, "building");
        AppendEvent("town_project_completed", $"{actor}:{project.Id}:{id}:{project.Plan.Name}", project.Plan.Site);
    }
}
