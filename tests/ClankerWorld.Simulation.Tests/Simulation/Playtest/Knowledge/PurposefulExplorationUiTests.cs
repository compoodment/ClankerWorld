using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PurposefulExplorationTests
{
    [Theory]
    [InlineData("gather wood", "wood")]
    [InlineData("harvest fruit", "fruit")]
    public async Task ActualUntargetedOrderPurposeAppearsOnTheOwnerCardAndListAcrossPauseAndReload(string text, string target)
    {
        var (state, actor, _, _, _) = await MaterialCourse();
        using var world = Restore(state);
        var receipt = world.SubmitInstruction(new("show-scout-purpose", "owner:test", actor, OwnerInstructionKind.MustDo, text));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(new SettlementExplorationGoal("resource", target, receipt.InstructionId), Person(world, actor).Exploration!.Goal);
        AssertPurpose(world, actor, target);
        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = Restore(PrivateWorldRuntimeCodec.Decode(paused));
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
        AssertPurpose(loaded, actor, target);
        loaded.CancelOrder(new("cancel-shown-purpose", "owner:test", loaded.Society.WorldId, actor, receipt.InstructionId));
        var visible = new OwnerWorldObservationStore(loaded).GetSnapshot().Inhabitants.Single(person => person.Id == actor);
        Assert.NotEqual("looking for " + target, visible.PublicIntention!.Summary);
        Assert.Equal("safe_idle", visible.PublicIntention.CandidateId);
        Assert.Equal(("cancelled", 0), (Order(loaded, receipt.InstructionId).Status, Order(loaded, receipt.InstructionId).CompletedUnits));
        loaded.Validate();
    }

    private static void AssertPurpose(PrivateWorldRuntime world, string actor, string target)
    {
        var visible = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == actor);
        Assert.Equal("safe_idle", visible.PublicIntention!.CandidateId);
        Assert.Equal("looking for " + target, visible.PublicIntention.Summary);
        // Local Must Do execution retains the prior admitted model intention.
        // These are the actual card/list arguments; no intention identity is invented.
        Assert.Equal("looking for " + target, ClankerWorld.GodotClient.UI.GameUiText.ActivityPhrase(
            visible.PublicIntention.CandidateId, visible.PublicIntention.Summary));
        Assert.Equal("looking for " + target, ClankerWorld.GodotClient.UI.GameUiText.ActivityPhrase(
            "knowledge_write:field_record", visible.PublicIntention.Summary));
    }
}
