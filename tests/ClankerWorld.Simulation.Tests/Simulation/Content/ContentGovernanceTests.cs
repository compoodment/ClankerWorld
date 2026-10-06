using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Tests;

public sealed class ContentGovernanceTests
{
    [Fact]
    public void ResolverReportsCyclesAndDoesNotReturnAPartialLock()
    {
        var first = Package(
            "first",
            "1.0.0",
            'a',
            [Dependency("second")]);
        var second = Package(
            "second",
            "1.0.0",
            'b',
            [Dependency("first")]);

        var result = ContentPackageResolver.Resolve([first, second], ["first"]);

        Assert.False(result.IsSuccess);
        Assert.Equal("dependency_cycle", result.FailureCode);
        Assert.Empty(result.Lock);
    }

    [Fact]
    public void ExecutableCapabilitiesAreDisabledAtTheDataOnlyBoundary()
    {
        var package = Package("unsafe", "1.0.0", 'a', capabilities: ["network"]);
        var registry = new ContentPackageRegistry();

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Propose(package));

        Assert.Contains("disabled capabilities", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuarantinedDependenciesCannotBeStagedOrActivated(bool stageBeforeQuarantine)
    {
        var core = Package("core", "1.0.0", 'a');
        var world = Package("world", "1.0.0", 'b', [Dependency("core")]);
        var registry = new ContentPackageRegistry();
        registry.Propose(core);
        registry.Propose(world);
        registry.Validate("core", ContentPackageResolver.Resolve([core, world], ["core"]), 0);
        registry.Approve("core", 0);
        registry.Stage("core", 0);
        registry.Activate("core", 1);
        registry.Validate("world", ContentPackageResolver.Resolve([core, world], ["world"]), 1);
        registry.Approve("world", 1);
        if (stageBeforeQuarantine)
        {
            registry.Stage("world", 1);
        }
        registry.Rollback("core", 2, "owner quarantine");
        registry = ContentPackageRegistry.Restore(registry.ExportState());
        if (stageBeforeQuarantine)
        {
            Assert.Empty(registry.ActivateReady(3));
            Assert.Throws<InvalidOperationException>(() => registry.Activate("world", 3));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => registry.Stage("world", 3));
        }
    }

    [Fact]
    public void ActiveDependentsPreventDependencyRollbackWithoutPartialMutation()
    {
        var core = Package("core", "1.0.0", 'a');
        var world = Package("world", "1.0.0", 'b', [Dependency("core")]);
        var registry = new ContentPackageRegistry();
        foreach (var package in new[] { core, world })
        {
            registry.Propose(package);
        }
        foreach (var package in new[] { core, world })
        {
            registry.Validate(package.PackageId, ContentPackageResolver.Resolve([core, world], [package.PackageId]), 0);
            registry.Approve(package.PackageId, 0);
            registry.Stage(package.PackageId, 0);
        }
        registry.ActivateReady(1);
        var before = registry.ExportState();
        Assert.Throws<InvalidOperationException>(() => registry.Rollback("core", 2, "owner rollback"));
        Assert.Equal(before.Packages, registry.ExportState().Packages);
        Assert.Equal(before.Events, registry.ExportState().Events);
        registry.Rollback("world", 2, "remove dependent first");
        registry.Rollback("core", 2, "now safe");
        Assert.All(ContentPackageRegistry.Restore(registry.ExportState()).ExportState().Packages,
            package => Assert.Equal(ContentPackageLifecycle.Quarantined, package.Lifecycle));
    }

    [Fact]
    public void ReadyPackagesActivateInDependencyOrderNotLexicalOrder()
    {
        var core = Package("z-core", "1.0.0", 'a');
        var world = Package("a-world", "1.0.0", 'b', [Dependency("z-core")]);
        var registry = new ContentPackageRegistry();
        registry.Propose(core);
        registry.Propose(world);
        foreach (var package in new[] { core, world })
        {
            registry.Validate(package.PackageId, ContentPackageResolver.Resolve([core, world], [package.PackageId]), 0);
            registry.Approve(package.PackageId, 0);
            registry.Stage(package.PackageId, 0);
        }
        var active = registry.ActivateReady(1);
        Assert.Equal(["z-core", "a-world"], active.Select(item => item.Manifest.PackageId));
        Assert.All(active, package =>
        {
            Assert.Equal(ContentPackageLifecycle.Active, package.Lifecycle);
            Assert.Equal(1, package.ActivationTick);
        });
        Assert.Equal(
            ["package_proposed", "package_validated", "package_approved", "package_staged", "package_activated"],
            registry.ExportState().Events.Where(item => item.PackageId == "z-core").Select(item => item.Kind));
    }

    private static ContentPackageManifest Package(
        string id,
        string version,
        char digestCharacter,
        IReadOnlyList<ContentDependency>? dependencies = null,
        IReadOnlyList<string>? capabilities = null) => new(
        id,
        ContentVersion.Parse(version),
        "sha256:" + new string(digestCharacter, 64),
        dependencies ?? [],
        [Definition("item", $"{id}-content", "sha256:" + new string(digestCharacter, 64))],
        capabilities ?? []);

    private static ContentDependency Dependency(string packageId) => new(
        packageId,
        new ContentVersionRange(ContentVersion.Parse("1.0.0"), ContentVersion.Parse("2.0.0")));

    private static ContentDefinition Definition(string kind, string localId, string digest) => new(
        kind,
        localId,
        ContentVersion.Parse("1.0.0"),
        localId,
        digest);
}
