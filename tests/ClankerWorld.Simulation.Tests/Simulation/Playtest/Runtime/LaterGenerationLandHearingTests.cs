using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class GrownAgentHelperMemoryTests
{
    private static readonly Lazy<Task<byte[]>> LaterAdult = new(CreateLaterAdultAsync);
    private static readonly Lazy<Task<byte[]>> LaterHearingGovernment = new(async () =>
        await CreateNativeHearingGovernmentAsync(false, PrivateWorldRuntimeCodec.Decode(await LaterAdult.Value)));

    [Fact]
    public async Task LaterNativeAdultWinsARealCaseElectionAfterASavedTie()
    {
        var state = PrivateWorldRuntimeCodec.Decode(await LaterHearingGovernment.Value);
        var births = state.Society.Society.Births.OrderBy(item => item.CommittedTick).ToArray();
        var actor = births[^1].ChildId;
        var other = births[^2].ChildId;
        Assert.True(actor.Length > 256);
        var actorName = state.Society.Society.GetInhabitant(actor).Name;
        var otherName = state.Society.Society.GetInhabitant(other).Name;
        var candidates = new HashSet<string>([actor, other], StringComparer.Ordinal);
        var choices = new NativeLandHearingChoices
        {
            Request = true,
            Filer = NativeHearingJudge,
            Judge = actor,
            CaseCandidates = candidates,
            CaseVote = voter => voter == actor ? actorName : voter == other ? otherName : null,
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        Assert.True(world.DisplaceAdult(actor));
        var house = world.WorldSimulation.Buildings.Single(item => item.InstanceId == NativeHearingHouse);
        foreach (var member in world.Society.GetHousehold(house.HouseholdId!).MemberIds.ToArray())
            Assert.True(world.DisplaceAdult(member));
        await NativeHearingUntil(world, () => world.Towns[0].LandHearings.Cases.Count == 1, 20);
        choices.Request = false;
        await NativeHearingUntil(world, () => world.Towns[0].LandHearings.Cases[0].Contest is { } contest &&
            contest.TiedCandidates.Contains(actor) && contest.Rounds.Any(round => round.Result == "tie"), NativeHearingDay * 4);
        var tied = world.Towns[0].LandHearings.Cases[0].Contest!;
        Assert.Contains(actor, tied.Voters);
        Assert.Contains(actor, tied.Candidates);
        Assert.Contains(actor, tied.TiedCandidates);
        var round = tied.Rounds.Single(item => item.Result == "tie");
        Assert.Contains(actor, round.Voters);
        Assert.Contains(actor, round.Candidates);
        Assert.Contains(actor, round.TiedCandidates);
        var pending = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        AssertInvalidLaterElectionRosters(world.ExportState(), world.Towns[0].LandHearings.Cases[0], tied, actor, other);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(pending), _ => new NativeLandHearingChoices
        { Judge = actor, CaseCandidates = candidates, CaseVote = _ => actorName, Rule = true });
        Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        choices.CaseVote = _ => actorName;
        choices.Rule = true;
        for (var step = 0; step < NativeHearingDay * 3 && world.Towns[0].LandHearings.Cases[0].Status == "pending"; step++)
        {
            NativeHearingPromptAll(world);
            NativeHearingPromptAll(replay);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var item = world.Towns[0].LandHearings.Cases[0];
        Assert.Equal("settled", item.Status);
        var election = Assert.Single(item.ContestHistory, contest => contest.Stage == "completed");
        Assert.Equal(actor, election.WinnerId);
        Assert.Contains(election.Rounds, completed => completed.Result == "tie" && completed.TiedCandidates.Contains(actor));
        var ruling = Assert.Single(item.Rulings);
        Assert.Equal((actor, "case_elected", "reclaim"), (ruling.Judge.AgentId, ruling.Judge.Kind, ruling.Outcome.Kind));
        Assert.Null(world.WorldSimulation.Buildings.Single(building => building.InstanceId == house.InstanceId).HouseholdId);
        Assert.Contains(choices.Selected, choice => choice.Actor == actor && choice.Choice.Contains("|hearing_judge_register|", StringComparison.Ordinal));
        Assert.Contains(choices.Selected, choice => choice.Actor == actor && choice.Choice.Contains("|hearing_judge_vote|", StringComparison.Ordinal));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        AssertInvalidLaterElectionRosters(world.ExportState(), item, election, actor, other);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new NativeLandHearingChoices());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static void AssertInvalidLaterElectionRosters(PrivateWorldRuntimeState state, TownLandCase item,
        TownLandCaseJudgeContest contest, string actor, string other)
    {
        foreach (var ids in new[] { new[] { actor, actor }, new[] { actor, other }.Order(StringComparer.Ordinal).Reverse().ToArray(),
                     new[] { " " + actor }, new[] { actor + "\0" }, new[] { actor + ":unknown" } })
        {
            var damaged = new[]
            {
                contest with { Voters = ids }, contest with { Candidates = ids }, contest with { TiedCandidates = ids },
                contest with { Rounds = contest.Rounds.Select(round => round with { Voters = ids }).ToArray() },
                contest with { Rounds = contest.Rounds.Select(round => round with { Candidates = ids }).ToArray() },
                contest with { Rounds = contest.Rounds.Select(round => round with { TiedCandidates = ids }).ToArray() },
            };
            foreach (var invalid in damaged)
            {
                var invalidCase = item.Contest is null ? item with
                { ContestHistory = item.ContestHistory.Select(saved => saved.Id == contest.Id ? invalid : saved).ToArray() } :
                    item with { Contest = invalid };
                var invalidState = state with
                {
                    Towns = state.Towns!.Select(town => town with
                    { LandHearings = town.LandHearings with { Cases = town.LandHearings.Cases.Select(saved => saved.Id == item.Id ? invalidCase : saved).ToArray() } }).ToArray()
                };
                Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalidState));
            }
        }
    }

    private static async Task<byte[]> CreateLaterAdultAsync()
    {
        var state = NativeHearingCalendar(PrivateWorldRuntimeCodec.Decode(await ConversationAdult.Value));
        var society = state.Society.Society;
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with
            {
                Society = society with
                {
                    // Keep the original adults alive for the later civic fixture.
                    Config = society.Config with
                    {
                        BaseNaturalMortalityBasisPoints = 0,
                        NaturalMortalitySlopeBasisPoints = 0,
                        DayLifecycle = society.Config.DayLifecycle! with { MaximumDay = 1_000 }
                    },
                },
            },
        };
        var choices = new LaterFamilyChoices();
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        world.Resume();
        var parent = Assert.Single(world.Society.Births).ChildId;
        for (var generation = 2; generation <= 3; generation++)
        {
            var mate = "agent:" + generation.ToString("D32", System.Globalization.CultureInfo.InvariantCulture);
            var household = world.Society.GetInhabitant(parent).HouseholdId!;
            var house = world.WorldSimulation.Buildings.First(item => item.HouseholdId == household &&
                item.InstanceId.Contains("house-", StringComparison.Ordinal));
            // The small generated House has three places. Older adults leave
            // through the real public operation before the next couple moves in.
            foreach (var member in world.Society.GetHousehold(household).MemberIds.Where(id => id != parent).ToArray())
                Assert.True(world.DisplaceAdult(member));
            Assert.Equal(household, world.AddAgent(mate, house.Position, household, world.Towns[0].Id));
            world.Pause();
            state = world.ExportState();
            society = state.Society.Society;
            var relation = "later-parents-" + generation;
            society = SocietyFixture.ProposeRelationship(society,
                new(relation, 1, SocietyRelationshipType.Partnership, parent, mate, society.WorldTick)).Checkpoint;
            society = SocietyFixture.AcceptRelationship(society, relation, 1, mate).Checkpoint;
            society = society with
            {
                Inventory = InventoryFixture.AddLot(society.Inventory,
                "later-birth-food-" + generation, "bread", household, 100, storageBuildingId: house.InstanceId)
            };
            world.LoadPausedCheckpoint(state with
            {
                Society = state.Society with { Society = society },
                Inhabitants = state.Inhabitants.Select(item => item with
                {
                    HungerBasisPoints = 10_000,
                    LastDecisionContext = null,
                    Survival = (item.Survival ?? new SurvivalCondition()) with { WarmthBasisPoints = 10_000, IllnessBasisPoints = 0 }
                }).ToArray(),
            });
            choices.First = parent;
            choices.Second = mate;
            choices.Names[mate] = generation == 2 ? "Ori" : "Elin";
            world.Resume();
            for (var tick = 0; tick < 20 && world.Inhabitants.Single(item => item.InhabitantId == parent).Parenthood?.Stage != "preparing"; tick++)
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var preparation = world.Inhabitants.Single(item => item.InhabitantId == parent).Parenthood!;
            Assert.Equal("preparing", preparation.Stage);
            // The agreed plan and birth are native; skip only idle waiting, as in
            // the parenthood timing fixtures. The next real tick performs the birth.
            world.Pause();
            state = world.ExportState();
            var targetTick = preparation.LastTransitionTick + 599;
            society = SocietyFixture.AdvanceTo(SocietyFixture.Resume(state.Society.Society).Checkpoint, targetTick).Checkpoint;
            society = society with
            {
                Inventory = InventoryFixture.AddLot(society.Inventory,
                "later-due-food-" + generation, "bread", household, 100, storageBuildingId: house.InstanceId)
            };
            var systems = state.WorldSystems!;
            world.LoadPausedCheckpoint(state with
            {
                Society = state.Society with { Society = SocietyFixture.Pause(society).Checkpoint },
                WorldSystems = systems with
                {
                    WorldTick = targetTick,
                    RegionalWeather = RegionalWeatherRules.Advance(systems, targetTick),
                    Climate = WeatherRules.Advance(systems.Climate, targetTick, state.WorldSeed, systems.Config),
                },
                Inhabitants = state.Inhabitants.Select(item => item with { LastDecisionContext = null }).ToArray(),
            });
            world.Resume();
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True(world.Society.Births.Count == generation,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    world.WorldTick,
                    Parents = world.Inhabitants.Where(item => item.InhabitantId == parent || item.InhabitantId == mate),
                    Events = world.ExportState().Events.TakeLast(8)
                }));
            var birth = world.Society.Births.OrderBy(item => item.CommittedTick).Last();
            Assert.Equal(new[] { parent, mate }.Order(StringComparer.Ordinal), world.Society.Relationships
                .Where(item => item.Type == SocietyRelationshipType.BiologicalParentage && item.TargetId == birth.ChildId)
                .Select(item => item.ProposerId).Order(StringComparer.Ordinal));
            Assert.Contains(world.ExportState().Events, item => item.Kind == "child_born" && item.Detail == birth.ChildId);
            parent = birth.ChildId;
            choices.First = choices.Second = null;
            choices.Names[parent] = generation == 2 ? "Lina" : "Nara";
            world.Pause();
            world.SetLifePace(365);
            world.Resume();
            for (var tick = 0; tick < 40 && world.Society.AgeAt(world.Society.GetInhabitant(parent), world.WorldTick) < 15; tick++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
                await WaitForRequests(world);
            }
            world.Pause();
            world.SetLifePace(1);
            world.Resume();
            for (var tick = 0; tick < 40 && (world.Society.GetInhabitant(parent).NeedsName ||
                world.Inhabitants.Single(item => item.InhabitantId == parent).IdentityChoicePending); tick++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
                await WaitForRequests(world);
            }
            Assert.Equal(SocietyAgeBand.Adult, world.Society.GetInhabitant(parent).AgeBand);
            Assert.False(world.Society.GetInhabitant(parent).NeedsName);
        }
        world.Pause();
        Assert.Equal(3, world.Society.Births.Count);
        Assert.True(parent.Length > 256);
        world.Validate();
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private sealed class LaterFamilyChoices : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 0;
        public string? First { get; set; }
        public string? Second { get; set; }
        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var prefix = observation.InhabitantId == First ? "parent_propose:" : observation.InhabitantId == Second ? "parent_accept:" : null;
            var selected = prefix is null ? null : observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal));
            selected ??= observation.Candidates.FirstOrDefault(item => item.Id.StartsWith("care:", StringComparison.Ordinal));
            selected ??= observation.Candidates.Single(item => item.Id == "safe_idle");
            var surname = observation.Self?.AllowedChildSurnames is { Count: > 0 } surnames ? surnames[0] : "Moss";
            var name = observation.NeedsName || observation.IsNameRetry
                ? Names.GetValueOrDefault(observation.InhabitantId, "Vale") + " " + surname : null;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                observation.Candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                ChosenName: name, ChosenPersonality: observation.NeedsPersonality ? "Inventive and independent" : null,
                ChosenAspiration: observation.NeedsAspiration ? "Study the hills" : null));
        }
    }
}
