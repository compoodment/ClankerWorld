using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string ToolRequestPrefix = "tool_request_";
    private List<ToolMakingRequestState> toolMakingRequests = [];

    public IReadOnlyList<ToolMakingRequestState> ToolMakingRequests => toolMakingRequests
        .OrderBy(request => request.Id, StringComparer.Ordinal).ToArray();

    public string? ToolMakingRequestNote(string actor)
    {
        gate.Wait();
        try { return ToolMakingRequestNoteCore(actor); }
        finally { gate.Release(); }
    }

    private string? ToolMakingRequestNoteCore(string actor) => inhabitants.ContainsKey(actor)
        ? ToolMakingRequestRules.Note(toolMakingRequests, actor, HouseholdFor(actor)) : null;

    private static string ToolRequestChoice(string verb, params string[] identity) => ToolRequestPrefix + verb + ":" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identity))));

    private static string ToolMakingRequestId(string actor, string building, string recipe, long tick) =>
        "tool-making-request:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new[] { actor, building, recipe, tick.ToString(System.Globalization.CultureInfo.InvariantCulture) }))));

    private bool IsToolRequestSeller(string actor, ToolMakingRequestState request) =>
        inhabitants.ContainsKey(actor) && AdultResident(actor) && HouseholdFor(actor) == request.SellerHouseholdId &&
        ToolRequestShop(request) is not null;

    private PlacedBuilding? ToolRequestShop(ToolMakingRequestState request) => worldSimulation.Buildings.FirstOrDefault(building =>
        building.InstanceId == request.BuildingInstanceId && building.HouseholdId == request.SellerHouseholdId &&
        worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
            definition.Tags.Contains("blacksmith", StringComparer.Ordinal)));

    private bool IdleForToolRequest(string actor) => inhabitants[actor].Project is null or { Stage: "completed" or "cancelled" } &&
        inhabitants[actor].Equipment?.Repair is null && FarmWorkFor(actor) is null && !IsConversationBusy(actor);

    private bool ToolRequestWanted(string actor, RecipeDefinition recipe)
    {
        var tool = ToolProgressionRules.Find(recipe.Outputs[0].ResourceId)!;
        if (tool.Family is ToolFamily.Hoe or ToolFamily.Sickle && FarmhouseForHousehold(HouseholdFor(actor)) is null)
            return false;
        var carried = ToolProgressionRules.BestUsableTool(society.Checkpoint.Inventory, actor, tool.Family);
        return carried is null || ToolProgressionRules.Find(carried.ItemKind)!.Tier < tool.Tier;
    }

    private IEnumerable<(PlacedBuilding Building, RecipeDefinition Recipe)> ToolRequestOptions(string actor)
    {
        if (!AdultResident(actor) || toolMakingRequests.Any(request => request.RequesterId == actor &&
                !ToolMakingRequestRules.IsTerminal(request.Status)) ||
            toolMakingRequests.Count(request => !ToolMakingRequestRules.IsTerminal(request.Status)) >= ToolMakingRequestRules.MaximumActiveRequests)
            return [];
        var position = inhabitants[actor].Position;
        return worldSimulation.Buildings.Where(building => building.HouseholdId is { } household && household != HouseholdFor(actor) &&
                inhabitants.Keys.Any(seller => AdultResident(seller) && HouseholdFor(seller) == household) &&
                worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                    definition.Tags.Contains("blacksmith", StringComparer.Ordinal)) &&
                (IsWithinInteractionRange(position, building.Position, ResourceInteractionRange) || KnowsMapFact(actor, building.Position)) &&
                (IsWithinInteractionRange(position, building.Position, ResourceInteractionRange) ||
                    FindUnoccupiedRoute(actor, position, building.Position, ResourceInteractionRange).Count > 0))
            .OrderBy(building => building.InstanceId, StringComparer.Ordinal)
            .SelectMany(building => worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == building.DefinitionId &&
                    ToolMakingRequestRules.IsToolRecipe(recipe) && ToolRequestWanted(actor, recipe) &&
                    !society.Checkpoint.Inventory.Lots.Any(lot => lot.OwnerId == building.HouseholdId &&
                        lot.StorageBuildingId == building.InstanceId && lot.ItemKind == recipe.Outputs[0].ResourceId &&
                        IsLooseBusinessLot(lot) && AvailableLotQuantity(lot) > 0))
                .OrderBy(recipe => recipe.CanonicalId, StringComparer.Ordinal).Select(recipe => (building, recipe))).Take(16);
    }

    private void AddToolMakingRequestCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!AdultResident(actor)) return;
        foreach (var request in toolMakingRequests.Where(request => !ToolMakingRequestRules.IsTerminal(request.Status))
                     .OrderBy(request => request.Id, StringComparer.Ordinal))
        {
            if (request.RequesterId == actor)
            {
                candidates.Add(new(ToolRequestChoice("withdraw", request.Id),
                    "Withdraw your tool request. Household materials and work remain household property.", 65, request.BuildingInstanceId));
                if (request.Status == ToolMakingRequestStatus.Ready && IdleForToolRequest(actor) && ToolRequestShop(request) is { } shop)
                {
                    if (!IsWithinInteractionRange(inhabitants[actor].Position, shop.Position, ResourceInteractionRange))
                        candidates.Add(new(ToolRequestChoice("collect", request.Id), "Walk to the Blacksmith to discuss payment for the finished tool.", 14, shop.InstanceId));
                    else if (ToolRequestQuote(actor, request, shop) is { } quote)
                        candidates.Add(new(ToolRequestChoice("collect", request.Id),
                            $"Offer {quote.PaymentQuantity} {quote.Payment.ItemKind} for the actual finished {request.ItemKind.Replace('_', ' ')}. The household may refuse.", 14, shop.InstanceId));
                }
            }
            if (!IsToolRequestSeller(actor, request)) continue;
            if (request.Status == ToolMakingRequestStatus.Requested)
            {
                var name = society.Checkpoint.GetInhabitant(request.RequesterId).Name;
                if (IdleForToolRequest(actor)) candidates.Add(new(ToolRequestChoice("accept", request.Id),
                    $"Accept {name}'s {request.ItemKind.Replace('_', ' ')} request using household materials; payment is discussed after it is made.", 50, request.BuildingInstanceId));
                candidates.Add(new(ToolRequestChoice("refuse", request.Id), $"Refuse {name}'s tool request without taking anything.", 65, request.BuildingInstanceId));
            }
            else if (request.Status == ToolMakingRequestStatus.Accepted && request.WorkerId == actor &&
                     inhabitants[actor].Project is { ToolMakingRequestId: { } id } project && id == request.Id &&
                     ToolRequestShop(request) is { } workShop && worldContent.Recipes.FirstOrDefault(recipe => recipe.CanonicalId == request.RecipeId) is { } workRecipe &&
                     HasIngredientsAtBuilding(workRecipe.Inputs, request.SellerHouseholdId, workShop.InstanceId) &&
                     inhabitants[actor].Equipment?.Repair is null && FarmWorkFor(actor) is null && !IsConversationBusy(actor))
                candidates.Add(new(ToolRequestChoice("work", request.Id),
                    "Continue the accepted tool work using its ordinary household production plan.", 30, request.BuildingInstanceId));
        }
        if (!IdleForToolRequest(actor) || NeedsUrgentFood(inhabitants[actor]) || NeedsUrgentWarmth(inhabitants[actor])) return;
        foreach (var (building, recipe) in ToolRequestOptions(actor))
        {
            var local = IsWithinInteractionRange(inhabitants[actor].Position, building.Position, ResourceInteractionRange);
            candidates.Add(new(ToolRequestChoice(local ? "place" : "visit", building.InstanceId, recipe.CanonicalId), local
                ? $"Ask this Blacksmith household to make a {recipe.Outputs[0].ResourceId.Replace('_', ' ')}. No payment or material changes hands now."
                : $"Visit the known Blacksmith to ask about making a {recipe.Outputs[0].ResourceId.Replace('_', ' ')}.", 33, building.InstanceId));
        }
    }

    private bool ApplyToolMakingRequestDecision(SocietyCognitionDispatchResult decision)
    {
        var selected = decision.Admission.Intention;
        if (selected is null || !selected.CandidateId.StartsWith(ToolRequestPrefix, StringComparison.Ordinal) ||
            selected.CandidateId.StartsWith(ToolRequestPrefix + "visit:", StringComparison.Ordinal) ||
            selected.CandidateId.StartsWith(ToolRequestPrefix + "work:", StringComparison.Ordinal) ||
            selected.CandidateId.StartsWith(ToolRequestPrefix + "collect:", StringComparison.Ordinal)) return false;
        if (PendingInstructionFor(decision.InhabitantId)?.Kind == OwnerInstructionKind.MustDo) return false;
        if (!decision.Admission.Accepted || decision.Admission.FellBack || selected.Provider != DecisionProviderKind.LargeLanguageModel ||
            selected.InhabitantId != decision.InhabitantId || !inhabitants.ContainsKey(decision.InhabitantId) || !AdultResident(decision.InhabitantId)) return true;
        var actor = decision.InhabitantId;
        if (!CreateCandidates(actor, inhabitants[actor]).Any(candidate => candidate.Id == selected.CandidateId)) return true;
        foreach (var request in toolMakingRequests.ToArray())
        {
            if (selected.CandidateId == ToolRequestChoice("withdraw", request.Id) && request.RequesterId == actor && !ToolMakingRequestRules.IsTerminal(request.Status))
            {
                if (request.OfferId is { } paidOffer && society.Checkpoint.Inventory.Offers.Any(offer =>
                        offer.Id == paidOffer && offer.State == DirectBarterState.Settled))
                {
                    SetToolRequest(request with { Status = ToolMakingRequestStatus.Fulfilled, Blocker = null }, "tool_request_completed", actor);
                    return true;
                }
                CloseToolRequestOffer(request, "The customer withdrew the request.");
                SetToolRequest(request with { Status = ToolMakingRequestStatus.Withdrawn, Blocker = null }, "tool_request_withdrawn", actor);
                return true;
            }
            if (request.Status != ToolMakingRequestStatus.Requested || !IsToolRequestSeller(actor, request)) continue;
            if (selected.CandidateId == ToolRequestChoice("refuse", request.Id))
            {
                SetToolRequest(request with { Status = ToolMakingRequestStatus.Refused }, "tool_request_refused", actor);
                return true;
            }
            if (selected.CandidateId == ToolRequestChoice("accept", request.Id) && IdleForToolRequest(actor))
            {
                SetToolRequest(request with { Status = ToolMakingRequestStatus.Accepted, WorkerId = actor, AcceptedTick = WorldTick }, "tool_request_accepted", actor);
                BeginProject(actor, inhabitants[actor], "build:recipe:" + request.RecipeId, request.Id);
                return true;
            }
        }
        foreach (var (building, recipe) in ToolRequestOptions(actor))
            if (selected.CandidateId == ToolRequestChoice("place", building.InstanceId, recipe.CanonicalId) &&
                IsWithinInteractionRange(inhabitants[actor].Position, building.Position, ResourceInteractionRange))
            {
                var id = ToolMakingRequestId(actor, building.InstanceId, recipe.CanonicalId, WorldTick);
                toolMakingRequests.Add(new(id, actor, building.HouseholdId!, building.InstanceId,
                    recipe.CanonicalId, recipe.Outputs[0].ResourceId, WorldTick, WorldTick));
                checkpointSchemaVersion = StateSchemaVersion;
                AppendEvent("tool_request_placed", actor + ":" + id);
                break;
            }
        return true;
    }

    // Authority-changing actions above never execute from a continuing or fallback intention.
    private void ApplyToolMakingRequestCandidate(string actor, PlaytestInhabitantState person, string candidate, bool freshChoice)
    {
        if (!AdultResident(actor)) return;
        foreach (var (building, recipe) in ToolRequestOptions(actor))
            if (candidate == ToolRequestChoice("visit", building.InstanceId, recipe.CanonicalId) && IdleForToolRequest(actor))
            {
                MoveToward(actor, person, building.Position, "tool_request", ResourceInteractionRange);
                return;
            }
        foreach (var request in toolMakingRequests.ToArray())
        {
            if (candidate == ToolRequestChoice("work", request.Id) && request.Status == ToolMakingRequestStatus.Accepted &&
                request.WorkerId == actor && IsToolRequestSeller(actor, request) &&
                person.Project?.ToolMakingRequestId == request.Id && (freshChoice || !person.Project.RequiresFreshChoice) &&
                person.Equipment?.Repair is null && FarmWorkFor(actor) is null && !IsConversationBusy(actor))
            {
                BeginProject(actor, person, "build:recipe:" + request.RecipeId, request.Id);
                return;
            }
            if (candidate != ToolRequestChoice("collect", request.Id) || request.Status != ToolMakingRequestStatus.Ready ||
                request.RequesterId != actor || !IdleForToolRequest(actor) || ToolRequestShop(request) is not { } shop) continue;
            if (!IsWithinInteractionRange(person.Position, shop.Position, ResourceInteractionRange))
                MoveToward(actor, person, shop.Position, "tool_request", ResourceInteractionRange);
            else if (ToolRequestQuote(actor, request, shop) is { } quote)
            {
                var offerId = OpenBusinessQuote(actor, shop, quote);
                SetToolRequest(request with { Status = ToolMakingRequestStatus.Offered, OfferId = offerId, Blocker = null });
            }
            return;
        }
    }

    private BusinessQuote? ToolRequestQuote(string actor, ToolMakingRequestState request, PlacedBuilding shop) =>
        request.JobId is { } jobId && worldSimulation.ProductionJobs.Any(job => job.JobId == jobId && job.State == WorldProductionJobState.Completed)
            ? BusinessOpportunity(actor, shop, jobId + ":output:00", requestedGoods: true) : null;

    private bool HasToolMakingDemand(RecipeDefinition recipe, string? owner, string? worker) => toolMakingRequests.Any(request =>
        request.SellerHouseholdId == owner && request.RecipeId == recipe.CanonicalId &&
        request.Status == ToolMakingRequestStatus.Accepted && request.JobId is null && (worker is null || request.WorkerId == worker));

    private string? ToolMakingRequestJobFor(string worker, RecipeDefinition recipe, string building) =>
        inhabitants.TryGetValue(worker, out var person) && person.Project?.ToolMakingRequestId is { } id &&
        person.Project.WorkDone >= ProjectWorkTicks &&
        toolMakingRequests.Any(request => request.Id == id && request.Status == ToolMakingRequestStatus.Accepted &&
            request.JobId is null && request.WorkerId == worker && request.AcceptedTick == person.Project.StartedTick &&
            request.RecipeId == recipe.CanonicalId && request.BuildingInstanceId == building && IsToolRequestSeller(worker, request)) ? id : null;

    private void BindToolMakingJob(WorldProductionJob job)
    {
        if (job.ToolMakingRequestId is not { } id) return;
        var request = toolMakingRequests.Single(request => request.Id == id);
        SetToolRequest(request with { JobId = job.JobId, Blocker = null });
    }

    private void CloseToolRequestOffer(ToolMakingRequestState request, string reason)
    {
        if (request.OfferId is null) return;
        var offer = society.Checkpoint.Inventory.Offers.FirstOrDefault(offer => offer.Id == request.OfferId);
        var binding = businessTrades.FirstOrDefault(trade => trade.OfferId == request.OfferId);
        if (offer is { State: DirectBarterState.Open } && binding is not null) CancelBusinessTrade(binding, offer, reason);
    }

    private void SetToolRequest(ToolMakingRequestState request, string? eventKind = null, string? actor = null)
    {
        if (request.Blocker is { Length: > 160 } blocker) request = request with { Blocker = blocker[..160] };
        var index = toolMakingRequests.FindIndex(item => item.Id == request.Id);
        if (index < 0 || toolMakingRequests[index] == request) return;
        toolMakingRequests[index] = request with { LastTransitionTick = WorldTick };
        if ((ToolMakingRequestRules.IsTerminal(request.Status) || request.Status == ToolMakingRequestStatus.Ready) &&
            request.WorkerId is { } worker && inhabitants.TryGetValue(worker, out var person) &&
            person.Project?.ToolMakingRequestId == request.Id)
            inhabitants[worker] = person with { Project = person.Project with { ToolMakingRequestId = null } };
        checkpointSchemaVersion = StateSchemaVersion;
        if (eventKind is not null) AppendEvent(eventKind, (actor ?? request.RequesterId) + ":" + request.Id);
    }

    private string? ToolMakingInputBlocker(ToolMakingRequestState request, SettlementProject project)
    {
        var recipe = worldContent.Recipes.First(item => item.CanonicalId == request.RecipeId);
        var missing = recipe.Inputs.Where(input => society.Checkpoint.Inventory.Lots.Where(lot =>
                lot.OwnerId == request.SellerHouseholdId && lot.StorageBuildingId == request.BuildingInstanceId &&
                lot.ItemKind == input.ResourceId && lot.FreshnessBasisPoints > 0 && lot.ConditionBasisPoints > 0)
            .Sum(lot => (long)AvailableLotQuantity(lot)) < input.Amount).Select(input => input.ResourceId.Replace('_', ' ')).ToArray();
        if (missing.Length == 0) return project.Blocker;
        return StorageRoomAfterInboundDeliveries(request.BuildingInstanceId) == 0
            ? "Waiting for room at the Blacksmith before more materials can arrive."
            : "Waiting for household materials at the Blacksmith: " + string.Join(", ", missing) + ".";
    }

    private void MaintainToolMakingRequests()
    {
        foreach (var original in toolMakingRequests.ToArray())
        {
            var request = original;
            if (ToolMakingRequestRules.IsTerminal(request.Status)) continue;
            if (!inhabitants.ContainsKey(request.RequesterId) || !AdultResident(request.RequesterId) || ToolRequestShop(request) is null ||
                !worldContent.Recipes.Any(recipe => recipe.CanonicalId == request.RecipeId && ToolMakingRequestRules.IsToolRecipe(recipe)))
            {
                CloseToolRequestOffer(request, "The requester or shop is no longer available.");
                SetToolRequest(request with { Status = ToolMakingRequestStatus.Interrupted, Blocker = "The requester or shop is no longer available." }, "tool_request_interrupted");
                continue;
            }
            if (request.Status == ToolMakingRequestStatus.Accepted)
            {
                if (request.WorkerId is not { } worker || !IsToolRequestSeller(worker, request))
                {
                    SetToolRequest(request with { Status = ToolMakingRequestStatus.Interrupted, Blocker = "The worker no longer has access to this shop." }, "tool_request_interrupted");
                    continue;
                }
                var project = inhabitants[worker].Project;
                if (request.JobId is null && project?.ToolMakingRequestId == request.Id)
                    request = request with { JobId = project.JobId, Blocker = project.JobId is null
                        ? ToolMakingInputBlocker(request, project) : project.Blocker };
                if (request.JobId is { } jobId)
                {
                    var job = worldSimulation.ProductionJobs.FirstOrDefault(job => job.JobId == jobId);
                    if (job is { State: WorldProductionJobState.Completed })
                        SetToolRequest(request with { Status = ToolMakingRequestStatus.Ready, Blocker = null }, "tool_request_ready");
                    else if (job is null or { State: WorldProductionJobState.Cancelled })
                        SetToolRequest(request with { Status = ToolMakingRequestStatus.Interrupted, Blocker = "Production was removed or cancelled." }, "tool_request_interrupted");
                    else SetToolRequest(request);
                }
                else if (project?.ToolMakingRequestId != request.Id || project.Stage is "completed" or "cancelled")
                    SetToolRequest(request with { Status = ToolMakingRequestStatus.Interrupted, Blocker = "The worker chose another task." }, "tool_request_interrupted");
                else SetToolRequest(request);
            }
            else if (request.Status == ToolMakingRequestStatus.Ready)
            {
                var linkedTrade = businessTrades.FirstOrDefault(trade => trade.BuyerId == request.RequesterId &&
                    trade.BuildingInstanceId == request.BuildingInstanceId && society.Checkpoint.Inventory.Offers.Any(offer =>
                        offer.Id == trade.OfferId && offer.FirstLotId == request.JobId + ":output:00" &&
                        offer.State is DirectBarterState.Open or DirectBarterState.Settled));
                if (linkedTrade is not null)
                {
                    var offer = society.Checkpoint.Inventory.GetOffer(linkedTrade.OfferId);
                    SetToolRequest(request with { OfferId = offer.Id, Status = offer.State == DirectBarterState.Settled
                        ? ToolMakingRequestStatus.Fulfilled : ToolMakingRequestStatus.Offered },
                        offer.State == DirectBarterState.Settled ? "tool_request_completed" : null);
                    continue;
                }
                var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == request.JobId + ":output:00");
                if (lot is null || lot.OwnerId != request.SellerHouseholdId || lot.StorageBuildingId != request.BuildingInstanceId ||
                    !IsLooseBusinessLot(lot) || AvailableLotQuantity(lot) < 1)
                    SetToolRequest(request with { Status = ToolMakingRequestStatus.Interrupted, Blocker = "The household's finished tool is no longer available." }, "tool_request_interrupted");
            }
            else if (request.Status == ToolMakingRequestStatus.Offered)
            {
                var offer = society.Checkpoint.Inventory.Offers.FirstOrDefault(offer => offer.Id == request.OfferId);
                if (offer is { State: DirectBarterState.Settled })
                    SetToolRequest(request with { Status = ToolMakingRequestStatus.Fulfilled }, "tool_request_completed");
                else if (offer is null or { State: DirectBarterState.Cancelled })
                    SetToolRequest(request with { Status = ToolMakingRequestStatus.Interrupted, Blocker = "The exchange was cancelled; goods retain their owners." }, "tool_request_interrupted");
            }
        }
        var retired = toolMakingRequests.Where(request => ToolMakingRequestRules.IsTerminal(request.Status))
            .OrderByDescending(request => request.LastTransitionTick).ThenBy(request => request.Id, StringComparer.Ordinal)
            .Skip(ToolMakingRequestRules.MaximumTerminalRequests).Select(request => request.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var request in toolMakingRequests.Where(request => ToolMakingRequestRules.IsTerminal(request.Status) &&
            request.OfferId is { } offer && !society.Checkpoint.Inventory.Offers.Any(item => item.Id == offer))) retired.Add(request.Id);
        if (retired.Count > 0)
        {
            toolMakingRequests.RemoveAll(request => retired.Contains(request.Id));
            worldSimulation = worldSimulation with { ProductionJobs = worldSimulation.ProductionJobs.Select(job =>
                job.ToolMakingRequestId is { } id && retired.Contains(id) ? job with { ToolMakingRequestId = null } : job).ToArray() };
        }
    }
}
