using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ExpiredBlockedHouseholdBuildTests
{
    [Fact]
    public async Task ExpiredBlockedPlanOffersANewSiteToItsOwnerAndKeepsTheHouseholdClaim()
    {
        using var setup = NormalPathWorld.CreateGenerated("probe-a", _ => new IdleProvider());
        var state = setup.ExportState();
        var farmhouse = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-farmhouse");
        var household = farmhouse.HouseholdId!;
        var members = state.Society.Society.Inhabitants.Where(item => item.HouseholdId == household &&
                item.AgeBand == SocietyAgeBand.Adult)
            .Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
        Assert.True(members.Length >= 2);
        var actor = members[0];
        var sibling = members[1];
        var silo = setup.WorldContent.Buildings.Single(item => item.LocalId == "silo-1x1");
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in silo.BuildCosts)
        {
            inventory = InventoryFixture.AddLot(inventory, $"retry-silo-{cost.ResourceId}", cost.ResourceId,
                household, cost.Amount * 2);
        }
        if (household != "household:camp-alpha")
        {
            inventory = InventoryFixture.AddLot(inventory, "retry-blocker-wood", "wood", "household:camp-alpha", 4);
        }
        var provider = new RetryChoiceProvider();
        provider.ChooseInitialBuild(actor, silo.CanonicalId);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor || person.InhabitantId == sibling
                ? person with { HungerBasisPoints = 9_500, LastDecisionContext = null, Project = null }
                : person).ToArray(),
        };
        using var observing = PrivateWorldRuntime.Restore(state, _ => provider);
        for (var tick = 0; tick < 120 && (provider.InitialCandidateId is null ||
                 observing.Inhabitants.Single(item => item.InhabitantId == actor).Project?.CandidateId != provider.InitialCandidateId); tick++)
        {
            Assert.True((await observing.AdvanceOneTickAsync()).Advanced);
        }

        var originallyOffered = provider.InitialCandidateId;
        Assert.NotNull(originallyOffered);
        var project = observing.Inhabitants.Single(item => item.InhabitantId == actor).Project;
        Assert.NotNull(project);
        Assert.Equal(originallyOffered, project.CandidateId);
        for (var tick = 0; tick < 120 && observing.Inhabitants.Single(item => item.InhabitantId == actor)
                 .Project!.WorkDone < 4; tick++)
        {
            Assert.True((await observing.AdvanceOneTickAsync()).Advanced);
        }

        project = observing.Inhabitants.Single(item => item.InhabitantId == actor).Project;
        Assert.NotNull(project);
        Assert.InRange(project.WorkDone, 4, 9);
        Assert.True(TownConstructionCandidateIds.TryParse(originallyOffered, out var blockedSelection));
        var blockedSite = blockedSelection.SitePosition!.Value;
        var workBeforeBlock = project.WorkDone;
        var blocker = observing.WorldContent.Buildings.Single(item => item.LocalId == "fire");
        var occupied = observing.PlaceBuilding("retry-blocked-site", blocker.CanonicalId, blockedSite);
        Assert.True(occupied.Applied, occupied.Failure);
        Assert.True((await observing.AdvanceOneTickAsync()).Advanced);
        project = observing.Inhabitants.Single(item => item.InhabitantId == actor).Project;
        Assert.NotNull(project);
        Assert.Equal("blocked", project.Stage);
        Assert.Equal(workBeforeBlock, project.WorkDone);

        state = observing.ExportState();
        var blockedSince = state.Society.Society.WorldTick;
        var retainedWork = project.WorkDone;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    HungerBasisPoints = 9_500,
                }
                : person.InhabitantId == sibling ? person with { LastDecisionContext = null } : person).ToArray(),
        };
        var householdMaterialsBeforeRetry = MaterialTotals(state.Society.Society.Inventory, household);
        provider.Arm(actor, silo.CanonicalId, blockedSite);
        using var world = PrivateWorldRuntime.Restore(state, _ => provider);

        for (var tick = 0; tick < 75 && world.Inhabitants.Single(item => item.InhabitantId == actor)
                 .Project?.CandidateId == originallyOffered; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        }

        var actorRequests = provider.RequestsFor(actor);
        var retryRequest = Assert.Single(actorRequests, item => item.CandidateIds.Any(id =>
            TownConstructionCandidateIds.TryParse(id, out var selection) && selection.IsBuilding &&
            selection.DefinitionId == silo.CanonicalId && selection.SitePosition != blockedSite));
        Assert.True(retryRequest.WorldTick - blockedSince >= 60,
            $"The blocked site was retried after only {retryRequest.WorldTick - blockedSince} ticks.");
        var siblingRequests = provider.RequestsFor(sibling);
        Assert.NotEmpty(siblingRequests);
        Assert.DoesNotContain(siblingRequests.SelectMany(item => item.CandidateIds), id =>
            TownConstructionCandidateIds.TryParse(id, out var selection) && selection.IsBuilding &&
            selection.DefinitionId == silo.CanonicalId);

        var retriedPerson = world.Inhabitants.Single(item => item.InhabitantId == actor);
        Assert.NotNull(retriedPerson.Project);
        Assert.NotEqual(originallyOffered, retriedPerson.Project.CandidateId);
        Assert.InRange(retriedPerson.Project.WorkDone, retainedWork, 10);
        Assert.Equal(householdMaterialsBeforeRetry, MaterialTotals(world.Society.Inventory, household));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "project_site_reselected" &&
            item.Detail.StartsWith(actor + ":" + silo.CanonicalId, StringComparison.Ordinal));
        Assert.Contains(world.WorldSimulation.Buildings, item => item.InstanceId == "retry-blocked-site" &&
            item.Position == blockedSite);
        Assert.Null(world.Inhabitants.Single(item => item.InhabitantId == sibling).Project);
        world.Validate();
    }

    private static (string ItemKind, int Quantity)[] MaterialTotals(InventoryCheckpoint inventory, string ownerId) =>
        inventory.Lots.Where(lot => lot.OwnerId == ownerId && lot.ItemKind is "wood" or "stone")
            .GroupBy(lot => lot.ItemKind, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (group.Key, group.Sum(lot => lot.Quantity))).ToArray();

    private sealed record CapturedRequest(long WorldTick, string[] CandidateIds);

    private sealed class RetryChoiceProvider : IDecisionProvider
    {
        private readonly ConcurrentDictionary<string, ConcurrentQueue<CapturedRequest>> requests = new(StringComparer.Ordinal);
        private string? actor;
        private string? definitionId;
        private GridPoint? blockedSite;
        private bool chooseInitialBuild;
        private bool chooseReplacement;

        public string? InitialCandidateId { get; private set; }

        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public CapturedRequest[] RequestsFor(string inhabitantId) =>
            requests.TryGetValue(inhabitantId, out var captured) ? captured.ToArray() : [];

        public void Arm(string targetActor, string targetDefinitionId, GridPoint oldSite)
        {
            requests.Clear();
            actor = targetActor;
            definitionId = targetDefinitionId;
            blockedSite = oldSite;
            chooseInitialBuild = false;
            chooseReplacement = true;
        }

        public void ChooseInitialBuild(string targetActor, string targetDefinitionId)
        {
            actor = targetActor;
            definitionId = targetDefinitionId;
            chooseInitialBuild = true;
            chooseReplacement = false;
        }

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var candidates = observation.Candidates;
            requests.GetOrAdd(observation.InhabitantId, _ => new ConcurrentQueue<CapturedRequest>())
                .Enqueue(new CapturedRequest(observation.WorldTick, candidates.Select(item => item.Id).ToArray()));
            var selected = candidates.Single(item => item.Id == "safe_idle");
            if (observation.InhabitantId == actor)
            {
                var matchingBuilding = candidates.FirstOrDefault(item => TownConstructionCandidateIds.TryParse(item.Id, out var selection) &&
                    selection.IsBuilding && selection.DefinitionId == definitionId &&
                    (!chooseReplacement || selection.SitePosition != blockedSite));
                if (chooseInitialBuild && matchingBuilding is not null)
                {
                    selected = matchingBuilding;
                    InitialCandidateId = selected.Id;
                    chooseInitialBuild = false;
                }
                else if (chooseReplacement && matchingBuilding is not null)
                {
                    selected = matchingBuilding;
                }
            }
            var probabilities = candidates.ToDictionary(item => item.Id,
                item => item.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId,
                Kind, ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration,
                observation.ObservationDigest, selected.Id, 1, probabilities));
        }
    }

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) =>
            new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with
                {
                    Candidates = [request.Observation.Candidates.Single(item => item.Id == "safe_idle")],
                },
            }, cancellationToken);
    }
}
