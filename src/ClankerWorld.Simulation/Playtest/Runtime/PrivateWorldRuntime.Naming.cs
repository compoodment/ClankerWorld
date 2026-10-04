using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void ApplyChosenNameOutcome(
        CognitionDecisionRequest request,
        CognitionDecisionResponse response,
        CognitionAdmissionResult admission)
    {
        if (response.Provider != DecisionProviderKind.LargeLanguageModel ||
            !admission.Accepted ||
            admission.FellBack && admission.Outcome != "candidate_not_legal" &&
            !admission.Outcome.StartsWith("low_confidence:", StringComparison.Ordinal))
        {
            return;
        }

        var inhabitantId = request.Observation.InhabitantId;
        var inhabitant = society.Checkpoint.GetInhabitant(inhabitantId);
        if (!inhabitant.NeedsName)
        {
            return;
        }

        var chosenName = CognitionDecisionResponse.NormalizeChosenName(response.ChosenName);
        var chosenKey = chosenName is null ? null : InhabitantNameRules.CanonicalKey(chosenName);
        if (chosenName is null || chosenKey is null)
        {
            CloseNameRequest(inhabitantId);
            AppendEvent("agent_name_unavailable", inhabitantId);
            return;
        }

        var duplicate = InhabitantNameRules.IsTaken(society.Checkpoint, inhabitantId, chosenName);
        if (!duplicate)
        {
            if (!InhabitantNameRules.IsAllowedChildName(society.Checkpoint, inhabitantId, chosenName))
            {
                CloseNameRequest(inhabitantId);
                AppendEvent("agent_name_unavailable", inhabitantId);
                return;
            }
            society.Apply(checkpoint => SocietyFixture.RenameInhabitant(checkpoint, inhabitantId, chosenName));
            AppendEvent("agent_named", inhabitantId);
            return;
        }

        AppendEvent("agent_name_rejected", inhabitantId);
        if (request.Observation.IsNameRetry)
        {
            CloseNameRequest(inhabitantId);
            AppendEvent("agent_name_retry_exhausted", inhabitantId);
            return;
        }

        var queued = society.EnqueueCognition(new SocietyCognitionScheduleEntry(
            $"name-retry:{request.RequestId}",
            inhabitantId,
            PriorityFor(inhabitants[inhabitantId]),
            WorldTick,
            [SocietyCognitionScheduler.NameRetryTriggerId],
            request.Observation with { IsNameRetry = false }));
        if (queued)
        {
            AppendEvent("agent_name_retry_requested", inhabitantId);
            return;
        }

        // The original queue entry has just been consumed, so this is only a
        // defensive bound for an unexpectedly full or unavailable queue.
        CloseNameRequest(inhabitantId);
        AppendEvent("agent_name_retry_unavailable", inhabitantId);
    }

    private void CloseNameRequest(string inhabitantId) =>
        society.Apply(checkpoint => SocietyFixture.CloseNaming(checkpoint, inhabitantId));

    private void CloseUnresolvedNameRetry(CognitionDecisionRequest request)
    {
        if (!request.Observation.IsNameRetry)
        {
            return;
        }

        var inhabitantId = request.Observation.InhabitantId;
        var inhabitant = society.Checkpoint.GetInhabitant(inhabitantId);
        if (!inhabitant.NeedsName)
        {
            return;
        }

        CloseNameRequest(inhabitantId);
        AppendEvent("agent_name_retry_unusable", inhabitantId);
    }

}
