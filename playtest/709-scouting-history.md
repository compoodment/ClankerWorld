- [ ] In a normally generated world, watch a resident start a short scouting
  outing over a wadeable river. Record the seed, world date, agent and actual
  crossing. Catch an outing whose recorded crossing runs across the direction
  of a later bridge, rather than only along it. ([#709](https://github.com/compoodment/ClankerWorld/issues/709))
- [ ] Let normal building construction add a Road bridge, or normal crossing
  traffic add a bridge, while that outing or its return is still active. Use
  real building costs and movement. Do not paint terrain, inject a bridge,
  replace the scout's history or reset its choices to manufacture the case. ([#709](https://github.com/compoodment/ClankerWorld/issues/709))
- [ ] Pause after the bridge appears, save, close and reopen that world. It
  should load at the same paused state without an invalid-exploration error or
  the host stopping because it cannot save. Existing scouting history and
  learned discoveries should remain. If inspecting a local checkpoint for
  this comparison, compare the actual recorded arrays before and after reload. ([#709](https://github.com/compoodment/ClankerWorld/issues/709))
- [ ] Resume and watch the scout return. Its next steps must respect the new
  bridge's direction and the current terrain. A legal detour, or the existing
  bounded blocked-return outcome, is acceptable; a sideways deck step,
  teleport or invented visit is not. Save and reload during the return too. ([#709](https://github.com/compoodment/ClankerWorld/issues/709))
- [ ] If ordinary play leaves a deceased agent with outing history, repeat
  save/reload after a later bridge appears at that crossing. Preserve the
  archived history and profile; a bridge built after death must not invent a
  visit by that agent. Record this case as pending if it has not occurred.
  Tell an agent the observations and failures in chat, with the build, seed and relevant world dates. A crossing that never occurred is not a passed check; this checklist does not require editing a save. ([#709](https://github.com/compoodment/ClankerWorld/issues/709))
