---
title: Device pairing
type: development-reference
status: active
updated: 2026-09-30
---

# Device pairing

Pairing gives one approved device permission to inspect and control the private
world. The private Tailnet encrypts the connection and limits who can reach the
server; being on that network alone does not grant world access. For the player
steps, see [Playing](../playing.md#connect-your-device).

The September 2026 identifier reset requires a new Windows registration and
device key. The previous registration is not imported; pair the matching new
client and host through the normal flow. Keep old authority in a private
rollback backup. This is the approved pre-release exception described in
[Saves and replay](saves-and-replay.md).

## Pairing flow

1. A Windows client creates a non-exportable current-user CNG P-256 signing
   key locally. Its non-secret pairing registration metadata lives under the
   app user's storage, while the private key remains in the Windows key store.
   The server receives only the public key and its fingerprint.
2. The unpaired client requests a pairing record. The server returns an opaque
   pairing ID, a short human-verification code, and a short expiry. The code is
   a comparison value, not a credential. Pairing and challenge state is bounded;
   a pairing also expires after a bounded number of incorrect visible-code
   attempts instead of becoming a durable brute-force target.
3. A host-local bootstrap administrator approves the matching pairing ID and
   code through a separate loopback-only listener. There is no bootstrap
   password or reusable approval secret in the client, a URL, or deployment
   configuration. Once an owner device exists, it may approve or revoke further
   devices through signed owner-device-management requests. It may also request
   the signed registry of paired-device lifecycle records. The registry exposes
   only public device IDs, public-key fingerprints, and lifecycle state; it
   never exposes private keys, comparison codes, or reusable challenges. The
   same separate host-local listener supports recovery revocation when every
   paired Windows device has been lost or revoked.
4. The device proves possession of its private key before activation. The
   server records its public-key fingerprint, fixed owner scope, and durable
   activation/revocation state. Private keys and raw approval codes never enter
   world saves, snapshots, event projections, or logs.
5. Each read or control request carries a short-lived server challenge and a
   signature over canonical request data. The server consumes the challenge
   once before asking the runtime to commit. Challenges are process-local:
   restarting the host invalidates both pending and consumed challenges, so an
   old nonce cannot be replayed after a crash. Pairing and revocation remain
   durable; idle reads do not rewrite the authority file. It
   derives the durable issuer from the active device; the runtime mints durable
   IDs, event ordering, and submission sequence. A client request ID and
   idempotency key are signed ingress values, not ownership claims.

The initial host-local approval route is deliberately separate from normal
network requests. For this private deployment, the owner can relay the short
comparison code through the already trusted direct-control channel; that
channel approves a pending pairing only, never supplies the device key.

## Client and host updates

An approved device can reconnect even when a particular action needs a newer
host. Authenticated challenge responses advertise supported action payloads;
New World preview and creation require `clankerworld.owner-world-creation.v2`.
The client checks each fresh challenge before signing or posting either action.
An absent or different format produces an update message while keeping the
existing pairing. Older hosts without this advertisement need an update too.
Other signed actions, including reconnect, keep their existing contracts.

The advertisement is compatibility information, not permission to weaken a
proof. The host still reconstructs the exact action payload and verifies its
binding, device signature and one-use challenge. The client never retries with
an older format. Actual challenge/access failures retain their access messages.
See [Releasing](releasing.md#matching-client-and-host-builds) for distribution order.

Owner pairing and signed control traffic require HTTPS. Literal loopback IPs
are the sole plaintext exception, for an explicit local-development host.

The headless host persists paired-device authority separately from the world
runtime. The authority file contains public keys, fingerprints, state, and
anti-replay hashes—not device private keys or raw comparison codes. The runtime
file contains world state and never becomes credential storage. Both files are
written atomically and use owner-only filesystem permissions on Unix hosts.

## Server-origin binding

An owner key is not a portable bearer credential for arbitrary URLs. Both a
pending pairing and an activated local registration are bound to one canonical
server origin. The client permits remote HTTPS origins, with literal loopback
HTTP only for explicit local development; paths, queries, fragments, and
userinfo are not accepted as part of an origin.

While pairing is pending, polling and activation remain at the origin that
issued the pairing record. The client verifies the expected server authority,
device ID, and public-key fingerprint before it saves an activated
registration. Once paired, an edited URL or command-line value cannot silently
retarget the registered owner key. Changing servers is an explicit local
operation: forget the registration and pair to the new server. The persisted
authority and world identities provide a second binding beyond the transport
origin.

Replacing or forgetting local registration also discards the held observation,
event cursor and terrain cache, and cancels an old in-flight refresh. This applies
to ordinary activation and recovery of an already-active pairing. The new host
starts at cursor zero, so a younger world can be entered without restarting the
client. Tick/event regression and terrain identity checks still apply within the
new observation timeline.

## Response-loss recovery

Instructions and paused-authoring batches are server-idempotent, but a client
can still lose the response after the server commits one. Before sending either
kind of request, the Godot client may atomically retain one non-secret pending
record. It is bound to the authority identity, device ID, public-key
fingerprint, and canonical server origin, and preserves the exact instruction
idempotency key or authoring batch ID.

The user can explicitly retry that one record. The retry obtains a new one-use
challenge and signature, then submits the same logical request so the server
returns the original receipt rather than creating a duplicate. Private-world
instruction keys ignore surrounding whitespace consistently. An exact instruction
retry still returns its accepted receipt after the recipient dies, including
after saving and restarting the host. A new instruction to a deceased agent or
a different request reusing that key remains rejected. This is not a
general offline queue: only one request is retained, it cannot cross a pairing
or origin boundary, and it can be explicitly forgotten. The record never
contains a private key, signature, challenge, comparison code, or bearer
credential.

## Current scope

Every paired device has the sole `owner` scope: world observation,
pause/resume, instruction submission, authoring-batch submission, and device
management. There are no viewer, operator, or multiplayer roles yet. A revoked
device immediately loses access. Authority state retains non-secret public-key
fingerprints and device IDs; world-side control events retain a non-secret
server-derived issuer string, never a private key or comparison code.

All world-changing requests remain requests. Authentication permits the server
to validate and enqueue them; only the authoritative runtime commits the
result. Unpaired devices receive neither full-world observations nor control
results. The unauthenticated HTTP surface therefore exposes only protocol
discovery, not world observation or a second owner interface. Legacy static
diagnostic assets are not a supported game client.

## Required evidence

Current pairing evidence must cover expiry, bounded failed-code attempts,
invalid proof, replayed challenge, revocation, unpaired read/write denial,
owner-only observation, server-derived issuer/tick/sequence, idempotent control
submission, paired-origin binding, signed registry/approval/revocation,
response-loss retry, and authority/runtime
recovery across restart. The Windows client must prove it can create/store a
device key, complete a paired reconnect, and reject a server response that
lacks the negotiated owner capability. The export path must be verified
separately from a real Windows 11 owner test; the latter is not implied by a
Linux CI export.

## Pairing volume and local recovery

Public pairing creation has a shared eight-attempt, one-minute budget, including
invalid keys. A shared budget avoids trusting proxy-supplied source addresses; a
429 response includes Retry-After: 60. Signed challenges, reconnect and pause do
not consume it. All pairing route bodies are capped at 16 KiB before normal JSON
binding on Kestrel; the general host request cap is explicitly 30,000,000 bytes.
This is bounded ingress, not a claim that the Tailnet endpoint is DoS-proof.

If unauthenticated requests fill the eight pending slots, the existing separate
loopback approval listener also accepts POST /api/v1/local/pairings with the same
public-key-only body as public pairing creation. It bypasses the public creation
budget and, only when necessary, expires the oldest **unapproved** pending request
to admit the operator's new request. It never expires an approved pairing or
revokes an active device; if all slots are approved, it refuses. Invalid keys and
already active keys must not displace a pending request. Approval and signed
activation still follow their existing checks. The ordinary forwarded listener
returns 404 for this recovery endpoint. Never forward the approval listener.

This is an operator recovery path, not automatic queue eviction on behalf of an
untrusted remote client. Keep the returned code and proof local/private. Tests
cover capacity recovery and signed owner availability; chunked-body enforcement
is a Kestrel boundary, not claimed from TestServer's Content-Length test alone.

## Bounded owner actions

Signed actions have a 15-second deadline over the complete challenge/sign/send/read
operation, or the HTTP client's shorter configured timeout. Cancellation covers
both response bodies even after successful headers. Reconnect retains its shorter
four-second deadline. A timeout does not prove that the server rejected an action;
instructions and authoring retain their exact existing retry record until a
receipt is accepted.

Pause/resume waits for the active owner action to release the client gate rather
than being dropped as a duplicate click. Ordinary duplicate actions still do not
queue. Confirming Quit to Menu can retry an unconfirmed pause, and leaving still
requires an accepted pause receipt, even if an observation already says paused.
A paused observation alone cannot prove a failed checkpoint write recovered.
A later failed refresh does not revoke an accepted receipt. This does not change Main Menu Quit Game.
