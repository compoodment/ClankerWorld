using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACompletedVoterCanFinishHouseCraftingWithoutHavingToVolunteer(bool willing)
    {
        var state = RegisteredElection(day: 48, nomineeIndexes: willing ? [0, 1, 2, 4] : [0, 1, 2]);
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var hall = state.WorldSimulation.Buildings.Single(building => building.DefinitionId == TownHallContent.TownHall().CanonicalId);
        var actor = state.Towns!.Single().ResidentIds[0];
        var insideHall = new GridPoint(hall.Position.X + 1, hall.Position.Y + 2);
        Assert.DoesNotContain(state.Inhabitants.Where(person => person.InhabitantId != actor), person => person.Position == insideHall);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = insideHall } : person).ToArray()
        };
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "voter-craft-fiber", "fiber", Alpha, 3,
            storageBuildingId: house.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "voter-craft-tool", "tool", actor, 1);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var rope = state.WorldContent!.Recipes.Single(recipe => recipe.LocalId == "twist-rope");
        var provider = new ChooseOneProject("build:recipe:" + rope.CanonicalId);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? provider : new CivicChooser(false, false));
        var nominees = world.TownCouncils.Single().Election!.Candidates.Where(id => id != actor).Take(3).ToArray();
        Assert.True(world.VoteTownElection(actor, Town, nominees).Applied);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var travelling = world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Equal("travelling", travelling.Project!.Stage);
        Assert.Contains(WorldContentSimulationRules.Footprint(TownHallContent.TownHall(), hall),
            tile => state.Map.FootDistance(tile, travelling.Position) <= 1);
        var rest = world.SubmitInstruction(new("voter-brief-rest", "owner:test", actor,
            OwnerInstructionKind.Suggestive, "Take a brief rest."));
        for (var turn = 0; turn < 4 && !world.ExportState().CompletedInstructionIds!.Contains(rest.InstructionId); turn++)
        {
            var beforeRest = world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Position;
            var rested = await world.AdvanceOneTickAsync();
            Assert.True(rested.Advanced);
            if (rested.Decisions.FirstOrDefault(decision => decision.InhabitantId == actor) is not { } decision) continue;
            Assert.Equal("safe_idle", decision.Admission.Intention!.CandidateId);
            Assert.Equal(beforeRest, world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Position);
        }
        Assert.Contains(rest.InstructionId, world.ExportState().CompletedInstructionIds!);
        Assert.Contains(WorldContentSimulationRules.Footprint(TownHallContent.TownHall(), hall), tile =>
            state.Map.FootDistance(tile, world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Position) <= 1);

        for (var tick = 0; tick < 40 && !world.WorldSimulation.ProductionJobs.Any(job =>
                 job.RecipeId == rope.CanonicalId && job.State == WorldProductionJobState.Completed); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var completed = Assert.Single(world.WorldSimulation.ProductionJobs, job =>
            job.RecipeId == rope.CanonicalId && job.State == WorldProductionJobState.Completed);
        Assert.Equal(actor, completed.WorkerId);
        Assert.Equal(house.InstanceId, completed.BuildingInstanceId);
        Assert.Equal(3, completed.InputReservationIds.Sum(id => world.Society.Inventory.GetReservation(id).Quantity));
        Assert.All(completed.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
            world.Society.Inventory.GetReservation(id).State));
        Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "rope" && lot.OwnerId == Alpha &&
            lot.StorageBuildingId == house.InstanceId && lot.Quantity == 1);
        var election = world.TownCouncils.Single().Election!;
        Assert.NotNull(election);
        Assert.True(world.WorldTick < election.ExpiryTick);
        Assert.Equal(willing, election.Candidates.Contains(actor, StringComparer.Ordinal));
        Assert.Equal(nominees.Order(StringComparer.Ordinal), election.Votes.Single(vote => vote.VoterId == actor).CandidateIds);
        Assert.Contains(willing ? "council_town_withdraw" : "council_town_volunteer", provider.FirstCandidateIds!);
        Assert.Equal(1, provider.ProjectChoices);
        world.Validate();
    }

    private sealed class ChooseOneProject(string candidateId) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public int ProjectChoices { get; private set; }
        public IReadOnlyList<string>? FirstCandidateIds { get; private set; }
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            FirstCandidateIds ??= request.Observation.Candidates.Select(candidate => candidate.Id).ToArray();
            var selected = ProjectChoices == 0 ? request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == candidateId) : null;
            if (selected is not null) ProjectChoices++;
            selected ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [selected] } }, cancellationToken);
        }
    }
}
