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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HouseExpansionCannotReserveHouseholdFields(bool foreignFields)
    {
        using var seed = PreparedWorld("first-town-house-a", out var actor, out var house);
        var state = seed.ExportState();
        var fertility = new LandFertility(state.Map, state.WorldSeed);
        var occupied = state.WorldSimulation!.Buildings.Where(building => building.InstanceId != house.InstanceId)
            .SelectMany(building => WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(
                definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(state.Map.Resources.Select(resource => resource.Position)).Concat(state.RoadTiles!)
            .Concat(state.Map.CampObjects.Select(item => item.Position)).ToHashSet();
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            Enumerable.Range(-1, 3).SelectMany(dy => Enumerable.Range(-1, 3).Select(dx => new GridPoint(point.X + dx, point.Y + dy)))
                .All(tile => fertility.CanFarm(tile) && !occupied.Contains(tile) &&
                    !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == tile)));
        var relocated = house with { Position = site, Entrance = null };
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor ? person with { Position = site } : person).ToArray(),
            WorldSimulation = state.WorldSimulation with
            {
                Buildings = state.WorldSimulation.Buildings.Select(building => building.InstanceId == house.InstanceId ? relocated : building).ToArray(),
            },
            Towns = state.Towns!.Select(town => town.Id == house.TownId ? town with
            {
                BorderTiles = TownBorderRules.ExpandForBuilding(state.Map, town, site, 2, 2),
            } : town).ToArray(),
        };
        using var clear = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        Assert.True(clear.StartBuildingExpansion(actor, house.InstanceId).Applied);
        var owner = foreignFields ? state.Society.Society.Households.First(item => item.Id != house.HouseholdId).Id : house.HouseholdId!;
        state = state with
        {
            Fields = new[] { new GridPoint(site.X, site.Y - 1), new(site.X - 1, site.Y),
                new(site.X + 1, site.Y), new(site.X, site.Y + 1) }
                .OrderBy(point => point.Y).ThenBy(point => point.X)
                .Select(point => new FarmFieldState(point, owner, FarmFieldStage.Prepared)).ToArray(),
        };
        using var blocked = ReloadState(state);
        var before = PrivateWorldRuntimeCodec.Encode(blocked.ExportState());
        Assert.False(blocked.StartBuildingExpansion(actor, house.InstanceId).Applied);
        Assert.Empty(blocked.WorldSimulation.BuildingExpansions ?? []);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(blocked.ExportState()));
        using var restored = Reload(blocked);
        Assert.Equal(state.Fields, restored.Fields);
        Assert.False(restored.StartBuildingExpansion(actor, house.InstanceId).Applied);
    }

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
        var site = FindUnaffiliatedOpenTile(state);
        const string actor = "agent:00000000000000000000000000000098";
        var household = seed.AddAgent(actor, site);
        Assert.Equal("household:" + actor, household);
        state = seed.ExportState();
        var definition = seed.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
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
    public async Task FullHouseExpansionAddsPlacesWithoutChangingItsIdentityStockOrCookingJobAcrossReload()
    {
        using var world = PreparedWorld("first-town-house-a", out var actor, out var building, householdWood: 10);
        var newResident = "agent:" + Guid.NewGuid().ToString("N");
        Assert.Equal(building.HouseholdId, world.AddAgent(newResident, building.Position));
        var full = new OwnerWorldObservationStore(world).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(3, full.PermanentResidentCount);
        Assert.Equal(3, full.ResidentLimit);
        Assert.False(full.HasDominantFamily);
        var recipe = world.WorldContent.Recipes.Single(item => item.LocalId == "house-meal");
        var cooking = world.StartProduction(recipe.CanonicalId, building.InstanceId, actor);
        Assert.True(cooking.Applied, cooking.Failure);
        var started = world.StartBuildingExpansion(actor, building.InstanceId);
        Assert.True(started.Applied, started.Failure);
        var initial = world.ExportState();
        var expansion = Assert.Single(initial.WorldSimulation!.BuildingExpansions!);
        var initialHouseView = new OwnerWorldObservationStore(world).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(3, initialHouseView.ResidentLimit);
        Assert.Equal(3, initialHouseView.PermanentResidentCount);
        var stored = initial.Society.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == building.InstanceId).Sum(lot => lot.Quantity);
        Assert.True(stored * 100 < BuildingStorageRules.Capacity(
            world.WorldContent.Buildings.Single(item => item.CanonicalId == building.DefinitionId), building) *
            BuildingStorageRules.NearlyFullPercent,
            "This House needs resident places while its storage is below the expansion threshold.");
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
        var stillSmall = new OwnerWorldObservationStore(restored).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(3, stillSmall.ResidentLimit);
        Assert.Equal(3, stillSmall.PermanentResidentCount);
        Assert.True((await restored.AdvanceOneTickAsync()).Advanced);

        var after = restored.WorldSimulation.Buildings.Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(building.DefinitionId, after.DefinitionId);
        Assert.Equal(building.HouseholdId, after.HouseholdId);
        Assert.Equal(building.PlacedTick, after.PlacedTick);
        Assert.Equal(1, after.Footprint!.Revision);
        Assert.Equal(2, after.Footprint.Width * after.Footprint.Height);
        Assert.Equal(WorldProductionJobState.Completed, restored.WorldSimulation.ProductionJobs.Single(item => item.JobId == cooking.JobId).State);
        Assert.Equal(WorldProductionJobState.Completed, restored.WorldSimulation.BuildingExpansions!.Single().State);
        var expandedCapacity = new OwnerWorldObservationStore(restored).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(6, expandedCapacity.ResidentLimit);
        Assert.Equal(3, expandedCapacity.PermanentResidentCount);
        var worker = restored.Inhabitants.Single(person => person.InhabitantId == actor);
        var learned = Assert.Single(worker.Skills ?? [], skill => skill.Kind == SettlementSkillKind.Building);
        Assert.Equal(expansion.CompletionTick, learned.LearnedTick);
        Assert.Null(learned.TeacherId);
        Assert.Equal(1, worker.Proficiency!.Building);
        Assert.All(expansion.InputReservationIds, id => Assert.Equal(InventoryReservationState.Completed,
            restored.Society.Inventory.GetReservation(id).State));
        Assert.Equal(initial.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity) - 5,
            restored.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.All(initial.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId), lot =>
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
        Assert.Equal(6, visible.ResidentLimit);
        Assert.Equal(3, visible.PermanentResidentCount);
        Assert.Equal(after.Footprint.Width, visible.Width);
        Assert.Equal(after.Footprint.Height, visible.Height);
        Assert.Equal(1, visible.FootprintRevision);
    }

    [Fact]
    public async Task ExpansionMaterialsBeyondFullHouseStockAreDeliveredAcrossReload()
    {
        using var seed = PreparedWorld("first-town-house-a", out var actor, out var building, householdWood: 10);
        var householdId = building.HouseholdId!;
        var firstExtraResident = "agent:" + Guid.NewGuid().ToString("N");
        Assert.Equal(householdId, seed.AddAgent(firstExtraResident, building.Position));
        var state = seed.ExportState();
        var warehouse = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-warehouse");
        var otherBuildings = state.WorldSimulation.Buildings.Where(item => item.InstanceId != building.InstanceId)
            .SelectMany(item => WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(
                definition => definition.CanonicalId == item.DefinitionId), item)).ToHashSet();
        var occupied = otherBuildings.Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position)).Concat(state.RoadTiles!).ToHashSet();
        var houseDefinition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == building.DefinitionId);
        var towns = state.Towns!;
        bool IsClearFootprint(IEnumerable<GridPoint> footprint, GridPoint source) => footprint.All(point =>
            state.Map.IsBuildable(point) && !occupied.Contains(point) && point != source &&
            !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == point) &&
            !towns.Where(town => town.Id != building.TownId).Any(town => town.BorderTiles.Contains(point)));
        GridPoint[] Rectangle(GridPoint position, int width, int height) =>
            Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width)
                .Select(x => new GridPoint(position.X + x, position.Y + y))).ToArray();

        var placement = state.Map.FootNeighbors(warehouse.Position)
            .Where(camp => state.Map.CanFootStep(warehouse.Position, camp) && !occupied.Contains(camp) &&
                !state.Inhabitants.Any(person => person.Position == camp))
            .SelectMany(camp => state.Map.FootNeighbors(camp)
                .Where(site => Math.Abs(site.X - camp.X) + Math.Abs(site.Y - camp.Y) == 1 &&
                    state.Map.CanFootStep(site, camp) && state.Map.IsBuildable(site) && !occupied.Contains(site) &&
                    !state.Inhabitants.Any(person => person.InhabitantId != actor && person.Position == site))
                .Select(site => (Camp: camp, Site: site)))
            .First(pair =>
            {
                var firstShapes = new (GridPoint Position, int Width, int Height, GridPoint[] Tiles)[]
                {
                    (new(pair.Site.X, pair.Site.Y - 1), 1, 2,
                        [new(pair.Site.X, pair.Site.Y - 1), pair.Site]),
                    (pair.Site, 1, 2, [pair.Site, new(pair.Site.X, pair.Site.Y + 1)]),
                    (new(pair.Site.X - 1, pair.Site.Y), 2, 1,
                        [new(pair.Site.X - 1, pair.Site.Y), pair.Site]),
                    (pair.Site, 2, 1, [pair.Site, new(pair.Site.X + 1, pair.Site.Y)]),
                };
                var firstShape = firstShapes.FirstOrDefault(shape => IsClearFootprint(shape.Tiles, pair.Camp));
                if (firstShape.Tiles is null) return false;
                var secondPositions = firstShape.Width == 1
                    ? new[] { new GridPoint(firstShape.Position.X - 1, firstShape.Position.Y), firstShape.Position }
                    : new[] { new GridPoint(firstShape.Position.X, firstShape.Position.Y - 1), firstShape.Position };
                return secondPositions.Any(position => IsClearFootprint(Rectangle(position, 2, 2), pair.Camp));
            });
        var camp = placement.Camp;
        var houseSite = placement.Site;
        Assert.True(state.Map.CanFootStep(houseSite, camp));
        Assert.Equal(1, state.Map.FootDistance(houseSite, camp));
        building = building with { Position = houseSite, Entrance = null };
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = houseSite }
                : person).ToArray(),
            WorldSimulation = state.WorldSimulation with
            {
                Buildings = state.WorldSimulation.Buildings.Select(item => item.InstanceId == building.InstanceId
                    ? building : item).ToArray(),
            },
            Towns = state.Towns!.Select(town => town.Id == building.TownId
                ? town with
                {
                    BorderTiles = TownBorderRules.ExpandForBuilding(state.Map, town, houseSite,
                    houseDefinition.Width + 2, houseDefinition.Height + 2)
                }
                : town).ToArray(),
        };
        var provider = new IdleProvider("expand_building:" + building.InstanceId);
        using var firstStage = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? provider : new IdleProvider());
        var initialCapacity = HouseResidentCapacityRules.Calculate(
            firstStage.Society.Inhabitants.Where(person => person.HouseholdId == householdId),
            houseDefinition.Width, houseDefinition.Height);
        Assert.Equal(initialCapacity.Limit, initialCapacity.ResidentCount);
        var firstStageWood = firstStage.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        Assert.True(firstStage.StartBuildingExpansion(actor, building.InstanceId).Applied);
        for (var tick = 0; tick < 20; tick++) Assert.True((await firstStage.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(firstStageWood - 4,
            firstStage.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));

        var intermediate = firstStage.ExportState();
        building = intermediate.WorldSimulation!.Buildings.Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(2, building.Footprint!.Width * building.Footprint.Height);
        var extraResidents = new[]
        {
            firstExtraResident,
            "agent:" + Guid.NewGuid().ToString("N"),
            "agent:" + Guid.NewGuid().ToString("N"),
            "agent:" + Guid.NewGuid().ToString("N"),
        };
        for (var index = 1; index < extraResidents.Length; index++)
            Assert.Equal(householdId, firstStage.AddAgent(extraResidents[index], building.Position));
        intermediate = firstStage.ExportState();
        var futurePositions = building.Footprint.Width == 1
            ? new[] { new GridPoint(building.Position.X - 1, building.Position.Y), building.Position }
            : new[] { new GridPoint(building.Position.X, building.Position.Y - 1), building.Position };
        var futureBuildingTiles = futurePositions.SelectMany(position => Enumerable.Range(0, 2)
            .SelectMany(y => Enumerable.Range(0, 2)
                .Select(x => new GridPoint(position.X + x, position.Y + y)))).ToHashSet();
        var occupiedAfterFirst = intermediate.WorldSimulation!.Buildings
            .SelectMany(item => WorldContentSimulationRules.Footprint(intermediate.WorldContent!.Buildings.Single(
                definition => definition.CanonicalId == item.DefinitionId), item))
            .Concat(intermediate.Map.Resources.Select(resource => resource.Position))
            .Concat(intermediate.Map.CampObjects.Select(item => item.Position))
            .Concat(intermediate.RoadTiles!).ToHashSet();
        var firstAwayPosition = intermediate.Map.FootNeighbors(building.Position)
            .Where(step => intermediate.Map.CanFootStep(building.Position, step) && !occupiedAfterFirst.Contains(step) &&
                !intermediate.Inhabitants.Any(person => !extraResidents.Contains(person.InhabitantId) && person.Position == step))
            .SelectMany(step => intermediate.Map.FootNeighbors(step).Where(point =>
                intermediate.Map.FootDistance(building.Position, point) == 2 && intermediate.Map.CanFootStep(step, point) &&
                intermediate.Map.IsBuildable(point) && !occupiedAfterFirst.Contains(point) &&
                !futureBuildingTiles.Contains(point) && point != camp &&
                !intermediate.Inhabitants.Any(person => !extraResidents.Contains(person.InhabitantId) && person.Position == point)))
            .Distinct().First();
        var otherAwayPositions = intermediate.Map.Tiles.Select(tile => tile.Position)
            .Where(point => intermediate.Map.IsBuildable(point) && !occupiedAfterFirst.Contains(point) &&
                !futureBuildingTiles.Contains(point) && point != camp && point != firstAwayPosition &&
                !intermediate.Inhabitants.Any(person => !extraResidents.Contains(person.InhabitantId) && person.Position == point) &&
                intermediate.Map.FootDistance(point, camp) >= 6)
            .OrderBy(point => point.Y).ThenBy(point => point.X).Take(3).ToArray();
        Assert.Equal(3, otherAwayPositions.Length);
        var residentPositions = new[] { firstAwayPosition }.Concat(otherAwayPositions).ToArray();
        intermediate = intermediate with
        {
            Inhabitants = intermediate.Inhabitants.Select(person => extraResidents.Contains(person.InhabitantId)
                ? person with { Position = residentPositions[Array.IndexOf(extraResidents, person.InhabitantId)] }
                : person.InhabitantId == actor ? person with { Position = building.Position } : person).ToArray(),
        };
        Assert.Equal(2, intermediate.Map.FootDistance(building.Position, firstAwayPosition));
        var effective = BuildingStorageRules.EffectiveDefinition(houseDefinition, building);
        var fullHousehold = HouseResidentCapacityRules.Calculate(
            intermediate.Society.Society.Inhabitants.Where(person => person.HouseholdId == householdId),
            effective.Width, effective.Height);
        Assert.Equal(fullHousehold.Limit, fullHousehold.ResidentCount);
        var inventory = intermediate.Society.Society.Inventory;
        var capacity = BuildingStorageRules.Capacity(houseDefinition, building)!.Value;
        Assert.Equal(8, BuildingStorageRules.ExpansionCosts(houseDefinition, building,
            new BuildingFootprintRevision(2, 2, 2)).Single(cost => cost.ResourceId == "wood").Amount);
        var houseStock = inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId).Sum(lot => lot.Quantity);
        Assert.InRange(houseStock, 0, capacity - 1);
        inventory = InventoryFixture.AddLot(inventory, "expansion-camp-wood", "wood", householdId, 2,
            groundPosition: new InventoryGroundPosition(camp.X, camp.Y));
        inventory = InventoryFixture.AddLot(inventory, "expansion-house-capacity-fill", "stone", householdId,
            capacity - houseStock, storageBuildingId: building.InstanceId);
        Assert.Equal(capacity, inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId).Sum(lot => lot.Quantity));
        intermediate = intermediate with
        {
            Society = intermediate.Society with { Society = intermediate.Society.Society with { Inventory = inventory } },
            Towns = intermediate.Towns!.Select(town => town.Id == building.TownId
                ? town with
                {
                    BorderTiles = TownBorderRules.ExpandForBuilding(intermediate.Map, town, building.Position,
                    effective.Width + 2, effective.Height + 2)
                }
                : town).ToArray(),
        };
        Assert.InRange(intermediate.Map.FootDistance(building.Position, camp), 1, 3);
        var initialHouseStock = inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId)
            .Select(lot => (lot.Id, lot.OwnerId, lot.ItemKind, lot.Quantity)).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        var secondStageWood = inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(intermediate)),
            id => id == actor ? provider : new IdleProvider());

        for (var tick = 0; tick < 100 && !world.Society.Inventory.Lots.Any(lot =>
                 lot.OwnerId == actor && lot.DeliveryBuildingId == building.InstanceId && lot.ItemKind == "wood"); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(provider.Seen, request => request.Observation.Candidates.Any(candidate =>
            candidate.Id == "expand_building:" + building.InstanceId));
        var transitLot = Assert.Single(world.Society.Inventory.Lots, lot =>
            lot.OwnerId == actor && lot.DeliveryBuildingId == building.InstanceId && lot.ItemKind == "wood");
        Assert.Equal(2, transitLot.Quantity);
        Assert.Equal(camp, world.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal) &&
            item.Detail.EndsWith(":building_expansion_material", StringComparison.Ordinal));
        Assert.Equal(secondStageWood, world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Equal(initialHouseStock, world.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == building.InstanceId)
            .Select(lot => (lot.Id, lot.OwnerId, lot.ItemKind, lot.Quantity)).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray());

        var transitBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(transitBytes),
            id => id == actor ? provider : new IdleProvider());
        Assert.Equal(transitBytes, PrivateWorldRuntimeCodec.Encode(resumed.ExportState()));
        for (var tick = 0; tick < 100 && resumed.WorldSimulation.BuildingExpansions?.Any(job =>
                 job.State == WorldProductionJobState.Running) != true; tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);

        var expansion = Assert.Single(resumed.WorldSimulation.BuildingExpansions!, job => job.State == WorldProductionJobState.Running);
        Assert.Equal(building.Position, resumed.Inhabitants.Single(person => person.InhabitantId == actor).Position);
        Assert.Contains(resumed.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal) &&
            item.Detail.EndsWith(":building_expansion_delivery", StringComparison.Ordinal));
        Assert.All(expansion.InputReservationIds, id => Assert.Equal(InventoryReservationState.Reserved,
            resumed.Society.Inventory.GetReservation(id).State));
        var deliveredWood = Assert.Single(resumed.Society.Inventory.Lots, lot =>
            lot.OwnerId == householdId && lot.ItemKind == "wood" && lot.GroundPosition ==
                new InventoryGroundPosition(building.Position.X, building.Position.Y));
        Assert.Equal(2, deliveredWood.Quantity);
        Assert.Null(deliveredWood.StorageBuildingId);
        Assert.All(initialHouseStock, original =>
        {
            var current = resumed.Society.Inventory.GetLot(original.Id);
            Assert.Equal(original.OwnerId, current.OwnerId);
            Assert.Equal(original.Quantity, current.Quantity);
            Assert.Equal(building.InstanceId, current.StorageBuildingId);
        });

        using var working = Reload(resumed);
        for (var tick = 0; tick < 20; tick++) Assert.True((await working.AdvanceOneTickAsync()).Advanced);
        var completedExpansion = working.WorldSimulation.BuildingExpansions!.Single(job => job.JobId == expansion.JobId);
        Assert.Equal(WorldProductionJobState.Completed, completedExpansion.State);
        Assert.Equal(secondStageWood - 8, working.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        var consumedByLot = expansion.InputReservationIds.Select(working.Society.Inventory.GetReservation)
            .GroupBy(reservation => reservation.LotId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(reservation => reservation.Quantity), StringComparer.Ordinal);
        Assert.All(initialHouseStock, original =>
        {
            var expectedQuantity = original.Quantity - consumedByLot.GetValueOrDefault(original.Id);
            var current = working.Society.Inventory.Lots.SingleOrDefault(lot => lot.Id == original.Id);
            if (expectedQuantity == 0)
            {
                Assert.Null(current);
                return;
            }

            Assert.NotNull(current);
            Assert.Equal(original.OwnerId, current.OwnerId);
            Assert.Equal(expectedQuantity, current.Quantity);
            Assert.Equal(building.InstanceId, current.StorageBuildingId);
        });
        var visibleHouse = new OwnerWorldObservationStore(working).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal(12, visibleHouse.ResidentLimit);
        Assert.Equal(6, visibleHouse.PermanentResidentCount);
        working.Validate();
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
        var placement = FindUnaffiliatedOpenTile(state);
        Assert.Equal("household:" + outsiderId, world.AddAgent(outsiderId, placement));
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
        Assert.NotEqual(VegetationCover.Forest, state.Map.VegetationAt(new GridPoint(house.Position.X, house.Position.Y + 1)));
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
    public async Task ExpansionPickupWaitsForCarrySpaceAndReservesRoomForOtherInboundDelivery()
    {
        using var seed = PreparedWorld("first-town-house-a", out var actor, out var house, householdWood: 4);
        var state = seed.ExportState();
        var householdId = house.HouseholdId!;
        var sourcePosition = state.Map.FootNeighbors(house.Position).First(point =>
            state.Map.CanFootStep(house.Position, point) && state.Map.IsBuildable(point) &&
            !state.Inhabitants.Any(person => person.Position == point) &&
            !state.WorldSimulation!.Buildings.Any(item => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == item.DefinitionId), item).Contains(point)) &&
            !state.Map.Resources.Any(resource => resource.Position == point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) && !state.RoadTiles!.Contains(point));
        var houseDefinition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        var houseCapacity = BuildingStorageRules.Capacity(houseDefinition, house)!.Value;
        var inventory = state.Society.Society.Inventory;
        var houseWood = inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId && lot.ItemKind == "wood").ToArray();
        Assert.Equal(4, houseWood.Sum(lot => lot.Quantity));
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => houseWood.Any(wood => wood.Id == lot.Id)
                ? lot with { StorageBuildingId = null, GroundPosition = new InventoryGroundPosition(sourcePosition.X, sourcePosition.Y) }
                : lot).ToArray(),
        };
        var currentHouseStock = inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
        Assert.True(houseCapacity - currentHouseStock > 1);
        inventory = InventoryFixture.AddLot(inventory, "expansion-inbound-room-fill", "stone", householdId,
            houseCapacity - currentHouseStock - 1, storageBuildingId: house.InstanceId);

        var otherCarrier = state.Society.Society.Inhabitants.First(person => person.HouseholdId == householdId &&
            person.Id != actor && PersonalEquipmentRules.FreeCapacity(inventory, person.Id,
                state.Inhabitants.Single(physical => physical.InhabitantId == person.Id).Equipment) > 0);
        inventory = InventoryFixture.AddLot(inventory, "expansion-other-inbound", "stone", otherCarrier.Id, 1);
        inventory = inventory with
        {
            Lots = inventory.Lots.Select(lot => lot.Id == "expansion-other-inbound"
                ? lot with { DeliveryBuildingId = house.InstanceId }
                : lot).ToArray(),
        };

        var physicalActor = state.Inhabitants.Single(person => person.InhabitantId == actor);
        var freeCarry = PersonalEquipmentRules.FreeCapacity(inventory, actor, physicalActor.Equipment);
        Assert.True(freeCarry > 0);
        inventory = InventoryFixture.AddLot(inventory, "expansion-carry-capacity-fill", "stone", actor, freeCarry);
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(inventory, actor, physicalActor.Equipment));
        var totalWood = inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { LastDecisionContext = null, Project = null }
                : person).ToArray(),
        };

        var blockedProvider = new IdleProvider("expand_building:" + house.InstanceId);
        using (var full = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
                   id => id == actor ? blockedProvider : new IdleProvider()))
        {
            for (var tick = 0; tick < 40 && blockedProvider.Seen.Count == 0; tick++)
                Assert.True((await full.AdvanceOneTickAsync()).Advanced);
            Assert.NotEmpty(blockedProvider.Seen);
            Assert.DoesNotContain(blockedProvider.Seen.SelectMany(request => request.Observation.Candidates),
                candidate => candidate.Id == "expand_building:" + house.InstanceId);
            Assert.Empty(full.WorldSimulation.BuildingExpansions ?? []);
        }

        inventory = InventoryFixture.Transfer(inventory, "expansion-open-one-carry-slot", actor, householdId,
            "expansion-carry-capacity-fill", 1, "test_free_expansion_capacity",
            destinationGroundPosition: new InventoryGroundPosition(sourcePosition.X, sourcePosition.Y));
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
        };
        var provider = new IdleProvider("expand_building:" + house.InstanceId);
        using var ready = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? provider : new IdleProvider());
        for (var tick = 0; tick < 40 && !ready.Society.Inventory.Lots.Any(lot =>
                 lot.OwnerId == actor && lot.ItemKind == "wood" && lot.DeliveryBuildingId == house.InstanceId); tick++)
            Assert.True((await ready.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(provider.Seen.SelectMany(request => request.Observation.Candidates),
            candidate => candidate.Id == "expand_building:" + house.InstanceId);
        var transit = Assert.Single(ready.Society.Inventory.Lots, lot =>
            lot.OwnerId == actor && lot.ItemKind == "wood" && lot.DeliveryBuildingId == house.InstanceId);
        Assert.Equal(1, transit.Quantity);
        Assert.Equal(0, PersonalEquipmentRules.FreeCapacity(ready.Society.Inventory, actor,
            ready.Inhabitants.Single(person => person.InhabitantId == actor).Equipment));

        var transitBytes = PrivateWorldRuntimeCodec.Encode(ready.ExportState());
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(transitBytes),
            id => id == actor ? provider : new IdleProvider());
        for (var tick = 0; tick < 5 && !resumed.Society.Inventory.Lots.Any(lot =>
                 lot.OwnerId == householdId && lot.ItemKind == "wood" &&
                 lot.GroundPosition == new InventoryGroundPosition(house.Position.X, house.Position.Y)); tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);

        var delivered = Assert.Single(resumed.Society.Inventory.Lots, lot =>
            lot.OwnerId == householdId && lot.ItemKind == "wood" &&
            lot.GroundPosition == new InventoryGroundPosition(house.Position.X, house.Position.Y));
        Assert.Equal(1, delivered.Quantity);
        Assert.Equal(totalWood, resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Equal(houseCapacity - 1, resumed.Society.Inventory.Lots
            .Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity));
        Assert.Equal(house.InstanceId, resumed.Society.Inventory.GetLot("expansion-other-inbound").DeliveryBuildingId);
        Assert.Equal(1, resumed.Society.Inventory.GetLot("expansion-other-inbound").Quantity);
        resumed.Validate();
    }

    [Fact]
    public async Task AHouseholdCanExpandFromItsAccessibleTownWarehouseWithoutMapWood()
    {
        using var seed = PreparedWorld("first-town-house-a", out var actor, out var house);
        var state = seed.ExportState();
        var householdId = house.HouseholdId!;
        var townId = house.TownId!;
        var warehouse = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-warehouse");
        Assert.Equal(townId, warehouse.TownId);
        Assert.Contains(actor, state.Towns!.Single(item => item.Id == townId).ResidentIds);

        var members = state.Society.Society.GetHousehold(householdId).MemberIds.ToHashSet(StringComparer.Ordinal);
        var otherHouse = state.WorldSimulation.Buildings.First(item => item.HouseholdId != householdId &&
            item.HouseholdId is not null && state.WorldContent!.Buildings.Single(definition =>
                definition.CanonicalId == item.DefinitionId).Tags.Contains("house", StringComparer.Ordinal));
        var otherHouseholdId = otherHouse.HouseholdId!;
        var inventory = state.Society.Society.Inventory;
        var obsoleteWoodLots = inventory.Lots.Where(lot => lot.ItemKind == "wood" &&
                (lot.OwnerId == householdId || members.Contains(lot.OwnerId) ||
                 lot.OwnerId == townId && lot.StorageBuildingId == warehouse.InstanceId))
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => !obsoleteWoodLots.Contains(lot.Id)).ToArray(),
            Reservations = inventory.Reservations.Where(reservation => !obsoleteWoodLots.Contains(reservation.LotId)).ToArray(),
        };
        const string townWoodId = "house-expansion-town-warehouse-wood";
        inventory = InventoryFixture.AddLot(inventory, townWoodId, "wood", townId, 4,
            storageBuildingId: warehouse.InstanceId);
        const string foreignWoodId = "house-expansion-foreign-house-wood";
        inventory = InventoryFixture.AddLot(inventory, foreignWoodId, "wood", otherHouseholdId, 4,
            storageBuildingId: otherHouse.InstanceId);

        var houseDefinition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
        var capacity = BuildingStorageRules.Capacity(houseDefinition, house)!.Value;
        var nearlyFullTarget = (capacity * BuildingStorageRules.NearlyFullPercent + 99) / 100;
        var currentHouseStock = inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity);
        var addedHouseStock = Math.Max(0, nearlyFullTarget - currentHouseStock);
        if (addedHouseStock > 0)
            inventory = InventoryFixture.AddLot(inventory, "house-expansion-near-full-stone", "stone", householdId,
                addedHouseStock, storageBuildingId: house.InstanceId);
        Assert.True(capacity - inventory.Lots.Where(lot => lot.StorageBuildingId == house.InstanceId).Sum(lot => lot.Quantity) >= 4);

        var woodResourceIds = state.Map.Resources.Where(resource => resource.Kind is "wood" or "construction")
            .Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Resources = state.Resources.Select(resource => woodResourceIds.Contains(resource.ResourceId)
                ? resource with { State = ResourceState.Depleted }
                : resource).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Ecology = state.WorldSystems.Ecology with
                {
                    Resources = state.WorldSystems.Ecology.Resources.Select(resource =>
                        resource.Kind is "wood" or "construction"
                            ? resource with { Quantity = 0, State = EcologyResourceState.Depleted, NextRegenerationDay = 100 }
                            : resource).ToArray(),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = warehouse.Position, LastDecisionContext = null, Project = null }
                : person).ToArray(),
        };

        var provider = new IdleProvider("expand_building:" + house.InstanceId);
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            id => id == actor ? provider : new IdleProvider());
        GridPoint? pickupPosition = null;
        for (var tick = 0; tick < 20 && !provider.Seen.SelectMany(request => request.Observation.Candidates)
                 .Any(candidate => candidate.Id == "expand_building:" + house.InstanceId); tick++)
        {
            var step = await world.AdvanceOneTickAsync();
            Assert.True(step.Advanced);
            if (step.Events.Any(item => item.Kind == "building_expansion_material_picked_up" &&
                    item.Detail.Contains(townWoodId, StringComparison.Ordinal)))
                pickupPosition = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        }
        Assert.Contains(provider.Seen.SelectMany(request => request.Observation.Candidates),
            candidate => candidate.Id == "expand_building:" + house.InstanceId);

        for (var tick = 0; tick < 100 && pickupPosition is null; tick++)
        {
            var step = await world.AdvanceOneTickAsync();
            Assert.True(step.Advanced);
            if (!step.Events.Any(item => item.Kind == "building_expansion_material_picked_up" &&
                    item.Detail.Contains(townWoodId, StringComparison.Ordinal))) continue;
            pickupPosition = world.Inhabitants.Single(person => person.InhabitantId == actor).Position;
            break;
        }
        if (pickupPosition is null)
        {
            var person = world.Inhabitants.Single(item => item.InhabitantId == actor);
            var recentEvents = string.Join(";", world.ExportState().Events.TakeLast(12)
                .Select(item => $"{item.Kind}:{item.Detail}"));
            var cargo = string.Join(";", world.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor)
                .Select(lot => $"{lot.ItemKind}={lot.Quantity}@{lot.StorageBuildingId ?? lot.DeliveryBuildingId ?? "ground"}"));
            Assert.Fail($"No expansion pickup after 100 ticks; position={person.Position}; cargo={cargo}; recent={recentEvents}");
        }
        Assert.NotEqual(house.Position, pickupPosition);
        var transit = Assert.Single(world.Society.Inventory.Lots, lot => lot.OwnerId == actor &&
            lot.ItemKind == "wood" && lot.DeliveryBuildingId == house.InstanceId);
        Assert.InRange(transit.Quantity, 1, 4);
        var woodBeforeCompletion = world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
        using var resumed = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())),
            id => id == actor ? provider : new IdleProvider());
        var completed = false;
        for (var tick = 0; tick < 500 && !completed; tick++)
        {
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
            completed = (resumed.WorldSimulation.BuildingExpansions ?? []).Any(job =>
                job.BuildingInstanceId == house.InstanceId && job.State == WorldProductionJobState.Completed);
        }
        Assert.True(completed, "Town warehouse stock should travel to the House and complete its expansion after reload.");
        Assert.Equal(woodBeforeCompletion - 4,
            resumed.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Equal(4, resumed.Society.Inventory.GetLot(foreignWoodId).Quantity);
        Assert.Equal(otherHouse.InstanceId, resumed.Society.Inventory.GetLot(foreignWoodId).StorageBuildingId);
        resumed.Validate();
    }
    [Fact]
    public async Task AnInvitedGuestCanReachAnOccupiedHouseDuringAStorm()
    {
        using var seed = PreparedWorld("first-town-house-a", out var actor, out var house);
        var guest = seed.Society.Inhabitants.First(person => person.HouseholdId != house.HouseholdId).Id;
        Assert.True(seed.SetHouseGuestInvitation(actor, house.InstanceId, guest, true).Applied);
        var state = seed.ExportState();
        Assert.NotEqual(VegetationCover.Forest, state.Map.VegetationAt(new GridPoint(house.Position.X + 1, house.Position.Y)));
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

    private static PrivateWorldRuntime PreparedWorld(string buildingId, out string actor, out PlacedBuilding building,
        int householdWood = 52)
    {
        using var seed = NormalPathWorld.CreateGenerated("expansion-world", _ => new IdleProvider());
        var state = seed.ExportState();
        building = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == buildingId);
        var targetBuilding = building;
        var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == targetBuilding.DefinitionId);
        actor = state.Society.Society.Inhabitants.First(person => targetBuilding.HouseholdId is null || person.HouseholdId == targetBuilding.HouseholdId).Id;
        var actorId = actor;
        var inventory = state.Society.Society.Inventory;
        inventory = InventoryFixture.AddLot(inventory, "expansion-wood", "wood", building.HouseholdId ?? building.TownId!,
            building.HouseholdId is null ? 210 : householdWood, storageBuildingId: building.InstanceId);
        if (building.HouseholdId is null)
            inventory = InventoryFixture.AddLot(inventory, "expansion-stone", "stone", building.TownId!, 8, storageBuildingId: building.InstanceId);
        var probeInventory = inventory;
        if (building.HouseholdId is not null && householdWood < 52)
            probeInventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "expansion-wood", "wood",
                building.HouseholdId, 52, storageBuildingId: building.InstanceId);
        var occupied = state.WorldSimulation.Buildings.Where(item => item.InstanceId != buildingId)
            .SelectMany(item => WorldContentSimulationRules.Footprint(state.WorldContent.Buildings.Single(value => value.CanonicalId == item.DefinitionId), item))
            .Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.RoadTiles!)
            .Concat(state.Fields!.Select(field => field.Position))
            .Concat(state.Inhabitants.Where(person => person.InhabitantId != actorId).Select(person => person.Position))
            .Concat(state.Towns!.Where(town => town.Id != targetBuilding.TownId).SelectMany(town => town.BorderTiles))
            .ToHashSet();
        var candidates = state.Map.Tiles.Select(tile => tile.Position)
            .Where(position => state.Map.FootDistance(targetBuilding.Position, position) < int.MaxValue)
            .OrderBy(position => state.Map.FootDistance(targetBuilding.Position, position))
            .ThenBy(position => position.Y).ThenBy(position => position.X);
        var connectedRoadTiles = ReachableFootTiles(state.Map, state.RoadTiles![0]);
        PrivateWorldRuntimeState? prepared = null;
        foreach (var site in candidates)
        {
            var guestApproaches = new[]
            {
                new GridPoint(site.X + 1, site.Y),
                new GridPoint(site.X, site.Y + 1),
            };
            if (guestApproaches.Any(point => state.Map.VegetationAt(point) == VegetationCover.Forest)) continue;
            var entrances = state.Map.FootNeighbors(site).Where(point =>
                    WorldContentSimulationRules.IsEntrance(definition, site, point) && state.Map.CanFootStep(site, point) &&
                    !occupied.Contains(point) && connectedRoadTiles.Contains(point))
                .OrderBy(point => Math.Abs(point.X - site.X) + Math.Abs(point.Y - site.Y))
                .ThenBy(point => point.Y).ThenBy(point => point.X).ToArray();
            if (entrances.Length == 0) continue;
            var envelope = Enumerable.Range(-1, 4).SelectMany(dy => Enumerable.Range(-1, 4)
                .Select(dx => new GridPoint(site.X + dx, site.Y + dy))).ToArray();
            if (!state.Map.IsBuildable(site) || envelope.Any(point =>
                    !state.Map.IsBuildable(point) || occupied.Contains(point))) continue;

            var placed = targetBuilding with { Position = site, Entrance = entrances[0] };
            var candidate = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actorId
                    ? person with { Position = site, HungerBasisPoints = 9_000 } : person).ToArray(),
                WorldSimulation = state.WorldSimulation with
                {
                    Buildings = state.WorldSimulation.Buildings.Select(item => item.InstanceId == buildingId ? placed : item).ToArray(),
                },
                Society = state.Society with { Society = state.Society.Society with { Inventory = probeInventory } },
                Towns = state.Towns!.Select(town => town.Id == placed.TownId
                    ? town with
                    {
                        BorderTiles = TownBorderRules.ExpandForBuilding(state.Map, town, site,
                        definition.Width + 2, definition.Height + 2)
                    } : town).ToArray(),
            };
            using var probe = PrivateWorldRuntime.Restore(
                PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(candidate)), _ => new IdleProvider());
            if (!probe.StartBuildingExpansion(actorId, buildingId).Applied) continue;
            prepared = candidate with
            {
                Society = candidate.Society with
                {
                    Society = candidate.Society.Society with { Inventory = inventory },
                },
            };
            building = placed;
            break;
        }

        Assert.NotNull(prepared);
        return PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(prepared)),
            _ => new IdleProvider());
    }

    private static GridPoint FindUnaffiliatedOpenTile(PrivateWorldRuntimeState state)
    {
        var buildings = state.WorldSimulation!.Buildings.SelectMany(building =>
            WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(item =>
                item.CanonicalId == building.DefinitionId), building));
        var unavailable = buildings.Concat(state.Map.Resources.Select(item => item.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.RoadTiles!)
            .Concat(state.Fields!.Select(item => item.Position))
            .Concat(state.Inhabitants.Select(item => item.Position))
            .Concat(state.Towns!.SelectMany(item => item.BorderTiles))
            .ToHashSet();
        return state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.IsBuildable(point) && !unavailable.Contains(point));
    }

    private static HashSet<GridPoint> ReachableFootTiles(SeededMap map, GridPoint start)
    {
        var visited = new HashSet<GridPoint> { start };
        var pending = new Queue<GridPoint>();
        pending.Enqueue(start);
        while (pending.TryDequeue(out var current))
        {
            foreach (var next in map.FootNeighbors(current))
            {
                if (!map.CanFootStep(current, next) || !visited.Add(next)) continue;
                pending.Enqueue(next);
            }
        }
        return visited;
    }

    private static PrivateWorldRuntime Reload(PrivateWorldRuntime world) => PrivateWorldRuntime.Restore(
        PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new IdleProvider());

    private static PrivateWorldRuntime ReloadState(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(
        PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new IdleProvider());

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
