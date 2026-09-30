using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ProjectInputBoundaryTests
{
    [Fact]
    public async Task CropPreparationDoesNotUseAnotherHouseholdsSeeds()
    {
        using var seed = await Prepared();
        var state = seed.ExportState();
        var beta = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-beta").Id;
        var recipe = seed.WorldContent.Recipes.Single(item => item.LocalId == "grain-plot");
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == "household:camp-beta" && lot.ItemKind == "seed").ToArray())
        {
            inventory = InventoryFixture.Reserve(inventory, "use-" + lot.Id, lot.OwnerId, lot.Id, lot.Quantity, "used", 1000);
            inventory = InventoryFixture.ConsumeReservation(inventory, "use-" + lot.Id);
        }
        var alphaSeeds = inventory.Lots.Where(lot => lot.OwnerId == "household:camp-alpha" && lot.ItemKind == "seed").Sum(lot => lot.Quantity);
        Assert.True(alphaSeeds > 0);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == beta ? person with
            {
                HungerBasisPoints = 9_500,
                Project = new SettlementProject("build:recipe:" + recipe.CanonicalId, "Plant", state.Society.Society.WorldTick, "working", 10,
                    LastTransitionTick: state.Society.Society.WorldTick),
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new Idle());
        _ = await world.AdvanceOneTickAsync();
        Assert.NotEqual("working", world.Inhabitants.Single(person => person.InhabitantId == beta).Project!.Stage);
        Assert.DoesNotContain(world.WorldSimulation.CropBuilds ?? [], job => job.WorkerId == beta);
        Assert.Equal(alphaSeeds, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == "household:camp-alpha" && lot.ItemKind == "seed").Sum(lot => lot.Quantity));
        _ = PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    [Fact]
    public async Task CookingWaitsForOnSiteIngredientsAndCanResumeAfterTheyReturn()
    {
        using var seed = await Prepared();
        var state = seed.ExportState();
        var beta = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-beta").Id;
        var house = seed.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
        var recipe = seed.WorldContent.Recipes.Single(item => item.LocalId == "house-meal");
        BuildingPlacementResult? placement = null;
        foreach (var point in state.Map.Tiles.Select(tile => tile.Position))
        {
            var candidate = seed.PlaceBuilding("cook-home", house.CanonicalId, point, "household:camp-beta");
            if (!candidate.Applied) continue;
            placement = candidate;
            break;
        }
        Assert.NotNull(placement);
        var placed = seed.WorldSimulation.Buildings.Single(item => item.InstanceId == "cook-home");
        state = seed.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == beta ? person with
            {
                Position = placed.Position,
                HungerBasisPoints = 9_500,
                Project = new SettlementProject("build:recipe:" + recipe.CanonicalId, "Cook", state.Society.Society.WorldTick, "working", 10,
                    LastTransitionTick: state.Society.Society.WorldTick),
            } : person).ToArray(),
        };
        using var blocked = PrivateWorldRuntime.Restore(state, _ => new Idle());
        _ = await blocked.AdvanceOneTickAsync();
        Assert.Equal("blocked", blocked.Inhabitants.Single(person => person.InhabitantId == beta).Project!.Stage);
        Assert.Empty(blocked.WorldSimulation.ProductionJobs);
        state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(blocked.ExportState()));
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Lots = state.Society.Society.Inventory.Lots.Select(lot =>
                        lot.OwnerId == "household:camp-beta" && lot.ItemKind is "food" or "wood"
                            ? lot with { StorageBuildingId = "cook-home" } : lot).ToArray()
                    },
                }
            }
        };
        using var replenished = PrivateWorldRuntime.Restore(state, _ => new Idle());
        for (var tick = 0; tick < 80 && replenished.WorldSimulation.ProductionJobs.Count == 0; tick++)
            _ = await replenished.AdvanceOneTickAsync();
        Assert.Contains(replenished.WorldSimulation.ProductionJobs, job => job.WorkerId == beta && job.RecipeId == recipe.CanonicalId);
        _ = PrivateWorldRuntimeCodec.Encode(replenished.ExportState());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolProjectUsesReachableWorkshopWhenFirstIsOccupied(bool reloadDuringProject)
    {
        using var seed = await Prepared();
        var workshop = seed.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
        var recipe = seed.WorldContent.Recipes.Single(item => item.LocalId == "tools");
        Assert.True(seed.PlaceBuilding("a-workshop", workshop.CanonicalId, new GridPoint(5, 0)).Applied);
        Assert.True(seed.PlaceBuilding("z-workshop", workshop.CanonicalId, new GridPoint(3, 1)).Applied);
        var state = seed.ExportState();
        var adults = state.Society.Society.Inhabitants.Where(person => person.HouseholdId == "household:camp-alpha").ToArray();
        var actor = adults[0].Id;
        var blocker = adults[1].Id;
        var society = SocietyFixture.AssignRole(state.Society.Society, actor, SocietyWorkRole.Trader).Checkpoint;
        var inventory = society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.ItemKind == "tool" && lot.Quantity > 0).ToArray())
        {
            inventory = InventoryFixture.Reserve(inventory, "use-" + lot.Id, lot.OwnerId, lot.Id, lot.Quantity, "used", 1000);
            inventory = InventoryFixture.ConsumeReservation(inventory, "use-" + lot.Id);
        }
        inventory = InventoryFixture.AddLot(inventory, "crafting-tool", "tool", actor, 1);
        state = state with
        {
            Society = state.Society with { Society = society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == blocker ? new GridPoint(5, 0) : person.Position,
                HungerBasisPoints = 9_500,
            }).ToArray(),
        };
        var chooser = new ToolChooser("build:recipe:" + recipe.CanonicalId);
        IDecisionProvider Provider(string id) => id == actor ? chooser : new Idle();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), Provider);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(chooser.SelectedRecipe);
        var checkpoint = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = reloadDuringProject ? PrivateWorldRuntime.Restore(checkpoint, Provider) : null;
        var continuing = restored ?? world;
        for (var tick = 0; tick < 60 && !continuing.WorldSimulation.ProductionJobs.Any(job =>
                 job.WorkerId == actor && job.State == WorldProductionJobState.Completed); tick++)
            Assert.True((await continuing.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(continuing.WorldSimulation.ProductionJobs, job => job.WorkerId == actor &&
            job.BuildingInstanceId == "z-workshop" && job.State == WorldProductionJobState.Completed);
        Assert.Equal(new GridPoint(5, 0), continuing.Inhabitants.Single(person => person.InhabitantId == blocker).Position);
        Assert.DoesNotContain(continuing.WorldSimulation.ProductionJobs, job => job.WorkerId == actor && job.BuildingInstanceId == "a-workshop");
        Assert.Contains(continuing.Society.Inventory.Lots, lot => lot.OwnerId == "household:camp-alpha" && lot.ItemKind == "tool" && lot.Quantity > 0);
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(continuing.ExportState()));
    }

    private sealed class ToolChooser(string recipeCandidate) : IDecisionProvider
    {
        public bool SelectedRecipe { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == recipeCandidate)
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            SelectedRecipe |= choice.Id == recipeCandidate;
            return new DeterministicDecisionProvider().DecideAsync(request with { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }

    private static async Task<PrivateWorldRuntime> Prepared()
    {
        var world = new PrivateWorldRuntime("project-input-boundary", _ => new Idle(), startPace: WorldStartPace.FounderSetup);
        var positions = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var i = 0; i < positions.Length; i++) world.PlaceFounder("founder:" + (i + 1).ToString("x32", CultureInfo.InvariantCulture), positions[i]);
        world.StartWorld();
        world.StageStarterContent();
        for (var tick = 0; tick < 9; tick++) _ = await world.AdvanceOneTickAsync();
        return world;
    }

    private sealed class Idle : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var idle = request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with { Observation = request.Observation with { Candidates = [idle] } }, cancellationToken);
        }
    }
}
