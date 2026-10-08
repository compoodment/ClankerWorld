---
name: review-merge
description: Review and merge ClankerWorld pull requests as fast as the rules allow, keeping two pull requests moving so the merging turn is never idle, with independent review, checks on the exact head and follow-through after each merge. Use when asked to review and merge pull requests, when your job is Review and merge, or when resuming a review claim this session still holds.
---

# Review and merge

This skill gets a ready pull request onto main with as little waiting as
possible. [CONTRIBUTING](../../CONTRIBUTING.md#review-and-merge) sets what a
merge needs, [AGENTS](../../AGENTS.md) sets your job and how you sign, and an
owner instruction applies as those pages describe. Where this skill differs
from them, they win; the rest is advice for doing the job quickly. Read the
current rules, toolchain and CI timings from the repository each time instead
of carrying them over from an earlier session. Run commands from the
repository root.

## What limits the speed

- **CI, not the number of reviewers.** GitHub refuses a merge unless the pull
  request includes the latest main and CI passed on that head, and the rules
  let only one pull request at a time take its final run, shared by every
  merging session. So once one pull request merges, the next has to take in
  the new main and run CI again before it can merge. The best the repository
  can do is about one merge per CI run. Read how long CI takes from recent
  runs on main.
- **Your job is to keep the merging turn busy.** When one pull request merges,
  the next should already be reviewed, green and free of conflicts with main,
  so its final run is only a catch-up.
- **What raises the limit:** a faster CI, and fewer final runs that fail or
  stall. More merging sessions help only while reviews, rather than CI, are
  what everyone is waiting on.

## 1. Set up once

- Check the branch, the working tree, open pull requests and recent merges.
  Keep other people's work. Use a separate worktree for each pull request you
  claim.
- Read CONTRIBUTING and AGENTS in full at the start, and note the main commit
  you read them from. Before each new claim, fetch main and diff
  `AGENTS.md CONTRIBUTING.md CLAUDE.md skills/` against that commit; read
  only what changed.
- Decide your session signature ([signing](../../AGENTS.md#sign-your-comments)).
  For each pull request, find which sessions authored it: an author, or an
  author's subagent, can't give it the independent review.
- Find the SDK, engine pins and setup in
  [Build and test](../../docs/development/build-and-test.md#toolchain). Share
  downloads and package caches between worktrees.
- If `git fetch` fails to authenticate, this repository is public: try an
  anonymous read for that one command, with credential helpers turned off,
  such as `git -c credential.helper= fetch https://github.com/compoodment/ClankerWorld.git main`.
  Check separately that your GitHub tools can do the writes you need. If
  a route is broken, spend a few minutes on it, then report what is missing.
  Never print credentials.

## 2. Keep two pull requests moving

This loop keeps one pull request in review while the other waits on CI, so a
reviewed, green pull request is ready the moment the turn frees.

1. **Claim and review the first.** Take the highest-priority, oldest pull
   request the [review search](../../CONTRIBUTING.md#review-and-merge) finds.
   Review it, fix what you find and push.
2. **While its CI runs, claim and review a second.** The rules allow a second
   claim only while the first waits on CI or for its merging turn. Claim it
   when you start on it, not earlier: an early claim only keeps others away.
3. **Take the turn for the first** once its CI is green and main merges into
   it cleanly. Its final run is the last catch-up with main, CI on that head,
   and the merge.
4. **Take the turn again for your second.** The moment the first merges, the
   turn is free. If your second is green and main merges into it cleanly,
   take the turn for it at once, with the usual search, label, comment and
   second search: merge main in, push and let its CI run while main's CI runs
   on your first merge. Merge the second only once main's CI has passed too.
   If main's CI fails, follow CONTRIBUTING's flaky-test or real-break steps;
   on a real break, release the turn so the revert or fix can go first.
5. **Follow through on the first** (section 7) while the second's CI runs.
   The second is now your first; claim a new second when it starts waiting.
6. **Repeat** until nothing ready is left, the owner says stop, or you hit a
   blocker you have to report.

| Your situation | Do this next |
| --- | --- |
| Your fix or catch-up is building | Read the contracts it touches or write review notes. Don't edit that checkout. |
| Your pushed head waits for CI | Finish the review and any fix checks; start your second review. |
| Your head is green, but another pull request holds the turn | Keep reviewing your second; watch for the turn to free. |
| You hold the turn | Merge main in, check what changed, push, wait for CI, merge. |
| You just merged; main's CI is running on it | Take the turn for your next green pull request and start its final run; do the follow-through; merge again only once main's CI passes. |

Ways to keep the turn from stalling:

- Before you take the turn, check that main merges in cleanly, for example
  with `git merge-tree --write-tree HEAD origin/main`, and look for version
  numbers main has taken since ([version numbers](../../CONTRIBUTING.md#review-and-merge)).
  A final run that conflicts or fails costs everyone a turn.
- If your head already includes the latest main and CI passed on it, the final
  run is only the merge: take the turn and merge.
- Release the turn the moment your final run fails or you stop, so the next
  pull request can go.
- Don't catch up with main again and again while you wait for the turn.
  Catch up when there is a conflict or a change that interacts with yours, or
  when your claim needs a push, and push every catch-up you make.
- In Claude Code, `/loop` keeps a session going, for example
  `/loop review and merge ready pull requests with the review-merge skill`.

## 3. Pick and claim

Follow the current [search and claim rules](../../CONTRIBUTING.md#review-and-merge).
Choose by priority, then age; don't pick easy pull requests to raise a merge
count. Before a deep review, check its dependencies, duplicates, authors,
current head, readiness and any existing claim.

- Ask GitHub only for the fields you need: number, state, labels, base, head,
  authors, blockers and run IDs. Page through long lists. Don't print whole
  descriptions, diffs or job logs; fetch the one file or failed step you need.
- When searching for blocked work, ask for issues and pull requests
  separately if a tool mixes them up or leaves one out.
- If a pull request seems to hold the merging turn, check that it is still
  open and still labelled.
- Sign your claim, then read the comments again to catch a competing claim.
  Before every push, check that the claim is still yours and read the newest
  comments. Being able to push to a branch doesn't make it yours; take one
  over only as AGENTS describes.

## 4. Review the change

- Read what the pull request changes, the contract it touches, the callers
  affected and the tests that matter. Review every commit the rules require,
  noting who pushed after it was marked ready. Keep main's changes that came
  in through merges apart from the feature, so you don't review them again.
  Note the head you reviewed.
- Check behavior as well as whether it merges: the server deciding world
  changes, model output treated as untrusted, which agent or household holds
  or has reserved each item, cancellation, and saves and replay where the
  change touches them. The [documentation guide](../../docs/README.md) shows
  where each contract is written. Check issue scope and the changelog entry as
  they apply.
- **Check that no doc still contradicts the change.** Run
  `node scripts/find-stale-docs.js` with the names of what changed and read
  every place it lists. A sentence elsewhere that the change made false, such
  as "not built yet" on another page, is a finding: fix it on the branch.
- For a large change, read independent parts at the same time with read-only
  subagents. They belong to your session, so they don't affect who counts as
  an independent reviewer.
- **A pull request that was stacked on another:** work out its own changes
  against its real parent before you resolve conflicts. A squash-merged parent
  can make changes already on main show up again. Keep current main's
  behavior: taking a whole older file can silently drop someone else's fields
  or fixes. Check save and schema version numbers even after a clean merge
  ([Saves and replay](../../docs/development/saves-and-replay.md)).
- **Fix what you find on the claimed branch.** Finish one coherent fix, format
  and build it, and push it straight away, as the rules require. Push merges
  of main as soon as they build, too. Don't edit a checkout while tools are
  building or reading it.
- A reviewer fix that needs a second look goes to a fresh subagent that didn't
  write it, with the fix's diff and the context it needs. Name it in the
  review record. That look covers only the fix, not the pull request, and each
  later fix needs its own.
- If the pull request needs an owner decision or a redesign, hand it back as
  [Drafts and readiness](../../CONTRIBUTING.md#drafts-and-readiness)
  describes, and move on.

## 5. Choose checks

Follow [Which checks to run](../../docs/development/build-and-test.md#which-checks-to-run)
and the current workflow. Before any long command, write down which behavior
could break, which check would catch it, which full or platform checks the
change needs, and what will stay unchecked.

- **Let CI run the full Release suite.** Its test jobs run it on the exact
  head on several machines at once, sooner than one machine can. Locally,
  build and run the tests for what changed, so problems show before the CI
  wait. Run the full suite locally only when you need its result before you
  push.
- List the tests a filter selects before you trust it, especially for classes
  split across files. Check that the tests and theory cases you meant actually
  ran: zero tests, or the wrong ones, is not a check.
- Read the **test-times** summary CI posts. A pull request that makes tests
  much slower than on main needs a fix or a stated reason before it merges:
  slower tests usually mean slower ticks for players, and every later merge
  waits longer for CI.
- Use the [test-audit skill](../test-audit/SKILL.md) when judging, adding or
  removing tests. A repaired test must still exercise the real change and keep
  the contract's assertions. Prefer a small world close to the moment being
  checked over years of idle simulation, or a wait that assumes a reply has
  arrived.
- Reuse a build only when its source, configuration and other inputs match.
  `--no-build` proves nothing about changed source.
- After a later catch-up with main, read the new diff and how it interacts
  with the change, then rerun the checks that apply. Don't rerun unrelated
  local suites because time has passed. Keep earlier results with the commit
  they ran on; the current head still needs green CI.
- Run independent read-only checks side by side if the machine can take it.
  Run builds that share output folders one at a time, and never two full
  suites at once on one machine.

Record the commands, results, selected tests, limits and the commit tested.
Check that the CI run and required checks belong to the current head,
including which commit the workflow checked out. A green run on an older head
doesn't cover a new push, and a passing export is not a Windows playtest.

## 6. Wait without wasting time

- Use events, such as subscribing to a pull request, where your tools have
  them. Otherwise check at a modest interval, without fetching the same logs
  again and again.
- Tell the owner what you are actually waiting on: CI queueing, tests running,
  or another reviewer's turn.
- Never cancel and restart CI to look busy, and never push an empty or
  pointless commit to keep a claim. A real catch-up with main is a fine push.

## 7. Merge and follow through

- Just before merging, go through CONTRIBUTING's checklist: current head,
  current main, review, dependencies, description and checks. Write the squash
  subject and body it describes: issue links, authors, the reviewed commit,
  checks and reviewer fixes. With command-line tools, put a multi-line body in
  a file. Let GitHub delete the branch and move stacked pull requests.
- Fetch main and find the squash commit. Check main's CI on that commit, not a
  run on the pull request or on a later commit. If it fails, follow the
  flaky-test or real-break steps in CONTRIBUTING; CI that is still running is
  not a break.
- Update closed and partly done issues, and unblock what waited on this work.
  Read the blocker lines before you edit them: an old mention is not always a
  blocker, and other blockers or newer claims must stay.
- If you close a pull request without merging, follow
  [what CONTRIBUTING asks after closing](../../CONTRIBUTING.md#review-and-merge):
  move pull requests stacked on it to main and tell whatever waited on it.
- Keep GitHub as the shared record. A small note outside the repository can
  carry the rules commit, claimed pull request and head, reviewed commit, run
  IDs, the fix check, the squash commit and what remains across a context
  reset. Before you resume, check the claim and head again: a note is not
  proof that the claim is still yours.

## 8. Report

- Report each merge as [AGENTS](../../AGENTS.md#report-your-result) asks: the
  squash commit, its issues closed and main's CI result. Add how long it took
  from claim to merge, and what it mostly waited on (review, fixes, CI, the
  turn, or main's CI).
- Give a full timing breakdown only when the owner asks, or when you are
  working out why merging is slow. Then separate active work from waiting,
  and don't add up waits that overlapped.
- When the owner says stop, stop where they said. Claim nothing new. If asked
  to finish the current pull request first, take it through main's CI and the
  issue follow-through. Release any other claim or turn with a signed comment
  naming the pushed head, the checks run and what remains. Stop watching pull
  requests once they merge or your claim ends.

## Changing this skill

Follow [skills/README.md](../README.md).
