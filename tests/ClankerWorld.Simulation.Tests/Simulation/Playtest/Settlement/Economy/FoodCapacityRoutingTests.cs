using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class FoodCapacityRoutingTests
{
    [Fact]
    public async Task ANearerFiveUnitOrchardDoesNotHideAReachableFourUnitBerryPickAcrossReload()
    {
        using var setup = new PrivateWorldRuntime("orchard-capacity-audit", _ => new FoodCapacityTestFixture.Choices());
        setup.StageStarterContent();
        for (var tick = 0; tick < 3; tick++) Assert.True((await setup.AdvanceOneTickAsync()).Advanced);
        var state = setup.ExportState();
        const string actor = "founder-scout";
        var household = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var berries = state.Map.Resources.Single(resource => resource.Id == "berry-patch");
        Assert.Equal(new GridPoint(4, 1), berries.Position);
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != actor).Select(person => person.Position).ToHashSet();
        var naturalSites = state.Map.Resources.Select(resource => resource.Position).ToHashSet();
        var facilities = state.Map.CampObjects.Select(item => item.Position).ToHashSet();
        var buildingTiles = state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
        var pairs = (from tile in state.Map.Tiles
                     let origin = tile.Position
                     where state.Map.IsBuildable(origin) && !occupied.Contains(origin) && !naturalSites.Contains(origin) &&
                         state.Map.FootDistance(origin, berries.Position) == 2 &&
                         state.Map.Resources.Where(resource => resource.Kind == "food" && resource.Id != berries.Id).All(resource =>
                             state.Map.FootDistance(origin, resource.Position) >= 2)
                     from site in state.Map.FootNeighbors(origin)
                     where state.Map.IsBuildable(site) && !naturalSites.Contains(site) && !facilities.Contains(site) &&
                         !buildingTiles.Contains(site) && !occupied.Contains(site) &&
                         !(state.RoadTiles ?? []).Contains(site) && !(state.Fields ?? []).Any(field => field.Position == site) &&
                         state.Map.FootDistance(site, berries.Position) <= 1 &&
                         (!state.Map.IsDiagonalFootStep(origin, site) ||
                          !occupied.Contains(new GridPoint(site.X, origin.Y)) && !occupied.Contains(new GridPoint(origin.X, site.Y)))
                     orderby origin.Y, origin.X, site.Y, site.X
                     select (Stand: origin, Orchard: site)).ToArray();
        Assert.NotEmpty(pairs);
        var pair = pairs[0];
        var orchardPoint = pair.Orchard;
        var stand = pair.Stand;
        Assert.True(state.Map.IsBuildable(stand));
        Assert.True(state.Map.IsReachableOnFoot(stand, berries.Position));
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != actor &&
                !(lot.OwnerId == household && InventoryContainerRules.IsFood(lot.ItemKind))).ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "protected-orchard-cargo", "wood", actor, 4);
        inventory = InventoryFixture.Reserve(inventory, "protected-orchard-load", actor, "protected-orchard-cargo", 4,
            "capacity_control", inventory.WorldTick + 100);
        inventory = InventoryFixture.AddLot(inventory, "actual-orchard-seed", "orchard_seed", actor, 1);
        state = FoodCapacityTestFixture.WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with
                {
                    Position = stand,
                    HungerBasisPoints = 1_000,
                    Equipment = null,
                    LastDecisionContext = null,
                    Survival = new SurvivalCondition(10_000)
                }
                : person).ToArray(),
        };
        using var planting = FoodCapacityTestFixture.Restore(state, actor, new FoodCapacityTestFixture.Choices());
        var planted = planting.PlantTree(actor, TreeGrowthRules.Orchard, "actual-orchard-seed", orchardPoint);
        Assert.True(planted.Planted, planted.Message);
        var orchardId = Assert.IsType<string>(planted.TreeId);
        state = planting.ExportState();
        Assert.DoesNotContain(state.Society.Society.Inventory.Lots, lot => lot.Id == "actual-orchard-seed");
        state = state with
        {
            Resources = state.Resources.Select(resource => resource.ResourceId == orchardId
                ? resource with { State = ResourceState.Available } : resource).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource => resource.Id == orchardId
                        ? TreeGrowthAndPlantingTests.InFruitingSeason(resource, state) with { IsPlanted = false }
                        : resource).ToArray(),
                },
            },
        };
        Assert.True(state.Map.FootDistance(stand, orchardPoint) < state.Map.FootDistance(stand, berries.Position));
        var orchard = state.WorldSystems!.Ecology.GetResource(orchardId);
        Assert.Equal((EcologyResourceState.Available, 1, false), (orchard.State, orchard.Quantity, orchard.IsPlanted));
        Assert.Equal("fruiting", TreeGrowthRules.StageOf(TreeGrowthRules.Orchard, orchard, state.WorldSystems.Climate.Season));
        Assert.Equal(ResourceState.Available, state.Resources.Single(resource => resource.ResourceId == orchardId).State);
        Assert.Equal(("fruit", TreeGrowthRules.Orchard),
            (state.Map.Resources.Single(resource => resource.Id == orchardId).Kind,
                state.Map.Resources.Single(resource => resource.Id == orchardId).TreeKind));
        Assert.True(state.Map.CanFootStep(stand, orchardPoint));
        Assert.DoesNotContain(state.Inhabitants, person => person.InhabitantId != actor && person.Position == orchardPoint);
        Assert.InRange(state.Map.FootDistance(orchardPoint, berries.Position), 0, 1);
        Assert.All(state.Map.Resources.Where(resource => resource.Kind == "food" && resource.Id != berries.Id),
            resource => Assert.True(state.Map.FootDistance(stand, berries.Position) < state.Map.FootDistance(stand, resource.Position) ||
                state.Map.FootDistance(stand, berries.Position) == state.Map.FootDistance(stand, resource.Position) &&
                StringComparer.Ordinal.Compare(berries.Id, resource.Id) < 0));
        Assert.Equal(4, PersonalEquipmentRules.CarriedQuantity(state.Society.Society.Inventory, actor, null));
        var berriesBefore = state.WorldSystems!.Ecology.GetResource(berries.Id).Quantity;
        Assert.True(berriesBefore > 0);
        var choices = new FoodCapacityTestFixture.Choices("seek_food", "harvest_food", "consume_food");
        using var world = FoodCapacityTestFixture.Restore(state, actor, choices);
        var started = world.WorldTick;
        for (var tick = 0; world.WorldTick - started < 60 && !world.ExportState().Events.Any(item => item.Kind == "food_consumed" && item.Detail == actor); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (tick == 1) await FoodCapacityTestFixture.AssertReplay(world, actor, "seek_food", "harvest_food", "consume_food");
        }
        Assert.Contains(choices.Offers, offer => offer.Contains("seek_food", StringComparer.Ordinal));
        Assert.Single(world.ExportState().Events, item => item.Kind == "food_harvested" && item.Detail == actor + ":4");
        Assert.Equal(berriesBefore - 1, world.WorldSystems.Ecology.GetResource(berries.Id).Quantity);
        Assert.InRange(state.Map.FootDistance(world.Inhabitants.Single(person => person.InhabitantId == actor).Position, berries.Position), 0, 1);
        Assert.Equal(3, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "berries").Sum(lot => lot.Quantity));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "food_consumed" && item.Detail == actor);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "fruit_harvested" && item.Detail.Contains(orchardId, StringComparison.Ordinal));
        Assert.Equal(4, world.Society.Inventory.GetLot("protected-orchard-cargo").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("protected-orchard-load").State);
        Assert.Equal(1, world.WorldSystems.Ecology.GetResource(orchardId).Quantity);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "carrying_full" && item.Detail == actor);
        await FoodCapacityTestFixture.AssertReplay(world, actor, "seek_food", "harvest_food", "consume_food");
    }

    [Fact]
    public async Task AFullCarrierFreesOneSharedServingSpaceInsteadOfTheLargerWildHarvestLoadAcrossReload()
    {
        var (state, actor, _) = await FoodCapacityTestFixture.Generated("shared-food-room-audit");
        var inventory = FoodCapacityTestFixture.WithoutPersonalCargo(state, actor);
        inventory = InventoryFixture.AddLot(inventory, "protected-food-wood", "wood", actor, 7);
        inventory = InventoryFixture.Reserve(inventory, "protected-food-wood-reservation", actor, "protected-food-wood", 7,
            "capacity_control", inventory.WorldTick + 100);
        inventory = InventoryFixture.AddLot(inventory, "one-spare-food-stone", "stone", actor, 1);
        var food = inventory.Lots.First(lot => lot.OwnerId == FoodCapacityTestFixture.Household &&
            lot.StorageBuildingId == FoodCapacityTestFixture.House && InventoryContainerRules.IsFood(lot.ItemKind));
        state = FoodCapacityTestFixture.WithFullness(FoodCapacityTestFixture.WithInventory(state, inventory), actor, 1_000);
        Assert.Equal(8, PersonalEquipmentRules.CarriedQuantity(inventory, actor, null));
        Assert.Contains(state.Map.Resources, resource => resource.Kind == "food" &&
            state.Resources.Any(status => status.ResourceId == resource.Id && status.State == ResourceState.Available) &&
            state.Map.IsReachableOnFoot(state.Inhabitants.Single(person => person.InhabitantId == actor).Position, resource.Position));
        var choices = new FoodCapacityTestFixture.Choices("make_room_for_food", "collect_shared_food", "consume_food");
        using var world = FoodCapacityTestFixture.Restore(state, actor, choices);
        var pickedUp = false;
        var started = world.WorldTick;
        for (var tick = 0; world.WorldTick - started < 40 && !world.ExportState().Events.Any(item => item.Kind == "food_consumed" && item.Detail == actor); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (!pickedUp && world.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && InventoryContainerRules.IsFood(lot.ItemKind)))
            {
                pickedUp = true;
                if (world.WorldTick - started < 40)
                    await FoodCapacityTestFixture.AssertReplay(world, actor, "make_room_for_food", "collect_shared_food", "consume_food");
            }
        }
        Assert.True(pickedUp);
        Assert.Single(world.ExportState().Events, item => item.Kind == "spare_cargo_stored" &&
            item.Detail == $"{actor}:stone:1:{FoodCapacityTestFixture.House}");
        Assert.Single(world.ExportState().Events, item => item.Kind == "food_consumed" && item.Detail == actor);
        Assert.Equal(7, world.Society.Inventory.GetLot("protected-food-wood").Quantity);
        Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("protected-food-wood-reservation").State);
        Assert.Equal(food.Quantity - 1, world.Society.Inventory.GetLot(food.Id).Quantity);
        Assert.Single(world.Society.Inventory.Lots, lot => (lot.Id == "one-spare-food-stone" || lot.ProvenanceLotId == "one-spare-food-stone") &&
            lot.Quantity == 1 && lot.OwnerId == FoodCapacityTestFixture.Household && lot.StorageBuildingId == FoodCapacityTestFixture.House);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind is "food_harvested" or "fruit_harvested");
        await FoodCapacityTestFixture.AssertReplay(world, actor, "make_room_for_food", "collect_shared_food", "consume_food");
    }

    [Theory]
    [InlineData(SocietyAgeBand.Child, false)]
    [InlineData(SocietyAgeBand.Adolescent, false)]
    [InlineData(SocietyAgeBand.Child, true)]
    public async Task SharedPotServingUsesExistingAgePermissionAndRealTakeThenConsumptionAcrossReload(
        SocietyAgeBand age, bool loose)
    {
        var (state, actor, _) = await FoodCapacityTestFixture.Generated("pot-only-food-capacity");
        var society = state.Society.Society;
        var inventory = FoodCapacityTestFixture.WithoutPersonalCargo(state, actor);
        var food = inventory.Lots.First(lot => lot.OwnerId == FoodCapacityTestFixture.Household &&
            lot.StorageBuildingId == FoodCapacityTestFixture.House && InventoryContainerRules.IsFood(lot.ItemKind));
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => !(lot.OwnerId == FoodCapacityTestFixture.Household &&
                InventoryContainerRules.IsFood(lot.ItemKind) && lot.Id != food.Id))
                .Select(lot => lot.Id == food.Id ? lot with { Quantity = 2 } : lot).ToArray(),
        };
        if (!loose)
        {
            inventory = InventoryFixture.AddLot(inventory, "child-shared-pot", InventoryContainerRules.StoragePot,
                FoodCapacityTestFixture.Household, 1, storageBuildingId: FoodCapacityTestFixture.House);
            inventory = InventoryFixture.PutIntoContainer(inventory, "actual-shared-pot-fill", FoodCapacityTestFixture.Household,
                "child-shared-pot", food.Id, 2);
        }
        if (age != SocietyAgeBand.Adult)
        {
            if (age == SocietyAgeBand.Adolescent && society.Config.DayLifecycle is not null)
            {
                // The day-based model has no adolescent band. Keep a valid
                // legacy age configuration for this distinct permission case.
                var legacy = society.Config with { ContractVersion = 2, DayLifecycle = null };
                var lifeTick = society.LifeTickAt(society.WorldTick);
                society = society with
                {
                    Config = legacy,
                    Inhabitants = society.Inhabitants.Select(person => person with
                    {
                        BirthTick = lifeTick - legacy.AdultYears * legacy.TicksPerLifecycleAge,
                        BirthLifeTick = society.LifeClock is null ? null : lifeTick - legacy.AdultYears * legacy.TicksPerLifecycleAge,
                        AgeBand = SocietyAgeBand.Adult,
                        LastLifecycleYearChecked = legacy.AdultYears,
                    }).ToArray(),
                };
            }
            var ageYears = age == SocietyAgeBand.Child ? society.Config.DayLifecycle?.ChildStartDay ?? 4 : society.Config.ChildYears;
            Assert.Equal(age, society.Config.AgeBandAt(ageYears));
            var birth = society.LifeTickAt(society.WorldTick) - ageYears * society.Config.TicksPerLifecycleAge;
            society = society with
            {
                Inhabitants = society.Inhabitants.Select(person => person.Id == actor ? person with
                {
                    BirthTick = birth,
                    BirthLifeTick = society.LifeClock is null ? null : birth,
                    AgeBand = age,
                    LastLifecycleYearChecked = ageYears,
                    CurrentRole = SocietyWorkRole.Unassigned,
                } : person).ToArray(),
            };
        }
        state = FoodCapacityTestFixture.WithFullness(state with
        {
            Society = state.Society with { Society = society with { Inventory = inventory } },
        }, actor, 1_000);
        if (age != SocietyAgeBand.Adult)
        {
            var town = Assert.Single(state.Towns!);
            Assert.Equal("founded", town.FoundingState);
            var governance = Assert.IsType<TownGovernanceState>(town.Governance);
            Assert.Equal("all_adult", governance.Form);
            var adults = town.ResidentIds.Where(id => society.Inhabitants.Any(person =>
                person.Id == id && person.Status == SocietyInhabitantStatus.Active &&
                person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder))
                .Order(StringComparer.Ordinal).ToArray();
            var updated = TownGovernanceRules.Advance(governance, town.Id, state.WorldSeed,
                adults, society.WorldTick, state.WorldSystems!.Config.TicksPerDay);
            Assert.Equal(adults, updated.Members);
            Assert.DoesNotContain(actor, updated.Members);
            state = state with { Towns = [town with { Governance = updated }] };
        }
        var choices = new FoodCapacityTestFixture.Choices("collect_shared_food", "consume_food");
        using var world = FoodCapacityTestFixture.Restore(state, actor, choices);
        var observedTake = false;
        var started = world.WorldTick;
        for (var tick = 0; world.WorldTick - started < 40 && !world.ExportState().Events.Any(item => item.Kind == "food_consumed" && item.Detail == actor); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            if (!observedTake && world.Society.Inventory.Lots.Any(lot => lot.OwnerId == actor && lot.ProvenanceLotId == food.Id))
            {
                observedTake = true;
                var serving = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ProvenanceLotId == food.Id);
                Assert.Equal(1, serving.Quantity);
                Assert.Null(serving.ContainerLotId);
                if (world.WorldTick - started < 40)
                    await FoodCapacityTestFixture.AssertReplay(world, actor, "collect_shared_food", "consume_food");
            }
        }
        Assert.Contains(choices.Offers, offer => offer.Contains("collect_shared_food", StringComparer.Ordinal));
        Assert.True(observedTake);
        Assert.Single(world.ExportState().Events, item => item.Kind == "food_consumed" && item.Detail == actor);
        Assert.Equal(1, world.Society.Inventory.GetLot(food.Id).Quantity);
        if (!loose)
        {
            Assert.Equal("child-shared-pot", world.Society.Inventory.GetLot(food.Id).ContainerLotId);
            Assert.Equal((FoodCapacityTestFixture.Household, FoodCapacityTestFixture.House, 1),
                (world.Society.Inventory.GetLot("child-shared-pot").OwnerId,
                    world.Society.Inventory.GetLot("child-shared-pot").StorageBuildingId, world.Society.Inventory.GetLot("child-shared-pot").Quantity));
            Assert.Single(world.Society.Inventory.Events, item => item.Kind == "container_contents_taken" &&
                item.Detail.Contains("child-shared-pot:" + food.Id + ":1:" + actor, StringComparison.Ordinal));
        }
        if (age is SocietyAgeBand.Child or SocietyAgeBand.Adolescent)
            Assert.All(choices.Offers, offer => Assert.DoesNotContain("take_food_from_pot", offer));
        Assert.Contains(world.Society.Inventory.Reservations, reservation => reservation.OwnerId == actor &&
            reservation.Quantity == 1 && reservation.Purpose == "direct_consumption" && reservation.State == InventoryReservationState.Completed);
        await FoodCapacityTestFixture.AssertReplay(world, actor, "collect_shared_food", "consume_food");
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("broken")]
    [InlineData("spoiled")]
    [InlineData("full")]
    [InlineData("foreign")]
    public async Task SharedPotRefusalsPreserveActualContentsFamilyRightsAndReservations(string obstruction)
    {
        var (state, actor, _) = await FoodCapacityTestFixture.Generated("pot-only-food-capacity");
        var inventory = FoodCapacityTestFixture.WithoutPersonalCargo(state, actor);
        var food = inventory.Lots.First(lot => lot.OwnerId == FoodCapacityTestFixture.Household &&
            lot.StorageBuildingId == FoodCapacityTestFixture.House && InventoryContainerRules.IsFood(lot.ItemKind));
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => !(lot.OwnerId == FoodCapacityTestFixture.Household &&
                InventoryContainerRules.IsFood(lot.ItemKind) && lot.Id != food.Id))
                .Select(lot => lot.Id == food.Id ? lot with { Quantity = 2 } : lot).ToArray(),
        };
        var owner = FoodCapacityTestFixture.Household;
        var house = FoodCapacityTestFixture.House;
        if (obstruction == "foreign")
        {
            owner = "household:camp-beta";
            house = "first-town-house-b";
            inventory = InventoryFixture.Transfer(inventory, "foreign-pot-food-setup", FoodCapacityTestFixture.Household,
                owner, food.Id, 2, "test_setup", destinationStorageBuildingId: house);
        }
        inventory = InventoryFixture.AddLot(inventory, "refused-shared-pot", InventoryContainerRules.StoragePot,
            owner, 1, storageBuildingId: house);
        inventory = InventoryFixture.PutIntoContainer(inventory, "refused-shared-pot-fill", owner, "refused-shared-pot", food.Id, 2);
        if (obstruction == "reserved")
            inventory = InventoryFixture.Reserve(inventory, "shared-pot-child-reservation", owner, food.Id, 1,
                "capacity_control", inventory.WorldTick + 100);
        if (obstruction is "broken" or "spoiled")
            inventory = inventory with
            {
                Lots = inventory.Lots.Select(lot => obstruction == "broken" && lot.Id == "refused-shared-pot"
                    ? lot with { ConditionBasisPoints = 0 }
                    : obstruction == "spoiled" && lot.Id == food.Id ? lot with { FreshnessBasisPoints = 0 } : lot).ToArray(),
            };
        if (obstruction == "full")
        {
            inventory = InventoryFixture.AddLot(inventory, "unmovable-full-pot-cargo", "stone", actor, 8);
            inventory = InventoryFixture.Reserve(inventory, "full-pot-cargo-reservation", actor,
                "unmovable-full-pot-cargo", 8, "capacity_control", inventory.WorldTick + 100);
        }
        state = FoodCapacityTestFixture.WithFullness(FoodCapacityTestFixture.WithInventory(state, inventory), actor, 1_000);
        var choices = new FoodCapacityTestFixture.Choices("collect_shared_food", "make_room_for_food", "consume_food");
        using var world = FoodCapacityTestFixture.Restore(state, actor, choices);
        for (var tick = 0; tick < 8; tick++) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.All(choices.Offers, offer => Assert.DoesNotContain("collect_shared_food", offer));
        Assert.Equal((owner, house, "refused-shared-pot", 2),
            (world.Society.Inventory.GetLot(food.Id).OwnerId, world.Society.Inventory.GetLot(food.Id).StorageBuildingId,
                world.Society.Inventory.GetLot(food.Id).ContainerLotId, world.Society.Inventory.GetLot(food.Id).Quantity));
        Assert.DoesNotContain(world.Society.Inventory.Events, item => item.Kind == "container_contents_taken");
        if (obstruction == "reserved")
            Assert.Equal(InventoryReservationState.Reserved, world.Society.Inventory.GetReservation("shared-pot-child-reservation").State);
        await FoodCapacityTestFixture.AssertReplay(world, actor, "collect_shared_food", "make_room_for_food", "consume_food");
    }
}
