using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ContainerReturnTests
{
    private const string Alpha = "household:camp-alpha";
    private const string House = "first-town-house-a";
    private const string Clinic = "container-return-clinic";
    private const string Vessel = "container-return-vessel";
    private const string UsedContents = "container-return-used-contents";
    private const string UsedReservation = "container-return-used-input";

    [Theory]
    [InlineData(InventoryContainerRules.StoragePot)]
    [InlineData(InventoryContainerRules.WaterJug)]
    public async Task EmptyHouseholdVesselIsWalkedFromItsWorkplaceAndDeliveredHomeAcrossReload(string kind)
    {
        var state = Prepared(kind);
        var actor = Actor(state);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        var workplace = state.WorldSimulation.Buildings.Single(building => building.InstanceId == Clinic);
        var original = state.Society.Society.Inventory.GetLot(Vessel);
        var provider = new VesselChoices("return_empty_vessel:", "haul_household_stock");
        using var world = Restore(state, actor, provider);
        Assert.Equal(house.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.NotEqual(house.Position, workplace.Position);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == UsedContents);
        Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(UsedReservation).State);
        await AdvanceUntil(world, () => world.Society.Inventory.GetLot(Vessel).OwnerId == actor, 120);

        Assert.Contains("return_empty_vessel:" + Vessel, provider.Offered);
        Assert.Equal(workplace.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        Assert.Single(world.ExportState().Events, item => item.Kind == "empty_vessel_picked_up" &&
            item.Detail == $"{actor}:{Vessel}:{Clinic}:{House}");
        var carried = world.Society.Inventory.GetLot(Vessel);
        Assert.Equal(original with
        {
            OwnerId = actor,
            StorageBuildingId = null,
            DeliveryBuildingId = House,
            LastProcessedTick = carried.LastProcessedTick
        }, carried);
        Assert.Equal(1, world.Society.Inventory.Lots.Where(lot => lot.Id == Vessel || lot.ContainerLotId == Vessel)
            .Sum(lot => lot.Quantity));

        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            actor, new VesselChoices("return_empty_vessel:", "haul_household_stock"));
        for (var tick = 0; tick < 120 && world.Society.Inventory.GetLot(Vessel).StorageBuildingId != House; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var returned = world.Society.Inventory.GetLot(Vessel);
        Assert.Equal((Alpha, House, null, 1), (returned.OwnerId, returned.StorageBuildingId, returned.DeliveryBuildingId, returned.Quantity));
        Assert.Equal(house.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Equal(original.Id, returned.Id);
        Assert.Equal(original.ProvenanceLotId, returned.ProvenanceLotId);
        Assert.Equal(original.ConditionBasisPoints, returned.ConditionBasisPoints);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ContainerLotId == Vessel);
        Assert.Single(world.ExportState().Events, item => item.Kind == "household_stock_delivered" &&
            item.Detail == $"{actor}:{Vessel}:1:{House}");
        Assert.Equal(InventoryReservationState.Completed, world.Society.Inventory.GetReservation(UsedReservation).State);
    }

    [Theory]
    [InlineData("foreign-adult")]
    [InlineData("remaining-contents")]
    [InlineData("full-hands")]
    [InlineData("full-house")]
    [InlineData("promised-house-space")]
    [InlineData("reserved-contents")]
    [InlineData("broken-vessel")]
    public async Task UnavailableVesselOrPhysicalRoomDoesNotOfferOrMoveAnEmptyReturn(string boundary)
    {
        var state = Prepared(InventoryContainerRules.WaterJug);
        var actor = Actor(state);
        var inventory = state.Society.Society.Inventory;
        if (boundary == "foreign-adult")
            actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId != Alpha).Id;
        if (boundary == "full-hands")
        {
            var person = state.Inhabitants.Single(item => item.InhabitantId == actor);
            var free = PersonalEquipmentRules.FreeCapacity(inventory, actor, person.Equipment);
            Assert.True(free > 0);
            inventory = InventoryFixture.AddLot(inventory, "container-return-ballast", "test_cargo", actor, free);
            Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(inventory, actor, person.Equipment));
        }
        if (boundary is "full-house" or "promised-house-space")
        {
            var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
            var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
            var free = BuildingStorageRules.Capacity(definition, house)!.Value -
                inventory.Lots.Where(lot => lot.StorageBuildingId == House).Sum(lot => lot.Quantity);
            var incoming = boundary == "promised-house-space" ? 1 : 0;
            Assert.True(free > incoming);
            inventory = InventoryFixture.AddLot(inventory, "container-return-full-house", "test_storage", Alpha,
                free - incoming, storageBuildingId: House);
            if (incoming != 0)
            {
                var deliverer = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha && person.Id != actor).Id;
                inventory = InventoryFixture.AddLot(inventory, "container-return-incoming", "wood", deliverer, 1,
                    deliveryBuildingId: House);
            }
        }
        if (boundary is "remaining-contents" or "reserved-contents")
        {
            inventory = InventoryFixture.AddLot(inventory, "container-return-live-water", InventoryContainerRules.FreshWater,
                Alpha, 1, storageBuildingId: Clinic, containerLotId: Vessel);
            if (boundary == "reserved-contents")
                inventory = InventoryFixture.Reserve(inventory, "container-return-live-water-work", Alpha,
                    "container-return-live-water", 1, "other_work", state.Society.Society.WorldTick + 1_000);
        }
        if (boundary == "broken-vessel")
            inventory = InventoryFixture.WearSingleUnit(inventory, Vessel, 10_000);
        state = WithInventory(state, inventory);
        var unchanged = state.Society.Society.Inventory.GetLot(Vessel);
        var provider = new VesselChoices("return_empty_vessel:", "haul_household_stock");
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), actor, provider);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(provider.Offered);
        Assert.DoesNotContain(provider.Offered, candidate => candidate.StartsWith("return_empty_vessel:", StringComparison.Ordinal));
        var actual = world.Society.Inventory.GetLot(Vessel);
        Assert.Equal(unchanged with { LastProcessedTick = actual.LastProcessedTick }, actual);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "empty_vessel_picked_up");
        if (boundary is "remaining-contents" or "reserved-contents")
        {
            Assert.Equal(1, world.Society.Inventory.GetLot("container-return-live-water").Quantity);
            if (boundary == "reserved-contents")
                Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("container-return-live-water-work").State);
        }
        Assert.NotEmpty(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task ReturnedJugIsFilledAndSuppliedForAnotherRealMedicineJobWithTheSameVessel()
    {
        var state = Prepared(InventoryContainerRules.WaterJug);
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "container-reuse-herbs", CareContent.MedicinalHerbs,
            Alpha, 6, storageBuildingId: Clinic);
        inventory = InventoryFixture.AddLot(inventory, "container-reuse-wood", "wood", Alpha, 3, storageBuildingId: Clinic);
        inventory = InventoryFixture.AddLot(inventory, "container-reuse-first-water", InventoryContainerRules.FreshWater,
            Alpha, 1, storageBuildingId: Clinic, containerLotId: Vessel);
        state = WithInventory(state, inventory);
        var workplace = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == Clinic);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = workplace.Position } : person).ToArray(),
        };
        using var first = Restore(state, actor, new VesselChoices());
        var recipe = first.WorldContent.Recipes.Single(item => item.LocalId == "clinic-medicine");
        var firstStart = first.StartProduction(recipe.CanonicalId, Clinic, actor);
        Assert.True(firstStart.Applied, firstStart.Failure);
        var firstJob = first.WorldSimulation.ProductionJobs.Single(item => item.JobId == firstStart.JobId);
        var firstWater = Assert.Single(firstJob.InputReservationIds.Select(first.Society.Inventory.GetReservation),
            input => input.LotId == "container-reuse-first-water");
        Assert.Equal((Alpha, 1), (firstWater.OwnerId, firstWater.Quantity));
        for (var tick = 0; tick < 16; tick++) Assert.True((await first.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(InventoryReservationState.Completed, first.Society.Inventory.GetReservation(firstWater.Id).State);
        Assert.Equal(2, first.Society.Inventory.GetLot(firstStart.JobId + ":output:00").Quantity);
        Assert.DoesNotContain(first.Society.Inventory.Lots, lot => lot.ContainerLotId == Vessel);
        var returnProvider = new VesselChoices("return_empty_vessel:", "haul_household_stock");
        using var returning = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(first.ExportState())), actor, returnProvider);
        await AdvanceUntil(returning, () => returning.Society.Inventory.GetLot(Vessel).StorageBuildingId == House, 160);
        Assert.Equal(Alpha, returning.Society.Inventory.GetLot(Vessel).OwnerId);

        var supply = new VesselChoices("supply_workstation:fresh_water", "fill_water_jug:", "collect_water_jug",
            "return_water_jug", "haul_household_stock");
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(returning.ExportState())), actor, supply);
        await AdvanceUntil(world, () => world.Society.Inventory.GetLot(Vessel).StorageBuildingId == Clinic &&
            world.Society.Inventory.Lots.Any(lot => lot.ContainerLotId == Vessel), 240);
        var water = Assert.Single(world.Society.Inventory.Lots, lot => lot.ContainerLotId == Vessel);
        Assert.Equal((InventoryContainerRules.FreshWater, Alpha, Clinic, 4),
            (water.ItemKind, water.OwnerId, water.StorageBuildingId, water.Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "water_jug_filled" &&
            item.Detail.StartsWith(actor + ":" + Vessel + ":4:", StringComparison.Ordinal));
        Assert.Equal(Vessel, world.Society.Inventory.GetLot(Vessel).Id);
        var result = world.StartProduction(recipe.CanonicalId, Clinic, actor);
        Assert.True(result.Applied, result.Failure);
        var job = world.WorldSimulation.ProductionJobs.Single(item => item.JobId == result.JobId);
        var doseWater = Assert.Single(job.InputReservationIds.Select(world.Society.Inventory.GetReservation), item => item.LotId == water.Id);
        Assert.Equal((Alpha, 1, InventoryReservationState.Reserved), (doseWater.OwnerId, doseWater.Quantity, doseWater.State));
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), actor,
            new VesselChoices());
        // The directed reuse phase is complete; both branches now use the same idle provider.
        using var completing = Restore(world.ExportState(), actor, new VesselChoices());
        for (var tick = 0; tick < 16; tick++)
        {
            Assert.True((await completing.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(completing.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(InventoryReservationState.Completed, completing.Society.Inventory.GetReservation(doseWater.Id).State);
        Assert.Equal(InventoryReservationState.Completed, completing.Society.Inventory.GetReservation(firstWater.Id).State);
        Assert.Equal(3, completing.Society.Inventory.GetLot(water.Id).Quantity);
        Assert.Equal((Alpha, Clinic, 1), (completing.Society.Inventory.GetLot(Vessel).OwnerId,
            completing.Society.Inventory.GetLot(Vessel).StorageBuildingId, completing.Society.Inventory.GetLot(Vessel).Quantity));
        Assert.Equal((CareContent.Medicine, Alpha, Clinic, 2),
            (completing.Society.Inventory.GetLot(result.JobId + ":output:00").ItemKind,
                completing.Society.Inventory.GetLot(result.JobId + ":output:00").OwnerId,
                completing.Society.Inventory.GetLot(result.JobId + ":output:00").StorageBuildingId,
                completing.Society.Inventory.GetLot(result.JobId + ":output:00").Quantity));
    }

    private static PrivateWorldRuntimeState Prepared(string kind)
    {
        using var generated = NormalPathWorld.CreateGenerated("empty-container-return", _ => new VesselChoices());
        var state = generated.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "container-return-clinic-stone", "stone",
            Alpha, 4, storageBuildingId: House);
        using var funded = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => new VesselChoices());
        var house = funded.WorldSimulation.Buildings.Single(building => building.InstanceId == House);
        Assert.Contains(Enumerable.Range(-8, 17).SelectMany(y => Enumerable.Range(-8, 17).Select(x =>
            new GridPoint(house.Position.X + x, house.Position.Y + y))), point =>
            point != house.Position && funded.PlaceBuilding(Clinic, CareContent.Clinic1x2().CanonicalId, point, Alpha).Applied);
        state = funded.ExportState();
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Vessel, kind, Alpha, 1, storageBuildingId: Clinic);
        inventory = InventoryFixture.AddLot(inventory, UsedContents,
            kind == InventoryContainerRules.WaterJug ? InventoryContainerRules.FreshWater : "grain", Alpha, 1,
            storageBuildingId: Clinic, containerLotId: Vessel);
        inventory = InventoryFixture.Reserve(inventory, UsedReservation, Alpha, UsedContents, 1,
            "consumed_workstation_input", state.Society.Society.WorldTick);
        inventory = InventoryFixture.ConsumeReservation(inventory, UsedReservation);
        var actor = Actor(state);
        var previous = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position, HungerBasisPoints = 9_000 }
                : person.Position == house.Position ? person with { Position = previous } : person).ToArray(),
        };
        return WithInventory(state, inventory);
    }

    private static string Actor(PrivateWorldRuntimeState state) => state.Society.Society.Inhabitants.First(person =>
        person.HouseholdId == Alpha && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, IDecisionProvider provider) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new VesselChoices());

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> complete, int budget)
    {
        for (var tick = 0; tick < budget && !complete(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(complete(), $"The real vessel phase did not finish within {budget} ticks at {world.WorldTick}.");
    }

    private sealed class VesselChoices(params string[] prefixes) : IDecisionProvider
    {
        public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates) Offered.Add(candidate.Id);
            var choice = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith(prefix, StringComparison.Ordinal))).FirstOrDefault(candidate => candidate is not null)
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
