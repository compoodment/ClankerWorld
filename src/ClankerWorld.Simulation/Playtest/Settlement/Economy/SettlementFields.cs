using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public enum FarmFieldStage { Tilling, Prepared, Planted, Growing, Ready, Harvested }

public sealed record FarmFieldTile(GridPoint Position, string HouseholdId, FarmFieldStage Stage,
    long PreparedTick, int WorkDone = 0, long LastWorkedTick = -1, string? RecipeId = null,
    string? JobId = null, IReadOnlyList<ContentQuantity>? Harvest = null, int TendingWork = 0);

public sealed record FarmActionResult(bool Applied, bool Completed, string? Failure);

public sealed partial class PrivateWorldRuntime
{
    // All field sizes, work times and reserves are provisional playtest balance.
    private const int TillingWork = 8;
    private const int HarvestWork = 4;
    private static readonly string[] CropStockKinds = ["grain", "potatoes", "cultivated_greens"];

    private IReadOnlyList<FarmFieldTile> FarmFields => worldSimulation.Fields ?? [];

    private void SetField(FarmFieldTile field) => worldSimulation = worldSimulation with
    {
        Fields = FarmFields.Where(item => item.Position != field.Position).Append(field)
            .OrderBy(item => item.Position.Y).ThenBy(item => item.Position.X).ToArray(),
    };

    private InventoryLot? FarmHoe(string actor) => CarriedTool(actor, ToolKind.Hoe);

    private InventoryLot? HarvestTool(string actor) => CarriedTool(actor, ToolKind.Sickle) ?? FarmHoe(actor);

    private string? FieldPermissionFailure(string actor, GridPoint point, bool newTile)
    {
        if (!AdultResident(actor) || !inhabitants.TryGetValue(actor, out var worker) ||
            society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } household ||
            newTile && FarmhouseForHousehold(household) is null)
            return "An adult from the household holding a Farmhouse must do the field work.";
        var farmhouse = FarmhouseForHousehold(household);
        if (!LandFertilityRules.IsFarmable(map, point)) return "Crops cannot grow on this ground.";
        if (FarmFields.FirstOrDefault(field => field.Position == point) is { } field && field.HouseholdId != household)
            return "This field belongs to another household.";
        if (newTile && (RoadAndBridgeTiles().Contains(point) ||
            (worldSimulation.BuildingExpansions ?? []).Any(job => job.State == WorldProductionJobState.Running && ExpansionTiles(job).Contains(point)) ||
            map.CampObjects.Any(item => item.Position == point) ||
            map.Resources.Any(item => item.Position == point) || worldSimulation.Buildings.Any(building =>
                WorldContentSimulationRules.Footprint(worldContent.Buildings.Single(definition =>
                    definition.CanonicalId == building.DefinitionId), building).Contains(point))))
            return "Choose free ground away from Roads, buildings and resource objects.";
        if (farmhouse is not null && towns.Any(town => town.Id != farmhouse.TownId && town.BorderTiles.Contains(point)))
            return "This ground belongs to another Town.";
        if (farmhouse?.TownId is { } townId && towns.FirstOrDefault(town => town.Id == townId) is { } ownTown &&
            !TownBorderRules.IsWithinOrAdjacent(ownTown, point, 1, 1))
            return "Choose ground in or beside the Farmhouse's Town.";
        if (!map.IsReachableOnFoot(worker.Position, point)) return "The field cannot be reached on foot.";
        return null;
    }

    public FarmActionResult TillField(string actor, GridPoint point)
    {
        gate.Wait();
        try { return TillFieldCore(actor, point); }
        finally { gate.Release(); }
    }

    private FarmActionResult TillFieldCore(string actor, GridPoint point)
    {
        if (FieldPermissionFailure(actor, point, true) is { } failure) return new(false, false, failure);
        if (inhabitants[actor].Position != point) return new(false, false, "Stand on the chosen tile to till it.");
        if (FarmHoe(actor) is not { } hoe) return new(false, false, "Carry a usable hoe to prepare the ground.");
        var field = FarmFields.FirstOrDefault(item => item.Position == point);
        if (field is not null && field.Stage != FarmFieldStage.Tilling)
            return new(false, false, "This tile has already been tilled.");
        field ??= new FarmFieldTile(point, HouseholdFor(actor), FarmFieldStage.Tilling, WorldTick);
        if (field.LastWorkedTick == WorldTick) return new(false, false, "This tile has already been worked this turn.");
        if (!SettlementIllnessRules.AllowsWork(actor, WorldTick, inhabitants[actor].Survival?.IllnessBasisPoints ?? 0))
            return new(false, false, "Illness slowed this turn's field work.");
        var work = Math.Min(TillingWork, field.WorkDone + ToolCapabilities.ForItem(hoe.ItemKind)!.WorkQuantity);
        UseTool(actor, ToolKind.Hoe);
        SetField(field with
        {
            WorkDone = work,
            LastWorkedTick = WorldTick,
            Stage = work == TillingWork ? FarmFieldStage.Prepared : FarmFieldStage.Tilling
        });
        if (work == TillingWork)
        {
            if (FarmhouseForHousehold(field.HouseholdId)?.TownId is { } townId)
            {
                var town = towns.Single(item => item.Id == townId);
                SetTown(town with { BorderTiles = TownBorderRules.Expand(map, town, [point]) });
            }
            AppendEvent("field_prepared", $"{actor}:{point.X},{point.Y}:{field.HouseholdId}");
            CreditCompletedWork(actor, "farming");
        }
        checkpointSchemaVersion = StateSchemaVersion;
        return new(true, work == TillingWork, null);
    }

    public FarmActionResult HarvestField(string actor, GridPoint point)
    {
        gate.Wait();
        try { return HarvestFieldCore(actor, point); }
        finally { gate.Release(); }
    }

    private FarmActionResult HarvestFieldCore(string actor, GridPoint point)
    {
        if (FieldPermissionFailure(actor, point, false) is { } failure) return new(false, false, failure);
        if (inhabitants[actor].Position != point) return new(false, false, "Stand in the field to harvest it.");
        if (FarmFields.FirstOrDefault(item => item.Position == point) is not { Stage: FarmFieldStage.Ready, Harvest: not null } field)
            return new(false, false, "The crop is not ready to harvest.");
        if (field.LastWorkedTick == WorldTick) return new(false, false, "This field has already been worked this turn.");
        if (HarvestTool(actor) is not { } harvestTool) return new(false, false, "Carry a usable hoe or sickle to harvest the crop.");
        if (!SettlementIllnessRules.AllowsWork(actor, WorldTick, inhabitants[actor].Survival?.IllnessBasisPoints ?? 0))
            return new(false, false, "Illness slowed this turn's field work.");
        var capability = ToolCapabilities.ForItem(harvestTool.ItemKind)!;
        var work = Math.Min(HarvestWork, field.WorkDone + (capability.Kind == ToolKind.Sickle ? capability.WorkQuantity : 1));
        UseTool(actor, capability.Kind);
        if (work < HarvestWork)
        {
            SetField(field with { WorkDone = work, LastWorkedTick = WorldTick });
            return new(true, false, null);
        }
        ApplyInventoryTransition(inventory =>
        {
            for (var index = 0; index < field.Harvest.Count; index++)
            {
                var output = field.Harvest[index];
                inventory = InventoryFixture.AddLot(inventory, $"{field.JobId}:output:{index:D2}",
                    output.ResourceId, field.HouseholdId, output.Amount, WorldTick,
                    groundPosition: new InventoryGroundPosition(point.X, point.Y));
            }
            return inventory;
        });
        SetField(field with { Stage = FarmFieldStage.Harvested, WorkDone = 0, LastWorkedTick = WorldTick, Harvest = null });
        AppendEvent("field_harvested", $"{actor}:{point.X},{point.Y}:{field.JobId}");
        CreditCompletedWork(actor, "farming");
        return new(true, true, null);
    }

    private bool PrepareFieldHarvest(WorldProductionJob job, RecipeDefinition recipe, long targetTick)
    {
        if (!WorldBuildSiteRules.TryGetFieldPosition(job.BuildingInstanceId, out var point) ||
            FarmFields.FirstOrDefault(field => field.Position == point && field.JobId == job.JobId) is not { } field)
            return false;
        var inventory = society.Checkpoint.Inventory;
        var planted = recipe.Tags.Contains("farm-crop", StringComparer.Ordinal);
        if (job.InputReservationIds.Any(id => planted ? inventory.GetReservation(id).State != InventoryReservationState.Completed :
            inventory.GetReservation(id) is not
            { State: InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed } reservation ||
            inventory.GetLot(reservation.LotId) is not { FreshnessBasisPoints: > 0, ConditionBasisPoints: > 0 }))
            return false;
        ApplyInventoryTransition(current =>
        {
            foreach (var id in job.InputReservationIds)
                if (current.GetReservation(id).State != InventoryReservationState.Completed)
                    current = InventoryFixture.ConsumeReservation(current, id);
            return current;
        });
        var weather = WeatherAt(point);
        var moisture = WeatherRules.SoilMoistureAt(worldSystems, point, map.Height, WeatherRules.RegionClimate(map, point));
        var harvest = recipe.Outputs.Select(output => new ContentQuantity(output.ResourceId,
            FoodItems.IsPlantingStock(output.ResourceId) && output.ResourceId != "potatoes" ? output.Amount :
                Math.Max(1, CropOutputQuantity(recipe, output, weather, moisture) *
                    LandFertilityRules.YieldPercent(LandFertilityRules.At(map, point)) / 100))).ToArray();
        SetField(field with { Stage = FarmFieldStage.Ready, WorkDone = 0, Harvest = harvest });
        if (survivalState is not null && weather is WeatherKind.Snow or WeatherKind.Storm)
            AppendEvent("crop_weather_loss", $"{job.JobId}:{weather.ToString().ToLowerInvariant()}");
        if (survivalState is not null && weather is not (WeatherKind.Snow or WeatherKind.Storm) &&
            (moisture < 15 || moisture >= 50))
            AppendEvent("crop_moisture_effect", $"{job.JobId}:{(moisture < 15 ? "dry" : "wet")}:{moisture}");
        AppendEvent("crop_ready", $"{job.JobId}:{point.X},{point.Y}");
        CreditCompletedWork(job.WorkerId, "farming");
        return true;
    }

    public FarmActionResult TendField(string actor, GridPoint point)
    {
        gate.Wait();
        try { return TendFieldCore(actor, point); }
        finally { gate.Release(); }
    }

    private FarmActionResult TendFieldCore(string actor, GridPoint point)
    {
        if (FieldPermissionFailure(actor, point, false) is { } failure) return new(false, false, failure);
        if (inhabitants[actor].Position != point || FarmHoe(actor) is null)
            return new(false, false, "Stand in the field with a usable hoe to tend it.");
        if (FarmFields.FirstOrDefault(field => field.Position == point) is not
            { Stage: FarmFieldStage.Growing, TendingWork: < 4 } field || field.LastWorkedTick == WorldTick)
            return new(false, false, "This crop does not need more tending now.");
        if (!SettlementIllnessRules.AllowsWork(actor, WorldTick, inhabitants[actor].Survival?.IllnessBasisPoints ?? 0))
            return new(false, false, "Illness slowed this turn's field work.");
        var capability = UseTool(actor, ToolKind.Hoe)!;
        var tending = Math.Min(4, field.TendingWork + capability.WorkQuantity);
        SetField(field with { TendingWork = tending, LastWorkedTick = WorldTick });
        worldSimulation = worldSimulation with
        {
            CropBuilds = (worldSimulation.CropBuilds ?? []).Select(job => job.JobId == field.JobId
                ? job with { CompletionTick = Math.Max(WorldTick + 1, job.CompletionTick - (tending - field.TendingWork)) } : job).ToArray(),
        };
        AppendEvent("field_tended", $"{actor}:{point.X},{point.Y}");
        return new(true, tending == 4, null);
    }

    private bool CanAffordTending(string actor, string household)
    {
        if (FarmHoe(actor) is not { } hoe) return false;
        var growing = FarmFields.Count(field => field.HouseholdId == household &&
            field.Stage is FarmFieldStage.Planted or FarmFieldStage.Growing or FarmFieldStage.Ready);
        if (CarriedTool(actor, ToolKind.Sickle) is { } sickle)
        {
            var harvest = ToolCapabilities.ForItem(sickle.ItemKind)!;
            if (sickle.ConditionBasisPoints >= (HarvestWork + harvest.WorkQuantity - 1) /
                harvest.WorkQuantity * growing * harvest.WearPerUse) return true;
        }
        return hoe.ConditionBasisPoints >= (HarvestWork * growing + 1) * ToolCapabilities.ForItem(hoe.ItemKind)!.WearPerUse;
    }

    private void MaintainFields(long targetTick)
    {
        foreach (var field in FarmFields.ToArray())
        {
            var job = (worldSimulation.CropBuilds ?? []).FirstOrDefault(item => item.JobId == field.JobId);
            if (field.Stage == FarmFieldStage.Planted && job?.StartedTick < targetTick)
                SetField(field with { Stage = FarmFieldStage.Growing });
            else if (field.Stage is FarmFieldStage.Planted or FarmFieldStage.Growing &&
                job?.State == WorldProductionJobState.Cancelled)
                SetField(field with { Stage = FarmFieldStage.Prepared, RecipeId = null, JobId = null, WorkDone = 0 });
        }
    }

    private long HouseholdCropStock(string household, string kind) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.ItemKind == kind && (lot.OwnerId == household ||
            inhabitants.ContainsKey(lot.OwnerId) && HouseholdFor(lot.OwnerId) == household))
        .Sum(lot => (long)AvailableLotQuantity(lot));

    /// <summary>Planting reserve is kept before trading surplus; potatoes are also their own seed.</summary>
    public int FarmPlantingReserve(string household, string kind) => FoodItems.IsPlantingStock(kind)
        ? Math.Max(1, FarmFields.Count(field => field.HouseholdId == household &&
            (kind == "potatoes" ? worldContent.Recipes.Any(recipe => recipe.CanonicalId == field.RecipeId &&
                recipe.Inputs.Any(input => input.ResourceId == kind)) : true))) : 0;

    private RecipeDefinition? PlannedCrop(string household) => worldContent.Recipes
        .Where(recipe => recipe.IsCrop && recipe.Tags.Contains("farm-crop", StringComparer.Ordinal) &&
            recipe.Inputs.All(input => HouseholdCropStock(household, input.ResourceId) >= input.Amount))
        .OrderBy(recipe => HouseholdCropStock(household, recipe.Outputs[0].ResourceId))
        .ThenBy(recipe => recipe.LocalId, StringComparer.Ordinal).FirstOrDefault();

    private GridPoint? PlannedFieldTile(string actor, PlacedBuilding farmhouse)
    {
        var town = towns.FirstOrDefault(item => item.Id == farmhouse.TownId);
        var candidates = town is null ? map.Tiles.Select(tile => tile.Position) :
            TownLayoutContext.CandidateBounds(map, town);
        return candidates.Where(point => !FarmFields.Any(field => field.Position == point) &&
                FieldPermissionFailure(actor, point, true) is null)
            .OrderByDescending(point => LandFertilityRules.At(map, point))
            .ThenBy(point => map.FootDistance(point, farmhouse.Position))
            .ThenBy(point => point.Y).ThenBy(point => point.X)
            .Cast<GridPoint?>().FirstOrDefault(point => point is { } position &&
                FindUnoccupiedRoute(actor, inhabitants[actor].Position, position, 0).Count > 0);
    }

    private void AddFieldCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState state)
    {
        if (society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } household ||
            FarmhouseForHousehold(household) is not { } farmhouse || CarriedHouseDelivery(actor) is not null) return;
        var ground = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == household &&
                lot.GroundPosition is not null && AvailableLotQuantity(lot) > 0)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
        if (ground is not null)
            candidates.Add(new("farm:collect", "Carry the household's harvested crop from its field into storage.", 16));
        if (FarmHoe(actor) is null)
        {
            if (SharedTool(actor, ToolKind.Hoe) is { } sharedHoe)
                candidates.Add(new("farm:hoe:" + sharedHoe.ItemKind, "Collect a hoe for household field work.", 23));
            if (HarvestTool(actor) is not null && FarmFields.FirstOrDefault(field =>
                field.HouseholdId == household && field.Stage == FarmFieldStage.Ready) is { } ready)
                candidates.Add(new($"farm:harvest:{ready.Position.X},{ready.Position.Y}", "Harvest the ripe crop with a sickle.", 17));
            return;
        }
        if (CanAffordTending(actor, household) && FarmFields.FirstOrDefault(field => field.HouseholdId == household &&
            field.Stage == FarmFieldStage.Growing && field.TendingWork < 4) is { } growing)
            candidates.Add(new($"farm:tend:{growing.Position.X},{growing.Position.Y}",
                "Tend the growing household crop with a hoe.", 18));
        var field = FarmFields.Where(item => item.HouseholdId == household && item.Stage is
                FarmFieldStage.Tilling or FarmFieldStage.Ready or FarmFieldStage.Prepared or FarmFieldStage.Harvested)
            .OrderBy(item => item.Stage == FarmFieldStage.Ready ? 0 : item.Stage == FarmFieldStage.Tilling ? 1 : 2)
            .ThenBy(item => map.FootDistance(state.Position, item.Position)).FirstOrDefault();
        if (field is not null)
        {
            var action = field.Stage == FarmFieldStage.Ready ? "harvest" : field.Stage == FarmFieldStage.Tilling ? "till" : "plant";
            if (action != "plant" || PlannedCrop(household) is not null && !society.Checkpoint.Inventory.Lots.Any(lot =>
                    lot.Quantity > 0 && lot.GroundPosition == new InventoryGroundPosition(field.Position.X, field.Position.Y)))
                candidates.Add(new($"farm:{action}:{field.Position.X},{field.Position.Y}",
                     $"{(action == "till" ? "Prepare" : action == "plant" ? "Plant" : "Harvest")} the household field.", action == "harvest" ? 17 : 22));
        }
        var population = society.Checkpoint.Inhabitants.Count(person => person.HouseholdId == household &&
            person.Status == SocietyInhabitantStatus.Active);
        var stock = CropStockKinds.Sum(kind => HouseholdCropStock(household, kind));
        var expected = FarmFields.Where(item => item.HouseholdId == household &&
            item.Stage is FarmFieldStage.Planted or FarmFieldStage.Growing or FarmFieldStage.Ready)
            .Sum(item => worldContent.Recipes.FirstOrDefault(recipe => recipe.CanonicalId == item.RecipeId)?.Outputs[0].Amount ?? 0);
        var prepared = FarmFields.Count(item => item.HouseholdId == household && item.Stage is
            FarmFieldStage.Tilling or FarmFieldStage.Prepared or FarmFieldStage.Harvested);
        var active = FarmFields.Count(item => item.HouseholdId == household &&
            item.Stage is FarmFieldStage.Planted or FarmFieldStage.Growing or FarmFieldStage.Ready);
        var hoe = FarmHoe(actor)!;
        var capability = ToolCapabilities.ForItem(hoe.ItemKind)!;
        var requiredUses = (TillingWork + capability.WorkQuantity - 1) / capability.WorkQuantity + HarvestWork * (active + 1);
        var enoughTool = hoe.ConditionBasisPoints >= requiredUses * capability.WearPerUse;
        if (enoughTool && prepared == 0 && stock + expected < Math.Max(6, population * 6) &&
            PlannedCrop(household) is not null && PlannedFieldTile(actor, farmhouse) is { } newTile)
            candidates.Add(new($"farm:till:{newTile.X},{newTile.Y}",
                $"Prepare another {LandFertilityRules.At(map, newTile).ToString().ToLowerInvariant()} field tile near the Farmhouse.", 24));
    }

    private void ApplyFieldCandidate(string actor, PlaytestInhabitantState state, string candidate)
    {
        if (candidate.StartsWith("farm:hoe:", StringComparison.Ordinal))
        { CollectEquipment(actor, state, candidate["farm:hoe:".Length..]); return; }
        if (candidate == "farm:collect") { CollectFieldHarvest(actor, state); return; }
        var pieces = candidate.Split(':');
        if (pieces.Length != 3 || !WorldBuildSiteRules.TryGetFieldPosition("field:" + pieces[2], out var point) ||
            FieldPermissionFailure(actor, point, pieces[1] == "till") is not null) return;
        if (pieces[1] == "plant")
        {
            if (PlannedCrop(HouseholdFor(actor)) is not { } recipe) return;
            foreach (var input in recipe.Inputs)
                if (!HasCarriedItem(actor, input.ResourceId))
                { CollectEquipment(actor, state, input.ResourceId); return; }
            if (state.Position != point) { MoveToward(actor, state, point, "plant_field", 0); return; }
            StartProductionCore(recipe.CanonicalId, WorldBuildSiteRules.FieldSiteId(point), actor, "field_planted");
            return;
        }
        if (state.Position != point) { MoveToward(actor, state, point, "field_work", 0); return; }
        if (pieces[1] == "till") TillFieldCore(actor, point);
        else if (pieces[1] == "harvest") HarvestFieldCore(actor, point);
        else if (pieces[1] == "tend") TendFieldCore(actor, point);
    }

    private void CollectFieldHarvest(string actor, PlaytestInhabitantState state)
    {
        var household = HouseholdFor(actor);
        var stock = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == household &&
            lot.GroundPosition is not null && AvailableLotQuantity(lot) > 0).OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();
        if (stock?.GroundPosition is not { } ground || FarmhouseForHousehold(household) is not { } farmhouse) return;
        var point = new GridPoint(ground.X, ground.Y);
        if (state.Position != point) { MoveToward(actor, state, point, "field_harvest", 0); return; }
        var store = stock.ItemKind is "grain" or "grain_seed" or "greens_seed" ||
            stock.ItemKind == "potatoes" && HouseholdCropStock(household, "potatoes") <= FarmPlantingReserve(household, "potatoes")
            ? HouseholdBuildingWithTag(household, "silo") ?? farmhouse : HouseForHousehold(household) ?? farmhouse;
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, $"field-pickup:{WorldTick}:{actor}",
            household, actor, stock.Id, Math.Min(HouseHaulLoadQuantity, AvailableLotQuantity(stock)),
            "field_harvest_collected", destinationDeliveryBuildingId: store.InstanceId));
        AppendEvent("field_harvest_collected", $"{actor}:{stock.Id}:{store.InstanceId}");
    }
}
