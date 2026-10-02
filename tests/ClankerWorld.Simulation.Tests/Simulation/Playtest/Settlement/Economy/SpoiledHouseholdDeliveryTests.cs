using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SpoiledHouseholdDeliveryTests
{
    private const string Household = "household:camp-alpha";
    private const string House = "first-town-house-a";
    private const string FirstGreens = "00-inflight-greens";
    private const string SecondGreens = "01-inflight-greens";
    private const string UnrelatedReservation = "retained-camp-wood";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealFourUnitPickupsSpoilInTransitAndWalkBackToCampBeforeTheEmptyHouseCanChange(bool reassign)
    {
        var (state, actor, camp) = await PreparedDelivery();
        var choices = DeliveryPolicy();
        using var pickupWorld = Restore(state, actor, choices);
        var house = pickupWorld.WorldSimulation.Buildings.Single(building => building.InstanceId == House);
        var initialInventory = pickupWorld.Society.Inventory;
        var quantities = initialInventory.Lots.ToDictionary(lot => lot.Id, lot => lot.Quantity, StringComparer.Ordinal);
        var reserved = initialInventory.GetReservation(UnrelatedReservation);

        for (var tick = 0; tick < 12 && !BothSpoiledAndCarried(pickupWorld, actor); tick++)
        {
            Assert.True((await pickupWorld.AdvanceOneTickAsync()).Advanced);
            Assert.InRange(PersonalEquipmentRules.CarriedQuantity(pickupWorld.Society.Inventory, actor, null), 0, 8);
        }

        Assert.True(BothSpoiledAndCarried(pickupWorld, actor),
            "The two real four-unit pickups did not spoil in transit. " +
            string.Join("; ", new[] { FirstGreens, SecondGreens }.Select(id =>
            {
                var lot = pickupWorld.Society.Inventory.GetLot(id);
                return $"{id}: owner={lot.OwnerId}, quantity={lot.Quantity}, freshness={lot.FreshnessBasisPoints}, delivery={lot.DeliveryBuildingId}";
            })) + $"; actor={pickupWorld.Inhabitants.Single(person => person.InhabitantId == actor).Position}" +
            "; offers=" + string.Join(" | ", choices.Offers.TakeLast(3).Select(offer => string.Join(",", offer))));
        foreach (var id in new[] { FirstGreens, SecondGreens })
        {
            var carried = pickupWorld.Society.Inventory.GetLot(id);
            Assert.Equal((actor, 4, House, (string?)null, (string?)null),
                (carried.OwnerId, carried.Quantity, carried.DeliveryBuildingId, carried.StorageBuildingId, carried.ContainerLotId));
            Assert.Null(carried.GroundPosition);
            Assert.Contains(pickupWorld.ExportState().Events, item => item.Kind == "household_stock_picked_up" &&
                item.Detail == $"{actor}:{id}:4:{House}");
            Assert.Contains(pickupWorld.Society.Inventory.Events, item => item.Kind == "lot_spoiled" && item.Detail == id);
        }
        Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(pickupWorld.Society.Inventory, actor, null));
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(pickupWorld.Society.Inventory, actor, null));
        Assert.DoesNotContain(pickupWorld.Society.Inventory.Lots, lot => lot.StorageBuildingId == House);
        AssertBuildingStillBlocked(pickupWorld, house, reassign);

        // Resume the actual in-flight state with the same admitted policy.
        // No intention, stock, position, reservation or delivery pointer is reset.
        var bytes = PrivateWorldRuntimeCodec.Encode(pickupWorld.ExportState());
        var recoveryChoices = DeliveryPolicy();
        using var recovered = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor, recoveryChoices);
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor,
            DeliveryPolicy());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(recovered.ExportState()));
        var previous = recovered.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var walked = false;
        for (var tick = 0; tick < 96 && !BothAtCamp(recovered, camp); tick++)
        {
            Assert.True((await recovered.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(recovered.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            var position = recovered.Inhabitants.Single(person => person.InhabitantId == actor).Position;
            if (position != previous)
            {
                Assert.True(state.Map.CanFootStep(previous, position));
                walked = true;
            }
            previous = position;
            Assert.InRange(PersonalEquipmentRules.CarriedQuantity(recovered.Society.Inventory, actor, null), 0, 8);
            if (recovered.Society.Inventory.Lots.Any(lot => lot.DeliveryBuildingId == House))
                AssertBuildingStillBlocked(recovered, house, reassign);
        }

        Assert.Contains(recoveryChoices.Offers, offer => offer.Contains("recover_household_delivery", StringComparer.Ordinal));
        Assert.True(walked, "The carrier must actually walk to camp before setting the spoiled stock down.");
        Assert.True(BothAtCamp(recovered, camp), "Spoiled deliveries stayed aboard instead of becoming real household camp stock.");
        var finalInventory = recovered.Society.Inventory;
        Assert.Equal(quantities.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            finalInventory.Lots.ToDictionary(lot => lot.Id, lot => lot.Quantity, StringComparer.Ordinal)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(reserved, finalInventory.GetReservation(UnrelatedReservation));
        Assert.Equal(initialInventory.Reservations, finalInventory.Reservations);
        Assert.Equal(0, PersonalEquipmentRules.CarriedQuantity(finalInventory, actor, null));
        Assert.Equal(8, PersonalEquipmentRules.FreeCapacity(finalInventory, actor, null));
        foreach (var id in new[] { FirstGreens, SecondGreens })
        {
            Assert.Equal(0, finalInventory.GetLot(id).FreshnessBasisPoints);
            Assert.Equal(10_000, finalInventory.GetLot(id).ConditionBasisPoints);
            Assert.Null(finalInventory.GetLot(id).ProvenanceLotId);
            Assert.Contains(recovered.ExportState().Events, item => item.Kind == "household_delivery_recovered" &&
                item.Detail.Contains(id, StringComparison.Ordinal));
        }
        Assert.DoesNotContain(finalInventory.Lots, lot => lot.StorageBuildingId == House || lot.DeliveryBuildingId == House);
        Assert.DoesNotContain(recovered.WorldSimulation.ProductionJobs,
            job => job.BuildingInstanceId == House && job.State == WorldProductionJobState.Running);
        var changed = reassign
            ? recovered.ReassignBuilding(House, house.TownId, Household, null, "household:camp-beta")
            : recovered.RemoveBuilding(House, house.TownId, Household);
        Assert.True(changed.Applied, changed.Failure);
        Assert.Equal(finalInventory, recovered.Society.Inventory);
        var finalBytes = PrivateWorldRuntimeCodec.Encode(recovered.ExportState());
        using var finalReload = Restore(PrivateWorldRuntimeCodec.Decode(finalBytes), actor, new DeliveryChoices());
        Assert.Equal(finalBytes, PrivateWorldRuntimeCodec.Encode(finalReload.ExportState()));
        Assert.True(BothAtCamp(finalReload, camp));
    }

    private static async Task<(PrivateWorldRuntimeState State, string Actor, GridPoint Camp)> PreparedDelivery(bool wholeFamily = false)
    {
        using var generated = NormalPathWorld.CreateGenerated("audit-town-invariants", _ => new DeliveryChoices());
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Household &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var house = generated.WorldSimulation.Buildings.Single(building => building.InstanceId == House);
        var otherHouse = generated.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-b");
        var camp = state.Map.CampObjects.FirstOrDefault(item => item.Id == "storage")?.Position ??
            generated.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-warehouse").Position;
        var farmhouse = generated.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-farmhouse");
        var footprints = generated.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            generated.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
        var source = state.Map.Tiles.Select(tile => tile.Position).Where(point => state.Map.IsBuildable(point) &&
                !footprints.Contains(point) && !state.Map.Resources.Any(resource => resource.Position == point) &&
                !state.Map.CampObjects.Any(item => item.Position == point) &&
                !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == point) &&
                state.Map.FootDistance(point, house.Position) >= 4 && state.Map.FootDistance(point, camp) >= 3 &&
                state.Map.IsReachableOnFoot(point, house.Position) && state.Map.IsReachableOnFoot(point, camp))
            .OrderBy(point => state.Map.FootDistance(point, farmhouse.Position)).ThenBy(point => point.Y).ThenBy(point => point.X).First();
        var inventory = state.Society.Society.Inventory;
        Assert.Equal(0, PersonalEquipmentRules.CarriedQuantity(inventory, actor, null));
        // Initial fixture relocation retains every real lot, owner and quantity.
        // The two Houses are empty before this test's delivery sequence starts.
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.StorageBuildingId == House || lot.StorageBuildingId == otherHouse.InstanceId
                ? lot with { StorageBuildingId = null, GroundPosition = lot.ContainerLotId is null ? new(camp.X, camp.Y) : null }
                : lot).ToArray(),
        };
        var wood = inventory.Lots.First(lot => lot.OwnerId == Household && lot.ItemKind == "wood" && lot.Quantity > 0);
        inventory = InventoryFixture.Reserve(inventory, UnrelatedReservation, Household, wood.Id, 1,
            "retained_recovery_control", long.MaxValue);
        // Hold unrelated loose camp supplies with real claims before the
        // action phase. They remain physical stock; freeing a recovery load
        // must not start a new unrelated delivery into the empty target House.
        foreach (var stock in inventory.Lots.Where(lot => lot.OwnerId == Household &&
            lot.StorageBuildingId is null && lot.DeliveryBuildingId is null && lot.ContainerLotId is null &&
            !InventoryContainerRules.IsContainer(lot.ItemKind) && lot.Quantity > 0 &&
            lot.ConditionBasisPoints > 0 && lot.FreshnessBasisPoints > 0).ToArray())
        {
            var alreadyReserved = inventory.Reservations.Where(reservation => reservation.LotId == stock.Id &&
                reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or
                    InventoryReservationState.Committed).Sum(reservation => reservation.Quantity);
            if (alreadyReserved < stock.Quantity)
                inventory = InventoryFixture.Reserve(inventory, $"retained-stock:{stock.Id}", Household, stock.Id,
                    stock.Quantity - alreadyReserved, "retained_recovery_control", long.MaxValue);
        }
        if (wholeFamily)
        {
            inventory = InventoryFixture.AddLot(inventory, Pot, InventoryContainerRules.StoragePot, Household, 1,
                groundPosition: new(source.X, source.Y));
            inventory = InventoryFixture.AddLot(inventory, PotGreens, "cultivated_greens", Household, 3,
                freshnessBasisPoints: 4, groundPosition: new(source.X, source.Y));
            inventory = InventoryFixture.PutIntoContainer(inventory, "fill-inflight-pot", Household, Pot, PotGreens, 3);
        }
        else
            inventory = InventoryFixture.AddLot(inventory, FirstGreens, "cultivated_greens", Household, 4,
                freshnessBasisPoints: 4, groundPosition: new(source.X, source.Y));
        inventory = InventoryFixture.AddLot(inventory, SecondGreens, "cultivated_greens", Household, 4,
            freshnessBasisPoints: 12, groundPosition: new(source.X, source.Y));
        state = SettlementWeatherTestFixture.WithWeather(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? source : person.Position,
                HungerBasisPoints = 9_000,
                Survival = new SurvivalCondition(10_000),
                LastDecisionContext = null,
            }).ToArray(),
        }, WeatherKind.Clear);
        using var prepared = Restore(state, actor, new DeliveryChoices());
        // The actual, already empty second House is removed through its normal
        // owner action so Beta is a legitimate reassignment destination later.
        var removal = prepared.RemoveBuilding(otherHouse.InstanceId, otherHouse.TownId, otherHouse.HouseholdId);
        Assert.True(removal.Applied, removal.Failure);
        return (prepared.ExportState(), actor, camp);
    }

    private static bool BothSpoiledAndCarried(PrivateWorldRuntime world, string actor) =>
        new[] { FirstGreens, SecondGreens }.All(id => world.Society.Inventory.GetLot(id) is { } lot &&
            lot.OwnerId == actor && lot.Quantity == 4 && lot.FreshnessBasisPoints == 0);

    private static bool BothAtCamp(PrivateWorldRuntime world, GridPoint camp) =>
        new[] { FirstGreens, SecondGreens }.All(id => world.Society.Inventory.GetLot(id) is { } lot &&
            lot.OwnerId == Household && lot.Quantity == 4 && lot.DeliveryBuildingId is null &&
            lot.StorageBuildingId is null && lot.ContainerLotId is null && lot.GroundPosition == new InventoryGroundPosition(camp.X, camp.Y));

    private static void AssertBuildingStillBlocked(PrivateWorldRuntime world, PlacedBuilding house, bool reassign)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var changed = reassign
            ? world.ReassignBuilding(House, house.TownId, Household, null, "household:camp-beta")
            : world.RemoveBuilding(House, house.TownId, Household);
        Assert.False(changed.Applied);
        Assert.Contains("Empty this building", changed.Failure, StringComparison.Ordinal);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, DeliveryChoices choices) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? choices : new DeliveryChoices());

    private static DeliveryChoices DeliveryPolicy() => new("haul_household_stock", "recover_household_delivery");

    private sealed class DeliveryChoices(params string[] allowed) : IDecisionProvider
    {
        internal List<string[]> Offers { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            Offers.Add(request.Observation.Candidates.Select(candidate => candidate.Id).ToArray());
            // Complete an actual usable delivery first. The same policy admits
            // recovery as soon as no legal haul remains, including after reload.
            var selected = allowed.SelectMany(id => request.Observation.Candidates.Where(candidate => candidate.Id == id))
                .FirstOrDefault() ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            var probabilities = request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1d, probabilities));
        }
    }
}
