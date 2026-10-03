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
    private void EnqueueDueCognition()
    {
        var cognitionState = society.Capture().Cognition;
        var runtimes = cognitionState.Runtimes
            .ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
        var namingRetries = cognitionState.Queue
            .Where(entry => entry.TriggerIds.Contains(
                SocietyCognitionScheduler.NameRetryTriggerId, StringComparer.Ordinal))
            .Select(entry => entry.InhabitantId)
            .ToHashSet(StringComparer.Ordinal);
        var waitingHosted = society.PendingHostedInhabitantIds();
        var waitingOrderDecisions = cognitionState.Queue
            .Where(entry => waitingHosted.Contains(entry.InhabitantId) &&
                entry.Observation.OperativeOrderInstructionId is not null &&
                IsOrderDecisionObservationCurrent(entry.Observation))
            .Select(entry => entry.InhabitantId)
            .ToHashSet(StringComparer.Ordinal);
        var staleOrderDecisions = cognitionState.Queue
            .Where(entry => waitingHosted.Contains(entry.InhabitantId) &&
                entry.Observation.OperativeOrderInstructionId is not null &&
                !IsOrderDecisionObservationCurrent(entry.Observation))
            .Select(entry => entry.InhabitantId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var inhabitant in society.Checkpoint.Inhabitants
                     .Where(item => item.Status == SocietyInhabitantStatus.Active)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (inhabitant.AgeBand == SocietyAgeBand.Infant)
            {
                continue;
            }
            // Submission already closes these. Recognition can change between
            // versions, so orders that are already waiting follow the same rule.
            CloseOrdersNotUnderstood(inhabitant.Id);
            var physical = inhabitants[inhabitant.Id];
            var operativeOrder = PendingInstructionFor(inhabitant.Id);
            if (waitingOrderDecisions.Contains(inhabitant.Id) && !HasNewObserverGuidanceFor(inhabitant.Id))
                continue;
            if (HasWaitingIdentityMoment(physical) && !NeedsUrgentFood(physical) && !NeedsUrgentWarmth(physical) &&
                !IsConversationBusy(inhabitant.Id))
                continue;
            if (FarmWorkFor(inhabitant.Id) is not null && operativeOrder is null &&
                !NeedsUrgentFood(physical) && !NeedsUrgentWarmth(physical) &&
                !ShouldDispatchConversationChoice(inhabitant.Id))
                continue;
            if (operativeOrder is not null && physical.Project is { Stage: not ("completed" or "cancelled") } orderedProject)
            {
                SetProject(inhabitant.Id, orderedProject with { Stage = "paused", Blocker = "Following an owner order." });
                physical = inhabitants[inhabitant.Id];
            }
            if (physical.Project is { Stage: not ("completed" or "cancelled") } project &&
                operativeOrder is null &&
                (NeedsUrgentFood(physical) || NeedsUrgentWarmth(physical) && !IsProtectiveProject(project)))
            {
                SetProject(inhabitant.Id, project with
                {
                    Stage = "paused",
                    Blocker = NeedsUrgentWarmth(physical) ? "Seeking warmth" : "Meeting food needs",
                });
                physical = inhabitants[inhabitant.Id];
            }
            if (IsConversationBusy(inhabitant.Id) && !ShouldDispatchConversationChoice(inhabitant.Id))
                continue;
            var conversationChoiceContext = ConversationChoiceContextFor(inhabitant.Id);
            var candidates = CreateCandidates(inhabitant.Id, physical)
                .Select(candidate => candidate with { DestinationName = DestinationNameForModel(candidate.DestinationId) })
                .ToList();
            if (PauseExpiredUnfillableHouseholdRecipeProject(inhabitant.Id))
            {
                physical = inhabitants[inhabitant.Id];
                candidates = CreateCandidates(inhabitant.Id, physical)
                    .Select(candidate => candidate with { DestinationName = DestinationNameForModel(candidate.DestinationId) })
                    .ToList();
            }
            var current = runtimes[inhabitant.Id].CurrentIntention;
            if (!namingRetries.Contains(inhabitant.Id) && !staleOrderDecisions.Contains(inhabitant.Id) &&
                !NeedsCognition(inhabitant.Id, current, candidates, conversationChoiceContext))
            {
                continue;
            }

            var generation = checked((int)(WorldTick + 1));
            var checkpoint = society.Checkpoint;
            var compaction = (checkpoint.MemoryCompactions ?? [])
                .SingleOrDefault(item => item.OwnerId == inhabitant.Id);
            var retrievedMemories = PrivateWorldMemoryRetrieval.Retrieve(
                checkpoint.Memories,
                checkpoint.Beliefs ?? [],
                compaction,
                inhabitant.Id,
                WorldTick,
                candidates);
            var observerGuidance = ObserverGuidanceFor(inhabitant.Id);
            var requiresPersonalProvider = checkpoint.Births.Any(birth => birth.ChildId == inhabitant.Id);
            var knownMapFacts = KnownMapFactsForCognition(inhabitant.Id);
            var self = new CognitionSelfContext(inhabitant.Id, inhabitant.Name, inhabitant.AgeBand.ToString(),
                physical.Personality, physical.Aspiration, inhabitant.HouseholdId,
                physical.Survival?.WarmthBasisPoints, physical.Survival?.IllnessBasisPoints,
                physical.RecentThoughts is { Count: > 0 } thoughts ? thoughts[^1].Text : null,
                checkpoint.Households.SingleOrDefault(item => item.Id == inhabitant.HouseholdId)?.Name,
                towns.SingleOrDefault(item => item.ResidentIds.Contains(inhabitant.Id, StringComparer.Ordinal))?.Name,
                HousingNote(inhabitant.Id), EquipmentNote(inhabitant.Id), ContinuityNote(inhabitant.Id),
                DepartureNote: DepartureNote(inhabitant.Id), CivicNote: CivicNote(inhabitant.Id),
                MedicalCareNote: MedicalCareNoteCore(inhabitant.Id), TownMembershipNote: TownMembershipNote(inhabitant.Id),
                ToolMakingRequestNote: ToolMakingRequestNoteCore(inhabitant.Id));
            var observation = new InhabitantObservation(
                inhabitant.Id,
                WorldTick,
                society.Checkpoint.RunEpoch,
                generation,
                ObservationDigest(inhabitant.Id, checkpoint.WorldId, physical, candidates, retrievedMemories,
                    [], knownMapFacts, self, observerGuidance),
                physical.HungerBasisPoints,
                candidates,
                NeedsName: inhabitant.NeedsName,
                RequiresPersonalProvider: requiresPersonalProvider,
                RetrievedMemories: retrievedMemories,
                KnownMapFacts: knownMapFacts, Self: self,
                NeedsPersonality: physical.IdentityChoicePending,
                NeedsAspiration: physical.IdentityChoicePending)
            {
                WorldId = checkpoint.WorldId,
                ObserverGuidance = observerGuidance,
                ConversationChoiceContext = conversationChoiceContext,
                OperativeOrderInstructionId = operativeOrder?.InstructionId,
            };
            if (jevEnabled && !requiresPersonalProvider && providerFactory is not null)
            {
                try
                {
                    var provider = providerFactory(inhabitant.Id);
                    if (provider.KindFor(observation) == DecisionProviderKind.Jev)
                    {
                        var memoryCandidates = PrivateWorldMemoryRetrieval.Unassessed(
                            checkpoint.Memories,
                            checkpoint.Beliefs ?? [],
                            compaction,
                            inhabitant.Id,
                            WorldTick);
                        if (memoryCandidates.Count > 0)
                        {
                            observation = observation with
                            {
                                MemoryCompactionCandidates = memoryCandidates,
                                ObservationDigest = ObservationDigest(
                                    inhabitant.Id, checkpoint.WorldId, physical, candidates, retrievedMemories,
                                    memoryCandidates, knownMapFacts, self, observerGuidance),
                            };
                        }
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Optional memory assistance cannot prevent the local
                    // retrieval path from supplying safe existing records.
                }
            }
            var accepted = society.EnqueueCognition(new SocietyCognitionScheduleEntry(
                $"tick:{WorldTick}:{inhabitant.Id}",
                inhabitant.Id,
                PriorityFor(physical),
                WorldTick,
                ["routine_tick"],
                observation));
            if (!accepted)
            {
                AppendEvent("cognition_backpressure", inhabitant.Id);
            }
            else
            {
                foreach (var message in observerGuidance)
                {
                    var instruction = instructionsByIdempotency.Values.SingleOrDefault(item =>
                        item.InstructionId == message.InstructionId);
                    if (instruction is not null && instruction.GuidancePromptedTick is null)
                    {
                        instructionsByIdempotency[instruction.IdempotencyKey] = instruction with
                        {
                            GuidancePromptedTick = WorldTick,
                        };
                    }
                }
                if (observerGuidance.Length > 0)
                    checkpointSchemaVersion = StateSchemaVersion;
                inhabitants[inhabitant.Id] = inhabitants[inhabitant.Id] with
                {
                    LastDecisionContext = DecisionContext(physical, candidates, conversationChoiceContext),
                };
            }
        }
    }

    private bool IsOrderDecisionObservationCurrent(InhabitantObservation observation) =>
        observation.WorldId == society.Checkpoint.WorldId &&
        observation.RunEpoch == society.Checkpoint.RunEpoch &&
        (observation.OperativeOrderInstructionId == PendingInstructionFor(observation.InhabitantId)?.InstructionId ||
            IsFinishedOrderDecisionAwaitingReply(observation));

    private bool IsFinishedOrderDecisionAwaitingReply(InhabitantObservation observation)
    {
        if (observation.OperativeOrderInstructionId is not { } orderId ||
            observation.WorldId != society.Checkpoint.WorldId ||
            observation.RunEpoch != society.Checkpoint.RunEpoch)
            return false;
        var instruction = instructionsByIdempotency.Values.SingleOrDefault(item =>
            item.InstructionId == orderId && item.TargetInhabitantId == observation.InhabitantId &&
            item.Kind == OwnerInstructionKind.MustDo && item.Order?.Status == "finished" &&
            item.ObserverReply is null && completedInstructionIds.Contains(orderId));
        // Local work can finish while its original personal reply is held.
        // Keep that exact message available for acknowledgement, never execution.
        return instruction is not null && observation.ObserverGuidance?.Any(message =>
            message.InstructionId == orderId && message.IssuerId == instruction.IssuerId &&
            message.TargetInhabitantId == instruction.TargetInhabitantId && message.Kind == "must_do" &&
            message.Text == instruction.Text && message.SubmittedTick == instruction.SubmittedTick &&
            message.RunEpoch == instruction.RunEpoch && message.SubmissionSequence == instruction.SubmissionSequence &&
            message.UnderstoodTask == UnderstoodTaskFor(instruction.Order?.Action) && message.ReplyAllowed) == true;
    }

    private bool NeedsCognition(
        string inhabitantId,
        CognitionIntention? current,
        List<CognitionCandidate> candidates,
        string? conversationChoiceContext)
    {
        if (conversationChoiceContext is not null &&
            !HasPromptedConversationChoice(inhabitants[inhabitantId].LastDecisionContext, conversationChoiceContext))
        {
            return true;
        }

        // A new instruction prompts one fresh decision. If it cannot progress
        // yet, it waits for the agent's usual decisions instead of requesting
        // another (possibly paid) decision on every tick.
        if (HasNewObserverGuidanceFor(inhabitantId))
        {
            return true;
        }

        if (HasNewGuardianCandidate(inhabitants[inhabitantId].LastDecisionContext, candidates))
        {
            return true;
        }

        // Cancellation or replacement ends an order-bound intention even when
        // its generic food candidate remains legal for ordinary personal plans.
        if (current?.OperativeOrderInstructionId is { } orderId &&
            orderId != PendingInstructionFor(inhabitantId)?.InstructionId)
            return true;

        if (PendingInstructionFor(inhabitantId) is { Order: { } order })
        {
            return current is null || checked(WorldTick - current.WorldTick) >= CognitionReevaluationIntervalTicks;
        }

        if (CanContinueEquipmentRepair(inhabitantId) || CanContinueLesson(inhabitantId) || CanContinueProject(inhabitants[inhabitantId]))
        {
            return false;
        }

        if (candidates.Count == 1 && candidates[0].Id == "safe_idle")
        {
            return false;
        }

        if (current is null)
        {
            return true;
        }

        // Finishing a project frees its inputs and work site for the next
        // craft. Reconsider once instead of repeating the old recipe before
        // the usual reevaluation interval has elapsed.
        if (inhabitants[inhabitantId].Project is { Stage: "completed" } completed &&
            current.CandidateId == completed.CandidateId && current.WorldTick <= completed.LastTransitionTick)
        {
            return true;
        }

        if (!candidates.Any(candidate => candidate.Id == current.CandidateId))
        {
            return true;
        }

        if (current.CandidateId is "harvest_food" or "consume_food")
        {
            return true;
        }

        if (current.CandidateId == "safe_idle")
        {
            var physical = inhabitants[inhabitantId];
            return DecisionContext(physical, candidates, conversationChoiceContext) != physical.LastDecisionContext ||
                checked(WorldTick - current.WorldTick) >= 300;
        }

        return checked(WorldTick - current.WorldTick) >= CognitionReevaluationIntervalTicks;
    }

    private string DecisionContext(
        PlaytestInhabitantState state,
        List<CognitionCandidate> candidates,
        string? conversationChoiceContext = null)
    {
        var context = $"{NeedsUrgentFood(state)}:{NeedsUrgentWarmth(state)}:" +
            // Optional advance permission remains available on ordinary decisions;
            // a healthy idle agent need not wake merely because someone walks past.
            string.Join('|', candidates.Where(candidate =>
                    !candidate.Id.StartsWith(MedicalAllowPrefix, StringComparison.Ordinal) ||
                    state.Survival is { IllnessBasisPoints: >= 2_500 })
                .Select(candidate => candidate.Id).Order(StringComparer.Ordinal));
        context += "|guardian_candidates=" + GuardianCandidateContext(candidates);
        return conversationChoiceContext is null
            ? context
            : $"{context}|conversation_choice={ConversationChoiceContextDigest(conversationChoiceContext)}";
    }

    private static string GuardianCandidateContext(IEnumerable<CognitionCandidate> candidates)
    {
        var ids = candidates.Where(candidate => candidate.Id.StartsWith("guardian_accept:", StringComparison.Ordinal))
            .Select(candidate => Convert.ToBase64String(Encoding.UTF8.GetBytes(candidate.Id))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_'))
            .Order(StringComparer.Ordinal).ToArray();
        return string.Join(',', ids);
    }

    private static bool HasNewGuardianCandidate(string? lastDecisionContext, IEnumerable<CognitionCandidate> candidates)
    {
        var previouslyOffered = GuardianCandidateContextFromLastDecision(lastDecisionContext);
        return candidates.Where(candidate => candidate.Id.StartsWith("guardian_accept:", StringComparison.Ordinal))
            .Select(candidate => Convert.ToBase64String(Encoding.UTF8.GetBytes(candidate.Id))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_'))
            .Any(candidate => !previouslyOffered.Contains(candidate));
    }

    private static HashSet<string> GuardianCandidateContextFromLastDecision(string? context)
    {
        const string marker = "|guardian_candidates=";
        if (context is null) return new(StringComparer.Ordinal);
        var start = context.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return new(StringComparer.Ordinal);
        start += marker.Length;
        var end = context.IndexOf('|', start);
        var encodedIds = end < 0 ? context[start..] : context[start..end];
        return encodedIds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasPromptedConversationChoice(string? lastDecisionContext, string conversationChoiceContext) =>
        lastDecisionContext?.EndsWith(
            $"|conversation_choice={ConversationChoiceContextDigest(conversationChoiceContext)}",
            StringComparison.Ordinal) == true;

    private static string ConversationChoiceContextDigest(string conversationChoiceContext) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conversationChoiceContext)));

    private HashSet<string> ApplyContinuingIntentions(
        IEnumerable<string> dispatchedInhabitantIds,
        IEnumerable<string> pendingHostedInhabitantIds)
    {
        var dispatched = dispatchedInhabitantIds.ToHashSet(StringComparer.Ordinal);
        var pendingHosted = pendingHostedInhabitantIds.ToHashSet(StringComparer.Ordinal);
        var orderActorsHandledThisTick = new HashSet<string>(StringComparer.Ordinal);
        var runtimes = society.Capture().Cognition.Runtimes
            .ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
        foreach (var inhabitant in society.Checkpoint.Inhabitants
                     .Where(item => item.Status == SocietyInhabitantStatus.Active)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (dispatched.Contains(inhabitant.Id) || !inhabitants.TryGetValue(inhabitant.Id, out var state))
            {
                continue;
            }
            var order = PendingInstructionFor(inhabitant.Id);
            if (pendingHosted.Contains(inhabitant.Id) && order is null)
                continue;
            if (IsConversationBusy(inhabitant.Id))
            {
                continue;
            }
            if (inhabitant.AgeBand == SocietyAgeBand.Infant)
            {
                continue;
            }
            if (order is not null)
            {
                orderActorsHandledThisTick.Add(inhabitant.Id);
                var orderCandidate = OrderCandidateFor(order, state);
                var urgentCandidate = NeedsUrgentFood(state) || NeedsUrgentWarmth(state)
                    ? UrgentSurvivalCandidateFor(inhabitant.Id, state)
                    : null;
                if (ShouldInterruptOrder(state, order, orderCandidate, urgentCandidate))
                {
                    SetOrderStatus(order, "interrupted", "Food or warmth needs come first.");
                    if (urgentCandidate is not null)
                        ApplyCandidate(inhabitant.Id, state, urgentCandidate.Id, reportIdle: false);
                    continue;
                }
                if (order.Order!.WaitForDecisionAfterFailure)
                {
                    if (urgentCandidate is not null)
                        ApplyCandidate(inhabitant.Id, state, urgentCandidate.Id, reportIdle: false);
                    continue;
                }
                if (orderCandidate is not null)
                {
                    ExecuteOrderStep(order, state, orderCandidate);
                    continue;
                }
                SetOrderStatus(order, "blocked", OrderBlockedReason(order, state));
                continue;
            }
            if (CanContinueEquipmentRepair(inhabitant.Id))
            {
                ContinueEquipmentRepair(inhabitant.Id);
                continue;
            }
            if (CanContinueLesson(inhabitant.Id))
            {
                ContinueLesson(inhabitant.Id);
                continue;
            }
            if (ContinueFarmWork(inhabitant.Id)) continue;
            if (CanContinueProject(state))
            {
                ContinueProject(inhabitant.Id, state);
                continue;
            }
            if (runtimes[inhabitant.Id].CurrentIntention is not { } intention)
            {
                continue;
            }

            if (intention.OperativeOrderInstructionId is not null &&
                intention.OperativeOrderInstructionId != order?.InstructionId)
                continue;

            if (intention.CandidateId.StartsWith("civic|", StringComparison.Ordinal))
            {
                // Formal civic acts require a fresh admitted personal choice. Only the physical trip
                // toward the notice place continues locally between ordinary model turns.
                var civic = intention.CandidateId.Split('|');
                if (civic.Length == 5 && civic[2] == "visit" && intention.Provider == DecisionProviderKind.LargeLanguageModel)
                    ContinueTownCivicVisit(inhabitant.Id, intention.CandidateId);
                continue;
            }
            if (IsOrnamentCandidate(intention.CandidateId))
            {
                if (intention.Provider == DecisionProviderKind.LargeLanguageModel)
                    ContinueOrnamentWalk(inhabitant.Id, intention.CandidateId);
                continue;
            }
            if (!CreateCandidates(inhabitant.Id, state).Any(candidate => candidate.Id == intention.CandidateId)) continue;
            ApplyCandidate(inhabitant.Id, state, intention.CandidateId, reportIdle: false);
        }

        return orderActorsHandledThisTick;
    }

    private void ApplySafeRoutinesWhileWaiting(
        IEnumerable<string> waitingIds,
        IEnumerable<string> orderActorsHandledThisTick)
    {
        var handledOrders = orderActorsHandledThisTick.ToHashSet(StringComparer.Ordinal);
        var safe = new HashSet<string>(StringComparer.Ordinal)
        {
            "consume_food", "collect_shared_food", "take_food_from_pot", "make_room_for_food", "recover_household_delivery",
            "harvest_food", "seek_food",
            "wear_clothing", "tend_fire", "seek_warmth",
        };
        foreach (var id in waitingIds.OrderBy(item => item, StringComparer.Ordinal))
        {
            if (!inhabitants.TryGetValue(id, out var state)) continue;
            if (handledOrders.Contains(id)) continue;
            if (PendingInstructionFor(id) is not null) continue;
            if (!NeedsUrgentFood(state) && !NeedsUrgentWarmth(state))
                continue;
            var candidate = CreateCandidates(id, state, restrictForOrder: false)
                .Where(item => safe.Contains(item.Id))
                .OrderBy(item => item.DeterministicPriority)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate is not null) ApplyCandidate(id, state, candidate.Id, reportIdle: false);
        }
    }

    private void ApplyDecision(SocietyCognitionDispatchResult decision)
    {
        if (decision.Admission.Accepted && !decision.Admission.FellBack &&
            decision.Admission.Intention?.Provider == DecisionProviderKind.Jev &&
            decision.Admission.MemoryCompactionScores is { Count: > 0 } memoryScores)
        {
            ApplyMemoryCompaction(decision.InhabitantId, memoryScores);
        }

        if (!decision.Admission.Accepted || decision.Admission.Intention is null ||
            !inhabitants.TryGetValue(decision.InhabitantId, out var state))
        {
            AppendEvent("cognition_rejected", $"{decision.InhabitantId}:{decision.Admission.Outcome}");
            return;
        }

        _ = ApplyObserverGuidanceResult(decision.InhabitantId, decision.Admission);
        if (ApplyMedicalConsentDecision(decision) || ApplyOrnamentDecision(decision))
            return;
        var pendingInstruction = PendingInstructionFor(decision.InhabitantId);
        var candidateId = decision.Admission.Intention.CandidateId;
        if (decision.Admission.Intention.OperativeOrderInstructionId != pendingInstruction?.InstructionId)
        {
            AppendEvent("instruction_order_stale_decision", decision.InhabitantId);
            return;
        }

        if (pendingInstruction is { } order)
        {
            if (decision.Admission.FellBack)
            {
                SetOrderStatus(order, "blocked", "The order will try again after a short wait.", waitForDecision: true);
                ApplyCandidate(decision.InhabitantId, state, candidateId, reportIdle: true);
            }
            else
            {
                var orderCandidate = OrderCandidateFor(order, state);
                var urgentCandidate = NeedsUrgentFood(state) || NeedsUrgentWarmth(state)
                    ? UrgentSurvivalCandidateFor(decision.InhabitantId, state)
                    : null;
                if (ShouldInterruptOrder(state, order, orderCandidate, urgentCandidate))
                {
                    SetOrderStatus(order, "interrupted", "Food or warmth needs come first.");
                    if (urgentCandidate is not null)
                        ApplyCandidate(decision.InhabitantId, state, urgentCandidate.Id, reportIdle: true);
                }
                else if (orderCandidate is not null)
                {
                    ExecuteOrderStep(order, state, orderCandidate);
                }
                else
                {
                    SetOrderStatus(order, "blocked", OrderBlockedReason(order, state));
                    ApplyCandidate(decision.InhabitantId, state, "safe_idle", reportIdle: true);
                }
            }
        }
        else
        {
            if (candidateId.StartsWith("civic|", StringComparison.Ordinal))
            {
                if (!decision.Admission.FellBack && decision.Admission.Intention.Provider == DecisionProviderKind.LargeLanguageModel)
                    ApplyTownCivicCandidate(decision.InhabitantId, candidateId, decision.Admission.CivicProposal,
                        decision.Admission.CivicBallot, decision.Admission.CivicLandTiles);
            }
            else if (!ApplyToolMakingRequestDecision(decision))
                ApplyCandidate(decision.InhabitantId, state, candidateId, reportIdle: true);
        }

    }

    private List<string> ApplyObserverGuidanceResult(
        string inhabitantId,
        CognitionAdmissionResult admission)
    {
        if (!admission.Accepted || admission.FellBack ||
            admission.Intention?.Provider != DecisionProviderKind.LargeLanguageModel ||
            admission.ObserverGuidance is not { } result ||
            result.WorldId != society.Checkpoint.WorldId ||
            result.InhabitantId != inhabitantId ||
            result.RunEpoch != admission.Intention.RunEpoch ||
            result.DecisionGeneration != admission.Intention.DecisionGeneration ||
            result.ObservationDigest != admission.Intention.ObservationDigest ||
            !inhabitants.ContainsKey(inhabitantId))
            return [];

        var replies = result.Replies.ToDictionary(reply => reply.InstructionId, StringComparer.Ordinal);
        var completedSuggestions = new List<string>();
        foreach (var message in result.Messages)
        {
            var instruction = instructionsByIdempotency.Values.SingleOrDefault(item =>
                item.InstructionId == message.InstructionId);
            if (instruction is null || instruction.IssuerId != message.IssuerId ||
                instruction.TargetInhabitantId != inhabitantId ||
                instruction.TargetInhabitantId != message.TargetInhabitantId ||
                ToWireValue(instruction.Kind) != message.Kind || instruction.Text != message.Text ||
                instruction.SubmittedTick != message.SubmittedTick || instruction.RunEpoch != message.RunEpoch ||
                instruction.SubmissionSequence != message.SubmissionSequence ||
                (instruction.Kind == OwnerInstructionKind.MustDo &&
                 UnderstoodTaskFor(instruction.Order?.Action) != message.UnderstoodTask) ||
                (instruction.Kind == OwnerInstructionKind.Suggestive && message.UnderstoodTask is not null) ||
                (instruction.ObserverReply is null) != message.ReplyAllowed ||
                completedInstructionIds.Contains(instruction.InstructionId) &&
                    !(instruction.Kind == OwnerInstructionKind.MustDo && instruction.Order?.Status == "finished"))
                continue;

            var observerReply = replies.TryGetValue(instruction.InstructionId, out var reply)
                ? reply.Text
                : instruction.ObserverReply;
            instructionsByIdempotency[instruction.IdempotencyKey] = instruction with
            {
                ObservedTick = instruction.ObservedTick ?? admission.Intention.WorldTick,
                ObserverReply = observerReply,
            };
            checkpointSchemaVersion = StateSchemaVersion;
            if (instruction.Kind == OwnerInstructionKind.Suggestive &&
                completedInstructionIds.Add(instruction.InstructionId))
                completedSuggestions.Add(instruction.InstructionId);
        }

        return completedSuggestions;
    }

    private void ApplyCandidate(
        string inhabitantId,
        PlaytestInhabitantState state,
        string candidateId,
        bool reportIdle)
    {
        if (IsOrnamentCandidate(candidateId)) return;
        if (candidateId.StartsWith(ToolRequestPrefix, StringComparison.Ordinal))
        {
            ApplyToolMakingRequestCandidate(inhabitantId, state, candidateId, reportIdle);
            return;
        }
        if (candidateId.StartsWith("talk:", StringComparison.Ordinal) ||
            candidateId.StartsWith("conversation_", StringComparison.Ordinal))
        {
            if (!ApplyConversationCandidate(inhabitantId, candidateId))
                AppendEvent("conversation_action_rejected", $"{inhabitantId}:{candidateId.Split(':')[0]}");
            else if (inhabitants[inhabitantId].Equipment?.Repair is not null)
                CancelEquipmentRepair(inhabitantId);
            return;
        }
        if (!AgePermitsCandidate(inhabitantId, candidateId))
        {
            AppendEvent("age_action_rejected", $"{inhabitantId}:{candidateId}");
            return;
        }
        if (inhabitants[inhabitantId].Equipment?.Repair is not null && candidateId != "repair_equipment")
        {
            CancelEquipmentRepair(inhabitantId);
            state = inhabitants[inhabitantId];
        }
        if (PendingInstructionFor(inhabitantId) is null && ContinueFarmWork(inhabitantId)) return;
        if (candidateId.StartsWith("medical_", StringComparison.Ordinal))
        {
            ApplyMedicalCandidate(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId.StartsWith("farm:", StringComparison.Ordinal))
        {
            ApplyFieldCandidate(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId.StartsWith("child_", StringComparison.Ordinal))
        {
            ApplyChildCandidate(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId.StartsWith("guardian_", StringComparison.Ordinal))
        {
            ApplyDependentCareCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("parent_", StringComparison.Ordinal) || candidateId.StartsWith("care:", StringComparison.Ordinal))
        {
            ApplyParenthoodCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("partner_", StringComparison.Ordinal))
        {
            ApplyFamilyCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("household_", StringComparison.Ordinal))
        {
            ApplyHousingCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("learn:", StringComparison.Ordinal) || candidateId.StartsWith("lesson_", StringComparison.Ordinal))
        {
            ApplyLearningCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("civic|", StringComparison.Ordinal))
        {
            // Civic choices execute only through the personal admission path above.
            return;
        }
        if (candidateId.StartsWith("council_", StringComparison.Ordinal))
        {
            ApplyCouncilCandidate(inhabitantId, candidateId);
            return;
        }
        if (candidateId.StartsWith("trade_", StringComparison.Ordinal))
        {
            ApplyTradeCandidate(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId.StartsWith("business_", StringComparison.Ordinal))
        {
            ApplyBusinessCandidate(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId == "haul_household_stock")
        {
            HaulHouseholdStock(inhabitantId, state);
            return;
        }
        if (candidateId == "recover_household_delivery")
        {
            RecoverHouseholdDelivery(inhabitantId, state);
            return;
        }
        if (candidateId == "store_household_food")
        {
            StoreHouseholdFood(inhabitantId, state);
            return;
        }
        if (candidateId == "make_room_for_food")
        {
            MakeRoomForFood(inhabitantId, state);
            return;
        }
        if (candidateId == "store_town_resources")
        {
            StoreTownResources(inhabitantId, state);
            return;
        }
        if (candidateId == "haul_farm_grain")
        {
            HaulFarmGrain(inhabitantId, state);
            return;
        }
        if (candidateId == "haul_farm_flour")
        {
            HaulFarmFlour(inhabitantId, state);
            return;
        }
        if (candidateId == "haul_smith_input")
        {
            HaulBlacksmithInput(inhabitantId, state);
            return;
        }
        if (candidateId == "gather_smith_ore")
        {
            GatherBlacksmithOre(inhabitantId, state);
            return;
        }
        if (candidateId.StartsWith(GatherBlacksmithInputPrefix, StringComparison.Ordinal))
        {
            GatherBlacksmithInput(inhabitantId, state, candidateId[GatherBlacksmithInputPrefix.Length..]);
            return;
        }
        if (candidateId == "deliver_smith_ore")
        {
            DeliverBlacksmithOre(inhabitantId, state);
            return;
        }
        if (candidateId is "collect_wooden_axe" or "collect_wooden_pickaxe" or "collect_wooden_hoe")
        {
            var kind = candidateId == "collect_wooden_axe" ? "wooden_axe" :
                candidateId == "collect_wooden_hoe" ? FarmFieldRules.Hoe : "wooden_pickaxe";
            if (MayCollectToolFamily(inhabitantId, ToolProgressionRules.Find(kind)!.Family))
                CollectEquipment(inhabitantId, state, kind);
            return;
        }
        if (candidateId.StartsWith(CollectToolPrefix, StringComparison.Ordinal))
        {
            var kind = candidateId[CollectToolPrefix.Length..];
            if (ToolProgressionRules.Find(kind) is { } tool && MayCollectToolFamily(inhabitantId, tool.Family))
                CollectEquipment(inhabitantId, state, kind);
            return;
        }
        if (candidateId.StartsWith(RepairToolPrefix, StringComparison.Ordinal))
        {
            RepairTool(inhabitantId, state, candidateId[RepairToolPrefix.Length..]);
            return;
        }
        if (candidateId.StartsWith(RareMiningPrefix, StringComparison.Ordinal))
        {
            GatherRareMaterial(inhabitantId, state, candidateId[RareMiningPrefix.Length..]);
            return;
        }
        if (candidateId == "knowledge_continue" || candidateId.StartsWith(KnowledgeWritePrefix, StringComparison.Ordinal) ||
            candidateId.StartsWith(KnowledgeCopyPrefix, StringComparison.Ordinal) || candidateId.StartsWith(KnowledgeReadPrefix, StringComparison.Ordinal))
        {
            ApplyKnowledgeWritingCandidate(inhabitantId, state, candidateId);
            return;
        }
        if (ApplyKnowledgeStorageCandidate(inhabitantId, state, candidateId)) return;
        if (candidateId.StartsWith(KnowledgeSharePrefix, StringComparison.Ordinal))
        {
            ApplyKnowledgeShare(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId.StartsWith(SupplyWorkstationPrefix, StringComparison.Ordinal))
        {
            SupplyWorkstation(inhabitantId, state, candidateId[SupplyWorkstationPrefix.Length..]);
            return;
        }
        if (candidateId.StartsWith(ReturnEmptyVesselPrefix, StringComparison.Ordinal))
        {
            ReturnEmptyVessel(inhabitantId, state, candidateId[ReturnEmptyVesselPrefix.Length..]);
            return;
        }
        if (candidateId == "collect_water_jug")
        {
            CollectWaterJug(inhabitantId, state);
            return;
        }
        if (candidateId == "return_water_jug")
        {
            ReturnWaterJug(inhabitantId, state);
            return;
        }
        if (candidateId == "store_food_in_pot")
        {
            StoreFoodInPot(inhabitantId, state);
            return;
        }
        if (candidateId == "take_food_from_pot")
        {
            TakeFoodFromPot(inhabitantId, state);
            return;
        }
        if (candidateId.StartsWith(FillWaterJugPrefix, StringComparison.Ordinal))
        {
            FillWaterJug(inhabitantId, state, candidateId[FillWaterJugPrefix.Length..]);
            return;
        }
        if (candidateId.StartsWith(GatherBuildingMaterialPrefix, StringComparison.Ordinal))
        {
            GatherBuildingMaterial(inhabitantId, state, candidateId[GatherBuildingMaterialPrefix.Length..]);
            return;
        }
        if (candidateId.StartsWith(ExpandBuildingPrefix, StringComparison.Ordinal))
        {
            ApplyBuildingExpansionCandidate(inhabitantId, state, candidateId[ExpandBuildingPrefix.Length..]);
            return;
        }
        if (candidateId.StartsWith(InviteHouseGuestPrefix, StringComparison.Ordinal) ||
            candidateId.StartsWith(RevokeHouseGuestPrefix, StringComparison.Ordinal))
        {
            var invited = candidateId.StartsWith(InviteHouseGuestPrefix, StringComparison.Ordinal);
            var household = society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId;
            if (household is not null && HouseForHousehold(household) is { } house)
                SetHouseGuestInvitationCore(inhabitantId, house.InstanceId,
                    candidateId[(invited ? InviteHouseGuestPrefix.Length : RevokeHouseGuestPrefix.Length)..], invited);
            return;
        }
        if (candidateId.StartsWith("build:", StringComparison.Ordinal))
        {
            BeginProject(inhabitantId, state, candidateId);
            return;
        }
        if (candidateId.StartsWith("assist:", StringComparison.Ordinal))
        {
            AssistProject(inhabitantId, state, candidateId[7..]);
            return;
        }
        if (candidateId == "replant_tree")
        {
            ReplantTree(inhabitantId, state);
            return;
        }
        if (candidateId is "plant_tree" or "plant_orchard")
        {
            PlantTreeNearby(inhabitantId, state, candidateId == "plant_orchard");
            return;
        }

        switch (candidateId)
        {
            case "explore":
                Explore(inhabitantId, state);
                break;
            case "wear_clothing":
                EquipPrivateItem(inhabitantId, state, carryAid: false);
                break;
            case "equip_carry_aid":
                EquipPrivateItem(inhabitantId, state, carryAid: true);
                break;
            case "repair_equipment":
                RepairEquipment(inhabitantId, state);
                break;
            case "tend_fire":
                TendFire(inhabitantId, state);
                break;
            case "seek_warmth":
                SeekWarmth(inhabitantId, state);
                break;
            case "seek_food":
                if (AvailableFoodSource(inhabitantId, state.Position) is { } foodSource)
                    MoveToward(inhabitantId, state, foodSource.Position, "food", ResourceInteractionRange);
                break;
            case "harvest_food":
                HarvestFood(inhabitantId, state);
                break;
            case "consume_food":
                ConsumeFood(inhabitantId, state);
                break;
            case "collect_shared_food":
                CollectSharedFood(inhabitantId, state);
                break;
            default:
                if (reportIdle)
                {
                    AppendEvent("inhabitant_idle", inhabitantId);
                }
                break;
        }
    }

    private void ApplyBuildDecision(
        string inhabitantId,
        PlaytestInhabitantState state,
        string candidateId)
    {
        if (!TownConstructionCandidateIds.TryParse(candidateId, out var selection))
        {
            AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:unknown_target");
            return;
        }

        if (selection.IsBuilding)
        {
            var definition = worldContent.Buildings.SingleOrDefault(item => item.CanonicalId == selection.DefinitionId);
            var layout = CreateTownLayoutContext(inhabitantId, selection.SitePosition, definition);
            TownConstructionSiteCandidate? rankedSite = null;
            if (definition is not null)
            {
                if (selection.SitePosition is { } selectedSite)
                    TownLayoutService.TryEvaluateConstructionSite(layout, definition, selectedSite, out rankedSite);
                else
                {
                    var sites = TownLayoutService.RankConstructionSites(layout, definition);
                    rankedSite = sites.Count == 0 ? null : sites[0];
                }
            }

            if (definition is null || rankedSite is null)
            {
                AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:selected_site_no_longer_legal");
                if (selection.SitePosition is { } rejectedSite && definition is not null)
                    AppendEvent("town_layout_site_rejected",
                        $"{TownForResident(inhabitantId) ?? "none"}|{inhabitantId}|{definition.CanonicalId}|{rejectedSite.X}|{rejectedSite.Y}|site_unavailable");
                return;
            }
            var householdProperty = definition.Tags.Any(IsHouseholdBuildingTag);
            var houseOwner = householdProperty
                ? society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId : null;
            if (householdProperty && houseOwner is null)
            {
                AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:household_required");
                return;
            }
            if (definition.Tags.Contains("house", StringComparer.Ordinal) && houseOwner is not null &&
                HouseForHousehold(houseOwner) is not null)
            {
                AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:existing_house");
                return;
            }

            var position = rankedSite.Position;
            if (state.Position != position)
            {
                MoveToward(inhabitantId, state, position, "build");
                return;
            }

            var placement = PlaceBuildingCore(
                BuildInstanceId(inhabitantId, definition),
                definition.CanonicalId,
                position,
                "build_completed",
                TownForResident(inhabitantId),
                houseOwner,
                BuildingConstructionOwner(inhabitantId, definition));
            if (!placement.Applied)
            {
                AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:{placement.Failure}");
            }
            else
            {
                CreditCompletedWork(inhabitantId, "building");
            }

            return;
        }

        var recipeId = selection.DefinitionId;
        var recipe = worldContent.Recipes.SingleOrDefault(item => item.CanonicalId == recipeId);
        if (recipe is null || !TryFindRecipeSite(recipe, out var siteId, out var sitePosition, inhabitantId))
        {
            AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:no_valid_site");
            return;
        }

        if (state.Position != sitePosition)
        {
            MoveToward(inhabitantId, state, sitePosition, "build");
            return;
        }

        var started = StartProductionCore(recipe.CanonicalId, siteId, inhabitantId, "build_started");
        if (!started.Applied)
        {
            AppendEvent("build_rejected", $"{inhabitantId}:{candidateId}:{started.Failure}");
        }
    }

    private List<CognitionCandidate> CreateCandidates(
        string inhabitantId,
        PlaytestInhabitantState state,
        bool restrictForOrder = true)
    {
        var currentConversation = ConversationFor(inhabitantId);
        var candidates = currentConversation is null
            ? new List<CognitionCandidate>()
            : ConversationCandidates(inhabitantId).ToList();
        if (currentConversation is not null &&
            (currentConversation.Status is AgentConversationStatus.Ready or AgentConversationStatus.AwaitingSpeaker or AgentConversationStatus.WrapUp ||
             currentConversation.Status == AgentConversationStatus.Proposed && currentConversation.InitiatorId == inhabitantId))
        {
            if (candidates.Count == 0)
                candidates.Add(new CognitionCandidate("safe_idle", "Continue safely without starting a new task.", 100));
            return candidates;
        }
        var instruction = PendingInstructionFor(inhabitantId);
        var instructionCandidate = instruction?.Order?.Action;

        var hasFood = PreferredFood(inhabitantId, inhabitantId).Any();
        if (hasFood && state.HungerBasisPoints < ComfortableFullness)
        {
            candidates.Add(new CognitionCandidate("consume_food", "Eat one carried food item.", 0));
        }

        if (instructionCandidate == "consume_food" && hasFood &&
            state.HungerBasisPoints < ComfortableFullness &&
            !candidates.Any(item => item.Id == "consume_food"))
        {
            candidates.Add(new CognitionCandidate("consume_food", "Follow the owner's food instruction.", 0));
        }

        var foodPriority = NeedsUrgentFood(state) ? 2 : state.HungerBasisPoints < RoutineFoodSeekFullness ? 5 : 90;
        // An optional reserve remains selectable without outranking ordinary activities.
        var wantsFood = !hasFood && state.HungerBasisPoints < 7_000;
        var shouldGatherFood = wantsFood && FreeCarryCapacity(inhabitantId) > 0;
        var foodSource = shouldGatherFood || instructionCandidate is "seek_food" or "harvest_food"
            ? AvailableFoodSource(inhabitantId, state.Position) : null;
        var sharedFood = shouldGatherFood ? AvailableSharedFood(inhabitantId) : null;
        if (sharedFood is not null && contentRegistry.ExportState().Packages.Any(package =>
                package.Manifest.PackageId == StarterContent.PackageId && package.Lifecycle == ContentPackageLifecycle.Active))
        {
            candidates.Add(new CognitionCandidate("collect_shared_food",
                "Collect one available household food serving from its store, then eat it.",
                foodPriority - 1, HouseholdId));
        }
        if (shouldGatherFood && foodSource is not null &&
            IsWithinInteractionRange(state.Position, foodSource.Position, ResourceInteractionRange))
        {
            candidates.Add(new CognitionCandidate(
                "harvest_food",
                "Gather several food servings from the nearby food source.",
                foodPriority,
                foodSource.Id));
        }
        else if (shouldGatherFood && foodSource is not null)
        {
            candidates.Add(new CognitionCandidate(
                "seek_food",
                "Travel within gathering range of an available food source.",
                foodPriority,
                foodSource.Id));
        }
        else if (wantsFood && sharedFood is null)
        {
            AddMakeRoomForFoodCandidate(candidates, inhabitantId, state, foodPriority);
        }

        if (instructionCandidate == "seek_food" && foodSource is not null &&
            !candidates.Any(item => item.Id == "seek_food"))
        {
            candidates.Add(new CognitionCandidate("seek_food", "Follow the owner's travel instruction.", 0, foodSource.Id));
        }

        if (instructionCandidate == "harvest_food" &&
            foodSource is not null &&
            IsWithinInteractionRange(state.Position, foodSource.Position, ResourceInteractionRange) &&
            !candidates.Any(item => item.Id == "harvest_food"))
        {
            candidates.Add(new CognitionCandidate("harvest_food", "Follow the owner's harvest instruction.", 0, foodSource.Id));
        }

        AddSurvivalCandidates(candidates, inhabitantId, state);
        AddDependentCareCandidates(candidates, inhabitantId);
        AddMedicalCareCandidates(candidates, inhabitantId);
        if (AdultResident(inhabitantId))
        {
            AddKnowledgeCandidates(candidates, inhabitantId, state);
            AddFamilyCandidates(candidates, inhabitantId);
            AddHousingCandidates(candidates, inhabitantId);
            AddParenthoodCandidates(candidates, inhabitantId);
            AddRecoverHouseholdDeliveryCandidate(candidates, inhabitantId, state);
            AddUrgentFoodPotCandidate(candidates, inhabitantId, state);
            AddBusinessCandidates(candidates, inhabitantId);
            AddToolMakingRequestCandidates(candidates, inhabitantId);
        }
        if (!NeedsUrgentWarmth(state) && ChildResident(inhabitantId))
        {
            AddChildCandidates(candidates, inhabitantId, state);
        }
        if (!NeedsUrgentFood(state) && AdultResident(inhabitantId))
        {
            var inhabitant = society.Checkpoint.GetInhabitant(inhabitantId);
            AddBuildCandidates(candidates, inhabitant, state);
            AddBuildingExpansionCandidates(candidates, inhabitantId);
            AddHouseGuestCandidates(candidates, inhabitantId);
            AddHouseHaulCandidate(candidates, inhabitantId, state);
            AddWarehouseStockCandidate(candidates, inhabitantId, state);
            AddFarmGrainCandidate(candidates, inhabitantId, state);
            AddFarmFlourCandidate(candidates, inhabitantId, state);
            AddFieldCandidates(candidates, inhabitantId, state);
            AddBlacksmithStockCandidate(candidates, inhabitantId, state);
            AddBlacksmithOreCandidates(candidates, inhabitantId, state);
            AddWorkstationSupplyCandidate(candidates, inhabitantId);
            AddContainerCandidates(candidates, inhabitantId, state);
            AddCraftToolCandidates(candidates, inhabitantId);
            AddRareMiningCandidates(candidates, inhabitantId);
            AddProjectAssistanceCandidates(candidates, inhabitantId);
            AddForestryCandidates(candidates, inhabitantId, state);
            AddTradeCandidates(candidates, inhabitantId);
            AddCouncilCandidates(candidates, inhabitantId);
            AddTownCivicCandidates(candidates, inhabitantId);
            AddLearningCandidates(candidates, inhabitantId);
            AddExplorationCandidate(candidates, inhabitantId, state);
        }

        if (currentConversation is null)
            candidates.AddRange(ConversationCandidates(inhabitantId));
        candidates.Add(new CognitionCandidate("safe_idle", "Continue safely without starting a new task.", 100));
        if (restrictForOrder && PendingInstructionFor(inhabitantId) is { } order)
            return RestrictCandidatesForOrder(candidates, order, state);
        return candidates;
    }

    private List<CognitionCandidate> RestrictCandidatesForOrder(
        List<CognitionCandidate> candidates,
        OwnerQueuedInstruction order,
        PlaytestInhabitantState state)
    {
        var safeIdle = candidates.Single(item => item.Id == "safe_idle");
        var urgent = NeedsUrgentFood(state) || NeedsUrgentWarmth(state);
        var urgentCandidate = urgent
            ? candidates.Where(candidate => IsSurvivalCandidate(candidate.Id))
                .OrderBy(candidate => candidate.DeterministicPriority)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .FirstOrDefault()
            : null;
        var taskCandidate = OrderCandidateFor(order, state);
        if (ShouldInterruptOrder(state, order, taskCandidate, urgentCandidate))
            taskCandidate = null;
        if (taskCandidate is null)
        {
            return candidates.Where(item => item.Id == "safe_idle" || urgent && IsSurvivalCandidate(item.Id))
                .ToList();
        }

        var selected = candidates.Where(item => item.Id == taskCandidate.Id ||
            urgent && IsSurvivalCandidate(item.Id)).ToList();
        if (selected.All(item => item.Id != taskCandidate.Id)) selected.Add(taskCandidate);
        if (selected.All(item => item.Id != safeIdle.Id)) selected.Add(safeIdle);
        return selected;
    }

    private void AddBuildCandidates(
        List<CognitionCandidate> candidates,
        SocietyInhabitant inhabitant,
        PlaytestInhabitantState state)
    {
        if (inhabitant.HouseholdId is { } planningHousehold)
            AddHouseholdBuildingPlans(candidates, inhabitant, state, planningHousehold);

        // Work follows what the household holds, not a role: crops need the
        // household's Farmhouse, and workstation recipes need a building the
        // household holds or a communal one (see TryFindRecipeSite).
        foreach (var recipe in worldContent.Recipes.Where(item =>
                     !item.Outputs.Any(output => output.ResourceId == "bedding") &&
                     !item.IsCrop && !IsGenericFoodRecipe(item)))
        {
            if (NeedsUrgentWarmth(state) && !recipe.Outputs.Any(output => PersonalEquipmentRules.IsGarment(output.ResourceId)))
            {
                continue;
            }
            var householdWorkstation = recipe.WorkstationBuildingId is { } workstationId &&
                worldContent.Buildings.Any(definition => definition.CanonicalId == workstationId &&
                    definition.Tags.Any(IsHouseholdBuildingTag));
            var recipeOwner = ProductionOwnerFor(null, inhabitant.Id);
            if (!NeedsRecipeOutput(recipe, recipeOwner, inhabitant.Id) || AnotherAgentWaitsForWorkSite(inhabitant.Id, recipe) ||
                !CanAcquireProjectInputs(recipe.Inputs, recipeOwner, inhabitant.Id) ||
                !TryFindRecipeSite(recipe, out var siteId, out var position, inhabitant.Id) ||
                householdWorkstation &&
                (recipeOwner is null || !HasIngredientsAtBuilding(recipe.Inputs, recipeOwner, siteId)))
            {
                continue;
            }

            candidates.Add(new CognitionCandidate(
                $"build:recipe:{recipe.CanonicalId}",
                $"{recipe.DisplayName} at the household work site.",
                recipe.Tags.Contains("named-meal", StringComparer.Ordinal) ? 20 : recipe.IsCrop ? 20 : OutdoorExposure(state.Position) > 0 && recipe.Outputs.Any(output => PersonalEquipmentRules.IsGarment(output.ResourceId)) ? 25 : 30,
                $"build-site:{position.X},{position.Y}"));
        }
    }

    private string? DestinationNameForModel(string? id)
    {
        if (id is null) return null;
        return society.Checkpoint.Households.SingleOrDefault(item => item.Id == id)?.Name ??
            towns.SingleOrDefault(item => item.Id == id)?.Name ??
            society.Checkpoint.Inhabitants.SingleOrDefault(item => item.Id == id)?.Name ??
            map.Resources.SingleOrDefault(item => item.Id == id)?.Kind.Replace('_', ' ');
    }

    private int PriorityFor(PlaytestInhabitantState state) =>
        NeedsUrgentFood(state) || NeedsUrgentWarmth(state) ? 20 : 0;

    private static string ObservationDigest(
        string inhabitantId,
        string worldId,
        PlaytestInhabitantState state,
        IReadOnlyList<CognitionCandidate> candidates,
        IReadOnlyList<CognitionMemoryExcerpt> memories,
        IReadOnlyList<CognitionMemoryCompactionCandidate> compactionCandidates,
        IReadOnlyList<CognitionKnowledgeFact> knownMapFacts,
        CognitionSelfContext self,
        IReadOnlyList<CognitionObserverGuidance> observerGuidance)
    {
        var text = new StringBuilder()
            .Append("clankerworld.private-world-observation/v2|")
            .Append(inhabitantId).Append('|')
            .Append(worldId.Length).Append(':').Append(worldId).Append('|')
            .Append(state.Position.X).Append(',').Append(state.Position.Y).Append('|')
            .Append(state.HungerBasisPoints).Append('|')
            .Append(string.Join(',', candidates.Select(candidate => candidate.Id)));
        foreach (var memory in memories)
            text.Append('|').Append(memory.Id.Length).Append(':').Append(memory.Id)
                .Append('|').Append(memory.SourceTick)
                .Append('|').Append(memory.SubjectId.Length).Append(':').Append(memory.SubjectId)
                .Append('|').Append(memory.Summary.Length).Append(':').Append(memory.Summary)
                .Append('|').Append(memory.Kind)
                .Append('|').Append(memory.Visibility ?? "none")
                .Append('|').Append(memory.Provenance ?? "none")
                .Append('|').Append(memory.ConfidenceBasisPoints ?? -1)
                .Append('|').Append(memory.SourceAgentId ?? "none")
                .Append('|').Append(memory.SourceEventId ?? -1)
                .Append('|').Append(memory.IsCorrected)
                .Append('|').Append(memory.ImportanceBasisPoints)
                .Append('|').Append(memory.ImportanceConfidenceBasisPoints);
        foreach (var memory in compactionCandidates)
            text.Append('|').Append(memory.Kind)
                .Append('|').Append(memory.Id.Length).Append(':').Append(memory.Id)
                .Append('|').Append(memory.SourceTick)
                .Append('|').Append(memory.SubjectId.Length).Append(':').Append(memory.SubjectId)
                .Append('|').Append(memory.Summary.Length).Append(':').Append(memory.Summary)
                .Append('|').Append(memory.Visibility ?? "none")
                .Append('|').Append(memory.Provenance ?? "none")
                .Append('|').Append(memory.ConfidenceBasisPoints ?? -1)
                .Append('|').Append(memory.SourceAgentId ?? "none")
                .Append('|').Append(memory.SourceEventId ?? -1)
                .Append('|').Append(memory.IsCorrected);
        foreach (var fact in knownMapFacts)
            text.Append("|map=").Append(fact.X).Append(',').Append(fact.Y).Append('|')
                .Append(fact.Terrain).Append('|').Append(string.Join(',', fact.ResourceKinds))
                .Append('|').Append(fact.DiscovererId).Append('|').Append(fact.Acquisition)
                .Append('|').Append(fact.LearnedTick);
        foreach (var message in observerGuidance)
            text.Append("|observer=").Append(message.InstructionId.Length).Append(':').Append(message.InstructionId)
                .Append('|').Append(message.IssuerId.Length).Append(':').Append(message.IssuerId)
                .Append('|').Append(message.TargetInhabitantId.Length).Append(':').Append(message.TargetInhabitantId)
                .Append('|').Append(message.Kind)
                .Append('|').Append(message.Text.Length).Append(':').Append(message.Text)
                .Append('|').Append(message.SubmittedTick)
                .Append('|').Append(message.RunEpoch)
                .Append('|').Append(message.SubmissionSequence)
                .Append('|').Append(message.UnderstoodTask ?? "none")
                .Append('|').Append(message.ReplyAllowed);
        text.Append("|self=").Append(JsonSerializer.Serialize(self))
            .Append("|identity_pending=").Append(state.IdentityChoicePending);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))}";
    }

}
