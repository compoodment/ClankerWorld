---
name: fix-issue
description: Pick up, claim, build and hand over a ClankerWorld issue, from the ready-work search to a pull request that is ready for review. Use when your job is Fix issues, when asked to implement or fix an issue, when continuing an abandoned draft, or when an owner request needs a pull request.
---

# Fix an issue

These are the rules for the Fix issues job. They bind like
[CONTRIBUTING](../../CONTRIBUTING.md), which holds the rules every job shares:
labels, priorities, claims, linking issues, drafts and what every change needs.
[AGENTS](../../AGENTS.md) adds signing and asking the owner. An owner request
that needs a pull request follows
[Prepare a pull request](#prepare-a-pull-request) and
[Drafts and readiness](#drafts-and-readiness) below, with no issue or claim.

Your job ends when your pull request is ready for review and linked to its
issues. Don't merge it yourself.

## Find work

Ready work is agreed, unblocked, unclaimed and not waiting on the owner:

```text
is:issue is:open label:"status:needs-pr" -label:"type:decision" -label:owner-task -label:"status:in-progress" -label:"status:blocked" -label:"status:needs-decision" sort:created-asc
```

Take the highest priority first: add `label:"priority:p0"` to the search, then
p1, p2 and p3, and take the first result, which is the oldest issue at that
level. Before you claim, check whether an owner decision outranks it: search
`is:open label:"status:needs-decision"` and
`is:issue is:open label:"type:decision"` for the same or a higher priority,
skipping Decision issues whose comments already record the owner's answer. If
you find one, ask the owner about it in your reply
([how](../../AGENTS.md#ask-the-owner-in-chat)), then carry on with the ready
work.

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
the new one ([what closing needs](../review-merge/SKILL.md#hand-back-or-close)).

## Check for duplicates and stale work

Many sessions file and fix issues at once, so by the time you reach an issue it
may already be done, filed twice or out of date. Before you claim it:

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
| The description no longer matches main | Update it to current main before you start, with a comment saying what changed. If the design itself is in question, ask the owner ([how](../../AGENTS.md#ask-the-owner-in-chat)) and add `status:needs-decision`. |

Record what you checked in your claim comment, in a line such as "Checked
against main 1a2b3c4: still broken; no other issue or pull request covers it."

## Claim an issue

Before you start, replace `status:needs-pr` with `status:in-progress` and
comment with who is working on it and your branch name in backticks, with a
prefix such as `codex/123-fix`; the release workflow finds your pushes through
that name, and only names with a slash. Agents sign the comment with their
session ID ([how](../../AGENTS.md#sign-your-comments)).

- **One claim at a time.** Claim an issue when you start on it, not to line up
  your next job. The exception is combined work: when one pull request, or one
  stack of pull requests built on each other, will cover several related
  issues, claim them together, and in each claim comment name every branch of
  the stack in backticks, starting with the one you push first, so a push to
  any of them keeps all the claims. Each issue's claim ends when the pull
  request that closes it is marked ready, and you are free to claim the next
  issue once all of yours are ready.
- **Only pushes keep a claim.** Push your branch and open a draft pull request
  within the first hour, even before the work builds, then push at least every
  hour. A draft is where unfinished work belongs. Work that exists only on your
  machine is invisible to everyone else and is lost if the claim passes on.
- **What the release workflow counts.** Adding `status:in-progress`, opening a
  linked draft, and pushes to a linked draft's branch or to one of the five
  branches most recently named in the issue's comments. A draft that only
  refers to the issue (`Refs`) counts when its branch is named in the issue's
  comments or, after one optional `prefix/`, starts with the issue number and a
  hyphen, optionally after `issue-`, such as `codex/123-fix` or
  `claude/issue-123-fix`. A push counts from when it reached GitHub, even if
  its commits are older. Pushes to ready pull requests belong to their
  reviewers and don't count. A released issue goes back to `status:needs-pr`,
  unless it is blocked, a decision or an owner task, with a comment naming the
  draft or branch to continue from. A release comes up to 30 minutes after the
  1.5 hours, or later when GitHub starts a scheduled run late. To preview it,
  run **Release stale claims** from Actions on `main` with its dry-run option;
  it runs only main's copy of the script.
- **A claim lasts while its label is on**, however long ago you last pushed.
  Only you, the release workflow, an owner request, or a reviewer clearing a
  forgotten `Refs` claim after merging ends it. Add the label again only when
  you claim the issue afresh.
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

While your draft closes an issue, the issue shows `status:in-progress` next to
`status:has-pr`. An abandoned draft keeps `status:has-pr` next to
`status:needs-pr` until someone claims its issue or a closing pull request is
ready. [Labels](../../docs/development/labels.md) lists everything the label
workflows do.

## Prepare a pull request

For work with several steps, keep a short plan and update it as you learn
more. If you cannot finish, push your branch and say what remains where the
next person will look.

1. Link each issue with `Closes` or `Refs`
   ([how](../../CONTRIBUTING.md#link-issues-from-the-pull-request)). If no
   issue is needed, such as for a direct owner request, say so.
2. Read the relevant game-design, current-feature and developer pages
   ([Find the right source](../../AGENTS.md#find-the-right-source)).
3. Check open pull requests and recent merges for overlap and name any you
   find; if one already makes your change, stop
   ([how](#check-for-duplicates-and-stale-work)).
   - **To build on an unmerged pull request #A,** branch from its branch, set
     your pull request's base to that branch and fill in the template's
     **Stacked on** line. While stacked, bring in newer main only by merging
     your base branch. When #A merges, GitHub moves your pull request to main,
     and main then needs merging in. If #A closes without merging, change your
     base to main and drop the parts of #A you don't need; if yours is already
     ready, whoever closed #A changes its base and its reviewer drops those
     parts.
   - **If your pull request needs another one's code or approved design** but
     is not stacked on it, write `Waits on #A` on the overlap line.
4. Keep to one concern, with the docs and tests it needs
   ([Every change](../../CONTRIBUTING.md#every-change)). Before marking ready,
   run `node scripts/find-stale-docs.js <names>` and fix each sentence your
   change made false
   ([how](../../CONTRIBUTING.md#keep-documentation-and-the-changelog-useful)).
5. Fill in the [pull request template](../../.github/pull_request_template.md)
   for someone who has not read the conversation, including the **Author**
   line and the stale-text search. Mark sections that do not apply.

## Drafts and readiness

[CONTRIBUTING](../../CONTRIBUTING.md#drafts-and-readiness) sets when a pull
request is a draft and when it is ready. As its author:

- **Waiting on the owner:** keep it a draft until every answer is in; don't
  mark it ready between batches of answers.
- **When the owner asks for a draft:** say at the top of its description that
  the owner asked and what it waits for, add `status:blocked` or
  `status:needs-decision` to it and to the issues it closes, and end your
  claim. Whoever continues it marks it ready only when the owner says so.
- **To fix a mistake after marking it ready:** only before a reviewer has
  claimed it (no `status:reviewing`), and only if you hold no other claim,
  convert it back to draft. Read the comments right after converting: if a
  review claim landed, mark it ready again without pushing, add
  `status:reviewing` back for that reviewer and comment instead. Otherwise
  replace the `status:needs-pr` that converting added to its issues with
  `status:in-progress`, with a signed comment naming the branch, before you
  push. Once it is claimed, comment instead, and the reviewer includes the
  change or hands the pull request back.
- **When a reviewer hands it back to draft,** its issues rejoin the queue, and
  you or any fixing agent picks it up again as an abandoned draft
  ([Find work](#find-work)).

## Report

Report as [AGENTS](../../AGENTS.md#report-your-result) asks: the pull request
link, the checks you ran, any you could not run, and what is still pending,
such as review or a playtest.
