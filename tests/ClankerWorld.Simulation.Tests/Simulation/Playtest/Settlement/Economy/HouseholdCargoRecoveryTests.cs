using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class HouseholdCargoRecoveryTests
{
    private const string Alpha = "household:camp-alpha";
    private const string JugId = "z-recovery-jug";
    private const string WaterId = "z-recovery-water";

    [Theory]
    [InlineData(4, true)]
    [InlineData(7, true)]
    [InlineData(4, false)]
    public async Task AFullCarrierSetsDownTheWholeLoadedJugAndKeepsEveryEquippedSlotAcrossReload(
        int houseRoom, bool hasSharedServing)
    {
        using var generated = NormalPathWorld.CreateGenerated("identity-pause", _ => new RecoveryChoices(false));
        var state = generated.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        var capacity = Assert.IsType<int>(BuildingStorageRules.Capacity(definition, house));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (lot.OwnerId != Alpha || !FoodItems.IsEdible(lot.ItemKind))).ToArray(),
        };
        var slots = new PersonalEquipment("a-kept-clothes", "a-kept-basket", WeaponLotId: "a-kept-sword",
            ShieldLotId: "a-kept-shield", ArmorLotId: "a-kept-armor", OrnamentLotId: "a-kept-ornament");
        (string Id, string Kind)[] gear = [(slots.ClothingLotId!, "clothing"), (slots.CarryAidLotId!, "basket"),
            (slots.WeaponLotId!, "sword"), (slots.ShieldLotId!, "shield"),
            (slots.ArmorLotId!, "basic_armor"), (slots.OrnamentLotId!, "gold_ornament")];
        foreach (var (id, kind) in gear)
            inventory = InventoryFixture.AddLot(inventory, id, kind, actor, 1,
                conditionBasisPoints: kind == "basket" ? 0 : 10_000);
        inventory = InventoryFixture.AddLot(inventory, JugId, "jug", actor, 1, containerCapacity: 8);
        inventory = InventoryFixture.AddLot(inventory, WaterId, "water", actor, 6, containerLotId: JugId);
        if (hasSharedServing)
            inventory = InventoryFixture.AddLot(inventory, "recovery-house-food", "berries", Alpha, 1,
                storageBuildingId: house.InstanceId);
        var filler = capacity - houseRoom - inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId)
            .Sum(lot => lot.Quantity);
        Assert.True(filler > 0);
        inventory = InventoryFixture.AddLot(inventory, "recovery-house-fill", "wood", Alpha, filler,
            storageBuildingId: house.InstanceId);
        var systems = state.WorldSystems!;
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Survival = new SettlementSurvivalState(state.WorldTick, []),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = person.InhabitantId == actor ? 6_000 : 9_000,
                Survival = new SurvivalCondition(),
                Position = person.InhabitantId == actor ? house.Position : person.Position,
                Equipment = person.InhabitantId == actor ? slots : person.Equipment,
                LastDecisionContext = null,
            }).ToArray(),
            WorldSystems = systems with
            {
                RegionalWeather = null,
                Config = systems.Config with
                { WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray() },
                Climate = systems.Climate with { Weather = WeatherKind.Clear },
            },
        };
        Assert.Equal(8, PersonalEquipmentRules.Capacity(inventory, actor, slots));
        Assert.Equal(11, PersonalEquipmentRules.CarriedQuantity(inventory, actor, slots));
        Assert.Equal(7, InventoryFixture.TransferLoadQuantity(inventory, JugId, 1));
        var choices = new RecoveryChoices(true);
        IDecisionProvider Provider(string id) => id == actor ? choices : new RecoveryChoices(false);
        using var world = PrivateWorldRuntime.Restore(state, Provider);
        PrivateWorldRuntime? resumed = null;
        try
        {
            for (var tick = 0; tick < 120 && world.Society.Inventory.GetLot(JugId).OwnerId == actor; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (resumed is null)
                {
                    world.Pause();
                    resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
                        PrivateWorldRuntimeCodec.Encode(world.ExportState())), id => new RecoveryChoices(id == actor));
                    world.Resume();
                    resumed.Resume();
                }
                else
                    Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
                    PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
            }
            Assert.Contains("make_room_for_food", choices.Offers);
            var jug = world.Society.Inventory.GetLot(JugId);
            var water = world.Society.Inventory.GetLot(WaterId);
            Assert.Equal((Alpha, 1, 8), (jug.OwnerId, jug.Quantity, jug.ContainerCapacity));
            Assert.Equal((Alpha, 6, JugId), (water.OwnerId, water.Quantity, water.ContainerLotId));
            Assert.Equal(houseRoom >= 7 ? house.InstanceId : null, jug.StorageBuildingId);
            Assert.Equal(jug.StorageBuildingId, water.StorageBuildingId);
            Assert.Equal(jug.GroundPosition, water.GroundPosition);
            var person = world.Inhabitants.Single(item => item.InhabitantId == actor);
            if (houseRoom >= 7)
            {
                Assert.Null(jug.GroundPosition);
                Assert.Equal(house.Position, person.Position);
            }
            else
            {
                var camp = state.Map.CampObjects.FirstOrDefault(item => item.Id == "storage")?.Position ??
                    state.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-warehouse").Position;
                Assert.Equal(new InventoryGroundPosition(camp.X, camp.Y), jug.GroundPosition);
                Assert.InRange(state.Map.FootDistance(person.Position, camp), 0, 1);
                Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
                    item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
            }
            Assert.Equal(slots, person.Equipment);
            foreach (var (id, kind) in gear)
            {
                var kept = world.Society.Inventory.GetLot(id);
                Assert.Equal((actor, kind, 1), (kept.OwnerId, kept.ItemKind, kept.Quantity));
                Assert.True(PersonalEquipmentRules.IsCarriedRoot(kept, actor));
                Assert.Null(kept.DeliveryBuildingId);
            }
            Assert.Equal(4, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, person.Equipment));
            Assert.Equal(4, PersonalEquipmentRules.FreeCapacity(world.Society.Inventory, actor, person.Equipment));
            Assert.Single(world.ExportState().Events, item => item.Kind == "spare_cargo_stored" &&
                item.Detail.StartsWith(actor + ":jug:1:", StringComparison.Ordinal));
            Assert.Equal(capacity - houseRoom + (houseRoom >= 7 ? 7 : 0),
                world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
            Assert.Equal(7, InventoryFixture.TransferLoadQuantity(world.Society.Inventory, JugId, 1));
            world.Validate();
            resumed!.Validate();
        }
        finally { resumed?.Dispose(); }
    }

    private sealed class RecoveryChoices(bool recover) : IDecisionProvider
    {
        public HashSet<string> Offers { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates) Offers.Add(candidate.Id);
            var selected = recover ? request.Observation.Candidates.FirstOrDefault(item => item.Id == "make_room_for_food") : null;
            selected ??= request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [selected] } }, cancellationToken);
        }
    }
}
