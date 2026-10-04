using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class HouseholdLandGrantTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TillOrdersAvoidAnotherHouseholdsRequestedLand(bool exactTile)
    {
        using var source = Create("household-grant", new Choices());
        var (farmer, household, point) = FarmerAndUnclaimedTitledPlot(source.ExportState());
        var applicant = source.Society.Inhabitants.First(person => person.HouseholdId != household).Id;
        var filing = source.RequestHouseholdLandUse("ordered-field-boundary", applicant, source.Towns[0].Id, [point]);
        Assert.True(filing.Applied, filing.Failure);
        using var world = PrivateWorldRuntime.Restore(EquipFarmerForLandTest(source.ExportState(), farmer, point),
            _ => new BoundaryFieldOrderProvider());
        var receipt = world.SubmitInstruction(new("protected-field-order", "owner:test", farmer,
            OwnerInstructionKind.MustDo, exactTile ? $"till a field at ({point.X}, {point.Y})" : "till a field"));
        for (var tick = 0; tick < 16; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var current = world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
            if (current.Status is "finished" or "blocked") break;
        }
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.DoesNotContain(world.Fields, field => field.Position == point);
        if (exactTile)
        {
            Assert.Equal("blocked", order.Status);
            Assert.Equal(0, order.CompletedUnits);
            Assert.Empty(world.Fields);
            Assert.Equal(10_000, world.Society.Inventory.GetLot("land-boundary-carried-hoe").ConditionBasisPoints);
        }
        else
        {
            Assert.Equal("finished", order.Status);
            Assert.Equal(1, order.CompletedUnits);
            var field = Assert.Single(world.Fields);
            Assert.Equal(household, field.HouseholdId);
            Assert.Equal(FarmFieldStage.Prepared, field.Stage);
            Assert.Null(field.Work);
            Assert.True(world.Society.Inventory.GetLot("land-boundary-carried-hoe").ConditionBasisPoints < 10_000);
        }
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new BoundaryFieldOrderProvider());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private sealed class BoundaryFieldOrderProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var observation = request.Observation;
            var selected = observation.OperativeOrderInstructionId is not null && observation.Candidates.Any(candidate => candidate.Id == "work_field")
                ? "work_field" : "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind,
                request.ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                selected, 1, new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}
