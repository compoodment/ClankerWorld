using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class ContentOrderRollbackSafetyTests
{
    private const string Actor = "founder-ilya";

    [Theory]
    [InlineData("prepare medicine", "produce_item", false)]
    [InlineData("prepare medicine", "produce_item", true)]
    public void RollbackPreservesUnboundOrdersIncludingCancelledHistory(string text, string action, bool cancelled)
    {
        using var world = CreateWorld();
        var receipt = world.SubmitInstruction(new("order", "owner:test", Actor, OwnerInstructionKind.MustDo, text));
        if (cancelled)
            Assert.True(world.CancelOrder(new("cancel", "owner:test", world.Society.WorldId,
                Actor, receipt.InstructionId)).Changed);
        var order = Assert.Single(world.ExportState().Instructions!).Order!;
        Assert.Equal((action, cancelled ? "cancelled" : "waiting"), (order.Action, order.Status));
        Assert.Null(order.ProductionBuildingId);
        Assert.Empty(world.WorldSimulation.Buildings);
        Assert.Empty(world.WorldSimulation.ProductionJobs);
        Assert.Equal(0, world.WorldTick);

        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var beforeReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(beforeReload.ExportState()));

        var error = Assert.Throws<InvalidOperationException>(() => world.RollbackContent(CareContent.PackageId, "withdraw unused care"));
        Assert.Contains("migration", error.Message, StringComparison.OrdinalIgnoreCase);
        var after = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(before, after);
        using var afterReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(after));
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(afterReload.ExportState()));
    }

    [Fact]
    public void UnrelatedRollbackLeavesProductionOrdersLoadable()
    {
        using var world = CreateWorld();
        world.SubmitInstruction(new("production", "owner:test", Actor, OwnerInstructionKind.MustDo,
            "prepare medicine"));
        var before = world.ExportState().Instructions!.ToArray();
        Assert.Equal("produce_item", Assert.Single(before).Order!.Action);

        var receipt = world.RollbackContent(PotteryContent.PackageId, "withdraw unrelated pottery");

        Assert.Equal(ContentPackageLifecycle.Quarantined, receipt.Lifecycle);
        Assert.DoesNotContain(world.WorldContent.Recipes, recipe => recipe.PackageDigest == PotteryContent.Create().PackageDigest);
        Assert.Equal(before, world.ExportState().Instructions!.ToArray());
        Assert.Equal(0, world.WorldTick);
        var after = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(after));
        Assert.Equal(after, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    private static PrivateWorldRuntime CreateWorld()
    {
        ContentPackageManifest[] packages =
        [
            StarterContent.Create(), SettlementContent.Create(), HouseContent.Create(),
            TailorContent.Create(), CareContent.Create(), PotteryContent.Create(),
        ];
        var resolution = ContentPackageResolver.Resolve(packages, packages.Select(package => package.PackageId));
        Assert.True(resolution.IsSuccess, resolution.Diagnostic);
        var definitions = new DeclarativeWorldContentState([], []);
        foreach (var entry in resolution.Lock)
            definitions = ContentDefinitionPayloadCodec.ApplyPackage(definitions,
                packages.Single(package => package.PackageId == entry.PackageId));
        var records = packages.Select(package => new ContentPackageRecord(package, ContentPackageLifecycle.Active,
            ContentPackageRules.LockDigest(ContentPackageResolver.Resolve(packages, [package.PackageId]).Lock),
            ValidationTick: 0, ActivationTick: 0, StagedTick: 0,
            ManifestDigest: ContentPackageManifestCodec.ComputeManifestDigest(package))).ToArray();
        // Active definitions suffice: no building, production job or world step can independently block rollback.
        using var genesis = new PrivateWorldRuntime("content-order-rollback");
        return PrivateWorldRuntime.Restore(genesis.ExportState() with
        {
            Content = new ContentRegistryState(records, []),
            WorldContent = definitions,
        });
    }
}
