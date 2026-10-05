using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownNonviolentPublicRecordTests
{
    [Fact]
    public async Task AnActuallyReadPublicPermissionCanGroundARehearingButAnUnreadRecordCannot()
    {
        var state = await NonviolentRuntimeFixture.FindingAsync();
        var town = state.Towns![0];
        var original = Assert.Single(town.Nonviolent.Cases);
        var position = original.Allegation.Position;
        var actor = NonviolentRuntimeFixture.Subject;
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        Assert.Contains(state.TownLandTitles!, title => title.TownId == town.Id && title.Tiles.Contains(position));
        var groups = state.HouseholdLandUseRights!.Where(right => right.TownId == town.Id)
            .GroupBy(right => right.HouseholdId).Select(group => (HouseholdId: group.Key,
                Tiles: (IEnumerable<GridPoint>)group.SelectMany(right => right.Tiles).Where(tile => tile != position)
                    .Concat(group.Key == household ? [position] : []).Distinct().ToArray())).ToArray();
        var rights = TownLandRightsRules.InitialUseRights(state.Map, town.Id, groups, 0);
        var right = Assert.Single(rights, right => right.HouseholdId == household && right.Tiles.Contains(position));
        state = state with { HouseholdLandUseRights = state.HouseholdLandUseRights!.Where(right => right.TownId != town.Id).Concat(rights).OrderBy(right => right.Id, StringComparer.Ordinal).ToArray() };
        NonviolentRuntimeFixture.Strict(state);

        var stage = 0;
        string? importedId = null;
        var provider = new NonviolentTestProvider
        {
            Choose = observation =>
            {
                var action = observation.InhabitantId == actor ? stage switch
                {
                    0 => "hearing_file",
                    1 => "hearing_inspect",
                    2 => "law_case_evidence",
                    3 => "law_case_inspect",
                    4 => "law_case_reopen",
                    _ => null,
                } : observation.InhabitantId == NonviolentRuntimeFixture.Judge ? stage switch
                {
                    5 => "law_case_inspect",
                    6 => "law_case_assess_reopen",
                    _ => null,
                } : null;
                return action is null ? null : observation.Candidates.FirstOrDefault(candidate =>
                    candidate.Id.Contains("|" + action + "|", StringComparison.Ordinal) &&
                    (stage != 4 || candidate.Id.EndsWith("|material_evidence", StringComparison.Ordinal)) &&
                    (stage != 6 || candidate.Id.EndsWith(":accept", StringComparison.Ordinal)));
            },
            LandTiles = (_, candidate) => candidate.Id.Contains("|hearing_file|", StringComparison.Ordinal) ? [new(position.X, position.Y)] : null,
            LandPayload = (_, candidate) => candidate.Id.Contains("|hearing_file|", StringComparison.Ordinal)
                ? new(Statement: "Please inspect my recorded permission on this exact plot.", RequestedOutcome: "confirm") : null,
            Payload = (_, candidate) => candidate.Id.Contains("|law_case_reopen|", StringComparison.Ordinal)
                ? new(Grounds: "This newly inspected public permission was not in the earlier hearing.", EvidenceIds: [importedId!])
                : candidate.Id.Contains("|law_case_assess_reopen|", StringComparison.Ordinal)
                    ? new(Statement: "The actual public permission is new material evidence requiring a fresh hearing.") : null,
        };
        using var world = NonviolentRuntimeFixture.Create(state, provider);
        NonviolentRuntimeFixture.Wake(world, actor, "file-for-real-public-record");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].LandHearings.Cases.Count > 0, 8);
        Assert.DoesNotContain(world.Towns[0].Nonviolent.Cases[0].Evidence, evidence => evidence.SourceRecordId == right.Id);
        stage = 1;
        NonviolentRuntimeFixture.Wake(world, actor, "inspect-real-public-record");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].LandHearings.Cases.Any(item =>
            item.Reads.Any(read => read.AgentId == actor && read.EvidenceIds.Any(id => item.Evidence.Any(evidence => evidence.Id == id && evidence.SourceRecordId == right.Id)))), 8);
        stage = 2;
        NonviolentRuntimeFixture.Wake(world, actor, "submit-only-known-public-record");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Cases[0].Evidence.Any(evidence => evidence.SourceRecordId == right.Id), 8);
        var imported = Assert.Single(world.Towns[0].Nonviolent.Cases[0].Evidence, evidence => evidence.SourceRecordId == right.Id);
        importedId = imported.Id;
        Assert.Equal("record", imported.Kind);
        Assert.Equal("record_inspection", imported.Acquisition);
        Assert.Equal(TownLandHearingRules.Version(right), imported.SourceVersion);
        Assert.Equal(actor, imported.SourceAgentId);
        var submitted = NonviolentRuntimeFixture.Strict(world.ExportState());
        var withoutRead = submitted with
        {
            Towns = submitted.Towns!.Select(item => item with
            { LandHearings = item.LandHearings with { Cases = item.LandHearings.Cases.Select(file => file with { Reads = [] }).ToArray() } }).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(withoutRead));
        var forged = submitted with
        {
            Towns = submitted.Towns!.Select(item => item with
            {
                Nonviolent = item.Nonviolent with
                {
                    Cases = item.Nonviolent.Cases.Select(file => file with
                    { Evidence = file.Evidence.Select(evidence => evidence.Id == imported.Id ? evidence with { SourceVersion = "invented" } : evidence).ToArray() }).ToArray()
                }
            }).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(forged));
        stage = 3;
        NonviolentRuntimeFixture.Wake(world, actor, "read-submitted-public-record");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Cases[0].Reads.Any(read => read.AgentId == actor && read.EvidenceIds.Contains(imported.Id)), 8);
        stage = 4;
        NonviolentRuntimeFixture.Wake(world, actor, "request-public-record-rehearing");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Cases[0].ReopenRequests.Count > 0, 8);
        stage = 5;
        NonviolentRuntimeFixture.Wake(world, NonviolentRuntimeFixture.Judge, "inspect-actual-reopening-request");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Cases[0].Reads.Any(read =>
            read.AgentId == NonviolentRuntimeFixture.Judge && read.ReopenRequestIds.Count > 0), 8);
        stage = 6;
        NonviolentRuntimeFixture.Wake(world, NonviolentRuntimeFixture.Judge, "assess-actual-public-record");
        await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Cases[0].Revisions.Count == 2, 8);
        var reopened = world.Towns[0].Nonviolent.Cases[0];
        Assert.Equal("accepted", Assert.Single(reopened.ReopenRequests).Status);
        Assert.Equal(original.Findings[0], Assert.Single(reopened.Findings));
        Assert.Equal(NonviolentRuntimeFixture.Day, reopened.Revisions[^1].DeadlineTick - reopened.Revisions[^1].PublishedTick);
        NonviolentRuntimeFixture.Strict(world.ExportState());
    }
}
