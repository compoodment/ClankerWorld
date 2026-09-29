using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Fact]
    public async Task ConsentingParentsPrepareAcrossRestartAndFeedARealChildWithoutInfantProviderCalls()
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == first ? "parent_propose:" : "parent_accept:"));
        await world.AdvanceOneTickAsync();
        Assert.Equal("requested", world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        Assert.Empty(world.Society.Births);
        await world.AdvanceOneTickAsync();
        Assert.Equal("preparing", world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ParentProvider("safe_idle"));
        Assert.False((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Resume();
        for (var tick = 0; tick < 601; tick++) await restored.AdvanceOneTickAsync();
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes), _ => new ParentProvider("safe_idle"));
        replay.Resume();
        for (var tick = 0; tick < 601; tick++) await replay.AdvanceOneTickAsync();
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var birth = Assert.Single(restored.Society.Births);
        Assert.Equal(5, restored.Inhabitants.Count);
        Assert.Equal("Ari 1", restored.Society.GetInhabitant(birth.ChildId).Name);
        Assert.Equal(SocietyAgeBand.Infant, restored.Society.GetInhabitant(birth.ChildId).AgeBand);
        Assert.Equal("completed", restored.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        var completedState = restored.ExportState();
        var forgedPending = completedState with
        {
            Inhabitants = completedState.Inhabitants.Select(person => person.InhabitantId == first
            ? person with { Parenthood = person.Parenthood! with { Stage = "preparing", ChildId = null } } : person).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(forgedPending));
        Assert.Equal(2, restored.Society.Relationships.Count(item => item.Type == SocietyRelationshipType.Caregiver && item.TargetId == birth.ChildId));
        var familySnapshot = new OwnerWorldObservationStore(restored).GetSnapshot();
        Assert.Contains(familySnapshot.Inhabitants.Single(person => person.Id == first).Relationships,
            relationship => relationship.OtherPartyId == birth.ChildId &&
                relationship.Type == "biological_parentage" && relationship.Direction == "parent");
        Assert.Contains(familySnapshot.Inhabitants.Single(person => person.Id == birth.ChildId).Relationships,
            relationship => relationship.OtherPartyId == first &&
                relationship.Type == "biological_parentage" && relationship.Direction == "child");
        Assert.Contains(familySnapshot.Inhabitants.Single(person => person.Id == first).SocialNotes,
            note => note.Contains("Caring for a child", StringComparison.Ordinal));

        state = restored.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == birth.ChildId
            ? person with { HungerBasisPoints = 1_000 } : person).ToArray()
        };
        var childProvider = new ParentProvider("build:");
        using var caring = PrivateWorldRuntime.Restore(state, actor => actor == birth.ChildId ? childProvider : new ParentProvider("care:"));
        var foodBefore = caring.Society.Inventory.Lots.Where(lot => lot.ItemKind == "food").Sum(lot => lot.Quantity);
        for (var tick = 0; tick < 80 && caring.Inhabitants.Single(person => person.InhabitantId == birth.ChildId).HungerBasisPoints < 3_000; tick++)
            await caring.AdvanceOneTickAsync();
        Assert.True(caring.Inhabitants.Single(person => person.InhabitantId == birth.ChildId).HungerBasisPoints >= 3_000);
        Assert.True(caring.Society.Inventory.Lots.Where(lot => lot.ItemKind == "food").Sum(lot => lot.Quantity) < foodBefore);
        Assert.Equal(0, childProvider.Calls);
        Assert.Null(caring.Inhabitants.Single(person => person.InhabitantId == birth.ChildId).Project);
        Assert.Throws<ArgumentException>(() => caring.SubmitInstruction(new("infant-order", "owner", birth.ChildId,
            OwnerInstructionKind.MustDo, "build a shelter")));
        Assert.Contains(caring.ExportState().Events, item => item.Kind == "child_cared_for");
        Assert.Contains(caring.Society.Relationships, item => item.ProposerId == second && item.TargetId == birth.ChildId);
    }

    [Fact]
    public async Task CaregiverCanEaseAnInfantsIllnessWithoutASeparateRestAction()
    {
        var prepared = await PreparedState();
        var first = prepared.Inhabitants[0].InhabitantId;
        var second = prepared.Inhabitants[1].InhabitantId;
        using var preparing = PrivateWorldRuntime.Restore(prepared, actor =>
            new ParentProvider(actor == first ? "parent_propose:" : actor == second ? "parent_accept:" : "safe_idle"));
        for (var tick = 0; tick < 605 && preparing.Society.Births.Count == 0; tick++) await preparing.AdvanceOneTickAsync();
        var childId = Assert.Single(preparing.Society.Births).ChildId;
        var state = preparing.ExportState();
        var caregiverId = state.Society.Society.Relationships.First(edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.TargetId == childId && edge.State == SocietyRelationshipState.Accepted).ProposerId;
        var child = state.Inhabitants.Single(person => person.InhabitantId == childId);
        var caregiverTile = state.Map.FootNeighbors(child.Position).First(point => state.Map.IsPassable(point) &&
            Math.Abs(point.X - child.Position.X) + Math.Abs(point.Y - child.Position.Y) == 1 &&
            !state.Inhabitants.Any(person => person.InhabitantId != caregiverId && person.InhabitantId != childId && person.Position == point));
        const int initialIllness = 9_000;
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId switch
            {
                var id when id == caregiverId => person with
                {
                    Position = caregiverTile,
                    HungerBasisPoints = 9_000,
                    Survival = new SurvivalCondition(10_000, 0),
                    Project = null,
                    LastDecisionContext = null,
                    TravelCooldownTicks = 0,
                },
                var id when id == childId => person with
                {
                    HungerBasisPoints = 9_000,
                    Survival = new SurvivalCondition(9_000, initialIllness),
                    Project = null,
                },
                _ => person,
            }).ToArray(),
        };
        using var caring = PrivateWorldRuntime.Restore(state, actor =>
            new ParentProvider(actor == caregiverId ? "care:" : "safe_idle"));

        Assert.True((await caring.AdvanceOneTickAsync()).Advanced);

        var caredFor = caring.Inhabitants.Single(person => person.InhabitantId == childId);
        Assert.Contains(caring.ExportState().Events, item => item.Kind == "child_cared_for" && item.Detail == childId);
        Assert.Equal(10_000, caredFor.Survival!.WarmthBasisPoints);
        Assert.True(caredFor.Survival.IllnessBasisPoints < initialIllness - 12,
            $"Expected direct caregiver care to improve on ordinary warm-and-fed recovery; actual illness {caredFor.Survival.IllnessBasisPoints}.");
    }

    [Theory]
    [InlineData("parent_decline:")]
    [InlineData("safe_idle")]
    public async Task RefusalOrSilenceNeverCreatesAChild(string response)
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == first ? "parent_propose:" : response));
        for (var tick = 0; tick < 125; tick++) await world.AdvanceOneTickAsync();
        Assert.Empty(world.Society.Births);
        Assert.Equal("cancelled", world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        Assert.Equal(4, world.Inhabitants.Count);
    }

    [Fact]
    public async Task EndingPartnershipCancelsPreparation()
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == first ? "parent_propose:" : "parent_accept:"));
        await world.AdvanceOneTickAsync();
        await world.AdvanceOneTickAsync();
        state = world.ExportState();
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.RevokeRelationship(checkpoint, "parents", first));
        using var restored = PrivateWorldRuntime.Restore(state with { Society = society.ExportState() }, _ => new ParentProvider("safe_idle"));
        await restored.AdvanceOneTickAsync();
        Assert.Equal("cancelled", restored.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        Assert.Empty(restored.Society.Births);
    }

    [Fact]
    public async Task OldSchemaCannotHideAnActiveParenthoodPlan()
    {
        var state = await PreparedState();
        state = state with
        {
            SchemaVersion = 8,
            Inhabitants = state.Inhabitants.Select((person, index) => index == 0
            ? person with { Parenthood = new(state.Inhabitants[1].InhabitantId, "requested", 0, 0) } : person).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(state));
    }

    [Fact]
    public async Task EitherParentCanWithdrawDuringPreparation()
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == first ? "parent_propose:" : "parent_accept:"));
        await world.AdvanceOneTickAsync();
        await world.AdvanceOneTickAsync();
        using var restored = PrivateWorldRuntime.Restore(world.ExportState(), _ => new ParentProvider("parent_cancel:"));
        for (var tick = 0; tick < 40; tick++) await restored.AdvanceOneTickAsync();
        Assert.Equal("cancelled", restored.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        Assert.Empty(restored.Society.Births);
    }

    [Fact]
    public async Task ParenthoodIsNotOfferedWithoutFoodAndHousing()
    {
        var state = await PreparedState();
        state = state with { WorldSimulation = state.WorldSimulation! with { Buildings = [] } };
        using var world = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("parent_propose:"));
        for (var tick = 0; tick < 4; tick++) await world.AdvanceOneTickAsync();
        Assert.All(world.Inhabitants, person => Assert.Null(person.Parenthood));
        Assert.Empty(world.Society.Births);
    }

    [Fact]
    public async Task ParentDeathCancelsPreparationAndCannotCreateAnOrphanedBirth()
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == first ? "parent_propose:" : "parent_accept:"));
        await world.AdvanceOneTickAsync();
        await world.AdvanceOneTickAsync();
        state = world.ExportState();
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, second, SocietyDeathCause.Accident, checkpoint.WorldTick));
        state = state with { Society = society.ExportState(), Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != second).ToArray() };
        using var restored = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("safe_idle"));
        await restored.AdvanceOneTickAsync();
        Assert.Equal("cancelled", restored.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        Assert.Empty(restored.Society.Births);
    }

    [Fact]
    public async Task ParenthoodTelemetryReportsStagesWithoutPrivateNames()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-parenthood-log-");
        try
        {
            var state = await PreparedState();
            var first = state.Inhabitants[0].InhabitantId;
            state = state with
            {
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Inhabitants = state.Society.Society.Inhabitants.Select(person => person with { Name = "private-parenthood-secret" }).ToArray(),
                    }
                }
            };
            using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == first ? "parent_propose:" : "safe_idle"));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromSeconds(30));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world, new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Contains(logger.Messages, message => message.Contains("settlement_family", StringComparison.Ordinal) &&
                message.Contains("event=parenthood_requested", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("private-parenthood-secret", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<PrivateWorldRuntimeState> PreparedState()
    {
        using var world = new PrivateWorldRuntime("settlement-parenthood", _ => new ParentProvider("safe_idle"));
        world.StageStarterContent();
        for (var tick = 0; tick < 5; tick++) await world.AdvanceOneTickAsync();
        var shelter = world.WorldContent.Buildings.First(building => building.Tags.Contains("shelter", StringComparer.Ordinal));
        var placed = false;
        var map = world.ExportState().Map;
        foreach (var tile in map.Tiles.Where(tile => map.IsPassable(tile.Position)))
        {
            if (world.PlaceBuilding("family-shelter", shelter.CanonicalId, tile.Position).Applied)
            {
                placed = true;
                break;
            }
        }
        Assert.True(placed);
        var state = world.ExportState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
            new("parents", 1, SocietyRelationshipType.Partnership, first, second, checkpoint.WorldTick)));
        society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint, "parents", 1, second));
        return state with
        {
            Society = society.ExportState(),
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray(),
                },
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Clear },
            },
        };
    }

    private sealed class ParentProvider(string prefix) : IDecisionProvider
    {
        public int Calls { get; private set; }
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            var candidate = request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [candidate] },
            }, cancellationToken);
        }
    }
}
