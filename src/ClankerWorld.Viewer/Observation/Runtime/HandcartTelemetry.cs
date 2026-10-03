using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;

namespace ClankerWorld.Viewer.Observation;

/// <summary>Committed cart milestones; names, private text and per-step movement stay out of these logs.</summary>
public static partial class HandcartTelemetry
{
    public static void Record(ILogger logger, PlaytestWorldEvent worldEvent, InventoryCheckpoint inventory,
        IReadOnlyList<string> actorIds)
    {
        if (worldEvent.Kind is not ("handcart_attached" or "handcart_parked" or "handcart_loaded" or
            "handcart_unloaded" or "handcart_repaired" or "handcart_transferred" or "handcart_blocked")) return;
        var actor = actorIds.OrderByDescending(id => id.Length).FirstOrDefault(id =>
            worldEvent.Detail.StartsWith(id + ":", StringComparison.Ordinal));
        if (actor is null) return;
        var cart = inventory.Lots.Where(lot => lot.ItemKind == InventoryContainerRules.Handcart)
            .OrderByDescending(lot => lot.Id.Length).FirstOrDefault(lot =>
                worldEvent.Detail.StartsWith(actor + ":" + lot.Id + ":", StringComparison.Ordinal) ||
                worldEvent.Detail == actor + ":" + lot.Id);
        var cargo = cart is null ? 0 : inventory.Lots.Where(lot => lot.ContainerLotId == cart.Id).Sum(lot => lot.Quantity);
        LogMilestone(logger, worldEvent.WorldTick, worldEvent.Kind, actor, cart?.Id ?? "none",
            cart?.ConditionBasisPoints / 100 ?? 0, cargo);
    }

    [LoggerMessage(EventId = 2260, Level = LogLevel.Information,
        Message = "handcart_activity tick={WorldTick} event={EventKind} inhabitant={InhabitantId} cart={CartId} condition={ConditionPercent} cargo={CargoQuantity}")]
    private static partial void LogMilestone(ILogger logger, long worldTick, string eventKind,
        string inhabitantId, string cartId, int conditionPercent, int cargoQuantity);
}
