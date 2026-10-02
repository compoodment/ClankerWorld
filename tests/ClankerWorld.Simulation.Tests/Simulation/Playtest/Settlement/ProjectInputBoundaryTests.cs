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
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("foreign-field-stock");
        var inventory = state.Society.Society.Inventory;
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == household && lot.ItemKind is "grain_seed" or "cultivated_green_seed" or "potatoes").ToArray())
        {
            inventory = InventoryFixture.Reserve(inventory, "used-" + lot.Id, household, lot.Id, lot.Quantity, "fixture", long.MaxValue);
            inventory = InventoryFixture.ConsumeReservation(inventory, "used-" + lot.Id);
        }
        var foreign = inventory.Lots.Where(lot => lot.OwnerId != household && lot.ItemKind is "grain_seed" or "cultivated_green_seed" or "potatoes")
            .Select(lot => (lot.Id, lot.OwnerId, lot.Quantity)).ToArray();
        Assert.NotEmpty(foreign);
        state = FarmFieldTests.WithInventory(state, inventory) with { Fields = [new(point, household, FarmFieldStage.Prepared)] };
        var recorder = new ActionCoverageRecorder();
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? recorder : new Idle());
        var foreignSeed = inventory.Lots.First(lot => lot.OwnerId != household && lot.ItemKind == "grain_seed");
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.StartFieldWork(actor, point, FarmWorkKind.Plant, "grain", foreignSeed.Id).Accepted);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 6; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(recorder.OfferedByAgent[actor].Keys, id => id.StartsWith("farm:Plant:", StringComparison.Ordinal));
        Assert.Equal(foreign, world.Society.Inventory.Lots.Where(lot => lot.OwnerId != household && lot.ItemKind is "grain_seed" or "cultivated_green_seed" or "potatoes")
            .Select(lot => (lot.Id, lot.OwnerId, lot.Quantity)).ToArray());
        world.Validate();
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
    [InlineData(true)]
    public async Task ToolProjectUsesReachableWorkshopWhenFirstIsOccupied(bool reloadDuringProject)
    {
        using var seed = await Prepared();
        var workshop = seed.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
        var recipe = seed.WorldContent.Recipes.Single(item => item.LocalId == "tools");
        var initial = seed.ExportState();
        var adults = initial.Society.Society.Inhabitants.Where(person => person.HouseholdId == "household:camp-alpha").ToArray();
        var actor = adults[0].Id;
        var blocker = adults[1].Id;
        var actorPosition = initial.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var initialTown = Assert.Single(initial.Towns!);
        var reachableBuildSites = initial.Map.Tiles.Select(tile => tile.Position)
            .Where(position => initial.Map.IsBuildable(position) &&
                TownBorderRules.IsWithinOrAdjacent(initialTown, position, workshop.Width, workshop.Height) &&
                initial.Map.IsReachableOnFoot(actorPosition, position))
            .OrderBy(position => initial.Map.FootDistance(actorPosition, position))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        GridPoint? firstWorkshopPosition = null;
        var firstWorkshopFailures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var position in reachableBuildSites)
        {
            var placement = seed.PlaceBuilding("a-workshop", workshop.CanonicalId, position);
            if (!placement.Applied)
            {
                var failure = placement.Failure ?? "<no failure reason>";
                firstWorkshopFailures[failure] = firstWorkshopFailures.GetValueOrDefault(failure) + 1;
                continue;
            }

            firstWorkshopPosition = position;
            break;
        }
        Assert.True(firstWorkshopPosition.HasValue,
            $"No reachable first workshop site on {initial.Map.Width}x{initial.Map.Height} map " +
            $"({reachableBuildSites.Length} candidate tiles). Placement failures: " +
            string.Join("; ", firstWorkshopFailures.Select(item => $"{item.Key} ({item.Value})")));
        GridPoint? secondWorkshopPosition = null;
        var secondWorkshopFailures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var position in reachableBuildSites.Where(position => position != firstWorkshopPosition.Value &&
                     initial.Inhabitants.All(person => person.Position != position)))
        {
            var placement = seed.PlaceBuilding("z-workshop", workshop.CanonicalId, position);
            if (!placement.Applied)
            {
                var failure = placement.Failure ?? "<no failure reason>";
                secondWorkshopFailures[failure] = secondWorkshopFailures.GetValueOrDefault(failure) + 1;
                continue;
            }

            secondWorkshopPosition = position;
            break;
        }
        Assert.True(secondWorkshopPosition.HasValue,
            $"No reachable second workshop site on {initial.Map.Width}x{initial.Map.Height} map " +
            $"({reachableBuildSites.Length} candidate tiles). " +
            $"Placement failures: {string.Join("; ", secondWorkshopFailures.Select(item => $"{item.Key} ({item.Value})"))}");
        var state = seed.ExportState();
        // No role: a communal Workshop serves any Town resident.
        var society = state.Society.Society;
        Assert.Equal(SocietyWorkRole.Unassigned, society.GetInhabitant(actor).CurrentRole);
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
                Position = person.InhabitantId == blocker ? firstWorkshopPosition.Value : person.Position,
                HungerBasisPoints = 9_500,
            }).ToArray(),
        };
        var chooser = new ToolChooser("build:recipe:" + recipe.CanonicalId);
        IDecisionProvider Provider(string id) => id == actor ? chooser : new Idle();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), Provider);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(chooser.SelectedRecipe, string.Join(", ", chooser.LastCandidateIds));
        var checkpoint = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = reloadDuringProject ? PrivateWorldRuntime.Restore(checkpoint, Provider) : null;
        var continuing = restored ?? world;
        for (var tick = 0; tick < 60 && !continuing.WorldSimulation.ProductionJobs.Any(job =>
                 job.WorkerId == actor && job.State == WorldProductionJobState.Completed); tick++)
            Assert.True((await continuing.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(continuing.WorldSimulation.ProductionJobs, job => job.WorkerId == actor &&
            job.BuildingInstanceId == "z-workshop" && job.State == WorldProductionJobState.Completed);
        Assert.Equal(firstWorkshopPosition.Value, continuing.Inhabitants.Single(person => person.InhabitantId == blocker).Position);
        Assert.DoesNotContain(continuing.WorldSimulation.ProductionJobs, job => job.WorkerId == actor && job.BuildingInstanceId == "a-workshop");
        Assert.Contains(continuing.Society.Inventory.Lots, lot => lot.OwnerId == "household:camp-alpha" && lot.ItemKind == "tool" && lot.Quantity > 0);
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(continuing.ExportState()));
    }

    private sealed class ToolChooser(string recipeCandidate) : IDecisionProvider
    {
        public bool SelectedRecipe { get; private set; }
        public IReadOnlyList<string> LastCandidateIds { get; private set; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            LastCandidateIds = request.Observation.Candidates.Select(candidate => candidate.Id).ToArray();
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
