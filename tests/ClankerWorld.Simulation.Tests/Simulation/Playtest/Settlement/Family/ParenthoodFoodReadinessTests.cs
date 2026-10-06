using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    private static readonly Lazy<Task<byte[]>> FoodReadinessCheckpoint = new(BuildFoodReadinessCheckpoint);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParenthoodCardsExplainTheActualFoodGateAndPreserveBirthPayment(bool enoughFood)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await FoodReadinessCheckpoint.Value);
        var owner = state.Inhabitants.Single(person => person.Parenthood is { Stage: "preparing" }).InhabitantId;
        var plan = state.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood!;
        var caregiver = plan.PrimaryCaregiverId!;
        state = FoodReadinessStock(state, caregiver, enoughFood);
        var observations = new ConcurrentQueue<CognitionDecisionRequest>();
        using var world = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("safe_idle", observations.Enqueue));
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(4, world.Society.GetHousehold(world.Society.GetInhabitant(caregiver).HouseholdId!).MemberIds.Count);
        var expected = enoughFood ? "12/12 ready-to-eat portions" : "3/12 ready-to-eat portions";
        foreach (var parent in new[] { owner, plan.PartnerId })
        {
            var notes = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == parent).SocialNotes;
            Assert.Contains(notes, note => note.Contains(expected, StringComparison.Ordinal));
            Assert.DoesNotContain(notes, note => note.Contains("shelter", StringComparison.Ordinal) ||
                note.Contains("consent", StringComparison.Ordinal));
        }
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ParentProvider("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        world.Resume();
        restored.Resume();
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        }
        if (enoughFood)
        {
            Assert.Single(world.Society.Births);
            Assert.Equal(10, world.Society.Inventory.GetLot("readiness-food").Quantity);
        }
        else
        {
            Assert.Empty(world.Society.Births);
            Assert.Equal("preparing", world.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood!.Stage);
            Assert.Equal(5, world.Society.Inventory.GetLot("readiness-food").Quantity);
            Assert.Contains(observations, request => request.Observation.Self?.ContinuityNote?.Contains(expected, StringComparison.Ordinal) == true);
            Assert.Contains(observations, request => request.Observation.Self?.ContinuityNote?.Contains("need 9 more", StringComparison.Ordinal) == true);
            Assert.All(observations, request => Assert.True(request.Observation.Self?.ContinuityNote?.Length is null or <= 256));
        }
        Assert.Equal(12, world.Society.Inventory.GetLot("readiness-private").Quantity);
        Assert.Equal(2, world.Society.Inventory.GetReservation("readiness-reservation").Quantity);
    }

    [Fact]
    public async Task CrossHouseholdParentsSeeTheBlockerWithoutLearningTheOtherHouseholdsStock()
    {
        var (state, initiator, partner) = ContinuityIntegrationCouple("food-readiness-privacy", separateHouseholds: true);
        using var preparing = PrivateWorldRuntime.Restore(state, id => new ParentProvider(id == initiator
            ? "parent_propose:" + partner : id == partner ? $"parent_accept:{initiator}:acceptor:" : "safe_idle"));
        Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);
        Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);
        preparing.Pause();
        var plan = preparing.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!;
        Assert.Equal("preparing", plan.Stage);
        Assert.Equal(partner, plan.PrimaryCaregiverId);
        var observations = new ConcurrentQueue<CognitionDecisionRequest>();
        using var world = PrivateWorldRuntime.Restore(FoodReadinessStock(preparing.ExportState(), partner, false),
            _ => new ParentProvider("safe_idle", observations.Enqueue));
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        Assert.All(snapshot.Inhabitants.Where(person => person.Id == initiator || person.Id == partner),
            person => Assert.Contains(person.SocialNotes, note => note.Contains("3/8 ready-to-eat portions", StringComparison.Ordinal)));
        world.Resume();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var own = Assert.Single(observations, request => request.Observation.Self!.OwnerId == partner).Observation.Self!.ContinuityNote!;
        var other = Assert.Single(observations, request => request.Observation.Self!.OwnerId == initiator).Observation.Self!.ContinuityNote!;
        Assert.Contains("3/8 ready-to-eat portions", own, StringComparison.Ordinal);
        Assert.Contains("caregiver's household needs more ready-to-eat food", other, StringComparison.Ordinal);
        Assert.DoesNotContain("3/8", other, StringComparison.Ordinal);
        Assert.DoesNotContain("need 5", other, StringComparison.Ordinal);
        Assert.Empty(world.Society.Births);
    }

    private static async Task<byte[]> BuildFoodReadinessCheckpoint()
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, id => new ParentProvider(id == first
            ? "parent_propose:" + second : id == second ? "parent_accept:" : "safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var plan = world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!;
        Assert.Equal("preparing", plan.Stage);
        // All preparation ticks run normally; stop one tick before the real birth gate.
        while (world.WorldTick < plan.LastTransitionTick + 599)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(world.Society.Births);
        world.Pause();
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private static PrivateWorldRuntimeState FoodReadinessStock(PrivateWorldRuntimeState state, string caregiver, bool enoughFood)
    {
        var society = state.Society.Society;
        var household = society.GetInhabitant(caregiver).HouseholdId!;
        var other = society.GetHousehold(household).MemberIds.First(id => id != caregiver);
        // Controlled, validated starting stocks. Raw, spoiled, private, carried-by-another
        // and broken-pot food must not inflate the birth gate or its displayed amount.
        var inventory = society.Inventory with
        {
            Lots = society.Inventory.Lots.Select(lot => lot.OwnerId == household ? lot with { FreshnessBasisPoints = 0 } : lot).Concat(new InventoryLot[]
            {
                new("readiness-food", "food", household, enoughFood ? 14 : 5, 10_000, 10_000, society.WorldTick),
                new("readiness-grain", "grain", household, 11, 10_000, 10_000, society.WorldTick),
                new("readiness-flour", "flour", household, 6, 10_000, 10_000, society.WorldTick),
                new("readiness-private", "bread", caregiver, 12, 10_000, 10_000, society.WorldTick),
                new("readiness-spoiled", "berries", household, 12, 10_000, 0, society.WorldTick),
                new("readiness-carried", "cultivated_greens", household, 7, 10_000, 10_000, society.WorldTick, CarrierId: other),
                new("readiness-pot", "storage_pot", household, 1, 0, 10_000, society.WorldTick),
                new("readiness-pot-food", "fruit", household, 4, 10_000, 10_000, society.WorldTick, ContainerLotId: "readiness-pot"),
            }).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray(),
        };
        inventory = InventoryFixture.Reserve(inventory, "readiness-reservation", household,
            "readiness-food", 2, "other household use", society.WorldTick + 100, isExclusive: false);
        return state with
        {
            Society = state.Society with { Society = society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with { HungerBasisPoints = 10_000, LastDecisionContext = null }).ToArray(),
        };
    }
}
