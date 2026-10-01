---
title: Need wording comparison
type: prototype-report
status: active
updated: 2026-10-01
---

# Need wording comparison

On 1 October the owner agreed that model requests should describe needs in
words on a stated scale instead of exact numbers
([#646](https://github.com/compoodment/ClankerWorld/issues/646)), and that the
words must be compared with today's numbers in a controlled comparison before
they become the default. [#672](https://github.com/compoodment/ClankerWorld/issues/672)
built the words and this comparison. **Only the offline half has been run.** It
cannot show whether words change a model's choices, so play still sends numbers.
[How it works](how-it-works.md#model-inputs-usage-and-memories) describes the
two request formats.

## What is compared

Each comparison runs the same worlds twice through the real personal-model
adapter. The only difference is how the request shows fullness, warmth and
illness:

| Numbers (today's default) | Words |
| --- | --- |
| `"hunger_basis_points": 4500` | `"fullness": "fine (starving, hungry, fine, full; starving is worst, full is best)"` |
| `"warmth_basis_points": 5500` | `"warmth": "chilly (freezing, chilly, warm; freezing is worst, warm is best)"` |
| `"illness_basis_points": 0` | `"illness": "well (very ill, ill, unwell, well; very ill is worst, well is best)"` |

In the numbers arm the instructions also explain the 0 to 10,000 scales; the
words arm says instead that each need gives its level and then the whole scale.

## Method

`NeedWordingComparisonTests.ReportNeedWordingComparison` uses the normal
private-world decision and survival code, like the
[survival priority prototype](survival-priority-prototype.md). Three
compatibility-map worlds use seeds `need-wording-0`, `-1` and `-2` with fixed
clear, rain and storm weather; regional weather episodes are off. After three
setup ticks, every agent starts at 45% fullness and 55% warmth. Each arm then
runs the same number of ticks. Ticks wait for every decision, so a slow model
cannot change the world, and up to four agents' requests are sent at once.

The report gives, per weather and arm:

- **Survival choices**: the share of accepted choices that were eating,
  getting food, warmth or clothing.
- **Time under a survival choice**: the share of living agent-ticks whose
  latest accepted choice was a survival choice. It is an estimate of time
  spent on survival, since an agent can finish an action before choosing again.
- **Food shortages**: agent-ticks below 20% fullness (starving) and below 40%
  (hungry or worse), and the household food stock at the start, its lowest
  point and the end.
- **Cold and illness**: agent-ticks below 35% warmth, agent-ticks at 25%
  illness or more (unwell or worse), and the highest illness reached.
- Meals, deaths, failed calls, average request size and reported tokens.

Each final world is saved and loaded again as a check.

## Offline results

Offline, a stand-in answers every request with the built-in choice. It takes
that choice from the agent's state, never from the request text, so both arms
make exactly the same choices by design. This run shows that the words reach
the model through the real adapter for a whole run without errors or exact
values, that they never change the built-in path, and what they cost in request
size. **It says nothing about how a model reads the words.**

Run on 1 October on `3503dba` (main `8e7e1c7` plus this change). Both arms
matched in every column but request size, so each row covers both:

| Ticks | Weather | Decisions | Survival choices | Time under a survival choice | Starving | Hungry or worse | Freezing | Unwell or worse | Peak illness | Food | Meals | Deaths |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| 360 | Clear | 119 | 9.2% (11) | 3.6% | 0 | 234 | 0 | 0 | 0% | 32/28/28 | 4 | 0 |
| 360 | Rain | 169 | 43.2% (73) | 45.1% | 0 | 30 | 0 | 0 | 0% | 32/28/28 | 4 | 0 |
| 360 | Storm | 131 | 41.2% (54) | 36.6% | 0 | 13 | 8 | 0 | 0% | 32/28/28 | 4 | 0 |
| 1,200 | Clear | 188 | 11.2% (21) | 1.3% | 0 | 234 | 0 | 0 | 0% | 32/15/15 | 8 | 0 |
| 1,200 | Rain | 299 | 46.8% (140) | 31.0% | 0 | 39 | 324 | 0 | 17.68% | 32/20/20 | 8 | 0 |
| 1,200 | Storm | 292 | 54.8% (160) | 52.5% | 0 | 77 | 1,569 | 340 | 43.72% | 32/16/16 | 8 | 0 |

Starving, hungry, freezing and unwell columns count agent-ticks. Births during
the 1,200-tick runs add agents. Every request in the numbers arm carried the
three numbers, and every request in the words arm carried all three words and
none of the numbers. No call failed. Words made each request 156 to 158
characters (about 2%) longer: 7,554 against 7,710 characters on average in the
360-tick clear run.

## Running the model-backed comparison

The model-backed run makes paid calls with the owner's key, so it only runs
when asked. It needs an OpenAI-compatible chat-completions endpoint, a model
name and a key in environment variables. OpenAI's endpoint is shown below;
Ollama Cloud's is `https://ollama.com/v1/chat/completions`.

```bash
export CLANKERWORLD_NEED_WORDING_COMPARISON=model
export CLANKERWORLD_NEED_WORDING_ENDPOINT=https://api.openai.com/v1/chat/completions
export CLANKERWORLD_NEED_WORDING_MODEL=<model name>
export CLANKERWORLD_NEED_WORDING_API_KEY=<key>
dotnet test --configuration Release --filter "FullyQualifiedName~ReportNeedWordingComparison" --logger "console;verbosity=detailed"
```

Optional settings: `CLANKERWORLD_NEED_WORDING_TICKS` (default 360),
`CLANKERWORLD_NEED_WORDING_RUNS` (repeats of each weather and arm, default 1)
and `CLANKERWORLD_NEED_WORDING_MAX_CALLS` (default 1,500). Once the call limit
is reached nothing more is sent and the report fails rather than mixing
fallback choices into the results. `CLANKERWORLD_NEED_WORDING_COMPARISON=offline`
repeats the offline run. The key is read when each call is made and is never
written to the report.

At the default length, the offline run made 419 decisions per arm, so expect
roughly 850 calls of about 2,000 input tokens each. Output tokens depend on the
model: the owner's sample in [#457](https://github.com/compoodment/ClankerWorld/issues/457)
averaged about 2,800 per call. A model's choices vary from run to run, so one
run per arm is a first look rather than proof; repeats make a difference more
convincing.

## Limits

These are short runs on a small map with four starting agents. Only personal
requests are compared; Jev's routine requests use the same fullness words but
need a separate key and are not part of this run. The test provider does not
speak in conversations, which carry no needs. Results from the model-backed
run belong in this page, with the model name, before the default changes.
