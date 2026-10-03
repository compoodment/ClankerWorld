using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;
using GodotSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLandHearingProjectionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ActualExpiryReachesOwnerClientJsonWithoutTurningPublicationOrInspectionIntoReceipt()
    {
        using var generated = NormalPathWorld.CreateGenerated("government-personal-path",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        var initial = generated.ExportState();
        var permission = initial.HouseholdLandUseRights![0];
        // Start beside a real permission expiry; preserve generated titles, buildings, goods and people.
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            HouseholdLandUseRights = initial.HouseholdLandUseRights!.Select(right => right.Id == permission.Id
                ? right with { AgreedEndTick = 1 } : right).ToArray(),
        }, _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var actual = Assert.Single(world.Towns[0].LandHearings.Cases);
        var notice = Assert.Single(world.Towns[0].Governance!.Notices,
            item => item.Id == actual.Revisions[0].NoticeId);
        Assert.Equal("land_hearing", notice.Kind);
        var beforeObservation = PrivateWorldRuntimeCodec.Encode(world.ExportState());

        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var client = JsonSerializer.Deserialize<GodotSnapshot>(JsonSerializer.Serialize(snapshot, JsonOptions), JsonOptions)!;
        var town = Assert.Single(snapshot.Towns);
        var clientTown = Assert.Single(client.Towns);
        var projected = Assert.Single(town.LandHearings);
        var received = Assert.Single(clientTown.LandHearings);
        Assert.Equal(1, town.LandHearingCount);
        Assert.Equal(1, clientTown.LandHearingCount);
        Assert.Equal((actual.Id, "expiry", "pending"), (projected.Id, projected.Kind, projected.Status));
        Assert.Equal((actual.Id, "expiry", "pending"), (received.Id, received.Kind, received.Status));
        var plot = permission.Tiles.Select(tile => (tile.X, tile.Y)).ToArray();
        Assert.Equal(plot, projected.Tiles.Select(tile => (tile.X, tile.Y)));
        Assert.Equal(plot, received.Tiles.Select(tile => (tile.X, tile.Y)));
        var window = (Published: 1L, Deadline: 1L + initial.Society.Society.Config.TicksPerWorldDay);
        Assert.Equal(window, (projected.PublishedTick, projected.DeadlineTick));
        Assert.Equal(window, (received.PublishedTick, received.DeadlineTick));
        Assert.Equal(notice.Id, projected.NoticeId);
        Assert.Equal(notice.Id, received.NoticeId);

        var household = initial.Society.Society.Households.Single(item => item.Id == permission.HouseholdId);
        var adults = initial.Society.Society.Inhabitants.Where(person => person.HouseholdId == household.Id &&
            person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
            .OrderBy(person => person.Id, StringComparer.Ordinal).ToArray();
        var party = Assert.Single(projected.Parties);
        var clientParty = Assert.Single(received.Parties);
        Assert.Equal(("household:" + household.Id, "household", household.Name), (party.Id, party.Kind, party.Name));
        Assert.Equal((party.Id, party.Kind, household.Name), (clientParty.Id, clientParty.Kind, clientParty.Name));
        Assert.Equal(adults.Select(person => person.Id), clientParty.AdultIds);
        Assert.Equal(adults.Select(person => person.Name), clientParty.AdultNames);
        Assert.Empty(party.NoticeAwareAdultIds);
        Assert.Empty(clientParty.NoticeAwareAdultIds);
        Assert.Empty(projected.Reads);
        Assert.Empty(received.Reads);
        Assert.Empty(projected.Evidence);
        Assert.Empty(received.Evidence);
        Assert.Empty(received.Responses);
        Assert.Empty(received.Rulings);
        Assert.Null(received.Judge);
        var publicJson = JsonSerializer.Serialize(projected, JsonOptions);
        foreach (var privateField in new[] { "rawReply", "recentPrivateThoughts", "recentMemories", "providerMessages" })
            Assert.DoesNotContain("\"" + privateField + "\"", publicJson, StringComparison.Ordinal);
        Assert.DoesNotContain(world.Towns[0].Governance!.Knowledge, receipt => receipt.NoticeId == notice.Id);
        Assert.Equal(beforeObservation, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }
}
