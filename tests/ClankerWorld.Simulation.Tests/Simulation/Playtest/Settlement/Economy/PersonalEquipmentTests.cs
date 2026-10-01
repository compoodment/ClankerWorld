using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class PersonalEquipmentTests
{
    private const string Alpha = "household:camp-alpha";

    [Fact]
    public void CapacityCountsDeliveredCargoOnceAndDoesNotUseAStoredOrBrokenSack()
    {
        var inventory = InventoryFixture.CreateGenesis([
            new("sack", "sack", "person", 1, 10_000, 10_000, 0), new("coat", "padded_coat", "person", 1, 10_000, 10_000, 0),
            new("wood", "wood", "person", 8, 10_000, 10_000, 0, DeliveryBuildingId: "house"),
            new("pot", "jug", "person", 1, 10_000, 10_000, 0), new("water", "water", "person", 3, 10_000, 10_000, 0),
        ]);
        var equipment = new PersonalEquipment("coat", "sack");
        Assert.Equal(12, PersonalEquipmentRules.CarriedQuantity(inventory, "person", equipment));
        Assert.Equal(12, PersonalEquipmentRules.FreeCapacity(inventory, "person", equipment));
        var broken = InventoryFixture.WearSingleUnit(inventory, "sack", 10_000);
        Assert.Equal(8, PersonalEquipmentRules.Capacity(broken, "person", equipment));
        Assert.Equal(12, PersonalEquipmentRules.CarriedQuantity(broken, "person", equipment));
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(broken, "person", equipment));
        var stored = InventoryFixture.Transfer(inventory, "store", "person", Alpha, "sack", 1, "store", "house");
        Assert.Equal(8, PersonalEquipmentRules.Capacity(stored, "person", equipment));
        Assert.Equal(12, PersonalEquipmentRules.CarriedQuantity(stored, "person", equipment));
    }

    [Fact]
    public void CoatsAndCloaksProtectAgainstTheirWeatherAndWearReducesProtection()
    {
        var basic = new InventoryLot("basic", "clothing", "person", 1, 10_000, 10_000, 0);
        var coat = new InventoryLot("coat", "padded_coat", "person", 1, 10_000, 10_000, 0);
        var cloak = new InventoryLot("cloak", "rain_cloak", "person", 1, 10_000, 10_000, 0);
        Assert.True(PersonalEquipmentRules.Protection(coat, WeatherKind.Snow) > PersonalEquipmentRules.Protection(basic, WeatherKind.Snow));
        Assert.True(PersonalEquipmentRules.Protection(cloak, WeatherKind.Storm) > PersonalEquipmentRules.Protection(basic, WeatherKind.Storm));
        Assert.True(PersonalEquipmentRules.Protection(coat, WeatherKind.Snow) > PersonalEquipmentRules.Protection(coat with { ConditionBasisPoints = 2_000 }, WeatherKind.Snow));
        Assert.Equal(0, PersonalEquipmentRules.Protection(cloak with { ConditionBasisPoints = 0 }, WeatherKind.Rain));
    }

    [Fact]
    public async Task FiberBecomesRopeAndAnEquippedBasketThroughHouseWorkAcrossReload()
    {
        using var generated = NormalPathWorld.CreateGenerated("equipment-basket-loop", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == Alpha).Id;
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "basket-fiber", "fiber", Alpha, 6, storageBuildingId: house.InstanceId);
        state = WithInventory(state, inventory);
        state = state with { Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == actor
            ? item with { HungerBasisPoints = 9_000 } : item).ToArray() };
        var rope = state.WorldContent!.Recipes.Single(item => item.LocalId == "twist-rope");
        var basket = state.WorldContent.Recipes.Single(item => item.LocalId == "weave-basket");
        string[] choices = ["equip_carry_aid", "build:recipe:" + basket.CanonicalId, "build:recipe:" + rope.CanonicalId];
        var actorProvider = new Choices(choices);
        IDecisionProvider Provider(string id) => id == actor ? actorProvider : new Choices([]);
        var world = PrivateWorldRuntime.Restore(state, Provider);
        try
        {
            var reopened = false;
            for (var tick = 0; tick < 300 && world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.CarryAidLotId is null; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!reopened && world.WorldSimulation.ProductionJobs.Any(job => job.RecipeId == basket.CanonicalId && job.State == WorldProductionJobState.Running))
                {
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, Provider);
                    reopened = true;
                }
            }
            Assert.True(reopened, "Jobs: " + string.Join(", ", world.WorldSimulation.ProductionJobs.Select(job => job.RecipeId + ":" + job.State)) +
                " Offers: " + string.Join("; ", actorProvider.Offers.TakeLast(4)));
            var person = world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor);
            var equipped = world.Society.Inventory.GetLot(Assert.IsType<string>(person.Equipment?.CarryAidLotId));
            Assert.Equal(("basket", actor, 1), (equipped.ItemKind, equipped.OwnerId, equipped.Quantity));
            Assert.Null(equipped.StorageBuildingId);
            Assert.Equal(16, PersonalEquipmentRules.Capacity(world.Society.Inventory, actor, person.Equipment));
            Assert.Equal(0, world.Society.Inventory.Lots.Where(lot => lot.Id == "basket-fiber").Sum(lot => lot.Quantity));
            Assert.Equal(2, world.WorldSimulation.ProductionJobs.Count(job => job.State == WorldProductionJobState.Completed));
            world.Validate();
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public async Task TimedRepairResumesAfterCodecReloadAndSpendsOnlyItsReservedCloth()
    {
        var (state, shop) = TailorTestWorld.Create("equipment-repair", 0);
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == Alpha).Id;
        var position = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == shop).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "repair-coat", "padded_coat", actor, 1);
        inventory = InventoryFixture.WearSingleUnit(inventory, "repair-coat", 8_000);
        inventory = InventoryFixture.AddLot(inventory, "repair-cloth", "cloth", actor, 2);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == actor
                ? item with { Position = position, HungerBasisPoints = 9_000, Survival = new(), Equipment = new("repair-coat") } : item).ToArray(),
        };
        IDecisionProvider Provider(string id) => new Choices(id == actor ? ["repair_equipment"] : []);
        using var first = PrivateWorldRuntime.Restore(state, Provider);
        for (var tick = 0; tick < 30 && first.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.Repair is null; tick++)
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
        var working = first.ExportState();
        var repair = Assert.IsType<EquipmentRepairWork>(working.Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.Repair);
        Assert.Equal(2, first.Society.Inventory.GetLot("repair-cloth").Quantity);
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(working)), Provider);
        for (var tick = 0; tick < 12 && resumed.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.Repair is not null; tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Null(resumed.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.Repair);
        Assert.Equal(1, resumed.Society.Inventory.GetLot("repair-cloth").Quantity);
        Assert.InRange(resumed.Society.Inventory.GetLot("repair-coat").ConditionBasisPoints, 7_800, 8_000);
        Assert.All(repair.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Completed, resumed.Society.Inventory.GetReservation(id).State));
        resumed.Validate();

        var interrupted = working with { Inhabitants = working.Inhabitants.Select(item => item.InhabitantId == actor ? item with { HungerBasisPoints = 1_000 } : item).ToArray() };
        using var cancelled = PrivateWorldRuntime.Restore(interrupted, Provider);
        Assert.True((await cancelled.AdvanceOneTickAsync()).Advanced);
        Assert.Null(cancelled.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.Repair);
        Assert.Equal(2, cancelled.Society.Inventory.GetLot("repair-cloth").Quantity);
        Assert.All(repair.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Released, cancelled.Society.Inventory.GetReservation(id).State));
        cancelled.Validate();
    }

    [Fact]
    public async Task ReplacingABrokenBasketKeepsItsOverloadedCargoAndTheOldBasket()
    {
        using var generated = NormalPathWorld.CreateGenerated("equipment-replacement", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == Alpha).Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "old-basket", "basket", actor, 1);
        inventory = InventoryFixture.WearSingleUnit(inventory, "old-basket", 10_000);
        inventory = InventoryFixture.AddLot(inventory, "overloaded-wood", "wood", actor, 12);
        inventory = InventoryFixture.AddLot(inventory, "replacement-sack", "sack", Alpha, 1, storageBuildingId: "first-town-house-a");
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == actor
            ? item with { Equipment = new(CarryAidLotId: "old-basket") } : item).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state, id => new Choices(id == actor ? ["equip_carry_aid"] : []));
        for (var tick = 0; tick < 120 && world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor).Equipment?.CarryAidLotId != "replacement-sack"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var person = world.ExportState().Inhabitants.Single(item => item.InhabitantId == actor);
        Assert.Equal("replacement-sack", person.Equipment?.CarryAidLotId);
        Assert.Equal((actor, 1, 0), (world.Society.Inventory.GetLot("old-basket").OwnerId, world.Society.Inventory.GetLot("old-basket").Quantity, world.Society.Inventory.GetLot("old-basket").ConditionBasisPoints));
        Assert.Equal(12, world.Society.Inventory.GetLot("overloaded-wood").Quantity);
        var reloaded = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(person.Equipment, reloaded.Inhabitants.Single(item => item.InhabitantId == actor).Equipment);
        world.Validate();
    }

    [Fact]
    public void CurrentSaveRejectsAnEquippedStackOrAnotherPersonsItem()
    {
        using var generated = NormalPathWorld.CreateGenerated("equipment-invalid", _ => new ActionCoverageRecorder(chooseIdle: true));
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        foreach (var (owner, quantity) in new[] { (actor, 2), (state.Inhabitants[1].InhabitantId, 1) })
        {
            var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "invalid-sack", "sack", owner, quantity);
            var invalid = WithInventory(state, inventory) with
            {
                Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == actor
                ? item with { Equipment = new(CarryAidLotId: "invalid-sack") } : item).ToArray()
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(invalid)));
        }
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private sealed class Choices(IReadOnlyList<string> allowed) : IDecisionProvider
    {
        public List<string> Offers { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offers.Add(string.Join(",", request.Observation.Candidates.Select(item => item.Id)));
            var permitted = request.Observation.Candidates.Where(item => allowed.Any(prefix => item.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .Select(item => item with
                {
                    DeterministicPriority = allowed.Select((prefix, index) => (prefix, index))
                    .First(entry => item.Id.StartsWith(entry.prefix, StringComparison.Ordinal)).index
                }).ToArray();
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                { Candidates = permitted.Length > 0 ? permitted : [request.Observation.Candidates.Single(item => item.Id == "safe_idle")] }
            }, cancellationToken);
        }
    }
}
