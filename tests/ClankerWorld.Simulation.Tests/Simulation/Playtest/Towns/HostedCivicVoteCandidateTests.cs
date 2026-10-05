using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class HostedCivicVoteCandidateTests
{
    private const string Author = "founder:00000000000000000000000000000001";
    private const string ProposalText = "Harvest dates: Publish harvest dates for our Town.";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOfferedHostedVoteWithOnlyItsEmptyFinalFieldOmittedLandsOnceAndReloads(bool yes)
    {
        using var provider = new HostedVoteProvider(yes);
        IDecisionProvider For(string actor) => actor == Author ? provider : new ActionCoverageRecorder(chooseIdle: true);
        using var world = NormalPathWorld.CreateGenerated("hosted-civic-vote-empty-final-field", For);
        for (var tick = 0; tick < 40 && !world.Towns[0].Governance!.Proposals.Any(p => p.Votes.Any(v => v.AgentId == Author)); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var proposal = Assert.Single(world.Towns[0].Governance!.Proposals);
        Assert.Equal(ProposalText, proposal.Text);
        Assert.True(proposal.Votes.Count == 1,
            $"Expected the eligible vote to land; got {proposal.Votes.Count} votes after {provider.OmittedVoteReplies} malformed hosted vote replies.");
        var vote = Assert.Single(proposal.Votes);
        Assert.Equal(Author, vote.AgentId);
        Assert.Equal(yes, vote.Yes);
        Assert.Equal(1, provider.OmittedVoteReplies);
        Assert.Contains(Author, proposal.Voters);
        world.Validate();

        world.Pause();
        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded), For);
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(vote, Assert.Single(restored.Towns[0].Governance!.Proposals.Single().Votes));
        world.Resume();
        restored.Resume();
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        Assert.Equal(1, provider.OmittedVoteReplies);
    }

    [Theory]
    [InlineData("unknown-proposal")]
    [InlineData("ambiguous-choice")]
    [InlineData("ambiguous-empty-choice")]
    [InlineData("multiple-empty-fields")]
    [InlineData("other-namespace")]
    public async Task AnUnofferedHostedChoiceStillFallsBackInsteadOfGuessingAnAction(string mode)
    {
        var (selected, offered) = mode switch
        {
            "unknown-proposal" => ("civic|town:test|yes|unknown", new[] { "civic|town:test|yes|known|" }),
            "ambiguous-choice" => ("civic|town:test|single|round", new[] { "civic|town:test|single|round|alice", "civic|town:test|single|round|bob" }),
            "ambiguous-empty-choice" => ("civic|town:test|single|round", new[] { "civic|town:test|single|round|", "civic|town:test|single|round|alice" }),
            "multiple-empty-fields" => ("civic|town:test|read", new[] { "civic|town:test|read||" }),
            "other-namespace" => ("route|town:test|move|place", new[] { "route|town:test|move|place|" }),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        using var handler = new ReplyHandler
        {
            Answer = JsonSerializer.Serialize(new { selected_candidate_id = selected, confidence = 1 }),
        };
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model");
        var runtime = new CognitionRuntime("actor", provider);
        var candidates = offered.Select(id => new CognitionCandidate(id, "An offered personal choice.", 10))
            .Prepend(new("safe_idle", "Continue safely.", 0)).ToArray();
        var observation = new InhabitantObservation("actor", 0, 0, 0, "digest", 8_000, candidates);

        var admission = await runtime.RequestAndDecideAsync(observation);

        Assert.True(admission.Accepted);
        Assert.True(admission.FellBack);
        Assert.Equal("candidate_not_legal", admission.Outcome);
        Assert.Equal("safe_idle", admission.Intention!.CandidateId);
    }

    private sealed class HostedVoteProvider : IDecisionProvider, IDisposable
    {
        private readonly bool yes;
        private readonly ReplyHandler handler = new();
        private readonly HttpClient client;
        private readonly OpenAiCompatibleDecisionProvider hosted;
        public int OmittedVoteReplies { get; private set; }
        public DecisionProviderKind Kind => hosted.Kind;
        public long ProviderEpoch => hosted.ProviderEpoch;

        public HostedVoteProvider(bool yes)
        {
            this.yes = yes;
            client = new(handler);
            hosted = new(client, () => "synthetic-key", new Uri("https://model.test/v1/chat/completions"), "synthetic-model");
        }

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var legal = observation.Candidates.Where(c => c.Id.StartsWith("civic|town:first|", StringComparison.Ordinal)).ToArray();
            var voteAction = yes ? "|yes|" : "|no|";
            var selected = legal.FirstOrDefault(c => c.Id.Contains(voteAction, StringComparison.Ordinal)) ??
                legal.FirstOrDefault(c => c.Id.Contains("|read|", StringComparison.Ordinal)) ??
                (observation.Self?.CivicNote?.Contains(ProposalText, StringComparison.Ordinal) != true
                    ? legal.FirstOrDefault(c => c.Id.Contains("|propose|", StringComparison.Ordinal)) : null) ??
                legal.FirstOrDefault(c => c.Id.Contains("|visit|", StringComparison.Ordinal)) ??
                observation.Candidates.Single(c => c.Id == "safe_idle");
            var replyId = selected.Id;
            if (replyId.Contains(voteAction, StringComparison.Ordinal))
            {
                Assert.EndsWith("|", replyId);
                replyId = replyId[..^1];
                OmittedVoteReplies++;
            }
            handler.Answer = JsonSerializer.Serialize(new
            {
                selected_candidate_id = replyId,
                confidence = 1,
                civic_proposal = selected.Id.Contains("|propose|", StringComparison.Ordinal) ? ProposalText : null,
            });
            return hosted.DecideAsync(request, cancellationToken);
        }

        public void Dispose() => client.Dispose();
    }

    private sealed class ReplyHandler : HttpMessageHandler
    {
        public string Answer { get; set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = Answer } } },
                }), Encoding.UTF8, "application/json"),
            });
    }
}
