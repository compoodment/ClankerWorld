namespace ClankerWorld.GodotClient.UI;

/// <summary>Public case facts shared by Town, tile and property inspection.</summary>
public static class LandHearingText
{
    public static IReadOnlyList<OwnerTownLandHearing> ForInspection(IEnumerable<OwnerTownLandHearing> hearings)
    {
        var matching = hearings.ToArray();
        return matching.Where(Active)
            .Concat(matching.Where(hearing => !Active(hearing)).OrderBy(hearing => hearing.SettledTick)
                .ThenBy(hearing => hearing.Id, StringComparer.Ordinal).TakeLast(1))
            .OrderBy(hearing => hearing.FiledTick).ThenBy(hearing => hearing.Id, StringComparer.Ordinal).ToArray();
    }

    public static string Summary(OwnerTownLandHearing hearing)
    {
        var subject = hearing.Kind == "expiry" ? "permission expiry" : "disputed permission";
        var status = hearing.SettledTick is null ? "awaiting a ruling" :
            hearing.ReopenRequests.Any(request => request.Status == "pending") ? "settled · rehearing awaiting assessment" : "settled";
        var parties = string.Join("; ", hearing.Parties.Select(party => party.Name));
        return $"Land hearing {CaseNumber(hearing.Id)} · {subject} · {status}" +
            (parties.Length > 0 ? " · " + parties : "");
    }

    public static string NoticeSummary(OwnerTownLandHearing hearing, Func<long, string> clock) =>
        $"Notice {hearing.Revision} · published {clock(hearing.PublishedTick)} · " +
        (hearing.SettledTick is null ? "answers due " : "response deadline ") + clock(hearing.DeadlineTick);

    public static string Outcome(OwnerLandHearingOutcome outcome, Func<long, string> clock)
    {
        var action = outcome.Kind switch
        {
            "confirm" => "Confirm the current use permission",
            "renew" => "Renew the use permission",
            "amend" => "Change the use permission",
            "end" => "End the use permission",
            "reject" => "Reject the unsupported request; retain current rights",
            _ => "Review the use permission",
        };
        return action + (outcome.HouseholdName is { } household ? " for " + household : "") +
            (outcome.AgreedEndTick is { } end ? " · agreed end " + clock(end) : "");
    }

    public static IReadOnlyList<string> Details(OwnerTownLandHearing hearing, Func<long, string> clock,
        IReadOnlyList<OwnerTownLaw>? laws = null)
    {
        var lines = new List<string>
        {
            Summary(hearing),
            "Plot: " + Tiles(hearing.Tiles),
            "Requested: " + Outcome(hearing.RequestedOutcome, clock),
            NoticeSummary(hearing, clock) + ". Publication does not mean the notice was read.",
        };
        foreach (var filing in hearing.Filings)
            lines.Add((filing.AgentName is { } filer ? "Filed by " + filer : "Automatic expiry review") +
                " · " + clock(filing.Tick) + ": " + filing.Text);
        foreach (var party in hearing.Parties)
        {
            string[] required = party.Kind == "town"
                ? party.RepresentativeId is { } representative ? [representative] : Array.Empty<string>()
                : party.AdultIds.ToArray();
            if (required.Length == 0)
            {
                lines.Add(party.Name + ": no eligible adult representative; a response has not been waived.");
                continue;
            }
            foreach (var adult in required)
            {
                var ordinal = party.AdultIds.ToList().IndexOf(adult);
                var name = adult == party.RepresentativeId ? party.RepresentativeName ?? "Town representative"
                    : ordinal >= 0 && ordinal < party.AdultNames.Count ? party.AdultNames[ordinal] : "Household adult";
                var response = hearing.Responses.LastOrDefault(item => item.Revision == hearing.Revision &&
                    item.PartyId == party.Id && item.AgentId == adult);
                var state = response is { Kind: "waive" } ? "explicitly waived a response"
                    : response is not null ? "answered" : "has not answered";
                lines.Add($"{party.Name} · {name}: {state}" +
                    (response is not null ? " · " + clock(response.Tick) + (response.Text.Length > 0 ? ": " + response.Text : "") :
                        party.NoticeAwareAdultIds.Contains(adult, StringComparer.Ordinal) ? " · learned the formal notice" : " · has not learned the formal notice"));
            }
        }
        lines.Add(hearing.Judge is { } judge ? "Adjudicator: " + Judge(judge) + " · assigned " + clock(judge.AssignedTick)
            : hearing.SettledTick is not null && !hearing.ReopenRequests.Any(request => request.Status == "pending")
                ? "The hearing is settled; each ruling names its adjudicator."
            : hearing.SettledTick is not null ? "Waiting for an eligible, willing adjudicator to assess the rehearing request; current rights remain in effect."
            : "Waiting for an eligible, willing adjudicator; existing rights remain protected.");
        foreach (var term in hearing.JudgeHistory)
            lines.Add("Former adjudicator: " + Judge(term.Judge) + " · ended " + clock(term.EndedTick) + " · " + Words(term.Reason));
        if (hearing.JudgeElection is { } election) AddElection(lines, election, clock, false);
        else if (hearing.LatestJudgeElection is { } latest) AddElection(lines, latest, clock, true);

        var evidenceNames = hearing.Evidence.Select((evidence, index) => (evidence.Id, Name: "Evidence " + (index + 1)))
            .ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        var reopenNames = hearing.ReopenRequests.Select((request, index) => (request.Id, Name: "Rehearing request " + (index + 1)))
            .ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        foreach (var evidence in hearing.Evidence)
        {
            var kind = evidence.Kind switch { "record" => "Verified record", "observation" => "Observation", _ => "Allegation" };
            var acquisition = evidence.Acquisition switch
            {
                "firsthand" => "observed directly",
                "record_inspection" => "inspected a public record",
                "relay" => "relayed testimony",
                _ => "submitted statement",
            };
            lines.Add($"{evidenceNames[evidence.Id]} · {kind}: {RecordText(hearing, evidence, clock)}");
            lines.Add($"Source: {evidence.SourceAgentName} · {acquisition} · {clock(evidence.ObservedTick)}; " +
                $"submitted by {evidence.SubmittedByName} · {clock(evidence.SubmittedTick)}." +
                (evidence.SourceRecordId is not null ? " The source is an inspected public record." : ""));
        }
        foreach (var read in hearing.Reads.Where(read => read.Revision == hearing.Revision)
            .GroupBy(read => read.AgentId).Select(group => group.OrderBy(read => read.ReadTick).Last()))
        {
            var records = read.EvidenceIds.Select(id => evidenceNames.GetValueOrDefault(id, "Earlier evidence"))
                .Concat(read.ReopenRequestIds.Select(id => reopenNames.GetValueOrDefault(id, "Earlier rehearing request"))).ToArray();
            lines.Add((read.SourceAgentName is { } source ? "Evidence learned by " + read.AgentName + " from " + source
                    : "Public file read by " + read.AgentName) + " · " + clock(read.ReadTick) +
                (records.Length > 0 ? " · " + string.Join(", ", records) : " · no evidence or rehearing request was available at that time") + ".");
        }
        for (var index = 0; index < hearing.Rulings.Count; index++)
        {
            var ruling = hearing.Rulings[index];
            lines.Add($"Ruling {index + 1}" + (index > 0 ? " after reopening" : "") + ": " + Outcome(ruling.Outcome, clock) +
                " · " + clock(ruling.Tick) + " · " + Judge(ruling.Judge));
            lines.Add("Exact plot: " + Tiles(ruling.Tiles) + ". Reason: " + ruling.Reasons);
            if (ruling.EvidenceIds.Count > 0)
                lines.Add("Evidence cited: " + string.Join(", ", ruling.EvidenceIds.Select(id => evidenceNames.GetValueOrDefault(id, "Earlier evidence"))) + ".");
            if (ruling.LawIds.Count > 0)
                lines.Add("Laws cited: " + string.Join("; ", ruling.LawIds.Select(id => LawName(id, hearing, laws))) + ".");
        }
        foreach (var request in hearing.ReopenRequests)
        {
            var grounds = request.Kind == "procedural_error" ? "procedural error" : "material new evidence";
            var status = request.Status == "pending" ? "awaiting assessment" : Words(request.Status);
            lines.Add($"Rehearing requested by {request.AgentName} · {reopenNames[request.Id]} · {grounds} · {status}: {request.Reasons}");
            if (request.AssessedBy is { } assessor)
                lines.Add("Assessed by " + Judge(assessor) + (request.AssessedTick is { } assessed ? " · " + clock(assessed) : "") +
                    (request.Assessment is { } assessment ? ": " + assessment : "."));
        }
        lines.Add("Use permission changes leave Town title, household membership, private buildings, crops and goods with their owners.");
        return lines.Select(GameUiText.PlainEllipses).ToArray();
    }

    private static string RecordText(OwnerTownLandHearing hearing, OwnerLandHearingEvidence evidence, Func<long, string> clock)
    {
        if (evidence.PermissionRecord is { } permission)
            return (evidence.RecordPartyName ?? "The household") + " has recorded use permission for " +
                Tiles(permission.Tiles) + " · granted " + clock(permission.GrantedTick) +
                (permission.AgreedEndTick is { } end ? " · agreed end " + clock(end) : " · no agreed end") + ".";
        if (evidence.TitleRecord is { } title)
            return (evidence.RecordPartyName ?? "The Town") + " holds formal title covering " + title.Tiles.Count + " recorded tiles" +
                " · recorded " + clock(title.RecordedTick) + ".";
        if (hearing.Rulings.FirstOrDefault(ruling => ruling.Id == evidence.SourceRecordId) is { } ruling)
            return "Recorded earlier ruling: " + Outcome(ruling.Outcome, clock) + " · " + clock(ruling.Tick) +
                " · " + Judge(ruling.Judge) + ".";
        return evidence.Text;
    }

    private static string LawName(string token, OwnerTownLandHearing hearing, IReadOnlyList<OwnerTownLaw>? laws)
    {
        if (hearing.Evidence.FirstOrDefault(evidence => evidence.SourceRecordId == token) is { } record)
            return record.Text + " (the inspected law text)";
        var id = token.Split('@', 2)[0];
        return laws?.FirstOrDefault(law => law.Id == id)?.Subject ?? "An earlier recorded law";
    }

    private static void AddElection(List<string> lines, OwnerLandHearingElection election, Func<long, string> clock, bool latest)
    {
        lines.Add((latest ? "Last case election: " : "Case election: ") + Words(election.Stage) + " · round " + election.Round);
        if (election.Stage == "voting" && election.DeadlineTick is { } deadline) lines.Add("Voting closes " + clock(deadline));
        if (election.Stage == "waiting") lines.Add("Waiting for the Town's other election to finish.");
        if (election.Candidates.Count > 0) lines.Add(string.Join(" · ", election.Candidates.Select(candidate => $"{candidate.Name}: {candidate.Votes} votes")));
        if (election.WinnerName is { } winner) lines.Add("Selected for this case: " + winner);
        if (election.Reason is { } reason) lines.Add(Words(reason));
    }

    private static string Judge(OwnerLandHearingJudge judge) => judge.AgentName +
        (judge.Kind == "case_elected" ? " · acting mayor for this case" : " · elected land mayor");
    private static string Tiles(IReadOnlyList<OwnerWorldPosition> tiles) => string.Join(", ", tiles.Select(tile => $"({tile.X}, {tile.Y})"));
    private static string CaseNumber(string id) => id[(id.LastIndexOf(':') + 1)..];
    private static bool Active(OwnerTownLandHearing hearing) => hearing.SettledTick is null ||
        hearing.ReopenRequests.Any(request => request.Status == "pending");
    private static string Words(string text) => text.Replace('_', ' ');
}
