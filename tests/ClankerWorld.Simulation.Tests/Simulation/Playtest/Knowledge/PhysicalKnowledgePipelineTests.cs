using System.Collections.Concurrent;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class PhysicalKnowledgePipelineTests
{
    [Fact]
    public async Task BuiltInChoicesGatherAndDeliverPaperInputsForRequestedWriting()
    {
        const string household = "household:camp-alpha";
        const string houseId = "first-town-house-a";
        using var setup = NormalPathWorld.CreateGenerated("physical-paper-house", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        // Unrelated starting wood would fill this one-tile House while the
        // writer fetches its jug. Leave space for the actual paper inputs.
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != household || lot.ItemKind != "wood").ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "paper-water-jug", InventoryContainerRules.WaterJug,
            household, 1, state.Society.Society.WorldTick, storageBuildingId: houseId);
        Assert.DoesNotContain(inventory.Lots, lot => lot.OwnerId == household && lot.ItemKind is "fiber" or "fresh_water" or "paper");
        var provider = new ChoosingProvider(actor, "explore");
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 9_500 } : person).ToArray(),
        }, _ => provider);
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "house-paper");
        await AdvanceUntil(world, () => world.ExportState().Knowledge!.Facts.Any(fact => fact.OwnerId == actor), 32);
        provider.UseBuiltIn = true;
        var observedFiberDepletion = false;
        for (var tick = 0; tick < 256 && !world.WorldSimulation.ProductionJobs.Any(job =>
                 job.RecipeId == recipe.CanonicalId && job.State == WorldProductionJobState.Running); tick++)
        {
            var before = world.ExportState();
            var step = await world.AdvanceOneTickAsync();
            Assert.True(step.Advanced);
            foreach (var gathered in step.Events.Where(item => item.Kind == "material_gathered" &&
                         item.Detail.StartsWith(actor + ":fiber:", StringComparison.Ordinal)))
            {
                var afterEcology = world.ExportState().WorldSystems!.Ecology;
                Assert.Single(before.Map.Resources, resource => resource.Kind == "fiber" &&
                    afterEcology.GetResource(resource.Id).Quantity < before.WorldSystems!.Ecology.GetResource(resource.Id).Quantity);
                observedFiberDepletion = true;
            }
        }
        var working = world.ExportState();
        Assert.True(observedFiberDepletion, JsonSerializer.Serialize(new
        {
            working.SchemaVersion,
            Tick = working.Society.Society.WorldTick,
            Actor = working.Inhabitants.Single(person => person.InhabitantId == actor),
            Resident = working.Society.Society.GetInhabitant(actor),
            Cognition = working.Society.Cognition.Runtimes.Single(runtime => runtime.InhabitantId == actor),
            Lots = working.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == household || lot.OwnerId == actor).ToArray(),
            working.Knowledge,
            Offered = provider.Offered.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Sources = working.Map.Resources.Where(resource => resource.Kind == "fiber").Select(resource => new
            {
                resource.Id,
                resource.Position,
                Quantity = working.WorldSystems!.Ecology.GetResource(resource.Id).Quantity,
            }).Take(20).ToArray(),
            RecentEvents = working.Events.TakeLast(80).ToArray(),
        }));
        Assert.Contains(working.Events, item => item.Kind == "material_gathered" &&
            item.Detail.StartsWith(actor + ":fiber:", StringComparison.Ordinal));
        Assert.Contains(working.Events, item => item.Kind == "workstation_supplied" &&
            item.Detail.EndsWith(":" + houseId, StringComparison.Ordinal));
        Assert.Contains(working.Events, item => item.Kind == "water_jug_filled" &&
            item.Detail.StartsWith(actor + ":paper-water-jug:", StringComparison.Ordinal));
        Assert.Contains(working.Events, item => item.Kind == "water_jug_returned" &&
            item.Detail == actor + ":paper-water-jug:" + houseId);
        var job = Assert.Single(working.WorldSimulation!.ProductionJobs, job => job.RecipeId == recipe.CanonicalId);
        Assert.Equal(actor, job.WorkerId);
        Assert.Equal(household, job.OwnerId);
        Assert.Equal(houseId, job.BuildingInstanceId);
        var inputs = job.InputReservationIds.Select(working.Society.Society.Inventory.GetReservation).ToArray();
        Assert.Equal(2, inputs.Where(input => working.Society.Society.Inventory.GetLot(input.LotId).ItemKind == "fiber")
            .Sum(input => input.Quantity));
        var waterInput = Assert.Single(inputs, input =>
            working.Society.Society.Inventory.GetLot(input.LotId).ItemKind == InventoryContainerRules.FreshWater);
        Assert.Equal(1, waterInput.Quantity);
        var actualWater = working.Society.Society.Inventory.GetLot(waterInput.LotId);
        Assert.Equal("paper-water-jug", actualWater.ContainerLotId);
        Assert.All(inputs, input =>
        {
            var lot = working.Society.Society.Inventory.GetLot(input.LotId);
            Assert.Equal((household, houseId), (lot.OwnerId, lot.StorageBuildingId));
        });
        var replayProvider = new ChoosingProvider(actor) { UseBuiltIn = true };
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(working)), _ => replayProvider);
        for (var tick = 0; tick < recipe.DurationTicks; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        var final = world.ExportState();
        Assert.Equal(WorldProductionJobState.Completed,
            final.WorldSimulation!.ProductionJobs.Single(item => item.JobId == job.JobId).State);
        Assert.All(inputs, input => Assert.Equal(InventoryReservationState.Completed,
            final.Society.Society.Inventory.GetReservation(input.Id).State));
        Assert.Equal(actualWater.Quantity - waterInput.Quantity,
            final.Society.Society.Inventory.Lots.Where(lot => lot.Id == waterInput.LotId).Sum(lot => lot.Quantity));
        var jug = final.Society.Society.Inventory.GetLot("paper-water-jug");
        Assert.Equal((household, houseId, 1), (jug.OwnerId, jug.StorageBuildingId, jug.Quantity));
        Assert.Equal(2, final.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "paper").Sum(lot => lot.Quantity));
        // Household supply and production outrank optional writing. Request the
        // writer's next ordinary decision through the same public path on both runs.
        provider.UseBuiltIn = replayProvider.UseBuiltIn = false;
        provider.Prefixes = replayProvider.Prefixes = ["knowledge_write:field_record", "knowledge_continue"];
        var request = new OwnerInstructionRequest("write-produced-paper", "owner:test", actor,
            OwnerInstructionKind.Suggestive, "Write a field record of what you discovered using our paper.");
        Assert.Equal(world.SubmitInstruction(request), restored.SubmitInstruction(request));
        for (var tick = 0; tick < 64 && world.ExportState().Knowledge!.Artifacts.Count == 0; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        final = world.ExportState();
        var written = Assert.Single(final.Knowledge!.Artifacts);
        Assert.Contains("knowledge_write:field_record", provider.Offered);
        Assert.Equal(actor, written.CreatorId);
        Assert.All(written.Materials, material => Assert.StartsWith(job.JobId + ":output:", material.LotId));
        Assert.All(written.Facts, fact => Assert.Contains(final.Knowledge.Facts,
            learned => learned.OwnerId == actor && FactIdentity(learned) == FactIdentity(fact)));
        Assert.Equal(1, final.Society.Society.Inventory.GetLot(written.LotId).Quantity);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(final), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        world.Validate();

        provider.UseBuiltIn = false;
        provider.Prefixes = ["knowledge_store:" + written.Id];
        _ = world.SubmitInstruction(new OwnerInstructionRequest("store-written-artifact", "owner:test", actor,
            OwnerInstructionKind.Suggestive, "Store your written discoveries in your House."));
        await AdvanceUntil(world, () => world.Society.Inventory.GetLot(written.LotId).StorageBuildingId == houseId, 64);
        var stored = world.ExportState();
        Assert.Equal(actor, stored.Society.Society.Inventory.GetLot(written.LotId).OwnerId);
        Assert.Contains(stored.Events, item => item.Kind == "agent_knowledge_artifact_stored" &&
            item.Detail.Contains(written.Id, StringComparison.Ordinal));
        var foreignActor = stored.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        var foreignProvider = new ChoosingProvider(foreignActor, "knowledge_collect:" + written.Id);
        using (var foreign = PrivateWorldRuntime.Restore(stored, _ => foreignProvider))
        {
            _ = foreign.SubmitInstruction(new OwnerInstructionRequest("foreign-record", "owner:test", foreignActor,
                OwnerInstructionKind.Suggestive, "Collect that person's field record."));
            Assert.True((await foreign.AdvanceOneTickAsync()).Advanced);
            Assert.DoesNotContain("knowledge_collect:" + written.Id, foreignProvider.Offered);
            Assert.Equal((actor, houseId), (foreign.Society.Inventory.GetLot(written.LotId).OwnerId,
                foreign.Society.Inventory.GetLot(written.LotId).StorageBuildingId));
        }
        provider.Prefixes = ["knowledge_collect:" + written.Id];
        _ = world.SubmitInstruction(new OwnerInstructionRequest("retrieve-written-artifact", "owner:test", actor,
            OwnerInstructionKind.Suggestive, "Carry your written discoveries again."));
        await AdvanceUntil(world, () => world.Society.Inventory.GetLot(written.LotId).StorageBuildingId is null, 64);
        Assert.Equal((actor, 1), (world.Society.Inventory.GetLot(written.LotId).OwnerId,
            world.Society.Inventory.GetLot(written.LotId).Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "agent_knowledge_artifact_collected" &&
            item.Detail.EndsWith(written.Id, StringComparison.Ordinal));
        world.Validate();
    }

    [Fact]
    public async Task PaperProductionRefusesForeignWorkersAndWaterInAnotherPrivateHouse()
    {
        const string household = "household:camp-alpha";
        const string otherHousehold = "household:camp-beta";
        const string houseId = "first-town-house-a";
        const string otherHouseId = "first-town-house-b";
        using var setup = NormalPathWorld.CreateGenerated("physical-paper-private-stock", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household).Id;
        var outsider = state.Society.Society.Inhabitants.First(person => person.HouseholdId == otherHousehold).Id;
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == houseId);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "paper-fiber", "fiber",
            household, 2, state.Society.Society.WorldTick, storageBuildingId: houseId);
        inventory = InventoryFixture.AddLot(inventory, "private-water-jug", InventoryContainerRules.WaterJug,
            otherHousehold, 1, inventory.WorldTick, storageBuildingId: otherHouseId);
        inventory = InventoryFixture.AddLot(inventory, "private-water", InventoryContainerRules.FreshWater,
            otherHousehold, 1, inventory.WorldTick, storageBuildingId: otherHouseId, containerLotId: "private-water-jug");
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = house.Position } : person).ToArray(),
        }, _ => new ActionCoverageRecorder(chooseIdle: true));
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "house-paper");
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var missing = world.StartProduction(recipe.CanonicalId, houseId, actor);
        Assert.False(missing.Applied);
        Assert.Contains("on-site", missing.Failure, StringComparison.Ordinal);
        var forbidden = world.StartProduction(recipe.CanonicalId, houseId, outsider);
        Assert.False(forbidden.Applied);
        Assert.Contains("household", forbidden.Failure, StringComparison.Ordinal);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        world.Validate();
    }

    [Theory]
    [InlineData("field_record", 1, 0)]
    [InlineData("field_map", 1, 0)]
    [InlineData("book", 2, 1)]
    public async Task WritingUsesRealSuppliesAndPreservesPendingWorkAcrossPauseDiscardAndReload(
        string kind, int paper, int cloth)
    {
        var (state, actor) = await LearnByExploring("physical-writing-" + kind);
        var knownBefore = state.Knowledge!.Facts.Where(fact => fact.OwnerId == actor).ToArray();
        var inventory = AddWritingSupplies(state, actor, paper, cloth);
        var provider = new ChoosingProvider(actor, "knowledge_write:" + kind, "knowledge_continue");
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => provider);
        await AdvanceUntil(world, () => world.ExportState().Knowledge!.WritingProjects!.Count > 0, 64);

        var working = world.ExportState();
        var project = Assert.Single(working.Knowledge!.WritingProjects!);
        Assert.Equal(actor, project.ActorId);
        Assert.Equal(kind, project.Kind);
        Assert.Empty(working.Knowledge.Artifacts);
        Assert.Equal(paper, CarriedQuantity(working, actor, "paper"));
        Assert.Equal(cloth, CarriedQuantity(working, actor, "cloth"));
        Assert.Equal(paper + cloth, project.MaterialReservationIds
            .Select(working.Society.Society.Inventory.GetReservation).Sum(reservation => reservation.Quantity));
        Assert.All(project.Facts, fact => Assert.Contains(knownBefore, known => FactIdentity(known) == FactIdentity(fact)));
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var expectedProgress = $"{(kind == "field_map" ? "Drawing" : "Writing")} a {kind.Replace('_', ' ')} · {project.WorkDone}/{project.WorkRequired}";
        Assert.Equal(expectedProgress, Assert.Single(snapshot.Inhabitants.Single(person => person.Id == actor)
            .DecisionFactors, factor => factor.Key == "knowledge-writing").Detail);
        Assert.All(snapshot.Inhabitants.Where(person => person.Id != actor), person =>
            Assert.DoesNotContain(person.DecisionFactors, factor => factor.Key == "knowledge-writing"));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldSnapshot>(
            JsonSerializer.Serialize(snapshot, options), options)!;
        Assert.Equal(expectedProgress, Assert.Single(client.Inhabitants.Single(person => person.Id == actor)
            .DecisionFactors, factor => factor.Key == "knowledge-writing").Detail);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(working with
        {
            Knowledge = working.Knowledge with { WritingProjects = [project with { Facts = [null!] }] },
        }));

        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.Resume();
        var beforeDiscard = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(beforeDiscard, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(beforeDiscard), _ =>
            new ChoosingProvider(actor, "knowledge_write:" + kind, "knowledge_continue"));
        Assert.Equal(expectedProgress, Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants
            .Single(person => person.Id == actor).DecisionFactors, factor => factor.Key == "knowledge-writing").Detail);
        for (var tick = 0; tick < 16; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }

        var final = world.ExportState();
        var artifact = Assert.Single(final.Knowledge!.Artifacts);
        Assert.Equal(kind, artifact.Kind);
        Assert.Equal(actor, artifact.CreatorId);
        Assert.Equal(project.Facts.Select(FactIdentity), artifact.Facts.Select(FactIdentity));
        var lot = final.Society.Society.Inventory.GetLot(artifact.LotId);
        Assert.Equal((actor, kind, 1), (lot.OwnerId, lot.ItemKind, lot.Quantity));
        Assert.Null(lot.StorageBuildingId);
        Assert.Equal(0, CarriedQuantity(final, actor, "paper"));
        Assert.Equal(0, CarriedQuantity(final, actor, "cloth"));
        Assert.Empty(final.Knowledge.WritingProjects!);
        Assert.All(project.MaterialReservationIds, id =>
            Assert.Equal(InventoryReservationState.Completed, final.Society.Society.Inventory.GetReservation(id).State));
        Assert.DoesNotContain(final.Knowledge.Facts, fact => fact.OwnerId != actor);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(final), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.DoesNotContain(new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == actor)
            .DecisionFactors, factor => factor.Key == "knowledge-writing");
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(final with
        {
            Knowledge = final.Knowledge with { Artifacts = [artifact with { Materials = [] }] },
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(final with
        {
            Knowledge = final.Knowledge with { Artifacts = [artifact with { Facts = null! }] },
        }));
        var selfSourcedFacts = artifact.Facts.Select(fact => fact with
        {
            Acquisition = "read",
            SourceArtifactId = artifact.Id,
            SourceAgentId = actor,
            LearnedTick = artifact.CreatedTick,
        }).ToArray();
        var cyclic = artifact with { SourceArtifactId = artifact.Id, Facts = selfSourcedFacts };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(final with
        {
            Knowledge = final.Knowledge with
            {
                Artifacts = [cyclic],
                Facts = final.Knowledge.Facts.Select(fact => selfSourcedFacts.FirstOrDefault(source => source.Id == fact.Id) ?? fact).ToArray(),
            },
        }));
        world.Validate();
        restored.Validate();
    }

    [Theory]
    [InlineData("field_record", 0, 0, false)]
    [InlineData("book", 2, 0, false)]
    [InlineData("field_map", 1, 0, true)]
    public async Task WritingDoesNotUseMissingForeignOrReservedMaterials(string kind, int paper, int cloth, bool reserved)
    {
        var (state, actor) = await LearnByExploring("physical-writing-refusal-" + kind);
        var inventory = AddWritingSupplies(state, actor, paper, cloth);
        var other = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        inventory = InventoryFixture.AddLot(inventory, "foreign-writing-paper", "paper", other, 2);
        inventory = InventoryFixture.AddLot(inventory, "foreign-writing-cloth", "cloth", other, 1);
        if (reserved)
            inventory = InventoryFixture.Reserve(inventory, "paper-earmarked-elsewhere", actor,
                "writing-paper", 1, "another physical commitment", state.Society.Society.WorldTick + 200);
        var provider = new ChoosingProvider(actor, "knowledge_write:" + kind, "knowledge_continue");
        using var world = PrivateWorldRuntime.Restore(WithInventory(state, inventory), _ => provider);
        for (var tick = 0; tick < 32; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var final = world.ExportState();
        Assert.DoesNotContain("knowledge_write:" + kind, provider.Offered);
        Assert.Empty(final.Knowledge!.Artifacts);
        Assert.Empty(final.Knowledge.WritingProjects!);
        Assert.Equal(paper, CarriedQuantity(final, actor, "paper"));
        Assert.Equal(cloth, CarriedQuantity(final, actor, "cloth"));
        Assert.Equal(2, final.Society.Society.Inventory.GetLot("foreign-writing-paper").Quantity);
        Assert.Equal(1, final.Society.Society.Inventory.GetLot("foreign-writing-cloth").Quantity);
        Assert.DoesNotContain(final.Knowledge.Facts, fact => fact.OwnerId != actor);
        world.Validate();
    }

    [Theory]
    [InlineData("field_record", 1, 0)]
    [InlineData("field_map", 1, 0)]
    [InlineData("book", 2, 1)]
    public async Task ReadingAndCopyingPreserveTheDiscovererAndCreateOnlyOnePaidPhysicalCopy(string kind, int paper, int cloth)
    {
        var (state, author) = await LearnByExploring("physical-copy-provenance");
        using var writing = PrivateWorldRuntime.Restore(WithInventory(state, AddWritingSupplies(state, author, paper, cloth)), _ =>
            new ChoosingProvider(author, "knowledge_write:" + kind, "knowledge_continue"));
        await AdvanceUntil(writing, () => writing.ExportState().Knowledge!.Artifacts.Count == 1, 64);
        var written = writing.ExportState();
        var original = Assert.Single(written.Knowledge!.Artifacts);
        var reader = written.Inhabitants.First(person => person.InhabitantId != author).InhabitantId;
        var untouched = written.Inhabitants.First(person => person.InhabitantId != author && person.InhabitantId != reader).InhabitantId;
        // Transfer this actual, written quantity-one artifact through the inventory
        // contract. Reading and copying below use normal offered runtime actions.
        var transferred = InventoryFixture.Transfer(written.Society.Society.Inventory, "give-written-record",
            author, reader, original.LotId, 1, "give the actual field record");
        var provider = new ChoosingProvider(reader, "knowledge_read:" + original.Id);
        using var reading = PrivateWorldRuntime.Restore(WithInventory(written, transferred), _ => provider);
        await AdvanceUntil(reading, () => reading.ExportState().Knowledge!.Facts.Any(fact => fact.OwnerId == reader), 64);
        var read = reading.ExportState();
        var learned = read.Knowledge!.Facts.Where(fact => fact.OwnerId == reader).ToArray();
        Assert.Equal(original.Facts.Count, learned.Length);
        Assert.All(learned, fact =>
        {
            Assert.Equal(author, fact.DiscovererId);
            Assert.Equal(original.Id, fact.SourceArtifactId);
            Assert.Equal("read", fact.Acquisition);
        });
        Assert.DoesNotContain(read.Knowledge.Facts, fact => fact.OwnerId == untouched);
        Assert.Single(read.Knowledge.Artifacts);

        provider.Prefixes = ["knowledge_copy:" + original.Id, "knowledge_continue"];
        for (var tick = 0; tick < 32; tick++)
            Assert.True((await reading.AdvanceOneTickAsync()).Advanced);
        Assert.Single(reading.ExportState().Knowledge!.Artifacts);
        Assert.DoesNotContain("knowledge_copy:" + original.Id, provider.Offered);

        var supplied = reading.ExportState();
        var suppliedInventory = InventoryFixture.AddLot(supplied.Society.Society.Inventory,
            "copy-paper", "paper", reader, paper, supplied.Society.Society.WorldTick);
        if (cloth > 0)
            suppliedInventory = InventoryFixture.AddLot(suppliedInventory, "copy-cloth", "cloth", reader, cloth,
                supplied.Society.Society.WorldTick);
        using var copying = PrivateWorldRuntime.Restore(WithInventory(supplied, suppliedInventory), _ =>
            new ChoosingProvider(reader, "knowledge_copy:" + original.Id, "knowledge_continue"));
        await AdvanceUntil(copying, () => copying.ExportState().Knowledge!.WritingProjects.Count == 1, 64);
        var pendingCopy = copying.ExportState();
        var copyProject = Assert.Single(pendingCopy.Knowledge!.WritingProjects);
        var removedSource = InventoryFixture.Transfer(pendingCopy.Society.Society.Inventory, "return-copy-source",
            reader, author, original.LotId, 1, "return the original before copying finishes");
        using (var interrupted = PrivateWorldRuntime.Restore(WithInventory(pendingCopy, removedSource), _ =>
                   new ChoosingProvider(reader, "knowledge_copy:" + original.Id, "knowledge_continue")))
        {
            Assert.True((await interrupted.AdvanceOneTickAsync()).Advanced);
            var cancelled = interrupted.ExportState();
            Assert.Single(cancelled.Knowledge!.Artifacts);
            Assert.Empty(cancelled.Knowledge.WritingProjects);
            Assert.Equal(paper, CarriedQuantity(cancelled, reader, "paper"));
            Assert.Equal(cloth, CarriedQuantity(cancelled, reader, "cloth"));
            Assert.All(copyProject.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Released,
                cancelled.Society.Society.Inventory.GetReservation(id).State));
            Assert.Contains(cancelled.Events, item => item.Kind == "agent_knowledge_writing_cancelled");
            Assert.Equal(original.Id, Assert.Single(cancelled.Knowledge.Artifacts).Id);
            interrupted.Validate();
        }
        await AdvanceUntil(copying, () => copying.ExportState().Knowledge!.Artifacts.Count == 2, 64);
        var final = copying.ExportState();
        var copy = Assert.Single(final.Knowledge!.Artifacts, artifact => artifact.Id != original.Id);
        Assert.Equal(reader, copy.CreatorId);
        Assert.NotEqual(original.LotId, copy.LotId);
        Assert.Equal(original.Facts.Select(fact => (fact.Position, fact.DiscovererId, fact.Terrain)),
            copy.Facts.Select(fact => (fact.Position, fact.DiscovererId, fact.Terrain)));
        Assert.All(copy.Facts, fact => Assert.Equal(reader, fact.OwnerId));
        Assert.DoesNotContain(final.Society.Society.Inventory.Lots, lot => lot.Id is "copy-paper" or "copy-cloth");
        Assert.Equal((reader, 1), (final.Society.Society.Inventory.GetLot(original.LotId).OwnerId,
            final.Society.Society.Inventory.GetLot(original.LotId).Quantity));
        Assert.Equal((reader, 1), (final.Society.Society.Inventory.GetLot(copy.LotId).OwnerId,
            final.Society.Society.Inventory.GetLot(copy.LotId).Quantity));
        Assert.DoesNotContain(final.Knowledge.Facts, fact => fact.OwnerId == untouched);
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(final)));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(final), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Fact]
    public async Task CopyingKnownSitesKeepsFirsthandKnowledgeAndSourceProvenanceAcrossReload()
    {
        var (state, author) = await LearnByExploring("physical-copy-overlap");
        using var writing = PrivateWorldRuntime.Restore(WithInventory(state, AddWritingSupplies(state, author, 2, 1)), _ =>
            new ChoosingProvider(author, "knowledge_write:book", "knowledge_continue"));
        await AdvanceUntil(writing, () => writing.ExportState().Knowledge!.Artifacts.Count == 1, 64);
        var written = writing.ExportState();
        var original = Assert.Single(written.Knowledge!.Artifacts);
        var reader = written.Inhabitants.First(person => person.InhabitantId != author).InhabitantId;
        var overlap = original.Facts.First(fact => written.Inhabitants.All(person =>
            person.InhabitantId == reader || person.Position != fact.Position)).Position;
        // Observe one of the same sites through a real outing, before receiving
        // the book. The reader's firsthand discovery must never be overwritten.
        using var exploring = PrivateWorldRuntime.Restore(written with
        {
            Inhabitants = written.Inhabitants.Select(person => person.InhabitantId == reader
                ? person with { Position = overlap, HungerBasisPoints = 9_500 } : person).ToArray(),
        }, _ => new ChoosingProvider(reader, "explore"));
        _ = exploring.SubmitInstruction(new OwnerInstructionRequest("explore-copy-overlap", "owner:test", reader,
            OwnerInstructionKind.Suggestive, "Explore the site where you are standing."));
        await AdvanceUntil(exploring, () => exploring.Knowledge.Facts.Any(fact =>
            fact.OwnerId == reader && fact.Position == overlap), 32);
        var explored = exploring.ExportState();
        var firsthand = Assert.Single(explored.Knowledge!.Facts, fact => fact.OwnerId == reader && fact.Position == overlap);
        Assert.Equal((reader, "firsthand"), (firsthand.DiscovererId, firsthand.Acquisition));
        Assert.Contains(original.Facts, fact => !explored.Knowledge.Facts.Any(known =>
            known.OwnerId == reader && known.Position == fact.Position));

        var inventory = InventoryFixture.Transfer(explored.Society.Society.Inventory, "give-overlapping-book",
            author, reader, original.LotId, 1, "give the actual written book");
        inventory = InventoryFixture.AddLot(inventory, "overlap-copy-paper", "paper", reader, 2, inventory.WorldTick);
        inventory = InventoryFixture.AddLot(inventory, "overlap-copy-cloth", "cloth", reader, 1, inventory.WorldTick);
        var copyCandidate = "knowledge_copy:" + original.Id;
        var provider = new ChoosingProvider(reader, copyCandidate, "knowledge_continue");
        using var copying = PrivateWorldRuntime.Restore(WithInventory(explored, inventory), _ => provider);
        _ = copying.SubmitInstruction(new OwnerInstructionRequest("copy-before-reading", "owner:test", reader,
            OwnerInstructionKind.Suggestive, "Copy the book using your supplies."));
        Assert.True((await copying.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(copyCandidate, provider.Offered);
        Assert.Empty(copying.Knowledge.WritingProjects);
        Assert.Equal((2, 1), (CarriedQuantity(copying.ExportState(), reader, "paper"),
            CarriedQuantity(copying.ExportState(), reader, "cloth")));

        provider.Prefixes = ["knowledge_read:" + original.Id];
        _ = copying.SubmitInstruction(new OwnerInstructionRequest("read-overlapping-book", "owner:test", reader,
            OwnerInstructionKind.Suggestive, "Read the other sites in this book."));
        await AdvanceUntil(copying, () => original.Facts.All(fact => copying.Knowledge.Facts.Any(known =>
            known.OwnerId == reader && known.Position == fact.Position)), 32);
        var learned = copying.ExportState();
        Assert.Equal(FactIdentity(firsthand), FactIdentity(Assert.Single(learned.Knowledge!.Facts,
            fact => fact.OwnerId == reader && fact.Position == overlap)));

        provider.Prefixes = [copyCandidate, "knowledge_continue"];
        _ = copying.SubmitInstruction(new OwnerInstructionRequest("copy-after-reading", "owner:test", reader,
            OwnerInstructionKind.Suggestive, "Make a paid copy of the book."));
        await AdvanceUntil(copying, () => copying.Knowledge.WritingProjects.Count == 1, 32);
        var pending = copying.ExportState();
        var project = Assert.Single(pending.Knowledge!.WritingProjects);
        Assert.Equal(original.Id, project.SourceArtifactId);
        Assert.All(project.Facts, fact =>
        {
            Assert.Equal((reader, author, original.Id, "read"),
                (fact.OwnerId, fact.DiscovererId, fact.SourceArtifactId, fact.Acquisition));
            Assert.Equal(author, fact.SourceAgentId);
        });
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(pending with
        {
            Knowledge = pending.Knowledge with
            {
                Facts = pending.Knowledge.Facts.Where(fact => fact.OwnerId != reader || fact.Position != overlap).ToArray(),
            },
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(pending with
        {
            Knowledge = pending.Knowledge with
            {
                Facts = pending.Knowledge.Facts.Select(fact => fact.OwnerId == reader && fact.Position == overlap
                    ? fact with { ResourceKinds = fact.ResourceKinds.Count == 0 ? ["fiber"] : [] } : fact).ToArray(),
            },
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(pending with
        {
            Knowledge = pending.Knowledge with
            {
                WritingProjects = [project with
                {
                    Facts = project.Facts.Select(fact => fact.Position == overlap
                        ? fact with { DiscovererId = reader } : fact).ToArray(),
                }],
            },
        }));

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(pending)), _ =>
            new ChoosingProvider(reader, copyCandidate, "knowledge_continue"));
        for (var tick = 0; tick < 16; tick++)
        {
            Assert.True((await copying.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        var final = copying.ExportState();
        var copy = Assert.Single(final.Knowledge!.Artifacts, artifact => artifact.Id != original.Id);
        Assert.Equal(original.Id, copy.SourceArtifactId);
        Assert.Equal(original.Facts.Select(fact => (fact.Position, fact.DiscovererId, fact.Terrain, string.Join(',', fact.ResourceKinds))),
            copy.Facts.Select(fact => (fact.Position, fact.DiscovererId, fact.Terrain, string.Join(',', fact.ResourceKinds))));
        var preserved = Assert.Single(final.Knowledge.Facts, fact => fact.OwnerId == reader && fact.Position == overlap);
        Assert.Equal(FactIdentity(firsthand), FactIdentity(preserved));
        Assert.Equal((firsthand.Id, firsthand.LearnedTick), (preserved.Id, preserved.LearnedTick));
        Assert.NotEqual(original.LotId, copy.LotId);
        Assert.Equal((reader, 1), (final.Society.Society.Inventory.GetLot(copy.LotId).OwnerId,
            final.Society.Society.Inventory.GetLot(copy.LotId).Quantity));
        Assert.Empty(final.Knowledge.WritingProjects);
        Assert.Equal((0, 0), (CarriedQuantity(final, reader, "paper"), CarriedQuantity(final, reader, "cloth")));
        Assert.All(project.MaterialReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
            final.Society.Society.Inventory.GetReservation(id).State));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(final), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        copying.Validate();
        restored.Validate();
    }

    private static async Task<(PrivateWorldRuntimeState State, string Actor)> LearnByExploring(string seed)
    {
        using var initial = new PrivateWorldRuntime(seed);
        var state = initial.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 9_500 }).ToArray(),
        }, _ => new ChoosingProvider(actor, "explore"));
        for (var tick = 0; tick < 75; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var learned = world.ExportState();
        Assert.Contains(learned.Events, item => item.Kind == "exploration_completed");
        Assert.True(learned.Knowledge!.Facts.Count(fact => fact.OwnerId == actor) >= 2);
        Assert.Empty(learned.Knowledge.Artifacts);
        return (PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(learned)), actor);
    }

    private static InventoryCheckpoint AddWritingSupplies(PrivateWorldRuntimeState state, string actor, int paper, int cloth)
    {
        var inventory = state.Society.Society.Inventory;
        if (paper > 0)
            inventory = InventoryFixture.AddLot(inventory, "writing-paper", "paper", actor, paper, inventory.WorldTick);
        if (cloth > 0)
            inventory = InventoryFixture.AddLot(inventory, "writing-cloth", "cloth", actor, cloth, inventory.WorldTick);
        return inventory;
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static int CarriedQuantity(PrivateWorldRuntimeState state, string actor, string kind) =>
        state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == kind &&
            PersonalEquipmentRules.IsCarried(lot, actor)).Sum(lot => lot.Quantity);

    private static (string Owner, string Discoverer, GridPoint Position, string Terrain, string Resources,
        string Acquisition, string? SourceAgent, string? SourceArtifact) FactIdentity(AgentKnowledgeFact fact) =>
        (fact.OwnerId, fact.DiscovererId, fact.Position, fact.Terrain, string.Join(',', fact.ResourceKinds),
            fact.Acquisition, fact.SourceAgentId, fact.SourceArtifactId);

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> complete, int ticks)
    {
        for (var tick = 0; tick < ticks && !complete(); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(complete(), $"Expected physical work to finish within {ticks} ticks; world tick {world.WorldTick}. " +
            string.Join(" | ", world.ExportState().Events.TakeLast(8).Select(item => item.Kind + ":" + item.Detail)));
    }

    private sealed class ChoosingProvider(string actor, params string[] prefixes) : IDecisionProvider
    {
        public string[] Prefixes { get; set; } = prefixes;
        public bool UseBuiltIn { get; set; }
        public ConcurrentBag<string> Offered { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var isActor = request.Observation.InhabitantId == actor;
            if (isActor)
                foreach (var candidate in request.Observation.Candidates)
                    Offered.Add(candidate.Id);
            if (isActor && UseBuiltIn)
                return new DeterministicDecisionProvider().DecideAsync(request, cancellationToken);
            var selected = isActor ? Prefixes.Select(prefix => request.Observation.Candidates
                    .FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .FirstOrDefault(candidate => candidate is not null) : null;
            selected ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}
