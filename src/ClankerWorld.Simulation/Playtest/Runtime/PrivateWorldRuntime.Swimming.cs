using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private bool CanSwim(string actor)
    {
        var person = inhabitants[actor];
        // A swimmer can always make their way out if their condition changes mid-crossing.
        if (SwimmingRules.IsSwimmingWater(map, person.Position)) return true;
        return PassengerBoat(actor) is null && AttachedHandcart(actor) is null &&
            !animalWorld.Animals.Any(animal => animal.RiderId == actor || animal.LeaderId == actor) &&
            MovingCareGroup(actor).All(id => SwimmingStartingCondition(id)) &&
            !inhabitants.Values.Any(child => child.GuardianPlacement?.CaregiverId == actor);
    }

    private bool SwimmingStartingCondition(string actor)
    {
        var person = inhabitants[actor];
        return society.Checkpoint.GetInhabitant(actor).AgeBand != SocietyAgeBand.Infant &&
            (person.Survival?.WarmthBasisPoints ?? 10_000) >= SwimmingRules.MinimumStartingWarmth &&
            (person.Survival?.IllnessBasisPoints ?? 0) <= SwimmingRules.MaximumStartingIllness &&
            PersonalEquipmentRules.CarriedQuantity(society.Checkpoint.Inventory, actor, person.Equipment) <= SwimmingRules.MaximumCarriedUnits &&
            AttachedHandcart(actor) is null &&
            !animalWorld.Animals.Any(animal => animal.RiderId == actor || animal.LeaderId == actor);
    }

    private int AgentStepCost(GridPoint from, GridPoint to) =>
        SwimmingRules.IsSwimmingWater(map, from) || SwimmingRules.IsSwimmingWater(map, to)
            ? SwimmingRules.StepCost : RoadStepCost(from, to);
}
