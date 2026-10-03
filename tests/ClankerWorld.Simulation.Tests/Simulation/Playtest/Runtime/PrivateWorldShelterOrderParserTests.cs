using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldShelterOrderParserTests
{
    private const string Actor = "founder-ilya";

    [Theory]
    [InlineData("seek shelter", "seek_shelter", null)]
    [InlineData("take cover", "seek_shelter", null)]
    [InlineData("shelter in my House", "seek_shelter", "house")]
    [InlineData("seek shelter in my House", "seek_shelter", "house")]
    [InlineData("take cover in my House", "seek_shelter", "house")]
    [InlineData("light a fire", "tend_fire", null)]
    [InlineData("tend the fire", "tend_fire", null)]
    [InlineData("light the fire in my House", "tend_fire", "house")]
    [InlineData("tend a fire in my House", "tend_fire", "house")]
    public void NativeSurvivalCommandsAreFiniteRequestsEvenBeforeTheirTargetsExist(string text, string action, string? buildingKind)
    {
        using var world = new PrivateWorldRuntime("shelter-order-parser");
        Assert.Empty(world.WorldContent.Buildings);
        var current = Submit(world, "current", "eat berries");

        var order = Submit(world, "shelter", text);

        Assert.Equal((action, "waiting", buildingKind, 1, 0, false, false),
            (order.Action, order.Status, order.TargetBuildingKind, order.RequestedUnits, order.CompletedUnits,
                order.RepeatUntilCancelled, order.QuantityIsExplicit));
        Assert.Equal(action == "seek_shelter" ? "shelters" : "fires", order.ProgressUnit);
        Assert.Null(order.ShelterBinding);
        Assert.Null(order.ShelterCompletion);
        Assert.Null(order.TargetPosition);
        Assert.Null(order.TargetDefinitionId);
        Assert.Null(order.LastEffectId);
        Assert.Equal("cancelled", world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order!.Status);
        Assert.Equal(0, current.CompletedUnits);
        Assert.Equal(0, world.WorldTick);
        world.Validate();
    }

    [Theory]
    [InlineData("please seek shelter at (2, 1) now.", "seek_shelter", null)]
    [InlineData("take cover at tile 2/1", "seek_shelter", null)]
    [InlineData("shelter in my House at 2 1 please", "seek_shelter", "house")]
    [InlineData("light a fire at (2, 1)", "tend_fire", null)]
    [InlineData("tend the fire in my House at (2, 1)", "tend_fire", "house")]
    public void ExactCoordinatesRoundTripWithoutInventingAShelterOrACompletion(string text, string action, string? buildingKind)
    {
        using var world = new PrivateWorldRuntime("shelter-coordinate-parser");
        var order = Submit(world, "location", text);
        Assert.Equal((action, buildingKind, new GridPoint(2, 1)), (order.Action, order.TargetBuildingKind, order.TargetPosition!.Value));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var json = Assert.Single(JsonNode.Parse(bytes)!["state"]!["instructions"]!.AsArray())!["order"]!.AsObject();
        Assert.False(json.ContainsKey("shelterBinding"));
        Assert.False(json.ContainsKey("shelterCompletion"));
        Assert.False(json.ContainsKey("quantityIsExplicit"));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(order, Assert.Single(restored.ExportState().Instructions!).Order);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void BoundedUnknownCoordinatesRemainExplicitIntentWithoutRevealingATarget()
    {
        using var world = new PrivateWorldRuntime("shelter-coordinate-bounds");
        var order = Submit(world, "edge", "seek shelter at (-10000000, +10000000)");
        Assert.Equal("seek_shelter", order.Action);
        Assert.Equal(new GridPoint(-10_000_000, 10_000_000), order.TargetPosition);
        Assert.Null(order.ShelterBinding);
        Assert.Null(order.ShelterCompletion);
        world.Validate();
    }

    [Theory]
    [InlineData("seek one shelter")]
    [InlineData("take two cover")]
    [InlineData("light one fire")]
    [InlineData("light 1 fire")]
    [InlineData("tend 0 fire")]
    [InlineData("light two fires")]
    [InlineData("keep seeking shelter")]
    [InlineData("repeat seek shelter")]
    [InlineData("repeatedly tend the fire")]
    [InlineData("light a fire until cancelled")]
    [InlineData("seek shelter until warm")]
    [InlineData("take cover for 10 ticks")]
    [InlineData("warm up")]
    [InlineData("heat the House")]
    [InlineData("shelter in the House")]
    [InlineData("shelter in my Warehouse")]
    [InlineData("light the fire in their House")]
    [InlineData("light a fire with two wood")]
    [InlineData("seek shelter near (2, 1)")]
    [InlineData("seek shelter at (10000001, 1)")]
    [InlineData("light a fire at (2, -10000001)")]
    [InlineData("take cover at (2.5, 1)")]
    [InlineData("seek shelter and light a fire")]
    [InlineData("tend the fire at (2, 1) and (3, 1)")]
    public void UnsupportedSurvivalCommandsCannotReplaceTheCurrentTaskOrQueryAModel(string text)
    {
        var provider = new UncalledModel();
        using var world = new PrivateWorldRuntime("unsupported-shelter-parser", _ => provider);
        var current = Submit(world, "current", "eat berries");

        var rejected = Submit(world, "unsupported", text);

        Assert.Equal(("unknown", "not_understood"), (rejected.Action, rejected.Status));
        Assert.Null(rejected.ShelterBinding);
        Assert.Null(rejected.ShelterCompletion);
        Assert.Null(rejected.TargetBuildingKind);
        Assert.Equal(current, world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, world.WorldTick);
        Assert.Single(world.ExportState().Events, item => item.Kind == "instruction_not_understood");
    }

    [Theory]
    [InlineData("seek shelter at (2, 1)")]
    [InlineData("light the fire in my House at (2, 1)")]
    public void SavedSurvivalOrdersRejectUnsupportedLifecycleAndUnrelatedWorkFields(string text)
    {
        using var world = new PrivateWorldRuntime("saved-shelter-parser");
        var order = Submit(world, "saved", text);
        Assert.True(order.Action is "seek_shelter" or "tend_fire");
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        (string Member, JsonNode? Value)[] corruptions =
        [
            ("requestedUnits", JsonValue.Create(2)),
            ("completedUnits", JsonValue.Create(1)),
            ("quantityIsExplicit", JsonValue.Create(true)),
            ("repeatUntilCancelled", JsonValue.Create(true)),
            ("progressUnit", JsonValue.Create("arrivals")),
            ("targetBuildingKind", JsonValue.Create("warehouse")),
            ("targetFoodKind", JsonValue.Create("berries")),
            ("targetItemKind", JsonValue.Create("wood")),
            ("targetRecipeId", JsonValue.Create("recipe-only")),
            ("targetDefinitionId", JsonValue.Create("construction-only")),
            ("targetStorageOwnerId", JsonValue.Create("household-only")),
            ("deliveryPurpose", JsonValue.Create("household_stock")),
            ("lastEffectId", JsonValue.Create("shelter:unearned")),
            ("shelterBinding", JsonNode.Parse("""{"kind":"unknown","position":{"x":2,"y":1}}""")),
            ("shelterBinding", JsonNode.Parse("""{"kind":"building","position":{"x":2,"y":1},"buildingInstanceId":"partial-house"}""")),
            ("shelterCompletion", JsonNode.Parse("""{"worldTick":0,"position":{"x":2,"y":1}}""")),
        ];
        foreach (var (member, value) in corruptions)
        {
            var document = JsonNode.Parse(bytes)!;
            Assert.Single(document["state"]!["instructions"]!.AsArray())!["order"]![member] = value;
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(JsonSerializer.SerializeToUtf8Bytes(document)));
        }
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(order, Assert.Single(restored.ExportState().Instructions!).Order);
    }

    [Fact]
    public void ShelterStateCannotBeAttachedToAnotherActionAndOlderSchemasAreRefused()
    {
        using var world = new PrivateWorldRuntime("shelter-schema-parser");
        _ = Submit(world, "other", "eat berries");
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        foreach (var member in new[] { "shelterBinding", "shelterCompletion" })
        {
            var document = JsonNode.Parse(bytes)!;
            Assert.Single(document["state"]!["instructions"]!.AsArray())!["order"]![member] = member == "shelterBinding"
                ? JsonNode.Parse("""{"kind":"natural","position":{"x":2,"y":1}}""")
                : JsonNode.Parse("""{"worldTick":0,"position":{"x":2,"y":1}}""");
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(JsonSerializer.SerializeToUtf8Bytes(document)));
        }
        foreach (var version in new[] { PrivateWorldRuntime.StateSchemaVersion - 1, PrivateWorldRuntime.StateSchemaVersion + 1 })
        {
            var document = JsonNode.Parse(bytes)!;
            document["state"]!["schemaVersion"] = version;
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(JsonSerializer.SerializeToUtf8Bytes(document)));
        }
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static OwnerInstructionOrder Submit(PrivateWorldRuntime world, string key, string text)
    {
        var receipt = world.SubmitInstruction(new(key, "owner:test", Actor, OwnerInstructionKind.MustDo, text));
        return world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    }

    private sealed class UncalledModel : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public int RequestCount { get; private set; }

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            RequestCount++;
            throw new InvalidOperationException("Parsing an unsupported order must not query the model.");
        }
    }
}
