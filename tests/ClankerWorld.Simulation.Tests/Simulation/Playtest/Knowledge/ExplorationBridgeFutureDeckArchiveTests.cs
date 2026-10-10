using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using Xunit.Abstractions;

namespace ClankerWorld.Simulation.Tests;

public sealed class ExplorationBridgeFutureDeckArchiveTests(ITestOutputHelper output)
{
    private static readonly GridPoint FutureDeck = new(55, 5);
    private static readonly GridPoint RoadBank = new(55, 4);
    private static readonly GridPoint WorkshopSite = new(55, 8);
    private static readonly string[] HistoricalFaults = ["visited", "discovery"];

    [Fact]
    public async Task LaterBridgeCannotValidateArchivedFootMemoriesButFreshwaterAllowsAFinalPosition()
    {
        using var world = await BuildCornerBridgeAfterActualScoutingAndNaturalDeath();
        var state = world.ExportState();
        var deceased = Assert.Single(state.DeceasedInhabitants!);
        var exploration = Assert.IsType<SettlementExploration>(deceased.LastPhysical.Exploration);
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        using (var strict = Restore(PrivateWorldRuntimeCodec.Decode(bytes)))
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(strict.ExportState()));

        // The real archive is valid. Only one historical point is changed in
        // each malformed copy, onto a deck which did not exist at death.
        Assert.All(HistoricalFaults, fault =>
        {
            var physical = fault switch
            {
                "visited" => deceased.LastPhysical with
                {
                    Exploration = exploration with
                    {
                        VisitedTiles = exploration.VisitedTiles.Append(FutureDeck).ToArray(),
                    },
                },
                _ => deceased.LastPhysical with
                {
                    Exploration = exploration with
                    {
                        OutingDiscoveries = (exploration.OutingDiscoveries ?? []).Append(FutureDeck).ToArray(),
                    },
                },
            };
            var malformed = state with { DeceasedInhabitants = [deceased with { LastPhysical = physical }] };
            var encodeError = Record.Exception(() => PrivateWorldRuntimeCodec.Encode(malformed));
            var restoreError = Record.Exception(() => { using var restored = Restore(malformed); });
            output.WriteLine($"Historical {fault} on future-only deck {FutureDeck}: codec={encodeError?.GetType().Name ?? "ACCEPTED"}, direct={restoreError?.GetType().Name ?? "ACCEPTED"}.");
            const string expected = "local exploration record";
            Assert.Contains(expected, Assert.IsType<InvalidDataException>(encodeError).Message, StringComparison.Ordinal);
            Assert.Contains(expected, Assert.IsType<InvalidDataException>(restoreError).Message, StringComparison.Ordinal);
        });
        // Wide freshwater can now hold a swimmer's final body position even
        // without the later bridge. It still cannot manufacture foot scouting memories.
        var waterPosition = state with
        {
            DeceasedInhabitants = [deceased with { LastPhysical = deceased.LastPhysical with { Position = FutureDeck } }],
        };
        using var savedSwimmer = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(waterPosition)));
        Assert.Equal(FutureDeck, Assert.Single(savedSwimmer.ExportState().DeceasedInhabitants!).LastPhysical.Position);
    }

    private async Task<PrivateWorldRuntime> BuildCornerBridgeAfterActualScoutingAndNaturalDeath()
    {
        using var scouting = ExplorationBridgeSaveTests.CreateScoutingWorld();
        var actor = scouting.Inhabitants[0].InhabitantId;
        for (var tick = 0; tick < 400 && Scout(scouting, actor).Exploration!.OutingPath.Count < 3; tick++)
        {
            var previous = Scout(scouting, actor).Position;
            Assert.True((await scouting.AdvanceOneTickAsync()).Advanced);
            var current = Scout(scouting, actor).Position;
            if (previous != current) Assert.True(scouting.ExportState().Map.CanFootStep(previous, current));
        }
        var walking = scouting.ExportState();
        var exploration = Assert.IsType<SettlementExploration>(Scout(scouting, actor).Exploration);
        Assert.Equal([new GridPoint(0, 101), new GridPoint(0, 102), new GridPoint(0, 103)], exploration.OutingPath);
        Assert.False(walking.Map.IsPassable(FutureDeck));
        Assert.True(RiverBridgeRules.TryResolve(walking.Map, "bridge-55-5-ns-2", out var crossing));
        Assert.Equal([RoadBank, new GridPoint(55, 7)], crossing!.Entrances);
        Assert.Equal([FutureDeck, new GridPoint(55, 6)], crossing.Span);
        Assert.True(walking.Map.IsBuildable(WorkshopSite));
        Assert.DoesNotContain(walking.Map.Resources, resource => crossing.Entrances.Contains(resource.Position) || resource.Position == WorkshopSite);
        using var aging = Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(AtNaturalLifeBoundary(walking, actor))));
        Assert.True((await aging.AdvanceOneTickAsync()).Advanced);
        var dead = aging.Society.GetInhabitant(actor);
        Assert.Equal(SocietyInhabitantStatus.Dead, dead.Status);
        Assert.Equal(SocietyDeathCause.NaturalAge, dead.DeathCause);
        var deceased = Assert.Single(aging.ExportState().DeceasedInhabitants!);
        Assert.Equal(exploration.OutingPath, deceased.LastPhysical.Exploration!.OutingPath);
        var naturalArchive = PrivateWorldRuntimeCodec.Encode(aging.ExportState());
        using (var strict = Restore(PrivateWorldRuntimeCodec.Decode(naturalArchive)))
            Assert.Equal(naturalArchive, PrivateWorldRuntimeCodec.Encode(strict.ExportState()));
        Assert.True((await aging.AdvanceOneTickAsync()).Advanced);
        var before = aging.ExportState();
        Assert.Empty(before.Bridges!);
        var town = Assert.Single(before.Towns!);
        // Like the primary bridge fixture, add a strictly validated street
        // frontage. Generated terrain/resources and actual archive stay intact.
        before = before with
        {
            RoadTiles = before.RoadTiles!.Append(RoadBank).Distinct().OrderBy(point => point.Y).ThenBy(point => point.X).ToArray(),
            Towns = [town with { BorderTiles = TownBorderRules.Expand(before.Map, town, [RoadBank, WorkshopSite]) }],
        };
        var preparedBytes = PrivateWorldRuntimeCodec.Encode(before);
        var world = Restore(PrivateWorldRuntimeCodec.Decode(preparedBytes));
        try
        {
            Assert.Equal(preparedBytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
            var woodBefore = Wood(world.ExportState());
            var workshop = world.WorldContent.Buildings.Single(item => item.LocalId == "workshop");
            Assert.Equal(10, Assert.Single(workshop.BuildCosts).Amount);
            var placement = world.PlaceBuilding("future-deck-archive-workshop", workshop.CanonicalId, WorkshopSite);
            Assert.True(placement.Applied, placement.Failure);
            var bridge = Assert.Single(world.Bridges, item => item.Id == crossing.Id);
            Assert.Equal(BridgeTriggers.Road, bridge.Trigger);
            Assert.True(bridge.BuiltTick > deceased.DeathTick);
            var after = world.ExportState();
            Assert.Equal(woodBefore - 10, Wood(after));
            Assert.False(before.Map.IsPassable(FutureDeck));
            Assert.True(after.Map.IsPassable(FutureDeck));
            Assert.Equal(JsonSerializer.Serialize(deceased), JsonSerializer.Serialize(Assert.Single(after.DeceasedInhabitants!)));
            Assert.Equal(JsonSerializer.Serialize(before.Knowledge!.Facts), JsonSerializer.Serialize(after.Knowledge!.Facts));
            output.WriteLine($"Actual scouting began {exploration.LastOutingTick}, natural death {deceased.DeathTick}, public paid corner bridge {bridge.Id} built {bridge.BuiltTick}; wood10 spent; deck {FutureDeck} becomes passable only after death.");
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    private static PrivateWorldRuntimeState AtNaturalLifeBoundary(PrivateWorldRuntimeState state, string actor)
    {
        var society = state.Society.Society;
        var nextLifeTick = society.Config.TicksPerLifecycleAge - 1;
        var delta = nextLifeTick - society.LifeTickAt(society.WorldTick);
        var days = society.Config.DayLifecycle!.MaximumDay - 1;
        return state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    LifeClock = new SocietyLifeClock(society.LifeClock?.Rate ?? 1, society.WorldTick, nextLifeTick),
                    Inhabitants = society.Inhabitants.Select(person => person.Id == actor ? person with
                    {
                        BirthLifeTick = nextLifeTick + 1 - (days + 1) * society.Config.TicksPerLifecycleAge,
                        AgeBand = SocietyAgeBand.Elder,
                        LastLifecycleYearChecked = days,
                    } : person with { BirthLifeTick = (person.BirthLifeTick ?? person.BirthTick) + delta }).ToArray(),
                },
            },
        };
    }

    private static int Wood(PrivateWorldRuntimeState state) => state.Society.Society.Inventory.Lots
        .Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity);
    private static PlaytestInhabitantState Scout(PrivateWorldRuntime world, string actor) =>
        world.Inhabitants.Single(person => person.InhabitantId == actor);
    private static PrivateWorldRuntime Restore(PrivateWorldRuntimeState state) => PrivateWorldRuntime.Restore(state, Provider);
    private static IDecisionProvider Provider(string actor) => new IdleProvider();

    private sealed class IdleProvider : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var choice = request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
