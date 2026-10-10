using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class RestaurantBusinessPipelineTests
{
    // Existing meal tests do not exercise the order emergency filter. This
    // compares real cooking, payment and eating with only the order changed.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UrgentCustomerCanBuyCookedRestaurantFoodWithoutCancellingAnOrder(bool ordered)
    {
        var fixture = CreateFixture("porridge", kitchenReady: true);
        using var cooking = PrivateWorldRuntime.Restore(fixture.State, id => id == fixture.Cook
            ? fixture.CookProvider : new PipelineProvider("safe_idle"));
        await AdvanceUntil(cooking, () => cooking.WorldSimulation.ProductionJobs.Any(job =>
            job.RecipeId == fixture.Recipe.CanonicalId && job.State == WorldProductionJobState.Completed), limit: 80);
        var initial = cooking.ExportState();
        Assert.Equal(2, Assert.Single(cooking.Society.Inventory.Lots, lot => lot.ItemKind == "porridge").Quantity);
        var foodSources = initial.Map.Resources.Where(resource => resource.Kind is "food" or "fruit")
            .Select(resource => resource.Id).ToHashSet();
        initial = initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == fixture.Customer
                ? person with { HungerBasisPoints = 1_500, LastDecisionContext = null } : person).ToArray(),
            Resources = initial.Resources.Select(resource => foodSources.Contains(resource.ResourceId)
                ? resource with { State = ResourceState.Depleted } : resource).ToArray(),
            WorldSystems = initial.WorldSystems! with
            {
                Ecology = initial.WorldSystems.Ecology with
                {
                    Resources = initial.WorldSystems.Ecology.Resources.Select(resource => foodSources.Contains(resource.Id)
                        ? resource with { Quantity = 0, State = EcologyResourceState.Depleted, NextRegenerationDay = 100 }
                        : resource).ToArray(),
                },
            },
        };
        fixture = fixture with { State = initial };
        using var world = Restore(fixture);
        var receipt = ordered ? world.SubmitInstruction(new("restaurant-order-survival", "owner:test",
            fixture.Customer, OwnerInstructionKind.MustDo, "keep gathering wood")) : null;
        await Advance(world, 24);
        var sale = Assert.Single(world.BusinessTrades, trade => trade.BuyerId == fixture.Customer);
        Assert.Equal("porridge", sale.GoodsKind);
        Assert.Equal(DirectBarterState.Settled, world.Society.Inventory.GetOffer(sale.OfferId).State);
        var customer = world.Inhabitants.Single(person => person.InhabitantId == fixture.Customer);
        Assert.Equal("porridge", customer.Survival!.LastMealKind);
        Assert.True(customer.HungerBasisPoints > 1_500);
        var payment = world.Society.Inventory.GetLot("restaurant-customer-payment");
        Assert.Equal((Beta, fixture.Restaurant.InstanceId, 1), (payment.OwnerId, payment.StorageBuildingId, payment.Quantity));
        if (receipt is not null)
        {
            var saved = world.ExportState();
            var order = Assert.Single(saved.Instructions!, item => item.InstructionId == receipt.InstructionId).Order!;
            Assert.True(order.RepeatUntilCancelled);
            Assert.DoesNotContain(receipt.InstructionId, saved.CompletedInstructionIds!);
            Assert.NotEqual("finished", order.Status);
            Assert.NotEqual("cancelled", order.Status);
        }
        await AssertStrictReplayAndDiscard(world, fixture);
    }
}
