using ClankerWorld.GodotClient.Pairing;

namespace ClankerWorld.GodotClient.ClientState;

/// <summary>
/// The non-secret half of a successful owner-device pairing. The matching
/// private P-256 key remains in the Windows current-user CNG store and is
/// deliberately neither represented nor written by this class.
/// </summary>
public sealed record OwnerDeviceRegistration(
    OwnerAuthorityIdentity Authority,
    string DeviceId,
    string PublicKeyFingerprint,
    string WorldUrl = "");
