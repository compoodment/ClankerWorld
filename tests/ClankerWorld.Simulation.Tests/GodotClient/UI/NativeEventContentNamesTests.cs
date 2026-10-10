using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Cognition;
using OwnerInstructionKind = ClankerWorld.Simulation.Harness.OwnerInstructionKind;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class NativeEventContentNamesTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<byte[]> Baseline = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("cart-set-unfinished", _ => new ProductionChooser());
        Assert.True(world.AdvanceOneTickAsync().AsTask().GetAwaiter().GetResult().Advanced);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    });

    [Fact]
    public void PaidNativeClinicNamesItsActualDefinitionAndPreservesItsAcceptedEvent()
    {
        var state = PrivateWorldRuntimeCodec.Decode(Baseline.Value);
        var home = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-b");
        var owner = home.HouseholdId!;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "event-clinic-wood", "wood", owner, 10,
            storageBuildingId: home.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "event-clinic-stone", "stone", owner, 4, storageBuildingId: home.InstanceId);
        using var world = Restore(WithInventory(state, inventory));
        var definition = world.WorldContent.Buildings.Single(building => building.LocalId == "clinic-1x2");
        var wood = Quantity(world, owner, "wood");
        var stone = Quantity(world, owner, "stone");
        BuildingPlacementResult? result = null;
        foreach (var tile in state.Map.Tiles.Where(tile => state.Map.IsBuildable(tile.Position))
            .OrderBy(tile => Math.Abs(tile.Position.X - home.Position.X) + Math.Abs(tile.Position.Y - home.Position.Y)))
        {
            result = world.PlaceBuilding("event-clinic", definition.CanonicalId, tile.Position, owner);
            if (result.Applied) break;
        }
        Assert.True(result?.Applied, result?.Failure);
        Assert.Equal(wood - 10, Quantity(world, owner, "wood"));
        Assert.Equal(stone - 4, Quantity(world, owner, "stone"));
        var snapshot = Snapshot(world);
        var placed = Assert.Single(snapshot.PlacedBuildings, building => building.InstanceId == "event-clinic");
        Assert.Equal("Clinic", placed.DisplayName);
        var worldEvent = ClientEvent(world.ExportState().Events.Last(item => item.Kind == "building_placed"));
        Assert.Contains(definition.CanonicalId, worldEvent.Detail, StringComparison.Ordinal);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal("A new Clinic was built.", WorldEventText.Describe(worldEvent, snapshot));
        Assert.Equal("A new Clinic was built.", WorldEventText.Describe(worldEvent, null));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        CheckReload(world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAxeProductionAndOwnerOrderKeepTheirRecipeName(bool ownerOrder)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Baseline.Value);
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var owner = smith.HouseholdId!;
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == owner).Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "event-axe-wood", "wood", owner, 3,
            storageBuildingId: smith.InstanceId);
        state = WithInventory(state, inventory) with
        {
            JevEnabled = true,
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? smith.Position : person.Position,
                Equipment = person.InhabitantId == actor ? null : person.Equipment,
                Project = null,
                LastDecisionContext = null,
                HungerBasisPoints = 10_000,
                Survival = new(),
            }).ToArray(),
        };
        using var world = Restore(state);
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "wooden-axe");
        if (ownerOrder)
            world.SubmitInstruction(new("event-make-axe", "owner:test", actor, OwnerInstructionKind.MustDo, "make one wooden axe"));
        else
        {
            var started = world.StartProduction(recipe.CanonicalId, smith.InstanceId, actor);
            Assert.True(started.Applied, started.Failure);
        }
        for (var tick = 0; tick < 90 && !world.WorldSimulation.ProductionJobs.Any(job => job.State == WorldProductionJobState.Completed); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var job = Assert.Single(world.WorldSimulation.ProductionJobs);
        Assert.Equal(WorldProductionJobState.Completed, job.State);
        Assert.Contains(world.Society.Inventory.Lots, lot => lot.Id == job.JobId + ":output:00" && lot.Quantity > 0 &&
            recipe.Outputs.Any(output => output.ResourceId == lot.ItemKind));
        var events = world.ExportState().Events.Where(item => item.Kind is "recipe_started" or "build_started" or "recipe_completed")
            .Select(ClientEvent).ToArray();
        Assert.Equal(2, events.Length);
        var snapshot = Snapshot(world);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal("Make wooden axe", recipe.DisplayName);
        Assert.Equal(ownerOrder ? "Work began on a new Make wooden axe." : "Work began on make wooden axe.",
            WorldEventText.Describe(events[0], snapshot));
        Assert.Equal("A batch of make wooden axe was made.", WorldEventText.Describe(events[1], snapshot));
        Assert.Equal("A batch of wooden axe was made.", WorldEventText.Describe(events[1], null));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        CheckReload(world);
    }

    [Fact]
    public void OrdinaryNativeWorldCreatedEventKeepsItsExistingWording()
    {
        using var world = Restore(PrivateWorldRuntimeCodec.Decode(Baseline.Value));
        var worldEvent = ClientEvent(world.ExportState().Events.Single(item => item.Kind == "world_created"));
        Assert.Equal("A new world has begun.", WorldEventText.Describe(worldEvent, Snapshot(world)));
        CheckReload(world);
    }

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
    private static int Quantity(PrivateWorldRuntime world, string owner, string kind) => world.Society.Inventory.Lots
        .Where(lot => lot.OwnerId == owner && lot.ItemKind == kind).Sum(lot => lot.Quantity);
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, _ => new ProductionChooser());
    private static OwnerWorldSnapshot Snapshot(PrivateWorldRuntime world) => JsonSerializer.Deserialize<OwnerWorldSnapshot>(
        JsonSerializer.Serialize(new OwnerWorldObservationStore(world).GetSnapshot(), WebJson), WebJson)!;
    private static OwnerWorldEvent ClientEvent(PlaytestWorldEvent worldEvent) => JsonSerializer.Deserialize<OwnerWorldEvent>(
        JsonSerializer.Serialize(worldEvent, WebJson), WebJson)!;
    private static void CheckReload(PrivateWorldRuntime world)
    {
        world.Validate();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var reloaded = Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
    }

    private sealed class ProductionChooser : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            request.Validate();
            var observation = request.Observation;
            var choice = observation.OperativeOrderInstructionId is not null && observation.Candidates.Any(candidate => candidate.Id == "produce_item")
                ? "produce_item" : "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind,
                request.ProviderEpoch, observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest,
                choice, 1, new Dictionary<string, double> { [choice] = 1 }));
        }
    }
}
