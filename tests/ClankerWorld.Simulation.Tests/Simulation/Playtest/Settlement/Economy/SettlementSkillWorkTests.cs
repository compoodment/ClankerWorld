using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementSkillWorkTests
{
    [Theory]
    [InlineData("mill-grain", SettlementSkillKind.Farming)]
    [InlineData("twist-rope", SettlementSkillKind.Crafting)]
    [InlineData("wooden-hammer", SettlementSkillKind.Smithing)]
    public async Task MatchingSkillFinishesTheSameProductionSoonerAcrossReload(string recipeName, SettlementSkillKind skill)
    {
        using var seed = NormalPathWorld.CreateGenerated("skill-production", _ => new IdleProvider());
        var state = seed.ExportState();
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == recipeName);
        var building = state.WorldSimulation!.Buildings.First(item => item.DefinitionId == recipe.WorkstationBuildingId);
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == building.HouseholdId).Id;
        var inventory = state.Society.Society.Inventory;
        foreach (var input in recipe.Inputs)
            inventory = InventoryFixture.AddLot(inventory, "skill-input:" + input.ResourceId, input.ResourceId,
                building.HouseholdId!, input.Amount, storageBuildingId: building.InstanceId);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = building.Position, HungerBasisPoints = 10_000 } : person).ToArray(),
        };
        using var novice = Restore(WithSkill(state, actor, null));
        using var skilled = Restore(WithSkill(state, actor, skill));
        using var unrelated = Restore(WithSkill(state, actor, SettlementSkillKind.Building));
        foreach (var world in new[] { novice, skilled, unrelated })
        {
            var started = world.StartProduction(recipe.CanonicalId, building.InstanceId, actor);
            Assert.True(started.Applied, started.Failure);
        }
        var normalJob = Assert.Single(novice.WorldSimulation.ProductionJobs);
        var fasterJob = Assert.Single(skilled.WorldSimulation.ProductionJobs);
        Assert.True(fasterJob.CompletionTick < normalJob.CompletionTick);
        Assert.Equal(normalJob.CompletionTick, Assert.Single(unrelated.WorldSimulation.ProductionJobs).CompletionTick);
        var before = PrivateWorldRuntimeCodec.Encode(skilled.ExportState());
        Assert.False((await skilled.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(skilled.ExportState()));
        await skilled.AdvanceOneTickAsync();
        using var reloaded = Reload(skilled);
        while (skilled.WorldTick < fasterJob.CompletionTick)
        {
            Assert.True((await skilled.AdvanceOneTickAsync()).Advanced);
            Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(skilled.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        }
        while (novice.WorldTick < fasterJob.CompletionTick) await novice.AdvanceOneTickAsync();
        Assert.Equal(WorldProductionJobState.Completed, Assert.Single(skilled.WorldSimulation.ProductionJobs).State);
        Assert.Equal(WorldProductionJobState.Running, Assert.Single(novice.WorldSimulation.ProductionJobs).State);
        Assert.All(fasterJob.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
            skilled.Society.Inventory.GetReservation(id).State));
        var output = skilled.Society.Inventory.Lots.Where(item => item.Id.StartsWith(fasterJob.JobId + ":output:", StringComparison.Ordinal)).ToArray();
        Assert.All(recipe.Outputs, item => Assert.Equal(item.Amount, output.Where(lot => lot.ItemKind == item.ResourceId).Sum(lot => lot.Quantity)));
        while (novice.WorldTick < normalJob.CompletionTick) await novice.AdvanceOneTickAsync();
        Assert.Equal(WorldProductionJobState.Completed, Assert.Single(novice.WorldSimulation.ProductionJobs).State);
        Assert.Contains(novice.Inhabitants.Single(person => person.InhabitantId == actor).Skills!, item => item.Kind == skill);
        novice.Validate();
        reloaded.Validate();
    }

    [Fact]
    public async Task BuildingSkillSpeedsPaidTownWorkWithoutChangingItsCompletionReceipt()
    {
        using var scenario = await TownProjectScenario.ApprovedAsync(initialTownStock: true);
        scenario.Policy.Supply = true;
        await scenario.UntilAsync(() => scenario.Project.WorkDone == 3, 240);
        var state = scenario.World.ExportState();
        var actor = scenario.Policy.Choices.Last(choice => choice.Id.StartsWith("town_project_work:", StringComparison.Ordinal)).Actor;
        IDecisionProvider Provider(string _) => new TownWorkProvider(actor);
        using var novice = PrivateWorldRuntime.Restore(WithSkill(state, actor, null), Provider);
        using var skilled = PrivateWorldRuntime.Restore(WithSkill(state, actor, SettlementSkillKind.Building), Provider);
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(skilled.ExportState())), Provider);
        var before = PrivateWorldRuntimeCodec.Encode(skilled.ExportState());
        Assert.False((await skilled.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(skilled.ExportState()));
        for (var tick = 0; tick < 80 && skilled.Towns[0].Projects[0].Stage != "completed"; tick++)
        {
            Assert.True((await skilled.AdvanceOneTickAsync()).Advanced);
            Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
            Assert.True((await novice.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(skilled.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        }
        var completed = Assert.Single(skilled.Towns[0].Projects);
        Assert.Equal("completed", completed.Stage);
        Assert.Equal(10, completed.WorkDone);
        Assert.NotEqual("completed", Assert.Single(novice.Towns[0].Projects).Stage);
        Assert.NotNull(completed.CompletedBuildingId);
        Assert.Contains(skilled.WorldSimulation.Buildings, building => building.InstanceId == completed.CompletedBuildingId);
        skilled.Validate();
    }

    [Fact]
    public async Task FarmingSkillShortensFieldWorkButDoesNotReplaceTheHoe()
    {
        var (state, actor, _, point) = FarmFieldTests.PreparedFarmer("skill-field");
        using var novice = Restore(WithSkill(state, actor, null));
        using var skilled = Restore(WithSkill(state, actor, SettlementSkillKind.Farming));
        using var unrelated = Restore(WithSkill(state, actor, SettlementSkillKind.Smithing));
        foreach (var world in new[] { novice, skilled, unrelated })
            Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        Assert.True(Assert.Single(skilled.Fields).Work!.RemainingTicks < Assert.Single(novice.Fields).Work!.RemainingTicks);
        Assert.Equal(Assert.Single(novice.Fields).Work!.RemainingTicks, Assert.Single(unrelated.Fields).Work!.RemainingTicks);
        await skilled.AdvanceOneTickAsync();
        await novice.AdvanceOneTickAsync();
        using var reloaded = Reload(skilled);
        for (var tick = 0; tick < 2; tick++)
        {
            await skilled.AdvanceOneTickAsync();
            await reloaded.AdvanceOneTickAsync();
            await novice.AdvanceOneTickAsync();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(skilled.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        }
        Assert.Equal(FarmFieldStage.Prepared, Assert.Single(skilled.Fields).Stage);
        Assert.NotNull(Assert.Single(novice.Fields).Work);
        var withoutHoe = state.Society.Society.Inventory with
        { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.Id != "carried-hoe").ToArray() };
        using var missingTool = Restore(WithSkill(FarmFieldTests.WithInventory(state, withoutHoe), actor, SettlementSkillKind.Farming));
        var before = PrivateWorldRuntimeCodec.Encode(missingTool.ExportState());
        Assert.False(missingTool.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(missingTool.ExportState()));
    }

    [Fact]
    public async Task BuildingSkillFinishesTheSameConstructionEarlierAcrossReload()
    {
        using var seed = new PrivateWorldRuntime("skill-building", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var actor = state.Inhabitants[0];
        var site = state.Map.Tiles.Last(tile => state.Map.IsPassable(tile.Position) &&
            !state.Map.CampObjects.Any(item => item.Position == tile.Position) &&
            !state.Map.Resources.Any(item => item.Position == tile.Position) &&
            !state.Inhabitants.Any(person => person.Position == tile.Position)).Position;
        var definition = seed.WorldContent.Buildings.Single(item => item.LocalId == "fire");
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person == actor ? person with
            {
                Position = site,
                HungerBasisPoints = 10_000,
                Project = new("build:building:" + definition.CanonicalId, definition.DisplayName,
                    seed.WorldTick, "working", LastTransitionTick: seed.WorldTick),
            } : person).ToArray(),
        };
        using var novice = Restore(WithSkill(state, actor.InhabitantId, null));
        using var skilled = Restore(WithSkill(state, actor.InhabitantId, SettlementSkillKind.Building));
        await skilled.AdvanceOneTickAsync();
        await novice.AdvanceOneTickAsync();
        using var reloaded = Reload(skilled);
        for (var tick = 0; tick < 20 && skilled.Inhabitants.Single(person => person.InhabitantId == actor.InhabitantId).Project!.Stage != "completed"; tick++)
        {
            await skilled.AdvanceOneTickAsync();
            await reloaded.AdvanceOneTickAsync();
            await novice.AdvanceOneTickAsync();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(skilled.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        }
        Assert.Equal("completed", skilled.Inhabitants.Single(person => person.InhabitantId == actor.InhabitantId).Project!.Stage);
        Assert.Equal("working", novice.Inhabitants.Single(person => person.InhabitantId == actor.InhabitantId).Project!.Stage);
        Assert.Contains(skilled.WorldSimulation.Buildings, item => item.DefinitionId == definition.CanonicalId && item.Position == site);
        reloaded.Validate();
    }

    internal static PrivateWorldRuntimeState WithSkill(PrivateWorldRuntimeState state, string actor, SettlementSkillKind? skill) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
        {
            Skills = skill is { } kind ? [new(kind, state.Society.Society.WorldTick)] : null,
            Proficiency = null,
            LastDecisionContext = null,
        } : person).ToArray(),
    };

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var restored = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        return restored;
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [request.Observation.Candidates.Single(item => item.Id == "safe_idle")] } }, cancellationToken);
    }

    private sealed class TownWorkProvider(string actor) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = observation.InhabitantId == actor
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("town_project_work:", StringComparison.Ordinal)) : null;
            selected ??= observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, request.ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                new Dictionary<string, double> { [selected.Id] = 1 }));
        }
    }
}
