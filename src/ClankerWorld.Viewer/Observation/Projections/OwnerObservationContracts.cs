namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// Authenticated reconnect response. The full capability handshake is coupled
/// to the signed observation so a public discovery handshake cannot be
/// mistaken for authority to inspect the world.
/// </summary>
public sealed record ViewerOwnerReconnect(ViewerHandshake Handshake, ViewerReconnectBaseline Baseline);
