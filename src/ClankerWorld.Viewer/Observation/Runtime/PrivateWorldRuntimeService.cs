using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Control;

namespace ClankerWorld.Viewer.Observation;

/// <summary>
/// Advances the integrated private-world alpha at a deliberately readable
/// cadence. Hosted providers are not called once per render frame.
/// </summary>
public sealed partial class PrivateWorldRuntimeService(
    PrivateWorldRuntime runtime,
    PrivateWorldStateFile stateFile,
    OwnerClientPresenceLease clientPresence,
    ILogger<PrivateWorldRuntimeService>? logger = null,
    WorldAutosaveStore? autosave = null,
    ManualWorldSaveStore? manualSaves = null,
    ProviderConfigurationStore? providers = null) : BackgroundService
{
    private string? lastGateState;
    private bool recoveryWritePending;
    private bool invalidStateHalt;
    private bool writingCheckpoint;
    private readonly Dictionary<string, FrozenChildModelBinding> pendingChildModelBindings = new(StringComparer.Ordinal);

    [LoggerMessage(EventId = 2287, Level = LogLevel.Error,
        Message = "world_recovery outcome={Outcome} tick={WorldTick} reason={Reason}")]
    private static partial void LogRecovery(ILogger logger, string outcome, long worldTick, string reason);


    [LoggerMessage(EventId = 2215, Level = LogLevel.Information,
        Message = "work_practice tick={WorldTick} inhabitant={InhabitantId} building={Building} farming={Farming} crafting={Crafting}")]
    private static partial void LogWorkPractice(ILogger logger, long worldTick, string inhabitantId, int building, int farming, int crafting);

    [LoggerMessage(EventId = 2216, Level = LogLevel.Information,
        Message = "social_standing tick={WorldTick} inhabitant={InhabitantId} subject={SubjectId} trust={Trust} reason={Reason}")]
    private static partial void LogSocialStanding(ILogger logger, long worldTick, string inhabitantId, string subjectId, int trust, string reason);

    [LoggerMessage(EventId = 2271, Level = LogLevel.Information,
        Message = "tree_planting tick={WorldTick} inhabitant={InhabitantId} outcome={Outcome} detail={Detail}")]
    private static partial void LogTreePlanting(ILogger logger, long worldTick, string inhabitantId, string outcome, string detail);

    [LoggerMessage(EventId = 2218, Level = LogLevel.Information,
        Message = "hosted_decision tick={WorldTick} inhabitant={InhabitantId} outcome={Outcome}")]
    private static partial void LogHostedDecision(ILogger logger, long worldTick, string inhabitantId, string outcome);

    [LoggerMessage(EventId = 2255, Level = LogLevel.Information,
        Message = "estate_will tick={WorldTick} estate={EstateId} deceased={DeceasedId} outcome={Outcome} reason={Reason}")]
    private static partial void LogEstateWill(ILogger logger, long worldTick, string estateId, string deceasedId, string outcome, string reason);

    [LoggerMessage(EventId = 2220, Level = LogLevel.Information,
        Message = "agent_belief_transition tick={WorldTick} owner={OwnerId} belief={BeliefId} outcome={Outcome} provenance={Provenance} confidence_basis_points={ConfidenceBasisPoints}")]
    private static partial void LogAgentBeliefTransition(ILogger logger, long worldTick, string ownerId,
        string beliefId, string outcome, SocietyBeliefProvenance provenance, int confidenceBasisPoints);

    [LoggerMessage(EventId = 2221, Level = LogLevel.Information,
        Message = "agent_memory_compaction tick={WorldTick} owner={OwnerId} assessed={AssessedCount} index_size={IndexSize}")]
    private static partial void LogAgentMemoryCompaction(ILogger logger, long worldTick, string ownerId,
        int assessedCount, int indexSize);

    [LoggerMessage(EventId = 2222, Level = LogLevel.Information,
        Message = "agent_knowledge tick={WorldTick} outcome={Outcome} agent={AgentId} recipient={RecipientId} artifact={ArtifactId} facts={FactCount}")]
    private static partial void LogAgentKnowledgeTransition(ILogger logger, long worldTick, string outcome,
        string agentId, string recipientId, string artifactId, int factCount);

    [LoggerMessage(EventId = 2253, Level = LogLevel.Information,
        Message = "autosave outcome=created save={SaveId} tick={WorldTick}")]
    private static partial void LogAutosaveCreated(ILogger logger, string saveId, long worldTick);

    [LoggerMessage(EventId = 2254, Level = LogLevel.Warning,
        Message = "autosave outcome=failed reason={Reason} tick={WorldTick}")]
    private static partial void LogAutosaveFailed(ILogger logger, string reason, long worldTick);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        runtime.AgentBeliefChanged += OnAgentBeliefChanged;
        if (logger is not null)
        {
            foreach (var town in runtime.Towns)
                TownTelemetry.Transition(logger, runtime.WorldTick, town.Id, TownTransitionKind.StateLoaded,
                    town.ResidentIds.Count, town.AssignedBuildingIds.Count, town.BorderTiles.Count);
        }
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        runtime.AgentBeliefChanged -= OnAgentBeliefChanged;
        return base.StopAsync(cancellationToken);
    }

    private void OnAgentBeliefChanged(PrivateWorldBeliefTransition transition)
    {
        if (logger is null) return;
        LogAgentBeliefTransition(logger, transition.WorldTick, transition.OwnerId, transition.BeliefId,
            transition.Outcome, transition.Provenance, transition.ConfidenceBasisPoints);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _ = await TryAdvanceOnceAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Advances one private-world tick only while at least one authenticated
    /// game client has a current presence lease. Manual world pause remains a
    /// separate persistent simulation state and is never cleared here.
    /// </summary>
    public async ValueTask<bool> TryAdvanceOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (invalidStateHalt)
        {
            runtime.Pause();
            return false;
        }
        try
        {
            if (recoveryWritePending)
            {
                // Keep the advanced in-memory state, retry only its checkpoint,
                // and require a later explicit Resume after recovery succeeds.
                runtime.Pause();
                writingCheckpoint = true;
                stateFile.Save(runtime);
                writingCheckpoint = false;
                recoveryWritePending = false;
                if (logger is not null) LogRecovery(logger, "saved_paused", runtime.WorldTick, "write_recovered");
                return false;
            }
            return await TryAdvanceCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            runtime.Pause();
            runtime.CancelPendingHostedDecisions();
            var writeFailure = writingCheckpoint && exception is (IOException or UnauthorizedAccessException);
            writingCheckpoint = false;
            if (writeFailure)
            {
                if (!recoveryWritePending && logger is not null)
                    LogRecovery(logger, "held_for_write", runtime.WorldTick, exception.GetType().Name);
                recoveryWritePending = true;
            }
            else
            {
                invalidStateHalt = true;
                if (logger is not null) LogRecovery(logger, "halted_for_inspection", runtime.WorldTick, exception.GetType().Name);
            }
            return false;
        }
    }

    private async ValueTask<bool> TryAdvanceCoreAsync(CancellationToken cancellationToken)
    {
        if (!clientPresence.HasActiveClient)
        {
            runtime.CancelPendingHostedDecisions();
            LogGateTransition("waiting_for_client", runtime.WorldTick);
            return false;
        }

        if (!TryBindPendingChildModels(out var recoveredChildBindings))
        {
            LogGateTransition("waiting_for_child_model_binding", runtime.WorldTick);
            return false;
        }
        if (recoveredChildBindings)
        {
            writingCheckpoint = true;
            _ = stateFile.Save(runtime);
            writingCheckpoint = false;
        }

        _ = runtime.StageStarterContent();
        using var monitorLifetime = new CancellationTokenSource();
        using var tickCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitor = MonitorTickGateAsync(tickCancellation, monitorLifetime.Token);
        var preparedChildBindings = new Dictionary<string, FrozenChildModelBinding>(StringComparer.Ordinal);
        PrivateWorldStepResult result;
        try
        {
            result = await runtime.AdvanceOneTickNonBlockingAsync(() => clientPresence.HasActiveClient,
                (proposed, events) => PrepareChildModelSelections(proposed, events, preparedChildBindings),
                tickCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && tickCancellation.IsCancellationRequested)
        {
            LogGateTransition(runtime.Society.IsPaused ? "paused" : "waiting_for_client", runtime.WorldTick);
            return false;
        }
        finally
        {
            await monitorLifetime.CancelAsync();
            await monitor;
        }
        LogGateTransition(result.Advanced ? "advancing" : result.Outcome, result.WorldTick);
        if (result.Advanced)
        {
            foreach (var (childId, binding) in preparedChildBindings)
                pendingChildModelBindings[childId] = binding;
            var bornChildren = result.Events.Where(item => item.Kind == "child_born").Select(item => item.Detail)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (bornChildren.Length > 0)
            {
                // The birth descriptor is part of the runtime tick commit. Persist it
                // before touching provider storage, so manual saves, world switches,
                // and shutdown cannot publish an unbound newborn.
                writingCheckpoint = true;
                if (providers is null)
                {
                    _ = stateFile.Save(runtime);
                }
                else
                {
                    lock (providers.WorldMutationGate)
                        _ = stateFile.Save(runtime);
                }
                writingCheckpoint = false;
                if (!TryBindPendingChildModels(out _))
                {
                    LogGateTransition("waiting_for_child_model_binding", runtime.WorldTick);
                    return false;
                }
            }
            writingCheckpoint = true;
            var compacted = stateFile.Save(runtime);
            writingCheckpoint = false;
            if (compacted && logger is not null)
            {
                var checkpoint = runtime.ExportState();
                LogHistoryCompacted(logger, result.WorldTick, checkpoint.EventHistoryFloor, checkpoint.Events.Count);
            }
            try
            {
                var saved = autosave is not null && manualSaves is not null && providers is not null
                    ? autosave.MaybeSave(DateTimeOffset.UtcNow, runtime, providers, manualSaves)
                    : null;
                if (saved is not null && logger is not null)
                    LogAutosaveCreated(logger, saved.Id, saved.WorldTick);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
            {
                // The active per-tick recovery save already committed; a
                // failed rotating copy must not halt world simulation.
                if (logger is not null) LogAutosaveFailed(logger, exception.GetType().Name, result.WorldTick);
            }
            if (logger?.IsEnabled(LogLevel.Information) == true)
            {
                foreach (var transition in result.MemoryCompactionTransitions)
                {
                    LogAgentMemoryCompaction(logger, transition.WorldTick, transition.OwnerId,
                        transition.AssessedCount, transition.IndexSize);
                }
                var actors = runtime.Society.Inhabitants.Select(person => person.Id)
                    .OrderByDescending(id => id.Length).ToArray();
                string? EventActor(string detail) => actors.FirstOrDefault(id => detail == id || detail.StartsWith(id + ":", StringComparison.Ordinal));
                foreach (var worldEvent in result.Events.Where(item => item.Kind.StartsWith("hosted_decision_", StringComparison.Ordinal)))
                {
                    var actor = EventActor(worldEvent.Detail);
                    if (actor is null) continue;
                    LogHostedDecision(logger, result.WorldTick, actor,
                        worldEvent.Kind["hosted_decision_".Length..] + worldEvent.Detail[actor.Length..]);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind is "tree_planted" or
                             "tree_planting_refused" or "tree_replanted" or "tree_seed_collected"))
                {
                    // Details after the actor are bounded IDs, species and refusal codes.
                    var actor = EventActor(worldEvent.Detail);
                    if (actor is null || worldEvent.Detail.Length <= actor.Length + 1) continue;
                    LogTreePlanting(logger, result.WorldTick, actor, worldEvent.Kind,
                        worldEvent.Detail[(actor.Length + 1)..]);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind.StartsWith("estate_will_", StringComparison.Ordinal)))
                {
                    var estate = runtime.Society.Estates.FirstOrDefault(item =>
                        worldEvent.Detail == item.Id || worldEvent.Detail.StartsWith(item.Id + ":", StringComparison.Ordinal));
                    if (estate is not null)
                    {
                        var outcome = worldEvent.Kind["estate_will_".Length..];
                        var reason = EstateWillReason(worldEvent, estate.Id, outcome);
                        LogEstateWill(logger, result.WorldTick, estate.Id, estate.DeceasedId,
                            outcome, reason);
                    }
                }
                var projects = runtime.Inhabitants.Where(person => person.Project is not null)
                    .ToDictionary(person => person.InhabitantId, person => person.Project!, StringComparer.Ordinal);
                foreach (var worldEvent in result.Events.Where(item => item.Kind.StartsWith("town_", StringComparison.Ordinal) ||
                             item.Kind is "bridge_built" or "traffic_bridge_not_built"))
                    LogTownEvent(worldEvent);
                foreach (var worldEvent in result.Events.Where(item => item.Kind == "work_practice_earned"))
                {
                    var actor = EventActor(worldEvent.Detail);
                    if (actor is not null && runtime.Inhabitants.FirstOrDefault(person => person.InhabitantId == actor)?.Proficiency is { } practice)
                        LogWorkPractice(logger, result.WorldTick, actor, practice.Building, practice.Farming, practice.Crafting);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind == "social_standing_changed"))
                {
                    var actor = EventActor(worldEvent.Detail);
                    if (actor is null || worldEvent.Detail.Length <= actor.Length + 1) continue;
                    var remainder = worldEvent.Detail[(actor.Length + 1)..];
                    var subject = actors.FirstOrDefault(id => remainder == id || remainder.StartsWith(id + ":", StringComparison.Ordinal));
                    var standing = runtime.Inhabitants.FirstOrDefault(person => person.InhabitantId == actor)?.SocialStanding?
                        .FirstOrDefault(item => item.SubjectId == subject);
                    if (subject is null || standing is null) continue;
                    var reason = remainder.Length > subject.Length ? remainder[(subject.Length + 1)..] : "cooperation";
                    LogSocialStanding(logger, result.WorldTick, actor, subject, standing.Trust, reason);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind is "project_chosen" or "project_progress" or
                             "project_request_fulfilled" or "town_resources_stored" or "town_resource_collected"))
                {
                    var actor = EventActor(worldEvent.Detail);
                    if (actor is null) continue;
                    var project = projects.GetValueOrDefault(actor);
                    LogSettlementActivity(logger, result.WorldTick, worldEvent.Kind, actor,
                        worldEvent.Kind is "town_resources_stored" or "town_resource_collected"
                            ? "warehouse" : project?.Stage ?? "helping", project?.WorkDone ?? 0,
                        worldEvent.Kind is not ("town_resources_stored" or "town_resource_collected") &&
                        project?.Blocker is not null);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind == "survival_condition_changed"))
                {
                    var actor = EventActor(worldEvent.Detail);
                    if (actor is null) continue;
                    if (runtime.Inhabitants.FirstOrDefault(person => person.InhabitantId == actor)?.Survival is { } condition)
                    {
                        var illnessWorkPercent = SettlementIllnessRules.WorkRatePercent(condition.IllnessBasisPoints);
                        var illnessTravelDelay = SettlementIllnessRules.TravelDelayTicks(condition.IllnessBasisPoints);
                        LogSurvivalCondition(logger, result.WorldTick, actor, condition.WarmthBasisPoints, condition.IllnessBasisPoints,
                            illnessWorkPercent, illnessTravelDelay);
                    }
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind is "fire_fuelled" or "fire_extinguished"))
                {
                    LogSurvivalEnvironment(logger, result.WorldTick, worldEvent.Kind);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind == "production_worker_unavailable"))
                {
                    var job = runtime.WorldSimulation.ProductionJobs.Concat(runtime.WorldSimulation.CropBuilds ?? [])
                        .FirstOrDefault(item => item.JobId == worldEvent.Detail);
                    if (job is not null) LogProductionCancelled(logger, result.WorldTick, job.JobId, job.WorkerId);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind is "settlement_trade_offered" or
                             "settlement_trade_declined" or "settlement_trade_completed" or "settlement_trade_cancelled"))
                {
                    LogSettlementTrade(logger, result.WorldTick, worldEvent.Kind);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind.StartsWith("agent_knowledge_", StringComparison.Ordinal)))
                {
                    LogKnowledgeEvent(worldEvent);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind is "council_steward_changed" or
                             "council_policy_proposed" or "council_vote_recorded" or "council_policy_adopted" or "council_policy_rejected"))
                {
                    LogSettlementCouncil(logger, result.WorldTick, worldEvent.Kind);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind is "lesson_requested" or "lesson_accepted" or
                             "lesson_training" or "lesson_completed" or "lesson_declined" or "lesson_cancelled"))
                {
                    LogSettlementLesson(logger, result.WorldTick, worldEvent.Kind);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind is "partnership_proposed" or "partnership_accepted" or
                             "partnership_refused" or "partnership_ended" or "partnership_expired" or
                             "parenthood_requested" or "parenthood_preparing" or "parenthood_cancelled" or "parenthood_completed" or
                             "child_born" or "child_cared_for" or "caregiver_proposed" or "caregiver_assigned" or
                             "caregiver_accepted" or "caregiver_refused" or "caregiver_proposal_expired" or "caregiver_ended" or
                             "dependent_cared_for"))
                {
                    LogSettlementFamily(logger, result.WorldTick, worldEvent.Kind);
                }
                foreach (var worldEvent in result.Events.Where(item => item.Kind is "housing_request_made" or
                             "housing_answer_recorded" or "household_joined" or "housing_request_refused" or
                             "housing_request_expired" or "housing_request_cancelled" or "housing_blocked"))
                {
                    LogSettlementHousing(logger, result.WorldTick, worldEvent.Kind);
                }
            }
        }

        foreach (var decision in result.Decisions.OrderBy(item => item.InhabitantId, StringComparer.Ordinal))
        {
            var intention = decision.Admission.Intention;
            if (logger?.IsEnabled(LogLevel.Information) == true)
            {
                var provider = intention?.Provider.ToString().ToLowerInvariant() ?? "none";
                LogCognitionDecision(
                    logger,
                    result.WorldTick,
                    decision.InhabitantId,
                    decision.Admission.Accepted,
                    decision.Admission.FellBack,
                    decision.Admission.Outcome,
                    provider,
                    intention?.CandidateId ?? "none",
                    intention?.Confidence ?? 0,
                    intention?.Usage?.ModelId ?? "none",
                    intention?.Usage?.InputTokens ?? 0,
                    intention?.Usage?.OutputTokens ?? 0);
            }
        }

        return result.Advanced;
    }

    private bool TryBindPendingChildModels(out bool changed)
    {
        changed = false;
        if (providers is null) return true;

        try
        {
            lock (providers.WorldMutationGate)
            {
                var configuration = providers.CaptureRuntimeConfiguration();
                var people = runtime.Inhabitants.ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
                foreach (var childId in pendingChildModelBindings.Keys.ToArray())
                {
                    if (!people.TryGetValue(childId, out var child))
                    {
                        pendingChildModelBindings.Remove(childId);
                        continue;
                    }
                    if (child.ChildModelSelection is null)
                        throw new InvalidDataException("A newly born child has no birth-bound model choice.");
                    if (pendingChildModelBindings[childId].Selection != child.ChildModelSelection)
                        pendingChildModelBindings[childId] = new FrozenChildModelBinding(child.ChildModelSelection, null);
                }

                foreach (var child in people.Values.Where(item => item.ChildModelSelection is not null))
                {
                    var selection = child.ChildModelSelection!;
                    var childRows = (configuration.Assignments ?? []).Where(item =>
                        item.InhabitantId == child.InhabitantId &&
                        item.Role is PlayerDecisionProviders.RoutineRole or PlayerDecisionProviders.PlanningRole).ToArray();
                    var birthRows = childRows.Where(item => item.SelectionReason is not null).ToArray();
                    if (selection.Provider is null)
                    {
                        if (birthRows.Length > 0)
                            throw new InvalidDataException("An unconfigured child has a saved birth model assignment.");
                        if (!pendingChildModelBindings.ContainsKey(child.InhabitantId))
                            continue;
                    }
                    else
                    {
                        if (birthRows.Any(item => item.Provider != selection.Provider || item.Model != selection.ModelId ||
                                item.CredentialSlotId != selection.CredentialSlotId || item.SelectionReason != selection.ChoiceReason))
                            throw new InvalidDataException("A child's saved route does not match its birth model choice.");
                        var bothRolesConfigured = new[] { PlayerDecisionProviders.RoutineRole, PlayerDecisionProviders.PlanningRole }
                            .All(role => childRows.Any(item => item.Role == role));
                        if (bothRolesConfigured)
                        {
                            pendingChildModelBindings.Remove(child.InhabitantId);
                            continue;
                        }
                    }
                    if (!pendingChildModelBindings.ContainsKey(child.InhabitantId))
                        pendingChildModelBindings.Add(child.InhabitantId, new FrozenChildModelBinding(selection, null));
                }

                foreach (var childId in pendingChildModelBindings.Keys.Order(StringComparer.Ordinal).ToArray())
                {
                    if (!people.TryGetValue(childId, out var child)) continue;
                    var binding = pendingChildModelBindings[childId];
                    if (child.ChildModelSelection != binding.Selection)
                        throw new InvalidDataException("The pending child model choice no longer matches the saved birth descriptor.");
                    providers.ConfigureChildModelSelectionWithCommit(childId, binding,
                        boundSelection => runtime.ApplyChildModelSelection(childId, boundSelection));
                    pendingChildModelBindings.Remove(childId);
                    changed = true;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or ArgumentException)
        {
            runtime.Pause();
            runtime.CancelPendingHostedDecisions();
            if (logger is not null)
                LogRecovery(logger, "waiting_for_child_model_binding", runtime.WorldTick, exception.GetType().Name);
            return false;
        }
    }

    private List<PreparedChildModelSelection> PrepareChildModelSelections(
        PrivateWorldRuntime proposed,
        IReadOnlyList<PlaytestWorldEvent> events,
        Dictionary<string, FrozenChildModelBinding> preparedBindings)
    {
        if (providers is null) return [];
        var births = events.Where(item => item.Kind == "child_born")
            .Select(item => item.Detail).Distinct(StringComparer.Ordinal).ToArray();
        if (births.Length == 0) return [];

        var configuration = providers.CaptureRuntimeConfiguration();
        var people = proposed.Inhabitants.ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
        var selections = new List<PreparedChildModelSelection>(births.Length);
        foreach (var childId in births)
        {
            if (!people.TryGetValue(childId, out var child) || child.ChildModelSelection is not null)
                throw new InvalidDataException("A newborn child cannot receive a second model choice.");
            var initiator = people.Values.FirstOrDefault(item => item.Parenthood?.ChildId == childId);
            if (initiator?.Parenthood is not { } plan)
                throw new InvalidDataException("A newly born child has no initiating parent record.");

            var selection = ChildModelSelectionResolver.Choose(initiator.InhabitantId, plan.PartnerId, configuration);
            var frozen = ProviderConfigurationStore.FreezeChildModelSelection(selection, configuration);
            preparedBindings.Add(childId, frozen);
            selections.Add(new PreparedChildModelSelection(childId, frozen.Selection));
        }
        return selections;
    }

    private void LogTownEvent(PlaytestWorldEvent worldEvent)
    {
        if (logger is null) return;
        var civicKind = worldEvent.Kind switch
        {
            "town_civic_council" => TownCivicTransitionKind.CouncilChanged,
            "town_civic_election" => TownCivicTransitionKind.ElectionOpened,
            "town_civic_runoff" => TownCivicTransitionKind.RunoffOpened,
            "town_civic_proposal" => TownCivicTransitionKind.ProposalOpened,
            "town_civic_result" => TownCivicTransitionKind.DecisionRecorded,
            "town_civic_cancelled" => TownCivicTransitionKind.ElectionCancelled,
            _ => (TownCivicTransitionKind?)null,
        };
        if (civicKind is { } civicTransition)
        {
            var fields = worldEvent.Detail.Split('|', 3);
            var civicTown = fields.Length == 3 ? runtime.Towns.FirstOrDefault(t => t.Id == fields[0]) : null;
            if (civicTown?.Governance is { } governance)
            {
                var proposal = governance.Proposals.FirstOrDefault(p => p.Id == fields[1]);
                var election = governance.Election?.Id == fields[1] ? governance.Election :
                    governance.ElectionHistory.FirstOrDefault(e => e.Id == fields[1]);
                TownTelemetry.Civic(logger, worldEvent.WorldTick, civicTown.Id, civicTransition,
                    governance.Form == "representative", governance.Members.Count, proposal?.Status ?? election?.Stage ?? "none",
                    proposal?.Votes.Count(v => v.Yes) ?? 0, proposal?.Votes.Count(v => !v.Yes) ?? 0, election?.Ballots.Count ?? 0);
            }
            return;
        }
        if (worldEvent.Kind == "town_layout_site_rejected")
        {
            var fields = worldEvent.Detail.Split('|', StringSplitOptions.None);
            if (fields.Length == 6 &&
                int.TryParse(fields[3], out var x) &&
                int.TryParse(fields[4], out var y) &&
                fields[5] == "site_unavailable")
            {
                TownTelemetry.SiteRejected(logger, worldEvent.WorldTick, fields[0], fields[1],
                    fields[2], x, y, fields[5]);
            }
            return;
        }
        if (worldEvent.Kind is "bridge_built" or "traffic_bridge_not_built" or "town_road_unconnected")
        {
            LogRoadOrBridgeEvent(logger, worldEvent, runtime.Towns);
            return;
        }
        var kind = worldEvent.Kind switch
        {
            "town_resident_joined" => TownTransitionKind.ResidentJoined,
            "town_resident_left" => TownTransitionKind.ResidentLeft,
            "town_membership_evaluated" => TownTransitionKind.ResidentUnaffiliated,
            "town_founded" => TownTransitionKind.Founded,
            "town_building_assigned" => TownTransitionKind.BuildingAssigned,
            "town_border_expanded" => TownTransitionKind.BorderExpanded,
            _ => (TownTransitionKind?)null,
        };
        if (kind is null) return;
        var townId = worldEvent.Kind == "town_membership_evaluated"
            ? "none"
            : runtime.Towns.OrderByDescending(item => item.Id.Length).FirstOrDefault(item =>
                worldEvent.Detail == item.Id || worldEvent.Detail.StartsWith(item.Id + ":", StringComparison.Ordinal))?.Id;
        if (townId is null) return;
        var town = runtime.Towns.FirstOrDefault(item => item.Id == townId);
        TownTelemetry.Transition(logger, worldEvent.WorldTick, townId, kind.Value,
            town?.ResidentIds.Count ?? 0, town?.AssignedBuildingIds.Count ?? 0, town?.BorderTiles.Count ?? 0);
    }

    /// <summary>Bounded Road and bridge outcomes from accepted events; details carry only IDs and reason codes.</summary>
    private static void LogRoadOrBridgeEvent(ILogger logger, PlaytestWorldEvent worldEvent,
        IReadOnlyList<TownRuntimeState> towns)
    {
        var detail = worldEvent.Detail;
        var reasonAt = detail.LastIndexOf(':');
        switch (worldEvent.Kind)
        {
            case "bridge_built":
                var fields = detail.Split(':', 3);
                if (fields.Length >= 2)
                    TownTelemetry.Bridge(logger, worldEvent.WorldTick, fields[1], fields[0], "built", "none");
                break;
            case "traffic_bridge_not_built" when reasonAt > 0:
                TownTelemetry.Bridge(logger, worldEvent.WorldTick, detail[..reasonAt], "traffic", "not_built",
                    detail[(reasonAt + 1)..]);
                break;
            case "town_road_unconnected":
                var town = towns.OrderByDescending(item => item.Id.Length)
                    .FirstOrDefault(item => detail.StartsWith(item.Id + ":", StringComparison.Ordinal));
                if (town is not null && reasonAt > town.Id.Length + 1)
                    TownTelemetry.RoadUnconnected(logger, worldEvent.WorldTick, town.Id,
                        detail[(town.Id.Length + 1)..reasonAt], detail[(reasonAt + 1)..]);
                break;
        }
    }

    private static string EstateWillReason(PlaytestWorldEvent worldEvent, string estateId, string outcome)
    {
        if (outcome == "started") return "decision_dispatch_attempted";
        if (outcome == "accepted") return "valid_heir_selected";
        if (outcome != "default" || !worldEvent.Detail.StartsWith(estateId + ":", StringComparison.Ordinal))
            return "unspecified";

        var reason = worldEvent.Detail[(estateId.Length + 1)..];
        return reason is "empty_estate" or "no_personal_model" or "timeout_or_cancelled" or
            "household_selected" or "invalid_response" or "deadline" or "interrupted" or
            "provider_changed" or "provider_unavailable" or "run_epoch_changed" or
            "invalid_estate_or_heir" || reason.StartsWith("provider_", StringComparison.Ordinal) && reason.Length <= 64 ||
            reason.StartsWith("setup_", StringComparison.Ordinal) && reason.Length <= 64
            ? reason
            : "unspecified";
    }

    private async Task MonitorTickGateAsync(CancellationTokenSource tickCancellation, CancellationToken monitorToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(monitorToken))
            {
                if (!clientPresence.HasActiveClient || runtime.Society.IsPaused)
                {
                    await tickCancellation.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (monitorToken.IsCancellationRequested)
        {
            // The proposed tick completed; no lifecycle watcher survives it.
        }
    }

    private void LogGateTransition(string state, long worldTick)
    {
        if (string.Equals(lastGateState, state, StringComparison.Ordinal))
        {
            return;
        }

        lastGateState = state;
        if (logger is not null)
        {
            LogWorldTickGate(logger, state, worldTick, clientPresence.ActiveClientCount);
        }
    }

    private void LogKnowledgeEvent(PlaytestWorldEvent worldEvent)
    {
        if (logger is null || !logger.IsEnabled(LogLevel.Information)) return;
        var fields = worldEvent.Detail.Split('|');
        var agentId = fields.ElementAtOrDefault(0) ?? "unknown";
        var recipientId = worldEvent.Kind is "agent_knowledge_shared" or "agent_knowledge_artifact_read"
            ? fields.ElementAtOrDefault(1) ?? "unknown"
            : "";
        var artifactId = worldEvent.Kind switch
        {
            "agent_knowledge_artifact_created" => fields.ElementAtOrDefault(1) ?? "",
            "agent_knowledge_shared" or "agent_knowledge_artifact_read" => fields.ElementAtOrDefault(2) ?? "",
            _ => "",
        };
        var factCountIndex = worldEvent.Kind switch
        {
            "agent_knowledge_artifact_created" or "agent_knowledge_shared" or "agent_knowledge_artifact_read" => 3,
            _ => 2,
        };
        var factCount = 0;
        if (int.TryParse(fields.ElementAtOrDefault(factCountIndex), out var parsedFactCount) &&
            parsedFactCount is >= 0 and <= 9)
        {
            factCount = parsedFactCount;
        }
        LogAgentKnowledgeTransition(logger, worldEvent.WorldTick, worldEvent.Kind,
            agentId, recipientId, artifactId, factCount);
    }

    [LoggerMessage(EventId = 2212, Level = LogLevel.Information,
        Message = "production_cancelled tick={WorldTick} job={JobId} inhabitant={InhabitantId} reason=worker_unavailable")]
    private static partial void LogProductionCancelled(ILogger logger, long worldTick, string jobId, string inhabitantId);

    [LoggerMessage(EventId = 2209, Level = LogLevel.Information,
        Message = "settlement_lesson tick={WorldTick} event={EventKind}")]
    private static partial void LogSettlementLesson(ILogger logger, long worldTick, string eventKind);

    [LoggerMessage(EventId = 2210, Level = LogLevel.Information,
        Message = "settlement_family tick={WorldTick} event={EventKind}")]
    private static partial void LogSettlementFamily(ILogger logger, long worldTick, string eventKind);

    [LoggerMessage(EventId = 2208, Level = LogLevel.Information,
        Message = "settlement_council tick={WorldTick} event={EventKind}")]
    private static partial void LogSettlementCouncil(ILogger logger, long worldTick, string eventKind);

    [LoggerMessage(EventId = 2211, Level = LogLevel.Information,
        Message = "settlement_housing tick={WorldTick} event={EventKind}")]
    private static partial void LogSettlementHousing(ILogger logger, long worldTick, string eventKind);

    [LoggerMessage(EventId = 2207, Level = LogLevel.Information,
        Message = "settlement_trade tick={WorldTick} event={EventKind}")]
    private static partial void LogSettlementTrade(ILogger logger, long worldTick, string eventKind);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Information,
        Message = "world_history_compacted tick={WorldTick} event_floor={EventFloor} recent_events={RecentEvents}")]
    private static partial void LogHistoryCompacted(ILogger logger, long worldTick, long eventFloor, int recentEvents);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Information,
        Message = "settlement_activity tick={WorldTick} event={EventKind} inhabitant={InhabitantId} stage={Stage} work={WorkDone} blocked={Blocked}")]
    private static partial void LogSettlementActivity(ILogger logger, long worldTick, string eventKind,
        string inhabitantId, string stage, int workDone, bool blocked);

    [LoggerMessage(EventId = 2205, Level = LogLevel.Information,
        Message = "survival_condition tick={WorldTick} inhabitant={InhabitantId} warmth={Warmth} illness={Illness} illness_work_percent={IllnessWorkPercent} illness_travel_delay={IllnessTravelDelay}")]
    private static partial void LogSurvivalCondition(ILogger logger, long worldTick, string inhabitantId, int warmth, int illness,
        int illnessWorkPercent, int illnessTravelDelay);

    [LoggerMessage(EventId = 2206, Level = LogLevel.Information,
        Message = "survival_environment tick={WorldTick} event={EventKind}")]
    private static partial void LogSurvivalEnvironment(ILogger logger, long worldTick, string eventKind);

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Information,
        Message = "cognition_decision tick={WorldTick} inhabitant={InhabitantId} accepted={Accepted} fallback={FellBack} outcome={Outcome} provider={Provider} candidate={CandidateId} confidence={Confidence} model={Model} input_tokens={InputTokens} output_tokens={OutputTokens}")]
    private static partial void LogCognitionDecision(
        ILogger logger,
        long worldTick,
        string inhabitantId,
        bool accepted,
        bool fellBack,
        string outcome,
        string provider,
        string candidateId,
        double confidence,
        string model,
        int inputTokens,
        int outputTokens);

    [LoggerMessage(
        EventId = 2202,
        Level = LogLevel.Information,
        Message = "world_tick_gate state={State} tick={WorldTick} active_clients={ActiveClientCount}")]
    private static partial void LogWorldTickGate(
        ILogger logger,
        string state,
        long worldTick,
        int activeClientCount);
}
