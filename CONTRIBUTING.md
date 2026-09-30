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

The templates add the kind label: `bug`, `type:implementation`,
`type:decision` or `type:prototype`. Add these when they apply:

| Label | Meaning |
| --- | --- |
| `area:…` | The part of the game it concerns, such as `area:kernel` or `area:worldgen` |
| `accessibility` | A barrier for people with disabilities |
| `gate:blocker` | Must finish before the current milestone |
| `status:in-progress` | Someone has [claimed it](#claim-an-issue) and is working on it |
| `status:blocked` | Waiting on a decision or another issue; a comment says which |
| `status:needs-playtest` | The change is merged, but computment still wants to check it in the game |
| `owner-task` | Only computment can do it, such as a Windows playtest |

A routine fix does not need a new design decision. Follow the agreed design and
existing behavior. If a change would settle an open game choice, use a Decision
issue or an explicit owner decision; a draft pull request does not make a
suggestion agreed.

Never include keys, pairing codes, private saves or raw model-service payloads.

## Work on an issue

Several people and agents work at the same time, often from the same GitHub
account. These steps stop two of them fixing the same thing and make sure an
issue closes when its work lands.

### Find work

Issues ready for an agent are open bugs, implementation issues and, when asked,
prototypes that nobody has claimed and that are not blocked or owner-only:

```text
is:issue is:open -label:"type:decision" -label:owner-task -label:"status:in-progress" -label:"status:blocked" -linked:pr
```

Read the issue's comments and its **Development** panel before starting. If the
problem is already fixed on main, say which commit fixed it and close the issue.

### Claim an issue

Before you start, add `status:in-progress` and comment with who is working on
it and the branch name. Assignment alone does not show which agent took the
issue when several agents share one account.

- Do not start a second fix for a claimed issue. If you think the claimed
  approach is wrong, say so on the issue.
- If you stop, remove the label and comment with what you learned.
- A claim with no pull request and no update for 12 hours is stale. Say on the
  issue that you are taking it over, then claim it again.

### Link issues from the pull request

GitHub closes an issue when a merged pull request's description names it with
a closing keyword. Only exact wording works.

- Write `Closes #123` for each issue the pull request completes. Use one keyword
  per issue: `Closes #12, closes #13`. `Fixes` and `Resolves` work the same way.
- Write `Refs #123` for an issue the pull request relates to or only partly
  completes.
- Other words close nothing: `Implements #123`, `Addresses #123` and
  `Part of #123` only mention the issue. `Fixes #12 and #13` closes only #12.
- GitHub also reads a keyword inside a negative sentence, so
  "this does not fix #123" closes #123. Write `Refs #123` instead.

### Close issues when the work merges

An issue is done when the pull request that makes its requested change merges.
Closing it does not claim the change was playtested; the pull request records
what was and was not checked.

- **Playtesting after merge.** Routine hands-on checks by computment do not keep
  an issue open. If the change still needs one, add `status:needs-playtest` to
  the issue. Remove the label after the check. If the same failure is still
  there, reopen the issue; if something else is wrong, open a new bug that
  links back.
- **Partial work.** Use `Refs`, and comment on the issue with what the pull
  request did and what remains. If the remainder is really a separate piece of
  work, open a new issue for it and close the original.
- **Replaced issues.** When you open an issue that covers an older one, carry
  over anything useful and close the older issue as a duplicate of the new one.
- **Decisions.** The pull request that records the owner's answer in the game
  design closes the Decision issue, and links or opens the implementation
  issues for the agreed work.
- **Not needed.** Close it as *not planned* with a one-line reason.

## Organize the work

For work with several steps, keep a short plan and work through it in a clear
order. Keep related code, docs and checks together. Avoid mixing unrelated
changes, starting competing fixes for the same issue (see
[Claim an issue](#claim-an-issue)), or leaving unexplained files and temporary
notes in the repository.

Use the related issue and PR to record progress, decisions and remaining work.
Before handing work over, state what changed, what was checked and what still
needs doing. Keep that record concise and current rather than opening a second
tracker in Markdown.

## Prepare a pull request

1. Link each issue with `Closes` or `Refs` as described in
   [Link issues from the pull request](#link-issues-from-the-pull-request).
   If no issue is needed, such as for a direct owner request, say so.
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
add `status:needs-playtest` to the issue when a hands-on check is still wanted,
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
  During alpha, an older save may stop loading. Do not write migration or
  old-save code only to keep one working.
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

The reviewer must not be the pull request's author, and the reviewer normally
merges. An author merges only when the owner explicitly asks and someone else
has already reviewed the current head.

Before merging, check that:

1. CI is green on the pull request's current head. If main was merged in or
   the branch changed after review, review and check the new head.
2. Someone other than the author reviewed that exact head. Record who reviewed
   which commit and what they checked, in the squash commit body or a comment.
3. The description links its issues with `Closes` and `Refs` as described in
   [Link issues from the pull request](#link-issues-from-the-pull-request).
   Correct the description first if it does not.
4. Any integration order named by this or another pull request is respected.

Squash-merge with the PR title as the commit subject. After merging:

- Delete the branch.
- Fetch main and confirm the squash commit is there.
- Check that every `Closes` issue is closed. The
  [Close fixed issues](.github/workflows/close-fixed-issues.yml) workflow closes
  any that GitHub missed; if one is still open, close it with a comment naming
  the pull request and commit.
- For each `Refs` issue, check that a comment says what remains. Remove
  `status:in-progress` if nobody is still working on it.

Versions and tags follow the [release policy](docs/development/releasing.md).
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
in the body when needed. Link issues in the pull request description as
described in [Link issues from the pull request](#link-issues-from-the-pull-request).
A closing keyword in a commit that reaches main also closes the issue, so do
not write one for an issue the change does not complete.

Report vulnerabilities privately to the repository owner. Never include
credentials, pairing material or private saves in a report.

Be respectful and assume good faith. Disagree with the design rather than the
person, and use concrete evidence to explain concerns.
