using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int BoatStepTicks = 3; // Provisional travel speed.
    private const string BoatTripPrefix = "boat_trip:";
    private sealed record BoatTripChoice(string Id, string TownId, PlacedBuilding Origin, PlacedBuilding Destination);

    private BoatState? PassengerBoat(string actor) => boatTransport.Boats.FirstOrDefault(boat => boat.Journey?.PassengerId == actor);
    private BoatTripRequest? WaitingBoatRequest(string actor) => boatTransport.Requests.FirstOrDefault(request =>
        request.PassengerId == actor && request.Status == "waiting");

    private bool ReadyToBoard(string actor) => AdultResident(actor) && PassengerBoat(actor) is null &&
        ConversationFor(actor) is null && AttachedHandcart(actor) is null && !HasGuardianPlacementTask(actor) &&
        !inhabitants.Values.Any(person => person.GuardianPlacement?.CaregiverId == actor) && MovingCareGroup(actor).Count == 1 &&
        inhabitants[actor].Project is not { Stage: not ("completed" or "cancelled") } &&
        inhabitants[actor].Equipment?.Repair is null && inhabitants[actor].MedicalTreatment is null &&
        !fields.Any(field => field.Work?.WorkerId == actor) &&
        !IsActiveTownProjectDeliveryForActor(actor);

    private bool IsActiveTownProjectDeliveryForActor(string actor) => society.Checkpoint.Inventory.Lots.Any(lot =>
        lot.CarrierId == actor && IsActiveTownProjectDelivery(lot.Id));

    private bool CanUseTownBoat(string townId, string actor) => towns.SingleOrDefault(town => town.Id == townId) is { } town &&
        (town.ResidentIds.Contains(actor, StringComparer.Ordinal) || TownBoatAccessRules.Allows(town, actor, WorldTick));

    private GridPoint? AvailablePortLanding(PlacedBuilding port, string passenger) =>
        PortGeometryFor(port).LandTiles.Where(point => !inhabitants.Values.Any(person =>
            person.InhabitantId != passenger && person.Position == point)).Cast<GridPoint?>().FirstOrDefault();

    private IReadOnlyList<GridPoint> BoatWaterRoute(GridPoint origin, GridPoint dock, string? boatId = null) =>
        PortNavigationRules.WaterRoute(map, origin, [dock], PortObstacles()
            .Concat(boatTransport.Boats.Where(boat => boat.Id != boatId).SelectMany(boat => boat.Journey is { } journey ? new[] { boat.Position, journey.ReservedDock } : new[] { boat.Position })).ToHashSet());

    private IEnumerable<BoatTripChoice> BoatTripChoices(string actor)
    {
        if (!ReadyToBoard(actor) || WaitingBoatRequest(actor) is not null) yield break;
        foreach (var origin in worldSimulation.Buildings.Where(port => Port(port.InstanceId) is not null && PortIsLegal(port))
                     .OrderBy(port => port.InstanceId, StringComparer.Ordinal))
        {
            var approach = origin.Entrance!.Value;
            if (!CanWalkForTownProject(actor, inhabitants[actor].Position, approach)) continue;
            foreach (var townId in boatTransport.Boats.Where(boat => boat.DockedPortId == origin.InstanceId)
                         .Select(boat => boat.TownId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (!CanUseTownBoat(townId, actor)) continue;
                foreach (var destination in worldSimulation.Buildings.Where(port => port.InstanceId != origin.InstanceId &&
                             Port(port.InstanceId) is not null && PortIsLegal(port)).OrderBy(port => port.InstanceId, StringComparer.Ordinal))
                {
                    // Geography is enough to request a trip; a full dock does not reserve a boat.
                    if (!boatTransport.Boats.Where(boat => boat.TownId == townId && boat.DockedPortId == origin.InstanceId && boat.Journey is null)
                            .Any(boat => PortNavigationRules.WaterRoute(map, boat.Position,
                                PortGeometryFor(destination).DockingTiles, PortObstacles()).Count > 0)) continue;
                    var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                        new[] { townId, origin.InstanceId, destination.InstanceId }))));
                    yield return new(BoatTripPrefix + key, townId, origin, destination);
                }
            }
        }
    }

    private void AddBoatCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (WaitingBoatRequest(actor) is { } waiting)
        {
            candidates.Add(new("boat_cancel:" + waiting.Id, "Cancel your waiting boat trip and release unused reservations.", 90));
            if (Port(waiting.OriginPortId) is { } origin && inhabitants[actor].Position != origin.Entrance)
                candidates.Add(new("boat_queue:" + waiting.Id, "Return to the Port's land approach to wait for your boat trip.", 80));
            return;
        }
        foreach (var choice in BoatTripChoices(actor))
            candidates.Add(new(choice.Id,
                FormattableString.Invariant($"Request a communal boat trip to the Port at ({choice.Destination.Position.X}, {choice.Destination.Position.Y}), carrying your goods; wait at the departure Port's land approach."),
                80, choice.Destination.InstanceId));
    }

    private bool ApplyBoatCandidate(string actor, string candidateId)
    {
        if (candidateId.StartsWith("boat_cancel:", StringComparison.Ordinal))
        {
            if (WaitingBoatRequest(actor) is { } request && candidateId == "boat_cancel:" + request.Id)
                SetBoatRequest(request with { Status = "cancelled", SettledTick = WorldTick });
            return true;
        }
        if (candidateId.StartsWith("boat_queue:", StringComparison.Ordinal))
        {
            if (WaitingBoatRequest(actor) is { } request && candidateId == "boat_queue:" + request.Id &&
                Port(request.OriginPortId) is { } origin)
                MoveToward(actor, inhabitants[actor], origin.Entrance!.Value, "boat_queue", 0);
            return true;
        }
        if (!candidateId.StartsWith(BoatTripPrefix, StringComparison.Ordinal)) return false;
        var choice = BoatTripChoices(actor).FirstOrDefault(choice => choice.Id == candidateId);
        if (choice is null) return true;
        if (inhabitants[actor].Position != choice.Origin.Entrance)
        {
            MoveToward(actor, inhabitants[actor], choice.Origin.Entrance!.Value, "boat_departure", 0);
            return true;
        }
        var sequence = checked(boatTransport.Sequence + 1);
        boatTransport = boatTransport with { Sequence = sequence };
        var queuedRequest = new BoatTripRequest("boat-request:" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequence, actor, choice.TownId, choice.Origin.InstanceId, choice.Destination.InstanceId, WorldTick);
        SetBoatRequest(queuedRequest);
        AppendEvent("boat_trip_requested", $"{actor}:{queuedRequest.Id}:{choice.Origin.InstanceId}:{choice.Destination.InstanceId}");
        return true;
    }

    private void SetBoatRequest(BoatTripRequest request) => boatTransport = boatTransport with
    {
        Requests = boatTransport.Requests.Where(item => item.Id != request.Id).Append(request)
            .OrderBy(item => item.Sequence).ToArray(),
    };

    private void ProcessBoatQueue()
    {
        foreach (var request in boatTransport.Requests.Where(request => request.Status == "waiting").OrderBy(request => request.Sequence).ToArray())
        {
            var origin = Port(request.OriginPortId);
            var destination = Port(request.DestinationPortId);
            if (!inhabitants.ContainsKey(request.PassengerId) || !CanUseTownBoat(request.BoatTownId, request.PassengerId) || origin is null || destination is null)
            {
                SetBoatRequest(request with { Status = "cancelled", SettledTick = WorldTick });
                AppendEvent("boat_trip_cancelled", $"{request.PassengerId}:{request.Id}:departure_unavailable");
                continue;
            }
            if (!ReadyToBoard(request.PassengerId) || inhabitants[request.PassengerId].Position != origin.Entrance ||
                !PortIsLegal(origin) || !PortIsLegal(destination) || AvailablePortLanding(destination, request.PassengerId) is null) continue;
            foreach (var boat in boatTransport.Boats.Where(boat => boat.TownId == request.BoatTownId &&
                         boat.DockedPortId == origin.InstanceId && boat.Journey is null).OrderBy(boat => boat.Id, StringComparer.Ordinal))
            {
                var route = FreePortDocks(destination).Select(dock => BoatWaterRoute(boat.Position, dock, boat.Id))
                    .FirstOrDefault(path => path.Count > 0);
                if (route is null) continue;
                var dock = route[^1];
                SetBoat(boat with
                {
                    DockedPortId = null,
                    Journey = new(request.Id, request.PassengerId, origin.InstanceId, destination.InstanceId, dock,
                        route, 0, WorldTick, checked(WorldTick + BoatStepTicks)),
                });
                SetBoatRequest(request with { Status = "underway", BoatId = boat.Id });
                inhabitants[request.PassengerId] = inhabitants[request.PassengerId] with
                { Position = boat.Position, MoveWaitTicks = 0, TravelCooldownTicks = 0 };
                AppendEvent("boat_departed", $"{request.PassengerId}:{boat.Id}:{origin.InstanceId}:{destination.InstanceId}", boat.Position);
                break;
            }
        }
    }

    private void ProcessBoatTransport(long tick)
    {
        foreach (var original in boatTransport.Boats.Where(boat => boat.Journey is not null).ToArray())
        {
            var boat = MoveBoatGroundCargo(original);
            var journey = boat.Journey!;
            if (tick < journey.NextMoveTick) continue;
            var target = Port(journey.Returning ? journey.OriginPortId : journey.DestinationPortId);
            if (target is null || !PortIsLegal(target)) { WaitOrRecoverBoat(boat, tick); continue; }
            if (journey.PathIndex + 1 < journey.WaterPath.Count)
            {
                var next = journey.WaterPath[journey.PathIndex + 1];
                if (PortObstacles().Contains(next) || boatTransport.Boats.Any(other => other.Id != boat.Id && (other.Position == next || other.Journey?.ReservedDock == next)))
                {
                    var revised = BoatWaterRoute(boat.Position, journey.ReservedDock, boat.Id);
                    if (revised.Count == 0) { WaitOrRecoverBoat(boat, tick); continue; }
                    journey = journey with { WaterPath = revised, PathIndex = 0 };
                    next = revised.Count > 1 ? revised[1] : boat.Position;
                }
                boat = boat with
                {
                    Position = next,
                    Journey = journey with
                    {
                        PathIndex = Math.Min(journey.PathIndex + 1, journey.WaterPath.Count - 1),
                        NextMoveTick = checked(tick + BoatStepTicks),
                        WaitingSinceTick = null,
                    }
                };
                SetBoat(boat);
                if (inhabitants.TryGetValue(journey.PassengerId, out var passenger))
                    inhabitants[journey.PassengerId] = passenger with { Position = next };
                boat = MoveBoatGroundCargo(boat);
                journey = boat.Journey!;
                if (journey.PathIndex + 1 < journey.WaterPath.Count) continue;
            }
            var landing = AvailablePortLanding(target, journey.PassengerId);
            if (landing is null) { WaitOrRecoverBoat(boat, tick); continue; }
            if (inhabitants.TryGetValue(journey.PassengerId, out var arriving))
                inhabitants[journey.PassengerId] = arriving with { Position = landing.Value, MoveWaitTicks = 0, TravelCooldownTicks = 0 };
            MoveBoatGroundCargo(boat, landing.Value);
            SetBoat(boat with { DockedPortId = target.InstanceId, Journey = null, GroundCargoLotIds = null });
            var request = boatTransport.Requests.Single(request => request.Id == journey.RequestId);
            SetBoatRequest(request with { Status = journey.Returning ? "returned" : "arrived", SettledTick = tick });
            AppendEvent(journey.Returning ? "boat_returned" : "boat_arrived", $"{journey.PassengerId}:{boat.Id}:{target.InstanceId}", landing.Value);
        }
        ProcessBoatQueue();
    }

    private void WaitOrRecoverBoat(BoatState boat, long tick)
    {
        var journey = boat.Journey!;
        var since = journey.WaitingSinceTick ?? tick;
        SetBoat(boat with { Journey = journey with { WaitingSinceTick = since, NextMoveTick = checked(tick + BoatStepTicks) } });
        if (journey.WaitingSinceTick is null) AppendEvent("boat_waiting", $"{journey.PassengerId}:{boat.Id}:arrival_blocked", boat.Position);
        if (!journey.Returning && tick - since < CivicDay) return;
        var recovery = Port(journey.Returning ? journey.DestinationPortId : journey.OriginPortId);
        if (recovery is null || !PortIsLegal(recovery) || AvailablePortLanding(recovery, journey.PassengerId) is null) return;
        var route = FreePortDocks(recovery, boat.Id).Select(dock => BoatWaterRoute(boat.Position, dock, boat.Id))
            .FirstOrDefault(path => path.Count > 0);
        if (route is null) return;
        var dock = route[^1];
        SetBoat(boat with
        {
            Journey = journey with
            {
                Returning = !journey.Returning,
                ReservedDock = dock,
                WaterPath = route,
                PathIndex = 0,
                WaitingSinceTick = null,
                NextMoveTick = checked(tick + BoatStepTicks),
            }
        });
        AppendEvent("boat_recovery_started", $"{journey.PassengerId}:{boat.Id}:{recovery.InstanceId}", boat.Position);
    }

    private string? RecordBoatDeath(string actor)
    {
        if (PassengerBoat(actor) is not { } boat) return null;
        var ids = society.Checkpoint.Inventory.Lots.Where(lot => lot.ContainerLotId is null && lot.Quantity > 0 &&
            lot.GroundPosition == new InventoryGroundPosition(boat.Position.X, boat.Position.Y))
            .Select(lot => lot.Id).Order(StringComparer.Ordinal).ToArray();
        SetBoat(boat with { GroundCargoLotIds = ids });
        return boat.Id;
    }

    private BoatState MoveBoatGroundCargo(BoatState boat, GridPoint? destination = null)
    {
        if (boat.GroundCargoLotIds is not { Count: > 0 } roots) return boat;
        var remaining = society.Checkpoint.Inventory.Lots.Where(lot => lot.ContainerLotId is null && lot.Quantity > 0 &&
            (roots.Contains(lot.Id, StringComparer.Ordinal) || lot.ProvenanceLotId is { } source && roots.Contains(source, StringComparer.Ordinal)) &&
            lot.GroundPosition is not null).Select(lot => lot.Id).Order(StringComparer.Ordinal).ToArray();
        var position = destination ?? boat.Position;
        ApplyInventoryTransition(inventory => inventory with
        {
            Lots = inventory.Lots.Select(lot => remaining.Contains(lot.Id, StringComparer.Ordinal)
                ? lot with { GroundPosition = new(position.X, position.Y) } : lot).ToArray(),
        });
        boat = boat with { GroundCargoLotIds = remaining };
        SetBoat(boat);
        return boat;
    }
}
