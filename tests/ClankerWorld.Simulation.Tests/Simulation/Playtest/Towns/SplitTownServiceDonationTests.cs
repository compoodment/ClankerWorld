using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class SplitTownServiceDonationTests
{
    // The existing service test keeps ten stone in one lot. Real storage and
    // collection must not turn an otherwise legal four-stone service into an
    // unencodable checkpoint or hide its dedicated completion choice.
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(true, false, 4)]
    [InlineData(true, true, 1)]
    public async Task RealSplitCarriedStoneCanFulfillAcceptedTownServiceAndRemainSaveable(
        bool split, bool ordinaryDonation, int reservedQuantity)
    {
        var state = await NonviolentRuntimeFixture.FindingAsync();
        const string actor = NonviolentRuntimeFixture.Subject;
        var town = state.Towns![0];
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId;
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind != "stone" ||
                lot.OwnerId != actor && lot.OwnerId != household).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "split-service-stone", "stone", actor, 8);
        inventory = InventoryFixture.AddLot(inventory, "split-service-sack", "sack", actor, 1);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var equip = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == actor
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id == "equip_carry_aid") : null,
        };
        using (var setup = NonviolentRuntimeFixture.Create(state, equip))
        {
            NonviolentRuntimeFixture.Wake(setup, actor, "equip-service-sack");
            await NonviolentRuntimeFixture.UntilAsync(setup, () => setup.Inhabitants.Single(person =>
                person.InhabitantId == actor).Equipment?.CarryAidLotId == "split-service-sack", 4);
            state = NonviolentRuntimeFixture.Strict(setup.ExportState());
        }
        state = await NonviolentRuntimeFixture.AcceptRemedyAsync(state,
            [new("public_service_goods", actor, town.Id, "stone", 4, warehouse.InstanceId)]);
        using (var preparing = NonviolentRuntimeFixture.Create(state, new NonviolentTestProvider()))
        {
            if (split)
            {
                await Command(preparing, actor, "store-service-stone", "store four stone in my House");
                await Command(preparing, actor, "collect-service-stone", "collect four stone");
                Assert.Equal([4, 4], preparing.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                    lot.ItemKind == "stone" && PersonalEquipmentRules.IsCarried(lot, actor)).Select(lot => lot.Quantity).Order());
            }
            await Command(preparing, actor, "reach-service-warehouse", $"go to ({warehouse.Position.X},{warehouse.Position.Y})");
            state = NonviolentRuntimeFixture.Strict(preparing.ExportState());
        }
        if (reservedQuantity > 0)
        {
            var held = state.Society.Society.Inventory;
            var source = held.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "stone" &&
                PersonalEquipmentRules.IsCarried(lot, actor)).OrderBy(lot => lot.Id, StringComparer.Ordinal).First();
            held = InventoryFixture.Reserve(held, "independent-stone-work", actor, source.Id, reservedQuantity,
                "independent_work", held.WorldTick + 1_000);
            state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = held } } };
            state = NonviolentRuntimeFixture.Strict(state);
        }
        var prefix = ordinaryDonation ? "store_town_resources" : "nonviolent_remedy:";
        var completion = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == actor
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal)) : null,
        };
        using var world = NonviolentRuntimeFixture.Create(state, completion);
        NonviolentRuntimeFixture.Wake(world, actor, "complete-split-service");
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var directory = Path.Combine(Path.GetTempPath(), "clankerworld-split-service-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "world.json");
            var file = new PrivateWorldStateFile(path);
            _ = file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(1));
            presence.RecordAuthenticatedReconnect("owner:test");
            using var service = new PrivateWorldRuntimeService(world, file, presence);
            for (var tick = 0; tick < 4 && world.Towns[0].Nonviolent.Effects.Count == 0; tick++)
                Assert.True(await service.TryAdvanceOnceAsync());
            _ = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(path));
            world.Pause();
            var pausedTick = world.WorldTick;
            Assert.False(await service.TryAdvanceOnceAsync());
            Assert.Equal(pausedTick, world.WorldTick);
            world.Resume();
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.True(world.WorldTick > pausedTick);
            _ = PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        if (reservedQuantity == 4)
        {
            Assert.Empty(world.Towns[0].Nonviolent.Effects);
            Assert.Empty(world.Towns[0].Nonviolent.NativeReceipts);
            Assert.Equal("pending", Assert.Single(world.Towns[0].Nonviolent.Agreements).Status);
            Assert.Equal(8, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "stone").Sum(lot => lot.Quantity));
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("independent-stone-work").State);
            Assert.DoesNotContain(completion.Observations.Where(observation => observation.InhabitantId == actor)
                .SelectMany(observation => observation.Candidates), candidate =>
                candidate.Id == "store_town_resources" || candidate.Id.StartsWith("nonviolent_remedy:", StringComparison.Ordinal));
            NonviolentRuntimeFixture.Strict(world.ExportState());
            return;
        }
        var donated = 4 - reservedQuantity;
        Assert.Equal(reservedQuantity == 0 ? "completed" : "pending", Assert.Single(world.Towns[0].Nonviolent.Agreements).Status);
        Assert.Equal(donated, Assert.Single(world.Towns[0].Nonviolent.Effects).Quantity);
        Assert.Equal(8 - donated, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "stone").Sum(lot => lot.Quantity));
        Assert.Equal(donated, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == town.Id && lot.ItemKind == "stone" &&
            lot.StorageBuildingId == warehouse.InstanceId).Sum(lot => lot.Quantity));
        var receiptProof = Assert.Single(world.Towns[0].Nonviolent.NativeReceipts);
        Assert.Equal(split ? 4 : 8, receiptProof.SourceQuantityBefore);
        Assert.Equal((split ? 4 : 8) - reservedQuantity, receiptProof.AvailableQuantityBefore);
        Assert.Equal(8L - reservedQuantity, receiptProof.CarriedAvailableQuantityBefore);
        if (reservedQuantity > 0)
        {
            var reservation = world.Society.Inventory.GetReservation("independent-stone-work");
            Assert.Equal(InventoryReservationState.Reserved, reservation.State);
            Assert.Equal(reservedQuantity, reservation.Quantity);
        }
        var completed = NonviolentRuntimeFixture.Strict(world.ExportState());
        // Restart both continuations, rather than mixing a hosted runtime's
        // in-flight reply scheduling with the blocking replay boundary.
        using var first = NonviolentRuntimeFixture.Create(completed, completion);
        using var replay = NonviolentRuntimeFixture.Create(completed, completion);
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True((await first.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Single(first.Towns[0].Nonviolent.Effects);
        Assert.Single(first.Towns[0].Nonviolent.NativeReceipts);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(first.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        if (split && ordinaryDonation && reservedQuantity == 0)
        {
            // Rehash the receipt and its effect reference, so these refusals
            // exercise the reserve contract rather than a stale digest.
            long?[] invalidAvailable = [null, 3, 7];
            var saved = world.ExportState();
            var savedTown = saved.Towns![0];
            foreach (var available in invalidAvailable)
            {
                var invalid = receiptProof with { CarriedAvailableQuantityBefore = available, Version = "" };
                invalid = invalid with { Version = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(invalid))).ToLowerInvariant() };
                var rejected = saved with
                {
                    Towns = [savedTown with
                    {
                        Nonviolent = savedTown.Nonviolent with
                        {
                            NativeReceipts = [invalid],
                            Effects = savedTown.Nonviolent.Effects.Select(effect => effect with { NativeReceiptVersion = invalid.Version }).ToArray(),
                        },
                    }],
                };
                var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(rejected));
                Assert.Contains("saved native remedy receipt", error.Message, StringComparison.Ordinal);
            }
        }
    }

    private static async Task Command(PrivateWorldRuntime world, string actor, string key, string text)
    {
        var receipt = world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.MustDo, text));
        await NonviolentRuntimeFixture.UntilAsync(world,
            () => world.ExportState().CompletedInstructionIds!.Contains(receipt.InstructionId), 80);
    }
}
