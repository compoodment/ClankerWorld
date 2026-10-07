using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using GodotSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed class ConstructionSiteProjectionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task HouseholdBuildingUnderWayReachesTheClientAsASiteWithItsProgress()
    {
        using var seed = new PrivateWorldRuntime("construction-site-projection", _ => new IdleProvider());
        seed.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await seed.AdvanceOneTickAsync();
        var state = seed.ExportState();
        var shelter = seed.WorldContent.Buildings.Single(item => item.LocalId == "shelter");
        var site = state.Map.Tiles.Select(tile => tile.Position).Last(position => state.Map.IsBuildable(position) &&
            !state.Map.CampObjects.Any(item => item.Position == position) &&
            !state.Map.Resources.Any(item => item.Position == position) &&
            !state.Inhabitants.Any(person => person.Position == position));
        var builder = state.Inhabitants[0];
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == builder.InhabitantId ? person with
            {
                Position = site,
                Project = new(TownConstructionCandidateIds.Building(shelter.CanonicalId, site),
                    shelter.DisplayName, seed.WorldTick, "working", 4, LastTransitionTick: seed.WorldTick),
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var household = world.Society.Inhabitants.Single(person => person.Id == builder.InhabitantId).HouseholdId;

        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var visible = Assert.Single(snapshot.ConstructionSites);
        Assert.Equal(("household:" + builder.InhabitantId, shelter.CanonicalId, shelter.DisplayName, site.X, site.Y, 4, 10, "working"),
            (visible.Id, visible.DefinitionId, visible.DisplayName, visible.Site.X, visible.Site.Y, visible.WorkDone, visible.WorkRequired, visible.Stage));
        Assert.Equal((shelter.Width, shelter.Height), (visible.Width, visible.Height));
        Assert.Equal(shelter.Tags, visible.Tags);
        Assert.Equal(((string?)null, household), (visible.TownId, visible.HouseholdId));
        // A household plan has no entrance until it is built, so the client draws it facing south.
        Assert.Null(visible.Entrance);

        var client = JsonSerializer.Deserialize<GodotSnapshot>(JsonSerializer.Serialize(snapshot, JsonOptions), JsonOptions)!;
        var received = Assert.Single(client.ConstructionSites);
        Assert.Equal((visible.Id, visible.DefinitionId, visible.Site.X, visible.Site.Y, visible.WorkDone, visible.WorkRequired),
            (received.Id, received.DefinitionId, received.Site.X, received.Site.Y, received.WorkDone, received.WorkRequired));
        Assert.Equal(visible.Tags, received.Tags);
        Assert.Equal(2, received.DrawnStage);
        Assert.Equal("Being built · 40% done", GameUiText.ConstructionDescription(received));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(10, 3)]
    public void SitesMoveThroughTheThreeApprovedStagesByThirdsOfTheirWork(int done, int stage)
    {
        var site = new OwnerWorldConstructionSite("town:project", "house", "House", ["house"], new(0, 0), 1, 1, null,
            done, 10, "working", "town-1", null);
        Assert.Equal(stage, site.DrawnStage);
    }

    [Theory]
    [InlineData("supplying", 0, "Waiting for materials · 0% done")]
    [InlineData("working", 7, "Being built · 70% done")]
    [InlineData("blocked", 3, "Work stopped · 30% done")]
    [InlineData("paused", 5, "Work paused · 50% done")]
    [InlineData("travelling", 0, "Builder on the way · 0% done")]
    [InlineData("gathering", 0, "Waiting for materials · 0% done")]
    public void SiteDescriptionSaysWhatTheWorkIsWaitingOnAndHowFarItHasGot(string stage, int done, string expected)
    {
        var site = new OwnerWorldConstructionSite("town:project", "house", "House", ["house"], new(0, 0), 1, 1, null,
            done, 10, stage, "town-1", null);
        Assert.Equal(expected, GameUiText.ConstructionDescription(site));
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = request.Observation.Candidates.Where(candidate => candidate.Id == "safe_idle").ToArray() },
            }, cancellationToken);
    }
}
