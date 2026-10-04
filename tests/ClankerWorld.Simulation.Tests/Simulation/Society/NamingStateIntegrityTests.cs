using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class NamingStateIntegrityTests
{
    private const string FirstFounder = "founder:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SecondFounder = "founder:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChosenFirstNamesRemainUniqueInSocietyIncludingDeceasedPeople(bool deceased)
    {
        var state = SocietyFixture.CreateGenesis("chosen-first-integrity",
            [SocietyFixture.CreateFounder("first", "Élodie Vale"), SocietyFixture.CreateFounder("second", "Maia Lake")]);
        if (deceased) state = SocietyFixture.Kill(state, "first", SocietyDeathCause.Accident).Checkpoint;
        var before = SocietyCheckpointCodec.Encode(state);
        Assert.Throws<InhabitantNameTakenException>(() =>
            SocietyFixture.RenameInhabitant(state, "second", "e\u0301LODIE\u00a0Ridge"));
        Assert.Equal(before, SocietyCheckpointCodec.Encode(state));

        var corrupt = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.Id == "second"
                ? person with { Name = "e\u0301LODIE\u00a0Ridge" } : person).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => SocietyFixture.Validate(corrupt));
        Assert.Throws<InvalidDataException>(() => SocietyCheckpointCodec.Encode(corrupt));
        var document = JsonNode.Parse(before)!;
        document["checkpoint"]!["inhabitants"]!.AsArray()
            .Single(person => person!["id"]!.GetValue<string>() == "second")!["name"] = "e\u0301LODIE\u00a0Ridge";
        Assert.Throws<InvalidDataException>(() => SocietyCheckpointCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));

        var legal = SocietyFixture.RenameInhabitant(state, "second", "Maia Grove").Checkpoint;
        Assert.Equal("Maia Grove", SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(legal)).GetInhabitant("second").Name);
        Assert.Equal(deceased ? SocietyInhabitantStatus.Dead : SocietyInhabitantStatus.Active,
            legal.GetInhabitant("first").Status);
    }

    [Fact]
    public void ClosingNamingKeepsAPlaceholderFreeUntilItsTextIsDeliberatelyChosen()
    {
        var state = SocietyFixture.CreateGenesis("closed-placeholder",
            [Placeholder("first"), Placeholder("second")]);
        state = SocietyFixture.CloseNaming(state, "first").Checkpoint;
        state = SocietyFixture.CloseNaming(state, "second").Checkpoint;
        Assert.All(state.Inhabitants, person =>
        {
            Assert.False(person.NeedsName);
            Assert.False(person.HasChosenName);
        });
        state = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(state));
        state = SocietyFixture.RenameInhabitant(state, "first", "New agent").Checkpoint;
        Assert.True(state.GetInhabitant("first").HasChosenName);
        Assert.False(state.GetInhabitant("second").HasChosenName);
        state = SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(state));
        Assert.Throws<InhabitantNameTakenException>(() => SocietyFixture.RenameInhabitant(state, "second", "NEW Lake"));
        var renamed = SocietyFixture.RenameInhabitant(state, "first", "New Vale").Checkpoint;
        Assert.True(renamed.GetInhabitant("first").HasChosenName);
        Assert.Equal("New agent", renamed.GetInhabitant("second").Name);
    }

    [Fact]
    public void PrivateWorldRejectsDuplicateChosenFirstNamesBeforeRestoreOrSerialization()
    {
        using var world = NamedWorld();
        var state = world.ExportState();
        var corrupt = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == SecondFounder
                        ? person with { Name = "ÉLODIE Grove" } : person).ToArray(),
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(corrupt));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(corrupt));
        var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!;
        document["state"]!["society"]!["society"]!["inhabitants"]!.AsArray()
            .Single(person => person!["id"]!.GetValue<string>() == SecondFounder)!["name"] = "ÉLODIE Grove";
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
        world.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingChosenStateAndPreviousPrivateSchemaRefuseWithoutReplacingTheFile(bool previousSchema)
    {
        using var world = NamedWorld();
        var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(world.ExportState()))!;
        if (previousSchema) document["state"]!["schemaVersion"] = PrivateWorldRuntime.StateSchemaVersion - 1;
        else
        {
            foreach (var person in document["state"]!["society"]!["society"]!["inhabitants"]!.AsArray())
                Assert.True(person!.AsObject().Remove("hasChosenName"));
        }
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        var directory = Directory.CreateTempSubdirectory("chosen-name-schema-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            File.WriteAllBytes(path, bytes);
            Assert.Throws<InvalidDataException>(() => new PrivateWorldStateFile(path).LoadOrCreate(world.ExportState().WorldSeed));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal([path], Directory.GetFiles(directory.FullName));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void StandaloneSocietyRequiresExplicitChosenStateAndRefusesItsPreviousEnvelope()
    {
        var society = SocietyFixture.CreateGenesis("chosen-society-format", [Placeholder("first")]);
        var current = SocietyCheckpointCodec.Encode(society);
        var missing = JsonNode.Parse(current)!;
        Assert.True(missing["checkpoint"]!["inhabitants"]![0]!.AsObject().Remove("hasChosenName"));
        var error = Record.Exception(() => SocietyCheckpointCodec.Decode(Encoding.UTF8.GetBytes(missing.ToJsonString())));
        Assert.True(error is InvalidDataException or JsonException, error?.ToString() ?? "The chosen-name distinction must be required.");

        var old = JsonNode.Parse(current)!;
        old["format"] = "clankerworld.society/v1";
        Assert.Throws<InvalidDataException>(() => SocietyCheckpointCodec.Decode(Encoding.UTF8.GetBytes(old.ToJsonString())));
        Assert.Equal(current, SocietyCheckpointCodec.Encode(SocietyCheckpointCodec.Decode(current)));
    }

    [Fact]
    public void SocietyRuntimePreservesChosenStateAndRefusesItsPreviousEnvelopeAndSchema()
    {
        var society = SocietyFixture.CreateGenesis("chosen-runtime-format",
            [SocietyFixture.CreateFounder("first", "Élodie Vale"), Placeholder("second")]);
        society = SocietyFixture.CloseNaming(society, "second").Checkpoint;
        using var runtime = new SocietyWorldRuntime(society);
        var state = runtime.ExportState();
        var current = SocietyWorldRuntimeCodec.Encode(state);
        var decoded = SocietyWorldRuntimeCodec.Decode(current);
        using var restored = SocietyWorldRuntime.Restore(decoded);
        Assert.Equal(current, SocietyWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.True(restored.Checkpoint.GetInhabitant("first").HasChosenName);
        Assert.False(restored.Checkpoint.GetInhabitant("second").HasChosenName);
        Assert.False(restored.Checkpoint.GetInhabitant("second").NeedsName);

        var oldHeader = JsonNode.Parse(current)!;
        oldHeader["format"] = "clankerworld.society-runtime/v1";
        Assert.Throws<InvalidDataException>(() => SocietyWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(oldHeader.ToJsonString())));

        var oldState = state with { SchemaVersion = SocietyWorldRuntime.StateSchemaVersion - 1 };
        Assert.Throws<InvalidDataException>(() => SocietyWorldRuntime.Restore(oldState));
        Assert.Throws<InvalidDataException>(() => SocietyWorldRuntimeCodec.Encode(oldState));
        var oldSchema = JsonNode.Parse(current)!;
        oldSchema["state"]!["schemaVersion"] = oldState.SchemaVersion;
        Assert.Throws<InvalidDataException>(() => SocietyWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(oldSchema.ToJsonString())));

        var missing = JsonNode.Parse(current)!;
        Assert.True(missing["state"]!["society"]!["inhabitants"]![0]!.AsObject().Remove("hasChosenName"));
        Assert.Throws<JsonException>(() => SocietyWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(missing.ToJsonString())));
    }

    private static SocietyInhabitant Placeholder(string id) => SocietyFixture.CreateFounder(id, "New agent") with
    {
        NeedsName = true,
        HasChosenName = false,
    };

    private static PrivateWorldRuntime NamedWorld()
    {
        var world = new PrivateWorldRuntime("chosen-state-integrity", startPace: WorldStartPace.FounderSetup);
        world.PlaceFounder(FirstFounder, new GridPoint(0, 0));
        world.PlaceFounder(SecondFounder, new GridPoint(1, 2));
        Assert.True(world.RenameAgent(FirstFounder, "Élodie Vale"));
        Assert.True(world.RenameAgent(SecondFounder, "Maia Lake"));
        return world;
    }
}
