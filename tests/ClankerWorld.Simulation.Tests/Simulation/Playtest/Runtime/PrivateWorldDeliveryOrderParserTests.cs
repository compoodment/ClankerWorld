using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class PrivateWorldDeliveryOrderParserTests
{
    private const string Actor = "founder-ilya";
    private static readonly ContentPackageManifest[] Packages =
    [
        StarterContent.Create(), SettlementContent.Create(), HouseContent.Create(), FarmContent.Create(),
        BlacksmithContent.Create(), TailorContent.Create(), PotteryContent.Create(), OrnamentContent.Create(),
        CareContent.Create(), HouseCookingContent.Create(),
    ];

    [Theory]
    [InlineData("haul two wood to my House", "household_stock", "wood", "house")]
    [InlineData("haul two grain to my Farmhouse", "household_stock", "grain", "farmhouse")]
    [InlineData("haul two potatoes to my Silo", "household_stock", "potatoes", "silo")]
    [InlineData("haul two flour to my House", "household_stock", "flour", "house")]
    [InlineData("supply two iron ore to my Blacksmith", "workstation_input", "iron_ore", "blacksmith")]
    [InlineData("supply two fresh water to my Clinic", "workstation_input", "fresh_water", "clinic")]
    [InlineData("supply two cloth to my Tailor Shop", "workstation_input", "cloth", "tailor")]
    [InlineData("supply two clay to my House", "workstation_input", "clay", "house")]
    [InlineData("deliver two berries to my House", "household_food", "berries", "house")]
    [InlineData("donate two tree seeds to my Town Warehouse", "town_surplus", "tree_seed", "warehouse")]
    [InlineData("stock two iron knives in my Store", "store_stock", "iron_knife", "store")]
    [InlineData("stock two diamond ornaments in my Store", "store_stock", "diamond_ornament", "store")]
    public void PolicyVerbsKeepExactItemsAndDestinationKinds(string text, string purpose, string kind, string building)
    {
        using var world = CreateWorld(activeInputs: purpose == "workstation_input");
        var order = Submit(world, "delivery", text + " at (2, 1)");
        Assert.Equal(("deliver_stock", "waiting", purpose, kind, building),
            (order.Action, order.Status, order.DeliveryPurpose, order.TargetItemKind, order.TargetBuildingKind));
        Assert.Equal((2, 0, "goods_items", true, false),
            (order.RequestedUnits, order.CompletedUnits, order.ProgressUnit, order.QuantityIsExplicit, order.RepeatUntilCancelled));
        Assert.Equal(new GridPoint(2, 1), order.TargetPosition);
        Assert.Null(order.DeliveryRoute);
        Assert.Null(order.DeliveryLotId);
        Assert.Null(order.DeliveryQuantity);
        Assert.Null(order.TargetStorageBuildingId);
        Assert.Null(order.TargetStorageOwnerId);
        Assert.Null(order.TargetStoragePosition);
        Assert.Null(order.TargetLotId);
        Assert.Null(order.TargetFoodKind);
        Assert.Null(order.TargetEquipmentKind);
        Assert.Null(order.TargetMaterialKind);
        Assert.Null(order.TargetRecipeId);
        world.Validate();
    }

    [Theory]
    [InlineData("haul grain to my Farmhouse", 1, "delivery_loads", false, false)]
    [InlineData("keep donating wood to my Town Warehouse", 1, "delivery_loads", false, true)]
    [InlineData("stock a sack in my Store", 1, "goods_items", true, false)]
    [InlineData("deliver three cultivated greens to my House until cancelled", 3, "goods_items", true, true)]
    public void LoadsAndExactItemCountsRetainTheirMeaning(string text, int quantity, string progress, bool explicitQuantity, bool repeat)
    {
        using var world = CreateWorld();
        var order = Submit(world, "quantity", text);
        Assert.Equal(("deliver_stock", quantity, progress, explicitQuantity, repeat),
            (order.Action, order.RequestedUnits, order.ProgressUnit, order.QuantityIsExplicit, order.RepeatUntilCancelled));
    }

    [Theory]
    [InlineData("donate two food to my Town Warehouse")]
    [InlineData("donate grain to my Town Warehouse")]
    [InlineData("donate wood to my Warehouse")]
    [InlineData("supply cloth to my Workshop")]
    [InlineData("supply fresh water to my Blacksmith")]
    [InlineData("supply grain to my Farmhouse")]
    [InlineData("supply fiber to my House")]
    [InlineData("deliver iron to my House")]
    [InlineData("deliver berries to my Clinic")]
    [InlineData("haul wood to my Silo")]
    [InlineData("haul seeds to my Farmhouse")]
    [InlineData("stock iron in my Store")]
    [InlineData("stock water jugs in my Store")]
    [InlineData("stock tools in my Store")]
    [InlineData("deliver porridge to my House")]
    [InlineData("haul 0 grain to my Farmhouse")]
    [InlineData("donate -1 wood to my Town Warehouse")]
    [InlineData("stock 1001 cloth in my Store")]
    [InlineData("deliver 1.5 berries to my House")]
    [InlineData("haul grain to my Farmhouse and donate wood to my Town Warehouse")]
    public void UnsupportedPoliciesAndPartialCommandsCannotReplaceTheCurrentTask(string text)
    {
        using var world = CreateWorld(activeInputs: true);
        var original = Submit(world, "current", "eat berries");
        var refused = Submit(world, "refused", text);
        Assert.Equal(("unknown", "not_understood"), (refused.Action, refused.Status));
        Assert.Null(refused.DeliveryPurpose);
        Assert.Null(refused.TargetBuildingKind);
        Assert.Null(refused.TargetItemKind);
        Assert.Equal(original, world.ExportState().Instructions!.Single(item => item.IdempotencyKey == "current").Order);
    }

    [Fact]
    public void SupplyRequiresAnActiveShippedRecipeInputAtTheNamedBuildingKind()
    {
        using var inactive = CreateWorld();
        Assert.Empty(inactive.WorldContent.Recipes);
        Assert.Equal("not_understood", Submit(inactive, "inactive", "supply cloth to my Tailor Shop").Status);
        using var active = CreateWorld(activeInputs: true);
        var accepted = Submit(active, "active", "supply cloth to my Tailor");
        Assert.Equal(("deliver_stock", "workstation_input", "cloth", "tailor"),
            (accepted.Action, accepted.DeliveryPurpose, accepted.TargetItemKind, accepted.TargetBuildingKind));
        Assert.Empty(active.WorldSimulation.Buildings);
    }

    [Fact]
    public void UnboundDeliveryIntentRoundTripsAndCorruptShipmentShapesAreRefused()
    {
        using var world = CreateWorld();
        var original = Submit(world, "save", "donate two wood to my Town Warehouse at (2, 1)");
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var orderJson = Assert.Single(JsonNode.Parse(bytes)!["state"]!["instructions"]!.AsArray())!["order"]!.AsObject();
        Assert.False(orderJson.ContainsKey("deliveryRoute"));
        Assert.False(orderJson.ContainsKey("deliveryLotId"));
        Assert.False(orderJson.ContainsKey("deliveryQuantity"));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(original, Assert.Single(restored.ExportState().Instructions!).Order);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

        (string Member, JsonNode? Value)[] corruptions =
        [
            ("deliveryPurpose", null),
            ("deliveryPurpose", JsonValue.Create("household_food")),
            ("targetBuildingKind", JsonValue.Create("store")),
            ("targetItemKind", JsonValue.Create("food")),
            ("deliveryRoute", JsonValue.Create("town_surplus")),
            ("deliveryLotId", JsonValue.Create("unbound-cargo")),
            ("deliveryQuantity", JsonValue.Create(2)),
            ("targetStorageBuildingId", JsonValue.Create("unbound-warehouse")),
            ("targetStorageOwnerId", JsonValue.Create("unknown-town")),
            ("targetStoragePosition", JsonNode.Parse("""{"x":2,"y":1}""")),
            ("targetLotId", JsonValue.Create("borrowed-return-only")),
            ("targetFoodKind", JsonValue.Create("berries")),
            ("targetRecipeId", JsonValue.Create("production-only")),
            ("progressUnit", JsonValue.Create("storage_loads")),
            ("quantityIsExplicit", JsonValue.Create(false)),
            ("lastEffectId", JsonValue.Create("delivery:stock:unearned")),
            ("action", JsonValue.Create("store_goods")),
        ];
        foreach (var (member, value) in corruptions)
        {
            var document = JsonNode.Parse(bytes)!;
            Assert.Single(document["state"]!["instructions"]!.AsArray())!["order"]![member] = value;
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(JsonSerializer.SerializeToUtf8Bytes(document)));
        }
    }

    private static PrivateWorldRuntime CreateWorld(bool activeInputs = false)
    {
        var genesis = new PrivateWorldRuntime("delivery-order-parser");
        if (!activeInputs) return genesis;
        using (genesis)
        {
            var resolution = ContentPackageResolver.Resolve(Packages, Packages.Select(item => item.PackageId));
            Assert.True(resolution.IsSuccess, resolution.Diagnostic);
            var definitions = new DeclarativeWorldContentState([], []);
            foreach (var entry in resolution.Lock)
                definitions = ContentDefinitionPayloadCodec.ApplyPackage(definitions, Packages.Single(package => package.PackageId == entry.PackageId));
            var records = Packages.Select(package => new ContentPackageRecord(package, ContentPackageLifecycle.Active,
                ContentPackageRules.LockDigest(ContentPackageResolver.Resolve(Packages, [package.PackageId]).Lock),
                ValidationTick: 0, ActivationTick: 0, StagedTick: 0,
                ManifestDigest: ContentPackageManifestCodec.ComputeManifestDigest(package))).ToArray();
            // Parsing needs active definitions, not a generated settlement or world steps.
            return PrivateWorldRuntime.Restore(genesis.ExportState() with
            {
                Content = new ContentRegistryState(records, []),
                WorldContent = definitions,
            });
        }
    }

    private static OwnerInstructionOrder Submit(PrivateWorldRuntime world, string key, string text)
    {
        var receipt = world.SubmitInstruction(new(key, "owner:test", Actor, OwnerInstructionKind.MustDo, text));
        return world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
    }
}
