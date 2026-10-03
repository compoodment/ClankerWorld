using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

internal sealed record BuildingVariantFixture(
    PrivateWorldRuntimeState State, string Actor, BuildingDefinition Definition)
{
    public string Household => State.Society.Society.GetInhabitant(Actor).HouseholdId!;
    public PlacedBuilding Building => State.WorldSimulation!.Buildings.Single(item => item.DefinitionId == Definition.CanonicalId);
}

internal static class BuildingVariantTestWorld
{
    // Cache only immutable encoded setup. Every caller gets its own decoded graph and providers.
    private static readonly Lazy<Task<byte[]>> Baseline = new(() =>
    {
        using var world = NormalPathWorld.CreateGenerated("probe-a", _ => new VariantActionProvider());
        return Task.FromResult(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    });

    public static async Task<BuildingVariantFixture> CreatePreparedAsync(string family)
    {
        var state = PrivateWorldRuntimeCodec.Decode((await Baseline.Value).ToArray());
        var (width, height) = family switch
        {
            "farmhouse" => (1, 2),
            "clinic" => (1, 1),
            _ => (2, 2),
        };
        var definition = state.WorldContent!.Buildings.Single(item => item.Tags.Contains(family, StringComparer.Ordinal) &&
            item.Width == width && item.Height == height);
        var household = family == "farmhouse" ? "household:camp-beta" : "household:camp-alpha";
        var actor = state.Society.Society.Inhabitants.First(item => item.HouseholdId == household).Id;
        var house = state.WorldSimulation!.Buildings.Single(item => item.HouseholdId == household &&
            state.WorldContent.Buildings.Single(building => building.CanonicalId == item.DefinitionId)
                .Tags.Contains("house", StringComparer.Ordinal));
        var kinds = definition.BuildCosts.Select(cost => cost.ResourceId).ToHashSet(StringComparer.Ordinal);
        var inventory = state.Society.Society.Inventory;
        var replaced = inventory.Lots.Where(lot => lot.OwnerId == household && kinds.Contains(lot.ItemKind))
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => !replaced.Contains(lot.Id)).ToArray(),
            Reservations = inventory.Reservations.Where(item => !replaced.Contains(item.LotId)).ToArray(),
        };
        foreach (var cost in definition.BuildCosts)
            inventory = InventoryFixture.AddLot(inventory, "variant-build-" + cost.ResourceId,
                cost.ResourceId, household, cost.Amount, storageBuildingId: house.InstanceId);
        state = WithInventory(state, inventory) with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == actor ? house.Position : person.Position,
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
                Project = null,
                TravelCooldownTicks = 0,
            }).ToArray(),
        };
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);
        return new(Strict(state), actor, definition);
    }

    public static PrivateWorldRuntime Restore(BuildingVariantFixture fixture, VariantActionProvider provider) =>
        PrivateWorldRuntime.Restore(fixture.State, id => id == fixture.Actor ? provider : new VariantActionProvider());

    public static bool IsBuildingCandidate(string id, string definitionId) =>
        TownConstructionCandidateIds.TryParse(id, out var selection) && selection.IsBuilding &&
        selection.DefinitionId == definitionId;

    public static async Task<BuildingVariantFixture> BuildAsync(string family)
    {
        var fixture = await CreatePreparedAsync(family);
        var provider = new VariantActionProvider(id => IsBuildingCandidate(id, fixture.Definition.CanonicalId));
        using var world = Restore(fixture, provider);
        await UntilAsync(world, () => world.Inhabitants.Single(person => person.InhabitantId == fixture.Actor)
            .Project is { Stage: "working", WorkDone: > 0 });
        Assert.DoesNotContain(world.WorldSimulation.Buildings, item => item.DefinitionId == fixture.Definition.CanonicalId);
        foreach (var cost in fixture.Definition.BuildCosts)
            Assert.Equal(cost.Amount, world.Society.Inventory.Lots.Where(lot => lot.OwnerId == fixture.Household &&
                lot.ItemKind == cost.ResourceId).Sum(lot => lot.Quantity));
        await UntilAsync(world, () => world.WorldSimulation.Buildings.Any(item => item.DefinitionId == fixture.Definition.CanonicalId));
        var building = Assert.Single(world.WorldSimulation.Buildings, item => item.DefinitionId == fixture.Definition.CanonicalId);
        var project = world.Inhabitants.Single(person => person.InhabitantId == fixture.Actor).Project!;
        Assert.Equal("completed", project.Stage);
        Assert.True(TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection));
        Assert.Equal(building.Position, selection.SitePosition);
        Assert.Contains(project.CandidateId, provider.Offered);
        Assert.Equal(fixture.Household, building.HouseholdId);
        Assert.Equal(fixture.State.Towns!.Single().Id, building.TownId);
        foreach (var cost in fixture.Definition.BuildCosts)
        {
            Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.OwnerId == fixture.Household && lot.ItemKind == cost.ResourceId);
            var paid = Assert.Single(world.Society.Inventory.Reservations, reservation =>
                reservation.LotId == "variant-build-" + cost.ResourceId &&
                reservation.Purpose == "building:" + building.InstanceId);
            Assert.Equal((fixture.Household, cost.Amount, InventoryReservationState.Completed),
                (paid.OwnerId, paid.Quantity, paid.State));
        }
        Assert.Equal(fixture.Definition.Width * fixture.Definition.Height,
            WorldContentSimulationRules.Footprint(fixture.Definition, building.Position).Distinct().Count());
        var entrance = Assert.IsType<GridPoint>(building.Entrance);
        Assert.Contains(entrance, world.RoadTiles);
        Assert.True(WorldContentSimulationRules.IsEntrance(fixture.Definition, building.Position, entrance));
        Assert.DoesNotContain(entrance,
            WorldContentSimulationRules.Footprint(fixture.Definition, building.Position));
        Assert.Equal(1, fixture.Definition.Capacity);
        Assert.Equal(family == "farmhouse" ? 96 : family == "clinic" ? 64 : 256,
            BuildingStorageRules.Capacity(fixture.Definition, building));
        var visible = new OwnerWorldObservationStore(world).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == building.InstanceId);
        Assert.Equal((fixture.Definition.CanonicalId, fixture.Definition.DisplayName, fixture.Definition.Width, fixture.Definition.Height),
            (visible.DefinitionId, visible.DisplayName, visible.Width, visible.Height));
        Assert.Equal((building.Position.X, building.Position.Y), (visible.Position.X, visible.Position.Y));
        Assert.Equal(new ViewerPosition(entrance.X, entrance.Y), visible.Entrance);
        Assert.Equal(BuildingStorageRules.Capacity(fixture.Definition, building), visible.StorageCapacity);
        Assert.Equal((fixture.Household, building.TownId), (visible.HouseholdId, visible.TownId));
        return fixture with { State = Strict(world.ExportState()) };
    }

    public static async Task UntilAsync(PrivateWorldRuntime world, Func<bool> complete, int limit = 160)
    {
        for (var tick = 0; tick < limit && !complete(); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(complete(), string.Join("\n", world.ExportState().Events.TakeLast(16)
            .Select(item => item.Kind + ": " + item.Detail)) + "\n" + string.Join("\n",
            world.Inhabitants.Select(person => person.InhabitantId + ": " + person.Project)));
    }

    public static PrivateWorldRuntimeState Strict(PrivateWorldRuntimeState state) =>
        PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state));

    public static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
}

internal sealed class VariantActionProvider(Predicate<string>? choose = null) : IDecisionProvider
{
    public Predicate<string>? Choose { get; set; } = choose;
    public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
    public List<string> Chosen { get; } = [];
    public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
    public long ProviderEpoch => 0;

    public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in request.Observation.Candidates) Offered.Add(candidate.Id);
        var selected = request.Observation.Candidates.FirstOrDefault(candidate => Choose?.Invoke(candidate.Id) == true) ??
            request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
        Chosen.Add(selected.Id);
        return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId,
            request.Observation.InhabitantId, Kind, ProviderEpoch, request.Observation.RunEpoch,
            request.Observation.DecisionGeneration, request.Observation.ObservationDigest, selected.Id, 1,
            request.Observation.Candidates.ToDictionary(candidate => candidate.Id,
                candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal)));
    }
}
