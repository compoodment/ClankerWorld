using ClankerWorld.Simulation.Content;

namespace ClankerWorld.Simulation.Playtest;

public enum ToolMakingRequestStatus
{
    Requested, Accepted, Ready, Offered, Fulfilled, Refused, Withdrawn, Interrupted,
}

/// <summary>Demand for ordinary household production; inventory remains the sole stock and payment authority.</summary>
public sealed record ToolMakingRequestState(
    string Id, string RequesterId, string SellerHouseholdId, string BuildingInstanceId,
    string RecipeId, string ItemKind, long RequestedTick, long LastTransitionTick,
    ToolMakingRequestStatus Status = ToolMakingRequestStatus.Requested,
    string? WorkerId = null, long? AcceptedTick = null,
    string? JobId = null, string? OfferId = null, string? Blocker = null);

public static class ToolMakingRequestRules
{
    public const int MaximumActiveRequests = 32;
    public const int MaximumTerminalRequests = 32;

    public static bool IsTerminal(ToolMakingRequestStatus status) => status is
        ToolMakingRequestStatus.Fulfilled or ToolMakingRequestStatus.Refused or
        ToolMakingRequestStatus.Withdrawn or ToolMakingRequestStatus.Interrupted;

    public static string Note(ToolMakingRequestState request)
    {
        var item = request.ItemKind.Replace('_', ' ');
        var text = request.Status switch
        {
            ToolMakingRequestStatus.Requested => $"Asked the Blacksmith household to make a {item}; no payment has been taken.",
            ToolMakingRequestStatus.Accepted => $"The household accepted the {item} request. It uses its own materials.",
            ToolMakingRequestStatus.Ready => $"The {item} was made. Bring payment to the shop to propose an exchange.",
            ToolMakingRequestStatus.Offered => $"An exchange for the actual {item} is pending. Both traders must meet at the shop.",
            ToolMakingRequestStatus.Fulfilled => $"The {item} was bought through the agreed exchange.",
            ToolMakingRequestStatus.Refused => $"The household refused the {item} request; nothing was taken.",
            ToolMakingRequestStatus.Withdrawn => $"The {item} request was withdrawn. Any work or finished tool remains household property.",
            _ => $"The {item} request was interrupted. Household materials and goods keep their owners.",
        };
        return request.Blocker is null ? text : (text + " " + request.Blocker)[..Math.Min(256, text.Length + 1 + request.Blocker.Length)];
    }

    public static string? Note(IEnumerable<ToolMakingRequestState> requests, string actor, string? household)
    {
        var selected = requests.Where(request => request.RequesterId == actor || request.SellerHouseholdId == household)
            .OrderBy(request => IsTerminal(request.Status)).ThenByDescending(request => request.LastTransitionTick)
            .ThenBy(request => request.Id, StringComparer.Ordinal).Take(3).Select(Note).ToArray();
        if (selected.Length == 0) return null;
        var text = string.Join(" ", selected);
        return text[..Math.Min(256, text.Length)];
    }

    public static bool IsToolRecipe(RecipeDefinition recipe) => !recipe.IsCrop &&
        recipe.WorkstationBuildingId is not null && recipe.Outputs.Count == 1 &&
        recipe.Outputs[0].Amount == 1 && ToolProgressionRules.Find(recipe.Outputs[0].ResourceId) is not null;
}
