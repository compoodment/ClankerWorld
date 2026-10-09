using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PurposefulExplorationTests
{
    [Fact]
    public async Task UntargetedMaterialOrderKeepsItsPurposePastEightStepsAndHandsOffOnlyAfterDiscoveryAcrossReplay()
    {
        var (state, actor, kind, source, index) = await MaterialCourse();
        using var world = Restore(state);
        var receipt = world.SubmitInstruction(new("purposeful-material", "owner:test", actor,
            OwnerInstructionKind.MustDo, "gather " + kind.Replace('_', ' ')));
        Assert.Equal("waiting", Order(world, receipt.InstructionId).Status);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var goal = Person(world, actor).Exploration!.Goal;
        Assert.Equal(new SettlementExplorationGoal("resource", kind, receipt.InstructionId), goal);
        Assert.Equal(0, Order(world, receipt.InstructionId).CompletedUnits);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var reached = new HashSet<GridPoint> { Person(world, actor).Position };
        var longOuting = false;
        var discovered = false;
        for (var tick = 0; tick < 100 && Order(world, receipt.InstructionId).Status != "finished"; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            reached.Add(Person(world, actor).Position);
            var facts = world.ExportState().Knowledge!.Facts.Where(fact => fact.OwnerId == actor).ToArray();
            Assert.All(facts, fact => Assert.Contains(reached, point => state.Map.FootDistance(fact.Position, point) <= 1));
            discovered |= facts.Any(fact => fact.Position == source.Position) ||
                state.Map.FootDistance(Person(world, actor).Position, source.Position) <= 1;
            var outing = Person(world, actor).Exploration!;
            longOuting |= outing.OutingPath.Count > 9;
            if (!discovered)
            {
                Assert.Equal(goal, outing.Goal);
                Assert.Equal(0, Order(world, receipt.InstructionId).CompletedUnits);
                Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == kind);
            }
        }
        Assert.True(index > 8);
        Assert.True(longOuting);
        Assert.True(discovered);
        Assert.Equal("finished", Order(world, receipt.InstructionId).Status);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == kind && lot.Quantity > 0);
        Assert.Contains(world.ExportState().Knowledge!.Facts, fact => fact.OwnerId == actor && fact.Position == source.Position);
        Assert.Null(Person(world, actor).Exploration!.Goal);
        Assert.Empty(Person(world, actor).Exploration!.OutingPath);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "exploration_goal_found" && item.Detail == actor + ":resource:" + kind);
        world.Validate();
    }

    [Theory]
    [InlineData("resource", "wood")]
    [InlineData("terrain", "Forest")]
    public async Task OrdinaryProjectNeedsOfferResourceAndTerrainPurposesWithoutRevealingUnseenSites(string kind, string target)
    {
        var (state, actor, _, source, _) = await MaterialCourse();
        var definition = state.WorldContent!.Buildings.Single(item => item.LocalId == "house-1x1");
        state = FarmFieldTests.WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != "wood").ToArray(),
        });
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Project = new(TownConstructionCandidateIds.Building(definition.CanonicalId, person.Position),
                    "Build a House", state.Society.Society.WorldTick, "paused",
                    LastTransitionTick: state.Society.Society.WorldTick, RequiresFreshChoice: true),
            } : person).ToArray(),
        };
        var chooser = new ScoutProvider($"explore_for:{kind}:{target}");
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? chooser : new ScoutProvider("safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(chooser.Offered.Contains("explore_for:resource:wood"),
            "Missing needed-wood choice: " + string.Join(',', chooser.Offered.Where(id => id.StartsWith("explore", StringComparison.Ordinal))) +
            "; actual project=" + Person(world, actor).Project);
        Assert.Contains("explore_for:terrain:Forest", chooser.Offered);
        Assert.Equal(new SettlementExplorationGoal(kind, target), Person(world, actor).Exploration!.Goal);
        var visible = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == actor);
        Assert.Equal("looking for " + target.ToLowerInvariant() + (kind == "terrain" ? " terrain" : ""), visible.PublicIntention!.Summary);
        Assert.Equal(visible.PublicIntention.Summary, ClankerWorld.GodotClient.UI.GameUiText.ActivityPhrase(visible.PublicIntention.CandidateId, null));
        Assert.DoesNotContain(world.ExportState().Knowledge!.Facts, fact => fact.OwnerId == actor && fact.Position == source.Position);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        Assert.False((await loaded.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        // Alter the stock of the unseen target only. Candidate IDs, chosen step
        // and saved purpose must not reveal that change.
        var withoutSource = state with
        {
            Resources = state.Resources.Select(item => item.ResourceId == source.Id ? item with { State = ResourceState.Depleted } : item).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(item => item.Id == source.Id
                        ? item with { Quantity = 0, State = EcologyResourceState.Depleted } : item).ToArray(),
                },
            },
        };
        var otherChooser = new ScoutProvider($"explore_for:{kind}:{target}");
        using var hidden = PrivateWorldRuntime.Restore(withoutSource, id => id == actor ? otherChooser : new ScoutProvider("safe_idle"));
        Assert.True((await hidden.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(chooser.Offered.Where(id => id.StartsWith("explore", StringComparison.Ordinal)),
            otherChooser.Offered.Where(id => id.StartsWith("explore", StringComparison.Ordinal)));
        Assert.Equal(Person(world, actor).Position, Person(hidden, actor).Position);
        Assert.Equal(Person(world, actor).Exploration!.Goal, Person(hidden, actor).Exploration!.Goal);
        for (var tick = 0; tick < 85 && Person(world, actor).Exploration!.Goal is not null; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Null(Person(world, actor).Exploration!.Goal);
        Assert.Empty(Person(world, actor).Exploration!.OutingPath);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "exploration_goal_found" &&
            item.Detail == $"{actor}:{kind}:{target}");
        if (kind == "terrain")
            Assert.Contains(world.ExportState().Knowledge!.Facts, fact => fact.OwnerId == actor &&
                fact.DiscovererId == actor && fact.Acquisition == "firsthand" && fact.Terrain == target &&
                fact.Position == Person(world, actor).Position);
        else
            Assert.Contains(world.ExportState().Knowledge!.Facts, fact => fact.OwnerId == actor && fact.Position == source.Position);
        var completed = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var completedReload = Restore(PrivateWorldRuntimeCodec.Decode(completed));
        Assert.Equal(completed, PrivateWorldRuntimeCodec.Encode(completedReload.ExportState()));
    }

    [Fact]
    public async Task FoodOrderRetainsItsKindThroughNonmatchingDiscoveriesAndCancellationReturnsWithoutCredit()
    {
        var (state, actor, _, _, _) = await MaterialCourse();
        var chooser = new ScoutProvider("safe_idle");
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? chooser : new ScoutProvider("safe_idle"));
        var receipt = world.SubmitInstruction(new("find-fruit", "owner:test", actor, OwnerInstructionKind.MustDo, "harvest fruit"));
        for (var tick = 0; tick < 40 && (Person(world, actor).Exploration?.OutingPath.Count ?? 0) < 12; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(new SettlementExplorationGoal("resource", "fruit", receipt.InstructionId), Person(world, actor).Exploration!.Goal);
        Assert.True(Person(world, actor).Exploration!.OutingPath.Count > 8);
        Assert.Contains(world.ExportState().Knowledge!.Facts, fact => fact.OwnerId == actor && fact.ResourceKinds.Contains("construction"));
        Assert.Equal(0, Order(world, receipt.InstructionId).CompletedUnits);
        var origin = Person(world, actor).Exploration!.OutingPath[0];
        world.CancelOrder(new("stop-fruit", "owner:test", world.Society.WorldId, actor, receipt.InstructionId));
        chooser.Preferred = "explore_return";
        world.SubmitInstruction(new("return-after-fruit", "owner:test", actor, OwnerInstructionKind.Suggestive, "Please return to where the trip started."));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved),
            id => new ScoutProvider(id == actor ? "explore_return" : "safe_idle"));
        for (var tick = 0; tick < 50 && Person(world, actor).Exploration!.OutingPath.Count > 0; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(origin, Person(world, actor).Position);
        Assert.Empty(Person(world, actor).Exploration!.OutingPath);
        Assert.Null(Person(world, actor).Exploration!.Goal);
        Assert.Equal(("cancelled", 0), (Order(world, receipt.InstructionId).Status, Order(world, receipt.InstructionId).CompletedUnits));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "exploration_goal_found" && item.Detail.EndsWith(":fruit", StringComparison.Ordinal));
        world.Validate();
    }

    [Fact]
    public async Task ExplicitFoodTargetReplacesAnUntargetedPurposeWithoutCreditingAnUnrelatedDiscovery()
    {
        var (state, actor, _, source, _) = await MaterialCourse();
        using var world = Restore(state);
        var original = world.SubmitInstruction(new("fruit-before-target", "owner:test", actor, OwnerInstructionKind.MustDo, "harvest fruit"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("fruit", Person(world, actor).Exploration!.Goal!.Target);
        var targeted = world.SubmitInstruction(new("exact-fruit-site", "owner:test", actor, OwnerInstructionKind.MustDo,
            $"harvest fruit at ({source.Position.X}, {source.Position.Y})"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Null(Person(world, actor).Exploration!.Goal);
        Assert.Equal(source.Position, Order(world, targeted.InstructionId).TargetPosition);
        Assert.Equal(0, Order(world, targeted.InstructionId).CompletedUnits);
        Assert.Equal("cancelled", Order(world, original.InstructionId).Status);
        Assert.Equal(1, world.WorldSystems.Ecology.GetResource(source.Id).Quantity);
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }

    [Fact]
    public async Task FoodNeedOffersAPurposeAlongsideCuriosityWithoutAnOrder()
    {
        var (state, actor, _, _, _) = await MaterialCourse();
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId;
        state = FarmFieldTests.WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != household &&
                (lot.OwnerId != actor || lot.ItemKind is "basket" or "wooden_axe" or "wooden_pickaxe")).ToArray(),
        }) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { HungerBasisPoints = 6_000 } : person).ToArray(),
        };
        var chooser = new ScoutProvider("explore_for:resource:food");
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? chooser : new ScoutProvider("safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains("explore", chooser.Offered);
        Assert.Contains("explore_for:resource:food", chooser.Offered);
        Assert.Equal(new SettlementExplorationGoal("resource", "food"), Person(world, actor).Exploration!.Goal);
        Assert.Empty(world.ExportState().Instructions!);
        world.Validate();
    }

    [Fact]
    public async Task StrictReloadRejectsForgedPurposesAndOrderLinksAndPreservesEarlierAlphaBytes()
    {
        var (state, actor, _, _, _) = await MaterialCourse();
        using var world = Restore(state);
        world.SubmitInstruction(new("validate-goal", "owner:test", actor, OwnerInstructionKind.MustDo, "gather wood"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var peer = world.Inhabitants.First(person => person.InhabitantId != actor).InhabitantId;
        var peerOrder = world.SubmitInstruction(new("peer-goal", "owner:test", peer, OwnerInstructionKind.MustDo, "gather wood"));
        state = world.ExportState();
        foreach (var goal in new SettlementExplorationGoal[]
        {
            new("resource", "invented"), new("terrain", "Ocean"), new("resource", "wood", "missing-order"),
            new("resource", "clay", Person(world, actor).Exploration!.Goal!.OrderInstructionId),
            new("resource", "wood", peerOrder.InstructionId),
        })
        {
            var invalid = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Exploration = person.Exploration! with { Goal = goal } } : person).ToArray()
            };
            Assert.Throws<InvalidDataException>(() => Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(invalid))));
        }
        var missingGoal = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!;
        var actorNode = missingGoal["state"]!["inhabitants"]!.AsArray()
            .Single(person => person!["inhabitantId"]!.GetValue<string>() == actor)!;
        actorNode["exploration"]!.AsObject().Remove("goal");
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(missingGoal.ToJsonString())));
        var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!;
        document["state"]!["schemaVersion"] = PrivateWorldRuntime.StateSchemaVersion - 1;
        var previous = Encoding.UTF8.GetBytes(document.ToJsonString());
        var preserved = previous.ToArray();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(previous));
        Assert.Equal(preserved, previous);
    }

    private static async Task<(PrivateWorldRuntimeState State, string Actor, string Kind, MapResource Source, int Index)> MaterialCourse()
    {
        using var setup = NormalPathWorld.CreateGenerated("purposeful-scouting", _ => new ScoutProvider("safe_idle"));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = SettlementWeatherTestFixture.WithWeather(setup.ExportState(), WeatherKind.Clear);
        var actor = state.Inhabitants[0].InhabitantId;
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "scout-basket", "basket", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "scout-axe", "wooden_axe", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "scout-pick", "wooden_pickaxe", actor, 1);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                Survival = new(),
                Project = null,
                LastDecisionContext = null,
                TravelCooldownTicks = 0,
                Exploration = null,
                Equipment = person.InhabitantId == actor ? new(CarryAidLotId: "scout-basket") : person.Equipment,
            }).ToArray(),
            Knowledge = state.Knowledge! with { Facts = state.Knowledge.Facts.Where(fact => fact.OwnerId != actor).ToArray() },
        };
        // Use a real curiosity outing to locate the fixture's future observation.
        // Neither the implementation nor its requested purpose receives that position.
        using var scouting = PrivateWorldRuntime.Restore(state, id => new ScoutProvider(id == actor ? "explore" : "safe_idle"));
        for (var tick = 0; tick < 65 && (Person(scouting, actor).Exploration?.OutingPath.Count ?? 0) < 35; tick++)
            Assert.True((await scouting.AdvanceOneTickAsync()).Advanced);
        var path = Person(scouting, actor).Exploration!.OutingPath;
        var candidates = state.Map.Resources.Where(source => source.Kind is "wood" or "construction" && path.Contains(source.Position))
            .Select(source => (Kind: "wood", Source: source, Index: path.ToList().IndexOf(source.Position)))
            .Where(item => item.Index > 12).OrderBy(item => item.Index).ToArray();
        Assert.True(candidates.Length > 0, "The generated real scouting path must first discover a supported material after eight steps: " +
            string.Join(',', path.Select((point, at) => $"{at}:{string.Join('/', state.Map.Resources.Where(source => source.Position == point).Select(source => source.Kind))}")));
        var target = candidates[0];
        // Controlled stock boundary: only this existing generated tree has wood
        // left. Earlier observed empty trees must not finish the search.
        var exhausted = state.Map.Resources.Where(source => source.Kind is "wood" or "construction" && source.Id != target.Source.Id)
            .Select(source => source.Id).ToHashSet(StringComparer.Ordinal);
        state = state with
        {
            Resources = state.Resources.Select(resource => exhausted.Contains(resource.ResourceId)
                ? resource with { State = ResourceState.Depleted } : resource).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => exhausted.Contains(resource.Id)
                        ? resource with { Quantity = 0, State = EcologyResourceState.Depleted } : resource).ToArray(),
                },
            },
        };
        return (state, actor, target.Kind, target.Source, target.Index);
    }

    private static PlaytestInhabitantState Person(PrivateWorldRuntime world, string actor) => world.Inhabitants.Single(person => person.InhabitantId == actor);
    private static OwnerInstructionOrder Order(PrivateWorldRuntime world, string id) => world.ExportState().Instructions!.Single(item => item.InstructionId == id).Order!;
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new ScoutProvider("safe_idle"));

    private sealed class ScoutProvider(string preferred) : IDecisionProvider
    {
        public string Preferred { get; set; } = preferred;
        public IReadOnlyList<string> Offered { get; private set; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Offered = request.Observation.Candidates.Select(item => item.Id).ToArray();
            var candidate = request.Observation.Candidates.FirstOrDefault(item => item.Id == Preferred)
                ?? request.Observation.Candidates.FirstOrDefault(item => item.Id == "safe_idle")
                ?? request.Observation.Candidates[0];
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [candidate] } }, cancellationToken);
        }
    }
}
