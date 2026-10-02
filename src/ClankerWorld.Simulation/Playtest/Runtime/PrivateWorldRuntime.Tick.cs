using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    public ValueTask<PrivateWorldStepResult> AdvanceOneTickAsync(CancellationToken cancellationToken = default) =>
        AdvanceOneTickAsync(null, cancellationToken);

    public async ValueTask<PrivateWorldStepResult> AdvanceOneTickAsync(
        Func<bool>? commitPermitted, CancellationToken cancellationToken = default) =>
        await AdvanceOneTickCoreAsync(false, commitPermitted, null, cancellationToken).ConfigureAwait(false);

    /// <summary>Playable-host path: hosted decisions run between ticks, never inside a tick transaction.</summary>
    public ValueTask<PrivateWorldStepResult> AdvanceOneTickNonBlockingAsync(
        Func<bool>? commitPermitted = null, CancellationToken cancellationToken = default) =>
        AdvanceOneTickCoreAsync(true, commitPermitted, null, cancellationToken);

    /// <summary>Playable-host path that binds birth choices into the admitted tick transaction.</summary>
    public ValueTask<PrivateWorldStepResult> AdvanceOneTickNonBlockingAsync(
        Func<bool>? commitPermitted,
        Func<PrivateWorldRuntime, IReadOnlyList<PlaytestWorldEvent>, IReadOnlyList<PreparedChildModelSelection>> prepareChildModelSelections,
        CancellationToken cancellationToken = default) =>
        AdvanceOneTickCoreAsync(true, commitPermitted, prepareChildModelSelections, cancellationToken);

    private async ValueTask<PrivateWorldStepResult> AdvanceOneTickCoreAsync(
        bool deferHosted, Func<bool>? commitPermitted,
        Func<PrivateWorldRuntime, IReadOnlyList<PlaytestWorldEvent>, IReadOnlyList<PreparedChildModelSelection>>? prepareChildModelSelections,
        CancellationToken cancellationToken)
    {
        await tickGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PrivateWorldRuntimeState baseline;
            long baselineEventId;
            PendingHostedDecision[] completed = [];
            PendingWillDecision[] completedWills = [];
            PendingConversationTurn[] completedConversationTurns = [];
            PendingIdentityMoment[] completedIdentityMoments = [];
            string[] activeWillIds = [];
            IReadOnlyDictionary<string, string> inactiveWillReasons = new Dictionary<string, string>();
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (society.Checkpoint.IsPaused)
                {
                    return new PrivateWorldStepResult(false, "paused", WorldTick, [], []);
                }
                if (deferHosted)
                {
                    foreach (var (id, pending) in pendingHosted.ToArray())
                    {
                        if (!society.Checkpoint.Inhabitants.Any(person => person.Id == id && person.Status == SocietyInhabitantStatus.Active) ||
                            society.CurrentProviderEpoch(id) != pending.Request.ProviderEpoch ||
                            society.Checkpoint.RunEpoch != pending.Request.Observation.RunEpoch)
                        {
                            CancelPendingHosted(id);
                        }
                    }
                    foreach (var (id, pending) in pendingWills.ToArray())
                    {
                        var estate = society.Checkpoint.Estates.FirstOrDefault(item => item.Id == id);
                        if (estate is null || estate.WillStatus != "pending")
                        {
                            CancelPendingWill(id);
                            continue;
                        }
                        if (society.Checkpoint.RunEpoch != pending.Request.Observation.RunEpoch)
                        {
                            CancelPendingWill(id, "run_epoch_changed");
                            continue;
                        }
                        try
                        {
                            var currentProvider = providerFactory?.Invoke(estate.DeceasedId) ?? new DeterministicDecisionProvider();
                            if (currentProvider.KindFor(pending.Request.Observation) != DecisionProviderKind.LargeLanguageModel ||
                                currentProvider.ProviderEpoch != pending.Request.ProviderEpoch)
                                CancelPendingWill(id, "provider_changed");
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException)
                        {
                            CancelPendingWill(id, "provider_unavailable");
                        }
                    }
                    completed = pendingHosted.Values.Where(item => item.Task.IsCompleted).ToArray();
                    completedWills = pendingWills.Values.Where(item => item.Task.IsCompleted).ToArray();
                    activeWillIds = pendingWills.Keys.ToArray();
                    inactiveWillReasons = new Dictionary<string, string>(pendingWillCancellationReasons, StringComparer.Ordinal);
                }
                ReconcilePendingConversationTurns();
                ReconcileIdentityMoments();
                completedIdentityMoments = pendingIdentityMoments.Values.Where(item => item.Task.IsCompleted).ToArray();
                completedConversationTurns = pendingConversationTurns.Values
                    .Where(item => item.Task.IsCompleted).ToArray();
                baseline = CaptureState();
                baselineEventId = nextEventId;
            }
            finally
            {
                gate.Release();
            }

            // World mutations operate on an isolated proposed tick. In the
            // playable path, external cognition itself runs between ticks.
            // Readers and owner controls use the last committed world.
            // The baseline was captured from this committed runtime under the
            // gate. Clone its mutable systems without regenerating or
            // revalidating millions of immutable terrain tiles each tick.
            using var proposed = RestoreCore(baseline, providerFactory,
                maxCognitionDispatchPerCycle,
                trustedPreparedState: true);
            var result = await proposed.AdvancePreparedTickAsync(deferHosted, completed, completedWills,
                activeWillIds, inactiveWillReasons, completedConversationTurns, cancellationToken).ConfigureAwait(false);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var gateHeld = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (commitPermitted is not null && !commitPermitted())
                {
                    return new PrivateWorldStepResult(false, "waiting_for_client", WorldTick, [], []);
                }
                if (nextEventId != baselineEventId || WorldTick != baseline.Society.Society.WorldTick || historyArchiveHead != baseline.HistoryArchiveHead)
                {
                    return new PrivateWorldStepResult(false, "tick_superseded_by_owner_change", WorldTick, [], []);
                }
                if (!result.Advanced)
                {
                    return result;
                }
                if (prepareChildModelSelections is not null)
                {
                    // Provider preparation can read another store whose owner
                    // transactions may call back into this runtime. Do it outside
                    // the runtime gate, then revalidate before committing.
                    gate.Release();
                    gateHeld = false;
                    var births = result.Events.Where(item => item.Kind == "child_born")
                        .Select(item => item.Detail).ToHashSet(StringComparer.Ordinal);
                    foreach (var prepared in prepareChildModelSelections(proposed, result.Events))
                    {
                        if (!births.Contains(prepared.ChildId))
                            throw new InvalidOperationException("A child model selection must belong to a child born in the committed tick.");
                        proposed.ApplyChildModelSelection(prepared.ChildId, prepared.Selection);
                    }
                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    gateHeld = true;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (commitPermitted is not null && !commitPermitted())
                        return new PrivateWorldStepResult(false, "waiting_for_client", WorldTick, [], []);
                    if (nextEventId != baselineEventId || WorldTick != baseline.Society.Society.WorldTick || historyArchiveHead != baseline.HistoryArchiveHead)
                        return new PrivateWorldStepResult(false, "tick_superseded_by_owner_change", WorldTick, [], []);
                }
                // A provider assignment can change without advancing a world
                // event or tick. Admit its completed turn only now, while
                // the live-state gate protects the commit boundary.
                proposed.CompleteConversationTurns(
                    completedConversationTurns,
                    proposed.WorldTick,
                    IsConversationTurnProviderCurrent);
                proposed.CompleteIdentityMoments(completedIdentityMoments, IsIdentityMomentProviderCurrent);
                CommitPreparedTick(proposed);
                foreach (var item in completedIdentityMoments)
                {
                    pendingIdentityMoments.Remove(item.Request.Observation.InhabitantId);
                    item.Cancellation.Dispose();
                }
                if (deferHosted)
                {
                    foreach (var id in pendingWills.Keys.Where(id =>
                                 society.Checkpoint.Estates.All(estate => estate.Id != id || estate.WillStatus != "pending"))
                             .ToArray())
                        CancelPendingWill(id);
                    foreach (var item in completed)
                    {
                        pendingHosted.Remove(item.Request.Observation.InhabitantId);
                        item.Cancellation.Dispose();
                    }
                    foreach (var item in completedWills)
                    {
                        pendingWills.Remove(item.EstateId);
                        item.Cancellation.Dispose();
                    }
                    foreach (var item in completedConversationTurns)
                    {
                        pendingConversationTurns.Remove(item.Request.ConversationId);
                        item.Cancellation.Dispose();
                    }
                    CancelNoLongerAwaitingConversationTurns();
                    foreach (var id in inactiveWillReasons.Keys)
                        pendingWillCancellationReasons.Remove(id);
                    if (commitPermitted is null || commitPermitted()) StartWillDecisions();
                    if (commitPermitted is null || commitPermitted()) StartHostedDecisions();
                }
                else
                {
                    foreach (var item in completedConversationTurns)
                    {
                        pendingConversationTurns.Remove(item.Request.ConversationId);
                        item.Cancellation.Dispose();
                    }
                    CancelNoLongerAwaitingConversationTurns();
                }
                if (commitPermitted is null || commitPermitted()) StartConversationTurns();
                if (commitPermitted is null || commitPermitted()) StartIdentityMoments();
                return result with { Events = events.Where(item => item.EventId >= baselineEventId).ToArray() };
            }
            finally
            {
                if (gateHeld) gate.Release();
            }
        }
        finally
        {
            tickGate.Release();
        }
    }

    private void StartHostedDecisions()
    {
        var capacity = Math.Max(0, maxCognitionDispatchPerCycle - pendingHosted.Count);
        if (capacity == 0) return;
        foreach (var preview in society.PreviewHostedRequests(pendingHosted.Keys.Concat(pendingIdentityMoments.Keys)
                     .ToHashSet(StringComparer.Ordinal)).Take(capacity))
        {
            var cancellation = new CancellationTokenSource();
            var task = Task.Run(async () =>
            {
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    return new HostedDecisionOutcome(
                        await preview.DecideAsync(cancellation.Token).ConfigureAwait(false), null);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    return new HostedDecisionOutcome(null,
                        CognitionProviderFailures.FromException(exception, cancellation.Token));
                }
            });
            pendingHosted.Add(preview.InhabitantId, new PendingHostedDecision(preview.Request, task, cancellation));
            RecordModelAttempt(preview.InhabitantId, "waiting");
            AppendEvent("hosted_decision_started", preview.InhabitantId);
        }
    }

    private void CancelPendingHosted(string inhabitantId)
    {
        if (!pendingHosted.Remove(inhabitantId, out var pending)) return;
        RecordModelAttempt(inhabitantId, "canceled");
        pending.Cancellation.Cancel();
        _ = pending.Task.ContinueWith(_ => pending.Cancellation.Dispose(), TaskScheduler.Default);
    }

    public void CancelPendingHostedDecisions()
    {
        gate.Wait();
        try
        {
            foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
            foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
            CancelIdentityMoments();
            foreach (var id in pendingConversationTurns.Keys.ToArray())
                CancelPendingConversationTurn(id, AgentConversationInterruption.Disconnected);
            SuspendAllConversations(AgentConversationInterruption.Disconnected);
        }
        finally { gate.Release(); }
    }

    private void CommitPreparedTick(PrivateWorldRuntime proposed)
    {
        // Transfer the committed society; disposing the proposal retires the old one.
        (society, proposed.society) = (proposed.society, society);
        worldSeed = proposed.worldSeed;
        map = proposed.map;
        fertility = proposed.fertility;
        fields = proposed.fields;
        geographyOptions = proposed.geographyOptions;
        contentRegistry = proposed.contentRegistry;
        worldSystems = proposed.worldSystems;
        survivalState = proposed.survivalState;
        council = proposed.council;
        worldContent = proposed.worldContent;
        worldSimulation = proposed.worldSimulation;
        assetReservations = proposed.assetReservations;
        inhabitants = proposed.inhabitants;
        deceasedInhabitants = proposed.deceasedInhabitants;
        resources = proposed.resources;
        knowledge = proposed.knowledge;
        instructionsByIdempotency = proposed.instructionsByIdempotency;
        instructionReceipts = proposed.instructionReceipts;
        completedInstructionIds = proposed.completedInstructionIds;
        events = proposed.events;
        nextEventId = proposed.nextEventId;
        eventHistoryFloor = proposed.eventHistoryFloor;
        historyArchiveHead = proposed.historyArchiveHead;
        checkpointSchemaVersion = proposed.checkpointSchemaVersion;
        jevEnabled = proposed.jevEnabled;
        jevPolicyRevision = proposed.jevPolicyRevision;
        founderSetup = proposed.founderSetup;
        towns = proposed.towns;
        townLandTitles = proposed.townLandTitles;
        householdLandUseRights = proposed.householdLandUseRights;
        householdLandUseRequests = proposed.householdLandUseRequests;
        roadTiles = proposed.roadTiles;
        bridges = proposed.bridges;
        bridgeTraffic = proposed.bridgeTraffic;
        conversations = proposed.conversations;
        conversationBudgets = proposed.conversationBudgets;
        businessTrades = proposed.businessTrades;
        roadBridgeDecks = proposed.roadBridgeDecks;
        nextInstructionSequence = proposed.nextInstructionSequence;
    }

    /// <summary>Rewind this paused world to a validated checkpoint of the same world.</summary>
    public void LoadPausedCheckpoint(PrivateWorldRuntimeState checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!string.Equals(checkpoint.WorldSeed, worldSeed, StringComparison.Ordinal))
            throw new InvalidDataException("A checkpoint belongs to a different world.");
        tickGate.Wait();
        try
        {
            gate.Wait();
            try
            {
                if (!society.Checkpoint.IsPaused)
                    throw new InvalidOperationException("Pause the world before loading a checkpoint.");
                using var restored = Restore(checkpoint, providerFactory,
                    maxCognitionDispatchPerCycle);
                // Loading never resumes a world implicitly, even if the saved
                // checkpoint was taken while it was running.
                restored.Pause();
                foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
                foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
                CancelIdentityMoments();
                foreach (var id in pendingConversationTurns.Keys.ToArray())
                    CancelPendingConversationTurn(id, AgentConversationInterruption.OwnerPaused, suspendCurrent: false);
                CommitPreparedTick(restored);
            }
            finally { gate.Release(); }
        }
        finally { tickGate.Release(); }
    }

    /// <summary>Replace the selected world while the current world is paused.</summary>
    public void SwitchPausedWorld(PrivateWorldRuntimeState checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        tickGate.Wait();
        try
        {
            gate.Wait();
            try
            {
                if (!society.Checkpoint.IsPaused)
                    throw new InvalidOperationException("Pause the world before selecting another world.");
                using var restored = Restore(checkpoint, providerFactory,
                    maxCognitionDispatchPerCycle);
                restored.Pause();
                foreach (var id in pendingHosted.Keys.ToArray()) CancelPendingHosted(id);
                foreach (var id in pendingWills.Keys.ToArray()) CancelPendingWill(id);
                CancelIdentityMoments();
                foreach (var id in pendingConversationTurns.Keys.ToArray())
                    CancelPendingConversationTurn(id, AgentConversationInterruption.OwnerPaused, suspendCurrent: false);
                CommitPreparedTick(restored);
            }
            finally { gate.Release(); }
        }
        finally { tickGate.Release(); }
    }

    private async ValueTask<PrivateWorldStepResult> AdvancePreparedTickAsync(
        bool deferHosted, IReadOnlyList<PendingHostedDecision> completed,
        IReadOnlyList<PendingWillDecision> completedWills, IReadOnlyList<string> activeWillIds,
        IReadOnlyDictionary<string, string> inactiveWillReasons,
        IReadOnlyList<PendingConversationTurn> completedConversationTurns,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (society.Checkpoint.IsPaused)
            {
                return new PrivateWorldStepResult(false, "paused", WorldTick, [], []);
            }

            var startingEvent = events.Count;
            var targetTick = checked(WorldTick + 1);
            StageSettlementContent();
            StageHouseContent();
            StageWarehouseContent();
            StageFarmContent();
            StageBlacksmithContent();
            StageOrnamentContent();
            StageHouseCookingContent();
            StagePotteryContent();
            StageSiloContent();
            StageTailorContent();
            StageCareContent();
            StageBuiltInContent(BusinessContent.PackageId, HouseContent.PackageId, BusinessContent.Create, "business_content_staged");
            var readyPackages = contentRegistry.GetActivationCandidates(targetTick);
            var reservationPreview = WorldAssetReservationLedger.Restore(
                assetReservations.ExportState(),
                assetReservations.Policy);
            foreach (var package in readyPackages)
            {
                var reservation = reservationPreview.TryReservePackage(
                    package.Manifest.PackageId,
                    package.Manifest.AssetReservations ?? [],
                    targetTick);
                if (!reservation.IsSuccess)
                {
                    return new PrivateWorldStepResult(
                        false,
                        $"asset_reservation_rejected:{reservation.FailureCode ?? "invalid"}",
                        WorldTick,
                        [],
                        []);
                }
            }

            assetReservations = reservationPreview;
            society.AdvanceTo(targetTick);
            if (deferHosted) await ProcessWillDecisionsAsync(completedWills, activeWillIds, inactiveWillReasons);
            var previousClimate = worldSystems.Climate;
            var campPosition = WeatherAnchor;
            var previousCampWeather = WeatherAt(campPosition);
            // Old saves retain their exact active daily weather until the first
            // resumed tick imports it into an episode. Loading alone changes nothing.
            worldSystems = RegionalWeatherRules.Initialize(worldSystems, map);
            worldSystems = WorldSystemsRules.AdvanceOneTick(worldSystems);
            SyncEcologyResourceStates();
            var campWeather = WeatherAt(campPosition);
            if (previousClimate.Season != worldSystems.Climate.Season || previousCampWeather != campWeather)
            {
                AppendEvent(
                    "weather_changed",
                    $"{worldSystems.Climate.Season.ToString().ToLowerInvariant()}:{campWeather.ToString().ToLowerInvariant()}");
            }

            var activatedWorldContent = worldContent;
            foreach (var package in readyPackages)
            {
                activatedWorldContent = ContentDefinitionPayloadCodec.ApplyPackage(
                    activatedWorldContent,
                    package.Manifest);
            }

            foreach (var activated in contentRegistry.ActivateReady(targetTick))
            {
                AppendEvent("content_activated", activated.Manifest.PackageId);
                if (activated.Manifest.PackageId == SettlementContent.PackageId)
                {
                    AddSettlementResources();
                }
                if (activated.Manifest.Definitions.Any(definition => !string.IsNullOrWhiteSpace(definition.PayloadJson)))
                {
                    AppendEvent(
                        "content_definitions_activated",
                        $"{activated.Manifest.PackageId}:buildings={activatedWorldContent.Buildings.Count}:recipes={activatedWorldContent.Recipes.Count}");
                }
            }
            worldContent = activatedWorldContent;
            if (targetTick == 1 && contentRegistry.ExportState().Packages.Any(package =>
                    package.Manifest.PackageId == SettlementContent.PackageId &&
                    package.Lifecycle == ContentPackageLifecycle.Active && package.ActivationTick == 0))
                AddSettlementResources();
            CancelUnavailableWorkers();
            ProcessBuildingExpansions(targetTick);
            ProcessProduction(targetTick);
            MaintainFarmFields();

            WearEquippedClothing();
            AdvanceSettlementSurvival();
            AdvanceMedicalTreatments();
            MaintainSettlementTrades();
            MaintainBusinessTrades();
            DrainNeeds();
            AdvanceMedicalTreatments();
            RemoveDeadPhysicalState();
            CancelFieldWorkForUnavailableWorkers();
            AdvanceSettlementCouncil();
            MaintainLessons();
            MaintainPartnerships();
            MaintainHousing();
            MaintainParenthood();
            MaintainDependentCare();
            DiscoverIdentityMoments();
            UpdateConversationsForTick(targetTick);
            EnqueueDueCognition();
            var deferredDecisions = new List<SocietyCognitionDispatchResult>();
            if (deferHosted)
            {
                foreach (var item in completed.OrderBy(
                             item => item.Request.Observation.InhabitantId, StringComparer.Ordinal))
                {
                    var id = item.Request.Observation.InhabitantId;
                    if (!inhabitants.TryGetValue(id, out var physical)) continue;
                    var outcome = await item.Task.ConfigureAwait(false);
                    var legal = CreateCandidates(id, physical).Select(candidate => candidate.Id)
                        .ToHashSet(StringComparer.Ordinal);
                    var decision = society.CompleteDeferredCognition(item.Request, outcome.Response,
                        outcome.Failure, legal);
                    if (decision is not null)
                    {
                        RecordModelCompletion(id, decision.Admission, outcome.Failure);
                        ApplyPersonalIdentityChoice(id, item.Request, outcome.Response, decision.Admission);
                        if (decision.Admission.Accepted && !decision.Admission.FellBack &&
                            outcome.Response is
                            {
                                Provider: DecisionProviderKind.LargeLanguageModel,
                                PrivateThought: { } thought
                            } && inhabitants.TryGetValue(id, out var thinking))
                        {
                            inhabitants[id] = thinking with
                            {
                                RecentThoughts = (thinking.RecentThoughts ?? [])
                                    .Append(new PlaytestPrivateThought(targetTick, thought))
                                    .TakeLast(MaximumRecentThoughts).ToArray(),
                            };
                            checkpointSchemaVersion = StateSchemaVersion;
                        }
                        if (outcome.Response is { } response)
                            ApplyChosenNameOutcome(item.Request, response, decision.Admission);
                        CloseUnresolvedNameRetry(item.Request);
                        deferredDecisions.Add(decision);
                        AppendEvent("hosted_decision_completed", $"{id}:{decision.Admission.Outcome}");
                    }
                    else
                    {
                        RecordModelAttempt(id, "canceled");
                        AppendEvent("hosted_decision_discarded", id);
                    }
                }
            }
            var dispatch = deferHosted
                ? await society.DispatchDeterministicCognitionAsync(cancellationToken).ConfigureAwait(false)
                : await society.DispatchCognitionAsync(cancellationToken).ConfigureAwait(false);
            var decisions = deferredDecisions.Concat(dispatch.Decisions)
                .OrderBy(item => item.InhabitantId, StringComparer.Ordinal).ToArray();
            foreach (var decision in decisions)
            {
                ApplyDecision(decision);
            }
            var waiting = deferHosted ? society.PendingHostedInhabitantIds() : new HashSet<string>(StringComparer.Ordinal);
            ApplyContinuingIntentions(decisions.Select(item => item.InhabitantId).Concat(waiting));
            AdvanceMedicalTreatments();
            if (deferHosted) ApplySafeRoutinesWhileWaiting(waiting);
            AdvanceBridgeTraffic();

            AppendEvent("tick_advanced", targetTick.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var newEvents = events.Skip(startingEvent).ToArray();
            return new PrivateWorldStepResult(true, "advanced", targetTick, decisions, newEvents)
            {
                MemoryCompactionTransitions = memoryCompactionTransitions.ToArray(),
            };
        }
        finally
        {
            gate.Release();
        }
    }

}
