using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PersonalGearTests
{
    [Fact]
    public async Task OrdinarySmithChoicesRefineGoldSetDiamondAndWearTheActualOutputAcrossReload()
    {
        var state = Initial();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = Actor(state, smith.HouseholdId!);
        state = Stock(Stock(Stock(state, "rare-ore", "gold_ore", smith.HouseholdId!, 4, smith.InstanceId),
            "fuel", "wood", smith.HouseholdId!, 2, smith.InstanceId), "gem", "diamond", smith.HouseholdId!, 1, smith.InstanceId);
        var gold = state.WorldContent!.Recipes.Single(recipe => recipe.LocalId == "refine-gold").CanonicalId;
        var ornament = state.WorldContent.Recipes.Single(recipe => recipe.LocalId == "gold-ornament").CanonicalId;
        var diamond = state.WorldContent.Recipes.Single(recipe => recipe.LocalId == "set-diamond").CanonicalId;
        state = At(state, actor, smith.Position);
        IDecisionProvider Provider(string id) => new Preferred(id == actor
            ? ["equip_personal_gear:diamond_ornament", "build:recipe:" + diamond, "build:recipe:" + ornament, "build:recipe:" + gold] : []);
        var world = PrivateWorldRuntime.Restore(state, Provider);
        try
        {
            var reloaded = false;
            for (var tick = 0; tick < 450 && world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.OrnamentLotId is null; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!reloaded && world.WorldSimulation.ProductionJobs.Any(job => job.RecipeId == diamond && job.State == WorldProductionJobState.Running))
                {
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, Provider);
                    reloaded = true;
                }
            }
            Assert.True(reloaded);
            var lotId = Assert.IsType<string>(world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.OrnamentLotId);
            var lot = world.Society.Inventory.GetLot(lotId);
            Assert.Equal("diamond_ornament", lot.ItemKind);
            Assert.Equal(actor, lot.OwnerId);
            Assert.Null(lot.StorageBuildingId);
            Assert.Equal(1, lot.Quantity);
            Assert.DoesNotContain(world.Society.Inventory.Lots, item => item.Id is "rare-ore" or "fuel" or "gem");
            Assert.Contains(world.WorldSimulation.ProductionJobs, job => job.RecipeId == gold && job.State == WorldProductionJobState.Completed);
            Assert.Contains(world.WorldSimulation.ProductionJobs, job => job.RecipeId == ornament && job.State == WorldProductionJobState.Completed);
            world.Validate();
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public void SwitchingWeaponPreservesOldStockAndForeignReservedAndBrokenItemsCannotBeEquipped()
    {
        var state = Initial();
        var actor = Actor(state, "household:camp-alpha");
        state = Stock(Stock(Stock(Stock(state, "spears", "spear", actor, 2), "sword", "sword", actor, 1),
            "foreign-shield", "shield", "household:camp-alpha", 1, "first-town-house-a"), "broken", "shield", actor, 1);
        state = ChangeInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == "broken" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray(),
        });
        state = Stock(state, "reserved", "shield", actor, 1);
        state = ChangeInventory(state, InventoryFixture.Reserve(state.Society.Society.Inventory, "held-shield", actor,
            "reserved", 1, "barter", 500));
        using var world = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        Assert.True(world.EquipItem(actor, "spears").Applied);
        var oldId = world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.WeaponLotId;
        Assert.True(world.EquipItem(actor, "sword").Applied);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        foreach (var id in new[] { "foreign-shield", "broken", "reserved" }) Assert.False(world.EquipItem(actor, id).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal("sword", world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.WeaponLotId);
        Assert.Equal(actor, world.Society.Inventory.GetLot(Assert.IsType<string>(oldId)).OwnerId);
        Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "spear" && lot.OwnerId == actor).Sum(lot => lot.Quantity));
        world.Validate();
    }

    [Fact]
    public void GivingWornOrnamentTransfersItsIdentityAndClearsTheDonorsSlotAcrossReload()
    {
        var state = Initial();
        var actor = Actor(state, "household:camp-alpha");
        var recipient = Actor(state, "household:camp-beta");
        state = Stock(state, "family-ornament", "gold_ornament", actor, 1);
        state = At(state, recipient, state.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        using var world = PrivateWorldRuntime.Restore(state, _ => new Preferred([]));
        Assert.True(world.EquipItem(actor, "family-ornament").Applied);
        Assert.True(world.GiveOrnament(actor, recipient, "family-ornament").Applied);
        Assert.Null(world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.OrnamentLotId);
        Assert.Equal(recipient, world.Society.Inventory.GetLot("family-ornament").OwnerId);
        Assert.True(world.EquipItem(recipient, "family-ornament").Applied);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal("family-ornament", restored.ExportState().Inhabitants.Single(person => person.InhabitantId == recipient).Equipment!.OrnamentLotId);
        Assert.Equal(1, restored.Society.Inventory.GetLot("family-ornament").Quantity);
        var before = PrivateWorldRuntimeCodec.Encode(restored.ExportState());
        Assert.False(restored.GiveOrnament(actor, recipient, "family-ornament").Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task OrdinaryRepairConsumesTheHouseholdsOnSiteIronAndKeepsTheEquippedGear()
    {
        var state = Initial();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = Actor(state, smith.HouseholdId!);
        state = Stock(Stock(state, "worn-sword", "sword", actor, 1), "repair-iron", "iron", smith.HouseholdId!, 1, smith.InstanceId);
        state = ChangeInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Select(lot => lot.Id == "worn-sword" ? lot with { ConditionBasisPoints = 3_000 } : lot).ToArray(),
        });
        state = At(state, actor, smith.Position);
        using var world = PrivateWorldRuntime.Restore(state, id => new Preferred(id == actor ? ["repair_combat_gear:"] : []));
        Assert.True(world.EquipItem(actor, "worn-sword").Applied);
        for (var tick = 0; tick < 20 && world.Society.Inventory.GetLot("worn-sword").ConditionBasisPoints < 10_000; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(10_000, world.Society.Inventory.GetLot("worn-sword").ConditionBasisPoints);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "repair-iron");
        Assert.Equal("worn-sword", world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.WeaponLotId);
        world.Validate();
    }

    [Fact]
    public async Task AnUnequippedOrnamentCanBeBarteredThroughNormalConsentAfterTheOfferReloads()
    {
        var state = Initial();
        var actor = Actor(state, "household:camp-alpha");
        var buyer = Actor(state, "household:camp-beta");
        var camp = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse").Position;
        state = ChangeInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor && lot.OwnerId != buyer).ToArray(),
        });
        state = Stock(Stock(state, "trade-ornament", "gold_ornament", actor, 1), "trade-food", "food", buyer, 2);
        var home = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a").Position;
        Assert.True(state.Map.FootDistance(home, camp) > 1);
        state = At(At(state, actor, home), buyer, camp);
        state = state with { Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 7_500 }).ToArray() };
        IDecisionProvider Provider(string id) => new Preferred(id == actor ? ["trade_meet:", "trade_propose:" + buyer] : id == buyer ? ["trade_accept:"] : []);
        var world = PrivateWorldRuntime.Restore(state, Provider);
        try
        {
            var reloaded = false;
            for (var tick = 0; tick < 150 && !world.Society.Inventory.Offers.Any(offer => offer.State == DirectBarterState.Settled); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!reloaded && world.Society.Inventory.Offers.Any(offer => offer.State == DirectBarterState.Open))
                {
                    Assert.Equal(actor, world.Society.Inventory.GetLot("trade-ornament").OwnerId);
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, Provider);
                    reloaded = true;
                }
            }
            Assert.True(reloaded);
            Assert.Contains(world.Society.Inventory.Offers, offer => offer.State == DirectBarterState.Settled);
            Assert.Equal(buyer, world.Society.Inventory.GetLot("trade-ornament").OwnerId);
            Assert.InRange(state.Map.FootDistance(world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Position, camp), 0, 1);
            Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "food" && lot.Quantity == 1);
            Assert.DoesNotContain(world.Society.Inventory.Reservations, reservation => reservation.State == InventoryReservationState.Reserved);
            world.Validate();
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public void SaveRefusesAReservedOrForeignOrnamentReference()
    {
        var state = Initial();
        var actor = Actor(state, "household:camp-alpha");
        state = Stock(state, "saved-ornament", "gold_ornament", "household:camp-alpha", 1, "first-town-house-a");
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Equipment = new EquipmentState(OrnamentLotId: "saved-ornament") } : person).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state));
        state = ChangeInventory(state, InventoryFixture.Transfer(state.Society.Society.Inventory, "take-ornament",
            "household:camp-alpha", actor, "saved-ornament", 1, "taken"));
        state = ChangeInventory(state, InventoryFixture.Reserve(state.Society.Society.Inventory, "saved-reservation", actor,
            "saved-ornament", 1, "barter", 500));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state));
    }

    private static PrivateWorldRuntimeState Initial()
    {
        using var world = NormalPathWorld.CreateGenerated("probe-a", _ => new Preferred([]));
        return world.ExportState();
    }

    private static string Actor(PrivateWorldRuntimeState state, string household) =>
        state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;

    private static PrivateWorldRuntimeState At(PrivateWorldRuntimeState state, string actor, GridPoint position) => state with
    { Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = position } : person).ToArray() };

    private static PrivateWorldRuntimeState Stock(PrivateWorldRuntimeState state, string id, string kind, string owner, int quantity,
        string? building = null) => ChangeInventory(state,
        InventoryFixture.AddLot(state.Society.Society.Inventory, id, kind, owner, quantity, storageBuildingId: building));

    private static PrivateWorldRuntimeState ChangeInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private sealed class Preferred(IReadOnlyList<string> prefixes) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith(prefix, StringComparison.Ordinal))).FirstOrDefault(candidate => candidate is not null)
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }
}
