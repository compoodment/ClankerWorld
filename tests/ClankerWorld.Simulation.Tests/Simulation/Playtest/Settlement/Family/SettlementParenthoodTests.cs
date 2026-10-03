using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
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
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == first || person.InhabitantId == second
                ? person with { Skills = [new(SettlementSkillKind.Building, state.Society.Society.WorldTick)] }
                : person).ToArray(),
        };
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
        Assert.Empty(restored.Inhabitants.Single(person => person.InhabitantId == birth.ChildId).Skills ?? []);
        var newborn = restored.Inhabitants.Single(person => person.InhabitantId == birth.ChildId);
        Assert.NotNull(newborn.Housing?.Blocker);
        Assert.Contains(newborn.Housing!.Blocker,
            new[] { HousingBlockers.NoAuthorizedHome, HousingBlockers.MissingMaterials, HousingBlockers.NoLegalSite });
        Assert.Contains(restored.ExportState().Events, item => item.Kind == "housing_blocked" &&
            item.Detail == $"{birth.ChildId}:{newborn.Housing.Blocker}");
        var newbornProfile = new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants
            .Single(person => person.Id == birth.ChildId);
        Assert.Contains(newbornProfile.DecisionFactors, factor => factor.Key == "housing" &&
            factor.Detail.Contains("No home", StringComparison.Ordinal));
        using (var reloaded = PrivateWorldRuntime.Restore(
                   PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(restored.ExportState()))))
        {
            Assert.Equal(newborn.Housing.Blocker,
                reloaded.Inhabitants.Single(person => person.InhabitantId == birth.ChildId).Housing?.Blocker);
            var reloadedState = reloaded.ExportState();
            var householdId = reloaded.Society.GetInhabitant(birth.ChildId).HouseholdId!;
            var members = reloaded.Society.GetHousehold(householdId).MemberIds
                .Where(member => member != birth.ChildId).ToArray();
            var forgedRequest = reloadedState with
            {
                Inhabitants = reloadedState.Inhabitants.Select(person => person.InhabitantId == birth.ChildId
                    ? person with
                    {
                        Housing = new SettlementHousing(Request: new SettlementHousingRequest(
                        householdId, reloaded.Society.WorldTick,
                        reloaded.Society.WorldTick + PrivateWorldRuntime.HousingRequestTicks, members, [], []))
                    }
                    : person).ToArray(),
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(forgedRequest));
            var forgedRefusal = reloadedState with
            {
                Inhabitants = reloadedState.Inhabitants.Select(person => person.InhabitantId == birth.ChildId
                    ? person with
                    {
                        Housing = (person.Housing ?? new()) with
                        {
                            Refusals = [new SettlementHousingRefusal(householdId, reloaded.Society.WorldTick)],
                        }
                    }
                    : person).ToArray(),
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(forgedRefusal));
        }
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

        var childId = birth.ChildId;
        state = completedState;
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
        using var tending = PrivateWorldRuntime.Restore(state, actor =>
            new ParentProvider(actor == caregiverId ? "care:" : "safe_idle"));

        var tendingStep = await tending.AdvanceOneTickAsync();
        Assert.True(tendingStep.Advanced);

        var caredFor = tending.Inhabitants.Single(person => person.InhabitantId == childId);
        Assert.Contains(tendingStep.Events, item => item.Kind == "child_cared_for" && item.Detail == childId);
        Assert.Equal(10_000, caredFor.Survival!.WarmthBasisPoints);
        Assert.True(caredFor.Survival.IllnessBasisPoints < initialIllness - 12,
            $"Expected direct caregiver care to improve on ordinary warm-and-fed recovery; actual illness {caredFor.Survival.IllnessBasisPoints}.");
    }

    [Fact]
    public async Task BirthUsesTheAgreedCaregiversActualHouseholdIfItChangesBeforeBirth()
    {
        using var seed = NormalPathWorld.CreateGenerated("parenthood-stale-intended-home", _ => new ParentProvider("safe_idle"));
        var state = seed.ExportState();
        var oldHome = state.Society.Society.Households.Single(item => item.Id == "household:camp-alpha");
        var newHome = state.Society.Society.Households.Single(item => item.Id == "household:camp-beta");
        var caregiver = oldHome.MemberIds[0];
        var otherParent = oldHome.MemberIds[1];
        var agreedHome = oldHome.Id;
        using (var society = SocietyWorldRuntime.Restore(state.Society))
        {
            society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
                new("stale-home-parents", 1, SocietyRelationshipType.Partnership, caregiver, otherParent, checkpoint.WorldTick)));
            society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint, "stale-home-parents", 1, otherParent));
            state = state with
            {
                Society = society.ExportState(),
                Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == caregiver || person.InhabitantId == otherParent
                    ? person with { Skills = [new(SettlementSkillKind.Building, state.Society.Society.WorldTick)] }
                    : person).ToArray(),
                WorldSystems = state.WorldSystems! with
                {
                    RegionalWeather = null,
                    Config = state.WorldSystems.Config with
                    {
                        WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray(),
                    },
                    Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Clear },
                },
            };
        }
        IDecisionProvider ProviderFor(string actor) => actor == caregiver
            ? new ParentProvider("parent_propose:")
            : actor == otherParent
                ? new ParentProvider($"parent_accept:{caregiver}:initiator")
                : new ParentProvider("safe_idle");

        using var preparing = PrivateWorldRuntime.Restore(state, ProviderFor);
        Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);
        Assert.True((await preparing.AdvanceOneTickAsync()).Advanced);
        Assert.Equal("preparing", preparing.Inhabitants.Single(item => item.InhabitantId == caregiver).Parenthood!.Stage);
        var plan = preparing.Inhabitants.Single(item => item.InhabitantId == caregiver).Parenthood!;
        Assert.Equal(caregiver, plan.PrimaryCaregiverId);
        Assert.Equal(agreedHome, plan.IntendedHouseholdId);

        preparing.Pause();
        var movedState = preparing.ExportState();
        var movedSociety = movedState.Society.Society;
        var membership = movedSociety.Relationships.Single(item => item.Type == SocietyRelationshipType.HouseholdMembership &&
            item.TargetId == caregiver && item.State == SocietyRelationshipState.Accepted);
        movedSociety = SocietyFixture.RevokeRelationship(movedSociety, membership.Id, caregiver).Checkpoint;
        movedSociety = SocietyFixture.JoinHousehold(movedSociety, caregiver, newHome.Id).Checkpoint;
        var newHouse = movedState.WorldSimulation!.Buildings.Single(item => item.HouseholdId == newHome.Id &&
            movedState.WorldContent!.Buildings.Single(definition => definition.CanonicalId == item.DefinitionId)
                .Tags.Contains("house", StringComparer.Ordinal));
        movedState = movedState with
        {
            Society = movedState.Society with
            {
                Society = movedSociety,
            },
        };

        using var moved = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(movedState)),
            ProviderFor);
        Assert.Equal(newHome.Id, moved.Society.GetInhabitant(caregiver).HouseholdId);
        Assert.Equal(agreedHome, moved.Society.GetInhabitant(otherParent).HouseholdId);
        var fullHome = new OwnerWorldObservationStore(moved).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == newHouse.InstanceId);
        Assert.Equal(3, fullHome.PermanentResidentCount);
        Assert.Equal(3, fullHome.ResidentLimit);
        Assert.False(fullHome.IsOvercrowded);
        moved.Resume();
        for (var tick = 0; tick < 620 && moved.Society.Births.Count == 0; tick++)
            Assert.True((await moved.AdvanceOneTickAsync()).Advanced);

        var birth = Assert.Single(moved.Society.Births);
        var child = moved.Society.GetInhabitant(birth.ChildId);
        var completed = moved.Inhabitants.Single(item => item.InhabitantId == caregiver).Parenthood!;
        Assert.Equal(caregiver, child.PrimaryCaregiverId);
        Assert.Equal(newHome.Id, child.HouseholdId);
        Assert.Equal(newHome.Id, birth.HouseholdId);
        Assert.Equal(agreedHome, completed.IntendedHouseholdId);
        Assert.Equal(newHome.Id, completed.BirthHouseholdId);
        Assert.Equal(agreedHome, moved.Society.GetInhabitant(otherParent).HouseholdId);
        var capacity = new OwnerWorldObservationStore(moved).GetSnapshot().PlacedBuildings
            .Single(item => item.InstanceId == newHouse.InstanceId);
        Assert.Equal(4, capacity.PermanentResidentCount);
        Assert.Equal(3, capacity.ResidentLimit);
        Assert.True(capacity.IsOvercrowded);
        var newborn = moved.Inhabitants.Single(item => item.InhabitantId == birth.ChildId);
        Assert.Equal(HousingBlockers.Overcrowded, newborn.Housing?.Blocker);
        Assert.Contains(new OwnerWorldObservationStore(moved).GetSnapshot().Inhabitants
            .Single(item => item.Id == birth.ChildId).DecisionFactors,
            factor => factor.Key == "housing" && factor.Detail.Contains("Housing need", StringComparison.Ordinal));
        Assert.Equal(4, capacity.PermanentResidentCount);
        using var restored = PrivateWorldRuntime.Restore(
            PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(moved.ExportState())), ProviderFor);
        Assert.Equal(newHome.Id, Assert.Single(restored.Society.Births).HouseholdId);
        Assert.Equal(newHome.Id, restored.Inhabitants.Single(item => item.InhabitantId == caregiver).Parenthood!.BirthHouseholdId);
        Assert.Equal(HousingBlockers.Overcrowded,
            restored.Inhabitants.Single(item => item.InhabitantId == birth.ChildId).Housing?.Blocker);
    }

    [Fact]
    public async Task ExistingSecondaryCaregiverCanChoosePrimaryCareAfterTheRecordedPrimaryDies()
    {
        using var initial = NormalPathWorld.CreateGenerated("secondary-caregiver-primary-choice", _ => new ParentProvider("safe_idle"));
        var state = initial.ExportState();
        var house = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        var householdId = house.HouseholdId!;
        var parents = state.Society.Society.GetHousehold(householdId).MemberIds
            .Where(id => state.Society.Society.GetInhabitant(id).Status == SocietyInhabitantStatus.Active)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(2, parents.Length);
        var primary = parents[0];
        var secondary = parents[1];
        var otherHousehold = state.Society.Society.Households.First(item => item.Id != householdId);
        var newPartner = otherHousehold.MemberIds[0];

        var society = state.Society.Society;
        society = SocietyFixture.ProposeRelationship(society,
            new("primary-care-parents", 1, SocietyRelationshipType.Partnership, primary, secondary, society.WorldTick)).Checkpoint;
        society = SocietyFixture.AcceptRelationship(society, "primary-care-parents", 1, secondary).Checkpoint;
        var food = society.Inventory.Lots.First(item => item.OwnerId == householdId && item.ItemKind == "food" && item.Quantity >= 4);
        var birth = SocietyFixture.CommitBirth(society, new SocietyBirthRequest(
            "secondary-caregiver-child", 1, primary, secondary, householdId, [primary, secondary], [primary, secondary],
            food.Id, 4, society.WorldTick, ChildName: "Ari", PrimaryCaregiverId: primary));
        var childId = Assert.IsType<string>(birth.CreatedId);
        society = birth.Checkpoint;
        Assert.Equal(primary, society.GetInhabitant(childId).PrimaryCaregiverId);
        Assert.Single(society.Relationships, item => item.Type == SocietyRelationshipType.Caregiver &&
            item.ProposerId == secondary && item.TargetId == childId && item.State == SocietyRelationshipState.Accepted);

        var childPosition = state.Map.Tiles.Select(item => item.Position).First(tile => state.Map.IsBuildable(tile) &&
            !state.Inhabitants.Any(person => person.Position == tile) &&
            !state.Map.Resources.Any(resource => resource.Position == tile) &&
            !state.Map.CampObjects.Any(camp => camp.Position == tile));
        state = state with
        {
            Society = state.Society with { Society = society },
            Survival = new SettlementSurvivalState(society.WorldTick, []),
            Inhabitants = state.Inhabitants.Append(new PlaytestInhabitantState(childId, childPosition,
                9_000, 0, "curious", "grow with the household", Survival: new SurvivalCondition(WarmthBasisPoints: 10_000)))
                .Select(person => person with
                {
                    Survival = new SurvivalCondition(WarmthBasisPoints: 10_000),
                    LastDecisionContext = null,
                }).ToArray(),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(primary, StringComparer.Ordinal)
                ? town with { ResidentIds = town.ResidentIds.Append(childId).Order(StringComparer.Ordinal).ToArray() }
                : town).ToArray(),
        };
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);

        society = SocietyFixture.Kill(state.Society.Society, primary, SocietyDeathCause.NaturalAge).Checkpoint;
        society = SocietyFixture.ProposeRelationship(society,
            new("replacement-primary-partnership", 1, SocietyRelationshipType.Partnership,
                secondary, newPartner, society.WorldTick)).Checkpoint;
        society = SocietyFixture.AcceptRelationship(society, "replacement-primary-partnership", 1, newPartner).Checkpoint;
        var deceasedPhysical = state.Inhabitants.Single(person => person.InhabitantId == primary);
        var deceased = new PlaytestDeceasedInhabitantState(primary, society.GetInhabitant(primary).DeathTick!.Value,
            society.AgeAt(society.GetInhabitant(primary), society.GetInhabitant(primary).DeathTick!.Value), deceasedPhysical);
        state = state with
        {
            Society = state.Society with { Society = society },
            Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != primary).ToArray(),
            DeceasedInhabitants = [.. state.DeceasedInhabitants ?? [], deceased],
            Towns = state.Towns!.Select(town =>
            {
                var residents = town.ResidentIds.Where(id => id != primary).ToArray();
                var adults = residents.Where(id => society.GetInhabitant(id).Status == SocietyInhabitantStatus.Active &&
                    society.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder);
                return town with
                {
                    ResidentIds = residents,
                    Governance = TownGovernanceRules.Advance(town.Governance!, town.Id, state.WorldSeed, adults,
                        society.WorldTick, state.WorldSystems!.Config.TicksPerDay),
                };
            }).ToArray(),
        };
        Assert.Null(society.GetInhabitant(childId).PrimaryCaregiverId);
        var replacementUnit = society.GetInhabitant(secondary).DomesticFamilyUnitId;
        Assert.NotEqual(replacementUnit, society.GetInhabitant(childId).DomesticFamilyUnitId);

        var caregiverProvider = new ParentProvider("guardian_accept:");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            actor => actor == secondary ? caregiverProvider : new ParentProvider("safe_idle"));
        for (var tick = 0; tick < 40 && world.Society.GetInhabitant(childId).PrimaryCaregiverId != secondary; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        Assert.Contains(caregiverProvider.SeenCandidates,
            candidate => candidate.Id == "guardian_accept:" + childId);
        Assert.Equal(secondary, world.Society.GetInhabitant(childId).PrimaryCaregiverId);
        Assert.Equal(replacementUnit, world.Society.GetInhabitant(childId).DomesticFamilyUnitId);
        Assert.Equal(primary, Assert.Single(world.Society.Births).PrimaryCaregiverId);
        Assert.Single(world.Society.Relationships, item => item.Type == SocietyRelationshipType.Caregiver &&
            item.ProposerId == secondary && item.TargetId == childId && item.State == SocietyRelationshipState.Accepted);
    }

    [Theory]
    [InlineData("parent_decline:")]
    [InlineData("safe_idle")]
    public async Task RefusalOrSilenceNeverCreatesAChild(string response)
    {
        // Ordinary refusal applies once eight non-elders live and the continuity rule is off.
        var state = WithEightNonElders(await PreparedState());
        var first = state.Inhabitants[0].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == first ? "parent_propose:" : response));
        for (var tick = 0; tick < 125; tick++) await world.AdvanceOneTickAsync();
        Assert.Empty(world.Society.Births);
        Assert.Equal("cancelled", world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        Assert.Equal(8, world.Inhabitants.Count);
        Assert.False(world.ExportState().Continuity!.Active);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirstCousinsButNotAuntsOrUnclesCanPlanAChild(bool cousins)
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        var third = state.Inhabitants[2].InhabitantId;
        var fourth = state.Inhabitants[3].InhabitantId;
        if (cousins)
        {
            (state, var grandparent) = FamilyTreeFixture.WithDeadAncestor(state, "grandparent");
            state = FamilyTreeFixture.WithRelationships(state, SocietyRelationshipType.BiologicalParentage,
                (grandparent, third), (grandparent, fourth), (third, first), (fourth, second));
        }
        else
        {
            state = FamilyTreeFixture.WithRelationships(state, SocietyRelationshipType.BiologicalParentage,
                (fourth, first), (fourth, third), (third, second));
        }
        using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == first ? "parent_propose:" : "parent_accept:"));
        await world.AdvanceOneTickAsync();
        if (cousins)
        {
            Assert.Equal("requested", world.Inhabitants.Single(person => person.InhabitantId == first).Parenthood!.Stage);
        }
        else
        {
            Assert.All(world.Inhabitants, person => Assert.Null(person.Parenthood));
        }
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
    public async Task EitherParentCanWithdrawDuringPreparation()
    {
        // Withdrawal is ordinary refusal, so it needs the continuity rule to be off.
        var state = WithEightNonElders(await PreparedState());
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
                RegionalWeather = null,
                Config = state.WorldSystems.Config with
                {
                    WeatherProfiles = Enum.GetValues<SeasonKind>().Select(season => new WeatherProfile(season, 1, 0, 0, 0, 0)).ToArray(),
                },
                Climate = state.WorldSystems.Climate with { Weather = WeatherKind.Clear },
            },
        };
    }

    private sealed class ParentProvider(string prefix, Action<CognitionDecisionRequest>? inspect = null) : IDecisionProvider
    {
        public int Calls { get; private set; }
        public List<CognitionCandidate> SeenCandidates { get; } = [];
        public List<string> SelectedCandidateIds { get; } = [];
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            inspect?.Invoke(request);
            SeenCandidates.AddRange(request.Observation.Candidates);
            var candidate = request.Observation.Candidates.FirstOrDefault(item => item.Id.StartsWith(prefix, StringComparison.Ordinal))
                ?? request.Observation.Candidates.Single(item => item.Id == "safe_idle");
            SelectedCandidateIds.Add(candidate.Id);
            return new DeterministicDecisionProvider().DecideAsync(request with
            {
                Observation = request.Observation with { Candidates = [candidate] },
            }, cancellationToken);
        }
    }
}
