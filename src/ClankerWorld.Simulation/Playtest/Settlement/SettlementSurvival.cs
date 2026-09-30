using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed record SurvivalCondition(int WarmthBasisPoints = 10_000, int IllnessBasisPoints = 0,
    int NutritionBasisPoints = 5_000, string? LastMealKind = null);
public sealed record CampFireState(string BuildingId, long FuelUntilTick);
public sealed record SettlementSurvivalState(long ActivatedTick, IReadOnlyList<CampFireState> Fires);

public static class SettlementIllnessRules
{
    public static int WorkRatePercent(int illnessBasisPoints) => Math.Clamp(illnessBasisPoints, 0, 10_000) switch
    {
        < 2_500 => 100,
        < 5_000 => 75,
        < 7_500 => 50,
        _ => 25,
    };

    public static int TravelDelayTicks(int illnessBasisPoints) => Math.Clamp(illnessBasisPoints, 0, 10_000) switch
    {
        < 2_500 => 0,
        < 7_500 => 1,
        _ => 2,
    };

    public static bool AllowsWork(string inhabitantId, long worldTick, int illnessBasisPoints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inhabitantId);
        ArgumentOutOfRangeException.ThrowIfNegative(worldTick);
        if (WorkRatePercent(illnessBasisPoints) == 100) return true;
        var phase = StablePhase(inhabitantId);
        return (worldTick % 4 + phase) % 4 < WorkRatePercent(illnessBasisPoints) / 25;
    }

    private static int StablePhase(string inhabitantId)
    {
        uint hash = 2_166_136_261;
        foreach (var character in inhabitantId)
        {
            hash = unchecked((hash ^ character) * 16_777_619);
        }
        return (int)(hash % 4);
    }
}

public sealed partial class PrivateWorldRuntime
{
    private SettlementSurvivalState? survivalState;
    private static readonly HashSet<string> PerishableKinds = new(StringComparer.Ordinal) { "food" };
    private const int IllnessRecoveryPerTick = 12;
    private const int ShelteredIllnessRecoveryBonusPerTick = 12;
    private const int IllnessCareReliefBasisPoints = 250;
    private const int ExposureIllnessIncreasePerTick = 8;
    private const int SpoiledMealIllnessIncreaseBasisPoints = 300;
    private const int FreshMealIllnessReliefBasisPoints = 100;

    private const int ComfortableFullness = 4_000;
    private const int ComfortableWarmth = 6_000;
    private const int UrgentFullness = 2_000;
    private const int UrgentWarmth = 3_500;
    // Provisional routine and outing reserves, not owner-approved balance.
    private const int RoutineFoodSeekFullness = 4_500;
    private const int OutingFullnessReserve = 3_000;

    private static bool NeedsUrgentFood(PlaytestInhabitantState person) => person.HungerBasisPoints < UrgentFullness;

    private bool NeedsUrgentWarmth(PlaytestInhabitantState person) =>
        person.Survival is { } condition && condition.WarmthBasisPoints < UrgentWarmth && WarmthChange(person) < 0;

    private bool ReadyForBriefInteraction(string actor) => inhabitants.TryGetValue(actor, out var person) &&
        !NeedsUrgentWarmth(person);

    private int WarmthChange(PlaytestInhabitantState person)
    {
        var naturalCover = WeatherAt(person.Position) == WeatherKind.Storm && NaturalStormCover(person.Position);
        var protection = (HasCarriedItem(person.InhabitantId, "clothing") ? 35 : 0) +
            (NearShelter(person.InhabitantId, person.Position) || naturalCover ? 45 : 0);
        var heat = AccessibleHeatingBuildings(person.InhabitantId).Any(building => IsFireLit(building) &&
            IsWithinInteractionRange(person.Position, building.Position,
                building.HouseholdId is null ? 2 : 0)) ? 90 : 0;
        var loss = Math.Max(0, WeatherExposure(person.Position) - protection);
        return -loss + heat + (loss == 0 ? 20 : 0);
    }

    private bool IsProtectiveProject(SettlementProject? project) =>
        project is not null &&
        TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection) &&
        (selection.IsBuilding
            ? worldContent.Buildings.Any(building => selection.DefinitionId == building.CanonicalId &&
                building.Tags.Any(tag => tag is "shelter" or "warmth" or "cooking"))
            : worldContent.Recipes.Any(recipe => selection.DefinitionId == recipe.CanonicalId &&
                recipe.Outputs.Any(output => output.ResourceId == "clothing")));

    private WeatherKind WeatherAt(GridPoint position) => WeatherRules.At(worldSystems, position, map.Height,
        WeatherRules.RegionClimate(map, position));

    private int WeatherExposure(GridPoint position) => WeatherAt(position) switch
    {
        WeatherKind.Snow => 60,
        WeatherKind.Storm => 55,
        WeatherKind.Rain => 25,
        _ => map.ClimateAt(position) switch
        {
            ClimateZone.Tropical => 0,
            ClimateZone.Polar => 55,
            ClimateZone.Cold => worldSystems.Climate.Season == SeasonKind.Winter ? 45 : 15,
            _ => worldSystems.Climate.Season == SeasonKind.Winter ? 40 : 0,
        },
    };

    private bool HasCarriedItem(string actor, string kind) => society.Checkpoint.Inventory.Lots.Any(lot =>
        lot.OwnerId == actor && lot.ItemKind == kind && AvailableLotQuantity(lot) > 0);

    private InventoryLot? SharedItem(string kind, string actor) => society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
        lot.OwnerId == HouseholdFor(actor) && lot.ItemKind == kind && AvailableLotQuantity(lot) > 0 &&
        (lot.StorageBuildingId is null || society.Checkpoint.GetInhabitant(actor).HouseholdId == lot.OwnerId)) ??
        society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
            lot.ItemKind == kind && kind != "food" && AvailableLotQuantity(lot) > 0 &&
            lot.OwnerId == TownForResident(actor) && WarehouseForResident(actor)?.InstanceId == lot.StorageBuildingId);

    private IEnumerable<PlacedBuilding> BuildingsWithTag(string tag) => worldSimulation.Buildings.Where(building =>
        worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains(tag, StringComparer.Ordinal)));

    private IEnumerable<PlacedBuilding> AccessibleShelters(string actor) => BuildingsWithTag("shelter")
        .Where(building => building.HouseholdId is null ||
            building.HouseholdId == society.Checkpoint.GetInhabitant(actor).HouseholdId);

    private bool NearShelter(string actor, GridPoint point) => AccessibleShelters(actor).Any(building =>
        IsWithinInteractionRange(point, building.Position,
            building.HouseholdId is null ? ResourceInteractionRange : 0));

    private bool NaturalStormCover(GridPoint point) =>
        map.VegetationAt(point) == VegetationCover.Forest ||
        map.Resources.Any(site => site.Position == point &&
            (site.TreeKind == "orchard" ||
             (site.TreeKind is "broadleaf" or "conifer") &&
             worldSystems.Ecology.GetResource(site.Id).Quantity > 0));

    private GridPoint? NearbyNaturalStormCover(string actor, GridPoint origin)
    {
        const int searchRadius = 8;
        var occupied = inhabitants.Values.Where(person => person.InhabitantId != actor)
            .Select(person => person.Position).ToHashSet();
        GridPoint? best = null;
        var bestDistance = int.MaxValue;
        for (var dy = -searchRadius; dy <= searchRadius; dy++)
            for (var dx = -searchRadius; dx <= searchRadius; dx++)
            {
                var x = origin.X + dx;
                if (map.WrapsEastWest) x = (x % map.Width + map.Width) % map.Width;
                var candidate = new GridPoint(x, origin.Y + dy);
                if (!map.IsPassable(candidate) || occupied.Contains(candidate) || !NaturalStormCover(candidate))
                    continue;
                var distance = map.FootDistance(origin, candidate);
                if (distance >= bestDistance || FindUnoccupiedRoute(actor, origin, candidate, 0).Count == 0) continue;
                best = candidate;
                bestDistance = distance;
            }
        return best;
    }

    private bool IsFireLit(PlacedBuilding building) => survivalState?.Fires.Any(fire =>
        fire.BuildingId == building.InstanceId && fire.FuelUntilTick > WorldTick) == true;

    private IEnumerable<PlacedBuilding> HeatingBuildings() => BuildingsWithTag("cooking").Concat(BuildingsWithTag("warmth"))
        .DistinctBy(building => building.InstanceId);

    private IEnumerable<PlacedBuilding> AccessibleHeatingBuildings(string actor) => HeatingBuildings()
        .Where(building => building.HouseholdId is null ||
            building.HouseholdId == society.Checkpoint.GetInhabitant(actor).HouseholdId);

    private void AdvanceSettlementSurvival()
    {
        if (survivalState is null && !contentRegistry.ExportState().Packages.Any(package => package.Manifest.PackageId == SettlementContent.PackageId &&
            package.Lifecycle == ContentPackageLifecycle.Active))
        {
            return;
        }
        if (survivalState is null)
        {
            survivalState = new SettlementSurvivalState(WorldTick, []);
            // Do not retroactively rot years of legacy inventory on migration.
            ApplyInventoryTransition(inventory => InventoryFixture.ProcessSpoilage(inventory, WorldTick, 0));
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("survival_activated", "weather_equipment_and_food");
        }
        var existingIds = worldSimulation.Buildings.Select(building => building.InstanceId).ToHashSet(StringComparer.Ordinal);
        foreach (var fire in survivalState.Fires.Where(fire => fire.FuelUntilTick <= WorldTick || !existingIds.Contains(fire.BuildingId)))
        {
            AppendEvent("fire_extinguished", fire.BuildingId);
        }
        survivalState = survivalState with
        {
            Fires = survivalState.Fires.Where(fire =>
            fire.FuelUntilTick > WorldTick && existingIds.Contains(fire.BuildingId)).ToArray()
        };
        var sharedStorage = BuildingsWithTag("storage").Any(building => building.HouseholdId is null);
        var shelteredOwners = sharedStorage
            ? society.Checkpoint.Households.Select(household => household.Id).ToHashSet(StringComparer.Ordinal)
            : BuildingsWithTag("storage").Where(building => building.HouseholdId is not null)
                .Select(building => building.HouseholdId!).ToHashSet(StringComparer.Ordinal);
        ApplyInventoryTransition(inventory => InventoryFixture.ProcessSpoilage(inventory, WorldTick, 4,
            PerishableKinds, shelteredOwners));
        foreach (var person in inhabitants.Values.ToArray())
        {
            var old = person.Survival ?? new SurvivalCondition();
            var warmth = Math.Clamp(old.WarmthBasisPoints + WarmthChange(person), 0, 10_000);
            var illnessChange = warmth < 2_500 || person.HungerBasisPoints < 500
                ? ExposureIllnessIncreasePerTick
                : warmth > 6_000 && person.HungerBasisPoints > 3_500
                    ? -(IllnessRecoveryPerTick + (NearShelter(person.InhabitantId, person.Position) ? ShelteredIllnessRecoveryBonusPerTick : 0))
                    : 0;
            var illness = Math.Clamp(old.IllnessBasisPoints + illnessChange, 0, 10_000);
            inhabitants[person.InhabitantId] = person with { Survival = old with { WarmthBasisPoints = warmth, IllnessBasisPoints = illness } };
            if (old.WarmthBasisPoints / 2_500 != warmth / 2_500 || old.IllnessBasisPoints / 2_500 != illness / 2_500)
            {
                AppendEvent("survival_condition_changed", $"{person.InhabitantId}:warmth={warmth}:illness={illness}");
            }
        }
    }

    private void AddSurvivalCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState person)
    {
        if (person.Survival is not { } condition)
        {
            return;
        }
        if (WeatherExposure(person.Position) > 0 && !HasCarriedItem(actor, "clothing") && SharedItem("clothing", actor) is not null)
        {
            candidates.Add(new CognitionCandidate("wear_clothing", "Collect woven clothing from camp to reduce exposure.", 3));
        }
        var losingWarmth = WarmthChange(person) < 0;
        if (AdultResident(actor) && losingWarmth && condition.WarmthBasisPoints < ComfortableWarmth && AccessibleHeatingBuildings(actor).Any(building => !IsFireLit(building)) &&
            (SharedItem("wood", actor) is not null || HasCarriedItem(actor, "wood") || MaterialSource("wood", actor) is not null))
        {
            candidates.Add(new CognitionCandidate("tend_fire", "Carry household wood to a hearth and keep the camp warm.", NeedsUrgentWarmth(person) ? 1 : 2));
        }
        var stormCover = WeatherAt(person.Position) == WeatherKind.Storm && !NaturalStormCover(person.Position) &&
            NearbyNaturalStormCover(actor, person.Position) is not null;
        var recoveringInCover = NearShelter(actor, person.Position) ||
            WeatherAt(person.Position) == WeatherKind.Storm && NaturalStormCover(person.Position) ||
            WarmthChange(person) > 20;
        if (condition.WarmthBasisPoints < ComfortableWarmth &&
            (recoveringInCover || losingWarmth && (ReachableWarmthDestinations(actor, person).Any() || stormCover)))
        {
            candidates.Add(new CognitionCandidate("seek_warmth", "Seek protection or remain in cover while recovering warmth.", NeedsUrgentWarmth(person) ? 2 : 3));
        }
    }

    private void CollectEquipment(string actor, PlaytestInhabitantState person, string kind)
    {
        if (HasCarriedItem(actor, kind) || SharedItem(kind, actor) is not { } item)
        {
            return;
        }
        var storage = HouseholdStockPosition(item);
        var interactionRange = HouseholdStockInteractionRange(item);
        if (!IsWithinInteractionRange(person.Position, storage, interactionRange))
        {
            MoveToward(actor, person, storage, "equipment", interactionRange);
            return;
        }
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"equipment:{WorldTick}:{actor}:{kind}",
            item.OwnerId, actor, item.Id, 1, "equipment_collected"));
        AppendEvent("equipment_collected", $"{actor}:{kind}");
    }

    private void TendFire(string actor, PlaytestInhabitantState person)
    {
        var building = AccessibleHeatingBuildings(actor).FirstOrDefault(building => !IsFireLit(building));
        if (building is null || survivalState is null)
        {
            return;
        }
        if (!HasCarriedItem(actor, "wood"))
        {
            if (SharedItem("wood", actor) is not null)
            {
                CollectEquipment(actor, person, "wood");
            }
            else if (MaterialSource("wood", actor) is { } source)
            {
                GatherProjectMaterial(actor, person, "wood", source);
            }
            return;
        }
        var interactionRange = building.HouseholdId is null ? ResourceInteractionRange : 0;
        if (!IsWithinInteractionRange(person.Position, building.Position, interactionRange))
        {
            MoveToward(actor, person, building.Position, "fuel_fire", interactionRange);
            return;
        }
        var fuel = society.Checkpoint.Inventory.Lots.First(lot => lot.OwnerId == actor && lot.ItemKind == "wood" && AvailableLotQuantity(lot) > 0);
        society.Apply(checkpoint => ClankerWorld.Simulation.Society.SocietyFixture.ConsumeInventory(checkpoint, actor, fuel.Id, 1, "heating_fuel"));
        survivalState = survivalState with { Fires = survivalState.Fires.Append(new CampFireState(building.InstanceId, WorldTick + 120)).ToArray() };
        AppendEvent("fire_fuelled", building.InstanceId);
    }

    private IEnumerable<PlacedBuilding> ReachableWarmthDestinations(string actor, PlaytestInhabitantState person) =>
        AccessibleHeatingBuildings(actor).Where(IsFireLit).Concat(AccessibleShelters(actor))
            .DistinctBy(building => building.InstanceId)
            .Where(building => !IsWithinInteractionRange(person.Position, building.Position,
                    building.HouseholdId is null ? ResourceInteractionRange : 0) &&
                FindUnoccupiedRoute(actor, person.Position, building.Position,
                    building.HouseholdId is null ? ResourceInteractionRange : 0).Count > 0)
            .OrderBy(building => IsFireLit(building) ? 0 : 1)
            .ThenBy(building => map.FootDistance(person.Position, building.Position));

    private void SeekWarmth(string actor, PlaytestInhabitantState person)
    {
        // Do not abandon useful cover merely because its original travel target disappeared.
        if (WarmthChange(person) >= 0 || WeatherAt(person.Position) == WeatherKind.Storm && NaturalStormCover(person.Position))
            return;
        var destination = ReachableWarmthDestinations(actor, person).FirstOrDefault();
        var cover = WeatherAt(person.Position) == WeatherKind.Storm && !NaturalStormCover(person.Position)
            ? NearbyNaturalStormCover(actor, person.Position) : null;
        if (cover is { } coverPoint &&
            (destination is null || map.FootDistance(person.Position, coverPoint) <
                map.FootDistance(person.Position, destination.Position)))
        {
            if (person.Position != coverPoint)
                MoveToward(actor, person, coverPoint, "storm_cover");
        }
        else if (destination is not null && !IsWithinInteractionRange(person.Position, destination.Position,
                     destination.HouseholdId is null ? ResourceInteractionRange : 0))
        {
            MoveToward(actor, person, destination.Position, "warmth",
                destination.HouseholdId is null ? ResourceInteractionRange : 0);
        }
    }

    private bool NeedsRecipeOutput(RecipeDefinition recipe, string? ownerId = null) => survivalState is null || recipe.Outputs.Any(output =>
    {
        var available = society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == output.ResourceId &&
                (ownerId is null || lot.OwnerId == ownerId))
            .Sum(AvailableLotQuantity);
        var target = output.ResourceId == "food" ? inhabitants.Count * 4 : Math.Max(1, inhabitants.Count);
        return available < target;
    });

    private int CropOutputQuantity(RecipeDefinition recipe, ContentQuantity output, WeatherKind weather,
        int soilMoisture)
    {
        if (!recipe.IsCrop || survivalState is null || output.ResourceId != "food")
            return output.Amount;
        return weather switch
        {
            WeatherKind.Snow => Math.Max(1, output.Amount / 2),
            WeatherKind.Storm => Math.Max(1, checked((int)((long)output.Amount * 3 / 4))),
            _ when soilMoisture < 15 => Math.Max(1, checked((int)((long)output.Amount * 3 / 4))),
            _ when soilMoisture >= 50 => checked(output.Amount + Math.Max(1, output.Amount / 4)),
            _ => output.Amount,
        };
    }

    private string FoodSource(InventoryLot lot)
    {
        var inventory = society.Checkpoint.Inventory;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var originId = lot.Id;
        while (lot.ProvenanceLotId is { } parent && visited.Add(lot.Id))
        {
            originId = parent;
            if (inventory.Lots.FirstOrDefault(item => item.Id == parent) is not { } source)
            {
                break;
            }
            lot = source;
        }
        var job = worldSimulation.ProductionJobs.Concat(worldSimulation.CropBuilds ?? [])
            .FirstOrDefault(job => originId.StartsWith(job.JobId + ":output:", StringComparison.Ordinal));
        if (job is not null)
        {
            return worldContent.Recipes.FirstOrDefault(recipe => recipe.CanonicalId == job.RecipeId)?.IsCrop == true ? "crops" : "cooked";
        }
        if (lot.ItemKind == "fruit") return "orchard";
        return originId.StartsWith("food:harvest:", StringComparison.Ordinal) ? "foraged" : "camp_rations";
    }

    private static bool IsEdibleFood(string kind) => kind is "food" or "fruit";

    private IEnumerable<InventoryLot> PreferredFood(string owner, string? actor = null)
    {
        var previous = actor is not null && inhabitants.TryGetValue(actor, out var person) ? person.Survival?.LastMealKind : null;
        return society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == owner &&
                IsEdibleFood(lot.ItemKind) && AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => previous is not null && FoodSource(lot) == previous ? 1 : 0)
            .ThenByDescending(lot => lot.FreshnessBasisPoints).ThenBy(lot => lot.Id, StringComparer.Ordinal);
    }

    private SurvivalCondition? AfterMeal(PlaytestInhabitantState person, InventoryLot food)
    {
        if (person.Survival is not { } condition)
        {
            return null;
        }
        var kind = FoodSource(food);
        var nutrition = Math.Clamp(condition.NutritionBasisPoints + (condition.LastMealKind is null ? 0 : condition.LastMealKind != kind ? 500 : -100), 0, 10_000);
        var illness = Math.Clamp(condition.IllnessBasisPoints +
            (food.FreshnessBasisPoints < 2_500 ? SpoiledMealIllnessIncreaseBasisPoints : -FreshMealIllnessReliefBasisPoints), 0, 10_000);
        AppendEvent("meal_eaten", $"{person.InhabitantId}:{kind}:nutrition={nutrition}");
        return condition with { NutritionBasisPoints = nutrition, LastMealKind = kind, IllnessBasisPoints = illness };
    }

    private static void ValidateSurvival(PrivateWorldRuntimeState state)
    {
        if (state.Survival is { } survival && (state.SchemaVersion < 6 || survival.ActivatedTick < 0 ||
            survival.ActivatedTick > state.Society.Society.WorldTick || survival.Fires.Count > (state.WorldSimulation?.Buildings.Count ?? 0) ||
            survival.Fires.Select(fire => fire.BuildingId).Distinct(StringComparer.Ordinal).Count() != survival.Fires.Count ||
            survival.Fires.Any(fire => fire.FuelUntilTick <= state.Society.Society.WorldTick || fire.FuelUntilTick - state.Society.Society.WorldTick > 120 ||
                state.WorldSimulation?.Buildings.Any(building => building.InstanceId == fire.BuildingId) != true)))
        {
            throw new InvalidDataException("The saved settlement survival state is invalid.");
        }
        foreach (var person in state.Inhabitants)
        {
            if (person.Survival is { } condition && (state.SchemaVersion < 6 || state.Survival is null ||
                condition.WarmthBasisPoints is < 0 or > 10_000 || condition.IllnessBasisPoints is < 0 or > 10_000 ||
                condition.NutritionBasisPoints is < 0 or > 10_000 || condition.LastMealKind is not (null or "crops" or "cooked" or "foraged" or "camp_rations" or "orchard")))
            {
                throw new InvalidDataException("The saved inhabitant survival condition is invalid.");
            }
        }
    }
}
