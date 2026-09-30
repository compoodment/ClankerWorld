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
            return "loaded inhabitant cognition settings";
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
            : $"{usageStatus.Attempts} model calls used on this installation. No limit set.";
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
        var action = new OwnerProviderConfigurationAction(
            role,
            provider,
            provider is "deterministic" or "inherit" ? null : EmptyToNull(cognitionModelInput.Text),
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
                return $"{ProviderDisplayName(provider)} will handle {RoleDisplayName(role).ToLowerInvariant()} at the next cognition boundary";
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
            EmptyToNull(cognitionModelInput.Text),
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
        if (providerConfiguration?.Assignments?.Any(item => item.CredentialSlotId == slotId) == true)
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
        cognitionRoleChoice.Visible = SelectedCognitionTarget() is null;
        var agentCredential = hosted && SelectedCognitionTarget() is not null && provider is ("openai" or "ollama-cloud");
        var newCredential = agentCredential && SelectedCredentialChoice() == "new";
        cognitionModelInput.Visible = hosted;
        cognitionCredentialChoice.Visible = agentCredential;
        cognitionCredentialLabelInput.Visible = newCredential;
        cognitionApiKeyInput.Visible = hosted && (!agentCredential || newCredential);
        cognitionCredentialHint.Visible = hosted;
        forgetCognitionCredentialButton.Visible = hosted && SelectedCognitionTarget() is null;
        deleteCognitionCredentialSlotButton.Visible = agentCredential &&
            SelectedCredentialChoice() is not ("default" or "new");
        if (hosted && option is not null && !cognitionModelInput.HasFocus())
        {
            cognitionModelInput.Text = SelectedAssignment() is { } assignment && assignment.Provider == provider
                ? assignment.Model ?? option.Model : option.Model;
        }

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
            : SelectedTargetWasBornHere() && SelectedAssignment() is null
            ? "No personal model selected for this child. After infancy, safe local decisions continue until a model is assigned; world defaults are not used."
            : $"Routine: {ProviderDisplayName(providerConfiguration.RoutineProvider)} · Planning: {ProviderDisplayName(providerConfiguration.PlanningProvider)}";
        RefreshControlAvailability();
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
        "openai" => "gpt-5-mini",
        "ollama-cloud" => "gpt-oss:120b-cloud",
        _ => string.Empty,
    };

    private void BuildCognitionSettingsPanel()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);

        cognitionTargetChoice.AddItem("World defaults");
        cognitionTargetChoice.ItemSelected += _ =>
        {
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
            cognitionApiKeyInput.Text = string.Empty;
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            var selected = SelectedProviderId();
            var option = providerConfiguration?.Providers.FirstOrDefault(item => item.Provider == selected);
            cognitionModelInput.Text = option?.Model ?? DefaultProviderModel(selected);
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
            cognitionApiKeyInput.Text = string.Empty;
            var selected = SelectedProviderId();
            var option = providerConfiguration?.Providers.FirstOrDefault(item => item.Provider == selected);
            cognitionModelInput.Text = option?.Model ?? DefaultProviderModel(selected);
            PopulateCredentialChoices();
            RenderProviderConfiguration();
        };
        providerRow.AddChild(cognitionProviderChoice);
        body.AddChild(providerRow);

        cognitionCredentialChoice.TooltipText = "Pick a saved key for this agent, or add another key for the same provider.";
        cognitionCredentialChoice.ItemSelected += _ =>
        {
            cognitionApiKeyInput.Text = string.Empty;
            RenderProviderConfiguration();
        };
        body.AddChild(cognitionCredentialChoice);

        cognitionCredentialLabelInput.PlaceholderText = "Name this key (for example, Personal account)";
        body.AddChild(cognitionCredentialLabelInput);

        cognitionModelInput.PlaceholderText = "Model ID";
        body.AddChild(cognitionModelInput);

        cognitionApiKeyInput.Secret = true;
        cognitionApiKeyInput.PlaceholderText = "Paste API key";
        body.AddChild(cognitionApiKeyInput);

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
        deleteCognitionCredentialSlotButton.TooltipText = "Delete a saved key you no longer use. Move any agents using it to another key first.";
        StyleButton(deleteCognitionCredentialSlotButton);
        deleteCognitionCredentialSlotButton.Pressed += () => _ = DeleteCredentialSlotAsync();
        buttons.AddChild(deleteCognitionCredentialSlotButton);
        refreshCognitionProviderButton.Text = "Refresh";
        StyleButton(refreshCognitionProviderButton);
        refreshCognitionProviderButton.Pressed += () => _ = RefreshProviderConfigurationAsync();
        buttons.AddChild(refreshCognitionProviderButton);
        body.AddChild(buttons);

        body.AddChild(new Label { Text = "Model calls" });
        usageMeterStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(usageMeterStatus);
        body.AddChild(new Label
        {
            Text = "Optional cap on paid model calls. The game pauses when you reach it. Leave blank for no cap.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        usageAttemptLimitInput.PlaceholderText = "Maximum model calls (blank = no limit)";
        usageAttemptLimitInput.TooltipText = "Every call counts, even ones that fail or are retried. This counts calls, not money.";
        body.AddChild(usageAttemptLimitInput);
        var usageButtons = new HBoxContainer();
        applyUsageLimitButton.Text = "Apply limit";
        StyleButton(applyUsageLimitButton);
        applyUsageLimitButton.Pressed += () => _ = ConfigureUsageAsync(grant: false);
        usageButtons.AddChild(applyUsageLimitButton);
        grantUsageCallsButton.Text = "Allow 100 more calls";
        grantUsageCallsButton.TooltipText = "Allow 100 more paid model calls. The world stays paused until you resume it.";
        StyleButton(grantUsageCallsButton, primary: true);
        grantUsageCallsButton.Pressed += () => _ = ConfigureUsageAsync(grant: true);
        grantUsageCallsButton.Visible = false;
        usageButtons.AddChild(grantUsageCallsButton);
        refreshUsageButton.Text = "Refresh usage";
        StyleButton(refreshUsageButton);
        refreshUsageButton.Pressed += () => _ = RefreshUsageAsync();
        usageButtons.AddChild(refreshUsageButton);
        body.AddChild(usageButtons);

        AddPanelContents(cognitionSettingsPanel, "Agent model", body);
        cognitionSettingsPanel.ThemeTypeVariation = "InsetPanel";
        RenderProviderConfiguration();
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
            selectedAgentOverviewScroll.Hide();
            selectedAgentModelScroll.Show();
            StyleIconButton(clearSelectionButton, PixelGlyph.Back);
            clearSelectionButton.TooltipText = "Back to the agent's profile";
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
        cognitionTargetChoice.Show();
        selectedAgentOverviewScroll.Show();
        StyleIconButton(clearSelectionButton, PixelGlyph.Close);
        clearSelectionButton.TooltipText = "Close";
        cognitionApiKeyInput.Text = string.Empty;
        cognitionCredentialLabelInput.Text = string.Empty;
    }

}
