- Use a disposable host and test device with a local test proxy that can hold
  a partial response. Try starting pairing, checking its status and activating
  the device while the proxy stops sending partway through a response. The
  connection controls should recover after about 15 seconds and show a failure.
  Restore responses and confirm that pairing can finish. If activation already
  committed, the next status poll should recover the active device registration.
  Main Menu Quit Game should stay available throughout the stall.
  ([#658](https://github.com/compoodment/ClankerWorld/issues/658))
