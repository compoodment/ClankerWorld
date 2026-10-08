using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PortBoatRuntimeTests
{
    [Fact]
    public async Task OwnerBoatOrderUsesPaidBoatAndFinishesOnlyAtItsNamedPortAcrossReload()
    {
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), new());
        var boat = Assert.Single(scenario.World.Boats);
        var destination = scenario.World.WorldSimulation.Buildings.Single(port =>
            port.InstanceId != boat.DockedPortId && PortNavigationRules.IsPort(
                scenario.World.WorldContent.Buildings.Single(definition => definition.CanonicalId == port.DefinitionId)));
        var receipt = SubmitBoatOrder(scenario.World, "owner-voyage", destination.InstanceId);
        Assert.Equal("travel_by_boat", BoatOrder(scenario.World, receipt).Action);
        await scenario.UntilAsync(() => scenario.World.Boats[0].Journey is not null, 120);
        Assert.Equal(0, BoatOrder(scenario.World, receipt).CompletedUnits);
        Assert.Equal(destination.InstanceId, scenario.World.Boats[0].Journey!.DestinationPortId);
        var cargo = scenario.World.Society.Inventory.GetLot("travel-jug");
        var bytes = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        Assert.False((await scenario.World.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
        using var replay = new BoatScenario(PrivateWorldRuntimeCodec.Decode(bytes), new());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
        for (var step = 0; BoatOrder(scenario.World, receipt).Status != "finished" && step < 160; step++)
        {
            Assert.True((await scenario.World.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.World.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()),
                PrivateWorldRuntimeCodec.Encode(replay.World.ExportState()));
            if (scenario.World.Boats[0].Journey is not null)
                Assert.Equal(0, BoatOrder(scenario.World, receipt).CompletedUnits);
        }
        Assert.Equal("finished", BoatOrder(scenario.World, receipt).Status);
        Assert.Equal(1, BoatOrder(scenario.World, receipt).CompletedUnits);
        Assert.Equal(destination.InstanceId, Assert.Single(scenario.World.Boats).DockedPortId);
        Assert.Equal("arrived", Assert.Single(scenario.World.BoatRequests).Status);
        Assert.Equal(cargo, scenario.World.Society.Inventory.GetLot(cargo.Id));
        Assert.Single(scenario.World.ExportState().Events, item => item.Kind == "instruction_order_finished");
        scenario.World.Validate();
        replay.World.Validate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingOrReplacingAWaitingBoatOrderReleasesItsRequest(bool replace)
    {
        using var scenario = new BoatScenario(PrivateWorldRuntimeCodec.Decode(await PaidBoat.Value), new());
        var boat = Assert.Single(scenario.World.Boats);
        var destination = scenario.World.Towns[0].Projects.Single(project =>
            project.CompletedBuildingId is not null && project.CompletedBuildingId != boat.DockedPortId).CompletedBuildingId!;
        AddLandingBlockers(scenario, destination);
        var receipt = SubmitBoatOrder(scenario.World, "wait-before-cancel", destination);
        Assert.Equal("travel_by_boat", BoatOrder(scenario.World, receipt).Action);
        await scenario.UntilAsync(() => scenario.World.BoatRequests.Any(request => request.Status == "waiting"), 120);
        Assert.Null(Assert.Single(scenario.World.BoatRequests).BoatId);
        if (replace)
        {
            var person = scenario.World.Inhabitants.Single(person => person.InhabitantId == BoatPolicy.Author);
            scenario.World.SubmitInstruction(new("replace-voyage", "owner:test", BoatPolicy.Author,
                OwnerInstructionKind.MustDo, FormattableString.Invariant($"move to ({person.Position.X}, {person.Position.Y})")));
        }
        else
            scenario.World.CancelOrder(new("cancel-voyage", "owner:test", scenario.World.Society.WorldId,
                BoatPolicy.Author, receipt.InstructionId));
        Assert.Equal("cancelled", BoatOrder(scenario.World, receipt).Status);
        Assert.Equal("cancelled", Assert.Single(scenario.World.BoatRequests).Status);
        Assert.Equal(0, BoatOrder(scenario.World, receipt).CompletedUnits);
        Assert.Equal(boat, Assert.Single(scenario.World.Boats));
        using var restored = new BoatScenario(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState())), new());
        Assert.True((await restored.World.AdvanceOneTickAsync()).Advanced);
        Assert.Null(Assert.Single(restored.World.Boats).Journey);
        restored.World.Validate();
    }

    private static OwnerInstructionReceipt SubmitBoatOrder(PrivateWorldRuntime world, string key, string destinationId)
    {
        var destination = world.WorldSimulation.Buildings.Single(port => port.InstanceId == destinationId);
        return world.SubmitInstruction(new(key, "owner:test", BoatPolicy.Author, OwnerInstructionKind.MustDo,
            FormattableString.Invariant($"Travel by boat to Port at ({destination.Position.X}, {destination.Position.Y})")));
    }

    private static OwnerInstructionOrder BoatOrder(PrivateWorldRuntime world, OwnerInstructionReceipt receipt) =>
        world.ExportState().Instructions!.Single(instruction => instruction.InstructionId == receipt.InstructionId).Order!;
}
