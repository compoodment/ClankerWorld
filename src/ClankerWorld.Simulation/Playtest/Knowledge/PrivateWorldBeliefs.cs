using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Safe operational signal for a belief-ledger transition. The belief text is
/// intentionally absent so diagnostics cannot disclose private memory content.
/// </summary>
public sealed record PrivateWorldBeliefTransition(
    long WorldTick,
    string OwnerId,
    string BeliefId,
    string Outcome,
    SocietyBeliefProvenance Provenance,
    int ConfidenceBasisPoints);

public sealed partial class PrivateWorldRuntime
{
    public event Action<PrivateWorldBeliefTransition>? AgentBeliefChanged;

    /// <summary>
    /// Persists a bounded belief in its owner's private ledger. This is not a
    /// world-event append and cannot mutate the world facts it describes.
    /// </summary>
    public SocietyAgentBelief RecordAgentBelief(SocietyAgentBelief belief) =>
        ApplyAgentBeliefChange(
            checkpoint => SocietyFixture.RecordAgentBelief(checkpoint, belief),
            belief?.Id.Trim() ?? throw new ArgumentNullException(nameof(belief)),
            "recorded");

    /// <summary>Supersedes only the named belief owned by the requesting agent.</summary>
    public SocietyAgentBelief CorrectAgentBelief(
        string ownerId,
        string beliefId,
        SocietyAgentBelief correction) =>
        ApplyAgentBeliefChange(
            checkpoint => SocietyFixture.CorrectAgentBelief(checkpoint, ownerId, beliefId, correction),
            correction?.Id.Trim() ?? throw new ArgumentNullException(nameof(correction)),
            "corrected");

    private SocietyAgentBelief ApplyAgentBeliefChange(
        Func<SocietyCheckpoint, SocietyCheckpoint> update,
        string beliefId,
        string outcome)
    {
        SocietyAgentBelief stored;
        PrivateWorldBeliefTransition transition;
        tickGate.Wait();
        try
        {
            gate.Wait();
            try
            {
                var updated = update(society.Checkpoint);
                ValidateBeliefEventSources(updated.Beliefs ?? [], events, eventHistoryFloor);
                society.Apply(_ => new SocietyOperationResult(updated));
                stored = (society.Checkpoint.Beliefs ?? []).Single(item => item.Id == beliefId);
                checkpointSchemaVersion = StateSchemaVersion;
                transition = new PrivateWorldBeliefTransition(
                    society.Checkpoint.WorldTick,
                    stored.OwnerId,
                    stored.Id,
                    outcome,
                    stored.Provenance,
                    stored.ConfidenceBasisPoints);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            tickGate.Release();
        }

        // Instrumentation is derived telemetry and must never become state or
        // make a committed simulation transition fail.
        try
        {
            AgentBeliefChanged?.Invoke(transition);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An optional diagnostic listener is not simulation authority.
        }

        return stored;
    }

    private static void ValidateBeliefEventSources(
        IEnumerable<SocietyAgentBelief> beliefs,
        IReadOnlyList<PlaytestWorldEvent> worldEvents,
        long eventHistoryFloor)
    {
        var retainedIds = worldEvents.Select(item => item.EventId).ToHashSet();
        foreach (var belief in beliefs)
        {
            if (belief.SourceEventId is { } sourceEventId && sourceEventId > eventHistoryFloor &&
                !retainedIds.Contains(sourceEventId))
                throw new InvalidDataException("An agent belief references an unknown world event.");
        }
    }
}
