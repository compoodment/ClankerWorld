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
        ValidateMedicalCare(inhabitants.Values, deceasedInhabitants.Values, society.Checkpoint);
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
        ValidatePhysicalInventoryLocations(society.Checkpoint.Inventory, worldSimulation, worldContent,
            society.Checkpoint.Inhabitants, map);
        ValidateFarmFields(fields.ToArray(), map, worldSeed, society.Checkpoint, worldSimulation, worldContent, RoadAndBridgeTiles().ToArray());
        if (worldSimulation.Buildings.Any(building => building.HouseholdId is { } householdId &&
            !society.Checkpoint.Households.Any(household => household.Id == householdId)))
            throw new InvalidDataException("A House references a missing household.");
        var inventoryReservationIds = society.Checkpoint.Inventory.Reservations
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var job in worldSimulation.ProductionJobs
                     .Concat(worldSimulation.CropBuilds ?? [])
                     .Where(item => item.State == WorldProductionJobState.Running))
        {
            if (job.InputReservationIds.Any(id => !inventoryReservationIds.Contains(id)))
            {
                throw new InvalidDataException($"Production job '{job.JobId}' has a missing inventory reservation.");
            }
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
        ValidateRoads(RoadTiles, map, founderSetup);
        ValidateBridges(Bridges, bridgeTraffic, map, RoadTiles, worldSimulation, worldContent,
            society.Checkpoint, inhabitants.Values);
        if (!RiverBridgeRules.SameDecks(map.BridgeDecks, RiverBridgeRules.Decks(bridges)))
            throw new InvalidDataException("The passable bridge decks do not match the saved bridges.");
        ValidatePlantedTrees();
        ValidateDeceasedArchive(deceasedInhabitants.Values, society.Checkpoint, map, checkpointSchemaVersion);
        AgentKnowledgeRules.Validate(knowledge, map, society.Checkpoint, WorldTick);
        ValidateHousing(inhabitants.Values, society.Checkpoint, checkpointSchemaVersion);
        ValidateEquipment(inhabitants.Values, society.Checkpoint, worldSimulation, worldContent, checkpointSchemaVersion);

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
            if (!map.IsPassable(inhabitant.Position) ||
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
                if (WorldContentSimulationRules.Footprint(definition, building).Any(tile => !border.Contains(tile)) &&
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
        IReadOnlyList<SocietyInhabitant> inhabitants, SeededMap map)
    {
        var buildings = simulation.Buildings.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var people = inhabitants.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var lot in inventory.Lots)
        {
            if (lot.GroundPosition is { } ground && (!map.Contains(new(ground.X, ground.Y)) ||
                lot.StorageBuildingId is not null || lot.DeliveryBuildingId is not null))
                throw new InvalidDataException($"Inventory lot '{lot.Id}' has an invalid ground location.");
            if (lot.StorageBuildingId is { } storageId)
            {
                if (!buildings.TryGetValue(storageId, out var storage) ||
                    !definitions.TryGetValue(storage.DefinitionId, out var definition) ||
                    !(storage.HouseholdId == lot.OwnerId && definition.Tags.Any(IsHouseholdBuildingTag) ||
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
        if (state.JevPolicyRevision < 0 || state.JevEnabled is null && state.JevPolicyRevision != 0)
            throw new InvalidDataException("The saved Jev routing policy is invalid.");
        if (state.Geography is not null &&
            (state.FounderSetup is null ||
             !string.Equals(state.Geography.Seed, state.WorldSeed, StringComparison.Ordinal)))
            throw new InvalidDataException("Generated geography does not match the saved world setup.");
        // New worlds can only be created at these sizes, so any other saved size is damage.
        if (state.Geography is { Size: not (WorldSizePreset.Small or WorldSizePreset.Medium) })
            throw new InvalidDataException("Only Small and Medium worlds can be loaded.");
        ValidateFounderSetup(state.FounderSetup, state.Society.Society);
        if (state.Towns is null || state.Knowledge is null || state.RoadTiles is null ||
            state.Bridges is null || state.BridgeTraffic is null || state.TownLandTitles is null ||
            state.HouseholdLandUseRights is null || state.HouseholdLandUseRequests is null)
            throw new InvalidDataException("The current private-world checkpoint is missing required Town, land-rights, map-knowledge, Road or bridge state.");
        if (state.Content is null || state.WorldSystems is null || state.WorldContent is null ||
            state.WorldSimulation is null || state.AssetReservations is null)
            throw new InvalidDataException("The current private-world checkpoint is missing required content or world-system state.");
        if (state.SchemaVersion < ToolProgressionSchemaVersion && HasSavedToolUseState(state))
            throw new InvalidDataException($"Saved tool use links require private-world schema {ToolProgressionSchemaVersion}.");
        if (state.SchemaVersion >= ConversationSchemaVersion && (state.Conversations is null || state.ConversationBudgets is null))
            throw new InvalidDataException($"Private-world schema {ConversationSchemaVersion} requires conversation state and daily budgets.");
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
        ValidateSavedInstructions(state.Instructions ?? [], state.CompletedInstructionIds ?? [], society.Checkpoint);
        ValidateBeliefEventSources(state.Society.Society.Beliefs ?? [], state.Events, state.EventHistoryFloor);
        ValidateConversationState(state, society.Checkpoint);
        ValidateBusinessTrades(state.BusinessTrades, society.Checkpoint, state.Map, society.Checkpoint.WorldTick);
        ValidateMedicalCare(state);
        AgentKnowledgeRules.Validate(state.Knowledge, travelMap, society.Checkpoint,
            society.Checkpoint.WorldTick);
        ValidateSurvival(state);
        ValidateCouncil(state);
        ValidateLessons(state);
        ValidateHousing(state.Inhabitants, state.Society.Society, state.SchemaVersion);
        ValidateEquipment(state.Inhabitants, state.Society.Society, state.WorldSimulation, state.WorldContent, state.SchemaVersion);
        foreach (var person in state.Inhabitants)
        {
            if (person.LastModelAttempt is { } attempt &&
                (!CognitionProviderFailures.IsStatus(attempt.Status) ||
                 attempt.WorldTick < 0 || attempt.WorldTick > state.Society.Society.WorldTick ||
                 (attempt.LastAcceptedCandidateId is null) != (attempt.LastAcceptedTick is null) ||
                 attempt.LastAcceptedTick is < 0 || attempt.LastAcceptedTick > attempt.WorldTick ||
                 attempt.LastAcceptedCandidateId is { } candidate &&
                    (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 512 || candidate.Any(char.IsControl)) ||
                 attempt.SetupBlocker is not (null or "unsupported_request") ||
                 attempt.SetupBlocker is not null && attempt.Status != "model_unavailable"))
                throw new InvalidDataException("The saved model attempt is invalid.");
            ValidateSavedChildModelSelection(person, state.Society.Society, state.SchemaVersion);
            ValidateProficiency(person);
            ValidateSocialStanding(person, state.Society.Society.Inhabitants.Select(item => item.Id),
                state.Society.Society.WorldTick);
            ValidatePrivateThoughts(person.RecentThoughts, state.Society.Society.WorldTick);
            AgentIdentityMoment.Validate(person.IdentityMoments, state.Society.Society.WorldTick, state.SchemaVersion);
            ValidateExploration(person.Exploration, travelMap, state.Society.Society.WorldTick);
        }
        ValidateParenthood(state);
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
        ValidateBuildingExpansionState(state.WorldSimulation, state.WorldContent, state.Society.Society,
            state.Map, state.SchemaVersion);
        ValidatePhysicalInventoryLocations(state.Society.Society.Inventory, state.WorldSimulation,
            state.WorldContent, state.Society.Society.Inhabitants, state.Map);
        ValidateFarmFields(state.Fields!.ToArray(), state.Map, state.WorldSeed, state.Society.Society,
            state.WorldSimulation, state.WorldContent, state.RoadTiles.Concat(
                state.Bridges.SelectMany(bridge => bridge.Entrances)).ToArray());
        if (state.WorldSimulation.Buildings.Any(building => building.HouseholdId is { } householdId &&
            !state.Society.Society.Households.Any(household => household.Id == householdId)))
            throw new InvalidDataException("A House references a missing household.");
        ValidateTowns(state.Towns, state.Map, state.FounderSetup,
            state.Society.Society, state.WorldSimulation, state.WorldContent);
        TownLandRightsRules.ValidateRecords(state.Map, state.Society.Society.WorldTick, state.Towns,
            state.TownLandTitles, state.HouseholdLandUseRights, state.HouseholdLandUseRequests,
            state.Society.Society);
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
        ValidateDeceasedArchive(state.DeceasedInhabitants ?? [], state.Society.Society, travelMap, state.SchemaVersion);
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
        SocietyCheckpoint checkpoint)
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
                     CognitionDecisionResponse.NormalizeObserverReply(instruction.ObserverReply) != instruction.ObserverReply))
                throw new InvalidDataException("The saved owner instruction or observer response is invalid.");
        }

        if (completedInstructionIds.Any(id => string.IsNullOrWhiteSpace(id) || !instructionIds.Contains(id)) ||
            completedInstructionIds.Distinct(StringComparer.Ordinal).Count() != completedInstructionIds.Count)
            throw new InvalidDataException("The completed owner instruction list is invalid.");
        var completed = completedInstructionIds.ToHashSet(StringComparer.Ordinal);
        if (instructions.Any(item => item.Kind == OwnerInstructionKind.Suggestive &&
                completed.Contains(item.InstructionId) && item.ObservedTick is null))
            throw new InvalidDataException("A suggestion cannot be completed before an agent's personal model observes it.");
    }

    private static void ValidateConversationState(PrivateWorldRuntimeState state, SocietyCheckpoint checkpoint)
    {
        var conversations = state.Conversations ?? [];
        var budgets = state.ConversationBudgets ?? [];
        var knownAgents = checkpoint.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var worldDay = checkpoint.Config.TicksPerWorldDay <= 0
            ? 0
            : checkpoint.WorldTick / checkpoint.Config.TicksPerWorldDay;
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
        int schemaVersion)
    {
        var archived = archive.ToArray();
        if (archived.Select(item => item.InhabitantId).Distinct(StringComparer.Ordinal).Count() != archived.Length)
            throw new InvalidDataException("The deceased inhabitant archive contains duplicate identities.");
        var deceasedById = society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Dead)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var person in archived)
        {
            if (!deceasedById.TryGetValue(person.InhabitantId, out var deceased) ||
                deceased.DeathTick != person.DeathTick || person.DeathTick < 0 || person.DeathTick > society.WorldTick ||
                person.AgeAtDeath < 0 || person.LastPhysical.InhabitantId != person.InhabitantId ||
                !map.IsPassable(person.LastPhysical.Position) ||
                person.LastPhysical.HungerBasisPoints is < 0 or > 10_000 ||
                person.LastPhysical.Equipment?.OrnamentLotId is not null)
                throw new InvalidDataException("The deceased inhabitant archive contains an invalid final state.");
            ValidatePrivateThoughts(person.LastPhysical.RecentThoughts, person.DeathTick);
            AgentIdentityMoment.Validate(person.LastPhysical.IdentityMoments, person.DeathTick, schemaVersion);
            ValidateSavedChildModelSelection(person.LastPhysical, society, schemaVersion);
            ValidateSkills(person.LastPhysical, schemaVersion, person.DeathTick,
                society.Inhabitants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal));
            ValidateExploration(person.LastPhysical.Exploration, map, person.DeathTick);
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
