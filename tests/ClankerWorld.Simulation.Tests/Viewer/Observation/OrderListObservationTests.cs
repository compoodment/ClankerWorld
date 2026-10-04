using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using OwnerWorldInstruction = ClankerWorld.GodotClient.UI.OwnerWorldInstruction;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// The agent card draws the orders the host reports and keeps no queue of its
/// own, so every open order, with its status and reason, has to reach the game
/// as the host holds it, and the same list has to come back after a reload.
/// </summary>
public sealed class OrderListObservationTests
{
    private const string Agent = "founder-ilya";
    private static readonly JsonSerializerOptions HostJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions GameJson = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task EveryOrderReachesTheGameWithItsHostStatusAndSurvivesReload()
    {
        using var genesis = new PrivateWorldRuntime("order-list-projection");
        // A full agent cannot eat yet, so the eating order stays open and says why.
        var state = genesis.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item.InhabitantId == Agent
                ? item with { HungerBasisPoints = 9_000 }
                : item).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state);
        var replaced = Submit(world, "replaced", "gather berries");
        var current = Submit(world, "current", "eat 3 berries");
        var queued = Enumerable.Range(1, 5)
            .Select(index => Submit(world, $"queued-{index}", "go to the berry patch", queue: true))
            .ToArray();
        var typo = Submit(world, "typo", "build a house");
        var suggestion = world.SubmitInstruction(new OwnerInstructionRequest("suggestion", "owner:test", Agent,
            OwnerInstructionKind.Suggestive, "Rest by the fire tonight."));
        var cancelled = world.CancelOrder(new OwnerOrderCancelRequest("cancel-last", "owner:test",
            world.ExportState().Society.Society.WorldId, Agent, queued[^1].InstructionId));
        Assert.True(cancelled.Changed);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var received = Received(world);
        var orders = received.Where(item => item.Kind == "must_do").ToArray();
        // More orders than the card's four-message history holds all reach the game.
        Assert.Equal(
            new[] { replaced, current }.Concat(queued).Append(typo).Select(item => item.InstructionId).ToArray(),
            orders.Select(item => item.InstructionId).ToArray());
        Assert.Contains(received, item => item.InstructionId == suggestion.InstructionId && item.Kind == "suggestive");

        var held = Assert.Single(world.ExportState().Instructions!, item => item.InstructionId == current.InstructionId).Order!;
        var shown = orders[1].Order!;
        Assert.Equal("blocked", shown.Status);
        Assert.Equal(held.Status, shown.Status);
        Assert.Equal("Waiting until hungry enough to eat.", shown.BlockedReason);
        Assert.Equal(held.BlockedReason, shown.BlockedReason);
        Assert.Equal((3, 0, "food_items"), (shown.RequestedUnits, shown.CompletedUnits, shown.ProgressUnit));
        Assert.All(orders.Skip(2).Take(4), item =>
        {
            Assert.Equal("queued", item.State);
            Assert.Equal("queued", item.Order!.Status);
        });
        Assert.Equal(("completed", "cancelled", "Replaced by a newer order."),
            (orders[0].State, orders[0].Order!.Status, orders[0].Order!.BlockedReason));
        Assert.Equal(("completed", "cancelled"), (orders[6].State, orders[6].Order!.Status));
        Assert.Equal(("completed", "not_understood"), (orders[7].State, orders[7].Order!.Status));

        // A reload shows the same orders in the same states; nothing on the card depends on the old session.
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal(received, Received(reloaded));
    }

    private static OwnerInstructionReceipt Submit(PrivateWorldRuntime world, string key, string text, bool queue = false) =>
        world.SubmitInstruction(new OwnerInstructionRequest(key, "owner:test", Agent, OwnerInstructionKind.MustDo, text, queue));

    /// <summary>The agent's messages as the game reads them from the host's snapshot.</summary>
    private static OwnerWorldInstruction[] Received(PrivateWorldRuntime world) =>
        JsonSerializer.Deserialize<OwnerWorldInstruction[]>(
                JsonSerializer.Serialize(new OwnerWorldObservationStore(world).GetSnapshot().Instructions, HostJson), GameJson)!
            .Where(item => item.TargetInhabitantId == Agent)
            .ToArray();
}
