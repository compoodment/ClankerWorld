using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Viewer.Observation;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementProjectTests
{
    [Fact]
    public async Task ExistingHouseEndsAStaleSecondHouseProjectWithoutConsumingMaterials()
    {
        using var seed = new PrivateWorldRuntime("one-house-per-household-project", _ => new IdleProvider());
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 6; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);
        var state = seed.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person =>
            person.HouseholdId == "household:camp-alpha").Id;
        var sites = state.Map.Tiles.Select(tile => tile.Position).Where(point =>
            state.Map.IsBuildable(point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point)).Take(2).ToArray();
        Assert.Equal(2, sites.Length);
        var house = seed.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        Assert.True(seed.PlaceBuilding("existing-alpha-house", house.CanonicalId, sites[0],
            "household:camp-alpha").Applied);
        state = seed.ExportState();
        var woodBefore = seed.Society.Inventory.Lots.Where(lot =>
            lot.OwnerId == "household:camp-alpha" && lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    HungerBasisPoints = 9_000,
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(house.CanonicalId, sites[1]),
                        house.DisplayName, seed.WorldTick, "acquiring", LastTransitionTick: seed.WorldTick),
                }
                : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var project = world.Inhabitants.Single(person => person.InhabitantId == actor).Project!;
        Assert.Equal("cancelled", project.Stage);
        Assert.Equal("This household already has a House.", project.Blocker);
        Assert.Single(world.WorldSimulation.Buildings, building => building.HouseholdId == "household:camp-alpha");
        Assert.Equal(woodBefore, world.Society.Inventory.Lots.Where(lot =>
            lot.OwnerId == "household:camp-alpha" && lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "project_progress" &&
            item.Detail.Contains("already has a House", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CarriedProjectWoodAndHouseholdHelpAreDeliveredAtHome()
    {
        using var seed = new PrivateWorldRuntime("project-material-home", _ => new IdleProvider());
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 6; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var state = seed.ExportState();
        var actor = state.Society.Society.Inhabitants[0].Id;
        var homeSite = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.IsBuildable(point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var house = seed.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        var placed = seed.PlaceBuilding("project-home", house.CanonicalId, homeSite, "household:camp-alpha");
        Assert.True(placed.Applied, placed.Failure);
        state = seed.ExportState();
        var buildSite = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            point != homeSite && state.Map.IsBuildable(point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var projectBuilding = seed.WorldContent.Buildings.Single(building => building.LocalId == "workshop");
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot =>
                lot.OwnerId != "household:camp-alpha" || lot.ItemKind != "wood").ToArray(),
        };
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = state.Map.GetObject("storage").Position,
                    HungerBasisPoints = 9_000,
                    Project = new SettlementProject(
                        TownConstructionCandidateIds.Building(projectBuilding.CanonicalId, buildSite),
                        projectBuilding.DisplayName, seed.WorldTick, "acquiring", LastTransitionTick: seed.WorldTick),
                } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(inventory, "personal-project-wood", "wood", actor, 4),
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        for (var tick = 0; tick < 20 && !world.ExportState().Events.Any(item =>
                 item.Kind == "project_material_delivered" && item.Detail == actor + ":wood"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var delivery = world.ExportState().Events.Single(item =>
            item.Kind == "project_material_delivered" && item.Detail == actor + ":wood");
        Assert.Equal(homeSite, delivery.Position);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.OwnerId == "household:camp-alpha" &&
            lot.ItemKind == "wood" && lot.Quantity == 4 && lot.StorageBuildingId == "project-home");
        Assert.Contains(new OwnerWorldObservationStore(world).GetSnapshot().PlacedBuildings
            .Single(building => building.InstanceId == "project-home").StoredItems!,
            item => item.Kind == "wood" && item.Quantity == 4);

        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Contains(restored.Society.Inventory.Lots, lot => lot.OwnerId == "household:camp-alpha" &&
            lot.ItemKind == "wood" && lot.Quantity == 4 && lot.StorageBuildingId == "project-home");

        var helper = state.Society.Society.Inhabitants.First(person => person.Id != actor).Id;
        var helping = world.ExportState();
        helping = helping with
        {
            Inhabitants = helping.Inhabitants.Select(person => person.InhabitantId == helper
                ? person with { Position = homeSite, HungerBasisPoints = 9_000, LastDecisionContext = null } : person).ToArray(),
            Society = helping.Society with
            {
                Society = helping.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(helping.Society.Society.Inventory,
                        "helper-project-wood", "wood", helper, 4),
                },
            },
        };
        using var helped = PrivateWorldRuntime.Restore(helping,
            id => id == helper ? new PreferredCandidateProvider("assist:wood") : new IdleProvider());
        Assert.True((await helped.AdvanceOneTickAsync()).Advanced);
        var contribution = helped.ExportState().Events.Single(item =>
            item.Kind == "project_request_fulfilled" &&
            item.Detail == $"{helper}:{actor}:wood:4");
        Assert.Equal(homeSite, contribution.Position);
        Assert.Contains(helped.Society.Inventory.Lots, lot => lot.Id == "helper-project-wood" &&
            lot.OwnerId == "household:camp-alpha" &&
            lot.ItemKind == "wood" && lot.Quantity == 4 && lot.StorageBuildingId == "project-home" &&
            lot.DeliveryBuildingId is null);
        using var resumed = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(helped.ExportState())));
        Assert.Contains(resumed.Society.Inventory.Lots, lot => lot.Id == "helper-project-wood" &&
            lot.OwnerId == "household:camp-alpha" &&
            lot.ItemKind == "wood" && lot.StorageBuildingId == "project-home" &&
            lot.DeliveryBuildingId is null);
    }

    [Fact]
    public async Task FinishedWorkBuildsAtTheCurrentValidSiteInsteadOfRetargetingAnEarlierTile()
    {
        using var seed = new PrivateWorldRuntime("stable-project-site", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var actor = state.Inhabitants[0];
        var position = state.Map.Tiles.Last(tile => state.Map.IsBuildable(tile.Position) &&
            !state.Map.CampObjects.Any(item => item.Position == tile.Position) &&
            !state.Map.Resources.Any(item => item.Position == tile.Position) &&
            !state.Inhabitants.Any(person => person.Position == tile.Position)).Position;
        var definition = seed.WorldContent.Buildings.Single(building => building.LocalId == "fire");
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "site-tool", "tool", actor.InhabitantId, 1),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person == actor ? person with
            {
                Position = position,
                HungerBasisPoints = 9_000,
                Project = new(TownConstructionCandidateIds.Building(definition.CanonicalId, position),
                    definition.DisplayName, seed.WorldTick, "working", 10,
                    LastTransitionTick: seed.WorldTick),
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        await world.AdvanceOneTickAsync();
        Assert.True(world.WorldSimulation.Buildings.Any(building => building.DefinitionId == definition.CanonicalId && building.Position == position),
            System.Text.Json.JsonSerializer.Serialize(world.Inhabitants.Single(person => person.InhabitantId == actor.InhabitantId)) +
            System.Text.Json.JsonSerializer.Serialize(world.ExportState().Events.TakeLast(12)));
        Assert.Equal("completed", world.Inhabitants.Single(person => person.InhabitantId == actor.InhabitantId).Project!.Stage);
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = request.Observation.Candidates.Where(candidate => candidate.Id == "safe_idle").ToArray() },
            }, cancellationToken);
    }

    private sealed class PreferredCandidateProvider(string candidateId) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == candidateId) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }

    [Fact]
    public async Task UnregisteredMapAdditionIsRejectedEvenWithARecomputedDigest()
    {
        using var world = new PrivateWorldRuntime("invalid-settlement-map");
        world.StageStarterContent();
        for (var tick = 0; tick < 10; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        var state = world.ExportState();
        var forgedMap = state.Map with
        {
            Resources = state.Map.Resources.Select(resource => resource.Id == "settlement-stone"
                ? resource with { Id = "unregistered-stone" } : resource).ToArray(),
        };
        forgedMap = forgedMap with { ManifestDigest = MapManifestCodec.Digest(forgedMap) };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with { Map = forgedMap }));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(11)]
    public async Task LegacyCheckpointMigratesOnLoadWithoutAdvancingWhilePaused(int schema)
    {
        using var seed = new PrivateWorldRuntime("legacy-settlement");
        seed.Pause();
        var legacy = seed.ExportState() with { SchemaVersion = schema };
        using var world = PrivateWorldRuntime.Restore(legacy);
        var loaded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, world.ExportState().SchemaVersion);
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(loaded, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        world.Resume();
        world.StageStarterContent();
        for (var tick = 0; tick < 10; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        var migrated = world.ExportState();
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, migrated.SchemaVersion);
        Assert.Contains(migrated.Map.Resources, resource => resource.Id == "settlement-seed");
        using var restored = PrivateWorldRuntime.Restore(migrated);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(migrated), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData("build:", "working", 0)]
    [InlineData("build:recipe:example", "working", 11)]
    public void InvalidProjectCheckpointFailsClosed(string candidate, string stage, int work)
    {
        using var world = new PrivateWorldRuntime("invalid-project");
        var state = world.ExportState();
        var person = state.Inhabitants[0];
        var invalid = state with
        {
            Inhabitants = state.Inhabitants.Select(item => item == person
                ? item with { Project = new SettlementProject(candidate, "Project", 0, stage, work) }
                : item).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalid));
    }

    [Fact]
    public async Task DefaultSettlementGathersDifferentInputsSharesAndCompletesVisibleProjects()
    {
        var provider = new ObservingProvider();
        using var world = new PrivateWorldRuntime("living-settlement", _ => provider);
        world.StageStarterContent();
        for (var tick = 0; tick < 1000; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        var state = world.ExportState();
        var gathered = state.Events.Where(item => item.Kind == "material_gathered")
            .Select(item => item.Detail.Split(':')[1]).ToHashSet(StringComparer.Ordinal);
        Assert.True(gathered.Count >= 3);
        Assert.Contains("stone", gathered);
        Assert.Contains("fiber", gathered);
        Assert.Contains(state.Events, item => item.Kind == "project_request_fulfilled");
        Assert.Contains(state.Events, item => item.Kind == "social_standing_changed");
        Assert.Contains(state.Inhabitants, person => person.SocialStanding?.Any(item => item.Trust >= 2) == true);
        Assert.Contains(state.Events, item => item.Kind == "project_progress" && item.Detail.Contains(":completed:", StringComparison.Ordinal));
        Assert.Contains(state.Events, item => item.Kind == "food_consumed");
        Assert.True(provider.MeaningfulProjectChoiceSeen);
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        Assert.NotEmpty(snapshot.Stockpiles);
        Assert.Contains(snapshot.Inhabitants, person => person.Project is not null);
        Assert.Contains(snapshot.Inhabitants, person => person.SocialNotes.Count > 0);
        Assert.Contains(snapshot.Inhabitants, person => person.SocialStanding.Count > 0);
        Assert.NotNull(snapshot.Council?.StewardName);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(state), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private sealed class ObservingProvider : IDecisionProvider
    {
        public bool MeaningfulProjectChoiceSeen { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            MeaningfulProjectChoiceSeen |= request.Observation.Candidates.Count(candidate => candidate.Id.StartsWith("build:", StringComparison.Ordinal)) > 1;
            return new DeterministicDecisionProvider().DecideAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task ProjectWorkSurvivesManualPauseAndRestartAndIsVisibleToOwner()
    {
        using var world = new PrivateWorldRuntime("settlement-project");
        world.StageStarterContent();
        for (var tick = 0; tick < 100 && !world.Inhabitants.Any(person => person.Project is { WorkDone: > 1 and < 9 }); tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        var worker = world.Inhabitants.First(person => person.Project is { WorkDone: > 1 and < 9 });
        var chosen = worker.Project!;
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        Assert.Equal(chosen.Label, snapshot.Inhabitants.Single(person => person.Id == worker.InhabitantId).Project!.Label);
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(chosen, restored.Inhabitants.Single(person => person.InhabitantId == worker.InhabitantId).Project);
        restored.Resume();
        for (var tick = 0; tick < 100; tick++)
        {
            await restored.AdvanceOneTickAsync();
        }
        Assert.Contains(restored.ExportState().Events, item => item.Kind == "project_progress" &&
            item.Detail == $"{worker.InhabitantId}:completed:{chosen.Label}");
    }

    [Fact]
    public async Task AChosenProjectAcquiresAndDeliversMissingWoodInsteadOfBeingHidden()
    {
        using var seed = new PrivateWorldRuntime("settlement-acquisition");
        var initial = seed.ExportState();
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Society = initial.Society with
            {
                Society = initial.Society.Society with
                {
                    Inventory = initial.Society.Society.Inventory with
                    {
                        Lots = initial.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != "wood").ToArray(),
                    },
                },
            },
        });
        world.StageStarterContent();
        for (var tick = 0; tick < 200; tick++)
        {
            await world.AdvanceOneTickAsync();
        }
        Assert.Contains(world.ExportState().Events, item => item.Kind == "project_chosen");
        Assert.Contains(world.ExportState().Events, item => item.Kind == "material_gathered" && item.Detail.Contains(":wood:", StringComparison.Ordinal));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "project_material_delivered");
        Assert.NotEmpty(world.WorldSimulation.Buildings);
    }
}
