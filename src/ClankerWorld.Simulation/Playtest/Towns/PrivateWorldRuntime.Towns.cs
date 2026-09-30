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
    public string PlaceFounder(string founderId, GridPoint position)
    {
        gate.Wait();
        try
        {
            ValidateFounderPlacementUnsafe(founderId, position);
            var setup = founderSetup!;

            var ordinal = setup.FounderIds.Count + 1;
            var householdId = ordinal <= 2 ? HouseholdId : SecondHouseholdId;
            var name = $"Founder {ordinal}";
            var founder = SocietyFixture.CreateFounder(founderId, name, config: society.Checkpoint.Config);
            society.Apply(checkpoint => SocietyFixture.PlaceFounder(checkpoint, founder, householdId));
            inhabitants.Add(founderId, new PlaytestInhabitantState(founderId, position, 6_500, 0,
                "undecided", "find a purpose"));
            founderSetup = setup with { FounderIds = [.. setup.FounderIds, founderId] };
            AddTownResident(TownBorderRules.FirstTownId, founderId, "founder_joined");
            AppendEvent("founder_placed", $"{founderId}:{householdId}");
            return householdId;
        }
        finally
        {
            gate.Release();
        }
    }

    public bool MoveFounder(string founderId, GridPoint position)
    {
        gate.Wait();
        try
        {
            if (founderSetup is not { Started: false } setup || !society.Checkpoint.IsPaused ||
                WorldTick != 0)
                throw new InvalidOperationException("Founders can only be moved during initial paused setup.");
            if (string.IsNullOrWhiteSpace(founderId) || !setup.FounderIds.Contains(founderId, StringComparer.Ordinal) ||
                !inhabitants.TryGetValue(founderId, out var founder))
                throw new ArgumentException("Choose a placed founder to move.", nameof(founderId));
            if (!map.IsBuildable(position) || map.CampObjects.Any(item => item.Position == position) ||
                map.Resources.Any(item => item.Position == position) ||
                inhabitants.Values.Any(person => person.InhabitantId != founderId && person.Position == position))
                throw new ArgumentException("Choose an empty passable tile for this founder.", nameof(position));
            if (founder.Position == position) return false;
            inhabitants[founderId] = founder with { Position = position };
            AppendEvent("founder_moved", $"{founderId}:{founder.Position.X},{founder.Position.Y}->{position.X},{position.Y}");
            return true;
        }
        finally { gate.Release(); }
    }

    public int UndoLastFounder(string founderId)
    {
        gate.Wait();
        try
        {
            if (founderSetup is not { Started: false } setup || !society.Checkpoint.IsPaused ||
                WorldTick != 0 || setup.FounderIds.Count == 0)
                throw new InvalidOperationException("Founder placement can only be undone during initial paused setup.");
            if (setup.FounderIds[^1] != founderId)
                throw new ArgumentException("Only the most recently placed founder can be undone.", nameof(founderId));
            society.Apply(checkpoint => SocietyFixture.UndoFounderPlacement(checkpoint, founderId));
            inhabitants.Remove(founderId);
            founderSetup = setup with { FounderIds = setup.FounderIds.Take(setup.FounderIds.Count - 1).ToArray() };
            RemoveTownResident(founderId);
            AppendEvent("founder_placement_undone", founderId);
            return founderSetup.FounderIds.Count;
        }
        finally { gate.Release(); }
    }

    public void ValidateFounderPlacement(string founderId, GridPoint position)
    {
        gate.Wait();
        try { ValidateFounderPlacementUnsafe(founderId, position); }
        finally { gate.Release(); }
    }

    private void ValidateFounderPlacementUnsafe(string founderId, GridPoint position)
    {
        if (founderSetup is not { Started: false } setup || !society.Checkpoint.IsPaused ||
            WorldTick != 0 || setup.FounderIds.Count >= RequiredFounders)
            throw new InvalidOperationException("Founders can only be placed during the initial paused setup.");
        if (geographyOptions is not null && setup.FounderIds.Count == 0 &&
            contentRegistry.ExportState().Packages.Any(package =>
                package.Manifest.PackageId == StarterContent.PackageId && package.ActivationTick == 0) &&
            !towns.Any(item => item.Id == TownBorderRules.FirstTownId && item.OriginSite is not null))
            throw new InvalidOperationException("Choose the first Town site before placing founders.");
        if (founderId is null || !founderId.StartsWith("founder:", StringComparison.Ordinal) ||
            !Guid.TryParseExact(founderId["founder:".Length..], "N", out _) ||
            inhabitants.ContainsKey(founderId))
            throw new ArgumentException("The founder ID is invalid or already used.", nameof(founderId));
        if (!map.IsBuildable(position) || map.CampObjects.Any(item => item.Position == position) ||
            map.Resources.Any(item => item.Position == position) ||
            inhabitants.Values.Any(person => person.Position == position))
            throw new ArgumentException("Choose an empty passable tile for this founder.", nameof(position));
    }

    public string? AddAgent(string agentId, GridPoint position)
    {
        gate.Wait();
        try
        {
            ValidateAgentPlacementUnsafe(agentId, position);
            var town = towns.SingleOrDefault(item => item.BorderTiles.Contains(position));
            // The Town border gives residency, not household membership.
            // Only recorded household property forces an existing household;
            // outside every Town, the adult begins an independent one.
            var householdId = HouseholdPropertyAt(position) ??
                (town is null ? "household:" + agentId : null);
            society.Apply(checkpoint => SocietyFixture.AddAdult(checkpoint, agentId, householdId));
            inhabitants.Add(agentId, new PlaytestInhabitantState(agentId, position, 6_500, 0,
                "undecided", "find a purpose"));
            if (town is not null) AddTownResident(town.Id, agentId, "agent_joined");
            else AppendEvent("town_membership_evaluated", $"{agentId}:unaffiliated");
            AppendEvent("agent_added", agentId);
            return householdId;
        }
        finally { gate.Release(); }
    }

    public void ValidateAgentPlacement(string agentId, GridPoint position)
    {
        gate.Wait();
        try { ValidateAgentPlacementUnsafe(agentId, position); }
        finally { gate.Release(); }
    }

    private void ValidateAgentPlacementUnsafe(string agentId, GridPoint position)
    {
        if (founderSetup is not { Started: true })
            throw new InvalidOperationException("Start the world with four founders before adding more agents.");
        if (agentId is null || !agentId.StartsWith("agent:", StringComparison.Ordinal) ||
            !Guid.TryParseExact(agentId["agent:".Length..], "N", out _) ||
            society.Checkpoint.Inhabitants.Any(person => person.Id == agentId))
            throw new ArgumentException("The agent ID is invalid or already used.", nameof(agentId));
        if (!map.IsBuildable(position) || map.CampObjects.Any(item => item.Position == position) ||
            map.Resources.Any(item => item.Position == position) ||
            (inhabitants.Values.Any(person => person.Position == position) && !IsHouseAt(position)))
            throw new ArgumentException("Choose an empty passable tile for this agent.", nameof(position));
        if (towns.Count(item => item.BorderTiles.Contains(position)) > 1)
            throw new InvalidOperationException("Overlapping Town borders cannot determine starting membership.");
        _ = HouseholdPropertyAt(position);
    }

    private string? HouseholdPropertyAt(GridPoint position)
    {
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var owners = worldSimulation.Buildings.Where(building => building.HouseholdId is not null &&
                definitions.TryGetValue(building.DefinitionId, out var definition) &&
                WorldContentSimulationRules.Footprint(definition, building.Position).Contains(position))
            .Select(building => building.HouseholdId!)
            .Distinct(StringComparer.Ordinal)
            .Take(2).ToArray();
        if (owners.Length > 1)
            throw new InvalidOperationException("Overlapping household property cannot determine starting membership.");
        return owners.FirstOrDefault();
    }

    private bool IsHouseAt(GridPoint position)
    {
        var definitions = worldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        return worldSimulation.Buildings.Any(building =>
            definitions.TryGetValue(building.DefinitionId, out var definition) &&
            definition.Tags.Contains("house", StringComparer.Ordinal) &&
            WorldContentSimulationRules.Footprint(definition, building.Position).Contains(position));
    }

    public bool RenameAgent(string agentId, string name)
    {
        gate.Wait();
        try
        {
            if (founderSetup is null || !society.Checkpoint.Inhabitants.Any(person => person.Id == agentId))
                throw new ArgumentException("Choose an agent in this world.", nameof(agentId));
            var result = society.Apply(checkpoint => SocietyFixture.RenameInhabitant(checkpoint, agentId, name));
            var changed = result.NewEvents is { Count: > 0 };
            if (changed) AppendEvent("agent_renamed", agentId);
            return changed;
        }
        finally { gate.Release(); }
    }

    public void StartWorld() => StartWorld(resume: true);

    public void StartWorld(bool resume)
    {
        gate.Wait();
        try
        {
            if (founderSetup is not { Started: false } setup || setup.FounderIds.Count != RequiredFounders ||
                !society.Checkpoint.IsPaused || WorldTick != 0)
                throw new InvalidOperationException("Place and configure all four founders before starting time.");
            founderSetup = setup with { Started = true };
            if (towns.SingleOrDefault(item => item.Id == TownBorderRules.FirstTownId) is { } firstTown)
            {
                SetTown(firstTown with { FoundingState = "founded" });
                AppendEvent("town_founded", $"{firstTown.Id}:residents:{firstTown.ResidentIds.Count}");
            }
            if (resume) society.Resume();
            AppendEvent("world_started", "four_founders_ready");
        }
        finally
        {
            gate.Release();
        }
    }

    private void AddTownResident(string townId, string residentId, string reason)
    {
        var town = towns.SingleOrDefault(item => item.Id == townId)
            ?? throw new InvalidOperationException("The resident's Town does not exist.");
        if (town.ResidentIds.Contains(residentId, StringComparer.Ordinal)) return;
        SetTown(town with
        {
            ResidentIds = town.ResidentIds.Append(residentId).Order(StringComparer.Ordinal).ToArray(),
        });
        var updated = towns.Single(item => item.Id == townId);
        AppendEvent("town_resident_joined", $"{updated.Id}:{residentId}:{reason}:residents:{updated.ResidentIds.Count}");
    }

    private void RemoveTownResident(string residentId)
    {
        var town = towns.SingleOrDefault(item => item.ResidentIds.Contains(residentId, StringComparer.Ordinal));
        if (town is null) return;
        var residents = town.ResidentIds.Where(id => id != residentId).ToArray();
        SetTown(town with { ResidentIds = residents });
        AppendEvent("town_resident_left", $"{town.Id}:{residentId}:residents:{residents.Length}");
    }

    private string? TownForResident(string residentId) => towns
        .SingleOrDefault(item => item.ResidentIds.Contains(residentId, StringComparer.Ordinal))?.Id;

    private TownRuntimeState? TownForOwnerPlacement(GridPoint position, BuildingDefinition definition) => towns
        .SingleOrDefault(item => TownBorderRules.IsWithinOrAdjacent(item, position, definition.Width, definition.Height));

    private void AssignBuildingToTown(PlacedBuilding building, BuildingDefinition definition)
    {
        var town = towns.SingleOrDefault(item => item.Id == building.TownId)
            ?? throw new InvalidOperationException("The assigned Town does not exist.");
        if (town.AssignedBuildingIds.Contains(building.InstanceId, StringComparer.Ordinal))
            throw new InvalidDataException("A placed building is already assigned to its Town.");
        var border = TownBorderRules.ExpandForBuilding(map, town, building.Position, definition.Width, definition.Height);
        SetTown(town with
        {
            AssignedBuildingIds = town.AssignedBuildingIds.Append(building.InstanceId)
                .Order(StringComparer.Ordinal).ToArray(),
            BorderTiles = border,
        });
        var updated = towns.Single(item => item.Id == town.Id);
        AppendEvent("town_building_assigned", $"{town.Id}:{building.InstanceId}:buildings:{updated.AssignedBuildingIds.Count}");
        if (!town.BorderTiles.SequenceEqual(border))
            AppendEvent("town_border_expanded", $"{town.Id}:{building.InstanceId}:tiles:{border.Count}");
        GenerateRoadToBuilding(building);
    }

    private void SetTown(TownRuntimeState updated)
    {
        var index = towns.FindIndex(item => item.Id == updated.Id);
        if (index < 0) throw new InvalidOperationException("The Town identity does not exist.");
        towns[index] = updated;
        checkpointSchemaVersion = StateSchemaVersion;
    }

    private static IReadOnlyList<TownRuntimeState> MigrateTowns(PrivateWorldRuntimeState state)
    {
        if (state.FounderSetup is not { } setup) return [];
        if (!setup.Started && setup.FounderIds.Count == 0 && state.Map.CampObjects.Count == 0)
            return [];
        var active = state.Society.Society.Inhabitants
            .Where(person => person.Status == SocietyInhabitantStatus.Active)
            .Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var residents = setup.FounderIds.Where(active.Contains).ToArray();
        return [TownBorderRules.CreateFirstTown(state.Map, residents, founded: setup.Started)];
    }

}
