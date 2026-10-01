using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Cognition;
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
public sealed partial class OwnerWorldObservationStore
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
        var fertility = new LandFertility(map, state.WorldSeed);
        var ecology = state.WorldSystems?.Ecology.Resources.ToDictionary(resource => resource.Id, StringComparer.Ordinal);
        var buildingDefinitions = state.WorldContent?.Buildings.ToDictionary(building => building.CanonicalId, StringComparer.Ordinal);
        var activeInhabitants = state.Society.Society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var inhabitantsById = state.Society.Society.Inhabitants
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
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
        var jobs = state.WorldSimulation?.ProductionJobs.ToArray() ?? [];
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
                    TreeGrowthRules.StageOf(resource.TreeKind, ecology?.GetValueOrDefault(resource.Id),
                        state.WorldSystems?.Climate.Season ?? SeasonKind.Spring),
                    resource.NaturalObjectKind))
                .ToArray(),
            actor,
            latestEventId)
        {
            PackedTerrain = packedTerrain,
            PackedMapLayers = state.Geography is null || mapLayersUnchanged ? null : PackMapLayers(map, state.WorldSeed),
            MapLayersDigest = mapLayersDigest,
            Livestock = (state.Livestock ?? []).Select(animal => new ViewerAnimal(animal.Id,
                animal.Kind.ToString().ToLowerInvariant(), animal.HouseholdId, ToPosition(animal.Position),
                LivestockRules.HasCare(animal, state.Society.Society.WorldTick), animal.RiderId,
                animal.NaturalDeathTick is not null ? !animal.HideCollected && animal.Kind != LivestockKind.Chicken ? "hide" : null : LivestockRules.Product(animal.Kind),
                animal.NaturalDeathTick is not null ? animal.HideCollected || animal.Kind == LivestockKind.Chicken ? 0 : 1 : animal.PendingProductQuantity,
                animal.NaturalDeathTick is not null, state.Society.Society.Inventory.Lots.Where(lot => lot.AnimalId == animal.Id)
                    .GroupBy(lot => lot.ItemKind).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new ViewerInventoryEntry(group.Key, group.Sum(lot => lot.Quantity))).ToArray())).ToArray(),
            Fields = (state.Fields ?? []).Select(field => new ViewerFarmField(ToPosition(field.Position), field.HouseholdId,
                field.Stage.ToString().ToLowerInvariant(), field.Crop, fertility.At(field.Position),
                field.Work?.WorkerId, field.Work?.RemainingTicks)).ToArray(),
            GroundStocks = state.Society.Society.Inventory.Lots.Where(lot => lot.GroundPosition is not null && lot.Quantity > 0)
                .GroupBy(lot => (Position: lot.GroundPosition!.Value, lot.OwnerId, lot.ItemKind))
                .OrderBy(group => group.Key.Position.Y).ThenBy(group => group.Key.Position.X)
                .ThenBy(group => group.Key.ItemKind, StringComparer.Ordinal).ThenBy(group => group.Key.OwnerId, StringComparer.Ordinal)
                .Select(group => new ViewerGroundStock(new(group.Key.Position.X, group.Key.Position.Y), group.Key.OwnerId,
                    group.Key.ItemKind, group.Sum(lot => lot.Quantity))).ToArray(),
            WrapsEastWest = state.Geography?.WrapEastWest == true,
            Boats = (state.BoatTransport?.Boats ?? []).Select(boat => new ViewerBoat(boat.Id, boat.TownId, ToPosition(boat.Position),
                boat.Journey?.PassengerId, boat.Journey?.OriginPortId, boat.Journey?.DestinationPortId,
                boat.Journey is null ? "docked" : boat.Journey.WaitingSinceTick is not null ? "waiting" : boat.Journey.Returning ? "returning" : "travelling",
                boat.Journey?.WaterPath.Skip(boat.Journey.PathIndex).Select(ToPosition).ToArray() ?? [],
                boat.Journey is { } journey ? state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == journey.PassengerId &&
                        lot.StorageBuildingId is null && lot.GroundPosition is null)
                    .GroupBy(lot => lot.ItemKind).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new ViewerInventoryEntry(group.Key, group.Sum(lot => lot.Quantity))).ToArray() : [],
                state.Society.Society.Inventory.Lots.Where(lot => boat.EstateCargoLotIds?.Contains(lot.Id, StringComparer.Ordinal) == true)
                    .GroupBy(lot => lot.ItemKind).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new ViewerInventoryEntry(group.Key, group.Sum(lot => lot.Quantity))).ToArray())).ToArray(),
            Inhabitants = activeInhabitants
                .Select(inhabitant => ToPlaytestInhabitant(state, inhabitant, physicalById[inhabitant.Id]))
                .Concat(state.Society.Society.Inhabitants
                    .Where(inhabitant => inhabitant.Status == SocietyInhabitantStatus.Dead && deceasedById.ContainsKey(inhabitant.Id))
                    .Select(inhabitant => ToDeceasedInhabitant(state, inhabitant, deceasedById[inhabitant.Id])))
                .OrderBy(inhabitant => inhabitant.Id, StringComparer.Ordinal)
                .ToArray(),
            Conversations = (state.Conversations ?? [])
                .OrderByDescending(conversation => conversation.LastUpdatedTick)
                .ThenBy(conversation => conversation.Id, StringComparer.Ordinal)
                .Take(16)
                .Select(conversation => new ViewerConversation(
                    conversation.Id,
                    conversation.InitiatorId,
                    inhabitantsById.GetValueOrDefault(conversation.InitiatorId)?.Name ?? conversation.InitiatorId,
                    conversation.InviteeId,
                    inhabitantsById.GetValueOrDefault(conversation.InviteeId)?.Name ?? conversation.InviteeId,
                    ConversationStatus(conversation.Status),
                    conversation.Interruption == AgentConversationInterruption.None
                        ? null : ConversationInterruption(conversation.Interruption),
                    conversation.Outcome,
                    conversation.CreatedTick,
                    conversation.LastUpdatedTick,
                    conversation.Turns.TakeLast(AgentConversationRules.MaximumPublicTurns +
                            AgentConversationRules.MaximumWrapUpTurns)
                        .Select(turn => new ViewerConversationTurn(
                            turn.Id,
                            turn.SpeakerId,
                            inhabitantsById.GetValueOrDefault(turn.SpeakerId)?.Name ?? turn.SpeakerId,
                            turn.Text,
                            turn.WorldTick,
                            turn.ListenerIds.Take(AgentConversationRules.MaximumListenersPerTurn).ToArray(),
                            turn.IsWrapUp))
                        .ToArray()))
                .ToArray(),
            Stockpiles = state.Society.Society.Households.Select(household =>
                new ViewerStockpile(household.Id, (household.Id, household.Name) switch
                {
                    ("household:camp-alpha", "Camp Alpha") => "First household",
                    ("household:camp-beta", "Camp Beta") => "Second household",
                    _ => household.Name,
                }, InventoryFor(state, household.Id))).ToArray(),
            Council = (state.Towns ?? []).Count == 0 && state.Council is { } council ? new ViewerCouncil(
                state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == council.StewardId)?.Name,
                council.FoodPolicy, council.Ballot?.Policy, council.Ballot?.Approvals.Count ?? 0,
                council.Ballot?.Rejections.Count ?? 0, council.Ballot?.Electorate.Count ?? 0) : null,
            TownCouncils = (state.TownCouncils ?? []).Select(current => new ViewerTownCouncil(current.TownId,
                (state.WorldSimulation?.Buildings ?? []).FirstOrDefault(building => building.TownId == current.TownId &&
                    (state.WorldContent?.Buildings ?? []).Any(definition => definition.CanonicalId == building.DefinitionId &&
                        definition.Tags.Contains("town_hall", StringComparer.Ordinal)))?.InstanceId,
                current.FoodPolicy, current.MemberIds.ToArray(), current.MemberIds.Select(id =>
                    state.Society.Society.Inhabitants.Single(person => person.Id == id).Name).ToArray(),
                current.TermStartedTick, current.TermExpiryTick, current.Election?.ExpiryTick,
                current.Election?.Votes.Count ?? 0, current.Election?.Electorate.Count ?? 0, current.Ballot?.Text,
                current.Ballot?.Approvals.Count ?? 0, current.Ballot?.Rejections.Count ?? 0, current.Ballot?.Electorate.Count ?? 0,
                (current.Laws ?? []).Select(law => new ViewerTownLaw(law.Key, law.Text, law.AdoptedTick)).ToArray())
            {
                ElectionIsRunoff = current.Election?.IsRunoff ?? false,
                ElectionAvailableSeats = current.Election?.AvailableSeats ?? 0,
                ElectionCandidateNames = (current.Election?.Candidates ?? []).Select(id => state.Society.Society.Inhabitants.Single(person => person.Id == id).Name).ToArray(),
                ElectionSelectedMemberNames = (current.Election?.SelectedMemberIds ?? []).Select(id => state.Society.Society.Inhabitants.Single(person => person.Id == id).Name).ToArray(),
                GoverningForm = current.GoverningForm,
                FallbackReason = current.FallbackReason,
                ElectionKind = current.Election?.Kind,
                ElectionRetryAfterTick = current.ElectionRetryAfterTick,
                CandidateRegister = current.CandidateRegister.Select(item => new ViewerTownCandidate(
                    state.Society.Society.Inhabitants.Single(person => person.Id == item.CandidateId).Name, item.FullTermWilling, item.ReplacementWilling)).ToArray(),
                LastDrawMemberNames = (current.LastElectionOutcome?.DrawnMemberIds ?? []).Select(id =>
                    state.Society.Society.Inhabitants.Single(person => person.Id == id).Name).ToArray()
            }).ToArray(),
            BusinessTrade = BusinessSnapshot(state),
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
            Carts = (state.WorldSimulation?.Carts ?? []).Select(cart => new ViewerCart(cart.Id,
                state.Society.Society.Inventory.GetLot(cart.LotId).OwnerId, ToPosition(cart.Position), cart.PullerId,
                state.Society.Society.Inventory.GetLot(cart.LotId).ConditionBasisPoints,
                state.Society.Society.Inventory.Lots.Where(lot => lot.CartId == cart.Id).Sum(lot => (long)lot.Quantity),
                CartContent.Capacity, state.Society.Society.Inventory.Lots.Where(lot => lot.CartId == cart.Id && lot.ContainerLotId is null)
                    .GroupBy(lot => lot.ItemKind, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new ViewerInventoryEntry(group.Key, group.Sum(lot => lot.Quantity),
                        HasCondition(group.Key) ? group.Min(lot => lot.ConditionBasisPoints) : null,
                        group.Where(lot => lot.ConditionBasisPoints == 0).Sum(lot => lot.Quantity),
                        group.Sum(lot => lot.ContainerCapacity), state.Society.Society.Inventory.Lots.Where(lot =>
                            group.Any(parent => parent.Id == lot.ContainerLotId)).GroupBy(lot => lot.ItemKind, StringComparer.Ordinal)
                            .Select(contents => new ViewerInventoryContent(contents.Key, contents.Sum(lot => lot.Quantity))).ToArray())).ToArray())).ToArray(),
            Bridges = (state.Bridges ?? []).OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new ViewerBridge(item.Id, item.Design, item.Trigger,
                    RiverBridgeRules.AxisOf(item) == BridgeAxis.EastWest ? "east_west" : "north_south",
                    item.Entrances.Select(ToPosition).ToArray(), item.Span.Select(ToPosition).ToArray(),
                    item.BuiltTick))
                .ToArray(),
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
                    item.Footprint?.Width ?? buildingDefinitions?.GetValueOrDefault(item.DefinitionId)?.Width ?? 1,
                    item.Footprint?.Height ?? buildingDefinitions?.GetValueOrDefault(item.DefinitionId)?.Height ?? 1,
                    item.TownId,
                    item.HouseholdId,
                    item.HouseholdId is { } householdId
                        ? InventoryFor(state, householdId, item.InstanceId)
                        : item.TownId is { } townId && buildingDefinitions?.GetValueOrDefault(item.DefinitionId)?
                            .Tags.Contains("warehouse", StringComparer.Ordinal) == true
                            ? InventoryFor(state, townId, item.InstanceId) : null,
                    item.Entrance is { } entrance ? ToPosition(entrance) : null,
                    buildingDefinitions?.GetValueOrDefault(item.DefinitionId) is { } storageDefinition
                        ? BuildingStorageRules.Capacity(storageDefinition, item) : null,
                    state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == item.InstanceId).Sum(lot => lot.Quantity),
                    item.Footprint?.Revision ?? 0,
                    (state.WorldSimulation.GuestInvitations ?? []).Where(invitation => invitation.HouseInstanceId == item.InstanceId && invitation.Active)
                        .Select(invitation => state.Society.Society.Inhabitants.Single(person => person.Id == invitation.GuestId).Name).ToArray(),
                    (state.WorldSimulation.BuildingExpansions ?? []).LastOrDefault(job => job.BuildingInstanceId == item.InstanceId)?.State.ToString().ToLowerInvariant(),
                    (state.WorldSimulation.BuildingExpansions ?? []).LastOrDefault(job => job.BuildingInstanceId == item.InstanceId)?.Failure))
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

    private static string ConversationStatus(AgentConversationStatus status) => status switch
    {
        AgentConversationStatus.Proposed => "proposed",
        AgentConversationStatus.Ready => "ready",
        AgentConversationStatus.AwaitingSpeaker => "awaiting_speaker",
        AgentConversationStatus.WrapUp => "wrap_up",
        AgentConversationStatus.Suspended => "suspended",
        AgentConversationStatus.Closed => "closed",
        _ => "unknown",
    };

    private static string ConversationInterruption(AgentConversationInterruption interruption) => interruption switch
    {
        AgentConversationInterruption.OwnerPaused => "owner_paused",
        AgentConversationInterruption.Disconnected => "disconnected",
        AgentConversationInterruption.UrgentNeed => "urgent_need",
        AgentConversationInterruption.ProviderUnavailable => "provider_unavailable",
        AgentConversationInterruption.ProviderTimedOut => "provider_timed_out",
        AgentConversationInterruption.ProviderRejected => "provider_rejected",
        AgentConversationInterruption.Restored => "restored",
        _ => "unknown",
    };

    internal static ViewerPackedTerrain PackTerrain(SeededMap map)
    {
        var bytes = new byte[checked(map.Width * map.Height)];
        foreach (var tile in map.Tiles)
            bytes[tile.Position.Y * map.Width + tile.Position.X] = checked((byte)tile.Terrain);
        return new ViewerPackedTerrain(map.Width, map.Height, "terrain-kind-v1",
            Convert.ToBase64String(bytes));
    }

    internal static ViewerPackedMapLayers? PackMapLayers(SeededMap map, string? worldSeed = null)
    {
        if (map.ClimateZones is not { } climate || map.ElevationLevels is not { } elevation ||
            map.HydrologyKinds is not { } hydrology || map.SurfaceKinds is not { } surface ||
            map.VegetationKinds is not { } vegetation) return null;
        var fertility = worldSeed is null ? null : new LandFertility(map, worldSeed);
        return new ViewerPackedMapLayers(map.Width, map.Height, "map-layers-v2",
            Convert.ToBase64String(climate), Convert.ToBase64String(elevation),
            Convert.ToBase64String(hydrology), Convert.ToBase64String(surface),
            Convert.ToBase64String(vegetation))
        {
            Fertility = fertility is null ? null : Convert.ToBase64String(map.Tiles.OrderBy(tile => tile.Position.Y)
                .ThenBy(tile => tile.Position.X).Select(tile => checked((byte)fertility.At(tile.Position))).ToArray()),
        };
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
        if (HousingDetail(state, physical.Housing) is { } housingDetail)
            decisionFactors.Add(new ViewerDecisionFactor("housing", housingDetail));
        if (physical.ChildModelSelection is { Provider: { } birthProvider } birthModel)
        {
            decisionFactors.Add(new ViewerDecisionFactor("birth-model-provider", birthProvider));
            if (birthModel.ModelId is { } modelId)
                decisionFactors.Add(new ViewerDecisionFactor("birth-model-id", modelId));
        }
        var runtime = state.Society.Cognition.Runtimes
            .FirstOrDefault(item => item.InhabitantId == inhabitant.Id);
        var modelStatus = physical.LastModelAttempt?.Status ?? "ready";
        if (modelStatus == "waiting" && state.Society.Society.IsPaused) modelStatus = "canceled";
        decisionFactors.Add(new ViewerDecisionFactor("model-status", modelStatus));
        if (physical.LastModelAttempt?.LastAcceptedCandidateId is { } acceptedCandidate)
            decisionFactors.Add(new ViewerDecisionFactor("last-model-choice", acceptedCandidate));
        if (physical.LastModelAttempt?.SetupBlocker is { } setupBlocker)
            decisionFactors.Add(new ViewerDecisionFactor("model-setup-blocker", setupBlocker));
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
                    EquipmentItem(state, physical, physical.Equipment?.WornClothingLotId) is { ConditionBasisPoints: > 0 },
                    inventory.Any(item => (item.Kind == "tool" || ToolCapabilities.ForItem(item.Kind) is not null) &&
                        item.Quantity > item.BrokenQuantity), survival.NutritionBasisPoints, survival.LastMealKind) : null,
            Equipment = EquipmentFor(state, physical),
            MedicalCare = new(inhabitant.HealthBasisPoints, physical.MedicalTreatment?.Kind,
                physical.MedicalTreatment?.RemainingTicks ?? 0, physical.MedicalTreatment is { } treatment
                    ? state.Society.Society.GetInhabitant(treatment.CaregiverId).Name : null),
            Lesson = physical.Lesson is { } lesson ? new ViewerLesson(
                state.Society.Society.GetInhabitant(lesson.TeacherId).Name, lesson.Skill.ToString().ToLowerInvariant(),
                lesson.Stage, lesson.Progress, 20) : null,
            Proficiency = physical.Proficiency is { } practice
                ? new ViewerProficiency(practice.Building, practice.Farming, practice.Crafting) : null,
            Skills = ProjectSkills(physical, state.Society.Society),
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
                .Concat(HousingRequestNotes(state, inhabitant))
                .ToArray(),
        };
    }

    /// <summary>Why an adult has no home, in player terms; null when they have one.</summary>
    private static string? HousingDetail(PrivateWorldRuntimeState state, SettlementHousing? housing)
    {
        if (housing?.Blocker is not { } blocker)
            return null;
        var asked = housing.Request is { } request
            ? state.Society.Society.Households.FirstOrDefault(item => item.Id == request.HouseholdId)?.Name ?? "another"
            : "another";
        return blocker switch
        {
            HousingBlockers.AwaitingAnswer => $"No home yet. Asked the {asked} household to live in their House; every adult member must agree.",
            HousingBlockers.NoHousehold => "No home. Belongs to no household, so no House can be planned. A household with a House may agree to take them in.",
            HousingBlockers.NoAuthorizedHome => "No home. The household holds no House yet and can plan one.",
            HousingBlockers.MissingMaterials => "No home. The household holds no House and lacks the materials to build one.",
            HousingBlockers.NoLegalSite => "No home. The household has the materials for a House but no legal site to build it.",
            _ => null,
        };
    }

    /// <summary>Requests to live in this adult's House that they must answer, or have answered.</summary>
    private static IEnumerable<string> HousingRequestNotes(PrivateWorldRuntimeState state, SocietyInhabitant member)
    {
        if (member.HouseholdId is null)
            yield break;
        foreach (var applicant in state.Inhabitants.OrderBy(person => person.InhabitantId, StringComparer.Ordinal))
        {
            if (applicant.Housing?.Request is not { } request || request.HouseholdId != member.HouseholdId ||
                !request.Members.Contains(member.Id, StringComparer.Ordinal))
                continue;
            var name = state.Society.Society.GetInhabitant(applicant.InhabitantId).Name;
            yield return request.Approvals.Contains(member.Id, StringComparer.Ordinal)
                ? $"Agreed to let {name} live in the House; waiting for the other adults."
                : request.Rejections.Contains(member.Id, StringComparer.Ordinal)
                    ? $"Refused {name}'s request to live in the House."
                    : $"{name} asked to live in the household's House. Every adult member must answer.";
        }
    }

    private static ViewerSkill[] ProjectSkills(PlaytestInhabitantState physical, SocietyCheckpoint society) =>
        (physical.Skills ?? []).Select(skill => new ViewerSkill(skill.Kind.ToString().ToLowerInvariant(),
            skill.LearnedTick, skill.TeacherId,
            skill.TeacherId is { } teacher ? society.GetInhabitant(teacher).Name : null)).ToArray();

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
            Skills = ProjectSkills(lastPhysical, state.Society.Society),
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

    private static ViewerEquippedItem? EquipmentItem(PrivateWorldRuntimeState state,
        PlaytestInhabitantState person, string? lotId)
    {
        var lot = state.Society.Society.Inventory.Lots.FirstOrDefault(item => item.Id == lotId &&
            item.OwnerId == person.InhabitantId && item.StorageBuildingId is null && item.DeliveryBuildingId is null &&
            item.ContainerLotId is null && item.GroundPosition is null && item.Quantity == 1);
        return lot is null ? null : new(lot.ItemKind, lot.ConditionBasisPoints);
    }

    private static ViewerEquipment EquipmentFor(PrivateWorldRuntimeState state, PlaytestInhabitantState person) =>
        new(CarryEquipmentRules.Load(state.Society.Society.Inventory, person.InhabitantId),
            CarryEquipmentRules.Capacity(state.Society.Society.Inventory, person),
            EquipmentItem(state, person, person.Equipment?.WornClothingLotId),
            EquipmentItem(state, person, person.Equipment?.CarryAidLotId),
            EquipmentItem(state, person, person.Equipment?.WeaponLotId),
            EquipmentItem(state, person, person.Equipment?.ShieldLotId),
            EquipmentItem(state, person, person.Equipment?.ArmorLotId),
            EquipmentItem(state, person, person.Equipment?.OrnamentLotId));

    private static bool HasCondition(string kind) => ToolCapabilities.ForItem(kind) is not null ||
        CarryEquipmentRules.IsClothing(kind) || CarryEquipmentRules.IsCarryAid(kind) || CombatGearContent.IsGear(kind);

    private static ViewerInventoryEntry[] InventoryFor(
        PrivateWorldRuntimeState state,
        string ownerId,
        string? storageBuildingId = null) => state.Society.Society.Inventory.Lots
        .Where(lot => lot.OwnerId == ownerId && lot.Quantity > 0 && lot.ContainerLotId is null &&
            (!state.Inhabitants.Any(person => person.InhabitantId == ownerId) ||
                lot.StorageBuildingId is null && lot.GroundPosition is null) &&
            (storageBuildingId is null || lot.StorageBuildingId == storageBuildingId))
        .GroupBy(lot => lot.ItemKind, StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => new ViewerInventoryEntry(group.Key, group.Sum(lot => lot.Quantity),
            !HasCondition(group.Key) ? null : group.Min(lot => lot.ConditionBasisPoints),
            !HasCondition(group.Key) ? 0 : group.Where(lot => lot.ConditionBasisPoints == 0).Sum(lot => lot.Quantity),
            group.Sum(lot => lot.ContainerCapacity), group.Any(lot => lot.ContainerCapacity > 0)
                ? state.Society.Society.Inventory.Lots.Where(lot => lot.ContainerLotId is not null &&
                        group.Any(vessel => vessel.Id == lot.ContainerLotId))
                    .GroupBy(lot => lot.ItemKind, StringComparer.Ordinal).OrderBy(contents => contents.Key, StringComparer.Ordinal)
                    .Select(contents => new ViewerInventoryContent(contents.Key, contents.Sum(lot => lot.Quantity))).ToArray()
                : null))
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
