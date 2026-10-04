using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class CouncilBallotExpiryTests
{
    private const long BallotExpiryTick = 121;
    private static readonly Lazy<Task<IReadOnlyDictionary<long, PrivateWorldRuntimeState>>> States = new(CreateStates);

    [Theory]
    [InlineData(121, true, false, "open", false)]
    [InlineData(121, false, false, "open", false)]
    [InlineData(120, true, false, "essential_first", false)]
    [InlineData(120, false, false, "open", true)]
    [InlineData(120, false, true, "essential_first", false)]
    [InlineData(119, false, true, "essential_first", false)]
    public async Task FoodPolicyRequiresALivingMajorityNoLaterThanTheExpiryTick(
        long startingTick, bool nonapproverDies, bool castThirdApproval,
        string expectedPolicy, bool expectedPending)
    {
        var state = (await States.Value)[startingTick];
        var society = state.Society.Society;
        var voters = society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
            person.HouseholdId is not null && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
            .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(4, voters.Length);
        var dying = voters[3];
        var maximumDay = society.Config.DayLifecycle!.MaximumDay;
        var nextTick = startingTick + 1;
        var birthTick = nextTick - maximumDay * society.Config.TicksPerLifecycleAge;
        var birthLifeTick = society.LifeTickAt(nextTick) - maximumDay * society.Config.TicksPerLifecycleAge;
        state = SettlementWeatherTestFixture.WithWeather(state with
        {
            Council = new(voters[0], "open", 0, new("essential_first", 1, BallotExpiryTick, voters, voters[..2], [])),
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => nonapproverDies && person.Id == dying
                        ? person with
                        {
                            BirthTick = birthTick,
                            BirthLifeTick = society.LifeClock is null ? null : birthLifeTick,
                            AgeBand = SocietyAgeBand.Elder,
                            LastLifecycleYearChecked = maximumDay - 1,
                        }
                        : person).ToArray(),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                LastDecisionContext = null,
                HungerBasisPoints = 7_000,
                Survival = new(),
            }).ToArray(),
        }, WeatherKind.Clear);

        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        IDecisionProvider Provider(string id) => new Pick(castThirdApproval && id == voters[2] ? "council_vote_yes" : "safe_idle");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), Provider);
        world.Validate();
        Assert.Equal("open", world.ExportState().Council!.FoodPolicy);
        Assert.Equal(2, world.ExportState().Council!.Ballot!.Approvals.Count);
        Assert.Equal(SocietyInhabitantStatus.Active, world.Society.GetInhabitant(dying).Status);

        var result = await world.AdvanceOneTickAsync();
        Assert.True(result.Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(nextTick, world.WorldTick);
        world.Validate();
        replay.Validate();
        var after = world.ExportState();
        var afterBytes = PrivateWorldRuntimeCodec.Encode(after);
        Assert.Equal(afterBytes, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(afterBytes));
        Assert.Equal(afterBytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(nonapproverDies ? SocietyInhabitantStatus.Dead : SocietyInhabitantStatus.Active,
            world.Society.GetInhabitant(dying).Status);
        if (nonapproverDies)
        {
            Assert.Equal(SocietyDeathCause.NaturalAge, world.Society.GetInhabitant(dying).DeathCause);
            Assert.Equal(nextTick, world.Society.GetInhabitant(dying).DeathTick);
        }
        var votes = result.Events.Where(item => item.Kind == "council_vote_recorded").ToArray();
        if (castThirdApproval) Assert.Equal(voters[2], Assert.Single(votes).Detail);
        else Assert.Empty(votes);
        Assert.Equal(expectedPending, after.Council!.Ballot is not null);
        Assert.Equal(expectedPolicy, after.Council.FoodPolicy);
        var resolutions = result.Events.Where(item => item.Kind is "council_policy_rejected" or "council_policy_adopted").ToArray();
        if (expectedPending)
        {
            Assert.Equal(0, after.Council.LastResolutionTick);
            Assert.Equal(voters, after.Council.Ballot!.Electorate);
            Assert.Equal(voters[..2], after.Council.Ballot.Approvals);
            Assert.Empty(after.Council.Ballot.Rejections);
            Assert.Equal(BallotExpiryTick, after.Council.Ballot.ExpiryTick);
            Assert.Empty(resolutions);
        }
        else
        {
            Assert.Equal(nextTick, after.Council.LastResolutionTick);
            var resolution = Assert.Single(resolutions);
            Assert.Equal(expectedPolicy == "open" ? "council_policy_rejected" : "council_policy_adopted", resolution.Kind);
            Assert.Equal("essential_first", resolution.Detail);
            Assert.Equal(nextTick, resolution.WorldTick);
        }
    }

    private static async Task<IReadOnlyDictionary<long, PrivateWorldRuntimeState>> CreateStates()
    {
        using var world = NormalPathWorld.CreateGenerated("audit-town-invariants", _ => new Pick("safe_idle"));
        var states = new Dictionary<long, PrivateWorldRuntimeState>();
        while (world.WorldTick < BallotExpiryTick)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (world.WorldTick >= 119) states.Add(world.WorldTick, world.ExportState());
        }
        return states;
    }

    private sealed class Pick(string wanted) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidate = request.Observation.Candidates.FirstOrDefault(item => item.Id == wanted)
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [candidate] },
            }, cancellationToken);
        }
    }
}
