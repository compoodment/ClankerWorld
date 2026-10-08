using System.Security.Cryptography;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private const string NonviolentRemedyPrefix = "nonviolent_remedy:";
    private const string NonviolentRelayPrefix = "nonviolent_relay:";
    private const int UnfiledConductLimit = 256;
    private (string TownId, string AgreementId, string TermId)? nonviolentRemedySelection;

    private static string NonviolentDigest<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();

    private static string ConductLawDigest(TownLawVersion version) => NonviolentDigest(new
    {
        version.Version,
        version.Subject,
        version.Rule,
        version.Scope,
        version.SiteTiles,
        version.ProposalId,
        version.AdoptedTick,
    });

    // This runs at the native successful action, never by searching the public event log.
    // A relevant law makes the act reportable, not illegal; only an agent may make that allegation.
    private void RecordNonviolentConduct(string actor, string kind, GridPoint position, string? targetId,
        string? itemKind, int quantity, string nativeActId)
    {
        if (!inhabitants.ContainsKey(actor)) return;
        var actorTownId = TownForResident(actor);
        foreach (var town in towns.ToArray())
        {
            if (town.Government is null) continue;
            var titles = townLandTitles.Where(title => title.RecordedTick <= WorldTick).ToArray();
            var applicable = TownLawRules.Applicable(town.Government, town.Id, titles,
                actorTownId == town.Id, position, WorldTick);
            if (applicable.Count == 0 || town.Nonviolent.ConductRecords.Any(record => record.Id == nativeActId)) continue;
            var laws = applicable.Select(pair => new TownConductLawSnapshot(pair.Law.Id, pair.Version.Version,
                ConductLawDigest(pair.Version), pair.Version.Scope,
                titles.Where(title => title.TownId == town.Id && title.Tiles.Contains(position))
                    .Select(title => title.Id).Order(StringComparer.Ordinal).ToArray(),
                (town.Governance?.Knowledge ?? []).Where(receipt => receipt.AgentId == actor && receipt.LearnedTick <= WorldTick &&
                    town.Governance!.Notices.Any(notice => notice.Id == receipt.NoticeId && notice.Kind == "law" &&
                        notice.SubjectId == pair.Law.Id && notice.PostedTick >= pair.Version.AdoptedTick && notice.PostedTick <= WorldTick))
                    .Select(receipt => receipt.NoticeId).Order(StringComparer.Ordinal).ToArray())).ToArray();
            var record = new TownConductRecord(nativeActId, actor, kind, position, WorldTick, actorTownId,
                targetId, itemKind, quantity, laws, "");
            record = record with { Version = NonviolentDigest(record) };
            var observations = inhabitants.Values.Where(person => person.InhabitantId == actor ||
                    CanObserveNonviolentAct(person.InhabitantId, actor, position))
                .OrderBy(person => person.InhabitantId, StringComparer.Ordinal)
                .Select(person => new TownConductAcquisition(nativeActId + ":seen:" + person.InhabitantId,
                    nativeActId, person.InhabitantId, "firsthand", WorldTick, person.Position,
                    DescribeNonviolentConduct(record), ObserverHouseholdId: HouseholdFor(person.InhabitantId),
                    PrivateSiteHouseholdId: NonviolentPrivateSite(position)?.HouseholdId)).ToArray();
            SetTown(town with
            {
                Nonviolent = PruneNonviolentConduct(town, town.Nonviolent with
                {
                    ConductRecords = town.Nonviolent.ConductRecords.Append(record).ToArray(),
                    Acquisitions = town.Nonviolent.Acquisitions.Concat(observations).ToArray(),
                })
            });
        }
    }

    private static TownNonviolentState PruneNonviolentConduct(TownRuntimeState town, TownNonviolentState state)
    {
        var retained = state.Cases.Select(file => file.Allegation.IncidentId)
            .Concat((town.Governance?.Proposals ?? []).Where(proposal => proposal.NonviolentRequest is not null)
                .Select(proposal => proposal.NonviolentRequest!.Allegation.IncidentId)).ToHashSet(StringComparer.Ordinal);
        foreach (var record in state.ConductRecords.Where(record => !retained.Contains(record.Id))
                     .OrderByDescending(record => record.Tick).ThenByDescending(record => record.Id, StringComparer.Ordinal)
                     .Take(UnfiledConductLimit)) retained.Add(record.Id);
        return state with
        {
            ConductRecords = state.ConductRecords.Where(record => retained.Contains(record.Id)).ToArray(),
            Acquisitions = state.Acquisitions.Where(item => retained.Contains(item.ConductId)).ToArray(),
        };
    }

    private static bool HasPriorLawNotice(TownRuntimeState town, TownViolationAllegation allegation, string subject) =>
        town.Nonviolent.ConductRecords.Any(record => record.Id == allegation.IncidentId && record.ActorId == subject &&
            record.Tick == allegation.ConductTick && record.Laws.Any(law => law.LawId == allegation.LawId &&
                law.Version == allegation.LawVersion && law.PriorNoticeIds.Count > 0));

    private static bool KnowsConductLaw(TownRuntimeState town, string actor, TownConductLawSnapshot snapshot)
    {
        var version = town.Government?.Laws.FirstOrDefault(law => law.Id == snapshot.LawId)?.Versions
            .FirstOrDefault(item => item.Version == snapshot.Version);
        return version is not null && (town.Governance?.Knowledge ?? []).Any(receipt => receipt.AgentId == actor &&
            town.Governance!.Notices.Any(notice => notice.Id == receipt.NoticeId && notice.Kind == "law" &&
                notice.SubjectId == snapshot.LawId && notice.PostedTick >= version.AdoptedTick &&
                (version.EndedTick is null || notice.PostedTick < version.EndedTick)));
    }

    private bool CanObserveNonviolentAct(string observer, string actor, GridPoint position)
    {
        if (!inhabitants.TryGetValue(observer, out var person) ||
            !IsWithinInteractionRange(person.Position, position, ResourceInteractionRange)) return false;
        // Being beside a private worksite does not grant access to its contents or work inside it.
        var privateSite = NonviolentPrivateSite(position);
        return privateSite is null || HouseholdFor(observer) == privateSite.HouseholdId || observer == actor;
    }

    private PlacedBuilding? NonviolentPrivateSite(GridPoint position) => worldSimulation.Buildings.FirstOrDefault(building =>
        building.HouseholdId is not null && worldContent.Buildings.FirstOrDefault(item => item.CanonicalId == building.DefinitionId) is { } definition &&
        WorldContentSimulationRules.Footprint(definition, building).Contains(position));

    private static string DescribeNonviolentConduct(TownConductRecord record) =>
        $"Observed {record.ConductKind.Replace('_', ' ')} at ({record.Position.X},{record.Position.Y})" +
        (record.ItemKind is null ? "." : $", involving {record.Quantity} {record.ItemKind.Replace('_', ' ')}.");

    private (TownViolationAllegation Allegation, TownCaseEvidence Evidence)[] NonviolentKnownReports(
        TownRuntimeState town, string actor)
    {
        var result = new List<(TownViolationAllegation, TownCaseEvidence)>();
        foreach (var acquisition in town.Nonviolent.Acquisitions.Where(item => item.AgentId == actor)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var record = town.Nonviolent.ConductRecords.Single(item => item.Id == acquisition.ConductId);
            foreach (var law in record.Laws.Where(law => KnowsConductLaw(town, actor, law) ||
                         acquisition.Kind == "record_inspection" && town.Nonviolent.Cases.Any(file => file.Id == acquisition.PublicCaseId &&
                             file.Allegation.LawId == law.LawId && file.Allegation.LawVersion == law.Version)))
                result.Add((new(record.Id, record.ActorId, record.ConductKind, record.Position, record.Tick,
                    law.LawId, law.Version, acquisition.Text, [acquisition.Id]),
                    NonviolentEvidence(record, acquisition, actor, 1)));
        }
        return result.GroupBy(item => (item.Item1.IncidentId, item.Item1.LawId, item.Item1.LawVersion))
            .Select(group => group.OrderBy(item => item.Item2.Kind == "observation" ? 0 : item.Item2.Kind == "record" ? 1 : 2)
                .ThenBy(item => item.Item2.Id, StringComparer.Ordinal).First()).ToArray();
    }

    private TownCaseEvidence[] NonviolentKnownEvidence(TownRuntimeState town,
        TownViolationCase file, string actor) => town.Nonviolent.Acquisitions
        .Where(item => item.AgentId == actor && item.ConductId == file.Allegation.IncidentId)
        .OrderBy(item => item.Id, StringComparer.Ordinal)
        .Select(item => NonviolentEvidence(town.Nonviolent.ConductRecords.Single(record => record.Id == item.ConductId),
            item, actor, file.Revisions[^1].Number))
        .Concat(NonviolentKnownPublicRecords(town, householdLandUseRights, townLandTitles, actor,
            file.Allegation.Position, file.Revisions[^1].Number, WorldTick)).ToArray();

    private TownCaseEvidence NonviolentEvidence(TownConductRecord record, TownConductAcquisition acquired,
        string actor, int revision) => new(acquired.Id, revision,
        acquired.Kind == "firsthand" ? "observation" : acquired.Kind == "record_inspection" ? "record" : "allegation",
        acquired.Kind, acquired.SourceAgentId ?? actor,
        record.Id, record.Version, acquired.Tick, actor, WorldTick, acquired.Text);

    private TownNonviolentState AcquireNonviolentInspectedSources(TownRuntimeState town, TownViolationCase file, string actor)
    {
        if (!inhabitants.TryGetValue(actor, out var reader) || !TownNonviolentRules.ReadCurrent(file, actor, WorldTick))
            return town.Nonviolent;
        var state = town.Nonviolent;
        foreach (var evidence in file.Evidence.Where(item => item.Kind is "observation" or "record" &&
                     item.SourceRecordId == file.Allegation.IncidentId && item.SubmittedTick <= WorldTick))
        {
            var original = state.Acquisitions.FirstOrDefault(item => item.Id == evidence.Id);
            if (original is null || state.Acquisitions.Any(item => item.AgentId == actor && item.ConductId == original.ConductId &&
                    item.Kind == "record_inspection" && item.PublicCaseId == file.Id)) continue;
            var acquired = new TownConductAcquisition(original.ConductId + ":inspect:" + file.Id + ":" + actor,
                original.ConductId, actor, "record_inspection", WorldTick, reader.Position, evidence.Text,
                actor, original.Id, ObserverHouseholdId: HouseholdFor(actor), PublicCaseId: file.Id);
            state = state with { Acquisitions = state.Acquisitions.Append(acquired).ToArray() };
        }
        return state;
    }

    private bool RelayNonviolentKnownReport(TownRuntimeState town, string source, string recipient,
        string incidentId, string statement)
    {
        if (!inhabitants.TryGetValue(source, out var speaker) || !inhabitants.TryGetValue(recipient, out var listener) ||
            source == recipient || !IsWithinInteractionRange(speaker.Position, listener.Position, ResourceInteractionRange) ||
            string.IsNullOrWhiteSpace(statement) || statement.Length > TownGovernanceRules.MaximumProposalText || statement.Any(char.IsControl)) return false;
        town = towns.Single(item => item.Id == town.Id);
        if (town.Nonviolent.Acquisitions.Any(item => item.AgentId == recipient && item.ConductId == incidentId)) return true;
        var original = town.Nonviolent.Acquisitions.Where(item => item.AgentId == source && item.ConductId == incidentId)
            .OrderBy(item => item.Tick).ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
        if (original is null) return false;
        var id = $"{incidentId}:relay:{WorldTick}:{source}:{recipient}";
        if (town.Nonviolent.Acquisitions.Any(item => item.Id == id)) return true;
        var acquired = new TownConductAcquisition(id, incidentId, recipient, "relay", WorldTick,
            listener.Position, statement.Trim(), source, original.Id, speaker.Position, HouseholdFor(recipient));
        SetTown(town with { Nonviolent = town.Nonviolent with { Acquisitions = town.Nonviolent.Acquisitions.Append(acquired).ToArray() } });
        return true;
    }

    private bool NonviolentRemedyTermKnownTo(TownRuntimeState town, TownRemedyTerm term, string actor)
    {
        if (term.TargetId is null) return true;
        if (term.Kind == "public_service_goods") return worldSimulation.Buildings.Any(building =>
            building.InstanceId == term.TargetId && building.TownId == town.Id &&
            (TownForResident(actor) == town.Id || inhabitants.TryGetValue(actor, out var person) &&
                IsWithinInteractionRange(person.Position, building.Position, ResourceInteractionRange)));
        var lot = society.Checkpoint.Inventory.Lots.FirstOrDefault(item => item.Id == term.TargetId);
        if (lot is null) return false;
        if (lot.OwnerId == actor || lot.OwnerId == HouseholdFor(actor)) return true;
        return inhabitants.TryGetValue(actor, out var observer) && inhabitants.TryGetValue(term.ContributorId, out var contributor) &&
            IsWithinInteractionRange(observer.Position, contributor.Position, ResourceInteractionRange) &&
            ToolProgressionRules.IsTopLevelCarriedLot(lot, term.ContributorId) &&
            CanObserveNonviolentAct(actor, term.ContributorId, contributor.Position);
    }

    private bool NonviolentWorkAllowed(string actor) => AdultResident(actor) && inhabitants.TryGetValue(actor, out var person) &&
        !NeedsUrgentFood(person) && !NeedsUrgentWarmth(person) && !IllDependentsNeedingCare(actor).Any() &&
        !ChildrenNeedingCare(actor).Any() &&
        SettlementIllnessRules.AllowsWork(actor, WorldTick, person.Survival?.IllnessBasisPoints ?? 0);

    private bool NonviolentEquipmentRepairPaused(string actor) => inhabitants[actor].Equipment?.Repair is { } repair &&
        towns.Any(town => town.Nonviolent.Agreements.Any(agreement => agreement.Status is "pending" or "overdue" &&
            !TownRemedyRules.IsSuperseded(town.Nonviolent, agreement.Id) && agreement.Terms.Any(term =>
                term.Kind == "repair_equipment" && term.ContributorId == actor && term.TargetId == repair.LotId &&
                NonviolentRemaining(town.Nonviolent, agreement, term) > 0))) &&
        (IllDependentsNeedingCare(actor).Any() || ChildrenNeedingCare(actor).Any());

    private InventoryLot? NonviolentReturnLot(TownRemedyTerm term) => society.Checkpoint.Inventory.Lots
        .Where(lot => lot.OwnerId == term.ContributorId && ToolProgressionRules.IsTopLevelCarriedLot(lot, term.ContributorId) &&
            lot.DeliveryBuildingId is null && lot.ItemKind == term.ItemKind && (term.TargetId is null || lot.Id == term.TargetId) &&
            !InventoryContainerRules.IsContainer(lot.ItemKind) &&
            !PersonalEquipmentRules.IsSelected(inhabitants[term.ContributorId].Equipment, lot.Id) && AvailableLotQuantity(lot) >= term.Quantity)
        .OrderBy(lot => lot.Id, StringComparer.Ordinal).FirstOrDefault();

    private bool NonviolentRemedyFeasible(TownRuntimeState town, TownRemedyTerm term)
    {
        if (term.Quantity <= 0 || !NonviolentWorkAllowed(term.ContributorId)) return false;
        var actor = term.ContributorId;
        var person = inhabitants[actor];
        if (term.Kind == "return_goods")
            return term.BeneficiaryId is { } recipient && recipient != actor && inhabitants.TryGetValue(recipient, out var beneficiary) &&
                IsWithinInteractionRange(person.Position, beneficiary.Position, ResourceInteractionRange) &&
                CanObserveNonviolentAct(actor, recipient, beneficiary.Position) &&
                NonviolentReturnLot(term) is { } lot && AvailableLotQuantity(lot) >= term.Quantity &&
                (!IsEdibleFood(lot.ItemKind) || person.HungerBasisPoints >= 6_500 &&
                    PreferredFood(actor, actor).Sum(AvailableLotQuantity) - term.Quantity >= 2) &&
                FreeCarryCapacity(recipient) >= term.Quantity;
        if (term.Kind == "public_service_goods")
        {
            var warehouse = WarehouseForResident(actor);
            return term.BeneficiaryId == town.Id && warehouse is not null && warehouse.TownId == town.Id &&
                (term.TargetId is null || term.TargetId == warehouse.InstanceId) && WarehouseResourceKinds.Contains(term.ItemKind ?? "") &&
                StorageRoom(warehouse.InstanceId) >= term.Quantity &&
                PersonalWarehouseAvailableQuantity(actor, term.ItemKind!) - WarehouseLoadQuantity >= term.Quantity &&
                (person.Position == warehouse.Position || FindUnoccupiedRoute(actor, person.Position, warehouse.Position, 0).Count > 0);
        }
        if (term.Kind != "repair_equipment" || term.Quantity != 1 || term.TargetId is null) return false;
        var predecessor = town.Nonviolent.Offers.Where(offer => offer.Terms.Any(item => item.Id == term.Id))
            .Select(offer => offer.ReplacesOfferId is { } previous ? town.Nonviolent.Offers.FirstOrDefault(item => item.Id == previous)?.AgreementId : null)
            .FirstOrDefault(id => id is not null);
        if (towns.Select(other => other.Id == town.Id ? town : other).Any(other => other.Nonviolent.Agreements.Any(agreement =>
                agreement.Status is "pending" or "overdue" && !TownRemedyRules.IsSuperseded(other.Nonviolent, agreement.Id) &&
                !(other.Id == town.Id && agreement.Id == predecessor) && agreement.Terms.Any(existing =>
                    existing.Kind == "repair_equipment" && existing.ContributorId == term.ContributorId && existing.TargetId == term.TargetId &&
                    NonviolentRemaining(other.Nonviolent, agreement, existing) > 0 &&
                    (other.Id != town.Id || existing.Id != term.Id))))) return false;
        var target = society.Checkpoint.Inventory.Lots.FirstOrDefault(lot => lot.Id == term.TargetId && lot.ItemKind == term.ItemKind &&
            lot.ConditionBasisPoints < 10_000 && (lot.OwnerId == actor || lot.OwnerId == HouseholdFor(actor)));
        if (target is null || term.BeneficiaryId != target.OwnerId) return false;
        if (person.Equipment?.Repair?.LotId == target.Id && CanContinueEquipmentRepair(actor)) return true;
        if (!NonviolentRepairMaterials(target.ItemKind).All(input => HasCarriedMaterial(actor, input.ResourceId, input.Amount))) return false;
        var native = new List<CognitionCandidate>();
        AddToolRepairCandidates(native, actor);
        AddEquipmentCandidates(native, actor, person);
        AddHandcartCandidates(native, actor, person);
        return native.Any(candidate => candidate.Id == RepairToolPrefix + target.Id || candidate.Id == RepairCartPrefix + target.Id ||
            candidate.Id == "repair_equipment" && WornEquipmentItems(actor).Any(lot => lot.Id == target.Id) &&
                CanPrepareEquipmentRepair(actor, person, target));
    }

    private bool NonviolentRemedyDurationFeasible(TownRuntimeState town, TownRemedyTerm[] terms, long ticks)
    {
        if (ticks <= 0 || terms.Length == 0 || terms.Any(term => !NonviolentRemedyFeasible(town, term)) ||
            !NonviolentRemedyCapacityFeasible(terms)) return false;
        foreach (var group in terms.GroupBy(term => term.ContributorId, StringComparer.Ordinal))
        {
            var actor = group.Key;
            var person = inhabitants[actor];
            var position = person.Position;
            long needed = 0;
            var used = new Dictionary<string, long>(StringComparer.Ordinal);
            var namedReturns = new Dictionary<string, long>(StringComparer.Ordinal);
            var repaired = new HashSet<string>(StringComparer.Ordinal);
            foreach (var term in group)
            {
                GridPoint destination;
                long work;
                if (term.Kind == "repair_equipment")
                {
                    if (!repaired.Add(term.TargetId!)) return false;
                    var target = society.Checkpoint.Inventory.GetLot(term.TargetId!);
                    var inProgress = person.Equipment?.Repair?.LotId == target.Id ? person.Equipment.Repair : null;
                    var materials = inProgress is null ? NonviolentRepairMaterials(target.ItemKind) : [];
                    foreach (var input in materials)
                        used[input.ResourceId] = used.GetValueOrDefault(input.ResourceId) + input.Amount;
                    if (target.ItemKind == InventoryContainerRules.Handcart)
                    {
                        destination = new(target.GroundPosition!.Value.X, target.GroundPosition.Value.Y);
                        work = 1;
                    }
                    else if (ToolProgressionRules.Find(target.ItemKind) is not null)
                    {
                        destination = HouseholdBuildingWithTag(HouseholdFor(actor), "blacksmith")!.Position;
                        work = 1;
                    }
                    else
                    {
                        destination = EquipmentRepairSite(actor, target)!.Position;
                        work = inProgress is null ? 1 + PersonalEquipmentRules.RepairWorkTicks :
                            PersonalEquipmentRules.RepairWorkTicks - inProgress.WorkDone;
                    }
                }
                else
                {
                    used[term.ItemKind!] = used.GetValueOrDefault(term.ItemKind!) + term.Quantity;
                    if (term.Kind == "return_goods" && term.TargetId is { } source)
                        namedReturns[source] = namedReturns.GetValueOrDefault(source) + term.Quantity;
                    destination = term.Kind == "return_goods" ? inhabitants[term.BeneficiaryId!].Position : WarehouseForResident(actor)!.Position;
                    work = term.Kind == "return_goods" ? 1 : ((long)term.Quantity + WarehouseLoadQuantity - 1) / WarehouseLoadQuantity;
                }
                var range = term.Kind == "return_goods" ? ResourceInteractionRange : 0;
                if (!IsWithinInteractionRange(position, destination, range))
                {
                    if (position == person.Position) needed += person.TravelCooldownTicks;
                    var route = FindUnoccupiedRoute(actor, position, destination, range);
                    if (route.Count < 2) return false;
                    for (var step = 1; step < route.Count; step++)
                    {
                        // The last arrival needs one tick; its cooldown delays later travel, not onsite work.
                        needed += step == route.Count - 1 ? 1 : (TravelStepCost(actor, route[step - 1], route[step]) + 99) / 100 +
                            SettlementIllnessRules.TravelDelayTicks(person.Survival?.IllnessBasisPoints ?? 0);
                    }
                    position = route[^1];
                }
                needed += work;
                if (needed > ticks) return false;
            }
            foreach (var promise in namedReturns)
                if (AvailableLotQuantity(society.Checkpoint.Inventory.GetLot(promise.Key)) < promise.Value) return false;
            foreach (var expenditure in used)
            {
                var stock = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                    ToolProgressionRules.IsTopLevelCarriedLot(lot, actor) && lot.DeliveryBuildingId is null &&
                    lot.ItemKind == expenditure.Key).Sum(lot => (long)AvailableLotQuantity(lot));
                var reserve = group.Any(term => term.Kind == "public_service_goods" && term.ItemKind == expenditure.Key)
                    ? WarehouseLoadQuantity : IsEdibleFood(expenditure.Key) ? 2 : 0;
                if (stock - reserve < expenditure.Value) return false;
            }
        }
        return true;
    }

    private bool NonviolentRemedyCapacityFeasible(IEnumerable<TownRemedyTerm> terms)
    {
        foreach (var returns in terms.Where(term => term.Kind == "return_goods")
                     .GroupBy(term => term.BeneficiaryId!, StringComparer.Ordinal))
            if (!inhabitants.ContainsKey(returns.Key) || returns.Sum(term => (long)term.Quantity) > FreeCarryCapacity(returns.Key)) return false;
        var deliveries = terms.Where(term => term.Kind == "public_service_goods")
            .Select(term => (Term: term, Warehouse: WarehouseForResident(term.ContributorId))).ToArray();
        if (deliveries.Any(delivery => delivery.Warehouse is null)) return false;
        foreach (var storage in deliveries.GroupBy(delivery => delivery.Warehouse!.InstanceId, StringComparer.Ordinal))
            if (storage.Sum(delivery => (long)delivery.Term.Quantity) > StorageRoom(storage.Key)) return false;
        return true;
    }

    private static string NonviolentRemedyCandidateId(TownRuntimeState town, TownRestorativeAgreement agreement, TownRemedyTerm term) =>
        NonviolentRemedyPrefix + NonviolentDigest(new { Town = town.Id, Agreement = agreement.Id, Term = term.Id });

    private static int NonviolentRemaining(TownNonviolentState state, TownRestorativeAgreement agreement, TownRemedyTerm term) =>
        term.Quantity - state.Effects.Where(effect => effect.AgreementId == agreement.Id && effect.TermId == term.Id).Sum(effect => effect.Quantity);

    private void AddNonviolentRemedyCandidates(List<CognitionCandidate> candidates, string actor, TownRuntimeState town)
    {
        if (inhabitants.TryGetValue(actor, out var speaker))
            foreach (var record in town.Nonviolent.ConductRecords.Where(record =>
                         town.Nonviolent.Acquisitions.Any(item => item.ConductId == record.Id && item.AgentId == actor)))
                foreach (var listener in inhabitants.Values.Where(person => person.InhabitantId != actor &&
                             IsWithinInteractionRange(speaker.Position, person.Position, ResourceInteractionRange) &&
                             !town.Nonviolent.Acquisitions.Any(item => item.ConductId == record.Id && item.AgentId == person.InhabitantId)))
                    candidates.Add(new(NonviolentRelayCandidateId(town, record, listener.InhabitantId),
                        "Tell this nearby person what you observed or were told about this act; it remains a report.",
                        185, listener.InhabitantId));
        if (!NonviolentWorkAllowed(actor)) return;
        foreach (var agreement in town.Nonviolent.Agreements.Where(item => item.Status is "pending" or "overdue" &&
                     !TownRemedyRules.IsSuperseded(town.Nonviolent, item.Id))
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
            foreach (var term in agreement.Terms.Where(item => item.ContributorId == actor))
            {
                var remaining = NonviolentRemaining(town.Nonviolent, agreement, term);
                if (remaining <= 0 || !NonviolentRemedyFeasible(town, term with { Quantity = remaining })) continue;
                candidates.Add(new(NonviolentRemedyCandidateId(town, agreement, term),
                    $"Voluntarily complete agreed {term.Kind.Replace('_', ' ')} using real goods or repair work.", 185, term.TargetId));
            }
    }

    private bool ApplyNonviolentRemedyAction(TownRuntimeState town, string actor, string candidateId)
    {
        if (candidateId.StartsWith(NonviolentRelayPrefix, StringComparison.Ordinal))
        {
            foreach (var record in town.Nonviolent.ConductRecords)
                foreach (var listener in inhabitants.Keys)
                    if (candidateId == NonviolentRelayCandidateId(town, record, listener))
                    {
                        var known = town.Nonviolent.Acquisitions.FirstOrDefault(item => item.ConductId == record.Id && item.AgentId == actor);
                        if (known is not null) RelayNonviolentKnownReport(town, actor, listener, record.Id, known.Text);
                        return true;
                    }
            return false;
        }
        if (!candidateId.StartsWith(NonviolentRemedyPrefix, StringComparison.Ordinal)) return false;
        town = towns.Single(item => item.Id == town.Id);
        foreach (var agreement in town.Nonviolent.Agreements.Where(item => item.Status is "pending" or "overdue" &&
                     !TownRemedyRules.IsSuperseded(town.Nonviolent, item.Id)))
            foreach (var term in agreement.Terms.Where(item => item.ContributorId == actor))
            {
                if (candidateId != NonviolentRemedyCandidateId(town, agreement, term)) continue;
                var remaining = NonviolentRemaining(town.Nonviolent, agreement, term);
                if (remaining <= 0 || !NonviolentRemedyFeasible(town, term with { Quantity = remaining })) return true;
                var previousSelection = nonviolentRemedySelection;
                nonviolentRemedySelection = (town.Id, agreement.Id, term.Id);
                try
                {
                    if (term.Kind == "return_goods")
                    {
                        var lot = NonviolentReturnLot(term with { Quantity = remaining })!;
                        var operation = $"remedy-return:{WorldTick}:{actor}:{nextEventId}";
                        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, operation, actor,
                            term.BeneficiaryId!, lot.Id, remaining, "voluntary_restitution"));
                        RecordNonviolentGoodsCompletion(actor, lot, term.BeneficiaryId!, remaining, null, operation, "return_goods");
                        AppendEvent("voluntary_goods_returned", $"{actor}:{term.BeneficiaryId}:{lot.Id}:{remaining}");
                    }
                    else if (term.Kind == "public_service_goods")
                        DeliverNonviolentTownGoods(actor, term, remaining);
                    else
                    {
                        var person = inhabitants[actor];
                        var target = society.Checkpoint.Inventory.GetLot(term.TargetId!);
                        if (target.ItemKind == InventoryContainerRules.Handcart)
                            ApplyHandcartCandidate(actor, person, RepairCartPrefix + target.Id);
                        else if (ToolProgressionRules.Find(target.ItemKind) is not null) RepairTool(actor, person, target.Id);
                        else if (person.Equipment?.Repair?.LotId == target.Id) ContinueEquipmentRepair(actor);
                        else RepairEquipment(actor, person, target.Id);
                    }
                }
                finally { nonviolentRemedySelection = previousSelection; }
                return true;
            }
        return false;
    }

    private static string NonviolentRelayCandidateId(TownRuntimeState town, TownConductRecord record, string listener) =>
        NonviolentRelayPrefix + NonviolentDigest(new { Town = town.Id, Act = record.Id, Listener = listener });

    private void DeliverNonviolentTownGoods(string actor, TownRemedyTerm term, int remaining)
    {
        var warehouse = WarehouseForResident(actor)!;
        if (inhabitants[actor].Position != warehouse.Position)
        {
            MoveToward(actor, inhabitants[actor], warehouse.Position, "voluntary_town_service", 0);
            return;
        }
        if (PersonalWarehouseSurplus(actor, term.ItemKind) is not { } surplus) return;
        var lot = surplus.Lot;
        var quantity = Math.Min(remaining, Math.Min(WarehouseLoadQuantity,
            Math.Min(StorageRoom(warehouse.InstanceId), surplus.Quantity)));
        if (quantity <= 0) return;
        var availableBefore = PersonalWarehouseAvailableQuantity(actor, lot.ItemKind);
        var operation = $"warehouse-stock:{WorldTick}:{actor}";
        ApplyInventoryTransition(inventory => InventoryFixture.Transfer(inventory, operation, actor, warehouse.TownId!,
            lot.Id, quantity, "town_resources_stored", warehouse.InstanceId));
        RecordNonviolentGoodsCompletion(actor, lot, warehouse.TownId!, quantity, warehouse.InstanceId, operation, "public_service_goods", availableBefore);
        AppendEvent("town_resources_stored", $"{actor}:{lot.Id}:{quantity}:{warehouse.InstanceId}");
    }

    private void RecordNonviolentRepairCompletion(string actor, InventoryLot originalTarget, string repairedLotId,
        string nativeReceiptId, GridPoint position, IReadOnlyList<string> reservationIds)
    {
        var result = society.Checkpoint.Inventory.GetLot(repairedLotId);
        if (result.ConditionBasisPoints <= originalTarget.ConditionBasisPoints) return;
        var receipt = new TownNativeRemedyReceipt(nativeReceiptId, "repair_equipment", actor, WorldTick,
            originalTarget.OwnerId, originalTarget.ItemKind, 1, originalTarget.Id, originalTarget.Id,
            repairedLotId, originalTarget.OwnerId, result.OwnerId, position,
            originalTarget.ConditionBasisPoints, result.ConditionBasisPoints, "", HouseholdFor(actor), TownForResident(actor),
            originalTarget.Quantity, 1, NonviolentRepairMaterials(originalTarget.ItemKind).Zip(reservationIds,
                (input, id) => new TownNativeRepairInput(input.ResourceId, society.Checkpoint.Inventory.GetReservation(id))).ToArray());
        RecordNonviolentNativeReceipt(receipt);
        RecordNonviolentConduct(actor, "repair_equipment", position, originalTarget.Id, originalTarget.ItemKind, 1, nativeReceiptId);
    }

    private void RecordNonviolentGoodsCompletion(string actor, InventoryLot sourceLot, string recipientId,
        int quantity, string? storageBuildingId, string nativeReceiptId, string kind, long? carriedAvailableBefore = null)
    {
        var resultId = quantity == sourceLot.Quantity ? sourceLot.Id : sourceLot.Id + "#transfer:" + nativeReceiptId;
        var result = society.Checkpoint.Inventory.GetLot(resultId);
        var receipt = new TownNativeRemedyReceipt(nativeReceiptId, kind, actor, WorldTick, recipientId,
            sourceLot.ItemKind, quantity, storageBuildingId ?? sourceLot.Id, sourceLot.Id, resultId,
            sourceLot.OwnerId, result.OwnerId, inhabitants[actor].Position,
            sourceLot.ConditionBasisPoints, result.ConditionBasisPoints, "", HouseholdFor(actor), TownForResident(actor),
            sourceLot.Quantity, AvailableLotQuantity(sourceLot), [], carriedAvailableBefore);
        RecordNonviolentNativeReceipt(receipt);
        RecordNonviolentConduct(actor, kind, receipt.Position, receipt.TargetId, receipt.ItemKind, quantity, nativeReceiptId);
    }

    private void RecordNonviolentNativeReceipt(TownNativeRemedyReceipt receipt)
    {
        if (towns.Any(town => town.Nonviolent.NativeReceipts.Any(item => item.Id == receipt.Id))) return;
        // One physical completion cannot be spent against two agreements or two Towns.
        foreach (var town in towns.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray())
            foreach (var agreement in town.Nonviolent.Agreements.Where(item => item.Status is "pending" or "overdue" &&
                     !TownRemedyRules.IsSuperseded(town.Nonviolent, item.Id))
                         .OrderBy(item => item.AcceptedTick).ThenBy(item => item.Id, StringComparer.Ordinal))
                foreach (var term in agreement.Terms)
                {
                    if (nonviolentRemedySelection is { } selected &&
                        (selected.TownId != town.Id || selected.AgreementId != agreement.Id || selected.TermId != term.Id)) continue;
                    if (agreement.AcceptedTick > receipt.Tick || term.Kind != receipt.Kind || term.ContributorId != receipt.ActorId ||
                        term.BeneficiaryId != receipt.BeneficiaryId || term.ItemKind != receipt.ItemKind ||
                        term.TargetId is not null && term.TargetId != receipt.TargetId) continue;
                    var remaining = NonviolentRemaining(town.Nonviolent, agreement, term);
                    if (remaining <= 0) continue;
                    receipt = receipt with { Version = NonviolentDigest(receipt) };
                    var effect = new TownRemedyEffect("effect:" + receipt.Id, agreement.Id, term.Id, receipt.ActorId,
                        receipt.Tick, receipt.Kind, receipt.BeneficiaryId, receipt.ItemKind, Math.Min(remaining, receipt.Quantity),
                        term.TargetId, receipt.Id, receipt.Version);
                    var state = town.Nonviolent with { NativeReceipts = town.Nonviolent.NativeReceipts.Append(receipt).ToArray() };
                    SetTown(town with { Nonviolent = TownRemedyRules.RecordEffect(state, effect) });
                    AppendEvent("law_case_remedy_effect", $"{town.Id}|{agreement.Id}|{receipt.ActorId}", receipt.Position);
                    return;
                }
    }

    private static IReadOnlyList<ContentQuantity> NonviolentRepairMaterials(string itemKind) =>
        itemKind == InventoryContainerRules.Handcart ? [new("wood", 1), new("iron_fittings", 1), new("rope", 1)] :
        ToolProgressionRules.Find(itemKind) is not null ? ToolProgressionRules.RepairMaterials(itemKind) :
        PersonalEquipmentRules.RepairMaterials(itemKind);
}
