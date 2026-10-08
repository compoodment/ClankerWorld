using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private BoatTripRequest? BoundBoatRequest(OwnerQueuedInstruction instruction) =>
        instruction.Order?.BoatTravel?.RequestId is { } id
            ? boatTransport.Requests.FirstOrDefault(request => request.Id == id)
            : null;

    private CognitionCandidate? BoatOrderCandidate(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        if (instruction.Order?.BoatTravel is not { } binding || Port(binding.DestinationPortId) is not { } destination ||
            !PortIsLegal(destination) || PassengerBoat(actor) is not null) return null;
        var waiting = WaitingBoatRequest(actor);
        if (waiting is not null && waiting.Id == binding.RequestId)
        {
            if (Port(waiting.OriginPortId) is not { } origin || inhabitants[actor].Position == origin.Entrance ||
                !CanWalkForTownProject(actor, inhabitants[actor].Position, origin.Entrance!.Value)) return null;
        }
        else if (!ReadyToBoard(actor) || waiting is null &&
                 !BoatTripChoices(actor).Any(choice => choice.Destination.InstanceId == binding.DestinationPortId)) return null;
        return new("boat_order", "Travel to the named Port using a real communal boat, its normal queue and safe landing rules.",
            0, binding.DestinationPortId);
    }

    private void ExecuteBoatOrder(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        var binding = instruction.Order!.BoatTravel!;
        if (WaitingBoatRequest(actor) is { } waiting)
        {
            if (waiting.Id == binding.RequestId)
            {
                _ = ApplyBoatCandidate(actor, "boat_queue:" + waiting.Id);
                return;
            }
            // An owner order replaces an ordinary waiting plan, never an underway journey.
            _ = ApplyBoatCandidate(actor, "boat_cancel:" + waiting.Id);
        }
        var choice = BoatTripChoices(actor).FirstOrDefault(choice => choice.Destination.InstanceId == binding.DestinationPortId);
        if (choice is null)
        {
            SetOrderStatus(instruction, "blocked", BoatOrderBlockedReason(instruction));
            return;
        }
        if (inhabitants[actor].Position != choice.Origin.Entrance)
        {
            MoveToward(actor, inhabitants[actor], choice.Origin.Entrance!.Value, "boat_departure", 0);
            return;
        }
        var request = QueueBoatTrip(actor, choice, instruction.InstructionId);
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        instructionsByIdempotency[current.IdempotencyKey] = current with
        { Order = current.Order! with { BoatTravel = binding with { RequestId = request.Id } } };
    }

    private string BoatOrderBlockedReason(OwnerQueuedInstruction instruction)
    {
        var actor = instruction.TargetInhabitantId;
        var binding = instruction.Order!.BoatTravel!;
        if (PassengerBoat(actor) is not null) return "The passenger must finish their current boat journey before starting another trip.";
        if (Port(binding.DestinationPortId) is not { } destination) return "The requested Port is no longer available.";
        if (!PortIsLegal(destination)) return "The destination Port or its land approach is blocked.";
        if (BoundBoatRequest(instruction) is { Status: "waiting" } request)
        {
            if (!CanUseTownBoat(request.BoatTownId, actor)) return "Waiting for permission to use the departure Town's boat.";
            if (Port(request.OriginPortId) is not { } origin || !PortIsLegal(origin)) return "The departure Port or its land approach is blocked.";
            if (inhabitants[actor].Position != origin.Entrance) return "Waiting for an open walking route to the departure Port's land approach.";
            if (!ReadyToBoard(actor)) return "Only an unaccompanied adult with no cart, animal or unfinished work can board.";
            if (AvailablePortLanding(destination, actor) is null) return "Waiting for a free landing tile at the destination Port.";
            if (!FreePortDocks(destination).Any()) return "Waiting for dock space at the destination Port.";
            var boats = boatTransport.Boats.Where(boat => boat.TownId == request.BoatTownId &&
                boat.DockedPortId == origin.InstanceId && boat.Journey is null).ToArray();
            if (boats.Length == 0) return "Waiting for a communal boat at the departure Port.";
            return "Waiting for an open water route and the next usable place in the boat queue.";
        }
        if (!ReadyToBoard(actor)) return "Only an unaccompanied adult with no cart, animal or unfinished work can board.";
        var departures = worldSimulation.Buildings.Where(port => port.InstanceId != destination.InstanceId &&
            Port(port.InstanceId) is not null && PortIsLegal(port)).ToArray();
        var reachable = departures.Where(port => CanWalkForTownProject(actor, inhabitants[actor].Position, port.Entrance!.Value)).ToArray();
        if (reachable.Length == 0) return "Waiting for an open walking route to a usable departure Port.";
        var available = boatTransport.Boats.Where(boat => boat.Journey is null &&
            reachable.Any(port => port.InstanceId == boat.DockedPortId)).ToArray();
        if (available.Length == 0) return "Waiting for a communal boat at a reachable departure Port.";
        if (!available.Any(boat => CanUseTownBoat(boat.TownId, actor))) return "Waiting for permission to use the departure Town's boat.";
        return "Waiting for an open water route to the requested Port.";
    }

    private void CancelBoatTravelForOrder(OwnerQueuedInstruction instruction)
    {
        if (BoundBoatRequest(instruction) is { Status: "waiting" } request)
        {
            SetBoatRequest(request with { Status = "cancelled", SettledTick = WorldTick });
            AppendEvent("boat_trip_cancelled", $"{request.PassengerId}:{request.Id}:order_cancelled");
        }
    }

    private OwnerQueuedInstruction? ActiveBoatOrder(BoatTripRequest request) =>
        request.OrderInstructionId is { } id
            ? instructionsByIdempotency.Values.FirstOrDefault(instruction => instruction.InstructionId == id &&
                instruction.Order is { Action: "travel_by_boat", BoatTravel: { } binding } order &&
                IsActiveOrder(order.Status) && binding.RequestId == request.Id)
            : null;

    private void SetBoatOrderTravelStatus(BoatTripRequest request, string status, string? reason)
    {
        if (ActiveBoatOrder(request) is { } instruction) SetOrderStatus(instruction, status, reason);
    }

    private void CreditBoatOrderArrival(BoatTripRequest request)
    {
        if (ActiveBoatOrder(request) is { } instruction && instruction.Order!.BoatTravel!.DestinationPortId == request.DestinationPortId)
            CreditOrderEffect(instruction, BoatOrderArrivalEffectId(request.Id), 1);
    }

    private static string BoatOrderArrivalEffectId(string requestId) => "boat-arrival:" + requestId;

    private static bool IsValidBoatOrderShape(OwnerInstructionOrder order) =>
        order.BoatTravel is { DestinationPortId.Length: > 0 and <= 128 } binding &&
        !binding.DestinationPortId.Any(char.IsControl) &&
        (binding.RequestId is null || binding.RequestId.Length is > 0 and <= 128 && !binding.RequestId.Any(char.IsControl)) &&
        order.TargetPosition is not null && order.TargetResourceId is null && order.TargetAgentId is null && order.TargetFoodKind is null &&
        order.RequestedUnits == 1 && order.CompletedUnits is 0 or 1 && order.ProgressUnit == "arrivals" &&
        !order.RepeatUntilCancelled && !order.QuantityIsExplicit && order.Status != "not_understood" &&
        (order.Status == "finished") == (order.CompletedUnits == 1) &&
        (order.CompletedUnits == 0 || binding.RequestId is not null) &&
        order.LastEffectId == (order.CompletedUnits == 1 && binding.RequestId is not null ? BoatOrderArrivalEffectId(binding.RequestId) : null);

    private static void ValidateBoatOrderBindings(PrivateWorldRuntimeState state)
    {
        if (!(state.Instructions ?? []).Any(instruction => instruction.Order?.Action == "travel_by_boat") &&
            !state.BoatTransport.Requests.Any(request => request.OrderInstructionId is not null)) return;
        var instructions = (state.Instructions ?? []).ToDictionary(instruction => instruction.InstructionId, StringComparer.Ordinal);
        var requests = state.BoatTransport.Requests.ToDictionary(request => request.Id, StringComparer.Ordinal);
        var requestsByInstruction = state.BoatTransport.Requests.Where(request => request.OrderInstructionId is not null)
            .ToLookup(request => request.OrderInstructionId!, StringComparer.Ordinal);
        foreach (var request in requests.Values.Where(request => request.OrderInstructionId is not null))
        {
            if (!instructions.TryGetValue(request.OrderInstructionId!, out var instruction) ||
                instruction.Order is not { Action: "travel_by_boat", BoatTravel: { } binding } ||
                instruction.TargetInhabitantId != request.PassengerId || binding.DestinationPortId != request.DestinationPortId ||
                request.RequestedTick < instruction.SubmittedTick)
                throw new InvalidDataException("An ordered boat request must retain its exact instruction, passenger and destination.");
        }
        foreach (var instruction in instructions.Values.Where(instruction => instruction.Order?.Action == "travel_by_boat"))
        {
            var order = instruction.Order!;
            if (!IsValidBoatOrderShape(order)) throw new InvalidDataException("A boat order must retain one stable destination and real arrival progress.");
            var binding = order.BoatTravel!;
            var port = state.WorldSimulation?.Buildings.FirstOrDefault(port => port.InstanceId == binding.DestinationPortId);
            if (port is null && !(state.Towns ?? []).SelectMany(town => town.Projects).Any(project =>
                    project.CompletedBuildingId == binding.DestinationPortId && project.RemovedTick is not null &&
                    project.Plan.Site == order.TargetPosition && state.WorldContent!.Buildings.Any(definition =>
                        definition.CanonicalId == project.Plan.DefinitionId && PortNavigationRules.IsPort(definition))) ||
                port is not null && (port.Position != order.TargetPosition || !state.WorldContent!.Buildings.Any(definition =>
                    definition.CanonicalId == port.DefinitionId && PortNavigationRules.IsPort(definition))))
                throw new InvalidDataException("A boat order's named Port must match its saved destination tile.");
            var owned = requestsByInstruction[instruction.InstructionId].ToArray();
            var latest = owned.LastOrDefault();
            if (binding.RequestId != latest?.Id || latest is not null &&
                (order.Status == "finished" && latest.Status != "arrived" ||
                 IsActiveOrder(order.Status) && latest.Status == "arrived" ||
                 order.Status == "cancelled" && latest.Status == "waiting"))
                throw new InvalidDataException("A boat order must bind its latest real request; only destination arrival completes it.");
            for (var index = 1; index < owned.Length; index++)
                if (owned[index - 1].SettledTick is not { } settled || owned[index].RequestedTick < settled)
                    throw new InvalidDataException("A boat order cannot start another request while its earlier journey is active.");
        }
    }
}
