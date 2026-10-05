using System.Globalization;

namespace ClankerWorld.GodotClient.UI;

/// <summary>Human-readable public hearing records, never instructions to impose a remedy.</summary>
public static class NonviolentHearingText
{
    public static IReadOnlyList<string> Details(OwnerTownNonviolentCase item, Func<long, string> clock, int ticksPerDay = 1_440)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        var lines = new List<string>
        {
            $"Non-land hearing · {item.SubjectName} · {Words(item.Status)}",
            $"Reported conduct: {Words(item.ConductKind)} at ({item.Position.X}, {item.Position.Y}) · {clock(item.ConductTick)}.",
            "Allegation: " + item.Statement,
        };
        if (item.ApplicableLaw is { } law)
        {
            lines.Add($"Law at the reported time: {law.Subject} · version {law.Version}: {law.Rule}");
            lines.Add("Scope: " + (law.Scope switch
            {
                "resident_duty" => "residents wherever they are",
                "site" => "the recorded site: " + string.Join(", ", law.Site.Select(tile => $"({tile.X}, {tile.Y})")),
                _ => "the Town's formally claimed land, including visitors",
            }) + " · effective from " + clock(law.AdoptedTick) +
                (law.EndedTick is { } ended ? " until " + clock(ended) : "") + ".");
        }
        else lines.Add($"The report cites law version {item.AllegedLawVersion}; its wording is unavailable in this view.");
        foreach (var filing in item.Filings)
            lines.Add($"Filed by {filing.AgentName} · {clock(filing.Tick)}: {filing.Statement}");
        foreach (var revision in item.Revisions)
        {
            lines.Add($"Notice {revision.Number} · published {clock(revision.PublishedTick)} · responses due {clock(revision.DeadlineTick)}.");
            foreach (var party in revision.Parties)
            {
                var support = party.RespondingAdultName is null ? "no eligible adult response or caregiver support recorded"
                    : party.Role == "town" ? "Town representative: " + party.RespondingAdultName
                    : party.RespondingAdultId == party.SubjectId ? "responds personally" : "caregiver support: " + party.RespondingAdultName;
                lines.Add($"At this notice: {party.SubjectName} · {Words(party.Role)} · {support}. " +
                    (party.NoticeAware ? "The responding adult learned the notice." : "No receipt of this notice is recorded for the responding adult."));
            }
            foreach (var response in item.Responses.Where(response => response.Revision == revision.Number))
                lines.Add(response.AgentName + (response.AgentId != response.RepresentedAgentId ? " supporting " + response.RepresentedAgentName : "") +
                    (response.Kind == "waive" ? " explicitly waived the response" : " answered") + " · " + clock(response.Tick) +
                    (response.Text.Length > 0 ? ": " + response.Text : "."));
        }
        if (item.Status == "pending") lines.Add("Current required responders:");
        foreach (var party in item.CurrentParties)
        {
            if (party.RespondingAdultId is not { } responder)
            {
                lines.Add(party.SubjectName + ": no current eligible " + (party.Role == "town" ? "Town representative" : "adult or caregiver") +
                    "; no response has been waived on their behalf.");
                continue;
            }
            var currentRevision = item.Revisions[^1].Number;
            var response = item.Responses.LastOrDefault(response => response.Revision == currentRevision &&
                response.PartyId == party.Id && response.AgentId == responder);
            var duty = party.Role == "town" ? "Town representative" : responder == party.SubjectId ? "personal response" : "caregiver support";
            lines.Add(party.SubjectName + " · " + duty + ": " + party.RespondingAdultName + " · " +
                (response?.Kind == "waive" ? "explicitly waived this response" : response is not null ? "answered this notice"
                    : party.NoticeAware ? "learned this notice; has not answered" : "has not learned this notice") + ".");
        }
        lines.Add("Publication alone is not receipt. Silence is not consent or proof of a violation.");
        lines.Add(item.Judge is { } judge ? "Adjudicator: " + Judge(judge) + " · assigned " + clock(judge.AssignedTick) + "."
            : item.Status == "pending" ? "Formal hearing pending: no eligible willing adjudicator is assigned. Council mediation remains voluntary."
            : "Each recorded finding names its adjudicator.");
        foreach (var term in item.JudgeHistory)
            lines.Add("Former adjudicator: " + Judge(term.Judge) + " · ended " + clock(term.EndedTick) + " · " + Words(term.Reason));
        if (item.JudgeElection is { } election) Election(lines, election, clock, false);
        else if (item.LatestJudgeElection is { } latest) Election(lines, latest, clock, true);
        var evidenceNames = item.Evidence.Select((evidence, index) => (evidence.Id, Name: "Evidence " + (index + 1)))
            .ToDictionary(entry => entry.Id, entry => entry.Name, StringComparer.Ordinal);
        string EvidenceNames(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => evidenceNames.GetValueOrDefault(id, "Earlier evidence")));
        foreach (var evidence in item.Evidence)
        {
            var kind = evidence.Kind switch { "record" => "inspected record", "observation" => "firsthand observation", _ => "allegation" };
            lines.Add($"{evidenceNames[evidence.Id]} · {kind}: {evidence.Text}");
            lines.Add($"Source: {evidence.SourceAgentName} · {Words(evidence.Acquisition)} · {clock(evidence.ObservedTick)}; " +
                $"submitted by {evidence.SubmittedByName} · {clock(evidence.SubmittedTick)}.");
        }
        foreach (var read in item.Reads)
            lines.Add((read.SourceAgentName is { } source ? read.AgentName + " learned the file from " + source
                : read.AgentName + " inspected the public file") + $" · notice {read.Revision} · {clock(read.ReadTick)}" +
                (read.EvidenceIds.Count > 0 ? " · " + EvidenceNames(read.EvidenceIds) : " · no evidence in that reading") + ".");
        for (var index = 0; index < item.Findings.Count; index++)
        {
            var finding = item.Findings[index];
            lines.Add($"Finding {index + 1} · " + (finding.Result == "supported" ? "violation supported" : "violation not supported") +
                " · " + clock(finding.Tick) + " · " + Judge(finding.Judge) + ".");
            lines.Add("Standard: " + Words(finding.Standard) + ". This is the adjudicator's assessment of the evidence.");
            lines.Add("Reasons: " + finding.Reasons);
            lines.Add("Uncertainty: " + (finding.Uncertainty.Length > 0 ? finding.Uncertainty : "none recorded") + ".");
            lines.Add("Social outcome: " + Words(finding.Consequence) + ".");
            if (finding.EvidenceIds.Count > 0) lines.Add("Evidence cited: " + EvidenceNames(finding.EvidenceIds) + ".");
            foreach (var party in finding.Parties)
                lines.Add("At this finding: " + party.SubjectName + (party.RespondingAdultId != party.SubjectId && party.RespondingAdultName is { } caregiver
                    ? " · supported by " + caregiver : "") + ".");
        }
        foreach (var request in item.ReopenRequests)
        {
            lines.Add($"Rehearing requested by {request.AgentName} · {Words(request.Kind)} · {Words(request.Status)}: {request.Reasons}");
            if (request.EvidenceIds.Count > 0) lines.Add("Evidence cited: " + EvidenceNames(request.EvidenceIds) + ".");
            if (request.AssessedBy is { } assessor)
                lines.Add("Assessed by " + Judge(assessor) + (request.AssessedTick is { } assessed ? " · " + clock(assessed) : "") +
                    (request.Assessment is { } assessment ? ": " + assessment : "."));
        }
        foreach (var offer in item.Offers)
        {
            var findingNumber = item.Findings.ToList().FindIndex(finding => finding.Id == offer.FindingId) + 1;
            lines.Add($"Voluntary remedy offer · {OfferStatus(offer.Status)} · terms {offer.Revision}" +
                (findingNumber > 0 ? $" · finding {findingNumber}" : "") + ".");
            lines.Add("Published " + clock(offer.PublishedTick) + " · answer deadline " + clock(offer.ResponseDeadlineTick) + ". " + offer.Reason);
            lines.Add("Proposed completion period: " + ((decimal)offer.CompletionTicks / ticksPerDay).ToString("0.########", CultureInfo.InvariantCulture) + " world days after acceptance.");
            if (offer.ReplacesOfferId is not null) lines.Add("These are proposed replacement terms; earlier responses remain in the history.");
            foreach (var term in offer.Terms) lines.Add("Offered: " + Term(term));
            foreach (var contributor in offer.Terms.DistinctBy(term => term.ContributorId))
                lines.Add(contributor.ContributorName + (offer.NoticeAwareContributorIds.Contains(contributor.ContributorId, StringComparer.Ordinal)
                    ? " learned these offer terms." : " has no recorded receipt of these offer terms."));
            foreach (var response in offer.Responses)
                lines.Add(response.AgentName + " · " + Response(response.Kind) + $" terms {response.Revision} · " + clock(response.Tick) +
                    (response.Reason is { } reason ? ": " + reason : "."));
            if (offer.Status != "accepted") lines.Add("An offer does not reserve goods or authorize work. Each contributor decides personally.");
        }
        foreach (var agreement in item.Agreements)
        {
            lines.Add("Voluntary agreement · " + (agreement.Superseded ? "superseded by a later agreement; retained history" : agreement.Status switch
            {
                "completed" => "completed through recorded work",
                "overdue" => "overdue; incomplete work remains voluntary",
                _ => "accepted; work still pending",
            }) + ".");
            lines.Add("Accepted " + clock(agreement.AcceptedTick) + " · agreed completion deadline " + clock(agreement.DeadlineTick) + ".");
            if (agreement.ReplacesAgreementId is not null) lines.Add("This agreement replaces earlier terms with new personal consent; completed effects remain recorded.");
            foreach (var consent in agreement.Consents)
                lines.Add(consent.AgentName + " personally accepted these terms · " + clock(consent.Tick) + ".");
            foreach (var term in agreement.Terms)
            {
                var completed = agreement.Effects.Where(effect => effect.TermId == term.Id).Sum(effect => effect.Quantity);
                lines.Add($"Recorded completion {completed}/{term.Quantity}: " + Term(term));
            }
            foreach (var effect in agreement.Effects)
                lines.Add("Actual " + (effect.Kind == "repair_equipment" ? "repair" : "delivery") + " by " + effect.ActorName +
                    " · " + effect.Quantity + (effect.ItemKind is { } itemKind ? " " + Words(itemKind) : " completed repair") +
                    (effect.BeneficiaryName is { } beneficiary ? " to " + beneficiary : "") +
                    (effect.TargetName is { } target ? " · " + target : "") + " · " + clock(effect.Tick) + ".");
        }
        if (item.Offers.Count > 0 || item.Agreements.Count > 0)
            lines.Add("Declining, silence or missed deadlines do not create a new violation or automatic punishment. Family members owe nothing without their own agreement.");
        return lines.Select(GameUiText.PlainEllipses).ToArray();
    }

    private static string Term(OwnerRemedyTerm term) => term.ContributorName + (term.Kind switch
    {
        "repair_equipment" => " repairs " + (term.TargetName ?? "the named equipment"),
        "public_service_goods" => " delivers " + term.Quantity + " " + Words(term.ItemKind ?? "goods") + " to " + (term.BeneficiaryName ?? "the named Town") + " stock" +
            (term.TargetName is { } target ? " at " + target : ""),
        _ => " returns " + term.Quantity + " " + Words(term.ItemKind ?? "goods") + " to " + (term.BeneficiaryName ?? "the named recipient"),
    }) + ".";
    private static string OfferStatus(string status) => status switch
    {
        "pending" => "awaiting personal responses",
        "accepted" => "accepted; see the separate work agreement",
        "declined" => "declined",
        "unanswered" => "unanswered when the response period ended",
        "countered" => "counteroffer proposed",
        "superseded" => "replaced by later terms",
        _ => Words(status),
    };
    private static string Response(string kind) => kind switch
    {
        "accept" => "personally accepted",
        "decline" => "declined",
        "counter" => "proposed different",
        _ => Words(kind),
    };
    private static string Judge(OwnerCaseJudge judge) => judge.AgentName + (judge.Kind == "case_elected"
        ? " · independently elected for this case" : " · separately authorized non-land adjudicator");
    private static void Election(List<string> lines, OwnerCaseElection election, Func<long, string> clock, bool latest)
    {
        lines.Add((latest ? "Last non-land case election: " : "Non-land case election: ") + Words(election.Stage) + " · round " + election.Round);
        if (election.Stage == "voting" && election.DeadlineTick is { } deadline) lines.Add("Voting closes " + clock(deadline));
        if (election.Stage == "waiting") lines.Add("Waiting for the Town's other election to finish.");
        if (election.Candidates.Count > 0) lines.Add(string.Join(" · ", election.Candidates.Select(candidate => $"{candidate.Name}: {candidate.Votes} votes")));
        if (election.WinnerName is { } winner) lines.Add("Selected for this case: " + winner);
        if (election.Reason is { } reason) lines.Add(Words(reason));
    }
    private static string Words(string value) => value.Replace('_', ' ');
}
