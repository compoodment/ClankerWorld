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

    private static bool HasModelList(string provider) => provider is "openai" or "ollama-cloud";

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
            if (!picker.IsLatest(lookup)) return;
            if (checkKey) picker.ShowList(list.Models, list.DefaultModel, list.Error);
            else picker.ShowList(list.Models, list.DefaultModel, PasteKeyNote, canRetry: false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (picker.IsLatest(lookup))
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
        var stamp = ++founderKeyEdits;
        GetTree().CreateTimer(PastedKeyPause).Timeout += () =>
        {
            if (stamp == founderKeyEdits) RequestFounderModels();
        };
    }

    private void OnCognitionKeyEdited()
    {
        var stamp = ++cognitionKeyEdits;
        GetTree().CreateTimer(PastedKeyPause).Timeout += () =>
        {
            if (stamp == cognitionKeyEdits) SyncCognitionModelPicker();
        };
    }
}
