using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class HandcartRuntimeTests
{
    [Fact]
    public async Task NormalCraftLoadPullSaveParkAndUnloadConservePhysicalMaterials()
    {
        using var setup = NormalPathWorld.CreateGenerated("normal-visible-handcart", _ => new CartChooser("safe_idle"));
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var household = smith.HouseholdId!;
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "handcart");
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "cart-wood", "wood", actor, 4);
        inventory = InventoryFixture.AddLot(inventory, "cart-fittings", "iron_fittings", actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "cart-rope", "rope", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "cart-load", "stone", household, 32,
            groundPosition: new(smith.Position.X, smith.Position.Y));
        state = AtPosition(state, actor, smith.Position, inventory);
        var held = InventoryFixture.Reserve(inventory, "held-cart-wood", actor, "cart-wood", 1,
            "other_work", inventory.WorldTick + 100);
        using (var unavailable = PrivateWorldRuntime.Restore(AtPosition(state, actor, smith.Position, held),
                   _ => new CartChooser("safe_idle")))
        {
            var rejectedStart = unavailable.StartProduction(recipe.CanonicalId, smith.InstanceId, actor);
            Assert.False(rejectedStart.Applied);
            Assert.Contains("Carry the wood", rejectedStart.Failure, StringComparison.Ordinal);
            Assert.Equal(InventoryDigest.State(held), InventoryDigest.State(unavailable.Society.Inventory));
            Assert.DoesNotContain(unavailable.WorldSimulation.ProductionJobs, job => job.RecipeId == recipe.CanonicalId);
        }
        var chooser = new CartChooser("build:recipe:" + recipe.CanonicalId);
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? chooser : new CartChooser("safe_idle"));
        await Until(world, () => world.WorldSimulation.ProductionJobs.Any(job => job.RecipeId == recipe.CanonicalId), 80);
        var running = world.ExportState();
        var job = Assert.Single(running.WorldSimulation!.ProductionJobs, job => job.RecipeId == recipe.CanonicalId);
        Assert.All(job.InputReservationIds, id =>
        {
            var reservation = world.Society.Inventory.GetReservation(id);
            Assert.Equal(actor, reservation.OwnerId);
            Assert.True(ToolProgressionRules.IsTopLevelCarriedLot(world.Society.Inventory.GetLot(reservation.LotId), actor));
        });
        var productionBytes = PrivateWorldRuntimeCodec.Encode(running);
        using (var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(productionBytes),
                   id => id == actor ? new CartChooser(chooser.Preferred) : new CartChooser("safe_idle")))
            Assert.Equal(productionBytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        await Until(world, () => world.Society.Inventory.Lots.Any(lot => lot.ItemKind == "handcart"), 140);
        var cart = Assert.Single(world.Society.Inventory.Lots, lot => lot.ItemKind == "handcart");
        Assert.Equal(actor, cart.OwnerId);
        Assert.Equal(new InventoryGroundPosition(smith.Position.X, smith.Position.Y), cart.GroundPosition);
        Assert.Null(cart.StorageBuildingId);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id is "cart-wood" or "cart-fittings" or "cart-rope");
        Assert.Contains(chooser.Offered, id => id == "build:recipe:" + recipe.CanonicalId);

        chooser.Preferred = "attach_handcart:" + cart.Id;
        await Until(world, () => world.ExportState().HandcartHitches!.Count == 1);
        chooser.Preferred = "load_handcart:cart-load";
        await Until(world, () => world.Society.Inventory.GetLot("cart-load").ContainerLotId == cart.Id);
        Assert.Equal(actor, world.Society.Inventory.GetLot("cart-load").OwnerId);
        Assert.Equal(0, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
        var destination = world.WorldSimulation.Buildings.Where(building => building.Position != smith.Position)
            .OrderByDescending(building => state.Map.FootDistance(smith.Position, building.Position)).First();
        chooser.Preferred = "pull_handcart:" + destination.InstanceId;
        await Until(world, () => world.Inhabitants.Single(person => person.InhabitantId == actor).Position != smith.Position);
        var journey = world.ExportState();
        var before = PrivateWorldRuntimeCodec.Encode(journey);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(before),
            id => id == actor ? new CartChooser(chooser.Preferred) : new CartChooser("safe_idle"));
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(journey.HandcartHitches, restored.ExportState().HandcartHitches);
        var rejected = await world.AdvanceOneTickAsync(() => false);
        Assert.False(rejected.Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        await Until(world, () => world.Inhabitants.Single(person => person.InhabitantId == actor).Position == destination.Position, 140);
        Assert.Equal(new InventoryGroundPosition(destination.Position.X, destination.Position.Y), world.Society.Inventory.GetLot(cart.Id).GroundPosition);
        Assert.Equal(32, world.Society.Inventory.GetLot("cart-load").Quantity);
        Assert.True(world.Society.Inventory.GetLot(cart.Id).ConditionBasisPoints < 10_000);
        chooser.Preferred = "park_handcart";
        await Until(world, () => world.ExportState().HandcartHitches!.Count == 0);
        chooser.Preferred = "unload_handcart_ground:cart-load";
        await Until(world, () => world.Society.Inventory.GetLot("cart-load").ContainerLotId is null);
        Assert.Equal(actor, world.Society.Inventory.GetLot("cart-load").OwnerId);
        Assert.Equal(world.Society.Inventory.GetLot(cart.Id).GroundPosition, world.Society.Inventory.GetLot("cart-load").GroundPosition);
        Assert.Equal(32, world.Society.Inventory.Lots.Where(lot => lot.Id == "cart-load").Sum(lot => lot.Quantity));
    }

    [Fact]
    public async Task BrokenCartCanUnloadAndRepairWhileOutsidersCannotClaimOrAccessHouseholdGoods()
    {
        using var setup = NormalPathWorld.CreateGenerated("broken-visible-handcart", _ => new CartChooser("safe_idle"));
        var state = setup.ExportState();
        var actor = state.Inhabitants[0].InhabitantId;
        var position = state.Inhabitants[0].Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "broken-cart", "handcart", actor, 1,
            conditionBasisPoints: 0, groundPosition: new(position.X, position.Y));
        // Add the contents with the broken condition only after the group is created.
        inventory = inventory with { Lots = inventory.Lots.Select(lot => lot.Id == "broken-cart" ? lot with { ConditionBasisPoints = 10_000 } : lot).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "cart-food", "potato", actor, 12, containerLotId: "broken-cart");
        inventory = inventory with { Lots = inventory.Lots.Select(lot => lot.Id == "broken-cart" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray() };
        inventory = InventoryFixture.AddLot(inventory, "repair-wood", "wood", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "repair-fittings", "iron_fittings", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "repair-rope", "rope", actor, 1);
        var otherHousehold = state.Society.Society.Households.First(household => household.Id != state.Society.Society.GetInhabitant(actor).HouseholdId).Id;
        inventory = InventoryFixture.AddLot(inventory, "unrelated", "wood", otherHousehold, 20,
            groundPosition: new(position.X, position.Y));
        state = AtPosition(state, actor, position, inventory);
        var chooser = new CartChooser("unload_handcart:cart-food");
        var outsider = new CartChooser("attach_handcart:broken-cart");
        using var world = PrivateWorldRuntime.Restore(state, id => id == actor ? chooser : outsider);
        await Until(world, () => world.Society.Inventory.GetLot("cart-food").Quantity < 12);
        Assert.Equal(5, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "potato" && lot.ContainerLotId is null).Sum(lot => lot.Quantity));
        Assert.Equal(7, world.Society.Inventory.GetLot("cart-food").Quantity);
        Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
        Assert.DoesNotContain(outsider.Offered, candidate => candidate == "attach_handcart:broken-cart");
        Assert.DoesNotContain(chooser.Offered, candidate => candidate == "load_handcart:unrelated");
        Assert.Equal(20, world.Society.Inventory.GetLot("unrelated").Quantity);
        chooser.Preferred = "repair_handcart:broken-cart";
        await Until(world, () => world.Society.Inventory.GetLot("broken-cart").ConditionBasisPoints == 10_000);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id is "repair-wood" or "repair-fittings" or "repair-rope");
        Assert.Equal(7, world.Society.Inventory.GetLot("cart-food").Quantity);
        Assert.Equal(position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Empty(world.ExportState().HandcartHitches!);
    }

    [Fact]
    public async Task BlockedTravelAndMidJourneyBreakageKeepLoadedCartAtItsActualPosition()
    {
        using var setup = NormalPathWorld.CreateGenerated("blocked-visible-handcart", _ => new CartChooser("safe_idle"));
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var owner = state.Society.Society.Inhabitants.First(person => person.HouseholdId != smith.HouseholdId).Id;
        var obstacle = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var obstacleOriginal = state.Inhabitants.Single(person => person.InhabitantId == obstacle).Position;
        var position = state.Inhabitants.Single(person => person.InhabitantId == owner).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "journey-cart", "handcart", owner, 1,
            groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "journey-cargo", "wood", owner, 32, containerLotId: "journey-cart");
        state = AtPosition(state, owner, position, inventory) with
        {
            Inhabitants = AtPosition(state, owner, position, inventory).Inhabitants.Select(person => person.InhabitantId == obstacle
                ? person with { Position = smith.Position } : person).ToArray(),
            HandcartHitches = [new("journey-cart", owner)],
        };
        using var blocked = PrivateWorldRuntime.Restore(state,
            id => new CartChooser(id == owner ? "pull_handcart:" + smith.InstanceId : "safe_idle"));
        await Until(blocked, () => blocked.ExportState().Events.Any(item => item.Kind == "handcart_blocked"));
        Assert.Equal(new InventoryGroundPosition(position.X, position.Y), blocked.Society.Inventory.GetLot("journey-cart").GroundPosition);
        Assert.Equal(32, blocked.Society.Inventory.GetLot("journey-cargo").Quantity);
        Assert.Single(blocked.ExportState().HandcartHitches!);
        var released = blocked.ExportState();
        released = released with
        {
            Society = released.Society with
            {
                Society = released.Society.Society with
                {
                    Inventory = released.Society.Society.Inventory with
                    {
                        Lots = released.Society.Society.Inventory.Lots.Select(lot => lot.Id == "journey-cart"
                            ? lot with { ConditionBasisPoints = 1 } : lot).ToArray(),
                    },
                },
            },
            Inhabitants = released.Inhabitants.Select(person => person.InhabitantId == obstacle
                ? person with { Position = obstacleOriginal }
                : person).ToArray(),
        };
        using var breaking = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(released)),
            id => new CartChooser(id == owner ? "pull_handcart:" + smith.InstanceId : "safe_idle"));
        await Until(breaking, () => breaking.Society.Inventory.GetLot("journey-cart").ConditionBasisPoints == 0);
        var broken = breaking.Society.Inventory.GetLot("journey-cart");
        var ownerPosition = breaking.Inhabitants.Single(person => person.InhabitantId == owner).Position;
        Assert.NotEqual(position, ownerPosition);
        Assert.Equal(new InventoryGroundPosition(ownerPosition.X, ownerPosition.Y), broken.GroundPosition);
        Assert.Empty(breaking.ExportState().HandcartHitches!);
        Assert.Equal(32, breaking.Society.Inventory.GetLot("journey-cargo").Quantity);
        Assert.Equal("journey-cart", breaking.Society.Inventory.GetLot("journey-cargo").ContainerLotId);
        var bytes = PrivateWorldRuntimeCodec.Encode(breaking.ExportState());
        using var reload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(reload.ExportState()));
    }

    [Fact]
    public void CartObservationShowsRealOwnershipPositionCargoAndExclusiveSaveAttachments()
    {
        using var setup = new PrivateWorldRuntime("cart-observation");
        var state = setup.ExportState();
        var person = state.Inhabitants[0];
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "cart", "handcart", person.InhabitantId, 1,
            groundPosition: new(person.Position.X, person.Position.Y));
        inventory = InventoryFixture.AddLot(inventory, "cargo", "wood", person.InhabitantId, 16, containerLotId: "cart");
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            HandcartHitches = [new("cart", person.InhabitantId)],
        };
        using var world = PrivateWorldRuntime.Restore(state);
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        var cart = Assert.Single(snapshot.Handcarts);
        Assert.Equal((16, 32, 100, person.InhabitantId), (cart.Cargo.Sum(item => item.Quantity), cart.Capacity, cart.ConditionPercent, cart.PullerId));
        Assert.DoesNotContain(snapshot.Inhabitants.Single(item => item.Id == person.InhabitantId).Inventory, item => item.Kind == "handcart" || item.Kind == "wood");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldSnapshot>(JsonSerializer.Serialize(snapshot, options), options)!;
        var text = GameUiText.HandcartDescription(Assert.Single(client.Handcarts));
        Assert.Contains("Pulled by", text, StringComparison.Ordinal);
        Assert.Contains("Cargo: 16/32", text, StringComparison.Ordinal);
        Assert.Contains(cart.OwnerName, text, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with { HandcartHitches = [new("cart", person.InhabitantId), new("cart", state.Inhabitants[1].InhabitantId)] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with { HandcartHitches = [new("cart", state.Inhabitants[1].InhabitantId)] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with { HandcartHitches = null }));
    }

    private static PrivateWorldRuntimeState AtPosition(PrivateWorldRuntimeState state, string actor,
        GridPoint position, InventoryCheckpoint inventory) => state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = position, HungerBasisPoints = 10_000, LastDecisionContext = null, Project = null, TravelCooldownTicks = 0 }
                : person).ToArray(),
        };

    private static async Task Until(PrivateWorldRuntime world, Func<bool> completed, int maximumTicks = 80)
    {
        for (var tick = 0; tick < maximumTicks && !completed(); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            world.Validate();
        }
        Assert.True(completed(), "The normal runtime did not complete the selected cart action.");
    }

    private sealed class CartChooser(string preferred) : IDecisionProvider
    {
        public string Preferred { get; set; } = preferred;
        public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var candidates = request.Observation.Candidates;
            Offered.UnionWith(candidates.Select(item => item.Id));
            var selected = candidates.FirstOrDefault(item => item.Id == Preferred) ??
                candidates.FirstOrDefault(item => item.Id == "continue_project") ?? candidates.Single(item => item.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId,
                Kind, ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1,
                candidates.ToDictionary(item => item.Id, item => item.Id == selected.Id ? 1d : 0d)));
        }
    }
}
