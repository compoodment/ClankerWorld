# Contributing to ClankerWorld

ClankerWorld is an early private alpha. Contributions should make the game
easier to play, understand or maintain. This guide covers the shared workflow
for people and coding agents. [AGENTS.md](AGENTS.md) adds instructions for agents.

## Find the right starting point

| I want to… | Start here |
| --- | --- |
| Understand or try the game | [Playing](docs/playing.md) |
| See what is available and what is unfinished | [What works today](docs/what-works.md) |
| Understand the intended game | [Game design](docs/game-design/README.md) |
| Report a problem or find work | [GitHub Issues](https://github.com/compoodment/ClankerWorld/issues) |
| Understand the code and its boundaries | [Developer guide](docs/development/README.md) |
| Build, test or package the game | [Build and test](docs/development/build-and-test.md) |

The [documentation guide](docs/README.md) explains where each kind of
information belongs. Read the pages relevant to your change. There is no
separate bug list in the repository: Issues is the place for reports,
reproduction steps, progress and evidence that a fix works.

## Issues and design questions

Search existing issues before opening one. Use the matching template:

| Kind | When to use it | What to include |
| --- | --- | --- |
| **Bug** | Something behaves wrongly | What happened, what you expected, steps to repeat it, and the build or commit |
| **Implementation** | An agreed feature needs building | The goal, the design it follows, how we will know it works, and what is out of scope |
| **Decision** | An unresolved game choice needs the owner's answer | The question, options, trade-offs, a recommendation, and what is waiting on it |
| **Prototype** | A small experiment would answer a question | What to learn, the smallest useful experiment, and how to judge the result |
| **Playtest report** | You played and have observations | Build, date, what you did, and what felt good, wrong or confusing |

Write a title that describes the problem or desired result in ordinary words.
Keep each issue to one topic; a playtest report can collect observations from
one session. Link related issues instead of copying the same progress notes.
When a playtest finding needs its own fix, link the follow-up issue.

Use an `area:` label when one fits, `accessibility` for accessibility barriers,
and `gate:blocker` only for work that must finish before the current milestone.
Keep implementation, decision and prototype labels consistent with the templates.

A routine fix does not need a new design decision. Follow the agreed design and
existing behavior. If a change would settle an open game choice, use a Decision
issue or an explicit owner decision; a draft pull request does not make a
suggestion agreed.

Do not close a gameplay issue merely because an isolated test passes. State
what was checked and whether the normal game path still needs testing.
Never include keys, pairing codes, private saves or raw model-service payloads.

## Organize the work

For work with several steps, keep a short plan and work through it in a clear
order. Keep related code, docs and checks together. Avoid mixing unrelated
changes, starting competing fixes for the same issue, or leaving unexplained
files and temporary notes in the repository.

Use the related issue and PR to record progress, decisions and remaining work.
Before handing work over, state what changed, what was checked and what still
needs doing. Keep that record concise and current rather than opening a second
tracker in Markdown.

## Prepare a pull request

1. Link the issue, or explain the purpose if no issue is needed.
2. Read the relevant game-design, current-feature and developer pages.
3. Check open pull requests for overlapping work. Name any overlap in your
   description and agree an integration order before stacking on an unmerged
   branch. Preserve other contributors' changes.
4. Keep the pull request to one concern. Include the docs and tests needed for
   that concern; leave unrelated cleanup for a separate change.
5. Use the [pull request template](.github/pull_request_template.md). Explain
   the result for someone who has not read the conversation. Mark sections
   that do not apply rather than removing them.

Use a short title about the effect, such as
`Show fullness instead of hunger on the agent panel`.

### Drafts and readiness

Open completed implementation PRs ready for review. Use draft status only for
unfinished work, proposals awaiting a design decision, or an explicit request
for a draft. Say what needs to happen before each draft becomes ready, and
mark it ready when that work is complete.

Routine hands-on playtesting by computment can happen after merge during this
private alpha. Missing a native Windows playtest, in-game tuning session or
preview-latency measurement is not by itself a reason to keep completed work
in draft or block its merge. Record what was and was not checked in the PR,
and track problems found during playtesting in Issues. Do not claim that
unperformed checks passed.

This does not waive green CI, independent review, relevant automated safety
and compatibility checks, or a specific pre-merge check explicitly required
by the owner or reviewer. Known correctness, security or data-loss problems
introduced by the change must still be resolved before merge. Release gates
remain separate.

Waiting for review is normal ready-for-review status, not a reason for a
draft. For dependent PRs, name the integration order and hold the dependent
merge until its prerequisite lands; a completed dependent PR can still be
ready for review.

### Change and verification rules

- Follow the [code boundaries](docs/development/how-it-works.md#core-rules).
  The server decides world changes, and model output is untrusted input.
- Keep credentials and private data out of code, saves, logs and reports.
- Runtime changes must remain diagnosable. Follow the
  [logging rules](docs/development/how-it-works.md#runtime-logging).
- Changes to saved state, events or replay need the relevant
  [compatibility and replay checks](docs/development/saves-and-replay.md).
- Add or update meaningful tests for behavior changes. Wording and docs changes
  do not need new tests; update existing checks when their contract changes.
- Run the applicable [build and test checks](docs/development/build-and-test.md#which-checks-to-run).
  Put commands and results in the PR, including anything that could not run.
  A passing export is different from a Windows playtest.

### Keep documentation and the changelog useful

Update the affected documentation in the same change when behavior, controls,
operations, compatibility or intended scope changes. Keep one home for each
subject and link to it. Use [what works today](docs/what-works.md) for a concise
feature summary and Issues for detailed failures, progress and repair evidence.

Only call a feature available in the game once it is connected to the normal
private-world and Godot play path. A data type, test fixture or passing unit
test can support a technical claim, but cannot establish playability. State
when deployment or hands-on verification is still pending.

Add a plain-English entry under **Unreleased** in [CHANGELOG.md](CHANGELOG.md)
for player-visible gameplay, UI, world-runtime, save-compatibility, deployment,
packaging or security changes. Skip refactors, tests and docs-only edits unless
they change an explicit supported behavior or operational promise. Preserve
other PRs' entries when resolving conflicts.

### Review and merge

- CI must be green.
- Have someone other than the branch author review the exact diff before merging.
- Squash-merge with the PR title as the commit subject.
- Delete the branch after merging.
- Versions and tags follow the [release policy](docs/development/releasing.md).
  A merged change does not automatically need a release.

## Writing clearly

These rules apply to documentation, issues, PR descriptions and game text.

- **Use plain English wherever possible.** Start with what a person wants to
  know, what happened, or what they can do next.
- Use familiar words and consistent names: *agent*, *model*, *key*, *Town* and
  *House*. Explain an unfamiliar term when it is needed.
- Prefer short paragraphs, descriptive headings and concrete examples.
  Use a list or table when it makes choices or steps easier to compare.
- Keep agreed design, suggestions and current behavior clearly distinguished.
  Do not turn a preference into a promise or a test result into a claim of
  successful playtesting.
- Keep necessary technical detail in the developer pages. Exact commands,
  field names, compatibility versions and security requirements must remain
  precise; explain their purpose before the detail.
- Avoid internal progress labels and implementation jargon in introductions
  and player guides. Link to the deeper explanation rather than repeating it.

### Writing player-facing text

Buttons, tooltips, hints, errors and event-log lines should say what happened
and what the player can do.

- Keep a tooltip to one sentence and a hint to two when possible. Put longer
  explanations in optional help.
- Avoid terms such as *cognition*, *tick*, *revision*, *atomic*, *signed request*
  and *provider role*. Text only for developers can remain technical behind
  **Developer tools**.
- Be kind about failures: explain what went wrong, whether anything was lost,
  and the next useful action.
- Make numbers readable and their direction clear: **Fullness 40%** when 100%
  means full.
- Use sentence case rather than all-caps labels.

| Instead of | Write |
| --- | --- |
| `submitting one-use signed owner request…` | `Sending…` |
| `disconnected · holding accepted tick 5040` | `Connection lost · showing the world as it was at 12:30` |
| `Deterministic (fallback) · take it easy` | `The model did not provide a usable choice. Built-in rules chose to take it easy.` |
| `Inspect ancestry and partnerships, including deceased relatives.` | `See their family, including those who have passed.` |

## Commits, security and conduct

Use an imperative commit summary of about 70 characters or fewer. Explain why
in the body when needed, and use `Fixes #123` only when the issue is actually
resolved; otherwise use `Refs #123`.

Report vulnerabilities privately to the repository owner. Never include
credentials, pairing material or private saves in a report.

Be respectful and assume good faith. Disagree with the design rather than the
person, and use concrete evidence to explain concerns.
