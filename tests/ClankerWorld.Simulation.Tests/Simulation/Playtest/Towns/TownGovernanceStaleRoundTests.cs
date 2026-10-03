using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownGovernanceStaleRoundTests
{
    private const int Day = 10;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedMainRoundChoiceCannotBecomeRunoffConsentAfterAnActualReadAndRelay(bool structured)
    {
        using var generated = NormalPathWorld.CreateGenerated("council-held-main-round", _ => new ActionCoverageRecorder(chooseIdle: true));
        for (var ordinal = 5; ordinal <= 8; ordinal++)
        {
            var setup = generated.ExportState();
            var site = setup.Towns![0].BorderTiles.First(p => setup.Map.IsBuildable(p) &&
                !setup.Map.Resources.Any(r => r.Position == p) && !setup.Map.CampObjects.Any(o => o.Position == p) &&
                !setup.Inhabitants.Any(i => i.Position == p));
            generated.AddAgent($"agent:{ordinal:D32}", site);
        }
        var initial = generated.ExportState();
        var town = initial.Towns![0];
        var ids = town.ResidentIds.ToArray();
        var reader = ids[0];
        var heldActor = ids[2];
        var target = ids[1];
        var governance = TownGovernanceState.Create(ids);
        foreach (var id in ids.Take(5))
            governance = TownGovernanceRules.Register(governance, id, true, null, ids, 0);
        governance = TownGovernanceRules.Advance(governance, town.Id, initial.WorldSeed, ids, 0, Day);
        var contestId = governance.Election!.Id;
        governance = TownGovernanceRules.VoteElection(governance, contestId, reader, [ids[0], ids[1], ids[2]], 0);
        governance = TownGovernanceRules.VoteElection(governance, contestId, ids[1], [ids[0], ids[3]], 0);
        foreach (var actor in ids)
            governance = TownGovernanceRules.LearnNotices(governance, actor, governance.Notices.Select(n => n.Id), 0);
        var society = initial.Society.Society;
        var oldDay = society.Config.TicksPerWorldDay;
        var shortened = initial with
        {
            Towns = [town with { Governance = governance }],
            Inhabitants = initial.Inhabitants.Select(p => p with
            {
                Position = town.OriginSite!.Value,
                HungerBasisPoints = 8_000,
            }).ToArray(),
            WorldSystems = RegionalWeatherRules.Initialize(initial.WorldSystems! with
            {
                Config = initial.WorldSystems.Config with { TicksPerDay = Day, CalendarOffsetTicks = 0 },
                RegionalWeather = null,
            }, initial.Map),
            Society = initial.Society with
            {
                Society = society with
                {
                    Config = society.Config with { TicksPerWorldDay = Day },
                    Inhabitants = society.Inhabitants.Select(p => p with
                    {
                        BirthTick = p.BirthTick / oldDay * Day,
                        BirthLifeTick = p.BirthLifeTick is { } birth ? birth / oldDay * Day : null,
                    }).ToArray(),
                }
            },
        };
        var control = new RoundControl(heldActor, reader, target, structured);
        var providers = new ConcurrentDictionary<string, RoundProvider>(StringComparer.Ordinal);
        using var world = PrivateWorldRuntime.Restore(shortened,
            id => providers.GetOrAdd(id, actor => new RoundProvider(actor, control)));
        try
        {
            for (var tick = 0; tick < 5 && !control.Started.Task.IsCompleted; tick++)
            {
                await world.AdvanceOneTickNonBlockingAsync();
                await Task.Delay(5);
            }
            await control.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            while (world.WorldTick < Day + 5 && !HasRunoffRelay(world, heldActor, reader))
            {
                await world.AdvanceOneTickNonBlockingAsync();
                await Task.Delay(5);
            }
            Assert.Equal("runoff", world.Towns[0].Governance!.Election!.Stage);
            Assert.Equal(contestId, world.Towns[0].Governance!.Election!.Id);
            Assert.True(HasRunoffRelay(world, heldActor, reader), "The held voter must actually learn the runoff from an informed reader.");
            Assert.Contains(world.Towns[0].Governance!.Knowledge, receipt => receipt.AgentId == reader &&
                receipt.SourceAgentId is null && world.Towns[0].Governance!.Notices.Any(n => n.Id == receipt.NoticeId && n.Kind == "runoff"));
            control.Release.TrySetResult(true);
            while (world.WorldTick < Day + 8)
            {
                await world.AdvanceOneTickNonBlockingAsync();
                await Task.Delay(5);
            }
            var runoff = world.Towns[0].Governance!.Election!;
            Assert.Equal("runoff", runoff.Stage);
            Assert.DoesNotContain(runoff.Ballots, ballot => ballot.AgentId == heldActor);
            var fresh = Assert.Single(runoff.Ballots, ballot => ballot.AgentId == reader);
            Assert.Equal([target], fresh.Choices);
            Assert.NotNull(control.MainAction);
            Assert.NotNull(control.FreshAction);
            Assert.NotEqual(control.MainAction, control.FreshAction);
            world.Validate();
        }
        finally
        {
            control.Release.TrySetResult(true);
        }
    }

    private static bool HasRunoffRelay(PrivateWorldRuntime world, string actor, string source)
    {
        var state = world.Towns[0].Governance!;
        return state.Knowledge.Any(receipt => receipt.AgentId == actor && receipt.SourceAgentId == source &&
            state.Notices.Any(n => n.Id == receipt.NoticeId && n.Kind == "runoff"));
    }

    private sealed class RoundControl(string heldActor, string reader, string target, bool structured)
    {
        public string HeldActor { get; } = heldActor;
        public string Reader { get; } = reader;
        public string Target { get; } = target;
        public bool Structured { get; } = structured;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? MainAction { get; set; }
        public string? FreshAction { get; set; }
    }

    private sealed class RoundProvider(string actor, RoundControl control) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var o = request.Observation;
            var vote = o.Candidates.FirstOrDefault(c => control.Structured
                ? c.Id.Contains("|ballot|", StringComparison.Ordinal)
                : c.Id.Contains("|single|", StringComparison.Ordinal) && c.Id.Split('|')[4] == control.Target);
            var selected = o.Candidates.Single(c => c.Id == "safe_idle");
            if (actor == control.HeldActor && !control.Started.Task.IsCompleted && vote is not null)
            {
                selected = vote;
                control.MainAction = selected.Id;
                control.Started.TrySetResult(true);
                // Deliberately ignore cancellation: authority must reject a reply after the round changes.
                await control.Release.Task;
            }
            else if (actor == control.Reader && o.WorldTick >= Day)
            {
                selected = o.Candidates.FirstOrDefault(c => c.Id.Contains("|read|", StringComparison.Ordinal)) ??
                    o.Candidates.FirstOrDefault(c => c.Id.Contains("|relay|", StringComparison.Ordinal) && c.Id.Split('|')[3] == control.HeldActor) ??
                    vote ?? selected;
                if (selected == vote) control.FreshAction = selected.Id;
            }
            return new(request.RequestId, o.InhabitantId, Kind, ProviderEpoch, o.RunEpoch,
                o.DecisionGeneration, o.ObservationDigest, selected.Id, 1,
                o.Candidates.ToDictionary(c => c.Id, c => c.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicBallot: control.Structured && selected.Id.Contains("|ballot|", StringComparison.Ordinal) ? [control.Target] : null);
        }
    }
}
