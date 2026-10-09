using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>One whole building and its shared goods, reclaimed by the Town or granted onward.</summary>
public sealed record TownPropertyRequest(string BuildingId, string? SourceHouseholdId, string? TargetHouseholdId);

/// <summary>Actual ownership and living claimants bound to a particular public notice.</summary>
public sealed record TownPropertySnapshot(int Revision, long Tick, PlacedBuilding Building,
    IReadOnlyList<InventoryLot> SharedLots, IReadOnlyList<string> LivingFormerMemberIds,
    IReadOnlyList<string> RecipientAdultIds);

public sealed record TownPropertyConsent(int Revision, string AgentId, bool Agreed, long Tick);

/// <summary>The physical transfer is committed with the ruling; its pre-transfer stock remains evidence.</summary>
public sealed record TownPropertyTransfer(string RulingId, int Revision, long Tick,
    PlacedBuilding PriorBuilding, PlacedBuilding ResultBuilding, IReadOnlyList<InventoryLot> PriorLots,
    IReadOnlyList<InventoryLot> ResultLots);

public sealed record TownPropertyCase(TownPropertyRequest Request, IReadOnlyList<TownPropertySnapshot> Snapshots,
    IReadOnlyList<TownPropertyConsent> Consents, TownPropertyTransfer? Transfer = null);
