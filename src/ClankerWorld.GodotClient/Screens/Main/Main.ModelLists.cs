using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private const double PastedKeyPause = 0.8;
    private int founderKeyEdits;
    private int cognitionKeyEdits;
    private string? cognitionModelContext;
    private string? cognitionModelLookup;

    private static bool HasModelList(string provider) => IsHostedProvider(provider) || provider is "jev" or "decisions";

    private const string PasteKeyNote = "Paste the key to check which of these it can use.";

    /// <summary>
    /// Fills a model picker with the game's list from the host. When a key is
    /// known, the host checks it with the provider: the saved key, a named key
    /// slot, or a key that was just pasted and not saved. Keys never come back
    /// to this device. Without a key yet, the list is shown unchecked.
    /// </summary>
    private async Task LoadModelListAsync(ModelPicker picker, string provider, string? credentialSlotId, string? apiKey,
        bool checkKey = true)
    {
        var generation = observationSession.RequestGeneration;
        var defaultModel = DefaultProviderModel(provider);
        if (!HasModelList(provider))
        {
            picker.ShowTypedOnly(picker.Model.Length > 0 ? picker.Model : defaultModel);
            return;
        }
        var lookup = picker.BeginLoading(defaultModel);
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            picker.ShowError("Connect this device to see the models.", defaultModel, canRetry: false);
            return;
        }
        try
        {
            var list = await ownerApi.ListProviderModelsAsync(ResolveWorldUri(), authority, deviceId,
                new OwnerProviderModelListAction(provider, credentialSlotId, apiKey, checkKey), signer, CancellationToken.None);
            if (!IsCurrentWorldRequest(generation) || !picker.IsLatest(lookup)) return;
            if (checkKey) picker.ShowList(list.Models, list.DefaultModel, list.Error);
            else picker.ShowList(list.Models, list.DefaultModel, PasteKeyNote, canRetry: false);
        }
        catch (ClankerWorld.GodotClient.Pairing.OwnerActionCompatibilityException exception)
        {
            if (IsCurrentWorldRequest(generation) && picker.IsLatest(lookup))
                picker.ShowError(FriendlyFailure(exception), defaultModel, canRetry: false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (IsCurrentWorldRequest(generation) && picker.IsLatest(lookup))
                picker.ShowError("Couldn't reach the game server for the model list. Type a model name or try again.", defaultModel);
        }
    }

    private void RequestFounderModels()
    {
        if (!founderSetupPanel.Visible) return;
        var provider = SelectedFounderProvider();
        var choice = SelectedFounderCredential();
        if (choice == "new")
        {
            var pasted = string.IsNullOrWhiteSpace(founderApiKeyInput.Text) ? null : founderApiKeyInput.Text;
            _ = LoadModelListAsync(founderModelPicker, provider, null, pasted, checkKey: pasted is not null);
            return;
        }
        _ = LoadModelListAsync(founderModelPicker, provider, choice == "default" ? null : choice, null);
    }

    /// <summary>
    /// Keeps the Model settings picker in step with the chosen agent, provider
    /// and key. The shown model is reset only when the agent or provider
    /// changes, so switching keys keeps the owner's pick.
    /// </summary>
    private void SyncCognitionModelPicker(bool force = false)
    {
        var provider = SelectedProviderId();
        if (provider is "deterministic" or "inherit" || providerConfiguration is null) return;
        var option = providerConfiguration.Providers.FirstOrDefault(item => item.Provider == provider);
        var assigned = SelectedAssignment() is { } assignment && assignment.Provider == provider
            ? assignment.Model ?? option?.Model : option?.Model;
        var model = assigned ?? DefaultProviderModel(provider);
        var context = $"{SelectedCognitionTarget()}|{SelectedRoleId()}|{provider}|{model}";
        if (context != cognitionModelContext)
        {
            cognitionModelContext = context;
            cognitionModelPicker.SetModel(model);
            force = true;
        }
        if (!cognitionSettingsPanel.IsVisibleInTree()) return;

        var agentCredential = SelectedCognitionTarget() is not null && HasModelList(provider);
        var credential = agentCredential ? SelectedCredentialChoice() : null;
        var pasted = string.IsNullOrWhiteSpace(cognitionApiKeyInput.Text) ? null : cognitionApiKeyInput.Text;
        var lookup = $"{provider}|{credential}|{pasted?.GetHashCode(StringComparison.Ordinal)}";
        if (!force && lookup == cognitionModelLookup) return;
        cognitionModelLookup = lookup;
        var slot = credential is null or "default" or "new" ? null : credential;
        var usePasted = credential == "new" || !agentCredential;
        _ = LoadModelListAsync(cognitionModelPicker, provider, slot, usePasted ? pasted : null,
            checkKey: credential != "new" || pasted is not null);
    }

    // A pasted key is looked up once the owner stops typing, not per keystroke.
    private void OnFounderKeyEdited()
    {
        ClearFounderModelSetupCheck();
        var stamp = ++founderKeyEdits;
        GetTree().CreateTimer(PastedKeyPause).Timeout += () =>
        {
            if (stamp == founderKeyEdits) RequestFounderModels();
        };
    }

    private void OnCognitionKeyEdited()
    {
        ClearCognitionModelSetupCheck();
        var stamp = ++cognitionKeyEdits;
        GetTree().CreateTimer(PastedKeyPause).Timeout += () =>
        {
            if (stamp == cognitionKeyEdits) SyncCognitionModelPicker();
        };
    }
    private string SelectedRoutineHelper() => routineHelperChoice.Selected switch { 0 => "off", 2 => "decisions", _ => "jev" };

    private void BuildRoutineHelperSettings()
    {
        routineHelperChoice.AddItem("Off");
        routineHelperChoice.AddItem("Jev");
        routineHelperChoice.AddItem("OpenAI Decisions");
        routineHelperChoice.Select(1);
        routineHelperChoice.TooltipText = "Choose an optional helper for small everyday actions and memory scoring.";
        routineHelperChoice.ItemSelected += _ =>
        {
            var provider = SelectedRoutineHelper();
            routineHelperModelPicker.SetModel(DefaultProviderModel(provider));
            RequestRoutineHelperModels();
            RefreshControlAvailability();
        };
        routineHelperKeyChoice.FitToLongestItem = false;
        routineHelperKeyChoice.ClipText = true;
        routineHelperKeyChoice.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        routineHelperKeyChoice.TooltipText = "Choose an existing OpenAI key for this world's helper.";
        routineHelperKeyChoice.ItemSelected += _ => { RequestRoutineHelperModels(); RefreshControlAvailability(); };
        routineHelperModelPicker.ModelChanged += RefreshControlAvailability;
        routineHelperModelPicker.RetryRequested += RequestRoutineHelperModels;
        applyRoutineHelperButton.Text = "Apply helper";
        StyleButton(applyRoutineHelperButton);
        applyRoutineHelperButton.Pressed += () => _ = SaveRoutineHelperAsync();
        var hint = new Label
        {
            Text = "Pause the world to change its helper. Jev uses your saved TypeSafe key; OpenAI Decisions uses your saved OpenAI key, and switching keeps memories.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        worldSettingsContent.AddChild(SettingsBox("Routine helper", routineHelperChoice, routineHelperKeyChoice, routineHelperModelPicker, applyRoutineHelperButton, hint));
    }

    private void RequestRoutineHelperModels()
    {
        var provider = SelectedRoutineHelper();
        routineHelperModelPicker.Visible = provider != "off";
        routineHelperKeyChoice.Visible = provider == "decisions";
        if (provider == "off")
        {
            _ = routineHelperModelPicker.BeginLoading(string.Empty); // Invalidate a previous provider's lookup.
            return;
        }
        _ = LoadModelListAsync(routineHelperModelPicker, provider, provider == "decisions" ? SelectedRoutineHelperKey() : null, null);
    }

    private void RenderRoutineHelperSettings(OwnerWorldSnapshot snapshot)
    {
        var provider = snapshot.RoutineHelperProvider ?? (snapshot.JevEnabled == true ? "jev" : "off");
        var model = snapshot.RoutineHelperModel ?? DefaultProviderModel(provider);
        var context = $"{snapshot.WorldId}|{observationSession.RequestGeneration}|{provider}|{model}|{snapshot.RoutineHelperCredentialSlotId}";
        if (context == observedRoutineHelperContext) return;
        observedRoutineHelperContext = context;
        routineHelperChoice.Select(provider switch { "off" => 0, "decisions" => 2, _ => 1 });
        routineHelperModelPicker.SetModel(model);
        RefreshRoutineHelperKeys(snapshot.RoutineHelperCredentialSlotId);
        RequestRoutineHelperModels();
    }

    private string? SelectedRoutineHelperKey()
    {
        if (routineHelperKeyChoice.Selected < 0) return null;
        var id = routineHelperKeyChoice.GetItemMetadata(routineHelperKeyChoice.Selected).AsString();
        return id.Length == 0 ? null : id;
    }

    private void RefreshRoutineHelperKeys(string? selectedSlotId = null)
    {
        routineHelperKeyChoice.Clear();
        routineHelperKeyChoice.AddItem("Default OpenAI key");
        routineHelperKeyChoice.SetItemMetadata(0, "");
        foreach (var slot in providerConfiguration?.CredentialSlots ?? [])
        {
            if (slot.Provider != "openai") continue;
            routineHelperKeyChoice.AddItem(slot.Label);
            routineHelperKeyChoice.SetItemMetadata(routineHelperKeyChoice.ItemCount - 1, slot.Id);
        }
        if (selectedSlotId is not null && !Enumerable.Range(0, routineHelperKeyChoice.ItemCount)
            .Any(index => routineHelperKeyChoice.GetItemMetadata(index).AsString() == selectedSlotId))
        {
            routineHelperKeyChoice.AddItem("Saved key unavailable — choose another");
            routineHelperKeyChoice.SetItemMetadata(routineHelperKeyChoice.ItemCount - 1, selectedSlotId);
        }
        for (var index = 0; index < routineHelperKeyChoice.ItemCount; index++)
            if (routineHelperKeyChoice.GetItemMetadata(index).AsString() == (selectedSlotId ?? "")) routineHelperKeyChoice.Select(index);
    }

}
