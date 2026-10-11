using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class OwnedToolCollectionCheckpointTests
{
    private const string Actor = "founder:00000000000000000000000000000003";
    private const string House = "first-town-house-b";
    private static readonly Lazy<byte[]> Generated = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("audit-varied-13564415-a", _ => new Choices());
        var state = world.ExportState();
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        return PrivateWorldRuntimeCodec.Encode(state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { Position = warehouse.Position } : person).ToArray(),
        });
    });

    [Theory]
    [InlineData("wooden_axe", false)]
    [InlineData("wooden_pickaxe", false)]
    [InlineData("wooden_axe", true)]
    [InlineData("wooden_pickaxe", true)]
    public async Task DepartedOwnerCollectsTheirToolThroughTheHostWithoutTransferringOwnership(string kind, bool ground)
    {
        var state = await DepartedOwnerAsync(kind);
        var lotId = LotId(kind);
        if (ground)
        {
            var position = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House).Position;
            state = WithInventory(state, InventoryFixture.Relocate(state.Society.Society.Inventory,
                "ground-tool-fixture", lotId, Actor, 1, groundPosition: new(position.X, position.Y)));
        }
        var before = state.Society.Society.Inventory.GetLot(lotId);
        var choices = new Choices("collect_" + kind);
        using var world = Restore(state, choices);
        var directory = Directory.CreateTempSubdirectory("owned-tool-collection-");
        try
        {
            var file = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"), _ => choices);
            file.Save(world);
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(5));
            presence.RecordAuthenticatedReconnect("test-owner");
            using var service = new PrivateWorldRuntimeService(world, file, presence);
            for (var tick = 0; tick < 16 && !Carried(world, lotId); tick++)
            {
                Assert.True(await service.TryAdvanceOnceAsync());
                Assert.False(world.Society.IsPaused);
                Assert.Equal(world.WorldTick, PrivateWorldRuntimeCodec.Decode(File.ReadAllBytes(file.Path)).Society.Society.WorldTick);
            }
            Assert.Contains("collect_" + kind, choices.Selected);
            Assert.True(Carried(world, lotId));
            var collected = world.Society.Inventory.GetLot(lotId);
            Assert.Equal((Actor, before.Quantity, before.ConditionBasisPoints, before.ProvenanceLotId),
                (collected.OwnerId, collected.Quantity, collected.ConditionBasisPoints, collected.ProvenanceLotId));
            Assert.Null(collected.StorageBuildingId);
            Assert.Null(collected.GroundPosition);
            Assert.Null(world.Society.GetInhabitant(Actor).HouseholdId);
            Assert.Equal(state.Events.Count(item => item.Kind == "equipment_collected" && item.Detail == Actor + ":" + kind) + 1,
                world.ExportState().Events.Count(item => item.Kind == "equipment_collected" && item.Detail == Actor + ":" + kind));
            choices.Wanted = "safe_idle";
            world.Pause();
            world.Resume();
            Assert.True(await service.TryAdvanceOnceAsync());
            using var replay = file.LoadOrCreate(state.WorldSeed);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            using var original = Restore(replay.ExportState(), new Choices());
            for (var tick = 0; tick < 3; tick++)
            {
                Assert.True((await original.AdvanceOneTickAsync()).Advanced);
                Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(original.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task RejectedOwnedToolCollectionRollsBackAndRetriesExactly()
    {
        var state = await DepartedOwnerAsync("wooden_axe");
        using var control = Restore(state, new Choices("collect_wooden_axe"));
        using var rejected = Restore(state, new Choices("collect_wooden_axe"));
        for (var tick = 0; tick < 16; tick++)
        {
            var before = PrivateWorldRuntimeCodec.Encode(rejected.ExportState());
            Assert.True((await control.AdvanceOneTickAsync()).Advanced);
            if (Carried(control, LotId("wooden_axe")))
            {
                Assert.False((await rejected.AdvanceOneTickAsync(() => false)).Advanced);
                Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(rejected.ExportState()));
                Assert.False(Carried(rejected, LotId("wooden_axe")));
                Assert.True((await rejected.AdvanceOneTickAsync()).Advanced);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(control.ExportState()), PrivateWorldRuntimeCodec.Encode(rejected.ExportState()));
                return;
            }
            Assert.True((await rejected.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(control.ExportState()), PrivateWorldRuntimeCodec.Encode(rejected.ExportState()));
        }
        Assert.Fail("The offered personal-tool collection never committed.");
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("full")]
    [InlineData("foreign-house")]
    [InlineData("other-carrier")]
    public async Task PersonalOwnershipDoesNotBypassCollectionBoundaries(string boundary)
    {
        var state = await DepartedOwnerAsync("wooden_axe");
        var inventory = state.Society.Society.Inventory;
        var lotId = LotId("wooden_axe");
        if (boundary == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "tool-commitment", Actor, lotId, 1, "other_work", long.MaxValue);
        if (boundary == "full")
        {
            var room = PersonalEquipmentRules.FreeCapacity(inventory, Actor, state.Inhabitants.Single(person => person.InhabitantId == Actor).Equipment);
            inventory = InventoryFixture.AddLot(inventory, "tool-ballast", "stone", Actor, room);
        }
        if (boundary == "foreign-house")
            inventory = InventoryFixture.Relocate(inventory, "foreign-storage-fixture", lotId, Actor, 1, storageBuildingId: "first-town-house-a");
        if (boundary == "other-carrier")
            inventory = InventoryFixture.Relocate(inventory, "other-custody-fixture", lotId, Actor, 1, carrierId: state.Inhabitants[0].InhabitantId);
        state = WithInventory(state, inventory);
        var choices = new Choices("collect_wooden_axe");
        using var world = Restore(state, choices);
        for (var tick = 0; tick < 12; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain("collect_wooden_axe", choices.Selected);
        Assert.DoesNotContain("collect_wooden_axe", choices.Offered);
        var retained = world.Society.Inventory.GetLot(lotId);
        var original = inventory.GetLot(lotId);
        Assert.Equal((original.OwnerId, original.Quantity, original.StorageBuildingId, original.GroundPosition, original.CarrierId),
            (retained.OwnerId, retained.Quantity, retained.StorageBuildingId, retained.GroundPosition, retained.CarrierId));
        if (boundary == "reserved") Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("tool-commitment").State);
        Assert.Equal(state.Events.Count(item => item.Kind == "equipment_collected"),
            world.ExportState().Events.Count(item => item.Kind == "equipment_collected"));
        using var reloaded = Restore(world.ExportState(), new Choices());
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    [Fact]
    public async Task SharedHouseholdToolsStillBelongToTheirLenderAfterCollection()
    {
        var state = PrivateWorldRuntimeCodec.Decode(Generated.Value);
        var household = state.Society.Society.GetInhabitant(Actor).HouseholdId!;
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == House);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "shared-tool", "wooden_axe", household, 1, storageBuildingId: House)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == Actor
                ? person with { Position = house.Position } : person).ToArray(),
        };
        using var world = Restore(state, new Choices("collect_wooden_axe"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var borrowed = world.Society.Inventory.GetLot("shared-tool");
        Assert.Equal((household, Actor, 1), (borrowed.OwnerId, borrowed.CarrierId, borrowed.Quantity));
        Assert.True(PersonalEquipmentRules.IsCarried(borrowed, Actor));
        using var reloaded = Restore(world.ExportState(), new Choices());
        Assert.Equal(borrowed, reloaded.Society.Inventory.GetLot("shared-tool"));
    }

    private static async Task<PrivateWorldRuntimeState> DepartedOwnerAsync(string kind)
    {
        var choices = new Choices("collect_" + kind);
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(Generated.Value), choices);
        var lotId = LotId(kind);
        Assert.Equal("town:first", world.Society.Inventory.GetLot(lotId).OwnerId);
        Suggest(world, "initial-tool", "Collect the " + kind.Replace('_', ' ') + " for work.");
        for (var tick = 0; tick < 16 && !Carried(world, lotId); tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(Carried(world, lotId));
        Assert.Equal(Actor, world.Society.Inventory.GetLot(lotId).OwnerId);
        choices.Wanted = "household_store_personal:" + lotId;
        Suggest(world, "store-tool", "Store my tool at home.");
        for (var tick = 0; tick < 16 && world.Society.Inventory.GetLot(lotId).StorageBuildingId != House; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(House, world.Society.Inventory.GetLot(lotId).StorageBuildingId);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "personal_goods_stored");
        choices.Wanted = "household_leave";
        Suggest(world, "leave-household", "Leave the household.");
        for (var tick = 0; tick < 16 && world.Society.GetInhabitant(Actor).HouseholdId is not null; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Null(world.Society.GetInhabitant(Actor).HouseholdId);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "household_left");
        Assert.Equal(Actor, world.Society.Inventory.GetLot(lotId).OwnerId);
        Suggest(world, "collect-tool", "Collect the stored tool for work.");
        return PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    private static string LotId(string kind) => "first-town-" + kind.Replace('_', '-');
    private static bool Carried(PrivateWorldRuntime world, string lotId) => PersonalEquipmentRules.IsCarried(world.Society.Inventory.GetLot(lotId), Actor);
    private static void Suggest(PrivateWorldRuntime world, string key, string text) =>
        world.SubmitInstruction(new(key, "owner:test", Actor, OwnerInstructionKind.Suggestive, text));
    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, Choices choices) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => choices);

    private sealed class Choices(string wanted = "safe_idle") : IDecisionProvider
    {
        public string Wanted { get; set; } = wanted;
        public ConcurrentQueue<string> Selected { get; } = new();
        public ConcurrentQueue<string> Offered { get; } = new();
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.InhabitantId == Actor && request.Observation.Candidates.Any(candidate => candidate.Id == Wanted)
                ? Wanted : "safe_idle";
            Selected.Enqueue(selected);
            if (request.Observation.InhabitantId == Actor)
                foreach (var candidate in request.Observation.Candidates) Offered.Enqueue(candidate.Id);
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected, 1, new Dictionary<string, double> { [selected] = 1 }));
        }
    }
}
