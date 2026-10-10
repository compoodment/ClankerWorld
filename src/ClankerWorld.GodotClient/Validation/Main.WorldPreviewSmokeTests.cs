using System.Collections.Concurrent;
using System.Diagnostics;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyPreviewAfterPendingSettingsAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousName = worldNameInput.Text;
        var previousSeed = worldSeedInput.Text;
        var previousUsage = usageStatus;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        var previews = new ConcurrentQueue<OwnerWorldCreationAction>();
        var settings = new OwnerUsageStatus(2, 2, 0, 0, 0, 0, 1000, false, []);
        host.PreviewHandler = action =>
        {
            previews.Enqueue(action);
            return Task.FromResult(WorldPreviewSmokeReply("after-settings", 1));
        };
        Task? pendingSettings = null;
        TaskCompletionSource? release = null;
        try
        {
            deviceKey = signer;
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            worldUrlInput.Text = host.Address;
            var reroll = worldSeedInput.GetParent().GetChildren().OfType<Button>().Single(button => button.Text == "Reroll");
            for (var scenario = 0; scenario < 3; scenario++)
            {
                ShowMainMenu();
                mainMenuSettingsButton.EmitSignal(BaseButton.SignalName.Pressed);
                usageStatus = settings;
                usageAttemptLimitInput.Text = "1000";
                RefreshControlAvailability();
                var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var heldReply = release;
                host.UsageLimitHandler = async action =>
                {
                    if (action.AttemptLimit != 1000)
                        throw new InvalidOperationException("The pending Settings check must send the actual signed limit action.");
                    received.TrySetResult();
                    await heldReply.Task.ConfigureAwait(false);
                    return settings;
                };
                pendingSettings = ConfigureUsageAsync(grant: false);
                await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                menuCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
                mainMenuNewButton.EmitSignal(BaseButton.SignalName.Pressed);
                await WaitForWorldPreviewSmokeAsync(() => worldMenuOverlay.Visible && worldMenuColumns.Visible,
                    "New World after its startup recovery check");
                if (!isOwnerAction || !worldMenuOverlay.Visible || !worldMenuColumns.Visible)
                    throw new InvalidOperationException("New World must open through the real controls while the Settings reply is held.");
                var before = previews.Count;
                // First prove opening alone schedules the initial preview. Then
                // change the seed twice while blocked to check the latest options.
                if (scenario != 0)
                {
                    reroll.EmitSignal(BaseButton.SignalName.Pressed);
                    reroll.EmitSignal(BaseButton.SignalName.Pressed);
                }
                var expected = CurrentWorldOptions();
                await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
                if (previews.Count != before || !worldCreateButton.Disabled)
                    throw new InvalidOperationException("The preview must wait until the pending owner action releases its gate.");
                if (scenario == 2)
                {
                    worldBackButton.EmitSignal(BaseButton.SignalName.Pressed);
                    mainMenuLoadButton.EmitSignal(BaseButton.SignalName.Pressed);
                    await WaitForWorldPreviewSmokeAsync(() => worldMenuOverlay.Visible && !worldMenuColumns.Visible,
                        "Load World after its startup recovery check");
                }
                release.TrySetResult();
                await pendingSettings.WaitAsync(TimeSpan.FromSeconds(5));
                if (scenario != 2)
                {
                    await WaitForWorldPreviewSmokeAsync(() => previewedWorldResult is not null && !worldMenuBusy,
                        "the automatic preview after the Settings acknowledgement (#1023)");
                    if (previews.Count != before + 1 || previews.Last().Seed != expected.Seed ||
                        !worldPreview.Visible || worldCreateButton.Disabled)
                        throw new InvalidOperationException("Finishing Settings must request exactly one preview for the latest seed and enable Create.");
                }
                else
                {
                    await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
                    if (previews.Count != before || worldMenuColumns.Visible || worldMenuHeading.Text != "Load World")
                        throw new InvalidOperationException("Switching to Load World must discard the blocked New World preview.");
                }
                worldMenuOverlay.Hide();
                InvalidateWorldPreview(refresh: false);
            }
        }
        finally
        {
            worldMenuOverlay.Hide();
            InvalidateWorldPreview(refresh: false);
            release?.TrySetResult();
            if (pendingSettings is not null) await pendingSettings.WaitAsync(TimeSpan.FromSeconds(5));
            worldListRequest.Cancel();
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            worldNameInput.Text = previousName;
            worldSeedInput.Text = previousSeed;
            usageStatus = previousUsage;
            settingsPanel.Hide();
            CloseGameMenu();
            ShowMainMenu();
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            statusToast.Hide();
        }
    }

    private async Task VerifyWorldPreviewRerollAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousName = worldNameInput.Text;
        var previousSeed = worldSeedInput.Text;
        var previousOverlayVisible = worldMenuOverlay.Visible;
        var previousColumnsVisible = worldMenuColumns.Visible;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        var requests = new ConcurrentQueue<WorldPreviewSmokeRequest>();
        var draining = 0;
        OwnerDeviceKey? signer = null;
        WorldActionSmokeHost? host = null;
        Exception? failure = null;
        try
        {
            System.Environment.SetEnvironmentVariable("CI", "true");
            signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
            host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64)
            {
                PreviewHandler = action =>
                {
                    var request = new WorldPreviewSmokeRequest(action);
                    requests.Enqueue(request);
                    // A request may reach the host after cleanup began enumerating the queue.
                    if (Volatile.Read(ref draining) != 0) request.Reply.TrySetResult(WorldPreviewSmokeReply("cleanup", 0));
                    return request.Reply.Task;
                },
            };
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            worldNameInput.Text = "Reroll smoke";
            worldSeedInput.Text = "initial-seed";
            worldMenuOverlay.Show();
            worldMenuColumns.Show();
            InvalidateWorldPreview(refresh: false);
            var reroll = worldSeedInput.GetParent().GetChildren().OfType<Button>().Single(button => button.Text == "Reroll");

            worldPreviewButton.EmitSignal(BaseButton.SignalName.Pressed);
            await WaitForWorldPreviewSmokeAsync(() => !requests.IsEmpty, "the initial signed preview request");
            requests.ElementAt(0).Reply.SetResult(WorldPreviewSmokeReply("initial", 1));
            await WaitForWorldPreviewSmokeAsync(() => !worldMenuBusy && previewedWorldResult?.ManifestDigest == "initial", "the initial preview");
            if (requests.Count != 1 || !worldPreview.Visible || worldPreviewStatus.TooltipText.Length == 0 || worldCreateButton.Disabled)
                throw new InvalidOperationException("The Reroll check must start with one visible, creatable signed preview.");

            // Programmatic Text assignment does not emit TextChanged. Press only the real Reroll button.
            reroll.EmitSignal(BaseButton.SignalName.Pressed);
            RequireWorldPreviewCleared();
            var firstRerollSeed = worldSeedInput.Text;
            if (firstRerollSeed == "initial-seed") throw new InvalidOperationException("Reroll must change the seed.");
            await WaitForWorldPreviewSmokeAsync(() => requests.Count >= 2, "the rerolled preview request");
            if (requests.Count != 2 || requests.ElementAt(1).Action.Seed != firstRerollSeed || !worldMenuBusy)
                throw new InvalidOperationException("The first Reroll must request its new seed and wait for the held reply.");

            // Coalesce changes while the older request is still in flight, without pressing Preview again.
            reroll.EmitSignal(BaseButton.SignalName.Pressed);
            reroll.EmitSignal(BaseButton.SignalName.Pressed);
            RequireWorldPreviewCleared();
            var latestSeed = worldSeedInput.Text;
            if (latestSeed == firstRerollSeed || !worldMenuBusy)
                throw new InvalidOperationException("The second Reroll must change the seed while the older reply is held.");
            requests.ElementAt(1).Reply.SetResult(WorldPreviewSmokeReply("obsolete", 2));
            await WaitForWorldPreviewSmokeAsync(() => requests.Count >= 3, "one replacement for the latest seed");
            RequireWorldPreviewCleared();
            if (requests.Count != 3 || requests.ElementAt(2).Action.Seed != latestSeed)
                throw new InvalidOperationException("Rerolls during generation must coalesce into one request for the latest seed.");
            requests.ElementAt(2).Reply.SetResult(WorldPreviewSmokeReply("latest", 3));
            await WaitForWorldPreviewSmokeAsync(() => !worldMenuBusy && previewedWorldResult?.ManifestDigest == "latest", "the latest preview");
            if (requests.Count != 3 || previewedWorldOptions?.Seed != latestSeed || !worldPreview.Visible ||
                worldCreateButton.Disabled || !worldPreviewStatus.Text.Contains("3 places", StringComparison.Ordinal))
                throw new InvalidOperationException("Only the latest rerolled preview may become visible and enable Create.");

            worldNameInput.Text = string.Empty;
            reroll.EmitSignal(BaseButton.SignalName.Pressed);
            RequireWorldPreviewCleared();
            await WaitForWorldPreviewSmokeAsync(() => worldPreviewStatus.Text == "Enter a world name and a seed first.",
                "validation after the Reroll debounce");
            RequireWorldPreviewCleared();
            if (requests.Count != 3)
                throw new InvalidOperationException("Reroll with an empty name must not send another preview request.");
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            try
            {
                Interlocked.Exchange(ref draining, 1);
                worldMenuOverlay.Hide();
                InvalidateWorldPreview(refresh: false);
                foreach (var request in requests) request.Reply.TrySetResult(WorldPreviewSmokeReply("cleanup", 0));
                await WaitForWorldPreviewSmokeAsync(() => !worldMenuBusy, "preview request cleanup");
            }
            catch (Exception cleanupFailure) when (failure is not null)
            {
                GD.Print("Reroll smoke cleanup also failed: " + cleanupFailure.Message);
            }
            finally
            {
                // A same-seed reply can arrive during cleanup on older clients. Clear it before restoring the menu.
                InvalidateWorldPreview(refresh: false);
                registration = previousRegistration;
                deviceKey = previousKey;
                worldUrlInput.Text = previousUrl;
                worldNameInput.Text = previousName;
                worldSeedInput.Text = previousSeed;
                worldMenuColumns.Visible = previousColumnsVisible;
                worldMenuOverlay.Visible = previousOverlayVisible;
                System.Environment.SetEnvironmentVariable("CI", previousCi);
                host?.Dispose();
                signer?.Dispose();
            }
        }
    }

    private void RequireWorldPreviewCleared()
    {
        if (worldPreview.Visible || worldPreviewStatus.TooltipText.Length != 0 || !worldCreateButton.Disabled ||
            worldAcceptUnmetTargets.ButtonPressed || previewedWorldOptions is not null || previewedWorldResult is not null)
            throw new InvalidOperationException("Reroll must immediately clear the old preview and measurements and disable Create.");
    }

    private async Task WaitForWorldPreviewSmokeAsync(Func<bool> condition, string boundary)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5))
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!condition()) throw new TimeoutException($"Timed out waiting for {boundary}: {worldPreviewStatus.Text}");
    }

    private static OwnerWorldPreview WorldPreviewSmokeReply(string digest, int sites) =>
        new(new(1, 1, "terrain-kind-v1", "AA=="), new(0, 0), digest, sites)
        {
            MapLayersDigest = "layers-" + digest,
            Coverage = new(0, 1, 0, 0, 0, 0, 0, 0, 0, 0, false, false, true, true),
        };

    private sealed record WorldPreviewSmokeRequest(OwnerWorldCreationAction Action)
    {
        public TaskCompletionSource<OwnerWorldPreview> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
