using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ChildSocialCooldownTargetsTests
{
    private static readonly Lazy<Task<byte[]>> CooledChild = new(CreateCooledChild);

    // The existing childhood checks prove actions and age gates, but not target admission
    // after cooldown. Real birth, nine native actions and a fourth actual encounter catch
    // an unavailable nearer person consuming one of the three child-social target slots.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CooledNearbyContactsDoNotHideAnotherReachablePlaymate(bool moveCooledContact)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await CooledChild.Value);
        var child = Assert.Single(state.Society.Society.Births).ChildId;
        var adults = state.Inhabitants.Where(person => person.InhabitantId != child)
            .Select(person => person.InhabitantId).Order(StringComparer.Ordinal).ToArray();
        var center = state.Inhabitants.Single(person => person.InhabitantId == child).Position;
        if (moveCooledContact)
            state = state with
            {
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == adults[2]
                ? person with { Position = new(center.X, center.Y + 3) } : person).ToArray()
            };
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        var provider = new SocialChoices { Wanted = "child_play:" + adults[3] };
        var replayProvider = new SocialChoices { Wanted = provider.Wanted };
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => provider);
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => replayProvider);
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        await world.AdvanceOneTickAsync();
        await replay.AdvanceOneTickAsync();
        Assert.Contains(provider.Wanted, provider.ChildCandidates);
        Assert.DoesNotContain(provider.ChildCandidates, candidate => adults.Take(3).Any(id => candidate.EndsWith(":" + id, StringComparison.Ordinal)));
        for (var tick = 0; tick < 16 && !world.Society.Memories.Any(memory =>
                 memory.OwnerId == child && memory.SubjectId == adults[3] && memory.Id.StartsWith("child-social:play:", StringComparison.Ordinal)); tick++)
        {
            await world.AdvanceOneTickAsync();
            await replay.AdvanceOneTickAsync();
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Single(world.Society.Memories, memory => memory.OwnerId == child && memory.SubjectId == adults[3] &&
            memory.Id.StartsWith("child-social:play:", StringComparison.Ordinal));
        Assert.Equal(9, world.Society.Memories.Count(memory => memory.OwnerId == child && adults.Take(3).Contains(memory.SubjectId) &&
            memory.Id.StartsWith("child-social:", StringComparison.Ordinal)));
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    private static async Task<byte[]> CreateCooledChild()
    {
        using var initial = NormalPathWorld.CreateGenerated("child-cooldown-targets", _ => new SocialChoices());
        var state = initial.ExportState();
        var society = state.Society.Society;
        var parents = society.GetHousehold("household:camp-alpha").MemberIds.Order(StringComparer.Ordinal).ToArray();
        var partnership = "child-cooldown-partnership";
        society = SocietyFixture.ProposeRelationship(society,
            new(partnership, 1, SocietyRelationshipType.Partnership, parents[0], parents[1], society.WorldTick)).Checkpoint;
        society = SocietyFixture.AcceptRelationship(society, partnership, 1, parents[1]).Checkpoint;
        society = ChosenBirthNameTestFixture.NameParent(society, parents[0]);
        var food = InventoryFixture.AddLot(society.Inventory, "child-cooldown-birth-food", "food", "household:camp-alpha", 4,
            storageBuildingId: "first-town-house-a");
        society = society with { Inventory = food };
        var birth = SocietyFixture.CommitBirth(society, new SocietyBirthRequest("child-cooldown-birth", 1, parents[0], parents[1],
            "household:camp-alpha", parents, parents, "child-cooldown-birth-food", 4, society.WorldTick,
            ChildName: ChosenBirthNameTestFixture.ChildName(society, parents[0], "Robin"), PrimaryCaregiverId: parents[0]));
        society = birth.Checkpoint;
        var child = Assert.IsType<string>(birth.CreatedId);
        var birthTime = society.LifeTickAt(society.WorldTick) - 4 * society.Config.TicksPerLifecycleAge;
        society = society with
        {
            Inhabitants = society.Inhabitants.Select(person => person.Id == child ? person with
            {
                BirthTick = birthTime,
                BirthLifeTick = society.LifeClock is null ? null : birthTime,
                AgeBand = SocietyAgeBand.Child,
                LastLifecycleYearChecked = 4,
            } : person).ToArray()
        };
        var taken = initial.WorldSimulation.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(
            initial.WorldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building)).ToHashSet();
        var center = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            Enumerable.Range(-3, 7).SelectMany(y => Enumerable.Range(-3, 7).Select(x => new GridPoint(point.X + x, point.Y + y)))
                .All(tile => state.Map.IsBuildable(tile) && !taken.Contains(tile)));
        var adults = state.Inhabitants.Select(person => person.InhabitantId).Order(StringComparer.Ordinal).ToArray();
        var positions = new[] { new GridPoint(center.X - 1, center.Y), new(center.X, center.Y - 1),
            new(center.X, center.Y + 1), new(center.X + 2, center.Y) };
        state = state with
        {
            Society = state.Society with { Society = society },
            Survival = new(society.WorldTick, []),
            Inhabitants = state.Inhabitants.Append(new(child, center, 10_000, 0, "curious", "grow with the household", Survival: new(10_000)))
                .Select(person => person with
                {
                    Position = person.InhabitantId == child ? center : positions[Array.IndexOf(adults, person.InhabitantId)],
                    HungerBasisPoints = 10_000,
                    Survival = new(10_000),
                    LastDecisionContext = null,
                    TravelCooldownTicks = 0,
                }).ToArray(),
            Towns = state.Towns!.Select(town => town with { ResidentIds = town.ResidentIds.Append(child).Order(StringComparer.Ordinal).ToArray() }).ToArray(),
        };
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);
        var provider = new SocialChoices();
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => provider);
        foreach (var adult in adults.Take(3))
            foreach (var kind in new[] { "converse", "play", "learn" })
            {
                provider.Wanted = "child_" + kind + ":" + adult;
                for (var tick = 0; tick < 16 && !world.Society.Memories.Any(memory => memory.OwnerId == child && memory.SubjectId == adult &&
                         memory.Id.StartsWith("child-social:" + kind + ":", StringComparison.Ordinal)); tick++)
                    await world.AdvanceOneTickAsync();
                Assert.Single(world.Society.Memories, memory => memory.OwnerId == child && memory.SubjectId == adult &&
                    memory.Id.StartsWith("child-social:" + kind + ":", StringComparison.Ordinal));
            }
        var cooled = world.ExportState();
        return PrivateWorldRuntimeCodec.Encode(cooled with
        {
            Inhabitants = cooled.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == child ? center : positions[Array.IndexOf(adults, person.InhabitantId)],
                LastDecisionContext = null,
                TravelCooldownTicks = 0,
            }).ToArray()
        });
    }

    private sealed class SocialChoices : IDecisionProvider
    {
        public string Wanted { get; set; } = "safe_idle";
        public HashSet<string> ChildCandidates { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var social = request.Observation.Candidates.Where(candidate => candidate.Id.StartsWith("child_", StringComparison.Ordinal) &&
                candidate.Id.Contains(':')).Select(candidate => candidate.Id).ToArray();
            Assert.True(social.Select(candidate => candidate[(candidate.IndexOf(':') + 1)..]).Distinct().Count() <= 3);
            foreach (var candidate in social) ChildCandidates.Add(candidate);
            var chosen = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == Wanted)?.Id ?? "safe_idle";
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind,
                ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                chosen, 1, new Dictionary<string, double> { [chosen] = 1 }));
        }
    }
}
