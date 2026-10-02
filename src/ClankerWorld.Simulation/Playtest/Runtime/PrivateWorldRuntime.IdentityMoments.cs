using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static readonly TimeSpan IdentityMomentTimeout = TimeSpan.FromSeconds(15);
    private readonly Dictionary<string, PendingIdentityMoment> pendingIdentityMoments = new(StringComparer.Ordinal);
    private sealed record IdentityMomentOutcome(CognitionDecisionResponse? Response);
    private sealed record PendingIdentityMoment(AgentIdentityMomentKind Kind,
        CognitionDecisionRequest Request, Task<IdentityMomentOutcome> Task, CancellationTokenSource Cancellation);

    private void DiscoverIdentityMoments()
    {
        var checkpoint = society.Checkpoint;
        foreach (var person in checkpoint.Inhabitants.Where(item => item.Status == SocietyInhabitantStatus.Active))
        {
            var physical = inhabitants[person.Id];
            var kinds = new List<AgentIdentityMomentKind>();
            var age = checkpoint.AgeAt(person, WorldTick);
            if (checkpoint.Config.DayLifecycle is { } days && age >= days.MaximumDay / 2)
                kinds.Add(AgentIdentityMomentKind.Midlife);
            if (person.AgeBand == SocietyAgeBand.Elder)
                kinds.Add(AgentIdentityMomentKind.Elder);
            if (checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.BiologicalParentage &&
                edge.ProposerId == person.Id))
                kinds.Add(AgentIdentityMomentKind.Parenthood);
            if (checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Partnership &&
                edge.State == SocietyRelationshipState.EndedByDeath &&
                (edge.ProposerId == person.Id || edge.TargetId == person.Id)))
                kinds.Add(AgentIdentityMomentKind.PartnerLoss);
            if (checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.BiologicalParentage &&
                edge.TargetId == person.Id && checkpoint.GetInhabitant(edge.ProposerId).Status == SocietyInhabitantStatus.Dead))
                kinds.Add(AgentIdentityMomentKind.ParentLoss);
            var existing = physical.IdentityMoments ?? [];
            var added = kinds.Where(kind => !existing.Any(moment => moment.Kind == kind))
                .Select(kind => new AgentIdentityMoment(kind, WorldTick)).ToArray();
            if (added.Length == 0) continue;
            inhabitants[person.Id] = physical with { IdentityMoments = existing.Concat(added).ToArray() };
            checkpointSchemaVersion = StateSchemaVersion;
        }
    }

    private static bool HasWaitingIdentityMoment(PlaytestInhabitantState physical) =>
        !physical.IdentityChoicePending && (physical.IdentityMoments ?? [])
            .Any(moment => moment.Outcome is "waiting" or "requested");

    private void StartIdentityMoments()
    {
        // Bounded separately from ordinary decisions. The installation usage
        // store still reserves each potentially paid request before HTTP.
        var capacity = Math.Max(0, maxCognitionDispatchPerCycle - pendingIdentityMoments.Count);
        foreach (var person in society.Checkpoint.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active && item.AgeBand != SocietyAgeBand.Infant)
            .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (capacity == 0) break;
            var physical = inhabitants[person.Id];
            if (physical.IdentityChoicePending || pendingIdentityMoments.ContainsKey(person.Id) ||
                pendingHosted.ContainsKey(person.Id) || IsConversationBusy(person.Id) ||
                NeedsUrgentFood(physical) || NeedsUrgentWarmth(physical)) continue;
            var moment = (physical.IdentityMoments ?? []).FirstOrDefault(item => item.Outcome == "waiting");
            if (moment is null) continue;
            try
            {
                var observation = CreateIdentityMomentObservation(person, physical, moment);
                var provider = providerFactory?.Invoke(person.Id) ?? new DeterministicDecisionProvider();
                if (provider.KindFor(observation) != DecisionProviderKind.LargeLanguageModel)
                {
                    FinishIdentityMoment(person.Id, moment.Kind, "kept");
                    continue;
                }
                var request = new CognitionDecisionRequest(
                    $"identity:{Guid.NewGuid():N}", provider.ProviderEpoch, observation);
                request.Validate();
                SetIdentityMoment(person.Id, moment with { Outcome = "requested", RequestedTick = WorldTick });
                var cancellation = new CancellationTokenSource(IdentityMomentTimeout);
                var task = Task.Run(async () =>
                {
                    try
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        return new IdentityMomentOutcome(await provider.DecideAsync(request, cancellation.Token)
                            .AsTask().WaitAsync(cancellation.Token).ConfigureAwait(false));
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        // Neither provider replies nor their exception messages
                        // enter events or logs.
                        return new IdentityMomentOutcome(null);
                    }
                });
                pendingIdentityMoments.Add(person.Id, new(moment.Kind, request, task, cancellation));
                AppendEvent("agent_identity_moment_started", $"{person.Id}:{moment.Kind}");
                capacity--;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                FinishIdentityMoment(person.Id, moment.Kind, "kept");
            }
        }
    }

    private InhabitantObservation CreateIdentityMomentObservation(
        SocietyInhabitant person, PlaytestInhabitantState physical, AgentIdentityMoment moment)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{person.Id}|{moment.Kind}|{moment.TriggeredTick}|{physical.Personality}|{physical.Aspiration}")));
        return new InhabitantObservation(person.Id, WorldTick, society.Checkpoint.RunEpoch,
            moment.TriggeredTick, digest, physical.HungerBasisPoints,
            [new("identity_optional", "Keep your current identity or choose an optional change for this life moment.")],
            RequiresPersonalProvider: true,
            Self: new(person.Id, person.Name, person.AgeBand.ToString(), physical.Personality, physical.Aspiration,
                person.HouseholdId, null, null, null),
            NeedsPersonality: true, NeedsAspiration: true)
        { IdentityMoment = moment.Reason };
    }

    private void CompleteIdentityMoments(IReadOnlyList<PendingIdentityMoment> completed,
        Func<PendingIdentityMoment, bool> providerIsCurrent)
    {
        foreach (var pending in completed)
        {
            var id = pending.Request.Observation.InhabitantId;
            if (!inhabitants.TryGetValue(id, out var physical) ||
                society.Checkpoint.GetInhabitant(id).Status != SocietyInhabitantStatus.Active ||
                !(physical.IdentityMoments ?? []).Any(moment => moment.Kind == pending.Kind && moment.Outcome == "requested"))
                continue;
            if (society.Checkpoint.RunEpoch != pending.Request.Observation.RunEpoch ||
                physical.Personality != pending.Request.Observation.Self!.Personality ||
                physical.Aspiration != pending.Request.Observation.Self.Aspiration || !providerIsCurrent(pending))
            {
                FinishIdentityMoment(id, pending.Kind, "interrupted");
                continue;
            }
            var response = pending.Task.GetAwaiter().GetResult().Response;
            var valid = false;
            try
            {
                response?.Validate();
                valid = response is not null && response.RequestId == pending.Request.RequestId &&
                    response.InhabitantId == id && response.Provider == DecisionProviderKind.LargeLanguageModel &&
                    response.ProviderEpoch == pending.Request.ProviderEpoch &&
                    response.RunEpoch == pending.Request.Observation.RunEpoch &&
                    response.DecisionGeneration == pending.Request.Observation.DecisionGeneration &&
                    response.ObservationDigest == pending.Request.Observation.ObservationDigest &&
                    response.SelectedCandidateId == "identity_optional";
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
            var personality = valid ? response!.ChosenPersonality : null;
            var aspiration = valid ? response!.ChosenAspiration : null;
            if (personality == physical.Personality) personality = null;
            if (aspiration == physical.Aspiration) aspiration = null;
            var accepted = personality is not null || aspiration is not null;
            inhabitants[id] = physical with
            {
                Personality = personality ?? physical.Personality,
                Aspiration = aspiration ?? physical.Aspiration,
                LastDecisionContext = accepted ? null : physical.LastDecisionContext,
            };
            FinishIdentityMoment(id, pending.Kind, accepted ? "accepted" : "kept", personality, aspiration);
        }
    }

    private bool IsIdentityMomentProviderCurrent(PendingIdentityMoment pending)
    {
        try
        {
            var provider = providerFactory?.Invoke(pending.Request.Observation.InhabitantId);
            return provider is not null && provider.KindFor(pending.Request.Observation) == DecisionProviderKind.LargeLanguageModel &&
                provider.ProviderEpoch == pending.Request.ProviderEpoch;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    private void FinishIdentityMoment(string id, AgentIdentityMomentKind kind, string outcome,
        string? personality = null, string? aspiration = null)
    {
        var moment = (inhabitants[id].IdentityMoments ?? []).Single(item => item.Kind == kind);
        SetIdentityMoment(id, moment with
        {
            Outcome = outcome,
            CompletedTick = WorldTick,
            Personality = personality,
            Aspiration = aspiration,
        });
        AppendEvent(outcome == "accepted" ? "agent_identity_revised" : "agent_identity_moment_kept", $"{id}:{kind}");
    }

    private void SetIdentityMoment(string id, AgentIdentityMoment moment)
    {
        var physical = inhabitants[id];
        inhabitants[id] = physical with
        {
            IdentityMoments = (physical.IdentityMoments ?? []).Select(item => item.Kind == moment.Kind ? moment : item).ToArray(),
        };
        checkpointSchemaVersion = StateSchemaVersion;
    }

    private void CancelIdentityMoments()
    {
        foreach (var id in pendingIdentityMoments.Keys.ToArray()) CancelIdentityMoment(id);
    }

    private void CancelIdentityMoment(string id)
    {
        if (!pendingIdentityMoments.Remove(id, out var pending)) return;
        if (inhabitants.TryGetValue(id, out var physical) &&
            (physical.IdentityMoments ?? []).Any(moment => moment.Kind == pending.Kind && moment.Outcome == "requested"))
            FinishIdentityMoment(id, pending.Kind, "interrupted");
        pending.Cancellation.Cancel();
        _ = pending.Task.ContinueWith(_ => pending.Cancellation.Dispose(), TaskScheduler.Default);
    }

    private void ReconcileIdentityMoments()
    {
        foreach (var (id, pending) in pendingIdentityMoments.ToArray())
            if (!inhabitants.ContainsKey(id) || society.Checkpoint.RunEpoch != pending.Request.Observation.RunEpoch ||
                !IsIdentityMomentProviderCurrent(pending))
                CancelIdentityMoment(id);
        // A saved request has no live provider task after restore. Its single
        // opportunity remains consumed; returning to the game never retries it.
        foreach (var physical in inhabitants.Values.ToArray())
            foreach (var moment in (physical.IdentityMoments ?? []).Where(item => item.Outcome == "requested"))
                if (!pendingIdentityMoments.ContainsKey(physical.InhabitantId))
                    FinishIdentityMoment(physical.InhabitantId, moment.Kind, "interrupted");
    }
}
