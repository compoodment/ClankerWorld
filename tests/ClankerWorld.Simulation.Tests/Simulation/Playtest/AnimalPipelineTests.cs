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
        var progress = cared.ExportState() with
        {
            AnimalWorld = cared.ExportState().AnimalWorld with
            { Animals = [cared.Animals.Single() with { ProductProgressTicks = AnimalRules.Definition(species).ProductDays * day - 1 }] }
        };
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
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = state.WorldSimulation!.Buildings.First(building => building.HouseholdId == state.Society.Society.GetInhabitant(person.InhabitantId).HouseholdId &&
                state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("house"))).Position
            }).ToArray()
        };
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
        var father = mother with
        {
            Id = "father",
            Name = "Ash",
            Sex = "male",
            Pregnancy = null,
            Position = new(yard.Position.X + 1, yard.Position.Y)
        };
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
        var full = state with
        {
            AnimalWorld = new(true, [mother, father,
            mother with { Id = "extra", Name = "Extra", Pregnancy = new("father", 0, 0) }], [])
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(full));
    }

    [Fact]
    public async Task OnlyOldAgeKillsAnimalsAndOwnedHideRemainsPrivateAtTheDeathPosition()
    {
        var (state, actor, home, yard) = CreateYard("animal-old-age");
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Config = state.Society.Society.Config with { TicksPerWorldDay = 2 },
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person with
                    {
                        BirthTick = person.BirthTick / 180,
                        BirthLifeTick = person.BirthLifeTick / 180
                    }).ToArray()
                }
            },
            WorldSystems = WorldSystemsRules.CreateGenesis(state.WorldSeed,
            state.WorldSystems!.Config with { TicksPerDay = 2, CalendarOffsetTicks = 0 }, state.WorldSystems.Ecology.Resources,
            state.WorldSystems.Factions, state.WorldSystems.Currency, state.WorldSystems.Culture, state.WorldSystems.Chunks)
        };
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
        inventory = InventoryFixture.Relocate(inventory, "carry-saddle", "saddle", home, 1);
        var chooser = new AnimalChooser("animal_order");
        using var world = PrivateWorldRuntime.Restore(At(state, actor, yard.Position, inventory, [horse]),
            id => id == actor ? chooser : new AnimalChooser());
        world.SubmitInstruction(new("saddle", "owner", actor, OwnerInstructionKind.MustDo, "saddle Moss"));
        await Until(world, () => world.Animals.Single().SaddleLotId is not null);
        Assert.Equal("saddle", world.Animals.Single().SaddleLotId);
        Assert.Equal(0, PersonalEquipmentRules.AvailableQuantity(world.Society.Inventory, world.Society.Inventory.GetLot("saddle")));
        world.SubmitInstruction(new("mount", "owner", actor, OwnerInstructionKind.MustDo, "mount Moss"));
        await Until(world, () => world.Animals.Single().RiderId == actor);
        var rideOrigin = world.Animals.Single().Position;
        var destination = new[] { new GridPoint(rideOrigin.X + 3, rideOrigin.Y), new(rideOrigin.X - 3, rideOrigin.Y),
            new(rideOrigin.X, rideOrigin.Y + 3), new(rideOrigin.X, rideOrigin.Y - 3) }.First(point =>
                Enumerable.Range(1, 3).All(step =>
                {
                    var previous = new GridPoint(rideOrigin.X + (point.X - rideOrigin.X) / 3 * (step - 1), rideOrigin.Y + (point.Y - rideOrigin.Y) / 3 * (step - 1));
                    var next = new GridPoint(rideOrigin.X + (point.X - rideOrigin.X) / 3 * step, rideOrigin.Y + (point.Y - rideOrigin.Y) / 3 * step);
                    return state.Map.CanFootStep(previous, next) && !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == next);
                }));
        chooser.Prefix = "move_to";
        world.SubmitInstruction(new("ride", "owner", actor, OwnerInstructionKind.MustDo, $"move to {destination.X},{destination.Y}"));
        await Until(world, () => world.Animals.Single().Position != rideOrigin);
        Assert.Equal(2, world.ExportState().Events.Count(item => item.WorldTick == world.WorldTick && item.Kind == "inhabitant_moved" && item.Detail.StartsWith(actor, StringComparison.Ordinal)));
        Assert.Equal(new InventoryGroundPosition(world.Animals.Single().Position.X, world.Animals.Single().Position.Y), world.Society.Inventory.GetLot("saddle").GroundPosition);
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
        inventory = InventoryFixture.AddLot(inventory, "supply-water", "fresh_water", home, 4, containerLotId: "supply-jug");
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
        {
            Inhabitants = At(state, seller, yard.Position, inventory, [animal]).Inhabitants.Select(person => person.InhabitantId == buyer ?
            person with { Position = new(yard.Position.X + 1, yard.Position.Y) } : person).ToArray()
        };
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

    [Theory]
    [InlineData("house", "cooked_eggs", 2, "eggs:2,wood:1")]
    [InlineData("house", "milk_porridge", 2, "grain:1,milk:1,wood:1")]
    [InlineData("restaurant", "rich_meal", 2, "bread:1,eggs:1,cultivated_greens:1,wood:1")]
    [InlineData("tailor", "padded_coat", 1, "wool:2,cloth:2")]
    [InlineData("tailor", "leather", 2, "hide:1,fresh_water:1,wood:1")]
    [InlineData("tailor", "leather_sack", 1, "leather:2,rope:1")]
    [InlineData("tailor", "saddle", 1, "leather:2,cloth:2,rope:1")]
    public async Task AnimalRecipesUseDeliveredIngredientsAndKeepTheirReusableJugAcrossReload(string station, string product,
        int quantity, string materials)
    {
        var (state, actor, home, yard) = CreateYard("animal-recipe-" + product);
        var building = state.WorldSimulation!.Buildings.FirstOrDefault(building => building.HouseholdId == home &&
            state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains(station)));
        if (building is null)
        {
            var definition = state.WorldContent!.Buildings.Where(definition => definition.Tags.Contains(station))
                .OrderBy(definition => definition.Width * definition.Height).First();
            var supplied = state.Society.Society.Inventory;
            foreach (var cost in definition.BuildCosts)
                supplied = InventoryFixture.AddLot(supplied, "station-cost-" + cost.ResourceId, cost.ResourceId, home, cost.Amount,
                    groundPosition: new(yard.Position.X, yard.Position.Y));
            using var setup = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = state.Society.Society with { Inventory = supplied } } }, _ => new AnimalChooser());
            foreach (var tile in state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, yard.Position)))
                if (setup.PlaceBuilding("animal-workstation", definition.CanonicalId, tile.Position, home).Applied) break;
            building = Assert.Single(setup.WorldSimulation.Buildings, item => item.InstanceId == "animal-workstation");
            state = setup.ExportState();
        }
        var recipe = state.WorldContent!.Recipes.Single(recipe => recipe.WorkstationBuildingId == building.DefinitionId &&
            recipe.Tags.Contains("animal-product") && recipe.Outputs.Any(output => output.ResourceId == product));
        var inventory = state.Society.Society.Inventory;
        if (materials.Contains("milk", StringComparison.Ordinal) || materials.Contains("fresh_water", StringComparison.Ordinal))
            inventory = InventoryFixture.AddLot(inventory, "recipe-jug", "water_jug", home, 1, storageBuildingId: building.InstanceId);
        foreach (var input in materials.Split(','))
        {
            var fields = input.Split(':');
            inventory = InventoryFixture.AddLot(inventory, "recipe-input-" + fields[0], fields[0], home, int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture),
                storageBuildingId: building.InstanceId, containerLotId: fields[0] is "milk" or "fresh_water" ? "recipe-jug" : null);
        }
        using var world = PrivateWorldRuntime.Restore(At(state, actor, building.Position, inventory, []), _ => new AnimalChooser());
        var started = world.StartProduction(recipe.CanonicalId, building.InstanceId, actor);
        Assert.True(started.Applied, started.Failure);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new AnimalChooser());
        await Until(reload, () => reload.WorldSimulation.ProductionJobs.Single(job => job.JobId == started.JobId).State == WorldProductionJobState.Completed);
        var output = Assert.Single(reload.Society.Inventory.Lots, lot => lot.Id.StartsWith(started.JobId + ":output:", StringComparison.Ordinal));
        Assert.Equal((product, quantity, home, building.InstanceId), (output.ItemKind, output.Quantity, output.OwnerId, output.StorageBuildingId));
        Assert.All(reload.WorldSimulation.ProductionJobs.Single(job => job.JobId == started.JobId).InputReservationIds,
            id => Assert.Equal(InventoryReservationState.Completed, reload.Society.Inventory.GetReservation(id).State));
        if (inventory.Lots.Any(lot => lot.Id == "recipe-jug"))
        {
            Assert.Equal("water_jug", reload.Society.Inventory.GetLot("recipe-jug").ItemKind);
            Assert.DoesNotContain(reload.Society.Inventory.Lots, lot => lot.ContainerLotId == "recipe-jug");
        }
        if (product == "leather_sack")
        {
            var personal = InventoryFixture.Transfer(reload.Society.Inventory, "equip-sack", home, actor, output.Id, 1, "equipment");
            var sack = personal.Lots.Single(lot => lot.OwnerId == actor && lot.ItemKind == "leather_sack");
            Assert.Equal(32, PersonalEquipmentRules.Capacity(personal, actor, new(CarryAidLotId: sack.Id)));
        }
        reload.Validate();
    }

    [Fact]
    public async Task TamingSpendsExactPersonalSuppliesThenLeadingMovesTheAnimalHomeWithoutTeleporting()
    {
        var (state, actor, home, yard) = CreateYard("animal-taming");
        var day = state.WorldSystems!.Config.TicksPerDay;
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            state.Map.FootDistance(point, yard.Position) == 3 && !state.Inhabitants.Any(person => person.Position == point));
        var animal = new AnimalState("wild", "Moss", "chicken", "female", -3L * day, site, "wild-herd",
            WildFedUntilTick: day, WildWaterUntilTick: day, CareUntilTick: day);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "tame-feed", "grain", actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "tame-jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "tame-water", "fresh_water", actor, 1, containerLotId: "tame-jug");
        using var world = PrivateWorldRuntime.Restore(At(state, actor, site, inventory, [animal]), id => id == actor ? new AnimalChooser("animal_order") : new AnimalChooser());
        world.SubmitInstruction(new("tame", "owner", actor, OwnerInstructionKind.MustDo, "tame Moss"));
        await Until(world, () => world.Animals.Single().HouseholdId == home);
        Assert.Equal(site, world.Animals.Single().Position);
        Assert.Equal(actor, world.Animals.Single().LeaderId);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id is "tame-feed" or "tame-water");
        world.SubmitInstruction(new("lead", "owner", actor, OwnerInstructionKind.MustDo, "lead home Moss"));
        await Until(world, () => world.Animals.Single().LeaderId is null);
        var position = world.Animals.Single().Position;
        Assert.Equal(position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.InRange(position.X - yard.Position.X, 0, 1);
        Assert.InRange(position.Y - yard.Position.Y, 0, 1);
        world.Validate();
    }

    [Fact]
    public async Task MilkIsHauledAsAWholeHouseholdJugAndAnAgreedSalePoursOnlyOnePortionIntoTheBuyersJug()
    {
        var (state, seller, home, yard) = CreateYard("animal-milk-sale");
        var definition = state.WorldContent!.Buildings.Single(definition => definition.LocalId == "store-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "milk-store-cost-" + cost.ResourceId, cost.ResourceId, home, cost.Amount, groundPosition: new(yard.Position.X, yard.Position.Y));
        using var setup = PrivateWorldRuntime.Restore(At(state, seller, yard.Position, inventory, []), _ => new AnimalChooser());
        foreach (var tile in state.Map.Tiles.OrderBy(tile => state.Map.FootDistance(tile.Position, yard.Position)))
            if (setup.PlaceBuilding("milk-store", definition.CanonicalId, tile.Position, home).Applied) break;
        var store = Assert.Single(setup.WorldSimulation.Buildings, building => building.InstanceId == "milk-store");
        state = setup.ExportState();
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "seller-jug", "water_jug", home, 1, groundPosition: new(yard.Position.X, yard.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "sale-milk", "milk", home, 2, containerLotId: "seller-jug");
        using var haul = PrivateWorldRuntime.Restore(At(state, seller, yard.Position, inventory, []), id => id == seller ?
            new AnimalChooser("animal:milk_stock:") : new AnimalChooser());
        await Until(haul, () => haul.Society.Inventory.GetLot("seller-jug").StorageBuildingId == store.InstanceId);
        Assert.Equal((home, store.InstanceId, 2), (haul.Society.Inventory.GetLot("sale-milk").OwnerId,
            haul.Society.Inventory.GetLot("sale-milk").StorageBuildingId, haul.Society.Inventory.GetLot("sale-milk").Quantity));
        state = haul.ExportState();
        var buyer = state.Society.Society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand == SocietyAgeBand.Adult && person.HouseholdId != home).Id;
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "buyer-jug", "water_jug", buyer, 1);
        inventory = InventoryFixture.AddLot(inventory, "milk-payment", "wood", buyer, 1);
        state = At(state, seller, store.Position, inventory, []) with
        {
            Inhabitants = At(state, seller, store.Position, inventory, []).Inhabitants
            .Select(person => person.InhabitantId == buyer ? person with { Position = new(store.Position.X + 1, store.Position.Y) } : person).ToArray()
        };
        var chooser = new AnimalChooser("animal:milk_offer:", DecisionProviderKind.LargeLanguageModel);
        using var offering = PrivateWorldRuntime.Restore(state, id => id == seller ? chooser : new AnimalChooser());
        await Until(offering, () => offering.ExportState().AnimalWorld.MilkOffers.Count == 1);
        Assert.Equal(1, offering.Society.Inventory.GetLot("milk-payment").Quantity);
        var bytes = PrivateWorldRuntimeCodec.Encode(offering.ExportState());
        Assert.False((await offering.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(offering.ExportState()));
        using var accepting = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), id => id == buyer ?
            new AnimalChooser("animal:milk_accept:", DecisionProviderKind.LargeLanguageModel) : new AnimalChooser());
        accepting.SubmitInstruction(new("notice-milk", "owner", buyer, OwnerInstructionKind.Suggestive, "Consider the offered milk."));
        await Until(accepting, () => accepting.ExportState().AnimalWorld.MilkOffers.Count == 0);
        Assert.Equal(home, accepting.Society.Inventory.GetLot("seller-jug").OwnerId);
        Assert.Equal(buyer, accepting.Society.Inventory.GetLot("buyer-jug").OwnerId);
        Assert.Equal(1, accepting.Society.Inventory.GetLot("sale-milk").Quantity);
        var purchased = Assert.Single(accepting.Society.Inventory.Lots, lot => lot.ContainerLotId == "buyer-jug");
        Assert.Equal((buyer, "milk", 1), (purchased.OwnerId, purchased.ItemKind, purchased.Quantity));
        var payment = Assert.Single(accepting.Society.Inventory.Lots, lot => lot.Id.StartsWith("milk-payment", StringComparison.Ordinal));
        Assert.Equal((home, new InventoryGroundPosition(store.Position.X, store.Position.Y)), (payment.OwnerId, payment.GroundPosition));
        accepting.Validate();
    }

    [Fact]
    public async Task OrdinaryPaidOrdersBuildAndExpandTheHouseholdsAnimalYard()
    {
        using var generated = NormalPathWorld.CreateGenerated("animal-native-yard", _ => new AnimalChooser());
        var state = generated.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var home = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var position = state.Inhabitants[0].Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "native-yard-wood", "wood", home, 16,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "native-yard-rope", "rope", home, 4, groundPosition: new(position.X, position.Y));
        var chooser = new AnimalChooser();
        var yardDefinition = AnimalContent.Yard();
        var expandedDefinition = BuildingStorageRules.WithSize(yardDefinition, 2, 4);
        var town = state.Towns!.Single(town => town.ResidentIds.Contains(actor));
        var occupied = state.RoadTiles!.Concat(state.Fields!.Select(field => field.Position))
            .Concat(state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position))
            .Concat(state.HouseholdLandUseRights!.Where(right => right.HouseholdId != home).SelectMany(right => right.Tiles))
            .Concat(state.HouseholdLandUseRequests!.Where(request => request.HouseholdId != home && request.Status == "pending")
                .SelectMany(TownLandRightsRules.UnresolvedRequestTiles))
            .Concat(state.TownLandTitles!.Where(title => title.TownId != town.Id).SelectMany(title => title.Tiles))
            .Concat(state.Towns!.SelectMany(item => item.Projects).Where(project => project.Stage is not ("completed" or "cancelled"))
                .SelectMany(project => TownProjectRules.Footprint(project.Plan).Append(project.Plan.Entrance)))
            .ToHashSet();
        // Reserve an expandable legal plot before asking the ordinary order to build.
        var site = Enumerable.Range(0, state.Map.Height).SelectMany(y => Enumerable.Range(0, state.Map.Width).Select(x => new GridPoint(x, y)))
            .OrderBy(point => Math.Abs(point.X - position.X) + Math.Abs(point.Y - position.Y)).First(point =>
                TownBorderRules.IsWithinOrAdjacent(town, point, 2, 4) &&
                WorldContentSimulationRules.Fits(state.Map, state.WorldSimulation!.Buildings.Select(building =>
                    (building, state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId))), expandedDefinition, point) &&
                WorldContentSimulationRules.Footprint(expandedDefinition, point).All(tile => !occupied.Contains(tile)));
        state = ExpansionLandFixture.WithRights(state, new("future-yard", yardDefinition.CanonicalId, site, 0, town.Id, home),
            WorldContentSimulationRules.Footprint(expandedDefinition, site));
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id is "native-yard-wood" or "native-yard-rope" ?
            lot with { GroundPosition = new(site.X, site.Y) } : lot).ToArray()
        };
        chooser.Prefix = "construct_building";
        using var world = PrivateWorldRuntime.Restore(At(state, actor, site, inventory, []), id => id == actor ? chooser : new AnimalChooser());
        world.SubmitInstruction(new("build-yard", "owner", actor, OwnerInstructionKind.MustDo,
            System.FormattableString.Invariant($"build an animal yard at ({site.X}, {site.Y})")));
        await Until(world, () => world.WorldSimulation.Buildings.Any(building => world.WorldContent.Buildings.Any(definition =>
            definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("animal-yard"))));
        var yard = world.WorldSimulation.Buildings.Single(building => world.WorldContent.Buildings.Any(definition =>
            definition.CanonicalId == building.DefinitionId && definition.Tags.Contains("animal-yard")));
        Assert.Equal(home, yard.HouseholdId);
        Assert.Equal("finished", world.ExportState().Instructions!.Single().Order!.Status);
        chooser.Prefix = "expand_building";
        world.SubmitInstruction(new("expand-yard", "owner", actor, OwnerInstructionKind.MustDo, "expand my animal yard"));
        await Until(world, () => world.WorldSimulation.Buildings.Single(building => building.InstanceId == yard.InstanceId).Footprint?.Revision == 1);
        var expanded = world.WorldSimulation.Buildings.Single(building => building.InstanceId == yard.InstanceId);
        var definition = BuildingStorageRules.EffectiveDefinition(world.WorldContent.Buildings.Single(definition => definition.CanonicalId == expanded.DefinitionId), expanded);
        Assert.Equal(8, definition.Width * definition.Height);
        Assert.Equal(16, BuildingStorageRules.Capacity(definition, expanded));
        world.Validate();
    }

    [Fact]
    public async Task SpoiledMilkIsEmptiedLocallyWithoutLosingTheJugOrFreshMilkAcrossRollbackAndReload()
    {
        var (state, actor, home, yard) = CreateYard("animal-spoiled-milk");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "reuse-jug", "water_jug", home, 1);
        inventory = InventoryFixture.AddLot(inventory, "spoiled-milk", "milk", home, 2, containerLotId: "reuse-jug");
        inventory = InventoryFixture.AddLot(inventory, "fresh-milk", "milk", home, 1, containerLotId: "reuse-jug");
        inventory = InventoryFixture.Relocate(inventory, "spoil-fixture-carry", "reuse-jug", home, 1, carrierId: actor);
        inventory = inventory with { Lots = inventory.Lots.Select(lot => lot.Id == "spoiled-milk" ? lot with { FreshnessBasisPoints = 0 } : lot).ToArray() };
        using var world = PrivateWorldRuntime.Restore(At(state, actor, yard.Position, inventory, []), id => id == actor ?
            new AnimalChooser("animal:empty_milk:") : new AnimalChooser());
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), id => id == actor ?
            new AnimalChooser("animal:empty_milk:") : new AnimalChooser());
        await Until(replay, () => !replay.Society.Inventory.Lots.Any(lot => lot.Id == "spoiled-milk"));
        Assert.Equal((home, 1, actor), (replay.Society.Inventory.GetLot("reuse-jug").OwnerId,
            replay.Society.Inventory.GetLot("fresh-milk").Quantity, replay.Society.Inventory.GetLot("fresh-milk").CarrierId));
        replay.Validate();
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
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? position : person.Position,
                HungerBasisPoints = 9500,
                LastDecisionContext = null,
                Project = null
            }).ToArray(),
        };

    private static async Task Until(PrivateWorldRuntime world, Func<bool> reached)
    {
        for (var tick = 0; tick < 80 && !reached(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(reached(), "The legal animal action did not complete within 80 ticks. " +
            System.Text.Json.JsonSerializer.Serialize(new
            {
                world.Animals,
                Instructions = world.ExportState().Instructions,
                Actors = world.Inhabitants.Select(person => new { person.InhabitantId, person.Position }),
                Events = world.ExportState().Events.TakeLast(12)
            }));
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
