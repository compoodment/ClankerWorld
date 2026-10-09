using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static bool IsShelterOrder(string action) => action is "seek_shelter" or "tend_fire";

    private bool ShelterBuildingKnown(string actor, PlaytestInhabitantState person, PlacedBuilding building) =>
        building.HouseholdId is { } owner &&
            (owner == society.Checkpoint.GetInhabitant(actor).HouseholdId || HasHouseGuestInvitation(actor, building.InstanceId)) ||
        IsTownHallStormRefuge(actor, building) ||
        IsWithinInteractionRange(person.Position, building.Position, ResourceInteractionRange);

    private bool ShelterBuildingCovers(PlacedBuilding building, GridPoint point) =>
        building.HouseholdId is null && !IsTownHall(building)
            ? IsWithinInteractionRange(point, building.Position, ResourceInteractionRange)
            : WorldContentSimulationRules.Footprint(worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building).Contains(point);

    private bool ShelterRouteIsOpen(string actor, GridPoint origin, GridPoint destination) =>
        origin == destination || FindUnoccupiedRoute(actor, origin, destination, 0).Count > 0;

    private PlacedBuilding? BoundShelterBuilding(string actor, OwnerInstructionOrder order)
    {
        if (order.ShelterBinding is not { Kind: "building" } binding) return null;
        var available = order.Action == "tend_fire" ? AccessibleHeatingBuildings(actor) : AccessibleShelters(actor);
        return available.FirstOrDefault(building => building.InstanceId == binding.BuildingInstanceId &&
            building.DefinitionId == binding.DefinitionId && building.HouseholdId == binding.OwnerId &&
            building.Position == binding.BuildingPosition && building.PlacedTick == binding.BuildingPlacedTick &&
            (order.TargetBuildingKind != "house" || building.HouseholdId == society.Checkpoint.GetInhabitant(actor).HouseholdId &&
                worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house", StringComparer.Ordinal)));
    }

    private bool ShelterBindingIsAvailable(string actor, OwnerInstructionOrder order)
    {
        if (order.ShelterBinding is not { } binding) return false;
        if (binding.Kind == "natural") return order.Action == "seek_shelter" &&
            WeatherAt(binding.Position) == WeatherKind.Storm && NaturalStormCover(binding.Position);
        if (BoundShelterBuilding(actor, order) is not { } building) return false;
        return order.Action == "tend_fire"
            ? !IsFireLit(building) && IsWithinInteractionRange(binding.Position, building.Position,
                building.HouseholdId is null ? ResourceInteractionRange : 0)
            : ShelterBuildingCovers(building, binding.Position);
    }

    private OwnerShelterBinding? SelectShelterBinding(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var order = instruction.Order!;
        if (order.ShelterBinding is { } bound) return bound;
        var buildings = (order.Action == "tend_fire" ? AccessibleHeatingBuildings(actor) : AccessibleShelters(actor))
            .Where(building => ShelterBuildingKnown(actor, person, building))
            .Where(building => order.TargetBuildingKind != "house" ||
                building.HouseholdId == society.Checkpoint.GetInhabitant(actor).HouseholdId &&
                worldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house", StringComparer.Ordinal))
            .Where(building => order.Action != "tend_fire" || !IsFireLit(building))
            .OrderBy(building => map.FootDistance(person.Position, building.Position))
            .ThenBy(building => building.InstanceId, StringComparer.Ordinal);
        var choices = new List<OwnerShelterBinding>();
        foreach (var building in buildings)
        {
            GridPoint? position;
            if (order.TargetPosition is { } requested)
            {
                if (order.Action == "tend_fire" ? requested != building.Position : !ShelterBuildingCovers(building, requested)) continue;
                position = requested;
            }
            else if (order.Action == "seek_shelter" && ShelterBuildingCovers(building, person.Position))
                position = person.Position;
            else if (IsTownHall(building))
                position = ReachableHallShelterPoint(actor, person.Position, building);
            else if (building.HouseholdId is null)
                position = map.FootNeighbors(building.Position).Append(building.Position)
                    .Where(point => map.IsPassable(point) && ShelterRouteIsOpen(actor, person.Position, point))
                    .OrderBy(point => map.FootDistance(person.Position, point)).ThenBy(point => point.Y).ThenBy(point => point.X)
                    .Select(point => (GridPoint?)point).FirstOrDefault();
            else
                position = building.Position;
            if (position is not { } destination || !ShelterRouteIsOpen(actor, person.Position, destination)) continue;
            choices.Add(new OwnerShelterBinding("building", destination, building.InstanceId, building.DefinitionId,
                building.HouseholdId, building.Position, building.PlacedTick));
        }
        if (order.Action == "seek_shelter" && order.TargetBuildingKind is null)
        {
            var natural = order.TargetPosition is { } requested
                ? IsWithinInteractionRange(person.Position, requested, ResourceInteractionRange) &&
                    WeatherAt(requested) == WeatherKind.Storm && NaturalStormCover(requested)
                    ? requested : (GridPoint?)null
                : WeatherAt(person.Position) == WeatherKind.Storm
                    ? NaturalStormCover(person.Position) ? person.Position : NearbyNaturalStormCover(actor, person.Position)
                    : null;
            if (natural is { } cover && WeatherAt(cover) == WeatherKind.Storm && ShelterRouteIsOpen(actor, person.Position, cover))
                choices.Add(new OwnerShelterBinding("natural", cover));
        }
        return choices.OrderBy(choice => map.FootDistance(person.Position, choice.Position))
            .ThenBy(choice => choice.Kind, StringComparer.Ordinal)
            .ThenBy(choice => choice.BuildingInstanceId, StringComparer.Ordinal).FirstOrDefault();
    }

    private bool CanInspectShelterTarget(OwnerInstructionOrder order, string actor, PlaytestInhabitantState person) =>
        order.ShelterBinding is null && order.TargetPosition is { } target && map.Contains(target) &&
        !IsWithinInteractionRange(person.Position, target, ResourceInteractionRange) &&
        FindUnoccupiedRoute(actor, person.Position, target, ResourceInteractionRange).Count > 0;

    private MapResource? KnownOrderFirewoodSource(string actor, PlaytestInhabitantState person)
    {
        var known = knowledge.Facts.Where(fact => fact.OwnerId == actor &&
                fact.ResourceKinds.Any(kind => kind is "wood" or "construction"))
            .Select(fact => fact.Position).ToHashSet();
        var toolCache = new Dictionary<(ToolFamily Family, int Tier), ToolDefinition?>();
        return map.Resources.Where(source => (source.Kind is "wood" or "construction") &&
                resources.GetValueOrDefault(source.Id) == ResourceState.Available &&
                (known.Contains(source.Position) || IsWithinInteractionRange(person.Position, source.Position, ResourceInteractionRange)))
            .OrderBy(source => map.FootDistance(person.Position, source.Position)).ThenBy(source => source.Id, StringComparer.Ordinal)
            .FirstOrDefault(source => CanGatherFromSource(actor, "wood", source, toolCache) &&
                FreeCarryCapacity(actor) > 0 &&
                (ProjectMaterialHarvest(actor, "wood", source) is not { } harvest ||
                    FreeCarryCapacity(actor) >= harvest.Quantity + harvest.TreeSeedQuantity) &&
                MaterialOrderRouteIsOpen(actor, person.Position, source.Position));
    }

    private bool CanObtainFirewood(string actor, PlaytestInhabitantState person) => HasCarriedOwnItem(actor, "wood") ||
        FreeCarryCapacity(actor) > 0 && SharedItem("wood", actor) is not null || KnownOrderFirewoodSource(actor, person) is not null;

    private CognitionCandidate? ShelterOrderCandidateFor(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        if (survivalState is null) return null;
        var order = instruction.Order!;
        var binding = SelectShelterBinding(instruction, person);
        if (binding is null)
            return CanInspectShelterTarget(order, instruction.TargetInhabitantId, person)
                ? new CognitionCandidate("inspect_shelter_site", "Travel to observe the shelter site named by this order.", 0) : null;
        if (!ShelterBindingIsAvailable(instruction.TargetInhabitantId, order with { ShelterBinding = binding }) ||
            !ShelterRouteIsOpen(instruction.TargetInhabitantId, person.Position, binding.Position) ||
            order.Action == "tend_fire" && !CanObtainFirewood(instruction.TargetInhabitantId, person)) return null;
        return new CognitionCandidate(order.Action,
            order.Action == "seek_shelter" ? "Reach the requested permitted shelter." : "Light one permitted hearth using your own wood.",
            0, binding.BuildingInstanceId);
    }

    private void ExecuteShelterOrderStep(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var actor = instruction.TargetInhabitantId;
        var current = instructionsByIdempotency[instruction.IdempotencyKey];
        var order = current.Order!;
        var binding = SelectShelterBinding(current, person);
        if (binding is null)
        {
            if (CanInspectShelterTarget(order, actor, person))
            {
                MoveToward(actor, person, order.TargetPosition!.Value, "owner_order_shelter_site", ResourceInteractionRange);
                var reached = inhabitants[actor].Position;
                if (reached != person.Position) RecordKnowledgeFact(actor, reached);
                if (IsWithinInteractionRange(reached, order.TargetPosition.Value, ResourceInteractionRange))
                    RecordKnowledgeFact(actor, order.TargetPosition.Value);
            }
            else SetOrderStatus(current, "blocked", ShelterOrderBlockedReason(current, person));
            return;
        }
        if (order.ShelterBinding is null)
        {
            order = order with { ShelterBinding = binding };
            current = current with { Order = order };
            instructionsByIdempotency[current.IdempotencyKey] = current;
            checkpointSchemaVersion = StateSchemaVersion;
        }
        if (!ShelterBindingIsAvailable(actor, order) || !ShelterRouteIsOpen(actor, person.Position, binding.Position))
        {
            SetOrderStatus(current, "blocked", ShelterOrderBlockedReason(current, person));
            return;
        }
        if (order.Action == "tend_fire" && !HasCarriedOwnItem(actor, "wood"))
        {
            if (FreeCarryCapacity(actor) > 0 && SharedItem("wood", actor) is not null)
                CollectEquipment(actor, person, "wood");
            else if (KnownOrderFirewoodSource(actor, person) is { } source)
            {
                _ = GatherProjectMaterial(actor, person, "wood", source);
                var reached = inhabitants[actor].Position;
                if (IsWithinInteractionRange(reached, source.Position, ResourceInteractionRange))
                    RecordKnowledgeFact(actor, source.Position);
            }
            else SetOrderStatus(current, "blocked", ShelterOrderBlockedReason(current, person));
            return;
        }
        if (person.Position != binding.Position)
            MoveToward(actor, person, binding.Position, order.Action == "tend_fire" ? "fuel_fire" : "warmth");
        if (inhabitants[actor].Position != binding.Position || !ShelterBindingIsAvailable(actor, order)) return;
        string? payment = null;
        if (order.Action == "tend_fire")
        {
            var effect = TendFireAt(actor, inhabitants[actor], BoundShelterBuilding(actor, order)!);
            if (effect is null) return;
            payment = effect.FuelReservationId;
        }
        var completion = new OwnerShelterCompletion(WorldTick, inhabitants[actor].Position, payment);
        current = instructionsByIdempotency[current.IdempotencyKey];
        current = current with { Order = current.Order! with { ShelterCompletion = completion } };
        instructionsByIdempotency[current.IdempotencyKey] = current;
        CreditOrderEffect(current, ShelterOrderReceipt(current.InstructionId, current.Order!), 1);
    }

    private string ShelterOrderBlockedReason(OwnerQueuedInstruction instruction, PlaytestInhabitantState person)
    {
        var order = instruction.Order!;
        if (!AgePermitsCandidate(instruction.TargetInhabitantId, order.Action))
            return order.Action == "tend_fire" ? "This agent is too young to light a fire." : "This agent needs a caregiver to reach shelter.";
        if (survivalState is null) return "Shelter and fire care are not available in this world yet.";
        if (order.ShelterBinding is not null && !ShelterBindingIsAvailable(instruction.TargetInhabitantId, order))
            return order.Action == "tend_fire" ? "The chosen hearth is unavailable, changed, or already burning." : "The chosen shelter is no longer available or permitted.";
        if (order.Action == "tend_fire" && !CanObtainFirewood(instruction.TargetInhabitantId, person))
            return "No usable owned firewood can be carried to the hearth right now.";
        return order.TargetPosition is not null ? "The requested shelter site is not permitted or reachable right now." :
            "No known permitted shelter is reachable right now.";
    }

    private static string ShelterOrderReceipt(string instructionId, OwnerInstructionOrder order) =>
        (order.Action == "tend_fire" ? "shelter:fire:" : "shelter:arrival:") +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { instructionId, order.Action, order.ShelterBinding, order.ShelterCompletion }))));

    private static void ValidateShelterOrderBindings(
        WorldContentSimulationState? simulation, DeclarativeWorldContentState content, SocietyCheckpoint society,
        IEnumerable<OwnerQueuedInstruction> instructions, long worldTick)
    {
        var fuelReceipts = new HashSet<string>(StringComparer.Ordinal);
        var householdIds = society.Households.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var instruction in instructions.Where(item => item.Order is { } order && IsShelterOrder(order.Action)))
        {
            var order = instruction.Order!;
            if (!IsValidShelterOrderShape(order, instruction, worldTick))
                throw new InvalidDataException("A shelter order has an invalid saved shape.");
            if (order.ShelterBinding is { } binding)
            {
                if (binding.Kind is not ("natural" or "building") || binding.Position.X < 0 || binding.Position.Y < 0 ||
                    order.TargetPosition is { } requested && requested != binding.Position)
                    throw new InvalidDataException("A shelter order has an invalid destination binding.");
                if (binding.Kind == "natural")
                {
                    if (order.Action != "seek_shelter" || order.TargetBuildingKind is not null || binding.BuildingInstanceId is not null ||
                        binding.DefinitionId is not null || binding.OwnerId is not null || binding.BuildingPosition is not null || binding.BuildingPlacedTick is not null)
                        throw new InvalidDataException("Natural cover cannot carry a building or fire binding.");
                }
                else
                {
                    var definition = content.Buildings.FirstOrDefault(item => item.CanonicalId == binding.DefinitionId);
                    if (simulation is null || !ValidConstructionIdentity(binding.BuildingInstanceId) || definition is null ||
                        binding.BuildingPosition is not { X: >= 0, Y: >= 0 } || binding.BuildingPlacedTick is not { } placed || placed < 0 || placed > worldTick ||
                        binding.OwnerId is { } owner && !householdIds.Contains(owner) ||
                        order.TargetBuildingKind == "house" && (!definition.Tags.Contains("house", StringComparer.Ordinal) || binding.OwnerId is null) ||
                        !(order.Action == "seek_shelter" ? definition.Tags.Any(tag => tag is "shelter" or TownHallContent.HallTag) :
                            definition.Tags.Any(tag => tag is "cooking" or "warmth")))
                        throw new InvalidDataException("A shelter order has an invalid building binding.");
                }
            }
            if (order.ShelterCompletion is not { } completion)
            {
                if (order.CompletedUnits != 0 || order.LastEffectId is not null)
                    throw new InvalidDataException("Shelter progress requires an actual completion receipt.");
                continue;
            }
            if (order.ShelterBinding is not { } completedBinding || order.Status != "finished" || order.CompletedUnits != 1 ||
                completion.WorldTick < instruction.SubmittedTick || completion.WorldTick > worldTick ||
                completedBinding.BuildingPlacedTick > completion.WorldTick || completion.Position != completedBinding.Position ||
                order.LastEffectId != ShelterOrderReceipt(instruction.InstructionId, order))
                throw new InvalidDataException("A shelter completion does not match its bound owner order.");
            if (order.Action == "seek_shelter")
            {
                if (completion.FuelReservationId is not null)
                    throw new InvalidDataException("A shelter arrival cannot claim a fire payment.");
                continue;
            }
            var reservation = society.Inventory.Reservations.FirstOrDefault(item => item.Id == completion.FuelReservationId);
            if (reservation is null || !fuelReceipts.Add(reservation.Id) || reservation.OwnerId != instruction.TargetInhabitantId ||
                reservation.Quantity != 1 || reservation.Purpose != "heating_fuel" || reservation.State != InventoryReservationState.Completed ||
                reservation.ExpiryTick != completion.WorldTick ||
                society.Inventory.Lots.FirstOrDefault(item => item.Id == reservation.LotId) is { ItemKind: not "wood" })
                throw new InvalidDataException("A fire order lacks its unique completed wood payment.");
        }
    }
}
