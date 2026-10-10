using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private IEnumerable<GridPoint> TownFoundingBuildingTiles(PlacedBuilding building) => WorldContentSimulationRules.Footprint(
        worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building);

    private TownRuntimeState? TownFoundingLayout(string actor)
    {
        if (founderSetup is not { Started: true } || !AdultResident(actor) || !ReadyForBriefInteraction(actor)) return null;
        var site = inhabitants[actor].Position;
        if (!map.IsBuildable(site)) return null;
        if (towns.Any(town => town.BorderTiles.Contains(site)) || townLandTitles.Any(title => title.Tiles.Contains(site))) return null;
        var claimed = towns.SelectMany(town => town.BorderTiles)
            .Concat(townLandTitles.SelectMany(title => title.Tiles)).ToHashSet();
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        var core = new HashSet<GridPoint> { site };
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<GridPoint> border;
        // Preserve the normal spare-land margin around any existing household buildings
        // incorporated into the first layout. Never swallow somebody else's property.
        while (true)
        {
            border = TownBorderRules.Around(map, core);
            if (border.Any(claimed.Contains)) return null;
            var tiles = border.ToHashSet();
            var added = false;
            foreach (var building in worldSimulation.Buildings)
            {
                var footprint = TownFoundingBuildingTiles(building).ToArray();
                if (!footprint.Any(tiles.Contains)) continue;
                if (building.TownId is not null || household is null || building.HouseholdId != household) return null;
                if (assigned.Add(building.InstanceId))
                {
                    core.UnionWith(footprint);
                    added = true;
                }
            }
            if (!added) break;
        }
        if (fields.Any(field => border.Contains(field.Position) && field.HouseholdId != household)) return null;
        if (worldSimulation.Buildings.Count(building => assigned.Contains(building.InstanceId) &&
            worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("warehouse", StringComparer.Ordinal)) > 1)
            return null;
        var id = "town:site:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            worldSeed + "|" + site.X.ToString(CultureInfo.InvariantCulture) + "," + site.Y.ToString(CultureInfo.InvariantCulture))));
        if (towns.Any(town => town.Id == id)) return null;
        return new TownRuntimeState(id, $"Town at {site.X.ToString(CultureInfo.InvariantCulture)}, {site.Y.ToString(CultureInfo.InvariantCulture)}",
            "founded", WorldTick, [], assigned.Order(StringComparer.Ordinal).ToArray(), border, site);
    }

    private void FoundTown(string actor, string candidate)
    {
        // Bind the model choice to the adult's current physical site, and recheck after
        // earlier choices have committed their titles. A stale or invented choice does nothing.
        if (TownFoundingLayout(actor) is not { } town || CivicAction(town.Id, "found") != candidate) return;
        var previous = TownForResident(actor);
        var group = TownCareGroup(actor, previous);
        var moving = group.ToHashSet(StringComparer.Ordinal);
        foreach (var other in towns.Where(item => item.ResidentIds.Any(moving.Contains)).ToArray())
            SetTown(other with { ResidentIds = other.ResidentIds.Where(id => !moving.Contains(id)).ToArray() });
        town = town with { ResidentIds = group };
        towns.Add(town);
        checkpointSchemaVersion = StateSchemaVersion;
        townLandTitles.AddRange(TownLandRightsRules.InitialTitles(map, town, WorldTick));
        townLandTitles = townLandTitles.OrderBy(title => title.Id, StringComparer.Ordinal).ToList();
        var assigned = town.AssignedBuildingIds.ToHashSet(StringComparer.Ordinal);
        var household = society.Checkpoint.GetInhabitant(actor).HouseholdId;
        if (household is not null)
        {
            var used = worldSimulation.Buildings.Where(building => assigned.Contains(building.InstanceId)).SelectMany(TownFoundingBuildingTiles)
                .Concat(fields.Where(field => field.HouseholdId == household && town.BorderTiles.Contains(field.Position)).Select(field => field.Position));
            var rights = TownLandRightsRules.InitialUseRights(map, town.Id, [(household, used)], WorldTick);
            householdLandUseRights.AddRange(rights.Select(right => right.Id.Length <= 128 ? right : right with
            {
                Id = "household-use:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(right.Id)))
            }));
            householdLandUseRights = householdLandUseRights.OrderBy(right => right.Id, StringComparer.Ordinal).ToList();
        }
        worldSimulation = worldSimulation with
        {
            Buildings = worldSimulation.Buildings.Select(building => assigned.Contains(building.InstanceId)
                ? building with { TownId = town.Id } : building).ToArray()
        };
        AdvanceTownGovernance();
        SettleTownAdmissions();
        AppendEvent("town_founded", $"{town.Id}|{actor}|{previous ?? "none"}|{group.Length}", inhabitants[actor].Position);
        if (worldSimulation.Buildings.Where(building => assigned.Contains(building.InstanceId))
            .OrderBy(building => building.PlacedTick).ThenBy(building => building.InstanceId, StringComparer.Ordinal)
            .FirstOrDefault() is { } firstBuilding)
            GenerateRoadBetweenTowns(town, firstBuilding);
    }
}
