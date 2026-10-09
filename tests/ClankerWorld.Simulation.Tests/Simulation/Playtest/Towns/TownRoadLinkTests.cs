using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownMembershipTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task NativeFirstBuildingLinksOnlyNearestTownAndProtectedLandRefusesWholeRoute(bool protectedLand, bool remoteBlockedTarget)
    {
        var actor = Founders[2];
        var state = WithTowns(Generated("nearest-town-road"), Founders[..2], Founders[2..]);
        var second = Town(state, Second);
        var first = Town(state, First);
        if (remoteBlockedTarget)
        {
            var targetHousehold = state.Society.Society.GetInhabitant(actor).HouseholdId!;
            state = state with
            {
                TownLandTitles = state.TownLandTitles!.Concat(TownLandRightsRules.InitialTitles(state.Map, second, 0))
                    .OrderBy(title => title.Id, StringComparer.Ordinal).ToArray(),
                HouseholdLandUseRights = state.HouseholdLandUseRights!.Concat(TownLandRightsRules.InitialUseRights(state.Map,
                    Second, [(targetHousehold, second.BorderTiles)], 0)).OrderBy(right => right.Id, StringComparer.Ordinal).ToArray(),
            };
            var warehouse = Assert.Single(state.WorldSimulation!.Buildings, building =>
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId).Tags.Contains("warehouse", StringComparer.Ordinal));
            // Reassignment requires an empty Warehouse. Prepare that stock
            // condition explicitly, then use the actual public transfer.
            var stored = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == warehouse.InstanceId)
                .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
            state = WithInventory(state, state.Society.Society.Inventory with
            {
                Lots = state.Society.Society.Inventory.Lots.Where(lot => !stored.Contains(lot.Id)).ToArray(),
                Reservations = state.Society.Society.Inventory.Reservations.Where(item => !stored.Contains(item.LotId)).ToArray(),
            });
            using var transfer = Reopen(state, new ScriptedModel());
            var reassigned = transfer.ReassignBuilding(warehouse.InstanceId, warehouse.TownId, warehouse.HouseholdId,
                targetTownId: Second, targetHouseholdId: null);
            Assert.True(reassigned.Applied, reassigned.Failure);
            Assert.DoesNotContain(transfer.RoadTiles, second.BorderTiles.Contains);
            Assert.Equal(Second, transfer.WorldSimulation.Buildings.Single(building => building.InstanceId == warehouse.InstanceId).TownId);
            Assert.DoesNotContain(warehouse.Position, second.BorderTiles);
            state = transfer.ExportState();
        }
        var taken = Taken(state).Concat(state.Towns!.SelectMany(town => town.BorderTiles)).ToHashSet();
        var site = state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => state.Map.FootDistance(point, second.OriginSite!.Value) + 3 < state.Map.FootDistance(point, first.OriginSite!.Value) &&
                state.Map.IsBuildable(point) && TownBorderRules.Around(state.Map, [point]).All(tile => !taken.Contains(tile)))
            .OrderBy(point => state.Map.FootDistance(point, second.OriginSite!.Value)).ThenBy(point => point.Y).ThenBy(point => point.X).First();
        using (var founding = Reopen(Calm(At(state, site, actor)), FoundingModel(actor)))
        {
            Assert.True((await founding.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(state.RoadTiles, founding.RoadTiles);
            state = founding.ExportState();
        }
        var town = Assert.Single(state.Towns!, item => item.Id != First && item.Id != Second);
        Assert.Empty(town.AssignedBuildingIds);
        Assert.Null(town.FirstBuildingCompletedTick);
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var house = state.WorldContent!.Buildings.Single(item => item.LocalId == "house-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in house.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "road-house-cost:" + cost.ResourceId, cost.ResourceId, household, cost.Amount);
        state = WithInventory(state, inventory);
        if (protectedLand)
            state = state with
            {
                HouseholdLandUseRights = state.HouseholdLandUseRights!.Concat(TownLandRightsRules.InitialUseRights(state.Map,
                    town.Id, [(household, town.BorderTiles)], state.Society.Society.WorldTick)).OrderBy(right => right.Id, StringComparer.Ordinal).ToArray(),
            };
        var before = PrivateWorldRuntimeCodec.Encode(state);
        using var world = Reopen(PrivateWorldRuntimeCodec.Decode(before), new ScriptedModel());
        using var replay = Reopen(PrivateWorldRuntimeCodec.Decode(before), new ScriptedModel());
        foreach (var runtime in new[] { world, replay })
        {
            var placed = runtime.PlaceBuilding("nearest-town-first-house", house.CanonicalId, site, household);
            Assert.True(placed.Applied, placed.Failure);
            Assert.Equal(state.Society.Society.WorldTick, runtime.Towns.Single(item => item.Id == town.Id).FirstBuildingCompletedTick);
            var events = runtime.ExportState().Events;
            if (protectedLand || remoteBlockedTarget)
            {
                Assert.DoesNotContain(events, item => item.Kind == "town_road_linked");
                Assert.Single(events, item => item.Kind == "town_road_link_unconnected" && item.Detail.StartsWith(town.Id + "|" + Second + "|", StringComparison.Ordinal));
                if (protectedLand) Assert.Equal(state.RoadTiles, runtime.RoadTiles);
                else Assert.All(runtime.RoadTiles.Except(state.RoadTiles!), tile => Assert.Contains(tile, town.BorderTiles));
                Assert.Empty(runtime.Bridges);
            }
            else
            {
                var linked = Assert.Single(events, item => item.Kind == "town_road_linked");
                Assert.StartsWith(town.Id + "|" + Second + "|", linked.Detail, StringComparison.Ordinal);
                Assert.Contains(second.OriginSite!.Value, runtime.RoadTiles);
                Assert.True(runtime.RoadTiles.Count > state.RoadTiles!.Count);
                Assert.NotNull(Assert.Single(runtime.WorldSimulation.Buildings, building => building.InstanceId == placed.InstanceId).Entrance);
                Assert.All(runtime.Bridges, bridge => Assert.StartsWith("road:town-link:" + town.Id + ":" + Second, bridge.RouteId!, StringComparison.Ordinal));
            }
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(state.TownLandTitles),
                System.Text.Json.JsonSerializer.Serialize(runtime.ExportState().TownLandTitles));
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(state.HouseholdLandUseRights),
                System.Text.Json.JsonSerializer.Serialize(runtime.ExportState().HouseholdLandUseRights));
            Assert.Equal(first.BorderTiles, runtime.Towns.Single(item => item.Id == First).BorderTiles);
            Assert.Equal(second.BorderTiles, runtime.Towns.Single(item => item.Id == Second).BorderTiles);
            runtime.Validate();
            AssertRoundTrip(runtime);
        }
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        foreach (var runtime in new[] { world, replay })
            Assert.True((await runtime.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(protectedLand || remoteBlockedTarget ? 0 : 1, world.ExportState().Events.Count(item => item.Kind == "town_road_linked"));
        Assert.True(world.RemoveBuilding("nearest-town-first-house", town.Id, household).Applied);
        Assert.Empty(world.Towns.Single(item => item.Id == town.Id).AssignedBuildingIds);
        var replacement = world.ExportState();
        var replacementInventory = replacement.Society.Society.Inventory;
        foreach (var cost in house.BuildCosts)
            replacementInventory = InventoryFixture.AddLot(replacementInventory,
                "replacement-road-house-cost:" + cost.ResourceId, cost.ResourceId, household, cost.Amount);
        using var rebuilt = Reopen(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(
            WithInventory(replacement, replacementInventory))), new ScriptedModel());
        var completion = rebuilt.Towns.Single(item => item.Id == town.Id).FirstBuildingCompletedTick;
        Assert.Equal(state.Society.Society.WorldTick, completion);
        Assert.True(rebuilt.PlaceBuilding("nearest-town-replacement-house", house.CanonicalId, site, household).Applied);
        Assert.Equal(completion, rebuilt.Towns.Single(item => item.Id == town.Id).FirstBuildingCompletedTick);
        Assert.Single(rebuilt.ExportState().Events, item => item.Kind is "town_road_linked" or "town_road_link_unconnected" &&
            item.Detail.StartsWith(town.Id + "|", StringComparison.Ordinal));
        AssertRoundTrip(rebuilt);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("future")]
    public void SavedFirstBuildingCompletionRejectsMissingOrImpossibleHistory(string damage)
    {
        var state = Generated("town-road-completion-validation");
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        var document = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        var town = document["state"]!["towns"]![0]!.AsObject();
        if (damage == "missing") town.Remove("firstBuildingCompletedTick");
        else if (damage == "null") town["firstBuildingCompletedTick"] = null;
        else town["firstBuildingCompletedTick"] = state.Society.Society.WorldTick + 1;
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(document)));
        using var original = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(original.ExportState()));
    }
}

public sealed class InterTownRoadRouteTests
{
    [Fact]
    public void DistantTownOnOpenLandConnectsWithinTheExistingSearchBudget()
    {
        var row = new string('.', 500);
        var map = RiverBridgeTests.Map(Enumerable.Repeat(row, 160).ToArray());
        var start = new GridPoint(0, 80);
        var end = new GridPoint(499, 80);
        var request = new RoadRouteRequest(map, [start], new HashSet<GridPoint> { end }, new HashSet<GridPoint>(), [], ReuseRoads: true);
        var route = Assert.IsType<RoadRouteProposal>(RoadRoutePlanner.Plan(request).Proposal);
        Assert.Equal(Enumerable.Range(0, 500).Select(x => new GridPoint(x, 80)), route.RoadTiles);
        Assert.Null(RoadRoutePlanner.Validate(request, route));
        // The local Dijkstra search still keeps its old budget and behavior.
        Assert.Null(RoadRoutePlanner.Plan(request with { ReuseRoads = false }).Proposal);
    }

    [Fact]
    public void TownLinkUsesWrappedDiagonalOnlyWithBothClearShoulders()
    {
        var map = RiverBridgeTests.Map(".........", ".........", ".........") with { WrapsEastWest = true };
        var start = new GridPoint(0, 1);
        var end = new GridPoint(8, 2);
        var request = new RoadRouteRequest(map, [start], new HashSet<GridPoint> { end }, new HashSet<GridPoint>(), [], ReuseRoads: true);
        var route = Assert.IsType<RoadRouteProposal>(RoadRoutePlanner.Plan(request).Proposal);
        Assert.Equal(new[] { start, end }, route.RoadTiles);
        Assert.Null(RoadRoutePlanner.Validate(request, route));
        foreach (var shoulder in new[] { new GridPoint(8, 1), new GridPoint(0, 2) })
        {
            var blocked = request with { Blocked = new HashSet<GridPoint> { shoulder } };
            Assert.NotNull(RoadRoutePlanner.Validate(blocked, route));
            var detour = Assert.IsType<RoadRouteProposal>(RoadRoutePlanner.Plan(blocked).Proposal);
            Assert.True(detour.RoadTiles.Count > 2);
            Assert.DoesNotContain(shoulder, detour.RoadTiles);
            Assert.Null(RoadRoutePlanner.Validate(blocked, detour));
        }
    }

    [Fact]
    public void TownLinkReusesCheaperExistingRoadEvenWhenItsGroundIsNowHeld()
    {
        var map = RiverBridgeTests.Map(".........", ".........", ".........");
        var start = new GridPoint(0, 2);
        var end = new GridPoint(8, 2);
        var roads = Enumerable.Range(0, 9).Select(x => new GridPoint(x, 1)).Append(start).Append(end).ToHashSet();
        var request = new RoadRouteRequest(map, [start], new HashSet<GridPoint> { end },
            new HashSet<GridPoint> { new(4, 1) }, [], roads, ReuseRoads: true);
        var route = Assert.IsType<RoadRouteProposal>(RoadRoutePlanner.Plan(request).Proposal);
        Assert.Contains(new GridPoint(4, 1), route.RoadTiles);
        Assert.All(route.RoadTiles, tile => Assert.Contains(tile, roads));
        Assert.Null(RoadRoutePlanner.Validate(request, route));
        Assert.Equal(route.RoadTiles, RoadRoutePlanner.Plan(request).Proposal!.RoadTiles);
        Assert.DoesNotContain(new GridPoint(4, 1), RoadRoutePlanner.Plan(request with { ReuseRoads = false }).Proposal!.RoadTiles);
    }

    [Theory]
    [InlineData("~", true)]
    [InlineData("~~", true)]
    [InlineData("~~~", false)]
    [InlineData("L", false)]
    public void TownLinkUsesOnlyLegalBridgeSpansAndRefusesUncrossableWater(string water, bool connected)
    {
        var row = "..." + water + "...";
        var map = RiverBridgeTests.Map(row, row, row);
        var request = new RoadRouteRequest(map, [new(0, 1)], new HashSet<GridPoint> { new(row.Length - 1, 1) },
            new HashSet<GridPoint>(), [], ReuseRoads: true);
        var result = RoadRoutePlanner.Plan(request);
        if (connected)
        {
            var route = Assert.IsType<RoadRouteProposal>(result.Proposal);
            Assert.Equal(water.Length, Assert.Single(route.NewCrossings).Span.Count);
            Assert.Null(RoadRoutePlanner.Validate(request, route));
            var crossing = route.NewCrossings[0];
            var blocked = request with { Blocked = crossing.Entrances.ToHashSet() };
            Assert.NotNull(RoadRoutePlanner.Validate(blocked, route));
        }
        else
        {
            Assert.Null(result.Proposal);
            Assert.Equal(RoadRouteOutcomes.RouteUnavailable, result.Outcome);
        }
    }
}
