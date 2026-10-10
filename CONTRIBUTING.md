# Contributing to ClankerWorld

ClankerWorld is an early private alpha. This page holds the rules every
contributor needs, person or coding agent, whatever their job.
[AGENTS.md](AGENTS.md) adds what is specific to coding agents. Each job's own
steps are in its skill, and they bind everyone doing that job just as this
page does:

| Job | Read |
| --- | --- |
| Find bugs | [find-bugs](skills/find-bugs/SKILL.md) |
| Fix issues | [fix-issue](skills/fix-issue/SKILL.md) |
| Review and merge | [review-merge](skills/review-merge/SKILL.md) |
| Record the owner's design decisions | [design-decisions](skills/design-decisions/SKILL.md) |

The [documentation guide](docs/README.md) points to the right page:
[playing](docs/playing.md), [what works today](docs/what-works.md),
[game design](docs/game-design/README.md) or the
[developer guide](docs/development/README.md).

## Issues and design questions

[GitHub Issues](https://github.com/compoodment/ClankerWorld/issues) is the only
place for bugs, work, experiments and open decisions; there is no bug list in
the repository. Search open and closed issues before opening one. If a closed
issue's fix did not work, reopen it with the new evidence instead of opening a
duplicate, and if an older open issue covers the same work, update it.
Otherwise use the matching template:

| Template | Use it when |
| --- | --- |
| **Bug** | Something behaves wrongly. Say what happened, what you expected, how to repeat it and the build. |
| **Implementation** | Agreed work needs building. Say the goal, the design it follows, how we will know it works and what is out of scope. |
| **Decision** | A game choice needs the owner's answer. Give the options, trade-offs and a recommendation. |
| **Prototype** | A small experiment would answer a question. Say what to learn and how to judge the result. |

- One topic per issue, with a title in ordinary words. Link related issues
  instead of copying progress notes between them.
- Prefer fewer, clearer issues. A problem you found, in the game or in code or
  tests, gets its own Bug issue. A merged change that still needs a hands-on
  check does not: its pull request adds a [playtest list](playtest/README.md)
  file instead ([how](#close-issues-when-the-work-merges)).
- An issue filed through the API or an agent's GitHub tools still uses its
  template's sections, and gets no labels unless you add them. An
  Implementation issue says under **Saves, replay and docs** which pages the
  change will update and which sentences it makes false.
- computment reads chat, not GitHub comments. An agent that needs their answer
  asks in its chat reply and records the answer afterwards
  ([how](AGENTS.md#ask-the-owner-in-chat)).
  Review and merge sessions are the exception for decisions found during
  review: they record the question on the pull request, hand it back and move
  on without asking in chat or waiting
  ([how](skills/review-merge/SKILL.md#hand-back-or-close)). They leave ready
  pull requests labelled `status:needs-decision` for the owner to answer when
  they choose to. Security reports and questions about a direct owner request
  in that same session still go to the owner in chat.
- The owner reports playtests in chat. Record the result in the
  [playtest list](playtest/README.md), with linked Bug or Implementation issues
  labelled `from:playtest` and the build details the owner gave. Do not open a
  separate report issue.
- A routine fix follows the agreed design and existing behavior. A change that
  would settle an open game choice needs a Decision issue or an explicit owner
  decision. An open pull request, draft or not, does not make a suggestion
  agreed or a feature available.
- Never include keys, pairing codes, private saves or raw model-service
  payloads. Never describe a security problem publicly
  ([Security problems](#priorities)).

### Labels

Every open issue has **one type**, **one or two areas**, **a priority** and,
while someone works on it or it waits on something, **a status**. Whoever
changes an issue's situation updates its labels in the same step. Use only the
labels in [`.github/labels.json`](.github/labels.json), and change them by
editing that file; [Labels](docs/development/labels.md) explains how to rename
one and what the label workflows do on their own. Never create a label by hand.

| Kind | Labels |
| --- | --- |
| Type | `type:bug`, `type:feature` (agreed work), `type:decision`, `type:experiment`, `type:docs`, `type:tooling` (CI, tests, build, refactors and how-we-work changes) |
| Area | `area:agents` (models, memories, personality, families, conversations), `area:towns` (buildings, households, land, work, trade), `area:world` (map, terrain, weather, plants, survival, time), `area:saves`, `area:interface` (screens, controls, art), `area:server` (host, pairing, keys, deployment), `area:tooling` (CI, tests, build, workflow) |
| Priority | `priority:p0` to `priority:p3`; see [Priorities](#priorities) |
| Issue status | `status:needs-pr` (agreed and unblocked; only on issues that need a pull request, never on decisions or owner tasks), `status:in-progress` ([claimed](#claims)), `status:has-pr` (a pull request closes it), `status:parked` (closed for a later stage) |
| Pull request status | `status:needs-review` (ready for review), `status:reviewing` ([claimed by a reviewer](#claims)) |
| Either | `status:needs-decision` (waiting on the owner), `status:blocked` (waiting on something its description names, such as `Blocked by #123`) |
| Other | `owner-task` (only computment can do it), `owner-priority` (the owner chose this priority), `regression`, `from:playtest`, `accessibility` |

Add by hand: every label on an issue created without a form; on other new
issues, its areas, a priority if it is not P2 and any status that applies;
`status:needs-pr` only when nothing is left to decide; the other statuses when
they become true; and `status:parked` when closing agreed work for later. The
workflows set a pull request's priority, type, areas and `status:needs-review`,
and move `status:has-pr`, `status:needs-pr` and `status:in-progress` on the
issues it closes; claim, blocked and decision labels stay manual.

### Priorities

| Priority | Means | Fixing agents |
| --- | --- | --- |
| `priority:p0` | Broken now: a crash, lost or damaged saves or keys, a security problem, or the game can't be played or playtested. Also every change to how we work | Take it before any other issue. If you hold another claim, push that work and hand the claim back first |
| `priority:p1` | Next up: hurts normal play, or needed for the next playtest | Take before any P2 or P3 |
| `priority:p2` | Normal: agreed features and ordinary bugs. The default | In order, oldest first |
| `priority:p3` | Polish: cosmetic issues, edge cases, nice-to-haves | Only when no higher issue is waiting |

- **Priorities decide what to pick up next.** They never hold back finished
  work, and other jobs don't switch: a bug finder files a P0 and tells the
  owner in chat but fixes it only if asked, and a reviewer finishes its current
  review and takes P0 pull requests next.
- **Bugs:** a crash, data loss or unplayable game is P0; wrong in normal play
  is P1; an edge case is P2; cosmetic is P3. A `regression`, something that
  used to work, goes one level higher.
- **Security problems:** a problem someone could exploit now, such as reading
  keys or private saves, getting past pairing or controlling a host, is P0.
  The repository is public, so never describe it in an issue, pull request or
  comment. Agents tell the owner in chat ([how](AGENTS.md#ask-the-owner-in-chat))
  and open an issue titled `Security report waiting for the owner`, with only
  `type:bug`, `owner-task`, `priority:p0` and one area, and no details. The
  owner decides in chat how it is fixed. Hardening ideas with no working
  exploit are ordinary issues.
- **Features, experiments and decisions** start at P2. They move to P1 for a
  playtest only when the owner says in chat that the next playtest needs them.
  Add `owner-priority` and a comment naming that playtest.
- **How we work:** changes to CI, labels, templates, Claude Code settings
  (`.claude/`), agent skills (`skills/` and the `.agents/` links to it),
  CONTRIBUTING, AGENTS or CLAUDE.md affect every agent, so they are P0.
- **At most 10 open P0 and 20 open P1 issues.** Count issues only, not pull
  requests. When a level is full, the least urgent issue there that the owner
  did not pick, counting the new one, goes down a level; between equals, the
  newest goes down. If the owner picked every other issue at that level, the
  new one goes down, and you say so in your chat reply.
  Record the cap demotion in a comment, such as "Moved to P1: P0 is full".
  While the higher level is full, do not move it back solely for severity.
  When an issue at that level closes, whoever closes it restores the oldest
  cap-demoted issue to that level.
- **Anyone may set or change a priority by these rules without asking the
  owner.** Change one only when the rules call for it, and add a one-line
  comment saying why. When the owner picks a priority, add `owner-priority`
  and say so in that comment. Nobody else moves an owner-picked priority,
  even to make room.

## Claims

Several people and agents work at once, often from the same GitHub account. A
claim says who holds a piece of work, so two of them never do the same thing.
[fix-issue](skills/fix-issue/SKILL.md#claim-an-issue) and
[review-merge](skills/review-merge/SKILL.md#claim-a-pull-request) give the
steps.

- **An issue claim** is `status:in-progress` plus a signed comment naming the
  branch. **A review claim** is `status:reviewing` on a ready pull request plus
  a signed comment naming the commit the review started from.
- **A claim lasts while its label is on.** Only the claimant, the release
  workflow, an owner request, or a reviewer clearing a forgotten `Refs` claim
  after merging ends one, so don't decide on your own clock that a claim has
  lapsed. Another session's claim is taken over only when the owner asks
  ([how](AGENTS.md#take-over-work-only-when-the-owner-asks)).
- **One claim at a time.** A fixing session holds one issue claim, except for
  combined work covered by one pull request or stack. A reviewing session holds
  one review claim, except for a second while the first waits on CI or in the
  merge queue. A session's subagents share its claims.
- **Only pushes keep a claim.** Every 30 minutes a workflow releases any claim
  with nothing pushed for 1.5 hours since its label was added or its last push.
  Comments, edits and other label changes do not count. Pushes to ready pull
  requests count for their review claims, not for issue claims. Removing and
  re-adding the label to restart the clock is not allowed. An issue claim waiting on the owner
  (`status:needs-decision` on the issue or its draft) is kept; a review claim
  is not.

## Pull requests

### Link issues from the pull request

GitHub closes an issue when a merged pull request's description names it with a
closing keyword, and only exact wording works.

- `Closes #123` for each issue the pull request completes, one keyword directly
  before each number: `Closes #12, closes #13`. `Fixes` and `Resolves` work the
  same way. A draft closes nothing, so write `Closes` from the start rather
  than `Refs` as a placeholder.
- `Refs #123` for an issue it relates to or only partly completes; list
  several as `Refs #12, #13`.
- Other words close nothing: `Implements #123`, `Addresses #123` and
  `Part of #123` only mention the issue. `Fixes #12 and #13` closes only #12,
  and a bare number on the template's Closes line closes nothing.
- A keyword in a negative sentence still counts: "this does not fix #123"
  closes #123. Write `Refs #123` instead.

### Drafts and readiness

Open a pull request as a **draft** and keep it there while you are working on
it. Mark it **ready for review** only when it is finished: every change pushed,
the checks passing, the description final with `Closes` for every issue it
completes, and nothing left to ask the owner.

- **Waiting on the owner means draft.** If the pull request needs an owner
  decision, add `status:needs-decision`, ask in chat, and keep it a draft until
  every answer is in. Reviewers follow the exception above: they record the
  decision on the pull request and hand it back without asking in chat.
- **The owner may ask for a draft.** If computment asks for a pull request to
  stay a draft, keep it one; whoever continues it marks it ready only when the
  owner says so ([how](skills/fix-issue/SKILL.md#drafts-and-readiness)).
- **Ready means handed over.** From then on a reviewer owns the branch, and
  the author stops pushing. Main moving on is never a reason to take a pull
  request back. Before a reviewer has claimed it, the author may convert it
  back to draft to fix a mistake in their own change
  ([how](skills/fix-issue/SKILL.md#drafts-and-readiness)).
- **Only a hand-back sends a claimed pull request to draft.** A reviewer who
  needs something they can't do, such as an owner decision or a redesign,
  hands it back ([how](skills/review-merge/SKILL.md#hand-back-or-close)).
- **Waiting for review or another pull request is not a reason to stay a
  draft.** If it must wait for a pull request it is not stacked on, write
  `Waits on #A` in its description and add `status:blocked`. A stacked pull
  request needs no label: it stays out of the review search until GitHub moves
  it to main.

### Who reviews and merges

Here a **session** is one top-level Claude Code or Codex conversation together
with every subagent or worker it starts. A pull request's **authors** are the
sessions that pushed to it while it was a draft; a reviewer's commits under a
review claim do not make it an author. The **reviewer** is a session that is
not an author, and normally merges, following
[review-merge](skills/review-merge/SKILL.md). An author merges only when the
owner explicitly asks and a session that is not an author has already reviewed
the current head, or to revert a commit that broke main. A merged change does
not need a release; see the [release policy](docs/development/releasing.md).

- **Pull requests merge only through GitHub's merge queue,** which tests each
  batch on top of main before merging it
  ([how](skills/review-merge/SKILL.md#add-it-to-the-merge-queue)).
- **If main's CI breaks,** the session that merged the first failing commit
  reverts or fixes it as a P0. Until main is green, nobody adds other pull
  requests to the queue, merges main into them or copies the pending fix:
  wait for it to merge.
- **Never delete a branch by hand** while open pull requests target it: GitHub
  closes them. GitHub deletes a merged pull request's branch itself.

### Close issues when the work merges

An issue is done when the pull request that makes its change merges. Closing it
does not claim the change was playtested; the pull request records what was and
was not checked.

- **Playtesting after merge** does not keep an issue open. If a hands-on check
  is still wanted, add a file to the [playtest list](playtest/README.md) in the
  same pull request. If the check fails the same way, reopen the issue; if
  something else is wrong, open a new bug that links back.
- **Partial work:** use `Refs` and comment on the issue with what was done and
  what remains. If the remainder is really separate work, open a new issue and
  close the original.
- **Replaced issues:** carry over anything useful, then close the older issue
  as a duplicate of the new one.
- **Decisions:** the pull request that records the owner's answer in the game
  design, or in the workflow rules for a workflow decision, closes the
  Decision issue, takes the question off every page that still lists it as
  open, and links or opens the implementation issues
  ([how](skills/design-decisions/SKILL.md)). Implementation may start once the
  owner explicitly approves the design, including in chat. The design pull
  request takes the highest priority of the issues it unblocks, and reviewers
  take it first. Each dependent implementation pull request writes
  `Waits on #<design PR>` in its description and merges only after that design
  pull request.
- **Not needed:** close as *not planned* with a one-line reason. Check the open
  `status:blocked` issues that name it: park them too if they cannot happen
  without it, or comment with what they now wait on.

## Every change

These rules hold for every pull request, whoever writes it, and reviewers check
them.

- **Keep to one concern.** Keep related code, docs and checks together. Do not
  mix unrelated changes, start a competing fix for claimed work, or leave
  scratch files in the repository. Record progress, decisions and what remains
  on the issue and pull request, not in a second tracker.
- **Plan and leave a trail.** For work with several steps, keep a short plan
  and update it as you learn more. If you cannot finish, push your branch and
  say what remains where the next person will look.
- **Fill in the [pull request template](.github/pull_request_template.md)** for
  someone who has not read the conversation, with a short title about the
  effect, such as `Show fullness instead of hunger on the agent panel`.

### Change and verification rules

- Follow the [code boundaries](docs/development/how-it-works.md#core-rules):
  the server decides world changes, and model output is untrusted input.
- Keep credentials and private data out of code, saves, logs and reports, and
  keep runtime changes diagnosable ([logging rules](docs/development/how-it-works.md#runtime-logging)).
- Changes to saved state, events or replay need the
  [replay checks](docs/development/saves-and-replay.md). During alpha an older
  save may stop loading; do not write migration or old-save code to keep one working.
- A save-format, schema or other version number in an unmerged pull request is
  provisional, and nobody reserves one, in a comment or anywhere else: it goes
  to whichever pull request merges first
  ([how to move yours](skills/review-merge/SKILL.md#review-the-change)).
- Add meaningful tests for behavior changes, and update existing checks when
  their contract changes. Wording and docs changes need no new tests.
- Keep each test quick, ideally under a minute on CI: every merge waits for a
  full CI run, and no run finishes before its slowest test. If CI warns about a
  slow test you added or changed, make it faster before marking the pull
  request ready ([how CI runs](docs/development/build-and-test.md#how-ci-runs)).
  If it warns that tests got slower than on main, find out why first: tests
  that slow down without changing usually mean a slower simulation. Fix it, or
  say in the description why the extra time is needed.
- Run the applicable [build and test checks](docs/development/build-and-test.md#which-checks-to-run)
  and put commands and results in the PR, including anything that could not
  run. A passing export is not a Windows playtest.
- Routine playtesting by computment can happen after merge during the alpha,
  so a missing Windows playtest, tuning session or latency measurement does not
  by itself block a merge. Record what was and was not checked, add a
  [playtest list](playtest/README.md) file when a hands-on check is still
  wanted, and never claim an unperformed check passed. This does not waive
  green CI, independent review, safety checks, a check the owner or reviewer
  asked for, or fixing correctness, security or data-loss problems the change
  introduces. Release gates stay separate.

### Keep documentation and the changelog useful

Update the affected docs in the same change, keeping one home per subject and
linking to it. Only call a feature available once it works on the normal
private-world and Godot play path; a test or data type alone does not make it
playable. Put detailed unresolved findings in Issues, and link them from a page
only when that helps a reader understand a limitation. If a page moves, update
the links to it; the [documentation checks](docs/development/build-and-test.md#focused-documentation-checks)
fail on any local link or heading link you miss.

**Find what your change made false.** A change that builds, removes or decides
something makes older sentences elsewhere wrong: a "not built yet",
"unfinished", "cannot yet" or "Still to decide" line in another paragraph, on
another page or in a game-design chapter. Writing about the new behavior is
not enough. Before you mark the pull request ready, run
`node scripts/find-stale-docs.js <names>` with the names a reader would use
for what you changed, such as the feature, building, item, order, setting or
class, and read every place it lists. Fix each sentence your change made
false, on any page, in the same pull request, and write the names you
searched in the pull request.

**Game-design pages describe the design.** A game-design chapter says what the
game is meant to do. What the build does today belongs in
[what works today](docs/what-works.md) and the developer pages, so don't write
that something is "not built yet" or "unfinished" in a game-design chapter;
the documentation checks refuse it.

Add a plain-English changelog entry for player-visible gameplay, UI,
world-runtime, save-compatibility, deployment, packaging or security changes.
Skip refactors, tests and docs-only edits unless they change an explicit
supported behavior or operational promise. If your change makes a waiting
entry wrong, edit or delete it in the same pull request.

Put the entry in a new file in [changes/](changes/README.md) rather than editing
[CHANGELOG.md](CHANGELOG.md), so parallel pull requests do not conflict. Start
the file with a `- ` bullet. Name the file after the issue number and the
change, such as `431-orchard-harvest.md`. When there is no issue, name it after
your branch without the `claude/` or `codex/` prefix, such as
`settings-redesign.md`. Put the file directly in `changes/`; entries in
subfolders are not collected. `scripts/collect-changes.sh` moves the entries
into CHANGELOG.md when a release is prepared. A catch-up outside a release is
an owner request, handled by one session at a time.

## Writing clearly

These rules cover docs, issues, PR descriptions and game text.

- **Use plain English.** Start with what the reader wants to know, what
  happened, or what they can do next. Prefer short paragraphs, clear headings,
  lists and concrete examples.
- Use consistent names: *agent*, *model*, *key*, *Town* and *House*. Explain an
  unfamiliar term when it is needed.
- Keep agreed design, suggestions and current behavior clearly apart. Do not
  turn a preference into a promise, or a test result into a playtest claim.
  When you reorganize design notes, keep their agreement labels and open
  questions.
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
Report vulnerabilities privately, never in an issue, pull request or comment
([Security problems](#priorities)): agents tell the owner in chat, and people
use [private vulnerability reporting](https://github.com/compoodment/ClankerWorld/security/advisories/new),
without credentials, pairing material or private saves. Be respectful, assume
good faith, and argue with evidence about the design rather than the person.
