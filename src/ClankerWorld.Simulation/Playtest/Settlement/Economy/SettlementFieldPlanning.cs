using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static string FarmCandidate(FarmWorkKind kind, GridPoint point, string? crop = null) =>
        $"farm:{kind}:{point.X}:{point.Y}:{crop ?? "-"}";

    private void AddFieldCandidates(List<CognitionCandidate> candidates, string actor, PlaytestInhabitantState state)
    {
        var householdId = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (householdId is null || FarmhouseForHousehold(householdId) is not { } farmhouse ||
            NeedsUrgentFood(state) || NeedsUrgentWarmth(state) ||
            (state.Project is { Stage: not ("completed" or "cancelled") } project && !project.RequiresFreshChoice)) return;
        var hasHoe = ToolProgressionRules.PlanWork(society.Checkpoint.Inventory, actor, ToolFamily.Hoe) is not null;
        foreach (var field in fields.Where(field => field.HouseholdId == householdId && field.Work is null))
        {
            if (!CanReachField(actor, state.Position, field.Position)) continue;
            if (field.Stage == FarmFieldStage.Ready)
                candidates.Add(new(FarmCandidate(FarmWorkKind.Harvest, field.Position), "Harvest the ready crop; it stays on the ground until carried.", 12));
            else if (hasHoe && field.Stage == FarmFieldStage.Growing && !field.Tended)
                candidates.Add(new(FarmCandidate(FarmWorkKind.Tend, field.Position), "Tend the growing crop with a hoe.", 13));
            else if (field.Stage is FarmFieldStage.Prepared or FarmFieldStage.Harvested && FarmNeedsFood(householdId))
            {
                if (HasOtherInhabitantClaimedPlanting(field, actor)) continue;
                var priority = 14;
                foreach (var crop in PlantableCrops(actor, field))
                    candidates.Add(new(FarmCandidate(FarmWorkKind.Plant, field.Position, crop),
                        $"Carry {FarmFieldRules.PlantingItem(crop).Replace('_', ' ')} to the field and plant {crop.Replace('_', ' ')}.", priority++));
            }
        }
        var population = society.Checkpoint.Inhabitants.Count(person => person.HouseholdId == householdId &&
            person.Status == SocietyInhabitantStatus.Active);
        var expectedYield = Math.Max(1, FarmFieldRules.HarvestQuantity(FarmFieldRules.Grain, fertility.At(farmhouse.Position)));
        var wantedFields = Math.Max(1, (int)Math.Ceiling(population * FarmFieldRules.MealsPerPersonPerDay * 2d / expectedYield));
        if (!hasHoe || !FarmNeedsFood(householdId) || fields.Count(field => field.HouseholdId == householdId) >= wantedFields) return;
        var site = NearbyFarmTiles(farmhouse.Position)
            .Where(FarmableFreeTile)
            .OrderByDescending(point => fields.Any(field => field.HouseholdId == householdId && map.FootDistance(field.Position, point) == 1))
            .ThenByDescending(point => fertility.At(point) - map.FootDistance(farmhouse.Position, point) * 2)
            .ThenBy(point => point.Y).ThenBy(point => point.X)
            .Cast<GridPoint?>().FirstOrDefault(point => CanReachField(actor, state.Position, point!.Value));
        if (site is { } chosen)
            candidates.Add(new(FarmCandidate(FarmWorkKind.Till, chosen),
                $"Prepare {World.LandFertility.Description(fertility.At(chosen)).ToLowerInvariant()} land for another household field.", 17));
    }

    private IEnumerable<GridPoint> NearbyFarmTiles(GridPoint origin)
    {
        for (var dy = -12; dy <= 12; dy++)
            for (var dx = -12; dx <= 12; dx++)
            {
                var x = origin.X + dx;
                if (map.WrapsEastWest) x = (x % map.Width + map.Width) % map.Width;
                var point = new GridPoint(x, origin.Y + dy);
                if (map.Contains(point)) yield return point;
            }
    }

    private bool HasOtherInhabitantClaimedPlanting(FarmFieldState field, string actor)
    {
        var runtimes = society.Capture().Cognition.Runtimes;
        foreach (var runtime in runtimes.Where(item => item.InhabitantId != actor))
        {
            if (runtime.CurrentIntention is not { } intention) continue;
            var crop = new[] { FarmFieldRules.Greens, FarmFieldRules.Grain, FarmFieldRules.Potatoes }
                .FirstOrDefault(item => intention.CandidateId == FarmCandidate(FarmWorkKind.Plant, field.Position, item));
            if (crop is null) continue;
            var eligible = inhabitants.TryGetValue(runtime.InhabitantId, out var claimant) &&
                AdultResident(runtime.InhabitantId) && HouseholdFor(runtime.InhabitantId) == field.HouseholdId &&
                !NeedsUrgentFood(claimant) && !NeedsUrgentWarmth(claimant) &&
                HasCarriedItem(runtime.InhabitantId, FarmFieldRules.Hoe) &&
                (claimant.Project is not { Stage: not ("completed" or "cancelled") } project || project.RequiresFreshChoice) &&
                FarmNeedsFood(field.HouseholdId);
            var stock = eligible ? PlantingStock(runtime.InhabitantId, field, crop) : null;
            if (stock is not null) return true;
        }
        return false;
    }

    // Other inhabitants can briefly occupy a destination or route tile. Keep
    // the field and seed choice legal while they pass so the selected task can
    // retry its physical route instead of being replaced by another crop.
    private bool CanReachField(string actor, GridPoint from, GridPoint destination) =>
        from == destination || map.IsReachableOnFoot(from, destination);

    private bool FarmNeedsFood(string householdId)
    {
        var population = society.Checkpoint.Inhabitants.Count(person => person.HouseholdId == householdId &&
            person.Status == SocietyInhabitantStatus.Active);
        var stock = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == householdId &&
            IsEdibleFood(lot.ItemKind)).Sum(AvailableLotQuantity);
        return stock < population * FarmFieldRules.MealsPerPersonPerDay * 2;
    }

    // A missing unit for existing eligible field work, not an inventory buffer.
    private bool WantsFieldPlantingStock(string actor, string itemKind)
    {
        var crop = new[] { FarmFieldRules.Greens, FarmFieldRules.Grain, FarmFieldRules.Potatoes }
            .FirstOrDefault(candidate => FarmFieldRules.PlantingItem(candidate) == itemKind);
        if (crop is null || !AdultResident(actor) ||
            society.Checkpoint.GetInhabitant(actor).Status != SocietyInhabitantStatus.Active ||
            society.Checkpoint.GetInhabitant(actor).HouseholdId is not { } householdId ||
            FarmhouseForHousehold(householdId) is null || !FarmNeedsFood(householdId) ||
            NeedsUrgentFood(inhabitants[actor]) || NeedsUrgentWarmth(inhabitants[actor]) ||
            (inhabitants[actor].Project is { Stage: not ("completed" or "cancelled") } project && !project.RequiresFreshChoice) ||
            ToolProgressionRules.PlanWork(society.Checkpoint.Inventory, actor, ToolFamily.Hoe) is null)
            return false;
        return fields.Any(field => field.HouseholdId == householdId && field.Work is null &&
            field.Stage is FarmFieldStage.Prepared or FarmFieldStage.Harvested &&
            CanReachField(actor, inhabitants[actor].Position, field.Position) &&
            !HasOtherInhabitantClaimedPlanting(field, actor) && PlantingStock(actor, field, crop) is null);
    }

    private IEnumerable<string> PlantableCrops(string actor, FarmFieldState field) =>
        new[] { FarmFieldRules.Greens, FarmFieldRules.Grain, FarmFieldRules.Potatoes }
            .OrderBy(crop => field.Crop == crop ? -1 : fields.Count(item => item.HouseholdId == field.HouseholdId &&
                (item.Work?.Crop ?? item.Crop) == crop))
            .Where(crop => PlantingStock(actor, field, crop) is not null);

    private InventoryLot? PlantingStock(string actor, FarmFieldState field, string crop)
    {
        var kind = FarmFieldRules.PlantingItem(crop);
        var inventory = society.Checkpoint.Inventory;
        var reserve = inventory.Reservations.FirstOrDefault(item => item.Id == field.ReplantingReservationId &&
            item.State == InventoryReservationState.Reserved);
        // Potatoes in a storage pot move only with the pot, so they are not planting stock.
        return inventory.Lots.Where(lot => lot.ItemKind == kind && lot.DeliveryBuildingId is null &&
                lot.ContainerLotId is null &&
                (lot.OwnerId == actor && PersonalEquipmentRules.IsCarried(lot, actor) ||
                    lot.OwnerId == field.HouseholdId && lot.CarrierId is null && FreeCarryCapacity(actor) > 0) &&
                (AvailableLotQuantity(lot) > 0 || reserve?.LotId == lot.Id))
            .OrderBy(lot => lot.OwnerId == actor ? 0 : reserve?.LotId == lot.Id ? 1 : 2)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal)
            .FirstOrDefault(lot => lot.OwnerId == actor ||
                map.IsReachableOnFoot(inhabitants[actor].Position, HouseholdStockPosition(lot)));
    }

    private void ApplyFieldCandidate(string actor, PlaytestInhabitantState state, string candidate)
    {
        var parts = candidate.Split(':');
        if (parts.Length != 5 || !Enum.TryParse<FarmWorkKind>(parts[1], out var kind) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var y)) return;
        var point = new GridPoint(x, y);
        var crop = parts[4] == "-" ? null : parts[4];
        if (kind == FarmWorkKind.Plant)
        {
            var field = fields.SingleOrDefault(item => item.Position == point && item.HouseholdId == HouseholdFor(actor));
            if (field is null || crop is null || !FarmFieldRules.IsCrop(crop) || PlantingStock(actor, field, crop) is not { } seed) return;
            if (seed.OwnerId != actor)
            {
                if (FreeCarryCapacity(actor) == 0) return;
                var source = HouseholdStockPosition(seed);
                var range = HouseholdStockInteractionRange(seed);
                if (!IsWithinInteractionRange(state.Position, source, range))
                {
                    MoveToward(actor, state, source, "planting_stock", range);
                    return;
                }
                ApplyInventoryTransition(inventory =>
                {
                    if (field.ReplantingReservationId is { } id && inventory.Reservations.Any(item =>
                        item.Id == id && item.LotId == seed.Id && item.State == InventoryReservationState.Reserved))
                        inventory = InventoryFixture.ReleaseReservation(inventory, id, "planting_stock_collected");
                    return InventoryFixture.Transfer(inventory, $"field-seed-pickup:{WorldTick}:{actor}",
                        field.HouseholdId, actor, seed.Id, 1, "field_seed_picked_up");
                });
                if (field.ReplantingReservationId is { } replant && !ActiveFarmReservation(replant))
                    SetFarmField(field with { ReplantingReservationId = null });
                return;
            }
            if (state.Position != point) { MoveToward(actor, state, point, "field_planting"); return; }
            StartFieldWorkCore(actor, point, kind, crop, seed.Id);
            return;
        }
        if (state.Position != point) { MoveToward(actor, state, point, "field_work"); return; }
        StartFieldWorkCore(actor, point, kind);
    }
}
