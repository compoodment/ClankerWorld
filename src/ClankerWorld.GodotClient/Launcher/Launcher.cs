using System.Globalization;
using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient.Launcher;

/// <summary>
/// The launcher window: pick a version and play, manage installed versions,
/// and change launcher settings. It uses the game's own theme, fonts, logo and
/// valley backdrop. Play opens the chosen version at its Main Menu and closes
/// the launcher; worlds are still chosen in the game. Nothing here updates
/// without the player's click, and the launcher never replaces itself.
/// </summary>
public partial class Launcher : Control
{
    /// <summary>The launcher's own version, separate from game versions.</summary>
    public const string Version = "1.0.0";

    private readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly MenuBackdrop backdrop = new();
    private readonly Label status = new();
    private readonly Label launcherUpdate = new();
    private readonly OptionButton versionChoice = new();
    private readonly Button playButton = new();
    private readonly ProgressBar progressBar = new() { MinValue = 0, MaxValue = 1, Step = 0.001, Visible = false };
    private readonly VBoxContainer installedRows = new();
    private readonly VBoxContainer availableRows = new();
    private readonly CheckButton developerMode = new() { Text = "Developer mode" };
    private readonly Label developerHint = new();
    private readonly Button playTab = new() { Text = "Play" };
    private readonly Button versionsTab = new() { Text = "Versions" };
    private readonly Button settingsTab = new() { Text = "Settings" };
    private readonly Control playPage = new VBoxContainer();
    private readonly Control versionsPage = new VBoxContainer();
    private readonly Control settingsPage = new VBoxContainer();
    private LauncherLayout layout = null!;
    private GameVersionStore store = null!;
    private GameReleaseFeed feed = null!;
    private LauncherSettings settings = new();
    private IReadOnlyList<InstalledVersion> installed = [];
    private ReleaseFeedResult releases = new([], null);
    private bool releasesReached;
    private bool busy;

    public override void _Ready()
    {
        var smokeTest = OS.GetCmdlineUserArgs().Contains("--launcher-smoke-test", StringComparer.Ordinal);
        layout = smokeTest
            ? new LauncherLayout(Path.Combine(Path.GetTempPath(), "clankerworld-launcher-check-" + Guid.NewGuid().ToString("N")))
            : LauncherLayout.ForUser(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData));
        store = new GameVersionStore(layout, http);
        feed = new GameReleaseFeed(http);
        settings = LauncherSettings.Load(layout);
        if (DisplayServer.GetName() != "headless") DisplayServer.SetIcon(MenuLogo.Icon(64));
        TextureFilter = TextureFilterEnum.Nearest;
        // A smaller window than the game's, at the same pixel scale.
        if (DisplayServer.GetName() != "headless")
        {
            GetTree().Root.ContentScaleSize = new Vector2I(960, 640);
            DisplayServer.WindowSetSize(new Vector2I(960, 640));
            GetWindow().MoveToCenter();
        }
        // The launcher follows the theme chosen in the game's settings.
        var preferences = new GameDisplayPreferencesStore(ProjectSettings.GlobalizePath("user://game-display-preferences.json")).Load();
        UiTheme.Apply(GetTree().Root, UiTheme.Resolve(UiTheme.Parse(preferences.Theme)));
        Build();
        store.CleanUp();
        RefreshInstalled();
        if (smokeTest)
        {
            _ = RunSmokeTestAsync();
            return;
        }
        _ = CheckForUpdatesAsync();
    }

    public override void _ExitTree() => http.Dispose();

    private void Build()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = UiTheme.Current.Backdrop, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.Night = ReferenceEquals(UiTheme.Current, UiTheme.Dark);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 14);
        center.AddChild(stack);
        var logo = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(MenuLogo.Create()),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(MenuLogo.Width, MenuLogo.Height) * 2,
        };
        stack.AddChild(logo);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(560, 0), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        stack.AddChild(card);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        card.AddChild(body);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 6);
        foreach (var (tab, page) in new[] { (playTab, playPage), (versionsTab, versionsPage), (settingsTab, settingsPage) })
        {
            StyleButton(tab);
            tab.ToggleMode = true;
            tab.ThemeTypeVariation = "TabButton";
            tab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            tab.Pressed += () => ShowPage(page);
            tabs.AddChild(tab);
        }
        body.AddChild(tabs);
        launcherUpdate.Visible = false;
        launcherUpdate.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(launcherUpdate);

        BuildPlayPage();
        BuildVersionsPage();
        BuildSettingsPage();
        foreach (var page in new[] { playPage, versionsPage, settingsPage }) body.AddChild(page);

        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.CustomMinimumSize = new Vector2(0, 24);
        body.AddChild(progressBar);
        body.AddChild(status);
        ShowPage(playPage);
    }

    private void BuildPlayPage()
    {
        ((BoxContainer)playPage).AddThemeConstantOverride("separation", 10);
        playPage.AddChild(new Label { Text = "Version" });
        versionChoice.ItemSelected += _ => UpdatePlayButton();
        playPage.AddChild(versionChoice);
        playButton.Text = "Play";
        StyleButton(playButton, primary: true);
        playButton.AddThemeFontOverride("font", UiFonts.Headings);
        playButton.AddThemeFontSizeOverride("font_size", UiFonts.Heading);
        playButton.Pressed += () => _ = PlayAsync();
        playPage.AddChild(playButton);
    }

    private void BuildVersionsPage()
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 260), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        list.AddChild(SectionLabel("Installed"));
        list.AddChild(installedRows);
        list.AddChild(SectionLabel("Available to install"));
        list.AddChild(availableRows);
        scroll.AddChild(list);
        versionsPage.AddChild(scroll);
    }

    private void BuildSettingsPage()
    {
        ((BoxContainer)settingsPage).AddThemeConstantOverride("separation", 10);
        developerMode.ButtonPressed = settings.DeveloperMode;
        developerMode.Toggled += on =>
        {
            settings = settings with { DeveloperMode = on };
            SaveSettings();
            UpdateDeveloperHint();
        };
        settingsPage.AddChild(developerMode);
        developerHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        developerHint.AddThemeColorOverride("font_color", UiTheme.Current.InkMuted);
        settingsPage.AddChild(developerHint);
        UpdateDeveloperHint();
        settingsPage.AddChild(new HSeparator());
        var folders = new HBoxContainer();
        folders.AddThemeConstantOverride("separation", 6);
        folders.AddChild(FolderButton("Open saves folder", layout.SavesDirectory));
        folders.AddChild(FolderButton("Open logs folder", layout.LogsDirectory));
        settingsPage.AddChild(folders);
        var check = new Button { Text = "Check for updates" };
        StyleButton(check);
        check.Pressed += () => _ = CheckForUpdatesAsync();
        settingsPage.AddChild(check);
        settingsPage.AddChild(new Label
        {
            Text = $"Launcher {Version}",
            ThemeTypeVariation = string.Empty,
        });
    }

    private void UpdateDeveloperHint() => developerHint.Text = settings.DeveloperMode
        ? "Developer tools open with F12 in the game."
        : "Turn on to open Developer tools with F12 in the game.";

    private static Button FolderButton(string text, string path)
    {
        var button = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        StyleButton(button);
        button.Pressed += () =>
        {
            Directory.CreateDirectory(path);
            OS.ShellShowInFileManager(path, openFolder: true);
        };
        return button;
    }

    private static Label SectionLabel(string text)
    {
        var label = new Label { Text = text.ToUpperInvariant() };
        label.AddThemeColorOverride("font_color", UiTheme.Current.InkMuted);
        return label;
    }

    private static void StyleButton(Button button, bool primary = false)
    {
        button.CustomMinimumSize = new Vector2(0, 34);
        button.ThemeTypeVariation = primary ? "PrimaryButton" : string.Empty;
    }

    private void ShowPage(Control page)
    {
        playPage.Visible = page == playPage;
        versionsPage.Visible = page == versionsPage;
        settingsPage.Visible = page == settingsPage;
        playTab.SetPressedNoSignal(page == playPage);
        versionsTab.SetPressedNoSignal(page == versionsPage);
        settingsTab.SetPressedNoSignal(page == settingsPage);
    }

    private async Task CheckForUpdatesAsync()
    {
        SetStatus("Checking for updates…");
        try
        {
            releases = await feed.FetchAsync(CancellationToken.None);
            releasesReached = true;
            SetStatus(installed.Count == 0 && releases.Games.Count == 0 ? "No game versions are published yet." : "");
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or TaskCanceledException or
            System.Text.Json.JsonException)
        {
            releasesReached = false;
            SetStatus(installed.Count > 0
                ? "Couldn't check for updates. You can still play an installed version."
                : "Couldn't reach ClankerWorld's releases. Check your internet connection and try again.");
        }
        ShowLauncherUpdate();
        RefreshInstalled();
    }

    private void ShowLauncherUpdate()
    {
        var newest = releases.NewestLauncher;
        var own = GameVersionName.Parse(Version);
        launcherUpdate.Visible = newest is not null && own is not null && newest.Version.CompareTo(own) > 0;
        if (launcherUpdate.Visible)
            launcherUpdate.Text = $"Launcher {newest!.Version} is available. Download it from {newest.Page} when you're ready.";
    }

    private void RefreshInstalled()
    {
        installed = store.Installed();
        var previous = versionChoice.Selected >= 0 ? versionChoice.GetItemText(versionChoice.Selected) : settings.LastPlayedVersion;
        versionChoice.Clear();
        foreach (var name in Choices())
        {
            versionChoice.AddItem(name);
            if (name == previous) versionChoice.Select(versionChoice.ItemCount - 1);
        }
        if (versionChoice.Selected < 0 && versionChoice.ItemCount > 0) versionChoice.Select(0);
        UpdatePlayButton();
        RebuildVersionRows();
    }

    /// <summary>Installed versions first, newest first; the newest release if it isn't installed yet.</summary>
    private IEnumerable<string> Choices()
    {
        var newest = releases.Games.FirstOrDefault(release => !release.PreRelease) ??
            (releases.Games.Count > 0 ? releases.Games[0] : null);
        if (newest is not null && installed.All(version => version.Version != newest.Version))
            yield return newest.Version.ToString();
        foreach (var version in installed) yield return version.Version.ToString();
    }

    private void UpdatePlayButton()
    {
        var chosen = Chosen();
        playButton.Disabled = busy || chosen is null;
        playButton.Text = chosen is null ? "Play"
            : installed.Any(version => version.Version == chosen) ? $"Play {chosen}" : $"Install and Play {chosen}";
    }

    private GameVersionName? Chosen() =>
        versionChoice.Selected < 0 ? null : GameVersionName.Parse(versionChoice.GetItemText(versionChoice.Selected));

    private void RebuildVersionRows()
    {
        foreach (var rows in new[] { installedRows, availableRows })
            foreach (var child in rows.GetChildren())
            {
                rows.RemoveChild(child);
                child.QueueFree();
            }
        var worlds = SavedWorlds.Read(layout);
        foreach (var version in installed)
        {
            var needing = worlds.Where(world => world.SavedBy == version.Version).Select(world => world.Name).ToArray();
            var detail = $"{FormatSize(version.SizeBytes)}" +
                (needing.Length == 0 ? "" : $" · Last saved: {string.Join(", ", needing)}");
            installedRows.AddChild(VersionRow(version.Version.ToString(), detail,
                ("Repair", () => _ = RepairAsync(version)), ("Remove", () => Remove(version, needing.Length))));
        }
        if (installed.Count == 0) installedRows.AddChild(new Label { Text = "No versions installed yet." });
        var available = releases.Games.Where(release => installed.All(version => version.Version != release.Version)).ToArray();
        foreach (var release in available)
            availableRows.AddChild(VersionRow(release.Version.ToString(),
                FormatSize(release.Package.Size) + (release.PreRelease ? " · Pre-release" : ""),
                ("Install", () => _ = InstallAsync(release, playAfter: false))));
        if (available.Length == 0)
            availableRows.AddChild(new Label
            {
                Text = releasesReached ? "Every published version is installed." : "Couldn't check for new versions.",
            });
    }

    private PanelContainer VersionRow(string name, string detail, params (string Text, Action Pressed)[] actions)
    {
        var row = new PanelContainer { ThemeTypeVariation = "InsetRow" };
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddChild(new Label { Text = name });
        var small = new Label { Text = detail, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        small.AddThemeColorOverride("font_color", UiTheme.Current.InkMuted);
        text.AddChild(small);
        line.AddChild(text);
        foreach (var (label, pressed) in actions)
        {
            var button = new Button { Text = label, Disabled = busy };
            StyleButton(button);
            if (label == "Remove") button.ThemeTypeVariation = "DangerButton";
            button.Pressed += pressed;
            line.AddChild(button);
        }
        row.AddChild(line);
        return row;
    }

    private async Task PlayAsync()
    {
        var chosen = Chosen();
        if (chosen is null || busy) return;
        var version = installed.FirstOrDefault(candidate => candidate.Version == chosen);
        if (version is null)
        {
            var release = releases.Games.FirstOrDefault(candidate => candidate.Version == chosen);
            if (release is null) return;
            version = await InstallAsync(release, playAfter: true);
            if (version is null) return;
        }
        try
        {
            GameStarter.Start(version, OS.GetExecutablePath(), settings.DeveloperMode);
        }
        catch (Exception exception) when (exception is GameInstallException or System.ComponentModel.Win32Exception or IOException)
        {
            SetStatus($"ClankerWorld {version.Version} didn't start. Try Repair on the Versions page.");
            return;
        }
        settings = settings with { LastPlayedVersion = version.Version.ToString() };
        SaveSettings();
        // The launcher closes while the game runs.
        GetTree().Quit();
    }

    private async Task<InstalledVersion?> InstallAsync(GameRelease release, bool playAfter)
    {
        if (busy) return null;
        SetBusy(true);
        SetStatus($"Downloading ClankerWorld {release.Version}…");
        var progress = new Progress<double>(fraction => progressBar.Value = fraction);
        try
        {
            var version = await Task.Run(() => store.InstallAsync(release, progress, CancellationToken.None));
            SetStatus(playAfter ? $"Starting ClankerWorld {release.Version}…" : $"ClankerWorld {release.Version} is installed.");
            return version;
        }
        catch (GameInstallException exception)
        {
            SetStatus(exception.Message);
            return null;
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or TaskCanceledException or
            IOException or UnauthorizedAccessException)
        {
            SetStatus("The download stopped. Your saves are untouched. Check your connection and try again.");
            return null;
        }
        finally
        {
            SetBusy(false);
            RefreshInstalled();
        }
    }

    private async Task RepairAsync(InstalledVersion version)
    {
        SetStatus($"Checking ClankerWorld {version.Version}…");
        var problems = await Task.Run(() => GameVersionStore.Verify(version));
        if (problems.Count == 0)
        {
            SetStatus($"ClankerWorld {version.Version} is fine. Every file matches its release.");
            return;
        }
        var release = releases.Games.FirstOrDefault(candidate => candidate.Version == version.Version);
        if (release is null)
        {
            SetStatus($"{problems.Count} files in {version.Version} changed, and its release couldn't be reached to repair it.");
            return;
        }
        await InstallAsync(release, playAfter: false);
    }

    private void Remove(InstalledVersion version, int worldsNeedingIt)
    {
        var confirm = new ConfirmationDialog
        {
            Title = $"Remove {version.Version}?",
            DialogText = (worldsNeedingIt == 0 ? "" :
                    $"{worldsNeedingIt} of your worlds were last saved by this version. You can install it again later.\n") +
                "Your worlds, keys and logs are kept.",
            OkButtonText = "Remove",
        };
        confirm.Confirmed += () =>
        {
            try
            {
                store.Remove(version);
                SetStatus($"Removed ClankerWorld {version.Version}. Your worlds are kept.");
            }
            catch (GameInstallException exception) { SetStatus(exception.Message); }
            RefreshInstalled();
        };
        AddChild(confirm);
        confirm.PopupCentered();
    }

    private void SetBusy(bool value)
    {
        busy = value;
        progressBar.Visible = value;
        progressBar.Value = 0;
        UpdatePlayButton();
    }

    private void SetStatus(string text) => status.Text = text;

    private void SaveSettings()
    {
        try { settings.Save(layout); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("Couldn't save launcher settings.");
        }
    }

    public static string FormatSize(long bytes) => bytes >= 1024L * 1024 * 1024
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.0} GB")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (bytes + 1024 * 1024 - 1) / (1024 * 1024))} MB");

    private async Task RunSmokeTestAsync()
    {
        var failures = new List<string>();
        void Expect(bool condition, string what) { if (!condition) failures.Add(what); }
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Expect(playPage.Visible && !versionsPage.Visible, "Play is the first page");
            Expect(playButton.Disabled && playButton.Text == "Play", "Play waits for a version");
            releases = GameReleaseFeed.Read(
            [
                SmokeRelease("v0.1.0-alpha.2", prerelease: true),
                SmokeRelease("v0.1.0-alpha.1", prerelease: true),
                new GitHubRelease("launcher-v9.0.0", "https://github.com/ClankerWorldOrg/ClankerWorld/releases/tag/launcher-v9.0.0", false, false, []),
            ]);
            releasesReached = true;
            ShowLauncherUpdate();
            RefreshInstalled();
            Expect(playButton.Text == "Install and Play 0.1.0-alpha.2", "Play offers the newest release");
            Expect(launcherUpdate.Visible, "A newer launcher is announced, never installed");
            versionsTab.EmitSignal(BaseButton.SignalName.Pressed);
            Expect(versionsPage.Visible && availableRows.GetChildCount() == 2, "Versions lists both releases");
            settingsTab.EmitSignal(BaseButton.SignalName.Pressed);
            developerMode.ButtonPressed = true;
            Expect(LauncherSettings.Load(layout).DeveloperMode, "Developer mode is remembered");
        }
        catch (Exception exception)
        {
            failures.Add(exception.ToString());
        }
        finally
        {
            try { Directory.Delete(layout.Root, recursive: true); }
            catch (DirectoryNotFoundException) { }
        }
        if (failures.Count == 0) GD.Print("Launcher checks passed: pages, version choice, release list, launcher notice and Developer mode.");
        else GD.PrintErr("Launcher checks failed: " + string.Join("; ", failures));
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }

    private static GitHubRelease SmokeRelease(string tag, bool prerelease)
    {
        var name = GameReleaseFeed.PackageName(GameVersionName.Parse(tag[1..])!) + ".zip";
        var root = "https://github.com/ClankerWorldOrg/ClankerWorld/releases/download/" + tag + "/";
        return new GitHubRelease(tag, "https://github.com/ClankerWorldOrg/ClankerWorld/releases/tag/" + tag, false, prerelease,
            [new GitHubReleaseAsset(name, 120 * 1024 * 1024, root + name), new GitHubReleaseAsset(name + ".sha256", 100, root + name + ".sha256")]);
    }
}
