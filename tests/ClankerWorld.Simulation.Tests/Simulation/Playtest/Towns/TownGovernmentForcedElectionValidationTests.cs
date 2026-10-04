using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownGovernmentHandoverTests
{
    private static readonly TownArrangement RepresentativeOnly =
        new(TownArrangementRules.ElectedCouncil, TownArrangementRules.NoOffice);

    [Fact]
    public void AForcedElectionReferenceCannotOutliveItsElectionRecord()
    {
        var town = ForcedCouncilHandover().RoundTrip();
        var change = Assert.Single(town.Government.Changes);
        var election = Assert.IsType<TownElection>(town.Council.Election);
        Assert.Equal(election.Id, change.ForcedCouncilElectionId);

        town.Council = town.Council with { Election = null };
        town.Government = town.Government with
        {
            Changes = [change with { ForcedCouncilElectionId = null }],
        };
        town.Validate(); // The missing election alone is legal; claiming it is not.
        town.Government = town.Government with { Changes = [change] };

        Assert.Throws<InvalidDataException>(() => town.RoundTrip());
    }

    [Fact]
    public void AForcedElectionReferenceCannotPointIntoAnotherTownsNativeHistory()
    {
        var town = ForcedCouncilHandover().RoundTrip();
        var other = ForcedCouncilHandover("town:other").RoundTrip();
        var foreignElection = Assert.IsType<TownElection>(other.Council.Election);
        var change = Assert.Single(town.Government.Changes);
        Assert.NotEqual(change.ForcedCouncilElectionId, foreignElection.Id);
        town.Government = town.Government with
        {
            Changes = [change with { ForcedCouncilElectionId = foreignElection.Id }],
        };

        Assert.Throws<InvalidDataException>(() => town.RoundTrip());
    }

    [Fact]
    public void AHandoverCannotClaimAnOrdinaryElectionOpenedBeforeItsApproval()
    {
        var town = new Town("a", "b", "c", "d", "e", "f", "g", "h");
        town.RegisterCouncil("a", "b", "c");
        town.Advance(0);
        var ordinary = Assert.IsType<TownElection>(town.Council.Election);
        town.Advance(1);
        town.Yes(town.Propose(RepresentativeOnly), "a", "b", "c", "d", "e");
        town = town.RoundTrip();
        var change = Assert.Single(town.Government.Changes);
        Assert.Equal("handover", change.Status);
        Assert.Null(change.ForcedCouncilElectionId);
        Assert.True(ordinary.OpenedTick < change.ApprovedTick);
        town.Government = town.Government with
        {
            Changes = [change with { ForcedCouncilElectionId = ordinary.Id }],
        };

        Assert.Throws<InvalidDataException>(() => town.RoundTrip());
    }

    [Fact]
    public void ACompletedArrangementCannotClaimAGenuineRegularCouncilElection()
    {
        var town = new Town("a", "b", "c", "d", "e", "f", "g", "h");
        town.RegisterCouncil("a", "b", "c");
        town.Advance(0);
        town.VoteEverything(0);
        town.Advance(Day);
        var termEnd = town.Council.TermEndTick!.Value;
        town.Advance(termEnd - Day);
        var regular = Assert.IsType<TownElection>(town.Council.Election);
        Assert.Equal("regular", regular.Kind);
        town.Yes(town.Propose(RepresentativeOnly), "a", "b", "c", "d", "e");
        town.VoteEverything(town.Tick);
        town.Advance(termEnd);
        town = town.RoundTrip();
        var change = Assert.Single(town.Government.Changes);
        Assert.Equal("completed", change.Status);
        Assert.Null(change.ForcedCouncilElectionId);
        Assert.Equal(regular.OpenedTick, change.ApprovedTick);
        Assert.Equal(regular.OpenedTick, change.SettledTick);
        Assert.Contains(town.Council.ElectionHistory,
            election => election.Id == regular.Id && election.Stage == "completed");
        town.Government = town.Government with
        {
            Changes = [change with { ForcedCouncilElectionId = regular.Id }],
        };

        Assert.Throws<InvalidDataException>(() => town.RoundTrip());
    }

    [Fact]
    public void TwoApprovedHandoversCannotOwnTheSameFailedElection()
    {
        var town = ConsecutiveCouncilHandovers(registerSecondCandidates: false).RoundTrip();
        var first = town.Government.Changes[0];
        var second = town.Government.Changes[1];
        var failed = Assert.Single(town.Council.ElectionHistory,
            election => election.Id == first.ForcedCouncilElectionId);
        Assert.Equal("failed", failed.Stage);
        Assert.Equal(second.ApprovedTick, failed.OpenedTick);
        Assert.Null(second.ForcedCouncilElectionId);
        var claimingSecond = second with { ForcedCouncilElectionId = failed.Id };
        town.Government = town.Government with
        {
            Changes = [first with { ForcedCouncilElectionId = null }, claimingSecond],
        };
        town.Validate(); // Each reference is otherwise valid on its own.
        town.Government = town.Government with { Changes = [first, claimingSecond] };

        Assert.Throws<InvalidDataException>(() => town.RoundTrip());
    }

    [Fact]
    public void ACancelledHandoverCannotOwnALaterLiveElection()
    {
        var town = ConsecutiveCouncilHandovers(registerSecondCandidates: true).RoundTrip();
        var first = town.Government.Changes[0];
        var second = town.Government.Changes[1];
        var live = Assert.IsType<TownElection>(town.Council.Election);
        Assert.Equal(first.SettledTick, live.OpenedTick);
        Assert.Equal(live.Id, second.ForcedCouncilElectionId);
        town.Government = town.Government with
        {
            Changes = [first with { ForcedCouncilElectionId = null }, second with { ForcedCouncilElectionId = null }],
        };
        town.Validate(); // The live election and both native changes remain structurally valid.
        town.Government = town.Government with
        {
            Changes = [first with { ForcedCouncilElectionId = live.Id }, second with { ForcedCouncilElectionId = null }],
        };

        Assert.Throws<InvalidDataException>(() => town.RoundTrip());
    }

    [Fact]
    public void PrivateSavesRequireExplicitForcedElectionProvenanceAndPreserveRefusedBytes()
    {
        using var world = new PrivateWorldRuntime("forced-council-save", startPace: WorldStartPace.FounderSetup);
        world.PlaceFounder("founder:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", new GridPoint(0, 0));
        world.PlaceFounder("founder:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", new GridPoint(1, 2));
        world.PlaceFounder("founder:cccccccccccccccccccccccccccccccc", new GridPoint(2, 2));
        world.PlaceFounder("founder:dddddddddddddddddddddddddddddddd", new GridPoint(3, 2));
        world.StartWorld(resume: false);
        var initial = world.ExportState();
        Assert.Equal(0, initial.Society.Society.WorldTick);
        Assert.Equal(4, Assert.Single(initial.Towns!).ResidentIds.Count);
        var forced = WithNativeGovernmentHandover(initial, ElectedMayor);
        var ordinary = WithNativeGovernmentHandover(initial,
            new(TownArrangementRules.Council, TownArrangementRules.Mayor));
        var directory = Directory.CreateTempSubdirectory("forced-council-provenance-");
        try
        {
            var path = Path.Combine(directory.FullName, "world.json");
            var file = new PrivateWorldStateFile(path);
            foreach (var state in new[] { ordinary, forced })
            {
                var expected = Assert.Single(Assert.Single(state.Towns!).Government!.Changes).ForcedCouncilElectionId;
                using var current = PrivateWorldRuntime.Restore(state);
                file.Save(current);
                var bytes = File.ReadAllBytes(path);
                var change = SavedGovernmentChange(JsonNode.Parse(bytes)!);
                Assert.True(change.TryGetPropertyValue("forcedCouncilElectionId", out var marker));
                Assert.Equal(expected, marker?.GetValue<string>());
                using var restored = file.LoadOrCreate(state.WorldSeed);
                Assert.Equal(expected, Assert.Single(Assert.Single(restored.Towns).Government!.Changes).ForcedCouncilElectionId);
                Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
                Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            Assert.Null(Assert.Single(Assert.Single(ordinary.Towns!).Government!.Changes).ForcedCouncilElectionId);
            var forcedTown = Assert.Single(forced.Towns!);
            Assert.Equal(Assert.IsType<TownElection>(forcedTown.Governance!.Election).Id,
                Assert.Single(forcedTown.Government!.Changes).ForcedCouncilElectionId);

            var currentBytes = PrivateWorldRuntimeCodec.Encode(forced);
            var missing = JsonNode.Parse(currentBytes)!;
            Assert.True(SavedGovernmentChange(missing).Remove("forcedCouncilElectionId"));
            AssertRefusedFile(Encoding.UTF8.GetBytes(missing.ToJsonString()));

            var older = JsonNode.Parse(currentBytes)!;
            older["state"]!["schemaVersion"] = PrivateWorldRuntime.StateSchemaVersion - 1;
            Assert.NotNull(SavedGovernmentChange(older)["forcedCouncilElectionId"]);
            var error = AssertRefusedFile(Encoding.UTF8.GetBytes(older.ToJsonString()));
            Assert.Contains($"minimum supported schema {PrivateWorldRuntime.StateSchemaVersion}", error.Message,
                StringComparison.Ordinal);

            InvalidDataException AssertRefusedFile(byte[] refused)
            {
                Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(refused));
                File.WriteAllBytes(path, refused);
                var exception = Assert.Throws<InvalidDataException>(() => file.LoadOrCreate(initial.WorldSeed));
                Assert.Equal(refused, File.ReadAllBytes(path));
                return exception;
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private static Town ForcedCouncilHandover(string id = "town:test", bool registerCandidates = true)
    {
        var town = new Town("a", "b", "c", "d", "e") { Id = id };
        if (registerCandidates) town.RegisterCouncil("a", "b", "c");
        town.Yes(town.Propose(RepresentativeOnly), "a", "b", "c");
        Assert.Equal("handover", Assert.Single(town.Government.Changes).Status);
        return town;
    }

    private static Town ConsecutiveCouncilHandovers(bool registerSecondCandidates)
    {
        var town = ForcedCouncilHandover(registerCandidates: false);
        town.Advance(TownArrangementRules.HandoverDays * Day);
        Assert.Equal("cancelled", Assert.Single(town.Government.Changes).Status);
        if (registerSecondCandidates) town.RegisterCouncil("a", "b", "c");
        town.Yes(town.Propose(RepresentativeOnly), "a", "b", "c");
        Assert.Equal(2, town.Government.Changes.Count);
        Assert.Equal("handover", town.Government.Changes[1].Status);
        return town;
    }

    private static PrivateWorldRuntimeState WithNativeGovernmentHandover(PrivateWorldRuntimeState state, TownArrangement target)
    {
        var town = Assert.Single(state.Towns!);
        var adults = town.ResidentIds;
        var day = state.WorldSystems!.Config.TicksPerDay;
        var council = town.Governance!;
        foreach (var actor in adults.Take(3))
            council = TownGovernanceRules.Register(council, actor, true, null, adults, 0);
        var (proposedCouncil, government) = TownGovernmentRules.Propose(council, town.Government!, town.Id,
            adults[0], target, false, adults, 0, day);
        var change = Assert.Single(government.Changes);
        foreach (var voter in adults.Take(3)) government = TownGovernmentRules.Vote(government, change.Id, voter, true, 0);
        (council, government) = TownGovernmentRules.Advance(proposedCouncil, government, town.Id, town.Name,
            state.WorldSeed, adults, 0, day);
        return state with { Towns = [town with { Governance = council, Government = government }] };
    }

    private static JsonObject SavedGovernmentChange(JsonNode document) =>
        document["state"]!["towns"]![0]!["government"]!["changes"]![0]!.AsObject();
}
