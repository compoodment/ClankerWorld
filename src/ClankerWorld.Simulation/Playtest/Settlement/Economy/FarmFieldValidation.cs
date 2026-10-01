using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidateFarmFields(FarmFieldState[] fields, SeededMap map, string seed,
        SocietyCheckpoint society, WorldContentSimulationState simulation, DeclarativeWorldContentState content,
        IReadOnlyList<GridPoint> roads)
    {
        if (fields.Length == 0) return;
        if (fields.Any(field => field is null))
            throw new InvalidDataException("A saved field record is missing.");
        var fertility = new LandFertility(map, seed);
        var occupied = map.Resources.Select(item => item.Position).Concat(map.CampObjects.Select(item => item.Position))
            .Concat(roads).Concat(simulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                content.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)))
            .Concat((simulation.BuildingExpansions ?? []).Where(job => job.State == WorldProductionJobState.Running).SelectMany(ExpansionTiles)).ToHashSet();
        if (fields.Select(field => field.Position).Distinct().Count() != fields.Length ||
            !fields.SequenceEqual(fields.OrderBy(field => field.Position.Y).ThenBy(field => field.Position.X)) ||
            fields.Where(field => field.Work is not null).GroupBy(field => field.Work!.WorkerId).Any(group => group.Count() > 1))
            throw new InvalidDataException("Fields must have distinct ordered tiles and one work site per worker.");
        foreach (var field in fields)
        {
            if (!fertility.CanFarm(field.Position) || occupied.Contains(field.Position) || !Enum.IsDefined(field.Stage) ||
                !society.Households.Any(household => household.Id == field.HouseholdId) || field.Cycle < 0 ||
                field.Stage is FarmFieldStage.Preparing or FarmFieldStage.Prepared && field.Crop is not null ||
                field.Stage is not (FarmFieldStage.Preparing or FarmFieldStage.Prepared) &&
                    (!FarmFieldRules.IsCrop(field.Crop) || field.PlantedTick < 0 || field.PlantedTick > society.WorldTick ||
                     field.ReadyTick <= field.PlantedTick) ||
                field.Stage is FarmFieldStage.Ready or FarmFieldStage.Harvested && (!field.Tended || field.ReadyTick > society.WorldTick) ||
                field.Stage == FarmFieldStage.Preparing && field.Work?.Kind != FarmWorkKind.Till)
                throw new InvalidDataException("A field has invalid land, ownership or crop state.");
            if (field.Work is { } work)
            {
                var worker = society.Inhabitants.SingleOrDefault(person => person.Id == work.WorkerId);
                var workToolsValid = work.Kind is FarmWorkKind.Till or FarmWorkKind.Tend
                    ? ToolProgressionRules.PlanWorkForLot(society.Inventory, work.WorkerId, ToolFamily.Hoe,
                        work.HoeLotId) is not null && work.SickleLotId is null
                    : work.HoeLotId is null && (work.Kind == FarmWorkKind.Harvest
                        ? work.SickleLotId is null || ToolProgressionRules.PlanWorkForLot(society.Inventory,
                            work.WorkerId, ToolFamily.Sickle, work.SickleLotId) is not null
                        : work.SickleLotId is null);
                if (worker is null || worker.Status != SocietyInhabitantStatus.Active || worker.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder) ||
                    worker.HouseholdId != field.HouseholdId || !Enum.IsDefined(work.Kind) || work.LastWorkedTick < 0 ||
                    work.LastWorkedTick > society.WorldTick || work.RemainingTicks < 1 || work.RemainingTicks > FarmFieldRules.WorkTicks(work.Kind) ||
                    work.Kind == FarmWorkKind.Till && field.Stage != FarmFieldStage.Preparing ||
                    work.Kind == FarmWorkKind.Tend && (field.Stage != FarmFieldStage.Growing || field.Tended) ||
                    work.Kind == FarmWorkKind.Harvest && field.Stage != FarmFieldStage.Ready ||
                    work.Kind == FarmWorkKind.Plant && (field.Stage is not (FarmFieldStage.Prepared or FarmFieldStage.Harvested) ||
                        !FarmFieldRules.IsCrop(work.Crop)) || work.Kind != FarmWorkKind.Plant && (work.Crop is not null || work.SeedReservationId is not null) ||
                    !workToolsValid)
                    throw new InvalidDataException("A field has invalid work in progress.");
                if (work.Kind == FarmWorkKind.Plant)
                {
                    var reservation = society.Inventory.Reservations.SingleOrDefault(item => item.Id == work.SeedReservationId);
                    var lot = society.Inventory.Lots.SingleOrDefault(item => item.Id == reservation?.LotId);
                    if (reservation is not { State: InventoryReservationState.Reserved, Quantity: 1, Purpose: "field_planting" } ||
                        reservation.OwnerId != work.WorkerId || reservation.ExpiryTick < society.WorldTick ||
                        lot is null || lot.OwnerId != work.WorkerId || lot.ItemKind != FarmFieldRules.PlantingItem(work.Crop!) ||
                        lot.GroundPosition is not null || lot.StorageBuildingId is not null || lot.DeliveryBuildingId is not null)
                        throw new InvalidDataException("Field planting needs its worker's actual reserved seed.");
                }
            }
            if (field.ReplantingReservationId is { } replantId)
            {
                var reservation = society.Inventory.Reservations.SingleOrDefault(item => item.Id == replantId);
                var lot = society.Inventory.Lots.SingleOrDefault(item => item.Id == reservation?.LotId);
                if (field.Crop is null || reservation is not { State: InventoryReservationState.Reserved, Quantity: 1, Purpose: "field_replanting" } ||
                    reservation.OwnerId != field.HouseholdId || lot is null || lot.OwnerId != field.HouseholdId ||
                    lot.ItemKind != FarmFieldRules.PlantingItem(field.Crop))
                    throw new InvalidDataException("A field needs its own usable replanting reserve.");
            }
        }
    }
}
