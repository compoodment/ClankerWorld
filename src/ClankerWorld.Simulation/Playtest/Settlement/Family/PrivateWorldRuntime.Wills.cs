using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const int WillDecisionDeadlineTicks = 30;
    private const int MaximumWillPersonHeirs = 16;
    private const string TownHeirKeyPrefix = "will:town:";
    private const string PersonHeirKeyPrefix = "will:heir:";
    private static readonly TimeSpan WillDecisionTimeout = TimeSpan.FromSeconds(15);
    private readonly Dictionary<string, PendingWillDecision> pendingWills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> pendingWillCancellationReasons = new(StringComparer.Ordinal);

    private sealed record WillDecisionOutcome(CognitionDecisionResponse? Response, string Failure);

    /// <summary>
    /// One in-flight will request. The keys the model may use map back to the
    /// heirs and frozen lots this request offered; nothing else is accepted.
    /// </summary>
    private sealed record PendingWillDecision(
        string EstateId,
        CognitionDecisionRequest Request,
        IReadOnlySet<string> CandidateIds,
        IReadOnlyDictionary<string, string> HeirKeys,
        IReadOnlyDictionary<string, string> ItemKeys,
        Task<WillDecisionOutcome> Task,
        CancellationTokenSource Cancellation);

    private sealed record WillRequestContext(
        InhabitantObservation Observation,
        IReadOnlyDictionary<string, string> HeirKeys,
        IReadOnlyDictionary<string, string> ItemKeys);

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
                var context = CreateWillRequestContext(estate);
                var observation = context.Observation;
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
                        cancellation.Token.ThrowIfCancellationRequested();
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
                    context.HeirKeys, context.ItemKeys, task, cancellation));
                AppendEvent("estate_will_started", estate.Id);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ResolveWillDefault(estate.Id, $"setup_{exception.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// The dead agent's own view for their will: what they owned, their family
    /// and household first among up to sixteen living people, and their Town
    /// when it has a Warehouse to keep inherited goods.
    /// </summary>
    private WillRequestContext CreateWillRequestContext(SocietyEstate estate)
    {
        var checkpoint = society.Checkpoint;
        var deceased = checkpoint.GetInhabitant(estate.DeceasedId);
        var archived = deceasedInhabitants.GetValueOrDefault(estate.DeceasedId);
        var snapshot = (estate.FrozenLots ?? []).OrderBy(item => item.LotId, StringComparer.Ordinal).ToArray();
        // A vessel's contents go with the vessel, so only top-level lots are offered.
        var containedIn = checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == estate.Id && lot.ContainerLotId is not null)
            .ToDictionary(lot => lot.Id, lot => lot.ContainerLotId!, StringComparer.Ordinal);
        var itemKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = new List<CognitionWillItem>();
        foreach (var lot in snapshot.Where(item => !containedIn.ContainsKey(item.LotId)).Take(CognitionWillContext.MaximumItems))
        {
            var key = "item:" + (items.Count + 1).ToString(CultureInfo.InvariantCulture);
            var contents = string.Join(", ", snapshot.Where(item => containedIn.GetValueOrDefault(item.LotId) == lot.LotId)
                .Select(item => $"{item.Quantity} {item.ItemKind}"));
            itemKeys.Add(key, lot.LotId);
            items.Add(new CognitionWillItem(key, lot.ItemKind, lot.Quantity,
                contents.Length is > 0 and <= 128 ? contents : null));
        }

        var people = checkpoint.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active && item.Id != deceased.Id)
            .Select(item => (Person: item, Relation: WillRelation(checkpoint, deceased, item)))
            .OrderBy(item => item.Relation is null ? 1 : 0)
            .ThenBy(item => item.Person.Id, StringComparer.Ordinal)
            .Take(MaximumWillPersonHeirs).ToArray();
        var offeredTownId = archived?.TownId is { } residentTownId && TownsThatMayInherit().Contains(residentTownId)
            ? residentTownId : null;
        // Reserve every short raw key first so a long identity's hash can never
        // shadow a different person's or Town's valid raw identity.
        var occupiedKeys = people.Select(item => PersonHeirKeyPrefix + item.Person.Id)
            .Concat(offeredTownId is null ? [] : new[] { TownHeirKeyPrefix + offeredTownId })
            .Where(key => key.Length <= CognitionWillContext.MaximumHeirKeyLength)
            .ToHashSet(StringComparer.Ordinal);
        var heirKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var heirs = new List<CognitionWillHeir>();
        foreach (var (person, relation) in people)
        {
            var key = BoundedWillHeirKey(PersonHeirKeyPrefix, person.Id, occupiedKeys);
            heirKeys.Add(key, person.Id);
            heirs.Add(new CognitionWillHeir(key, person.Name, relation));
        }
        if (offeredTownId is { } townId)
        {
            var key = BoundedWillHeirKey(TownHeirKeyPrefix, townId, occupiedKeys);
            heirKeys.Add(key, townId);
            heirs.Add(new CognitionWillHeir(key, towns.Single(item => item.Id == townId).Name, "your Town"));
        }

        var summary = string.Join(", ", items.Select(item =>
            $"{item.Quantity} {item.Kind}{(item.Contents is { } held ? $" holding {held}" : "")}"));
        var recordedTown = towns.SingleOrDefault(item => item.Id == archived?.TownId);
        var defaultRule = TownEstateDefaultRules.AtDeath(recordedTown?.Government, estate.CreatedTick)?.Version.EstateDefault;
        var defaultDescription = defaultRule is null
            ? "Use default inheritance: living household heirs share your belongings; if none remain, your recorded Town inherits, or communal stock when you have no recorded Town."
            : $"Use default inheritance: {recordedTown!.Name} receives {defaultRule.TownSharePercent}% of each eligible lot, rounded down and limited by Warehouse room; living household heirs share the rest. Vessel families stay together. If no household heirs remain, your recorded Town inherits.";
        var candidates = new List<CognitionCandidate>
        {
            new(CognitionWillContext.HouseholdCandidateId,
                $"You have died. Your belongings: {summary}. {defaultDescription}"),
        };
        if (heirs.Count > 0)
        {
            candidates.Add(new CognitionCandidate(CognitionWillContext.HeirsCandidateId,
                "You have died. Name one to three heirs from possible heirs and say how your belongings are divided."));
        }

        var self = archived is null ? null : new CognitionSelfContext(deceased.Id, deceased.Name,
            deceased.AgeBand.ToString(), archived.LastPhysical.Personality, archived.LastPhysical.Aspiration,
            CognitionHouseholdId(deceased.HouseholdId), null, null, null,
            checkpoint.Households.SingleOrDefault(item => item.Id == deceased.HouseholdId)?.Name,
            towns.SingleOrDefault(item => item.Id == archived.TownId)?.Name);
        var memories = PrivateWorldMemoryRetrieval.Retrieve(checkpoint.Memories, checkpoint.Beliefs ?? [],
            (checkpoint.MemoryCompactions ?? []).SingleOrDefault(item => item.OwnerId == deceased.Id),
            deceased.Id, estate.CreatedTick, candidates);
        var digestInput = estate.Id + "|" + string.Join("|", snapshot.Select(item =>
            $"{item.LotId}:{item.ItemKind}:{item.Quantity}")) + "|" +
            string.Join("|", candidates.Select(item => $"{item.Id}:{item.Description}")) + "|" +
            string.Join("|", heirs.Select(item => $"{item.Key}:{heirKeys[item.Key]}:{item.Relation}")) + "|" +
            string.Join("|", memories.Select(item => item.Id));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestInput)));
        var observation = new InhabitantObservation(estate.DeceasedId, estate.CreatedTick,
            checkpoint.RunEpoch, 0, digest, 0, candidates, RequiresPersonalProvider: true,
            RetrievedMemories: memories, Self: self, Will: new CognitionWillContext(items, heirs));
        return new WillRequestContext(observation, heirKeys, itemKeys);
    }

    private static string BoundedWillHeirKey(string prefix, string id, HashSet<string> occupiedKeys)
    {
        var raw = prefix + id;
        if (raw.Length <= CognitionWillContext.MaximumHeirKeyLength) return raw;
        var hashed = prefix + "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        var key = hashed;
        for (var collision = 1; !occupiedKeys.Add(key); collision++)
            key = hashed + ":" + collision.ToString(CultureInfo.InvariantCulture);
        return key;
    }

    /// <summary>A short label of how a living person was related to the dead agent, if at all.</summary>
    private static string? WillRelation(SocietyCheckpoint checkpoint, SocietyInhabitant deceased, SocietyInhabitant person)
    {
        bool Between(SocietyRelationship item) =>
            item.ProposerId == deceased.Id && item.TargetId == person.Id ||
            item.ProposerId == person.Id && item.TargetId == deceased.Id;
        foreach (var relationship in checkpoint.Relationships.Where(Between).OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (relationship.Type == SocietyRelationshipType.BiologicalParentage)
                return relationship.ProposerId == deceased.Id ? "your child" : "your parent";
        }
        if (checkpoint.Relationships.Any(item => Between(item) && item.Type == SocietyRelationshipType.Partnership &&
                item.State is SocietyRelationshipState.Accepted or SocietyRelationshipState.EndedByDeath))
            return "your partner";
        return deceased.HouseholdId is { } household && person.HouseholdId == household ? "your household" : null;
    }

    /// <summary>Towns with a Warehouse of their own, the only place a Town keeps inherited goods.</summary>
    private HashSet<string> TownsThatMayInherit() => towns
        .Where(town => town.Id.StartsWith("town:", StringComparison.Ordinal) && TownInheritanceWarehouse(town.Id) is not null)
        .Select(town => town.Id).ToHashSet(StringComparer.Ordinal);

    private PlacedBuilding? TownInheritanceWarehouse(string townId) => worldSimulation.Buildings
        .Where(building => building.TownId == townId && building.HouseholdId is null &&
            worldContent.Buildings.Any(definition => definition.CanonicalId == building.DefinitionId &&
                definition.Tags.Contains("warehouse", StringComparer.Ordinal)))
        .OrderBy(building => building.InstanceId, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>
    /// Where each Town can keep goods an estate leaves it this tick. Only built
    /// when an estate is due, so ordinary ticks do no storage scan.
    /// </summary>
    private SocietyTownStore[]? TownStoresForDueEstates(long targetTick)
    {
        if (!society.Checkpoint.Estates.Any(estate => !estate.Settled && estate.WillStatus != "pending" && estate.ExpiryTick <= targetTick))
            return null;
        // A Warehouse takes no food, and no handcart, which stays on the ground:
        // the household path keeps such a cart and its cargo where they are.
        var refused = new HashSet<string>(society.Checkpoint.Inventory.Lots
            .Where(lot => InventoryContainerRules.IsFood(lot.ItemKind))
            .Select(lot => lot.ItemKind).Append(InventoryContainerRules.Handcart), StringComparer.Ordinal);
        return towns.Select(town => TownInheritanceWarehouse(town.Id) is { } warehouse
                ? new SocietyTownStore(town.Id, warehouse.InstanceId, DestinationRoom(warehouse.InstanceId), refused)
                : null)
            .OfType<SocietyTownStore>().ToArray();
    }

    private SocietyDefaultEstateDivision[] DefaultEstateDivisionsForDueEstates(long targetTick) =>
        society.Checkpoint.Estates.Where(estate => !estate.Settled && estate.WillStatus != "pending" && estate.ExpiryTick <= targetTick)
            .Select(estate => deceasedInhabitants.TryGetValue(estate.DeceasedId, out var deceased) && deceased.TownId is { } townId &&
                towns.FirstOrDefault(town => town.Id == townId) is { } town
                ? new SocietyDefaultEstateDivision(estate.Id, townId,
                    TownEstateDefaultRules.AtDeath(town.Government, estate.CreatedTick)?.Version.EstateDefault?.TownSharePercent ?? 0)
                : null).OfType<SocietyDefaultEstateDivision>().ToArray();

    private void ProcessWillDecisions(
        IReadOnlyList<PendingWillDecision> completed, IReadOnlyList<string> activeIds,
        IReadOnlyDictionary<string, string> inactiveReasons,
        Func<PendingWillDecision, bool> providerIsCurrent)
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
                var outcome = pending.Task.GetAwaiter().GetResult();
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
                            providerIsCurrent(pending) &&
                            society.Checkpoint.RunEpoch == pending.Request.Observation.RunEpoch &&
                            response.RunEpoch == pending.Request.Observation.RunEpoch &&
                            response.DecisionGeneration == pending.Request.Observation.DecisionGeneration &&
                            response.ObservationDigest == pending.Request.Observation.ObservationDigest &&
                            pending.CandidateIds.Contains(response.SelectedCandidateId);
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException) { }
                }
                if (!valid || response is null)
                {
                    ResolveWillDefault(estate.Id, outcome.Failure == "none" ? "invalid_response" : outcome.Failure);
                    continue;
                }

                // Final words belong to an admitted reply; they stand even when
                // its division is unusable and default inheritance applies.
                var finalWords = response.Will?.FinalWords;
                if (response.SelectedCandidateId == CognitionWillContext.HouseholdCandidateId)
                {
                    ResolveWillDefault(estate.Id, "household_selected", finalWords);
                    continue;
                }
                var directive = WillDirective(pending, response.Will);
                if (directive is null)
                {
                    ResolveWillDefault(estate.Id, "invalid_estate_or_heir", finalWords);
                    continue;
                }
                var result = society.Apply(checkpoint => SocietyFixture.ResolveWill(
                    checkpoint, estate.Id, directive, "accepted", finalWords, TownsThatMayInherit()));
                var accepted = result.Checkpoint.GetEstate(estate.Id).WillStatus == "accepted";
                AppendEvent(accepted ? "estate_will_accepted" : "estate_will_default", accepted
                    ? $"{estate.Id}:{directive.Split}:{directive.HeirIds.Count.ToString(CultureInfo.InvariantCulture)}"
                    : $"{estate.Id}:invalid_estate_or_heir");
            }
            else if (!active.Contains(estate.Id))
            {
                ResolveWillDefault(estate.Id, inactiveReasons.GetValueOrDefault(estate.Id, "interrupted"));
            }
        }
    }

    private bool IsWillDecisionProviderCurrent(PendingWillDecision pending)
    {
        try
        {
            var provider = providerFactory?.Invoke(pending.Request.Observation.InhabitantId);
            return provider is not null &&
                provider.KindFor(pending.Request.Observation) == DecisionProviderKind.LargeLanguageModel &&
                provider.ProviderEpoch == pending.Request.ProviderEpoch;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    /// <summary>
    /// Maps a reply's keys back to the heirs and lots this request offered.
    /// Any key the request did not offer makes the whole division unusable.
    /// </summary>
    private static SocietyWillDirective? WillDirective(PendingWillDecision pending, CognitionWillChoice? choice)
    {
        if (choice is not { HeirKeys.Count: > 0, Split: { } split }) return null;
        var heirs = new List<string>();
        foreach (var key in choice.HeirKeys)
        {
            if (!pending.HeirKeys.TryGetValue(key, out var heirId)) return null;
            heirs.Add(heirId);
        }
        Dictionary<string, string>? lotHeirs = null;
        if (choice.ItemHeirs is { Count: > 0 } items)
        {
            lotHeirs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (itemKey, heirKey) in items)
            {
                if (!pending.ItemKeys.TryGetValue(itemKey, out var lotId) ||
                    !pending.HeirKeys.TryGetValue(heirKey, out var heirId) ||
                    !heirs.Contains(heirId, StringComparer.Ordinal) || !lotHeirs.TryAdd(lotId, heirId))
                    return null;
            }
        }
        return new SocietyWillDirective(heirs, split, lotHeirs);
    }

    private void ResolveWillDefault(string estateId, string reason, string? finalWords = null)
    {
        society.Apply(checkpoint => SocietyFixture.ResolveWill(checkpoint, estateId, null, reason, finalWords));
        AppendEvent("estate_will_default", $"{estateId}:{reason}");
    }

    private void CancelPendingWill(string estateId, string reason = "interrupted", bool underRuntimeGate = true)
    {
        var estateIsPending = society.Checkpoint.Estates.Any(item => item.Id == estateId && item.WillStatus == "pending");
        if (!pendingWills.Remove(estateId, out var pending)) return;
        if (estateIsPending) pendingWillCancellationReasons[estateId] = reason;
        else pendingWillCancellationReasons.Remove(estateId);
        CancelProviderCall(pending.Cancellation, underRuntimeGate);
        _ = pending.Task.ContinueWith(_ => pending.Cancellation.Dispose(), TaskScheduler.Default);
    }
}
