using System.Collections.Concurrent;
using System.Diagnostics;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
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
