using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class NativeOrchardObservationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RevisitedOrchardBecomesAKnownFoodOrderDestination(bool firstObservationAfterPlanting, bool anotherPlanter)
    {
        var (state, actor, target, blocked) = await ObserveEmptyTile();
        var planter = actor;
        if (anotherPlanter)
        {
            planter = state.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
            state = FarmFieldTests.WithInventory(state, InventoryFixture.Transfer(state.Society.Society.Inventory,
                "give-revisit-seed", actor, planter, "orchard-observation-seed", 1, "give planting seed")) with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == planter ? person with
                {
                    Position = state.Map.FootNeighbors(target).First(),
                    TravelCooldownTicks = 0,
                } : person).ToArray(),
            };
        }
        using var planting = Restore(state, actor);
        var result = planting.PlantTree(planter, TreeGrowthRules.Orchard, "orchard-observation-seed", target);
        Assert.True(result.Planted, result.Message);
        state = planting.ExportState();
        if (anotherPlanter)
            Assert.Empty(Assert.Single(state.Knowledge!.Facts, fact => fact.OwnerId == actor && fact.Position == target).ResourceKinds);
        var today = WorldCalendarRules.FromTick(planting.WorldTick, planting.WorldSystems.Config).DayIndex;
        state = state with
        {
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == result.TreeId
                        ? resource with { NextRegenerationDay = today } : resource).ToArray(),
                },
            },
        };
        using (var growing = Restore(state, actor))
        {
            Assert.True((await growing.AdvanceOneTickAsync()).Advanced);
            Assert.False(growing.WorldSystems.Ecology.GetResource(result.TreeId!).IsPlanted);
            state = growing.ExportState();
        }
        state = state with
        {
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == result.TreeId
                        ? TreeGrowthAndPlantingTests.InFruitingSeason(resource, state) : resource).ToArray(),
                },
            },
            Knowledge = firstObservationAfterPlanting ? state.Knowledge! with
            {
                Facts = state.Knowledge.Facts.Where(fact => fact.OwnerId != actor || fact.Position != target).ToArray(),
            } : state.Knowledge,
        };
        using var world = Restore(state, actor);
        await Move(world, actor, target, "revisit-orchard");
        var observed = Assert.Single(world.ExportState().Knowledge!.Facts, fact => fact.OwnerId == actor && fact.Position == target);
        var away = state.Map.Tiles.Select(tile => tile.Position).Where(point => !blocked.Contains(point) &&
                state.Map.IsPassable(point) && state.Map.FootDistance(point, target) == 5 && state.Map.IsReachableOnFoot(target, point))
            .OrderBy(point => point.Y).ThenBy(point => point.X).First();
        await Move(world, actor, away, "leave-orchard");
        var receipt = world.SubmitInstruction(new("harvest-known-orchard", "owner:test", actor, OwnerInstructionKind.MustDo,
            "harvest fruit from " + result.TreeId));
        var orderTicks = 0;
        for (; orderTicks < 30 && Order(world, receipt).Status != "finished"; orderTicks++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(bytes), actor);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        var fruit = world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "fruit").Sum(lot => lot.Quantity);
        output.WriteLine($"first_observation_after_planting={firstObservationAfterPlanting}; another_planter={anotherPlanter}; tile={target}; " +
            $"observed_resources={string.Join(',', observed.ResourceKinds)}; harvest_status={Order(world, receipt).Status}; " +
            $"harvest_ticks={orderTicks}; fruit={fruit}");
        Assert.Contains("fruit", observed.ResourceKinds);
        Assert.Equal("finished", Order(world, receipt).Status);
        Assert.Equal(4, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "fruit").Sum(lot => lot.Quantity));
    }

    private static async Task<(PrivateWorldRuntimeState State, string Actor, GridPoint Target, HashSet<GridPoint> Blocked)> ObserveEmptyTile()
    {
        var state = GeographyGeneratorTests.StartedGeneratedWorld(new GeographyOptions("orchard-knowledge-audit", WorldSizePreset.Small));
        var actor = state.Inhabitants[0].InhabitantId;
        using var initial = Restore(state, actor);
        var blocked = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building))
            .Concat(initial.RoadTiles).Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var target = state.Map.Tiles.Select(tile => tile.Position).Where(point => !blocked.Contains(point) &&
                TreeGrowthRules.GroundRefusal(state.Map, point) is null &&
                state.Map.IsReachableOnFoot(state.Inhabitants[0].Position, point))
            .OrderBy(point => state.Map.FootDistance(state.Inhabitants[0].Position, point)).First();
        var stand = state.Map.FootNeighbors(target).First(point =>
            !state.Inhabitants.Skip(1).Any(person => person.Position == point));
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "orchard-observation-seed", TreeGrowthRules.OrchardSeedItem, actor, 1);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = stand,
                HungerBasisPoints = 9000,
                TravelCooldownTicks = 0,
                LastDecisionContext = null,
                Project = null,
                Exploration = null,
            } : person).ToArray(),
        };
        using var planting = Restore(state, actor);
        await Move(planting, actor, target, "visit-empty");
        var empty = Assert.Single(planting.ExportState().Knowledge!.Facts, fact => fact.OwnerId == actor && fact.Position == target);
        Assert.Empty(empty.ResourceKinds);
        return (planting.ExportState(), actor, target, blocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlantingPreservesPublishedAndUnfinishedRecords(bool unfinished)
    {
        var (state, actor, target, _) = await ObserveEmptyTile();
        var provider = new WritingProvider(actor, "knowledge_write:field_record");
        using var world = RestoreWriting(WithPaper(state, actor), provider);
        RequestWriting(world, actor, "write-empty-site");
        await Until(world, () => unfinished ? world.ExportState().Knowledge!.WritingProjects.Count == 1
            : world.ExportState().Knowledge!.Artifacts.Count == 1);
        var before = world.ExportState();
        var snapshot = unfinished ? Assert.Single(before.Knowledge!.WritingProjects).Facts
            : Assert.Single(before.Knowledge!.Artifacts).Facts;
        var original = Assert.Single(snapshot);
        Assert.Empty(original.ResourceKinds);
        Assert.True(world.PlantTree(actor, TreeGrowthRules.Orchard, "orchard-observation-seed", target).Planted);
        var changed = world.ExportState();
        Assert.Equal(original, Assert.Single(changed.Knowledge!.EarlierFacts));
        var current = Assert.Single(changed.Knowledge.Facts, fact => fact.OwnerId == actor && fact.Position == target);
        Assert.Contains("fruit", current.ResourceKinds);
        Assert.Equal("firsthand", current.Acquisition);
        Assert.Equal(original.Id, current.Id);
        Assert.Equal(before.Knowledge.Facts.Count, changed.Knowledge.Facts.Count);
        Assert.Equal(snapshot, unfinished ? Assert.Single(changed.Knowledge.WritingProjects).Facts
            : Assert.Single(changed.Knowledge.Artifacts).Facts);
        AssertReload(changed);
        if (unfinished)
        {
            var bytes = PrivateWorldRuntimeCodec.Encode(changed);
            Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            using var replay = RestoreWriting(changed, new WritingProvider(actor, "knowledge_write:field_record"));
            for (var tick = 0; tick < 8 && world.ExportState().Knowledge!.Artifacts.Count == 0; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
            Assert.Equal(snapshot, Assert.Single(world.ExportState().Knowledge!.Artifacts).Facts);
        }
        var final = world.ExportState();
        Assert.DoesNotContain(final.Society.Society.Inventory.Lots, lot => lot.Id == "orchard-record-paper");
        AssertReload(final);
        foreach (var invalid in new[]
        {
            final.Knowledge! with { EarlierFacts = [] },
            final.Knowledge! with { EarlierFacts = [original, original] },
            final.Knowledge! with { EarlierFacts = [current] },
            final.Knowledge! with { EarlierFacts = [original with { LearnedTick = final.Society.Society.WorldTick + 1 }] },
            final.Knowledge! with { EarlierFacts = [original with { Id = "unbacked-old-observation" }] },
            final.Knowledge! with { EarlierFacts = [original with { ResourceKinds = ["grain"] }] },
        })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(final with { Knowledge = invalid }));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CopySnapshotSurvivesReaderObservationAndReleasesHistoryOnCancellation(bool cancel, bool observeBeforeCopy)
    {
        var (state, author, target, _) = await ObserveEmptyTile();
        var authorProvider = new WritingProvider(author, "knowledge_write:field_record");
        using var writing = RestoreWriting(WithPaper(state, author), authorProvider);
        RequestWriting(writing, author, "write-source");
        await Until(writing, () => writing.ExportState().Knowledge!.Artifacts.Count == 1);
        state = writing.ExportState();
        var source = Assert.Single(state.Knowledge!.Artifacts);
        var reader = state.Inhabitants.First(person => person.InhabitantId != author).InhabitantId;
        // The original physical record is transferred as setup; reading, copying,
        // planting and cancellation below run the ordinary gameplay actions.
        state = FarmFieldTests.WithInventory(state, InventoryFixture.Transfer(state.Society.Society.Inventory,
            "give-orchard-record", author, reader, source.LotId, 1, "give written record"));
        var provider = new WritingProvider(reader, "knowledge_read:" + source.Id);
        using var reading = RestoreWriting(state, provider);
        RequestWriting(reading, reader, "read-source");
        await Until(reading, () => reading.ExportState().Knowledge!.Facts.Any(fact => fact.OwnerId == reader && fact.Position == target));
        var learned = Assert.Single(reading.ExportState().Knowledge!.Facts, fact => fact.OwnerId == reader && fact.Position == target);
        Assert.Equal("read", learned.Acquisition);
        state = WithPaper(reading.ExportState(), reader);
        state = FarmFieldTests.WithInventory(state, InventoryFixture.Transfer(state.Society.Society.Inventory,
            "give-orchard-seed", author, reader, "orchard-observation-seed", 1, "give planting seed")) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == reader ? person with
            {
                Position = target,
                TravelCooldownTicks = 0,
                LastDecisionContext = null,
            } : person).ToArray(),
        };
        provider.Prefix = "knowledge_copy:" + source.Id;
        using var copying = RestoreWriting(state, provider);
        if (observeBeforeCopy)
        {
            Assert.True(copying.PlantTree(reader, TreeGrowthRules.Orchard, "orchard-observation-seed", target).Planted);
            Assert.Empty(copying.ExportState().Knowledge!.EarlierFacts);
        }
        RequestWriting(copying, reader, "copy-source");
        await Until(copying, () => copying.ExportState().Knowledge!.WritingProjects.Count == 1);
        var project = Assert.Single(copying.ExportState().Knowledge!.WritingProjects);
        Assert.True(project.StartedTick > learned.LearnedTick);
        if (!observeBeforeCopy)
            Assert.True(copying.PlantTree(reader, TreeGrowthRules.Orchard, "orchard-observation-seed", target).Planted);
        state = copying.ExportState();
        if (observeBeforeCopy) Assert.Empty(state.Knowledge!.EarlierFacts);
        else Assert.Equal(FactKey(learned), FactKey(Assert.Single(state.Knowledge!.EarlierFacts)));
        Assert.Contains("fruit", Assert.Single(state.Knowledge.Facts,
            fact => fact.OwnerId == reader && fact.Position == target).ResourceKinds);
        Assert.Empty(Assert.Single(project.Facts).ResourceKinds);
        Assert.Empty(Assert.Single(state.Knowledge.Facts, fact => fact.OwnerId == author && fact.Position == target).ResourceKinds);
        Assert.Equal(project.Facts, Assert.Single(state.Knowledge.WritingProjects).Facts);
        AssertReload(state);
        if (observeBeforeCopy)
        {
            // Controlled saved-history boundary after actual paid copying starts:
            // a later observation alone cannot prove the site was known then.
            var witness = Assert.Single(state.Knowledge.Facts,
                fact => fact.OwnerId == reader && fact.Position == target);
            Assert.True((await copying.AdvanceOneTickAsync()).Advanced);
            state = copying.ExportState();
            Assert.True(copying.WorldTick > project.StartedTick);
            var later = witness with { LearnedTick = copying.WorldTick, ResourceKinds = ["wood"] };
            var futureOnly = state with
            {
                Knowledge = state.Knowledge! with
                {
                    Facts = state.Knowledge.Facts.Select(fact => fact.Id == witness.Id && fact.OwnerId == reader
                        ? later : fact).ToArray(),
                },
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(futureOnly));
            var backed = futureOnly with
            {
                Knowledge = futureOnly.Knowledge! with { EarlierFacts = [witness] },
            };
            AssertReload(backed);
            Assert.Empty(Assert.Single(backed.Knowledge.WritingProjects).Facts.Single().ResourceKinds);
            Assert.Equal(["wood"], Assert.Single(backed.Knowledge.Facts,
                fact => fact.OwnerId == reader && fact.Position == target).ResourceKinds);
        }
        if (cancel)
        {
            state = FarmFieldTests.WithInventory(state, InventoryFixture.Transfer(state.Society.Society.Inventory,
                "return-orchard-source", reader, author, source.LotId, 1, "return original"));
            using var interrupted = RestoreWriting(state, provider);
            Assert.True((await interrupted.AdvanceOneTickAsync()).Advanced);
            state = interrupted.ExportState();
            Assert.Empty(state.Knowledge!.WritingProjects);
            Assert.Empty(state.Knowledge.EarlierFacts);
            Assert.Single(state.Knowledge.Artifacts);
            Assert.Equal(1, state.Society.Society.Inventory.GetLot("orchard-record-paper").Quantity);
            Assert.Contains(state.Events, item => item.Kind == "agent_knowledge_writing_cancelled");
        }
        else
        {
            await Until(copying, () => copying.ExportState().Knowledge!.Artifacts.Count == 2);
            state = copying.ExportState();
            Assert.Equal(project.Facts, Assert.Single(state.Knowledge!.Artifacts, artifact => artifact.CreatorId == reader).Facts);
            if (observeBeforeCopy) Assert.Empty(state.Knowledge.EarlierFacts);
            else Assert.Equal(FactKey(learned), FactKey(Assert.Single(state.Knowledge.EarlierFacts)));
            Assert.DoesNotContain(state.Society.Society.Inventory.Lots, lot => lot.Id == "orchard-record-paper");
        }
        AssertReload(state);
    }

    [Fact]
    public async Task FullLedgerRefreshesAnExistingSiteWithoutDuplicateLearning()
    {
        var (state, actor, target, _) = await ObserveEmptyTile();
        var fact = Assert.Single(state.Knowledge!.Facts, item => item.OwnerId == actor && item.Position == target);
        // Controlled capacity boundary; the target fact came from actual movement.
        var others = state.Map.Tiles.Where(tile => tile.Position != target && state.Map.IsPassable(tile.Position))
            .Take(AgentKnowledgeRules.MaximumFactsPerAgent - 1).Select((tile, index) => fact with
            {
                Id = "capacity-site-" + index,
                Position = tile.Position,
                Terrain = tile.Terrain.ToString(),
            }).ToArray();
        state = state with { Knowledge = state.Knowledge with { Facts = others.Append(fact).ToArray() } };
        using var world = Restore(state, actor);
        Assert.True(world.PlantTree(actor, TreeGrowthRules.Orchard, "orchard-observation-seed", target).Planted);
        var planted = world.ExportState();
        Assert.Equal(AgentKnowledgeRules.MaximumFactsPerAgent, planted.Knowledge!.Facts.Count);
        var observed = Assert.Single(planted.Knowledge.Facts, item => item.Position == target);
        Assert.Contains("fruit", observed.ResourceKinds);
        Assert.Empty(planted.Knowledge.EarlierFacts);
        var learnedEvents = planted.Events.Count(item => item.Kind == "agent_knowledge_learned");
        await Move(world, actor, target, "unchanged-orchard");
        var revisited = world.ExportState();
        Assert.Equal(FactKey(observed), FactKey(Assert.Single(revisited.Knowledge!.Facts, item => item.Position == target)));
        Assert.Equal(learnedEvents, revisited.Events.Count(item => item.Kind == "agent_knowledge_learned"));
        Assert.Empty(revisited.Knowledge.EarlierFacts);
        AssertReload(revisited);
    }

    private static string FactKey(AgentKnowledgeFact fact) => System.Text.Json.JsonSerializer.Serialize(fact);

    private static PrivateWorldRuntimeState WithPaper(PrivateWorldRuntimeState state, string actor) =>
        FarmFieldTests.WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "orchard-record-paper", "paper", actor, 1, state.Society.Society.WorldTick));

    private static PrivateWorldRuntime RestoreWriting(PrivateWorldRuntimeState state, WritingProvider provider) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => provider);

    private static void RequestWriting(PrivateWorldRuntime world, string actor, string key) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.Suggestive, "Read or write the field record."));

    private static async Task Until(PrivateWorldRuntime world, Func<bool> done)
    {
        for (var tick = 0; tick < 64 && !done(); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(done());
    }

    private static void AssertReload(PrivateWorldRuntimeState state)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        loaded.Validate();
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }

    private sealed class WritingProvider(string actor, string prefix) : IDecisionProvider
    {
        public string Prefix { get; set; } = prefix;
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates;
            var selected = request.Observation.InhabitantId == actor
                ? candidates.FirstOrDefault(item => item.Id.StartsWith(Prefix, StringComparison.Ordinal))
                  ?? candidates.FirstOrDefault(item => item.Id == "knowledge_continue") : null;
            selected ??= candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor) => PrivateWorldRuntime.Restore(
        PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), id => id == actor ? new DeterministicDecisionProvider() : new Idle());

    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;

    private static async Task Move(PrivateWorldRuntime world, string actor, GridPoint target, string key)
    {
        var receipt = world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, $"move to {target.X},{target.Y}"));
        for (var tick = 0; tick < 35 && Order(world, receipt).Status != "finished"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("finished", Order(world, receipt).Status);
    }

    private sealed class Idle : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [request.Observation.Candidates.Single(item => item.Id == "safe_idle")] },
            }, cancellationToken);
    }
}
