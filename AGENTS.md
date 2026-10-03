# Working on ClankerWorld

Read [CONTRIBUTING.md](CONTRIBUTING.md) in full before you start. It holds the
shared rules for issues, labels, priorities, claims, pull requests, review,
playtesting and writing, or links to the page that does. This file does not
repeat them; it adds only what is specific to coding agents. When the two seem
to disagree, CONTRIBUTING wins, except that an explicit owner instruction for
your task wins over both ([Ask the owner in chat](#ask-the-owner-in-chat)). Put
any change to a shared rule in CONTRIBUTING only.

## Start with what you were asked

- If the owner asks for a proposal or review first, give it in chat before
  changing files. If they ask for a draft pull request, leave it a draft
  ([Drafts and readiness](CONTRIBUTING.md#drafts-and-readiness)). If they ask
  for a pull request, or to implement something, carry it through to a pull
  request that is ready for review. Other jobs end where
  [Know your job](#know-your-job) says.
- Before changing files, look at the current branch, working tree and open pull
  requests. Keep unrelated and uncommitted work, and refresh the repository
  state instead of trusting old notes.
- The rules change often, and you read them only when your session starts,
  from your checkout. At the start, run `git fetch origin` and
  `git diff HEAD origin/main -- AGENTS.md CONTRIBUTING.md CLAUDE.md skills/`;
  if it shows changes, read main's versions
  (`git show origin/main:CONTRIBUTING.md`).
  Note `git rev-parse --short origin/main`, and before each new issue or review
  claim fetch again and diff from that commit. If anything changed, follow the
  new rules from then on and note the new commit.
- Never skip required review to meet a deadline.

## Ask the owner in chat

computment reads your chat replies, not GitHub comments, issues or pull request
descriptions. Put whatever you need from them in your reply: a decision, an
owner task, a go-ahead for something outside your job, or a problem you cannot
solve. Give the options and your recommendation, highest priority first. Then
record the answer yourself: a design choice in its game-design chapter, a
workflow choice in CONTRIBUTING or its linked developer page, and a short
comment on the issue saying it was the owner's answer. In the same step,
remove `status:needs-decision` from the issue and from any pull request that
waited on it. If nobody holds the issue (no `status:in-progress`) and it is not
a decision or owner task, add `status:needs-pr` too.

An instruction the owner gives you in chat applies to your session at once,
even where it differs from these files; say in your comments that it was the
owner's request. It never skips required review, green CI or safety checks. If
it sounds like a rule for every agent, ask the owner whether to write it into
CONTRIBUTING or AGENTS. Other running sessions follow it only after it merges
and they next check for rule changes.

When the owner asks what you need from them, answer from these searches,
highest priority first:

```text
is:open label:"status:needs-decision"
is:issue is:open label:"type:decision"
is:issue is:open label:owner-task
```

Before asking about a Decision issue without `status:needs-decision`, read its
comments: if they record the owner's answer, record it in the game design
instead of asking again, unless an open pull request already does
(`status:has-pr`).

Then add the [playtest list](playtest/README.md): how many files and checks are
waiting for the owner to try in the game by hand. Playtest checks are never
owner-task issues, so these searches do not show them.

## Sign your comments

Many sessions share one GitHub account, so sign every claim, review claim,
review result and hand-off comment with your tool and a session ID, for
example: "Claude Code session 1a2b3c4d is working on this on branch
`claude/123-fix`." Write the same signature on your pull request's **Author**
line.

- Claude Code: use the first 8 characters of `CLAUDE_CODE_SESSION_ID`. The
  `claude.ai/code/session_…` link Claude Code adds to commits and pull requests
  is another ID for the same session; don't sign with it. Other tools: use the
  ID your tool shows for this session, or make one up once with
  `openssl rand -hex 4`. Keep one ID for the whole session, even if these rules
  change while you run.
- Subagents and parallel workers belong to the session that started them. They
  sign with its ID and a role, such as `1a2b3c4d/fix-2`, work under its claims,
  and never count as a different session for the pull request's review. The
  one exception is the fresh-subagent check of a reviewer's own fix that
  CONTRIBUTING's Review and merge asks for.
- A comment with your ID is yours, even after a pause or a context reset. Your
  claim is still yours only while its label is on and no newer claim or
  "Claim released" comment follows yours. Check both when you resume, before
  you push. If someone else has claimed it since, don't push there: comment
  with anything useful and leave it to them. If nobody has, claim it again,
  provided you hold no other claim. The branch alone doesn't prove the claim is
  yours, because whoever continues a released draft uses the same branch.
- Another session's issue or review claim stands while its label is on. Take it over only when
  the owner asks ([how](#take-over-work-only-when-the-owner-asks)).

## Take over work only when the owner asks

Only the owner can hand you a claim another session still holds: an issue, a
draft or a review. Their request never waives green CI, review of the exact
head by a different session or merging on current main, and it never gives you
extra claims; a takeover counts as your one claim. If they name several pull
requests or issues, take them one at a time in that order.

1. Look for signs the other session is still working: a push or a signed
   comment in the last hour. If you find any, tell the owner and wait for their
   answer.
2. Comment, signed: "Claude Code session 1a2b3c4d is taking this over at the
   owner's request in chat. Please push nothing more to `codex/123-fix`; push
   any unpushed work to a new branch and name it here." Then remove the claim
   label (`status:in-progress` on the issue, `status:reviewing` on the pull
   request) and add it again, so the claim dates from you.
3. In your chat reply, name the session you took over from, so the owner can
   tell it to stop.
4. If you finish another session's draft, you are one of its authors: a
   different session reviews and merges it.

Every session: before you push to a claimed branch, read its newest comments.
If a push is rejected as non-fast-forward, read the comments before merging
anything, and never force-push a branch another session has pushed to.

## Know your job

Several agents usually work at once, each given one job. Each job follows the
parts of CONTRIBUTING named in its row and ends at a different point.

| Job | Follow | Ends when |
| --- | --- | --- |
| Find bugs | [Issues](CONTRIBUTING.md#issues-and-design-questions) and the bug and security rules in [Priorities](CONTRIBUTING.md#priorities) | Each problem has its own Bug issue, or, for a security problem, the owner has it in chat and its placeholder issue is open |
| Fix issues | [Find work](CONTRIBUTING.md#find-work) through [Drafts and readiness](CONTRIBUTING.md#drafts-and-readiness) | Its pull request is ready for review and linked to its issues |
| Review and merge | [Review and merge](CONTRIBUTING.md#review-and-merge), with the [review-merge skill](skills/review-merge/SKILL.md) | The change is on main, main's CI passes on it, its issues are closed or updated, and what waited on it is unblocked |
| Owner requests | [Issues](CONTRIBUTING.md#issues-and-design-questions), [Prepare a pull request](CONTRIBUTING.md#prepare-a-pull-request) through [Drafts and readiness](CONTRIBUTING.md#drafts-and-readiness), and the Decisions rule in [Close issues when the work merges](CONTRIBUTING.md#close-issues-when-the-work-merges) | The requested pull request is ready, or a draft if the owner asked for one, or decisions are recorded |

What each job adds:

- **Watching pull requests:** a fixing session may watch its own draft, and
  stops watching when it marks it ready. A reviewer starts watching when it
  claims a pull request, and stops when it merges, hands it back or its claim
  lapses. An event on a pull request you no longer own is not a reason to push
  or comment; if it needs the owner, say so in chat.
- **Find bugs:** reproduce the problem on current main first, and say whether
  you saw it in the game or in code or tests. Fix it only if asked.
- **Fix issues:** before you claim, check whether an owner decision outranks
  your next issue: search `is:open label:"status:needs-decision"` and
  `is:issue is:open label:"type:decision"` for the same or a higher priority,
  skipping Decision issues whose comments already record the owner's answer.
  If you find one, ask the owner about it in your reply, then carry on with the
  ready work. Push as you go: only pushes keep your claim.
- **Review and merge:** claim one pull request at a time, when you start
  reviewing it (a second only while the first waits on CI or its merging
  turn). Fix what you find yourself, push each fix as it builds, take
  your turn for the final run with `status:merging`, and check main's CI after
  each merge. The [review-merge skill](skills/review-merge/SKILL.md) shows
  how to review your next pull request while the first waits, so the merging
  turn is never idle.
- **Owner requests:** the owner asks you directly for something, such as a
  change to how the repository works or a set of decisions. It needs no issue
  or claim; say so in the pull request. Turn agreed work into Implementation
  issues: reuse an older issue if one covers it, and give new ones their
  [labels](CONTRIBUTING.md#labels), including `status:needs-pr`.
  Implementation may start after the owner's explicit design approval, but
  its pull request waits for the design pull request to merge, as described in
  [Decisions](CONTRIBUTING.md#close-issues-when-the-work-merges).
  When the owner requests a release, the preparing session also performs
  the post-merge verification, tagging and publication steps in
  [Releasing](docs/development/releasing.md#release-gate).

## Find the right source

Use the [documentation guide](docs/README.md) to choose the right page.

| Task | Read |
| --- | --- |
| Change game rules or intended features | [Game design](docs/game-design/README.md) and the relevant chapter |
| Describe what a player can do now | [What works today](docs/what-works.md) and [Playing](docs/playing.md) |
| Change simulation, model handling or logging | [How it works](docs/development/how-it-works.md) |
| Change saved state, events or compatibility | [Saves and replay](docs/development/saves-and-replay.md) |
| Change owner access or device credentials | [Device pairing](docs/development/device-pairing.md) |
| Build, check or package a change | [Build and test](docs/development/build-and-test.md) |
| Prepare an explicitly requested release | [Releasing](docs/development/releasing.md) |
| Review, prune or add tests | [Test audit skill](skills/test-audit/SKILL.md) |
| Review and merge pull requests | [Review-merge skill](skills/review-merge/SKILL.md) |

The [skills folder](skills/README.md) holds step-by-step guides for jobs that
come up again and again. Claude Code and Codex list them on their own; when
your task matches one, follow its `SKILL.md`.

## Report your result

- **Fixing:** give the pull request link, the checks you ran, any you could not
  run, and what is still pending, such as review or a playtest. Do not merge
  it yourself; [Review and merge](CONTRIBUTING.md#review-and-merge) lists the
  exceptions.
- **Merging:** fetch `origin/main`, confirm the squash commit is there, its
  issues closed and main's CI passed on it (or what you did about a failure),
  and give the commit hash.
- Never call work delivered while review or another required step is still
  pending.
