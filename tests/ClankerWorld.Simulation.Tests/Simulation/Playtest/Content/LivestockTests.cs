using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class LivestockTests
{
    [Theory]
    [InlineData(LivestockKind.Chicken, "eggs", 1)]
    [InlineData(LivestockKind.Cow, "milk", 2)]
    [InlineData(LivestockKind.Sheep, "wool", 2)]
    public async Task ActualCareProducesOnlyLocalStockAndCollectionReplays(LivestockKind kind, string product, int quantity)
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new Pick());
        var (state, actor, house) = AtHouse(seed);
        state = WithInventory(state, Supplies(state.Society.Society.Inventory, actor));
        using var world = PrivateWorldRuntime.Restore(state, _ => new Pick());
        Assert.True(world.PlaceAcquiredLivestock("animal:test", kind, house.HouseholdId!, house.Position, "existing-herd:test").Applied);
        var other = world.Society.Inhabitants.First(person => person.HouseholdId != house.HouseholdId).Id;
        Assert.False(world.CareForLivestock(other, "animal:test", "feed").Applied);
        Assert.True(world.CareForLivestock(actor, "animal:test", "feed").Applied);
        Assert.True(world.CareForLivestock(actor, "animal:test", "water").Applied);
        Assert.True(world.CareForLivestock(actor, "animal:test", "care").Applied);
        Assert.Equal(7, world.Society.Inventory.GetLot("animal-feed").Quantity);
        Assert.Equal(7, world.Society.Inventory.GetLot("animal-water").Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("animal-water-jug").Quantity);
        for (var tick = 0; tick < LivestockRules.ProductTicks; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(quantity, Assert.Single(world.Livestock).PendingProductQuantity);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ItemKind == product);
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        Assert.Equal(quantity, Assert.Single(snapshot.Livestock).ProductQuantity);
        Assert.False(world.CollectLivestockProduct(other, "animal:test").Applied);
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        saved = FreshChoice(saved, actor);
        using var first = PrivateWorldRuntime.Restore(saved, id => new Pick(id == actor ? "animal_collect:" : "safe_idle"));
        using var replay = PrivateWorldRuntime.Restore(saved, id => new Pick(id == actor ? "animal_collect:" : "safe_idle"));
        for (var tick = 0; tick < 8 && first.Livestock[0].PendingProductQuantity > 0; tick++)
        {
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(0, first.Livestock[0].PendingProductQuantity);
        var output = Assert.Single(first.Society.Inventory.Lots, lot => lot.ItemKind == product);
        Assert.Equal(quantity, output.Quantity);
        Assert.Equal(actor, output.OwnerId);
        Assert.Null(output.StorageBuildingId);
        Assert.Equal(house.Position, first.Livestock[0].Position);
        if (product == "milk")
        {
            Assert.Equal("animal-empty-jug", output.ContainerLotId);
            Assert.Equal(1, first.Society.Inventory.GetLot("animal-empty-jug").Quantity);
            Assert.Throws<InvalidOperationException>(() => InventoryFixture.Transfer(first.Society.Inventory,
                "loose-milk", actor, other, output.Id, 1, "give"));
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(first.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Fact]
    public async Task NormalCareHaulsHouseholdJugWaterCollectsMilkAndCooksItAtItsHouse()
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new Pick());
        var (state, actor, house) = AtHouse(seed);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "herd-house-grain", "grain", house.HouseholdId!, 8,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "herd-house-water-jug", "water_jug", house.HouseholdId!, 1,
            storageBuildingId: house.InstanceId, containerCapacity: 8);
        inventory = InventoryFixture.AddLot(inventory, "herd-house-water", "water", house.HouseholdId!, 8,
            storageBuildingId: house.InstanceId, containerLotId: "herd-house-water-jug");
        inventory = InventoryFixture.AddLot(inventory, "herd-empty-milk-jug", "water_jug", actor, 1, containerCapacity: 8);
        inventory = InventoryFixture.AddLot(inventory, "herd-cooking-fuel", "wood", house.HouseholdId!, 1, storageBuildingId: house.InstanceId);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), id => new Pick(id == actor ? "herd-normal" : "safe_idle"));
        Assert.True(world.PlaceAcquiredLivestock("animal:cow", LivestockKind.Cow, house.HouseholdId!, house.Position, "existing-herd:cow").Applied);
        for (var tick = 0; tick < 180 && !world.Society.Inventory.Lots.Any(lot => lot.ItemKind == "milk" && lot.StorageBuildingId == house.InstanceId); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var milk = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "milk");
        Assert.Equal(2, milk.Quantity);
        Assert.Equal(house.HouseholdId, milk.OwnerId);
        Assert.Equal(house.InstanceId, milk.StorageBuildingId);
        Assert.Equal("herd-empty-milk-jug", milk.ContainerLotId);
        Assert.True(world.Society.Inventory.GetLot("herd-house-water").Quantity < 8);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "livestock_cared_for" && item.Detail.EndsWith(":water", StringComparison.Ordinal));
        var mixed = InventoryFixture.AddLot(world.Society.Inventory, "mixed-water", "water", house.HouseholdId!, 1,
            storageBuildingId: house.InstanceId, containerLotId: milk.ContainerLotId);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(WithInventory(world.ExportState(), mixed)));
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "milk-porridge" && item.Tags.Contains("house-cooking"));
        var started = world.StartProduction(recipe.CanonicalId, house.InstanceId, actor);
        Assert.True(started.Applied, started.Failure);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            id => new Pick(id == actor ? "herd-normal" : "safe_idle"));
        for (var tick = 0; tick < 16; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(1, world.Society.Inventory.GetLot(milk.Id).Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot(milk.ContainerLotId!).Quantity);
        Assert.Equal(2, world.Society.Inventory.GetLot(started.JobId + ":output:00").Quantity);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Fact]
    public async Task NaturalHideReachesTailorLeatherAndAUsableCoatWithoutSlaughterOrRegrowth()
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new Pick());
        var (state, actor, house) = AtHouse(seed);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "tailor-building-wood", "wood", house.HouseholdId!, 8,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "tailor-building-fiber", "fiber", house.HouseholdId!, 2, storageBuildingId: house.InstanceId);
        using var builder = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new Pick());
        var definition = builder.WorldContent.Buildings.Single(item => item.Tags.Contains("tailor"));
        var site = builder.Towns.Single().BorderTiles.First(point => state.Map.IsBuildable(point) &&
            !builder.RoadTiles.Contains(point) && !state.Inhabitants.Any(person => person.Position == point) &&
            !state.Map.Resources.Any(resource => resource.Position == point) &&
            !builder.WorldSimulation.Buildings.Any(building => WorldContentSimulationRules.Footprint(
                builder.WorldContent.Buildings.Single(value => value.CanonicalId == building.DefinitionId), building.Position).Contains(point)));
        var placed = builder.PlaceBuilding("hide-tailor", definition.CanonicalId, site, house.HouseholdId);
        Assert.True(placed.Applied, placed.Failure);
        inventory = InventoryFixture.AddLot(builder.Society.Inventory, "leather-fuel", "wood", house.HouseholdId!, 1, storageBuildingId: "hide-tailor");
        using var world = PrivateWorldRuntime.Restore(WithInventory(builder.ExportState(), inventory), id => new Pick(id == actor ? "haul_household_stock" : "safe_idle"));
        Assert.True(world.PlaceAcquiredLivestock("animal:sheep", LivestockKind.Sheep, house.HouseholdId!, house.Position, "existing-herd:sheep").Applied);
        Assert.True(world.RecordNaturalLivestockDeath("animal:sheep").Applied);
        Assert.False(world.RecordNaturalLivestockDeath("animal:sheep").Applied);
        Assert.True(world.CollectLivestockProduct(actor, "animal:sheep").Applied);
        Assert.False(world.CollectLivestockProduct(actor, "animal:sheep").Applied);
        for (var tick = 0; tick < 40 && !world.Society.Inventory.Lots.Any(lot => lot.ItemKind == "hide" && lot.StorageBuildingId == "hide-tailor"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var hide = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "hide");
        Assert.Equal(1, hide.Quantity);
        Assert.Equal(house.HouseholdId, hide.OwnerId);
        Assert.Equal("hide-tailor", hide.StorageBuildingId);
        using var tailoring = PrivateWorldRuntime.Restore(FreshChoice(world.ExportState(), actor), _ => new Pick());
        var tan = tailoring.WorldContent.Recipes.Single(item => item.LocalId == "tan-leather");
        var tanning = tailoring.StartProduction(tan.CanonicalId, "hide-tailor", actor);
        Assert.True(tanning.Applied, tanning.Failure);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(tailoring.ExportState())),
            _ => new Pick());
        for (var tick = 0; tick < 24; tick++)
        {
            Assert.True((await tailoring.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(tailoring.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.DoesNotContain(tailoring.Society.Inventory.Lots, lot => lot.ItemKind == "hide");
        Assert.Equal(2, tailoring.Society.Inventory.GetLot(tanning.JobId + ":output:00").Quantity);
        var sew = tailoring.WorldContent.Recipes.Single(item => item.LocalId == "sew-leather-coat");
        var sewing = tailoring.StartProduction(sew.CanonicalId, "hide-tailor", actor);
        Assert.True(sewing.Applied, sewing.Failure);
        for (var tick = 0; tick < 30; tick++) Assert.True((await tailoring.AdvanceOneTickAsync()).Advanced);
        var coatId = sewing.JobId + ":output:00";
        inventory = InventoryFixture.Transfer(tailoring.Society.Inventory, "collect-coat", house.HouseholdId!, actor, coatId, 1, "collect");
        using var wearer = PrivateWorldRuntime.Restore(WithInventory(tailoring.ExportState(), inventory), _ => new Pick());
        Assert.True(wearer.EquipItem(actor, coatId).Applied);
        for (var tick = 0; tick < 4; tick++) Assert.True((await wearer.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(9_998, wearer.Society.Inventory.GetLot(coatId).ConditionBasisPoints);
        Assert.True(CarryEquipmentRules.Protection(wearer.Society.Inventory.GetLot(coatId),
            ClankerWorld.Simulation.World.WeatherKind.Rain) > 0);
        Assert.True(Assert.Single(wearer.Livestock).HideCollected);
        wearer.Validate();
    }

    [Fact]
    public async Task MissedCarePausesProductionAndRidingWithoutInventingMortality()
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new Pick());
        var (state, actor, house) = AtHouse(seed);
        state = WithInventory(state, Supplies(state.Society.Society.Inventory, actor));
        using var world = PrivateWorldRuntime.Restore(state, _ => new Pick());
        Assert.True(world.PlaceAcquiredLivestock("animal:horse", LivestockKind.Horse, house.HouseholdId!, house.Position, "existing-herd:horse").Applied);
        Assert.True(world.PlaceAcquiredLivestock("animal:sheep", LivestockKind.Sheep, house.HouseholdId!, house.Position, "existing-herd:sheep").Applied);
        foreach (var id in new[] { "animal:horse", "animal:sheep" })
            foreach (var care in new[] { "feed", "water", "care" }) Assert.True(world.CareForLivestock(actor, id, care).Applied);
        Assert.True(world.RideHorse(actor, "animal:horse", true).Applied);
        for (var tick = 0; tick <= LivestockRules.CareTicks; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Null(world.Livestock.Single(animal => animal.Id == "animal:horse").RiderId);
        Assert.False(world.RideHorse(actor, "animal:horse", true).Applied);
        Assert.All(world.Livestock, animal => Assert.Null(animal.NaturalDeathTick));
        Assert.Equal(2, world.Livestock.Single(animal => animal.Id == "animal:sheep").PendingProductQuantity);
        Assert.True(world.CollectLivestockProduct(actor, "animal:sheep").Applied);
        for (var tick = 0; tick < LivestockRules.ProductTicks * 2; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, world.Livestock.Single(animal => animal.Id == "animal:sheep").PendingProductQuantity);
        Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wool").Sum(lot => lot.Quantity));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new Pick());
        restored.Validate();
    }

    [Fact]
    public async Task HorseCarriesWholeVesselsWithTheirOwnersAndRevocationDoesNotConfiscateCargo()
    {
        using var seed = NormalPathWorld.CreateGenerated("probe-a", _ => new Pick());
        var (state, actor, house) = AtHouse(seed);
        var visitor = seed.Society.Inhabitants.First(person => person.HouseholdId != house.HouseholdId).Id;
        var inventory = Supplies(state.Society.Society.Inventory, actor);
        inventory = InventoryFixture.AddLot(inventory, "visitor-stone", "stone", visitor, 4);
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new Pick());
        Assert.True(world.PlaceAcquiredLivestock("animal:horse", LivestockKind.Horse, house.HouseholdId!, house.Position, "existing-herd:horse").Applied);
        foreach (var care in new[] { "feed", "water", "care" }) Assert.True(world.CareForLivestock(actor, "animal:horse", care).Applied);
        Assert.True(world.LoadHorseCargo(actor, "animal:horse", "animal-water-jug", 1).Applied);
        Assert.Equal("animal:horse", world.Society.Inventory.GetLot("animal-water").AnimalId);
        Assert.Equal(actor, world.Society.Inventory.GetLot("animal-water").OwnerId);
        Assert.Equal("animal-water-jug", world.Society.Inventory.GetLot("animal-water").ContainerLotId);
        Assert.True(world.AllowHorseRiding(actor, "animal:horse", visitor, true).Applied);
        state = MoveActor(world.ExportState(), visitor, house.Position) with
        {
            Inhabitants = MoveActor(world.ExportState(), visitor, house.Position).Inhabitants
            .Select(person => person.InhabitantId == visitor ? person with { HungerBasisPoints = 1_000, LastDecisionContext = null } : person).ToArray()
        };
        using var rider = PrivateWorldRuntime.Restore(state, id => new Pick(id == visitor ? "collect_shared_food" : "safe_idle"));
        Assert.True(rider.RideHorse(visitor, "animal:horse", true).Applied);
        Assert.True(rider.LoadHorseCargo(visitor, "animal:horse", "visitor-stone", 4).Applied);
        Assert.False(rider.UnloadHorseCargo(visitor, "animal:horse", "animal-water-jug", 1).Applied);
        var before = rider.Inhabitants.Single(person => person.InhabitantId == visitor).Position;
        for (var tick = 0; tick < 8; tick++)
        {
            Assert.True((await rider.AdvanceOneTickAsync()).Advanced);
            var position = rider.Inhabitants.Single(person => person.InhabitantId == visitor).Position;
            Assert.Equal(position, rider.Livestock[0].Position);
            Assert.All(rider.Society.Inventory.Lots.Where(lot => lot.AnimalId == "animal:horse"),
                lot => Assert.Equal(new InventoryGroundPosition(position.X, position.Y), lot.GroundPosition));
        }
        Assert.NotEqual(before, rider.Inhabitants.Single(person => person.InhabitantId == visitor).Position);
        Assert.True(rider.AllowHorseRiding(actor, "animal:horse", visitor, false).Applied);
        Assert.Null(rider.Livestock[0].RiderId);
        Assert.False(rider.RideHorse(visitor, "animal:horse", true).Applied);
        Assert.True(rider.UnloadHorseCargo(visitor, "animal:horse", "visitor-stone", 4).Applied);
        Assert.Equal(visitor, rider.Society.Inventory.GetLot("visitor-stone").OwnerId);
        Assert.Null(rider.Society.Inventory.GetLot("visitor-stone").AnimalId);
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(rider.ExportState())), _ => new Pick());
        Assert.Equal(actor, reloaded.Society.Inventory.GetLot("animal-water-jug").OwnerId);
        Assert.Equal(7, reloaded.Society.Inventory.GetLot("animal-water").Quantity);
    }

    private static (PrivateWorldRuntimeState State, string Actor, PlacedBuilding House) AtHouse(PrivateWorldRuntime seed)
    {
        var house = seed.WorldSimulation.Buildings.First(building => seed.WorldContent.Buildings.Any(definition =>
            definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house")));
        var actor = seed.Society.Inhabitants.First(person => person.HouseholdId == house.HouseholdId).Id;
        return (MoveActor(seed.ExportState(), actor, house.Position), actor, house);
    }

    private static PrivateWorldRuntimeState MoveActor(PrivateWorldRuntimeState state, string actor, GridPoint position) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = position, HungerBasisPoints = 9_000 }
            : person.Position == position ? person with { Position = state.Inhabitants.Single(item => item.InhabitantId == actor).Position } : person).ToArray(),
    };

    private static InventoryCheckpoint Supplies(InventoryCheckpoint inventory, string actor)
    {
        inventory = InventoryFixture.AddLot(inventory, "animal-feed", "grain", actor, 8);
        inventory = InventoryFixture.AddLot(inventory, "animal-water-jug", "water_jug", actor, 1, containerCapacity: 8);
        inventory = InventoryFixture.AddLot(inventory, "animal-water", "water", actor, 8, containerLotId: "animal-water-jug");
        inventory = InventoryFixture.AddLot(inventory, "animal-shearing-knife", "knife", actor, 1);
        return InventoryFixture.AddLot(inventory, "animal-empty-jug", "water_jug", actor, 1, containerCapacity: 8);
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    // A phase changes the fixture's selector; its previous action must not
    // continue before that selector receives its first observation.
    private static PrivateWorldRuntimeState FreshChoice(PrivateWorldRuntimeState state, string actor) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { LastDecisionContext = null } : person).ToArray(),
        Society = state.Society with
        {
            Cognition = state.Society.Cognition with
            {
                Runtimes = state.Society.Cognition.Runtimes.Select(runtime => runtime.InhabitantId == actor ? runtime with { CurrentIntention = null } : runtime).ToArray(),
            }
        },
    };

    private sealed class Pick(string prefix = "safe_idle") : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidate = request.Observation.Candidates.Where(item => item.Id.StartsWith(prefix, StringComparison.Ordinal) || prefix == "herd-normal" &&
                    (item.Id.StartsWith("animal_", StringComparison.Ordinal) || item.Id == "haul_household_stock"))
                .OrderBy(item => item.DeterministicPriority).ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault()
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [candidate] } }, cancellationToken);
        }
    }
}
