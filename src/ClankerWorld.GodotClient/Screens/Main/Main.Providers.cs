using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly Label usageScopeHint = new();
    private int usageReads;
    private bool usageLimitEdited;

    private async Task RefreshProviderConfigurationAsync()
    {
        cognitionApiKeyInput.Text = string.Empty;
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            cognitionConfigurationStatus.Text = "Connect this device before setting up agent models.";
            RenderProviderConfiguration();
            return;
        }

        await RunOwnerActionAsync(async () =>
        {
            providerConfiguration = await AwaitCurrentWorldResultAsync(ownerApi.GetProviderStatusAsync(
                ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None));
            RefreshRoutineHelperKeys(SelectedRoutineHelperKey());
            if (worldSettingsContent.Visible) RequestRoutineHelperModels();
            PopulateCognitionTargets();
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            return "Loaded Agent model settings";
        });
    }

    /// <summary>
    /// Reads the installation's model-call count for Game Settings without a
    /// status message, so opening Settings from the Main Menu stays quiet.
    /// </summary>
    private async Task RefreshUsageAsync()
    {
        var read = ++usageReads;
        var owner = registration;
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            usageMeterStatus.Text = "Connect this device to see model-call usage.";
            return;
        }
        try
        {
            var status = await ownerApi.GetUsageStatusAsync(ResolveWorldUri(), authority, deviceId,
                signer, CancellationToken.None);
            if (read != usageReads || !ReferenceEquals(owner, registration)) return;
            usageStatus = status;
            RenderUsageStatus();
        }
        catch (Exception exception)
        {
            if (read == usageReads && ReferenceEquals(owner, registration))
                usageMeterStatus.Text = "Could not load model calls: " + FriendlyFailure(exception);
        }
    }

    private async Task ObserveUsagePauseAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var read = ++usageReads;
        var owner = registration;
        try
        {
            var status = await AwaitCurrentWorldResultAsync(ownerApi.GetUsageStatusAsync(ResolveWorldUri(), authority, deviceId,
                signer, CancellationToken.None));
            if (read != usageReads || !ReferenceEquals(owner, registration)) return;
            usageStatus = status;
            RenderUsageStatus();
            if (usageStatus.LimitReached)
                SetStatus(UsageLimitPausedMessage, good: false);
        }
        catch (Exception)
        {
            // A pause is authoritative even when the optional meter read is unavailable.
        }
    }

    private const string UsageLimitPausedMessage =
        "Model-call limit reached, so the world is paused. Raise the limit in Settings → Game, then resume.";

    private void RenderUsageStatus()
    {
        if (usageStatus is null)
        {
            usageMeterStatus.Text = "Loading model calls...";
            usageMeterStatus.TooltipText = string.Empty;
            grantUsageCallsButton.Hide();
            RefreshControlAvailability();
            return;
        }
        if (usageStatus.AccountingError is not null)
        {
            usageMeterStatus.Text = usageStatus.AccountingError;
            usageMeterStatus.TooltipText = "Call totals are unavailable until accounting is restored.";
            grantUsageCallsButton.Visible = false;
            RefreshControlAvailability();
            return;
        }
        if (!usageLimitEdited)
            usageAttemptLimitInput.Text = usageStatus.AttemptLimit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
        var rows = usageStatus.Rows.OrderByDescending(row => row.Attempts)
            .Select(row => $"{row.Provider} / {row.Model}: {Count(row.Attempts)} calls");
        usageMeterStatus.Text = usageStatus.AttemptLimit is { } limit
            ? $"{Count(usageStatus.Attempts)} of {Count(limit)} calls used across all worlds."
            : $"{Count(usageStatus.Attempts)} calls used across all worlds. No limit is set.";
        if (usageStatus.LimitReached)
            usageMeterStatus.Text += " Time is paused. Raise the limit or allow more calls, then resume.";
        if (usageStatus.Rows.Count > 0)
            usageMeterStatus.Text += "\n" + string.Join("\n", rows);
        usageMeterStatus.TooltipText = $"Started {Count(usageStatus.Attempts)}, completed {Count(usageStatus.Completed)}, " +
            $"failed {Count(usageStatus.Failed)} and interrupted {Count(usageStatus.Abandoned)}; providers reported " +
            $"{Count(usageStatus.InputTokens)} input and {Count(usageStatus.OutputTokens)} output tokens, shown for information only.";
        grantUsageCallsButton.Visible = usageStatus.LimitReached;
        RefreshControlAvailability();
    }

    private async Task ConfigureUsageAsync(bool grant)
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        long? cap = null;
        if (!grant && !string.IsNullOrWhiteSpace(usageAttemptLimitInput.Text))
        {
            if (!long.TryParse(usageAttemptLimitInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed is < 1 or > 1_000_000)
            {
                SetStatus("Enter a number of calls from 1 to 1,000,000, or leave it blank for no limit.", good: false);
                return;
            }
            cap = parsed;
        }
        var action = grant ? new OwnerUsageLimitAction(null, AdditionalCalls: 100) :
            new OwnerUsageLimitAction(cap);
        await RunOwnerActionAsync(async () =>
        {
            var status = await AwaitCurrentWorldResultAsync(ownerApi.ConfigureUsageLimitAsync(ResolveWorldUri(), authority, deviceId,
                action, signer, CancellationToken.None));
            usageReads++;
            usageStatus = status;
            usageLimitEdited = false;
            RenderUsageStatus();
            if (grant)
                return "Allowed 100 more model calls. Resume the world when ready";
            return cap is null ? "Model-call limit turned off"
                : string.Create(CultureInfo.InvariantCulture, $"Model-call limit set to {cap:N0} calls");
        });
    }

    private async Task SaveProviderConfigurationAsync()
    {
        var generation = observationSession.RequestGeneration;
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            SetStatus("Connect this device before setting up agent models.", good: false);
            return;
        }

        var role = SelectedRoleId();
        var provider = SelectedProviderId();
        var target = SelectedCognitionTarget();
        var hostedAgent = target is not null && IsHostedProvider(provider);
        var credentialChoice = hostedAgent ? SelectedCredentialChoice() : null;
        var creatingSlot = credentialChoice == "new";
        if (creatingSlot && (string.IsNullOrWhiteSpace(cognitionCredentialLabelInput.Text) ||
            string.IsNullOrWhiteSpace(cognitionApiKeyInput.Text)))
        {
            SetStatus("Give the new key a name and paste the key.", good: false);
            return;
        }
        if (HasModelList(provider) && cognitionModelPicker.Model.Length == 0)
        {
            SetStatus("Pick a model first.", good: false);
            return;
        }
        var action = new OwnerProviderConfigurationAction(
            role,
            provider,
            provider is "deterministic" or "inherit" ? null : EmptyToNull(cognitionModelPicker.Model),
            provider is "deterministic" or "inherit" || hostedAgent && !creatingSlot
                ? null : EmptyToNull(cognitionApiKeyInput.Text),
            ForgetCredential: false,
            InhabitantId: target,
            CredentialSlotId: creatingSlot ? Guid.NewGuid().ToString("N") : credentialChoice is null or "default" ? null : credentialChoice,
            NewCredentialLabel: creatingSlot ? EmptyToNull(cognitionCredentialLabelInput.Text) : null,
            Thinking: hostedAgent ? SelectedThinking(cognitionThinkingChoice) : null);
        try
        {
            await RunOwnerActionAsync(async () =>
            {
                providerConfiguration = await AwaitCurrentWorldResultAsync(ownerApi.ConfigureProviderAsync(
                    ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None));
                PopulateCredentialChoices();
                return $"{ProviderDisplayName(provider)} will handle {RoleDisplayName(role).ToLowerInvariant()} at the agent's next model choice";
            });
        }
        finally
        {
            if (IsCurrentWorldRequest(generation))
            {
                cognitionApiKeyInput.Text = string.Empty;
                cognitionCredentialLabelInput.Text = string.Empty;
                RenderProviderConfiguration();
            }
        }
    }

    private async Task ForgetProviderCredentialAsync()
    {
        var generation = observationSession.RequestGeneration;
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            SetStatus("Connect this device before changing keys.", good: false);
            return;
        }

        var role = SelectedRoleId();
        var provider = SelectedProviderId();
        if (provider == "deterministic")
        {
            SetStatus("Built-in rules use no API key", good: false);
            return;
        }

        var action = new OwnerProviderConfigurationAction(
            role,
            provider,
            EmptyToNull(cognitionModelPicker.Model),
            null,
            ForgetCredential: true);
        await RunOwnerActionAsync(async () =>
        {
            providerConfiguration = await AwaitCurrentWorldResultAsync(ownerApi.ConfigureProviderAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None));
            return $"forgot the saved {ProviderDisplayName(provider)} key";
        });
        if (IsCurrentWorldRequest(generation))
        {
            cognitionApiKeyInput.Text = string.Empty;
            RenderProviderConfiguration();
        }
    }

    private async Task DeleteCredentialSlotAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            SetStatus("Connect this device before deleting a saved key.", good: false);
            return;
        }

        var slotId = SelectedCredentialChoice();
        var slot = providerConfiguration?.CredentialSlots?.FirstOrDefault(item => item.Id == slotId);
        if (slot is null)
        {
            SetStatus("Choose a saved key to delete.", good: false);
            return;
        }
        if (providerConfiguration?.Assignments?.Any(item =>
                item.CredentialSlotId == slotId && item.SelectionReason is null) == true)
        {
            SetStatus("An agent is still using this key. Give that agent another key first.", good: false);
            return;
        }

        await RunOwnerActionAsync(async () =>
        {
            providerConfiguration = await AwaitCurrentWorldResultAsync(ownerApi.DeleteCredentialSlotAsync(
                ResolveWorldUri(), authority, deviceId, slotId, signer, CancellationToken.None));
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            return $"deleted saved key {slot.Label}";
        });
    }

    private string SelectedRoleId() => SelectedCognitionTarget() is not null
        ? "personal" : cognitionRoleChoice.Selected == 1 ? "planning" : "routine";

    private string? SelectedCognitionTarget() => cognitionTargetChoice.Selected <= 0
        ? null : cognitionTargetChoice.GetItemMetadata(cognitionTargetChoice.Selected).AsString();

    private bool SelectedTargetWasBornHere() => observationSession.Current?.Baseline.Snapshot.Inhabitants
        .FirstOrDefault(item => item.Id == SelectedCognitionTarget())?.Relationships
        .Any(item => item.Type == "biological_parentage" && item.Direction == "child") == true;

    private void PopulateCognitionTargets()
    {
        var target = SelectedCognitionTarget();
        cognitionTargetChoice.Clear();
        cognitionTargetChoice.AddItem("World defaults");
        foreach (var inhabitant in observationSession.Current?.Baseline.Snapshot.Inhabitants ?? [])
        {
            cognitionTargetChoice.AddItem(inhabitant.DisplayName);
            var index = cognitionTargetChoice.ItemCount - 1;
            cognitionTargetChoice.SetItemMetadata(index, inhabitant.Id);
            if (inhabitant.Id == target)
            {
                cognitionTargetChoice.Select(index);
            }
        }
    }

    private InhabitantProviderAssignment? SelectedAssignment() => providerConfiguration?.Assignments?
        .FirstOrDefault(item => item.InhabitantId == SelectedCognitionTarget() &&
            item.Role == (SelectedRoleId() == "personal" ? "planning" : SelectedRoleId()));

    private string ActiveProviderForSelectedRole() => SelectedCognitionTarget() is not null
        ? SelectedAssignment()?.Provider ?? "inherit"
        : providerConfiguration is null
        ? "deterministic"
        : SelectedRoleId() == "planning"
            ? providerConfiguration.PlanningProvider
            : providerConfiguration.RoutineProvider;

    private void PopulateProviderChoices(string selectedProvider)
    {
        cognitionProviderChoice.Clear();
        if (SelectedCognitionTarget() is not null)
        {
            AddProviderChoice(SelectedTargetWasBornHere()
                ? "No personal model (safe local)" : "Use world default", "inherit");
        }
        AddProviderChoice("Built-in rules (no model)", "deterministic");
        if (SelectedRoleId() == "routine" && SelectedCognitionTarget() is null)
        {
            AddProviderChoice("Jev", "jev");
        }
        if (SelectedRoleId() == "planning" || SelectedCognitionTarget() is not null)
        {
            AddProviderChoice("OpenAI", "openai");
            AddProviderChoice("Ollama Cloud", "ollama-cloud");
            AddProviderChoice("Anthropic", "anthropic");
        }
        if (SelectedCognitionTarget() is not null && selectedProvider == "jev")
            AddProviderChoice("Jev (legacy assignment)", "jev");

        SelectProviderChoice(selectedProvider);
    }

    private void SelectProviderChoice(string provider)
    {
        for (var index = 0; index < cognitionProviderChoice.ItemCount; index++)
        {
            if (cognitionProviderChoice.GetItemMetadata(index).AsString() == provider)
            {
                cognitionProviderChoice.Select(index);
                return;
            }
        }
        cognitionProviderChoice.Select(0);
    }

    private void AddProviderChoice(string label, string id)
    {
        cognitionProviderChoice.AddItem(label);
        cognitionProviderChoice.SetItemMetadata(cognitionProviderChoice.ItemCount - 1, id);
    }

    private string SelectedProviderId() => cognitionProviderChoice.Selected < 0
        ? "deterministic" : cognitionProviderChoice.GetItemMetadata(cognitionProviderChoice.Selected).AsString();

    private string SelectedCredentialChoice() => cognitionCredentialChoice.Selected < 0
        ? "default" : cognitionCredentialChoice.GetItemMetadata(cognitionCredentialChoice.Selected).AsString();

    private void PopulateCredentialChoices()
    {
        cognitionCredentialChoice.Clear();
        cognitionCredentialChoice.AddItem("Provider default key");
        cognitionCredentialChoice.SetItemMetadata(0, "default");
        var provider = SelectedProviderId();
        foreach (var slot in providerConfiguration?.CredentialSlots ?? [])
        {
            if (slot.Provider != provider) continue;
            cognitionCredentialChoice.AddItem(slot.Label);
            cognitionCredentialChoice.SetItemMetadata(cognitionCredentialChoice.ItemCount - 1, slot.Id);
        }
        cognitionCredentialChoice.AddItem("Add another API key...");
        cognitionCredentialChoice.SetItemMetadata(cognitionCredentialChoice.ItemCount - 1, "new");
        // The agent's saved thinking level applies only to the provider it was set for.
        SelectThinking(cognitionThinkingChoice, SelectedAssignment()?.Provider == provider ? SelectedAssignment()?.Thinking : null);
        var assignedSlot = SelectedAssignment()?.Provider == provider ? SelectedAssignment()?.CredentialSlotId : null;
        for (var index = 0; index < cognitionCredentialChoice.ItemCount; index++)
        {
            if (cognitionCredentialChoice.GetItemMetadata(index).AsString() != assignedSlot) continue;
            cognitionCredentialChoice.Select(index);
            return;
        }
        cognitionCredentialChoice.Select(0);
    }

    /// <summary>Providers that host an agent's own paid model, with a key and a thinking setting.</summary>
    private static bool IsHostedProvider([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? provider) =>
        provider is "openai" or "ollama-cloud" or "anthropic";

    /// <summary>The thinking levels every hosted provider accepts; the model default sends nothing.</summary>
    private static void PopulateThinkingChoices(OptionButton choice)
    {
        choice.Clear();
        foreach (var (label, id) in new[] { ("Model default", "default"), ("Low", "low"), ("Medium", "medium"), ("High", "high") })
        {
            choice.AddItem(label);
            choice.SetItemMetadata(choice.ItemCount - 1, id);
        }
        choice.ClipText = true;
        choice.TooltipText = "How much the model thinks before it answers; more thinking takes longer and uses more paid tokens.";
        choice.Select(0);
    }

    private static string? SelectedThinking(OptionButton choice) => choice.Selected < 0 ||
        choice.GetItemMetadata(choice.Selected).AsString() is not { } id || id == "default" ? null : id;

    private static void SelectThinking(OptionButton choice, string? thinking)
    {
        for (var index = 0; index < choice.ItemCount; index++)
        {
            if (choice.GetItemMetadata(index).AsString() != (thinking ?? "default")) continue;
            choice.Select(index);
            return;
        }
        choice.Select(0);
    }

    private void RenderProviderConfiguration()
    {
        var provider = SelectedProviderId();
        var option = providerConfiguration?.Providers.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.Ordinal));
        var hosted = provider is not ("deterministic" or "inherit");
        var personalSetupCheckAvailable = SelectedCognitionTarget() is not null && HasModelList(provider);
        cognitionRoleChoice.Visible = SelectedCognitionTarget() is null;
        var agentCredential = hosted && SelectedCognitionTarget() is not null && IsHostedProvider(provider);
        var newCredential = agentCredential && SelectedCredentialChoice() == "new";
        cognitionModelPicker.Visible = hosted;
        cognitionThinkingChoice.Visible = agentCredential;
        cognitionModelSetupCheckButton.Visible = personalSetupCheckAvailable;
        cognitionCredentialChoice.Visible = agentCredential;
        cognitionCredentialLabelInput.Visible = newCredential;
        cognitionApiKeyInput.Visible = hosted && (!agentCredential || newCredential);
        cognitionCredentialHint.Visible = hosted;
        forgetCognitionCredentialButton.Visible = hosted && SelectedCognitionTarget() is null;
        deleteCognitionCredentialSlotButton.Visible = agentCredential &&
            SelectedCredentialChoice() is not ("default" or "new");
        if (hosted) SyncCognitionModelPicker();
        ResetCognitionModelSetupCheckForCurrentChoice();
        cognitionModelSetupCheckStatus.Visible = personalSetupCheckAvailable && cognitionModelSetupCheckStatus.Text.Length > 0;

        cognitionApiKeyInput.PlaceholderText = newCredential ? "New API key" : option?.HasCredential == true
            ? "Leave blank to keep saved key"
            : "API key";
        cognitionCredentialHint.Text = newCredential
            ? "A new key is stored privately on the host and can be reused for other agents."
            : agentCredential && SelectedCredentialChoice() != "default"
            ? "This key is saved on the host."
            : agentCredential && option?.HasCredential != true
            ? "No provider default key. Select Add another API key to give this agent one."
            : option?.HasCredential == true
            ? "The key is saved on the host."
            : "No saved key";
        cognitionConfigurationStatus.Text = providerConfiguration is null
            ? "Loading..."
            : SelectedTargetWasBornHere()
            ? SelectedChildModelStatus()
            : $"Routine: {ProviderDisplayName(providerConfiguration.RoutineProvider)} · Planning: {ProviderDisplayName(providerConfiguration.PlanningProvider)}";
        // An agent's own page needs no summary of the world's defaults, and it
        // loads fresh each time it opens, so it needs no Refresh either.
        var agentPage = selectedAgentModelScroll.Visible;
        cognitionConfigurationStatus.Visible = !agentPage || SelectedTargetWasBornHere();
        refreshCognitionProviderButton.Visible = !agentPage;
        // Saved keys are deleted from the world's settings; an agent's page links there instead.
        deleteCognitionCredentialSlotButton.Visible &= !agentPage;
        openModelSettingsButton.Visible = agentPage;
        RefreshControlAvailability();
    }

    private string SelectedChildModelStatus()
    {
        var childId = SelectedCognitionTarget();
        var assignments = providerConfiguration?.Assignments ?? [];
        var routine = assignments.FirstOrDefault(item => item.InhabitantId == childId && item.Role == "routine");
        var planning = assignments.FirstOrDefault(item => item.InhabitantId == childId && item.Role == "planning");
        var child = observationSession.Current?.Baseline.Snapshot.Inhabitants.FirstOrDefault(item => item.Id == childId);
        var birthProvider = child?.DecisionFactors.FirstOrDefault(item => item.Key == "birth-model-provider")?.Detail;
        var birthModel = child?.DecisionFactors.FirstOrDefault(item => item.Key == "birth-model-id")?.Detail;
        if ((routine is null || planning is null) && IsHostedProvider(birthProvider) &&
            !string.IsNullOrWhiteSpace(birthModel))
        {
            var pendingModelName = $"{ProviderDisplayName(birthProvider)} · {birthModel}";
            if (routine is null && planning is null)
                return $"Model needs setup: {pendingModelName} was chosen at birth; its settings are waiting to be saved. Built-in choices continue until setup is recovered.";

            var pendingRoute = $"{pendingModelName} waiting for saved setup; built-in choices continue";
            return $"Routine: {(routine is null ? pendingRoute : ChildModelRouteSummary(routine))} · Planning: {(planning is null ? pendingRoute : ChildModelRouteSummary(planning))}.";
        }
        if (IsUnconfiguredChildRoute(routine) && IsUnconfiguredChildRoute(planning))
            return "No personal model selected for this child. Safe local decisions continue until a model is assigned; world defaults are not used.";

        if (!SameChildProviderRoute(routine, planning))
        {
            return $"Routine: {ChildModelRouteSummary(routine)} · Planning: {ChildModelRouteSummary(planning)}.";
        }

        var assignment = planning ?? routine!;

        var provider = ProviderDisplayName(assignment.Provider);
        var model = assignment.Model ?? providerConfiguration!.Providers.FirstOrDefault(item =>
            item.Provider == assignment.Provider)?.Model;
        var modelName = string.IsNullOrWhiteSpace(model) ? provider : $"{provider} · {model}";
        if (ChildModelNeedsSetup(assignment))
            return $"Model needs setup: {modelName} has no available key on this computer. Add or select a key; built-in choices continue until then, with no other model used.";

        return assignment.SelectionReason switch
        {
            "parents_agreed" => $"Their model choices matched: {modelName}.",
            "initiating_parent" => $"Chosen from the parent who began the family plan: {modelName}.",
            _ => $"Personal model: {modelName}.",
        };
    }

    private string ChildModelRouteSummary(InhabitantProviderAssignment? assignment)
    {
        if (IsUnconfiguredChildRoute(assignment))
            return "no personal model (safe local; world defaults are not used)";

        var provider = ProviderDisplayName(assignment!.Provider);
        var model = assignment.Model ?? providerConfiguration!.Providers.FirstOrDefault(item =>
            item.Provider == assignment.Provider)?.Model;
        var modelName = string.IsNullOrWhiteSpace(model) ? provider : $"{provider} · {model}";
        return ChildModelNeedsSetup(assignment)
            ? $"{modelName} needs setup; built-in choices continue until a key is available"
            : modelName;
    }

    private static bool IsUnconfiguredChildRoute(InhabitantProviderAssignment? assignment) =>
        assignment is null || assignment.Provider is "deterministic" or "inherit";

    private static bool SameChildProviderRoute(
        InhabitantProviderAssignment? left,
        InhabitantProviderAssignment? right) =>
        IsUnconfiguredChildRoute(left) && IsUnconfiguredChildRoute(right) ||
        left is not null && right is not null && left.Provider == right.Provider &&
        left.Model == right.Model && left.CredentialSlotId == right.CredentialSlotId;

    private bool ChildModelNeedsSetup(InhabitantProviderAssignment assignment)
    {
        if (assignment.Provider is "deterministic" or "inherit") return false;
        if (assignment.CredentialSlotId is { } slotId)
            return providerConfiguration?.CredentialSlots?.Any(slot => slot.Id == slotId && slot.Provider == assignment.Provider) != true;
        return providerConfiguration?.Providers.FirstOrDefault(item => item.Provider == assignment.Provider)?.HasCredential != true;
    }

    private static string RoleDisplayName(string role) => role == "planning"
        ? "Planning and work decisions"
        : role == "personal" ? "this agent's decisions"
        : "Routine survival decisions";

    private static string ProviderDisplayName(string provider) => provider switch
    {
        "jev" => "Jev",
        "decisions" or "openaidecisions" => "OpenAI Decisions",
        "openai" => "OpenAI",
        "ollama-cloud" => "Ollama Cloud",
        "anthropic" => "Anthropic",
        "inherit" => "World default",
        _ => "Built-in rules",
    };

    private static string DefaultProviderModel(string provider) => provider switch
    {
        "jev" => "jev-1.13.0",
        "openai" or "decisions" => "gpt-6-luna",
        "ollama-cloud" => "glm-5.3-flash:cloud",
        "anthropic" => "claude-haiku-5-5",
        _ => string.Empty,
    };

    /// <summary>A small caption above a field that hides and shows with it.</summary>
    private static Label FieldCaption(string text, Control field)
    {
        var caption = new Label { Text = text, ThemeTypeVariation = "DimLabel", Visible = field.Visible };
        field.VisibilityChanged += () => caption.Visible = field.Visible;
        return caption;
    }

    private void BuildCognitionSettingsPanel()
    {
        // Long key or model names are cut short rather than widening the Profile.
        foreach (var choice in new[] { cognitionProviderChoice, cognitionCredentialChoice, cognitionRoleChoice })
            choice.ClipText = true;
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);

        cognitionTargetChoice.AddItem("World defaults");
        cognitionTargetChoice.ItemSelected += _ =>
        {
            ClearCognitionModelSetupCheck();
            cognitionApiKeyInput.Text = string.Empty;
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
        };
        body.AddChild(cognitionTargetChoice);

        cognitionRoleChoice.AddItem("Routine survival");
        cognitionRoleChoice.AddItem("Planning and work");
        cognitionRoleChoice.TooltipText = "Routine covers everyday choices. Planning covers bigger projects. Jev, the optional helper, is turned on or off for the whole world in Settings.";
        cognitionRoleChoice.ItemSelected += _ =>
        {
            ClearCognitionModelSetupCheck();
            cognitionApiKeyInput.Text = string.Empty;
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
        };
        var providerRow = new HBoxContainer();
        cognitionRoleChoice.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cognitionProviderChoice.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        providerRow.AddChild(cognitionRoleChoice);

        PopulateProviderChoices("deterministic");
        cognitionProviderChoice.ItemSelected += _ =>
        {
            ClearCognitionModelSetupCheck();
            cognitionApiKeyInput.Text = string.Empty;
            PopulateCredentialChoices();
            RenderProviderConfiguration();
        };
        providerRow.AddChild(cognitionProviderChoice);
        body.AddChild(FieldCaption("Provider", providerRow));
        body.AddChild(providerRow);

        cognitionCredentialChoice.TooltipText = "Pick a saved key for this agent, or add another key for the same provider.";
        cognitionCredentialChoice.ItemSelected += _ =>
        {
            ClearCognitionModelSetupCheck();
            cognitionApiKeyInput.Text = string.Empty;
            RenderProviderConfiguration();
        };
        body.AddChild(FieldCaption("API key", cognitionCredentialChoice));
        body.AddChild(cognitionCredentialChoice);

        cognitionCredentialLabelInput.PlaceholderText = "Name this key (for example, Personal account)";
        body.AddChild(cognitionCredentialLabelInput);

        cognitionApiKeyInput.Secret = true;
        cognitionApiKeyInput.PlaceholderText = "Paste API key";
        cognitionApiKeyInput.TextChanged += _ => OnCognitionKeyEdited();
        body.AddChild(cognitionApiKeyInput);

        cognitionModelPicker.RetryRequested += () => SyncCognitionModelPicker(force: true);
        cognitionModelPicker.ModelChanged += ClearCognitionModelSetupCheck;
        body.AddChild(FieldCaption("Model", cognitionModelPicker));
        body.AddChild(cognitionModelPicker);
        PopulateThinkingChoices(cognitionThinkingChoice);
        cognitionThinkingChoice.ItemSelected += _ => ClearCognitionModelSetupCheck();
        body.AddChild(FieldCaption("Thinking", cognitionThinkingChoice));
        body.AddChild(cognitionThinkingChoice);
        cognitionModelSetupCheckButton.Text = "Test model · 1 paid call";
        cognitionModelSetupCheckButton.TooltipText = "Sends one request with this model and key. It counts toward your paid-call limit.";
        StyleButton(cognitionModelSetupCheckButton);
        cognitionModelSetupCheckButton.Pressed += () => _ = RunCognitionModelSetupCheckAsync();
        body.AddChild(cognitionModelSetupCheckButton);
        cognitionModelSetupCheckStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cognitionModelSetupCheckStatus.ThemeTypeVariation = "DimLabel";
        body.AddChild(cognitionModelSetupCheckStatus);

        cognitionCredentialHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cognitionCredentialHint.ThemeTypeVariation = "DimLabel";
        body.AddChild(cognitionCredentialHint);

        cognitionCredentialHint.TooltipText = "Keys are sent securely and stay on the game server. They are never shown again, logged, or saved in world files.";

        cognitionConfigurationStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(cognitionConfigurationStatus);

        // The buttons wrap rather than push the box wider than an agent's Profile.
        var buttons = new HFlowContainer();
        buttons.AddThemeConstantOverride("h_separation", 6);
        buttons.AddThemeConstantOverride("v_separation", 6);
        saveCognitionProviderButton.Text = "Apply";
        StyleButton(saveCognitionProviderButton, primary: true);
        saveCognitionProviderButton.Pressed += () => _ = SaveProviderConfigurationAsync();
        buttons.AddChild(saveCognitionProviderButton);
        forgetCognitionCredentialButton.Text = "Remove key";
        StyleButton(forgetCognitionCredentialButton);
        forgetCognitionCredentialButton.Pressed += () => _ = ForgetProviderCredentialAsync();
        buttons.AddChild(forgetCognitionCredentialButton);
        deleteCognitionCredentialSlotButton.Text = "Delete this key";
        deleteCognitionCredentialSlotButton.TooltipText = "Delete a saved key. Move agents using it to another key first; a child bound at birth keeps its model and waits for setup.";
        StyleButton(deleteCognitionCredentialSlotButton);
        deleteCognitionCredentialSlotButton.Pressed += () => _ = DeleteCredentialSlotAsync();
        buttons.AddChild(deleteCognitionCredentialSlotButton);
        openModelSettingsButton.Text = "Open model settings";
        openModelSettingsButton.TooltipText = "Opens this agent's model in Settings, where you can also delete saved keys.";
        StyleButton(openModelSettingsButton);
        openModelSettingsButton.Pressed += () => _ = OpenWorldModelSettingsAsync();
        openModelSettingsButton.Hide();
        buttons.AddChild(openModelSettingsButton);
        refreshCognitionProviderButton.Text = "Refresh";
        StyleButton(refreshCognitionProviderButton);
        refreshCognitionProviderButton.Pressed += () => _ = RefreshProviderConfigurationAsync();
        buttons.AddChild(refreshCognitionProviderButton);
        body.AddChild(buttons);

        AddPanelContents(cognitionSettingsPanel, "Agent model", body);
        cognitionSettingsPanel.ThemeTypeVariation = "InsetPanel";
        RenderProviderConfiguration();
    }

    /// <summary>
    /// The installation's model-call count and optional limit. One count and
    /// limit cover every world, so the box sits on the Game page and opens
    /// from the Main Menu as well as in a world.
    /// </summary>
    private void BuildUsageLimitPanel()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        usageScopeHint.Text = "Counts every model call attempt in all your worlds, including ones that fail or are retried. " +
            "The count never resets; the Event Log warns you once at 80% of the limit.";
        usageScopeHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        usageScopeHint.ThemeTypeVariation = "DimLabel";
        body.AddChild(usageScopeHint);
        usageMeterStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(usageMeterStatus);
        usageAttemptLimitInput.PlaceholderText = "No limit";
        usageAttemptLimitInput.TooltipText = "Your worlds pause when this many calls have been made; it counts calls, not money.";
        usageAttemptLimitInput.TextChanged += _ => usageLimitEdited = true;
        body.AddChild(DisplaySettingRow("Limit", usageAttemptLimitInput));
        var usageButtons = new HBoxContainer();
        usageButtons.AddThemeConstantOverride("separation", 6);
        applyUsageLimitButton.Text = "Set limit";
        StyleButton(applyUsageLimitButton);
        applyUsageLimitButton.Pressed += () => _ = ConfigureUsageAsync(grant: false);
        usageButtons.AddChild(applyUsageLimitButton);
        grantUsageCallsButton.Text = "Allow 100 more calls";
        grantUsageCallsButton.TooltipText = "Allow 100 more model calls; the world stays paused until you resume it.";
        StyleButton(grantUsageCallsButton, primary: true);
        grantUsageCallsButton.Pressed += () => _ = ConfigureUsageAsync(grant: true);
        grantUsageCallsButton.Visible = false;
        usageButtons.AddChild(grantUsageCallsButton);
        refreshUsageButton.Text = "Refresh";
        StyleButton(refreshUsageButton);
        refreshUsageButton.Pressed += () => _ = RefreshUsageAsync();
        usageButtons.AddChild(refreshUsageButton);
        body.AddChild(usageButtons);
        AddPanelContents(usageLimitPanel, "Model calls", body);
        usageLimitPanel.ThemeTypeVariation = "InsetPanel";
        RenderUsageStatus();
    }

    private void OpenAgentModelEditor()
    {
        if (selectedInhabitantId is null || registration is null || observationSession.Current is not { } current) return;
        PopulateCognitionTargets();
        for (var index = 1; index < cognitionTargetChoice.ItemCount; index++)
        {
            if (cognitionTargetChoice.GetItemMetadata(index).AsString() != selectedInhabitantId) continue;
            cognitionTargetChoice.Select(index);
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            cognitionTargetChoice.Hide();
            cognitionSettingsPanel.Reparent(selectedAgentModelContent, keepGlobalTransform: false);
            selectedAgentOverview.Hide();
            renameRow.Hide();
            selectedAgentModelScroll.Show();
            UpdateAgentProfileCloseButton(living: true);
            RenderProviderConfiguration();
            PositionSelectedInhabitantCard(current.Baseline.Snapshot);
            _ = RefreshProviderConfigurationAsync();
            return;
        }
        SetStatus("You can't change this agent's model right now.", good: false);
    }

    private readonly MarginContainer selectedAgentModelGap = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    /// <summary>
    /// An agent's model settings fill the Profile's width and are as tall as
    /// they need, up to the bottom of the screen, scrolling beyond that.
    /// </summary>
    private void BuildAgentModelScroll()
    {
        selectedAgentModelScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        selectedAgentModelContent.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        selectedAgentModelContent.MinimumSizeChanged += PositionAgentProfile;
        selectedAgentModelGap.AddChild(selectedAgentModelContent);
        selectedAgentModelScroll.AddChild(selectedAgentModelGap);
    }

    private void FitAgentModelScroll()
    {
        var content = selectedAgentModelContent.GetCombinedMinimumSize().Y;
        var rest = agentProfilePanel.GetCombinedMinimumSize().Y - selectedAgentModelScroll.CustomMinimumSize.Y;
        var room = Math.Max(120, UiSize.Y - HudTop - 14 - rest);
        var scrolls = content > room;
        selectedAgentModelGap.AddThemeConstantOverride("margin_right", scrolls ? SettingsScrollGap : 0);
        selectedAgentModelScroll.CustomMinimumSize = new Vector2(0, scrolls ? room : content);
    }

    /// <summary>
    /// From an agent's page, opens Settings on the World page at the Agent
    /// model box with this agent still chosen, where saved keys are managed.
    /// </summary>
    private async Task OpenWorldModelSettingsAsync()
    {
        var generation = observationSession.RequestGeneration;
        CloseAgentModelEditor();
        if (!gameMenuPanel.Visible) await ToggleGameMenuAsync();
        if (!IsCurrentWorldRequest(generation)) return;
        ShowSettingsSection(worldSpecific: true);
        RenderProviderConfiguration();
        // The page opens at its top; bring the box into view once it is laid out.
        Callable.From(() => settingsScroll.EnsureControlVisible(cognitionSettingsPanel)).CallDeferred();
    }

    private void CloseAgentModelEditor()
    {
        if (!selectedAgentModelScroll.Visible) return;
        selectedAgentModelScroll.Hide();
        // Agent model is the World page's last box, so it returns to the end.
        cognitionSettingsPanel.Reparent(worldSettingsContent, keepGlobalTransform: false);
        cognitionTargetChoice.Show();
        selectedAgentOverview.Show();
        cognitionApiKeyInput.Text = string.Empty;
        cognitionCredentialLabelInput.Text = string.Empty;
    }

}
