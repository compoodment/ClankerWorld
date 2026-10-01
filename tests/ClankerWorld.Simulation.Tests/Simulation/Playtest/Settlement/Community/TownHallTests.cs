using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class TownHallTests
{
    private const string Alpha = "household:camp-alpha";
    private const string Town = TownBorderRules.FirstTownId;

    [Fact]
    public async Task OrdinaryTownHallConstructionConsumesPhysicalSuppliesAndBelongsToItsTown()
    {
        var state = Initial();
        var builder = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "hall-wood", "wood", Alpha, 24, storageBuildingId: "first-town-house-a");
        inventory = InventoryFixture.AddLot(inventory, "hall-stone", "stone", Alpha, 12, storageBuildingId: "first-town-house-a");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var world = PrivateWorldRuntime.Restore(state, actor => new CivicChooser(actor == builder, vote: false));
        for (var tick = 0; tick < 240 && !world.WorldSimulation.Buildings.Any(building => building.DefinitionId == TownHallContent.TownHall().CanonicalId); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var hall = Assert.Single(world.WorldSimulation.Buildings, building => building.DefinitionId == TownHallContent.TownHall().CanonicalId);
        Assert.Equal(Town, hall.TownId);
        Assert.Null(hall.HouseholdId);
        Assert.Equal(12, WorldContentSimulationRules.Footprint(TownHallContent.TownHall(), hall).Count());
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "hall-stone");
        Assert.Equal(state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity) - 24,
            world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
        Assert.Contains(hall.InstanceId, world.Towns.Single().AssignedBuildingIds);
        var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var loaded = PrivateWorldRuntime.Restore(saved);
        var visible = Assert.Single(new OwnerWorldObservationStore(loaded).GetSnapshot().TownCouncils);
        Assert.Equal(hall.InstanceId, visible.HallId);
        Assert.Equal(4, visible.MemberIds.Count);
        Assert.Null(visible.TermExpiryTick);
        loaded.Validate();
    }

    [Fact]
    public async Task OrdinaryRuleVotingRequiresPhysicalAttendanceAndKeepsPrivateGoodsOwned()
    {
        var state = WithHall(Initial());
        var inventory = state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Select(lot =>
            FoodItems.IsEdible(lot.ItemKind) ? lot with { Quantity = 1 } : lot).ToArray()
        };
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        var privateOwners = state.Society.Society.Inventory.Lots.ToDictionary(lot => lot.Id, lot => lot.OwnerId);
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(build: false, vote: true));
        for (var tick = 0; tick < 480 && world.TownCouncils.SingleOrDefault()?.FoodPolicy != "essential_first"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var adopted = Assert.Single(world.TownCouncils);
        Assert.Equal("essential_first", adopted.FoodPolicy);
        Assert.Contains(adopted.Laws!, law => law.Key == "shared_food");
        Assert.Contains(world.Society.Memories, memory => memory.Id.StartsWith("town-rule:", StringComparison.Ordinal) &&
            memory.Summary.Contains("at its Hall", StringComparison.Ordinal));
        Assert.All(world.Society.Inventory.Lots, lot => Assert.Equal(privateOwners[lot.Id], lot.OwnerId));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "town_law_vote_recorded");
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())));
        Assert.Equal("essential_first", loaded.TownCouncils.Single().FoodPolicy);
        loaded.Validate();
    }

    [Fact]
    public async Task SplitCouncilKeepsExistingRuleAcrossReloadAndTheProvisionalRepealActionUsesTheSameMajority()
    {
        var state = CivicCalendar(AtHall(WithHall(Initial())));
        var adults = state.Society.Society.Inhabitants.Select(person => person.Id).ToArray();
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        Assert.True(world.ProposeTownLaw(adults[0], Town, "protected_grove", "Ask before felling Town trees.").Applied);
        Assert.True(world.VoteTownLaw(adults[0], Town, true).Applied);
        Assert.True(world.VoteTownLaw(adults[1], Town, true).Applied);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new CivicChooser(false, false));
        Assert.True(loaded.VoteTownLaw(adults[2], Town, false).Applied);
        Assert.True(loaded.VoteTownLaw(adults[3], Town, false).Applied);
        Assert.Null(loaded.TownCouncils.Single().Ballot);
        Assert.Empty(loaded.TownCouncils.Single().Laws!);
        Assert.Equal("open", loaded.TownCouncils.Single().FoodPolicy);
        Assert.False(loaded.ProposeTownLaw(adults[0], Town, "protected_grove", "Ask before felling Town trees.").Applied);
        await AdvanceTo(loaded, 24);
        Assert.True(loaded.ProposeTownLaw(adults[0], Town, "protected_grove", "Ask before felling Town trees.").Applied);
        Assert.True(loaded.VoteTownLaw(adults[0], Town, true).Applied);
        Assert.True(loaded.VoteTownLaw(adults[1], Town, true).Applied);
        Assert.True(loaded.VoteTownLaw(adults[2], Town, true).Applied);
        Assert.Single(loaded.TownCouncils.Single().Laws!);
        Assert.True(loaded.ProposeTownLaw(adults[0], Town, "protected_grove", "Repeal the tree request.", repeal: true).Applied);
        Assert.True(loaded.VoteTownLaw(adults[0], Town, true).Applied);
        Assert.True(loaded.VoteTownLaw(adults[1], Town, true).Applied);
        Assert.True(loaded.VoteTownLaw(adults[2], Town, true).Applied);
        Assert.Empty(loaded.TownCouncils.Single().Laws!);
        loaded.Validate();
    }

    [Fact]
    public async Task EightAdultsAttendTheHallRegisterAndElectThreeForTenDaysAcrossReload()
    {
        var state = CivicCalendar(WithHall(AddAdults(Initial(), 4)), 100);
        var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, true));
        try
        {
            var restarted = false;
            for (var tick = 0; tick < 600 && world.TownCouncils.SingleOrDefault()?.TermStartedTick is null; tick++)
            {
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
                if (!restarted && world.TownCouncils.Single().Election?.Votes.Count > 0)
                {
                    var saved = PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState()));
                    world.Dispose();
                    world = PrivateWorldRuntime.Restore(saved, _ => new CivicChooser(false, true));
                    restarted = true;
                }
            }
            Assert.True(restarted);
            var elected = Assert.Single(world.TownCouncils);
            Assert.Equal(3, elected.MemberIds.Count);
            Assert.Null(elected.Election);
            Assert.Equal(10L * world.WorldSystems.Config.TicksPerDay,
                elected.TermExpiryTick - elected.TermStartedTick);
            Assert.Equal(8, world.ExportState().Events.Where(item => item.Kind == "town_election_vote_recorded")
                .Select(item => item.Detail.Split('|')[1]).Distinct().Count());
            Assert.Equal(301, elected.TermStartedTick);
            Assert.Equal(8, world.ExportState().Events.Count(item => item.Kind == "town_candidacy_changed"));
            Assert.All(elected.MemberIds, id => Assert.Contains(id, world.Towns.Single().ResidentIds));
            world.Validate();
        }
        finally { world.Dispose(); }
    }

    [Fact]
    public void EightAdultsAcrossTwoTownsDoNotCreateAGlobalElectorate()
    {
        var state = AtHall(WithHall(AddAdults(Initial(), 4)));
        var first = state.Towns!.Single();
        var outsiders = first.ResidentIds.Skip(4).ToArray();
        state = state with
        {
            Towns = [first with { ResidentIds = first.ResidentIds.Take(4).ToArray() },
            first with { Id = "town:visitors", Name = "Visitor Town", ResidentIds = outsiders, AssignedBuildingIds = [] }],
            TownCouncils = [new(Town, "open", 0, first.ResidentIds.Take(4).ToArray(), Laws: []),
                new("town:visitors", "open", 0, outsiders, Laws: [])]
        };
        using var world = PrivateWorldRuntime.Restore(state);
        var local = first.ResidentIds[0];
        Assert.True(world.ProposeTownLaw(local, Town, "protected_grove", "Ask before cutting trees.").Applied);
        Assert.Equal(4, world.TownCouncils.Single(council => council.TownId == Town).MemberIds.Count);
        Assert.All(world.TownCouncils, council => Assert.Null(council.Election));
        Assert.False(world.VoteTownLaw(outsiders[0], Town, true).Applied);
        Assert.Empty(world.TownCouncils.Single(council => council.TownId == Town).Ballot!.Approvals);
        Assert.False(world.ProposeTownLaw(local, "town:visitors", "other_town", "Take their goods.").Applied);
        world.Validate();
    }

    [Fact]
    public async Task RegularElectionOpensBeforeTheTenDayTermEndsAndPreservesIncumbentsUntilVotingFinishes()
    {
        var state = RegisteredElection();
        var adults = CivicAdults(state.Towns!.Single().ResidentIds);
        var selected = adults.Take(3).ToArray();
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        Assert.Equal(selected.Order(StringComparer.Ordinal), world.TownCouncils.Single().Election!.Candidates);
        foreach (var actor in adults) Assert.True(world.VoteTownElection(actor, Town, selected).Applied);
        Assert.Null(world.TownCouncils.Single().TermExpiryTick);
        await AdvanceTo(world, 24);
        Assert.Equal(264, world.TownCouncils.Single().TermExpiryTick);
        var next = adults.Skip(3).Take(3).ToArray();
        foreach (var nominee in next) Assert.True(world.VolunteerTownCouncil(nominee, Town).Applied);
        await AdvanceTo(world, 239);
        Assert.Null(world.TownCouncils.Single().Election);
        await AdvanceTo(world, 240);
        var current = world.TownCouncils.Single();
        Assert.NotNull(current.Election);
        Assert.Equal(selected, current.MemberIds);
        Assert.Equal(24, current.TermStartedTick);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(world.ExportState())), _ => new CivicChooser(false, false));
        foreach (var actor in adults) Assert.True(loaded.VoteTownElection(actor, Town, next).Applied);
        Assert.Equal(selected, loaded.TownCouncils.Single().MemberIds);
        await AdvanceTo(loaded, 264);
        Assert.Equal(next.Order(StringComparer.Ordinal), loaded.TownCouncils.Single().MemberIds);
        Assert.Equal(504, loaded.TownCouncils.Single().TermExpiryTick);
        loaded.Validate();
    }

    [Fact]
    public async Task RealDeathsOpenReplacementVotingKeepRepresentationBelowEightAndFallBackAtThree()
    {
        var state = RegisteredElection(extra: 5);
        var adults = CivicAdults(state.Towns!.Single().ResidentIds);
        var selected = adults.Take(3).ToArray();
        using (var setup = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false)))
        {
            foreach (var actor in setup.TownCouncils.Single().Election!.Electorate.ToArray()) Assert.True(setup.VoteTownElection(actor, Town, selected).Applied);
            await AdvanceTo(setup, 24);
            state = setup.ExportState();
        }
        state = DiesNextTick(state, selected[0]);
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(SocietyInhabitantStatus.Dead, world.Society.GetInhabitant(selected[0]).Status);
        Assert.Equal(8, world.Towns.Single().ResidentIds.Count);
        Assert.Equal("replacement", world.TownCouncils.Single().Election!.Kind);
        Assert.Equal(2, world.TownCouncils.Single().MemberIds.Count);
        Assert.DoesNotContain(selected[0], world.TownCouncils.Single().MemberIds);
        Assert.Equal(264, world.TownCouncils.Single().TermExpiryTick);
        state = DiesNextTick(world.ExportState(), selected[1]);
        using var loaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => new CivicChooser(false, false));
        Assert.True((await loaded.AdvanceOneTickAsync()).Advanced);
        Assert.Single(loaded.TownCouncils.Single().MemberIds);
        Assert.Equal("representative", loaded.TownCouncils.Single().GoverningForm);
        Assert.Equal(264, loaded.TownCouncils.Single().TermExpiryTick);
        Assert.Single(new OwnerWorldObservationStore(loaded).GetSnapshot().TownCouncils.Single().MemberNames);
        loaded.Validate();
        state = loaded.ExportState();
        foreach (var actor in adults.Skip(3).Take(4))
        {
            using var mortality = PrivateWorldRuntime.Restore(DiesNextTick(state, actor), _ => new CivicChooser(false, false));
            Assert.True((await mortality.AdvanceOneTickAsync()).Advanced);
            state = mortality.ExportState();
        }
        using var reduced = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)));
        Assert.Equal(3, reduced.TownCouncils.Single().MemberIds.Count);
        Assert.Equal("collective", reduced.TownCouncils.Single().GoverningForm);
        Assert.Equal("population", reduced.TownCouncils.Single().FallbackReason);
        Assert.Null(reduced.TownCouncils.Single().TermStartedTick);
        Assert.Null(reduced.TownCouncils.Single().Election);
        reduced.Validate();
    }

    [Fact]
    public void AResidentChildAtTheHallHasNoCouncilVote()
    {
        var state = AtHall(WithHall(AddAdults(Initial(), 4)));
        var child = ExtraAdultId(0);
        var config = state.Society.Society.Config;
        var age = config.DayLifecycle!.AdultStartDay - 1;
        var birth = -age * config.TicksPerLifecycleAge;
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person.Id == child
                    ? person with
                    {
                        BirthTick = birth,
                        BirthLifeTick = state.Society.Society.LifeClock is null ? null : birth,
                        AgeBand = SocietyAgeBand.Child,
                        LastLifecycleYearChecked = age
                    }
                    : person).ToArray()
                }
            },
            TownCouncils = state.TownCouncils!.Select(council => council with
            { MemberIds = council.MemberIds.Where(id => id != child).ToArray(), Election = null }).ToArray()
        };
        using var world = PrivateWorldRuntime.Restore(state);
        var adult = state.Inhabitants.First(person => person.InhabitantId != child).InhabitantId;
        Assert.True(world.ProposeTownLaw(adult, Town, "quiet_meetings", "Let each speaker finish.").Applied);
        Assert.Equal(7, world.TownCouncils.Single().MemberIds.Count);
        Assert.False(world.VoteTownLaw(child, Town, true).Applied);
        Assert.False(world.ProposeTownLaw(child, Town, "child_rule", "A child cannot impose this rule.").Applied);
        Assert.False(world.VoteTownElection(child, Town, [adult]).Applied);
        world.Validate();
    }

    [Fact]
    public async Task RemoteOrRepeatedCandidateVotesAndForgedTermsAreRefusedWhileBallotsCanBeRevised()
    {
        var state = RegisteredElection();
        var adults = CivicAdults(state.Towns!.Single().ResidentIds);
        using (var nominations = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false)))
        {
            state = nominations.ExportState();
        }
        var hall = state.WorldSimulation!.Buildings.Single(building => building.DefinitionId == TownHallContent.TownHall().CanonicalId);
        var footprint = WorldContentSimulationRules.Footprint(TownHallContent.TownHall(), hall).ToArray();
        var occupied = state.Inhabitants.Where(person => person.InhabitantId != adults[0])
            .Select(person => person.Position).ToHashSet();
        var remotePosition = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsPassable(point) &&
            footprint.All(tile => state.Map.FootDistance(point, tile) > 1) && !occupied.Contains(point) &&
            !state.Map.Resources.Any(resource => resource.Position == point) &&
            !state.Map.CampObjects.Any(item => item.Position == point));
        Assert.All(footprint, tile => Assert.True(state.Map.FootDistance(remotePosition, tile) > 1));
        var remoteState = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == adults[0]
                ? person with { Position = remotePosition } : person).ToArray()
        };
        using (var remote = PrivateWorldRuntime.Restore(remoteState))
            Assert.False(remote.VoteTownElection(adults[0], Town, adults.Take(3).ToArray()).Applied);
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        Assert.True(world.VoteTownElection(adults[0], Town, adults.Take(3).ToArray()).Applied);
        Assert.True(world.VoteTownElection(adults[0], Town, adults.Take(2).ToArray()).Applied);
        Assert.Equal(adults.Take(2), world.TownCouncils.Single().Election!.Votes.Single(vote => vote.VoterId == adults[0]).CandidateIds);
        Assert.True(world.VoteTownElection(adults[0], Town, adults.Take(3).ToArray()).Applied);
        Assert.False(world.VoteTownElection(adults[1], Town, [adults[0], adults[0]]).Applied);
        foreach (var actor in adults.Skip(1)) Assert.True(world.VoteTownElection(actor, Town, adults.Take(3).ToArray()).Applied);
        Assert.NotNull(world.TownCouncils.Single().Election);
        await AdvanceTo(world, 24);
        state = world.ExportState();
        var elected = state.TownCouncils!.Single();
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        { TownCouncils = [elected with { TermExpiryTick = elected.TermExpiryTick + 1 }] }));
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(state with
        { TownCouncils = [elected with { MemberIds = elected.MemberIds.Append("absent-person").ToArray() }] }));
    }

    private static async Task AdvanceTo(PrivateWorldRuntime world, long tick)
    {
        while (world.WorldTick < tick) Assert.True((await world.AdvanceOneTickAsync()).Advanced);
    }

    private static PrivateWorldRuntimeState CivicCalendar(PrivateWorldRuntimeState state, int day = 24)
    {
        var config = state.WorldSystems!.Config with
        { TicksPerDay = day, DaysPerYear = 4, SpringDays = 1, SummerDays = 1, AutumnDays = 1, WinterDays = 1 };
        var societyConfig = state.Society.Society.Config with { TicksPerWorldDay = day, DaysPerWorldYear = 4 };
        var birth = -societyConfig.TicksPerLifecycleAge * societyConfig.FounderStartingAge;
        return state with
        {
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems with { Config = config, RegionalWeather = null }, state.Map),
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Config = societyConfig,
                    Inhabitants = state.Society.Society.Inhabitants.Select(person => person with
                    {
                        BirthTick = birth,
                        BirthLifeTick = state.Society.Society.LifeClock is null ? null : birth,
                        LastLifecycleYearChecked = societyConfig.FounderStartingAge
                    }).ToArray()
                }
            }
        };
    }

    private static PrivateWorldRuntimeState Initial()
    {
        using var world = NormalPathWorld.CreateGenerated("probe-a", _ => new CivicChooser(false, false));
        return world.ExportState() with
        {
            Survival = new SettlementSurvivalState(0, []),
            Inhabitants = world.Inhabitants.Select(person => person with { HungerBasisPoints = 9_000, Survival = new() }).ToArray()
        };
    }

    private static PrivateWorldRuntimeState AddAdults(PrivateWorldRuntimeState state, int count)
    {
        using var world = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
        var house = world.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
        for (var index = 0; index < count; index++)
            Assert.Equal(Alpha, world.AddAgent(ExtraAdultId(index), house.Position));
        return world.ExportState();
    }

    private static PrivateWorldRuntimeState RegisteredElection(int extra = 4, int day = 24, params int[] nomineeIndexes)
    {
        var state = CivicCalendar(AtHall(WithHall(Initial())), day);
        var indexes = nomineeIndexes.Length == 0 ? [0, 1, 2] : nomineeIndexes;
        using (var register = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false)))
        {
            var adults = CivicAdults(register.Towns.Single().ResidentIds);
            foreach (var index in indexes.Where(index => index < 4))
                Assert.True(register.VolunteerTownCouncil(adults[index], Town).Applied);
            state = register.ExportState();
        }
        for (var index = 0; index < extra; index++)
        {
            using var added = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
            var house = added.WorldSimulation.Buildings.Single(building => building.InstanceId == "first-town-house-a");
            Assert.Equal(Alpha, added.AddAgent(ExtraAdultId(index), house.Position));
            state = AtHall(added.ExportState());
            if (indexes.Contains(index + 4))
            {
                using var registered = PrivateWorldRuntime.Restore(state, _ => new CivicChooser(false, false));
                Assert.True(registered.VolunteerTownCouncil(ExtraAdultId(index), Town).Applied);
                state = registered.ExportState();
            }
        }
        return state;
    }

    private static string[] CivicAdults(IEnumerable<string> ids) => ids
        .OrderBy(id => id.StartsWith("founder:", StringComparison.Ordinal) ? 0 : 1)
        .ThenBy(id => id, StringComparer.Ordinal).ToArray();

    private static string ExtraAdultId(int index) => "agent:" + (index + 100).ToString("x32", System.Globalization.CultureInfo.InvariantCulture);

    private static PrivateWorldRuntimeState WithHall(PrivateWorldRuntimeState state)
    {
        var inventory = InventoryFixture.AddLot(state.Society.Society.Inventory, "hall-wood", "wood", Alpha, 24, storageBuildingId: "first-town-house-a");
        inventory = InventoryFixture.AddLot(inventory, "hall-stone", "stone", Alpha, 12, storageBuildingId: "first-town-house-a");
        state = state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };
        using var world = PrivateWorldRuntime.Restore(state);
        var hall = world.WorldContent.Buildings.Single(definition => definition.CanonicalId == TownHallContent.TownHall().CanonicalId);
        foreach (var position in world.Towns.Single().BorderTiles)
        {
            var placed = world.PlaceBuilding("test-town-hall", hall.CanonicalId, position);
            if (placed.Applied) return world.ExportState();
        }
        throw new InvalidOperationException("No legal shared Hall placement was found.");
    }

    private static PrivateWorldRuntimeState AtHall(PrivateWorldRuntimeState state)
    {
        var hall = state.WorldSimulation!.Buildings.Single(building => building.DefinitionId == TownHallContent.TownHall().CanonicalId);
        var footprint = WorldContentSimulationRules.Footprint(TownHallContent.TownHall(), hall).ToArray();
        var positions = state.Map.Tiles.Select(tile => tile.Position).Where(point => state.Map.IsPassable(point) &&
            footprint.Any(tile => state.Map.FootDistance(point, tile) <= 1) &&
            !state.Map.Resources.Any(resource => resource.Position == point)).Take(state.Inhabitants.Count).ToArray();
        Assert.Equal(state.Inhabitants.Count, positions.Length);
        return state with { Inhabitants = state.Inhabitants.Select((person, index) => person with { Position = positions[index] }).ToArray() };
    }

    private static PrivateWorldRuntimeState DiesNextTick(PrivateWorldRuntimeState state, string actor)
    {
        var society = state.Society.Society;
        var maxAge = society.Config.DayLifecycle!.MaximumDay;
        var birth = society.LifeTickAt(society.WorldTick) - maxAge * society.Config.TicksPerLifecycleAge + 1;
        return state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person =>
            person.Id == actor ? person with
            {
                BirthTick = birth,
                BirthLifeTick = society.LifeClock is null ? null : birth,
                AgeBand = SocietyAgeBand.Elder,
                LastLifecycleYearChecked = maxAge - 1
            } : person).ToArray()
                }
            }
        };
    }

    private sealed class CivicChooser(bool build, bool vote) : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var selected = vote ? request.Observation.Candidates.FirstOrDefault(candidate =>
                candidate.Id is "council_town_visit" or "council_town_volunteer" or "council_town_vote_yes" ||
                candidate.Id.StartsWith("council_town_propose:", StringComparison.Ordinal) || candidate.Id.StartsWith("council_town_support:", StringComparison.Ordinal)) : null;
            selected ??= build ? request.Observation.Candidates.FirstOrDefault(candidate => TownConstructionCandidateIds.TryParse(candidate.Id, out var selection) &&
                selection.IsBuilding && selection.DefinitionId == TownHallContent.TownHall().CanonicalId) : null;
            selected ??= request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind,
                ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected.Id, 1, new Dictionary<string, double> { [selected.Id] = 1 }));
        }
    }
}
