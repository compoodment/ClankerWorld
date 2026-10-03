using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static string NonviolentToken(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string NonviolentReportToken(TownViolationAllegation report) => NonviolentToken(report.IncidentId + "|" + report.LawId + "|" + report.LawVersion.ToString(CultureInfo.InvariantCulture));
    private static string NonviolentOfferToken(TownRemedyOffer offer) => offer.Id + ":" + offer.Revision.ToString(CultureInfo.InvariantCulture) + ":" + offer.TermsHash;
    private static TownCaseRevision NonviolentRevision(TownViolationCase item) => item.Revisions[^1];
    private static string NonviolentCaseToken(TownViolationCase item) => item.Id + ":" + NonviolentRevision(item).Number.ToString(CultureInfo.InvariantCulture);

    private static string NonviolentActionToken(TownViolationCase item) => NonviolentCaseToken(item) + ":" +
        TownHearingProcedure.Digest(new { item.Evidence, item.Responses, item.Findings, item.Judge, item.ReopenRequests });

    private TownCaseParty[] NonviolentParties(TownRuntimeState town, TownViolationCase item) =>
        TownNonviolentPartyRules.CurrentParties(town, item, society.Checkpoint, WorldTick);

    private bool NonviolentMayInspect(TownRuntimeState town, TownViolationCase item, string actor) =>
        inhabitants.ContainsKey(actor) && society.Checkpoint.GetInhabitant(actor).AgeBand != SocietyAgeBand.Infant &&
        (TownAdults(town).Contains(actor, StringComparer.Ordinal) || item.Judge?.AgentId == actor ||
            item.Filings.Any(filing => filing.AgentId == actor) ||
            town.Nonviolent.Offers.Any(offer => offer.CaseId == item.Id && offer.Terms.Any(term => term.ContributorId == actor)) || NonviolentParties(town, item).Any(party =>
                party.SubjectId == actor || party.RespondingAdultId == actor));
    private bool NonviolentReadCurrent(TownViolationCase item, string actor) => TownNonviolentRules.ReadCurrent(item, actor, WorldTick);
    private bool NonviolentKnows(TownRuntimeState town, TownViolationCase item, string actor) =>
        town.Governance!.Knowledge.Any(receipt => receipt.AgentId == actor && receipt.NoticeId == NonviolentRevision(item).NoticeId && receipt.LearnedTick <= WorldTick);
    private bool NonviolentConflict(TownRuntimeState town, TownViolationCase item, string actor) =>
        item.Filings.Any(filing => filing.AgentId == actor) || item.DirectStakeIds.Contains(actor, StringComparer.Ordinal) ||
        NonviolentParties(town, item).Any(party => party.SubjectId == actor || party.RespondingAdultId == actor ||
            party.HouseholdId is { } household && household == HouseholdFor(actor));
    private bool NonviolentJudgeValid(TownRuntimeState town, TownViolationCase item, TownCaseJudge judge)
    {
        if (!TownAdults(town).Contains(judge.AgentId, StringComparer.Ordinal) || NonviolentConflict(town, item, judge.AgentId) ||
            town.Government is not { } government || government.Arrangement.NonLand != TownArrangementRules.Mayor) return false;
        if (judge.Kind == "case_elected") return item.JudgeConsents.Any(consent => consent.AgentId == judge.AgentId && consent.WithdrawnTick is null) &&
            item.ContestHistory.Any(contest => contest.Id == judge.AuthorityId && contest.Stage == "completed" && contest.WinnerId == judge.AgentId);
        return judge.Kind == "non_land_mayor" && TownGovernmentRules.CurrentNonLandAuthority(government, WorldTick) is { } authority &&
            authority.HolderId == judge.AgentId && authority.AuthorityId == judge.AuthorityId;
    }
    private static bool TownNonviolentElectionBusy(TownRuntimeState town) => town.Nonviolent.Cases.Any(item => item.Contest is { Stage: "voting" });
    private bool MayVisitNonviolentCase(string actor, TownRuntimeState town) => inhabitants.ContainsKey(actor) &&
        CanWalkToCivicBoard(actor, town) && (town.Nonviolent.Cases.Any(item => NonviolentMayInspect(town, item, actor) && NonviolentKnows(town, item, actor)) ||
            NonviolentKnownReports(town, actor).Count > 0);

    private (TownGovernanceState Council, TownNonviolentState Nonviolent) AdvanceTownNonviolent(TownRuntimeState town,
        TownGovernanceState council, TownGovernmentState government)
    {
        var state = TownRemedyRules.Advance(town.Nonviolent, WorldTick);
        foreach (var proposal in council.Proposals.Where(proposal => proposal.Kind == "law_case" && proposal.Status == "passed" && proposal.NonviolentRequest is not null).ToArray())
        {
            if (state.Cases.Any(item => item.Filings.Any(filing => filing.AuthorityId == proposal.Id)) ||
                government.Arrangement.Ordinary is not (TownArrangementRules.Council or TownArrangementRules.ElectedCouncil or TownArrangementRules.AllAdultCouncil) ||
                !council.Members.Contains(proposal.AuthorId, StringComparer.Ordinal) || !TownAdults(town).Contains(proposal.AuthorId, StringComparer.Ordinal)) continue;
            var request = proposal.NonviolentRequest!;
            if (state.Cases.Any(item => item.Key == TownNonviolentRules.CaseKey(town.Id, request.Allegation) && item.Status != "pending")) continue;
            var report = NonviolentKnownReports(town with { Governance = council, Nonviolent = state }, proposal.AuthorId)
                .FirstOrDefault(report => NonviolentReportToken(report.Allegation) == NonviolentReportToken(request.Allegation));
            if (report.Allegation is null) continue;
            (council, state) = OpenNonviolentCase(town, council, state, request.Allegation,
                new(proposal.AuthorId, "town", WorldTick, request.Statement, request.Allegation.SourceEvidenceIds, proposal.Id), report.Evidence);
        }
        foreach (var item in state.Cases.Where(item => item.Status == "pending").ToArray())
        {
            var before = NonviolentRevision(item).Number;
            state = TownNonviolentRules.ReviseParties(state, item.Id, NonviolentParties(town, item), WorldTick, CivicDay, "notice:" + (council.Notices.Count + 1));
            var revised = state.Cases.Single(current => current.Id == item.Id);
            if (NonviolentRevision(revised).Number != before) council = PostNonviolentNotice(council, revised);
        }
        state = state with { Cases = state.Cases.Select(item => item with
        { DirectStakeIds = item.Filings.Select(filing => filing.AgentId).Concat(NonviolentParties(town, item).Select(party => party.SubjectId)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() }).ToArray() };
        var parties = state.Cases.ToDictionary(item => item.Id, item => (IReadOnlyList<TownCaseParty>)NonviolentParties(town, item), StringComparer.Ordinal);
        (state, council) = TownCaseJudgeRules.Advance(state, council, government, TownAdults(town), LandHearingHouseholds(), WorldTick, CivicDay,
            TownLandHearingElectionBusy(town) || government.Contest is { Stage: "voting" or "ready" } || council.Election is { Stage: "ready" }, parties);
        return (council, state);
    }

    private static TownGovernanceState PostNonviolentNotice(TownGovernanceState council, TownViolationCase item) =>
        TownGovernanceRules.PostNotice(council, "law_case", NonviolentCaseToken(item),
            "A reported act is alleged to breach a Town law. The named people may answer with caregiver support where needed. No guilt, payment or work is presumed.", NonviolentRevision(item).PublishedTick);
    private void NonviolentEvent(string kind, TownRuntimeState town, string subject, string actor) =>
        AppendEvent("law_case_" + kind, $"{town.Id}|{subject}|{actor}", CivicBoard(town));
    private static string NonviolentText(string? text) => CognitionDecisionResponse.NormalizeIdentityText(text) ??
        throw new InvalidOperationException("A legal submission needs a clear personal statement.");

    private static string NonviolentExcerpt(string text, int limit) => text[..Math.Min(limit, text.Length)];

    private string NonviolentKnownAllegationText(TownRuntimeState town, TownViolationAllegation allegation)
    {
        var version = town.Government!.Laws.Single(law => law.Id == allegation.LawId).Versions.Single(version => version.Version == allegation.LawVersion);
        return NonviolentExcerpt(society.Checkpoint.GetInhabitant(allegation.SubjectId).Name, 80) + " allegedly " +
            allegation.ConductKind.Replace('_', ' ') + " at (" + allegation.Position.X + "," + allegation.Position.Y + ") at world tick " +
            allegation.ConductTick.ToString(CultureInfo.InvariantCulture) + ". Known source: " + NonviolentExcerpt(allegation.Statement, 256) +
            ". Applicable law version " + allegation.LawVersion.ToString(CultureInfo.InvariantCulture) + ": " +
            NonviolentExcerpt(TownLawRules.Text(version.Subject, version.Rule), 512);
    }

    private string NonviolentEvidenceChoices(TownRuntimeState town, TownViolationCase item, string actor)
    {
        var reads = item.Reads.Where(read => read.AgentId == actor && read.Revision == NonviolentRevision(item).Number && read.ReadTick <= WorldTick).ToArray();
        var known = reads.SelectMany(read => read.EvidenceIds).ToHashSet(StringComparer.Ordinal);
        var responses = reads.SelectMany(read => read.ResponseIds).ToHashSet(StringComparer.Ordinal);
        var findings = reads.SelectMany(read => read.FindingIds).ToHashSet(StringComparer.Ordinal);
        return (reads.Length == 0 ? "" : " Inspected allegation: " + NonviolentKnownAllegationText(town, item.Allegation) + ".") +
            " Inspected evidence: " + string.Join("; ", item.Evidence.Where(evidence => known.Contains(evidence.Id)).TakeLast(8)
            .Select(evidence => evidence.Id + " (" + evidence.Kind + ", " + evidence.Acquisition + "): " + NonviolentExcerpt(evidence.Text, 256))) +
            ". Inspected responses: " + string.Join("; ", item.Responses.Where(response => responses.Contains(TownNonviolentRules.ResponseToken(response))).TakeLast(8)
                .Select(response => NonviolentExcerpt(society.Checkpoint.GetInhabitant(response.AgentId).Name, 80) + " " + response.Kind + ": " + NonviolentExcerpt(response.Text, 256))) +
            ". Inspected prior findings: " + string.Join("; ", item.Findings.Where(finding => findings.Contains(finding.Id)).TakeLast(4)
                .Select(finding => finding.Id + " " + finding.Result + "/" + finding.Consequence + ": " + NonviolentExcerpt(finding.Reasons, 256) +
                    "; uncertainty: " + NonviolentExcerpt(finding.Uncertainty, 160))) + ".";
    }
}
