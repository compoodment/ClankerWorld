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
- computment reads chat, not GitHub comments. An agent that needs their answer
  asks in its chat reply and records the answer afterwards
  ([how](AGENTS.md#ask-the-owner-in-chat)).
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
editing that file; a workflow then updates the repository. To rename a label,
change its name and add the old name to its `aliases`, so issues keep it, and
in the same pull request update the issue forms, `.github/scripts/` and the
searches here and in AGENTS.md that name it. Never create a label by hand;
retired ones, such as `status:needs-playtest`, are deleted again every six
hours.

| Kind | Labels |
| --- | --- |
| Type | `type:bug`, `type:feature` (agreed work), `type:decision`, `type:experiment`, `type:docs`, `type:tooling` (CI, tests, build, refactors and how-we-work changes) |
| Area | `area:agents` (models, memories, personality, families, conversations), `area:towns` (buildings, households, land, work, trade), `area:world` (map, terrain, weather, plants, survival, time), `area:saves`, `area:interface` (screens, controls, art), `area:server` (host, pairing, keys, deployment), `area:tooling` (CI, tests, build, workflow) |
| Priority | `priority:p0` to `priority:p3`; see [Priorities](#priorities) |
| Issue status | `status:needs-pr` (agreed and unblocked; only on issues that need a pull request, never on decisions or owner tasks), `status:in-progress` ([claimed](#claim-an-issue)), `status:has-pr` (a pull request closes it), `status:parked` (closed for a later stage) |
| Pull request status | `status:needs-review` (ready for review), `status:reviewing` ([claimed by a reviewer](#review-and-merge)), `status:merging` ([taking its turn to merge](#review-and-merge)) |
| Either | `status:needs-decision` (waiting on the owner), `status:blocked` (waiting on something its description names, such as `Blocked by #123`) |
| Other | `owner-task` (only computment can do it), `owner-priority` (the owner chose this priority), `regression`, `from:playtest`, `accessibility` |

`status:in-progress` stays next to `status:has-pr` while the claimed pull
request is a draft, and next to `status:needs-decision` while the claim waits
on the owner.

- **Automatic on issues:** issue forms filed on GitHub's website set the type
  and `priority:p2`, and the Decision form also adds `status:needs-decision`.
  An issue created through the API or an agent's GitHub tools gets no labels,
  so add them by hand. An issue a pull request closes (`Closes`, `Fixes` or
  `Resolves` directly before its number) gets `status:has-pr` in place of
  `status:needs-pr`, and loses `status:in-progress` when that pull request is
  marked ready. If the pull request goes back to draft, nobody holds the issue
  and the pull request has no `status:needs-decision` or `status:blocked`, the
  issue gets `status:needs-pr` back next to `status:has-pr`. When the last such
  pull request closes, `status:has-pr` comes off; if none merged, the issue goes
  back to `status:needs-pr`, unless it is claimed, blocked, waiting on the owner
  or parked, or is a decision or owner task. `Refs` changes no issue labels.
  Quiet claims are [released automatically](#claim-an-issue).
- **Automatic on pull requests:** a priority (the highest of the open issues it
  closes; if none has one, of the open issues it refers to; otherwise P2; P0 if
  it changes [how we work](#priorities)); one type, from the first ticked
  **Type of change** box, or `type:docs` when none is ticked and only
  documentation changes; at most two areas from the files it changes, set when
  it opens, reopens, is marked ready, is pushed to or changes base, unless
  someone has changed its areas by hand; and `status:needs-review` while it is
  ready. A priority added by hand is never removed, and wins when it is higher.
  `status:needs-review`, `status:reviewing` and `status:merging` come off when
  it closes or goes back to draft. A pull request stacked on another's branch is judged by what it
  would change on main.
- **By hand:** all labels on issues created without a form; on other new
  issues, areas, a priority if it is not P2 and any status that applies;
  `status:needs-pr` only when nothing is left to decide; the other statuses
  when they become true; and `status:parked` when closing agreed work for
  later.

### Priorities

| Priority | Means | Fixing agents |
| --- | --- | --- |
| `priority:p0` | Broken now: a crash, lost or damaged saves or keys, a security problem, or the game can't be played or playtested. Also every change to how we work | Take it before any other issue. If you hold another claim, push that work and hand the claim back first |
| `priority:p1` | Next up: hurts normal play, or needed for the next playtest | Take before any P2 or P3 |
| `priority:p2` | Normal: agreed features and ordinary bugs. The default | In order, oldest first |
| `priority:p3` | Polish: cosmetic issues, edge cases, nice-to-haves | Only when no higher issue is waiting |

- **Priorities decide what to pick up next.** They never hold back finished
  work ([Review and merge](#review-and-merge)), and other jobs don't switch: a
  bug finder files a P0 and tells the owner in chat but fixes it only if asked,
  and a reviewer finishes its current review and takes P0 pull requests next.
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
  (`.claude/`), CONTRIBUTING, AGENTS or CLAUDE.md affect every agent, so they
  are P0.
- **At most 5 open P0 and 10 open P1 issues.** Count issues only, not pull
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

## Work on an issue

Several people and agents work at once, often from the same GitHub account.
These steps stop two of them fixing the same thing and make sure issues close
when their work lands.

### Find work

Ready work is agreed, unblocked, unclaimed and not waiting on the owner:

```text
is:issue is:open label:"status:needs-pr" -label:"type:decision" -label:owner-task -label:"status:in-progress" -label:"status:blocked" -label:"status:needs-decision" sort:created-asc
```

Take the highest priority first: add `label:"priority:p0"` to the search, then
p1, p2 and p3, and take the first result, which is the oldest issue at that
level. Before you claim it, check that the work is still needed and that
nobody else is already doing it
([how](#check-for-duplicates-and-stale-work)).

If the issue also has `status:has-pr`, an earlier draft was abandoned: continue
that pull request's branch rather than starting again. If the draft also
closes other open issues that nobody holds, claim them together with yours as
combined work, naming its branch on each, so the queue does not hand them to
another session working on the same branch. Say on the pull request that you
are taking it over, fetch its latest head before each push, and never
force-push. If a push is rejected because the branch moved, read its newest
comments first: if another session has claimed the work since, stop and leave
it to them; otherwise merge the new commits in before you continue.

Merge main into an abandoned draft before anything else. If it no longer fits
current main, for example because most of it conflicts or the code it changes
has been rewritten, start a fresh branch from main instead. Carry over what
still applies, open a new draft that closes the same issues, name the new
branch in your claim comment, and close the old draft with a comment naming
the new one ([after closing without merging](#review-and-merge)).

### Check for duplicates and stale work

Many sessions file and fix issues at once, so by the time you reach an issue it
may already be done, filed twice or out of date. Before you claim an issue:

- **Read its comments and the pull requests that name it**, open or merged
  (search `is:pr <number>`).
- **Check it against current main.** Reproduce a bug on current main, in the
  game or with a test. For a feature, read the code and docs it names to see
  whether it is already there: a pull request may have done it without naming
  the issue.
- **Search open issues and pull requests by topic**, with a few words from its
  title and the features or files it names, not only by its number.

Then act on what you find:

| You find | Do this |
| --- | --- |
| Main already does it | Close the issue as completed with a comment naming the commit or pull request. If only part is done, comment with what remains and carry on with the rest. |
| A bug you can't reproduce on main | Comment with what you tried. Close it as not planned, unless it describes an intermittent failure; anyone who sees it again reopens it with the new evidence. |
| Another open issue covers the same work | Keep the one that is further along: claimed, with a pull request, or with more detail; between equals, the older one. Copy anything useful from the other into it, keep the higher priority, and close the other as a duplicate. Never close an issue another session holds; comment on both instead. |
| An open pull request already does the work | Comment on the issue and the pull request, linking them, and pick other work. If that pull request should close the issue, ask its author or reviewer in a comment to add `Closes #<number>`. |
| The description no longer matches main | Update it to current main before you start, with a comment saying what changed. If the design itself is in question, ask the owner ([how](AGENTS.md#ask-the-owner-in-chat)) and add `status:needs-decision`. |

Record what you checked in your claim comment, in a line such as "Checked
against main 1a2b3c4: still broken; no other issue or pull request covers it."

### Claim an issue

Before you start, replace `status:needs-pr` with `status:in-progress` and
comment with who is working on it and your branch name in backticks, with a
prefix such as `codex/123-fix` (the cleanup finds only names with a slash). The cleanup below finds your pushes through that name. Agents
sign the comment with a session ID ([how](AGENTS.md#sign-your-comments)).

- **One claim at a time.** Claim an issue when you start on it, not to line up
  your next job. Each session holds one issue claim; a session's subagents
  share it. The exception is combined work: when one pull request, or one stack
  of pull requests built on each other, will cover several related issues,
  claim them together, and in each claim comment name every branch of the
  stack in backticks, starting with the one you push first, so a push to any of
  them keeps all the claims. Each issue's claim ends when the pull request that
  closes it is marked ready, and you are free to claim the next issue once all
  of yours are ready.
- **Only pushes keep a claim.** Push your branch and open a draft pull request
  within the first hour, even before the work builds, then push at least every
  hour. A draft is where unfinished work belongs. Work that exists only on your
  machine is invisible to everyone else and is lost if the claim passes on.
- **Quiet claims are released automatically.** Every 30 minutes a workflow
  releases any claim with nothing pushed for 1.5 hours. What counts: adding
  `status:in-progress`, opening a linked draft, and pushes to a linked draft's
  branch or to one of the five branches most recently named in the issue's
  comments. A draft that only refers to the issue (`Refs`) counts when its
  branch is named in the issue's comments or, after one optional `prefix/`,
  starts with the issue number and a hyphen, optionally after `issue-`, such
  as `codex/123-fix` or `claude/issue-123-fix`. A push counts from when it reached GitHub, even if
  its commits are older. Comments, edits, other label changes and pushes to
  ready pull requests, which belong to their reviewers, do not count. The
  released issue goes back to `status:needs-pr`, unless it is blocked, a
  decision or an owner task, with a comment naming the draft or branch to
  continue from. A release comes up to 30 minutes after the 1.5 hours, or later
  when GitHub starts a scheduled run late.
- **A claim lasts while its label is on.** `status:in-progress` means someone
  holds the issue, however long ago they last pushed. Only the claimant, the
  release workflow or an owner request ends a claim (or a reviewer clearing a
  forgotten `Refs` claim after merging, under Review and merge), so don't
  decide on your own clock that one has lapsed. Don't remove and re-add the label to restart
  the clock; add it again only when you claim the issue afresh. Another
  session's claim is taken over only when the owner asks
  ([how](AGENTS.md#take-over-work-only-when-the-owner-asks)).
- **Waiting on the owner:** add `status:needs-decision` to the issue, or to
  your own draft, and ask in chat. That keeps the claim, and the 1.5 hours
  start again when the label comes off the issue or the draft.
- **Waiting on anything else:** if you can keep working, keep pushing.
  Otherwise add `status:blocked`, name each blocker in the issue description
  as `Blocked by #123` or describe the outside event, push what you have and
  remove `status:in-progress`. A blocked issue stays out of the queue until
  whoever finishes the blocker, or learns that the outside event has happened,
  removes `status:blocked` and adds `status:needs-pr` if nobody holds it.
- **Stopping:** push your branch, put `status:needs-pr` back in place of
  `status:in-progress` (or `status:blocked`, as above), and comment with what
  you learned and the branch name.
- **When your pull request is marked ready**, each issue it closes loses
  `status:in-progress` automatically and keeps `status:has-pr`. Issues it only
  refers to (`Refs`) keep your label, so end those claims yourself: comment
  with what remains, then replace `status:in-progress` with `status:needs-pr`,
  or with `status:blocked` naming your pull request if the rest must wait for
  it to merge.
- Do not start a second fix for a claimed issue. If you think its approach is
  wrong, say so on the issue.

An abandoned draft keeps `status:has-pr` next to `status:needs-pr` until
someone claims its issue or a closing pull request is ready. To preview the
cleanup, run **Release stale claims** from Actions on `main` with its dry-run
option; it runs only main's copy of the script.

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
  Decision issue and links or opens the implementation issues. Implementation
  may start once the owner explicitly approves the design, including in chat.
  The design pull request takes the highest priority of the issues it
  unblocks, and reviewers take it first. Each dependent implementation pull
  request writes `Waits on #<design PR>` in its description and merges only
  after that design pull request.
- **Not needed:** close as *not planned* with a one-line reason. Check the open
  `status:blocked` issues that name it: park them too if they cannot happen
  without it, or comment with what they now wait on.

## Organize the work

For work with several steps, keep a short plan and update it as you learn
more. If you cannot finish, push your branch and say what remains where the
next person will look. Keep related code, docs and checks together. Do not mix
unrelated changes, start a competing fix (see [Claim an issue](#claim-an-issue))
or leave scratch files in the repository. Record progress, decisions and what
remains on the issue and pull request, not in a second tracker.

## Prepare a pull request

1. Link each issue with `Closes` or `Refs`
   ([how](#link-issues-from-the-pull-request)). If no issue is needed, such as
   for a direct owner request, say so.
2. Read the relevant game-design, current-feature and developer pages.
3. Check open pull requests and recent merges for overlap and name any you
   find; if one already makes your change, stop
   ([how](#check-for-duplicates-and-stale-work)). To build on an
   unmerged pull request #A, branch from its branch, set your pull request's
   base to that branch and fill in the template's **Stacked on** line. While
   stacked, bring in newer main only by merging your base branch. When #A
   merges, GitHub moves your pull request to main, and main then needs merging
   in. If #A closes without merging, change your base to main and drop the
   parts of #A you don't need. If yours is already ready, whoever closed #A
   changes its base ([after closing without merging](#review-and-merge)) and
   its reviewer drops those parts. If your pull request needs another one's
   code or approved design but is not stacked on it, write `Waits on #A` on
   the overlap line.
4. Keep to one concern, with the docs and tests it needs.
5. Fill in the [pull request template](.github/pull_request_template.md) for
   someone who has not read the conversation, including the **Author** line.
   Mark sections that do not apply.

Use a short title about the effect, such as
`Show fullness instead of hunger on the agent panel`.

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
- Keep each test quick, ideally under a minute on CI: every merge waits for a
  full CI run, and no run finishes before its slowest test. If CI warns about a
  slow test you added or changed, make it faster before marking the pull
  request ready ([how CI runs](docs/development/build-and-test.md#how-ci-runs)).
- Run the applicable [build and test checks](docs/development/build-and-test.md#which-checks-to-run)
  and put commands and results in the PR, including anything that could not
  run. A passing export is not a Windows playtest.

### Keep documentation and the changelog useful

Update the affected docs in the same change, keeping one home per subject and
linking to it. Only call a feature available once it works on the normal
private-world and Godot play path; a test or data type alone does not make it
playable. Put detailed unresolved findings in Issues, and link them from a page
only when that helps a reader understand a limitation. If a page moves, update
the links to it; the [documentation checks](docs/development/build-and-test.md#focused-documentation-checks)
fail on any local link or heading link you miss.

Add a plain-English changelog entry for player-visible gameplay, UI,
world-runtime, save-compatibility, deployment, packaging or security changes.
Skip refactors, tests and docs-only edits unless they change an explicit
supported behavior or operational promise.

If your change makes a waiting entry wrong, edit or delete it in the same pull
request.

Put the entry in a new file in [changes/](changes/README.md) rather than editing
[CHANGELOG.md](CHANGELOG.md), so parallel pull requests do not conflict. Start
the file with a `- ` bullet. Name the file after the issue number and the
change, such as `431-orchard-harvest.md`. When there is no issue, name it after
your branch without the `claude/` or `codex/` prefix, such as
`settings-redesign.md`. Put the file directly in `changes/`; entries in
subfolders are not collected. `scripts/collect-changes.sh` moves the entries
into CHANGELOG.md when a release is prepared. A catch-up outside a release is
an owner request, handled by one session at a time.

### Drafts and readiness

Open a pull request as a **draft** and keep it there while you are working on
it. Mark it **ready for review** only when it is finished: every change pushed,
the checks passing, the description final with `Closes` for every issue it
completes, and nothing left to ask the owner.

- **Waiting on the owner means draft.** If the pull request needs an owner
  decision, add `status:needs-decision`, ask in chat, and keep it a draft until
  every answer is in; don't mark it ready between batches of answers.
- **The owner may ask for a draft.** If computment asks for a pull request to
  stay a draft, keep it one. Say at the top of its description that the owner
  asked and what it waits for, add `status:blocked` or `status:needs-decision`
  to it and to the issues it closes, and end your claim. Whoever continues it
  marks it ready only when the owner says so.
- **Ready means handed over.** From then on a reviewer owns the branch: they
  merge main in and fix conflicts, CI failures and review findings themselves
  ([Review and merge](#review-and-merge)). The author stops pushing. Main
  moving on is never a reason to take a pull request back. Before a reviewer
  has claimed it (no `status:reviewing`), the author may convert it back to
  draft to fix a mistake in their own change, if they hold no other claim. Read
  the comments right after converting: if a review claim landed, mark it ready
  again without pushing, add `status:reviewing` back for that reviewer and
  comment instead. Otherwise replace the `status:needs-pr` that converting added
  to its issues with `status:in-progress`, with a signed comment naming the
  branch, before you push.
  Once it is claimed, comment instead, and the reviewer includes the change or
  hands the pull request back.
- **Only a hand-back sends a claimed pull request to draft.** A reviewer who
  needs something they can't do, such as an owner decision or a redesign,
  converts it to draft and comments with what is needed. For an owner decision,
  add `status:needs-decision` before converting, so its issues wait instead of
  rejoining the queue. Otherwise the issues it closes go back to the queue
  automatically, and the author, or any fixing agent, picks it up again. If it
  closes none, the reviewer also updates its `Refs` issues: for an owner
  decision, add `status:needs-decision` to them; otherwise replace
  `status:blocked` with `status:needs-pr`, naming the draft to continue. For a
  direct owner request, tell the owner in chat what is needed.

Once the work is finished, waiting for review or for a prerequisite pull
request to merge is not a reason to stay in draft. If it must wait for a pull
request it is not stacked on, write `Waits on #A` in its description and add
`status:blocked`. A stacked pull request needs no label: it stays out of the
review search until GitHub moves it to main.

Routine playtesting by computment can happen after merge during the alpha, so a
missing Windows playtest, tuning session or latency measurement does not by
itself block a merge. Record what was and was not checked, add a
[playtest list](playtest/README.md) file when a hands-on check is still wanted,
and never claim an unperformed check passed. This does not waive green CI,
independent review, safety checks, a check the owner or reviewer asked for, or
fixing correctness, security or data-loss problems the change introduces.
Release gates stay separate.

### Review and merge

Here a **session** is one top-level Claude Code or Codex conversation together
with every subagent or worker it starts. A pull request's **authors** are the
sessions that pushed to it while it was a draft; a reviewer's commits under a
review claim do not make it an author. The **reviewer** is a session that is
not an author, and normally merges. An author merges only when the owner
explicitly asks and a session that is not an author has already reviewed the
current head, or to revert a commit that broke main (below).

Several reviewers may be merging at the same time, so:

- **Claim a pull request when you start reviewing it.** Search
  `is:pr is:open draft:false base:main -label:"status:reviewing" -label:"status:blocked" sort:created-asc`
  and take the first one at the highest priority; one with no priority label
  counts as P2. Add `status:reviewing` and comment with who is reviewing and
  the commit you started from. Then read the comments again: if someone else
  claimed it before you and their claim has not been released since, leave the
  label alone and pick another. Pull requests missing from that search are
  stacked or wait on another pull request; if that one is ready and unclaimed,
  review it instead.
- **Check it isn't a duplicate.** Before you review in depth, check that main
  or another open pull request doesn't already make the same change. If main
  has it, close the pull request with a comment naming the commit
  ([after closing without merging](#review-and-merge)), and close or update
  its issues ([how](#check-for-duplicates-and-stale-work)). If another open
  pull request makes it, comment on both. Review the one that is further
  along: more complete, already reviewed, or the older between equals. Hand
  the other back to draft with a comment saying which pull request it
  duplicates, so its author decides; if another reviewer holds it, comment
  instead. If it isn't clear which to keep, ask the owner in chat.
- **One review claim at a time.** A claim is not a place in the queue. Take a
  second pull request only while the first waits on CI or for its merging
  turn.
- **Priority decides what you claim, not when you merge.** Once your pull
  request passes the checks below, merge it. Don't hold it back for
  higher-priority pull requests that are still in review. Wait only for a pull
  request its description says it must follow (check 5).
- **Only pushes keep a review claim.** Push each fix and each merge of main as
  soon as it builds. A claim is released 1.5 hours after you add
  `status:reviewing` or last push, whichever is later; comments don't count,
  and don't remove and re-add the label to restart the clock. If your claim
  lapses while you are still reviewing and nobody else has claimed it, claim
  it again. Waiting on the owner does not keep a review claim: hand the pull
  request back instead. If you stop without merging, remove the label and
  comment with the head you leave and what is still unchecked. The label also
  comes off when the pull request closes or goes back to draft.
- **Never use draft as a hold.** Your claim keeps others away while you merge
  main in, add a fix or wait for CI. If you find the pull request must wait for
  another one, add `status:blocked`, write `Waits on #A` in its description and
  remove your claim. Convert to draft only to hand a pull request back
  ([Drafts and readiness](#drafts-and-readiness)).
- **Fix what you find.** Fix problems on the pull request's branch rather than
  handing it back, small or large: merge main in, resolve conflicts, repair
  tests or change code. Merging main in, resolving a conflict by keeping either
  side unchanged, test repairs and wording need no second look. Any other fix,
  such as a change under `src/` or to the save format, needs a review of just
  that diff before you merge, by a fresh subagent that did not write it; name
  it in the review record.
- **Merge only on top of current main.** GitHub's Protect main ruleset refuses
  a merge unless the branch includes the latest main and `verify`,
  `windows-documentation` and `windows-provider-storage` have passed on its
  head, and it allows only squash merges. Two pull requests can each pass
  alone and still break main together, so after merging main in, look for
  clashes git cannot see.
- **Take turns for the final run.** Only one pull request at a time may be in
  its final run: merging main in for the last time, waiting for CI on that head
  and merging. Get CI green and conflicts resolved first, so the final run is
  only a catch-up. Before you start, search
  `is:pr is:open label:"status:merging"`. If another pull request has the
  label, keep reviewing and wait; merging main in again after it merges is a
  push that keeps your review claim. Otherwise add `status:merging` to yours,
  comment, and search again; if another pull request also has the label, the
  one whose `status:merging` comment came first keeps the turn, and the other
  removes its label and waits. Remove the label when you merge, when CI fails
  or when you stop. A merging turn is not a claim: one taken more than 60
  minutes ago has lapsed, and anyone may remove the label with a comment.
- **Version numbers go to whoever merges first.** A save-format, schema or
  other version number in an unmerged pull request is provisional, and nobody
  reserves one, in a comment or anywhere else. Git merges two identical number
  changes without a conflict, so a clean merge does not prove the number is
  still free. If main has taken yours, move this pull request to the next free
  number (above its base's number if it is stacked), update its replay checks,
  save docs and description, and run CI again.

Before merging, check that:

1. The pull request is ready for review, not a draft. Review every commit
   pushed since it was last marked ready as part of the head, and say in your
   review who pushed them if it was not you or an earlier reviewer whose claim
   has ended.
2. CI is green on the current head, and that head includes the latest main. If
   main was merged in or the branch changed after review, review and check the
   new head.
3. A session that is not one of its authors reviewed that exact head. The
   authors' own subagents may check the work, but they are not this review.
   The one exception is a pure revert of a commit that broke main (After
   merging).
4. The description links its issues correctly
   ([Link issues](#link-issues-from-the-pull-request)); fix it first if not.
5. If the description says this pull request must follow another, that one has
   merged. Code dependencies and the design-before-implementation rule above
   set such an order in the description; an order stated only in a comment
   binds nobody.

Squash-merge with `<PR title> (#<number>)` as the subject. Write the body
yourself, never GitHub's list of branch commit messages: what changed and why;
the pull request's own `Closes` and `Refs` lines, or "Direct owner request; no
issue"; `Author: <tool> session <id>` for each author; `Review: <tool> session <id> reviewed
<sha>; checked <what>`; and each reviewer fix, or "none". Use no other closing
keywords. GitHub then deletes the branch and moves pull requests stacked on it
to main. Never delete a branch by hand while open pull requests target it:
GitHub closes them. If that happens, restore the branch from the merged pull
request's page, reopen those pull requests and change their base to main.

After merging:

- Fetch main and confirm the squash commit is there. Wait for main's CI on it
  before you merge anything else; you may start your next review meanwhile. If
  it fails:
  - **Known flaky test:** if only one test failed and it has an open Bug issue
    for intermittent failures, add the run link to that issue and carry on. If
    you think a test is flaky but it has no such issue, re-run the failed job
    if you can and open a P1 Bug with both runs; if you can't re-run it, treat
    it as a real break.
  - **Real break:** whoever merged the first failing commit owns it as a P0,
    even if others have merged since. Comment on that pull request, then open
    a pull request that reverts its squash commit, or a fix if that is quicker,
    with `priority:p0` and the failing run's link. A pure revert may be merged
    by its author once its CI passes. When a revert merges, reopen each issue
    the reverted pull request closed, with `status:needs-pr` and a comment
    naming the revert, the failing run and the branch to continue from. Until main is green, nobody merges main
    into other pull requests or copies the pending fix: wait for it to merge.
- Check every `Closes` issue closed. The
  [Close fixed issues](.github/workflows/close-fixed-issues.yml) workflow closes
  any GitHub missed; if one is still open, close it with a comment naming the
  pull request and commit.
- For each `Refs` issue, check a comment says what remains. If its newest
  claim is the author's claim from before this pull request was marked ready
  (a forgotten claim), or it is `status:blocked` waiting on this pull request,
  replace that label with `status:needs-pr` and comment, or close the issue if
  nothing remains. Leave any newer claim alone.
- Unblock what waited on this work. Search
  `is:open label:"status:blocked" <number>` for this pull request and each
  issue it closed. Remove `status:blocked` from pull requests that waited only
  on it, and from issues whose named blockers have all merged or closed as
  completed; give those issues `status:needs-pr` unless someone claims them or
  a ready pull request closes them, and comment naming the merge commit.

Closing a pull request without merging leaves the work that depended on it
stuck, so whoever closes it:

- changes the base of each pull request stacked on it to main, with a comment
  saying which of the closed pull request's parts it still carries;
- searches `is:open label:"status:blocked" <number>` and comments on each pull
  request or issue that waited on it that it closed without merging. Where the
  work can go on without it, remove `status:blocked` and the `Waits on` or
  `Blocked by` line, and give an issue nobody holds `status:needs-pr`.
  Otherwise ask the owner in chat what it should wait on now.

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
