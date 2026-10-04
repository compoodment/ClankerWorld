using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

internal static class HouseRelocationTestWorld
{
    public const string Household = "household:camp-alpha";
    public const string HouseId = "first-town-house-a";
    public const int Day = 12;

    public static PrivateWorldRuntimeState Crowded(int residents = 4, int day = Day)
    {
        using var generated = NormalPathWorld.CreateGenerated("relocation-runtime", _ => new Choices());
        for (var ordinal = 5; ordinal <= residents; ordinal++)
        {
            var setup = generated.ExportState();
            var occupied = setup.WorldSimulation!.Buildings.SelectMany(building =>
                WorldContentSimulationRules.Footprint(setup.WorldContent!.Buildings.Single(definition =>
                    definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
            var site = setup.Towns![0].BorderTiles.First(point => setup.Map.IsBuildable(point) &&
                !occupied.Contains(point) && !setup.Map.Resources.Any(resource => resource.Position == point) &&
                !setup.Map.CampObjects.Any(item => item.Position == point) &&
                !setup.Inhabitants.Any(person => person.Position == point));
            generated.AddAgent($"agent:{ordinal:D32}", site);
        }
        generated.Pause();
        var state = generated.ExportState();
        var society = state.Society.Society;
        foreach (var person in society.Inhabitants.Where(person => person.HouseholdId != Household).ToArray())
        {
            if (person.HouseholdId is not null)
                society = SocietyFixture.LeaveHousehold(society, person.Id).Checkpoint;
            society = SocietyFixture.JoinHouseholdCareGroup(society, person.Id, Household).Checkpoint;
        }
        var originalDay = society.Config.TicksPerWorldDay;
        society = society with
        {
            Config = society.Config with { TicksPerWorldDay = day },
            Inhabitants = society.Inhabitants.Select(person => person with
            {
                BirthTick = person.BirthTick / originalDay * day,
                BirthLifeTick = person.BirthLifeTick is { } birth ? birth / originalDay * day : null,
                DomesticFamilyUnitId = "relocation-family:" + person.Id,
            }).ToArray(),
        };
        return state with
        {
            Society = state.Society with { Society = society },
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            {
                Config = state.WorldSystems.Config with { TicksPerDay = day, CalendarOffsetTicks = 0 },
                RegionalWeather = null,
            }, state.Map),
            Towns = state.Towns!.Select(town => town with
            {
                Governance = TownGovernanceState.Create(town.ResidentIds),
            }).ToArray(),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 9_000,
                Survival = person.Survival is { } survival ? survival with { WarmthBasisPoints = 9_000 } : null,
                Project = null,
                LastDecisionContext = null,
                Housing = null,
            }).ToArray(),
        };
    }

    public static PrivateWorldRuntimeState WithDependent(PrivateWorldRuntimeState state, string caregiver, string child)
    {
        var society = state.Society.Society;
        society = society with
        {
            Inhabitants = society.Inhabitants.Select(person => person.Id == child ? person with
            {
                AgeBand = SocietyAgeBand.Infant,
                BirthTick = society.WorldTick,
                BirthLifeTick = null,
                LastLifecycleYearChecked = 0,
                PrimaryCaregiverId = caregiver,
                DomesticFamilyUnitId = society.GetInhabitant(caregiver).DomesticFamilyUnitId,
            } : person).ToArray(),
            Relationships = society.Relationships.Append(new SocietyRelationship("relocation-care:" + child, 1,
                SocietyRelationshipType.Caregiver, caregiver, child, SocietyRelationshipState.Accepted,
                SocietyConsentState.Accepted, society.WorldTick, society.WorldTick, "public", Household, [caregiver]))
                .OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray(),
            Households = society.Households.Select(home => home.Id == Household
                ? home with { CaregiverIds = home.CaregiverIds.Append(caregiver).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() }
                : home).ToArray(),
        };
        return state with
        {
            Society = state.Society with { Society = society },
            Towns = state.Towns!.Select(town => town with
            {
                Governance = TownGovernanceRules.Advance(town.Governance!, town.Id, state.WorldSeed,
                    town.ResidentIds.Where(id => society.Inhabitants.Any(person => person.Id == id &&
                        person.Status == SocietyInhabitantStatus.Active &&
                        person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)),
                    society.WorldTick, state.WorldSystems!.Config.TicksPerDay),
            }).ToArray(),
        };
    }

    public static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    public static PrivateWorldRuntimeState WithExpandableHouse(PrivateWorldRuntimeState state, string worker)
    {
        var house = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == HouseId);
        var occupied = state.WorldSimulation.Buildings.Where(building => building.InstanceId != HouseId)
            .SelectMany(building => WorldContentSimulationRules.Footprint(state.WorldContent!.Buildings.Single(
                definition => definition.CanonicalId == building.DefinitionId), building))
            .Concat(state.Map.Resources.Select(resource => resource.Position))
            .Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.RoadTiles!).Concat(state.Fields!.Select(field => field.Position))
            .Concat(state.Inhabitants.Where(person => person.InhabitantId != worker).Select(person => person.Position))
            .ToHashSet();
        var site = state.Map.Tiles.Select(tile => tile.Position)
            .OrderBy(point => Math.Abs(point.X - house.Position.X) + Math.Abs(point.Y - house.Position.Y))
            .ThenBy(point => point.Y).ThenBy(point => point.X).First(point =>
            Enumerable.Range(-1, 3).SelectMany(dy => Enumerable.Range(-1, 3)
                .Select(dx => new GridPoint(point.X + dx, point.Y + dy)))
                .All(tile => state.Map.IsBuildable(tile) && !occupied.Contains(tile)));
        var moved = house with { Position = site, Entrance = null };
        var relocated = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "relocation-expansion-wood", "wood", Household, 8, storageBuildingId: HouseId)) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == worker
                ? person with { Position = site } : person).ToArray(),
            WorldSimulation = state.WorldSimulation with
            {
                Buildings = state.WorldSimulation.Buildings.Select(building => building.InstanceId == HouseId
                    ? moved : building).ToArray(),
            },
            Towns = state.Towns!.Select(town => town.Id == house.TownId ? town with
            {
                BorderTiles = TownBorderRules.ExpandForBuilding(state.Map, town, site, 2, 2),
            } : town).ToArray(),
        };
        // Expansion onto extra tiles needs the household's recorded use of that
        // land; HouseholdLandGrantTests covers the Council request itself.
        return ExpansionLandFixture.WithRights(relocated, moved, Enumerable.Range(-1, 3).SelectMany(dy =>
            Enumerable.Range(-1, 3).Select(dx => new GridPoint(site.X + dx, site.Y + dy))));
    }

    public static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state, Choices? choices = null) =>
        PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            _ => choices ?? new Choices());

    public static async Task AdvanceTo(PrivateWorldRuntime world, long tick)
    {
        world.Resume();
        while (world.WorldTick < tick) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    }

    public static PlaytestInhabitantState[] Noticed(PrivateWorldRuntime world) => world.Inhabitants
        .Where(person => person.Housing?.Relocation is not null).OrderBy(person => person.InhabitantId, StringComparer.Ordinal).ToArray();

    public sealed class Choices : IDecisionProvider
    {
        public ConcurrentDictionary<string, string> Wanted { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, string> HousingNotes { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, string[]> Offered { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            if (observation.Self?.HousingNote is { } housingNote)
                HousingNotes[observation.InhabitantId] = housingNote;
            Offered[observation.InhabitantId] = observation.Candidates.Select(candidate => candidate.Id).ToArray();
            var wanted = Wanted.GetValueOrDefault(observation.InhabitantId, "safe_idle");
            var selected = observation.Candidates.FirstOrDefault(candidate => candidate.Id == wanted) ??
                observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = observation with { Candidates = [selected] },
            }, cancellationToken);
        }
    }
}
