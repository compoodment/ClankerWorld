using System.Text.Json;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PortBoatRuntimeTests
{
    private sealed record NativeBoatHistory(byte[] Bytes, IReadOnlyList<BoatTripRequest> Cancelled,
        BoatTripRequest Waiting, string OriginalBoat);
    private static readonly Lazy<Task<NativeBoatHistory>> ClosedBoatHistory = new(BuildClosedBoatHistoryAsync);

    [Fact]
    public async Task RepeatedNativeCancellationsKeepARecentWindowAndAnOlderWaitingRequest()
    {
        var history = await ClosedBoatHistory.Value;
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(history.Bytes));
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        Assert.Equal(41, snapshot.BoatRequests.Count);
        var prepared = PrivateWorldHistory.Prepare(world.ExportState());
        Assert.Equal(history.Waiting, Assert.Single(prepared.State.BoatTransport.Requests, item => item.Id == history.Waiting.Id));
        Assert.Equal(41, prepared.State.BoatTransport.Requests.Count);
        Assert.Equal(history.Cancelled.TakeLast(40),
            prepared.State.BoatTransport.Requests.Where(request => request.Status == "cancelled"));
        Assert.Equal(history.Cancelled.Take(24), prepared.Segment.ClosedBoatRequests!);
        Assert.Equal(new RetiredBoatRequestRange(2, 25), Assert.Single(prepared.State.BoatTransport.RetiredRequestRanges));
        Assert.Equal(65, prepared.State.BoatTransport.Sequence);
        Assert.Equal(history.OriginalBoat, JsonSerializer.Serialize(Assert.Single(prepared.State.BoatTransport.Boats)));
        // Preparing a plan does not change the live world or publish retirement.
        Assert.Equal(history.Bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(prepared.State));
    }

    [Fact]
    public async Task ArchivedBoatOutcomesReloadReplayAndExtendWithoutLosingTheOlderQueue()
    {
        var history = await ClosedBoatHistory.Value;
        var directory = Directory.CreateTempSubdirectory("clankerworld-boat-history-");
        try
        {
            var policy = HistoryBoatPolicy();
            using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(history.Bytes), policy);
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), policy.CreateProvider);
            var visible = JsonSerializer.Serialize(new OwnerWorldObservationStore(scenario.World).GetSnapshot().BoatRequests);
            Assert.True(file.Save(scenario.World));
            var compacted = scenario.World.ExportState();
            Assert.Equal(41, compacted.BoatTransport.Requests.Count);
            Assert.Equal(visible, JsonSerializer.Serialize(new OwnerWorldObservationStore(scenario.World).GetSnapshot().BoatRequests));
            Assert.Equal(history.Cancelled.Take(24), ReadArchivedBoatRequests(file));
            var bytes = PrivateWorldRuntimeCodec.Encode(compacted);
            Assert.Equal(bytes, File.ReadAllBytes(file.Path));
            Assert.False((await scenario.World.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
            var replayPolicy = HistoryBoatPolicy();
            using var replay = new BoatScenario(PrivateWorldRuntimeCodec.Decode(bytes), replayPolicy);
            using var loaded = file.LoadOrCreate(compacted.WorldSeed);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
            for (var step = 0; step < 3; step++)
            {
                Assert.True((await scenario.World.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.World.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()),
                    PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
            }
            policy.Trips = true;
            await scenario.UntilAsync(() => scenario.World.BoatRequests.Any(request =>
                request.PassengerId == Follower && request.Status == "waiting"), 120);
            var next = Assert.Single(scenario.World.BoatRequests, request => request.PassengerId == Follower && request.Status == "waiting");
            Assert.Equal(66, next.Sequence);
            policy.Trips = false;
            policy.CancelWaiting = true;
            await scenario.UntilAsync(() => scenario.World.BoatRequests.Any(request => request.Id == next.Id && request.Status == "cancelled"), 8);
            Assert.True(file.Save(scenario.World));
            Assert.Equal(new RetiredBoatRequestRange(2, 26), Assert.Single(scenario.World.ExportState().BoatTransport.RetiredRequestRanges));
            Assert.Equal(history.Cancelled.Take(25), ReadArchivedBoatRequests(file));
            Assert.Equal(history.Waiting, Assert.Single(scenario.World.BoatRequests, request => request.Id == history.Waiting.Id));
            Assert.Equal(history.OriginalBoat, JsonSerializer.Serialize(Assert.Single(scenario.World.Boats)));
            using var extended = file.LoadOrCreate(compacted.WorldSeed);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()), PrivateWorldRuntimeCodec.Encode(extended.ExportState()));
            var checkpoint = File.ReadAllBytes(file.Path);
            var archive = Directory.GetFiles(file.Path + ".history", "*.json")[0];
            var original = File.ReadAllBytes(archive);
            File.Delete(archive);
            Assert.Throws<FileNotFoundException>(() => file.LoadOrCreate(compacted.WorldSeed));
            Assert.Equal(checkpoint, File.ReadAllBytes(file.Path));
            File.WriteAllBytes(archive, original);
            File.WriteAllText(archive, "corrupt");
            Assert.Throws<InvalidDataException>(() => file.LoadOrCreate(compacted.WorldSeed));
            Assert.Equal(checkpoint, File.ReadAllBytes(file.Path));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedBoatHistoryWriteKeepsTheLiveQueueAndPreviousCheckpoint(bool failCheckpoint)
    {
        var history = await ClosedBoatHistory.Value;
        var directory = Directory.CreateTempSubdirectory("clankerworld-boat-history-failure-");
        try
        {
            using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(history.Bytes));
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            File.WriteAllBytes(file.Path, history.Bytes);
            if (failCheckpoint)
            {
                File.Move(file.Path, file.Path + ".previous");
                Directory.CreateDirectory(file.Path);
            }
            else File.WriteAllText(file.Path + ".history", "block archive directory creation");
            Assert.Throws<IOException>(() => file.Save(world));
            Assert.Equal(history.Bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            Assert.Equal(history.Bytes, File.ReadAllBytes(failCheckpoint ? file.Path + ".previous" : file.Path));
            Assert.Empty(world.ExportState().BoatTransport.RetiredRequestRanges);
            if (failCheckpoint)
                Assert.Equal(history.Cancelled.Take(24), ReadArchivedBoatRequests(file));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("zero")]
    [InlineData("reversed")]
    [InlineData("future")]
    [InlineData("adjacent")]
    [InlineData("overlap")]
    [InlineData("unordered")]
    [InlineData("live-collision")]
    [InlineData("gap")]
    [InlineData("missing-waiting")]
    [InlineData("missing-archive")]
    public async Task DamagedBoatRetirementAuthorityIsRefused(string damage)
    {
        var history = await ClosedBoatHistory.Value;
        var directory = Directory.CreateTempSubdirectory("clankerworld-boat-history-damage-");
        try
        {
            using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(history.Bytes));
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            Assert.True(file.Save(world));
            var original = File.ReadAllBytes(file.Path);
            var document = JsonNode.Parse(original)!;
            var state = document["state"]!;
            var transport = state["boatTransport"]!;
            var ranges = damage switch
            {
                "zero" => "[{\"firstSequence\":0,\"lastSequence\":25}]",
                "reversed" => "[{\"firstSequence\":26,\"lastSequence\":25}]",
                "future" => "[{\"firstSequence\":2,\"lastSequence\":66}]",
                "adjacent" => "[{\"firstSequence\":2,\"lastSequence\":12},{\"firstSequence\":13,\"lastSequence\":25}]",
                "overlap" => "[{\"firstSequence\":2,\"lastSequence\":25},{\"firstSequence\":24,\"lastSequence\":25}]",
                "unordered" => "[{\"firstSequence\":14,\"lastSequence\":25},{\"firstSequence\":2,\"lastSequence\":12}]",
                "live-collision" => "[{\"firstSequence\":1,\"lastSequence\":25}]",
                "gap" => "[{\"firstSequence\":2,\"lastSequence\":24}]",
                _ => null,
            };
            if (ranges is not null) transport["retiredRequestRanges"] = JsonNode.Parse(ranges);
            else if (damage == "missing") transport.AsObject().Remove("retiredRequestRanges");
            else if (damage == "null") transport["retiredRequestRanges"] = null;
            else if (damage == "missing-waiting") transport["requests"]!.AsArray().RemoveAt(0);
            else if (damage == "missing-archive") state["historyArchiveHead"] = null;
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(System.Text.Encoding.UTF8.GetBytes(document.ToJsonString())));
            Assert.Equal(original, File.ReadAllBytes(file.Path));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("arrived")]
    [InlineData("returned")]
    [InlineData("death")]
    public async Task AnOlderVoyageRetainsItsBindingAndArchivesItsFinalOutcome(string outcome)
    {
        var history = await ClosedBoatHistory.Value;
        var policy = HistoryBoatPolicy();
        policy.IdleActors.Remove(BoatPolicy.Author);
        policy.Trips = true;
        policy.TripActor = BoatPolicy.Author;
        using var departure = new BoatScenario(PrivateWorldRuntimeCodec.Decode(history.Bytes), policy);
        var origin = departure.World.WorldSimulation.Buildings.Single(port => port.InstanceId == departure.World.Boats[0].DockedPortId);
        departure.World.SubmitInstruction(new("return-history-traveler", "owner:test", BoatPolicy.Author,
            OwnerInstructionKind.MustDo, $"move to {origin.Entrance!.Value.X},{origin.Entrance.Value.Y}"));
        await departure.UntilAsync(() => departure.World.Inhabitants.Single(person =>
            person.InhabitantId == BoatPolicy.Author).Position == origin.Entrance, 120);
        var blockedLanding = departure.World.Inhabitants.Single(person => person.InhabitantId == Blockers[0]).Position;
        departure.World.SubmitInstruction(new("clear-history-landing", "owner:test", Blockers[0],
            OwnerInstructionKind.MustDo, "move to 194,10"));
        policy.IdleActors.Remove(Blockers[0]);
        await departure.UntilAsync(() => departure.World.Boats[0].Journey is not null, 30);
        if (outcome != "arrived")
        {
            departure.World.SubmitInstruction(new("reblock-history-landing", "owner:test", Blockers[0],
                OwnerInstructionKind.MustDo, $"move to {blockedLanding.X},{blockedLanding.Y}"));
            await departure.UntilAsync(() => departure.World.Inhabitants.Single(person =>
                person.InhabitantId == Blockers[0]).Position == blockedLanding, 30);
        }
        var state = departure.World.ExportState();
        if (outcome == "death")
        {
            var checkpoint = state.Society.Society;
            var maximum = checkpoint.Config.DayLifecycle!.MaximumDay;
            var birth = checkpoint.LifeTickAt(checkpoint.WorldTick + 1) - maximum * checkpoint.Config.TicksPerLifecycleAge;
            state = state with
            {
                Society = state.Society with
                {
                    Society = checkpoint with
                    {
                        Config = checkpoint.Config with { BaseNaturalMortalityBasisPoints = 0, NaturalMortalitySlopeBasisPoints = 0 },
                        Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == BoatPolicy.Author ? person with
                        {
                            BirthTick = checkpoint.LifeClock is null ? birth : person.BirthTick,
                            BirthLifeTick = checkpoint.LifeClock is null ? null : birth,
                            AgeBand = SocietyAgeBand.Elder,
                            LastLifecycleYearChecked = maximum - 1,
                        } : person).ToArray(),
                    },
                },
            };
        }
        policy.Trips = false;
        policy.IdleActors.Add(BoatPolicy.Author);
        policy.IdleActors.Add(Blockers[0]);
        using var voyage = new BoatScenario(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), policy);
        var directory = Directory.CreateTempSubdirectory("clankerworld-boat-voyage-history-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), policy.CreateProvider);
            var boat = JsonSerializer.Serialize(Assert.Single(voyage.World.Boats));
            Assert.True(file.Save(voyage.World));
            Assert.Equal(boat, JsonSerializer.Serialize(Assert.Single(voyage.World.Boats)));
            Assert.Equal("underway", Assert.Single(voyage.World.BoatRequests, request => request.Id == history.Waiting.Id).Status);
            Assert.Equal(41, voyage.World.BoatRequests.Count);
            Assert.Equal(history.Cancelled.Take(24), ReadArchivedBoatRequests(file));
            var bytes = PrivateWorldRuntimeCodec.Encode(voyage.World.ExportState());
            Assert.False((await voyage.World.AdvanceOneTickAsync(() => false)).Advanced);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(voyage.World.ExportState()));
            if (outcome == "death")
            {
                await voyage.UntilAsync(() => (voyage.World.ExportState().DeceasedInhabitants ?? []).Any(person =>
                    person.InhabitantId == BoatPolicy.Author), 4);
                Assert.Contains("travel-jug", voyage.World.Boats[0].GroundCargoLotIds!);
                _ = file.Save(voyage.World);
                using var restored = file.LoadOrCreate(state.WorldSeed);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(voyage.World.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
                Assert.Contains("travel-jug", restored.Boats[0].GroundCargoLotIds!);
            }
            await voyage.UntilAsync(() => voyage.World.Boats[0].Journey is null, 120);
            var finished = Assert.Single(voyage.World.BoatRequests, request => request.Id == history.Waiting.Id);
            Assert.Equal(outcome == "arrived" ? "arrived" : "returned", finished.Status);
            Assert.True(file.Save(voyage.World));
            Assert.Equal(40, voyage.World.BoatRequests.Count);
            Assert.Equal(new RetiredBoatRequestRange(1, 25), Assert.Single(voyage.World.ExportState().BoatTransport.RetiredRequestRanges));
            Assert.Equal(new[] { finished }.Concat(history.Cancelled.Take(24)), ReadArchivedBoatRequests(file));
            using var complete = file.LoadOrCreate(state.WorldSeed);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(voyage.World.ExportState()), PrivateWorldRuntimeCodec.Encode(complete.ExportState()));
            if (outcome == "death")
            {
                var jug = complete.Society.Inventory.GetLot("travel-jug");
                var water = complete.Society.Inventory.GetLot("travel-water");
                Assert.Equal(jug.Id, water.ContainerLotId);
                Assert.Equal(jug.OwnerId, water.OwnerId);
                Assert.Null(water.GroundPosition);
                Assert.NotNull(jug.GroundPosition);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private static BoatPolicy HistoryBoatPolicy()
    {
        var policy = new BoatPolicy { TripActor = Follower };
        policy.IdleActors.Add(BoatPolicy.Author);
        foreach (var blocker in Blockers) policy.IdleActors.Add(blocker);
        return policy;
    }

    private static BoatTripRequest[] ReadArchivedBoatRequests(PrivateWorldStateFile file) =>
        Directory.GetFiles(file.Path + ".history", "*.json")
            .SelectMany(path => JsonSerializer.Deserialize<PrivateWorldHistorySegment>(File.ReadAllBytes(path))!.ClosedBoatRequests ?? [])
            .OrderBy(request => request.Sequence).ToArray();

    private static async Task<NativeBoatHistory> BuildClosedBoatHistoryAsync()
    {
        var policy = new BoatPolicy { Trips = true };
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), policy);
        Assert.Empty(scenario.World.BoatRequests);
        var originalBoat = JsonSerializer.Serialize(Assert.Single(scenario.World.Boats));
        var destination = scenario.World.Towns[0].Projects.Single(project =>
            project.CompletedBuildingId is not null && project.CompletedBuildingId != scenario.World.Boats[0].DockedPortId);
        AddLandingBlockers(scenario, destination.CompletedBuildingId!);
        await scenario.UntilAsync(() => scenario.World.BoatRequests.Any(request => request.PassengerId == BoatPolicy.Author), 120);
        var waiting = Assert.Single(scenario.World.BoatRequests);
        Assert.Equal("waiting", waiting.Status);
        policy.Trips = false;
        scenario.World.SubmitInstruction(new("leave-boat-approach", "owner:test", BoatPolicy.Author,
            OwnerInstructionKind.MustDo, "move to 194,10"));
        await scenario.UntilAsync(() => scenario.World.Inhabitants.Single(person =>
            person.InhabitantId == BoatPolicy.Author).Position == new GridPoint(194, 10), 30);
        policy.IdleActors.Add(BoatPolicy.Author);
        policy.TripActor = Follower;
        var cancelled = new List<BoatTripRequest>();

        for (var cycle = 0; cycle < 64; cycle++)
        {
            policy.Trips = true;
            policy.CancelWaiting = false;
            await scenario.UntilAsync(() => scenario.World.BoatRequests.Any(request =>
                request.PassengerId == Follower && request.Status == "waiting"), 120);
            var request = Assert.Single(scenario.World.BoatRequests, request =>
                request.PassengerId == Follower && request.Status == "waiting");
            policy.Trips = false;
            policy.CancelWaiting = true;
            await scenario.UntilAsync(() => scenario.World.BoatRequests.Any(item =>
                item.Id == request.Id && item.Status == "cancelled"), 8);
            cancelled.Add(Assert.Single(scenario.World.BoatRequests, item => item.Id == request.Id));
            Assert.Equal(waiting, Assert.Single(scenario.World.BoatRequests, item => item.Id == waiting.Id));
            Assert.Equal(originalBoat, JsonSerializer.Serialize(Assert.Single(scenario.World.Boats)));
            scenario.World.Validate();
            if (cycle is 15 or 31 or 47 or 63)
            {
                var bytes = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
                using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
                Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            }
        }

        Assert.Equal(64, cancelled.Select(request => request.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(65, scenario.World.ExportState().BoatTransport.Sequence);
        return new(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()), cancelled, waiting, originalBoat);
    }
}
