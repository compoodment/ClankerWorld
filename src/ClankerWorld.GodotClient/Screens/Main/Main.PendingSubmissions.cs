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
            ? pendingSubmissionStore.TryLoad(binding)
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
                ResolveWorldUri());
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
            !pending.Binding.Matches(binding))
        {
            SetStatus("this retained request is not bound to the current paired device and pinned server; forget it explicitly before making a new request", good: false);
            return;
        }

        var completed = false;
        await RunOwnerActionAsync(async () =>
        {
            if (pending.Instruction is { } instruction)
            {
                var receipt = await ownerApi.SubmitInstructionAsync(
                    ResolveWorldUri(), authority, deviceId, instruction.ToAction(), signer, CancellationToken.None);
                completed = true;
                return $"confirmed {instruction.Kind} instruction {receipt.InstructionId}";
            }

            if (pending.Authoring is { } authoring)
            {
                var receipt = await ownerApi.SubmitAuthoringAsync(
                    ResolveWorldUri(), authority, deviceId, authoring.ToAction(), signer, CancellationToken.None);
                completed = true;
                return receipt.Applied
                    ? $"confirmed authoring batch {receipt.BatchId} at revision {receipt.Revision}"
                    : $"authoring batch rejected · {receipt.Failure ?? "unknown validation failure"}";
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
                $"Retained instruction retry · {instruction.Kind} for {instruction.TargetInhabitantId} · ID {instruction.IdempotencyKey}",
            { Authoring: { } authoring } =>
                $"Retained paused-authoring retry · batch {authoring.BatchId}",
            _ => "No retained owner request. A network failure keeps one instruction or authoring batch here for an exact retry.",
        };
    }

    private async Task RunOwnerActionAsync(Func<Task<string>> action)
    {
        if (isOwnerAction)
        {
            return;
        }

        isOwnerAction = true;
        refreshCancellation?.Cancel();
        RefreshControlAvailability();
        try
        {
            SetStatus("Sending…", good: true);
            var detail = await action();
            SetStatus(detail, good: true);
            await RefreshAsync();
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
        catch (TaskCanceledException exception)
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
    }

}
