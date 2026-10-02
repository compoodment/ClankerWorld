# Pairing after a stalled response

Pending Windows check for [#658](https://github.com/compoodment/ClankerWorld/issues/658). Automated transport tests cover starting
pairing, status polling and activation with a partial JSON response, timeout,
caller cancellation and a successful later request. They do not verify the
Windows connection screen or key store by hand.

- Use a disposable host and test device with a local test proxy that can hold
  a partial response. Try starting pairing, checking its status and activating
  the device while the proxy stops sending partway through a response. The
  connection controls should recover after about 15 seconds and show a failure.
  Restore responses and confirm that pairing can finish. If activation already
  committed, the next status poll should recover the active device registration.
  Main Menu Quit Game should stay available throughout the stall.
  ([#658](https://github.com/compoodment/ClankerWorld/issues/658))

Record the client and host commits, Windows build, date, screen resolution and
interface size. Do not include pairing codes, private keys or private saves.
