using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;
using System.Text;
using System.Text.Json.Nodes;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLawEvidenceSaveTests
{
    [Theory]
    [InlineData(212)]
    [InlineData(213)]
    [InlineData(214)]
    public async Task InspectingUnicodeLawKeepsItsHistoricalEvidenceLoadable(int fillerLength)
    {
        var state = NonviolentRuntimeFixture.Prepared();
        var town = state.Towns![0];
        var law = Assert.Single(town.Government!.Laws);
        var text = "Paths: " + new string('a', fillerLength) + "😀tail";
        var (council, government) = TownLawRules.ProposeAmendment(town.Governance!, town.Government,
            town.Id, NonviolentRuntimeFixture.Judge, law.Id, text, town.ResidentIds, 0, NonviolentRuntimeFixture.Day);
        foreach (var voter in town.ResidentIds.Take(3))
            council = TownGovernanceRules.VoteProposal(council, council.Proposals[^1].Id, voter, true, 0);
        (council, government) = TownLawRules.Enact(council, government, town.Id, town.Name, 0);
        foreach (var actor in new[] { NonviolentRuntimeFixture.Subject, NonviolentRuntimeFixture.Witness, NonviolentRuntimeFixture.Judge })
            council = TownGovernanceRules.LearnNotices(council, actor, council.Notices.Select(notice => notice.Id), 0);
        state = NonviolentRuntimeFixture.Strict(state with { Towns = [town with { Governance = council, Government = government }] });
        var conduct = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == NonviolentRuntimeFixture.Subject
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|visit|", StringComparison.Ordinal)) : null
        };
        using (var world = NonviolentRuntimeFixture.Create(state, conduct))
        {
            await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.ConductRecords.Count > 0, 6);
            state = NonviolentRuntimeFixture.Strict(world.ExportState());
        }
        using (var world = NonviolentRuntimeFixture.Create(state, NonviolentRuntimeFixture.FilingProvider()))
        {
            await NonviolentRuntimeFixture.UntilAsync(world, () => world.Towns[0].Nonviolent.Cases.Count > 0, 8);
            state = NonviolentRuntimeFixture.Strict(world.ExportState());
        }
        var inspector = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == NonviolentRuntimeFixture.Judge
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|law_case_inspect|", StringComparison.Ordinal)) : null
        };
        var directory = Directory.CreateTempSubdirectory("clankerworld-law-evidence-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), _ => inspector);
            using var inspected = NonviolentRuntimeFixture.Create(state, inspector);
            file.Save(inspected);
            var goodBytes = File.ReadAllBytes(file.Path);
            using (var good = file.LoadOrCreate(state.WorldSeed)) good.Validate();
            await NonviolentRuntimeFixture.UntilAsync(inspected, () => inspected.Towns[0].Nonviolent.Cases[0].Evidence
                .Any(evidence => evidence.SourceRecordId?.StartsWith(law.Id + "@", StringComparison.Ordinal) == true), 8);
            inspected.Validate();
            file.Save(inspected);
            Assert.NotEqual(goodBytes, File.ReadAllBytes(file.Path));
            using var restored = file.LoadOrCreate(state.WorldSeed);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(inspected.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            var evidence = Assert.Single(restored.Towns[0].Nonviolent.Cases[0].Evidence,
                item => item.SourceRecordId?.StartsWith(law.Id + "@", StringComparison.Ordinal) == true);
            Assert.StartsWith("Applicable law at the alleged act: Paths: ", evidence.Text);
            Assert.DoesNotContain('�', evidence.Text);
            Assert.Equal(fillerLength == 213 ? 255 : 256, evidence.Text.Length);
            Assert.Equal(fillerLength == 212, evidence.Text.EndsWith("😀", StringComparison.Ordinal));
            Assert.False(char.IsHighSurrogate(evidence.Text[^1]));
            Assert.Equal(text, TownLawRules.Text(restored.Towns[0].Government!.Laws[0].Versions[^1].Subject,
                restored.Towns[0].Government!.Laws[0].Versions[^1].Rule));
            for (var tick = 0; tick < 3; tick++)
            {
                Assert.True((await inspected.AdvanceOneTickAsync()).Advanced);
                Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(inspected.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            }
            if (fillerLength == 213)
            {
                // The old writer replaced the last good file with this exact
                // JSON representation of a split historical-law excerpt.
                var damaged = JsonNode.Parse(File.ReadAllBytes(file.Path))!;
                var savedCase = damaged["state"]!["towns"]![0]!["nonviolent"]!["cases"]![0]!;
                var savedEvidence = savedCase["evidence"]!.AsArray().Single(item =>
                    item!["sourceRecordId"]!.GetValue<string>() == evidence.SourceRecordId)!;
                savedEvidence["text"] = ("Applicable law at the alleged act: " + text)[..256];
                var damagedBytes = Encoding.UTF8.GetBytes(damaged.ToJsonString());
                File.WriteAllBytes(file.Path, damagedBytes);
                Assert.Throws<InvalidDataException>(() => file.LoadOrCreate(state.WorldSeed));
                Assert.Equal(damagedBytes, File.ReadAllBytes(file.Path));
                Assert.Empty(Directory.GetFiles(directory.FullName, ".world.json.*.tmp"));
            }
        }
        finally { directory.Delete(recursive: true); }
    }
}
