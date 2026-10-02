using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Fact]
    public void EndingOneCareObligationPreservesOtherDependentsHouseholdProjection()
    {
        var adult = SocietyFixture.CreateFounder("adult", "Adult");
        var child = SocietyFixture.CreateFounder("child", "Child") with
        {
            BirthTick = 0,
            AgeBand = SocietyAgeBand.Infant,
            LastLifecycleYearChecked = 0,
        };
        var checkpoint = SocietyFixture.CreateGenesis("multiple-care", [adult, child, child with { Id = "other" }]);
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "Home", ["adult", "child", "other"]).Checkpoint;
        var first = SocietyFixture.AssumeInfantCare(checkpoint, "adult", "child");
        var second = SocietyFixture.AssumeInfantCare(first.Checkpoint, "adult", "other");
        checkpoint = SocietyFixture.RevokeRelationship(second.Checkpoint, first.CreatedId!, "adult").Checkpoint;
        Assert.Contains("adult", checkpoint.GetHousehold("home").CaregiverIds);
        checkpoint = SocietyFixture.RevokeRelationship(checkpoint, second.CreatedId!, "adult").Checkpoint;
        Assert.DoesNotContain("adult", checkpoint.GetHousehold("home").CaregiverIds);
    }

    [Fact]
    public async Task OrphanedInfantGetsOneVoluntaryCaregiverAndRealCareAcrossRestart()
    {
        var state = await OrphanState();
        var child = state.Society.Society.Births.Single().ChildId;
        using var world = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("guardian_accept:"));
        for (var tick = 0; tick < 40 && !world.Society.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                 edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted); tick++) await world.AdvanceOneTickAsync();
        var caregiver = Assert.Single(world.Society.Relationships, edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted);
        Assert.Equal(SocietyConsentState.Accepted, caregiver.Consent);
        Assert.Equal([caregiver.ProposerId], caregiver.AcceptedBy);
        Assert.Equal(2, world.Society.Relationships.Count(edge => edge.Type == SocietyRelationshipType.BiologicalParentage && edge.TargetId == child));
        Assert.Contains(world.ExportState().Events, item => item.Kind == "guardian_needed" && item.Detail == child);
        Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_needed" && item.Detail == child);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "guardian_assigned" && item.Detail == child);
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ParentProvider("care:"));
        Assert.False((await restored.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        restored.Resume();
        for (var tick = 0; tick < 80 && !restored.ExportState().Events.Any(item => item.Kind == "child_cared_for"); tick++)
            await restored.AdvanceOneTickAsync();
        Assert.Contains(restored.ExportState().Events, item => item.Kind == "child_cared_for");
        Assert.True(restored.Inhabitants.Single(person => person.InhabitantId == child).HungerBasisPoints > 3_000);
        Assert.Contains(new OwnerWorldObservationStore(restored).GetSnapshot().Inhabitants.Single(person => person.Id == child).Relationships,
            edge => edge.Type == "caregiver");
    }

    [Fact]
    public async Task OlderDependentKeepsVisibleGuardianNeedUntilAnAdultAcceptsAcrossReload()
    {
        var state = await OrphanState(olderChild: true);
        var child = state.Society.Society.Births.Single().ChildId;
        using var world = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("safe_idle"));
        for (var tick = 0; tick < 1; tick++)
            await world.AdvanceOneTickAsync();
        var pending = world.ExportState();
        var initialSearch = Assert.Single(pending.Inhabitants, person => person.InhabitantId == child).GuardianSearch!;
        Assert.Equal("household", initialSearch.Stage);
        Assert.NotEmpty(initialSearch.OfferedAdultIds);
        Assert.Single(pending.Events, item => item.Kind == "guardian_needed" && item.Detail == child);
        Assert.Contains("Needs a guardian. No adult has accepted care yet; nearby adults may still feed them.",
            new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == child).SocialNotes);
        world.Pause();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var pendingReload = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ParentProvider("safe_idle"));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(pendingReload.ExportState()));

        var acceptingAdult = initialSearch.OfferedAdultIds[0];
        var acceptingProvider = new ParentProvider("guardian_accept:");
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            actor => actor == acceptingAdult ? acceptingProvider : new ParentProvider("safe_idle"));
        restored.Resume();
        var stageTicks = restored.ExportState().WorldSystems!.Config.TicksPerDay;
        for (var tick = 0; tick < stageTicks && !restored.Society.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                 edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted); tick++)
            await restored.AdvanceOneTickAsync();
        var accepted = Assert.Single(restored.Society.Relationships, edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted);
        Assert.Equal(acceptingAdult, accepted.ProposerId);
        Assert.Contains(acceptingProvider.SeenCandidates, candidate => candidate.Id == "guardian_accept:" + child);
        Assert.True(accepted.EffectiveTick - state.Society.Society.WorldTick < stageTicks,
            "A newly opened household offer must be decided before its one-day stage expires.");
        Assert.Equal(SocietyConsentState.Accepted, accepted.Consent);
        Assert.Equal([accepted.ProposerId], accepted.AcceptedBy);
        Assert.Null(Assert.Single(restored.ExportState().Inhabitants, person => person.InhabitantId == child).GuardianSearch);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "guardian_needed" && item.Detail == child);
        Assert.Single(restored.ExportState().Events, item => item.Kind == "guardian_assigned" && item.Detail == child);

        using var waiting = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new ParentProvider("safe_idle"));
        waiting.Resume();
        var waitingStageTicks = waiting.ExportState().WorldSystems!.Config.TicksPerDay;
        for (var tick = 0; tick < waitingStageTicks; tick++) await waiting.AdvanceOneTickAsync();
        var townPending = waiting.ExportState();
        Assert.Equal("town", Assert.Single(townPending.Inhabitants, person => person.InhabitantId == child).GuardianSearch!.Stage);
        Assert.Single(townPending.Events, item => item.Kind == "guardian_needed" && item.Detail == child);
        waiting.Pause();
        var townSave = PrivateWorldRuntimeCodec.Encode(waiting.ExportState());
        using var townRestored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(townSave), _ => new ParentProvider("safe_idle"));
        Assert.Equal(townSave, PrivateWorldRuntimeCodec.Encode(townRestored.ExportState()));
    }

    [Fact]
    public async Task GuardianOrderRejectsAnExtraTargetAndAcceptsTheExactChildId()
    {
        var state = await OrphanState(olderChild: true);
        var child = state.Society.Society.Births.Single().ChildId;
        var childName = state.Society.Society.GetInhabitant(child).Name;
        var acceptingAdult = state.Society.Society.Inhabitants.First(person => person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder && person.Id != child).Id;
        using var world = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("safe_idle"));

        var mismatched = world.SubmitInstruction(new OwnerInstructionRequest("guardian-mismatch", "owner:test",
            acceptingAdult, OwnerInstructionKind.MustDo, $"guardian for {childName} and SomeoneElse"));
        Assert.Contains(mismatched.InstructionId, world.ExportState().CompletedInstructionIds ?? []);
        Assert.DoesNotContain(world.Society.Relationships, edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted);

        var exact = world.SubmitInstruction(new OwnerInstructionRequest("guardian-exact-id", "owner:test",
            acceptingAdult, OwnerInstructionKind.MustDo, $"guardian_accept:{child}"));
        for (var tick = 0; tick < 40 && !world.Society.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                 edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);

        var accepted = Assert.Single(world.Society.Relationships, edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted);
        Assert.Equal(acceptingAdult, accepted.ProposerId);
        Assert.Contains(exact.InstructionId, world.ExportState().CompletedInstructionIds ?? []);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "instruction_applied" && item.Detail.StartsWith(exact.InstructionId + ":guardian_accept:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NearbyAdultCanFeedAnOrphanWhileGuardianSearchRemainsOpen()
    {
        var state = await OrphanState();
        var childId = state.Society.Society.Births.Single().ChildId;
        var adultId = state.Inhabitants.First(person => person.InhabitantId != childId).InhabitantId;
        var child = state.Inhabitants.Single(person => person.InhabitantId == childId);
        var feedingTile = state.Map.FootNeighbors(child.Position).First(point => state.Map.IsPassable(point) &&
            !state.Inhabitants.Any(person => person.InhabitantId != adultId && person.Position == point));
        state = state with
        {
            Society = state.Society with
            {
                Society = state.Society.Society with
                {
                    Inventory = InventoryFixture.AddLot(state.Society.Society.Inventory,
                        "orphan-nearby-food", "berries", adultId, 1),
                },
            },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId switch
            {
                var id when id == adultId => person with { Position = feedingTile, HungerBasisPoints = 9_000, LastDecisionContext = null },
                var id when id == childId => person with { HungerBasisPoints = 2_000, LastDecisionContext = null },
                _ => person,
            }).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state, actor =>
            new ParentProvider(actor == adultId ? "care:" : "safe_idle"));
        for (var tick = 0; tick < 10 && !world.ExportState().Events.Any(item => item.Kind == "child_cared_for"); tick++)
            await world.AdvanceOneTickAsync();

        Assert.Contains(world.ExportState().Events, item => item.Kind == "child_cared_for" && item.Detail == childId);
        Assert.Null(world.Society.GetInhabitant(childId).PrimaryCaregiverId);
        Assert.NotNull(world.ExportState().Inhabitants.Single(person => person.InhabitantId == childId).GuardianSearch);
        Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_needed" && item.Detail == childId);
    }

    [Fact]
    public async Task AcceptedGuardianCanTendToAnIllOlderDependent()
    {
        var state = await OrphanState(olderChild: true);
        var childId = state.Society.Society.Births.Single().ChildId;
        var adultId = state.Inhabitants.First(person => person.InhabitantId != childId).InhabitantId;
        using var proposing = PrivateWorldRuntime.Restore(state, actor =>
            new ParentProvider(actor == adultId ? "guardian_accept:" : "safe_idle"));
        for (var tick = 0; tick < 40 && proposing.Society.GetInhabitant(childId).PrimaryCaregiverId != adultId; tick++)
            await proposing.AdvanceOneTickAsync();

        Assert.Equal(adultId, proposing.Society.GetInhabitant(childId).PrimaryCaregiverId);
        var accepted = Assert.Single(proposing.Society.Relationships, edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.ProposerId == adultId && edge.TargetId == childId && edge.State == SocietyRelationshipState.Accepted);
        var stateToTend = proposing.ExportState();
        var child = stateToTend.Inhabitants.Single(person => person.InhabitantId == childId);
        var caregiverTile = stateToTend.Map.FootNeighbors(child.Position).First(point => stateToTend.Map.IsPassable(point) &&
            Math.Abs(point.X - child.Position.X) + Math.Abs(point.Y - child.Position.Y) == 1 &&
            !stateToTend.Inhabitants.Any(person => person.InhabitantId != accepted.ProposerId && person.Position == point));
        const int initialIllness = 9_000;
        stateToTend = stateToTend with
        {
            Inhabitants = stateToTend.Inhabitants.Select(person => person.InhabitantId switch
            {
                var id when id == accepted.ProposerId => person with
                {
                    Position = caregiverTile,
                    HungerBasisPoints = 4_500,
                    Survival = new SurvivalCondition(10_000, 0),
                    Project = null,
                    LastDecisionContext = null,
                    TravelCooldownTicks = 0,
                },
                var id when id == childId => person with
                {
                    HungerBasisPoints = 9_000,
                    Survival = new SurvivalCondition(9_000, 0),
                    Project = null,
                    LastDecisionContext = null,
                },
                _ => person,
            }).ToArray(),
        };
        using var society = SocietyWorldRuntime.Restore(stateToTend.Society);
        foreach (var resident in society.Checkpoint.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
                     person.Id != accepted.ProposerId && person.Id != childId).ToArray())
        {
            society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, resident.Id,
                SocietyDeathCause.Accident, checkpoint.WorldTick));
        }
        var societyState = society.ExportState();
        Assert.Equal(accepted.ProposerId, societyState.Society.GetInhabitant(childId).PrimaryCaregiverId);
        Assert.True(SocietyFixture.HasActivePrimaryCaregiver(societyState.Society, childId),
            $"Expected the retained guardian {accepted.ProposerId} to remain active after removing unrelated residents.");
        var projectInventory = societyState.Society.Inventory with
        {
            Lots = societyState.Society.Inventory.Lots.Where(lot => lot.ItemKind != "tool").ToArray(),
        };
        projectInventory = InventoryFixture.AddLot(projectInventory,
            $"ill-dependent-project-wood:{accepted.ProposerId}", "wood", accepted.ProposerId, 8);
        societyState = societyState with
        {
            Society = societyState.Society with { Inventory = projectInventory },
        };
        stateToTend = stateToTend with
        {
            Society = societyState,
            Inhabitants = stateToTend.Inhabitants
                .Where(person => person.InhabitantId == accepted.ProposerId || person.InhabitantId == childId).ToArray(),
        };
        var stateWithProject = stateToTend with
        {
            Inhabitants = stateToTend.Inhabitants
                .Select(person => person.InhabitantId == accepted.ProposerId
                ? person with { Project = null }
                : person).ToArray(),
        };
        Assert.Null(stateWithProject.Inhabitants.Single(person => person.InhabitantId == childId).GuardianSearch);

        var projectProvider = new ParentProvider("build:building:");
        using (var projectWorld = PrivateWorldRuntime.Restore(stateWithProject, actor =>
                   actor == accepted.ProposerId ? projectProvider : new ParentProvider("safe_idle")))
        {
            for (var tick = 0; tick < 5 && projectWorld.Inhabitants.Single(person => person.InhabitantId == accepted.ProposerId).Project is null; tick++)
                Assert.True((await projectWorld.AdvanceOneTickAsync()).Advanced);

            var selectedProject = Assert.IsType<SettlementProject>(
                projectWorld.Inhabitants.Single(person => person.InhabitantId == accepted.ProposerId).Project);
            Assert.StartsWith("build:building:", selectedProject.CandidateId, StringComparison.Ordinal);
            Assert.Contains(projectProvider.SeenCandidates, candidate => candidate.Id == selectedProject.CandidateId);
            Assert.Equal(selectedProject.CandidateId,
                Assert.Single(projectWorld.ExportState().Society.Cognition.Runtimes, item => item.InhabitantId == accepted.ProposerId)
                    .CurrentIntention?.CandidateId);
            projectWorld.Pause();
            stateWithProject = projectWorld.ExportState();
        }

        stateWithProject = stateWithProject with
        {
            Inhabitants = stateWithProject.Inhabitants.Select(person => person.InhabitantId == childId
                ? person with { Survival = new SurvivalCondition(9_000, initialIllness) }
                : person).ToArray(),
        };
        var careProvider = new ParentProvider("care:");
        using var working = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(stateWithProject)), actor =>
            actor == accepted.ProposerId ? careProvider : new ParentProvider("safe_idle"));
        working.Resume();
        Assert.True(SocietyFixture.HasActivePrimaryCaregiver(working.Society, childId),
            "The continuing-project scenario must start with an active accepted guardian.");

        Assert.True((await working.AdvanceOneTickAsync()).Advanced);
        Assert.True(SocietyFixture.HasActivePrimaryCaregiver(working.Society, childId),
            "Advancing the unrelated project must not clear its dependent's active guardian.");

        var progressed = working.Inhabitants.Single(person => person.InhabitantId == accepted.ProposerId).Project!;
        Assert.True(progressed.Stage is "acquiring" or "gathering" or "delivering" or "travelling" or "working" or "waiting" or "blocked",
            $"Expected the existing project to continue, but it is {progressed.Stage}.");
        Assert.Contains($"care:{childId}", careProvider.SelectedCandidateIds);
        Assert.DoesNotContain($"guardian_tend:{childId}", careProvider.SelectedCandidateIds);
        Assert.Contains(working.ExportState().Events, item => item.Kind == "child_cared_for" && item.Detail == childId);

        stateToTend = stateToTend with
        {
            Inhabitants = stateToTend.Inhabitants.Select(person => person.InhabitantId == childId
                ? person with { Survival = new SurvivalCondition(9_000, initialIllness) }
                : person).ToArray(),
        };
        var caregiverProvider = new ParentProvider("guardian_tend:");
        using var tending = PrivateWorldRuntime.Restore(stateToTend, actor =>
            actor == accepted.ProposerId ? caregiverProvider : new ParentProvider("safe_idle"));
        for (var tick = 0; tick < 8 && !tending.ExportState().Events.Any(item => item.Kind == "dependent_cared_for"); tick++)
            await tending.AdvanceOneTickAsync();

        var tended = tending.Inhabitants.Single(person => person.InhabitantId == childId);
        Assert.Contains(tending.ExportState().Events, item => item.Kind == "dependent_cared_for" && item.Detail == childId);
        Assert.True(tended.Survival!.IllnessBasisPoints <= initialIllness - 250,
            $"Expected accepted-guardian care to relieve illness by at least 250 points; actual illness {tended.Survival.IllnessBasisPoints}.");
        Assert.Equal(10_000, tended.Survival.WarmthBasisPoints);
    }

    [Fact]
    public async Task ProtectedCareCannotReplaceALivingCaregiverOrForgeAnOlderChildsConsent()
    {
        var state = await OrphanState();
        var checkpoint = state.Society.Society;
        var child = checkpoint.Births.Single().ChildId;
        var adults = checkpoint.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active && person.Id != child).ToArray();
        var accepted = SocietyFixture.AssumeInfantCare(checkpoint, adults[0].Id, child);
        Assert.NotNull(accepted.CreatedId);
        Assert.Null(SocietyFixture.AssumeInfantCare(accepted.Checkpoint, adults[1].Id, child).CreatedId);
        Assert.Null(SocietyFixture.AssumeInfantCare(accepted.Checkpoint, adults[0].Id, child).CreatedId);
        var older = await OrphanState(olderChild: true);
        Assert.Null(SocietyFixture.AssumeInfantCare(older.Society.Society, adults[0].Id, child).CreatedId);
    }

    [Fact]
    public async Task CaregiverCanWithdrawAndAnotherAdultCanVolunteerWithoutRewritingParentage()
    {
        var state = await OrphanState();
        var child = state.Society.Society.Births.Single().ChildId;
        var adult = state.Inhabitants.First(person => person.InhabitantId != child).InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == adult ? "guardian_accept:" : "safe_idle"));
        for (var tick = 0; tick < 40 && !world.Society.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                 edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted); tick++) await world.AdvanceOneTickAsync();
        using var leaving = PrivateWorldRuntime.Restore(world.ExportState(), actor => new ParentProvider(actor == adult ? "guardian_end:" : "guardian_accept:"));
        for (var tick = 0; tick < 40; tick++) await leaving.AdvanceOneTickAsync();
        Assert.Contains(leaving.Society.Relationships, edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.ProposerId == adult && edge.TargetId == child && edge.State == SocietyRelationshipState.Revoked);
        Assert.Contains(leaving.Society.Relationships, edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.ProposerId != adult && edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted);
        Assert.Equal(state.Society.Society.Births, leaving.Society.Births);
    }

    [Fact]
    public async Task CaregiverTelemetryExposesOutcomeButNotPrivateNames()
    {
        var directory = Directory.CreateTempSubdirectory("clankerworld-care-log-");
        try
        {
            var state = await OrphanState();
            state = state with
            {
                Society = state.Society with
                {
                    Society = state.Society.Society with
                    {
                        Inhabitants = state.Society.Society.Inhabitants.Select(person => person with { Name = "private-care-secret" }).ToArray(),
                    }
                }
            };
            using var world = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("guardian_accept:"));
            var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(1));
            presence.RecordAuthenticatedReconnect("owner");
            var logger = new RecordingLogger<PrivateWorldRuntimeService>();
            using var service = new PrivateWorldRuntimeService(world, new PrivateWorldStateFile(Path.Combine(directory.FullName, "world.json")), presence, logger);
            for (var tick = 0; tick < 40 && !logger.Messages.Any(message => message.Contains("event=guardian_assigned", StringComparison.Ordinal)); tick++)
                Assert.True(await service.TryAdvanceOnceAsync());
            Assert.Contains(logger.Messages, message => message.Contains("settlement_family", StringComparison.Ordinal) &&
                message.Contains("event=guardian_assigned", StringComparison.Ordinal));
            Assert.DoesNotContain(logger.Messages, message => message.Contains("private-care-secret", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<PrivateWorldRuntimeState> OrphanState(bool olderChild = false)
    {
        var state = await PreparedState();
        var first = state.Inhabitants[0].InhabitantId;
        var second = state.Inhabitants[1].InhabitantId;
        using var world = PrivateWorldRuntime.Restore(state, actor => new ParentProvider(actor == first ? "parent_propose:" : "parent_accept:"));
        for (var tick = 0; tick < 605; tick++) await world.AdvanceOneTickAsync();
        state = world.ExportState();
        var child = Assert.Single(state.Society.Society.Births).ChildId;
        using var society = SocietyWorldRuntime.Restore(state.Society);
        society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, first, SocietyDeathCause.Accident, checkpoint.WorldTick));
        society.Apply(checkpoint => SocietyFixture.Kill(checkpoint, second, SocietyDeathCause.Accident, checkpoint.WorldTick));
        var societyState = society.ExportState();
        if (olderChild)
        {
            societyState = societyState with
            {
                Society = societyState.Society with
                {
                    Inhabitants = societyState.Society.Inhabitants.Select(person => person.Id == child ? person with
                    {
                        BirthTick = societyState.Society.WorldTick - 3 * societyState.Society.Config.TicksPerWorldYear,
                        LastLifecycleYearChecked = 3,
                        AgeBand = SocietyAgeBand.Child,
                    } : person).ToArray(),
                }
            };
        }
        return state with
        {
            Society = societyState,
            Inhabitants = state.Inhabitants.Where(person => person.InhabitantId != first && person.InhabitantId != second)
                .Select(person => person with { LastDecisionContext = null, HungerBasisPoints = person.InhabitantId == child && !olderChild ? 2_000 : 9_000 }).ToArray(),
        };
    }
}
