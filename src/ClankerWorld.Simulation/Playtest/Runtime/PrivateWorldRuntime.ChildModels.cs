namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    public const string ChildModelRole = "personal";
    public const string ChildModelChoiceParentsAgreed = "parents_agreed";
    public const string ChildModelChoiceInitiatingParent = "initiating_parent";
    public const string ChildModelChoiceNoParentModel = "no_parent_model";

    public const string DeterministicModelEndpointIdentity = "built-in-rules-v1";
    public const string OpenAiModelEndpointIdentity = "openai-chat-completions-v1";
    public const string OllamaCloudModelEndpointIdentity = "ollama-cloud-chat-completions-v1";
    public const string AnthropicModelEndpointIdentity = "anthropic-messages-v1";

    /// <summary>Records the selected personal model after the birth tick commits.</summary>
    public void ApplyChildModelSelection(string childId, ChildPersonalModelSelection selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childId);
        ArgumentNullException.ThrowIfNull(selection);
        gate.Wait();
        try
        {
            if (!inhabitants.TryGetValue(childId, out var child) ||
                !society.Checkpoint.Births.Any(item => item.ChildId == childId))
            {
                throw new InvalidOperationException("A personal model can only be bound to a child born in this world.");
            }
            if (child.ChildModelSelection is { } existing)
            {
                if (existing != selection)
                    throw new InvalidOperationException("A child's birth model choice is already recorded.");
                return;
            }

            ValidateChildModelSelection(selection);
            inhabitants[childId] = child with { ChildModelSelection = selection };
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("child_model_selected", $"{childId}:{selection.ChoiceReason}");
        }
        finally
        {
            gate.Release();
        }
    }

    private static void ValidateChildModelSelection(ChildPersonalModelSelection selection)
    {
        if (selection.Role != ChildModelRole || selection.ChoiceReason is not
            (ChildModelChoiceParentsAgreed or ChildModelChoiceInitiatingParent or ChildModelChoiceNoParentModel))
        {
            throw new InvalidDataException("The saved child model choice has an unsupported role or reason.");
        }

        if (selection.Provider is null)
        {
            if (selection.EndpointIdentity is not null || selection.ModelId is not null || selection.CredentialSlotId is not null ||
                selection.ChoiceReason == ChildModelChoiceParentsAgreed)
            {
                throw new InvalidDataException("The saved child model choice is incomplete.");
            }
            return;
        }

        if (selection.ChoiceReason == ChildModelChoiceNoParentModel)
            throw new InvalidDataException("A child with no parental model choice cannot have a provider assignment.");

        var endpoint = selection.Provider switch
        {
            "deterministic" => DeterministicModelEndpointIdentity,
            "openai" => OpenAiModelEndpointIdentity,
            "ollama-cloud" => OllamaCloudModelEndpointIdentity,
            "anthropic" => AnthropicModelEndpointIdentity,
            _ => throw new InvalidDataException("The saved child model provider is unsupported."),
        };
        if (selection.EndpointIdentity != endpoint)
            throw new InvalidDataException("The saved child model endpoint does not match its provider.");

        if (selection.Provider == "deterministic")
        {
            if (selection.ModelId is not null || selection.CredentialSlotId is not null)
                throw new InvalidDataException("Built-in child decisions cannot reference a model or key slot.");
            return;
        }

        if (string.IsNullOrWhiteSpace(selection.ModelId) || selection.ModelId.Length > 200 ||
            selection.ModelId != selection.ModelId.Trim() || selection.ModelId.Any(char.IsControl) ||
            selection.CredentialSlotId is not { } slotId || !Guid.TryParseExact(slotId, "N", out _))
        {
            throw new InvalidDataException("The saved child model or required key-slot reference is invalid.");
        }
    }
}
