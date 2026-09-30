using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void ApplyPersonalIdentityChoice(string id, CognitionDecisionRequest request,
        CognitionDecisionResponse? response, CognitionAdmissionResult admission)
    {
        if (!admission.Accepted || admission.FellBack ||
            response is not { Provider: DecisionProviderKind.LargeLanguageModel } ||
            !request.Observation.NeedsPersonality && !request.Observation.NeedsAspiration ||
            !inhabitants.TryGetValue(id, out var physical) || !physical.IdentityChoicePending)
            return;

        var personality = CognitionDecisionResponse.NormalizeIdentityText(response.ChosenPersonality);
        var aspiration = CognitionDecisionResponse.NormalizeIdentityText(response.ChosenAspiration);
        inhabitants[id] = physical with
        {
            Personality = personality ?? physical.Personality,
            Aspiration = aspiration ?? physical.Aspiration,
            IdentityChoicePending = false,
        };
        checkpointSchemaVersion = StateSchemaVersion;
        AppendEvent(personality is not null || aspiration is not null ? "agent_identity_chosen" : "agent_identity_kept", id);
    }
}
