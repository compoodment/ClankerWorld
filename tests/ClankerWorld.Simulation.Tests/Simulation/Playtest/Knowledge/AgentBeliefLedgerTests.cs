using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class AgentBeliefLedgerTests
{
    [Fact]
    public void CorrectionIsOwnerPrivateVersionedStateAndDoesNotAppendWorldEvents()
    {
        using var world = new PrivateWorldRuntime("belief-correction");
        var before = world.ExportState();
        var rumor = new SocietyAgentBelief(
            "belief:lost-tool:rumor",
            "founder-scout",
            "Mira moved the tool before the storm.",
            SocietyBeliefProvenance.Hearsay,
            6_500,
            0,
            SourceAgentId: "founder-mira",
            SourceEventId: 1,
            AboutInhabitantId: "founder-mira");
        world.RecordAgentBelief(rumor);

        var corrected = world.CorrectAgentBelief(
            "founder-scout",
            rumor.Id,
            new SocietyAgentBelief(
                "belief:lost-tool:seen",
                "founder-scout",
                "I saw the tool beside the storehouse.",
                SocietyBeliefProvenance.Firsthand,
                9_600,
                0,
                SourceEventId: 1,
                AboutInhabitantId: "founder-mira"));

        var state = world.ExportState();
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, state.SchemaVersion);
        Assert.Equal(before.Society.Society.WorldTick, state.Society.Society.WorldTick);
        Assert.Equal(before.Events, state.Events);
        Assert.Equal(before.Society.Society.Events, state.Society.Society.Events);
        Assert.Equal(before.Society.Society.Inhabitants, state.Society.Society.Inhabitants);
        var beliefs = state.Society.Society.Beliefs!;
        Assert.Equal(new[] { rumor.Id, corrected.Id }, beliefs.Select(item => item.Id));
        Assert.Equal(corrected.Id, beliefs.Single(item => item.Id == rumor.Id).SupersededByBeliefId);
        Assert.Equal(0, beliefs.Single(item => item.Id == rumor.Id).SupersededTick);
        Assert.Equal(rumor.Id, beliefs.Single(item => item.Id == corrected.Id).SupersedesBeliefId);
        Assert.Equal(SocietyBeliefProvenance.Firsthand, corrected.Provenance);
        Assert.Equal(9_600, corrected.ConfidenceBasisPoints);

        var restored = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));
        using var reloaded = PrivateWorldRuntime.Restore(restored);
        Assert.Equal(beliefs, reloaded.Society.Beliefs);
        Assert.Equal(before.Events, reloaded.ExportState().Events);
        Assert.Equal(before.Society.Society.Events, reloaded.Society.Events);

        var schema19 = before with { SchemaVersion = 19 };
        var migrated = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(schema19));
        using var oldWorld = PrivateWorldRuntime.Restore(migrated);
        Assert.Empty(oldWorld.Society.Beliefs ?? []);
        Assert.Equal(PrivateWorldRuntime.StateSchemaVersion, oldWorld.ExportState().SchemaVersion);
    }

    [Fact]
    public async Task OwnerProjectionAndTelemetryDoNotDiscloseAnotherAgentsPrivateBelief()
    {
        using var world = new PrivateWorldRuntime("belief-privacy");
        var directory = Directory.CreateTempSubdirectory();
        var logger = new RecordingLogger<PrivateWorldRuntimeService>();
        using var service = new PrivateWorldRuntimeService(
            world,
            new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")),
            new OwnerClientPresenceLease(TimeSpan.FromSeconds(30)),
            logger);
        await service.StartAsync(CancellationToken.None);
        const string secretStatement = "Mira told me the hidden store is beneath the old oak.";
        var transitions = new List<PrivateWorldBeliefTransition>();
        world.AgentBeliefChanged += transitions.Add;
        var scoutBelief = world.RecordAgentBelief(new SocietyAgentBelief(
            "belief:scout-secret", "founder-scout", secretStatement,
            SocietyBeliefProvenance.Hearsay, 4_200, 0,
            SourceAgentId: "founder-mira"));
        world.RecordAgentBelief(new SocietyAgentBelief(
            "belief:mira-secret", "founder-mira", "I hid the location from Scout.",
            SocietyBeliefProvenance.Firsthand, 10_000, 0));

        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var scout = snapshot.Inhabitants.Single(item => item.Id == "founder-scout");
        var mira = snapshot.Inhabitants.Single(item => item.Id == "founder-mira");
        Assert.Equal(scoutBelief.Statement, Assert.Single(scout.RecentBeliefs).Statement);
        Assert.Equal("hearsay", scout.RecentBeliefs[0].Provenance);
        Assert.Equal(4_200, scout.RecentBeliefs[0].ConfidenceBasisPoints);
        Assert.Equal("Mira", scout.RecentBeliefs[0].SourceAgentName);
        Assert.Equal("I hid the location from Scout.", Assert.Single(mira.RecentBeliefs).Statement);
        Assert.DoesNotContain(mira.RecentBeliefs, item => item.Statement == secretStatement);

        var scoutTransition = Assert.Single(transitions, item => item.BeliefId == scoutBelief.Id);
        Assert.Equal("recorded", scoutTransition.Outcome);
        Assert.Equal("founder-scout", scoutTransition.OwnerId);
        var serializedTelemetry = JsonSerializer.Serialize(scoutTransition);
        Assert.DoesNotContain(secretStatement, serializedTelemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("Statement", serializedTelemetry, StringComparison.Ordinal);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Detail.Contains(secretStatement, StringComparison.Ordinal));
        Assert.DoesNotContain(world.Society.Events, item => item.Detail.Contains(secretStatement, StringComparison.Ordinal));
        await service.StopAsync(CancellationToken.None);
        Assert.Contains(logger.Messages, message => message.Contains(
            "agent_belief_transition tick=0 owner=founder-scout belief=belief:scout-secret outcome=recorded provenance=Hearsay confidence_basis_points=4200",
            StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(secretStatement, StringComparison.Ordinal));
        directory.Delete(recursive: true);
    }

    [Fact]
    public void HearsayRequiresAReporterAndAgentsCannotCorrectAnotherOwnersBelief()
    {
        using var world = new PrivateWorldRuntime("belief-validation");
        Assert.Throws<InvalidDataException>(() => world.RecordAgentBelief(new SocietyAgentBelief(
            "belief:unattributed", "founder-scout", "Someone moved the tool.",
            SocietyBeliefProvenance.Hearsay, 5_000, 0)));
        Assert.Throws<InvalidDataException>(() => world.RecordAgentBelief(new SocietyAgentBelief(
            "belief:unknown-source", "founder-scout", "Someone moved the tool.",
            SocietyBeliefProvenance.Inference, 5_000, 0, SourceEventId: 999)));
        Assert.Throws<InvalidDataException>(() => world.RecordAgentBelief(new SocietyAgentBelief(
            "belief:private medical detail", "founder-scout", "A private detail.",
            SocietyBeliefProvenance.Inference, 5_000, 0)));
        Assert.Empty(world.Society.Beliefs ?? []);

        var belief = world.RecordAgentBelief(new SocietyAgentBelief(
            "belief:owned", "founder-scout", "The tool is missing.",
            SocietyBeliefProvenance.Inference, 4_000, 0));
        Assert.Throws<InvalidOperationException>(() => world.CorrectAgentBelief(
            "founder-mira", belief.Id,
            new SocietyAgentBelief("belief:stolen", "founder-mira", "Scout moved the tool.",
                SocietyBeliefProvenance.Hearsay, 8_000, 0, SourceAgentId: "founder-rowan")));
    }
}
