using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;
using GodotOwnerWorldSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandRightsTests
{
    private static readonly JsonSerializerOptions GodotJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AcceptedLayoutTitlesAllConnectedLandAndAllocatesOnlyStarterBuildingFootprints()
    {
        var geography = new GeographyOptions("town-land-rights-starter", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        var site = world.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position;
        world.AcceptFirstTownLayout(site);

        var map = world.ExportState().Map;
        var town = Assert.Single(world.Towns);
        var titleTiles = world.TownLandTitles.SelectMany(item => item.Tiles).ToHashSet();
        Assert.NotEmpty(world.TownLandTitles);
        Assert.True(town.BorderTiles.ToHashSet().SetEquals(titleTiles));
        Assert.All(world.TownLandTitles, title =>
        {
            Assert.Equal(town.Id, title.TownId);
            Assert.Equal(0, title.RecordedTick);
            Assert.Single(TownLandRightsRules.ConnectedPlots(map, title.Tiles));
        });
        Assert.All(titleTiles, tile => Assert.True(map.IsLand(tile)));

        var expectedByHousehold = world.WorldSimulation.Buildings
            .Where(building => building.HouseholdId is not null)
            .GroupBy(building => building.HouseholdId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.SelectMany(building =>
            {
                var definition = world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
                return WorldContentSimulationRules.Footprint(definition, building);
            }).ToHashSet(), StringComparer.Ordinal);
        var occupiedLand = world.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(
                world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building))
            .Concat(map.Resources.Select(item => item.Position))
            .Concat(map.CampObjects.Select(item => item.Position))
            .Concat(world.RoadTiles).ToHashSet();
        Assert.Equal(expectedByHousehold.Count, world.HouseholdLandUseRights.Select(item => item.HouseholdId)
            .Distinct(StringComparer.Ordinal).Count());
        foreach (var (householdId, expectedTiles) in expectedByHousehold)
        {
            var rights = world.HouseholdLandUseRights.Where(item => item.HouseholdId == householdId).ToArray();
            Assert.NotEmpty(rights);
            Assert.All(rights, right =>
            {
                Assert.Equal(town.Id, right.TownId);
                Assert.Equal(TownLandRightsRules.StarterAllocationSource, right.GrantSource);
                Assert.Null(right.AgreedEndTick);
                Assert.Equal(0, right.GrantedTick);
            });
            Assert.True(expectedTiles.SetEquals(rights.SelectMany(item => item.Tiles)));
        }
        Assert.Contains(titleTiles, tile => !occupiedLand.Contains(tile));

        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void RequestsStayPendingConflictDoesNotMoveAnyoneAndMembershipUsesOnlyAcceptedRights()
    {
        using var world = CreateStartedTown("town-land-rights-requests");
        var state = world.ExportState();
        var map = state.Map;
        var alpha = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-alpha").Id;
        var beta = state.Society.Society.Inhabitants.First(item => item.HouseholdId == "household:camp-beta").Id;
        var titled = world.TownLandTitles.SelectMany(item => item.Tiles).ToHashSet();
        var used = world.HouseholdLandUseRights.SelectMany(item => item.Tiles).ToHashSet();
        var occupied = world.Inhabitants.Select(item => item.Position)
            .Concat(map.CampObjects.Select(item => item.Position))
            .Concat(map.Resources.Select(item => item.Position)).ToHashSet();
        var openTile = titled.OrderBy(item => item.Y).ThenBy(item => item.X).First(tile =>
            map.IsBuildable(tile) && !used.Contains(tile) && !occupied.Contains(tile));
        var betaTile = world.HouseholdLandUseRights.Where(item => item.HouseholdId == "household:camp-beta")
            .SelectMany(item => item.Tiles).First(tile => map.IsReachableOnFoot(openTile, tile));

        var first = world.RequestHouseholdLandUse("request:open", alpha, TownBorderRules.FirstTownId, [openTile]);
        Assert.True(first.Applied, first.Failure);
        Assert.False(first.IsDisputed);
        Assert.Equal(["household:camp-alpha"], TownLandRightsRules.ClaimantsAt(openTile,
            world.HouseholdLandUseRights, world.HouseholdLandUseRequests));

        const string addedAgent = "agent:00000000000000000000000000000091";
        Assert.Null(world.AddAgent(addedAgent, openTile));
        Assert.Equal(openTile, Assert.Single(world.Inhabitants, item => item.InhabitantId == addedAgent).Position);
        Assert.Contains(addedAgent, Assert.Single(world.Towns).ResidentIds);
        Assert.DoesNotContain(world.Society.Inhabitants, item => item.Id == addedAgent && item.HouseholdId is not null);

        var firstBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(firstBytes));
        Assert.Equal(firstBytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        Assert.True(reloaded.RequestHouseholdLandUse("request:open", alpha, TownBorderRules.FirstTownId, [openTile])
            .IsDuplicate);
        Assert.Equal(firstBytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));

        var positionsBeforeDispute = reloaded.Inhabitants.ToDictionary(item => item.InhabitantId,
            item => item.Position, StringComparer.Ordinal);
        var reachabilityBeforeDispute = reloaded.ExportState().Map.IsReachableOnFoot(openTile, betaTile);
        Assert.True(reachabilityBeforeDispute);
        var second = reloaded.RequestHouseholdLandUse("request:conflict", alpha,
            TownBorderRules.FirstTownId, [betaTile]);
        Assert.True(second.Applied, second.Failure);
        Assert.True(second.IsDisputed);
        Assert.Equal(["household:camp-alpha", "household:camp-beta"],
            TownLandRightsRules.ClaimantsAt(betaTile, reloaded.HouseholdLandUseRights,
                reloaded.HouseholdLandUseRequests));
        Assert.Throws<InvalidOperationException>(() => reloaded.ValidateAgentPlacement(
            "agent:00000000000000000000000000000092", betaTile));
        Assert.Equal(positionsBeforeDispute, reloaded.Inhabitants.ToDictionary(item => item.InhabitantId,
            item => item.Position, StringComparer.Ordinal));
        Assert.Equal(reachabilityBeforeDispute, reloaded.ExportState().Map.IsReachableOnFoot(openTile, betaTile));
        Assert.Equal(state.WorldSimulation!.Buildings, reloaded.WorldSimulation.Buildings);
        Assert.Equal(state.Society.Society.Inventory.Lots, reloaded.Society.Inventory.Lots);

        var saved = PrivateWorldRuntimeCodec.Encode(reloaded.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var projected = new OwnerWorldObservationStore(restored).GetSnapshot();
        var godot = JsonSerializer.Deserialize<GodotOwnerWorldSnapshot>(
            JsonSerializer.Serialize(projected, GodotJsonOptions), GodotJsonOptions);
        var request = Assert.Single(godot!.HouseholdLandUseRequests, item => item.Id == "request:conflict");
        Assert.True(request.IsDisputed);
        Assert.Equal(["household:camp-alpha", "household:camp-beta"], request.ClaimantHouseholdIds);
        Assert.Contains(request.DisputedTiles, tile => tile.X == betaTile.X && tile.Y == betaTile.Y);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public void RestoreRejectsNullAndUncoveredLandRecordsAsInvalidData()
    {
        using var world = CreateStartedTown("town-land-rights-damaged-state");
        var state = world.ExportState();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            TownLandTitles = [.. state.TownLandTitles!, null!],
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRights = [.. state.HouseholdLandUseRights!, null!],
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRequests = [null!],
        }));
        var right = state.HouseholdLandUseRights![0];
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRights = [right with { TownId = "town:missing" }],
        }));
        var town = Assert.Single(state.Towns!, item => item.Id == TownBorderRules.FirstTownId);
        var unclaimedTile = state.TownLandTitles!.Where(item => item.TownId == town.Id)
            .SelectMany(item => item.Tiles)
            .First(tile => state.HouseholdLandUseRights!.All(useRight => !useRight.Tiles.Contains(tile)));
        var household = state.Society.Society.Households[0];
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRequests = [new HouseholdLandUseRequest("request:unknown-agent", town.Id,
                household.Id, "founder:missing", [unclaimedTile], state.Society.Society.WorldTick)],
        }));
    }

    [Fact]
    public void RestoreRejectsNullTownIdsBeforeLookingUpTownRecords()
    {
        using var world = CreateStartedTown("town-land-rights-null-town-id");
        var state = world.ExportState();
        Assert.NotEmpty(state.TownLandTitles!);
        Assert.NotEmpty(state.HouseholdLandUseRights!);
        var title = state.TownLandTitles![0];
        var right = state.HouseholdLandUseRights![0];
        var town = Assert.Single(state.Towns!, item => item.Id == TownBorderRules.FirstTownId);
        var household = state.Society.Society.Households[0];
        var agent = state.Society.Society.Inhabitants.First(person =>
            person.HouseholdId == household.Id && person.Status == SocietyInhabitantStatus.Active);
        var plot = title.Tiles.Take(1).ToArray();
        var request = new HouseholdLandUseRequest("request:null-town", town.Id, household.Id,
            agent.Id, plot, state.Society.Society.WorldTick);

        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            TownLandTitles = [title with { TownId = null! }],
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRights = [right with { TownId = null! }],
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with
        {
            HouseholdLandUseRequests = [request with { TownId = null! }],
        }));
    }

    private static PrivateWorldRuntime CreateStartedTown(string seed)
    {
        var geography = new GeographyOptions(seed, WorldSizePreset.Small);
        var world = new PrivateWorldRuntime(geography.Seed,
            startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        world.AcceptFirstTownLayout(world.ExportState().Map.Resources.Single(item => item.Id == "berry-patch").Position);
        var map = world.ExportState().Map;
        var buildingTiles = world.WorldSimulation.Buildings.SelectMany(building =>
        {
            var definition = world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
            return WorldContentSimulationRules.Footprint(definition, building.Position);
        }).ToHashSet();
        var founders = map.Tiles.Where(tile => map.IsBuildable(tile.Position) &&
                !buildingTiles.Contains(tile.Position) && !map.CampObjects.Any(item => item.Position == tile.Position) &&
                !map.Resources.Any(item => item.Position == tile.Position))
            .Take(PrivateWorldRuntime.RequiredFounders).Select(tile => tile.Position).ToArray();
        Assert.Equal(PrivateWorldRuntime.RequiredFounders, founders.Length);
        for (var index = 0; index < founders.Length; index++)
            world.PlaceFounder($"founder:{index + 1:D32}", founders[index]);
        world.StartWorld();
        return world;
    }
}
