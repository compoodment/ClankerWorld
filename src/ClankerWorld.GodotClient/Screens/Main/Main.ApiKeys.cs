using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly PanelContainer apiKeysPanel = new();
    private readonly OptionButton apiKeyProviderChoice = new();
    private readonly LineEdit apiKeyLabelInput = new();
    private readonly LineEdit apiKeyInput = new();
    private readonly Button saveApiKeyButton = new();
    private readonly Label apiKeysStatus = new();
    private readonly Label savedApiKeys = new();
    private int apiKeysRead;

    private void BuildApiKeysPanel()
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 7);
        body.AddChild(new Label
        {
            Text = "Save a key once, then choose it for any agent.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        apiKeyProviderChoice.AddItem("OpenAI");
        apiKeyProviderChoice.SetItemMetadata(0, "openai");
        apiKeyProviderChoice.AddItem("Ollama Cloud");
        apiKeyProviderChoice.SetItemMetadata(1, "ollama-cloud");
        apiKeyProviderChoice.ItemSelected += _ => apiKeyInput.Text = string.Empty;
        body.AddChild(apiKeyProviderChoice);
        apiKeyLabelInput.PlaceholderText = "Name this key (for example, Personal account)";
        body.AddChild(apiKeyLabelInput);
        apiKeyInput.Secret = true;
        apiKeyInput.PlaceholderText = "Paste API key";
        body.AddChild(apiKeyInput);
        saveApiKeyButton.Text = "Save API key";
        StyleButton(saveApiKeyButton);
        saveApiKeyButton.Pressed += () => _ = SaveApiKeyAsync();
        body.AddChild(saveApiKeyButton);
        apiKeysStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        apiKeysStatus.Text = "Connect and pair this device to save keys.";
        body.AddChild(apiKeysStatus);
        savedApiKeys.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        // Shown once the host has said which keys are saved, so it leaves no blank line before then.
        savedApiKeys.Hide();
        body.AddChild(savedApiKeys);
        AddPanelContents(apiKeysPanel, "API keys", body);
        apiKeysPanel.ThemeTypeVariation = "InsetPanel";
        apiKeysPanel.VisibilityChanged += () =>
        {
            if (apiKeysPanel.IsVisibleInTree()) return;
            apiKeyInput.Text = string.Empty;
            apiKeysRead++;
        };
        gameSettingsContent.AddChild(apiKeysPanel);
    }

    private async Task RefreshApiKeysAsync()
    {
        RefreshControlAvailability();
        var read = ++apiKeysRead;
        var owner = registration;
        savedApiKeys.Text = string.Empty;
        savedApiKeys.Hide();
        if (!TryGetOwner(out var authority, out var deviceId, out var signer))
        {
            apiKeysStatus.Text = "Connect and pair this device to save keys.";
            return;
        }
        apiKeysStatus.Text = "Checking saved keys…";
        try
        {
            var status = await ownerApi.GetProviderStatusAsync(
                ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None);
            if (read != apiKeysRead || !ReferenceEquals(owner, registration) || !apiKeysPanel.IsVisibleInTree()) return;
            providerConfiguration = status;
            RenderSavedApiKeys();
            apiKeysStatus.Text = "Keys stay private on the host.";
        }
        catch (Exception exception)
        {
            if (read == apiKeysRead && ReferenceEquals(owner, registration) && apiKeysPanel.IsVisibleInTree())
                apiKeysStatus.Text = "Could not check saved keys: " + FriendlyFailure(exception);
        }
    }

    private void RenderSavedApiKeys()
    {
        savedApiKeys.Text = providerConfiguration?.CredentialSlots is { Count: > 0 } slots
            ? "Saved keys:\n" + string.Join('\n', slots.Select(slot => $"{ProviderDisplayName(slot.Provider)} · {slot.Label}"))
            : "No named keys saved yet.";
        savedApiKeys.Show();
    }

    private async Task SaveApiKeyAsync()
    {
        if (isOwnerAction || !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var label = apiKeyLabelInput.Text.Trim();
        if (label.Length is < 1 or > 64 || label.Any(char.IsControl) || string.IsNullOrWhiteSpace(apiKeyInput.Text))
        {
            apiKeysStatus.Text = "Give the key a name of 64 characters or fewer and paste the key.";
            return;
        }
        apiKeysRead++;
        var action = new OwnerCredentialSlotCreationAction(Guid.NewGuid().ToString("N"),
            apiKeyProviderChoice.GetItemMetadata(apiKeyProviderChoice.Selected).AsString(), label, apiKeyInput.Text);
        apiKeyInput.Text = string.Empty;
        apiKeysStatus.Text = "Saving key…";
        var saved = false;
        await RunOwnerActionAsync(async () =>
        {
            providerConfiguration = await ownerApi.CreateCredentialSlotAsync(
                ResolveWorldUri(), authority, deviceId, action, signer, CancellationToken.None);
            saved = true;
            RenderSavedApiKeys();
            apiKeyLabelInput.Text = string.Empty;
            apiKeysStatus.Text = $"Saved {label}. Choose it when adding an agent.";
            PopulateFounderCredentials();
            PopulateCredentialChoices();
            return "API key saved on the host";
        });
        if (!saved) apiKeysStatus.Text = "The key was not confirmed saved. Check the host connection and try again.";
    }
}
