using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed class OwnerAuthorityStateFileTests
{
    [Fact]
    public void PairingAndDeviceStateSurviveAHostRestartWithoutPersistingTheVisibleCode()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-owner-authority-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = System.IO.Path.Combine(directory, "owner-authority.json");
            var identity = new OwnerAuthorityIdentity("authority-test-file", "world-test-file");
            var clock = new MutableClock(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
            var stateFile = new OwnerAuthorityStateFile(path);
            using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var first = stateFile.LoadOrCreate(identity, clock, CryptographicOwnerAuthorityRandom.Instance);
            var pairing = first.StartPairing(new OwnerPairingRequest(
                Convert.ToBase64String(deviceKey.ExportSubjectPublicKeyInfo()))).Value!;
            stateFile.Save(first);

            var persistedText = File.ReadAllText(path);
            Assert.DoesNotContain(pairing.PairingCode, persistedText, StringComparison.Ordinal);

            var restarted = stateFile.LoadOrCreate(identity, clock, CryptographicOwnerAuthorityRandom.Instance);
            var approval = restarted.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode);
            Assert.True(approval.IsSuccess);
            stateFile.Save(restarted);

            var activatedAfterRestart = stateFile.LoadOrCreate(identity, clock, CryptographicOwnerAuthorityRandom.Instance);
            var activationProof = OwnerAuthorityStore.CreatePairingActivationCanonicalProof(
                identity,
                pairing.PairingId,
                pairing.DeviceId,
                pairing.PublicKeyFingerprint);
            var activated = activatedAfterRestart.ActivatePairing(new OwnerPairingActivationRequest(
                pairing.PairingId,
                activationProof,
                Sign(deviceKey, activationProof)));
            Assert.True(activated.IsSuccess);
            stateFile.Save(activatedAfterRestart);

            var finalRestart = stateFile.LoadOrCreate(identity, clock, CryptographicOwnerAuthorityRandom.Instance);
            var device = finalRestart.GetDevice(pairing.DeviceId);
            Assert.True(device.IsSuccess);
            Assert.Equal(OwnerDeviceState.Active, device.Value!.State);

            var initialWrites = stateFile.WriteCount;
            OwnerChallengeConsumeRequest? oldRequest = null;
            for (var poll = 0; poll < 600; poll++)
            {
                var requestId = $"poll-{poll}";
                var issueProof = OwnerAuthorityStore.CreateChallengeIssueCanonicalProof(identity, pairing.DeviceId, requestId);
                var challenge = finalRestart.IssueChallenge(new OwnerChallengeIssueRequest(
                    pairing.DeviceId, requestId, issueProof, Sign(deviceKey, issueProof))).Value!;
                stateFile.Save(finalRestart);
                var consumeProof = OwnerAuthorityStore.CreateChallengeConsumeCanonicalProof(
                    identity, pairing.DeviceId, challenge.ChallengeId, challenge.Nonce, "read-snapshot");
                oldRequest = new OwnerChallengeConsumeRequest(pairing.DeviceId, challenge.ChallengeId,
                    challenge.Nonce, "read-snapshot", consumeProof, Sign(deviceKey, consumeProof));
                Assert.True(finalRestart.ConsumeChallenge(oldRequest).IsSuccess);
                Assert.False(finalRestart.ConsumeChallenge(oldRequest).IsSuccess);
                stateFile.Save(finalRestart);
                clock.UtcNow += TimeSpan.FromSeconds(1);
            }
            Assert.Equal(initialWrites, stateFile.WriteCount);
            var pendingIssueProof = OwnerAuthorityStore.CreateChallengeIssueCanonicalProof(identity, pairing.DeviceId, "pending-at-restart");
            var pendingChallenge = finalRestart.IssueChallenge(new OwnerChallengeIssueRequest(
                pairing.DeviceId, "pending-at-restart", pendingIssueProof, Sign(deviceKey, pendingIssueProof))).Value!;
            var pendingConsumeProof = OwnerAuthorityStore.CreateChallengeConsumeCanonicalProof(
                identity, pairing.DeviceId, pendingChallenge.ChallengeId, pendingChallenge.Nonce, "read-snapshot");
            var pendingRequest = new OwnerChallengeConsumeRequest(pairing.DeviceId, pendingChallenge.ChallengeId,
                pendingChallenge.Nonce, "read-snapshot", pendingConsumeProof, Sign(deviceKey, pendingConsumeProof));
            stateFile.Save(finalRestart);
            var afterPollingRestart = stateFile.LoadOrCreate(identity, clock);
            Assert.False(afterPollingRestart.ConsumeChallenge(oldRequest!).IsSuccess);
            Assert.False(afterPollingRestart.ConsumeChallenge(pendingRequest).IsSuccess);
            Assert.True(finalRestart.ConsumeChallenge(pendingRequest).IsSuccess);
            Assert.True(afterPollingRestart.GetDevice(pairing.DeviceId).IsSuccess);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void StateFileRejectsAnActiveDeviceThatDidNotComeFromAnActivePairing()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-owner-authority-tampered-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = System.IO.Path.Combine(directory, "owner-authority.json");
            var identity = new OwnerAuthorityIdentity("authority-test-file", "world-test-file");
            using var injectedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publicKey = Convert.ToBase64String(injectedKey.ExportSubjectPublicKeyInfo());
            var injectedDevice = new OwnerStoredDevice(
                "device_injected",
                publicKey,
                OwnerAuthorityStore.GetPublicKeyFingerprint(publicKey),
                OwnerDeviceState.Active,
                new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
                null);
            var tampered = new OwnerAuthorityState(1, identity, [], [injectedDevice], []);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(tampered));

            var stateFile = new OwnerAuthorityStateFile(path);
            var exception = Assert.Throws<InvalidDataException>(() => stateFile.LoadOrCreate(identity));

            Assert.Contains("inconsistent", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void StateFileRejectsAStateDocumentFromAnotherServerAuthority()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"clankerworld-owner-authority-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = System.IO.Path.Combine(directory, "owner-authority.json");
            var originalIdentity = new OwnerAuthorityIdentity("authority-original", "world-test-file");
            var stateFile = new OwnerAuthorityStateFile(path);
            stateFile.Save(new OwnerAuthorityStore(
                originalIdentity,
                SystemOwnerAuthorityClock.Instance,
                CryptographicOwnerAuthorityRandom.Instance));

            var exception = Assert.Throws<InvalidDataException>(() => stateFile.LoadOrCreate(
                new OwnerAuthorityIdentity("authority-replacement", originalIdentity.WorldId)));

            Assert.Contains("another server authority or world", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string Sign(ECDsa key, string canonicalProof) => Convert.ToBase64String(
        key.SignData(
            Encoding.UTF8.GetBytes(canonicalProof),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    private sealed class MutableClock(DateTimeOffset utcNow) : IOwnerAuthorityClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
