using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static bool IsSavedBoatPassenger(BoatTransportState transport, string actor, GridPoint position) =>
        transport.Boats.Any(boat => boat.Journey?.PassengerId == actor && boat.Position == position);

    private static void ValidateBoatTransport(PrivateWorldRuntimeState state)
    {
        var transport = state.BoatTransport;
        if (transport is null || transport.Sequence < 0 || transport.Boats is null || transport.Requests is null ||
            transport.Boats.Any(boat => boat is null) || transport.Requests.Any(request => request is null) ||
            transport.Boats.Select(boat => boat.Id).Distinct(StringComparer.Ordinal).Count() != transport.Boats.Count ||
            transport.Boats.Select(boat => boat.ProjectId).Distinct(StringComparer.Ordinal).Count() != transport.Boats.Count ||
            transport.Boats.Select(boat => boat.Position).Distinct().Count() != transport.Boats.Count ||
            !transport.Boats.Select(boat => boat.Id).SequenceEqual(transport.Boats.Select(boat => boat.Id).Order(StringComparer.Ordinal)) ||
            transport.Requests.Select(request => request.Sequence).Distinct().Count() != transport.Requests.Count ||
            !transport.Requests.Select(request => request.Sequence).SequenceEqual(transport.Requests.Select(request => request.Sequence).Order()) ||
            (transport.Requests.Count == 0 ? 0 : transport.Requests[^1].Sequence) != transport.Sequence)
            throw new InvalidDataException("Saved boat assets and trip queues are missing, duplicated or out of order.");
        var tick = state.Society.Society.WorldTick;
        var people = state.Society.Society.Inhabitants.ToDictionary(person => person.Id, StringComparer.Ordinal);
        var towns = new Dictionary<string, TownRuntimeState>(StringComparer.Ordinal);
        foreach (var town in state.Towns ?? [])
            if (town is null || string.IsNullOrWhiteSpace(town.Id) || !towns.TryAdd(town.Id, town))
                throw new InvalidDataException("Saved boat authority requires unique, known Town identities.");
        foreach (var town in towns.Values)
            foreach (var project in town.Projects.Where(project => project.Stage == "completed" && project.Plan.BoatPortId is not null))
                if (project.RemovedTick is not null || !transport.Boats.Any(boat => boat.Id == project.CompletedBoatId &&
                        boat.ProjectId == project.Id && boat.TownId == town.Id))
                    throw new InvalidDataException("Each completed boat project must retain its one physical communal boat.");
        var passengers = new HashSet<string>(StringComparer.Ordinal);
        var docks = new Dictionary<GridPoint, string>();
        PortGeometry Geometry(string portId)
        {
            var port = state.WorldSimulation?.Buildings.SingleOrDefault(building => building.InstanceId == portId);
            var definition = state.WorldContent?.Buildings.SingleOrDefault(definition => definition.CanonicalId == port?.DefinitionId);
            if (port is null || definition is null || !PortNavigationRules.IsPort(definition))
                throw new InvalidDataException("A boat trip references an unavailable Port.");
            return PortNavigationRules.Geometry(state.Map, definition, port.Position);
        }
        void Claim(GridPoint dock, string boatId)
        {
            if (docks.TryGetValue(dock, out var existing) && existing != boatId)
                throw new InvalidDataException("Two boats cannot occupy or reserve the same docking water.");
            docks[dock] = boatId;
        }
        foreach (var boat in transport.Boats)
        {
            if (!towns.TryGetValue(boat.TownId, out var town) ||
                !PortNavigationRules.NavigableWater(state.Map, boat.Position) ||
                town.Projects.SingleOrDefault(project => project.Id == boat.ProjectId) is not
                { Stage: "completed", Plan.BoatPortId: not null } project ||
                project.CompletedBoatId != boat.Id || project.CompletedBuildingId is not null ||
                boat.Id != TownProjectRules.BuildingId(project.Id))
                throw new InvalidDataException("Every communal boat needs one completed, paid Town boat project.");
            Claim(boat.Position, boat.Id);
            if (boat.Journey is { } journey)
            {
                if (boat.DockedPortId is not null || !passengers.Add(journey.PassengerId) || !people.TryGetValue(journey.PassengerId, out var passenger) ||
                    journey.OriginPortId == journey.DestinationPortId || journey.WaterPath is not { Count: > 0 } ||
                    journey.PathIndex < 0 || journey.PathIndex >= journey.WaterPath.Count ||
                    journey.WaterPath[journey.PathIndex] != boat.Position || journey.StartedTick < 0 || journey.StartedTick > tick ||
                    journey.NextMoveTick < journey.StartedTick ||
                    journey.WaitingSinceTick is { } waiting && (waiting < journey.StartedTick || waiting > tick) ||
                    journey.WaterPath[^1] != journey.ReservedDock ||
                    !Geometry(journey.Returning ? journey.OriginPortId : journey.DestinationPortId).DockingTiles.Contains(journey.ReservedDock) ||
                    journey.WaterPath.Any(point => !PortNavigationRules.NavigableWater(state.Map, point)))
                    throw new InvalidDataException("A boat must retain one passenger, a real destination reservation and a water route.");
                _ = Geometry(journey.OriginPortId);
                _ = Geometry(journey.DestinationPortId);
                for (var index = 1; index < journey.WaterPath.Count; index++)
                    if (state.Map.FootDistance(journey.WaterPath[index - 1], journey.WaterPath[index]) != 1 ||
                        journey.WaterPath[index - 1].X != journey.WaterPath[index].X && journey.WaterPath[index - 1].Y != journey.WaterPath[index].Y)
                        throw new InvalidDataException("A saved boat route must follow connected cardinal water tiles.");
                Claim(journey.ReservedDock, boat.Id);
                if (passenger.Status == SocietyInhabitantStatus.Active &&
                        !state.Inhabitants.Any(person => person.InhabitantId == passenger.Id && person.Position == boat.Position) ||
                    passenger.Status == SocietyInhabitantStatus.Dead &&
                        !(state.DeceasedInhabitants ?? []).Any(person => person.InhabitantId == passenger.Id && person.BoatIdAtDeath == boat.Id) ||
                    (state.Conversations ?? []).Any(conversation => conversation.Status != AgentConversationStatus.Closed &&
                        AgentConversationRules.IsParticipant(conversation, passenger.Id)))
                    throw new InvalidDataException("An underway passenger must stay aboard and out of unfinished conversations.");
                if (transport.Requests.SingleOrDefault(request => request.Id == journey.RequestId) is not { Status: "underway" } request ||
                    request.BoatId != boat.Id || request.PassengerId != journey.PassengerId || request.BoatTownId != boat.TownId ||
                    request.OriginPortId != journey.OriginPortId || request.DestinationPortId != journey.DestinationPortId ||
                    request.RequestedTick > journey.StartedTick)
                    throw new InvalidDataException("The underway boat and trip request must refer to the same reservation.");
            }
            else if (boat.DockedPortId is null || !Geometry(boat.DockedPortId).DockingTiles.Contains(boat.Position))
                throw new InvalidDataException("An idle boat must be moored in a real Port's docking space.");
            if (boat.Journey is { } cargoJourney && people[cargoJourney.PassengerId].Status == SocietyInhabitantStatus.Dead)
            {
                var physicalCargo = state.Society.Society.Inventory.Lots.Where(lot => lot.Quantity > 0 &&
                    lot.ContainerLotId is null && lot.GroundPosition == new InventoryGroundPosition(boat.Position.X, boat.Position.Y))
                    .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
                if (boat.GroundCargoLotIds is null || !physicalCargo.SetEquals(boat.GroundCargoLotIds))
                    throw new InvalidDataException("A deceased boat passenger must retain every physical cargo root until safe landing.");
            }
            if (boat.GroundCargoLotIds is { } cargo)
            {
                if (cargo.Distinct(StringComparer.Ordinal).Count() != cargo.Count || boat.Journey is not { } estateJourney ||
                    !people.TryGetValue(estateJourney.PassengerId, out var deceased) || deceased.Status != SocietyInhabitantStatus.Dead)
                    throw new InvalidDataException("Only a deceased passenger's unique estate cargo may remain aboard.");
                foreach (var id in cargo)
                {
                    var lot = state.Society.Society.Inventory.Lots.SingleOrDefault(lot => lot.Id == id);
                    if (lot is null || lot.Quantity <= 0 || lot.ContainerLotId is not null || lot.GroundPosition != new InventoryGroundPosition(boat.Position.X, boat.Position.Y))
                        throw new InvalidDataException("Aboard estate cargo must remain at its one physical boat location.");
                }
            }
        }
        var queuedPassengers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in transport.Requests)
        {
            if (request.Sequence < 1 || request.Sequence > transport.Sequence ||
                request.Id != "boat-request:" + request.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                !people.ContainsKey(request.PassengerId) || !towns.ContainsKey(request.BoatTownId) ||
                string.IsNullOrWhiteSpace(request.OriginPortId) || string.IsNullOrWhiteSpace(request.DestinationPortId) ||
                request.OriginPortId == request.DestinationPortId || request.RequestedTick < 0 || request.RequestedTick > tick ||
                request.Status is not ("waiting" or "underway" or "arrived" or "returned" or "cancelled") ||
                (request.Status is "arrived" or "returned" or "cancelled") != (request.SettledTick is not null) ||
                request.SettledTick is { } settled && (settled < request.RequestedTick || settled > tick) ||
                request.Status == "waiting" && (request.BoatId is not null || !queuedPassengers.Add(request.PassengerId) || passengers.Contains(request.PassengerId)) ||
                request.Status == "cancelled" && request.BoatId is not null ||
                request.Status == "underway" && !transport.Boats.Any(boat => boat.Id == request.BoatId && boat.Journey?.RequestId == request.Id) ||
                request.Status is "arrived" or "returned" && !transport.Boats.Any(boat => boat.Id == request.BoatId))
                throw new InvalidDataException("The saved boat queue contains a missing traveler, duplicate active request or invalid outcome.");
        }
        foreach (var person in state.DeceasedInhabitants ?? [])
            if (person.BoatIdAtDeath is { } id && !transport.Boats.Any(boat => boat.Id == id))
                throw new InvalidDataException("A boat death must retain its actual communal asset.");
    }
}
