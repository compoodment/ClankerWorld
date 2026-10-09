using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class HoeUpgradePlantingClaimTests
{
    // The occupied-field check uses a wooden hoe and stops at the selected
    // intention. A supported upgrade must still protect the chosen crop until
    // actual planting, with another farmer trying to plant their real grain seed.
    // Losing the hoe must release the claim so the second farmer can plant.
    [Theory]
    [InlineData("original")]
    [InlineData("upgraded")]
    [InlineData("removed")]
    public async Task ChosenCropClaimRequiresAUsableCarriedHoe(string hoeChange)
    {
        var (state, actor, household, point) = FarmFieldTests.PreparedFarmer("field-claim-through-occupancy");
        state = FarmFieldTests.FeedFarmTownFromAvailableStock(state, household);
        var sibling = state.Society.Society.Inhabitants.First(person => person.HouseholdId == household &&
            person.AgeBand == SocietyAgeBand.Adult && person.Id != actor).Id;
        var origin = state.Inhabitants.Single(person => person.InhabitantId == sibling).Position;
        var occupied = state.Map.Resources.Select(resource => resource.Position)
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var away = state.Map.Tiles.Select(tile => tile.Position).First(position => position != point &&
            position != origin && state.Map.IsBuildable(position) && !occupied.Contains(position) &&
            !state.Inhabitants.Any(person => person.Position == position && person.InhabitantId != actor && person.InhabitantId != sibling) &&
            state.Map.IsReachableOnFoot(position, point));
        var greens = $"farm:Plant:{point.X}:{point.Y}:{FarmFieldRules.Greens}";
        var grain = $"farm:Plant:{point.X}:{point.Y}:{FarmFieldRules.Grain}";
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
            "upgrade-greens-seed", FarmFieldRules.GreensSeed, actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "upgrade-sibling-grain", FarmFieldRules.GrainSeed, sibling, 1);
        inventory = InventoryFixture.AddLot(inventory, "upgrade-sibling-hoe", FarmFieldRules.Hoe, sibling, 1);
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Fields = [new(point, household, FarmFieldStage.Prepared)],
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? origin : person.InhabitantId == sibling ? away : person.Position,
                HungerBasisPoints = 10_000,
                Project = null,
                LastDecisionContext = null,
            }).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with { Inventory = inventory },
                Cognition = state.Society.Cognition with
                {
                    Queue = [],
                    Runtimes = state.Society.Cognition.Runtimes.Select(runtime => runtime with { CurrentIntention = null }).ToArray(),
                },
            },
        };
        var contest = false;
        var policy = new MarketRulesPolicy
        {
            Choose = (id, candidates) => candidates.FirstOrDefault(candidate =>
                id == actor && candidate.Id == greens || contest && id == sibling && candidate.Id == grain) ??
                candidates.Single(candidate => candidate.Id == "safe_idle"),
        };
        using (var selecting = PrivateWorldRuntime.Restore(state, policy.CreateProvider))
        {
            Assert.True((await selecting.AdvanceOneTickAsync()).Advanced);
            state = selecting.ExportState();
            Assert.Equal(greens, state.Society.Cognition.Runtimes.Single(runtime => runtime.InhabitantId == actor).CurrentIntention?.CandidateId);
            Assert.Equal(1, state.Society.Society.Inventory.GetLot("upgrade-greens-seed").Quantity);
        }
        if (hoeChange != "original")
        {
            inventory = state.Society.Society.Inventory with
            {
                Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.Id != "carried-hoe").ToArray(),
            };
            if (hoeChange == "upgraded")
                inventory = InventoryFixture.AddLot(inventory, "upgrade-iron-hoe", "iron_hoe", actor, 1);
            state = FarmFieldTests.WithInventory(state, inventory);
        }
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == sibling
                ? person with { Position = point, LastDecisionContext = null } : person).ToArray(),
        };
        // Offers before the original planting intention was selected are not contested choices.
        policy.Offered.Clear();
        contest = true;
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var blocked = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        Assert.False((await blocked.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(blocked.ExportState()));
        for (var tick = 0; tick < 8; tick++)
        {
            Assert.True((await blocked.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(blocked.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        state = blocked.ExportState() with
        {
            Inhabitants = blocked.Inhabitants.Select(person => person.InhabitantId == sibling
                ? person with { Position = away, LastDecisionContext = null } : person).ToArray(),
        };
        bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var continuing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), policy.CreateProvider);
        for (var tick = 0; tick < 16; tick++)
        {
            Assert.True((await continuing.AdvanceOneTickAsync()).Advanced);
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(continuing.ExportState()), PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        }
        var field = continuing.Fields.Single(item => item.Position == point);
        var retainsClaim = hoeChange != "removed";
        Assert.Equal((retainsClaim ? FarmFieldRules.Greens : FarmFieldRules.Grain, FarmFieldStage.Growing), (field.Crop, field.Stage));
        if (retainsClaim)
        {
            Assert.DoesNotContain(policy.OfferedTo(sibling), candidate => candidate.Id == grain);
            Assert.DoesNotContain(continuing.Society.Inventory.Lots, lot => lot.Id == "upgrade-greens-seed");
            Assert.Equal(1, continuing.Society.Inventory.GetLot("upgrade-sibling-grain").Quantity);
        }
        else
        {
            Assert.Contains(policy.OfferedTo(sibling), candidate => candidate.Id == grain);
            Assert.Equal(1, continuing.Society.Inventory.GetLot("upgrade-greens-seed").Quantity);
            Assert.DoesNotContain(continuing.Society.Inventory.Lots, lot => lot.Id == "upgrade-sibling-grain");
        }
        bytes = PrivateWorldRuntimeCodec.Encode(continuing.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }
}
