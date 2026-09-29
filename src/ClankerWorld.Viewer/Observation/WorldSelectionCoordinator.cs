using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Viewer.Observation;

/// <summary>Serializes paused-world selection against saves and provider routing.</summary>
public sealed class WorldSelectionCoordinator(
    WorldCatalogStore catalog,
    PrivateWorldRuntime runtime,
    PrivateWorldStateFile stateFile,
    ProviderConfigurationStore providers,
    WorldAutosaveStore autosave,
    WorldJevPolicy jevPolicy,
    ILogger<WorldSelectionCoordinator> logger,
    Func<string, IDecisionProvider> providerFactory)
{
    private readonly object gate = providers.WorldMutationGate;

    public WorldCatalogSnapshot List()
    {
        lock (gate)
        {
            var snapshot = catalog.Capture();
            return snapshot with
            {
                Worlds = snapshot.Worlds.Select(world =>
                world.Id == snapshot.ActiveId
                    ? world with { Compatibility = "compatible", CompatibilityReason = null }
                    : Assess(world)).ToArray()
            };
        }
    }

    private CatalogWorld Assess(CatalogWorld world)
    {
        try
        {
            var checkpoint = catalog.Read(world.Id);
            using var verified = PrivateWorldRuntime.Restore(checkpoint, providerFactory);
            if (!providers.CanRestoreWorldAssignments(world.Assignments))
                return world with
                {
                    Compatibility = "incompatible",
                    CompatibilityReason = "Required model configuration or a local credential is unavailable."
                };
            return world with { Compatibility = "compatible", CompatibilityReason = null };
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or
            FileNotFoundException or System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            return world with
            {
                Compatibility = "incompatible",
                CompatibilityReason = "The saved checkpoint or required content cannot be restored."
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return world with
            {
                Compatibility = "unknown",
                CompatibilityReason = "The saved checkpoint could not be checked right now."
            };
        }
    }

    public ViewerWorldPreview Preview(GeographyOptions geography)
    {
        ArgumentNullException.ThrowIfNull(geography);
        if (geography.Size is not (WorldSizePreset.Small or WorldSizePreset.Medium))
            throw new ArgumentException("Only Small and Medium are playable yet.", nameof(geography));
        lock (gate)
        {
            // Preview is read-only. The title screen can preview a new map while
            // the currently selected world is running or waiting for a client;
            // Create and Select still require a confirmed pause.
            var map = GeneratedCampMapGenerator.Generate(geography);
            // The preview contract retains a suggested passable area for older
            // clients, but fresh maps have no placed camp or Town at this site.
            var camp = map.Resources.First(item => item.Id == "berry-patch").Position;
            WorldSelectionTelemetry.Previewed(logger, map.Width, map.Height);
            return new ViewerWorldPreview(OwnerWorldObservationStore.PackTerrain(map),
                new ViewerPosition(camp.X, camp.Y), map.ManifestDigest, map.Resources.Count)
            {
                PackedMapLayers = OwnerWorldObservationStore.PackMapLayers(map),
                MapLayersDigest = MapLayerManifestCodec.Digest(map),
            };
        }
    }

    public CatalogWorld Create(string name, GeographyOptions geography)
    {
        ArgumentNullException.ThrowIfNull(geography);
        if (geography.Size is not (WorldSizePreset.Small or WorldSizePreset.Medium))
            throw new ArgumentException("Large, Huge and Mega need compact persistent terrain before they can be played.", nameof(geography));
        lock (gate)
        {
            RequirePaused();
            using var created = new PrivateWorldRuntime(geography.Seed, providerFactory,
                startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
            created.InitializeFirstTownContent();
            var entry = catalog.Add(name, created.ExportState());
            SelectCore(entry, created.ExportState());
            if (logger.IsEnabled(LogLevel.Information))
            {
                var sizeName = geography.Size.ToString().ToLowerInvariant();
                WorldSelectionTelemetry.Created(logger, entry.Id, sizeName);
            }
            return entry;
        }
    }

    public CatalogWorld Select(string id)
    {
        lock (gate)
        {
            RequirePaused();
            var entry = catalog.Capture().Worlds.SingleOrDefault(world => world.Id == id)
                ?? throw new FileNotFoundException("The selected world does not exist.");
            if (entry.Id == catalog.Capture().ActiveId) return entry;
            var assessed = Assess(entry);
            if (assessed.Compatibility == "incompatible")
            {
                WorldSelectionTelemetry.Failed(logger, entry.Id, "incompatible_checkpoint");
                throw new InvalidDataException(assessed.CompatibilityReason);
            }
            SelectCore(entry, catalog.Read(id));
            WorldSelectionTelemetry.Selected(logger, entry.Id);
            return entry;
        }
    }

    private void SelectCore(CatalogWorld entry, PrivateWorldRuntimeState target)
    {
        var old = runtime.ExportState();
        var oldEntry = catalog.Active();
        var oldAssignments = providers.CaptureRuntimeConfiguration().Assignments ?? [];
        var oldAutosave = autosave.Capture();
        catalog.ArchiveActive(old, oldAssignments, oldAutosave);
        try
        {
            runtime.SwitchPausedWorld(target);
            stateFile.Save(runtime);
            providers.RestoreWorldAssignments(entry.Assignments);
            autosave.SelectWorld(entry.WorldId, entry.AutosaveSettings);
            jevPolicy.Initialize(runtime.JevEnabled, runtime.JevPolicyRevision);
            catalog.Select(entry.Id);
        }
        catch
        {
            WorldSelectionTelemetry.Failed(logger, entry.Id, "commit_failed");
            runtime.SwitchPausedWorld(old);
            stateFile.Save(runtime);
            providers.RestoreWorldAssignments(oldAssignments);
            autosave.SelectWorld(oldEntry.WorldId, oldAutosave);
            jevPolicy.Initialize(runtime.JevEnabled, runtime.JevPolicyRevision);
            catalog.Select(oldEntry.Id);
            throw;
        }
    }

    private void RequirePaused()
    {
        if (!runtime.Society.IsPaused)
            throw new InvalidOperationException("Pause the current world before switching worlds.");
    }
}
