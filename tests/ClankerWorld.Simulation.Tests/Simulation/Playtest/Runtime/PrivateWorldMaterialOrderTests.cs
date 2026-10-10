using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Theory]
    [InlineData("wood", "wooden_axe", 6)]
    [InlineData("stone", "wooden_pickaxe", 6)]
    [InlineData("fiber", null, 4)]
    [InlineData("clay", null, 4)]
    [InlineData("iron_ore", "stone_pickaxe", 7)]
    [InlineData("gold_ore", "iron_pickaxe", 8)]
    [InlineData("diamond", "iron_pickaxe", 8)]
    public async Task MaterialOrderUsesRealHarvestToolsAndSourceStock(string kind, string? tool, int quantity)
    {
        var (state, source) = MaterialOrderState(kind, tool);
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "one-load", $"gather {kind.Replace('_', ' ')} from {source.Id}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var after = world.ExportState();
        var order = CancellationOrder(after, receipt.InstructionId);
        Assert.Equal(("gather_material", "finished", 1, "harvests", kind),
            (order.Action, order.Status, order.CompletedUnits, order.ProgressUnit, order.TargetMaterialKind));
        var lot = Assert.Single(world.Society.Inventory.Lots, item => item.OwnerId == HarvestInstructionActor && item.ItemKind == kind);
        Assert.Equal(quantity, lot.Quantity);
        Assert.Equal("gather:" + lot.Id, order.LastEffectId);
        Assert.Equal(0, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        if (tool is not null)
            Assert.Equal(10_000 - ToolProgressionRules.Find(tool)!.WearLossBasisPoints,
                world.Society.Inventory.GetLot("material-order-tool").ConditionBasisPoints);
        var projected = Assert.Single(new OwnerWorldObservationStore(world).GetSnapshot().Instructions,
            item => item.InstructionId == receipt.InstructionId);
        Assert.Equal(order.TargetMaterialKind, projected.Order!.TargetMaterialKind);
        Assert.Equal(order.TargetResourceId, projected.Order.TargetResourceId);
        Assert.Equal(order.CompletedUnits, projected.Order.CompletedUnits);
        world.Validate();
    }

    [Fact]
    public async Task MaterialOrderQuantitiesAndQueueContinueIdenticallyAfterReload()
    {
        var (state, source) = MaterialOrderState("fiber", quantity: 3);
        using var world = RestoreMaterialOrderWorld(state);
        var first = SubmitMaterialOrder(world, "five", $"gather five plant fiber from {source.Id}");
        var second = SubmitMaterialOrder(world, "queued", $"gather fiber from {source.Id}", queue: true);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("doing", 4, "material_items"), (CancellationOrder(world.ExportState(), first.InstructionId).Status,
            CancellationOrder(world.ExportState(), first.InstructionId).CompletedUnits,
            CancellationOrder(world.ExportState(), first.InstructionId).ProgressUnit));
        Assert.Equal("queued", CancellationOrder(world.ExportState(), second.InstructionId).Status);
        using var restored = RestoreMaterialOrderWorld(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        Assert.Equal(("finished", 8), (CancellationOrder(restored.ExportState(), first.InstructionId).Status,
            CancellationOrder(restored.ExportState(), first.InstructionId).CompletedUnits));
        Assert.Equal("finished", CancellationOrder(restored.ExportState(), second.InstructionId).Status);
        Assert.Equal(12, restored.Society.Inventory.Lots.Where(lot => lot.OwnerId == HarvestInstructionActor && lot.ItemKind == "fiber").Sum(lot => lot.Quantity));
        Assert.Equal(3, restored.ExportState().Events.Count(item => item.Kind == "material_gathered" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal)));
        restored.Validate();
    }

    [Theory]
    [InlineData("gather wood and stone")]
    [InlineData("gather -2 wood")]
    [InlineData("gather 1.5 stone")]
    [InlineData("gather wood from berry-patch")]
    [InlineData("do not gather wood")]
    [InlineData("gather iron")]
    [InlineData("gather cloth")]
    [InlineData("gather stone at (12,)")]
    public void MaterialOrderRejectsUnsupportedTextWithoutReplacingTheTask(string text)
    {
        var (state, _) = MaterialOrderState("fiber");
        using var world = RestoreMaterialOrderWorld(state);
        var current = SubmitMaterialOrder(world, "current", "keep gathering fiber until cancelled");
        var unsupported = SubmitMaterialOrder(world, "unsupported", text);
        Assert.Equal("not_understood", CancellationOrder(world.ExportState(), unsupported.InstructionId).Status);
        Assert.Equal("waiting", CancellationOrder(world.ExportState(), current.InstructionId).Status);
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaterialOrderDiscoversAnExplicitUnknownSiteByTravellingThere(bool nearlyFullMemory)
    {
        var (state, source) = MaterialOrderState("clay", distant: true);
        if (nearlyFullMemory)
        {
            var position = CancellationActorPosition(state);
            var remembered = state.Map.Tiles.Where(tile => state.Map.IsPassable(tile.Position) &&
                    state.Map.FootDistance(tile.Position, position) > 8 &&
                    state.Map.FootDistance(tile.Position, source.Position) > 8)
                .Take(127).Select((tile, index) => new AgentKnowledgeFact(
                    $"material-known-{index}", HarvestInstructionActor, HarvestInstructionActor,
                    tile.Position, tile.Terrain.ToString(), [], state.Society.Society.WorldTick, "firsthand")).ToArray();
            Assert.Equal(127, remembered.Length);
            state = state with { Knowledge = state.Knowledge! with { Facts = state.Knowledge.Facts.Concat(remembered).ToArray() } };
        }
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "unknown-site", $"gather clay from {source.Id}");
        Assert.DoesNotContain(world.ExportState().Knowledge!.Facts,
            fact => fact.OwnerId == HarvestInstructionActor && fact.Position == source.Position);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(0, CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits);
        Assert.Equal(1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        Assert.DoesNotContain(world.ExportState().Knowledge!.Facts,
            fact => fact.OwnerId == HarvestInstructionActor && fact.Position == source.Position);
        using var restored = RestoreMaterialOrderWorld(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 35 && CancellationOrder(restored.ExportState(), receipt.InstructionId).Status != "finished"; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", CancellationOrder(restored.ExportState(), receipt.InstructionId).Status);
        Assert.Contains(restored.ExportState().Knowledge!.Facts,
            fact => fact.OwnerId == HarvestInstructionActor && fact.Position == source.Position && fact.ResourceKinds.Contains("clay"));
        Assert.Equal(4, Assert.Single(restored.Society.Inventory.Lots,
            lot => lot.OwnerId == HarvestInstructionActor && lot.ItemKind == "clay").Quantity);
        restored.Validate();
    }

    [Fact]
    public async Task MaterialOrderDoesNotSubstituteAResourceForAnEmptyExplicitTile()
    {
        var (state, source) = MaterialOrderState("fiber");
        var empty = state.Map.FootNeighbors(CancellationActorPosition(state)).First(point =>
            state.Map.IsPassable(point) && !state.Map.Resources.Any(item => item.Position == point));
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "empty", $"gather fiber at tile ({empty.X}, {empty.Y})");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        Assert.Contains("no available matching material", CancellationOrder(world.ExportState(), receipt.InstructionId).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == HarvestInstructionActor && lot.ItemKind == "fiber");
        world.Validate();
    }

    [Fact]
    public async Task MaterialOrderCanInspectAnEmptyImpassableTileWithoutSavingInvalidKnowledge()
    {
        var (state, _) = MaterialOrderState("fiber");
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != HarvestInstructionActor)
            .Select(person => person.Position).ToHashSet();
        var buildings = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
        var target = state.Map.Tiles.Select(tile => tile.Position).First(point => !state.Map.IsPassable(point) &&
            state.Map.Tiles.Any(neighbor => state.Map.FootDistance(point, neighbor.Position) == 1 && state.Map.IsPassable(neighbor.Position) && !occupied.Contains(neighbor.Position) && !buildings.Contains(neighbor.Position)));
        var shore = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.FootDistance(point, target) == 1 && state.Map.IsPassable(point) && !occupied.Contains(point) && !buildings.Contains(point));
        var position = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsPassable(point) &&
            !occupied.Contains(point) && !buildings.Contains(point) && state.Map.FootDistance(point, target) is >= 5 and <= 7 &&
            DeterministicRouteFinder.TryFind(state.Map, point, shore, out var route) && route.All(step => !occupied.Contains(step) && !buildings.Contains(step)));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                ? person with { Position = position } : person).ToArray()
        };
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "impassable-site", $"gather fiber at ({target.X}, {target.Y})");
        for (var tick = 0; tick < 35 && CancellationOrder(world.ExportState(), receipt.InstructionId).Status != "blocked"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            world.Validate();
        }
        Assert.Equal("blocked", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        Assert.Equal(0, CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits);
        Assert.DoesNotContain(world.ExportState().Knowledge!.Facts,
            fact => fact.OwnerId == HarvestInstructionActor && fact.Position == target);
        using var restored = RestoreMaterialOrderWorld(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        restored.Validate();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task MaterialOrderWaitsForUsableToolsAndCarryingSpace(bool full, bool withTool)
    {
        var (state, source) = MaterialOrderState("stone", withTool ? "wooden_pickaxe" : null);
        var inventory = state.Society.Society.Inventory;
        if (full) inventory = FillActorCarry(inventory, state.Inhabitants.Single(item => item.InhabitantId == HarvestInstructionActor), "material-cargo");
        else inventory = inventory with { Lots = inventory.Lots.Where(lot => ToolProgressionRules.Find(lot.ItemKind) is null).ToArray() };
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "blocked", $"gather stone from {source.Id}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        Assert.Equal(0, CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits);
        Assert.Equal(1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        Assert.Contains(full ? "Carrying" : "tool", CancellationOrder(world.ExportState(), receipt.InstructionId).BlockedReason, StringComparison.Ordinal);
        var blocked = world.ExportState();
        inventory = blocked.Society.Society.Inventory;
        if (full) inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.Id != "material-cargo").ToArray() };
        if (!withTool) inventory = InventoryFixture.AddLot(inventory, "material-order-tool", "wooden_pickaxe", HarvestInstructionActor, 1);
        blocked = blocked with { Society = blocked.Society with { Society = blocked.Society.Society with { Inventory = inventory } } };
        using var restored = RestoreMaterialOrderWorld(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(blocked)));
        for (var tick = 0; tick < 5 && CancellationOrder(restored.ExportState(), receipt.InstructionId).Status != "finished"; tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", CancellationOrder(restored.ExportState(), receipt.InstructionId).Status);
        restored.Validate();
    }

    [Fact]
    public async Task MaterialOrderRepeatsOnlyUntilCancelledAcrossReload()
    {
        var (state, source) = MaterialOrderState("fiber", quantity: 3);
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "repeat", $"keep gathering fiber from {source.Id} until cancelled");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(("doing", 2), (CancellationOrder(world.ExportState(), receipt.InstructionId).Status,
            CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits));
        CancelTravelOrder(world, receipt.InstructionId, "cancel-materials");
        using var restored = RestoreMaterialOrderWorld(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        for (var tick = 0; tick < 3; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("cancelled", CancellationOrder(restored.ExportState(), receipt.InstructionId).Status);
        Assert.Equal(1, restored.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        restored.Validate();
    }

    [Fact]
    public void MaterialOrderRejectsDamagedSavedTargetsAndProgress()
    {
        var (state, _) = MaterialOrderState("fiber");
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "validation", "gather two fiber");
        var saved = world.ExportState();
        var order = CancellationOrder(saved, receipt.InstructionId);
        foreach (var invalid in new[] { order with { TargetMaterialKind = null }, order with { TargetMaterialKind = "cloth" },
                     order with { TargetFoodKind = "berries" }, order with { TargetAgentId = HarvestInstructionActor },
                     order with { ProgressUnit = "food_items" },
                     order with { LastEffectId = "gather:material:fake" }, order with { CompletedUnits = 1 } })
        {
            var damaged = saved with
            {
                Instructions = saved.Instructions!.Select(item => item.InstructionId == receipt.InstructionId
                ? item with { Order = invalid } : item).ToArray()
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(damaged)));
        }
    }

    [Fact]
    public async Task MaterialOrderToolCollectionDoesNotCountAsGatheredGoods()
    {
        var (state, source) = MaterialOrderState("stone");
        var household = state.Society.Society.GetInhabitant(HarvestInstructionActor).HouseholdId!;
        var position = CancellationActorPosition(state);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "material-shared-tool", "wooden_pickaxe", household, 1,
            groundPosition: new InventoryGroundPosition(position.X, position.Y));
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "shared-tool", $"gather stone from {source.Id}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("doing", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        var tool = world.Society.Inventory.GetLot("material-shared-tool");
        Assert.Equal(household, tool.OwnerId);
        Assert.Equal(HarvestInstructionActor, tool.CarrierId);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "equipment_collected" &&
            item.Detail == HarvestInstructionActor + ":wooden_pickaxe");
        Assert.Equal(0, CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits);
        Assert.Equal(1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == HarvestInstructionActor && lot.ItemKind == "stone");
        world.Validate();
    }

    [Fact]
    public async Task MaterialOrderResumesAfterUrgentEating()
    {
        var (state, source) = MaterialOrderState("fiber");
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                ? person with { HungerBasisPoints = 1_000 } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "material-urgent-food", "fruit", HarvestInstructionActor, 1),
                }
            },
        };
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "survival", $"gather fiber from {source.Id}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("interrupted", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        Assert.Equal(1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "material-urgent-food");
        for (var tick = 0; tick < 5 && CancellationOrder(world.ExportState(), receipt.InstructionId).Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        world.Validate();
    }

    [Fact]
    public async Task MaterialOrderUsesOnePersonalDecisionAcrossRepeatedHarvests()
    {
        var (state, source) = MaterialOrderState("fiber", quantity: 3);
        var provider = new CancellationPlanningProvider(orderCandidate: "gather_material");
        using var world = PrivateWorldRuntime.Restore(state, CancellationProviderFactory(provider));
        var receipt = SubmitMaterialOrder(world, "personal", $"keep gathering fiber from {source.Id}");
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(3, CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits);
        var request = Assert.Single(provider.Requests, item => item.OperativeOrderInstructionId == receipt.InstructionId);
        Assert.Contains(request.ObserverGuidance!, guidance => guidance.InstructionId == receipt.InstructionId &&
            guidance.UnderstoodTask == "gather the requested material from a natural source");
        world.Validate();
    }

    [Fact]
    public async Task MaterialOrderCancelledWhileAReplyIsHeldCannotHarvestAgain()
    {
        var (state, source) = MaterialOrderState("fiber", quantity: 3);
        var provider = new CancellationPlanningProvider(holdFirstOrder: true, orderCandidate: "gather_material");
        using var world = PrivateWorldRuntime.Restore(state, CancellationProviderFactory(provider));
        var receipt = SubmitMaterialOrder(world, "held", $"keep gathering fiber from {source.Id}");
        try
        {
            // The tick dispatches the call itself; only the thread-pool start of the
            // provider is left to scheduling, which a loaded parallel run can delay.
            var deadline = TimeSpan.FromSeconds(10);
            var dispatch = await world.AdvanceOneTickNonBlockingAsync();
            Assert.True(dispatch.Advanced);
            Assert.Contains(dispatch.Events, item => item.Kind == "hosted_decision_started" && item.Detail == HarvestInstructionActor);
            await provider.Started.Task.WaitAsync(deadline);
            Assert.Equal(1, CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits);
            CancelTravelOrder(world, receipt.InstructionId, "cancel-held-material");
            provider.Release.TrySetResult(true);
            await provider.Returned.Task.WaitAsync(deadline);
            for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            Assert.Equal("cancelled", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
            Assert.Equal(2, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
            Assert.Single(world.ExportState().Events, item => item.Kind == "material_gathered" &&
                item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal));
            world.Validate();
        }
        finally { provider.Release.TrySetResult(true); }
    }

    [Fact]
    public async Task MaterialOrderWithoutKnownSitesExploresWithoutRemoteKnowledge()
    {
        var (state, source) = MaterialOrderState("clay", distant: true);
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != HarvestInstructionActor)
            .Select(person => person.Position).ToHashSet();
        var position = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsPassable(point) &&
            !occupied.Contains(point) && state.Map.FootDistance(point, source.Position) is >= 5 and <= 8 &&
            !state.Map.Resources.Any(item => item.Kind == "clay" && state.Map.FootDistance(point, item.Position) <= 2));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
            ? person with { Position = position } : person).ToArray()
        };
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "explore-materials", "gather clay");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "exploration_started" &&
            item.Detail.StartsWith(HarvestInstructionActor + ":", StringComparison.Ordinal));
        Assert.Equal(0, CancellationOrder(world.ExportState(), receipt.InstructionId).CompletedUnits);
        Assert.DoesNotContain(world.ExportState().Knowledge!.Facts,
            fact => fact.OwnerId == HarvestInstructionActor && fact.Position == source.Position);
        Assert.Equal(1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        world.Validate();
    }

    [Fact]
    public async Task MaterialOrderDoesNotGiveChildrenAdultGatheringWork()
    {
        var (state, source) = MaterialOrderState("fiber");
        var checkpoint = state.Society.Society;
        var birth = checkpoint.LifeTickAt(checkpoint.WorldTick) - 4 * checkpoint.Config.TicksPerLifecycleAge;
        state = state with
        {
            Society = state.Society with
            {
                Society = checkpoint with
                {
                    Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == HarvestInstructionActor ? person with
                    {
                        BirthTick = birth,
                        BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                        AgeBand = SocietyAgeBand.Child,
                        LastLifecycleYearChecked = 4,
                        CurrentRole = SocietyWorkRole.Unassigned,
                    } : person).ToArray(),
                }
            },
            Towns = state.Towns!.Select(town => town with
            {
                Governance = TownGovernanceState.Create(town.ResidentIds.Where(id => id != HarvestInstructionActor &&
                    checkpoint.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)),
            }).ToArray(),
        };
        using var world = RestoreMaterialOrderWorld(state);
        var receipt = SubmitMaterialOrder(world, "child-materials", $"gather fiber from {source.Id}");
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("blocked", CancellationOrder(world.ExportState(), receipt.InstructionId).Status);
        Assert.Contains("too young", CancellationOrder(world.ExportState(), receipt.InstructionId).BlockedReason, StringComparison.Ordinal);
        Assert.Equal(1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        world.Validate();
    }

    private static OwnerInstructionReceipt SubmitMaterialOrder(PrivateWorldRuntime world, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new OwnerInstructionRequest(key, "owner:test", HarvestInstructionActor, OwnerInstructionKind.MustDo, text, queue));

    private static PrivateWorldRuntime RestoreMaterialOrderWorld(PrivateWorldRuntimeState state) =>
        PrivateWorldRuntime.Restore(state, _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));

    private static (PrivateWorldRuntimeState State, MapResource Source) MaterialOrderState(
        string kind, string? tool = null, int quantity = 1, bool distant = false)
    {
        using var setup = CreateKnownBerryOrderWorld(_ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var state = setup.ExportState();
        var buildings = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            setup.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != HarvestInstructionActor).Select(person => person.Position).ToHashSet();
        var source = state.Map.Resources.Where(item => (item.Kind == kind || kind == "wood" && item.Kind == "construction") &&
                (kind != "wood" || TreeGrowthRules.IsWoodTree(item.TreeKind)) && state.Map.IsPassable(item.Position) &&
                state.Map.FootNeighbors(item.Position).Any(point => state.Map.IsPassable(point) && !buildings.Contains(point) && !occupied.Contains(point)))
            .OrderBy(item => item.Id, StringComparer.Ordinal).First();
        var stand = distant ? state.Map.Tiles.Select(tile => tile.Position).Where(point => state.Map.IsPassable(point) &&
                !buildings.Contains(point) && !occupied.Contains(point) && state.Map.FootDistance(point, source.Position) is >= 5 and <= 7)
            .First(point => DeterministicRouteFinder.TryFind(state.Map, point, source.Position, out var route) && route.All(step => !occupied.Contains(step)))
            : state.Map.FootNeighbors(source.Position).First(point => state.Map.IsPassable(point) && !buildings.Contains(point) && !occupied.Contains(point));
        var inventory = state.Society.Society.Inventory with { Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != HarvestInstructionActor).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "material-order-basket", "basket", HarvestInstructionActor, 1);
        if (tool is not null) inventory = InventoryFixture.AddLot(inventory, "material-order-tool", tool, HarvestInstructionActor, 1);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor ? person with
            {
                Position = stand,
                HungerBasisPoints = 10_000,
                TravelCooldownTicks = 0,
                Project = null,
                Equipment = new(CarryAidLotId: "material-order-basket"),
                Exploration = null,
            } : person).ToArray(),
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Knowledge = state.Knowledge! with { Facts = state.Knowledge.Facts.Where(fact => fact.OwnerId != HarvestInstructionActor).ToArray() },
            Resources = state.Resources.Select(resource => resource.ResourceId == source.Id ? resource with { State = ResourceState.Available } : resource).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == source.Id
                        ? resource with { Quantity = quantity, State = EcologyResourceState.Available } : resource).ToArray(),
                }
            },
        };
        return (state, source);
    }
}
