using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class RiverBankWideningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WideningKeepsBothConnectedBanksIncludingAcrossTheWorldSeam(bool wrapped)
    {
        var map = RiverBridgeTests.Map("...~...", "...~...", "...~~~.", "...~~..", "...~...");
        if (wrapped)
            map = map with
            {
                Tiles = map.Tiles.Select(tile => tile with
                { Position = new((tile.Position.X + 3) % map.Width, tile.Position.Y) }).ToArray(),
                WrapsEastWest = true,
            };
        var upper = Crossing(map, wrapped ? 6 : 3, 0);
        var lower = Crossing(map, wrapped ? 6 : 3, 4);
        Assert.True(RiverBridgeRules.SharesBanks(map, upper, lower));
        Assert.True(RiverBridgeRules.SharesBanks(map, lower, upper));
    }

    [Fact]
    public void RoadReusesTheUpperCrossingInsteadOfTheLowerDuplicate()
    {
        var map = RiverBridgeTests.Map("...~...", "...~...", "...~~~.", "...~~..", "...~...");
        AssertReused(map, Crossing(map, 3, 0), Crossing(map, 3, 4), new HashSet<GridPoint>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedWideningRespectsBlockedGroundAndReusesTheBridgeAfterClearingTheApproach(bool clearApproach)
    {
        var map = GeneratedCampMapGenerator.Generate(new GeographyOptions("bridge-audit-7", WorldSizePreset.Small,
            WaterPercent: 50, HydrologyVersion: 1));
        GridPoint[] west = [new(178, 91), new(178, 92), new(178, 93), new(178, 94), new(178, 95)];
        GridPoint[] east = [new(180, 91), new(181, 91), new(182, 91), new(182, 92), new(182, 93),
            new(182, 94), new(181, 94), new(181, 95), new(180, 95)];
        foreach (var path in new[] { west, east })
        {
            Assert.All(path, point => Assert.True(map.IsLand(point)));
            Assert.All(path.Zip(path.Skip(1)), step => Assert.True(map.CanFootStep(step.First, step.Second)));
        }
        var upper = Crossing(map, 179, 91);
        var lower = Crossing(map, 179, 95);
        Assert.True(RiverBridgeRules.SharesBanks(map, upper, lower));
        Assert.True(RiverBridgeRules.SharesBanks(map, lower, upper));
        var blocked = map.Resources.Select(resource => resource.Position).Concat(map.CampObjects.Select(item => item.Position)).ToHashSet();
        Assert.Equal(1_721, blocked.Count);
        if (clearApproach)
        {
            // Separate cleared-ground control: the primary row retains every
            // generated resource and camp blocker. Other obstructions stay put.
            Assert.True(blocked.Remove(new(178, 94)));
            Assert.True(blocked.Remove(new(178, 92)));
            var proposal = AssertReused(map, upper, lower, blocked);
            Assert.Empty(proposal.NewCrossings);
        }
        else
        {
            // Walkable shore does not grant a Road permission to erase resources.
            var result = RoadRoutePlanner.Plan(Request(map, upper, lower, blocked));
            Assert.Equal(RoadRouteOutcomes.RouteUnavailable, result.Outcome);
            Assert.Null(result.Proposal);
        }
    }

    [Theory]
    [InlineData("...~~~~")]
    [InlineData("PPP~~~.")]
    public void AnOpenTributaryOrPeakBarrierStillSeparatesTheBanks(string middle)
    {
        var map = RiverBridgeTests.Map("...~...", "...~...", middle, "...~~..", "...~...");
        var upper = Crossing(map, 3, 0);
        var lower = Crossing(map, 3, 4);
        Assert.False(RiverBridgeRules.SharesBanks(map, upper, lower));
        Assert.False(RiverBridgeRules.SharesBanks(map, lower, upper));
    }

    [Fact]
    public void SeparateBranchesWithInlandSourcesDoNotBecomeTheSameBanks()
    {
        var map = RiverBridgeTests.Map("..........", "...~..~...", "...~..~...", "...~..~...",
            "...~..~...", "...~..~...", "...~..~...", "...~..~...", "...~~~~...", "....~.....");
        // A walk around distant headwaters must not erase the distinct bank
        // between the two branches at these crossings.
        var west = Crossing(map, 3, 4);
        var east = Crossing(map, 6, 4);
        Assert.False(RiverBridgeRules.SharesBanks(map, west, east));
        Assert.False(RiverBridgeRules.SharesBanks(map, east, west));
    }

    [Fact]
    public void NearbyBanksOnAVeryLongRiverKeepTheirExistingConnection()
    {
        var map = RiverBridgeTests.Map(Enumerable.Repeat("...~...", RiverBridgeRules.MaximumBankSearchTiles + 10).ToArray());
        Assert.True(RiverBridgeRules.SharesBanks(map, Crossing(map, 3, 0), Crossing(map, 3, 4)));
        Assert.False(RiverBridgeRules.SharesBanks(map, Crossing(map, 3, 0), Crossing(map, 3, map.Height - 1)));
    }

    [Fact]
    public void AnUnfinishedWideningSearchDoesNotRefuseANeededCrossing()
    {
        var width = RiverBridgeRules.MaximumBankSearchTiles + 10;
        var narrow = "...~" + new string('.', width - 4);
        var wide = "..." + new string('~', width - 4) + ".";
        var map = RiverBridgeTests.Map(narrow, narrow, wide, wide, narrow);
        Assert.False(RiverBridgeRules.SharesBanks(map, Crossing(map, 3, 0), Crossing(map, 3, 4)));
    }

    private static RiverCrossing Crossing(SeededMap map, int x, int y)
    {
        Assert.True(RiverBridgeRules.TryResolve(map, $"bridge-{x}-{y}-ew-1", out var crossing));
        return crossing!;
    }

    private static RoadRouteRequest Request(SeededMap map, RiverCrossing upper, RiverCrossing lower,
        IReadOnlySet<GridPoint> blocked)
    {
        var existing = RiverBridgeRules.ToBridge(upper, BridgeTriggers.Traffic, 0, null);
        var bridged = RiverBridgeTests.WithBridges(map, existing);
        Assert.All(upper.Entrances.Concat(lower.Entrances), point => Assert.DoesNotContain(point, blocked));
        return new(bridged, [lower.EntranceA], new HashSet<GridPoint> { lower.EntranceB }, blocked, [existing]);
    }

    private static RoadRouteProposal AssertReused(SeededMap map, RiverCrossing upper, RiverCrossing lower,
        IReadOnlySet<GridPoint> blocked)
    {
        var request = Request(map, upper, lower, blocked);
        var result = RoadRoutePlanner.Plan(request);
        Assert.Equal(RoadRouteOutcomes.Connected, result.Outcome);
        var proposal = Assert.IsType<RoadRouteProposal>(result.Proposal);
        Assert.Null(RoadRoutePlanner.Validate(request, proposal));
        Assert.DoesNotContain(proposal.NewCrossings, crossing => crossing.Id == lower.Id);
        Assert.Equal([upper.Id], proposal.UsedBridgeIds);
        Assert.DoesNotContain(proposal.RoadTiles, blocked.Contains);
        return proposal;
    }
}
