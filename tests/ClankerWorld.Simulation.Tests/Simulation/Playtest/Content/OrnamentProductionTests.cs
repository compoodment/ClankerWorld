using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class OrnamentProductionTests
{
    private const string Smith = "first-town-blacksmith";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticSmithSupplyKeepsAWornOrnamentButCanDeliverTheSameUnwornOwnedUnit(bool worn)
    {
        using var setup = NormalPathWorld.CreateGenerated("ornament-smith-selected", _ => new Choices([]));
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == Smith);
        var owner = smith.HouseholdId!;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == owner).Id;
        var inventory = state.Society.Society.Inventory;
        foreach (var (kind, quantity) in new[] { ("wood", 6), ("stone", 4), ("iron_ore", 4), ("iron", 4),
                     ("gold_ore", 4), (OrnamentContent.Gold, 4), ("diamond", 2) })
            inventory = InventoryFixture.AddLot(inventory, "smith-waiting-" + kind, kind, owner, quantity,
                storageBuildingId: Smith);
        inventory = InventoryFixture.AddLot(inventory, "smith-personal-ornament", OrnamentContent.GoldOrnament, actor, 1);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = smith.Position, HungerBasisPoints = 9_000, Project = null, LastDecisionContext = null,
                Equipment = (person.Equipment ?? new PersonalEquipment()) with
                { OrnamentLotId = worn ? "smith-personal-ornament" : null },
            } : person).ToArray(),
        };
        var choices = new Choices(["haul_smith_input"]);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? choices : new Choices([]));
        var setting = world.WorldContent.Recipes.Single(recipe => recipe.LocalId == "set-diamond");
        Assert.False(world.StartProduction(setting.CanonicalId, Smith, actor).Applied);
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var ornament = world.Society.Inventory.GetLot("smith-personal-ornament");
        Assert.Equal(1, ornament.Quantity);
        if (worn)
        {
            Assert.DoesNotContain("haul_smith_input", choices.Selected);
            Assert.Equal(actor, ornament.OwnerId);
            Assert.Null(ornament.StorageBuildingId);
            Assert.Equal(ornament.Id, world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment!.OrnamentLotId);
            Assert.False(world.StartProduction(setting.CanonicalId, Smith, actor).Applied);
        }
        else
        {
            Assert.Contains("haul_smith_input", choices.Selected);
            Assert.Equal((owner, Smith), (ornament.OwnerId, ornament.StorageBuildingId));
            Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == actor).Equipment?.OrnamentLotId);
            Assert.True(world.StartProduction(setting.CanonicalId, Smith, actor).Applied);
            Assert.Contains(world.Society.Inventory.Reservations, item => item.LotId == ornament.Id &&
                item.OwnerId == owner && item.Quantity == 1 && item.State == InventoryReservationState.Reserved);
        }
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new Choices([]));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
    }

    [Fact]
    public async Task AnIronPickMinesFiniteRareStockWhichWalksIntoActualOrnamentRecipesAcrossReload()
    {
        using var generated = NormalPathWorld.CreateGenerated("ornament-rare-pipeline", _ => new Choices([]));
        var state = generated.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == Smith);
        var owner = smith.HouseholdId!;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == owner).Id;
        var goldSource = state.Map.Resources.Where(source => source.Kind == "gold_ore" && !source.IsRenewable &&
                state.Map.IsReachableOnFoot(smith.Position, source.Position) &&
                !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == source.Position))
            .OrderBy(source => state.Map.FootDistance(smith.Position, source.Position)).ThenBy(source => source.Id).First();
        var diamondSources = state.Map.Resources.Where(source => source.Kind == "diamond" && !source.IsRenewable &&
                state.Map.IsReachableOnFoot(smith.Position, source.Position)).ToArray();
        Assert.NotEmpty(diamondSources);
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "ornament-pick", "iron_pickaxe", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "ornament-basket", "basket", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "ornament-fuel", "wood", owner, 2, storageBuildingId: Smith);
        state = WithInventory(state, inventory) with
        {
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == goldSource.Id
                        ? resource with { Quantity = 1 } : resource).ToArray(),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = goldSource.Position, HungerBasisPoints = 10_000, Project = null,
                LastDecisionContext = null, Equipment = new(CarryAidLotId: "ornament-basket"),
            } : person).ToArray(),
        };
        var choices = new Choices(["gather_rare_material:gold_ore"]);
        IDecisionProvider Provider(string id) => id == actor ? choices : new Choices([]);
        using var mining = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), Provider);
        await Until(mining, () => mining.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ItemKind == "gold_ore"), 20);
        var rawGold = Assert.Single(mining.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "gold_ore");
        Assert.Equal(8, rawGold.Quantity);
        Assert.True(PersonalEquipmentRules.IsCarried(rawGold, actor));
        Assert.Null(rawGold.DeliveryBuildingId);
        Assert.Equal((0, EcologyResourceState.Depleted), (mining.WorldSystems.Ecology.GetResource(goldSource.Id).Quantity,
            mining.WorldSystems.Ecology.GetResource(goldSource.Id).State));
        Assert.Equal(9_000, mining.Society.Inventory.GetLot("ornament-pick").ConditionBasisPoints);
        choices.Preferences = ["haul_household_stock", "haul_smith_input"];
        await Until(mining, () => mining.Inhabitants.Single(person => person.InhabitantId == actor).Position != goldSource.Position,
            40);
        Assert.Equal((actor, 8), (mining.Society.Inventory.GetLot(rawGold.Id).OwnerId,
            mining.Society.Inventory.GetLot(rawGold.Id).Quantity));
        Assert.DoesNotContain(mining.Society.Inventory.Lots, lot => lot.ItemKind == "gold_ore" && lot.StorageBuildingId == Smith);
        var bytes = PrivateWorldRuntimeCodec.Encode(mining.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        await Until(resumed, () => resumed.Society.Inventory.Lots.Any(lot => lot.ItemKind == "gold_ore" &&
            lot.StorageBuildingId == Smith), 600);
        var deliveredGold = Assert.Single(resumed.Society.Inventory.Lots, lot => lot.ItemKind == "gold_ore" &&
            lot.StorageBuildingId == Smith);
        Assert.Equal((owner, Smith, rawGold.Id, 4), (deliveredGold.OwnerId, deliveredGold.StorageBuildingId,
            deliveredGold.ProvenanceLotId, deliveredGold.Quantity));
        choices.Preferences = [];
        var firstGold = await Produce(resumed, actor, "refine-gold", 24,
            [(deliveredGold.Id, 2), ("ornament-fuel", 1)]);
        var secondGold = await Produce(resumed, actor, "refine-gold", 24,
            [(deliveredGold.Id, 2), ("ornament-fuel", 1)]);
        Assert.DoesNotContain(resumed.Society.Inventory.Lots, lot => lot.Id is "ornament-fuel" || lot.Id == deliveredGold.Id);
        Assert.Equal(4, resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == "gold_ore").Sum(lot => lot.Quantity));
        Assert.Equal(2, resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == OrnamentContent.Gold).Sum(lot => lot.Quantity));
        var plain = await Produce(resumed, actor, "gold-ornament", 24, [(firstGold, 1), (secondGold, 1)]);
        Assert.Equal((OrnamentContent.GoldOrnament, owner, Smith, 1),
            (resumed.Society.Inventory.GetLot(plain).ItemKind, resumed.Society.Inventory.GetLot(plain).OwnerId,
                resumed.Society.Inventory.GetLot(plain).StorageBuildingId, resumed.Society.Inventory.GetLot(plain).Quantity));
        Assert.DoesNotContain(resumed.Society.Inventory.Lots, lot => lot.ItemKind == OrnamentContent.Gold);
        var beforeDiamond = diamondSources.ToDictionary(source => source.Id,
            source => resumed.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        choices.Preferences = ["gather_rare_material:diamond"];
        await Until(resumed, () => resumed.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ItemKind == "diamond"), 600);
        var diamonds = Assert.Single(resumed.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "diamond");
        Assert.Equal(8, diamonds.Quantity);
        Assert.Single(diamondSources, source => resumed.WorldSystems.Ecology.GetResource(source.Id).Quantity == beforeDiamond[source.Id] - 1);
        Assert.Equal(8_000, resumed.Society.Inventory.GetLot("ornament-pick").ConditionBasisPoints);
        choices.Preferences = ["haul_household_stock", "haul_smith_input"];
        await Until(resumed, () => resumed.Society.Inventory.Lots.Any(lot => lot.ItemKind == "diamond" && lot.StorageBuildingId == Smith), 600);
        var onSiteDiamond = Assert.Single(resumed.Society.Inventory.Lots, lot => lot.ItemKind == "diamond" && lot.StorageBuildingId == Smith);
        Assert.Equal((owner, diamonds.Id, 2), (onSiteDiamond.OwnerId, onSiteDiamond.ProvenanceLotId, onSiteDiamond.Quantity));
        choices.Preferences = [];
        var finished = await Produce(resumed, actor, "set-diamond", 28, [(plain, 1), (onSiteDiamond.Id, 1)]);
        Assert.Equal((OrnamentContent.DiamondOrnament, owner, Smith, 1),
            (resumed.Society.Inventory.GetLot(finished).ItemKind, resumed.Society.Inventory.GetLot(finished).OwnerId,
                resumed.Society.Inventory.GetLot(finished).StorageBuildingId, resumed.Society.Inventory.GetLot(finished).Quantity));
        Assert.DoesNotContain(resumed.Society.Inventory.Lots, lot => lot.Id == plain);
        Assert.Equal(7, resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == "diamond").Sum(lot => lot.Quantity));
        Assert.Contains("gather_rare_material:gold_ore", choices.Selected);
        Assert.Contains("gather_rare_material:diamond", choices.Selected);
        Assert.Contains("haul_smith_input", choices.Selected);
        var finishedBytes = PrivateWorldRuntimeCodec.Encode(resumed.ExportState());
        using var finalReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(finishedBytes), Provider);
        Assert.Equal(finishedBytes, PrivateWorldRuntimeCodec.Encode(finalReload.ExportState()));
        Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.True((await finalReload.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(resumed.ExportState()), PrivateWorldRuntimeCodec.Encode(finalReload.ExportState()));
    }

    [Theory]
    [InlineData("remote")]
    [InlineData("private")]
    [InlineData("reserved")]
    public void OrnamentRecipesCannotConsumeRemoteForeignOrReservedInputs(string boundary)
    {
        using var setup = NormalPathWorld.CreateGenerated("ornament-input-boundary", _ => new Choices([]));
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == Smith);
        var owner = smith.HouseholdId!;
        var worker = state.Society.Society.Inhabitants.First(person => person.HouseholdId == owner).Id;
        var actor = boundary == "private"
            ? state.Society.Society.Inhabitants.First(person => person.HouseholdId != owner).Id : worker;
        var house = state.WorldSimulation.Buildings.Single(building => building.HouseholdId == owner &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "guarded-gold", OrnamentContent.Gold,
            owner, 2, storageBuildingId: boundary == "remote" ? house.InstanceId : Smith);
        if (boundary == "reserved") inventory = InventoryFixture.Reserve(inventory, "guarded-gold-other-work",
            owner, "guarded-gold", 1, "other_work", 120);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = smith.Position } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), _ => new Choices([]));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var result = world.StartProduction(world.WorldContent.Recipes.Single(recipe => recipe.LocalId == "gold-ornament").CanonicalId,
            Smith, actor);
        Assert.False(result.Applied);
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        Assert.Equal(2, world.Society.Inventory.GetLot("guarded-gold").Quantity);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task NormalRecipeChoicesMakeAndSetOnlyActualOnSiteGoldAndDiamond()
    {
        using var setup = NormalPathWorld.CreateGenerated("ornament-normal-recipes", _ => new Choices([]));
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == Smith);
        var owner = smith.HouseholdId!;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == owner).Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "normal-gold", OrnamentContent.Gold,
            owner, 2, storageBuildingId: Smith);
        inventory = InventoryFixture.AddLot(inventory, "normal-diamond", "diamond", owner, 1, storageBuildingId: Smith);
        var house = state.WorldSimulation.Buildings.Single(building => building.HouseholdId == owner &&
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("house"));
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            { Position = house.Position, HungerBasisPoints = 10_000, Project = null, LastDecisionContext = null } : person).ToArray(),
        };
        var plain = state.WorldContent!.Recipes.Single(recipe => recipe.LocalId == "gold-ornament");
        var setting = state.WorldContent.Recipes.Single(recipe => recipe.LocalId == "set-diamond");
        var choices = new Choices(["build:recipe:" + plain.CanonicalId, "build:recipe:" + setting.CanonicalId]);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? choices : new Choices([]));
        await Until(world, () => world.WorldSimulation.ProductionJobs.Any(job => job.RecipeId == plain.CanonicalId), 120);
        var running = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(Smith, running.BuildingInstanceId);
        Assert.Equal(actor, running.WorkerId);
        Assert.Equal(smith.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == actor ? choices : new Choices([]));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        await Until(resumed, () => resumed.Society.Inventory.Lots.Any(lot => lot.ItemKind == OrnamentContent.DiamondOrnament), 180);
        Assert.Contains("build:recipe:" + plain.CanonicalId, choices.Selected);
        Assert.Contains("build:recipe:" + setting.CanonicalId, choices.Selected);
        Assert.Equal(2, resumed.WorldSimulation.ProductionJobs.Count);
        Assert.All(resumed.WorldSimulation.ProductionJobs, job => Assert.Equal(WorldProductionJobState.Completed, job.State));
        Assert.DoesNotContain(resumed.Society.Inventory.Lots, lot => lot.Id is "normal-gold" or "normal-diamond" ||
            lot.ItemKind == OrnamentContent.GoldOrnament);
        var finished = Assert.Single(resumed.Society.Inventory.Lots, lot => lot.ItemKind == OrnamentContent.DiamondOrnament);
        Assert.Equal((owner, Smith, 1), (finished.OwnerId, finished.StorageBuildingId, finished.Quantity));
        Assert.Contains(resumed.Society.Inventory.Reservations, item => item.LotId == "normal-gold" && item.Quantity == 2 &&
            item.OwnerId == owner && item.State == InventoryReservationState.Completed);
        Assert.Contains(resumed.Society.Inventory.Reservations, item => item.LotId == "normal-diamond" && item.Quantity == 1 &&
            item.OwnerId == owner && item.State == InventoryReservationState.Completed);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(resumed.ExportState()),
            PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(resumed.ExportState()))));
    }

    private static async Task<string> Produce(PrivateWorldRuntime world, string actor, string localId,
        int workTicks, (string LotId, int Quantity)[] expectedInputs)
    {
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == localId);
        var started = world.StartProduction(recipe.CanonicalId, Smith, actor);
        Assert.True(started.Applied, started.Failure);
        var job = world.WorldSimulation.ProductionJobs.Single(item => item.JobId == started.JobId);
        Assert.Equal(workTicks, job.CompletionTick - job.StartedTick);
        var reservations = job.InputReservationIds.Select(world.Society.Inventory.GetReservation).ToArray();
        Assert.Equal(expectedInputs.Length, reservations.Length);
        foreach (var (lotId, quantity) in expectedInputs)
            Assert.Contains(reservations, item => item.LotId == lotId && item.Quantity == quantity &&
                item.State == InventoryReservationState.Reserved);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new Choices([]));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        for (var tick = 1; tick < workTicks; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.All(reservations, item => Assert.Equal(InventoryReservationState.Completed,
            world.Society.Inventory.GetReservation(item.Id).State));
        Assert.All(reservations, item => Assert.Equal(world.Society.GetInhabitant(actor).HouseholdId, item.OwnerId));
        return started.JobId + ":output:00";
    }

    private static async Task Until(PrivateWorldRuntime world, Func<bool> complete, int limit)
    {
        for (var tick = 0; tick < limit && !complete(); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(complete(), "The real extraction, delivery or production did not finish within its phase budget.");
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) => state with
    { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private sealed class Choices(string[] preferences) : IDecisionProvider
    {
        public string[] Preferences { get; set; } = preferences;
        public HashSet<string> Selected { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = Preferences.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.StartsWith(prefix, StringComparison.Ordinal))).FirstOrDefault(candidate => candidate is not null)
                ?? request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            Selected.Add(selected.Id);
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [selected] } }, cancellationToken);
        }
    }
}
