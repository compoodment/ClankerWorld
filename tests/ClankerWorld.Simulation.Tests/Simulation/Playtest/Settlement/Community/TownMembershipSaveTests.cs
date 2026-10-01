using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    [Fact]
    public void AdultArrivalsUpdateTheCollectiveAndInitialElectionBeforeImmediateSave()
    {
        using var world = PrivateWorldRuntime.Restore(WithHall(Initial()), _ => new CivicChooser(false, false));
        var house = world.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        for (var index = 0; index < 4; index++)
        {
            var id = ExtraAdultId(index);
            Assert.Equal(Alpha, world.AddAgent(id, house.Position));
            var current = Assert.Single(world.TownCouncils);
            Assert.Equal(5 + index, current.MemberIds.Count);
            Assert.Contains(id, current.MemberIds);
            if (index == 3)
            {
                Assert.NotNull(current.Election);
                Assert.Equal(8, current.Election.Electorate.Count);
                Assert.Contains(id, current.Election.Electorate);
            }
            else Assert.Null(current.Election);
            using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
            Assert.Equal(current.MemberIds, loaded.TownCouncils.Single().MemberIds);
            Assert.Equal(current.Election?.Electorate, loaded.TownCouncils.Single().Election?.Electorate);
            loaded.Validate();
        }
    }

    [Fact]
    public void MultipleStartedTownsValidateTheirOwnResidentsBuildingsAndHistoricalBorders()
    {
        var state = AtHall(WithHall(AddAdults(Initial(), 4)));
        var first = state.Towns!.Single();
        var locals = first.ResidentIds.Take(4).ToArray();
        var visitors = first.ResidentIds.Skip(4).ToArray();
        const string other = "town:visitors";
        const string farm = "first-town-farmhouse";
        state = state with
        {
            Towns = [first with { ResidentIds = locals, AssignedBuildingIds = first.AssignedBuildingIds.Where(id => id != farm).ToArray() },
                first with { Id = other, Name = "Visitor Town", ResidentIds = visitors, AssignedBuildingIds = [farm] }],
            TownCouncils = [new(Town, "open", 0, locals, Laws: []), new(other, "open", 0, visitors, Laws: [])],
            WorldSimulation = state.WorldSimulation! with
            {
                Buildings = state.WorldSimulation.Buildings.Select(building =>
                building.InstanceId == farm ? building with { TownId = other } : building).ToArray()
            }
        };
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));
        using var world = PrivateWorldRuntime.Restore(saved);
        Assert.Equal(2, world.Towns.Count);
        Assert.Equal(other, world.WorldSimulation.Buildings.Single(building => building.InstanceId == farm).TownId);
        Assert.Equal(first.BorderTiles, world.Towns.Single(town => town.Id == Town).BorderTiles);
        Assert.True(world.ProposeTownLaw(locals[0], Town, "protected_grove", "Ask before cutting trees.").Applied);
        Assert.False(world.VoteTownLaw(visitors[0], Town, true).Applied);
        Assert.Equal(4, world.TownCouncils.Single(council => council.TownId == Town).MemberIds.Count);
        Assert.Equal(4, world.TownCouncils.Single(council => council.TownId == other).MemberIds.Count);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with
        { Towns = saved.Towns!.Select(town => town.Id == other ? town with { ResidentIds = town.ResidentIds.Append(locals[0]).ToArray() } : town).ToArray() }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with
        {
            WorldSimulation = saved.WorldSimulation! with
            {
                Buildings = saved.WorldSimulation.Buildings.Select(building =>
            building.InstanceId == farm ? building with { TownId = "town:missing" } : building).ToArray()
            }
        }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(saved with
        { Towns = saved.Towns!.Select(town => town.Id == other ? town with { AssignedBuildingIds = [] } : town).ToArray() }));
        world.Validate();
    }

    [Fact]
    public void FounderSetupStillRequiresOnlyTheOriginalFirstTown()
    {
        var geography = new GeographyOptions("starter-layout-accept", WorldSizePreset.Small);
        using var world = new PrivateWorldRuntime(geography.Seed, startPace: WorldStartPace.FounderSetup, geographyOptions: geography);
        world.InitializeFirstTownContent();
        world.AcceptFirstTownLayout(world.ExportState().Map.Resources.Single(resource => resource.Id == "berry-patch").Position);
        var state = world.ExportState();
        var first = Assert.Single(state.Towns!);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        { Towns = [first, first with { Id = "town:premature", Name = "Premature Town", AssignedBuildingIds = [], ResidentIds = [] }] }));
        PrivateWorldRuntimeCodec.Encode(state);
    }
}
