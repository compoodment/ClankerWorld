using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    public void Validate()
    {
        SocietyFixture.Validate(society.Checkpoint);
        ValidateBeliefEventSources(society.Checkpoint.Beliefs ?? [], events, eventHistoryFloor);
        society.Validate();
        ValidateBusinessTrades(BusinessTrades, society.Checkpoint, map, WorldTick);
        ValidateToolMakingRequests(ToolMakingRequests, worldSimulation, worldContent, society.Checkpoint, inhabitants.Values, BusinessTrades, WorldTick);
        ValidateMedicalCare(inhabitants.Values, deceasedInhabitants.Values, society.Checkpoint, map);
        contentRegistry.Validate();
        worldContent.Validate();
        var expectedWorldContent = RebuildWorldContent(contentRegistry.ExportState());
        if (!string.Equals(worldContent.StateDigest, expectedWorldContent.StateDigest, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The private-world typed content does not match active package records.");
        }
        assetReservations.Validate();
        ValidateAssetReservationsAgainstActivePackages();
        WorldContentSimulationRules.Validate(worldSimulation, worldContent, map, WorldTick);
        ValidateBuildingExpansionState(worldSimulation, worldContent, society.Checkpoint, map, checkpointSchemaVersion);
        ValidateBoatTransport(CaptureState());
        ValidateAnimalState(CaptureState());
        ValidateHandcarts(handcartHitches, society.Checkpoint.Inventory, inhabitants.Values.ToArray(), map);
        ValidatePhysicalInventoryLocations(society.Checkpoint.Inventory, worldSimulation, worldContent,
            society.Checkpoint.Inhabitants, map, society.Checkpoint.Estates);
        ValidateFarmFields(fields.ToArray(), map, worldSeed, society.Checkpoint, worldSimulation, worldContent, RoadAndBridgeTiles().ToArray());
        ValidateFieldOrderBindings(fields, instructionsByIdempotency.Values);
        ValidateProductionOrderBindings(worldSimulation, worldContent, inhabitants.Values, instructionsByIdempotency.Values);
        ValidateCustodyOrderBindings(society.Checkpoint, instructionsByIdempotency.Values);
        ValidateDeliveryOrderBindings(worldSimulation, society.Checkpoint, towns, instructionsByIdempotency.Values);
        ValidateConstructionOrderBindings(worldSimulation, worldContent, society.Checkpoint, inhabitants.Values,
            instructionsByIdempotency.Values, WorldTick);
        ValidateExpansionOrderBindings(worldSimulation, worldContent, society.Checkpoint, towns,
            instructionsByIdempotency.Values, WorldTick);
        ValidateShelterOrderBindings(worldSimulation, worldContent, society.Checkpoint,
            instructionsByIdempotency.Values, WorldTick);
        if (worldSimulation.Buildings.Any(building => building.HouseholdId is { } householdId &&
            !society.Checkpoint.Households.Any(household => household.Id == householdId)))
            throw new InvalidDataException("A House references a missing household.");
        var inventoryReservationIds = society.Checkpoint.Inventory.Reservations
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var productionOwners = society.Checkpoint.Households.Select(home => home.Id)
            .Concat(society.Checkpoint.Inhabitants.Select(person => person.Id)).ToHashSet(StringComparer.Ordinal);
        if (worldSimulation.ProductionJobs.Any(job => job.OwnerId is { } owner &&
                (!productionOwners.Contains(owner) || job.State is WorldProductionJobState.Running or WorldProductionJobState.Paused && worldSimulation.Buildings.Single(building =>
                    building.InstanceId == job.BuildingInstanceId).HouseholdId is { } home && owner != home && owner != job.WorkerId)))
            throw new InvalidDataException("A production job has an unknown owner or differs from its private building's owner.");
        foreach (var job in worldSimulation.ProductionJobs
                     .Concat(worldSimulation.CropBuilds ?? [])
                     .Where(item => item.State is WorldProductionJobState.Running or WorldProductionJobState.Paused))
        {
            if (job.OwnerId is null)
                throw new InvalidDataException("An active production job has no recorded owner.");
            if (job.InputReservationIds.Any(id => !inventoryReservationIds.Contains(id)))
            {
                throw new InvalidDataException($"Production job '{job.JobId}' has a missing inventory reservation.");
            }
            if (job.OwnerId is { } owner && job.InputReservationIds.Any(id =>
                    society.Checkpoint.Inventory.GetReservation(id) is { State: InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed } reservation &&
                    reservation.OwnerId != owner))
                throw new InvalidDataException("A production job differs from its committed materials' owner.");
        }
        WorldSystemsRules.Validate(worldSystems);
        if (worldSystems.WorldTick != WorldTick ||
            !string.Equals(worldSystems.WorldSeed, worldSeed, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The private-world richer-systems state does not match the authoritative clock or seed.");
        }
        var mapValidation = MapAcceptance.Validate(map, allowEmptyCamp: founderSetup is not null);
        if (!mapValidation.IsValid)
        {
            throw new InvalidDataException($"The private-world map is invalid: {mapValidation.Failure}");
        }
        var activeIds = society.Checkpoint.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var physicalIds = inhabitants.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!activeIds.SequenceEqual(physicalIds))
        {
            throw new InvalidDataException("The private-world physical and society populations disagree.");
        }
        ValidateFounderSetup(founderSetup, society.Checkpoint);
        ValidateTowns(towns, map, founderSetup, society.Checkpoint, worldSimulation, worldContent);
        TownLandRightsRules.ValidateRecords(map, WorldTick, towns, townLandTitles,
            householdLandUseRights, householdLandUseRequests, society.Checkpoint);
        foreach (var town in towns)
        {
            TownGovernanceValidation.Validate(town, society.Checkpoint, worldSystems.Config.TicksPerDay);
            TownGovernmentValidation.Validate(town, society.Checkpoint, townLandTitles, worldSystems.Config.TicksPerDay);
        }
        ValidateTownAdmissions(towns, society.Checkpoint, checkpointSchemaVersion);
        ValidateLandHearings(map, society.Checkpoint, towns, householdLandUseRights, householdLandUseRequests, townLandTitles, worldSystems.Config.TicksPerDay);
        TownProjectValidation.Validate(towns, society.Checkpoint, map, worldSimulation, worldContent,
            townLandTitles, householdLandUseRights, householdLandUseRequests, fields, RoadTiles, Bridges);
        ValidatePaidMarkets(towns, society.Checkpoint, map, worldSimulation, worldContent,
            fields, householdLandUseRights, householdLandUseRequests, RoadTiles, Bridges);
        MarketTradeValidation.Validate(towns, society.Checkpoint, map, worldSimulation, worldContent, WorldTick, inhabitants.Values);
        foreach (var town in towns)
            TownNonviolentValidation.Validate(map, society.Checkpoint, town, townLandTitles, worldSystems.Config.TicksPerDay);
        ValidateNonviolentPhysicalState(map, society.Checkpoint, towns, townLandTitles, householdLandUseRights);
        ValidateRoads(RoadTiles, map, founderSetup);
        ValidateBridges(Bridges, bridgeTraffic, map, RoadTiles, worldSimulation, worldContent,
            society.Checkpoint, inhabitants.Values);
        if (!RiverBridgeRules.SameDecks(map.BridgeDecks, RiverBridgeRules.Decks(bridges)))
            throw new InvalidDataException("The passable bridge decks do not match the saved bridges.");
        ValidatePlantedTrees();
        ValidateDeceasedArchive(deceasedInhabitants.Values, society.Checkpoint, map, bridges, checkpointSchemaVersion, towns);
        AgentKnowledgeRules.Validate(knowledge, map, society.Checkpoint, WorldTick);
        ValidateHousing(inhabitants.Values, society.Checkpoint, checkpointSchemaVersion);
        ValidateDependentCare(inhabitants.Values, society.Checkpoint, towns, checkpointSchemaVersion);
        ValidateGuardianPlacements(inhabitants.Values, society.Checkpoint, towns, map, checkpointSchemaVersion);
        ValidateDepartures(inhabitants.Values, society.Checkpoint, checkpointSchemaVersion);
        ValidateEquipment(inhabitants.Values, society.Checkpoint, worldSimulation, worldContent, checkpointSchemaVersion);
        ValidateRepairOrderBindings(inhabitants.Values, society.Checkpoint.Inventory, instructionsByIdempotency.Values);
        ValidateContinuity(continuity, society.Checkpoint, checkpointSchemaVersion);

        foreach (var inhabitant in inhabitants.Values)
        {
            ValidateProficiency(inhabitant);
            ValidateSocialStanding(inhabitant, society.Checkpoint.Inhabitants.Select(item => item.Id), WorldTick);
            ValidatePrivateThoughts(inhabitant.RecentThoughts, WorldTick);
            AgentIdentityMoment.Validate(inhabitant.IdentityMoments, WorldTick, checkpointSchemaVersion);
            if (inhabitant.Project is { } project)
            {
                ValidateProject(project, WorldTick);
            }
            if (!map.IsPassable(inhabitant.Position) && !IsSavedBoatPassenger(boatTransport, inhabitant.InhabitantId, inhabitant.Position) ||
                inhabitant.HungerBasisPoints is < 0 or > 10_000 ||
                inhabitant.MoveWaitTicks < 0 || inhabitant.TravelCooldownTicks < 0)
            {
                throw new InvalidDataException($"Physical state for '{inhabitant.InhabitantId}' is invalid.");
            }
        }

        var expectedEventId = checked(eventHistoryFloor + 1);
        var previousTick = 0L;
        foreach (var worldEvent in events)
        {
            if (worldEvent.EventId != expectedEventId ||
                worldEvent.WorldTick < previousTick ||
                worldEvent.WorldTick > WorldTick ||
                worldEvent.Position is { } eventPosition && !map.Contains(eventPosition))
            {
                throw new InvalidDataException("Private-world events are not a committed ordered sequence.");
            }

            expectedEventId++;
            previousTick = worldEvent.WorldTick;
        }
    }

    private static void ValidateTowns(
        IReadOnlyList<TownRuntimeState>? savedTowns,
        SeededMap map,
        FounderSetupState? setup,
        SocietyCheckpoint society,
        WorldContentSimulationState simulation,
        DeclarativeWorldContentState content)
    {
        ArgumentNullException.ThrowIfNull(savedTowns);
        if (setup is null)
        {
            if (savedTowns.Count != 0 || simulation.Buildings.Any(item => item.TownId is not null))
                throw new InvalidDataException("A legacy world cannot claim an unrecorded Town or Town-assigned building.");
            return;
        }

        if (savedTowns.Count == 0 && !setup.Started && setup.FounderIds.Count == 0 &&
            map.CampObjects.Count == 0 && simulation.Buildings.Count == 0)
            return;

        var firstTown = savedTowns.FirstOrDefault(item => item.Id == TownBorderRules.FirstTownId);
        if (firstTown is null || savedTowns.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != savedTowns.Count)
            throw new InvalidDataException("A founder-setup world must keep its original first Town and unique Town identities.");

        var active = society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active)
            .Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var assignedResidents = new HashSet<string>(StringComparer.Ordinal);
        var byInstance = simulation.Buildings.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var townIds = savedTowns.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var town in savedTowns)
        {
            var isFirstTown = town.Id == TownBorderRules.FirstTownId;
            if (string.IsNullOrWhiteSpace(town.Id) || town.Id != town.Id.Trim() || town.Id.Length > 128 ||
                town.Id.Any(char.IsControl) || string.IsNullOrWhiteSpace(town.Name) || town.Name != town.Name.Trim() ||
                town.Name.Length > 120 || town.FoundedTick < 0 || town.FoundedTick > society.WorldTick ||
                town.OriginSite is { } origin && !map.IsBuildable(origin) ||
                town.ResidentIds is null || town.AssignedBuildingIds is null || town.BorderTiles is null ||
                town.ResidentIds.Distinct(StringComparer.Ordinal).Count() != town.ResidentIds.Count ||
                town.AssignedBuildingIds.Distinct(StringComparer.Ordinal).Count() != town.AssignedBuildingIds.Count ||
                town.BorderTiles.Distinct().Count() != town.BorderTiles.Count || town.BorderTiles.Count == 0 ||
                town.ResidentIds.Any(id => !active.Contains(id) || !assignedResidents.Add(id)))
                throw new InvalidDataException("A Town identity, founding record, membership, or footprint is invalid.");

            if (isFirstTown)
            {
                if (town.Name != TownBorderRules.FirstTownName ||
                    town.FoundingState != (setup.Started ? "founded" : "founding") || town.FoundedTick != 0)
                    throw new InvalidDataException("The original first Town's identity and founding history are invalid.");
            }
            else if (town.FoundingState != "founded")
            {
                throw new InvalidDataException("An additional recorded Town must have a completed founding record.");
            }

            var assignedIds = simulation.Buildings.Where(item => item.TownId == town.Id)
                .Select(item => item.InstanceId).Order(StringComparer.Ordinal).ToArray();
            if (!town.AssignedBuildingIds.Order(StringComparer.Ordinal).SequenceEqual(assignedIds))
                throw new InvalidDataException("Town building assignments disagree with the placed-building state.");

            var border = town.BorderTiles.ToHashSet();
            if (town.BorderTiles.Any(point => !map.Contains(point)) ||
                town.OriginSite is { } site && !border.Contains(site))
                throw new InvalidDataException("The saved Town border does not cover its founding site.");
            foreach (var buildingId in town.AssignedBuildingIds)
            {
                if (!byInstance.TryGetValue(buildingId, out var building) ||
                    !definitions.TryGetValue(building.DefinitionId, out var definition))
                    throw new InvalidDataException("A Town references a building that is not placed.");
                if (WorldContentSimulationRules.Footprint(definition, building).Where(map.IsLand).Any(tile => !border.Contains(tile)) &&
                    !definition.Tags.Contains("warehouse", StringComparer.Ordinal))
                    throw new InvalidDataException("The saved Town border does not cover an assigned building.");
            }

            var warehouses = town.AssignedBuildingIds.Count(id => definitions.TryGetValue(byInstance[id].DefinitionId, out var definition) &&
                definition.Tags.Contains("warehouse", StringComparer.Ordinal));
            if (warehouses > 1)
                throw new InvalidDataException("A Town can have only one assigned Warehouse.");
        }

        if (simulation.Buildings.Any(item => item.TownId is { } townId && !townIds.Contains(townId)))
            throw new InvalidDataException("A placed building references a Town that is not recorded.");
    }

    private static void ValidateFounderSetup(FounderSetupState? setup, SocietyCheckpoint society)
    {
        if (setup is null) return;
        if (setup.FounderIds is null || setup.FounderIds.Count > RequiredFounders ||
            setup.FounderIds.Distinct(StringComparer.Ordinal).Count() != setup.FounderIds.Count ||
            setup.FounderIds.Any(id => string.IsNullOrWhiteSpace(id) ||
                !society.Inhabitants.Any(person => person.Id == id)))
            throw new InvalidDataException("Founder setup references invalid or duplicate agents.");
        if (setup.Started)
        {
            if (setup.FounderIds.Count != RequiredFounders)
                throw new InvalidDataException("A started world requires four configured founders.");
        }
        else if (!society.IsPaused || society.WorldTick != 0 ||
                 society.Inhabitants.Count != setup.FounderIds.Count ||
                 society.Inhabitants.Any(person => person.Status != SocietyInhabitantStatus.Active))
            throw new InvalidDataException("An incomplete founder setup must remain paused at creation time.");
    }

    private static void ValidatePhysicalInventoryLocations(InventoryCheckpoint inventory,
        WorldContentSimulationState simulation, DeclarativeWorldContentState content,
        IReadOnlyList<SocietyInhabitant> inhabitants, SeededMap map, IReadOnlyList<SocietyEstate> estates)
    {
        var buildings = simulation.Buildings.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var people = inhabitants.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var heldEstates = estates.Where(estate => !estate.Settled)
            .ToDictionary(estate => estate.Id, StringComparer.Ordinal);
        bool HasEstateStorage(InventoryLot lot)
        {
            if (heldEstates.TryGetValue(lot.OwnerId, out var estate))
                return people.ContainsKey(estate.DeceasedId) && (estate.FrozenLots ?? []).Any(frozen =>
                    frozen.LotId == lot.Id && frozen.ItemKind == lot.ItemKind && frozen.Quantity == lot.Quantity &&
                    frozen.StorageBuildingId == lot.StorageBuildingId);
            return lot.OwnerId == "settlement:communal" && estates.Where(item => item.Settled)
                .SelectMany(item => item.FrozenLots ?? []).Any(frozen =>
                    (frozen.LotId == lot.Id || frozen.LotId == lot.ProvenanceLotId) && frozen.ItemKind == lot.ItemKind &&
                    frozen.Quantity >= lot.Quantity && frozen.StorageBuildingId == lot.StorageBuildingId);
        }
        foreach (var lot in inventory.Lots)
        {
            if (lot.CarrierId is { } carrierId && (!people.TryGetValue(carrierId, out var custodian) ||
                custodian.Status != SocietyInhabitantStatus.Active))
                throw new InvalidDataException("Inventory physical custody references an unavailable person.");
            if (lot.GroundPosition is { } ground && (!map.Contains(new(ground.X, ground.Y)) ||
                lot.StorageBuildingId is not null || lot.DeliveryBuildingId is not null))
                throw new InvalidDataException($"Inventory lot '{lot.Id}' has an invalid ground location.");
            if (lot.StorageBuildingId is { } storageId)
            {
                if (!buildings.TryGetValue(storageId, out var storage) ||
                    !definitions.TryGetValue(storage.DefinitionId, out var definition) ||
                    !(storage.HouseholdId == lot.OwnerId && definition.Tags.Any(IsHouseholdBuildingTag) ||
                      (people.ContainsKey(lot.OwnerId) || HasEstateStorage(lot)) && storage.HouseholdId is not null &&
                      definition.Tags.Contains("house", StringComparer.Ordinal) ||
                      storage.TownId == lot.OwnerId && storage.HouseholdId is null && !WarehouseFoodKinds.Contains(lot.ItemKind) &&
                      definition.Tags.Contains("warehouse", StringComparer.Ordinal)))
                    throw new InvalidDataException($"Inventory lot '{lot.Id}' has an invalid building storage location.");
            }
            if (lot.DeliveryBuildingId is { } deliveryId &&
                (!buildings.TryGetValue(deliveryId, out var destination) ||
                 destination.HouseholdId is null ||
                 !people.TryGetValue(lot.OwnerId, out var carrier) ||
                 carrier.Status != SocietyInhabitantStatus.Active ||
                 carrier.HouseholdId != destination.HouseholdId))
                throw new InvalidDataException($"Inventory lot '{lot.Id}' has an invalid House delivery destination.");
        }
    }

    private static bool HasSavedToolUseState(PrivateWorldRuntimeState state) =>
        state.WorldSimulation is { } simulation &&
            simulation.ProductionJobs.Concat(simulation.CropBuilds ?? [])
                .Any(job => job is not null && job.ToolLotId is not null) ||
        (state.Fields ?? []).Any(field => field is not null &&
            (field.Work?.HoeLotId is not null || field.Work?.SickleLotId is not null));

    internal static void ValidateMinimumSupportedSchemaVersion(int schemaVersion)
    {
        if (schemaVersion < MinimumSupportedStateSchemaVersion)
            throw new InvalidDataException(
                $"Private-world save schema {schemaVersion} is older than the minimum supported schema {MinimumSupportedStateSchemaVersion}.");
    }

    internal static void ValidateStateForCodec(PrivateWorldRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateMinimumSupportedSchemaVersion(state.SchemaVersion);
        if (state.SchemaVersion > StateSchemaVersion || string.IsNullOrWhiteSpace(state.WorldSeed) || state.EventHistoryFloor < 0)
        {
            throw new InvalidDataException("The private-world runtime state schema or seed is invalid.");
        }
        if (state.SchemaVersion < 34 && (state.Fields is { Count: > 0 } ||
            state.Society.Society.Inventory.Lots.Any(lot => lot.GroundPosition is not null)))
            throw new InvalidDataException("Household fields and ground harvest lots require private-world schema 34.");
        if (state.SchemaVersion >= 34 && state.Fields is null)
            throw new InvalidDataException("Private-world schema 34 requires authoritative household field state.");
        if (state.SchemaVersion < ConversationSchemaVersion &&
            (state.Conversations is { Count: > 0 } || state.ConversationBudgets is { Count: > 0 }))
            throw new InvalidDataException($"Conversation history and daily budgets require private-world schema {ConversationSchemaVersion}.");
        try { (state.RoutineHelper ?? throw new ArgumentException("Missing routine helper.")).Validate(); }
        catch (ArgumentException exception) { throw new InvalidDataException("The saved routine helper is invalid.", exception); }
        if ((state.RoutineHelper.Provider != "off") != (state.JevEnabled ?? true))
            throw new InvalidDataException("The saved helper availability is inconsistent.");
        if (state.JevPolicyRevision < 0 || state.JevEnabled is null && state.JevPolicyRevision != 0)
            throw new InvalidDataException("The saved Jev routing policy is invalid.");
        if (state.Geography is not null &&
            (state.FounderSetup is null ||
             !string.Equals(state.Geography.Seed, state.WorldSeed, StringComparison.Ordinal)))
            throw new InvalidDataException("Generated geography does not match the saved world setup.");
        // New worlds can only be created at these sizes, so any other saved size is damage.
        if (state.Geography is { Size: not (WorldSizePreset.Small or WorldSizePreset.Medium) })
            throw new InvalidDataException("Only Small and Medium worlds can be loaded.");
        if (state.HandcartHitches is null)
            throw new InvalidDataException("The current private-world checkpoint is missing cart attachments.");
        // A map from an older terrain generator cannot be rebuilt; refuse it
        // by name rather than as a mismatched regeneration.
        if (state.Geography is { } geography &&
            geography.BalancedVisibilityVersion != GeographyGenerator.CurrentBalancedVisibilityVersion)
            throw new InvalidDataException(GeographyGenerator.OlderTerrainVersionMessage);
        ValidateFounderSetup(state.FounderSetup, state.Society.Society);
        if (state.Towns is null || state.Knowledge is null || state.RoadTiles is null ||
            state.Bridges is null || state.BridgeTraffic is null || state.TownLandTitles is null ||
            state.HouseholdLandUseRights is null || state.HouseholdLandUseRequests is null)
            throw new InvalidDataException("The current private-world checkpoint is missing required Town, land-rights, map-knowledge, Road or bridge state.");
        if (state.Content is null || state.WorldSystems is null || state.WorldContent is null ||
            state.WorldSimulation is null || state.AssetReservations is null)
            throw new InvalidDataException("The current private-world checkpoint is missing required content or world-system state.");
        ValidateAnimalState(state);
        if (state.SchemaVersion < ToolProgressionSchemaVersion && HasSavedToolUseState(state))
            throw new InvalidDataException($"Saved tool use links require private-world schema {ToolProgressionSchemaVersion}.");
        if (state.SchemaVersion >= ConversationSchemaVersion && (state.Conversations is null || state.ConversationBudgets is null))
            throw new InvalidDataException($"Private-world schema {ConversationSchemaVersion} requires conversation state and daily budgets.");
        if (state.SchemaVersion >= OrderLifecycleSchemaVersion && state.OrderCancellations is null)
            throw new InvalidDataException($"Private-world schema {OrderLifecycleSchemaVersion} requires order-cancellation retry state.");
        var hasArchivedEvents = state.EventHistoryFloor > 0 || state.Society.Society.EventHistoryFloor > 0 ||
            state.Society.Society.Inventory.EventHistoryFloor > 0 || state.Society.Cognition.EventHistoryFloor > 0 ||
            state.Society.Cognition.Runtimes.Any(runtime => runtime.EventHistoryFloor > 0);
        if ((hasArchivedEvents && state.HistoryArchiveHead is null) ||
            (state.HistoryArchiveHead is { } head && (head.Length != 64 || head.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))))
        {
            throw new InvalidDataException("The private-world history reference or schema is invalid.");
        }

        if (state.Geography is not null && (state.Map.ClimateZones is null || state.Map.ElevationLevels is null ||
            state.Map.HydrologyKinds is null || state.Map.SurfaceKinds is null || state.Map.VegetationKinds is null))
            throw new InvalidDataException("The current generated geography is missing required map layers.");

        if (!MapAcceptance.Validate(state.Map, allowEmptyCamp: state.FounderSetup is not null).IsValid)
        {
            throw new InvalidDataException("The private-world runtime contains an invalid map.");
        }
        // Saved positions and paths may be on bridge decks, so check them
        // against the same bridged map that movement uses.
        var travelMap = TravelMap(state);

        using var society = SocietyWorldRuntime.Restore(state.Society);
        if (state.SchemaVersion >= ObserverGuidanceSchemaVersion &&
            (state.Instructions is null || state.CompletedInstructionIds is null))
            throw new InvalidDataException($"Private-world schema {ObserverGuidanceSchemaVersion} requires authoritative instruction state.");
        var latestWorldEventId = state.Events.Count == 0 ? state.EventHistoryFloor : state.Events[^1].EventId;
        ValidateSavedInstructions(state.Instructions ?? [], state.CompletedInstructionIds ?? [], society.Checkpoint,
            state.Society.Society.WorldId, latestWorldEventId,
            state.OrderCancellations ?? [], state.WorldContent);
        ValidateBeliefEventSources(state.Society.Society.Beliefs ?? [], state.Events, state.EventHistoryFloor);
        ValidateConversationState(state, society.Checkpoint);
        AgentMarriageValidation.Validate(state, society.Checkpoint);
        ValidateBusinessTrades(state.BusinessTrades, society.Checkpoint, state.Map, society.Checkpoint.WorldTick);
        ValidateToolMakingRequests(state.ToolMakingRequests, state.WorldSimulation, state.WorldContent, society.Checkpoint, state.Inhabitants, state.BusinessTrades!, society.Checkpoint.WorldTick);
        ValidateMedicalCare(state);
        AgentKnowledgeRules.Validate(state.Knowledge, travelMap, society.Checkpoint,
            society.Checkpoint.WorldTick);
        ValidateSurvival(state);
        ValidateCouncil(state);
        foreach (var town in state.Towns ?? [])
        {
            TownGovernanceValidation.Validate(town, society.Checkpoint, state.WorldSystems!.Config.TicksPerDay);
            TownGovernmentValidation.Validate(town, society.Checkpoint, state.TownLandTitles!, state.WorldSystems!.Config.TicksPerDay);
        }
        ValidateTownAdmissions(state.Towns ?? [], society.Checkpoint, state.SchemaVersion);
        TownProjectValidation.Validate(state.Towns ?? [], society.Checkpoint, state.Map, state.WorldSimulation!, state.WorldContent!,
            state.TownLandTitles!, state.HouseholdLandUseRights!, state.HouseholdLandUseRequests!, state.Fields!, state.RoadTiles!, state.Bridges!);
        ValidatePaidMarkets(state.Towns ?? [], society.Checkpoint, state.Map, state.WorldSimulation!, state.WorldContent!,
            state.Fields!, state.HouseholdLandUseRights!, state.HouseholdLandUseRequests!, state.RoadTiles!, state.Bridges!);
        MarketTradeValidation.Validate(state.Towns ?? [], society.Checkpoint, state.Map, state.WorldSimulation!, state.WorldContent!, society.Checkpoint.WorldTick, state.Inhabitants);
        ValidateLessons(state);
        ValidateHousing(state.Inhabitants, state.Society.Society, state.SchemaVersion);
        ValidateDependentCare(state.Inhabitants, state.Society.Society, state.Towns ?? [], state.SchemaVersion);
        ValidateGuardianPlacements(state.Inhabitants, state.Society.Society, state.Towns ?? [], travelMap, state.SchemaVersion);
        ValidateDepartures(state.Inhabitants, state.Society.Society, state.SchemaVersion);
        ValidateEquipment(state.Inhabitants, state.Society.Society, state.WorldSimulation, state.WorldContent, state.SchemaVersion);
        ValidateRepairOrderBindings(state.Inhabitants, state.Society.Society.Inventory, state.Instructions ?? []);
        foreach (var person in state.Inhabitants)
        {
            if (person.LastModelAttempt is { } attempt &&
                (!CognitionProviderFailures.IsStatus(attempt.Status) ||
                 attempt.WorldTick < 0 || attempt.WorldTick > state.Society.Society.WorldTick ||
                 (attempt.LastAcceptedCandidateId is null) != (attempt.LastAcceptedTick is null) ||
                 attempt.LastAcceptedTick is < 0 || attempt.LastAcceptedTick > attempt.WorldTick ||
                 attempt.LastAcceptedCandidateId is { } candidate &&
                    // Offered action IDs can include an entire valid inventory lineage.
                    (string.IsNullOrWhiteSpace(candidate) || candidate.Any(char.IsControl)) ||
                 attempt.SetupBlocker is not (null or "unsupported_request") ||
                 attempt.SetupBlocker is not null && attempt.Status != "model_unavailable"))
                throw new InvalidDataException("The saved model attempt is invalid.");
            ValidateSavedChildModelSelection(person, state.Society.Society, state.SchemaVersion);
            ValidateProficiency(person);
            ValidateSocialStanding(person, state.Society.Society.Inhabitants.Select(item => item.Id),
                state.Society.Society.WorldTick);
            ValidatePrivateThoughts(person.RecentThoughts, state.Society.Society.WorldTick);
            AgentIdentityMoment.Validate(person.IdentityMoments, state.Society.Society.WorldTick, state.SchemaVersion);
            ValidateExploration(person.Exploration, travelMap, state.Bridges, state.Society.Society.WorldTick);
        }
        ValidateParenthood(state);
        ValidateContinuity(state.Continuity, state.Society.Society, state.SchemaVersion);
        ContentPackageRegistry.Restore(state.Content);
        WorldSystemsRules.Validate(state.WorldSystems);
        if (state.WorldSystems.WorldTick != state.Society.Society.WorldTick ||
            !string.Equals(state.WorldSystems.WorldSeed, state.WorldSeed, StringComparison.Ordinal) ||
            state.WorldSystems.Config.TicksPerDay != state.Society.Society.Config.TicksPerWorldDay ||
            state.WorldSystems.Config.DaysPerYear != state.Society.Society.Config.DaysPerWorldYear)
        {
            throw new InvalidDataException("The saved world systems do not match the society clock, calendar, or seed.");
        }
        state.WorldContent.Validate();
        WorldContentSimulationRules.Validate(state.WorldSimulation, state.WorldContent, state.Map,
            state.Society.Society.WorldTick);
        ValidateProductionOrderBindings(state.WorldSimulation, state.WorldContent, state.Inhabitants, state.Instructions ?? []);
        ValidateCustodyOrderBindings(society.Checkpoint, state.Instructions ?? []);
        ValidateDeliveryOrderBindings(state.WorldSimulation, society.Checkpoint, state.Towns, state.Instructions ?? []);
        ValidateConstructionOrderBindings(state.WorldSimulation, state.WorldContent, society.Checkpoint, state.Inhabitants,
            state.Instructions ?? [], state.Society.Society.WorldTick);
        ValidateExpansionOrderBindings(state.WorldSimulation, state.WorldContent, society.Checkpoint, state.Towns,
            state.Instructions ?? [], state.Society.Society.WorldTick);
        ValidateShelterOrderBindings(state.WorldSimulation, state.WorldContent, society.Checkpoint,
            state.Instructions ?? [], state.Society.Society.WorldTick);
        ValidateBuildingExpansionState(state.WorldSimulation, state.WorldContent, state.Society.Society,
            state.Map, state.SchemaVersion);
        ValidateBoatTransport(state);
        ValidateHandcarts(state.HandcartHitches, state.Society.Society.Inventory, state.Inhabitants, travelMap);
        ValidatePhysicalInventoryLocations(state.Society.Society.Inventory, state.WorldSimulation,
            state.WorldContent, state.Society.Society.Inhabitants, state.Map, state.Society.Society.Estates);
        ValidateFarmFields(state.Fields!.ToArray(), state.Map, state.WorldSeed, state.Society.Society,
            state.WorldSimulation, state.WorldContent, state.RoadTiles.Concat(
                state.Bridges.SelectMany(bridge => bridge.Entrances)).ToArray());
        ValidateFieldOrderBindings(state.Fields!, state.Instructions ?? []);
        if (state.WorldSimulation.Buildings.Any(building => building.HouseholdId is { } householdId &&
            !state.Society.Society.Households.Any(household => household.Id == householdId)))
            throw new InvalidDataException("A House references a missing household.");
        ValidateTowns(state.Towns, state.Map, state.FounderSetup,
            state.Society.Society, state.WorldSimulation, state.WorldContent);
        TownLandRightsRules.ValidateRecords(state.Map, state.Society.Society.WorldTick, state.Towns,
            state.TownLandTitles, state.HouseholdLandUseRights, state.HouseholdLandUseRequests,
            state.Society.Society);
        ValidateLandHearings(state.Map, society.Checkpoint, state.Towns ?? [], state.HouseholdLandUseRights!,
            state.HouseholdLandUseRequests!, state.TownLandTitles!, state.WorldSystems!.Config.TicksPerDay);
        foreach (var town in state.Towns ?? [])
            TownNonviolentValidation.Validate(state.Map, society.Checkpoint, town, state.TownLandTitles!, state.WorldSystems.Config.TicksPerDay);
        ValidateNonviolentPhysicalState(state.Map, society.Checkpoint, state.Towns ?? [], state.TownLandTitles!, state.HouseholdLandUseRights!);
        ValidateRoads(state.RoadTiles, state.Map, state.FounderSetup);
        ValidateBridges(state.Bridges, state.BridgeTraffic, travelMap,
            state.RoadTiles, state.WorldSimulation, state.WorldContent, state.Society.Society,
            state.Inhabitants);
        WorldAssetReservationLedger.Restore(state.AssetReservations);
        var activeIds = state.Society.Society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id)
            .OrderBy(item => item, StringComparer.Ordinal);
        var physicalIds = state.Inhabitants
            .Select(item => item.InhabitantId)
            .OrderBy(item => item, StringComparer.Ordinal);
        if (!activeIds.SequenceEqual(physicalIds))
        {
            throw new InvalidDataException("The saved private-world populations disagree.");
        }
        ValidateDeceasedArchive(state.DeceasedInhabitants ?? [], state.Society.Society, travelMap, state.Bridges,
            state.SchemaVersion, state.Towns ?? []);
        foreach (var inhabitant in state.Inhabitants)
        {
            if (inhabitant.Project is { } project)
            {
                ValidateProject(project, state.Society.Society.WorldTick);
            }
        }
    }

    private static void ValidateSavedInstructions(
        IReadOnlyList<OwnerQueuedInstruction> instructions,
        IReadOnlyList<string> completedInstructionIds,
        SocietyCheckpoint checkpoint,
        string worldId,
        long latestEventId,
        IReadOnlyList<OwnerOrderCancellation> cancellations,
        DeclarativeWorldContentState worldContent)
    {
        var people = checkpoint.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var instructionIds = new HashSet<string>(StringComparer.Ordinal);
        var idempotencyKeys = new HashSet<string>(StringComparer.Ordinal);
        var sequences = new HashSet<long>();
        foreach (var instruction in instructions)
        {
            if (instruction is null || string.IsNullOrWhiteSpace(instruction.InstructionId) ||
                instruction.InstructionId.Length > OwnerQueuedInstruction.MaximumIdentifierLength ||
                instruction.InstructionId != $"private-instruction-{instruction.SubmissionSequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)}" ||
                !instructionIds.Add(instruction.InstructionId) ||
                !IsValidInstructionIdentifier(instruction.IdempotencyKey) ||
                !idempotencyKeys.Add(instruction.IdempotencyKey) ||
                !IsValidInstructionIdentifier(instruction.IssuerId) ||
                !people.Contains(instruction.TargetInhabitantId) ||
                instruction.Kind is not (OwnerInstructionKind.Suggestive or OwnerInstructionKind.MustDo) ||
                instruction.State != OwnerInstructionState.Queued ||
                string.IsNullOrWhiteSpace(instruction.Text) || instruction.Text.Length > OwnerQueuedInstruction.MaximumTextLength ||
                instruction.Text != instruction.Text.Trim() || instruction.Text.Any(char.IsControl) ||
                instruction.SubmittedTick < 0 || instruction.SubmittedTick > checkpoint.WorldTick ||
                instruction.GuidancePromptedTick is { } promptedTick &&
                    (promptedTick < instruction.SubmittedTick || promptedTick > checkpoint.WorldTick) ||
                instruction.RunEpoch < 0 || instruction.RunEpoch > checkpoint.RunEpoch ||
                instruction.SubmissionSequence <= 0 || !sequences.Add(instruction.SubmissionSequence) ||
                instruction.ObservedTick is { } observedTick &&
                    (observedTick < instruction.SubmittedTick || observedTick > checkpoint.WorldTick) ||
                instruction.ObserverReply is not null &&
                    (instruction.ObservedTick is null ||
                     CognitionDecisionResponse.NormalizeObserverReply(instruction.ObserverReply) != instruction.ObserverReply) ||
                instruction.Kind == OwnerInstructionKind.Suggestive && instruction.Order is not null ||
                instruction.Kind == OwnerInstructionKind.MustDo && instruction.Order is null ||
                instruction.Order is { } order && !IsValidSavedOrder(order, instruction, completedInstructionIds,
                    people, checkpoint.WorldTick, worldContent))
                throw new InvalidDataException("The saved owner instruction or observer response is invalid.");
        }

        if (completedInstructionIds.Any(id => string.IsNullOrWhiteSpace(id) || !instructionIds.Contains(id)) ||
            completedInstructionIds.Distinct(StringComparer.Ordinal).Count() != completedInstructionIds.Count)
            throw new InvalidDataException("The completed owner instruction list is invalid.");
        var completed = completedInstructionIds.ToHashSet(StringComparer.Ordinal);
        if (instructions.Any(item => item.Kind == OwnerInstructionKind.Suggestive &&
                completed.Contains(item.InstructionId) && item.ObservedTick is null))
            throw new InvalidDataException("A suggestion cannot be completed before an agent's personal model observes it.");

        var cancellationKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cancellation in cancellations)
        {
            var instruction = cancellation is null ? null : instructions.SingleOrDefault(item =>
                item.InstructionId == cancellation.OrderId);
            if (cancellation is null || string.IsNullOrWhiteSpace(cancellation.IdempotencyKey) ||
                cancellation.IdempotencyKey.Length > 128 || cancellation.IdempotencyKey.Any(char.IsControl) ||
                cancellation.IdempotencyKey != cancellation.IdempotencyKey.Trim() ||
                !cancellationKeys.Add(cancellation.IdempotencyKey) ||
                string.IsNullOrWhiteSpace(cancellation.IssuerId) || cancellation.IssuerId.Length > 128 ||
                cancellation.IssuerId.Any(char.IsControl) || cancellation.IssuerId != cancellation.IssuerId.Trim() ||
                cancellation.WorldId != worldId ||
                cancellation.TargetInhabitantId != instruction?.TargetInhabitantId ||
                instruction?.Kind != OwnerInstructionKind.MustDo || instruction.Order is null ||
                cancellation.Receipt is null || cancellation.Receipt.OrderId != cancellation.OrderId ||
                cancellation.Receipt.Status is not ("finished" or "cancelled" or "not_understood") ||
                cancellation.Receipt.Status != instruction.Order.Status ||
                cancellation.Receipt.Changed && cancellation.Receipt.Status != "cancelled" ||
                cancellation.Receipt.WorldTick < 0 || cancellation.Receipt.WorldTick > checkpoint.WorldTick ||
                cancellation.Receipt.LatestEventId < 0 || cancellation.Receipt.LatestEventId > latestEventId)
                throw new InvalidDataException("The saved order-cancellation receipt is invalid or does not match its world, actor, and order.");
        }
    }

    private static bool IsValidSavedOrder(
        OwnerInstructionOrder order,
        OwnerQueuedInstruction instruction,
        IReadOnlyList<string> completedInstructionIds,
        HashSet<string> people,
        long worldTick,
        DeclarativeWorldContentState worldContent)
    {
        var knownStatus = order.Status is "queued" or "waiting" or "doing" or "interrupted" or "blocked" or
            "finished" or "cancelled" or "not_understood";
        var terminal = order.Status is "finished" or "cancelled" or "not_understood";
        var isCompleted = completedInstructionIds.Contains(instruction.InstructionId, StringComparer.Ordinal);
        if ((order.TargetAnimalId is not null) != IsAnimalOrder(order.Action)) return false;
        if (!knownStatus || (order.Status == "queued" && !instruction.Queue) ||
            order.BlockedReason is { Length: > 256 } || order.BlockedReason?.Any(char.IsControl) == true ||
            order.LastEffectId is { Length: > 512 } || order.LastEffectId?.Any(char.IsControl) == true ||
            order.TargetResourceId is { Length: > 128 } || order.TargetResourceId?.Any(char.IsControl) == true ||
            order.Action != "produce_item" && (order.TargetRecipeId is not null || order.TargetOutputKind is not null ||
                order.ProductionBuildingId is not null || order.ProductionJobId is not null || order.ProductionProjectStartedTick is not null) ||
            order.Action is not ("collect_goods" or "store_goods" or "return_borrowed" or "deliver_stock") && order.TargetItemKind is not null ||
            order.Action != "deliver_stock" && (order.DeliveryPurpose is not null ||
                order.DeliveryRoute is not null || order.DeliveryLotId is not null || order.DeliveryQuantity is not null) ||
            order.Action is not ("deliver_stock" or "construct_building" or "expand_building" or "seek_shelter" or "tend_fire") && order.TargetBuildingKind is not null ||
            order.Action != "construct_building" && (order.TargetDefinitionId is not null || order.ConstructionOwnerId is not null ||
                order.ConstructionPosition is not null || order.ConstructionStartedTick is not null || order.ConstructionInstanceId is not null) ||
            order.Action != "expand_building" && order.ExpansionBinding is not null ||
            order.Action is not ("seek_shelter" or "tend_fire") && (order.ShelterBinding is not null || order.ShelterCompletion is not null) ||
            !IsValidCustodyBindingShape(order) ||
            order.TargetFoodKind is not (null or "berries" or "fruit" or "wild_greens") &&
                (order.Action is not ("consume_food" or "collect_food") || !IsEdibleFood(order.TargetFoodKind)) ||
            !IsFieldOrder(order.Action) && order.TargetCropKind is not null ||
            order.Action is not ("repair_equipment" or "repair_tool" or "collect_equipment" or "store_equipment") && order.TargetEquipmentKind is not null ||
            order.Action is not ("gather_material" or "store_material" or "collect_material") && order.TargetMaterialKind is not null ||
            order.TargetPosition is { X: < -10_000_000 or > 10_000_000 } ||
            order.TargetPosition is { Y: < -10_000_000 or > 10_000_000 } ||
            order.WaitForDecisionAfterFailure && (order.Status != "blocked" || order.BlockedReason is null) ||
            order.Status == "blocked" && string.IsNullOrWhiteSpace(order.BlockedReason) ||
            terminal != isCompleted)
            return false;

        if (IsAnimalOrder(order.Action))
            return !string.IsNullOrWhiteSpace(order.TargetAnimalId) && order.TargetAnimalId.Length <= 128 &&
                !order.TargetAnimalId.Any(char.IsControl) && order.RequestedUnits == 1 && order.CompletedUnits >= 0 &&
                (order.RepeatUntilCancelled || order.CompletedUnits <= 1) && order.ProgressUnit == "animal_tasks" &&
                order.TargetResourceId is null && order.TargetAgentId is null && order.TargetPosition is null &&
                order.Status != "not_understood" &&
                (order.Status == "finished") == (!order.RepeatUntilCancelled && order.CompletedUnits == 1) &&
                (order.CompletedUnits == 0 ? order.LastEffectId is null : order.LastEffectId?.StartsWith("animal-order:", StringComparison.Ordinal) == true);
        if (order.Action == "unknown")
            return order.Status == "not_understood" && order.RequestedUnits == 0 && order.CompletedUnits == 0 &&
                order.ProgressUnit == "none" && !order.RepeatUntilCancelled && order.TargetFoodKind is null &&
                order.TargetResourceId is null && order.TargetPosition is null && order.LastEffectId is null &&
                order.TargetAgentId is null;

        if (order.Action is "seek_shelter" or "tend_fire")
            return IsValidShelterOrderShape(order, instruction, worldTick);

        if (order.Action is "construct_building" or "expand_building")
        {
            var construction = order.Action == "construct_building";
            if (!PrivateWorldBuildingOrderCatalog.Supports(order.Action, order.TargetBuildingKind) ||
                !PrivateWorldBuildingOrderCatalog.Available(worldContent).Any(item => item.BuildingKind == order.TargetBuildingKind) ||
                order.RequestedUnits != 1 || order.CompletedUnits is < 0 or > 1 || order.RepeatUntilCancelled ||
                order.ProgressUnit != (construction ? "buildings" : "expansions") ||
                order.Status == "not_understood" || (order.Status == "finished") != (order.CompletedUnits == 1) ||
                order.TargetAgentId is not null || order.TargetFoodKind is not null || order.TargetResourceId is not null ||
                (order.CompletedUnits == 0 ? order.LastEffectId is not null :
                    !IsValidCustodyReceipt(order.LastEffectId, construction ? "construction:building:" : "expand:job:")))
                return false;
            if (!construction) return order.ExpansionBinding is not null || order.CompletedUnits == 0;
            return PrivateWorldBuildingOrderCatalog.Find(worldContent, order.TargetDefinitionId)?.BuildingKind == order.TargetBuildingKind &&
                (order.ConstructionInstanceId is null
                    ? order.ConstructionOwnerId is null && order.ConstructionPosition is null &&
                        order.ConstructionStartedTick is null && order.CompletedUnits == 0
                    : IsValidProductionBindingId(order.ConstructionInstanceId) &&
                        order.ConstructionOwnerId is { } owner && IsValidProductionBindingId(owner) &&
                        order.ConstructionPosition is { X: >= -10_000_000 and <= 10_000_000, Y: >= -10_000_000 and <= 10_000_000 } site &&
                        (order.TargetPosition is null || order.TargetPosition == site) &&
                        order.ConstructionStartedTick >= instruction.SubmittedTick && order.ConstructionStartedTick <= worldTick);
        }

        if (order.Action == "deliver_stock")
            return PrivateWorldDeliveryOrderCatalog.IsValidTarget(order.DeliveryPurpose, order.TargetItemKind,
                    order.TargetBuildingKind, PrivateWorldDeliveryOrderCatalog.AvailableInputs(worldContent)) &&
                order.TargetAgentId is null && order.TargetFoodKind is null && order.TargetResourceId is null &&
                order.RequestedUnits is >= 1 and <= 1000 && order.CompletedUnits is >= 0 and <= 1_000_000 &&
                (order.RepeatUntilCancelled || order.CompletedUnits <= order.RequestedUnits) &&
                order.Status != "not_understood" &&
                (order.Status == "finished") == (!order.RepeatUntilCancelled && order.CompletedUnits >= order.RequestedUnits) &&
                (order.QuantityIsExplicit ? order.ProgressUnit == "goods_items"
                    : order.ProgressUnit == "delivery_loads" && order.RequestedUnits == 1) &&
                (order.Status != "finished" || order.DeliveryLotId is null) &&
                (order.TargetStorageBuildingId is null
                    ? order.DeliveryRoute is null && order.DeliveryLotId is null && order.DeliveryQuantity is null
                    : PrivateWorldDeliveryOrderCatalog.IsValidRoute(order.DeliveryPurpose, order.DeliveryRoute,
                        order.TargetItemKind, order.TargetBuildingKind)) &&
                (order.DeliveryLotId is null ? order.DeliveryQuantity is null :
                    !string.IsNullOrWhiteSpace(order.DeliveryLotId) && order.DeliveryLotId == order.DeliveryLotId.Trim() &&
                    !order.DeliveryLotId.Any(char.IsControl) && order.DeliveryQuantity is > 0 and <= 1_000_000 &&
                    (!order.QuantityIsExplicit || order.RepeatUntilCancelled ||
                        order.DeliveryQuantity <= order.RequestedUnits - order.CompletedUnits)) &&
                (order.CompletedUnits == 0 ? order.LastEffectId is null : IsValidCustodyReceipt(order.LastEffectId, "delivery:stock:"));

        if (order.Action is "collect_goods" or "store_goods" or "return_borrowed")
        {
            var defaultProgress = order.Action switch
            {
                "collect_goods" => "collection_loads",
                "store_goods" => "storage_loads",
                _ => "return_loads",
            };
            var receiptPrefix = order.Action switch
            {
                "collect_goods" => "collect:personal:",
                "store_goods" => "store:personal:",
                _ => "return:borrowed:",
            };
            return (order.Action == "return_borrowed"
                    ? PrivateWorldCustodyOrderCatalog.IsReturnKind(order.TargetItemKind)
                    : PrivateWorldCustodyOrderCatalog.IsGoodsKind(order.TargetItemKind)) &&
                order.TargetAgentId is null && order.TargetFoodKind is null && order.TargetResourceId is null &&
                order.RequestedUnits is >= 1 and <= 1000 && order.CompletedUnits is >= 0 and <= 1_000_000 &&
                (order.RepeatUntilCancelled || order.CompletedUnits <= order.RequestedUnits) &&
                order.Status != "not_understood" &&
                (order.Status == "finished") == (!order.RepeatUntilCancelled && order.CompletedUnits >= order.RequestedUnits) &&
                (order.QuantityIsExplicit ? order.ProgressUnit == "goods_items"
                    : order.ProgressUnit == defaultProgress && order.RequestedUnits == 1) &&
                (order.CompletedUnits == 0 ? order.LastEffectId is null : IsValidCustodyReceipt(order.LastEffectId, receiptPrefix));
        }

        if (order.Action == "produce_item")
        {
            var recipe = PrivateWorldProductionOrderCatalog.Find(worldContent, order.TargetRecipeId);
            return recipe is not null && order.TargetOutputKind == recipe.OutputKind &&
                order.TargetAgentId is null && order.TargetFoodKind is null && order.TargetResourceId is null &&
                order.RequestedUnits is >= 1 and <= 1000 && order.CompletedUnits is >= 0 and <= 1_000_000 &&
                (order.RepeatUntilCancelled || order.CompletedUnits <= order.RequestedUnits) &&
                order.Status != "not_understood" &&
                (order.Status == "finished") == (!order.RepeatUntilCancelled && order.CompletedUnits >= order.RequestedUnits) &&
                (order.ProgressUnit == "output_items"
                    ? order.QuantityIsExplicit && order.RequestedUnits % recipe.OutputQuantity == 0 &&
                        order.CompletedUnits % recipe.OutputQuantity == 0
                    : order.ProgressUnit == "production_batches" && (order.QuantityIsExplicit || order.RequestedUnits == 1)) &&
                (order.ProductionBuildingId is null ? order.ProductionProjectStartedTick is null && order.ProductionJobId is null :
                    IsValidProductionBindingId(order.ProductionBuildingId) && order.ProductionProjectStartedTick is >= 0 &&
                    order.ProductionProjectStartedTick <= worldTick &&
                    (order.ProductionJobId is null || IsValidProductionBindingId(order.ProductionJobId))) &&
                (order.CompletedUnits == 0 ? order.LastEffectId is null : IsValidProductionReceipt(order.LastEffectId));
        }

        if (IsFieldOrder(order.Action))
            return (order.Action == "till_field" ? order.TargetCropKind is null :
                    order.TargetCropKind is null ? order.Action != "plant_field" : FarmFieldRules.IsCrop(order.TargetCropKind)) &&
                order.TargetAgentId is null && order.TargetFoodKind is null && order.TargetResourceId is null &&
                order.RequestedUnits is >= 1 and <= 1000 && order.CompletedUnits is >= 0 and <= 1_000_000 &&
                (order.QuantityIsExplicit || order.RequestedUnits == 1) && order.ProgressUnit == "fields" &&
                (order.RepeatUntilCancelled || order.CompletedUnits <= order.RequestedUnits) &&
                order.Status != "not_understood" &&
                (order.Status == "finished") == (!order.RepeatUntilCancelled && order.CompletedUnits >= order.RequestedUnits) &&
                (order.CompletedUnits == 0 ? order.LastEffectId is null : order.LastEffectId?.StartsWith("field:work:", StringComparison.Ordinal) == true);

        if (order.Action is "repair_equipment" or "repair_tool")
            return (order.Action == "repair_tool" ? PrivateWorldInstructionOrderParser.IsToolKind(order.TargetEquipmentKind) :
                    PrivateWorldInstructionOrderParser.IsEquipmentKind(order.TargetEquipmentKind)) &&
                order.TargetAgentId is null && order.TargetFoodKind is null && order.TargetResourceId is null && order.TargetPosition is null &&
                order.RequestedUnits is >= 1 and <= 1000 && order.CompletedUnits is >= 0 and <= 1_000_000 &&
                (order.QuantityIsExplicit || order.RequestedUnits == 1) && order.ProgressUnit == "repairs" &&
                (order.RepeatUntilCancelled || order.CompletedUnits <= order.RequestedUnits) &&
                order.Status != "not_understood" &&
                (order.Status == "finished") == (!order.RepeatUntilCancelled && order.CompletedUnits >= order.RequestedUnits) &&
                (order.CompletedUnits == 0 ? order.LastEffectId is null : order.LastEffectId?.StartsWith(order.Action == "repair_tool" ? "repair:tool:" : "repair:equipment:", StringComparison.Ordinal) == true);

        if (order.Action is "collect_material" or "collect_food" or "collect_equipment")
            return (order.Action switch
            {
                "collect_material" => PrivateWorldInstructionOrderParser.IsMaterialKind(order.TargetMaterialKind) && order.TargetFoodKind is null,
                "collect_equipment" => (PrivateWorldInstructionOrderParser.IsEquipmentKind(order.TargetEquipmentKind) ||
                    PrivateWorldInstructionOrderParser.IsToolKind(order.TargetEquipmentKind)) && order.TargetFoodKind is null,
                _ => order.TargetMaterialKind is null,
            }) && order.TargetAgentId is null && order.TargetResourceId is null &&
                order.RequestedUnits is >= 1 and <= 1000 && order.CompletedUnits is >= 0 and <= 1_000_000 &&
                (order.RepeatUntilCancelled || order.CompletedUnits <= order.RequestedUnits) &&
                order.Status != "not_understood" &&
                (order.Status == "finished") == (!order.RepeatUntilCancelled && order.CompletedUnits >= order.RequestedUnits) &&
                (order.QuantityIsExplicit ? order.ProgressUnit == (order.Action switch
                {
                    "collect_food" => "food_items",
                    "collect_equipment" => "equipment_items",
                    _ => "material_items",
                }) : order.ProgressUnit == "collection_loads" && order.RequestedUnits == 1) &&
                (order.CompletedUnits == 0 ? order.LastEffectId is null : order.LastEffectId?.StartsWith("collect:personal:", StringComparison.Ordinal) == true);

        if (order.Action is "store_material" or "store_equipment")
            return (order.Action == "store_equipment"
                ? PrivateWorldInstructionOrderParser.IsEquipmentKind(order.TargetEquipmentKind) ||
                    PrivateWorldInstructionOrderParser.IsToolKind(order.TargetEquipmentKind)
                : PrivateWorldInstructionOrderParser.IsMaterialKind(order.TargetMaterialKind)) &&
                order.TargetAgentId is null && order.TargetFoodKind is null && order.TargetResourceId is null &&
                order.RequestedUnits is >= 1 and <= 1000 && order.CompletedUnits is >= 0 and <= 1_000_000 &&
                (order.RepeatUntilCancelled || order.CompletedUnits <= order.RequestedUnits) &&
                order.Status != "not_understood" &&
                (order.Status == "finished") == (!order.RepeatUntilCancelled && order.CompletedUnits >= order.RequestedUnits) &&
                (order.QuantityIsExplicit ? order.ProgressUnit == (order.Action == "store_equipment" ? "equipment_items" : "material_items")
                    : order.ProgressUnit == "storage_loads" && order.RequestedUnits == 1) &&
                (order.CompletedUnits == 0 ? order.LastEffectId is null : order.LastEffectId?.StartsWith("store:personal:", StringComparison.Ordinal) == true);

        if (order.Action == "gather_material")
            return PrivateWorldInstructionOrderParser.IsMaterialKind(order.TargetMaterialKind) &&
                order.TargetAgentId is null && order.TargetFoodKind is null && !(order.TargetResourceId is not null && order.TargetPosition is not null) &&
                order.RequestedUnits is >= 1 and <= 1000 && order.CompletedUnits is >= 0 and <= 1_000_000 &&
                order.Status != "not_understood" &&
                (order.Status == "finished") == (!order.RepeatUntilCancelled && order.CompletedUnits >= order.RequestedUnits) &&
                (order.QuantityIsExplicit ? order.ProgressUnit == "material_items" : order.ProgressUnit == "harvests" && order.RequestedUnits == 1) &&
                (order.CompletedUnits == 0 ? order.LastEffectId is null : order.LastEffectId?.StartsWith("gather:material:", StringComparison.Ordinal) == true);

        if (order.Action == "move_to")
            return order.TargetPosition is { } destination && order.TargetFoodKind is null && order.TargetResourceId is null &&
                order.TargetAgentId is null &&
                order.RequestedUnits == 1 && order.CompletedUnits is 0 or 1 &&
                (order.Status == "finished") == (order.CompletedUnits == 1) && order.Status != "not_understood" &&
                order.ProgressUnit == "arrivals" && !order.RepeatUntilCancelled && !order.QuantityIsExplicit &&
                order.LastEffectId == (order.CompletedUnits == 1 ? MovementOrderEffectId(destination) : null);

        if (order.Action == "accept_guardianship")
            return order.TargetAgentId is { } child && people.Contains(child) && child != instruction.TargetInhabitantId &&
                order.RequestedUnits == 1 && order.CompletedUnits is 0 or 1 &&
                (order.Status == "finished") == (order.CompletedUnits == 1) && order.Status != "not_understood" &&
                order.ProgressUnit == "guardianships" && !order.RepeatUntilCancelled && !order.QuantityIsExplicit &&
                order.TargetFoodKind is null && order.TargetResourceId is null && order.TargetPosition is null &&
                order.LastEffectId == (order.CompletedUnits == 1 ? GuardianOrderEffectId(instruction.TargetInhabitantId, child) : null);

        if (order.TargetAgentId is not null || order.Action is not ("consume_food" or "seek_food" or "harvest_food") ||
            order.RequestedUnits is < 1 or > 1000 || order.CompletedUnits is < 0 or > 1_000_000 ||
            order.Status == "finished" && (order.RepeatUntilCancelled || order.CompletedUnits < order.RequestedUnits) ||
            order.Action == "consume_food" && order.ProgressUnit != "food_items" ||
            order.Action == "seek_food" && order.ProgressUnit != "arrivals" ||
            order.Action == "harvest_food" && order.ProgressUnit is not ("harvests" or "food_items") ||
            order.Action == "harvest_food" && order.QuantityIsExplicit != (order.ProgressUnit == "food_items"))
            return false;

        return true;
    }

    private static bool IsValidShelterOrderShape(OwnerInstructionOrder order, OwnerQueuedInstruction instruction, long worldTick)
    {
        var seeking = order.Action == "seek_shelter";
        if (order.TargetAgentId is not null || order.TargetBuildingKind is not (null or "house") || order.TargetFoodKind is not null ||
            order.TargetResourceId is not null || order.RequestedUnits != 1 || order.CompletedUnits is < 0 or > 1 ||
            order.RepeatUntilCancelled || order.QuantityIsExplicit || order.ProgressUnit != (seeking ? "shelters" : "fires") ||
            order.Status == "not_understood" || (order.Status == "finished") != (order.CompletedUnits == 1) ||
            (order.CompletedUnits == 0 ? order.LastEffectId is not null :
                !IsValidCustodyReceipt(order.LastEffectId, seeking ? "shelter:arrival:" : "shelter:fire:")))
            return false;
        if (order.ShelterBinding is not { } binding)
            return order.CompletedUnits == 0 && order.ShelterCompletion is null;
        if (!IsBoundedShelterPosition(binding.Position) ||
            order.TargetPosition is { } requested && requested != binding.Position ||
            order.TargetBuildingKind == "house" && (binding.Kind != "building" || binding.OwnerId is null))
            return false;
        if (binding.Kind == "natural")
        {
            if (!seeking || binding.BuildingInstanceId is not null || binding.DefinitionId is not null ||
                binding.OwnerId is not null || binding.BuildingPosition is not null || binding.BuildingPlacedTick is not null)
                return false;
        }
        else if (binding.Kind == "building")
        {
            if (binding.BuildingInstanceId is not { } buildingId || !IsValidProductionBindingId(buildingId) ||
                binding.DefinitionId is not { } definitionId || !IsValidProductionBindingId(definitionId) ||
                binding.OwnerId is { } ownerId && !IsValidProductionBindingId(ownerId) ||
                binding.BuildingPosition is not { } anchor || !IsBoundedShelterPosition(anchor) ||
                binding.BuildingPlacedTick is not { } placed || placed < 0 || placed > worldTick)
                return false;
        }
        else return false;
        if (order.ShelterCompletion is not { } completion) return order.CompletedUnits == 0;
        return order.CompletedUnits == 1 && completion.WorldTick >= instruction.SubmittedTick && completion.WorldTick <= worldTick &&
            (binding.BuildingPlacedTick is null || binding.BuildingPlacedTick <= completion.WorldTick) &&
            IsBoundedShelterPosition(completion.Position) &&
            (seeking ? completion.Position == binding.Position && completion.FuelReservationId is null :
                !string.IsNullOrWhiteSpace(completion.FuelReservationId));
    }

    private static bool IsBoundedShelterPosition(GridPoint position) =>
        position.X is >= -10_000_000 and <= 10_000_000 && position.Y is >= -10_000_000 and <= 10_000_000;

    private static bool IsValidProductionBindingId(string id) => id.Length > 0 &&
        id == id.Trim() && !id.Any(char.IsControl);

    private static bool IsValidCustodyBindingShape(OwnerInstructionOrder order)
    {
        var stores = order.Action is "store_material" or "store_equipment" or "store_goods" or "return_borrowed" or "deliver_stock";
        if (!stores && (order.TargetStorageBuildingId is not null || order.TargetStorageOwnerId is not null ||
            order.TargetStoragePosition is not null) || order.Action != "return_borrowed" && order.TargetLotId is not null)
            return false;
        if (order.TargetStorageBuildingId is null)
            return order.TargetStorageOwnerId is null && order.TargetStoragePosition is null && order.TargetLotId is null &&
                (!stores || order.CompletedUnits == 0);
        return IsValidProductionBindingId(order.TargetStorageBuildingId) &&
            order.TargetStorageOwnerId is { } owner && IsValidProductionBindingId(owner) &&
            order.TargetStoragePosition is { X: >= -10_000_000 and <= 10_000_000, Y: >= -10_000_000 and <= 10_000_000 } location &&
            (order.TargetPosition is null || order.TargetPosition == location) &&
            (order.TargetLotId is null || !string.IsNullOrWhiteSpace(order.TargetLotId) &&
                order.TargetLotId == order.TargetLotId.Trim() && !order.TargetLotId.Any(char.IsControl));
    }

    private static bool IsValidCustodyReceipt(string? receipt, string prefix) =>
        receipt is not null && receipt.Length == prefix.Length + 64 && receipt.StartsWith(prefix, StringComparison.Ordinal) &&
        receipt.Skip(prefix.Length).All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsValidProductionReceipt(string? receipt) => receipt is { Length: 76 } &&
        receipt.StartsWith("produce:job:", StringComparison.Ordinal) &&
        receipt.Skip(12).All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateConversationState(PrivateWorldRuntimeState state, SocietyCheckpoint checkpoint)
    {
        var conversations = state.Conversations ?? [];
        var budgets = state.ConversationBudgets ?? [];
        var knownAgents = checkpoint.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var worldDay = WorldCalendarRules.FromTick(checkpoint.WorldTick, state.WorldSystems!.Config).DayIndex;
        if (conversations.Any(item => item is null) || budgets.Any(item => item is null) ||
            conversations.Count > AgentConversationRules.MaximumSavedConversations ||
            conversations.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != conversations.Count ||
            budgets.Count > knownAgents.Count ||
            budgets.Select(item => item.AgentId).Distinct(StringComparer.Ordinal).Count() != budgets.Count ||
            budgets.Any(item => !knownAgents.Contains(item.AgentId) || item.WorldDay < 0 || item.WorldDay > worldDay ||
                item.Count is < 1 or > AgentConversationRules.MaximumConversationsPerWorldDay))
            throw new InvalidDataException("The saved conversation list or daily budgets are invalid.");

        foreach (var conversation in conversations)
        {
            AgentConversationRules.Validate(conversation, checkpoint.WorldTick);
            if (!knownAgents.Contains(conversation.InitiatorId) || !knownAgents.Contains(conversation.InviteeId) ||
                conversation.Turns.Any(turn => turn.ListenerIds.Any(id => !knownAgents.Contains(id))))
                throw new InvalidDataException("A saved conversation references an unknown agent.");
        }

        var activeParticipants = conversations
            .Where(item => item.Status != AgentConversationStatus.Closed)
            .SelectMany(item => new[] { item.InitiatorId, item.InviteeId });
        if (activeParticipants.Distinct(StringComparer.Ordinal).Count() != activeParticipants.Count())
            throw new InvalidDataException("An agent cannot take part in overlapping conversations.");

        var turns = conversations.SelectMany(item => item.Turns).ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var group in (checkpoint.Beliefs ?? []).Where(item => item.SourceTurnId is not null)
                     .GroupBy(item => (item.OwnerId, item.SourceTurnId)))
        {
            var sourceTurnId = group.Key.SourceTurnId!;
            var beliefs = group.ToArray();
            if (!turns.TryGetValue(sourceTurnId, out var turn) ||
                !turn.ListenerIds.Contains(group.Key.OwnerId, StringComparer.Ordinal) ||
                beliefs.Any(item => item.SourceAgentId != turn.SpeakerId) ||
                beliefs.Count(item => item.SupersedesBeliefId is null) != 1 ||
                beliefs.Single(item => item.SupersedesBeliefId is null) is not
                { Provenance: SocietyBeliefProvenance.Hearsay } sourceBelief ||
                sourceBelief.FormedTick != turn.WorldTick || sourceBelief.Statement != turn.Text)
                throw new InvalidDataException("An agent belief references a conversation turn it did not hear.");
        }
    }

    private static SeededMap TravelMap(PrivateWorldRuntimeState state)
    {
        var bridges = state.Bridges!;
        RiverBridgeRules.ValidateSaved(bridges, state.Map, state.RoadTiles!, state.Society.Society.WorldTick);
        var decks = RiverBridgeRules.Decks(bridges);
        return RiverBridgeRules.SameDecks(state.Map.BridgeDecks, decks) ? state.Map : state.Map with { BridgeDecks = decks };
    }

    private static void ValidateSavedChildModelSelection(
        PlaytestInhabitantState person, SocietyCheckpoint society, int schemaVersion)
    {
        if (person.ChildModelSelection is not { } selection) return;
        if (schemaVersion < ChildModelSelectionSchemaVersion || !society.Births.Any(item => item.ChildId == person.InhabitantId))
            throw new InvalidDataException("A saved child model choice requires schema 33 and a recorded birth.");
        ValidateChildModelSelection(selection);
    }

    private static void ValidateDeceasedArchive(
        IEnumerable<PlaytestDeceasedInhabitantState> archive,
        SocietyCheckpoint society,
        SeededMap map,
        IEnumerable<BridgeState> bridges,
        int schemaVersion,
        IReadOnlyList<TownRuntimeState> towns)
    {
        var townIds = towns.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        // A will may name only a Town that exists; the society layer cannot see Towns.
        if (society.Estates.Any(estate => (estate.WillHeirIds ?? []).Any(id =>
                id.StartsWith("town:", StringComparison.Ordinal) && !townIds.Contains(id))))
            throw new InvalidDataException("A will names a Town that does not exist.");
        if (schemaVersion < WillHeirsSchemaVersion && (archive.Any(person => person.TownId is not null) ||
            society.Estates.Any(estate => estate.WillHeirIds is not null || estate.FinalWords is not null)))
            throw new InvalidDataException($"Wills with several heirs or final words require private-world schema {WillHeirsSchemaVersion}.");
        var archived = archive.ToArray();
        if (archived.Select(item => item.InhabitantId).Distinct(StringComparer.Ordinal).Count() != archived.Length)
            throw new InvalidDataException("The deceased inhabitant archive contains duplicate identities.");
        var archivedById = archived.ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
        if (society.Estates.Any(estate => (estate.WillHeirIds ?? []).Any(id =>
                id.StartsWith("town:", StringComparison.Ordinal) &&
                (!archivedById.TryGetValue(estate.DeceasedId, out var person) || person.TownId != id))))
            throw new InvalidDataException("A will names a Town other than the deceased person's Town.");
        var deceasedById = society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Dead)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var person in archived)
        {
            // Later construction cannot change the final position or memories
            // of a deceased person, or make impossible old steps valid.
            var deathMap = MapWithBridges(map, bridges.Where(bridge => bridge.BuiltTick <= person.DeathTick));
            if (!deceasedById.TryGetValue(person.InhabitantId, out var deceased) ||
                deceased.DeathTick != person.DeathTick || person.DeathTick < 0 || person.DeathTick > society.WorldTick ||
                person.AgeAtDeath < 0 || person.LastPhysical.InhabitantId != person.InhabitantId ||
                !deathMap.IsPassable(person.LastPhysical.Position) &&
                    !(person.BoatIdAtDeath is not null && PortNavigationRules.NavigableWater(deathMap, person.LastPhysical.Position)) ||
                person.LastPhysical.HungerBasisPoints is < 0 or > 10_000 ||
                person.LastPhysical.Equipment?.OrnamentLotId is not null ||
                person.LastPhysical.GuardianPlacement is not null ||
                person.TownId is { } townId && !townIds.Contains(townId))
                throw new InvalidDataException("The deceased inhabitant archive contains an invalid final state.");
            // Archived care-group departures now authorize personal collection.
            // Validate them at death, before they can become a permission source.
            ValidateDepartures([person.LastPhysical], society with { WorldTick = person.DeathTick }, schemaVersion);
            ValidatePrivateThoughts(person.LastPhysical.RecentThoughts, person.DeathTick);
            AgentIdentityMoment.Validate(person.LastPhysical.IdentityMoments, person.DeathTick, schemaVersion);
            ValidateSavedChildModelSelection(person.LastPhysical, society, schemaVersion);
            ValidateSkills(person.LastPhysical, schemaVersion, person.DeathTick,
                society.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal));
            ValidateExploration(person.LastPhysical.Exploration, deathMap, bridges, person.DeathTick);
            if (person.LastPhysical.Equipment is { } equipment)
                ValidateEquipmentShape(equipment, person.DeathTick, schemaVersion);
        }
    }

    private static void ValidatePrivateThoughts(
        IReadOnlyList<PlaytestPrivateThought>? thoughts, long latestTick)
    {
        if (thoughts is null) return;
        if (thoughts.Count > MaximumRecentThoughts)
            throw new InvalidDataException("The private-thought history exceeds its size limit.");
        long previousTick = -1;
        foreach (var thought in thoughts)
        {
            if (thought is null || thought.Text is null ||
                thought.WorldTick < 0 || thought.WorldTick < previousTick || thought.WorldTick > latestTick ||
                CognitionDecisionResponse.NormalizePrivateThought(thought.Text) != thought.Text)
                throw new InvalidDataException("The private-thought history contains an invalid entry.");
            previousTick = thought.WorldTick;
        }
    }

    private static string NormalizeRequiredText(string? value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }
}
