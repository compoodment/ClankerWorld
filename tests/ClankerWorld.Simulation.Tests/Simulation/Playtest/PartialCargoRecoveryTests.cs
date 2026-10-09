using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PartialCargoRecoveryTests
{
    private const string Stone = "partial-cargo-stone";
    private const string Wood = "partial-cargo-payment";
    private const string Offer = "partial-cargo-offer";
    private const string Jug = "a-protected-cargo-jug";
    private const string Water = "protected-cargo-water";
    private static readonly Lazy<Task<byte[]>> Mounted = new(CreateMountedAsync);
    private static readonly Lazy<Task<byte[]>> Hungry = new(CreateHungryAsync);

    [Theory]
    [InlineData(0, 8, 4)]
    [InlineData(1, 8, 4)]
    [InlineData(12, 12, 0)]
    public async Task ActualDismountDropsOnlyLooseSurplusAndKeepsTheOriginalClaim(int reserved, int carried, int dropped)
    {
        var state = WithCargo(PrivateWorldRuntimeCodec.Decode(await Mounted.Value), 12, reserved);
        var actor = state.Inhabitants[0].InhabitantId;
        var original = state.Society.Society.Inventory;
        using var world = Restore(state, actor, new Choices("animal_order"));
        using var replay = Restore(Copy(state), actor, new Choices("animal_order"));
        var order = new OwnerInstructionRequest("partial-dismount", "owner:test", actor, OwnerInstructionKind.MustDo, "dismount Moss");
        _ = world.SubmitInstruction(order);
        _ = replay.SubmitInstruction(order);
        await RefuseAsync(world);
        for (var tick = 0; tick < 12 && world.Animals.Single().RiderId is not null; tick++)
            await AdvancePairAsync(world, replay);
        Assert.Null(world.Animals.Single().RiderId);
        var inventory = world.Society.Inventory;
        Assert.Equal(carried, inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "stone" &&
            PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity));
        var point = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        Assert.Equal(dropped, inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "stone" &&
            lot.GroundPosition == new InventoryGroundPosition(point.X, point.Y)).Sum(lot => lot.Quantity));
        Assert.Equal(carried, inventory.GetLot(Stone).Quantity);
        if (dropped > 0) Assert.Equal(Stone, Assert.Single(inventory.Lots, lot => lot.OwnerId == actor &&
            lot.ItemKind == "stone" && lot.GroundPosition is not null).ProvenanceLotId);
        Assert.Equal(12, inventory.Lots.Where(lot => lot.ItemKind == "stone" && lot.OwnerId == actor).Sum(lot => lot.Quantity));
        Assert.Equal(8, PersonalEquipmentRules.Capacity(inventory, actor, null));
        AssertClaims(original, inventory, reserved);
        AssertReload(world);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(8, false)]
    public async Task NativeFoodRecoveryStoresOnlyUnreservedStoneAndEatsActualHouseBread(int reserved, bool recovers)
    {
        var state = WithCargo(PrivateWorldRuntimeCodec.Decode(await Hungry.Value), 8, reserved);
        var actor = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var original = state.Society.Society.Inventory;
        var choices = new Choices("make_room_for_food", "collect_shared_food", "consume_food");
        using var world = Restore(state, actor, choices);
        using var replay = Restore(Copy(state), actor, new Choices("make_room_for_food", "collect_shared_food", "consume_food"));
        await RefuseAsync(world);
        for (var tick = 0; tick < 40; tick++) await AdvancePairAsync(world, replay);
        Assert.Equal(recovers, choices.RoomOffered);
        Assert.Equal(recovers, world.ExportState().Events.Any(item => item.Kind == "food_consumed" && item.Detail == actor));
        var inventory = world.Society.Inventory;
        Assert.Equal(recovers ? 7 : 8, inventory.GetLot(Stone).Quantity);
        Assert.Equal(8, inventory.Lots.Where(lot => lot.ItemKind == "stone").Sum(lot => lot.Quantity));
        Assert.Equal(recovers ? 1 : 0, inventory.Lots.Where(lot => lot.ItemKind == "stone" && lot.OwnerId == household).Sum(lot => lot.Quantity));
        if (recovers)
        {
            var stored = Assert.Single(inventory.Lots, lot => lot.ItemKind == "stone" && lot.OwnerId == household);
            Assert.Equal(Stone, stored.ProvenanceLotId);
            Assert.Equal(state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == household &&
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house")).InstanceId,
                stored.StorageBuildingId);
            Assert.True(world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints > 1_500);
            Assert.True(inventory.Lots.Where(lot => lot.ItemKind == "bread").Sum(lot => lot.Quantity) < 8);
        }
        else Assert.Equal(8, inventory.GetLot("partial-cargo-bread").Quantity);
        AssertClaims(original, inventory, reserved);
        AssertReload(world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AClaimOnTheVesselOrItsContentsPreservesTheWholeFamilyDuringDismount(bool reserveContents)
    {
        var state = WithCargo(PrivateWorldRuntimeCodec.Decode(await Mounted.Value), 12, 1);
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Jug, "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, Water, "fresh_water", actor, 2, containerLotId: Jug);
        inventory = InventoryFixture.Reserve(inventory, "protected-cargo-family", actor, reserveContents ? Water : Jug,
            1, "held-vessel-work", inventory.WorldTick + 1_000);
        state = WithInventory(state, inventory);
        using var world = Restore(state, actor, new Choices("animal_order"));
        using var replay = Restore(Copy(state), actor, new Choices("animal_order"));
        var order = new OwnerInstructionRequest("protected-dismount", "owner:test", actor, OwnerInstructionKind.MustDo, "dismount Moss");
        _ = world.SubmitInstruction(order);
        _ = replay.SubmitInstruction(order);
        await RefuseAsync(world);
        for (var tick = 0; tick < 12 && world.Animals.Single().RiderId is not null; tick++) await AdvancePairAsync(world, replay);
        Assert.Null(world.Animals.Single().RiderId);
        var final = world.Society.Inventory;
        Assert.True(PersonalEquipmentRules.IsCarried(final.GetLot(Jug), actor));
        Assert.Null(final.GetLot(Jug).GroundPosition);
        Assert.Equal((actor, Jug, 2), (final.GetLot(Water).OwnerId, final.GetLot(Water).ContainerLotId, final.GetLot(Water).Quantity));
        Assert.Equal(5, final.GetLot(Stone).Quantity);
        Assert.Equal(7, final.Lots.Where(lot => lot.ItemKind == "stone" && lot.GroundPosition is not null).Sum(lot => lot.Quantity));
        Assert.Equal(inventory.GetReservation("protected-cargo-family"), final.GetReservation("protected-cargo-family"));
        Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(final, actor, null));
        AssertClaims(inventory, final, 1);
        AssertReload(world);
    }

    private static async Task<byte[]> CreateMountedAsync()
    {
        using var setup = NormalPathWorld.CreateGenerated("animal-horse-cargo", _ => new Choices("safe_idle"));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var home = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var definition = state.WorldContent!.Buildings.Single(item => item.Tags.Contains("animal-yard"));
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "partial-yard-cost-" + cost.ResourceId, cost.ResourceId, home, cost.Amount);
        using var builder = PrivateWorldRuntime.Restore(WithInventory(state, inventory));
        foreach (var tile in state.Map.Tiles.OrderBy(item => state.Map.FootDistance(item.Position, state.Inhabitants[0].Position)))
            if (builder.PlaceBuilding("partial-cargo-yard", definition.CanonicalId, tile.Position, home).Applied) break;
        var yard = Assert.Single(builder.WorldSimulation.Buildings, building => building.InstanceId == "partial-cargo-yard");
        foreach (var cost in definition.BuildCosts)
            Assert.Equal(inventory.Lots.Where(lot => lot.OwnerId == home && lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity) - cost.Amount,
                builder.Society.Inventory.Lots.Where(lot => lot.OwnerId == home && lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity));
        state = Fresh(builder.ExportState(), actor, yard.Position, 9_500);
        var day = state.WorldSystems!.Config.TicksPerDay;
        var horse = new AnimalState("partial-cargo-horse", "Moss", "horse", "female", -7L * day, yard.Position,
            "household:" + home, home, yard.InstanceId, CareUntilTick: state.Society.Society.WorldTick + day);
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "partial-cargo-saddle", "saddle", home, 1);
        state = WithInventory(state, inventory) with { AnimalWorld = new(true, [horse], []) };
        using var world = Restore(state, actor, new Choices("animal_order"));
        _ = world.SubmitInstruction(new("partial-saddle", "owner:test", actor, OwnerInstructionKind.MustDo, "saddle Moss"));
        for (var tick = 0; tick < 30 && world.Animals.Single().SaddleLotId is null; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("partial-cargo-saddle", world.Animals.Single().SaddleLotId);
        _ = world.SubmitInstruction(new("partial-mount", "owner:test", actor, OwnerInstructionKind.MustDo, "mount Moss"));
        for (var tick = 0; tick < 30 && world.Animals.Single().RiderId is null; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(actor, world.Animals.Single().RiderId);
        world.Validate();
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private static async Task<byte[]> CreateHungryAsync()
    {
        using var world = NormalPathWorld.CreateGenerated("child-carried-milk-audit", _ => new Choices("safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var state = world.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var home = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var house = state.WorldSimulation!.Buildings.Single(building => building.HouseholdId == home &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var inventory = state.Society.Society.Inventory with
        { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor && !(lot.OwnerId == home && lot.ItemKind == "food")).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "partial-cargo-bread", "bread", home, 8, storageBuildingId: house.InstanceId);
        return PrivateWorldRuntimeCodec.Encode(Fresh(WithInventory(state, inventory), actor, house.Position, 1_500));
    }

    private static PrivateWorldRuntimeState WithCargo(PrivateWorldRuntimeState state, int quantity, int reserved)
    {
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = state.Society.Society.Inventory with { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, Stone, "stone", actor, quantity);
        if (reserved > 0)
        {
            var other = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
            inventory = InventoryFixture.AddLot(inventory, Wood, "wood", other, 1);
            // A real open kernel offer is an explicit pending-work fixture, not a fabricated runtime trade.
            inventory = InventoryFixture.CreateDirectBarterOffer(inventory, new(Offer, 1, actor, other, Stone, reserved, Wood, 1, inventory.WorldTick + 1_000));
        }
        return WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            { Equipment = null, Project = null, LastDecisionContext = null }).ToArray()
        };
    }

    private static void AssertClaims(InventoryCheckpoint before, InventoryCheckpoint after, int reserved)
    {
        Assert.Equal(before.Reservations, after.Reservations);
        Assert.Equal(before.Offers, after.Offers);
        if (reserved == 0) return;
        Assert.True(after.GetLot(Stone).Quantity >= reserved);
        var offer = after.GetOffer(Offer);
        // The unchanged source identity must still settle the exact promised units after surplus moves.
        var accepted = InventoryFixture.AcceptDirectBarterOffer(after, Offer, 1, offer.FirstPartyId);
        var settled = InventoryFixture.AcceptDirectBarterOffer(accepted, Offer, 1, offer.SecondPartyId);
        Assert.Equal(DirectBarterState.Settled, settled.GetOffer(Offer).State);
    }

    private static PrivateWorldRuntimeState Fresh(PrivateWorldRuntimeState state, string actor, GridPoint position, int fullness) => state with
    {
        JevEnabled = false,
        RoutineHelper = RoutineHelperSettings.Off,
        Inhabitants = state.Inhabitants.Select(person => person with
        {
            Position = person.InhabitantId == actor ? position : person.Position,
            HungerBasisPoints = person.InhabitantId == actor ? fullness : 9_500,
            Survival = new(),
            Project = null,
            LastDecisionContext = null
        }).ToArray(),
    };
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntimeState Copy(PrivateWorldRuntimeState state) => PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, Choices choices) =>
        PrivateWorldRuntime.Restore(state, id => id == actor ? choices : new Choices("safe_idle"));
    private static async Task RefuseAsync(PrivateWorldRuntime world)
    {
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }
    private static async Task AdvancePairAsync(PrivateWorldRuntime world, PrivateWorldRuntime replay)
    {
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }
    private static void AssertReload(PrivateWorldRuntime world)
    {
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        restored.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
    private sealed class Choices(params string[] prefixes) : IDecisionProvider
    {
        public bool RoomOffered { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            RoomOffered |= observation.Candidates.Any(item => item.Id == "make_room_for_food");
            var selected = prefixes.Select(prefix => observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .FirstOrDefault(item => item is not null) ?? observation.Candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                observation.Candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
        }
    }
}
