using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidateAnimalState(PrivateWorldRuntimeState state)
    {
        var world = state.AnimalWorld;
        if (world is null || world.Animals is null || world.Offers is null || world.SupplyTrips is null || world.MilkOffers is null ||
            world.Animals.Any(animal => animal is null) || world.Offers.Any(offer => offer is null) || world.SupplyTrips.Any(trip => trip is null) || world.MilkOffers.Any(offer => offer is null))
            throw new InvalidDataException("The current checkpoint requires complete animal state.");
        if (!world.Seeded && (world.Animals.Count != 0 || world.Offers.Count != 0 || world.SupplyTrips.Count != 0 || world.MilkOffers.Count != 0))
            throw new InvalidDataException("An unseeded animal world cannot contain animal history or custody.");
        static bool Identifier(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512 && !value.Any(char.IsControl);
        static bool InventoryReference(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
        var society = state.Society.Society;
        var people = society.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        void UniqueAgents(IEnumerable<string> ids)
        {
            var values = ids.ToArray();
            if (values.Any(value => !InventoryReference(value) || !people.Contains(value)) ||
                values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new InvalidDataException("Animal agent references must name unique, known people.");
        }
        static void Unique(IEnumerable<string> ids)
        {
            var values = ids.ToArray();
            if (values.Any(value => !Identifier(value)) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new InvalidDataException("Animal identities and permissions must be unique, short identifiers.");
        }
        static void UniqueInventoryReferences(IEnumerable<string> ids)
        {
            var values = ids.ToArray();
            if (values.Any(value => !InventoryReference(value)) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new InvalidDataException("Animal inventory references must be unique, well-formed lot identities.");
        }
        Unique(world.Animals.Select(animal => animal.Id));
        Unique(world.Offers.Select(offer => offer.Id));
        UniqueAgents(world.SupplyTrips.Select(trip => trip.ActorId));
        Unique(world.MilkOffers.Select(offer => offer.Id));
        UniqueAgents(world.MilkOffers.Select(offer => offer.SellerId));
        UniqueAgents(world.MilkOffers.Select(offer => offer.BuyerId));
        UniqueInventoryReferences(world.MilkOffers.Select(offer => offer.MilkLotId));
        UniqueAgents(world.Animals.Select(animal => animal.RiderId).OfType<string>());
        UniqueAgents(world.Animals.Select(animal => animal.LeaderId).OfType<string>());
        var inventory = society.Inventory;
        var day = state.WorldSystems!.Config.TicksPerDay;
        var map = TravelMap(state);
        foreach (var offer in world.MilkOffers)
        {
            var milk = inventory.Lots.FirstOrDefault(lot => lot.Id == offer.MilkLotId);
            var seller = society.Inhabitants.FirstOrDefault(person => person.Id == offer.SellerId);
            var buyer = society.Inhabitants.FirstOrDefault(person => person.Id == offer.BuyerId);
            if (seller is null || buyer is null || seller.HouseholdId == buyer.HouseholdId || milk is null || milk.ItemKind != "milk" ||
                milk.ContainerLotId is null || !(milk.OwnerId == seller.Id || milk.OwnerId == seller.HouseholdId) || !InventoryReference(offer.ReceivingJugId) || !InventoryReference(offer.PaymentLotId) ||
                !Identifier(offer.BuildingId) || !map.IsPassable(offer.Position) || offer.OfferedTick < 0 || offer.OfferedTick > society.WorldTick ||
                !state.WorldSimulation!.Buildings.Any(building => building.InstanceId == offer.BuildingId && building.Position == offer.Position) ||
                !inventory.Lots.Any(jug => jug.Id == offer.ReceivingJugId && jug.ItemKind == InventoryContainerRules.WaterJug &&
                    (jug.OwnerId == buyer.Id || jug.OwnerId == buyer.HouseholdId) && PersonalEquipmentRules.IsCarried(jug, buyer.Id)) ||
                !inventory.Lots.Any(payment => payment.Id == offer.PaymentLotId && payment.OwnerId == buyer.Id && PersonalEquipmentRules.IsCarried(payment, buyer.Id)) ||
                !inventory.Reservations.Any(held => held.Id == offer.Id + "-milk" && held.LotId == milk.Id && held.OwnerId == milk.OwnerId &&
                    held.Quantity == 1 && held.IsExclusive && held.State == InventoryReservationState.Reserved && held.Purpose == "milk-sale:" + offer.Id))
                throw new InvalidDataException("A milk exchange requires its exact held product, parties, payment, receiving jug and physical shop or stall.");
        }
        foreach (var animal in world.Animals)
        {
            if (!Identifier(animal.Name) || !Identifier(animal.HerdId) || !AnimalRules.Species.Any(species => species.Id == animal.Species) ||
                animal.Sex is not ("female" or "male") || animal.CarePermissions is null || animal.RidingPermissions is null)
                throw new InvalidDataException("An animal has invalid species, sex, name or permissions.");
            var definition = AnimalRules.Definition(animal.Species);
            if (animal.BornTick > society.WorldTick || animal.BornTick < -(long)definition.AdultDays * day || !map.IsPassable(animal.Position) ||
                animal.CareUntilTick < 0 || animal.CareUntilTick > society.WorldTick + day || animal.BreedingReadyTick < 0 ||
                animal.WildFedUntilTick < 0 || animal.WildWaterUntilTick < 0 ||
                animal.ProductProgressTicks < 0 || animal.ProductProgressTicks >= Math.Max(1, definition.ProductDays * day) ||
                animal.DiedTick is { } death && (death < 0 || death > society.WorldTick || animal.Pregnancy is not null || animal.RiderId is not null ||
                    animal.LeaderId is not null || animal.ReadyProductLotId is not null || animal.SaddleLotId is not null))
                throw new InvalidDataException("An animal has invalid age, care, product progress or physical state.");
            UniqueAgents(animal.CarePermissions); UniqueAgents(animal.RidingPermissions);
            if (animal.CarePermissions.Concat(animal.RidingPermissions).Any(id => !society.Inhabitants.Any(person => person.Id == id)) ||
                animal.Species != "horse" && (animal.RidingPermissions.Count != 0 || animal.RiderId is not null || animal.SaddleLotId is not null))
                throw new InvalidDataException("Animal permissions must name known people and only horses may be ridden.");
            if ((animal.HouseholdId is null) != (animal.YardId is null) || animal.HouseholdId is { } home &&
                (!society.Households.Any(household => household.Id == home) || animal.DiedTick is null && !state.WorldSimulation!.Buildings.Any(building =>
                    building.InstanceId == animal.YardId && building.HouseholdId == home && state.WorldContent!.Buildings.Any(buildingDefinition =>
                        buildingDefinition.CanonicalId == building.DefinitionId && buildingDefinition.Tags.Contains(AnimalContent.YardTag)))) ||
                animal.HouseholdId is null && (animal.SaddleLotId is not null || animal.ReadyProductLotId is not null ||
                    animal.RiderId is not null || animal.LeaderId is not null || animal.CarePermissions.Count != 0 || animal.RidingPermissions.Count != 0))
                throw new InvalidDataException("An owned animal needs its household's actual animal yard.");
            if (animal.Pregnancy is { } pregnancy && (!Identifier(pregnancy.FatherId) || animal.Sex != "female" ||
                !AnimalRules.IsAdult(animal, society.WorldTick, day) || pregnancy.StartedTick < 0 || pregnancy.StartedTick > society.WorldTick ||
                pregnancy.ProgressTicks < 0 || pregnancy.ProgressTicks >= definition.GestationDays * day ||
                world.Animals.FirstOrDefault(father => father.Id == pregnancy.FatherId) is { } father && (father.Species != animal.Species || father.Sex != "male")))
                throw new InvalidDataException("An animal pregnancy has invalid parents or progress.");
            if (animal.TamingWork is { } tame && (animal.HouseholdId is not null || tame.WorkTicks is < 1 or >= AnimalRules.TamingWorkTicks ||
                !state.Inhabitants.Any(person => person.InhabitantId == tame.ActorId)))
                throw new InvalidDataException("An animal taming task has invalid work or actor.");
            if ((animal.RiderId is not null && animal.LeaderId is not null) || (animal.LeaderId is null) != (animal.LeadDestination is null) ||
                animal.LeadDestination is { } destination && !map.IsPassable(destination))
                throw new InvalidDataException("An animal cannot be ridden and led together and needs a legal lead destination.");
            foreach (var actor in new[] { animal.RiderId, animal.LeaderId }.OfType<string>())
                if (!state.Inhabitants.Any(person => person.InhabitantId == actor && person.Position == animal.Position) ||
                    state.HandcartHitches!.Any(hitch => hitch.PullerId == actor) ||
                    state.BoatTransport.Boats.Any(boat => boat.Journey?.PassengerId == actor) ||
                    world.Animals.Any(other => other.Id != animal.Id && (other.RiderId == actor || other.LeaderId == actor)))
                    throw new InvalidDataException("Animal riders and leaders must share the animal's physical position without another attachment.");
            if (animal.RiderId is not null && (animal.SaddleLotId is null || !AnimalRules.IsAdult(animal, society.WorldTick, day)))
                throw new InvalidDataException("A ridden adult horse needs a fitted saddle.");
            if ((animal.ReadyProductLotId is null) != (animal.ReadyProductReservationId is null) || animal.ReadyProductLotId is { } productId &&
                (!AnimalRules.HasProduct(animal) || !inventory.Lots.Any(lot => lot.Id == productId && lot.OwnerId == animal.HouseholdId &&
                    lot.ItemKind == definition.Product && lot.Quantity == definition.ProductQuantity &&
                    lot.GroundPosition == new InventoryGroundPosition(animal.Position.X, animal.Position.Y)) ||
                 !inventory.Reservations.Any(held => held.Id == animal.ReadyProductReservationId && held.LotId == productId &&
                    held.OwnerId == animal.HouseholdId && held.Quantity == definition.ProductQuantity && held.IsExclusive &&
                    held.State == InventoryReservationState.Reserved && held.Purpose == "animal-product:" + animal.Id)))
                throw new InvalidDataException("A ready animal product requires its real, held household lot.");
            if ((animal.SaddleLotId is null) != (animal.SaddleReservationId is null) || animal.SaddleLotId is { } saddleId &&
                (!inventory.Lots.Any(lot => lot.Id == saddleId && lot.ItemKind == "saddle" && lot.OwnerId == animal.HouseholdId && lot.Quantity == 1 &&
                    lot.GroundPosition == new InventoryGroundPosition(animal.Position.X, animal.Position.Y)) ||
                 !inventory.Reservations.Any(held => held.Id == animal.SaddleReservationId && held.LotId == saddleId && held.OwnerId == animal.HouseholdId &&
                    held.Quantity == 1 && held.IsExclusive && held.State == InventoryReservationState.Reserved && held.Purpose == "animal-saddle:" + animal.Id)))
                throw new InvalidDataException("A fitted saddle requires its real, held household lot.");
        }
        foreach (var household in society.Households)
        {
            int Used(string? yard = null) => world.Animals.Where(animal => animal.DiedTick is null && animal.HouseholdId == household.Id &&
                (yard is null || animal.YardId == yard)).Sum(animal => 1 + (animal.Pregnancy is null ? 0 : 1)) +
                world.Offers.Where(offer => offer.ReceivingHouseholdId == household.Id && (yard is null || offer.ReceivingYardId == yard))
                    .Sum(offer => 1 + (world.Animals.FirstOrDefault(animal => animal.Id == offer.AnimalId)?.Pregnancy is null ? 0 : 1));
            if (Used() > AnimalRules.PopulationCap) throw new InvalidDataException("A household has more than eight animals and reserved offspring places.");
            foreach (var yard in state.WorldSimulation!.Buildings.Where(building => building.HouseholdId == household.Id &&
                         state.WorldContent!.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId && definition.Tags.Contains(AnimalContent.YardTag))))
            {
                var definition = BuildingStorageRules.EffectiveDefinition(state.WorldContent!.Buildings.Single(definition => definition.CanonicalId == yard.DefinitionId), yard);
                if (Used(yard.InstanceId) > Math.Min(8, definition.Width * definition.Height))
                    throw new InvalidDataException("The animal yard has no room for its animals and reserved offspring places.");
            }
        }
        foreach (var herd in world.Animals.Where(animal => animal.DiedTick is null && animal.HouseholdId is null).GroupBy(animal => animal.HerdId))
            if (herd.Sum(animal => 1 + (animal.Pregnancy is null ? 0 : 1)) > AnimalRules.PopulationCap)
                throw new InvalidDataException("A wild herd exceeds eight animals and reserved offspring places.");
        Unique(world.Offers.Select(offer => offer.AnimalId));
        foreach (var offer in world.Offers)
            if (!world.Animals.Any(animal => animal.Id == offer.AnimalId && animal.DiedTick is null && animal.HouseholdId != offer.ReceivingHouseholdId) ||
                !society.Inhabitants.Any(person => person.Id == offer.SellerId) || !society.Inhabitants.Any(person => person.Id == offer.BuyerId &&
                    person.HouseholdId == offer.ReceivingHouseholdId) || !state.WorldSimulation!.Buildings.Any(yard =>
                    yard.InstanceId == offer.ReceivingYardId && yard.HouseholdId == offer.ReceivingHouseholdId) ||
                offer.OfferedTick < 0 || offer.OfferedTick > society.WorldTick ||
                (offer.PaymentLotId is null) != (offer.PaymentKind is null) || offer.PaymentQuantity != (offer.PaymentLotId is null ? 0 : 1))
                throw new InvalidDataException("An animal trade offer has invalid parties, receiving space or exact payment.");
        foreach (var trip in world.SupplyTrips)
            if (!state.Inhabitants.Any(person => person.InhabitantId == trip.ActorId) ||
                !inventory.Lots.Any(lot => lot.Id == trip.LotId && PersonalEquipmentRules.IsCarried(lot, trip.ActorId)) ||
                !state.WorldSimulation!.Buildings.Any(yard => yard.InstanceId == trip.YardId && (yard.HouseholdId ==
                    society.Inhabitants.Single(person => person.Id == trip.ActorId).HouseholdId || world.Animals.Any(animal =>
                        animal.YardId == yard.InstanceId && animal.CarePermissions.Contains(trip.ActorId)))) ||
                (trip.AnimalId is null) != (trip.Action is null) || trip.AnimalId is not null &&
                (!world.Animals.Any(animal => animal.Id == trip.AnimalId) || trip.Action is not ("care" or "collect" or "saddle")))
                throw new InvalidDataException("An animal supply trip needs its real carried goods and household yard.");
        if (inventory.Lots.Any(lot => lot.ItemKind == "milk" && lot.ContainerLotId is null &&
                !world.Animals.Any(animal => animal.ReadyProductLotId == lot.Id)))
            throw new InvalidDataException("Collected milk must be inside a water jug.");
        if ((state.Instructions ?? []).Any(instruction => instruction.Order?.TargetAnimalId is { } id &&
                !world.Animals.Any(animal => animal.Id == id)))
            throw new InvalidDataException("An animal order must retain its exact known animal identity.");
    }
}
