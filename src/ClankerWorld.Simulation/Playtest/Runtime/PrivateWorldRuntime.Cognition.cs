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
            if (FarmWorkFor(inhabitant.Id) is not null && !NeedsUrgentFood(physical) && !NeedsUrgentWarmth(physical) &&
                !ShouldDispatchConversationChoice(inhabitant.Id))
                continue;
            if (physical.Project is { Stage: not ("completed" or "cancelled") } project &&
                (NeedsUrgentFood(physical) || NeedsUrgentWarmth(physical) && !IsProtectiveProject(project)))
            {
                SetProject(inhabitant.Id, project with { Stage = "paused", Blocker = NeedsUrgentWarmth(physical) ? "Seeking warmth" : "Meeting food needs" });
                physical = inhabitants[inhabitant.Id];
            }
            if (IsConversationBusy(inhabitant.Id) && !ShouldDispatchConversationChoice(inhabitant.Id))
                continue;
            var conversationChoiceContext = ConversationChoiceContextFor(inhabitant.Id);
            var candidates = CreateCandidates(inhabitant.Id, physical)
                .Select(candidate => candidate with { DestinationName = DestinationNameForModel(candidate.DestinationId) })
                .ToList();
            var current = runtimes[inhabitant.Id].CurrentIntention;
            if (!namingRetries.Contains(inhabitant.Id) &&
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
            var requiresPersonalProvider = checkpoint.Births.Any(birth => birth.ChildId == inhabitant.Id);
            var knownMapFacts = KnownMapFactsForCognition(inhabitant.Id);
            var self = new CognitionSelfContext(inhabitant.Id, inhabitant.Name, inhabitant.AgeBand.ToString(),
                physical.Personality, physical.Aspiration, inhabitant.HouseholdId,
                physical.Survival?.WarmthBasisPoints, physical.Survival?.IllnessBasisPoints,
                physical.RecentThoughts is { Count: > 0 } thoughts ? thoughts[^1].Text : null,
                checkpoint.Households.SingleOrDefault(item => item.Id == inhabitant.HouseholdId)?.Name,
                towns.SingleOrDefault(item => item.ResidentIds.Contains(inhabitant.Id, StringComparer.Ordinal))?.Name,
                HousingNote(inhabitant.Id), EquipmentNote(inhabitant.Id));
            var observation = new InhabitantObservation(
                inhabitant.Id,
                WorldTick,
                society.Checkpoint.RunEpoch,
                generation,
                ObservationDigest(inhabitant.Id, physical, candidates, retrievedMemories, [], knownMapFacts, self),
                physical.HungerBasisPoints,
                candidates,
                NeedsName: inhabitant.NeedsName,
                RequiresPersonalProvider: requiresPersonalProvider,
                RetrievedMemories: retrievedMemories,
                KnownMapFacts: knownMapFacts, Self: self,
                NeedsPersonality: physical.IdentityChoicePending,
                NeedsAspiration: physical.IdentityChoicePending)
            {
                ConversationChoiceContext = conversationChoiceContext,
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
                                    inhabitant.Id, physical, candidates, retrievedMemories, memoryCandidates, knownMapFacts, self),
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
                inhabitants[inhabitant.Id] = inhabitants[inhabitant.Id] with
                {
                    LastDecisionContext = DecisionContext(physical, candidates, conversationChoiceContext),
                };
            }
        }
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
        if (PendingInstructionFor(inhabitantId) is { } instruction &&
            (current is null || current.WorldTick <= instruction.SubmittedTick))
        {
            return true;
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
            string.Join('|', candidates.Select(candidate => candidate.Id).Order(StringComparer.Ordinal));
        return conversationChoiceContext is null
            ? context
            : $"{context}|conversation_choice={ConversationChoiceContextDigest(conversationChoiceContext)}";
    }

    private static bool HasPromptedConversationChoice(string? lastDecisionContext, string conversationChoiceContext) =>
        lastDecisionContext?.EndsWith(
            $"|conversation_choice={ConversationChoiceContextDigest(conversationChoiceContext)}",
            StringComparison.Ordinal) == true;

    private static string ConversationChoiceContextDigest(string conversationChoiceContext) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conversationChoiceContext)));

    private void ApplyContinuingIntentions(IEnumerable<string> dispatchedInhabitantIds)
    {
        var dispatched = dispatchedInhabitantIds.ToHashSet(StringComparer.Ordinal);
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
            if (IsConversationBusy(inhabitant.Id))
            {
                continue;
            }
            if (inhabitant.AgeBand == SocietyAgeBand.Infant)
            {
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
            if (runtimes[inhabitant.Id].CurrentIntention is not { } intention ||
                !CreateCandidates(inhabitant.Id, state).Any(candidate => candidate.Id == intention.CandidateId))
            {
                continue;
            }

            ApplyCandidate(inhabitant.Id, state, intention.CandidateId, reportIdle: false);
        }
    }

    private void ApplySafeRoutinesWhileWaiting(IEnumerable<string> waitingIds)
    {
        var safe = new HashSet<string>(StringComparer.Ordinal)
        {
            "consume_food", "collect_shared_food", "harvest_food", "seek_food",
            "wear_clothing", "tend_fire", "seek_warmth",
        };
        foreach (var id in waitingIds.OrderBy(item => item, StringComparer.Ordinal))
        {
            if (!inhabitants.TryGetValue(id, out var state)) continue;
            if (!NeedsUrgentFood(state) && !NeedsUrgentWarmth(state))
                continue;
            var candidate = CreateCandidates(id, state)
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

        var pendingInstruction = PendingInstructionFor(decision.InhabitantId);
        var candidateId = decision.Admission.Intention.CandidateId;
        var forcedCandidate = !decision.Admission.FellBack && pendingInstruction?.Kind == OwnerInstructionKind.MustDo
            ? InstructionCandidate(pendingInstruction.Text)
            : null;
        if (forcedCandidate is not null && CreateCandidates(decision.InhabitantId, state)
            .Any(candidate => candidate.Id == forcedCandidate))
        {
            candidateId = forcedCandidate;
        }

        var carriedFoodBefore = society.Checkpoint.Inventory.Lots.Where(lot =>
            lot.OwnerId == decision.InhabitantId && IsEdibleFood(lot.ItemKind)).Sum(lot => (long)lot.Quantity);
        ApplyCandidate(decision.InhabitantId, state, candidateId, reportIdle: true);
        var forcedApplied = forcedCandidate == candidateId && (candidateId switch
        {
            "seek_food" => inhabitants[decision.InhabitantId].Position != state.Position,
            "consume_food" => inhabitants[decision.InhabitantId].HungerBasisPoints > state.HungerBasisPoints,
            "harvest_food" => society.Checkpoint.Inventory.Lots.Where(lot =>
                lot.OwnerId == decision.InhabitantId && IsEdibleFood(lot.ItemKind)).Sum(lot => (long)lot.Quantity) > carriedFoodBefore,
            _ => false,
        });

        if (!decision.Admission.FellBack && pendingInstruction is not null &&
            (pendingInstruction.Kind == OwnerInstructionKind.Suggestive || forcedApplied))
        {
            completedInstructionIds.Add(pendingInstruction.InstructionId);
            AppendEvent("instruction_applied", $"{pendingInstruction.InstructionId}:{candidateId}");
        }
    }

    private void ApplyCandidate(
        string inhabitantId,
        PlaytestInhabitantState state,
        string candidateId,
        bool reportIdle)
    {
        if (candidateId.StartsWith("talk:", StringComparison.Ordinal) ||
            candidateId.StartsWith("conversation_", StringComparison.Ordinal))
        {
            if (!ApplyConversationCandidate(inhabitantId, candidateId))
                AppendEvent("conversation_action_rejected", $"{inhabitantId}:{candidateId.Split(':')[0]}");
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
        if (ContinueFarmWork(inhabitantId)) return;
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
        if (candidateId == "haul_household_stock")
        {
            HaulHouseholdStock(inhabitantId, state);
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
        if (candidateId == "deliver_smith_ore")
        {
            DeliverBlacksmithOre(inhabitantId, state);
            return;
        }
        if (candidateId is "collect_wooden_axe" or "collect_wooden_pickaxe" or "collect_wooden_hoe")
        {
            CollectEquipment(inhabitantId, state,
                candidateId == "collect_wooden_axe" ? "wooden_axe" : candidateId == "collect_wooden_hoe" ? FarmFieldRules.Hoe : "wooden_pickaxe");
            return;
        }
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
        PlaytestInhabitantState state)
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
        var instructionCandidate = instruction is null ? null : InstructionCandidate(instruction.Text);

        var hasFood = PreferredFood(inhabitantId, inhabitantId).Any();
        if (hasFood && state.HungerBasisPoints < ComfortableFullness)
        {
            candidates.Add(new CognitionCandidate("consume_food", "Eat one carried food item.", 0));
        }

        if (instructionCandidate == "consume_food" && hasFood && !candidates.Any(item => item.Id == "consume_food"))
        {
            candidates.Add(new CognitionCandidate("consume_food", "Follow the owner's food instruction.", 0));
        }

        var foodPriority = NeedsUrgentFood(state) ? 2 : state.HungerBasisPoints < RoutineFoodSeekFullness ? 5 : 90;
        // An optional reserve remains selectable without outranking ordinary activities.
        var wantsFood = !hasFood && state.HungerBasisPoints < 7_000;
        var shouldGatherFood = wantsFood && FreeCarryCapacity(inhabitantId) > 0;
        var foodSource = shouldGatherFood || instructionCandidate is "seek_food" or "harvest_food"
            ? AvailableFoodSource(inhabitantId, state.Position) : null;
        if (foodSource is not null && FreeCarryCapacity(inhabitantId) < FoodHarvestCarryUnits(foodSource))
            foodSource = null;
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
        if (AdultResident(inhabitantId))
        {
            AddKnowledgeCandidates(candidates, inhabitantId, state);
            AddFamilyCandidates(candidates, inhabitantId);
            AddHousingCandidates(candidates, inhabitantId);
            AddParenthoodCandidates(candidates, inhabitantId);
            AddUrgentFoodPotCandidate(candidates, inhabitantId, state);
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
            AddProjectAssistanceCandidates(candidates, inhabitantId);
            AddForestryCandidates(candidates, inhabitantId, state);
            AddTradeCandidates(candidates, inhabitantId);
            AddCouncilCandidates(candidates, inhabitantId);
            AddLearningCandidates(candidates, inhabitantId);
            AddExplorationCandidate(candidates, inhabitantId, state);
        }

        if (currentConversation is null)
            candidates.AddRange(ConversationCandidates(inhabitantId));
        candidates.Add(new CognitionCandidate("safe_idle", "Continue safely without starting a new task.", 100));
        return candidates;
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
                     !item.IsCrop))
        {
            if (NeedsUrgentWarmth(state) && !recipe.Outputs.Any(output => PersonalEquipmentRules.IsGarment(output.ResourceId)))
            {
                continue;
            }
            var householdWorkstation = recipe.WorkstationBuildingId is { } workstationId &&
                worldContent.Buildings.Any(definition => definition.CanonicalId == workstationId &&
                    definition.Tags.Any(IsHouseholdBuildingTag));
            var recipeOwner = ProductionOwnerFor(null, inhabitant.Id);
            if (!NeedsRecipeOutput(recipe, recipeOwner) || AnotherAgentWaitsForWorkSite(inhabitant.Id, recipe) ||
                !CanAcquireProjectInputs(recipe.Inputs, recipeOwner, inhabitant.Id) ||
                !TryFindRecipeSite(recipe, out var siteId, out var position, inhabitant.Id) ||
                householdWorkstation &&
                (recipeOwner is null || !HasIngredientsAtBuilding(recipe.Inputs, recipeOwner, siteId)))
            {
                continue;
            }

            candidates.Add(new CognitionCandidate(
                $"build:recipe:{recipe.CanonicalId}",
                $"Build {recipe.DisplayName} at a valid site.",
                recipe.IsCrop ? 20 : WeatherExposure(state.Position) > 0 && recipe.Outputs.Any(output => PersonalEquipmentRules.IsGarment(output.ResourceId)) ? 25 : 30,
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
        PlaytestInhabitantState state,
        IReadOnlyList<CognitionCandidate> candidates,
        IReadOnlyList<CognitionMemoryExcerpt> memories,
        IReadOnlyList<CognitionMemoryCompactionCandidate> compactionCandidates,
        IReadOnlyList<CognitionKnowledgeFact> knownMapFacts,
        CognitionSelfContext self)
    {
        var text = new StringBuilder()
            .Append("clankerworld.private-world-observation/v1|")
            .Append(inhabitantId).Append('|')
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
        text.Append("|self=").Append(JsonSerializer.Serialize(self))
            .Append("|identity_pending=").Append(state.IdentityChoicePending);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))}";
    }

}
