using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyUsageSettingsRepliesAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousUsage = usageStatus;
        var previousObservation = observationSession.Current;
        var previousInWorld = isInWorld;
        var registrationPath = ProjectSettings.GlobalizePath("user://owner-device-registration.json");
        var previousRegistrationFile = File.Exists(registrationPath) ? File.ReadAllBytes(registrationPath) : null;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        var failures = new List<string>();
        var healthy = new OwnerUsageStatus(2, 2, 0, 0, 0, 0, 1000, false, []);
        async Task CheckAsync(Func<Task> check)
        {
            try { await check(); }
            catch (InvalidOperationException exception) { failures.Add(exception.Message); }
        }
        void Connect(WorldActionSmokeHost host)
        {
            deviceKey = signer;
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            worldUrlInput.Text = host.Address;
            ShowMainMenu();
            CloseGameMenu();
        }
        async Task TypeLimitAsync(string text)
        {
            usageAttemptLimitInput.GrabFocus();
            usageAttemptLimitInput.SelectAll();
            if (text.Length == 0)
                GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = Key.Backspace }, true);
            foreach (var character in text)
                GetViewport().PushInput(new InputEventKey { Pressed = true, Unicode = character }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (usageAttemptLimitInput.Text != text)
                throw new InvalidOperationException("The native key events must edit the actual Model calls limit field.");
        }
        try
        {
            await CheckAsync(async () =>
            {
                using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64) { Usage = healthy };
                Connect(host);
                usageStatus = healthy;
                RenderUsageStatus();
                for (var focusCase = 0; focusCase < 3; focusCase++)
                {
                    var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    host.UsageHandler = async () =>
                    {
                        var captured = healthy;
                        received.TrySetResult();
                        await release.Task.ConfigureAwait(false);
                        return captured;
                    };
                    OpenMainMenuSettings();
                    await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    var draft = focusCase == 2 ? string.Empty : "10";
                    try
                    {
                        await TypeLimitAsync(draft);
                        if (focusCase != 0) applyUsageLimitButton.GrabFocus();
                    }
                    finally { release.TrySetResult(); }
                    // The real Settings signal started this read; wait for its renderer to run.
                    for (var frame = 0; frame < 30; frame++)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (usageAttemptLimitInput.Text != draft)
                        throw new InvalidOperationException("#1022: an unfocused draft limit was overwritten by a delayed usage read.");
                    await ConfigureUsageAsync(grant: false);
                    long? expectedLimit = draft.Length == 0 ? null : 10;
                    if (host.UsageLimits.Last().AttemptLimit != expectedLimit || usageStatus?.AttemptLimit != expectedLimit)
                        throw new InvalidOperationException("Set limit must submit the player's edited value and accept its acknowledgement.");
                    menuCloseButton.EmitSignal(BaseButton.SignalName.Pressed);
                }
                host.UsageHandler = () => Task.FromResult<OwnerUsageStatus?>(healthy);
                await RefreshUsageAsync();
                if (usageAttemptLimitInput.Text != "1000")
                    throw new InvalidOperationException("An untouched limit must follow the host after successful application clears its draft.");
            });

            await CheckAsync(async () =>
            {
                using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64) { Usage = healthy with { AttemptLimit = 2, LimitReached = true } };
                Connect(host);
                var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var reads = 0;
                host.UsageHandler = async () =>
                {
                    var captured = host.Usage;
                    if (Interlocked.Increment(ref reads) == 1)
                    {
                        received.TrySetResult();
                        await release.Task.ConfigureAwait(false);
                    }
                    return captured;
                };
                host.Reconnect = new(new(new(1, 1),
                    ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                     "owner-control.request.v1", "paused-authoring.request.v1"], []),
                    new(new("usage-world", 0, "usage-map", [new(0, 0, "meadow")], [], [], null, 0)
                    { Authoring = new(true, 0, 0, 0, "usage-map", "usage-map", "clear", "spring", []) }, new(0, 0, [])));
                wasObservedPaused = false;
                await RefreshAsync();
                await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    OpenMainMenuSettings();
                    await RefreshUsageAsync();
                    await TypeLimitAsync("10");
                    applyUsageLimitButton.GrabFocus();
                    await ConfigureUsageAsync(grant: false);
                    if (usageStatus?.AttemptLimit != 10 || host.Usage?.AttemptLimit != 10)
                        throw new InvalidOperationException("The signed limit action must acknowledge the new limit before releasing the old pause read.");
                }
                finally { release.TrySetResult(); }
                for (var frame = 0; frame < 30; frame++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (usageStatus?.AttemptLimit != 10 || usageStatus.LimitReached || usageAttemptLimitInput.Text != "10")
                    throw new InvalidOperationException("#1034: an older pause reply replaced the acknowledged limit or reached state.");
                await ConfigureUsageAsync(grant: false);
                if (host.UsageLimits.Last().AttemptLimit != 10)
                    throw new InvalidOperationException("A second Set limit must submit the acknowledged value, not a stale reply.");
            });

            await CheckAsync(async () =>
            {
                using var oldHost = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
                using var newHost = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
                Connect(oldHost);
                usageStatus = healthy with { AccountingError = "Previous host accounting unavailable." };
                RenderUsageStatus();
                var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                oldHost.UsageHandler = async () =>
                {
                    received.TrySetResult();
                    await release.Task.ConfigureAwait(false);
                    return healthy with { AccountingError = "Previous host accounting unavailable." };
                };
                var oldRead = RefreshUsageAsync();
                await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                ForgetLocalRegistration();
                Connect(newHost);
                newHost.UsageHandler = () => Task.FromResult<OwnerUsageStatus?>(null);
                await RefreshUsageAsync();
                release.TrySetResult();
                await oldRead.WaitAsync(TimeSpan.FromSeconds(5));
                if (usageStatus is not null || !usageAttemptLimitInput.Editable || applyUsageLimitButton.Disabled ||
                    usageAttemptLimitInput.Text.Length != 0 || grantUsageCallsButton.Visible)
                    throw new InvalidOperationException("#1032: replacing a host retained its old accounting error or limit controls after the new read failed.");
                newHost.UsageHandler = () => Task.FromResult<OwnerUsageStatus?>(healthy with { AttemptLimit = 10 });
                await RefreshUsageAsync();
                if (usageStatus?.AttemptLimit != 10 || usageAttemptLimitInput.Text != "10")
                    throw new InvalidOperationException("Retry must display the new host's own healthy limit.");
            });
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
        }
        finally
        {
            registration = previousRegistration;
            if (previousRegistrationFile is null) registrationStore.Forget();
            else File.WriteAllBytes(registrationPath, previousRegistrationFile);
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            usageStatus = previousUsage;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            pairingPanel.Hide();
            settingsPanel.Hide();
            CloseGameMenu();
            ShowMainMenu();
            isInWorld = previousInWorld;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            statusToast.Hide();
        }
    }
}
