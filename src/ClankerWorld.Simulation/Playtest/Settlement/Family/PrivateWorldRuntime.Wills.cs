using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int WillDecisionDeadlineTicks = 30;
    private static readonly TimeSpan WillDecisionTimeout = TimeSpan.FromSeconds(15);
    private readonly Dictionary<string, PendingWillDecision> pendingWills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> pendingWillCancellationReasons = new(StringComparer.Ordinal);

    private sealed record WillDecisionOutcome(CognitionDecisionResponse? Response, string Failure);
    private sealed record PendingWillDecision(
        string EstateId,
        CognitionDecisionRequest Request,
        IReadOnlySet<string> CandidateIds,
        Task<WillDecisionOutcome> Task,
        CancellationTokenSource Cancellation);

    private void StartWillDecisions()
    {
        foreach (var estate in society.Checkpoint.Estates.Where(item => !item.Settled && item.WillStatus is null)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            society.Apply(checkpoint => SocietyFixture.MarkWillStarted(checkpoint, estate.Id));
            if (estate.FrozenLots is not { Count: > 0 })
            {
                ResolveWillDefault(estate.Id, "empty_estate");
                continue;
            }

            try
            {
                var observation = CreateWillObservation(estate);
                var provider = providerFactory?.Invoke(estate.DeceasedId) ?? new DeterministicDecisionProvider();
                if (provider.KindFor(observation) != DecisionProviderKind.LargeLanguageModel)
                {
                    ResolveWillDefault(estate.Id, "no_personal_model");
                    continue;
                }

                var request = new CognitionDecisionRequest(
                    $"will:{estate.Id}", provider.ProviderEpoch, observation);
                request.Validate();
                var cancellation = new CancellationTokenSource(WillDecisionTimeout);
                var task = Task.Run(async () =>
                {
                    try
                    {
                        return new WillDecisionOutcome(
                            await provider.DecideAsync(request, cancellation.Token).ConfigureAwait(false), "none");
                    }
                    catch (OperationCanceledException)
                    {
                        return new WillDecisionOutcome(null, "timeout_or_cancelled");
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        return new WillDecisionOutcome(null, $"provider_{exception.GetType().Name}");
                    }
                });
                pendingWills.Add(estate.Id, new PendingWillDecision(estate.Id, request,
                    observation.Candidates.Select(item => item.Id).ToHashSet(StringComparer.Ordinal),
                    task, cancellation));
                AppendEvent("estate_will_started", estate.Id);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ResolveWillDefault(estate.Id, $"setup_{exception.GetType().Name}");
            }
        }
    }

    private InhabitantObservation CreateWillObservation(SocietyEstate estate)
    {
        var snapshot = estate.FrozenLots ?? [];
        var summary = string.Join(", ", snapshot.Take(16)
            .Select(item => $"{item.Quantity} {item.ItemKind} (lot {item.LotId})"));
        var candidates = new List<CognitionCandidate>
        {
            new("will:household", $"You have died. Your frozen personal estate is {summary}. Leave it on the household default inheritance path."),
        };
        foreach (var person in society.Checkpoint.Inhabitants
                     .Where(item => item.Status == SocietyInhabitantStatus.Active && item.Id != estate.DeceasedId)
                     .OrderBy(item => item.Id, StringComparer.Ordinal).Take(16))
        {
            candidates.Add(new CognitionCandidate($"will:heir:{person.Id}",
                $"You have died. Direct your frozen personal estate ({summary}) to {person.Name} ({person.Id})."));
        }
        var digestInput = estate.Id + "|" + string.Join("|", snapshot.Select(item =>
            $"{item.LotId}:{item.ItemKind}:{item.Quantity}")) + "|" + string.Join("|", candidates.Select(item => item.Id));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestInput)));
        return new InhabitantObservation(estate.DeceasedId, estate.CreatedTick,
            society.Checkpoint.RunEpoch, 0, digest, 0, candidates, RequiresPersonalProvider: true);
    }

    private async ValueTask ProcessWillDecisionsAsync(
        IReadOnlyList<PendingWillDecision> completed, IReadOnlyList<string> activeIds,
        IReadOnlyDictionary<string, string> inactiveReasons)
    {
        var completedById = completed.ToDictionary(item => item.EstateId, StringComparer.Ordinal);
        var active = activeIds.ToHashSet(StringComparer.Ordinal);
        foreach (var estate in society.Checkpoint.Estates.Where(item => item.WillStatus == "pending" && !item.Settled)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (WorldTick >= estate.CreatedTick + WillDecisionDeadlineTicks)
            {
                ResolveWillDefault(estate.Id, "deadline");
                continue;
            }

            if (completedById.TryGetValue(estate.Id, out var pending))
            {
                var outcome = await pending.Task.ConfigureAwait(false);
                var response = outcome.Response;
                var valid = false;
                if (response is not null)
                {
                    try
                    {
                        response.Validate();
                        valid = response.RequestId == pending.Request.RequestId &&
                            response.InhabitantId == estate.DeceasedId &&
                            response.Provider == DecisionProviderKind.LargeLanguageModel &&
                            response.ProviderEpoch == pending.Request.ProviderEpoch &&
                            (providerFactory?.Invoke(estate.DeceasedId).ProviderEpoch ?? 0) == pending.Request.ProviderEpoch &&
                            response.RunEpoch == pending.Request.Observation.RunEpoch &&
                            response.DecisionGeneration == pending.Request.Observation.DecisionGeneration &&
                            response.ObservationDigest == pending.Request.Observation.ObservationDigest &&
                            response.Confidence >= minimumCognitionConfidence &&
                            pending.CandidateIds.Contains(response.SelectedCandidateId);
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException) { }
                }
                if (!valid || response is null || response.SelectedCandidateId == "will:household")
                {
                    ResolveWillDefault(estate.Id, valid ? "household_selected" : outcome.Failure == "none" ? "invalid_response" : outcome.Failure);
                    continue;
                }
                var heirId = response.SelectedCandidateId["will:heir:".Length..];
                var result = society.Apply(checkpoint => SocietyFixture.ResolveWill(checkpoint, estate.Id, heirId, "accepted"));
                var accepted = result.Checkpoint.GetEstate(estate.Id).WillStatus == "accepted";
                AppendEvent(accepted ? "estate_will_accepted" : "estate_will_default",
                    accepted ? $"{estate.Id}:{heirId}" : $"{estate.Id}:invalid_estate_or_heir");
            }
            else if (!active.Contains(estate.Id))
            {
                ResolveWillDefault(estate.Id, inactiveReasons.GetValueOrDefault(estate.Id, "interrupted"));
            }
        }
    }

    private void ResolveWillDefault(string estateId, string reason)
    {
        society.Apply(checkpoint => SocietyFixture.ResolveWill(checkpoint, estateId, null, reason));
        AppendEvent("estate_will_default", $"{estateId}:{reason}");
    }

    private void CancelPendingWill(string estateId, string reason = "interrupted")
    {
        var estateIsPending = society.Checkpoint.Estates.Any(item => item.Id == estateId && item.WillStatus == "pending");
        if (!pendingWills.Remove(estateId, out var pending)) return;
        if (estateIsPending) pendingWillCancellationReasons[estateId] = reason;
        else pendingWillCancellationReasons.Remove(estateId);
        pending.Cancellation.Cancel();
        _ = pending.Task.ContinueWith(_ => pending.Cancellation.Dispose(), TaskScheduler.Default);
    }
}
