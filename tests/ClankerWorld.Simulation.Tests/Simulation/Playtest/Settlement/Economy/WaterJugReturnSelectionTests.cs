using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class WaterJugReturnSelectionTests
{
    private const string House = "first-town-house-a";
    private const string Claim = "held-other-jug";

    [Theory]
    [InlineData(true, "contents", false, false, -1)]
    [InlineData(true, "root", false, false, -1)]
    [InlineData(false, "contents", false, false, -1)]
    [InlineData(true, "none", false, false, -1)]
    [InlineData(true, "contents", true, false, -1)]
    [InlineData(true, "contents", false, true, -1)]
    [InlineData(true, "contents", false, false, 2)]
    public async Task AnOfferedReturnMovesItsEligibleJugAndPreservesTheOtherFamilyAcrossReload(
        bool otherFirst, string heldPart, bool returnedFull, bool walk, int houseRoom)
    {
        var (state, actor, other, returned) = Prepared(otherFirst, heldPart, returnedFull, walk, houseRoom);
        var policy = new ReturnChoices();
        var saved = PrivateWorldRuntimeCodec.Encode(state);
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(saved), actor, policy);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var original = state.Society.Society.Inventory;
        var quantity = original.Lots.Sum(lot => lot.Quantity);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains("return_water_jug", policy.Offered);
        if (!walk) Assert.Equal(House, world.Society.Inventory.GetLot(returned).StorageBuildingId);
        if (walk)
        {
            Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
            Assert.Equal(actor, world.Society.Inventory.GetLot(returned).OwnerId);
        }
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), actor, new ReturnChoices());
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        for (var tick = 0; tick < (heldPart == "none" ? 0 : 3); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var home = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).HouseholdId;
        foreach (var id in new[] { returned, returned + "-water" })
        {
            var actual = world.Society.Inventory.GetLot(id);
            Assert.Equal(original.GetLot(id) with { OwnerId = home!, StorageBuildingId = House, LastProcessedTick = actual.LastProcessedTick }, actual);
        }
        foreach (var id in new[] { other, other + "-water" })
        {
            var actual = world.Society.Inventory.GetLot(id);
            Assert.Equal(original.GetLot(id) with { LastProcessedTick = actual.LastProcessedTick }, actual);
        }
        if (heldPart != "none") Assert.Equal(original.GetReservation(Claim), world.Society.Inventory.GetReservation(Claim));
        Assert.Equal(quantity, world.Society.Inventory.Lots.Sum(lot => lot.Quantity));
        Assert.Single(world.ExportState().Events, item => item.Kind == "water_jug_returned" && item.Detail == $"{actor}:{returned}:{House}");
        world.Validate();
    }

    [Theory]
    [InlineData("all-reserved")]
    [InlineData("full-house")]
    public async Task AReturnWithoutAnUnreservedFamilyOrHouseRoomMovesNothing(string boundary)
    {
        var (state, actor, _, returned) = Prepared(true, "contents", false, false, boundary == "full-house" ? 0 : -1);
        var inventory = state.Society.Society.Inventory;
        if (boundary == "all-reserved") inventory = InventoryFixture.Reserve(inventory, "held-return-jug", actor,
            returned, 1, "other_work", state.Society.Society.WorldTick + 1_000);
        state = WithInventory(state, inventory);
        var policy = new ReturnChoices();
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), actor, policy);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(policy.Offered);
        Assert.DoesNotContain("return_water_jug", policy.Offered);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "water_jug_returned");
        foreach (var lot in inventory.Lots.Where(lot => lot.OwnerId == actor))
        {
            var actual = world.Society.Inventory.GetLot(lot.Id);
            Assert.Equal(lot with { LastProcessedTick = actual.LastProcessedTick }, actual);
        }
        Assert.Equal(inventory.GetReservation(Claim), world.Society.Inventory.GetReservation(Claim));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor, new ReturnChoices());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        world.Validate();
    }

    private static (PrivateWorldRuntimeState State, string Actor, string Other, string Returned) Prepared(
        bool otherFirst, string heldPart, bool returnedFull, bool walk, int houseRoom)
    {
        using var setup = NormalPathWorld.CreateGenerated("jug-return-selection-audit", _ => new ReturnChoices(false));
        var state = ShelterOrderTestFixture.WithClearWeather(setup.ExportState());
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == house.HouseholdId &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var origin = walk ? state.Map.FootNeighbors(house.Position).First(point =>
            (point.X == house.Position.X || point.Y == house.Position.Y) && state.Map.IsPassable(point) &&
            state.Inhabitants.All(person => person.Position != point)) : house.Position;
        var previous = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        state = state with { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { Position = origin, HungerBasisPoints = 10_000, Equipment = null, Survival = person.Survival is { } survival
                ? survival with { WarmthBasisPoints = 10_000, NutritionBasisPoints = 10_000 } : null }
            : person.Position == origin ? person with { Position = previous } : person).ToArray() };
        var other = otherFirst ? "jug-a" : "jug-b";
        var returned = otherFirst ? "jug-b" : "jug-a";
        var inventory = state.Society.Society.Inventory;
        foreach (var (id, water) in new[] { (other, returnedFull ? 1 : 4), (returned, returnedFull ? 4 : 1) })
        {
            inventory = InventoryFixture.AddLot(inventory, id, InventoryContainerRules.WaterJug, actor, 1);
            inventory = InventoryFixture.AddLot(inventory, id + "-water", InventoryContainerRules.FreshWater, actor, water, containerLotId: id);
        }
        var free = PersonalEquipmentRules.FreeCapacity(inventory, actor, null);
        if (free > 0) inventory = InventoryFixture.AddLot(inventory, "carried-stone", "stone", actor, free);
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(inventory, actor, null));
        if (heldPart != "none") inventory = InventoryFixture.Reserve(inventory, Claim, actor,
            heldPart == "root" ? other : other + "-water", 1, "other_work", state.Society.Society.WorldTick + 1_000);
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        var room = BuildingStorageRules.Capacity(definition, house)!.Value - inventory.Lots.Where(lot => lot.StorageBuildingId == House).Sum(lot => lot.Quantity);
        Assert.True(room >= 5);
        if (houseRoom >= 0) inventory = InventoryFixture.AddLot(inventory, "house-stone", "stone", house.HouseholdId!, room - houseRoom, storageBuildingId: House);
        return (WithInventory(state, inventory), actor, other, returned);
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, ReturnChoices policy) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? policy : new ReturnChoices(false));

    private sealed class ReturnChoices(bool chooseReturn = true) : IDecisionProvider
    {
        public ConcurrentBag<string> Offered { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates) Offered.Add(candidate.Id);
            var choice = chooseReturn ? request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "return_water_jug") : null;
            choice ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
