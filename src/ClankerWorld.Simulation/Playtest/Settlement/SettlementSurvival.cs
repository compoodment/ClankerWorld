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
    private static readonly HashSet<string> PerishableKinds = new(StringComparer.Ordinal)
        { "food", "fruit", "berries", "wild_greens", "cultivated_greens",
            "simple_meal", "bread", "porridge", "berry_porridge", "fruit_porridge", "stew", "restaurant_meal", "eggs", "milk", "cooked_eggs", "milk_porridge", "rich_meal" };
    private static readonly IReadOnlyDictionary<string, int> FoodSpoilageRates =
        new Dictionary<string, int>(StringComparer.Ordinal) { ["bread"] = 2, ["milk"] = 28 };
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
    // Provisional balance (#641, #673): how much colder full night is than
    // the same weather by day, in the per-tick exposure units weather uses.
    // Dusk and dawn fade it in and out. Shelter, clothing and fire offset it
    // exactly as they offset weather.
    private const int NightChillAtFullDarkness = 15;
    // Provisional balance: trees give about half a building's storm protection.
    private const int NaturalStormProtection = 22;

    private static bool NeedsUrgentFood(PlaytestInhabitantState person) => person.HungerBasisPoints < UrgentFullness;

    private bool NeedsUrgentWarmth(PlaytestInhabitantState person) =>
        person.Survival is { } condition && condition.WarmthBasisPoints < UrgentWarmth && WarmthChange(person) < 0;

    private bool ReadyForBriefInteraction(string actor) => inhabitants.TryGetValue(actor, out var person) &&
        !NeedsUrgentWarmth(person);

    private int WarmthChange(PlaytestInhabitantState person)
    {
        var naturalCover = WeatherAt(person.Position) == WeatherKind.Storm && NaturalStormCover(person.Position);
        var protection = ClothingProtection(person.InhabitantId, person.Position) +
            (NearShelter(person.InhabitantId, person.Position) ? 45 : naturalCover ? NaturalStormProtection : 0);
        var heat = AccessibleHeatingBuildings(person.InhabitantId).Any(building => IsFireLit(building) &&
            IsWithinInteractionRange(person.Position, building.Position,
                building.HouseholdId is null ? 2 : 0)) ? 90 : 0;
        var loss = Math.Max(0, OutdoorExposure(person.Position) - protection);
        return IsSwimming(person.InhabitantId, person.Position)
            ? -Math.Max(SwimmingRules.WarmthLossPerTick, loss - heat)
            : -loss + heat + (loss == 0 ? 20 : 0);
    }

    private bool IsProtectiveProject(SettlementProject? project) =>
        project is not null &&
        TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection) &&
        (selection.IsBuilding
            ? worldContent.Buildings.Any(building => selection.DefinitionId == building.CanonicalId &&
                building.Tags.Any(tag => tag is "shelter" or "warmth" or "cooking"))
            : worldContent.Recipes.Any(recipe => selection.DefinitionId == recipe.CanonicalId &&
                recipe.Outputs.Any(output => PersonalEquipmentRules.IsGarment(output.ResourceId))));

    private WeatherKind WeatherAt(GridPoint position) => WeatherRules.At(worldSystems, position, map.Height,
        WeatherRules.RegionClimate(map, position));

    /// <summary>The cold of being outdoors here now: weather, climate and season, plus night.</summary>
    private int OutdoorExposure(GridPoint position) => WeatherExposure(position) +
        NightChillAtFullDarkness * DaylightRules.DarknessBasisPoints(worldSystems) / DaylightRules.FullDarkness;

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
        PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null &&
        lot.ContainerLotId is null && lot.ItemKind == kind && AvailableLotQuantity(lot) > 0);

    /// <summary>Carried goods the agent owns, so may spend; borrowed household goods are excluded.</summary>
    private bool HasCarriedOwnItem(string actor, string kind) => society.Checkpoint.Inventory.Lots.Any(lot =>
        lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) && lot.DeliveryBuildingId is null &&
        lot.ContainerLotId is null && lot.ItemKind == kind && AvailableLotQuantity(lot) > 0);

    private InventoryLot? SharedItem(string kind, string actor, GridPoint? returnTo = null, int returnRange = 0) => society.Checkpoint.Inventory.Lots.FirstOrDefault(lot =>
        lot.OwnerId == HouseholdFor(actor) && lot.CarrierId is null && lot.ContainerLotId is null && lot.ItemKind == kind && AvailableLotQuantity(lot) > 0 &&
        (lot.StorageBuildingId is null || society.Checkpoint.GetInhabitant(actor).HouseholdId == lot.OwnerId) &&
        CanReachSharedItem(actor, lot) && (returnTo is null || PickupCarryCapacity(actor, lot, returnTo.Value, returnRange) >= 1)) ??
        (kind == "food" ? null : AvailableWarehouseStock(actor, kind).FirstOrDefault(lot =>
            returnTo is null || PickupCarryCapacity(actor, lot, returnTo.Value, returnRange) >= 1));

    private bool CanReachSharedItem(string actor, InventoryLot lot) =>
        !OnBorrowedMarketStall(lot) && FindUnoccupiedRoute(actor, inhabitants[actor].Position, HouseholdStockPosition(lot),
            HouseholdStockInteractionRange(lot)).Count > 0;

    private IEnumerable<PlacedBuilding> BuildingsWithTag(string tag) => worldSimulation.Buildings.Where(building =>
        worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains(tag, StringComparer.Ordinal)));

    private bool IsTownHall(PlacedBuilding building) => worldContent.Buildings.Any(definition =>
        definition.CanonicalId == building.DefinitionId && definition.Tags.Contains(TownHallContent.HallTag, StringComparer.Ordinal));

    private bool IsTownHallStormRefuge(string actor, PlacedBuilding building) =>
        IsTownHall(building) && building.TownId is { } townId && TownForResident(actor) == townId &&
        !HasHome(actor) && WeatherAt(building.Position) == WeatherKind.Storm;

    private IEnumerable<PlacedBuilding> AccessibleShelters(string actor, bool includeStormRefuge = true) => BuildingsWithTag("shelter")
        .Where(building => building.HouseholdId is null ||
            building.HouseholdId == society.Checkpoint.GetInhabitant(actor).HouseholdId
            || WeatherAt(building.Position) == WeatherKind.Storm && HasHouseGuestInvitation(actor, building.InstanceId))
        .Concat(includeStormRefuge ? BuildingsWithTag(TownHallContent.HallTag).Where(building => IsTownHallStormRefuge(actor, building)) : [])
        .DistinctBy(building => building.InstanceId);

    private bool NearShelter(string actor, GridPoint point) => AccessibleShelters(actor).Any(building =>
        ShelterBuildingCovers(actor, building, point));

    private int WarmthDestinationRange(string actor, GridPoint origin, PlacedBuilding building) =>
        !IsSwimming(actor, origin) && building.HouseholdId is null && !IsTownHall(building) ? ResourceInteractionRange : 0;

    private GridPoint? ReachableHallShelterPoint(string actor, GridPoint origin, PlacedBuilding hall) =>
        WorldContentSimulationRules.Footprint(worldContent.Buildings.Single(definition => definition.CanonicalId == hall.DefinitionId), hall)
            .Where(point => map.IsPassable(point) && ShelterRouteIsOpen(actor, origin, point))
            .OrderBy(point => map.FootDistance(origin, point)).ThenBy(point => point.Y).ThenBy(point => point.X)
            .Select(point => (GridPoint?)point).FirstOrDefault();

    private GridPoint? WarmthDestinationPoint(string actor, PlaytestInhabitantState person, PlacedBuilding building) =>
        IsTownHall(building) ? ReachableHallShelterPoint(actor, person.Position, building) : building.Position;

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

    private PlacedBuilding? ReachableUnlitHearth(string actor, PlaytestInhabitantState person) =>
        AccessibleHeatingBuildings(actor).FirstOrDefault(building =>
        {
            if (IsFireLit(building)) return false;
            var range = building.HouseholdId is null ? ResourceInteractionRange : 0;
            return IsWithinInteractionRange(person.Position, building.Position, range) ||
                FindUnoccupiedRoute(actor, person.Position, building.Position, range).Count > 0;
        });

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
            PerishableKinds, shelteredOwners, FoodSpoilageRates));
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
        AddEquipmentCandidates(candidates, actor, person);
        AddOrnamentCandidates(candidates, actor);
        var losingWarmth = WarmthChange(person) < 0;
        if (AdultResident(actor) && losingWarmth && condition.WarmthBasisPoints < ComfortableWarmth &&
            ReachableUnlitHearth(actor, person) is { } hearth &&
            (HasCarriedOwnItem(actor, "wood") || FreeCarryCapacity(actor) > 0 && SharedItem("wood", actor,
                hearth.Position, hearth.HouseholdId is null ? ResourceInteractionRange : 0) is not null ||
                MaterialSource("wood", actor, hearth.Position, hearth.HouseholdId is null ? ResourceInteractionRange : 0) is { } firewood &&
                FreeCarryCapacity(actor) >= ProjectMaterialCarryUnits(actor, "wood", firewood)))
        {
            candidates.Add(new CognitionCandidate("tend_fire", "Carry wood to an unlit hearth and keep it burning for warmth.", NeedsUrgentWarmth(person) ? 1 : 2));
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

    private void CollectEquipment(string actor, PlaytestInhabitantState person, string kind,
        GridPoint? returnTo = null, int returnRange = 0)
    {
        if (FreeCarryCapacity(actor) <= 0 || HasCarriedEquipmentAtLeast(actor, kind) ||
            SharedItem(kind, actor, returnTo, returnRange) is not { ContainerLotId: null } item)
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
        // Every household work tool, of any tier, is borrowed rather than handed over.
        var borrowedTool = item.ItemKind == "tool" || ToolProgressionRules.Find(item.ItemKind) is not null;
        ApplyInventoryTransition(inventory => borrowedTool && item.OwnerId == society.Checkpoint.GetInhabitant(actor).HouseholdId
            ? InventoryFixture.Relocate(inventory, $"equipment:{WorldTick}:{actor}:{kind}", item.Id, item.OwnerId, 1, actor)
            : InventoryFixture.Transfer(inventory, $"equipment:{WorldTick}:{actor}:{kind}",
                item.OwnerId, actor, item.Id, 1, "equipment_collected"));
        AppendEvent("equipment_collected", $"{actor}:{kind}");
    }

    private bool HasCarriedEquipmentAtLeast(string actor, string kind)
    {
        if (ToolProgressionRules.Find(kind) is not { } requested)
            return HasCarriedOwnItem(actor, kind);
        var carried = ToolProgressionRules.BestUsableTool(society.Checkpoint.Inventory, actor, requested.Family);
        return carried is not null && ToolProgressionRules.Find(carried.ItemKind)!.Tier >= requested.Tier;
    }

    private sealed record FireFuelEffect(string BuildingId, string FuelReservationId);

    private void TendFire(string actor, PlaytestInhabitantState person)
    {
        var building = ReachableUnlitHearth(actor, person);
        if (building is not null) _ = TendFireAt(actor, person, building);
    }

    private FireFuelEffect? TendFireAt(string actor, PlaytestInhabitantState person, PlacedBuilding building)
    {
        if (survivalState is null || IsFireLit(building) ||
            !AccessibleHeatingBuildings(actor).Any(item => item.InstanceId == building.InstanceId))
            return null;
        if (!HasCarriedOwnItem(actor, "wood"))
        {
            var range = building.HouseholdId is null ? ResourceInteractionRange : 0;
            if (SharedItem("wood", actor, building.Position, range) is not null)
                CollectEquipment(actor, person, "wood", building.Position, range);
            else if (MaterialSource("wood", actor, building.Position, range) is { } source)
                GatherProjectMaterial(actor, person, "wood", source, returnTo: building.Position, returnRange: range);
            return null;
        }
        var interactionRange = building.HouseholdId is null ? ResourceInteractionRange : 0;
        if (!IsWithinInteractionRange(person.Position, building.Position, interactionRange))
        {
            MoveToward(actor, person, building.Position, "fuel_fire", interactionRange);
            return null;
        }
        var fuel = society.Checkpoint.Inventory.Lots.First(lot => lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) &&
            lot.DeliveryBuildingId is null && lot.ContainerLotId is null && lot.ItemKind == "wood" && AvailableLotQuantity(lot) > 0);
        var previousReservations = society.Checkpoint.Inventory.Reservations.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var result = society.Apply(checkpoint => ClankerWorld.Simulation.Society.SocietyFixture.ConsumeInventory(checkpoint, actor, fuel.Id, 1, "heating_fuel"));
        var payment = result.Checkpoint.Inventory.Reservations.Single(item => !previousReservations.Contains(item.Id) &&
            item.OwnerId == actor && item.LotId == fuel.Id && item.Quantity == 1 && item.Purpose == "heating_fuel" &&
            item.State == InventoryReservationState.Completed && item.ExpiryTick == WorldTick);
        survivalState = survivalState with { Fires = survivalState.Fires.Append(new CampFireState(building.InstanceId, WorldTick + 120)).ToArray() };
        AppendEvent("fire_fuelled", building.InstanceId);
        return new FireFuelEffect(building.InstanceId, payment.Id);
    }

    private IEnumerable<PlacedBuilding> ReachableWarmthDestinations(string actor, PlaytestInhabitantState person) =>
        AccessibleHeatingBuildings(actor).Where(IsFireLit).Concat(AccessibleShelters(actor))
            .DistinctBy(building => building.InstanceId)
            .Where(building => !(CanUseLitHearth(actor, building)
                    ? IsWithinInteractionRange(person.Position, building.Position, WarmthDestinationRange(actor, person.Position, building))
                    : ShelterBuildingCovers(actor, building, person.Position)) &&
                WarmthDestinationPoint(actor, person, building) is { } target &&
                FindUnoccupiedRoute(actor, person.Position, target, WarmthDestinationRange(actor, person.Position, building)).Count > 0)
            .OrderBy(building => CanUseLitHearth(actor, building) ? 0 : 1)
            .ThenBy(building => map.FootDistance(person.Position, building.Position));

    private bool CanUseLitHearth(string actor, PlacedBuilding building) => IsFireLit(building) &&
        AccessibleHeatingBuildings(actor).Any(hearth => hearth.InstanceId == building.InstanceId);

    private void SeekWarmth(string actor, PlaytestInhabitantState person)
    {
        // Keep protection that already stops cooling, even if the original travel target disappeared.
        if (WarmthChange(person) >= 0)
            return;
        var destination = ReachableWarmthDestinations(actor, person).FirstOrDefault();
        // Keep building cover unless another destination can supply usable heat.
        if (NearShelter(actor, person.Position) && (destination is null || !CanUseLitHearth(actor, destination)))
            return;
        // Buildings provide more protection than natural cover, even without a lit hearth.
        if (WeatherAt(person.Position) == WeatherKind.Storm && NaturalStormCover(person.Position) &&
            destination is null)
            return;
        var cover = destination is null && WeatherAt(person.Position) == WeatherKind.Storm && !NaturalStormCover(person.Position)
            ? NearbyNaturalStormCover(actor, person.Position) : null;
        if (cover is { } coverPoint)
        {
            if (person.Position != coverPoint)
                MoveToward(actor, person, coverPoint, "storm_cover");
        }
        else if (destination is not null && WarmthDestinationPoint(actor, person, destination) is { } target &&
                 !IsWithinInteractionRange(person.Position, target, WarmthDestinationRange(actor, person.Position, destination)))
        {
            MoveToward(actor, person, target, "warmth", WarmthDestinationRange(actor, person.Position, destination));
        }
    }

    private bool NeedsRecipeOutput(RecipeDefinition recipe, string? ownerId = null, string? requestWorker = null) =>
        HouseToolsContent.IsCrudeToolRecipe(recipe) ? NeedsHouseTool(recipe, ownerId) :
        HasToolMakingDemand(recipe, ownerId, requestWorker) || recipe.Outputs.Any(output =>
    {
        if (output.ResourceId == KnowledgeContent.Paper)
            return NeedsKnowledgePaper(ownerId);
        if (survivalState is null)
            return true;
        var available = society.Checkpoint.Inventory.Lots.Where(lot => lot.ItemKind == output.ResourceId &&
                (ownerId is null || lot.OwnerId == ownerId))
            .Sum(AvailableLotQuantity);
        var residentCount = ownerId is null ? inhabitants.Count :
            inhabitants.Values.Count(person => society.Checkpoint.GetInhabitant(person.InhabitantId).HouseholdId == ownerId);
        if (IsPreparedMeal(output.ResourceId) && !recipe.Inputs.Any(input => IsPreparedMeal(input.ResourceId)))
            available = society.Checkpoint.Inventory.Lots.Where(lot => IsPreparedMeal(lot.ItemKind) &&
                    (ownerId is null || lot.OwnerId == ownerId)).Sum(AvailableLotQuantity);
        var target = IsPreparedMeal(output.ResourceId) ? Math.Max(2, residentCount * 2)
            : output.ResourceId == "food" ? inhabitants.Count * 4 : Math.Max(1, inhabitants.Count);
        return available < target;
    });

    /// <summary>One usable family tool per adult, counting better tools and work already paid for.</summary>
    private bool NeedsHouseTool(RecipeDefinition recipe, string? householdId)
    {
        if (householdId is null) return false;
        var adults = inhabitants.Keys.Where(actor => AdultResident(actor) && HouseholdFor(actor) == householdId).ToArray();
        if (adults.Length == 0) return false;
        var family = ToolProgressionRules.Find(recipe.Outputs[0].ResourceId)!.Family;
        var tools = society.Checkpoint.Inventory.Lots.Where(lot => lot.ContainerLotId is null &&
            lot.DeliveryBuildingId is null && ToolProgressionRules.Find(lot.ItemKind)?.Family == family &&
            AvailableLotQuantity(lot) > 0).ToArray();
        // Private extras and borrowed tools held by the same adult cover only that adult.
        var uncovered = adults.Where(actor => !tools.Any(lot =>
            lot.OwnerId == householdId && ToolProgressionRules.IsTopLevelCarriedLot(lot, actor) ||
            lot.OwnerId == actor && (ToolProgressionRules.IsTopLevelCarriedLot(lot, actor) ||
                lot.CarrierId is null && CanReachHouseToolStock(actor, householdId, lot)))).ToArray();
        if (uncovered.Length == 0) return false;
        // Free shared stock can wait for an adult's route to reopen; do not keep making
        // replacements merely because that adult is temporarily away from the household.
        var available = tools.Where(lot => lot.OwnerId == householdId && lot.CarrierId is null &&
                adults.Any(actor => CanReachHouseToolStock(actor, householdId, lot)))
            .Sum(lot => (long)AvailableLotQuantity(lot));
        if (available >= uncovered.Length) return false;
        var committed = worldSimulation.ProductionJobs.Where(job => job.OwnerId == householdId &&
                job.ToolMakingRequestId is null &&
                job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused &&
                worldSimulation.Buildings.Any(building => building.InstanceId == job.BuildingInstanceId &&
                    building.HouseholdId == householdId && adults.Any(actor =>
                        inhabitants[actor].Position == building.Position ||
                        FindUnoccupiedRoute(actor, inhabitants[actor].Position, building.Position, 0).Count > 0)))
            .SelectMany(job => worldContent.Recipes.FirstOrDefault(item => item.CanonicalId == job.RecipeId)?.Outputs ?? [])
            .Where(output => ToolProgressionRules.Find(output.ResourceId)?.Family == family)
            .Sum(output => (long)output.Amount);
        return available + committed < uncovered.Length;
    }

    private bool CanReachHouseToolStock(string actor, string householdId, InventoryLot lot)
    {
        if (OnBorrowedMarketStall(lot))
            return false;
        if (lot.StorageBuildingId is { } storage && !worldSimulation.Buildings.Any(building =>
                building.InstanceId == storage && building.HouseholdId == householdId))
            return false;
        var position = HouseholdStockPosition(lot);
        var range = HouseholdStockInteractionRange(lot);
        return IsWithinInteractionRange(inhabitants[actor].Position, position, range) ||
            FindUnoccupiedRoute(actor, inhabitants[actor].Position, position, range).Count > 0;
    }

    private string FoodSource(InventoryLot lot)
    {
        if (IsPreparedMeal(lot.ItemKind)) return lot.ItemKind;
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
        if (FarmFieldRules.IsCrop(lot.ItemKind)) return "crops";
        return originId.StartsWith("food:harvest:", StringComparison.Ordinal) ? "foraged" : "camp_rations";
    }

    private static bool IsPreparedMeal(string kind) => kind is "simple_meal" or "porridge" or
        "berry_porridge" or "fruit_porridge" or "bread" or "stew" or "restaurant_meal" or "cooked_eggs" or "milk_porridge" or "rich_meal";

    private static bool IsEdibleFood(string kind) => IsPreparedMeal(kind) ||
        kind is "food" or "fruit" or "berries" or "wild_greens" or "cultivated_greens";

    private static int FoodNourishment(string kind) => kind switch
    {
        "restaurant_meal" => 6_000,
        "rich_meal" => 7_000,
        "cooked_eggs" => 4_000,
        "milk_porridge" => 5_000,
        "berry_porridge" or "fruit_porridge" or "stew" => 5_000,
        "simple_meal" or "porridge" or "bread" => 4_000,
        "berries" => 2_000,
        "wild_greens" => 1_500,
        "cultivated_greens" => 4_000,
        _ => 3_000,
    };

    private IEnumerable<InventoryLot> PreferredFood(string owner, string? actor = null)
    {
        var previous = actor is not null && inhabitants.TryGetValue(actor, out var person) ? person.Survival?.LastMealKind : null;
        var inventory = society.Checkpoint.Inventory;
        return inventory.Lots.Where(lot => lot.OwnerId == owner && IsEdibleFood(lot.ItemKind) &&
                (owner != actor ? lot.ContainerLotId is null && lot.CarrierId is null :
                    UsablePersonalFood(inventory, lot, actor!)) &&
                AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => previous is not null && FoodSource(lot) == previous ? 1 : 0)
            .ThenByDescending(lot => lot.FreshnessBasisPoints).ThenBy(lot => lot.Id, StringComparer.Ordinal);
    }

    private static bool UsablePersonalFood(InventoryCheckpoint inventory, InventoryLot food, string actor)
    {
        if (food.DeliveryBuildingId is not null) return false;
        if (food.ContainerLotId is not { } containerId)
            return PersonalEquipmentRules.IsCarried(food, actor);
        return inventory.Lots.Any(pot => pot.Id == containerId && pot.OwnerId == actor &&
            pot.ItemKind == InventoryContainerRules.StoragePot && pot.ConditionBasisPoints > 0 &&
            pot.DeliveryBuildingId is null && PersonalEquipmentRules.IsCarried(pot, actor) &&
            !HasActiveContainerReservation(inventory, pot.Id));
    }

    private SurvivalCondition? AfterMeal(PlaytestInhabitantState person, InventoryLot food)
    {
        if (person.Survival is not { } condition)
        {
            return null;
        }
        var kind = FoodSource(food);
        var nutrition = Math.Clamp(condition.NutritionBasisPoints + (condition.LastMealKind is null ? 0 : condition.LastMealKind != kind ? 500 : -100) +
            (food.ItemKind == "restaurant_meal" ? 500 : food.ItemKind is "berry_porridge" or "fruit_porridge" ? 250 : 0), 0, 10_000);
        var illness = Math.Clamp(condition.IllnessBasisPoints +
            (food.FreshnessBasisPoints < 2_500 ? SpoiledMealIllnessIncreaseBasisPoints : -FreshMealIllnessReliefBasisPoints), 0, 10_000);
        AppendEvent("meal_eaten", $"{person.InhabitantId}:{kind}:nutrition={nutrition}");
        return condition with { NutritionBasisPoints = nutrition, LastMealKind = kind, IllnessBasisPoints = illness };
    }

    private static void ValidateSurvival(PrivateWorldRuntimeState state)
    {
        if (state.Survival is { } survival && (survival.ActivatedTick < 0 ||
            survival.ActivatedTick > state.Society.Society.WorldTick || survival.Fires.Count > (state.WorldSimulation?.Buildings.Count ?? 0) ||
            survival.Fires.Select(fire => fire.BuildingId).Distinct(StringComparer.Ordinal).Count() != survival.Fires.Count ||
            survival.Fires.Any(fire => fire.FuelUntilTick <= state.Society.Society.WorldTick || fire.FuelUntilTick - state.Society.Society.WorldTick > 120 ||
                state.WorldSimulation?.Buildings.Any(building => building.InstanceId == fire.BuildingId) != true)))
        {
            throw new InvalidDataException("The saved settlement survival state is invalid.");
        }
        foreach (var person in state.Inhabitants)
        {
            if (person.Survival is { } condition && (state.Survival is null ||
                condition.WarmthBasisPoints is < 0 or > 10_000 || condition.IllnessBasisPoints is < 0 or > 10_000 ||
                condition.NutritionBasisPoints is < 0 or > 10_000 || condition.LastMealKind is not (null or "crops" or "cooked" or "foraged" or "camp_rations" or "orchard") &&
                !IsPreparedMeal(condition.LastMealKind!)))
            {
                throw new InvalidDataException("The saved inhabitant survival condition is invalid.");
            }
        }
    }
}
