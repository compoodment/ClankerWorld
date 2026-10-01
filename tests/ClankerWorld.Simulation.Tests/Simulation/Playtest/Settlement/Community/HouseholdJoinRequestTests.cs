using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// An adult with no home asks a household that holds a House before any
/// construction is considered (#464). Every adult member must agree; nothing
/// is granted by standing nearby or while the request is pending.
/// </summary>
public sealed class HouseholdJoinRequestTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Beta = "household:camp-beta";

    [Fact]
    public async Task AdultOnTownLandMustAskAndEveryAdultMustAgreeBeforeJoining()
    {
        var provider = new ScriptedProvider();
        using var world = NormalPathWorld.CreateGenerated("housing-join", _ => provider);
        var agent = "agent:" + Guid.NewGuid().ToString("N");
        Assert.Null(world.AddAgent(agent, TownTileBeside(world, "first-town-house-a")));
        provider.Choices[agent] = "household_ask:" + Alpha;
        await AdvanceUntil(world, () => world.ExportState().Events.Any(item => item.Kind == "housing_request_made"));

        // Standing beside a House grants nothing; the adult is told why and may ask.
        Assert.Null(world.Society.GetInhabitant(agent).HouseholdId);
        var offered = provider.Offered[agent];
        Assert.Contains("household_ask:" + Alpha, offered.Keys);
        Assert.Contains("household_ask:" + Beta, offered.Keys);
        Assert.DoesNotContain(offered.Keys, id => id.StartsWith("build:", StringComparison.Ordinal));
        Assert.All(offered.Values, description => Assert.Null(RetiredWording.Find(description)));
        Assert.Contains("no household", provider.HousingNotes[agent], StringComparison.Ordinal);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "housing_blocked" && item.Detail == $"{agent}:no_household");
        var request = world.Inhabitants.Single(person => person.InhabitantId == agent).Housing!.Request!;
        Assert.Equal(Alpha, request.HouseholdId);
        var members = world.Society.GetHousehold(Alpha).MemberIds.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(2, members.Length);
        Assert.Equal(members, request.Members);
        Assert.Equal(PrivateWorldRuntime.HousingRequestTicks, request.ExpiryTick - request.RequestedTick);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "housing_request_made" && item.Detail == $"{agent}:{Alpha}");

        // One agreement is not enough, and a pending request grants no household access.
        // The second adult keeps busy with other work rather than answering yet.
        provider.Choices[agent] = "safe_idle";
        provider.Choices[members[0]] = "household_admit:" + agent;
        provider.Choices[members[1]] = ScriptedProvider.AnythingButHousing;
        await AdvanceUntil(world, () => world.Inhabitants.Single(person => person.InhabitantId == agent)
            .Housing?.Request?.Approvals.Contains(members[0], StringComparer.Ordinal) == true);
        Assert.Equal([members[0]], world.Inhabitants.Single(person => person.InhabitantId == agent).Housing!.Request!.Approvals);
        Assert.Null(world.Society.GetInhabitant(agent).HouseholdId);
        Assert.DoesNotContain(world.Society.GetHousehold(Alpha).MemberIds, id => id == agent);
        Assert.DoesNotContain(provider.Offered[agent].Keys, id => id is "collect_shared_food" or "haul_household_stock");
        Assert.DoesNotContain(provider.Offered[agent].Keys, id => id.StartsWith("household_ask:", StringComparison.Ordinal));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var visible = new OwnerWorldObservationStore(world).GetSnapshot();
        var applicant = visible.Inhabitants.Single(person => person.Id == agent);
        Assert.Equal("unhoused", applicant.DecisionFactors.Single(factor => factor.Key == "household").Detail);
        Assert.Contains("every adult member must agree", applicant.DecisionFactors.Single(factor => factor.Key == "housing").Detail, StringComparison.Ordinal);
        Assert.Contains(visible.Inhabitants.Single(person => person.Id == members[0]).SocialNotes,
            note => note.StartsWith("Agreed to let", StringComparison.Ordinal));
        Assert.Contains(visible.Inhabitants.Single(person => person.Id == members[1]).SocialNotes,
            note => note.Contains("asked to live", StringComparison.Ordinal));

        // The pending request survives a save, and replay from it is deterministic.
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        provider.Choices[members[1]] = "household_admit:" + agent;
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
        Assert.Equal(Alpha, restored.Inhabitants.Single(person => person.InhabitantId == agent).Housing!.Request!.HouseholdId);
        restored.Resume();
        replay.Resume();
        var ticks = 0;
        await AdvanceUntil(restored, () => restored.Society.GetInhabitant(agent).HouseholdId == Alpha, 80, () => ticks++);
        for (var tick = 0; tick < ticks; tick++)
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));

        // With every adult's agreement the adult is a member, with a member's access.
        Assert.Contains(agent, restored.Society.GetHousehold(Alpha).MemberIds);
        Assert.Contains(restored.Society.Relationships, edge => edge.Type == SocietyRelationshipType.HouseholdMembership &&
            edge.State == SocietyRelationshipState.Accepted && edge.TargetId == agent && edge.HouseholdId == Alpha);
        Assert.Null(restored.Inhabitants.Single(person => person.InhabitantId == agent).Housing);
        Assert.Contains(restored.ExportState().Events, item => item.Kind == "household_joined" && item.Detail == $"{agent}:{Alpha}");
        await AdvanceUntil(restored, () => provider.Offered[agent].ContainsKey("collect_shared_food"));
        var housed = new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants.Single(person => person.Id == agent);
        Assert.DoesNotContain(housed.DecisionFactors, factor => factor.Key == "housing");
        Assert.NotEqual("unhoused", housed.DecisionFactors.Single(factor => factor.Key == "household").Detail);
        restored.Validate();
    }

    [Fact]
    public async Task OneRefusalEndsTheRequestAndThatHouseholdIsNotAskedAgainForAWhile()
    {
        var provider = new ScriptedProvider();
        using var world = NormalPathWorld.CreateGenerated("housing-refuse", _ => provider);
        var agent = "agent:" + Guid.NewGuid().ToString("N");
        Assert.Null(world.AddAgent(agent, TownTileBeside(world, "first-town-house-a")));
        provider.Choices[agent] = "household_ask:" + Alpha;
        await AdvanceUntil(world, () => world.Inhabitants.Single(person => person.InhabitantId == agent).Housing?.Request is not null);
        provider.Choices[agent] = "safe_idle";
        var members = world.Society.GetHousehold(Alpha).MemberIds.Order(StringComparer.Ordinal).ToArray();
        provider.Choices[members[0]] = "household_admit:" + agent;
        provider.Choices[members[1]] = "household_refuse:" + agent;
        await AdvanceUntil(world, () => world.ExportState().Events.Any(item => item.Kind == "housing_request_refused"));

        Assert.Null(world.Society.GetInhabitant(agent).HouseholdId);
        var housing = world.Inhabitants.Single(person => person.InhabitantId == agent).Housing!;
        Assert.Null(housing.Request);
        Assert.Equal(Alpha, Assert.Single(housing.Refusals!).HouseholdId);
        Assert.Equal(world.WorldTick, Assert.Single(housing.Refusals!).Tick);

        // The other household can still be asked; the one that refused waits out its cooldown.
        provider.Choices[agent] = "household_ask:";
        await AdvanceUntil(world, () => world.Inhabitants.Single(person => person.InhabitantId == agent).Housing?.Request is not null);
        Assert.DoesNotContain("household_ask:" + Alpha, provider.Offered[agent].Keys);
        Assert.Equal(Beta, world.Inhabitants.Single(person => person.InhabitantId == agent).Housing!.Request!.HouseholdId);
        Assert.Equal("no_household", world.Inhabitants.Single(person => person.InhabitantId == agent).Housing!.Blocker);
        world.Validate();
        _ = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
    }

    [Fact]
    public async Task UnansweredRequestExpiresAndCountsAsARefusal()
    {
        var provider = new ScriptedProvider();
        using var world = NormalPathWorld.CreateGenerated("housing-expire", _ => provider);
        var agent = "agent:" + Guid.NewGuid().ToString("N");
        Assert.Null(world.AddAgent(agent, TownTileBeside(world, "first-town-house-b")));
        provider.Choices[agent] = "household_ask:" + Beta;
        await AdvanceUntil(world, () => world.Inhabitants.Single(person => person.InhabitantId == agent).Housing?.Request is not null);
        provider.Choices[agent] = "safe_idle";
        var requestedTick = world.Inhabitants.Single(person => person.InhabitantId == agent).Housing!.Request!.RequestedTick;
        await AdvanceUntil(world, () => world.ExportState().Events.Any(item => item.Kind == "housing_request_expired"),
            PrivateWorldRuntime.HousingRequestTicks + 5);

        Assert.True(world.WorldTick > requestedTick + PrivateWorldRuntime.HousingRequestTicks);
        Assert.Null(world.Society.GetInhabitant(agent).HouseholdId);
        var housing = world.Inhabitants.Single(person => person.InhabitantId == agent).Housing!;
        Assert.Null(housing.Request);
        Assert.Equal(Beta, Assert.Single(housing.Refusals!).HouseholdId);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "household_joined");
    }

    [Fact]
    public async Task AdultPlacedOnHouseholdPropertyJoinsWithoutConsentAndIsNeverAsked()
    {
        var provider = new ScriptedProvider();
        using var world = NormalPathWorld.CreateGenerated("housing-property", _ => provider);
        var house = world.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        var agent = "agent:" + Guid.NewGuid().ToString("N");
        Assert.Equal(Alpha, world.AddAgent(agent, house.Position));
        provider.Choices[agent] = "household_";
        await AdvanceUntil(world, () => provider.Offered.ContainsKey(agent));
        for (var tick = 0; tick < 5; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Equal(Alpha, world.Society.GetInhabitant(agent).HouseholdId);
        Assert.DoesNotContain(provider.Offered[agent].Keys, id => id.StartsWith("household_", StringComparison.Ordinal));
        Assert.Null(provider.HousingNotes[agent]);
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == agent).Housing);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind.StartsWith("housing_", StringComparison.Ordinal));
        var visible = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == agent);
        Assert.DoesNotContain(visible.DecisionFactors, factor => factor.Key == "housing");
    }

    [Fact]
    public async Task HouseholdWithoutAHouseIsToldAboutMissingMaterialsAndThenNoLegalSite()
    {
        var provider = new ScriptedProvider();
        using var seed = new PrivateWorldRuntime("housing-sites", _ => provider, startPace: WorldStartPace.FounderSetup);
        var positions = new[] { new GridPoint(0, 0), new GridPoint(1, 2), new GridPoint(2, 2), new GridPoint(3, 2) };
        for (var index = 0; index < positions.Length; index++)
            seed.PlaceFounder($"founder:{index + 1:D32}", positions[index]);
        seed.StartWorld();
        Assert.True(seed.StageStarterContent());
        await AdvanceUntil(seed, () => seed.WorldContent.Buildings.Any(item => item.LocalId == "farmhouse-1x1"));
        var house = seed.WorldContent.Buildings.Single(item => item.LocalId == "house-1x1");
        var filler = seed.WorldContent.Buildings.Single(item => item.LocalId == "farmhouse-1x1");
        var placed = seed.PlaceBuilding("alpha-house", house.CanonicalId, FreeTiles(seed)[0], Alpha);
        Assert.True(placed.Applied, placed.Failure);
        var agent = "agent:" + Guid.NewGuid().ToString("N");
        var householdId = seed.AddAgent(agent, FreeTiles(seed)[0]) ?? "household:newcomers";

        // A household of its own, without a House and without wood.
        var state = seed.ExportState();
        var society = state.Society.Society;
        if (!society.Households.Any(item => item.Id == householdId))
        {
            var tick = society.WorldTick;
            society = society with
            {
                Households = society.Households.Append(new SocietyHousehold(householdId, "Newcomers", [agent], []))
                    .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                Inhabitants = society.Inhabitants.Select(person => person.Id == agent ? person with { HouseholdId = householdId } : person).ToArray(),
                Relationships = society.Relationships.Append(new SocietyRelationship($"{householdId}:membership:{agent}", 1,
                        SocietyRelationshipType.HouseholdMembership, householdId, agent, SocietyRelationshipState.Accepted,
                        SocietyConsentState.ProtectedLifecycle, tick, tick, "household", householdId,
                        new[] { householdId, agent }.Order(StringComparer.Ordinal).ToArray()))
                    .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            };
        }
        state = state with { Society = state.Society with { Society = society } };
        using (var unstocked = PrivateWorldRuntime.Restore(state, _ => provider))
        {
            await AdvanceUntil(unstocked, () => Housing(unstocked, agent)?.Blocker == HousingBlockers.MissingMaterials, 5);
            Assert.DoesNotContain(provider.Offered.GetValueOrDefault(agent)?.Keys.ToArray() ?? [],
                id => id.StartsWith("household_", StringComparison.Ordinal));
            Assert.Contains(unstocked.ExportState().Events, item => item.Kind == "housing_blocked" && item.Detail == $"{agent}:missing_materials");
            Assert.Contains("lacks the materials", new OwnerWorldObservationStore(unstocked).GetSnapshot().Inhabitants
                .Single(person => person.Id == agent).DecisionFactors.Single(factor => factor.Key == "housing").Detail, StringComparison.Ordinal);
        }

        // With the wood in hand a House can be planned, so that is what the adult is told.
        var wood = house.BuildCosts.Single(cost => cost.ResourceId == "wood").Amount;
        state = state with
        {
            Society = state.Society with
            {
                Society = society with { Inventory = InventoryFixture.AddLot(society.Inventory, "newcomer-wood", "wood", householdId, wood) },
            },
        };
        using (var stocked = PrivateWorldRuntime.Restore(state, _ => provider))
        {
            await AdvanceUntil(stocked, () => Housing(stocked, agent)?.Blocker == HousingBlockers.NoAuthorizedHome, 5);
            await AdvanceUntil(stocked, () => provider.Offered.GetValueOrDefault(agent)?.Keys.Any(id =>
                TownConstructionCandidateIds.TryParse(id, out var selection) && selection.IsBuilding &&
                selection.DefinitionId == house.CanonicalId) == true);
            Assert.Contains("holds no House yet", provider.HousingNotes[agent], StringComparison.Ordinal);
            state = stocked.ExportState();
        }

        // Standing in a House with every other tile taken, there is no legal site.
        // The owner's fill placements draw their costs from the first household.
        var fillStock = filler.BuildCosts.Aggregate(state.Society.Society.Inventory, (inventory, cost) =>
            InventoryFixture.AddLot(inventory, "fill-" + cost.ResourceId, cost.ResourceId, Alpha, cost.Amount * 40));
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == agent
                ? person with { Position = placed.Position } : person).ToArray(),
            Society = state.Society with { Society = state.Society.Society with { Inventory = fillStock } },
        };
        using var crowded = PrivateWorldRuntime.Restore(state, _ => provider);
        // Each placement may lay a short Road, so the free tiles are found again each time.
        for (var free = FreeTiles(crowded); free.Length > 0; free = FreeTiles(crowded))
        {
            var filled = crowded.PlaceBuilding($"fill-{free[0].X}-{free[0].Y}", filler.CanonicalId, free[0]);
            Assert.True(filled.Applied, filled.Failure);
        }
        await AdvanceUntil(crowded, () => Housing(crowded, agent)?.Blocker == HousingBlockers.NoLegalSite, 5);
        Assert.Contains(crowded.ExportState().Events, item => item.Kind == "housing_blocked" && item.Detail == $"{agent}:no_legal_site");
        Assert.True((await crowded.AdvanceOneTickAsync()).Advanced);
        Assert.DoesNotContain(provider.Offered[agent].Keys, id => id.StartsWith("build:building:", StringComparison.Ordinal));
        Assert.Contains("no legal site", new OwnerWorldObservationStore(crowded).GetSnapshot().Inhabitants
            .Single(person => person.Id == agent).DecisionFactors.Single(factor => factor.Key == "housing").Detail, StringComparison.Ordinal);
        crowded.Validate();
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(crowded.ExportState())));
        Assert.Equal(HousingBlockers.NoLegalSite, Housing(reloaded, agent)?.Blocker);
    }

    [Fact]
    public async Task ForgedOrOldSchemaHousingStateIsRefused()
    {
        var provider = new ScriptedProvider();
        using var world = NormalPathWorld.CreateGenerated("housing-forged", _ => provider);
        var agent = "agent:" + Guid.NewGuid().ToString("N");
        Assert.Null(world.AddAgent(agent, TownTileBeside(world, "first-town-house-a")));
        provider.Choices[agent] = "household_ask:" + Alpha;
        await AdvanceUntil(world, () => Housing(world, agent)?.Request is not null);
        world.Pause();
        var state = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        var request = Housing(state, agent)!.Request!;

        var old = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state with { SchemaVersion = 30 }));
        Assert.Contains("Housing", old.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(WithHousing(state, agent,
            housing => housing with { Request = request with { Approvals = [agent] } })));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(WithHousing(state, agent,
            housing => housing with { Request = request with { ExpiryTick = request.ExpiryTick + 1 } })));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(WithHousing(state, agent,
            housing => housing with { Request = request with { HouseholdId = "household:nobody" } })));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(WithHousing(state, agent,
            housing => housing with { Refusals = [new SettlementHousingRefusal("household:nobody", 0)] })));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(WithHousing(state, agent,
            housing => housing with { Blocker = "no_such_reason" })));
        var housed = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == agent
                        ? person with { HouseholdId = Beta } : person).ToArray(),
                },
            },
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(housed));
        using var intact = PrivateWorldRuntime.Restore(state);
        Assert.Equal(request, Housing(intact, agent)!.Request);
    }

    private static SettlementHousing? Housing(PrivateWorldRuntime world, string agent) =>
        world.Inhabitants.Single(person => person.InhabitantId == agent).Housing;

    private static SettlementHousing? Housing(PrivateWorldRuntimeState state, string agent) =>
        state.Inhabitants.Single(person => person.InhabitantId == agent).Housing;

    private static PrivateWorldRuntimeState WithHousing(PrivateWorldRuntimeState state, string agent,
        Func<SettlementHousing, SettlementHousing> change)
    {
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == agent
                ? person with { Housing = change(person.Housing!) } : person).ToArray(),
        };
    }

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> condition, int maxTicks = 40, Action? onTick = null)
    {
        for (var tick = 0; tick < maxTicks && !condition(); tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            onTick?.Invoke();
        }
        Assert.True(condition(), $"The expected state was not reached within {maxTicks} ticks.");
    }

    /// <summary>Unclaimed Town land near a building: buildable, not built on, not a Road and empty.</summary>
    private static GridPoint TownTileBeside(PrivateWorldRuntime world, string instanceId)
    {
        var anchor = world.WorldSimulation.Buildings.Single(item => item.InstanceId == instanceId).Position;
        var town = world.Towns.Single();
        var free = FreeTiles(world).ToHashSet();
        return Enumerable.Range(1, 4)
            .SelectMany(distance => Enumerable.Range(-distance, 2 * distance + 1)
                .SelectMany(dx => Enumerable.Range(-distance, 2 * distance + 1)
                    .Select(dy => new GridPoint(anchor.X + dx, anchor.Y + dy))))
            .First(point => free.Contains(point) && town.BorderTiles.Contains(point));
    }

    /// <summary>Buildable tiles with no camp object, resource, building, Road or agent, in a stable order.</summary>
    private static GridPoint[] FreeTiles(PrivateWorldRuntime world)
    {
        var state = world.ExportState();
        var map = state.Map;
        var definitions = world.WorldContent.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        var footprints = world.WorldSimulation.Buildings
            .SelectMany(building => WorldContentSimulationRules.Footprint(definitions[building.DefinitionId], building.Position))
            .ToHashSet();
        var roads = world.RoadTiles.ToHashSet();
        return map.Tiles.Select(tile => tile.Position)
            .Where(point => map.IsBuildable(point) && !footprints.Contains(point) && !roads.Contains(point) &&
                !map.CampObjects.Any(item => item.Position == point) && !map.Resources.Any(item => item.Position == point) &&
                !state.Inhabitants.Any(person => person.Position == point))
            .OrderBy(point => point.Y).ThenBy(point => point.X)
            .ToArray();
    }

    /// <summary>
    /// Chooses, for each agent, the first offered candidate with the scripted
    /// prefix, or safe_idle, and records what each agent was offered and told.
    /// <see cref="AnythingButHousing"/> lets the built-in rules choose among
    /// everything except housing answers, so the agent stays busy and is asked
    /// again soon instead of idling for a long time.
    /// </summary>
    private sealed class ScriptedProvider : IDecisionProvider
    {
        public const string AnythingButHousing = "!household_";

        public ConcurrentDictionary<string, string> Choices { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, Dictionary<string, string>> Offered { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, string?> HousingNotes { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            Offered[observation.InhabitantId] = observation.Candidates.ToDictionary(item => item.Id, item => item.Description, StringComparer.Ordinal);
            HousingNotes[observation.InhabitantId] = observation.Self?.HousingNote;
            var prefix = Choices.GetValueOrDefault(observation.InhabitantId, "safe_idle");
            var candidates = prefix == AnythingButHousing
                ? observation.Candidates.Where(item => !item.Id.StartsWith("household_", StringComparison.Ordinal)).ToArray()
                : [observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal))
                    ?? observation.Candidates.Single(item => item.Id == "safe_idle")];
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = observation with { Candidates = candidates },
            }, cancellationToken);
        }
    }
}
