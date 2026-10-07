using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Viewer.Observation;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class AnimalPipelineTests
{
    [Theory]
    [InlineData(false, "pending")]
    [InlineData(true, "pending")]
    [InlineData(false, "decline")]
    [InlineData(false, "expiry")]
    public async Task HousematesKeepMilkOffersCheckpointableWithOneOfferPerSourceLot(bool splitMilk, string release)
    {
        var (state, seller, home, yard) = CreateYard("animal-shared-milk-offers");
        var definition = state.WorldContent!.Buildings.Single(item => item.LocalId == "store-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "shared-store-cost-" + cost.ResourceId,
                cost.ResourceId, home, cost.Amount, groundPosition: new(yard.Position.X, yard.Position.Y));
        using var setup = PrivateWorldRuntime.Restore(At(state, seller, yard.Position, inventory, []), _ => new AnimalChooser());
        foreach (var tile in state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, yard.Position)))
            if (setup.PlaceBuilding("shared-milk-store", definition.CanonicalId, tile.Position, home).Applied) break;
        var store = Assert.Single(setup.WorldSimulation.Buildings, item => item.InstanceId == "shared-milk-store");
        state = setup.ExportState();
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "00-shared-jug", "water_jug", home, 1,
            groundPosition: new(yard.Position.X, yard.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "00-shared-milk", "milk", home, splitMilk ? 1 : 2,
            containerLotId: "00-shared-jug");
        if (splitMilk)
        {
            inventory = InventoryFixture.AddLot(inventory, "01-shared-jug", "water_jug", home, 1,
                groundPosition: new(yard.Position.X, yard.Position.Y));
            inventory = InventoryFixture.AddLot(inventory, "01-shared-milk", "milk", home, 1,
                containerLotId: "01-shared-jug");
        }
        state = At(state, seller, yard.Position, inventory, []) with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
        };
        using var haul = PrivateWorldRuntime.Restore(state,
            id => id == seller ? new AnimalChooser("animal:milk_stock:") : new AnimalChooser());
        await Until(haul, () => haul.Society.Inventory.GetLot("00-shared-jug").StorageBuildingId == store.InstanceId &&
            (!splitMilk || haul.Society.Inventory.GetLot("01-shared-jug").StorageBuildingId == store.InstanceId));
        state = haul.ExportState();
        var sibling = state.Society.Society.Inhabitants.Single(person => person.HouseholdId == home && person.Id != seller).Id;
        var buyers = state.Society.Society.Inhabitants.Where(person => person.HouseholdId != home &&
            person.Status == SocietyInhabitantStatus.Active && person.AgeBand == SocietyAgeBand.Adult).Select(person => person.Id).ToArray();
        Assert.Equal(2, buyers.Length);
        inventory = state.Society.Society.Inventory;
        foreach (var buyer in buyers)
        {
            inventory = InventoryFixture.AddLot(inventory, buyer + "-receiving-jug", "water_jug", buyer, 1);
            inventory = InventoryFixture.AddLot(inventory, buyer + "-milk-payment", "wood", buyer, 1);
        }
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = store.Position,
                HungerBasisPoints = 9_500,
                LastDecisionContext = null,
                Project = null,
            }).ToArray(),
        };
        using var offering = PrivateWorldRuntime.Restore(state, id => id == seller
            ? new AnimalChooser("animal:milk_offer:", DecisionProviderKind.LargeLanguageModel) : new AnimalChooser());
        await Until(offering, () => offering.ExportState().AnimalWorld.MilkOffers.Count == 1);
        state = offering.ExportState() with
        {
            Inhabitants = offering.ExportState().Inhabitants.Select(person => person.InhabitantId == sibling
                ? person with { LastDecisionContext = null } : person).ToArray(),
        };
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        IDecisionProvider Provider(string id) => id == sibling
            ? new AnimalChooser("animal:milk_offer:", DecisionProviderKind.LargeLanguageModel) : new AnimalChooser();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        world.Resume();
        var directory = Directory.CreateTempSubdirectory("clanker-shared-milk-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), Provider);
            file.Save(world);
            Assert.True(File.Exists(Path.Combine(directory.FullName, "world.json")));
            for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            using var service = new PrivateWorldRuntimeService(world, file, presence);
            Assert.True(await service.TryAdvanceOnceAsync());
            world.Resume();
            Assert.True(await service.TryAdvanceOnceAsync());
            world.Validate();
            var offers = world.ExportState().AnimalWorld.MilkOffers;
            Assert.Equal(splitMilk ? 2 : 1, offers.Count);
            Assert.Equal(offers.Count, offers.Select(offer => offer.MilkLotId).Distinct(StringComparer.Ordinal).Count());
            foreach (var offer in offers)
            {
                var held = world.Society.Inventory.GetReservation(offer.Id + "-milk");
                Assert.Equal((offer.MilkLotId, home, 1, InventoryReservationState.Reserved),
                    (held.LotId, held.OwnerId, held.Quantity, held.State));
            }
            Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "milk").Sum(lot => lot.Quantity));
            Assert.All(buyers, buyer => Assert.Equal(1, world.Society.Inventory.GetLot(buyer + "-milk-payment").Quantity));
            Assert.Equal(home, world.Society.Inventory.GetLot("00-shared-jug").OwnerId);
            if (splitMilk) Assert.Equal(home, world.Society.Inventory.GetLot("01-shared-jug").OwnerId);
            var final = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var loaded = file.LoadOrCreate(state.WorldSeed);
            Assert.Equal(final, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
            using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(final), Provider);
            for (var tick = 0; tick < 2; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
            if (release == "pending") return;
            var previous = Assert.Single(world.ExportState().AnimalWorld.MilkOffers);
            state = world.ExportState() with
            {
                Inhabitants = world.ExportState().Inhabitants.Select(person => person.InhabitantId == previous.BuyerId
                    ? person with { LastDecisionContext = null } : person).ToArray(),
            };
            using var closing = PrivateWorldRuntime.Restore(state, id => release == "decline" && id == previous.BuyerId
                ? new AnimalChooser("animal:milk_decline:", DecisionProviderKind.LargeLanguageModel) : new AnimalChooser());
            for (var tick = 0; tick < 120 && closing.ExportState().AnimalWorld.MilkOffers.Count != 0; tick++)
                Assert.True((await closing.AdvanceOneTickAsync()).Advanced);
            Assert.Empty(closing.ExportState().AnimalWorld.MilkOffers);
            Assert.NotEqual(InventoryReservationState.Reserved, closing.Society.Inventory.GetReservation(previous.Id + "-milk").State);
            Assert.Equal((home, 2), (closing.Society.Inventory.GetLot("00-shared-milk").OwnerId,
                closing.Society.Inventory.GetLot("00-shared-milk").Quantity));
            Assert.Equal(home, closing.Society.Inventory.GetLot("00-shared-jug").OwnerId);
            Assert.All(buyers, buyer => Assert.Equal((buyer, 1),
                (closing.Society.Inventory.GetLot(buyer + "-milk-payment").OwnerId,
                 closing.Society.Inventory.GetLot(buyer + "-milk-payment").Quantity)));
            closing.Validate();
            state = closing.ExportState() with
            {
                Inhabitants = closing.ExportState().Inhabitants.Select(person => person.InhabitantId == sibling
                    ? person with { LastDecisionContext = null } : person).ToArray(),
            };
            using var renewed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), Provider);
            await Until(renewed, () => renewed.ExportState().AnimalWorld.MilkOffers.Count == 1);
            var next = Assert.Single(renewed.ExportState().AnimalWorld.MilkOffers);
            Assert.Equal((sibling, "00-shared-milk"), (next.SellerId, next.MilkLotId));
            Assert.Equal(InventoryReservationState.Reserved, renewed.Society.Inventory.GetReservation(next.Id + "-milk").State);
            renewed.Validate();
            var saved = PrivateWorldRuntimeCodec.Encode(renewed.ExportState());
            using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), Provider);
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("sheep")]
    [InlineData("cow")]
    public async Task LeadingMovesTheExactNewlyReadyProductAndItsReservationAcrossReload(string species)
    {
        var (state, actor, home, yard) = CreateYard("animal-led-product-" + species);
        var day = state.WorldSystems!.Config.TicksPerDay;
        var position = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            state.Map.FootDistance(point, yard.Position) == 3 && !state.Inhabitants.Any(person => person.Position == point));
        var definition = AnimalRules.Definition(species);
        var animal = new AnimalState("led", "Moss", species, "female", -(long)definition.AdultDays * day,
            position, "household:" + home, home, yard.InstanceId, CareUntilTick: day,
            ProductProgressTicks: definition.ProductDays * day - 1, LeaderId: actor, LeadDestination: yard.Position);
        using var world = PrivateWorldRuntime.Restore(At(state, actor, position, state.Society.Society.Inventory, [animal]),
            id => id == actor ? new AnimalChooser("animal:lead_home:") : new AnimalChooser());
        await Until(world, () => world.Animals.Single().Position != position && world.Animals.Single().ReadyProductLotId is not null);
        var moved = world.Animals.Single();
        var lot = world.Society.Inventory.GetLot(moved.ReadyProductLotId!);
        Assert.Equal((definition.Product, home, definition.ProductQuantity), (lot.ItemKind, lot.OwnerId, lot.Quantity));
        Assert.Equal(new InventoryGroundPosition(moved.Position.X, moved.Position.Y), lot.GroundPosition);
        var reservation = world.Society.Inventory.GetReservation(moved.ReadyProductReservationId!);
        Assert.Equal((lot.Id, home, lot.Quantity, InventoryReservationState.Reserved),
            (reservation.LotId, reservation.OwnerId, reservation.Quantity, reservation.State));
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == actor ? new AnimalChooser("animal:lead_home:") : new AnimalChooser());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutsiderCareProtectsTheAggregateReserveOfTheHouseholdSupplyingItsGreens(bool enough)
    {
        var (state, owner, home, yard) = CreateYard("animal-outsider-feed");
        var outsider = state.Society.Society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand == SocietyAgeBand.Adult && person.HouseholdId != home).Id;
        var outsiderHome = state.Society.Society.GetInhabitant(outsider).HouseholdId!;
        var family = state.Society.Society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
            person.HouseholdId == outsiderHome).Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var inventory = state.Society.Society.Inventory with
        { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != outsiderHome && !family.Contains(lot.OwnerId)).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "00-feed", "wild_greens", outsider, 1);
        inventory = InventoryFixture.AddLot(inventory, "01-feed", "wild_greens", outsider, 1);
        inventory = InventoryFixture.AddLot(inventory, "family-food", "berries", outsiderHome,
            family.Count * 2 - 1 + (enough ? 1 : 0), groundPosition: new(yard.Position.X, yard.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "animal-family-food", "berries", home, 20,
            groundPosition: new(yard.Position.X, yard.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "water-jug", "water_jug", outsider, 1);
        inventory = InventoryFixture.AddLot(inventory, "water", "fresh_water", outsider, 2, containerLotId: "water-jug");
        var day = state.WorldSystems!.Config.TicksPerDay;
        var animal = new AnimalState("cow", "Moss", "cow", "female", -7L * day, yard.Position,
            "household:" + home, home, yard.InstanceId)
        { CarePermissions = [outsider] };
        using var world = PrivateWorldRuntime.Restore(At(state, outsider, yard.Position, inventory, [animal]),
            id => id == outsider ? new AnimalChooser("animal:care:") : new AnimalChooser());
        if (enough) await Until(world, () => world.Animals.Single().CareUntilTick > world.WorldTick);
        else for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(enough ? 0 : 2, world.Society.Inventory.Lots.Where(lot => lot.Id is "00-feed" or "01-feed").Sum(lot => lot.Quantity));
        Assert.Equal(enough ? 0 : 2, world.Society.Inventory.Lots.Where(lot => lot.Id == "water").Sum(lot => lot.Quantity));
        Assert.Equal(enough, world.ExportState().Events.Any(item => item.Kind == "animal_cared"));
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrinkingMilkRequiresAUsableSourceJug(bool broken)
    {
        var (state, actor, _, yard) = CreateYard("animal-broken-milk");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "milk-jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "milk", "milk", actor, 1, containerLotId: "milk-jug");
        if (broken) inventory = inventory with
        { Lots = inventory.Lots.Select(lot => lot.Id == "milk-jug" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray() };
        state = At(state, actor, yard.Position, inventory, []) with
        { Inhabitants = At(state, actor, yard.Position, inventory, []).Inhabitants.Select(person => person.InhabitantId == actor ? person with { HungerBasisPoints = 3000 } : person).ToArray() };
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? new AnimalChooser("drink_milk") : new AnimalChooser());
        if (!broken) await Until(world, () => world.ExportState().Events.Any(item => item.Kind == "milk_drunk"));
        else for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(broken ? 1 : 0, world.Society.Inventory.Lots.Where(lot => lot.Id == "milk").Sum(lot => lot.Quantity));
        Assert.Equal(actor, world.Society.Inventory.GetLot("milk-jug").OwnerId);
        Assert.Equal(broken ? 0 : 10_000, world.Society.Inventory.GetLot("milk-jug").ConditionBasisPoints);
        world.Validate();
    }
}
