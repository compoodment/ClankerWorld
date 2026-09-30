using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;

namespace ClankerWorld.GodotClient.ClientState;

/// <summary>
/// The non-secret identity boundary for one locally retained owner request.
/// A pending request cannot be replayed against a different world, device key,
/// or server origin after a pairing changes.
/// </summary>
public sealed record OwnerPendingSubmissionBinding(
    OwnerAuthorityIdentity Authority,
    string DeviceId,
    string PublicKeyFingerprint,
    string ServerOrigin)
{
    /// <summary>
    /// Creates a validated, canonical binding. Only HTTPS is accepted remotely;
    /// literal loopback HTTP remains available for explicit local development.
    /// </summary>
    public static OwnerPendingSubmissionBinding Create(
        OwnerAuthorityIdentity authority,
        string deviceId,
        string publicKeyFingerprint,
        Uri serverOrigin)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority.ServerAuthorityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority.WorldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyFingerprint);

        return new OwnerPendingSubmissionBinding(
            authority,
            deviceId,
            publicKeyFingerprint,
            CanonicalizeServerOrigin(serverOrigin));
    }

    internal bool Matches(OwnerPendingSubmissionBinding expected) =>
        Authority is not null &&
        expected.Authority is not null &&
        string.Equals(Authority.ServerAuthorityId, expected.Authority.ServerAuthorityId, StringComparison.Ordinal) &&
        string.Equals(Authority.WorldId, expected.Authority.WorldId, StringComparison.Ordinal) &&
        string.Equals(DeviceId, expected.DeviceId, StringComparison.Ordinal) &&
        string.Equals(PublicKeyFingerprint, expected.PublicKeyFingerprint, StringComparison.Ordinal) &&
        string.Equals(ServerOrigin, expected.ServerOrigin, StringComparison.Ordinal);

    internal static bool IsValid(OwnerPendingSubmissionBinding? binding)
    {
        if (binding?.Authority is null ||
            string.IsNullOrWhiteSpace(binding.Authority.ServerAuthorityId) ||
            string.IsNullOrWhiteSpace(binding.Authority.WorldId) ||
            string.IsNullOrWhiteSpace(binding.DeviceId) ||
            string.IsNullOrWhiteSpace(binding.PublicKeyFingerprint) ||
            !Uri.TryCreate(binding.ServerOrigin, UriKind.Absolute, out var origin))
        {
            return false;
        }

        try
        {
            return string.Equals(
                binding.ServerOrigin,
                CanonicalizeServerOrigin(origin),
                StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string CanonicalizeServerOrigin(Uri serverOrigin)
    {
        ArgumentNullException.ThrowIfNull(serverOrigin);
        if (!WorldServerOrigin.TryResolve(serverOrigin.AbsoluteUri, out var canonicalOrigin))
        {
            throw new ArgumentException(
                "The pending owner request must be bound to an absolute HTTPS origin (or literal loopback HTTP for local development).",
                nameof(serverOrigin));
        }

        return canonicalOrigin.AbsoluteUri;
    }
}

/// <summary>
/// A client-owned instruction payload whose idempotency key survives a lost
/// response. It contains no signed challenge or reusable credential.
/// </summary>
public sealed record OwnerPendingInstructionSubmission(
    string IdempotencyKey,
    string TargetInhabitantId,
    string Kind,
    string Text,
    string WorldId)
{
    internal bool IsValid =>
        !string.IsNullOrWhiteSpace(WorldId) &&
        !string.IsNullOrWhiteSpace(IdempotencyKey) &&
        !string.IsNullOrWhiteSpace(TargetInhabitantId) &&
        !string.IsNullOrWhiteSpace(Kind) &&
        !string.IsNullOrWhiteSpace(Text);

    public bool CanRetryIn(string? worldId) => !string.IsNullOrWhiteSpace(WorldId) && WorldId == worldId;

    public OwnerInstructionAction ToAction() => new(IdempotencyKey, TargetInhabitantId, Kind, Text, WorldId);

    public static OwnerPendingInstructionSubmission FromAction(OwnerInstructionAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new OwnerPendingInstructionSubmission(
            action.IdempotencyKey,
            action.TargetInhabitantId,
            action.Kind,
            action.Text,
            action.WorldId);
    }
}

/// <summary>
/// A client-owned authoring operation. These are intentionally projection
/// types: the Godot client never references simulation/server operation types.
/// </summary>
public sealed record OwnerPendingAuthoringOperation(
    string Kind,
    string? Id,
    string? Value,
    string? SecondaryValue,
    int? X,
    int? Y,
    bool? IsRenewable)
{
    internal bool IsValid => !string.IsNullOrWhiteSpace(Kind);

    public OwnerAuthoringOperationAction ToAction() => new(Kind, Id, Value, SecondaryValue, X, Y, IsRenewable);

    public static OwnerPendingAuthoringOperation FromAction(OwnerAuthoringOperationAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new OwnerPendingAuthoringOperation(
            action.Kind,
            action.Id,
            action.Value,
            action.SecondaryValue,
            action.X,
            action.Y,
            action.IsRenewable);
    }
}

/// <summary>
/// A complete authoring batch whose batch identifier survives a lost response.
/// </summary>
public sealed record OwnerPendingAuthoringSubmission(
    string BatchId,
    IReadOnlyList<OwnerPendingAuthoringOperation> Operations)
{
    internal bool IsValid =>
        !string.IsNullOrWhiteSpace(BatchId) &&
        Operations is { Count: > 0 } &&
        Operations.All(operation => operation is not null && operation.IsValid);

    public OwnerAuthoringBatchAction ToAction() => new(
        BatchId,
        Operations.Select(operation => operation.ToAction()).ToArray());

    public static OwnerPendingAuthoringSubmission FromAction(OwnerAuthoringBatchAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(action.Operations);
        return new OwnerPendingAuthoringSubmission(
            action.BatchId,
            action.Operations.Select(OwnerPendingAuthoringOperation.FromAction).ToArray());
    }
}

/// <summary>
/// A strict one-of document. Exactly one instruction or authoring batch is
/// retained, so one explicit retry can preserve the server idempotency token
/// without becoming a local offline command queue.
/// </summary>
public sealed record OwnerPendingSubmission(
    OwnerPendingSubmissionBinding Binding,
    OwnerPendingInstructionSubmission? Instruction,
    OwnerPendingAuthoringSubmission? Authoring)
{
    public bool IsInstruction => Instruction is not null;

    public bool IsAuthoring => Authoring is not null;

    public string LogicalId => Instruction?.IdempotencyKey ?? Authoring?.BatchId ?? string.Empty;

    internal bool IsValid =>
        OwnerPendingSubmissionBinding.IsValid(Binding) &&
        ((Instruction is { IsValid: true } && Authoring is null) ||
         (Authoring is { IsValid: true } && Instruction is null));

    public static OwnerPendingSubmission ForInstruction(
        OwnerPendingSubmissionBinding binding,
        OwnerInstructionAction action)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new OwnerPendingSubmission(binding, OwnerPendingInstructionSubmission.FromAction(action), null);
    }

    public static OwnerPendingSubmission ForAuthoring(
        OwnerPendingSubmissionBinding binding,
        OwnerAuthoringBatchAction action)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new OwnerPendingSubmission(binding, null, OwnerPendingAuthoringSubmission.FromAction(action));
    }
}

/// <summary>
/// Atomically retains one non-secret client request before it is sent. A
/// response loss can then be retried with the exact same instruction
/// idempotency key or authoring batch identifier. The store deliberately does
/// not keep device private keys, signatures, challenge values, pairing codes,
/// or bearer material.
/// </summary>
public sealed class OwnerPendingSubmissionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    private readonly string path;

    /// <summary>
    /// Creates a store at a caller-owned, user-local path. Godot UI code should
    /// pass a path under <c>user://</c>; this class remains engine-independent
    /// so the persistence and validation rules can be tested normally.
    /// </summary>
    public OwnerPendingSubmissionStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    /// <summary>
    /// Returns the one validated pending submission bound to
    /// <paramref name="expectedBinding"/>. Missing, corrupt, stale, or
    /// mismatched records fail closed as <see langword="null"/>.
    /// </summary>
    public OwnerPendingSubmission? TryLoad(OwnerPendingSubmissionBinding expectedBinding)
    {
        ArgumentNullException.ThrowIfNull(expectedBinding);
        if (!OwnerPendingSubmissionBinding.IsValid(expectedBinding) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var pending = JsonSerializer.Deserialize<OwnerPendingSubmission>(File.ReadAllText(path), JsonOptions);
            return pending is { IsValid: true } && pending.Binding.Matches(expectedBinding)
                ? pending
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Atomically creates the sole pending record. It never overwrites an
    /// existing record, including an unreadable one; callers must explicitly
    /// forget stale local state instead of silently discarding a retry.
    /// </summary>
    public bool TrySave(OwnerPendingSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (!submission.IsValid)
        {
            throw new ArgumentException("The pending owner submission is invalid.", nameof(submission));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, submission, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// Removes a record only when the stored document still exactly matches
    /// the completed request. It will not delete a request for another paired
    /// device or server origin.
    /// </summary>
    public bool TryClear(OwnerPendingSubmission completedSubmission)
    {
        ArgumentNullException.ThrowIfNull(completedSubmission);
        var stored = TryLoad(completedSubmission.Binding);
        if (stored is null ||
            !string.Equals(JsonSerializer.Serialize(stored, JsonOptions), JsonSerializer.Serialize(completedSubmission, JsonOptions), StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Explicitly discards the local pending record. This is intended for the
    /// same user-initiated recovery path that forgets a local device
    /// registration; it is never called automatically on a network failure.
    /// </summary>
    public bool TryForget()
    {
        try
        {
            if (!File.Exists(path))
            {
                return true;
            }

            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
