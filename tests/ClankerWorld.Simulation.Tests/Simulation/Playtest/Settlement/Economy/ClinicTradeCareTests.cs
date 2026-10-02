using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed class ClinicTradeCareTests
{
    private const string Alpha = "household:camp-alpha";
    private const string House = "first-town-house-a";
    private const string Clinic = "clinic-purchase-site";

    [Fact]
    public async Task AnIllVisitorBuysActualClinicMedicineAndUsesOnlyThePurchasedDoseAcrossReload()
    {
        using var generated = NormalPathWorld.CreateGenerated("clinic-purchase-care", _ => new Choices([]));
        var state = AddStock(generated.ExportState(), "clinic-build-stone", "stone", Alpha, 4, House);
        using var funded = PrivateWorldRuntime.Restore(state, _ => new Choices([]));
        var house = funded.WorldSimulation.Buildings.Single(building => building.InstanceId == House);
        Assert.Contains(Enumerable.Range(-8, 17).SelectMany(y => Enumerable.Range(-8, 17).Select(x =>
            new GridPoint(house.Position.X + x, house.Position.Y + y))), point =>
            funded.PlaceBuilding(Clinic, CareContent.Clinic1x2().CanonicalId, point, Alpha).Applied);
        var construction = funded.Society.Inventory.Reservations.Where(item =>
            item.Purpose.Contains(Clinic, StringComparison.Ordinal)).ToArray();
        Assert.Contains(construction, item => item.LotId == "clinic-build-stone" && item.Quantity == 4 &&
            item.State == InventoryReservationState.Completed);
        Assert.Equal(10, construction.Where(item => item.LotId != "clinic-build-stone").Sum(item => item.Quantity));

        state = funded.ExportState();
        var seller = state.Society.Society.Inhabitants.First(person => person.HouseholdId == Alpha).Id;
        var buyer = state.Society.Society.Inhabitants.First(person => person.HouseholdId != Alpha).Id;
        var buyerHousehold = state.Society.Society.GetInhabitant(buyer).HouseholdId;
        var clinicPosition = state.WorldSimulation!.Buildings.Single(building => building.InstanceId == Clinic).Position;
        state = AddStock(state, "clinic-herbs", CareContent.MedicinalHerbs, Alpha, 4, Clinic);
        state = AddStock(state, "clinic-fuel", "wood", Alpha, 2, Clinic);
        state = AddStock(state, "clinic-jug", InventoryContainerRules.WaterJug, Alpha, 1, Clinic);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "clinic-water", InventoryContainerRules.FreshWater, Alpha, 4,
            storageBuildingId: Clinic, containerLotId: "clinic-jug"));
        state = PutAt(state, seller, clinicPosition);
        using var manufacturing = PrivateWorldRuntime.Restore(state, _ => new Choices([]));
        var recipe = manufacturing.WorldContent.Recipes.Single(item => item.LocalId == "clinic-medicine");
        var madeLots = new List<string>();
        for (var batch = 0; batch < 2; batch++)
        {
            var started = manufacturing.StartProduction(recipe.CanonicalId, Clinic, seller);
            Assert.True(started.Applied, started.Failure);
            for (var tick = 0; tick < 16; tick++) Assert.True((await manufacturing.AdvanceOneTickAsync()).Advanced);
            var job = manufacturing.WorldSimulation.ProductionJobs.Single(item => item.JobId == started.JobId);
            Assert.Equal(WorldProductionJobState.Completed, job.State);
            var paidInputs = job.InputReservationIds.Select(manufacturing.Society.Inventory.GetReservation).ToArray();
            Assert.Contains(paidInputs, item => item.LotId == "clinic-herbs" && item.Quantity == 2);
            Assert.Contains(paidInputs, item => item.LotId == "clinic-fuel" && item.Quantity == 1);
            Assert.Contains(paidInputs, item => item.LotId == "clinic-water" && item.Quantity == 1);
            Assert.All(paidInputs, item => Assert.Equal((Alpha, InventoryReservationState.Completed),
                (item.OwnerId, item.State)));
            var lotId = started.JobId + ":output:00";
            madeLots.Add(lotId);
            Assert.Equal((CareContent.Medicine, Alpha, Clinic, 2),
                (manufacturing.Society.Inventory.GetLot(lotId).ItemKind,
                    manufacturing.Society.Inventory.GetLot(lotId).OwnerId,
                    manufacturing.Society.Inventory.GetLot(lotId).StorageBuildingId,
                    manufacturing.Society.Inventory.GetLot(lotId).Quantity));
        }
        state = manufacturing.ExportState();
        Assert.Equal(2, state.Society.Society.Inventory.GetLot("clinic-water").Quantity);
        state = WithInventory(state, InventoryFixture.AddLot(state.Society.Society.Inventory,
            "clinic-buyer-payment", "wood", buyer, 2));
        state = PutAt(state, buyer, clinicPosition);
        var sellerStart = state.Map.Tiles.Select(tile => tile.Position).First(point =>
            state.Map.IsPassable(point) && state.Map.FootDistance(point, clinicPosition) is >= 4 and <= 6 &&
            state.Map.IsReachableOnFoot(point, clinicPosition) &&
            state.Inhabitants.All(person => person.Position != point));
        state = PutAt(state, seller, sellerStart) with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == buyer
                ? person with
                {
                    Position = clinicPosition,
                    HungerBasisPoints = 9_000,
                    Survival = new SurvivalCondition(IllnessBasisPoints: 5_000),
                    LastDecisionContext = null
                }
                : person.InhabitantId == seller
                    ? person with { Position = sellerStart, HungerBasisPoints = 9_000, LastDecisionContext = null }
                    : person).ToArray(),
        };
        Assert.True(state.Map.FootDistance(sellerStart, clinicPosition) > 1);
        Assert.Equal(clinicPosition, state.Inhabitants.Single(person => person.InhabitantId == buyer).Position);

        var buyerChoices = new Choices(["business_shop:" + Clinic, "business_continue:"]);
        var sellerChoices = new Choices(["business_continue:"]);
        IDecisionProvider Provider(string id) => id == buyer ? buyerChoices :
            id == seller ? sellerChoices : new Choices([]);
        using var world = PrivateWorldRuntime.Restore(state, Provider);
        var beforeDenied = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.TreatPatient(buyer, buyer, CareContent.Medicine).Applied);
        Assert.False(world.StartProduction(recipe.CanonicalId, Clinic, buyer).Applied);
        Assert.Equal(beforeDenied, PrivateWorldRuntimeCodec.Encode(world.ExportState()));

        for (var tick = 0; tick < 40 && world.BusinessTrades.Count == 0; tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        var trade = Assert.Single(world.BusinessTrades);
        var offer = world.Society.Inventory.GetOffer(trade.OfferId);
        Assert.Equal(DirectBarterState.Open, offer.State);
        Assert.Equal((Alpha, buyer, 2, 1, "clinic-buyer-payment"),
            (offer.FirstPartyId, offer.SecondPartyId, offer.FirstQuantity, offer.SecondQuantity, offer.SecondLotId));
        Assert.Contains(offer.FirstLotId, madeLots);
        Assert.Equal((Alpha, Clinic, 2), (world.Society.Inventory.GetLot(offer.FirstLotId).OwnerId,
            world.Society.Inventory.GetLot(offer.FirstLotId).StorageBuildingId,
            world.Society.Inventory.GetLot(offer.FirstLotId).Quantity));
        var barterReservations = world.Society.Inventory.Reservations.Where(item => item.Purpose == "barter:" + offer.Id).ToArray();
        Assert.NotEmpty(barterReservations);
        Assert.All(barterReservations,
            item => Assert.Equal(InventoryReservationState.Reserved, item.State));
        Assert.False(world.TreatPatient(buyer, buyer, CareContent.Medicine).Applied);
        Assert.Null(world.ExportState().Inhabitants.Single(person => person.InhabitantId == buyer).MedicalTreatment);

        var openBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(openBytes), Provider);
        Assert.Equal(openBytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        for (var tick = 0; tick < 80 && world.Society.Inventory.GetOffer(offer.Id).State == DirectBarterState.Open; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Equal(DirectBarterState.Settled, world.Society.Inventory.GetOffer(offer.Id).State);
        Assert.Equal(seller, Assert.Single(world.BusinessTrades).SellerActorId);
        var physical = world.ExportState().Inhabitants;
        Assert.NotEqual(sellerStart, physical.Single(person => person.InhabitantId == seller).Position);
        Assert.True(state.Map.FootDistance(physical.Single(person => person.InhabitantId == seller).Position,
            physical.Single(person => person.InhabitantId == buyer).Position) <= 1);
        Assert.Equal(clinicPosition, physical.Single(person => person.InhabitantId == buyer).Position);
        Assert.Contains("business_shop:" + Clinic, buyerChoices.Offered);
        Assert.Contains("business_continue:" + offer.Id, sellerChoices.Offered);
        var purchased = world.Society.Inventory.GetLot(offer.FirstLotId);
        Assert.Equal((buyer, 2), (purchased.OwnerId, purchased.Quantity));
        Assert.True(PersonalEquipmentRules.IsCarried(purchased, buyer));
        var payment = Assert.Single(world.Society.Inventory.Lots, lot => lot.ProvenanceLotId == "clinic-buyer-payment");
        Assert.Equal((Alpha, Clinic, 1), (payment.OwnerId, payment.StorageBuildingId, payment.Quantity));
        Assert.Equal(1, world.Society.Inventory.GetLot("clinic-buyer-payment").Quantity);
        var privateRemainderId = Assert.Single(madeLots, id => id != purchased.Id);
        Assert.Equal((Alpha, Clinic, 2), (world.Society.Inventory.GetLot(privateRemainderId).OwnerId,
            world.Society.Inventory.GetLot(privateRemainderId).StorageBuildingId,
            world.Society.Inventory.GetLot(privateRemainderId).Quantity));
        Assert.Equal(buyerHousehold, world.Society.GetInhabitant(buyer).HouseholdId);
        Assert.Null(physical.Single(person => person.InhabitantId == buyer).MedicalConsent);
        var afterPurchase = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.False(world.StartProduction(recipe.CanonicalId, Clinic, buyer).Applied);
        Assert.False(world.TreatPatient(seller, buyer, CareContent.Medicine).Applied);
        Assert.Equal(afterPurchase, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        Assert.Equal(afterPurchase, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));

        buyerChoices.TreatmentCandidate = "medical_treat:" + buyer + ":medicine";
        for (var tick = 0; tick < 40 && world.ExportState().Inhabitants.Single(person =>
                 person.InhabitantId == buyer).MedicalTreatment is null; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await restored.AdvanceOneTickAsync()).Advanced);
        }
        var treatment = Assert.IsType<MedicalTreatmentState>(world.ExportState().Inhabitants.Single(person =>
            person.InhabitantId == buyer).MedicalTreatment);
        Assert.Equal((buyer, purchased.Id, buyer, CareContent.Medicine),
            (treatment.CaregiverId, treatment.SupplyLotId, treatment.SupplyOwnerId, treatment.Kind));
        var dose = world.Society.Inventory.GetReservation(treatment.DoseReservationId);
        Assert.Equal((purchased.Id, buyer, 1, InventoryReservationState.Completed),
            (dose.LotId, dose.OwnerId, dose.Quantity, dose.State));
        Assert.Contains("medical_treat:" + buyer + ":medicine", buyerChoices.Offered);
        Assert.Equal(1, world.Society.Inventory.GetLot(purchased.Id).Quantity);
        Assert.Equal(2, world.Society.Inventory.GetLot(privateRemainderId).Quantity);
        Assert.Null(world.ExportState().Inhabitants.Single(person => person.InhabitantId == buyer).MedicalConsent);
        var treatmentBytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(treatmentBytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        using var resumedTreatment = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(treatmentBytes), Provider);
        buyerChoices.TreatmentCandidate = null;
        for (var tick = 0; tick < treatment.RemainingTicks; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await resumedTreatment.AdvanceOneTickAsync()).Advanced);
        }
        Assert.Null(world.ExportState().Inhabitants.Single(person => person.InhabitantId == buyer).MedicalTreatment);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "medical_treatment_completed" && item.Detail == buyer);
        Assert.Equal(1, world.Society.Inventory.GetLot(purchased.Id).Quantity);
        Assert.Equal(2, world.Society.Inventory.GetLot(privateRemainderId).Quantity);
        Assert.Equal(buyerHousehold, world.Society.GetInhabitant(buyer).HouseholdId);
        Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()),
            PrivateWorldRuntimeCodec.Encode(resumedTreatment.ExportState()));
    }

    private static PrivateWorldRuntimeState AddStock(PrivateWorldRuntimeState state, string id, string kind,
        string owner, int quantity, string building) => WithInventory(state, InventoryFixture.AddLot(
            state.Society.Society.Inventory, id, kind, owner, quantity, storageBuildingId: building));

    private static PrivateWorldRuntimeState WithInventory(PrivateWorldRuntimeState state, InventoryCheckpoint inventory) =>
        state with { Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } } };

    private static PrivateWorldRuntimeState PutAt(PrivateWorldRuntimeState state, string actor, GridPoint position)
    {
        var oldPosition = state.Inhabitants.Single(person => person.InhabitantId == actor).Position;
        return state with
        {
            Inhabitants = state.Inhabitants.Select(person => person.InhabitantId == actor
                ? person with { Position = position, HungerBasisPoints = 9_000, LastDecisionContext = null }
                : person.Position == position ? person with { Position = oldPosition } : person).ToArray(),
        };
    }

    private sealed class Choices(IReadOnlyList<string> prefixes) : IDecisionProvider
    {
        public string? TreatmentCandidate { get; set; }
        public HashSet<string> Offered { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
            CancellationToken cancellationToken = default)
        {
            foreach (var candidate in request.Observation.Candidates) Offered.Add(candidate.Id);
            var choice = request.Observation.Candidates.FirstOrDefault(candidate => candidate.Id == TreatmentCandidate) ??
                prefixes.Select(prefix => request.Observation.Candidates.FirstOrDefault(candidate =>
                    candidate.Id.StartsWith(prefix, StringComparison.Ordinal))).FirstOrDefault(candidate => candidate is not null) ??
                request.Observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
            return new DeterministicDecisionProvider().DecideAsync(request with
            { Observation = request.Observation with { Candidates = [choice] } }, cancellationToken);
        }
    }
}
