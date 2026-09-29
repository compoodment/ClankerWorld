using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed class OwnerAuthorityStoreTests
{
    [Fact]
    public void LocalPairingRecoveryPreservesApprovedRequestsAndRejectsInvalidKeysBeforeEviction()
    {
        var store = NewStore(NewClock());
        using var activeKey = CreateP256Key();
        var active = StartPairing(store, activeKey);
        Assert.True(store.ApprovePendingPairingLocally(active.PairingId, active.PairingCode).IsSuccess);
        Assert.True(store.ActivatePairing(new(active.PairingId, active.ActivationCanonicalProof,
            Sign(activeKey, active.ActivationCanonicalProof))).IsSuccess);
        using var approvedKey = CreateP256Key();
        var approved = StartPairing(store, approvedKey);
        Assert.True(store.ApprovePendingPairingLocally(approved.PairingId, approved.PairingCode).IsSuccess);
        for (var index = 1; index < OwnerAuthorityStore.MaximumPendingPairings; index++)
        {
            using var key = CreateP256Key();
            StartPairing(store, key);
        }
        Assert.False(store.StartPairingLocally(new("invalid")).IsSuccess);
        Assert.Equal(OwnerAuthorityFailure.PublicKeyAlreadyRegistered,
            store.StartPairingLocally(new(Convert.ToBase64String(activeKey.ExportSubjectPublicKeyInfo()))).Failure);
        Assert.Equal(OwnerAuthorityStore.MaximumPendingPairings,
            store.ExportState().Pairings.Count(item => item.State is OwnerPairingState.Pending or OwnerPairingState.Approved));
        using var ownerKey = CreateP256Key();
        var recovered = store.StartPairingLocally(new(Convert.ToBase64String(ownerKey.ExportSubjectPublicKeyInfo())));
        Assert.True(recovered.IsSuccess);
        Assert.Equal(OwnerPairingState.Approved, store.GetPairingStatus(approved.PairingId).Value!.State);
        Assert.Single(store.ExportState().Pairings, item => item.State == OwnerPairingState.Expired);
    }

    [Fact]
    public void LocalApprovalRejectsTheWrongVisibleCodeWithoutChangingPendingState()
    {
        var clock = NewClock();
        var store = NewStore(clock);
        using var deviceKey = CreateP256Key();
        var pairing = StartPairing(store, deviceKey);
        var wrongCode = pairing.PairingCode == "000000" ? "000001" : "000000";

        var approval = store.ApprovePendingPairingLocally(pairing.PairingId, wrongCode);
        var status = store.GetPairingStatus(pairing.PairingId);

        Assert.Equal(OwnerAuthorityFailure.PairingCodeMismatch, approval.Failure);
        Assert.True(status.IsSuccess);
        Assert.Equal(OwnerPairingState.Pending, status.Value!.State);
    }

    [Fact]
    public void PairingVisibleCodeAttemptsAreBoundedAndSurviveRestore()
    {
        var clock = NewClock();
        var store = NewStore(clock);
        using var deviceKey = CreateP256Key();
        var pairing = StartPairing(store, deviceKey);
        var wrongCode = pairing.PairingCode == "000000" ? "000001" : "000000";

        for (var attempt = 0; attempt < OwnerAuthorityStore.MaximumWrongVisibleCodeAttempts - 1; attempt++)
        {
            var rejected = store.ApprovePendingPairingLocally(pairing.PairingId, wrongCode);
            Assert.Equal(OwnerAuthorityFailure.PairingCodeMismatch, rejected.Failure);
        }

        var persistedState = JsonSerializer.Deserialize<OwnerAuthorityState>(
            JsonSerializer.Serialize(store.ExportState()));
        Assert.NotNull(persistedState);
        var restored = OwnerAuthorityStore.Restore(
            persistedState,
            clock,
            CryptographicOwnerAuthorityRandom.Instance);
        var finalRejectedAttempt = restored.ApprovePendingPairingLocally(pairing.PairingId, wrongCode);
        var status = restored.GetPairingStatus(pairing.PairingId);
        var afterLockout = restored.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode);

        Assert.Equal(OwnerAuthorityFailure.PairingCodeMismatch, finalRejectedAttempt.Failure);
        Assert.True(status.IsSuccess);
        Assert.Equal(OwnerPairingState.Expired, status.Value!.State);
        Assert.Equal(OwnerAuthorityFailure.PairingExpired, afterLockout.Failure);
    }

    [Fact]
    public void LegacyPairingStateWithoutAttemptCountRestoresWithTheDefault()
    {
        var clock = NewClock();
        var store = NewStore(clock);
        using var deviceKey = CreateP256Key();
        var pairing = StartPairing(store, deviceKey);
        var currentState = store.ExportState();
        var legacyState = currentState with
        {
            Pairings = currentState.Pairings
                .Select(stored => new OwnerStoredPairing(
                    stored.PairingId,
                    stored.DeviceId,
                    stored.PublicKeySpkiBase64,
                    stored.PublicKeyFingerprint,
                    stored.VisibleCodeHashBase64,
                    stored.ExpiresAtUtc,
                    stored.ActivationCanonicalProof,
                    stored.State))
                .ToArray(),
        };

        var restored = OwnerAuthorityStore.Restore(
            legacyState,
            clock,
            CryptographicOwnerAuthorityRandom.Instance);
        var approval = restored.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode);

        Assert.True(approval.IsSuccess);
    }

    [Fact]
    public void PendingPairingsAreCappedAndExpiredPairingHistoryIsTrimmed()
    {
        var clock = NewClock();
        var store = NewStore(clock);
        using var renewalKey = CreateP256Key();

        for (var index = 0; index < OwnerAuthorityStore.MaximumPendingPairings; index++)
        {
            using var key = CreateP256Key();
            StartPairing(store, key);
        }

        using var overCapacityKey = CreateP256Key();
        var overCapacity = store.StartPairing(
            new OwnerPairingRequest(Convert.ToBase64String(overCapacityKey.ExportSubjectPublicKeyInfo())));

        Assert.Equal(OwnerAuthorityFailure.PairingCapacityExceeded, overCapacity.Failure);

        clock.UtcNow = clock.UtcNow.Add(OwnerAuthorityStore.PendingPairingLifetime);
        for (var index = 0; index <= OwnerAuthorityStore.MaximumRetainedExpiredPairings; index++)
        {
            StartPairing(store, renewalKey);
            clock.UtcNow = clock.UtcNow.Add(OwnerAuthorityStore.PendingPairingLifetime);
        }

        var exported = store.ExportState();

        Assert.Equal(
            OwnerAuthorityStore.MaximumRetainedExpiredPairings,
            exported.Pairings.Count(pairing => pairing.State == OwnerPairingState.Expired));
    }

    [Fact]
    public void PendingPairingExpiresAfterTenMinutesOfOperationalTime()
    {
        var clock = NewClock();
        var store = NewStore(clock);
        using var deviceKey = CreateP256Key();
        var pairing = StartPairing(store, deviceKey);
        clock.UtcNow = clock.UtcNow.Add(OwnerAuthorityStore.PendingPairingLifetime);

        var approval = store.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode);
        var status = store.GetPairingStatus(pairing.PairingId);

        Assert.Equal(OwnerAuthorityFailure.PairingExpired, approval.Failure);
        Assert.True(status.IsSuccess);
        Assert.Equal(OwnerPairingState.Expired, status.Value!.State);
    }

    [Fact]
    public void ActivationRejectsAProofFromADifferentPrivateKey()
    {
        var clock = NewClock();
        var store = NewStore(clock);
        using var registeredKey = CreateP256Key();
        using var wrongKey = CreateP256Key();
        var pairing = StartPairing(store, registeredKey);
        var approval = store.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode);
        Assert.True(approval.IsSuccess);

        var activation = store.ActivatePairing(
            new OwnerPairingActivationRequest(
                pairing.PairingId,
                pairing.ActivationCanonicalProof,
                Sign(wrongKey, pairing.ActivationCanonicalProof)));
        var device = store.GetDevice(pairing.DeviceId);

        Assert.Equal(OwnerAuthorityFailure.InvalidSignature, activation.Failure);
        Assert.Equal(OwnerAuthorityFailure.DeviceNotFound, device.Failure);
    }

    [Fact]
    public void ApprovedDeviceActivatesOnlyAfterItProvesItsRegisteredPrivateKey()
    {
        var clock = NewClock();
        var store = NewStore(clock);
        using var deviceKey = CreateP256Key();
        var pairing = StartPairing(store, deviceKey);
        var approval = store.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode);
        Assert.True(approval.IsSuccess);

        var activation = store.ActivatePairing(
            new OwnerPairingActivationRequest(
                pairing.PairingId,
                pairing.ActivationCanonicalProof,
                Sign(deviceKey, pairing.ActivationCanonicalProof)));
        var status = store.GetPairingStatus(pairing.PairingId);

        Assert.True(activation.IsSuccess);
        Assert.Equal(pairing.DeviceId, activation.Value!.DeviceId);
        Assert.Equal(pairing.PublicKeyFingerprint, activation.Value.PublicKeyFingerprint);
        Assert.Equal(OwnerDeviceState.Active, activation.Value.State);
        Assert.True(status.IsSuccess);
        Assert.Equal(OwnerPairingState.Active, status.Value!.State);
    }

    [Fact]
    public void ChallengeCannotBeReplayedAndRevocationBlocksOutstandingChallenges()
    {
        var clock = NewClock();
        var store = NewStore(clock);
        using var deviceKey = CreateP256Key();
        var pairing = StartPairing(store, deviceKey);
        Assert.True(store.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode).IsSuccess);
        Assert.True(store.ActivatePairing(
            new OwnerPairingActivationRequest(
                pairing.PairingId,
                pairing.ActivationCanonicalProof,
                Sign(deviceKey, pairing.ActivationCanonicalProof))).IsSuccess);

        var firstChallenge = IssueChallenge(store, deviceKey, pairing.DeviceId, "challenge-request-1");
        const string firstBinding = "control-request-sha256:replay-proof";
        var firstConsumption = CreateConsumptionRequest(store.Identity, pairing.DeviceId, firstChallenge, firstBinding, deviceKey);

        var firstUse = store.ConsumeChallenge(firstConsumption);
        var replay = store.ConsumeChallenge(firstConsumption);

        Assert.True(firstUse.IsSuccess);
        Assert.Equal(OwnerAuthorityFailure.ChallengeConsumed, replay.Failure);

        var outstandingChallenge = IssueChallenge(store, deviceKey, pairing.DeviceId, "challenge-request-2");
        Assert.True(store.RevokeDeviceLocally(pairing.DeviceId).IsSuccess);
        var afterRevocation = store.ConsumeChallenge(
            CreateConsumptionRequest(
                store.Identity,
                pairing.DeviceId,
                outstandingChallenge,
                "control-request-sha256:revoked-device",
                deviceKey));
        var issueAfterRevocation = store.IssueChallenge(
            CreateChallengeIssueRequest(store.Identity, pairing.DeviceId, "challenge-request-3", deviceKey));

        Assert.Equal(OwnerAuthorityFailure.DeviceRevoked, afterRevocation.Failure);
        Assert.Equal(OwnerAuthorityFailure.DeviceRevoked, issueAfterRevocation.Failure);
    }

    [Fact]
    public void PendingChallengesAreCappedAndTerminalChallengeHistoryIsTrimmed()
    {
        var clock = NewClock();
        var store = NewStore(clock);
        using var deviceKey = CreateP256Key();
        var pairing = StartPairing(store, deviceKey);
        Assert.True(store.ApprovePendingPairingLocally(pairing.PairingId, pairing.PairingCode).IsSuccess);
        Assert.True(store.ActivatePairing(
            new OwnerPairingActivationRequest(
                pairing.PairingId,
                pairing.ActivationCanonicalProof,
                Sign(deviceKey, pairing.ActivationCanonicalProof))).IsSuccess);

        for (var index = 0; index < OwnerAuthorityStore.MaximumPendingChallengesPerDevice; index++)
        {
            IssueChallenge(store, deviceKey, pairing.DeviceId, $"pending-cap-{index}");
        }

        var overCapacity = store.IssueChallenge(
            CreateChallengeIssueRequest(store.Identity, pairing.DeviceId, "pending-cap-overflow", deviceKey));

        Assert.Equal(OwnerAuthorityFailure.ChallengeCapacityExceeded, overCapacity.Failure);

        clock.UtcNow = clock.UtcNow.Add(OwnerAuthorityStore.ChallengeLifetime);
        for (var index = 0; index <= OwnerAuthorityStore.MaximumRetainedTerminalChallenges; index++)
        {
            IssueChallenge(store, deviceKey, pairing.DeviceId, $"terminal-retention-{index}");
            clock.UtcNow = clock.UtcNow.Add(OwnerAuthorityStore.ChallengeLifetime);
        }

        var exported = store.ExportState();

        Assert.Equal(
            OwnerAuthorityStore.MaximumRetainedTerminalChallenges,
            exported.Challenges.Count(challenge => challenge.State != OwnerStoredChallengeState.Pending));
    }

    private static OwnerAuthorityStore NewStore(MutableClock clock) => new(
        new OwnerAuthorityIdentity("authority-test-1", "world-test-1"),
        clock,
        CryptographicOwnerAuthorityRandom.Instance);

    private static MutableClock NewClock() => new(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private static ECDsa CreateP256Key() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static OwnerPairingStart StartPairing(OwnerAuthorityStore store, ECDsa deviceKey)
    {
        var pairing = store.StartPairing(new OwnerPairingRequest(Convert.ToBase64String(deviceKey.ExportSubjectPublicKeyInfo())));

        Assert.True(pairing.IsSuccess);
        return pairing.Value!;
    }

    private static OwnerChallenge IssueChallenge(
        OwnerAuthorityStore store,
        ECDsa deviceKey,
        string deviceId,
        string requestId)
    {
        var issued = store.IssueChallenge(CreateChallengeIssueRequest(store.Identity, deviceId, requestId, deviceKey));

        Assert.True(issued.IsSuccess);
        return issued.Value!;
    }

    private static OwnerChallengeIssueRequest CreateChallengeIssueRequest(
        OwnerAuthorityIdentity identity,
        string deviceId,
        string requestId,
        ECDsa deviceKey)
    {
        var canonicalProof = OwnerAuthorityStore.CreateChallengeIssueCanonicalProof(identity, deviceId, requestId);
        return new OwnerChallengeIssueRequest(deviceId, requestId, canonicalProof, Sign(deviceKey, canonicalProof));
    }

    private static OwnerChallengeConsumeRequest CreateConsumptionRequest(
        OwnerAuthorityIdentity identity,
        string deviceId,
        OwnerChallenge challenge,
        string binding,
        ECDsa deviceKey)
    {
        var canonicalProof = OwnerAuthorityStore.CreateChallengeConsumeCanonicalProof(
            identity,
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding);
        return new OwnerChallengeConsumeRequest(
            deviceId,
            challenge.ChallengeId,
            challenge.Nonce,
            binding,
            canonicalProof,
            Sign(deviceKey, canonicalProof));
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
