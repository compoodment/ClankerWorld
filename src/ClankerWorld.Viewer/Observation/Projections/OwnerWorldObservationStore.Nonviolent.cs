using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Viewer.Observation;

public sealed partial class OwnerWorldObservationStore
{
    public const int RecentClosedNonviolentCaseLimit = 8;

    private static ViewerTownNonviolentCase[] ProjectNonviolentCases(PrivateWorldRuntimeState state, TownRuntimeState town)
    {
        var ledger = town.Nonviolent;
        var tick = state.Society.Society.WorldTick;
        string Name(string id) => state.Society.Society.Inhabitants.FirstOrDefault(person => person.Id == id)?.Name ??
            (state.Towns ?? []).FirstOrDefault(other => other.Id == id)?.Name ?? "An earlier participant";
        string? Household(string? id) => id is null ? null :
            state.Society.Society.Households.FirstOrDefault(household => household.Id == id)?.Name ?? "An earlier household";
        string? Target(string? id) => id is null ? null :
            state.Society.Society.Inventory.Lots.FirstOrDefault(lot => lot.Id == id)?.ItemKind.Replace('_', ' ') ??
            ((state.WorldSimulation?.Buildings ?? []).FirstOrDefault(building => building.InstanceId == id) is { } building
                ? state.WorldContent?.Buildings.FirstOrDefault(definition => definition.CanonicalId == building.DefinitionId)?.DisplayName : null)
            ?? "the recorded target";
        var names = state.Society.Society.Inhabitants.Select(person => (person.Id, person.Name))
            .Concat(state.Society.Society.Households.Select(household => (household.Id, household.Name)))
            .Concat((state.Towns ?? []).Select(other => (other.Id, other.Name)))
            .OrderByDescending(pair => pair.Id.Length).ToArray();
        string Readable(string text)
        {
            foreach (var pair in names) text = text.Replace(pair.Id, pair.Name, StringComparison.Ordinal);
            return text;
        }
        bool Aware(string noticeId, long published, string actor, long asOf) => (town.Governance?.Knowledge ?? [])
            .Any(receipt => receipt.NoticeId == noticeId && receipt.AgentId == actor &&
                receipt.LearnedTick >= published && receipt.LearnedTick <= asOf);
        ViewerCaseParty Party(TownCaseParty party, TownCaseRevision revision, long asOf) => new(party.Id,
            party.Role, party.SubjectId, party.Role == "town" ? town.Name : Name(party.SubjectId), party.RespondingAdultId,
            party.RespondingAdultId is { } adult ? Name(adult) : null, Household(party.HouseholdId),
            party.RespondingAdultId is { } responder && Aware(revision.NoticeId, revision.PublishedTick, responder, asOf));
        ViewerCaseJudge Judge(TownCaseJudge judge) => new(judge.AgentId, Name(judge.AgentId), judge.Kind,
            judge.AuthorityId, judge.AssignedTick);
        ViewerCaseElection Election(TownCaseJudgeContest contest) => new(contest.Id, contest.Stage, contest.Round,
            contest.RoundDeadlineTick, contest.Candidates.Select(id => new ViewerCivicCandidate(id, Name(id),
                contest.Ballots.Count(ballot => ballot.CandidateId == id))).ToArray(),
            contest.WinnerId is { } winner ? Name(winner) : null, contest.Reason);
        ViewerRemedyTerm Term(TownRemedyTerm term) => new(term.Id, term.Kind, term.ContributorId, Name(term.ContributorId),
            term.BeneficiaryId, term.BeneficiaryId is { } beneficiary ? Name(beneficiary) : null,
            term.ItemKind, term.Quantity, term.TargetId, Target(term.TargetId));
        ViewerRemedyResponse Consent(TownRemedyResponse response) => new(response.AgentId, Name(response.AgentId),
            response.Revision, response.Kind, response.Tick, response.Reason is { } reason ? Readable(reason) : null);
        bool Active(TownViolationCase item) => item.Status == "pending" || item.ReopenRequests.Any(request => request.Status == "pending") ||
            ledger.Offers.Any(offer => offer.CaseId == item.Id && (offer.Status == "pending" ||
                ledger.Agreements.Any(agreement => agreement.OfferId == offer.Id && !TownRemedyRules.IsSuperseded(ledger, agreement.Id) && agreement.Status is "pending" or "overdue")));
        return ledger.Cases.Where(Active).Concat(ledger.Cases.Where(item => !Active(item))
                .OrderBy(item => item.SettledTick).ThenBy(item => item.Id, StringComparer.Ordinal).TakeLast(RecentClosedNonviolentCaseLimit))
            .OrderBy(item => item.FiledTick).ThenBy(item => item.Id, StringComparer.Ordinal).Select(item =>
            {
                var allegation = item.Allegation;
                var law = town.Government?.Laws.FirstOrDefault(candidate => candidate.Id == allegation.LawId)?
                    .Versions.FirstOrDefault(version => version.Version == allegation.LawVersion);
                var offers = ledger.Offers.Where(offer => offer.CaseId == item.Id).ToArray();
                var offerIds = offers.Select(offer => offer.Id).ToHashSet(StringComparer.Ordinal);
                return new ViewerTownNonviolentCase(item.Id, item.Status, item.FiledTick, item.SettledTick,
                    allegation.SubjectId, Name(allegation.SubjectId), allegation.ConductKind, ToPosition(allegation.Position),
                    allegation.ConductTick, Readable(allegation.Statement), law is null ? null :
                        new ViewerTownLaw(allegation.LawId, law.Subject, law.Rule, law.Scope, law.SiteTiles.Count,
                            law.Version, law.AdoptedTick, law.EndedTick)
                        { Site = law.SiteTiles.Select(ToPosition).ToArray() },
                    allegation.LawVersion,
                    item.Revisions.Select(revision => new ViewerCaseRevision(revision.Number, revision.NoticeId,
                        revision.PublishedTick, revision.DeadlineTick, revision.Parties.Select(party => Party(party, revision, tick)).ToArray())).ToArray(),
                    item.Filings.Select(filing => new ViewerCaseFiling(filing.AgentId, Name(filing.AgentId), filing.Kind,
                        filing.Tick, Readable(filing.Statement), filing.EvidenceIds.ToArray())).ToArray(),
                    item.Evidence.Select(evidence => new ViewerCaseEvidence(evidence.Id, evidence.Revision, evidence.Kind,
                        evidence.Acquisition, evidence.SourceAgentId, Name(evidence.SourceAgentId), evidence.SourceRecordId,
                        evidence.SourceVersion, evidence.ObservedTick, evidence.SubmittedByAgentId, Name(evidence.SubmittedByAgentId),
                        evidence.SubmittedTick, Readable(evidence.Text))).ToArray(),
                    item.Reads.Select(read => new ViewerCaseRead(read.Revision, read.AgentId, Name(read.AgentId), read.ReadTick,
                        read.EvidenceIds.ToArray(), read.ReopenRequestIds.ToArray(), read.SourceAgentId is { } source ? Name(source) : null)).ToArray(),
                    item.Responses.Select(response => new ViewerCaseResponse(response.Revision, response.PartyId, response.AgentId,
                        Name(response.AgentId), response.RepresentedAgentId,
                        item.Revisions.Single(revision => revision.Number == response.Revision).Parties.Any(party =>
                            party.Id == response.PartyId && party.Role == "town") ? town.Name : Name(response.RepresentedAgentId), response.Kind,
                        Readable(response.Text), response.Tick)).ToArray(),
                    item.Findings.Select(finding => new ViewerViolationFinding(finding.Id, finding.Revision, Judge(finding.Judge),
                        finding.Tick, finding.Result, finding.Standard, finding.EvidenceIds.ToArray(), Readable(finding.Reasons),
                        Readable(finding.Uncertainty), finding.Consequence, finding.Parties.Select(party => Party(party,
                            item.Revisions.Single(revision => revision.Number == finding.Revision), finding.Tick)).ToArray())).ToArray(),
                    item.Judge is { } judge ? Judge(judge) : null,
                    item.JudgeHistory.Select(term => new ViewerCaseJudgeTerm(Judge(term.Judge), term.EndedTick, term.Reason)).ToArray(),
                    item.Contest is { } contest ? Election(contest) : null,
                    item.ContestHistory.Count > 0 ? Election(item.ContestHistory[^1]) : null,
                    item.ReopenRequests.Select(request => new ViewerCaseReopenRequest(request.Id, Name(request.AgentId), request.Tick,
                        request.Kind, request.EvidenceIds.ToArray(), Readable(request.Reasons), request.Status,
                        request.AssessedBy is { } assessor ? Judge(assessor) : null, request.AssessedTick,
                        request.Assessment is { } assessment ? Readable(assessment) : null)).ToArray(),
                    offers.Select(offer => new ViewerRemedyOffer(offer.Id, offer.FindingId, offer.Revision,
                        offer.Terms.Select(Term).ToArray(), Readable(offer.Reason), offer.NoticeId, offer.PublishedTick,
                        offer.ResponseDeadlineTick, offer.CompletionTicks, offer.Status, offer.Responses.Select(Consent).ToArray(),
                        offer.Terms.Select(term => term.ContributorId).Distinct(StringComparer.Ordinal)
                            .Where(actor => Aware(offer.NoticeId, offer.PublishedTick, actor, tick)).ToArray(),
                        offer.ReplacesOfferId, offer.AgreementId)).ToArray(),
                    ledger.Agreements.Where(agreement => offerIds.Contains(agreement.OfferId)).Select(agreement =>
                        new ViewerRestorativeAgreement(agreement.Id, agreement.OfferId, agreement.OfferRevision,
                            agreement.Terms.Select(Term).ToArray(), agreement.Consents.Select(Consent).ToArray(),
                            agreement.AcceptedTick, agreement.DeadlineTick, agreement.Status, agreement.ReplacesAgreementId,
                            ledger.Effects.Where(effect => effect.AgreementId == agreement.Id).Select(effect => new ViewerRemedyEffect(
                                effect.Id, effect.TermId, effect.ActorId, Name(effect.ActorId), effect.Tick, effect.Kind,
                                effect.BeneficiaryId is { } beneficiary ? Name(beneficiary) : null, effect.ItemKind,
                                effect.Quantity, Target(effect.TargetId), effect.NativeReceiptId)).ToArray())
                        {
                            Superseded = TownRemedyRules.IsSuperseded(ledger, agreement.Id),
                        }).ToArray())
                {
                    CurrentParties = item.Status == "pending" ? TownNonviolentPartyRules.CurrentParties(town, item,
                        state.Society.Society, tick).Select(party => Party(party, item.Revisions[^1], tick)).ToArray() : [],
                };
            }).ToArray();
    }
}
