using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    public BusinessActionResult RequestBusinessTool(string buyerId, string buildingId, string toolKind) =>
        BusinessAction(() => RequestBusinessToolCore(buyerId, buildingId, toolKind));

    private BusinessActionResult RequestBusinessToolCore(string buyerId, string buildingId, string toolKind)
    {
        var site = BusinessSite(buildingId);
        var recipe = worldContent.Recipes.FirstOrDefault(recipe => recipe.WorkstationBuildingId == site?.DefinitionId &&
            recipe.Outputs.Any(output => output.ResourceId == toolKind) && ToolCapabilities.ForItem(toolKind) is not null);
        if (site?.HouseholdId is not { } householdId || !AdultResident(buyerId) || HouseholdFor(buyerId) == householdId ||
            recipe is null || !IsWithinInteractionRange(inhabitants[buyerId].Position, site.Position, ResourceInteractionRange) ||
            businessTrade.ToolOrders.Any(order => order.BuyerId == buyerId && order.State is "queued" or "running" or "ready"))
            return new(false, Failure: "A customer at the Blacksmith may request one supported tool at a time.");
        var id = "business-tool-order:" + businessTrade.NextSequence;
        var order = new BusinessToolOrder(id, buyerId, householdId, site.InstanceId, recipe.CanonicalId, toolKind,
            WorldTick + 600, "queued", Blocker: ToolOrderBlocker(site, recipe));
        businessTrade = businessTrade with
        {
            NextSequence = businessTrade.NextSequence + 1,
            ToolOrders = businessTrade.ToolOrders.Append(order).ToArray(),
        };
        AppendEvent("business_tool_requested", $"{buyerId}|{site.InstanceId}|{toolKind}|{order.Blocker ?? "ready_to_make"}");
        return new(true, id);
    }

    private bool HasBusinessToolDemand(RecipeDefinition recipe, string? owner) => owner is not null &&
        businessTrade.ToolOrders.Any(order => order.HouseholdId == owner && order.RecipeId == recipe.CanonicalId &&
            order.ExpiryTick >= WorldTick && order.State == "queued");

    private string? ToolOrderBlocker(PlacedBuilding site, RecipeDefinition recipe)
    {
        if (!HasIngredientsAtBuilding(recipe.Inputs, site.HouseholdId!, site.InstanceId))
            return BusinessStorageRoom(site.InstanceId) == 0
                ? "Missing actual inputs; Blacksmith storage is full for deliveries."
                : "Missing actual inputs at the Blacksmith.";
        if (BusinessStorageRoom(site.InstanceId) < Math.Max(0, recipe.Outputs.Sum(output => output.Amount) -
                recipe.Inputs.Sum(input => input.Amount))) return "Blacksmith storage is full.";
        if (worldSimulation.ProductionJobs.Any(job => job.BuildingInstanceId == site.InstanceId &&
                job.State == WorldProductionJobState.Running)) return "Waiting for the Blacksmith work slot.";
        return null;
    }

    private void MaintainBusinessToolOrders()
    {
        foreach (var order in businessTrade.ToolOrders.Where(order => order.State is "queued" or "running" or "ready").ToArray())
        {
            var site = BusinessSite(order.BuildingId);
            var recipe = worldContent.Recipes.FirstOrDefault(recipe => recipe.CanonicalId == order.RecipeId);
            if (!AdultResident(order.BuyerId) || site?.HouseholdId != order.HouseholdId || recipe is null || order.ExpiryTick < WorldTick)
            {
                SetBusinessToolOrder(order with { State = "cancelled", Blocker = "The request expired or its customer or business became unavailable." });
                continue;
            }
            if (society.Checkpoint.Inventory.Lots.Any(lot => lot.OwnerId == order.BuyerId &&
                    lot.ItemKind == order.ToolKind && lot.ConditionBasisPoints > 0 && lot.StorageBuildingId is null &&
                    lot.DeliveryBuildingId is null && lot.ContainerLotId is null && lot.GroundPosition is null))
            {
                SetBusinessToolOrder(order with { State = "completed", Blocker = null });
                continue;
            }
            if (order.State == "queued" && society.Checkpoint.Inventory.Lots.Any(lot =>
                    lot.OwnerId == order.HouseholdId && lot.StorageBuildingId == order.BuildingId &&
                    lot.ItemKind == order.ToolKind && AvailableLotQuantity(lot) > 0))
            {
                SetBusinessToolOrder(order with { State = "ready", Blocker = "Tool stocked at the Blacksmith; exact barter still required." });
                continue;
            }
            if (order.State == "running")
            {
                var job = worldSimulation.ProductionJobs.FirstOrDefault(job => job.JobId == order.JobId);
                if (job?.State == WorldProductionJobState.Completed)
                    SetBusinessToolOrder(order with { State = "ready", Blocker = "Tool stocked at the Blacksmith; exact barter still required." });
                else if (job is null || job.State == WorldProductionJobState.Cancelled)
                    SetBusinessToolOrder(order with { State = "queued", JobId = null, Blocker = "The work was interrupted; its actual remaining inputs are needed again." });
            }
            else if (order.State == "ready" && !society.Checkpoint.Inventory.Lots.Any(lot =>
                         lot.OwnerId == order.HouseholdId && lot.StorageBuildingId == order.BuildingId &&
                         lot.ItemKind == order.ToolKind && AvailableLotQuantity(lot) > 0))
                SetBusinessToolOrder(order with { State = "queued", JobId = null, Blocker = ToolOrderBlocker(site!, recipe) });
            else if (order.State == "queued")
                SetBusinessToolOrder(order with { Blocker = ToolOrderBlocker(site!, recipe) });
        }
        businessTrade = businessTrade with
        {
            ToolOrders = businessTrade.ToolOrders.Where(order => order.State is "queued" or "running" or "ready")
                .Concat(businessTrade.ToolOrders.Where(order => order.State is "completed" or "cancelled").TakeLast(32)).ToArray(),
        };
    }

    private void SetBusinessToolOrder(BusinessToolOrder updated) => businessTrade = businessTrade with
    {
        ToolOrders = businessTrade.ToolOrders.Select(order => order.Id == updated.Id ? updated : order).ToArray(),
    };

    private void AddBusinessToolCandidates(List<CognitionCandidate> candidates, string actor)
    {
        foreach (var order in businessTrade.ToolOrders.Where(order => order.HouseholdId == HouseholdFor(actor) &&
                     order.State == "queued" && order.Blocker is null))
            candidates.Add(new("business_make_tool:" + order.Id,
                "Make the customer's requested tool from actual stocked Blacksmith inputs.", 25, order.BuildingId));
        if (businessTrade.ToolOrders.Any(order => order.BuyerId == actor && order.State is "queued" or "running" or "ready")) return;
        var wanted = HouseholdFor(actor) is { } householdId && FarmhouseForHousehold(householdId) is not null
            ? ToolKind.Hoe : inhabitants[actor].Project is { Stage: not ("completed" or "cancelled") } ? ToolKind.Hammer : ToolKind.Axe;
        if (CarriedTool(actor, wanted) is not null || SharedTool(actor, wanted) is not null) return;
        foreach (var site in worldSimulation.Buildings.Where(site => site.HouseholdId is not null &&
                     site.HouseholdId != HouseholdFor(actor) && worldContent.Buildings.Any(definition =>
                         definition.CanonicalId == site.DefinitionId && definition.Tags.Contains("blacksmith", StringComparer.Ordinal))))
        {
            var recipe = worldContent.Recipes.Where(recipe => recipe.WorkstationBuildingId == site.DefinitionId &&
                    recipe.Outputs.Any(output => ToolCapabilities.ForItem(output.ResourceId)?.Kind == wanted))
                .OrderBy(recipe => recipe.Outputs.Where(output => ToolCapabilities.ForItem(output.ResourceId) is not null)
                    .Min(output => ToolCapabilities.ForItem(output.ResourceId)!.Tier)).FirstOrDefault();
            if (recipe is null) continue;
            var item = recipe.Outputs.First(output => ToolCapabilities.ForItem(output.ResourceId)?.Kind == wanted).ResourceId;
            candidates.Add(new("business_request_tool:" + site.InstanceId + "|" + item,
                $"Visit the Blacksmith and request a {item.Replace('_', ' ')}; missing inputs and storage are reported.", 33, site.InstanceId));
        }
    }

    private bool ApplyBusinessToolCandidate(string actor, PlaytestInhabitantState state, string candidate)
    {
        if (candidate.StartsWith("business_request_tool:", StringComparison.Ordinal))
        {
            var terms = candidate[22..].Split('|', 2);
            if (terms.Length != 2 || BusinessSite(terms[0]) is not { } site) return true;
            if (!IsWithinInteractionRange(state.Position, site.Position, ResourceInteractionRange))
                MoveToward(actor, state, site.Position, "tool_request", ResourceInteractionRange);
            else RequestBusinessToolCore(actor, site.InstanceId, terms[1]);
            return true;
        }
        if (!candidate.StartsWith("business_make_tool:", StringComparison.Ordinal)) return false;
        var order = businessTrade.ToolOrders.FirstOrDefault(order => order.Id == candidate[19..] &&
            order.HouseholdId == HouseholdFor(actor) && order.State == "queued");
        if (order is null || BusinessSite(order.BuildingId) is not { } blacksmith) return true;
        if (state.Position != blacksmith.Position) MoveToward(actor, state, blacksmith.Position, "tool_order_work", 0);
        else
        {
            var result = StartProductionCore(order.RecipeId, order.BuildingId, actor, "business_tool_started");
            SetBusinessToolOrder(result.Applied ? order with { State = "running", JobId = result.JobId, Blocker = null }
                : order with { Blocker = result.Failure });
        }
        return true;
    }
}
