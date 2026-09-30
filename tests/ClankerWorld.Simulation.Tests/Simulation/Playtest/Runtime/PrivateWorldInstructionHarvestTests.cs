using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    private const string HarvestInstructionActor = "agent:00000000000000000000000000000099";

    [Theory]
    [InlineData(false, "food")]
    [InlineData(true, "fruit")]
    public async Task MustDoHarvestCompletesForBerriesAndFruitAndAdvancesQueueAfterReload(bool orchard, string itemKind)
    {
        using var world = CreateHarvestInstructionWorld(orchard);
        var harvest = world.SubmitInstruction(new OwnerInstructionRequest("harvest", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "harvest food"));
        var eat = world.SubmitInstruction(new OwnerInstructionRequest("eat", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "eat food"));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var state = world.ExportState();
        Assert.Contains(world.Society.Inventory.Lots, lot =>
            lot.OwnerId == HarvestInstructionActor && lot.ItemKind == itemKind && lot.Quantity == 4);
        Assert.Contains(harvest.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.DoesNotContain(eat.InstructionId, state.CompletedInstructionIds ?? []);
        Assert.Single(state.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == harvest.InstructionId + ":harvest_food");
        world.Validate();

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(state)), _ =>
                new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        for (var tick = 0; tick < 50 && !(restored.ExportState().CompletedInstructionIds ?? []).Contains(eat.InstructionId); tick++)
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(eat.InstructionId, restored.ExportState().CompletedInstructionIds ?? []);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "instruction_applied" &&
            item.Detail == harvest.InstructionId + ":harvest_food");
        Assert.Contains(restored.ExportState().Events, item => item.Kind == "food_consumed" &&
            item.Detail == HarvestInstructionActor);
        restored.Validate();
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Fact]
    public async Task DepletedOrchardDoesNotCompleteMustDoHarvestAcrossReload()
    {
        using var initial = CreateHarvestInstructionWorld(orchard: true);
        var state = initial.ExportState();
        var foodIds = state.Map.Resources.Where(resource => resource.Kind is "food" or "fruit")
            .Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
        using var world = PrivateWorldRuntime.Restore(state with
        {
            Resources = state.Resources.Select(resource => foodIds.Contains(resource.ResourceId)
                ? resource with { State = ResourceState.Depleted } : resource).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => foodIds.Contains(resource.Id)
                        ? resource with { Quantity = 0, State = EcologyResourceState.Depleted, NextRegenerationDay = 100 }
                        : resource).ToArray(),
                },
            },
        }, _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        var harvest = world.SubmitInstruction(new OwnerInstructionRequest("depleted-harvest", "owner:test",
            HarvestInstructionActor, OwnerInstructionKind.MustDo, "harvest food"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(harvest.InstructionId, world.ExportState().CompletedInstructionIds ?? []);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "instruction_applied");

        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ =>
                new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true));
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(harvest.InstructionId, restored.ExportState().CompletedInstructionIds ?? []);
        Assert.DoesNotContain(restored.ExportState().Events, item => item.Kind == "food_harvested");
        restored.Validate();
    }

    private static PrivateWorldRuntime CreateHarvestInstructionWorld(bool orchard)
    {
        var options = new GeographyOptions("audit-food-route-13", WorldSizePreset.Small);
        var world = new PrivateWorldRuntime(options.Seed,
            _ => new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true),
            startPace: WorldStartPace.FounderSetup, geographyOptions: options);
        var map = world.ExportState().Map;
        var anchor = map.Resources.Single(item => item.Id == "berry-patch").Position;
        world.InitializeFirstTownContent();
        world.AcceptFirstTownLayout(anchor);
        var buildingTiles = world.WorldSimulation.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(
                world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId),
                building.Position)).ToHashSet();
        var startingTiles = map.Tiles.Where(tile =>
                Math.Abs(tile.Position.X - anchor.X) <= 5 && Math.Abs(tile.Position.Y - anchor.Y) <= 5 &&
                map.IsBuildable(tile.Position) && !buildingTiles.Contains(tile.Position) &&
                !map.Resources.Any(item => item.Position == tile.Position))
            .Take(4).Select(tile => tile.Position).ToArray();
        Assert.Equal(4, startingTiles.Length);
        for (var index = 0; index < startingTiles.Length; index++)
            world.PlaceFounder("founder:" + (index + 1).ToString("x32", System.Globalization.CultureInfo.InvariantCulture), startingTiles[index]);
        world.StartWorld();
        world.AddAgent(HarvestInstructionActor, orchard ? new GridPoint(29, 53) : new GridPoint(126, 66));
        return world;
    }
}
