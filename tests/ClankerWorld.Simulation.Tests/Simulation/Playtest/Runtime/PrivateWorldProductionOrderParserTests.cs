using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldProductionOrderParserTests
{
    private const string Actor = "founder-ilya";
    private static readonly ContentPackageManifest[] ShippedPackages =
    [
        StarterContent.Create(), SettlementContent.Create(), HouseContent.Create(), FarmContent.Create(),
        BlacksmithContent.Create(), TailorContent.Create(), HouseCookingContent.Create(),
        OrnamentContent.Create(), CareContent.Create(),
    ];

    [Theory]
    [InlineData("make two sacks", "sew-sack", "sack", 2, "output_items", true)]
    [InlineData("make two workshop tools", "tools", "tool", 2, "output_items", true)]
    [InlineData("mill flour", "mill-grain", "flour", 1, "production_batches", false)]
    [InlineData("mill grain", "mill-grain", "flour", 1, "production_batches", false)]
    [InlineData("refine iron", "refine-iron", "iron", 1, "production_batches", false)]
    [InlineData("refine gold", "refine-gold", "gold", 1, "production_batches", false)]
    [InlineData("make iron knives", "iron-knife", "iron_knife", 1, "production_batches", false)]
    public void ProductionNamesBindTheExactActiveRecipeAndOutput(string text, string recipeLocalId,
        string outputKind, int requestedUnits, string progressUnit, bool explicitQuantity)
    {
        using var world = CreateWorld();
        var order = Submit(world, "production", text);
        var recipe = Assert.Single(world.WorldContent.Recipes, item => item.LocalId == recipeLocalId);

        Assert.Equal(("produce_item", "waiting", recipe.CanonicalId, outputKind),
            (order.Action, order.Status, order.TargetRecipeId, order.TargetOutputKind));
        Assert.Equal((requestedUnits, progressUnit, explicitQuantity, 0),
            (order.RequestedUnits, order.ProgressUnit, order.QuantityIsExplicit, order.CompletedUnits));
        Assert.False(order.RepeatUntilCancelled);
        Assert.Null(order.TargetFoodKind);
        Assert.Null(order.TargetMaterialKind);
        Assert.Null(order.TargetEquipmentKind);
        Assert.Null(order.TargetCropKind);
        Assert.Null(order.TargetResourceId);
        Assert.Null(order.TargetPosition);
        Assert.Null(order.LastEffectId);
        world.Validate();
    }

    [Theory]
    [InlineData("cook food", 1, "production_batches", false)]
    [InlineData("cook eight food", 8, "output_items", true)]
    [InlineData("cook two batches of food", 2, "production_batches", true)]
    public void CookingKeepsItemCountsDistinctFromBatchesOfFour(string text, int quantity,
        string progressUnit, bool explicitQuantity)
    {
        using var world = CreateWorld([StarterContent.Create()]);
        var recipe = Assert.Single(world.WorldContent.Recipes, item => item.LocalId == "meal");
        Assert.Equal(4, Assert.Single(recipe.Outputs).Amount);

        var order = Submit(world, "cook", text);
        Assert.Equal(("produce_item", recipe.CanonicalId, "food"),
            (order.Action, order.TargetRecipeId, order.TargetOutputKind));
        Assert.Equal((quantity, progressUnit, explicitQuantity),
            (order.RequestedUnits, order.ProgressUnit, order.QuantityIsExplicit));
    }

    [Theory]
    [InlineData("make sacks", 1, false, false)]
    [InlineData("keep making sacks", 1, false, true)]
    [InlineData("repeat make two sacks", 2, true, true)]
    [InlineData("make sacks until cancelled", 1, false, true)]
    public void DefaultAndRepeatingProductionKeepTheirQuantityMeaning(string text, int quantity,
        bool explicitQuantity, bool repeat)
    {
        using var world = CreateWorld();
        var order = Submit(world, "repeat", text);
        Assert.Equal(("produce_item", "sack", quantity, explicitQuantity, repeat),
            (order.Action, order.TargetOutputKind, order.RequestedUnits, order.QuantityIsExplicit,
                order.RepeatUntilCancelled));
        Assert.Equal(explicitQuantity ? "output_items" : "production_batches", order.ProgressUnit);
    }

    [Fact]
    public void ProductionRecipeOutputAndExactTileSurviveCheckpointRoundTrip()
    {
        using var world = CreateWorld();
        var order = Submit(world, "tile", "make two sacks at (2, 1)");
        Assert.Equal("produce_item", order.Action);
        Assert.Equal(new GridPoint(2, 1), order.TargetPosition);
        Assert.Equal(world.WorldContent.Recipes.Single(item => item.LocalId == "sew-sack").CanonicalId,
            order.TargetRecipeId);
        Assert.Equal(("sack", 2, "output_items"),
            (order.TargetOutputKind, order.RequestedUnits, order.ProgressUnit));

        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(order, Assert.Single(restored.ExportState().Instructions!).Order);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData("cook food", "cook household meal", "house-meal", "food")]
    [InlineData("cook food", "cook camp meal", "meal", "food")]
    [InlineData("cook food", "cook hearty meal", "hearty-meal", "food")]
    [InlineData("make bandages", "make house bandages", "house-bandages", "bandage")]
    [InlineData("make bandages", "make tailor bandages", "tailor-bandages", "bandage")]
    public void AmbiguousOutputsNeedARecipeQualifier(string ambiguous, string qualified,
        string recipeLocalId, string outputKind)
    {
        using var world = CreateWorld();
        var current = Submit(world, "current", "eat berries");
        var rejected = Submit(world, "ambiguous", ambiguous);
        Assert.Equal("not_understood", rejected.Status);
        Assert.Equal(current, world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order);

        var accepted = Submit(world, "qualified", qualified);
        var recipe = Assert.Single(world.WorldContent.Recipes, item => item.LocalId == recipeLocalId);
        Assert.Equal(("produce_item", recipe.CanonicalId, outputKind),
            (accepted.Action, accepted.TargetRecipeId, accepted.TargetOutputKind));
    }

    [Fact]
    public void StagedRecipesCannotBecomeProductionTargetsBeforeActivation()
    {
        using var world = CreateWorld(ShippedPackages.Where(package =>
            package.PackageId is not (TailorContent.PackageId or CareContent.PackageId)));
        var tailor = TailorContent.Create();
        world.ProposeContent(tailor);
        world.ValidateContent(tailor.PackageId, world.ResolveContent(tailor.PackageId));
        world.ApproveContent(tailor.PackageId);
        Assert.Equal(ContentPackageLifecycle.Staged, world.StageContent(tailor.PackageId).Lifecycle);

        var current = Submit(world, "current", "eat berries");
        var rejected = Submit(world, "inactive", "make two sacks");
        Assert.Equal("not_understood", rejected.Status);
        Assert.Equal(current, world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order);
        Assert.DoesNotContain(world.WorldContent.Recipes, item => item.LocalId == "sew-sack");
        world.Validate();
    }

    [Theory]
    [InlineData("make sacks and cook food")]
    [InlineData("make dragons")]
    [InlineData("make tools")]
    [InlineData("make stone hoes")]
    [InlineData("make 0 sacks")]
    [InlineData("make 1001 sacks")]
    [InlineData("make 1.5 sacks")]
    [InlineData("mill wood")]
    [InlineData("refine sacks")]
    [InlineData("cook bread")]
    [InlineData("cook porridge")]
    [InlineData("cook vegetable stew")]
    [InlineData("cook restaurant meals")]
    public void UnsupportedOrPartialProductionTextDoesNotReplaceTheCurrentOrder(string text)
    {
        using var world = CreateWorld();
        var current = Submit(world, "current", "make sacks");
        var rejected = Submit(world, "unsupported", text);
        Assert.Equal(("unknown", "not_understood"), (rejected.Action, rejected.Status));
        Assert.Null(rejected.TargetRecipeId);
        Assert.Null(rejected.TargetOutputKind);
        Assert.Equal(current, world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order);
        Assert.Single(world.ExportState().Events, item => item.Kind == "instruction_not_understood");
    }

    [Fact]
    public void ItemCountsThatCannotBeMadeInWholeBatchesAreRejected()
    {
        using var world = CreateWorld([StarterContent.Create()]);
        var current = Submit(world, "current", "eat berries");
        var rejected = Submit(world, "partial-batch", "cook two food");
        Assert.Equal("not_understood", rejected.Status);
        Assert.Equal(current, world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order);
    }

    [Fact]
    public void CheckpointRejectsInvalidProductionTargetsAndProgress()
    {
        using var world = CreateWorld([StarterContent.Create()]);
        var original = Submit(world, "saved-production", "cook eight food");
        Assert.Equal(("produce_item", "food", 8),
            (original.Action, original.TargetOutputKind, original.RequestedUnits));
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var cropId = world.WorldContent.Recipes.Single(item => item.LocalId == "vegetables").CanonicalId;
        (string Member, JsonNode? Value)[] corruptions =
        [
            ("targetRecipeId", null),
            ("targetRecipeId", JsonValue.Create("missing-recipe")),
            ("targetRecipeId", JsonValue.Create(cropId)),
            ("targetOutputKind", null),
            ("targetOutputKind", JsonValue.Create("sack")),
            ("targetFoodKind", JsonValue.Create("berries")),
            ("targetMaterialKind", JsonValue.Create("wood")),
            ("targetEquipmentKind", JsonValue.Create("sack")),
            ("targetCropKind", JsonValue.Create("grain")),
            ("targetResourceId", JsonValue.Create("berry-patch")),
            ("action", JsonValue.Create("consume_food")),
            ("progressUnit", JsonValue.Create("food_items")),
            ("quantityIsExplicit", JsonValue.Create(false)),
            ("requestedUnits", JsonValue.Create(6)),
            ("productionJobId", JsonValue.Create("unbound-job")),
            ("lastEffectId", JsonValue.Create("production:unearned")),
        ];
        foreach (var (member, value) in corruptions)
        {
            var document = JsonNode.Parse(bytes)!;
            var order = Assert.Single(document["state"]!["instructions"]!.AsArray())!["order"]!;
            order[member] = value;
            var damaged = JsonSerializer.SerializeToUtf8Bytes(document);
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(damaged));
        }
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(original, Assert.Single(restored.ExportState().Instructions!).Order);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static PrivateWorldRuntime CreateWorld(IEnumerable<ContentPackageManifest>? manifests = null)
    {
        var packages = (manifests ?? ShippedPackages).ToArray();
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

        // Parsing needs active immutable definitions, but no placed buildings or world steps.
        using var genesis = new PrivateWorldRuntime("production-order-parser");
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
