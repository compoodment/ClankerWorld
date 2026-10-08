using ClankerWorld.Simulation.Cognition;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private (TownGovernanceState Council, TownNonviolentState Nonviolent) OpenNonviolentCase(TownRuntimeState town,
        TownGovernanceState council, TownNonviolentState state, TownViolationAllegation allegation, TownViolationFiling filing,
        TownCaseEvidence? evidence)
    {
        var count = state.Cases.Count;
        state = TownNonviolentRules.File(state, town.Id, allegation, filing, TownNonviolentPartyRules.CurrentParties(town, allegation, [filing], society.Checkpoint, WorldTick),
            WorldTick, CivicDay, "notice:" + (council.Notices.Count + 1));
        var item = state.Cases.Single(current => current.Key == TownNonviolentRules.CaseKey(town.Id, allegation));
        if (state.Cases.Count > count) council = PostNonviolentNotice(council, item);
        council = TownGovernanceRules.LearnNotice(council, filing.AgentId, NonviolentRevision(item).NoticeId, WorldTick);
        if (evidence is not null)
            state = AddNonviolentEvidence(state, item, evidence, filing.AgentId, council);
        return (council, state);
    }

    private TownNonviolentState AddNonviolentEvidence(TownNonviolentState state, TownViolationCase item,
        TownCaseEvidence evidence, string actor, TownGovernanceState council)
    {
        if (item.Evidence.Any(old => old.Kind == evidence.Kind && old.SourceRecordId == evidence.SourceRecordId &&
                old.SourceVersion == evidence.SourceVersion && old.SourceAgentId == evidence.SourceAgentId && old.Text == evidence.Text)) return state;
        var nativeSource = state.ConductRecords.Any(record => record.Id == evidence.SourceRecordId) &&
            state.Acquisitions.Any(acquisition => acquisition.Id == evidence.Id && acquisition.ConductId == evidence.SourceRecordId && acquisition.AgentId == actor);
        var id = nativeSource ? evidence.Id : "case-evidence:" + NonviolentToken(item.Id + "|" + evidence.Id + "|" + actor + "|" + NonviolentRevision(item).Number);
        return TownNonviolentRules.AddEvidence(state, item.Id, NonviolentRevision(item).Number, evidence with
        { Id = id, Revision = NonviolentRevision(item).Number, SubmittedByAgentId = actor, SubmittedTick = WorldTick }, council.Knowledge);
    }

    private (TownGovernanceState Council, TownNonviolentState Nonviolent) ApplyTownNonviolentAction(TownRuntimeState town,
        string actor, string action, string subject, string choice, CognitionNonviolentChoice? payload,
        TownGovernanceState council, TownGovernmentState government)
    {
        (council, var state) = AdvanceTownNonviolent(town, council, government);
        town = town with { Governance = council, Government = government, Nonviolent = state };
        var candidates = new List<CognitionCandidate>();
        AddTownNonviolentCandidates(candidates, actor, town);
        if (!candidates.Any(candidate => candidate.Id == $"civic|{town.Id}|{action}|{subject}|{choice}"))
            throw new InvalidOperationException("This legal choice no longer has current authority or informed eligibility.");
        if (action is "law_case_file" or "law_case_file_affected" or "law_case_file_town" or "law_case_propose_town")
        {
            var report = NonviolentKnownReports(town, actor).Single(report => NonviolentReportToken(report.Allegation) == subject);
            var text = NonviolentText(payload?.Statement);
            var allegation = report.Allegation with { Statement = text };
            if (action == "law_case_propose_town")
            {
                var request = new TownViolationFilingRequest(allegation, text);
                council = TownGovernanceRules.SubmitProposal(council, town.Id, actor, "law_case", null,
                    "Authorize this Town legal report.", "council:" + council.Revision, TownAdults(town), WorldTick, CivicDay,
                    noticeText: TownNonviolentGovernmentFilingRules.Describe(request), nonviolentRequest: request);
                return (council, state);
            }
            var office = action == "law_case_file_town" ? government.Offices.Single(office => office.Mandates == "ordinary" && office.HolderId == actor && office.TermEndTick > WorldTick) : null;
            var filing = new TownViolationFiling(actor, office is not null ? "town" : action == "law_case_file_affected" ? "affected" : "witness",
                WorldTick, text, allegation.SourceEvidenceIds, office?.ElectionId);
            var opened = OpenNonviolentCase(town, council, state, allegation, filing, report.Evidence);
            NonviolentEvent("opened", town, opened.Nonviolent.Cases.Single(item => item.Key == TownNonviolentRules.CaseKey(town.Id, allegation)).Id, actor);
            return opened;
        }
        if (action == "remedy_renegotiate")
        {
            var agreement = state.Agreements.Single(agreement => agreement.Id == subject && agreement.TermsHash == choice);
            var oldOffer = state.Offers.Single(offer => offer.Id == agreement.OfferId);
            var terms = NonviolentTerms(town, actor, oldOffer.CaseId, payload, agreement.Id);
            state = TownRemedyRules.Offer(state, oldOffer.CaseId, oldOffer.FindingId, actor, terms,
                payload?.CompletionTicks ?? checked(3L * CivicDay), NonviolentText(payload?.Statement), WorldTick, CivicDay,
                "notice:" + (council.Notices.Count + 1), true, replacesAgreementId: agreement.Id);
            council = PostNonviolentOffer(council, state.Offers[^1]);
            NonviolentEvent("offer", town, state.Offers[^1].Id, actor);
            return (council, state);
        }
        if (action is "remedy_read" or "remedy_accept" or "remedy_decline" or "remedy_counter")
        {
            var offer = state.Offers.Single(offer => NonviolentOfferToken(offer) == subject);
            if (action == "remedy_read") council = TownGovernanceRules.LearnNotice(council, actor, offer.NoticeId, WorldTick);
            else if (action == "remedy_counter")
            {
                var terms = NonviolentTerms(town, actor, offer.CaseId, payload);
                var text = NonviolentText(payload?.Statement);
                state = TownRemedyRules.Respond(state, offer.Id, offer.Revision, actor, "counter", text, WorldTick, CivicDay, council.Knowledge,
                    NonviolentRemedyDurationFeasible(town, offer.Terms.Where(term => term.ContributorId == actor).ToArray(), offer.CompletionTicks));
                state = TownRemedyRules.Offer(state, offer.CaseId, offer.FindingId, actor, terms, payload?.CompletionTicks ?? checked(3L * CivicDay),
                    text, WorldTick, CivicDay, "notice:" + (council.Notices.Count + 1), true, replacesOfferId: offer.Id);
                council = PostNonviolentOffer(council, state.Offers[^1]);
            }
            else state = TownRemedyRules.Respond(state, offer.Id, offer.Revision, actor, action == "remedy_accept" ? "accept" : "decline",
                payload?.Statement, WorldTick, CivicDay, council.Knowledge,
                NonviolentRemedyCapacityFeasible(offer.Terms) &&
                NonviolentRemedyDurationFeasible(town, offer.Terms.Where(term => term.ContributorId == actor).ToArray(), offer.CompletionTicks));
            NonviolentEvent("offer_response", town, offer.Id, actor);
            return (council, state);
        }
        var item = state.Cases.Single(item => NonviolentActionToken(item) == subject || item.Contest is { } contest &&
            NonviolentActionToken(item) + "#" + TownCaseJudgeRules.RoundToken(contest) == subject);
        if (payload?.EvidenceIds is { } references)
            payload = payload with
            {
                EvidenceIds = references.Select(reference => item.Evidence.FirstOrDefault(evidence =>
                evidence.Id == reference || CivicAgentToken(evidence.Id) == reference)?.Id ?? reference).ToArray()
            };
        var revision = NonviolentRevision(item);
        var parties = NonviolentParties(town, item);
        switch (action)
        {
            case "law_case_inspect":
                council = TownGovernanceRules.LearnNotice(council, actor, revision.NoticeId, WorldTick);
                foreach (var evidence in NonviolentKnownEvidence(town, item, actor))
                {
                    state = AddNonviolentEvidence(state, state.Cases.Single(current => current.Id == item.Id), evidence, actor, council);
                }
                var law = government.Laws.Single(law => law.Id == item.Allegation.LawId);
                var version = law.Versions.Single(version => version.Version == item.Allegation.LawVersion);
                var lawText = ("Applicable law at the alleged act: " + TownLawRules.Text(version.Subject, version.Rule));
                state = AddNonviolentEvidence(state, state.Cases.Single(current => current.Id == item.Id),
                    new("law:" + law.Id + ":" + version.Version, revision.Number, "record", "record_inspection", actor,
                        LandHearingLawToken(law, version), TownLandHearingRules.LawVersion(version), item.Allegation.ConductTick,
                        actor, WorldTick, NonviolentExcerpt(lawText, 256)), actor, council);
                state = TownNonviolentRules.Inspect(state, item.Id, revision.Number, actor, WorldTick);
                state = AcquireNonviolentInspectedSources(town with { Nonviolent = state, Governance = council },
                    state.Cases.Single(current => current.Id == item.Id), actor);
                break;
            case "law_case_evidence":
                foreach (var evidence in NonviolentKnownEvidence(town, item, actor))
                    state = AddNonviolentEvidence(state, state.Cases.Single(current => current.Id == item.Id), evidence, actor, council);
                break;
            case "law_case_advise":
            case "law_case_statement":
                var text = NonviolentText(payload?.Statement);
                state = AddNonviolentEvidence(state, item,
                    new("statement:" + NonviolentToken(actor + "|" + text), revision.Number, "allegation", "statement", actor,
                        null, null, WorldTick, actor, WorldTick, text), actor, council);
                break;
            case "law_case_answer":
            case "law_case_waive":
                var party = parties.Single(party => CivicAgentToken(party.Id) == choice);
                state = TownNonviolentRules.Respond(state, item.Id, revision.Number, actor, party.SubjectId, party.Id,
                    action == "law_case_answer" ? "answer" : "waive", action == "law_case_answer" ? NonviolentText(payload?.Statement) : "I waive only this response opportunity, without admitting the allegation.",
                    WorldTick, council.Knowledge, parties);
                break;
            case "law_case_judge_register": state = TownCaseJudgeRules.Register(state, item.Id, actor, TownAdults(town), LandHearingHouseholds(), WorldTick, parties); break;
            case "law_case_judge_withdraw": state = TownCaseJudgeRules.Withdraw(state, item.Id, actor, WorldTick); break;
            case "law_case_judge_resign": state = TownCaseJudgeRules.Resign(state, item.Id, actor, WorldTick); break;
            case "law_case_judge_vote": state = TownCaseJudgeRules.Vote(state, item.Id, TownCaseJudgeRules.RoundToken(item.Contest!), actor, ResolveCivicAgentToken(choice), WorldTick); break;
            case "law_case_find":
                state = TownNonviolentRules.Rule(state, item.Id, revision.Number, item.Judge!, WorldTick,
                    choice == "unsupported" ? "unsupported" : "supported", choice == "unsupported" ? "none" : choice,
                    payload?.EvidenceIds ?? [], NonviolentText(payload?.Statement), NonviolentText(payload?.Uncertainty), parties,
                    item.Judge is { } judge && NonviolentJudgeValid(town, item, judge),
                    crediblePriorNotice: HasPriorLawNotice(town, item.Allegation, item.Allegation.SubjectId));
                council = TownGovernanceRules.PostNotice(council, "law_case", state.Cases.Single(current => current.Id == item.Id).Findings[^1].Id,
                    "A reasoned civil finding was recorded: " + choice + ". This finding changes no physical facts, stock, membership or office.", WorldTick);
                break;
            case "law_case_reopen":
                state = TownNonviolentRules.RequestReopen(state, item.Id, actor, choice, payload?.EvidenceIds ?? [], NonviolentText(payload?.Grounds ?? payload?.Statement), WorldTick);
                break;
            case "law_case_assess_reopen":
                var request = item.ReopenRequests.Single(request => request.Status == "pending" &&
                    (CivicAgentToken(request.Id + ":accept") == choice || CivicAgentToken(request.Id + ":reject") == choice));
                var accept = CivicAgentToken(request.Id + ":accept") == choice;
                state = TownNonviolentRules.Reopen(state, item.Id, request.Id, item.Judge!, accept, NonviolentText(payload?.Statement), parties,
                    WorldTick, CivicDay, "notice:" + (council.Notices.Count + 1), item.Judge is { } reopeningJudge && NonviolentJudgeValid(town, item, reopeningJudge));
                if (accept) council = PostNonviolentNotice(council, state.Cases.Single(current => current.Id == item.Id));
                break;
            case "law_case_relay":
                var recipient = ResolveCivicAgentToken(choice);
                council = TownGovernanceRules.LearnNotice(council, recipient, revision.NoticeId, WorldTick, actor);
                state = TownNonviolentRules.RelayRead(state, item.Id, revision.Number, actor, recipient, WorldTick);
                break;
            case "remedy_offer":
                var proposedTerms = NonviolentTerms(town, actor, item.Id, payload);
                state = TownRemedyRules.Offer(state, item.Id, choice, actor, proposedTerms,
                    payload?.CompletionTicks ?? checked(3L * CivicDay), NonviolentText(payload?.Statement), WorldTick, CivicDay,
                    "notice:" + (council.Notices.Count + 1), true);
                council = PostNonviolentOffer(council, state.Offers[^1]);
                break;
            default: throw new InvalidOperationException("Unsupported nonviolent case action.");
        }
        NonviolentEvent(action switch
        {
            "law_case_inspect" => "inspected",
            "law_case_relay" => "relayed",
            "law_case_find" => "finding",
            "law_case_answer" or "law_case_waive" => "response",
            "law_case_reopen" => "reopen_requested",
            "law_case_assess_reopen" => state.Cases.Single(current => current.Id == item.Id).Status == "pending" ? "reopened" : "rejected",
            "law_case_judge_register" or "law_case_judge_withdraw" or "law_case_judge_resign" => "judge_consent",
            "remedy_offer" => "offer",
            _ => "evidence"
        }, town, item.Id, actor);
        return (council, state);
    }

    private TownRemedyTerm[] NonviolentTerms(TownRuntimeState town, string actor, string caseId, CognitionNonviolentChoice? payload, string? replacingAgreementId = null)
    {
        if (payload?.Terms is not { Count: > 0 } terms) throw new InvalidOperationException("A restorative offer needs finite named goods or work.");
        string? PhysicalReference(string? reference) => reference is null ? null :
            society.Checkpoint.Inventory.Lots.Select(lot => lot.Id).Concat(worldSimulation.Buildings.Select(building => building.InstanceId))
                .FirstOrDefault(id => id == reference || CivicAgentToken(id) == reference) ?? reference;
        string BeneficiaryReference(string reference) => society.Checkpoint.Inhabitants.Select(person => person.Id)
            .Concat(society.Checkpoint.Households.Select(household => household.Id)).Concat(towns.Select(item => item.Id))
            .FirstOrDefault(id => id == reference || CivicAgentToken(id) == reference) ?? reference;
        var result = terms.Select((term, index) => new TownRemedyTerm("term:" + NonviolentToken(caseId + "|" + town.Nonviolent.Sequence + "|" + index),
            term.Kind, ResolveCivicAgentToken(term.ContributorId), term.BeneficiaryId is { } beneficiary ? BeneficiaryReference(beneficiary) : null,
            term.ItemKind, term.Quantity, PhysicalReference(term.TargetId))).ToArray();
        if (replacingAgreementId is not null)
            town = town with
            {
                Nonviolent = town.Nonviolent with
                { Agreements = town.Nonviolent.Agreements.Where(agreement => agreement.Id != replacingAgreementId).ToArray() }
            };
        if (result.Any(term => !LandHearingAdult(term.ContributorId) || !NonviolentRemedyFeasible(town, term) || !NonviolentRemedyTermKnownTo(town, term, actor)))
            throw new InvalidOperationException("A proposed contribution must be feasible, lawful and known without searching private property.");
        var duration = payload.CompletionTicks ?? checked(3L * CivicDay);
        if (duration > long.MaxValue - WorldTick - CivicDay || !NonviolentRemedyDurationFeasible(town, result, duration))
            throw new InvalidOperationException("The proposed deadline leaves insufficient time for the named physical work and travel.");
        return result;
    }

    private TownGovernanceState PostNonviolentOffer(TownGovernanceState council, TownRemedyOffer offer) =>
        TownGovernanceRules.PostNotice(council, "remedy", offer.Id, "Voluntary restorative offer: " + NonviolentTermsText(offer.Terms) +
            ". " + NonviolentOfferTiming(offer) + " Each contributing adult may accept, decline or counter; silence is not consent and declining is no offense.", offer.PublishedTick);
}
