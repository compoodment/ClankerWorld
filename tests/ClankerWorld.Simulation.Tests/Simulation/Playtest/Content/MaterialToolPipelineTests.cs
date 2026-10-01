using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class MaterialToolPipelineTests
{
    [Theory]
    [InlineData("iron_ore", "wooden_pickaxe", false)]
    [InlineData("iron_ore", "stone_pickaxe", true)]
    [InlineData("gold_ore", "stone_pickaxe", false)]
    [InlineData("gold_ore", "iron_pickaxe", true)]
    [InlineData("diamond", "iron_pickaxe", true)]
    public async Task ExtractionRequiresTheCarriedTierAndDepletesFiniteOutcrop(string material, string tool, bool permitted)
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new Pick("safe_idle"));
        var state = seed.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var source = state.Map.Resources.First(resource => resource.Kind == material);
        var stock = InventoryFixture.AddLot(state.Society.Society.Inventory, "mining-test-tool", tool, actor, 1);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = stock } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = source.Position, LastDecisionContext = null, HungerBasisPoints = 10_000 }
                : person).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems!.Ecology with
                {
                    Resources = state.WorldSystems!.Ecology.Resources.Select(resource => resource.Id == source.Id
                        ? resource with { Quantity = 1 } : resource).ToArray(),
                }
            },
        };
        using var extracting = PrivateWorldRuntime.Restore(state,
            id => new Pick(id == actor ? "mine_material:" + material : "safe_idle"));
        for (var tick = 0; tick < 8; tick++) Assert.True((await extracting.AdvanceOneTickAsync()).Advanced);
        var after = extracting.ExportState();
        if (!permitted)
        {
            Assert.DoesNotContain(extracting.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == material);
            Assert.Equal(1, after.WorldSystems!.Ecology.GetResource(source.Id).Quantity);
            Assert.Equal(10_000, extracting.Society.Inventory.GetLot("mining-test-tool").ConditionBasisPoints);
            return;
        }
        var output = Assert.Single(extracting.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == material);
        Assert.Equal(ToolCapabilities.ForItem(tool)!.WorkQuantity, output.Quantity);
        Assert.Null(output.StorageBuildingId);
        Assert.Equal(0, after.WorldSystems!.Ecology.GetResource(source.Id).Quantity);
        Assert.False(after.WorldSystems!.Ecology.GetResource(source.Id).IsRenewable);
        Assert.Equal(10_000 - ToolCapabilities.ForItem(tool)!.WearPerUse,
            extracting.Society.Inventory.GetLot("mining-test-tool").ConditionBasisPoints);
        var observed = new OwnerWorldObservationStore(extracting).GetSnapshot().Inhabitants.Single(person => person.Id == actor);
        Assert.True(observed.Survival!.HasTool);
        Assert.Equal(extracting.Society.Inventory.GetLot("mining-test-tool").ConditionBasisPoints,
            observed.Inventory.Single(item => item.Kind == tool).ConditionBasisPoints);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(after)),
            id => new Pick(id == actor ? "mine_material:" + material : "safe_idle"));
        Assert.Equal(output, restored.Society.Inventory.GetLot(output.Id));
        for (var tick = 0; tick < 8; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(output.Quantity, restored.Society.Inventory.GetLot(output.Id).Quantity);
    }

    [Fact]
    public async Task KnifeSpeedsPreparationAndItsWearAndReservedIngredientsSurviveReload()
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new Pick("safe_idle"));
        var state = seed.ExportState();
        var house = seed.WorldSimulation.Buildings.First(building => seed.WorldContent.Buildings.Any(definition =>
            definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house")));
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == house.HouseholdId).Id;
        var stock = InventoryFixture.AddLot(state.Society.Society.Inventory, "preparation-knife", "knife", actor, 1);
        stock = InventoryFixture.AddLot(stock, "preparation-food", "food", house.HouseholdId!, 2, storageBuildingId: house.InstanceId);
        stock = InventoryFixture.AddLot(stock, "preparation-wood", "wood", house.HouseholdId!, 1, storageBuildingId: house.InstanceId);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = stock } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position } : person).ToArray(),
        };
        using var preparing = PrivateWorldRuntime.Restore(state, _ => new Pick("safe_idle"));
        var recipe = preparing.WorldContent.Recipes.Single(item => item.LocalId == "house-meal");
        var result = preparing.StartProduction(recipe.CanonicalId, house.InstanceId, actor);
        Assert.True(result.Applied, result.Failure);
        var job = preparing.WorldSimulation.ProductionJobs.Single(item => item.JobId == result.JobId);
        Assert.Equal(state.Society.Society.WorldTick + recipe.DurationTicks / 2, job.CompletionTick);
        Assert.Equal(9_875, preparing.Society.Inventory.GetLot("preparation-knife").ConditionBasisPoints);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(preparing.ExportState())),
            _ => new Pick("safe_idle"));
        for (var tick = 0; tick < recipe.DurationTicks / 2; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(WorldProductionJobState.Completed, restored.WorldSimulation.ProductionJobs.Single(item => item.JobId == result.JobId).State);
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed, restored.Society.Inventory.GetReservation(id).State));
        Assert.Equal(9_875, restored.Society.Inventory.GetLot("preparation-knife").ConditionBasisPoints);
    }

    [Fact]
    public void WearCannotDamageAnotherOwnersReservedOrBrokenItemAndKeepsItsIdentity()
    {
        var original = InventoryFixture.CreateGenesis([new("axe", "wooden_axe", "alice", 1, 500, 10_000, 0)]);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.ChangeCondition(original, "axe", "bob", -500, "tool_used"));
        var reserved = InventoryFixture.Reserve(original, "trade", "alice", "axe", 1, "barter", 5);
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.ChangeCondition(reserved, "axe", "alice", -500, "tool_used"));
        var broken = InventoryFixture.ChangeCondition(original, "axe", "alice", -500, "tool_used");
        Assert.Equal("alice", broken.GetLot("axe").OwnerId);
        Assert.Equal(1, broken.GetLot("axe").Quantity);
        Assert.Null(ToolCapabilities.Best(broken.Lots, ToolKind.Axe));
        Assert.Throws<InvalidOperationException>(() => InventoryFixture.ChangeCondition(broken, "axe", "alice", 10_000, "tool_repaired"));
        Assert.Equal(500, original.GetLot("axe").ConditionBasisPoints);
    }

    [Fact]
    public async Task RepairCarriesTheWornToolToItsHouseholdsSmithAndConsumesOnSiteMaterial()
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new Pick("safe_idle"));
        var state = seed.ExportState();
        var smith = seed.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var stock = InventoryFixture.AddLot(state.Society.Society.Inventory, "repair-tool", "stone_pickaxe", actor, 1,
            conditionBasisPoints: 3_000);
        stock = InventoryFixture.AddLot(stock, "repair-hammer", "hammer", actor, 1);
        stock = InventoryFixture.AddLot(stock, "repair-stone", "stone", smith.HouseholdId!, 1, storageBuildingId: smith.InstanceId);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = stock } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = smith.Position, LastDecisionContext = null } : person).ToArray(),
        };
        using var repairing = PrivateWorldRuntime.Restore(state,
            id => new Pick(id == actor ? "repair_tool:repair-tool" : "safe_idle"));
        for (var tick = 0; tick < 8 && repairing.Society.Inventory.GetLot("repair-tool").ConditionBasisPoints < 10_000; tick++)
            Assert.True((await repairing.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(10_000, repairing.Society.Inventory.GetLot("repair-tool").ConditionBasisPoints);
        Assert.Equal(9_750, repairing.Society.Inventory.GetLot("repair-hammer").ConditionBasisPoints);
        Assert.DoesNotContain(repairing.Society.Inventory.Lots, lot => lot.Id == "repair-stone");
        Assert.Contains(repairing.ExportState().Events, item => item.Kind == "tool_repaired");
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(repairing.ExportState())));
        Assert.Equal(repairing.Society.Inventory.GetLot("repair-tool"), restored.Society.Inventory.GetLot("repair-tool"));
    }

    [Fact]
    public async Task FallenWoodKeepsTheFirstTierReplacementPossibleAfterEveryAxeBreaks()
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new Pick("safe_idle"));
        var state = seed.ExportState();
        var smith = seed.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var loose = state.Map.Resources.First(resource => resource.NaturalObjectKind == "fallen_wood");
        var stock = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != "wood")
                .Select(lot => ToolCapabilities.ForItem(lot.ItemKind)?.Kind == ToolKind.Axe
                    ? lot with { ConditionBasisPoints = 0 } : lot).ToArray(),
        };
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = stock } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = loose.Position, LastDecisionContext = null } : person).ToArray(),
        };
        using var gathering = PrivateWorldRuntime.Restore(state,
            id => new Pick(id == actor ? "haul_smith_input" : "safe_idle"));
        for (var tick = 0; tick < 8 && !gathering.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ItemKind == "wood"); tick++)
            Assert.True((await gathering.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(gathering.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "wood" && lot.Quantity == 4);
        Assert.Equal(2, gathering.ExportState().WorldSystems!.Ecology.GetResource(loose.Id).Quantity);
    }

    private sealed class Pick(string candidateId) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == candidateId) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }
}
