using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Society;
using System.Text;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static readonly HashSet<string> GenericGuardianInstructionWords = new(StringComparer.Ordinal)
    {
        "accept", "accepted", "accepting", "care", "caregiver", "child", "children", "dependent", "dependents",
        "guardian", "guardianship", "infant", "infants", "orphan", "orphans", "primary", "please", "take",
        "in", "of", "for", "to", "the", "a", "an", "this", "that", "one", "whoever", "any", "someone", "and",
        "help", "raise", "responsibility", "become", "be", "should", "must", "can", "could", "would", "your", "their",
        "our", "my", "household", "home", "family", "into",
    };

    private long GuardianStageTicks => Math.Max(1, worldSystems.Config.TicksPerDay);

    private bool NeedsCaregiver(string child) => inhabitants.ContainsKey(child) &&
        society.Checkpoint.GetInhabitant(child).AgeBand is SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent &&
        !SocietyFixture.HasActivePrimaryCaregiver(society.Checkpoint, child);

    private bool CanAcceptGuardian(string adult, string child, bool ordered = false) => AdultResident(adult) &&
        ReadyForBriefInteraction(adult) &&
        NeedsCaregiver(child) && (ordered || inhabitants[child].GuardianSearch is { } search &&
            search.OfferedAdultIds.Contains(adult, StringComparer.Ordinal) &&
            GuardianAdultsForStage(child, search.Stage).Contains(adult, StringComparer.Ordinal));

    private string? GuardianTargetForInstruction(string text)
    {
        var pending = inhabitants.Keys.Where(NeedsCaregiver).Order(StringComparer.Ordinal).ToArray();
        const string directIdPrefix = "guardian_accept:";
        var normalized = text.Trim();
        if (normalized.StartsWith(directIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var requestedId = normalized[directIdPrefix.Length..];
            return pending.SingleOrDefault(id => string.Equals(id, requestedId, StringComparison.Ordinal));
        }

        var matches = pending.Where(child =>
        {
            var name = society.Checkpoint.GetInhabitant(child).Name;
            return ContainsWholeGuardianTarget(text, name, isIdentifier: false) ||
                ContainsWholeGuardianTarget(text, child, isIdentifier: true);
        }).ToArray();
        if (matches.Length > 1)
            return null;

        if (matches.Length == 1)
        {
            var child = matches[0];
            var name = society.Checkpoint.GetInhabitant(child).Name;
            var remainder = RemoveWholeGuardianTarget(text, name, isIdentifier: false);
            remainder = RemoveWholeGuardianTarget(remainder, child, isIdentifier: true);
            return InstructionWords(remainder).All(GenericGuardianInstructionWords.Contains) ? child : null;
        }

        // A generic request may mean the only dependent needing a guardian.
        // Any other word is treated as an explicit target, even when that
        // target is unknown, so "care for Bob" cannot silently select Alice.
        return pending.Length == 1 && InstructionWords(text).All(GenericGuardianInstructionWords.Contains)
            ? pending[0]
            : null;
    }

    private static bool ContainsWholeGuardianTarget(string text, string target, bool isIdentifier)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var start = 0;
        while ((start = text.IndexOf(target, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = start + target.Length;
            var beforeIsPart = start > 0 && GuardianTargetCharacter(text[start - 1], isIdentifier);
            var afterIsPart = end < text.Length && GuardianTargetCharacter(text[end], isIdentifier);
            if (!beforeIsPart && !afterIsPart)
                return true;
            start = end;
        }

        return false;
    }

    private static string RemoveWholeGuardianTarget(string text, string target, bool isIdentifier)
    {
        if (string.IsNullOrWhiteSpace(target))
            return text;

        var result = new StringBuilder(text.Length);
        var copiedThrough = 0;
        var searchFrom = 0;
        while ((searchFrom = text.IndexOf(target, searchFrom, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = searchFrom + target.Length;
            var beforeIsPart = searchFrom > 0 && GuardianTargetCharacter(text[searchFrom - 1], isIdentifier);
            var afterIsPart = end < text.Length && GuardianTargetCharacter(text[end], isIdentifier);
            if (!beforeIsPart && !afterIsPart)
            {
                result.Append(text, copiedThrough, searchFrom - copiedThrough);
                result.Append(' ', target.Length);
                copiedThrough = end;
            }
            searchFrom = end;
        }

        result.Append(text, copiedThrough, text.Length - copiedThrough);
        return result.ToString();
    }

    private static bool GuardianTargetCharacter(char value, bool isIdentifier) =>
        char.IsLetterOrDigit(value) || value == '_' || value == '-' || isIdentifier && value == ':' ||
        !isIdentifier && value is '\'' or '’';

    private bool EligibleCaregiver(string adult, string child) => AdultResident(adult) && NeedsCaregiver(child) &&
        society.Checkpoint.GetInhabitant(adult).HouseholdId is { } household &&
        society.Checkpoint.GetInhabitant(child).HouseholdId == household;

    private IEnumerable<SocietyRelationship> CareProposals() => society.Checkpoint.Relationships.Where(edge =>
        edge.Type == SocietyRelationshipType.Caregiver && edge.State == SocietyRelationshipState.Proposed &&
        edge.Id.StartsWith("settlement-care:", StringComparison.Ordinal));

    private IEnumerable<string> IllDependentsNeedingCare(string adult) => society.Checkpoint.Relationships
        .Where(edge => edge.Type == SocietyRelationshipType.Caregiver && edge.ProposerId == adult &&
            edge.State == SocietyRelationshipState.Accepted && edge.EffectiveTick <= WorldTick &&
            inhabitants.TryGetValue(edge.TargetId, out var recipient) &&
            society.Checkpoint.GetInhabitant(edge.TargetId).AgeBand != SocietyAgeBand.Infant &&
            recipient.Survival is { IllnessBasisPoints: >= 2_500 } && !RecentlyCaredForIllness(edge.TargetId))
        .Select(edge => edge.TargetId);

    private bool CanOfferCare(string adult, string child) => EligibleCaregiver(adult, child) &&
        inhabitants[child].GuardianSearch is null &&
        !CareProposals().Any(edge => edge.TargetId == child) &&
        !society.Checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.ProposerId == adult && edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted) &&
        !society.Checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.ProposerId == adult && edge.TargetId == child && WorldTick - Math.Max(edge.ProposedTick, edge.EffectiveTick) < worldSystems.Config.TicksPerDay);

    private bool CanAssumePrimaryCare(string adult, string child) => AdultResident(adult) && NeedsCaregiver(child) &&
        inhabitants[child].GuardianSearch is null &&
        society.Checkpoint.GetInhabitant(adult).HouseholdId is { } household &&
        society.Checkpoint.GetInhabitant(child).HouseholdId == household &&
        society.Checkpoint.Relationships.Any(edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.ProposerId == adult && edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted);

    private bool HasDependentCareDecision(string actor) => ReadyForBriefInteraction(actor) &&
        (CareProposals().Any(edge => edge.TargetId == actor) ||
         inhabitants.Keys.Any(child => CanOfferCare(actor, child) || CanAssumePrimaryCare(actor, child) ||
             CanAcceptGuardian(actor, child)));

    private void AddOrderedGuardianCandidate(List<CognitionCandidate> candidates, string actor, string? instructionText)
    {
        if (instructionText is null || InstructionCandidate(instructionText) != "guardian_accept" ||
            !AdultResident(actor) || !ReadyForBriefInteraction(actor) ||
            GuardianTargetForInstruction(instructionText) is not { } child ||
            !NeedsCaregiver(child) || candidates.Any(candidate => candidate.Id == "guardian_accept:" + child))
            return;
        candidates.Add(new("guardian_accept:" + child,
            $"Accept primary care of {society.Checkpoint.GetInhabitant(child).Name}. They can move into your household only if your House has room and you share their Town.", 3));
    }

    private void MaintainDependentCare()
    {
        foreach (var edge in CareProposals().ToArray())
        {
            if (!EligibleCaregiver(edge.ProposerId, edge.TargetId) || WorldTick - edge.ProposedTick > 120)
            {
                society.Apply(checkpoint => SocietyFixture.RevokeRelationship(checkpoint, edge.Id, edge.ProposerId));
                AppendEvent("caregiver_proposal_expired", edge.TargetId);
            }
        }

        foreach (var child in inhabitants.Keys.Order(StringComparer.Ordinal).ToArray())
        {
            if (!NeedsCaregiver(child))
            {
                if (inhabitants[child].GuardianSearch is not null)
                    SetGuardianSearch(child, null);
                continue;
            }

            var search = inhabitants[child].GuardianSearch;
            if (search is null)
            {
                search = NewGuardianSearch(child, WorldTick);
                SetGuardianSearch(child, search);
                AppendEvent("guardian_needed", child);
                continue;
            }

            var stageAdults = GuardianAdultsForStage(child, search.Stage);
            if (search.Stage != "town" && (stageAdults.Length == 0 ||
                WorldTick - search.StageStartedTick >= GuardianStageTicks))
            {
                search = NextGuardianStage(child, search);
            }
            else
            {
                var currentAdults = stageAdults;
                if (!search.OfferedAdultIds.SequenceEqual(currentAdults, StringComparer.Ordinal))
                    search = search with { OfferedAdultIds = currentAdults };
            }
            if (search != inhabitants[child].GuardianSearch)
                SetGuardianSearch(child, search);
        }
    }

    private SettlementGuardianSearch NewGuardianSearch(string child, long tick)
    {
        foreach (var stage in new[] { "relatives", "household", "town" })
        {
            var adults = GuardianAdultsForStage(child, stage);
            if (adults.Length > 0 || stage == "town")
                return new(stage, tick, tick, adults);
        }
        throw new InvalidOperationException("A guardian search must have a final Town stage.");
    }

    private SettlementGuardianSearch NextGuardianStage(string child, SettlementGuardianSearch previous)
    {
        var nextStage = previous.Stage switch
        {
            "relatives" => "household",
            "household" => "town",
            _ => "town",
        };
        while (true)
        {
            var adults = GuardianAdultsForStage(child, nextStage);
            if (adults.Length > 0 || nextStage == "town")
                return new(nextStage, previous.StartedTick, WorldTick, adults);
            nextStage = "town";
        }
    }

    private string[] GuardianAdultsForStage(string child, string stage)
    {
        return GuardianAdultsForStage(society.Checkpoint, towns, child, stage);
    }

    private static string[] GuardianAdultsForStage(
        SocietyCheckpoint checkpoint,
        IReadOnlyList<TownRuntimeState> towns,
        string child,
        string stage)
    {
        var relatives = LivingAdultRelatives(checkpoint, child).ToHashSet(StringComparer.Ordinal);
        var householdId = checkpoint.GetInhabitant(child).HouseholdId;
        var householdAdults = householdId is null ? [] : checkpoint.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active &&
                person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder &&
                person.HouseholdId == householdId)
            .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();
        return stage switch
        {
            "relatives" => relatives.Order(StringComparer.Ordinal).ToArray(),
            "household" => householdAdults.Where(id => !relatives.Contains(id)).ToArray(),
            "town" => TownAdultsFor(checkpoint, towns, child).Where(id => !relatives.Contains(id) &&
                !householdAdults.Contains(id, StringComparer.Ordinal)).ToArray(),
            _ => throw new InvalidDataException("The saved guardian search stage is invalid."),
        };
    }

    private string[] TownAdultsFor(string child) => TownAdultsFor(society.Checkpoint, towns, child);

    private static string[] TownAdultsFor(SocietyCheckpoint checkpoint, IReadOnlyList<TownRuntimeState> towns, string child)
    {
        var townId = towns.SingleOrDefault(item => item.ResidentIds.Contains(child, StringComparer.Ordinal))?.Id;
        if (townId is null)
            return [];
        return towns.Single(town => town.Id == townId).ResidentIds
            .Where(id => id != child && checkpoint.Inhabitants.Any(person => person.Id == id &&
                person.Status == SocietyInhabitantStatus.Active &&
                person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder))
            .Order(StringComparer.Ordinal).ToArray();
    }

    private string[] LivingAdultRelatives(string child) => LivingAdultRelatives(society.Checkpoint, child);

    private static string[] LivingAdultRelatives(SocietyCheckpoint checkpoint, string child)
    {
        var parentage = checkpoint.Relationships.Where(edge =>
                edge.Type == SocietyRelationshipType.BiologicalParentage &&
                edge.State is not (SocietyRelationshipState.Proposed or SocietyRelationshipState.Rejected))
            .ToArray();
        var parents = parentage.Where(edge => edge.TargetId == child)
            .Select(edge => edge.ProposerId).ToHashSet(StringComparer.Ordinal);
        var grandparents = parentage.Where(edge => parents.Contains(edge.TargetId))
            .Select(edge => edge.ProposerId).ToHashSet(StringComparer.Ordinal);
        var adultSiblings = parentage.Where(edge => parents.Contains(edge.ProposerId))
            .Select(edge => edge.TargetId);
        var auntsAndUncles = parentage.Where(edge => grandparents.Contains(edge.ProposerId))
            .Select(edge => edge.TargetId).Where(id => !parents.Contains(id));
        return parents.Concat(grandparents).Concat(adultSiblings).Concat(auntsAndUncles)
            .Where(id => id != child && checkpoint.Inhabitants.Any(person => person.Id == id &&
                person.Status == SocietyInhabitantStatus.Active &&
                person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private void SetGuardianSearch(string child, SettlementGuardianSearch? search)
    {
        inhabitants[child] = inhabitants[child] with { GuardianSearch = search };
        checkpointSchemaVersion = StateSchemaVersion;
    }

    private void AddDependentCareCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!ReadyForBriefInteraction(actor)) return;
        foreach (var edge in society.Checkpoint.Relationships.Where(edge => edge.Type == SocietyRelationshipType.Caregiver &&
                     (edge.ProposerId == actor || edge.TargetId == actor) &&
                     (edge.State == SocietyRelationshipState.Accepted || edge.State == SocietyRelationshipState.Proposed && edge.ProposerId == actor)))
        {
            candidates.Add(new("guardian_end:" + edge.Id, "Withdraw from this caregiving relationship or proposal.", 110));
        }
        foreach (var dependent in IllDependentsNeedingCare(actor).Where(dependent =>
                     !NeedsUrgentFood(inhabitants[actor]) || IsWithinInteractionRange(inhabitants[actor].Position, inhabitants[dependent].Position, ResourceInteractionRange)))
        {
            candidates.Add(new("guardian_tend:" + dependent,
                "Offer warmth and practical care to your ill dependent.", 75));
        }
        foreach (var child in inhabitants.Keys.Order(StringComparer.Ordinal).Where(child => CanAcceptGuardian(actor, child)))
        {
            if (candidates.Any(candidate => candidate.Id == "guardian_accept:" + child))
                continue;
            candidates.Add(new("guardian_accept:" + child,
                $"Accept primary care of {society.Checkpoint.GetInhabitant(child).Name}. They can move into your household only if your House has room and you share their Town.", 3));
        }
        foreach (var edge in CareProposals().Where(edge => edge.TargetId == actor && EligibleCaregiver(edge.ProposerId, actor)))
        {
            candidates.Add(new("guardian_accept:" + edge.Id, $"Accept care from {society.Checkpoint.GetInhabitant(edge.ProposerId).Name}.", 3));
            candidates.Add(new("guardian_refuse:" + edge.Id, "Refuse this caregiver proposal.", 70));
        }
        foreach (var child in inhabitants.Keys.Order(StringComparer.Ordinal).Where(child => CanOfferCare(actor, child)))
        {
            candidates.Add(new("guardian_offer:" + child,
                $"Offer to care for {society.Checkpoint.GetInhabitant(child).Name}, who has no surviving active caregiver. Older dependents may refuse.", 4));
        }
        foreach (var child in inhabitants.Keys.Order(StringComparer.Ordinal).Where(child => CanAssumePrimaryCare(actor, child)))
        {
            candidates.Add(new("guardian_primary:" + child,
                $"Become {society.Checkpoint.GetInhabitant(child).Name}'s primary caregiver. The former primary caregiver is unavailable, and this child already recognizes you as a caregiver.", 4));
        }
    }

    private void ApplyDependentCareCandidate(string actor, string candidate)
    {
        var target = candidate[(candidate.IndexOf(':', StringComparison.Ordinal) + 1)..];
        if (candidate.StartsWith("guardian_tend:", StringComparison.Ordinal))
        {
            TendToIllDependent(actor, target);
            return;
        }
        if (candidate.StartsWith("guardian_accept:", StringComparison.Ordinal))
        {
            if (inhabitants.ContainsKey(target) && NeedsCaregiver(target))
            {
                var instruction = PendingInstructionFor(actor);
                var ordered = instruction is { Kind: OwnerInstructionKind.MustDo or OwnerInstructionKind.Suggestive } &&
                    InstructionCandidate(instruction.Text) == "guardian_accept" && GuardianTargetForInstruction(instruction.Text) == target;
                AcceptGuardian(actor, target, CanAcceptGuardian(actor, target, ordered));
                return;
            }
            var edge = CareProposals().FirstOrDefault(edge => edge.Id == target && edge.TargetId == actor);
            if (edge is not null && EligibleCaregiver(edge.ProposerId, actor))
            {
                society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint, edge.Id, edge.Revision, actor));
                AppendEvent("caregiver_accepted", actor);
            }
            return;
        }
        if (candidate.StartsWith("guardian_end:", StringComparison.Ordinal))
        {
            var existing = society.Checkpoint.Relationships.FirstOrDefault(edge => edge.Id == target &&
                edge.Type == SocietyRelationshipType.Caregiver && (edge.ProposerId == actor || edge.TargetId == actor) &&
                (edge.State == SocietyRelationshipState.Accepted || edge.State == SocietyRelationshipState.Proposed && edge.ProposerId == actor));
            if (existing is null) return;
            society.Apply(checkpoint => SocietyFixture.RevokeRelationship(checkpoint, target, actor));
            AppendEvent("caregiver_ended", actor);
            return;
        }
        if (candidate.StartsWith("guardian_primary:", StringComparison.Ordinal))
        {
            if (!CanAssumePrimaryCare(actor, target)) return;
            var result = society.Apply(checkpoint => SocietyFixture.AssumePrimaryCare(checkpoint, actor, target));
            if (result.NewEvents?.Any(item => item.Kind == "primary_caregiver_assumed") == true)
                AppendEvent("primary_caregiver_assigned", target);
            return;
        }
        if (candidate.StartsWith("guardian_offer:", StringComparison.Ordinal))
        {
            if (!CanOfferCare(actor, target)) return;
            if (society.Checkpoint.GetInhabitant(target).AgeBand == SocietyAgeBand.Infant)
            {
                var result = society.Apply(checkpoint => SocietyFixture.AssumeInfantCare(checkpoint, actor, target));
                if (result.CreatedId is not null) AppendEvent("caregiver_assigned", target);
            }
            else
            {
                society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
                    new($"settlement-care:{actor}:{target}:{WorldTick}", 1, SocietyRelationshipType.Caregiver,
                        actor, target, WorldTick, PrivacyClass: "public", HouseholdId: HouseholdFor(actor))));
                AppendEvent("caregiver_proposed", target);
            }
            return;
        }
        var proposal = CareProposals().FirstOrDefault(edge => edge.Id == target && edge.TargetId == actor);
        if (proposal is null || !EligibleCaregiver(proposal.ProposerId, actor)) return;
        if (candidate.StartsWith("guardian_refuse:", StringComparison.Ordinal))
        {
            society.Apply(checkpoint => SocietyFixture.RefuseRelationship(checkpoint, proposal.Id, proposal.Revision, actor));
            AppendEvent("caregiver_refused", actor);
        }
    }

    private void AcceptGuardian(string adult, string child, bool authorized)
    {
        if (!authorized || !AdultResident(adult) || !NeedsCaregiver(child)) return;
        var adultHousehold = society.Checkpoint.GetInhabitant(adult).HouseholdId;
        var adultTown = TownForResident(adult);
        var childTown = TownForResident(child);
        var destination = adultHousehold is not null && adultHousehold != society.Checkpoint.GetInhabitant(child).HouseholdId &&
            adultTown is not null && adultTown == childTown &&
            CanFitGuardianHousehold(adultHousehold, adult, child)
            ? adultHousehold : null;
        var result = society.Apply(checkpoint => SocietyFixture.AcceptDependentGuardianship(
            checkpoint, adult, child, destination));
        if (result.NewEvents?.Any(item => item.Kind == "dependent_guardian_accepted") != true ||
            !SocietyFixture.HasActivePrimaryCaregiver(society.Checkpoint, child))
            return;
        SetGuardianSearch(child, null);
        SetHousing(child, (inhabitants[child].Housing ?? new()) with { Blocker = HousingBlocker(child) });
        AppendEvent("guardian_assigned", child);
    }

    private static void ValidateDependentCare(
        IEnumerable<PlaytestInhabitantState> physical,
        SocietyCheckpoint checkpoint,
        IReadOnlyList<TownRuntimeState> towns,
        int schemaVersion)
    {
        var people = checkpoint.Inhabitants.ToDictionary(person => person.Id, StringComparer.Ordinal);
        foreach (var state in physical)
        {
            if (state.GuardianSearch is not { } search) continue;
            if (schemaVersion < DependentGuardianSearchSchemaVersion || !people.TryGetValue(state.InhabitantId, out var child) ||
                child.Status != SocietyInhabitantStatus.Active ||
                child.AgeBand is not (SocietyAgeBand.Infant or SocietyAgeBand.Child or SocietyAgeBand.Adolescent) ||
                SocietyFixture.HasActivePrimaryCaregiver(checkpoint, child.Id) ||
                search.Stage is not ("relatives" or "household" or "town") ||
                search.StartedTick < 0 || search.StartedTick > checkpoint.WorldTick ||
                search.StageStartedTick < search.StartedTick || search.StageStartedTick > checkpoint.WorldTick ||
                search.OfferedAdultIds is null ||
                search.OfferedAdultIds.Any(id => string.IsNullOrWhiteSpace(id) || !people.TryGetValue(id, out var adult) ||
                    adult.Status != SocietyInhabitantStatus.Active || adult.AgeBand is not (SocietyAgeBand.Adult or SocietyAgeBand.Elder)) ||
                search.OfferedAdultIds.Distinct(StringComparer.Ordinal).Count() != search.OfferedAdultIds.Count ||
                !search.OfferedAdultIds.SequenceEqual(search.OfferedAdultIds.Order(StringComparer.Ordinal)) ||
                !search.OfferedAdultIds.SequenceEqual(GuardianAdultsForStage(checkpoint, towns, child.Id, search.Stage), StringComparer.Ordinal) ||
                search.Stage != "town" && search.OfferedAdultIds.Count == 0)
                throw new InvalidDataException("The saved dependent guardian search is invalid.");
        }
    }

    private bool CanFitGuardianHousehold(string householdId, string adult, string child) =>
        HouseResidentCapacity(householdId, society.Checkpoint.GetInhabitant(child) with
        {
            DomesticFamilyUnitId = society.Checkpoint.GetInhabitant(adult).DomesticFamilyUnitId,
        }) is { IsOvercrowded: false };

    private bool CanTendToIllDependent(string adult, string dependent) => AdultResident(adult) &&
        IllDependentsNeedingCare(adult).Contains(dependent, StringComparer.Ordinal);

    private void TendToIllDependent(string adult, string dependent)
    {
        if (!ReadyForBriefInteraction(adult) || !CanTendToIllDependent(adult, dependent)) return;
        var caregiver = inhabitants[adult];
        var recipient = inhabitants[dependent];
        if (NeedsUrgentFood(caregiver) && !IsWithinInteractionRange(caregiver.Position, recipient.Position, ResourceInteractionRange)) return;
        if (!IsWithinInteractionRange(caregiver.Position, recipient.Position, ResourceInteractionRange))
        {
            MoveToward(adult, caregiver, recipient.Position, "care", ResourceInteractionRange);
            return;
        }
        recipient = recipient with
        {
            Survival = recipient.Survival! with
            {
                WarmthBasisPoints = Math.Min(10_000, recipient.Survival.WarmthBasisPoints + 1_000),
                IllnessBasisPoints = Math.Max(0, recipient.Survival.IllnessBasisPoints - IllnessCareReliefBasisPoints),
            },
        };
        inhabitants[dependent] = recipient;
        AppendEvent("dependent_cared_for", dependent);
    }
}
