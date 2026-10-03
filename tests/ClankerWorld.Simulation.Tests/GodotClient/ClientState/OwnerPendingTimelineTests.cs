using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed class OwnerPendingTimelineTests
{
    [Theory]
    [InlineData("instruction")]
    [InlineData("cancel")]
    [InlineData("authoring")]
    public void SameWorldReplacementKeepsPendingPayloadButCannotRebindOrRetryIt(string kind)
    {
        var directory = Directory.CreateTempSubdirectory("clanker-pending-timeline-");
        try
        {
            var path = Path.Combine(directory.FullName, "pending.json");
            var store = new OwnerPendingSubmissionStore(path);
            var originalTimeline = new OwnerObserverTimeline("host-instance-a", 4);
            var binding = Binding(originalTimeline);
            var pending = kind switch
            {
                "instruction" => OwnerPendingSubmission.ForInstruction(binding,
                    new("exact-instruction-key", "camp-alpha", "must_do", "Gather these exact berries.", "world-A", true)),
                "cancel" => OwnerPendingSubmission.ForOrderCancel(binding,
                    new("exact-cancel-key", "camp-alpha", "private-instruction-0000000007", "world-A")),
                _ => OwnerPendingSubmission.ForAuthoring(binding,
                    new("exact-authoring-key", [new("terrain", null, "meadow", null, 2, 3, null)])),
            };
            Assert.True(store.TrySave(pending));
            var bytes = File.ReadAllBytes(path);

            foreach (var nextTimeline in new[] { new OwnerObserverTimeline("host-instance-a", 5), new("host-instance-b", 0) })
            {
                var nextBinding = Binding(nextTimeline);
                Assert.Null(store.TryLoad(nextBinding));
                var historical = Assert.IsType<OwnerPendingSubmission>(store.TryLoadForRegistration(nextBinding));
                Assert.Equal(binding, historical.Binding);
                Assert.Equal(pending.LogicalId, historical.LogicalId);
                Assert.False(historical.Binding.CanRetryIn("world-A", nextTimeline));
                Assert.False(historical.Binding.CanRetryIn("world-B", originalTimeline));
                Assert.True(historical.Binding.CanRetryIn("world-A", originalTimeline));
                Assert.Equal(pending.Instruction?.ToAction(), historical.Instruction?.ToAction());
                Assert.Equal(pending.OrderCancel?.ToAction(), historical.OrderCancel?.ToAction());
                if (pending.Authoring is { } authoring)
                {
                    Assert.Equal(authoring.BatchId, historical.Authoring!.BatchId);
                    Assert.Equal(authoring.Operations, historical.Authoring.Operations);
                }
                Assert.Equal(bytes, File.ReadAllBytes(path));
            }

            var original = Assert.IsType<OwnerPendingSubmission>(store.TryLoad(binding));
            Assert.True(original.Binding.CanRetryIn("world-A", originalTimeline));
            Assert.Null(store.TryLoadForRegistration(binding with { DeviceId = "another-device" }));
            Assert.True(store.TryClear(original));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void LegacyPendingRequestStaysLegacyInsteadOfAdoptingTheFirstAdvertisedTimeline()
    {
        var directory = Directory.CreateTempSubdirectory("clanker-pending-legacy-timeline-");
        try
        {
            var store = new OwnerPendingSubmissionStore(Path.Combine(directory.FullName, "pending.json"));
            var legacy = OwnerPendingSubmissionBinding.Create(new("authority-01", "installation-01"),
                "device-01", "sha256:public-key", new Uri("https://clanker.tailnet.example:8443/"));
            var action = new OwnerInstructionAction("legacy-key", "camp-alpha", "suggestive", "Keep this text.", "world-A");
            Assert.True(store.TrySave(OwnerPendingSubmission.ForInstruction(legacy, action)));
            Assert.True(legacy.CanRetryIn("world-A", null));
            var timeline = new OwnerObserverTimeline("host-instance-a", 0);
            var capable = Binding(timeline);

            Assert.Null(store.TryLoad(capable));
            var historical = Assert.IsType<OwnerPendingSubmission>(store.TryLoadForRegistration(capable));
            Assert.Null(historical.Binding.Timeline);
            Assert.Equal(action, historical.Instruction!.ToAction());
            Assert.False(historical.Binding.CanRetryIn("world-A", timeline));
            Assert.NotNull(store.TryLoad(legacy));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static OwnerPendingSubmissionBinding Binding(OwnerObserverTimeline timeline) =>
        OwnerPendingSubmissionBinding.Create(new OwnerAuthorityIdentity("authority-01", "installation-01"),
            "device-01", "sha256:public-key", new Uri("https://clanker.tailnet.example:8443/"), timeline, "world-A");
}
