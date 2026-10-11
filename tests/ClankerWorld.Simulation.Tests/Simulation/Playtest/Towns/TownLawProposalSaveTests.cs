using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownLawProposalSaveTests
{
    [Theory]
    [InlineData("create", false)]
    [InlineData("autosave", false)]
    [InlineData("overwrite", false)]
    [InlineData("create", true)]
    [InlineData("autosave", true)]
    [InlineData("overwrite", true)]
    public void RejectedCheckpointLeavesNamedSaveAndTimelineUntouched(string operation, bool serializationOnly)
    {
        var directory = Directory.CreateTempSubdirectory("rejected-law-save-");
        try
        {
            using var world = NonviolentRuntimeFixture.Create(NonviolentRuntimeFixture.Prepared(), new NonviolentTestProvider());
            world.Pause();
            var path = Path.Combine(directory.FullName, "world.json");
            var store = new ManualWorldSaveStore(path);
            var saved = store.Create("Good law", world, []);
            var goodBytes = PrivateWorldRuntimeCodec.Encode(store.Read(saved.Id));
            var before = Directory.GetFiles(path + ".manual").ToDictionary(file => file, File.ReadAllBytes);
            if (serializationOnly)
            {
                // Deliberate native boundary input: distinct UTF-16 keys become the
                // same replacement character in JSON. Encode alone accepts this;
                // the serialized checkpoint must also pass the actual decoder.
                foreach (var key in new[] { "serialization:\uD800", "serialization:\uD801" })
                    world.SubmitInstruction(new(key, "owner:test", NonviolentRuntimeFixture.Judge,
                        OwnerInstructionKind.Suggestive, "Consider the current public law."));
                world.Validate();
                var unreadable = PrivateWorldRuntimeCodec.Encode(world.ExportState());
                Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(unreadable));
            }
            else
            {
                // Simulate an invalid live snapshot without changing its last good save.
                var drafts = Assert.IsAssignableFrom<IList<TownLawDraft>>(world.Towns[0].Government!.LawDrafts);
                drafts[0] = drafts[0] with { Rule = "This no longer matches the actual council vote." };
            }
            Assert.Throws<InvalidDataException>(() =>
            {
                if (operation == "overwrite") store.Overwrite(saved.Id, world, []);
                else if (operation == "autosave") store.CreateAutosave(world, [], new WorldAutosaveSettings(
                    world.Society.WorldId, true, 5, 5, DateTimeOffset.UtcNow, 0));
                else store.Create("Invalid law", world, []);
            });
            var reopened = new ManualWorldSaveStore(path);
            Assert.Equal(saved, Assert.Single(reopened.List()));
            Assert.Equal(goodBytes, PrivateWorldRuntimeCodec.Encode(reopened.Read(saved.Id)));
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(path + ".manual").Order(StringComparer.Ordinal));
            foreach (var (file, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(file));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false, 225)]
    [InlineData(false, 226)]
    [InlineData(false, 227)]
    [InlineData(true, 232)]
    [InlineData(true, 233)]
    [InlineData(true, 234)]
    public async Task UnicodeProposalPreservesTheFullDraftThroughActiveAndNamedSaves(bool repeal, int fillerLength)
    {
        var state = NonviolentRuntimeFixture.Prepared();
        var town = state.Towns![0];
        var law = Assert.Single(town.Government!.Laws);
        var text = "Paths: " + new string('a', fillerLength) + "😀tail";
        var (council, government) = TownLawRules.ProposeAmendment(town.Governance!, town.Government,
            town.Id, NonviolentRuntimeFixture.Judge, law.Id, text, town.ResidentIds, 0, NonviolentRuntimeFixture.Day);
        if (repeal)
        {
            foreach (var voter in town.ResidentIds.Take(3))
                council = TownGovernanceRules.VoteProposal(council, council.Proposals[^1].Id, voter, true, 0);
            (council, government) = TownLawRules.Enact(council, government, town.Id, town.Name, 0);
            (council, government) = TownLawRules.ProposeRepeal(council, government, town.Id,
                NonviolentRuntimeFixture.Judge, law.Id, town.ResidentIds, 0, NonviolentRuntimeFixture.Day);
        }
        using var world = NonviolentRuntimeFixture.Create(state with
        { Towns = [town with { Governance = council, Government = government }] }, new NonviolentTestProvider());
        world.Validate();
        var label = council.Proposals[^1].Text;
        var prefix = repeal ? "Repeal law 1: " : "Amend law 1 to read: ";
        var complete = prefix + text;
        var cut = fillerLength == (repeal ? 233 : 226) ? 254 : 255;
        Assert.Equal(complete[..cut] + "…", label);
        Assert.True(label.Length <= TownLawRules.MaximumTextLength);
        var directory = Directory.CreateTempSubdirectory("law-proposal-save-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            var file = new PrivateWorldStateFile(path, _ => new NonviolentTestProvider());
            file.Save(world);
            using var active = file.LoadOrCreate(state.WorldSeed);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(active.ExportState()));
            world.Pause();
            var store = new ManualWorldSaveStore(path);
            var saved = store.Create("Law proposal", world, []);
            var overwrite = store.Overwrite(saved.Id, world, []);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
                PrivateWorldRuntimeCodec.Encode(store.Read(overwrite.BackupId)));
            using var named = NonviolentRuntimeFixture.Create(new ManualWorldSaveStore(path).Read(saved.Id), new NonviolentTestProvider());
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(named.ExportState()));
            Assert.Equal(text, TownLawRules.Text(named.Towns[0].Government!.LawDrafts[^1].Subject,
                named.Towns[0].Government!.LawDrafts[^1].Rule));
            Assert.Equal(label, named.Towns[0].Governance!.Proposals[^1].Text);
            world.Resume();
            named.Resume();
            for (var tick = 0; tick < 3; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await named.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(named.ExportState()));
            }
        }
        finally { directory.Delete(recursive: true); }
    }
}
