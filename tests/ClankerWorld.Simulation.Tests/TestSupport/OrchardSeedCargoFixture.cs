using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class OrchardSeedCargoFixture : IAsyncLifetime
{
    private byte[]? _baseline;

    public string Actor { get; private set; } = string.Empty;
    public string Household { get; private set; } = string.Empty;
    private const string BasketLotId = "food-recovery-basket";
    public IReadOnlyList<string> SeedLotIds { get; private set; } = [];

    public PrivateWorldRuntimeState CreateState() => PrivateWorldRuntimeCodec.Decode(
        _baseline ?? throw new InvalidOperationException("The orchard seed cargo fixture has not been initialized."));

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        var state = GeographyGeneratorTests.StartedGeneratedWorld(new GeographyOptions("orchard-reserve-deadlock", WorldSizePreset.Small));
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        using var probe = PrivateWorldRuntime.Restore(state);
        var blocked = probe.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                probe.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building.Position))
            .Concat(probe.RoadTiles).Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var occupied = state.Inhabitants.Skip(1).Select(person => person.Position).ToHashSet();
        var start = state.Inhabitants[0].Position;
        var site = state.Map.Tiles.Select(tile => tile.Position).Where(point => !blocked.Contains(point) &&
                TreeGrowthRules.GroundRefusal(state.Map, point) is null && state.Map.IsReachableOnFoot(start, point))
            .OrderBy(point => state.Map.FootDistance(start, point)).First();
        var stand = state.Map.FootNeighbors(site).First(point => !blocked.Contains(point) && !occupied.Contains(point));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, BasketLotId, "basket", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "food-recovery-orchard-start", "orchard_seed", actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = stand, HungerBasisPoints = 6_000, Equipment = new(CarryAidLotId: BasketLotId) } : person).ToArray(),
        };
        using (var planting = PrivateWorldRuntime.Restore(state, _ => new Choices([])))
        {
            var result = planting.PlantTree(actor, TreeGrowthRules.Orchard, "food-recovery-orchard-start", site);
            Assert.True(result.Planted, result.Message);
            state = planting.ExportState();
        }
        var treeId = TreeGrowthRules.PlantedTreeId(site);
        for (var pick = 0; pick < 5; pick++)
        {
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { Position = stand, HungerBasisPoints = 6_000 } : person).ToArray(),
                WorldSystems = state.WorldSystems! with
                {
                    Ecology = state.WorldSystems.Ecology with
                    {
                        Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == treeId
                            ? TreeGrowthAndPlantingTests.InFruitingSeason(resource, state) with { IsPlanted = false } : resource).ToArray(),
                    },
                },
            };
            var before = state.Events.Count(item => item.Kind == "fruit_harvested" && item.Detail.Contains(treeId, StringComparison.Ordinal));
            using var harvest = PrivateWorldRuntime.Restore(RoundTrip(state), id => new Choices(id == actor ? ["harvest_food"] : []));
            for (var tick = 0; tick < 12 && harvest.ExportState().Events.Count(item => item.Kind == "fruit_harvested" &&
                     item.Detail.Contains(treeId, StringComparison.Ordinal)) == before; tick++)
                Assert.True((await harvest.AdvanceOneTickAsync()).Advanced);
            state = harvest.ExportState();
            Assert.Equal(before + 1, state.Events.Count(item => item.Kind == "fruit_harvested" && item.Detail.Contains(treeId, StringComparison.Ordinal)));
            inventory = state.Society.Society.Inventory;
            foreach (var food in inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "fruit").ToArray())
                inventory = Consume(inventory, food);
            state = WithInventory(state, inventory);
        }
        inventory = state.Society.Society.Inventory;
        var seeds = inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "orchard_seed").ToArray();
        Assert.Equal(5, seeds.Length);
        Assert.All(seeds, seed => Assert.Equal(1, seed.Quantity));
        Assert.Equal(5, inventory.Reservations.Count(reservation => reservation.OwnerId == actor &&
            reservation.Purpose == "orchard_replanting" && reservation.State == InventoryReservationState.Reserved));
        foreach (var food in inventory.Lots.Where(lot => (lot.OwnerId == actor || lot.OwnerId == household) &&
                     lot.ItemKind is "food" or "berries" or "fruit" or "wild_greens" or "cultivated_greens" or "meal" or "simple_meal" or "bread" or "porridge").ToArray())
            inventory = Consume(inventory, food);
        inventory = InventoryFixture.WearSingleUnit(inventory, BasketLotId, inventory.GetLot(BasketLotId).ConditionBasisPoints - 10);
        // Move once toward a real ordinary patch; actual movement wear breaks the basket.
        var target = state.Map.Resources.Where(resource => resource.Kind == "food" && resource.TreeKind is null &&
                state.WorldSystems!.Ecology.GetResource(resource.Id).Quantity > 0 && state.Map.IsReachableOnFoot(start, resource.Position))
            .OrderBy(resource => state.Map.FootDistance(start, resource.Position))
            .Select(resource => (Resource: resource, Stands: state.Map.FootNeighbors(resource.Position)
                .SelectMany(point => state.Map.FootNeighbors(point)).Where(point => state.Map.FootDistance(point, resource.Position) == 2 &&
                    !blocked.Contains(point) && !occupied.Contains(point)).ToArray()))
            .First(item => item.Stands.Length > 0);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = target.Stands[0], HungerBasisPoints = 1_000 } : person).ToArray(),
        };
        var movement = new Choices(["seek_food"]);
        using (var walking = PrivateWorldRuntime.Restore(RoundTrip(state), id => id == actor ? movement : new Choices([])))
        {
            for (var tick = 0; tick < 12 && walking.Society.Inventory.GetLot(BasketLotId).ConditionBasisPoints > 0; tick++)
                Assert.True((await walking.AdvanceOneTickAsync()).Advanced);
            state = walking.ExportState();
        }
        Assert.Contains("seek_food", movement.Offered);
        inventory = state.Society.Society.Inventory;
        Assert.Equal(0, inventory.GetLot(BasketLotId).ConditionBasisPoints);
        var physical = state.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Equal(8, PersonalEquipmentRules.Capacity(inventory, actor, physical.Equipment));
        Assert.Equal(3, PersonalEquipmentRules.FreeCapacity(inventory, actor, physical.Equipment));
        Actor = actor;
        Household = household;
        SeedLotIds = Array.AsReadOnly(seeds.Select(seed => seed.Id).ToArray());
        _baseline = PrivateWorldRuntimeCodec.Encode(state);
    }

    private static InventoryCheckpoint Consume(InventoryCheckpoint inventory, InventoryLot food)
    {
        var id = "fixture-consume:" + food.Id;
        inventory = InventoryFixture.Reserve(inventory, id, food.OwnerId, food.Id, food.Quantity, "prepare orchard seed cargo fixture", long.MaxValue);
        return InventoryFixture.ConsumeReservation(inventory, id);
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntimeState RoundTrip(PrivateWorldRuntimeState state) =>
        PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));

    public sealed class Choices(string[] prefixes) : IDecisionProvider
    {
        public List<string> Offered { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered.AddRange(request.Observation.Candidates.Select(candidate => candidate.Id));
            var selected = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == prefix))
                .FirstOrDefault(candidate => candidate is not null) ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}
