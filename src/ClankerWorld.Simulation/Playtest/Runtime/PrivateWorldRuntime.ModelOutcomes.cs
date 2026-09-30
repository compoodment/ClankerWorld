using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void RecordModelAttempt(string id, string status, string? acceptedCandidateId = null,
        string? setupBlocker = null)
    {
        if (!inhabitants.TryGetValue(id, out var physical)) return;
        var previous = physical.LastModelAttempt;
        var next = new PlaytestModelAttempt(status, WorldTick,
            acceptedCandidateId ?? previous?.LastAcceptedCandidateId,
            acceptedCandidateId is not null ? WorldTick : previous?.LastAcceptedTick, setupBlocker);
        if (next == previous) return;
        inhabitants[id] = physical with { LastModelAttempt = next };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent("model_attempt_status", $"{id}:{status}");
    }

    private void RecordModelCompletion(string id, CognitionAdmissionResult admission, string? failure)
    {
        if (admission.Accepted && !admission.FellBack)
            RecordModelAttempt(id, "ready", admission.Intention?.CandidateId);
        else
        {
            var outcome = failure ?? admission.Outcome;
            RecordModelAttempt(id, CognitionProviderFailures.Status(outcome),
                setupBlocker: outcome == "unsupported_request" ? "unsupported_request" : null);
        }
    }
}
