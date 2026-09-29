using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private readonly Button founderSetupButton = new();
    private readonly Button townSiteButton = new();
    private readonly Button moveFounderButton = new();
    private readonly Button undoFounderButton = new();
    private readonly Button addAgentButton = new();
    private readonly Button startWorldButton = new();
    private readonly PanelContainer founderSetupPanel = new();
    private readonly Label founderSetupHint = new();
    private readonly OptionButton founderProviderChoice = new();
    private readonly OptionButton founderCredentialChoice = new();
    private readonly LineEdit founderModelInput = new();
    private readonly LineEdit founderKeyLabelInput = new();
    private readonly LineEdit founderApiKeyInput = new();
    private bool placingAddedAgent;
    private bool choosingFirstTownSite;
    private string? movingFounderId;

    private void ToggleMoveFounder()
    {
        if (movingFounderId is not null)
        {
            movingFounderId = null;
            moveFounderButton.Text = "Move founder";
            SetStatus("Founder move cancelled", good: true);
            return;
        }
        if (observationSession.Current?.Baseline.Snapshot is not
            { FounderSetup: { Started: false, Placed: > 0 } } snapshot ||
            selectedInhabitantId is not { } founderId ||
            !founderId.StartsWith("founder:", StringComparison.Ordinal) ||
            !snapshot.Inhabitants.Any(person => person.Id == founderId && person.Lifecycle == "active"))
            return;
        movingFounderId = founderId;
        moveFounderButton.Text = "Cancel move";
        founderApiKeyInput.Text = string.Empty;
        founderSetupPanel.Hide();
        SetStatus("Click an empty passable tile to move the selected founder before Start World.", good: true,
            StatusToastKind.Sticky);
    }

    private async Task MoveFounderAtAsync(Vector2I tile)
    {
        if (movingFounderId is not { } founderId || isOwnerAction ||
            observationSession.Current?.Baseline.Snapshot is not { FounderSetup: { Started: false } } snapshot ||
            !MapContains(snapshot, tile.X, tile.Y) ||
            snapshot.Inhabitants.Any(person => person.Id != founderId &&
                person.Position.X == tile.X && person.Position.Y == tile.Y) ||
            snapshot.Objects.Any(item => item.Position.X == tile.X && item.Position.Y == tile.Y) ||
            snapshot.Resources.Any(item => item.Position.X == tile.X && item.Position.Y == tile.Y))
        {
            SetStatus("Pick an empty spot you can walk to.", good: false);
            return;
        }
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            var result = await ownerApi.MoveFounderAsync(ResolveWorldUri(), authority, deviceId,
                new OwnerFounderMoveAction(founderId, tile.X, tile.Y), signer, CancellationToken.None);
            movingFounderId = null;
            moveFounderButton.Text = "Move founder";
            return result.Changed ? $"Founder moved to {result.X}, {result.Y}" : "The founder is already there";
        });
    }

    private async Task UndoLastFounderAsync()
    {
        if (isOwnerAction || observationSession.Current?.Baseline.Snapshot is not
            { FounderSetup: { Started: false, Placed: > 0 } } snapshot ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var founderId = snapshot.FounderSetup.LastFounderId;
        if (founderId is null) return;
        await RunOwnerActionAsync(async () =>
        {
            var result = await ownerApi.UndoFounderAsync(ResolveWorldUri(), authority, deviceId,
                new OwnerFounderUndoAction(founderId), signer, CancellationToken.None);
            if (selectedInhabitantId == founderId) selectedInhabitantId = null;
            movingFounderId = null;
            founderApiKeyInput.Text = string.Empty;
            providerConfiguration = await ownerApi.GetProviderStatusAsync(
                ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None);
            PopulateFounderCredentials();
            return $"Last founder removed · {result.Placed}/{result.Required} placed. Add another when you're ready.";
        });
    }

    private void ToggleFirstTownSite()
    {
        if (choosingFirstTownSite)
        {
            CancelFirstTownSiteSelection();
            return;
        }
        if (observationSession.Current?.Baseline.Snapshot.FounderSetup is not
            { CanChooseTownSite: true }) return;
        choosingFirstTownSite = true;
        townSiteButton.Text = TownSiteButtonText(observationSession.Current.Baseline.Snapshot.FounderSetup);
        founderApiKeyInput.Text = string.Empty;
        founderSetupPanel.Hide();
        SetStatus("Click buildable land to generate the first Town. Choose again to redo before placing founders.", good: true,
            StatusToastKind.Sticky);
    }

    private void CancelFirstTownSiteSelection()
    {
        choosingFirstTownSite = false;
        townSiteButton.Text = TownSiteButtonText(observationSession.Current?.Baseline.Snapshot.FounderSetup);
        SetStatus("Town-site selection closed", good: true);
    }

    private async Task AcceptFirstTownSiteAtAsync(Vector2I tile)
    {
        if (!choosingFirstTownSite || isOwnerAction ||
            observationSession.Current?.Baseline.Snapshot is not { FounderSetup: { CanChooseTownSite: true } } snapshot ||
            !MapContains(snapshot, tile.X, tile.Y) ||
            !TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            var result = await ownerApi.AcceptFirstTownLayoutAsync(ResolveWorldUri(), authority, deviceId,
                new OwnerFirstTownLayoutAction(tile.X, tile.Y), signer, CancellationToken.None);
            choosingFirstTownSite = false;
            return $"Your Town is set: {result.Buildings} buildings and {result.RoadTiles} road tiles near {result.X}, {result.Y}. Add founders, or pick a different spot.";
        });
    }

    private void BuildFounderSetupPanel(Control canvas)
    {
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 7);
        founderSetupHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        founderSetupHint.CustomMinimumSize = new Vector2(320, 0);
        body.AddChild(founderSetupHint);

        founderProviderChoice.AddItem("OpenAI");
        founderProviderChoice.SetItemMetadata(0, "openai");
        founderProviderChoice.AddItem("Ollama Cloud");
        founderProviderChoice.SetItemMetadata(1, "ollama-cloud");
        founderProviderChoice.ItemSelected += _ =>
        {
            founderModelInput.Text = DefaultProviderModel(SelectedFounderProvider());
            PopulateFounderCredentials();
        };
        body.AddChild(founderProviderChoice);

        founderModelInput.PlaceholderText = "Model name for this agent";
        founderModelInput.Text = DefaultProviderModel("openai");
        body.AddChild(founderModelInput);

        founderCredentialChoice.ItemSelected += _ => RenderFounderCredentialInputs();
        body.AddChild(founderCredentialChoice);
        founderKeyLabelInput.PlaceholderText = "Name this key (for example, Personal account)";
        body.AddChild(founderKeyLabelInput);
        founderApiKeyInput.Secret = true;
        founderApiKeyInput.PlaceholderText = "Paste API key";
        body.AddChild(founderApiKeyInput);

        var close = new Button { Text = "Close" };
        StyleButton(close);
        close.Pressed += () =>
        {
            founderApiKeyInput.Text = string.Empty;
            founderSetupPanel.Hide();
            placingAddedAgent = false;
        };
        body.AddChild(close);
        AddPanelContents(founderSetupPanel, "Add an agent", body);
        founderSetupPanel.Position = new Vector2(350, 14);
        founderSetupPanel.ZIndex = 90;
        founderSetupPanel.Hide();
        canvas.AddChild(founderSetupPanel);
        PopulateFounderCredentials();
    }

    private string SelectedFounderProvider() => founderProviderChoice.GetItemMetadata(founderProviderChoice.Selected).AsString();

    private string SelectedFounderCredential() => founderCredentialChoice.GetItemMetadata(founderCredentialChoice.Selected).AsString();

    private void PopulateFounderCredentials()
    {
        founderCredentialChoice.Clear();
        founderCredentialChoice.AddItem("Default key for this provider");
        founderCredentialChoice.SetItemMetadata(0, "default");
        foreach (var slot in providerConfiguration?.CredentialSlots ?? [])
        {
            if (slot.Provider != SelectedFounderProvider()) continue;
            founderCredentialChoice.AddItem(slot.Label);
            founderCredentialChoice.SetItemMetadata(founderCredentialChoice.ItemCount - 1, slot.Id);
        }
        founderCredentialChoice.AddItem("Add a new API key…");
        founderCredentialChoice.SetItemMetadata(founderCredentialChoice.ItemCount - 1, "new");
        var defaultAvailable = providerConfiguration?.Providers.Any(option =>
            option.Provider == SelectedFounderProvider() && option.HasCredential) == true;
        founderCredentialChoice.Select(founderCredentialChoice.ItemCount > 2 ? 1 : defaultAvailable ? 0 : founderCredentialChoice.ItemCount - 1);
        RenderFounderCredentialInputs();
    }

    private void RenderFounderCredentialInputs()
    {
        var newKey = SelectedFounderCredential() == "new";
        founderKeyLabelInput.Visible = newKey;
        founderApiKeyInput.Visible = newKey;
        if (!newKey)
        {
            founderKeyLabelInput.Text = string.Empty;
            founderApiKeyInput.Text = string.Empty;
        }
    }

    private async Task ToggleFounderSetupAsync()
    {
        choosingFirstTownSite = false;
        townSiteButton.Text = TownSiteButtonText(observationSession.Current?.Baseline.Snapshot.FounderSetup);
        movingFounderId = null;
        moveFounderButton.Text = "Move founder";
        placingAddedAgent = false;
        if (founderSetupPanel.Visible)
        {
            founderApiKeyInput.Text = string.Empty;
            founderSetupPanel.Hide();
            return;
        }
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            providerConfiguration = await ownerApi.GetProviderStatusAsync(
                ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None);
            PopulateFounderCredentials();
            founderSetupPanel.Show();
            return "Pick a model and key, then click an empty spot to place the founder.";
        });
    }

    private async Task ToggleAddAgentAsync()
    {
        if (founderSetupPanel.Visible && placingAddedAgent)
        {
            founderApiKeyInput.Text = string.Empty;
            founderSetupPanel.Hide();
            placingAddedAgent = false;
            return;
        }
        if (observationSession.Current?.Baseline.Snapshot is not { FounderSetup: { Started: true } }) return;
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            providerConfiguration = await ownerApi.GetProviderStatusAsync(
                ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None);
            PopulateFounderCredentials();
            placingAddedAgent = true;
            townBorderFilter.ButtonPressed = true;
            householdPropertyFilter.ButtonPressed = true;
            ResetAddAgentPlacementHint();
            founderSetupPanel.Show();
            return "Click empty land or a House to place the new agent.";
        });
    }

    private async Task PlaceAgentAtAsync(Vector2I tile)
    {
        if (isOwnerAction || observationSession.Current?.Baseline.Snapshot is not { FounderSetup: { Started: true } } snapshot ||
            !MapContains(snapshot, tile.X, tile.Y) ||
            (snapshot.Inhabitants.Any(item => item.Lifecycle == "active" &&
                item.Position.X == tile.X && item.Position.Y == tile.Y) &&
                !IsHouseAt(snapshot, tile)) ||
            snapshot.Objects.Any(item => item.Position.X == tile.X && item.Position.Y == tile.Y) ||
            snapshot.Resources.Any(item => item.Position.X == tile.X && item.Position.Y == tile.Y))
        {
            SetStatus("Click empty land or a House.", good: false);
            return;
        }
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var provider = SelectedFounderProvider();
        var model = founderModelInput.Text.Trim();
        var choice = SelectedFounderCredential();
        var newKey = choice == "new";
        if (model.Length == 0)
        {
            SetStatus("Pick a model first.", good: false);
            return;
        }
        if (newKey && (string.IsNullOrWhiteSpace(founderKeyLabelInput.Text) ||
            string.IsNullOrWhiteSpace(founderApiKeyInput.Text)))
        {
            SetStatus("Give the new key a name and paste the key.", good: false);
            return;
        }
        var agentId = "agent:" + Guid.NewGuid().ToString("N");
        var cognition = new OwnerProviderConfigurationAction(
            "personal", provider, model, newKey ? founderApiKeyInput.Text : null,
            ForgetCredential: false, InhabitantId: agentId,
            CredentialSlotId: newKey ? Guid.NewGuid().ToString("N") : choice == "default" ? null : choice,
            NewCredentialLabel: newKey ? founderKeyLabelInput.Text.Trim() : null);
        try
        {
            await RunOwnerActionAsync(async () =>
            {
                var receipt = await ownerApi.PlaceAgentAsync(ResolveWorldUri(), authority, deviceId,
                    new OwnerAgentPlacementAction(agentId, tile.X, tile.Y, cognition), signer, CancellationToken.None);
                providerConfiguration = await ownerApi.GetProviderStatusAsync(
                    ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None);
                placingAddedAgent = false;
                founderSetupPanel.Hide();
                if (receipt.HouseholdId is null)
                {
                    var townName = snapshot.Towns.FirstOrDefault(item =>
                        item.BorderTiles.Any(point => point.X == tile.X && point.Y == tile.Y))?.Name ?? "a Town";
                    return $"Agent joined {townName} without a household";
                }
                var newHousehold = receipt.HouseholdId == "household:" + agentId;
                return newHousehold ? "Agent placed in a new independent household"
                    : $"Agent joined {GameUiText.PartyName(snapshot, receipt.HouseholdId)}";
            });
        }
        finally
        {
            founderApiKeyInput.Text = string.Empty;
            founderKeyLabelInput.Text = string.Empty;
        }
    }

    private async Task PlaceFounderAtAsync(Vector2I tile)
    {
        if (isOwnerAction || observationSession.Current?.Baseline.Snapshot is not { FounderSetup: { Started: false } } snapshot ||
            !MapContains(snapshot, tile.X, tile.Y) ||
            snapshot.Inhabitants.Any(item => item.Position.X == tile.X && item.Position.Y == tile.Y) ||
            snapshot.Objects.Any(item => item.Position.X == tile.X && item.Position.Y == tile.Y) ||
            snapshot.Resources.Any(item => item.Position.X == tile.X && item.Position.Y == tile.Y))
        {
            SetStatus("Click empty land near the Town.", good: false);
            return;
        }
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        var provider = SelectedFounderProvider();
        var model = founderModelInput.Text.Trim();
        var choice = SelectedFounderCredential();
        var newKey = choice == "new";
        if (model.Length == 0)
        {
            SetStatus("Pick a model first.", good: false);
            return;
        }
        if (newKey && (string.IsNullOrWhiteSpace(founderKeyLabelInput.Text) ||
            string.IsNullOrWhiteSpace(founderApiKeyInput.Text)))
        {
            SetStatus("Give the new key a name and paste the key.", good: false);
            return;
        }
        var founderId = "founder:" + Guid.NewGuid().ToString("N");
        var cognition = new OwnerProviderConfigurationAction(
            "personal", provider, model, newKey ? founderApiKeyInput.Text : null,
            ForgetCredential: false, InhabitantId: founderId,
            CredentialSlotId: newKey ? Guid.NewGuid().ToString("N") : choice == "default" ? null : choice,
            NewCredentialLabel: newKey ? founderKeyLabelInput.Text.Trim() : null);
        try
        {
            await RunOwnerActionAsync(async () =>
            {
                var receipt = await ownerApi.PlaceFounderAsync(ResolveWorldUri(), authority, deviceId,
                    new OwnerFounderPlacementAction(founderId, tile.X, tile.Y, cognition), signer, CancellationToken.None);
                providerConfiguration = await ownerApi.GetProviderStatusAsync(
                    ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None);
                PopulateFounderCredentials();
                return $"Founder {receipt.Placed}/{receipt.Required} placed · joins {GameUiText.PartyName(snapshot, receipt.HouseholdId)}";
            });
        }
        finally
        {
            founderApiKeyInput.Text = string.Empty;
            founderKeyLabelInput.Text = string.Empty;
        }
    }

    private async Task StartFounderWorldAsync()
    {
        if (!TryGetOwner(out var authority, out var deviceId, out var signer)) return;
        await RunOwnerActionAsync(async () =>
        {
            _ = await ownerApi.StartWorldAsync(ResolveWorldUri(), authority, deviceId, signer, CancellationToken.None);
            founderSetupPanel.Hide();
            return "World started";
        });
    }

    private void RenderFounderSetup(OwnerWorldSnapshot snapshot)
    {
        var setup = snapshot.FounderSetup;
        townSiteButton.Visible = setup is { CanChooseTownSite: true };
        moveFounderButton.Visible = setup is { Started: false, Placed: > 0 };
        undoFounderButton.Visible = setup is { Started: false, Placed: > 0 };
        moveFounderButton.Text = movingFounderId is null ? "Move founder" : "Cancel move";
        founderSetupButton.Visible = setup is { Started: false };
        startWorldButton.Visible = setup is { Started: false };
        founderHudRow.Visible = setup is { Started: false };
        addAgentButton.Visible = setup is { Started: true };
        if (setup is not { Started: false })
        {
            choosingFirstTownSite = false;
            movingFounderId = null;
            if (!placingAddedAgent) founderSetupPanel.Hide();
            return;
        }
        if (!setup.CanChooseTownSite) choosingFirstTownSite = false;
        townSiteButton.Text = TownSiteButtonText(setup);
        if (movingFounderId is not null && !snapshot.Inhabitants.Any(person => person.Id == movingFounderId))
        {
            movingFounderId = null;
            moveFounderButton.Text = "Move founder";
        }
        founderSetupButton.Text = $"Add founders {setup.Placed}/{setup.Required}";
        founderSetupHint.Text = setup.Placed < setup.Required
            ? $"Choose this founder’s model and key, then place them near your Town. The first two share one household; the next two share another. {setup.Placed}/{setup.Required} placed."
            : "All four founders are placed. Move or undo one if you need to, then choose Start World to let time run.";
    }

    // Town-site selection is a map-click mode; the button shows how to leave it.
    private string TownSiteButtonText(OwnerFounderSetup? setup) =>
        choosingFirstTownSite ? "Cancel Town site" :
        setup?.HasAcceptedTownSite == true ? "Redo Town site" : "Choose Town site";

    private void ResetAddAgentPlacementHint()
    {
        founderSetupHint.Text = "Pick a provider, model and key for this adult, then point at a tile. On a household's property they join that household. On other Town land they join the Town only. Outside the Town they start their own household. House tiles can be shared.";
    }

    private void PreviewAddAgentPlacement(OwnerWorldSnapshot snapshot, Vector2I tile)
    {
        if (!placingAddedAgent || !founderSetupPanel.Visible) return;
        if (!MapContains(snapshot, tile.X, tile.Y) ||
            (snapshot.Inhabitants.Any(item => item.Lifecycle == "active" &&
                item.Position.X == tile.X && item.Position.Y == tile.Y) &&
                !IsHouseAt(snapshot, tile)) ||
            snapshot.Objects.Any(item => item.Position.X == tile.X && item.Position.Y == tile.Y) ||
            snapshot.Resources.Any(item => item.Position.X == tile.X && item.Position.Y == tile.Y))
        {
            founderSetupHint.Text = "Point at empty land or a House to see where this adult would belong.";
            return;
        }
        var ownerId = snapshot.PlacedBuildings.FirstOrDefault(item => item.HouseholdId is not null &&
            tile.X >= item.Position.X && tile.X < item.Position.X + item.Width &&
            tile.Y >= item.Position.Y && tile.Y < item.Position.Y + item.Height)?.HouseholdId;
        var town = snapshot.Towns.FirstOrDefault(item =>
            item.BorderTiles.Any(point => point.X == tile.X && point.Y == tile.Y));
        var home = ownerId is not null
            ? GameUiText.PartyName(snapshot, ownerId)
            : town is null ? "new independent household" : "none";
        founderSetupHint.Text = $"Tile {tile.X}, {tile.Y} · Household: {home} · Town: {town?.Name ?? "no Town"}. " +
            "Placement requires a passable tile, no conflicting occupant outside a House, and server validation.";
    }

    private static bool IsHouseAt(OwnerWorldSnapshot snapshot, Vector2I tile) =>
        snapshot.PlacedBuildings.Any(item => item.Tags?.Contains("house", StringComparer.Ordinal) == true &&
            tile.X >= item.Position.X && tile.X < item.Position.X + item.Width &&
            tile.Y >= item.Position.Y && tile.Y < item.Position.Y + item.Height);
}
