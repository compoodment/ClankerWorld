# Contributing to ClankerWorld

ClankerWorld is an early private alpha. This is the shared workflow for people
and coding agents; [AGENTS.md](AGENTS.md) adds what each agent job does. The
[documentation guide](docs/README.md) points to the right page:
[playing](docs/playing.md), [what works today](docs/what-works.md),
[game design](docs/game-design/README.md) or the
[developer guide](docs/development/README.md).

## Issues and design questions

[GitHub Issues](https://github.com/compoodment/ClankerWorld/issues) is the only
place for bugs, work, experiments and open decisions; there is no bug list in
the repository. Search before opening one, and use the matching template:

| Template | Use it when |
| --- | --- |
| **Bug** | Something behaves wrongly. Say what happened, what you expected, how to repeat it and the build. |
| **Implementation** | Agreed work needs building. Say the goal, the design it follows, how we will know it works and what is out of scope. |
| **Decision** | A game choice needs the owner's answer. Give the options, trade-offs and a recommendation. |
| **Prototype** | A small experiment would answer a question. Say what to learn and how to judge the result. |
| **Playtest report** | You played and noticed things. Rough notes are fine. |

- One topic per issue, with a title in ordinary words. Link related issues
  instead of copying progress notes between them.
- Prefer fewer, clearer issues. Something normal playtesting will show anyway
  does not need its own issue; add it to the playtest checklist in
  [#285](https://github.com/compoodment/ClankerWorld/issues/285).
- A routine fix follows the agreed design and existing behavior. A change that
  would settle an open game choice needs a Decision issue or an explicit owner
  decision; a draft pull request does not make a suggestion agreed.
- Never include keys, pairing codes, private saves or raw model-service payloads.

### Labels

Every open issue has **one type**, **one or two areas** and, while it waits on
something, **a status**. Whoever changes an issue's situation updates its
labels in the same step. Labels are defined in
[`.github/labels.json`](.github/labels.json); edit that file to add or rename
one, and a workflow updates the repository.

| Kind | Labels |
| --- | --- |
| Type | `type:bug`, `type:feature` (agreed work), `type:decision`, `type:experiment`, `type:playtest`, `type:docs` |
| Area | `area:agents` (models, memories, personality, families, conversations), `area:towns` (buildings, households, land, work, trade), `area:world` (map, terrain, weather, plants, survival, time), `area:saves`, `area:interface` (screens, controls, art), `area:server` (host, pairing, keys, deployment), `area:tooling` (CI, tests, build) |
| Status | `status:ready` (agreed, unblocked, needs a pull request), `status:in-progress` ([claimed](#claim-an-issue)), `status:has-pr`, `status:needs-review`, `status:needs-decision`, `status:blocked` (say by what), `status:needs-playtest` (merged; waiting for a hands-on check), `status:parked` (closed for a later stage) |
| Other | `owner-task` (only computment can do it), `priority:blocker` (must finish before the current milestone), `severity:data-loss`, `regression`, `from:playtest`, `accessibility` |

- **Automatic:** the templates set the type. A pull request gets areas from the
  files it changes, a type from its ticked **Type of change** box and
  `status:needs-review` while it is ready. An issue it links with `Closes` or
  `Refs` gets `status:has-pr`, which is removed when the last such pull request
  closes; if none merged, the issue goes back to `status:ready`.
- **By hand:** areas and a status on new issues; `status:ready` only when
  nothing is left to decide; the other statuses when they become true; and
  `status:parked` when closing agreed work for later.

## Work on an issue

Several people and agents work at once, often from the same GitHub account.
These steps stop two of them fixing the same thing and make sure issues close
when their work lands.

### Find work

Ready work is agreed, unblocked, unclaimed and not waiting on the owner:

```text
is:issue is:open label:"status:ready" -label:"type:decision" -label:owner-task -label:"status:in-progress" -label:"status:blocked" -label:"status:needs-decision" -label:"status:needs-playtest" -label:"status:has-pr" -linked:pr
```

Read the issue's comments and **Development** panel first. If main already
fixes it, name the commit and close the issue.

### Claim an issue

Before you start, add `status:in-progress` and comment with who is working on
it and the branch name; assignment alone does not show which agent took it.

- Do not start a second fix for a claimed issue. If you think the approach is
  wrong, say so on the issue.
- If you stop, remove the label and comment with what you learned.
- A claim with no pull request and no update for 12 hours is stale: say you are
  taking it over, then claim it again.

### Link issues from the pull request

GitHub closes an issue when a merged pull request's description names it with a
closing keyword, and only exact wording works.

- `Closes #123` for each issue the pull request completes, one keyword per
  issue: `Closes #12, closes #13`. `Fixes` and `Resolves` work the same way.
- `Refs #123` for an issue it relates to or only partly completes.
- Other words close nothing: `Implements #123`, `Addresses #123` and
  `Part of #123` only mention the issue. `Fixes #12 and #13` closes only #12.
- A keyword in a negative sentence still counts: "this does not fix #123"
  closes #123. Write `Refs #123` instead.

### Close issues when the work merges

An issue is done when the pull request that makes its change merges. Closing it
does not claim the change was playtested; the pull request records what was and
was not checked.

- **Playtesting after merge** does not keep an issue open. If a hands-on check
  is still wanted, add `status:needs-playtest` and remove it after the check.
  If the same failure remains, reopen the issue; if something else is wrong,
  open a new bug that links back.
- **Partial work:** use `Refs` and comment on the issue with what was done and
  what remains. If the remainder is really separate work, open a new issue and
  close the original.
- **Replaced issues:** carry over anything useful, then close the older issue
  as a duplicate of the new one.
- **Decisions:** the pull request that records the owner's answer in the game
  design closes the Decision issue and links or opens the implementation issues.
- **Not needed:** close as *not planned* with a one-line reason.

## Organize the work

For work with several steps, keep a short plan and work through it in order.
Keep related code, docs and checks together. Do not mix unrelated changes,
start a competing fix (see [Claim an issue](#claim-an-issue)) or leave scratch
files in the repository. Record progress, decisions and what remains on the
issue and pull request, not in a second tracker.

## Prepare a pull request

1. Link each issue with `Closes` or `Refs`
   ([how](#link-issues-from-the-pull-request)). If no issue is needed, such as
   for a direct owner request, say so.
2. Read the relevant game-design, current-feature and developer pages.
3. Check open pull requests for overlap. Name any overlap and agree an
   integration order before stacking on an unmerged branch.
4. Keep to one concern, with the docs and tests it needs.
5. Fill in the [pull request template](.github/pull_request_template.md) for
   someone who has not read the conversation. Mark sections that do not apply.

Use a short title about the effect, such as
`Show fullness instead of hunger on the agent panel`.

### Drafts and readiness

Open completed work ready for review. Use a draft only for unfinished work, a
proposal awaiting a decision, or when asked for one, and say what must happen
before it is ready. Waiting for review, or for a prerequisite pull request to
merge, is not a reason for a draft.

Routine playtesting by computment can happen after merge during the alpha, so a
missing Windows playtest, tuning session or latency measurement does not by
itself block a merge. Record what was and was not checked, add
`status:needs-playtest` to the issue when a hands-on check is still wanted, and
never claim an unperformed check passed. This does not waive green CI,
independent review, safety checks, a check the owner or reviewer asked for, or
fixing correctness, security or data-loss problems the change introduces.
Release gates stay separate.

### Change and verification rules

- Follow the [code boundaries](docs/development/how-it-works.md#core-rules):
  the server decides world changes, and model output is untrusted input.
- Keep credentials and private data out of code, saves, logs and reports, and
  keep runtime changes diagnosable ([logging rules](docs/development/how-it-works.md#runtime-logging)).
- Changes to saved state, events or replay need the
  [replay checks](docs/development/saves-and-replay.md). During alpha an older
  save may stop loading; do not write migration or old-save code to keep one working.
- Add meaningful tests for behavior changes, and update existing checks when
  their contract changes. Wording and docs changes need no new tests.
- Run the applicable [build and test checks](docs/development/build-and-test.md#which-checks-to-run)
  and put commands and results in the PR, including anything that could not
  run. A passing export is not a Windows playtest.

### Keep documentation and the changelog useful

Update the affected docs in the same change, keeping one home per subject and
linking to it. Only call a feature available once it works on the normal
private-world and Godot play path; a test or data type alone does not make it
playable.

Add a plain-English changelog entry for player-visible gameplay, UI,
world-runtime, save-compatibility, deployment, packaging or security changes.
Skip refactors, tests and docs-only edits unless they change an explicit
supported behavior or operational promise.

Put the entry in a new file in [changes/](changes/README.md) rather than editing
[CHANGELOG.md](CHANGELOG.md), so parallel pull requests do not conflict. Name
the file after the issue number and the change, such as
`431-orchard-harvest.md`, or after your branch when there is no issue.
`scripts/collect-changes.sh` moves the entries into CHANGELOG.md when a release
is prepared, or whenever the changelog should catch up.

### Review and merge

The reviewer is not the pull request's author and normally merges. An author
merges only when the owner explicitly asks and someone else has already
reviewed the current head. Before merging, check that:

1. CI is green on the current head. If main was merged in or the branch changed
   after review, review and check the new head.
2. Someone other than the author reviewed that exact head. Record who reviewed
   which commit and what they checked, in the squash commit body or a comment.
3. The description links its issues correctly
   ([Link issues](#link-issues-from-the-pull-request)); fix it first if not.
4. Any integration order named by this or another pull request is respected.

Squash-merge with the PR title as the commit subject, delete the branch, then:

- Fetch main and confirm the squash commit is there.
- Check every `Closes` issue closed. The
  [Close fixed issues](.github/workflows/close-fixed-issues.yml) workflow closes
  any GitHub missed; if one is still open, close it with a comment naming the
  pull request and commit.
- For each `Refs` issue, check a comment says what remains, and remove
  `status:in-progress` if nobody is still working on it.

A merged change does not need a release; see the
[release policy](docs/development/releasing.md).

## Writing clearly

These rules cover docs, issues, PR descriptions and game text.

- **Use plain English.** Start with what the reader wants to know, what
  happened, or what they can do next. Prefer short paragraphs, clear headings,
  lists and concrete examples.
- Use consistent names: *agent*, *model*, *key*, *Town* and *House*. Explain an
  unfamiliar term when it is needed.
- Keep agreed design, suggestions and current behavior clearly apart. Do not
  turn a preference into a promise, or a test result into a playtest claim.
- Keep exact technical detail in the developer pages, and explain its purpose
  first. Link to deeper explanations rather than repeating them.

### Writing player-facing text

Buttons, tooltips, hints, errors and Event Log lines say what happened and what
the player can do.

- Keep a tooltip to one sentence and a hint to two. Use sentence case.
- Avoid developer terms such as *cognition*, *tick*, *revision*, *atomic*,
  *signed request* and *provider role* outside **Developer tools**.
- Be kind about failures: what went wrong, whether anything was lost, and the
  next useful action. Make numbers read clearly: **Fullness 40%** when 100% is full.

| Instead of | Write |
| --- | --- |
| `disconnected · holding accepted tick 5040` | `Connection lost · showing the world as it was at 12:30` |
| `Deterministic (fallback) · take it easy` | `The model did not provide a usable choice. Built-in rules chose to take it easy.` |

## Commits, security and conduct

Use an imperative commit summary of about 70 characters, with the reason in the
body when needed. A closing keyword in a commit that reaches main also closes
the issue, so do not write one for an issue the change does not complete.
Report vulnerabilities privately to the repository owner, without credentials,
pairing material or private saves. Be respectful, assume good faith, and argue
with evidence about the design rather than the person.
