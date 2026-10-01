using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

// Pipeline proofs start with previously worked soil. FarmFieldTests and
// TiledFarmingTests separately exercise physical tilling, tools and permissions.
internal static class FarmTestFields
{
    internal static PrivateWorldRuntimeState Prepare(PrivateWorldRuntimeState state, string actor, GridPoint point,
        string crop = FarmFieldRules.Grain)
    {
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "fixture-hoe:" + actor, "iron_hoe", actor, 1, state.Society.Society.WorldTick);
        inventory = InventoryFixture.AddLot(inventory, "fixture-crop:" + actor + ":" + crop,
            FarmFieldRules.PlantingItem(crop), actor, 1, state.Society.Society.WorldTick);
        return FarmFieldTests.WithInventory(state, inventory) with
        {
            Fields = [new(point, household, FarmFieldStage.Prepared)],
        };
    }

    internal static async Task PlantAndGrow(PrivateWorldRuntime world, string actor, GridPoint point,
        string crop = FarmFieldRules.Grain, string? seedId = null)
    {
        var planting = world.StartFieldWork(actor, point, FarmWorkKind.Plant, crop,
            seedId ?? "fixture-crop:" + actor + ":" + crop);
        Assert.True(planting.Accepted, planting.Message);
        for (var tick = 0; tick < FarmFieldRules.WorkTicks(FarmWorkKind.Plant); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(FarmFieldStage.Planted, world.Fields.Single(field => field.Position == point).Stage);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var tending = world.StartFieldWork(actor, point, FarmWorkKind.Tend);
        Assert.True(tending.Accepted, tending.Message);
        for (var tick = 0; tick < world.WorldSystems.Config.TicksPerDay + 20 &&
            world.Fields.Single(field => field.Position == point).Stage != FarmFieldStage.Ready; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(FarmFieldStage.Ready, world.Fields.Single(field => field.Position == point).Stage);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot =>
            lot.Id.StartsWith(FarmFieldRules.FieldId(point) + ":harvest:", StringComparison.Ordinal));
    }

    internal static async Task Harvest(PrivateWorldRuntime world, string actor, GridPoint point)
    {
        var started = world.StartFieldWork(actor, point, FarmWorkKind.Harvest);
        Assert.True(started.Accepted, started.Message);
        for (var tick = 0; tick < 20 && world.Fields.Single(field => field.Position == point).Work is not null; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(FarmFieldStage.Harvested, world.Fields.Single(field => field.Position == point).Stage);
    }
}

internal sealed class FarmCarryProvider(bool active) : IDecisionProvider
{
    public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
    public long ProviderEpoch => 0;
    public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var preferred = active ? request.Observation.Candidates.Where(candidate =>
            candidate.Id is "haul_household_stock" or "farm:collect").OrderBy(candidate => candidate.DeterministicPriority).FirstOrDefault() : null;
        return new DeterministicDecisionProvider().DecideAsync(request with
        {
            Observation = request.Observation with { Candidates = [preferred ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")] },
        }, cancellationToken);
    }
}
