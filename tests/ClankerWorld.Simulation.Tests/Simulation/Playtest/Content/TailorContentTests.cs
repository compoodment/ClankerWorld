using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class TailorContentTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";

    [Fact]
    public async Task HouseholdTurnsFiberIntoClothAndClothingThroughOrdinaryChoicesAcrossReload()
    {
        var (state, shopId) = WorldWithTailorShop("tailor-flow", fiberInHouse: 6);
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var weave = state.WorldContent!.Recipes.Single(item => item.LocalId == "weave-cloth");
        var sew = state.WorldContent.Recipes.Single(item => item.LocalId == "sew-clothing");
        Assert.Equal("cloth", Assert.Single(weave.Outputs).ResourceId);
        Assert.Equal("cloth", Assert.Single(sew.Inputs).ResourceId);
        Assert.Equal("clothing", Assert.Single(sew.Outputs).ResourceId);
        Assert.DoesNotContain(state.WorldContent.Buildings, building => building.LocalId == "weaving-frame");
        Assert.DoesNotContain(state.WorldContent.Recipes, recipe => recipe.DisplayName == "Woven clothing");
        foreach (var (household, house) in new[] { (Alpha, "first-town-house-a"), (Beta, "first-town-house-b") })
        {
            var members = state.Society.Society.Inhabitants.Count(person => person.HouseholdId == household);
            Assert.Equal(2, members);
            Assert.Equal(members, state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == household &&
                lot.ItemKind == "clothing" && lot.StorageBuildingId == house).Sum(lot => lot.Quantity));
        }
        var allowed = new[] { "haul_household_stock", "supply_workstation:fiber", "build:recipe:" + weave.CanonicalId,
            "build:recipe:" + sew.CanonicalId };
        IDecisionProvider Provider(string id) => new AllowedChoices(id == actor ? allowed : []);

        var world = PrivateWorldRuntime.Restore(state, Provider);
        try
        {
            var reloaded = false;
            for (var tick = 0; tick < 1_500 && !TailorOutputs(world, shopId).Any(lot => lot.ItemKind == "clothing"); tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!reloaded && world.WorldSimulation.ProductionJobs.Any(job => job.RecipeId == weave.CanonicalId &&
                        job.State == WorldProductionJobState.Running))
                {
                    // Interrupted work: save and load while cloth is being woven.
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, Provider);
                    reloaded = true;
                }
            }
            Assert.True(reloaded);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "workstation_input_picked_up");

            var jobs = world.WorldSimulation.ProductionJobs.Where(job => job.State == WorldProductionJobState.Completed).ToArray();
            var woven = jobs.Count(job => job.RecipeId == weave.CanonicalId);
            var sewn = jobs.Count(job => job.RecipeId == sew.CanonicalId);
            Assert.InRange(woven, 2, 3);
            Assert.Equal(1, sewn);
            Assert.All(jobs, job => Assert.Equal(actor, job.WorkerId));
            // Exact accounting: the House's fiber plus any the agent gathered
            // once it ran out, three per cloth and two cloth per garment.
            var gathered = world.ExportState().Events.Where(item => item.Kind == "material_gathered" &&
                    item.Detail.StartsWith(actor + ":fiber:", StringComparison.Ordinal))
                .Sum(item => int.Parse(item.Detail[(actor.Length + ":fiber:".Length)..], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(6 + gathered - 3 * woven, HouseholdTotal(world, "fiber"));
            Assert.Equal(woven - 2 * sewn, HouseholdTotal(world, "cloth"));
            Assert.Equal(sewn, TailorOutputs(world, shopId).Where(lot => lot.ItemKind == "clothing").Sum(lot => lot.Quantity));
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == Beta && lot.ItemKind is "cloth" or "fiber");
            world.Validate();
        }
        finally
        {
            world.Dispose();
        }
    }

    private static (PrivateWorldRuntimeState State, string ShopId) WorldWithTailorShop(string seed, int fiberInHouse)
        => TailorTestWorld.Create(seed, fiberInHouse);

    private static IEnumerable<InventoryLot> TailorOutputs(PrivateWorldRuntime world, string shopId) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == Alpha && lot.StorageBuildingId == shopId && lot.Quantity > 0);

    private static int HouseholdTotal(PrivateWorldRuntime world, string kind) => world.Society.Inventory.Lots
        .Where(lot => lot.ItemKind == kind && (lot.OwnerId == Alpha ||
            world.Society.Inhabitants.Any(person => person.Id == lot.OwnerId && person.HouseholdId == Alpha)))
        .Sum(lot => lot.Quantity);

    /// <summary>Chooses among the allowed candidates by the built-in rules, otherwise stays idle.</summary>
    private sealed class AllowedChoices(IReadOnlyList<string> allowed) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var permitted = request.Observation.Candidates
                .Where(candidate => allowed.Any(prefix => candidate.Id.StartsWith(prefix, StringComparison.Ordinal)))
                .ToArray();
            var choices = permitted.Length > 0 ? permitted
                : [request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle")];
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = choices },
            }, cancellationToken);
        }
    }
}
