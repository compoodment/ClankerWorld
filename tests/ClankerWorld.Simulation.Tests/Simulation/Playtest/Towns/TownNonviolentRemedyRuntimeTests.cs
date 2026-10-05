using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownNonviolentRemedyRuntimeTests
{
    [Fact]
    public async Task PersonalConsentReservesNothingAndOnlyTheActualTransferCompletesTheAgreementOnceAcrossReplay()
    {
        var state = await NonviolentRuntimeFixture.AcceptedRemedyAsync();
        var ledger = state.Towns![0].Nonviolent;
        var agreement = Assert.Single(ledger.Agreements);
        Assert.Equal(agreement.AcceptedTick + 3L * NonviolentRuntimeFixture.Day, agreement.DeadlineTick);
        Assert.Equal(NonviolentRuntimeFixture.Subject, Assert.Single(agreement.Consents).AgentId);
        Assert.Equal(agreement.TermsHash, agreement.Consents[0].TermsHash);
        Assert.Empty(ledger.Effects);
        Assert.Empty(ledger.NativeReceipts);
        Assert.DoesNotContain(state.Society.Society.Inventory.Reservations, reservation => reservation.LotId == NonviolentRuntimeFixture.ReturnLot);
        var total = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        var provider = CompletionProvider();
        using var world = NonviolentRuntimeFixture.Create(state, provider);
        NonviolentRuntimeFixture.Wake(world, NonviolentRuntimeFixture.Subject, "perform-voluntary-return");
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Contains(provider.Selected, selected => selected.Contains("nonviolent_remedy:", StringComparison.Ordinal));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = NonviolentRuntimeFixture.Create(PrivateWorldRuntimeCodec.Decode(before), CompletionProvider());
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }

        var completed = world.Towns[0].Nonviolent;
        Assert.Equal("completed", Assert.Single(completed.Agreements).Status);
        var effect = Assert.Single(completed.Effects);
        var receipt = Assert.Single(completed.NativeReceipts);
        Assert.Equal(receipt.Id, effect.NativeReceiptId);
        Assert.Equal(receipt.Version, effect.NativeReceiptVersion);
        Assert.Equal(1, effect.Quantity);
        Assert.Equal(NonviolentRuntimeFixture.Subject, effect.ActorId);
        Assert.Equal(NonviolentRuntimeFixture.Witness, effect.BeneficiaryId);
        Assert.Equal("return_goods", receipt.Kind);
        Assert.Equal(NonviolentRuntimeFixture.Subject, receipt.PreviousOwnerId);
        Assert.Equal(NonviolentRuntimeFixture.Witness, receipt.ResultOwnerId);
        Assert.Equal(1, world.Society.Inventory.GetLot(NonviolentRuntimeFixture.ReturnLot).Quantity);
        Assert.Equal(NonviolentRuntimeFixture.Witness, world.Society.Inventory.GetLot(receipt.ResultLotId).OwnerId);
        Assert.Equal(total, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Single(world.ExportState().Events, item => item.Kind == "voluntary_goods_returned");
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        NonviolentRuntimeFixture.Strict(world.ExportState());

        // Duplicating a real receipt is not another completed contribution.
        var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(world.ExportState()))!;
        var effects = document["state"]!["towns"]![0]!["nonviolent"]!["effects"]!.AsArray();
        effects.Add(effects[0]!.DeepClone());
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public async Task ReservedGoodsDoNotBecomeAvailableThroughConsentAndTheSameAgreementResumesAfterNativeRelease()
    {
        var state = await NonviolentRuntimeFixture.AcceptedRemedyAsync();
        var inventory = InventoryFixture.Reserve(state.Society.Society.Inventory, "independent-wood-work",
            NonviolentRuntimeFixture.Subject, NonviolentRuntimeFixture.ReturnLot, 2, "independent-work", long.MaxValue);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var provider = CompletionProvider();
        using (var blocked = NonviolentRuntimeFixture.Create(NonviolentRuntimeFixture.Strict(state), provider))
        {
            NonviolentRuntimeFixture.Wake(blocked, NonviolentRuntimeFixture.Subject, "blocked-voluntary-return");
            Assert.True((await blocked.AdvanceOneTickAsync()).Advanced);
            Assert.DoesNotContain(provider.Observations.Where(observation => observation.InhabitantId == NonviolentRuntimeFixture.Subject)
                .SelectMany(observation => observation.Candidates), candidate => candidate.Id.StartsWith("nonviolent_remedy:", StringComparison.Ordinal));
            Assert.Empty(blocked.Towns[0].Nonviolent.Effects);
            Assert.Equal(2, blocked.Society.Inventory.GetLot(NonviolentRuntimeFixture.ReturnLot).Quantity);
            state = NonviolentRuntimeFixture.Strict(blocked.ExportState());
        }
        inventory = InventoryFixture.ReleaseReservation(state.Society.Society.Inventory, "independent-wood-work");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var released = NonviolentRuntimeFixture.Create(state, CompletionProvider());
        NonviolentRuntimeFixture.Wake(released, NonviolentRuntimeFixture.Subject, "released-voluntary-return");
        await NonviolentRuntimeFixture.UntilAsync(released, () => released.Towns[0].Nonviolent.Effects.Count > 0, 5);
        Assert.Single(released.Towns[0].Nonviolent.Agreements);
        Assert.Equal("completed", released.Towns[0].Nonviolent.Agreements[0].Status);
        NonviolentRuntimeFixture.Strict(released.ExportState());
    }

    [Fact]
    public async Task AgreedRepairTargetsTheNamedCoatInsteadOfThePreferredBasketAndSpendsMaterialsOnlyOnCompletion()
    {
        var state = await NonviolentRuntimeFixture.FindingAsync();
        var actor = NonviolentRuntimeFixture.Subject;
        var household = state.Society.Society.Inhabitants.Single(person => person.Id == actor).HouseholdId!;
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "remedy-shop-fiber", "fiber", household, 2, storageBuildingId: "first-town-house-b");
        using (var setup = NonviolentRuntimeFixture.Create(WithInventory(state, inventory), new NonviolentTestProvider()))
        {
            var definition = setup.WorldContent.Buildings.Single(building => building.LocalId == "tailor-shop-1x1");
            var house = setup.WorldSimulation.Buildings.Single(building => building.HouseholdId == household &&
                setup.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId).Tags.Contains("house"));
            var placed = Enumerable.Range(-4, 9).SelectMany(dy => Enumerable.Range(-4, 9)
                .Select(dx => new GridPoint(house.Position.X + dx, house.Position.Y + dy)))
                .Any(point => setup.PlaceBuilding("remedy-tailor", definition.CanonicalId, point, household).Applied);
            Assert.True(placed);
            state = setup.ExportState();
        }
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "remedy-basket", "basket", actor, 1, conditionBasisPoints: 3_000);
        inventory = InventoryFixture.AddLot(inventory, "remedy-coat", "padded_coat", actor, 1, conditionBasisPoints: 3_000);
        inventory = InventoryFixture.AddLot(inventory, "remedy-cloth", "cloth", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "remedy-fiber", "fiber", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "remedy-rope", "rope", actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { Equipment = new(ClothingLotId: "remedy-coat", CarryAidLotId: "remedy-basket") } : person).ToArray(),
        };
        state = await NonviolentRuntimeFixture.AcceptRemedyAsync(state,
            [new("repair_equipment", actor, actor, "padded_coat", 1, "remedy-coat")]);
        var shop = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "remedy-tailor");
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Position = shop.Position } : person).ToArray()
        };
        using var world = NonviolentRuntimeFixture.Create(state, CompletionProvider());
        NonviolentRuntimeFixture.Wake(world, actor, "perform-named-repair");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair is not null, 4);
        var repair = world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.Repair!;
        Assert.Equal("remedy-coat", repair.LotId);
        Assert.Empty(world.Towns[0].Nonviolent.Effects);
        Assert.Equal(1, world.Society.Inventory.GetLot("remedy-cloth").Quantity);
        var checkpoint = NonviolentRuntimeFixture.Strict(world.ExportState());
        using var replay = NonviolentRuntimeFixture.Create(checkpoint, CompletionProvider());
        for (var tick = 0; tick < PersonalEquipmentRules.RepairWorkTicks + 2; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal("completed", Assert.Single(replay.Towns[0].Nonviolent.Agreements).Status);
        Assert.Single(replay.Towns[0].Nonviolent.Effects);
        var receipt = Assert.Single(replay.Towns[0].Nonviolent.NativeReceipts);
        Assert.Equal("remedy-coat", receipt.TargetId);
        Assert.InRange(replay.Society.Inventory.GetLot("remedy-coat").ConditionBasisPoints, 8_900, 9_000);
        Assert.InRange(replay.Society.Inventory.GetLot("remedy-basket").ConditionBasisPoints, 2_900, 3_000);
        Assert.DoesNotContain(replay.Society.Inventory.Lots, lot => lot.Id == "remedy-cloth");
        Assert.Equal(1, replay.Society.Inventory.GetLot("remedy-fiber").Quantity);
        Assert.Equal(1, replay.Society.Inventory.GetLot("remedy-rope").Quantity);
        Assert.All(repair.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Completed, replay.Society.Inventory.GetReservation(id).State));
        NonviolentRuntimeFixture.Strict(replay.ExportState());
    }

    [Fact]
    public async Task TownServiceCreditsOnlyBoundedLoadsActuallyStoredInTheResidentWarehouseAcrossRestart()
    {
        var state = await NonviolentRuntimeFixture.FindingAsync();
        var actor = NonviolentRuntimeFixture.Subject;
        var town = state.Towns![0];
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "service-stone", "stone", actor, 10);
        state = await NonviolentRuntimeFixture.AcceptRemedyAsync(WithInventory(state, inventory),
            [new("public_service_goods", actor, town.Id, "stone", 6, warehouse.InstanceId)]);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Position = warehouse.Position } : person).ToArray()
        };
        using var world = NonviolentRuntimeFixture.Create(state, CompletionProvider());
        NonviolentRuntimeFixture.Wake(world, actor, "perform-town-service");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Effects.Count > 0, 4);
        Assert.Equal(4, Assert.Single(world.Towns[0].Nonviolent.Effects).Quantity);
        Assert.Equal("pending", Assert.Single(world.Towns[0].Nonviolent.Agreements).Status);
        Assert.Equal(6, world.Society.Inventory.GetLot("service-stone").Quantity);
        using var replay = NonviolentRuntimeFixture.Create(NonviolentRuntimeFixture.Strict(world.ExportState()), CompletionProvider());
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal("completed", Assert.Single(replay.Towns[0].Nonviolent.Agreements).Status);
        Assert.Equal(6, replay.Towns[0].Nonviolent.Effects.Sum(effect => effect.Quantity));
        Assert.Equal(2, replay.Towns[0].Nonviolent.NativeReceipts.Count);
        Assert.Equal(4, replay.Society.Inventory.GetLot("service-stone").Quantity);
        Assert.Equal(6, replay.Society.Inventory.Lots.Where(lot => lot.ItemKind == "stone" &&
            lot.OwnerId == town.Id && lot.StorageBuildingId == warehouse.InstanceId).Sum(lot => lot.Quantity));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        NonviolentRuntimeFixture.Strict(replay.ExportState());
    }

    [Fact]
    public async Task ConsentCannotSpendTheSameRemainingStockTwiceAcrossIndividuallyFeasibleTerms()
    {
        var state = await NonviolentRuntimeFixture.FindingAsync();
        var actor = NonviolentRuntimeFixture.Subject;
        state = await NonviolentRuntimeFixture.OfferRemedyAsync(state,
            [new("return_goods", actor, NonviolentRuntimeFixture.Witness, "wood", 1, NonviolentRuntimeFixture.ReturnLot),
                new("return_goods", actor, NonviolentRuntimeFixture.Judge, "wood", 1, NonviolentRuntimeFixture.ReturnLot)]);
        var inventory = InventoryFixture.Reserve(state.Society.Society.Inventory, "other-native-work", actor,
            NonviolentRuntimeFixture.ReturnLot, 1, "independent-work", long.MaxValue);
        inventory = InventoryFixture.AddLot(inventory, "unrelated-spare-wood", "wood", actor, 1);
        var provider = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == actor
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|remedy_accept|", StringComparison.Ordinal)) : null,
        };
        using var world = NonviolentRuntimeFixture.Create(WithInventory(state, inventory), provider);
        NonviolentRuntimeFixture.Wake(world, actor, "consider-changed-stock");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var observation = Assert.Single(provider.Observations, item => item.InhabitantId == actor);
        Assert.DoesNotContain(observation.Candidates, candidate => candidate.Id.Contains("|remedy_accept|", StringComparison.Ordinal));
        Assert.Contains(observation.Candidates, candidate => candidate.Id.Contains("|remedy_decline|", StringComparison.Ordinal));
        Assert.Empty(world.Towns[0].Nonviolent.Agreements);
        Assert.Empty(world.Towns[0].Nonviolent.Effects);
        Assert.Equal(2, world.Society.Inventory.GetLot(NonviolentRuntimeFixture.ReturnLot).Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("unrelated-spare-wood").Quantity);
        NonviolentRuntimeFixture.Strict(world.ExportState());
    }

    [Theory]
    [InlineData(10, "0.5 world days")]
    [InlineData(20, "1 world day")]
    public async Task ThePublishedOfferAndPersonalChoicesDiscloseTheExactCompletionPeriodBeforeConsent(long completionTicks, string period)
    {
        var state = await NonviolentRuntimeFixture.OfferRemedyAsync(await NonviolentRuntimeFixture.FindingAsync(),
            [new("return_goods", NonviolentRuntimeFixture.Subject, NonviolentRuntimeFixture.Witness, "wood", 1, NonviolentRuntimeFixture.ReturnLot)],
            completionTicks);
        var town = state.Towns![0];
        var offer = Assert.Single(town.Nonviolent.Offers);
        var timing = "Complete within " + period + " after everyone accepts. Answer within 1 world day of publication.";
        Assert.Contains(timing, town.Governance!.Notices.Single(notice => notice.Id == offer.NoticeId).Text, StringComparison.Ordinal);
        state = state with
        {
            Towns = [town with { Governance = town.Governance with
        { Knowledge = town.Governance.Knowledge.Where(receipt => receipt.AgentId != NonviolentRuntimeFixture.Subject || receipt.NoticeId != offer.NoticeId).ToArray() } }]
        };
        var provider = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == NonviolentRuntimeFixture.Subject
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|remedy_read|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|remedy_accept|", StringComparison.Ordinal)) : null,
        };
        using var world = NonviolentRuntimeFixture.Create(NonviolentRuntimeFixture.Strict(state), provider);
        NonviolentRuntimeFixture.Wake(world, NonviolentRuntimeFixture.Subject, "read-exact-completion-period");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Agreements.Count > 0, 6);
        var choices = provider.Observations.SelectMany(observation => observation.Candidates).ToArray();
        Assert.Contains(choices, candidate => candidate.Id.Contains("|remedy_read|", StringComparison.Ordinal) && candidate.Description.Contains(timing, StringComparison.Ordinal));
        Assert.Contains(choices, candidate => candidate.Id.Contains("|remedy_accept|", StringComparison.Ordinal) && candidate.Description.Contains(timing, StringComparison.Ordinal));
        var agreement = Assert.Single(world.Towns[0].Nonviolent.Agreements);
        Assert.Equal(agreement.AcceptedTick + completionTicks, agreement.DeadlineTick);
        NonviolentRuntimeFixture.Strict(world.ExportState());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsentRequiresRoomForAllPromisedGoodsAtTheDestination(bool townService)
    {
        var state = await NonviolentRuntimeFixture.FindingAsync();
        var actor = NonviolentRuntimeFixture.Subject;
        var town = state.Towns![0];
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "capacity-test-stone", "stone", actor, 6);
        var kind = townService ? "public_service_goods" : "return_goods";
        var beneficiary = townService ? town.Id : NonviolentRuntimeFixture.Witness;
        var target = townService ? warehouse.InstanceId : NonviolentRuntimeFixture.ReturnLot;
        state = await NonviolentRuntimeFixture.OfferRemedyAsync(WithInventory(state, inventory),
            [new(kind, actor, beneficiary, townService ? "stone" : "wood", 1, target),
                new(kind, actor, beneficiary, townService ? "stone" : "wood", 1, target)]);
        inventory = state.Society.Society.Inventory;
        int room;
        if (townService)
        {
            using var setup = NonviolentRuntimeFixture.Create(state, new NonviolentTestProvider());
            var definition = setup.WorldContent.Buildings.Single(building => building.CanonicalId == warehouse.DefinitionId);
            room = BuildingStorageRules.Capacity(definition, warehouse)!.Value -
                inventory.Lots.Where(lot => lot.StorageBuildingId == warehouse.InstanceId).Sum(lot => lot.Quantity);
            inventory = InventoryFixture.AddLot(inventory, "destination-filler", "wood", town.Id, room - 1,
                storageBuildingId: warehouse.InstanceId);
        }
        else
        {
            var recipient = state.Inhabitants.Single(person => person.InhabitantId == beneficiary);
            room = PersonalEquipmentRules.FreeCapacity(inventory, beneficiary, recipient.Equipment);
            inventory = InventoryFixture.AddLot(inventory, "destination-filler", "stone", beneficiary, room - 1);
        }
        Assert.True(room >= 2);
        var provider = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == actor
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|remedy_accept|", StringComparison.Ordinal)) : null,
        };
        using var world = NonviolentRuntimeFixture.Create(WithInventory(state, inventory), provider);
        NonviolentRuntimeFixture.Wake(world, actor, "consider-destination-room");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var observation = Assert.Single(provider.Observations, item => item.InhabitantId == actor);
        Assert.DoesNotContain(observation.Candidates, candidate => candidate.Id.Contains("|remedy_accept|", StringComparison.Ordinal));
        Assert.Contains(observation.Candidates, candidate => candidate.Id.Contains("|remedy_decline|", StringComparison.Ordinal));
        Assert.Empty(world.Towns[0].Nonviolent.Agreements);
        Assert.Empty(world.Towns[0].Nonviolent.Effects);
        Assert.Equal(room - 1, world.Society.Inventory.GetLot("destination-filler").Quantity);
        NonviolentRuntimeFixture.Strict(world.ExportState());
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static NonviolentTestProvider CompletionProvider() => new()
    {
        Choose = observation => observation.InhabitantId == NonviolentRuntimeFixture.Subject
            ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith("nonviolent_remedy:", StringComparison.Ordinal)) : null
    };
}
