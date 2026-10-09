using System.Collections;
using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ChunkValidationReuseTests
{
    [Fact]
    public void RepeatedWorldTicksReuseValidatedChunkMetadata()
    {
        var resources = new CountingChunkResources([
            new("tree-a", "tree", new GridPoint(1, 1), true),
            new("stone-a", "stone", new GridPoint(2, 1), false),
            new("greens-a", "wild-greens", new GridPoint(3, 1), true),
        ]);
        var manifest = ChunkManifestCodec.WithDigest(new(new(0, 0), 16, 16, 16, "probe", "1", resources));
        var initial = WorldSystemsRules.CreateGenesis("chunk-validation", chunks: [manifest]);
        resources.Reads = 0;
        var state = initial;
        for (var tick = 0; tick < 16; tick++) state = WorldSystemsRules.AdvanceOneTick(state);
        Assert.Same(initial.Chunks, state.Chunks);
        Assert.InRange(resources.Reads, 0, 2 * resources.Items.Length);
    }

    [Theory]
    [InlineData("constructor")]
    [InlineData("with")]
    [InlineData("readonly-view")]
    public void WarmedValidationEitherSnapshotsOrRejectsChangedResourceAliases(string input)
    {
        var original = new ChunkResourceMetadata("tree-a", "tree", new GridPoint(1, 1), true);
        var borrowed = new[] { original };
        IReadOnlyList<ChunkResourceMetadata> resources = input == "readonly-view" ? Array.AsReadOnly(borrowed) : borrowed;
        var manifest = new ChunkManifest(new(0, 0), 16, 16, 16, "probe", "1", resources);
        if (input == "with") manifest = manifest with { Resources = resources };
        manifest = ChunkManifestCodec.WithDigest(manifest);
        var state = WorldSystemsRules.CreateGenesis("chunk-validation-alias", chunks: [manifest]);
        var before = WorldSystemsCodec.Encode(state);
        borrowed[0] = original with { Kind = "stone" };
        // Borrowed collections may be snapshotted, or changes may invalidate
        // validation. Silently accepting changed metadata under its old digest
        // is forbidden, including through a caller-owned read-only wrapper.
        if (manifest.Resources[0] == borrowed[0])
            Assert.Throws<InvalidDataException>(() => WorldSystemsCodec.Encode(state));
        else Assert.Equal(before, WorldSystemsCodec.Encode(state));
    }

    [Theory]
    [InlineData("resource-limit")]
    [InlineData("chunk-limit")]
    [InlineData("duplicate-chunk")]
    [InlineData("digest")]
    [InlineData("position")]
    [InlineData("duplicate-resource")]
    [InlineData("kind")]
    [InlineData("generator")]
    public void WarmedValidationChecksChangedManifestsAndCurrentWorldLimits(string damage)
    {
        var resources = new ChunkResourceMetadata[]
        {
            new("tree-a", "tree", new GridPoint(1, 1), true),
            new("stone-a", "stone", new GridPoint(2, 1), false),
            new("greens-a", "wild-greens", new GridPoint(3, 1), true),
        };
        var manifest = ChunkManifestCodec.WithDigest(new(new(0, 0), 16, 16, 16, "probe", "1", resources));
        var chunks = new List<ChunkManifest> { manifest };
        var state = WorldSystemsRules.CreateGenesis("chunk-validation-change", chunks: chunks);
        WorldSystemsRules.Validate(state);
        if (damage == "resource-limit") state = state with { Config = state.Config with { MaxResourcesPerChunk = 2 } };
        else if (damage == "chunk-limit")
        {
            chunks.Add(ChunkManifestCodec.WithDigest(manifest with { Coordinate = new(1, 0) }));
            state = state with { Config = state.Config with { MaxChunkCount = 1 } };
        }
        else if (damage == "duplicate-chunk") chunks.Add(manifest);
        else if (damage == "digest") chunks[0] = manifest with { ManifestDigest = new string('0', 64) };
        else if (damage == "generator") chunks[0] = manifest with { GeneratorVersion = "2" };
        else
        {
            resources[0] = damage switch
            {
                "position" => resources[0] with { LocalPosition = new(16, 1) },
                "duplicate-resource" => resources[1],
                "kind" => resources[0] with { Kind = "stone" },
                _ => throw new InvalidOperationException(),
            };
            chunks[0] = manifest with { Resources = resources };
        }
        if (damage is "resource-limit" or "position" or "duplicate-resource")
            Assert.ThrowsAny<ArgumentException>(() => WorldSystemsRules.Validate(state));
        else Assert.Throws<InvalidDataException>(() => WorldSystemsRules.Validate(state));
    }

    [Fact]
    public void LoadedChunkMetadataStillRequiresItsExactDigestAfterValidationWasWarmed()
    {
        var manifest = ChunkManifestCodec.WithDigest(new(new(0, 0), 16, 16, 16, "probe", "1",
            [new("tree-a", "tree", new GridPoint(1, 1), true)]));
        var state = WorldSystemsRules.CreateGenesis("chunk-validation-load", chunks: [manifest]);
        var bytes = WorldSystemsCodec.Encode(state);
        Assert.Equal(bytes, WorldSystemsCodec.Encode(WorldSystemsCodec.Decode(bytes)));
        var document = JsonNode.Parse(bytes)!;
        document["state"]!["chunks"]![0]!["resources"]![0]!["kind"] = "stone";
        Assert.Throws<InvalidDataException>(() => WorldSystemsCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
        Assert.Equal(bytes, WorldSystemsCodec.Encode(state));
    }

    private sealed class CountingChunkResources(ChunkResourceMetadata[] items) : IReadOnlyList<ChunkResourceMetadata>
    {
        internal ChunkResourceMetadata[] Items { get; } = items;
        internal int Reads { get; set; }
        public int Count => Items.Length;
        public ChunkResourceMetadata this[int index]
        {
            get { Reads++; return Items[index]; }
        }
        public IEnumerator<ChunkResourceMetadata> GetEnumerator()
        {
            for (var index = 0; index < Items.Length; index++) yield return this[index];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
