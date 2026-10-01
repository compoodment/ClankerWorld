using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Controlled comparison of the two ways a request can show needs (#672):
/// today's exact numbers and the agreed words. Both arms run the same
/// fixed-seed worlds through the real personal-model adapter; only the need
/// format differs. Offline, a stand-in replies with the built-in choice, which
/// it takes from the agent's state rather than the request text, so that run
/// checks the harness and the request content, not how a model reads words.
/// The model-backed run is opt-in and makes paid calls; see
/// docs/development/need-wording-comparison.md.
/// </summary>
public sealed class NeedWordingComparisonTests(ITestOutputHelper output)
{
    private static readonly WeatherKind[] Weathers = [WeatherKind.Clear, WeatherKind.Rain, WeatherKind.Storm];

    [Fact]
    public async Task WordsAndNumbersRunTheSameStateChosenWorldThroughThePersonalAdapter()
    {
        var numbers = await RunAsync(ModelNeedFormat.Numbers, WeatherKind.Storm, run: 0, ticks: 90, OfflineStandIn);
        var words = await RunAsync(ModelNeedFormat.Words, WeatherKind.Storm, run: 0, ticks: 90, OfflineStandIn);

        Assert.True(numbers.Decisions > 0);
        Assert.Equal(0, numbers.Failures + words.Failures);
        Assert.Equal(numbers.Choices, words.Choices);
        Assert.Equal(numbers.Outcome, words.Outcome);
        Assert.Equal(numbers.Decisions, numbers.RequestsWithNumbers);
        Assert.Equal(0, numbers.RequestsWithWords);
        Assert.Equal(words.Decisions, words.RequestsWithWords);
        Assert.Equal(0, words.RequestsWithNumbers);
    }

    [NeedWordingComparisonFact]
    public async Task ReportNeedWordingComparison()
    {
        var mode = NeedWordingComparisonFactAttribute.Mode;
        var ticks = Setting("CLANKERWORLD_NEED_WORDING_TICKS", 360);
        var runs = Setting("CLANKERWORLD_NEED_WORDING_RUNS", 1);
        var callBudget = new CallBudget(Setting("CLANKERWORLD_NEED_WORDING_MAX_CALLS", 1_500));
        Func<ModelNeedFormat, IDecisionProvider> providerFor = mode == "model" ? ModelProvider(callBudget) : OfflineStandIn;
        output.WriteLine($"mode={mode}; ticks={ticks}; runs={runs}; max_calls={callBudget.Limit}");

        var reports = new List<RunReport>();
        for (var run = 0; run < runs; run++)
            foreach (var weather in Weathers)
                foreach (var format in new[] { ModelNeedFormat.Numbers, ModelNeedFormat.Words })
                {
                    var report = await RunAsync(format, weather, run, ticks, providerFor);
                    Assert.False(callBudget.Exceeded,
                        "The paid-call limit was reached, so the later decisions fell back. Raise CLANKERWORLD_NEED_WORDING_MAX_CALLS or shorten the run.");
                    reports.Add(report);
                    output.WriteLine(report.Line());
                }

        output.WriteLine(string.Empty);
        output.WriteLine(RunReport.TableHeader);
        foreach (var report in reports) output.WriteLine(report.TableRow());
    }

    private static async Task<RunReport> RunAsync(
        ModelNeedFormat format, WeatherKind weather, int run, int ticks, Func<ModelNeedFormat, IDecisionProvider> providerFor)
    {
        var seed = $"need-wording-{Array.IndexOf(Weathers, weather)}";
        using var setup = new PrivateWorldRuntime(seed, _ => new DeterministicDecisionProvider());
        setup.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) await setup.AdvanceOneTickAsync();
        var initial = setup.ExportState();
        initial = initial with
        {
            // Same start as the survival priority prototype: fine but not full, chilly.
            Inhabitants = initial.Inhabitants.Select(person => person with
            { HungerBasisPoints = 4_500, Survival = new SurvivalCondition(WarmthBasisPoints: 5_500), LastDecisionContext = null }).ToArray(),
            WorldSystems = initial.WorldSystems! with
            {
                RegionalWeather = null,
                Config = initial.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season =>
                        new WeatherProfile(season, weather == WeatherKind.Clear ? 100 : 0, 0,
                            weather == WeatherKind.Rain ? 100 : 0, weather == WeatherKind.Storm ? 100 : 0, 0)).ToArray(),
                },
                Climate = initial.WorldSystems.Climate with { Weather = weather },
            },
        };

        var measured = new MeasuredProvider(providerFor(format));
        using var world = PrivateWorldRuntime.Restore(initial, _ => measured);
        var report = new RunReport(format, weather, run, ticks) { InitialFood = Food(world) };
        report.MinimumFood = report.InitialFood;
        var latestChoice = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var tick = 0; tick < ticks; tick++)
        {
            var step = await world.AdvanceOneTickAsync();
            Assert.True(step.Advanced, step.Outcome);
            foreach (var decision in step.Decisions)
            {
                if (decision.Admission.Intention is { } intention)
                {
                    latestChoice[decision.InhabitantId] = intention.CandidateId;
                    report.Choices.Add($"{world.WorldTick}:{decision.InhabitantId}:{intention.CandidateId}");
                    report.CountChoice(Category(intention.CandidateId));
                }
                if (decision.Admission.FellBack) report.Fallbacks++;
            }
            foreach (var person in world.Inhabitants)
            {
                report.AgentTicks++;
                if (latestChoice.TryGetValue(person.InhabitantId, out var choice) && Category(choice) == "survival")
                    report.SurvivalAgentTicks++;
                if (person.HungerBasisPoints < 2_000) report.StarvingAgentTicks++;
                if (person.HungerBasisPoints < 4_000) report.HungryAgentTicks++;
                if (person.Survival is { } condition)
                {
                    if (condition.WarmthBasisPoints < 3_500) report.FreezingAgentTicks++;
                    if (condition.IllnessBasisPoints >= 2_500) report.UnwellAgentTicks++;
                    report.PeakIllness = Math.Max(report.PeakIllness, condition.IllnessBasisPoints);
                }
            }
            report.MinimumFood = Math.Min(report.MinimumFood, Food(world));
        }

        var state = world.ExportState();
        report.FinalFood = Food(world);
        report.Meals = state.Events.Count(item => item.Kind == "food_consumed");
        report.Deaths = world.Society.Inhabitants.Count(person => person.Status == SocietyInhabitantStatus.Dead);
        report.Decisions = measured.Calls;
        report.Failures = measured.Failures;
        report.RequestCharacters = measured.RequestCharacters;
        report.InputTokens = measured.InputTokens;
        report.OutputTokens = measured.OutputTokens;
        report.RequestsWithNumbers = measured.RequestsWithNumbers;
        report.RequestsWithWords = measured.RequestsWithWords;
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        Assert.Equal(world.WorldTick, restored.WorldTick);
        return report;
    }

    private static IDecisionProvider OfflineStandIn(ModelNeedFormat format) => new OfflineStandInProvider(format);

    private static Func<ModelNeedFormat, IDecisionProvider> ModelProvider(CallBudget budget)
    {
        var endpoint = new Uri(Required("CLANKERWORLD_NEED_WORDING_ENDPOINT"), UriKind.Absolute);
        var model = Required("CLANKERWORLD_NEED_WORDING_MODEL");
        _ = Required("CLANKERWORLD_NEED_WORDING_API_KEY");
        var client = new HttpClient(new BudgetHandler(budget));
        // The key is read at call time and never written to the report.
        return format => new OpenAiCompatibleDecisionProvider(client,
            () => Environment.GetEnvironmentVariable("CLANKERWORLD_NEED_WORDING_API_KEY"),
            endpoint, model, needFormat: format);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Set {name} for the model-backed comparison.");

    private static int Setting(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

    private static int Food(PrivateWorldRuntime world) => world.Society.Inventory.Lots
        .Where(lot => lot.ItemKind is "food" or "fruit" or "berries" or "wild_greens" or "cultivated_greens")
        .Sum(lot => lot.Quantity);

    // The survival priority prototype's categories.
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

    private sealed class RunReport(ModelNeedFormat format, WeatherKind weather, int run, int ticks)
    {
        public const string TableHeader =
            "| Weather | Run | Needs as | Decisions | Survival choices | Time under a survival choice | Starving agent-ticks | Hungry or worse | Freezing | Unwell or worse | Peak illness | Food (start/min/end) | Meals | Deaths | Failed calls | Request characters | Input/output tokens |\n" +
            "| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: | --- |";

        public List<string> Choices { get; } = [];
        public Dictionary<string, int> ChoiceCategories { get; } = new(StringComparer.Ordinal);
        public int Decisions { get; set; }
        public int Fallbacks { get; set; }
        public int Failures { get; set; }
        public int AgentTicks { get; set; }
        public int SurvivalAgentTicks { get; set; }
        public int StarvingAgentTicks { get; set; }
        public int HungryAgentTicks { get; set; }
        public int FreezingAgentTicks { get; set; }
        public int UnwellAgentTicks { get; set; }
        public int PeakIllness { get; set; }
        public int InitialFood { get; init; }
        public int MinimumFood { get; set; }
        public int FinalFood { get; set; }
        public int Meals { get; set; }
        public int Deaths { get; set; }
        public long RequestCharacters { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public int RequestsWithNumbers { get; set; }
        public int RequestsWithWords { get; set; }

        /// <summary>Everything that describes what happened in the world, without request size or usage.</summary>
        public string Outcome =>
            $"{Decisions}|{Fallbacks}|{Categories()}|{AgentTicks}|{SurvivalAgentTicks}|{StarvingAgentTicks}|{HungryAgentTicks}|" +
            $"{FreezingAgentTicks}|{UnwellAgentTicks}|{PeakIllness}|{InitialFood}/{MinimumFood}/{FinalFood}|{Meals}|{Deaths}";

        public void CountChoice(string category) => ChoiceCategories[category] = ChoiceCategories.GetValueOrDefault(category) + 1;

        public string Line() =>
            $"weather={weather}; run={run}; needs={format}; ticks={ticks}; decisions={Decisions}; fallbacks={Fallbacks}; failures={Failures}; " +
            $"choices={Categories()}; survivalAgentTicks={SurvivalAgentTicks}/{AgentTicks}; starving={StarvingAgentTicks}; hungry={HungryAgentTicks}; " +
            $"freezing={FreezingAgentTicks}; unwell={UnwellAgentTicks}; peakIllness={PeakIllness}; food={InitialFood}/{MinimumFood}/{FinalFood}; " +
            $"meals={Meals}; deaths={Deaths}; requestCharacters={RequestCharacters}; tokens={InputTokens}/{OutputTokens}";

        public string TableRow() =>
            $"| {weather} | {run + 1} | {format} | {Decisions} | {Share(ChoiceCategories.GetValueOrDefault("survival"), Decisions)} | " +
            $"{Share(SurvivalAgentTicks, AgentTicks)} | {StarvingAgentTicks} | {HungryAgentTicks} | {FreezingAgentTicks} | {UnwellAgentTicks} | " +
            $"{PeakIllness / 100.0:0.00}% | {InitialFood}/{MinimumFood}/{FinalFood} | {Meals} | {Deaths} | {Failures} | " +
            $"{(Decisions == 0 ? 0 : RequestCharacters / Decisions)} avg | {InputTokens}/{OutputTokens} |";

        private string Categories() => string.Join(',', ChoiceCategories.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}:{item.Value}"));

        private static string Share(int part, int whole) => whole == 0 ? "0.0%" : $"{100.0 * part / whole:0.0}% ({part})";
    }

    /// <summary>Counts calls and request content around the real adapter, without keeping any request text.</summary>
    private sealed class MeasuredProvider(IDecisionProvider inner) : IDecisionProvider
    {
        private readonly object sync = new();
        public DecisionProviderKind Kind => inner.Kind;
        public long ProviderEpoch => inner.ProviderEpoch;
        public int Calls { get; private set; }
        public int Failures { get; private set; }
        public long InputTokens { get; private set; }
        public long OutputTokens { get; private set; }
        public long RequestCharacters => (inner as OfflineStandInProvider)?.RequestCharacters ?? 0;
        public int RequestsWithNumbers => (inner as OfflineStandInProvider)?.RequestsWithNumbers ?? 0;
        public int RequestsWithWords => (inner as OfflineStandInProvider)?.RequestsWithWords ?? 0;

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            lock (sync) Calls++;
            try
            {
                var response = await inner.DecideAsync(request, cancellationToken);
                lock (sync)
                {
                    InputTokens += response.Usage?.InputTokens ?? 0;
                    OutputTokens += response.Usage?.OutputTokens ?? 0;
                }
                return response;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                lock (sync) Failures++;
                throw;
            }
        }
    }

    /// <summary>
    /// Sends each request through the real personal adapter to a local handler
    /// that answers with the built-in choice. It never reads the needs, so both
    /// arms must make the same choices.
    /// </summary>
    private sealed class OfflineStandInProvider(ModelNeedFormat format) : IDecisionProvider
    {
        private readonly object sync = new();
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 2;
        public long RequestCharacters { get; private set; }
        public int RequestsWithNumbers { get; private set; }
        public int RequestsWithWords { get; private set; }

        public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates
                .OrderBy(candidate => candidate.DeterministicPriority)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .First().Id;
            var handler = new ReplyHandler(choice);
            using var client = new HttpClient(handler);
            var adapter = new OpenAiCompatibleDecisionProvider(client, () => "offline-stand-in",
                new Uri("https://offline.invalid/v1/chat/completions"), "offline-stand-in", needFormat: format);
            var response = await adapter.DecideAsync(request, cancellationToken);
            using var body = JsonDocument.Parse(handler.Body!);
            var input = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            using var question = JsonDocument.Parse(input);
            var root = question.RootElement;
            var self = root.TryGetProperty("self", out var selfValue) && selfValue.ValueKind == JsonValueKind.Object ? selfValue : default;
            var numbers = root.TryGetProperty("hunger_basis_points", out _) ||
                self.ValueKind == JsonValueKind.Object && (self.TryGetProperty("warmth_basis_points", out _) || self.TryGetProperty("illness_basis_points", out _));
            var words = root.TryGetProperty("fullness", out var fullness) &&
                fullness.GetString() == ModelNeedWords.Fullness(request.Observation.HungerBasisPoints) &&
                (self.ValueKind != JsonValueKind.Object ||
                    self.GetProperty("warmth").GetString() == (request.Observation.Self?.WarmthBasisPoints is { } warmth ? ModelNeedWords.Warmth(warmth) : null) &&
                    self.GetProperty("illness").GetString() == (request.Observation.Self?.IllnessBasisPoints is { } illness ? ModelNeedWords.Illness(illness) : null));
            lock (sync)
            {
                RequestCharacters += handler.Body!.Length;
                if (numbers) RequestsWithNumbers++;
                if (words && !numbers) RequestsWithWords++;
            }
            return response;
        }
    }

    private sealed class ReplyHandler(string choice) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var content = JsonSerializer.Serialize(new { selected_candidate_id = choice, confidence = 1 });
            var reply = JsonSerializer.Serialize(new
            {
                model = "offline-stand-in",
                choices = new[] { new { message = new { role = "assistant", content } } },
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class CallBudget(int limit)
    {
        private int used;
        public int Limit => limit;
        public bool Exceeded => Volatile.Read(ref used) > limit;
        public bool TryTake() => Interlocked.Increment(ref used) <= limit;
    }

    /// <summary>Refuses to send once the paid-call limit is spent.</summary>
    private sealed class BudgetHandler(CallBudget budget) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            budget.TryTake()
                ? base.SendAsync(request, cancellationToken)
                : throw new HttpRequestException("The comparison's paid-call limit is spent.");
    }
}

/// <summary>
/// Runs the need-wording report only when CLANKERWORLD_NEED_WORDING_COMPARISON
/// is "offline" or "model". "model" also needs an endpoint, model and key and
/// makes paid calls.
/// </summary>
public sealed class NeedWordingComparisonFactAttribute : FactAttribute
{
    public static string? Mode => Environment.GetEnvironmentVariable("CLANKERWORLD_NEED_WORDING_COMPARISON")?.Trim().ToLowerInvariant();

    public NeedWordingComparisonFactAttribute()
    {
        if (Mode is not ("offline" or "model"))
            Skip = "Set CLANKERWORLD_NEED_WORDING_COMPARISON to offline or model to run the need-wording comparison.";
    }
}
