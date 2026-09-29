using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private void CancelUnavailableWorkers()
    {
        var active = society.Checkpoint.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active)
            .Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var cancelled = worldSimulation.ProductionJobs.Concat(worldSimulation.CropBuilds ?? [])
            .Where(job => job.State == WorldProductionJobState.Running && !active.Contains(job.WorkerId))
            .OrderBy(job => job.JobId, StringComparer.Ordinal).ToArray();
        if (cancelled.Length == 0) return;
        ApplyInventoryTransition(inventory =>
        {
            foreach (var job in cancelled)
            {
                foreach (var id in job.InputReservationIds.Order(StringComparer.Ordinal))
                {
                    if (inventory.GetReservation(id).State is InventoryReservationState.Reserved or InventoryReservationState.PartiallyConsumed)
                        inventory = InventoryFixture.ReleaseReservation(inventory, id, "production_worker_unavailable");
                }
            }
            return inventory;
        });
        var ids = cancelled.Select(job => job.JobId).ToHashSet(StringComparer.Ordinal);
        worldSimulation = worldSimulation with
        {
            ProductionJobs = worldSimulation.ProductionJobs.Select(job => ids.Contains(job.JobId)
                ? job with { State = WorldProductionJobState.Cancelled } : job).ToArray(),
            CropBuilds = worldSimulation.CropBuilds?.Select(job => ids.Contains(job.JobId)
                ? job with { State = WorldProductionJobState.Cancelled } : job).ToArray(),
        };
        foreach (var job in cancelled) AppendEvent("production_worker_unavailable", job.JobId);
    }
}
