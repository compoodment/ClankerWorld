using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private static readonly System.Text.Json.JsonSerializerOptions CompatibilitySmokeJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private async Task VerifyNewWorldCompatibilityMessageAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        var origin = $"http://127.0.0.1:{port}/";
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(origin);
        listener.Start();
        try
        {
            var authority = new OwnerAuthorityIdentity("compatibility-smoke", "compatibility-world");
            registration = new(authority, "compatibility-device", signer.PublicKeyFingerprint, origin);
            deviceKey = signer;
            worldUrlInput.Text = origin;
            worldNameInput.Text = "Disposable";
            worldSeedInput.Text = "compatibility-smoke";
            var response = Task.Run(async () =>
            {
                var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                if (context.Request.Url!.AbsolutePath != OwnerPairingEndpoints.ChallengeIssue)
                    throw new InvalidOperationException("New World must check an authenticated challenge first.");
                context.Response.ContentType = "application/json";
                await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.OutputStream,
                    new OwnerChallenge(authority, registration.DeviceId, "challenge-smoke", "nonce-smoke",
                        DateTimeOffset.UtcNow.AddMinutes(1)), CompatibilitySmokeJsonOptions);
                context.Response.Close();
            });
            await PreviewWorldAsync();
            await response;
            if (!worldPreviewStatus.Text.Contains("matching updates", StringComparison.Ordinal) ||
                !worldPreviewStatus.Text.Contains("pairing can stay", StringComparison.Ordinal) ||
                worldPreviewStatus.Text.Contains("not allowed", StringComparison.Ordinal) ||
                !worldCreateButton.Disabled || worldPreviewButton.Disabled || registration is null)
                throw new InvalidOperationException("New World must show the update remedy, keep pairing and leave Create unavailable without a preview.");
        }
        finally
        {
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            listener.Stop();
        }
    }

    private async Task VerifyUiScaleAt1440pAsync(Window displayWindow)
    {
        var originalWindowSize = displayWindow.Size;
        var originalRenderSize = displayWindow.ContentScaleSize;
        var originalScaleMode = displayWindow.ContentScaleMode;
        var originalScaleAspect = displayWindow.ContentScaleAspect;
        OpenMainMenuSettings();
        try
        {
            displayWindow.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            displayWindow.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            displayWindow.Size = new Vector2I(2560, 1440);
            displayWindow.ContentScaleSize = new Vector2I(2560, 1440);
            for (var frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // The interface follows the screen: 1440p picks 200% with no setting to change.
            if (uiLayer.Factor != 2 || menuLayer.Factor != 2)
                throw new InvalidOperationException($"A 1440p screen must show the interface at 200%: {uiLayer.Factor}.");
            SetUiFactor(1);

            var smokeMap = new OwnerWorldSnapshot("ui-scale-smoke", 0, "ui-scale-map",
                Enumerable.Range(0, 16).Select(index => new OwnerWorldTile(index % 4, index / 4, "meadow")).ToArray(),
                [], [], null, 0)
            {
                PackedTerrain = new OwnerWorldPackedTerrain(4, 4, "terrain-kind-v1",
                    Convert.ToBase64String(new byte[16])),
            };
            RenderMap(smokeMap);
            for (var frame = 0; frame < 2; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            VerifyPixelText("at 100%");
            var nativeRenderSize = displayWindow.ContentScaleSize;
            var baseClockHeight = clockLabel.GetGlobalRect().Size.Y;
            var baseChoiceHeight = themeChoice.GetGlobalRect().Size.Y;
            var baseSettingsWidth = gameMenuPanel.GetGlobalRect().Size.X;
            var mapStageScale = mapStage.Scale;
            if (uiLayer.Factor != 1 || menuLayer.Factor != 1 || baseClockHeight < 1 || baseChoiceHeight < 1 || baseSettingsWidth < 1)
                throw new InvalidOperationException("The interface size smoke check could not read the interface at 100%.");
            if (TileAtCanvas(mapStage.Position + new Vector2(currentTileSize * 1.5f, currentTileSize * 1.5f), smokeMap) != new Vector2I(1, 1))
                throw new InvalidOperationException("1440p interface size map input smoke check could not resolve its reference tile.");

            ApplyUiScale();
            RenderMap(smokeMap);
            for (var frame = 0; frame < 2; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            const int factor = 2;
            VerifyPixelText("at 200%");
            // Text, controls and panels grow together instead of text alone.
            if (!Mathf.IsEqualApprox(clockLabel.GetGlobalRect().Size.Y, baseClockHeight * factor) ||
                !Mathf.IsEqualApprox(themeChoice.GetGlobalRect().Size.Y, baseChoiceHeight * factor) ||
                !Mathf.IsEqualApprox(gameMenuPanel.GetGlobalRect().Size.X, baseSettingsWidth * factor))
                throw new InvalidOperationException("200% must enlarge text, controls and panels by the same 2×.");
            // Dialogs are separate windows: their contents, frame and title scale on their own.
            var titleSize = quitGameConfirmation.GetThemeFontSize("title_font_size");
            if (!Mathf.IsEqualApprox(quitGameConfirmation.ContentScaleFactor, factor) ||
                !Mathf.IsEqualApprox(deletionConfirmation.ContentScaleFactor, factor) ||
                !Mathf.IsEqualApprox(buildingRemoveConfirmation.ContentScaleFactor, factor) ||
                buildingRemoveConfirmation.OkButtonText != "Remove building" ||
                deletionConfirmation.GetThemeFontSize("title_font_size") != UiFonts.Heading * factor ||
                DialogSize(FitDialog(quitGameConfirmation)) != FitDialog(quitGameConfirmation) * factor || titleSize != UiFonts.Heading * factor ||
                quitGameConfirmation.GetThemeConstant("title_height") != 30 * factor ||
                !Mathf.IsEqualApprox(themeChoice.GetPopup().ContentScaleFactor, factor) ||
                UiTheme.Theme.GetFontSize("font_size", "TooltipLabel") != UiFonts.Body * factor ||
                AgentMarker.TextScale != factor)
                throw new InvalidOperationException($"Dialogs, drop-down lists, tooltips and map names must grow with the interface: title {titleSize}.");
            if (displayWindow.Size != new Vector2I(2560, 1440) ||
                displayWindow.ContentScaleSize != nativeRenderSize)
                throw new InvalidOperationException("The interface size changed the 1440p window or native render size.");
            if (mapStage.Scale != mapStageScale ||
                TileAtCanvas(mapStage.Position + new Vector2(currentTileSize * 1.5f, currentTileSize * 1.5f), smokeMap) != new Vector2I(1, 1))
                throw new InvalidOperationException("The interface size changed the terrain transform or map interaction coordinates.");
            var settingsViewport = settingsScroll.GetGlobalRect();
            var choiceBounds = themeChoice.GetGlobalRect();
            if (!mainMenuOverlay.GetGlobalRect().Encloses(gameMenuPanel.GetGlobalRect()) ||
                choiceBounds.Position.X < settingsViewport.Position.X - 1 ||
                choiceBounds.End.X > settingsViewport.End.X + 1)
                throw new InvalidOperationException("Game Settings escaped its usable bounds at 1440p and 200%.");
            await VerifyAgentConversationReaderAt200PercentAsync();

            var emptyLayer = Convert.ToBase64String(new byte[16]);
            var fieldMap = smokeMap with
            {
                PackedMapLayers = new OwnerWorldPackedMapLayers(4, 4, "map-layers-v2",
                    emptyLayer, emptyLayer, emptyLayer, emptyLayer, emptyLayer)
                {
                    Fertility = Convert.ToBase64String(Enumerable.Repeat((byte)67, 16).ToArray()),
                },
                Fields = [new(new(1, 1), "household:field-smoke", "growing", "cultivated_greens", 67, null, null)],
                GroundStocks = [new(new(1, 1), "household:field-smoke", "cultivated_greens", 3)],
                Stockpiles = [new("household:field-smoke", "Farm household", [])],
            };
            RenderMap(fieldMap);
            householdPropertyFilter.ButtonPressed = true;
            selectedTile = new(1, 1);
            selectedTilePanel.Show();
            RenderTileInspection(fieldMap);
            for (var frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!selectedTileText.Text.Contains("Soil fertility: Good", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Field: Growing", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Used by: Farm household", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("3 Cultivated greens", StringComparison.OrdinalIgnoreCase) ||
                terrainLayer.HouseholdPropertyTileCount != 1 ||
                !mapCanvas.GetGlobalRect().Encloses(selectedTilePanel.GetGlobalRect()) ||
                !TileCardText().Contains("Fertility\nGood", StringComparison.Ordinal) ||
                !TileCardText().Contains("Field · growing cultivated greens", StringComparison.Ordinal) ||
                !TileCardText().Contains("Household\nFarm household", StringComparison.Ordinal) ||
                !TileCardText().Contains("3 cultivated greens", StringComparison.Ordinal))
                throw new InvalidOperationException("Field ownership, crop and soil inspection must show on the tile card and fit at 200% interface size: " + TileCardText());
            householdPropertyFilter.ButtonPressed = false;
            selectedTile = null;
            selectedTilePanel.Hide();
            terrainLayer.SetSelectedTile(null);
            RenderMap(smokeMap);

            var generatedMap = smokeMap with
            {
                WorldId = "zoom-bounds-smoke",
                PackedTerrain = new OwnerWorldPackedTerrain(256, 128, "terrain-kind-v1",
                    Convert.ToBase64String(new byte[256 * 128])),
            };
            cameraZoom = 0.65f;
            RenderMap(generatedMap);
            if (mapStage.Size.Y * 0.7f < mapCanvas.Size.Y - 1)
                throw new InvalidOperationException("Small-map zoom-out exposed the north/south map edge at 1440p.");
            cameraZoom = maximumCameraZoom;
            RenderMap(generatedMap);
            var highResolutionVisibleRows = mapCanvas.Size.Y / currentTileSize;
            displayWindow.Size = new Vector2I(1280, 720);
            displayWindow.ContentScaleSize = new Vector2I(1280, 720);
            for (var frame = 0; frame < 2; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (uiLayer.Factor != 1)
                throw new InvalidOperationException($"A 720p window must show the interface at 100%: {uiLayer.Factor}.");
            cameraZoom = maximumCameraZoom;
            RenderMap(generatedMap);
            var lowResolutionVisibleRows = mapCanvas.Size.Y / currentTileSize;
            if (Math.Abs(highResolutionVisibleRows - lowResolutionVisibleRows) > 2)
                throw new InvalidOperationException($"Maximum zoom-in showed different world heights at 1440p and 720p: {highResolutionVisibleRows:0.0} versus {lowResolutionVisibleRows:0.0} rows.");
        }
        finally
        {
            displayWindow.Size = originalWindowSize;
            displayWindow.ContentScaleMode = originalScaleMode;
            displayWindow.ContentScaleAspect = originalScaleAspect;
            displayWindow.ContentScaleSize = originalRenderSize;
            ApplyUiScale();
        }
    }

    private async Task VerifyAgentConversationReaderAt200PercentAsync()
    {
        if (uiLayer.Factor != 2)
            throw new InvalidOperationException("Conversation history smoke must run at 200% UI Scale.");

        var menuVisible = mainMenuOverlay.Visible;
        var gameMenuVisible = gameMenuPanel.Visible;
        var menuShadeVisible = menuShade.Visible;
        var oldSelection = selectedInhabitantId;
        var oldProfileRequested = agentProfileRequested;
        var oldProfileVisible = agentProfilePanel.Visible;
        var oldQuickCardVisible = selectedInhabitantCard.Visible;
        var wasInWorld = isInWorld;
        isInWorld = true;
        mainMenuOverlay.Hide();
        gameMenuPanel.Hide();
        menuShade.Hide();
        try
        {
            const string firstAgentId = "conversation-ui-a";
            const string secondAgentId = "conversation-ui-b";
            const string thirdAgentId = "conversation-ui-c";
            const string fourthAgentId = "conversation-ui-d";
            var firstPosition = new OwnerWorldPosition(1, 1);
            var secondPosition = new OwnerWorldPosition(2, 1);
            var thirdPosition = new OwnerWorldPosition(1, 3);
            var fourthPosition = new OwnerWorldPosition(2, 3);
            OwnerWorldInhabitant Agent(string id, string name, OwnerWorldPosition position) => new(
                id, name, "active", position, 8_000, [], [], new OwnerWorldRoute("idle", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(position, [position], [position]), false);
            var publicText = string.Join(' ', Enumerable.Repeat("A public sentence with enough words to wrap comfortably on a large screen.", 6));
            var closedTurns = Enumerable.Range(0, 7).Select(index =>
            {
                var speaker = index == 6 || index % 2 == 0 ? firstAgentId : secondAgentId;
                var listener = speaker == firstAgentId ? secondAgentId : firstAgentId;
                return new OwnerWorldConversationTurn(
                    $"conversation:ui-closed:turn:{index + 1}", speaker,
                    speaker == firstAgentId ? "Aster" : "Rowan", publicText, index + 1, [listener], index == 6);
            }).ToArray();
            var conversations = new OwnerWorldConversation[]
            {
                new("conversation:ui-closed", firstAgentId, "Aster", secondAgentId, "Rowan",
                    "closed", null, "agreed", 0, 8, closedTurns),
                new("conversation:ui-interrupted", thirdAgentId, "Mira", fourthAgentId, "Ilya",
                    "suspended", "owner_paused", null, 0, 7,
                    [new("conversation:ui-interrupted:turn:1", thirdAgentId, "Mira", publicText, 5,
                        [fourthAgentId], false)]),
            };
            var conversationMap = new OwnerWorldSnapshot("conversation-ui-smoke", 8, "conversation-ui-map",
                Enumerable.Range(0, 36).Select(index => new OwnerWorldTile(index % 6, index / 6, "meadow")).ToArray(),
                [], [], null, 0)
            {
                PackedTerrain = new OwnerWorldPackedTerrain(6, 6, "terrain-kind-v1",
                    Convert.ToBase64String(new byte[36])),
                Inhabitants = [
                    Agent(firstAgentId, "Aster", firstPosition), Agent(secondAgentId, "Rowan", secondPosition),
                    Agent(thirdAgentId, "Mira", thirdPosition), Agent(fourthAgentId, "Ilya", fourthPosition)],
                Conversations = conversations,
            };
            RenderMap(conversationMap);
            selectedInhabitantId = secondAgentId;
            RenderSelectedInhabitantCard(conversationMap);
            OpenAgentProfile(speak: false);
            for (var frame = 0; frame < 2; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var marker = inhabitantVisuals[firstAgentId];
            if (!marker.ConversationBadgeVisible || !marker.ConversationUnread || marker.TooltipText.Contains(publicText, StringComparison.Ordinal))
                throw new InvalidOperationException("A nearby public conversation must show a bounded unread bubble preview without placing its full history in the tooltip.");
            marker._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = marker.ConversationBadgeBounds.GetCenter(),
            });
            for (var frame = 0; frame < 2; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!conversationPanel.Visible || !conversationReaderStatus.Text.StartsWith("Closed ·", StringComparison.Ordinal) ||
                marker.ConversationUnread || selectedInhabitantId != secondAgentId || !agentProfilePanel.Visible ||
                conversationReaderSummary.Text.Contains(publicText, StringComparison.Ordinal))
                throw new InvalidOperationException("Clicking the conversation bubble must open its closed-session summary, mark it read locally, and leave agent selection alone.");

            conversationHistoryButton.EmitSignal(BaseButton.SignalName.Pressed);
            for (var frame = 0; frame < 2; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!conversationHistoryText.Visible || !conversationHistoryText.ScrollActive ||
                !conversationHistoryText.Text.Contains(publicText, StringComparison.Ordinal) ||
                conversationHistoryText.GetContentHeight() <= conversationHistoryText.Size.Y ||
                conversationPanel.Position.X < 0 || conversationPanel.Position.Y < HudTop - 1 ||
                conversationPanel.Position.X + conversationPanel.Size.X > UiSize.X + 1 ||
                conversationPanel.Position.Y + conversationPanel.Size.Y > UiSize.Y + 1)
                throw new InvalidOperationException("Expanded long conversation history must scroll inside the 200% layout bounds.");

            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (conversationPanel.Visible || selectedInhabitantId != secondAgentId ||
                !agentProfilePanel.Visible || gameMenuPanel.Visible)
                throw new InvalidOperationException("Escape must close expanded conversation history before the selected agent's Profile, without clearing selection or opening the Pause Menu.");

            OpenConversationReader(conversationMap, thirdAgentId, "conversation:ui-interrupted");
            if (!conversationReaderStatus.Text.StartsWith("Interrupted · world paused", StringComparison.Ordinal))
                throw new InvalidOperationException("An interrupted conversation must keep its pause reason visible in the summary.");
        }
        finally
        {
            conversationPanel.Hide();
            openConversationId = null;
            openConversationAgentId = null;
            selectedInhabitantId = oldSelection;
            agentProfileRequested = oldProfileRequested;
            agentProfilePanel.Visible = oldProfileVisible;
            selectedInhabitantCard.Visible = oldQuickCardVisible;
            isInWorld = wasInWorld;
            mainMenuOverlay.Visible = menuVisible;
            gameMenuPanel.Visible = gameMenuVisible;
            menuShade.Visible = menuShadeVisible;
        }
    }

    /// <summary>Whether a button is the square icon button showing <paramref name="glyph"/>.</summary>
    private static bool ShowsGlyph(Button button, PixelGlyph glyph) =>
        button.ThemeTypeVariation == "IconButton" && button.Text.Length == 0 &&
        button.Icon == PixelIcons.Texture(glyph, Colors.White, Colors.White, 1);

    /// <summary>
    /// Every close and back control is the same small icon button, both menus
    /// use the same choice buttons, and permanent deletion is drawn in red.
    /// </summary>
    private void VerifyConsistentButtons()
    {
        var buttons = FindChildren("*", nameof(Button), recursive: true, owned: false).OfType<Button>().ToArray();
        var stray = buttons.Where(button => button.Text.Trim() is "×" or "<" or "‹" or "Back" or "Close" ||
            button.Text.StartsWith('←')).Select(button => $"{button.GetPath()} '{button.Text}'").ToArray();
        if (stray.Length > 0)
            throw new InvalidOperationException($"Close and back must use the shared icon buttons: {string.Join(", ", stray)}");
        var closes = buttons.Where(button => ShowsGlyph(button, PixelGlyph.Close) || ShowsGlyph(button, PixelGlyph.Back)).ToArray();
        var square = new Vector2(PixelIcons.Grid + 2 * UiTheme.IconButtonMargin, PixelIcons.Grid + 2 * UiTheme.IconButtonMargin);
        if (closes.Length < 8 || closes.Any(button => button.GetCombinedMinimumSize() != square ||
                button.SizeFlagsVertical != Control.SizeFlags.ShrinkCenter || button.SizeFlagsHorizontal.HasFlag(Control.SizeFlags.Expand)))
            throw new InvalidOperationException($"Close and back buttons must share one {square.X}-pixel square: {string.Join(", ", closes.Select(button => button.GetCombinedMinimumSize()))}");
        Button[] choices = [mainMenuContinueButton, mainMenuNewButton, mainMenuLoadButton, mainMenuSettingsButton, quitGameButton,
            menuResumeButton, menuSaveWorldButton, settingsButton, modLibraryButton, menuQuitToMainButton];
        if (choices.Any(button => button.GetThemeFont("font") != UiFonts.Headings || button.GetThemeFontSize("font_size") != UiFonts.Heading ||
                button.Alignment != HorizontalAlignment.Left))
            throw new InvalidOperationException("The Main Menu and Pause Menu choices must share one button style.");
        if (deletionConfirmation.GetOkButton().ThemeTypeVariation != "DangerButton" ||
            quitGameConfirmation.GetOkButton().ThemeTypeVariation != "PrimaryButton")
            throw new InvalidOperationException("Permanent deletion must be the red action; other confirmations stay green.");
    }

    /// <summary>
    /// The model picker shows the game's list in its order, marks models the
    /// key can't use, keeps an unlisted model as a typed name, and explains a
    /// key that couldn't be checked with a Retry.
    /// </summary>
    private void VerifyModelPicker()
    {
        var parent = founderCredentialChoice.GetParent();
        if (founderModelPicker.GetParent() != parent || founderModelPicker.GetIndex() < founderApiKeyInput.GetIndex() ||
            cognitionModelPicker.GetIndex() < cognitionApiKeyInput.GetIndex())
            throw new InvalidOperationException("Add agent and Model settings must ask for the key before the model it offers.");
        var picker = new ModelPicker();
        AddChild(picker);
        try
        {
            string Items() => string.Join(" | ", Enumerable.Range(0, picker.Choice.ItemCount)
                .Where(index => !picker.Choice.IsItemSeparator(index)).Select(picker.Choice.GetItemText));
            int Index(string text) => Enumerable.Range(0, picker.Choice.ItemCount).First(item => picker.Choice.GetItemText(item) == text);
            void Pick(string text)
            {
                var index = Index(text);
                picker.Choice.Select(index);
                picker.Choice.EmitSignal(OptionButton.SignalName.ItemSelected, index);
            }

            picker.SetModel("gpt-6-luna");
            var stale = picker.BeginLoading("gpt-6-luna");
            if (!Items().Contains("Loading models…", StringComparison.Ordinal) || picker.Model != "gpt-6-luna")
                throw new InvalidOperationException($"A loading model list must keep the current model: {Items()}.");
            var lookup = picker.BeginLoading("gpt-6-luna");
            if (picker.IsLatest(stale) || !picker.IsLatest(lookup))
                throw new InvalidOperationException("Only the newest model lookup may fill the picker.");
            OwnerProviderModelChoice[] listed = [new("gpt-6.1-sol", true), new("gpt-6-astra", false), new("gpt-6-sol", true), new("gpt-6-luna", true)];
            picker.ShowList(listed, "gpt-6-luna");
            var unavailable = "gpt-6-astra" + ModelPicker.UnavailableNote;
            if (Items() != $"gpt-6.1-sol | {unavailable} | gpt-6-sol | gpt-6-luna | {ModelPicker.TypeOwnText}" ||
                picker.Choice.GetItemText(picker.Choice.Selected) != "gpt-6-luna" || picker.Problem.Length > 0 ||
                !picker.Choice.IsItemDisabled(Index(unavailable)) || picker.Choice.IsItemDisabled(Index("gpt-6-sol")))
                throw new InvalidOperationException($"The model list must keep the game's order and mark models the key can't use: {Items()}.");
            OwnerProviderModelChoice[] noLuna = [new("gpt-6.1-sol", false), new("gpt-6-astra", true), new("gpt-6-sol", true), new("gpt-6-luna", false)];
            const string cantUseLuna = "This key can't use gpt-6-luna. Choose a model it can use.";
            picker.SetModel("gpt-6-luna", isNewAgent: true);
            picker.ShowList(noLuna, "gpt-6-luna");
            if (picker.Model.Length > 0 || picker.Choice.GetItemText(picker.Choice.Selected) != ModelPicker.ChooseText ||
                !picker.Choice.IsItemDisabled(Index(ModelPicker.ChooseText)) || picker.Problem != cantUseLuna || picker.CanRetry)
                throw new InvalidOperationException($"When the key can't use a new agent's model, no other model may be chosen for the owner: {Items()}.");
            Pick("gpt-6-sol");
            if (picker.Model != "gpt-6-sol" || picker.Problem.Length > 0)
                throw new InvalidOperationException("Choosing a model must clear the request to choose one.");
            picker.SetModel("gpt-6-luna");
            picker.ShowList(noLuna, "gpt-6-luna");
            if (picker.Model != "gpt-6-luna" || picker.Choice.GetItemText(picker.Choice.Selected) != "gpt-6-luna" + ModelPicker.UnavailableNote ||
                picker.Problem != cantUseLuna || picker.CanRetry)
                throw new InvalidOperationException($"An existing agent's model the key can't use must stay shown, with a request to choose another: {Items()}.");
            var fresh = new ModelPicker();
            fresh.SetModel("gpt-6-luna", isNewAgent: true);
            fresh.ShowList([new("gpt-6.1-sol", true), new("gpt-6-sol", true), new("gpt-6-luna", true)], "gpt-6-luna",
                PasteKeyNote, canRetry: false);
            fresh.ShowList([new("gpt-6.1-sol", false), new("gpt-6-sol", true), new("gpt-6-luna", false)], "gpt-6-luna");
            var (freshModel, freshProblem) = (fresh.Model, fresh.Problem);
            fresh.Free();
            if (freshModel.Length > 0 || freshProblem != cantUseLuna)
                throw new InvalidOperationException("A new agent must not start on another model when the key can't use the default.");
            picker.SetModel("my-fine-tune");
            if (!picker.TypedInput.Visible || picker.TypedInput.Text != "my-fine-tune" || picker.Model != "my-fine-tune" ||
                picker.Choice.GetItemText(picker.Choice.Selected) != ModelPicker.TypeOwnText)
                throw new InvalidOperationException("An agent's model outside the game's list must stay, shown as a typed name.");
            Pick("gpt-6-sol");
            if (picker.Model != "gpt-6-sol" || picker.TypedInput.Visible)
                throw new InvalidOperationException("Picking a listed model must choose it.");
            Pick(ModelPicker.TypeOwnText);
            if (!picker.TypedInput.Visible || picker.TypedInput.Text != "gpt-6-sol")
                throw new InvalidOperationException("Type a model name must open a text box starting from the chosen model.");
            picker.TypedInput.Text = "my-other-model";
            picker.ShowList(listed, "gpt-6-luna");
            if (picker.Model != "my-other-model" || !picker.TypedInput.Visible)
                throw new InvalidOperationException("A new list must not replace a typed model name.");
            picker.SetModel("gpt-6-luna");
            picker.ShowList(listed, "gpt-6-luna", "OpenAI refused this key.");
            if (picker.Problem != "OpenAI refused this key." || !picker.CanRetry || picker.Model != "gpt-6-luna" ||
                !Items().Contains("gpt-6.1-sol", StringComparison.Ordinal))
                throw new InvalidOperationException("A key that couldn't be checked must be explained with Retry while the list stays usable.");
            picker.ShowList(listed, "gpt-6-luna", PasteKeyNote, canRetry: false);
            if (picker.Problem != PasteKeyNote || picker.CanRetry)
                throw new InvalidOperationException("Before a new key is pasted, the list must ask for it without offering Retry.");
            picker.ShowError("Couldn't reach the game server.", "gpt-6-luna");
            if (picker.Model != "gpt-6-luna" || !Items().Contains(ModelPicker.TypeOwnText, StringComparison.Ordinal))
                throw new InvalidOperationException("Without a list from the game server, the current model and a typed name must stay usable.");
            picker.ShowTypedOnly("jev-1.13.0");
            if (picker.Choice.Visible || !picker.TypedInput.Visible || picker.Model != "jev-1.13.0")
                throw new InvalidOperationException("A provider without a model list must use a typed name.");
        }
        finally
        {
            picker.QueueFree();
        }
    }

    /// <summary>The selected child's settings show its saved choice without exposing local key data.</summary>
    private void VerifyChildModelStatus()
    {
        const string childId = "agent:ui-child";
        const string slotId = "4a6fa705672f4aa89a50ba458f6ed8b1";
        var previousReconnect = observationSession.Current;
        var previousConfiguration = providerConfiguration;
        string? restorationFailure = null;
        try
        {
            var inhabitant = new OwnerWorldInhabitant(childId, "Mira", "alive", new(0, 0), 0, [], [],
                new("none", null, null, [], "ui-test"), new(new(0, 0), [], []), false)
            {
                Relationships = [new OwnerWorldInhabitantRelationship(
                    "ui-child-parentage", "founder:parent", "biological_parentage", "accepted", "family", 0, "child")],
            };
            var snapshot = new OwnerWorldSnapshot("ui-child-world", 0, "ui-map", [new(0, 0, "meadow")], [], [],
                null, 0)
            { Inhabitants = [inhabitant] };
            var reconnect = new OwnerWorldReconnect(
                new OwnerWorldHandshake(new(1, 1),
                    ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                        "owner-control.request.v1", "paused-authoring.request.v1"], []),
                new OwnerWorldReconnectBaseline(snapshot, new OwnerWorldEventSlice(0, 0, [])));
            if (!observationSession.TryAccept(reconnect, 0, out var failure))
                throw new InvalidOperationException($"A child status smoke observation must be coherent: {failure}");

            var childConfiguration = new OwnerProviderConfigurationStatus("deterministic", "deterministic", 1,
                [new("deterministic", string.Empty, false), new("openai", "gpt-6-luna", false),
                    new("ollama-cloud", "glm-5.3-flash:cloud", false)],
                [new(childId, "routine", "openai", "gpt-6.1-sol", slotId, "initiating_parent"),
                    new(childId, "planning", "openai", "gpt-6.1-sol", slotId, "initiating_parent")], []);
            providerConfiguration = childConfiguration;
            PopulateCognitionTargets();
            cognitionTargetChoice.Select(1);
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            if (!cognitionConfigurationStatus.Text.Contains("Model needs setup", StringComparison.Ordinal) ||
                !cognitionConfigurationStatus.Text.Contains("gpt-6.1-sol", StringComparison.Ordinal) ||
                !cognitionConfigurationStatus.Text.Contains("no other model used", StringComparison.Ordinal) ||
                cognitionConfigurationStatus.Text.Contains("secret", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A child whose saved key is unavailable must see its selected model and the setup needed to use it, without key data.");

            providerConfiguration = childConfiguration with
            {
                CredentialSlots = [new(slotId, "openai", "Child model key 1")],
            };
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            if (!cognitionConfigurationStatus.Text.Contains("Chosen from the parent who began the family plan", StringComparison.Ordinal) ||
                !cognitionConfigurationStatus.Text.Contains("gpt-6.1-sol", StringComparison.Ordinal))
                throw new InvalidOperationException("A child with its saved key available must see the initiating parent's selected model.");

            providerConfiguration = childConfiguration with { Assignments = [] };
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            if (!cognitionConfigurationStatus.Text.Contains("No personal model selected for this child", StringComparison.Ordinal) ||
                !cognitionConfigurationStatus.Text.Contains("world defaults are not used", StringComparison.Ordinal))
                throw new InvalidOperationException("A child without a birth-time choice must remain explicitly unconfigured.");

            providerConfiguration = childConfiguration with
            {
                Assignments = [new(childId, "routine", "inherit"), new(childId, "planning", "inherit")],
            };
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            if (!cognitionConfigurationStatus.Text.Contains("No personal model selected for this child", StringComparison.Ordinal) ||
                !cognitionConfigurationStatus.Text.Contains("world defaults are not used", StringComparison.Ordinal))
                throw new InvalidOperationException("An explicit no-model choice must survive as a safe local child route.");

            providerConfiguration = childConfiguration with
            {
                Assignments = [new(childId, "routine", "openai", "gpt-6.1-sol", slotId), new(childId, "planning", "inherit")],
            };
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            if (!cognitionConfigurationStatus.Text.Contains("Routine: OpenAI · gpt-6.1-sol needs setup", StringComparison.Ordinal) ||
                !cognitionConfigurationStatus.Text.Contains("Planning: no personal model", StringComparison.Ordinal))
                throw new InvalidOperationException("A child with a role-specific override must see both saved routes and setup state.");

            snapshot = snapshot with
            {
                Inhabitants = [inhabitant with
                {
                    DecisionFactors = [new("birth-model-provider", "openai"), new("birth-model-id", "frozen-before-save-model")],
                }],
            };
            reconnect = reconnect with { Baseline = reconnect.Baseline with { Snapshot = snapshot } };
            observationSession.ResetAfterLoad();
            if (!observationSession.TryAccept(reconnect, 0, out failure))
                throw new InvalidOperationException($"A pending birth model observation must be coherent: {failure}");
            providerConfiguration = childConfiguration with { Assignments = [] };
            PopulateCognitionTargets();
            cognitionTargetChoice.Select(1);
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
            if (!cognitionConfigurationStatus.Text.Contains("Model needs setup", StringComparison.Ordinal) ||
                !cognitionConfigurationStatus.Text.Contains("frozen-before-save-model", StringComparison.Ordinal) ||
                !cognitionConfigurationStatus.Text.Contains("waiting", StringComparison.Ordinal) ||
                cognitionConfigurationStatus.Text.Contains("No personal model selected", StringComparison.Ordinal))
                throw new InvalidOperationException("A saved birth choice awaiting provider storage must remain visible instead of appearing unconfigured.");

            providerConfiguration = childConfiguration with
            {
                Assignments = [new(childId, "planning", "inherit")],
            };
            RenderProviderConfiguration();
            if (!cognitionConfigurationStatus.Text.Contains("Routine: OpenAI · frozen-before-save-model waiting", StringComparison.Ordinal) ||
                !cognitionConfigurationStatus.Text.Contains("Planning: no personal model", StringComparison.Ordinal))
                throw new InvalidOperationException("A pending birth route must remain visible alongside an explicit role-specific override.");

            providerConfiguration = childConfiguration with
            {
                Assignments = [new(childId, "routine", "inherit"), new(childId, "planning", "inherit")],
            };
            RenderProviderConfiguration();
            if (!cognitionConfigurationStatus.Text.Contains("No personal model selected", StringComparison.Ordinal) ||
                cognitionConfigurationStatus.Text.Contains("frozen-before-save-model", StringComparison.Ordinal))
                throw new InvalidOperationException("An explicit no-model override must take precedence over the historical birth choice.");
        }
        finally
        {
            observationSession.ResetAfterLoad();
            if (previousReconnect is not null &&
                !observationSession.TryAccept(previousReconnect, previousReconnect.Baseline.Events.AfterEventId, out restorationFailure))
            {
                restorationFailure ??= "The previous observation was rejected.";
            }
            providerConfiguration = previousConfiguration;
            PopulateCognitionTargets();
            PopulateProviderChoices(ActiveProviderForSelectedRole());
            PopulateCredentialChoices();
            RenderProviderConfiguration();
        }
        if (restorationFailure is not null)
            throw new InvalidOperationException($"The child status smoke could not restore the previous observation: {restorationFailure}");
    }

    /// <summary>Pixel lettering stays crisp only at whole multiples of its pixel size.</summary>
    private void VerifyPixelText(string when)
    {
        var theme = GetTree().Root.Theme;
        if (theme.DefaultFont != UiFonts.Text || theme.DefaultFontSize != UiFonts.PixelSize ||
            UiFonts.Text.Antialiasing != TextServer.FontAntialiasing.None ||
            UiFonts.Text.SubpixelPositioning != TextServer.SubpixelPositioning.Disabled)
            throw new InvalidOperationException("Body text must use Fusion Pixel at its 12 px size without smoothing.");
        foreach (var heading in new Control[] { menuHeadingLabel, selectedActorNameLabel, clockLabel, mainMenuContinueButton })
            if (heading.GetThemeFont("font") != UiFonts.Headings)
                throw new InvalidOperationException($"{heading.Name} must use the Timber heading lettering.");
        ConfirmationDialog[] dialogs = [quitGameConfirmation, quitToMenuConfirmation, manualSaveLoadConfirmation, manualSaveOverwriteConfirmation, deletionConfirmation];
        if (dialogs.Any(dialog => dialog.GetThemeFont("title_font") != UiFonts.Headings))
            throw new InvalidOperationException("Dialog titles must use the Timber heading lettering.");
        foreach (var letter in TimberFont.Characters)
            if (!UiFonts.Headings.HasChar(letter) || !UiFonts.Headings.HasChar(char.ToLowerInvariant(letter)))
                throw new InvalidOperationException($"Timber must draw '{letter}' in both cases.");
        var uneven = new List<string>();
        foreach (var control in FindChildren("*", nameof(Control), recursive: true, owned: false).OfType<Control>())
        {
            string[] items = control switch
            {
                RichTextLabel => ["normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size"],
                Label or Button or LineEdit or TextEdit or ItemList => ["font_size"],
                _ => [],
            };
            foreach (var item in items)
                if (control.GetThemeFontSize(item) % UiFonts.PixelSize != 0)
                    uneven.Add($"{control.Name} {item}={control.GetThemeFontSize(item)}");
        }
        uneven.AddRange(dialogs.Where(dialog => dialog.GetThemeFontSize("title_font_size") % UiFonts.PixelSize != 0)
            .Select(dialog => $"{dialog.Title} title={dialog.GetThemeFontSize("title_font_size")}"));
        if (uneven.Count > 0)
            throw new InvalidOperationException($"Text must be drawn at whole multiples of {UiFonts.PixelSize} px {when}: {string.Join(", ", uneven.Take(10))}.");
    }

    private async Task VerifyMenuBackdropAsync()
    {
        var originalPalette = UiTheme.Current;
        try
        {
            if (!mainMenuBackdrop.IsVisibleInTree() || mainMenuBackdrop.MouseFilter != MouseFilterEnum.Ignore ||
                !mainMenuBackdrop.GetGlobalRect().Grow(1).Encloses(mainMenuOverlay.GetGlobalRect()))
                throw new InvalidOperationException("The Main Menu backdrop must fill the title screen without taking clicks.");
            UiTheme.Apply(GetTree().Root, UiTheme.Light);
            var day = mainMenuBackdrop.Scene;
            if (mainMenuBackdrop.Night || day.Night || day.Stars.Count != 0 || day.Glows.Count != 0 ||
                day.Land.GetWidth() != MenuScene.Width || day.Land.GetHeight() != MenuScene.Height ||
                day.Land.GetPixel(0, 0).A != 0 || day.Land.GetPixel(0, MenuScene.Height - 1).A < 1 ||
                day.Clouds.Count == 0 || day.Chimneys.Count == 0 || day.Sparkles.Count == 0)
                throw new InvalidOperationException("The Light theme must show the daytime valley with clouds, smoke and river sparkles.");
            if (!MenuScene.Create(night: false).Land.GetData().AsSpan().SequenceEqual(day.Land.GetData()))
                throw new InvalidOperationException("The Main Menu backdrop must draw the same picture every time.");
            UiTheme.Apply(GetTree().Root, UiTheme.Dark);
            var dusk = mainMenuBackdrop.Scene;
            if (!mainMenuBackdrop.Night || !dusk.Night || dusk.Stars.Count == 0 || dusk.Glows.Count == 0 ||
                dusk.FireflyHomes.Count == 0 || dusk.MoonReflection.Count == 0)
                throw new InvalidOperationException("The Dark theme must switch the backdrop to dusk with stars, lights and fireflies.");
            var before = mainMenuBackdrop.AnimationTime;
            for (var frame = 0; frame < 4; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (mainMenuBackdrop.AnimationTime <= before)
                throw new InvalidOperationException("The Main Menu backdrop must animate while the title screen is open.");
            mainMenuOverlay.Hide();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var hidden = mainMenuBackdrop.AnimationTime;
            for (var frame = 0; frame < 4; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (mainMenuBackdrop.AnimationTime != hidden)
                throw new InvalidOperationException("The Main Menu backdrop must hold still while it is hidden.");
        }
        finally
        {
            mainMenuOverlay.Show();
            UiTheme.Apply(GetTree().Root, originalPalette);
        }
    }

    private async Task VerifyFirstWorldListAsync()
    {
        VerifyWorldThumbnailFallback();
        worldMenuColumns.Hide();
        worldSelectionList.Show();
        worldSelectButton.Show();
        worldMenuOverlay.Show();
        var response = new TaskCompletionSource<WorldCatalogSnapshot>();
        var loading = worldListRequest.RefreshAsync(_ => response.Task);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!worldMenuStatus.Text.StartsWith("Checking saved worlds", StringComparison.Ordinal) ||
            worldSelectionList.Placeholder != "Checking saved worlds…" ||
            !worldSelectButton.Disabled || !worldDeleteButton.Disabled || worldBackButton.Disabled)
            throw new InvalidOperationException("The first world-list opening must show checking progress with Back available.");
        response.SetResult(new WorldCatalogSnapshot("world-0", Enumerable.Range(0, 7).Select(index =>
            new CatalogWorld($"world-{index}", $"World {index}", $"world-{index}", "seed",
                DateTimeOffset.UnixEpoch, [], null, index == 6 ? "incompatible" : "compatible",
                Thumbnail: index switch
                {
                    0 => new WorldThumbnail(1, 1, "terrain-kind-v1", null!),
                    1 => new WorldThumbnail(2, 1, "terrain-kind-v1", "AAU="),
                    _ => null,
                })).ToArray()));
        await loading;
        for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (worldSelectionList.ItemCount != 7 || listedActiveWorldId != "world-0" || !worldSelectionList.IsVisibleInTree() ||
            worldMenuScroll.Size.Y < 300)
            throw new InvalidOperationException("The first opening must display a delayed seven-world result without reopening.");
        // Each world is a card, current world first; the arrow keys move between them.
        if (worldSelectionList.GetItemTitle(0) != "World 0" || worldSelectionList.GetItemTitle(6) != "World 6")
            throw new InvalidOperationException("World cards must show each world's name, the current world first.");
        worldSelectionList.Select(0);
        worldSelectionList._GuiInput(new InputEventAction { Action = "ui_down", Pressed = true });
        if (worldSelectionList.GetSelectedItems() is not [1])
            throw new InvalidOperationException("The down arrow must choose the next world card.");
        worldSelectionList.Select(6);
        worldSelectionList.EmitSignal(SlotList.SignalName.ItemSelected, 6L);
        if (!worldSelectButton.Disabled)
            throw new InvalidOperationException("An incompatible world must remain blocked after listing.");
        worldMenuOverlay.Hide();
    }

    private static void VerifyWorldThumbnailFallback()
    {
        WorldThumbnail?[] invalid =
        [
            null,
            new(1, 1, "terrain-kind-v1", null!),
            new(1, 1, null!, "AA=="),
            new(1, 1, "unknown", "AA=="),
            new(1, 1, "terrain-kind-v1", "!!!!"),
            new(1, 1, "terrain-kind-v1", "AAAA"),
            new(1, 1, "terrain-kind-v1", "/w=="),
            new(0, 1, "terrain-kind-v1", ""),
            new(1, -1, "terrain-kind-v1", ""),
            new(97, 1, "terrain-kind-v1", Convert.ToBase64String(new byte[97])),
            new(1, 2049, "terrain-kind-v1", Convert.ToBase64String(new byte[2049])),
            new(int.MaxValue, int.MaxValue, "terrain-kind-v1", "AA=="),
        ];
        foreach (var thumbnail in invalid)
        {
            using var unusable = WorldThumbnailTexture(thumbnail);
            if (unusable is not null)
                throw new InvalidOperationException("Missing or damaged world thumbnails must fall back to the globe.");
        }
        using var texture = WorldThumbnailTexture(new WorldThumbnail(2, 1, "terrain-kind-v1", "AAU="));
        if (texture is null || texture.GetWidth() != 2 || texture.GetHeight() != 1)
            throw new InvalidOperationException("A valid world thumbnail must retain its shape.");
        using var image = texture.GetImage();
        if (image.GetPixel(0, 0) != TerrainTextures.BaseColor(TerrainStyle.Grass) ||
            image.GetPixel(1, 0) != TerrainTextures.BaseColor(TerrainStyle.Ocean))
            throw new InvalidOperationException("World thumbnails must draw the overview's terrain colors.");
    }

    /// <summary>The logo replaces the old title and slogan, sits above the card and stays crisp.</summary>
    private void VerifyMainMenuLogo(Vector2I size)
    {
        var logo = mainMenuLogo.GetGlobalRect();
        var scale = logo.Size.X / MenuLogo.Width;
        if (!mainMenuLogo.IsVisibleInTree() || mainMenuLogo.Texture.GetWidth() != MenuLogo.Width ||
            mainMenuLogo.Texture.GetHeight() != MenuLogo.Height || scale < 2 || scale != Mathf.Floor(scale) ||
            logo.Size.Y != MenuLogo.Height * scale || logo.End.Y > mainMenuCard.GetGlobalRect().Position.Y ||
            !mainMenuOverlay.GetGlobalRect().Encloses(logo))
            throw new InvalidOperationException($"At {size} the logo must sit above the menu card at a whole-number scale: logo={logo} card={mainMenuCard.GetGlobalRect()}.");
        if (mainMenuCard.FindChildren("*", nameof(Label), true, false).OfType<Label>().Any(label =>
                label.Visible && (label.Text == "CLANKERWORLD" || label.Text.StartsWith("A world shaped", StringComparison.Ordinal))))
            throw new InvalidOperationException("The Main Menu must show the logo instead of a text title or slogan.");
        if (!mainMenuStatus.Visible || mainMenuStatus.Text != "Connect this device to your world to play.")
            throw new InvalidOperationException("An unpaired Main Menu must say how to start playing.");
        SetMainMenuStatus(null);
        if (mainMenuStatus.Visible)
            throw new InvalidOperationException("The Main Menu status line must disappear when nothing needs attention.");
        RefreshMainMenuAvailability();
        if (!MenuLogo.Create().GetData().AsSpan().SequenceEqual(((ImageTexture)mainMenuLogo.Texture).GetImage().GetData()))
            throw new InvalidOperationException("The logo must be drawn the same way every time.");
        foreach (var iconSize in MenuLogo.IconSizes)
        {
            var icon = MenuLogo.Icon(iconSize);
            if (icon.GetWidth() != iconSize || icon.GetHeight() != iconSize || icon.GetPixel(iconSize / 2, iconSize / 2).A < 1)
                throw new InvalidOperationException($"The {iconSize} px window icon must be a filled square image.");
        }
        VerifyAppIconFile();
    }

    /// <summary>The committed Windows program icon must still match the logo art.</summary>
    private static void VerifyAppIconFile()
    {
        const string stale = "The Windows program icon is out of date. Run: godot --headless --path src/ClankerWorld.GodotClient -- --write-app-icon";
        var file = Godot.FileAccess.GetFileAsBytes(AppIconPath);
        if (file.Length < 6 || BitConverter.ToUInt16(file, 2) != 1 || BitConverter.ToUInt16(file, 4) != MenuLogo.IconSizes.Length)
            throw new InvalidOperationException(stale);
        for (var i = 0; i < MenuLogo.IconSizes.Length; i++)
        {
            var entry = 6 + 16 * i;
            var size = file[entry] == 0 ? 256 : file[entry];
            var length = (int)BitConverter.ToUInt32(file, entry + 8);
            var offset = (int)BitConverter.ToUInt32(file, entry + 12);
            var image = new Image();
            if (size != MenuLogo.IconSizes[i] || offset + length > file.Length ||
                image.LoadPngFromBuffer(file[offset..(offset + length)]) != Error.Ok)
                throw new InvalidOperationException(stale);
            image.Convert(Image.Format.Rgba8);
            if (!image.GetData().AsSpan().SequenceEqual(MenuLogo.Icon(size).GetData()))
                throw new InvalidOperationException(stale);
        }
    }

    private async Task VerifyMenuLayoutAsync()
    {
        try
        {
            VerifyEventLogAgentNames();
            await VerifyNewcomerOfferAsync();
            await VerifyMenuBackdropAsync();
            // Tooltips and other windows the engine creates on demand follow the root's filter,
            // so pixel frames must not be smoothed there either.
            if (GetTree().Root.CanvasItemDefaultTextureFilter != Viewport.DefaultCanvasItemTextureFilter.Nearest)
                throw new InvalidOperationException("Tooltips and other windows must draw pixel frames without smoothing.");
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1024, 768) })
            {
                GetWindow().Size = size;
                for (var frame = 0; frame < 3; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!mainMenuOverlay.GetGlobalRect().Encloses(mainMenuStack.GetGlobalRect()) ||
                    mainMenuOverlay.GetGlobalRect().GetCenter().DistanceTo(mainMenuStack.GetGlobalRect().GetCenter()) > 2)
                    throw new InvalidOperationException($"Main Menu escaped its centered bounds at {size}.");
                VerifyMainMenuLogo(size);
                manualSaveOverlay.Show();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!manualSaveOverlay.GetGlobalRect().Encloses(manualSaveCard.GetGlobalRect()) ||
                    manualSaveOverlay.GetGlobalRect().GetCenter().DistanceTo(manualSaveCard.GetGlobalRect().GetCenter()) > 2)
                    throw new InvalidOperationException($"Save/load panel escaped its centered bounds at {size}.");
                manualSaveOverlay.Hide();
                ResetWorldGenerationOptions();
                if (CurrentWorldOptions().WaterPercent != 50 || CurrentWorldOptions().ForestCover != "Normal" ||
                    CurrentWorldOptions().MountainRelief != "Normal" || CurrentWorldOptions().RiverAbundance != "Normal" ||
                    !CurrentWorldOptions().LatitudeCooling || !CurrentWorldOptions().WrapEastWest || worldSizeChoice.ItemCount != 2)
                    throw new InvalidOperationException("Reset must restore the complete supported New World preset.");
                worldAdvancedToggle.ButtonPressed = true;
                if (!worldAdvancedOptions.Visible || !worldForestChoice.KeyboardReachable)
                    throw new InvalidOperationException("Advanced generation controls must be expandable and keyboard accessible.");
                var missedCoverage = new OwnerWorldCandidateReport(2, 100, 15, 10, 15, 10,
                    2, 10, 1, 10, true, true, false, true);
                previewedWorldOptions = CurrentWorldOptions();
                previewedWorldResult = new OwnerWorldPreview(new OwnerWorldPackedTerrain(1, 1, "terrain-v1", "AA=="),
                    new OwnerWorldPosition(0, 0), "preview-manifest")
                {
                    MapLayersDigest = "preview-layers",
                    Coverage = missedCoverage,
                    Candidates = [missedCoverage],
                };
                worldAcceptUnmetTargets.Show();
                if (CanCreatePreview(CurrentWorldOptions()))
                    throw new InvalidOperationException("A preview that misses a default Balanced target must require explicit acceptance.");
                worldAcceptUnmetTargets.ButtonPressed = true;
                if (!CanCreatePreview(CurrentWorldOptions()))
                    throw new InvalidOperationException("Explicitly accepting displayed coverage misses must enable creation of that preview.");
                worldAcceptUnmetTargets.ButtonPressed = false;
                var mountainOnly = missedCoverage with { ForestTargetApplicable = false, ForestTargetMet = false };
                var mountainOnlyPreview = previewedWorldResult with
                {
                    Coverage = mountainOnly,
                    Candidates = [mountainOnly],
                };
                SetWorldPreviewStatus(mountainOnlyPreview);
                if (!worldPreviewStatus.Text.Contains("Met applicable Normal target: mountains", StringComparison.Ordinal) ||
                    worldPreviewStatus.Text.Contains("Both default Balanced trial targets", StringComparison.Ordinal))
                    throw new InvalidOperationException("Preview must name only the applicable Normal target when the other control is Low or High.");
                var forestOnly = missedCoverage with
                {
                    ForestTargetApplicable = true,
                    ForestTargetMet = false,
                    MountainTargetApplicable = false,
                    MountainTargetMet = false,
                };
                SetWorldPreviewStatus(mountainOnlyPreview with { Coverage = forestOnly, Candidates = [forestOnly] });
                if (!worldPreviewStatus.Text.Contains("Missed: Forest 15.0%", StringComparison.Ordinal) ||
                    !worldPreviewStatus.Text.Contains("Candidate results:", StringComparison.Ordinal))
                    throw new InvalidOperationException("Preview must name a missed Normal target and candidate results when only forest is targeted.");
                var noTargets = mountainOnly with { MountainTargetApplicable = false, MountainTargetMet = false };
                SetWorldPreviewStatus(mountainOnlyPreview with { Coverage = noTargets, Candidates = [noTargets] });
                if (!worldPreviewStatus.Text.Contains("No trial targets apply", StringComparison.Ordinal) ||
                    !worldPreviewStatus.Text.Contains($"{15d:F1}% forest and {10d:F1}% mountains", StringComparison.Ordinal) ||
                    worldAcceptUnmetTargets.Visible)
                    throw new InvalidOperationException("Preview without targets must show measured coverage without an acceptance gate.");
                InvalidateWorldPreview(refresh: false);
                var preset = CurrentWorldOptions();
                worldForestChoice.Select(0);
                if (SameGeneration(preset, CurrentWorldOptions()))
                    throw new InvalidOperationException("Changing an advanced setting must invalidate the matching preview.");
                ResetWorldGenerationOptions();
                worldAdvancedToggle.ButtonPressed = false;
                worldMenuHeading.Text = "New World";
                worldMenuStatus.Text = "Pick a seed and size. After creating the world, choose where your first Town goes and add four founders, then start time.";
                worldPreviewStatus.Text = "Map preview · you will choose where your first Town goes after creating the world.";
                worldPreview.Show();
                worldMenuOverlay.Show();
                if (!worldNameInput.GetParent().GetChildren().OfType<Label>().Any(label => label.Text == "Name") ||
                    !worldSeedInput.GetParent().GetChildren().OfType<Label>().Any(label => label.Text == "Seed"))
                    throw new InvalidOperationException("New World name and seed fields must keep visible captions once filled.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!worldMenuOverlay.GetGlobalRect().Encloses(worldMenuCard.GetGlobalRect()) ||
                    worldMenuOverlay.GetGlobalRect().GetCenter().DistanceTo(worldMenuCard.GetGlobalRect().GetCenter()) > 2)
                    throw new InvalidOperationException($"World creation/selection panel escaped its centered bounds at {size}.");
                if (!worldMenuScroll.GetGlobalRect().Grow(1).Encloses(worldCreateButton.GetGlobalRect()) ||
                    worldPreviewFrame.Size.X < 480 || worldPreviewFrame.GetGlobalRect().Position.X <= worldNameInput.GetGlobalRect().Position.X)
                    throw new InvalidOperationException($"At {size} New World must show a large preview beside its options and Create World without scrolling: preview={worldPreviewFrame.GetGlobalRect()} create={worldCreateButton.GetGlobalRect()} scroll={worldMenuScroll.GetGlobalRect()}.");
                worldMenuOverlay.Hide();
                worldPreview.Hide();
            }
            OpenMainMenuSettings();
            if (!mainMenuOverlay.Visible || mainMenuCard.Visible || !gameMenuPanel.Visible || !gameSettingsContent.Visible ||
                worldSettingsCategoryButton.Visible || worldSettingsContent.Visible || menuResumeButton.Visible ||
                !ShowsGlyph(menuCloseButton, PixelGlyph.Back) || !mainMenuBackdrop.IsVisibleInTree() || mainMenuLogo.Visible)
                throw new InvalidOperationException("Main Menu Settings must keep the title background and show only Game Settings.");
            if (!gameSettingsCategoryButton.ButtonPressed || gameSettingsCategoryButton.Disabled)
                throw new InvalidOperationException("The open Settings category must read as selected, not disabled.");
            if (!apiKeysPanel.IsVisibleInTree() || !gameSettingsContent.IsAncestorOf(apiKeysPanel) || !apiKeyInput.Secret)
                throw new InvalidOperationException("Main Menu Game Settings must offer API keys with a masked key entry before placing any agents.");
            if (!usageLimitPanel.IsVisibleInTree() || !gameSettingsContent.IsAncestorOf(usageLimitPanel) ||
                worldSettingsContent.IsAncestorOf(usageLimitPanel) ||
                !usageScopeHint.Text.Contains("all your worlds", StringComparison.Ordinal) ||
                !usageScopeHint.Text.Contains("call attempt", StringComparison.Ordinal))
                throw new InvalidOperationException("Main Menu Game Settings must show the model-call limit and say it covers every world and counts call attempts.");
            apiKeyInput.Text = "test-only-ui-key";
            apiKeyProviderChoice.Select(1);
            apiKeyProviderChoice.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
            if (apiKeyInput.Text.Length != 0)
                throw new InvalidOperationException("Changing API key provider must clear the pasted key.");
            apiKeyInput.Text = "test-only-ui-key";
            apiKeysPanel.Hide();
            if (apiKeyInput.Text.Length != 0)
                throw new InvalidOperationException("Hiding API key settings must clear the pasted key.");
            apiKeysPanel.Show();
            settingsButton.EmitSignal(BaseButton.SignalName.Pressed);
            settingsButton.EmitSignal(BaseButton.SignalName.Pressed);
            gameSettingsCategoryButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!settingsPanel.Visible || !gameSettingsContent.Visible)
                throw new InvalidOperationException("The selected Settings category must remain open.");
            worldSettingsCategoryButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!gameMenuPanel.Visible || !gameSettingsContent.Visible || worldSettingsContent.Visible ||
                !returnToMainMenu)
                throw new InvalidOperationException("World Settings cannot be opened from the Main Menu.");
            for (var frame = 0; frame < 2; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Math.Abs(clockFormatChoice.GetGlobalRect().Position.X - themeChoice.GetGlobalRect().Position.X) > 1 ||
                Math.Abs(dateFormatChoice.GetGlobalRect().Position.X - windowSizeChoice.GetGlobalRect().Position.X) > 1)
                throw new InvalidOperationException("Game Settings choices must share one aligned caption column.");
            // Both themes keep text readable on every surface it sits on.
            foreach (var palette in new[] { UiTheme.Light, UiTheme.Dark })
            {
                (string Pair, Color Text, Color Surface, float Minimum)[] readable =
                [
                    ("ink on parchment", palette.Ink, palette.Paper, 7f),
                    ("muted ink on parchment", palette.InkMuted, palette.Paper, 4.5f),
                    ("section headings", palette.Section, palette.Paper, 4.5f),
                    ("links", palette.Link, palette.Paper, 4.5f),
                    ("warnings", palette.Warning, palette.Paper, 4.5f),
                    ("good status", palette.Good, palette.Paper, 4.5f),
                    ("bad status", palette.Bad, palette.Paper, 4.5f),
                    ("button text", palette.Ink, palette.Button, 4.5f),
                    ("primary button text", palette.PrimaryInk, palette.Primary, 4.5f),
                    ("paused button text", palette.EmberInk, palette.Ember, 4.5f),
                    ("text on the wooden bar", palette.OnWood, palette.Wood, 4.5f),
                    ("soft text on the wooden bar", palette.OnWoodSoft, palette.Wood, 4.5f),
                    ("field text", palette.Ink, palette.Field, 4.5f),
                    ("disabled text", palette.InkFaint, palette.FieldDisabled, 3f),
                ];
                foreach (var (pair, text, surface, minimum) in readable)
                    if (UiTheme.Contrast(text, surface) < minimum)
                        throw new InvalidOperationException($"{palette.Name} theme {pair} is too faint: {UiTheme.Contrast(text, surface):0.00}.");
            }
            var themeBefore = displayPreferences.Theme;
            var frameBefore = settingsPanel.GetThemeStylebox("panel");
            var (switchTo, expected) = UiTheme.Current == UiTheme.Dark
                ? (UiThemeChoice.Light, UiTheme.Light)
                : (UiThemeChoice.Dark, UiTheme.Dark);
            themeChoice.Select((int)switchTo);
            SetUiTheme((int)switchTo);
            if (UiTheme.Current != expected || GetTree().Root.Theme != UiTheme.Theme ||
                displayPreferences.Theme != UiTheme.Key(switchTo) || settingsPanel.GetThemeStylebox("panel") == frameBefore ||
                appBackdrop.Color != expected.Backdrop)
                throw new InvalidOperationException("Choosing a theme must restyle the open window at once and be remembered.");
            themeChoice.Select((int)UiTheme.Parse(themeBefore));
            SetUiTheme((int)UiTheme.Parse(themeBefore));
            var (hazeBefore, flashesBefore) = (displayPreferences.CloudHaze, displayPreferences.LightningFlashes);
            cloudHazeToggle.ButtonPressed = !hazeBefore;
            lightningToggle.ButtonPressed = !flashesBefore;
            if (weatherLayer.CloudsEnabled == hazeBefore || weatherLayer.LightningEnabled == flashesBefore ||
                displayPreferences.CloudHaze == hazeBefore || displayPreferences.LightningFlashes == flashesBefore)
                throw new InvalidOperationException("The cloud haze and lightning switches must take effect at once and be remembered.");
            cloudHazeToggle.ButtonPressed = hazeBefore;
            lightningToggle.ButtonPressed = flashesBefore;
            if (!topBarShade.Visible || topBarShade.ZIndex <= mainMenuOverlay.ZIndex)
                throw new InvalidOperationException("Main Menu Settings must shade the top bar like the rest of the title backdrop.");
            SetStatus("Settings status check", good: true);
            if (!statusToast.Visible || statusToast.ZIndex <= gameMenuPanel.ZIndex || statusToast.ZIndex <= mainMenuOverlay.ZIndex)
                throw new InvalidOperationException("Status messages must remain visible above Main Menu Settings.");
            statusToast.Hide();
            var backPoint = menuCloseButton.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = backPoint }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var hoveredBackControl = GetViewport().GuiGetHoveredControl();
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = backPoint,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = backPoint,
                ButtonIndex = MouseButton.Left,
                Pressed = false,
            }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!mainMenuOverlay.Visible || !mainMenuCard.Visible || gameMenuPanel.Visible)
                throw new InvalidOperationException($"Clicking Back in Main Menu Settings must return to the Main Menu. Back={menuCloseButton.GetGlobalRect()}, pointer={backPoint}, hovered={hoveredBackControl?.GetPath()}");
            OpenMenuForSetup();
            if (!mainMenuOverlay.Visible || mainMenuCard.Visible || !gameMenuPanel.Visible || menuResumeButton.Visible ||
                !ShowsGlyph(menuCloseButton, PixelGlyph.Back) || menuHeadingLabel.Text != "Connect this device" || !topBarShade.Visible)
                throw new InvalidOperationException("Connect/pair setup must keep the title backdrop with a single compact back button.");
            menuCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!mainMenuCard.Visible || gameMenuPanel.Visible || topBarShade.Visible)
                throw new InvalidOperationException("Back from connect/pair setup must return to the Main Menu.");
            var displayWindow = GetWindow();
            var originalWindowSize = displayWindow.Size;
            var originalRenderSize = displayWindow.ContentScaleSize;
            var originalScaleMode = displayWindow.ContentScaleMode;
            var originalDisplayPreferences = displayPreferences;
            var originalWindowChoice = windowSizeChoice.Selected;
            try
            {
                await VerifyFirstWorldListAsync();
                await VerifyWorldActionSelectionAsync();
                await VerifyNewWorldCompatibilityMessageAsync();
                await VerifyAutosaveSettingsOwnershipAsync();
                await VerifyUiScaleAt1440pAsync(displayWindow);
                await VerifyManualSaveListOwnershipAsync();
                VerifySaveBranchList();
                windowSizeChoice.Select(1);
                SetWindowSize(1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (displayWindow.Size != DisplaySizePresets[1])
                    throw new InvalidOperationException("Window Size must change the physical window size.");
                // The game draws at the window's own resolution whatever its size.
                windowSizeChoice.Select(0);
                SetWindowSize(0);
                if (displayWindow.Size != DisplaySizePresets[0] ||
                    displayWindow.ContentScaleSize != AutomaticRenderSize() ||
                    displayWindow.ContentScaleMode != Window.ContentScaleModeEnum.Viewport)
                    throw new InvalidOperationException($"The picture must follow the window's own resolution: render={displayWindow.ContentScaleSize}, expected={AutomaticRenderSize()}.");
            }
            finally
            {
                displayWindow.Size = originalWindowSize;
                displayWindow.ContentScaleSize = originalRenderSize;
                displayWindow.ContentScaleMode = originalScaleMode;
                windowSizeChoice.Select(originalWindowChoice);
                SaveDisplayPreferences(originalDisplayPreferences);
                ApplyUiScale();
            }
            mainMenuOverlay.Hide();
            isInWorld = true;
            returnToMainMenu = false;
            SetWorldMenuActionsVisible(true);
            pairingPanel.Hide();
            gameMenuPanel.Show();
            var pauseActions = menuQuitToMainButton.GetParent<VBoxContainer>().GetChildren()
                .OfType<Button>().Where(button => button.Visible).Select(button => button.Text).ToArray();
            if (!pauseActions.SequenceEqual(new[] { "Save World", "Settings", "Mod Library", "Quit to Menu" }) ||
                gameMenuPanel.FindChildren("*", nameof(Button), recursive: true, owned: false)
                    .OfType<Button>().Any(button => button.Visible && button.Text == "Create"))
                throw new InvalidOperationException("Pause Menu must have only the four ordered actions and no player Create workbench.");
            modLibraryButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!modLibraryPanel.Visible || settingsPanel.Visible)
                throw new InvalidOperationException("Mod Library action must open the in-world package view.");
            if (menuActions.Visible || menuHeadingLabel.Text != "Mod Library" || !ShowsGlyph(menuCloseButton, PixelGlyph.Back))
                throw new InvalidOperationException("A Pause Menu page must replace the menu's buttons and offer a way back.");
            if (!HandleEscape() || !menuActions.Visible || modLibraryPanel.Visible || !gameMenuPanel.Visible ||
                menuHeadingLabel.Text != "Paused")
                throw new InvalidOperationException("Escape on a Pause Menu page must return to the menu's buttons, not close the menu.");
            settingsButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (modLibraryPanel.Visible || !settingsPanel.Visible || !worldSettingsCategoryButton.Visible)
                throw new InvalidOperationException("Settings action must open the in-world Game/World category view.");
            // Developer tools have their own F12 panel; Settings keeps only Game and World.
            var settingsCategories = worldSettingsCategoryButton.GetParent().GetChildren().OfType<Button>()
                .Where(button => button.Visible).Select(button => button.Text).ToArray();
            if (!settingsCategories.SequenceEqual(new[] { "Game", "World" }) || gameMenuPanel.IsAncestorOf(developerBody) ||
                gameMenuPanel.FindChildren("*", nameof(Button), recursive: true, owned: false)
                    .OfType<Button>().Any(button => button.Text.Contains("Developer", StringComparison.Ordinal)))
                throw new InvalidOperationException($"The Pause Menu must no longer hold Developer tools: {string.Join(", ", settingsCategories)}.");
            gameSettingsCategoryButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!settingsScroll.Visible || !gameSettingsContent.Visible)
                throw new InvalidOperationException("Game Settings must open in the Settings panel.");
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!GetViewportRect().Grow(1).Encloses(gameMenuPanel.GetGlobalRect()))
                throw new InvalidOperationException($"Pause Menu Settings must fit on screen: menu={gameMenuPanel.GetGlobalRect()} screen={GetViewportRect()}.");
            // Game and World share one width. The call limit covers every world, so it is on the Game page only.
            if (!usageLimitPanel.IsVisibleInTree() || usageLimitPanel.GetParent() != gameSettingsContent ||
                usageLimitPanel.GetIndex() != apiKeysPanel.GetIndex() + 1)
                throw new InvalidOperationException("In-world Game Settings must show Model calls right after API keys.");
            var gamePageWidth = gameMenuPanel.Size.X;
            settingsScroll.ScrollVertical = 200;
            worldSettingsCategoryButton.EmitSignal(BaseButton.SignalName.Pressed);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!worldSettingsContent.Visible || !Mathf.IsEqualApprox(gameMenuPanel.Size.X, gamePageWidth) ||
                settingsScroll.ScrollVertical != 0 || usageLimitPanel.IsVisibleInTree() ||
                worldSettingsContent.IsAncestorOf(usageLimitPanel) ||
                cognitionSettingsPanel.GetParent() != worldSettingsContent ||
                cognitionSettingsPanel.GetIndex() != worldSettingsContent.GetChildCount() - 1)
                throw new InvalidOperationException($"World Settings must open at the top, keep the Game page's width, end with Agent model and leave Model calls to Game Settings: {gameMenuPanel.Size.X} vs {gamePageWidth}.");
            ShowPauseMenuButtons();
            menuQuitToMainButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!quitToMenuConfirmation.Visible)
                throw new InvalidOperationException("Quit to Menu must request confirmation.");
            if (quitToMenuConfirmation.OkButtonText != "Quit to Menu" || quitGameConfirmation.OkButtonText != "Quit Game" ||
                manualSaveOverwriteConfirmation.OkButtonText != "Overwrite" ||
                quitToMenuConfirmation.GetThemeStylebox("embedded_border", "Window") != UiTheme.Theme.GetStylebox("embedded_border", "Window"))
                throw new InvalidOperationException("Confirmations must use the game's panel style and name their action instead of OK.");
            quitToMenuConfirmation.Hide();
            listedSaveWorldId = "smoke-world";
            listedManualSaves = [new ManualWorldSave("smoke-save", "Selected snapshot", DateTimeOffset.UtcNow, 0, false)];
            manualSaveList.Clear();
            manualSaveList.AddItem("Selected snapshot");
            manualSaveList.Select(0);
            manualSaveDeleteButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!deletionConfirmation.Visible || pendingDeletion?.Id != "smoke-save" ||
                !deletionConfirmation.DialogText.Contains("Selected snapshot", StringComparison.Ordinal) ||
                deletionConfirmation.OkButtonText != "Delete permanently")
                throw new InvalidOperationException("Save deletion must name the selected snapshot and require permanent confirmation.");
            deletionConfirmation.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            deletionConfirmation.Hide();
            if (pendingDeletion is not null || listedManualSaves.Length != 1)
                throw new InvalidOperationException("Canceling deletion must clear its target without changing saves.");
            listedManualSaves = [];
            manualSaveList.Clear();
            listedSaveWorldId = null;
            listedActiveWorldId = "active-world";
            listedWorlds = [new CatalogWorld("active-world", "Active", "active", "seed", DateTimeOffset.UtcNow, [], null),
                new CatalogWorld("other-world", "Other", "other", "seed-two", DateTimeOffset.UtcNow, [], null)];
            worldSelectionList.Clear();
            worldSelectionList.AddItem("Active");
            worldSelectionList.AddItem("Other");
            worldSelectionList.Select(0);
            ConfirmWorldDeletion();
            if (pendingDeletion is not null || deletionConfirmation.Visible)
                throw new InvalidOperationException("Deleting the active world must be blocked before confirmation.");
            worldSelectionList.Select(1);
            ConfirmWorldDeletion();
            if (pendingDeletion?.Id != "other-world" || !deletionConfirmation.Visible ||
                !deletionConfirmation.DialogText.Contains("all of its manual saves and autosaves", StringComparison.Ordinal))
                throw new InvalidOperationException("World deletion must identify the selected world and all its saves.");
            deletionConfirmation.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            deletionConfirmation.Hide();
            listedWorlds = [];
            worldSelectionList.Clear();
            // The pause receipt can succeed even when the following reconnect
            // fails. Leaving must not require a newer snapshot in that case.
            menuPauseConfirmed = true;
            menuPausedWorld = true;
            QuitToMainMenu();
            if (!mainMenuOverlay.Visible || gameMenuPanel.Visible || !resumeWorldOnContinue)
                throw new InvalidOperationException("An accepted pause must allow Quit to Menu despite a held snapshot.");
            mainMenuOverlay.Hide();
            isInWorld = true;
            resumeWorldOnContinue = false;
            gameMenuPanel.Show();
            menuShade.Show();
            if (!topBarShade.Visible)
                throw new InvalidOperationException("Top-bar actions must be blocked while the Pause Menu is open.");
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(1024, 768) })
            {
                GetWindow().Size = size;
                foreach (var settingsVisible in new[] { false, true })
                {
                    foreach (var worldSpecific in settingsVisible ? new[] { false, true } : new[] { false })
                    {
                        if (settingsVisible)
                        {
                            ShowSettingsSection(worldSpecific);
                            if (gameSettingsContent.Visible == worldSpecific || worldSettingsContent.Visible != worldSpecific)
                                throw new InvalidOperationException("Game and World Settings must show different controls.");
                            (worldSpecific ? worldSettingsCategoryButton : gameSettingsCategoryButton)
                                .EmitSignal(BaseButton.SignalName.Pressed);
                            if (!settingsPanel.Visible || gameSettingsContent.Visible == worldSpecific ||
                                worldSettingsContent.Visible != worldSpecific)
                                throw new InvalidOperationException("Re-selecting a Settings category must leave its page open.");
                        }
                        else settingsPanel.Hide();
                        foreach (var selected in new[] { false, true, false })
                        {
                            selectedInhabitantCard.Visible = selected;
                            for (var frame = 0; frame < 5; frame++)
                            {
                                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            }
                            ApplyResponsiveLayout();
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            var menu = gameMenuPanel.GetGlobalRect();
                            var bounds = gameMenuPanel.GetParent<Control>().GetGlobalRect();
                            if (menu.GetCenter().DistanceTo(bounds.GetCenter()) > 2 || !bounds.Encloses(menu))
                            {
                                throw new InvalidOperationException($"Menu escaped its centered bounds: window={size}, settings={settingsVisible}, world={worldSpecific}, selected={selected}, menu={menu}, bounds={bounds}");
                            }
                            ShowWorldInfoPage(towns: true);
                            worldInfoPanel.Show();
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            if (!mapCanvas.GetGlobalRect().Encloses(worldInfoPanel.GetGlobalRect()))
                                throw new InvalidOperationException($"World Info escaped the world viewport: window={size} map={mapCanvas.GetGlobalRect()} info={worldInfoPanel.GetGlobalRect()} hud={hudBar.GetGlobalRect()}");
                            worldInfoPanel.Hide();
                        }
                    }
                }
            }
            gameMenuPanel.Hide();
            menuShade.Hide();
            if (topBarShade.Visible)
                throw new InvalidOperationException("Closing the Pause Menu must restore top-bar actions.");
            selectedInhabitantCard.Hide();
            var sampleResource = new OwnerWorldResource("wood", "construction", new(1, 1), false, "available", 8, 12, 0, 0, "spring");
            var sample = new OwnerWorldSnapshot("ui-test", 0, "ui-map", Enumerable.Range(0, 16)
                .Select(index => new OwnerWorldTile(index % 4, index / 4, "meadow")).ToArray(), [], [sampleResource], null, 0)
            {
                PackedTerrain = new OwnerWorldPackedTerrain(4, 4, "terrain-kind-v1",
                    Convert.ToBase64String(new byte[16])),
                PackedMapLayers = new OwnerWorldPackedMapLayers(4, 4, "map-layers-v1",
                    Convert.ToBase64String(Enumerable.Repeat((byte)2, 16).ToArray()),
                    Convert.ToBase64String(Enumerable.Repeat((byte)123, 16).ToArray()),
                    Convert.ToBase64String(new byte[16]),
                    Convert.ToBase64String(Enumerable.Repeat((byte)1, 16).ToArray()),
                    Convert.ToBase64String(Enumerable.Repeat((byte)3, 16).ToArray())),
                Towns = [new OwnerWorldTown("town:first", "First Town", "founding", 0,
                    ["founder:1", "founder:2", "founder:3", "founder:4"], [],
                    [new(0, 0), new(1, 0), new(0, 1), new(1, 1), new(2, 2), new(3, 3)])],
                TownLandTitles = [new("title:first", "town:first",
                    [new(0, 0), new(1, 0), new(0, 1), new(1, 1), new(2, 2), new(3, 3)], 0)],
                PlacedBuildings = [new("test-hall", "test-definition", new(0, 2), 0, "Test hall", ["shelter"], 2, 1)],
                ContentPackages = [new("owner-building-ui-test", "1.0.0", "sha256:test", "proposed", null, null, null, null,
                    "sha256:manifest", "Mira's shelter study", "builder-test")],
            };
            // A smoke run may load an existing installation's 12-hour or date
            // preference. Check explicit formats without saving over that choice.
            var installedClockPreferences = displayPreferences;
            try
            {
                foreach (var (twelveHour, expectedClock) in new[]
                         { (false, "01-02-0001 · 00:00"), (true, "01-02-0001 · 12:00 AM") })
                {
                    displayPreferences = installedClockPreferences with
                    {
                        UseTwelveHourClock = twelveHour,
                        DateFormat = "dmy",
                    };
                    Render(sample with { WorldTick = 3_600, CalendarPace = new OwnerWorldCalendarPace(360, 40) }, []);
                    if (clockLabel.Text != expectedClock ||
                        !worldInfoText.Text.Contains("40 days", StringComparison.Ordinal) ||
                        !TownListText().Contains("First Town", StringComparison.Ordinal) ||
                        !TownListText().Contains("4 residents · founding", StringComparison.Ordinal))
                        throw new InvalidOperationException("World Info must show the saved calendar and only the first Town's established founding, membership and border facts.");
                }
            }
            finally
            {
                displayPreferences = installedClockPreferences;
            }
            var civicTown = sample.Towns[0] with
            {
                FoundingState = "founded",
                Governance = new OwnerTownGovernance("representative", "none", ["Mira Vale", "Sol Reed", "Ash Rowan"],
                    7_200, 0, ["Mira Vale (full term)"],
                    [new("proposal-1", "law", "Keep public harvest records.", "pending", 1, 0, 2, 3_960),
                     new("proposal-2", "admission", "Admit Nia Moss.", "passed", 2, 0, 2, 3_600),
                     new("proposal-3", "law", "Close the public path.", "rejected", 0, 2, 2, 3_600),
                     new("proposal-4", "law", "Reserve storm fuel.", "cancelled", 1, 0, 2, 3_600)],
                    new("election-1", "regular", "runoff", 1, 3_960,
                        [new("candidate-1", "Nia Moss", 2), new("candidate-2", "Sol Reed", 1)], ["Mira Vale", "Ash Rowan"])),
            };
            Render(sample with { WorldTick = 3_600, CalendarPace = new OwnerWorldCalendarPace(360, 40), Towns = [civicTown] }, []);
            var civicLabels = TownListText();
            foreach (var phrase in new[] { "Council: elected representatives", "Mira Vale, Sol Reed, Ash Rowan", "Term ends ",
                         "Scheduled council election", "Runoff voting", "1 seat", "Sol Reed: 1 vote", "Nia Moss: 2 votes",
                         "1 yes / 0 no", "Pending law proposal", "Passed admission proposal",
                         "Rejected law proposal", "Cancelled law proposal" })
                if (!civicLabels.Contains(phrase, StringComparison.Ordinal))
                    throw new InvalidOperationException("The Town council rows must show readable names, current ballots and honest proposal states: " + phrase);
            if (civicLabels.Contains("1 seats", StringComparison.Ordinal) || civicLabels.Contains("1 votes", StringComparison.Ordinal) ||
                civicLabels.Contains("tick", StringComparison.OrdinalIgnoreCase) || civicLabels.Contains("candidate-", StringComparison.Ordinal) ||
                civicLabels.Contains("representative", StringComparison.Ordinal) && !civicLabels.Contains("elected representatives", StringComparison.Ordinal))
                throw new InvalidOperationException("Normal Town council rows must use world clocks and names rather than internal counters or IDs.");
            var revisedCivicTown = civicTown with
            {
                Governance = civicTown.Governance! with
                {
                    Proposals = civicTown.Governance!.Proposals.Select(p => p.Id == "proposal-1" ? p with { Status = "passed", Yes = 2 } : p).ToArray(),
                }
            };
            Render(sample with { WorldTick = 3_600, CalendarPace = new OwnerWorldCalendarPace(360, 40), Towns = [revisedCivicTown] }, []);
            if (!TownListText().Contains("Passed law proposal: Keep public harvest records.", StringComparison.Ordinal))
                throw new InvalidOperationException("A civic result must refresh its Town row even when Town membership is unchanged.");
            foreach (var (stage, phrase) in new[] { ("main", " · Voting · "), ("ready", "Representatives chosen") })
            {
                var electionTown = civicTown with
                {
                    Governance = civicTown.Governance! with
                    {
                        Election = civicTown.Governance!.Election! with { Kind = stage == "main" ? "initial" : "regular", Stage = stage },
                    },
                };
                Render(sample with { WorldTick = 3_600, Towns = [electionTown] }, []);
                var labels = TownListText();
                if (!labels.Contains(phrase, StringComparison.Ordinal) || labels.Contains(" · Main · ", StringComparison.Ordinal) ||
                    labels.Contains(" · Ready · ", StringComparison.Ordinal) || labels.Contains("Initial election", StringComparison.Ordinal) ||
                    stage == "ready" && (labels.Contains("Voting closes", StringComparison.Ordinal) ||
                        !labels.Contains("take office when the current term ends", StringComparison.Ordinal)))
                    throw new InvalidOperationException("The Towns page must explain voting and the pending handover in plain language.");
            }
            foreach (var (stage, phrase) in new[]
                     {
                         ("completed", "Last election completed: Mira Vale, Ash Rowan"),
                         ("failed", "The last election did not elect a supported council."),
                         ("cancelled", "The last election was cancelled."),
                     })
            {
                var settledTown = civicTown with
                {
                    Governance = civicTown.Governance! with
                    {
                        Election = null,
                        LatestElection = civicTown.Governance!.Election! with { Stage = stage },
                    },
                };
                Render(sample with { Towns = [settledTown] }, []);
                if (!TownListText().Contains(phrase, StringComparison.Ordinal) || TownListText().Contains("Voting closes", StringComparison.Ordinal))
                    throw new InvalidOperationException("The latest completed, failed or cancelled election must remain visible after voting closes.");
            }
            var secondCivicTown = civicTown with { Id = "town:second", Name = "Second Town" };
            var civicEventSnapshot = sample with { Towns = [civicTown, secondCivicTown] };
            foreach (var town in civicEventSnapshot.Towns)
            {
                foreach (var kind in new[] { "council", "election", "runoff", "proposal", "result", "cancelled" })
                {
                    var eventText = WorldEventText.Describe(new(1, 0, "town_civic_" + kind, town.Id + "|subject-id|notice"), civicEventSnapshot);
                    if (!eventText.Contains(town.Name, StringComparison.Ordinal) || eventText.Contains(town.Id, StringComparison.Ordinal) ||
                        kind == "council" && eventText.Contains("proposals", StringComparison.Ordinal) ||
                        kind == "result" && !eventText.Contains("Towns page", StringComparison.Ordinal))
                        throw new InvalidOperationException("Civic events must identify the Town, use the real page name and claim only known outcomes.");
                }
            }
            var civicWindowSize = displayWindow.Size;
            var civicRenderSize = displayWindow.ContentScaleSize;
            var civicPanelVisible = worldInfoPanel.Visible;
            try
            {
                var longProposals = Enumerable.Range(1, 8).Select(index => new OwnerCivicProposal(
                    "long-proposal-" + index, "law", $"Proposal {index}: " + string.Join(" ", Enumerable.Repeat("Keep clear public harvest records.", 7)),
                    index == 8 ? "passed" : "pending", 2, 0, 2, 3_960)).ToArray();
                var longCivicTown = civicTown with { Governance = civicTown.Governance! with { Proposals = longProposals } };
                foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
                {
                    displayWindow.Size = size;
                    displayWindow.ContentScaleSize = size;
                    Render(sample with { Towns = [longCivicTown, longCivicTown with { Id = "town:second", Name = "Second Town" }] }, []);
                    ShowWorldInfoPage(towns: true);
                    worldInfoPanel.Show();
                    townsScroll.ScrollVertical = 0;
                    for (var frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    ApplyResponsiveLayout();
                    for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var panelRect = worldInfoPanel.GetGlobalRect();
                    var scrollRect = townsScroll.GetGlobalRect();
                    if (!GetViewportRect().Grow(1).Encloses(panelRect) || !panelRect.Grow(1).Encloses(scrollRect) ||
                        townsScroll.GetVScrollBar().MaxValue <= townsScroll.GetVScrollBar().Page)
                        throw new InvalidOperationException($"Long civic results must stay in a screen-bounded, scrollable Towns page at {size}: panel={panelRect}, scroll={scrollRect}.");
                    townsScroll.ScrollVertical = (int)townsScroll.GetVScrollBar().MaxValue;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var lastCivicLabel = townList.GetChild<PanelContainer>(1)
                        .FindChildren("*", nameof(Label), recursive: true, owned: false).OfType<Label>()
                        .Single(label => label.Text.StartsWith("Council:", StringComparison.Ordinal));
                    var lastLineBottom = lastCivicLabel.GetGlobalRect().End.Y;
                    if (townsScroll.ScrollVertical == 0 || lastLineBottom < scrollRect.Position.Y || lastLineBottom > scrollRect.End.Y + 1)
                        throw new InvalidOperationException("Scrolling to the bottom must make the last Town's proposal result reachable.");
                    ShowWorldInfoPage(towns: false);
                    if (townsScroll.Visible || !worldInfoText.Visible)
                        throw new InvalidOperationException("The World page must replace the scrolling Towns contents.");
                }
            }
            finally
            {
                displayWindow.Size = civicWindowSize;
                displayWindow.ContentScaleSize = civicRenderSize;
                Render(sample, []);
                ShowWorldInfoPage(towns: true);
                worldInfoPanel.Visible = civicPanelVisible;
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                ApplyResponsiveLayout();
            }
            Render(sample, []);
            // Town rows are built after startup, so their text must still get the theme's sizes.
            VerifyPixelText("in rows added after startup");
            VerifyConsistentButtons();
            VerifyPanelParts();
            VerifyMapPanels();
            VerifyModelPicker();
            VerifyChildModelStatus();
            VerifyModelSetupCheckControls();
            Render(sample with { JevEnabled = true }, []);
            if (!jevAssistanceToggle.ButtonPressed)
                throw new InvalidOperationException("World Settings must reflect this world's saved Jev assistance choice.");
            Render(sample with { JevEnabled = false }, []);
            if (jevAssistanceToggle.ButtonPressed)
                throw new InvalidOperationException("World Settings must show when Jev assistance is off.");
            usageStatus = new OwnerUsageStatus(2, 1, 0, 1, 10, 3, 2, true,
                [new OwnerUsageRow("openai", "test-model", "planning", 2, 1, 0, 1, 10, 3)]);
            RenderUsageStatus();
            if (!usageMeterStatus.Text.Contains("2 of 2 calls used across all worlds", StringComparison.Ordinal) ||
                !usageMeterStatus.Text.Contains("Time is paused", StringComparison.Ordinal) ||
                !usageMeterStatus.Text.Contains("openai / test-model", StringComparison.Ordinal) ||
                !usageMeterStatus.TooltipText.Contains("for information only", StringComparison.Ordinal) ||
                !grantUsageCallsButton.Visible || usageAttemptLimitInput.Text != "2")
                throw new InvalidOperationException("Game Settings must present call attempts, scope, provider/model, tokens as information and explicit consent at the limit.");
            usageStatus = usageStatus with { Attempts = 812, AttemptLimit = 1_000, LimitReached = false };
            RenderUsageStatus();
            if (!usageMeterStatus.Text.StartsWith("812 of 1,000 calls used across all worlds.", StringComparison.Ordinal) ||
                usageMeterStatus.Text.Contains("Time is paused", StringComparison.Ordinal) || grantUsageCallsButton.Visible)
                throw new InvalidOperationException("Below the limit, Model calls must show grouped counts and offer no extra allowance.");
            usageStatus = usageStatus with { AccountingError = "Accounting unavailable. Restore a trusted backup and restart." };
            RenderUsageStatus();
            if (!usageMeterStatus.Text.Contains("Restore a trusted backup", StringComparison.Ordinal) ||
                usageMeterStatus.Text.Contains("calls used", StringComparison.Ordinal) ||
                grantUsageCallsButton.Visible || !applyUsageLimitButton.Disabled || usageAttemptLimitInput.Editable)
                throw new InvalidOperationException("Unavailable accounting must not display zero usage or offer a cap bypass.");
            usageStatus = null;
            RenderUsageStatus();
            Render(sample, []);
            Render(sample, []);
            if (worldDetails.GetParsedText().Length == 0)
                throw new InvalidOperationException("An unchanged refresh must keep the Town panel's stores and projects visible.");
            if (!worldDetails.GetParsedText().Contains("Shared stores", StringComparison.Ordinal) ||
                !worldDetails.GetParsedText().Contains("No one is working on a project right now.", StringComparison.Ordinal))
                throw new InvalidOperationException($"The Town panel must head its sections and say when nothing is under way instead of leaving gaps: {worldDetails.GetParsedText()}");
            RenderWorldDetails(sample with
            {
                Authoring = new OwnerWorldAuthoringState(false, 3, 7, 1, "initial-digest", "current-digest", "clear", "spring", []),
            });
            if (worldDetails.TooltipText.Length != 0 ||
                worldDetails.GetParsedText().Contains("digest", StringComparison.OrdinalIgnoreCase) ||
                worldDetails.GetParsedText().Contains("revision", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Town panel must not show operator diagnostics such as revisions or digests.");
            RenderWorldDetails(sample);
            // Top-bar panels hug their contents, and short text leaves no empty space below it.
            foreach (var panel in new PanelContainer[] { rosterPanel, eventsPanel, worldInfoPanel, filtersPanel, worldOverviewPanel })
            {
                panel.Show();
                for (var frame = 0; frame < 3; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (Math.Abs(panel.Size.Y - panel.GetCombinedMinimumSize().Y) > 1)
                    throw new InvalidOperationException($"{panel.Name} must fit its contents: size={panel.Size} contents={panel.GetCombinedMinimumSize()}.");
                foreach (var text in panel.FindChildren("*", nameof(RichTextLabel), recursive: true, owned: false).OfType<RichTextLabel>())
                    if (text.IsVisibleInTree() && text.CustomMinimumSize.Y > Math.Max(UiFonts.Body * 2, text.GetContentHeight()) + 1)
                        throw new InvalidOperationException($"{panel.Name} must not reserve empty space below its text: height={text.CustomMinimumSize.Y} text={text.GetContentHeight()}.");
                panel.Hide();
            }
            foreach (var panel in new PanelContainer[] { rosterPanel, eventsPanel, worldInfoPanel, filtersPanel, worldOverviewPanel })
            {
                panel.Show();
                var close = panel.FindChildren("*", nameof(Button), recursive: true, owned: false)
                    .OfType<Button>().FirstOrDefault(button => ShowsGlyph(button, PixelGlyph.Close));
                if (close is null || close.FocusMode == Control.FocusModeEnum.None)
                    throw new InvalidOperationException($"{panel.Name} must have a keyboard-reachable close button in its heading.");
                close.GrabFocus();
                close.EmitSignal(BaseButton.SignalName.Pressed);
                if (panel.Visible || close.HasFocus())
                    throw new InvalidOperationException($"{panel.Name} must close and release focus when its close button is pressed.");
            }
            SetStatus("Action result check", good: true);
            ExpireStatusToast(refreshSucceeded: true);
            if (!statusToast.Visible)
                throw new InvalidOperationException("A new action result must survive the next observation refresh.");
            statusToastShownAtMsec -= StatusToastMilliseconds;
            ExpireStatusToast(refreshSucceeded: false);
            if (statusToast.Visible)
                throw new InvalidOperationException("An action result must clear after its reading time.");
            ShowHeldState("connection check");
            ExpireStatusToast(refreshSucceeded: false);
            if (!statusLabel.Text.StartsWith("Connection lost", StringComparison.Ordinal) ||
                statusLabel.Text.Contains("tick", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Connection problems must be described without internal tick numbers.");
            if (!statusToast.Visible)
                throw new InvalidOperationException("A connection problem must stay visible until a refresh succeeds.");
            ExpireStatusToast(refreshSucceeded: true);
            if (statusToast.Visible)
                throw new InvalidOperationException("A successful refresh must clear a connection problem.");
            choosingFirstTownSite = true;
            SetStatus("Map mode check", good: true, StatusToastKind.Sticky);
            statusToastShownAtMsec -= StatusToastMilliseconds;
            ExpireStatusToast(refreshSucceeded: true);
            if (!statusToast.Visible)
                throw new InvalidOperationException("Map-click instructions must stay visible while their mode is active.");
            RenderFounderSetup(sample with { FounderSetup = new OwnerFounderSetup(4, 0, false) { CanChooseTownSite = true } });
            if (townSiteButton.Text != "Cancel Town site")
                throw new InvalidOperationException("An active Town-site selection must show how to cancel it.");
            choosingFirstTownSite = false;
            ExpireStatusToast(refreshSucceeded: true);
            if (statusToast.Visible)
                throw new InvalidOperationException("Map-click instructions must clear after their mode ends.");
            Render(sample with
            {
                FounderSetup = new OwnerFounderSetup(4, 0, false)
                {
                    CanChooseTownSite = true,
                }
            }, []);
            if (!townSiteButton.Visible || townSiteButton.Text != "Choose Town site" ||
                !founderSetupButton.Disabled)
                throw new InvalidOperationException("Paused New World must offer Town-site selection before founders.");
            Render(sample with
            {
                FounderSetup = new OwnerFounderSetup(4, 0, false)
                {
                    CanChooseTownSite = true,
                    HasAcceptedTownSite = true,
                }
            }, []);
            if (!townSiteButton.Visible || townSiteButton.Text != "Redo Town site")
                throw new InvalidOperationException("Accepted first Town must offer a redo before founders.");
            Render(sample with
            {
                FounderSetup = new OwnerFounderSetup(4, 2, false)
                {
                    LastFounderId = "founder:00000000000000000000000000000002",
                },
            }, []);
            founderSetupPanel.Show();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!founderSetupButton.Visible || !founderSetupButton.Text.Contains("2/4", StringComparison.Ordinal) ||
                !startWorldButton.Visible || !startWorldButton.Disabled ||
                !undoFounderButton.Visible || !founderHudRow.Visible ||
                HudButtons().Where(button => button.IsVisibleInTree())
                    .Any(button => !mapCanvas.GetGlobalRect().Encloses(button.GetGlobalRect())) ||
                !mapCanvas.GetGlobalRect().Encloses(founderSetupPanel.GetGlobalRect()))
                throw new InvalidOperationException("Founder setup must keep every HUD action inside the world view and Start World gated.");
            founderSetupPanel.Hide();
            Render(sample with { FounderSetup = new OwnerFounderSetup(4, 4, true) }, []);
            if (!addAgentButton.Visible || founderSetupButton.Visible || startWorldButton.Visible ||
                undoFounderButton.Visible)
                throw new InvalidOperationException("Started worlds must offer Add Agent instead of founder setup controls.");
            Render(sample, []);
            RenderModLibrary(sample);
            if (!modLibraryContents.Text.Contains("proposed by builder-test", StringComparison.Ordinal))
                throw new InvalidOperationException("Mod Library must show existing agent proposal provenance.");
            RenderMap(sample);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!terrainLayer.DrawsGroundTextures)
                throw new InvalidOperationException("Zoomed-in terrain must draw pixel-art ground textures.");
            var inspectClick = mapStage.Position + new Vector2(currentTileSize * 1.5f,
                currentTileSize * 1.5f);
            HandleMapInput(new InputEventMouseButton
            {
                Position = inspectClick,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            });
            if (!selectedTilePanel.Visible || terrainLayer.SelectedTile != new Vector2I(1, 1) ||
                !selectedTileText.Text.Contains("8 available", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Climate: Temperate", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Elevation: 123/255", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Terrain: Meadow", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Surface: Sand", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Vegetation: Scrub", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Town: First Town", StringComparison.Ordinal) ||
                selectedTileText.Text.Contains("Fertility", StringComparison.Ordinal) ||
                selectedTileText.Text.Contains("unavailable", StringComparison.Ordinal) ||
                selectedTileText.Text.Contains("none", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Selected-tile inspection must show available map facts without empty placeholders.");
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!mapCanvas.GetGlobalRect().Encloses(selectedTilePanel.GetGlobalRect()))
                throw new InvalidOperationException($"Selected-tile inspection must open inside the world view: map={mapCanvas.GetGlobalRect()} card={selectedTilePanel.GetGlobalRect()}.");
            // The card names the ground and lists each fact in plain words; climate has its own row.
            if (tileTitle.Text != "Meadow" || tileSubtitle.Text != "Tile 1, 1" ||
                !TileCardText().Contains("Climate\nTemperate", StringComparison.Ordinal) ||
                !TileCardText().Contains("Height\nMiddle · 123 of 255", StringComparison.Ordinal) ||
                !TileCardText().Contains("Town\nFirst Town", StringComparison.Ordinal) ||
                selectedTilePanel.GetCombinedMinimumSize().Y > selectedTilePanel.Size.Y + 1)
                throw new InvalidOperationException("The tile card must name the ground, give climate its own row and fit its facts: " + TileCardText());
            var ownedMap = sample with
            {
                PlacedBuildings = [.. sample.PlacedBuildings,
                    new("test-house", "house", new(2, 2), 0, "House", ["shelter"], 2, 1,
                        HouseholdId: "household:one")],
                Stockpiles =
                [
                    new("household:one", "Founder's household", []),
                    new("household:two", "Other household", []),
                ],
                HouseholdLandUseRights = [new("right:one", "town:first", "household:one",
                    [new(2, 2)], 0, "starter_allocation", null),
                    new("right:disputed", "town:first", "household:one",
                        [new(1, 1)], 0, "starter_allocation", null)],
                HouseholdLandUseRequests =
                [
                    new("request:disputed", "town:first", "household:two", "founder:4",
                        [new(1, 1)], 1, null, true,
                        ["household:one", "household:two"], [new(1, 1)]),
                    new("request:open", "town:first", "household:two", "founder:4",
                        [new(3, 3)], 1, null, false, ["household:two"], []),
                ],
            };
            RenderMap(ownedMap);
            if (townBorderFilter.ButtonPressed || householdPropertyFilter.ButtonPressed ||
                townLandTitleFilter.ButtonPressed || householdLandUseFilter.ButtonPressed || disputedLandFilter.ButtonPressed ||
                terrainLayer.TownBorderTileCount != 0 || terrainLayer.HouseholdPropertyTileCount != 0 ||
                terrainLayer.TownLandTitleTileCount != 0 || terrainLayer.HouseholdLandUseTileCount != 0 ||
                terrainLayer.DisputedLandTileCount != 0 ||
                !townBorderHint.Text.Contains("Town borders are hidden", StringComparison.Ordinal))
                throw new InvalidOperationException("Map Filters must start off, with no Town or household land records drawn.");
            filtersButton.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!filtersPanel.Visible || !mapCanvas.GetGlobalRect().Encloses(filtersPanel.GetGlobalRect()))
                throw new InvalidOperationException($"Map Filters must open inside the world view: map={mapCanvas.GetGlobalRect()} filters={filtersPanel.GetGlobalRect()} site_visible={townSiteButton.Visible}.");
            townBorderFilter.ButtonPressed = true;
            if (terrainLayer.TownBorderTileCount == 0 ||
                !townBorderHint.Text.Contains("dashed line", StringComparison.Ordinal))
                throw new InvalidOperationException("Turning on Town borders must draw them and explain the dashed line.");
            townBorderFilter.ButtonPressed = false;
            townLandTitleFilter.ButtonPressed = true;
            if (terrainLayer.TownLandTitleTileCount == 0)
                throw new InvalidOperationException("Turning on Town land title must tint and outline titled land.");
            townLandTitleFilter.ButtonPressed = false;
            householdLandUseFilter.ButtonPressed = true;
            if (terrainLayer.HouseholdLandUseTileCount < 2 || terrainLayer.DisputedLandTileCount != 0)
                throw new InvalidOperationException("The household land-use filter must show recorded rights and pending requests separately from disputes.");
            householdLandUseFilter.ButtonPressed = false;
            disputedLandFilter.ButtonPressed = true;
            if (terrainLayer.DisputedLandTileCount != 1)
                throw new InvalidOperationException("The disputed-land filter must show only the tiles with competing claims.");
            disputedLandFilter.ButtonPressed = false;
            householdPropertyFilter.ButtonPressed = true;
            if (terrainLayer.TownBorderTileCount != 0 || terrainLayer.HouseholdPropertyTileCount == 0 ||
                !townBorderHint.Text.Contains("Town borders are hidden", StringComparison.Ordinal))
                throw new InvalidOperationException("The Town border filter must update the map and its visible explanation.");
            selectedTile = new Vector2I(2, 2);
            selectedTilePanel.Show();
            RenderTileInspection(ownedMap);
            // The visible card, not only the hidden plain text, must carry the land facts.
            if (!TileCardText().Contains("Household\nFounder's household", StringComparison.Ordinal) ||
                !TileCardText().Contains("Land title\nFirst Town", StringComparison.Ordinal) ||
                !TileCardText().Contains("Use right\nFounder's household", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Town land title: First Town", StringComparison.Ordinal))
                throw new InvalidOperationException("The tile card must show the household property, its use right and Town title: " + TileCardText());
            selectedTile = new Vector2I(1, 1);
            RenderTileInspection(ownedMap);
            if (!TileCardText().Contains("Use right\nFounder's household", StringComparison.Ordinal) ||
                !TileCardText().Contains("Use request\nOther household", StringComparison.Ordinal) ||
                !TileCardText().Contains("Disputed\nFounder's household; Other household", StringComparison.Ordinal) ||
                !selectedTileText.Text.Contains("Disputed household claims: Founder's household; Other household", StringComparison.Ordinal))
                throw new InvalidOperationException("The tile card must list each household's use claim and the dispute: " + TileCardText());
            await VerifyBuildingCardsAsync(ownedMap);
            RenderMap(ownedMap);
            householdPropertyFilter.ButtonPressed = false;
            var placementMapStagePosition = mapStage.Position;
            placingAddedAgent = true;
            founderSetupPanel.Show();
            if (townBorderFilter.ButtonPressed || householdPropertyFilter.ButtonPressed ||
                townLandTitleFilter.ButtonPressed || householdLandUseFilter.ButtonPressed || disputedLandFilter.ButtonPressed ||
                terrainLayer.TownBorderTileCount == 0 || terrainLayer.HouseholdPropertyTileCount == 0 ||
                terrainLayer.TownLandTitleTileCount == 0 || terrainLayer.HouseholdLandUseTileCount == 0 ||
                terrainLayer.DisputedLandTileCount == 0)
                throw new InvalidOperationException("Add Agent placement must show recorded Town and household claims without switching Filters on.");
            ResetAddAgentPlacementHint();
            for (var frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var placementFields = new Control[] { founderProviderChoice, founderCredentialChoice,
                founderKeyLabelInput, founderApiKeyInput, founderModelPicker };
            var placementFieldRects = placementFields.Select(field => field.GetGlobalRect()).ToArray();
            void HoverPlacementTile(int x, int y)
            {
                // Pin this fixture tile to clear map space before hovering it.
                // The four-tile map can otherwise sit underneath Add Agent,
                // and headless windows may clamp requested physical sizes.
                var pointer = new Vector2(24, mapCanvas.Size.Y - 24);
                var globalPointer = mapCanvas.GetGlobalTransform() * pointer;
                if (!mapCanvas.GetGlobalRect().HasPoint(globalPointer) || founderSetupPanel.GetGlobalRect().HasPoint(globalPointer))
                    throw new InvalidOperationException($"Placement hover must target visible map outside Add Agent: tile={x},{y}, pointer={globalPointer}, panel={founderSetupPanel.GetGlobalRect()}.");
                mapStage.Position = pointer - new Vector2(x * (currentTileSize + TileGap) + currentTileSize / 2f,
                    y * (currentTileSize + TileGap) + currentTileSize / 2f);
                UpdateTileHover(pointer);
            }
            HoverPlacementTile(2, 2);
            if (!founderSetupHint.Text.Contains("Household: Founder's household · Town: First Town", StringComparison.Ordinal))
                throw new InvalidOperationException("Add Agent must preview the recorded household use right and Town membership: " + founderSetupHint.Text);
            HoverPlacementTile(0, 0);
            if (!founderSetupHint.Text.Contains("Household: none · Town: First Town", StringComparison.Ordinal))
                throw new InvalidOperationException("Town land without a household use right must not give Add Agent household membership.");
            HoverPlacementTile(3, 3);
            if (!founderSetupHint.Text.Contains("Household: none · Town: First Town", StringComparison.Ordinal))
                throw new InvalidOperationException("A single pending use request must not give Add Agent household membership.");
            HoverPlacementTile(3, 0);
            if (!founderSetupHint.Text.Contains("Household: new independent household · Town: no Town", StringComparison.Ordinal))
                throw new InvalidOperationException("Unclaimed land must preview a new independent household.");

            PreviewAddAgentPlacement(ownedMap with { Resources = [] }, new Vector2I(1, 1));
            if (!founderSetupHint.Text.Contains("overlap here", StringComparison.Ordinal) ||
                !founderSetupHint.Text.Contains("Choose", StringComparison.Ordinal))
                throw new InvalidOperationException("Add Agent must refuse a tile with disputed household land-use claims.");

            PreviewAddAgentPlacement(ownedMap with
            {
                HouseholdLandUseRights = [.. ownedMap.HouseholdLandUseRights,
                    new("right:empty", "town:first", "household:two", [new(0, 0)], 0, "starter_allocation", null)],
            }, new Vector2I(0, 0));
            if (!founderSetupHint.Text.Contains("Household: Other household · Town: First Town", StringComparison.Ordinal))
                throw new InvalidOperationException($"A use right on empty Town land must give Add Agent that household; preview was '{founderSetupHint.Text}'.");
            PreviewAddAgentPlacement(ownedMap with
            {
                Fields = [new(new(0, 0), "household:one", "growing", "cultivated_greens", 67, null, null)],
                HouseholdLandUseRights = [.. ownedMap.HouseholdLandUseRights,
                    new("right:tilled", "town:first", "household:two", [new(0, 0)], 0, "starter_allocation", null)],
            }, new Vector2I(0, 0));
            if (!founderSetupHint.Text.Contains("overlap here", StringComparison.Ordinal))
                throw new InvalidOperationException($"A field must not override another household's use right; preview was '{founderSetupHint.Text}'.");
            PreviewAddAgentPlacement(ownedMap with
            {
                HouseholdLandUseRights = [new("right:previous", "town:first", "household:two",
                    [new(2, 2)], 0, "starter_allocation", null)],
                HouseholdLandUseRequests = [],
            }, new Vector2I(2, 2));
            if (!founderSetupHint.Text.Contains("Household: Founder's household · Town: First Town", StringComparison.Ordinal))
                throw new InvalidOperationException($"A building's owner must come before another household's use right; preview was '{founderSetupHint.Text}'.");

            var overlappingProperties = ownedMap with
            {
                PlacedBuildings = [.. ownedMap.PlacedBuildings,
                    new("other-house", "house", new(2, 2), 0, "Other House", ["house"], 1, 1,
                        HouseholdId: "household:two")],
            };
            PreviewAddAgentPlacement(overlappingProperties, new Vector2I(2, 2));
            if (!founderSetupHint.Text.Contains("Household property or land claims overlap", StringComparison.Ordinal) ||
                !founderSetupHint.Text.Contains("Choose", StringComparison.Ordinal))
                throw new InvalidOperationException($"Add Agent must refuse two households' conflicting land claims; preview was '{founderSetupHint.Text}'.");

            var secondTown = sample.Towns[0] with { Id = "town:second", Name = "Second Town" };
            PreviewAddAgentPlacement(sample with { Towns = [.. sample.Towns, secondTown] }, new Vector2I(0, 0));
            if (!founderSetupHint.Text.Contains("Town borders overlap", StringComparison.Ordinal) ||
                !founderSetupHint.Text.Contains("Choose", StringComparison.Ordinal))
                throw new InvalidOperationException("Add Agent must refuse a tile inside two Town borders.");

            PreviewAddAgentPlacement(ownedMap, new Vector2I(2, 2));
            for (var frame = 0; frame < 3; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (placementFields.Where((field, index) => field.GetGlobalRect() != placementFieldRects[index]).Any())
                throw new InvalidOperationException("Add Agent fields must stay in place when the introductory hint changes to a placement preview.");
            var placementPreview = founderSetupHint.Text;
            UpdateTileHover(mapCanvas.GetGlobalTransform().AffineInverse() *
                (founderSetupPanel.GetGlobalRect().Position + founderSetupPanel.GetGlobalRect().Size / 2));
            if (founderSetupHint.Text != placementPreview)
                throw new InvalidOperationException("A pointer inside Add Agent must keep the last visible placement preview.");
            founderModelPicker.Choice.GetPopup().Popup();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            HoverPlacementTile(0, 0);
            if (founderSetupHint.Text != placementPreview)
                throw new InvalidOperationException("An open model popup must not preview the map behind it.");
            founderModelPicker.Choice.GetPopup().Hide();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            HoverPlacementTile(0, 0);
            if (!founderSetupHint.Text.Contains("Household: none · Town: First Town", StringComparison.Ordinal))
                throw new InvalidOperationException("Closing the model popup must resume map placement previews.");
            founderSetupPanel.Hide();
            placingAddedAgent = false;
            mapStage.Position = placementMapStagePosition;
            if (terrainLayer.TownBorderTileCount != 0 || terrainLayer.HouseholdPropertyTileCount != 0 ||
                terrainLayer.TownLandTitleTileCount != 0 || terrainLayer.HouseholdLandUseTileCount != 0 ||
                terrainLayer.DisputedLandTileCount != 0)
                throw new InvalidOperationException("Leaving Add Agent placement must hide land overlays the Filters leave off.");
            filtersButton.EmitSignal(BaseButton.SignalName.Pressed);
            RenderMap(sample);
            selectedTile = new Vector2I(1, 1);
            terrainLayer.SetSelectedTile(selectedTile);
            RenderTileInspection(sample);
            var renderedSurface = terrainMap?.DisplayColorAt(1, 1) ?? Colors.Transparent;
            var expectedSurface = new Color("AA985F");
            if (terrainMap?.SurfaceAt(1, 1) != 1 ||
                Math.Abs(renderedSurface.R - expectedSurface.R) > 0.001f ||
                Math.Abs(renderedSurface.G - expectedSurface.G) > 0.001f ||
                Math.Abs(renderedSurface.B - expectedSurface.B) > 0.001f)
                throw new InvalidOperationException("The rendered map must use its separate surface and vegetation layers.");
            var testHydrology = new byte[16];
            var testSurfaces = Enumerable.Repeat((byte)0, 16).ToArray();
            testHydrology[6] = 3;
            testSurfaces[6] = 4;
            var testLayers = new OwnerWorldPackedMapLayers(4, 4, "map-layers-v2",
                Convert.ToBase64String(Enumerable.Repeat((byte)2, 16).ToArray()),
                Convert.ToBase64String(Enumerable.Repeat((byte)123, 16).ToArray()),
                Convert.ToBase64String(testHydrology), Convert.ToBase64String(testSurfaces),
                Convert.ToBase64String(Enumerable.Repeat((byte)1, 16).ToArray()));
            var transitionMap = WorldTerrainMap.FromTiles(sample.Tiles, 4, 4, testLayers);
            if ((transitionMap.WaterEdgeMaskAt(1, 1, false) & 2) == 0 ||
                (transitionMap.SurfaceBoundaryMaskAt(1, 1, false) & 2) == 0)
                throw new InvalidOperationException("Generated water and ground changes must expose functional tile-edge transitions.");
            var coastPieces = new List<(TerrainStyle Style, int Piece)>();
            var edgeProbe = new List<(TerrainStyle Style, int Piece)>();
            TerrainTransitions.CollectCoast(transitionMap, 2, 1, false, coastPieces);
            var riverSides = coastPieces.Count(item => item.Piece < TerrainTransitions.OuterCornerPiece(0, 0));
            TerrainTransitions.CollectCoast(transitionMap, 1, 1, false, edgeProbe);
            if (transitionMap.StyleAt(2, 1) != TerrainStyle.River || riverSides != 4 || coastPieces.Count != 8 || edgeProbe.Count != 0)
                throw new InvalidOperationException($"A river tile surrounded by land must take a rounded bank on every side and corner, and land tiles none: {coastPieces.Count} pieces.");
            foreach (var atlasSize in new[] { 16, 32 })
                for (var start = 0; start < TerrainTransitions.Levels; start++)
                    for (var end = 0; end < TerrainTransitions.Levels; end++)
                    {
                        var piece = TerrainTransitions.EdgePiece(0, start, end, 0);
                        var land = CoastEdges.Piece(CoastEdges.LandRow, piece, atlasSize);
                        var shallow = CoastEdges.Piece(CoastEdges.ShallowRow, piece, atlasSize);
                        var foam = CoastEdges.Piece(CoastEdges.FoamRow, piece, atlasSize);
                        var last = atlasSize - 1;
                        var band = CoastEdges.Band(false, atlasSize);
                        int Depth(Image image, int column)
                        {
                            var depth = 0;
                            while (depth < atlasSize && image.GetPixel(column, depth).A > 0) depth++;
                            return depth;
                        }
                        if (Depth(land, 0) != TerrainTransitions.Reach(start, atlasSize) ||
                            Depth(land, last) != TerrainTransitions.Reach(end, atlasSize) ||
                            Depth(shallow, 0) != TerrainTransitions.Reach(start, atlasSize) + band ||
                            Depth(shallow, last) != TerrainTransitions.Reach(end, atlasSize) + band)
                            throw new InvalidOperationException($"{atlasSize}px shores must meet each tile corner at its shared reach, with the shallow band just beyond.");
                        for (var column = 0; column < atlasSize; column++)
                            for (var row = 0; row < atlasSize; row++)
                                if (foam.GetPixel(column, row).A > 0 && land.GetPixel(column, row).A > 0)
                                    throw new InvalidOperationException($"{atlasSize}px foam must lie along the land's edge, not on it.");
                    }
            // A tile's pixels packed as RGBA, row by row, for exact comparisons.
            static uint[] Packed(Image tile)
            {
                var data = tile.GetData();
                var pixels = new uint[data.Length / 4];
                for (var index = 0; index < pixels.Length; index++)
                    pixels[index] = (uint)data[index * 4] << 24 | (uint)data[index * 4 + 1] << 16 | (uint)data[index * 4 + 2] << 8 | data[index * 4 + 3];
                return pixels;
            }
            // Pixels that differ between line `lineA` of one tile and line `lineB` of another.
            static int Mismatches(uint[] a, int lineA, uint[] b, int lineB, int size, bool columns)
            {
                var count = 0;
                for (var along = 0; along < size; along++)
                    if (columns ? a[along * size + lineA] != b[along * size + lineB] : a[lineA * size + along] != b[lineB * size + along])
                        count++;
                return count;
            }
            // Lone pixels: one colour set alone among eight neighbours of another single colour.
            static int LonePixels(uint[] pixels, int size)
            {
                var lone = 0;
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var around = pixels[y * size + (x + 1) % size];
                        var alone = around != pixels[y * size + x];
                        for (var dy = -1; dy <= 1 && alone; dy++)
                            for (var dx = -1; dx <= 1 && alone; dx++)
                                alone = (dx == 0 && dy == 0) || pixels[(y + dy + size) % size * size + (x + dx + size) % size] == around;
                        if (alone) lone++;
                    }
                return lone;
            }
            foreach (var atlasSize in new[] { 16, 32 })
                foreach (var style in Enum.GetValues<TerrainStyle>())
                {
                    var baseColor = TerrainTextures.BaseColor(style);
                    Image[] tiles = [TerrainTextures.Tile(style, 0, atlasSize), TerrainTextures.Tile(style, 1, atlasSize)];
                    if (style != TerrainStyle.Unknown && tiles[0].GetData().SequenceEqual(tiles[1].GetData()))
                        throw new InvalidOperationException($"{style} needs two distinct texture variants.");
                    if (style is TerrainStyle.Mountain or TerrainStyle.Peak)
                    {
                        // Mountains and peaks are relief shapes kept inside their
                        // tile, so mountain tiles meet on plain ground without seams.
                        foreach (var texture in tiles)
                        {
                            var relief = 0;
                            for (var ty = 0; ty < atlasSize; ty++)
                                for (var tx = 0; tx < atlasSize; tx++)
                                    if (!texture.GetPixel(tx, ty).IsEqualApprox(baseColor)) relief++;
                            if (relief > atlasSize * atlasSize * 0.4f)
                                throw new InvalidOperationException($"{style} {atlasSize}px texture is too busy: {relief} relief pixels.");
                            for (var edge = 0; edge < atlasSize; edge++)
                                if (!texture.GetPixel(edge, 0).IsEqualApprox(baseColor) || !texture.GetPixel(0, edge).IsEqualApprox(baseColor))
                                    throw new InvalidOperationException($"{style} {atlasSize}px relief must stay off tile edges so neighbors join without seams.");
                        }
                        continue;
                    }
                    // Calm ground: soft patches and a few small motifs that keep
                    // the overview colour on average, never per-pixel grain.
                    var pixels = tiles.Select(Packed).ToArray();
                    foreach (var tile in pixels)
                    {
                        var (red, green, blue) = (0f, 0f, 0f);
                        foreach (var pixel in tile)
                        {
                            red += (pixel >> 24) / 255f;
                            green += (pixel >> 16 & 0xFF) / 255f;
                            blue += (pixel >> 8 & 0xFF) / 255f;
                        }
                        var count = tile.Length;
                        if (Math.Abs(red / count - baseColor.R) > 0.04f || Math.Abs(green / count - baseColor.G) > 0.04f ||
                            Math.Abs(blue / count - baseColor.B) > 0.04f)
                            throw new InvalidOperationException($"{style} {atlasSize}px ground must average within 4% of its overview colour.");
                        var lone = LonePixels(tile, atlasSize);
                        if (lone > atlasSize / 4)
                            throw new InvalidOperationException($"{style} {atlasSize}px ground is grainy: {lone} lone pixels.");
                    }
                    // Seamless: patches may run across an edge only if they carry
                    // on at the opposite edge, so where any two tiles meet, in
                    // either variant, the join is no rougher than a line inside one.
                    var last = atlasSize - 1;
                    var roughestColumn = 0;
                    var roughestRow = 0;
                    foreach (var tile in pixels)
                        for (var line = 0; line < last; line++)
                        {
                            roughestColumn = Math.Max(roughestColumn, Mismatches(tile, line, tile, line + 1, atlasSize, columns: true));
                            roughestRow = Math.Max(roughestRow, Mismatches(tile, line, tile, line + 1, atlasSize, columns: false));
                        }
                    // A tile's east edge against its neighbour's west edge, and its south edge against the neighbour's north edge.
                    foreach (var tile in pixels)
                        foreach (var neighbour in pixels)
                            if (Mismatches(tile, last, neighbour, 0, atlasSize, columns: true) > roughestColumn ||
                                Mismatches(tile, last, neighbour, 0, atlasSize, columns: false) > roughestRow)
                                throw new InvalidOperationException($"{style} {atlasSize}px ground must join its neighbours without a seam.");
                }
            foreach (var atlasSize in new[] { 16, 32 })
                foreach (var style in new[] { TerrainStyle.Ocean, TerrainStyle.Lake, TerrainStyle.River, TerrainStyle.ShallowWater })
                {
                    // Water repeats as one larger block: it must average near
                    // the flat color that the overview and shore bands use, and
                    // its tiles must differ so no tile grid shows.
                    var waterBlock = WaterTextures.Block(style, atlasSize);
                    var waterBase = TerrainTextures.BaseColor(style);
                    var pixels = waterBlock.GetWidth() * waterBlock.GetHeight();
                    var (red, green, blue) = (0f, 0f, 0f);
                    for (var py = 0; py < waterBlock.GetHeight(); py++)
                        for (var px = 0; px < waterBlock.GetWidth(); px++)
                        {
                            var pixel = waterBlock.GetPixel(px, py);
                            red += pixel.R;
                            green += pixel.G;
                            blue += pixel.B;
                        }
                    if (Math.Abs(red / pixels - waterBase.R) > 0.03f || Math.Abs(green / pixels - waterBase.G) > 0.03f ||
                        Math.Abs(blue / pixels - waterBase.B) > 0.03f)
                        throw new InvalidOperationException($"{style} {atlasSize}px water must average close to its base color.");
                    var distinctTiles = new HashSet<string>();
                    for (var ty = 0; ty < WaterTextures.BlockTiles; ty++)
                        for (var tx = 0; tx < WaterTextures.BlockTiles; tx++)
                            distinctTiles.Add(Convert.ToBase64String(waterBlock.GetRegion(
                                new Rect2I(tx * atlasSize, ty * atlasSize, atlasSize, atlasSize)).GetData()));
                    if (distinctTiles.Count < WaterTextures.BlockTiles ||
                        WaterTextures.Region(style, WaterTextures.BlockTiles, -WaterTextures.BlockTiles, atlasSize) !=
                        WaterTextures.Region(style, 0, 0, atlasSize))
                        throw new InvalidOperationException($"{style} water must vary between tiles and repeat only as a whole block.");
                }
            if (!TerrainTransitions.Overlaps(TerrainStyle.Grass, TerrainStyle.Sand) ||
                TerrainTransitions.Overlaps(TerrainStyle.Sand, TerrainStyle.Grass) ||
                !TerrainTransitions.Overlaps(TerrainStyle.Snow, TerrainStyle.Rock) ||
                TerrainTransitions.Overlaps(TerrainStyle.Peak, TerrainStyle.Mountain) ||
                TerrainTransitions.Overlaps(TerrainStyle.Grass, TerrainStyle.Ocean) ||
                TerrainTransitions.Overlaps(TerrainStyle.Lake, TerrainStyle.Sand))
                throw new InvalidOperationException("Land edges must follow the surface order and leave water to its shoreline pieces.");
            // Pixels covered from one edge inward along a line, stopping at the first gap.
            static int Reached(Image piece, int startX, int startY, int stepX, int stepY)
            {
                var count = 0;
                for (int px = startX, py = startY; px >= 0 && py >= 0 && px < piece.GetWidth() && py < piece.GetHeight(); px += stepX, py += stepY, count++)
                    if (piece.GetPixel(px, py).A <= 0) break;
                return count;
            }
            foreach (var atlasSize in new[] { 16, 32 })
                foreach (var over in new[] { TerrainStyle.Grass, TerrainStyle.Snow })
                {
                    var last = atlasSize - 1;
                    var depths = new HashSet<int>();
                    for (var start = 0; start < TerrainTransitions.Levels; start++)
                        for (var end = 0; end < TerrainTransitions.Levels; end++)
                            for (var variant = 0; variant < TerrainTransitions.EdgeVariants; variant++)
                            {
                                var north = TerrainTransitions.Piece(over, TerrainTransitions.EdgePiece(0, start, end, variant), atlasSize);
                                var west = TerrainTransitions.Piece(over, TerrainTransitions.EdgePiece(3, start, end, variant), atlasSize);
                                if (Reached(north, 0, 0, 0, 1) != TerrainTransitions.Reach(start, atlasSize) ||
                                    Reached(north, last, 0, 0, 1) != TerrainTransitions.Reach(end, atlasSize) ||
                                    Reached(west, 0, 0, 1, 0) != TerrainTransitions.Reach(start, atlasSize) ||
                                    Reached(west, 0, last, 1, 0) != TerrainTransitions.Reach(end, atlasSize))
                                    throw new InvalidOperationException($"{over} {atlasSize}px edges must meet each tile corner at that corner's shared reach.");
                                for (var column = 0; column < atlasSize; column++)
                                {
                                    for (var row = TerrainTransitions.MaximumReach(atlasSize); row < atlasSize; row++)
                                        if (north.GetPixel(column, row).A > 0)
                                            throw new InvalidOperationException($"{over} {atlasSize}px edges must stay within a quarter of the tile so it still reads as a square.");
                                    depths.Add(Reached(north, column, 0, 0, 1));
                                }
                            }
                    if (depths.Count < 4)
                        throw new InvalidOperationException($"{over} {atlasSize}px edges must wander rather than run in straight lines.");
                    for (var level = 0; level < TerrainTransitions.Levels; level++)
                    {
                        var corner = TerrainTransitions.Piece(over, TerrainTransitions.OuterCornerPiece(3, level), atlasSize);
                        var reach = TerrainTransitions.Reach(level, atlasSize);
                        if (Reached(corner, 0, 0, 1, 0) != reach || Reached(corner, 0, 0, 0, 1) != reach || corner.GetPixel(last, last).A > 0)
                            throw new InvalidOperationException($"{over} {atlasSize}px outer corners must meet both neighboring edges at the corner's reach.");
                    }
                }
            var edgeMap = WorldTerrainMap.FromTiles(
                Enumerable.Range(0, 9).Select(index => new OwnerWorldTile(index % 3, index / 3, index == 4 ? "meadow" : "sand")).ToArray(), 3, 3);
            var edgePieces = new List<(TerrainStyle Style, int Piece)>();
            TerrainTransitions.Collect(edgeMap, 1, 0, false, edgePieces);
            var southLevels = (TerrainTransitions.CornerLevel(1, 1, 3, false), TerrainTransitions.CornerLevel(2, 1, 3, false));
            if (edgePieces.Count != 1 || edgePieces[0].Style != TerrainStyle.Grass ||
                edgePieces[0].Piece / (TerrainTransitions.Levels * TerrainTransitions.Levels * TerrainTransitions.EdgeVariants) != 2 ||
                edgePieces[0].Piece % (TerrainTransitions.Levels * TerrainTransitions.Levels * TerrainTransitions.EdgeVariants) / TerrainTransitions.EdgeVariants !=
                    southLevels.Item1 * TerrainTransitions.Levels + southLevels.Item2)
                throw new InvalidOperationException("Sand beside grass must take one south grass edge between its south corners' reaches.");
            TerrainTransitions.Collect(edgeMap, 0, 0, false, edgePieces);
            if (edgePieces.Count != 1 || edgePieces[0].Piece != TerrainTransitions.OuterCornerPiece(1, TerrainTransitions.CornerLevel(1, 1, 3, false)))
                throw new InvalidOperationException("Sand diagonal to grass must take one rounded outer corner.");
            TerrainTransitions.Collect(edgeMap, 1, 1, false, edgePieces);
            if (edgePieces.Count != 0)
                throw new InvalidOperationException("Grass must not take an edge from lower sand.");
            if (TerrainTransitions.CornerLevel(3, 5, 3, true) != TerrainTransitions.CornerLevel(0, 5, 3, true))
                throw new InvalidOperationException("Edge corners must continue across a wrapped world seam.");
            if (!TerrainTransitions.WaterOverlaps(TerrainStyle.River, TerrainStyle.Ocean) ||
                TerrainTransitions.WaterOverlaps(TerrainStyle.Ocean, TerrainStyle.River) ||
                !TerrainTransitions.WaterOverlaps(TerrainStyle.Lake, TerrainStyle.River) ||
                TerrainTransitions.WaterOverlaps(TerrainStyle.Grass, TerrainStyle.Ocean))
                throw new InvalidOperationException("Lighter water must fan into darker water, and land never into water this way.");
            var mouthMap = WorldTerrainMap.FromTiles(
                Enumerable.Range(0, 9).Select(index => new OwnerWorldTile(index % 3, index / 3, index == 3 ? "river" : "ocean")).ToArray(), 3, 3);
            TerrainTransitions.CollectWater(mouthMap, 1, 1, false, edgePieces);
            var mouthLevels = (TerrainTransitions.CornerLevel(1, 1, 3, false), TerrainTransitions.CornerLevel(1, 2, 3, false));
            if (edgePieces.Count != 1 || edgePieces[0].Style != TerrainStyle.River ||
                edgePieces[0].Piece != TerrainTransitions.EdgePiece(3, mouthLevels.Item1, mouthLevels.Item2, (int)(PixelArt.Hash(1, 1, 94) % TerrainTransitions.EdgeVariants)))
                throw new InvalidOperationException("Sea beside a river mouth must take one soft west edge of river water.");
            TerrainTransitions.CollectWater(mouthMap, 0, 1, false, edgePieces);
            if (edgePieces.Count != 0)
                throw new InvalidOperationException("A river must not take an edge from the darker sea.");
            var spriteData = new HashSet<string>(StringComparer.Ordinal);
            foreach (var atlasSize in new[] { 16, 32 })
                foreach (var nature in Enum.GetValues<NatureSprite>())
                {
                    var sprite = NatureSprites.Sprite(nature, atlasSize);
                    var covered = 0;
                    for (var sy = 0; sy < atlasSize; sy++)
                        for (var sx = 0; sx < atlasSize; sx++)
                            if (sprite.GetPixel(sx, sy).A > 0.05f) covered++;
                    if (sprite.GetPixel(0, 0).A > 0 || sprite.GetPixel(atlasSize - 1, 0).A > 0 ||
                        covered < atlasSize * atlasSize * 0.03f || covered > atlasSize * atlasSize * 0.9f)
                        throw new InvalidOperationException($"{nature} {atlasSize}px sprite must sit on a transparent tile with visible art: {covered} pixels.");
                    if (atlasSize == 32 && !spriteData.Add(Convert.ToBase64String(sprite.GetData())))
                        throw new InvalidOperationException($"{nature} must look different from every other nature sprite.");
                }
            for (byte code = 1; code <= 9; code++)
                if (NatureSprites.ForTree(code) is null)
                    throw new InvalidOperationException($"Tree state {code} has no sprite.");
            foreach (var (species, stages) in new[]
                     {
                         ("broadleaf", new[] { "seed", "sapling", "mature", "stump" }),
                         ("conifer", new[] { "seed", "sapling", "mature", "stump" }),
                         ("orchard", new[] { "growing", "fruiting", "picked" }),
                     })
                foreach (var stage in stages)
                    if (TreeArtManifest.For(species, stage) is not { } art || string.IsNullOrWhiteSpace(art.AssetId) ||
                        string.IsNullOrWhiteSpace(art.Source) || string.IsNullOrWhiteSpace(art.Licence) ||
                        string.IsNullOrWhiteSpace(art.Review) || art.Code > 0 != art.Sprite is not null)
                        throw new InvalidOperationException($"The tree art manifest must describe {species} {stage}.");
            if (TreeArtManifest.Entries.Where(entry => entry.Code > 0).Select(entry => entry.Code).Distinct().Count() !=
                TreeArtManifest.Entries.Count(entry => entry.Code > 0))
                throw new InvalidOperationException("Each drawn tree stage needs its own terrain code.");
            for (byte kind = 1; kind <= 12; kind++)
                if (NatureSprites.ForNaturalObject(kind, 0) is null)
                    throw new InvalidOperationException($"Natural object {kind} has no sprite.");
            // Farm fields: every crop and growth state draws its overlay over the
            // tilled soil at both sizes, and the host's names pick the right art.
            foreach (var atlasSize in new[] { 16, 32 })
                foreach (var crop in Enum.GetValues<FieldCrop>())
                    foreach (var growth in Enum.GetValues<FieldGrowth>())
                    {
                        var overlay = FieldSprites.Overlay(crop, growth, atlasSize);
                        var drawn = 0;
                        for (var oy = 0; oy < overlay.GetHeight(); oy++)
                            for (var ox = 0; ox < overlay.GetWidth(); ox++)
                                if (overlay.GetPixel(ox, oy).A > 0.05f) drawn++;
                        if (overlay.GetWidth() != atlasSize || overlay.GetHeight() != atlasSize || drawn == 0 ||
                            FieldSprites.Texture(crop, growth, atlasSize).GetWidth() != atlasSize)
                            throw new InvalidOperationException($"The {crop} field must draw its {growth} overlay at {atlasSize}px.");
                    }
            if (FieldSprites.CropFor("grain") != FieldCrop.Grain || FieldSprites.CropFor("potatoes") != FieldCrop.Potato ||
                FieldSprites.CropFor("cultivated_greens") != FieldCrop.Greens || FieldSprites.CropFor(null) != FieldCrop.Grain ||
                FieldSprites.GrowthFor("preparing") != FieldGrowth.Prepared || FieldSprites.GrowthFor("prepared") != FieldGrowth.Prepared ||
                FieldSprites.GrowthFor("planted") != FieldGrowth.Seeded || FieldSprites.GrowthFor("growing") != FieldGrowth.Sprout ||
                FieldSprites.GrowthFor("ready") != FieldGrowth.Mature || FieldSprites.GrowthFor("harvested") != FieldGrowth.Harvested)
                throw new InvalidOperationException("Field crops and stages must pick the matching field art.");
            var buildingData = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tilePixels in new[] { 16, 32 })
                foreach (var kind in Enum.GetValues<BuildingKind>())
                    foreach (var (footprintWidth, footprintHeight) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
                    {
                        var roof = BuildingSprites.Render(kind, footprintWidth, footprintHeight, tilePixels);
                        var covered = 0;
                        for (var by = 0; by < roof.GetHeight(); by++)
                            for (var bx = 0; bx < roof.GetWidth(); bx++)
                                if (roof.GetPixel(bx, by).A > 0.05f) covered++;
                        var area = roof.GetWidth() * roof.GetHeight();
                        // Hearths and bedrolls keep a fixed size; roofs and paths cover most of their footprint.
                        var minimum = kind is BuildingKind.Hearth or BuildingKind.Bedroll ? 0.05f : 0.2f;
                        if (roof.GetWidth() != footprintWidth * tilePixels || roof.GetHeight() != footprintHeight * tilePixels ||
                            roof.GetPixel(0, 0).A > 0 || covered < area * minimum || covered > area * 0.95f)
                            throw new InvalidOperationException($"{kind} {footprintWidth}x{footprintHeight} {tilePixels}px building art must fill its footprint inside a clear margin: {covered} of {area} pixels.");
                        if (tilePixels == 32 && footprintWidth == 2 && footprintHeight == 1 &&
                            !buildingData.Add(Convert.ToBase64String(roof.GetData())))
                            throw new InvalidOperationException($"{kind} buildings must look different from every other building family.");
                    }
            var doorFootprint = new Rect2I(10, 10, 2, 1);
            if (BuildingDoor.Facing(doorFootprint, new Vector2I(11, 11)) != new BuildingDoor(DoorSide.South, 1) ||
                BuildingDoor.Facing(doorFootprint, new Vector2I(10, 9)) != new BuildingDoor(DoorSide.North, 0) ||
                BuildingDoor.Facing(doorFootprint, new Vector2I(12, 10)) != new BuildingDoor(DoorSide.East, 0) ||
                BuildingDoor.Facing(doorFootprint, new Vector2I(9, 10)) != new BuildingDoor(DoorSide.West, 0) ||
                BuildingDoor.Facing(doorFootprint, new Vector2I(12, 11)) != BuildingDoor.Default ||
                BuildingDoor.Facing(doorFootprint, null) != BuildingDoor.Default || default(BuildingDoor) != BuildingDoor.Default)
                throw new InvalidOperationException("A building's door must face the entrance tile beside its footprint.");
            var northDoor = BuildingSprites.Render(BuildingKind.House, 1, 1, 32, new BuildingDoor(DoorSide.North, 0));
            var southDoor = BuildingSprites.Render(BuildingKind.House, 1, 1, 32);
            if (Enum.GetValues<DoorSide>().Select(side => Convert.ToBase64String(
                    BuildingSprites.Render(BuildingKind.House, 1, 1, 32, new BuildingDoor(side, 0)).GetData())).Distinct().Count() != 4 ||
                northDoor.GetPixel(16, 1).A < 0.5f || southDoor.GetPixel(16, 1).A > 0)
                throw new InvalidOperationException("A building must show its door on the side it faces.");
            if (BuildingSprites.Render(BuildingKind.House, 1, 1, 32, new BuildingDoor(DoorSide.South, 0)).GetPixel(16, 31).A < 0.5f ||
                southDoor.GetPixel(16, 31).A > 0)
                throw new InvalidOperationException("A building facing a Road must start its doorstep path at the edge of its footprint.");
            bool Drawn(RoadLinks links, int x, int y, bool dark = false) => RoadSprites.Render(links, 0, 32, dark).GetPixel(x, y).A > 0.5f;
            const RoadLinks road = RoadLinks.Road;
            if (!Drawn(road | RoadLinks.North | RoadLinks.South, 16, 0) || !Drawn(road | RoadLinks.North | RoadLinks.South, 16, 31) ||
                Drawn(road | RoadLinks.North | RoadLinks.South, 1, 16) || Drawn(road | RoadLinks.North | RoadLinks.South, 30, 16))
                throw new InvalidOperationException("A straight Road piece must run edge to edge along the Road and nowhere else.");
            if (!Drawn(road | RoadLinks.NorthEast, 29, 2) || Drawn(road | RoadLinks.NorthEast, 16, 1) ||
                Drawn(road | RoadLinks.NorthEast | RoadLinks.North, 29, 2))
                throw new InvalidOperationException("A diagonal Road step must draw one smooth diagonal only where no straight path joins it.");
            if (!Drawn(RoadLinks.North | RoadLinks.East, 30, 1) || Drawn(RoadLinks.North | RoadLinks.East, 16, 16) ||
                Drawn(RoadLinks.North | RoadLinks.East | RoadLinks.NorthEast, 30, 1) ||
                RoadSprites.Draws(RoadLinks.North) || !RoadSprites.Draws(RoadLinks.North | RoadLinks.East))
                throw new InvalidOperationException("A tile beside a diagonal Road must draw only its share of that diagonal.");
            if (!Drawn(road | RoadLinks.North | RoadLinks.East | RoadLinks.NorthEast, 28, 4) ||
                Drawn(road | RoadLinks.North | RoadLinks.East, 29, 2))
                throw new InvalidOperationException("A Road corner must fill in only where the tile between its two arms is Road too.");
            if (!Drawn(road | RoadLinks.DoorNorth, 16, 1) || Drawn(road, 16, 1) || Drawn(road | RoadLinks.DoorNorth, 11, 1))
                throw new InvalidOperationException("A narrow doorstep path must run from the Road to the building's door.");
            if (!RoadSprites.NeedsDarkEdge(TerrainStyle.Sand) || !RoadSprites.NeedsDarkEdge(TerrainStyle.Snow) ||
                RoadSprites.NeedsDarkEdge(TerrainStyle.Grass) || RoadSprites.NeedsDarkEdge(TerrainStyle.ForestGrass) ||
                RoadSprites.Render(road | RoadLinks.East, 0, 32, true).GetPixel(16, 23) is var darkEdge &&
                    (darkEdge.A < 0.5f || darkEdge.Luminance >= RoadSprites.WornEdge.Luminance))
                throw new InvalidOperationException("Roads on sand and snow must take a solid darker edge to stay visible.");
            var itemLooks = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in ItemIcons.Kinds.Append("crate"))
            {
                // Every pixel stays inside a one-pixel margin, so the outline
                // round the silhouette is never cut off or broken.
                if (!ItemIcons.FitsGrid(item))
                    throw new InvalidOperationException($"The {item} icon must be 16 by 16 with an empty edge for its outline.");
                var icon = ItemIcons.Render(item, 32);
                if (icon.GetWidth() != 32 || icon.GetPixel(0, 0).A > 0 || icon.GetPixel(31, 31).A > 0 ||
                    !itemLooks.Add(Convert.ToBase64String(icon.GetData())))
                    throw new InvalidOperationException($"The {item} icon must sit on a clear square and look different from every other item.");
            }
            var toolKinds = new[]
            {
                "wooden_axe", "stone_axe", "iron_axe", "wooden_pickaxe", "stone_pickaxe", "iron_pickaxe",
                "wooden_hoe", "iron_hoe", "wooden_hammer", "stone_hammer", "wooden_sickle", "iron_sickle", "iron_knife",
            };
            if (toolKinds.Any(kind => !ItemIcons.Has(kind)))
                throw new InvalidOperationException("Every Blacksmith tool tier must have its own item icon.");
            if (ItemIcons.Has("never-an-item") || Convert.ToBase64String(ItemIcons.Render("never-an-item", 32).GetData()) !=
                    Convert.ToBase64String(ItemIcons.Render("crate", 32).GetData()) || !ItemIcons.Has("wood"))
                throw new InvalidOperationException("An item without its own icon must show the crate.");
            if (GameUiText.ItemName("storage_pot") != "Storage pot" ||
                GameUiText.ItemName("water_jug") != "Water jug" ||
                GameUiText.ItemName("fresh_water") != "Fresh water")
                throw new InvalidOperationException("Pottery and water items must have clear player-facing names.");
            string IconData(string kind) => Convert.ToBase64String(ItemIcons.Render(kind, 32).GetData());
            if (IconData("wooden_hammer") == IconData("stone_hammer") ||
                IconData("wooden_sickle") == IconData("iron_sickle"))
                throw new InvalidOperationException("Wooden and stronger work tools must show distinct tier colours.");
            if (!ItemIcons.Has("storage_pot") || !ItemIcons.Has("fresh_water") ||
                IconData("storage_pot") != IconData("clay_pot") || IconData("fresh_water") != IconData("water") ||
                IconData("storage_pot") == IconData("water_jug"))
                throw new InvalidOperationException("The storage pot and fresh water must show their approved, distinct icons.");
            if (BuildingSprites.KindFor(["shelter"]) != BuildingKind.Shelter ||
                BuildingSprites.KindFor(["house", "shelter"]) != BuildingKind.House ||
                BuildingSprites.KindFor(["cooking", "warmth"]) != BuildingKind.Hearth ||
                BuildingSprites.KindFor(["silo", "farm-storage"]) != BuildingKind.Silo ||
                BuildingSprites.KindFor(["tailor", "clothing-making"]) != BuildingKind.TailorShop ||
                BuildingSprites.KindFor(null) != BuildingKind.Generic ||
                BuildingSprites.KindForObject("campfire") != BuildingKind.Hearth ||
                BuildingSprites.KindForObject("cooking") != BuildingKind.Hearth ||
                BuildingSprites.KindForObject("path") != BuildingKind.Path ||
                NatureSprites.ForCampResource("construction") != NatureSprite.WoodPile ||
                NatureSprites.ForCampResource("iron_ore") is not null ||
                BuildingSprites.KindForObject("resource") is not null)
                throw new InvalidOperationException("Buildings and camp objects must pick their art family from their recorded tags and kinds.");
            var agentData = new HashSet<string>(StringComparer.Ordinal);
            foreach (var agentSize in new[] { 16, 32 })
                for (var stage = 0; stage < 4; stage++)
                    for (var variant = 0; variant < AgentSprites.VariantCount; variant++)
                    {
                        var figure = AgentSprites.Sprite(variant, stage, agentSize);
                        if (figure.GetPixel(0, 0).A > 0 || figure.GetPixel(agentSize / 2, agentSize / 2).A < 0.9f)
                            throw new InvalidOperationException($"Agent variant {variant} stage {stage} {agentSize}px must be a solid figure on a clear tile.");
                        if (agentSize == 32 && !agentData.Add(Convert.ToBase64String(figure.GetData())))
                            throw new InvalidOperationException($"Agent variant {variant} stage {stage} must look different from every other agent sprite.");
                    }
            if (AgentSprites.VariantFor("founder:1") != AgentSprites.VariantFor("founder:1") ||
                Enumerable.Range(1, 12).Select(index => AgentSprites.VariantFor($"founder:{index}")).Distinct().Count() < 3 ||
                AgentSprites.StageIndex("elder") != 3 || AgentSprites.StageIndex(null) != 2)
                throw new InvalidOperationException("Agent appearance must be stable per agent, varied across agents, and follow their life stage.");
            testHydrology[3] = 1;
            testSurfaces[3] = 4;
            var seamLayers = testLayers with
            {
                Hydrology = Convert.ToBase64String(testHydrology),
                Surface = Convert.ToBase64String(testSurfaces),
            };
            var seamMap = WorldTerrainMap.FromTiles(sample.Tiles, 4, 4, seamLayers);
            if ((seamMap.WaterEdgeMaskAt(0, 0, true) & 8) == 0)
                throw new InvalidOperationException("Water-edge transitions must continue across an enabled world seam.");
            // A mountain at (1, 1) raises a hill base on the high land around
            // it; low ground stays lowland, and hills warm the overview color.
            var hillElevation = Enumerable.Repeat((byte)200, 16).ToArray();
            hillElevation[5] = 230;
            hillElevation[6] = 150;
            var hillLayers = testLayers with
            {
                Elevation = Convert.ToBase64String(hillElevation),
                Hydrology = Convert.ToBase64String(new byte[16]),
                Surface = Convert.ToBase64String(new byte[16]),
            };
            var hillMap = WorldTerrainMap.FromTiles(sample.Tiles, 4, 4, hillLayers);
            var flatMap = WorldTerrainMap.FromTiles(sample.Tiles, 4, 4, testLayers);
            if (!hillMap.IsHillAt(0, 0) || !hillMap.IsHillAt(3, 3) || hillMap.IsHillAt(1, 1) || hillMap.IsHillAt(2, 1) ||
                hillMap.DisplayColorAt(0, 0).IsEqualApprox(TerrainTextures.BaseColor(hillMap.StyleAt(0, 0))) ||
                flatMap.IsHillAt(0, 0) ||
                !flatMap.DisplayColorAt(0, 0).IsEqualApprox(TerrainTextures.BaseColor(flatMap.StyleAt(0, 0))))
                throw new InvalidOperationException("Hills must ring a mountain above low ground and warm only their own overview color.");
            foreach (var atlasSize in new[] { 16, 32 })
            {
                var overlays = new[] { TerrainTextures.HillOverlay(0, atlasSize), TerrainTextures.HillOverlay(1, atlasSize) };
                foreach (var overlay in overlays)
                {
                    var relief = 0;
                    for (var oy = 0; oy < atlasSize; oy++)
                        for (var ox = 0; ox < atlasSize; ox++)
                            if (overlay.GetPixel(ox, oy).A > 0.05f) relief++;
                    for (var edge = 0; edge < atlasSize; edge++)
                        if (overlay.GetPixel(edge, 0).A > 0 || overlay.GetPixel(0, edge).A > 0)
                            throw new InvalidOperationException($"{atlasSize}px hill relief must stay off tile edges.");
                    if (relief < atlasSize * atlasSize * 0.08f || relief > atlasSize * atlasSize * 0.7f)
                        throw new InvalidOperationException($"{atlasSize}px hill relief must be visible without hiding the ground: {relief} pixels.");
                }
                if (overlays[0].GetData().SequenceEqual(overlays[1].GetData()))
                    throw new InvalidOperationException("Hills need two distinct relief variants.");
            }
            var marker = mapObjectVisuals["resource:wood"];
            var identity = marker.GetInstanceId();
            var entered = false;
            marker.MouseEntered += () => entered = true;
            GetViewport().PushInput(new InputEventMouseMotion { Position = marker.GetGlobalRect().GetCenter(), GlobalPosition = marker.GetGlobalRect().GetCenter() }, inLocalCoords: true);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            RenderMap(sample with { Resources = [sampleResource with { Quantity = 7 }] });
            if (!selectedTileText.Text.Contains("7 available", StringComparison.Ordinal))
                throw new InvalidOperationException("Selected-tile resource stock must refresh with observations.");
            ClearTileSelection();
            if (!entered || marker.MouseFilter == MouseFilterEnum.Ignore || marker.GetInstanceId() != identity ||
                !marker.TooltipText.Contains("7/12", StringComparison.Ordinal) || !marker.Text.Contains("7/12", StringComparison.Ordinal))
                throw new InvalidOperationException($"Resource hover/update failed: entered={entered}, filter={marker.MouseFilter}, stable={marker.GetInstanceId() == identity}, text={marker.Text}, rect={marker.GetGlobalRect()}, hovered={GetViewport().GuiGetHoveredControl()?.GetPath()}.");
            var sampleTree = new OwnerWorldResource("sample-tree", "construction", new(3, 1), true,
                "available", 1, 1, 1, 6, "spring", "broadleaf");
            RenderMap(sample with { Resources = [sampleResource, sampleTree] });
            if (terrainLayer.TreeStageAt(3, 1) != "mature" || mapObjectVisuals.ContainsKey("resource:sample-tree"))
                throw new InvalidOperationException("A live tree must render as a terrain object, not a resource text label.");
            RenderMap(sample with { Resources = [sampleResource, sampleTree with { Quantity = 0, State = "depleted" }] });
            if (terrainLayer.TreeStageAt(3, 1) != "stump")
                throw new InvalidOperationException("Harvested trees must become visible stumps.");
            RenderMap(sample with { Resources = [sampleResource, sampleTree with { Quantity = 0, State = "depleted", IsPlanted = true }] });
            if (terrainLayer.TreeStageAt(3, 1) != "sapling")
                throw new InvalidOperationException("Replanted trees must become visible saplings.");
            var plantedTree = new OwnerWorldResource("planted-tree-2-2", "construction", new(2, 2), true,
                "depleted", 0, 1, 1, 6, "spring", "conifer", true, TreeStage: "sapling");
            RenderMap(sample with { Resources = [sampleResource, sampleTree, plantedTree] });
            if (terrainLayer.TreeStageAt(2, 2) != "sapling" || terrainLayer.TreeStageAt(3, 1) != "mature" ||
                NatureSprites.ForTree(TreeArtManifest.For("conifer", "sapling")!.Code) != NatureSprite.ConiferSapling)
                throw new InvalidOperationException("A tree planted on a new tile must draw the host's sapling stage on its own tile.");
            RenderMap(sample with { Resources = [sampleResource, sampleTree, plantedTree with { Quantity = 1, State = "available", IsPlanted = false, TreeStage = "mature" }] });
            if (terrainLayer.TreeStageAt(2, 2) != "mature")
                throw new InvalidOperationException("A grown sapling must draw as a mature tree.");
            var sampleNaturalObject = new OwnerWorldResource("sample-berry-bush", "food", new(0, 2), true,
                "available", 4, 8, NaturalObjectKind: "berry_bush");
            RenderMap(sample with { Resources = [sampleResource, sampleNaturalObject] });
            if (terrainLayer.NaturalObjectNameAt(0, 2) != "Berry bush" ||
                terrainLayer.NaturalObjectStageAt(0, 2) != "available" || mapObjectVisuals.ContainsKey("resource:sample-berry-bush") == false)
                throw new InvalidOperationException("Natural food patches must draw and remain inspectable as distinct objects.");
            RenderMap(sample with { Resources = [sampleResource, sampleNaturalObject with { Quantity = 0, State = "depleted" }] });
            if (terrainLayer.NaturalObjectStageAt(0, 2) != "regrowing")
                throw new InvalidOperationException("Renewable natural patches must render their depleted/regrowing transition.");
            var naturalRoster = new[]
            {
                sampleNaturalObject,
                new OwnerWorldResource("sample-wild-greens", "food", new(1, 0), true, "available", 4, 8,
                    NaturalObjectKind: "wild_greens"),
                new OwnerWorldResource("sample-fiber", "fiber", new(2, 0), true, "available", 4, 8,
                    NaturalObjectKind: "fiber_plant"),
                new OwnerWorldResource("sample-reeds", "fiber", new(3, 0), true, "available", 4, 8,
                    NaturalObjectKind: "reeds"),
                new OwnerWorldResource("sample-stone", "stone", new(0, 1), false, "available", 3, 3,
                    NaturalObjectKind: "stone_outcrop"),
                new OwnerWorldResource("sample-iron", "iron_ore", new(1, 2), false, "available", 3, 3,
                    NaturalObjectKind: "iron_outcrop"),
                new OwnerWorldResource("sample-gold", "gold_ore", new(2, 2), false, "available", 3, 3,
                    NaturalObjectKind: "gold_outcrop"),
                new OwnerWorldResource("sample-diamond", "diamond", new(3, 2), false, "available", 3, 3,
                    NaturalObjectKind: "diamond_outcrop"),
                new OwnerWorldResource("sample-clay", "clay", new(0, 3), false, "available", 3, 3,
                    NaturalObjectKind: "clay_bank"),
                new OwnerWorldResource("sample-seed-patch", "seed", new(1, 3), true, "available", 4, 8,
                    NaturalObjectKind: "wild_seed_patch"),
                new OwnerWorldResource("sample-fertile-soil", "fertile_land", new(2, 3), false, "available", 1, 1,
                    NaturalObjectKind: "fertile_soil"),
            };
            var expectedNaturalNames = new[]
            {
                "Berry bush", "Wild greens", "Fiber plant", "Reeds", "Stone outcrop", "Iron outcrop",
                "Gold outcrop", "Diamond outcrop", "Clay bank", "Wild seed patch", "Fertile soil",
            };
            RenderMap(sample with { Resources = [sampleResource, .. naturalRoster] });
            foreach (var (resource, expectedName) in naturalRoster.Zip(expectedNaturalNames))
            {
                if (terrainLayer.NaturalObjectNameAt(resource.Position.X, resource.Position.Y) != expectedName ||
                    terrainLayer.NaturalObjectStageAt(resource.Position.X, resource.Position.Y) != "available" ||
                    !mapObjectVisuals.TryGetValue("resource:" + resource.Id, out var resourceVisual) ||
                    !resourceVisual.TooltipText.Contains(expectedName, StringComparison.Ordinal))
                    throw new InvalidOperationException($"The {expectedName} natural object must draw as a distinct inspectable map site.");
            }
            var fallenWood = new OwnerWorldResource("sample-fallen-wood", "wood", new(3, 3), false,
                "available", 3, 3, NaturalObjectKind: "fallen_wood");
            var fallenWoodMap = sample with { Resources = [sampleResource, fallenWood] };
            RenderMap(fallenWoodMap);
            if (terrainLayer.NaturalObjectNameAt(3, 3) != "Fallen wood" ||
                terrainLayer.NaturalObjectStageAt(3, 3) != "available" ||
                !mapObjectVisuals["resource:sample-fallen-wood"].TooltipText.Contains("Fallen wood", StringComparison.Ordinal))
                throw new InvalidOperationException("Loose fallen wood must have a named, available natural site after an observation refresh.");
            selectedTile = new Vector2I(3, 3);
            RenderTileInspection(fallenWoodMap);
            if (!selectedTileText.Text.Contains("Fallen wood · 3 available", StringComparison.Ordinal))
                throw new InvalidOperationException("Selecting loose fallen wood must show its name and remaining stock.");
            ClearTileSelection();
            RenderMap(fallenWoodMap with { Resources = [sampleResource, fallenWood with { Quantity = 0, State = "depleted" }] });
            if (terrainLayer.NaturalObjectNameAt(3, 3) != "Fallen wood" ||
                terrainLayer.NaturalObjectStageAt(3, 3) != "depleted")
                throw new InvalidOperationException("Exhausted loose wood must keep its site name and show its depleted stage.");
            var sampleOrchard = new OwnerWorldResource("sample-orchard", "fruit", new(2, 1), true,
                "available", 1, 1, 1, 3, "spring", "orchard", TreeStage: "fruiting");
            RenderMap(sample with { Resources = [sampleResource, sampleTree, sampleOrchard] });
            if (terrainLayer.TreeStageAt(2, 1) != "fruiting" || mapObjectVisuals.ContainsKey("resource:sample-orchard"))
                throw new InvalidOperationException("Orchard fruit must render on one tree tile instead of a resource label.");
            RenderMap(sample with { Resources = [sampleResource, sampleTree, sampleOrchard with { Quantity = 0, TreeStage = "picked" }] });
            if (terrainLayer.TreeStageAt(2, 1) != "picked")
                throw new InvalidOperationException("Picked orchard trees must lose their visible fruit.");
            RenderMap(sample with { Resources = [sampleResource, sampleTree, sampleOrchard with { Quantity = 0, TreeStage = "growing" }] });
            if (terrainLayer.TreeStageAt(2, 1) != "growing")
                throw new InvalidOperationException("Regrowing orchard trees must show their growing stage.");
            var builtMarker = mapObjectVisuals["building:test-hall"];
            if (builtMarker.Text.Length > 0 || !builtMarker.TooltipText.Contains("Test hall", StringComparison.Ordinal) ||
                builtMarker.Size.X <= builtMarker.Size.Y)
                throw new InvalidOperationException("Built structures must keep their multi-tile footprint and hover help without a name on the map.");
            var founderPosition = new OwnerWorldPosition(2, 0);
            var founder = new OwnerWorldInhabitant("founder-ui-test", "Rowan", "active", founderPosition,
                8_000, [], [], new OwnerWorldRoute("idle", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(founderPosition, [founderPosition], [founderPosition]), false)
            {
                Survival = new OwnerWorldSurvival(8_200, 300, true, false, 7_400, null),
            };
            var occupied = sample with { Inhabitants = [founder] };
            RenderMap(occupied);
            var founderButton = inhabitantVisuals[founder.Id];
            var founderButtonIdentity = founderButton.GetInstanceId();
            if (founderButton.Variant != AgentSprites.VariantFor(founder.Id) || founderButton.Caption != "Rowan" ||
                founderButton.NameShown)
                throw new InvalidOperationException("An agent on the map must use their stable sprite and keep their name hidden until needed.");
            founderButton.EmitSignal(Control.SignalName.MouseEntered);
            var hoverNamed = founderButton.NameShown;
            founderButton.EmitSignal(Control.SignalName.MouseExited);
            founderButton.Selected = true;
            var selectedNamed = founderButton.NameShown;
            founderButton.Selected = false;
            if (!hoverNamed || !selectedNamed || founderButton.NameShown)
                throw new InvalidOperationException("An agent's name must show only while it is hovered or selected.");
            if (terrainLayer.CampResourceSpriteCount == 0 || mapObjectVisuals["resource:wood"].Text.Contains('▰'))
                throw new InvalidOperationException("Older camp resources such as the wood store must draw as sprites instead of glyphs.");
            if (terrainLayer.BuildingSpriteCount != occupied.PlacedBuildings.Count ||
                mapObjectVisuals.TryGetValue("building:test-hall", out var hallMarker) && hallMarker.Text.Contains('⌂'))
                throw new InvalidOperationException("Placed buildings must be drawn as roof art rather than text glyphs.");
            selectedInhabitantId = founder.Id;
            RenderSelectedInhabitantCard(occupied);
            RenderMap(occupied with { WorldTick = 1 });
            RenderSelectedInhabitantCard(occupied with { WorldTick = 1 });
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (inhabitantVisuals[founder.Id].GetInstanceId() != founderButtonIdentity ||
                quickWarmthMeter.Percent != 82 || quickFullnessMeter.Percent != 80 ||
                !quickWarmthMeter.IsVisibleInTree() || agentProfilePanel.Visible ||
                !selectedInhabitantCard.GetGlobalRect().Encloses(quickWarmthMeter.GetGlobalRect()))
                throw new InvalidOperationException("The quick card's condition bars and agent hover targets must survive observation refreshes.");
            if (!mapCanvas.GetGlobalRect().Grow(1).Encloses(selectedInhabitantCard.GetGlobalRect()))
                throw new InvalidOperationException($"The agent card must fit inside the world view: map={mapCanvas.GetGlobalRect()} card={selectedInhabitantCard.GetGlobalRect()}.");
            if (selectedInhabitantCard.GetGlobalRect().Intersects(inhabitantVisuals[founder.Id].GetGlobalRect()))
                throw new InvalidOperationException($"The agent card must not cover the agent it describes: card={selectedInhabitantCard.GetGlobalRect()} agent={inhabitantVisuals[founder.Id].GetGlobalRect()}.");
            // The quick card opens the Profile, which docks on the left below the
            // top bar and steps back to the quick card.
            quickCardProfileButton.EmitSignal(BaseButton.SignalName.Pressed);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!agentProfilePanel.Visible || selectedInhabitantCard.Visible || agentProfilePanel.Position.X > 20 ||
                agentProfilePanel.Position.Y < HudTop - 1 || !ShowsGlyph(profileCloseButton, PixelGlyph.Back) ||
                !selectedActorConditionLabel.Text.Contains("Clothed", StringComparison.Ordinal) || profileDietMeter.Percent != 74 ||
                !mapCanvas.GetGlobalRect().Grow(1).Encloses(agentProfilePanel.GetGlobalRect()))
                throw new InvalidOperationException($"The Profile must replace the quick card, dock on the left below the top bar and offer a way back: {agentProfilePanel.GetGlobalRect()}.");
            var messageSnapshot = occupied with
            {
                Instructions =
                [
                    new OwnerWorldInstruction("message-suggestion", founder.Id, "suggestive",
                        "Try the riverbank berries.", "completed", 0, 0, 1, 1, "I will look there."),
                    new OwnerWorldInstruction("message-other-agent", "agent:other", "suggestive",
                        "Private message for someone else.", "pending", 0, 0, 2),
                ],
            };
            RenderSelectedInhabitantCard(messageSnapshot);
            var renderedMessages = instructionHistory.GetParsedText();
            if (!renderedMessages.Contains("Try the riverbank berries.", StringComparison.Ordinal) ||
                !renderedMessages.Contains("Suggestion heard by their personal model", StringComparison.Ordinal) ||
                !renderedMessages.Contains("Agent reply: “I will look there.”", StringComparison.Ordinal) ||
                renderedMessages.Contains("Private message for someone else", StringComparison.Ordinal) ||
                privateThoughtHistory.GetParsedText().Contains("I will look there.", StringComparison.Ordinal) ||
                instructionText.MaxLength != 512)
                throw new InvalidOperationException("The Profile must show only this agent's original observer messages and keep a short reply separate from private thoughts.");
            // The server keeps closed messages that no personal model heard, such as orders the game
            // could not act on. They must show as closed without hiding an older order that is still open.
            RenderSelectedInhabitantCard(occupied with
            {
                Instructions =
                [
                    new OwnerWorldInstruction("message-open-order", founder.Id, "must_do",
                        "Eat the berries you carry.", "queued", 0, 0, 1,
                        Order: new OwnerWorldInstructionOrder("consume_food", "waiting", 1, 0, "food_items", false)),
                    .. Enumerable.Range(1, 4).Select(index => new OwnerWorldInstruction($"message-closed-order-{index}",
                        founder.Id, "must_do", $"Build house number {index}.", "completed", 0, 0, 1 + index)),
                ],
            });
            renderedMessages = instructionHistory.GetParsedText();
            if (!renderedMessages.Contains("Waiting · Eating food\n“You said: Eat the berries you carry.”", StringComparison.Ordinal) ||
                !renderedMessages.Contains("Order closed · not reported as heard\n“You said: Build house number 4.”", StringComparison.Ordinal) ||
                !renderedMessages.Contains("Build house number 2.", StringComparison.Ordinal) ||
                renderedMessages.Contains("Build house number 1.", StringComparison.Ordinal) ||
                !instructionCancelButton.Visible || instructionCancelButton.Text != "Cancel task")
                throw new InvalidOperationException($"The Profile must list closed unheard orders as closed and keep an open order in view: {renderedMessages}");
            var queuedOrderSnapshot = occupied with
            {
                Instructions =
                [
                    new OwnerWorldInstruction("message-active-order", founder.Id, "must_do",
                        "Eat three berries you carry.", "queued", 0, 0, 1,
                        Order: new OwnerWorldInstructionOrder("consume_food", "doing", 3, 2, "food_items", false)),
                    .. Enumerable.Range(1, 4).Select(index => new OwnerWorldInstruction($"message-queued-order-{index}",
                        founder.Id, "must_do", $"Gather berries from queued site {index}.", "queued", 0, 0, 1 + index,
                        Order: new OwnerWorldInstructionOrder("harvest_food", "queued", 1, 0, "harvests", false))),
                ],
            };
            RenderSelectedInhabitantCard(queuedOrderSnapshot);
            renderedMessages = instructionHistory.GetParsedText();
            if (!renderedMessages.Contains("Doing · Eating food · 2/3 food items\n“You said: Eat three berries you carry.”", StringComparison.Ordinal) ||
                !renderedMessages.Contains("Gather berries from queued site 4.", StringComparison.Ordinal) ||
                !renderedMessages.Contains("Gather berries from queued site 2.", StringComparison.Ordinal) ||
                renderedMessages.Contains("Gather berries from queued site 1.", StringComparison.Ordinal) ||
                renderedMessages.Split("You said:", StringSplitOptions.None).Length - 1 != 4 ||
                !instructionCancelButton.Visible || PendingOrderToCancel(queuedOrderSnapshot, founder.Id)?.InstructionId != "message-active-order")
                throw new InvalidOperationException($"Four queued orders must not hide the active task, its progress or the task Cancel targets: {renderedMessages}");
            RenderSelectedInhabitantCard(queuedOrderSnapshot with
            {
                Instructions =
                [
                    .. queuedOrderSnapshot.Instructions,
                    new OwnerWorldInstruction("message-new-suggestion", founder.Id, "suggestive",
                        "Try the sunny riverbank next.", "queued", 0, 0, 6),
                ],
            });
            renderedMessages = instructionHistory.GetParsedText();
            if (!renderedMessages.Contains("Eat three berries you carry.", StringComparison.Ordinal) ||
                !renderedMessages.Contains("Suggestion waiting for their personal model\n“You said: Try the sunny riverbank next.”", StringComparison.Ordinal) ||
                renderedMessages.Split("You said:", StringSplitOptions.None).Length - 1 != 4)
                throw new InvalidOperationException("Keeping the active task visible must preserve the newest unread suggestion and the four-message history limit.");
            var alreadyFinished = OrderCancellationResultText(
                new OwnerOrderControlReceipt("order-private-id", "finished", false, 0, 0));
            var alreadyUnrecognized = OrderCancellationResultText(
                new OwnerOrderControlReceipt("order-private-id", "not_understood", false, 0, 0));
            if (alreadyFinished != "That order had already finished." ||
                alreadyUnrecognized != "The agent could not follow that order." ||
                alreadyUnrecognized.Contains("not_understood", StringComparison.Ordinal) ||
                InstructionSubmissionResultText("must_do", queue: true) != "Order added to the queue." ||
                InstructionSubmissionResultText("must_do", queue: false) != "Order sent." ||
                InstructionSubmissionResultText("suggestive", queue: false) != "Suggestion sent.")
                throw new InvalidOperationException("Task confirmations must use player-facing wording instead of internal status values.");
            // Read all, or clicking the Profile's thoughts, opens the reader beside the Profile.
            var suggestDisabled = instructionSuggestButton.Disabled;
            var orderDisabled = instructionOrderButton.Disabled;
            var queueDisabled = instructionQueueToggle.Disabled;
            var cancelDisabled = instructionCancelButton.Disabled;
            instructionSuggestButton.Disabled = instructionOrderButton.Disabled = false;
            instructionQueueToggle.Disabled = false;
            instructionCancelButton.Disabled = false;
            try
            {
                instructionOrderButton.ButtonPressed = false;
                instructionOrderButton.GrabFocus();
                if (!instructionOrderButton.HasFocus())
                    throw new InvalidOperationException("Order must be reachable by keyboard in the Profile.");
                Input.ParseInputEvent(new InputEventAction { Action = "ui_accept", Pressed = true });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Input.ParseInputEvent(new InputEventAction { Action = "ui_accept", Pressed = false });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!instructionOrderButton.ButtonPressed || instructionSuggestButton.ButtonPressed || !instructionQueueToggle.Visible)
                    throw new InvalidOperationException("Keyboard activation must switch the instruction kind to Order.");
                instructionQueueToggle.GrabFocus();
                if (!instructionQueueToggle.HasFocus())
                    throw new InvalidOperationException("Queue must be reachable by keyboard in the Profile.");
                Input.ParseInputEvent(new InputEventAction { Action = "ui_accept", Pressed = true });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Input.ParseInputEvent(new InputEventAction { Action = "ui_accept", Pressed = false });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!instructionQueueToggle.ButtonPressed)
                    throw new InvalidOperationException("Keyboard activation must turn on queued orders.");
                instructionCancelButton.GrabFocus();
                if (!instructionCancelButton.HasFocus())
                    throw new InvalidOperationException("Cancel task must be reachable by keyboard in the Profile.");
                Input.ParseInputEvent(new InputEventAction { Action = "ui_accept", Pressed = true });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Input.ParseInputEvent(new InputEventAction { Action = "ui_accept", Pressed = false });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!statusLabel.Text.Contains("Wait for the world to load before cancelling an order.", StringComparison.Ordinal))
                    throw new InvalidOperationException("Cancel task must explain when the owner has not loaded a world yet.");
                instructionSuggestButton.GrabFocus();
                if (!instructionSuggestButton.HasFocus())
                    throw new InvalidOperationException("Suggest must be reachable by keyboard in the Profile.");
                instructionSuggestButton.ButtonPressed = true;
            }
            finally
            {
                instructionQueueToggle.ButtonPressed = false;
                instructionQueueToggle.Disabled = queueDisabled;
                instructionCancelButton.Disabled = cancelDisabled;
                instructionSuggestButton.Disabled = suggestDisabled;
                instructionOrderButton.Disabled = orderDisabled;
            }
            readThoughtsButton.GrabFocus();
            if (!readThoughtsButton.HasFocus())
                throw new InvalidOperationException("Read all must be reachable by keyboard in the Profile.");
            Input.ParseInputEvent(new InputEventAction { Action = "ui_accept", Pressed = true });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventAction { Action = "ui_accept", Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            readThoughtsButton.ReleaseFocus();
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!thoughtsPanel.Visible || !agentProfilePanel.Visible ||
                thoughtsPanel.Position.X < agentProfilePanel.Position.X + agentProfilePanel.Size.X ||
                !mapCanvas.GetGlobalRect().Grow(1).Encloses(thoughtsPanel.GetGlobalRect()) ||
                !thoughtsReaderText.GetParsedText().Contains("None recorded yet.", StringComparison.Ordinal))
                throw new InvalidOperationException($"Read all must open the thoughts reader beside the Profile: reader={thoughtsPanel.GetGlobalRect()} profile={agentProfilePanel.GetGlobalRect()}.");
            thoughtsPanel.Hide();
            privateThoughtHistory.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            if (!thoughtsPanel.Visible || privateThoughtHistory.MouseDefaultCursorShape != Control.CursorShape.PointingHand)
                throw new InvalidOperationException("Clicking the Profile's thoughts must open the thoughts reader.");
            thoughtsPanel.Hide();
            profileCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (agentProfilePanel.Visible || !selectedInhabitantCard.Visible || selectedInhabitantId != founder.Id)
                throw new InvalidOperationException("Back on the Profile must return to the quick card with the agent still selected.");
            quickCardSpeakButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!agentProfilePanel.Visible || !speakSection.Visible)
                throw new InvalidOperationException("Speak on the quick card must open the Profile at its message box.");
            profileCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
            selectedInhabitantId = null;
            RenderSelectedInhabitantCard(occupied with { WorldTick = 1 });
            if (selectedInhabitantCard.Visible || agentProfilePanel.Visible)
                throw new InvalidOperationException("Clearing the selection must close both the quick card and the Profile.");
            selectedInhabitantId = founder.Id;
            RenderSelectedInhabitantCard(occupied with { WorldTick = 1 });
            if (inhabitantSocialDetails.GetParsedText().Length == 0 || privateThoughtHistory.GetParsedText().Length == 0)
                throw new InvalidOperationException("Reselecting an unchanged agent must restore their social details and private thoughts.");
            RenderSelectedInhabitantCard(occupied with
            {
                Inhabitants = [founder with
                {
                    Survival = null,
                    PublicIntention = new OwnerWorldPublicIntention("safe_idle", "keeping a safe routine", "deterministic", 1),
                    Lesson = new("Mira", "farming", "training", 3, 20),
                    Skills = [new("building", 0, "teacher-id", "Mira"), new("crafting", 0, null, null)],
                    Relationships = [new OwnerWorldInhabitantRelationship("home:test", "household:one",
                        "household_membership", "accepted", "household", 1)],
                }],
                Stockpiles = [new("household:one", "Founder's household", [])],
            });
            if (quickWarmthMeter.Visible || profileWarmthMeter.Visible || selectedActorConditionLabel.Text.Contains('%') ||
                !inhabitantSocialDetails.Text.Contains("Member of Founder's household", StringComparison.Ordinal) ||
                inhabitantSocialDetails.Text.Contains("household:one", StringComparison.OrdinalIgnoreCase) ||
                inhabitantDetails.Text.Contains("Unassigned", StringComparison.Ordinal) ||
                !inhabitantDetails.Text.Contains("Building skill · taught by Mira", StringComparison.Ordinal) ||
                !inhabitantDetails.Text.Contains("Crafting skill · learned by doing", StringComparison.Ordinal) ||
                !inhabitantDetails.Text.Contains("Learning Farming with Mira", StringComparison.Ordinal) ||
                inhabitantDetails.Text.Contains("teacher-id", StringComparison.Ordinal) ||
                !quickCardActivityLabel.Text.Contains("Keeping a safe routine", StringComparison.Ordinal))
                throw new InvalidOperationException($"The agent cards must read naturally, name households and omit unavailable condition or unassigned-role placeholders: {quickCardActivityLabel.Text} / {inhabitantSocialDetails.Text}");
            RenderSelectedInhabitantCard(occupied with { WorldTick = 1 });
            if (!quickWarmthMeter.Visible || !profileWarmthMeter.Visible)
                throw new InvalidOperationException("Reported agent condition must be shown again.");
            RenderSelectedInhabitantCard(occupied with
            {
                Inhabitants = [founder with
                {
                    DecisionFactors = [.. founder.DecisionFactors.Where(factor => factor.Key != "model-status"),
                        new("model-status", "unusable_reply"), new("last-model-choice", "seek_food")],
                    PublicIntention = new("safe_idle", "keeping a safe routine", "deterministic", 1),
                }],
            });
            if (!quickCardActivityLabel.Text.Contains("Model: Unusable reply", StringComparison.Ordinal) ||
                !quickCardActivityLabel.Text.Contains("Keeping a safe routine", StringComparison.Ordinal) ||
                !inhabitantDetails.Text.Contains("Last model choice:", StringComparison.Ordinal) ||
                quickCardActivityLabel.Text.Contains("unusable_reply", StringComparison.Ordinal))
                throw new InvalidOperationException("Agent cards must distinguish a failed model attempt, the safe activity and the last accepted model choice.");
            RenderSelectedInhabitantCard(occupied with { WorldTick = 1 });
            UpdateTileHover(founderButton.Position + mapStage.Position + founderButton.Size / 2);
            if (terrainLayer.HoveredTile is not null)
                throw new InvalidOperationException("An agent marker must take hover priority over its ground tile.");
            UpdateTileHover(new Vector2(currentTileSize * 1.5f, currentTileSize * 0.5f) + mapStage.Position);
            if (terrainLayer.HoveredTile != new Vector2I(1, 0))
                throw new InvalidOperationException("The hovered ground tile must receive a square outline.");
            selectedInhabitantId = null;
            selectedInhabitantCard.Hide();
            var agentClick = founderButton.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = agentClick,
                GlobalPosition = agentClick,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            }, inLocalCoords: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (selectedInhabitantId != founder.Id)
                throw new InvalidOperationException("Clicking an agent marker must select the agent before the ground tile.");
            selectedInhabitantId = null;
            RenderMap(sample with { Resources = [], PlacedBuildings = [] });
            if (inhabitantVisuals.ContainsKey(founder.Id))
                throw new InvalidOperationException("Removed agent marker was retained.");
            if (mapObjectVisuals.ContainsKey("resource:wood")) throw new InvalidOperationException("Removed resource marker was retained.");
            if (mapObjectVisuals.ContainsKey("building:test-hall")) throw new InvalidOperationException("Removed building marker was retained.");
            await VerifyAgentPosesAsync(sample, founder);
            await VerifyMountainReliefAsync();
            await VerifyDesertAndSnowArtAsync();
            var crowded = sample with
            {
                WorldId = "ui-marker-bounds",
                PackedTerrain = null,
                PackedMapLayers = null,
                MapLayersDigest = null,
                Tiles = Enumerable.Range(0, 64 * 64)
                    .Select(index => new OwnerWorldTile(index % 64, index / 64, "meadow")).ToArray(),
                Inhabitants = Enumerable.Range(0, 4)
                    .Select(index => founder with { Id = $"crowded-{index}", Position = new OwnerWorldPosition(2, 2) })
                    .ToArray(),
                Resources = [],
                PlacedBuildings = [],
            };
            foreach (var zoom in new[] { 1f, 2f, 4f })
            {
                cameraZoom = zoom;
                RenderMap(crowded);
                CenterCameraAt(new Vector2(2.5f, 2.5f));
                var tileRect = new Rect2(new Vector2(2 * currentTileSize, 2 * currentTileSize),
                    new Vector2(currentTileSize, currentTileSize));
                var markers = crowded.Inhabitants.Select(person => inhabitantVisuals[person.Id]).ToArray();
                if (markers.Any(marker => !tileRect.Encloses(new Rect2(marker.Position, marker.Size))) ||
                    markers.Where((marker, i) => markers.Skip(i + 1)
                        .Any(other => new Rect2(marker.Position, marker.Size).Intersects(
                            new Rect2(other.Position, other.Size)))).Any())
                    throw new InvalidOperationException($"Crowded marker hitboxes overflow or overlap at {currentTileSize}px tiles.");
                UpdateTileHover(new Vector2(3.5f * currentTileSize, 2.5f * currentTileSize) + mapStage.Position);
                if (terrainLayer.HoveredTile != new Vector2I(3, 2))
                    throw new InvalidOperationException("An adjacent tile must not hit a crowded agent marker.");
            }
            RenderMap(sample with { Resources = [], PlacedBuildings = [] });
            var smallMapTileSize = currentTileSize;
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = mapCanvas.Size / 2 });
            if (currentTileSize <= smallMapTileSize)
                throw new InvalidOperationException("Mouse-wheel zoom must work even when the small starter map reaches its fitted tile-size cap.");
            RenderMap(sample with
            {
                WorldId = "ui-navigation",
                PackedTerrain = null,
                PackedMapLayers = null,
                MapLayersDigest = null,
                Tiles = Enumerable.Range(0, 192)
                    .Select(index => new OwnerWorldTile(index % 16, index / 16, "meadow")).ToArray(),
                Resources = [],
                PlacedBuildings = [],
            });
            mapButton.EmitSignal(BaseButton.SignalName.Pressed);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!worldOverviewPanel.Visible || worldOverview.VisibleTiles.Size.Y <= 0)
                throw new InvalidOperationException("The top-left map button did not open a camera-aware world overview.");
            var fittedTileSize = currentTileSize;
            var fittedViewHeight = worldOverview.VisibleTiles.Size.Y;
            for (var index = 0; index < 2; index++)
                HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = mapCanvas.Size / 2 });
            if (currentTileSize <= fittedTileSize || worldOverview.VisibleTiles.Size.Y >= fittedViewHeight)
                throw new InvalidOperationException($"Mouse-wheel zoom did not narrow the visible world area: tile={fittedTileSize}->{currentTileSize}, view={fittedViewHeight}->{worldOverview.VisibleTiles.Size.Y}.");
            var beforeOverviewClick = mapStage.Position;
            worldOverview._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = new Vector2(worldOverview.Size.X / 2, 12),
            });
            if (mapStage.Position.DistanceTo(beforeOverviewClick) < 1)
                throw new InvalidOperationException("Clicking the overview did not move the world camera.");
            var beforeOverviewDrag = mapStage.Position;
            worldOverview._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(worldOverview.Size.X / 2, worldOverview.Size.Y - 12),
            });
            worldOverview._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            if (mapStage.Position.DistanceTo(beforeOverviewDrag) < 1)
                throw new InvalidOperationException("Dragging the overview did not move the world camera.");
            var beforeMiddleDrag = mapStage.Position;
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
            HandleMapInput(new InputEventMouseMotion { Relative = new Vector2(0, 60) });
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false });
            if (mapStage.Position.DistanceTo(beforeMiddleDrag) < 1)
                throw new InvalidOperationException("Middle-drag did not pan the world camera.");
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            var beforeKeyboardPan = mapStage.Position;
            PanCameraForFrame(new Vector2(0, 1), 0.1);
            if (mapStage.Position.DistanceTo(beforeKeyboardPan) < 1)
                throw new InvalidOperationException("Keyboard panning did not move the world camera.");
            var eventDestination = cameraCenterTiles.X < 8 ? new OwnerWorldPosition(15, 11) : new OwnerWorldPosition(0, 0);
            knownEvents[100] = new OwnerWorldEvent(100, 1, "food_consumed", "founder-scout", eventDestination);
            RenderEventLog();
            eventLog.AddText("\nscroll-sentinel");
            RenderEventLog();
            if (!eventLog.GetParsedText().Contains("scroll-sentinel", StringComparison.Ordinal))
                throw new InvalidOperationException("An unchanged Event Log must not be rebuilt, which would reset its scroll position.");
            var beforeEventJump = cameraCenterTiles;
            eventLog.EmitSignal(RichTextLabel.SignalName.MetaClicked, "100");
            if (cameraCenterTiles.DistanceTo(beforeEventJump) < 0.5f)
                throw new InvalidOperationException("Clicking a located event did not move the world camera.");
            knownEvents[101] = new OwnerWorldEvent(101, 2, "food_consumed", "founder-scout", null);
            RenderEventLog();
            var loggedDay = SplitClock(DisplayWorldClock(1)).Date;
            if (eventLog.GetParsedText().Split(loggedDay).Length != 2)
                throw new InvalidOperationException($"Same-day events must share one date heading: {eventLog.GetParsedText()}");
            knownEvents.Remove(101);
            RenderEventLog();
            // Events that arrive while the log is closed are counted on its
            // button, then marked read once it is opened.
            eventsPanel.Hide();
            UpdateUnreadEvents(eventsWorldId);
            var readBefore = unreadEvents;
            knownEvents[102] = new OwnerWorldEvent(102, 3, "food_consumed", "founder-scout", null);
            RenderEventLog();
            if (unreadEvents != readBefore + 1 || !eventsBadge.Visible ||
                eventsBadge.Text != (readBefore + 1).ToString(CultureInfo.InvariantCulture))
                throw new InvalidOperationException($"A new event must show an unread count on the Event Log button: {unreadEvents} after {readBefore}.");
            knownEvents[103] = new OwnerWorldEvent(103, 3, "model_call_warning", "used:812:limit:1000", null);
            RenderEventLog();
            var loggedWarning = eventLog.GetParsedText();
            if (unreadEvents != readBefore + 2 ||
                loggedWarning.Split("Model calls: 812 of 1,000 used across all worlds.").Length != 2 ||
                !loggedWarning.Contains("raise it in Settings → Game.", StringComparison.Ordinal))
                throw new InvalidOperationException($"The 80% model-call warning must be one Event Log row pointing to Game Settings: {loggedWarning}");
            if (eventsBadge.ZIndex < 1 || !eventsBadge.ZAsRelative)
                throw new InvalidOperationException("The unread count must draw over the HUD button next to Events instead of being covered by it.");
            ToggleEvents();
            if (unreadEvents != 0 || eventsBadge.Visible || !eventLog.GetParsedText().Contains('●'))
                throw new InvalidOperationException("Opening the Event Log must mark events read and dot the rows that were new.");
            VerifyEventRows();
            // The mouse wheel over a panel scrolls it and never zooms the map behind it, even at the end of the scroll.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var zoomBeforeWheel = cameraZoom;
            var overEventLog = eventScroll.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = overEventLog, GlobalPosition = overEventLog }, true);
            for (var turn = 0; turn < 40; turn++)
                GetViewport().PushInput(new InputEventMouseButton
                {
                    Position = overEventLog,
                    GlobalPosition = overEventLog,
                    ButtonIndex = MouseButton.WheelDown,
                    Pressed = true,
                }, true);
            if (!Mathf.IsEqualApprox(cameraZoom, zoomBeforeWheel))
                throw new InvalidOperationException("Scrolling over a panel must not zoom the map behind it.");
            ToggleEvents();
            // Each top-bar panel opens under the button that opened it, not at the far side of the screen.
            foreach (var (panel, button, toggle) in new (Control Panel, Button Button, Action Toggle)[]
            {
                (filtersPanel, filtersButton, ToggleMapFilters), (rosterPanel, inhabitantsButton, ToggleInhabitants),
                (worldInfoPanel, worldInfoButton, ToggleWorldInfo),
            })
            {
                toggle();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var panelBounds = panel.GetGlobalRect();
                var buttonBounds = button.GetGlobalRect();
                if (!panel.Visible || panelBounds.Position.Y < buttonBounds.End.Y ||
                    panelBounds.Position.X > buttonBounds.End.X || panelBounds.End.X < buttonBounds.Position.X)
                    throw new InvalidOperationException($"{panel.Name} must open under its {button.Text} button: panel {panelBounds}, button {buttonBounds}.");
                toggle();
            }
            // The Event Log sits flush with the right edge of the screen instead.
            ToggleEvents();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var logRight = eventsPanel.GetGlobalRect().End.X;
            var screenRight = uiLayer.GetGlobalRect().End.X - 14 * uiLayer.Factor;
            ToggleEvents();
            if (Math.Abs(logRight - screenRight) > 1)
                throw new InvalidOperationException($"The Event Log must open at the right edge of the screen: ends at {logRight}, edge {screenRight}.");
            knownEvents.Remove(102);
            knownEvents.Remove(103);
            RenderEventLog();
            var largeTerrain = Enumerable.Range(0, 256 * 128)
                .Select(index => (byte)(index % 37 == 0 ? 3 : 0)).ToArray();
            var largeMap = sample with
            {
                WorldId = "ui-large-map",
                MapManifestDigest = "ui-large-map-v1",
                Tiles = [],
                PackedTerrain = new OwnerWorldPackedTerrain(256, 128, "terrain-kind-v1",
                    Convert.ToBase64String(largeTerrain)),
                PackedMapLayers = null,
                MapLayersDigest = null,
                Authoring = new OwnerWorldAuthoringState(true, 0, 0, 0, "ui-large-map-v1",
                    "ui-large-map-v1", "clear", "spring", []),
                WeatherRegionSize = 32,
                WeatherRegions = Enumerable.Range(0, 4)
                    .SelectMany(x => Enumerable.Range(0, 2).Select(y => new OwnerWeatherRegion(x, y, "snow", 12)))
                    .Append(new OwnerWeatherRegion(4, 2, "rain", 78)).ToArray(),
                Resources = [],
                PlacedBuildings = [],
                Towns = [],
            };
            var siteWidth = 32;
            var siteHeight = 32;
            var siteLength = siteWidth * siteHeight;
            var siteTerrain = new OwnerWorldPackedTerrain(siteWidth, siteHeight, "terrain-kind-v1",
                Convert.ToBase64String(new byte[siteLength]));
            var siteHydrology = new byte[siteLength];
            var siteSurface = new byte[siteLength];
            siteHydrology[(siteHeight - 1) * siteWidth + siteWidth - 1] = 1;
            siteSurface[(siteHeight - 1) * siteWidth + siteWidth - 1] = 4;
            siteSurface[16 * siteWidth + 21] = 7;
            var siteLayers = new OwnerWorldPackedMapLayers(siteWidth, siteHeight, "map-layers-v1",
                Convert.ToBase64String(Enumerable.Repeat((byte)2, siteLength).ToArray()),
                Convert.ToBase64String(Enumerable.Repeat((byte)100, siteLength).ToArray()),
                Convert.ToBase64String(siteHydrology), Convert.ToBase64String(siteSurface),
                Convert.ToBase64String(Enumerable.Repeat((byte)1, siteLength).ToArray()));
            var siteSnapshot = new OwnerWorldSnapshot("ui-town-site-guidance", 0, "site-guidance-map",
                [], [],
                [
                    new("site-food", "food", new(20, 16), false, "available", 4, NaturalObjectKind: "berry_bush"),
                    new("site-farmland", "fertile_land", new(21, 16), false, "available", 1, NaturalObjectKind: "fertile_soil"),
                    new("site-wood", "construction", new(19, 16), true, "available", 5, TreeKind: "broadleaf", TreeStage: "mature"),
                    new("site-stone", "stone", new(20, 18), false, "available", 3, NaturalObjectKind: "stone_outcrop"),
                ], null, 0)
            {
                PackedTerrain = siteTerrain,
                PackedMapLayers = siteLayers,
                FounderSetup = new OwnerFounderSetup(4, 0, false) { CanChooseTownSite = true },
            };
            RenderMap(siteSnapshot);
            choosingFirstTownSite = true;
            UpdateTownSiteGuidance(siteSnapshot, force: true);
            RenderFounderSetup(siteSnapshot);
            var guidance = terrainLayer.CurrentTownSiteGuidance
                ?? throw new InvalidOperationException("Choosing a Town site must draw suitability guidance.");
            var betterSite = guidance.At(20, 16);
            var lessSuitableSite = guidance.At(2, 2);
            if (!betterSite.IsBuildableGround || !betterSite.HasNearbyFood || !betterSite.HasNearbyFarmland ||
                !betterSite.HasNearbyWood || !betterSite.HasNearbyStone ||
                lessSuitableSite.GuidanceStrength >= betterSite.GuidanceStrength ||
                !lessSuitableSite.IsBuildableGround ||
                !CanSubmitFirstTownSiteChoice(siteSnapshot, new Vector2I(2, 2)) ||
                !townSiteButton.Visible ||
                townSiteButton.Text != "Cancel Town site")
                throw new InvalidOperationException("Town-site advice must distinguish a stronger local mix while still submitting a less suitable buildable site for the host's layout check.");
            UpdateHoverReadout(siteSnapshot, new Vector2I(20, 16));
            if (!hoverReadoutLabel.Text.Contains("Town-site advice", StringComparison.Ordinal) ||
                !hoverReadoutLabel.Text.Contains("fertile ground", StringComparison.Ordinal) ||
                !hoverReadoutLabel.Text.Contains("stone", StringComparison.Ordinal) ||
                !hoverReadoutLabel.Text.Contains("Roads", StringComparison.Ordinal))
                throw new InvalidOperationException("Town-site hover help must explain the nearby factors behind its map tint.");
            UpdateHoverReadout(siteSnapshot, new Vector2I(2, 2));
            if (!hoverReadoutLabel.Text.Contains("no food nearby", StringComparison.Ordinal) ||
                !hoverReadoutLabel.Text.Contains("no stone nearby", StringComparison.Ordinal))
                throw new InvalidOperationException("Town-site hover help must identify missing nearby factors as well as helpful ones.");
            var wrappedSiteSnapshot = siteSnapshot with
            {
                WorldId = "ui-wrapped-site-guidance",
                WrapsEastWest = true,
                Resources = [new("seam-food", "food", new(siteWidth - 1, 16), false, "available", 1,
                    NaturalObjectKind: "wild_greens")],
            };
            var wrappedSiteTerrain = WorldTerrainMap.FromPacked(siteTerrain, siteLayers, wrapsEastWest: true);
            var wrappedGuidance = TownSiteGuidance.Create(wrappedSiteTerrain, wrappedSiteSnapshot);
            if (!wrappedGuidance.At(0, 16).HasNearbyFood ||
                TownSiteGuidance.Create(WorldTerrainMap.FromPacked(siteTerrain, siteLayers), wrappedSiteSnapshot)
                    .At(0, 16).HasNearbyFood ||
                guidance.At(siteWidth - 1, siteHeight - 1).IsBuildableGround)
                throw new InvalidOperationException("Town-site advice must follow wrapped geography and leave water unshaded.");
            choosingFirstTownSite = false;
            UpdateTownSiteGuidance(null);
            UpdateHoverReadout(siteSnapshot, new Vector2I(2, 2));
            RenderFounderSetup(sample);
            RenderMap(largeMap);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (terrainLayer.DrawsGroundTextures)
                throw new InvalidOperationException("Overview zoom must keep the flat one-pixel-per-tile palette instead of textures.");
            if (terrainLayer.GetChildCount() != 0 || terrainLayer.VisibleTileCount >= largeTerrain.Length / 2 ||
                worldOverview.VisibleTiles.Size.X >= 256)
                throw new InvalidOperationException($"A regional map must draw only the visible terrain without per-tile nodes: children={terrainLayer.GetChildCount()}, visible={terrainLayer.VisibleTileCount}, overview={worldOverview.VisibleTiles.Size}.");
            if (seasonLabel.Text != "Spring" || weatherLabel.Text != "Rain" ||
                !worldInfoText.Text.Contains("Soil moisture here: 78%", StringComparison.Ordinal) ||
                terrainLayer.WeatherAt(150, 80) != "rain" || terrainLayer.WeatherAt(20, 20) != "snow")
                throw new InvalidOperationException("The world HUD and info must show weather and moisture at the camera.");
            // Regions are squares on the host; on the map their weather must
            // reach the middle fully but end in a wandering, soft edge.
            var edgeColumns = new List<int>();
            for (var edgeRow = 68; edgeRow <= 92; edgeRow += 2)
                for (var column = 100; column < 160; column++)
                    if (weatherLayer.CoverageAt(column, edgeRow, "rain") > 0.5f)
                    {
                        edgeColumns.Add(column);
                        break;
                    }
            if (weatherLayer.GetChildCount() != 0 || weatherLayer.CoverageAt(144, 80, "rain") < 0.9f ||
                weatherLayer.CoverageAt(144, 80, "snow") > 0.1f || edgeColumns.Count < 10 ||
                edgeColumns.Max() - edgeColumns.Min() < 3)
                throw new InvalidOperationException($"Rain must fill its region and end in a wandering edge, not a square: edge={string.Join(',', edgeColumns)}.");
            // Lightning brightens softly and rarely: never a strobe.
            var (brightest, lit, flashes, wasLit) = (0f, 0, 0, false);
            for (var step = 0; step < 9000; step++)
            {
                var flash = WeatherLayer.LightningFlash(step * 0.01);
                brightest = Math.Max(brightest, flash);
                if (flash > 0.02f) lit++;
                if (flash > 0.02f && !wasLit) flashes++;
                wasLit = flash > 0.02f;
            }
            if (brightest > 0.31f || lit > 900 || flashes > 25)
                throw new InvalidOperationException($"Lightning must stay soft and rare: peak={brightest}, lit samples={lit}, flashes={flashes}.");
            var startedMap = largeMap with { FounderSetup = null };
            RenderWorldHud(startedMap);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // The HUD's pause control shows stopped time on its own, and the
            // weather holds still until time runs again.
            var frozenAt = weatherLayer.AnimationTime;
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var pausedWidth = pauseButton.Size.X;
            if (pauseButton.ThemeTypeVariation != "EmberButton" || pauseButton.Caption != "Paused" ||
                !weatherLayer.Paused || weatherLayer.AnimationTime != frozenAt)
                throw new InvalidOperationException($"A paused world must show Paused on its pause control and freeze its weather: {pauseButton.Caption}, {weatherLayer.AnimationTime - frozenAt}s.");
            RenderWorldHud(startedMap with { Authoring = startedMap.Authoring! with { IsPaused = false } });
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (pauseButton.ThemeTypeVariation == "EmberButton" || weatherLayer.Paused || weatherLayer.AnimationTime <= frozenAt)
                throw new InvalidOperationException("Weather must move again, and the pause control return to normal, once time runs.");
            if (pauseButton.Caption != "Pause" || !Mathf.IsEqualApprox(pauseButton.Size.X, pausedWidth))
                throw new InvalidOperationException($"The pause control must keep its width when its word changes: {pausedWidth} then {pauseButton.Size.X}.");
            RenderWorldHud(largeMap);
            UpdateTileHover(mapCanvas.Size / 2);
            var hoveredCenter = TileAtCanvas(mapCanvas.Size / 2, largeMap);
            if (!hoverReadout.Visible || hoverReadoutLabel.Text.Contains($"{hoveredCenter.X}, {hoveredCenter.Y}", StringComparison.Ordinal) ||
                hoverReadoutLabel.Text.Any(char.IsDigit) || hoverReadout.MouseFilter != Control.MouseFilterEnum.Ignore)
                throw new InvalidOperationException($"Hovering ground must show a click-through readout of what is there, without map coordinates: {hoverReadoutLabel.Text}");
            var beforeRoad = largeMap with { RoadTiles = [] };
            UpdateHoverReadout(beforeRoad, hoveredCenter);
            var afterRoad = beforeRoad with { RoadTiles = [new OwnerWorldPosition(hoveredCenter.X, hoveredCenter.Y)] };
            UpdateHoverReadout(afterRoad, hoveredCenter);
            if (!hoverReadoutLabel.Text.Contains(" · Road", StringComparison.Ordinal))
                throw new InvalidOperationException("The hovered tile must reflect a newly observed road without moving the pointer.");
            var withBridge = beforeRoad with
            {
                Bridges =
                [
                    new OwnerWorldBridge("bridge-ui-test", "plank_span_1", "traffic", "east_west",
                        [new OwnerWorldPosition(hoveredCenter.X - 1, hoveredCenter.Y), new OwnerWorldPosition(hoveredCenter.X + 1, hoveredCenter.Y)],
                        [new OwnerWorldPosition(hoveredCenter.X, hoveredCenter.Y)], 0),
                ],
            };
            // Draw the saved deck, then read it back from the hover readout and World Info.
            RenderMap(withBridge);
            RenderWorldInfo(withBridge);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            UpdateHoverReadout(withBridge, hoveredCenter);
            if (!hoverReadoutLabel.Text.Contains(" · Bridge", StringComparison.Ordinal) ||
                !worldInfoText.Text.Contains("Bridges: 1", StringComparison.Ordinal))
                throw new InvalidOperationException($"A saved bridge must show in the hover readout and World Info: {hoverReadoutLabel.Text}");
            RenderMap(largeMap);
            RenderWorldInfo(largeMap);
            UpdateTileHover(new Vector2(-5, -5));
            if (hoverReadout.Visible)
                throw new InvalidOperationException("Leaving the map must hide the hover readout.");
            var beforeLargePan = worldOverview.VisibleTiles.Position;
            CenterCameraAt(new Vector2(20, 20));
            if (worldOverview.VisibleTiles.Position.DistanceTo(beforeLargePan) < 1 ||
                terrainLayer.VisibleTileCount >= largeTerrain.Length / 2)
                throw new InvalidOperationException("Panning a large map must update the camera-bounded terrain view.");
            if (seasonLabel.Text != "Spring" || weatherLabel.Text != "Snow" ||
                !worldInfoText.Text.Contains("here: Spring · Snow", StringComparison.Ordinal) ||
                !worldInfoText.Text.Contains("Soil moisture here: 12%", StringComparison.Ordinal))
                throw new InvalidOperationException($"Panning must update HUD and World Info to local weather: camera={cameraCenterTiles}, HUD={seasonLabel.Text} · {weatherLabel.Text}, info={worldInfoText.Text}.");
            var wrappedMap = largeMap with
            {
                WorldId = "ui-wrapped-map",
                WrapsEastWest = true,
                WeatherRegions = [.. largeMap.WeatherRegions, new OwnerWeatherRegion(7, 2, "storm", 40)],
                Resources = [new OwnerWorldResource("seam-wood", "construction", new(255, 64),
                    true, "available", 5, 10, 0, 0, "spring")],
            };
            RenderMap(wrappedMap);
            CenterCameraAt(new Vector2(0.5f, 64));
            if (worldOverview.VisibleTiles.Position.X >= 0 ||
                terrainLayer.VisibleTileCount >= largeTerrain.Length / 2 ||
                !worldOverview.WrapsEastWest ||
                terrainLayer.WeatherAt(-1, 70) != "storm" ||
                TileAtCanvas(mapCanvas.Size / 2 - new Vector2(2 * currentTileSize, 0), wrappedMap).X != 254)
                throw new InvalidOperationException("Wrapped camera must render and target the western seam without an empty edge.");
            var seamMarker = mapObjectVisuals["resource:seam-wood"];
            if (!mapCanvas.GetGlobalRect().HasPoint(seamMarker.GetGlobalRect().GetCenter()))
                throw new InvalidOperationException("Resources across the wrapped seam must remain visible at the camera.");
            var seamEntered = false;
            seamMarker.MouseEntered += () => seamEntered = true;
            GetViewport().PushInput(new InputEventMouseMotion
            {
                Position = seamMarker.GetGlobalRect().GetCenter(),
                GlobalPosition = seamMarker.GetGlobalRect().GetCenter(),
            }, inLocalCoords: true);
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!seamEntered)
                throw new InvalidOperationException("A resource across the wrapped seam must remain hoverable.");
            PanCamera(new Vector2(-5, 0));
            if (cameraCenterTiles.X < 250 || worldOverview.VisibleTiles.End.X <= 256 ||
                !mapCanvas.GetGlobalRect().HasPoint(seamMarker.GetGlobalRect().GetCenter()))
                throw new InvalidOperationException("Panning west across a wrapped seam must not clamp the camera.");
            PanCamera(new Vector2(10, 0));
            if (cameraCenterTiles.X > 10 || worldOverview.VisibleTiles.Position.X >= 0 ||
                !mapCanvas.GetGlobalRect().HasPoint(seamMarker.GetGlobalRect().GetCenter()))
                throw new InvalidOperationException("Panning east across a wrapped seam must remain continuous.");
            var overviewAvailable = worldOverview.Size - new Vector2(12, 12);
            var overviewScale = Math.Min(overviewAvailable.X / 256, overviewAvailable.Y / 128);
            var overviewAtlasSize = new Vector2(256 * overviewScale, 128 * overviewScale);
            var overviewAtlasPosition = (worldOverview.Size - overviewAtlasSize) / 2;
            var overviewAtlasY = overviewAtlasPosition.Y + overviewAtlasSize.Y / 2;
            var overviewRightEdge = overviewAtlasPosition.X + overviewAtlasSize.X;
            CenterCameraAt(new Vector2(254, 64));
            var eastDragStart = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = new Vector2(overviewRightEdge - 1, overviewAtlasY),
            });
            worldOverview._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(overviewRightEdge + 48, overviewAtlasY),
            });
            var eastAfterFirstDrag = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(overviewRightEdge + 96, overviewAtlasY),
            });
            var eastAfterSecondDrag = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            if (PositiveMod(eastAfterFirstDrag - eastDragStart, 256) <= 2 ||
                PositiveMod(eastAfterSecondDrag - eastAfterFirstDrag, 256) <= 2)
                throw new InvalidOperationException("Dragging past the wrapped overview's eastern edge must continue panning east.");

            var overviewLeftEdge = overviewAtlasPosition.X;
            CenterCameraAt(new Vector2(2, 64));
            var westDragStart = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = new Vector2(overviewLeftEdge + 1, overviewAtlasY),
            });
            worldOverview._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(overviewLeftEdge - 48, overviewAtlasY),
            });
            var westAfterFirstDrag = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(overviewLeftEdge - 96, overviewAtlasY),
            });
            var westAfterSecondDrag = cameraCenterTiles.X;
            worldOverview._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            if (PositiveMod(westDragStart - westAfterFirstDrag, 256) <= 2 ||
                PositiveMod(westAfterFirstDrag - westAfterSecondDrag, 256) <= 2)
                throw new InvalidOperationException("Dragging past the wrapped overview's western edge must continue panning west.");

            CenterCameraAt(new Vector2(0.5f, 64));
            var wrappedStride = currentTileSize + TileGap;
            var seamMarkerCellSample = mapStage.Position + new Vector2(
                seamMarker.Position.X + Math.Min(seamMarker.Size.X / 2, wrappedStride / 2f),
                seamMarker.Position.Y + wrappedStride / 2f);
            var seamTileUnderMarker = TileAtCanvas(seamMarkerCellSample, wrappedMap);
            if (seamTileUnderMarker != new Vector2I(255, 64))
                throw new InvalidOperationException($"A seam marker must select its canonical tile before repeated wrapping: " +
                    $"tile={seamTileUnderMarker}, camera={cameraCenterTiles}, stage={mapStage.Position}, " +
                    $"marker={seamMarker.Position}+{seamMarker.Size}, sample={seamMarkerCellSample}.");
            var nearestSeamTileX = 255 + MathF.Round((cameraCenterTiles.X - 255) / 256) * 256;
            var seamSelectionPosition = mapStage.Position + new Vector2(
                (nearestSeamTileX + 0.5f) * wrappedStride, 64.5f * wrappedStride);
            HandleMapInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = seamSelectionPosition,
            });
            if (terrainLayer.SelectedTile != new Vector2I(255, 64) ||
                !selectedTileText.Text.Contains("Tile 255, 64", StringComparison.Ordinal))
                throw new InvalidOperationException("Selecting a wrapped seam marker must inspect its canonical tile.");

            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            var beforeFramePan = cameraCenterTiles;
            cameraCenterTiles = new Vector2(128, 64);
            PanCameraForFrame(new Vector2(1, 0), 0.1);
            var cardinalDistance = cameraCenterTiles.DistanceTo(new Vector2(128, 64));
            cameraCenterTiles = new Vector2(128, 64);
            PanCameraForFrame(new Vector2(1, 1), 0.05);
            PanCameraForFrame(new Vector2(1, 1), 0.05);
            if (Math.Abs(cameraCenterTiles.DistanceTo(new Vector2(128, 64)) - cardinalDistance) > 0.01f)
                throw new InvalidOperationException("Diagonal pan must match cardinal speed and depend on elapsed time, not repeat count.");
            var beforeEcho = cameraCenterTiles;
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Right, Pressed = true, Echo = true });
            if (cameraCenterTiles != beforeEcho)
                throw new InvalidOperationException("Keyboard repeat events must not add camera movement.");
            cameraCenterTiles = beforeFramePan;
            var repeatedKeyStart = cameraCenterTiles.X;
            var previousKeyCenter = repeatedKeyStart;
            for (var step = 0; step < 512; step++)
            {
                PanCameraForFrame(new Vector2(1, 0), 0.1);
                if (Math.Abs(PositiveMod(cameraCenterTiles.X - previousKeyCenter, 256) - 1.5f) > 0.01f)
                    throw new InvalidOperationException("Held right movement must keep moving through multiple wrapped laps.");
                previousKeyCenter = cameraCenterTiles.X;
            }
            for (var step = 0; step < 512; step++)
            {
                PanCameraForFrame(new Vector2(-1, 0), 0.1);
                if (Math.Abs(PositiveMod(previousKeyCenter - cameraCenterTiles.X, 256) - 1.5f) > 0.01f)
                    throw new InvalidOperationException("Held left movement must keep moving through multiple wrapped laps.");
                previousKeyCenter = cameraCenterTiles.X;
            }
            if (Math.Abs(cameraCenterTiles.X - repeatedKeyStart) > 0.01f)
                throw new InvalidOperationException("Equal repeated horizontal key travel must return to the same wrapped position.");

            var dragStride = currentTileSize + TileGap;
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
            for (var lap = 0; lap < 4; lap++)
            {
                var beforeEastDrag = cameraCenterTiles.X;
                HandleMapInput(new InputEventMouseMotion
                {
                    Relative = new Vector2(-dragStride * (256 + 37), 0),
                });
                if (Math.Abs(PositiveMod(cameraCenterTiles.X - beforeEastDrag, 256) - 37) > 0.01f)
                    throw new InvalidOperationException("Repeated eastward main-map drags must cross full wrapped laps.");
            }
            for (var lap = 0; lap < 4; lap++)
            {
                var beforeWestDrag = cameraCenterTiles.X;
                HandleMapInput(new InputEventMouseMotion
                {
                    Relative = new Vector2(dragStride * (256 + 37), 0),
                });
                if (Math.Abs(PositiveMod(beforeWestDrag - cameraCenterTiles.X, 256) - 37) > 0.01f)
                    throw new InvalidOperationException("Repeated westward main-map drags must cross full wrapped laps.");
            }
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false });
            nearestSeamTileX = 255 + MathF.Round((cameraCenterTiles.X - 255) / 256) * 256;
            seamSelectionPosition = mapStage.Position + new Vector2(
                (nearestSeamTileX + 0.5f) * wrappedStride, 64.5f * wrappedStride);
            if (Math.Abs(cameraCenterTiles.X - repeatedKeyStart) > 0.01f ||
                terrainLayer.SelectedTile != new Vector2I(255, 64) ||
                TileAtCanvas(seamSelectionPosition, wrappedMap) != new Vector2I(255, 64) ||
                !mapCanvas.GetGlobalRect().HasPoint(seamMarker.GetGlobalRect().GetCenter()) ||
                !selectedTileText.Text.Contains("Tile 255, 64", StringComparison.Ordinal))
                throw new InvalidOperationException("Repeated wrapped travel must preserve marker visibility and canonical selection alignment.");

            if (mapObjectVisuals["resource:seam-wood"].Text.Contains('\n', StringComparison.Ordinal))
                throw new InvalidOperationException("A map marker too small for its name must show its glyph instead of a clipped fragment.");
            RenderMap(largeMap);
            CenterCameraAt(new Vector2(-5, 64));
            if (worldOverview.VisibleTiles.Position.X < 0 || worldOverview.WrapsEastWest)
                throw new InvalidOperationException("Non-wrapped worlds must retain bounded horizontal camera edges.");

            cameraZoom = 1;
            RenderMap(largeMap);
            var oldVisibleWidth = worldOverview.VisibleTiles.Size.X;
            var oldTileCount = terrainLayer.VisibleTileCount;
            var baselinePan = System.Diagnostics.Stopwatch.StartNew();
            for (var step = 0; step < 8; step++)
            {
                CenterCameraAt(new Vector2(80 + step, 64));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            baselinePan.Stop();
            cameraZoom = 0.65f;
            RenderMap(largeMap);
            // The HUD floats over the map, so the full-height view needs a
            // 9 px floor to keep the Small map's poles out of sight.
            if (currentTileSize > 9 || worldOverview.VisibleTiles.Size.X < oldVisibleWidth * 1.3f ||
                terrainLayer.VisibleTileCount > 40_000)
                throw new InvalidOperationException($"Overview zoom must widen bounded terrain coverage: tile={currentTileSize}, width={oldVisibleWidth}->{worldOverview.VisibleTiles.Size.X}, tiles={terrainLayer.VisibleTileCount}.");
            var overviewWood = fallenWood with { Position = new(84, 64) };
            var overviewWoodMap = largeMap with { Resources = [overviewWood] };
            RenderMap(overviewWoodMap);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (terrainLayer.TileSize >= WorldTerrainLayer.SpriteTileMinimum ||
                terrainLayer.OverviewNaturalObjectDrawCount != 1 ||
                terrainLayer.NaturalObjectNameAt(84, 64) != "Fallen wood" ||
                terrainLayer.NaturalObjectStageAt(84, 64) != "available" ||
                !mapObjectVisuals["resource:sample-fallen-wood"].TooltipText.Contains("Fallen wood", StringComparison.Ordinal))
                throw new InvalidOperationException("Available loose wood must remain drawn and inspectable below sprite zoom.");
            RenderMap(largeMap);
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (terrainLayer.OverviewNaturalObjectDrawCount != 0 ||
                mapObjectVisuals.ContainsKey("resource:sample-fallen-wood"))
                throw new InvalidOperationException("Removing loose wood in the next observation must clear its overview drawing and marker.");
            var wideVisibleWidth = worldOverview.VisibleTiles.Size.X;
            var wideTileCount = terrainLayer.VisibleTileCount;
            var widePan = System.Diagnostics.Stopwatch.StartNew();
            for (var step = 0; step < 8; step++)
            {
                CenterCameraAt(new Vector2(80 + step, 64));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            widePan.Stop();
            GD.Print($"Zoom comparison at {mapCanvas.Size}: 12 px={oldVisibleWidth:0} columns/{oldTileCount} tiles, {baselinePan.Elapsed.TotalMilliseconds:0} ms/8 pan frames; 8 px={wideVisibleWidth:0} columns/{wideTileCount} tiles, {widePan.Elapsed.TotalMilliseconds:0} ms/8 pan frames (headless sample).");
            cameraZoom = 1;
            RenderMap(largeMap);
            var waterCenter = WorldTerrainMap.FromTiles(
                (from y in Enumerable.Range(0, 9)
                 from x in Enumerable.Range(0, 9)
                 select new OwnerWorldTile(x, y, x is >= 5 and <= 7 && y is >= 5 and <= 7
                     ? "meadow" : "ocean")).ToArray(), 9, 9);
            var emptyWorldFocus = InitialCameraCenter(largeMap with
            {
                Towns = [],
                Inhabitants = [],
                Objects = [],
            }, waterCenter);
            if (emptyWorldFocus.DistanceTo(new Vector2(6.5f, 6.5f)) > 0.01f)
                throw new InvalidOperationException($"A new world without a Town or agents must open over dry land: camera={emptyWorldFocus}.");
            CenterCameraAt(new Vector2(80, 64));
            var zoomPointer = mapCanvas.Size * new Vector2(0.25f, 0.3f);
            var tileUnderPointer = (zoomPointer - mapStage.Position) / (currentTileSize + TileGap);
            HandleMapInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = zoomPointer });
            var tileUnderPointerAfterZoom = (zoomPointer - mapStage.Position) / (currentTileSize + TileGap);
            if (tileUnderPointerAfterZoom.DistanceTo(tileUnderPointer) > 0.1f)
                throw new InvalidOperationException($"Mouse-wheel zoom must keep the pointed-at tile under the cursor: {tileUnderPointer} -> {tileUnderPointerAfterZoom}.");
            cameraZoom = 1;
            RenderMap(largeMap);
            var focusedMap = largeMap with
            {
                WorldId = "ui-camera-focus",
                Towns = [new OwnerWorldTown("town:first", "First Town", "founding", 0, [], [],
                    [new(180, 80), new(181, 80), new(180, 81), new(181, 81)])],
            };
            RenderMap(focusedMap);
            if (cameraCenterTiles.DistanceTo(new Vector2(181, 81)) > 1.5f)
                throw new InvalidOperationException($"A newly opened world must frame its first Town, not the map center: camera={cameraCenterTiles}.");
            RenderMap(focusedMap with
            {
                WorldId = "ui-camera-seam",
                WrapsEastWest = true,
                Towns = [new OwnerWorldTown("town:first", "First Town", "founding", 0, [], [], [new(255, 64), new(0, 64)])],
            });
            if (PositiveMod(cameraCenterTiles.X + 1, 256) > 2 || Math.Abs(cameraCenterTiles.Y - 64.5f) > 1.5f)
                throw new InvalidOperationException($"A Town across the wrapped seam must be framed as one place: camera={cameraCenterTiles}.");
            RenderMap(largeMap);
            var rosterPosition = new OwnerWorldPosition(180, 90);
            OwnerWorldInhabitant RosterAgent(string id, string name, string lifecycle, int fullness) =>
                new(id, name, lifecycle, rosterPosition, fullness, [], [],
                    new OwnerWorldRoute("idle", null, null, [], string.Empty),
                    new OwnerWorldSpatialKnowledge(rosterPosition, [rosterPosition], [rosterPosition]), false);
            var rosterMap = largeMap with
            {
                WorldId = "ui-roster",
                Inhabitants =
                [
                    RosterAgent("roster-rowan", "Rowan", "active", 2_000) with
                    {
                        PublicIntention = new OwnerWorldPublicIntention("seek_food", "looking for food", "deterministic", 1),
                    },
                    RosterAgent("roster-ilya", "Ilya", "active", 9_000),
                    RosterAgent("roster-mira", "Mira", "dead", 5_000),
                ],
            };
            RenderMap(rosterMap);
            RenderInhabitantList(rosterMap);
            // The cards show who is doing what and who needs help; the hidden text list keeps the same order for selection.
            if (rosterCards.ItemCount != 3 || !rosterCards.Visible || inhabitantList.Visible ||
                rosterCards.GetItemTitle(0) != "Ilya" || rosterCards.GetItemTitle(1) != "Rowan" || rosterCards.GetItemTitle(2) != "Mira" ||
                !RosterCardText(1).Contains("Looking for food", StringComparison.Ordinal) ||
                !RosterCardText(1).Contains("hungry", StringComparison.OrdinalIgnoreCase) ||
                RosterCardText(0).Contains("hungry", StringComparison.OrdinalIgnoreCase) ||
                !RosterCardText(2).Contains("Died", StringComparison.Ordinal) ||
                !rosterSummaryLabel.Text.Contains("1 hungry", StringComparison.Ordinal))
                throw new InvalidOperationException("The Agents list must show a card per agent, living first, with activity and a Hungry tag: " +
                    string.Join(" | ", Enumerable.Range(0, rosterCards.ItemCount).Select(RosterCardText)));
            if (inhabitantList.ItemCount != 4 ||
                !inhabitantList.GetItemText(0).StartsWith("Ilya", StringComparison.Ordinal) ||
                !inhabitantList.GetItemText(1).Contains("looking for food", StringComparison.Ordinal) ||
                !inhabitantList.GetItemText(1).Contains("very hungry", StringComparison.Ordinal) ||
                inhabitantList.GetItemText(0).Contains("hungry", StringComparison.Ordinal) ||
                inhabitantList.GetItemText(2) != "Deceased" || inhabitantList.IsItemSelectable(2) ||
                !inhabitantList.GetItemText(3).StartsWith("Mira", StringComparison.Ordinal))
                throw new InvalidOperationException("The roster must list the living with their activity and hunger before the deceased.");
            RenderWorldHud(rosterMap);
            if (!agentsWarning.Visible || inhabitantsButton.Text != "2" ||
                !inhabitantsButton.TooltipText.Contains("hungry: Rowan", StringComparison.Ordinal))
                throw new InvalidOperationException("The Agents button must count the living and flag anyone hungry.");
            CenterCameraAt(new Vector2(40, 30));
            SelectInhabitantFromList(1);
            if (selectedInhabitantId != "roster-rowan" || cameraCenterTiles.DistanceTo(new Vector2(180.5f, 90.5f)) > 1.5f)
                throw new InvalidOperationException($"Choosing a living agent in the roster must bring them into view: camera={cameraCenterTiles}.");
            selectedInhabitantId = null;
            RenderInhabitantList(rosterMap with { Inhabitants = [] });
            if (inhabitantList.Visible || rosterCards.Visible || !rosterSummaryLabel.Text.Contains("No one lives here yet", StringComparison.Ordinal))
                throw new InvalidOperationException("An empty roster must show its summary without an empty list box.");
            var formerPosition = new OwnerWorldPosition(2, 2);
            var deceased = new OwnerWorldInhabitant("agent:00000000000000000000000000000098", "Mira", "dead", formerPosition,
                5_000, [], [new("age-band", "elder"), new("death-tick", "1")],
                new OwnerWorldRoute("deceased", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(formerPosition, [formerPosition], [formerPosition]), false)
            {
                RecentPrivateThoughts = [new OwnerWorldPrivateThought(1, "I hope Rowan remembers our garden.")],
                RecentMemories = [new OwnerWorldAgentMemory(1, "living-parent", "Rowan",
                    "I hid the garden tools where Rowan cannot see them.", "private")],
                RecentKnowledgeFacts = [new OwnerWorldKnowledgeFact(2, 7, 9, "Forest", ["wood"],
                    "Mira", "firsthand", null)],
                KnowledgeArtifacts = [new OwnerWorldKnowledgeArtifact("knowledge-artifact-000001", "field_map",
                    "Field map · 2 sites", 2, "Mira",
                    [new OwnerWorldKnowledgeSite(7, 9, "Forest", ["wood"], "Mira"),
                     new OwnerWorldKnowledgeSite(8, 9, "River", [], "Mira")])],
            };
            var historicalSnapshot = sample with
            {
                WorldId = "ui-deceased",
                Inhabitants = [deceased],
                Resources = [],
                PlacedBuildings = [],
            };
            RenderMap(historicalSnapshot);
            RenderInhabitantList(historicalSnapshot);
            selectedInhabitantId = deceased.Id;
            RenderSelectedInhabitantCard(historicalSnapshot);
            if (entityLayer.GetChildren().Any(child => !child.IsQueuedForDeletion()) ||
                inhabitantList.ItemCount != 1 || !rosterSummaryLabel.Text.Contains("1 deceased", StringComparison.Ordinal) ||
                !agentProfilePanel.Visible || selectedInhabitantCard.Visible || speakSection.Visible ||
                !selectedActorSummaryLabel.Text.Contains("Dead", StringComparison.Ordinal) ||
                !ShowsGlyph(profileCloseButton, PixelGlyph.Close) || renameAgentInput.Text != "Mira")
                throw new InvalidOperationException("A deceased inhabitant must open straight to a historical Profile without appearing as a living map actor.");
            if (!privateThoughtHistory.Text.Contains("I hope Rowan remembers our garden.", StringComparison.Ordinal) ||
                !thoughtsHeading.Text.Contains("HISTORICAL", StringComparison.Ordinal) ||
                !thoughtsReaderText.GetParsedText().Contains("I hope Rowan remembers our garden.", StringComparison.Ordinal) ||
                !thoughtsReaderTitle.Text.Contains("historical", StringComparison.Ordinal))
                throw new InvalidOperationException("Deceased profiles must retain their saved private thoughts without generating new ones.");
            memoriesButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!memoriesPanel.Visible ||
                !memoryHistory.Text.Contains("I hid the garden tools", StringComparison.Ordinal) ||
                !memoryHistory.Text.Contains("Field map", StringComparison.Ordinal) ||
                !memoryHistory.Text.Contains("Forest at (7, 9)", StringComparison.Ordinal) ||
                inhabitantSocialDetails.Text.Contains("I hid the garden tools", StringComparison.Ordinal))
                throw new InvalidOperationException("Historical memories and bounded agent-owned map records must be inspectable separately from public social notes.");
            memoriesPanel.Hide();
            cameraZoom = 4;
            RenderMap(historicalSnapshot);
            var deathDestination = cameraCenterTiles.X < 8
                ? new OwnerWorldPosition(15, 11) : new OwnerWorldPosition(0, 0);
            knownEvents[101] = new OwnerWorldEvent(101, 2, "inhabitant_removed", deceased.Id,
                deathDestination);
            RenderEventLog();
            if (!eventLog.GetParsedText().Contains("died.", StringComparison.Ordinal) ||
                eventLog.GetParsedText().Contains("scroll-sentinel", StringComparison.Ordinal) ||
                DescribeWorldEvent(knownEvents[101], historicalSnapshot) != "Mira died." ||
                gameSettingsContent.GetChildren().OfType<Label>()
                    .Any(label => label.Text.Contains("event pop-ups", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Deaths must remain in the Event Log without an event pop-up setting.");
            ToggleEvents();
            if (!eventsPanel.Visible || !agentProfilePanel.Visible)
                throw new InvalidOperationException("The Event Log and the agent's Profile must remain available together.");
            var beforeDeathJump = cameraCenterTiles;
            eventLog.EmitSignal(RichTextLabel.SignalName.MetaClicked, "101");
            if (eventsPanel.Visible || cameraCenterTiles.DistanceTo(beforeDeathJump) < 0.5f)
                throw new InvalidOperationException("A death in the Event Log must jump to its location.");
            var parentPosition = new OwnerWorldPosition(1, 1);
            var parent = new OwnerWorldInhabitant("living-parent", "Rowan", "active", parentPosition,
                7_000, [], [new("age-band", "adult")],
                new OwnerWorldRoute("idle", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(parentPosition, [parentPosition], [parentPosition]), false)
            {
                Relationships =
                [
                    new OwnerWorldInhabitantRelationship("birth:test", deceased.Id,
                        "biological_parentage", "accepted", "family", 1, "parent"),
                    new OwnerWorldInhabitantRelationship("partner:test", "living-partner",
                        "partnership", "accepted", "family", 1, "partner"),
                ],
            };
            var partner = new OwnerWorldInhabitant("living-partner", "Ilya", "active", parentPosition,
                7_000, [], [new("age-band", "adult")],
                new OwnerWorldRoute("idle", null, null, [], string.Empty),
                new OwnerWorldSpatialKnowledge(parentPosition, [parentPosition], [parentPosition]), false)
            {
                Relationships = [new OwnerWorldInhabitantRelationship("partner:test", parent.Id,
                    "partnership", "accepted", "family", 1, "partner")],
            };
            var child = deceased with
            {
                Relationships = [new OwnerWorldInhabitantRelationship("birth:test", parent.Id,
                    "biological_parentage", "accepted", "family", 1, "child")],
            };
            var family = new List<OwnerWorldInhabitant> { parent, child, partner };
            var currentParent = parent;
            for (var generation = 0; generation < 8; generation++)
            {
                var ancestor = parent with
                {
                    Id = $"ancestor:{generation}",
                    DisplayName = $"Ancestor {generation + 1}",
                    Relationships = [new OwnerWorldInhabitantRelationship($"birth:ancestor:{generation}", currentParent.Id,
                        "biological_parentage", "accepted", "family", 1, "parent")],
                };
                family.Add(ancestor);
                currentParent = ancestor;
            }
            ShowFamilyTree(historicalSnapshot with { Inhabitants = family.ToArray() }, child.Id);
            if (!familyTreePanel.Visible || familyTreeView.ParentEdgeCount != 9 || familyTreeView.PartnerEdgeCount != 1 ||
                !familyTreeView.VisiblePersonIds.Contains(parent.Id) ||
                !familyTreeView.VisiblePersonIds.Contains(child.Id) ||
                !familyTreeView.VisiblePersonIds.Contains(partner.Id) ||
                family.Any(person => !familyTreeView.VisiblePersonIds.Contains(person.Id)))
                throw new InvalidOperationException("Family tree must show ancestry, partnerships and deceased profiles.");
            var familyWindow = GetWindow();
            var originalFamilySize = familyWindow.Size;
            var originalFamilyRenderSize = familyWindow.ContentScaleSize;
            foreach (var size in new[] { new Vector2I(1920, 1080), new Vector2I(1280, 720), new Vector2I(1024, 768) })
            {
                familyWindow.Size = size;
                familyWindow.ContentScaleSize = size;
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                ApplyResponsiveLayout();
                var expectedFactor = size == new Vector2I(1920, 1080) ? 2 : 1;
                if (GetViewportRect().Size != new Vector2(size.X, size.Y) || uiLayer.Factor != expectedFactor)
                    throw new InvalidOperationException($"Family tree smoke must exercise the requested viewport and interface size at {size}: viewport={GetViewportRect().Size}, factor={uiLayer.Factor}.");
                var panelRect = familyTreePanel.GetGlobalRect();
                var scrollRect = familyTreeScroll.GetGlobalRect();
                if (familyTreeScroll.Size.Y <= 0 || familyTreeView.Size.Y <= 0 ||
                    !GetViewportRect().Encloses(panelRect) || !panelRect.Encloses(scrollRect))
                    throw new InvalidOperationException($"Family tree must show its content in a screen-bounded scroll panel at {size}: panel={panelRect}, scroll={scrollRect}, tree={familyTreeView.Size}.");
                if (size == new Vector2I(1024, 768) &&
                    familyTreeScroll.GetVScrollBar().MaxValue <= familyTreeScroll.GetVScrollBar().Page)
                    throw new InvalidOperationException("A tall family tree must remain scrollable when the panel is capped to the screen.");
            }
            foreach (var size in new[] { new Vector2I(1920, 1080), new Vector2I(1280, 720), new Vector2I(1024, 768) })
            {
                familyWindow.Size = size;
                familyWindow.ContentScaleSize = size;
                familyTreePanel.Hide();
                ShowFamilyTree(historicalSnapshot with { Inhabitants = [parent, child, partner] }, child.Id);
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                ApplyResponsiveLayout();
                var panelRect = familyTreePanel.GetGlobalRect();
                var scrollRect = familyTreeScroll.GetGlobalRect();
                if (familyTreeScroll.Size.Y < familyTreeView.GetCombinedMinimumSize().Y ||
                    !GetViewportRect().Encloses(panelRect) || !panelRect.Encloses(scrollRect) ||
                    !panelRect.Encloses(familyTreeStatus.GetGlobalRect()))
                    throw new InvalidOperationException($"Reopened short Family Tree must fit its content and help text at {size}: panel={panelRect}, scroll={scrollRect}, help={familyTreeStatus.GetGlobalRect()}.");
                if (familyTreeScroll.GetVScrollBar().MaxValue > familyTreeScroll.GetVScrollBar().Page)
                    throw new InvalidOperationException("A fitting short tree must not retain the long tree's vertical scroll range.");
            }
            familyWindow.Size = originalFamilySize;
            familyWindow.ContentScaleSize = originalFamilyRenderSize;
            for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ApplyResponsiveLayout();
            familyTreeView.GetChildren().OfType<Button>().Single(button => button.Text.StartsWith(parent.DisplayName, StringComparison.Ordinal))
                .EmitSignal(BaseButton.SignalName.Pressed);
            if (selectedInhabitantId != parent.Id || familyTreePanel.Visible)
                throw new InvalidOperationException("Selecting a relative must open that person's agent profile.");
            familyTreeView.SetPeople("roommates-only", [parent with { Relationships = [] }, child with { Relationships = [] }, partner with { Relationships = [] }], child.Id);
            if (familyTreeView.VisiblePersonIds.Count != 1 || familyTreeView.ParentEdgeCount != 0)
                throw new InvalidOperationException("Household membership must not create a family link.");
            familyTreePanel.Hide();
            eventsPanel.Show();
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (eventsPanel.Visible)
                throw new InvalidOperationException("Escape must close the open world panel.");
            choosingFirstTownSite = true;
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (choosingFirstTownSite || townSiteButton.Text == "Cancel Town site")
                throw new InvalidOperationException("Escape must cancel an active Town-site selection.");
            if (HudButtons().Any(button => button.FocusMode == Control.FocusModeEnum.None))
                throw new InvalidOperationException("Top-bar actions must remain reachable by keyboard focus.");
            eventsButton.GrabFocus();
            eventsButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (eventsButton.HasFocus())
                throw new InvalidOperationException("A pressed top-bar button must return keyboard focus to map controls.");
            eventsButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (!pauseButton.TooltipText.Contains("(Space)", StringComparison.Ordinal) ||
                !inhabitantsButton.TooltipText.Contains("(R)", StringComparison.Ordinal) ||
                !eventsButton.TooltipText.Contains("(E)", StringComparison.Ordinal))
                throw new InvalidOperationException("Top-bar tooltips must keep naming their keyboard shortcuts after refreshes.");
            RenderMap(occupied);
            ClearInhabitantSelection();
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.I, Pressed = true });
            if (!worldInfoPanel.Visible || !worldInfoText.Text.Contains("F1", StringComparison.Ordinal))
                throw new InvalidOperationException("I must open World Info, which points to the F1 controls list.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.I, Pressed = true });
            if (worldInfoPanel.Visible)
                throw new InvalidOperationException("Pressing a panel shortcut again must close that panel.");
            ShowWorldInfoPage(towns: false);
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.T, Pressed = true });
            if (!worldInfoPanel.Visible || !WorldInfoShowsTowns)
                throw new InvalidOperationException("T must open World Info on its Towns page; a world can hold several Towns.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.T, Pressed = true });
            if (worldInfoPanel.Visible)
                throw new InvalidOperationException("Pressing T again must close the Towns page.");
            // The three HUD groups float over the map side by side, never on top of each other.
            var hudGroups = new[] { hudLeft, hudTime, hudRight }.Select(row => row.GetParent<Control>().GetGlobalRect()).ToArray();
            if (hudGroups.Any(rect => !mapCanvas.GetGlobalRect().Encloses(rect)) ||
                hudGroups[0].Intersects(hudGroups[1]) || hudGroups[1].Intersects(hudGroups[2]) || hudGroups[0].Intersects(hudGroups[2]))
                throw new InvalidOperationException($"HUD groups must sit inside the world view without overlapping: {string.Join(' ', hudGroups)}.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.F1, Pressed = true });
            for (var frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!controlsPanel.Visible || !mapCanvas.GetGlobalRect().Encloses(controlsPanel.GetGlobalRect()))
                throw new InvalidOperationException($"F1 must open the controls list inside the world view: map={mapCanvas.GetGlobalRect()} controls={controlsPanel.GetGlobalRect()}.");
            VerifyControlsKeycaps();
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (controlsPanel.Visible)
                throw new InvalidOperationException("Escape must close the controls list.");
            await VerifyDeveloperToolsAsync(occupied, founder);
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Minus, Pressed = true });
            var zoomedOutTile = currentTileSize;
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Equal, Pressed = true });
            if (currentTileSize <= zoomedOutTile)
                throw new InvalidOperationException("The + key must zoom in after - zoomed out.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.N, Pressed = true });
            if (selectedInhabitantId != founder.Id ||
                !mapCanvas.GetGlobalRect().Encloses(inhabitantVisuals[founder.Id].GetGlobalRect()))
                throw new InvalidOperationException($"N must select the next living agent and bring them into view: selected={selectedInhabitantId} marker={inhabitantVisuals[founder.Id].GetGlobalRect()}.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.N, Pressed = true });
            if (selectedInhabitantId != founder.Id)
                throw new InvalidOperationException("N with a single living agent must keep them selected.");
            ClearTileSelection();
            // Escape steps back from the Profile to the quick card, then clears the selection.
            RenderSelectedInhabitantCard(renderedMapSnapshot!);
            OpenAgentProfile(speak: false);
            OpenThoughtsReader();
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (thoughtsPanel.Visible || !agentProfilePanel.Visible)
                throw new InvalidOperationException("Escape must close the thoughts reader before the Profile.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (agentProfilePanel.Visible || !selectedInhabitantCard.Visible)
                throw new InvalidOperationException("Escape must step back from the Profile to the quick card.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (selectedInhabitantCard.Visible || selectedInhabitantId is not null)
                throw new InvalidOperationException("Escape on the quick card must clear the selection.");
            selectedInhabitantCard.Hide();
            agentProfilePanel.Hide();
            selectedInhabitantId = null;
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (!gameMenuPanel.Visible || !topBarShade.Visible)
                throw new InvalidOperationException("Escape with nothing open must open the Pause Menu.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.I, Pressed = true });
            if (worldInfoPanel.Visible)
                throw new InvalidOperationException("Map shortcuts must not act behind the Pause Menu.");
            _UnhandledKeyInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (gameMenuPanel.Visible)
                throw new InvalidOperationException("Escape must close the Pause Menu.");
            quitGameButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (quitGameConfirmation.DialogText.Contains("saved", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Quit Game is offered only on the Main Menu, so it must not talk about saving progress.");
            if (!quitGameConfirmation.Visible)
                throw new InvalidOperationException("Quit Game must ask for confirmation before exiting.");
            quitGameConfirmation.Hide();
            // Confirmations hug their message: a short one leaves no empty space
            // and a long one wraps at a readable width instead of stretching.
            var shortDialog = FitDialog(quitGameConfirmation);
            var dialogMargins = quitGameConfirmation.GetThemeStylebox("panel").GetMinimumSize();
            deletionConfirmation.DialogText = "Permanently delete ‘A world with quite a long name’ and all of its manual saves and autosaves? Your other worlds and account settings stay unchanged. There is no undo.";
            var longDialog = FitDialog(deletionConfirmation);
            if (shortDialog.Y >= 120 || quitGameConfirmation.GetLabel().GetLineCount() != 1 ||
                longDialog.X != DialogTextWidth + (int)dialogMargins.X || deletionConfirmation.GetLabel().GetLineCount() < 2 ||
                longDialog.Y <= shortDialog.Y)
                throw new InvalidOperationException($"Confirmations must fit their message: short {shortDialog}, long {longDialog}.");
            await VerifyRefusedAgentRenameAsync();
            GD.Print("UI checks passed: startup Main Menu and settings, compact in-world pause menu and read-only Mod Library, confirmed quit, World Info Towns page, resource hover, square tile hover and agent priority, agent facings, walk steps and activity frames, bounded marker hitboxes at zoom, building footprints, mountain relief chunks drawn off the main thread, soft snow edges and desert cacti, camera-bounded large terrain and regional weather, zoom, middle-drag, WASD, overview navigation, Event Log jumps without pop-ups, keyboard shortcuts and the F1 controls list, Developer tools on F12 with readouts, agent jumps and planned paths, private thoughts, memories, deceased inspection, family tree and refused agent renames.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.Message);
            GetTree().Quit(1);
        }
    }

    private void VerifyEventLogAgentNames()
    {
        const string founderId = "founder:00000000000000000000000000000001";
        const string agentId = "agent:00000000000000000000000000000099";
        const string childId = "world:inhabitant:birth:" + founderId + ":" + agentId + ":1";
        var position = new OwnerWorldPosition(0, 0);
        OwnerWorldInhabitant Person(string id, string name, string lifecycle = "active") =>
            new(id, name, lifecycle, position, 8_000, [], [],
                new("idle", null, null, [], ""), new(position, [], []), false);
        var snapshot = new OwnerWorldSnapshot("event-name-smoke", 1, "event-name-map",
            [new(0, 0, "meadow")], [], [], null, 6)
        {
            Inhabitants = [Person(founderId, "Rowan"), Person(agentId, "Aster", "dead"),
                Person(childId, "Mira"), Person("founder-scout", "Scout")],
            Towns = [new("town:first", "First Town", "founded", 0, [], [], [])],
        };
        OwnerWorldEvent[] events =
        [
            new(1, 1, "food_harvested", founderId + ":4"),
            new(2, 1, "food_consumed", "founder-scout"),
            new(3, 1, "child_born", childId),
            new(4, 1, "inhabitant_removed", agentId),
            new(5, 1, "town_resident_joined", "town:first:" + founderId + ":founder_joined:residents:4"),
            new(6, 1, "town_resident_left", "town:first:" + agentId + ":residents:3"),
        ];
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        try
        {
            var baseline = new OwnerWorldReconnectBaseline(snapshot, new(1, 0, events));
            if (!observationSession.TryAccept(new(handshake, baseline), 0, out var failure))
                throw new InvalidOperationException("Event Log name fixture was rejected: " + failure);
            foreach (var worldEvent in events) knownEvents[worldEvent.EventId] = worldEvent;
            RenderEventLog();
            string[] expected = ["Rowan gathered food.", "Scout ate.", "Mira was born.", "Aster died.",
                "Rowan joined the first Town.", "Aster left the first Town."];
            if (expected.Any(text => !eventLog.GetParsedText().Contains(text, StringComparison.Ordinal)))
                throw new InvalidOperationException("The Event Log must show full living, deceased and descendant names for normal IDs.");
            snapshot = snapshot with
            {
                Inhabitants = snapshot.Inhabitants.Select(person => person.Id == founderId
                    ? person with { DisplayName = "Renamed Rowan" } : person).ToArray(),
            };
            baseline = baseline with { Snapshot = snapshot };
            if (!observationSession.TryAccept(new(handshake, baseline), 0, out failure))
                throw new InvalidOperationException("Renamed Event Log fixture was rejected: " + failure);
            RenderEventLog();
            if (!eventLog.GetParsedText().Contains("Renamed Rowan gathered food.", StringComparison.Ordinal))
                throw new InvalidOperationException("An existing Event Log entry must resolve the current agent name after rename.");
        }
        finally
        {
            observationSession.ResetAfterLoad();
            knownEvents.Clear();
            UpdateUnreadEvents(null);
            renderedEventLog = string.Empty;
            RenderEventLog();
        }
    }
}
