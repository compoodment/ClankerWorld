using ClankerWorld.Simulation.Harness;
using System.Text.Json.Serialization;

namespace ClankerWorld.Simulation.Playtest;

public sealed record TownLandTransferParty(string HouseholdId, string Kind, IReadOnlyList<string> AdultIds);

/// <summary>A separate actual adult response for one household, with its contemporaneous adult roster.</summary>
public sealed record TownLandTransferResponse(string HouseholdId, string AgentId, string Kind, long Tick,
    IReadOnlyList<string> PartyAdults);

public sealed record TownLandSalePrice(string SellerHouseholdId, string ItemKind, int Quantity);

public sealed record TownLandSalePaymentLot(string SourceLotId, int Quantity, long TransferEventId);

public sealed record TownLandSalePayment(string BuyerAgentId, string SellerAgentId, GridPoint Position,
    IReadOnlyList<TownLandSalePaymentLot> Lots);

public sealed record TownLandTransferReceipt(string AdjustmentId, long Tick, IReadOnlyList<TownLandTransferParty> Parties)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TownLandSalePayment? Payment { get; init; }
}

/// <summary>Published immutable permission terms; proposing them supplies no household consent.</summary>
public sealed record TownLandTransferRequest(string Id, string TownId, string FilerId, string TargetHouseholdId,
    IReadOnlyList<GridPoint> Tiles, IReadOnlyList<TownLandRightVersion> RightVersions,
    IReadOnlyList<TownLandTransferParty> Parties, string NoticeId, long ProposedTick,
    IReadOnlyList<TownLandTransferResponse> Responses, string Status = "pending", long? SettledTick = null,
    string? Reason = null, TownLandTransferReceipt? Receipt = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TownLandSalePrice? Price { get; init; }
}
