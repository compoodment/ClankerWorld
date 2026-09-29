using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// Server-side projection of the Phase 2/3 composite runtime. It converts the
/// protected simulation records into stable viewer DTOs while retaining the
/// distinction between the live fixture topology, cognition state, and paused
/// authoring state.
/// </summary>
public sealed class OwnerWorldObservationStore
{
    private const int AgentKnowledgeArtifactLimit = 8;
    private static readonly string[] OwnerServerCapabilities =
    [
        "snapshot.read.v1",
        "event-replay.read.v1",
        "event-history-reset.read.v1",
        "reconnect-baseline.read.v1",
        "seeded-map.read.v1",
        "inhabitant-inspection.read.v1",
        "spatial-knowledge.read.v1",
        "owner-map-layer-delta.v1",
        "owner-device-pairing.v1",
        "owner-observation.read.v1",
        "owner-control.request.v1",
        "owner-provider-configuration.v1",
        "owner-inhabitant-provider-configuration.v1",
        "paused-authoring.request.v1",
        "content-governance.read.v1",
        "content-governance.write.v1",
    ];

    private static readonly string[] OwnerClientCapabilities =
    [
        "snapshot.read.v1",
        "event-replay.read.v1",
        "reconnect-baseline.read.v1",
        "owner-device-pairing.v1",
    ];

    private readonly OwnerWorldRuntime? ownerRuntime;
    private readonly PrivateWorldRuntime? privateRuntime;

    public OwnerWorldObservationStore(OwnerWorldRuntime runtime)
    {
        ownerRuntime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public OwnerWorldObservationStore(PrivateWorldRuntime runtime)
    {
        privateRuntime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public ViewerHandshake GetOwnerHandshake() => new(
        new ProtocolVersion(Major: 1, Minor: 1),
        privateRuntime is null ? OwnerServerCapabilities.ToArray() : [.. OwnerServerCapabilities, "owner-life-pace.v1", "owner-jev-assistance.v1", "owner-building-design.v1", "owner-terrain-delta.v1"],
        OwnerClientCapabilities.ToArray());

    public ViewerWorldSnapshot GetSnapshot() => privateRuntime is not null
        ? ToSnapshot(privateRuntime.ExportState())
        : ToSnapshot(ownerRuntime!.Capture(0).Snapshot);

    public ViewerEventSlice GetEventsAfter(long afterEventId)
    {
        if (privateRuntime is not null)
        {
            var state = privateRuntime.ExportState();
            return new ViewerEventSlice(
                state.Society.Society.WorldTick,
                afterEventId,
                state.Events
                    .Where(worldEvent => worldEvent.EventId > afterEventId)
                    .Select(ToEvent)
                    .ToArray(), state.EventHistoryFloor, afterEventId < state.EventHistoryFloor);
        }

        var capture = ownerRuntime!.Capture(afterEventId);
        return new ViewerEventSlice(
            capture.Snapshot.World.Identity.WorldTick,
            capture.AfterEventId,
            capture.Events.Select(ToEvent).ToArray());
    }

    public ViewerReconnectBaseline GetReconnectBaseline(long afterEventId,
        string? knownTerrainWorldId = null, string? knownTerrainDigest = null,
        string? knownMapLayersDigest = null)
    {
        if (privateRuntime is not null)
        {
            var state = privateRuntime.ExportState();
            var privateSnapshot = ToSnapshot(state, knownTerrainWorldId, knownTerrainDigest,
                knownMapLayersDigest);
            return new ViewerReconnectBaseline(
                privateSnapshot,
                new ViewerEventSlice(
                    privateSnapshot.WorldTick,
                    afterEventId,
                    state.Events
                        .Where(worldEvent => worldEvent.EventId > afterEventId)
                        .Select(ToEvent)
                        .ToArray(), state.EventHistoryFloor, afterEventId < state.EventHistoryFloor));
        }

        var capture = ownerRuntime!.Capture(afterEventId);
        var snapshot = ToSnapshot(capture.Snapshot);
        return new ViewerReconnectBaseline(
            snapshot,
            new ViewerEventSlice(
                snapshot.WorldTick,
                capture.AfterEventId,
                capture.Events.Select(ToEvent).ToArray()));
    }

    private static ViewerWorldSnapshot ToSnapshot(OwnerWorldSnapshot state)
    {
        var map = state.CurrentMap;
        var resourceStates = state.World.Resources.ToDictionary(resource => resource.Id, StringComparer.Ordinal);
        var actor = ToActor(state.World.Actor);
        return new ViewerWorldSnapshot(
            state.World.Identity.WorldId,
            state.World.Identity.WorldTick,
            state.CurrentMapManifestDigest,
            map.Tiles
                .OrderBy(tile => tile.Position.Y)
                .ThenBy(tile => tile.Position.X)
                .Select(tile => new ViewerTile(tile.Position.X, tile.Position.Y, ToWireValue(tile.Terrain)))
                .ToArray(),
            map.CampObjects
                .Where(mapObject => mapObject.Kind != "bedroll")
                .OrderBy(mapObject => mapObject.Id, StringComparer.Ordinal)
                .Select(mapObject => new ViewerMapObject(mapObject.Id, mapObject.Kind, ToPosition(mapObject.Position)))
                .ToArray(),
            map.Resources
                .OrderBy(resource => resource.Id, StringComparer.Ordinal)
                .Select(resource => new ViewerResource(
                    resource.Id,
                    resource.Kind,
                    ToPosition(resource.Position),
                    resource.IsRenewable,
                    resourceStates.TryGetValue(resource.Id, out var runtimeResource)
                        ? ToWireValue(runtimeResource.State)
                        : "available"))
                .ToArray(),
            actor,
            state.LatestGlobalEventId)
        {
            Inhabitants = CreateInhabitants(state),
            Authoring = new ViewerAuthoringState(
                state.IsPaused,
                state.RunEpoch,
                state.Revision,
                state.TopologyRevision,
                state.InitialMapManifestDigest,
                state.CurrentMapManifestDigest,
                state.Climate.Weather,
                state.Climate.Season,
                state.ApprovedAssetReferences
                    .OrderBy(reference => reference.AssetId, StringComparer.Ordinal)
                    .Select(reference => $"{reference.AssetId}@{reference.AssetDigest}")
                    .ToArray()),
            Instructions = state.Instructions
                .OrderBy(instruction => instruction.SubmissionSequence)
                .Select(instruction => new ViewerInstruction(
                    instruction.InstructionId,
                    instruction.TargetInhabitantId,
                    ToWireValue(instruction.Kind),
                    instruction.Text,
                    ToWireValue(instruction.State),
                    instruction.SubmittedTick,
                    instruction.RunEpoch,
                    instruction.SubmissionSequence))
                .ToArray(),
            Cognition = state.Cognition is null
                ? null
                : new ViewerCognition(
                    state.Cognition.ProviderKind.ToString().ToLowerInvariant(),
                    state.Cognition.IsPaused,
                    state.Cognition.InFlightRequestId,
                    state.Cognition.CurrentIntention?.CandidateId,
                    state.Cognition.CurrentIntention?.Provider.ToString().ToLowerInvariant(),
                    state.Cognition.Events
                        .OrderBy(worldEvent => worldEvent.EventId)
                        .TakeLast(12)
                        .Select(worldEvent => new ViewerCognitionEvent(
                            worldEvent.EventId,
                            worldEvent.WorldTick,
                            worldEvent.Kind,
                            worldEvent.Detail))
                        .ToArray()),
        };
    }

    private static ViewerWorldSnapshot ToSnapshot(PrivateWorldRuntimeState state,
        string? knownTerrainWorldId = null, string? knownTerrainDigest = null,
        string? knownMapLayersDigest = null)
    {
        var map = state.Map;
        var ecology = state.WorldSystems?.Ecology.Resources.ToDictionary(resource => resource.Id, StringComparer.Ordinal);
        var buildingDefinitions = state.WorldContent?.Buildings.ToDictionary(building => building.CanonicalId, StringComparer.Ordinal);
        var activeInhabitants = state.Society.Society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var physicalById = state.Inhabitants.ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
        var deceasedById = (state.DeceasedInhabitants ?? []).ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
        var resourceStates = state.Resources.ToDictionary(item => item.ResourceId, item => item.State, StringComparer.Ordinal);
        var first = activeInhabitants.FirstOrDefault();
        ViewerActor? actor = null;
        if (first is not null)
        {
            var physical = physicalById[first.Id];
            var inventory = InventoryFor(state, first.Id);
            actor = new ViewerActor(first.Id, ToPosition(physical.Position), physical.HungerBasisPoints,
                inventory.Where(item => item.Kind == "food").Sum(item => item.Quantity),
                inventory.Where(item => item.Kind == "wood").Sum(item => item.Quantity));
        }
        var jobs = state.WorldSimulation?.ProductionJobs.Concat(state.WorldSimulation.CropBuilds ?? []).ToArray() ?? [];
        var latestEventId = state.Events.Count == 0 ? 0 : state.Events[^1].EventId;
        var terrainUnchanged = state.Geography is not null &&
            string.Equals(knownTerrainWorldId, state.Society.Society.WorldId, StringComparison.Ordinal) &&
            string.Equals(knownTerrainDigest, map.ManifestDigest, StringComparison.Ordinal);
        var mapLayersDigest = state.Geography is null ? null : MapLayerManifestCodec.Digest(map);
        var mapLayersUnchanged = terrainUnchanged && mapLayersDigest is not null &&
            string.Equals(knownMapLayersDigest, mapLayersDigest, StringComparison.Ordinal);
        var packedTerrain = state.Geography is null || terrainUnchanged ? null : PackTerrain(map);
        var weatherAnchor = map.CampObjects.FirstOrDefault(item => item.Kind == "cooking")?.Position ??
            state.Towns?.FirstOrDefault(item => item.OriginSite is not null)?.OriginSite ??
            map.Resources.First(item => item.Id == "berry-patch").Position;
        var campWeather = state.WorldSystems is { } currentSystems
            ? WeatherRules.At(currentSystems,
                weatherAnchor, map.Height)
            : WeatherKind.Clear;
        return new ViewerWorldSnapshot(
            state.Society.Society.WorldId,
            state.Society.Society.WorldTick,
            map.ManifestDigest,
            (state.Geography is null ? map.Tiles : [])
                .OrderBy(tile => tile.Position.Y)
                .ThenBy(tile => tile.Position.X)
                .Select(tile => new ViewerTile(tile.Position.X, tile.Position.Y, ToWireValue(tile.Terrain)))
                .ToArray(),
            map.CampObjects
                .Where(mapObject => mapObject.Kind != "bedroll")
                .OrderBy(mapObject => mapObject.Id, StringComparer.Ordinal)
                .Select(mapObject => new ViewerMapObject(mapObject.Id, mapObject.Kind, ToPosition(mapObject.Position)))
                .ToArray(),
            map.Resources
                .OrderBy(resource => resource.Id, StringComparer.Ordinal)
                .Select(resource => new ViewerResource(
                    resource.Id,
                    resource.Kind,
                    ToPosition(resource.Position),
                    resource.IsRenewable,
                    resourceStates.TryGetValue(resource.Id, out var resourceState)
                        ? ToWireValue(resourceState)
                        : "available",
                    ecology?.GetValueOrDefault(resource.Id)?.Quantity,
                    ecology?.GetValueOrDefault(resource.Id)?.Capacity,
                    ecology?.GetValueOrDefault(resource.Id)?.RegenerationAmount,
                    ecology?.GetValueOrDefault(resource.Id)?.RegenerationIntervalDays,
                    ecology?.GetValueOrDefault(resource.Id)?.RegenerationSeason.ToString().ToLowerInvariant(),
                    resource.TreeKind,
                    ecology?.GetValueOrDefault(resource.Id)?.IsPlanted ?? false,
                    TreeStageFor(resource.TreeKind, ecology?.GetValueOrDefault(resource.Id)),
                    resource.NaturalObjectKind))
                .ToArray(),
            actor,
            latestEventId)
        {
            PackedTerrain = packedTerrain,
            PackedMapLayers = state.Geography is null || mapLayersUnchanged ? null : PackMapLayers(map),
            MapLayersDigest = mapLayersDigest,
            WrapsEastWest = state.Geography?.WrapEastWest == true,
            Inhabitants = activeInhabitants
                .Select(inhabitant => ToPlaytestInhabitant(state, inhabitant, physicalById[inhabitant.Id]))
                .Concat(state.Society.Society.Inhabitants
                    .Where(inhabitant => inhabitant.Status == SocietyInhabitantStatus.Dead && deceasedById.ContainsKey(inhabitant.Id))
                    .Select(inhabitant => ToDeceasedInhabitant(state, inhabitant, deceasedById[inhabitant.Id])))
                .OrderBy(inhabitant => inhabitant.Id, StringComparer.Ordinal)
                .ToArray(),
            Stockpiles = state.Society.Society.Households.Select(household =>
                new ViewerStockpile(household.Id, household.Name, InventoryFor(state, household.Id))).ToArray(),
            Council = state.Council is { } council ? new ViewerCouncil(
                state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == council.StewardId)?.Name,
                council.FoodPolicy, council.Ballot?.Policy, council.Ballot?.Approvals.Count ?? 0,
                council.Ballot?.Rejections.Count ?? 0, council.Ballot?.Electorate.Count ?? 0) : null,
            LifePaceRate = state.Society.Society.LifeClock?.Rate ?? 1,
            JevEnabled = state.JevEnabled ?? true,
            FounderSetup = state.FounderSetup is { } setup
                ? new ViewerFounderSetup(PrivateWorldRuntime.RequiredFounders, setup.FounderIds.Count, setup.Started)
                {
                    CanChooseTownSite = state.Geography is not null && !setup.Started && setup.FounderIds.Count == 0,
                    HasAcceptedTownSite = (state.Towns ?? []).Any(town => town.OriginSite is not null),
                    LastFounderId = !setup.Started && setup.FounderIds.Count > 0
                        ? setup.FounderIds[^1] : null,
                }
                : null,
            Towns = (state.Towns ?? []).OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new ViewerTown(item.Id, item.Name, item.FoundingState, item.FoundedTick,
                    item.ResidentIds.ToArray(), item.AssignedBuildingIds.ToArray(),
                    item.BorderTiles.OrderBy(point => point.Y).ThenBy(point => point.X)
                        .Select(ToPosition).ToArray()))
                .ToArray(),
            RoadTiles = (state.RoadTiles ?? []).OrderBy(point => point.Y).ThenBy(point => point.X)
                .Select(ToPosition).ToArray(),
            WeatherRegions = state.WorldSystems is { } weatherSystems
                ? CreateWeatherRegions(weatherSystems, map)
                : [],
            WeatherRegionSize = WeatherRules.RegionSize,
            CalendarPace = state.WorldSystems is { } worldSystems
                ? new ViewerCalendarPace(worldSystems.Config.TicksPerDay, worldSystems.Config.DaysPerYear)
                : null,
            Authoring = new ViewerAuthoringState(
                state.Society.Society.IsPaused,
                state.Society.Society.RunEpoch,
                state.EventHistoryFloor + state.Events.Count,
                0,
                map.ManifestDigest,
                map.ManifestDigest,
                campWeather.ToString().ToLowerInvariant(),
                state.WorldSystems?.Climate.Season.ToString().ToLowerInvariant() ?? "spring",
                []),
            Instructions = (state.Instructions ?? [])
                .Where(instruction => !(state.CompletedInstructionIds ?? []).Contains(instruction.InstructionId, StringComparer.Ordinal))
                .OrderBy(instruction => instruction.SubmissionSequence)
                .Select(instruction => new ViewerInstruction(
                    instruction.InstructionId,
                    instruction.TargetInhabitantId,
                    ToWireValue(instruction.Kind),
                    instruction.Text,
                    ToWireValue(instruction.State),
                    instruction.SubmittedTick,
                    instruction.RunEpoch,
                    instruction.SubmissionSequence))
                .ToArray(),
            Cognition = ToCognition(state),
            ContentPackages = state.Content?.Packages
                .OrderBy(package => package.Manifest.PackageId, StringComparer.Ordinal)
                .Select(package => new ViewerContentPackage(
                    package.Manifest.PackageId,
                    package.Manifest.Version.ToString(),
                    package.Manifest.PackageDigest,
                    package.Lifecycle.ToString().ToLowerInvariant(),
                    package.LockDigest,
                    package.ValidationTick,
                    package.StagedTick,
                    package.ActivationTick,
                    package.ManifestDigest,
                    package.Manifest.Definitions.Count == 0 ? null : package.Manifest.Definitions[0].DisplayName,
                    state.Content.Events.LastOrDefault(item => item.PackageId == package.Manifest.PackageId &&
                        item.Kind == "package_proposed_by_inhabitant")?.Detail))
                .ToArray() ?? [],
            ContentEvents = state.Content?.Events
                .OrderBy(item => item.EventId)
                .Select(item => new ViewerContentGovernanceEvent(
                    item.EventId,
                    item.WorldTick,
                    item.PackageId,
                    item.Kind,
                    item.Detail))
                .ToArray() ?? [],
            WorldSystems = state.WorldSystems is { } systems
                ? new ViewerWorldSystemsSummary(
                    systems.Climate.Season.ToString().ToLowerInvariant(),
                    campWeather.ToString().ToLowerInvariant(),
                    systems.Ecology.Resources.Count,
                    systems.Factions.Factions.Count,
                    systems.Currency.Accounts.Count,
                    systems.Culture.Cultures.Count,
                    systems.Chunks.Count,
                    state.WorldContent?.Buildings.Count ?? 0,
                    state.WorldContent?.Recipes.Count ?? 0,
                    state.WorldSimulation?.Buildings.Count ?? 0,
                    jobs.Length,
                    state.AssetReservations?.Reservations
                        .Select(item => item.NormalizedDigest)
                        .Distinct(StringComparer.Ordinal)
                        .Count() ?? 0,
                    state.AssetReservations is { } assetState
                        ? assetState.Reservations
                            .GroupBy(item => item.NormalizedDigest, StringComparer.Ordinal)
                            .Sum(group => group.First().DurableStorageBytes)
                        : 0,
                    state.AssetReservations is { } cacheState
                        ? cacheState.Reservations
                            .GroupBy(item => $"{item.NormalizedDigest}|{item.DecodeProfile}", StringComparer.Ordinal)
                            .Sum(group => group.First().DecodedCacheBytes)
                        : 0,
                    state.AssetReservations is { } gpuState
                        ? gpuState.Reservations
                            .GroupBy(item => $"{item.NormalizedDigest}|{item.DecodeProfile}", StringComparer.Ordinal)
                            .Sum(group => group.First().GpuBytes)
                        : 0,
                    state.AssetReservations?.Reservations.Sum(item => item.RenderUnits) ?? 0)
                : null,
            PlacedBuildings = state.WorldSimulation?.Buildings
                .OrderBy(item => item.InstanceId, StringComparer.Ordinal)
                .Select(item => new ViewerPlacedBuilding(
                    item.InstanceId,
                    item.DefinitionId,
                    ToPosition(item.Position),
                    item.PlacedTick,
                    buildingDefinitions?.GetValueOrDefault(item.DefinitionId)?.DisplayName,
                    buildingDefinitions?.GetValueOrDefault(item.DefinitionId)?.Tags,
                    buildingDefinitions?.GetValueOrDefault(item.DefinitionId)?.Width ?? 1,
                    buildingDefinitions?.GetValueOrDefault(item.DefinitionId)?.Height ?? 1,
                    item.TownId,
                    item.HouseholdId,
                    item.HouseholdId is { } householdId
                        ? InventoryFor(state, householdId, item.InstanceId)
                        : item.TownId is { } townId && buildingDefinitions?.GetValueOrDefault(item.DefinitionId)?
                            .Tags.Contains("warehouse", StringComparer.Ordinal) == true
                            ? InventoryFor(state, townId, item.InstanceId) : null))
                .ToArray() ?? [],
            ProductionJobs = jobs
                .OrderBy(item => item.JobId, StringComparer.Ordinal)
                .Select(item => new ViewerProductionJob(
                    item.JobId,
                    item.RecipeId,
                    item.BuildingInstanceId,
                    item.WorkerId,
                    item.StartedTick,
                    item.CompletionTick,
                    item.State.ToString().ToLowerInvariant()))
                .ToArray(),
        };
    }

    internal static ViewerPackedTerrain PackTerrain(SeededMap map)
    {
        var bytes = new byte[checked(map.Width * map.Height)];
        foreach (var tile in map.Tiles)
            bytes[tile.Position.Y * map.Width + tile.Position.X] = checked((byte)tile.Terrain);
        return new ViewerPackedTerrain(map.Width, map.Height, "terrain-kind-v1",
            Convert.ToBase64String(bytes));
    }

    internal static ViewerPackedMapLayers? PackMapLayers(SeededMap map)
    {
        if (map.ClimateZones is not { } climate || map.ElevationLevels is not { } elevation ||
            map.HydrologyKinds is not { } hydrology || map.SurfaceKinds is not { } surface ||
            map.VegetationKinds is not { } vegetation) return null;
        return new ViewerPackedMapLayers(map.Width, map.Height, "map-layers-v2",
            Convert.ToBase64String(climate), Convert.ToBase64String(elevation),
            Convert.ToBase64String(hydrology), Convert.ToBase64String(surface),
            Convert.ToBase64String(vegetation));
    }

    private static ViewerWeatherRegion[] CreateWeatherRegions(WorldSystemsState systems, SeededMap map)
    {
        if (map.Height <= WeatherRules.RegionSize)
            return [new ViewerWeatherRegion(0, 0,
                WeatherRules.At(systems, new GridPoint(0, 0), map.Height).ToString().ToLowerInvariant(),
                WeatherRules.SoilMoistureAt(systems, new GridPoint(0, 0), map.Height))];
        var columns = (map.Width + WeatherRules.RegionSize - 1) / WeatherRules.RegionSize;
        var rows = (map.Height + WeatherRules.RegionSize - 1) / WeatherRules.RegionSize;
        return Enumerable.Range(0, rows)
            .SelectMany(y => Enumerable.Range(0, columns).Select(x => new ViewerWeatherRegion(x, y,
                WeatherRules.At(systems, new GridPoint(x * WeatherRules.RegionSize,
                        y * WeatherRules.RegionSize), map.Height,
                    WeatherRules.RegionClimate(map, new GridPoint(x * WeatherRules.RegionSize,
                        y * WeatherRules.RegionSize))).ToString().ToLowerInvariant(),
                WeatherRules.SoilMoistureAt(systems,
                    new GridPoint(x * WeatherRules.RegionSize, y * WeatherRules.RegionSize), map.Height,
                    WeatherRules.RegionClimate(map, new GridPoint(x * WeatherRules.RegionSize,
                        y * WeatherRules.RegionSize))))))
            .ToArray();
    }

    private static List<ViewerInhabitant> CreateInhabitants(OwnerWorldSnapshot state)
    {
        var inhabitants = new List<ViewerInhabitant>
        {
            ToProtectedActor(state),
        };
        inhabitants.AddRange(state.FounderDrafts
            .OrderBy(draft => draft.Id, StringComparer.Ordinal)
            .Select(ToFounderDraft));
        return inhabitants;
    }

    private static ViewerInhabitant ToProtectedActor(OwnerWorldSnapshot state)
    {
        var world = state.World;
        var actor = world.Actor;
        var route = DetermineFixtureRoute(world);
        var perceived = KnownNearby(world.Map, actor.Position).ToArray();
        var known = KnownFixtureTopology(actor.Position, perceived, route);
        var cognition = state.Cognition;
        var decisionFactors = new List<ViewerDecisionFactor>
        {
            new(
                "decision-source",
                cognition is null
                    ? "deterministic fixture"
                    : $"{cognition.ProviderKind.ToString().ToLowerInvariant()} provider"),
            new("hunger", $"{actor.HungerBasisPoints} basis points"),
            new("fixture-topology", world.Map.ManifestDigest),
        };
        if (cognition?.CurrentIntention is { } intention)
        {
            decisionFactors.Add(new ViewerDecisionFactor("current-intention", intention.CandidateId));
            decisionFactors.Add(new ViewerDecisionFactor("intention-provider", intention.Provider.ToString().ToLowerInvariant()));
        }

        return new ViewerInhabitant(
            actor.Id,
            "Scout",
            "active_fixture",
            ToPosition(actor.Position),
            actor.HungerBasisPoints,
            [
                new ViewerInventoryEntry("food", actor.FoodItems),
                new ViewerInventoryEntry("wood", actor.WoodItems),
            ],
            decisionFactors,
            route,
            new ViewerSpatialKnowledge(ToPosition(actor.Position), perceived, known),
            IsDraft: false)
        {
            PublicIntention = cognition?.CurrentIntention is { } publicIntention
                ? ToPublicIntention(publicIntention.CandidateId, publicIntention.Provider.ToString().ToLowerInvariant(), publicIntention.WorldTick)
                : null,
        };
    }

    private static ViewerInhabitant ToFounderDraft(OwnerFounderDraft draft) => new(
        draft.Id,
        draft.DisplayName,
        "authoring_draft",
        ToPosition(draft.Position),
        0,
        [],
        [
            new ViewerDecisionFactor("status", "paused authoring draft; not active in the protected fixture"),
            new ViewerDecisionFactor("created-revision", draft.CreatedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ],
        new ViewerRoute("not_active", null, null, [], string.Empty),
        new ViewerSpatialKnowledge(ToPosition(draft.Position), [ToPosition(draft.Position)], [ToPosition(draft.Position)]),
        IsDraft: true);

    private static ViewerInhabitant ToPlaytestInhabitant(
        PrivateWorldRuntimeState state,
        SocietyInhabitant inhabitant,
        PlaytestInhabitantState physical)
    {
        var inventory = InventoryFor(state, inhabitant.Id);
        var route = DeterminePlaytestRoute(state, physical, inventory);
        var perceived = KnownNearby(state.Map, physical.Position).ToArray();
        var known = KnownFixtureTopology(physical.Position, perceived, route);
        var household = state.Society.Society.Households
            .FirstOrDefault(item => item.Id == inhabitant.HouseholdId);
        var decisionFactors = new List<ViewerDecisionFactor>
        {
            new("personality", physical.Personality),
            new("aspiration", physical.Aspiration),
            new("age-band", inhabitant.AgeBand.ToString().ToLowerInvariant()),
            new(state.Society.Society.Config.DayLifecycle is null ? "age-years" : "age-days",
                state.Society.Society.AgeAt(inhabitant, state.Society.Society.WorldTick).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("role", inhabitant.CurrentRole.ToString().ToLowerInvariant()),
            new("household", household?.Name ?? "unhoused"),
            new("hunger", $"{physical.HungerBasisPoints} basis points"),
        };
        var runtime = state.Society.Cognition.Runtimes
            .FirstOrDefault(item => item.InhabitantId == inhabitant.Id);
        if (state.Society.Cognition.Queue.Any(item => item.InhabitantId == inhabitant.Id))
            decisionFactors.Add(new ViewerDecisionFactor("decision-pending", "true"));
        if (runtime?.CurrentIntention is { } intention)
        {
            decisionFactors.Add(new ViewerDecisionFactor("current-intention", intention.CandidateId));
            decisionFactors.Add(new ViewerDecisionFactor("intention-provider", intention.Provider.ToString().ToLowerInvariant()));
        }

        return new ViewerInhabitant(
            inhabitant.Id,
            inhabitant.Name,
            inhabitant.Status.ToString().ToLowerInvariant(),
            ToPosition(physical.Position),
            physical.HungerBasisPoints,
            inventory,
            decisionFactors,
            route,
            new ViewerSpatialKnowledge(ToPosition(physical.Position), perceived, known),
            IsDraft: false)
        {
            PublicIntention = runtime?.CurrentIntention is { } publicIntention
                ? ToPublicIntention(publicIntention.CandidateId, publicIntention.Provider.ToString().ToLowerInvariant(), publicIntention.WorldTick)
                : null,
            Relationships = RelationshipsFor(state, inhabitant.Id),
            RecentPrivateThoughts = (physical.RecentThoughts ?? [])
                .Select(thought => new ViewerPrivateThought(thought.WorldTick, thought.Text)).ToArray(),
            RecentMemories = MemoriesFor(state, inhabitant.Id),
            RecentBeliefs = BeliefsFor(state, inhabitant.Id),
            RecentKnowledgeFacts = KnowledgeFactsFor(state, inhabitant.Id),
            KnowledgeArtifacts = KnowledgeArtifactsFor(state, inhabitant.Id),
            Project = physical.Project is { } project
                ? new ViewerProject(project.Label, project.Stage, project.WorkDone, 10, project.Blocker, project.StartedTick)
                : null,
            Survival = physical.Survival is { } survival
                ? new ViewerSurvival(survival.WarmthBasisPoints, survival.IllnessBasisPoints,
                    inventory.Any(item => item.Kind == "clothing" && item.Quantity > 0),
                    inventory.Any(item => item.Kind == "tool" && item.Quantity > 0), survival.NutritionBasisPoints, survival.LastMealKind) : null,
            Lesson = physical.Lesson is { } lesson ? new ViewerLesson(
                state.Society.Society.GetInhabitant(lesson.TeacherId).Name, lesson.Role.ToString().ToLowerInvariant(),
                lesson.Stage, lesson.Progress, 20) : null,
            Proficiency = physical.Proficiency is { } practice
                ? new ViewerProficiency(practice.Building, practice.Farming, practice.Crafting) : null,
            SocialStanding = SocialStandingFor(state, inhabitant.Id, physical),
            SocialNotes = state.Society.Society.Inventory.Offers.Where(offer => offer.State == DirectBarterState.Open &&
                    (offer.FirstPartyId == inhabitant.Id || offer.SecondPartyId == inhabitant.Id))
                .Select(offer => offer.AcceptedBy.Contains(inhabitant.Id, StringComparer.Ordinal)
                    ? "Waiting for the other inhabitant to accept or decline an exchange."
                    : "An exchange is offered; acceptance or refusal is still undecided.")
                .Concat(state.Inhabitants.Where(person => person.Parenthood is { } plan &&
                    (person.InhabitantId == inhabitant.Id || plan.PartnerId == inhabitant.Id)).Select(person =>
                    person.Parenthood!.Stage == "preparing" ? "Preparing for parenthood; food, shelter and both parents' consent are still required."
                    : person.Parenthood.Stage == "requested" ? "Parenthood proposed; waiting for a separate decision."
                    : person.Parenthood.Stage == "completed" ? "Caring for a child in the household." : "Parenthood plan withdrawn."))
                .Concat(inhabitant.AgeBand is SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent &&
                    !state.Society.Society.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                        edge.State == SocietyRelationshipState.Accepted && edge.TargetId == inhabitant.Id &&
                        state.Society.Society.GetInhabitant(edge.ProposerId).Status == SocietyInhabitantStatus.Active)
                    ? ["No active caregiver; household adults may offer support."] : Array.Empty<string>())
                .ToArray(),
        };
    }

    private static ViewerInhabitant ToDeceasedInhabitant(
        PrivateWorldRuntimeState state,
        SocietyInhabitant inhabitant,
        PlaytestDeceasedInhabitantState archived)
    {
        var lastPhysical = archived.LastPhysical;
        var position = ToPosition(lastPhysical.Position);
        var estate = state.Society.Society.Estates.FirstOrDefault(item => item.DeceasedId == inhabitant.Id);
        return new ViewerInhabitant(
            inhabitant.Id,
            inhabitant.Name,
            "dead",
            position,
            lastPhysical.HungerBasisPoints,
            [],
            [
                new("personality", lastPhysical.Personality),
                new("aspiration", lastPhysical.Aspiration),
                new("age-band", inhabitant.AgeBand.ToString().ToLowerInvariant()),
                new(state.Society.Society.Config.DayLifecycle is null ? "age-years" : "age-days",
                    archived.AgeAtDeath.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("role", inhabitant.CurrentRole.ToString().ToLowerInvariant()),
                new("death-tick", archived.DeathTick.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("death-cause", inhabitant.DeathCause?.ToString().ToLowerInvariant() ?? "unknown"),
                new("will-status", estate?.WillStatus ?? "not_requested"),
                new("will-heir", estate?.WillBeneficiaryId is { } heirId
                    ? state.Society.Society.Inhabitants.FirstOrDefault(item => item.Id == heirId)?.Name ?? heirId
                    : ""),
            ],
            new ViewerRoute("deceased", null, null, [], string.Empty),
            new ViewerSpatialKnowledge(position, [position], [position]),
            IsDraft: false)
        {
            Relationships = RelationshipsFor(state, inhabitant.Id),
            RecentPrivateThoughts = (lastPhysical.RecentThoughts ?? [])
                .Select(thought => new ViewerPrivateThought(thought.WorldTick, thought.Text)).ToArray(),
            RecentMemories = MemoriesFor(state, inhabitant.Id),
            RecentBeliefs = BeliefsFor(state, inhabitant.Id),
            RecentKnowledgeFacts = KnowledgeFactsFor(state, inhabitant.Id),
            KnowledgeArtifacts = KnowledgeArtifactsFor(state, inhabitant.Id),
            Proficiency = lastPhysical.Proficiency is { } practice
                ? new ViewerProficiency(practice.Building, practice.Farming, practice.Crafting) : null,
            SocialStanding = SocialStandingFor(state, inhabitant.Id, lastPhysical),
        };
    }

    private static ViewerAgentMemory[] MemoriesFor(PrivateWorldRuntimeState state, string ownerId) =>
        state.Society.Society.Memories
            .Where(memory => memory.OwnerId == ownerId && memory.TombstonedTick is null)
            .OrderByDescending(memory => memory.SourceTick)
            .ThenBy(memory => memory.Id, StringComparer.Ordinal)
            .Take(16)
            .Select(memory => new ViewerAgentMemory(
                memory.SourceTick,
                memory.SubjectId,
                state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == memory.SubjectId)?.Name ?? memory.SubjectId,
                memory.Summary,
                memory.Visibility))
            .ToArray();

    private static ViewerAgentBelief[] BeliefsFor(PrivateWorldRuntimeState state, string ownerId) =>
        (state.Society.Society.Beliefs ?? [])
            .Where(belief => belief.OwnerId == ownerId)
            .OrderByDescending(belief => belief.FormedTick)
            .ThenBy(belief => belief.Id, StringComparer.Ordinal)
            .Take(16)
            .Select(belief => new ViewerAgentBelief(
                belief.FormedTick,
                belief.Statement,
                belief.Provenance.ToString().ToLowerInvariant(),
                belief.ConfidenceBasisPoints,
                belief.SourceAgentId,
                belief.SourceAgentId is { } sourceId
                    ? state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == sourceId)?.Name ?? sourceId
                    : null,
                belief.SourceEventId,
                belief.AboutInhabitantId,
                belief.SupersededByBeliefId is not null,
                belief.SupersededTick))
            .ToArray();

    private static ViewerAgentKnowledgeFact[] KnowledgeFactsFor(PrivateWorldRuntimeState state, string ownerId)
    {
        var names = state.Society.Society.Inhabitants.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        return (state.Knowledge?.Facts ?? []).Where(fact => fact.OwnerId == ownerId)
            .OrderByDescending(fact => fact.LearnedTick)
            .ThenBy(fact => fact.Id, StringComparer.Ordinal)
            .Take(16)
            .Select(fact => new ViewerAgentKnowledgeFact(
                fact.LearnedTick,
                fact.Position.X,
                fact.Position.Y,
                fact.Terrain,
                fact.ResourceKinds,
                names.GetValueOrDefault(fact.DiscovererId, fact.DiscovererId),
                fact.Acquisition,
                fact.SourceAgentId is { } sourceId ? names.GetValueOrDefault(sourceId, sourceId) : null))
            .ToArray();
    }

    private static ViewerAgentKnowledgeArtifact[] KnowledgeArtifactsFor(PrivateWorldRuntimeState state, string ownerId)
    {
        var names = state.Society.Society.Inhabitants.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        var heldLotIds = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == ownerId)
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        return (state.Knowledge?.Artifacts ?? []).Where(artifact => heldLotIds.Contains(artifact.LotId))
            .OrderByDescending(artifact => artifact.CreatedTick)
            .ThenBy(artifact => artifact.Id, StringComparer.Ordinal)
            .Take(AgentKnowledgeArtifactLimit)
            .Select(artifact => new ViewerAgentKnowledgeArtifact(
                artifact.Id,
                artifact.Kind,
                artifact.Title,
                artifact.CreatedTick,
                names.GetValueOrDefault(artifact.CreatorId, artifact.CreatorId),
                artifact.Facts.Select(fact => new ViewerKnowledgeSite(
                    fact.Position.X,
                    fact.Position.Y,
                    fact.Terrain,
                    fact.ResourceKinds,
                    names.GetValueOrDefault(fact.DiscovererId, fact.DiscovererId))).ToArray()))
            .ToArray();
    }

    private static ViewerSocialStanding[] SocialStandingFor(
        PrivateWorldRuntimeState state,
        string ownerId,
        PlaytestInhabitantState physical)
    {
        var saved = (physical.SocialStanding ?? []).ToDictionary(item => item.SubjectId, item => item.Trust, StringComparer.Ordinal);
        return state.Society.Society.Inhabitants.Where(subject => subject.Id != ownerId)
            .Select(subject => new ViewerSocialStanding(subject.Id, subject.Name,
                saved.GetValueOrDefault(subject.Id, LegacyTrustScore(state, ownerId, subject.Id))))
            .Where(item => item.Trust > 0)
            .OrderByDescending(item => item.Trust).ThenBy(item => item.SubjectId, StringComparer.Ordinal).ToArray();
    }

    private static int LegacyTrustScore(PrivateWorldRuntimeState state, string ownerId, string subjectId) =>
        Math.Min(10, state.Society.Society.Memories.Where(memory => memory.OwnerId == ownerId &&
                memory.SubjectId == subjectId && memory.TombstonedTick is null)
            .Sum(memory => memory.Id.StartsWith("project-gratitude:", StringComparison.Ordinal) ? 2
                : memory.Id.StartsWith("lesson-gratitude:", StringComparison.Ordinal) ? 2
                : memory.Id.StartsWith("settlement-trust:", StringComparison.Ordinal) ? 1 : 0));

    private static ViewerInhabitantRelationship[] RelationshipsFor(
        PrivateWorldRuntimeState state,
        string inhabitantId) => state.Society.Society.Relationships
        .Where(relationship =>
            (relationship.ProposerId == inhabitantId || relationship.TargetId == inhabitantId) &&
            relationship.State is SocietyRelationshipState.Proposed or SocietyRelationshipState.Accepted or SocietyRelationshipState.EndedByDeath)
        .OrderBy(relationship => relationship.Type)
        .ThenBy(relationship => relationship.Id, StringComparer.Ordinal)
        .Select(relationship => new ViewerInhabitantRelationship(
            relationship.Id,
            relationship.ProposerId == inhabitantId ? relationship.TargetId : relationship.ProposerId,
            ToWireValue(relationship.Type),
            ToWireValue(relationship.State),
            relationship.PrivacyClass,
            relationship.EffectiveTick,
            relationship.Type == SocietyRelationshipType.BiologicalParentage
                ? relationship.ProposerId == inhabitantId ? "parent" : "child"
                : relationship.Type == SocietyRelationshipType.Partnership ? "partner" : null))
        .ToArray();

    private static ViewerPublicIntention ToPublicIntention(
        string candidateId,
        string provider,
        long worldTick) => new(
        candidateId,
        PublicIntentionSummary(candidateId),
        provider,
        worldTick);

    private static string PublicIntentionSummary(string candidateId) => candidateId switch
    {
        "seek_food" => "looking for food",
        "harvest_food" => "gathering food",
        "consume_food" => "eating carried food",
        "safe_idle" => "keeping a safe routine",
        _ => candidateId.Replace('_', ' '),
    };

    private static string ToWireValue(SocietyRelationshipType type) => type switch
    {
        SocietyRelationshipType.Partnership => "partnership",
        SocietyRelationshipType.Caregiver => "caregiver",
        SocietyRelationshipType.HouseholdMembership => "household_membership",
        SocietyRelationshipType.BiologicalParentage => "biological_parentage",
        SocietyRelationshipType.LegalGuardian => "legal_guardian",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string ToWireValue(SocietyRelationshipState state) => state switch
    {
        SocietyRelationshipState.Proposed => "proposed",
        SocietyRelationshipState.Accepted => "accepted",
        SocietyRelationshipState.Rejected => "rejected",
        SocietyRelationshipState.Revoked => "revoked",
        SocietyRelationshipState.Dissolved => "dissolved",
        SocietyRelationshipState.EndedByDeath => "ended_by_death",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static ViewerInventoryEntry[] InventoryFor(
        PrivateWorldRuntimeState state,
        string ownerId,
        string? storageBuildingId = null) => state.Society.Society.Inventory.Lots
        .Where(lot => lot.OwnerId == ownerId && lot.Quantity > 0 &&
            (storageBuildingId is null || lot.StorageBuildingId == storageBuildingId))
        .GroupBy(lot => lot.ItemKind, StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => new ViewerInventoryEntry(group.Key, group.Sum(lot => lot.Quantity)))
        .ToArray();

    private static ViewerRoute DeterminePlaytestRoute(
        PrivateWorldRuntimeState state,
        PlaytestInhabitantState physical,
        IReadOnlyList<ViewerInventoryEntry> inventory)
    {
        var food = inventory.FirstOrDefault(item => item.Kind == "food");
        if (food is { Quantity: > 0 } && physical.HungerBasisPoints < 8_500)
        {
            return new ViewerRoute("consume", null, null, [], state.Map.ManifestDigest);
        }

        var berry = state.Map.GetResource("berry-patch");
        var berryState = state.Resources.FirstOrDefault(item => item.ResourceId == berry.Id)?.State;
        if (berryState == ResourceState.Available && IsWithinInteractionRange(physical.Position, berry.Position))
        {
            return new ViewerRoute("harvest", berry.Id, ToPosition(berry.Position), [], state.Map.ManifestDigest);
        }

        if (berryState == ResourceState.Available && physical.HungerBasisPoints < 7_000)
        {
            // The starter patch is camp-reachable. Reject an agent on a
            // separate island using the map's cached camp component instead
            // of exhaustively searching the entire world on every reconnect.
            if ((state.Map.IsReachableFromCampOnFoot(berry.Position) &&
                 !state.Map.IsReachableFromCampOnFoot(physical.Position)) ||
                !DeterministicRouteFinder.TryFind(state.Map, physical.Position, berry.Position,
                    out var path))
                return new ViewerRoute("food_unreachable", null, null, [], state.Map.ManifestDigest);
            return new ViewerRoute("seek_food", berry.Id, ToPosition(berry.Position),
                path.Skip(1).Select(ToPosition).ToArray(), state.Map.ManifestDigest);
        }

        return new ViewerRoute("idle", null, null, [], state.Map.ManifestDigest);
    }

    private static bool IsWithinInteractionRange(GridPoint origin, GridPoint destination) =>
        Math.Abs(origin.X - destination.X) + Math.Abs(origin.Y - destination.Y) <= 1;

    private static ViewerRoute DetermineFixtureRoute(HarnessWorld world)
    {
        var actor = world.Actor;
        if (actor.FoodItems > 0)
        {
            return new ViewerRoute("consume", null, null, [], world.Map.ManifestDigest);
        }

        var berry = world.Map.GetResource("berry-patch");
        if (world.GetResource(berry.Id).State == ResourceState.Available)
        {
            return RouteTo(world.Map, actor.Position, berry.Position, "harvest", berry.Id);
        }

        return new ViewerRoute("fixture_complete", null, null, [], world.Map.ManifestDigest);
    }

    private static ViewerRoute RouteTo(
        SeededMap map,
        GridPoint origin,
        GridPoint destination,
        string status,
        string destinationId)
    {
        var steps = origin == destination
            ? []
            : DeterministicRouteFinder.Find(map, origin, destination)
                .Skip(1)
                .Select(ToPosition)
                .ToArray();
        return new ViewerRoute(status, destinationId, ToPosition(destination), steps, map.ManifestDigest);
    }

    private static IEnumerable<ViewerPosition> KnownNearby(SeededMap map, GridPoint origin) => map.Tiles
        .Where(tile => Math.Abs(tile.Position.X - origin.X) <= 1 && Math.Abs(tile.Position.Y - origin.Y) <= 1)
        .OrderBy(tile => tile.Position.Y)
        .ThenBy(tile => tile.Position.X)
        .Select(tile => ToPosition(tile.Position));

    /// <summary>
    /// The deterministic fixture has no persistent cognitive map. Its truthful
    /// knowledge is therefore limited to the current local perception and the
    /// route/destination it has already committed to follow. The server still
    /// owns the full map for routing, but must not accidentally project that
    /// omniscience as inhabitant knowledge.
    /// </summary>
    private static ViewerPosition[] KnownFixtureTopology(
        GridPoint currentPosition,
        IReadOnlyList<ViewerPosition> perceived,
        ViewerRoute route)
    {
        var routeKnowledge = route.Destination is null
            ? route.Steps
            : route.Steps.Append(route.Destination);

        return perceived
            .Append(ToPosition(currentPosition))
            .Concat(routeKnowledge)
            .Distinct()
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
    }

    private static ViewerActor ToActor(HarnessActor actor) => new(
        actor.Id,
        ToPosition(actor.Position),
        actor.HungerBasisPoints,
        actor.FoodItems,
        actor.WoodItems);

    private static ViewerEvent ToEvent(OwnerWorldEvent worldEvent) => new(
        worldEvent.EventId,
        worldEvent.WorldTick,
        worldEvent.Kind,
        worldEvent.Detail);

    private static ViewerEvent ToEvent(PlaytestWorldEvent worldEvent) => new(
        worldEvent.EventId,
        worldEvent.WorldTick,
        worldEvent.Kind,
        worldEvent.Detail,
        worldEvent.Position is { } position ? ToPosition(position) : null);

    private static ViewerCognition ToCognition(PrivateWorldRuntimeState state)
    {
        var runtimes = state.Society.Cognition.Runtimes
            .OrderBy(runtime => runtime.InhabitantId, StringComparer.Ordinal)
            .ToArray();
        var current = runtimes
            .Select(runtime => runtime.CurrentIntention)
            .FirstOrDefault(intention => intention is not null);
        var provider = current?.Provider.ToString().ToLowerInvariant() ??
            (state.Society.Society.WorldDefaultProviderBindingId is null ? "deterministic" : "configured");
        return new ViewerCognition(
            provider,
            state.Society.Society.IsPaused,
            null,
            current?.CandidateId,
            current?.Provider.ToString().ToLowerInvariant(),
            state.Society.Cognition.Events
                .OrderBy(worldEvent => worldEvent.EventId)
                .TakeLast(12)
                .Select(worldEvent => new ViewerCognitionEvent(
                    worldEvent.EventId,
                    worldEvent.WorldTick,
                    worldEvent.Kind,
                    worldEvent.Detail))
                .ToArray(),
            runtimes.Where(runtime => runtime.CurrentIntention is not null)
                .Select(runtime =>
                {
                    var intention = runtime.CurrentIntention!;
                    return new ViewerInhabitantDecision(
                        runtime.InhabitantId, intention.Usage?.ProviderId ?? intention.Provider.ToString().ToLowerInvariant(),
                        intention.CandidateId, intention.WorldTick, intention.Confidence,
                        intention.Usage?.ModelId, intention.Usage?.InputTokens, intention.Usage?.OutputTokens,
                        intention.Usage?.Role, intention.Usage?.LatencyMilliseconds,
                        runtime.Events.LastOrDefault(item => item.WorldTick == intention.WorldTick &&
                            item.Kind is "cognition_fallback_applied" or "cognition_decision_applied")?.Kind == "cognition_fallback_applied");
                }).ToArray());
    }

    private static ViewerPosition ToPosition(GridPoint point) => new(point.X, point.Y);

    private static string ToWireValue(TerrainKind terrain) => terrain switch
    {
        TerrainKind.Meadow => "meadow",
        TerrainKind.Water => "water",
        TerrainKind.Mountain => "mountain",
        TerrainKind.River => "river",
        TerrainKind.Lake => "lake",
        TerrainKind.Ocean => "ocean",
        TerrainKind.Peak => "peak",
        TerrainKind.Sand => "sand",
        TerrainKind.Forest => "forest",
        TerrainKind.Snow => "snow",
        _ => throw new ArgumentOutOfRangeException(nameof(terrain)),
    };

    private static string ToWireValue(ResourceState state) => state switch
    {
        ResourceState.Available => "available",
        ResourceState.Depleted => "depleted",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string? TreeStageFor(string? kind, EcologyResource? resource) => kind switch
    {
        "orchard" when resource?.Quantity > 0 => "fruiting",
        "orchard" when resource?.State == EcologyResourceState.Depleted => "picked",
        "orchard" => "growing",
        _ => null,
    };

    private static string ToWireValue(OwnerInstructionKind kind) => kind switch
    {
        OwnerInstructionKind.Suggestive => "suggestive",
        OwnerInstructionKind.MustDo => "must_do",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ToWireValue(OwnerInstructionState state) => state switch
    {
        OwnerInstructionState.Queued => "queued",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
