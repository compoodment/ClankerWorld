using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorkstationSupplyReserveTests
{
    [Fact]
    public async Task ReadyHouseWritingConsumesItsCoverRatherThanShuttlingItToTheTailor()
    {
        var state = await ReadyWorldAsync(houseCover: true);
        var actor = state.Inhabitants[0].InhabitantId;
        var provider = new SupplyChoiceProvider();
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new IdleProvider());
        for (var tick = 0; tick < 60 && !world.Knowledge.Artifacts.Any(item => item.Kind == "book"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var book = Assert.Single(world.Knowledge.Artifacts, item => item.Kind == "book");
        var inventory = world.Society.Inventory;
        Assert.All(book.InputReservationIds!.Select(inventory.GetReservation), item => Assert.Equal(InventoryReservationState.Completed, item.State));
        Assert.Equal(3, book.InputReservationIds!.Select(inventory.GetReservation).Sum(item => item.Quantity));
        Assert.DoesNotContain(inventory.Lots, lot => lot.Id == "house-cover");
        Assert.Equal(2, inventory.GetLot("tailor-cloth").Quantity);
        Assert.DoesNotContain(inventory.Events, item => item.Kind == "inventory_transferred" &&
            item.Detail.Contains(":house-cover:", StringComparison.Ordinal));
        Assert.DoesNotContain(provider.Offered, item => item.Id == "supply_workstation:cloth" && item.DestinationId == "reserve-tailor");
        world.Validate();
    }

    [Fact]
    public async Task OneReadySewingBatchStaysAtTheTailorAndCanBeConsumedByRealProduction()
    {
        var state = await ReadyWorldAsync(houseCover: false);
        var actor = state.Inhabitants[0].InhabitantId;
        var provider = new SupplyChoiceProvider();
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new IdleProvider());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2, world.Society.Inventory.GetLot("tailor-cloth").Quantity);
        Assert.DoesNotContain(provider.Offered, item => item.Id == "knowledge_supply:cloth");
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "sew-clothing");
        var start = world.StartProduction(recipe.CanonicalId, "reserve-tailor", actor);
        Assert.True(start.Applied, start.Failure);
        for (var tick = 0; tick < 30; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var job = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(WorldProductionJobState.Completed, job.State);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "tailor-cloth");
        Assert.Equal(("clothing", 1), (world.Society.Inventory.GetLot(job.JobId + ":output:00").ItemKind,
            world.Society.Inventory.GetLot(job.JobId + ":output:00").Quantity));
        Assert.Empty(world.Knowledge.Artifacts);
        world.Validate();
    }

    private static async Task<PrivateWorldRuntimeState> ReadyWorldAsync(bool houseCover)
    {
        using var seed = new PrivateWorldRuntime("workstation-supply-reserves", _ => new IdleProvider());
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 8; tick++) Assert.True((await seed.AdvanceOneTickAsync()).Advanced);
        var state = seed.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var occupied = state.Map.Resources.Select(item => item.Position).Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)))
            .Concat(state.Inhabitants.Select(person => person.Position)).ToHashSet();
        var sites = state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) && !occupied.Contains(tile.Position))
            .OrderBy(tile => state.Map.FootDistance(state.Inhabitants[0].Position, tile.Position)).Take(2).Select(tile => tile.Position).ToArray();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "reserve-wood", "wood", household, 16);
        inventory = InventoryFixture.AddLot(inventory, "reserve-build-fiber", "fiber", household, 2);
        using var placing = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } }, _ => new IdleProvider());
        Assert.True(placing.PlaceBuilding("reserve-house", HouseContent.House1x1().CanonicalId, sites[0], household).Applied);
        Assert.True(placing.PlaceBuilding("reserve-tailor", TailorContent.TailorShop1x1().CanonicalId, sites[1], household).Applied);
        state = placing.ExportState();
        inventory = state.Society.Society.Inventory;
        foreach (var (site, definition) in new[] { ("reserve-house", HouseContent.House1x1().CanonicalId), ("reserve-tailor", TailorContent.TailorShop1x1().CanonicalId) })
            foreach (var inputs in state.WorldContent!.Recipes.Where(recipe => recipe.WorkstationBuildingId == definition)
                .SelectMany(recipe => recipe.Inputs).Where(input => input.ResourceId != "cloth").GroupBy(input => input.ResourceId))
            {
                var quantity = inputs.Max(input => input.Amount) * 2;
                var id = site + "-" + inputs.Key;
                if (inputs.Key is "water" or "milk")
                {
                    var jug = id + "-jug";
                    inventory = InventoryFixture.AddLot(inventory, jug, "water_jug", household, 1, storageBuildingId: site, containerCapacity: 8);
                    inventory = InventoryFixture.AddLot(inventory, id, inputs.Key, household, quantity, storageBuildingId: site, containerLotId: jug);
                }
                else inventory = InventoryFixture.AddLot(inventory, id, inputs.Key, household, quantity, storageBuildingId: site);
            }
        inventory = InventoryFixture.AddLot(inventory, "house-paper", "paper", household, 2, storageBuildingId: "reserve-house");
        inventory = InventoryFixture.AddLot(inventory, "tailor-cloth", "cloth", household, 2, storageBuildingId: "reserve-tailor");
        if (houseCover) inventory = InventoryFixture.AddLot(inventory, "house-cover", "cloth", household, 1, storageBuildingId: "reserve-house");
        var position = sites[0];
        var fact = new AgentKnowledgeFact("reserve-known-site", actor, actor, position,
            state.Map.TerrainKindAt(position)!.Value.ToString(), [], state.Society.Society.WorldTick, "firsthand");
        return state with
        {
            Knowledge = new([fact], []),
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = houseCover ? sites[0] : sites[1],
                HungerBasisPoints = 10_000,
                LastDecisionContext = null
            } : person).ToArray()
        };
    }

    private sealed class SupplyChoiceProvider : IDecisionProvider
    {
        public List<CognitionCandidate> Offered { get; } = [];
        private bool collectedSupply;
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered.AddRange(request.Observation.Candidates);
            var choices = request.Observation.Candidates.Where(item => item.Id is "supply_workstation:cloth" or "knowledge_supply:cloth" or "knowledge_write:book" ||
                collectedSupply && item.Id == "haul_household_stock").OrderBy(item => item.DeterministicPriority).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
            var choice = choices.FirstOrDefault() ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            if (choice.Id is "supply_workstation:cloth" or "knowledge_supply:cloth") collectedSupply = true;
            return new DeterministicDecisionProvider().DecideAsync(request with { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.Single(item => item.Id == "safe_idle")]
                }
            }, cancellationToken);
    }
}
