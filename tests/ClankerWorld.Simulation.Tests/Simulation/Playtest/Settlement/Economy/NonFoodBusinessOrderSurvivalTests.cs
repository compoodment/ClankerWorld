using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class BusinessTradeTests
{
    // Normal shopping tests do not prove that hunger keeps a tool quote out
    // of an order's survival choices. Exercise both a new and an open quote.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UrgentFoodDoesNotExposeNonFoodBusinessChoicesDuringAnOrder(bool openQuote)
    {
        var (state, buyer, _, shopId) = CreateShopState();
        if (openQuote)
        {
            using var offering = PrivateWorldRuntime.Restore(state, id => id == buyer
                ? new ShopProvider("business_shop:") : new ShopProvider("safe_idle"));
            for (var tick = 0; tick < 12 && offering.BusinessTrades.Count == 0; tick++)
                Assert.True((await offering.AdvanceOneTickAsync()).Advanced);
            Assert.Single(offering.BusinessTrades);
            state = offering.ExportState();
        }
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == buyer
                ? person with { HungerBasisPoints = 1_500, LastDecisionContext = null } : person).ToArray(),
        };
        var candidate = openQuote ? "business_continue:" + Assert.Single(state.BusinessTrades!).OfferId : "business_shop:" + shopId;
        var ordinary = new ShopProvider(candidate);
        using (var control = PrivateWorldRuntime.Restore(state, id => id == buyer ? ordinary : new ShopProvider("safe_idle")))
        {
            Assert.True((await control.AdvanceOneTickAsync()).Advanced);
            Assert.Contains(candidate, ordinary.Seen);
        }
        var ordered = new ShopProvider(candidate);
        using var world = PrivateWorldRuntime.Restore(state, id => id == buyer ? ordered : new ShopProvider("safe_idle"));
        var receipt = world.SubmitInstruction(new("nonfood-survival-order", "owner:test", buyer,
            OwnerInstructionKind.MustDo, "keep gathering wood"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(candidate, ordered.Seen);
        Assert.Equal(openQuote ? 1 : 0, world.BusinessTrades.Count);
        Assert.DoesNotContain(receipt.InstructionId, world.ExportState().CompletedInstructionIds!);
        world.Validate();
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }
}
