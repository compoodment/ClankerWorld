using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Viewer.Control;

internal static class ChildModelSelectionResolver
{
    public static ChildPersonalModelSelection Choose(
        string initiatingParentId,
        string partnerId,
        RuntimeProviderConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initiatingParentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerId);
        ArgumentNullException.ThrowIfNull(configuration);

        var initiating = PersonalChoice(configuration, initiatingParentId);
        var partner = PersonalChoice(configuration, partnerId);
        if (initiating is null)
        {
            return new ChildPersonalModelSelection(
                PrivateWorldRuntime.ChildModelRole,
                null,
                null,
                null,
                null,
                partner is null
                    ? PrivateWorldRuntime.ChildModelChoiceNoParentModel
                    : PrivateWorldRuntime.ChildModelChoiceInitiatingParent);
        }

        var reason = partner is not null && SameModel(initiating, partner)
            ? PrivateWorldRuntime.ChildModelChoiceParentsAgreed
            : PrivateWorldRuntime.ChildModelChoiceInitiatingParent;
        return initiating with { ChoiceReason = reason };
    }

    private static ChildPersonalModelSelection? PersonalChoice(
        RuntimeProviderConfiguration configuration,
        string inhabitantId)
    {
        var assignment = configuration.Assignments?.FirstOrDefault(item =>
            item.InhabitantId == inhabitantId && item.Role == PlayerDecisionProviders.PlanningRole);
        if (assignment is null) return null;

        var endpoint = assignment.Provider switch
        {
            PlayerDecisionProviders.Deterministic => PrivateWorldRuntime.DeterministicModelEndpointIdentity,
            PlayerDecisionProviders.OpenAi => PrivateWorldRuntime.OpenAiModelEndpointIdentity,
            PlayerDecisionProviders.OllamaCloud => PrivateWorldRuntime.OllamaCloudModelEndpointIdentity,
            _ => null,
        };
        if (endpoint is null) return null;
        if (assignment.Provider != PlayerDecisionProviders.Deterministic && string.IsNullOrWhiteSpace(assignment.Model))
            return null;

        return new ChildPersonalModelSelection(
            PrivateWorldRuntime.ChildModelRole,
            assignment.Provider,
            endpoint,
            assignment.Provider == PlayerDecisionProviders.Deterministic ? null : assignment.Model,
            assignment.Provider == PlayerDecisionProviders.Deterministic ? null : assignment.CredentialSlotId,
            PrivateWorldRuntime.ChildModelChoiceInitiatingParent);
    }

    private static bool SameModel(ChildPersonalModelSelection left, ChildPersonalModelSelection right) =>
        left.Provider == right.Provider && left.EndpointIdentity == right.EndpointIdentity && left.ModelId == right.ModelId;
}
