using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    private const string HarvestInstructionActor = "agent:00000000000000000000000000000099";

    [Theory]
    [InlineData(false, "berries")]
    [InlineData(true, "fruit")]
    public async Task MustDoHarvestCompletesForBerriesAndFruitAndAdvancesQueueAfterReload(bool orchard, string itemKind)
    {
        var provider = new OrderCandidateRecordingProvider("harvest_food");
        using var world = CreateHarvestInstructionWorld(orchard, provider);
        var before = world.ExportState();
        var actor = before.Inhabitants.Single(person => person.InhabitantId == HarvestInstructionActor);
        var target = orchard
            ? before.Map.Resources.Where(resource => resource.TreeKind == "orchard")
                .OrderBy(resource => before.Map.FootDistance(actor.Position, resource.Position))
                .ThenBy(resource => resource.Id, StringComparer.Ordinal).First()
            : before.Map.Resources.Single(resource => resource.Id == "berry-patch");
        var harvest = world.SubmitInstruction(new OwnerInstructionRequest("harvest", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo,
            $"gather {itemKind} from {target.Id}"));
        var eat = world.SubmitInstruction(new OwnerInstructionRequest("eat", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "eat food", Queue: true));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var state = world.ExportState();
        Assert.Contains(world.Society.Inventory.Lots, lot =>
            lot.OwnerId == HarvestInstructionActor && lot.ItemKind == itemKind && lot.Quantity == 4);
        Assert.Contains(harvest.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.DoesNotContain(eat.InstructionId, state.CompletedInstructionIds ?? []);
        var harvestRequest = Assert.Single(provider.Requests, request =>
            request.InhabitantId == HarvestInstructionActor);
        Assert.Contains(harvestRequest.Candidates, candidate => candidate.Id == "harvest_food" &&
            candidate.DestinationId == target.Id);
        Assert.Single(state.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == harvest.InstructionId + ":harvest_food");
        world.Validate();

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), _ =>
                new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        for (var tick = 0; tick < 50 && !(restored.ExportState().CompletedInstructionIds ?? []).Contains(eat.InstructionId); tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(eat.InstructionId, restored.ExportState().CompletedInstructionIds ?? []);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "instruction_applied" &&
            item.Detail == harvest.InstructionId + ":harvest_food");
        Assert.Contains(restored.ExportState().Events, item => item.Kind == "food_consumed" &&
            item.Detail == HarvestInstructionActor);
        restored.Validate();
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData("gather wood")]
    [InlineData("gather wood at berry-patch")]
    [InlineData("go to the Blacksmith")]
    [InlineData("gather berries and build a House")]
    [InlineData("gather berries from berry-patch-unknown")]
    [InlineData("eat 3 wood")]
    [InlineData("eat 3 stones")]
    [InlineData("eat -3 wood")]
    [InlineData("eat 1.5 berries")]
    [InlineData("do not eat berries")]
    [InlineData("gather -3 berries")]
    public async Task UnsupportedInstructionsDoNotSubstituteARealFoodAction(string text)
    {
        var setup = CreateHarvestInstructionWorld(orchard: false,
            new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var initialState = setup.ExportState();
        setup.Dispose();
        if (text.StartsWith("eat ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("do not eat ", StringComparison.OrdinalIgnoreCase))
        {
            initialState = initialState with
            {
                Inhabitants = initialState.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                    ? person with { HungerBasisPoints = 9_000 } : person).ToArray(),
                Society = initialState.Society with
                {
                    Society = initialState.Society.Society with
                    {
                        Inventory = InventoryFixture.AddLot(initialState.Society.Society.Inventory,
                            "unsupported-order-bait-berries", "berries", HarvestInstructionActor, 1),
                    },
                },
            };
        }
        using var world = PrivateWorldRuntime.Restore(initialState, _ =>
            new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var before = world.ExportState();
        var actorBefore = before.Inhabitants.Single(person => person.InhabitantId == HarvestInstructionActor);
        var foodBefore = before.Society.Society.Inventory.Lots
            .Where(lot => lot.OwnerId == HarvestInstructionActor &&
                (lot.ItemKind is "berries" or "fruit" or "wild_greens"))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal)
            .Select(lot => (lot.Id, lot.ItemKind, lot.Quantity)).ToArray();
        var eventIdsBefore = before.Events.Select(item => item.EventId).ToHashSet();

        var order = world.SubmitInstruction(new OwnerInstructionRequest("unsupported", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, text));
        var submitted = world.ExportState();
        Assert.Equal("not_understood", Assert.Single(submitted.Instructions!,
            item => item.InstructionId == order.InstructionId).Order!.Status);
        Assert.Contains(order.InstructionId, submitted.CompletedInstructionIds ?? []);

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var after = world.ExportState();
        var foodAfter = after.Society.Society.Inventory.Lots
            .Where(lot => lot.OwnerId == HarvestInstructionActor &&
                (lot.ItemKind is "berries" or "fruit" or "wild_greens"))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal)
            .Select(lot => (lot.Id, lot.ItemKind, lot.Quantity)).ToArray();
        Assert.Equal(foodBefore, foodAfter);
        Assert.Equal(actorBefore.Position,
            after.Inhabitants.Single(person => person.InhabitantId == HarvestInstructionActor).Position);
        var newEvents = after.Events.Where(item => !eventIdsBefore.Contains(item.EventId)).ToArray();
        Assert.DoesNotContain(newEvents, item => item.Kind is "food_harvested" or "food_consumed");
        Assert.DoesNotContain(newEvents, item => item.Kind == "instruction_applied" &&
            item.Detail.StartsWith(order.InstructionId + ":", StringComparison.Ordinal));
        Assert.Contains(newEvents, item => item.Kind == "instruction_not_understood" &&
            item.Detail == HarvestInstructionActor + ":" + order.InstructionId);
        world.Validate();
    }

    [Fact]
    public async Task DepletedOrchardDoesNotCompleteMustDoHarvestAcrossReload()
    {
        using var initial = CreateHarvestInstructionWorld(orchard: true);
        var state = initial.ExportState();
        var foodIds = state.Map.Resources.Where(resource => resource.Kind is "food" or "fruit")
            .Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Resources = state.Resources.Select(resource => foodIds.Contains(resource.ResourceId)
                ? resource with { State = ResourceState.Depleted } : resource).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => foodIds.Contains(resource.Id)
                        ? resource with { Quantity = 0, State = EcologyResourceState.Depleted, NextRegenerationDay = 100 }
                        : resource).ToArray(),
                },
            },
        }, _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var harvest = world.SubmitInstruction(new OwnerInstructionRequest("depleted-harvest", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "harvest food"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(harvest.InstructionId, world.ExportState().CompletedInstructionIds ?? []);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "instruction_applied");

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ =>
                new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(harvest.InstructionId, restored.ExportState().CompletedInstructionIds ?? []);
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind == "food_harvested");
        restored.Validate();
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(3, true)]
    public async Task OrchardHarvestNeedsRoomForFruitAndItsProtectedSeed(int cargo, bool fits)
    {
        using var initial = CreateHarvestInstructionWorld(orchard: true);
        var state = initial.ExportState();
        var inventory = state.Society.Society.Inventory;
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != HarvestInstructionActor).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "orchard-cargo", "wood", HarvestInstructionActor, cargo);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                ? person with { HungerBasisPoints = 6_000, LastDecisionContext = null } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ =>
            new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var instruction = world.SubmitInstruction(new OwnerInstructionRequest("orchard-capacity", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "harvest food"));
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var saved = world.ExportState();
        Assert.Equal(fits, (saved.CompletedInstructionIds ?? []).Contains(instruction.InstructionId));
        Assert.Equal(fits ? 4 : 0, world.Society.Inventory.Lots.Where(lot =>
            lot.OwnerId == HarvestInstructionActor && lot.ItemKind == "fruit").Sum(lot => lot.Quantity));
        var seeds = world.Society.Inventory.Lots.Where(lot => lot.OwnerId == HarvestInstructionActor &&
            lot.ItemKind == "orchard_seed" && lot.Quantity > 0).ToArray();
        if (fits)
        {
            var seed = Assert.Single(seeds);
            Assert.Equal(1, seed.Quantity);
            Assert.Contains(world.Society.Inventory.Reservations, item => item.LotId == seed.Id &&
                item.State == InventoryReservationState.Reserved && item.Quantity == 1);
        }
        else Assert.Empty(seeds);
        Assert.Equal(cargo + (fits ? 5 : 0), PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, HarvestInstructionActor, null));
        Assert.Equal(cargo, world.Society.Inventory.GetLot("orchard-cargo").Quantity);
        world.Validate();
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved));
    }

    private static PrivateWorldRuntime CreateHarvestInstructionWorld(bool orchard, IDecisionProvider? provider = null)
    {
        var options = new GeographyOptions("audit-food-route-13", WorldSizePreset.Small);
        var world = new PrivateWorldRuntime(options.Seed,
            _ => provider ?? new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true),
            startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        var map = world.ExportState().Map;
        var anchor = map.Resources.Single(item => item.Id == "berry-patch").Position;
        world.InitializeFirstTownContent();
        world.AcceptFirstTownLayout(anchor);
        var buildingTiles = world.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(
                world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId),
                building.Position)).ToHashSet();
        var startingTiles = map.Tiles.Where(tile =>
                Math.Abs(tile.Position.X - anchor.X) <= 5 && Math.Abs(tile.Position.Y - anchor.Y) <= 5 &&
                map.IsBuildable(tile.Position) && !buildingTiles.Contains(tile.Position) &&
                !map.Resources.Any(item => item.Position == tile.Position))
            .Take(4).Select(tile => tile.Position).ToArray();
        Assert.Equal(4, startingTiles.Length);
        for (var index = 0; index < startingTiles.Length; index++)
            world.PlaceFounder("founder:" + (index + 1).ToString("x32", System.Globalization.CultureInfo.InvariantCulture), startingTiles[index]);
        world.StartWorld();
        world.AddAgent(HarvestInstructionActor, orchard ? OrchardStand(world) : new GridPoint(126, 66));
        var state = world.ExportState();
        var actorPosition = state.Inhabitants.Single(item => item.InhabitantId == HarvestInstructionActor).Position;
        var target = orchard
            ? state.Map.Resources.Where(resource => resource.TreeKind == "orchard")
                .OrderBy(resource => state.Map.FootDistance(actorPosition, resource.Position))
                .ThenBy(resource => resource.Id, StringComparer.Ordinal).First()
            : state.Map.Resources.Single(resource => resource.Id == "berry-patch");
        var terrain = state.Map.Tiles.Single(tile => tile.Position == target.Position).Terrain.ToString();
        var knownSource = new AgentKnowledgeFact(
            "instruction-known-food-site:" + target.Id, HarvestInstructionActor, HarvestInstructionActor,
            target.Position, terrain, [orchard ? "fruit" : "berries"],
            state.Society.Society.WorldTick, "firsthand");
        state = state with
        {
            Knowledge = state.Knowledge! with { Facts = state.Knowledge.Facts.Append(knownSource).ToArray() },
        };
        if (orchard)
        {
            // Orchard trees fruit only in autumn; put them in season for this check.
            var orchards = state.Map.Resources.Where(resource => resource.TreeKind == "orchard")
                .Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
            state = state with
            {
                Resources = state.Resources.Select(resource => orchards.Contains(resource.ResourceId)
                    ? resource with { State = ResourceState.Available } : resource).ToArray(),
                WorldSystems = state.WorldSystems! with
                {
                    Ecology = state.WorldSystems.Ecology with
                    {
                        Resources = state.WorldSystems.Ecology.Resources.Select(resource => orchards.Contains(resource.Id)
                            ? TreeGrowthAndPlantingTests.InFruitingSeason(resource, state) : resource).ToArray(),
                    },
                },
            };
        }
        world.Dispose();
        return PrivateWorldRuntime.Restore(state,
            _ => provider ?? new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
    }

    // An empty tile beside an orchard tree with no other food within reach,
    // found from the generated map so terrain tuning cannot strand the test.
    private static GridPoint OrchardStand(PrivateWorldRuntime world)
    {
        var map = world.ExportState().Map;
        var occupied = world.Inhabitants.Select(person => person.Position).ToHashSet();
        var food = map.Resources.Where(item => item.Kind == "food").Select(item => item.Position).ToArray();
        return map.Resources.Where(item => item.TreeKind == "orchard")
            .SelectMany(item => map.FootNeighbors(item.Position))
            .First(point => map.IsBuildable(point) && !occupied.Contains(point) &&
                map.Resources.All(item => item.Position != point) &&
                world.Towns.All(town => !town.BorderTiles.Contains(point)) &&
                food.All(site => map.FootDistance(point, site) > 3));
    }
}
