using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Fact]
    public async Task ChildCanGrowIntoAWorkingAdultThroughSavedSettlementLife()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-generation-");
        PrivateWorldRuntime? world = null;
        try
        {
            var observations = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
            var state = await PreparedState();
            world = PrivateWorldRuntime.Restore(state, _ => new GenerationProvider(observations));
            world.Pause();
            world.SetLifePace(1_460);
            world.Resume();
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), _ => new GenerationProvider(observations));
            string? childId = null;
            var careSeen = false;
            var restarts = 0;
            for (var tick = 0; tick < 12_000; tick++)
            {
                var step = await world.AdvanceOneTickAsync();
                careSeen |= step.Events.Any(item => item.Kind == "child_cared_for");
                if (childId is null && world.Society.Births.Count > 0) childId = world.Society.Births[0].ChildId;
                if (childId is not null)
                {
                    var child = world.Society.GetInhabitant(childId);
                    Assert.True(child.Status == SocietyInhabitantStatus.Active,
                        $"Child died at tick {world.WorldTick}, age {world.Society.AgeAt(child, world.WorldTick)}, cause {child.DeathCause}.");
                    if (child.AgeBand == SocietyAgeBand.Adult && child.CurrentRole is SocietyWorkRole.Builder or SocietyWorkRole.Farmer &&
                        world.Inhabitants.Single(person => person.InhabitantId == childId).Project is { Stage: "completed" })
                        break;
                }
                if (tick % 256 == 0) file.Save(world);
                if (tick is 2_000 or 5_000)
                {
                    world.Pause();
                    file.Save(world);
                    world.Dispose();
                    world = file.LoadOrCreate(state.WorldSeed);
                    Assert.True(world.Society.IsPaused);
                    world.Resume();
                    restarts++;
                }
            }
            Assert.NotNull(childId);
            var grown = world.Society.GetInhabitant(childId);
            Assert.Equal(SocietyAgeBand.Adult, grown.AgeBand);
            Assert.True(grown.CurrentRole is SocietyWorkRole.Builder or SocietyWorkRole.Farmer,
                "Teaching state=" + System.Text.Json.JsonSerializer.Serialize(world.Inhabitants.Select(person => new
                {
                    person.InhabitantId,
                    person.Lesson,
                    person.Position,
                    person.HungerBasisPoints,
                    person.Survival,
                    person.Project,
                    Choices = observations.GetValueOrDefault(person.InhabitantId),
                })));
            Assert.True(world.Inhabitants.Single(person => person.InhabitantId == childId).Project is { Stage: "completed" },
                $"Role={grown.CurrentRole}; Choices={observations.GetValueOrDefault(childId)}; " +
                "Physical=" + System.Text.Json.JsonSerializer.Serialize(world.Inhabitants.Single(person => person.InhabitantId == childId)) + "; " +
                "Stock=" + string.Join(',', world.Society.Inventory.Lots.GroupBy(lot => lot.ItemKind).Select(group => group.Key + "=" + group.Sum(lot => lot.Quantity))) +
                "; Sources=" + string.Join(',', world.WorldSystems.Ecology.Resources.Select(resource => resource.Kind + "=" + resource.Quantity)));
            Assert.True(careSeen);
            Assert.Equal(2, restarts);
            Assert.NotNull(world.ExportState().HistoryArchiveHead);
        }
        finally
        {
            world?.Dispose();
            directory.Delete(recursive: true);
        }
    }

    private sealed class GenerationProvider(System.Collections.Concurrent.ConcurrentDictionary<string, string> observations) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates;
            observations[request.Observation.InhabitantId] = string.Join(',', candidates.Select(item => item.Id));
            CognitionCandidate? chosen = null;
            if (request.Observation.HungerBasisPoints >= 3_500)
            {
                chosen = candidates.FirstOrDefault(item => item.Id.StartsWith("care:", StringComparison.Ordinal));
                if (request.Observation.WorldTick < 1_000)
                    chosen ??= candidates.FirstOrDefault(item => item.Id.StartsWith("parent_accept:", StringComparison.Ordinal) ||
                        item.Id.StartsWith("parent_propose:", StringComparison.Ordinal));
            }
            var allowed = chosen is null ? candidates.Where(item => !item.Id.StartsWith("parent_", StringComparison.Ordinal)).ToArray() : [chosen];
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = allowed },
            }, cancellationToken);
        }
    }
}
