using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// Server-side projection of the Phase 2/3 composite runtime. It converts the
/// protected simulation records into stable viewer DTOs while retaining the
/// distinction between the live fixture topology, cognition state, and paused
/// authoring state.
/// </summary>
public sealed partial class OwnerWorldObservationStore
{
    private static ViewerTownGovernment ProjectGovernment(TownGovernmentState government, Func<string, string> name, long tick)
    {
        ViewerMayoralElection Election(TownMayoralContest contest) => new(contest.Id,
            TownArrangementRules.MandateLabel(contest.Mandates), contest.Stage, contest.Round, contest.RoundDeadlineTick,
            contest.Candidates.Select(id => new ViewerCivicCandidate(id, name(id), contest.Ballots.Count(b => b.CandidateId == id))).ToArray(),
            contest.WinnerId is { } winner ? name(winner) : null, contest.Reason);
        return new(TownArrangementRules.Declaration(government.Arrangement),
            government.Laws.TakeLast(16).Select(l =>
            {
                var v = TownLawRules.Current(l);
                return new ViewerTownLaw(l.Id, v.Subject, v.Rule, v.BoatAccess is null ? v.Scope : "communal_boats", v.SiteTiles.Count, v.Version, v.AdoptedTick, v.EndedTick)
                { Site = v.SiteTiles.Select(ToPosition).ToArray() };
            }).ToArray(), government.Laws.Count,
            government.Offices.Select(o => new ViewerTownOffice(TownArrangementRules.MandateLabel(o.Mandates),
                o.HolderId is { } holder ? name(holder) : null, o.TermEndTick, o.VacancyReason)).ToArray(),
            government.Changes.TakeLast(8).Select(c => new ViewerGovernmentChange(c.Id,
                (c.Kind == "replace_mayor" ? "Replace the elected mayoral mandates. " : "") + TownArrangementRules.Declaration(c.Target),
                c.Status, c.Votes.Count(v => v.Yes), c.Votes.Count(v => !v.Yes), c.Voters.Count / 2 + 1,
                c.DeadlineTick, c.HandoverDeadlineTick, c.Reason)
            {
                NonLandExtension = c.NonLandExtension is { } extension ? new(extension.HolderId, name(extension.HolderId),
                    TownArrangementRules.MandateLabel(extension.BaseMandate), extension.TermStartTick, extension.TermEndTick, extension.ConsentTick) : null,
            }).ToArray(),
            government.Contest is { } live ? Election(live) : null,
            government.ContestHistory.Count > 0 ? Election(government.ContestHistory[^1]) : null, government.MayoralRetryTick)
        {
            NonLandAuthorized = government.Arrangement.NonLand == TownArrangementRules.Mayor,
            NonLandAuthority = TownGovernmentRules.CurrentNonLandAuthority(government, tick) is { } authority
                ? new(authority.HolderId, name(authority.HolderId), authority.AuthorityId, authority.EffectiveTick,
                    authority.TermStartTick, authority.TermEndTick) : null,
            NonLandGrants = government.NonLandGrants.Select(grant => new ViewerNonLandGrant(grant.Id, grant.HolderId,
                name(grant.HolderId), TownArrangementRules.MandateLabel(grant.BaseMandate), grant.ConsentTick,
                grant.EffectiveTick, grant.TermStartTick, grant.TermEndTick)).ToArray(),
        };
    }

    private static ViewerTownLandHearing[] ProjectLandHearings(PrivateWorldRuntimeState state, TownRuntimeState town)
    {
        var cases = town.LandHearings?.Cases ?? [];
        string AgentName(string id) => state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == id)?.Name ?? "Unknown adult";
        string HouseholdName(string id) => state.Society.Society.Households.FirstOrDefault(household => household.Id == id)?.Name ?? "Unknown household";
        HashSet<string> Awareness(string noticeId, long tick) => (town.Governance?.Knowledge ?? [])
            .Where(receipt => receipt.NoticeId == noticeId && receipt.LearnedTick <= tick)
            .Select(receipt => receipt.AgentId).ToHashSet(StringComparer.Ordinal);
        ViewerLandHearingParty Party(TownLandCaseParty party, IReadOnlySet<string> aware) => new(party.Id, party.Kind,
            party.HouseholdId is { } household ? HouseholdName(household) :
                (state.Towns ?? []).FirstOrDefault(other => other.Id == party.TownId)?.Name ?? town.Name,
            party.AdultIds.ToArray(), party.AdultIds.Select(AgentName).ToArray(), party.RepresentativeId,
            party.RepresentativeId is { } representative ? AgentName(representative) : null,
            party.AdultIds.Where(aware.Contains).Concat(party.RepresentativeId is { } agent && aware.Contains(agent) ? [agent] : [])
                .Distinct(StringComparer.Ordinal).ToArray());
        var names = state.Society.Society.Inhabitants.Select(person => (Id: person.Id, Name: person.Name))
            .Concat(state.Society.Society.Households.Select(household => (Id: household.Id, Name: household.Name)))
            .Concat((state.Towns ?? []).Select(other => (Id: other.Id, Name: other.Name)))
            .Concat((state.WorldContent?.Buildings ?? []).Select(building => (Id: building.CanonicalId, Name: building.DisplayName)))
            .OrderByDescending(item => item.Id.Length).ToArray();
        string Readable(string text)
        {
            foreach (var name in names) text = text.Replace(name.Id, name.Name, StringComparison.Ordinal);
            return text;
        }
        ViewerLandHearingOutcome Outcome(TownLandRequestedOutcome outcome) => new(outcome.Kind, outcome.HouseholdId,
            outcome.HouseholdId is { } household ? HouseholdName(household) : null, outcome.AgreedEndTick);
        ViewerLandHearingJudge Judge(TownLandCaseJudge judge) => new(judge.AgentId, AgentName(judge.AgentId),
            judge.Kind, judge.AuthorityId, judge.AssignedTick);
        ViewerLandHearingElection Election(TownLandCaseJudgeContest contest) => new(contest.Id, contest.Stage,
            contest.Round, contest.RoundDeadlineTick, contest.Candidates.Select(id => new ViewerCivicCandidate(id,
                AgentName(id), contest.Ballots.Count(ballot => ballot.CandidateId == id))).ToArray(),
            contest.WinnerId is { } winner ? AgentName(winner) : null, contest.Reason);
        ViewerLandHearingEvidence Evidence(TownLandEvidence evidence, TownLandCase item)
        {
            var right = item.Revisions.SelectMany(revision => revision.RightVersions)
                .Concat(town.LandHearings?.OriginalRights ?? [])
                .Concat((state.HouseholdLandUseRights ?? []).Select(TownLandHearingRules.Snapshot))
                .Concat((town.LandHearings?.Adjustments ?? []).SelectMany(adjustment => adjustment.ResultRights)
                    .Select(TownLandHearingRules.Snapshot))
                .FirstOrDefault(version => version.Id == evidence.SourceRecordId && version.Version == evidence.SourceVersion)?.Right;
            var title = (state.TownLandTitles ?? []).FirstOrDefault(record => record.Id == evidence.SourceRecordId);
            var lawToken = evidence.SourceRecordId?.Split('@', 2);
            return new(evidence.Id, evidence.Revision, evidence.Kind, evidence.Acquisition,
                evidence.SourceAgentId, AgentName(evidence.SourceAgentId), evidence.SourceRecordId,
                evidence.SourceVersion, evidence.ObservedTick, evidence.SubmittedByAgentId,
                AgentName(evidence.SubmittedByAgentId), evidence.SubmittedTick, Readable(evidence.Text))
            {
                PermissionRecord = right is not null ? new(right.Id, right.TownId, right.HouseholdId,
                    right.Tiles.Select(ToPosition).ToArray(), right.GrantedTick, right.GrantSource, right.AgreedEndTick) : null,
                TitleRecord = title is not null ? new(title.Id, title.TownId, title.Tiles.Select(ToPosition).ToArray(), title.RecordedTick) : null,
                RecordPartyName = right is not null ? HouseholdName(right.HouseholdId) : title is not null
                    ? (state.Towns ?? []).FirstOrDefault(other => other.Id == title.TownId)?.Name : null,
                LawVersion = lawToken is { Length: 2 } && int.TryParse(lawToken[1], out var versionNumber) ? versionNumber : null,
            };
        }
        static bool Active(TownLandCase item) => item.SettledTick is null || item.ReopenRequests.Any(request => request.Status == "pending");
        return cases.Where(Active)
            .Concat(cases.Where(item => !Active(item)).OrderBy(item => item.SettledTick)
                .ThenBy(item => item.Id, StringComparer.Ordinal).TakeLast(RecentSettledLandHearingLimit))
            .OrderBy(item => item.FiledTick).ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(item =>
            {
                var revision = item.Revisions[^1];
                var aware = Awareness(revision.NoticeId, state.Society.Society.WorldTick);
                return new ViewerTownLandHearing(item.Id, item.Kind, item.Status, item.FiledTick, item.SettledTick,
                    revision.Number, revision.Tiles.Select(ToPosition).ToArray(), revision.RightVersions.Select(version =>
                        new ViewerLandHearingRightVersion(version.Id, version.Version, new ViewerHouseholdLandUseRight(
                            version.Right.Id, version.Right.TownId, version.Right.HouseholdId,
                            version.Right.Tiles.Select(ToPosition).ToArray(), version.Right.GrantedTick,
                            version.Right.GrantSource, version.Right.AgreedEndTick))).ToArray(),
                    revision.NoticeId, revision.PublishedTick, revision.DeadlineTick, Outcome(revision.RequestedOutcome),
                    item.Filings.Select(filing => new ViewerLandHearingFiling(filing.AgentId,
                        filing.AgentId is { } filer ? AgentName(filer) : null, filing.Kind, Readable(filing.Text),
                        Outcome(filing.RequestedOutcome), filing.Tick, filing.AuthorityId)).ToArray(),
                    revision.Parties.Select(party => Party(party, aware)).ToArray(),
                    item.Evidence.Select(evidence => Evidence(evidence, item)).ToArray(),
                    item.Responses.Select(response => new ViewerLandHearingResponse(response.Revision, response.PartyId,
                        response.AgentId, AgentName(response.AgentId), response.Kind, Readable(response.Text), response.Tick)).ToArray(),
                    item.Rulings.Select(ruling => new ViewerLandHearingRuling(ruling.Id, ruling.Revision, Judge(ruling.Judge),
                        ruling.Tick, Outcome(ruling.Outcome), item.Revisions.Single(old => old.Number == ruling.Revision)
                            .Tiles.Select(ToPosition).ToArray(), ruling.EvidenceIds.ToArray(), ruling.LawIds.ToArray(),
                        Readable(ruling.Reasons), ruling.AdjustmentIds.ToArray())
                    {
                        Parties = ruling.Parties.Select(party => Party(party, Awareness(
                            item.Revisions.Single(old => old.Number == ruling.Revision).NoticeId, ruling.Tick))).ToArray(),
                    }).ToArray(),
                    item.Judge is { } judge ? Judge(judge) : null,
                    item.JudgeHistory.Select(term => new ViewerLandHearingJudgeTerm(Judge(term.Judge), term.EndedTick, term.Reason)).ToArray(),
                    item.Contest is { } contest ? Election(contest) : null,
                    item.ContestHistory.Count > 0 ? Election(item.ContestHistory[^1]) : null,
                    item.ReopenRequests.Select(request => new ViewerLandHearingReopenRequest(request.Id, request.AgentId,
                        AgentName(request.AgentId), request.Tick, request.Kind, request.EvidenceIds.ToArray(), Readable(request.Reasons),
                        request.Status, request.AssessedBy is { } adjudicator ? Judge(adjudicator) : null,
                        request.AssessedTick, request.Assessment is { } assessment ? Readable(assessment) : null)).ToArray())
                {
                    CurrentParties = item.Status == "pending" ? TownLandCasePartyRules.CurrentParties(town, revision.Tiles,
                        state.HouseholdLandUseRights ?? [], state.HouseholdLandUseRequests ?? [], state.Society.Society.Inhabitants,
                        state.Society.Society.WorldTick, item).Select(party => Party(party, aware)).ToArray() : [],
                    Reads = item.Reads.Select(read => new ViewerLandHearingRead(read.Revision, read.AgentId, AgentName(read.AgentId),
                        read.ReadTick, read.EvidenceIds.ToArray(), read.SourceAgentId,
                        read.SourceAgentId is { } source ? AgentName(source) : null)
                    {
                        ReopenRequestIds = read.ReopenRequestIds.ToArray(),
                    }).ToArray(),
                };
            }).ToArray();
    }

    private static ViewerLandTransfer[] ProjectLandTransfers(PrivateWorldRuntimeState state, TownRuntimeState town)
    {
        var transfers = town.LandHearings?.Transfers ?? [];
        var receipts = town.Governance?.Knowledge ?? [];
        var adultHouseholds = state.Society.Society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
            person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).ToDictionary(person => person.Id, person => person.HouseholdId, StringComparer.Ordinal);
        string AgentName(string id) => state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == id)?.Name ?? "Unknown adult";
        string HouseholdName(string id) => state.Society.Society.Households.FirstOrDefault(household => household.Id == id)?.Name ?? "Unknown household";
        return transfers.Where(request => request.Status == "pending")
            .Concat(transfers.Where(request => request.Status != "pending").OrderBy(request => request.SettledTick)
                .ThenBy(request => request.Id, StringComparer.Ordinal).TakeLast(RecentClosedLandTransferLimit))
            .OrderBy(request => request.ProposedTick).ThenBy(request => request.Id, StringComparer.Ordinal)
            .Select(request =>
            {
                var parties = request.Status == "pending" ? TownLandTransferRules.PartiesFor(request.RightVersions.Select(version => version.Right),
                    request.Tiles, request.TargetHouseholdId, adultHouseholds) : request.Receipt?.Parties ?? request.Parties;
                var rosterKind = request.Status == "pending" ? "current" : request.Receipt is not null ? "settlement" : "publication";
                var asOf = request.SettledTick ?? state.Society.Society.WorldTick;
                var aware = receipts.Where(receipt => receipt.NoticeId == request.NoticeId && receipt.LearnedTick >= request.ProposedTick &&
                    receipt.LearnedTick <= asOf).Select(receipt => receipt.AgentId).ToHashSet(StringComparer.Ordinal);
                return new ViewerLandTransfer(request.Id, request.FilerId, AgentName(request.FilerId), request.TargetHouseholdId,
                    HouseholdName(request.TargetHouseholdId), request.Tiles.Select(ToPosition).ToArray(), request.RightVersions.Select(version =>
                        new ViewerLandHearingRightVersion(version.Id, version.Version, new ViewerHouseholdLandUseRight(version.Right.Id,
                            version.Right.TownId, version.Right.HouseholdId, version.Right.Tiles.Select(ToPosition).ToArray(),
                            version.Right.GrantedTick, version.Right.GrantSource, version.Right.AgreedEndTick))).ToArray(),
                    parties.Select(party => new ViewerLandTransferParty(party.HouseholdId, party.Kind, HouseholdName(party.HouseholdId), rosterKind,
                        party.AdultIds.ToArray(), party.AdultIds.Select(AgentName).ToArray(),
                        TownLandTransferRules.AcceptedAdults(request, party, receipts, asOf).ToArray(), party.AdultIds.Where(aware.Contains).ToArray())).ToArray(),
                    request.NoticeId, request.ProposedTick, request.Responses.Select(response => new ViewerLandTransferResponse(response.HouseholdId,
                        HouseholdName(response.HouseholdId), response.AgentId, AgentName(response.AgentId), response.Kind, response.Tick,
                        response.PartyAdults.ToArray())).ToArray(), request.Status, request.SettledTick, request.Reason, request.Receipt?.AdjustmentId);
            }).ToArray();
    }

    private static ViewerLandHearingProposal ProjectLandHearingProposal(PrivateWorldRuntimeState state, TownLandFilingRequest request)
    {
        var statement = request.Statement;
        var names = state.Society.Society.Inhabitants.Select(person => (Id: person.Id, Name: person.Name))
            .Concat(state.Society.Society.Households.Select(household => (Id: household.Id, Name: household.Name)))
            .Concat((state.Towns ?? []).Select(town => (Id: town.Id, Name: town.Name))).OrderByDescending(item => item.Id.Length);
        foreach (var name in names) statement = statement.Replace(name.Id, name.Name, StringComparison.Ordinal);
        var outcome = request.RequestedOutcome;
        return new(request.Tiles.Select(ToPosition).ToArray(), new(outcome.Kind, outcome.HouseholdId,
            outcome.HouseholdId is { } householdId ? state.Society.Society.Households.FirstOrDefault(household => household.Id == householdId)?.Name ?? "Unknown household" : null,
            outcome.AgreedEndTick), statement);
    }

    private static string LandRequestApprovalDetail(PrivateWorldRuntimeState state, HouseholdLandUseRequest request, bool disputed)
    {
        if (request.Status != "pending") return request.Status == "hearing_resolved"
            ? "Use request resolved by a recorded land ruling" : "Household request " + request.Status.Replace('_', ' ');
        var adults = state.Society.Society.Inhabitants.Where(p => p.HouseholdId == request.HouseholdId &&
            p.Status == SocietyInhabitantStatus.Active && p.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder).ToArray();
        var accepted = adults.Count(p => request.Consents.Any(c => c.AgentId == p.Id && c.Accepted));
        var proposal = state.Towns?.Single(t => t.Id == request.TownId).Governance?.Proposals.SingleOrDefault(p => p.Id == request.CouncilProposalId);
        var council = proposal?.Status == "passed" ? "Council approved" : proposal is null ? "Awaiting Council consideration" :
            $"Council votes: {proposal.Votes.Count(v => v.Yes)}/{proposal.RequiredYes}";
        var resolved = request.HearingResolutions.SelectMany(receipt => receipt.Tiles).Distinct().Count();
        return $"{council}; household acceptance: {accepted}/{adults.Length}" + (disputed ? "; disputed plot" : "") +
            (resolved > 0 ? $"; {resolved}/{request.Tiles.Count} requested tiles resolved by land rulings; remaining tiles still pending" : "");
    }

    private const int AgentKnowledgeArtifactLimit = 8;
    private const int RecentClosedInstructionLimitPerAgent = 6;
    public const int RecentCivicProposalLimit = 8;
    public const int RecentSettledLandHearingLimit = 8;
    public const int RecentClosedLandTransferLimit = 8;
    private static readonly string[] OwnerServerCapabilities =
    [
        "snapshot.read.v1",
        "event-replay.read.v1",
        "event-history-reset.read.v1",
        "reconnect-baseline.read.v1",
        "seeded-map.read.v1",
        "inhabitant-inspection.read.v1",
        "spatial-knowledge.read.v1",
        "owner-map-layer-delta.v1",
        "owner-device-pairing.v1",
        "owner-observation.read.v1",
        "owner-control.request.v1",
        "owner-provider-configuration.v1",
        "owner-inhabitant-provider-configuration.v1",
        "paused-authoring.request.v1",
        "content-governance.read.v1",
        "content-governance.write.v1",
    ];

    private static readonly string[] OwnerClientCapabilities =
    [
        "snapshot.read.v1",
        "event-replay.read.v1",
        "reconnect-baseline.read.v1",
        "owner-device-pairing.v1",
    ];

    private readonly OwnerWorldRuntime? ownerRuntime;
    private readonly PrivateWorldRuntime? privateRuntime;

    public OwnerWorldObservationStore(OwnerWorldRuntime runtime)
    {
        ownerRuntime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public OwnerWorldObservationStore(PrivateWorldRuntime runtime)
    {
        privateRuntime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public ViewerHandshake GetOwnerHandshake() => new(
        new ProtocolVersion(Major: 1, Minor: 1),
        privateRuntime is null ? OwnerServerCapabilities.ToArray() : [.. OwnerServerCapabilities, "owner-life-pace.v1", "owner-jev-assistance.v1", "owner-routine-helper.v1", "owner-building-design.v1", "owner-terrain-delta.v1", "owner-observation-timeline.v1"],
        OwnerClientCapabilities.ToArray());

    public ViewerWorldSnapshot GetSnapshot()
    {
        if (privateRuntime is null) return ToSnapshot(ownerRuntime!.Capture(0).Snapshot);
        var (state, diagnostics) = privateRuntime.ExportStateWithDiagnostics();
        return ToSnapshot(state, diagnostics);
    }

    public ViewerEventSlice GetEventsAfter(long afterEventId)
    {
        if (privateRuntime is not null)
        {
            var state = privateRuntime.ExportState();
            return new ViewerEventSlice(
                state.Society.Society.WorldTick,
                afterEventId,
                state.Events
                    .Where(worldEvent => worldEvent.EventId > afterEventId)
                    .Select(ToEvent)
                    .ToArray(), state.EventHistoryFloor, afterEventId < state.EventHistoryFloor);
        }

        var capture = ownerRuntime!.Capture(afterEventId);
        return new ViewerEventSlice(
            capture.Snapshot.World.Identity.WorldTick,
            capture.AfterEventId,
            capture.Events.Select(ToEvent).ToArray());
    }

    public ViewerReconnectBaseline GetReconnectBaseline(long afterEventId,
        string? knownTerrainWorldId = null, string? knownTerrainDigest = null,
        string? knownMapLayersDigest = null)
    {
        if (privateRuntime is not null)
        {
            var (state, diagnostics, timeline) = privateRuntime.ExportObservation();
            var privateSnapshot = ToSnapshot(state, diagnostics, knownTerrainWorldId, knownTerrainDigest,
                knownMapLayersDigest);
            return new ViewerReconnectBaseline(
                privateSnapshot,
                new ViewerEventSlice(
                    privateSnapshot.WorldTick,
                    afterEventId,
                    state.Events
                        .Where(worldEvent => worldEvent.EventId > afterEventId)
                        .Select(ToEvent)
                        .ToArray(), state.EventHistoryFloor, afterEventId < state.EventHistoryFloor),
                new ViewerObserverTimeline(timeline.InstanceId, timeline.Generation));
        }

        var capture = ownerRuntime!.Capture(afterEventId);
        var snapshot = ToSnapshot(capture.Snapshot);
        return new ViewerReconnectBaseline(
            snapshot,
            new ViewerEventSlice(
                snapshot.WorldTick,
                capture.AfterEventId,
                capture.Events.Select(ToEvent).ToArray()));
    }

    private static ViewerWorldSnapshot ToSnapshot(OwnerWorldSnapshot state)
    {
        var map = state.CurrentMap;
        var resourceStates = state.World.Resources.ToDictionary(resource => resource.Id, StringComparer.Ordinal);
        var actor = ToActor(state.World.Actor);
        return new ViewerWorldSnapshot(
            state.World.Identity.WorldId,
            state.World.Identity.WorldTick,
            state.CurrentMapManifestDigest,
            map.Tiles
                .OrderBy(tile => tile.Position.Y)
                .ThenBy(tile => tile.Position.X)
                .Select(tile => new ViewerTile(tile.Position.X, tile.Position.Y, ToWireValue(tile.Terrain)))
                .ToArray(),
            map.CampObjects
                .Where(mapObject => mapObject.Kind != "bedroll")
                .OrderBy(mapObject => mapObject.Id, StringComparer.Ordinal)
                .Select(mapObject => new ViewerMapObject(mapObject.Id, mapObject.Kind, ToPosition(mapObject.Position)))
                .ToArray(),
            map.Resources
                .OrderBy(resource => resource.Id, StringComparer.Ordinal)
                .Select(resource => new ViewerResource(
                    resource.Id,
                    resource.Kind,
                    ToPosition(resource.Position),
                    resource.IsRenewable,
                    resourceStates.TryGetValue(resource.Id, out var runtimeResource)
                        ? ToWireValue(runtimeResource.State)
                        : "available"))
                .ToArray(),
            actor,
            state.LatestGlobalEventId)
        {
            Inhabitants = CreateInhabitants(state),
            Authoring = new ViewerAuthoringState(
                state.IsPaused,
                state.RunEpoch,
                state.Revision,
                state.TopologyRevision,
                state.InitialMapManifestDigest,
                state.CurrentMapManifestDigest,
                state.Climate.Weather,
                state.Climate.Season,
                state.ApprovedAssetReferences
                    .OrderBy(reference => reference.AssetId, StringComparer.Ordinal)
                    .Select(reference => $"{reference.AssetId}@{reference.AssetDigest}")
                    .ToArray()),
            Instructions = state.Instructions
                .OrderBy(instruction => instruction.SubmissionSequence)
                .Select(instruction => new ViewerInstruction(
                    instruction.InstructionId,
                    instruction.TargetInhabitantId,
                    ToWireValue(instruction.Kind),
                    instruction.Text,
                    ToWireValue(instruction.State),
                    instruction.SubmittedTick,
                    instruction.RunEpoch,
                    instruction.SubmissionSequence))
                .ToArray(),
            Cognition = state.Cognition is null
                ? null
                : new ViewerCognition(
                    state.Cognition.ProviderKind.ToString().ToLowerInvariant(),
                    state.Cognition.IsPaused,
                    state.Cognition.InFlightRequestId,
                    state.Cognition.CurrentIntention?.CandidateId,
                    state.Cognition.CurrentIntention?.Provider.ToString().ToLowerInvariant(),
                    state.Cognition.Events
                        .OrderBy(worldEvent => worldEvent.EventId)
                        .TakeLast(12)
                        .Select(worldEvent => new ViewerCognitionEvent(
                            worldEvent.EventId,
                            worldEvent.WorldTick,
                            worldEvent.Kind,
                            worldEvent.Detail))
                        .ToArray()),
        };
    }

    private static bool RequiresWorldCreation(PrivateWorldRuntimeState state)
    {
        // This is a presentation decision about the captured save, not a new
        // world or a save migration. Keep any authored work accessible, even
        // when its founders or buildings have since been removed.
        return state.Geography is null &&
            state.FounderSetup is { Started: false, FounderIds.Count: 0 } &&
            state.Society.Society is { WorldTick: 0, IsPaused: true, Inhabitants.Count: 0, EventHistoryFloor: 0 } &&
            state.Inhabitants.Count == 0 && state.DeceasedInhabitants is null or { Count: 0 } &&
            state.Content is { Packages.Count: 0, Events.Count: 0 } &&
            state.WorldContent is { Buildings.Count: 0, Recipes.Count: 0 } &&
            state.WorldSimulation is { Buildings.Count: 0, ProductionJobs.Count: 0 } simulation &&
            simulation.CropBuilds is null or { Count: 0 } &&
            simulation.BuildingExpansions is null or { Count: 0 } &&
            simulation.GuestInvitations is null or { Count: 0 } &&
            state.Fields is null or { Count: 0 } && state.RoadTiles is null or { Count: 0 } &&
            state.Bridges is null or { Count: 0 } && state.Instructions is null or { Count: 0 } &&
            state.EventHistoryFloor == 0 && state.HistoryArchiveHead is null &&
            state.Events.All(item => item.Kind is "world_created" or "town_founding_started" or "continuity_rule_on" or "paused") &&
            state.Society.Society.Events.All(item => item.Kind is "household_created" or "paused") &&
            state.Society.Society.Inventory.Events.Count == 0;
    }

    private static ViewerWorldSnapshot ToSnapshot(PrivateWorldRuntimeState state, PrivateWorldDiagnostics diagnostics,
        string? knownTerrainWorldId = null, string? knownTerrainDigest = null,
        string? knownMapLayersDigest = null)
    {
        var map = state.Map;
        var fertility = new LandFertility(map, state.WorldSeed);
        var ecology = state.WorldSystems?.Ecology.Resources.ToDictionary(resource => resource.Id, StringComparer.Ordinal);
        var buildingDefinitions = state.WorldContent?.Buildings.ToDictionary(building => building.CanonicalId, StringComparer.Ordinal);
        var storageChanges = RecentBuildingStorageChanges(state.Society.Society.Inventory);
        var activeInhabitants = state.Society.Society.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var inhabitantsById = state.Society.Society.Inhabitants
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        ViewerTownElection ProjectElection(TownElection election) => new(election.Id, election.Kind, election.Stage,
            election.Seats, election.DeadlineTick, election.Candidates.Select(id => new ViewerCivicCandidate(
                id, inhabitantsById.GetValueOrDefault(id)?.Name ?? id,
                election.Ballots.Count(ballot => ballot.Choices.Contains(id, StringComparer.Ordinal)))).ToArray(),
            election.SettledSeats.Select(id => inhabitantsById.GetValueOrDefault(id)?.Name ?? id).ToArray());
        ViewerTownProjectPlan ProjectPlan(TownProjectPayload plan, string proposerId)
        {
            var definition = buildingDefinitions?.GetValueOrDefault(plan.DefinitionId) ??
                TownProjectRules.DefinitionFor(plan.DefinitionId) ?? throw new InvalidDataException("Unsupported Town project definition.");
            return new ViewerTownProjectPlan(plan.Name, proposerId,
                inhabitantsById.GetValueOrDefault(proposerId)?.Name ?? proposerId,
                plan.DefinitionId, plan.BoatPortId is null ? definition.DisplayName : "Communal boat", ToPosition(plan.BoatPortId is null ? plan.Site : TownProjectRules.WorkSite(plan)), ToPosition(plan.Entrance),
                plan.BoatPortId is null ? definition.Width : 1, plan.BoatPortId is null ? definition.Height : 1,
                plan.Budget.Select(q => new ViewerTownProjectBudget(q.ResourceId, q.Amount)).ToArray())
            {
                Tags = plan.BoatPortId is null ? definition.Tags.ToArray() : ["boat_project"],
                BoatPortId = plan.BoatPortId,
            };
        }
        ViewerCivicProposal ProjectProposal(TownProposal proposal) => new(proposal.Id, proposal.Kind,
            proposal.Text, proposal.Status, proposal.Votes.Count(v => v.Yes), proposal.Votes.Count(v => !v.Yes),
            proposal.RequiredYes, proposal.DeadlineTick)
        {
            Project = proposal.Project is { } plan ? ProjectPlan(plan, proposal.AuthorId) : null,
        };
        ViewerTownProject ProjectConstruction(TownRuntimeState town, TownConstructionProject project)
        {
            // Approval belongs to the full civic ledger, even when it is older than the recent proposal list.
            var approval = town.Governance!.Proposals.Single(p => p.Id == project.ProposalId);
            var plan = ProjectPlan(project.Plan, approval.AuthorId);
            return new ViewerTownProject(project.Id, project.ProposalId, plan.Name, plan.ProposerId,
                plan.ProposerName, plan.DefinitionId, plan.DisplayName, plan.Site, plan.Entrance,
                plan.Width, plan.Height, project.Plan.Budget.Select(q => new ViewerTownProjectMaterial(
                    q.ResourceId, q.Amount, TownProjectRules.DeliveredQuantity(project, town.Id,
                        state.Society.Society.Inventory, q.ResourceId))).ToArray(),
                project.WorkDone, TownProjectRules.RequiredWork(project.Plan), project.Stage, project.Blocker,
                project.CompletedBuildingId, ProjectProposal(approval))
            {
                Tags = plan.Tags,
                CompletedBoatId = project.CompletedBoatId,
            };
        }
        string MarketOwnerName(string id) => inhabitantsById.GetValueOrDefault(id)?.Name ??
            state.Society.Society.Households.FirstOrDefault(household => household.Id == id)?.Name ??
            state.Towns?.FirstOrDefault(town => town.Id == id)?.Name ?? id;
        ViewerMarket ProjectMarket(TownMarketState market)
        {
            var inventory = state.Society.Society.Inventory;
            var plaza = MarketContent.PlazaOrigin(market.Site);
            var stalls = market.Stalls.Where(stall => stall.RemovedTick is null)
                .OrderBy(stall => stall.SlotIndex).Select(stall =>
                {
                    var building = state.WorldSimulation?.Buildings.FirstOrDefault(item => item.InstanceId == stall.BuildingId);
                    if (building is null) return null;
                    var occupancy = market.RemovedTick is null ? market.Occupancies
                        .LastOrDefault(item => item.StallBuildingId == stall.BuildingId && item.EndedTick is null) : null;
                    var stock = MarketTradeRules.StockAt(market, stall.BuildingId, building.Position, inventory)
                        .OrderBy(lot => lot.OwnerId, StringComparer.Ordinal).ThenBy(lot => lot.ItemKind, StringComparer.Ordinal)
                        .ThenBy(lot => lot.Id, StringComparer.Ordinal).Select(lot => new ViewerMarketStock(
                            lot.Id, lot.ContainerLotId, lot.OwnerId, MarketOwnerName(lot.OwnerId), lot.ItemKind,
                            lot.Quantity, MarketTradeRules.AvailableQuantity(inventory, lot))).ToArray();
                    var trades = market.Trades.Where(trade => trade.StallBuildingId == stall.BuildingId)
                        .OrderByDescending(trade => inventory.GetOffer(trade.OfferId).State == DirectBarterState.Open)
                        .ThenByDescending(trade => trade.ProposedTick).ThenBy(trade => trade.OfferId, StringComparer.Ordinal)
                        .Take(8).Select(trade =>
                        {
                            var offer = inventory.GetOffer(trade.OfferId);
                            return new ViewerMarketTrade(trade.OfferId, trade.SellerAgentId, MarketOwnerName(trade.SellerAgentId),
                                trade.GoodsOwnerId, MarketOwnerName(trade.GoodsOwnerId), trade.PaymentOwnerId,
                                MarketOwnerName(trade.PaymentOwnerId), trade.BuyerId, MarketOwnerName(trade.BuyerId),
                                trade.GoodsKind, offer.FirstQuantity, trade.PaymentKind, offer.SecondQuantity,
                                offer.State.ToString().ToLowerInvariant(), trade.CancellationReason,
                                offer.AcceptedBy.Contains(offer.FirstPartyId, StringComparer.Ordinal),
                                offer.AcceptedBy.Contains(offer.SecondPartyId, StringComparer.Ordinal));
                        }).ToArray();
                    return new ViewerMarketStall(stall.BuildingId, stall.SlotIndex, ToPosition(building.Position),
                        occupancy?.SellerAgentId, occupancy is null ? null : MarketOwnerName(occupancy.SellerAgentId),
                        occupancy?.StartedTick, stock, trades);
                }).OfType<ViewerMarketStall>().ToArray();
            return new ViewerMarket(market.Id, market.ProjectId, market.HallBuildingId, ToPosition(market.Site),
                ToPosition(plaza), MarketContent.PlazaWidth, MarketContent.PlazaHeight, stalls, market.RemovedTick);
        }
        var physicalById = state.Inhabitants.ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
        var deceasedById = (state.DeceasedInhabitants ?? []).ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
        var resourceStates = state.Resources.ToDictionary(item => item.ResourceId, item => item.State, StringComparer.Ordinal);
        var completedInstructionIds = (state.CompletedInstructionIds ?? []).ToHashSet(StringComparer.Ordinal);
        var visibleInstructions = ProjectPrivateInstructions(state, completedInstructionIds);
        var first = activeInhabitants.FirstOrDefault();
        ViewerActor? actor = null;
        if (first is not null)
        {
            var physical = physicalById[first.Id];
            var inventory = InventoryFor(state, first.Id);
            actor = new ViewerActor(first.Id, ToPosition(physical.Position), physical.HungerBasisPoints,
                inventory.Where(item => item.Kind == "food").Sum(item => item.Quantity),
                inventory.Where(item => item.Kind == "wood").Sum(item => item.Quantity));
        }
        var jobs = state.WorldSimulation?.ProductionJobs.Concat(state.WorldSimulation.CropBuilds ?? []).ToArray() ?? [];
        var latestEventId = state.Events.Count == 0 ? 0 : state.Events[^1].EventId;
        var terrainUnchanged = state.Geography is not null &&
            string.Equals(knownTerrainWorldId, state.Society.Society.WorldId, StringComparison.Ordinal) &&
            string.Equals(knownTerrainDigest, map.ManifestDigest, StringComparison.Ordinal);
        var mapLayersDigest = state.Geography is null ? null : MapLayerManifestCodec.Digest(map);
        var mapLayersUnchanged = terrainUnchanged && mapLayersDigest is not null &&
            string.Equals(knownMapLayersDigest, mapLayersDigest, StringComparison.Ordinal);
        var packedTerrain = state.Geography is null || terrainUnchanged ? null : PackTerrain(map);
        var weatherAnchor = map.CampObjects.FirstOrDefault(item => item.Kind == "cooking")?.Position ??
            state.Towns?.FirstOrDefault(item => item.OriginSite is not null)?.OriginSite ??
            map.Resources.First(item => item.Id == "berry-patch").Position;
        var campWeather = state.WorldSystems is { } currentSystems
            ? WeatherRules.At(currentSystems,
                weatherAnchor, map.Height)
            : WeatherKind.Clear;
        return new ViewerWorldSnapshot(
            state.Society.Society.WorldId,
            state.Society.Society.WorldTick,
            map.ManifestDigest,
            (state.Geography is null ? map.Tiles : [])
                .OrderBy(tile => tile.Position.Y)
                .ThenBy(tile => tile.Position.X)
                .Select(tile => new ViewerTile(tile.Position.X, tile.Position.Y, ToWireValue(tile.Terrain)))
                .ToArray(),
            map.CampObjects
                .Where(mapObject => mapObject.Kind != "bedroll")
                .OrderBy(mapObject => mapObject.Id, StringComparer.Ordinal)
                .Select(mapObject => new ViewerMapObject(mapObject.Id, mapObject.Kind, ToPosition(mapObject.Position)))
                .ToArray(),
            map.Resources
                .OrderBy(resource => resource.Id, StringComparer.Ordinal)
                .Select(resource => new ViewerResource(
                    resource.Id,
                    resource.Kind,
                    ToPosition(resource.Position),
                    resource.IsRenewable,
                    resourceStates.TryGetValue(resource.Id, out var resourceState)
                        ? ToWireValue(resourceState)
                        : "available",
                    ecology?.GetValueOrDefault(resource.Id)?.Quantity,
                    ecology?.GetValueOrDefault(resource.Id)?.Capacity,
                    ecology?.GetValueOrDefault(resource.Id)?.RegenerationAmount,
                    ecology?.GetValueOrDefault(resource.Id)?.RegenerationIntervalDays,
                    ecology?.GetValueOrDefault(resource.Id)?.RegenerationSeason.ToString().ToLowerInvariant(),
                    resource.TreeKind,
                    ecology?.GetValueOrDefault(resource.Id)?.IsPlanted ?? false,
                    TreeGrowthRules.StageOf(resource.TreeKind, ecology?.GetValueOrDefault(resource.Id),
                        state.WorldSystems?.Climate.Season ?? SeasonKind.Spring),
                    resource.NaturalObjectKind))
                .ToArray(),
            actor,
            latestEventId)
        {
            PackedTerrain = packedTerrain,
            ContinuityRuleActive = state.Continuity?.Active,
            PackedMapLayers = state.Geography is null || mapLayersUnchanged ? null : PackMapLayers(map, state.WorldSeed),
            MapLayersDigest = mapLayersDigest,
            Fields = (state.Fields ?? []).Select(field => new ViewerFarmField(ToPosition(field.Position), field.HouseholdId,
                field.Stage.ToString().ToLowerInvariant(), field.Crop, fertility.At(field.Position),
                field.Work?.WorkerId, field.Work?.RemainingTicks)).ToArray(),
            Handcarts = ProjectHandcarts(state),
            Boats = ProjectBoats(state),
            BoatRequests = ProjectBoatRequests(state),
            GroundStocks = state.Society.Society.Inventory.Lots.Where(lot => lot.GroundPosition is not null && lot.Quantity > 0 &&
                lot.ItemKind != InventoryContainerRules.Handcart)
                .GroupBy(lot => (Position: lot.GroundPosition!.Value, lot.OwnerId, lot.ItemKind))
                .OrderBy(group => group.Key.Position.Y).ThenBy(group => group.Key.Position.X)
                .ThenBy(group => group.Key.ItemKind, StringComparer.Ordinal).ThenBy(group => group.Key.OwnerId, StringComparer.Ordinal)
                .Select(group => new ViewerGroundStock(new(group.Key.Position.X, group.Key.Position.Y), group.Key.OwnerId,
                    group.Key.ItemKind, group.Sum(lot => lot.Quantity))).ToArray(),
            WrapsEastWest = state.Geography?.WrapEastWest == true,
            LastTickMilliseconds = diagnostics.LastTickMilliseconds,
            Inhabitants = activeInhabitants
                .Select(inhabitant => ToPlaytestInhabitant(state, inhabitant, physicalById[inhabitant.Id]) with
                {
                    PlannedRoute = ToPlannedRoute(diagnostics.PlannedRoutes.GetValueOrDefault(inhabitant.Id)),
                })
                .Concat(state.Society.Society.Inhabitants
                    .Where(inhabitant => inhabitant.Status == SocietyInhabitantStatus.Dead && deceasedById.ContainsKey(inhabitant.Id))
                    .Select(inhabitant => ToDeceasedInhabitant(state, inhabitant, deceasedById[inhabitant.Id])))
                .OrderBy(inhabitant => inhabitant.Id, StringComparer.Ordinal)
                .ToArray(),
            Conversations = (state.Conversations ?? [])
                .OrderByDescending(conversation => conversation.LastUpdatedTick)
                .ThenBy(conversation => conversation.Id, StringComparer.Ordinal)
                .Take(16)
                .Select(conversation => new ViewerConversation(
                    conversation.Id,
                    conversation.InitiatorId,
                    inhabitantsById.GetValueOrDefault(conversation.InitiatorId)?.Name ?? conversation.InitiatorId,
                    conversation.InviteeId,
                    inhabitantsById.GetValueOrDefault(conversation.InviteeId)?.Name ?? conversation.InviteeId,
                    ConversationStatus(conversation.Status),
                    conversation.Interruption == AgentConversationInterruption.None
                        ? null : ConversationInterruption(conversation.Interruption),
                    conversation.Outcome,
                    conversation.CreatedTick,
                    conversation.LastUpdatedTick,
                    conversation.Turns.TakeLast(AgentConversationRules.MaximumPublicTurns +
                            AgentConversationRules.MaximumWrapUpTurns)
                        .Select(turn => new ViewerConversationTurn(
                            turn.Id,
                            turn.SpeakerId,
                            inhabitantsById.GetValueOrDefault(turn.SpeakerId)?.Name ?? turn.SpeakerId,
                            turn.Text,
                            turn.WorldTick,
                            turn.ListenerIds.Take(AgentConversationRules.MaximumListenersPerTurn).ToArray(),
                            turn.IsWrapUp, turn.SurnameChoice))
                        .ToArray())
                {
                    Kind = conversation.Kind == AgentConversationKind.MarriageSurname ? "marriage_surname" : "ordinary",
                    ChosenSurname = state.Marriages.FirstOrDefault(item => item.SurnameConversationId == conversation.Id)?.ChosenSurname,
                })
                .ToArray(),
            Stockpiles = state.Society.Society.Households.Select(household =>
                new ViewerStockpile(household.Id, (household.Id, household.Name) switch
                {
                    ("household:camp-alpha", "Camp Alpha") => "First household",
                    ("household:camp-beta", "Camp Beta") => "Second household",
                    _ => household.Name,
                }, InventoryFor(state, household.Id))).ToArray(),
            Council = state.Council is { } council ? new ViewerCouncil(
                state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == council.StewardId)?.Name,
                council.FoodPolicy, council.Ballot?.Policy, council.Ballot?.Approvals.Count ?? 0,
                council.Ballot?.Rejections.Count ?? 0, council.Ballot?.Electorate.Count ?? 0) : null,
            LifePaceRate = state.Society.Society.LifeClock?.Rate ?? 1,
            JevEnabled = state.JevEnabled ?? true,
            RoutineHelperProvider = state.RoutineHelper.Provider,
            RoutineHelperModel = state.RoutineHelper.Model,
            RoutineHelperCredentialSlotId = state.RoutineHelper.CredentialSlotId,
            FounderSetup = state.FounderSetup is { } setup
                ? new ViewerFounderSetup(PrivateWorldRuntime.RequiredFounders, setup.FounderIds.Count, setup.Started)
                {
                    RequiresWorldCreation = RequiresWorldCreation(state),
                    CanChooseTownSite = state.Geography is not null && !setup.Started && setup.FounderIds.Count == 0,
                    HasAcceptedTownSite = (state.Towns ?? []).Any(town => town.OriginSite is not null),
                    LastFounderId = !setup.Started && setup.FounderIds.Count > 0
                        ? setup.FounderIds[^1] : null,
                }
                : null,
            Towns = (state.Towns ?? []).OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new ViewerTown(item.Id, item.Name, item.FoundingState, item.FoundedTick,
                    item.ResidentIds.ToArray(), item.AssignedBuildingIds.ToArray(),
                    item.BorderTiles.OrderBy(point => point.Y).ThenBy(point => point.X)
                        .Select(ToPosition).ToArray())
                {
                    LandHearings = ProjectLandHearings(state, item),
                    LandHearingCount = item.LandHearings?.Cases.Count ?? 0,
                    NonviolentCases = ProjectNonviolentCases(state, item),
                    NonviolentCaseCount = item.Nonviolent.Cases.Count,
                    LandTransfers = ProjectLandTransfers(state, item),
                    LandTransferCount = item.LandHearings?.Transfers.Count ?? 0,
                    Government = item.Government is { } government ? ProjectGovernment(government,
                        id => inhabitantsById.GetValueOrDefault(id)?.Name ?? id, state.Society.Society.WorldTick) : null,
                    Governance = item.Governance is { } civic ? new ViewerTownGovernance(
                        civic.Form, civic.Fallback, civic.Members.Select(id => inhabitantsById.GetValueOrDefault(id)?.Name ?? id).ToArray(),
                        civic.TermEndTick, civic.RetryTick, civic.Candidates.Select(c =>
                            (inhabitantsById.GetValueOrDefault(c.AgentId)?.Name ?? c.AgentId) + (c.FullTerm ? " (full term)" : " (current vacancy only)")).ToArray(),
                        civic.Proposals.TakeLast(RecentCivicProposalLimit).Select(p => new ViewerCivicProposal(p.Id, p.Kind,
                            item.Government?.LawDrafts.SingleOrDefault(d => d.ProposalId == p.Id) is { } draft ? TownLawRules.VoteText(draft) :
                                p.Text + (p.LandClaimTiles is { } tiles ? " Exact tiles: " + TownLandClaimRules.DescribeTiles(tiles) + "." :
                                    p.Kind == "land_use" && state.HouseholdLandUseRequests?.SingleOrDefault(r => r.Id == p.SubjectId) is { } landRequest
                                        ? " Exact tiles: " + TownLandClaimRules.DescribeTiles(landRequest.Tiles) + ". " +
                                            LandRequestApprovalDetail(state, landRequest, landRequest.Tiles.Any(tile => TownLandRightsRules.IsDisputed(tile,
                                                state.HouseholdLandUseRights ?? [], state.HouseholdLandUseRequests ?? []))) : ""), p.Status,
                            p.Votes.Count(v => v.Yes), p.Votes.Count(v => !v.Yes), p.RequiredYes, p.DeadlineTick)
                        {
                            LandHearingRequest = p.LandHearingRequest is { } request ? ProjectLandHearingProposal(state, request) : null,
                            Project = p.Project is { } plan ? ProjectPlan(plan, p.AuthorId) : null,
                        }).ToArray(),
                        civic.Election is { } election ? ProjectElection(election) : null)
                    {
                        LatestElection = civic.ElectionHistory.Count > 0
                            ? ProjectElection(civic.ElectionHistory[^1]) : null,
                    } : null,
                    Projects = item.Projects.OrderBy(project => project.ApprovedTick)
                        .ThenBy(project => project.Id, StringComparer.Ordinal)
                        .Select(project => ProjectConstruction(item, project)).ToArray(),
                    Markets = item.Markets.OrderBy(market => market.Id, StringComparer.Ordinal)
                        .Select(ProjectMarket).ToArray(),
                })
                .ToArray(),
            TownLandTitles = (state.TownLandTitles ?? []).OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new ViewerTownLandTitle(item.Id, item.TownId,
                    item.Tiles.Select(ToPosition).ToArray(), item.RecordedTick)).ToArray(),
            HouseholdLandUseRights = (state.HouseholdLandUseRights ?? []).OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new ViewerHouseholdLandUseRight(item.Id, item.TownId, item.HouseholdId,
                    item.Tiles.Select(ToPosition).ToArray(), item.GrantedTick, item.GrantSource, item.AgreedEndTick))
                .ToArray(),
            HouseholdLandUseRequests = (state.HouseholdLandUseRequests ?? [])
                .Where(item => item.Status == "pending")
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item =>
                {
                    var unresolved = item.Tiles.Where(tile => !item.HearingResolutions.Any(receipt => receipt.Tiles.Contains(tile))).ToArray();
                    var claimants = unresolved.SelectMany(tile => TownLandRightsRules.ClaimantsAt(tile,
                            state.HouseholdLandUseRights ?? [], state.HouseholdLandUseRequests ?? []))
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                    var disputedTiles = unresolved.Where(tile => TownLandRightsRules.IsDisputed(tile,
                            state.HouseholdLandUseRights ?? [], state.HouseholdLandUseRequests ?? []))
                        .OrderBy(tile => tile.Y).ThenBy(tile => tile.X).ToArray();
                    return new ViewerHouseholdLandUseRequest(item.Id, item.TownId, item.HouseholdId,
                        item.RequestedByAgentId, unresolved.Select(ToPosition).ToArray(), item.RequestedTick,
                        item.AgreedEndTick, disputedTiles.Length > 0, claimants,
                        disputedTiles.Select(ToPosition).ToArray())
                    {
                        ApprovalDetail = LandRequestApprovalDetail(state, item, disputedTiles.Length > 0),
                    };
                }).ToArray(),
            RoadTiles = (state.RoadTiles ?? []).OrderBy(point => point.Y).ThenBy(point => point.X)
                .Select(ToPosition).ToArray(),
            Bridges = (state.Bridges ?? []).OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new ViewerBridge(item.Id, item.Design, item.Trigger,
                    RiverBridgeRules.AxisOf(item) == BridgeAxis.EastWest ? "east_west" : "north_south",
                    item.Entrances.Select(ToPosition).ToArray(), item.Span.Select(ToPosition).ToArray(),
                    item.BuiltTick))
                .ToArray(),
            WeatherRegions = state.WorldSystems is { } weatherSystems
                ? CreateWeatherRegions(weatherSystems, map)
                : [],
            WeatherRegionSize = WeatherRules.RegionSize,
            DarknessBasisPoints = state.WorldSystems is { } daylightSystems
                ? DaylightRules.DarknessBasisPoints(daylightSystems)
                : null,
            CalendarPace = state.WorldSystems is { } worldSystems
                ? new ViewerCalendarPace(worldSystems.Config.TicksPerDay, worldSystems.Config.DaysPerYear,
                    worldSystems.Config.SpringDays, worldSystems.Config.SummerDays,
                    worldSystems.Config.AutumnDays, worldSystems.Config.WinterDays,
                    worldSystems.Config.CalendarOffsetTicks)
                : null,
            Authoring = new ViewerAuthoringState(
                state.Society.Society.IsPaused,
                state.Society.Society.RunEpoch,
                state.EventHistoryFloor + state.Events.Count,
                0,
                map.ManifestDigest,
                map.ManifestDigest,
                campWeather.ToString().ToLowerInvariant(),
                state.WorldSystems?.Climate.Season.ToString().ToLowerInvariant() ?? "spring",
                []),
            Instructions = visibleInstructions
                .Select(instruction => new ViewerInstruction(
                    instruction.InstructionId,
                    instruction.TargetInhabitantId,
                    ToWireValue(instruction.Kind),
                    instruction.Text,
                    completedInstructionIds.Contains(instruction.InstructionId)
                        ? "completed" : ToWireValue(instruction.State),
                    instruction.SubmittedTick,
                    instruction.RunEpoch,
                    instruction.SubmissionSequence,
                    instruction.ObservedTick,
                    instruction.ObserverReply,
                    instruction.Order is { } order ? new ViewerInstructionOrder(
                        order.Action, order.Status, order.RequestedUnits, order.CompletedUnits,
                        order.ProgressUnit, order.RepeatUntilCancelled, order.TargetFoodKind,
                        order.TargetResourceId, order.TargetPosition?.X, order.TargetPosition?.Y,
                        order.BlockedReason, order.TargetAgentId, order.TargetMaterialKind, order.TargetEquipmentKind, order.TargetCropKind,
                        order.TargetOutputKind, order.TargetItemKind, order.TargetBuildingKind) : null))
                .ToArray(),
            Cognition = ToCognition(state),
            ContentPackages = state.Content?.Packages
                .OrderBy(package => package.Manifest.PackageId, StringComparer.Ordinal)
                .Select(package => new ViewerContentPackage(
                    package.Manifest.PackageId,
                    package.Manifest.Version.ToString(),
                    package.Manifest.PackageDigest,
                    package.Lifecycle.ToString().ToLowerInvariant(),
                    package.LockDigest,
                    package.ValidationTick,
                    package.StagedTick,
                    package.ActivationTick,
                    package.ManifestDigest,
                    package.Manifest.Definitions.Count == 0 ? null : package.Manifest.Definitions[0].DisplayName,
                    state.Content.Events.LastOrDefault(item => item.PackageId == package.Manifest.PackageId &&
                        item.Kind == "package_proposed_by_inhabitant")?.Detail))
                .ToArray() ?? [],
            ContentEvents = state.Content?.Events
                .OrderBy(item => item.EventId)
                .Select(item => new ViewerContentGovernanceEvent(
                    item.EventId,
                    item.WorldTick,
                    item.PackageId,
                    item.Kind,
                    item.Detail))
                .ToArray() ?? [],
            WorldSystems = state.WorldSystems is { } systems
                ? new ViewerWorldSystemsSummary(
                    systems.Climate.Season.ToString().ToLowerInvariant(),
                    campWeather.ToString().ToLowerInvariant(),
                    systems.Ecology.Resources.Count,
                    systems.Factions.Factions.Count,
                    systems.Currency.Accounts.Count,
                    systems.Culture.Cultures.Count,
                    systems.Chunks.Count,
                    state.WorldContent?.Buildings.Count ?? 0,
                    state.WorldContent?.Recipes.Count ?? 0,
                    state.WorldSimulation?.Buildings.Count ?? 0,
                    jobs.Length,
                    state.AssetReservations?.Reservations
                        .Select(item => item.NormalizedDigest)
                        .Distinct(StringComparer.Ordinal)
                        .Count() ?? 0,
                    state.AssetReservations is { } assetState
                        ? assetState.Reservations
                            .GroupBy(item => item.NormalizedDigest, StringComparer.Ordinal)
                            .Sum(group => group.First().DurableStorageBytes)
                        : 0,
                    state.AssetReservations is { } cacheState
                        ? cacheState.Reservations
                            .GroupBy(item => $"{item.NormalizedDigest}|{item.DecodeProfile}", StringComparer.Ordinal)
                            .Sum(group => group.First().DecodedCacheBytes)
                        : 0,
                    state.AssetReservations is { } gpuState
                        ? gpuState.Reservations
                            .GroupBy(item => $"{item.NormalizedDigest}|{item.DecodeProfile}", StringComparer.Ordinal)
                            .Sum(group => group.First().GpuBytes)
                        : 0,
                    state.AssetReservations?.Reservations.Sum(item => item.RenderUnits) ?? 0)
                : null,
            PlacedBuildings = state.WorldSimulation?.Buildings
                .OrderBy(item => item.InstanceId, StringComparer.Ordinal)
                .Select(item =>
                {
                    var definition = buildingDefinitions?.GetValueOrDefault(item.DefinitionId);
                    var width = item.Footprint?.Width ?? definition?.Width ?? 1;
                    var height = item.Footprint?.Height ?? definition?.Height ?? 1;
                    var residentCapacity = item.HouseholdId is { } residentHousehold &&
                        definition?.Tags.Contains("house", StringComparer.Ordinal) == true
                            ? HouseResidentCapacityRules.Calculate(
                                state.Society.Society.Inhabitants.Where(person => person.HouseholdId == residentHousehold),
                                width, height)
                            : null;
                    return new ViewerPlacedBuilding(
                    item.InstanceId,
                    item.DefinitionId,
                    ToPosition(item.Position),
                    item.PlacedTick,
                    definition?.DisplayName,
                    definition?.Tags,
                    width,
                    height,
                    item.TownId,
                    item.HouseholdId,
                    item.HouseholdId is { } householdId
                        ? InventoryFor(state, householdId, item.InstanceId)
                        : item.TownId is { } townId && buildingDefinitions?.GetValueOrDefault(item.DefinitionId)?
                            .Tags.Contains("warehouse", StringComparer.Ordinal) == true
                            ? InventoryFor(state, townId, item.InstanceId) : null,
                    item.Entrance is { } entrance ? ToPosition(entrance) : null,
                    buildingDefinitions?.GetValueOrDefault(item.DefinitionId) is { } storageDefinition
                        ? BuildingStorageRules.Capacity(storageDefinition, item) : null,
                    state.Society.Society.Inventory.Lots.Where(lot => lot.StorageBuildingId == item.InstanceId).Sum(lot => lot.Quantity),
                    item.Footprint?.Revision ?? 0,
                    (state.WorldSimulation.GuestInvitations ?? []).Where(invitation => invitation.HouseInstanceId == item.InstanceId && invitation.Active)
                        .Select(invitation => state.Society.Society.Inhabitants.Single(person => person.Id == invitation.GuestId).Name).ToArray(),
                    (state.WorldSimulation.BuildingExpansions ?? []).LastOrDefault(job => job.BuildingInstanceId == item.InstanceId)?.State.ToString().ToLowerInvariant(),
                    (state.WorldSimulation.BuildingExpansions ?? []).LastOrDefault(job => job.BuildingInstanceId == item.InstanceId)?.Failure,
                    residentCapacity?.Limit,
                    residentCapacity?.ResidentCount ?? 0,
                    residentCapacity?.HasDominantFamily ?? false,
                    residentCapacity?.IsOvercrowded ?? false)
                    {
                        Trades = BusinessTradesAt(state, item.InstanceId),
                        ToolMakingRequests = ToolMakingRequestsAt(state, item.InstanceId),
                        AllowsHouseholdOwner = definition?.Tags.Any(HouseholdBuildingKinds.IsKindTag) == true,
                        RecentStorageChanges = storageChanges.GetValueOrDefault(item.InstanceId) ?? [],
                    };
                })
                .ToArray() ?? [],
            ProductionJobs = jobs
                .OrderBy(item => item.JobId, StringComparer.Ordinal)
                .Select(item => new ViewerProductionJob(
                    item.JobId,
                    item.RecipeId,
                    item.BuildingInstanceId,
                    item.WorkerId,
                    item.StartedTick,
                    item.CompletionTick,
                    item.State.ToString().ToLowerInvariant()))
                .ToArray(),
        };
    }

    // Every open message stays visible. Closed messages are bounded to each
    // agent's newest few, whether or not a personal model heard them: an order
    // the game could not act on, or one done by local rules, still belongs on
    // the card. Orders and suggestions are bounded separately, so heard
    // suggestions never push the latest finished orders off the card. Newest
    // means latest submitted, the order the card reads them in.
    private static OwnerQueuedInstruction[] ProjectPrivateInstructions(
        PrivateWorldRuntimeState state,
        HashSet<string> completedInstructionIds) =>
        (state.Instructions ?? [])
            .GroupBy(instruction => (instruction.TargetInhabitantId, instruction.Kind))
            .SelectMany(group =>
            {
                var pending = group.Where(instruction => !completedInstructionIds.Contains(instruction.InstructionId));
                var recentClosed = group
                    .Where(instruction => completedInstructionIds.Contains(instruction.InstructionId))
                    .OrderByDescending(instruction => instruction.SubmissionSequence)
                    .Take(RecentClosedInstructionLimitPerAgent);
                return pending.Concat(recentClosed);
            })
            .OrderBy(instruction => instruction.SubmissionSequence)
            .ToArray();

    private static string ConversationStatus(AgentConversationStatus status) => status switch
    {
        AgentConversationStatus.Proposed => "proposed",
        AgentConversationStatus.Ready => "ready",
        AgentConversationStatus.AwaitingSpeaker => "awaiting_speaker",
        AgentConversationStatus.WrapUp => "wrap_up",
        AgentConversationStatus.Suspended => "suspended",
        AgentConversationStatus.Closed => "closed",
        _ => "unknown",
    };

    private static string ConversationInterruption(AgentConversationInterruption interruption) => interruption switch
    {
        AgentConversationInterruption.OwnerPaused => "owner_paused",
        AgentConversationInterruption.Disconnected => "disconnected",
        AgentConversationInterruption.UrgentNeed => "urgent_need",
        AgentConversationInterruption.ProviderUnavailable => "provider_unavailable",
        AgentConversationInterruption.ProviderTimedOut => "provider_timed_out",
        AgentConversationInterruption.ProviderRejected => "provider_rejected",
        AgentConversationInterruption.Restored => "restored",
        _ => "unknown",
    };

    internal static ViewerPackedTerrain PackTerrain(SeededMap map)
    {
        var bytes = new byte[checked(map.Width * map.Height)];
        foreach (var tile in map.Tiles)
            bytes[tile.Position.Y * map.Width + tile.Position.X] = checked((byte)tile.Terrain);
        return new ViewerPackedTerrain(map.Width, map.Height, "terrain-kind-v1",
            Convert.ToBase64String(bytes));
    }

    internal static ViewerPackedMapLayers? PackMapLayers(SeededMap map, string? worldSeed = null)
    {
        if (map.ClimateZones is not { } climate || map.ElevationLevels is not { } elevation ||
            map.HydrologyKinds is not { } hydrology || map.SurfaceKinds is not { } surface ||
            map.VegetationKinds is not { } vegetation) return null;
        var fertility = worldSeed is null ? null : new LandFertility(map, worldSeed);
        return new ViewerPackedMapLayers(map.Width, map.Height, "map-layers-v2",
            Convert.ToBase64String(climate), Convert.ToBase64String(elevation),
            Convert.ToBase64String(hydrology), Convert.ToBase64String(surface),
            Convert.ToBase64String(vegetation))
        {
            Fertility = fertility is null ? null : Convert.ToBase64String(map.Tiles.OrderBy(tile => tile.Position.Y)
                .ThenBy(tile => tile.Position.X).Select(tile => checked((byte)fertility.At(tile.Position))).ToArray()),
        };
    }

    private static ViewerWeatherRegion[] CreateWeatherRegions(WorldSystemsState systems, SeededMap map)
    {
        if (map.Height <= WeatherRules.RegionSize)
            return [new ViewerWeatherRegion(0, 0,
                WeatherRules.At(systems, new GridPoint(0, 0), map.Height).ToString().ToLowerInvariant(),
                WeatherRules.SoilMoistureAt(systems, new GridPoint(0, 0), map.Height))];
        var columns = (map.Width + WeatherRules.RegionSize - 1) / WeatherRules.RegionSize;
        var rows = (map.Height + WeatherRules.RegionSize - 1) / WeatherRules.RegionSize;
        return Enumerable.Range(0, rows)
            .SelectMany(y => Enumerable.Range(0, columns).Select(x => new ViewerWeatherRegion(x, y,
                WeatherRules.At(systems, new GridPoint(x * WeatherRules.RegionSize,
                        y * WeatherRules.RegionSize), map.Height,
                    WeatherRules.RegionClimate(map, new GridPoint(x * WeatherRules.RegionSize,
                        y * WeatherRules.RegionSize))).ToString().ToLowerInvariant(),
                WeatherRules.SoilMoistureAt(systems,
                    new GridPoint(x * WeatherRules.RegionSize, y * WeatherRules.RegionSize), map.Height,
                    WeatherRules.RegionClimate(map, new GridPoint(x * WeatherRules.RegionSize,
                        y * WeatherRules.RegionSize))))))
            .ToArray();
    }

    private static List<ViewerInhabitant> CreateInhabitants(OwnerWorldSnapshot state)
    {
        var inhabitants = new List<ViewerInhabitant>
        {
            ToProtectedActor(state),
        };
        inhabitants.AddRange(state.FounderDrafts
            .OrderBy(draft => draft.Id, StringComparer.Ordinal)
            .Select(ToFounderDraft));
        return inhabitants;
    }

    private static ViewerInhabitant ToProtectedActor(OwnerWorldSnapshot state)
    {
        var world = state.World;
        var actor = world.Actor;
        var route = DetermineFixtureRoute(world);
        var perceived = KnownNearby(world.Map, actor.Position).ToArray();
        var known = KnownFixtureTopology(actor.Position, perceived, route);
        var cognition = state.Cognition;
        var decisionFactors = new List<ViewerDecisionFactor>
        {
            new(
                "decision-source",
                cognition is null
                    ? "deterministic fixture"
                    : $"{cognition.ProviderKind.ToString().ToLowerInvariant()} provider"),
            new("hunger", $"{actor.HungerBasisPoints} basis points"),
            new("fixture-topology", world.Map.ManifestDigest),
        };
        if (cognition?.CurrentIntention is { } intention)
        {
            decisionFactors.Add(new ViewerDecisionFactor("current-intention", intention.CandidateId));
            decisionFactors.Add(new ViewerDecisionFactor("intention-provider", intention.Provider.ToString().ToLowerInvariant()));
        }

        return new ViewerInhabitant(
            actor.Id,
            "Scout",
            "active_fixture",
            ToPosition(actor.Position),
            actor.HungerBasisPoints,
            [
                new ViewerInventoryEntry("food", actor.FoodItems),
                new ViewerInventoryEntry("wood", actor.WoodItems),
            ],
            decisionFactors,
            route,
            new ViewerSpatialKnowledge(ToPosition(actor.Position), perceived, known),
            IsDraft: false)
        {
            PublicIntention = cognition?.CurrentIntention is { } publicIntention
                ? ToPublicIntention(publicIntention.CandidateId, publicIntention.Provider.ToString().ToLowerInvariant(), publicIntention.WorldTick)
                : null,
        };
    }

    private static ViewerInhabitant ToFounderDraft(OwnerFounderDraft draft) => new(
        draft.Id,
        draft.DisplayName,
        "authoring_draft",
        ToPosition(draft.Position),
        0,
        [],
        [
            new ViewerDecisionFactor("status", "paused authoring draft; not active in the protected fixture"),
            new ViewerDecisionFactor("created-revision", draft.CreatedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ],
        new ViewerRoute("not_active", null, null, [], string.Empty),
        new ViewerSpatialKnowledge(ToPosition(draft.Position), [ToPosition(draft.Position)], [ToPosition(draft.Position)]),
        IsDraft: true);

    private static ViewerInhabitant ToPlaytestInhabitant(
        PrivateWorldRuntimeState state,
        SocietyInhabitant inhabitant,
        PlaytestInhabitantState physical)
    {
        // A handcart and the goods inside it stay with the cart, not in its owner's hands.
        var cartIds = state.Society.Society.Inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.Handcart)
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        var inventory = state.Society.Society.Inventory.Lots.Where(lot =>
                PersonalEquipmentRules.IsPhysicallyCarried(state.Society.Society.Inventory, lot, inhabitant.Id) && !cartIds.Contains(lot.Id))
            .GroupBy(lot => lot.ItemKind).Select(group => new ViewerInventoryEntry(group.Key, group.Sum(lot => lot.Quantity))).ToArray();
        var route = DeterminePlaytestRoute(state, physical, inventory);
        var perceived = KnownNearby(state.Map, physical.Position).ToArray();
        var known = KnownFixtureTopology(physical.Position, perceived, route);
        var household = state.Society.Society.Households
            .FirstOrDefault(item => item.Id == inhabitant.HouseholdId);
        var decisionFactors = new List<ViewerDecisionFactor>
        {
            new("personality", physical.Personality),
            new("aspiration", physical.Aspiration),
            new("age-band", inhabitant.AgeBand.ToString().ToLowerInvariant()),
            new(state.Society.Society.Config.DayLifecycle is null ? "age-years" : "age-days",
                state.Society.Society.AgeAt(inhabitant, state.Society.Society.WorldTick).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("role", inhabitant.CurrentRole.ToString().ToLowerInvariant()),
            new("household", household?.Name ?? "unhoused"),
            new("hunger", $"{physical.HungerBasisPoints} basis points"),
        };
        var personalStored = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == inhabitant.Id &&
            !PersonalEquipmentRules.IsPhysicallyCarried(state.Society.Society.Inventory, lot, inhabitant.Id) &&
            !cartIds.Contains(lot.Id) && (lot.ContainerLotId is null || !cartIds.Contains(lot.ContainerLotId))).Sum(lot => lot.Quantity);
        var borrowed = state.Society.Society.Inventory.Lots.Where(lot => lot.CarrierId == inhabitant.Id && lot.OwnerId != inhabitant.Id).Sum(lot => lot.Quantity);
        decisionFactors.Add(new("personal-goods-awaiting-collection", $"{personalStored} units; ownership stays personal"));
        decisionFactors.Add(new("borrowed-goods", $"{borrowed} units; ownership stays with the lender"));
        if (physical.Departures?.Any(departure => departure.SharedProject is not null) == true)
            decisionFactors.Add(new("departed-household-work", "Previous work stays recorded with its original household."));
        var ownedBuildings = state.WorldSimulation!.Buildings.Where(building => building.HouseholdId == inhabitant.HouseholdId && inhabitant.HouseholdId is not null)
            .Select(building => building.InstanceId).ToHashSet(StringComparer.Ordinal);
        var pausedWork = state.WorldSimulation.ProductionJobs.Count(job => job.State == WorldProductionJobState.Paused && ownedBuildings.Contains(job.BuildingInstanceId)) +
            (state.WorldSimulation.BuildingExpansions ?? []).Count(job => job.State == WorldProductionJobState.Paused && ownedBuildings.Contains(job.BuildingInstanceId));
        if (pausedWork > 0) decisionFactors.Add(new("paused-household-work", $"{pausedWork} jobs; members can take over at the site when the committed inputs are available to them."));
        if (inhabitant.PrimaryCaregiverId is { } primary)
            decisionFactors.Add(new("primary-caregiver", state.Society.Society.GetInhabitant(primary).Name));
        var dependents = SocietyFixture.MovingCareGroup(state.Society.Society, inhabitant.Id).Where(id => id != inhabitant.Id)
            .Select(id => state.Society.Society.GetInhabitant(id).Name).ToArray();
        if (dependents.Length > 0) decisionFactors.Add(new("dependent-care", string.Join(", ", dependents)));
        var guardianNotes = GuardianCareNotes(state, inhabitant, physical).ToArray();
        decisionFactors.AddRange(guardianNotes.Select(note => new ViewerDecisionFactor("guardian-care", note)));
        if (HousingDetail(state, inhabitant, physical.Housing) is { } housingDetail)
            decisionFactors.Add(new ViewerDecisionFactor("housing", housingDetail));
        if (state.Knowledge?.WritingProjects.SingleOrDefault(project => project.ActorId == inhabitant.Id) is { } writing)
        {
            var kind = writing.Kind.Replace('_', ' ');
            var action = writing.SourceArtifactId is not null ? "Copying" : writing.Kind == "field_map" ? "Drawing" : "Writing";
            decisionFactors.Add(new ViewerDecisionFactor("knowledge-writing",
                $"{action} a {kind} · {writing.WorkDone}/{writing.WorkRequired}"));
        }
        if (TownMembershipText.Describe(state.Towns ?? [], state.Society.Society, inhabitant.Id,
                state.WorldSystems!.Config.TicksPerDay,
                TownMembershipText.TownsWithWarehouse(state.WorldSimulation, state.WorldContent!),
                calendarOffsetTicks: state.WorldSystems.Config.CalendarOffsetTicks) is { } townMembership)
            decisionFactors.Add(new ViewerDecisionFactor("town-membership", townMembership)
            {
                AcceptanceDeadlineTick = TownMembershipText.AcceptanceDeadline(state.Towns ?? [],
                    state.Society.Society, inhabitant.Id, state.WorldSystems!.Config.TicksPerDay),
            });
        decisionFactors.AddRange(IdentityMomentFactors(physical));
        if (physical.ChildModelSelection is { Provider: { } birthProvider } birthModel)
        {
            decisionFactors.Add(new ViewerDecisionFactor("birth-model-provider", birthProvider));
            if (birthModel.ModelId is { } modelId)
                decisionFactors.Add(new ViewerDecisionFactor("birth-model-id", modelId));
        }
        var runtime = state.Society.Cognition.Runtimes
            .FirstOrDefault(item => item.InhabitantId == inhabitant.Id);
        var modelStatus = physical.LastModelAttempt?.Status ?? "ready";
        if (modelStatus == "waiting" && state.Society.Society.IsPaused) modelStatus = "canceled";
        decisionFactors.Add(new ViewerDecisionFactor("model-status", modelStatus));
        if (physical.LastModelAttempt?.LastAcceptedCandidateId is { } acceptedCandidate)
            decisionFactors.Add(new ViewerDecisionFactor("last-model-choice", acceptedCandidate));
        if (physical.LastModelAttempt?.SetupBlocker is { } setupBlocker)
            decisionFactors.Add(new ViewerDecisionFactor("model-setup-blocker", setupBlocker));
        if (state.Society.Cognition.Queue.Any(item => item.InhabitantId == inhabitant.Id))
            decisionFactors.Add(new ViewerDecisionFactor("decision-pending", "true"));
        if (runtime?.CurrentIntention is { } intention)
        {
            decisionFactors.Add(new ViewerDecisionFactor("current-intention", intention.CandidateId));
            decisionFactors.Add(new ViewerDecisionFactor("intention-provider", intention.Provider.ToString().ToLowerInvariant()));
        }

        return new ViewerInhabitant(
            inhabitant.Id,
            inhabitant.Name,
            inhabitant.Status.ToString().ToLowerInvariant(),
            ToPosition(physical.Position),
            physical.HungerBasisPoints,
            inventory,
            decisionFactors,
            route,
            new ViewerSpatialKnowledge(ToPosition(physical.Position), perceived, known),
            IsDraft: false)
        {
            PublicIntention = runtime?.CurrentIntention is { } publicIntention
                ? ToPublicIntention(publicIntention.CandidateId, publicIntention.Provider.ToString().ToLowerInvariant(), publicIntention.WorldTick)
                : null,
            Relationships = RelationshipsFor(state, inhabitant.Id),
            RecentPrivateThoughts = (physical.RecentThoughts ?? [])
                .Select(thought => new ViewerPrivateThought(thought.WorldTick, thought.Text)).ToArray(),
            RecentMemories = MemoriesFor(state, inhabitant.Id),
            RecentBeliefs = BeliefsFor(state, inhabitant.Id),
            RecentKnowledgeFacts = KnowledgeFactsFor(state, inhabitant.Id),
            KnowledgeArtifacts = KnowledgeArtifactsFor(state, inhabitant.Id),
            Project = physical.Project is { } project
                ? new ViewerProject(project.Label, project.Stage, project.WorkDone, 10, project.Blocker, project.StartedTick)
                : null,
            Survival = physical.Survival is { } survival
                ? new ViewerSurvival(survival.WarmthBasisPoints, survival.IllnessBasisPoints,
                    PersonalEquipmentRules.EquippedUnit(state.Society.Society.Inventory, inhabitant.Id, physical.Equipment?.ClothingLotId) is
                    { ConditionBasisPoints: > 0 },
                    inventory.Any(item => item.Kind == "tool" && item.Quantity > 0), survival.NutritionBasisPoints, survival.LastMealKind) : null,
            Equipment = EquipmentFor(state, physical),
            MedicalCareNote = inhabitant.Status == SocietyInhabitantStatus.Active ? MedicalCareRules.Note(physical) : null,
            ToolMakingRequestNote = inhabitant.Status == SocietyInhabitantStatus.Active
                ? ToolMakingRequestRules.Note(state.ToolMakingRequests ?? [], inhabitant.Id, inhabitant.HouseholdId) : null,
            Lesson = physical.Lesson is { } lesson ? new ViewerLesson(
                state.Society.Society.GetInhabitant(lesson.TeacherId).Name, lesson.Skill.ToString().ToLowerInvariant(),
                lesson.Stage, lesson.Progress, 20) : null,
            Proficiency = physical.Proficiency is { } practice
                ? new ViewerProficiency(practice.Building, practice.Farming, practice.Crafting) : null,
            Skills = ProjectSkills(physical, state.Society.Society),
            SocialStanding = SocialStandingFor(state, inhabitant.Id, physical),
            SocialNotes = state.Society.Society.Inventory.Offers.Where(offer => offer.State == DirectBarterState.Open &&
                    (offer.FirstPartyId == inhabitant.Id || offer.SecondPartyId == inhabitant.Id))
                .Select(offer => offer.AcceptedBy.Contains(inhabitant.Id, StringComparer.Ordinal)
                    ? "Waiting for the other inhabitant to accept or decline an exchange."
                    : "An exchange is offered; acceptance or refusal is still undecided.")
                .Concat(BusinessTradeNotes(state, inhabitant))
                .Concat(MarriageNotes(state, inhabitant.Id))
                .Concat(state.Inhabitants.Where(person => person.Parenthood is { } plan &&
                    (person.InhabitantId == inhabitant.Id || plan.PartnerId == inhabitant.Id)).Select(person =>
                    person.Parenthood!.Stage == "preparing" ? ContinuityPlanDue(state, person.InhabitantId, person.Parenthood.PartnerId)
                        ? "Preparing for parenthood under the continuity rule. " +
                            PrivateWorldRuntime.ParenthoodFoodNote(state.Society.Society, person.Parenthood.PrimaryCaregiverId!)
                        : "Preparing for parenthood. " +
                            PrivateWorldRuntime.ParenthoodFoodNote(state.Society.Society, person.Parenthood.PrimaryCaregiverId!)
                    : person.Parenthood.Stage == "requested" ? "Parenthood proposed; waiting for a separate decision."
                    : person.Parenthood.Stage == "postponed" ? "Parenthood put off for now."
                    : person.Parenthood.Stage == "completed" ? "Caring for a child in the household." : "Parenthood plan withdrawn."))
                .Concat(guardianNotes)
                .Concat(HousingRequestNotes(state, inhabitant))
                .ToArray(),
        };
    }

    /// <summary>The continuity rule has sent this couple's plan ahead; their two days of "not yet" are up.</summary>
    private static bool ContinuityPlanDue(PrivateWorldRuntimeState state, string owner, string partner) =>
        state.Continuity?.Couples.Any(couple => couple.DeadlineTick <= state.Society.Society.WorldTick &&
            (couple.FirstPartnerId == owner && couple.SecondPartnerId == partner ||
             couple.FirstPartnerId == partner && couple.SecondPartnerId == owner)) == true;

    private static ViewerToolMakingRequest[] ToolMakingRequestsAt(PrivateWorldRuntimeState state, string building) =>
        (state.ToolMakingRequests ?? []).Where(request => request.BuildingInstanceId == building)
            .OrderBy(request => ToolMakingRequestRules.IsTerminal(request.Status))
            .ThenByDescending(request => request.LastTransitionTick).ThenBy(request => request.Id, StringComparer.Ordinal).Take(8)
            .Select(request => new ViewerToolMakingRequest(request.Id,
                state.Society.Society.GetInhabitant(request.RequesterId).Name, request.RecipeId,
                state.WorldContent?.Recipes.FirstOrDefault(recipe => recipe.CanonicalId == request.RecipeId)?.DisplayName
                    ?? request.ItemKind.Replace('_', ' '),
                request.ItemKind, request.Status.ToString().ToLowerInvariant(), request.Blocker, request.OfferId)).ToArray();
    private static ViewerBusinessTrade[] BusinessTradesAt(PrivateWorldRuntimeState state, string buildingId) =>
        (state.BusinessTrades ?? []).Where(trade => trade.BuildingInstanceId == buildingId)
            .OrderByDescending(trade => state.Society.Society.Inventory.GetOffer(trade.OfferId).State == DirectBarterState.Open)
            .ThenByDescending(trade => trade.ProposedTick).ThenBy(trade => trade.OfferId, StringComparer.Ordinal)
            .Take(8).Select(trade =>
            {
                var offer = state.Society.Society.Inventory.GetOffer(trade.OfferId);
                return new ViewerBusinessTrade(trade.OfferId, state.Society.Society.GetInhabitant(trade.BuyerId).Name,
                    trade.GoodsKind, offer.FirstQuantity, trade.PaymentKind, offer.SecondQuantity,
                    offer.State.ToString().ToLowerInvariant(), trade.CancellationReason);
            }).ToArray();

    private static IEnumerable<string> BusinessTradeNotes(PrivateWorldRuntimeState state, SocietyInhabitant person)
    {
        foreach (var trade in (state.BusinessTrades ?? []).Where(trade =>
                     trade.BuyerId == person.Id || trade.SellerHouseholdId == person.HouseholdId)
                     .OrderByDescending(trade => state.Society.Society.Inventory.GetOffer(trade.OfferId).State == DirectBarterState.Open)
                     .ThenByDescending(trade => trade.ProposedTick).ThenBy(trade => trade.OfferId, StringComparer.Ordinal).Take(4))
        {
            var offer = state.Society.Society.Inventory.GetOffer(trade.OfferId);
            var building = state.WorldSimulation?.Buildings.FirstOrDefault(item => item.InstanceId == trade.BuildingInstanceId);
            var name = state.WorldContent?.Buildings.FirstOrDefault(item => item.CanonicalId == building?.DefinitionId)?.DisplayName ?? "shop";
            var terms = $"{offer.FirstQuantity} {trade.GoodsKind.Replace('_', ' ')} for {offer.SecondQuantity} {trade.PaymentKind.Replace('_', ' ')}";
            yield return offer.State switch
            {
                DirectBarterState.Open => $"Exchange at the {name}: {terms}. Both traders must meet there; goods are set aside until then.",
                DirectBarterState.Settled => $"Bought at the {name}: {terms}. The buyer carries the purchase; payment is stored at the shop.",
                _ => $"Exchange at the {name} cancelled: {trade.CancellationReason}",
            };
        }
    }

    /// <summary>Accepted care and a pending move are separate from needing a guardian.</summary>
    private static IEnumerable<string> GuardianCareNotes(
        PrivateWorldRuntimeState state, SocietyInhabitant inhabitant, PlaytestInhabitantState physical)
    {
        if (physical.GuardianSearch is not null)
            yield return "Needs a guardian. No adult has accepted care yet; nearby adults may still feed them.";
        else if (inhabitant.AgeBand is SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent &&
            !state.Society.Society.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                edge.State == SocietyRelationshipState.Accepted && edge.TargetId == inhabitant.Id &&
                state.Society.Society.GetInhabitant(edge.ProposerId).Status == SocietyInhabitantStatus.Active))
            yield return "No active caregiver; household adults may offer support.";

        foreach (var child in state.Inhabitants.OrderBy(person => person.InhabitantId, StringComparer.Ordinal))
        {
            if (child.GuardianPlacement is not { } placement ||
                child.InhabitantId != inhabitant.Id && placement.CaregiverId != inhabitant.Id)
                continue;
            var guardianName = state.Society.Society.GetInhabitant(placement.CaregiverId).Name;
            var childName = state.Society.Society.GetInhabitant(child.InhabitantId).Name;
            var hasBlocker = !string.IsNullOrWhiteSpace(placement.Blocker);
            var progress = hasBlocker ? "The move is waiting."
                : placement.HouseId is null ? "Waiting for a suitable home."
                : placement.Stage == "escorting" ? $"They are travelling together to {guardianName}'s House."
                : $"{guardianName} is going to collect {childName}.";
            var blocker = !hasBlocker
                ? string.Empty : " " + placement.Blocker!.TrimEnd('.') + ".";
            yield return $"{guardianName} accepted care for {childName}. {progress}{blocker}";
        }
    }

    /// <summary>The current housing need, including overcrowding and a saved move-out notice.</summary>
    private static string? HousingDetail(PrivateWorldRuntimeState state, SocietyInhabitant inhabitant,
        SettlementHousing? housing)
    {
        if (housing is not null && (housing.Relocation is not null || housing.Blocker == HousingBlockers.Overcrowded))
            return OvercrowdedHousingDetail(state, inhabitant, housing);
        if (housing?.Blocker is not { } blocker)
            return null;
        var asked = housing.Request is { } request
            ? state.Society.Society.Households.FirstOrDefault(item => item.Id == request.HouseholdId)?.Name ?? "another"
            : "another";
        return blocker switch
        {
            HousingBlockers.AwaitingAnswer => $"No home yet. Asked the {asked} household to live in their House; every adult member must agree.",
            HousingBlockers.NoHousehold => "No home. Seek an accepting household with room for the complete care group first; otherwise start a household and build a House.",
            HousingBlockers.NoAuthorizedHome => "No home. The household holds no House yet and can plan one.",
            HousingBlockers.MissingMaterials => "No home. The household holds no House and lacks the materials to build one.",
            HousingBlockers.NoLegalSite => "No home. The household has the materials for a House but no legal site to build it.",
            _ => null,
        };
    }

    private static string OvercrowdedHousingDetail(PrivateWorldRuntimeState state, SocietyInhabitant inhabitant,
        SettlementHousing housing)
    {
        var society = state.Society.Society;
        var house = state.WorldSimulation!.Buildings.FirstOrDefault(building =>
            inhabitant.HouseholdId is not null && building.HouseholdId == inhabitant.HouseholdId &&
            state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains("house", StringComparer.Ordinal)));
        var counts = "Housing need.";
        var construction = string.Empty;
        if (house is not null)
        {
            var definition = state.WorldContent!.Buildings.Single(item => item.CanonicalId == house.DefinitionId);
            var capacity = HouseResidentCapacityRules.Calculate(society.Inhabitants.Where(person =>
                    person.HouseholdId == inhabitant.HouseholdId),
                house.Footprint?.Width ?? definition.Width, house.Footprint?.Height ?? definition.Height);
            counts = $"{(capacity.IsOvercrowded ? "House overcrowded" : "House")}: {capacity.ResidentCount} residents, {capacity.Limit} places.";
            construction = (state.WorldSimulation.BuildingExpansions ?? [])
                .LastOrDefault(job => job.BuildingInstanceId == house.InstanceId)?.State switch
            {
                WorldProductionJobState.Running => " Expansion is under way; it adds places only when finished.",
                WorldProductionJobState.Paused => " Expansion is paused; it adds places only when finished.",
                WorldProductionJobState.Cancelled => " The last expansion was cancelled.",
                _ => string.Empty,
            };
        }
        if (housing.Relocation is { } notice)
        {
            var hours = (long)Math.Ceiling(Math.Max(0, notice.DeadlineTick - society.WorldTick) *
                24d / state.WorldSystems!.Config.TicksPerDay);
            var reason = notice.Reason switch
            {
                HouseRelocationRules.Volunteer => "Volunteered to move",
                HouseRelocationRules.LatestUnrelatedArrival => "Notice issued as the most recent arrival outside the main family",
                _ => "Notice issued as the most recent arrival when no family had a majority",
            };
            var next = housing.Request is { } request
                ? $" Asked the {society.Households.FirstOrDefault(item => item.Id == request.HouseholdId)?.Name ?? "other"} household; every adult member must agree."
                : " Seek an accepting household with room or start a household and build a House.";
            return $"{counts} Move-out notice: about {hours} world {(hours == 1 ? "hour" : "hours")} left. {reason}." +
                next + construction;
        }
        var notices = state.Inhabitants.Count(person => person.Housing?.Relocation is { } relocation &&
            relocation.HouseholdId == inhabitant.HouseholdId);
        return counts + (notices > 0
            ? $" {notices} {(notices == 1 ? "adult has" : "adults have")} notice to move out; completed expansion may let them stay."
            : " Nobody has notice to move out. Seek a feasible expansion or a voluntary household split that preserves dependent care.") + construction;
    }

    /// <summary>Requests to live in this adult's House that they must answer, or have answered.</summary>
    private static IEnumerable<string> HousingRequestNotes(PrivateWorldRuntimeState state, SocietyInhabitant member)
    {
        if (member.HouseholdId is null)
            yield break;
        foreach (var applicant in state.Inhabitants.OrderBy(person => person.InhabitantId, StringComparer.Ordinal))
        {
            if (applicant.Housing?.Request is not { } request || request.HouseholdId != member.HouseholdId ||
                !request.Members.Contains(member.Id, StringComparer.Ordinal))
                continue;
            var name = state.Society.Society.GetInhabitant(applicant.InhabitantId).Name;
            yield return request.Approvals.Contains(member.Id, StringComparer.Ordinal)
                ? $"Agreed to let {name} live in the House; waiting for the other adults."
                : request.Rejections.Contains(member.Id, StringComparer.Ordinal)
                    ? $"Refused {name}'s request to live in the House."
                    : $"{name} asked to live in the household's House. Every adult member must answer.";
        }
    }

    private static ViewerSkill[] ProjectSkills(PlaytestInhabitantState physical, SocietyCheckpoint society) =>
        (physical.Skills ?? []).Select(skill => new ViewerSkill(skill.Kind.ToString().ToLowerInvariant(),
            skill.LearnedTick, skill.TeacherId,
            skill.TeacherId is { } teacher ? society.GetInhabitant(teacher).Name : null)).ToArray();

    private static ViewerInhabitant ToDeceasedInhabitant(
        PrivateWorldRuntimeState state,
        SocietyInhabitant inhabitant,
        PlaytestDeceasedInhabitantState archived)
    {
        var lastPhysical = archived.LastPhysical;
        var position = ToPosition(lastPhysical.Position);
        var estate = state.Society.Society.Estates.FirstOrDefault(item => item.DeceasedId == inhabitant.Id);
        return new ViewerInhabitant(
            inhabitant.Id,
            inhabitant.Name,
            "dead",
            position,
            lastPhysical.HungerBasisPoints,
            [],
            new ViewerDecisionFactor[]
            {
                new("personality", lastPhysical.Personality),
                new("aspiration", lastPhysical.Aspiration),
                new("age-band", inhabitant.AgeBand.ToString().ToLowerInvariant()),
                new(state.Society.Society.Config.DayLifecycle is null ? "age-years" : "age-days",
                    archived.AgeAtDeath.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("role", inhabitant.CurrentRole.ToString().ToLowerInvariant()),
                new("death-tick", archived.DeathTick.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("death-cause", inhabitant.DeathCause?.ToString().ToLowerInvariant() ?? "unknown"),
                new("will-status", estate?.WillStatus ?? "not_requested"),
            }.Concat(IdentityMomentFactors(lastPhysical)).ToArray(),
            new ViewerRoute("deceased", null, null, [], string.Empty),
            new ViewerSpatialKnowledge(position, [position], [position]),
            IsDraft: false)
        {
            Relationships = RelationshipsFor(state, inhabitant.Id),
            RecentPrivateThoughts = (lastPhysical.RecentThoughts ?? [])
                .Select(thought => new ViewerPrivateThought(thought.WorldTick, thought.Text)).ToArray(),
            RecentMemories = MemoriesFor(state, inhabitant.Id),
            RecentBeliefs = BeliefsFor(state, inhabitant.Id),
            RecentKnowledgeFacts = KnowledgeFactsFor(state, inhabitant.Id),
            KnowledgeArtifacts = KnowledgeArtifactsFor(state, inhabitant.Id),
            Proficiency = lastPhysical.Proficiency is { } practice
                ? new ViewerProficiency(practice.Building, practice.Farming, practice.Crafting) : null,
            Skills = ProjectSkills(lastPhysical, state.Society.Society),
            SocialStanding = SocialStandingFor(state, inhabitant.Id, lastPhysical),
            SocialNotes = MarriageNotes(state, inhabitant.Id).ToArray(),
            FinalWill = estate is { WillStatus: { } status } ? FinalWillFor(state, estate, status) : null,
        };
    }

    /// <summary>The will as written: each named heir's exact goods, not later fallbacks.</summary>
    private static ViewerFinalWill FinalWillFor(PrivateWorldRuntimeState state, SocietyEstate estate, string status)
    {
        var frozen = (estate.FrozenLots ?? []).ToDictionary(item => item.LotId, StringComparer.Ordinal);
        var heirs = (estate.WillHeirIds ?? []).Select(heirId =>
        {
            var town = (state.Towns ?? []).FirstOrDefault(item => item.Id == heirId);
            var items = (estate.WillBequests ?? []).Where(item => item.HeirId == heirId && frozen.ContainsKey(item.LotId))
                .GroupBy(item => frozen[item.LotId].ItemKind, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ViewerInventoryEntry(group.Key, group.Sum(item => item.Quantity)))
                .ToArray();
            return new ViewerWillHeir(heirId,
                town?.Name ?? state.Society.Society.Inhabitants.FirstOrDefault(item => item.Id == heirId)?.Name ?? heirId,
                town is not null, items);
        }).ToArray();
        return new ViewerFinalWill(status, estate.WillSplit, heirs, estate.FinalWords);
    }

    private static ViewerAgentMemory[] MemoriesFor(PrivateWorldRuntimeState state, string ownerId) =>
        state.Society.Society.Memories
            .Where(memory => memory.OwnerId == ownerId && memory.TombstonedTick is null)
            .OrderByDescending(memory => memory.SourceTick)
            .ThenBy(memory => memory.Id, StringComparer.Ordinal)
            .Take(16)
            .Select(memory => new ViewerAgentMemory(
                memory.SourceTick,
                memory.SubjectId,
                state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == memory.SubjectId)?.Name ?? memory.SubjectId,
                memory.Summary,
                memory.Visibility))
            .ToArray();

    private static ViewerAgentBelief[] BeliefsFor(PrivateWorldRuntimeState state, string ownerId) =>
        (state.Society.Society.Beliefs ?? [])
            .Where(belief => belief.OwnerId == ownerId)
            .OrderByDescending(belief => belief.FormedTick)
            .ThenBy(belief => belief.Id, StringComparer.Ordinal)
            .Take(16)
            .Select(belief => new ViewerAgentBelief(
                belief.FormedTick,
                belief.Statement,
                belief.Provenance.ToString().ToLowerInvariant(),
                belief.ConfidenceBasisPoints,
                belief.SourceAgentId,
                belief.SourceAgentId is { } sourceId
                    ? state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == sourceId)?.Name ?? sourceId
                    : null,
                belief.SourceEventId,
                belief.AboutInhabitantId,
                belief.SupersededByBeliefId is not null,
                belief.SupersededTick))
            .ToArray();

    private static ViewerAgentKnowledgeFact[] KnowledgeFactsFor(PrivateWorldRuntimeState state, string ownerId)
    {
        var names = state.Society.Society.Inhabitants.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        return (state.Knowledge?.Facts ?? []).Where(fact => fact.OwnerId == ownerId)
            .OrderByDescending(fact => fact.LearnedTick)
            .ThenBy(fact => fact.Id, StringComparer.Ordinal)
            .Take(16)
            .Select(fact => new ViewerAgentKnowledgeFact(
                fact.LearnedTick,
                fact.Position.X,
                fact.Position.Y,
                fact.Terrain,
                fact.ResourceKinds,
                names.GetValueOrDefault(fact.DiscovererId, fact.DiscovererId),
                fact.Acquisition,
                fact.SourceAgentId is { } sourceId ? names.GetValueOrDefault(sourceId, sourceId) : null))
            .ToArray();
    }

    private static ViewerAgentKnowledgeArtifact[] KnowledgeArtifactsFor(PrivateWorldRuntimeState state, string ownerId)
    {
        var names = state.Society.Society.Inhabitants.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        var heldLotIds = state.Society.Society.Inventory.Lots.Where(lot => lot.OwnerId == ownerId)
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        return (state.Knowledge?.Artifacts ?? []).Where(artifact => heldLotIds.Contains(artifact.LotId))
            .OrderByDescending(artifact => artifact.CreatedTick)
            .ThenBy(artifact => artifact.Id, StringComparer.Ordinal)
            .Take(AgentKnowledgeArtifactLimit)
            .Select(artifact => new ViewerAgentKnowledgeArtifact(
                artifact.Id,
                artifact.Kind,
                artifact.Title,
                artifact.CreatedTick,
                names.GetValueOrDefault(artifact.CreatorId, artifact.CreatorId),
                artifact.Facts.Select(fact => new ViewerKnowledgeSite(
                    fact.Position.X,
                    fact.Position.Y,
                    fact.Terrain,
                    fact.ResourceKinds,
                    names.GetValueOrDefault(fact.DiscovererId, fact.DiscovererId))).ToArray()))
            .ToArray();
    }

    private static ViewerSocialStanding[] SocialStandingFor(
        PrivateWorldRuntimeState state,
        string ownerId,
        PlaytestInhabitantState physical)
    {
        var saved = (physical.SocialStanding ?? []).ToDictionary(item => item.SubjectId, item => item.Trust, StringComparer.Ordinal);
        return state.Society.Society.Inhabitants.Where(subject => subject.Id != ownerId)
            .Select(subject => new ViewerSocialStanding(subject.Id, subject.Name,
                saved.GetValueOrDefault(subject.Id, LegacyTrustScore(state, ownerId, subject.Id))))
            .Where(item => item.Trust > 0)
            .OrderByDescending(item => item.Trust).ThenBy(item => item.SubjectId, StringComparer.Ordinal).ToArray();
    }

    private static int LegacyTrustScore(PrivateWorldRuntimeState state, string ownerId, string subjectId) =>
        Math.Min(10, state.Society.Society.Memories.Where(memory => memory.OwnerId == ownerId &&
                memory.SubjectId == subjectId && memory.TombstonedTick is null)
            .Sum(memory => memory.Id.StartsWith("project-gratitude:", StringComparison.Ordinal) ? 2
                : memory.Id.StartsWith("lesson-gratitude:", StringComparison.Ordinal) ? 2
                : memory.Id.StartsWith("settlement-trust:", StringComparison.Ordinal) ? 1 : 0));

    private static ViewerInhabitantRelationship[] RelationshipsFor(
        PrivateWorldRuntimeState state,
        string inhabitantId) => state.Society.Society.Relationships
        .Where(relationship =>
            (relationship.ProposerId == inhabitantId || relationship.TargetId == inhabitantId) &&
            relationship.State is SocietyRelationshipState.Proposed or SocietyRelationshipState.Accepted or SocietyRelationshipState.EndedByDeath)
        .OrderBy(relationship => relationship.Type)
        .ThenBy(relationship => relationship.Id, StringComparer.Ordinal)
        .Select(relationship => new ViewerInhabitantRelationship(
            relationship.Id,
            relationship.ProposerId == inhabitantId ? relationship.TargetId : relationship.ProposerId,
            ToWireValue(relationship.Type),
            ToWireValue(relationship.State),
            relationship.PrivacyClass,
            relationship.EffectiveTick,
            relationship.Type == SocietyRelationshipType.BiologicalParentage
                ? relationship.ProposerId == inhabitantId ? "parent" : "child"
                : relationship.Type == SocietyRelationshipType.Partnership ? "partner" : null))
        .ToArray();

    private static IEnumerable<string> MarriageNotes(PrivateWorldRuntimeState state, string agentId) =>
        state.Marriages.Where(marriage => AgentMarriageRules.HasParticipant(marriage, agentId))
            .Select(marriage => AgentMarriageRules.Note(marriage, agentId, state.Society.Society));

    private static ViewerPublicIntention ToPublicIntention(
        string candidateId,
        string provider,
        long worldTick) => new(
        candidateId,
        PublicIntentionSummary(candidateId),
        provider,
        worldTick);

    private static string PublicIntentionSummary(string candidateId) => candidateId switch
    {
        "seek_food" => "looking for food",
        "move_to" => "walking to the ordered tile",
        "harvest_food" => "gathering food",
        "gather_material" => "gathering the ordered material",
        "work_field" => "working on a household field",
        "repair_tool" => "repairing a personal tool",
        "repair_equipment" => "repairing personal equipment",
        "collect_material" => "collecting personal materials",
        "collect_food" => "collecting personal food",
        "collect_equipment" => "collecting personal equipment",
        "store_material" => "storing personal materials in the House",
        "store_equipment" => "storing personal equipment in the House",
        "inspect_material_site" => "checking the ordered material site",
        "consume_food" => "eating carried food",
        "safe_idle" => "keeping a safe routine",
        _ when candidateId.StartsWith("guardian_relocate:", StringComparison.Ordinal) => "bringing a child home",
        _ when candidateId.StartsWith("guardian_follow:", StringComparison.Ordinal) => "following their guardian home",
        _ => candidateId.Replace('_', ' '),
    };

    private static string ToWireValue(SocietyRelationshipType type) => type switch
    {
        SocietyRelationshipType.Partnership => "partnership",
        SocietyRelationshipType.Caregiver => "caregiver",
        SocietyRelationshipType.HouseholdMembership => "household_membership",
        SocietyRelationshipType.BiologicalParentage => "biological_parentage",
        SocietyRelationshipType.LegalGuardian => "legal_guardian",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string ToWireValue(SocietyRelationshipState state) => state switch
    {
        SocietyRelationshipState.Proposed => "proposed",
        SocietyRelationshipState.Accepted => "accepted",
        SocietyRelationshipState.Rejected => "rejected",
        SocietyRelationshipState.Revoked => "revoked",
        SocietyRelationshipState.Dissolved => "dissolved",
        SocietyRelationshipState.EndedByDeath => "ended_by_death",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static IEnumerable<ViewerDecisionFactor> IdentityMomentFactors(PlaytestInhabitantState physical) =>
        (physical.IdentityMoments ?? []).Where(moment => moment.Outcome == "accepted")
            .Select(moment => new ViewerDecisionFactor("identity-change", moment.Reason + ": " +
                string.Join("; ", new[]
                {
                    moment.Personality is null ? null : "Personality: " + moment.Personality,
                    moment.Aspiration is null ? null : "Aspiration: " + moment.Aspiration,
                }.Where(text => text is not null))));

    private static ViewerEquipment EquipmentFor(PrivateWorldRuntimeState state, PlaytestInhabitantState person)
    {
        var inventory = state.Society.Society.Inventory;
        var garment = PersonalEquipmentRules.EquippedUnit(inventory, person.InhabitantId, person.Equipment?.ClothingLotId);
        var aid = PersonalEquipmentRules.EquippedUnit(inventory, person.InhabitantId, person.Equipment?.CarryAidLotId);
        var ornament = PersonalEquipmentRules.EquippedUnit(inventory, person.InhabitantId, person.Equipment?.OrnamentLotId);
        var repair = person.Equipment?.Repair;
        return new(PersonalEquipmentRules.CarriedQuantity(inventory, person.InhabitantId, person.Equipment),
            PersonalEquipmentRules.Capacity(inventory, person.InhabitantId, person.Equipment),
            garment?.ItemKind, garment?.ConditionBasisPoints / 100, aid?.ItemKind, aid?.ConditionBasisPoints / 100,
            inventory.Lots.FirstOrDefault(lot => lot.Id == repair?.LotId)?.ItemKind,
            repair?.WorkDone ?? 0, PersonalEquipmentRules.RepairWorkTicks, ornament?.ItemKind);
    }

    private static string BoatPassengerName(PrivateWorldRuntimeState state, string id) =>
        state.Society.Society.Inhabitants.Single(person => person.Id == id).Name;

    private static ViewerBoat[] ProjectBoats(PrivateWorldRuntimeState state) => state.BoatTransport.Boats.Select(boat =>
    {
        var journey = boat.Journey;
        var roots = boat.GroundCargoLotIds ?? [];
        var cargo = state.Society.Society.Inventory.Lots.Where(lot => lot.Quantity > 0 &&
            (journey is not null && PersonalEquipmentRules.IsCarried(lot, journey.PassengerId) || roots.Contains(lot.Id, StringComparer.Ordinal) ||
             lot.ContainerLotId is { } container && roots.Contains(container, StringComparer.Ordinal)));
        return new ViewerBoat(boat.Id, boat.TownId, state.Towns!.Single(town => town.Id == boat.TownId).Name,
            ToPosition(boat.Position), boat.DockedPortId, journey?.PassengerId,
            journey is null ? null : BoatPassengerName(state, journey.PassengerId),
            journey is null ? null : journey.Returning ? journey.OriginPortId : journey.DestinationPortId,
            journey is null ? "moored" : journey.WaitingSinceTick is not null ? "waiting" : journey.Returning ? "returning" : "underway",
            journey is null ? null : ToPosition(journey.ReservedDock),
            cargo.GroupBy(lot => lot.ItemKind, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ViewerInventoryEntry(group.Key, group.Sum(lot => lot.Quantity))).ToArray());
    }).ToArray();

    private static ViewerBoatTripRequest[] ProjectBoatRequests(PrivateWorldRuntimeState state) =>
        state.BoatTransport.Requests.Where(request => request.Status is "waiting" or "underway")
            .Concat(state.BoatTransport.Requests.Where(request => request.Status is not ("waiting" or "underway")).TakeLast(40))
            .OrderBy(request => request.Sequence).Select(request => new ViewerBoatTripRequest(request.Id, request.Sequence,
            request.PassengerId, BoatPassengerName(state, request.PassengerId), request.BoatTownId,
            request.OriginPortId, request.DestinationPortId, request.Status, request.BoatId)).ToArray();

    private static ViewerHandcart[] ProjectHandcarts(PrivateWorldRuntimeState state)
    {
        string Name(string id) => state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == id)?.Name ??
            state.Society.Society.Households.FirstOrDefault(household => household.Id == id)?.Name ??
            (id == "settlement:communal" ? "Communal property" : "Estate property");
        var inventory = state.Society.Society.Inventory;
        return inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.Handcart)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).Select(cart =>
            {
                var hitch = state.HandcartHitches!.FirstOrDefault(item => item.CartLotId == cart.Id);
                return new ViewerHandcart(cart.Id, cart.OwnerId, Name(cart.OwnerId),
                    new(cart.GroundPosition!.Value.X, cart.GroundPosition.Value.Y), InventoryContainerRules.HandcartCapacity,
                    cart.ConditionBasisPoints / 100, hitch?.PullerId, hitch is null ? null : Name(hitch.PullerId),
                    inventory.Lots.Where(lot => lot.ContainerLotId == cart.Id).GroupBy(lot => lot.ItemKind, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => new ViewerInventoryEntry(group.Key, group.Sum(lot => lot.Quantity))).ToArray());
            }).ToArray();
    }

    private static ViewerInventoryEntry[] InventoryFor(
        PrivateWorldRuntimeState state,
        string ownerId,
        string? storageBuildingId = null) => state.Society.Society.Inventory.Lots
        .Where(lot => lot.OwnerId == ownerId && lot.Quantity > 0 &&
            (storageBuildingId is null || lot.StorageBuildingId == storageBuildingId))
        .GroupBy(lot => lot.ItemKind, StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => new ViewerInventoryEntry(group.Key, group.Sum(lot => lot.Quantity)))
        .ToArray();

    private static ViewerRoute DeterminePlaytestRoute(
        PrivateWorldRuntimeState state,
        PlaytestInhabitantState physical,
        IReadOnlyList<ViewerInventoryEntry> inventory)
    {
        if (state.BoatTransport.Boats.FirstOrDefault(boat => boat.Journey?.PassengerId == physical.InhabitantId) is { Journey: { } journey })
            return new ViewerRoute(journey.WaitingSinceTick is not null ? "boat_waiting" : journey.Returning ? "boat_returning" : "boat_travel",
                journey.Returning ? journey.OriginPortId : journey.DestinationPortId, ToPosition(journey.ReservedDock),
                journey.WaterPath.Skip(journey.PathIndex + 1).Select(ToPosition).ToArray(), state.Map.ManifestDigest);
        var food = inventory.FirstOrDefault(item => item.Kind == "food");
        if (food is { Quantity: > 0 } && physical.HungerBasisPoints < 8_500)
        {
            return new ViewerRoute("consume", null, null, [], state.Map.ManifestDigest);
        }

        var berry = state.Map.GetResource("berry-patch");
        var berryState = state.Resources.FirstOrDefault(item => item.ResourceId == berry.Id)?.State;
        if (berryState == ResourceState.Available && IsWithinInteractionRange(physical.Position, berry.Position))
        {
            return new ViewerRoute("harvest", berry.Id, ToPosition(berry.Position), [], state.Map.ManifestDigest);
        }

        if (berryState == ResourceState.Available && physical.HungerBasisPoints < 7_000)
        {
            // The starter patch is camp-reachable. Reject an agent on a
            // separate island using the map's cached camp component instead
            // of exhaustively searching the entire world on every reconnect.
            if ((state.Map.IsReachableFromCampOnFoot(berry.Position) &&
                 !state.Map.IsReachableFromCampOnFoot(physical.Position)) ||
                !DeterministicRouteFinder.TryFind(state.Map, physical.Position, berry.Position,
                    out var path))
                return new ViewerRoute("food_unreachable", null, null, [], state.Map.ManifestDigest);
            return new ViewerRoute("seek_food", berry.Id, ToPosition(berry.Position),
                path.Skip(1).Select(ToPosition).ToArray(), state.Map.ManifestDigest);
        }

        return new ViewerRoute("idle", null, null, [], state.Map.ManifestDigest);
    }

    /// <summary>A planned route as the owner sees it, with at most <see cref="ViewerPlannedRoute.StepLimit"/> steps.</summary>
    public static ViewerPlannedRoute? ToPlannedRoute(PlaytestPlannedRoute? route) => route is null ? null :
        new ViewerPlannedRoute(route.Reason, ToPosition(route.Destination),
            route.Steps.Take(ViewerPlannedRoute.StepLimit).Select(ToPosition).ToArray(), route.Steps.Count);

    private static bool IsWithinInteractionRange(GridPoint origin, GridPoint destination) =>
        Math.Abs(origin.X - destination.X) + Math.Abs(origin.Y - destination.Y) <= 1;

    private static ViewerRoute DetermineFixtureRoute(HarnessWorld world)
    {
        var actor = world.Actor;
        if (actor.FoodItems > 0)
        {
            return new ViewerRoute("consume", null, null, [], world.Map.ManifestDigest);
        }

        var berry = world.Map.GetResource("berry-patch");
        if (world.GetResource(berry.Id).State == ResourceState.Available)
        {
            return RouteTo(world.Map, actor.Position, berry.Position, "harvest", berry.Id);
        }

        return new ViewerRoute("fixture_complete", null, null, [], world.Map.ManifestDigest);
    }

    private static ViewerRoute RouteTo(
        SeededMap map,
        GridPoint origin,
        GridPoint destination,
        string status,
        string destinationId)
    {
        var steps = origin == destination
            ? []
            : DeterministicRouteFinder.Find(map, origin, destination)
                .Skip(1)
                .Select(ToPosition)
                .ToArray();
        return new ViewerRoute(status, destinationId, ToPosition(destination), steps, map.ManifestDigest);
    }

    private static IEnumerable<ViewerPosition> KnownNearby(SeededMap map, GridPoint origin) => map.Tiles
        .Where(tile => Math.Abs(tile.Position.X - origin.X) <= 1 && Math.Abs(tile.Position.Y - origin.Y) <= 1)
        .OrderBy(tile => tile.Position.Y)
        .ThenBy(tile => tile.Position.X)
        .Select(tile => ToPosition(tile.Position));

    /// <summary>
    /// The deterministic fixture has no persistent cognitive map. Its truthful
    /// knowledge is therefore limited to the current local perception and the
    /// route/destination it has already committed to follow. The server still
    /// owns the full map for routing, but must not accidentally project that
    /// omniscience as inhabitant knowledge.
    /// </summary>
    private static ViewerPosition[] KnownFixtureTopology(
        GridPoint currentPosition,
        IReadOnlyList<ViewerPosition> perceived,
        ViewerRoute route)
    {
        var routeKnowledge = route.Destination is null
            ? route.Steps
            : route.Steps.Append(route.Destination);

        return perceived
            .Append(ToPosition(currentPosition))
            .Concat(routeKnowledge)
            .Distinct()
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
    }

    private static ViewerActor ToActor(HarnessActor actor) => new(
        actor.Id,
        ToPosition(actor.Position),
        actor.HungerBasisPoints,
        actor.FoodItems,
        actor.WoodItems);

    private static ViewerEvent ToEvent(OwnerWorldEvent worldEvent) => new(
        worldEvent.EventId,
        worldEvent.WorldTick,
        worldEvent.Kind,
        worldEvent.Detail);

    private static ViewerEvent ToEvent(PlaytestWorldEvent worldEvent) => new(
        worldEvent.EventId,
        worldEvent.WorldTick,
        worldEvent.Kind,
        worldEvent.Detail,
        worldEvent.Position is { } position ? ToPosition(position) : null);

    private static ViewerCognition ToCognition(PrivateWorldRuntimeState state)
    {
        var runtimes = state.Society.Cognition.Runtimes
            .OrderBy(runtime => runtime.InhabitantId, StringComparer.Ordinal)
            .ToArray();
        var current = runtimes
            .Select(runtime => runtime.CurrentIntention)
            .FirstOrDefault(intention => intention is not null);
        var provider = current?.Provider.ToString().ToLowerInvariant() ??
            (state.Society.Society.WorldDefaultProviderBindingId is null ? "deterministic" : "configured");
        return new ViewerCognition(
            provider,
            state.Society.Society.IsPaused,
            null,
            current?.CandidateId,
            current?.Provider.ToString().ToLowerInvariant(),
            state.Society.Cognition.Events
                .OrderBy(worldEvent => worldEvent.EventId)
                .TakeLast(12)
                .Select(worldEvent => new ViewerCognitionEvent(
                    worldEvent.EventId,
                    worldEvent.WorldTick,
                    worldEvent.Kind,
                    worldEvent.Detail))
                .ToArray(),
            runtimes.Where(runtime => runtime.CurrentIntention is not null)
                .Select(runtime =>
                {
                    var intention = runtime.CurrentIntention!;
                    return new ViewerInhabitantDecision(
                        runtime.InhabitantId, intention.Usage?.ProviderId ?? intention.Provider.ToString().ToLowerInvariant(),
                        intention.CandidateId, intention.WorldTick, intention.Confidence,
                        intention.Usage?.ModelId, intention.Usage?.InputTokens, intention.Usage?.OutputTokens,
                        intention.Usage?.Role, intention.Usage?.LatencyMilliseconds,
                        runtime.Events.LastOrDefault(item => item.WorldTick == intention.WorldTick &&
                            item.Kind is "cognition_fallback_applied" or "cognition_decision_applied")?.Kind == "cognition_fallback_applied");
                }).ToArray());
    }

    private static ViewerPosition ToPosition(GridPoint point) => new(point.X, point.Y);

    private static string ToWireValue(TerrainKind terrain) => terrain switch
    {
        TerrainKind.Meadow => "meadow",
        TerrainKind.Water => "water",
        TerrainKind.Mountain => "mountain",
        TerrainKind.River => "river",
        TerrainKind.Lake => "lake",
        TerrainKind.Ocean => "ocean",
        TerrainKind.Peak => "peak",
        TerrainKind.Sand => "sand",
        TerrainKind.Forest => "forest",
        TerrainKind.Snow => "snow",
        _ => throw new ArgumentOutOfRangeException(nameof(terrain)),
    };

    private static string ToWireValue(ResourceState state) => state switch
    {
        ResourceState.Available => "available",
        ResourceState.Depleted => "depleted",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string ToWireValue(OwnerInstructionKind kind) => kind switch
    {
        OwnerInstructionKind.Suggestive => "suggestive",
        OwnerInstructionKind.MustDo => "must_do",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string ToWireValue(OwnerInstructionState state) => state switch
    {
        OwnerInstructionState.Queued => "queued",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
