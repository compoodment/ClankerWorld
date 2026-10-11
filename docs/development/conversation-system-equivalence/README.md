---
title: Conversation-system equivalence
type: developer-guide
status: active
updated: 2026-10-11
---

# Check conversation-system equivalence

The extraction in [#1379](https://github.com/ClankerWorldOrg/ClankerWorld/issues/1379)
keeps the native conversation and marriage behavior. This probe complements the
general [tick comparison](../../../scripts/compare-tick-equivalence.sh): it makes
real agents select offered conversation actions and answer with a local provider,
so pending-turn admission and listener memories run on the normal tick path.
It makes no hosted model calls.

Run from the candidate checkout with .NET 10 and Python 3 available:

```bash
bash scripts/compare-tick-equivalence.sh /path/to/base /path/to/candidate .evidence/keep/1379/standard
bash docs/development/conversation-system-equivalence/run.sh /path/to/base /path/to/candidate .evidence/keep/1379/dialogue
```

Use a fresh output directory for each command. Both runners compile the same
probe source against each checkout and retain the commit, working-tree status,
inputs and outputs. The comparator checks every decompressed checkpoint byte,
the ordered event records and every recorded observation digest. The dialogue
probe also refuses a proposed tick at step 16 and checks that the checkpoint
stays unchanged. It fails if no public turns or observations occurred.

The default dialogue scenario is a generated Small world with seed
`town-project-real-donation` and 64 ticks. Optional arguments are comma-separated
seeds, tick count and `generated` and/or `legacy` modes. This is a deterministic
correctness check; it does not measure model latency or Windows playability.
The conversation, marriage, talk-order and marriage-order tests cover the
other native lifecycle cases, including load suspension, surname memories and
marriage ending/remarriage. The system tests enable snapshot-cache recomputation
throughout the test assembly and exercise isolated discarded state and live
position, urgent-need and budget queries.

## Extraction check on October 11

On Linux with .NET SDK 10.0.401, unchanged main
`6b95b135cfc7e8cb753af167c0769bcb3353e38c` was compared with the production source
at `9539ff1678cab322295a44e701bae80e99bb5def`. The candidate had the corrected
listener-belief assertion and these probe/docs files as working-tree changes;
no production code differed from that commit. The two commands above passed:

| Scenario | Paired frames | Result |
| --- | ---: | --- |
| General probe: two seeds, generated and legacy, 16 ticks each | 68 | All checkpoint bytes, events and digests matched |
| Local dialogue: generated world, 64 ticks | 65 | All checkpoint bytes, events and digests matched |

Each dialogue run ended with four conversations, 28 public turns, 28 beliefs
and 40 observation digests. Each refused tick preserved its checkpoint bytes.
The dialogue source SHA-256 was
`eaf265c1a3b89d0d041b28deba51b4fe043e92590c660689a460921a7f572251`.
Raw captures, run metadata and logs were retained in `.evidence/keep/1379/`.
These bounded scenarios complement the native lifecycle tests and full CI;
they do not establish a performance improvement or a Windows playtest result.
