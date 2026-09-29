using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.Simulation.Tests;

public sealed class OwnerPendingSubmissionStoreTests
{
    [Fact]
    public void InstructionRoundTripPreservesItsExactIdempotencyKeyAndBinding()
    {
        using var fixture = new PendingStoreFixture();
        var binding = PendingStoreFixture.CreateBinding();
        var action = new OwnerInstructionAction("instruction_exact", "camp-alpha", "must_do", "Gather wood before dusk.");
        var pending = OwnerPendingSubmission.ForInstruction(binding, action);

        Assert.True(fixture.Store.TrySave(pending));

        var rawDocument = File.ReadAllText(fixture.FilePath);
        Assert.DoesNotContain("signature", rawDocument, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("challenge", rawDocument, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pairingCode", rawDocument, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKey", rawDocument, StringComparison.OrdinalIgnoreCase);

        var loaded = fixture.Store.TryLoad(binding);

        Assert.NotNull(loaded);
        Assert.True(loaded.IsInstruction);
        Assert.False(loaded.IsAuthoring);
        Assert.Equal("instruction_exact", loaded.LogicalId);
        Assert.Equal(action, loaded.Instruction!.ToAction());
        Assert.True(fixture.Store.TryClear(loaded));
        Assert.Null(fixture.Store.TryLoad(binding));
    }

    [Fact]
    public void AuthoringRoundTripPreservesBatchIdAndAllOperations()
    {
        using var fixture = new PendingStoreFixture();
        var binding = PendingStoreFixture.CreateBinding();
        var action = new OwnerAuthoringBatchAction(
            "batch_exact",
            [
                new OwnerAuthoringOperationAction("terrain", null, "meadow", null, 2, 3, null),
                new OwnerAuthoringOperationAction("resource", "berry-01", "berry", "ripe", 4, 5, true),
            ]);

        Assert.True(fixture.Store.TrySave(OwnerPendingSubmission.ForAuthoring(binding, action)));

        var loaded = fixture.Store.TryLoad(binding);

        Assert.NotNull(loaded);
        Assert.False(loaded.IsInstruction);
        Assert.True(loaded.IsAuthoring);
        Assert.Equal("batch_exact", loaded.LogicalId);
        var restored = loaded.Authoring!.ToAction();
        Assert.Equal(action.BatchId, restored.BatchId);
        Assert.Equal(action.Operations, restored.Operations);
    }

    [Fact]
    public void ClearRefusesToDiscardAChangedPendingRequest()
    {
        using var fixture = new PendingStoreFixture();
        var binding = PendingStoreFixture.CreateBinding();
        var pending = OwnerPendingSubmission.ForInstruction(
            binding,
            new OwnerInstructionAction("instruction-01", "camp-alpha", "suggestive", "Rest."));
        Assert.True(fixture.Store.TrySave(pending));

        var changed = pending with
        {
            Instruction = pending.Instruction! with { Text = "Do not use this changed payload." },
        };

        Assert.False(fixture.Store.TryClear(changed));
        Assert.Equal("instruction-01", fixture.Store.TryLoad(binding)!.LogicalId);
    }

    [Fact]
    public void DifferentAuthorityDeviceFingerprintOrOriginFailsClosed()
    {
        using var fixture = new PendingStoreFixture();
        var binding = PendingStoreFixture.CreateBinding();
        Assert.True(fixture.Store.TrySave(OwnerPendingSubmission.ForInstruction(
            binding,
            new OwnerInstructionAction("instruction-01", "camp-alpha", "suggestive", "Rest."))));

        var differentAuthority = OwnerPendingSubmissionBinding.Create(
            new OwnerAuthorityIdentity("other-authority", binding.Authority.WorldId),
            binding.DeviceId,
            binding.PublicKeyFingerprint,
            new Uri(binding.ServerOrigin));
        var differentDevice = OwnerPendingSubmissionBinding.Create(
            binding.Authority,
            "device-other",
            binding.PublicKeyFingerprint,
            new Uri(binding.ServerOrigin));
        var differentFingerprint = OwnerPendingSubmissionBinding.Create(
            binding.Authority,
            binding.DeviceId,
            "sha256:other",
            new Uri(binding.ServerOrigin));
        var differentOrigin = OwnerPendingSubmissionBinding.Create(
            binding.Authority,
            binding.DeviceId,
            binding.PublicKeyFingerprint,
            new Uri("https://other.tailnet.example:8443/"));

        Assert.Null(fixture.Store.TryLoad(differentAuthority));
        Assert.Null(fixture.Store.TryLoad(differentDevice));
        Assert.Null(fixture.Store.TryLoad(differentFingerprint));
        Assert.Null(fixture.Store.TryLoad(differentOrigin));
        Assert.NotNull(fixture.Store.TryLoad(binding));
    }

    [Fact]
    public void ExistingOrCorruptRecordCannotBeSilentlyOverwritten()
    {
        using var fixture = new PendingStoreFixture();
        var binding = PendingStoreFixture.CreateBinding();
        var first = OwnerPendingSubmission.ForInstruction(
            binding,
            new OwnerInstructionAction("instruction-first", "camp-alpha", "suggestive", "Rest."));
        var second = OwnerPendingSubmission.ForInstruction(
            binding,
            new OwnerInstructionAction("instruction-second", "camp-alpha", "must_do", "Work."));

        Assert.True(fixture.Store.TrySave(first));
        Assert.False(fixture.Store.TrySave(second));
        Assert.Equal("instruction-first", fixture.Store.TryLoad(binding)!.LogicalId);

        Assert.True(fixture.Store.TryForget());
        File.WriteAllText(fixture.FilePath, "{\"binding\":{\"signatureBase64\":\"must-not-be-accepted\"}}");
        Assert.Null(fixture.Store.TryLoad(binding));
        Assert.False(fixture.Store.TrySave(second));
    }

    [Fact]
    public void BindingRejectsPlaintextRemoteOriginAndCanonicalizesAllowedOrigin()
    {
        var authority = new OwnerAuthorityIdentity("authority-01", "world-01");

        Assert.Throws<ArgumentException>(() => OwnerPendingSubmissionBinding.Create(
            authority,
            "device-01",
            "sha256:key",
            new Uri("http://example.test:5188/")));

        var binding = OwnerPendingSubmissionBinding.Create(
            authority,
            "device-01",
            "sha256:key",
            new Uri("HTTPS://CLANKER.TAILNET.EXAMPLE:8443/"));
        var loopback = OwnerPendingSubmissionBinding.Create(
            authority,
            "device-01",
            "sha256:key",
            new Uri("http://127.0.0.1:5188/"));

        Assert.Equal("https://clanker.tailnet.example:8443/", binding.ServerOrigin);
        Assert.Equal("http://127.0.0.1:5188/", loopback.ServerOrigin);
    }

    private sealed class PendingStoreFixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-pending-submission-tests-{Guid.NewGuid():N}");

        public PendingStoreFixture()
        {
            Directory.CreateDirectory(directory);
            FilePath = System.IO.Path.Combine(directory, "pending.json");
            Store = new OwnerPendingSubmissionStore(FilePath);
        }

        public string FilePath { get; }

        public OwnerPendingSubmissionStore Store { get; }

        public static OwnerPendingSubmissionBinding CreateBinding() => OwnerPendingSubmissionBinding.Create(
            new OwnerAuthorityIdentity("authority-01", "world-01"),
            "device-01",
            "sha256:public-key",
            new Uri("https://clanker.tailnet.example:8443/"));

        public void Dispose()
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }
}
