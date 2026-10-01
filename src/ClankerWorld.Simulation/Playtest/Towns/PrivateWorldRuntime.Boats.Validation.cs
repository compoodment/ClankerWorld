using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static bool IsSavedBoatPassenger(BoatTransportState? transport, string actor, GridPoint position) =>
        transport?.Boats.Any(boat => boat.Journey?.PassengerId == actor && boat.Position == position) == true;

    private static void ValidateBoatTransport(PrivateWorldRuntimeState state)
    {
        if (state.BoatTransport is not { } transport) return;
        if (state.SchemaVersion < 36 || transport.SchemaVersion != 1 || transport.Boats is null || transport.GuestPermissions is null ||
            transport.Boats.Any(boat => boat is null) || transport.GuestPermissions.Any(permission => permission is null) ||
            transport.Boats.Select(boat => boat.Id).Distinct(StringComparer.Ordinal).Count() != transport.Boats.Count ||
            transport.Boats.Select(boat => boat.BuildJobId).Distinct(StringComparer.Ordinal).Count() != transport.Boats.Count ||
            transport.Boats.Select(boat => boat.Position).Distinct().Count() != transport.Boats.Count ||
            !transport.Boats.Select(boat => boat.Id).SequenceEqual(transport.Boats.Select(boat => boat.Id).Order(StringComparer.Ordinal)) ||
            transport.GuestPermissions.Distinct().Count() != transport.GuestPermissions.Count)
            throw new InvalidDataException("Saved communal boats and permissions are malformed or duplicated.");
        var people = state.Society.Society.Inhabitants.ToDictionary(person => person.Id, StringComparer.Ordinal);
        var towns = (state.Towns ?? []).Select(town => town.Id).ToHashSet(StringComparer.Ordinal);
        if (transport.GuestPermissions.Any(permission => !towns.Contains(permission.TownId) || !people.ContainsKey(permission.VisitorId)))
            throw new InvalidDataException("Boat guest permissions reference an unknown Town or agent.");
        var passengers = new HashSet<string>(StringComparer.Ordinal);
        var cargo = new HashSet<string>(StringComparer.Ordinal);
        foreach (var boat in transport.Boats)
        {
            var job = state.WorldSimulation?.ProductionJobs.FirstOrDefault(item => item.JobId == boat.BuildJobId);
            var recipe = state.WorldContent?.Recipes.FirstOrDefault(item => item.CanonicalId == job?.RecipeId);
            var launch = state.Society.Society.Inventory.Reservations.FirstOrDefault(item => item.Id == boat.BuildJobId + ":launch");
            var buildPort = state.WorldSimulation?.Buildings.FirstOrDefault(item => item.InstanceId == job?.BuildingInstanceId);
            var portDefinition = state.WorldContent?.Buildings.FirstOrDefault(item => item.CanonicalId == buildPort?.DefinitionId);
            if (!towns.Contains(boat.TownId) || !PortNavigationRules.NavigableWater(state.Map, boat.Position) ||
                boat.Id != "boat-" + boat.BuildJobId || job?.State != WorldProductionJobState.Completed ||
                recipe?.Tags.Contains("boat", StringComparer.Ordinal) != true || buildPort?.TownId != boat.TownId ||
                portDefinition is null || !PortNavigationRules.IsPort(portDefinition) || job.InputReservationIds is null ||
                job.InputReservationIds.Count == 0 || job.InputReservationIds.Any(id =>
                    state.Society.Society.Inventory.Reservations.FirstOrDefault(item => item.Id == id) is not { } input ||
                    input.OwnerId != boat.TownId || input.State != InventoryReservationState.Completed) ||
                recipe.WorkstationBuildingId != buildPort.DefinitionId || launch?.State != InventoryReservationState.Completed ||
                launch.OwnerId != boat.TownId || launch.Quantity != 1 || launch.Purpose != "launch_communal_boat" ||
                launch.LotId != boat.BuildJobId + ":output:00")
                throw new InvalidDataException("Each physical Town boat must come from one completed, consumed Port build job.");
            if (boat.Journey is { } journey)
            {
                if (boat.DockedPortId is not null || !passengers.Add(journey.PassengerId) || !people.TryGetValue(journey.PassengerId, out var person) ||
                    journey.WaterPath is null || journey.WaterPath.Count == 0 || journey.PathIndex < 0 || journey.PathIndex >= journey.WaterPath.Count ||
                    journey.WaterPath[journey.PathIndex] != boat.Position || journey.StartedTick < 0 || journey.StartedTick > state.Society.Society.WorldTick ||
                    journey.NextMoveTick < journey.StartedTick || journey.WaitingSinceTick is { } waiting && (waiting < journey.StartedTick || waiting > state.Society.Society.WorldTick) ||
                    string.IsNullOrWhiteSpace(journey.OriginPortId) || string.IsNullOrWhiteSpace(journey.DestinationPortId) || journey.OriginPortId == journey.DestinationPortId ||
                    journey.WaterPath.Any(point => !PortNavigationRules.NavigableWater(state.Map, point)))
                    throw new InvalidDataException("A boat must retain one real passenger and a valid saved water journey.");
                for (var index = 1; index < journey.WaterPath.Count; index++)
                    if (state.Map.FootDistance(journey.WaterPath[index - 1], journey.WaterPath[index]) != 1 ||
                        journey.WaterPath[index - 1].X != journey.WaterPath[index].X && journey.WaterPath[index - 1].Y != journey.WaterPath[index].Y)
                        throw new InvalidDataException("Saved boat route steps must follow connected cardinal water tiles.");
                var origin = state.WorldSimulation?.Buildings.FirstOrDefault(item => item.InstanceId == journey.OriginPortId);
                var destination = state.WorldSimulation?.Buildings.FirstOrDefault(item => item.InstanceId == journey.DestinationPortId);
                var originDefinition = state.WorldContent?.Buildings.FirstOrDefault(item => item.CanonicalId == origin?.DefinitionId);
                var destinationDefinition = state.WorldContent?.Buildings.FirstOrDefault(item => item.CanonicalId == destination?.DefinitionId);
                if (origin is null || destination is null || originDefinition is null || destinationDefinition is null ||
                    !PortNavigationRules.IsPort(originDefinition) || !PortNavigationRules.IsPort(destinationDefinition) ||
                    !Berths(state.Map, PortNavigationRules.Geometry(state.Map,
                        journey.Returning ? originDefinition : destinationDefinition,
                        journey.Returning ? origin.Position : destination.Position)).Contains(journey.WaterPath[^1]))
                    throw new InvalidDataException("A saved boat journey must end at its real arrival Port's berth.");
                if (person.Status == SocietyInhabitantStatus.Active &&
                    !state.Inhabitants.Any(item => item.InhabitantId == person.Id && item.Position == boat.Position) ||
                    person.Status == SocietyInhabitantStatus.Dead &&
                    !(state.DeceasedInhabitants ?? []).Any(item => item.InhabitantId == person.Id && item.BoatIdAtDeath == boat.Id))
                    throw new InvalidDataException("The passenger and physical boat positions disagree.");
            }
            else
            {
                var port = state.WorldSimulation?.Buildings.FirstOrDefault(item => item.InstanceId == boat.DockedPortId);
                var definition = state.WorldContent?.Buildings.FirstOrDefault(item => item.CanonicalId == port?.DefinitionId);
                if (port is null || definition is null || !PortNavigationRules.IsPort(definition) ||
                    !Berths(state.Map, PortNavigationRules.Geometry(state.Map, definition, port.Position)).Contains(boat.Position))
                    throw new InvalidDataException("An idle boat must remain at a completed Port's actual berth.");
            }
            foreach (var id in boat.EstateCargoLotIds ?? [])
            {
                var lot = state.Society.Society.Inventory.Lots.FirstOrDefault(item => item.Id == id);
                if (!cargo.Add(id) || lot is null || lot.ContainerLotId is not null || !IsBoatEstateCargo(transport, lot))
                    throw new InvalidDataException("Aboard estate stock must remain at one physical boat location.");
            }
        }
    }
}
