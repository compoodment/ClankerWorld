using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class SurvivalPriorityPrototypeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(360)]
    [InlineData(1200)]
    public async Task ReportFixedSeedSurvivalPriorities(int ticks)
    {
        for (var seed = 0; seed < 3; seed++)
        {
            var choices = new Dictionary<string, int>();
            var unchosen = new Dictionary<string, int>();
            using var setup = new PrivateWorldRuntime($"survival-priority-{seed}", _ => new CandidateProbe());
            setup.StageStarterContent();
            for (var tick = 0; tick < 3; tick++) await setup.AdvanceOneTickAsync();
            var weather = new[] { WeatherKind.Clear, WeatherKind.Rain, WeatherKind.Storm }[seed];
            var initial = setup.ExportState();
            initial = initial with
            {
                Inhabitants = initial.Inhabitants.Select(person => person with
                { HungerBasisPoints = 4_500, Survival = new SurvivalCondition(WarmthBasisPoints: 5_500), LastDecisionContext = null }).ToArray(),
                WorldSystems = initial.WorldSystems! with
                {
                    // Keep this controlled comparison on the historical fixed-profile weather path.
                    RegionalWeather = null,
                    Config = initial.WorldSystems.Config with
                    {
                        WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season =>
                        new WeatherProfile(season, weather == WeatherKind.Clear ? 100 : 0, 0,
                            weather == WeatherKind.Rain ? 100 : 0, weather == WeatherKind.Storm ? 100 : 0, 0)).ToArray()
                    },
                    Climate = initial.WorldSystems.Climate with { Weather = weather },
                },
            };
            using var world = PrivateWorldRuntime.Restore(initial, _ => new MeasuredLocalProvider(choices, unchosen));
            var initialFood = Food(world);
            Assert.Equal(32, initialFood);
            Assert.Equal(3, world.WorldTick);
            var minimumFood = initialFood;
            var peakIllness = 0;
            for (var tick = 0; tick < ticks; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                minimumFood = Math.Min(minimumFood, Food(world));
                if (seed == 2 && tick % 120 == 0)
                    foreach (var person in world.Inhabitants)
                        output.WriteLine($"trace tick={world.WorldTick} actor={person.InhabitantId} position={person.Position} warmth={person.Survival?.WarmthBasisPoints} illness={person.Survival?.IllnessBasisPoints} project={person.Project?.Stage} choice={world.ExportState().Society.Cognition.Runtimes.Single(item => item.InhabitantId == person.InhabitantId).CurrentIntention?.CandidateId}");
                peakIllness = Math.Max(peakIllness, world.Inhabitants.Select(person => person.Survival?.IllnessBasisPoints ?? 0).DefaultIfEmpty().Max());
            }
            var state = world.ExportState();
            var deaths = world.Society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Dead).ToArray();
            output.WriteLine($"ticks={ticks}; seed={seed}; weather={weather}; decisions={string.Join(',', choices.OrderBy(item => item.Key).Select(item => item.Key + ':' + item.Value))}; unchosen={string.Join(',', unchosen.OrderBy(item => item.Key).Select(item => item.Key + ':' + item.Value))}; food={initialFood}/{minimumFood}/{Food(world)}; peakIllness={peakIllness}; deaths={deaths.Length}; causes={string.Join(',', deaths.Select(person => person.DeathCause))}; meals={state.Events.Count(item => item.Kind == "food_consumed")}; explorationMoves={state.Events.Count(item => item.Kind == "inhabitant_moved" && item.Detail.EndsWith(":explore", StringComparison.Ordinal))}; projectsCompleted={state.Events.Count(item => item.Kind == "project_progress" && item.Detail.Contains(":completed:", StringComparison.Ordinal))}");
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
            Assert.Equal(world.WorldTick, restored.WorldTick);
        }
    }

    [Theory]
    [InlineData(4_000, 6_000)]
    [InlineData(3_900, 5_900)]
    [InlineData(2_100, 5_900)]
    public async Task ComfortableReferencesDoNotVetoAvailableConstruction(int fullness, int warmth)
    {
        var initial = await BoundaryState(fullness, warmth);
        var provider = new CandidateProbe();
        using var world = PrivateWorldRuntime.Restore(initial, _ => provider);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(provider.Seen, request => request.Observation.InhabitantId == "founder-rowan" &&
            request.Observation.Candidates.Any(candidate => candidate.Id.StartsWith("build:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BelowComfortableFullnessCanStillChooseAndPerformCuriosity()
    {
        var provider = new CandidateProbe("explore");
        using var world = PrivateWorldRuntime.Restore(await BoundaryState(3_900, 5_900), _ => provider);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "exploration_started");
    }

    [Fact]
    public async Task UrgentFoodPrefersEatingButDoesNotRemoveCareResponse()
    {
        var initial = await BoundaryState(1_900, 6_000);
        initial = initial with
        {
            Society = initial.Society with
            {
                Society = initial.Society.Society with
                {
                    Inhabitants = initial.Society.Society.Inhabitants.Select(person => person.Id == "founder-mira" ? person with
                    {
                        BirthTick = initial.Society.Society.WorldTick - 3 * initial.Society.Society.Config.TicksPerWorldYear,
                        AgeBand = SocietyAgeBand.Child,
                        CurrentRole = SocietyWorkRole.Unassigned,
                        LastLifecycleYearChecked = 3
                    } : person).ToArray(),
                }
            }
        };
        var provider = new CandidateProbe(chooseLowest: true);
        using var world = PrivateWorldRuntime.Restore(initial, _ => provider);
        var step = await world.AdvanceOneTickAsync();
        var scout = Assert.Single(provider.Seen, request => request.Observation.InhabitantId == "founder-scout");
        Assert.Contains(scout.Observation.Candidates, candidate => candidate.Id == "guardian_offer:founder-mira");
        Assert.DoesNotContain(scout.Observation.Candidates, candidate => candidate.Id == "explore");
        Assert.Contains(step.Decisions, decision => decision.InhabitantId == "founder-scout" && decision.Admission.Intention?.CandidateId == "consume_food");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColdAgentOnlyInterruptsForWarmthWhileActuallyLosingIt(bool sheltered)
    {
        var initial = await BoundaryState(4_500, 3_200, WeatherKind.Rain);
        using var setup = PrivateWorldRuntime.Restore(initial);
        var site = initial.Map.Tiles.Select(tile => tile.Position).First(point => initial.Map.IsBuildable(point) &&
            !initial.Map.Resources.Any(item => item.Position == point) && !initial.Map.CampObjects.Any(item => item.Position == point) &&
            !initial.Inhabitants.Any(item => item.Position == point));
        var house = setup.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
        var placement = setup.PlaceBuilding("warm-home", house.CanonicalId, site, "household:camp-alpha");
        Assert.True(placement.Applied, placement.Failure);
        initial = setup.ExportState();
        if (sheltered) initial = initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == "founder-scout"
            ? person with { Position = site } : person).ToArray()
        };
        var provider = new CandidateProbe(chooseLowest: true);
        using var world = PrivateWorldRuntime.Restore(initial, _ => provider);
        var step = await world.AdvanceOneTickAsync();
        Assert.True(step.Advanced);
        var candidates = Assert.Single(provider.Seen, request => request.Observation.InhabitantId == "founder-scout").Observation.Candidates;
        if (sheltered)
        {
            Assert.Contains(candidates, candidate => candidate.Id == "seek_warmth" && candidate.DeterministicPriority == 3);
            Assert.DoesNotContain(candidates, candidate => candidate.Id == "explore");
            for (var tick = 0; tick < 20; tick++) await world.AdvanceOneTickAsync();
            var recovering = world.Inhabitants.Single(person => person.InhabitantId == "founder-scout");
            Assert.Equal(site, recovering.Position);
            Assert.True(recovering.Survival!.WarmthBasisPoints > 3_200);
        }
        else
        {
            Assert.Contains(candidates, candidate => candidate.Id == "seek_warmth" && candidate.DeterministicPriority <= 2);
            Assert.Contains(step.Decisions, decision => decision.InhabitantId == "founder-scout" && decision.Admission.Intention?.CandidateId is "seek_warmth" or "tend_fire");
        }
    }

    private static async Task<PrivateWorldRuntimeState> BoundaryState(int fullness, int warmth, WeatherKind weather = WeatherKind.Clear)
    {
        using var setup = new PrivateWorldRuntime("survival-boundary", _ => new CandidateProbe());
        setup.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await setup.AdvanceOneTickAsync();
        var state = setup.ExportState();
        var inventory = state.Society.Society.Inventory;
        foreach (var person in state.Inhabitants)
            inventory = InventoryFixture.AddLot(inventory, "boundary-food-" + person.InhabitantId, "food", person.InhabitantId, 4, state.Society.Society.WorldTick);
        return state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            { HungerBasisPoints = fullness, Survival = new SurvivalCondition(WarmthBasisPoints: warmth), LastDecisionContext = null }).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season =>
                    new WeatherProfile(season, weather == WeatherKind.Clear ? 100 : 0, 0,
                        weather == WeatherKind.Rain ? 100 : 0, weather == WeatherKind.Storm ? 100 : 0, 0)).ToArray()
                },
                Climate = state.WorldSystems.Climate with { Weather = weather },
            },
        };
    }

    private sealed class CandidateProbe(string? selected = null, bool chooseLowest = false) : IDecisionProvider
    {
        public List<CognitionDecisionRequest> Seen { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Seen.Add(request);
            var choice = chooseLowest ? request.Observation.Candidates.OrderBy(candidate => candidate.DeterministicPriority).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).First().Id
                : request.Observation.Candidates.Any(candidate => candidate.Id == selected) ? selected! : "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind,
                ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                choice, 1, request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice ? 1d : 0d)));
        }
    }

    private static int Food(PrivateWorldRuntime world) => world.Society.Inventory.Lots
        .Where(lot => lot.ItemKind is "food" or "fruit").Sum(lot => lot.Quantity);

    private static string Category(string id) => id switch
    {
        "consume_food" or "collect_shared_food" or "harvest_food" or "seek_food" or "wear_clothing" or "tend_fire" or "seek_warmth" => "survival",
        "explore" or "child_explore" => "exploration",
        _ when id.StartsWith("build:", StringComparison.Ordinal) => "building",
        _ when id.StartsWith("partner_", StringComparison.Ordinal) || id.StartsWith("learn:", StringComparison.Ordinal) ||
            id.StartsWith("teach_", StringComparison.Ordinal) || id.StartsWith("knowledge_share:", StringComparison.Ordinal) ||
            id.StartsWith("guardian_", StringComparison.Ordinal) || id.StartsWith("care:", StringComparison.Ordinal) => "social-care",
        _ => "other",
    };

    private sealed class MeasuredLocalProvider(Dictionary<string, int> choices, Dictionary<string, int> unchosen) : IDecisionProvider
    {
        private readonly DeterministicDecisionProvider local = new();
        public DecisionProviderKind Kind => local.Kind;
        public long ProviderEpoch => local.ProviderEpoch;
        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var response = await local.DecideAsync(request, cancellationToken);
            var selected = Category(response.SelectedCandidateId);
            choices[selected] = choices.GetValueOrDefault(selected) + 1;
            foreach (var category in request.Observation.Candidates.Select(candidate => Category(candidate.Id)).Distinct().Where(category => category != selected))
                unchosen[category] = unchosen.GetValueOrDefault(category) + 1;
            return response;
        }
    }
}
