using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

var output = Path.GetFullPath(args[0]);
var variant = args[1];
Directory.CreateDirectory(output);
var report = new List<object>();
foreach (var size in new[] { WorldSizePreset.Small, WorldSizePreset.Medium })
{
    var options = new GeographyOptions("cached-observation-cost-audit", size);
    using var world = new PrivateWorldRuntime(options.Seed, startPace: WorldStartPace.FounderSetup, geographyOptions: options);
    var state = world.ExportState();
    var saved = PrivateWorldRuntimeCodec.Encode(state);
    var store = new OwnerWorldObservationStore(world);
    var initial = store.GetSnapshot();
    var cached = store.GetReconnectBaseline(0, initial.WorldId, initial.MapManifestDigest, initial.MapLayersDigest).Snapshot;
    var miss = store.GetReconnectBaseline(0, initial.WorldId, initial.MapManifestDigest, new string('0', 64)).Snapshot;
    if (cached.PackedTerrain is not null || cached.PackedMapLayers is not null || miss.PackedMapLayers is null)
        throw new InvalidOperationException("Unexpected terrain omission");
    File.WriteAllText(Path.Combine(output, $"{variant}-{size}-initial.json"), JsonSerializer.Serialize(initial));
    File.WriteAllText(Path.Combine(output, $"{variant}-{size}-cached.json"), JsonSerializer.Serialize(cached));
    File.WriteAllText(Path.Combine(output, $"{variant}-{size}-miss.json"), JsonSerializer.Serialize(miss));
    File.WriteAllBytes(Path.Combine(output, $"{variant}-{size}-state.json"), saved);
    var expected = JsonSerializer.Serialize(cached);
    for (var repeat = 0; repeat < 5; repeat++)
        if (expected != JsonSerializer.Serialize(store.GetReconnectBaseline(0, initial.WorldId,
                initial.MapManifestDigest, initial.MapLayersDigest).Snapshot)) throw new InvalidOperationException("Projection changed");
    var projection = Measure(() => _ = store.GetReconnectBaseline(0, initial.WorldId,
        initial.MapManifestDigest, initial.MapLayersDigest));
    var digest = Measure(() => _ = MapLayerManifestCodec.Digest(state.Map));
    var fertility = Measure(() => _ = new LandFertility(state.Map, state.WorldSeed));
    if (!saved.AsSpan().SequenceEqual(PrivateWorldRuntimeCodec.Encode(world.ExportState())))
        throw new InvalidOperationException("Observation changed state");
    using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
    restored.Validate();
    if (!saved.AsSpan().SequenceEqual(PrivateWorldRuntimeCodec.Encode(restored.ExportState())))
        throw new InvalidOperationException("Restore changed state");
    var restoredSnapshot = new OwnerWorldObservationStore(restored).GetSnapshot();
    if (JsonSerializer.Serialize(initial) != JsonSerializer.Serialize(restoredSnapshot))
        throw new InvalidOperationException("Reload changed projection");
    report.Add(new { Size = size.ToString(), state.Map.Width, state.Map.Height, Projection = projection,
        LayerDigest = digest, FertilityConstructor = fertility, SavedSha256 = Convert.ToHexStringLower(SHA256.HashData(saved)),
        SameSnapshot = true, SameState = true, SameReload = true });
    Console.WriteLine(JsonSerializer.Serialize(report[^1]));
}
File.WriteAllText(Path.Combine(output, variant + "-measurements.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

static object Measure(Action action)
{
    for (var warm = 0; warm < 5; warm++) action();
    var milliseconds = new double[100];
    var allocations = new long[100];
    for (var sample = 0; sample < 100; sample++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        action();
        milliseconds[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
    }
    return new { MedianMs = milliseconds.Order().ElementAt(49), P95Ms = milliseconds.Order().ElementAt(94),
        BytesPerCall = allocations.Average(), SamplesMs = milliseconds, AllocatedBytes = allocations };
}
