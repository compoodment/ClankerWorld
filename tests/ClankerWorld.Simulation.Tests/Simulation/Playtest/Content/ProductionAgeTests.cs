using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class ProductionAgeTests
{
    [Theory]
    [InlineData(SocietyAgeBand.Infant, false)]
    [InlineData(SocietyAgeBand.Child, false)]
    [InlineData(SocietyAgeBand.Adolescent, false)]
    [InlineData(SocietyAgeBand.Infant, true)]
    [InlineData(SocietyAgeBand.Child, true)]
    [InlineData(SocietyAgeBand.Adolescent, true)]
    public async Task YoungWorkersCannotStartRecipesOrCropsBeforeOrAfterReload(SocietyAgeBand age, bool crop)
    {
        var prepared = await PrepareAsync(age, crop);
        using var world = PrivateWorldRuntime.Restore(prepared.State, _ => new IdleProvider());
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var rejected = world.StartProduction(prepared.RecipeId, prepared.WorkstationId, prepared.WorkerId);
        Assert.False(rejected.Applied);
        Assert.Contains("too young", rejected.Failure, StringComparison.Ordinal);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => new IdleProvider());
        Assert.False(reloaded.StartProduction(prepared.RecipeId, prepared.WorkstationId, prepared.WorkerId).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Theory]
    [InlineData(SocietyAgeBand.Adult, false)]
    [InlineData(SocietyAgeBand.Elder, false)]
    [InlineData(SocietyAgeBand.Adult, true)]
    [InlineData(SocietyAgeBand.Elder, true)]
    public async Task EligibleWorkersCompleteRecipesAndCropsAfterReload(SocietyAgeBand age, bool crop)
    {
        var prepared = await PrepareAsync(age, crop);
        using var world = PrivateWorldRuntime.Restore(prepared.State, _ => new IdleProvider());
        var started = world.StartProduction(prepared.RecipeId, prepared.WorkstationId, prepared.WorkerId);
        Assert.True(started.Applied, started.Failure);
        await CompleteAfterReloadAsync(world.ExportState(), started.JobId!);
    }

    internal static async Task CompleteAfterReloadAsync(PrivateWorldRuntimeState state, string jobId)
    {
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new IdleProvider());
        var job = restored.WorldSimulation.ProductionJobs.Concat(restored.WorldSimulation.CropBuilds ?? [])
            .Single(item => item.JobId == jobId);
        Assert.Equal(WorldProductionJobState.Running, job.State);
        restored.Resume();
        while (restored.WorldTick < job.CompletionTick)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(WorldProductionJobState.Completed, restored.WorldSimulation.ProductionJobs
            .Concat(restored.WorldSimulation.CropBuilds ?? []).Single(item => item.JobId == jobId).State);
        var recipe = restored.WorldContent.Recipes.Single(item => item.CanonicalId == job.RecipeId);
        foreach (var output in recipe.Outputs)
            Assert.Contains(restored.Society.Inventory.Lots, lot => lot.Id.StartsWith(jobId + ":", StringComparison.Ordinal) &&
                lot.ItemKind == output.ResourceId && lot.Quantity == output.Amount);
        var completed = restored.ExportState();
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(completed)));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(completed), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    internal static async Task<PreparedProduction> PrepareAsync(SocietyAgeBand age, bool crop = false)
    {
        using var seed = new PrivateWorldRuntime(SeededWorldObservationStore.SampleSeed, _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 5; tick++) await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var worker = state.Inhabitants[0];
        var site = crop ? state.Map.GetResource(ClankerWorld.Simulation.Harness.SeededMapGenerator.FertileLandResourceId).Position :
            state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
                !state.Map.CampObjects.Any(item => item.Position == tile.Position) &&
                !state.Map.Resources.Any(item => item.Position == tile.Position) &&
                !state.Inhabitants.Any(person => person.InhabitantId != worker.InhabitantId && person.Position == tile.Position)).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker.InhabitantId
                ? person with { Position = site } : person).ToArray(),
        };
        using var placing = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var workstation = WorldBuildSiteRules.FertileLandSiteId(site);
        if (!crop)
        {
            var workshop = placing.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
            var placed = placing.PlaceBuilding("age-workshop", workshop.CanonicalId, site);
            Assert.True(placed.Applied, placed.Failure);
            workstation = placed.InstanceId;
        }
        placing.Pause();
        state = placing.ExportState();
        var society = state.Society.Society;
        var ageYears = age switch
        {
            SocietyAgeBand.Infant => 0,
            SocietyAgeBand.Child => society.Config.InfantYears,
            SocietyAgeBand.Adolescent => society.Config.ChildYears,
            SocietyAgeBand.Adult => society.Config.AdultYears,
            _ => society.Config.ElderYears,
        };
        var birth = society.LifeTickAt(society.WorldTick) - ageYears * society.Config.TicksPerLifecycleAge;
        state = state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == worker.InhabitantId ? person with
                    {
                        BirthTick = birth,
                        BirthLifeTick = society.LifeClock is null ? null : birth,
                        AgeBand = age,
                        LastLifecycleYearChecked = ageYears,
                        CurrentRole = SocietyWorkRole.Unassigned,
                    } : person).ToArray(),
                }
            },
        };
        using var validated = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        validated.Validate();
        var recipe = placing.WorldContent.Recipes.Single(item => item.LocalId == (crop ? "vegetables" : "tools"));
        return new(state, recipe.CanonicalId, workstation, worker.InhabitantId);
    }

    internal sealed record PreparedProduction(PrivateWorldRuntimeState State, string RecipeId, string WorkstationId, string WorkerId);

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = request.Observation.Candidates.Where(candidate => candidate.Id == "safe_idle").ToArray() },
            }, cancellationToken);
    }
}
