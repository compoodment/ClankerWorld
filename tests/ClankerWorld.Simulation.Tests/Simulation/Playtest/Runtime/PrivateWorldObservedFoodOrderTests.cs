using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Theory]
    [InlineData(false, "berries")]
    [InlineData(true, "fruit")]
    public async Task FullMapMemoryDoesNotBlockAnObservedFoodHarvestAcrossReload(bool orchard, string kind)
    {
        using var setup = CreateHarvestInstructionWorld(orchard, hungerBasisPoints: 9_500);
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = FullFoodOrderMemory(setup.ExportState());
        var actor = state.Inhabitants.Single(person => person.InhabitantId == HarvestInstructionActor);
        var target = state.Map.Resources.Where(resource => orchard ? resource.TreeKind == "orchard" : resource.Id == "berry-patch")
            .OrderBy(resource => state.Map.FootDistance(actor.Position, resource.Position)).First();
        using var ordered = PrivateWorldRuntime.Restore(state, _ => new DeterministicDecisionProvider());
        ordered.Validate();
        var receipt = ordered.SubmitInstruction(new OwnerInstructionRequest("observed-food", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, $"gather {kind} from {target.Id}"));
        var checkpoint = PrivateWorldRuntimeCodec.Encode(ordered.ExportState());
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(checkpoint), _ => new DeterministicDecisionProvider());
        world.Resume();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var harvested = world.ExportState();
        var order = harvested.Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal("finished", order.Status);
        Assert.Equal(1, order.CompletedUnits);
        Assert.Contains(receipt.InstructionId, harvested.CompletedInstructionIds!);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == HarvestInstructionActor && lot.ItemKind == kind && lot.Quantity > 0);
        Assert.Equal(AgentKnowledgeRules.MaximumFactsPerAgent, harvested.Knowledge!.Facts.Count(fact => fact.OwnerId == HarvestInstructionActor));
        Assert.DoesNotContain(harvested.Knowledge.Facts, fact => fact.OwnerId == HarvestInstructionActor && fact.Position == target.Position);
        world.Validate();
        using var replayed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(harvested)), _ => new DeterministicDecisionProvider());
        replayed.Resume();
        Assert.True((await replayed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(1, replayed.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!.CompletedUnits);
        Assert.Single(replayed.ExportState().Events, item => item.Kind == "instruction_applied" && item.Detail == receipt.InstructionId + ":harvest_food");
        replayed.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullMapMemoryKeepsUnobservedExactFoodTargetsPrivate(bool nameResource)
    {
        using var setup = CreateHarvestInstructionWorld(orchard: false, hungerBasisPoints: 9_500);
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = FullFoodOrderMemory(setup.ExportState());
        var actor = state.Inhabitants.Single(person => person.InhabitantId == HarvestInstructionActor);
        var target = state.Map.Resources.First(resource => resource.Kind == "food" &&
            state.Map.FootDistance(actor.Position, resource.Position) > 20 && state.Map.IsReachableOnFoot(actor.Position, resource.Position));
        var other = state.Inhabitants.First(person => person.InhabitantId != HarvestInstructionActor).InhabitantId;
        var otherFact = new AgentKnowledgeFact("other-food-order-site", other, other, target.Position,
            state.Map.TerrainKindAt(target.Position)!.Value.ToString(), ["berries"], state.Society.Society.WorldTick, "firsthand");
        state = state with { Knowledge = state.Knowledge! with { Facts = state.Knowledge.Facts.Append(otherFact).ToArray() } };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        world.Validate();
        var text = nameResource ? $"gather berries from {target.Id}" : $"gather berries at ({target.Position.X},{target.Position.Y})";
        var receipt = world.SubmitInstruction(new OwnerInstructionRequest("private-food", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, text));
        world.Resume();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var final = world.ExportState();
        var order = final.Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal(0, order.CompletedUnits);
        Assert.DoesNotContain(receipt.InstructionId, final.CompletedInstructionIds!);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == HarvestInstructionActor && lot.ItemKind == "berries");
        Assert.DoesNotContain(final.Knowledge!.Facts, fact => fact.OwnerId == HarvestInstructionActor && fact.Position == target.Position);
        Assert.Equal(nameResource ? target.Id : null, order.TargetResourceId);
        Assert.Equal(nameResource ? null : target.Position, order.TargetPosition);
        world.Validate();
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(final));
    }

    private static PrivateWorldRuntimeState FullFoodOrderMemory(PrivateWorldRuntimeState state)
    {
        // Controlled starting memory, not a claim that the actor played these visits.
        var resourceTiles = state.Map.Resources.Select(resource => resource.Position).ToHashSet();
        var facts = state.Map.Tiles.Where(tile => state.Map.IsPassable(tile.Position) && !resourceTiles.Contains(tile.Position))
            .Take(AgentKnowledgeRules.MaximumFactsPerAgent).Select((tile, index) => new AgentKnowledgeFact(
                "full-food-order-memory:" + index, HarvestInstructionActor, HarvestInstructionActor,
                tile.Position, tile.Terrain.ToString(), [], state.Society.Society.WorldTick, "firsthand")).ToArray();
        Assert.Equal(AgentKnowledgeRules.MaximumFactsPerAgent, facts.Length);
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                ? person with { Survival = new SurvivalCondition() } : person).ToArray(),
            Knowledge = state.Knowledge! with
            {
                Facts = state.Knowledge.Facts.Where(fact => fact.OwnerId != HarvestInstructionActor).Concat(facts).ToArray(),
            },
        };
    }
}
