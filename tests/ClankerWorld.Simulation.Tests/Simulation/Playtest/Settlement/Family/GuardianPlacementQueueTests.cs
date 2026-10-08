using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class GuardianPlacementQueueTests
{
    [Theory]
    [InlineData("blocked")]
    [InlineData("opened")]
    [InlineData("second-only")]
    [InlineData("opened-during-escort")]
    public async Task AnUnreachableCollectionDoesNotPreventAnotherAcceptedChildMovingHomeAcrossReplay(string mode)
    {
        var scenario = GuardianPlacementTestFixture.Prepared(orphanCount: 2, residentChildren: 0);
        var state = scenario.State;
        var first = scenario.Children[0];
        var second = scenario.Children[1];
        var guardian = scenario.Guardians[0];
        var home = scenario.House.Position;
        var occupied = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var distant = state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => state.Map.FootDistance(home, point) is >= 5 and <= 9)
            .OrderBy(point => state.Map.FootDistance(home, point)).ThenBy(point => point.Y).ThenBy(point => point.X)
            .First(point => state.Map.IsBuildable(point) && !occupied.Contains(point) &&
                state.Map.IsReachableOnFoot(home, point) && state.Map.FootNeighbors(point).Count() == 8 &&
                state.Map.FootNeighbors(point).All(neighbor => state.Map.IsBuildable(neighbor) && !occupied.Contains(neighbor)));
        var ring = state.Map.FootNeighbors(distant)
            .OrderBy(point => state.Map.IsDiagonalFootStep(distant, point) ? 1 : 0)
            .ThenBy(point => point.Y).ThenBy(point => point.X).ToArray();
        var nearby = state.Map.FootNeighbors(home).First(point => state.Map.IsBuildable(point) &&
            !occupied.Contains(point) && !ring.Contains(point) && point != distant);
        if (mode == "opened-during-escort")
            nearby = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
                !occupied.Contains(point) && state.Map.FootDistance(home, point) == 3 &&
                state.Map.FootDistance(distant, point) >= 6 && state.Map.IsReachableOnFoot(home, point));
        var day = state.WorldSystems!.Config.TicksPerDay;
        var until = state.Society.Society.WorldTick + day;
        var blockers = ring.Select((point, index) => new AnimalState("guardian-route-blocker:" + index, "Blocker" + index,
            "horse", "male", -7L * day, point, "guardian-route-blockers", CareUntilTick: until,
            WildFedUntilTick: until, WildWaterUntilTick: until)).ToArray();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == first ? distant : person.InhabitantId == second ? nearby : home,
                HungerBasisPoints = 9_500,
                Survival = new(),
                Project = null,
                LastDecisionContext = null,
                TravelCooldownTicks = 0,
                MoveWaitTicks = 0,
            }).ToArray(),
            AnimalWorld = new(true, mode == "opened" ? blockers.Skip(1).ToArray() : blockers, []),
        };
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);
        var choices = new GuardianPlacementChoices();
        using var accepting = GuardianPlacementTestFixture.Restore(state, choices);
        await GuardianPlacementTestFixture.Step(accepting);
        Assert.All(scenario.Children, child => Assert.NotNull(GuardianPlacementTestFixture.Person(accepting, child).GuardianSearch));
        choices.Set(guardian, "guardian_accept:", "guardian_relocate:");
        if (mode != "second-only") accepting.SubmitInstruction(new("accept-blocked-child", "owner:test", guardian,
            OwnerInstructionKind.MustDo, "become guardian for " + first));
        var receipt = accepting.SubmitInstruction(new("accept-nearby-child", "owner:test", guardian,
            OwnerInstructionKind.MustDo, "become guardian for " + second, Queue: mode != "second-only"));
        await GuardianPlacementTestFixture.Until(accepting, () => accepting.ExportState().Instructions!
            .Single(item => item.InstructionId == receipt.InstructionId).Order!.Status == "finished", maximumTicks: 12);
        Assert.Equal(guardian, accepting.Society.GetInhabitant(second).PrimaryCaregiverId);
        if (mode != "second-only") Assert.Equal(guardian, accepting.Society.GetInhabitant(first).PrimaryCaregiverId);
        Assert.All(accepting.ExportState().Instructions!, item => Assert.Equal("finished", item.Order!.Status));
        choices.Set(guardian, "guardian_relocate:");
        using var world = GuardianPlacementTestFixture.Reload(accepting, choices.Copy());
        using var replay = GuardianPlacementTestFixture.Reload(world, choices.Copy());
        var initial = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(initial, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        if (mode == "opened-during-escort")
        {
            await GuardianPlacementTestFixture.Until(world,
                () => GuardianPlacementTestFixture.Person(world, second).GuardianPlacement?.Stage == "escorting",
                maximumTicks: 20, replay: replay);
            var released = ReleaseFirstApproach(world);
            using var resumed = GuardianPlacementTestFixture.Restore(released, choices.Copy());
            using var resumedReplay = GuardianPlacementTestFixture.Reload(resumed, choices.Copy());
            for (var tick = 0; tick < 20 && !GuardianPlacementTestFixture.Placed(resumed, scenario, second); tick++)
            {
                var previousEvents = resumed.ExportState().Events.Count;
                await GuardianPlacementTestFixture.Step(resumed);
                await GuardianPlacementTestFixture.Step(resumedReplay);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(resumed.ExportState()), PrivateWorldRuntimeCodec.Encode(resumedReplay.ExportState()));
                Assert.DoesNotContain(resumed.ExportState().Events.Skip(previousEvents), item => item.Kind == "inhabitant_moved" &&
                    item.Detail.StartsWith(guardian + ":", StringComparison.Ordinal) &&
                    item.Detail.EndsWith(":collect_guardian_child", StringComparison.Ordinal));
                resumed.Validate();
            }
            Assert.True(GuardianPlacementTestFixture.Placed(resumed, scenario, second));
            await GuardianPlacementTestFixture.Until(resumed, () => GuardianPlacementTestFixture.Placed(resumed, scenario, first),
                maximumTicks: 100, replay: resumedReplay);
            Assert.True(GuardianPlacementTestFixture.Placed(resumed, scenario, second));
            using var finalReload = GuardianPlacementTestFixture.Reload(resumed, choices.Copy());
            await GuardianPlacementTestFixture.Step(resumed);
            await GuardianPlacementTestFixture.Step(finalReload);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(resumed.ExportState()), PrivateWorldRuntimeCodec.Encode(finalReload.ExportState()));
            return;
        }
        for (var tick = 0; tick < 30; tick++)
        {
            await GuardianPlacementTestFixture.Step(world);
            await GuardianPlacementTestFixture.Step(replay);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            world.Validate();
            Assert.All(world.ExportState().AnimalWorld.Animals, animal =>
                Assert.Equal(blockers.Single(blocker => blocker.Id == animal.Id).Position, animal.Position));
        }
        Assert.True(GuardianPlacementTestFixture.Placed(world, scenario, second), "The reachable accepted child should move home.");
        Assert.Equal(guardian, world.Society.GetInhabitant(second).PrimaryCaregiverId);
        if (mode == "opened") Assert.True(GuardianPlacementTestFixture.Placed(world, scenario, first));
        if (mode == "blocked")
        {
            Assert.False(GuardianPlacementTestFixture.Placed(world, scenario, first));
            var pending = Assert.IsType<SettlementGuardianPlacement>(GuardianPlacementTestFixture.Person(world, first).GuardianPlacement);
            Assert.Contains("clear walking route", pending.Blocker!, StringComparison.Ordinal);
            Assert.Equal(guardian, world.Society.GetInhabitant(first).PrimaryCaregiverId);
            var released = ReleaseFirstApproach(world);
            using var resumed = GuardianPlacementTestFixture.Restore(released, choices.Copy());
            using var resumedReplay = GuardianPlacementTestFixture.Reload(resumed, choices.Copy());
            await GuardianPlacementTestFixture.Until(resumed, () => GuardianPlacementTestFixture.Placed(resumed, scenario, first),
                maximumTicks: 100, replay: resumedReplay);
            Assert.True(GuardianPlacementTestFixture.Placed(resumed, scenario, second));
            Assert.Equal(guardian, resumed.Society.GetInhabitant(first).PrimaryCaregiverId);
            resumed.Validate();
        }
        PrivateWorldRuntimeState ReleaseFirstApproach(PrivateWorldRuntime runtime)
        {
            var current = runtime.ExportState();
            return current with
            {
                AnimalWorld = current.AnimalWorld with
                { Animals = current.AnimalWorld.Animals.Where(animal => animal.Id != blockers[0].Id).ToArray() },
            };
        }
        using var loaded = GuardianPlacementTestFixture.Reload(world, choices.Copy());
        await GuardianPlacementTestFixture.Step(world);
        await GuardianPlacementTestFixture.Step(loaded);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(loaded.ExportState()));
    }
}
