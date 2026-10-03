using System.Text.Json;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    /// <summary>Entering an untouched host starts the normal creation flow without exposing or resuming its old camp.</summary>
    private async Task VerifyFreshHostEntryAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousObservation = observationSession.Current;
        var previousInWorld = isInWorld;
        var previousResume = resumeWorldOnContinue;
        var previousMenuVisible = mainMenuOverlay.Visible;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var host = new WorldActionSmokeHost(signer.PublicKeySpkiBase64);
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        var bootstrap = new OwnerWorldSnapshot("bootstrap", 0, "entry-map", [new(0, 0, "meadow")],
            [new("campfire", "cooking", new(0, 0))], [], null, 0)
        {
            FounderSetup = new(4, 0, false) { RequiresWorldCreation = true },
            Authoring = new(true, 0, 0, 0, "entry-map", "entry-map", "clear", "spring", []),
        };
        var generated = bootstrap with
        {
            WorldId = "generated",
            Objects = [],
            FounderSetup = new(4, 0, false) { CanChooseTownSite = true },
        };
        OwnerWorldReconnect Reply(OwnerWorldSnapshot snapshot) => new(handshake, new(snapshot, new(0, snapshot.WorldTick, [])));
        int Requests(string path) => host.Requests.Count(request => request == path);
        async Task WaitForPreviewAsync()
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (worldMenuBusy || previewedWorldResult is null)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                    throw new InvalidOperationException("Entering New World must finish its normal map preview.");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        try
        {
            registration = new(host.Authority, "smoke-device", signer.PublicKeyFingerprint, host.Address);
            deviceKey = signer;
            worldUrlInput.Text = host.Address;
            host.SupportedActionPayloads = [OwnerWorldActionPayload.WorldCreationPayloadDomain];
            host.Preview = new(new(1, 1, "terrain-kind-v1", "AA=="), new(0, 0), "created-manifest")
            {
                MapLayersDigest = "created-layers",
                Coverage = new(2, 1, 0, 0, 0, 0, 0, 0, 0, 0, false, false, true, true),
            };
            host.ReleasePause.TrySetResult();
            host.ReleaseSelect.TrySetResult();
            host.Reconnect = Reply(bootstrap);
            observationSession.ResetAfterLoad();
            ShowMainMenu();
            worldMenuOverlay.Hide();
            resumeWorldOnContinue = true;

            await EnterWorldAsync();
            await WaitForPreviewAsync();
            if (isInWorld || resumeWorldOnContinue || !mainMenuOverlay.Visible || !worldMenuOverlay.Visible ||
                worldMenuHeading.Text != "New World" || !worldMenuColumns.Visible || !worldPreview.Visible ||
                worldCreateButton.Disabled || mainMenuContinueButton.Disabled || mainMenuNewButton.Disabled ||
                mainMenuLoadButton.Disabled || Requests(OwnerPairingEndpoints.OwnerResume) != 0)
                throw new InvalidOperationException("Continue on an untouched host must keep the camp behind the menus and offer New World without resuming.");

            // Creation still submits the candidate the player actually previewed, then enters founder setup.
            host.CreatedWorld = new("created-entry", "New World", generated.WorldId, worldSeedInput.Text,
                DateTimeOffset.UnixEpoch, [], null, "compatible");
            host.CreatedObservation = Reply(generated);
            await CreateSelectedWorldAsync();
            if (!isInWorld || mainMenuOverlay.Visible || worldMenuOverlay.Visible || resumeWorldOnContinue ||
                observationSession.Current?.Baseline.Snapshot.WorldId != generated.WorldId ||
                host.WorldCreations.ToArray() is not [var creation] || creation.CandidateAttempt != 2 ||
                creation.ExpectedManifestDigest != "created-manifest" || creation.ExpectedMapLayersDigest != "created-layers" ||
                Requests(OwnerPairingEndpoints.OwnerResume) != 0)
                throw new InvalidOperationException("The fresh-host route must preserve preview-bound creation and enter the generated founder setup without resuming it.");

            // Selecting the still-untouched bootstrap from Load World uses the same entry boundary.
            ShowMainMenu();
            ShowWorldActionSmokeMenu();
            var selectedWorld = new CatalogWorld("bootstrap-entry", "Earlier host", bootstrap.WorldId, "bootstrap-seed",
                DateTimeOffset.UnixEpoch, [], null, "compatible");
            host.SelectedWorld = selectedWorld;
            host.Catalog = new("created-entry", [selectedWorld]);
            host.Reconnect = Reply(bootstrap);
            await RefreshWorldListAsync();
            ChooseWorldActionSmokeRow(0);
            await SelectListedWorldAsync();
            await WaitForPreviewAsync();
            if (isInWorld || !mainMenuOverlay.Visible || !worldMenuOverlay.Visible ||
                worldMenuHeading.Text != "New World" || resumeWorldOnContinue ||
                Requests(OwnerPairingEndpoints.OwnerResume) != 0)
                throw new InvalidOperationException("Load World must route an untouched bootstrap to New World too.");

            // A failed read must not act on the bootstrap flag in the previously held observation.
            worldMenuOverlay.Hide();
            var previewRequests = Requests(OwnerPairingEndpoints.OwnerWorldPreview);
            var refreshes = successfulRefreshCount;
            host.Reconnect = null;
            resumeWorldOnContinue = true;
            await EnterWorldAsync();
            if (isInWorld || worldMenuOverlay.Visible || !mainMenuOverlay.Visible || !resumeWorldOnContinue ||
                mainMenuContinueButton.Disabled || successfulRefreshCount != refreshes ||
                Requests(OwnerPairingEndpoints.OwnerWorldPreview) != previewRequests ||
                Requests(OwnerPairingEndpoints.OwnerResume) != 0 ||
                !mainMenuStatus.Text.StartsWith("Could not reach your world.", StringComparison.Ordinal))
                throw new InvalidOperationException("A failed Continue refresh must retain the menu and retry state without opening New World or resuming.");

            // Older hosts omit the new property; existing/progressed worlds also keep Continue's resume behavior.
            var olderSetup = JsonSerializer.Deserialize<OwnerFounderSetup>(
                "{\"required\":4,\"placed\":0,\"started\":false}", CompatibilitySmokeJsonOptions)!;
            foreach (var setup in new[] { olderSetup, new OwnerFounderSetup(4, 4, true) })
            {
                ShowMainMenu();
                observationSession.ResetAfterLoad();
                host.Reconnect = Reply(generated with { FounderSetup = setup });
                resumeWorldOnContinue = setup.Started;
                var resumes = Requests(OwnerPairingEndpoints.OwnerResume);
                await EnterWorldAsync();
                if (!isInWorld || mainMenuOverlay.Visible || worldMenuOverlay.Visible || resumeWorldOnContinue ||
                    Requests(OwnerPairingEndpoints.OwnerWorldPreview) != previewRequests ||
                    Requests(OwnerPairingEndpoints.OwnerResume) != resumes + (setup.Started ? 1 : 0))
                    throw new InvalidOperationException("A host without the new flag and an existing world must continue normally.");
            }
        }
        finally
        {
            worldListRequest.Cancel();
            worldMenuOverlay.Hide();
            InvalidateWorldPreview(refresh: false);
            listedWorlds = [];
            listedActiveWorldId = null;
            worldSelectionList.Clear();
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            observationSession.ResetAfterLoad();
            if (previousObservation is not null)
                observationSession.TryAccept(previousObservation, previousObservation.Baseline.Events.AfterEventId, out _);
            isInWorld = previousInWorld;
            resumeWorldOnContinue = previousResume;
            mainMenuOverlay.Visible = previousMenuVisible;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            RefreshControlAvailability();
            RefreshMainMenuAvailability();
            statusToast.Hide();
        }
    }
}
