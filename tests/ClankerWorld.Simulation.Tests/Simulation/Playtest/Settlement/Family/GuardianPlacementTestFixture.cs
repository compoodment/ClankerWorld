using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

internal sealed record GuardianPlacementScenario(PrivateWorldRuntimeState State, string[] Children, string[] Guardians,
    PlacedBuilding SourceHouse, PlacedBuilding House, string OriginalTownId, string DestinationTownId);

internal static class GuardianPlacementTestFixture
{
    internal const string SourceHousehold = "household:camp-alpha";
    internal const string DestinationHousehold = "household:camp-beta";
    internal const string DestinationTown = "town:guardian-placement";
    private static readonly Lazy<byte[]> Generated = new(CreateGenerated);
    private static readonly ConcurrentDictionary<(int Orphans, int Residents), Lazy<byte[]>> Baselines = new();

    internal static GuardianPlacementScenario Prepared(int orphanCount = 1, int residentChildren = 1)
    {
        var bytes = Baselines.GetOrAdd((orphanCount, residentChildren), key =>
            new Lazy<byte[]>(() => CreateBaseline(key.Orphans, key.Residents))).Value;
        var state = PrivateWorldRuntimeCodec.Decode(bytes);
        var society = state.Society.Society;
        var children = society.Births.Where(birth => birth.RequestId.StartsWith("guardian-placement-orphan-", StringComparison.Ordinal))
            .OrderBy(birth => birth.RequestId, StringComparer.Ordinal).Select(birth => birth.ChildId).ToArray();
        var guardians = society.GetHousehold(DestinationHousehold).MemberIds.Where(id =>
            society.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Order(StringComparer.Ordinal).ToArray();
        var source = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var destination = state.WorldSimulation.Buildings.Single(building => building.InstanceId == "guardian-placement-house");
        return new(state, children, guardians, source, destination, source.TownId!, DestinationTown);
    }

    internal static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, GuardianPlacementChoices? choices = null)
    {
        choices ??= new();
        return PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => choices);
    }

    internal static PrivateWorldRuntime Reload(PrivateWorldRuntime world, GuardianPlacementChoices choices)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var restored = Restore(PrivateWorldRuntimeCodec.Decode(bytes), choices);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        return restored;
    }

    internal static GuardianPlacementChoices Accepting(GuardianPlacementScenario scenario, bool both = false)
    {
        var choices = new GuardianPlacementChoices();
        for (var index = 0; index < (both ? scenario.Children.Length : 1); index++)
            choices.Set(scenario.Guardians[index], "guardian_accept:" + scenario.Children[index],
                "guardian_relocate:" + scenario.Children[index]);
        return choices;
    }

    internal static PlaytestInhabitantState Person(PrivateWorldRuntime world, string id) =>
        world.Inhabitants.Single(person => person.InhabitantId == id);

    internal static string? TownOf(PrivateWorldRuntime world, string id) =>
        world.Towns.SingleOrDefault(town => town.ResidentIds.Contains(id, StringComparer.Ordinal))?.Id;

    internal static bool Placed(PrivateWorldRuntime world, GuardianPlacementScenario scenario, string child) =>
        world.Society.GetInhabitant(child).HouseholdId == scenario.House.HouseholdId &&
        TownOf(world, child) == scenario.DestinationTownId && Person(world, child).GuardianPlacement is null;

    internal static HouseResidentCapacityRules.Capacity Capacity(PrivateWorldRuntime world, string household = DestinationHousehold) =>
        HouseResidentCapacityRules.Calculate(world.Society.Inhabitants.Where(person => person.HouseholdId == household), 1, 1);

    internal static async Task Step(PrivateWorldRuntime world)
    {
        var before = world.Inhabitants.ToDictionary(person => person.InhabitantId);
        var result = await world.AdvanceOneTickAsync();
        Assert.True(result.Advanced);
        var map = world.ExportState().Map;
        foreach (var person in world.Inhabitants)
        {
            if (!before.TryGetValue(person.InhabitantId, out var previous)) continue;
            var movement = result.Events.Where(item => item.Kind == "inhabitant_moved" &&
                item.Detail.StartsWith(person.InhabitantId + ":", StringComparison.Ordinal)).ToArray();
            Assert.True(movement.Length <= 1, $"{person.InhabitantId} moved more than once in world tick {world.WorldTick}.");
            if (previous.Position != person.Position)
            {
                Assert.True(map.CanFootStep(previous.Position, person.Position),
                    $"{person.InhabitantId} made an illegal step from {previous.Position} to {person.Position}.");
                Assert.Single(movement);
                Assert.Equal(0, previous.TravelCooldownTicks);
            }
            else if (previous.TravelCooldownTicks > 0)
            {
                Assert.True(person.TravelCooldownTicks >= previous.TravelCooldownTicks - 1,
                    $"{person.InhabitantId} spent more than one travel cooldown action in a tick.");
            }
        }
    }

    internal static async Task Until(PrivateWorldRuntime world, Func<bool> completed, int maximumTicks = 100,
        PrivateWorldRuntime? replay = null)
    {
        for (var tick = 0; tick < maximumTicks && !completed(); tick++)
        {
            await Step(world);
            if (replay is not null)
            {
                await Step(replay);
                Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
            }
        }
        Assert.True(completed(), $"Guardian placement did not reach the expected state within {maximumTicks} ticks.");
    }

    internal static async Task Accepted(PrivateWorldRuntime world, GuardianPlacementScenario scenario, bool both = false) =>
        await Until(world, () => scenario.Children.Take(both ? scenario.Children.Length : 1)
            .All(child => world.Society.GetInhabitant(child).PrimaryCaregiverId is not null), maximumTicks: 12);

    private static byte[] CreateGenerated()
    {
        using var world = NormalPathWorld.CreateGenerated("guardian-relocation", _ => new GuardianPlacementChoices());
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private static byte[] CreateBaseline(int orphanCount, int residentChildren)
    {
        var state = PrivateWorldRuntimeCodec.Decode(Generated.Value);
        var society = state.Society.Society;
        var parents = society.GetHousehold(SourceHousehold).MemberIds.Order(StringComparer.Ordinal).ToArray();
        var guardians = society.GetHousehold(DestinationHousehold).MemberIds.Order(StringComparer.Ordinal).ToArray();
        var source = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        var oldDestination = state.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-b");
        foreach (var pair in new[] { parents, guardians })
        {
            var id = "guardian-placement-partners:" + pair[0];
            society = SocietyFixture.ProposeRelationship(society,
                new(id, 1, SocietyRelationshipType.Partnership, pair[0], pair[1], society.WorldTick)).Checkpoint;
            society = SocietyFixture.AcceptRelationship(society, id, 1, pair[1]).Checkpoint;
        }
        var inventory = InventoryFixture.CreateGenesis([]) with { WorldTick = society.WorldTick };
        inventory = InventoryFixture.AddLot(inventory, "guardian-placement-birth-food", "food", SourceHousehold, orphanCount * 4,
            storageBuildingId: source.InstanceId);
        if (residentChildren > 0)
            inventory = InventoryFixture.AddLot(inventory, "guardian-placement-resident-food", "food", DestinationHousehold, residentChildren * 4,
                storageBuildingId: oldDestination.InstanceId);
        society = society with { Inventory = inventory };
        var newborns = new List<(string Id, bool Orphan)>();
        foreach (var orphan in new[] { true, false })
        {
            var pair = orphan ? parents : guardians;
            for (var index = 0; index < (orphan ? orphanCount : residentChildren); index++)
            {
                var birth = SocietyFixture.CommitBirth(society, new SocietyBirthRequest(
                    "guardian-placement-" + (orphan ? "orphan-" : "resident-") + index, 1, pair[0], pair[1],
                    orphan ? SourceHousehold : DestinationHousehold, pair, pair,
                    orphan ? "guardian-placement-birth-food" : "guardian-placement-resident-food", 4, society.WorldTick,
                    ChildName: (orphan ? "Robin " : "Ari ") + index, PrimaryCaregiverId: pair[0]));
                society = birth.Checkpoint;
                newborns.Add((Assert.IsType<string>(birth.CreatedId), orphan));
            }
        }
        society = society with
        {
            Relationships = society.Relationships.Concat(guardians.Select((guardian, index) => new SocietyRelationship(
                "guardian-placement-grandparent-" + index, 1, SocietyRelationshipType.BiologicalParentage, guardian, parents[0],
                SocietyRelationshipState.Accepted, SocietyConsentState.ProtectedLifecycle, society.WorldTick, society.WorldTick, "family")))
                .OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray(),
        };
        var deceased = new List<PlaytestDeceasedInhabitantState>(state.DeceasedInhabitants ?? []);
        foreach (var parent in parents)
        {
            society = SocietyFixture.Kill(society, parent, SocietyDeathCause.Accident).Checkpoint;
            var person = society.GetInhabitant(parent);
            deceased.Add(new(parent, person.DeathTick!.Value, society.AgeAt(person, person.DeathTick.Value),
                state.Inhabitants.Single(item => item.InhabitantId == parent)));
        }
        var people = state.Inhabitants.Where(person => !parents.Contains(person.InhabitantId, StringComparer.Ordinal)).ToList();
        foreach (var (id, orphan) in newborns)
        {
            var near = orphan ? source.Position : oldDestination.Position;
            var position = state.Map.Tiles.Select(tile => tile.Position).Where(point => state.Map.IsBuildable(point) &&
                    !people.Any(person => person.Position == point) && !state.Map.Resources.Any(resource => resource.Position == point) &&
                    !state.Map.CampObjects.Any(camp => camp.Position == point))
                .OrderBy(point => state.Map.FootDistance(point, near)).ThenBy(point => point.Y).ThenBy(point => point.X).First();
            people.Add(new(id, position, 10_000, 0, "curious", "grow with the household", Survival: new(10_000)));
        }
        var first = state.Towns!.Single();
        var residents = society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active)
            .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();
        var destinationResidents = society.GetHousehold(DestinationHousehold).MemberIds.Order(StringComparer.Ordinal).ToArray();
        var occupied = state.Map.Resources.Select(resource => resource.Position).Concat(state.Map.CampObjects.Select(camp => camp.Position))
            .Concat(state.RoadTiles ?? []).Concat((state.Fields ?? []).Select(field => field.Position))
            .Concat((state.HouseholdLandUseRights ?? []).SelectMany(right => right.Tiles))
            .Concat(state.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
                state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building))).ToHashSet();
        var center = state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => state.Map.IsBuildable(point) && !occupied.Contains(point) &&
                state.Map.FootDistance(source.Position, point) >= 8 &&
                !TownBorderRules.Around(state.Map, [point]).Intersect(first.BorderTiles).Any() &&
                state.Map.FootNeighbors(point).Any(entrance => !state.Map.IsDiagonalFootStep(point, entrance) &&
                    state.Map.IsBuildable(entrance) && !occupied.Contains(entrance) &&
                    !TownBorderRules.Around(state.Map, [point, entrance]).Intersect(first.BorderTiles).Any()) &&
                state.Map.IsReachableOnFoot(source.Position, point) &&
                state.Map.FootNeighbors(point).Count(neighbor => state.Map.IsBuildable(neighbor) && !occupied.Contains(neighbor)) >= 3)
            .OrderBy(point => state.Map.FootDistance(source.Position, point)).ThenBy(point => point.Y).ThenBy(point => point.X).First();
        var localRoad = state.Map.FootNeighbors(center).Where(entrance => !state.Map.IsDiagonalFootStep(center, entrance) &&
                state.Map.IsBuildable(entrance) && !occupied.Contains(entrance) &&
                !TownBorderRules.Around(state.Map, [center, entrance]).Intersect(first.BorderTiles).Any())
            .OrderBy(point => point.Y).ThenBy(point => point.X).First();
        state = state with
        {
            // Give the second Town a local entrance Road. Otherwise the native global Road
            // network deliberately connects the new House back to the original Town.
            RoadTiles = (state.RoadTiles ?? []).Append(localRoad).OrderBy(point => point.Y).ThenBy(point => point.X).ToArray(),
            Society = state.Society with { Society = society },
            Inhabitants = people.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                Survival = new(10_000),
                Project = null,
                Equipment = null,
                LastDecisionContext = null,
                Position = destinationResidents.Contains(person.InhabitantId, StringComparer.Ordinal) ? center : person.Position,
            }).ToArray(),
            DeceasedInhabitants = deceased.ToArray(),
            Survival = new(society.WorldTick, []),
            Towns =
            [
                first with { ResidentIds = residents.Except(destinationResidents, StringComparer.Ordinal).ToArray(), Governance = TownGovernanceState.Create([]) },
                new(DestinationTown, "Guardian Town", "founded", 0, destinationResidents, [], [center], center,
                    TownGovernanceState.Create(guardians), TownGovernmentState.Create()),
            ],
        };
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);
        var stock = InventoryFixture.AddLot(state.Society.Society.Inventory, "guardian-home-timber", "wood", DestinationHousehold, 8);
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = stock } } };
        using var world = Restore(state);
        var removal = world.RemoveBuilding(oldDestination.InstanceId, oldDestination.TownId, DestinationHousehold);
        Assert.True(removal.Applied, removal.Failure);
        var placement = world.PlaceBuilding("guardian-placement-house", oldDestination.DefinitionId, center, DestinationHousehold);
        Assert.True(placement.Applied, placement.Failure);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "guardian-home-timber");
        Assert.Equal(DestinationTown, world.WorldSimulation.Buildings.Single(building => building.InstanceId == placement.InstanceId).TownId);
        Assert.Empty(world.Towns.Single(town => town.Id == DestinationTown).BorderTiles.Intersect(first.BorderTiles));
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }
}

internal sealed class GuardianPlacementChoices : IDecisionProvider
{
    private readonly Dictionary<string, string[]> scripts = new(StringComparer.Ordinal);
    internal List<(string Actor, string Choice)> Selected { get; } = [];
    public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
    public long ProviderEpoch => 0;
    internal void Set(string actor, params string[] prefixes) => scripts[actor] = prefixes;
    internal GuardianPlacementChoices Copy()
    {
        var copy = new GuardianPlacementChoices();
        foreach (var (actor, prefixes) in scripts) copy.Set(actor, prefixes);
        return copy;
    }
    public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var observation = request.Observation;
        var prefixes = scripts.GetValueOrDefault(observation.InhabitantId, []);
        var candidate = prefixes.Select(prefix => observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal)))
            .FirstOrDefault(item => item is not null) ?? observation.Candidates.Single(item => item.Id == "safe_idle");
        Selected.Add((observation.InhabitantId, candidate.Id));
        return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
            observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, candidate.Id, 1,
            new Dictionary<string, double> { [candidate.Id] = 1 }));
    }
}
