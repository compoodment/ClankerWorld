using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void AddCivicAndBusinessDetails(OwnerWorldSnapshot snapshot, List<TownLine> lines)
    {
        foreach (var council in snapshot.TownCouncils)
        {
            lines.Add(new(TownStyle.Heading, GameUiText.PartyName(snapshot, council.TownId) + " council"));
            lines.Add(new(TownStyle.Detail, council.GoverningForm == "representative" ? "Representative council · two yes votes are required" :
                council.FallbackReason == "candidate" ? "All adults govern while a complete willing council is sought" : "All adult residents govern"));
            lines.Add(new(TownStyle.Body, council.MemberNames.Count == 0 ? "No councillors seated; representative seats may be vacant." : string.Join(", ", council.MemberNames)));
            lines.Add(new(TownStyle.Body, council.FoodPolicy == "essential_first" ? "Communal food: hungry residents first" : "Communal food: open access"));
            if (council.HallId is null) lines.Add(new(TownStyle.Note, "Build a Town Hall so residents can meet and vote."));
            if (council.TermExpiryTick is { } expiry) lines.Add(new(TownStyle.Detail, "Representative term ends " + DisplayWorldClock(expiry)));
            foreach (var candidate in council.CandidateRegister)
                lines.Add(new(TownStyle.Detail, candidate.Name + " is willing: " +
                    string.Join(" and ", new[] { candidate.FullTermWilling ? "full term" : null, candidate.ReplacementWilling ? "remaining term" : null }.Where(text => text is not null))));
            if (council.ElectionRetryAfterTick is { } retry)
                lines.Add(new(TownStyle.Note, "Next election retry after " + DisplayWorldClock(retry) + "; improved circumstances may allow it sooner."));
            if (council.LastDrawMemberNames.Count > 0)
                lines.Add(new(TownStyle.Detail, "Recorded tie draw selected: " + string.Join(", ", council.LastDrawMemberNames)));
            if (council.ElectionExpiryTick is { } electionExpiry)
            {
                lines.Add(new(TownStyle.Body, GameUiText.TownElectionSummary(council) + " · closes " + DisplayWorldClock(electionExpiry)));
                lines.Add(new(TownStyle.Detail, council.ElectionCandidateNames.Count == 0 ? "No willing candidates yet." : "Willing candidates: " + string.Join(", ", council.ElectionCandidateNames)));
                if (council.ElectionSelectedMemberNames.Count > 0)
                    lines.Add(new(TownStyle.Detail, "Already selected: " + string.Join(", ", council.ElectionSelectedMemberNames)));
            }
            if (council.ProposedRule is { } rule)
                lines.Add(new(TownStyle.Body, $"Proposed: {rule} · {council.Approvals} yes / {council.Rejections} no / {council.Voters} eligible voters"));
            foreach (var law in council.Laws) lines.Add(new(TownStyle.Detail, law.Text));
        }
        if (snapshot.BusinessTrade is not { } business) return;
        string Person(string id) => GameUiText.PartyName(snapshot, id);
        string Site(string id) => snapshot.PlacedBuildings.FirstOrDefault(building => building.InstanceId == id)?.DisplayName ?? "Business";
        lines.Add(new(TownStyle.Heading, "Goods offered for barter"));
        if (business.Listings.Count == 0) lines.Add(new(TownStyle.Note, "No business goods are offered yet."));
        foreach (var listing in business.Listings.Take(16))
        {
            lines.Add(new(TownStyle.Body, $"{Site(listing.BuildingId)} · {Person(listing.SellerId)}: " +
                $"{Pretty(listing.GoodsKind)} × {listing.GoodsQuantity} for {Pretty(listing.PaymentKind)} × {listing.PaymentQuantity}"));
            if (listing.Contents.Count > 0) lines.Add(new(TownStyle.Detail, "Contains " + string.Join(", ", listing.Contents.Select(item =>
                $"{Pretty(item.Kind)} × {item.Quantity}"))));
        }
        if (business.Listings.Count > 16) lines.Add(new(TownStyle.Note, $"{business.Listings.Count - 16} more offers are available."));
        foreach (var offer in business.Offers.Where(offer => offer.State == "open").Take(12))
        {
            lines.Add(new(TownStyle.Body, $"{Person(offer.BuyerId)} and {Person(offer.SellerId)}: " +
                $"meet at {Site(offer.BuildingId)} for {Pretty(offer.GoodsKind)} × {offer.GoodsQuantity} ↔ {Pretty(offer.PaymentKind)} × {offer.PaymentQuantity}"));
            if (offer.Blocker is { } blocker) lines.Add(new(TownStyle.Warning, blocker));
        }
        if (business.Stalls.Count > 0) lines.Add(new(TownStyle.Heading, "Market stalls"));
        foreach (var stall in business.Stalls)
            lines.Add(new(TownStyle.Body, $"{Person(stall.HouseholdId)} holds a stall · {stall.StoredQuantity} goods and receipts still there"));
        foreach (var order in business.ToolOrders.Where(order => order.State is "queued" or "running" or "ready").Take(12))
        {
            lines.Add(new(TownStyle.Body, $"{Person(order.BuyerId)} requested {Pretty(order.ToolKind)} at {Site(order.BuildingId)} · {Pretty(order.State)}"));
            if (order.Blocker is { } blocker) lines.Add(new(TownStyle.Warning, blocker));
        }
    }
}
