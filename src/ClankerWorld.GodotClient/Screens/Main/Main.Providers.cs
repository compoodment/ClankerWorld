using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
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
            providerConfiguration = await ownerApi.GetProviderStatusAsync(
                ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None);
            PopulateCognitionTargets();
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            return "Loaded Agent model settings";
        });
    }

    private async Task RefreshUsageAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            usageMeterStatus.Text = "Connect this device to see model-call usage.";
            return;
        }
        await RunOwnerActionAsync(async () =>
        {
            usageStatus = await ownerApi.GetUsageStatusAsync(ResolveWorldUri(), authority, deviceId,
                signer, CancellationToken.None);
            RenderUsageStatus();
            return "loaded paid-call usage";
        });
    }

    private async Task ObserveUsagePauseAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        try
        {
            usageStatus = await ownerApi.GetUsageStatusAsync(ResolveWorldUri(), authority, deviceId,
                signer, CancellationToken.None);
            RenderUsageStatus();
            if (usageStatus.LimitReached)
                SetStatus("Paid-call limit reached. The world is paused; open World Settings to allow more calls.", good: false);
        }
        catch (Exception)
        {
            // A pause is authoritative even when the optional meter read is unavailable.
        }
    }

    private void RenderUsageStatus()
    {
        if (usageStatus is null)
        {
            usageMeterStatus.Text = "Loading model calls…";
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
        if (!usageAttemptLimitInput.HasFocus())
            usageAttemptLimitInput.Text = usageStatus.AttemptLimit?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var rows = usageStatus.Rows.OrderByDescending(row => row.Attempts)
            .Select(row => $"{row.Provider} / {row.Model}: {row.Attempts} calls");
        usageMeterStatus.Text = usageStatus.AttemptLimit is { } limit
            ? $"{usageStatus.Attempts} of {limit} model calls used on this installation."
            : $"{usageStatus.Attempts} model calls used on this installation.";
        if (usageStatus.LimitReached)
            usageMeterStatus.Text += " Time is paused. Raise the limit to allow more calls, then resume.";
        if (usageStatus.Rows.Count > 0)
            usageMeterStatus.Text += "\n" + string.Join("\n", rows);
        usageMeterStatus.TooltipText = $"Calls started: {usageStatus.Attempts}; completed: {usageStatus.Completed}; " +
            $"failed: {usageStatus.Failed}; interrupted: {usageStatus.Abandoned}. " +
            $"Known input/output tokens: {usageStatus.InputTokens}/{usageStatus.OutputTokens}. " +
            "Counts since this installation began; each retry counts as another call.";
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
                SetStatus("Enter a number of calls from 1 to 1,000,000, or leave it blank for no cap.", good: false);
                return;
            }
            cap = parsed;
        }
        var action = grant ? new OwnerUsageLimitAction(null, AdditionalCalls: 100) :
            new OwnerUsageLimitAction(cap);
        await RunOwnerActionAsync(async () =>
        {
            usageStatus = await ownerApi.ConfigureUsageLimitAsync(ResolveWorldUri(), authority, deviceId,
                action, signer, CancellationToken.None);
            RenderUsageStatus();
            if (grant)
                return "Allowed 100 more paid calls. Resume the world when ready";
            return cap is null ? "paid-call limit turned off" : $"paid-call limit set to {cap} attempts";
        });
    }

    private async Task SaveProviderConfigurationAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            SetStatus("Connect this device before setting up agent models.", good: false);
            return;
        }

        var role = SelectedRoleId();
        var provider = SelectedProviderId();
        var target = SelectedCognitionTarget();
        var hostedAgent = target is not null && provider is ("openai" or "ollama-cloud");
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
            NewCredentialLabel: creatingSlot ? EmptyToNull(cognitionCredentialLabelInput.Text) : null);
        try
        {
            await RunOwnerActionAsync(async () =>
            {
                providerConfiguration = await ownerApi.ConfigureProviderAsync(
                    ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None);
                PopulateCredentialChoices();
                return $"{ProviderDisplayName(provider)} will handle {RoleDisplayName(role).ToLowerInvariant()} at the agent's next model choice";
            });
        }
        finally
        {
            cognitionApiKeyInput.Text = string.Empty;
            cognitionCredentialLabelInput.Text = string.Empty;
            RenderProviderConfiguration();
        }
    }

    private async Task ForgetProviderCredentialAsync()
    {
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
            providerConfiguration = await ownerApi.ConfigureProviderAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None);
            return $"forgot the saved {ProviderDisplayName(provider)} key";
        });
        cognitionApiKeyInput.Text = string.Empty;
        RenderProviderConfiguration();
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
            providerConfiguration = await ownerApi.DeleteCredentialSlotAsync(
                ResolveWorldUri(), authority, deviceId, slotId, signer, CancellationToken.None);
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
        cognitionCredentialChoice.AddItem("Add another API key…");
        cognitionCredentialChoice.SetItemMetadata(cognitionCredentialChoice.ItemCount - 1, "new");
        var assignedSlot = SelectedAssignment()?.Provider == provider ? SelectedAssignment()?.CredentialSlotId : null;
        for (var index = 0; index < cognitionCredentialChoice.ItemCount; index++)
        {
            if (cognitionCredentialChoice.GetItemMetadata(index).AsString() != assignedSlot) continue;
            cognitionCredentialChoice.Select(index);
            return;
        }
        cognitionCredentialChoice.Select(0);
    }

    private void RenderProviderConfiguration()
    {
        var provider = SelectedProviderId();
        var option = providerConfiguration?.Providers.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.Ordinal));
        var hosted = provider is not ("deterministic" or "inherit");
        var personalSetupCheckAvailable = SelectedCognitionTarget() is not null && HasModelList(provider);
        cognitionRoleChoice.Visible = SelectedCognitionTarget() is null;
        var agentCredential = hosted && SelectedCognitionTarget() is not null && provider is ("openai" or "ollama-cloud");
        var newCredential = agentCredential && SelectedCredentialChoice() == "new";
        cognitionModelPicker.Visible = hosted;
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
            ? "Named key saved on host"
            : agentCredential && option?.HasCredential != true
            ? "No provider default key. Select Add another API key to give this agent one."
            : option?.HasCredential == true
            ? "Key saved on host"
            : "No saved key";
        cognitionConfigurationStatus.Text = providerConfiguration is null
            ? "Loading…"
            : SelectedTargetWasBornHere()
            ? SelectedChildModelStatus()
            : $"Routine: {ProviderDisplayName(providerConfiguration.RoutineProvider)} · Planning: {ProviderDisplayName(providerConfiguration.PlanningProvider)}";
        RefreshControlAvailability();
    }

    private string SelectedChildModelStatus()
    {
        var childId = SelectedCognitionTarget();
        var assignments = providerConfiguration?.Assignments ?? [];
        var routine = assignments.FirstOrDefault(item => item.InhabitantId == childId && item.Role == "routine");
        var planning = assignments.FirstOrDefault(item => item.InhabitantId == childId && item.Role == "planning");
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
        "openai" => "OpenAI",
        "ollama-cloud" => "Ollama Cloud",
        "inherit" => "World default",
        _ => "Built-in rules",
    };

    private static string DefaultProviderModel(string provider) => provider switch
    {
        "jev" => "jev-1.13.0",
        "openai" => "gpt-6-luna",
        "ollama-cloud" => "glm-5.3-flash:cloud",
        _ => string.Empty,
    };

    private void BuildCognitionSettingsPanel()
    {
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
        body.AddChild(providerRow);

        cognitionCredentialChoice.TooltipText = "Pick a saved key for this agent, or add another key for the same provider.";
        cognitionCredentialChoice.ItemSelected += _ =>
        {
            ClearCognitionModelSetupCheck();
            cognitionApiKeyInput.Text = string.Empty;
            RenderProviderConfiguration();
        };
        body.AddChild(cognitionCredentialChoice);

        cognitionCredentialLabelInput.PlaceholderText = "Name this key (for example, Personal account)";
        body.AddChild(cognitionCredentialLabelInput);

        cognitionApiKeyInput.Secret = true;
        cognitionApiKeyInput.PlaceholderText = "Paste API key";
        cognitionApiKeyInput.TextChanged += _ => OnCognitionKeyEdited();
        body.AddChild(cognitionApiKeyInput);

        cognitionModelPicker.RetryRequested += () => SyncCognitionModelPicker(force: true);
        cognitionModelPicker.ModelChanged += ClearCognitionModelSetupCheck;
        body.AddChild(cognitionModelPicker);
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

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        saveCognitionProviderButton.Text = "Apply";
        StyleButton(saveCognitionProviderButton, primary: true);
        saveCognitionProviderButton.Pressed += () => _ = SaveProviderConfigurationAsync();
        buttons.AddChild(saveCognitionProviderButton);
        forgetCognitionCredentialButton.Text = "Remove key";
        StyleButton(forgetCognitionCredentialButton);
        forgetCognitionCredentialButton.Pressed += () => _ = ForgetProviderCredentialAsync();
        buttons.AddChild(forgetCognitionCredentialButton);
        deleteCognitionCredentialSlotButton.Text = "Delete named key";
        deleteCognitionCredentialSlotButton.TooltipText = "Delete a saved key. Move agents using it to another key first; a child bound at birth keeps its model and waits for setup.";
        StyleButton(deleteCognitionCredentialSlotButton);
        deleteCognitionCredentialSlotButton.Pressed += () => _ = DeleteCredentialSlotAsync();
        buttons.AddChild(deleteCognitionCredentialSlotButton);
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
    /// The installation's model-call count and optional limit. It stays on the
    /// World page when the Agent model box moves into an agent's card.
    /// </summary>
    private void BuildUsageLimitPanel()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        usageMeterStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(usageMeterStatus);
        usageAttemptLimitInput.PlaceholderText = "No limit";
        usageAttemptLimitInput.TooltipText = "Time pauses when this many calls have been made. Every call counts, even ones that fail or are retried. This counts calls, not money.";
        body.AddChild(DisplaySettingRow("Limit", usageAttemptLimitInput));
        var usageButtons = new HBoxContainer();
        usageButtons.AddThemeConstantOverride("separation", 6);
        applyUsageLimitButton.Text = "Set limit";
        StyleButton(applyUsageLimitButton);
        applyUsageLimitButton.Pressed += () => _ = ConfigureUsageAsync(grant: false);
        usageButtons.AddChild(applyUsageLimitButton);
        grantUsageCallsButton.Text = "Allow 100 more calls";
        grantUsageCallsButton.TooltipText = "Allow 100 more paid model calls. The world stays paused until you resume it.";
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

    private void CloseAgentModelEditor()
    {
        if (!selectedAgentModelScroll.Visible) return;
        selectedAgentModelScroll.Hide();
        cognitionSettingsPanel.Reparent(worldSettingsContent, keepGlobalTransform: false);
        worldSettingsContent.MoveChild(cognitionSettingsPanel, usageLimitPanel.GetIndex());
        cognitionTargetChoice.Show();
        selectedAgentOverview.Show();
        cognitionApiKeyInput.Text = string.Empty;
        cognitionCredentialLabelInput.Text = string.Empty;
    }

}
