using System.Reflection;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class GrownAgentHelperMemoryTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task NativeAdultsFileLandRequestsThroughPublicAndPersonalPaths(bool later, bool shortControl, bool formedHousehold)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await (later ? LaterAdult : ConversationAdult).Value);
        var native = state.Society.Society.Births.OrderBy(birth => birth.CommittedTick).Last().ChildId;
        var household = state.Society.Society.GetInhabitant(native).HouseholdId;
        if (formedHousehold)
        {
            state = await FormNativeLandRequestHousehold(state, native);
            Assert.True(state.Society.Society.GetInhabitant(native).HouseholdId!.Length > (later ? 256 : 128));
        }
        var control = state.Society.Society.Inhabitants.First(person => person.Id.Length <= 128 &&
            person.Status == SocietyInhabitantStatus.Active && person.HouseholdId == household &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var actor = shortControl ? control : native;
        Assert.True(native.Length > (later ? 256 : 128));
        Assert.Equal(later ? 3 : 1, state.Society.Society.Births.Count);
        var town = Assert.Single(state.Towns!, item => item.ResidentIds.Contains(native));
        Assert.Contains(control, town.ResidentIds);
        using var publicWorld = PrivateWorldRuntime.Restore(state, _ => new QuietProvider());
        // Read the same free-land query used by the candidate descriptions.
        // Keep both actual positions and require an advertised plot for both.
        var query = typeof(PrivateWorldRuntime).GetMethod("RequestableLandNear", BindingFlags.Instance | BindingFlags.NonPublic)!;
        GridPoint[] Offered(string id) => (GridPoint[])query.Invoke(publicWorld,
            [town, state.Inhabitants.Single(person => person.InhabitantId == id).Position, 6])!;
        var plot = Offered(native).Intersect(Offered(control)).First();
        var requestId = new string('r', 128);
        AssertLandRequestRefused(publicWorld, new string('r', 129), control, town.Id, [plot]);
        foreach (var invalid in new[] { " " + native, native + "\0", native + ":unknown", "" })
            AssertLandRequestRefused(publicWorld, "bad-person", invalid, town.Id, [plot]);
        AssertLandRequestRefused(publicWorld, "bad-plot", actor, town.Id, [plot, plot]);
        AssertLandRequestRefused(publicWorld, "untitled-plot", actor, town.Id,
            [state.Map.Tiles.First(tile => state.Map.IsLand(tile.Position) &&
                !state.TownLandTitles!.Any(title => title.Tiles.Contains(tile.Position))).Position]);
        var result = publicWorld.RequestHouseholdLandUse(requestId, actor, town.Id, [plot]);
        Assert.True(result.Applied, result.Failure);
        var request = Assert.IsType<HouseholdLandUseRequest>(result.Request);
        Assert.Equal(actor, request.RequestedByAgentId);
        Assert.Equal(state.Society.Society.GetInhabitant(actor).HouseholdId, request.HouseholdId);
        AssertPendingLandRequest(publicWorld, state, actor, plot);
        var saved = PrivateWorldRuntimeCodec.Encode(publicWorld.ExportState());
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new QuietProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        Assert.True(publicWorld.RequestHouseholdLandUse(requestId, actor, town.Id, [plot]).IsDuplicate);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(publicWorld.ExportState()));
        AssertLandRequestRefused(publicWorld, requestId, shortControl ? native : control, town.Id, [plot]);
        foreach (var invalid in new[] { " " + actor, actor + "\0", actor + ":unknown", "" })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(publicWorld.ExportState() with
            { HouseholdLandUseRequests = [request with { RequestedByAgentId = invalid }] }));
        foreach (var invalid in new[] { " " + request.HouseholdId, request.HouseholdId + "\0", request.HouseholdId + ":unknown", "" })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(publicWorld.ExportState() with
            { HouseholdLandUseRequests = [request with { HouseholdId = invalid }] }));

        var choices = new NativeLandRequestChoices(actor, plot);
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        world.Resume();
        world.SubmitInstruction(new OwnerInstructionRequest("consider-native-land", "owner:test", actor,
            OwnerInstructionKind.Suggestive, $"Consider asking for household use of ({plot.X}, {plot.Y})."));
        var folder = Directory.CreateTempSubdirectory("native-land-request-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(folder.FullName, "world.json"), _ => choices);
            file.Save(world);
            var before = File.ReadAllBytes(file.Path);
            Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            Assert.Equal(before, File.ReadAllBytes(file.Path));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(10));
            presence.RecordAuthenticatedReconnect("land-test-owner");
            using var service = new PrivateWorldRuntimeService(world, file, presence);
            for (var step = 0; step < 20 && world.HouseholdLandUseRequests.Count == 0; step++)
            {
                Assert.True(await service.TryAdvanceOnceAsync());
                await WaitForRequests(world);
            }
            Assert.Contains(choices.Selected, selected => selected.Contains("|request_land_use|", StringComparison.Ordinal));
            AssertPendingLandRequest(world, state, actor, plot);
            saved = File.ReadAllBytes(file.Path);
            using var strict = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new QuietProvider());
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(strict.ExportState()));
            // Reply arrival is an external input. Continue two worlds from the
            // same paused checkpoint with identical synchronous idle inputs.
            world.Pause();
            var checkpoint = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var left = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(checkpoint), _ => new QuietProvider());
            using var right = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(checkpoint), _ => new QuietProvider());
            left.Resume();
            right.Resume();
            for (var step = 0; step < 3; step++)
            {
                Assert.True((await left.AdvanceOneTickAsync()).Advanced);
                Assert.True((await right.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(left.ExportState()), PrivateWorldRuntimeCodec.Encode(right.ExportState()));
            }
            AssertPendingLandRequest(left, state, actor, plot);
        }
        finally { folder.Delete(true); }
    }

    private static async Task<PrivateWorldRuntimeState> FormNativeLandRequestHousehold(PrivateWorldRuntimeState state, string actor)
    {
        var choices = new NativeLandHouseholdChoices(actor);
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        world.Resume();
        world.SubmitInstruction(new("consider-native-household", "owner:test", actor, OwnerInstructionKind.Suggestive,
            "Consider founding your own household when existing households refuse your request to join."));
        var folder = Directory.CreateTempSubdirectory("native-land-household-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(folder.FullName, "world.json"));
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(10));
            presence.RecordAuthenticatedReconnect("land-household-owner");
            using var host = new PrivateWorldRuntimeService(world, file, presence);
            for (var tick = 0; tick < 40 &&
                !(world.Society.GetInhabitant(actor).HouseholdId?.StartsWith("household:solo:" + actor + ":", StringComparison.Ordinal) ?? false); tick++)
            {
                Assert.True(await host.TryAdvanceOnceAsync());
                await WaitForRequests(world);
                var saved = File.ReadAllBytes(file.Path);
                using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new QuietProvider());
                Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            }
            var household = world.Society.GetInhabitant(actor).HouseholdId;
            Assert.True(choices.Founded);
            Assert.NotNull(household);
            Assert.StartsWith("household:solo:" + actor + ":", household);
            Assert.True(household.Length > actor.Length);
            Assert.Contains(actor, world.Society.GetHousehold(household).MemberIds);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "household_founded");
            Assert.Equal(state.Society.Society.Births.Select(birth => birth.ChildId), world.Society.Births.Select(birth => birth.ChildId));
            world.Pause();
            return world.ExportState();
        }
        finally { folder.Delete(true); }
    }

    private sealed class NativeLandHouseholdChoices(string actor) : IDecisionProvider
    {
        public bool Founded { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var own = observation.InhabitantId == actor && !Founded;
            var selected = observation.Candidates.FirstOrDefault(candidate => own && candidate.Id == "household_found") ??
                observation.Candidates.FirstOrDefault(candidate => own && candidate.Id == "household_leave") ??
                observation.Candidates.FirstOrDefault(candidate => own && candidate.Id.StartsWith("household_ask:", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => observation.InhabitantId != actor && candidate.Id == "household_refuse:" + actor) ??
                observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            if (selected.Id == "household_found") Founded = true;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                new Dictionary<string, double> { [selected.Id] = 1 },
                ChosenName: observation.NeedsName ? observation.Self!.Name : null,
                ChosenPersonality: observation.NeedsPersonality ? "patient" : null,
                ChosenAspiration: observation.NeedsAspiration ? "build a home" : null));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LandFilingStillRefusesActualYoungAndNonresidentPeople(bool nonresident)
    {
        PrivateWorldRuntimeState state;
        string actor;
        string town;
        if (nonresident)
        {
            var scenario = GuardianPlacementTestFixture.Prepared(1, 0);
            state = scenario.State;
            actor = scenario.Guardians[0];
            town = scenario.OriginalTownId;
            Assert.DoesNotContain(actor, state.Towns!.Single(item => item.Id == town).ResidentIds);
        }
        else
        {
            state = PrivateWorldRuntimeCodec.Decode(await Born.Value);
            actor = Assert.Single(state.Society.Society.Births).ChildId;
            town = state.Towns!.Single(item => item.ResidentIds.Contains(actor)).Id;
            Assert.Equal(SocietyAgeBand.Infant, state.Society.Society.GetInhabitant(actor).AgeBand);
        }
        using var world = PrivateWorldRuntime.Restore(state, _ => new QuietProvider());
        AssertLandRequestRefused(world, "ineligible-person", actor, town,
            [state.TownLandTitles!.First(item => item.TownId == town).Tiles[0]]);
    }

    private static void AssertLandRequestRefused(PrivateWorldRuntime world, string key, string actor,
        string town, GridPoint[] plot)
    {
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.RequestHouseholdLandUse(key, actor, town, plot).Applied);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static void AssertPendingLandRequest(PrivateWorldRuntime world, PrivateWorldRuntimeState initial,
        string actor, GridPoint plot)
    {
        var request = Assert.Single(world.HouseholdLandUseRequests);
        Assert.Equal(actor, request.RequestedByAgentId);
        Assert.Equal(initial.Society.Society.GetInhabitant(actor).HouseholdId, request.HouseholdId);
        Assert.Equal("pending", request.Status);
        Assert.Equal([plot], request.Tiles);
        Assert.Empty(request.Consents);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(initial.HouseholdLandUseRights),
            JsonSerializer.SerializeToUtf8Bytes(world.HouseholdLandUseRights));
        var proposal = Assert.Single(world.Towns.Single(town => town.Id == request.TownId).Governance!.Proposals,
            item => item.Id == request.CouncilProposalId);
        Assert.Equal(actor, proposal.AuthorId);
        Assert.Equal("pending", proposal.Status);
        Assert.Empty(proposal.Votes);
    }

    private sealed class NativeLandRequestChoices(string actor, GridPoint plot) : IDecisionProvider
    {
        public List<string> Selected { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var selected = observation.InhabitantId == actor ? observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id.Contains("|request_land_use|", StringComparison.Ordinal) &&
                candidate.Description.Contains($"({plot.X}, {plot.Y})", StringComparison.Ordinal)) : null;
            selected ??= observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            Selected.Add(selected.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate == selected ? 1d : 0d, StringComparer.Ordinal),
                CivicLandTiles: selected.Id.Contains("|request_land_use|", StringComparison.Ordinal)
                    ? [new(plot.X, plot.Y)] : null));
        }
    }
}
