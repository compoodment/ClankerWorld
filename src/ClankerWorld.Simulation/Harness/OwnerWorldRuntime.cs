using System.Globalization;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;

namespace ClankerWorld.Simulation.Harness;

/// <summary>
/// The two owner-to-inhabitant instruction strengths exposed by the Phase 2
/// fixture. The request never carries an instruction ID: that ID is minted by
/// the authoritative runtime after validation.
/// </summary>
public enum OwnerInstructionKind
{
    Suggestive,
    MustDo,
}

/// <summary>
/// The minimal durable lifecycle for an owner instruction entering the
/// cognition-aware fixture. Later phases may add active, completed, and
/// rejected states without making a client responsible for changing a queued
/// instruction's state.
/// </summary>
public enum OwnerInstructionState
{
    Queued,
}

/// <summary>
/// An untrusted client request to queue a human instruction. The idempotency
/// key is client supplied; every other durable identifier is server minted.
/// </summary>
public sealed record OwnerInstructionRequest(
    string IdempotencyKey,
    string IssuerId,
    string TargetInhabitantId,
    OwnerInstructionKind Kind,
    string Text);

/// <summary>
/// The authoritative queued record retained by the world runtime.
/// </summary>
public sealed record OwnerQueuedInstruction(
    string InstructionId,
    string IdempotencyKey,
    string IssuerId,
    string TargetInhabitantId,
    OwnerInstructionKind Kind,
    string Text,
    long SubmittedTick,
    long RunEpoch,
    long SubmissionSequence,
    OwnerInstructionState State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ObservedTick = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ObserverReply = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? GuidancePromptedTick = null)
{
    public const int MaximumTextLength = 512;
    public const int MaximumIdentifierLength = 128;
}

/// <summary>
/// A stable reply to an instruction submission. Repeating the same request
/// with its idempotency key returns this exact receipt and does not append a
/// second event.
/// </summary>
public sealed record OwnerInstructionReceipt(
    string InstructionId,
    string IdempotencyKey,
    long SubmittedTick,
    long RunEpoch,
    long Revision);

/// <summary>
/// One event in the Phase 2 composite runtime's global event stream. This is
/// deliberately separate from <see cref="HarnessWorld.Events"/>, whose IDs
/// belong to the original Phase 1 scripted fixture only.
/// </summary>
public sealed record OwnerWorldEvent(
    long EventId,
    long WorldTick,
    long Revision,
    string Kind,
    string Detail);

/// <summary>
/// Deliberately small authoring-time climate state. Weather and season do not
/// yet feed the Phase 1 harness; retaining them here makes the observation and
/// authoring boundary truthful without pretending the fixture simulates them.
/// </summary>
public sealed record OwnerClimate(string Weather, string Season);

/// <summary>
/// A proposed founder. It is intentionally not a <see cref="HarnessActor"/>:
/// paused authoring may stage future inhabitants but may not edit the protected
/// actor record used by the existing deterministic fixture.
/// </summary>
public sealed record OwnerFounderDraft(
    string Id,
    string DisplayName,
    GridPoint Position,
    long CreatedRevision);

/// <summary>
/// A reference that has already passed whatever approval pipeline owns the
/// actual asset bytes. Phase 2 stores only the approved reference, never bytes
/// or an untrusted client-side asset payload.
/// </summary>
public sealed record OwnerApprovedAssetReference(string AssetId, string AssetDigest);

/// <summary>
/// Decides whether an asset reference has already been approved by the
/// server-owned asset catalog. The runtime deliberately accepts a policy
/// rather than a client-provided allow-list, so an owner request can name an
/// asset but cannot make that asset approved.
/// </summary>
public interface IOwnerApprovedAssetReferencePolicy
{
    /// <summary>
    /// Returns whether this exact canonical ID/digest pair is approved for
    /// attachment to the Phase 2 authored-world projection.
    /// </summary>
    bool IsApproved(OwnerApprovedAssetReference reference);
}

/// <summary>
/// Fail-closed default for asset references. Hosts must explicitly inject a
/// server-owned catalog policy before they can accept asset attachments.
/// </summary>
public sealed class DenyAllApprovedAssetReferencePolicy : IOwnerApprovedAssetReferencePolicy
{
    public static DenyAllApprovedAssetReferencePolicy Instance { get; } = new();

    private DenyAllApprovedAssetReferencePolicy()
    {
    }

    public bool IsApproved(OwnerApprovedAssetReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return false;
    }
}

/// <summary>
/// Base type for paused authoring operations. There is no actor-field operation
/// by design; map changes, founder drafts, climate, and approved references are
/// the entire Phase 2 fixture surface.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SetTerrainOperation), "set_terrain")]
[JsonDerivedType(typeof(PlaceResourceOperation), "place_resource")]
[JsonDerivedType(typeof(RemoveResourceOperation), "remove_resource")]
[JsonDerivedType(typeof(PlaceObjectOperation), "place_object")]
[JsonDerivedType(typeof(RemoveObjectOperation), "remove_object")]
[JsonDerivedType(typeof(PlaceBuildingOperation), "place_building")]
[JsonDerivedType(typeof(RemoveBuildingOperation), "remove_building")]
[JsonDerivedType(typeof(PlacePlantOperation), "place_plant")]
[JsonDerivedType(typeof(RemovePlantOperation), "remove_plant")]
[JsonDerivedType(typeof(CreateFounderDraftOperation), "create_founder_draft")]
[JsonDerivedType(typeof(RemoveFounderDraftOperation), "remove_founder_draft")]
[JsonDerivedType(typeof(SetWeatherOperation), "set_weather")]
[JsonDerivedType(typeof(SetSeasonOperation), "set_season")]
[JsonDerivedType(typeof(SetWeatherSeasonOperation), "set_weather_season")]
[JsonDerivedType(typeof(AddApprovedAssetReferenceOperation), "add_approved_asset_reference")]
[JsonDerivedType(typeof(RemoveApprovedAssetReferenceOperation), "remove_approved_asset_reference")]
public abstract record OwnerAuthoringOperation
{
    public abstract string OperationKind { get; }
}

public sealed record SetTerrainOperation(GridPoint Position, TerrainKind Terrain) : OwnerAuthoringOperation
{
    public override string OperationKind => "set_terrain";
}

public sealed record PlaceResourceOperation(
    string Id,
    string Kind,
    GridPoint Position,
    bool IsRenewable) : OwnerAuthoringOperation
{
    public override string OperationKind => "place_resource";
}

public sealed record RemoveResourceOperation(string Id) : OwnerAuthoringOperation
{
    public override string OperationKind => "remove_resource";
}

/// <summary>
/// Places a generic ecosystem/world object. Use a meaningful kind such as
/// <c>plant:oak</c> or <c>object:landmark</c>; the existing map manifest is
/// intentionally the common representation for objects, plants, and buildings.
/// </summary>
public sealed record PlaceObjectOperation(string Id, string Kind, GridPoint Position) : OwnerAuthoringOperation
{
    public override string OperationKind => "place_object";
}

public sealed record RemoveObjectOperation(string Id) : OwnerAuthoringOperation
{
    public override string OperationKind => "remove_object";
}

/// <summary>
/// A convenience operation for a building placement. Its resulting map object
/// uses <paramref name="BuildingKind"/> as its manifest kind.
/// </summary>
public sealed record PlaceBuildingOperation(string Id, string BuildingKind, GridPoint Position) : OwnerAuthoringOperation
{
    public override string OperationKind => "place_building";
}

public sealed record RemoveBuildingOperation(string Id) : OwnerAuthoringOperation
{
    public override string OperationKind => "remove_building";
}

/// <summary>
/// A convenience operation for a plant placement. Its resulting map object
/// uses <paramref name="PlantKind"/> as its manifest kind.
/// </summary>
public sealed record PlacePlantOperation(string Id, string PlantKind, GridPoint Position) : OwnerAuthoringOperation
{
    public override string OperationKind => "place_plant";
}

public sealed record RemovePlantOperation(string Id) : OwnerAuthoringOperation
{
    public override string OperationKind => "remove_plant";
}

public sealed record CreateFounderDraftOperation(
    string Id,
    string DisplayName,
    GridPoint Position) : OwnerAuthoringOperation
{
    public override string OperationKind => "create_founder_draft";
}

public sealed record RemoveFounderDraftOperation(string Id) : OwnerAuthoringOperation
{
    public override string OperationKind => "remove_founder_draft";
}

public sealed record SetWeatherOperation(string Weather) : OwnerAuthoringOperation
{
    public override string OperationKind => "set_weather";
}

public sealed record SetSeasonOperation(string Season) : OwnerAuthoringOperation
{
    public override string OperationKind => "set_season";
}

public sealed record SetWeatherSeasonOperation(string Weather, string Season) : OwnerAuthoringOperation
{
    public override string OperationKind => "set_weather_season";
}

public sealed record AddApprovedAssetReferenceOperation(
    string AssetId,
    string AssetDigest) : OwnerAuthoringOperation
{
    public override string OperationKind => "add_approved_asset_reference";
}

public sealed record RemoveApprovedAssetReferenceOperation(string AssetId) : OwnerAuthoringOperation
{
    public override string OperationKind => "remove_approved_asset_reference";
}

/// <summary>
/// All operations in a batch are validated against a private candidate before
/// any live Phase 2 state changes.
/// </summary>
public sealed record OwnerAuthoringBatch(
    string BatchId,
    IReadOnlyList<OwnerAuthoringOperation> Operations,
    string IssuerId = "system");

/// <summary>
/// The outcome of a paused authoring batch. A rejected receipt never represents
/// a partial live mutation or an appended global event.
/// </summary>
public sealed record OwnerAuthoringBatchReceipt(
    string BatchId,
    bool Applied,
    string? Failure,
    long Revision,
    long TopologyRevision,
    string CurrentMapManifestDigest);

/// <summary>
/// A detached state projection suitable for an observation adapter. The
/// original fixture world remains available for its protected actor and tick
/// state, while <see cref="CurrentMap"/> is the separately versioned authoring
/// topology.
/// </summary>
public sealed record OwnerWorldSnapshot(
    HarnessWorld World,
    SeededMap CurrentMap,
    string InitialMapManifestDigest,
    string CurrentMapManifestDigest,
    long TopologyRevision,
    bool IsPaused,
    long RunEpoch,
    long Revision,
    OwnerClimate Climate,
    IReadOnlyList<OwnerFounderDraft> FounderDrafts,
    IReadOnlyList<OwnerApprovedAssetReference> ApprovedAssetReferences,
    IReadOnlyList<OwnerQueuedInstruction> Instructions,
    long LatestGlobalEventId)
{
    /// <summary>
    /// The bounded Phase 3 cognition projection. It is optional so a legacy
    /// Phase 2 save can still be inspected before its first cognition tick.
    /// </summary>
    public CognitionRuntimeSnapshot? Cognition { get; init; }
}

/// <summary>
/// An atomic reconnect-style observation: a detached state projection and the
/// requested ordered suffix of the composite global stream.
/// </summary>
public sealed record OwnerWorldCapture(
    OwnerWorldSnapshot Snapshot,
    long AfterEventId,
    IReadOnlyList<OwnerWorldEvent> Events);

/// <summary>
/// One successfully applied authoring batch retained for idempotent retries.
/// The receipt revision supplies the authoritative application order when a
/// runtime is restored.
/// </summary>
public sealed record OwnerAppliedAuthoringBatchState(
    string BatchId,
    string IssuerId,
    IReadOnlyList<OwnerAuthoringOperation> Operations,
    OwnerAuthoringBatchReceipt Receipt);

/// <summary>
/// Complete durable state of the Phase 2 composite runtime. It is deliberately
/// separate from the owner-authority key registry: this document is simulation
/// state and contains no credentials or anti-replay material.
/// </summary>
public sealed record OwnerWorldRuntimeState(
    int SchemaVersion,
    HarnessWorld World,
    SeededMap CurrentMap,
    bool IsPaused,
    long RunEpoch,
    long Revision,
    long TopologyRevision,
    OwnerClimate Climate,
    IReadOnlyList<OwnerFounderDraft> FounderDrafts,
    IReadOnlyList<OwnerApprovedAssetReference> ApprovedAssetReferences,
    IReadOnlyList<OwnerQueuedInstruction> Instructions,
    IReadOnlyList<OwnerInstructionReceipt> InstructionReceipts,
    IReadOnlyList<OwnerAppliedAuthoringBatchState> AppliedAuthoringBatches,
    IReadOnlyList<OwnerWorldEvent> GlobalEvents,
    long NextGlobalEventId,
    long NextInstructionSequence,
    CognitionRuntimeState? Cognition = null,
    int ConsecutiveProviderFailures = 0);

/// <summary>
/// The result of one Phase 3 cognition/action boundary. The world state is
/// still read through <see cref="OwnerWorldRuntime.Capture"/>; this result
/// is useful to a scheduler and tests that need to distinguish a local
/// fallback from a hosted decision.
/// </summary>
public sealed record OwnerCognitionAdvanceResult(
    bool Advanced,
    bool PausedForProviderOutage,
    string Outcome,
    string? CandidateId,
    CognitionAdmissionResult? Cognition,
    IReadOnlyList<MovementEvent> MovementEvents);

/// <summary>
/// The Phase 2/3 composition root around the existing deterministic harness.
/// It intentionally does not replace <see cref="LiveSeededWorldRuntime"/>.
/// The Phase 1 harness remains the protected tick fixture; this runtime adds a
/// global event stream, pause/run-epoch controls, instruction ingress, paused
/// authoring state, and the bounded cognition/action boundary.
/// </summary>
public sealed class OwnerWorldRuntime
{
    /// <summary>
    /// The version of <see cref="OwnerWorldRuntimeState"/> understood by
    /// this runtime. Saved worlds are rejected rather than guessed across a
    /// schema boundary.
    /// </summary>
    public const int StateSchemaVersion = 1;

    private static readonly HashSet<string> ValidSeasons = new(StringComparer.Ordinal)
    {
        "spring",
        "summer",
        "autumn",
        "winter",
    };

    private readonly object sync = new();
    private readonly IOwnerApprovedAssetReferencePolicy approvedAssetReferencePolicy;
    private readonly IDecisionProvider decisionProvider;
    private readonly Dictionary<string, OwnerQueuedInstruction> instructionsByIdempotency =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnerInstructionReceipt> instructionReceipts =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, AppliedAuthoringBatch> appliedAuthoringBatches =
        new(StringComparer.Ordinal);
    private readonly List<OwnerWorldEvent> globalEvents = [];
    private HarnessWorld world;
    private SeededMap currentMap;
    private OwnerClimate climate = new("clear", "spring");
    private IReadOnlyList<OwnerFounderDraft> founderDrafts = [];
    private IReadOnlyList<OwnerApprovedAssetReference> approvedAssetReferences = [];
    private bool isPaused;
    private long runEpoch;
    private long revision;
    private long topologyRevision;
    private long nextGlobalEventId = 1;
    private long nextInstructionSequence = 1;
    private CognitionRuntime cognition;
    private int consecutiveProviderFailures;

    // Bound legacy save telemetry; an outage must not pause the whole world.
    private const int MaximumRecordedProviderFailures = 3;

    /// <summary>
    /// Creates a Phase 2 runtime. Asset references are denied unless the host
    /// supplies a policy backed by its approved server-side catalog.
    /// </summary>
    public OwnerWorldRuntime(
        string worldSeed,
        IOwnerApprovedAssetReferencePolicy? approvedAssetReferencePolicy = null,
        IDecisionProvider? decisionProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldSeed);
        this.approvedAssetReferencePolicy = approvedAssetReferencePolicy ??
            DenyAllApprovedAssetReferencePolicy.Instance;
        this.decisionProvider = decisionProvider ?? new DeterministicDecisionProvider();
        world = ScriptedHarness.CreateGenesis(worldSeed);
        currentMap = CloneMap(world.Map);
        cognition = new CognitionRuntime(world.Actor.Id, this.decisionProvider);
    }

    /// <summary>
    /// Creates a detached, serializable save document at one atomic runtime
    /// boundary. Callers may serialize the returned record without retaining a
    /// mutable reference to the live world.
    /// </summary>
    public OwnerWorldRuntimeState ExportState()
    {
        lock (sync)
        {
            return new OwnerWorldRuntimeState(
                StateSchemaVersion,
                CloneWorld(world),
                CloneMap(currentMap),
                isPaused,
                runEpoch,
                revision,
                topologyRevision,
                climate with { },
                founderDrafts.Select(draft => draft with { }).ToArray(),
                approvedAssetReferences.Select(reference => reference with { }).ToArray(),
                instructionsByIdempotency.Values
                    .OrderBy(instruction => instruction.SubmissionSequence)
                    .ThenBy(instruction => instruction.InstructionId, StringComparer.Ordinal)
                    .Select(instruction => instruction with { })
                    .ToArray(),
                instructionReceipts.Values
                    .OrderBy(receipt => receipt.InstructionId, StringComparer.Ordinal)
                    .Select(receipt => receipt with { })
                    .ToArray(),
                appliedAuthoringBatches
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new OwnerAppliedAuthoringBatchState(
                        pair.Key,
                        pair.Value.IssuerId,
                        CloneOperations(pair.Value.Operations),
                        pair.Value.Receipt with { }))
                    .ToArray(),
                globalEvents.Select(CloneEvent).ToArray(),
                nextGlobalEventId,
                nextInstructionSequence,
                cognition.ExportState(),
                consecutiveProviderFailures);
        }
    }

    /// <summary>
    /// Restores a runtime only after proving that the fixture replay, global
    /// event log, idempotency records, and authored projection agree. An
    /// optional expected seed binds a save to a host's configured world.
    /// </summary>
    public static OwnerWorldRuntime Restore(
        OwnerWorldRuntimeState state,
        string? expectedWorldSeed = null,
        IOwnerApprovedAssetReferencePolicy? approvedAssetReferencePolicy = null,
        IDecisionProvider? decisionProvider = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != StateSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported Phase 2 runtime state schema '{state.SchemaVersion.ToString(CultureInfo.InvariantCulture)}'.");
        }

        if (state.World?.Identity is null || string.IsNullOrWhiteSpace(state.World.Identity.WorldSeed))
        {
            throw new InvalidDataException("The Phase 2 runtime state has no valid fixture world identity.");
        }

        if (expectedWorldSeed is not null &&
            !string.Equals(expectedWorldSeed, state.World.Identity.WorldSeed, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Phase 2 runtime state belongs to a different configured world seed.");
        }

        var runtime = new OwnerWorldRuntime(
            state.World.Identity.WorldSeed,
            approvedAssetReferencePolicy,
            decisionProvider);
        runtime.ImportState(state);
        return runtime;
    }

    /// <summary>
    /// Stops subsequent fixture ticks at an atomic boundary. Repeating pause is
    /// idempotent and does not create a second durable control event.
    /// </summary>
    public bool Pause(string issuerId = "system")
    {
        var normalizedIssuerId = NormalizeRequiredText(issuerId, "Pause issuer ID");
        lock (sync)
        {
            if (isPaused)
            {
                return false;
            }

            isPaused = true;
            _ = cognition.Pause(world.Identity.WorldTick);
            AppendGlobalEvent("paused", $"issuer:{normalizedIssuerId}");
            return true;
        }
    }

    /// <summary>
    /// Alias that reads naturally at an action boundary.
    /// </summary>
    public bool RequestPause(string issuerId = "system") => Pause(issuerId);

    /// <summary>
    /// Resumes a paused world and creates a new run epoch without advancing
    /// world time. Repeating resume while already running is idempotent.
    /// </summary>
    public bool Resume(string issuerId = "system")
    {
        var normalizedIssuerId = NormalizeRequiredText(issuerId, "Resume issuer ID");
        lock (sync)
        {
            if (!isPaused)
            {
                return false;
            }

            isPaused = false;
            runEpoch = checked(runEpoch + 1);
            _ = cognition.Resume(world.Identity.WorldTick);
            AppendGlobalEvent("resumed", $"epoch:{runEpoch}:issuer:{normalizedIssuerId}");
            return true;
        }
    }

    /// <summary>
    /// Advances exactly one cognition/action boundary of the protected fixture.
    /// The current authoring topology is intentionally separate, so an
    /// experimental map edit cannot mutate its protected actor fields or
    /// invalidate its deterministic action path.
    /// </summary>
    public bool TryAdvanceOneAction() =>
        AdvanceOneActionAsync().AsTask().GetAwaiter().GetResult().Advanced;

    /// <summary>
    /// Runs one bounded cognition request and commits exactly one authoritative
    /// action. The provider chooses only from candidates generated here; route
    /// stepping, needs, resources, and event commits remain local kernel work.
    /// </summary>
    public async ValueTask<OwnerCognitionAdvanceResult> AdvanceOneActionAsync(
        CancellationToken cancellationToken = default)
    {
        InhabitantObservation observation;
        lock (sync)
        {
            if (isPaused)
            {
                return NotAdvanced("paused");
            }

            observation = CreateCognitionObservation();
        }

        CognitionAdmissionResult decision;
        try
        {
            decision = await cognition.RequestAndDecideAsync(observation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            decision = new CognitionAdmissionResult(false, false, $"cognition_host_failure:{exception.GetType().Name}", null);
        }

        lock (sync)
        {
            if (isPaused || decision.Intention is null)
            {
                return new OwnerCognitionAdvanceResult(
                    false,
                    false,
                    isPaused ? "paused" : decision.Outcome,
                    null,
                    decision,
                    []);
            }

            var candidate = observation.Candidates.Single(candidate =>
                string.Equals(candidate.Id, decision.Intention.CandidateId, StringComparison.Ordinal));
            var movementEvents = new List<MovementEvent>();
            world = ExecuteCognitionCandidate(world, candidate, movementEvents);
            AppendGlobalEvent("fixture_action_committed", world.Events[^1].Detail ?? string.Empty);

            if (decision.FellBack && IsProviderFailure(decision.Outcome))
            {
                consecutiveProviderFailures = Math.Min(MaximumRecordedProviderFailures, consecutiveProviderFailures + 1);
            }
            else if (!decision.FellBack)
            {
                consecutiveProviderFailures = 0;
            }

            return new OwnerCognitionAdvanceResult(
                true,
                false,
                decision.Outcome,
                candidate.Id,
                decision,
                movementEvents);
        }
    }

    /// <summary>
    /// Queues a server-minted instruction. A repeat with the same idempotency
    /// key and the same request returns the original receipt without mutation.
    /// A key cannot be reused for a different request.
    /// </summary>
    public OwnerInstructionReceipt SubmitInstruction(OwnerInstructionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInstructionRequest(request);

        lock (sync)
        {
            // Founder drafts are authoring-time proposals, not active people.
            // This fixture has one authoritative inhabitant, so rejecting an
            // unknown target keeps a signed owner request from becoming a
            // durable but meaningless command record.
            if (!string.Equals(request.TargetInhabitantId.Trim(), world.Actor.Id, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"No active inhabitant with ID '{request.TargetInhabitantId.Trim()}' exists.",
                    nameof(request));
            }

            if (instructionsByIdempotency.TryGetValue(request.IdempotencyKey, out var existing))
            {
                if (!Matches(existing, request))
                {
                    throw new InvalidOperationException(
                        "An idempotency key cannot be reused for a different instruction request.");
                }

                return instructionReceipts[request.IdempotencyKey];
            }

            var sequence = nextInstructionSequence++;
            var instructionId = CreateInstructionId(sequence);
            var queued = new OwnerQueuedInstruction(
                instructionId,
                request.IdempotencyKey,
                request.IssuerId.Trim(),
                request.TargetInhabitantId.Trim(),
                request.Kind,
                request.Text.Trim(),
                world.Identity.WorldTick,
                runEpoch,
                sequence,
                OwnerInstructionState.Queued);
            instructionsByIdempotency.Add(queued.IdempotencyKey, queued);
            AppendGlobalEvent("instruction_queued", $"{queued.InstructionId}:{ToWireValue(queued.Kind)}");
            var receipt = new OwnerInstructionReceipt(
                queued.InstructionId,
                queued.IdempotencyKey,
                queued.SubmittedTick,
                queued.RunEpoch,
                revision);
            instructionReceipts.Add(queued.IdempotencyKey, receipt);
            return receipt;
        }
    }

    /// <summary>
    /// Applies a complete authoring batch only while the world is paused. All
    /// validation happens against a private candidate; a failure returns a
    /// rejected receipt and leaves the live state/event log untouched.
    /// </summary>
    public OwnerAuthoringBatchReceipt ApplyAuthoringBatch(OwnerAuthoringBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (string.IsNullOrWhiteSpace(batch.BatchId))
        {
            return RejectedBatch(batch, "An authoring batch ID is required.");
        }

        string normalizedIssuerId;
        try
        {
            normalizedIssuerId = NormalizeRequiredText(batch.IssuerId, "Authoring issuer ID");
        }
        catch (ArgumentException exception)
        {
            return RejectedBatch(batch, exception.Message);
        }

        var normalizedBatchId = batch.BatchId.Trim();
        var normalizedOperations = batch.Operations?.ToArray();

        lock (sync)
        {
            if (appliedAuthoringBatches.TryGetValue(normalizedBatchId, out var applied))
            {
                return Matches(applied, normalizedIssuerId, normalizedOperations)
                    ? applied.Receipt
                    : RejectedBatch(batch, "An authoring batch ID cannot be reused for a different request.");
            }

            if (!isPaused)
            {
                return RejectedBatch(batch, "Paused authoring requires the world to be paused.");
            }

            if (normalizedOperations is null || normalizedOperations.Length == 0)
            {
                return RejectedBatch(batch, "An authoring batch must contain at least one operation.");
            }

            var candidate = new AuthoringCandidate(
                CloneMap(currentMap),
                climate,
                founderDrafts.ToArray(),
                approvedAssetReferences.ToArray());

            try
            {
                foreach (var operation in normalizedOperations)
                {
                    if (operation is null)
                    {
                        throw new InvalidOperationException("An authoring batch cannot contain a null operation.");
                    }

                    ApplyOperation(candidate, operation);
                }

                ValidateCandidate(candidate);
            }
            catch (ArgumentException exception)
            {
                return RejectedBatch(batch, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return RejectedBatch(batch, exception.Message);
            }

            var topologyChanged = !string.Equals(
                currentMap.ManifestDigest,
                candidate.Map.ManifestDigest,
                StringComparison.Ordinal);
            currentMap = candidate.Map;
            climate = candidate.Climate;
            founderDrafts = candidate.FounderDrafts
                .OrderBy(draft => draft.Id, StringComparer.Ordinal)
                .ToArray();
            approvedAssetReferences = candidate.ApprovedAssetReferences
                .OrderBy(reference => reference.AssetId, StringComparer.Ordinal)
                .ToArray();
            if (topologyChanged)
            {
                topologyRevision = checked(topologyRevision + 1);
            }

            AppendGlobalEvent(
                "authoring_batch_applied",
                $"{normalizedBatchId}:issuer={normalizedIssuerId}:operations={normalizedOperations.Length.ToString(CultureInfo.InvariantCulture)}");
            var receipt = new OwnerAuthoringBatchReceipt(
                normalizedBatchId,
                true,
                null,
                revision,
                topologyRevision,
                currentMap.ManifestDigest);
            appliedAuthoringBatches.Add(
                normalizedBatchId,
                new AppliedAuthoringBatch(
                    normalizedIssuerId,
                    normalizedOperations,
                    receipt));
            return receipt;
        }
    }

    /// <summary>
    /// Captures one detached state baseline and one ordered global event suffix
    /// under the same lock. A client cannot combine a later map revision with an
    /// earlier control/instruction history through this API.
    /// </summary>
    public OwnerWorldCapture Capture(long afterEventId = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterEventId);

        lock (sync)
        {
            var events = globalEvents
                .Where(worldEvent => worldEvent.EventId > afterEventId)
                .Select(CloneEvent)
                .ToArray();
            return new OwnerWorldCapture(CreateSnapshot(), afterEventId, events);
        }
    }

    private void ImportState(OwnerWorldRuntimeState state)
    {
        var document = RequireStateDocument(state);
        ValidateCounters(state);

        var restoredWorld = RestoreFixtureWorld(document.World);
        var restoredEvents = document.GlobalEvents
            .Select(CloneEvent)
            .ToArray();
        ValidateGlobalEventShape(state, restoredEvents);

        var restoredInstructions = RestoreInstructions(
            document.Instructions,
            document.InstructionReceipts,
            restoredWorld,
            state);
        var restoredAuthoring = RestoreAuthoring(
            document.AppliedAuthoringBatches,
            restoredWorld,
            restoredEvents,
            state,
            approvedAssetReferencePolicy);

        ValidateGlobalEventSemantics(
            state,
            restoredWorld,
            restoredEvents,
            restoredInstructions,
            restoredAuthoring);

        if (!EquivalentMap(restoredAuthoring.CurrentMap, document.CurrentMap) ||
            restoredAuthoring.Climate != document.Climate ||
            !restoredAuthoring.FounderDrafts.SequenceEqual(document.FounderDrafts) ||
            !restoredAuthoring.ApprovedAssetReferences.SequenceEqual(document.ApprovedAssetReferences) ||
            restoredAuthoring.TopologyRevision != state.TopologyRevision)
        {
            throw new InvalidDataException("The saved authored world projection does not match its applied batches.");
        }

        if (state.ConsecutiveProviderFailures < 0 ||
            state.ConsecutiveProviderFailures > MaximumRecordedProviderFailures)
        {
            throw new InvalidDataException("The saved provider failure counter is invalid.");
        }

        var restoredCognition = state.Cognition is null
            ? new CognitionRuntime(restoredWorld.Actor.Id, decisionProvider)
            : CognitionRuntime.Restore(state.Cognition, decisionProvider);
        if (state.Cognition is null && state.IsPaused)
        {
            _ = restoredCognition.Pause(restoredWorld.Identity.WorldTick);
        }

        if (!string.Equals(restoredCognition.InhabitantId, restoredWorld.Actor.Id, StringComparison.Ordinal) ||
            restoredCognition.Capture().IsPaused != state.IsPaused)
        {
            throw new InvalidDataException("The saved cognition state does not match the world pause or actor boundary.");
        }

        lock (sync)
        {
            world = restoredWorld;
            currentMap = CloneMap(restoredAuthoring.CurrentMap);
            climate = restoredAuthoring.Climate with { };
            founderDrafts = restoredAuthoring.FounderDrafts
                .Select(draft => draft with { })
                .ToArray();
            approvedAssetReferences = restoredAuthoring.ApprovedAssetReferences
                .Select(reference => reference with { })
                .ToArray();
            isPaused = state.IsPaused;
            runEpoch = state.RunEpoch;
            revision = state.Revision;
            topologyRevision = state.TopologyRevision;
            nextGlobalEventId = state.NextGlobalEventId;
            nextInstructionSequence = state.NextInstructionSequence;
            cognition = restoredCognition;
            consecutiveProviderFailures = state.ConsecutiveProviderFailures;

            instructionsByIdempotency.Clear();
            foreach (var pair in restoredInstructions.ByIdempotency)
            {
                instructionsByIdempotency.Add(pair.Key, pair.Value with { });
            }

            instructionReceipts.Clear();
            foreach (var pair in restoredInstructions.Receipts)
            {
                instructionReceipts.Add(pair.Key, pair.Value with { });
            }

            appliedAuthoringBatches.Clear();
            foreach (var pair in restoredAuthoring.Batches)
            {
                appliedAuthoringBatches.Add(
                    pair.Key,
                    new AppliedAuthoringBatch(
                        pair.Value.IssuerId,
                        CloneOperations(pair.Value.Operations),
                        pair.Value.Receipt with { }));
            }

            globalEvents.Clear();
            globalEvents.AddRange(restoredEvents);
        }
    }

    private static RequiredStateDocument RequireStateDocument(OwnerWorldRuntimeState state)
    {
        var storedWorld = state.World ??
            throw new InvalidDataException("The Phase 2 runtime state has no fixture world.");
        var storedMap = state.CurrentMap ??
            throw new InvalidDataException("The Phase 2 runtime state has no current map.");
        var storedClimate = state.Climate ??
            throw new InvalidDataException("The Phase 2 runtime state has no climate projection.");
        var storedDrafts = state.FounderDrafts ??
            throw new InvalidDataException("The Phase 2 runtime state has no founder draft collection.");
        var storedAssets = state.ApprovedAssetReferences ??
            throw new InvalidDataException("The Phase 2 runtime state has no asset reference collection.");
        var storedInstructions = state.Instructions ??
            throw new InvalidDataException("The Phase 2 runtime state has no instruction collection.");
        var storedReceipts = state.InstructionReceipts ??
            throw new InvalidDataException("The Phase 2 runtime state has no instruction receipt collection.");
        var storedBatches = state.AppliedAuthoringBatches ??
            throw new InvalidDataException("The Phase 2 runtime state has no authoring batch collection.");
        var storedEvents = state.GlobalEvents ??
            throw new InvalidDataException("The Phase 2 runtime state has no global event collection.");

        RequireMapCollections(storedMap, "current map");
        if (storedClimate.Weather is null || storedClimate.Season is null ||
            storedDrafts.Any(draft => draft is null) ||
            storedAssets.Any(reference => reference is null) ||
            storedInstructions.Any(instruction => instruction is null) ||
            storedReceipts.Any(receipt => receipt is null) ||
            storedBatches.Any(batch => batch is null) ||
            storedEvents.Any(worldEvent => worldEvent is null))
        {
            throw new InvalidDataException("The Phase 2 runtime state contains a null required record.");
        }

        return new RequiredStateDocument(
            storedWorld,
            storedMap,
            storedClimate,
            storedDrafts,
            storedAssets,
            storedInstructions,
            storedReceipts,
            storedBatches,
            storedEvents);
    }

    private static void ValidateCounters(OwnerWorldRuntimeState state)
    {
        if (state.RunEpoch < 0 ||
            state.Revision < 0 ||
            state.TopologyRevision < 0 ||
            state.NextGlobalEventId <= 0 ||
            state.NextInstructionSequence <= 0 ||
            state.TopologyRevision > state.Revision ||
            state.RunEpoch > state.Revision)
        {
            throw new InvalidDataException("The Phase 2 runtime counters are invalid.");
        }
    }

    private static HarnessWorld RestoreFixtureWorld(HarnessWorld storedWorld)
    {
        if (storedWorld.Identity is null ||
            storedWorld.Map is null ||
            storedWorld.Actor is null ||
            storedWorld.Resources is null ||
            storedWorld.Events is null ||
            storedWorld.Resources.Any(resource => resource is null) ||
            storedWorld.Events.Any(worldEvent => worldEvent is null))
        {
            throw new InvalidDataException("The saved fixture world is incomplete.");
        }

        RequireMapCollections(storedWorld.Map, "fixture map");
        try
        {
            var replayed = ScriptedHarness.ReplayFromGenesis(storedWorld.Identity, storedWorld.Events);
            if (!EquivalentWorld(replayed, storedWorld))
            {
                throw new InvalidDataException("The saved fixture world does not match deterministic replay.");
            }

            return CloneWorld(replayed);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException("The saved fixture world cannot be deterministically restored.", exception);
        }
    }

    private static void ValidateGlobalEventShape(
        OwnerWorldRuntimeState state,
        OwnerWorldEvent[] events)
    {
        if (state.Revision != events.Length ||
            state.NextGlobalEventId != checked(events.Length + 1L))
        {
            throw new InvalidDataException("The global event counters do not match the saved event stream.");
        }

        for (var index = 0; index < events.Length; index++)
        {
            var expectedId = index + 1L;
            var worldEvent = events[index];
            if (worldEvent.EventId != expectedId ||
                worldEvent.Revision != expectedId ||
                worldEvent.WorldTick < 0 ||
                string.IsNullOrWhiteSpace(worldEvent.Kind) ||
                worldEvent.Detail is null)
            {
                throw new InvalidDataException("The global event stream is not a contiguous canonical sequence.");
            }
        }
    }

    private static RestoredInstructionState RestoreInstructions(
        IReadOnlyList<OwnerQueuedInstruction> storedInstructions,
        IReadOnlyList<OwnerInstructionReceipt> storedReceipts,
        HarnessWorld restoredWorld,
        OwnerWorldRuntimeState state)
    {
        var byIdempotency = new Dictionary<string, OwnerQueuedInstruction>(StringComparer.Ordinal);
        var sequences = new HashSet<long>();

        foreach (var stored in storedInstructions)
        {
            var idempotencyKey = stored.IdempotencyKey;
            _ = RequireStoredText(idempotencyKey, "Instruction idempotency key");
            var issuerId = RequireCanonicalStoredText(stored.IssuerId, "Instruction issuer ID");
            var targetInhabitantId = RequireCanonicalStoredText(
                stored.TargetInhabitantId,
                "Instruction target inhabitant ID");
            var text = RequireCanonicalStoredText(stored.Text, "Instruction text");
            if (!Enum.IsDefined(stored.Kind) ||
                stored.State != OwnerInstructionState.Queued ||
                stored.SubmissionSequence <= 0 ||
                stored.SubmittedTick < 0 ||
                stored.SubmittedTick > restoredWorld.Identity.WorldTick ||
                stored.RunEpoch < 0 ||
                stored.RunEpoch > state.RunEpoch ||
                !string.Equals(targetInhabitantId, restoredWorld.Actor.Id, StringComparison.Ordinal) ||
                !string.Equals(
                    stored.InstructionId,
                    CreateInstructionId(stored.SubmissionSequence),
                    StringComparison.Ordinal) ||
                !sequences.Add(stored.SubmissionSequence))
            {
                throw new InvalidDataException("The saved instruction queue is inconsistent.");
            }

            var normalized = stored with
            {
                IssuerId = issuerId,
                TargetInhabitantId = targetInhabitantId,
                Text = text,
            };
            if (!byIdempotency.TryAdd(idempotencyKey, normalized))
            {
                throw new InvalidDataException("The saved instruction queue has duplicate idempotency keys.");
            }
        }

        if (!sequences.OrderBy(sequence => sequence).SequenceEqual(
                Enumerable.Range(1, sequences.Count).Select(value => (long)value)) ||
            state.NextInstructionSequence != checked(byIdempotency.Count + 1L))
        {
            throw new InvalidDataException("The saved instruction sequence counter is inconsistent.");
        }

        var receipts = new Dictionary<string, OwnerInstructionReceipt>(StringComparer.Ordinal);
        var receiptsByRevision = new Dictionary<long, OwnerInstructionReceipt>();
        foreach (var stored in storedReceipts)
        {
            var idempotencyKey = stored.IdempotencyKey;
            _ = RequireStoredText(idempotencyKey, "Instruction receipt idempotency key");
            if (!byIdempotency.TryGetValue(idempotencyKey, out var instruction) ||
                stored.Revision <= 0 ||
                stored.Revision > state.Revision ||
                !string.Equals(stored.InstructionId, instruction.InstructionId, StringComparison.Ordinal) ||
                stored.SubmittedTick != instruction.SubmittedTick ||
                stored.RunEpoch != instruction.RunEpoch ||
                !receipts.TryAdd(idempotencyKey, stored with { }) ||
                !receiptsByRevision.TryAdd(stored.Revision, stored with { }))
            {
                throw new InvalidDataException("The saved instruction receipts are inconsistent.");
            }
        }

        if (receipts.Count != byIdempotency.Count ||
            byIdempotency.Keys.Any(key => !receipts.ContainsKey(key)))
        {
            throw new InvalidDataException("Every saved instruction must retain exactly one idempotency receipt.");
        }

        return new RestoredInstructionState(byIdempotency, receipts, receiptsByRevision);
    }

    private static RestoredAuthoringState RestoreAuthoring(
        IReadOnlyList<OwnerAppliedAuthoringBatchState> storedBatches,
        HarnessWorld restoredWorld,
        OwnerWorldEvent[] events,
        OwnerWorldRuntimeState state,
        IOwnerApprovedAssetReferencePolicy approvedAssetReferencePolicy)
    {
        var batches = new Dictionary<string, RestoredAuthoringBatch>(StringComparer.Ordinal);
        var batchesByRevision = new Dictionary<long, RestoredAuthoringBatch>();
        foreach (var stored in storedBatches)
        {
            if (stored.Receipt is null || stored.Operations is null)
            {
                throw new InvalidDataException("The saved authoring batch is incomplete.");
            }

            var batchId = RequireCanonicalStoredText(stored.BatchId, "Authoring batch ID");
            var issuerId = RequireCanonicalStoredText(stored.IssuerId, "Authoring issuer ID");
            var operations = CloneOperations(stored.Operations);
            if (operations.Length == 0 ||
                !stored.Receipt.Applied ||
                stored.Receipt.Failure is not null ||
                stored.Receipt.Revision <= 0 ||
                stored.Receipt.Revision > state.Revision ||
                stored.Receipt.TopologyRevision < 0 ||
                !string.Equals(stored.Receipt.BatchId, batchId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The saved authoring batch receipt is inconsistent.");
            }

            var batch = new RestoredAuthoringBatch(
                batchId,
                issuerId,
                operations,
                stored.Receipt with { });
            if (!batches.TryAdd(batchId, batch) ||
                !batchesByRevision.TryAdd(batch.Receipt.Revision, batch))
            {
                throw new InvalidDataException("The saved authoring idempotency records are not unique.");
            }
        }

        var replay = new OwnerWorldRuntime(
            restoredWorld.Identity.WorldSeed,
            approvedAssetReferencePolicy)
        {
            world = CloneWorld(restoredWorld),
            currentMap = CloneMap(restoredWorld.Map),
        };
        foreach (var batch in batchesByRevision.Values.OrderBy(batch => batch.Receipt.Revision))
        {
            var eventIndex = checked((int)(batch.Receipt.Revision - 1));
            if (eventIndex < 0 || eventIndex >= events.Length)
            {
                throw new InvalidDataException("The saved authoring receipt has no matching global event.");
            }

            replay.revision = batch.Receipt.Revision - 1;
            var candidate = new AuthoringCandidate(
                CloneMap(replay.currentMap),
                replay.climate,
                replay.founderDrafts,
                replay.approvedAssetReferences);
            try
            {
                foreach (var operation in batch.Operations)
                {
                    replay.ApplyOperation(candidate, operation);
                }

                replay.ValidateCandidate(candidate);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
            {
                throw new InvalidDataException("The saved authoring operations cannot be replayed.", exception);
            }

            var topologyChanged = !string.Equals(
                replay.currentMap.ManifestDigest,
                candidate.Map.ManifestDigest,
                StringComparison.Ordinal);
            replay.currentMap = candidate.Map;
            replay.climate = candidate.Climate;
            replay.founderDrafts = candidate.FounderDrafts
                .OrderBy(draft => draft.Id, StringComparer.Ordinal)
                .ToArray();
            replay.approvedAssetReferences = candidate.ApprovedAssetReferences
                .OrderBy(reference => reference.AssetId, StringComparer.Ordinal)
                .ToArray();
            if (topologyChanged)
            {
                replay.topologyRevision = checked(replay.topologyRevision + 1);
            }

            var expectedReceipt = new OwnerAuthoringBatchReceipt(
                batch.BatchId,
                true,
                null,
                batch.Receipt.Revision,
                replay.topologyRevision,
                replay.currentMap.ManifestDigest);
            if (batch.Receipt != expectedReceipt ||
                !string.Equals(
                    events[eventIndex].Detail,
                    $"{batch.BatchId}:issuer={batch.IssuerId}:operations={batch.Operations.Count.ToString(CultureInfo.InvariantCulture)}",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The saved authoring receipt does not match its replayed result.");
            }
        }

        return new RestoredAuthoringState(
            CloneMap(replay.currentMap),
            replay.climate with { },
            replay.founderDrafts.Select(draft => draft with { }).ToArray(),
            replay.approvedAssetReferences.Select(reference => reference with { }).ToArray(),
            replay.topologyRevision,
            batches.ToDictionary(
                pair => pair.Key,
                pair => new AppliedAuthoringBatch(
                    pair.Value.IssuerId,
                    CloneOperations(pair.Value.Operations),
                    pair.Value.Receipt with { }),
                StringComparer.Ordinal),
            batchesByRevision);
    }

    private static void ValidateGlobalEventSemantics(
        OwnerWorldRuntimeState state,
        HarnessWorld restoredWorld,
        IReadOnlyList<OwnerWorldEvent> events,
        RestoredInstructionState instructions,
        RestoredAuthoringState authoring)
    {
        var fixtureEventIndex = 0;
        var previousTick = 0L;
        var replayPaused = false;
        var replayRunEpoch = 0L;
        var seenInstructionRevisions = new HashSet<long>();
        var seenAuthoringRevisions = new HashSet<long>();

        foreach (var worldEvent in events)
        {
            if (worldEvent.WorldTick < previousTick ||
                worldEvent.WorldTick > restoredWorld.Identity.WorldTick)
            {
                throw new InvalidDataException("The saved global event stream has an invalid world-tick order.");
            }

            previousTick = worldEvent.WorldTick;
            switch (worldEvent.Kind)
            {
                case "fixture_action_committed":
                    if (replayPaused || fixtureEventIndex >= restoredWorld.Events.Count)
                    {
                        throw new InvalidDataException("A saved fixture event was committed outside a running fixture state.");
                    }

                    var expectedFixtureEvent = restoredWorld.Events[fixtureEventIndex++];
                    if (worldEvent.WorldTick != expectedFixtureEvent.WorldTick ||
                        !string.Equals(worldEvent.Detail, expectedFixtureEvent.Detail, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("A saved fixture event does not match deterministic replay.");
                    }

                    break;

                case "instruction_queued":
                    if (!instructions.ReceiptsByRevision.TryGetValue(worldEvent.Revision, out var receipt) ||
                        !instructions.ByIdempotency.TryGetValue(receipt.IdempotencyKey, out var instruction) ||
                        worldEvent.WorldTick != instruction.SubmittedTick ||
                        !string.Equals(
                            worldEvent.Detail,
                            $"{instruction.InstructionId}:{ToWireValue(instruction.Kind)}",
                            StringComparison.Ordinal) ||
                        !seenInstructionRevisions.Add(worldEvent.Revision))
                    {
                        throw new InvalidDataException("A saved instruction event does not match its receipt.");
                    }

                    break;

                case "authoring_batch_applied":
                    if (!replayPaused ||
                        !authoring.BatchesByRevision.TryGetValue(worldEvent.Revision, out var batch) ||
                        !string.Equals(
                            worldEvent.Detail,
                            $"{batch.BatchId}:issuer={batch.IssuerId}:operations={batch.Operations.Count.ToString(CultureInfo.InvariantCulture)}",
                            StringComparison.Ordinal) ||
                        !seenAuthoringRevisions.Add(worldEvent.Revision))
                    {
                        throw new InvalidDataException("A saved authoring event does not match its batch receipt.");
                    }

                    break;

                case "paused":
                    if (replayPaused || !IsValidIssuerDetail(worldEvent.Detail, "issuer:"))
                    {
                        throw new InvalidDataException("A saved pause event is invalid.");
                    }

                    replayPaused = true;
                    break;

                case "resumed":
                    var expectedPrefix = $"epoch:{checked(replayRunEpoch + 1).ToString(CultureInfo.InvariantCulture)}:issuer:";
                    if (!replayPaused || !IsValidIssuerDetail(worldEvent.Detail, expectedPrefix))
                    {
                        throw new InvalidDataException("A saved resume event is invalid.");
                    }

                    replayPaused = false;
                    replayRunEpoch = checked(replayRunEpoch + 1);
                    break;

                default:
                    throw new InvalidDataException($"The saved global event kind '{worldEvent.Kind}' is not supported.");
            }
        }

        if (fixtureEventIndex != restoredWorld.Events.Count ||
            replayPaused != state.IsPaused ||
            replayRunEpoch != state.RunEpoch ||
            seenInstructionRevisions.Count != instructions.ReceiptsByRevision.Count ||
            seenAuthoringRevisions.Count != authoring.BatchesByRevision.Count)
        {
            throw new InvalidDataException("The saved global event stream does not account for the retained runtime state.");
        }
    }

    private static bool IsValidIssuerDetail(string detail, string prefix)
    {
        if (!detail.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            _ = NormalizeRequiredText(detail[prefix.Length..], "Event issuer ID");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string RequireStoredText(string? value, string label)
    {
        try
        {
            return NormalizeRequiredText(value, label);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"The saved {label.ToLowerInvariant()} is invalid.", exception);
        }
    }

    private static string RequireCanonicalStoredText(string? value, string label)
    {
        var normalized = RequireStoredText(value, label);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The saved {label.ToLowerInvariant()} is not canonical.");
        }

        return normalized;
    }

    private static void RequireMapCollections(SeededMap map, string label)
    {
        if (map is null ||
            map.Tiles is null ||
            map.CampObjects is null ||
            map.Resources is null ||
            map.Tiles.Any(tile => tile is null) ||
            map.CampObjects.Any(mapObject => mapObject is null) ||
            map.Resources.Any(resource => resource is null))
        {
            throw new InvalidDataException($"The saved {label} is incomplete.");
        }
    }

    private static bool EquivalentWorld(HarnessWorld left, HarnessWorld right) =>
        left.Identity == right.Identity &&
        EquivalentMap(left.Map, right.Map) &&
        left.Actor == right.Actor &&
        left.Resources.SequenceEqual(right.Resources) &&
        left.Events.SequenceEqual(right.Events);

    private static bool EquivalentMap(SeededMap left, SeededMap right) =>
        left.Width == right.Width &&
        left.Height == right.Height &&
        left.GenerationAttempt == right.GenerationAttempt &&
        string.Equals(left.ManifestDigest, right.ManifestDigest, StringComparison.Ordinal) &&
        left.Tiles.SequenceEqual(right.Tiles) &&
        left.CampObjects.SequenceEqual(right.CampObjects) &&
        left.Resources.SequenceEqual(right.Resources);

    private static OwnerAuthoringOperation[] CloneOperations(
        IReadOnlyList<OwnerAuthoringOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        return operations.Select(CloneOperation).ToArray();
    }

    private static OwnerAuthoringOperation CloneOperation(OwnerAuthoringOperation? operation) => operation switch
    {
        SetTerrainOperation setTerrain => setTerrain with { },
        PlaceResourceOperation placeResource => placeResource with { },
        RemoveResourceOperation removeResource => removeResource with { },
        PlaceObjectOperation placeObject => placeObject with { },
        RemoveObjectOperation removeObject => removeObject with { },
        PlaceBuildingOperation placeBuilding => placeBuilding with { },
        RemoveBuildingOperation removeBuilding => removeBuilding with { },
        PlacePlantOperation placePlant => placePlant with { },
        RemovePlantOperation removePlant => removePlant with { },
        CreateFounderDraftOperation createFounder => createFounder with { },
        RemoveFounderDraftOperation removeFounder => removeFounder with { },
        SetWeatherOperation setWeather => setWeather with { },
        SetSeasonOperation setSeason => setSeason with { },
        SetWeatherSeasonOperation setWeatherSeason => setWeatherSeason with { },
        AddApprovedAssetReferenceOperation addAsset => addAsset with { },
        RemoveApprovedAssetReferenceOperation removeAsset => removeAsset with { },
        null => throw new InvalidDataException("A saved authoring batch contains a null operation."),
        _ => throw new InvalidDataException(
            $"A saved authoring batch contains unsupported operation '{operation.OperationKind}'."),
    };

    private static string CreateInstructionId(long sequence) =>
        $"instruction-{sequence.ToString("D10", CultureInfo.InvariantCulture)}";

    private InhabitantObservation CreateCognitionObservation()
    {
        var actor = world.Actor;
        var generation = cognition.Capture().DecisionGeneration + 1;
        var candidates = CreateCognitionCandidates(world);
        return new InhabitantObservation(
            actor.Id,
            world.Identity.WorldTick,
            runEpoch,
            generation,
            CognitionObservationDigest.Create(world, runEpoch, generation, candidates),
            actor.HungerBasisPoints,
            candidates);
    }

    private static List<CognitionCandidate> CreateCognitionCandidates(HarnessWorld current)
    {
        var candidates = new List<CognitionCandidate>();
        var food = current.Map.GetResource("berry-patch");
        var foodAvailable = current.GetResource(food.Id).State == ResourceState.Available;

        if (current.Actor.FoodItems > 0 && current.Actor.HungerBasisPoints < 8_500)
        {
            candidates.Add(new CognitionCandidate(
                "consume_food",
                "Eat one carried food item to restore hunger.",
                0));
        }

        if (foodAvailable && current.Actor.Position == food.Position)
        {
            candidates.Add(new CognitionCandidate(
                "harvest_food",
                "Collect available food at the current tile.",
                0,
                food.Id));
        }
        else if (foodAvailable && current.Actor.HungerBasisPoints < 7_000)
        {
            candidates.Add(new CognitionCandidate(
                "seek_food",
                "Travel to the available food source.",
                5,
                food.Id));
        }

        candidates.Add(new CognitionCandidate(
            "safe_idle",
            "Continue safely without starting a new task.",
            100));
        return candidates;
    }

    private static HarnessWorld ExecuteCognitionCandidate(
        HarnessWorld current,
        CognitionCandidate candidate,
        List<MovementEvent> movementEvents)
    {
        if (candidate.DestinationId is not null)
        {
            var destination = ResolveDestination(current.Map, candidate.DestinationId);
            if (current.Actor.Position != destination)
            {
                var route = DeterministicRouteFinder.Find(current.Map, current.Actor.Position, destination);
                if (route.Count < 2)
                {
                    throw new InvalidOperationException("A destination intention must have a next route step.");
                }

                var movement = DeterministicMovementResolver.Resolve(
                    current.Map,
                    [new MovementActor(current.Actor.Id, current.Actor.Position, 0)],
                    [new MovementIntent(current.Actor.Id, route[1])]);
                movementEvents.AddRange(movement.Events);
                return ScriptedHarness.ApplyMovement(current, movement.GetActor(current.Actor.Id).Position);
            }
        }

        return candidate.Id switch
        {
            "harvest_food" => ScriptedHarness.ApplyHarvest(current, "berry-patch"),
            "consume_food" => ScriptedHarness.ApplyConsume(current),
            "safe_idle" => ScriptedHarness.ApplyIdle(current),
            _ => throw new InvalidDataException($"Cognition candidate '{candidate.Id}' has no executor."),
        };
    }

    private static GridPoint ResolveDestination(SeededMap map, string destinationId)
    {
        var mapObject = map.CampObjects.SingleOrDefault(mapObject =>
            string.Equals(mapObject.Id, destinationId, StringComparison.Ordinal));
        if (mapObject is not null)
        {
            return mapObject.Position;
        }

        var resource = map.Resources.SingleOrDefault(resource =>
            string.Equals(resource.Id, destinationId, StringComparison.Ordinal));
        return resource?.Position ?? throw new InvalidDataException(
            $"Cognition destination '{destinationId}' is not present in the authoritative map.");
    }

    private static bool IsProviderFailure(string outcome) =>
        outcome.StartsWith("provider_failure", StringComparison.Ordinal) ||
        outcome.StartsWith("provider_cancelled", StringComparison.Ordinal);

    private static OwnerCognitionAdvanceResult NotAdvanced(string outcome) =>
        new(false, false, outcome, null, null, []);

    private OwnerWorldSnapshot CreateSnapshot() => new(
        CloneWorld(world),
        CloneMap(currentMap),
        world.Identity.InitialMapManifestDigest,
        currentMap.ManifestDigest,
        topologyRevision,
        isPaused,
        runEpoch,
        revision,
        climate with { },
        founderDrafts.Select(draft => draft with { }).ToArray(),
        approvedAssetReferences.Select(reference => reference with { }).ToArray(),
        instructionsByIdempotency.Values
            .OrderBy(instruction => instruction.SubmissionSequence)
            .ThenBy(instruction => instruction.InstructionId, StringComparer.Ordinal)
            .Select(instruction => instruction with { })
            .ToArray(),
        nextGlobalEventId - 1)
    {
        Cognition = cognition.Capture(),
    };

    private void AppendGlobalEvent(string kind, string detail)
    {
        revision = checked(revision + 1);
        globalEvents.Add(new OwnerWorldEvent(
            nextGlobalEventId++,
            world.Identity.WorldTick,
            revision,
            kind,
            detail));
    }

    private OwnerAuthoringBatchReceipt RejectedBatch(OwnerAuthoringBatch batch, string failure) => new(
        batch.BatchId,
        false,
        failure,
        revision,
        topologyRevision,
        currentMap.ManifestDigest);

    private void ApplyOperation(AuthoringCandidate candidate, OwnerAuthoringOperation operation)
    {
        switch (operation)
        {
            case SetTerrainOperation setTerrain:
                candidate.Map = SetTerrain(candidate.Map, setTerrain.Position, setTerrain.Terrain);
                candidate.MapTouched = true;
                return;

            case PlaceResourceOperation placeResource:
                candidate.Map = PlaceResource(candidate.Map, placeResource);
                candidate.MapTouched = true;
                return;

            case RemoveResourceOperation removeResource:
                candidate.Map = RemoveResource(candidate.Map, removeResource.Id);
                candidate.MapTouched = true;
                return;

            case PlaceObjectOperation placeObject:
                candidate.Map = PlaceObject(candidate.Map, placeObject.Id, placeObject.Kind, placeObject.Position);
                candidate.MapTouched = true;
                return;

            case RemoveObjectOperation removeObject:
                candidate.Map = RemoveObject(candidate.Map, removeObject.Id);
                candidate.MapTouched = true;
                return;

            case PlaceBuildingOperation placeBuilding:
                if (!candidate.Map.IsBuildable(placeBuilding.Position))
                {
                    throw new InvalidOperationException("Buildings require buildable ground; mountains and peaks are not build sites.");
                }
                candidate.Map = PlaceObject(candidate.Map, placeBuilding.Id, placeBuilding.BuildingKind, placeBuilding.Position);
                candidate.MapTouched = true;
                return;

            case RemoveBuildingOperation removeBuilding:
                candidate.Map = RemoveObject(candidate.Map, removeBuilding.Id);
                candidate.MapTouched = true;
                return;

            case PlacePlantOperation placePlant:
                candidate.Map = PlaceObject(candidate.Map, placePlant.Id, placePlant.PlantKind, placePlant.Position);
                candidate.MapTouched = true;
                return;

            case RemovePlantOperation removePlant:
                candidate.Map = RemoveObject(candidate.Map, removePlant.Id);
                candidate.MapTouched = true;
                return;

            case CreateFounderDraftOperation createFounder:
                CreateFounderDraft(candidate, createFounder);
                return;

            case RemoveFounderDraftOperation removeFounder:
                RemoveFounderDraft(candidate, removeFounder.Id);
                return;

            case SetWeatherOperation setWeather:
                candidate.Climate = candidate.Climate with { Weather = NormalizeRequiredText(setWeather.Weather, "Weather") };
                return;

            case SetSeasonOperation setSeason:
                candidate.Climate = candidate.Climate with { Season = NormalizeSeason(setSeason.Season) };
                return;

            case SetWeatherSeasonOperation setClimate:
                candidate.Climate = new OwnerClimate(
                    NormalizeRequiredText(setClimate.Weather, "Weather"),
                    NormalizeSeason(setClimate.Season));
                return;

            case AddApprovedAssetReferenceOperation addAsset:
                AddApprovedAssetReference(candidate, addAsset);
                return;

            case RemoveApprovedAssetReferenceOperation removeAsset:
                RemoveApprovedAssetReference(candidate, removeAsset.AssetId);
                return;

            default:
                throw new InvalidOperationException($"Unsupported authoring operation '{operation.OperationKind}'.");
        }
    }

    private void ValidateCandidate(AuthoringCandidate candidate)
    {
        candidate.Map = CanonicalizeMap(candidate.Map);
        if (candidate.MapTouched)
        {
            var mapValidation = MapAcceptance.Validate(candidate.Map);
            if (!mapValidation.IsValid)
            {
                throw new InvalidOperationException(mapValidation.Failure ?? "The authored map is invalid.");
            }

            // Terrain may change while an actor is elsewhere than the founder
            // start. Do not allow such a map edit to strand the protected
            // fixture actor on an impassable tile.
            if (!candidate.Map.IsPassable(world.Actor.Position))
            {
                throw new InvalidOperationException("The protected fixture actor must remain on passable terrain.");
            }
        }

        var draftIds = new HashSet<string>(StringComparer.Ordinal);
        var draftPositions = new HashSet<GridPoint>();
        foreach (var draft in candidate.FounderDrafts)
        {
            if (!draftIds.Add(draft.Id) || !draftPositions.Add(draft.Position))
            {
                throw new InvalidOperationException("Founder drafts require unique IDs and positions.");
            }

            if (!candidate.Map.IsPassable(draft.Position))
            {
                throw new InvalidOperationException("Founder drafts must be placed on passable terrain.");
            }
        }

        if (candidate.FounderDrafts.Any(draft =>
                string.Equals(draft.Id, world.Actor.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("A founder draft cannot reuse the protected fixture actor ID.");
        }

        if (candidate.ApprovedAssetReferences
            .Select(reference => reference.AssetId)
            .Distinct(StringComparer.Ordinal)
            .Count() != candidate.ApprovedAssetReferences.Count)
        {
            throw new InvalidOperationException("Approved asset references require unique asset IDs.");
        }
    }

    private static SeededMap SetTerrain(SeededMap map, GridPoint position, TerrainKind terrain)
    {
        EnsureContains(map, position);
        return map with
        {
            Tiles = map.Tiles
                .Select(tile => tile.Position == position ? tile with { Terrain = terrain } : tile)
                .ToArray(),
        };
    }

    private static SeededMap PlaceResource(SeededMap map, PlaceResourceOperation operation)
    {
        var id = NormalizeRequiredText(operation.Id, "Resource ID");
        var kind = NormalizeRequiredText(operation.Kind, "Resource kind");
        EnsureContains(map, operation.Position);
        if (map.Resources.Any(resource => string.Equals(resource.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"A resource with ID '{id}' already exists.");
        }

        return map with { Resources = map.Resources.Append(new MapResource(id, kind, operation.Position, operation.IsRenewable)).ToArray() };
    }

    private static SeededMap RemoveResource(SeededMap map, string resourceId)
    {
        var id = NormalizeRequiredText(resourceId, "Resource ID");
        if (!map.Resources.Any(resource => string.Equals(resource.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"No resource with ID '{id}' exists.");
        }

        return map with
        {
            Resources = map.Resources
                .Where(resource => !string.Equals(resource.Id, id, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static SeededMap PlaceObject(SeededMap map, string objectId, string objectKind, GridPoint position)
    {
        var id = NormalizeRequiredText(objectId, "Object ID");
        var kind = NormalizeRequiredText(objectKind, "Object kind");
        EnsureContains(map, position);
        if (map.CampObjects.Any(mapObject => string.Equals(mapObject.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"An object with ID '{id}' already exists.");
        }

        return map with { CampObjects = map.CampObjects.Append(new CampObject(id, kind, position)).ToArray() };
    }

    private static SeededMap RemoveObject(SeededMap map, string objectId)
    {
        var id = NormalizeRequiredText(objectId, "Object ID");
        if (!map.CampObjects.Any(mapObject => string.Equals(mapObject.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"No object with ID '{id}' exists.");
        }

        return map with
        {
            CampObjects = map.CampObjects
                .Where(mapObject => !string.Equals(mapObject.Id, id, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private void CreateFounderDraft(AuthoringCandidate candidate, CreateFounderDraftOperation operation)
    {
        var id = NormalizeRequiredText(operation.Id, "Founder draft ID");
        var displayName = NormalizeRequiredText(operation.DisplayName, "Founder draft display name");
        EnsureContains(candidate.Map, operation.Position);
        if (candidate.FounderDrafts.Any(draft => string.Equals(draft.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"A founder draft with ID '{id}' already exists.");
        }

        candidate.FounderDrafts.Add(new OwnerFounderDraft(id, displayName, operation.Position, checked(revision + 1)));
    }

    private static void RemoveFounderDraft(AuthoringCandidate candidate, string founderDraftId)
    {
        var id = NormalizeRequiredText(founderDraftId, "Founder draft ID");
        var removed = candidate.FounderDrafts.RemoveAll(draft => string.Equals(draft.Id, id, StringComparison.Ordinal));
        if (removed == 0)
        {
            throw new InvalidOperationException($"No founder draft with ID '{id}' exists.");
        }
    }

    private void AddApprovedAssetReference(
        AuthoringCandidate candidate,
        AddApprovedAssetReferenceOperation operation)
    {
        var id = NormalizeRequiredText(operation.AssetId, "Asset ID");
        var digest = NormalizeRequiredText(operation.AssetDigest, "Asset digest");
        if (candidate.ApprovedAssetReferences.Any(reference => string.Equals(reference.AssetId, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"An approved asset reference with ID '{id}' already exists.");
        }

        var reference = new OwnerApprovedAssetReference(id, digest);
        if (!approvedAssetReferencePolicy.IsApproved(reference))
        {
            throw new InvalidOperationException(
                "The requested asset reference is not approved by the server-owned asset catalog.");
        }

        candidate.ApprovedAssetReferences.Add(reference);
    }

    private static void RemoveApprovedAssetReference(AuthoringCandidate candidate, string assetId)
    {
        var id = NormalizeRequiredText(assetId, "Asset ID");
        var removed = candidate.ApprovedAssetReferences.RemoveAll(reference =>
            string.Equals(reference.AssetId, id, StringComparison.Ordinal));
        if (removed == 0)
        {
            throw new InvalidOperationException($"No approved asset reference with ID '{id}' exists.");
        }
    }

    private static void ValidateInstructionRequest(OwnerInstructionRequest request)
    {
        _ = NormalizeRequiredText(request.IdempotencyKey, "Instruction idempotency key");
        _ = NormalizeRequiredText(request.IssuerId, "Instruction issuer ID");
        _ = NormalizeRequiredText(request.TargetInhabitantId, "Instruction target inhabitant ID");
        _ = NormalizeRequiredText(request.Text, "Instruction text");
        if (!Enum.IsDefined(request.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Instruction kind is not supported.");
        }
    }

    private static bool Matches(OwnerQueuedInstruction instruction, OwnerInstructionRequest request) =>
        string.Equals(instruction.IssuerId, request.IssuerId.Trim(), StringComparison.Ordinal) &&
        string.Equals(instruction.TargetInhabitantId, request.TargetInhabitantId.Trim(), StringComparison.Ordinal) &&
        instruction.Kind == request.Kind &&
        string.Equals(instruction.Text, request.Text.Trim(), StringComparison.Ordinal);

    private static bool Matches(
        AppliedAuthoringBatch applied,
        string issuerId,
        IReadOnlyList<OwnerAuthoringOperation>? operations) =>
        operations is not null &&
        string.Equals(applied.IssuerId, issuerId, StringComparison.Ordinal) &&
        applied.Operations.SequenceEqual(operations);

    private static string NormalizeRequiredText(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} is required.", nameof(value));
        }

        return value.Trim();
    }

    private static string NormalizeSeason(string? season)
    {
        var normalized = NormalizeRequiredText(season, "Season").ToLowerInvariant();
        if (!ValidSeasons.Contains(normalized))
        {
            throw new ArgumentOutOfRangeException(nameof(season), "Season must be spring, summer, autumn, or winter.");
        }

        return normalized;
    }

    private static void EnsureContains(SeededMap map, GridPoint position)
    {
        if (!map.Contains(position))
        {
            throw new InvalidOperationException("The requested map position is outside the bounded grid.");
        }
    }

    private static SeededMap CanonicalizeMap(SeededMap map)
    {
        var canonical = map with
        {
            Tiles = map.Tiles
                .OrderBy(tile => tile.Position.Y)
                .ThenBy(tile => tile.Position.X)
                .Select(tile => tile with { })
                .ToArray(),
            CampObjects = map.CampObjects
                .OrderBy(mapObject => mapObject.Id, StringComparer.Ordinal)
                .Select(mapObject => mapObject with { })
                .ToArray(),
            Resources = map.Resources
                .OrderBy(resource => resource.Id, StringComparer.Ordinal)
                .Select(resource => resource with { })
                .ToArray(),
            ManifestDigest = string.Empty,
        };
        return canonical with { ManifestDigest = MapManifestCodec.Digest(canonical) };
    }

    private static SeededMap CloneMap(SeededMap map) => map with
    {
        Tiles = map.Tiles.Select(tile => tile with { }).ToArray(),
        CampObjects = map.CampObjects.Select(mapObject => mapObject with { }).ToArray(),
        Resources = map.Resources.Select(resource => resource with { }).ToArray(),
    };

    private static HarnessWorld CloneWorld(HarnessWorld source) => source with
    {
        Map = CloneMap(source.Map),
        Actor = source.Actor with { },
        Resources = source.Resources.Select(resource => resource with { }).ToArray(),
        Events = source.Events.Select(worldEvent => worldEvent with { }).ToArray(),
    };

    private static OwnerWorldEvent CloneEvent(OwnerWorldEvent source) => source with { };

    private static string ToWireValue(OwnerInstructionKind kind) => kind switch
    {
        OwnerInstructionKind.Suggestive => "suggestive",
        OwnerInstructionKind.MustDo => "must_do",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed record RequiredStateDocument(
        HarnessWorld World,
        SeededMap CurrentMap,
        OwnerClimate Climate,
        IReadOnlyList<OwnerFounderDraft> FounderDrafts,
        IReadOnlyList<OwnerApprovedAssetReference> ApprovedAssetReferences,
        IReadOnlyList<OwnerQueuedInstruction> Instructions,
        IReadOnlyList<OwnerInstructionReceipt> InstructionReceipts,
        IReadOnlyList<OwnerAppliedAuthoringBatchState> AppliedAuthoringBatches,
        IReadOnlyList<OwnerWorldEvent> GlobalEvents);

    private sealed record RestoredInstructionState(
        Dictionary<string, OwnerQueuedInstruction> ByIdempotency,
        Dictionary<string, OwnerInstructionReceipt> Receipts,
        Dictionary<long, OwnerInstructionReceipt> ReceiptsByRevision);

    private sealed record RestoredAuthoringBatch(
        string BatchId,
        string IssuerId,
        IReadOnlyList<OwnerAuthoringOperation> Operations,
        OwnerAuthoringBatchReceipt Receipt);

    private sealed record RestoredAuthoringState(
        SeededMap CurrentMap,
        OwnerClimate Climate,
        IReadOnlyList<OwnerFounderDraft> FounderDrafts,
        IReadOnlyList<OwnerApprovedAssetReference> ApprovedAssetReferences,
        long TopologyRevision,
        Dictionary<string, AppliedAuthoringBatch> Batches,
        Dictionary<long, RestoredAuthoringBatch> BatchesByRevision);

    private sealed class AuthoringCandidate
    {
        public AuthoringCandidate(
            SeededMap map,
            OwnerClimate climate,
            IEnumerable<OwnerFounderDraft> drafts,
            IEnumerable<OwnerApprovedAssetReference> assets)
        {
            Map = map;
            Climate = climate with { };
            FounderDrafts = drafts.Select(draft => draft with { }).ToList();
            ApprovedAssetReferences = assets.Select(reference => reference with { }).ToList();
        }

        public SeededMap Map { get; set; }

        public OwnerClimate Climate { get; set; }

        public List<OwnerFounderDraft> FounderDrafts { get; }

        public List<OwnerApprovedAssetReference> ApprovedAssetReferences { get; }

        public bool MapTouched { get; set; }
    }

    private sealed record AppliedAuthoringBatch(
        string IssuerId,
        IReadOnlyList<OwnerAuthoringOperation> Operations,
        OwnerAuthoringBatchReceipt Receipt);
}
