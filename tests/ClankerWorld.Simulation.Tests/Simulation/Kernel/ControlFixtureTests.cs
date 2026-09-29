using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed class ControlFixtureTests
{
    [Fact]
    public void CommandsAndMessagesRejectDuplicatesAndDeliverByDeclaredOrder()
    {
        var command = new DurableCommand("command-1", "key-1", "owner", "world", 0, 1, DurableCommandState.Queued);
        var current = ControlFixture.QueueCommand(ControlFixture.Genesis, command);
        var state = ControlDigest.State(current);
        Assert.Throws<InvalidOperationException>(() => ControlFixture.QueueCommand(current, command));
        Assert.Equal(state, ControlDigest.State(current));

        current = ControlFixture.QueueMessage(current, new DurableMessage("message-b", "a", "b", 0, 2, 2, "direct", 20, DurableMessageState.Pending));
        current = ControlFixture.QueueMessage(current, new DurableMessage("message-a", "a", "b", 0, 2, 1, "direct", 20, DurableMessageState.Pending));
        current = ControlFixture.AdvanceDelivery(current, 2);

        Assert.All(current.Messages, message => Assert.Equal(DurableMessageState.Delivered, message.State));
        var delivered = current.Events.Where(item => item.Kind == "message_delivered").Select(item => item.Detail).ToArray();
        Assert.Equal(2, delivered.Length);
        Assert.Equal("message-a", delivered[0]);
        Assert.Equal("message-b", delivered[1]);
    }

    [Fact]
    public void ProviderResultsCannotApplyAfterPauseRunEpochOrConfigurationSupersession()
    {
        var paused = ControlFixture.IssueProviderRequest(ControlFixture.Genesis, "paused-request");
        paused = ControlFixture.Pause(paused);
        paused = ControlFixture.ApplyProviderResult(paused, new ProviderResult("paused-request", 0, 0, "late"));
        Assert.Equal(ProviderRequestState.Rejected, paused.ProviderRequests.Single().State);
        Assert.Contains(paused.Events, item => item.Detail == "paused-request:paused");

        var resumed = ControlFixture.Resume(paused);
        resumed = ControlFixture.IssueProviderRequest(resumed, "config-request");
        var staleConfig = ControlFixture.SupersedeConfiguration(resumed);
        staleConfig = ControlFixture.ApplyProviderResult(staleConfig, new ProviderResult("config-request", 1, 0, "late"));
        Assert.Contains(staleConfig.Events, item => item.Detail == "config-request:configuration_epoch");

        var current = ControlFixture.IssueProviderRequest(staleConfig, "current-request");
        current = ControlFixture.ApplyProviderResult(current, new ProviderResult("current-request", 1, 1, "fresh"));
        Assert.Equal(ProviderRequestState.Applied, current.ProviderRequests.Single(request => request.Id == "current-request").State);
    }

    [Fact]
    public void CanonicalControlSaveRestoresTheExactStateAndEventDigest()
    {
        var current = ControlFixture.QueueCommand(ControlFixture.Genesis, new DurableCommand("command-1", "key-1", "owner", "world", 0, 1, DurableCommandState.Queued));
        current = ControlFixture.QueueMessage(current, new DurableMessage("message-1", "owner", "agent", 0, 1, 1, "direct", 10, DurableMessageState.Pending));
        current = ControlFixture.AdvanceDelivery(current, 1);
        var restored = ControlCheckpointCodec.Decode(ControlCheckpointCodec.Encode(current));

        Assert.Equal(ControlDigest.State(current), ControlDigest.State(restored));
        Assert.Equal(ControlDigest.Events(current.Events), ControlDigest.Events(restored.Events));
        Assert.True(ControlCheckpointCodec.Encode(current).SequenceEqual(ControlCheckpointCodec.Encode(restored)));
    }

    [Fact]
    public void PausedCheckpointsCannotAdvanceDeliveryOrMutateMessages()
    {
        var queued = ControlFixture.QueueMessage(
            ControlFixture.Pause(ControlFixture.Genesis),
            new DurableMessage("message-1", "owner", "agent", 0, 5, 1, "direct", 10, DurableMessageState.Pending));
        var before = ControlDigest.State(queued);

        Assert.Throws<InvalidOperationException>(() => ControlFixture.AdvanceDelivery(queued, 5));
        Assert.Equal(before, ControlDigest.State(queued));
        Assert.Equal(DurableMessageState.Pending, queued.Messages.Single().State);
        Assert.Equal(0, queued.WorldTick);
        Assert.DoesNotContain(queued.Events, item => item.Kind is "message_delivered" or "message_expired");
    }
}
