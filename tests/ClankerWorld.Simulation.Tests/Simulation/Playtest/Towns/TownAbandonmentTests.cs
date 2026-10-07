using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;
using System.Text.Json;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownMembershipTests
{
    [Fact]
    public async Task ResettlementMovesTheCareGroupAndKeepsTheSameTownPropertyLawsAndHistory()
    {
        var caregiver = Founders[0];
        var (state, children) = WithChildren(Generated("town-resettlement-care"), caregiver, Founders[1], 2);
        state = WithTowns(state, [caregiver, .. children], []);
        state = WithAbandonedLaw(state);
        var original = Town(state, Second);
        var model = new ScriptedModel();
        model.Scripts[caregiver] = [Civic(Second, "resettle")];
        state = Calm(At(state, original.OriginSite!.Value, caregiver));
        var physical = state.Inhabitants.ToDictionary(person => person.InhabitantId, person => person.Position);
        var buildings = state.WorldSimulation!.Buildings.ToArray();
        var stock = state.Society.Society.Inventory.Lots.ToArray();
        var rights = state.HouseholdLandUseRights;
        var titles = state.TownLandTitles;
        using var world = Reopen(state, model);
        await AdvanceUntil(world, () => world.Towns.Single(town => town.Id == Second).ResidentIds.Contains(caregiver), 12);

        var revived = world.Towns.Single(town => town.Id == Second);
        Assert.Equal(new[] { caregiver }.Concat(children).Order(StringComparer.Ordinal), revived.ResidentIds);
        Assert.Empty(world.Towns.Single(town => town.Id == First).ResidentIds);
        Assert.Equal([caregiver], revived.Governance!.Members);
        Assert.Equal(original.Id, revived.Id);
        Assert.Equal(original.Name, revived.Name);
        Assert.Equal(original.FoundedTick, revived.FoundedTick);
        Assert.Equal(original.BorderTiles, revived.BorderTiles);
        Assert.Equal(original.AssignedBuildingIds, revived.AssignedBuildingIds);
        Assert.Equal(JsonSerializer.Serialize(original.Government!.Laws), JsonSerializer.Serialize(revived.Government!.Laws));
        Assert.Equal(JsonSerializer.Serialize(buildings), JsonSerializer.Serialize(world.WorldSimulation.Buildings));
        Assert.Equal(JsonSerializer.Serialize(rights), JsonSerializer.Serialize(world.ExportState().HouseholdLandUseRights));
        Assert.Equal(JsonSerializer.Serialize(titles), JsonSerializer.Serialize(world.ExportState().TownLandTitles));
        Assert.Equal(stock.Select(lot => (lot.Id, lot.OwnerId, lot.Quantity, lot.StorageBuildingId)),
            world.Society.Inventory.Lots.Select(lot => (lot.Id, lot.OwnerId, lot.Quantity, lot.StorageBuildingId)));
        foreach (var child in children)
        {
            var person = world.Society.GetInhabitant(child);
            Assert.Equal((Alpha, caregiver), (person.HouseholdId, person.PrimaryCaregiverId));
            Assert.Equal(physical[child], world.Inhabitants.Single(item => item.InhabitantId == child).Position);
        }
        Assert.DoesNotContain(revived.Governance.Knowledge, receipt => receipt.AgentId == caregiver);
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_resettled");
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_revived");
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_abandoned" && item.Detail == First);
        AssertRoundTrip(world);

        // The old explicit choice cannot run again. Later visitors need the restored council's approval.
        var returned = Calm(At(world.ExportState(), revived.OriginSite!.Value, Founders[2]));
        using var restored = Reopen(returned, model);
        for (var tick = 0; tick < 6; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "town_resettled");
        Assert.DoesNotContain(Founders[2], restored.Towns.Single(town => town.Id == Second).ResidentIds);
        Assert.Contains(model.ObservationsOf(Founders[2]), observation => observation.Candidates.Any(candidate =>
            candidate.Id == Civic(Second, "admission") + "|"));
        Assert.DoesNotContain(model.ObservationsOf(Founders[2]), observation => observation.Candidates.Any(candidate =>
            candidate.Id == Civic(Second, "resettle") + "|"));
        AssertRoundTrip(restored);
    }

    [Fact]
    public async Task LastResidentDeathMarksAbandonmentOnceAndPreservesBuildingsBordersAndRoads()
    {
        var actor = Founders[0];
        var state = Calm(AtNaturalLifeBoundary(WithTowns(Generated("town-abandon-last-death"), [actor], []), actor));
        var original = Town(state, First);
        var buildings = state.WorldSimulation!.Buildings.ToArray();
        var roads = state.RoadTiles;
        using var world = Reopen(state, new ScriptedModel());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(world.Towns.Single(town => town.Id == First).ResidentIds);
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_abandoned" && item.Detail == First);
        Assert.Equal(original.BorderTiles, world.Towns.Single(town => town.Id == First).BorderTiles);
        Assert.Equal(original.AssignedBuildingIds, world.Towns.Single(town => town.Id == First).AssignedBuildingIds);
        Assert.Equal(JsonSerializer.Serialize(buildings), JsonSerializer.Serialize(world.WorldSimulation.Buildings));
        Assert.Equal(roads, world.ExportState().RoadTiles);
        AssertRoundTrip(world);
        using var restored = Reopen(world.ExportState(), new ScriptedModel());
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "town_abandoned" && item.Detail == First);
    }

    [Fact]
    public async Task SalvageCollectsOnlyPhysicalUnreservedTownStockWithinCarryingSpaceWithoutJoining()
    {
        var actor = Founders[0];
        var state = WithTowns(Generated("town-public-salvage"), Founders, []);
        var ground = Board(state, Second);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "abandoned-wood", "wood", Second, 7,
            conditionBasisPoints: 7_600, groundPosition: new(ground.X, ground.Y));
        inventory = InventoryFixture.Reserve(inventory, "salvage-reserved", Second, "abandoned-wood", 2,
            "saved-town-work", 100);
        inventory = InventoryFixture.AddLot(inventory, "private-abandoned-wood", "wood", Alpha, 3,
            groundPosition: new(ground.X, ground.Y));
        inventory = InventoryFixture.AddLot(inventory, "already-carried", "stone", actor, 5);
        var model = new ScriptedModel();
        model.Scripts[actor] = ["town_salvage:"];
        using var world = Reopen(Calm(At(WithInventory(state, inventory), ground, actor)), model);
        await AdvanceUntil(world, () => world.ExportState().Events.Any(item => item.Kind == "town_stock_salvaged"), 12);

        var carried = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "wood");
        Assert.Equal(3, carried.Quantity);
        Assert.Equal(7_600, carried.ConditionBasisPoints);
        Assert.Null(carried.GroundPosition);
        Assert.Null(carried.StorageBuildingId);
        Assert.Null(carried.DeliveryBuildingId);
        Assert.Equal(4, world.Society.Inventory.GetLot("abandoned-wood").Quantity);
        Assert.Equal(2, world.Society.Inventory.GetReservation("salvage-reserved").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("salvage-reserved").State);
        Assert.Equal((Alpha, 3), (world.Society.Inventory.GetLot("private-abandoned-wood").OwnerId,
            world.Society.Inventory.GetLot("private-abandoned-wood").Quantity));
        Assert.Contains(actor, world.Towns.Single(town => town.Id == First).ResidentIds);
        Assert.Empty(world.Towns.Single(town => town.Id == Second).ResidentIds);
        Assert.Empty(world.Towns.Single(town => town.Id == Second).Governance!.Members);
        AssertRoundTrip(world);
        using var restored = Reopen(world.ExportState(), model);
        for (var tick = 0; tick < 4; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "town_stock_salvaged");
        Assert.Equal(7, restored.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" &&
            (lot.OwnerId == Second || lot.OwnerId == actor)).Sum(lot => lot.Quantity));
        var settling = new ScriptedModel();
        settling.Scripts[Founders[1]] = [Civic(Second, "resettle")];
        settling.Scripts[Founders[2]] = ["town_salvage:"];
        using var revived = Reopen(Calm(At(restored.ExportState(), ground, Founders[1], Founders[2])), settling);
        await AdvanceUntil(revived, () => !revived.Towns.Single(town => town.Id == Second).IsAbandoned, 8);
        Assert.Equal((actor, 3), (revived.Society.Inventory.GetLot(carried.Id).OwnerId, revived.Society.Inventory.GetLot(carried.Id).Quantity));
        Assert.Equal((Second, 4), (revived.Society.Inventory.GetLot("abandoned-wood").OwnerId,
            revived.Society.Inventory.GetLot("abandoned-wood").Quantity));
        Assert.Single(revived.ExportState().Events, item => item.Kind == "town_stock_salvaged");
        AssertRoundTrip(revived);
    }

    [Fact]
    public async Task ChildrenStillCountWhenTheirLastAdultResidentDies()
    {
        var caregiver = Founders[0];
        var (state, children) = WithChildren(Generated("town-child-residents"), caregiver, Founders[1], 1);
        var child = children[0];
        state = WithTowns(state, [caregiver, child], []);
        using var world = Reopen(Calm(AtNaturalLifeBoundary(state, caregiver)), new ScriptedModel());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var town = world.Towns.Single(item => item.Id == First);
        Assert.Equal([child], town.ResidentIds);
        Assert.False(town.IsAbandoned);
        Assert.False(new OwnerWorldObservationStore(world).GetSnapshot().Towns.Single(item => item.Id == First).IsAbandoned);
        Assert.Empty(town.Governance!.Members);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "town_abandoned" && item.Detail == First);
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task AnUnhousedResidentAwayFromTownPreventsResettlementAndPublicSalvage()
    {
        var visitor = Founders[0];
        var resident = Founders[2];
        var state = WithTowns(Generated("town-unhoused-traveler"), [visitor], [resident]);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "occupied-town-stock", "gold", Second, 1,
            groundPosition: new(Board(state, Second).X, Board(state, Second).Y));
        // The resident is physically in the first Town and has no House or household.
        var departure = SocietyFixture.LeaveHousehold(state.Society.Society, resident);
        Assert.Null(departure.Checkpoint.GetInhabitant(resident).HouseholdId);
        var society = departure.Checkpoint;
        state = state with { Society = state.Society with { Society = society } };
        inventory = inventory with { Lots = inventory.Lots.Where(lot => lot.OwnerId != resident).ToArray() };
        state = At(At(WithInventory(state, inventory), Board(state, First), resident), Board(state, Second), visitor);
        var model = new ScriptedModel();
        model.Scripts[visitor] = ["!" + Civic(Second, "resettle") + "|"];
        using var world = Reopen(Calm(state), model);
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.False(world.Towns.Single(town => town.Id == Second).IsAbandoned);
        Assert.Equal([resident], world.Towns.Single(town => town.Id == Second).ResidentIds);
        Assert.Equal(Second, world.Society.Inventory.GetLot("occupied-town-stock").OwnerId);
        Assert.DoesNotContain(model.ObservationsOf(visitor), observation => observation.Candidates.Any(candidate =>
            candidate.Id == Civic(Second, "resettle") + "|" || candidate.Id.StartsWith("town_salvage:", StringComparison.Ordinal)));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind is "town_resettled" or "town_stock_salvaged");
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task CompetingSalvagersSplitOnlyTheAvailableGoodsAndKeepIndependentTransferReceipts()
    {
        var first = Founders[0];
        var second = Founders[1];
        var state = WithTowns(Generated("town-competing-salvage"), Founders, []);
        var position = Board(state, Second);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "competing-stock", "wood", Second, 4,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.Reserve(inventory, "competing-reservation", Second, "competing-stock", 1, "saved-town-work", 100);
        inventory = InventoryFixture.AddLot(inventory, "first-load", "stone", first, 7);
        inventory = InventoryFixture.AddLot(inventory, "second-load", "stone", second, 6);
        var model = new ScriptedModel();
        model.Scripts[first] = model.Scripts[second] = ["town_salvage:"];
        using var world = Reopen(Calm(At(WithInventory(state, inventory), position, first, second)), model);
        await AdvanceUntil(world, () => world.ExportState().Events.Count(item => item.Kind == "town_stock_salvaged") == 2, 12);
        Assert.Equal(1, Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == first && lot.ItemKind == "wood").Quantity);
        Assert.Equal(2, Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == second && lot.ItemKind == "wood").Quantity);
        Assert.Equal(1, world.Society.Inventory.GetLot("competing-stock").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("competing-reservation").State);
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood" &&
            (lot.OwnerId == Second || lot.OwnerId == first || lot.OwnerId == second)).Sum(lot => lot.Quantity));
        AssertRoundTrip(world);
        using var restored = Reopen(world.ExportState(), model);
        for (var tick = 0; tick < 3; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(2, restored.ExportState().Events.Count(item => item.Kind == "town_stock_salvaged"));
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(5, false, false)]
    [InlineData(0, true, false)]
    public async Task SalvagedVesselsMoveWithAllTheirContentsOnlyWhenTheWholeLoadIsUnreservedAndFits(
        int carried, bool reserved, bool collected)
    {
        var actor = Founders[0];
        var state = WithTowns(Generated("town-vessel-salvage"), Founders, []);
        var position = Board(state, Second);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "salvage-jug", "water_jug", Second, 1,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "salvage-water", "fresh_water", Second, 3, containerLotId: "salvage-jug");
        if (carried > 0) inventory = InventoryFixture.AddLot(inventory, "vessel-load", "stone", actor, carried);
        if (reserved) inventory = InventoryFixture.Reserve(inventory, "water-reservation", Second, "salvage-water", 1, "saved-town-work", 100);
        var model = new ScriptedModel();
        model.Scripts[actor] = ["town_salvage:"];
        using var world = Reopen(Calm(At(WithInventory(state, inventory), position, actor)), model);
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(collected ? actor : Second, world.Society.Inventory.GetLot("salvage-jug").OwnerId);
        Assert.Equal(collected ? actor : Second, world.Society.Inventory.GetLot("salvage-water").OwnerId);
        Assert.Equal(3, world.Society.Inventory.GetLot("salvage-water").Quantity);
        Assert.Equal("salvage-jug", world.Society.Inventory.GetLot("salvage-water").ContainerLotId);
        Assert.Equal(collected, world.ExportState().Events.Any(item => item.Kind == "town_stock_salvaged"));
        if (reserved) Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("water-reservation").State);
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task ADelayedSalvageReplyCannotCollectAfterAnotherAdultExplicitlyRevivesTheTown()
    {
        var visitor = Founders[0];
        var settler = Founders[1];
        var state = WithTowns(Generated("town-delayed-salvage"), Founders, []);
        var position = Board(state, Second);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "delayed-stock", "wood", Second, 4,
            groundPosition: new(position.X, position.Y));
        var model = new DeferredTownSalvageModel(visitor);
        model.OtherChoices.Scripts[settler] = [Civic(Second, "resettle")];
        using var world = Reopen(Calm(At(WithInventory(state, inventory), position, visitor, settler)), model);
        var deadline = TimeSpan.FromSeconds(10);
        for (var tick = 0; tick < 12 && (model.Pending is null ||
                 !world.Towns.Single(town => town.Id == Second).ResidentIds.Contains(settler)); tick++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(deadline)).Advanced);
            await Task.Delay(10);
        }
        Assert.NotNull(model.Pending);
        Assert.Contains(settler, world.Towns.Single(town => town.Id == Second).ResidentIds);
        Assert.Contains(model.Pending!.Observation.Candidates, candidate => candidate.Id.StartsWith("town_salvage:", StringComparison.Ordinal));
        model.Resolve();
        var completed = false;
        for (var tick = 0; tick < 10 && !completed; tick++)
        {
            await Task.Delay(10);
            var step = await world.AdvanceOneTickNonBlockingAsync().AsTask().WaitAsync(deadline);
            Assert.True(step.Advanced);
            completed = step.Decisions.Any(decision => decision.InhabitantId == visitor && decision.Admission.FellBack) ||
                step.Events.Any(item => item.Kind == "hosted_decision_discarded" && item.Detail == visitor);
        }
        Assert.True(completed, "The host must process and refuse the previously valid salvage reply after revival.");
        Assert.Equal((Second, 4), (world.Society.Inventory.GetLot("delayed-stock").OwnerId,
            world.Society.Inventory.GetLot("delayed-stock").Quantity));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "town_stock_salvaged");
        Assert.Equal([settler], world.Towns.Single(town => town.Id == Second).ResidentIds);
        Assert.Contains(visitor, world.Towns.Single(town => town.Id == First).ResidentIds);
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task StandaloneWarehouseSalvageKeepsPrivateStoredGoodsAndTownMembershipIntact()
    {
        var actor = Founders[0];
        var state = WithTowns(Generated("town-warehouse-salvage"), [], Founders);
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != First).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "warehouse-salvage", "cloth", First, 3,
            storageBuildingId: warehouse.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "private-stored-cloth", "cloth", Alpha, 2,
            storageBuildingId: "first-town-house-a");
        var model = new ScriptedModel();
        model.Scripts[actor] = ["town_salvage:"];
        using var world = Reopen(Calm(At(WithInventory(state, inventory), warehouse.Position, actor)), model);
        await AdvanceUntil(world, () => world.ExportState().Events.Any(item => item.Kind == "town_stock_salvaged"), 8);
        var pickedUp = world.Society.Inventory.GetLot("warehouse-salvage");
        Assert.Equal((actor, 3), (pickedUp.OwnerId, pickedUp.Quantity));
        Assert.Null(pickedUp.StorageBuildingId);
        Assert.Equal(Alpha, world.Society.Inventory.GetLot("private-stored-cloth").OwnerId);
        Assert.Contains(actor, world.Towns.Single(town => town.Id == Second).ResidentIds);
        Assert.True(world.Towns.Single(town => town.Id == First).IsAbandoned);
        AssertRoundTrip(world);
    }

    [Theory]
    [InlineData("resettle", "town_resettled")]
    [InlineData("salvage", "town_stock_salvaged")]
    public async Task AbandonmentChoicesRollBackAndReplayExactlyAcrossThePickupOrMembershipBoundary(string action, string eventKind)
    {
        var actor = Founders[0];
        var state = WithTowns(Generated("town-abandonment-replay-" + action), [actor], []);
        var position = Board(state, Second);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "replay-stock", "wood", Second, 3,
            groundPosition: new(position.X, position.Y));
        ScriptedModel Policy()
        {
            var provider = new ScriptedModel();
            provider.Scripts[actor] = [action == "resettle" ? Civic(Second, "resettle") : "town_salvage:"];
            return provider;
        }
        using var world = Reopen(Calm(At(WithInventory(state, inventory), position, actor)), Policy());
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using (var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), _ => Policy(), maxCognitionDispatchPerCycle: 8))
        {
            for (var tick = 0; tick < 3; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
        }
        Assert.Single(world.ExportState().Events, item => item.Kind == eventKind);
        var after = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var continued = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(after), _ => Policy(), maxCognitionDispatchPerCycle: 8);
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await continued.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(continued.ExportState()));
        }
        Assert.Single(continued.ExportState().Events, item => item.Kind == eventKind);
    }

    [Fact]
    public async Task AFormerLawCanBeReadAndRepealedByTheNewCouncilAfterResettlement()
    {
        var actor = Founders[0];
        var state = WithAbandonedLaw(WithTowns(ShortDays(Generated("town-resettled-law"), 30), [actor], []));
        var model = new ScriptedModel();
        model.Scripts[actor] = [Civic(Second, "resettle"), Civic(Second, "read"), Civic(Second, "yes"), Civic(Second, "repeal")];
        using var world = Reopen(Calm(At(state, Board(state, Second), actor)), model);
        await AdvanceUntil(world, () => world.Towns.Single(town => town.Id == Second).Governance!.Proposals.Any(proposal => proposal.AuthorId == actor), 8);
        // A new owner suggestion opens another personal turn; proposing never casts an automatic vote.
        world.SubmitInstruction(new("consider-repeal-vote", "owner:test", actor, OwnerInstructionKind.Suggestive,
            "Consider your vote on the repeal you proposed."));
        await AdvanceUntil(world, () => world.Towns.Single(town => town.Id == Second).Government!.Laws[0].Versions[^1].EndedTick is not null, 12);
        var town = world.Towns.Single(item => item.Id == Second);
        var law = Assert.Single(town.Government!.Laws);
        Assert.Equal("Grove", law.Versions[0].Subject);
        Assert.NotNull(law.Versions[0].EndedByProposalId);
        var repeal = town.Governance!.Proposals.Single(proposal => proposal.Id == law.Versions[0].EndedByProposalId);
        Assert.Equal("passed", repeal.Status);
        Assert.Equal([actor], repeal.Voters);
        Assert.Equal([actor], town.Governance.Members);
        Assert.Empty(town.Government.Offices);
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task RepeatedResettlementRevivesTheSameTownRecordsAndRecordsEachRealRosterTransitionOnce()
    {
        var actor = Founders[0];
        var state = WithTowns(Generated("town-repeated-revival"), [actor], []);
        var original = state.Towns!.Select(town => (town.Id, town.Name, town.BorderTiles)).ToArray();
        foreach (var destination in new[] { Second, First, Second })
        {
            var model = new ScriptedModel();
            model.Scripts[actor] = [Civic(destination, "resettle")];
            using var world = Reopen(Calm(At(state, Board(state, destination), actor)), model);
            await AdvanceUntil(world, () => world.Towns.Single(town => town.Id == destination).ResidentIds.Contains(actor), 8);
            Assert.Equal([actor], world.Towns.Single(town => town.Id == destination).Governance!.Members);
            Assert.Empty(world.Towns.Single(town => town.Id != destination).ResidentIds);
            AssertRoundTrip(world);
            state = world.ExportState();
        }
        Assert.Equal(2, state.Towns!.Count);
        foreach (var (id, name, border) in original)
        {
            Assert.Equal(name, Town(state, id).Name);
            Assert.Equal(border, Town(state, id).BorderTiles);
        }
        foreach (var kind in new[] { "town_resettled", "town_revived", "town_abandoned" })
            Assert.Equal(3, state.Events.Count(item => item.Kind == kind));
    }

    [Fact]
    public async Task TwoAdultsChoosingTheFirstResidentExceptionProduceOnlyOneSettlerAndCouncil()
    {
        var state = WithTowns(Generated("town-delayed-salvage"), Founders, []);
        var model = new ScriptedModel();
        foreach (var actor in Founders[..2]) model.Scripts[actor] = [Civic(Second, "resettle")];
        using var world = Reopen(Calm(At(state, Board(state, Second), Founders[..2])), model);
        for (var tick = 0; tick < 4; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var town = world.Towns.Single(item => item.Id == Second);
        var settler = Assert.Single(town.ResidentIds);
        Assert.Contains(settler, Founders[..2]);
        Assert.Equal([settler], town.Governance!.Members);
        Assert.Contains(Founders.First(actor => actor != settler), world.Towns.Single(item => item.Id == First).ResidentIds);
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_resettled");
        Assert.Single(world.ExportState().Events, item => item.Kind == "town_revived");
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task AnInventedRemoteResettlementChoiceCannotCreateResidence()
    {
        var actor = Founders[0];
        var state = WithTowns(Generated("town-remote-settler"), Founders, []);
        var model = new ScriptedModel();
        model.Scripts[actor] = ["!" + Civic(Second, "resettle") + "|"];
        using var world = Reopen(Calm(At(state, Board(state, First), actor)), model);
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, model.InventionsUsed(actor));
        Assert.True(world.Towns.Single(town => town.Id == Second).IsAbandoned);
        Assert.Contains(actor, world.Towns.Single(town => town.Id == First).ResidentIds);
        Assert.DoesNotContain(model.ObservationsOf(actor), observation => observation.Candidates.Any(candidate =>
            candidate.Id == Civic(Second, "resettle") + "|"));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "town_resettled");
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task SalvagingAHandcartPreservesItsPhysicalLocationAndCargoUntilSomeonePullsIt()
    {
        var actor = Founders[0];
        var state = WithTowns(Generated("town-cart-salvage"), Founders, []);
        var position = Board(state, Second);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "salvage-cart", "handcart", Second, 1,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "cart-cargo", "wood", Second, 12, containerLotId: "salvage-cart");
        inventory = InventoryFixture.AddLot(inventory, "full-carrier", "stone", actor, 8);
        var model = new ScriptedModel();
        model.Scripts[actor] = ["town_salvage:"];
        using var world = Reopen(Calm(At(WithInventory(state, inventory), position, actor)), model);
        await AdvanceUntil(world, () => world.ExportState().Events.Any(item => item.Kind == "town_stock_salvaged"), 8);
        var cart = world.Society.Inventory.GetLot("salvage-cart");
        Assert.Equal(actor, cart.OwnerId);
        Assert.Equal(new InventoryGroundPosition(position.X, position.Y), cart.GroundPosition);
        Assert.Null(cart.CarrierId);
        Assert.Equal((actor, 12, cart.Id), (world.Society.Inventory.GetLot("cart-cargo").OwnerId,
            world.Society.Inventory.GetLot("cart-cargo").Quantity, world.Society.Inventory.GetLot("cart-cargo").ContainerLotId));
        AssertRoundTrip(world);
    }

    private sealed class DeferredTownSalvageModel(string actor) : IDecisionProvider
    {
        private readonly TaskCompletionSource<CognitionDecisionResponse> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ScriptedModel OtherChoices { get; } = new();
        public CognitionDecisionRequest? Pending { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Observation.InhabitantId != actor || Pending is not null ||
                !request.Observation.Candidates.Any(candidate => candidate.Id.StartsWith("town_salvage:", StringComparison.Ordinal)))
                return OtherChoices.DecideAsync(request, cancellationToken);
            Pending = request;
            return new(completion.Task.WaitAsync(cancellationToken));
        }
        public void Resolve()
        {
            var request = Pending!;
            var observation = request.Observation;
            var selected = observation.Candidates.Single(candidate => candidate.Id.StartsWith("town_salvage:", StringComparison.Ordinal)).Id;
            var scores = observation.Candidates.ToDictionary(candidate => candidate.Id, _ => 0d, StringComparer.Ordinal);
            scores[selected] = 1;
            completion.TrySetResult(new(request.RequestId, actor, Kind, ProviderEpoch, observation.RunEpoch,
                observation.DecisionGeneration, observation.ObservationDigest, selected, 1, scores));
        }
    }

    private static PrivateWorldRuntimeState WithAbandonedLaw(PrivateWorldRuntimeState state)
    {
        var town = Town(state, Second);
        var former = Founders[3];
        var council = TownGovernanceState.Create([former]);
        var (proposed, government) = TownLawRules.ProposeAdoption(council, town.Government!, Second, former,
            "Grove: Leave the saplings.", TownLawRules.Jurisdiction, [], [former], 0, state.WorldSystems!.Config.TicksPerDay);
        proposed = TownGovernanceRules.VoteProposal(proposed, proposed.Proposals[^1].Id, former, true, 0);
        (proposed, government) = TownGovernmentRules.Advance(proposed, government, Second, town.Name, state.WorldSeed,
            [former], 0, state.WorldSystems.Config.TicksPerDay);
        Assert.Single(government.Laws);
        (proposed, government) = TownGovernmentRules.Advance(proposed, government, Second, town.Name, state.WorldSeed,
            [], 0, state.WorldSystems.Config.TicksPerDay);
        return state with
        {
            Towns = state.Towns!.Select(item => item.Id == Second
            ? item with { Governance = proposed, Government = government } : item).ToArray()
        };
    }
}
