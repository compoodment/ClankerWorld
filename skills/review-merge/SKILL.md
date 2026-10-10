---
name: review-merge
description: Review and merge ClankerWorld pull requests by the repository's review rules, as fast as they allow, keeping reviewed pull requests flowing into the merge queue, with independent review, checks on the exact head and follow-through after each merge. Use when asked to review and merge pull requests, when your job is Review and merge, or when resuming a review claim this session still holds.
---

# Review and merge

These are the rules for the Review and merge job, with advice for doing it
quickly. They bind like [CONTRIBUTING](../../CONTRIBUTING.md), which holds the
rules every job shares: labels, priorities, claims, drafts, who merges and what
every change needs. [AGENTS](../../AGENTS.md) adds signing, asking the owner
and taking over work. Read the current rules, toolchain and CI timings from the
repository each time instead of carrying them over from an earlier session.
Run commands from the repository root.

Your job ends when the change is on main, main's CI passes on it, its issues
are closed or updated, and what waited on it is unblocked.

The rules are in [Claim a pull request](#claim-a-pull-request),
[Review the change](#review-the-change),
[Add it to the merge queue](#add-it-to-the-merge-queue),
[Before merging](#before-merging),
[Merge and follow through](#merge-and-follow-through) and
[Hand back or close](#hand-back-or-close). The other sections, and the tips
marked as ways to work quickly, are advice.

## What limits the speed

- **Reviews, now that the merge queue does the merging.** A pull request
  merges only through GitHub's merge queue. The queue tests each batch of
  queued pull requests on top of main, several batches at once, and merges a
  batch when CI passes on it. Reviewers no longer take turns: each one adds
  its reviewed pull requests to the queue and goes back to reviewing.
- **Your job is to keep reviewed pull requests flowing into the queue.** Add
  each one as soon as it is reviewed and green, so the queue always has work.
- **What slows the queue:** batches that fail. A failing pull request is taken
  out and the batches behind it are tested again, which costs everyone a CI
  run. Catch problems in review, not in the queue.

## Set up once

- Check the branch, the working tree, open pull requests and recent merges.
  Keep other people's work. Use a separate worktree for each pull request you
  claim.
- Read CONTRIBUTING, AGENTS and this skill in full at the start, and note the
  main commit you read them from. Before each new claim, fetch main and diff
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
  such as `git -c credential.helper= fetch https://github.com/ClankerWorldOrg/ClankerWorld.git main`.
  Check separately that your GitHub tools can do the writes you need. If
  a route is broken, spend a few minutes on it, then report what is missing.
  Never print credentials.

## Claim a pull request

Several reviewers may be merging at the same time, so:

- **Claim a pull request when you start reviewing it.** Search
  `is:pr is:open draft:false base:main -label:"status:reviewing" -label:"status:blocked" sort:created-asc`
  and take the first one at the highest priority; one with no priority label
  counts as P2. Choose by priority, then age; don't pick easy pull requests to
  raise a merge count. Before a deep review, check its dependencies,
  duplicates, authors, current head, readiness and any existing claim. Add
  `status:reviewing` and comment with who is
  reviewing and the commit you started from. Then read the comments again: if
  someone else claimed it before you and their claim has not been released
  since, leave the label alone and pick another. Pull requests missing from
  that search are stacked or wait on another pull request; if that one is
  ready and unclaimed, review it instead.
- **One review claim at a time.** A claim is not a place in the queue. Take a
  second pull request only while the first waits on CI or in the merge
  queue, and review it in that time, so it is ready when the first merges
  ([Keep two pull requests moving](#keep-two-pull-requests-moving)).
- **Priority decides what you claim, not when you merge.** Once your pull
  request passes the checks [before merging](#before-merging), merge it. Don't
  hold it back for higher-priority pull requests that are still in review.
  Wait only for a pull request its description says it must follow.
- **Only pushes keep a review claim.** Push each fix and each merge of main as
  soon as it builds. A claim is released 1.5 hours after you add
  `status:reviewing` or last push, whichever is later. If your claim lapses
  while you are still reviewing and nobody else has claimed it, claim it
  again. Waiting on the owner does not keep a review claim: hand the pull
  request back instead. If you stop without merging, remove the label and
  comment with the head you leave and what is still unchecked. The label also
  comes off when the pull request closes or goes back to draft.
- **Never use draft as a hold.** Your claim keeps others away while you merge
  main in, add a fix or wait for CI. If you find the pull request must wait for
  another one, add `status:blocked`, write `Waits on #A` in its description and
  remove your claim. Convert to draft only to [hand it back](#hand-back-or-close).
- **Before every push,** check that the claim is still yours and read the
  newest comments. Being able to push to a branch doesn't make it yours; take
  one over only as [AGENTS](../../AGENTS.md#take-over-work-only-when-the-owner-asks)
  describes.
- Ask GitHub only for the fields you need: number, state, labels, base, head,
  authors, blockers and run IDs. Page through long lists. Don't print whole
  descriptions, diffs or job logs; fetch the one file or failed step you need.
  When searching for blocked work, ask for issues and pull requests separately
  if a tool mixes them up or leaves one out.

### Check it isn't a duplicate

Before you review in depth, check that main or another open pull request
doesn't already make the same change.

- If main has it, close the pull request with a comment naming the commit
  ([what closing needs](#hand-back-or-close)), and close or update its issues
  ([how](../fix-issue/SKILL.md#check-for-duplicates-and-stale-work)).
- If another open pull request makes it, comment on both. Review the one that
  is further along: more complete, already reviewed, or the older between
  equals. Hand the other back to draft with a comment saying which pull
  request it duplicates, so its author decides; if another reviewer holds it,
  comment instead. If it isn't clear which to keep, ask the owner in chat.

## Keep two pull requests moving

This loop keeps one pull request in review while another waits on CI or in
the queue.

1. **Claim and review the first.** Review it, fix what you find and push.
2. **While its CI runs, claim and review a second.** Claim it when you start on
   it, not earlier: an early claim only keeps others away.
3. **Add the first to the queue** once its CI is green
   ([how](#add-it-to-the-merge-queue)), and go back to the second.
4. **Follow through on the first** when the queue merges it
   ([after merging](#after-merging)). The second is now your first; claim a
   new second when it starts waiting.
5. **Repeat** until nothing ready is left, the owner says stop, or you hit a
   blocker you have to report.

| Your situation | Do this next |
| --- | --- |
| Your fix is building | Read the contracts it touches or write review notes. Don't edit that checkout. |
| Your pushed head waits for CI | Finish the review and any fix checks; start your second review. |
| Your head is green and reviewed | Post the review record and add it to the queue. |
| It is in the queue | Keep reviewing your second; act if the queue takes it out. |
| The queue merged it | Do the follow-through while you review the next one. |

In Claude Code, `/loop` keeps a session going, for example
`/loop review and merge ready pull requests with the review-merge skill`.

## Review the change

- Read what the pull request changes, the contract it touches, the callers
  affected and the tests that matter. Review every commit pushed since it was
  last marked ready, noting who pushed after that. Keep main's changes that
  came in through merges apart from the feature, so you don't review them
  again. Note the head you reviewed.
- Check behavior as well as whether it merges: the server deciding world
  changes, model output treated as untrusted, which agent or household holds
  or has reserved each item, cancellation, and saves and replay where the
  change touches them. The [documentation guide](../../docs/README.md) shows
  where each contract is written. Check the rules every change follows
  ([Every change](../../CONTRIBUTING.md#every-change)), issue scope and the
  changelog entry.
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
  or fixes.
- **Version numbers go to whoever merges first.** A save-format, schema or
  other version number in an unmerged pull request is provisional, and nobody
  reserves one, in a comment or anywhere else. Git merges two identical number
  changes without a conflict, so a clean merge does not prove the number is
  still free, and nor does a batch the queue built without a conflict. Before
  you add a pull request to the queue, check main and the pull requests
  already queued for the same number. If one has taken yours, move this pull
  request to the next free number (above its base's number if it is stacked),
  update its replay checks, save docs and description, and run CI again
  ([Saves and replay](../../docs/development/saves-and-replay.md)).

### Fix what you find

Fix problems on the pull request's branch rather than handing it back, small or
large: merge main in, resolve conflicts, repair tests or change code. Finish
one coherent fix, format and build it, and push it straight away; push merges
of main as soon as they build, too. Don't edit a checkout while tools are
building or reading it.

Merging main in, resolving a conflict by keeping either side unchanged, test
repairs and wording need no second look. Any other fix, such as a change under
`src/` or to the save format, needs a review of just that diff before you
merge, by a fresh subagent that did not write it, given the fix's diff and the
context it needs. Name it in the review record. That look covers only the fix,
not the pull request, and each later fix needs its own.

If the author comments with a change they want after you claimed the pull
request, include it or hand the pull request back. If the pull request needs
an owner decision or a redesign, [hand it back](#hand-back-or-close) and move
on.

## Choose checks

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

## Add it to the merge queue

GitHub's Protect main ruleset merges pull requests only through its merge
queue, and only by squash. A pull request can join the queue once `verify`,
`windows-documentation` and `windows-provider-storage` have passed on its head.
The queue then builds it on top of main and the pull requests ahead of it,
runs CI on that batch and merges the batch when CI passes. Two pull requests
can each pass alone and still break main together; the queue catches that
before either merges.

- **Before you add it,** finish [Before merging](#before-merging). You don't
  need to merge main in first, because the queue tests it on top of main. Merge
  main in only when GitHub reports a conflict, or when a change on main
  interacts with this one and you need to check that by hand.
- **Post the review record** as a signed comment, because the squash commit
  takes only the pull request's title, with `(#<number>)` added:
  `Review: <tool> session <id> reviewed <sha>; checked <what>`, each reviewer
  fix or "none", and each author as `Author: <tool> session <id>`.
- **Add it** by enabling squash auto-merge with your GitHub tools, or with
  GitHub's "Merge when ready" button; GitHub adds it to the queue once its
  checks pass. GraphQL's `enqueuePullRequest` mutation also works. Never merge
  it directly, even if your tools offer to. Keep `status:reviewing` on while it
  is queued; the claim ends when it merges.
- **While it is queued,** don't push to it: a push takes it out of the queue.
  Push only a fix you need, then add it again.
- **If the queue takes it out,** because its batch failed, a conflict appeared
  or a check timed out, read the batch's CI run (the Verify run for the
  `merge_group` event on a `gh-readonly-queue/main/...` branch). If the
  failure is this pull request's, fix it, push and add it again. If a pull
  request ahead of it caused the failure, add it again as it is. Flaky and
  broken-main failures follow [after merging](#after-merging).
- **Priority decides what you review, not the queue's order.** The queue is
  first come, first served.

## Before merging

Check that:

1. The pull request is ready for review, not a draft. Review every commit
   pushed since it was last marked ready as part of the head, and say in your
   review who pushed them if it was not you or an earlier reviewer whose claim
   has ended.
2. CI is green on the current head; the queue then tests it on top of the
   latest main. Main's own CI has passed on its latest commit, or failed only
   on a known flaky test ([after merging](#after-merging)): while main is
   broken, every batch fails. If main was merged in or the branch changed
   after review, review and check the new head.
3. A session that is not one of its authors reviewed that exact head. The
   authors' own subagents may check the work, but they are not this review.
   The one exception is a pure revert of a commit that broke main.
4. The description links its issues correctly
   ([Link issues](../../CONTRIBUTING.md#link-issues-from-the-pull-request));
   fix it first if not.
5. If the description says this pull request must follow another, that one has
   merged. Code dependencies and the
   [design-before-implementation rule](../../CONTRIBUTING.md#close-issues-when-the-work-merges)
   set such an order in the description; an order stated only in a comment
   binds nobody.
6. No doc still contradicts the change. Run `node scripts/find-stale-docs.js`
   with the change's names, read what it lists, and fix any sentence the change
   made false
   ([how](../../CONTRIBUTING.md#keep-documentation-and-the-changelog-useful));
   a wording fix needs no second look.

## Merge and follow through

The queue squash-merges with `<PR title> (#<number>)` as the commit message;
the review record lives in your signed comment on the pull request. Make sure
the title says what the change does before you add it. The description's own
`Closes` lines close its issues. GitHub then deletes the branch and moves pull
requests stacked on it to main. Never delete a branch by hand while open pull
requests target it: GitHub closes them. If that happens, restore the branch
from the merged pull request's page, reopen those pull requests and change
their base to main.

### After merging

- Fetch main and confirm the squash commit is there, and watch main's CI on
  that commit, not a run on the pull request or on a later commit. Meanwhile
  you may review and queue your next pull request. CI that is still running
  is not a break. If main's CI fails:
  - **Known flaky test:** if only one test failed and it has an open Bug issue
    for intermittent failures, add the run link to that issue and carry on. If
    you think a test is flaky but it has no such issue, re-run the failed job
    if you can and open a P1 Bug with both runs; if you can't re-run it, treat
    it as a real break.
  - **Real break:** whoever merged the first failing commit owns it as a P0,
    even if others have merged since. Add nothing else to the queue until the
    revert or fix has merged, so it goes first. Comment on that pull
    request, then open a pull request that reverts its squash commit, or a fix
    if that is quicker, with `priority:p0` and the failing run's link. A pure
    revert may be merged by its author once its CI passes. When a revert
    merges, reopen each issue the reverted pull request closed, with
    `status:needs-pr` and a comment naming the revert, the failing run and the
    branch to continue from. Until main is green, nobody merges main into
    other pull requests or copies the pending fix: wait for it to merge.
- Check every `Closes` issue closed. The
  [Close fixed issues](../../.github/workflows/close-fixed-issues.yml) workflow
  closes any GitHub missed; if one is still open, close it with a comment
  naming the pull request and commit.
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
  a ready pull request closes them, and comment naming the merge commit. Read
  the blocker lines before you edit them: an old mention is not always a
  blocker, and other blockers or newer claims must stay.
- Keep GitHub as the shared record. A small note outside the repository can
  carry the rules commit, claimed pull request and head, reviewed commit, run
  IDs, the fix check, the squash commit and what remains across a context
  reset. Before you resume, check the claim and head again: a note is not
  proof that the claim is still yours.

## Hand back or close

**Handing back.** Only a hand-back sends a claimed pull request to draft. When
it needs something you can't do, such as an owner decision or a redesign,
convert it to draft and comment with what is needed.

- For an owner decision, add `status:needs-decision` before converting, so its
  issues wait instead of rejoining the queue. Otherwise the issues it closes go
  back to the queue automatically, and the author, or any fixing agent, picks
  it up again.
- If it closes no issue, update its `Refs` issues: for an owner decision, add
  `status:needs-decision` to them; otherwise replace `status:blocked` with
  `status:needs-pr`, naming the draft to continue.
- For a direct owner request, tell the owner in chat what is needed.

**Closing without merging** leaves the work that depended on it stuck, so
whoever closes a pull request:

- changes the base of each pull request stacked on it to main, with a comment
  saying which of the closed pull request's parts it still carries; a ready
  one's reviewer then drops the parts it doesn't need;
- searches `is:open label:"status:blocked" <number>` and comments on each pull
  request or issue that waited on it that it closed without merging. Where the
  work can go on without it, remove `status:blocked` and the `Waits on` or
  `Blocked by` line, and give an issue nobody holds `status:needs-pr`.
  Otherwise ask the owner in chat what it should wait on now.

## Wait without wasting time

- Use events, such as subscribing to a pull request, where your tools have
  them. Otherwise check at a modest interval, without fetching the same logs
  again and again.
- Tell the owner what you are actually waiting on: CI queueing, tests running,
  or the merge queue.
- Never cancel and restart CI to look busy, and never push an empty or
  pointless commit to keep a claim. A real catch-up with main is a fine push.

## Report

- Report each merge as [AGENTS](../../AGENTS.md#report-your-result) asks: the
  squash commit, its issues closed and main's CI result. Add how long it took
  from claim to merge, and what it mostly waited on (review, fixes, CI, the
  queue, or main's CI).
- Give a full timing breakdown only when the owner asks, or when you are
  working out why merging is slow. Then separate active work from waiting,
  and don't add up waits that overlapped.
- When the owner says stop, stop where they said. Claim nothing new. If asked
  to finish the current pull request first, take it through main's CI and the
  issue follow-through. Release any other claim with a signed comment
  naming the pushed head, the checks run and what remains. Stop watching pull
  requests once they merge or your claim ends.

## Changing this skill

Follow [skills/README.md](../README.md).
