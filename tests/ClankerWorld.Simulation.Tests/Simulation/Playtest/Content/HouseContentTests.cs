using System.Text.Json;
using System.Globalization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class HouseContentTests
{
    [Fact]
    public async Task AdultCarriesPersonalSpareFoodIntoOwnHouseWithoutLosingLastServing()
    {
        using var seed = new PrivateWorldRuntime("house-personal-food", _ => new IdleProvider(),
            startPace: WorldStartPace.FounderSetup);
        var founderPositions = new[]
        {
            new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2),
        };
        for (var index = 0; index < founderPositions.Length; index++)
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founderPositions[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 8; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var state = seed.ExportState();
        var actor = state.Society.Society.Inhabitants.First(person =>
            person.HouseholdId == "household:camp-alpha").Id;
        var camp = state.Map.GetObject("storage").Position;
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != actor)
            .Select(person => person.Position).ToHashSet();
        var reachable = new HashSet<GridPoint> { camp };
        var pending = new Queue<GridPoint>();
        pending.Enqueue(camp);
        while (pending.TryDequeue(out var current))
        {
            foreach (var next in state.Map.FootNeighbors(current))
            {
                if (occupied.Contains(next) ||
                    state.Map.IsDiagonalFootStep(current, next) &&
                    (occupied.Contains(new GridPoint(next.X, current.Y)) ||
                     occupied.Contains(new GridPoint(current.X, next.Y))))
                    continue;
                if (reachable.Add(next)) pending.Enqueue(next);
            }
        }
        var site = reachable.OrderBy(point => state.Map.FootDistance(camp, point))
            .ThenBy(point => point.Y).ThenBy(point => point.X).First(point =>
            state.Map.IsBuildable(point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.Position == point));
        var house = seed.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        var placed = seed.PlaceBuilding("food-home-alpha", house.CanonicalId, site, "household:camp-alpha");
        Assert.True(placed.Applied, placed.Failure);
        var foodBefore = HouseholdQuantity(seed, "household:camp-alpha", "food");
        state = seed.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = camp, HungerBasisPoints = 9_000 } : person).ToArray(),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
                        "food:personal-surplus", "food", actor, 4),
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state,
            id => id == actor ? new PreferredCandidateProvider("store_household_food") : new IdleProvider());
        for (var tick = 0; tick < 20 && !world.ExportState().Events.Any(item =>
                 item.Kind == "household_food_stored" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var delivery = world.ExportState().Events.Single(item => item.Kind == "household_food_stored" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        Assert.Equal(site, delivery.Position);
        Assert.Equal(1, world.Society.Inventory.GetLot("food:personal-surplus").Quantity);
        Assert.Equal(foodBefore + 3, HouseholdQuantity(world, "household:camp-alpha", "food"));
        var stored = new OwnerWorldObservationStore(world).GetSnapshot().PlacedBuildings
            .Single(building => building.InstanceId == "food-home-alpha").StoredItems!;
        Assert.Contains(stored, item => item.Kind == "food" && item.Quantity == 3);

        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Contains(new OwnerWorldObservationStore(restored).GetSnapshot().PlacedBuildings
            .Single(building => building.InstanceId == "food-home-alpha").StoredItems!,
            item => item.Kind == "food" && item.Quantity == 3);
    }

    [Fact]
    public async Task HouseMealUsesOnlyItsHouseholdsIngredientsAndKeepsTheOutputOnReload()
    {
        using var seed = new PrivateWorldRuntime("house-meal", _ => new IdleProvider(),
            startPace: WorldStartPace.FounderSetup);
        var founderPositions = new[]
        {
            new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2),
        };
        for (var index = 0; index < founderPositions.Length; index++)
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founderPositions[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 8; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var state = seed.ExportState();
        var alpha = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-alpha").Id;
        var beta = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-beta").Id;
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.IsBuildable(point) &&
            state.Map.FootDistance(point, state.Map.GetObject("storage").Position) >= 3 &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.Inhabitants.Any(person => person.InhabitantId != beta && person.Position == point));
        var house = seed.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        var recipe = seed.WorldContent.Recipes.Single(item => item.LocalId == "house-meal");
        Assert.True(seed.PlaceBuilding("meal-home-beta", house.CanonicalId, site, "household:camp-beta").Applied);
        state = seed.ExportState() with
        {
            Inhabitants = seed.ExportState().Inhabitants.Select(person => person.InhabitantId == beta
                ? person with { Position = site, HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        using (var unstocked = PrivateWorldRuntime.Restore(state, _ => new IdleProvider()))
        {
            var remote = unstocked.StartProduction(recipe.CanonicalId, "meal-home-beta", beta);
            Assert.False(remote.Applied);
            Assert.Contains("on-site", remote.Failure, StringComparison.Ordinal);
        }
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = state.Society.Society.Inventory with
                    {
                        Lots = state.Society.Society.Inventory.Lots.Select(lot =>
                            lot.OwnerId == "household:camp-beta" && (lot.ItemKind is "food" or "wood")
                                ? lot with { StorageBuildingId = "meal-home-beta" } : lot).ToArray(),
                    },
                },
            },
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        var alphaFood = HouseholdQuantity(world, "household:camp-alpha", "food");
        var betaFood = HouseholdQuantity(world, "household:camp-beta", "food");
        var betaWood = HouseholdQuantity(world, "household:camp-beta", "wood");
        var rejected = world.StartProduction(recipe.CanonicalId, "meal-home-beta", alpha);
        Assert.False(rejected.Applied);
        Assert.Contains("Only a member", rejected.Failure, StringComparison.Ordinal);
        var started = world.StartProduction(recipe.CanonicalId, "meal-home-beta", beta);
        Assert.True(started.Applied, started.Failure);
        Assert.All(world.WorldSimulation.ProductionJobs.Single(job => job.JobId == started.JobId)
            .InputReservationIds, reservationId =>
                Assert.Equal("household:camp-beta", world.Society.Inventory.GetReservation(reservationId).OwnerId));

        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var resumed = PrivateWorldRuntime.Restore(saved, _ => new IdleProvider());
        for (var tick = 0; tick < recipe.DurationTicks; tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(alphaFood, HouseholdQuantity(resumed, "household:camp-alpha", "food"));
        Assert.Equal(betaFood + 2, HouseholdQuantity(resumed, "household:camp-beta", "food"));
        Assert.Equal(betaWood - 1, HouseholdQuantity(resumed, "household:camp-beta", "wood"));
        Assert.Equal(WorldProductionJobState.Completed,
            resumed.WorldSimulation.ProductionJobs.Single(job => job.JobId == started.JobId).State);
        var cookedLotId = $"{started.JobId}:output:00";
        Assert.Equal("meal-home-beta", resumed.Society.Inventory.GetLot(cookedLotId).StorageBuildingId);
        var projected = new OwnerWorldObservationStore(resumed).GetSnapshot().PlacedBuildings
            .Single(building => building.InstanceId == "meal-home-beta");
        Assert.Contains(projected.StoredItems!, item => item.Kind == "food" && item.Quantity == betaFood + 2);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldPlacedBuilding>(
            JsonSerializer.Serialize(projected, options), options)!;
        Assert.Contains(client.StoredItems!, item => item.Kind == "food" && item.Quantity == betaFood + 2);

        var finished = resumed.ExportState();
        var invalid = finished with
        {
            Society = finished.Society with
            {
                Society = finished.Society.Society with
                {
                    Inventory = finished.Society.Society.Inventory with
                    {
                        Lots = finished.Society.Society.Inventory.Lots.Select(lot => lot.Id == cookedLotId
                            ? lot with { StorageBuildingId = "missing-house" } : lot).ToArray(),
                    },
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalid));

        var otherBeta = finished.Society.Society.Inhabitants.First(person =>
            person.HouseholdId == "household:camp-beta" && person.Id != beta).Id;
        var houseApproach = finished.Map.FootNeighbors(site).First(point =>
            finished.Inhabitants.All(person => person.InhabitantId == beta || person.Position != point));
        var pickupState = finished with
        {
            Inhabitants = finished.Inhabitants.Select(person => person.InhabitantId == beta
                ? person with { Position = houseApproach, HungerBasisPoints = 2_000 }
                : person.InhabitantId == otherBeta ? person with { Position = site }
                : person).ToArray(),
        };
        using var pickup = PrivateWorldRuntime.Restore(pickupState,
            _ => new PreferredCandidateProvider("collect_shared_food"));
        for (var tick = 0; tick < 40 && !pickup.ExportState().Events.Any(item =>
                 item.Kind == "household_food_collected" && item.Detail.StartsWith(beta + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await pickup.AdvanceOneTickAsync()).Advanced);
        var pickupEvents = pickup.ExportState().Events;
        var collection = pickupEvents.FirstOrDefault(item =>
            item.Kind == "household_food_collected" && item.Detail.StartsWith(beta + ":", StringComparison.Ordinal));
        Assert.NotNull(collection);
        Assert.NotNull(collection.Position);
        Assert.Equal(site, collection.Position!.Value);
        Assert.Equal(site, pickup.Inhabitants.Single(person => person.InhabitantId == otherBeta).Position);
        Assert.Contains(pickup.Society.Inventory.Lots, lot => lot.OwnerId == beta &&
            lot.ProvenanceLotId == cookedLotId && lot.StorageBuildingId is null);
    }

    [Fact]
    public async Task HouseholdStockIsCarriedFromCampBeforeItAppearsAtTheHouse()
    {
        using var seed = new PrivateWorldRuntime("house-haul", _ => new IdleProvider(),
            startPace: WorldStartPace.FounderSetup);
        var founderPositions = new[]
        {
            new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2),
        };
        for (var index = 0; index < founderPositions.Length; index++)
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founderPositions[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 8; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var initial = seed.ExportState();
        var actor = initial.Society.Society.Inhabitants.First(person =>
            person.HouseholdId == "household:camp-alpha").Id;
        var camp = initial.Map.GetObject("storage").Position;
        var occupied = initial.Inhabitants.Where(person => person.InhabitantId != actor)
            .Select(person => person.Position).ToHashSet();
        var reachable = new HashSet<GridPoint> { camp };
        var pending = new Queue<GridPoint>();
        pending.Enqueue(camp);
        while (pending.TryDequeue(out var current))
        {
            foreach (var next in initial.Map.FootNeighbors(current))
            {
                if (occupied.Contains(next) ||
                    initial.Map.IsDiagonalFootStep(current, next) &&
                    (occupied.Contains(new GridPoint(next.X, current.Y)) ||
                     occupied.Contains(new GridPoint(current.X, next.Y))))
                    continue;
                if (reachable.Add(next)) pending.Enqueue(next);
            }
        }
        var site = reachable.OrderBy(point => initial.Map.FootDistance(camp, point))
            .ThenBy(point => point.Y).ThenBy(point => point.X).First(point =>
            initial.Map.IsBuildable(point) &&
            !initial.Map.CampObjects.Any(item => item.Position == point) &&
            !initial.Map.Resources.Any(item => item.Position == point) &&
            !initial.Inhabitants.Any(person => person.Position == point));
        var house = seed.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        Assert.True(seed.PlaceBuilding("haul-home-alpha", house.CanonicalId, site, "household:camp-alpha").Applied);
        var staged = seed.ExportState();
        var foodBefore = staged.Society.Society.Inventory.Lots
            .Where(lot => lot.OwnerId == "household:camp-alpha" && lot.ItemKind == "food")
            .Sum(lot => lot.Quantity);
        staged = staged with
        {
            Inhabitants = staged.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = camp, HungerBasisPoints = 9_000 } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(staged,
            id => id == actor ? new PreferredCandidateProvider("haul_household_stock") : new IdleProvider());
        for (var tick = 0; tick < 20 && !world.ExportState().Events.Any(item =>
                 item.Kind == "household_stock_picked_up" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "household_stock_picked_up" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        var carried = world.Society.Inventory.Lots.Single(lot =>
            lot.OwnerId == actor && lot.DeliveryBuildingId == "haul-home-alpha");
        Assert.Null(carried.StorageBuildingId);
        Assert.Empty(new OwnerWorldObservationStore(world).GetSnapshot().PlacedBuildings
            .Single(building => building.InstanceId == "haul-home-alpha").StoredItems!);

        var midJourney = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var invalid = midJourney with
        {
            Society = midJourney.Society with
            {
                Society = midJourney.Society.Society with
                {
                    Inventory = midJourney.Society.Society.Inventory with
                    {
                        Lots = midJourney.Society.Society.Inventory.Lots.Select(lot => lot.Id == carried.Id
                            ? lot with { DeliveryBuildingId = "missing-house" } : lot).ToArray(),
                    },
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(invalid));

        using var resumed = PrivateWorldRuntime.Restore(midJourney,
            id => id == actor ? new PreferredCandidateProvider("haul_household_stock") : new IdleProvider());
        for (var tick = 0; tick < 20 && !resumed.ExportState().Events.Any(item =>
                 item.Kind == "household_stock_delivered" && item.Detail.StartsWith(actor + ":", StringComparison.Ordinal)); tick++)
            Assert.True((await resumed.AdvanceOneTickAsync()).Advanced);
        var delivery = resumed.ExportState().Events.First(item => item.Kind == "household_stock_delivered" &&
            item.Detail.StartsWith(actor + ":", StringComparison.Ordinal));
        Assert.Equal(site, delivery.Position);
        Assert.Equal("haul-home-alpha", resumed.Society.Inventory.GetLot(carried.Id).StorageBuildingId);
        Assert.Null(resumed.Society.Inventory.GetLot(carried.Id).DeliveryBuildingId);
        Assert.Equal(foodBefore, resumed.Society.Inventory.Lots
            .Where(lot => lot.OwnerId == "household:camp-alpha" && lot.ItemKind == "food")
            .Sum(lot => lot.Quantity));
        Assert.Contains(new OwnerWorldObservationStore(resumed).GetSnapshot().PlacedBuildings
            .Single(building => building.InstanceId == "haul-home-alpha").StoredItems!,
            item => item.Kind == "food" && item.Quantity == carried.Quantity);
    }

    [Fact]
    public async Task AnotherHouseholdDoesNotReceiveHouseRefugeInSnow()
    {
        using var seed = new PrivateWorldRuntime("house-refuge", _ => new IdleProvider(),
            startPace: WorldStartPace.FounderSetup);
        var founderPositions = new[]
        {
            new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2),
        };
        for (var index = 0; index < founderPositions.Length; index++)
            seed.PlaceFounder("founder:" + (index + 1).ToString("x32", CultureInfo.InvariantCulture), founderPositions[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        for (var tick = 0; tick < 6; tick++)
            Assert.True((await seed.AdvanceOneTickAsync()).Advanced);

        var state = seed.ExportState();
        var alpha = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-alpha").Id;
        var beta = state.Society.Society.Inhabitants.First(person => person.HouseholdId == "household:camp-beta").Id;
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point =>
        {
            var neighbor = point with { X = point.X + 1 };
            return state.Map.IsBuildable(point) && state.Map.IsPassable(neighbor) &&
                !state.Map.CampObjects.Any(item => item.Position == point) &&
                !state.Map.Resources.Any(item => item.Position == point) &&
                !state.Inhabitants.Any(person => person.InhabitantId != alpha && person.InhabitantId != beta &&
                    (person.Position == point || person.Position == neighbor));
        });
        var house = seed.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        Assert.True(seed.PlaceBuilding("refuge-alpha", house.CanonicalId, site, "household:camp-alpha").Applied);
        state = seed.ExportState();
        var snowy = state.WorldSystems!;
        var profiles = Enum.GetValues<SeasonKind>().Select(season =>
            new WeatherProfile(season, 0, 0, 0, 0, 1)).ToArray();
        state = state with
        {
            WorldSystems = snowy with
            {
                Config = snowy.Config with { WeatherProfiles = profiles },
                Climate = snowy.Climate with { Weather = WeatherKind.Snow },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == alpha
                ? person with { Position = site, HungerBasisPoints = 9_000, Survival = new SurvivalCondition(5_000) }
                : person.InhabitantId == beta
                    ? person with
                    {
                        Position = site with { X = site.X + 1 },
                        HungerBasisPoints = 9_000,
                        Survival = new SurvivalCondition(5_000)
                    }
                    : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => new IdleProvider());
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var protectedWarmth = world.Inhabitants.Single(person => person.InhabitantId == alpha).Survival!.WarmthBasisPoints;
        var outsiderWarmth = world.Inhabitants.Single(person => person.InhabitantId == beta).Survival!.WarmthBasisPoints;
        Assert.True(protectedWarmth > outsiderWarmth,
            $"The owner's refuge should protect only its household: {protectedWarmth} vs {outsiderWarmth}.");

        var beforeAlphaWood = HouseholdWood(world, "household:camp-alpha");
        var beforeBetaWood = HouseholdWood(world, "household:camp-beta");
        var map = world.ExportState().Map;
        var secondSite = map.Tiles.Select(tile => tile.Position).First(point =>
            map.IsBuildable(point) &&
            !map.CampObjects.Any(item => item.Position == point) &&
            !map.Resources.Any(item => item.Position == point) &&
            !world.WorldSimulation.Buildings.Any(building => building.Position == point));
        var betaPlacement = world.PlaceBuilding("refuge-beta", house.CanonicalId, secondSite,
            "household:camp-beta");
        Assert.True(betaPlacement.Applied, betaPlacement.Failure);
        Assert.Equal(beforeAlphaWood, HouseholdWood(world, "household:camp-alpha"));
        Assert.Equal(beforeBetaWood - 8, HouseholdWood(world, "household:camp-beta"));
    }

    [Fact]
    public async Task HouseRequiresARealHouseholdAndKeepsItsOwnerAcrossSaveAndOwnerProjection()
    {
        using var world = new PrivateWorldRuntime("house-property");
        Assert.True(world.StageStarterContent());
        for (var tick = 0; tick < 6; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var house = world.WorldContent.Buildings.Single(building => building.LocalId == "house-1x1");
        var state = world.ExportState();
        var site = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.IsBuildable(point) &&
            !state.Map.CampObjects.Any(item => item.Position == point) &&
            !state.Map.Resources.Any(item => item.Position == point) &&
            !state.WorldSimulation!.Buildings.Any(item => item.Position == point));
        Assert.False(world.PlaceBuilding("home-alpha", house.CanonicalId, site).Applied);
        Assert.False(world.PlaceBuilding("home-alpha", house.CanonicalId, site, "household:unknown").Applied);
        var placement = world.PlaceBuilding("home-alpha", house.CanonicalId, site, "household:camp-alpha");
        Assert.True(placement.Applied, placement.Failure);

        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var restored = PrivateWorldRuntime.Restore(saved);
        var projected = new OwnerWorldObservationStore(restored).GetSnapshot().PlacedBuildings
            .Single(building => building.InstanceId == "home-alpha");
        Assert.Equal("household:camp-alpha", projected.HouseholdId);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = JsonSerializer.Deserialize<ClankerWorld.GodotClient.UI.OwnerWorldPlacedBuilding>(
            JsonSerializer.Serialize(projected, options), options)!;
        Assert.Equal(projected.HouseholdId, client.HouseholdId);

        var orphaned = saved with
        {
            WorldSimulation = saved.WorldSimulation! with
            {
                Buildings = saved.WorldSimulation.Buildings.Select(building =>
                    building.InstanceId == "home-alpha" ? building with { HouseholdId = "household:unknown" } : building).ToArray(),
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(orphaned));
    }

    private static int HouseholdWood(PrivateWorldRuntime world, string householdId) => world.Society.Inventory.Lots
        .Where(lot => lot.OwnerId == householdId && lot.ItemKind == "wood").Sum(lot => lot.Quantity);

    private static int HouseholdQuantity(PrivateWorldRuntime world, string householdId, string kind) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == householdId && lot.ItemKind == kind)
            .Sum(lot => lot.Quantity);

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(new CognitionDecisionResponse(
                request.RequestId, request.Observation.InhabitantId, Kind, ProviderEpoch,
                request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, "safe_idle", 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == "safe_idle" ? 1d : 0d)));
    }

    private sealed class PreferredCandidateProvider(string candidateId) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            var selected = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == candidateId) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
                request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
                request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
                request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                    candidate => candidate.Id == selected.Id ? 1d : 0d)));
        }
    }
}
