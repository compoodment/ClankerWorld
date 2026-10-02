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
built the words and this comparison. In the
[model-backed run](#model-backed-results), words did worse than numbers with
GLM 5.3 Flash, so play keeps sending numbers.
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

Run on 1 October on `3503dba` (main `8e7e1c7` plus this change) and again
after merging main `69ff32d`, with the same results. Both arms matched in every
column but request size, so each row covers both:

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

## Model-backed results

Run on 1 and 2 October on this branch after merging main (with #615), at 360
ticks, one run per weather and arm. Starving, hungry, freezing and unwell
columns count agent-ticks. The model-backed run does not measure request size.

### GLM 5.3 Flash on Ollama Cloud (`glm-5.3-flash`)

| Weather | Needs as | Decisions | Survival choices | Time under a survival choice | Starving | Hungry or worse | Freezing | Unwell or worse | Peak illness | Food | Meals | Deaths | Failed calls | Input/output tokens |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: | --- |
| Clear | Numbers | 116 | 9.5% (11) | 1.5% | 0 | 12 | 0 | 0 | 0% | 32/32/36 | 4 | 0 | 0 | 165,389/52,325 |
| Clear | Words | 128 | 9.4% (12) | 1.3% | 0 | 193 | 0 | 0 | 0% | 32/32/44 | 4 | 0 | 0 | 183,448/63,221 |
| Rain | Numbers | 146 | 41.8% (61) | 51.7% | 0 | 124 | 155 | 0 | 0% | 32/28/32 | 4 | 0 | 1 | 211,832/70,067 |
| Rain | Words | 94 | 57.4% (54) | 59.0% | 0 | 394 | 461 | 0 | 0% | 32/32/41 | 3 | 0 | 0 | 142,258/38,929 |
| Storm | Numbers | 154 | 24.0% (37) | 33.3% | 0 | 77 | 12 | 0 | 0% | 32/28/36 | 4 | 0 | 1 | 213,092/68,018 |
| Storm | Words | 84 | 41.7% (35) | 39.3% | 0 | 87 | 243 | 0 | 0% | 32/32/40 | 4 | 0 | 0 | 115,844/31,420 |

**Words did worse with GLM 5.3 Flash.** Nobody starved, fell ill or died in
either arm, but with words agents spent 704 agent-ticks freezing against 167
with numbers, and 674 hungry or worse against 213. They picked survival
choices more often yet still stayed cold and hungry for longer, and made fewer
decisions in rain and storm. Two calls failed, both in the numbers arm.

## Running the model-backed comparison

The owner has chosen how this is settled
([#672](https://github.com/compoodment/ClankerWorld/issues/672)): a later
session given the owner's keys runs it at the default length with two models,
**GLM 5.3 Flash** on Ollama Cloud and **GPT 6 Luna** on OpenAI. The words
become the default if they do no worse than numbers.

The run makes paid calls, so it only starts when asked. Set
`CLANKERWORLD_NEED_WORDING_PROVIDER` to `ollama` or `openai` to use that
service's chat-completions endpoint, or give any OpenAI-compatible endpoint in
`CLANKERWORLD_NEED_WORDING_ENDPOINT`. The key comes from
`CLANKERWORLD_NEED_WORDING_API_KEY` when it is set; otherwise from
`OLLAMA_API_KEY` for `ollama.com` or `OPENAI_API_KEY` for `api.openai.com`, the
variables the owner has set up for new sessions. The key is read when each
call is made and is never written to the report. Each command below is one
model's whole comparison, run from the repository root.

GLM 5.3 Flash on Ollama Cloud:

```bash
CLANKERWORLD_NEED_WORDING_COMPARISON=model \
CLANKERWORLD_NEED_WORDING_PROVIDER=ollama \
CLANKERWORLD_NEED_WORDING_MODEL=glm-5.3-flash \
dotnet test --configuration Release --filter "FullyQualifiedName~ReportNeedWordingComparison" --logger "console;verbosity=detailed"
```

GPT 6 Luna on OpenAI. Read its exact model ID from the model list first; the
game's own list calls it `gpt-6-luna`:

```bash
curl -s https://api.openai.com/v1/models -H "Authorization: Bearer $OPENAI_API_KEY" | grep -o '"id": *"[^"]*luna[^"]*"'
CLANKERWORLD_NEED_WORDING_COMPARISON=model \
CLANKERWORLD_NEED_WORDING_PROVIDER=openai \
CLANKERWORLD_NEED_WORDING_MODEL=<model ID from the list> \
dotnet test --configuration Release --filter "FullyQualifiedName~ReportNeedWordingComparison" --logger "console;verbosity=detailed"
```

If Ollama Cloud refuses `glm-5.3-flash`, check the name its model list gives
(`https://ollama.com/v1/models` with the same key).

Optional settings: `CLANKERWORLD_NEED_WORDING_TICKS` (default 360),
`CLANKERWORLD_NEED_WORDING_RUNS` (repeats of each weather and arm, default 1)
and `CLANKERWORLD_NEED_WORDING_MAX_CALLS` (default 1,500 per model run). Once
the call limit is reached nothing more is sent and the report fails rather than
mixing fallback choices into the results. A failed call falls back for that
decision and is counted in the report; many failures make the result
unreliable, so rerun. `CLANKERWORLD_NEED_WORDING_COMPARISON=offline` repeats the
offline run.

At the default length, the offline run made 419 decisions per arm, so expect
roughly 850 calls per model of about 2,000 input tokens each. Output tokens
depend on the model: the owner's sample in
[#457](https://github.com/compoodment/ClankerWorld/issues/457) averaged about
2,800 per call. A model's choices vary from run to run, so one run per arm is a
first look rather than proof; repeats make a difference more convincing.

Record each model's table here with the model ID and date, then change
`ModelNeedWords.DefaultFormat` if the words did no worse.

## Limits

These are short runs on a small map with four starting agents. The test
provider does not speak in conversations, which carry no needs.

Jev's routine requests are not part of the comparison. They use the same
words for fullness, warmth and illness, but Jev runs on TypeSafe's own
System One API with its own key and question format, not on an
OpenAI-compatible chat-completions endpoint, so neither model above nor its key
can answer them. Every decision in the comparison goes to the personal model,
including routine ones that Jev would take in play when it is switched on.
