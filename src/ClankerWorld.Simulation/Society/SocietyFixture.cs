using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Society;

/// <summary>
/// Authoritative Phase 4 society transitions. Every public operation validates
/// against a checkpoint and returns a replacement checkpoint with durable
/// events; callers never receive a partially-mutated world.
/// </summary>
public static partial class SocietyFixture
{
    public static SocietyInhabitant CreateFounder(
        string id,
        string name,
        string? providerBindingId = null,
        int healthBasisPoints = 10_000,
        SocietyConfig? config = null)
    {
        var effectiveConfig = config ?? new SocietyConfig();
        effectiveConfig.Validate();
        return new(
            NormalizeRequiredText(id, nameof(id)),
            NormalizeRequiredText(name, nameof(name)),
            checked(-effectiveConfig.FounderStartingAge * effectiveConfig.TicksPerLifecycleAge),
            SocietyInhabitantStatus.Active,
            effectiveConfig.AgeBandAt(effectiveConfig.FounderStartingAge),
            ValidateBasisPoints(healthBasisPoints, nameof(healthBasisPoints)),
            null,
            NormalizeOptionalText(providerBindingId),
            SocietyWorkRole.Unassigned,
            effectiveConfig.FounderStartingAge);
    }

    public static SocietyCheckpoint CreateGenesis(
        string worldId,
        IEnumerable<SocietyInhabitant>? founders = null,
        IEnumerable<InventoryLot>? lots = null,
        SocietyConfig? config = null,
        string? worldDefaultProviderBindingId = null)
    {
        var effectiveConfig = config ?? new SocietyConfig();
        effectiveConfig.Validate();
        var inhabitants = (founders ??
                [CreateFounder("founder-scout", "Scout", config: effectiveConfig)])
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        ValidateInhabitants(inhabitants, effectiveConfig, 0);
        var inventory = InventoryFixture.CreateGenesis(lots ?? []);
        return new SocietyCheckpoint(
            NormalizeRequiredText(worldId, nameof(worldId)),
            0,
            0,
            false,
            effectiveConfig,
            NormalizeOptionalText(worldDefaultProviderBindingId),
            inhabitants,
            [],
            [],
            [],
            [],
            [],
            [],
            inventory,
            []);
    }

    public static SocietyOperationResult CreateHousehold(
        SocietyCheckpoint checkpoint,
        string householdId,
        string name,
        IEnumerable<string> memberIds)
    {
        Validate(checkpoint);
        var id = NormalizeRequiredText(householdId, nameof(householdId));
        var householdName = NormalizeRequiredText(name, nameof(name));
        var requestedMembers = memberIds?.ToArray() ?? throw new ArgumentNullException(nameof(memberIds));
        var members = requestedMembers.Length == 0 && checkpoint.WorldTick == 0 &&
            checkpoint.Inhabitants.Count == 0
                ? []
                : CanonicalIds(requestedMembers, nameof(memberIds));
        if (checkpoint.Households.Any(item => item.Id == id))
        {
            throw new InvalidOperationException("A household ID may be used only once.");
        }

        foreach (var memberId in members)
        {
            EnsureActive(checkpoint, memberId);
            if (checkpoint.GetInhabitant(memberId).HouseholdId is not null)
            {
                throw new InvalidOperationException("An inhabitant cannot join a second primary household.");
            }
        }

        var household = new SocietyHousehold(id, householdName, members, []);
        var inhabitants = checkpoint.Inhabitants.Select(item => members.Contains(item.Id, StringComparer.Ordinal)
                ? item with { HouseholdId = id }
                : item)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var relationships = checkpoint.Relationships.ToList();
        foreach (var memberId in members)
        {
            relationships.Add(new SocietyRelationship(
                $"{id}:membership:{memberId}",
                1,
                SocietyRelationshipType.HouseholdMembership,
                id,
                memberId,
                SocietyRelationshipState.Accepted,
                SocietyConsentState.ProtectedLifecycle,
                checkpoint.WorldTick,
                checkpoint.WorldTick,
                "household",
                id,
                new[] { id, memberId }.OrderBy(value => value, StringComparer.Ordinal).ToArray()));
        }

        var next = checkpoint with
        {
            Inhabitants = inhabitants,
            Households = checkpoint.Households.Append(household)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Relationships = relationships.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        return Commit(next, "household_created", $"{id}:{string.Join(',', members)}", id);
    }

    public static SocietyOperationResult PlaceFounder(
        SocietyCheckpoint checkpoint,
        SocietyInhabitant founder,
        string householdId)
    {
        Validate(checkpoint);
        ArgumentNullException.ThrowIfNull(founder);
        if (checkpoint.WorldTick != 0 || !checkpoint.IsPaused ||
            founder.Status != SocietyInhabitantStatus.Active || founder.HouseholdId is not null ||
            checkpoint.Inhabitants.Any(person => person.Id == founder.Id))
            throw new InvalidOperationException("A founder can only be placed once during paused world setup.");
        var household = checkpoint.GetHousehold(householdId);
        var memberIds = household.MemberIds.Append(founder.Id).Order(StringComparer.Ordinal).ToArray();
        var membership = new SocietyRelationship(
            $"{householdId}:membership:{founder.Id}", 1,
            SocietyRelationshipType.HouseholdMembership,
            householdId, founder.Id, SocietyRelationshipState.Accepted,
            SocietyConsentState.ProtectedLifecycle, 0, 0,
            "household", householdId,
            new[] { householdId, founder.Id }.Order(StringComparer.Ordinal).ToArray());
        var next = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Append(founder with { HouseholdId = householdId, NeedsName = true })
                .OrderBy(person => person.Id, StringComparer.Ordinal).ToArray(),
            Households = checkpoint.Households.Select(item => item.Id == householdId
                ? item with { MemberIds = memberIds } : item).ToArray(),
            Relationships = checkpoint.Relationships.Append(membership)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        return Commit(next, "founder_placed", $"{founder.Id}:{householdId}", founder.Id);
    }

    public static SocietyOperationResult UndoFounderPlacement(SocietyCheckpoint checkpoint, string founderId)
    {
        Validate(checkpoint);
        if (!checkpoint.IsPaused || checkpoint.WorldTick != 0)
            throw new InvalidOperationException("Founder placement can only be undone during paused setup.");
        var founder = checkpoint.Inhabitants.SingleOrDefault(item => item.Id == founderId);
        if (founder is null || founder.Status != SocietyInhabitantStatus.Active ||
            founder.HouseholdId is not { } householdId ||
            checkpoint.Relationships.Any(item =>
                (item.ProposerId == founderId || item.TargetId == founderId) &&
                item.Type != SocietyRelationshipType.HouseholdMembership) ||
            checkpoint.Inventory.Lots.Any(item => item.OwnerId == founderId))
            throw new InvalidOperationException("This founder cannot be removed from setup.");
        var next = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Where(item => item.Id != founderId).ToArray(),
            Households = checkpoint.Households.Select(item => item.Id == householdId
                ? item with { MemberIds = item.MemberIds.Where(id => id != founderId).ToArray() }
                : item).ToArray(),
            Relationships = checkpoint.Relationships.Where(item =>
                item.ProposerId != founderId && item.TargetId != founderId).ToArray(),
        };
        return Commit(next, "founder_placement_undone", $"{founderId}:{householdId}");
    }

    public static SocietyOperationResult AddAdult(
        SocietyCheckpoint checkpoint, string inhabitantId, string? householdId)
    {
        Validate(checkpoint);
        var id = NormalizeRequiredText(inhabitantId, nameof(inhabitantId));
        var home = householdId is null ? null : NormalizeRequiredText(householdId, nameof(householdId));
        if (checkpoint.Inhabitants.Any(person => person.Id == id))
            throw new InvalidOperationException("The new agent ID already exists.");
        var existingHousehold = home is null ? null :
            checkpoint.Households.FirstOrDefault(household => household.Id == home);

        var age = checkpoint.Config.FounderStartingAge;
        var lifeBirth = checked(checkpoint.LifeTickAt(checkpoint.WorldTick) -
            age * checkpoint.Config.TicksPerLifecycleAge);
        var person = CreateFounder(id, "New agent", config: checkpoint.Config) with
        {
            BirthTick = checkpoint.LifeClock is null ? lifeBirth : checkpoint.WorldTick,
            BirthLifeTick = checkpoint.LifeClock is null ? null : lifeBirth,
            HouseholdId = home,
            NeedsName = true,
        };
        var households = checkpoint.Households;
        var relationships = checkpoint.Relationships;
        if (home is not null)
        {
            var household = existingHousehold is null
                ? new SocietyHousehold(home, "New household", [id], [])
                : existingHousehold with
                {
                    MemberIds = existingHousehold.MemberIds.Append(id)
                        .Order(StringComparer.Ordinal).ToArray(),
                };
            households = existingHousehold is null
                ? checkpoint.Households.Append(household).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray()
                : checkpoint.Households.Select(item => item.Id == home ? household : item).ToArray();
            var membership = new SocietyRelationship(
                $"{home}:membership:{id}", 1, SocietyRelationshipType.HouseholdMembership,
                home, id, SocietyRelationshipState.Accepted,
                SocietyConsentState.ProtectedLifecycle, checkpoint.WorldTick, checkpoint.WorldTick,
                "household", home, new[] { home, id }.Order(StringComparer.Ordinal).ToArray());
            relationships = checkpoint.Relationships.Append(membership)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        }
        var next = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Append(person).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Households = households,
            Relationships = relationships,
        };
        return Commit(next, "agent_added", $"{id}:{home ?? "no_household"}", id);
    }

    public static SocietyOperationResult RenameInhabitant(
        SocietyCheckpoint checkpoint, string inhabitantId, string name)
    {
        Validate(checkpoint);
        var id = NormalizeRequiredText(inhabitantId, nameof(inhabitantId));
        var chosen = NormalizeRequiredText(name, nameof(name));
        if (chosen.Length > 48 || chosen.Any(char.IsControl))
            throw new ArgumentException("Choose a name of at most 48 characters without control characters.", nameof(name));
        var existing = checkpoint.GetInhabitant(id);
        if (existing.Name == chosen && !existing.NeedsName) return new SocietyOperationResult(checkpoint);
        var next = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person =>
                person.Id == id ? person with { Name = chosen, NeedsName = false } : person).ToArray(),
        };
        return Commit(next, "inhabitant_renamed", id, id);
    }

    public static SocietyOperationResult ProposeRelationship(
        SocietyCheckpoint checkpoint,
        SocietyRelationshipProposal proposal)
    {
        Validate(checkpoint);
        ArgumentNullException.ThrowIfNull(proposal);
        ValidateProposal(proposal, checkpoint.WorldTick);
        EnsureActive(checkpoint, proposal.ProposerId);
        EnsureActive(checkpoint, proposal.TargetId);
        if (checkpoint.Relationships.Any(item => item.Id == proposal.Id))
        {
            throw new InvalidOperationException("A relationship proposal ID may be used only once.");
        }

        var relationship = new SocietyRelationship(
            proposal.Id,
            proposal.Revision,
            proposal.Type,
            proposal.ProposerId,
            proposal.TargetId,
            SocietyRelationshipState.Proposed,
            SocietyConsentState.Pending,
            proposal.RequestedTick,
            0,
            proposal.PrivacyClass,
            proposal.HouseholdId,
            []);
        var next = checkpoint with
        {
            Relationships = checkpoint.Relationships.Append(relationship)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        return Commit(next, "relationship_proposed", $"{proposal.Id}:r{proposal.Revision}", proposal.Id);
    }

    public static SocietyOperationResult AcceptRelationship(
        SocietyCheckpoint checkpoint,
        string relationshipId,
        int revision,
        string acceptorId)
    {
        Validate(checkpoint);
        var relationship = checkpoint.GetRelationship(relationshipId);
        var acceptor = NormalizeRequiredText(acceptorId, nameof(acceptorId));
        if (relationship.Type == SocietyRelationshipType.BiologicalParentage)
            return Reject(checkpoint, "relationship_rejected", "parentage_requires_birth_transaction");
        if (relationship.State != SocietyRelationshipState.Proposed || relationship.Revision != revision ||
            relationship.TargetId != acceptor)
        {
            return Reject(checkpoint, "relationship_rejected", $"{relationshipId}:stale_or_wrong_acceptor");
        }

        EnsureActive(checkpoint, relationship.ProposerId);
        EnsureActive(checkpoint, relationship.TargetId);
        if (relationship.Type == SocietyRelationshipType.Partnership &&
            checkpoint.Relationships.Any(item => item.State == SocietyRelationshipState.Accepted &&
                item.Type == SocietyRelationshipType.Partnership &&
                (item.ProposerId == relationship.ProposerId || item.TargetId == relationship.ProposerId ||
                 item.ProposerId == relationship.TargetId || item.TargetId == relationship.TargetId)))
        {
            return Reject(checkpoint, "relationship_rejected", $"{relationshipId}:cardinality_conflict");
        }

        if (relationship.Type == SocietyRelationshipType.HouseholdMembership)
        {
            if (relationship.HouseholdId is null || !checkpoint.Households.Any(item => item.Id == relationship.HouseholdId))
            {
                return Reject(checkpoint, "relationship_rejected", $"{relationshipId}:missing_household");
            }

            if (checkpoint.GetInhabitant(relationship.TargetId).HouseholdId is not null)
            {
                return Reject(checkpoint, "relationship_rejected", $"{relationshipId}:existing_household");
            }
        }

        var accepted = relationship with
        {
            State = SocietyRelationshipState.Accepted,
            Consent = SocietyConsentState.Accepted,
            EffectiveTick = checked(checkpoint.WorldTick + 1),
            AcceptedBy = new[] { relationship.ProposerId, relationship.TargetId }
                .OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        };
        var next = checkpoint with
        {
            Relationships = checkpoint.Relationships.Select(item => item.Id == relationship.Id ? accepted : item)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        next = ApplyRelationshipProjection(next, accepted);
        return Commit(next, "relationship_accepted", $"{relationshipId}:r{revision}", relationshipId);
    }

    public static SocietyOperationResult RefuseRelationship(
        SocietyCheckpoint checkpoint,
        string relationshipId,
        int revision,
        string targetId)
    {
        Validate(checkpoint);
        var relationship = checkpoint.GetRelationship(relationshipId);
        if (relationship.State != SocietyRelationshipState.Proposed || relationship.Revision != revision ||
            relationship.TargetId != targetId)
        {
            return Reject(checkpoint, "relationship_rejected", $"{relationshipId}:stale_or_wrong_target");
        }

        var rejected = relationship with
        {
            State = SocietyRelationshipState.Rejected,
            Consent = SocietyConsentState.Refused,
            EffectiveTick = checkpoint.WorldTick,
        };
        return Commit(
            checkpoint with
            {
                Relationships = checkpoint.Relationships.Select(item => item.Id == relationship.Id ? rejected : item)
                    .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            },
            "relationship_rejected",
            $"{relationshipId}:consent_refused",
            relationshipId);
    }

    public static SocietyOperationResult RevokeRelationship(
        SocietyCheckpoint checkpoint,
        string relationshipId,
        string actorId)
    {
        Validate(checkpoint);
        var relationship = checkpoint.GetRelationship(relationshipId);
        var actor = NormalizeRequiredText(actorId, nameof(actorId));
        if (relationship.Type == SocietyRelationshipType.BiologicalParentage)
            return Reject(checkpoint, "relationship_rejected", "parentage_is_historical");
        if (relationship.State is not (SocietyRelationshipState.Accepted or SocietyRelationshipState.Proposed) ||
            (relationship.ProposerId != actor && relationship.TargetId != actor))
        {
            return Reject(checkpoint, "relationship_rejected", $"{relationshipId}:unauthorized_revoke");
        }

        var revoked = relationship with
        {
            State = SocietyRelationshipState.Revoked,
            Consent = SocietyConsentState.Revoked,
            EffectiveTick = checkpoint.WorldTick,
        };
        var next = checkpoint with
        {
            Relationships = checkpoint.Relationships.Select(item => item.Id == relationship.Id ? revoked : item)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        next = RemoveRelationshipProjection(next, revoked);
        return Commit(next, "relationship_revoked", $"{relationshipId}:{actor}", relationshipId);
    }

    public static SocietyOperationResult AssignRole(
        SocietyCheckpoint checkpoint,
        string inhabitantId,
        SocietyWorkRole role)
    {
        Validate(checkpoint);
        EnsureActive(checkpoint, inhabitantId);
        var next = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(item => item.Id == inhabitantId
                    ? item with { CurrentRole = role }
                    : item)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        return Commit(next, "work_role_assigned", $"{inhabitantId}:{role}", inhabitantId);
    }

    public static SocietyOperationResult RecordSocialMemory(
        SocietyCheckpoint checkpoint,
        SocietySocialMemory memory)
    {
        Validate(checkpoint);
        ArgumentNullException.ThrowIfNull(memory);
        EnsureActive(checkpoint, memory.OwnerId);
        if (!checkpoint.Inhabitants.Any(item => item.Id == memory.SubjectId) ||
            checkpoint.Memories.Any(item => item.Id == memory.Id))
        {
            throw new InvalidOperationException("A social memory requires a known subject and unique ID.");
        }

        var next = checkpoint with
        {
            Memories = checkpoint.Memories.Append(memory with
            { Summary = NormalizeRequiredText(memory.Summary, nameof(memory.Summary)) })
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        return Commit(next, "social_memory_recorded", memory.Id, memory.Id);
    }

    /// <summary>
    /// Adds an agent-owned belief without appending to the authoritative event
    /// history. The public event stream remains a record of world outcomes.
    /// </summary>
    public static SocietyCheckpoint RecordAgentBelief(
        SocietyCheckpoint checkpoint,
        SocietyAgentBelief belief)
    {
        Validate(checkpoint);
        ArgumentNullException.ThrowIfNull(belief);
        var normalized = NormalizeBelief(belief);
        EnsureActive(checkpoint, normalized.OwnerId);
        ValidateBeliefInput(checkpoint, normalized, allowSupersedes: false);
        if ((checkpoint.Beliefs ?? []).Any(item => item.Id == normalized.Id))
            throw new InvalidOperationException("The agent belief ID is already used.");

        return checkpoint with
        {
            Beliefs = (checkpoint.Beliefs ?? []).Append(normalized)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
    }

    /// <summary>
    /// Keeps an agent's earlier belief as corrected history and records the
    /// replacement. This changes neither simulation facts nor public events.
    /// </summary>
    public static SocietyCheckpoint CorrectAgentBelief(
        SocietyCheckpoint checkpoint,
        string ownerId,
        string beliefId,
        SocietyAgentBelief correction)
    {
        Validate(checkpoint);
        var owner = NormalizeRequiredText(ownerId, nameof(ownerId));
        var targetId = NormalizeRequiredText(beliefId, nameof(beliefId));
        EnsureActive(checkpoint, owner);
        ArgumentNullException.ThrowIfNull(correction);
        var beliefs = checkpoint.Beliefs ?? [];
        var previous = beliefs.SingleOrDefault(item => item.Id == targetId)
            ?? throw new InvalidOperationException("The belief to correct does not exist.");
        if (previous.OwnerId != owner || correction.OwnerId != owner)
            throw new InvalidOperationException("An agent can correct only their own belief.");
        if (previous.SupersededByBeliefId is not null)
            throw new InvalidOperationException("The belief has already been corrected.");
        if (correction.Id == previous.Id || beliefs.Any(item => item.Id == correction.Id))
            throw new InvalidOperationException("The correction must use a new belief ID.");

        var replacement = NormalizeBelief(correction) with
        {
            FormedTick = checkpoint.WorldTick,
            SupersedesBeliefId = previous.Id,
            SupersededByBeliefId = null,
            SupersededTick = null,
        };
        ValidateBeliefInput(checkpoint, replacement, allowSupersedes: true);
        var revised = beliefs.Select(item => item.Id == previous.Id
                ? item with { SupersededByBeliefId = replacement.Id, SupersededTick = checkpoint.WorldTick }
                : item)
            .Append(replacement)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        return checkpoint with { Beliefs = revised };
    }

    /// <summary>
    /// Merges Jev's bounded salience estimates into an owner's private index.
    /// Existing experience and belief text is left untouched and no event is
    /// appended to the authoritative world history.
    /// </summary>
    public static SocietyCheckpoint RecordAgentMemoryCompaction(
        SocietyCheckpoint checkpoint,
        string ownerId,
        IReadOnlyList<SocietyAgentMemoryImportance> scores)
    {
        Validate(checkpoint);
        var owner = NormalizeRequiredText(ownerId, nameof(ownerId));
        ArgumentNullException.ThrowIfNull(scores);
        EnsureActive(checkpoint, owner);
        if (scores.Count is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(scores));

        var existingSources = (checkpoint.MemoryCompactions ?? [])
            .SingleOrDefault(item => item.OwnerId == owner)?.Sources ?? [];
        var combined = existingSources.ToDictionary(
            item => $"{item.Kind}:{item.SourceId}", StringComparer.Ordinal);
        foreach (var score in scores)
        {
            ArgumentNullException.ThrowIfNull(score);
            if (!IsCanonicalBoundedText(score.SourceId, 128) || !Enum.IsDefined(score.Kind) ||
                score.SourceTick < 0 || score.SourceTick > checkpoint.WorldTick ||
                score.ImportanceBasisPoints is < 0 or > 10_000 ||
                score.ImportanceConfidenceBasisPoints is < 0 or > 10_000)
                throw new InvalidDataException("An agent memory importance assessment is invalid.");

            var sourceTick = score.Kind switch
            {
                SocietyMemorySourceKind.Experience => checkpoint.Memories
                    .SingleOrDefault(item => item.Id == score.SourceId && item.OwnerId == owner)?.SourceTick,
                SocietyMemorySourceKind.Belief => (checkpoint.Beliefs ?? [])
                    .SingleOrDefault(item => item.Id == score.SourceId && item.OwnerId == owner)?.FormedTick,
                _ => null,
            };
            if (sourceTick is null || sourceTick.Value != score.SourceTick)
                throw new InvalidOperationException("An agent may compact only their own existing memory sources.");

            combined[$"{score.Kind}:{score.SourceId}"] = score with { AssessedTick = checkpoint.WorldTick };
        }

        var boundedSources = combined.Values
            .OrderByDescending(item => item.SourceTick)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal)
            .Take(256)
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal).ToArray();
        var compactions = (checkpoint.MemoryCompactions ?? [])
            .Where(item => item.OwnerId != owner)
            .Append(new SocietyAgentMemoryCompaction(owner, boundedSources))
            .OrderBy(item => item.OwnerId, StringComparer.Ordinal).ToArray();
        return checkpoint with { MemoryCompactions = compactions };
    }

    public static SocietyOperationResult CreateOrganization(
        SocietyCheckpoint checkpoint,
        string organizationId,
        SocietyOrganizationKind kind,
        string ownerId,
        IEnumerable<string> memberIds)
    {
        Validate(checkpoint);
        var id = NormalizeRequiredText(organizationId, nameof(organizationId));
        EnsureActive(checkpoint, ownerId);
        var members = CanonicalIds(memberIds, nameof(memberIds));
        foreach (var member in members)
        {
            EnsureActive(checkpoint, member);
        }

        if (checkpoint.Organizations.Any(item => item.Id == id))
        {
            throw new InvalidOperationException("An organization ID may be used only once.");
        }

        var organization = new SocietyOrganization(id, kind, ownerId, members, $"organization:{id}");
        var next = checkpoint with
        {
            Organizations = checkpoint.Organizations.Append(organization)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        return Commit(next, "organization_created", id, id);
    }

    public static SocietyOperationResult AddOrganizationMember(
        SocietyCheckpoint checkpoint,
        string organizationId,
        string ownerId,
        string memberId)
    {
        Validate(checkpoint);
        var organization = checkpoint.Organizations.Single(item => item.Id == organizationId);
        if (organization.OwnerId != ownerId)
        {
            return Reject(checkpoint, "organization_member_rejected", $"{organizationId}:unauthorized");
        }

        EnsureActive(checkpoint, memberId);
        var members = organization.MemberIds.Append(memberId).Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var next = checkpoint with
        {
            Organizations = checkpoint.Organizations.Select(item => item.Id == organizationId
                    ? item with { MemberIds = members }
                    : item)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
        return Commit(next, "organization_member_added", $"{organizationId}:{memberId}", memberId);
    }

    public static SocietyOperationResult TransferInventory(
        SocietyCheckpoint checkpoint,
        string transferId,
        string senderId,
        string recipientId,
        string lotId,
        int quantity,
        string purpose = "direct_transfer")
    {
        Validate(checkpoint);
        EnsureLivingParty(checkpoint, senderId);
        EnsureLivingParty(checkpoint, recipientId);
        var inventory = InventoryFixture.Transfer(
            checkpoint.Inventory,
            transferId,
            senderId,
            recipientId,
            lotId,
            quantity,
            purpose);
        return Commit(checkpoint with { Inventory = inventory }, "inventory_transfer_committed", transferId, transferId);
    }

    public static SocietyOperationResult ConsumeInventory(
        SocietyCheckpoint checkpoint,
        string ownerId,
        string lotId,
        int quantity,
        string purpose = "direct_consumption")
    {
        Validate(checkpoint);
        var owner = NormalizeRequiredText(ownerId, nameof(ownerId));
        EnsureLivingParty(checkpoint, owner);
        var lot = checkpoint.Inventory.GetLot(lotId);
        if (quantity <= 0 || lot.OwnerId != owner || lot.Quantity < quantity)
        {
            throw new InvalidOperationException("Consumption requires an owned lot with sufficient quantity.");
        }

        var reservationId = $"consume:{owner}:{lotId}:{checkpoint.Inventory.EventHistoryFloor + checkpoint.Inventory.Events.Count + 1}";
        var reserved = InventoryFixture.Reserve(
            checkpoint.Inventory,
            reservationId,
            owner,
            lotId,
            quantity,
            purpose,
            checkpoint.WorldTick);
        var consumed = InventoryFixture.ConsumeReservation(reserved, reservationId);
        return Commit(
            checkpoint with { Inventory = consumed },
            "inventory_consumed",
            $"{owner}:{lotId}:{quantity}:{purpose}",
            owner);
    }

    public static SocietyOperationResult CreateBarterOffer(
        SocietyCheckpoint checkpoint,
        DirectBarterProposal proposal)
    {
        Validate(checkpoint);
        EnsureLivingParty(checkpoint, proposal.FirstPartyId);
        EnsureLivingParty(checkpoint, proposal.SecondPartyId);
        var inventory = InventoryFixture.CreateDirectBarterOffer(checkpoint.Inventory, proposal);
        return Commit(checkpoint with { Inventory = inventory }, "barter_offer_created", proposal.Id, proposal.Id);
    }

    public static SocietyOperationResult AcceptBarterOffer(
        SocietyCheckpoint checkpoint,
        string offerId,
        int revision,
        string partyId)
    {
        Validate(checkpoint);
        EnsureLivingParty(checkpoint, partyId);
        var inventory = InventoryFixture.AcceptDirectBarterOffer(checkpoint.Inventory, offerId, revision, partyId);
        var offer = inventory.GetOffer(offerId);
        return Commit(
            checkpoint with { Inventory = inventory },
            offer.State == DirectBarterState.Settled ? "barter_settled" : "barter_accepted",
            $"{offerId}:{partyId}:r{revision}",
            offerId);
    }

    public static SocietyOperationResult CommitBirth(
        SocietyCheckpoint checkpoint,
        SocietyBirthRequest request)
    {
        Validate(checkpoint);
        ArgumentNullException.ThrowIfNull(request);
        ValidateBirthRequest(request, checkpoint.WorldTick);
        var existing = checkpoint.Births.SingleOrDefault(item => item.RequestId == request.Id);
        if (existing is not null)
        {
            return existing.Revision == request.Revision
                ? new SocietyOperationResult(checkpoint, existing.ChildId, [])
                : Reject(checkpoint, "birth_rejected", $"{request.Id}:revision_conflict");
        }

        var firstParent = checkpoint.GetInhabitant(request.FirstParentId);
        var secondParent = checkpoint.GetInhabitant(request.SecondParentId);
        var household = checkpoint.GetHousehold(request.HouseholdId);
        if (!IsAdult(firstParent) || !IsAdult(secondParent) ||
            !request.ConsentingParentIds.OrderBy(item => item, StringComparer.Ordinal)
                .SequenceEqual(new[] { firstParent.Id, secondParent.Id }.OrderBy(item => item, StringComparer.Ordinal)) ||
            !HasActivePartnership(checkpoint, firstParent.Id, secondParent.Id) ||
            request.CaregiverIds.Count == 0 ||
            request.CaregiverIds.Any(id => !household.MemberIds.Contains(id, StringComparer.Ordinal) ||
                !IsAdult(checkpoint.GetInhabitant(id))))
        {
            return Reject(checkpoint, "birth_rejected", $"{request.Id}:readiness_or_consent");
        }

        var sourceLot = checkpoint.Inventory.GetLot(request.FoodLotId);
        if (sourceLot.OwnerId != request.HouseholdId &&
            sourceLot.OwnerId != firstParent.Id && sourceLot.OwnerId != secondParent.Id)
        {
            return Reject(checkpoint, "birth_rejected", $"{request.Id}:food_access");
        }

        InventoryCheckpoint inventory;
        try
        {
            var reservationId = $"birth:{request.Id}:food";
            inventory = InventoryFixture.Reserve(
                checkpoint.Inventory,
                reservationId,
                sourceLot.OwnerId,
                request.FoodLotId,
                request.FoodQuantity,
                $"birth:{request.Id}",
                checkpoint.WorldTick);
            inventory = InventoryFixture.ConsumeReservation(inventory, reservationId);
        }
        catch (InvalidOperationException)
        {
            return Reject(checkpoint, "birth_rejected", $"{request.Id}:reservation_failed");
        }

        var childId = $"{checkpoint.WorldId}:inhabitant:{request.Id}";
        var child = new SocietyInhabitant(
            childId,
            request.ChildName is null ? $"Child {childId}" : NormalizeRequiredText(request.ChildName, nameof(request.ChildName)),
            checkpoint.WorldTick,
            SocietyInhabitantStatus.Active,
            SocietyAgeBand.Infant,
            10_000,
            household.Id,
            ResolveNewbornProvider(checkpoint, firstParent, secondParent, request),
            SocietyWorkRole.Unassigned,
            0,
            BirthLifeTick: checkpoint.LifeClock is null ? null : checkpoint.LifeTickAt(checkpoint.WorldTick));
        var relationships = checkpoint.Relationships.ToList();
        relationships.AddRange(
        [
            BirthRelationship(request, childId, SocietyRelationshipType.BiologicalParentage, firstParent.Id),
            BirthRelationship(request, childId, SocietyRelationshipType.BiologicalParentage, secondParent.Id),
            ..request.CaregiverIds.Select(caregiver => BirthRelationship(
                request,
                childId,
                SocietyRelationshipType.Caregiver,
                caregiver)),
            BirthRelationship(request, childId, SocietyRelationshipType.HouseholdMembership, household.Id),
        ]);
        var updatedHousehold = household with
        {
            MemberIds = household.MemberIds.Append(childId).Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            CaregiverIds = household.CaregiverIds.Union(request.CaregiverIds, StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal).ToArray(),
        };
        var next = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Append(child).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Households = checkpoint.Households.Select(item => item.Id == household.Id ? updatedHousehold : item)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Relationships = relationships.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Births = checkpoint.Births.Append(new SocietyBirthRecord(request.Id, childId, request.Revision, checkpoint.WorldTick))
                .OrderBy(item => item.RequestId, StringComparer.Ordinal).ToArray(),
            Inventory = inventory,
        };
        return Commit(next, "birth_committed", $"{request.Id}:{childId}", childId);
    }

    public static SocietyOperationResult AdvanceTo(
        SocietyCheckpoint checkpoint,
        long targetTick)
    {
        Validate(checkpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetTick, checkpoint.WorldTick);
        if (checkpoint.IsPaused)
        {
            throw new InvalidOperationException("A paused society must resume before it can advance.");
        }

        var current = checkpoint;
        var boundaryEvents = new List<(long Tick, string InhabitantId, int AgeYears)>();
        foreach (var inhabitant in checkpoint.Inhabitants.Where(item => item.Status == SocietyInhabitantStatus.Active))
        {
            var oldAge = checkpoint.AgeAt(inhabitant, checkpoint.WorldTick);
            var newAge = checkpoint.AgeAt(inhabitant, targetTick);
            for (var age = Math.Max(oldAge + 1, inhabitant.LastLifecycleYearChecked + 1); age <= newAge; age++)
            {
                boundaryEvents.Add((
                    checkpoint.LifeClock?.WorldTickFor(checked((inhabitant.BirthLifeTick ?? inhabitant.BirthTick) + age * checkpoint.Config.TicksPerLifecycleAge))
                        ?? checked(inhabitant.BirthTick + age * checkpoint.Config.TicksPerLifecycleAge),
                    inhabitant.Id,
                    checked((int)age)));
            }
        }

        foreach (var boundaryGroup in boundaryEvents
                     .GroupBy(item => item.Tick)
                     .OrderBy(group => group.Key))
        {
            current = current with
            {
                WorldTick = boundaryGroup.Key,
                Inventory = WithInventoryTick(current.Inventory, boundaryGroup.Key),
            };

            var transitionDetails = new List<string>();
            var mortalityIds = new List<string>();
            foreach (var boundary in boundaryGroup.OrderBy(item => item.InhabitantId, StringComparer.Ordinal))
            {
                var inhabitant = current.GetInhabitant(boundary.InhabitantId);
                if (inhabitant.Status != SocietyInhabitantStatus.Active)
                {
                    continue;
                }

                var nextBand = current.Config.AgeBandAt(boundary.AgeYears);
                current = current with
                {
                    Inhabitants = current.Inhabitants.Select(item => item.Id == inhabitant.Id
                            ? item with
                            {
                                AgeBand = nextBand,
                                LastLifecycleYearChecked = boundary.AgeYears,
                            }
                            : item)
                        .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                };
                if (nextBand != inhabitant.AgeBand)
                {
                    transitionDetails.Add($"{inhabitant.Id}:{inhabitant.AgeBand}:{nextBand}");
                }

                var risk = current.Config.NaturalMortalityRiskBasisPoints(boundary.AgeYears);
                if (risk > 0 && MortalityRoll(current.WorldId, inhabitant.Id, boundary.AgeYears) < risk)
                {
                    mortalityIds.Add(inhabitant.Id);
                }
            }

            current = CommitMany(
                current,
                transitionDetails.Select(detail => ("age_band_transition", detail)));
            foreach (var inhabitantId in mortalityIds)
            {
                if (current.GetInhabitant(inhabitantId).Status == SocietyInhabitantStatus.Active)
                {
                    current = Kill(current, inhabitantId, SocietyDeathCause.NaturalAge, current.WorldTick).Checkpoint;
                }
            }
        }

        current = current with
        {
            WorldTick = targetTick,
            Inventory = WithInventoryTick(current.Inventory, targetTick),
        };
        current = SettleDueEstates(current, targetTick);
        return new SocietyOperationResult(
            current,
            null,
            current.Events.Skip(checkpoint.Events.Count).ToArray());
    }

    public static SocietyOperationResult Kill(
        SocietyCheckpoint checkpoint,
        string inhabitantId,
        SocietyDeathCause cause,
        long? deathTick = null)
    {
        Validate(checkpoint);
        var inhabitant = checkpoint.GetInhabitant(inhabitantId);
        if (inhabitant.Status == SocietyInhabitantStatus.Dead)
        {
            return new SocietyOperationResult(checkpoint, null, []);
        }

        var tick = deathTick ?? checkpoint.WorldTick;
        ArgumentOutOfRangeException.ThrowIfLessThan(tick, checkpoint.WorldTick);
        return Kill(checkpoint with { WorldTick = tick }, inhabitantId, cause, tick);
    }

    public static SocietyOperationResult MarkWillStarted(SocietyCheckpoint checkpoint, string estateId)
    {
        Validate(checkpoint);
        var estate = checkpoint.GetEstate(estateId);
        if (estate.Settled || estate.WillStatus is not null)
            return new SocietyOperationResult(checkpoint, null, []);
        var next = checkpoint with
        {
            Estates = checkpoint.Estates.Select(item => item.Id == estateId
            ? item with { WillStatus = "pending" } : item).ToArray()
        };
        return Commit(next, "estate_will_started", estateId, estateId);
    }

    public static SocietyOperationResult ResolveWill(
        SocietyCheckpoint checkpoint, string estateId, string? beneficiaryId, string outcome)
    {
        Validate(checkpoint);
        var estate = checkpoint.GetEstate(estateId);
        if (estate.WillStatus != "pending" || estate.Settled)
            return new SocietyOperationResult(checkpoint, null, []);
        var validSnapshot = estate.FrozenLots is not null &&
            estate.FrozenLots.All(frozen => checkpoint.Inventory.Lots.Any(lot =>
                lot.Id == frozen.LotId && lot.OwnerId == estate.Id &&
                lot.ItemKind == frozen.ItemKind && lot.Quantity == frozen.Quantity)) &&
            checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == estate.Id).Count() == estate.FrozenLots.Count;
        var validBeneficiary = beneficiaryId is not null &&
            checkpoint.Inhabitants.Any(item => item.Id == beneficiaryId && item.Status == SocietyInhabitantStatus.Active);
        var accepted = validSnapshot && validBeneficiary && outcome == "accepted";
        var next = checkpoint with
        {
            Estates = checkpoint.Estates.Select(item => item.Id == estateId
            ? item with
            {
                BeneficiaryIds = accepted ? [beneficiaryId!] : item.BeneficiaryIds,
                WillStatus = accepted ? "accepted" : "default",
                WillBeneficiaryId = accepted ? beneficiaryId : null,
            } : item).ToArray()
        };
        return Commit(next, accepted ? "estate_will_accepted" : "estate_will_default",
            $"{estateId}:{(accepted ? beneficiaryId : outcome)}", estateId);
    }

    public static SocietyOperationResult Pause(SocietyCheckpoint checkpoint)
    {
        Validate(checkpoint);
        return checkpoint.IsPaused
            ? new SocietyOperationResult(checkpoint, null, [])
            : Commit(checkpoint with { IsPaused = true }, "paused", "requested");
    }

    public static SocietyOperationResult Resume(SocietyCheckpoint checkpoint)
    {
        Validate(checkpoint);
        return !checkpoint.IsPaused
            ? new SocietyOperationResult(checkpoint, null, [])
            : Commit(
                checkpoint with
                {
                    IsPaused = false,
                    RunEpoch = checked(checkpoint.RunEpoch + 1),
                },
                "resumed",
                $"epoch:{checkpoint.RunEpoch + 1}");
    }

    public static void Validate(SocietyCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.WorldId);
        if (checkpoint.WorldTick < 0 || checkpoint.RunEpoch < 0)
        {
            throw new InvalidDataException("Society clock and run epoch must be non-negative.");
        }

        checkpoint.Config.Validate();
        if (checkpoint.Inventory.WorldTick != checkpoint.WorldTick)
        {
            throw new InvalidDataException("Society and inventory clocks must agree.");
        }

        if (checkpoint.LifeClock is { } clock && (clock.Rate is not (1 or 365 or 1_460) ||
            clock.WorldAnchorTick < 0 || clock.WorldAnchorTick > checkpoint.WorldTick || clock.LifeAnchorTick < clock.WorldAnchorTick ||
            checkpoint.Config.ContractVersion < 2))
        {
            throw new InvalidDataException("The biological life clock is invalid.");
        }
        ValidateInhabitants(checkpoint.Inhabitants, checkpoint.Config, checkpoint.WorldTick, checkpoint.LifeClock);
        EnsureCanonicalIds(checkpoint.Households.Select(item => item.Id), "households");
        EnsureCanonicalIds(checkpoint.Relationships.Select(item => item.Id), "relationships");
        EnsureCanonicalIds(checkpoint.Organizations.Select(item => item.Id), "organizations");
        EnsureCanonicalIds(checkpoint.Memories.Select(item => item.Id), "memories");
        ValidateAgentBeliefs(checkpoint);
        ValidateAgentMemoryCompactions(checkpoint);
        EnsureCanonicalIds(checkpoint.Estates.Select(item => item.Id), "estates");
        EnsureCanonicalIds(checkpoint.Births.Select(item => item.RequestId), "births");
        foreach (var estate in checkpoint.Estates)
        {
            if (estate.CreatedTick < 0 || estate.ExpiryTick < estate.CreatedTick ||
                !checkpoint.Inhabitants.Any(item => item.Id == estate.DeceasedId &&
                    item.Status == SocietyInhabitantStatus.Dead) ||
                estate.WillStatus is not (null or "pending" or "accepted" or "default") ||
                (estate.WillStatus == "accepted" &&
                    (estate.WillBeneficiaryId is null ||
                     !estate.BeneficiaryIds.Contains(estate.WillBeneficiaryId, StringComparer.Ordinal))) ||
                (estate.WillStatus != "accepted" && estate.WillBeneficiaryId is not null))
                throw new InvalidDataException("An estate record is malformed.");
            if (estate.FrozenLots is { } frozen)
            {
                EnsureCanonicalIds(frozen.Select(item => item.LotId), $"estate:{estate.Id}:lots");
                if (frozen.Any(item => string.IsNullOrWhiteSpace(item.ItemKind) || item.Quantity <= 0))
                    throw new InvalidDataException("An estate snapshot is malformed.");
            }
        }
        foreach (var household in checkpoint.Households)
        {
            EnsureCanonicalIds(household.MemberIds, $"household:{household.Id}:members");
            EnsureCanonicalIds(household.CaregiverIds, $"household:{household.Id}:caregivers");
            if (household.MemberIds.Any(id => !checkpoint.Inhabitants.Any(item => item.Id == id)))
            {
                throw new InvalidDataException("Households cannot reference unknown inhabitants.");
            }
        }

        foreach (var relationship in checkpoint.Relationships)
        {
            if (relationship.Revision <= 0 || relationship.ProposedTick < 0 || relationship.EffectiveTick < 0 ||
                string.IsNullOrWhiteSpace(relationship.ProposerId) ||
                string.IsNullOrWhiteSpace(relationship.TargetId) ||
                (relationship.State == SocietyRelationshipState.Accepted &&
                    relationship.Consent is not (SocietyConsentState.Accepted or SocietyConsentState.ProtectedLifecycle)))
            {
                throw new InvalidDataException("A relationship record is malformed.");
            }
        }

        if (checkpoint.EventHistoryFloor < 0)
        {
            throw new InvalidDataException("The society event history floor is invalid.");
        }
        var expectedEventId = checked(checkpoint.EventHistoryFloor + 1);
        var previousTick = 0L;
        foreach (var societyEvent in checkpoint.Events)
        {
            if (societyEvent.EventId != expectedEventId || societyEvent.WorldTick < previousTick ||
                societyEvent.WorldTick > checkpoint.WorldTick)
            {
                throw new InvalidDataException("Society events must be a canonical committed sequence.");
            }

            expectedEventId++;
            previousTick = societyEvent.WorldTick;
        }
    }

    private static void ValidateAgentBeliefs(SocietyCheckpoint checkpoint)
    {
        var beliefs = checkpoint.Beliefs ?? [];
        EnsureCanonicalIds(beliefs.Select(item => item.Id), "agent beliefs");
        var byId = beliefs.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var belief in beliefs)
        {
            ValidateBeliefInput(checkpoint, belief, allowSupersedes: true);
            if (belief.SupersedesBeliefId is { } priorId)
            {
                if (!byId.TryGetValue(priorId, out var prior) || prior.OwnerId != belief.OwnerId ||
                    prior.SupersededByBeliefId != belief.Id || prior.SupersededTick != belief.FormedTick ||
                    prior.FormedTick > belief.FormedTick)
                    throw new InvalidDataException("An agent belief correction has no matching owner-private predecessor.");
            }

            if ((belief.SupersededByBeliefId is null) != (belief.SupersededTick is null))
                throw new InvalidDataException("An agent belief correction link is incomplete.");
            if (belief.SupersededByBeliefId is { } replacementId &&
                (!byId.TryGetValue(replacementId, out var replacement) || replacement.OwnerId != belief.OwnerId ||
                 replacement.SupersedesBeliefId != belief.Id || replacement.FormedTick != belief.SupersededTick))
                throw new InvalidDataException("An agent belief correction link is inconsistent.");
        }
    }

    private static void ValidateAgentMemoryCompactions(SocietyCheckpoint checkpoint)
    {
        var compactions = checkpoint.MemoryCompactions ?? [];
        EnsureCanonicalIds(compactions.Select(item => item.OwnerId), "agent memory compactions");
        var memoryById = checkpoint.Memories.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var beliefById = (checkpoint.Beliefs ?? []).ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var compaction in compactions)
        {
            if (!IsCanonicalBoundedText(compaction.OwnerId, 128) || compaction.Sources is null ||
                compaction.Sources.Count is < 1 or > 256 ||
                !checkpoint.Inhabitants.Any(item => item.Id == compaction.OwnerId))
                throw new InvalidDataException("An agent memory compaction is malformed.");

            var expected = compaction.Sources.OrderBy(item => item.Kind)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal).ToArray();
            if (!compaction.Sources.SequenceEqual(expected) ||
                compaction.Sources.Select(item => $"{item.Kind}:{item.SourceId}")
                    .Distinct(StringComparer.Ordinal).Count() != compaction.Sources.Count)
                throw new InvalidDataException("Agent memory compaction sources must be unique and canonical.");

            foreach (var source in compaction.Sources)
            {
                if (!IsCanonicalBoundedText(source.SourceId, 128) || !Enum.IsDefined(source.Kind) ||
                    source.SourceTick < 0 || source.SourceTick > checkpoint.WorldTick ||
                    source.AssessedTick < source.SourceTick || source.AssessedTick > checkpoint.WorldTick ||
                    source.ImportanceBasisPoints is < 0 or > 10_000 ||
                    source.ImportanceConfidenceBasisPoints is < 0 or > 10_000)
                    throw new InvalidDataException("An agent memory compaction assessment is malformed.");

                var validOwnerSource = source.Kind switch
                {
                    SocietyMemorySourceKind.Experience => memoryById.TryGetValue(source.SourceId, out var memory) &&
                        memory.OwnerId == compaction.OwnerId && memory.SourceTick == source.SourceTick,
                    SocietyMemorySourceKind.Belief => beliefById.TryGetValue(source.SourceId, out var belief) &&
                        belief.OwnerId == compaction.OwnerId && belief.FormedTick == source.SourceTick,
                    _ => false,
                };
                if (!validOwnerSource)
                    throw new InvalidDataException("An agent memory compaction references another agent or an unknown source.");
            }
        }
    }

    private static void ValidateBeliefInput(
        SocietyCheckpoint checkpoint,
        SocietyAgentBelief belief,
        bool allowSupersedes)
    {
        if (!IsSafeBeliefId(belief.Id) || !IsCanonicalBoundedText(belief.OwnerId, 128) ||
            !IsCanonicalBoundedText(belief.Statement, 512) ||
            belief.Statement.Any(char.IsControl) || !Enum.IsDefined(belief.Provenance) ||
            belief.ConfidenceBasisPoints is < 0 or > 10_000 || belief.FormedTick < 0 ||
            belief.FormedTick > checkpoint.WorldTick || belief.SourceEventId is <= 0 ||
            belief.SupersededTick is < 0 || belief.SupersededTick > checkpoint.WorldTick ||
            belief.SupersededByBeliefId is { } superseding && !IsSafeBeliefId(superseding) ||
            belief.SupersedesBeliefId is { } superseded && !IsSafeBeliefId(superseded))
            throw new InvalidDataException("An agent belief has invalid bounded fields.");

        if (!checkpoint.Inhabitants.Any(item => item.Id == belief.OwnerId) ||
            belief.SourceAgentId is { } sourceAgent && !checkpoint.Inhabitants.Any(item => item.Id == sourceAgent) ||
            belief.AboutInhabitantId is { } subject && !checkpoint.Inhabitants.Any(item => item.Id == subject))
            throw new InvalidDataException("An agent belief references an unknown inhabitant.");

        if ((belief.Provenance == SocietyBeliefProvenance.Hearsay &&
             (belief.SourceAgentId is null || belief.SourceAgentId == belief.OwnerId)) ||
            (belief.Provenance == SocietyBeliefProvenance.Firsthand &&
             belief.SourceAgentId is not null && belief.SourceAgentId != belief.OwnerId))
            throw new InvalidDataException("An agent belief's provenance does not match its witness or reporter.");

        if ((!allowSupersedes && (belief.SupersedesBeliefId is not null ||
                                  belief.SupersededByBeliefId is not null || belief.SupersededTick is not null)) ||
            belief.SupersededByBeliefId is null && belief.SupersededTick is not null)
            throw new InvalidDataException("Only a correction may link a belief to its predecessor.");
    }

    private static SocietyAgentBelief NormalizeBelief(SocietyAgentBelief belief) => belief with
    {
        Id = NormalizeRequiredText(belief.Id, nameof(belief.Id)),
        OwnerId = NormalizeRequiredText(belief.OwnerId, nameof(belief.OwnerId)),
        Statement = NormalizeBeliefStatement(belief.Statement),
        SourceAgentId = NormalizeOptionalText(belief.SourceAgentId),
        AboutInhabitantId = NormalizeOptionalText(belief.AboutInhabitantId),
        SupersedesBeliefId = NormalizeOptionalText(belief.SupersedesBeliefId),
        SupersededByBeliefId = NormalizeOptionalText(belief.SupersededByBeliefId),
    };

    private static string NormalizeBeliefStatement(string statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var normalized = statement.Trim();
        if (normalized.Length is 0 or > 512 || normalized.Any(char.IsControl))
            throw new ArgumentOutOfRangeException(nameof(statement), "A belief must be bounded single-line text.");
        return normalized;
    }

    private static bool IsCanonicalBoundedText(string? value, int maximumLength) =>
        value is { Length: > 0 } && value.Length <= maximumLength &&
        value == value.Trim() && !value.Any(char.IsControl);

    // Belief IDs appear in operational telemetry. Keep caller-provided prose
    // and other private content out of this identifier channel.
    private static bool IsSafeBeliefId(string? value) =>
        IsCanonicalBoundedText(value, 128) &&
        value!.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '-' or '_');

    private static SocietyOperationResult Kill(
        SocietyCheckpoint checkpoint,
        string inhabitantId,
        SocietyDeathCause cause,
        long deathTick)
    {
        var inhabitant = checkpoint.GetInhabitant(inhabitantId);
        var estateId = $"estate:{inhabitant.Id}:{deathTick.ToString(CultureInfo.InvariantCulture)}";
        var householdBeneficiaries = checkpoint.Households
            .Where(household => household.MemberIds.Contains(inhabitant.Id, StringComparer.Ordinal))
            .SelectMany(household => household.MemberIds.Concat(household.CaregiverIds))
            .Where(id => id != inhabitant.Id)
            .Where(id => checkpoint.GetInhabitant(id).Status == SocietyInhabitantStatus.Active)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var frozenLots = checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == inhabitant.Id)
            .OrderBy(lot => lot.Id, StringComparer.Ordinal)
            .Select(lot => new SocietyEstateLot(lot.Id, lot.ItemKind, lot.Quantity)).ToArray();
        var inventory = MoveOwnedLotsToEstate(checkpoint.Inventory, inhabitant.Id, estateId, deathTick);
        var relationships = checkpoint.Relationships.Select(relationship =>
                relationship.ProposerId == inhabitant.Id || relationship.TargetId == inhabitant.Id
                    ? relationship with
                    {
                        State = relationship.State is SocietyRelationshipState.Accepted or SocietyRelationshipState.Proposed
                            ? SocietyRelationshipState.EndedByDeath
                            : relationship.State,
                        Consent = relationship.State is SocietyRelationshipState.Accepted or SocietyRelationshipState.Proposed
                            ? SocietyConsentState.Revoked
                            : relationship.Consent,
                        EffectiveTick = deathTick,
                    }
                    : relationship)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var next = checkpoint with
        {
            WorldTick = deathTick,
            Inhabitants = checkpoint.Inhabitants.Select(item => item.Id == inhabitant.Id
                    ? item with
                    {
                        Status = SocietyInhabitantStatus.Dead,
                        HealthBasisPoints = 0,
                        DeathTick = deathTick,
                        DeathCause = cause,
                    }
                    : item)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Relationships = relationships,
            Estates = checkpoint.Estates.Append(new SocietyEstate(
                    estateId,
                    inhabitant.Id,
                    deathTick,
                    checked(deathTick + checkpoint.Config.EstateEscrowDays *
                        (long)checkpoint.Config.TicksPerWorldDay),
                    householdBeneficiaries,
                    FrozenLots: frozenLots))
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            Inventory = inventory,
        };
        var result = Commit(next, "death_committed", $"{inhabitant.Id}:{cause}:{deathTick}", inhabitant.Id);
        foreach (var relationship in relationships.Where(item =>
                     (item.ProposerId == inhabitant.Id || item.TargetId == inhabitant.Id) &&
                     item.State == SocietyRelationshipState.EndedByDeath))
        {
            result = Commit(result.Checkpoint, "relationship_ended_by_death", relationship.Id);
        }

        return Commit(
            result.Checkpoint,
            "estate_created",
            $"{estateId}:{string.Join(',', householdBeneficiaries)}",
            estateId);
    }

    private static SocietyCheckpoint SettleDueEstates(
        SocietyCheckpoint checkpoint,
        long targetTick)
    {
        var current = checkpoint;
        foreach (var estate in checkpoint.Estates.Where(item => !item.Settled &&
                     item.WillStatus != "pending" && item.ExpiryTick <= targetTick)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var beneficiaries = estate.BeneficiaryIds
                .Where(id => current.Inhabitants.Any(item =>
                    item.Id == id && item.Status == SocietyInhabitantStatus.Active))
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var lots = current.Inventory.Lots.Where(lot => lot.OwnerId == estate.Id).ToArray();
            var nextLots = current.Inventory.Lots.Where(lot => lot.OwnerId != estate.Id).ToList();
            foreach (var lot in lots)
            {
                if (beneficiaries.Length == 0)
                {
                    nextLots.Add(lot with
                    {
                        OwnerId = "settlement:communal",
                        StorageBuildingId = null,
                        DeliveryBuildingId = null,
                    });
                    continue;
                }

                var baseShare = lot.Quantity / beneficiaries.Length;
                var remainder = lot.Quantity % beneficiaries.Length;
                for (var index = 0; index < beneficiaries.Length; index++)
                {
                    var quantity = baseShare + (index < remainder ? 1 : 0);
                    if (quantity == 0)
                    {
                        continue;
                    }

                    nextLots.Add(lot with
                    {
                        Id = $"{lot.Id}#estate:{estate.Id}:{beneficiaries[index]}",
                        OwnerId = beneficiaries[index],
                        Quantity = quantity,
                        ProvenanceLotId = lot.Id,
                        StorageBuildingId = null,
                        DeliveryBuildingId = null,
                    });
                }
            }

            var inventoryEvents = current.Inventory.Events.ToList();
            inventoryEvents.Add(new InventoryEvent(
                checked(current.Inventory.EventHistoryFloor + inventoryEvents.Count + 1L),
                targetTick,
                "estate_settled",
                estate.Id));
            current = current with
            {
                WorldTick = targetTick,
                Inventory = new InventoryCheckpoint(
                    targetTick,
                    nextLots.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                    current.Inventory.Reservations,
                    current.Inventory.Offers,
                    inventoryEvents, current.Inventory.EventHistoryFloor),
                Estates = current.Estates.Select(item =>
                        item.Id == estate.Id ? item with { Settled = true } : item)
                    .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            };
            current = Commit(current, "estate_settled", estate.Id).Checkpoint;
        }

        return current;
    }

    private static InventoryCheckpoint MoveOwnedLotsToEstate(
        InventoryCheckpoint inventory,
        string ownerId,
        string estateId,
        long targetTick)
    {
        // Use the normal cancellation transition so both parties' reservations
        // are released. Completed exchanges are not retroactively cancelled.
        foreach (var offer in inventory.Offers.Where(offer => offer.State == DirectBarterState.Open &&
                     (offer.FirstPartyId == ownerId || offer.SecondPartyId == ownerId)).ToArray())
            inventory = InventoryFixture.CancelDirectBarterOffer(inventory, offer.Id, offer.Revision, ownerId);
        var lots = inventory.Lots.Select(lot => lot.OwnerId == ownerId
                ? lot with { OwnerId = estateId, StorageBuildingId = null, DeliveryBuildingId = null }
                : lot)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var reservations = inventory.Reservations.Select(reservation => reservation.OwnerId == ownerId
                ? reservation with { State = InventoryReservationState.Released }
                : reservation)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var offers = inventory.Offers.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var events = inventory.Events.ToList();
        events.Add(new InventoryEvent(
            checked(inventory.EventHistoryFloor + events.Count + 1L),
            targetTick,
            "estate_escrow_created",
            estateId));
        return new InventoryCheckpoint(targetTick, lots, reservations, offers, events, inventory.EventHistoryFloor);
    }

    private static InventoryCheckpoint WithInventoryTick(
        InventoryCheckpoint inventory,
        long targetTick) =>
        inventory.WorldTick == targetTick
            ? inventory
            : InventoryFixture.ReleaseExpiredReservations(inventory, targetTick);

    private static SocietyRelationship BirthRelationship(
        SocietyBirthRequest request,
        string childId,
        SocietyRelationshipType type,
        string sourceId)
    {
        return new SocietyRelationship(
            $"birth:{request.Id}:{type}:{sourceId}",
            1,
            type,
            sourceId,
            childId,
            SocietyRelationshipState.Accepted,
            SocietyConsentState.ProtectedLifecycle,
            request.RequestedTick,
            request.RequestedTick,
            type == SocietyRelationshipType.HouseholdMembership ? "household" : "family",
            request.HouseholdId,
            new[] { sourceId, childId }.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    private static string? ResolveNewbornProvider(
        SocietyCheckpoint checkpoint,
        SocietyInhabitant firstParent,
        SocietyInhabitant secondParent,
        SocietyBirthRequest request) =>
        request.ProviderPolicy switch
        {
            NewbornProviderPolicy.PerChild => NormalizeOptionalText(request.RequestedProviderBindingId),
            NewbornProviderPolicy.ParentInheritance =>
                firstParent.ProviderBindingId == secondParent.ProviderBindingId
                    ? firstParent.ProviderBindingId
                    : null,
            NewbornProviderPolicy.WorldDefault => checkpoint.WorldDefaultProviderBindingId,
            NewbornProviderPolicy.Hybrid =>
                firstParent.ProviderBindingId is not null &&
                firstParent.ProviderBindingId == secondParent.ProviderBindingId
                    ? firstParent.ProviderBindingId
                    : checkpoint.WorldDefaultProviderBindingId,
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    private static SocietyCheckpoint ApplyRelationshipProjection(
        SocietyCheckpoint checkpoint,
        SocietyRelationship relationship)
    {
        if (relationship.Type != SocietyRelationshipType.HouseholdMembership &&
            relationship.Type != SocietyRelationshipType.Caregiver)
        {
            return checkpoint;
        }

        if (relationship.HouseholdId is null)
        {
            return checkpoint;
        }

        var household = checkpoint.GetHousehold(relationship.HouseholdId);
        var members = relationship.Type == SocietyRelationshipType.HouseholdMembership
            ? household.MemberIds.Append(relationship.TargetId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : household.MemberIds;
        var caregivers = relationship.Type == SocietyRelationshipType.Caregiver
            ? household.CaregiverIds.Append(relationship.ProposerId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : household.CaregiverIds;
        return checkpoint with
        {
            Inhabitants = relationship.Type == SocietyRelationshipType.HouseholdMembership
                ? checkpoint.Inhabitants.Select(item => item.Id == relationship.TargetId
                        ? item with { HouseholdId = household.Id }
                        : item)
                    .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray()
                : checkpoint.Inhabitants,
            Households = checkpoint.Households.Select(item => item.Id == household.Id
                    ? item with { MemberIds = members, CaregiverIds = caregivers }
                    : item)
                .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
    }

    private static SocietyCheckpoint RemoveRelationshipProjection(
        SocietyCheckpoint checkpoint,
        SocietyRelationship relationship)
    {
        if (relationship.Type != SocietyRelationshipType.HouseholdMembership &&
            relationship.Type != SocietyRelationshipType.Caregiver ||
            relationship.HouseholdId is null)
        {
            return checkpoint;
        }

        var household = checkpoint.GetHousehold(relationship.HouseholdId);
        return checkpoint with
        {
            Inhabitants = relationship.Type == SocietyRelationshipType.HouseholdMembership
                ? checkpoint.Inhabitants.Select(item => item.Id == relationship.TargetId
                        ? item with { HouseholdId = null }
                        : item)
                    .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray()
                : checkpoint.Inhabitants,
            Households = checkpoint.Households.Select(item => item.Id == household.Id
                    ? item with
                    {
                        MemberIds = relationship.Type == SocietyRelationshipType.HouseholdMembership
                            ? household.MemberIds.Where(id => id != relationship.TargetId).ToArray()
                            : household.MemberIds,
                        CaregiverIds = relationship.Type == SocietyRelationshipType.Caregiver
                            ? household.CaregiverIds.Where(id => id != relationship.ProposerId ||
                                checkpoint.Relationships.Any(other => other.Id != relationship.Id &&
                                    other.Type == SocietyRelationshipType.Caregiver && other.State == SocietyRelationshipState.Accepted &&
                                    other.ProposerId == id && other.HouseholdId == household.Id)).ToArray()
                            : household.CaregiverIds,
                    }
                    : item)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };
    }

    private static SocietyOperationResult Commit(
        SocietyCheckpoint checkpoint,
        string kind,
        string detail,
        string? createdId = null)
    {
        var next = CommitMany(checkpoint, [(kind, detail)]);
        return new SocietyOperationResult(next, createdId, [next.Events[^1]]);
    }

    private static SocietyCheckpoint CommitMany(
        SocietyCheckpoint checkpoint,
        IEnumerable<(string Kind, string Detail)> pending)
    {
        var events = checkpoint.Events.ToList();
        foreach (var item in pending)
        {
            events.Add(new SocietyEvent(
                checked(checkpoint.EventHistoryFloor + events.Count + 1L),
                checkpoint.WorldTick,
                NormalizeRequiredText(item.Kind, nameof(item.Kind)),
                NormalizeRequiredText(item.Detail, nameof(item.Detail))));
        }

        var next = checkpoint with { Events = events };
        Validate(next);
        return next;
    }

    private static SocietyOperationResult Reject(
        SocietyCheckpoint checkpoint,
        string kind,
        string detail) =>
        Commit(checkpoint, kind, detail);

    private static bool HasActivePartnership(
        SocietyCheckpoint checkpoint,
        string firstId,
        string secondId) =>
        checkpoint.Relationships.Any(item =>
            item.Type == SocietyRelationshipType.Partnership &&
            item.State == SocietyRelationshipState.Accepted &&
            ((item.ProposerId == firstId && item.TargetId == secondId) ||
             (item.ProposerId == secondId && item.TargetId == firstId)));

    private static bool IsAdult(SocietyInhabitant inhabitant) =>
        inhabitant.Status == SocietyInhabitantStatus.Active &&
        inhabitant.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder;

    private static void EnsureActive(SocietyCheckpoint checkpoint, string id)
    {
        if (checkpoint.GetInhabitant(id).Status != SocietyInhabitantStatus.Active)
        {
            throw new InvalidOperationException($"Inhabitant '{id}' is not active.");
        }
    }

    private static void EnsureLivingParty(
        SocietyCheckpoint checkpoint,
        string id)
    {
        if (!checkpoint.Inhabitants.Any(item =>
                item.Id == id && item.Status == SocietyInhabitantStatus.Active) &&
            !checkpoint.Households.Any(item => item.Id == id) &&
            !checkpoint.Organizations.Any(item => item.Id == id))
        {
            throw new InvalidOperationException($"Party '{id}' is not an active society participant.");
        }
    }

    private static void ValidateProposal(
        SocietyRelationshipProposal proposal,
        long worldTick)
    {
        NormalizeRequiredText(proposal.Id, nameof(proposal.Id));
        NormalizeRequiredText(proposal.ProposerId, nameof(proposal.ProposerId));
        NormalizeRequiredText(proposal.TargetId, nameof(proposal.TargetId));
        NormalizeRequiredText(proposal.PrivacyClass, nameof(proposal.PrivacyClass));
        if (proposal.Revision <= 0 || proposal.ProposerId == proposal.TargetId ||
            proposal.RequestedTick < 0 || proposal.RequestedTick > worldTick ||
            proposal.Type is SocietyRelationshipType.BiologicalParentage)
        {
            throw new ArgumentOutOfRangeException(nameof(proposal));
        }
    }

    private static void ValidateBirthRequest(
        SocietyBirthRequest request,
        long worldTick)
    {
        NormalizeRequiredText(request.Id, nameof(request.Id));
        NormalizeRequiredText(request.FirstParentId, nameof(request.FirstParentId));
        NormalizeRequiredText(request.SecondParentId, nameof(request.SecondParentId));
        NormalizeRequiredText(request.HouseholdId, nameof(request.HouseholdId));
        if (request.ChildName is not null && (string.IsNullOrWhiteSpace(request.ChildName) || request.ChildName.Length > 80))
        {
            throw new ArgumentException("A child name must contain between 1 and 80 characters.", nameof(request));
        }
        if (request.Revision <= 0 || request.FirstParentId == request.SecondParentId ||
            request.FoodQuantity <= 0 || request.RequestedTick < 0 ||
            request.RequestedTick > worldTick || request.CaregiverIds.Count == 0 ||
            request.ConsentingParentIds.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static void ValidateInhabitants(
        IReadOnlyList<SocietyInhabitant> inhabitants,
        SocietyConfig config,
        long worldTick,
        SocietyLifeClock? lifeClock = null)
    {
        EnsureCanonicalIds(inhabitants.Select(item => item.Id), "inhabitants");
        foreach (var inhabitant in inhabitants)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(inhabitant.Name);
            if (inhabitant.BirthTick > worldTick ||
                inhabitant.BirthLifeTick is { } birthLife && (lifeClock is null || birthLife > lifeClock.At(worldTick)) ||
                inhabitant.HealthBasisPoints is < 0 or > 10_000 ||
                inhabitant.LastLifecycleYearChecked < 0 ||
                inhabitant.Status == SocietyInhabitantStatus.Active && inhabitant.DeathTick is not null ||
                inhabitant.Status == SocietyInhabitantStatus.Dead && inhabitant.DeathTick is null)
            {
                throw new InvalidDataException("An inhabitant lifecycle record is malformed.");
            }

            var birth = inhabitant.BirthLifeTick ?? inhabitant.BirthTick;
            var lifeTick = lifeClock?.At(worldTick) ?? worldTick;
            var expectedBand = config.AgeBandAt(birth, lifeTick);
            if (inhabitant.Status == SocietyInhabitantStatus.Active &&
                inhabitant.AgeBand != expectedBand &&
                birth + config.TicksPerLifecycleAge * inhabitant.LastLifecycleYearChecked <= lifeTick)
            {
                throw new InvalidDataException(
                    $"An active inhabitant has a stale age band: {inhabitant.Id}:{inhabitant.AgeBand}:{expectedBand}:" +
                    $"tick={worldTick}:birth={inhabitant.BirthTick}:last={inhabitant.LastLifecycleYearChecked}.");
            }
        }
    }

    private static int MortalityRoll(
        string worldId,
        string inhabitantId,
        int ageYears)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"clankerworld.society-mortality/v1|{worldId}|{inhabitantId}|{ageYears}"));
        return checked((int)(BitConverter.ToUInt32(bytes, 0) % 10_000));
    }

    private static string[] CanonicalIds(
        IEnumerable<string> ids,
        string name)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var actual = ids.Select(id => NormalizeRequiredText(id, name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (actual.Length == 0)
        {
            throw new ArgumentException("At least one ID is required.", name);
        }

        return actual;
    }

    private static void EnsureCanonicalIds(
        IEnumerable<string> ids,
        string name)
    {
        var actual = ids.ToArray();
        if (actual.Any(string.IsNullOrWhiteSpace) ||
            actual.Distinct(StringComparer.Ordinal).Count() != actual.Length ||
            !actual.SequenceEqual(actual.OrderBy(id => id, StringComparer.Ordinal)))
        {
            throw new InvalidDataException($"Society {name} must contain unique canonical IDs.");
        }
    }

    private static int ValidateBasisPoints(int value, string name)
    {
        if (value is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }

    private static string NormalizeRequiredText(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
