using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// A damaged checkpoint is refused as invalid data, never as an unexpected
/// fault that would escape Load World's per-world isolation (#421).
/// </summary>
public sealed class DamagedCheckpointEntryTests
{
    // Every list below threw a null-reference fault on an empty entry before
    // the decoder refused empty entries generically.
    [Theory]
    [InlineData("map.resources")]
    [InlineData("society.society.inventory.lots")]
    [InlineData("society.cognition.runtimes[0].events")]
    [InlineData("worldSystems.factions.standings")]
    [InlineData("worldContent.recipes[0].inputs")]
    [InlineData("worldSimulation.buildingExpansions")]
    [InlineData("towns[0].residentIds")]
    public async Task EmptyEntryInAnySavedListIsRefusedAsInvalidData(string list)
    {
        var healthy = await HealthyCheckpoint();
        var document = JsonNode.Parse(healthy)!;
        // An optional list this short run never filled is absent; add it.
        var split = list.LastIndexOf('.');
        var owner = split < 0 ? document["state"]! : Navigate(document["state"]!, list[..split]);
        var name = list[(split + 1)..];
        if (!name.Contains('[', StringComparison.Ordinal) && owner[name] is null)
            owner[name] = new JsonArray();
        var array = (JsonArray)Navigate(document["state"]!, list);
        if (array.Count == 0) array.Add(null);
        else array[0] = null;

        var damaged = Encoding.UTF8.GetBytes(document.ToJsonString());
        var refused = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(damaged));
        Assert.Contains("empty entry", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public async Task UnplayableSavedWorldSizeIsRefusedAsInvalidData(int size)
    {
        var document = JsonNode.Parse(await HealthyCheckpoint())!;
        document["state"]!["geography"]!["size"] = size;

        var damaged = Encoding.UTF8.GetBytes(document.ToJsonString());
        var refused = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(damaged));
        Assert.Contains("Small and Medium", refused.Message, StringComparison.Ordinal);
    }

    private static async Task<byte[]> HealthyCheckpoint()
    {
        using var world = NormalPathWorld.CreateGenerated("damaged-entry", _ => new ActionCoverageRecorder());
        for (var tick = 0; tick < 30; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        world.Pause();
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    /// <summary>Follows a path such as <c>towns[0].residentIds</c> from the saved state.</summary>
    private static JsonNode Navigate(JsonNode node, string path)
    {
        foreach (var part in path.Split('.'))
        {
            var bracket = part.IndexOf('[', StringComparison.Ordinal);
            node = node[bracket < 0 ? part : part[..bracket]]!;
            if (bracket >= 0)
                node = node[int.Parse(part[(bracket + 1)..^1], System.Globalization.CultureInfo.InvariantCulture)]!;
        }
        return node;
    }
}
