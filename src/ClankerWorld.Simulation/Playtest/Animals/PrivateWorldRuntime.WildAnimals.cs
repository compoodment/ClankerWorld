using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private AnimalState AdvanceWildAnimalCare(AnimalState animal, long tick)
    {
        if (AnimalRules.HasCare(animal, tick)) return animal;
        var definition = AnimalRules.Definition(animal.Species);
        if (animal.WildFedUntilTick <= tick)
        {
            var occupied = WildAnimalRouteOccupants(animal);
            var forage = WildAnimalForageSources(map, worldSystems.Ecology, animal)
                .FirstOrDefault(source => map.IsReachableOnFoot(animal.Position, source.Position) &&
                    SharedUnoccupiedRoute(animal.Position, occupied, source.Position, 1).Count > 0);
            if (forage is null) return animal;
            if (map.FootDistance(animal.Position, forage.Position) > 1)
                return MoveWildAnimalToward(animal, forage.Position, tick, 1);
            var harvested = EcologyRules.Harvest(worldSystems.Ecology.GetResource(forage.Id), definition.DailyFeed);
            if (!harvested.IsValid || harvested.Resource is not { } remaining) return animal;
            worldSystems = worldSystems with
            {
                Ecology = worldSystems.Ecology with
                { Resources = worldSystems.Ecology.Resources.Select(resource => resource.Id == forage.Id ? remaining : resource).ToArray() }
            };
            SyncEcologyResourceStates();
            SetAnimal(animal = animal with { WildFedUntilTick = tick + AnimalDayTicks });
        }
        if (animal.WildWaterUntilTick <= tick)
        {
            var shore = FreshWaterShorePositions().Where(point => !animalWorld.Animals.Any(other => other.Id != animal.Id &&
                    other.DiedTick is null && other.Position == point) && !inhabitants.Values.Any(person => person.Position == point) &&
                    map.FootDistance(animal.Position, point) <= 12 &&
                    map.IsReachableOnFoot(animal.Position, point)).OrderBy(point => map.FootDistance(animal.Position, point))
                .ThenBy(point => point.Y).ThenBy(point => point.X).Cast<GridPoint?>().FirstOrDefault();
            if (shore is null) return animal;
            if (animal.Position != shore.Value) return MoveWildAnimalToward(animal, shore.Value, tick, 0);
            SetAnimal(animal = animal with { WildWaterUntilTick = tick + AnimalDayTicks });
        }
        if (animal.WildFedUntilTick > tick && animal.WildWaterUntilTick > tick)
            SetAnimal(animal = animal with { CareUntilTick = Math.Min(animal.WildFedUntilTick, animal.WildWaterUntilTick) });
        return animal;
    }

    internal static IOrderedEnumerable<MapResource> WildAnimalForageSources(SeededMap map, EcologyState ecology, AnimalState animal)
    {
        var feed = AnimalRules.Definition(animal.Species).DailyFeed;
        return map.Resources.Where(source => source.NaturalObjectKind == "wild_greens" &&
                map.FootDistance(animal.Position, source.Position) <= 12 &&
                ecology.TryGetResource(source.Id, out var resource) && resource.Quantity >= feed)
            .OrderBy(source => map.FootDistance(animal.Position, source.Position)).ThenBy(source => source.Id, StringComparer.Ordinal);
    }

    private AnimalState MoveWildAnimalToward(AnimalState animal, GridPoint destination, long tick, int range)
    {
        if (tick % 2 != 0 || animal.TamingWork is not null) return animal;
        var route = SharedUnoccupiedRoute(animal.Position, WildAnimalRouteOccupants(animal), destination, range);
        if (route.Count > 1) MoveAnimal(animal, route[1]);
        return Animal(animal.Id)!;
    }

    private HashSet<GridPoint> WildAnimalRouteOccupants(AnimalState animal) =>
        inhabitants.Values.Select(person => person.Position).Concat(animalWorld.Animals
            .Where(other => other.DiedTick is null && other.Id != animal.Id).Select(other => other.Position)).ToHashSet();
}
