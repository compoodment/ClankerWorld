using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static void ValidateToolMakingRequests(IReadOnlyList<ToolMakingRequestState>? requests,
        WorldContentSimulationState simulation, DeclarativeWorldContentState content, SocietyCheckpoint societyState,
        IEnumerable<PlaytestInhabitantState> people, IReadOnlyList<BusinessTradeState> trades, long worldTick)
    {
        if (requests is null || requests.Any(request => request is null) || requests.Count >
                ToolMakingRequestRules.MaximumActiveRequests + ToolMakingRequestRules.MaximumTerminalRequests ||
            requests.Count(request => !ToolMakingRequestRules.IsTerminal(request.Status)) > ToolMakingRequestRules.MaximumActiveRequests ||
            requests.Count(request => ToolMakingRequestRules.IsTerminal(request.Status)) > ToolMakingRequestRules.MaximumTerminalRequests ||
            !requests.Select(request => request.Id).SequenceEqual(requests.Select(request => request.Id)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) ||
            requests.Where(request => !ToolMakingRequestRules.IsTerminal(request.Status)).GroupBy(request => request.RequesterId)
                .Any(group => group.Count() > 1) || requests.Where(request => request.JobId is not null).GroupBy(request => request.JobId)
                .Any(group => group.Count() > 1) || requests.Where(request => request.OfferId is not null).GroupBy(request => request.OfferId)
                .Any(group => group.Count() > 1))
            throw new InvalidDataException("Tool requests are missing, unbounded or duplicated.");
        var physical = people.ToDictionary(person => person.InhabitantId, StringComparer.Ordinal);
        foreach (var request in requests)
        {
            var terminal = ToolMakingRequestRules.IsTerminal(request.Status);
            var recipe = content.Recipes.FirstOrDefault(recipe => recipe.CanonicalId == request.RecipeId);
            var building = simulation.Buildings.FirstOrDefault(building => building.InstanceId == request.BuildingInstanceId);
            var accepted = request.AcceptedTick is not null;
            if (!Enum.IsDefined(request.Status) || !BoundedBusinessText(request.Id, 128) ||
                !request.Id.StartsWith("tool-making-request:", StringComparison.Ordinal) ||
                request.Id != ToolMakingRequestId(request.RequesterId, request.BuildingInstanceId, request.RecipeId, request.RequestedTick) ||
                !BoundedBusinessText(request.BuildingInstanceId, 256) || !BoundedBusinessText(request.RecipeId, 128) ||
                ToolProgressionRules.Find(request.ItemKind) is null || request.RequestedTick < 0 ||
                request.LastTransitionTick < request.RequestedTick || request.LastTransitionTick > worldTick ||
                request.AcceptedTick is { } acceptedTick && (acceptedTick < request.RequestedTick || acceptedTick > request.LastTransitionTick) ||
                !societyState.Inhabitants.Any(person => person.Id == request.RequesterId) ||
                !societyState.Households.Any(household => household.Id == request.SellerHouseholdId) ||
                request.Blocker is not null && !BoundedBusinessText(request.Blocker, 160) ||
                accepted != (request.WorkerId is not null) || request.WorkerId is { } worker &&
                    !societyState.Inhabitants.Any(person => person.Id == worker) ||
                request.Status is ToolMakingRequestStatus.Requested or ToolMakingRequestStatus.Refused &&
                    (accepted || request.JobId is not null || request.OfferId is not null) ||
                request.Status is ToolMakingRequestStatus.Accepted or ToolMakingRequestStatus.Ready or
                    ToolMakingRequestStatus.Offered or ToolMakingRequestStatus.Fulfilled && !accepted ||
                !terminal && (building is null || building.HouseholdId != request.SellerHouseholdId ||
                    recipe is null || recipe.WorkstationBuildingId != building.DefinitionId || !ToolMakingRequestRules.IsToolRecipe(recipe) ||
                    !content.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                        definition.Tags.Contains("blacksmith", StringComparer.Ordinal)) ||
                    !physical.ContainsKey(request.RequesterId) ||
                    societyState.GetInhabitant(request.RequesterId).AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder)) ||
                request.Status == ToolMakingRequestStatus.Accepted &&
                    (!physical.ContainsKey(request.WorkerId!) || societyState.GetInhabitant(request.WorkerId!).HouseholdId != request.SellerHouseholdId ||
                     societyState.GetInhabitant(request.WorkerId!).AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder)) ||
                recipe is not null && (!ToolMakingRequestRules.IsToolRecipe(recipe) || recipe.Outputs[0].ResourceId != request.ItemKind))
                throw new InvalidDataException("A tool request has invalid identity, authority, status or timing.");
            if (request.JobId is { } jobId)
            {
                var job = simulation.ProductionJobs.FirstOrDefault(job => job.JobId == jobId);
                if (!accepted || job is null || job.ToolMakingRequestId != request.Id || job.WorkerId != request.WorkerId || job.RecipeId != request.RecipeId ||
                    job.BuildingInstanceId != request.BuildingInstanceId || job.StartedTick < request.AcceptedTick.GetValueOrDefault() ||
                    !HasCompleteToolMakingInputs(job, recipe, societyState.Inventory) ||
                    job.InputReservationIds.Any(id => !societyState.Inventory.Reservations.Any(receipt => receipt.Id == id &&
                        receipt.OwnerId == request.SellerHouseholdId && (job.State != WorldProductionJobState.Completed ||
                            receipt.State == InventoryReservationState.Completed))) ||
                    request.Status is ToolMakingRequestStatus.Ready or ToolMakingRequestStatus.Offered or ToolMakingRequestStatus.Fulfilled &&
                        job.State != WorldProductionJobState.Completed ||
                    societyState.Inventory.Lots.Any(lot => lot.Id == jobId + ":output:00" && lot.ItemKind != request.ItemKind))
                    throw new InvalidDataException("A tool request is not bound to its actual ordinary production job.");
            }
            else if (request.Status is ToolMakingRequestStatus.Ready or ToolMakingRequestStatus.Offered or ToolMakingRequestStatus.Fulfilled)
                throw new InvalidDataException("An unfinished tool request cannot promise goods or payment.");
            if (request.OfferId is { } offerId)
            {
                var offer = societyState.Inventory.Offers.FirstOrDefault(offer => offer.Id == offerId);
                var trade = trades.FirstOrDefault(trade => trade.OfferId == offerId);
                if (request.JobId is null || offer is null || trade is null ||
                    offer.FirstLotId != request.JobId + ":output:00" || offer.FirstQuantity != 1 ||
                    offer.FirstPartyId != request.SellerHouseholdId || offer.SecondPartyId != request.RequesterId ||
                    trade.BuildingInstanceId != request.BuildingInstanceId || trade.SellerHouseholdId != request.SellerHouseholdId ||
                    trade.BuyerId != request.RequesterId || trade.GoodsKind != request.ItemKind ||
                    offer.State == DirectBarterState.Settled && request.Status != ToolMakingRequestStatus.Fulfilled ||
                    request.Status == ToolMakingRequestStatus.Fulfilled && offer.State != DirectBarterState.Settled ||
                    request.Status == ToolMakingRequestStatus.Offered && offer.State != DirectBarterState.Open ||
                    ToolMakingRequestRules.IsTerminal(request.Status) && request.Status != ToolMakingRequestStatus.Fulfilled &&
                        offer.State != DirectBarterState.Cancelled)
                    throw new InvalidDataException("A tool request does not match its exact physical inventory exchange.");
            }
            else if (request.Status is ToolMakingRequestStatus.Offered or ToolMakingRequestStatus.Fulfilled)
                throw new InvalidDataException("A tool purchase must retain its authoritative inventory offer.");
            if (request.Status == ToolMakingRequestStatus.Accepted && request.JobId is null &&
                (!physical.TryGetValue(request.WorkerId!, out var person) ||
                    person.Project?.ToolMakingRequestId != request.Id || person.Project.StartedTick != request.AcceptedTick ||
                    person.Project.CandidateId != "build:recipe:" + request.RecipeId))
                throw new InvalidDataException("An accepted tool request has no actual assigned work plan.");
        }
        foreach (var person in physical.Values.Where(person => person.Project?.ToolMakingRequestId is not null))
            if (!requests.Any(request => request.Id == person.Project!.ToolMakingRequestId &&
                request.WorkerId == person.InhabitantId && request.AcceptedTick == person.Project.StartedTick &&
                request.Status == ToolMakingRequestStatus.Accepted && person.Project.CandidateId == "build:recipe:" + request.RecipeId))
                throw new InvalidDataException("A household project has an invalid tool request binding.");
        foreach (var job in simulation.ProductionJobs.Where(job => job.ToolMakingRequestId is not null))
            if (!requests.Any(request => request.Id == job.ToolMakingRequestId && request.JobId == job.JobId))
                throw new InvalidDataException("A production job has no matching tool request history.");
    }

    private static bool HasCompleteToolMakingInputs(WorldProductionJob job, RecipeDefinition? recipe, InventoryCheckpoint inventory)
    {
        var purpose = job.JobId + ":input";
        var receipts = inventory.Reservations.Where(receipt => receipt.Purpose == purpose).ToArray();
        if (receipts.Length == 0 || job.InputReservationIds is not { Count: > 0 } ||
            !job.InputReservationIds.Order(StringComparer.Ordinal).SequenceEqual(receipts.Select(receipt => receipt.Id).Order(StringComparer.Ordinal)))
            return false;
        if (recipe is null) return true;
        var matched = 0;
        for (var index = 0; index < recipe.Inputs.Count; index++)
        {
            var input = recipe.Inputs[index];
            var group = receipts.Where(receipt => receipt.Id == purpose + ":quantity:" +
                index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":lot:" + receipt.LotId).ToArray();
            if (group.Sum(receipt => (long)receipt.Quantity) != input.Amount || group.Any(receipt =>
                inventory.Lots.Any(lot => lot.Id == receipt.LotId && lot.ItemKind != input.ResourceId)))
                return false;
            matched += group.Length;
        }
        return matched == receipts.Length;
    }
}
