using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class OrnamentPersonalUseTests
{
    private const string Alpha = "household:camp-alpha";
    private const string House = "first-town-house-a";
    private const string Ornament = "personal-use-ornament";
    private static readonly Lazy<byte[]> Generated = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("ornament-personal-use", _ => new OrnamentChoices());
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Fact]
    public async Task APersonalChoiceWalksToHeldStockAndWearsOneRealUnitAcrossReplay()
    {
        var state = Prepared();
        var actor = Actor(state);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Ornament, "gold_ornament", Alpha, 2,
            storageBuildingId: House);
        state = WithInventory(state, inventory);
        var provider = new OrnamentChoices("wear_ornament:", DecisionProviderKind.LargeLanguageModel);
        using var world = Restore(state, actor, provider);
        Assert.NotEqual(house.Position, Physical(world, actor).Position);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.WearOrnament(actor, Ornament).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var startingLoad = PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, Physical(world, actor).Equipment);
        for (var tick = 0; tick < 121 && Physical(world, actor).Position == state.Inhabitants.Single(person => person.InhabitantId == actor).Position; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(provider.Offered, candidate => candidate.Id.StartsWith("wear_ornament:", StringComparison.Ordinal));
        Assert.Null(Physical(world, actor).Equipment?.OrnamentLotId);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        Assert.Equal((Alpha, House, 2), (world.Society.Inventory.GetLot(Ornament).OwnerId,
            world.Society.Inventory.GetLot(Ornament).StorageBuildingId, world.Society.Inventory.GetLot(Ornament).Quantity));

        var transit = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(transit), actor,
            new OrnamentChoices("wear_ornament:", DecisionProviderKind.LargeLanguageModel));
        // The walk continues between model turns; putting the ornament on still waits for a fresh choice.
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(house.Position, Physical(world, actor).Position);
        Assert.Null(Physical(world, actor).Equipment?.OrnamentLotId);
        for (var tick = 0; tick < 181 && Physical(world, actor).Equipment?.OrnamentLotId is null; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        }
        var selectedId = Assert.IsType<string>(Physical(world, actor).Equipment?.OrnamentLotId);
        var selected = world.Society.Inventory.GetLot(selectedId);
        Assert.Equal((actor, 1, Ornament, 10_000, (string?)null),
            (selected.OwnerId, selected.Quantity, selected.ProvenanceLotId, selected.ConditionBasisPoints, selected.StorageBuildingId));
        Assert.Equal(house.Position, Physical(world, actor).Position);
        Assert.Equal((Alpha, House, 1), (world.Society.Inventory.GetLot(Ornament).OwnerId,
            world.Society.Inventory.GetLot(Ornament).StorageBuildingId, world.Society.Inventory.GetLot(Ornament).Quantity));
        Assert.Equal(startingLoad + 1, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, Physical(world, actor).Equipment));
        Assert.Single(world.ExportState().Events, item => item.Kind == "ornament_worn");
        var selectedInventory = world.Society.Inventory;
        Assert.True(world.RemoveOrnament(actor).Applied);
        Assert.True(resumed.RemoveOrnament(actor).Applied);
        Assert.Null(Physical(world, actor).Equipment?.OrnamentLotId);
        Assert.Equal(selectedInventory, world.Society.Inventory);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        Assert.True(world.WearOrnament(actor, selectedId).Applied);
        Assert.Equal(selectedId, Physical(world, actor).Equipment!.OrnamentLotId);
        Assert.Equal(selected, world.Society.Inventory.GetLot(selectedId));
        world.Validate();
    }

    [Fact]
    public async Task AWornOrnamentIsNeverPutAwayAsPersonalStorage()
    {
        var state = Prepared(local: true);
        var actor = Actor(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Ornament, "gold_ornament", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "ornament-storage-control", "cloth", actor, 1);
        var choices = new OrnamentChoices("household_store_personal:", DecisionProviderKind.Deterministic);
        using var world = Restore(WithInventory(state, inventory), actor, choices);
        Assert.True(world.WearOrnament(actor, Ornament).Applied);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        // The unworn control item may be stored; the worn ornament stays with the adult, so the save still loads.
        Assert.Contains(choices.Offered, candidate => candidate.Id == "household_store_personal:ornament-storage-control");
        Assert.DoesNotContain(choices.Offered, candidate => candidate.Id == "household_store_personal:" + Ornament);
        Assert.Equal(Ornament, Physical(world, actor).Equipment!.OrnamentLotId);
        Assert.Null(world.Society.Inventory.GetLot(Ornament).StorageBuildingId);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Fact]
    public async Task ABorrowedOrnamentCanBeNeitherWornNorGivenByItsCarrier()
    {
        // Carrying is custody, not ownership, for example goods kept after leaving a household.
        var state = Prepared(local: true);
        var actor = Actor(state);
        var owner = Recipient(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Ornament, "gold_ornament", owner, 1);
        inventory = InventoryFixture.AddLot(inventory, "own-ornament-control", "gold_ornament", actor, 1);
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == Ornament ? lot with { CarrierId = actor } : lot).ToArray(),
        };
        var choices = new OrnamentChoices("safe_idle", DecisionProviderKind.LargeLanguageModel);
        using var world = Restore(WithInventory(state, inventory), actor, choices);
        Assert.False(world.WearOrnament(actor, Ornament).Applied);
        // Giving it used to throw inside the tick and stall the world.
        Assert.False(world.GiveOrnament(actor, owner, Ornament).Applied);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(choices.Offered, candidate => candidate.Id == ChoiceId("gift_ornament:", "own-ornament-control", owner));
        Assert.DoesNotContain(choices.Offered, candidate => candidate.Id == ChoiceId("wear_ornament:", Ornament) ||
            candidate.Id == ChoiceId("gift_ornament:", Ornament, owner));
        Assert.Equal((owner, actor), (world.Society.Inventory.GetLot(Ornament).OwnerId, world.Society.Inventory.GetLot(Ornament).CarrierId));
        Assert.Null(Physical(world, actor).Equipment?.OrnamentLotId);
        world.Validate();
    }

    [Theory]
    [InlineData("foreign-household")]
    [InlineData("full-hands")]
    [InlineData("reserved-stock")]
    public async Task WearingCannotTakeForeignReservedOrOverCapacityHouseholdStock(string boundary)
    {
        var state = Prepared(local: true);
        var actor = Actor(state);
        var foreign = boundary == "foreign-household";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Ornament, "gold_ornament",
            foreign ? "household:camp-beta" : Alpha, 1,
            storageBuildingId: foreign ? "first-town-house-b" : House);
        if (boundary == "full-hands")
        {
            var load = PersonalEquipmentRules.CarriedQuantity(inventory, actor, state.Inhabitants.Single(person => person.InhabitantId == actor).Equipment);
            inventory = InventoryFixture.AddLot(inventory, "wear-full-hands", "stone", actor, 8 - load);
        }
        if (boundary == "reserved-stock")
            inventory = InventoryFixture.Reserve(inventory, "wear-stock-reserved", Alpha, Ornament, 1, "other_household_work", 120);
        var provider = new OrnamentChoices("wear_ornament:", DecisionProviderKind.LargeLanguageModel);
        using var world = Restore(WithInventory(state, inventory), actor, provider);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.WearOrnament(actor, Ornament).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(provider.Offered, candidate => candidate.Id.StartsWith("wear_ornament:", StringComparison.Ordinal));
        var original = inventory.GetLot(Ornament);
        var retained = world.Society.Inventory.GetLot(Ornament);
        Assert.Equal((original.OwnerId, original.ItemKind, original.Quantity, original.StorageBuildingId,
                original.DeliveryBuildingId, original.GroundPosition, original.ProvenanceLotId,
                original.ConditionBasisPoints, original.FreshnessBasisPoints),
            (retained.OwnerId, retained.ItemKind, retained.Quantity, retained.StorageBuildingId,
                retained.DeliveryBuildingId, retained.GroundPosition, retained.ProvenanceLotId,
                retained.ConditionBasisPoints, retained.FreshnessBasisPoints));
        Assert.Null(Physical(world, actor).Equipment?.OrnamentLotId);
        Assert.NotEmpty(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Theory]
    [InlineData("gold_ornament")]
    [InlineData("diamond_ornament")]
    public void AStackSplitsOnlyOneUnreservedUnitAndItsWornGiftKeepsProvenance(string kind)
    {
        var state = Prepared(local: true);
        var actor = Actor(state);
        var recipient = Recipient(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Ornament, kind, actor, 3);
        inventory = InventoryFixture.Reserve(inventory, "ornament-other-two", actor, Ornament, 2, "other_owned_work", 120);
        using var world = Restore(WithInventory(state, inventory));
        var before = PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, Physical(world, actor).Equipment);
        var worn = world.WearOrnament(actor, Ornament);
        Assert.True(worn.Applied);
        var unit = world.Society.Inventory.GetLot(Assert.IsType<string>(worn.LotId));
        Assert.Equal((actor, 1, Ornament), (unit.OwnerId, unit.Quantity, unit.ProvenanceLotId));
        Assert.Equal(2, world.Society.Inventory.GetLot(Ornament).Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("ornament-other-two").State);
        Assert.Equal(before, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, Physical(world, actor).Equipment));
        Assert.Equal(8, PersonalEquipmentRules.Capacity(world.Society.Inventory, actor, Physical(world, actor).Equipment));
        var given = world.GiveOrnament(actor, recipient, unit.Id);
        Assert.True(given.Applied);
        Assert.Equal(unit.Id, given.LotId);
        Assert.Null(Physical(world, actor).Equipment?.OrnamentLotId);
        Assert.Null(Physical(world, recipient).Equipment?.OrnamentLotId);
        Assert.Equal(unit with { OwnerId = recipient }, world.Society.Inventory.GetLot(unit.Id));
        Assert.False(world.GiveOrnament(actor, recipient, Ornament).Applied);
        Assert.Equal(3, world.Society.Inventory.Lots.Where(lot => lot.Id == Ornament || lot.ProvenanceLotId == Ornament).Sum(lot => lot.Quantity));
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("ground")]
    [InlineData("stored")]
    [InlineData("promised")]
    [InlineData("remote")]
    [InlineData("recipient-full")]
    [InlineData("broken")]
    public async Task GiftsRefuseUnavailableUncarriedRemoteOrFullRecipientsWithoutChangingProperty(string boundary)
    {
        var state = Prepared(local: boundary != "remote");
        var actor = Actor(state);
        var recipient = Recipient(state);
        if (boundary == "remote")
        {
            var location = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
            var remote = state.Map.Tiles.First(tile => state.Map.IsPassable(tile.Position) &&
                state.Map.FootDistance(location, tile.Position) >= 4 &&
                state.Inhabitants.All(person => person.Position != tile.Position) &&
                state.Map.Resources.All(resource => resource.Position != tile.Position) &&
                state.Map.CampObjects.All(item => item.Position != tile.Position)).Position;
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == recipient
                ? person with { Position = remote } : person).ToArray()
            };
        }
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Ornament, "gold_ornament",
            boundary == "foreign" ? recipient : boundary is "stored" or "promised" ? Alpha : actor, 1,
            conditionBasisPoints: boundary == "broken" ? 0 : 10_000,
            storageBuildingId: boundary == "stored" ? House : null,
            groundPosition: boundary == "ground" ? new(state.Inhabitants.Single(person => person.InhabitantId == actor).Position.X,
                state.Inhabitants.Single(person => person.InhabitantId == actor).Position.Y) : null);
        if (boundary == "promised")
            inventory = InventoryFixture.Transfer(inventory, "gift-promised-house-delivery", Alpha, actor,
                Ornament, 1, "collect", destinationDeliveryBuildingId: House);
        if (boundary == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "gift-reserved", actor, Ornament, 1, "other_owned_work", 120);
        if (boundary == "recipient-full")
        {
            var physical = state.Inhabitants.Single(person => person.InhabitantId == recipient);
            var load = PersonalEquipmentRules.CarriedQuantity(inventory, recipient, physical.Equipment);
            inventory = InventoryFixture.AddLot(inventory, "gift-full-recipient", "stone", recipient, 8 - load);
        }
        var provider = new OrnamentChoices("gift_ornament:", DecisionProviderKind.LargeLanguageModel, destination: recipient);
        using var world = Restore(WithInventory(state, inventory), actor, provider);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.GiveOrnament(actor, recipient, Ornament).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(provider.Offered, candidate => candidate.Id.StartsWith("gift_ornament:", StringComparison.Ordinal) &&
            candidate.DestinationId == recipient);
        Assert.Equal(inventory.GetLot(Ornament).OwnerId, world.Society.Inventory.GetLot(Ornament).OwnerId);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "ornament_given");
        Assert.NotEmpty(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Theory]
    [InlineData(DecisionProviderKind.Deterministic, false, false)]
    [InlineData(DecisionProviderKind.Jev, false, false)]
    [InlineData(DecisionProviderKind.LargeLanguageModel, true, false)]
    [InlineData(DecisionProviderKind.LargeLanguageModel, false, true)]
    [InlineData(DecisionProviderKind.LargeLanguageModel, false, false)]
    public async Task AnAutonomousGiftRequiresOneFreshExactPersonalChoiceAndCannotBeForcedOrReplayed(
        DecisionProviderKind kind, bool fail, bool mustDo)
    {
        var state = Prepared(local: true);
        var actor = Actor(state);
        var recipient = Recipient(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, Ornament, "diamond_ornament", actor, 1);
        if (mustDo)
        {
            inventory = InventoryFixture.AddLot(inventory, "ornament-forced-berry", "berries", actor, 1);
            // Below comfortable fullness, so the eat order runs instead of waiting.
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                    ? person with { HungerBasisPoints = 3_000 } : person).ToArray(),
            };
        }
        var provider = new OrnamentChoices("gift_ornament:", kind, fail, recipient);
        using var world = Restore(WithInventory(state, inventory), actor, provider);
        Assert.True(world.WearOrnament(actor, Ornament).Applied);
        OwnerInstructionReceipt? instruction = null;
        if (mustDo)
            instruction = world.SubmitInstruction(new("ornament-owner-food", "owner:test", actor, OwnerInstructionKind.MustDo, "eat food"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        // An active order narrows the choices to the order, so a gift cannot be chosen under one.
        if (mustDo) Assert.DoesNotContain(provider.Offered, candidate => candidate.Id.StartsWith("gift_ornament:", StringComparison.Ordinal));
        else Assert.Contains(provider.Offered, candidate => candidate.Id.StartsWith("gift_ornament:", StringComparison.Ordinal) && candidate.DestinationId == recipient);
        var applied = kind == DecisionProviderKind.LargeLanguageModel && !fail && !mustDo;
        Assert.Equal(applied ? recipient : actor, world.Society.Inventory.GetLot(Ornament).OwnerId);
        Assert.Equal(applied ? null : Ornament, Physical(world, actor).Equipment?.OrnamentLotId);
        Assert.Equal(applied ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "ornament_given"));
        if (instruction is not null)
        {
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "ornament-forced-berry");
            Assert.Contains(instruction.InstructionId, world.ExportState().CompletedInstructionIds!);
        }
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 3; tick++) Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(applied ? 1 : 0, replay.ExportState().Events.Count(item => item.Kind == "ornament_given"));
        Assert.Equal(applied ? recipient : actor, replay.Society.Inventory.GetLot(Ornament).OwnerId);
    }

    [Theory]
    [InlineData("wear_ornament:", DecisionProviderKind.Deterministic)]
    [InlineData("wear_ornament:", DecisionProviderKind.Jev)]
    [InlineData("wear_ornament:", DecisionProviderKind.LargeLanguageModel)]
    [InlineData("remove_ornament", DecisionProviderKind.Deterministic)]
    [InlineData("remove_ornament", DecisionProviderKind.Jev)]
    [InlineData("remove_ornament", DecisionProviderKind.LargeLanguageModel)]
    public async Task WearingAndRemovingArePersonalChoicesRatherThanRoutineFallbacks(string action, DecisionProviderKind kind)
    {
        var state = Prepared(local: true);
        var actor = Actor(state);
        var provider = new OrnamentChoices(action, kind);
        using var world = Restore(WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            Ornament, "gold_ornament", actor, 1)), actor, provider);
        var removing = action == "remove_ornament";
        if (removing) Assert.True(world.WearOrnament(actor, Ornament).Applied);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(provider.Offered, candidate => candidate.Id.StartsWith(action, StringComparison.Ordinal));
        var worn = removing ? kind != DecisionProviderKind.LargeLanguageModel : kind == DecisionProviderKind.LargeLanguageModel;
        Assert.Equal(worn ? Ornament : null, Physical(world, actor).Equipment?.OrnamentLotId);
        Assert.Equal((actor, 1), (world.Society.Inventory.GetLot(Ornament).OwnerId, world.Society.Inventory.GetLot(Ornament).Quantity));
        Assert.NotEmpty(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task APersonalOrnamentChoiceCannotInterruptActualPaidFieldWorkAcrossReload()
    {
        var (state, actor, _, point) = FarmFieldTests.PreparedFarmer("ornament-during-field-work");
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            Ornament, "gold_ornament", actor, 1));
        var provider = new OrnamentChoices("wear_ornament:", DecisionProviderKind.LargeLanguageModel);
        using var world = Restore(state, actor, provider);
        Assert.True(world.StartFieldWork(actor, point, FarmWorkKind.Till).Accepted);
        var work = Assert.Single(world.Fields).Work!;
        Assert.Equal((actor, FarmWorkKind.Till, 8), (work.WorkerId, work.Kind, work.RemainingTicks));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(before), actor,
            new OrnamentChoices("wear_ornament:", DecisionProviderKind.LargeLanguageModel));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        var continuing = Assert.Single(world.Fields).Work!;
        // The current provisional wooden hoe completes two work units and wears by 1,000 per stroke.
        Assert.Equal((actor, FarmWorkKind.Till, 6), (continuing.WorkerId, continuing.Kind, continuing.RemainingTicks));
        Assert.Equal(9_000, world.Society.Inventory.GetLot("carried-hoe").ConditionBasisPoints);
        Assert.Equal(point, Physical(world, actor).Position);
        Assert.Null(Physical(world, actor).Equipment?.OrnamentLotId);
        Assert.Equal((actor, 1), (world.Society.Inventory.GetLot(Ornament).OwnerId,
            world.Society.Inventory.GetLot(Ornament).Quantity));
        Assert.DoesNotContain(provider.Offered, candidate => candidate.Id.StartsWith("wear_ornament:", StringComparison.Ordinal));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind is "ornament_worn" or "field_work_interrupted");
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("foreign")]
    [InlineData("stack")]
    [InlineData("ground")]
    [InlineData("stored")]
    [InlineData("promised")]
    [InlineData("reserved")]
    [InlineData("wrong-kind")]
    public void MalformedSelectedReferencesAreRefused(string boundary)
    {
        var state = Prepared(local: true);
        var actor = Actor(state);
        var recipient = Recipient(state);
        using var worn = Restore(WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            Ornament, "gold_ornament", actor, 1)));
        Assert.True(worn.WearOrnament(actor, Ornament).Applied);
        state = worn.ExportState();
        var inventory = state.Society.Society.Inventory;
        if (boundary == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "worn-malformed-reservation", actor, Ornament, 1, "other_owned_work", 120);
        else
            inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => lot.Id != Ornament ? lot : boundary switch
                {
                    "foreign" => lot with { OwnerId = recipient },
                    "stack" => lot with { Quantity = 2 },
                    "ground" => lot with { GroundPosition = new(Physical(worn, actor).Position.X, Physical(worn, actor).Position.Y) },
                    "stored" => lot with { OwnerId = Alpha, StorageBuildingId = House },
                    "promised" => lot with { DeliveryBuildingId = House },
                    "wrong-kind" => lot with { ItemKind = "wood" },
                    _ => lot,
                }).ToArray()
            };
        state = WithInventory(state, inventory);
        if (boundary == "unknown") state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Equipment = person.Equipment! with { OrnamentLotId = "missing-ornament" } } : person).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state));
        Assert.Throws<InvalidDataException>(() => Restore(state));
    }

    [Fact]
    public async Task NaturalDeathKeepsTheExactOrnamentInTheEstateAndClearsTheWornReference()
    {
        var state = Prepared(local: true);
        var actor = Actor(state);
        using var wearing = Restore(WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            Ornament, "gold_ornament", actor, 1)));
        Assert.True(wearing.WearOrnament(actor, Ornament).Applied);
        state = wearing.ExportState();
        var checkpoint = state.Society.Society;
        var age = checkpoint.Config.DayLifecycle?.ElderStartDay ?? checkpoint.Config.ElderYears;
        var birth = checkpoint.LifeTickAt(checkpoint.WorldTick) - (age + 1L) * checkpoint.Config.TicksPerLifecycleAge + 1;
        checkpoint = checkpoint with
        {
            Config = checkpoint.Config with { BaseNaturalMortalityBasisPoints = 10_000, NaturalMortalitySlopeBasisPoints = 0 },
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == actor ? person with
            {
                BirthTick = checkpoint.LifeClock is null ? birth : person.BirthTick,
                BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                AgeBand = SocietyAgeBand.Elder,
                LastLifecycleYearChecked = age,
            } : person).ToArray(),
        };
        using var world = Restore(state with { Society = state.Society with { Society = checkpoint } });
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyDeathCause.NaturalAge, world.Society.GetInhabitant(actor).DeathCause);
        var estate = Assert.Single(world.Society.Estates, item => item.DeceasedId == actor);
        Assert.Equal((estate.Id, 1, 10_000), (world.Society.Inventory.GetLot(Ornament).OwnerId,
            world.Society.Inventory.GetLot(Ornament).Quantity, world.Society.Inventory.GetLot(Ornament).ConditionBasisPoints));
        var dead = Assert.Single(world.ExportState().DeceasedInhabitants!, item => item.InhabitantId == actor);
        Assert.Null(dead.LastPhysical.Equipment?.OrnamentLotId);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        var malformed = world.ExportState() with
        {
            DeceasedInhabitants = [dead with { LastPhysical = dead.LastPhysical with
            { Equipment = (dead.LastPhysical.Equipment ?? new PersonalEquipment()) with { OrnamentLotId = Ornament } } }]
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(malformed));
    }

    private static PrivateWorldRuntimeState Prepared(bool local = false)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Generated.Value);
        var actor = Actor(state);
        var recipient = Recipient(state);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        var others = state.Inhabitants.Where(person => person.InhabitantId != actor && person.InhabitantId != recipient)
            .Select(person => person.Position).ToHashSet();
        bool Clear(GridPoint point) => state.Map.IsPassable(point) && !others.Contains(point) &&
            state.Map.Resources.All(resource => resource.Position != point) &&
            state.Map.CampObjects.All(item => item.Position != point);
        // Keep the actual two-step stock route unoccupied. Local gift fixtures put
        // the recipient beside the House; walking fixtures keep them off that route.
        var route = (from near in state.Map.FootNeighbors(house.Position)
                     where Clear(near)
                     from far in state.Map.FootNeighbors(near)
                     where state.Map.FootDistance(far, house.Position) >= 2 && Clear(far)
                     select (Near: near, Far: far)).First();
        var recipientPosition = local ? route.Near : state.Map.Tiles.First(tile =>
            Clear(tile.Position) && tile.Position != house.Position && tile.Position != route.Near &&
            tile.Position != route.Far && state.Map.FootDistance(tile.Position, route.Far) >= 3).Position;
        return state with
        {
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? local ? house.Position : route.Far :
                    person.InhabitantId == recipient ? recipientPosition : person.Position,
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
            }).ToArray(),
        };
    }

    private static string ChoiceId(string prefix, params string[] identity) => prefix + Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(identity))));
    private static string Actor(PrivateWorldRuntimeState state) => state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
    private static string Recipient(PrivateWorldRuntimeState state) => state.Society.Society.Inhabitants.First(person => person.HouseholdId != Alpha).Id;
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PlaytestInhabitantState Physical(PrivateWorldRuntime world, string actor) => world.Inhabitants.Single(person => person.InhabitantId == actor);
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string? actor = null, IDecisionProvider? provider = null) =>
        PrivateWorldRuntime.Restore(state, id => id == actor && provider is not null ? provider : new OrnamentChoices());

    private sealed class OrnamentChoices(string prefix = "safe_idle", DecisionProviderKind kind = DecisionProviderKind.Deterministic,
        bool fail = false, string? destination = null, string? observerReply = null) : IDecisionProvider
    {
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 0;
        public List<CognitionCandidate> Offered { get; } = [];
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Offered.AddRange(request.Observation.Candidates);
            if (fail) throw new InvalidOperationException("ornament personal provider unavailable");
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal) &&
                (destination is null || candidate.DestinationId == destination))
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1, new Dictionary<string, double> { [selected.Id] = 1 })
            {
                ObserverReplies = observerReply is null ? null : request.Observation.ObserverGuidance?
                    .Where(message => message.ReplyAllowed)
                    .Select(message => new CognitionObserverReply(message.InstructionId, observerReply)).ToArray(),
            });
        }
    }
}
