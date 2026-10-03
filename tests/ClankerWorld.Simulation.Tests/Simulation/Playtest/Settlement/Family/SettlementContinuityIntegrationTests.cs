using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Control;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Fact]
    public async Task ContinuityDeadlineWithoutARequestCreatesAValidPlanAndReplaysToBirth()
    {
        var (state, first, second) = ContinuityIntegrationCouple("continuity-unrequested-birth", separateHouseholds: false);
        using var world = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var couple = Assert.Single(world.ExportState().Continuity!.Couples);
        Assert.Equal(world.WorldTick + 2L * state.WorldSystems!.Config.TicksPerDay, couple.DeadlineTick);
        Assert.All(world.Inhabitants, person => Assert.Null(person.Parenthood));

        await AdvanceContinuityIntegrationTo(world, couple.DeadlineTick - 1);
        Assert.All(world.Inhabitants, person => Assert.Null(person.Parenthood));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var owner = couple.FirstPartnerId;
        var partner = owner == first ? second : first;
        var home = world.Society.GetInhabitant(owner).HouseholdId!;
        var plan = world.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood!;
        Assert.Equal("preparing", plan.Stage);
        Assert.Equal(partner, plan.PartnerId);
        Assert.Equal(owner, plan.PrimaryCaregiverId);
        Assert.Equal(home, plan.IntendedHouseholdId);
        Assert.Equal(couple.DeadlineTick, plan.LastTransitionTick);
        Assert.Empty(world.Society.Births);
        world.Validate();

        world.Pause();
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new ParentProvider("safe_idle"));
        Assert.False((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Resume();
        await AdvanceContinuityIntegrationTo(restored, couple.DeadlineTick + 601);

        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new ParentProvider("safe_idle"));
        replay.Resume();
        await AdvanceContinuityIntegrationTo(replay, restored.WorldTick);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(restored.ExportState()),
            PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        var birth = Assert.Single(restored.Society.Births);
        Assert.Equal(owner, birth.PrimaryCaregiverId);
        Assert.Equal(home, birth.HouseholdId);
        Assert.Equal(SocietyAgeBand.Infant, restored.Society.GetInhabitant(birth.ChildId).AgeBand);
        var completed = restored.Inhabitants.Single(person => person.InhabitantId == owner).Parenthood!;
        Assert.Equal("completed", completed.Stage);
        Assert.Equal(birth.ChildId, completed.ChildId);
        Assert.Equal(home, completed.BirthHouseholdId);
        using var reloadedBirth = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(
            PrivateWorldRuntimeCodec.Encode(restored.ExportState())));
        Assert.Equal(birth, Assert.Single(reloadedBirth.Society.Births));
    }

    [Fact]
    public async Task ContinuityResumesPostponedAcceptanceWithItsChosenCaregiverHomeAndInitiatingModel()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-continuity-caregiver-");
        try
        {
            var (state, initiator, acceptor) = ContinuityIntegrationCouple("continuity-postponed-caregiver",
                separateHouseholds: true);
            var initiatorHome = state.Society.Society.GetInhabitant(initiator).HouseholdId!;
            var acceptorHome = state.Society.Society.GetInhabitant(acceptor).HouseholdId!;
            Assert.NotEqual(initiatorHome, acceptorHome);
            Assert.True(string.CompareOrdinal(acceptor, initiator) < 0);
            var initiate = new ParentProvider("parent_propose:" + acceptor);
            var accept = new ParentProvider($"parent_accept:{initiator}:acceptor:");
            var putOff = new ParentProvider("parent_postpone:" + initiator);
            var postponing = false;
            IDecisionProvider ProviderFor(string id) => id == initiator ? initiate
                : id == acceptor ? postponing ? putOff : accept
                : new ParentProvider("safe_idle");
            using var world = PrivateWorldRuntime.Restore(state, ProviderFor);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.Equal("requested", world.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!.Stage);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var agreed = world.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!;
            Assert.Equal("preparing", agreed.Stage);
            Assert.Equal(acceptor, agreed.PrimaryCaregiverId);
            Assert.Equal(acceptorHome, agreed.IntendedHouseholdId);
            Assert.Contains(accept.SeenCandidates, candidate =>
                candidate.Id == $"parent_accept:{initiator}:acceptor:{Uri.EscapeDataString(acceptorHome)}");
            var deadline = Assert.Single(world.ExportState().Continuity!.Couples).DeadlineTick;

            postponing = true;
            for (var tick = 0; tick < 40 &&
                 world.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!.Stage != "postponed"; tick++)
                Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            var postponed = world.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!;
            Assert.Equal("postponed", postponed.Stage);
            Assert.Equal(acceptor, postponed.PrimaryCaregiverId);
            Assert.Equal(acceptorHome, postponed.IntendedHouseholdId);
            Assert.Contains(putOff.SeenCandidates, candidate => candidate.Id == "parent_postpone:" + initiator);
            Assert.Contains("Parenthood put off for now.", new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants
                .Single(person => person.Id == initiator).SocialNotes);
            world.Pause();
            var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
            using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
                _ => new ParentProvider("safe_idle"));
            Assert.Equal(postponed, restored.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood);
            restored.Resume();
            await AdvanceContinuityIntegrationTo(restored, deadline - 1);
            Assert.Equal("postponed", restored.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!.Stage);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);

            var resumed = restored.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!;
            Assert.Equal("preparing", resumed.Stage);
            Assert.Equal(acceptor, resumed.PrimaryCaregiverId);
            Assert.Equal(acceptorHome, resumed.IntendedHouseholdId);
            Assert.Equal(agreed.RequestedTick, resumed.RequestedTick);
            Assert.Equal(deadline, resumed.LastTransitionTick);
            Assert.Null(restored.Inhabitants.Single(person => person.InhabitantId == acceptor).Parenthood);
            restored.Validate();
            await AdvanceContinuityIntegrationTo(restored, deadline + 599);
            Assert.Empty(restored.Society.Births);

            var caregiverFood = ContinuityHouseholdFood(restored, acceptorHome);
            var initiatorFood = ContinuityHouseholdFood(restored, initiatorHome);
            var providers = new ProviderConfigurationStore(Path.Combine(directory.FullName, "providers.json"),
                new ProviderConfigurationSeed("deterministic", null, null, null, null, null, null));
            _ = providers.Configure(new("personal", "openai", "continuity-initiating-parent-model",
                "continuity-initiator-test-key", false, initiator));
            _ = providers.Configure(new("personal", "ollama-cloud", "continuity-caregiver-model",
                "continuity-caregiver-test-key", false, acceptor));
            var stateFile = new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json"),
                _ => new ParentProvider("safe_idle"));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromHours(1));
            presence.RecordAuthenticatedReconnect("owner");
            using var service = new PrivateWorldRuntimeService(restored, stateFile, presence, providers: providers);
            Assert.True(await service.TryAdvanceOnceAsync());

            var birth = Assert.Single(restored.Society.Births);
            Assert.Equal($"family:{initiator}:{agreed.RequestedTick}", birth.RequestId);
            Assert.Equal(acceptor, birth.PrimaryCaregiverId);
            Assert.Equal(acceptorHome, birth.HouseholdId);
            Assert.Equal(acceptorHome, restored.Society.GetInhabitant(birth.ChildId).HouseholdId);
            Assert.Equal(caregiverFood - 4, ContinuityHouseholdFood(restored, acceptorHome));
            Assert.Equal(initiatorFood, ContinuityHouseholdFood(restored, initiatorHome));
            var completed = restored.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!;
            Assert.Equal("completed", completed.Stage);
            Assert.Equal(acceptor, completed.PrimaryCaregiverId);
            Assert.Equal(acceptorHome, completed.IntendedHouseholdId);
            Assert.Equal(acceptorHome, completed.BirthHouseholdId);
            var child = restored.Inhabitants.Single(person => person.InhabitantId == birth.ChildId);
            Assert.Equal("openai", child.ChildModelSelection!.Provider);
            Assert.Equal("continuity-initiating-parent-model", child.ChildModelSelection.ModelId);
            Assert.Equal(PrivateWorldRuntime.ChildModelChoiceInitiatingParent, child.ChildModelSelection.ChoiceReason);
            using var reloadedBirth = stateFile.LoadOrCreate(state.WorldSeed);
            Assert.Equal(birth, Assert.Single(reloadedBirth.Society.Births));
            Assert.Equal(child.ChildModelSelection,
                reloadedBirth.Inhabitants.Single(person => person.InhabitantId == child.InhabitantId).ChildModelSelection);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-caregiver")]
    [InlineData("non-parent-caregiver")]
    [InlineData("missing-home")]
    [InlineData("unknown-home")]
    public async Task ContinuityPostponedCheckpointRequiresTheChosenParentAndExistingHome(string damage)
    {
        var (state, initiator, acceptor) = ContinuityIntegrationCouple("continuity-postponed-checkpoint",
            separateHouseholds: true);
        var postponing = false;
        using var world = PrivateWorldRuntime.Restore(state, id => new ParentProvider(id == initiator
            ? "parent_propose:" + acceptor
            : id == acceptor ? postponing ? "parent_postpone:" + initiator : $"parent_accept:{initiator}:acceptor:"
            : "safe_idle"));
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("preparing", world.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!.Stage);
        postponing = true;
        for (var tick = 0; tick < 40 &&
             world.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!.Stage != "postponed"; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("postponed", world.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!.Stage);
        world.Pause();
        state = world.ExportState();
        var healthyBytes = PrivateWorldRuntimeCodec.Encode(state);
        var plan = state.Inhabitants.Single(person => person.InhabitantId == initiator).Parenthood!;
        var other = state.Inhabitants.First(person => person.InhabitantId != initiator && person.InhabitantId != acceptor).InhabitantId;
        var damagedPlan = damage switch
        {
            "missing-caregiver" => plan with { PrimaryCaregiverId = null },
            "non-parent-caregiver" => plan with { PrimaryCaregiverId = other },
            "missing-home" => plan with { IntendedHouseholdId = null },
            "unknown-home" => plan with { IntendedHouseholdId = "household:missing-continuity-home" },
            _ => throw new ArgumentOutOfRangeException(nameof(damage)),
        };
        var damagedState = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == initiator
                ? person with { Parenthood = damagedPlan } : person).ToArray(),
        };
        var document = JsonNode.Parse(healthyBytes)!;
        var savedPlan = document["state"]!["inhabitants"]!.AsArray()
            .Single(person => person!["inhabitantId"]!.GetValue<string>() == initiator)!["parenthood"]!;
        savedPlan["primaryCaregiverId"] = damagedPlan.PrimaryCaregiverId;
        savedPlan["intendedHouseholdId"] = damagedPlan.IntendedHouseholdId;
        var restoreFailure = Record.Exception(() =>
        {
            using var rejected = PrivateWorldRuntime.Restore(damagedState);
        });
        var decodeFailure = Record.Exception(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
        Assert.IsType<InvalidDataException>(restoreFailure);
        Assert.IsType<InvalidDataException>(decodeFailure);
        using var healthy = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(healthyBytes));
        Assert.Equal(healthyBytes, PrivateWorldRuntimeCodec.Encode(healthy.ExportState()));
    }

    [Fact]
    public void ContinuityPostponedCheckpointCannotResumeAnAlreadyCompletedBirth()
    {
        var (state, initiator, partner) = ContinuityIntegrationCouple("continuity-completed-birth-checkpoint",
            separateHouseholds: true);
        var society = state.Society.Society;
        var home = society.GetInhabitant(partner).HouseholdId!;
        var food = society.Inventory.Lots.First(lot => lot.OwnerId == home && lot.ItemKind == "food" && lot.Quantity >= 4);
        var committed = SocietyFixture.CommitBirth(society, new SocietyBirthRequest(
            $"family:{initiator}:{society.WorldTick}", 1, initiator, partner, home, [partner], [initiator, partner],
            food.Id, 4, society.WorldTick, PrimaryCaregiverId: partner));
        var childId = Assert.IsType<string>(committed.CreatedId);
        var birth = Assert.Single(committed.Checkpoint.Births);
        var childPosition = state.Map.Tiles.Select(tile => tile.Position).First(point => state.Map.IsBuildable(point) &&
            !state.Inhabitants.Any(person => person.Position == point) &&
            !state.Map.Resources.Any(resource => resource.Position == point) &&
            !state.Map.CampObjects.Any(camp => camp.Position == point));
        var completed = new SettlementParenthood(partner, "completed", society.WorldTick, society.WorldTick,
            ChildId: childId, PrimaryCaregiverId: partner, IntendedHouseholdId: home, BirthHouseholdId: home);
        state = state with
        {
            Society = state.Society with { Society = committed.Checkpoint },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == initiator
                ? person with { Parenthood = completed } : person)
                .Append(new PlaytestInhabitantState(childId, childPosition, 8_000, 0, "curious", "grow with the household"))
                .ToArray(),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(partner, StringComparer.Ordinal)
                ? town with { ResidentIds = town.ResidentIds.Append(childId).Order(StringComparer.Ordinal).ToArray() }
                : town).ToArray(),
        };
        using var canonical = PrivateWorldRuntime.Restore(state);
        state = canonical.ExportState();
        var healthyBytes = PrivateWorldRuntimeCodec.Encode(state);
        using var healthy = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(healthyBytes));
        Assert.Equal(birth, Assert.Single(healthy.Society.Births));
        Assert.Equal(healthyBytes, PrivateWorldRuntimeCodec.Encode(healthy.ExportState()));

        var postponed = completed with { Stage = "postponed", ChildId = null, BirthHouseholdId = null };
        var damagedState = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == initiator
                ? person with { Parenthood = postponed } : person).ToArray(),
        };
        var document = JsonNode.Parse(healthyBytes)!;
        var savedPlan = document["state"]!["inhabitants"]!.AsArray()
            .Single(person => person!["inhabitantId"]!.GetValue<string>() == initiator)!["parenthood"]!;
        savedPlan["stage"] = "postponed";
        savedPlan["childId"] = null;
        savedPlan["birthHouseholdId"] = null;
        var restoreFailure = Record.Exception(() =>
        {
            using var rejected = PrivateWorldRuntime.Restore(damagedState);
        });
        var decodeFailure = Record.Exception(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
        Assert.True(restoreFailure?.GetType() == typeof(InvalidDataException) && decodeFailure?.GetType() == typeof(InvalidDataException),
            $"Expected both completed-birth tamper paths to be refused; Restore: {restoreFailure?.GetType().Name ?? "accepted"}; Decode: {decodeFailure?.GetType().Name ?? "accepted"}.");
    }

    private static (PrivateWorldRuntimeState State, string Initiator, string Partner) ContinuityIntegrationCouple(
        string seed, bool separateHouseholds)
    {
        using var initial = NormalPathWorld.CreateGenerated(seed, _ => new ParentProvider("safe_idle"));
        var state = initial.ExportState();
        var alpha = state.Society.Society.GetHousehold("household:camp-alpha");
        var beta = state.Society.Society.GetHousehold("household:camp-beta");
        // A cross-household initiator comes later by ID, so resumption must preserve the
        // initiating parent instead of substituting the automatic ordinal tie-break.
        var initiator = separateHouseholds ? beta.MemberIds.Order(StringComparer.Ordinal).First()
            : alpha.MemberIds.Order(StringComparer.Ordinal).First();
        var partner = separateHouseholds ? alpha.MemberIds.Order(StringComparer.Ordinal).First()
            : alpha.MemberIds.Order(StringComparer.Ordinal).Last();
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
            new("continuity-integration-parents", 1, SocietyRelationshipType.Partnership,
                initiator, partner, checkpoint.WorldTick)));
        society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint,
            "continuity-integration-parents", 1, partner));
        state = state with
        {
            Society = society.ExportState(),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                LastDecisionContext = null,
                Skills = person.InhabitantId == initiator || person.InhabitantId == partner
                    ? [new(SettlementSkillKind.Building, state.Society.Society.WorldTick)] : person.Skills,
            }).ToArray(),
            WorldSystems = state.WorldSystems! with
            {
                RegionalWeather = null,
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>()
                        .Select(season => new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray(),
                },
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Clear },
            },
        };
        return (state, initiator, partner);
    }

    private static async Task AdvanceContinuityIntegrationTo(PrivateWorldRuntime world, long targetTick)
    {
        while (world.WorldTick < targetTick)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(targetTick, world.WorldTick);
    }

    private static int ContinuityHouseholdFood(PrivateWorldRuntime world, string householdId) =>
        world.Society.Inventory.Lots.Where(lot => lot.OwnerId == householdId && lot.ItemKind == "food")
            .Sum(lot => lot.Quantity);
}
