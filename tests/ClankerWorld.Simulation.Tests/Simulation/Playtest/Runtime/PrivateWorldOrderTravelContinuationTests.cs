using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class PrivateWorldRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TravelOrderDelayedByCooldownFinishesOnArrivalAndAdvancesQueue(bool reload)
    {
        var provider = new OrderCandidateRecordingProvider("seek_food");
        Func<string, IDecisionProvider> providers = id => id == HarvestInstructionActor
            ? provider
            : new CountingSelectingProvider(DecisionProviderKind.Deterministic, chooseIdle: true);
        using var setup = CreateKnownBerryOrderWorld(providers);
        var initial = setup.ExportState();
        var source = initial.Map.Resources.Single(resource => resource.Id == "berry-patch");
        var occupied = initial.Inhabitants.Where(person => person.InhabitantId != HarvestInstructionActor)
            .Select(person => person.Position).ToHashSet();
        occupied.UnionWith(initial.WorldSimulation!.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(
                setup.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId),
                building.Position)));
        var start = initial.Map.Tiles.Select(tile => tile.Position)
            .Where(point => initial.Map.IsPassable(point) && !occupied.Contains(point) &&
                initial.Map.FootDistance(point, source.Position) is >= 4 and <= 6 &&
                !initial.Map.Resources.Any(resource => resource.Position == point))
            .OrderBy(point => initial.Map.FootDistance(point, source.Position))
            .ThenBy(point => point.Y).ThenBy(point => point.X)
            .First(point => DeterministicRouteFinder.TryFind(initial.Map, point, source.Position, out var route) &&
                route.All(step => !occupied.Contains(step)));
        initial = initial with
        {
            Inhabitants = initial.Inhabitants.Select(person => person.InhabitantId == HarvestInstructionActor
                ? person with { Position = start, HungerBasisPoints = 3_000, TravelCooldownTicks = 1 }
                : person).ToArray(),
            Society = initial.Society with
            {
                Society = initial.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(initial.Society.Society.Inventory,
                        "queued-travel-food", "berries", HarvestInstructionActor, 1),
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(initial)), providers);
        var travel = world.SubmitInstruction(new OwnerInstructionRequest(
            "delayed-travel", "owner:test", HarvestInstructionActor, OwnerInstructionKind.MustDo,
            "travel to berries"));
        var eat = world.SubmitInstruction(new OwnerInstructionRequest(
            "eat-after-travel", "owner:test", HarvestInstructionActor, OwnerInstructionKind.MustDo,
            "eat berries", Queue: true));

        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var delayed = world.ExportState();
        var delayedActor = delayed.Inhabitants.Single(person => person.InhabitantId == HarvestInstructionActor);
        Assert.Equal(start, delayedActor.Position);
        Assert.Equal(0, delayedActor.TravelCooldownTicks);
        Assert.Equal("doing", CancellationOrder(delayed, travel.InstructionId).Status);
        Assert.Equal(0, CancellationOrder(delayed, travel.InstructionId).CompletedUnits);
        Assert.DoesNotContain(travel.InstructionId, delayed.CompletedInstructionIds ?? []);
        Assert.Equal("queued", CancellationOrder(delayed, eat.InstructionId).Status);
        Assert.Single(provider.Requests);
        Assert.DoesNotContain(delayed.Events, item => item.Kind == "instruction_applied");

        using var restored = reload ? PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(delayed)), providers) : null;
        var continuing = restored ?? world;
        var requestsAtArrivalStart = provider.Requests.Count;
        for (var tick = 0; tick < 16 && !continuing.ExportState().CompletedInstructionIds!.Contains(travel.InstructionId); tick++)
        {
            requestsAtArrivalStart = provider.Requests.Count;
            Assert.True((await continuing.AdvanceOneTickAsync()).Advanced);
        }

        var arrived = continuing.ExportState();
        Assert.Contains(travel.InstructionId, arrived.CompletedInstructionIds ?? []);
        Assert.Equal("finished", CancellationOrder(arrived, travel.InstructionId).Status);
        Assert.Equal(1, CancellationOrder(arrived, travel.InstructionId).CompletedUnits);
        Assert.StartsWith("arrival:berry-patch:", CancellationOrder(arrived, travel.InstructionId).LastEffectId);
        var arrivalPosition = arrived.Inhabitants.Single(person => person.InhabitantId == HarvestInstructionActor).Position;
        Assert.NotEqual(start, arrivalPosition);
        Assert.InRange(arrived.Map.FootDistance(arrivalPosition, source.Position), 0, 1);
        Assert.Equal(requestsAtArrivalStart, provider.Requests.Count);
        Assert.DoesNotContain(eat.InstructionId, arrived.CompletedInstructionIds ?? []);
        Assert.Single(arrived.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == travel.InstructionId + ":seek_food");

        for (var tick = 0; tick < 8 && !continuing.ExportState().CompletedInstructionIds!.Contains(eat.InstructionId); tick++)
            Assert.True((await continuing.AdvanceOneTickAsync()).Advanced);

        var finished = continuing.ExportState();
        Assert.Contains(eat.InstructionId, finished.CompletedInstructionIds ?? []);
        Assert.Single(finished.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == travel.InstructionId + ":seek_food");
        Assert.Single(finished.Events, item => item.Kind == "instruction_applied" &&
            item.Detail == eat.InstructionId + ":consume_food");
        Assert.DoesNotContain(finished.Society.Society.Inventory.Lots, lot => lot.Id == "queued-travel-food");
        continuing.Validate();
        using var finalReload = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(finished)), providers);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(finished), PrivateWorldRuntimeCodec.Encode(finalReload.ExportState()));
    }
}
