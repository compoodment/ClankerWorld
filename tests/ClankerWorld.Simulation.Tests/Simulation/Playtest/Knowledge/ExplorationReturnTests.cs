using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ExplorationReturnTests
{
    [Theory]
    [InlineData("direct")]
    [InlineData("detour")]
    [InlineData("displaced_finish")]
    [InlineData("blocked")]
    public async Task ReturnKeepsItsDestinationAndDiscoveriesAcrossReload(string scenario)
    {
        using var initial = new PrivateWorldRuntime("return-detour-regression");
        var state = initial.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var origin = scenario == "blocked" ? new GridPoint(2, 2) : new GridPoint(0, 0);
        var position = scenario switch
        {
            "blocked" => new GridPoint(0, 0),
            "displaced_finish" => new GridPoint(1, 0),
            _ => new GridPoint(1, 1),
        };
        GridPoint[] path = scenario switch
        {
            "blocked" => [origin, new GridPoint(1, 1), position],
            "displaced_finish" => [origin],
            _ => [origin, position],
        };
        GridPoint[] otherPositions = scenario == "blocked"
            ? [new(1, 0), new(0, 1), new(1, 1)]
            : [scenario == "detour" ? new(0, 1) : new(4, 1), new(4, 2), new(4, 3)];
        var fact = new AgentKnowledgeFact("return-discovery", actor, actor, origin,
            (state.Map.TerrainKindAt(origin) ?? throw new InvalidOperationException()).ToString(),
            state.Map.Resources.Where(resource => resource.Position == origin).Select(resource => resource.Kind).ToArray(),
            0, "firsthand");
        state = state with
        {
            Knowledge = new PrivateWorldKnowledgeState([fact], []),
            Inhabitants = state.Inhabitants.Select((person, index) => person with
            {
                Position = index == 0 ? position : otherPositions[index - 1],
                HungerBasisPoints = 9_500,
                MoveWaitTicks = index == 0 && scenario == "blocked" ? 29 : 0,
                TravelCooldownTicks = 0,
                Exploration = index == 0 ? new SettlementExploration(path, path, 0, true, [origin]) : null,
            }).ToArray(),
        };
        IDecisionProvider Provider(string id) => new ReturnProvider(id == actor);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), Provider);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), Provider);
        for (var tick = 0; tick < 5; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        restored.Validate();
        var result = restored.ExportState();
        var explorer = restored.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Empty(explorer.Exploration!.OutingPath);
        Assert.DoesNotContain(result.Events, item => item.Kind == "exploration_aborted" && item.Detail == actor + ":interrupted_movement");
        var remembered = Assert.Single(result.Knowledge!.Facts);
        Assert.Equal((fact.Id, actor, actor, origin, fact.Terrain, "firsthand"),
            (remembered.Id, remembered.OwnerId, remembered.DiscovererId, remembered.Position, remembered.Terrain, remembered.Acquisition));
        Assert.Equal(fact.ResourceKinds, remembered.ResourceKinds);
        Assert.Empty(result.Knowledge.Artifacts);
        if (scenario == "blocked")
        {
            Assert.Equal(position, explorer.Position);
            Assert.Contains(result.Events, item => item.Kind == "exploration_aborted" && item.Detail == actor + ":return_blocked");
            Assert.DoesNotContain(result.Events, item => item.Kind == "exploration_completed");
        }
        else
        {
            Assert.Equal(origin, explorer.Position);
            Assert.Contains(result.Events, item => item.Kind == "exploration_completed" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        }
        result = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(result));
        await WriteReturnedDiscoveryAsync(result, actor, remembered);
    }

    private static async Task WriteReturnedDiscoveryAsync(PrivateWorldRuntimeState state, string actor, AgentKnowledgeFact fact)
    {
        // The blocked return has already been proved. Move its three blockers away
        // only for the separate journey to a writing House; keep the author in place.
        GridPoint[] otherPositions = [new(4, 1), new(4, 2), new(4, 3)];
        state = state with
        {
            Inhabitants = state.Inhabitants.Select((person, index) => person with
            {
                Position = index == 0 ? person.Position : otherPositions[index - 1],
                TravelCooldownTicks = 0,
                LastDecisionContext = null,
            }).ToArray(),
        };
        using var activating = PrivateWorldRuntime.Restore(state, _ => new ReturnProvider(false));
        Assert.True(activating.StageStarterContent());
        for (var tick = 0; tick < 8; tick++) Assert.True((await activating.AdvanceOneTickAsync()).Advanced);
        state = activating.ExportState();
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var authorPosition = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        var occupied = state.Map.CampObjects.Select(item => item.Position).Concat(state.Map.Resources.Select(item => item.Position))
            .Concat(state.RoadTiles ?? []).Concat(state.Inhabitants.Select(person => person.Position))
            .Concat(state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))).ToHashSet();
        var housePosition = state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) && !occupied.Contains(tile.Position))
            .OrderBy(tile => state.Map.FootDistance(authorPosition, tile.Position)).First().Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "return-writing-wood", "wood", household, 8);
        using var placing = PrivateWorldRuntime.Restore(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        }, _ => new ReturnProvider(false));
        var placed = placing.PlaceBuilding("return-writing-house", HouseContent.House1x1().CanonicalId, housePosition, household);
        Assert.True(placed.Applied, placed.Failure);
        state = placing.ExportState();
        inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "return-writing-paper", "paper", household, 1,
            storageBuildingId: "return-writing-house");
        using var writing = PrivateWorldRuntime.Restore(state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        }, id => id == actor ? new WritingProvider() : new ReturnProvider(false));
        Assert.NotEqual(housePosition, authorPosition);
        for (var tick = 0; tick < 40 && writing.Knowledge.Artifacts.Count == 0; tick++)
            Assert.True((await writing.AdvanceOneTickAsync()).Advanced);
        var written = writing.ExportState();
        Assert.Equal(housePosition, written.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Contains(written.Events, item => item.Kind == "inhabitant_moved" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal) && item.Detail.EndsWith(":knowledge_writing", StringComparison.Ordinal));
        var artifact = Assert.Single(written.Knowledge!.Artifacts);
        var copiedFact = Assert.Single(artifact.Facts);
        Assert.Equal((fact.Id, fact.OwnerId, fact.DiscovererId, fact.Position, fact.Terrain, fact.Acquisition),
            (copiedFact.Id, copiedFact.OwnerId, copiedFact.DiscovererId, copiedFact.Position, copiedFact.Terrain, copiedFact.Acquisition));
        Assert.Equal(fact.ResourceKinds, copiedFact.ResourceKinds);
        Assert.Equal((actor, "field_record", "return-writing-house"), (artifact.CreatorId, artifact.Kind, artifact.WritingBuildingId));
        var stock = written.Society.Society.Inventory;
        var paper = stock.GetReservation(Assert.Single(artifact.InputReservationIds!));
        Assert.Equal((household, "return-writing-paper", 1, "knowledge-writing:" + artifact.Id, InventoryReservationState.Completed),
            (paper.OwnerId, paper.LotId, paper.Quantity, paper.Purpose, paper.State));
        Assert.DoesNotContain(stock.Lots, lot => lot.Id == "return-writing-paper");
        Assert.Equal((actor, "field_record", 1),
            (stock.GetLot(artifact.LotId).OwnerId, stock.GetLot(artifact.LotId).ItemKind, stock.GetLot(artifact.LotId).Quantity));
        using var saved = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(written)),
            _ => new ReturnProvider(false));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(written), PrivateWorldRuntimeCodec.Encode(saved.ExportState()));
        saved.Validate();
    }

    private sealed class WritingProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.FirstOrDefault(item => item.Id == "knowledge_write:field_record")
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }

    private sealed class ReturnProvider(bool explore) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.FirstOrDefault(item => explore && item.Id == "explore")
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
