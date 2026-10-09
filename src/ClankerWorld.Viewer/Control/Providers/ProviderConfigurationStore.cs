using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Viewer.Control;

public static class PlayerDecisionProviders
{
    public const string RoutineRole = "routine";
    public const string PlanningRole = "planning";
    public const string PersonalRole = "personal";
    public const string Inherit = "inherit";
    public const string Deterministic = "deterministic";
    public const string Jev = "jev";
    public const string Decisions = "decisions";
    public const string OpenAi = "openai";
    public const string OllamaCloud = "ollama-cloud";
    public const string Anthropic = "anthropic";

    public const string DefaultJevModel = "jev-1.13.0";
    public const string DefaultOpenAiModel = "gpt-6-luna";
    public const string DefaultOllamaCloudModel = "glm-5.3-flash:cloud";
    public const string DefaultAnthropicModel = "claude-haiku-5-5";

    public static readonly Uri JevEndpoint = new("https://api.typesafe.ai/v1/systemone", UriKind.Absolute);
    public static readonly Uri OpenAiEndpoint = new("https://api.openai.com/v1/chat/completions", UriKind.Absolute);
    public static readonly Uri OllamaCloudEndpoint = new("https://ollama.com/v1/chat/completions", UriKind.Absolute);

    /// <summary>Anthropic's API root; its official client adds the Messages and Models paths.</summary>
    public static readonly Uri AnthropicEndpoint = new("https://api.anthropic.com", UriKind.Absolute);

    /// <summary>Providers that host an agent's own paid model, with a key and a thinking setting.</summary>
    public static bool IsHosted(string? provider) => provider is OpenAi or OllamaCloud or Anthropic;

    public static string Normalize(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        Deterministic => Deterministic,
        Jev => Jev,
        Decisions => Decisions,
        OpenAi or "openai-compatible" => OpenAi,
        "ollama" or OllamaCloud => OllamaCloud,
        "claude" or Anthropic => Anthropic,
        _ => throw new ArgumentException(
            "Provider must be deterministic, jev, decisions, openai, ollama-cloud, or anthropic.",
            nameof(provider)),
    };

    public static string DefaultModel(string provider) => Normalize(provider) switch
    {
        Deterministic => string.Empty,
        Jev => DefaultJevModel,
        Decisions => DefaultOpenAiModel,
        OpenAi => DefaultOpenAiModel,
        OllamaCloud => DefaultOllamaCloudModel,
        Anthropic => DefaultAnthropicModel,
        _ => throw new InvalidOperationException("Unsupported decision provider."),
    };

    public static string NormalizeRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        RoutineRole => RoutineRole,
        PlanningRole => PlanningRole,
        _ => throw new ArgumentException("Provider role must be routine or planning.", nameof(role)),
    };

    public static void ValidateRoleProvider(string role, string provider)
    {
        var normalizedRole = NormalizeRole(role);
        var normalizedProvider = Normalize(provider);
        var valid = normalizedRole switch
        {
            RoutineRole => normalizedProvider is Deterministic or Jev || IsHosted(normalizedProvider),
            PlanningRole => normalizedProvider is Deterministic || IsHosted(normalizedProvider),
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                normalizedRole == RoutineRole
                    ? "Routine cognition must use deterministic, Jev, OpenAI, Ollama Cloud, or Anthropic."
                    : "Planning cognition must use deterministic, OpenAI, Ollama Cloud, or Anthropic.",
                nameof(provider));
        }
    }
}

public sealed record ProviderConfigurationSeed(
    string ActiveProvider,
    string? JevModel,
    string? JevApiKey,
    string? OpenAiModel,
    string? OpenAiApiKey,
    string? OllamaCloudModel,
    string? OllamaCloudApiKey,
    string? AnthropicModel = null,
    string? AnthropicApiKey = null);

public sealed record StoredProviderCredential(string Model, string? ApiKey);

public sealed record ProviderCredentialSlot(string Id, string Provider, string Label, string ApiKey);

public sealed record ProviderConfigurationState(
    int SchemaVersion,
    long Revision,
    string RoutineProvider,
    string PlanningProvider,
    StoredProviderCredential Jev,
    StoredProviderCredential OpenAi,
    StoredProviderCredential OllamaCloud,
    IReadOnlyList<InhabitantProviderAssignment>? Assignments = null,
    IReadOnlyList<ProviderCredentialSlot>? CredentialSlots = null,
    IReadOnlyList<string>? DeletedCredentialSlotIds = null,
    StoredProviderCredential? Anthropic = null);

public sealed record RuntimeProviderConfiguration(
    string RoutineProvider,
    string PlanningProvider,
    StoredProviderCredential Jev,
    StoredProviderCredential OpenAi,
    StoredProviderCredential OllamaCloud,
    long Revision,
    IReadOnlyList<InhabitantProviderAssignment>? Assignments = null,
    IReadOnlyList<ProviderCredentialSlot>? CredentialSlots = null,
    StoredProviderCredential? Anthropic = null);

internal sealed record FrozenChildModelBinding(
    ChildPersonalModelSelection Selection,
    string? CopiedDefaultApiKey);

/// <summary>
/// Keeps player-supplied hosted-provider credentials outside world saves and
/// owner-device authority state. The file is installation-local, atomically
/// replaced, and restricted to the service account on Unix hosts.
/// </summary>
public sealed class ProviderConfigurationStore
{
    public const int StateSchemaVersion = 3;
    private const int MaximumApiKeyLength = 4096;
    private const int MaximumModelLength = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly object gate = new();
    private ProviderConfigurationState state;

    public ProviderConfigurationStore(string path, ProviderConfigurationSeed seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(seed);
        Path = System.IO.Path.GetFullPath(path);
        state = LoadOrCreate(seed);
    }

    public string Path { get; }

    // Shared by selection and founder/add-agent setup across the complete
    // world/checkpoint/routing transaction, not just individual store writes.
    public object WorldMutationGate { get; } = new();

    public void ConfigureWithCommit(OwnerProviderConfigurationAction action, Action commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        lock (gate)
        {
            var before = state;
            try
            {
                Configure(action);
                commit();
            }
            catch
            {
                if (state != before)
                {
                    SaveUnsafe(before);
                    state = before;
                }
                throw;
            }
        }
    }

    public RuntimeProviderConfiguration CaptureRuntimeConfiguration()
    {
        lock (gate)
        {
            return new RuntimeProviderConfiguration(
                state.RoutineProvider,
                state.PlanningProvider,
                state.Jev,
                state.OpenAi,
                state.OllamaCloud,
                state.Revision,
                state.Assignments,
                state.CredentialSlots,
                AnthropicCredential(state.Anthropic));
        }
    }

    public OwnerProviderConfigurationStatus CaptureStatus()
    {
        lock (gate)
        {
            return ToStatus(state);
        }
    }

    /// <summary>Save a named key without creating an agent or changing any model assignment.</summary>
    public OwnerProviderConfigurationStatus CreateCredentialSlot(OwnerCredentialSlotCreationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!Guid.TryParseExact(action.CredentialSlotId, "N", out _))
            throw new ArgumentException("Choose a valid named credential slot.", nameof(action));
        var provider = PlayerDecisionProviders.Normalize(action.Provider);
        if (!PlayerDecisionProviders.IsHosted(provider))
            throw new ArgumentException("Choose OpenAI, Ollama Cloud or Anthropic for this key.", nameof(action));
        var label = NormalizeSlotLabel(action.Label);
        var key = NormalizeRequiredApiKey(action.ApiKey);
        lock (gate)
        {
            var slots = state.CredentialSlots ?? [];
            if (slots.Any(slot => slot.Id == action.CredentialSlotId) ||
                (state.DeletedCredentialSlotIds ?? []).Contains(action.CredentialSlotId))
                throw new ArgumentException("That credential slot ID has already been used.", nameof(action));
            var next = state with
            {
                CredentialSlots = [.. slots, new ProviderCredentialSlot(action.CredentialSlotId, provider, label, key)],
                Revision = checked(state.Revision + 1),
            };
            SaveUnsafe(next);
            state = next;
            return ToStatus(next);
        }
    }

    /// <summary>Delete an unused named key from installation-local provider storage.</summary>
    public OwnerProviderConfigurationStatus DeleteCredentialSlot(string slotId)
    {
        if (!Guid.TryParseExact(slotId, "N", out _))
            throw new ArgumentException("Choose a valid named credential slot.", nameof(slotId));
        lock (gate)
        {
            var slots = state.CredentialSlots ?? [];
            if (!slots.Any(item => item.Id == slotId))
                throw new ArgumentException("That named credential slot no longer exists.", nameof(slotId));
            if ((state.Assignments ?? []).Any(item => item.CredentialSlotId == slotId && item.SelectionReason is null))
                throw new InvalidOperationException("This key is assigned to an agent. Choose another key or world default for that agent before deleting it.");

            var next = state with
            {
                CredentialSlots = slots.Where(item => item.Id != slotId).ToArray(),
                DeletedCredentialSlotIds = [.. state.DeletedCredentialSlotIds ?? [], slotId],
                Revision = checked(state.Revision + 1),
            };
            SaveUnsafe(next);
            state = next;
            return ToStatus(next);
        }
    }

    /// <summary>Restore per-agent routing from a world checkpoint, leaving installation keys and defaults alone.</summary>
    public void RestoreWorldAssignments(IReadOnlyList<InhabitantProviderAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        lock (gate)
        {
            var next = PreparedWorldAssignments(assignments);
            ValidateState(next);
            SaveUnsafe(next);
            state = next;
        }
    }

    /// <summary>Preflight a saved world's required local credentials without changing the active routing.</summary>
    public bool CanRestoreWorldAssignments(IReadOnlyList<InhabitantProviderAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        lock (gate)
        {
            try { ValidateState(PreparedWorldAssignments(assignments)); return true; }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or OverflowException)
            { return false; }
        }
    }

    /// <summary>Stores a child's inherited model and commits its world descriptor as one owner operation.</summary>
    public ChildPersonalModelSelection ConfigureChildModelSelectionWithCommit(
        string childId,
        ChildPersonalModelSelection selection,
        Action<ChildPersonalModelSelection> commit)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(commit);
        var frozen = FreezeChildModelSelection(selection, CaptureRuntimeConfiguration());
        return ConfigureChildModelSelectionWithCommit(childId, frozen, commit);
    }

    internal static FrozenChildModelBinding FreezeChildModelSelection(
        ChildPersonalModelSelection selection,
        RuntimeProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!PlayerDecisionProviders.IsHosted(selection.Provider) || selection.CredentialSlotId is not null)
        {
            return new FrozenChildModelBinding(selection, null);
        }

        var provider = PlayerDecisionProviders.Normalize(selection.Provider);
        var credential = provider switch
        {
            PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.Decisions => configuration.OpenAi,
            PlayerDecisionProviders.OllamaCloud => configuration.OllamaCloud,
            PlayerDecisionProviders.Anthropic => AnthropicCredential(configuration.Anthropic),
            _ => throw new InvalidOperationException("A child model provider is unsupported."),
        };
        var slotId = Guid.NewGuid().ToString("N");
        return new FrozenChildModelBinding(
            selection with { Provider = provider, CredentialSlotId = slotId },
            string.IsNullOrWhiteSpace(credential.ApiKey) ? null : credential.ApiKey);
    }

    internal ChildPersonalModelSelection ConfigureChildModelSelectionWithCommit(
        string childId,
        FrozenChildModelBinding frozen,
        Action<ChildPersonalModelSelection> commit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childId);
        ArgumentNullException.ThrowIfNull(frozen);
        ArgumentNullException.ThrowIfNull(commit);
        if (childId != childId.Trim())
            throw new ArgumentException("A child model choice requires a valid inhabitant ID.", nameof(childId));
        var boundSelection = frozen.Selection;
        if (boundSelection.ChoiceReason is not (PrivateWorldRuntime.ChildModelChoiceParentsAgreed or
            PrivateWorldRuntime.ChildModelChoiceInitiatingParent or PrivateWorldRuntime.ChildModelChoiceNoParentModel))
            throw new ArgumentException("The child model choice has an unsupported reason.", nameof(frozen));

        lock (gate)
        {
            var before = state;
            var credentialSlots = (state.CredentialSlots ?? []).ToList();
            var existingRows = (state.Assignments ?? []).Where(item =>
                item.InhabitantId == childId && item.SelectionReason is not null).ToArray();
            if (existingRows.Length > 0 && existingRows.Any(item =>
                    item.Provider != boundSelection.Provider || item.Model != boundSelection.ModelId ||
                    item.CredentialSlotId != boundSelection.CredentialSlotId ||
                    item.SelectionReason != boundSelection.ChoiceReason))
            {
                throw new InvalidDataException("The saved child model assignment does not match its birth choice.");
            }
            if (boundSelection.Provider is { } selectedProvider && PlayerDecisionProviders.IsHosted(selectedProvider))
            {
                selectedProvider = PlayerDecisionProviders.Normalize(selectedProvider);
                if (boundSelection.CredentialSlotId is not { } slotId || !Guid.TryParseExact(slotId, "N", out _))
                    throw new InvalidDataException("A hosted child model must keep its birth-bound credential slot.");
                if (frozen.CopiedDefaultApiKey is { } copiedApiKey)
                {
                    var existingSlot = credentialSlots.SingleOrDefault(item => item.Id == slotId);
                    if (existingSlot is not null)
                    {
                        if (existingSlot.Provider != selectedProvider || existingSlot.ApiKey != copiedApiKey)
                            throw new InvalidDataException("A child's birth-bound credential slot conflicts with local provider storage.");
                    }
                    else
                    {
                        if ((state.DeletedCredentialSlotIds ?? []).Contains(slotId))
                            throw new InvalidDataException("A child's birth-bound credential slot was already deleted.");
                        var suffix = 1;
                        string label;
                        do
                        {
                            label = $"Child model key {suffix++}";
                        } while (credentialSlots.Any(item => item.Provider == selectedProvider && item.Label == label));
                        credentialSlots.Add(new ProviderCredentialSlot(slotId, selectedProvider, label,
                            NormalizeRequiredApiKey(copiedApiKey)));
                    }
                }
            }
            var assignments = (state.Assignments ?? [])
                .Where(item => item.InhabitantId != childId ||
                    item.Role is not (PlayerDecisionProviders.RoutineRole or PlayerDecisionProviders.PlanningRole) ||
                    item.SelectionReason is null)
                .ToList();
            if (boundSelection.Provider is { } provider)
            {
                provider = PlayerDecisionProviders.Normalize(provider);
                if (provider == PlayerDecisionProviders.Jev ||
                    provider != PlayerDecisionProviders.Deterministic && !PlayerDecisionProviders.IsHosted(provider))
                    throw new ArgumentException("A child needs a personal model provider.", nameof(frozen));
                if (boundSelection.ChoiceReason == PrivateWorldRuntime.ChildModelChoiceNoParentModel)
                    throw new ArgumentException("An unconfigured child cannot have a provider assignment.", nameof(frozen));
                if (provider == PlayerDecisionProviders.Deterministic &&
                    (boundSelection.ModelId is not null || boundSelection.CredentialSlotId is not null))
                    throw new ArgumentException("Built-in child decisions cannot reference a model or key slot.", nameof(frozen));
                if (PlayerDecisionProviders.IsHosted(provider) &&
                    (string.IsNullOrWhiteSpace(boundSelection.ModelId) ||
                     boundSelection.CredentialSlotId is { } slotId && !Guid.TryParseExact(slotId, "N", out _)))
                    throw new ArgumentException("The inherited child model choice is incomplete.", nameof(frozen));
                foreach (var role in new[] { PlayerDecisionProviders.RoutineRole, PlayerDecisionProviders.PlanningRole })
                {
                    if (assignments.Any(item => item.InhabitantId == childId && item.Role == role))
                        continue;
                    assignments.Add(new InhabitantProviderAssignment(childId, role, provider,
                        boundSelection.ModelId, boundSelection.CredentialSlotId, boundSelection.ChoiceReason));
                }
            }
            else if (boundSelection.ChoiceReason == PrivateWorldRuntime.ChildModelChoiceParentsAgreed)
            {
                throw new ArgumentException("Agreed parents must have selected a model.", nameof(frozen));
            }

            var next = state with
            {
                CredentialSlots = credentialSlots,
                Assignments = assignments.OrderBy(item => item.InhabitantId, StringComparer.Ordinal)
                    .ThenBy(item => item.Role, StringComparer.Ordinal).ToArray(),
                Revision = checked(state.Revision + 1),
            };
            SaveUnsafe(next);
            state = next;
            try
            {
                commit(boundSelection);
            }
            catch
            {
                SaveUnsafe(before);
                state = before;
                throw;
            }
            return boundSelection;
        }
    }

    private ProviderConfigurationState PreparedWorldAssignments(IReadOnlyList<InhabitantProviderAssignment> assignments)
    {
        // Historical assignments to explicitly deleted keys fall back to
        // deterministic cognition, never another hosted account's credential.
        var deleted = state.DeletedCredentialSlotIds ?? [];
        var ordered = assignments.Select(item => item.CredentialSlotId is { } slot && deleted.Contains(slot) && item.SelectionReason is null
                ? item with { Provider = PlayerDecisionProviders.Deterministic, Model = null, CredentialSlotId = null, Thinking = null }
                : item)
            .OrderBy(item => item.InhabitantId, StringComparer.Ordinal)
            .ThenBy(item => item.Role, StringComparer.Ordinal).ToArray();
        return state with { Assignments = ordered, Revision = checked(state.Revision + 1) };
    }

    public OwnerProviderConfigurationStatus Configure(OwnerProviderConfigurationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (gate)
        {
            var role = action.InhabitantId is not null && action.Role == PlayerDecisionProviders.PersonalRole
                ? PlayerDecisionProviders.PersonalRole
                : PlayerDecisionProviders.NormalizeRole(action.Role);
            if (action.InhabitantId is not null)
            {
                return ConfigureInhabitant(action, role);
            }
            if (action.CredentialSlotId is not null || action.NewCredentialLabel is not null)
                throw new ArgumentException("Credential slots belong to individual inhabitants.", nameof(action));
            if (action.Thinking is not null)
                throw new ArgumentException("Thinking is set for each agent with its model.", nameof(action));
            var provider = PlayerDecisionProviders.Normalize(action.Provider);
            PlayerDecisionProviders.ValidateRoleProvider(role, provider);
            var current = state;
            var next = action.ForgetCredential
                ? ForgetCredential(current, role, provider, action)
                : SelectProvider(current, role, provider, action);

            if (next == current)
            {
                return ToStatus(current);
            }

            next = next with { Revision = checked(current.Revision + 1) };
            SaveUnsafe(next);
            state = next;
            return ToStatus(next);
        }
    }

    private OwnerProviderConfigurationStatus ConfigureInhabitant(OwnerProviderConfigurationAction action, string role)
    {
        var id = action.InhabitantId!;
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
        {
            throw new ArgumentException("An assignment requires a valid inhabitant ID.", nameof(action));
        }
        if (action.ForgetCredential)
        {
            throw new ArgumentException("Remove shared keys from world settings, not an individual assignment.", nameof(action));
        }

        var roles = role == PlayerDecisionProviders.PersonalRole
            ? new[] { PlayerDecisionProviders.RoutineRole, PlayerDecisionProviders.PlanningRole }
            : [role];
        var assignments = (state.Assignments ?? [])
            .Where(item => item.InhabitantId != id || !roles.Contains(item.Role, StringComparer.Ordinal)).ToList();
        var next = state;
        if (action.Provider == PlayerDecisionProviders.Inherit)
        {
            if (action.Model is not null || action.ApiKey is not null || action.Thinking is not null ||
                action.CredentialSlotId is not null || action.NewCredentialLabel is not null)
            {
                throw new ArgumentException("Inheritance does not accept a model or key.", nameof(action));
            }
            foreach (var assignedRole in roles)
                assignments.Add(new InhabitantProviderAssignment(id, assignedRole, PlayerDecisionProviders.Inherit));
        }
        else
        {
            var provider = PlayerDecisionProviders.Normalize(action.Provider);
            if (provider is PlayerDecisionProviders.Jev or PlayerDecisionProviders.Decisions)
                throw new ArgumentException("Choose a personal model for this agent; routine helpers belong to the world.", nameof(action));
            PlayerDecisionProviders.ValidateRoleProvider(
                role == PlayerDecisionProviders.PersonalRole ? PlayerDecisionProviders.PlanningRole : role, provider);
            var thinking = ModelThinking.Normalize(action.Thinking);
            if (thinking is not null && !PlayerDecisionProviders.IsHosted(provider))
                throw new ArgumentException("Only a hosted model has a thinking setting.", nameof(action));
            string? slotId = null;
            string? model;
            if (action.CredentialSlotId is not null)
            {
                if (!PlayerDecisionProviders.IsHosted(provider) ||
                    !Guid.TryParseExact(action.CredentialSlotId, "N", out _))
                    throw new ArgumentException("A hosted agent credential needs a valid slot ID.", nameof(action));
                slotId = action.CredentialSlotId;
                var slots = state.CredentialSlots ?? [];
                if (action.NewCredentialLabel is not null)
                {
                    if (slots.Any(item => item.Id == slotId))
                        throw new ArgumentException("That credential slot already exists.", nameof(action));
                    if ((state.DeletedCredentialSlotIds ?? []).Contains(slotId))
                        throw new ArgumentException("That credential slot ID was deleted and cannot be reused.", nameof(action));
                    var label = NormalizeSlotLabel(action.NewCredentialLabel);
                    var key = NormalizeRequiredApiKey(action.ApiKey);
                    next = next with { CredentialSlots = [.. slots, new ProviderCredentialSlot(slotId, provider, label, key)] };
                }
                else if (action.ApiKey is not null || !slots.Any(item => item.Id == slotId && item.Provider == provider))
                    throw new ArgumentException("Select an existing credential slot or create a new one.", nameof(action));
                model = NormalizeModel(action.Model, CredentialFor(state, provider)!.Model);
            }
            else
            {
                if (action.NewCredentialLabel is not null)
                    throw new ArgumentException("A new credential requires a slot ID.", nameof(action));
                next = SelectProvider(state,
                    role == PlayerDecisionProviders.PersonalRole ? PlayerDecisionProviders.PlanningRole : role,
                    provider, action);
                model = provider == PlayerDecisionProviders.Deterministic ? null : CredentialFor(next, provider)!.Model;
                // Personal model selection must not change the world model.
                if (provider != PlayerDecisionProviders.Deterministic)
                    next = WithCredential(next, provider, CredentialFor(next, provider)! with { Model = CredentialFor(state, provider)!.Model });
            }
            foreach (var assignedRole in roles)
                assignments.Add(new InhabitantProviderAssignment(id, assignedRole, provider, model, slotId, Thinking: thinking));
        }

        next = next with
        {
            RoutineProvider = state.RoutineProvider,
            PlanningProvider = state.PlanningProvider,
            Assignments = assignments.OrderBy(item => item.InhabitantId, StringComparer.Ordinal)
                .ThenBy(item => item.Role, StringComparer.Ordinal).ToArray(),
            Revision = checked(state.Revision + 1),
        };
        SaveUnsafe(next);
        state = next;
        return ToStatus(next);
    }

    private ProviderConfigurationState LoadOrCreate(ProviderConfigurationSeed seed)
    {
        lock (gate)
        {
            if (!File.Exists(Path))
            {
                var created = CreateInitial(seed);
                SaveUnsafe(created);
                return created;
            }

            var stored = File.ReadAllText(Path);
            var json = ProviderCredentialFile.Decode(stored);
            var loaded = JsonSerializer.Deserialize<ProviderConfigurationState>(json, JsonOptions) ??
                throw new InvalidDataException("The provider-configuration state file is empty.");
            // Files written before Anthropic was added have no record for it.
            loaded = loaded with { Anthropic = AnthropicCredential(loaded.Anthropic) };
            if (loaded.SchemaVersion == 2)
            {
                loaded = loaded with { SchemaVersion = StateSchemaVersion, CredentialSlots = [] };
                ValidateState(loaded);
                SaveUnsafe(loaded);
            }
            ValidateState(loaded);
            if (OperatingSystem.IsWindows() && !ProviderCredentialFile.IsProtected(stored))
                SaveUnsafe(loaded);
            RestrictPermissions(Path);
            return loaded;
        }
    }

    private static ProviderConfigurationState CreateInitial(ProviderConfigurationSeed seed)
    {
        var jev = new StoredProviderCredential(
            NormalizeModel(seed.JevModel, PlayerDecisionProviders.DefaultJevModel),
            NormalizeOptionalApiKey(seed.JevApiKey));
        var openAi = new StoredProviderCredential(
            NormalizeModel(seed.OpenAiModel, PlayerDecisionProviders.DefaultOpenAiModel),
            NormalizeOptionalApiKey(seed.OpenAiApiKey));
        var ollamaCloud = new StoredProviderCredential(
            NormalizeModel(seed.OllamaCloudModel, PlayerDecisionProviders.DefaultOllamaCloudModel),
            NormalizeOptionalApiKey(seed.OllamaCloudApiKey));
        var anthropic = new StoredProviderCredential(
            NormalizeModel(seed.AnthropicModel, PlayerDecisionProviders.DefaultAnthropicModel),
            NormalizeOptionalApiKey(seed.AnthropicApiKey));
        var requestedProvider = PlayerDecisionProviders.Normalize(seed.ActiveProvider);
        var hasRequestedCredential = requestedProvider == PlayerDecisionProviders.Deterministic ||
            !string.IsNullOrWhiteSpace(CredentialFor(requestedProvider, jev, openAi, ollamaCloud, anthropic).ApiKey);
        var routineProvider = requestedProvider == PlayerDecisionProviders.Jev && hasRequestedCredential
            ? PlayerDecisionProviders.Jev
            : PlayerDecisionProviders.Deterministic;
        var planningProvider = PlayerDecisionProviders.IsHosted(requestedProvider) && hasRequestedCredential
                ? requestedProvider
                : PlayerDecisionProviders.Deterministic;

        return new ProviderConfigurationState(
            StateSchemaVersion,
            0,
            routineProvider,
            planningProvider,
            jev,
            openAi,
            ollamaCloud,
            Anthropic: anthropic);
    }

    private static ProviderConfigurationState SelectProvider(
        ProviderConfigurationState current,
        string role,
        string provider,
        OwnerProviderConfigurationAction action)
    {
        if (provider == PlayerDecisionProviders.Deterministic)
        {
            if (!string.IsNullOrWhiteSpace(action.ApiKey) || !string.IsNullOrWhiteSpace(action.Model))
            {
                throw new ArgumentException("Deterministic cognition does not accept a model or API key.", nameof(action));
            }

            return ActiveProviderFor(current, role) == provider
                ? current
                : WithActiveProvider(current, role, provider);
        }

        var oldCredential = CredentialFor(current, provider)!;
        var model = NormalizeModel(action.Model, oldCredential.Model);
        var apiKey = string.IsNullOrWhiteSpace(action.ApiKey)
            ? oldCredential.ApiKey
            : NormalizeRequiredApiKey(action.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException(
                $"{provider} requires an API key. Paste one before enabling the provider.",
                nameof(action));
        }

        return WithActiveProvider(
            WithCredential(current, provider, new StoredProviderCredential(model, apiKey)),
            role,
            provider);
    }

    private static ProviderConfigurationState ForgetCredential(
        ProviderConfigurationState current,
        string role,
        string provider,
        OwnerProviderConfigurationAction action)
    {
        if (provider == PlayerDecisionProviders.Deterministic)
        {
            throw new ArgumentException("Deterministic cognition has no credential to forget.", nameof(action));
        }

        if (!string.IsNullOrWhiteSpace(action.ApiKey))
        {
            throw new ArgumentException("A forget request cannot also contain an API key.", nameof(action));
        }

        var oldCredential = CredentialFor(current, provider)!;
        var model = NormalizeModel(action.Model, oldCredential.Model);
        var next = WithCredential(current, provider, new StoredProviderCredential(model, null));
        next = next with
        {
            Assignments = (next.Assignments ?? [])
                .Where(item => item.Provider != provider || item.CredentialSlotId is not null || item.SelectionReason is not null).ToArray(),
        };
        if (next.RoutineProvider == provider)
            next = next with { RoutineProvider = PlayerDecisionProviders.Deterministic };
        if (next.PlanningProvider == provider)
            next = next with { PlanningProvider = PlayerDecisionProviders.Deterministic };
        return next;
    }

    private static string ActiveProviderFor(ProviderConfigurationState state, string role) => role switch
    {
        PlayerDecisionProviders.RoutineRole => state.RoutineProvider,
        PlayerDecisionProviders.PlanningRole => state.PlanningProvider,
        _ => throw new ArgumentException("Unsupported cognition provider role.", nameof(role)),
    };

    private static ProviderConfigurationState WithActiveProvider(
        ProviderConfigurationState state,
        string role,
        string provider) => role switch
        {
            PlayerDecisionProviders.RoutineRole => state with { RoutineProvider = provider },
            PlayerDecisionProviders.PlanningRole => state with { PlanningProvider = provider },
            _ => throw new ArgumentException("Unsupported cognition provider role.", nameof(role)),
        };

    private static ProviderConfigurationState WithCredential(
        ProviderConfigurationState state,
        string provider,
        StoredProviderCredential credential) => provider switch
        {
            PlayerDecisionProviders.Jev => state with { Jev = credential },
            PlayerDecisionProviders.OpenAi => state with { OpenAi = credential },
            PlayerDecisionProviders.OllamaCloud => state with { OllamaCloud = credential },
            PlayerDecisionProviders.Anthropic => state with { Anthropic = credential },
            _ => throw new ArgumentException("The selected provider does not store a credential.", nameof(provider)),
        };

    private static StoredProviderCredential? CredentialFor(ProviderConfigurationState state, string provider) =>
        CredentialFor(provider, state.Jev, state.OpenAi, state.OllamaCloud, AnthropicCredential(state.Anthropic));

    /// <summary>Anthropic's default record, for files and seeds from before it was added.</summary>
    internal static StoredProviderCredential AnthropicCredential(StoredProviderCredential? stored) =>
        stored ?? new StoredProviderCredential(PlayerDecisionProviders.DefaultAnthropicModel, null);

    private static StoredProviderCredential CredentialFor(
        string provider,
        StoredProviderCredential jev,
        StoredProviderCredential openAi,
        StoredProviderCredential ollamaCloud,
        StoredProviderCredential anthropic) => provider switch
        {
            PlayerDecisionProviders.Jev => jev,
            PlayerDecisionProviders.OpenAi => openAi,
            PlayerDecisionProviders.OllamaCloud => ollamaCloud,
            PlayerDecisionProviders.Anthropic => anthropic,
            PlayerDecisionProviders.Deterministic => new StoredProviderCredential(string.Empty, null),
            _ => throw new ArgumentException("Unsupported decision provider.", nameof(provider)),
        };

    private static OwnerProviderConfigurationStatus ToStatus(ProviderConfigurationState state) => new(
        state.RoutineProvider,
        state.PlanningProvider,
        state.Revision,
        new ReadOnlyCollection<OwnerProviderOptionStatus>(
        [
            new(PlayerDecisionProviders.Deterministic, string.Empty, false),
            new(PlayerDecisionProviders.Jev, state.Jev.Model, !string.IsNullOrWhiteSpace(state.Jev.ApiKey)),
            new(PlayerDecisionProviders.OpenAi, state.OpenAi.Model, !string.IsNullOrWhiteSpace(state.OpenAi.ApiKey)),
            new(PlayerDecisionProviders.OllamaCloud, state.OllamaCloud.Model, !string.IsNullOrWhiteSpace(state.OllamaCloud.ApiKey)),
            new(PlayerDecisionProviders.Anthropic, AnthropicCredential(state.Anthropic).Model,
                !string.IsNullOrWhiteSpace(state.Anthropic?.ApiKey)),
        ]), state.Assignments ?? [], (state.CredentialSlots ?? [])
            .Select(item => new OwnerProviderCredentialStatus(item.Id, item.Provider, item.Label)).ToArray());

    private static void ValidateState(ProviderConfigurationState state)
    {
        if (state.SchemaVersion != StateSchemaVersion || state.Revision < 0)
        {
            throw new InvalidDataException("The provider-configuration state has an unsupported schema or revision.");
        }

        var routine = PlayerDecisionProviders.Normalize(state.RoutineProvider);
        var planning = PlayerDecisionProviders.Normalize(state.PlanningProvider);
        PlayerDecisionProviders.ValidateRoleProvider(PlayerDecisionProviders.RoutineRole, routine);
        PlayerDecisionProviders.ValidateRoleProvider(PlayerDecisionProviders.PlanningRole, planning);
        ValidateCredential(state.Jev, PlayerDecisionProviders.DefaultJevModel);
        ValidateCredential(state.OpenAi, PlayerDecisionProviders.DefaultOpenAiModel);
        ValidateCredential(state.OllamaCloud, PlayerDecisionProviders.DefaultOllamaCloudModel);
        ValidateCredential(AnthropicCredential(state.Anthropic), PlayerDecisionProviders.DefaultAnthropicModel);
        var slotIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in state.CredentialSlots ?? [])
        {
            if (!Guid.TryParseExact(slot.Id, "N", out _) || !slotIds.Add(slot.Id) ||
                !PlayerDecisionProviders.IsHosted(slot.Provider))
                throw new InvalidDataException("A credential slot has an invalid or duplicate identity.");
            _ = NormalizeSlotLabel(slot.Label);
            _ = NormalizeRequiredApiKey(slot.ApiKey);
        }
        var deletedSlotIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in state.DeletedCredentialSlotIds ?? [])
        {
            if (!Guid.TryParseExact(id, "N", out _) || !deletedSlotIds.Add(id) || slotIds.Contains(id))
                throw new InvalidDataException("A deleted credential slot has an invalid or reused identity.");
        }
        var assignmentKeys = new HashSet<(string, string)>();
        foreach (var assignment in state.Assignments ?? [])
        {
            if (string.IsNullOrWhiteSpace(assignment.InhabitantId) ||
                !assignmentKeys.Add((assignment.InhabitantId, assignment.Role)))
            {
                throw new InvalidDataException("Provider assignments must have unique inhabitant/role identities.");
            }
            if (assignment.Provider == PlayerDecisionProviders.Inherit)
            {
                if (assignment.Role is not (PlayerDecisionProviders.RoutineRole or PlayerDecisionProviders.PlanningRole) ||
                    assignment.Model is not null || assignment.CredentialSlotId is not null || assignment.SelectionReason is not null ||
                    assignment.Thinking is not null)
                    throw new InvalidDataException("An inherited assignment cannot carry a model or credential.");
                continue;
            }
            PlayerDecisionProviders.ValidateRoleProvider(assignment.Role, assignment.Provider);
            if (assignment.Thinking is not null &&
                (!PlayerDecisionProviders.IsHosted(assignment.Provider) || ModelThinking.Normalize(assignment.Thinking) != assignment.Thinking))
                throw new InvalidDataException("An agent assignment has an invalid thinking setting.");
            if (assignment.SelectionReason is not null &&
                (assignment.SelectionReason is not (PrivateWorldRuntime.ChildModelChoiceParentsAgreed or
                    PrivateWorldRuntime.ChildModelChoiceInitiatingParent) ||
                 assignment.Role is not (PlayerDecisionProviders.RoutineRole or PlayerDecisionProviders.PlanningRole)))
                throw new InvalidDataException("A child model assignment has an invalid selection reason.");
            var retainsRequestedModelWithoutKey = assignment.SelectionReason is not null;
            if (assignment.CredentialSlotId is not null &&
                !(state.CredentialSlots ?? []).Any(item => item.Id == assignment.CredentialSlotId && item.Provider == assignment.Provider) &&
                !(retainsRequestedModelWithoutKey && Guid.TryParseExact(assignment.CredentialSlotId, "N", out _)))
                throw new InvalidDataException("An agent assignment references a missing provider credential slot.");
            if (assignment.Provider != PlayerDecisionProviders.Deterministic &&
                assignment.CredentialSlotId is null &&
                string.IsNullOrWhiteSpace(CredentialFor(state, assignment.Provider)?.ApiKey) && !retainsRequestedModelWithoutKey)
            {
                throw new InvalidDataException("An assigned provider has no stored credential.");
            }
            if (assignment.Model is not null)
            {
                _ = NormalizeModel(assignment.Model, string.Empty);
            }
        }
        var assignments = state.Assignments ?? [];
        foreach (var group in assignments.Where(item => item.SelectionReason is not null)
                     .GroupBy(item => item.InhabitantId, StringComparer.Ordinal))
        {
            var rows = group.ToArray();
            var pairedBirthChoice = rows.Length == 2 &&
                rows.Any(item => item.Role == PlayerDecisionProviders.RoutineRole) &&
                rows.Any(item => item.Role == PlayerDecisionProviders.PlanningRole) &&
                rows.Select(item => (item.Provider, item.Model, item.CredentialSlotId, item.SelectionReason)).Distinct().Count() == 1;
            var preservedBirthChoiceWithOwnerOverride = rows.Length == 1 &&
                rows[0].Role is (PlayerDecisionProviders.RoutineRole or PlayerDecisionProviders.PlanningRole) &&
                assignments.Any(item => item.InhabitantId == group.Key &&
                    item.Role == (rows[0].Role == PlayerDecisionProviders.RoutineRole
                        ? PlayerDecisionProviders.PlanningRole : PlayerDecisionProviders.RoutineRole) &&
                    item.SelectionReason is null);
            if (!pairedBirthChoice && !preservedBirthChoiceWithOwnerOverride)
                throw new InvalidDataException("A child birth model must retain both routine and planning assignments.");
        }
        if ((routine != PlayerDecisionProviders.Deterministic &&
                string.IsNullOrWhiteSpace(CredentialFor(state, routine)?.ApiKey)) ||
            (planning != PlayerDecisionProviders.Deterministic &&
                string.IsNullOrWhiteSpace(CredentialFor(state, planning)?.ApiKey)))
        {
            throw new InvalidDataException("An active hosted cognition provider has no stored credential.");
        }
    }

    private static void ValidateCredential(StoredProviderCredential? credential, string fallbackModel)
    {
        if (credential is null)
        {
            throw new InvalidDataException("The provider-configuration state is missing a provider record.");
        }

        _ = NormalizeModel(credential.Model, fallbackModel);
        _ = NormalizeOptionalApiKey(credential.ApiKey);
    }

    private void SaveUnsafe(ProviderConfigurationState next)
    {
        ValidateState(next);
        var directory = System.IO.Path.GetDirectoryName(Path) ??
            throw new InvalidOperationException("The provider-configuration path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var encoded = ProviderCredentialFile.Encode(JsonSerializer.Serialize(next, JsonOptions));
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporaryPath, options))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(encoded);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            RestrictPermissions(temporaryPath);
            File.Move(temporaryPath, Path, overwrite: true);
            RestrictPermissions(Path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string NormalizeModel(string? model, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(model) ? fallback.Trim() : model.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaximumModelLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"A provider model must contain 1 to {MaximumModelLength} printable characters.", nameof(model));
        }

        return normalized;
    }

    private static string NormalizeSlotLabel(string? label)
    {
        var normalized = label?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 64 || normalized.Any(char.IsControl))
            throw new ArgumentException("A credential label must contain 1 to 64 printable characters.", nameof(label));
        return normalized;
    }

    private static string? NormalizeOptionalApiKey(string? apiKey) => string.IsNullOrWhiteSpace(apiKey)
        ? null
        : NormalizeRequiredApiKey(apiKey);

    private static string NormalizeRequiredApiKey(string? apiKey)
    {
        var normalized = apiKey?.Trim() ?? string.Empty;
        if (normalized.Length is < 8 or > MaximumApiKeyLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"An API key must contain 8 to {MaximumApiKeyLength} printable characters.",
                nameof(apiKey));
        }

        return normalized;
    }

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

/// <summary>
/// World-save-owned Jev availability and routing revision mirrored into the
/// host's decision adapter without putting provider credentials in the save.
/// </summary>
public sealed class WorldJevPolicy
{
    private readonly object gate = new();
    private RoutineHelperSettings helper = RoutineHelperSettings.Jev;
    private long revision;

    public (bool Enabled, long Revision, RoutineHelperSettings Helper) Capture()
    {
        lock (gate) return (helper.Provider != "off", revision, helper);
    }

    public void Initialize(bool savedEnabled, long savedRevision, RoutineHelperSettings? savedHelper = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(savedRevision);
        var next = savedHelper ?? (savedEnabled ? RoutineHelperSettings.Jev : RoutineHelperSettings.Off);
        next.Validate();
        if (savedEnabled != (next.Provider != "off")) throw new ArgumentException("Inconsistent helper availability.");
        lock (gate) { helper = next; revision = savedRevision; }
    }

    public void Set(bool nextEnabled, long nextRevision, RoutineHelperSettings? nextHelper = null)
    {
        var next = nextHelper ?? (nextEnabled ? RoutineHelperSettings.Jev : RoutineHelperSettings.Off);
        next.Validate();
        lock (gate)
        {
            if (nextEnabled != (next.Provider != "off") || nextRevision < revision || nextRevision > revision + 1 ||
                (helper != next && nextRevision == revision) ||
                (helper == next && nextRevision != revision && (revision != 0 || nextRevision != 1)))
                throw new InvalidOperationException("The helper routing revision is inconsistent with the saved world.");
            helper = next;
            revision = nextRevision;
        }
    }
}

/// <summary>
/// A stable provider object shared by every inhabitant. Player changes update
/// its kind and epoch dynamically; requests already issued against an older
/// configuration are rejected and fall back locally at the cognition boundary.
/// </summary>
public sealed partial class ConfigurableDecisionProvider(
    ProviderConfigurationStore configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<ConfigurableDecisionProvider>? logger = null,
    WorldJevPolicy? jevPolicy = null,
    ProviderUsageStore? usageStore = null) : IDecisionProvider, IAgentConversationProvider
{
    private readonly WorldJevPolicy jevPolicy = jevPolicy ?? new WorldJevPolicy();
    private static readonly HashSet<string> RoutineCandidateIds = new(StringComparer.Ordinal)
    {
        "consume_food",
        "collect_shared_food",
        "harvest_food",
        "seek_food",
        "wear_clothing",
        "tend_fire",
        "seek_warmth",
        "safe_idle",
    };

    public DecisionProviderKind Kind
    {
        get
        {
            var selected = configuration.CaptureRuntimeConfiguration();
            var primary = selected.PlanningProvider != PlayerDecisionProviders.Deterministic
                ? selected.PlanningProvider
                : selected.RoutineProvider;
            if (primary == PlayerDecisionProviders.Jev)
            {
                var helper = jevPolicy.Capture().Helper;
                primary = helper.Provider == "off" ? PlayerDecisionProviders.Deterministic : helper.Provider;
            }
            return MapKind(primary);
        }
    }

    public long ProviderEpoch => checked(configuration.CaptureRuntimeConfiguration().Revision + jevPolicy.Capture().Revision);

    long IAgentConversationProvider.ProviderEpoch => configuration.CaptureRuntimeConfiguration().Revision;

    public bool CanSpeakAs(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId) || agentId != agentId.Trim() || agentId.Any(char.IsControl)) return false;
        try
        {
            var route = ConversationRouteFor(configuration.CaptureRuntimeConfiguration(), agentId);
            return route.Provider == PlayerDecisionProviders.Deterministic ||
                !string.IsNullOrWhiteSpace(route.Credential.ApiKey);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    public async ValueTask<AgentConversationTurnResponse> SpeakAsync(
        AgentConversationTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        var selected = configuration.CaptureRuntimeConfiguration();
        if (request.ExpectedProviderEpoch != selected.Revision)
            throw new ProviderConversationUnavailableException(
                "The personal conversation provider changed after this request was issued.");
        var route = ConversationRouteFor(selected, request.SpeakerId);
        if (route.Provider == PlayerDecisionProviders.Deterministic)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Purpose == AgentConversationPurpose.SurnameChoice)
                throw new ProviderConversationUnavailableException("The surname conversation needs each partner's selected personal model.");
            return new AgentConversationTurnResponse(
                request.RequestId,
                request.ConversationId,
                request.Revision,
                request.RunEpoch,
                request.SpeakerId,
                request.Purpose == AgentConversationPurpose.WrapUp
                    ? "I think we have said what matters. We can leave it here without changing anything."
                    : $"It is good to talk with you, {request.OtherParticipantName}.",
                AgentConversationDisposition.Continue);
        }

        if (string.IsNullOrWhiteSpace(route.Credential.ApiKey))
            throw new ProviderConversationUnavailableException("The assigned personal model key is unavailable.");
        if (usageStore is null)
            throw new ProviderConversationUnavailableException("Paid conversation usage accounting is unavailable.");
        if (!request.AllowedEffects.Contains(AgentConversationEffect.None))
            throw new InvalidDataException("The conversation host supplied an invalid effect whitelist.");

        var purposeWire = PurposeWireValue(request.Purpose);

        const string instructions = "Speak as one agent in a bounded shared conversation. Use only your own identity plus the public history included below. Never claim the other person agreed. Do not invent events, private thoughts, promises, ownership, resources or world changes. Return JSON only with utterance (one line, at most 500 characters), disposition (continue or withdraw), and effect (one of allowed_effects). Mutual trust and marriage are proposals only: both people must separately accept the same wrap-up. For surname_choice, marriage consent already exists: include surname_choice, exactly one of allowed_surnames, and effect none. Each partner has at most two alternating valid turns; continued disagreement after four turns uses a disclosed seeded draw. A withdrawal suspends that surname session without counting a turn. Do not include reasoning.";
        var input = JsonSerializer.Serialize(new
        {
            purpose = purposeWire,
            speaker = new
            {
                id = request.SpeakerId,
                name = request.SpeakerName,
                personality = request.SpeakerPersonality,
                aspiration = request.SpeakerAspiration,
            },
            other_participant = new { id = request.OtherParticipantId, name = request.OtherParticipantName },
            public_history = request.PublicHistory.Select(turn => new
            {
                speaker_id = turn.SpeakerId,
                utterance = turn.Text,
                is_wrap_up = turn.IsWrapUp,
                surname_choice = turn.SurnameChoice,
            }).ToArray(),
            allowed_effects = request.AllowedEffects.Select(EffectWireValue).ToArray(),
            allowed_surnames = request.AllowedSurnames,
        }, ConversationJsonOptions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConversationTimeout);

        var role = "conversation";
        var usageTicket = usageStore?.Begin(route.Provider, route.Credential.Model, role);
        var timer = Stopwatch.StartNew();
        var inputTokens = 0;
        var outputTokens = 0;
        try
        {
            AgentConversationTurnResponse turn;
            if (route.Provider == PlayerDecisionProviders.Anthropic)
            {
                var reply = await new AnthropicModelClient(httpClientFactory.CreateClient("model"), route.Credential.ApiKey!)
                    .CompleteAsync(new HostedModelCall(route.Credential.Model, instructions, input, route.Thinking, ConversationTimeout),
                        timeout.Token).ConfigureAwait(false);
                inputTokens = reply.InputTokens;
                outputTokens = reply.OutputTokens;
                turn = ParseConversationFields(request, reply.Text, route.Credential.Model, inputTokens, outputTokens);
            }
            else
            {
                // OpenAI and Ollama Cloud read reasoning_effort; the model default sends none.
                var payload = new JsonObject
                {
                    ["model"] = route.Credential.Model,
                    ["response_format"] = new JsonObject { ["type"] = "json_object" },
                    ["messages"] = new JsonArray(
                        new JsonObject { ["role"] = "system", ["content"] = instructions },
                        new JsonObject { ["role"] = "user", ["content"] = input }),
                };
                if (route.Thinking is not null) payload["reasoning_effort"] = route.Thinking;
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, route.Endpoint)
                {
                    Content = new StringContent(payload.ToJsonString(ConversationJsonOptions), Encoding.UTF8, "application/json"),
                };
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", route.Credential.ApiKey);
                using var response = await httpClientFactory.CreateClient("model").SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("The assigned conversation model did not complete the request.");

                var body = await ProviderResponseBody.ReadAsync(response.Content, timeout.Token).ConfigureAwait(false);
                turn = ParseConversationResponse(request, body, route.Credential.Model, out inputTokens, out outputTokens);
            }
            timer.Stop();
            if (usageTicket is not null)
                usageStore!.Finish(usageTicket, "completed", turn.InputTokens, turn.OutputTokens);
            if (logger is not null && logger.IsEnabled(LogLevel.Information))
                LogConversationCall(logger, "completed", purposeWire,
                    request.PublicHistory.Count + 1,
                    timer.ElapsedMilliseconds, turn.InputTokens, turn.OutputTokens);
            return turn;
        }
        catch (OperationCanceledException)
        {
            timer.Stop();
            if (usageTicket is not null) usageStore!.Finish(usageTicket, "abandoned");
            if (logger is not null && logger.IsEnabled(LogLevel.Information))
                LogConversationCall(logger, "cancelled", purposeWire,
                    request.PublicHistory.Count + 1, timer.ElapsedMilliseconds, 0, 0);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            timer.Stop();
            if (usageTicket is not null) usageStore!.Finish(usageTicket, "failed", inputTokens, outputTokens);
            if (logger is not null && logger.IsEnabled(LogLevel.Information))
                LogConversationCall(logger, "failed", purposeWire,
                    request.PublicHistory.Count + 1, timer.ElapsedMilliseconds, inputTokens, outputTokens);
            throw;
        }
    }

    private static readonly JsonSerializerOptions ConversationJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12,
    };

    private static readonly TimeSpan ConversationTimeout = TimeSpan.FromSeconds(45);

    private sealed record ConversationRoute(string Provider, StoredProviderCredential Credential, Uri Endpoint, string? Thinking = null);

    private static ConversationRoute ConversationRouteFor(RuntimeProviderConfiguration selected, string speakerId)
    {
        var assignment = AssignmentFor(selected, speakerId, PlayerDecisionProviders.PlanningRole)
            ?? throw new ProviderConversationUnavailableException("The agent has no explicit personal planning assignment.");
        if (assignment.Provider == PlayerDecisionProviders.Deterministic)
            return new ConversationRoute(PlayerDecisionProviders.Deterministic,
                new StoredProviderCredential(string.Empty, null), new Uri("http://127.0.0.1/"));
        if (!PlayerDecisionProviders.IsHosted(assignment.Provider))
            throw new ProviderConversationUnavailableException("The assigned planning provider cannot speak in conversations.");

        var credential = CredentialFor(selected, assignment.Provider);
        if (assignment.CredentialSlotId is { } slotId)
        {
            var slot = selected.CredentialSlots?.FirstOrDefault(item => item.Id == slotId && item.Provider == assignment.Provider)
                ?? throw new ProviderConversationUnavailableException("The assigned personal model credential is unavailable.");
            credential = credential with { ApiKey = slot.ApiKey };
        }
        if (assignment.Model is { } model) credential = credential with { Model = model };
        if (string.IsNullOrWhiteSpace(credential.Model) || string.IsNullOrWhiteSpace(credential.ApiKey))
            throw new ProviderConversationUnavailableException("The assigned personal model is unavailable.");
        var endpoint = assignment.Provider switch
        {
            PlayerDecisionProviders.OpenAi => PlayerDecisionProviders.OpenAiEndpoint,
            PlayerDecisionProviders.OllamaCloud => PlayerDecisionProviders.OllamaCloudEndpoint,
            _ => PlayerDecisionProviders.AnthropicEndpoint,
        };
        return new ConversationRoute(assignment.Provider, credential, endpoint, assignment.Thinking);
    }

    private static string EffectWireValue(AgentConversationEffect effect) => effect switch
    {
        AgentConversationEffect.None => "none",
        AgentConversationEffect.MutualTrust => "mutual_trust",
        AgentConversationEffect.Marriage => "marriage",
        _ => throw new InvalidDataException("The conversation effect is not allowed."),
    };

    private static string PurposeWireValue(AgentConversationPurpose purpose) => purpose switch
    {
        AgentConversationPurpose.PublicTurn => "public_turn",
        AgentConversationPurpose.WrapUp => "wrap_up",
        AgentConversationPurpose.SurnameChoice => "surname_choice",
        _ => throw new InvalidDataException("The conversation purpose is not allowed."),
    };

    private static AgentConversationTurnResponse ParseConversationResponse(
        AgentConversationTurnRequest request,
        string body,
        string model,
        out int inputTokens,
        out int outputTokens)
    {
        inputTokens = 0;
        outputTokens = 0;
        try
        {
            using var envelope = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 12 });
            var root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The conversation provider response envelope is invalid.");
            // A charged response can report valid usage even when its dialogue
            // is rejected. Keep those bounded counts for failure accounting.
            inputTokens = ReadUsage(root, "prompt_tokens");
            outputTokens = ReadUsage(root, "completion_tokens");
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() != 1 || choices[0].ValueKind != JsonValueKind.Object ||
                !choices[0].TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("The conversation provider response envelope is invalid.");
            return ParseConversationFields(request, content.GetString()!, model, inputTokens, outputTokens);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The conversation provider returned invalid JSON.");
        }
    }

    /// <summary>Validates one conversation turn's JSON fields, whichever wire format carried them.</summary>
    private static AgentConversationTurnResponse ParseConversationFields(
        AgentConversationTurnRequest request,
        string text,
        string model,
        int inputTokens,
        int outputTokens)
    {
        try
        {
            using var reply = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            var fields = reply.RootElement;
            if (fields.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The conversation provider response fields are invalid.");
            var allowedFields = new HashSet<string>(StringComparer.Ordinal)
            {
                "utterance", "disposition", "effect",
            };
            if (request.Purpose == AgentConversationPurpose.SurnameChoice) allowedFields.Add("surname_choice");
            var seenFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields.EnumerateObject())
            {
                if (!allowedFields.Contains(field.Name) || !seenFields.Add(field.Name))
                    throw new InvalidDataException("The conversation provider response contains unknown or duplicate fields.");
            }
            if (seenFields.Count != allowedFields.Count ||
                !fields.TryGetProperty("utterance", out var utterance) || utterance.ValueKind != JsonValueKind.String ||
                !fields.TryGetProperty("disposition", out var disposition) || disposition.ValueKind != JsonValueKind.String ||
                !fields.TryGetProperty("effect", out var effect) || effect.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("The conversation provider response fields are invalid.");
            var outcome = disposition.GetString() switch
            {
                "continue" => AgentConversationDisposition.Continue,
                "withdraw" => AgentConversationDisposition.Withdraw,
                _ => throw new InvalidDataException("The conversation provider disposition is unknown."),
            };
            var proposedEffect = effect.GetString() switch
            {
                "none" => AgentConversationEffect.None,
                "mutual_trust" => AgentConversationEffect.MutualTrust,
                "marriage" => AgentConversationEffect.Marriage,
                _ => throw new InvalidDataException("The conversation provider effect is unknown."),
            };
            if (!request.AllowedEffects.Contains(proposedEffect))
                throw new InvalidDataException("The conversation provider proposed an effect outside the host whitelist.");
            if (outcome == AgentConversationDisposition.Withdraw && proposedEffect != AgentConversationEffect.None)
                throw new InvalidDataException("A conversation withdrawal cannot carry an effect.");

            string? surnameChoice = null;
            if (request.Purpose == AgentConversationPurpose.SurnameChoice)
            {
                if (!fields.TryGetProperty("surname_choice", out var choice) || choice.ValueKind != JsonValueKind.String ||
                    !request.AllowedSurnames.Contains(choice.GetString()!, StringComparer.Ordinal))
                    throw new InvalidDataException("The surname choice is outside the partners' original surnames.");
                surnameChoice = choice.GetString();
            }

            var response = new AgentConversationTurnResponse(
                request.RequestId,
                request.ConversationId,
                request.Revision,
                request.RunEpoch,
                request.SpeakerId,
                utterance.GetString()!,
                outcome,
                proposedEffect,
                inputTokens,
                outputTokens,
                model,
                surnameChoice);
            if (!AgentConversationText.IsValidUtterance(response.Text))
                throw new InvalidDataException("The conversation provider utterance is outside the host length or text bounds.");
            return response;
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The conversation provider returned invalid JSON.");
        }
    }

    private static int ReadUsage(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object ||
            !usage.TryGetProperty(propertyName, out var value)) return 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var count) || count is < 0 or > 1_000_000)
            throw new InvalidDataException("The conversation provider usage is outside its bound.");
        return count;
    }

    [LoggerMessage(
        EventId = 2104,
        Level = LogLevel.Information,
        Message = "conversation_call status={Status} purpose={Purpose} turn_count={TurnCount} latency_ms={LatencyMilliseconds} input_tokens={InputTokens} output_tokens={OutputTokens}")]
    private static partial void LogConversationCall(
        ILogger logger,
        string status,
        string purpose,
        int turnCount,
        long latencyMilliseconds,
        int inputTokens,
        int outputTokens);

    public DecisionProviderKind KindFor(InhabitantObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var selected = configuration.CaptureRuntimeConfiguration();
        return MapKind(ProviderFor(selected, observation, jevPolicy.Capture()).Provider);
    }

    public async ValueTask<CognitionDecisionResponse> DecideAsync(
        CognitionDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var selected = configuration.CaptureRuntimeConfiguration();
        var worldJev = jevPolicy.Capture();
        var providerEpoch = checked(selected.Revision + worldJev.Revision);
        if (request.ProviderEpoch != providerEpoch)
        {
            throw new InvalidOperationException("The cognition provider changed after this request was issued.");
        }

        var isRoutine = IsRoutine(request.Observation);
        var role = isRoutine ? PlayerDecisionProviders.RoutineRole : PlayerDecisionProviders.PlanningRole;
        var routing = ProviderFor(selected, request.Observation, worldJev);
        var providerId = routing.Provider;
        var credential = CredentialFor(selected, providerId);
        if (providerId == PlayerDecisionProviders.Decisions ||
            providerId == PlayerDecisionProviders.Jev && worldJev.Revision > 0)
            credential = credential with { Model = worldJev.Helper.Model };
        if (providerId == PlayerDecisionProviders.Decisions && worldJev.Helper.CredentialSlotId is { } helperSlotId)
        {
            var helperSlot = selected.CredentialSlots?.FirstOrDefault(item => item.Id == helperSlotId && item.Provider == PlayerDecisionProviders.OpenAi);
            if (helperSlot is null) throw new CognitionProviderUnavailableException("missing_key", "Choose a saved OpenAI key for this world's helper.");
            credential = credential with { ApiKey = helperSlot.ApiKey };
        }
        if (routing.Assignment?.CredentialSlotId is { } slotId)
        {
            var slot = selected.CredentialSlots?.FirstOrDefault(item => item.Id == slotId && item.Provider == providerId)
                ?? throw new InvalidOperationException("The assigned credential slot is unavailable.");
            credential = credential with { ApiKey = slot.ApiKey };
        }
        if (routing.Assignment?.Model is { } model)
        {
            credential = credential with { Model = model };
        }
        if (providerId != PlayerDecisionProviders.Deterministic && string.IsNullOrWhiteSpace(credential.ApiKey))
            throw new CognitionProviderUnavailableException("missing_key", "Add a key in this agent's model settings.");
        var thinking = PlayerDecisionProviders.IsHosted(providerId) ? routing.Assignment?.Thinking : null;
        IDecisionProvider provider = providerId switch
        {
            PlayerDecisionProviders.Deterministic => new DeterministicDecisionProvider(),
            PlayerDecisionProviders.Jev => new JevDecisionProvider(
                httpClientFactory.CreateClient("typesafe"),
                () => credential.ApiKey,
                PlayerDecisionProviders.JevEndpoint,
                credential.Model,
                providerEpoch: providerEpoch),
            PlayerDecisionProviders.Decisions => new OpenAiDecisionsProvider(
                httpClientFactory.CreateClient("model"), () => credential.ApiKey,
                model: credential.Model, providerEpoch: providerEpoch),
            PlayerDecisionProviders.OpenAi => new OpenAiCompatibleDecisionProvider(
                httpClientFactory.CreateClient("model"),
                () => credential.ApiKey,
                PlayerDecisionProviders.OpenAiEndpoint,
                credential.Model,
                providerEpoch: providerEpoch,
                thinking: thinking),
            PlayerDecisionProviders.OllamaCloud => new OpenAiCompatibleDecisionProvider(
                httpClientFactory.CreateClient("model"),
                () => credential.ApiKey,
                PlayerDecisionProviders.OllamaCloudEndpoint,
                credential.Model,
                providerEpoch: providerEpoch,
                thinking: thinking),
            PlayerDecisionProviders.Anthropic => new OpenAiCompatibleDecisionProvider(
                new AnthropicModelClient(httpClientFactory.CreateClient("model"), credential.ApiKey!),
                credential.Model,
                thinking,
                providerEpoch: providerEpoch),
            _ => throw new InvalidOperationException("Unsupported cognition provider configuration."),
        };

        // Reserve before any potentially billable HTTP request. A retry is a
        // new DecideAsync invocation and consumes a separate allowance.
        var usageTicket = providerId == PlayerDecisionProviders.Deterministic
            ? null : usageStore?.Begin(providerId, credential.Model, role);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await provider.DecideAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            if (usageTicket is not null)
                usageStore!.Finish(usageTicket, "completed", response.Usage?.InputTokens ?? 0,
                    response.Usage?.OutputTokens ?? 0);
            if (logger is not null)
            {
                LogProviderCallCompleted(
                    logger,
                    providerId,
                    role,
                    string.IsNullOrWhiteSpace(credential.Model) ? "local" : credential.Model,
                    request.Observation.InhabitantId,
                    request.Observation.WorldTick,
                    response.SelectedCandidateId,
                    response.Confidence,
                    response.Usage?.InputTokens ?? 0,
                    response.Usage?.OutputTokens ?? 0,
                    stopwatch.ElapsedMilliseconds);
            }
            return response with
            {
                Provider = MapKind(providerId),
                ProviderEpoch = providerEpoch,
                Usage = new CognitionUsage(
                    response.Usage?.ModelId ?? (string.IsNullOrWhiteSpace(credential.Model) ? null : credential.Model),
                    response.Usage?.InputTokens ?? 0, response.Usage?.OutputTokens ?? 0,
                    stopwatch.ElapsedMilliseconds, providerId, role),
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            if (usageTicket is not null) usageStore!.Finish(usageTicket, "abandoned");
            if (logger is not null)
            {
                LogProviderCallCancelled(
                    logger,
                    providerId,
                    role,
                    string.IsNullOrWhiteSpace(credential.Model) ? "local" : credential.Model,
                    request.Observation.InhabitantId,
                    request.Observation.WorldTick,
                    stopwatch.ElapsedMilliseconds);
            }
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            stopwatch.Stop();
            if (usageTicket is not null) usageStore!.Finish(usageTicket, "failed");
            if (logger is not null)
            {
                LogProviderCallFailed(
                    logger,
                    providerId,
                    role,
                    string.IsNullOrWhiteSpace(credential.Model) ? "local" : credential.Model,
                    request.Observation.InhabitantId,
                    request.Observation.WorldTick,
                    exception.GetType().Name,
                    stopwatch.ElapsedMilliseconds);
            }
            throw;
        }
    }

    private static (string Provider, InhabitantProviderAssignment? Assignment) ProviderFor(
        RuntimeProviderConfiguration configuration,
        InhabitantObservation observation,
        (bool Enabled, long Revision, RoutineHelperSettings Helper) policy)
    {
        var helper = policy.Helper;
        var routine = IsRoutine(observation);
        var helperEligible = observation.Self?.LifeStage is "Adult" or "Elder" ||
            (observation.Self is null && !observation.RequiresPersonalProvider);
        // Birth provenance protects personal requests throughout life. It does
        // not exclude a grown resident from the shared routine helper.
        if (routine && helperEligible && helper.Provider != "off" &&
            (helper.Provider == "decisions" || policy.Revision > 0 || observation.RequiresPersonalProvider))
            return (helper.Provider, null);
        var role = routine ? PlayerDecisionProviders.RoutineRole : PlayerDecisionProviders.PlanningRole;
        var assigned = AssignmentFor(configuration, observation.InhabitantId, role);
        if (assigned?.Provider == PlayerDecisionProviders.Inherit)
        {
            if (observation.RequiresPersonalProvider)
                return (PlayerDecisionProviders.Deterministic, assigned);
            assigned = null;
        }
        // World-born residents never inherit a potentially billable world
        // default for personal requests. Their explicit assignment remains
        // the route to their own model, including after they grow up.
        if (observation.RequiresPersonalProvider && assigned is null)
            return (PlayerDecisionProviders.Deterministic, null);
        if (observation.RequiresPersonalProvider && assigned?.SelectionReason is not null &&
            !HasUsableCredential(configuration, assigned))
            return (PlayerDecisionProviders.Deterministic, null);
        var provider = assigned?.Provider ?? (routine ? configuration.RoutineProvider : configuration.PlanningProvider);
        if (observation.RequiresPersonalProvider && provider == PlayerDecisionProviders.Jev)
            return (PlayerDecisionProviders.Deterministic, null);
        if (routine && helperEligible && provider == PlayerDecisionProviders.Jev && helper.Provider != "off")
            return (helper.Provider, null);
        if (routine && provider == PlayerDecisionProviders.Jev && (helper.Provider == "off" || !helperEligible))
        {
            // Jev is a world-level helper, never a requirement for an agent to
            // continue. Prefer this agent's personal planner, then the world
            // planner, and finally local safe decisions when no model is set.
            assigned = AssignmentFor(configuration, observation.InhabitantId, PlayerDecisionProviders.PlanningRole);
            provider = assigned?.Provider == PlayerDecisionProviders.Inherit
                ? configuration.PlanningProvider
                : assigned?.Provider ?? configuration.PlanningProvider;
        }
        return (provider, assigned);
    }

    private static InhabitantProviderAssignment? AssignmentFor(RuntimeProviderConfiguration configuration, string inhabitantId, string role) =>
        configuration.Assignments?.FirstOrDefault(item => item.InhabitantId == inhabitantId && item.Role == role);

    private static bool HasUsableCredential(
        RuntimeProviderConfiguration configuration,
        InhabitantProviderAssignment assignment)
    {
        if (assignment.Provider == PlayerDecisionProviders.Deterministic) return true;
        if (assignment.CredentialSlotId is { } slotId)
            return configuration.CredentialSlots?.Any(item => item.Id == slotId && item.Provider == assignment.Provider) == true;
        return !string.IsNullOrWhiteSpace(CredentialFor(configuration, assignment.Provider).ApiKey);
    }

    private static bool IsRoutine(InhabitantObservation observation) =>
        observation.ObserverGuidance is not { Count: > 0 } &&
        !observation.NeedsName && !observation.IsNameRetry &&
        !observation.NeedsPersonality && !observation.NeedsAspiration &&
        observation.Candidates.All(candidate => RoutineCandidateIds.Contains(candidate.Id) || candidate.Id.StartsWith("care:", StringComparison.Ordinal));

    private static StoredProviderCredential CredentialFor(
        RuntimeProviderConfiguration configuration,
        string provider) => provider switch
        {
            PlayerDecisionProviders.Jev => configuration.Jev,
            PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.Decisions => configuration.OpenAi,
            PlayerDecisionProviders.OllamaCloud => configuration.OllamaCloud,
            PlayerDecisionProviders.Anthropic => ProviderConfigurationStore.AnthropicCredential(configuration.Anthropic),
            PlayerDecisionProviders.Deterministic => new StoredProviderCredential(string.Empty, null),
            _ => throw new InvalidOperationException("Unsupported cognition provider configuration."),
        };

    private static DecisionProviderKind MapKind(string provider) => provider switch
    {
        PlayerDecisionProviders.Deterministic => DecisionProviderKind.Deterministic,
        PlayerDecisionProviders.Jev => DecisionProviderKind.Jev,
        PlayerDecisionProviders.Decisions => DecisionProviderKind.OpenAiDecisions,
        PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.OllamaCloud or PlayerDecisionProviders.Anthropic =>
            DecisionProviderKind.LargeLanguageModel,
        _ => throw new InvalidOperationException("Unsupported cognition provider configuration."),
    };

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Information,
        Message = "cognition_provider_call status=completed provider={Provider} role={Role} model={Model} inhabitant={InhabitantId} tick={WorldTick} candidate={CandidateId} confidence={Confidence} input_tokens={InputTokens} output_tokens={OutputTokens} latency_ms={LatencyMilliseconds}")]
    private static partial void LogProviderCallCompleted(
        ILogger logger,
        string provider,
        string role,
        string model,
        string inhabitantId,
        long worldTick,
        string candidateId,
        double confidence,
        int inputTokens,
        int outputTokens,
        long latencyMilliseconds);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Information,
        Message = "cognition_provider_call status=cancelled provider={Provider} role={Role} model={Model} inhabitant={InhabitantId} tick={WorldTick} latency_ms={LatencyMilliseconds}")]
    private static partial void LogProviderCallCancelled(
        ILogger logger,
        string provider,
        string role,
        string model,
        string inhabitantId,
        long worldTick,
        long latencyMilliseconds);

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Warning,
        Message = "cognition_provider_call status=failed provider={Provider} role={Role} model={Model} inhabitant={InhabitantId} tick={WorldTick} error_type={ErrorType} latency_ms={LatencyMilliseconds}")]
    private static partial void LogProviderCallFailed(
        ILogger logger,
        string provider,
        string role,
        string model,
        string inhabitantId,
        long worldTick,
        string errorType,
        long latencyMilliseconds);
}
