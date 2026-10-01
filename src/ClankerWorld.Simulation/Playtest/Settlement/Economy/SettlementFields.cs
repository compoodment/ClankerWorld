using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    public IReadOnlyList<FarmFieldState> Fields => fields.ToArray();

    /// <summary>Start physical work at one tile. Refusal leaves the field and inventory untouched.</summary>
    public FarmWorkResult StartFieldWork(string workerId, GridPoint position, FarmWorkKind kind,
        string? crop = null, string? seedLotId = null)
    {
        gate.Wait();
        try { return StartFieldWorkCore(workerId, position, kind, crop, seedLotId); }
        finally { gate.Release(); }
    }

    private FarmWorkResult StartFieldWorkCore(string workerId, GridPoint position, FarmWorkKind kind,
        string? crop = null, string? seedLotId = null)
    {
        if (!Enum.IsDefined(kind) || !inhabitants.TryGetValue(workerId, out var worker) || !AdultResident(workerId) ||
            society.Checkpoint.GetInhabitant(workerId).HouseholdId is not { } householdId ||
            FarmhouseForHousehold(householdId) is null)
            return new(false, "An adult from a household with a Farmhouse must do the work.");
        if (worker.Position != position) return new(false, "The worker must stand on the field tile.");
        if (!HasCarriedItem(workerId, FarmFieldRules.Hoe)) return new(false, "The worker needs a hoe.");
        if (NeedsUrgentFood(worker) || NeedsUrgentWarmth(worker) ||
            worker.Project is { Stage: not ("completed" or "cancelled") } || FarmWorkFor(workerId) is not null)
            return new(false, "The worker must finish other work or meet urgent needs first.");
        var field = fields.SingleOrDefault(item => item.Position == position);
        if (kind == FarmWorkKind.Till)
        {
            if (field is not null || !FarmableFreeTile(position))
                return new(false, "This land cannot be tilled: choose free farmable land.");
            field = new(position, householdId, FarmFieldStage.Preparing);
        }
        else if (field is null || field.HouseholdId != householdId || field.Work is not null)
            return new(false, "This household does not have a free field here.");
        if (kind == FarmWorkKind.Plant &&
            (field.Stage is not (FarmFieldStage.Prepared or FarmFieldStage.Harvested) || !FarmFieldRules.IsCrop(crop)))
            return new(false, "Plant a supported crop on prepared or harvested soil.");
        if (kind == FarmWorkKind.Tend && (field.Stage != FarmFieldStage.Growing || field.Tended))
            return new(false, "This crop does not need tending now.");
        if (kind == FarmWorkKind.Harvest && field.Stage != FarmFieldStage.Ready)
            return new(false, "The crop is not ready to harvest.");
        string? reservationId = null;
        if (kind == FarmWorkKind.Plant)
        {
            var seed = society.Checkpoint.Inventory.Lots.SingleOrDefault(lot => lot.Id == seedLotId);
            if (seed is null || seed.OwnerId != workerId || seed.GroundPosition is not null ||
                seed.StorageBuildingId is not null || seed.DeliveryBuildingId is not null ||
                seed.ItemKind != FarmFieldRules.PlantingItem(crop!) || AvailableLotQuantity(seed) < 1)
                return new(false, "Carry one of your own usable planting items to the field.");
            reservationId = $"{FarmFieldRules.FieldId(position)}:plant:{field.Cycle}:{WorldTick}:{workerId}";
            ApplyInventoryTransition(inventory => InventoryFixture.Reserve(inventory, reservationId,
                workerId, seed.Id, 1, "field_planting", checked(WorldTick + FarmFieldRules.WorkTicks(kind) + 1)));
        }
        SetFarmField(field with { Work = new(workerId, kind, FarmFieldRules.WorkTicks(kind), WorldTick, reservationId, crop) });
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("field_work_started", $"{workerId}:{FarmFieldRules.FieldId(position)}:{kind}");
        return new(true, "Field work started.");
    }

    private FarmFieldState? FarmWorkFor(string workerId) => fields.SingleOrDefault(field => field.Work?.WorkerId == workerId);

    private bool FarmableFreeTile(GridPoint position)
    {
        if (!fertility.CanFarm(position) || fields.Any(field => field.Position == position) ||
            RoadAndBridgeTiles().Contains(position) || map.CampObjects.Any(item => item.Position == position) ||
            (worldSimulation.BuildingExpansions ?? []).Any(job => job.State == WorldProductionJobState.Running && ExpansionTiles(job).Contains(position)) ||
            map.Resources.Any(item => item.Position == position)) return false;
        return !worldSimulation.Buildings.Any(building => WorldContentSimulationRules.Footprint(
            worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building).Contains(position));
    }

    private void SetFarmField(FarmFieldState field)
    {
        fields.RemoveAll(item => item.Position == field.Position);
        fields.Add(field);
        fields = fields.OrderBy(item => item.Position.Y).ThenBy(item => item.Position.X).ToList();
    }

    private void MaintainFarmFields()
    {
        foreach (var field in fields.ToArray())
        {
            if (field.Work is { } work && (!inhabitants.TryGetValue(work.WorkerId, out var worker) ||
                !AdultResident(work.WorkerId) || HouseholdFor(work.WorkerId) != field.HouseholdId || FarmhouseForHousehold(field.HouseholdId) is null ||
                worker.Position != field.Position || !HasCarriedItem(work.WorkerId, FarmFieldRules.Hoe) ||
                NeedsUrgentFood(worker) || NeedsUrgentWarmth(worker) ||
                work.SeedReservationId is { } id && !ActiveFarmReservation(id)))
            {
                CancelFarmWork(field);
                continue;
            }
            if (field.Stage == FarmFieldStage.Planted && WorldTick > field.PlantedTick)
                SetFarmField(field with { Stage = FarmFieldStage.Growing });
            else if (field.Stage == FarmFieldStage.Growing && field.Tended && WorldTick >= field.ReadyTick)
            {
                SetFarmField(field with { Stage = FarmFieldStage.Ready });
                AppendEvent("field_ready", FarmFieldRules.FieldId(field.Position));
            }
            if (field.ReplantingReservationId is { } reserve && !ActiveFarmReservation(reserve))
                SetFarmField(fields.Single(item => item.Position == field.Position) with { ReplantingReservationId = null });
        }
    }

    private bool ActiveFarmReservation(string id) => society.Checkpoint.Inventory.Reservations.Any(reservation =>
        reservation.Id == id && reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed);

    private void CancelFarmWork(FarmFieldState field)
    {
        if (field.Work?.SeedReservationId is { } reservationId && ActiveFarmReservation(reservationId))
            ApplyInventoryTransition(inventory => InventoryFixture.ReleaseReservation(inventory, reservationId, "field_work_interrupted"));
        if (field.Stage == FarmFieldStage.Preparing) fields.RemoveAll(item => item.Position == field.Position);
        else SetFarmField(field with { Work = null });
        AppendEvent("field_work_interrupted", FarmFieldRules.FieldId(field.Position));
    }

    private bool ContinueFarmWork(string workerId)
    {
        if (FarmWorkFor(workerId) is not { Work: { } work } field) return false;
        if (work.LastWorkedTick == WorldTick) return true;
        if (!inhabitants.TryGetValue(workerId, out var worker) || !AdultResident(workerId) || HouseholdFor(workerId) != field.HouseholdId ||
            FarmhouseForHousehold(field.HouseholdId) is null || worker.Position != field.Position ||
            NeedsUrgentFood(worker) || NeedsUrgentWarmth(worker) || !HasCarriedItem(workerId, FarmFieldRules.Hoe))
        {
            CancelFarmWork(field);
            return false;
        }
        work = work with { RemainingTicks = work.RemainingTicks - 1, LastWorkedTick = WorldTick };
        if (work.RemainingTicks > 0) { SetFarmField(field with { Work = work }); return true; }
        switch (work.Kind)
        {
            case FarmWorkKind.Till:
                SetFarmField(field with { Stage = FarmFieldStage.Prepared, Work = null });
                break;
            case FarmWorkKind.Plant:
                ApplyInventoryTransition(inventory =>
                {
                    if (field.ReplantingReservationId is { } previous && ActiveFarmReservation(previous))
                        inventory = InventoryFixture.ReleaseReservation(inventory, previous, "field_replanting_completed");
                    return InventoryFixture.ConsumeReservation(inventory, work.SeedReservationId!);
                });
                SetFarmField(field with
                {
                    Stage = FarmFieldStage.Planted,
                    Crop = work.Crop,
                    PlantedTick = WorldTick,
                    ReadyTick = checked(WorldTick + FarmFieldRules.GrowthTicks(worldSystems.Config.TicksPerDay, fertility.At(field.Position))),
                    Tended = false,
                    Work = null,
                    ReplantingReservationId = null
                });
                break;
            case FarmWorkKind.Tend:
                SetFarmField(field with { Tended = true, Work = null });
                break;
            case FarmWorkKind.Harvest:
                CompleteFieldHarvest(field);
                break;
        }
        CreditCompletedWork(workerId, "farming");
        AppendEvent(work.Kind == FarmWorkKind.Till ? "field_prepared" : work.Kind == FarmWorkKind.Plant ? "field_planted" :
            work.Kind == FarmWorkKind.Tend ? "field_tended" : "field_harvested",
            $"{workerId}:{FarmFieldRules.FieldId(field.Position)}:{(work.Kind == FarmWorkKind.Plant ? work.Crop : field.Crop)}");
        return true;
    }

    private void CompleteFieldHarvest(FarmFieldState field)
    {
        var cycle = checked(field.Cycle + 1);
        var prefix = $"{FarmFieldRules.FieldId(field.Position)}:harvest:{cycle}";
        var crop = field.Crop!;
        var plantingItem = FarmFieldRules.PlantingItem(crop);
        var plantingLotId = plantingItem == crop ? prefix + ":crop" : prefix + ":seed";
        var replantId = prefix + ":replant";
        var quantity = FarmFieldRules.HarvestQuantity(crop, fertility.At(field.Position));
        var weather = WeatherAt(field.Position);
        var moisture = WeatherRules.SoilMoistureAt(worldSystems, field.Position, map.Height,
            WeatherRules.RegionClimate(map, field.Position));
        if (weather == WeatherKind.Snow) quantity = Math.Max(1, quantity / 2);
        else if (weather == WeatherKind.Storm || moisture < 15) quantity = Math.Max(1, quantity * 3 / 4);
        else if (moisture >= 50) quantity += Math.Max(1, quantity / 4);
        ApplyInventoryTransition(inventory =>
        {
            var next = InventoryFixture.AddLot(inventory, prefix + ":crop", crop, field.HouseholdId,
                quantity, WorldTick,
                groundPosition: new(field.Position.X, field.Position.Y));
            if (plantingItem != crop)
                next = InventoryFixture.AddLot(next, plantingLotId, plantingItem, field.HouseholdId, 2, WorldTick,
                    groundPosition: new(field.Position.X, field.Position.Y));
            return InventoryFixture.Reserve(next, replantId, field.HouseholdId, plantingLotId, 1, "field_replanting", long.MaxValue);
        });
        SetFarmField(field with { Stage = FarmFieldStage.Harvested, Cycle = cycle, Work = null, ReplantingReservationId = replantId });
        if (weather is WeatherKind.Snow or WeatherKind.Storm)
            AppendEvent("crop_weather_loss", $"{prefix}:{weather.ToString().ToLowerInvariant()}");
        else if (moisture < 15 || moisture >= 50)
            AppendEvent("crop_moisture_effect", $"{prefix}:{(moisture < 15 ? "dry" : "wet")}:{moisture}");
    }
}
