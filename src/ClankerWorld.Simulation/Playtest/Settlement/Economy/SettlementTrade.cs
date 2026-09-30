using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string SettlementTradePrefix = "settlement-trade:";

    private bool HasTradeResponse(string actor) => !NeedsUrgentWarmth(inhabitants[actor]) && society.Checkpoint.Inventory.Offers.Any(offer =>
        offer.Id.StartsWith(SettlementTradePrefix, StringComparison.Ordinal) &&
        offer.State == DirectBarterState.Open && offer.ExpiryTick >= WorldTick &&
        (offer.FirstPartyId == actor || offer.SecondPartyId == actor) &&
        !offer.AcceptedBy.Contains(actor, StringComparer.Ordinal));

    private bool WantsTradeItem(string actor, InventoryLot item)
    {
        var state = inhabitants[actor];
        var kind = item.ItemKind;
        if (kind is "field_map" or "field_record")
        {
            // An agent can offer a record they physically hold; a prospective
            // recipient wants it only if it contains a fact they have not learned.
            if (item.OwnerId == actor ||
                knowledge.Facts.Count(fact => fact.OwnerId == actor) >= AgentKnowledgeRules.MaximumFactsPerAgent)
                return false;
            var artifact = knowledge.Artifacts.FirstOrDefault(candidate => candidate.LotId == item.Id);
            return artifact?.Facts.Any(fact => !KnowsMapFact(actor, fact.Position)) == true;
        }

        var owned = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor && lot.ItemKind == kind)
            .Sum(AvailableLotQuantity);
        if (kind == "food")
        {
            // Keeping a small trade reserve is different from taking a food errand.
            return owned < 2 && state.HungerBasisPoints < 8_500;
        }
        if (kind is "tool" or "clothing")
        {
            return owned == 0;
        }
        if (state.Project is not { Stage: not ("completed" or "cancelled") } project)
        {
            return false;
        }
        if (!TownConstructionCandidateIds.TryParse(project.CandidateId, out var selection))
            return false;
        var inputs = selection.IsBuilding
            ? worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId)?.BuildCosts
            : worldContent.Recipes.FirstOrDefault(item => item.CanonicalId == selection.DefinitionId)?.Inputs;
        return inputs?.Any(input => input.ResourceId == kind && !HasAvailableQuantities([input]) && owned < input.Amount) == true;
    }

    private (InventoryLot Give, InventoryLot Take)? TradeOpportunity(string actor, string other)
    {
        if (actor == other || !AdultResident(actor) || !AdultResident(other) ||
            NeedsUrgentWarmth(inhabitants[actor]) || NeedsUrgentWarmth(inhabitants[other]) ||
            society.Checkpoint.Inventory.Offers.Any(offer =>
                offer.State == DirectBarterState.Open &&
                (offer.FirstPartyId == actor || offer.SecondPartyId == actor || offer.FirstPartyId == other || offer.SecondPartyId == other) ||
                offer.Id.StartsWith(SettlementTradePrefix, StringComparison.Ordinal) && offer.ExpiryTick + 120 >= WorldTick &&
                (offer.FirstPartyId == actor && offer.SecondPartyId == other || offer.FirstPartyId == other && offer.SecondPartyId == actor)))
        {
            return null;
        }
        var lots = society.Checkpoint.Inventory.Lots;
        foreach (var give in lots.Where(lot => lot.OwnerId == actor && TradeQuantityAvailable(lot) &&
                     !WantsTradeItem(actor, lot) && WantsTradeItem(other, lot) &&
                     (lot.ItemKind != "food" || inhabitants[actor].HungerBasisPoints >= 6_500)))
        {
            var take = lots.FirstOrDefault(lot => lot.OwnerId == other && lot.ItemKind != give.ItemKind &&
                TradeQuantityAvailable(lot) && !WantsTradeItem(other, lot) && WantsTradeItem(actor, lot) &&
                (lot.ItemKind != "food" || inhabitants[other].HungerBasisPoints >= 6_500));
            if (take is not null)
            {
                return (give, take);
            }
        }
        return null;
    }

    private void AddTradeCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (NeedsUrgentWarmth(inhabitants[actor]))
        {
            return;
        }
        foreach (var offer in society.Checkpoint.Inventory.Offers.Where(offer => offer.State == DirectBarterState.Open &&
                     offer.Id.StartsWith(SettlementTradePrefix, StringComparison.Ordinal) &&
                     offer.ExpiryTick >= WorldTick && (offer.FirstPartyId == actor || offer.SecondPartyId == actor)))
        {
            if (offer.AcceptedBy.Contains(actor, StringComparer.Ordinal))
            {
                candidates.Add(new("trade_decline:" + offer.Id, "Withdraw the pending exchange and release both reserved items.", 110));
                continue;
            }
            var giveId = offer.FirstPartyId == actor ? offer.FirstLotId : offer.SecondLotId;
            var takeId = offer.FirstPartyId == actor ? offer.SecondLotId : offer.FirstLotId;
            var give = society.Checkpoint.Inventory.GetLot(giveId);
            var take = society.Checkpoint.Inventory.GetLot(takeId);
            var useful = WantsTradeItem(actor, take) && (give.ItemKind != "food" || inhabitants[actor].HungerBasisPoints >= 6_500);
            candidates.Add(new("trade_accept:" + offer.Id, $"Accept exchange: give one {give.ItemKind}, receive one {take.ItemKind}.", useful ? 12 : 60));
            candidates.Add(new("trade_decline:" + offer.Id, "Decline this exchange and release both reserved items.", useful ? 60 : 12));
        }
        foreach (var other in inhabitants.Keys.Where(other => other != actor)
                     .OrderByDescending(other => TrustScore(actor, other)).ThenBy(other => other, StringComparer.Ordinal))
        {
            if (TradeOpportunity(actor, other) is { } trade)
            {
                candidates.Add(new("trade_propose:" + other,
                    $"Offer {society.Checkpoint.GetInhabitant(other).Name} one {trade.Give.ItemKind} for one {trade.Take.ItemKind}; they may refuse.", 14));
            }
        }
    }

    private void ApplyTradeCandidate(string actor, PlaytestInhabitantState state, string candidate)
    {
        if (candidate.StartsWith("trade_propose:", StringComparison.Ordinal))
        {
            var other = candidate[14..];
            if (!inhabitants.ContainsKey(other) || TradeOpportunity(actor, other) is not { } trade)
            {
                return;
            }
            var id = $"{SettlementTradePrefix}{WorldTick}:{actor}:{other}";
            society.Apply(checkpoint => SocietyFixture.CreateBarterOffer(checkpoint,
                new DirectBarterProposal(id, 1, actor, other, trade.Give.Id, 1, trade.Take.Id, 1, WorldTick + 120)));
            society.Apply(checkpoint => SocietyFixture.AcceptBarterOffer(checkpoint, id, 1, actor));
            AppendEvent("settlement_trade_offered", actor + ":" + other);
            return;
        }
        var accept = candidate.StartsWith("trade_accept:", StringComparison.Ordinal);
        var offerId = candidate[(accept ? 13 : 14)..];
        var offer = society.Checkpoint.Inventory.Offers.FirstOrDefault(item => item.Id == offerId && item.State == DirectBarterState.Open);
        if (offer is null || offer.ExpiryTick < WorldTick ||
            (offer.FirstPartyId != actor && offer.SecondPartyId != actor) || accept && offer.AcceptedBy.Contains(actor, StringComparer.Ordinal))
        {
            return;
        }
        if (!accept)
        {
            ApplyInventoryTransition(inventory => InventoryFixture.CancelDirectBarterOffer(inventory, offerId, offer.Revision, actor));
            AppendEvent("settlement_trade_declined", actor);
            return;
        }
        var camp = SettlementStoragePosition;
        if (!IsWithinInteractionRange(state.Position, camp, ResourceInteractionRange))
        {
            MoveToward(actor, state, camp, "trade", ResourceInteractionRange);
            return;
        }
        society.Apply(checkpoint => SocietyFixture.AcceptBarterOffer(checkpoint, offerId, offer.Revision, actor));
        if (society.Checkpoint.Inventory.GetOffer(offerId).State == DirectBarterState.Settled)
        {
            ReadTradedKnowledge(offer.FirstPartyId, offer.SecondPartyId, offer.FirstLotId, offer.SecondLotId);
            foreach (var (owner, subject) in new[] { (offer.FirstPartyId, offer.SecondPartyId), (offer.SecondPartyId, offer.FirstPartyId) })
            {
                IncreaseTrust(owner, subject, 1, "barter_completed");
                var memoryId = "settlement-trust:" + owner + ":" + subject;
                if (!society.Checkpoint.Memories.Any(memory => memory.Id == memoryId))
                {
                    society.Apply(checkpoint => SocietyFixture.RecordSocialMemory(checkpoint, new(memoryId, owner, subject,
                        $"Completed a mutually accepted exchange with {checkpoint.GetInhabitant(subject).Name}.", "public", WorldTick)));
                }
            }
            AppendEvent("settlement_trade_completed", actor);
        }
    }

    private bool TradeQuantityAvailable(InventoryLot lot) =>
        AvailableLotQuantity(lot) >= (lot.ItemKind is "field_map" or "field_record" ? 1 : 2);

    private void MaintainSettlementTrades()
    {
        foreach (var offer in society.Checkpoint.Inventory.Offers.Where(offer => offer.State == DirectBarterState.Open &&
                     offer.Id.StartsWith(SettlementTradePrefix, StringComparison.Ordinal)).ToArray())
        {
            var currentInventory = society.Checkpoint.Inventory;
            var parties = new[]
            {
                (Owner: offer.FirstPartyId, Lot: offer.FirstLotId, Quantity: offer.FirstQuantity, Reservation: offer.Id + ":first"),
                (Owner: offer.SecondPartyId, Lot: offer.SecondLotId, Quantity: offer.SecondQuantity, Reservation: offer.Id + ":second"),
            };
            if (parties.Any(party => !society.Checkpoint.Inhabitants.Any(person => person.Id == party.Owner && person.Status == SocietyInhabitantStatus.Active) ||
                    !currentInventory.Lots.Any(lot => lot.Id == party.Lot && lot.OwnerId == party.Owner && lot.Quantity >= party.Quantity &&
                        lot.FreshnessBasisPoints > 0 && lot.ConditionBasisPoints > 0) ||
                    !currentInventory.Reservations.Any(reservation => reservation.Id == party.Reservation && reservation.State == InventoryReservationState.Reserved)))
            {
                ApplyInventoryTransition(inventory => InventoryFixture.CancelDirectBarterOffer(inventory, offer.Id, offer.Revision, offer.FirstPartyId));
                AppendEvent("settlement_trade_cancelled", offer.FirstPartyId);
            }
        }
        var stale = society.Checkpoint.Inventory.Offers.Where(offer => offer.Id.StartsWith(SettlementTradePrefix, StringComparison.Ordinal) &&
                offer.State != DirectBarterState.Open && offer.ExpiryTick + 120 < WorldTick)
            .OrderByDescending(offer => offer.ExpiryTick).Skip(16).Select(offer => offer.Id).ToHashSet(StringComparer.Ordinal);
        if (stale.Count > 0)
        {
            ApplyInventoryTransition(inventory => inventory with
            {
                Offers = inventory.Offers.Where(offer => !stale.Contains(offer.Id)).ToArray(),
                Reservations = inventory.Reservations.Where(reservation => !stale.Any(id => reservation.Id == id + ":first" || reservation.Id == id + ":second")).ToArray(),
            });
        }
    }
}
