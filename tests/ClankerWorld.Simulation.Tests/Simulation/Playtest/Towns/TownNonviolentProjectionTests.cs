using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownNonviolentProjectionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task NativeReportReachesTownInspectionWithoutTurningPublicationIntoReceiptFindingOrRemedy()
    {
        var state = await NonviolentRuntimeFixture.FiledAsync();
        using var world = NonviolentRuntimeFixture.Create(state, new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == NonviolentRuntimeFixture.Subject
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal)) : null,
        });
        var actual = Assert.Single(world.Towns[0].Nonviolent.Cases);
        var revision = actual.Revisions[^1];
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var client = Client(snapshot);
        var town = Assert.Single(client.Towns);
        var hearing = Assert.Single(town.NonviolentCases);
        Assert.Equal(1, town.NonviolentCaseCount);
        Assert.Equal((actual.Id, actual.Allegation.SubjectId, actual.Allegation.ConductKind, actual.Allegation.ConductTick),
            (hearing.Id, hearing.SubjectId, hearing.ConductKind, hearing.ConductTick));
        Assert.Equal((actual.Allegation.Position.X, actual.Allegation.Position.Y), (hearing.Position.X, hearing.Position.Y));
        Assert.Equal(world.Society.GetInhabitant(actual.Allegation.SubjectId).Name, hearing.SubjectName);
        Assert.Equal(actual.Allegation.LawVersion, hearing.ApplicableLaw!.Version);
        Assert.Equal("resident_duty", hearing.ApplicableLaw.Scope);
        Assert.Equal((revision.NoticeId, revision.PublishedTick, revision.DeadlineTick),
            (hearing.Revisions[^1].NoticeId, hearing.Revisions[^1].PublishedTick, hearing.Revisions[^1].DeadlineTick));
        var subject = Assert.Single(hearing.Revisions[^1].Parties, party => party.SubjectId == NonviolentRuntimeFixture.Subject);
        Assert.False(subject.NoticeAware);
        var current = Assert.Single(hearing.CurrentParties, party => party.SubjectId == NonviolentRuntimeFixture.Subject);
        Assert.Equal(subject.RespondingAdultId, current.RespondingAdultId);
        Assert.False(current.NoticeAware);
        Assert.Empty(hearing.Findings);
        Assert.Empty(hearing.Offers);
        Assert.Empty(hearing.Agreements);
        Assert.Null(hearing.Judge);
        Assert.False(town.Government!.NonLandAuthorized);
        Assert.Null(town.Government.NonLandAuthority);
        Assert.Equal(actual.Evidence.Select(evidence => (evidence.Kind, evidence.Acquisition, evidence.SourceRecordId, evidence.SourceVersion)),
            hearing.Evidence.Select(evidence => (evidence.Kind, evidence.Acquisition, evidence.SourceRecordId, evidence.SourceVersion)));
        Assert.Equal(actual.Reads.Select(read => (read.AgentId, read.Revision, read.ReadTick)),
            hearing.Reads.Select(read => (read.AgentId, read.Revision, read.ReadTick)));
        var text = string.Join("\n", NonviolentHearingText.Details(hearing, tick => "time " + tick));
        Assert.Contains("Allegation:", text, StringComparison.Ordinal);
        Assert.Contains("no eligible willing adjudicator", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Voluntary agreement", text, StringComparison.Ordinal);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Governance!.Knowledge.Any(receipt =>
            receipt.AgentId == NonviolentRuntimeFixture.Subject && receipt.NoticeId == revision.NoticeId), 10);
        var learned = Assert.Single(Assert.Single(Client(new OwnerWorldObservationStore(world).GetSnapshot()).Towns).NonviolentCases);
        Assert.True(Assert.Single(learned.Revisions[^1].Parties, party => party.SubjectId == NonviolentRuntimeFixture.Subject).NoticeAware);
        Assert.Empty(learned.Responses);
        Assert.Empty(learned.Findings);
        Assert.Empty(learned.Agreements);
    }

    private static OwnerWorldSnapshot Client(ViewerWorldSnapshot snapshot) =>
        JsonSerializer.Deserialize<OwnerWorldSnapshot>(JsonSerializer.Serialize(snapshot, JsonOptions), JsonOptions)!;
}
