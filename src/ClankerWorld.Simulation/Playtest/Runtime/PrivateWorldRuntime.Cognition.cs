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
        var runtimes = society.Capture().Cognition.Runtimes
            .ToDictionary(item => item.InhabitantId, StringComparer.Ordinal);
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
            if (physical.Project is { Stage: not ("completed" or "cancelled") } project &&
                (NeedsUrgentFood(physical) || NeedsUrgentWarmth(physical) && !IsProtectiveProject(project)))
            {
                SetProject(inhabitant.Id, project with { Stage = "paused", Blocker = NeedsUrgentWarmth(physical) ? "Seeking warmth" : "Meeting food needs" });
                physical = inhabitants[inhabitant.Id];
            }
            var candidates = CreateCandidates(inhabitant.Id, physical);
            var current = runtimes[inhabitant.Id].CurrentIntention;
            if (!NeedsCognition(inhabitant.Id, current, candidates))
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
                physical.RecentThoughts is { Count: > 0 } thoughts ? thoughts[^1].Text : null);
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
                KnownMapFacts: knownMapFacts, Self: self);
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
                    LastDecisionContext = DecisionContext(physical, candidates),
                };
            }
        }
    }

    private bool NeedsCognition(
        string inhabitantId,
        CognitionIntention? current,
        List<CognitionCandidate> candidates)
    {
        // A new instruction prompts one fresh decision. If it cannot progress
        // yet, it waits for the agent's usual decisions instead of requesting
        // another (possibly paid) decision on every tick.
        if (PendingInstructionFor(inhabitantId) is { } instruction &&
            (current is null || current.WorldTick <= instruction.SubmittedTick))
        {
            return true;
        }

        if (CanContinueLesson(inhabitantId) || CanContinueProject(inhabitants[inhabitantId]))
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
            return DecisionContext(physical, candidates) != physical.LastDecisionContext ||
                checked(WorldTick - current.WorldTick) >= 300;
        }

        return checked(WorldTick - current.WorldTick) >= CognitionReevaluationIntervalTicks;
    }

    private string DecisionContext(PlaytestInhabitantState state, List<CognitionCandidate> candidates) =>
        $"{NeedsUrgentFood(state)}:{NeedsUrgentWarmth(state)}:" +
        string.Join('|', candidates.Select(candidate => candidate.Id).Order(StringComparer.Ordinal));

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
            if (inhabitant.AgeBand == SocietyAgeBand.Infant)
            {
                continue;
            }
            if (CanContinueLesson(inhabitant.Id))
            {
                ContinueLesson(inhabitant.Id);
                continue;
            }
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
        if (!AgePermitsCandidate(inhabitantId, candidateId))
        {
            AppendEvent("age_action_rejected", $"{inhabitantId}:{candidateId}");
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
        if (candidateId == "collect_wooden_axe" || candidateId == "collect_wooden_pickaxe")
        {
            CollectEquipment(inhabitantId, state,
                candidateId == "collect_wooden_axe" ? "wooden_axe" : "wooden_pickaxe");
            return;
        }
        if (candidateId.StartsWith(KnowledgeSharePrefix, StringComparison.Ordinal))
        {
            ApplyKnowledgeShare(inhabitantId, state, candidateId);
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

        switch (candidateId)
        {
            case "explore":
                Explore(inhabitantId, state);
                break;
            case "wear_clothing":
                CollectEquipment(inhabitantId, state, "clothing");
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
            var layout = CreateTownLayoutContext(inhabitantId, selection.SitePosition);
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
                society.Checkpoint.GetInhabitant(inhabitantId).HouseholdId is null ? inhabitantId : null);
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
        var candidates = new List<CognitionCandidate>();
        var instruction = PendingInstructionFor(inhabitantId);
        var instructionCandidate = instruction is null ? null : InstructionCandidate(instruction.Text);

        var hasFood = society.Checkpoint.Inventory.Lots.Any(item =>
            item.OwnerId == inhabitantId && IsEdibleFood(item.ItemKind) && AvailableLotQuantity(item) > 0);
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
        var shouldGatherFood = !hasFood && state.HungerBasisPoints < 7_000;
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
            AddParenthoodCandidates(candidates, inhabitantId);
        }
        if (!NeedsUrgentWarmth(state) && ChildResident(inhabitantId))
        {
            AddChildCandidates(candidates, inhabitantId, state);
        }
        if (!NeedsUrgentFood(state) && AdultResident(inhabitantId))
        {
            var inhabitant = society.Checkpoint.GetInhabitant(inhabitantId);
            AddBuildCandidates(candidates, inhabitant, state);
            AddHouseHaulCandidate(candidates, inhabitantId, state);
            AddWarehouseStockCandidate(candidates, inhabitantId, state);
            AddFarmGrainCandidate(candidates, inhabitantId, state);
            AddFarmFlourCandidate(candidates, inhabitantId, state);
            AddBlacksmithStockCandidate(candidates, inhabitantId, state);
            AddBlacksmithOreCandidates(candidates, inhabitantId, state);
            AddCraftToolCandidates(candidates, inhabitantId);
            AddProjectAssistanceCandidates(candidates, inhabitantId);
            AddForestryCandidates(candidates, inhabitantId, state);
            AddTradeCandidates(candidates, inhabitantId);
            AddCouncilCandidates(candidates, inhabitantId);
            AddLearningCandidates(candidates, inhabitantId);
            AddExplorationCandidate(candidates, inhabitantId, state);
        }

        candidates.Add(new CognitionCandidate("safe_idle", "Continue safely without starting a new task.", 100));
        return candidates;
    }

    private void AddBuildCandidates(
        List<CognitionCandidate> candidates,
        SocietyInhabitant inhabitant,
        PlaytestInhabitantState state)
    {
        var canBuildStructures = inhabitant.CurrentRole == SocietyWorkRole.Builder ||
            state.Aspiration.Contains("build", StringComparison.OrdinalIgnoreCase);
        if (canBuildStructures)
        {
            var layout = CreateTownLayoutContext(inhabitant.Id);
            foreach (var definition in worldContent.Buildings)
            {
                if (RetiredBuildings.Contains(definition))
                    continue;
                if (definition.Tags.Contains("warehouse", StringComparer.Ordinal) &&
                    (TownForResident(inhabitant.Id) is not { } townId ||
                     worldSimulation.Buildings.Any(building => building.TownId == townId &&
                         worldContent.Buildings.Any(existing => existing.CanonicalId == building.DefinitionId &&
                             existing.Tags.Contains("warehouse", StringComparer.Ordinal)))))
                    continue;
                if (definition.Tags.Contains("house", StringComparer.Ordinal) &&
                    (inhabitant.HouseholdId is null || HouseForHousehold(inhabitant.HouseholdId) is not null))
                    continue;
                if (definition.Tags.Contains("farmhouse", StringComparer.Ordinal) && inhabitant.HouseholdId is null)
                    continue;
                if (definition.Tags.Contains("blacksmith", StringComparer.Ordinal) &&
                    (inhabitant.HouseholdId is null || TownForResident(inhabitant.Id) is null))
                    continue;
                if (NeedsUrgentWarmth(state) && !definition.Tags.Any(tag => tag is "shelter" or "warmth" or "cooking"))
                {
                    continue;
                }
                var instanceId = BuildInstanceId(inhabitant.Id, definition);
                var constructionOwner = definition.Tags.Any(IsHouseholdBuildingTag)
                    ? inhabitant.HouseholdId : inhabitant.HouseholdId is null ? inhabitant.Id : null;
                if (worldSimulation.Buildings.Any(item => item.InstanceId == instanceId) ||
                    !CanAcquireProjectInputs(definition.BuildCosts, constructionOwner, inhabitant.Id))
                {
                    continue;
                }

                var sites = TownLayoutService.RankConstructionSites(layout, definition);
                for (var rank = 0; rank < sites.Count; rank++)
                {
                    var site = sites[rank];
                    var description = string.Join(" ", site.Reasons.Select(reason => reason.Description));
                    candidates.Add(new CognitionCandidate(
                        TownConstructionCandidateIds.Building(definition.CanonicalId, site.Position),
                        $"Plan {definition.DisplayName} at ({site.Position.X}, {site.Position.Y}): {description}",
                        20 + rank,
                        $"build-site:{site.Position.X},{site.Position.Y}"));
                }
            }
        }

        var canGrow = inhabitant.CurrentRole == SocietyWorkRole.Farmer ||
            state.Aspiration.Contains("self-sufficient", StringComparison.OrdinalIgnoreCase);
        var canProduce = inhabitant.CurrentRole is SocietyWorkRole.Farmer or
            SocietyWorkRole.Builder or SocietyWorkRole.Trader or SocietyWorkRole.Organizer;
        foreach (var recipe in worldContent.Recipes.Where(item =>
                     !item.Outputs.Any(output => output.ResourceId == "bedding") &&
                     (item.IsCrop ? canGrow : canProduce)))
        {
            if (NeedsUrgentWarmth(state) && !recipe.Outputs.Any(output => output.ResourceId == "clothing"))
            {
                continue;
            }
            var householdWorkstation = recipe.WorkstationBuildingId is { } workstationId &&
                worldContent.Buildings.Any(definition => definition.CanonicalId == workstationId &&
                    definition.Tags.Any(IsHouseholdBuildingTag));
            var recipeOwner = ProductionOwnerFor(null, inhabitant.Id);
            if (!NeedsRecipeOutput(recipe, recipeOwner) || !CanAcquireProjectInputs(recipe.Inputs, recipeOwner, inhabitant.Id) ||
                !TryFindRecipeSite(recipe, out var siteId, out var position, inhabitant.Id) ||
                householdWorkstation &&
                (recipeOwner is null || !HasIngredientsAtBuilding(recipe.Inputs, recipeOwner, siteId)))
            {
                continue;
            }

            candidates.Add(new CognitionCandidate(
                $"build:recipe:{recipe.CanonicalId}",
                $"Build {recipe.DisplayName} at a valid site.",
                recipe.IsCrop ? 20 : WeatherExposure(state.Position) > 0 && recipe.Outputs.Any(output => output.ResourceId == "clothing") ? 25 : 30,
                $"build-site:{position.X},{position.Y}"));
        }
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
        text.Append("|self=").Append(JsonSerializer.Serialize(self));
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))}";
    }

}
