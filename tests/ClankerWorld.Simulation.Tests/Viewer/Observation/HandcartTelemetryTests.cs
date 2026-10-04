using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class HandcartTelemetryTests
{
    private static readonly string[] ActorIds = ["founder:1"];

    [Fact]
    public void CommittedMilestonesReportPhysicalAccountingWithoutPrivateDetailOrStepNoise()
    {
        var logger = new RecordingLogger<PrivateWorldRuntimeService>();
        var inventory = InventoryFixture.CreateGenesis([
            new("cart", "handcart", "founder:1", 1, 8_500, 10_000, 0, GroundPosition: new(1, 2)),
            new("load", "stone", "founder:1", 12, 10_000, 10_000, 0, ContainerLotId: "cart"),
        ]);
        HandcartTelemetry.Record(logger, new(1, 3, "handcart_loaded", "founder:1:cart:private-thought-provider-payload"), inventory, ActorIds);
        HandcartTelemetry.Record(logger, new(2, 3, "handcart_moved", "founder:1:cart"), inventory, ActorIds);
        HandcartTelemetry.Record(logger, new(3, 3, "handcart_blocked", "founder:1:no_route:private-text"), inventory, ActorIds);
        var messages = logger.Messages.ToArray();
        Assert.Equal(2, messages.Length);
        Assert.Contains("event=handcart_loaded inhabitant=founder:1 cart=cart condition=85 cargo=12", messages[0], StringComparison.Ordinal);
        Assert.Contains("event=handcart_blocked", messages[1], StringComparison.Ordinal);
        Assert.All(messages, message =>
        {
            Assert.DoesNotContain("private", message, StringComparison.Ordinal);
            Assert.DoesNotContain("provider-payload", message, StringComparison.Ordinal);
            Assert.DoesNotContain("handcart_moved", message, StringComparison.Ordinal);
        });
    }
}
