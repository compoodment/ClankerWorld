using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

/// <summary>
/// Town membership and admission (#602). A Town's resident list is the only
/// record of membership: travel, households and Houses never change it, and
/// an adult joins another Town only through a passed admission they asked for
/// or personally accept. Every consent here comes from an accepted personal
/// model choice, as in play.
/// </summary>
public sealed class TownMembershipTests
{
    private const string First = "town:first";
    private const string Second = "town:second";
    private const string Alpha = "household:camp-alpha";
    private const string NewcomerName = "Rowan";
    private static readonly string[] Founders = [Founder(1), Founder(2), Founder(3), Founder(4)];

    [Fact]
    public async Task ThreeDaysInsideAnotherTownAndMadeUpCivicChoicesLeaveMembershipUnchanged()
    {
        var traveler = Founders[0];
        var state = WithTowns(ShortDays(Generated("membership-travel"), 30), Founders[..3], Founders[3..]);
        var second = Town(state, Second);
        // The Second Town's council has a law proposal posted; the traveler may read it but has no vote.
        second = second with
        {
            Governance = TownGovernanceRules.SubmitProposal(second.Governance!, Second, Founders[3], "law", null, "Share storm warnings.",
                "council:0", [Founders[3]], 0, state.WorldSystems!.Config.TicksPerDay),
        };
        state = state with { Towns = [Town(state, First), second] };
        var board = second.OriginSite!.Value;
        var start = WalkInside(state, second, board);
        var model = new ScriptedModel();
        // After reading, two choices no one offered: an invented civic act and an approval that does not exist.
        model.Scripts[traveler] = [Civic(Second, "visit"), Civic(Second, "read"),
            $"!civic|{Second}|join||", $"!civic|{Second}|accept_admission|{Second}:proposal:1|"];
        using var world = Reopen(Calm(At(state, start, traveler)), model);
        var firstResidents = world.Towns.Single(town => town.Id == First).ResidentIds.ToArray();
        var secondResidents = world.Towns.Single(town => town.Id == Second).ResidentIds.ToArray();
        var lastEvent = world.ExportState().Events[^1].EventId;
        var end = world.WorldTick + 3L * world.ExportState().WorldSystems!.Config.TicksPerDay;
        var refused = new List<string>();
        while (world.WorldTick < end)
        {
            var step = await world.AdvanceOneTickAsync();
            Assert.True(step.Advanced);
            refused.AddRange(step.Decisions.Where(item => item.InhabitantId == traveler && item.Admission.FellBack)
                .Select(item => item.Admission.Outcome));
            Assert.Equal(firstResidents, world.Towns.Single(town => town.Id == First).ResidentIds);
            Assert.Equal(secondResidents, world.Towns.Single(town => town.Id == Second).ResidentIds);
            Assert.Contains(world.Inhabitants.Single(person => person.InhabitantId == traveler).Position, second.BorderTiles);
        }

        // The traveler walked to the notice place and read it; asking was offered but is a separate choice.
        var events = world.ExportState().Events;
        Assert.True(world.ExportState().Map.FootDistance(board, world.Inhabitants.Single(person => person.InhabitantId == traveler).Position) <= 1);
        var acts = events.Where(item => item.Kind == "town_civic_action" &&
            item.Detail.StartsWith($"{Second}|{traveler}|", StringComparison.Ordinal)).Select(item => item.Detail.Split('|')[2]).ToArray();
        Assert.Equal("visit", acts[0]);
        Assert.Equal("read", acts[1]);
        Assert.All(acts.Skip(2), act => Assert.Equal("read", act));
        Assert.Contains(world.Towns.Single(town => town.Id == Second).Governance!.Knowledge, receipt => receipt.AgentId == traveler);
        Assert.DoesNotContain(model.ObservationsOf(traveler), observation => observation.Candidates.Any(candidate =>
            candidate.Id.StartsWith(Civic(Second, "yes"), StringComparison.Ordinal) || candidate.Id.StartsWith(Civic(Second, "no"), StringComparison.Ordinal)));
        Assert.Contains(model.ObservationsOf(traveler), observation =>
            observation.Candidates.Any(candidate => candidate.Id == Civic(Second, "admission") + "|"));
        Assert.Equal(2, model.InventionsUsed(traveler));
        Assert.Equal(["candidate_not_legal", "candidate_not_legal"], refused);
        Assert.DoesNotContain(events, item => item.EventId > lastEvent && (item.Kind.StartsWith("town_admission_", StringComparison.Ordinal) ||
            item.Kind is "town_resident_joined" or "town_resident_left" or "town_civic_action_rejected"));
        Assert.DoesNotContain(world.Towns.Single(town => town.Id == Second).Governance!.Proposals, proposal => proposal.Kind == "admission");
        Assert.Equal(secondResidents, world.Towns.Single(town => town.Id == Second).Governance!.Members);
        Assert.Contains(traveler, world.Towns.Single(town => town.Id == First).Governance!.Members);
        Assert.Equal("Town: resident of First Town · may sit and vote on its council and collect its Warehouse stock in person, housed or not",
            OwnerView(world, traveler));
        world.Validate();
    }

    // Founders joining the first Town and Add Agent outside every border are covered by FounderSetupTests.
    [Fact]
    public void AddAgentInsideTheSecondTownBorderJoinsOnlyThatTownAndItsCouncil()
    {
        var state = WithTowns(Generated("membership-add-agent"), Founders[..2], Founders[2..]);
        using var world = PrivateWorldRuntime.Restore(state, _ => new ScriptedModel());
        var second = world.Towns.Single(town => town.Id == Second);
        var site = second.BorderTiles.First(point => point != second.OriginSite &&
            world.Inhabitants.All(person => person.Position != point));
        var agent = NewAgentId();
        world.ValidateAgentPlacement(agent, site, expectedHouseholdId: null, expectedTownId: Second);
        Assert.Null(world.AddAgent(agent, site, null, Second));

        second = world.Towns.Single(town => town.Id == Second);
        Assert.Contains(agent, second.ResidentIds);
        Assert.Contains(agent, second.Governance!.Members);
        Assert.DoesNotContain(agent, world.Towns.Single(town => town.Id == First).ResidentIds);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "town_resident_joined" &&
            item.Detail == $"{Second}:{agent}:agent_joined:residents:3");
        Assert.All(world.Towns, town => Assert.Empty(town.Admissions ?? []));
        world.Validate();
    }

    [Fact]
    public async Task HouseholdAcceptanceWhileApprovalIsPendingGivesNoMembershipUntilTheCouncilVotes()
    {
        var applicant = NewAgentId();
        var state = WithTowns(Generated("membership-household-first", applicant), Founders, null);
        var model = new ScriptedModel();
        model.Scripts[applicant] = [Civic(First, "admission"), "household_ask:" + Alpha];
        foreach (var founder in Founders) model.Scripts[founder] = ["household_admit:" + applicant, Civic(First, "read")];
        var everyone = Founders.Append(applicant).ToArray();
        using var world = Reopen(Calm(At(state, Board(state, First), everyone)), model);
        Assert.Null(world.Society.GetInhabitant(applicant).HouseholdId);

        await AdvanceUntil(world, () => world.Society.GetInhabitant(applicant).HouseholdId == Alpha);
        var proposal = Assert.Single(world.Towns.Single(town => town.Id == First).Governance!.Proposals);
        Assert.Equal(("admission", applicant, applicant, "pending"), (proposal.Kind, proposal.AuthorId, proposal.SubjectId, proposal.Status));
        Assert.Empty(proposal.Votes);
        Assert.DoesNotContain(world.Towns, town => town.ResidentIds.Contains(applicant, StringComparer.Ordinal));
        Assert.Empty(world.Towns.Single(town => town.Id == First).Admissions ?? []);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "household_joined" && item.Detail == $"{applicant}:{Alpha}");
        Assert.Contains(model.ObservationsOf(applicant), observation => observation.Self?.TownMembershipNote?.Contains(
            "admission to First Town pending until world day", StringComparison.Ordinal) == true);
        Assert.StartsWith("Town: none · no council vote or Warehouse access", OwnerView(world, applicant), StringComparison.Ordinal);

        foreach (var founder in Founders) model.Scripts[founder] = [Civic(First, "read"), Civic(First, "yes")];
        using var voting = Reopen(Paused(world), model);
        await AdvanceUntil(voting, () => voting.Towns.Single(town => town.Id == First).ResidentIds.Contains(applicant, StringComparer.Ordinal));

        var first = voting.Towns.Single(town => town.Id == First);
        Assert.Equal("passed", first.Governance!.Proposals.Single(item => item.Id == proposal.Id).Status);
        var record = Assert.Single(first.Admissions!);
        Assert.Equal((proposal.Id, applicant, "admitted"), (record.ProposalId, record.SubjectId, record.Status));
        Assert.Null(record.PreviousTownId);
        Assert.Null(record.Reason);
        Assert.Equal([applicant], record.MemberIds!);
        Assert.Single(voting.ExportState().Events, item => item.Kind == "town_admission_accepted" &&
            item.Detail == $"{First}|{applicant}|none|1");
        Assert.Equal(Alpha, voting.Society.GetInhabitant(applicant).HouseholdId);
        Assert.Contains(applicant, first.Governance.Members);
        Assert.Equal("Town: resident of First Town · may sit and vote on its council and collect its Warehouse stock in person, housed or not",
            OwnerView(voting, applicant));
        voting.Validate();
        AssertRoundTrip(voting);
    }

    [Fact]
    public async Task SponsoredApprovalWaitsForTheNewcomerAndAcceptanceLapsesTheOtherTownsApproval()
    {
        var newcomer = NewAgentId();
        var model = new ScriptedModel();
        var directory = Directory.CreateTempSubdirectory("clankerworld-town-admission-");
        var logger = new RecordingLogger<PrivateWorldRuntimeService>();
        var services = new List<PrivateWorldRuntimeService>();
        try
        {
            var stepper = ServiceSteps(Path.Combine(directory.FullName, "world.json"), logger, services);
            var (approved, firstProposal, secondProposal) = await TwoApprovalsAsync("membership-sponsored", newcomer, model, stepper);

            // Both councils approved; neither approval changed membership.
            Assert.DoesNotContain(approved.Towns!, town => town.ResidentIds.Contains(newcomer, StringComparer.Ordinal));
            AssertApproval(Town(approved, First), firstProposal, newcomer);
            AssertApproval(Town(approved, Second), secondProposal, newcomer);
            Assert.Equal([$"{Second}|{newcomer}|{secondProposal}", $"{First}|{newcomer}|{firstProposal}"],
                approved.Events.Where(item => item.Kind == "town_admission_approved").Select(item => item.Detail));
            var secondMembers = Town(approved, Second).Governance!.Members.ToArray();

            model.Scripts[newcomer] = [Civic(First, "accept_admission")];
            using var world = Reopen(approved, model);
            var step = stepper(world);
            await AdvanceUntil(world, () => world.Towns.Single(town => town.Id == First).ResidentIds.Contains(newcomer, StringComparer.Ordinal), step: step);

            var first = world.Towns.Single(town => town.Id == First);
            var second = world.Towns.Single(town => town.Id == Second);
            var admitted = Assert.Single(first.Admissions!);
            Assert.Equal((firstProposal, "admitted"), (admitted.ProposalId, admitted.Status));
            Assert.Null(admitted.PreviousTownId);
            Assert.Equal([newcomer], admitted.MemberIds!);
            var lapsed = Assert.Single(second.Admissions!);
            Assert.Equal((secondProposal, "lapsed", "joined_elsewhere"), (lapsed.ProposalId, lapsed.Status, lapsed.Reason));
            Assert.Null(lapsed.PreviousTownId);
            Assert.Null(lapsed.MemberIds);
            Assert.DoesNotContain(newcomer, second.ResidentIds);
            Assert.Contains(newcomer, first.Governance!.Members);
            Assert.Equal(secondMembers, second.Governance!.Members);
            var events = world.ExportState().Events;
            Assert.Single(events, item => item.Kind == "town_admission_accepted" && item.Detail == $"{First}|{newcomer}|none|1");
            Assert.Single(events, item => item.Kind == "town_admission_lapsed" &&
                item.Detail == $"{Second}|{newcomer}|{secondProposal}|joined_elsewhere");
            Assert.Contains(model.ObservationsOf(newcomer), observation => observation.Candidates.Any(candidate =>
                candidate.Id.StartsWith(Civic(First, "accept_admission"), StringComparison.Ordinal) &&
                candidate.Description.Contains("This gives no House or household place.", StringComparison.Ordinal)));

            // The admission log names Towns and counts only, for every outcome.
            var admissionLines = logger.Messages.Where(message => message.StartsWith("town_admission ", StringComparison.Ordinal)).ToArray();
            Assert.Contains(admissionLines, line => line.Contains($"town={Second} outcome=awaiting_acceptance previous_town=none members=0 residents=2", StringComparison.Ordinal));
            Assert.Contains(admissionLines, line => line.Contains($"town={First} outcome=awaiting_acceptance previous_town=none members=0 residents=2", StringComparison.Ordinal));
            Assert.Contains(admissionLines, line => line.Contains($"town={First} outcome=admitted previous_town=none members=1 residents=3", StringComparison.Ordinal));
            Assert.Contains(admissionLines, line => line.Contains($"town={Second} outcome=lapsed:joined_elsewhere previous_town=none members=0 residents=2", StringComparison.Ordinal));
            var names = world.Society.Inhabitants.Select(person => person.Name).Append(NewcomerName).ToArray();
            Assert.All(admissionLines, line =>
            {
                Assert.DoesNotContain(names, name => line.Contains(name, StringComparison.Ordinal));
                Assert.DoesNotContain(newcomer, line, StringComparison.Ordinal);
                Assert.DoesNotContain("Admit ", line, StringComparison.Ordinal);
            });
            world.Validate();
        }
        finally
        {
            foreach (var service in services) service.Dispose();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ApprovedAdmissionsReloadReplayAndApplyOnceOnlyFromThePersonalModel()
    {
        var newcomer = NewAgentId();
        var (approved, firstProposal, secondProposal) = await TwoApprovalsAsync("membership-replay", newcomer, new ScriptedModel(), DirectSteps);
        var bytes = PrivateWorldRuntimeCodec.Encode(approved);
        using (var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes)))
            Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

        // Built-in rules choosing the offered acceptance give no consent.
        var builtIn = new ScriptedModel(DecisionProviderKind.Deterministic);
        builtIn.Scripts[newcomer] = [Civic(First, "accept_admission")];
        using (var refusing = Reopen(approved, builtIn))
        {
            var chosen = new List<CognitionAdmissionResult>();
            for (var tick = 0; tick < 8; tick++)
            {
                var step = await refusing.AdvanceOneTickAsync();
                Assert.True(step.Advanced);
                chosen.AddRange(step.Decisions.Where(item => item.InhabitantId == newcomer).Select(item => item.Admission));
            }
            Assert.Contains(chosen, admission => admission is { Accepted: true, FellBack: false } &&
                admission.Intention?.Provider == DecisionProviderKind.Deterministic &&
                admission.Intention.CandidateId.StartsWith(Civic(First, "accept_admission"), StringComparison.Ordinal));
            Assert.DoesNotContain(refusing.Towns, town => town.ResidentIds.Contains(newcomer, StringComparer.Ordinal));
            Assert.All(refusing.Towns, town => Assert.Equal("approved", Assert.Single(town.Admissions!).Status));
            Assert.DoesNotContain(refusing.ExportState().Events, item => item.Kind == "town_admission_accepted");
        }

        ScriptedModel Accepting()
        {
            var accepting = new ScriptedModel();
            // Repeated acceptances after joining were not offered and change nothing.
            accepting.Scripts[newcomer] = [Civic(First, "accept_admission"),
                $"!civic|{First}|accept_admission|{firstProposal}|", $"!civic|{Second}|accept_admission|{secondProposal}|"];
            return accepting;
        }
        using var world = Reopen(approved, Accepting());
        using var replay = Reopen(approved, Accepting());
        var refused = new List<string>();
        for (var tick = 0; tick < 12; tick++)
        {
            var step = await world.AdvanceOneTickAsync();
            Assert.True(step.Advanced);
            refused.AddRange(step.Decisions.Where(item => item.InhabitantId == newcomer && item.Admission.FellBack)
                .Select(item => item.Admission.Outcome));
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        Assert.Contains(newcomer, world.Towns.Single(town => town.Id == First).ResidentIds);
        Assert.Contains("candidate_not_legal", refused);

        var saved = PrivateWorldRuntimeCodec.Encode(Paused(world));
        using var reloaded = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => Accepting());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(reloaded.ExportState()));
        reloaded.Resume();
        for (var tick = 0; tick < 6; tick++) Assert.True((await reloaded.AdvanceOneTickAsync()).Advanced);
        var events = reloaded.ExportState().Events;
        Assert.Single(events, item => item.Kind == "town_admission_accepted");
        Assert.Single(events, item => item.Kind == "town_admission_lapsed");
        Assert.Equal("admitted", Assert.Single(reloaded.Towns.Single(town => town.Id == First).Admissions!).Status);
        Assert.Equal("lapsed", Assert.Single(reloaded.Towns.Single(town => town.Id == Second).Admissions!).Status);
        Assert.Single(reloaded.Towns, town => town.ResidentIds.Contains(newcomer, StringComparer.Ordinal));
        reloaded.Validate();
    }

    [Fact]
    public async Task CaregiverMovingTownsTakesDependentChildrenInTheSameStepAndBothCouncilsFollow()
    {
        var caregiver = Founders[0];
        var (state, children) = WithChildren(Generated("membership-care-group"), caregiver, Founders[1], 2);
        state = WithTowns(state, [Founders[0], Founders[1], .. children], Founders[2..]);
        var model = new ScriptedModel();
        model.Scripts[caregiver] = [Civic(Second, "admission"), Civic(Second, "read")];
        foreach (var voter in Founders[2..]) model.Scripts[voter] = [Civic(Second, "read"), Civic(Second, "yes")];
        var positions = state.Inhabitants.ToDictionary(person => person.InhabitantId, person => person.Position, StringComparer.Ordinal);
        using var world = Reopen(Calm(At(state, Board(state, Second), caregiver, Founders[2], Founders[3])), model);
        var firstRevision = world.Towns.Single(town => town.Id == First).Governance!.Revision;

        await AdvanceUntil(world, () => world.Towns.Single(town => town.Id == Second).ResidentIds.Contains(caregiver, StringComparer.Ordinal));

        var group = children.Append(caregiver).Order(StringComparer.Ordinal).ToArray();
        var first = world.Towns.Single(town => town.Id == First);
        var second = world.Towns.Single(town => town.Id == Second);
        Assert.Equal([Founders[1]], first.ResidentIds);
        Assert.Equal(group.Concat(Founders[2..]).Order(StringComparer.Ordinal), second.ResidentIds);
        var record = Assert.Single(second.Admissions!);
        Assert.Equal(("admitted", First), (record.Status, record.PreviousTownId));
        Assert.Equal(group, record.MemberIds!);
        var events = world.ExportState().Events;
        var accepted = Assert.Single(events, item => item.Kind == "town_admission_accepted");
        Assert.Equal($"{Second}|{caregiver}|{First}|3", accepted.Detail);
        Assert.DoesNotContain(events, item => item.Kind is "town_resident_joined" or "town_resident_left" &&
            item.WorldTick >= accepted.WorldTick);
        Assert.Equal([Founders[1]], first.Governance!.Members);
        Assert.True(first.Governance.Revision > firstRevision);
        Assert.Equal(new[] { caregiver, Founders[2], Founders[3] }.Order(StringComparer.Ordinal), second.Governance!.Members);
        foreach (var child in children)
        {
            var person = world.Society.GetInhabitant(child);
            Assert.Equal((Alpha, caregiver), (person.HouseholdId, person.PrimaryCaregiverId));
            Assert.Equal(positions[child], world.Inhabitants.Single(item => item.InhabitantId == child).Position);
            Assert.Equal("Town: resident of Second Town with their primary caregiver · council rights begin at adulthood", OwnerView(world, child));
        }
        Assert.Equal(Alpha, world.Society.GetInhabitant(caregiver).HouseholdId);
        Assert.Contains(model.ObservationsOf(caregiver), observation => observation.Candidates.Any(candidate =>
            candidate.Id == Civic(Second, "admission") + "|" &&
            candidate.Description.Contains("If approved you leave First Town", StringComparison.Ordinal) &&
            candidate.Description.Contains("Your dependent children would join with you.", StringComparison.Ordinal)));
        world.Validate();
        AssertRoundTrip(world);
    }

    [Fact]
    public async Task NewbornJoinsThePrimaryCaregiversTownWhenParentsLiveInDifferentTowns()
    {
        var caregiver = Founders[0];
        var otherParent = Founders[1];
        var state = Generated("membership-newborn");
        var society = Partners(state.Society.Society, caregiver, otherParent);
        state = state with
        {
            Society = state.Society with { Society = society },
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == caregiver || person.InhabitantId == otherParent
                ? person with { Skills = [new(SettlementSkillKind.Building, society.WorldTick)] } : person).ToArray(),
        };
        state = WithTowns(state, Founders[1..], [caregiver]);
        var model = new ScriptedModel();
        model.Scripts[caregiver] = ["parent_propose:"];
        model.Scripts[otherParent] = [$"parent_accept:{caregiver}:initiator"];
        using var world = Reopen(Calm(state), model);

        await AdvanceUntil(world, () => world.Society.Births.Count == 1, 700);

        var child = Assert.Single(world.Society.Births).ChildId;
        Assert.Equal(caregiver, world.Society.GetInhabitant(child).PrimaryCaregiverId);
        Assert.Equal([caregiver, child], world.Towns.Single(town => town.Id == Second).ResidentIds.Order(StringComparer.Ordinal));
        Assert.Equal(Founders[1..], world.Towns.Single(town => town.Id == First).ResidentIds);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "town_resident_joined" &&
            item.Detail == $"{Second}:{child}:child_joined:residents:2");
        foreach (var parent in new[] { caregiver, otherParent })
            Assert.Single(world.Society.Relationships, edge => edge.Type == SocietyRelationshipType.BiologicalParentage &&
                edge.ProposerId == parent && edge.TargetId == child);
        // No admission vote, and the child has no council seat.
        Assert.All(world.Towns, town => Assert.Empty(town.Admissions ?? []));
        Assert.Equal([caregiver], world.Towns.Single(town => town.Id == Second).Governance!.Members);
        world.Validate();
    }

    [Fact]
    public async Task ChildReachingAdulthoodGainsCouncilRightsInTheTownTheyAlreadyBelongTo()
    {
        var (state, children) = WithChildren(Generated("membership-coming-of-age"), Founders[0], Founders[1], 1);
        var child = children[0];
        var society = state.Society.Society;
        var config = society.Config;
        // Two ticks before the adult start day.
        var birth = society.LifeTickAt(society.WorldTick) - config.DayLifecycle!.AdultStartDay * config.TicksPerLifecycleAge + 2;
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
                        AgeBand = SocietyAgeBand.Child,
                        LastLifecycleYearChecked = config.DayLifecycle.AdultStartDay - 1,
                    } : person).ToArray(),
                },
            },
        };
        state = WithTowns(state, [.. Founders, child], null);
        var model = new ScriptedModel();
        using var world = Reopen(Calm(state), model);
        var residents = world.Towns.Single(town => town.Id == First).ResidentIds.ToArray();
        Assert.DoesNotContain(child, world.Towns.Single(town => town.Id == First).Governance!.Members);
        Assert.Equal("Town: resident of First Town with their primary caregiver · council rights begin at adulthood", OwnerView(world, child));
        var lastEvent = world.ExportState().Events[^1].EventId;

        await AdvanceUntil(world, () => model.ObservationsOf(child).Any(item => item.Self?.LifeStage == nameof(SocietyAgeBand.Adult)), 20);

        Assert.Equal(SocietyAgeBand.Adult, world.Society.GetInhabitant(child).AgeBand);
        var first = world.Towns.Single(town => town.Id == First);
        Assert.Equal(residents, first.ResidentIds);
        Assert.Contains(child, first.Governance!.Members);
        Assert.Contains(first.Governance.Notices, notice => notice.Kind == "council" && notice.SubjectId == "council:" + first.Governance.Revision);
        Assert.DoesNotContain(world.ExportState().Events, item => item.EventId > lastEvent &&
            (item.Kind.StartsWith("town_admission_", StringComparison.Ordinal) || item.Kind is "town_resident_joined" or "town_resident_left"));
        const string adult = "Town: resident of First Town · may sit and vote on its council and collect its Warehouse stock in person, housed or not";
        Assert.Equal(adult, OwnerView(world, child));
        // As a child they were told why they had no council choices; as an adult the same membership gives them.
        foreach (var observation in model.ObservationsOf(child))
        {
            var grown = observation.Self?.LifeStage == nameof(SocietyAgeBand.Adult);
            Assert.Equal(grown ? adult : "Town: resident of First Town with their primary caregiver · council rights begin at adulthood",
                observation.Self?.TownMembershipNote);
            Assert.Equal(grown, observation.Candidates.Any(candidate => candidate.Id == Civic(First, "register") + "|"));
            Assert.Equal(grown, observation.Candidates.Any(candidate => candidate.Id == Civic(First, "propose") + "|"));
        }
        world.Validate();
    }

    [Fact]
    public async Task HomelessResidentsCollectTheirTownsWarehouseStockWhereverItStandsAndLoseItAfterMoving()
    {
        var actor = Founders[3];
        var state = WithTowns(Generated("membership-warehouse"), Founders[..2], Founders[2..]);
        var warehouse = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == "first-town-warehouse");
        var stocked = state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == warehouse.InstanceId)
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        state = WithInventory(state, state.Society.Society.Inventory with
        {
            Lots = state.Society.Society.Inventory.Lots.Where(lot => !stocked.Contains(lot.Id)).ToArray(),
            Reservations = state.Society.Society.Inventory.Reservations.Where(item => !stocked.Contains(item.LotId)).ToArray(),
        });
        using (var setup = PrivateWorldRuntime.Restore(state, _ => new ScriptedModel()))
        {
            // The Warehouse now belongs to the Second Town but stands outside its border.
            var reassigned = setup.ReassignBuilding(warehouse.InstanceId, warehouse.TownId, warehouse.HouseholdId,
                targetTownId: Second, targetHouseholdId: null);
            Assert.True(reassigned.Applied, reassigned.Failure);
            Assert.True(setup.DisplaceAdult(actor));
            state = Paused(setup);
        }
        Assert.Null(state.Society.Society.GetInhabitant(actor).HouseholdId);
        Assert.DoesNotContain(warehouse.Position, Town(state, Second).BorderTiles);
        var workshop = state.WorldContent!.Buildings.Single(item => item.LocalId == "workshop");
        var site = FreeTile(state, state.Map.Tiles.Select(tile => tile.Position));
        var project = new SettlementProject(TownConstructionCandidateIds.Building(workshop.CanonicalId, site),
            workshop.DisplayName, state.Society.Society.WorldTick, "acquiring", LastTransitionTick: state.Society.Society.WorldTick);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory, "second-town-wood", "wood", Second, 8,
            storageBuildingId: warehouse.InstanceId));
        state = WithProject(At(state, warehouse.Position, actor), actor, project);
        var model = new ScriptedModel();

        using (var collecting = Reopen(state, model))
        {
            Assert.Equal("Town: resident of Second Town · may sit and vote on its council and collect its Warehouse stock in person, housed or not",
                OwnerView(collecting, actor));
            await AdvanceUntil(collecting, () => collecting.ExportState().Events.Any(item => item.Kind == "town_resource_collected" &&
                item.Detail.StartsWith(actor + ":", StringComparison.Ordinal)), 5);
            state = Paused(collecting);
        }
        var carried = Assert.Single(state.Society.Society.Inventory.Lots, lot => lot.OwnerId == actor && lot.ItemKind == "wood");
        Assert.Null(carried.StorageBuildingId);
        Assert.Equal(4, state.Society.Society.Inventory.GetLot("second-town-wood").Quantity);

        // Moving to the First Town: the First Town's council admits them.
        model.Scripts[actor] = [Civic(First, "admission")];
        foreach (var voter in Founders[..2]) model.Scripts[voter] = [Civic(First, "read"), Civic(First, "yes")];
        state = WithProject(At(state, Board(state, First), actor, Founders[0], Founders[1]), actor, null);
        using (var moving = Reopen(state, model))
        {
            await AdvanceUntil(moving, () => moving.Towns.Single(town => town.Id == First).ResidentIds.Contains(actor, StringComparer.Ordinal));
            state = Paused(moving);
        }
        Assert.Equal([Founders[2]], Town(state, Second).ResidentIds);

        model.Scripts.Clear();
        var collectedBefore = state.Events.Count(item => item.Kind == "town_resource_collected");
        state = WithProject(At(state, warehouse.Position, actor), actor, project with { LastTransitionTick = state.Society.Society.WorldTick });
        using var moved = Reopen(state, model);
        Assert.Equal("Town: resident of First Town · may sit and vote on its council, housed or not; it has no Warehouse yet", OwnerView(moved, actor));
        for (var tick = 0; tick < 3; tick++) Assert.True((await moved.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(collectedBefore, moved.ExportState().Events.Count(item => item.Kind == "town_resource_collected"));
        Assert.Equal(4, moved.Society.Inventory.GetLot("second-town-wood").Quantity);
        Assert.Equal(Second, moved.Society.Inventory.GetLot("second-town-wood").OwnerId);
        Assert.Equal(actor, moved.Society.Inventory.GetLot(carried.Id).OwnerId);
        Assert.True(moved.Society.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == "wood").Sum(lot => lot.Quantity) >= 4);
        moved.Validate();
    }

    [Theory]
    [InlineData("missing_record")]
    [InlineData("not_admission")]
    [InlineData("duplicate_proposal")]
    [InlineData("unsorted_members")]
    [InlineData("missing_members")]
    [InlineData("approved_resident")]
    [InlineData("unknown_lapse_reason")]
    public void LoadingRefusesTamperedAdmissionRecords(string damage)
    {
        var (state, newcomer, law) = CheckpointWithAdmission();
        AssertRoundTrips(state);
        var document = JsonNode.Parse(PrivateWorldRuntimeCodec.Encode(state))!;
        var town = document["state"]!["towns"]![0]!;
        var records = town["admissions"]!.AsArray();
        var record = records[0]!;
        switch (damage)
        {
            case "missing_record": town.AsObject().Remove("admissions"); break;
            case "not_admission": record["proposalId"] = law; break;
            case "duplicate_proposal": records.Add(record.DeepClone()); break;
            case "unsorted_members": record["memberIds"] = new JsonArray(Founders[0], newcomer); break;
            case "missing_members": record.AsObject().Remove("memberIds"); break;
            case "approved_resident":
                record["status"] = "approved";
                record.AsObject().Remove("memberIds");
                break;
            default:
                record["status"] = "lapsed";
                record.AsObject().Remove("memberIds");
                record["reason"] = "moved_away";
                break;
        }
        Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public void KnownLapseReasonForAnExistingResidentLoads()
    {
        var (state, _, _) = CheckpointWithAdmission();
        var town = state.Towns![0];
        var record = town.Admissions![0] with { Status = "lapsed", MemberIds = null, Reason = "already_resident" };
        AssertRoundTrips(state with { Towns = [town with { Admissions = [record] }] });
    }

    [Fact]
    public async Task AnAdultWithNoTownOutsideEveryBorderCanWalkToTheNoticePlaceAndAsk()
    {
        var newcomer = NewAgentId();
        var state = WithTowns(Generated("membership-outside", newcomer), Founders, null);
        var town = Town(state, First);
        var board = town.OriginSite!.Value;
        // Add Agent inside a border makes a resident, so an adult with no Town starts outside every border.
        var taken = Taken(state);
        var start = state.Map.Tiles.Select(tile => tile.Position)
            .Where(point => !town.BorderTiles.Contains(point) && state.Map.IsBuildable(point) && !taken.Contains(point) &&
                state.Map.IsReachableOnFoot(point, board))
            .OrderBy(point => state.Map.FootDistance(point, board)).ThenBy(point => point.Y).ThenBy(point => point.X).First();
        var model = new ScriptedModel();
        model.Scripts[newcomer] = [Civic(First, "visit"), Civic(First, "admission")];
        using var world = Reopen(Calm(At(state, start, newcomer)), model);

        await AdvanceUntil(world, () => world.Towns.Single(item => item.Id == First).Governance!.Proposals
            .Any(proposal => proposal is { Kind: "admission", Status: "pending" } && proposal.SubjectId == newcomer), 80);

        Assert.Contains(model.ObservationsOf(newcomer)[0].Candidates, candidate => candidate.Id == Civic(First, "visit") + "|");
        Assert.Equal("visit", world.ExportState().Events.First(item => item.Kind == "town_civic_action" &&
            item.Detail.StartsWith($"{First}|{newcomer}|", StringComparison.Ordinal)).Detail.Split('|')[2]);
        // Walking in registered nothing; only a council vote can admit them.
        Assert.DoesNotContain(world.Towns, item => item.ResidentIds.Contains(newcomer, StringComparer.Ordinal));
        world.Validate();
    }

    [Fact]
    public async Task RefusedRequestWaitsADayAndTheApplicantLearnsOnlyFromTheResultNotice()
    {
        var applicant = NewAgentId();
        var state = WithTowns(ShortDays(Generated("membership-refused", applicant), 30), Founders, null);
        var model = new ScriptedModel();
        model.Scripts[applicant] = [Civic(First, "read"), Civic(First, "admission")];
        foreach (var founder in Founders) model.Scripts[founder] = [Civic(First, "read"), Civic(First, "no")];
        using var world = Reopen(Calm(At(state, Board(state, First), [.. Founders, applicant])), model);
        var day = world.ExportState().WorldSystems!.Config.TicksPerDay;

        await AdvanceUntil(world, () => world.Towns.Single(town => town.Id == First).Governance!.Proposals.Count == 2, day + 60);

        var proposals = world.Towns.Single(town => town.Id == First).Governance!.Proposals;
        var refused = proposals[0];
        Assert.Equal("rejected", refused.Status);
        var settled = refused.SettledTick!.Value;
        Assert.True(proposals[1].OpenedTick >= settled + day);
        var observations = model.ObservationsOf(applicant);
        const string admission = "civic|town:first|admission||";
        const string refusal = "First Town's council did not approve admission on world day 1";
        Assert.DoesNotContain(observations, observation => observation.WorldTick >= settled && observation.WorldTick < settled + day &&
            observation.Candidates.Any(candidate => candidate.Id == admission));
        Assert.Contains(observations, observation => observation.WorldTick >= settled + day &&
            observation.Candidates.Any(candidate => candidate.Id == admission));
        // Told of the refusal only after reading the posted result.
        var unread = Assert.Single(observations, observation => observation.WorldTick >= settled &&
            observation.Candidates.Any(candidate => candidate.Id == Civic(First, "read") + "|"));
        Assert.DoesNotContain("did not approve", unread.Self?.TownMembershipNote ?? "", StringComparison.Ordinal);
        Assert.Contains(observations, observation => observation.WorldTick > unread.WorldTick &&
            observation.Self?.TownMembershipNote?.EndsWith(refusal, StringComparison.Ordinal) == true);
        Assert.DoesNotContain(world.Towns, town => town.ResidentIds.Contains(applicant, StringComparer.Ordinal));
        Assert.All(world.Towns, town => Assert.Empty(town.Admissions ?? []));
        world.Validate();
    }

    [Fact]
    public void MembershipTextDescribesResidenceRightsAndOnlyTheAdmissionNoticesTheReaderKnows()
    {
        const int day = 360;
        var society = Generated("membership-text").Society.Society;
        var (resident, child, outsider, voter) = (Founders[0], Founders[1], Founders[2], Founders[3]);
        society = society with
        {
            WorldTick = 5,
            Inhabitants = society.Inhabitants.Select(person => person.Id == child
                ? person with { AgeBand = SocietyAgeBand.Child, PrimaryCaregiverId = resident } : person).ToArray(),
        };
        var first = new TownRuntimeState(First, "First Town", "founded", 0, [child, resident], [], [new(0, 0)], new(0, 0),
            TownGovernanceState.Create([resident]));
        var council = TownGovernanceState.Create([voter]);
        var second = new TownRuntimeState(Second, "Second Town", "founded", 0, [voter], [], [new(5, 5)], new(5, 5), council);
        string? Describe(string id, TownRuntimeState? secondTown = null, IReadOnlySet<string>? warehouses = null,
            Func<TownRuntimeState, string, string, bool>? knows = null) =>
            TownMembershipText.Describe([first, secondTown ?? second], society, id, day,
                warehouses ?? new HashSet<string>([First], StringComparer.Ordinal), knows);
        const string none = "Town: none · no council vote or Warehouse access; a Town council must approve admission at its notice place";

        Assert.Equal(none, Describe(outsider));
        Assert.Equal("Town: resident of First Town · may sit and vote on its council and collect its Warehouse stock in person, housed or not",
            Describe(resident));
        Assert.Equal("Town: resident of First Town · may sit and vote on its council, housed or not; it has no Warehouse yet",
            Describe(resident, warehouses: new HashSet<string>(StringComparer.Ordinal)));
        Assert.Equal("Town: resident of Second Town · may vote in its council elections, housed or not; it has no Warehouse yet",
            Describe(voter, second with { Governance = council with { Form = "representative" } }));
        Assert.Equal("Town: resident of First Town with their primary caregiver · council rights begin at adulthood", Describe(child));
        // A guardian from another Town does not move the child, so the line does not claim they share it.
        var guardedElsewhere = society with
        {
            Inhabitants = society.Inhabitants.Select(person => person.Id == child ? person with { PrimaryCaregiverId = voter } : person).ToArray(),
        };
        Assert.Equal("Town: resident of First Town · council rights begin at adulthood",
            TownMembershipText.Describe([first, second], guardedElsewhere, child, day, new HashSet<string>([First], StringComparer.Ordinal)));
        Assert.Null(Describe("agent:missing"));
        var homelessChild = society with
        {
            Inhabitants = society.Inhabitants.Select(person => person.Id == outsider ? person with { AgeBand = SocietyAgeBand.Child } : person).ToArray(),
        };
        Assert.Equal("Town: none · follows their primary caregiver's Town",
            TownMembershipText.Describe([first, second], homelessChild, outsider, day, new HashSet<string>(StringComparer.Ordinal)));

        // Pending, approved, refused and cancelled requests, each only once its notice is known.
        var pending = TownGovernanceRules.SubmitProposal(council, Second, outsider, "admission", outsider, "Admit me.", "council:0", [voter], 0, day);
        var proposal = pending.Proposals[0].Id;
        Assert.Equal(none + " · admission to Second Town pending until world day 2; grants nothing yet",
            Describe(outsider, second with { Governance = pending }));
        Assert.Equal(none, Describe(outsider, second with { Governance = pending }, knows: (_, _, _) => false));

        var passed = TownGovernanceRules.SubmitProposal(council, Second, voter, "admission", outsider, "Admit them.", "council:0", [voter], 0, day);
        passed = TownGovernanceRules.VoteProposal(passed, proposal, voter, true, 1);
        var approved = second with
        {
            Governance = passed,
            Admissions = [new TownAdmissionRecord(proposal, outsider, "approved", 1)],
        };
        Assert.Equal(none + " · Second Town's council approved admission; not accepted yet", Describe(outsider, approved));
        Assert.Equal(none, Describe(outsider, approved, knows: (_, kind, _) => kind != "result"));

        var rejected = TownGovernanceRules.VoteProposal(pending, proposal, voter, false, 1);
        Assert.Equal("rejected", rejected.Proposals[0].Status);
        Assert.Equal(none + " · Second Town's council did not approve admission on world day 1",
            Describe(outsider, second with { Governance = rejected }));
        Assert.Equal(none, Describe(outsider, second with { Governance = rejected }, knows: (_, kind, _) => kind != "result"));
        var nextDay = society with { WorldTick = 1 + day };
        Assert.Equal(none, TownMembershipText.Describe([first, second with { Governance = rejected }], nextDay, outsider, day,
            new HashSet<string>(StringComparer.Ordinal)));

        var cancelled = TownGovernanceRules.Advance(pending, Second, "membership-text", [voter, resident], 2, day);
        Assert.Equal("cancelled", cancelled.Proposals[0].Status);
        Assert.Equal(none + " · admission request to Second Town closed when its council changed; ask again",
            Describe(outsider, second with { Governance = cancelled }));
        Assert.Equal(none, Describe(outsider, second with { Governance = cancelled },
            knows: (_, kind, subject) => kind != "council" || subject != "council:1"));

        var longName = new string('A', 200);
        var text = TownMembershipText.Describe([first with { Name = longName }, second], society, resident, day,
            new HashSet<string>(StringComparer.Ordinal))!;
        Assert.Equal(TownMembershipText.MaximumLength, text.Length);
        Assert.StartsWith("Town: resident of " + longName + " · may sit", text, StringComparison.Ordinal);
    }

    private static void AssertApproval(TownRuntimeState town, string proposal, string newcomer)
    {
        var record = Assert.Single(town.Admissions!);
        Assert.Equal((proposal, newcomer, "approved"), (record.ProposalId, record.SubjectId, record.Status));
        Assert.Null(record.PreviousTownId);
        Assert.Null(record.MemberIds);
        Assert.Null(record.Reason);
        Assert.Equal("passed", town.Governance!.Proposals.Single(item => item.Id == proposal).Status);
    }

    /// <summary>
    /// A resident of each Town asks their council to admit the unaffiliated
    /// newcomer, the councils vote yes through ordinary turns and the newcomer
    /// reads each result at the notice place without accepting. The Second
    /// Town approves first. Returns the paused state and both proposal IDs.
    /// </summary>
    private static async Task<(PrivateWorldRuntimeState State, string FirstProposal, string SecondProposal)> TwoApprovalsAsync(
        string seed, string newcomer, ScriptedModel model, Func<PrivateWorldRuntime, Func<Task>> stepper)
    {
        var state = WithTowns(Generated(seed, newcomer), Founders[..2], Founders[2..]);
        string? secondProposal = null;
        foreach (var (townId, sponsor, voter) in new[] { (Second, Founders[2], Founders[3]), (First, Founders[0], Founders[1]) })
        {
            model.Scripts.Clear();
            model.Scripts[sponsor] = [$"civic|{townId}|request_admission|{newcomer}|", Civic(townId, "read"), Civic(townId, "yes")];
            model.Scripts[voter] = [Civic(townId, "read"), Civic(townId, "yes")];
            model.Scripts[newcomer] = [Civic(townId, "read")];
            state = At(state, Board(state, townId), sponsor, voter, newcomer);
            using var world = Reopen(secondProposal is null ? Calm(state) : state, model);
            var accept = Civic(townId, "accept_admission");
            await AdvanceUntil(world, () => model.ObservationsOf(newcomer).Any(observation =>
                observation.Candidates.Any(candidate => candidate.Id.StartsWith(accept, StringComparison.Ordinal))), step: stepper(world));
            state = Paused(world);

            // The newcomer learns of the approval only from its posted result.
            var town = Town(state, townId);
            var record = Assert.Single(town.Admissions!);
            var approvedTick = state.Events.Single(item => item.Kind == "town_admission_approved" &&
                item.Detail == $"{townId}|{newcomer}|{record.ProposalId}").WorldTick;
            var observed = model.ObservationsOf(newcomer);
            var unread = observed.Where(observation => observation.WorldTick >= approvedTick &&
                observation.Candidates.Any(candidate => candidate.Id == Civic(townId, "read") + "|")).ToArray();
            Assert.NotEmpty(unread);
            Assert.All(unread, observation =>
            {
                Assert.DoesNotContain(observation.Candidates, candidate => candidate.Id.StartsWith(accept, StringComparison.Ordinal));
                Assert.DoesNotContain($"{town.Name}'s council approved", observation.Self?.TownMembershipNote ?? "", StringComparison.Ordinal);
            });
            Assert.Contains(observed, observation => observation.Candidates.Any(candidate => candidate.Id.StartsWith(accept, StringComparison.Ordinal)) &&
                observation.Self?.TownMembershipNote?.Contains("council approved admission; not accepted yet", StringComparison.Ordinal) == true);
            if (townId == Second) secondProposal = record.ProposalId;
            else return (state, record.ProposalId, secondProposal!);
        }
        throw new InvalidOperationException("Both Towns approve the newcomer.");
    }

    /// <summary>A saved world where a passed admission brought the newcomer in, plus a pending law proposal.</summary>
    private static (PrivateWorldRuntimeState State, string Newcomer, string LawProposal) CheckpointWithAdmission()
    {
        var newcomer = NewAgentId();
        var state = Generated("membership-tampered", newcomer);
        var town = state.Towns!.Single();
        var day = state.WorldSystems!.Config.TicksPerDay;
        var governance = TownGovernanceRules.SubmitProposal(town.Governance!, First, Founders[0], "admission", newcomer,
            "Admit the newcomer.", "council:" + town.Governance!.Revision, town.Governance.Members, 0, day);
        var admission = governance.Proposals[^1].Id;
        foreach (var voter in Founders.Take(3)) governance = TownGovernanceRules.VoteProposal(governance, admission, voter, true, 0);
        Assert.Equal("passed", governance.Proposals[^1].Status);
        governance = TownGovernanceRules.SubmitProposal(governance, First, Founders[0], "law", null, "Post harvest dates.",
            "council:" + governance.Revision, governance.Members, 0, day);
        var record = new TownAdmissionRecord(admission, newcomer, "admitted", 0, MemberIds: [newcomer]);
        return (state with { Towns = [town with { Governance = governance, Admissions = [record] }] }, newcomer, governance.Proposals[^1].Id);
    }

    private static string Founder(int number) => $"founder:{number:D32}";

    private static string NewAgentId() => "agent:" + Guid.NewGuid().ToString("N");

    private static string Civic(string townId, string kind) => $"civic|{townId}|{kind}|";

    private static TownRuntimeState Town(PrivateWorldRuntimeState state, string townId) => state.Towns!.Single(town => town.Id == townId);

    private static GridPoint Board(PrivateWorldRuntimeState state, string townId) => Town(state, townId).OriginSite!.Value;

    /// <summary>A generated world with four founders in the first Town and any added agents named and unhoused inside it.</summary>
    private static PrivateWorldRuntimeState Generated(string seed, params string[] added)
    {
        using var world = NormalPathWorld.CreateGenerated(seed, _ => new ActionCoverageRecorder(chooseIdle: true));
        foreach (var agent in added)
        {
            Assert.Null(world.AddAgent(agent, FreeTile(world.ExportState(), world.Towns.Single().BorderTiles)));
            Assert.True(world.RenameAgent(agent, NewcomerName));
        }
        return world.ExportState();
    }

    /// <summary>
    /// Sets each Town's residents and a fresh all-adult council. Agents named
    /// in neither list belong to no Town. A Second Town is founded on open
    /// land beside the first when <paramref name="second"/> is given.
    /// </summary>
    private static PrivateWorldRuntimeState WithTowns(PrivateWorldRuntimeState state, IEnumerable<string> first, IEnumerable<string>? second)
    {
        var society = state.Society.Society;
        string[] Adults(IEnumerable<string> ids) => ids.Where(id => society.GetInhabitant(id).AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder)
            .Order(StringComparer.Ordinal).ToArray();
        var original = Town(state, First);
        var towns = new List<TownRuntimeState>
        {
            original with { ResidentIds = first.Order(StringComparer.Ordinal).ToArray(), Governance = TownGovernanceState.Create(Adults(first)) },
        };
        if (second is not null)
        {
            var taken = Taken(state).Concat(original.BorderTiles).ToHashSet();
            var origin = state.Map.Tiles.Select(tile => tile.Position)
                .OrderBy(point => state.Map.FootDistance(point, original.OriginSite!.Value)).ThenBy(point => point.Y).ThenBy(point => point.X)
                .First(point => Square(point).All(tile => state.Map.Contains(tile) && state.Map.IsBuildable(tile) && !taken.Contains(tile)) &&
                    state.Map.IsReachableOnFoot(original.OriginSite!.Value, point));
            towns.Add(new TownRuntimeState(Second, "Second Town", "founded", 0, second.Order(StringComparer.Ordinal).ToArray(), [],
                Square(origin).ToArray(), origin, TownGovernanceState.Create(Adults(second))));
        }
        return state with { Towns = towns };

        static IEnumerable<GridPoint> Square(GridPoint center) =>
            Enumerable.Range(-2, 5).SelectMany(dy => Enumerable.Range(-2, 5).Select(dx => new GridPoint(center.X + dx, center.Y + dy)));
    }

    /// <summary>Tiles a newcomer cannot be placed on: resources, camp objects, buildings, Roads, fields and household land.</summary>
    private static HashSet<GridPoint> Taken(PrivateWorldRuntimeState state)
    {
        var definitions = state.WorldContent!.Buildings.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        return state.Map.Resources.Select(item => item.Position)
            .Concat(state.Map.CampObjects.Select(item => item.Position))
            .Concat(state.WorldSimulation!.Buildings.SelectMany(building => WorldContentSimulationRules.Footprint(definitions[building.DefinitionId], building)))
            .Concat(state.RoadTiles ?? [])
            .Concat((state.Fields ?? []).Select(field => field.Position))
            .Concat((state.HouseholdLandUseRights ?? []).SelectMany(right => right.Tiles))
            .ToHashSet();
    }

    private static GridPoint FreeTile(PrivateWorldRuntimeState state, IEnumerable<GridPoint> within)
    {
        var taken = Taken(state);
        taken.UnionWith(state.Inhabitants.Select(person => person.Position));
        return within.Where(point => state.Map.IsBuildable(point) && !taken.Contains(point))
            .OrderBy(point => point.Y).ThenBy(point => point.X).First();
    }

    /// <summary>A start inside the Town border with a short walk to its notice place that stays inside the border.</summary>
    private static GridPoint WalkInside(PrivateWorldRuntimeState state, TownRuntimeState town, GridPoint board) =>
        town.BorderTiles.Where(point => state.Map.FootDistance(point, board) is >= 2 and <= 3)
            .OrderBy(point => point.Y).ThenBy(point => point.X)
            .First(point => DeterministicRouteFinder.TryFind(state.Map, point, board, out var route) && route.All(town.BorderTiles.Contains));

    /// <summary>Partners the parents and gives the caregiver <paramref name="count"/> newborns in their household, each placed nearby.</summary>
    private static (PrivateWorldRuntimeState State, string[] Children) WithChildren(PrivateWorldRuntimeState state, string caregiver,
        string otherParent, int count)
    {
        var society = Partners(state.Society.Society, caregiver, otherParent);
        var household = society.GetInhabitant(caregiver).HouseholdId!;
        var children = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var food = society.Inventory.Lots.First(lot => lot.OwnerId == household && lot.ItemKind == "food" && lot.Quantity >= 4);
            var birth = SocietyFixture.CommitBirth(society, new SocietyBirthRequest($"membership-child-{index}", 1, caregiver, otherParent,
                household, [caregiver, otherParent], [caregiver, otherParent], food.Id, 4, society.WorldTick,
                ChildName: $"Ari {index + 1}", PrimaryCaregiverId: caregiver));
            children.Add(Assert.IsType<string>(birth.CreatedId));
            society = birth.Checkpoint;
        }
        var people = state.Inhabitants.ToList();
        foreach (var child in children)
        {
            var tile = FreeTile(state with { Inhabitants = people }, state.Map.Tiles.Select(item => item.Position)
                .OrderBy(point => state.Map.FootDistance(point, Board(state, First))));
            people.Add(new PlaytestInhabitantState(child, tile, 9_000, 0, "curious", "grow with the household",
                Survival: new SurvivalCondition(WarmthBasisPoints: 10_000)));
        }
        return (state with
        {
            Society = state.Society with { Society = society },
            Survival = new SettlementSurvivalState(society.WorldTick, []),
            Inhabitants = people.Select(person => person with { Survival = person.Survival ?? new SurvivalCondition() }).ToArray(),
        }, children.ToArray());
    }

    private static SocietyCheckpoint Partners(SocietyCheckpoint society, string first, string second)
    {
        society = SocietyFixture.ProposeRelationship(society, new("membership-parents", 1, SocietyRelationshipType.Partnership,
            first, second, society.WorldTick)).Checkpoint;
        return SocietyFixture.AcceptRelationship(society, "membership-parents", 1, second).Checkpoint;
    }

    /// <summary>Clear weather and well-fed, warm agents, so only the scripted choices matter.</summary>
    private static PrivateWorldRuntimeState Calm(PrivateWorldRuntimeState state)
    {
        state = SettlementWeatherTestFixture.WithWeather(state, WeatherKind.Clear);
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                Survival = person.Survival is null ? null : person.Survival with { WarmthBasisPoints = 10_000 },
                TravelCooldownTicks = 0,
            }).ToArray(),
        };
    }

    /// <summary>The same world with shorter days, so rules measured in days play out in a few dozen ticks.</summary>
    private static PrivateWorldRuntimeState ShortDays(PrivateWorldRuntimeState state, int day)
    {
        var society = state.Society.Society;
        var old = society.Config.TicksPerWorldDay;
        return state with
        {
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            {
                Config = state.WorldSystems.Config with { TicksPerDay = day },
                RegionalWeather = null,
            }, state.Map),
            Society = state.Society with
            {
                Society = society with
                {
                    Config = society.Config with { TicksPerWorldDay = day },
                    Inhabitants = society.Inhabitants.Select(person => person with
                    {
                        BirthTick = person.BirthTick / old * day,
                        BirthLifeTick = person.BirthLifeTick is { } birth ? birth / old * day : null,
                    }).ToArray(),
                },
            },
        };
    }

    private static PrivateWorldRuntimeState At(PrivateWorldRuntimeState state, GridPoint point, params string[] ids) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => ids.Contains(person.InhabitantId, StringComparer.Ordinal)
            ? person with { Position = point } : person).ToArray(),
    };

    private static PrivateWorldRuntimeState WithProject(PrivateWorldRuntimeState state, string actor, SettlementProject? project) => state with
    {
        Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
            ? person with { Project = project, HungerBasisPoints = 10_000 } : person).ToArray(),
    };

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntimeState Paused(PrivateWorldRuntime world)
    {
        world.Pause();
        return world.ExportState();
    }

    /// <summary>
    /// Loads a saved state as a new session in which every agent makes a fresh
    /// choice under the current script. Every agent may ask their model in the
    /// same tick, so no decision waits behind the default limit of four.
    /// </summary>
    private static PrivateWorldRuntime Reopen(PrivateWorldRuntimeState state, IDecisionProvider model)
    {
        state = state with
        {
            Inhabitants = state.Inhabitants.Select(person => person with { LastDecisionContext = null }).ToArray(),
            Society = state.Society with { Cognition = state.Society.Cognition with { MaxDispatchPerCycle = 8 } },
        };
        var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => model,
            maxCognitionDispatchPerCycle: 8);
        if (world.Society.IsPaused) world.Resume();
        return world;
    }

    private static string? OwnerView(PrivateWorldRuntime world, string id) =>
        new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(person => person.Id == id)
            .DecisionFactors.SingleOrDefault(factor => factor.Key == "town-membership")?.Detail;

    private static Func<Task> DirectSteps(PrivateWorldRuntime world) =>
        async () => Assert.True((await world.AdvanceOneTickAsync()).Advanced);

    /// <summary>Steps each world through the host service, which saves every tick and writes the admission log.</summary>
    private static Func<PrivateWorldRuntime, Func<Task>> ServiceSteps(string path, RecordingLogger<PrivateWorldRuntimeService> logger,
        List<PrivateWorldRuntimeService> services) => world =>
    {
        var presence = new OwnerClientPresenceLease(TimeSpan.FromMinutes(1));
        presence.RecordAuthenticatedReconnect("test-device");
        var service = new PrivateWorldRuntimeService(world, new PrivateWorldStateFile(path), presence, logger);
        services.Add(service);
        return async () =>
        {
            Assert.True(await service.TryAdvanceOnceAsync());
            await Task.Delay(1);
        };
    };

    private static async Task AdvanceUntil(PrivateWorldRuntime world, Func<bool> condition, int maxTicks = 60, Func<Task>? step = null)
    {
        step ??= DirectSteps(world);
        for (var tick = 0; tick < maxTicks && !condition(); tick++) await step();
        Assert.True(condition(), $"The expected state was not reached within {maxTicks} ticks.");
    }

    private static void AssertRoundTrip(PrivateWorldRuntime world) => AssertRoundTrips(world.ExportState());

    private static void AssertRoundTrips(PrivateWorldRuntimeState state)
    {
        var encoded = PrivateWorldRuntimeCodec.Encode(state);
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(encoded));
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
    }

    /// <summary>
    /// Answers as each agent's personal model would: the first offered
    /// candidate that starts with a scripted prefix, otherwise the next unused
    /// invented choice (a script entry starting with <c>!</c>, sent although
    /// nobody offered it), otherwise waiting.
    /// </summary>
    private sealed class ScriptedModel(DecisionProviderKind kind = DecisionProviderKind.LargeLanguageModel) : IDecisionProvider
    {
        private readonly ConcurrentDictionary<string, ConcurrentQueue<InhabitantObservation>> seen = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<(string Agent, string Choice), byte> invented = new();

        public ConcurrentDictionary<string, string[]> Scripts { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => kind;
        public long ProviderEpoch => 1;

        public InhabitantObservation[] ObservationsOf(string id) => seen.TryGetValue(id, out var queue) ? queue.ToArray() : [];

        public int InventionsUsed(string id) => invented.Keys.Count(key => key.Agent == id);

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            seen.GetOrAdd(observation.InhabitantId, _ => new()).Enqueue(observation);
            var script = Scripts.GetValueOrDefault(observation.InhabitantId, []);
            var selected = script.Where(entry => !entry.StartsWith('!')).Select(prefix => observation.Candidates
                .FirstOrDefault(candidate => candidate.Id.StartsWith(prefix, StringComparison.Ordinal))?.Id).FirstOrDefault(id => id is not null);
            selected ??= script.Where(entry => entry.StartsWith('!')).Select(entry => entry[1..])
                .FirstOrDefault(choice => invented.TryAdd((observation.InhabitantId, choice), 0));
            selected ??= "safe_idle";
            var scores = observation.Candidates.ToDictionary(candidate => candidate.Id, _ => 0d, StringComparer.Ordinal);
            scores[selected] = 1d;
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected, 1, scores));
        }
    }
}
