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
        ValidatePhysicalInventoryLocations(society.Checkpoint.Inventory, worldSimulation, worldContent,
            society.Checkpoint.Inhabitants);
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
        ValidateRoads(RoadTiles, map, founderSetup);
        ValidateDeceasedArchive(deceasedInhabitants.Values, society.Checkpoint, map, checkpointSchemaVersion);
        AgentKnowledgeRules.Validate(knowledge, map, society.Checkpoint, WorldTick, checkpointSchemaVersion);

        foreach (var inhabitant in inhabitants.Values)
        {
            ValidateProficiency(inhabitant, checkpointSchemaVersion);
            ValidateSocialStanding(inhabitant, society.Checkpoint.Inhabitants.Select(item => item.Id), checkpointSchemaVersion, WorldTick);
            ValidatePrivateThoughts(inhabitant.RecentThoughts, checkpointSchemaVersion, WorldTick);
            if (inhabitant.Project is { } project)
            {
                ValidateProject(project, WorldTick);
                if (checkpointSchemaVersion < 5)
                {
                    throw new InvalidDataException("Persistent projects require private-world schema 5.");
                }
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
        if (savedTowns.Count != 1)
            throw new InvalidDataException("A founder-setup world must have exactly one first Town.");
        var town = savedTowns[0];
        if (town.Id != TownBorderRules.FirstTownId || town.Name != TownBorderRules.FirstTownName ||
            town.FoundingState != (setup.Started ? "founded" : "founding") || town.FoundedTick != 0 ||
            town.OriginSite is { } origin && !map.IsBuildable(origin) ||
            town.ResidentIds is null || town.AssignedBuildingIds is null || town.BorderTiles is null ||
            town.ResidentIds.Distinct(StringComparer.Ordinal).Count() != town.ResidentIds.Count ||
            town.AssignedBuildingIds.Distinct(StringComparer.Ordinal).Count() != town.AssignedBuildingIds.Count ||
            town.BorderTiles.Distinct().Count() != town.BorderTiles.Count || town.BorderTiles.Count == 0)
            throw new InvalidDataException("The first Town identity, founding state, or membership is invalid.");

        var active = society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active)
            .Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        if (town.ResidentIds.Any(id => !active.Contains(id)) ||
            setup.FounderIds.Any(id => active.Contains(id) && !town.ResidentIds.Contains(id, StringComparer.Ordinal)))
            throw new InvalidDataException("Town residents must be active inhabitants and active founders retain their founding membership.");

        var byInstance = simulation.Buildings.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        var assignedIds = simulation.Buildings.Where(item => item.TownId == town.Id)
            .Select(item => item.InstanceId).Order(StringComparer.Ordinal).ToArray();
        if (!town.AssignedBuildingIds.Order(StringComparer.Ordinal).SequenceEqual(assignedIds) ||
            simulation.Buildings.Any(item => item.TownId is not null && item.TownId != town.Id))
            throw new InvalidDataException("Town building assignments disagree with the placed-building state.");

        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        foreach (var buildingId in town.AssignedBuildingIds)
            if (!byInstance.ContainsKey(buildingId))
                throw new InvalidDataException("A Town references a building that is not placed.");
        var expected = TownBorderRules.ExpectedBorder(map, town, simulation.Buildings, definitions);
        if (!town.BorderTiles.OrderBy(point => point.Y).ThenBy(point => point.X)
            .SequenceEqual(expected.OrderBy(point => point.Y).ThenBy(point => point.X)) ||
            town.BorderTiles.Any(point => !map.Contains(point)))
            throw new InvalidDataException("The saved Town border does not match its founding area and assigned buildings.");
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
        IReadOnlyList<SocietyInhabitant> inhabitants)
    {
        var buildings = simulation.Buildings.ToDictionary(item => item.InstanceId, StringComparer.Ordinal);
        var definitions = content.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var people = inhabitants.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var lot in inventory.Lots)
        {
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

    internal static void ValidateStateForCodec(PrivateWorldRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion < 10 && (state.Society.Society.LifeClock is not null ||
            state.Society.Society.Inhabitants.Any(person => person.BirthLifeTick is not null)))
        {
            throw new InvalidDataException("Biological life pacing requires private-world schema 10.");
        }
        if (state.SchemaVersion is < 1 or > StateSchemaVersion || string.IsNullOrWhiteSpace(state.WorldSeed) || state.EventHistoryFloor < 0)
        {
            throw new InvalidDataException("The private-world runtime state schema or seed is invalid.");
        }
        if (state.SchemaVersion < 20 && state.Society.Society.Beliefs is { Count: > 0 })
            throw new InvalidDataException("Agent belief history requires private-world schema 20.");
        if (state.SchemaVersion < 22 && state.Society.Society.MemoryCompactions is { Count: > 0 })
            throw new InvalidDataException("Agent memory compaction indexes require private-world schema 22.");
        if (state.SchemaVersion < 24 && state.RoadTiles is { Count: > 0 })
            throw new InvalidDataException("Generated Roads require private-world schema 24.");
        if (state.JevPolicyRevision < 0 || state.JevEnabled is null && state.JevPolicyRevision != 0 ||
            state.SchemaVersion < 15 && (state.JevEnabled is not null || state.JevPolicyRevision != 0))
            throw new InvalidDataException("The saved Jev routing policy is invalid.");
        if (state.SchemaVersion < 16 && state.FounderSetup is not null)
            throw new InvalidDataException("Founder setup requires private-world schema 16.");
        if (state.Geography is not null &&
            (state.SchemaVersion < 17 || state.FounderSetup is null ||
             !string.Equals(state.Geography.Seed, state.WorldSeed, StringComparison.Ordinal)))
            throw new InvalidDataException("Generated geography does not match the saved world setup.");
        ValidateFounderSetup(state.FounderSetup, state.Society.Society);
        if (state.SchemaVersion >= 21 && state.Towns is null)
            throw new InvalidDataException("Private-world schema 21 requires authoritative Town state.");
        if (state.SchemaVersion >= 23 && state.Knowledge is null)
            throw new InvalidDataException("Private-world schema 23 requires agent map-knowledge state.");
        if (state.SchemaVersion >= 24 && state.RoadTiles is null)
            throw new InvalidDataException("Private-world schema 24 requires authoritative Road state.");
        var hasArchivedEvents = state.EventHistoryFloor > 0 || state.Society.Society.EventHistoryFloor > 0 ||
            state.Society.Society.Inventory.EventHistoryFloor > 0 || state.Society.Cognition.EventHistoryFloor > 0 ||
            state.Society.Cognition.Runtimes.Any(runtime => runtime.EventHistoryFloor > 0);
        if ((hasArchivedEvents && state.HistoryArchiveHead is null) ||
            (state.SchemaVersion < 4 && (hasArchivedEvents || state.HistoryArchiveHead is not null)) ||
            (state.HistoryArchiveHead is { } head && (head.Length != 64 || head.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))))
        {
            throw new InvalidDataException("The private-world history reference or schema is invalid.");
        }

        if (!MapAcceptance.Validate(state.Map, allowEmptyCamp: state.FounderSetup is not null).IsValid)
        {
            throw new InvalidDataException("The private-world runtime contains an invalid map.");
        }

        using var society = SocietyWorldRuntime.Restore(state.Society);
        ValidateBeliefEventSources(state.Society.Society.Beliefs ?? [], state.Events, state.EventHistoryFloor);
        AgentKnowledgeRules.Validate(state.Knowledge, state.Map, society.Checkpoint,
            society.Checkpoint.WorldTick, state.SchemaVersion);
        ValidateSurvival(state);
        ValidateCouncil(state);
        ValidateLessons(state);
        foreach (var person in state.Inhabitants)
        {
            ValidateProficiency(person, state.SchemaVersion);
            ValidateSocialStanding(person, state.Society.Society.Inhabitants.Select(item => item.Id),
                state.SchemaVersion, state.Society.Society.WorldTick);
            ValidatePrivateThoughts(person.RecentThoughts, state.SchemaVersion, state.Society.Society.WorldTick);
            ValidateExploration(person.Exploration, state.Map, state.Society.Society.WorldTick);
        }
        ValidateParenthood(state);
        ContentPackageRegistry.Restore(state.Content);
        if (state.SchemaVersion >= 3 && state.Content is null)
        {
            throw new InvalidDataException("The current private-world schema requires content governance state.");
        }

        if (state.WorldSystems is not null)
        {
            WorldSystemsRules.Validate(state.WorldSystems);
            if (state.WorldSystems.WorldTick != state.Society.Society.WorldTick ||
                !string.Equals(state.WorldSystems.WorldSeed, state.WorldSeed, StringComparison.Ordinal) ||
                state.WorldSystems.Config.TicksPerDay != state.Society.Society.Config.TicksPerWorldDay ||
                state.WorldSystems.Config.DaysPerYear != state.Society.Society.Config.DaysPerWorldYear)
            {
                throw new InvalidDataException("The saved world systems do not match the society clock, calendar, or seed.");
            }
        }
        else if (state.SchemaVersion >= 3)
        {
            throw new InvalidDataException("The current private-world schema requires richer-systems state.");
        }

        state.WorldContent?.Validate();
        if (state.SchemaVersion >= 3 &&
            (state.WorldContent is null || state.WorldSimulation is null || state.AssetReservations is null))
        {
            throw new InvalidDataException(
                "The current private-world schema requires content simulation and world asset reservation state.");
        }

        if (state.WorldContent is not null && state.WorldSimulation is not null)
        {
            WorldContentSimulationRules.Validate(
                state.WorldSimulation,
                state.WorldContent,
                state.Map,
                state.Society.Society.WorldTick);
            ValidatePhysicalInventoryLocations(state.Society.Society.Inventory, state.WorldSimulation,
                state.WorldContent, state.Society.Society.Inhabitants);
            if (state.WorldSimulation.Buildings.Any(building => building.HouseholdId is { } householdId &&
                !state.Society.Society.Households.Any(household => household.Id == householdId)))
                throw new InvalidDataException("A House references a missing household.");
            ValidateTowns(state.Towns ?? MigrateTowns(state), state.Map, state.FounderSetup,
                state.Society.Society, state.WorldSimulation, state.WorldContent);
            ValidateRoads(state.RoadTiles ?? [], state.Map, state.FounderSetup);
        }

        if (state.AssetReservations is not null)
        {
            WorldAssetReservationLedger.Restore(state.AssetReservations);
        }
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
        ValidateDeceasedArchive(state.DeceasedInhabitants ?? [], state.Society.Society, state.Map, state.SchemaVersion);
        foreach (var inhabitant in state.Inhabitants)
        {
            if (inhabitant.Project is { } project)
            {
                if (state.SchemaVersion < 5)
                {
                    throw new InvalidDataException("Settlement projects require save schema 5.");
                }
                ValidateProject(project, state.Society.Society.WorldTick);
            }
        }
    }

    private static void ValidateDeceasedArchive(
        IEnumerable<PlaytestDeceasedInhabitantState> archive,
        SocietyCheckpoint society,
        SeededMap map,
        int schemaVersion)
    {
        var archived = archive.ToArray();
        if (archived.Length > 0 && schemaVersion < 13)
            throw new InvalidDataException("Deceased inhabitant archives require private-world schema 13.");
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
                person.LastPhysical.HungerBasisPoints is < 0 or > 10_000)
                throw new InvalidDataException("The deceased inhabitant archive contains an invalid final state.");
            ValidatePrivateThoughts(person.LastPhysical.RecentThoughts, schemaVersion, person.DeathTick);
            ValidateExploration(person.LastPhysical.Exploration, map, person.DeathTick);
        }
    }

    private static void ValidatePrivateThoughts(
        IReadOnlyList<PlaytestPrivateThought>? thoughts, int schemaVersion, long latestTick)
    {
        if (thoughts is null) return;
        if (schemaVersion < 14 || thoughts.Count > MaximumRecentThoughts)
            throw new InvalidDataException("The private-thought history version or size is invalid.");
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
