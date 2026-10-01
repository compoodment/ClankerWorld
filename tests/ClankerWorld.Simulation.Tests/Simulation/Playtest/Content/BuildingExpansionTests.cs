using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class BuildingExpansionTests
{
    [Fact]
    public async Task AHouseCanGrowAgainToTwoByTwoWithoutCreatingAnotherHouse()
    {
        using var seed = PreparedWorld("first-town-house-a", out var actor, out var original);
        Assert.True(seed.StartBuildingExpansion(actor, original.InstanceId).Applied);
        for (var tick = 0; tick < 20; tick++) Assert.True((await seed.AdvanceOneTickAsync()).Advanced);
        var state = seed.ExportState();
        var firstWorker = state.Inhabitants.Single(person => person.InhabitantId == actor);
        var firstSkill = Assert.Single(firstWorker.Skills ?? [], skill => skill.Kind == SettlementSkillKind.Building);
        Assert.Equal(1, firstWorker.Proficiency!.Building);
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == original.InstanceId);
        var stored = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = house.Position } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "second-expansion-wood", "wood", house.HouseholdId!,
                    104 - stored, storageBuildingId: house.InstanceId),
                }
            },
        };
        using var growing = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new IdleProvider());
        var started = growing.StartBuildingExpansion(actor, house.InstanceId);
        Assert.True(started.Applied, started.Failure);
        using var reloaded = Reload(growing);
        for (var tick = 0; tick < 20; tick++) Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        var expanded = reloaded.WorldSimulation.Buildings.Single(item => item.InstanceId == house.InstanceId);
        Assert.Equal(new BuildingFootprintRevision(2, 2, 2), expanded.Footprint);
        Assert.Single(reloaded.WorldSimulation.Buildings, item => item.HouseholdId == house.HouseholdId && item.DefinitionId == house.DefinitionId);
        Assert.Equal(256, new OwnerWorldObservationStore(reloaded).GetSnapshot().PlacedBuildings.Single(item => item.InstanceId == house.InstanceId).StorageCapacity);
        Assert.All(reloaded.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId), lot => Assert.Equal(house.HouseholdId, lot.OwnerId));
        var secondWorker = reloaded.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Equal(2, secondWorker.Proficiency!.Building);
        Assert.Equal(firstSkill, Assert.Single(secondWorker.Skills ?? [], skill => skill.Kind == SettlementSkillKind.Building));
        Assert.False(reloaded.StartBuildingExpansion(actor, house.InstanceId).Applied);
    }

    [Fact]
    public void ACurrentSaveCannotRemoveOrReassignAHouseWhileItHoldsStockAndJobs()
    {
        using var world = PreparedWorld("first-town-house-a", out var actor, out var house);
        Assert.True(world.StartBuildingExpansion(actor, house.InstanceId).Applied);
        var state = world.ExportState();
        var missing = state with
        {
            WorldSimulation = state.WorldSimulation! with { Buildings = state.WorldSimulation.Buildings.Where(item => item.InstanceId != house.InstanceId).ToArray() },
            Towns = state.Towns!.Select(town => town with { AssignedBuildingIds = town.AssignedBuildingIds.Where(id => id != house.InstanceId).ToArray() }).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(missing));
        var otherHousehold = state.Society.Society.Households.First(item => item.Id != house.HouseholdId).Id;
        var reassigned = state with
        {
            WorldSimulation = state.WorldSimulation! with
            {
                Buildings = state.WorldSimulation.Buildings.Select(item => item.InstanceId == house.InstanceId ? item with { HouseholdId = otherHousehold } : item).ToArray(),
            }
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(reassigned));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(state), PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task AHelperFromAnotherHouseholdCanDeliverMaterialsWithoutStockAccess()
    {
        using var seed = PreparedWorld("first-town-house-a", out var actor, out var house);
        var helper = seed.Society.Inhabitants.First(person => person.HouseholdId != house.HouseholdId).Id;
        var definition = seed.WorldContent.Buildings.Single(item => item.LocalId == "blacksmith-1x2");
        var wood = definition.BuildCosts.Single(item => item.ResourceId == "wood").Amount;
        var state = seed.ExportState();
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId != house.HouseholdId || lot.ItemKind != "wood").ToArray(),
        };
        inventory = InventoryFixture.AddLot(inventory, "helper-building-wood", "wood", helper, wood);
        var helperPosition = state.Map.FootNeighbors(house.Position).First(point =>
            state.Map.IsBuildable(point) && !state.Inhabitants.Any(person => person.Position == point));
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == helper
                ? person with { Position = helperPosition, HungerBasisPoints = 9_000, LastDecisionContext = null }
                : person.InhabitantId == actor ? person with
                {
                    Project = new SettlementProject(TownConstructionCandidateIds.Building(definition.CanonicalId, house.Position),
                        definition.DisplayName, seed.WorldTick, "acquiring"),
                } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == helper ? new IdleProvider("assist:wood") : new IdleProvider());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var delivered = world.Society.Inventory.GetLot("helper-building-wood");
        Assert.Equal(house.HouseholdId, delivered.OwnerId);
        Assert.Equal(house.InstanceId, delivered.StorageBuildingId);
        Assert.Equal(wood, delivered.Quantity);
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "house-meal");
        Assert.False(world.StartProduction(recipe.CanonicalId, house.InstanceId, helper).Applied);
        using var reloaded = Reload(world);
        Assert.Equal(delivered, reloaded.Society.Inventory.GetLot(delivered.Id));
    }

    [Fact]
    public async Task AHouseholdWithoutAHouseBuildsItsFirstOneFromPersonallyCarriedMaterials()
    {
        using var seed = PreparedWorld("first-town-house-a", out _, out _);
        var state = seed.ExportState();
        var site = state.Map.Tiles.First(tile => state.Map.IsBuildable(tile.Position) &&
            !state.Map.Resources.Any(resource => resource.Position == tile.Position) &&
            !state.Towns!.SelectMany(town => town.BorderTiles).Contains(tile.Position) &&
            !state.Inhabitants.Any(person => person.Position == tile.Position)).Position;
        const string actor = "agent:00000000000000000000000000000098";
        var household = seed.AddAgent(actor, site);
        var definition = seed.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
        state = seed.ExportState();
        var inventory = state.Society.Society.Inventory;
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "first-house-" + cost.ResourceId, cost.ResourceId, actor, cost.Amount);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with
            {
                HungerBasisPoints = 9_000,
                Project = new SettlementProject(TownConstructionCandidateIds.Building(definition.CanonicalId, site),
                    definition.DisplayName, seed.WorldTick, "working", 10),
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new IdleProvider());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var house = Assert.Single(world.WorldSimulation.Buildings, item => item.HouseholdId == household);
        Assert.Equal(site, house.Position);
        Assert.Equal("completed", world.Inhabitants.Single(person => person.InhabitantId == actor).Project!.Stage);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id.StartsWith("first-house-", StringComparison.Ordinal));
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == household && lot.StorageBuildingId is null);
        using var reloaded = Reload(world);
        Assert.Contains(reloaded.WorldSimulation.Buildings, item => item == house);
    }

    [Fact]
    public async Task HouseExpansionKeepsItsIdentityStockAndCookingJobAcrossReload()
    {
        using var world = PreparedWorld("first-town-house-a", out var actor, out var building);
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "house-meal");
        var cooking = world.StartProduction(recipe.CanonicalId, building.InstanceId, actor);
        Assert.True(cooking.Applied, cooking.Failure);
        var started = world.StartBuildingExpansion(actor, building.InstanceId);
        Assert.True(started.Applied, started.Failure);
        var initial = world.ExportState();
        var expansion = Assert.Single(initial.WorldSimulation!.BuildingExpansions!);
        var initialWorker = initial.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Empty(initialWorker.Skills ?? []);
        Assert.Equal(0, initialWorker.Proficiency?.Building ?? 0);
        var cookingJob = Assert.Single(initial.WorldSimulation.ProductionJobs);
        Assert.Equal(WorldProductionJobState.Running, cookingJob.State);
        Assert.All(expansion.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            initial.Society.Society.Inventory.GetReservation(id).State));
        using var restored = Reload(world);
        for (var tick = 0; tick < 19; tick++) Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        var working = restored.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.DoesNotContain(working.Skills ?? [], skill => skill.Kind == SettlementSkillKind.Building);
        Assert.Equal(0, working.Proficiency?.Building ?? 0);
        Assert.Equal(WorldProductionJobState.Running, restored.WorldSimulation.BuildingExpansions!.Single().State);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);

        var after = restored.WorldSimulation.Buildings.Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(building.DefinitionId, after.DefinitionId);
        Assert.Equal(building.HouseholdId, after.HouseholdId);
        Assert.Equal(building.PlacedTick, after.PlacedTick);
        Assert.Equal(1, after.Footprint!.Revision);
        Assert.Equal(2, after.Footprint.Width * after.Footprint.Height);
        Assert.Equal(WorldProductionJobState.Completed, restored.WorldSimulation.ProductionJobs.Single(item => item.JobId == cooking.JobId).State);
        Assert.Equal(WorldProductionJobState.Completed, restored.WorldSimulation.BuildingExpansions!.Single().State);
        var worker = restored.Inhabitants.Single(person => person.InhabitantId == actor);
        var learned = Assert.Single(worker.Skills ?? [], skill => skill.Kind == SettlementSkillKind.Building);
        Assert.Equal(expansion.CompletionTick, learned.LearnedTick);
        Assert.Null(learned.TeacherId);
        Assert.Equal(1, worker.Proficiency!.Building);
        Assert.All(expansion.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
            restored.Society.Inventory.GetReservation(id).State));
        Assert.Equal(initial.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity) - 5,
            restored.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        var recipeInputs = cookingJob.InputReservationIds.Select(initial.Society.Society.Inventory.GetReservation).Select(item => item.LotId).ToHashSet(StringComparer.Ordinal);
        Assert.All(cookingJob.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed, restored.Society.Inventory.GetReservation(id).State));
        Assert.Equal(initial.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId && lot.ItemKind == "potatoes").Sum(lot => lot.Quantity) - 2,
            restored.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId && lot.ItemKind == "potatoes").Sum(lot => lot.Quantity));
        Assert.All(initial.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId && !recipeInputs.Contains(lot.Id)), lot =>
        {
            var actual = restored.Society.Inventory.GetLot(lot.Id);
            Assert.Equal(lot.OwnerId, actual.OwnerId);
            Assert.Equal(lot.StorageBuildingId, actual.StorageBuildingId);
        });
        using var completed = Reload(restored);
        var savedWorker = completed.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Equal(learned, Assert.Single(savedWorker.Skills ?? [], skill => skill.Kind == SettlementSkillKind.Building));
        Assert.Equal(worker.Proficiency, savedWorker.Proficiency);
        var visible = new OwnerWorldObservationStore(completed).GetSnapshot().PlacedBuildings.Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(128, visible.StorageCapacity);
        Assert.Equal(after.Footprint.Width, visible.Width);
        Assert.Equal(after.Footprint.Height, visible.Height);
        Assert.Equal(1, visible.FootprintRevision);
    }

    [Fact]
    public async Task AStaleExpansionReleasesMaterialsAndKeepsTheOriginalBuilding()
    {
        using var world = PreparedWorld("first-town-house-a", out var actor, out var building);
        Assert.True(world.StartBuildingExpansion(actor, building.InstanceId).Applied);
        var saved = world.ExportState();
        var initialWorker = saved.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Empty(initialWorker.Skills ?? []);
        var job = saved.WorldSimulation!.BuildingExpansions!.Single();
        var extra = Enumerable.Range(0, job.TargetFootprint.Height).SelectMany(dy =>
                Enumerable.Range(0, job.TargetFootprint.Width).Select(dx => new GridPoint(job.TargetPosition.X + dx, job.TargetPosition.Y + dy)))
            .First(point => point != building.Position);
        saved = saved with { RoadTiles = saved.RoadTiles!.Append(extra).Distinct().OrderBy(point => point.Y).ThenBy(point => point.X).ToArray() };
        using var stale = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(saved)), _ => new IdleProvider());
        Assert.True((await stale.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(building, stale.WorldSimulation.Buildings.Single(item => item.InstanceId == building.InstanceId));
        Assert.Equal(WorldProductionJobState.Cancelled, stale.WorldSimulation.BuildingExpansions!.Single().State);
        Assert.Contains("Road", stale.WorldSimulation.BuildingExpansions!.Single().Failure);
        Assert.All(job.InputReservationIds, id => Assert.Equal(InventoryReservationState.Released, stale.Society.Inventory.GetReservation(id).State));
        Assert.Equal(saved.Society.Society.Inventory.Lots.Sum(lot => lot.Quantity), stale.Society.Inventory.Lots.Sum(lot => lot.Quantity));
        var worker = stale.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Empty(worker.Skills ?? []);
        Assert.Equal(initialWorker.Proficiency, worker.Proficiency);
        using var cancelled = Reload(stale);
        Assert.Equal(building, cancelled.WorldSimulation.Buildings.Single(item => item.InstanceId == building.InstanceId));
        var savedWorker = cancelled.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Empty(savedWorker.Skills ?? []);
        Assert.Equal(initialWorker.Proficiency, savedWorker.Proficiency);
    }

    [Fact]
    public async Task WarehouseExpansionRequiresCurrentResidencyAndKeepsCommunalStock()
    {
        using var world = PreparedWorld("first-town-warehouse", out var actor, out var building);
        var started = world.StartBuildingExpansion(actor, building.InstanceId);
        Assert.True(started.Applied, started.Failure);
        Assert.Empty(world.Inhabitants.Single(person => person.InhabitantId == actor).Skills ?? []);
        using var reloaded = Reload(world);
        for (var tick = 0; tick < 20; tick++) Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(WorldProductionJobState.Completed, reloaded.WorldSimulation.BuildingExpansions!.Single().State);
        Assert.Null(reloaded.WorldSimulation.BuildingExpansions!.Single().Failure);
        var worker = reloaded.Inhabitants.Single(person => person.InhabitantId == actor);
        var learned = Assert.Single(worker.Skills ?? [], skill => skill.Kind == SettlementSkillKind.Building);
        Assert.Equal(reloaded.WorldSimulation.BuildingExpansions!.Single().CompletionTick, learned.LearnedTick);
        Assert.Null(learned.TeacherId);
        Assert.Equal(1, worker.Proficiency!.Building);
        var expanded = reloaded.WorldSimulation.Buildings.Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(6, expanded.Footprint!.Width * expanded.Footprint.Height);
        Assert.Equal(building.TownId, expanded.TownId);
        Assert.Null(expanded.HouseholdId);
        var snapshot = new OwnerWorldObservationStore(reloaded).GetSnapshot().PlacedBuildings.Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(384, snapshot.StorageCapacity);
        Assert.All(reloaded.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId), lot =>
        {
            Assert.Equal(building.TownId, lot.OwnerId);
            Assert.False(lot.ItemKind is "food" or "grain" or "flour");
        });
        using var completed = Reload(reloaded);
        var savedWorker = completed.Inhabitants.Single(person => person.InhabitantId == actor);
        Assert.Equal(learned, Assert.Single(savedWorker.Skills ?? [], skill => skill.Kind == SettlementSkillKind.Building));
        Assert.Equal(worker.Proficiency, savedWorker.Proficiency);
        var state = world.ExportState();
        var outsiderId = "agent:00000000000000000000000000000099";
        var placement = state.Map.Tiles.First(tile => state.Map.IsBuildable(tile.Position) &&
            !state.Map.Resources.Any(resource => resource.Position == tile.Position) &&
            !state.Towns!.SelectMany(town => town.BorderTiles).Contains(tile.Position) &&
            !world.Inhabitants.Any(person => person.Position == tile.Position)).Position;
        world.AddAgent(outsiderId, placement);
        Assert.False(world.StartBuildingExpansion(outsiderId, building.InstanceId).Applied);
    }

    [Fact]
    public void AnotherHouseholdCannotExpandTheHouseOrUseItsGuestInvitations()
    {
        using var world = PreparedWorld("first-town-house-a", out var actor, out var building);
        var outsider = world.Society.Inhabitants.First(person => person.HouseholdId != building.HouseholdId).Id;
        Assert.False(world.StartBuildingExpansion(outsider, building.InstanceId).Applied);
        Assert.False(world.SetHouseGuestInvitation(outsider, building.InstanceId, actor, true).Applied);
        Assert.True(world.SetHouseGuestInvitation(actor, building.InstanceId, outsider, true).Applied);
        using var restored = Reload(world);
        var visible = new OwnerWorldObservationStore(restored).GetSnapshot().PlacedBuildings.Single(item => item.InstanceId == building.InstanceId);
        Assert.Contains(restored.Society.Inhabitants.Single(person => person.Id == outsider).Name, visible.InvitedGuests!);
        var recipe = restored.WorldContent.Recipes.Single(item => item.LocalId == "house-meal");
        Assert.False(restored.StartProduction(recipe.CanonicalId, building.InstanceId, outsider).Applied);
        Assert.True(restored.SetHouseGuestInvitation(actor, building.InstanceId, outsider, false).Applied);
        using var revoked = Reload(restored);
        Assert.Empty(new OwnerWorldObservationStore(revoked).GetSnapshot().PlacedBuildings.Single(item => item.InstanceId == building.InstanceId).InvitedGuests!);
    }

    [Fact]
    public async Task InvitedGuestReceivesOnlyStormRefugeUntilAnyAdultHouseholdMemberRevokesIt()
    {
        using var world = PreparedWorld("first-town-house-a", out var actor, out var house);
        var guest = world.Society.Inhabitants.First(person => person.HouseholdId != house.HouseholdId).Id;
        Assert.True(world.SetHouseGuestInvitation(actor, house.InstanceId, guest, true).Applied);
        var state = world.ExportState();
        state = state with
        {
            Survival = new SettlementSurvivalState(0, []),
            WorldSimulation = state.WorldSimulation! with
            {
                Buildings = state.WorldSimulation.Buildings.Select(item => item.InstanceId == house.InstanceId
                    ? item with { Footprint = new BuildingFootprintRevision(1, 2, 1) } : item).ToArray(),
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == guest
                ? person with { Position = new GridPoint(house.Position.X, house.Position.Y + 1), HungerBasisPoints = 9_000, Survival = new SurvivalCondition(4_000) }
                : person.InhabitantId == actor ? person with { Position = new GridPoint(house.Position.X + 1, house.Position.Y) } : person).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Storm },
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 1, 0)).ToArray(),
                },
            },
        };
        using var invited = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new IdleProvider());
        using var revoked = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var otherAdult = revoked.Society.Inhabitants.First(person => person.Id != actor && person.HouseholdId == house.HouseholdId).Id;
        Assert.True(revoked.SetHouseGuestInvitation(otherAdult, house.InstanceId, guest, false).Applied);
        Assert.False(invited.StartProduction(invited.WorldContent.Recipes.Single(item => item.LocalId == "house-meal").CanonicalId,
            house.InstanceId, guest).Applied);
        Assert.True((await invited.AdvanceOneTickAsync()).Advanced);
        Assert.True((await revoked.AdvanceOneTickAsync()).Advanced);
        Assert.True(invited.Inhabitants.Single(person => person.InhabitantId == guest).Survival!.WarmthBasisPoints >
            revoked.Inhabitants.Single(person => person.InhabitantId == guest).Survival!.WarmthBasisPoints);
        Assert.Equal(invited.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity),
            revoked.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
    }

    [Fact]
    public async Task NormalChoicesOfferExpansionOnlyForNearlyFullBuildingsTheAdultMayUse()
    {
        using var seed = PreparedWorld("first-town-house-a", out var actor, out var house);
        var preferred = new IdleProvider("expand_building:" + house.InstanceId);
        using var world = PrivateWorldRuntime.Restore(seed.ExportState(), id => id == actor ? preferred : new IdleProvider());
        for (var tick = 0; tick < 20 && world.WorldSimulation.BuildingExpansions is not { Count: > 0 }; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(preferred.Seen, request => request.Observation.Candidates.Any(candidate => candidate.Id == "expand_building:" + house.InstanceId));
        Assert.Single(world.WorldSimulation.BuildingExpansions!);
        var underfull = seed.ExportState();
        underfull = underfull with
        {
            Society = underfull.Society with
            {
                Society = underfull.Society.Society with
                { Inventory = underfull.Society.Society.Inventory with { Lots = underfull.Society.Society.Inventory.Lots.Where(lot => lot.Id != "expansion-wood").ToArray() } }
            }
        };
        using var empty = PrivateWorldRuntime.Restore(underfull, _ => new IdleProvider());
        var rejected = empty.StartBuildingExpansion(actor, house.InstanceId);
        Assert.False(rejected.Applied);
        Assert.Contains("nearly full", rejected.Failure);
        Assert.Empty(empty.WorldSimulation.BuildingExpansions ?? []);
    }

    [Fact]
    public async Task AnInvitedGuestCanReachAnOccupiedHouseDuringAStorm()
    {
        using var seed = PreparedWorld("first-town-house-a", out var actor, out var house);
        var guest = seed.Society.Inhabitants.First(person => person.HouseholdId != house.HouseholdId).Id;
        Assert.True(seed.SetHouseGuestInvitation(actor, house.InstanceId, guest, true).Applied);
        var state = seed.ExportState();
        state = state with
        {
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == guest
                ? person with { Position = new GridPoint(house.Position.X + 1, house.Position.Y), HungerBasisPoints = 9_000, Survival = new SurvivalCondition(4_000) }
                : person).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Storm },
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 0, 0, 0, 1, 0)).ToArray(),
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => new IdleProvider(id == guest ? "seek_warmth" : "safe_idle"));
        for (var tick = 0; tick < 12 && world.Inhabitants.Single(person => person.InhabitantId == guest).Position != house.Position; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(house.Position, world.Inhabitants.Single(person => person.InhabitantId == guest).Position);
        Assert.Equal(house.Position, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
    }

    private static PrivateWorldRuntime PreparedWorld(string buildingId, out string actor, out PlacedBuilding building)
    {
        using var seed = NormalPathWorld.CreateGenerated("expansion-world", _ => new IdleProvider());
        var state = seed.ExportState();
        building = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == buildingId);
        var targetBuilding = building;
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == targetBuilding.DefinitionId);
        var occupied = state.WorldSimulation.Buildings.Where(item => item.InstanceId != buildingId)
            .SelectMany(item => WorldContentSimulationRules.Footprint(state.WorldContent.Buildings.Single(value => value.CanonicalId == item.DefinitionId), item))
            .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(state.RoadTiles!).ToHashSet();
        var site = state.Map.Tiles.Select(tile => tile.Position).First(position =>
            Enumerable.Range(-1, 4).SelectMany(dy => Enumerable.Range(-1, 4).Select(dx => new GridPoint(position.X + dx, position.Y + dy)))
                .All(point => state.Map.IsBuildable(point) && !occupied.Contains(point)));
        building = building with { Position = site, Entrance = null };
        actor = state.Society.Society.Inhabitants.First(person => targetBuilding.HouseholdId is null || person.HouseholdId == targetBuilding.HouseholdId).Id;
        var actorId = actor;
        var placed = building;
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "expansion-wood", "wood", building.HouseholdId ?? building.TownId!,
            building.HouseholdId is null ? 210 : 20, storageBuildingId: building.InstanceId);
        if (building.HouseholdId is null)
            inventory = InventoryFixture.AddLot(inventory, "expansion-stone", "stone", building.TownId!, 8, storageBuildingId: building.InstanceId);
        else
        {
            inventory = InventoryFixture.AddLot(inventory, "expansion-potatoes", "potatoes", building.HouseholdId, 3, storageBuildingId: building.InstanceId);
            var stored = inventory.Lots.Where(lot => lot.StorageBuildingId == placed.InstanceId).Sum(lot => lot.Quantity);
            inventory = InventoryFixture.AddLot(inventory, "expansion-filler", "stone", building.HouseholdId, 60 - stored, storageBuildingId: building.InstanceId);
        }
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actorId
                ? person with { Position = site, HungerBasisPoints = 9_000 } : person).ToArray(),
            WorldSimulation = state.WorldSimulation with { Buildings = state.WorldSimulation.Buildings.Select(item => item.InstanceId == buildingId ? placed : item).ToArray() },
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Towns = state.Towns!.Select(town => town.Id == placed.TownId
                ? town with { BorderTiles = TownBorderRules.ExpandForBuilding(state.Map, town, site, definition.Width + 2, definition.Height + 2) } : town).ToArray(),
        };
        return PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new IdleProvider());
    }

    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world) => PrivateWorldRuntime.Restore(
        PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new IdleProvider());

    private sealed class IdleProvider(string preferred = "safe_idle") : IDecisionProvider
    {
        public List<CognitionDecisionRequest> Seen { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Seen.Add(request);
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == preferred)?.Id ?? "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                selected, 1, request.Observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected ? 1d : 0d)));
        }
    }
}
