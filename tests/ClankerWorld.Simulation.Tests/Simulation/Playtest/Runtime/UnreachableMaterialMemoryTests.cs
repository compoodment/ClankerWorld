using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class UnreachableMaterialMemoryTests
{
    private const string Actor = "founder:00000000000000000000000000000001";
    private static readonly GridPoint Origin = new(127, 59);
    private static readonly GridPoint Remote = new(19, 98);
    private static readonly GridPoint Local = new(131, 61);
    private static readonly Lazy<Task<byte[]>> Initial = new(CreateInitial);

    // Existing material-order exploration has no remembered source. A real island
    // source must not prevent an untargeted order scouting its current landmass;
    // reachable memories still harvest, and explicit destinations stay binding.
    [Theory]
    [InlineData("remote")]
    [InlineData("none")]
    [InlineData("local")]
    [InlineData("explicit-remote")]
    public async Task UntargetedGatheringCanExplorePastAnUnreachableMemory(string memory)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Initial.Value);
        var remote = state.Map.Resources.Single(source => source.Position == Remote && source.Kind == "fiber");
        var local = state.Map.Resources.Single(source => source.Position == Local && source.Kind == "fiber");
        Assert.False(DeterministicRouteFinder.TryFind(state.Map, Origin, Remote, out _));
        Assert.True(DeterministicRouteFinder.TryFind(state.Map, Origin, Local, out _));
        var remembered = memory == "local" ? local : remote;
        var facts = state.Knowledge!.Facts.Where(fact => fact.OwnerId != Actor).ToArray();
        if (memory != "none")
            facts = facts.Append(new AgentKnowledgeFact("island-material-memory", Actor, Actor,
                remembered.Position, state.Map.TerrainKindAt(remembered.Position)!.Value.ToString(),
                ["fiber"], state.Society.Society.WorldTick, "firsthand")).ToArray();
        state = state with { Knowledge = state.Knowledge with { Facts = facts } };
        var policy = IdlePolicy();
        using var world = PrivateWorldRuntime.Restore(state, policy.CreateProvider);
        var receipt = world.SubmitInstruction(new("island-gather", "owner:test", Actor,
            OwnerInstructionKind.MustDo, memory == "explicit-remote" ? "gather fiber from " + remote.Id : "gather fiber"));
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before), policy.CreateProvider);
        for (var tick = 0; tick < 20; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var result = world.ExportState();
        var order = result.Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        var explored = result.Events.Any(item => item.Kind == "exploration_started" && item.Detail.StartsWith(Actor + ":", StringComparison.Ordinal));
        if (memory == "explicit-remote")
        {
            Assert.False(explored);
            Assert.Equal(("blocked", 0, remote.Id), (order.Status, order.CompletedUnits, order.TargetResourceId));
            Assert.Equal(Origin, result.Inhabitants.Single(person => person.InhabitantId == Actor).Position);
        }
        else if (memory == "local")
        {
            Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
            Assert.Contains(result.Events, item => item.Kind == "material_gathered" && item.Detail.StartsWith(Actor + ":", StringComparison.Ordinal));
            Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == Actor && lot.ItemKind == "fiber" && lot.Quantity > 0);
        }
        else Assert.True(explored, $"{memory}: {order.Status}, completed={order.CompletedUnits}, reason={order.BlockedReason}");
        if (memory is "remote" or "explicit-remote")
            Assert.Contains(result.Knowledge!.Facts, fact => fact.OwnerId == Actor && fact.Position == Remote && fact.ResourceKinds.Contains("fiber"));
        var bytes = PrivateWorldRuntimeCodec.Encode(result);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static MarketRulesPolicy IdlePolicy() => new(DecisionProviderKind.Deterministic)
    {
        Choose = (_, candidates) => candidates.Single(candidate => candidate.Id == "safe_idle"),
    };

    private static async Task<byte[]> CreateInitial()
    {
        var policy = IdlePolicy();
        using var generated = NormalPathWorld.CreateGenerated("island-lesson-audit-0", policy.CreateProvider);
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "island-material-basket", "basket", Actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "island-material-cloak", "rain_cloak", Actor, 1);
        state = PaidMarketWorld.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == Actor ? Origin : person.Position,
                HungerBasisPoints = 9_500,
                Survival = new SurvivalCondition(),
                Project = null,
                Exploration = null,
                TravelCooldownTicks = 0,
                LastDecisionContext = null,
                Equipment = person.InhabitantId == Actor ? new("island-material-cloak", "island-material-basket") : person.Equipment,
            }).ToArray(),
        };
        return PrivateWorldRuntimeCodec.Encode(state);
    }
}
