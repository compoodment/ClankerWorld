using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private TownPropertySnapshot CaptureProperty(TownRuntimeState town, TownPropertyRequest request, int revision)
    {
        if (!TownPropertyRules.IsValid(request)) throw new InvalidOperationException("The property request is malformed.");
        var building = worldSimulation.Buildings.SingleOrDefault(building => building.InstanceId == request.BuildingId);
        if (building is null || building.TownId != town.Id || building.HouseholdId != request.SourceHouseholdId)
            throw new InvalidOperationException("The building no longer belongs to the noticed owner.");
        var definition = worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId);
        if (!definition.Tags.Any(IsHouseholdBuildingTag))
            throw new InvalidOperationException("This request needs an existing household building.");
        var plot = WorldContentSimulationRules.Footprint(definition, building).ToArray();
        if (plot.Length > 64 || plot.Any(tile => !TownLandRightsRules.IsCoveredByTownTitle(tile, town.Id, townLandTitles)) ||
            householdLandUseRights.Any(right => right.TownId == town.Id && right.HouseholdId != request.SourceHouseholdId && right.Tiles.Any(plot.Contains)))
            throw new InvalidOperationException("The building needs an undisputed footprint under this Town's title.");
        var former = Array.Empty<string>();
        var recipients = Array.Empty<string>();
        if (request.SourceHouseholdId is { } source)
        {
            var household = society.Checkpoint.Households.SingleOrDefault(household => household.Id == source);
            if (household is null || household.MemberIds.Any(id => society.Checkpoint.GetInhabitant(id).Status == SocietyInhabitantStatus.Active))
                throw new InvalidOperationException("The household still has members; its property cannot be reclaimed.");
            former = TownPropertyRules.FormerMembers(society.Checkpoint, source, WorldTick);
        }
        else
        {
            if (!town.LandHearings.Cases.Any(item => item.Property?.Transfer is { ResultBuilding.HouseholdId: null } transfer &&
                    transfer.ResultBuilding.InstanceId == building.InstanceId && transfer.ResultBuilding.TownId == town.Id))
                throw new InvalidOperationException("Only property previously recovered by a Town ruling can be granted this way.");
            var target = society.Checkpoint.Households.SingleOrDefault(household => household.Id == request.TargetHouseholdId);
            recipients = target is null ? [] : HouseholdAdults(target.Id);
            if (target is null || recipients.Length == 0 || worldSimulation.Buildings.Any(other =>
                    other.HouseholdId == target.Id && other.DefinitionId == building.DefinitionId))
                throw new InvalidOperationException("The receiving household needs current adults and room for this building.");
        }
        var owner = request.SourceHouseholdId ?? town.Id;
        var inventory = society.Checkpoint.Inventory;
        var roots = inventory.Lots.Where(lot => lot.OwnerId == owner && lot.Quantity > 0 && lot.CarrierId is null && lot.ContainerLotId is null &&
                (lot.StorageBuildingId == building.InstanceId || lot.GroundPosition is { } ground && plot.Contains(new(ground.X, ground.Y))))
            .Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        var lots = inventory.Lots.Where(lot => lot.Quantity > 0 && (roots.Contains(lot.Id) || lot.ContainerLotId is { } container && roots.Contains(container)))
            .OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
        if (lots.Any(lot => lot.OwnerId != owner || lot.CarrierId is not null))
            throw new InvalidOperationException("Shared containers must retain one recorded owner and physical custody.");
        return new(revision, WorldTick, building, lots, former, recipients);
    }

    private bool PropertyIsCurrent(TownRuntimeState town, TownLandCase item)
    {
        if (item.Property is not { Transfer: null } property) return false;
        try
        {
            return TownPropertyRules.MaterialVersion(property.Snapshots[^1]) == TownPropertyRules.MaterialVersion(
                CaptureProperty(town, property.Request, TownLandHearingRules.CurrentRevision(item).Number));
        }
        catch (InvalidOperationException) { return false; }
    }

    private bool PropertyCanTransfer(TownRuntimeState town, TownLandCase item)
    {
        if (!PropertyIsCurrent(town, item) || !TownPropertyRules.HasAllConsent(item, WorldTick)) return false;
        var building = worldSimulation.Buildings.Single(building => building.InstanceId == item.Property!.Request.BuildingId);
        if (BuildingMutationBlocker(building, allowStoredGoods: true) is not null) return false;
        var inventory = society.Checkpoint.Inventory;
        var lotIds = item.Property!.Snapshots[^1].SharedLots.Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        return !inventory.Reservations.Any(reservation => lotIds.Contains(reservation.LotId) &&
            reservation.State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed or InventoryReservationState.Committed);
    }

    private void AddTownPropertyCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        if (!NearCivicBoard(actor, town) || !TownAdults(town).Contains(actor, StringComparer.Ordinal) ||
            town.Government is not { } government || town.Governance is not { } council) return;
        var mayor = government.Arrangement.Ordinary == TownArrangementRules.Mayor && government.Offices.Any(office =>
            office.Mandates == "ordinary" && office.HolderId == actor && office.TermEndTick > WorldTick);
        var councillor = government.Arrangement.Ordinary is TownArrangementRules.Council or TownArrangementRules.ElectedCouncil or
            TownArrangementRules.AllAdultCouncil && council.Members.Contains(actor, StringComparer.Ordinal);
        if (!mayor && !councillor) return;
        var empty = society.Checkpoint.Households.Where(household => !household.MemberIds.Any(id =>
            society.Checkpoint.GetInhabitant(id).Status == SocietyInhabitantStatus.Active)).Select(household => household.Id).ToHashSet(StringComparer.Ordinal);
        var recovered = town.LandHearings.Cases.Where(item => item.Property?.Transfer is { ResultBuilding.HouseholdId: null })
            .Select(item => item.Property!.Transfer!.ResultBuilding.InstanceId).ToHashSet(StringComparer.Ordinal);
        foreach (var building in worldSimulation.Buildings.Where(building => building.TownId == town.Id &&
                     (building.HouseholdId is { } source && empty.Contains(source) || building.HouseholdId is null && recovered.Contains(building.InstanceId)))
                     .OrderBy(building => building.InstanceId, StringComparer.Ordinal))
        {
            var targets = building.HouseholdId is not null ? new string?[] { null } :
                society.Checkpoint.Households.Where(household => household.MemberIds.Count > 0).Select(household => (string?)household.Id);
            foreach (var target in targets)
            {
                var request = new TownPropertyRequest(building.InstanceId, building.HouseholdId, target);
                TownPropertySnapshot snapshot;
                try { snapshot = CaptureProperty(town, request, 1); }
                catch (InvalidOperationException) { continue; }
                var plot = WorldContentSimulationRules.Footprint(worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building).ToArray();
                if (TownPropertyRules.FilingRefusal(town.LandHearings, plot, new(request, [snapshot], [])) is not null) continue;
                candidates.Add(new(CivicAction(town.Id, "hearing_property_request", building.InstanceId, target ?? "town"),
                    (mayor ? "Open" : "Ask the Council to authorize") + " a public property hearing to " +
                    (target is null ? "recover the empty household's " : "grant household " + target + " the recovered ") +
                    "building " + building.InstanceId + " and its " + snapshot.SharedLots.Count + " recorded shared goods lots. " +
                    "Give your statement in civic_land_hearing.statement. The normal notice and independent ruling are required. " +
                    "Living former members keep their property unless each personally agrees; personal goods and carried goods remain untouched.", 185));
            }
        }
    }

    private (TownGovernanceState Council, TownLandHearingState LandHearings) RequestPropertyCase(TownRuntimeState town,
        string actor, string subject, string choice, string statement, TownGovernanceState council, TownGovernmentState government)
    {
        var building = worldSimulation.Buildings.Single(building => CivicAgentToken(building.InstanceId) == subject);
        var request = new TownPropertyRequest(building.InstanceId, building.HouseholdId, choice == "town" ? null :
            society.Checkpoint.Households.Single(household => CivicAgentToken(household.Id) == choice).Id);
        var snapshot = CaptureProperty(town, request, 1);
        var tiles = TownLandRightsRules.OrderTiles(WorldContentSimulationRules.Footprint(
            worldContent.Buildings.Single(definition => definition.CanonicalId == building.DefinitionId), building));
        var property = new TownPropertyCase(request, [snapshot], []);
        if (TownPropertyRules.FilingRefusal(town.LandHearings, tiles, property) is { } refusal) throw new InvalidOperationException(refusal);
        var outcome = TownPropertyRules.Outcome(request);
        if (government.Arrangement.Ordinary == TownArrangementRules.Mayor)
        {
            var office = government.Offices.Single(office => office.Mandates == "ordinary" && office.HolderId == actor && office.TermEndTick > WorldTick);
            return OpenLandHearing(town, council, town.LandHearings, new(actor, "town", statement, outcome, WorldTick, office.ElectionId),
                tiles, townRepresentative: actor, propertyRequest: request);
        }
        var filing = new TownLandFilingRequest(tiles, outcome, statement) { Property = request };
        council = TownGovernanceRules.SubmitProposal(council, town.Id, actor, "land_hearing", null,
            "Authorize this Town property hearing.", "council:" + council.Revision, TownAdults(town), WorldTick, CivicDay,
            noticeText: TownLandGovernmentFilingRules.Describe(filing), landHearingRequest: filing);
        return (council, town.LandHearings);
    }

    private TownLandHearingState TransferCaseProperty(TownRuntimeState town, TownLandHearingState hearings, string caseId, TownPropertySnapshot prior)
    {
        var item = hearings.Cases.Single(item => item.Id == caseId);
        var property = item.Property!;
        var ruling = item.Rulings[^1];
        var recipient = property.Request.TargetHouseholdId ?? town.Id;
        var resultBuilding = prior.Building with { HouseholdId = property.Request.TargetHouseholdId };
        var inventory = society.Checkpoint.Inventory;
        foreach (var lot in prior.SharedLots.Where(lot => lot.ContainerLotId is null))
            inventory = InventoryFixture.Transfer(inventory, ruling.Id + ":" + lot.Id, lot.OwnerId, recipient, lot.Id,
                lot.Quantity, "noticed Town property ruling", destinationStorageBuildingId: lot.StorageBuildingId,
                destinationGroundPosition: lot.GroundPosition);
        ApplyInventoryTransition(_ => inventory);
        worldSimulation = worldSimulation with
        {
            Buildings = worldSimulation.Buildings.Select(building => building.InstanceId == prior.Building.InstanceId ? resultBuilding : building).ToArray(),
            GuestInvitations = RemoveHouseInvitations(prior.Building.InstanceId)
        };
        var lotIds = prior.SharedLots.Select(lot => lot.Id).ToHashSet(StringComparer.Ordinal);
        var transfer = new TownPropertyTransfer(ruling.Id, ruling.Revision, WorldTick, prior.Building, resultBuilding, prior.SharedLots,
            inventory.Lots.Where(lot => lotIds.Contains(lot.Id)).OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray());
        return TownPropertyRules.Replace(hearings, item with { Property = property with { Transfer = transfer } });
    }
}
