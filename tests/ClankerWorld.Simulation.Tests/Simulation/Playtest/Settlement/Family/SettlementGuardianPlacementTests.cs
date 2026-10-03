using ClankerWorld.GodotClient.UI;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;
using static ClankerWorld.Simulation.Tests.GuardianPlacementTestFixture;

namespace ClankerWorld.Simulation.Tests;

public sealed class SettlementGuardianPlacementTests
{
    [Fact]
    public async Task AcceptedGuardianCollectsAndPhysicallyEscortsChildBeforeChangingHomeAndTown()
    {
        var scenario = Prepared();
        var child = scenario.Children[0];
        var guardian = scenario.Guardians[0];
        var choices = Accepting(scenario);
        using var world = Restore(scenario.State, choices);
        var origin = Person(world, child).Position;
        var birth = world.Society.Births.Single(item => item.ChildId == child);
        var parentage = world.Society.Relationships.Where(item => item.Type == SocietyRelationshipType.BiologicalParentage &&
            item.TargetId == child).ToArray();
        Assert.True(scenario.State.Map.FootDistance(origin, scenario.House.Position) >= 6);
        Assert.NotEqual(scenario.OriginalTownId, scenario.DestinationTownId);
        await Accepted(world, scenario);
        Assert.Contains(choices.Selected, item => item.Actor == guardian && item.Choice == "guardian_accept:" + child);
        Assert.Equal(origin, Person(world, child).Position);
        Assert.Equal(SourceHousehold, world.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(scenario.OriginalTownId, TownOf(world, child));
        Assert.Equal("collecting", Person(world, child).GuardianPlacement!.Stage);
        AssertPlacementNote(world, child, guardian, "going to collect");

        await Until(world, () => Person(world, child).GuardianPlacement?.Stage == "escorting");
        Assert.True(scenario.State.Map.FootDistance(Person(world, child).Position, Person(world, guardian).Position) <= 2);
        AssertPlacementNote(world, child, guardian, "travelling together");
        await Until(world, () => Placed(world, scenario, child));

        Assert.Equal(scenario.House.Position, Person(world, child).Position);
        Assert.True(scenario.State.Map.FootDistance(Person(world, guardian).Position, scenario.House.Position) <= 1);
        Assert.Equal(guardian, world.Society.GetInhabitant(child).PrimaryCaregiverId);
        Assert.Equal(birth, world.Society.Births.Single(item => item.ChildId == child));
        Assert.Equal(parentage, world.Society.Relationships.Where(item => item.Type == SocietyRelationshipType.BiologicalParentage &&
            item.TargetId == child).ToArray());
        Assert.DoesNotContain(child, world.Towns.Single(town => town.Id == scenario.OriginalTownId).ResidentIds);
        Assert.Single(world.Towns.Single(town => town.Id == scenario.DestinationTownId).ResidentIds, id => id == child);
        AssertNoPlacementNote(world, child, guardian);
        var completed = Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_placement_completed");
        Assert.Equal(child + ":" + guardian, completed.Detail);
        Assert.True(GameUiText.IsPlayerFacingEvent(completed.Kind));
        Assert.True(GameUiText.IsPlayerFacingEvent(Assert.Single(world.ExportState().Events,
            item => item.Kind == "guardian_placement_pending").Kind));
        await Step(world);
        Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_placement_completed");
    }

    [Fact]
    public async Task MidJourneyPauseAndStrictReplayPreservePlacementAndOneCooldownActionPerTick()
    {
        var scenario = Prepared();
        var child = scenario.Children[0];
        var choices = Accepting(scenario);
        using var initial = Restore(scenario.State, choices);
        await Accepted(initial, scenario);
        await Until(initial, () => Person(initial, child).GuardianPlacement?.Stage == "escorting" &&
            Person(initial, child).Position != scenario.State.Inhabitants.Single(person => person.InhabitantId == child).Position);
        // A legal saved movement delay isolates the non-moving action budget as well as the per-step movement checks.
        var state = initial.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == child
            ? person with { TravelCooldownTicks = 2 } : person).ToArray()
        };
        using var world = Restore(state, choices);
        var position = Person(world, child).Position;
        world.Pause();
        var paused = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(paused, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        using var replay = Reload(world, choices.Copy());
        world.Resume();
        replay.Resume();
        var decrements = 0;
        for (var tick = 0; tick < 8 && Person(world, child).TravelCooldownTicks > 0; tick++)
        {
            var prior = Person(world, child).TravelCooldownTicks;
            await Step(world);
            await Step(replay);
            Assert.Equal(position, Person(world, child).Position);
            var current = Person(world, child).TravelCooldownTicks;
            Assert.InRange(prior - current, 0, 1);
            if (current < prior) decrements++;
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Equal(0, Person(world, child).TravelCooldownTicks);
        Assert.Equal(2, decrements);
        await Until(world, () => Placed(world, scenario, child), replay: replay);
        Assert.True(Placed(replay, scenario, child));
    }

    [Fact]
    public async Task ChildsAdmittedSocialIntentionYieldsToEscortWithoutSpendingTwoMovementActions()
    {
        var scenario = Prepared();
        var child = scenario.Children[0];
        var guardian = scenario.Guardians[0];
        var state = scenario.State;
        var society = state.Society.Society;
        var childAge = Assert.IsType<SocietyDayLifecycle>(society.Config.DayLifecycle).ChildStartDay;
        var birth = society.LifeTickAt(society.WorldTick + 1) - childAge * society.Config.TicksPerLifecycleAge;
        state = state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == child ? person with
                    {
                        BirthTick = birth,
                        BirthLifeTick = society.LifeClock is null ? null : birth,
                        AgeBand = society.Config.AgeBandAt(childAge - 1),
                        LastLifecycleYearChecked = childAge - 1,
                    } : person).ToArray(),
                },
            },
        };
        var choices = new GuardianPlacementChoices();
        var social = "child_converse:" + scenario.Guardians[1];
        choices.Set(child, social);
        using var initial = Restore(state, choices);
        Assert.Equal(SocietyAgeBand.Infant, initial.Society.GetInhabitant(child).AgeBand);
        await Step(initial);
        Assert.Equal(SocietyAgeBand.Child, initial.Society.GetInhabitant(child).AgeBand);
        await Until(initial, () => initial.ExportState().Society.Cognition.Runtimes.Single(item =>
            item.InhabitantId == child).CurrentIntention?.CandidateId == social, maximumTicks: 6);
        Assert.Contains(choices.Selected, item => item.Actor == child && item.Choice == social);
        choices.Set(guardian, "guardian_accept:" + child, "guardian_relocate:" + child);
        initial.SubmitInstruction(new("accept-child-care", "owner:test", guardian, OwnerInstructionKind.Suggestive,
            "Accept care for the orphan and accompany them home."));
        await Accepted(initial, scenario);
        await Until(initial, () => Person(initial, child).GuardianPlacement?.Stage == "escorting");
        Assert.Equal(social, initial.ExportState().Society.Cognition.Runtimes.Single(item =>
            item.InhabitantId == child).CurrentIntention?.CandidateId);
        state = initial.ExportState();
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == child
            ? person with { TravelCooldownTicks = 2 } : person).ToArray()
        };
        using var world = Restore(state, choices);
        using var replay = Reload(world, choices.Copy());
        var position = Person(world, child).Position;
        for (var remaining = 1; remaining >= 0; remaining--)
        {
            await Step(world);
            await Step(replay);
            Assert.Equal(position, Person(world, child).Position);
            Assert.Equal(remaining, Person(world, child).TravelCooldownTicks);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var firstEscortTick = world.WorldTick;
        await Until(world, () => Placed(world, scenario, child), replay: replay);
        Assert.Contains(world.ExportState().Events, item => item.WorldTick > firstEscortTick &&
            item.Kind == "inhabitant_moved" && item.Detail.StartsWith(child + ":", StringComparison.Ordinal) &&
            item.Detail.EndsWith(":follow_guardian", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AcceptedCareWaitsForARealResidentPlaceThenContinuesAfterAdultDisplacement()
    {
        var scenario = Prepared(residentChildren: 2);
        var child = scenario.Children[0];
        var choices = Accepting(scenario);
        using var world = Restore(scenario.State, choices);
        await Accepted(world, scenario);
        Assert.Equal(4, Capacity(world).ResidentCount);
        Assert.Equal(4, Capacity(world).Limit);
        var origin = Person(world, child).Position;
        for (var tick = 0; tick < 3; tick++) await Step(world);
        Assert.Equal(origin, Person(world, child).Position);
        Assert.Equal(SourceHousehold, world.Society.GetInhabitant(child).HouseholdId);
        Assert.Contains("free resident place", Person(world, child).GuardianPlacement!.Blocker!, StringComparison.Ordinal);
        AssertPlacementNote(world, child, scenario.Guardians[0], "The move is waiting.");

        Assert.True(world.DisplaceAdult(scenario.Guardians[1]));
        Assert.Equal(3, Capacity(world).ResidentCount);
        using var replay = Reload(world, choices.Copy());
        await Until(world, () => Placed(world, scenario, child), replay: replay);
        Assert.Equal(4, Capacity(world).ResidentCount);
        Assert.False(Capacity(world).IsOvercrowded);
    }

    [Fact]
    public async Task TwoAcceptedJourneysCannotClaimTheSameLastResidentPlace()
    {
        var scenario = Prepared(orphanCount: 2);
        var choices = Accepting(scenario, both: true);
        using var world = Restore(scenario.State, choices);
        await Accepted(world, scenario, both: true);
        Assert.All(scenario.Children, child => Assert.NotNull(Person(world, child).GuardianPlacement));
        await Until(world, () => scenario.Children.Any(child => Placed(world, scenario, child)));
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.False(Capacity(world).IsOvercrowded);
            await Step(world);
        }
        var placed = Assert.Single(scenario.Children, child => Placed(world, scenario, child));
        var waiting = Assert.Single(scenario.Children, child => child != placed);
        Assert.Equal(4, Capacity(world).ResidentCount);
        Assert.Equal(SourceHousehold, world.Society.GetInhabitant(waiting).HouseholdId);
        Assert.Equal(scenario.OriginalTownId, TownOf(world, waiting));
        Assert.Contains("free resident place", Person(world, waiting).GuardianPlacement!.Blocker!, StringComparison.Ordinal);
        Assert.NotNull(world.Society.GetInhabitant(waiting).PrimaryCaregiverId);
        Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_placement_completed");
        using var restored = Reload(world, choices.Copy());
        Assert.Equal(Person(world, waiting).GuardianPlacement, Person(restored, waiting).GuardianPlacement);
    }

    [Fact]
    public async Task ExplicitlyEndingCareDuringEscortCancelsPlacementWithoutChangingMembership()
    {
        var scenario = Prepared();
        var child = scenario.Children[0];
        var guardian = scenario.Guardians[0];
        var choices = Accepting(scenario);
        using var world = Restore(scenario.State, choices);
        await Accepted(world, scenario);
        await Until(world, () => Person(world, child).GuardianPlacement?.Stage == "escorting");
        var care = Person(world, child).GuardianPlacement!.CareRelationshipId;
        choices.Set(guardian, "guardian_end:" + care);
        world.SubmitInstruction(new("end-accepted-care", "owner:test", guardian, OwnerInstructionKind.Suggestive,
            "Withdraw from this caregiving relationship."));
        await Until(world, () => world.Society.GetInhabitant(child).PrimaryCaregiverId is null, maximumTicks: 12);
        Assert.Contains(choices.Selected, item => item.Actor == guardian && item.Choice == "guardian_end:" + care);
        Assert.Equal(SocietyRelationshipState.Revoked, world.Society.GetRelationship(care).State);
        Assert.Null(Person(world, child).GuardianPlacement);
        Assert.Equal(SourceHousehold, world.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(scenario.OriginalTownId, TownOf(world, child));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "guardian_placement_completed");
        var cancelled = Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_placement_cancelled");
        Assert.Equal(child, cancelled.Detail);
        Assert.True(GameUiText.IsPlayerFacingEvent(cancelled.Kind));
        using var restored = Reload(world, choices.Copy());
        await Step(restored);
        Assert.Null(Person(restored, child).GuardianPlacement);
    }

    [Fact]
    public async Task RefusedArrivalTickCannotCommitPhysicalOrMembershipPlacement()
    {
        var scenario = Prepared();
        var child = scenario.Children[0];
        var guardian = scenario.Guardians[0];
        using var world = Restore(scenario.State, Accepting(scenario));
        await Accepted(world, scenario);
        await Until(world, () => Person(world, child).GuardianPlacement?.Stage == "escorting" &&
            Person(world, guardian).Position == scenario.House.Position && Person(world, child).TravelCooldownTicks == 0 &&
            scenario.State.Map.FootDistance(Person(world, child).Position, scenario.House.Position) == 1);
        var before = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(before, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(SourceHousehold, world.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(scenario.OriginalTownId, TownOf(world, child));
        await Step(world);
        Assert.True(Placed(world, scenario, child));
        Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_placement_completed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UrgentChildReceivesActualFoodBeforeTheEscortResumes(bool fullCarry)
    {
        var scenario = Prepared();
        var child = scenario.Children[0];
        var guardian = scenario.Guardians[0];
        var choices = Accepting(scenario);
        using var initial = Restore(scenario.State, choices);
        await Accepted(initial, scenario);
        await Until(initial, () => Person(initial, child).GuardianPlacement?.Stage == "escorting");
        var state = initial.ExportState();
        var inventory = fullCarry
            ? InventoryFixture.AddLot(state.Society.Society.Inventory, "guardian-travel-meal", "food", DestinationHousehold, 1,
                storageBuildingId: scenario.House.InstanceId)
            : InventoryFixture.AddLot(state.Society.Society.Inventory, "guardian-travel-meal", "food", guardian, 1);
        if (fullCarry)
            inventory = InventoryFixture.AddLot(inventory, "guardian-spare-wood", "wood", guardian,
                PersonalEquipmentRules.BaseCapacity);
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == child
                ? person with { HungerBasisPoints = 100 } : person).ToArray(),
        };
        if (fullCarry)
        {
            // The longest name a child may hold: 48 characters ending in its parents' surname.
            var surname = state.Society.Society.GetInhabitant(child).Name.Split(' ')[^1];
            state = state with
            {
                Society = state.Society with
                {
                    Society = SocietyFixture.RenameInhabitant(state.Society.Society, child,
                        "Robin" + new string('r', 42 - surname.Length) + " " + surname).Checkpoint,
                },
            };
        }
        using var world = Restore(state, choices);
        if (fullCarry) Assert.Equal(48, world.Society.GetInhabitant(child).Name.Length);
        using var replay = Reload(world, choices.Copy());
        var childPosition = Person(world, child).Position;
        await Until(world, () => world.ExportState().Events.Any(item => item.Kind == "child_cared_for" && item.Detail == child),
            maximumTicks: fullCarry ? 100 : 6, replay: replay);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.ItemKind == "food");
        if (fullCarry)
        {
            Assert.Equal(PersonalEquipmentRules.BaseCapacity,
                world.Society.Inventory.Lots.Where(lot => lot.ItemKind == "wood").Sum(lot => lot.Quantity));
            Assert.Contains(world.Society.Inventory.Lots, lot => lot.ItemKind == "wood" &&
                lot.OwnerId == DestinationHousehold && lot.StorageBuildingId == scenario.House.InstanceId);
            Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" &&
                item.Detail.StartsWith(guardian + ":", StringComparison.Ordinal) && item.Detail.EndsWith(":make_room", StringComparison.Ordinal));
        }
        Assert.True(Person(world, child).HungerBasisPoints > 100);
        Assert.Equal(childPosition, Person(world, child).Position);
        Assert.Equal(SourceHousehold, world.Society.GetInhabitant(child).HouseholdId);
        Assert.NotNull(Person(world, child).GuardianPlacement);
        await Until(world, () => Placed(world, scenario, child), replay: replay);
    }

    private static void AssertPlacementNote(PrivateWorldRuntime world, string child, string guardian, string progress)
    {
        var prefix = $"{world.Society.GetInhabitant(guardian).Name} accepted care for {world.Society.GetInhabitant(child).Name}.";
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        foreach (var actor in new[] { child, guardian })
        {
            var person = snapshot.Inhabitants.Single(item => item.Id == actor);
            var note = Assert.Single(person.SocialNotes, text => text.StartsWith(prefix, StringComparison.Ordinal));
            Assert.Contains(progress, note, StringComparison.Ordinal);
            Assert.Contains(person.DecisionFactors, factor => factor.Key == "guardian-care" && factor.Detail == note);
        }
    }

    private static void AssertNoPlacementNote(PrivateWorldRuntime world, string child, string guardian)
    {
        var snapshot = new OwnerWorldObservationStore(world).GetSnapshot();
        foreach (var actor in new[] { child, guardian })
        {
            var person = snapshot.Inhabitants.Single(item => item.Id == actor);
            Assert.DoesNotContain(person.SocialNotes, text => text.Contains("accepted care for", StringComparison.Ordinal));
            Assert.DoesNotContain(person.DecisionFactors, factor => factor.Key == "guardian-care");
        }
    }
}
