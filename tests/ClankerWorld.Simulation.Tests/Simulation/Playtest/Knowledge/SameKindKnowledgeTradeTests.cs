using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class SameKindKnowledgeTradeTests
{
    [Theory]
    [InlineData("field_record", false)]
    [InlineData("field_map", false)]
    [InlineData("field_record", true)]
    public async Task NativeWrittenArtifactsTradeOnlyForReciprocalUnknownContents(string secondKind, bool shareFirst)
    {
        using var initial = NormalPathWorld.CreateGenerated("same-kind-record-trade", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        var state = ShelterOrderTestFixture.WithClearWeather(initial.ExportState());
        var adults = state.Society.Society.Inhabitants.Where(person => person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
            .Take(2).Select(person => person.Id).ToArray();
        Assert.Equal(2, adults.Length);
        var first = adults[0];
        var second = adults[1];
        var origin = state.Inhabitants.Single(person => person.InhabitantId == first).Position;
        var meeting = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse").Position;
        var blocked = state.Map.Resources.Select(resource => resource.Position)
            .Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)))
            .Concat(state.Inhabitants.Select(person => person.Position)).ToHashSet();
        var distant = state.Map.Tiles.Select(tile => tile.Position).Where(point => state.Map.IsBuildable(point) &&
                !blocked.Contains(point) && state.Map.FootDistance(origin, point) >= 24 && state.Map.IsReachableOnFoot(point, meeting))
            .OrderBy(point => state.Map.FootDistance(origin, point)).ThenBy(point => point.Y).ThenBy(point => point.X).First();
        state = ShelterOrderTestFixture.At(state, second, distant);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_500,
                LastDecisionContext = null,
                Project = null,
            }).ToArray(),
        };
        var phase = "explore";
        var returning = new HashSet<string>(StringComparer.Ordinal);
        var policy = new MarketRulesPolicy
        {
            Choose = (id, candidates) =>
            {
                var prefix = phase switch
                {
                    "explore" => returning.Contains(id) ? "explore_return" : "explore",
                    "write" => "knowledge_write:" + (id == first ? "field_record" : secondKind),
                    "share" => "knowledge_share:",
                    _ => id == first ? "trade_propose:" + second : "trade_accept:",
                };
                return adults.Contains(id) ? candidates.FirstOrDefault(candidate =>
                    candidate.Id.StartsWith(prefix, StringComparison.Ordinal) || phase == "write" && candidate.Id == "knowledge_continue" ||
                    phase == "trade" && candidate.Id.StartsWith("trade_wait:", StringComparison.Ordinal)) ??
                    candidates.Single(candidate => candidate.Id == "safe_idle") : candidates.Single(candidate => candidate.Id == "safe_idle");
            },
        };
        using (var exploring = PrivateWorldRuntime.Restore(state, policy.CreateProvider))
        {
            for (var tick = 0; tick < 64 && !adults.All(id => exploring.ExportState().Events.Any(item =>
                     item.Kind == "exploration_completed" && item.Detail.StartsWith(id + ":", StringComparison.Ordinal))); tick++)
            {
                Assert.True((await exploring.AdvanceOneTickAsync()).Advanced);
                foreach (var id in adults)
                    if (exploring.Inhabitants.Single(person => person.InhabitantId == id).Exploration is
                        { Returning: false, OutingPath.Count: > 8 } && returning.Add(id))
                        exploring.SubmitInstruction(new OwnerInstructionRequest("trade-record-return-" + id,
                            "owner:test", id, OwnerInstructionKind.Suggestive, "Please return from scouting."));
            }
            state = exploring.ExportState();
            foreach (var id in adults)
            {
                Assert.Contains(state.Events, item => item.Kind == "exploration_completed" && item.Detail.StartsWith(id + ":", StringComparison.Ordinal));
                Assert.True(state.Knowledge!.Facts.Count(fact => fact.OwnerId == id && fact.Acquisition == "firsthand") >= 2);
            }
        }
        var inventory = state.Society.Society.Inventory;
        foreach (var id in adults)
            inventory = InventoryFixture.AddLot(inventory, "record-trade-paper-" + id, "paper", id, 1);
        state = ShelterOrderTestFixture.WithInventory(state, inventory);
        phase = "write";
        using (var writing = PrivateWorldRuntime.Restore(state, policy.CreateProvider))
        {
            for (var tick = 0; tick < 24 && writing.ExportState().Knowledge!.Artifacts.Count < 2; tick++)
                Assert.True((await writing.AdvanceOneTickAsync()).Advanced);
            state = writing.ExportState();
        }
        Assert.Equal(2, state.Knowledge!.Artifacts.Count);
        var firstArtifact = Assert.Single(state.Knowledge.Artifacts, item => item.CreatorId == first);
        var secondArtifact = Assert.Single(state.Knowledge.Artifacts, item => item.CreatorId == second);
        foreach (var id in adults)
            Assert.DoesNotContain(state.Society.Society.Inventory.Lots, lot => lot.OwnerId == id && lot.ItemKind == "paper");
        Assert.All(firstArtifact.Facts, fact => Assert.DoesNotContain(state.Knowledge.Facts, known => known.OwnerId == second && known.Position == fact.Position));
        Assert.All(secondArtifact.Facts, fact => Assert.DoesNotContain(state.Knowledge.Facts, known => known.OwnerId == first && known.Position == fact.Position));
        state = ShelterOrderTestFixture.At(state, first, meeting);
        var neighboring = state.Map.FootNeighbors(meeting).First(point => state.Map.IsBuildable(point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        state = ShelterOrderTestFixture.At(state, second, neighboring);
        if (shareFirst)
        {
            phase = "share";
            using var sharing = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
            for (var tick = 0; tick < 12; tick++) Assert.True((await sharing.AdvanceOneTickAsync()).Advanced);
            state = sharing.ExportState();
            Assert.All(firstArtifact.Facts, fact => Assert.Contains(state.Knowledge!.Facts, known => known.OwnerId == second && known.Position == fact.Position));
            Assert.All(secondArtifact.Facts, fact => Assert.Contains(state.Knowledge!.Facts, known => known.OwnerId == first && known.Position == fact.Position));
        }
        var knownBefore = state.Knowledge!.Facts;
        phase = "trade";
        policy.Offered.Clear();
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 24; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(!shareFirst, policy.OfferedTo(first).Any(candidate => candidate.Id == "trade_propose:" + second));
        var final = world.ExportState();
        Assert.Equal(2, final.Knowledge!.Artifacts.Count);
        Assert.Equal(2, world.Society.Inventory.Lots.Where(lot => AgentKnowledgeRules.IsArtifactKind(lot.ItemKind)).Sum(lot => lot.Quantity));
        if (shareFirst)
        {
            Assert.Empty(world.Society.Inventory.Offers);
            Assert.Equal(first, world.Society.Inventory.GetLot(firstArtifact.LotId).OwnerId);
            Assert.Equal(second, world.Society.Inventory.GetLot(secondArtifact.LotId).OwnerId);
            Assert.Equal(JsonSerializer.Serialize(knownBefore), JsonSerializer.Serialize(final.Knowledge.Facts));
        }
        else
        {
            var settled = Assert.Single(world.Society.Inventory.Offers, offer => offer.State == DirectBarterState.Settled);
            Assert.Equal((first, second), (settled.FirstPartyId, settled.SecondPartyId));
            Assert.Equal(second, world.Society.Inventory.GetLot(firstArtifact.LotId).OwnerId);
            Assert.Equal(first, world.Society.Inventory.GetLot(secondArtifact.LotId).OwnerId);
            AssertLearned(first, second, secondArtifact);
            AssertLearned(second, first, firstArtifact);
            Assert.All(knownBefore, known => Assert.Contains(final.Knowledge.Facts,
                fact => JsonSerializer.Serialize(fact) == JsonSerializer.Serialize(known)));
        }
        bytes = PrivateWorldRuntimeCodec.Encode(final);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

        void AssertLearned(string recipient, string sender, AgentKnowledgeArtifact artifact)
        {
            var added = final.Knowledge.Facts.Where(fact => fact.OwnerId == recipient &&
                !knownBefore.Any(known => known.OwnerId == recipient && known.Position == fact.Position)).ToArray();
            Assert.Equal(artifact.Facts.Select(fact => fact.Position).OrderBy(point => point.Y).ThenBy(point => point.X),
                added.Select(fact => fact.Position).OrderBy(point => point.Y).ThenBy(point => point.X));
            Assert.All(added, fact =>
            {
                var original = Assert.Single(artifact.Facts, written => written.Position == fact.Position);
                Assert.Equal(("read", sender, artifact.Id, original.DiscovererId),
                    (fact.Acquisition, fact.SourceAgentId, fact.SourceArtifactId, fact.DiscovererId));
                Assert.Equal(original.Terrain, fact.Terrain);
                Assert.Equal(original.ResourceKinds, fact.ResourceKinds);
            });
        }
    }
}
