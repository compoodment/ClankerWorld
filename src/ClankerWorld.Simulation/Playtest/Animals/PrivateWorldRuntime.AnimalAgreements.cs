using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private sealed record AnimalAgreement(string Action, string AnimalId, string OtherId, string? LotId = null)
    {
        public string Id => "animal:agreement:" + AnimalKey(Action + ":" + AnimalId + ":" + OtherId + ":" + LotId);
    }

    private IEnumerable<AnimalAgreement> AnimalAgreements(string actor)
    {
        if (!AdultResident(actor)) yield break;
        foreach (var animal in animalWorld.Animals.Where(animal => AnimalHouseholdMember(actor, animal)))
        {
            foreach (var recipient in inhabitants.Values.Where(person => person.InhabitantId != actor && AdultResident(person.InhabitantId) &&
                         HouseholdFor(person.InhabitantId) != animal.HouseholdId &&
                         IsWithinInteractionRange(inhabitants[actor].Position, person.Position, ResourceInteractionRange))
                         .OrderBy(person => person.InhabitantId, StringComparer.Ordinal).Take(8))
            {
                yield return new(animal.CarePermissions.Contains(recipient.InhabitantId) ? "revoke_care" : "grant_care", animal.Id, recipient.InhabitantId);
                if (animal.Species == "horse") yield return new(animal.RidingPermissions.Contains(recipient.InhabitantId) ? "revoke_ride" : "grant_ride", animal.Id, recipient.InhabitantId);
                if (animal.RiderId is not null || animal.LeaderId is not null || animal.ReadyProductLotId is not null ||
                    animalWorld.Offers.Any(offer => offer.AnimalId == animal.Id) ||
                    HouseholdFor(recipient.InhabitantId) is not { } home || AnimalYard(home) is not { } yard ||
                    !HasAnimalTransferSpace(home, yard, animal) ||
                    !IsWithinInteractionRange(inhabitants[actor].Position, animal.Position, ResourceInteractionRange) ||
                    !IsWithinInteractionRange(recipient.Position, animal.Position, ResourceInteractionRange)) continue;
                yield return new("offer_gift", animal.Id, recipient.InhabitantId);
                foreach (var lot in society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == recipient.InhabitantId &&
                             PersonalEquipmentRules.IsCarried(lot, recipient.InhabitantId) && lot.ContainerLotId is null &&
                             lot.DeliveryBuildingId is null && !InventoryContainerRules.IsContainer(lot.ItemKind) && AvailableLotQuantity(lot) > 0 &&
                             !PersonalEquipmentRules.IsSelected(recipient.Equipment, lot.Id)).OrderBy(lot => lot.Id, StringComparer.Ordinal).Take(4))
                    yield return new("offer_sale", animal.Id, recipient.InhabitantId, lot.Id);
            }
            // Revocation stays available after the permitted adult moves away.
            foreach (var permission in animal.CarePermissions) yield return new("revoke_care", animal.Id, permission);
            foreach (var permission in animal.RidingPermissions) yield return new("revoke_ride", animal.Id, permission);
        }
        foreach (var offer in animalWorld.Offers.Where(offer => offer.BuyerId == actor && ValidAnimalOffer(offer)))
        {
            yield return new("accept", offer.AnimalId, offer.Id);
            yield return new("decline", offer.AnimalId, offer.Id);
        }
    }

    private bool HasAnimalTransferSpace(string household, PlacedBuilding yard, AnimalState animal, string? ignoringOffer = null)
    {
        var reserved = ignoringOffer is null ? 0 : 1 + (animal.Pregnancy is null ? 0 : 1);
        var required = 1 + (animal.Pregnancy is null ? 0 : 1);
        return AnimalPlacesUsed(household) - reserved + required <= AnimalRules.PopulationCap &&
            AnimalPlacesUsed(household, yard.InstanceId) - reserved + required <= AnimalYardCapacity(yard);
    }

    private bool ValidAnimalOffer(AnimalTradeOffer offer)
    {
        var animal = Animal(offer.AnimalId);
        var yard = worldSimulation.Buildings.FirstOrDefault(building => building.InstanceId == offer.ReceivingYardId &&
            building.HouseholdId == offer.ReceivingHouseholdId && worldContent.Buildings.Any(definition =>
                definition.CanonicalId == building.DefinitionId && definition.Tags.Contains(AnimalContent.YardTag)));
        if (animal is null || animal.DiedTick is not null || animal.RiderId is not null || animal.LeaderId is not null ||
            animal.ReadyProductLotId is not null || yard is null || !AnimalHouseholdMember(offer.SellerId, animal) ||
            !AdultResident(offer.BuyerId) || HouseholdFor(offer.BuyerId) != offer.ReceivingHouseholdId ||
            animal.HouseholdId == offer.ReceivingHouseholdId || WorldTick - offer.OfferedTick >= AnimalDayTicks ||
            !HasAnimalTransferSpace(offer.ReceivingHouseholdId, yard, animal, offer.Id) ||
            !IsWithinInteractionRange(inhabitants[offer.SellerId].Position, animal.Position, ResourceInteractionRange) ||
            !IsWithinInteractionRange(inhabitants[offer.BuyerId].Position, animal.Position, ResourceInteractionRange)) return false;
        return offer.PaymentLotId is null || society.Checkpoint.Inventory.Lots.Any(lot => lot.Id == offer.PaymentLotId &&
            lot.OwnerId == offer.BuyerId && lot.ItemKind == offer.PaymentKind && PersonalEquipmentRules.IsCarried(lot, offer.BuyerId) &&
            lot.ContainerLotId is null && lot.DeliveryBuildingId is null && AvailableLotQuantity(lot) >= offer.PaymentQuantity &&
            !PersonalEquipmentRules.IsSelected(inhabitants[offer.BuyerId].Equipment, lot.Id));
    }

    private void AddAnimalPermissionAndTradeCandidates(List<CognitionCandidate> candidates, string actor)
    {
        foreach (var agreement in AnimalAgreements(actor).DistinctBy(item => item.Id).Take(48))
        {
            var animal = Animal(agreement.AnimalId)!;
            var name = society.Checkpoint.Inhabitants.FirstOrDefault(person => person.Id == agreement.OtherId)?.Name ?? agreement.OtherId;
            var offer = animalWorld.Offers.FirstOrDefault(offer => offer.Id == agreement.OtherId);
            var description = agreement.Action switch
            {
                "grant_care" => $"Allow {name} to care for {animal.Name} using its animal-yard supplies and collect its household products.",
                "grant_ride" => $"Allow {name} to ride {animal.Name}; this grants no ownership or stock access.",
                "revoke_care" => $"Revoke {name}'s care permission for {animal.Name}.",
                "revoke_ride" => $"Revoke {name}'s riding permission for {animal.Name}.",
                "offer_gift" => $"Offer the specific animal {animal.Name} as a gift to {name}'s household; they must accept.",
                "offer_sale" => $"Offer {animal.Name} to {name}'s household for one {society.Checkpoint.Inventory.GetLot(agreement.LotId!).ItemKind.Replace('_', ' ')} from their specific carried lot; they must accept.",
                "accept" => $"Accept {animal.Name} for your household and lead it home, " +
                    (offer!.PaymentKind is null ? "as an offered gift." : $"paying exactly {offer.PaymentQuantity} {offer.PaymentKind.Replace('_', ' ')} from the named carried lot."),
                _ => $"Decline the offer for {animal.Name}.",
            };
            candidates.Add(new(agreement.Id, description, 190, animal.Id));
        }
    }

    private bool ApplyAnimalConsentDecision(SocietyCognitionDispatchResult decision)
    {
        var selected = decision.Admission.Intention;
        if (selected is null || !selected.CandidateId.StartsWith("animal:agreement:", StringComparison.Ordinal)) return false;
        if (!decision.Admission.Accepted || decision.Admission.FellBack || selected.Provider != DecisionProviderKind.LargeLanguageModel ||
            selected.InhabitantId != decision.InhabitantId || PendingInstructionFor(decision.InhabitantId)?.Kind == OwnerInstructionKind.MustDo) return true;
        var actor = decision.InhabitantId;
        var agreement = AnimalAgreements(actor).FirstOrDefault(agreement => agreement.Id == selected.CandidateId);
        if (agreement is null) return true;
        var animal = Animal(agreement.AnimalId)!;
        if (agreement.Action.StartsWith("grant_", StringComparison.Ordinal) || agreement.Action.StartsWith("revoke_", StringComparison.Ordinal))
        {
            var riding = agreement.Action.EndsWith("ride", StringComparison.Ordinal);
            var grant = agreement.Action.StartsWith("grant_", StringComparison.Ordinal);
            var permissions = (riding ? animal.RidingPermissions : animal.CarePermissions).Where(id => id != agreement.OtherId)
                .Concat(grant ? [agreement.OtherId] : Array.Empty<string>()).Order(StringComparer.Ordinal).ToArray();
            SetAnimal(riding ? animal with { RidingPermissions = permissions } : animal with { CarePermissions = permissions });
            if (riding && !grant && animal.RiderId == agreement.OtherId) EndAnimalRide(Animal(animal.Id)!, "permission_revoked");
            AppendEvent("animal_permission_changed", actor + ":" + animal.Id + ":" + agreement.Action + ":" + agreement.OtherId);
        }
        else if (agreement.Action is "offer_sale" or "offer_gift")
        {
            var home = HouseholdFor(agreement.OtherId)!;
            var payment = agreement.LotId is null ? null : society.Checkpoint.Inventory.GetLot(agreement.LotId);
            var offer = new AnimalTradeOffer("animal-offer-" + AnimalKey(actor + ":" + animal.Id + ":" + WorldTick + ":" + nextEventId),
                animal.Id, actor, agreement.OtherId, home, AnimalYard(home)!.InstanceId, payment?.ItemKind, payment is null ? 0 : 1, WorldTick)
            { PaymentLotId = payment?.Id };
            animalWorld = animalWorld with { Offers = animalWorld.Offers.Append(offer).OrderBy(offer => offer.Id, StringComparer.Ordinal).ToArray() };
            AppendEvent("animal_trade_offered", offer.Id + ":" + animal.Id + ":" + (payment?.Id ?? "gift"));
        }
        else
        {
            var offer = animalWorld.Offers.Single(offer => offer.Id == agreement.OtherId);
            if (agreement.Action == "accept")
            {
                if (offer.PaymentLotId is { } payment)
                    ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, offer.Id + "-payment", actor,
                        animal.HouseholdId!, payment, offer.PaymentQuantity, "animal_sale_payment",
                        destinationGroundPosition: new(animal.Position.X, animal.Position.Y)));
                if (animal.SaddleReservationId is { } saddleHeld)
                    ApplyInventoryTransition(inventory => InventoryFixture.ReleaseReservation(inventory, saddleHeld, "animal_transferred"));
                SetAnimal(animal with { HouseholdId = offer.ReceivingHouseholdId, YardId = offer.ReceivingYardId,
                    HerdId = "household:" + offer.ReceivingHouseholdId, CarePermissions = [], RidingPermissions = [],
                    SaddleLotId = null, SaddleReservationId = null, TamingWork = null,
                    LeaderId = inhabitants[actor].Position == animal.Position ? actor : null,
                    LeadDestination = inhabitants[actor].Position == animal.Position ? AnimalYard(offer.ReceivingHouseholdId)!.Position : null });
                AppendEvent("animal_transferred", offer.Id + ":" + animal.Id + ":" + offer.ReceivingHouseholdId, animal.Position);
            }
            animalWorld = animalWorld with { Offers = animalWorld.Offers.Where(item => item.Id != offer.Id).ToArray() };
        }
        return true;
    }
}
