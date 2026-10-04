using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldBuildingOrderParserTests
{
    private const string Actor = "founder-ilya";
    private static readonly ContentPackageManifest[] Packages =
    [
        StarterContent.Create(), SettlementContent.Create(), HouseContent.Create(), FarmContent.Create(),
        BlacksmithContent.Create(), TailorContent.Create(), SiloContent.Create(), CareContent.Create(),
        BusinessContent.Create(), WarehouseContent.Create(),
    ];

    [Theory]
    [InlineData("build House", "house", "house-1x1", false)]
    [InlineData("build a Farmhouse", "farmhouse", "farmhouse-1x1", true)]
    [InlineData("build one Blacksmith", "blacksmith", "blacksmith-1x2", true)]
    [InlineData("build 1 Tailor", "tailor", "tailor-shop-1x1", true)]
    [InlineData("build a Tailor Shop", "tailor", "tailor-shop-1x1", true)]
    [InlineData("build Silo", "silo", "silo-1x1", false)]
    [InlineData("build Clinic", "clinic", "clinic-1x2", false)]
    [InlineData("build Store", "store", "store-1x1", false)]
    public void BuildingNamesBindTheActiveStartingFootprintAndReplaceTheCurrentTask(
        string text, string kind, string definitionLocalId, bool explicitQuantity)
    {
        using var world = CreateWorld();
        _ = Submit(world, "current", "eat berries");
        var order = Submit(world, "building", text);
        var definition = Assert.Single(world.WorldContent.Buildings, item => item.LocalId == definitionLocalId);

        Assert.Equal(("construct_building", "waiting", kind, definition.CanonicalId),
            (order.Action, order.Status, order.TargetBuildingKind, order.TargetDefinitionId));
        Assert.Equal((1, 0, "buildings", explicitQuantity, false),
            (order.RequestedUnits, order.CompletedUnits, order.ProgressUnit, order.QuantityIsExplicit,
                order.RepeatUntilCancelled));
        Assert.Equal("cancelled", world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order!.Status);
        Assert.Null(order.TargetPosition);
        Assert.Null(order.TargetItemKind);
        Assert.Null(order.TargetRecipeId);
        Assert.Null(order.DeliveryPurpose);
        Assert.Null(order.ConstructionOwnerId);
        Assert.Null(order.ConstructionPosition);
        Assert.Null(order.ConstructionStartedTick);
        Assert.Null(order.ConstructionInstanceId);
        Assert.Null(order.ExpansionBinding);
        Assert.Empty(world.WorldSimulation.Buildings);
        world.Validate();
    }

    [Theory]
    [InlineData("expand my House", "house", false)]
    [InlineData("expand one my House", "house", true)]
    [InlineData("expand my Town Warehouse", "warehouse", false)]
    [InlineData("expand 1 my Town Warehouse", "warehouse", true)]
    public void ExpansionNamesAnExistingBuildingKindWithoutInventingAnInstance(
        string text, string kind, bool explicitQuantity)
    {
        using var world = CreateWorld();
        _ = Submit(world, "current", "eat berries");
        var order = Submit(world, "expansion", text);
        Assert.Equal(("expand_building", "waiting", kind, 1, "expansions", explicitQuantity, false),
            (order.Action, order.Status, order.TargetBuildingKind, order.RequestedUnits, order.ProgressUnit,
                order.QuantityIsExplicit, order.RepeatUntilCancelled));
        Assert.Equal("cancelled", world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order!.Status);
        Assert.Null(order.TargetDefinitionId);
        Assert.Null(order.ConstructionInstanceId);
        Assert.Null(order.ExpansionBinding);
        Assert.Empty(world.WorldSimulation.Buildings);
        world.Validate();
    }

    [Theory]
    [InlineData("build Warehouse")]
    [InlineData("build a Town Warehouse")]
    [InlineData("build Restaurant")]
    [InlineData("build Castle")]
    [InlineData("build Store 1x2")]
    [InlineData("build 2 House")]
    [InlineData("build 0 House")]
    [InlineData("build 1.5 House")]
    [InlineData("repeat build House")]
    [InlineData("keep building House")]
    [InlineData("build House until cancelled")]
    [InlineData("build House and Store")]
    [InlineData("expand a House")]
    [InlineData("expand my Warehouse")]
    [InlineData("expand my Clinic")]
    [InlineData("expand two my House")]
    [InlineData("expand my House until cancelled")]
    [InlineData("expand my House at (2, 1) and (3, 1)")]
    public void UnsupportedBuildingsCountsAndPartialCommandsPreserveTheCurrentTask(string text)
    {
        using var world = CreateWorld();
        var current = Submit(world, "current", "eat berries");
        var rejected = Submit(world, "unsupported", text);
        Assert.Equal(("unknown", "not_understood"), (rejected.Action, rejected.Status));
        Assert.Null(rejected.TargetBuildingKind);
        Assert.Null(rejected.TargetDefinitionId);
        Assert.Null(rejected.ExpansionBinding);
        Assert.Equal(current, world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order);
        Assert.Single(world.ExportState().Events, item => item.Kind == "instruction_not_understood");
    }

    [Theory]
    [InlineData(BusinessContent.PackageId, "build Store")]
    [InlineData(WarehouseContent.PackageId, "expand my Town Warehouse")]
    public void StagedBuildingPackagesCannotBeRequestedBeforeActivation(string packageId, string text)
    {
        using var world = CreateWorld(Packages.Where(package => package.PackageId != packageId));
        var package = Packages.Single(item => item.PackageId == packageId);
        world.ProposeContent(package);
        world.ValidateContent(packageId, world.ResolveContent(packageId));
        world.ApproveContent(packageId);
        Assert.Equal(ContentPackageLifecycle.Staged, world.StageContent(packageId).Lifecycle);

        var current = Submit(world, "current", "eat berries");
        var rejected = Submit(world, "inactive", text);
        Assert.Equal("not_understood", rejected.Status);
        Assert.Equal(current, world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order);
        world.Validate();
    }

    [Theory]
    [InlineData("please build one House at (2, 1) now.", "construct_building", "buildings")]
    [InlineData("expand my Town Warehouse at (2, 1)", "expand_building", "expansions")]
    public void ExactLocationsRoundTripWithoutPersistingUnresolvedWorkBindings(string text, string action, string progress)
    {
        using var world = CreateWorld();
        var original = Submit(world, "save", text);
        Assert.Equal((action, 1, progress, new GridPoint(2, 1)),
            (original.Action, original.RequestedUnits, original.ProgressUnit, original.TargetPosition!.Value));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var orderJson = Assert.Single(JsonNode.Parse(bytes)!["state"]!["instructions"]!.AsArray())!["order"]!.AsObject();
        foreach (var member in new[]
        {
            "constructionOwnerId", "constructionPosition", "constructionStartedTick", "constructionInstanceId", "expansionBinding",
        })
            Assert.False(orderJson.ContainsKey(member));
        Assert.Equal(action == "construct_building", orderJson.ContainsKey("targetDefinitionId"));

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(original, Assert.Single(restored.ExportState().Instructions!).Order);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void CheckpointRejectsMismatchedBuildingTargetsAndPartialConstructionBindings()
    {
        using var world = CreateWorld();
        var original = Submit(world, "saved-building", "build one House at (2, 1)");
        Assert.Equal("construct_building", original.Action);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var unsupportedFootprint = world.WorldContent.Buildings.Single(item => item.LocalId == "store-1x2").CanonicalId;
        (string Member, JsonNode? Value)[] corruptions =
        [
            ("targetDefinitionId", null),
            ("targetDefinitionId", JsonValue.Create("missing-definition")),
            ("targetDefinitionId", JsonValue.Create(unsupportedFootprint)),
            ("targetBuildingKind", null),
            ("targetBuildingKind", JsonValue.Create("farmhouse")),
            ("requestedUnits", JsonValue.Create(2)),
            ("repeatUntilCancelled", JsonValue.Create(true)),
            ("progressUnit", JsonValue.Create("expansions")),
            ("targetItemKind", JsonValue.Create("wood")),
            ("targetFoodKind", JsonValue.Create("berries")),
            ("targetRecipeId", JsonValue.Create("production-only")),
            ("deliveryPurpose", JsonValue.Create("household_stock")),
            ("constructionOwnerId", JsonValue.Create(Actor)),
            ("constructionPosition", JsonNode.Parse("""{"x":2,"y":1}""")),
            ("constructionStartedTick", JsonValue.Create(0)),
            ("constructionInstanceId", JsonValue.Create("unbound-house")),
            ("lastEffectId", JsonValue.Create("construction:unearned")),
            ("action", JsonValue.Create("expand_building")),
        ];
        foreach (var (member, value) in corruptions)
        {
            var document = JsonNode.Parse(bytes)!;
            Assert.Single(document["state"]!["instructions"]!.AsArray())!["order"]![member] = value;
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(JsonSerializer.SerializeToUtf8Bytes(document)));
        }
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(original, Assert.Single(restored.ExportState().Instructions!).Order);
    }

    [Theory]
    [InlineData("build one House at (2, 1)", "construct_building")]
    [InlineData("expand my Town Warehouse at (2, 1)", "expand_building")]
    public void BuildingSavesRejectAnAgentTarget(string text, string action)
    {
        using var world = CreateWorld();
        Assert.Equal(action, Submit(world, "agent-target", text).Action);
        var saved = world.ExportState();
        var bytes = PrivateWorldRuntimeCodec.Encode(saved);
        using var valid = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(valid.ExportState()));
        var mixed = saved with
        {
            Instructions = saved.Instructions!.Select(instruction => instruction with
            {
                Order = instruction.Order! with { TargetAgentId = Actor },
            }).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(mixed));
    }

    private static PrivateWorldRuntime CreateWorld(IEnumerable<ContentPackageManifest>? available = null)
    {
        var packages = (available ?? Packages).ToArray();
        var resolution = ContentPackageResolver.Resolve(packages, packages.Select(item => item.PackageId));
        Assert.True(resolution.IsSuccess, resolution.Diagnostic);
        var definitions = new DeclarativeWorldContentState([], []);
        foreach (var entry in resolution.Lock)
            definitions = ContentDefinitionPayloadCodec.ApplyPackage(definitions,
                packages.Single(package => package.PackageId == entry.PackageId));
        var records = packages.Select(package => new ContentPackageRecord(package, ContentPackageLifecycle.Active,
            ContentPackageRules.LockDigest(ContentPackageResolver.Resolve(packages, [package.PackageId]).Lock),
            ValidationTick: 0, ActivationTick: 0, StagedTick: 0,
            ManifestDigest: ContentPackageManifestCodec.ComputeManifestDigest(package))).ToArray();

        // Parsing needs immutable active definitions, not a generated settlement or construction work.
        using var genesis = new PrivateWorldRuntime("building-order-parser");
        return PrivateWorldRuntime.Restore(genesis.ExportState() with
        {
            Content = new ContentRegistryState(records, []),
            WorldContent = definitions,
        });
    }

    private static OwnerInstructionOrder Submit(PrivateWorldRuntime world, string key, string text)
    {
        var receipt = world.SubmitInstruction(new(key, "owner:test", Actor, OwnerInstructionKind.MustDo, text));
        return world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    }
}
