using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>Validates the durable civil procedure, consent and performance file before it is used.</summary>
public static class TownNonviolentValidation
{
    public static void Validate(SeededMap map, SocietyCheckpoint society, TownRuntimeState town,
        IReadOnlyList<TownLandTitleRecord> titles, int day)
    {
        var state = town.Nonviolent;
        var tick = society.WorldTick;
        Check(state is not null && state.Sequence >= 0 && state.Cases is not null && state.Offers is not null &&
            state.Agreements is not null && state.Effects is not null && day > 0, "The Town must retain its nonviolent case and remedy records.");
        Check(state.Cases.All(c => c is not null) && state.Offers.All(o => o is not null) &&
            state.Agreements.All(a => a is not null) && state.Effects.All(e => e is not null), "Nonviolent records cannot contain null entries.");
        // Establish the shape of every file before any cross-case reference lookup.
        foreach (var item in state.Cases) Shape(item);
        foreach (var offer in state.Offers)
            Check(Id(offer.Id) && Id(offer.CaseId) && Id(offer.FindingId) && TownRemedyRules.ValidTerms(offer.Terms) &&
                offer.Responses is not null && offer.Responses.All(r => r is not null), "A remedy offer must retain its typed terms and responses.");
        foreach (var agreement in state.Agreements)
            Check(Id(agreement.Id) && Id(agreement.OfferId) && TownRemedyRules.ValidTerms(agreement.Terms) &&
                agreement.Consents is not null && agreement.Consents.All(r => r is not null), "An agreement must retain its terms and personal consents.");
        foreach (var effect in state.Effects)
            Check(Id(effect.Id) && Id(effect.AgreementId) && Id(effect.TermId) && Id(effect.ActorId) &&
                Id(effect.NativeReceiptId) && Id(effect.NativeReceiptVersion), "A remedy effect needs actual named physical provenance.");
        Unique(state.Cases.Select(c => c.Key));
        Unique(state.Cases.Select(c => c.Id).Concat(state.Cases.SelectMany(c => c.Findings.Select(f => f.Id)))
            .Concat(state.Cases.SelectMany(c => c.ReopenRequests.Select(r => r.Id))).Concat(state.Offers.Select(o => o.Id))
            .Concat(state.Agreements.Select(a => a.Id)).Concat(state.Effects.Select(e => e.Id)));
        Check(state.Sequence >= state.Cases.Count + state.Cases.Sum(c => c.Findings.Count + c.ReopenRequests.Count) +
            state.Offers.Count + state.Agreements.Count, "The nonviolent record sequence cannot precede its saved records.");
        var people = society.Inhabitants.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var households = society.Households.Select(h => h.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in state.Cases) ValidateCase(map, society, town, item, people, households, titles, day);
        foreach (var offer in state.Offers) ValidateOffer(state, town, offer, people, tick, day);
        foreach (var agreement in state.Agreements) ValidateAgreement(state, agreement, tick);
        Unique(state.Effects.Select(e => e.NativeReceiptId));
        foreach (var effect in state.Effects)
        {
            var agreement = state.Agreements.SingleOrDefault(a => a.Id == effect.AgreementId);
            var term = agreement?.Terms.SingleOrDefault(t => t.Id == effect.TermId);
            Check(agreement is not null && term is not null && people.ContainsKey(effect.ActorId) && effect.Tick >= agreement.AcceptedTick &&
                effect.Tick <= tick && effect.Quantity > 0 && effect.ActorId == term.ContributorId && effect.Kind == term.Kind &&
                effect.BeneficiaryId == term.BeneficiaryId && effect.ItemKind == term.ItemKind && effect.TargetId == term.TargetId &&
                !state.Agreements.Any(a => a.ReplacesAgreementId == agreement.Id && effect.Tick > a.AcceptedTick),
                "A performance receipt must be the agreed contributor's actual later effect, with exact goods or work.");
        }
    }

    private static void Shape(TownViolationCase item)
    {
        Check(Id(item.Id) && Id(item.Key) && Id(item.TownId) && TownNonviolentRules.ValidAllegation(item.Allegation) &&
            item.Revisions is { Count: > 0 } && item.Filings is { Count: > 0 } && item.Evidence is not null && item.Reads is not null &&
            item.Responses is not null && item.Findings is not null && item.JudgeHistory is not null && item.JudgeConsents is not null &&
            item.ContestHistory is not null && item.ReopenRequests is not null && Canonical(item.DirectStakeIds), "A case must retain its complete file.");
        Check(item.Revisions.All(r => r is not null && TownNonviolentRules.ValidParties(r.Parties)) &&
            item.Filings.All(f => f is not null && f.EvidenceIds is not null) &&
            item.Evidence.All(e => e is not null) && item.Reads.All(r => r is not null && r.EvidenceIds is not null && r.ReopenRequestIds is not null && r.ResponseIds is not null && r.FindingIds is not null) &&
            item.Responses.All(r => r is not null) && item.Findings.All(f => f is not null && Id(f.Id) && f.Judge is not null &&
                f.EvidenceIds is not null && TownNonviolentRules.ValidParties(f.Parties)) &&
            item.JudgeHistory.All(h => h is not null && h.Judge is not null) && item.JudgeConsents.All(c => c is not null) &&
            item.ContestHistory.All(c => c is not null) && item.ReopenRequests.All(r => r is not null && Id(r.Id) && r.EvidenceIds is not null),
            "A nonviolent case cannot contain incomplete file entries.");
    }

    private static void ValidateCase(SeededMap map, SocietyCheckpoint society, TownRuntimeState town,
        TownViolationCase item, Dictionary<string, SocietyInhabitant> people, HashSet<string> households,
        IReadOnlyList<TownLandTitleRecord> titles, int day)
    {
        var tick = society.WorldTick;
        Check(item.TownId == town.Id && item.Key == TownNonviolentRules.CaseKey(town.Id, item.Allegation) &&
            item.Status is "pending" or "settled" && item.FiledTick >= item.Allegation.ConductTick && item.FiledTick <= tick &&
            map.Contains(item.Allegation.Position) && people.ContainsKey(item.Allegation.SubjectId), "A saved allegation must identify its actual Town, person, place and time.");
        var law = town.Government?.Laws.SingleOrDefault(l => l.Id == item.Allegation.LawId);
        // Native conduct captures the actual wording before another action can amend
        // the law in the same tick. Live applicability still uses InForceAt.
        var version = law?.Versions.SingleOrDefault(v => v.Version == item.Allegation.LawVersion);
        Check(version is not null && version.AdoptedTick <= item.Allegation.ConductTick &&
            (version.EndedTick is null || version.EndedTick >= item.Allegation.ConductTick),
            "A report must use the law wording in force at the time of conduct.");
        if (version.Scope is TownLawRules.Jurisdiction or TownLawRules.Site)
            Check(titles.Any(t => t.TownId == town.Id && t.RecordedTick <= item.Allegation.ConductTick && t.Tiles.Contains(item.Allegation.Position)) &&
                (version.Scope != TownLawRules.Site || version.SiteTiles.Contains(item.Allegation.Position)),
                "A later Town claim cannot create earlier territorial jurisdiction.");
        // Resident-duty context and actual source acquisition are checked against the
        // native conduct snapshot by the runtime's physical-source validator.
        Check(item.Revisions.Select(r => r.Number).SequenceEqual(Enumerable.Range(1, item.Revisions.Count)) &&
            item.Revisions[0].PublishedTick == item.FiledTick && OrderedTimes(item.Revisions.Select(r => r.PublishedTick)), "Hearing revisions must retain their original and later notice windows.");
        foreach (var revision in item.Revisions)
        {
            Check(revision.PublishedTick >= item.FiledTick && revision.PublishedTick <= tick &&
                revision.DeadlineTick >= revision.PublishedTick && revision.DeadlineTick - revision.PublishedTick == day && Id(revision.NoticeId) &&
                town.Governance?.Notices.Any(n => n.Id == revision.NoticeId && n.Kind == "law_case" &&
                    n.SubjectId == item.Id + ":" + revision.Number.ToString(CultureInfo.InvariantCulture) && n.PostedTick == revision.PublishedTick) == true,
                "A hearing needs its actual published notice and a complete response day.");
            ValidateParties(revision.Parties, people, households, society);
            ValidateTownParty(town, item, revision.Parties, revision.PublishedTick);
        }
        Check(item.DirectStakeIds.All(people.ContainsKey), "Direct stakes must name known people.");
        foreach (var filing in item.Filings)
            Check(Id(filing.AgentId) && people.ContainsKey(filing.AgentId) && filing.Kind is "witness" or "affected" or "town" &&
                Text(filing.Statement) && filing.Tick >= item.FiledTick && filing.Tick <= tick && Canonical(filing.EvidenceIds) &&
                (filing.Kind != "town" || ValidTownFiling(town, item, filing)), "A filing must retain a specific informed reporter and source.");
        Unique(item.Evidence.Select(e => e.Id));
        foreach (var evidence in item.Evidence)
            Check(TownNonviolentRules.ValidEvidence(evidence) && people.ContainsKey(evidence.SourceAgentId) && people.ContainsKey(evidence.SubmittedByAgentId) &&
                evidence.SubmittedTick <= tick && item.Revisions.Any(r => r.Number == evidence.Revision &&
                    TownNonviolentRules.HasNoticeReceipt(r, evidence.SubmittedByAgentId, evidence.SubmittedTick, town.Governance!.Knowledge)),
                "Evidence must preserve its source and actual informed submission.");
        Unique(item.Responses.Select(TownNonviolentRules.ResponseToken));
        foreach (var response in item.Responses)
        {
            var revision = item.Revisions.SingleOrDefault(r => r.Number == response.Revision);
            var party = revision?.Parties.SingleOrDefault(p => p.Id == response.PartyId);
            Check(revision is not null && party is not null && response.AgentId == party.RespondingAdultId && response.RepresentedAgentId == party.SubjectId &&
                response.Kind is "answer" or "waive" && Text(response.Text) && response.Tick <= tick &&
                TownNonviolentRules.HasNoticeReceipt(revision, response.AgentId, response.Tick, town.Governance!.Knowledge),
                "A response must belong to the noticed person or their recorded caregiver support.");
        }
        for (var index = 0; index < item.Reads.Count; index++)
        {
            var read = item.Reads[index];
            Check(Id(read.AgentId) && people.ContainsKey(read.AgentId) && read.ReadTick <= tick && Canonical(read.EvidenceIds) && Canonical(read.ReopenRequestIds) && Canonical(read.ResponseIds) && Canonical(read.FindingIds) &&
                item.Revisions.Any(r => r.Number == read.Revision && r.PublishedTick <= read.ReadTick) &&
                read.EvidenceIds.All(id => item.Evidence.Any(e => e.Id == id && e.SubmittedTick <= read.ReadTick)) &&
                read.ReopenRequestIds.All(id => item.ReopenRequests.Any(r => r.Id == id && r.Tick <= read.ReadTick)) &&
                read.ResponseIds.All(id => item.Responses.Any(r => TownNonviolentRules.ResponseToken(r) == id && r.Tick <= read.ReadTick)) &&
                read.FindingIds.All(id => item.Findings.Any(f => f.Id == id && f.Tick <= read.ReadTick)), "A file read cannot contain unknown or future facts.");
            if (read.SourceAgentId is { } source)
            {
                var prior = item.Reads.Take(index).Where(r => r.Revision == read.Revision && r.ReadTick <= read.ReadTick).ToArray();
                Check(source != read.AgentId && prior.Any(r => r.AgentId == source) &&
                    read.EvidenceIds.All(id => prior.Any(r => (r.AgentId == source || r.AgentId == read.AgentId) && r.EvidenceIds.Contains(id))) &&
                    read.ReopenRequestIds.All(id => prior.Any(r => (r.AgentId == source || r.AgentId == read.AgentId) && r.ReopenRequestIds.Contains(id))) &&
                    read.ResponseIds.All(id => prior.Any(r => (r.AgentId == source || r.AgentId == read.AgentId) && r.ResponseIds.Contains(id))) &&
                    read.FindingIds.All(id => prior.Any(r => (r.AgentId == source || r.AgentId == read.AgentId) && r.FindingIds.Contains(id))),
                    "A relay cannot convey file material neither recipient nor source had read.");
            }
        }
        Check(OrderedTimes(item.Reads.Select(r => r.ReadTick)), "File reads must preserve their chronology.");
        foreach (var consent in item.JudgeConsents)
            Check(Id(consent.AgentId) && people.ContainsKey(consent.AgentId) && consent.Tick >= item.FiledTick && consent.Tick <= tick &&
                (consent.WithdrawnTick is null || consent.WithdrawnTick >= consent.Tick && consent.WithdrawnTick <= tick), "A case judge needs their own recorded willingness.");
        Unique(item.JudgeConsents.Where(c => c.WithdrawnTick is null).Select(c => c.AgentId));
        foreach (var contest in item.ContestHistory.Concat(item.Contest is { } live ? [live] : []))
            ValidateContest(contest, item, people, tick, day, ReferenceEquals(contest, item.Contest));
        if (item.Judge is { } judge)
        {
            ValidateJudge(judge, item, town.Government, people, tick);
            Check(town.Government is { Arrangement.NonLand: TownArrangementRules.Mayor } currentGovernment &&
                (judge.Kind == "case_elected" || TownGovernmentRules.CurrentNonLandAuthority(currentGovernment, tick) is { } authority &&
                    authority.HolderId == judge.AgentId && authority.AuthorityId == judge.AuthorityId) &&
                people[judge.AgentId].Status == SocietyInhabitantStatus.Active &&
                people[judge.AgentId].AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder && town.ResidentIds.Contains(judge.AgentId) &&
                !TownNonviolentRules.JudgeConflict(judge.AgentId, people[judge.AgentId].HouseholdId,
                    TownNonviolentPartyRules.CurrentParties(town, item, society, tick), item.DirectStakeIds.ToHashSet(StringComparer.Ordinal)),
                "A current case judge must remain an independent eligible Town adult.");
        }
        foreach (var term in item.JudgeHistory)
        {
            Check(term.EndedTick >= term.Judge.AssignedTick && term.EndedTick <= tick && Text(term.Reason), "An ended assignment must retain its actual end and reason.");
            ValidateJudge(term.Judge, item, town.Government, people, term.Judge.AssignedTick);
        }
        foreach (var finding in item.Findings) ValidateFinding(town, item, finding, people, households, society);
        Check(item.Status == "settled" ? item.Findings.Count > 0 && item.SettledTick == item.Findings[^1].Tick : item.SettledTick is null,
            "A settled case must retain its finding; reopening keeps prior findings without a false settlement time.");
        foreach (var request in item.ReopenRequests)
        {
            Check(Id(request.AgentId) && people.ContainsKey(request.AgentId) && request.Tick >= item.FiledTick && request.Tick <= tick &&
                request.Kind is "material_evidence" or "procedural_error" && request.Status is "pending" or "accepted" or "rejected" &&
                Text(request.Reasons) && Canonical(request.EvidenceIds) && request.EvidenceIds.Count > 0 &&
                request.EvidenceIds.All(id => item.Evidence.Any(e => e.Id == id && e.SubmittedTick <= request.Tick)), "A reopening request must identify real submitted grounds.");
            Check(request.Status == "pending" ? request.AssessedBy is null && request.AssessedTick is null && request.Assessment is null :
                request.AssessedBy is not null && request.AssessedTick >= request.Tick && request.AssessedTick <= tick && Text(request.Assessment),
                "A reopening assessment needs its actual judge, time and reasons.");
            if (request.AssessedBy is { } assessor)
            {
                ValidateJudge(assessor, item, town.Government, people, request.AssessedTick!.Value);
                Check(item.Reads.Any(r => r.AgentId == assessor.AgentId && r.ReadTick <= request.AssessedTick && r.ReopenRequestIds.Contains(request.Id)), "The assessing judge must actually read the reopening request.");
                if (request.Status == "accepted") Check(request.Kind == "material_evidence" ?
                    TownNonviolentRules.MaterialNewEvidence(item, request) : TownNonviolentRules.DemonstratedProceduralError(item, request), "Accepted reopening needs established grounds, not repeated disagreement.");
            }
        }
    }

    private static void ValidateParties(IReadOnlyList<TownCaseParty> parties, Dictionary<string, SocietyInhabitant> people,
        HashSet<string> households, SocietyCheckpoint society)
    {
        Check(TownNonviolentRules.ValidParties(parties), "Case parties must be complete and unique.");
        foreach (var party in parties)
            Check(people.ContainsKey(party.SubjectId) && (party.RespondingAdultId is null || people.ContainsKey(party.RespondingAdultId)) &&
                (party.HouseholdId is null || households.Contains(party.HouseholdId)) &&
                (party.CareRelationshipId is null ? party.Role == "town" || party.RespondingAdultId is null || party.RespondingAdultId == party.SubjectId :
                    society.Relationships.Any(r => r.Id == party.CareRelationshipId && r.Type == SocietyRelationshipType.Caregiver &&
                        r.ProposerId == party.RespondingAdultId && r.TargetId == party.SubjectId && r.Revision >= party.CareRevision)),
                "A party's caregiver support must have actual care provenance, not shared housing.");
    }

    private static bool OrdinaryMayorAt(TownRuntimeState town, string actor, string? authority, long tick) =>
        town.Government is { } government && (government.Offices.Any(o => o.Mandates == "ordinary" && o.HolderId == actor &&
            (authority is null || o.ElectionId == authority) && o.TermStartTick <= tick && o.TermEndTick > tick) ||
            government.OfficeHistory.Any(o => o.Mandates == "ordinary" && o.HolderId == actor &&
                (authority is null || o.ElectionId == authority) && o.StartTick <= tick && o.EndTick >= tick));

    private static bool ValidTownFiling(TownRuntimeState town, TownViolationCase item, TownViolationFiling filing) =>
        Id(filing.AuthorityId) && (OrdinaryMayorAt(town, filing.AgentId, filing.AuthorityId, filing.Tick) ||
            town.Governance?.Proposals.Any(p => p.Id == filing.AuthorityId && p.Kind == "law_case" && p.Status == "passed" &&
                p.AuthorId == filing.AgentId && p.SettledTick <= filing.Tick && p.NonviolentRequest is { } request &&
                TownNonviolentRules.CaseKey(town.Id, request.Allegation) == item.Key) == true);

    private static void ValidateTownParty(TownRuntimeState town, TownViolationCase item, IReadOnlyList<TownCaseParty> parties, long tick)
    {
        foreach (var party in parties.Where(p => p.Role == "town"))
            Check(party.Id == "town:" + town.Id && party.CareRelationshipId is null &&
                item.Filings.Any(f => f.Kind == "town" && f.AgentId == party.SubjectId && f.Tick <= tick) &&
                (party.RespondingAdultId is null || OrdinaryMayorAt(town, party.RespondingAdultId, null, tick) ||
                    item.Filings.Any(f => f.Kind == "town" && f.AgentId == party.RespondingAdultId && f.Tick <= tick &&
                        town.Governance!.Proposals.Any(p => p.Id == f.AuthorityId && p.AuthorId == f.AgentId && p.Kind == "law_case" && p.Status == "passed"))),
                "The Town party needs its actual public filing and institutional representative, never household liability.");
    }

    private static void ValidateFinding(TownRuntimeState town, TownViolationCase item, TownViolationFinding finding,
        Dictionary<string, SocietyInhabitant> people, HashSet<string> households, SocietyCheckpoint society)
    {
        var revision = item.Revisions.SingleOrDefault(r => r.Number == finding.Revision);
        Check(revision is not null && finding.Tick >= revision.PublishedTick && finding.Tick <= society.WorldTick &&
            finding.Result is "supported" or "unsupported" && finding.Standard == TownNonviolentRules.CivilStandard &&
            finding.Consequence is "none" or "explanation" or "warning" or "censure" && Text(finding.Reasons) && Text(finding.Uncertainty) &&
            Canonical(finding.EvidenceIds) && finding.EvidenceIds.All(id => item.Evidence.Any(e => e.Id == id && e.SubmittedTick <= finding.Tick)),
            "A finding requires its noticed case, civil standard, sources, reasons and uncertainty.");
        ValidateParties(finding.Parties, people, households, society);
        ValidateTownParty(town, item, finding.Parties, finding.Tick);
        ValidateJudge(finding.Judge, item, town.Government, people, finding.Tick);
        Check(!TownNonviolentRules.RequiresNewNotice(revision, finding.Parties) &&
            !TownNonviolentRules.JudgeConflict(finding.Judge.AgentId, null, finding.Parties) &&
            TownHearingProcedure.ResponsesClosed(revision.DeadlineTick, finding.Tick, finding.Parties.Select(p => (p.Id, p.RespondingAdultId)),
                item.Responses.Where(r => r.Revision == finding.Revision).Select(r => (r.PartyId, r.AgentId, r.Tick))) &&
            item.Reads.Any(r => r.AgentId == finding.Judge.AgentId && r.Revision == finding.Revision && r.ReadTick <= finding.Tick &&
                item.Evidence.Where(e => e.SubmittedTick <= finding.Tick).All(e => e.SubmittedTick <= r.ReadTick && r.EvidenceIds.Contains(e.Id)) &&
                item.Responses.Where(response => response.Revision == finding.Revision && response.Tick <= finding.Tick).All(response => response.Tick <= r.ReadTick && r.ResponseIds.Contains(TownNonviolentRules.ResponseToken(response)))),
            "A finding cannot bypass a current party's hearing or the judge's actual file read.");
        Check(finding.Result == "unsupported" ? finding.Consequence == "none" : finding.Parties.All(p => p.RespondingAdultId is not null) &&
            finding.EvidenceIds.Any(id => item.Evidence.Any(e => e.Id == id && TownNonviolentRules.SupportsConduct(item, e))),
            "Unsupported allegations or an unrepresented child cannot support an adverse finding.");
        if (finding.Consequence == "censure")
            Check(town.Nonviolent.ConductRecords?.Any(c => c.Id == item.Allegation.IncidentId && c.ActorId == item.Allegation.SubjectId &&
                c.Laws?.Any(l => l.LawId == item.Allegation.LawId && l.Version == item.Allegation.LawVersion && l.PriorNoticeIds is { Count: > 0 }) == true) == true,
                "Censure must retain credible prior law notice; explanation and warning remain available without it.");
    }

    private static void ValidateJudge(TownCaseJudge judge, TownViolationCase item, TownGovernmentState? government,
        Dictionary<string, SocietyInhabitant> people, long at)
    {
        Check(Id(judge.AuthorityId) && Id(judge.AgentId) && people.ContainsKey(judge.AgentId) && judge.AssignedTick >= item.FiledTick && judge.AssignedTick <= at &&
            government is not null && TownGovernmentRules.NonLandScopeAt(government, at, includeEndingTick: true) &&
            (judge.Kind == "non_land_mayor" ? TownGovernmentRules.NonLandAuthorityAt(government, judge.AgentId, judge.AuthorityId, at) :
                judge.Kind == "case_elected" && item.ContestHistory.Any(c => c.Id == judge.AuthorityId && c.Stage == "completed" &&
                    c.WinnerId == judge.AgentId && c.SettledTick <= judge.AssignedTick) && item.JudgeConsents.Any(c => c.AgentId == judge.AgentId &&
                        c.Tick <= at && (c.WithdrawnTick is null || c.WithdrawnTick >= at))), "The judge must have exact historical non-land authority for this decision.");
    }

    private static void ValidateContest(TownCaseJudgeContest contest, TownViolationCase item,
        Dictionary<string, SocietyInhabitant> people, long tick, int day, bool live)
    {
        Check(Id(contest.Id) && contest.Stage is "waiting" or "voting" or "completed" or "failed" or "cancelled" &&
            live == (contest.Stage is "waiting" or "voting") && contest.OpenedTick >= item.FiledTick && contest.OpenedTick <= tick &&
            contest.Voters is not null && contest.Candidates is not null && contest.Ballots is not null && contest.TiedCandidates is not null &&
            contest.Rounds is not null && contest.Rounds.All(r => r is not null), "A case election must retain its real rounds and status.");
        Check(Canonical(contest.Voters) && Canonical(contest.Candidates) && Canonical(contest.TiedCandidates) &&
            contest.Voters.Concat(contest.Candidates).All(people.ContainsKey) && Ballots(contest.Voters, contest.Candidates, contest.Ballots) &&
            contest.Round == contest.Rounds.Count + (contest.Stage == "voting" ? 1 : 0) &&
            contest.Interruptions == contest.Rounds.Count(r => r.Result == "interrupted") &&
            (live ? contest.SettledTick is null : contest.SettledTick >= contest.OpenedTick && contest.SettledTick <= tick) &&
            (contest.Stage != "voting" || contest.RoundOpenedTick >= contest.OpenedTick && contest.RoundDeadlineTick == contest.RoundOpenedTick + day && contest.RoundDeadlineTick > tick) &&
            (contest.Stage != "waiting" || contest.RoundOpenedTick is null && contest.RoundDeadlineTick is null && contest.Voters.Count == 0 && contest.Candidates.Count == 0 && contest.Ballots.Count == 0),
            "Case-election timing, ballots or status are inconsistent.");
        string[]? tied = null;
        for (var index = 0; index < contest.Rounds.Count; index++)
        {
            var r = contest.Rounds[index];
            Check(r.Number == index + 1 && r.OpenedTick >= contest.OpenedTick && r.ClosedTick >= r.OpenedTick && r.ClosedTick <= tick &&
                r.Result is "tie" or "winner" or "failed" or "interrupted" or "cancelled" && Canonical(r.Voters) && Canonical(r.Candidates) &&
                Canonical(r.TiedCandidates) && r.Voters.Concat(r.Candidates).All(people.ContainsKey) && Ballots(r.Voters, r.Candidates, r.Ballots) &&
                (tied is null || r.Candidates.All(tied.Contains)) && (r.Result is not ("winner" or "tie") || r.ClosedTick >= r.OpenedTick + day),
                "Case-election rounds must preserve real ballots, time and the prior tie.");
            var max = r.Candidates.Select(id => r.Ballots.Count(b => b.CandidateId == id)).DefaultIfEmpty().Max();
            var top = max == 0 ? [] : r.Candidates.Where(id => r.Ballots.Count(b => b.CandidateId == id) == max).ToArray();
            if (r.Result == "tie") { Check(top.Length > 1 && r.TiedCandidates.SequenceEqual(top), "A saved tie requires actual tied votes."); tied = top; }
            if (r.Result == "winner") Check(top.Length == 1 && top[0] == contest.WinnerId, "A case winner needs a unique positive vote count.");
        }
        Check(contest.Stage != "completed" || contest.Rounds.Count > 0 && contest.Rounds[^1].Result == "winner" && contest.WinnerId is { } winner && Id(winner) && people.ContainsKey(winner), "A completed case election needs its actual winner.");
    }

    private static void ValidateOffer(TownNonviolentState state, TownRuntimeState town, TownRemedyOffer offer,
        Dictionary<string, SocietyInhabitant> people, long tick, int day)
    {
        var item = state.Cases.SingleOrDefault(c => c.Id == offer.CaseId);
        var finding = item?.Findings.SingleOrDefault(f => f.Id == offer.FindingId);
        Check(finding is { Result: "supported" } && offer.PublishedTick >= finding.Tick && offer.PublishedTick <= tick &&
            offer.Revision > 0 && offer.ResponseDeadlineTick >= offer.PublishedTick && offer.ResponseDeadlineTick - offer.PublishedTick == day && offer.CompletionTicks > 0 &&
            offer.TermsHash == TownRemedyRules.TermsHash(offer.Terms, offer.CompletionTicks) && Text(offer.Reason) &&
            offer.Status is "pending" or "accepted" or "declined" or "unanswered" or "countered" or "superseded" &&
            offer.Terms.All(t => people.ContainsKey(t.ContributorId)) &&
            town.Governance?.Notices.Any(n => n.Id == offer.NoticeId && n.Kind == "remedy" && n.SubjectId == offer.Id && n.PostedTick == offer.PublishedTick) == true,
            "A voluntary offer needs its actual finding, publication, exact terms and response window.");
        Unique(offer.Responses.Select(r => r.AgentId));
        foreach (var response in offer.Responses)
            Check(offer.Terms.Any(t => t.ContributorId == response.AgentId) && response.Revision == offer.Revision && response.TermsHash == offer.TermsHash &&
                response.Kind is "accept" or "decline" or "counter" && response.Tick >= offer.PublishedTick && response.Tick < offer.ResponseDeadlineTick &&
                response.Tick <= tick && response.NoticeId == offer.NoticeId &&
                TownHearingProcedure.HasNotice(offer.NoticeId, offer.PublishedTick, response.AgentId, response.Tick, town.Governance!.Knowledge),
                "Each remedy response must be that contributor's informed answer to the exact offer.");
        Check(offer.Status switch
        {
            "pending" => tick < offer.ResponseDeadlineTick && offer.AgreementId is null && offer.Responses.All(r => r.Kind == "accept") && MissingConsent(offer),
            "accepted" or "superseded" => offer.AgreementId is not null && state.Agreements.Any(a => a.Id == offer.AgreementId && a.OfferId == offer.Id),
            "declined" => offer.AgreementId is null && offer.Responses.Any(r => r.Kind == "decline"),
            "countered" => offer.AgreementId is null && offer.Responses.Any(r => r.Kind == "counter"),
            "unanswered" => tick >= offer.ResponseDeadlineTick && offer.AgreementId is null && offer.Responses.All(r => r.Kind == "accept") && MissingConsent(offer),
            _ => false,
        }, "Offer status cannot invent consent or conceal decline, silence or a counteroffer.");
        if (offer.ReplacesOfferId is { } replaced)
            Check(state.Offers.TakeWhile(o => o.Id != offer.Id).Any(o => o.Id == replaced && o.CaseId == offer.CaseId && o.PublishedTick <= offer.PublishedTick &&
                o.Revision + 1 == offer.Revision && o.Status is "countered" or "accepted" or "superseded"), "A counteroffer must retain the prior exact offer.");
    }
    private static void ValidateAgreement(TownNonviolentState state, TownRestorativeAgreement agreement, long tick)
    {
        var offer = state.Offers.SingleOrDefault(o => o.Id == agreement.OfferId);
        Check(offer is not null && offer.AgreementId == agreement.Id && agreement.OfferRevision == offer.Revision && agreement.TermsHash == offer.TermsHash &&
            agreement.Terms.SequenceEqual(offer.Terms) && agreement.Consents.SequenceEqual(offer.Responses) && agreement.Consents.Count > 0 &&
            agreement.Consents.All(c => c.Kind == "accept") && agreement.Terms.All(t => agreement.Consents.Any(c => c.AgentId == t.ContributorId)) &&
            agreement.AcceptedTick == agreement.Consents.Max(c => c.Tick) && agreement.AcceptedTick <= tick &&
            agreement.DeadlineTick >= agreement.AcceptedTick && agreement.DeadlineTick - agreement.AcceptedTick == offer.CompletionTicks, "An agreement needs every contributor's own exact consent and recorded deadline.");
        var complete = true;
        foreach (var term in agreement.Terms)
        {
            var quantity = state.Effects.Where(e => e.AgreementId == agreement.Id && e.TermId == term.Id).Sum(e => (long)e.Quantity);
            Check(quantity >= 0 && quantity <= term.Quantity, "Performance cannot exceed or invent the agreed quantity.");
            complete &= quantity == term.Quantity;
        }
        Check(agreement.Status == (complete ? "completed" : tick > agreement.DeadlineTick ? "overdue" : "pending"), "Compliance must reflect actual physical performance and the agreed deadline.");
        var previousOffer = TownRemedyRules.ReplacedAgreementOffer(state, offer);
        Check(agreement.ReplacesAgreementId == previousOffer?.AgreementId,
            "An agreement must retain its exact predecessor through the counteroffer history.");
        if (agreement.ReplacesAgreementId is { } replaced)
            Check(replaced != agreement.Id && state.Agreements.TakeWhile(a => a.Id != agreement.Id).Any(a => a.Id == replaced && a.AcceptedTick <= agreement.AcceptedTick && previousOffer!.Id == a.OfferId) &&
                state.Agreements.Count(a => a.ReplacesAgreementId == replaced) == 1, "Renegotiation must retain one exact earlier agreement.");
    }
    private static bool MissingConsent(TownRemedyOffer offer) =>
        offer.Terms.Any(t => !offer.Responses.Any(r => r.AgentId == t.ContributorId && r.Kind == "accept"));
    private static bool Ballots(IReadOnlyList<string> voters, IReadOnlyList<string> candidates, IReadOnlyList<TownMayoralBallot>? ballots) =>
        ballots is not null && ballots.All(b => b is not null && voters.Contains(b.AgentId) && candidates.Contains(b.CandidateId)) &&
        ballots.Select(b => b.AgentId).Distinct(StringComparer.Ordinal).Count() == ballots.Count;
    private static bool Id(string? value) => TownHearingProcedure.Id(value);
    private static bool Text(string? value) => TownHearingProcedure.Text(value);
    private static bool Canonical(IReadOnlyList<string>? values) => values is not null && values.All(Id) && values.SequenceEqual(TownHearingProcedure.Ordered(values));
    private static bool OrderedTimes(IEnumerable<long> values) => values.SequenceEqual(values.Order());
    private static void Unique(IEnumerable<string> values) { var all = values.ToArray(); Check(all.All(Id) && all.Distinct(StringComparer.Ordinal).Count() == all.Length, "Saved civil record identities must be unique."); }
    private static void Check([DoesNotReturnIf(false)] bool valid, string reason) { if (!valid) throw new InvalidDataException(reason); }
}
