using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;
using GodotSnapshot = ClankerWorld.GodotClient.UI.OwnerWorldSnapshot;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownGovernanceRuntimeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string FirstAuthor = "founder:00000000000000000000000000000001";
    private const string SecondAuthor = "founder:00000000000000000000000000000003";
    private const string FirstText = "Publish harvest dates for our Town.";
    private const string SecondText = "Share notices about storms in our Town.";

    [Fact]
    public async Task TwoGeneratedTownsUseOrdinaryPersonalTurnsAndClientProjectionAndRoundtripIndependently()
    {
        var providers = new ConcurrentDictionary<string, CivicProvider>(StringComparer.Ordinal);
        using var generated = NormalPathWorld.CreateGenerated("council-two-normal-towns", id => providers.GetOrAdd(id, key => new CivicProvider(key)));
        var initial = generated.ExportState();
        var ids = initial.Towns![0].ResidentIds.ToArray();
        var first = initial.Towns[0];
        var secondSite = initial.Map.Tiles.Select(t => t.Position).First(p => initial.Map.IsBuildable(p) && !first.BorderTiles.Contains(p));
        var second = new TownRuntimeState("town:second", "Second Town", "founded", 0, ids[2..], [], [secondSite], secondSite,
            TownGovernanceState.Create(ids[2..]));
        var split = initial with
        {
            Towns = [first with { ResidentIds = ids[..2], Governance = TownGovernanceState.Create(ids[..2]) }, second],
            Inhabitants = initial.Inhabitants.Select(p => p with
            {
                Position = ids.AsSpan(0, 2).Contains(p.InhabitantId) ? first.OriginSite!.Value : secondSite,
                HungerBasisPoints = 8_000,
            }).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(split, id => providers.GetOrAdd(id, key => new CivicProvider(key)));
        var directory = Directory.CreateTempSubdirectory("clankerworld-civic-client-");
        try
        {
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(1));
            presence.RecordAuthenticatedReconnect("test-device");
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"));
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world, file, presence, logger);
            for (var tick = 0; tick < 80 && world.Towns.Any(t => !t.Governance!.Proposals.Any(p => p.Status == "passed")); tick++)
            {
                Assert.True(await service.TryAdvanceOnceAsync());
                await Task.Delay(2);
            }
            Assert.All(world.Towns, town =>
            {
                var proposal = Assert.Single(town.Governance!.Proposals);
                Assert.Equal("passed", proposal.Status);
                Assert.Equal(2, proposal.Votes.Count);
                Assert.All(proposal.Votes, vote => Assert.Contains(vote.AgentId, town.ResidentIds));
                Assert.Equal(town.Id == "town:first" ? FirstText : SecondText, proposal.Text);
                Assert.NotEmpty(town.Governance.Knowledge);
            });
            foreach (var provider in providers.Values)
            {
                if (provider.Observations.First().InhabitantId is not (FirstAuthor or SecondAuthor))
                    Assert.Contains(provider.Observations, o => o.Candidates.Any(c => c.Id.Contains("|read|", StringComparison.Ordinal)));
                Assert.Contains(provider.Observations, o => o.Self?.CivicNote?.Contains("Town", StringComparison.Ordinal) == true);
                Assert.DoesNotContain(provider.Observations, o => o.Self?.CivicNote?.Contains("tick ", StringComparison.Ordinal) == true);
            }
            Assert.Contains(logger.Messages, m => m.Contains("town_civic", StringComparison.Ordinal) && m.Contains("transition=DecisionRecorded", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, m => m.Contains(FirstText, StringComparison.Ordinal) || m.Contains(SecondText, StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, m => m.Contains("town_civic_action", StringComparison.Ordinal) || m.Contains("Read Town notice", StringComparison.Ordinal));
            world.Pause();
            var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            file.Save(world);
            using var restored = file.LoadOrCreate(world.ExportState().WorldSeed);
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            var snapshot = new OwnerWorldObservationStore(restored).GetSnapshot();
            var client = JsonSerializer.Deserialize<GodotSnapshot>(JsonSerializer.Serialize(snapshot, JsonOptions), JsonOptions)!;
            Assert.Equal(2, client.Towns.Count);
            Assert.All(client.Towns, town =>
            {
                Assert.Equal(2, town.Governance!.MemberNames.Count);
                Assert.Equal("passed", Assert.Single(town.Governance.Proposals).Status);
                Assert.Null(town.Governance.Election);
            });
            var savedClock = restored.WorldTick;
            Assert.False((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(savedClock, restored.WorldTick);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalTurnsRegisterCandidatesAndCastStructuredBallotsWhoseConsentAndChoicesReload(bool longDescendantId)
    {
        var observed = new ConcurrentQueue<InhabitantObservation>();
        using var source = NormalPathWorld.CreateGenerated("council-normal-election", _ => new ElectionProvider(observed));
        var firstCandidate = longDescendantId ? "child:" + FirstAuthor + ":" + new string('a', 250) : FirstAuthor;
        // Descendant IDs retain ancestry and may be longer than a model action token.
        var initialJson = Encoding.UTF8.GetString(PrivateWorldRuntimeCodec.Encode(source.ExportState()))
            .Replace(FirstAuthor, firstCandidate, StringComparison.Ordinal);
        using var generated = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(initialJson)),
            _ => new ElectionProvider(observed));
        await generated.AdvanceOneTickAsync();
        Assert.Equal(4, generated.Towns[0].Governance!.Candidates.Count);
        for (var ordinal = 5; ordinal <= 8; ordinal++)
        {
            var state = generated.ExportState();
            var site = state.Towns![0].BorderTiles.First(p => state.Map.IsBuildable(p) &&
                !state.Map.Resources.Any(r => r.Position == p) && !state.Map.CampObjects.Any(o => o.Position == p) &&
                !state.Inhabitants.Any(i => i.Position == p));
            generated.AddAgent($"agent:{ordinal:D32}", site);
        }
        var opening = generated.Towns[0].Governance!.Election!;
        Assert.Equal(4, opening.Candidates.Count);
        var positioned = generated.ExportState();
        using var world = PrivateWorldRuntime.Restore(positioned with
        {
            Inhabitants = positioned.Inhabitants.Select(p => p with { Position = positioned.Towns![0].OriginSite!.Value }).ToArray(),
        }, _ => new ElectionProvider(observed));
        for (var tick = 0; tick < 40 && world.Towns[0].Governance!.Election!.Ballots.Count < 4; tick++)
            await world.AdvanceOneTickAsync();
        var election = world.Towns[0].Governance!.Election!;
        Assert.NotEmpty(election.Ballots);
        Assert.All(election.Ballots, b => Assert.Equal(3, b.Choices.Count));
        Assert.Contains(firstCandidate, election.Candidates);
        Assert.Contains(election.Ballots, b => b.Choices.Contains(firstCandidate));
        Assert.Equal(4, election.Candidates.Count); // Later personal registrations stay in the continuing register.
        Assert.Contains(observed, o => o.Candidates.Any(c => c.Id.Contains("|ballot|", StringComparison.Ordinal)) && o.Self?.CivicNote is not null);
        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(election.Ballots.Select(b => b.AgentId + ":" + string.Join(",", b.Choices)),
            restored.Towns[0].Governance!.Election!.Ballots.Select(b => b.AgentId + ":" + string.Join(",", b.Choices)));
    }

    [Fact]
    public async Task CompletedPrivateWorldDrawAndAcceptedEventsReplayWithoutRerolling()
    {
        using var generated = NormalPathWorld.CreateGenerated("council-private-saved-draw", _ => new ActionCoverageRecorder(chooseIdle: true));
        for (var ordinal = 5; ordinal <= 8; ordinal++)
        {
            var setup = generated.ExportState();
            var site = setup.Towns![0].BorderTiles.First(p => setup.Map.IsBuildable(p) &&
                !setup.Map.Resources.Any(r => r.Position == p) && !setup.Map.CampObjects.Any(o => o.Position == p) &&
                !setup.Inhabitants.Any(i => i.Position == p));
            generated.AddAgent($"agent:{ordinal:D32}", site);
        }
        var initial = generated.ExportState();
        var society = initial.Society.Society;
        var oldDay = society.Config.TicksPerWorldDay;
        const int shortDay = 10;
        var town = initial.Towns![0];
        var ids = town.ResidentIds.ToArray();
        var governance = TownGovernanceState.Create(ids);
        foreach (var id in ids.Take(5)) governance = TownGovernanceRules.Register(governance, id, true, null, ids, 0);
        governance = TownGovernanceRules.Advance(governance, town.Id, initial.WorldSeed, ids, 0, shortDay);
        governance = TownGovernanceRules.VoteElection(governance, governance.Election!.Id, ids[0], [ids[0], ids[1], ids[2]], 0);
        governance = TownGovernanceRules.VoteElection(governance, governance.Election!.Id, ids[1], [ids[0], ids[3]], 0);
        var shortened = initial with
        {
            Towns = [town with { Governance = governance }],
            WorldSystems = RegionalWeatherRules.Initialize(initial.WorldSystems! with
            {
                Config = initial.WorldSystems.Config with { TicksPerDay = shortDay },
                RegionalWeather = null,
            }, initial.Map),
            Society = initial.Society with
            {
                Society = society with
                {
                    Config = society.Config with { TicksPerWorldDay = shortDay },
                    Inhabitants = society.Inhabitants.Select(p => p with
                    {
                        BirthTick = p.BirthTick / oldDay * shortDay,
                        BirthLifeTick = p.BirthLifeTick is { } birth ? birth / oldDay * shortDay : null,
                    }).ToArray(),
                }
            },
        };
        using var world = PrivateWorldRuntime.Restore(shortened, _ => new ActionCoverageRecorder(chooseIdle: true));
        for (var tick = 0; tick < shortDay * 2; tick++) await world.AdvanceOneTickAsync();
        var accepted = Assert.Single(world.Towns[0].Governance!.ElectionHistory);
        Assert.Equal("completed", accepted.Stage);
        Assert.Equal(3, accepted.DrawOrder.Count);
        Assert.DoesNotContain(ids[4], accepted.DrawOrder);
        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded), _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        await world.AdvanceOneTickAsync();
        await replay.AdvanceOneTickAsync();
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Equal(accepted.DrawOrder, replay.Towns[0].Governance!.ElectionHistory[0].DrawOrder);
    }

    [Fact]
    public async Task TravelersGainNoUnseenCivicInformationAndKeepRecordedCouncilMembership()
    {
        var recorder = new ActionCoverageRecorder(chooseIdle: true);
        using var generated = NormalPathWorld.CreateGenerated("council-unseen-travel", _ => recorder);
        var initial = generated.ExportState();
        var town = initial.Towns![0];
        var governance = TownGovernanceRules.SubmitProposal(town.Governance!, town.Id, FirstAuthor, "law", null,
            FirstText, "same", town.ResidentIds, 0, initial.WorldSystems!.Config.TicksPerDay);
        var far = initial.Map.Tiles.Select(t => t.Position).First(p => initial.Map.IsBuildable(p) && !town.BorderTiles.Contains(p));
        using var world = PrivateWorldRuntime.Restore(initial with
        {
            Towns = [town with { Governance = governance }],
            Inhabitants = initial.Inhabitants.Select(p => p with { Position = far }).ToArray(),
        }, _ => recorder);
        await world.AdvanceOneTickAsync();
        Assert.All(recorder.OfferedByAgent.Values, offered => Assert.DoesNotContain(offered.Keys, id =>
            id.Contains("|yes|", StringComparison.Ordinal) || id.Contains("|no|", StringComparison.Ordinal)));
        Assert.All(world.Towns[0].Governance!.Knowledge, _ => Assert.Fail("A traveler was given an unseen notice."));
        Assert.Equal(4, world.Towns[0].Governance!.Members.Count);
        Assert.Equal("pending", world.Towns[0].Governance!.Proposals[0].Status);
    }

    [Fact]
    public async Task PendingProposalRetainsWindowAcrossPauseReloadAndRejectedPreparedTick()
    {
        var provider = new CivicProvider(FirstAuthor);
        using var generated = NormalPathWorld.CreateGenerated("council-window-rollback", id => id == FirstAuthor ? provider : new ActionCoverageRecorder(chooseIdle: true));
        var before = PrivateWorldRuntimeCodec.Encode(generated.ExportState());
        var refused = await generated.AdvanceOneTickAsync(() => false);
        Assert.False(refused.Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(generated.ExportState()));
        await generated.AdvanceOneTickAsync();
        var proposal = Assert.Single(generated.Towns[0].Governance!.Proposals);
        generated.Pause();
        var paused = generated.ExportState();
        for (var attempt = 0; attempt < 3; attempt++) Assert.False((await generated.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(proposal.DeadlineTick, generated.Towns[0].Governance!.Proposals[0].DeadlineTick);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(paused)),
            _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.Equal(proposal.DeadlineTick, restored.Towns[0].Governance!.Proposals[0].DeadlineTick);
        restored.Resume();
        await restored.AdvanceOneTickAsync();
        Assert.Equal(proposal.DeadlineTick, restored.Towns[0].Governance!.Proposals[0].DeadlineTick);
    }

    [Fact]
    public async Task ConcurrentCouncilChangeDiscardsDelayedProposalVote()
    {
        var provider = new CivicProvider(FirstAuthor, holdVote: true);
        using var generated = NormalPathWorld.CreateGenerated("council-stale-held-vote", id => id == FirstAuthor ? provider : new ActionCoverageRecorder(chooseIdle: true));
        for (var tick = 0; tick < 80 && !provider.VoteStarted.Task.IsCompleted; tick++)
        {
            await generated.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(2);
        }
        await provider.VoteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var original = Assert.Single(generated.Towns[0].Governance!.Proposals);
        var state = generated.ExportState();
        var site = state.Towns![0].BorderTiles.First(p => state.Map.IsBuildable(p) &&
            !state.Map.Resources.Any(r => r.Position == p) && !state.Map.CampObjects.Any(o => o.Position == p) &&
            !state.Inhabitants.Any(i => i.Position == p));
        generated.AddAgent("agent:00000000000000000000000000000099", site);
        Assert.Equal("cancelled", generated.Towns[0].Governance!.Proposals[0].Status);
        provider.ReleaseVote.TrySetResult(true);
        for (var tick = 0; tick < 8; tick++)
        {
            await generated.AdvanceOneTickNonBlockingAsync();
            await Task.Delay(2);
        }
        var cancelled = generated.Towns[0].Governance!.Proposals.Single(p => p.Id == original.Id);
        Assert.Empty(cancelled.Votes);
        Assert.Equal("cancelled", cancelled.Status);
        generated.Validate();
    }

    [Theory]
    [InlineData("[7]")]
    [InlineData("[{}]")]
    [InlineData("null")]
    [InlineData("[\"a\",\"b\",\"c\",\"d\"]")]
    public async Task MalformedStructuredCivicBallotFailsAsProviderData(string ballot)
    {
        using var handler = new BallotHandler(ballot);
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model");
        var observation = new InhabitantObservation("a", 0, 0, 0, "digest", 8_000, [new("safe_idle", "Wait safely.")]);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await provider.DecideAsync(new("request", provider.ProviderEpoch, observation)));
    }

    private sealed class ElectionProvider(ConcurrentQueue<InhabitantObservation> observed) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var o = request.Observation;
            observed.Enqueue(o);
            var selected = o.Candidates.FirstOrDefault(c => c.Id.Contains("|register|", StringComparison.Ordinal)) ??
                o.Candidates.FirstOrDefault(c => c.Id.Contains("|read|", StringComparison.Ordinal)) ??
                o.Candidates.FirstOrDefault(c => c.Id.Contains("|ballot|", StringComparison.Ordinal)) ??
                o.Candidates.Single(c => c.Id == "safe_idle");
            var choices = selected.Id.Contains("|ballot|", StringComparison.Ordinal)
                ? o.Candidates.Where(c => c.Id.Contains("|single|", StringComparison.Ordinal)).Select(c => c.Id.Split('|')[4]).Take(3).ToArray() : null;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, o.InhabitantId, Kind, ProviderEpoch,
                o.RunEpoch, o.DecisionGeneration, o.ObservationDigest, selected.Id, 1,
                o.Candidates.ToDictionary(c => c.Id, c => c.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal), CivicBallot: choices));
        }
    }

    private sealed class BallotHandler(string ballot) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var answer = "{\"selected_candidate_id\":\"safe_idle\",\"confidence\":1,\"civic_ballot\":" + ballot + "}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = answer } } } }), Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class CivicProvider(string actor, bool holdVote = false) : IDecisionProvider
    {
        public ConcurrentQueue<InhabitantObservation> Observations { get; } = new();
        public TaskCompletionSource<bool> VoteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseVote { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            Observations.Enqueue(observation);
            var ownTown = actor is FirstAuthor or "founder:00000000000000000000000000000002" ? "town:first" : "town:second";
            var legal = observation.Candidates.Where(c => c.Id.StartsWith("civic|" + ownTown + "|", StringComparison.Ordinal)).ToArray();
            var text = ownTown == "town:first" ? FirstText : SecondText;
            var author = actor is FirstAuthor or SecondAuthor;
            var selected = legal.FirstOrDefault(c => c.Id.Contains("|yes|", StringComparison.Ordinal)) ??
                legal.FirstOrDefault(c => c.Id.Contains("|read|", StringComparison.Ordinal)) ??
                (author && observation.Self?.CivicNote?.Contains(text, StringComparison.Ordinal) != true
                    ? legal.FirstOrDefault(c => c.Id.Contains("|propose|", StringComparison.Ordinal)) : null) ??
                observation.Candidates.Single(c => c.Id == "safe_idle");
            if (holdVote && selected.Id.Contains("|yes|", StringComparison.Ordinal))
            {
                VoteStarted.TrySetResult(true);
                // Ignore cancellation deliberately: delayed provider replies must still be rejected by authority checks.
                await ReleaseVote.Task;
            }
            return new(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                selected.Id, 1, observation.Candidates.ToDictionary(c => c.Id, c => c.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicProposal: selected.Id.Contains("|propose|", StringComparison.Ordinal) ? text : null);
        }
    }
}
