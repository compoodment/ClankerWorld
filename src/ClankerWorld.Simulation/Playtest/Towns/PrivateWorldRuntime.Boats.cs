using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int BoatStepTicks = 3; // Trial travel speed, not a terrain or visibility penalty.
    private const string BoatTravelPrefix = "boat_travel:";
    private BoatTransportState boatTransport = BoatTransportState.Empty();
    public IReadOnlyList<BoatState> Boats => boatTransport.Boats;

    private PlacedBuilding? Port(string id) => worldSimulation.Buildings.FirstOrDefault(building => building.InstanceId == id &&
        worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && PortNavigationRules.IsPort(definition)));

    private PortGeometry PortGeometryFor(PlacedBuilding port) => PortNavigationRules.Geometry(map,
        worldContent.Buildings.Single(definition => definition.CanonicalId == port.DefinitionId), port.Position);

    private static IEnumerable<GridPoint> Berths(SeededMap map, PortGeometry geometry) => geometry.DockingTiles
        .Where(dock => geometry.LandTiles.Any(land => map.FootDistance(dock, land) == 1));

    private BoatState? PassengerBoat(string actor) => boatTransport.Boats.FirstOrDefault(boat => boat.Journey?.PassengerId == actor);

    private bool CanUseTownBoat(string townId, string actor) => inhabitants.ContainsKey(actor) &&
        (towns.Any(town => town.Id == townId && town.ResidentIds.Contains(actor, StringComparer.Ordinal)) ||
         boatTransport.GuestPermissions.Contains(new BoatGuestAccess(townId, actor)));

    public EquipmentChangeResult SetBoatGuestPermission(string townId, string visitorId, bool allowed)
    {
        gate.Wait();
        try
        {
            if (!towns.Any(town => town.Id == townId) || !inhabitants.ContainsKey(visitorId))
                return new(false, "Choose an existing Town and a living visitor.");
            var permission = new BoatGuestAccess(townId, visitorId);
            var permissions = boatTransport.GuestPermissions.Where(item => item != permission);
            if (allowed) permissions = permissions.Append(permission);
            boatTransport = boatTransport with
            {
                GuestPermissions = permissions.OrderBy(item => item.TownId, StringComparer.Ordinal)
                .ThenBy(item => item.VisitorId, StringComparer.Ordinal).ToArray()
            };
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent(allowed ? "boat_permission_granted" : "boat_permission_revoked", $"{visitorId}:{townId}");
            return new(true);
        }
        finally { gate.Release(); }
    }

    public BuildingPlacementResult PlacePort(string instanceId, string definitionId, GridPoint position, string builderId)
    {
        gate.Wait();
        try
        {
            var definition = worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == definitionId);
            if (!AdultResident(builderId) || definition is null || !PortNavigationRules.IsPort(definition) ||
                TownForResident(builderId) is not { } townId)
                return BuildingPlacementResult.Rejected(instanceId, definitionId, position, "An adult Town resident must build a Port for their Town.");
            return PlaceBuildingCore(instanceId, definitionId, position, "port_built", townId, constructionOwnerId: HouseholdFor(builderId));
        }
        finally { gate.Release(); }
    }

    private HashSet<GridPoint> WaterObstacles() => map.CampObjects.Select(item => item.Position)
        .Concat(map.Resources.Select(item => item.Position))
        .Concat(RoadAndBridgeTiles().Where(point => !map.IsLand(point)))
        .Concat(Bridges.SelectMany(bridge => bridge.Span))
        .Concat(worldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)))
        .ToHashSet();

    private bool PortIsLegal(PlacedBuilding port)
    {
        var definition = worldContent.Buildings.Single(item => item.CanonicalId == port.DefinitionId);
        var occupied = WaterObstacles();
        foreach (var point in WorldContentSimulationRules.Footprint(definition, port)) occupied.Remove(point);
        return port.TownId is not null && PortNavigationRules.Fits(map, definition, port.Position, occupied, out _);
    }

    public BoatJourneyStartResult StartBoatJourney(string boatId, string passengerId, string destinationPortId)
    {
        gate.Wait();
        try { return StartBoatJourneyCore(boatId, passengerId, destinationPortId); }
        finally { gate.Release(); }
    }

    private BoatJourneyStartResult StartBoatJourneyCore(string boatId, string passengerId, string destinationPortId)
    {
        var boat = boatTransport.Boats.FirstOrDefault(item => item.Id == boatId);
        if (boat is null || boat.Journey is not null || boat.DockedPortId is null)
            return new(false, "This physical boat is already reserved or is away from a Port.");
        if (!AdultResident(passengerId) || !CanUseTownBoat(boat.TownId, passengerId) || PassengerBoat(passengerId) is not null)
            return new(false, "The traveler needs permission for this Town's communal boat.");
        if (livestock.Any(animal => animal.RiderId == passengerId))
            return new(false, "Dismount the horse before boarding this passenger boat.");
        if (PulledCart(passengerId) is not null)
            return new(false, "Park and detach the cart before boarding this passenger boat.");
        if (inhabitants[passengerId].Project is { Stage: not ("completed" or "cancelled") } || inhabitants[passengerId].WaterWork is not null)
            return new(false, "Finish the current work before boarding a boat.");
        var origin = Port(boat.DockedPortId);
        var destination = Port(destinationPortId);
        if (origin is null || destination is null || origin.InstanceId == destination.InstanceId || !PortIsLegal(origin) || !PortIsLegal(destination))
            return new(false, "Travel needs two completed legal Ports.");
        var geometry = PortGeometryFor(origin);
        var person = inhabitants[passengerId];
        if (!geometry.LandTiles.Contains(person.Position) || map.FootDistance(person.Position, boat.Position) != 1)
            return new(false, "Bring the traveler to this boat's land end before boarding.");
        var route = BoatWaterRoute(boat.Position, destination);
        if (route.Count == 0) return new(false, "No connected navigable water route joins these Ports.");
        SetBoat(boat with
        {
            DockedPortId = null,
            Journey = new(passengerId, origin.InstanceId, destination.InstanceId,
            route, 0, WorldTick, checked(WorldTick + BoatStepTicks))
        });
        inhabitants[passengerId] = person with { Position = boat.Position, MoveWaitTicks = 0, TravelCooldownTicks = 0 };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("boat_departed", $"{passengerId}:{boat.Id}:{origin.InstanceId}:{destination.InstanceId}");
        return new(true);
    }

    private void SetBoat(BoatState boat) => boatTransport = boatTransport with
    { Boats = boatTransport.Boats.Where(item => item.Id != boat.Id).Append(boat).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray() };

    private void CompleteBoatConstruction(long targetTick)
    {
        foreach (var job in worldSimulation.ProductionJobs.Where(job => job.State == WorldProductionJobState.Completed &&
            worldContent.Recipes.Any(recipe => recipe.CanonicalId == job.RecipeId && recipe.Tags.Contains("boat", StringComparer.Ordinal)) &&
            !boatTransport.Boats.Any(boat => boat.BuildJobId == job.JobId)).OrderBy(job => job.JobId, StringComparer.Ordinal))
        {
            if (Port(job.BuildingInstanceId) is not { TownId: { } townId } port || !PortIsLegal(port)) continue;
            var berth = Berths(map, PortGeometryFor(port)).Cast<GridPoint?>().FirstOrDefault(point =>
                !boatTransport.Boats.Any(boat => boat.Position == point));
            var output = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == job.JobId + ":output:00" &&
                lot.ItemKind == PortContent.BoatOutputKind && lot.OwnerId == townId && AvailableLotQuantity(lot) == 1);
            if (berth is null || output is null) continue;
            ApplyInventoryTransition(inventory => InventoryFixture.ConsumeReservation(InventoryFixture.Reserve(inventory,
                job.JobId + ":launch", townId, output.Id, 1, "launch_communal_boat", targetTick), job.JobId + ":launch"));
            SetBoat(new("boat-" + job.JobId, townId, job.JobId, berth.Value, port.InstanceId));
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("communal_boat_launched", $"{job.WorkerId}:boat-{job.JobId}:{port.InstanceId}:{townId}");
        }
    }

    private void ProcessBoatTransport(long targetTick)
    {
        CompleteBoatConstruction(targetTick);
        foreach (var boat in boatTransport.Boats.ToArray()) MoveBoatEstateCargo(boat);
        foreach (var original in boatTransport.Boats.Where(boat => boat.Journey is not null).ToArray())
        {
            var boat = original;
            var journey = boat.Journey!;
            if (targetTick < journey.NextMoveTick) continue;
            var targetPortId = journey.Returning ? journey.OriginPortId : journey.DestinationPortId;
            var target = Port(targetPortId);
            var legal = target is not null && PortIsLegal(target);
            if (journey.PathIndex + 1 < journey.WaterPath.Count)
            {
                var next = journey.WaterPath[journey.PathIndex + 1];
                if (!legal || WaterObstacles().Contains(next) || boatTransport.Boats.Any(other => other.Id != boat.Id && other.Position == next))
                {
                    WaitOrReturnBoat(boat, targetTick);
                    continue;
                }
                boat = boat with
                {
                    Position = next,
                    Journey = journey with
                    {
                        PathIndex = journey.PathIndex + 1,
                        NextMoveTick = checked(targetTick + BoatStepTicks),
                        WaitingSinceTick = null
                    }
                };
                SetBoat(boat);
                if (inhabitants.TryGetValue(journey.PassengerId, out var passenger))
                    inhabitants[journey.PassengerId] = passenger with { Position = next };
                MoveBoatEstateCargo(boat);
                if (boat.Journey!.PathIndex + 1 < boat.Journey.WaterPath.Count) continue;
            }
            if (!legal || target is null) { WaitOrReturnBoat(boat, targetTick); continue; }
            var landing = PortGeometryFor(target).LandTiles.Cast<GridPoint?>().FirstOrDefault(point =>
                map.FootDistance(boat.Position, point!.Value) == 1 && !inhabitants.Values.Any(person =>
                    person.InhabitantId != journey.PassengerId && person.Position == point.Value));
            if (landing is null) { WaitOrReturnBoat(boat, targetTick); continue; }
            if (inhabitants.TryGetValue(journey.PassengerId, out var arriving))
                inhabitants[journey.PassengerId] = arriving with { Position = landing.Value, MoveWaitTicks = 0 };
            SetBoat(boat with { Journey = null, DockedPortId = target.InstanceId });
            AppendEvent(journey.Returning ? "boat_returned" : "boat_arrived", $"{journey.PassengerId}:{boat.Id}:{target.InstanceId}");
        }
    }

    private void WaitOrReturnBoat(BoatState boat, long tick)
    {
        var journey = boat.Journey!;
        var since = journey.WaitingSinceTick ?? tick;
        SetBoat(boat with { Journey = journey with { WaitingSinceTick = since, NextMoveTick = checked(tick + BoatStepTicks) } });
        if (journey.WaitingSinceTick is null) AppendEvent("boat_waiting", $"{journey.PassengerId}:{boat.Id}:arrival_blocked");
        if (journey.Returning || tick - since < society.Checkpoint.Config.TicksPerWorldDay || Port(journey.OriginPortId) is not { } origin || !PortIsLegal(origin)) return;
        var geometry = PortGeometryFor(origin);
        var reachableBerths = Berths(map, geometry).Where(berth => geometry.LandTiles.Any(land =>
            map.FootDistance(berth, land) == 1 && !inhabitants.Values.Any(person =>
                person.InhabitantId != journey.PassengerId && person.Position == land)));
        var route = PortNavigationRules.WaterRoute(map, boat.Position, reachableBerths, WaterObstacles());
        if (route.Count == 0) return;
        SetBoat(boat with
        {
            Journey = journey with
            {
                Returning = true,
                WaterPath = route,
                PathIndex = 0,
                WaitingSinceTick = null,
                NextMoveTick = checked(tick + BoatStepTicks)
            }
        });
        AppendEvent("boat_return_started", $"{journey.PassengerId}:{boat.Id}:{origin.InstanceId}");
    }

    private void AddBoatTravelCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState person)
    {
        if (PulledCart(actor) is not null || livestock.Any(animal => animal.RiderId == actor)) return;
        if (!AdultResident(actor) || person.Project is { Stage: not ("completed" or "cancelled") } || person.WaterWork is not null) return;
        foreach (var boat in boatTransport.Boats.Where(boat => boat.Journey is null && boat.DockedPortId is not null && CanUseTownBoat(boat.TownId, actor)))
        {
            if (Port(boat.DockedPortId!) is not { } origin || !PortIsLegal(origin) ||
                FindUnoccupiedRoute(actor, person.Position, PortGeometryFor(origin).WorkPosition, 0).Count == 0) continue;
            foreach (var destination in worldSimulation.Buildings.Where(port => port.InstanceId != origin.InstanceId && Port(port.InstanceId) is not null)
                         .OrderBy(port => port.InstanceId, StringComparer.Ordinal))
                if (PortIsLegal(destination) && BoatWaterRoute(boat.Position, destination).Count > 0)
                    candidates.Add(new(BoatTravelPrefix + boat.Id + "|" + destination.InstanceId,
                        $"Travel in the communal boat to the Port at ({destination.Position.X}, {destination.Position.Y}), carrying your own goods.", 80, destination.InstanceId));
        }
    }

    private void TakeBoatCandidate(string actor, PlaytestInhabitantState person, string selection)
    {
        var pieces = selection.Split('|');
        if (pieces.Length != 2 || boatTransport.Boats.FirstOrDefault(boat => boat.Id == pieces[0]) is not { DockedPortId: { } originId } boat || Port(originId) is not { } origin) return;
        var land = PortGeometryFor(origin).LandTiles.FirstOrDefault(point => map.FootDistance(point, boat.Position) == 1);
        if (person.Position != land) { MoveToward(actor, person, land, "boat_boarding", 0); return; }
        var started = StartBoatJourneyCore(boat.Id, actor, pieces[1]);
        if (!started.Applied) AppendEvent("boat_departure_blocked", $"{actor}:{boat.Id}:{started.Failure}");
    }
}
