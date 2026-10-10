using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Simulation.Tests;

public sealed class BlacksmithGatherReachabilityTests
{
    [Theory]
    [InlineData("wood", "unreachable", "routine")]
    [InlineData("wood", "unreachable", "supply")]
    [InlineData("wood", "missing", "routine")]
    [InlineData("wood", "missing", "supply")]
    [InlineData("wood", "reachable", "routine")]
    [InlineData("wood", "unreachable", "gather")]
    [InlineData("iron_ore", "unreachable", "routine")]
    [InlineData("iron_ore", "missing", "routine")]
    [InlineData("iron_ore", "reachable", "routine")]
    [InlineData("iron_ore", "unreachable", "supply")]
    [InlineData("iron_ore", "missing", "supply")]
    [InlineData("iron_ore", "unreachable", "gather")]
    public async Task UnreachableStockDoesNotSuppressNativeBlacksmithGathering(string kind, string stock, string orderKind)
    {
        using var generated = NormalPathWorld.CreateGenerated("cart-set-unfinished", _ => new ActionCoverageRecorder(chooseIdle: true));
        Assert.True((await generated.AdvanceOneTickAsync()).Advanced);
        var state = generated.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var position = kind == "wood" ? new GridPoint(111, 62) : new GridPoint(60, 50);
        var remote = kind == "wood" ? new GridPoint(238, 85) : state.Map.Tiles.Select(tile => tile.Position)
            .First(point => state.Map.IsBuildable(point) && !SwimmingRules.IsReachable(state.Map, position, point));
        Assert.True(state.Map.IsBuildable(remote));
        Assert.False(state.Map.IsReachableFromCampOnFoot(remote));
        Assert.False(SwimmingRules.IsReachable(state.Map, position, remote));
        Assert.True(state.Map.IsReachableFromCampOnFoot(position));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                (lot.OwnerId != smith.HouseholdId || lot.ItemKind != kind)).ToArray(),
        };
        foreach (var input in state.WorldContent!.Recipes.Where(recipe => recipe.WorkstationBuildingId == smith.DefinitionId &&
                     !recipe.Outputs.Any(output => output.ResourceId == InventoryContainerRules.Handcart))
                     .SelectMany(recipe => recipe.Inputs).GroupBy(input => input.ResourceId, StringComparer.Ordinal))
        {
            if (input.Key == kind) continue;
            inventory = InventoryFixture.AddLot(inventory, "smith-reserve-" + input.Key, input.Key, smith.HouseholdId!,
                input.Max(item => item.Amount) * 2, storageBuildingId: smith.InstanceId);
        }
        inventory = InventoryFixture.AddLot(inventory, "smith-gather-tool", kind == "wood" ? "wooden_axe" : "stone_pickaxe", actor, 1);
        if (stock != "missing")
        {
            var source = stock == "reachable" ? position : remote;
            inventory = InventoryFixture.AddLot(inventory, "audit-smith-stock", kind, smith.HouseholdId!, 4,
                groundPosition: new(source.X, source.Y));
        }
        state = FarmFieldTests.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                Position = position,
                HungerBasisPoints = 10_000,
                Project = null,
                LastDecisionContext = null,
                Survival = new SurvivalCondition()
            } : person).ToArray(),
        };
        var choices = new SmithChoices(kind);
        using var world = Restore(state, actor, choices);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(state), PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var noun = kind == "wood" ? "wood" : "iron ore";
        OwnerInstructionReceipt? receipt = orderKind == "routine" ? null : world.SubmitInstruction(new("smith-gather-order", "owner:test",
            actor, OwnerInstructionKind.MustDo, orderKind == "supply" ? $"supply one {noun} to my Blacksmith" : $"gather one {noun}"));
        for (var tick = 0; tick < 24 && (receipt is null ? !Obtained(world) : Order(world).Status != "finished"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(Obtained(world), $"kind={kind} stock={stock} order={orderKind} gatherOffered={choices.GatherOffered}");
        Assert.Equal(stock == "reachable" ? 4 : kind == "wood" ? 6 : 7, world.Society.Inventory.Lots.Where(lot =>
            lot.ItemKind == kind && (lot.OwnerId == actor || lot.OwnerId == smith.HouseholdId && lot.StorageBuildingId == smith.InstanceId))
            .Sum(lot => lot.Quantity));
        if (receipt is not null)
        {
            // The ore source is much farther from the smith than the wood source.
            using var walkingReplay = Restore(world.ExportState(), actor, new SmithChoices(kind));
            for (var tick = 0; tick < 180 && Order(world).Status != "finished"; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                Assert.True((await walkingReplay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
                    PrivateWorldRuntimeCodec.Encode(walkingReplay.ExportState()));
            }
            Assert.Equal("finished", Order(world).Status);
            Assert.Equal(orderKind == "supply" ? 1 : kind == "wood" ? 6 : 7, Order(world).CompletedUnits);
        }
        if (stock == "unreachable")
        {
            var lot = world.Society.Inventory.GetLot("audit-smith-stock");
            Assert.Equal((smith.HouseholdId, 4, new InventoryGroundPosition(remote.X, remote.Y)),
                (lot.OwnerId, lot.Quantity, lot.GroundPosition));
        }
        Assert.Equal(inventory.Reservations, world.Society.Inventory.Reservations);
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(saved), actor, new SmithChoices(kind));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        reloaded.Validate();
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));

        bool Obtained(PrivateWorldRuntime runtime) => runtime.Society.Inventory.Lots.Any(lot => lot.ItemKind == kind &&
            (lot.OwnerId == actor || lot.OwnerId == smith.HouseholdId && lot.StorageBuildingId == smith.InstanceId));
        OwnerInstructionOrder Order(PrivateWorldRuntime runtime) => runtime.ExportState().Instructions!
            .Single(instruction => instruction.InstructionId == receipt!.InstructionId).Order!;
    }

    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, string actor, SmithChoices choices) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? choices : new ActionCoverageRecorder(chooseIdle: true));

    private sealed class SmithChoices(string kind) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public bool GatherOffered { get; private set; }
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var gather = kind == "iron_ore" ? "gather_smith_ore" : "gather_smith_input:" + kind;
            GatherOffered |= request.Observation.Candidates.Any(candidate => candidate.Id == gather);
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == gather) ??
                request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "haul_smith_input") ??
                request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == "haul_household_stock") ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind,
                request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1, new Dictionary<string, double> { [selected.Id] = 1 }));
        }
    }
}
