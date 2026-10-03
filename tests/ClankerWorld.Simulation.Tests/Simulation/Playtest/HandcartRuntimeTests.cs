using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
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
    public async Task DeathKeepsTheCartInEscrowThenLetsItsHeirReachRepairAndUnloadAcrossReload()
    {
        using var setup = new PrivateWorldRuntime("inherited-visible-handcart", _ => new CartChooser("safe_idle"));
        var state = setup.ExportState();
        var owner = state.Inhabitants[0].InhabitantId;
        var household = state.Society.Society.GetInhabitant(owner).HouseholdId;
        var heir = state.Society.Society.Inhabitants.First(person => person.Id != owner && person.HouseholdId == household).Id;
        // Another household member still cannot claim the heir's personal cart.
        var outsider = state.Society.Society.Inhabitants.First(person => person.Id != owner && person.Id != heir).Id;
        var position = state.Inhabitants.Single(person => person.InhabitantId == owner).Position;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "estate-cart", "handcart", owner, 1,
            conditionBasisPoints: 1, groundPosition: new(position.X, position.Y));
        inventory = InventoryFixture.AddLot(inventory, "estate-cargo", "stone", owner, 32, containerLotId: "estate-cart");
        inventory = InventoryFixture.AddLot(inventory, "heir-repair-wood", "wood", heir, 1);
        inventory = InventoryFixture.AddLot(inventory, "heir-repair-fittings", "iron_fittings", heir, 1);
        inventory = InventoryFixture.AddLot(inventory, "heir-repair-rope", "rope", heir, 1);
        var society = state.Society.Society;
        state = state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inventory = inventory,
                    LifeClock = null,
                    Config = society.Config with
                    {
                        TicksPerWorldDay = 1,
                        DaysPerWorldYear = 4,
                        EstateEscrowDays = 1,
                        ContractVersion = 3,
                        DayLifecycle = new(3, 12, 900, 1000),
                        BaseNaturalMortalityBasisPoints = 0,
                        NaturalMortalitySlopeBasisPoints = 0,
                    },
                    Inhabitants = society.Inhabitants.Select(person => person with
                    {
                        BirthTick = person.Id == owner ? -999 : -12,
                        BirthLifeTick = null,
                        AgeBand = person.Id == owner ? SocietyAgeBand.Elder : SocietyAgeBand.Adult,
                        LastLifecycleYearChecked = person.Id == owner ? 999 : 12,
                    }).ToArray(),
                },
            },
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Config = state.WorldSystems.Config with
                { TicksPerDay = 1, DaysPerYear = 4, SpringDays = 1, SummerDays = 1, AutumnDays = 1, WinterDays = 1 },
            },
            Inhabitants = state.Inhabitants.Select(person => person with
            { HungerBasisPoints = 10_000, Project = null, LastDecisionContext = null }).ToArray(),
            HandcartHitches = [new("estate-cart", owner)],
        };
        using var dying = PrivateWorldRuntime.Restore(state, _ => new CartChooser("safe_idle"));
        Assert.True((await dying.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyInhabitantStatus.Dead, dying.Society.GetInhabitant(owner).Status);
        var estate = Assert.Single(dying.Society.Estates);
        Assert.Equal(estate.Id, dying.Society.Inventory.GetLot("estate-cart").OwnerId);
        Assert.Equal(estate.Id, dying.Society.Inventory.GetLot("estate-cargo").OwnerId);
        Assert.Empty(dying.ExportState().HandcartHitches!);
        Assert.Equal(new InventoryGroundPosition(position.X, position.Y), dying.Society.Inventory.GetLot("estate-cart").GroundPosition);
        var visibleEscrowCart = Assert.Single(new OwnerWorldObservationStore(dying).GetSnapshot().Handcarts);
        Assert.Equal("Estate property", visibleEscrowCart.OwnerName);
        Assert.Null(visibleEscrowCart.PullerId);
        Assert.Equal(32, visibleEscrowCart.Cargo.Sum(item => item.Quantity));
        var escrow = dying.ExportState();
        escrow = escrow with
        {
            Society = escrow.Society with
            { Society = escrow.Society.Society with { Inventory = InventoryFixture.WearSingleUnit(dying.Society.Inventory, "estate-cart", 1) } },
        };
        var bytes = PrivateWorldRuntimeCodec.Encode(escrow);
        var chooser = new CartChooser("repair_handcart:estate-cart");
        var outsiderChooser = new CartChooser("attach_handcart:estate-cart");
        using var recovered = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            id => id == heir ? chooser : id == outsider ? outsiderChooser : new CartChooser("safe_idle"));
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(recovered.ExportState()));
        await Until(recovered, () => recovered.Society.Inventory.GetLot("estate-cart").OwnerId == heir);
        Assert.Equal(heir, recovered.Society.Inventory.GetLot("estate-cargo").OwnerId);
        Assert.Equal(new InventoryGroundPosition(position.X, position.Y), recovered.Society.Inventory.GetLot("estate-cart").GroundPosition);
        Assert.Equal(32, recovered.Society.Inventory.GetLot("estate-cargo").Quantity);
        await Until(recovered, () => recovered.Society.Inventory.GetLot("estate-cart").ConditionBasisPoints == 10_000);
        Assert.Equal(position, recovered.Inhabitants.Single(person => person.InhabitantId == heir).Position);
        Assert.DoesNotContain(recovered.Society.Inventory.Lots, lot => lot.Id is "heir-repair-wood" or "heir-repair-fittings" or "heir-repair-rope");
        Assert.DoesNotContain(outsiderChooser.Offered, id => id is "attach_handcart:estate-cart" or "repair_handcart:estate-cart" or "unload_handcart_ground:estate-cargo");
        chooser.Preferred = "unload_handcart_ground:estate-cargo";
        await Until(recovered, () => recovered.Society.Inventory.GetLot("estate-cargo").ContainerLotId is null);
        var cargo = recovered.Society.Inventory.GetLot("estate-cargo");
        Assert.Equal((heir, 32, new InventoryGroundPosition(position.X, position.Y)), (cargo.OwnerId, cargo.Quantity, cargo.GroundPosition));
        var recoveredBytes = PrivateWorldRuntimeCodec.Encode(recovered.ExportState());
        using var finalReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(recoveredBytes));
        Assert.Equal(recoveredBytes, PrivateWorldRuntimeCodec.Encode(finalReload.ExportState()));
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

    [Fact]
    public async Task ParkedCartIsPulledNotCollectedIntoItsOwnersHands()
    {
        using var setup = NormalPathWorld.CreateGenerated("parked-cart-not-collected", _ => new CartChooser("safe_idle"));
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var parkedAt = new InventoryGroundPosition(smith.Position.X, smith.Position.Y);
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "parked-cart", "handcart", actor, 1,
            groundPosition: parkedAt);
        // Collecting personal goods once moved a parked cart into its owner's hands and failed the tick.
        var chooser = new CartChooser("household_collect:parked-cart");
        using var world = PrivateWorldRuntime.Restore(AtPosition(state, actor, smith.Position, inventory),
            id => id == actor ? chooser : new CartChooser("safe_idle"));
        await Until(world, () => chooser.Offered.Contains("attach_handcart:parked-cart"), 20);

        Assert.DoesNotContain("household_collect:parked-cart", chooser.Offered);
        Assert.Equal(parkedAt, world.Society.Inventory.GetLot("parked-cart").GroundPosition);
        Assert.Equal(0, PersonalEquipmentRules.CarriedQuantity(world.Society.Inventory, actor, null));
    }

    [Fact]
    public async Task LeavingTheHouseholdStopsAPersonalCartBuildAndKeepsItsMaterials()
    {
        using var setup = NormalPathWorld.CreateGenerated("leaving-cart-builder", _ => new CartChooser("safe_idle"));
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var recipe = state.WorldContent!.Recipes.Single(item => item.LocalId == "handcart");
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "leaver-wood", "wood", actor, 4);
        inventory = InventoryFixture.AddLot(inventory, "leaver-fittings", "iron_fittings", actor, 2);
        inventory = InventoryFixture.AddLot(inventory, "leaver-rope", "rope", actor, 1);
        var chooser = new CartChooser("build:recipe:" + recipe.CanonicalId);
        using var world = PrivateWorldRuntime.Restore(AtPosition(state, actor, smith.Position, inventory),
            id => id == actor ? chooser : new CartChooser("safe_idle"));
        await Until(world, () => world.WorldSimulation.ProductionJobs.Any(job => job.RecipeId == recipe.CanonicalId), 80);
        var job = Assert.Single(world.WorldSimulation.ProductionJobs, job => job.RecipeId == recipe.CanonicalId);
        chooser.Preferred = "safe_idle";

        // Nobody else may finish a cart for its builder, so leaving stops the work
        // instead of pausing it with the builder's materials locked forever.
        Assert.True(world.DisplaceAdult(actor));

        Assert.Equal(WorldProductionJobState.Cancelled, world.WorldSimulation.ProductionJobs.Single(item => item.JobId == job.JobId).State);
        Assert.All(job.InputReservationIds, id =>
            Assert.NotEqual(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation(id).State));
        foreach (var (lotId, quantity) in new[] { ("leaver-wood", 4), ("leaver-fittings", 2), ("leaver-rope", 1) })
        {
            var lot = world.Society.Inventory.GetLot(lotId);
            Assert.Equal(quantity, lot.Quantity);
            Assert.True(ToolProgressionRules.IsTopLevelCarriedLot(lot, actor) && lot.OwnerId == actor);
        }
        Assert.Contains(world.ExportState().Events, item => item.Kind == "recipe_cancelled" &&
            item.Detail.StartsWith(job.JobId + ":", StringComparison.Ordinal));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new CartChooser("safe_idle"));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    [Theory]
    [InlineData("haul_smith_input", "smith_input_delivered", 2)]
    [InlineData("supply_workstation:wood", "workstation_supplied", 0)]
    public async Task HouseholdHaulsLeaveABuildersCartMaterialsWithThem(string haul, string delivered, int extraWood)
    {
        using var setup = NormalPathWorld.CreateGenerated("cart-materials-kept", _ => new CartChooser("safe_idle"));
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "kept-wood", "wood", actor, 4);
        inventory = InventoryFixture.AddLot(inventory, "kept-rope", "rope", actor, 1);
        if (extraWood > 0)
            inventory = InventoryFixture.AddLot(inventory, "extra-wood", "wood", actor, extraWood);
        inventory = InventoryFixture.AddLot(inventory, "stock-fittings", "iron_fittings", smith.HouseholdId!, 2,
            groundPosition: new(smith.Position.X, smith.Position.Y));
        // Household hauls once took a builder's carried cart wood into stock, and the builder
        // collected it back for the cart, round and round, so neither the cart nor anything else got done.
        var chooser = new CartChooser(haul);
        using var world = PrivateWorldRuntime.Restore(AtPosition(state, actor, smith.Position, inventory),
            id => id == actor ? chooser : new CartChooser("safe_idle"));
        await Until(world, () => chooser.Offered.Contains("collect_handcart_material:iron_fittings") &&
            chooser.Offered.Contains(haul), 20);
        // Wood beyond the cart's set still goes where it is needed.
        if (extraWood > 0)
            await Until(world, () => world.ExportState().Events.Any(item => item.Kind == delivered &&
                item.Detail.Contains($":extra-wood:{extraWood}:", StringComparison.Ordinal)));
        for (var tick = 0; tick < 30; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            world.Validate();
        }

        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == delivered &&
            item.Detail.Contains(":kept-", StringComparison.Ordinal));
        foreach (var (lotId, quantity) in new[] { ("kept-wood", 4), ("kept-rope", 1) })
        {
            var lot = world.Society.Inventory.GetLot(lotId);
            Assert.Equal(quantity, lot.Quantity);
            Assert.True(ToolProgressionRules.IsTopLevelCarriedLot(lot, actor) && lot.OwnerId == actor);
        }
    }

    [Fact]
    public async Task NobodyCollectsCartMaterialsForASetTheyCannotFinish()
    {
        using var setup = NormalPathWorld.CreateGenerated("cart-set-unfinished", _ => new CartChooser("safe_idle"));
        var state = setup.ExportState();
        var smith = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-blacksmith");
        var actor = state.Society.Society.Inhabitants.First(person => person.HouseholdId == smith.HouseholdId).Id;
        var site = new InventoryGroundPosition(smith.Position.X, smith.Position.Y);
        Assert.DoesNotContain(state.Society.Society.Inventory.Lots, lot => lot.ItemKind == "iron_fittings");
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "stock-wood", "wood", smith.HouseholdId!, 4, groundPosition: site);
        inventory = InventoryFixture.AddLot(inventory, "stock-rope", "rope", smith.HouseholdId!, 1, groundPosition: site);
        var unfinished = new CartChooser("safe_idle");
        using (var world = PrivateWorldRuntime.Restore(AtPosition(state, actor, smith.Position, inventory),
                   id => id == actor ? unfinished : new CartChooser("safe_idle")))
            await Until(world, () => unfinished.Offered.Count > 0, 20);
        // Without fittings anywhere, wood and rope taken for a cart would only sit in the builder's hands.
        Assert.DoesNotContain(unfinished.Offered, id => id.StartsWith("collect_handcart_material:", StringComparison.Ordinal));

        inventory = InventoryFixture.AddLot(inventory, "stock-fittings", "iron_fittings", smith.HouseholdId!, 2, groundPosition: site);
        var complete = new CartChooser("safe_idle");
        using var finishable = PrivateWorldRuntime.Restore(AtPosition(state, actor, smith.Position, inventory),
            id => id == actor ? complete : new CartChooser("safe_idle"));
        await Until(finishable, () => complete.Offered.Count > 0, 20);
        Assert.Contains("collect_handcart_material:wood", complete.Offered);
        Assert.Contains("collect_handcart_material:iron_fittings", complete.Offered);
        Assert.Contains("collect_handcart_material:rope", complete.Offered);
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
