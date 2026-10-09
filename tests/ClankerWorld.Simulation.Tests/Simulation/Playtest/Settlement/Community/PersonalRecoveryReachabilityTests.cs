using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class PersonalRecoveryReachabilityTests
{
    private const string Earlier = "a-audit-personal-stone";
    private const string Local = "z-audit-personal-stone";
    private static readonly GridPoint Disconnected = new(209, 1);

    [Theory]
    [InlineData("unreachable", true, false)]
    [InlineData("unreachable", false, false)]
    [InlineData("missing", true, false)]
    [InlineData("reachable", true, false)]
    [InlineData("missing", false, false)]
    [InlineData("reachable", false, false)]
    [InlineData("unreachable", true, true)]
    [InlineData("moved", false, false)]
    public async Task RoutineRecoverySkipsUnreachableBelongingsAndCollectsTheLocalLot(
        string earlier, bool builtin, bool explicitOrder)
    {
        using var setup = NormalPathWorld.CreateGenerated("personal-recovery-unreachable",
            _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == house.HouseholdId &&
            person.AgeBand == SocietyAgeBand.Adult).Id;
        Assert.True(state.Map.IsBuildable(Disconnected));
        Assert.False(state.Map.IsReachableFromCampOnFoot(Disconnected));
        Assert.True(state.Map.FootDistance(house.Position, Disconnected) > 16);
        var inventory = state.Society.Society.Inventory;
        foreach (var kind in new[] { "wooden_axe", "wooden_pickaxe", "wooden_hoe" })
            inventory = InventoryFixture.AddLot(inventory, "recovery-tool-" + kind, kind, actor, 1);
        if (earlier != "missing")
        {
            var position = earlier == "reachable" ? house.Position : Disconnected;
            if (earlier == "moved")
                position = Enumerable.Range(0, state.Map.Height).SelectMany(y => Enumerable.Range(0, state.Map.Width)
                    .Select(x => new GridPoint(x, y))).First(point => state.Map.IsBuildable(point) &&
                    state.Map.FootDistance(house.Position, point) == 4 && state.Map.IsReachableFromCampOnFoot(point));
            inventory = InventoryFixture.AddLot(inventory, Earlier, "stone", actor, 1,
                groundPosition: new(position.X, position.Y));
        }
        inventory = InventoryFixture.AddLot(inventory, Local, "stone", actor, 1,
            groundPosition: new(house.Position.X, house.Position.Y));
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = house.Position,
                    HungerBasisPoints = 10_000,
                    Project = null,
                    LastDecisionContext = null,
                    Survival = new SurvivalCondition()
                } : person).ToArray(),
        };
        var chooser = new RecoveryChooser(builtin);
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using var world = Restore(state, actor, chooser);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        if (earlier == "moved")
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Contains("household_collect:" + Earlier, chooser.Offered);
            Assert.NotEqual(house.Position, world.ExportState().Inhabitants.Single(person => person.InhabitantId == actor).Position);
            Assert.False(PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot(Earlier), actor));
            state = FarmFieldTests.WithInventory(world.ExportState(), InventoryFixture.Relocate(world.Society.Inventory,
                "recovery-source-moved", Earlier, actor, 1, groundPosition: new(Disconnected.X, Disconnected.Y)));
            var resumedChooser = new RecoveryChooser(false);
            using var resumed = Restore(state, actor, resumedChooser);
            using var replay = Restore(state, actor, new RecoveryChooser(false));
            for (var tick = 0; tick < 8 && !PersonalEquipmentRules.IsCarried(resumed.Society.Inventory.GetLot(Local), actor); tick++)
            {
                Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(resumed.ExportState()),
                    PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
            Assert.True(PersonalEquipmentRules.IsCarried(resumed.Society.Inventory.GetLot(Local), actor));
            Assert.DoesNotContain("household_collect:" + Earlier, resumedChooser.Offered);
            var moved = resumed.Society.Inventory.GetLot(Earlier);
            Assert.Equal((actor, 1, new InventoryGroundPosition(Disconnected.X, Disconnected.Y)),
                (moved.OwnerId, moved.Quantity, moved.GroundPosition));
            resumed.Validate();
            return;
        }
        OwnerInstructionReceipt? receipt = explicitOrder ? world.SubmitInstruction(new("reachable-personal-stone", "owner:test",
            actor, OwnerInstructionKind.MustDo, "collect one stone")) : null;
        for (var tick = 0; tick < 8 && !PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot(Local), actor); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var local = world.Society.Inventory.GetLot(Local);
        Assert.True(PersonalEquipmentRules.IsCarried(local, actor), string.Join(", ", chooser.Offered
            .Where(id => id.StartsWith("household_collect:", StringComparison.Ordinal)).Distinct()));
        Assert.Equal((actor, 1), (local.OwnerId, local.Quantity));
        if (earlier == "unreachable")
        {
            var remote = world.Society.Inventory.GetLot(Earlier);
            Assert.Equal((actor, 1, new InventoryGroundPosition(Disconnected.X, Disconnected.Y)),
                (remote.OwnerId, remote.Quantity, remote.GroundPosition));
            Assert.DoesNotContain("household_collect:" + Earlier, chooser.Offered);
        }
        if (earlier == "reachable" && !explicitOrder)
            Assert.True(PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot(Earlier), actor));
        if (receipt is not null)
        {
            var order = world.ExportState().Instructions!.Single(item => item.InstructionId == receipt.InstructionId).Order!;
            Assert.Equal(("finished", 1), (order.Status, order.CompletedUnits));
        }
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = Restore(PrivateWorldRuntimeCodec.Decode(saved), actor, new RecoveryChooser(builtin));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Validate();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
            PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, RecoveryChooser chooser) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? chooser : new ActionCoverageRecorder(chooseIdle: true));

    private sealed class RecoveryChooser(bool builtin) : IDecisionProvider
    {
        private readonly DeterministicDecisionProvider provider = new();
        public List<string> Offered { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            Offered.AddRange(request.Observation.Candidates.Select(candidate => candidate.Id));
            if (!builtin)
            {
                var selected = request.Observation.Candidates.Where(candidate => candidate.Id.StartsWith("household_collect:", StringComparison.Ordinal))
                    .OrderBy(candidate => candidate.Id, StringComparer.Ordinal).FirstOrDefault() ??
                    request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
                request = request with { Observation = request.Observation with { Candidates = [selected] } };
            }
            return provider.DecideAsync(request, cancellationToken);
        }
    }
}
