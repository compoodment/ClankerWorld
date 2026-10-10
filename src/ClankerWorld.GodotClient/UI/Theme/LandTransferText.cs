namespace ClankerWorld.GodotClient.UI;

/// <summary>Public exact permission terms and the households' separate personal consent.</summary>
public static class LandTransferText
{
    public static IReadOnlyList<OwnerLandTransfer> ForInspection(IEnumerable<OwnerLandTransfer> transfers)
    {
        var matching = transfers.ToArray();
        return matching.Where(transfer => transfer.Status == "pending")
            .Concat(matching.Where(transfer => transfer.Status != "pending").OrderBy(transfer => transfer.SettledTick)
                .ThenBy(transfer => transfer.Id, StringComparer.Ordinal).TakeLast(1))
            .OrderBy(transfer => transfer.ProposedTick).ThenBy(transfer => transfer.Id, StringComparer.Ordinal).ToArray();
    }

    public static string Summary(OwnerLandTransfer transfer) => "Permission transfer " +
        transfer.Id[(transfer.Id.LastIndexOf(':') + 1)..] + " · " + (transfer.Status == "pending" && transfer.Price is not null && transfer.Parties.All(party => party.AdultIds.Count > 0 && party.AcceptedAdultIds.Count == party.AdultIds.Count)
            ? "awaiting goods payment" : Status(transfer.Status)) + " · " +
        string.Join("; ", transfer.Parties.Where(party => party.Kind == "source").Select(party => party.HouseholdName)) +
        " → " + transfer.TargetHouseholdName;

    public static string Acceptance(OwnerLandTransfer transfer) => string.Join("; ", transfer.Parties.Select(party =>
        party.HouseholdName + ": " + party.AcceptedAdultIds.Count + "/" + party.AdultIds.Count + " " +
        Roster(party.RosterKind) + " adults accepted"));

    public static IReadOnlyList<string> Terms(OwnerLandTransfer transfer, Func<long, string> clock) =>
        transfer.RightVersions.Select(version =>
        {
            var right = version.Right;
            var household = transfer.Parties.FirstOrDefault(party => party.HouseholdId == right.HouseholdId)?.HouseholdName ?? "The source household";
            var plot = right.Tiles.Where(tile => transfer.Tiles.Any(point => point.X == tile.X && point.Y == tile.Y)).ToArray();
            return "Recorded terms from " + household + ": " + Tiles(plot) + " · granted " + clock(right.GrantedTick) +
                (right.AgreedEndTick is { } end ? " · agreed end " + clock(end) : " · no agreed end") + ". These terms remain unchanged.";
        }).ToArray();

    public static IReadOnlyList<string> Details(OwnerLandTransfer transfer, Func<long, string> clock)
    {
        var lines = new List<string>
        {
            Summary(transfer),
            "Exact plot: " + Tiles(transfer.Tiles),
            "Proposed by " + transfer.FilerName + " · " + clock(transfer.ProposedTick) + ". Proposing supplies no acceptance.",
            Acceptance(transfer),
        };
        if (transfer.Price is { } price)
        {
            lines.Add("Goods price: " + price.Quantity + " " + price.ItemKind + " to " + price.SellerHouseholdName + ". No money price.");
            lines.Add(transfer.Payment is { } payment
                ? "Paid by " + payment.BuyerName + " to " + payment.SellerName + " for the household at (" + payment.Position.X + ", " + payment.Position.Y + ") · " + clock(payment.Tick) + "."
                : "Payment has not been made. The buyer must carry the agreed goods to meet a seller adult at the notice place before permission moves.");
        }
        lines.AddRange(Terms(transfer, clock));
        foreach (var party in transfer.Parties)
        {
            if (party.AdultIds.Count == 0) lines.Add(party.HouseholdName + ": no eligible adult; the transfer cannot complete.");
            for (var index = 0; index < party.AdultIds.Count; index++)
            {
                var adult = party.AdultIds[index];
                var response = transfer.Responses.LastOrDefault(item => item.HouseholdId == party.HouseholdId && item.AgentId == adult);
                var accepted = party.AcceptedAdultIds.Contains(adult, StringComparer.Ordinal);
                var name = index < party.AdultNames.Count ? party.AdultNames[index] : "Household adult";
                lines.Add(party.HouseholdName + " · " + name + ": " + (accepted ? "personally accepted" : response is { Kind: "decline" }
                        ? "declined" : "has not supplied a valid acceptance") +
                    (response is not null ? " · " + clock(response.Tick) : party.NoticeAwareAdultIds.Contains(adult, StringComparer.Ordinal)
                        ? " · learned the published terms" : " · has not learned the published terms"));
            }
        }
        if (transfer.SettledTick is { } settled) lines.Add("Closed " + clock(settled) + " · " + Status(transfer.Status));
        if (transfer.Reason is { } reason) lines.Add(Reason(reason));
        lines.Add("Every current adult in every source and receiving household must accept. No routine mayor or Council approval is needed.");
        lines.Add("Use permission and any agreed goods payment move; Town title, membership, buildings, crops, other goods and private-building access stay unchanged.");
        return lines.Select(GameUiText.PlainEllipses).ToArray();
    }

    private static string Status(string status) => status switch
    {
        "pending" => "awaiting household acceptance",
        "transferred" => "completed",
        "rejected" => "declined",
        "withdrawn" => "withdrawn",
        _ => "terms no longer eligible",
    };
    private static string Roster(string kind) => kind switch { "current" => "current", "settlement" => "settlement", _ => "published" };
    private static string Reason(string reason) => reason switch
    {
        "adult_declined" => "An affected adult declined; permission did not move.",
        "filer_withdrew" => "The proposer withdrew the transfer; permission did not move.",
        "rights_changed" => "The recorded source permission changed; fresh terms and acceptance are required.",
        "permission_expired" => "A source permission reached its agreed end; it requires expiry review.",
        "plot_disputed" => "The plot has competing claims; the voluntary transfer cannot settle them.",
        "hearing_open" => "The plot is under review; a voluntary transfer cannot bypass the hearing.",
        _ => "The published terms no longer allow this transfer.",
    };
    private static string Tiles(IReadOnlyList<OwnerWorldPosition> tiles) => string.Join(", ", tiles.Select(tile => $"({tile.X}, {tile.Y})"));
}
