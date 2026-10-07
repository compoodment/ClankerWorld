using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    // The expensive birth/death setup is shared as encoded bytes. Every test
    // decodes its own independent checkpoint and keeps all live work local.
    private static readonly Lazy<Task<byte[]>> GuardianOrderFixture = new(async () =>
    {
        using var world = PrivateWorldRuntime.Restore(await OrphanState(olderChild: true), _ => new ParentProvider("safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    private static async Task<PrivateWorldRuntimeState> GuardianOrderState() =>
        PrivateWorldRuntimeCodec.Decode(await GuardianOrderFixture.Value);

    private static (string Child, string Adult) GuardianOrderPeople(PrivateWorldRuntimeState state)
    {
        var child = state.Society.Society.Births.Single().ChildId;
        var search = state.Inhabitants.Single(person => person.InhabitantId == child).GuardianSearch!;
        return (child, search.OfferedAdultIds[0]);
    }

    private static OwnerInstructionRequest GuardianRequest(string key, string adult, string child, bool queue = false) =>
        new(key, "owner:test", adult, OwnerInstructionKind.MustDo, $"Become guardian for {child}", queue);

    [Theory]
    [InlineData("Élodie", "Élodie", false)]
    [InlineData("Élodie", "E\u0301lodie", false)]
    [InlineData("E\u0301lodie", "Élodie", false)]
    [InlineData("Élodie", "éLODIE", false)]
    [InlineData("Élodie", "Élodie", true)]
    public async Task GuardianOrderKeepsTheNamedChildAcrossRenameSaveAndReplayAndCompletesOnce(string storedFirstName, string firstName, bool useId)
    {
        var initial = await GuardianOrderState();
        var (child, adult) = GuardianOrderPeople(initial);
        using var named = SocietyWorldRuntime.Restore(initial.Society);
        var surname = InhabitantNameRules.SurnameKey(named.Checkpoint.GetInhabitant(child).Name);
        named.Apply(checkpoint => SocietyFixture.RenameInhabitant(checkpoint, child, storedFirstName + " " + surname));
        using var setup = PrivateWorldRuntime.Restore(initial with { Society = named.ExportState() }, _ => new ParentProvider("safe_idle"));
        var request = GuardianRequest("guardian-order", adult, useId ? child : firstName + " " + surname);
        var receipt = setup.SubmitInstruction(request);
        Assert.Equal(receipt, setup.SubmitInstruction(request));
        var saved = setup.ExportState();
        Assert.Equal(child, Assert.Single(saved.Instructions!).Order!.TargetAgentId);
        using var renamed = SocietyWorldRuntime.Restore(saved.Society);
        renamed.Apply(checkpoint => SocietyFixture.RenameInhabitant(checkpoint, child,
            "Renamed " + InhabitantNameRules.SurnameKey(checkpoint.GetInhabitant(child).Name)));
        using var world = PrivateWorldRuntime.Restore(saved with { Society = renamed.ExportState() }, _ => new ParentProvider("safe_idle"));
        world.Pause();
        var directory = Directory.CreateTempSubdirectory("clankerworld-guardian-order-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), _ => new ParentProvider("safe_idle"));
            file.Save(world);
            using var restored = file.LoadOrCreate(initial.WorldSeed);
            Assert.Equal(File.ReadAllBytes(file.Path), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            Assert.False((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
            world.Resume();
            restored.Resume();
            for (var tick = 0; tick < 3; tick++)
            {
                Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
                Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
            }
            Assert.Equal(adult, restored.Society.GetInhabitant(child).PrimaryCaregiverId);
            var state = restored.ExportState();
            var instruction = Assert.Single(state.Instructions!);
            Assert.Equal(request.Text, instruction.Text);
            Assert.Equal("finished", instruction.Order!.Status);
            Assert.Equal(1, instruction.Order.CompletedUnits);
            Assert.Equal(child, instruction.Order.TargetAgentId);
            Assert.Contains(receipt.InstructionId, state.CompletedInstructionIds!);
            Assert.Single(state.Events, item => item.Kind == "guardian_assigned" && item.Detail == child);
            Assert.Single(state.Events, item => item.Kind == "instruction_order_finished");
            var projected = Assert.Single(new OwnerWorldObservationStore(restored).GetSnapshot().Instructions).Order!;
            Assert.Equal(child, projected.TargetAgentId);
            Assert.Equal("finished", projected.Status);
            restored.Validate();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedGuardianOrderCannotAppointAnyoneAfterCancellationOrReplacement(bool replace)
    {
        var initial = await GuardianOrderState();
        var (child, adult) = GuardianOrderPeople(initial);
        using var world = PrivateWorldRuntime.Restore(initial, _ => new ParentProvider("safe_idle"));
        world.SubmitInstruction(new("food-first", "owner:test", adult, OwnerInstructionKind.MustDo, "keep eating berries"));
        var receipt = world.SubmitInstruction(GuardianRequest("queued-guardian", adult, child, queue: true));
        Assert.Equal("queued", world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!.Status);
        var cancel = new OwnerOrderCancelRequest("cancel-guardian", "owner:test", world.Society.WorldId, adult, receipt.InstructionId);
        if (replace)
            world.SubmitInstruction(new("replacement", "owner:test", adult, OwnerInstructionKind.MustDo, "eat berries"));
        else
            Assert.True(world.CancelOrder(cancel).Changed);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            _ => new ParentProvider("safe_idle"));
        if (!replace) Assert.True(restored.CancelOrder(cancel).Changed);
        for (var tick = 0; tick < 3; tick++) Assert.True((await restored.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Null(restored.Society.GetInhabitant(child).PrimaryCaregiverId);
        var order = restored.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
        Assert.Equal("cancelled", order.Status);
        Assert.Equal(0, order.CompletedUnits);
        restored.Validate();
    }

    [Fact]
    public async Task AQueuedGuardianOrderCannotReplaceAnotherAdultsAcceptedCare()
    {
        var initial = await GuardianOrderState();
        var (child, adult) = GuardianOrderPeople(initial);
        var other = initial.Inhabitants.Single(person => person.InhabitantId == child).GuardianSearch!.OfferedAdultIds
            .First(id => id != adult);
        using var world = PrivateWorldRuntime.Restore(initial, _ => new ParentProvider("safe_idle"));
        var food = world.SubmitInstruction(new("food-first", "owner:test", adult, OwnerInstructionKind.MustDo, "keep eating berries"));
        var waiting = world.SubmitInstruction(GuardianRequest("waiting-guardian", adult, child, queue: true));
        world.SubmitInstruction(GuardianRequest("other-guardian", other, child));
        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(other, world.Society.GetInhabitant(child).PrimaryCaregiverId);
        world.CancelOrder(new("cancel-food", "owner:test", world.Society.WorldId, adult, food.InstructionId));
        for (var tick = 0; tick < 3; tick++) Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        Assert.Equal(other, world.Society.GetInhabitant(child).PrimaryCaregiverId);
        var order = world.ExportState().Instructions!.Single(item => item.InstructionId == waiting.InstructionId).Order!;
        Assert.Equal("blocked", order.Status);
        Assert.Equal(0, order.CompletedUnits);
        Assert.Contains("no longer", order.BlockedReason, StringComparison.Ordinal);
        Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_assigned" && item.Detail == child);
        world.Validate();
    }

    [Theory]
    [InlineData("Become guardian for unknown child")]
    [InlineData("Become guardian for {child} and gather food")]
    [InlineData("Become guardian for {child} until cancelled")]
    [InlineData("Do not become guardian for {child}")]
    [InlineData("Become guardian for {adult}")]
    [InlineData("Become guardian for {name} and gather food")]
    [InlineData("Become guardian for {name} until cancelled")]
    [InlineData("Become guardian for {firstName}")]
    public async Task UnsupportedGuardianOrdersPreserveTheCurrentTask(string text)
    {
        var initial = await GuardianOrderState();
        var (child, adult) = GuardianOrderPeople(initial);
        using var named = SocietyWorldRuntime.Restore(initial.Society);
        var childName = "Élodie " + InhabitantNameRules.SurnameKey(named.Checkpoint.GetInhabitant(child).Name);
        named.Apply(checkpoint => SocietyFixture.RenameInhabitant(checkpoint, child, childName));
        using var world = PrivateWorldRuntime.Restore(initial with { Society = named.ExportState() }, _ => new ParentProvider("safe_idle"));
        var current = world.SubmitInstruction(new("existing", "owner:test", adult, OwnerInstructionKind.MustDo, "gather berries"));
        var rejected = world.SubmitInstruction(new("rejected", "owner:test", adult, OwnerInstructionKind.MustDo,
            text.Replace("{child}", child, StringComparison.Ordinal).Replace("{adult}", adult, StringComparison.Ordinal)
                .Replace("{name}", childName, StringComparison.Ordinal).Replace("{firstName}", "E\u0301lodie", StringComparison.Ordinal)));
        var state = world.ExportState();
        Assert.Equal("waiting", state.Instructions!.Single(item => item.InstructionId == current.InstructionId).Order!.Status);
        Assert.Equal("not_understood", state.Instructions!.Single(item => item.InstructionId == rejected.InstructionId).Order!.Status);
        var self = world.SubmitInstruction(GuardianRequest("child-cannot-accept", child, child));
        Assert.Equal("not_understood", world.ExportState().Instructions!.Single(item => item.InstructionId == self.InstructionId).Order!.Status);
        world.Validate();
    }

    [Fact]
    public async Task SavedGuardianOrdersRejectMissingTargetsMixedTasksAndInventedProgress()
    {
        var initial = await GuardianOrderState();
        var (child, adult) = GuardianOrderPeople(initial);
        using var world = PrivateWorldRuntime.Restore(initial, _ => new ParentProvider("safe_idle"));
        world.SubmitInstruction(GuardianRequest("guardian-validation", adult, child));
        var state = world.ExportState();
        var instruction = Assert.Single(state.Instructions!);
        var order = instruction.Order!;
        foreach (var damaged in new[]
        {
            order with { TargetAgentId = null },
            order with { TargetAgentId = "missing-child" },
            order with { TargetAgentId = adult },
            order with { TargetFoodKind = "berries" },
            order with { TargetMaterialKind = "wood" },
            order with { RequestedUnits = 2 },
            order with { RepeatUntilCancelled = true },
            order with { CompletedUnits = 1 },
            order with { LastEffectId = "invented" },
        })
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
            {
                Instructions = [instruction with { Order = damaged }],
            }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with { SchemaVersion = 52 }));
    }
}
