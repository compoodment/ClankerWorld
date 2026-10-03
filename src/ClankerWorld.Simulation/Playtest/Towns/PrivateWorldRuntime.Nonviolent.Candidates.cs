using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void AddChildNonviolentCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!ChildResident(actor) || NeedsUrgentFood(inhabitants[actor]) || NeedsUrgentWarmth(inhabitants[actor])) return;
        foreach (var town in towns.Where(town => town.FoundingState == "founded" && town.Governance is not null))
            AddTownNonviolentCandidates(candidates, actor, town);
    }

    private void AddTownNonviolentCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        if (town.Governance is not { } council || !inhabitants.ContainsKey(actor) ||
            society.Checkpoint.GetInhabitant(actor).AgeBand == SocietyAgeBand.Infant) return;
        var adult = LandHearingAdult(actor);
        var atBoard = NearCivicBoard(actor, town);
        var history = CivicHistory(town);
        if (!atBoard && MayVisitNonviolentCase(actor, town) && !candidates.Any(candidate => candidate.Id == CivicAction(town.Id, "visit")))
            candidates.Add(new(CivicAction(town.Id, "visit"), "Visit the public notice place for a reported act you actually know about. Visiting grants no membership or private access.", 175));
        if (adult && atBoard)
            foreach (var report in NonviolentKnownReports(town, actor).Where(report => !town.Nonviolent.Cases.Any(item =>
                         item.Key == TownNonviolentRules.CaseKey(town.Id, report.Allegation) &&
                         (item.Status != "pending" || item.Filings.Any(filing => filing.AgentId == actor &&
                             (filing.Kind == "affected" || actor == item.Allegation.SubjectId))))).Take(6))
            {
                var token = NonviolentReportToken(report.Allegation);
                var label = "Report known conduct as an allegation: " + NonviolentKnownAllegationText(town, report.Allegation) +
                    ". Supply civic_nonviolent.statement explaining the alleged breach; the engine does not decide guilt. Source: " + report.Evidence.Acquisition + ".";
                if (!town.Nonviolent.Cases.Any(item => item.Key == TownNonviolentRules.CaseKey(town.Id, report.Allegation) && item.Filings.Any(filing => filing.AgentId == actor)))
                    candidates.Add(new(CivicAction(town.Id, "law_case_file", token), label, 185));
                if (actor != report.Allegation.SubjectId)
                    candidates.Add(new(CivicAction(town.Id, "law_case_file_affected", token),
                        "Report this known act as personally affecting you, explaining your claimed harm in civic_nonviolent.statement. Your claimed stake grants a response opportunity, not proof that the allegation or harm is true. " + label, 185));
                if (town.Government is { } government && TownAdults(town).Contains(actor, StringComparer.Ordinal))
                {
                    if (government.Arrangement.Ordinary == TownArrangementRules.Mayor && government.Offices.Any(office =>
                            office.Mandates == "ordinary" && office.HolderId == actor && office.TermEndTick > WorldTick))
                        candidates.Add(new(CivicAction(town.Id, "law_case_file_town", token), "File this known allegation under your current ordinary Town office. " + label, 186));
                    else if (government.Arrangement.Ordinary is TownArrangementRules.Council or TownArrangementRules.ElectedCouncil or TownArrangementRules.AllAdultCouncil &&
                        council.Members.Contains(actor, StringComparer.Ordinal))
                        candidates.Add(new(CivicAction(town.Id, "law_case_propose_town", token), "Ask the Council to authorize this known Town allegation; approval is required. " + label, 186));
                }
            }
        foreach (var item in town.Nonviolent.Cases)
        {
            var revision = NonviolentRevision(item);
            var token = NonviolentActionToken(item);
            var parties = NonviolentParties(town, item);
            var isParty = parties.Any(party => party.SubjectId == actor || party.RespondingAdultId == actor);
            if (atBoard && NonviolentMayInspect(town, item, actor) && (!NonviolentReadCurrent(item, actor) ||
                item.ReopenRequests.Any(request => !item.Reads.Any(read => read.AgentId == actor && read.ReopenRequestIds.Contains(request.Id, StringComparer.Ordinal)))))
                candidates.Add(new(CivicAction(town.Id, "law_case_inspect", token),
                    "Read this public case, its alleged act, historical law and sourced evidence. It remains an allegation until a reasoned finding; reading grants no private access.", 177));
            if (!NonviolentKnows(town, item, actor)) continue;
            if (NonviolentMayInspect(town, item, actor))
            {
                candidates.Add(new(CivicAction(town.Id, "law_case_statement", token),
                    "Submit your own statement via civic_nonviolent.statement. A child may speak with caregiver support; testimony is not a verified fact or another person's consent.", 182));
                if (NonviolentKnownEvidence(town, item, actor).Any(evidence => !item.Evidence.Any(old => old.SourceRecordId == evidence.SourceRecordId &&
                        old.SourceVersion == evidence.SourceVersion && old.SubmittedByAgentId == actor)))
                    candidates.Add(new(CivicAction(town.Id, "law_case_evidence", token), "Submit only evidence you actually observed or lawfully learned. Private information stays private.", 181));
            }
            if (adult && council.Members.Contains(actor, StringComparer.Ordinal) && town.Government?.Arrangement.NonLand != TownArrangementRules.Mayor)
                candidates.Add(new(CivicAction(town.Id, "law_case_advise", token), "Offer Council mediation or advice via civic_nonviolent.statement while the formal non-land mandate is absent. Advice is a sourced statement, not a finding or enforceable consequence.", 183));
            foreach (var party in parties.Where(party => item.Status == "pending" && party.RespondingAdultId == actor &&
                         !item.Responses.Any(response => response.Revision == revision.Number && response.PartyId == party.Id)))
            {
                candidates.Add(new(CivicAction(town.Id, "law_case_answer", token, party.Id),
                    "Answer this allegation for " + society.Checkpoint.GetInhabitant(party.SubjectId).Name +
                    (party.SubjectId == actor ? "." : " with your actual caregiver support.") + " Supply civic_nonviolent.statement. This promises no goods or work." + NonviolentEvidenceChoices(town, item, actor), 165));
                candidates.Add(new(CivicAction(town.Id, "law_case_waive", token, party.Id), "Explicitly waive only this person's current response opportunity; this admits nothing and supplies no restorative consent.", 166));
            }
            if (adult && TownAdults(town).Contains(actor, StringComparer.Ordinal) && (item.Status == "pending" || item.ReopenRequests.Any(request => request.Status == "pending")))
            {
                var willing = item.JudgeConsents.Any(consent => consent.AgentId == actor && consent.WithdrawnTick is null);
                if (!willing && !NonviolentConflict(town, item, actor)) candidates.Add(new(CivicAction(town.Id, "law_case_judge_register", token), "Personally agree to stand as an independent judge of this case only, subject to actual resident election.", 180));
                if (willing) candidates.Add(new(CivicAction(town.Id, "law_case_judge_withdraw", token), "Withdraw your willingness to adjudicate this case; its records remain.", 185));
                if (item.Judge is { Kind: "case_elected" } held && held.AgentId == actor)
                    candidates.Add(new(CivicAction(town.Id, "law_case_judge_resign", token), "Resign your authority over this case only.", 185));
            }
            if (item.Contest is { Stage: "voting" } contest && contest.Voters.Contains(actor, StringComparer.Ordinal) && history.Knows(actor, TownCaseJudgeRules.RoundToken(contest)))
                foreach (var candidate in contest.Candidates)
                    candidates.Add(new(CivicAction(town.Id, "law_case_judge_vote", token + "#" + TownCaseJudgeRules.RoundToken(contest), candidate),
                        "Vote for " + society.Checkpoint.GetInhabitant(candidate).Name + " as independent judge of this case only.", 167));
            if (item.Judge is { } judge && judge.AgentId == actor && NonviolentJudgeValid(town, item, judge) && NonviolentReadCurrent(item, actor))
            {
                if (item.Status == "pending" && TownNonviolentRules.CanCloseResponses(item, parties, WorldTick))
                    foreach (var outcome in new[] { "unsupported", "explanation", "warning", "censure" }.Where(outcome => outcome != "censure" ||
                                 HasPriorLawNotice(town, item.Allegation, item.Allegation.SubjectId)))
                        candidates.Add(new(CivicAction(town.Id, "law_case_find", token, outcome),
                            "Record " + outcome + ". An adverse finding requires more-likely-than-not evidence, reasons and uncertainty in civic_nonviolent; rumor or silence alone is insufficient. A first incident without credible prior law notice favors explanation or warning. No property or membership changes." + NonviolentEvidenceChoices(town, item, actor), 165));
                foreach (var request in item.ReopenRequests.Where(request => request.Status == "pending" && item.Reads.Any(read => read.AgentId == actor &&
                             read.Revision == revision.Number && read.ReopenRequestIds.Contains(request.Id, StringComparer.Ordinal))))
                {
                    if (request.Kind == "material_evidence" ? TownNonviolentRules.MaterialNewEvidence(item, request) : TownNonviolentRules.DemonstratedProceduralError(item, request))
                        candidates.Add(new(CivicAction(town.Id, "law_case_assess_reopen", token, request.Id + ":accept"), "Reopen on established material evidence or procedure error, with reasons in civic_nonviolent.statement. Prior findings and physical effects remain recorded.", 165));
                    candidates.Add(new(CivicAction(town.Id, "law_case_assess_reopen", token, request.Id + ":reject"), "Explain why these reopening grounds are not established; retain the request and prior result.", 166));
                }
            }
            if (item.Status == "settled" && isParty && NonviolentReadCurrent(item, actor))
                foreach (var grounds in new[] { "material_evidence", "procedural_error" })
                    candidates.Add(new(CivicAction(town.Id, "law_case_reopen", token, grounds), "Request reopening for " + grounds.Replace('_', ' ') + ". Supply actual evidence_ids and grounds; a claim alone changes no finding." + NonviolentEvidenceChoices(town, item, actor), 184));
            if (adult && item.Findings.LastOrDefault() is { Result: "supported" } finding && NonviolentReadCurrent(item, actor) &&
                (isParty || finding.Judge.AgentId == actor && NonviolentJudgeValid(town, item, finding.Judge)))
                candidates.Add(new(CivicAction(town.Id, "remedy_offer", token, finding.Id),
                    "Offer feasible named goods, repair or Town resource delivery in civic_nonviolent.terms, with reasons and optional completion_ticks (default three days after consent). Each contributing adult must personally agree. No stock is reserved and no work forced.", 183));
            foreach (var recipient in inhabitants.Values.Where(person => person.InhabitantId != actor &&
                         IsWithinInteractionRange(person.Position, inhabitants[actor].Position, ResourceInteractionRange)))
                if (NonviolentReadCurrent(item, actor) && NonviolentMayInspect(town, item, recipient.InhabitantId) && !NonviolentReadCurrent(item, recipient.InhabitantId))
                    candidates.Add(new(CivicAction(town.Id, "law_case_relay", token, recipient.InhabitantId), "Relay the case material you actually read to " + society.Checkpoint.GetInhabitant(recipient.InhabitantId).Name + " nearby.", 181));
        }
        foreach (var agreement in town.Nonviolent.Agreements.Where(agreement => adult && agreement.Status != "completed" &&
                     !TownRemedyRules.IsSuperseded(town.Nonviolent, agreement.Id) && agreement.Terms.Any(term => term.ContributorId == actor)))
        {
            var original = town.Nonviolent.Offers.Single(offer => offer.Id == agreement.OfferId);
            var file = town.Nonviolent.Cases.Single(item => item.Id == original.CaseId);
            if (NonviolentReadCurrent(file, actor) && !town.Nonviolent.Offers.Any(offer => offer.CaseId == file.Id && offer.Status == "pending"))
                candidates.Add(new(CivicAction(town.Id, "remedy_renegotiate", agreement.Id, agreement.TermsHash),
                    "Request a feasible change to your unfinished voluntary agreement via civic_nonviolent.terms. Prior work remains recorded; replacement terms require fresh consent. Inability is no new offense.", 183));
        }
        foreach (var offer in town.Nonviolent.Offers.Where(offer => offer.Status == "pending" && WorldTick < offer.ResponseDeadlineTick &&
                     offer.Terms.Any(term => term.ContributorId == actor)))
        {
            if (atBoard && !history.Known(actor).Contains(offer.NoticeId))
                candidates.Add(new(CivicAction(town.Id, "remedy_read", NonviolentOfferToken(offer)), "Read the actual voluntary offer: " + NonviolentTermsText(offer.Terms) + ". Publication alone is not your awareness or consent.", 177));
            if (!adult || !history.Known(actor).Contains(offer.NoticeId) || offer.Responses.Any(response => response.AgentId == actor && response.Kind == "accept")) continue;
            if (offer.Terms.Where(term => term.ContributorId == actor).All(term => NonviolentRemedyFeasible(town, term)))
                candidates.Add(new(CivicAction(town.Id, "remedy_accept", NonviolentOfferToken(offer)), "Personally accept your exact contribution: " + NonviolentTermsText(offer.Terms.Where(term => term.ContributorId == actor)) + ". This does not start work or reserve goods.", 165));
            candidates.Add(new(CivicAction(town.Id, "remedy_decline", NonviolentOfferToken(offer)), "Decline this voluntary offer. Declining creates no offense or automatic penalty.", 166));
            candidates.Add(new(CivicAction(town.Id, "remedy_counter", NonviolentOfferToken(offer)), "Counter with feasible named terms in civic_nonviolent.terms; everyone must read and consent to the new offer.", 183));
        }
    }

    private static string NonviolentTermsText(IEnumerable<TownRemedyTerm> terms) => string.Join("; ", terms.Select(term =>
        term.Quantity + " " + (term.ItemKind ?? term.Kind).Replace('_', ' ') + " by " + term.ContributorId +
        (term.BeneficiaryId is { } beneficiary ? " for " + beneficiary : "") + (term.TargetId is { } target ? " at " + target : "")));
}
