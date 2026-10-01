using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string PortSupplyPrefix = "port_supply:";
    private const string PortBuildBoatPrefix = "port_build_boat:";
    private const string PortMaterialPrefix = "port_material:";
    private const string BoatEstatePickupPrefix = "boat_estate_pickup:";
    private const int PlannedTownPorts = 2; // Trial planning bound; explicit legal placement remains available.
    private sealed record PortSupplyNeed(PlacedBuilding Port, string Kind, int Missing,
        InventoryLot? Carried, InventoryLot? Stock, MapResource? Source);

    private bool PortNeedsBoat(PlacedBuilding port) =>
        !worldSimulation.ProductionJobs.Any(job => job.BuildingInstanceId == port.InstanceId &&
            worldContent.Recipes.Any(recipe => recipe.CanonicalId == job.RecipeId && recipe.Tags.Contains("boat", StringComparer.Ordinal)) &&
            job.State is WorldProductionJobState.Running or WorldProductionJobState.Completed);

    private RecipeDefinition BoatRecipe(PlacedBuilding port) => worldContent.Recipes.Single(recipe =>
        recipe.WorkstationBuildingId == port.DefinitionId && recipe.Tags.Contains("boat", StringComparer.Ordinal));

    private IEnumerable<PortSupplyNeed> PortSupplyNeeds(string actor)
    {
        if (TownForResident(actor) is not { } town || !AdultResident(actor)) yield break;
        var inventory = society.Checkpoint.Inventory;
        foreach (var port in worldSimulation.Buildings.Where(port => port.TownId == town && Port(port.InstanceId) is not null)
                     .OrderBy(port => port.InstanceId, StringComparer.Ordinal))
        {
            if (!PortNeedsBoat(port) || !PortIsLegal(port) || StorageRoom(port.InstanceId) == 0) continue;
            foreach (var input in BoatRecipe(port).Inputs)
            {
                var missing = input.Amount - inventory.Lots.Where(lot => lot.OwnerId == town &&
                    lot.StorageBuildingId == port.InstanceId && lot.ItemKind == input.ResourceId).Sum(AvailableLotQuantity);
                if (missing <= 0) continue;
                var carried = inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == input.ResourceId &&
                    lot.StorageBuildingId is null && lot.DeliveryBuildingId is null && lot.GroundPosition is null &&
                    lot.ContainerLotId is null && AvailableLotQuantity(lot) > 0).OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
                var stock = carried is null ? inventory.Lots.Where(lot => lot.ItemKind == input.ResourceId &&
                        lot.ContainerLotId is null && lot.GroundPosition is null && lot.DeliveryBuildingId is null &&
                        lot.StorageBuildingId != port.InstanceId && AvailableLotQuantity(lot) > 0 &&
                        (lot.OwnerId == HouseholdFor(actor) || lot.OwnerId == town && lot.StorageBuildingId is { } storage &&
                            worldSimulation.Buildings.Any(building => building.InstanceId == storage &&
                                worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("warehouse", StringComparer.Ordinal)))))
                    .OrderBy(lot => lot.OwnerId == town ? 0 : 1).ThenBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault() : null;
                var source = carried is null && stock is null ? MaterialSource(input.ResourceId, actor) : null;
                if (carried is null && CarryingRoom(actor) == 0 || carried is null && stock is null && source is null) continue;
                yield return new(port, input.ResourceId, missing, carried, stock, source);
            }
        }
    }

    private void AddPortCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState person)
    {
        AddBoatEstatePickupCandidates(candidates, actor, person);
        if (!AdultResident(actor) || NeedsUrgentFood(person) || NeedsUrgentWarmth(person) ||
            person.Project is { Stage: not ("completed" or "cancelled") } || TownForResident(actor) is not { } town) return;
        var ports = worldSimulation.Buildings.Where(port => port.TownId == town && Port(port.InstanceId) is not null).ToArray();
        if (PortSupplyNeeds(actor).FirstOrDefault() is { } need)
            candidates.Add(new(PortSupplyPrefix + need.Port.InstanceId + "|" + need.Kind,
                $"Carry real {need.Kind} into the communal Port to build its Town boat.", 38, need.Port.InstanceId));
        foreach (var port in ports.Where(PortNeedsBoat))
        {
            var recipe = BoatRecipe(port);
            if (PortIsLegal(port) && HasIngredientsAtBuilding(recipe.Inputs, town, port.InstanceId))
                candidates.Add(new(PortBuildBoatPrefix + port.InstanceId,
                    "Build the Town's communal boat from wood, rope and iron fittings already stored at this Port.", 37, port.InstanceId));
        }
        if (ports.Length >= PlannedTownPorts || inhabitants.Values.Any(other => other.Project is { Stage: not ("completed" or "cancelled") } project &&
            TownForResident(other.InhabitantId) == town && TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection) &&
            selection.IsBuilding && worldContent.Buildings.Any(definition => definition.CanonicalId == selection.DefinitionId && PortNavigationRules.IsPort(definition)))) return;
        foreach (var definition in worldContent.Buildings.Where(PortNavigationRules.IsPort).OrderBy(definition => definition.CanonicalId, StringComparer.Ordinal))
        {
            var sites = TownLayoutService.RankConstructionSites(CreateTownLayoutContext(actor, building: definition), definition, 1);
            if (sites.Count == 0) continue;
            if (HouseholdHasMaterialsInHand(HouseholdFor(actor), definition.BuildCosts))
            {
                var site = sites[0];
                candidates.Add(new(TownConstructionCandidateIds.Building(definition.CanonicalId, site.Position),
                    $"Build a communal {definition.DisplayName} at ({site.Position.X}, {site.Position.Y}) with a land approach and clear docking water.", 40,
                    $"build-site:{site.Position.X},{site.Position.Y}"));
            }
            else if (definition.BuildCosts.FirstOrDefault(cost => HouseholdMaterialInHand(HouseholdFor(actor), cost.ResourceId) < cost.Amount) is { Amount: > 0 } missing &&
                MaterialSource(missing.ResourceId, actor) is not null && !candidates.Any(candidate => candidate.Id == PortMaterialPrefix + missing.ResourceId))
                candidates.Add(new(PortMaterialPrefix + missing.ResourceId, $"Gather {missing.ResourceId} for a legal communal Port.", 44));
        }
    }

    private void GatherPortMaterial(string actor, PlaytestInhabitantState person, string kind)
    {
        if (!AdultResident(actor) || TownForResident(actor) is null || MaterialSource(kind, actor) is not { } source) return;
        if (CollectGatheringTool(actor, person, kind)) return;
        GatherProjectMaterial(actor, person, kind, source);
    }

    private void SupplyPort(string actor, PlaytestInhabitantState person, string selection)
    {
        var pieces = selection.Split('|');
        if (pieces.Length != 2 || PortSupplyNeeds(actor).FirstOrDefault(need => need.Port.InstanceId == pieces[0] && need.Kind == pieces[1]) is not { } need) return;
        if (need.Carried is { } carried)
        {
            var work = BuildingWorkPosition(need.Port);
            if (person.Position != work) { MoveToward(actor, person, work, "port_supply", 0); return; }
            var quantity = Math.Min(StorageRoom(need.Port.InstanceId), Math.Min(need.Missing, AvailableLotQuantity(carried)));
            if (quantity == 0) return;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"port-supply:{WorldTick}:{actor}",
                actor, need.Port.TownId!, carried.Id, quantity, "communal_port_supplied", need.Port.InstanceId));
            AppendEvent("communal_port_supplied", $"{actor}:{carried.Id}:{quantity}:{need.Port.InstanceId}");
        }
        else if (need.Stock is { } stock)
        {
            var point = HouseholdStockPosition(stock);
            var range = HouseholdStockInteractionRange(stock);
            if (!IsWithinInteractionRange(person.Position, point, range)) { MoveToward(actor, person, point, "port_supply", range); return; }
            var quantity = Math.Min(CarryingRoom(actor), Math.Min(HouseHaulLoadQuantity, Math.Min(need.Missing, AvailableLotQuantity(stock))));
            if (quantity == 0) return;
            ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"port-pickup:{WorldTick}:{actor}",
                stock.OwnerId, actor, stock.Id, quantity, "port_material_picked_up"));
            AppendEvent("port_material_picked_up", $"{actor}:{stock.Id}:{quantity}:{need.Port.InstanceId}");
        }
        else if (need.Source is { } source)
        {
            if (CollectGatheringTool(actor, person, need.Kind)) return;
            GatherProjectMaterial(actor, person, need.Kind, source);
        }
    }

    private void BuildPortBoat(string actor, PlaytestInhabitantState person, string portId)
    {
        if (!AdultResident(actor) || Port(portId) is not { } port || port.TownId != TownForResident(actor) || !PortNeedsBoat(port)) return;
        var work = BuildingWorkPosition(port);
        if (person.Position != work) { MoveToward(actor, person, work, "boat_building", 0); return; }
        var result = StartProductionCore(BoatRecipe(port).CanonicalId, portId, actor, "communal_boat_build_started");
        if (!result.Applied) AppendEvent("communal_boat_build_blocked", $"{actor}:{portId}:{result.Failure}");
    }

    private bool MayPickUpBoatCargo(string actor, InventoryLot lot) => lot.OwnerId == actor || lot.OwnerId == HouseholdFor(actor) ||
        lot.OwnerId == TownForResident(actor) || lot.OwnerId == "settlement:communal";

    private void AddBoatEstatePickupCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState person)
    {
        foreach (var boat in boatTransport.Boats.Where(boat => boat.Journey is null && boat.DockedPortId is not null))
            foreach (var lot in society.Checkpoint.Inventory.Lots.Where(lot => boat.EstateCargoLotIds?.Contains(lot.Id, StringComparer.Ordinal) == true &&
                MayPickUpBoatCargo(actor, lot) && AvailableLotQuantity(lot) > 0))
                if (InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, lot.ContainerCapacity == 0 ? 1 : lot.Quantity) <= CarryingRoom(actor) &&
                    FindUnoccupiedRoute(actor, person.Position, PortGeometryFor(Port(boat.DockedPortId!)!).WorkPosition, 0).Count > 0)
                    candidates.Add(new(BoatEstatePickupPrefix + lot.Id, $"Collect your inherited {lot.ItemKind} from the docked boat at its actual Port.", 28, boat.Id));
    }

    private void PickUpBoatEstateCargo(string actor, PlaytestInhabitantState person, string lotId)
    {
        var boat = boatTransport.Boats.FirstOrDefault(boat => boat.Journey is null && boat.EstateCargoLotIds?.Contains(lotId, StringComparer.Ordinal) == true);
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == lotId);
        if (boat?.DockedPortId is not { } portId || lot is null || !MayPickUpBoatCargo(actor, lot) || Port(portId) is not { } port) return;
        var landing = PortGeometryFor(port).LandTiles.First(point => map.FootDistance(point, boat.Position) == 1);
        if (person.Position != landing) { MoveToward(actor, person, landing, "boat_estate_pickup", 0); return; }
        var quantity = lot.ContainerCapacity == 0 ? Math.Min(CarryingRoom(actor), AvailableLotQuantity(lot)) : lot.Quantity;
        if (quantity <= 0 || InventoryFixture.TransferLoadQuantity(society.Checkpoint.Inventory, lot.Id, quantity) > CarryingRoom(actor)) return;
        if (lot.OwnerId == actor)
            ApplyInventoryTransition(inventory => quantity == lot.Quantity
                ? inventory with
                {
                    Lots = inventory.Lots.Select(item => item.Id == lot.Id || item.ContainerLotId == lot.Id
                        ? item with { GroundPosition = null } : item).ToArray()
                }
                : inventory with
                {
                    Lots = inventory.Lots.Select(item => item.Id == lot.Id ? item with { Quantity = item.Quantity - quantity } : item)
                    .Append(lot with
                    {
                        Id = $"{lot.Id}#boat-pickup:{WorldTick}:{actor}",
                        Quantity = quantity,
                        ProvenanceLotId = lot.Id,
                        GroundPosition = null
                    }).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray()
                });
        else ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"boat-estate-pickup:{WorldTick}:{actor}",
            lot.OwnerId, actor, lot.Id, quantity, "boat_estate_picked_up"));
        MoveBoatEstateCargo(boat);
        AppendEvent("boat_estate_picked_up", $"{actor}:{boat.Id}:{lot.Id}:{quantity}");
    }
}
