using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    private static readonly Lazy<Task<byte[]>> BirthBeforeDueTick = new(CreateBirthBeforeDueTick);

    [Theory]
    [InlineData("removed")]
    [InlineData("reassigned")]
    [InlineData("kept")]
    public async Task PreparedBirthContinuesAfterShelterLossAcrossReload(string shelterChange)
    {
        using var preparing = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(await BirthBeforeDueTick.Value), _ => new ParentProvider("safe_idle"));
        var parent = preparing.Inhabitants.Single(person => person.Parenthood is { Stage: "preparing" }).InhabitantId;
        var plan = preparing.Inhabitants.Single(person => person.InhabitantId == parent).Parenthood!;
        var home = preparing.Society.GetInhabitant(plan.PrimaryCaregiverId!).HouseholdId!;
        var shelter = preparing.WorldSimulation.Buildings.Single(building => building.InstanceId == "family-house");
        if (shelterChange == "removed")
        {
            var result = preparing.RemoveBuilding(shelter.InstanceId, shelter.TownId, shelter.HouseholdId);
            Assert.True(result.Applied, result.Failure);
        }
        else if (shelterChange == "reassigned")
        {
            var result = preparing.ReassignBuilding(shelter.InstanceId, shelter.TownId, shelter.HouseholdId, null, "other-household");
            Assert.True(result.Applied, result.Failure);
        }
        var bytes = PrivateWorldRuntimeCodec.Encode(preparing.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ParentProvider("safe_idle"));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ParentProvider("safe_idle"));
        Assert.False((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(restored.Society.Births);
        restored.Resume();
        replay.Resume();
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var birth = Assert.Single(restored.Society.Births);
        Assert.Equal(home, birth.HouseholdId);
        Assert.Equal(plan.PrimaryCaregiverId, birth.PrimaryCaregiverId);
        Assert.Equal("completed", restored.Inhabitants.Single(person => person.InhabitantId == parent).Parenthood!.Stage);
        var child = restored.Inhabitants.Single(person => person.InhabitantId == birth.ChildId);
        Assert.True(restored.ExportState().Map.IsBuildable(child.Position));
        Assert.DoesNotContain(restored.Inhabitants, person => person.InhabitantId != birth.ChildId && person.Position == child.Position);
        if (shelterChange != "kept")
        {
            var caregiver = restored.Inhabitants.Single(person => person.InhabitantId == plan.PrimaryCaregiverId);
            Assert.InRange(restored.ExportState().Map.FootDistance(caregiver.Position, child.Position), 0, 1);
            Assert.NotNull(child.Housing?.Blocker);
            Assert.Contains(restored.ExportState().Events, item => item.Kind == "housing_blocked" &&
                item.Detail == $"{birth.ChildId}:{child.Housing.Blocker}");
        }
        using var savedBirth = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(restored.ExportState())), _ => new ParentProvider("safe_idle"));
        Assert.Equal(birth, Assert.Single(savedBirth.Society.Births));
        Assert.Equal(child.Housing, savedBirth.Inhabitants.Single(person => person.InhabitantId == birth.ChildId).Housing);
        savedBirth.Resume();
        Assert.True((await savedBirth.AdvanceOneTickAsync()).Advanced);
        Assert.Single(savedBirth.Society.Births);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparedBirthStillWaitsForFoodWithOrWithoutAHouse(bool removeHouse)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await BirthBeforeDueTick.Value);
        var parent = state.Inhabitants.Single(person => person.Parenthood is { Stage: "preparing" });
        var home = state.Society.Society.GetInhabitant(parent.Parenthood!.PrimaryCaregiverId!).HouseholdId;
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != home || lot.ItemKind != "food").ToArray(),
                    },
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new ParentProvider("safe_idle"));
        if (removeHouse)
        {
            var house = world.WorldSimulation.Buildings.Single(building => building.InstanceId == "family-house");
            Assert.True(world.RemoveBuilding(house.InstanceId, house.TownId, house.HouseholdId).Applied);
        }
        world.Resume();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(world.Society.Births);
        Assert.Equal("preparing", world.Inhabitants.Single(person => person.InhabitantId == parent.InhabitantId).Parenthood!.Stage);
    }

    private static async Task<byte[]> CreateBirthBeforeDueTick()
    {
        var state = await PreparedState();
        var parent = state.Inhabitants[0].InhabitantId;
        var other = state.Inhabitants[2].InhabitantId;
        using (var society = SocietyWorldRuntime.Restore(state.Society))
        {
            var membership = society.Checkpoint.Relationships.Single(item => item.Type == SocietyRelationshipType.HouseholdMembership &&
                item.TargetId == other && item.State == SocietyRelationshipState.Accepted);
            society.Apply(checkpoint => SocietyFixture.RevokeRelationship(checkpoint, membership.Id, other));
            society.Apply(checkpoint => SocietyFixture.CreateHousehold(checkpoint, "other-household", "Other household", [other]));
            state = state with { Society = society.ExportState() };
        }
        using var setup = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("safe_idle"));
        var shelter = setup.WorldSimulation.Buildings.Single(building => building.InstanceId == "family-shelter");
        Assert.True(setup.RemoveBuilding(shelter.InstanceId, shelter.TownId, shelter.HouseholdId).Applied);
        var home = setup.Society.GetInhabitant(parent).HouseholdId!;
        var placed = setup.PlaceBuilding("family-house", HouseContent.House1x1().CanonicalId, shelter.Position, home);
        Assert.True(placed.Applied, placed.Failure);
        using var preparing = PrivateWorldRuntime.Restore(setup.ExportState(),
            actor => new ParentProvider(actor == parent ? "parent_propose:" : "parent_accept:"));
        Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);
        Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);
        var plan = preparing.Inhabitants.Single(person => person.InhabitantId == parent).Parenthood!;
        Assert.Equal("preparing", plan.Stage);
        using var waiting = PrivateWorldRuntime.Restore(preparing.ExportState(), _ => new ParentProvider("safe_idle"));
        while (waiting.WorldTick < plan.LastTransitionTick + 599)
            Assert.True((await waiting.AdvanceOneTickAsync()).Advanced);
        Assert.Empty(waiting.Society.Births);
        waiting.Pause();
        return PrivateWorldRuntimeCodec.Encode(waiting.ExportState());
    }
}
