using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private bool IsSwimming(string actor, GridPoint position) =>
        PassengerBoat(actor) is null && SwimmingRules.IsSwimmingWater(map, position);

    private bool CanSwim(string actor, GridPoint? origin = null, int additionalCarriedUnits = 0)
    {
        var person = inhabitants[actor];
        if (PassengerBoat(actor) is not null || AttachedHandcart(actor) is not null ||
            animalWorld.Animals.Any(animal => animal.RiderId == actor || animal.LeaderId == actor))
            return false;
        // A swimmer can always make their way out if their condition changes mid-crossing.
        if (SwimmingRules.IsSwimmingWater(map, origin ?? person.Position)) return true;
        return MovingCareGroup(actor).All(id => SwimmingStartingCondition(id, id == actor ? additionalCarriedUnits : 0)) &&
            !inhabitants.Values.Any(child => child.GuardianPlacement?.CaregiverId == actor);
    }

    private bool SwimmingStartingCondition(string actor, int additionalCarriedUnits = 0)
    {
        var person = inhabitants[actor];
        return person.GuardianPlacement is null && society.Checkpoint.GetInhabitant(actor).AgeBand != SocietyAgeBand.Infant &&
            (person.Survival?.WarmthBasisPoints ?? 10_000) >= SwimmingRules.MinimumStartingWarmth &&
            (person.Survival?.IllnessBasisPoints ?? 0) <= SwimmingRules.MaximumStartingIllness &&
            PersonalEquipmentRules.CarriedQuantity(society.Checkpoint.Inventory, actor, person.Equipment) <=
                SwimmingRules.MaximumCarriedUnits - additionalCarriedUnits &&
            AttachedHandcart(actor) is null &&
            !animalWorld.Animals.Any(animal => animal.RiderId == actor || animal.LeaderId == actor);
    }

    // Preserve occupancy-independent task selection while permitting an eligible freshwater route.
    private bool CanReachByFootOrSwimming(string actor, GridPoint from, GridPoint destination) =>
        map.IsReachableOnFoot(from, destination) ||
        SwimmingRules.IsReachable(map, from, destination) && CanSwim(actor);

    private int AgentStepCost(GridPoint from, GridPoint to) =>
        SwimmingRules.IsSwimmingWater(map, from) || SwimmingRules.IsSwimmingWater(map, to)
            ? SwimmingRules.StepCost : RoadStepCost(from, to);

    // Plan from the tile where the real pickup will happen, including an
    // interaction beside a resource or storage building. No inventory is changed.
    private GridPoint? PickupPosition(string actor, GridPoint source, int sourceRange)
    {
        var position = inhabitants[actor].Position;
        if (IsWithinInteractionRange(position, source, sourceRange)) return position;
        var approach = FindUnoccupiedRoute(actor, position, source, sourceRange);
        return approach.Count == 0 ? null : approach[^1];
    }

    private int PickupCarryCapacity(string actor, GridPoint source, GridPoint destination,
        int destinationRange = 0, int sourceRange = 0)
    {
        if (PickupPosition(actor, source, sourceRange) is not { } origin) return 0;
        var capacity = FreeCarryCapacity(actor);
        if ((destinationRange > 0 || map.IsReachableOnFoot(origin, destination)) &&
            FindUnoccupiedRoute(actor, origin, destination, destinationRange, allowSwimming: false).Count > 0)
            return capacity;
        var swimmingRoom = Math.Max(0, SwimmingRules.MaximumCarriedUnits -
            PersonalEquipmentRules.CarriedQuantity(society.Checkpoint.Inventory, actor, inhabitants[actor].Equipment));
        return swimmingRoom > 0 && FindUnoccupiedRoute(actor, origin, destination, destinationRange,
            additionalCarriedUnits: 1).Count > 0 ? Math.Min(capacity, swimmingRoom) : 0;
    }

    private int PickupCarryCapacity(string actor, ClankerWorld.Simulation.Kernel.InventoryLot stock,
        GridPoint destination, int destinationRange = 0) =>
        PickupCarryCapacity(actor, HouseholdStockPosition(stock), destination,
            destinationRange, HouseholdStockInteractionRange(stock));

    private bool CanReturnWithHarvest(string actor, string itemKind, ClankerWorld.Simulation.Harness.MapResource source,
        GridPoint destination, int destinationRange = 0, bool useHarvestBonus = true)
    {
        if (PickupPosition(actor, source.Position, ResourceInteractionRange) is not { } origin) return false;
        // Existing foot gathering may first make room. Keep that behavior.
        if ((destinationRange > 0 || map.IsReachableOnFoot(origin, destination)) &&
            FindUnoccupiedRoute(actor, origin, destination, destinationRange, allowSwimming: false).Count > 0)
            return true;
        return ProjectMaterialCarryUnits(actor, itemKind, source, useHarvestBonus) <=
            PickupCarryCapacity(actor, source.Position, destination, destinationRange, ResourceInteractionRange);
    }
}
