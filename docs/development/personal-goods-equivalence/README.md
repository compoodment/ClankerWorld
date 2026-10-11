---
title: Personal recovery and birth-food equivalence
type: developer-guide
status: active
updated: 2026-10-11
---

# Personal recovery and birth-food equivalence

This probe checks the personal recovery and birth-food split of
[#1370](https://github.com/ClankerWorldOrg/ClankerWorld/issues/1370).
The same [Probe.cs](Probe.cs) runs against the unchanged production baseline
`a7bd16422706852a8030cac53b6c54ed82aa1cef` and the candidate.

## Native feature comparison

Eight scenarios produced **26 matching frames** over 18 committed ticks.
Complete checkpoint bytes, ordered events, observation digests and complete
candidate lists match exactly. Every scenario also refuses a tick without
changing its checkpoint and strictly reloads every captured frame.

- Routine recovery and owner collection orders each retrieve damaged wood,
  spoiled fruit and a broken storage pot with its contents. Actual movement
  preserves personal ownership and emits one collection receipt.
- Birth reserves and consumes stored food with full caregiver cargo and a
  separate active reservation in the same pot family. The food stays in
  its House and pot. A second pot supplies the remaining household reserve.
- Breaking both pots prevents birth and preserves their food.

Generated scenarios use native geography, first-Town layout, founder placement
and Start World. The initial activation tick runs normally. Inventory,
relationship, warmth and position fixtures are explicit. Parent proposal and
acceptance run through native candidates; only the fixture clock skips idle
waiting, using `SocietyFixture.AdvanceTo`. The actual birth gate runs normally.
No birth, collection receipt, ownership transfer or provider response is
inserted into saved history, and no model service is called.

Probe source SHA-256:
`8e9ef0d61156f47b086e922cc5d2be4ee266172006b71cdbe8621f47cea54e5f`.
Raw frames, source, build/run logs, failed fixture setup attempts and the
comparison manifest are retained in `.evidence/keep/1370/` in the author's
workspace. The first setup attempts used an inactive survival fixture and an
oversized pot; neither is passing evidence.

## Repeat the comparison

Compile this unchanged source in a .NET 10 console project referencing each
checkout's `src/ClankerWorld.Simulation/ClankerWorld.Simulation.csproj`.
Pass a separate output directory as its sole argument. Compare the decompressed
`*.checkpoint.gz` bytes exactly, and compare all `*.events.json` and
`*.digests.json` arrays without removing fields or changing their order.
Each successful run prints all eight scenario names and its final summary.

The broader native tick comparison also passed: 68 frames across two seeds,
16 ticks each, in both generated and legacy worlds. Complete checkpoint bytes,
events and observation digests match. Its method is described in
[Build and test](../build-and-test.md#compare-tick-equivalence).
These bounded scenarios verify the paths they exercise; they do not claim
whole-world coverage or a Windows playtest.
