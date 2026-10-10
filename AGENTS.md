# Working on ClankerWorld

Read [CONTRIBUTING.md](CONTRIBUTING.md) in full before you start, then the
skill for your job ([Know your job](#know-your-job)). CONTRIBUTING holds the
rules every job shares: issues, labels, priorities, claims, pull requests and
writing. Each job's skill holds that job's own rules, and they bind like
CONTRIBUTING. This file does not repeat either; it adds only what is specific
to coding agents. When they seem to disagree, CONTRIBUTING wins over this file
and this file wins over any skill, except that an explicit owner instruction
for your task wins over all of them
([Ask the owner in chat](#ask-the-owner-in-chat)). Put a change to a shared
rule in CONTRIBUTING, and a change to one job's rules in its skill.

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
  (`git show origin/main:CONTRIBUTING.md`), including your job's skill.
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
workflow choice in CONTRIBUTING, the job's skill or its linked developer page,
and a short
comment on the issue saying it was the owner's answer. In the same step,
remove `status:needs-decision` from the issue and from any pull request that
waited on it. If nobody holds the issue (no `status:in-progress`) and it is not
a decision or owner task, add `status:needs-pr` too.

Review and merge sessions follow the
[decision exception](CONTRIBUTING.md#issues-and-design-questions): they skip
pull requests waiting on the owner, and when a review turns up a question only
the owner can answer, they write it on the pull request and hand it back
([how](skills/review-merge/SKILL.md#hand-back-or-close)). The owner answers those
when they choose to, through the searches below. Security reports and questions
about a direct owner request in that same session still go to the owner in chat.

An instruction the owner gives you in chat applies to your session at once,
even where it differs from these files; say in your comments that it was the
owner's request. It never skips required review, green CI or safety checks. If
it sounds like a rule for every agent, ask the owner whether to write it into
CONTRIBUTING, AGENTS or a job's skill. Other running sessions follow it only after it merges
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
  one exception is the fresh-subagent check of a reviewer's own fix that the
  [review-merge skill](skills/review-merge/SKILL.md#fix-what-you-find) asks
  for.
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

Several agents usually work at once, each given one job. Read the skill for
yours before you start: it holds that job's rules and says where the job ends.

| Job | Read | Ends when |
| --- | --- | --- |
| Find bugs | [find-bugs](skills/find-bugs/SKILL.md) | Each problem has its own Bug issue, or, for a security problem, the owner has it in chat and its placeholder issue is open |
| Fix issues | [fix-issue](skills/fix-issue/SKILL.md) | Its pull request is ready for review and linked to its issues |
| Review and merge | [review-merge](skills/review-merge/SKILL.md) | The change is on main, main's CI passes on it, its issues are closed or updated, and what waited on it is unblocked |
| Owner requests | [design-decisions](skills/design-decisions/SKILL.md) for design questions and answers, and the pull request steps in [fix-issue](skills/fix-issue/SKILL.md#prepare-a-pull-request) for any pull request | The requested pull request is ready, or a draft if the owner asked for one, or decisions are recorded |

- **Owner requests:** the owner asks you directly for something, such as a
  change to how the repository works or a set of decisions. It needs no issue
  or claim; say so in the pull request, which follows fix-issue's
  [pull request steps](skills/fix-issue/SKILL.md#prepare-a-pull-request) like
  any other. Turn agreed work into Implementation issues: reuse an older issue
  if one covers it, and give new ones every form section and their
  [labels](CONTRIBUTING.md#labels), including `status:needs-pr`
  ([how](skills/design-decisions/SKILL.md#4-open-the-implementation-issues)).
  When the owner requests a release, the
  preparing session also performs the post-merge verification, tagging and
  publication steps in [Releasing](docs/development/releasing.md#release-gate).
- **Watching pull requests:** a fixing session may watch its own draft, and
  stops watching when it marks it ready. A reviewer starts watching when it
  claims a pull request, and stops when it merges, hands it back or its claim
  lapses. An event on a pull request you no longer own is not a reason to push
  or comment; if it needs the owner, say so in chat.

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
| Find docs your change made false | `node scripts/find-stale-docs.js <names>` ([how](CONTRIBUTING.md#keep-documentation-and-the-changelog-useful)) |
| Prepare an explicitly requested release | [Releasing](docs/development/releasing.md) |
| Review, prune or add tests | [Test audit skill](skills/test-audit/SKILL.md) |
| Understand a label that moved on its own | [Labels](docs/development/labels.md) |

The [skills folder](skills/README.md) holds each job's rules and guides for
tasks that come up again and again. Claude Code and Codex list them on their
own; when your task matches one, follow its `SKILL.md`.

## Report your result

- **Fixing:** give the pull request link, the checks you ran, any you could not
  run, and what is still pending, such as review or a playtest. Do not merge
  it yourself; [Who reviews and merges](CONTRIBUTING.md#who-reviews-and-merges)
  lists the exceptions.
- **Merging:** fetch `origin/main`, confirm the squash commit is there, its
  issues closed and main's CI passed on it (or what you did about a failure),
  and give the commit hash.
- Never call work delivered while review or another required step is still
  pending.
- **On your own machine:** keep evidence in `.evidence/`, with what is worth
  keeping in `.evidence/keep/`. When your job ends, run
  `scripts/clean-workspace.sh` and remove what it lists with `--apply`
  ([Disk space](docs/development/build-and-test.md#disk-space)).
