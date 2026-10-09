using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class ProviderConfigurationStoreTests
{
    [Theory]
    [InlineData("jev", "Adult")]
    [InlineData("decisions", "Adult")]
    [InlineData("jev", "Elder")]
    [InlineData("decisions", "Elder")]
    public async Task WorldBornAdultOrElderUsesSelectedHelperForRoutineChoiceAndOwnMemoryScores(string helper, string lifeStage)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-grown-helper-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), EmptySeed());
            _ = store.Configure(new("routine", "jev", "jev-test", "synthetic-jev-key", false));
            _ = store.Configure(new("planning", "openai", "world-planner", "synthetic-openai-key", false));
            var policy = new WorldJevPolicy();
            policy.Set(true, 1, helper == "jev" ? RoutineHelperSettings.Jev : new("decisions", "gpt-6-luna"));
            using var handler = new ProviderResponseHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler), jevPolicy: policy);
            var observation = RequestObservation(strategic: false) with
            {
                RequiresPersonalProvider = true,
                Self = new("inhabitant-test", "Zuri Ash", lifeStage, "Inventive", "Learn", "family", null, null, null),
                MemoryCompactionCandidates = [new("private-source", "inhabitant-test", "experience", "friend", "A remembered promise.", 1)],
            };
            Assert.Equal(helper == "jev" ? DecisionProviderKind.Jev : DecisionProviderKind.OpenAiDecisions, router.KindFor(observation));
            var runtime = new CognitionRuntime("inhabitant-test", router);
            var response = await router.DecideAsync(runtime.IssueRequest(observation));
            var admitted = runtime.ApplyResponse(response);
            Assert.True(admitted.Accepted);
            Assert.False(admitted.FellBack);
            Assert.Equal("safe_idle", admitted.Intention!.CandidateId);
            var score = Assert.Single(admitted.MemoryCompactionScores!);
            Assert.Equal("private-source", score.Id);
            Assert.Equal("inhabitant-test", score.OwnerId);
            Assert.Equal(9_000, score.ImportanceBasisPoints);
            Assert.Equal(helper == "jev" ? "api.typesafe.ai" : "api.openai.com", handler.LastUri!.Host);
            Assert.Equal(helper == "jev" ? "/v1/systemone" : "/v1/decisions", handler.LastUri.AbsolutePath);
            Assert.Equal(1, handler.RequestCount);
            Assert.DoesNotContain("private-source", handler.LastBody, StringComparison.Ordinal);
        }
        finally { directory.Delete(recursive: true); }
    }
    [Theory]
    [InlineData("Infant", true)]
    [InlineData("Child", true)]
    [InlineData("Adolescent", true)]
    [InlineData("Child", false)]
    public async Task MinorsKeepTheirOwnModelWithTheWorldHelperEnabled(string lifeStage, bool worldBorn)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-minor-helper-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), EmptySeed());
            _ = store.Configure(new("routine", "jev", "jev-test", "synthetic-jev-key", false));
            _ = store.Configure(new("personal", "openai", "own-model", "synthetic-personal-key", false, "inhabitant-test"));
            var policy = new WorldJevPolicy();
            policy.Set(true, 1, new("decisions", "gpt-6-luna"));
            using var handler = new ProviderResponseHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler), jevPolicy: policy);
            var observation = RequestObservation(strategic: false) with
            {
                RequiresPersonalProvider = worldBorn,
                Self = new("inhabitant-test", "Zuri Ash", lifeStage, "Inventive", "Learn", "family", null, null, null),
            };
            Assert.Equal(DecisionProviderKind.LargeLanguageModel, router.KindFor(observation));
            _ = await router.DecideAsync(new("minor", router.ProviderEpoch, observation));
            Assert.Equal("/v1/chat/completions", handler.LastUri!.AbsolutePath);
            Assert.Equal("own-model", handler.LastModel);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("off", true)]
    [InlineData("off", false)]
    [InlineData("name", true)]
    [InlineData("name-retry", true)]
    [InlineData("identity", true)]
    [InlineData("planning", true)]
    public async Task GrownWorldBornPersonalRequestsAndHelperOffKeepBirthModelProtection(string requestKind, bool assigned)
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-grown-personal-");
        try
        {
            var store = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"), EmptySeed());
            _ = store.Configure(new("routine", "jev", "jev-test", "synthetic-jev-key", false));
            _ = store.Configure(new("planning", "openai", "world-planner", "synthetic-world-key", false));
            if (assigned) _ = store.Configure(new("personal", "openai", "birth-model", "synthetic-personal-key", false, "inhabitant-test"));
            var policy = new WorldJevPolicy();
            policy.Set(requestKind != "off", 1, requestKind == "off" ? RoutineHelperSettings.Off : RoutineHelperSettings.Jev);
            using var handler = new ProviderResponseHandler();
            var router = new ConfigurableDecisionProvider(store, new FixedHttpClientFactory(handler), jevPolicy: policy);
            var observation = RequestObservation(strategic: requestKind == "planning") with
            {
                RequiresPersonalProvider = true,
                Self = new("inhabitant-test", "Zuri Ash", "Adult", "Inventive", "Learn", "family", null, null, null),
                NeedsName = requestKind == "name",
                IsNameRetry = requestKind == "name-retry",
                NeedsPersonality = requestKind == "identity",
                NeedsAspiration = requestKind == "identity",
            };
            Assert.Equal(assigned ? DecisionProviderKind.LargeLanguageModel : DecisionProviderKind.Deterministic, router.KindFor(observation));
            _ = await router.DecideAsync(new("personal", router.ProviderEpoch, observation));
            Assert.Equal(assigned ? 1 : 0, handler.RequestCount);
            if (assigned)
            {
                Assert.Equal("/v1/chat/completions", handler.LastUri!.AbsolutePath);
                Assert.Equal("birth-model", handler.LastModel);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

}
