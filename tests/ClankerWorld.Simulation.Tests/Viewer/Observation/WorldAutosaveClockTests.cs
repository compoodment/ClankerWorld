using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class WorldAutosaveClockTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RotatingAutosavesRecoverTheirCadenceAfterABackwardsClockCorrectionAndRestart(bool clockBack)
    {
        var directory = Directory.CreateTempSubdirectory("clanker-autosave-clock-");
        try
        {
            using var runtime = new PrivateWorldRuntime("autosave-clock");
            var path = Path.Combine(directory.FullName, "world.json");
            var saves = new ManualWorldSaveStore(path);
            var autosave = new WorldAutosaveStore(path, runtime.Society.WorldId);
            var providers = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            autosave.Configure(true, 5, 3);
            runtime.Pause();
            var manual = saves.Create("Keep this manual save", runtime, []);
            runtime.Resume();
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            var firstTime = autosave.Capture().LastSavedUtc.AddMinutes(6);
            var first = Assert.IsType<ManualWorldSave>(autosave.MaybeSave(firstTime, runtime, providers, saves));
            var firstSettings = autosave.Capture();
            var corrected = clockBack ? firstTime.AddHours(-1) : firstTime;
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            Assert.Null(autosave.MaybeSave(corrected, runtime, providers, saves));
            Assert.Equal(firstSettings with { LastSavedUtc = corrected }, autosave.Capture());
            Assert.Equal(autosave.Capture(), new WorldAutosaveStore(path, runtime.Society.WorldId).Capture());
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            Assert.Null(autosave.MaybeSave(corrected.AddMinutes(4), runtime, providers, saves));
            autosave = new WorldAutosaveStore(path, runtime.Society.WorldId);
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            var second = Assert.IsType<ManualWorldSave>(autosave.MaybeSave(corrected.AddMinutes(5), runtime, providers, saves));
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(2, saves.List().Count(save => save.IsAutosave));
            Assert.Null(autosave.MaybeSave(corrected.AddMinutes(10), runtime, providers, saves));
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            Assert.NotNull(autosave.MaybeSave(corrected.AddMinutes(10), runtime, providers, saves));
            Assert.Equal(3, saves.List().Count(save => save.IsAutosave));
            Assert.Contains(saves.List(), save => save.Id == manual.Id && !save.IsAutosave);
            autosave.Configure(false, 5, 3);
            var disabled = autosave.Capture();
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            Assert.Null(autosave.MaybeSave(corrected.AddMinutes(15), runtime, providers, saves));
            Assert.Equal(disabled, autosave.Capture());
            Assert.Equal(disabled, new WorldAutosaveStore(path, runtime.Society.WorldId).Capture());
            Assert.Equal(3, saves.List().Count(save => save.IsAutosave));
            Assert.Contains(saves.List(), save => save.Id == manual.Id && !save.IsAutosave);
            runtime.Validate();
            var bytes = PrivateWorldRuntimeCodec.Encode(runtime.ExportState());
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            restored.Validate();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RecoveredAutosavesKeepTheLatestBranchCopiesWhenEarlierCopiesHaveFutureDates()
    {
        var directory = Directory.CreateTempSubdirectory("clanker-autosave-clock-rotation-");
        try
        {
            using var runtime = new PrivateWorldRuntime("autosave-clock-rotation");
            var path = Path.Combine(directory.FullName, "world.json");
            var saves = new ManualWorldSaveStore(path);
            var autosave = new WorldAutosaveStore(path, runtime.Society.WorldId);
            var providers = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            autosave.Configure(true, 1, 3);
            runtime.Pause();
            var manual = saves.Create("Keep this manual save", runtime, []);
            runtime.Resume();
            var fastTime = autosave.Capture().LastSavedUtc.AddHours(1);
            var earlier = new List<ManualWorldSave>();
            for (var i = 0; i < 3; i++)
            {
                Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
                var saved = Assert.IsType<ManualWorldSave>(autosave.MaybeSave(
                    fastTime.AddMinutes(i), runtime, providers, saves));
                earlier.Add(saved);
                // Model already-written timestamps from the fast host clock,
                // without changing the machine's clock or checkpoint bytes.
                var metadataPath = Path.Combine(path + ".manual", saved.Id + ".meta.json");
                var metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!;
                metadata["Save"]!["CreatedUtc"] = fastTime.AddMinutes(i).ToString("O");
                File.WriteAllText(metadataPath, metadata.ToJsonString());
            }
            Assert.Equal(3, saves.List().Count(save => save.IsAutosave));
            var corrected = fastTime.AddMinutes(2).AddHours(-1);
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
            Assert.Null(autosave.MaybeSave(corrected, runtime, providers, saves));
            autosave = new WorldAutosaveStore(path, runtime.Society.WorldId);
            var recovered = new List<ManualWorldSave>();
            for (var i = 1; i <= 3; i++)
            {
                Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
                recovered.Add(Assert.IsType<ManualWorldSave>(autosave.MaybeSave(
                    corrected.AddMinutes(i), runtime, providers, saves)));
            }
            Assert.Single(recovered.Select(save => save.Branch!.Id).Distinct());
            Assert.Equal(earlier[0].Branch!.Id, recovered[0].Branch!.Id);
            Assert.Equal(recovered.Select(save => save.Id).Order(),
                saves.List().Where(save => save.IsAutosave).Select(save => save.Id).Order());
            Assert.Contains(saves.List(), save => save.Id == manual.Id && !save.IsAutosave);
            foreach (var saved in recovered)
            {
                using var restored = PrivateWorldRuntime.Restore(saves.Read(saved.Id));
                restored.Validate();
                Assert.Equal(saved.WorldTick, restored.WorldTick);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void BackwardsClockCorrectionCannotOverwriteAnotherWorldsSchedule()
    {
        var directory = Directory.CreateTempSubdirectory("clanker-autosave-other-world-");
        try
        {
            using var runtime = new PrivateWorldRuntime("autosave-other-world");
            var path = Path.Combine(directory.FullName, "world.json");
            var autosave = new WorldAutosaveStore(path, "another-world");
            var before = autosave.Capture();
            var savedBytes = File.ReadAllBytes(path + ".autosave.json");
            var saves = new ManualWorldSaveStore(path);
            var providers = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            Assert.Throws<InvalidDataException>(() =>
                autosave.MaybeSave(before.LastSavedUtc.AddHours(-1), runtime, providers, saves));
            Assert.Equal(before, autosave.Capture());
            Assert.Equal(savedBytes, File.ReadAllBytes(path + ".autosave.json"));
            Assert.Empty(saves.List());
        }
        finally { directory.Delete(recursive: true); }
    }
}
