using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class AnimalPipelineTests
{
    [Fact]
    public async Task NormalWorldSeedsPhysicalWildHerdsWithoutGivingHouseholdsAnimalsAndReplaysTheTick()
    {
        using var world = NormalPathWorld.CreateGenerated("animal-wild-arrival", _ => new AnimalChooser());
        var baseline = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(baseline), _ => new AnimalChooser());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.NotEmpty(world.Animals);
        Assert.All(world.Animals, animal => { Assert.Null(animal.HouseholdId); Assert.True(world.ExportState().Map.IsPassable(animal.Position)); });
        Assert.All(world.Animals.GroupBy(animal => animal.HerdId), herd => Assert.InRange(herd.Count(), 1, 8));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        world.Validate();
    }

    [Theory]
    [InlineData("chicken", "eggs", 1, 1, 1)]
    [InlineData("sheep", "wool", 2, 2, 2)]
    [InlineData("cow", "milk", 2, 2, 2)]
    public async Task CareConsumesRealFeedAndJugWaterThenOnePhysicalBatchWaitsForLocalCollection(string species,
        string product, int feedCost, int waterCost, int productQuantity)
    {
        var (prepared, actor, home, yard) = CreateYard("animal-care-" + species);
        var position = yard.Position;
        var day = prepared.WorldSystems!.Config.TicksPerDay;
        var animal = new AnimalState("test-animal", "Moss", species, "female", -(long)AnimalRules.Definition(species).AdultDays * day,
            position, "household:" + home, home, yard.InstanceId);
        var inventory = InventoryFixture.AddLot(prepared.Society.Society.Inventory, "care-grain", "grain", actor, feedCost);
        inventory = InventoryFixture.AddLot(inventory, "care-jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "care-water", "fresh_water", actor, waterCost, containerLotId: "care-jug");
        inventory = InventoryFixture.AddLot(inventory, "milk-jug", "water_jug", home, 1,
            storageBuildingId: yard.InstanceId);
        var state = At(prepared, actor, position, inventory, [animal]);
        var chooser = new AnimalChooser("animal:care:");
        using var cared = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? chooser : new AnimalChooser());
        await Until(cared, () => cared.Animals.Single().CareUntilTick > cared.WorldTick);
        Assert.DoesNotContain(cared.Society.Inventory.Lots, lot => lot.Id is "care-grain" or "care-water");
        Assert.Equal("water_jug", cared.Society.Inventory.GetLot("care-jug").ItemKind);
        var progress = cared.ExportState() with { AnimalWorld = cared.ExportState().AnimalWorld with
        { Animals = [cared.Animals.Single() with { ProductProgressTicks = AnimalRules.Definition(species).ProductDays * day - 1 }] } };
        chooser.Prefix = "safe_idle";
        using var ready = PrivateWorldRuntime.Restore(progress, id => id == actor ? chooser : new AnimalChooser());
        Assert.True((await ready.AdvanceOneTickAsync()).Advanced);
        var lotId = ready.Animals.Single().ReadyProductLotId!;
        Assert.NotNull(lotId);
        var lot = ready.Society.Inventory.GetLot(lotId);
        Assert.Equal((product, home, productQuantity), (lot.ItemKind, lot.OwnerId, lot.Quantity));
        Assert.Equal(new InventoryGroundPosition(position.X, position.Y), lot.GroundPosition);
        Assert.Equal(0, PersonalEquipmentRules.AvailableQuantity(ready.Society.Inventory, lot));
        var bytes = PrivateWorldRuntimeCodec.Encode(ready.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), id => id == actor ? chooser : new AnimalChooser());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        Assert.False((await ready.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(ready.ExportState()));
        chooser.Prefix = "animal_order";
        reload.SubmitInstruction(new("collect-" + species, "owner", actor, OwnerInstructionKind.MustDo, "collect from Moss"));
        await Until(reload, () => reload.Animals.Single().ReadyProductLotId is null);
        var collected = reload.Society.Inventory.Lots.Single(lot => lot.ItemKind == product);
        Assert.Equal(home, collected.OwnerId);
        Assert.Equal(productQuantity, collected.Quantity);
        Assert.True(PersonalEquipmentRules.IsPhysicallyCarried(reload.Society.Inventory, collected, actor));
        if (product == "milk") Assert.Equal("milk-jug", collected.ContainerLotId);
        else Assert.Null(collected.ContainerLotId);
        reload.Validate();
    }

    [Fact]
    public async Task ReservedFeedCannotBeSpentAndNoCareMeansNoProductsOrBreedingProgress()
    {
        var (prepared, actor, home, yard) = CreateYard("animal-reserved-feed");
        var day = prepared.WorldSystems!.Config.TicksPerDay;
        var female = new AnimalState("hen", "Juniper", "chicken", "female", -3L * day, yard.Position, "household:" + home,
            home, yard.InstanceId, ProductProgressTicks: day - 1, Pregnancy: new("rooster", day - 1, 0));
        var male = new AnimalState("rooster", "Ash", "chicken", "male", -3L * day, new(yard.Position.X + 1, yard.Position.Y),
            "household:" + home, home, yard.InstanceId);
        var inventory = InventoryFixture.AddLot(prepared.Society.Society.Inventory, "held-feed", "grain", actor, 1);
        inventory = InventoryFixture.Reserve(inventory, "planting-feed", actor, "held-feed", 1, "planting", 1000);
        inventory = InventoryFixture.AddLot(inventory, "jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "water", "fresh_water", actor, 1, containerLotId: "jug");
        using var world = PrivateWorldRuntime.Restore(At(prepared, actor, yard.Position, inventory, [female, male]),
            id => id == actor ? new AnimalChooser("animal:care:") : new AnimalChooser());
        for (var tick = 0; tick < 20; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, world.Society.Inventory.GetLot("held-feed").Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("water").Quantity);
        Assert.All(world.Animals, animal => Assert.Null(animal.ReadyProductLotId));
        Assert.Equal(day - 1, world.Animals.Single(animal => animal.Id == "hen").Pregnancy!.ProgressTicks);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind is "animal_born" or "animal_cared");
        world.Validate();
    }

    [Fact]
    public void DeveloperAnimalPlacementIsPausedBoundedDurableAndRetriesCannotDuplicateAnAnimal()
    {
        var (state, actor, _, _) = CreateYard("animal-developer");
        state = state with { Inhabitants = state.Inhabitants.Select(person => person with
        { Position = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == state.Society.Society.GetInhabitant(person.InhabitantId).HouseholdId &&
            state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house"))).Position }).ToArray() };
        using var world = PrivateWorldRuntime.Restore(state, _ => new AnimalChooser());
        Assert.False(world.ApplyDeveloperEdit(DeveloperEditTests.Edit(world, "add_animal", "horse:female", actor: actor)).Applied);
        world.Pause();
        var edit = DeveloperEditTests.Edit(world, "add_animal", "horse:female", actor: actor);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Throws<IOException>(() => world.ApplyDeveloperEdit(edit, _ => throw new IOException("storage offline")));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True(world.ApplyDeveloperEdit(edit).Applied);
        Assert.True(world.ApplyDeveloperEdit(edit).AlreadyApplied);
        for (var index = 0; index < 3; index++) Assert.True(world.ApplyDeveloperEdit(DeveloperEditTests.Edit(world, "add_animal", "chicken:female", actor: actor)).Applied);
        Assert.False(world.ApplyDeveloperEdit(DeveloperEditTests.Edit(world, "add_animal", "cow:male", actor: actor)).Applied);
        Assert.Equal(4, world.Animals.Count);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        Assert.False(world.RemoveBuilding("test-yard", null, world.Animals[0].HouseholdId).Applied);
    }

    [Fact]
    public void CurrentCheckpointRejectsMissingAnimalIdentityAndJugContentsCannotMix()
    {
        var (state, actor, home, yard) = CreateYard("animal-damaged");
        var animal = new AnimalState("animal", "Bee", "chicken", "female", 0, yard.Position, "household:" + home, home, yard.InstanceId);
        var bytes = PrivateWorldRuntimeCodec.Encode(state with { AnimalWorld = new(true, [animal], []) });
        var document = JsonNode.Parse(bytes)!;
        var root = document["state"] ?? document["runtime"];
        Assert.NotNull(root);
        var row = root!["animalWorld"]!["animals"]![0]!.AsObject();
        Assert.True(row.Remove("position"));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString())));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "milk", "milk", actor, 1, containerLotId: "jug");
        Assert.Throws<InvalidDataException>(() => InventoryFixture.AddLot(inventory, "water", "fresh_water", actor, 1, containerLotId: "jug"));
    }

    [Fact]
    public async Task BirthUsesAReservedPlaceBesideTheMotherAndCannotOverfillTheYard()
    {
        var (state, actor, home, yard) = CreateYard("animal-birth");
        var day = state.WorldSystems!.Config.TicksPerDay;
        var mother = new AnimalState("mother", "Moss", "sheep", "female", -5L * day, yard.Position,
            "household:" + home, home, yard.InstanceId, CareUntilTick: day,
            Pregnancy: new("father", 4 * day - 1, 0));
        var father = mother with { Id = "father", Name = "Ash", Sex = "male", Pregnancy = null,
            Position = new(yard.Position.X + 1, yard.Position.Y) };
        using var world = PrivateWorldRuntime.Restore(At(state, actor, yard.Position, state.Society.Society.Inventory, [mother, father]), _ => new AnimalChooser());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var child = Assert.Single(world.Animals, animal => animal.BornTick == 1);
        Assert.Equal((home, yard.InstanceId, 0L), (child.HouseholdId, child.YardId, child.CareUntilTick));
        Assert.InRange(state.Map.FootDistance(mother.Position, child.Position), 1, 1);
        Assert.Null(world.Animals.Single(animal => animal.Id == mother.Id).Pregnancy);
        Assert.Equal(1 + 2 * day, world.Animals.Single(animal => animal.Id == mother.Id).BreedingReadyTick);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new AnimalChooser());
        Assert.True((await reload.AdvanceOneTickAsync()).Advanced);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
        var full = state with { AnimalWorld = new(true, [mother, father,
            mother with { Id = "extra", Name = "Extra", Pregnancy = new("father", 0, 0) }], []) };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(full));
    }

    [Fact]
    public async Task OnlyOldAgeKillsAnimalsAndOwnedHideRemainsPrivateAtTheDeathPosition()
    {
        var (state, actor, home, yard) = CreateYard("animal-old-age");
        state = state with { WorldSystems = WorldSystemsRules.CreateGenesis(state.WorldSeed,
            state.WorldSystems!.Config with { TicksPerDay = 2, CalendarOffsetTicks = 0 }, state.WorldSystems.Ecology.Resources,
            state.WorldSystems.Factions, state.WorldSystems.Currency, state.WorldSystems.Culture, state.WorldSystems.Chunks) };
        var animal = new AnimalState("cow", "Moss", "cow", "female", -14, yard.Position, "household:" + home, home, yard.InstanceId);
        using var world = PrivateWorldRuntime.Restore(At(state, actor, yard.Position, state.Society.Society.Inventory, [animal]), _ => new AnimalChooser());
        for (var tick = 0; tick < 106; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var dead = Assert.Single(world.Animals);
        Assert.Equal(106L, dead.DiedTick);
        var hide = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "hide");
        Assert.Equal((home, 1, new InventoryGroundPosition(dead.Position.X, dead.Position.Y)),
            (hide.OwnerId, hide.Quantity, hide.GroundPosition));
        Assert.Single(world.ExportState().Events, item => item.Kind == "animal_died");
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "animal_cared");
        world.Validate();
    }

    [Fact]
    public async Task ARealSaddleEnablesMountedCargoAndDismountPreservesGoodsAtTheActualPosition()
    {
        var (state, actor, home, yard) = CreateYard("animal-horse-cargo");
        var day = state.WorldSystems!.Config.TicksPerDay;
        var horse = new AnimalState("horse", "Moss", "horse", "female", -7L * day, yard.Position,
            "household:" + home, home, yard.InstanceId, CareUntilTick: day);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "saddle", "saddle", home, 1);
        inventory = InventoryFixture.Relocate(inventory, "carry-saddle", "saddle", home, 1, carrierId: actor);
        using var world = PrivateWorldRuntime.Restore(At(state, actor, yard.Position, inventory, [horse]),
            id => id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
        world.SubmitInstruction(new("saddle", "owner", actor, OwnerInstructionKind.MustDo, "saddle Moss"));
        await Until(world, () => world.Animals.Single().SaddleLotId is not null);
        Assert.Equal("saddle", world.Animals.Single().SaddleLotId);
        Assert.Equal(0, PersonalEquipmentRules.AvailableQuantity(world.Society.Inventory, world.Society.Inventory.GetLot("saddle")));
        world.SubmitInstruction(new("mount", "owner", actor, OwnerInstructionKind.MustDo, "mount Moss"));
        await Until(world, () => world.Animals.Single().RiderId == actor);
        world.Pause();
        Assert.True(world.ApplyDeveloperEdit(DeveloperEditTests.Edit(world, "give_goods", "stone", 14, actor)).Applied);
        Assert.Equal(14, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "stone").Sum(lot => lot.Quantity));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), id => id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Resume();
        restored.SubmitInstruction(new("dismount", "owner", actor, OwnerInstructionKind.MustDo, "dismount Moss"));
        await Until(restored, () => restored.Animals.Single().RiderId is null);
        var person = restored.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.True(PersonalEquipmentRules.CarriedQuantity(restored.Society.Inventory, actor, person.Equipment) <=
            PersonalEquipmentRules.Capacity(restored.Society.Inventory, actor, person.Equipment));
        Assert.Equal(14, restored.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "stone").Sum(lot => lot.Quantity));
        Assert.Contains(restored.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "stone" &&
            lot.GroundPosition == new InventoryGroundPosition(person.Position.X, person.Position.Y));
        restored.Validate();
    }

    [Fact]
    public async Task ARepeatedCareOrderFetchesRealHouseholdFeedAndAJugBeforeWalkingToTheAnimal()
    {
        var (state, actor, home, yard) = CreateYard("animal-supply-route");
        var house = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == home &&
            state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house")));
        var day = state.WorldSystems!.Config.TicksPerDay;
        var hen = new AnimalState("hen", "Moss", "chicken", "female", -3L * day, yard.Position, "household:" + home, home, yard.InstanceId);
        var ground = new InventoryGroundPosition(house.Position.X, house.Position.Y);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "supply-feed", "grain", home, 4, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "supply-jug", "water_jug", home, 1, groundPosition: ground);
        inventory = InventoryFixture.AddLot(inventory, "supply-water", "fresh_water", home, 4, groundPosition: ground, containerLotId: "supply-jug");
        using var world = PrivateWorldRuntime.Restore(At(state, actor, house.Position, inventory, [hen]), id =>
            id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
        world.SubmitInstruction(new("care", "owner", actor, OwnerInstructionKind.MustDo, "repeat care for Moss"));
        await Until(world, () => world.Animals.Single().CareUntilTick > world.WorldTick);
        Assert.Equal(yard.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Equal(3, world.Society.Inventory.Lots.Where(lot => lot.Id.StartsWith("supply-feed", StringComparison.Ordinal)).Sum(lot => lot.Quantity));
        Assert.Equal(3, world.Society.Inventory.GetLot("supply-water").Quantity);
        Assert.Equal(home, world.Society.Inventory.GetLot("supply-jug").OwnerId);
        Assert.Equal(actor, world.Society.Inventory.GetLot("supply-jug").CarrierId);
        Assert.Equal(1, world.ExportState().Instructions!.Single().Order!.CompletedUnits);
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactAnimalGiftOrSaleRequiresTwoFreshPersonalChoicesAndClearsOldPermissions(bool paid)
    {
        var (state, seller, home, yard) = CreateYard("animal-transfer-" + paid);
        var buyer = state.Society.Society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand == SocietyAgeBand.Adult && person.HouseholdId != home).Id;
        var buyerHome = state.Society.Society.GetInhabitant(buyer).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "second-yard-wood", "wood", buyerHome, 8,
            groundPosition: new(yard.Position.X, yard.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "second-yard-rope", "rope", buyerHome, 2, groundPosition: new(yard.Position.X, yard.Position.Y));
        using var setup = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } }, _ => new AnimalChooser());
        var definition = setup.WorldContent.Buildings.Single(item => item.Tags.Contains("animal-yard"));
        var placed = false;
        foreach (var tile in state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, yard.Position)))
            if (setup.PlaceBuilding("receiving-yard", definition.CanonicalId, tile.Position, buyerHome).Applied) { placed = true; break; }
        Assert.True(placed);
        state = setup.ExportState();
        var day = state.WorldSystems!.Config.TicksPerDay;
        var animal = new AnimalState("horse", "Moss", "horse", "female", -7L * day, yard.Position, "household:" + home, home, yard.InstanceId)
        { CarePermissions = [buyer], RidingPermissions = [buyer] };
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "payment", "wood", buyer, 2);
        state = At(state, seller, yard.Position, inventory, [animal]) with
        { Inhabitants = At(state, seller, yard.Position, inventory, [animal]).Inhabitants.Select(person => person.InhabitantId == buyer ?
            person with { Position = new(yard.Position.X + 1, yard.Position.Y) } : person).ToArray() };
        var offerText = paid ? "Offer Moss to" : "Offer the specific animal Moss as a gift";
        IDecisionProvider Provider(string id, DecisionProviderKind kind) => id == seller ?
            new AnimalChooser(kind: kind, select: candidate => candidate.Description.StartsWith(offerText, StringComparison.Ordinal)) :
            id == buyer ? new AnimalChooser(kind: kind, select: candidate => candidate.Description.StartsWith("Accept Moss", StringComparison.Ordinal)) : new AnimalChooser();
        using var fallback = PrivateWorldRuntime.Restore(state, id => Provider(id, DecisionProviderKind.Deterministic));
        for (var index = 0; index < 5; index++) Assert.True((await fallback.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(fallback.ExportState().AnimalWorld.Offers);
        Assert.Equal(home, fallback.Animals.Single().HouseholdId);
        Assert.Equal(2, fallback.Society.Inventory.GetLot("payment").Quantity);
        using var world = PrivateWorldRuntime.Restore(state, id => Provider(id, DecisionProviderKind.LargeLanguageModel));
        await Until(world, () => world.Animals.Single().HouseholdId == buyerHome);
        var transferred = world.Animals.Single();
        Assert.Equal("receiving-yard", transferred.YardId);
        Assert.Equal(yard.Position, transferred.Position);
        Assert.Empty(transferred.CarePermissions);
        Assert.Empty(transferred.RidingPermissions);
        Assert.Empty(world.ExportState().AnimalWorld.Offers);
        Assert.Equal(paid ? 1 : 2, world.Society.Inventory.GetLot("payment").Quantity);
        Assert.Equal(paid ? 1 : 0, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == home && lot.Id.StartsWith("payment", StringComparison.Ordinal)).Sum(lot => lot.Quantity));
        world.Validate();
    }

    private static (PrivateWorldRuntimeState State, string Actor, string Home, PlacedBuilding Yard) CreateYard(string seed)
    {
        using var setup = NormalPathWorld.CreateGenerated(seed, _ => new AnimalChooser());
        var state = setup.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var home = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "yard-wood", "wood", home, 8,
            groundPosition: new(state.Inhabitants[0].Position.X, state.Inhabitants[0].Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "yard-rope", "rope", home, 2,
            groundPosition: new(state.Inhabitants[0].Position.X, state.Inhabitants[0].Position.Y));
        using var world = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } },
            _ => new AnimalChooser());
        var definition = world.WorldContent.Buildings.Single(item => item.Tags.Contains("animal-yard"));
        foreach (var tile in state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, state.Inhabitants[0].Position)))
        {
            var result = world.PlaceBuilding("test-yard", definition.CanonicalId, tile.Position, home);
            if (!result.Applied) continue;
            var yard = world.WorldSimulation.Buildings.Single(building => building.InstanceId == "test-yard");
            Assert.Equal(inventory.Lots.Where(lot => lot.OwnerId == home && lot.ItemKind == "wood").Sum(lot => lot.Quantity) - 8,
                world.Society.Inventory.Lots.Where(lot => lot.OwnerId == home && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
            Assert.Equal(inventory.Lots.Where(lot => lot.OwnerId == home && lot.ItemKind == "rope").Sum(lot => lot.Quantity) - 2,
                world.Society.Inventory.Lots.Where(lot => lot.OwnerId == home && lot.ItemKind == "rope").Sum(lot => lot.Quantity));
            var saved = world.ExportState();
            return (saved with { AnimalWorld = new(true, [], []) }, actor, home, yard);
        }
        throw new InvalidOperationException("No legal site for the paid animal yard.");
    }

    private static PrivateWorldRuntimeState At(PrivateWorldRuntimeState state, string actor, GridPoint position,
        InventoryCheckpoint inventory, AnimalState[] animals) => state with
    {
        Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        AnimalWorld = new(true, animals.OrderBy(animal => animal.Id, StringComparer.Ordinal).ToArray(), []),
        Inhabitants = state.Inhabitants.Select(person => person with { Position = person.InhabitantId == actor ? position : person.Position,
            HungerBasisPoints = 9500, LastDecisionContext = null, Project = null }).ToArray(),
    };

    private static async Task Until(PrivateWorldRuntime world, Func<bool> reached)
    {
        for (var tick = 0; tick < 80 && !reached(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(reached(), "The legal animal action did not complete within 80 ticks. " +
            System.Text.Json.JsonSerializer.Serialize(new { world.Animals, Actors = world.Inhabitants.Select(person => new { person.InhabitantId, person.Position }),
                Events = world.ExportState().Events.TakeLast(12) }));
    }

    private sealed class AnimalChooser(string prefix = "safe_idle", DecisionProviderKind kind = DecisionProviderKind.Deterministic,
        Func<CognitionCandidate, bool>? select = null) : IDecisionProvider
    {
        public string Prefix { get; set; } = prefix;
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates;
            var choice = candidates.FirstOrDefault(candidate => select?.Invoke(candidate) ?? candidate.Id.StartsWith(Prefix, StringComparison.Ordinal)) ??
                candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind,
                ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                choice.Id, 1, candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice.Id ? 1d : 0d)));
        }
    }
}
