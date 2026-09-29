using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class RetiredBuildingTests
{
    [Fact]
    public async Task FreshWorldNeverOffersRetiredBuildingsOrResidentStudies()
    {
        using var baseline = new PrivateWorldRuntime("retired-fresh", _ => new IdleProvider());
        var state = baseline.ExportState();
        var inventory = state.Society.Society.Inventory;
        foreach (var person in state.Inhabitants)
        {
            foreach (var (kind, quantity) in new[] { ("wood", 40), ("stone", 20), ("fiber", 20) })
                inventory = InventoryFixture.AddLot(inventory, $"retired-fresh-{kind}:{person.InhabitantId}", kind,
                    person.InhabitantId, quantity);
        }
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = inventory,
                    Inhabitants = state.Society.Society.Inhabitants.Select(person =>
                        person with { CurrentRole = SocietyWorkRole.Builder }).ToArray(),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_000,
                Proficiency = new SettlementProficiency(Building: 9),
            }).ToArray(),
        };
        var provider = new RecordingProvider();
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);
        world.StageStarterContent();
        for (var tick = 0; tick < 12; tick++) await world.AdvanceOneTickAsync();

        var retired = world.WorldContent.Buildings.Where(RetiredBuildings.Contains)
            .Select(definition => definition.CanonicalId).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(["fire", "shelter", "stone-hearth", "storage"], world.WorldContent.Buildings
            .Where(RetiredBuildings.Contains).Select(definition => definition.LocalId).Order(StringComparer.Ordinal));
        var offered = provider.Candidates
            .Select(id => TownConstructionCandidateIds.TryParse(id, out var selection) && selection.IsBuilding
                ? selection.DefinitionId : null)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(offered);
        Assert.DoesNotContain(offered, retired.Contains);
        Assert.DoesNotContain(provider.Candidates, id => id.StartsWith("invent:building:", StringComparison.Ordinal));
        Assert.DoesNotContain(world.Content.Packages, item =>
            item.Manifest.PackageId.StartsWith("owner-building-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OldSaveKeepsRetiredBuildingsAndFinishesProjectsUnderWay()
    {
        using var seed = new PrivateWorldRuntime("retired-old-save", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var shelter = seed.WorldContent.Buildings.Single(item => item.LocalId == "shelter");
        var storehouse = seed.WorldContent.Buildings.Single(item => item.LocalId == "storage");
        var sites = state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position) &&
                !state.Map.CampObjects.Any(item => item.Position == tile.Position) &&
                !state.Map.Resources.Any(item => item.Position == tile.Position) &&
                !state.Inhabitants.Any(person => person.Position == tile.Position))
            .Select(tile => tile.Position).ToArray();
        var (builder, helper) = (state.Inhabitants[0], state.Inhabitants[1]);
        var (shelterSite, storehouseSite) = (sites[^1], sites[0]);
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "retired-tool", "tool", builder.InhabitantId, 1);
        inventory = InventoryFixture.AddLot(inventory, "retired-helper-tool", "tool", helper.InhabitantId, 1);
        inventory = InventoryFixture.AddLot(inventory, "retired-helper-wood", "wood", helper.InhabitantId, 6);
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = inventory,
                    Inhabitants = state.Society.Society.Inhabitants.Select(person =>
                        person with { Name = "sk-retired-private-name" }).ToArray(),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == builder.InhabitantId ? person with
            {
                Position = shelterSite,
                HungerBasisPoints = 9_000,
                Project = new(TownConstructionCandidateIds.Building(shelter.CanonicalId, shelterSite),
                    shelter.DisplayName, seed.WorldTick, "working", 10, LastTransitionTick: seed.WorldTick),
            } : person.InhabitantId == helper.InhabitantId ? person with
            {
                Position = storehouseSite,
                HungerBasisPoints = 9_000,
                Project = new(TownConstructionCandidateIds.Building(storehouse.CanonicalId, storehouseSite),
                    storehouse.DisplayName, seed.WorldTick, "working", 0, LastTransitionTick: seed.WorldTick),
            } : person).ToArray(),
        };
        using var before = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        await before.AdvanceOneTickAsync();
        Assert.Contains(before.WorldSimulation.Buildings, building => building.DefinitionId == shelter.CanonicalId);

        var saved = PrivateWorldRuntimeCodec.Encode(before.ExportState());
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new IdleProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Contains(world.WorldSimulation.Buildings, building =>
            building.DefinitionId == shelter.CanonicalId && building.Position == shelterSite);
        Assert.Equal("working", world.Inhabitants.Single(person => person.InhabitantId == helper.InhabitantId).Project!.Stage);

        var directory = Directory.CreateTempSubdirectory("retired-buildings-log-");
        try
        {
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using (var service = new PrivateWorldRuntimeService(world,
                       new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")),
                       new OwnerClientPresenceLease(TimeSpan.FromSeconds(30)), logger))
            {
                await service.StartAsync(CancellationToken.None);
                await service.StopAsync(CancellationToken.None);
            }
            Assert.Contains(logger.Messages, message => message ==
                $"retired_buildings tick={world.WorldTick} standing=1 projects=1 outcome=kept_not_offered");
            Assert.DoesNotContain(logger.Messages, message => message.Contains("sk-retired-private-name", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        for (var tick = 0; tick < 15 && !world.WorldSimulation.Buildings.Any(building =>
                 building.DefinitionId == storehouse.CanonicalId); tick++)
            await world.AdvanceOneTickAsync();
        Assert.Contains(world.WorldSimulation.Buildings, building =>
            building.DefinitionId == storehouse.CanonicalId && building.Position == storehouseSite);
        Assert.Equal("completed", world.Inhabitants.Single(person => person.InhabitantId == helper.InhabitantId).Project!.Stage);
    }

    [Fact]
    public async Task FreshWorldLogsNothingAboutRetiredBuildings()
    {
        using var world = new PrivateWorldRuntime("retired-quiet", _ => new IdleProvider());
        world.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await world.AdvanceOneTickAsync();
        Assert.Contains(world.WorldContent.Buildings, RetiredBuildings.Contains);
        var directory = Directory.CreateTempSubdirectory("retired-buildings-quiet-");
        try
        {
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using (var service = new PrivateWorldRuntimeService(world,
                       new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")),
                       new OwnerClientPresenceLease(TimeSpan.FromSeconds(30)), logger))
            {
                await service.StartAsync(CancellationToken.None);
                await service.StopAsync(CancellationToken.None);
            }
            Assert.DoesNotContain(logger.Messages, message => message.StartsWith("retired_buildings", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task OldResidentStudyStaysReviewableButNoNewStudyFollows()
    {
        using var seed = new PrivateWorldRuntime("retired-study", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var actor = state.Society.Society.Inhabitants[0];
        var registry = ContentPackageRegistry.Restore(state.Content);
        var study = BuildingDesign.Create("private-inventor's shelter study", "shelter", 7);
        registry.Propose(study, seed.WorldTick, actor.Id);
        state = state with
        {
            Content = registry.ExportState(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == actor.Id
                        ? person with { CurrentRole = SocietyWorkRole.Builder, Name = "private-inventor" }
                        : person).ToArray(),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor.Id
                ? person with { HungerBasisPoints = 9_000, Proficiency = new SettlementProficiency(Building: 9) }
                : person).ToArray(),
        };
        var provider = new RecordingProvider();
        using var world = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor.Id ? provider : new IdleProvider());
        for (var tick = 0; tick < 10; tick++) await world.AdvanceOneTickAsync();

        var package = Assert.Single(world.Content.Packages, item =>
            item.Manifest.PackageId.StartsWith("owner-building-", StringComparison.Ordinal));
        Assert.Equal(ContentPackageLifecycle.Proposed, package.Lifecycle);
        Assert.Equal("shelter", BuildingDesign.Read(package.Manifest).Purpose);
        Assert.Equal(actor.Id, new OwnerWorldObservationStore(world).GetSnapshot().ContentPackages
            .Single(item => item.PackageId == package.Manifest.PackageId).ProposedByInhabitantId);
        Assert.NotEmpty(provider.Candidates);
        Assert.DoesNotContain(provider.Candidates, id => id.StartsWith("invent:building:", StringComparison.Ordinal));
    }

    private sealed class RecordingProvider : IDecisionProvider
    {
        private readonly HashSet<string> candidates = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public IReadOnlyCollection<string> Candidates
        {
            get { lock (candidates) return candidates.ToArray(); }
        }

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            lock (candidates)
                candidates.UnionWith(request.Observation.Candidates.Select(candidate => candidate.Id));
            return new IdleProvider().DecideAsync(request, cancellationToken);
        }
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = request.Observation.Candidates.Where(candidate => candidate.Id == "safe_idle").ToArray(),
                },
            }, cancellationToken);
    }
}
