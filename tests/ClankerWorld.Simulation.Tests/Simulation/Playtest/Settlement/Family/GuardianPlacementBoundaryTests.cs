using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class GuardianPlacementBoundaryTests
{
    private static readonly Lazy<Task<byte[]>> AcceptedCheckpoint = new(CreateAcceptedCheckpoint);

    [Theory]
    [InlineData("stage")]
    [InlineData("future_start")]
    [InlineData("care_edge")]
    [InlineData("care_revision")]
    [InlineData("caregiver")]
    [InlineData("partial_house")]
    [InlineData("unknown_household")]
    [InlineData("unknown_town")]
    [InlineData("escorting_without_house")]
    public async Task SavedPlacementRejectsForgedCareAndIncompleteDestinations(string damage)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await AcceptedCheckpoint.Value);
        var child = Assert.Single(state.Inhabitants, person => person.GuardianPlacement is not null);
        var placement = Assert.IsType<SettlementGuardianPlacement>(child.GuardianPlacement);
        var otherAdult = state.Society.Society.Inhabitants.First(person => person.Id != placement.CaregiverId &&
            person.Status == SocietyInhabitantStatus.Active && person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).Id;
        var invalid = damage switch
        {
            "stage" => placement with { Stage = "arrived" },
            "future_start" => placement with { StartedTick = state.Society.Society.WorldTick + 1 },
            "care_edge" => placement with
            {
                CareRelationshipId = state.Society.Society.Relationships.First(edge =>
                    edge.Type == SocietyRelationshipType.BiologicalParentage && edge.TargetId == child.InhabitantId).Id,
            },
            "care_revision" => placement with { CareRevision = placement.CareRevision + 1 },
            "caregiver" => placement with { CaregiverId = otherAdult },
            "partial_house" => placement with { HousePosition = null },
            "unknown_household" => placement with { DestinationHouseholdId = "household:missing-guardian-home" },
            "unknown_town" => placement with { DestinationTownId = "town:missing-guardian-home" },
            "escorting_without_house" => placement with
            {
                Stage = "escorting",
                HouseId = null,
                HouseDefinitionId = null,
                HousePlacedTick = null,
                HousePosition = null,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(damage)),
        };
        using var original = GuardianPlacementTestFixture.Restore(state);
        var canonical = PrivateWorldRuntimeCodec.Encode(original.ExportState());
        var damaged = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == child.InhabitantId
                ? person with { GuardianPlacement = invalid } : person).ToArray(),
        };
        var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntime.Restore(damaged, _ => new GuardianPlacementChoices()));
        Assert.Contains("guardian placement", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(canonical, PrivateWorldRuntimeCodec.Encode(original.ExportState()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovedOrReassignedHouseRemainsLoadableButCannotReceiveTheChild(bool reassign)
    {
        var scenario = GuardianPlacementTestFixture.Prepared();
        var choices = GuardianPlacementTestFixture.Accepting(scenario);
        using var world = GuardianPlacementTestFixture.Restore(PrivateWorldRuntimeCodec.Decode(await AcceptedCheckpoint.Value), choices);
        var child = scenario.Children[0];
        var accepted = Assert.IsType<SettlementGuardianPlacement>(GuardianPlacementTestFixture.Person(world, child).GuardianPlacement);
        Assert.Equal(scenario.House.InstanceId, accepted.HouseId);
        if (reassign)
        {
            var remove = world.RemoveBuilding(scenario.SourceHouse.InstanceId, scenario.SourceHouse.TownId,
                scenario.SourceHouse.HouseholdId);
            Assert.True(remove.Applied, remove.Failure);
            var result = world.ReassignBuilding(scenario.House.InstanceId, scenario.House.TownId,
                scenario.House.HouseholdId, null, scenario.SourceHouse.HouseholdId);
            Assert.True(result.Applied, result.Failure);
        }
        else
        {
            var result = world.RemoveBuilding(scenario.House.InstanceId, scenario.House.TownId, scenario.House.HouseholdId);
            Assert.True(result.Applied, result.Failure);
        }

        // Public edits can make the saved route stale between ticks. Loading is
        // legal; the next physical action must recheck the current home.
        using var replay = GuardianPlacementTestFixture.Reload(world, choices.Copy());
        for (var tick = 0; tick < 3; tick++)
        {
            await GuardianPlacementTestFixture.Step(world);
            await GuardianPlacementTestFixture.Step(replay);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var pending = Assert.IsType<SettlementGuardianPlacement>(GuardianPlacementTestFixture.Person(world, child).GuardianPlacement);
        Assert.Null(pending.HouseId);
        Assert.Contains("completed House", pending.Blocker, StringComparison.Ordinal);
        Assert.Equal(scenario.Guardians[0], world.Society.GetInhabitant(child).PrimaryCaregiverId);
        Assert.Equal(GuardianPlacementTestFixture.SourceHousehold, world.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(scenario.OriginalTownId, GuardianPlacementTestFixture.TownOf(world, child));
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "guardian_placement_completed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalAgeOrGuardianDeathBoundaryCancelsPlacementWithoutTransferringMembership(bool guardianDies)
    {
        var scenario = GuardianPlacementTestFixture.Prepared();
        var state = PrivateWorldRuntimeCodec.Decode(await AcceptedCheckpoint.Value);
        var child = scenario.Children[0];
        var guardian = scenario.Guardians[0];
        var society = state.Society.Society;
        var id = guardianDies ? guardian : child;
        var age = guardianDies
            ? Assert.IsType<SocietyDayLifecycle>(society.Config.DayLifecycle).MaximumDay
            : society.Config.DayLifecycle?.AdultStartDay ?? society.Config.AdultYears;
        var birth = society.LifeTickAt(society.WorldTick + 1) - age * society.Config.TicksPerLifecycleAge;
        state = state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Inhabitants = society.Inhabitants.Select(person => person.Id == id ? person with
                    {
                        BirthTick = birth,
                        BirthLifeTick = society.LifeClock is null ? null : birth,
                        AgeBand = society.Config.AgeBandAt(age - 1),
                        LastLifecycleYearChecked = age - 1,
                    } : person).ToArray(),
                },
            },
        };
        var choices = GuardianPlacementTestFixture.Accepting(scenario);
        using var world = GuardianPlacementTestFixture.Restore(state, choices);
        Assert.NotNull(GuardianPlacementTestFixture.Person(world, child).GuardianPlacement);
        using var replay = GuardianPlacementTestFixture.Reload(world, choices.Copy());
        await GuardianPlacementTestFixture.Step(world);
        await GuardianPlacementTestFixture.Step(replay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.Null(GuardianPlacementTestFixture.Person(world, child).GuardianPlacement);
        Assert.Equal(GuardianPlacementTestFixture.SourceHousehold, world.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(scenario.OriginalTownId, GuardianPlacementTestFixture.TownOf(world, child));
        Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_placement_cancelled" && item.Detail == child);
        Assert.DoesNotContain(world.ExportState().Events, item => item.Kind == "guardian_placement_completed");
        Assert.Equal(state.Society.Society.Births, world.Society.Births);
        if (guardianDies)
        {
            Assert.Equal(SocietyInhabitantStatus.Dead, world.Society.GetInhabitant(guardian).Status);
            Assert.DoesNotContain(world.Inhabitants, person => person.InhabitantId == guardian);
        }
        else Assert.Equal(SocietyAgeBand.Adult, world.Society.GetInhabitant(child).AgeBand);
    }

    [Fact]
    public async Task ExistingHouseholdMembershipDoesNotSpendASecondPlaceWhenOnlyTownPlacementRemains()
    {
        var scenario = GuardianPlacementTestFixture.Prepared();
        var state = PrivateWorldRuntimeCodec.Decode(await AcceptedCheckpoint.Value);
        var child = scenario.Children[0];
        var placement = Assert.IsType<SettlementGuardianPlacement>(state.Inhabitants.Single(person => person.InhabitantId == child).GuardianPlacement);
        // The Society operation deliberately changes only household membership.
        // The runtime must still finish physical arrival and the independent Town transfer.
        var householdMove = SocietyFixture.PlaceDependentWithGuardian(state.Society.Society, placement.CaregiverId,
            child, placement.CareRelationshipId, placement.CareRevision, GuardianPlacementTestFixture.DestinationHousehold);
        Assert.Equal(child, householdMove.CreatedId);
        state = state with { Society = state.Society with { Society = householdMove.Checkpoint } };
        var choices = GuardianPlacementTestFixture.Accepting(scenario);
        using var world = GuardianPlacementTestFixture.Restore(state, choices);
        Assert.Equal(scenario.OriginalTownId, GuardianPlacementTestFixture.TownOf(world, child));
        var capacity = GuardianPlacementTestFixture.Capacity(world);
        Assert.Equal(4, capacity.ResidentCount);
        Assert.Equal(4, capacity.Limit);
        Assert.NotNull(GuardianPlacementTestFixture.Person(world, child).GuardianPlacement);
        // Revoke and accept through the normal offered decisions, so a fresh
        // acceptance must create placement even though household already agrees.
        choices.Set(placement.CaregiverId, "guardian_end:" + placement.CareRelationshipId);
        world.SubmitInstruction(new("same-home-end-care", "owner:test", placement.CaregiverId,
            OwnerInstructionKind.Suggestive, "Withdraw from this caregiving relationship."));
        await GuardianPlacementTestFixture.Until(world,
            () => world.Society.GetInhabitant(child).PrimaryCaregiverId is null, maximumTicks: 12);
        Assert.Null(GuardianPlacementTestFixture.Person(world, child).GuardianPlacement);
        Assert.Equal(GuardianPlacementTestFixture.DestinationHousehold, world.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(scenario.OriginalTownId, GuardianPlacementTestFixture.TownOf(world, child));
        choices.Set(placement.CaregiverId, "guardian_accept:" + child, "guardian_relocate:" + child);
        world.SubmitInstruction(new("same-home-accept-care", "owner:test", placement.CaregiverId,
            OwnerInstructionKind.Suggestive, "Accept primary care of this child again."));
        await GuardianPlacementTestFixture.Accepted(world, scenario);
        var renewed = Assert.IsType<SettlementGuardianPlacement>(GuardianPlacementTestFixture.Person(world, child).GuardianPlacement);
        Assert.NotEqual(placement.CareRelationshipId, renewed.CareRelationshipId);
        Assert.Equal(GuardianPlacementTestFixture.DestinationHousehold, renewed.DestinationHouseholdId);
        Assert.Equal(scenario.OriginalTownId, GuardianPlacementTestFixture.TownOf(world, child));
        Assert.Equal(capacity, GuardianPlacementTestFixture.Capacity(world));
        using var replay = GuardianPlacementTestFixture.Reload(world, choices.Copy());
        await GuardianPlacementTestFixture.Until(world, () => GuardianPlacementTestFixture.Placed(world, scenario, child), replay: replay);
        Assert.Equal(scenario.House.Position, GuardianPlacementTestFixture.Person(world, child).Position);
        Assert.Equal(capacity, GuardianPlacementTestFixture.Capacity(world));
        Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_placement_completed");
    }

    [Fact]
    public async Task NativeDeathArchivesTheChildWithoutAnActionablePlacement()
    {
        var scenario = GuardianPlacementTestFixture.Prepared();
        var state = PrivateWorldRuntimeCodec.Decode(await AcceptedCheckpoint.Value);
        var child = scenario.Children[0];
        var pending = Assert.IsType<SettlementGuardianPlacement>(state.Inhabitants.Single(person => person.InhabitantId == child).GuardianPlacement);
        var society = state.Society.Society;
        var compactLifecycle = new SocietyDayLifecycle(1, 2, 3, 4);
        var config = society.Config with { DayLifecycle = compactLifecycle };
        // A lawful short lifecycle and the public fast life pace cross several
        // age boundaries in one tick. The child is still a dependent on restore;
        // the runtime itself commits death and removes the active physical state.
        state = state with
        {
            Society = state.Society with
            {
                Society = society with
                {
                    Config = config,
                    Inhabitants = society.Inhabitants.Select(person =>
                    {
                        if (person.Status != SocietyInhabitantStatus.Active) return person;
                        var age = person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder ? 2 : 0;
                        var birth = society.LifeTickAt(society.WorldTick) - age * config.TicksPerLifecycleAge;
                        return person with
                        {
                            BirthTick = birth,
                            BirthLifeTick = society.LifeClock is null ? null : birth,
                            AgeBand = config.AgeBandAt(age),
                            LastLifecycleYearChecked = age,
                        };
                    }).ToArray(),
                },
            },
        };
        using var world = GuardianPlacementTestFixture.Restore(state);
        Assert.Equal(SocietyAgeBand.Infant, world.Society.GetInhabitant(child).AgeBand);
        Assert.NotNull(GuardianPlacementTestFixture.Person(world, child).GuardianPlacement);
        world.Pause();
        Assert.True(world.SetLifePace(1_460));
        world.Resume();
        await GuardianPlacementTestFixture.Step(world);
        Assert.Equal(SocietyDeathCause.NaturalAge, world.Society.GetInhabitant(child).DeathCause);
        Assert.DoesNotContain(world.Inhabitants, person => person.InhabitantId == child);
        var final = world.ExportState();
        var archived = Assert.Single(final.DeceasedInhabitants!, person => person.InhabitantId == child);
        Assert.Null(archived.LastPhysical.GuardianPlacement);
        using var reload = GuardianPlacementTestFixture.Reload(world, new());
        var invalid = final with
        {
            DeceasedInhabitants = final.DeceasedInhabitants!.Select(person => person.InhabitantId == child
                ? person with { LastPhysical = person.LastPhysical with { GuardianPlacement = pending } } : person).ToArray(),
        };
        var error = Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Encode(invalid));
        Assert.Contains("deceased inhabitant archive", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("guardian")]
    [InlineData("household")]
    public async Task HouseholdPlacementCannotReuseAnotherCareRevisionGuardianOrDestination(string mismatch)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await AcceptedCheckpoint.Value);
        var child = Assert.Single(state.Inhabitants, person => person.GuardianPlacement is not null);
        var placement = Assert.IsType<SettlementGuardianPlacement>(child.GuardianPlacement);
        var society = state.Society.Society;
        var guardian = mismatch == "guardian"
            ? society.GetHousehold(GuardianPlacementTestFixture.DestinationHousehold).MemberIds.First(id =>
                id != placement.CaregiverId && society.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
            : placement.CaregiverId;
        var result = SocietyFixture.PlaceDependentWithGuardian(society, guardian, child.InhabitantId,
            placement.CareRelationshipId, placement.CareRevision + (mismatch == "revision" ? 1 : 0),
            mismatch == "household" ? GuardianPlacementTestFixture.SourceHousehold : GuardianPlacementTestFixture.DestinationHousehold);
        Assert.Null(result.CreatedId);
        Assert.Equal(society.Inhabitants, result.Checkpoint.Inhabitants);
        Assert.Equal(society.Households, result.Checkpoint.Households);
        Assert.Equal(society.Relationships, result.Checkpoint.Relationships);
        Assert.Contains(result.NewEvents!, item => item.Kind == "care_placement_rejected");
    }

    [Fact]
    public async Task PlacementUsesTheGuardiansRecordedTownRatherThanTheHousesGeographicTown()
    {
        var scenario = GuardianPlacementTestFixture.Prepared();
        var state = PrivateWorldRuntimeCodec.Decode(await AcceptedCheckpoint.Value);
        var child = scenario.Children[0];
        var movingIds = state.Towns!.Single(town => town.Id == scenario.DestinationTownId).ResidentIds;
        state = state with
        {
            Towns = state.Towns!.Select(town => town with
            {
                ResidentIds = town.Id == scenario.OriginalTownId
                    ? town.ResidentIds.Concat(movingIds).Order(StringComparer.Ordinal).ToArray()
                    : town.ResidentIds.Except(movingIds, StringComparer.Ordinal).ToArray(),
                Governance = TownGovernanceState.Create(town.Id == scenario.OriginalTownId ? scenario.Guardians : []),
            }).ToArray(),
        };
        var choices = GuardianPlacementTestFixture.Accepting(scenario);
        using var world = GuardianPlacementTestFixture.Restore(state, choices);
        Assert.Equal(scenario.DestinationTownId, world.WorldSimulation.Buildings.Single(building => building.InstanceId == scenario.House.InstanceId).TownId);
        Assert.Equal(scenario.OriginalTownId, GuardianPlacementTestFixture.TownOf(world, scenario.Guardians[0]));
        using var replay = GuardianPlacementTestFixture.Reload(world, choices.Copy());
        await GuardianPlacementTestFixture.Until(world, () => GuardianPlacementTestFixture.Person(world, child).GuardianPlacement is null, replay: replay);
        Assert.Equal(scenario.House.Position, GuardianPlacementTestFixture.Person(world, child).Position);
        Assert.Equal(GuardianPlacementTestFixture.DestinationHousehold, world.Society.GetInhabitant(child).HouseholdId);
        Assert.Equal(scenario.OriginalTownId, GuardianPlacementTestFixture.TownOf(world, child));
        Assert.DoesNotContain(child, world.Towns.Single(town => town.Id == scenario.DestinationTownId).ResidentIds);
        Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_placement_completed");
    }

    [Fact]
    public async Task NativeLongHouseIdentitySurvivesAcceptedPlacementAndStrictReload()
    {
        var scenario = GuardianPlacementTestFixture.Prepared();
        var inventory = InventoryFixture.AddLot(scenario.State.Society.Society.Inventory,
            "guardian-long-home-timber", "wood", GuardianPlacementTestFixture.DestinationHousehold, 8);
        var state = scenario.State with
        {
            Society = scenario.State.Society with { Society = scenario.State.Society.Society with { Inventory = inventory } },
        };
        var choices = GuardianPlacementTestFixture.Accepting(scenario);
        using var world = GuardianPlacementTestFixture.Restore(state, choices);
        var removal = world.RemoveBuilding(scenario.House.InstanceId, scenario.House.TownId, scenario.House.HouseholdId);
        Assert.True(removal.Applied, removal.Failure);
        var longId = "guardian-house-" + new string('h', 600);
        var result = world.PlaceBuilding(longId, scenario.House.DefinitionId, scenario.House.Position, scenario.House.HouseholdId);
        Assert.True(result.Applied, result.Failure);
        Assert.DoesNotContain(world.Society.Inventory.Lots, lot => lot.Id == "guardian-long-home-timber");
        await GuardianPlacementTestFixture.Accepted(world, scenario);
        var placement = Assert.IsType<SettlementGuardianPlacement>(GuardianPlacementTestFixture.Person(world, scenario.Children[0]).GuardianPlacement);
        Assert.Equal(longId, placement.HouseId);
        using var replay = GuardianPlacementTestFixture.Reload(world, choices.Copy());
        await GuardianPlacementTestFixture.Step(world);
        await GuardianPlacementTestFixture.Step(replay);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    private static async Task<byte[]> CreateAcceptedCheckpoint()
    {
        var scenario = GuardianPlacementTestFixture.Prepared();
        using var world = GuardianPlacementTestFixture.Restore(scenario.State, GuardianPlacementTestFixture.Accepting(scenario));
        await GuardianPlacementTestFixture.Accepted(world, scenario);
        var placement = Assert.IsType<SettlementGuardianPlacement>(GuardianPlacementTestFixture.Person(world, scenario.Children[0]).GuardianPlacement);
        Assert.Equal(scenario.House.InstanceId, placement.HouseId);
        Assert.Equal(scenario.Guardians[0], placement.CaregiverId);
        Assert.Equal(GuardianPlacementTestFixture.SourceHousehold, world.Society.GetInhabitant(scenario.Children[0]).HouseholdId);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }
}
