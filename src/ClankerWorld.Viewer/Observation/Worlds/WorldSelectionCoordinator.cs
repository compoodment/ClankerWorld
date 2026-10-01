using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using System.Diagnostics;
using System.Security.Cryptography;

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
    private readonly Dictionary<string, CachedCheckpoint> checkedCheckpoints = new(StringComparer.Ordinal);

    private sealed record CachedCheckpoint(string WorldId, string Seed, string Digest,
        string? HistoryArchiveHead, bool Restorable, WorldThumbnail? Thumbnail);

    public WorldCatalogSnapshot List(CancellationToken cancellationToken = default)
    {
        var elapsed = Stopwatch.StartNew();
        var (worldCount, cacheHits, scans) = (0, 0, 0);
        try
        {
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snapshot = catalog.Capture();
                var currentIds = snapshot.Worlds.Select(world => world.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var id in checkedCheckpoints.Keys.Where(id => !currentIds.Contains(id)).ToArray())
                    checkedCheckpoints.Remove(id);
                var worlds = new CatalogWorld[snapshot.Worlds.Count];
                worldCount = worlds.Length;
                for (var index = 0; index < worlds.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var world = snapshot.Worlds[index];
                    if (world.Id == snapshot.ActiveId)
                        worlds[index] = WithThumbnail(world, () => runtime.ExportState().Map) with
                        {
                            Compatibility = "compatible",
                            CompatibilityReason = null
                        };
                    else
                    {
                        worlds[index] = Assess(world, out var cacheHit);
                        if (cacheHit) cacheHits++;
                        else scans++;
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                WorldSelectionTelemetry.Listed(logger, worldCount, cacheHits, scans, elapsed.ElapsedMilliseconds);
                return snapshot with { Worlds = worlds };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WorldSelectionTelemetry.ListCanceled(logger, worldCount, cacheHits, scans, elapsed.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Worlds catalogued before thumbnails existed get one the first time they
    /// are listed, from the map already at hand, and the catalog keeps it.
    /// </summary>
    private CatalogWorld WithThumbnail(CatalogWorld world, Func<SeededMap> map) =>
        world.Thumbnail is not null ? world : WithThumbnail(world, WorldThumbnail.From(map()));

    private CatalogWorld WithThumbnail(CatalogWorld world, WorldThumbnail? thumbnail)
    {
        if (world.Thumbnail is not null || thumbnail is null) return world;
        try
        {
            catalog.RememberThumbnail(world.Id, thumbnail);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The list still shows it; the catalog can keep it next time.
        }
        return world with { Thumbnail = thumbnail };
    }

    private CatalogWorld Assess(CatalogWorld world, out bool cacheHit)
    {
        cacheHit = false;
        try
        {
            var bytes = catalog.ReadSnapshotBytes(world.Id);
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!checkedCheckpoints.TryGetValue(world.Id, out var checkedCheckpoint) ||
                checkedCheckpoint.WorldId != world.WorldId || checkedCheckpoint.Seed != world.Seed ||
                checkedCheckpoint.Digest != digest)
            {
                checkedCheckpoint = CheckCheckpoint(world, bytes, digest);
                checkedCheckpoints[world.Id] = checkedCheckpoint;
            }
            else cacheHit = true;
            world = WithThumbnail(world, checkedCheckpoint.Thumbnail);
            if (!checkedCheckpoint.Restorable)
                return Incompatible(world);
            // History files and provider credentials can change without a
            // checkpoint rewrite; never reuse their previous assessment.
            stateFile.VerifyRequiredHistory(checkedCheckpoint.HistoryArchiveHead);
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
            return Incompatible(world);
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

    private static CachedCheckpoint CheckCheckpoint(CatalogWorld world, byte[] bytes, string digest)
    {
        WorldThumbnail? thumbnail = null;
        try
        {
            var checkpoint = PrivateWorldRuntimeCodec.Decode(bytes);
            if (checkpoint.Society.Society.WorldId != world.WorldId || checkpoint.WorldSeed != world.Seed)
                throw new InvalidDataException("The selected world checkpoint does not match its catalog entry.");
            // Keep the picture even if the restore below fails, as a full read did.
            if (world.Thumbnail is null) thumbnail = WorldThumbnail.From(checkpoint.Map);
            // Structural validation is independent of the installation's mutable
            // provider routing. Credentials and assignments are checked in Assess.
            using var verified = PrivateWorldRuntime.Restore(checkpoint);
            return new CachedCheckpoint(world.WorldId, world.Seed, digest,
                checkpoint.HistoryArchiveHead, true, thumbnail);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or
            System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            return new CachedCheckpoint(world.WorldId, world.Seed, digest, null, false, thumbnail);
        }
    }

    private static CatalogWorld Incompatible(CatalogWorld world) => world with
    {
        Compatibility = "incompatible",
        CompatibilityReason = "The saved checkpoint or required content cannot be restored."
    };

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
            var selection = GeographyCandidateSelector.Select(geography);
            var map = selection.Map;
            // The preview contract retains a suggested passable area for older
            // clients, but fresh maps have no placed camp or Town at this site.
            var camp = map.Resources.First(item => item.Id == "berry-patch").Position;
            WorldSelectionTelemetry.Previewed(logger, map.Width, map.Height);
            return new ViewerWorldPreview(OwnerWorldObservationStore.PackTerrain(map),
                new ViewerPosition(camp.X, camp.Y), map.ManifestDigest, map.Resources.Count)
            {
                PackedMapLayers = OwnerWorldObservationStore.PackMapLayers(map, geography.Seed),
                MapLayersDigest = MapLayerManifestCodec.Digest(map),
                Coverage = selection.Selected,
                Candidates = selection.Candidates,
            };
        }
    }

    public CatalogWorld Create(string name, GeographyOptions geography, int candidateAttempt,
        string expectedManifestDigest, string expectedMapLayersDigest, bool acceptUnmetTargets)
    {
        ArgumentNullException.ThrowIfNull(geography);
        if (geography.Size is not (WorldSizePreset.Small or WorldSizePreset.Medium))
            throw new ArgumentException("Large, Huge and Mega need compact persistent terrain before they can be played.", nameof(geography));
        lock (gate)
        {
            RequirePaused();
            // Regenerate the bounded selection once so the signed create action
            // can be checked against the exact preview identity.
            var selection = GeographyCandidateSelector.Select(geography with { CandidateAttempt = 0 });
            if (candidateAttempt != selection.Map.GenerationAttempt ||
                !string.Equals(expectedManifestDigest, selection.Map.ManifestDigest, StringComparison.Ordinal) ||
                !string.Equals(expectedMapLayersDigest, MapLayerManifestCodec.Digest(selection.Map), StringComparison.Ordinal))
                throw new InvalidOperationException("The preview is out of date. Preview the map again before creating it.");
            if (!selection.Selected.MeetsTargets && !acceptUnmetTargets)
                throw new InvalidOperationException("The selected map misses the displayed trial targets. Accept its coverage explicitly or choose a new seed.");

            var chosenOptions = geography with { CandidateAttempt = candidateAttempt };
            using var created = PrivateWorldRuntime.CreateFromGeneratedGeography(geography.Seed,
                chosenOptions, selection.Map, providerFactory);
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
            var assessed = Assess(entry, out _);
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
        stateFile.VerifyRequiredHistory(target);
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
