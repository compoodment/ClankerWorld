using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Globalization;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private bool TryBeginPendingInstruction(
        OwnerInstructionAction action,
        out OwnerPendingSubmission pending)
    {
        pending = null!;
        if (!TryCreatePendingSubmissionBinding(out var binding))
        {
            SetStatus("cannot retain an instruction until this paired device has a valid pinned server origin", good: false);
            return false;
        }

        pending = OwnerPendingSubmission.ForInstruction(binding, action);
        return TryRetainPendingSubmission(pending);
    }

    private bool TryBeginPendingAuthoring(
        OwnerAuthoringBatchAction action,
        out OwnerPendingSubmission pending)
    {
        pending = null!;
        if (!TryCreatePendingSubmissionBinding(out var binding))
        {
            SetStatus("cannot retain authoring until this paired device has a valid pinned server origin", good: false);
            return false;
        }

        pending = OwnerPendingSubmission.ForAuthoring(binding, action);
        return TryRetainPendingSubmission(pending);
    }

    private bool TryBeginPendingOrderCancel(
        OwnerOrderCancelAction action,
        out OwnerPendingSubmission pending)
    {
        pending = null!;
        if (!TryCreatePendingSubmissionBinding(out var binding))
        {
            SetStatus("cannot retain an order cancellation until this paired device has a valid pinned server origin", good: false);
            return false;
        }
        pending = OwnerPendingSubmission.ForOrderCancel(binding, action);
        return TryRetainPendingSubmission(pending);
    }

    private bool TryRetainPendingSubmission(OwnerPendingSubmission candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (pendingSubmission is not null)
        {
            SetStatus("a prior owner request is awaiting confirmation; retry it or explicitly forget it first", good: false);
            return false;
        }

        if (!pendingSubmissionStore.TrySave(candidate))
        {
            SetStatus("could not retain the owner request locally; retry or forget the existing local retry record first", good: false);
            return false;
        }

        pendingSubmission = candidate;
        RenderPendingSubmission();
        RefreshControlAvailability();
        return true;
    }

    private void LoadPendingSubmission()
    {
        pendingSubmission = TryCreatePendingSubmissionBinding(out var binding)
            ? pendingSubmissionStore.TryLoadForRegistration(binding)
            : null;
        RenderPendingSubmission();
        RefreshControlAvailability();
    }

    private bool TryCreatePendingSubmissionBinding(out OwnerPendingSubmissionBinding binding)
    {
        binding = null!;
        if (registeredEndpointInvalid || registration is null || deviceKey is null)
        {
            return false;
        }

        try
        {
            binding = OwnerPendingSubmissionBinding.Create(
                registration.Authority,
                registration.DeviceId,
                deviceKey.PublicKeyFingerprint,
                ResolveWorldUri(), observationSession.Timeline, observationSession.Current?.Baseline.Snapshot.WorldId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task RetryPendingSubmissionAsync()
    {
        var pending = pendingSubmission;
        if (pending is null)
        {
            SetStatus("there is no loaded owner request to retry", good: false);
            return;
        }

        if (!TryGetOwner(out var authority, out var deviceId, out var signer) ||
            !TryCreatePendingSubmissionBinding(out var binding) ||
            !pending.Binding.MatchesRegistration(binding) || !CanRetryPendingSubmission(pending))
        {
            SetStatus("This retained request belongs to a previous world state or paired connection. Check the world, then explicitly forget it before sending a new request.", good: false);
            return;
        }

        if (pending.Instruction is { } retainedInstruction &&
            !retainedInstruction.CanRetryIn(observationSession.Current?.Baseline.Snapshot.WorldId))
        {
            SetStatus("This instruction belongs to a previous world state. Check the world, then explicitly forget it before sending a new request.", good: false);
            return;
        }
        if (pending.OrderCancel is { } retainedCancellation &&
            !retainedCancellation.CanRetryIn(observationSession.Current?.Baseline.Snapshot.WorldId))
        {
            SetStatus("This cancellation belongs to a previous world state. Check the world, then explicitly forget it before sending a new request.", good: false);
            return;
        }

        var completed = false;
        await RunOwnerActionAsync(async () =>
        {
            if (pending.Instruction is { } instruction)
            {
                await AwaitCurrentWorldResultAsync(ownerApi.SubmitInstructionAsync(
                    ResolveWorldUri(), authority, deviceId, instruction.ToAction(), signer, CancellationToken.None));
                completed = true;
                return InstructionSubmissionResultText(instruction.Kind, instruction.Queue);
            }

            if (pending.Authoring is { } authoring)
            {
                var receipt = await AwaitCurrentWorldResultAsync(ownerApi.SubmitAuthoringAsync(
                    ResolveWorldUri(), authority, deviceId, authoring.ToAction(), signer, CancellationToken.None));
                completed = true;
                return receipt.Applied
                    ? $"confirmed authoring batch {receipt.BatchId} at revision {receipt.Revision}"
                    : $"authoring batch rejected · {receipt.Failure ?? "unknown validation failure"}";
            }

            if (pending.OrderCancel is { } cancellation)
            {
                var receipt = await AwaitCurrentWorldResultAsync(ownerApi.CancelOrderAsync(
                    ResolveWorldUri(), authority, deviceId, cancellation.ToAction(), signer, CancellationToken.None));
                completed = true;
                return OrderCancellationResultText(receipt);
            }

            throw new InvalidOperationException("The retained owner request has no supported payload.");
        });

        if (completed)
        {
            CompletePendingSubmission(pending);
        }
    }

    private void CompletePendingSubmission(OwnerPendingSubmission completed)
    {
        if (!CanRetryPendingSubmission(completed)) return;
        if (!pendingSubmissionStore.TryClear(completed))
        {
            SetStatus("server confirmed the request, but its local retry record could not be cleared; retry remains safe or forget it after checking the world", good: false);
            return;
        }

        if (ReferenceEquals(pendingSubmission, completed))
        {
            pendingSubmission = null;
        }

        RenderPendingSubmission();
        RefreshControlAvailability();
    }

    private void ForgetPendingSubmission()
    {
        if (!pendingSubmissionStore.TryForget())
        {
            SetStatus("could not discard the local retry record", good: false);
            return;
        }

        pendingSubmission = null;
        RenderPendingSubmission();
        RefreshControlAvailability();
        SetStatus("discarded the local retry record; no server state was changed", good: false);
    }

    private void RenderPendingSubmission()
    {
        pendingSubmissionLabel.Text = pendingSubmission switch
        {
            { Instruction: { } instruction } =>
                $"Retained instruction retry · {instruction.Kind} for {instruction.TargetInhabitantId} · ID {instruction.IdempotencyKey}" +
                (CanRetryPendingSubmission(pendingSubmission)
                    ? string.Empty : " · Previous world state: check the world, then forget this request before sending a new one."),
            { Authoring: { } authoring } =>
                $"Retained paused-authoring retry · batch {authoring.BatchId}" +
                (CanRetryPendingSubmission(pendingSubmission)
                    ? string.Empty : " · Previous world state: check the world, then forget this request before sending a new one."),
            { OrderCancel: { } cancellation } =>
                $"Retained order-cancellation retry · order {cancellation.OrderId} for {cancellation.TargetInhabitantId}" +
                (CanRetryPendingSubmission(pendingSubmission)
                    ? string.Empty : " · Previous world state: check the world, then forget this request before sending a new one."),
            _ => "No retained owner request. A network failure keeps one instruction, order cancellation or authoring batch here for an exact retry.",
        };
    }

    private readonly OwnerActionGate ownerActionGate = new();

    private bool CanRetryPendingSubmission(OwnerPendingSubmission? pending)
    {
        var worldId = observationSession.Current?.Baseline.Snapshot.WorldId;
        return pending is not null && !observationSession.AwaitingFreshBaseline &&
            pending.Binding.CanRetryIn(worldId, observationSession.Timeline) &&
            (pending.Instruction is not { } instruction || instruction.CanRetryIn(worldId)) &&
            (pending.OrderCancel is not { } cancellation || cancellation.CanRetryIn(worldId));
    }

    private async Task RunOwnerActionAsync(Func<Task<string>> action, bool waitForTurn = false,
        string? conflictMessage = null)
    {
        var generation = observationSession.RequestGeneration;
        if (!IsCurrentWorldRequest(generation)) return;
        await ownerActionGate.RunAsync(async () =>
        {
            if (!IsCurrentWorldRequest(generation)) return;
            isOwnerAction = true;
            refreshCancellation?.Cancel();
            RefreshControlAvailability();
            try
            {
                SetStatus("Sending...", good: true);
                var detail = await action();
                SetStatus(detail, good: true);
                await RefreshAsync();
            }
            catch (ObsoleteWorldRequestException)
            {
                // A different world state is being displayed or recovered.
            }
            catch (System.Net.Http.HttpRequestException exception)
                when (exception.StatusCode == System.Net.HttpStatusCode.Conflict && conflictMessage is not null)
            {
                SetStatus(conflictMessage, good: false);
            }
            catch (System.Net.Http.HttpRequestException exception) when (exception.StatusCode is not null)
            {
                // The host answered and refused this one action; the connection
                // and the displayed world remain current.
                SetStatus($"The world host did not accept that request · {FriendlyFailure(exception)}", good: false);
            }
            catch (System.Net.Http.HttpRequestException exception)
            {
                ShowHeldState($"could not reach the world host · {FriendlyFailure(exception)}");
            }
            catch (OperationCanceledException exception)
            {
                ShowHeldState($"the world host did not respond · {FriendlyFailure(exception)}");
            }
            catch (Exception exception)
            {
                SetStatus($"Could not complete that action · {FriendlyFailure(exception)}", good: false);
            }
            finally
            {
                isOwnerAction = false;
                RefreshControlAvailability();
            }
        }, waitForTurn);
    }

}
