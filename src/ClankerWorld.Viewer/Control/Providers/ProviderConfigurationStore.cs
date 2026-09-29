using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Viewer.Control;

public static class PlayerDecisionProviders
{
    public const string RoutineRole = "routine";
    public const string PlanningRole = "planning";
    public const string PersonalRole = "personal";
    public const string Deterministic = "deterministic";
    public const string Jev = "jev";
    public const string OpenAi = "openai";
    public const string OllamaCloud = "ollama-cloud";

    public const string DefaultJevModel = "jev-1.13.0";
    public const string DefaultOpenAiModel = "gpt-5-mini";
    public const string DefaultOllamaCloudModel = "gpt-oss:120b-cloud";

    public static readonly Uri JevEndpoint = new("https://api.typesafe.ai/v1/systemone", UriKind.Absolute);
    public static readonly Uri OpenAiEndpoint = new("https://api.openai.com/v1/chat/completions", UriKind.Absolute);
    public static readonly Uri OllamaCloudEndpoint = new("https://ollama.com/v1/chat/completions", UriKind.Absolute);

    public static string Normalize(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        Deterministic => Deterministic,
        Jev => Jev,
        OpenAi or "openai-compatible" => OpenAi,
        "ollama" or OllamaCloud => OllamaCloud,
        _ => throw new ArgumentException(
            "Provider must be deterministic, jev, openai, or ollama-cloud.",
            nameof(provider)),
    };

    public static string DefaultModel(string provider) => Normalize(provider) switch
    {
        Deterministic => string.Empty,
        Jev => DefaultJevModel,
        OpenAi => DefaultOpenAiModel,
        OllamaCloud => DefaultOllamaCloudModel,
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
            RoutineRole => normalizedProvider is Deterministic or Jev or OpenAi or OllamaCloud,
            PlanningRole => normalizedProvider is Deterministic or OpenAi or OllamaCloud,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                normalizedRole == RoutineRole
                    ? "Routine cognition must use deterministic, Jev, OpenAI, or Ollama Cloud."
                    : "Planning cognition must use deterministic, OpenAI, or Ollama Cloud.",
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
    string? OllamaCloudApiKey);

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
    IReadOnlyList<string>? DeletedCredentialSlotIds = null);

public sealed record RuntimeProviderConfiguration(
    string RoutineProvider,
    string PlanningProvider,
    StoredProviderCredential Jev,
    StoredProviderCredential OpenAi,
    StoredProviderCredential OllamaCloud,
    long Revision,
    IReadOnlyList<InhabitantProviderAssignment>? Assignments = null,
    IReadOnlyList<ProviderCredentialSlot>? CredentialSlots = null);

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
                state.CredentialSlots);
        }
    }

    public OwnerProviderConfigurationStatus CaptureStatus()
    {
        lock (gate)
        {
            return ToStatus(state);
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
            if ((state.Assignments ?? []).Any(item => item.CredentialSlotId == slotId))
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

    private ProviderConfigurationState PreparedWorldAssignments(IReadOnlyList<InhabitantProviderAssignment> assignments)
    {
        // Historical assignments to explicitly deleted keys fall back to
        // deterministic cognition, never another hosted account's credential.
        var deleted = state.DeletedCredentialSlotIds ?? [];
        var ordered = assignments.Select(item => item.CredentialSlotId is { } slot && deleted.Contains(slot)
                ? item with { Provider = PlayerDecisionProviders.Deterministic, Model = null, CredentialSlotId = null }
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
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id != id.Trim())
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
        if (action.Provider == "inherit")
        {
            if (action.Model is not null || action.ApiKey is not null ||
                action.CredentialSlotId is not null || action.NewCredentialLabel is not null)
            {
                throw new ArgumentException("Inheritance does not accept a model or key.", nameof(action));
            }
        }
        else
        {
            var provider = PlayerDecisionProviders.Normalize(action.Provider);
            if (provider == PlayerDecisionProviders.Jev)
                throw new ArgumentException("Jev is world-level assistance, not an individual agent's model.", nameof(action));
            PlayerDecisionProviders.ValidateRoleProvider(
                role == PlayerDecisionProviders.PersonalRole ? PlayerDecisionProviders.PlanningRole : role, provider);
            string? slotId = null;
            string? model;
            if (action.CredentialSlotId is not null)
            {
                if (provider is not (PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.OllamaCloud) ||
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
                assignments.Add(new InhabitantProviderAssignment(id, assignedRole, provider, model, slotId));
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
        var requestedProvider = PlayerDecisionProviders.Normalize(seed.ActiveProvider);
        var hasRequestedCredential = requestedProvider == PlayerDecisionProviders.Deterministic ||
            !string.IsNullOrWhiteSpace(CredentialFor(requestedProvider, jev, openAi, ollamaCloud).ApiKey);
        var routineProvider = requestedProvider == PlayerDecisionProviders.Jev && hasRequestedCredential
            ? PlayerDecisionProviders.Jev
            : PlayerDecisionProviders.Deterministic;
        var planningProvider = requestedProvider is PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.OllamaCloud &&
            hasRequestedCredential
                ? requestedProvider
                : PlayerDecisionProviders.Deterministic;

        return new ProviderConfigurationState(
            StateSchemaVersion,
            0,
            routineProvider,
            planningProvider,
            jev,
            openAi,
            ollamaCloud);
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
                .Where(item => item.Provider != provider || item.CredentialSlotId is not null).ToArray(),
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
            _ => throw new ArgumentException("The selected provider does not store a credential.", nameof(provider)),
        };

    private static StoredProviderCredential? CredentialFor(ProviderConfigurationState state, string provider) =>
        CredentialFor(provider, state.Jev, state.OpenAi, state.OllamaCloud);

    private static StoredProviderCredential CredentialFor(
        string provider,
        StoredProviderCredential jev,
        StoredProviderCredential openAi,
        StoredProviderCredential ollamaCloud) => provider switch
        {
            PlayerDecisionProviders.Jev => jev,
            PlayerDecisionProviders.OpenAi => openAi,
            PlayerDecisionProviders.OllamaCloud => ollamaCloud,
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
        var slotIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in state.CredentialSlots ?? [])
        {
            if (!Guid.TryParseExact(slot.Id, "N", out _) || !slotIds.Add(slot.Id) ||
                slot.Provider is not (PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.OllamaCloud))
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
            PlayerDecisionProviders.ValidateRoleProvider(assignment.Role, assignment.Provider);
            if (assignment.CredentialSlotId is not null &&
                !(state.CredentialSlots ?? []).Any(item => item.Id == assignment.CredentialSlotId && item.Provider == assignment.Provider))
                throw new InvalidDataException("An agent assignment references a missing provider credential slot.");
            if (assignment.Provider != PlayerDecisionProviders.Deterministic &&
                assignment.CredentialSlotId is null &&
                string.IsNullOrWhiteSpace(CredentialFor(state, assignment.Provider)?.ApiKey))
            {
                throw new InvalidDataException("An assigned provider has no stored credential.");
            }
            if (assignment.Model is not null)
            {
                _ = NormalizeModel(assignment.Model, string.Empty);
            }
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
    private bool enabled = true;
    private long revision;

    public (bool Enabled, long Revision) Capture()
    {
        lock (gate) return (enabled, revision);
    }

    // Called once when the saved world is loaded, before the host starts ticking.
    public void Initialize(bool savedEnabled, long savedRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(savedRevision);
        lock (gate)
        {
            enabled = savedEnabled;
            revision = savedRevision;
        }
    }

    public void Set(bool nextEnabled, long nextRevision)
    {
        lock (gate)
        {
            if (nextRevision < revision || nextRevision > revision + 1 ||
                (enabled == nextEnabled) != (nextRevision == revision))
                throw new InvalidOperationException("The Jev routing revision is inconsistent with the saved world.");
            enabled = nextEnabled;
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
    ProviderUsageStore? usageStore = null) : IDecisionProvider
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
            if (primary == PlayerDecisionProviders.Jev && !jevPolicy.Capture().Enabled)
                primary = PlayerDecisionProviders.Deterministic;
            return MapKind(primary);
        }
    }

    public long ProviderEpoch => checked(configuration.CaptureRuntimeConfiguration().Revision + jevPolicy.Capture().Revision);

    public DecisionProviderKind KindFor(InhabitantObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var selected = configuration.CaptureRuntimeConfiguration();
        return MapKind(ProviderFor(selected, observation, jevPolicy.Capture().Enabled).Provider);
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
        var routing = ProviderFor(selected, request.Observation, worldJev.Enabled);
        var providerId = routing.Provider;
        var credential = CredentialFor(selected, providerId);
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
        IDecisionProvider provider = providerId switch
        {
            PlayerDecisionProviders.Deterministic => new DeterministicDecisionProvider(),
            PlayerDecisionProviders.Jev => new JevDecisionProvider(
                httpClientFactory.CreateClient("typesafe"),
                () => credential.ApiKey,
                PlayerDecisionProviders.JevEndpoint,
                credential.Model,
                providerEpoch: providerEpoch),
            PlayerDecisionProviders.OpenAi => new OpenAiCompatibleDecisionProvider(
                httpClientFactory.CreateClient("model"),
                () => credential.ApiKey,
                PlayerDecisionProviders.OpenAiEndpoint,
                credential.Model,
                providerEpoch: providerEpoch),
            PlayerDecisionProviders.OllamaCloud => new OpenAiCompatibleDecisionProvider(
                httpClientFactory.CreateClient("model"),
                () => credential.ApiKey,
                PlayerDecisionProviders.OllamaCloudEndpoint,
                credential.Model,
                providerEpoch: providerEpoch),
            _ => throw new InvalidOperationException("Unsupported cognition provider configuration."),
        };

        // Reserve before any potentially billable HTTP request. A retry is a
        // new DecideAsync invocation and consumes a separate allowance.
        var usageTicket = providerId == PlayerDecisionProviders.Deterministic ||
            string.IsNullOrWhiteSpace(credential.ApiKey)
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
        bool jevEnabled)
    {
        var routine = IsRoutine(observation);
        var role = routine ? PlayerDecisionProviders.RoutineRole : PlayerDecisionProviders.PlanningRole;
        var assigned = AssignmentFor(configuration, observation.InhabitantId, role);
        // Children born in this world never inherit a potentially billable
        // world default. Their own explicit assignment is the only route to a
        // personal model after infancy; until then they use local safe choices.
        if (observation.RequiresPersonalProvider && assigned is null)
            return (PlayerDecisionProviders.Deterministic, null);
        var provider = assigned?.Provider ?? (routine ? configuration.RoutineProvider : configuration.PlanningProvider);
        if (observation.RequiresPersonalProvider && provider == PlayerDecisionProviders.Jev)
            return (PlayerDecisionProviders.Deterministic, null);
        if (routine && provider == PlayerDecisionProviders.Jev && !jevEnabled)
        {
            // Jev is a world-level helper, never a requirement for an agent to
            // continue. Prefer this agent's personal planner, then the world
            // planner, and finally local safe decisions when no model is set.
            assigned = AssignmentFor(configuration, observation.InhabitantId, PlayerDecisionProviders.PlanningRole);
            provider = assigned?.Provider ?? configuration.PlanningProvider;
        }
        return (provider, assigned);
    }

    private static InhabitantProviderAssignment? AssignmentFor(RuntimeProviderConfiguration configuration, string inhabitantId, string role) =>
        configuration.Assignments?.FirstOrDefault(item => item.InhabitantId == inhabitantId && item.Role == role);

    private static bool IsRoutine(InhabitantObservation observation) =>
        observation.Candidates.All(candidate => RoutineCandidateIds.Contains(candidate.Id) || candidate.Id.StartsWith("care:", StringComparison.Ordinal));

    private static StoredProviderCredential CredentialFor(
        RuntimeProviderConfiguration configuration,
        string provider) => provider switch
        {
            PlayerDecisionProviders.Jev => configuration.Jev,
            PlayerDecisionProviders.OpenAi => configuration.OpenAi,
            PlayerDecisionProviders.OllamaCloud => configuration.OllamaCloud,
            PlayerDecisionProviders.Deterministic => new StoredProviderCredential(string.Empty, null),
            _ => throw new InvalidOperationException("Unsupported cognition provider configuration."),
        };

    private static DecisionProviderKind MapKind(string provider) => provider switch
    {
        PlayerDecisionProviders.Deterministic => DecisionProviderKind.Deterministic,
        PlayerDecisionProviders.Jev => DecisionProviderKind.Jev,
        PlayerDecisionProviders.OpenAi or PlayerDecisionProviders.OllamaCloud => DecisionProviderKind.LargeLanguageModel,
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
