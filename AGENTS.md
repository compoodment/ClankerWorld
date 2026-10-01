# Working on ClankerWorld

Read [CONTRIBUTING.md](CONTRIBUTING.md) before you start. It holds the shared
rules for issues, labels, priorities, pull requests, review, playtesting and
writing, or links to the page that does. This file adds only what is specific
to coding agents; when the two seem to disagree, CONTRIBUTING wins.

## Start with what you were asked

- If the user asks for a proposal or review first, present it before
  implementing. If they ask only for a draft or a pull request, stop there.
  When you are asked to implement something, carry it through to a pull
  request that is ready for review. Other jobs end where
  [Know your job](#know-your-job) says.
- Before changing files, look at the current branch, working tree and open pull
  requests. Keep unrelated and uncommitted work, and refresh the repository
  state instead of trusting old notes.
- Never skip required review to meet a deadline.

## Ask the owner in chat

computment reads your chat replies, not GitHub comments, issues or pull request
descriptions. Put whatever you need from them in your reply: a decision, an
owner task, a go-ahead for something outside your job, or a problem you cannot
solve. Give the options and your recommendation, highest priority first. Then
record the answer yourself: a design choice in its game-design chapter, and a
short comment on the issue saying it was the owner's answer.

When the owner asks what you need from them, answer from these searches,
highest priority first:

```text
is:open label:"status:needs-decision"
is:issue is:open label:owner-task
```

## Sign your comments

Many sessions share one GitHub account, so sign every claim, review claim and
hand-off comment with your tool and a session ID, for example: "Claude Code
session d1496bec is working on this on branch `claude/123-fix`." Other
sessions can then tell your claim apart from their own.

- Use the ID your tool gives this session if you can find one; Claude Code
  sets `CLAUDE_CODE_SESSION_ID`, so use its first 8 characters. Otherwise make
  one up once, for example with `openssl rand -hex 4`, and use it for the
  rest of the session. If you lose track of it, the branch you are on tells
  you which claim is yours.
- A comment with your ID is yours, even after a pause or a context reset. A
  different ID, or just "Codex" or "Claude", is another session: don't take
  over its claim while it is still active.

## Know your job

Several agents usually work at once, each given one job. Every job follows
[Work on an issue](CONTRIBUTING.md#work-on-an-issue); each ends at a different
point.

| Job | Follow | Ends when |
| --- | --- | --- |
| Find bugs | [Issues](CONTRIBUTING.md#issues-and-design-questions) and the bug rule in [Priorities](CONTRIBUTING.md#priorities) | Each problem has its own Bug issue |
| Fix issues | [Find work](CONTRIBUTING.md#find-work) through [Drafts and readiness](CONTRIBUTING.md#drafts-and-readiness) | A pull request is ready for review and linked to its issues |
| Review and merge | [Review and merge](CONTRIBUTING.md#review-and-merge) | The change is on main and its issues are closed or updated |
| Owner requests | [Issues](CONTRIBUTING.md#issues-and-design-questions), [Drafts and readiness](CONTRIBUTING.md#drafts-and-readiness) and the Decisions rule in [Close issues when the work merges](CONTRIBUTING.md#close-issues-when-the-work-merges) | The requested pull request is ready, or decisions are recorded |

What each job adds:

- **Find bugs:** reproduce the problem on current main first, and say whether
  you saw it in the game or in code or tests. Fix it only if asked.
- **Fix issues:** if the highest-priority work is waiting on a decision rather
  than code, ask the owner about it instead of skipping it silently. Keep your
  pull request in draft until it is finished, including while it waits on an
  owner decision. Marking it ready hands it over: after that, don't push to it
  or convert it to draft once a reviewer has claimed it
  ([Drafts and readiness](CONTRIBUTING.md#drafts-and-readiness)). Push your
  branch and open the draft within the first hour, then push at least every 2
  hours: only pushes keep a claim, and one with nothing pushed for 4 hours is
  released automatically. Comments don't count; while you wait on the owner,
  add `status:needs-decision`, which keeps it
  ([Claim an issue](CONTRIBUTING.md#claim-an-issue)). If you are paused, push
  before you stop, so another agent can continue it.
- **Review and merge:** another reviewer may be merging at the same time, so
  claim each pull request with `status:reviewing` when you start reviewing it,
  one at a time, and merge only on top of current main
  ([Review and merge](CONTRIBUTING.md#review-and-merge)). Only pushes keep a
  review claim; one with nothing pushed for 2 hours is released.
  Fix what you find yourself instead of handing it back, and never convert a
  pull request to draft to hold it; draft only to hand it back. After each
  merge, check main's CI; later pull requests may need main merged in again.
- **Owner requests:** the owner asks you directly for something, such as a
  change to how the repository works or a set of decisions. It needs no issue;
  say so in the pull request. Turn agreed work into Implementation issues:
  reuse an older issue if one covers it, and give new ones their
  [labels](CONTRIBUTING.md#labels), including `status:needs-pr`.

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

## Report your result

- **Fixing:** give the pull request link, the checks you ran, any you could not
  run, and what is still pending, such as review or a playtest. Do not merge
  it yourself; [Review and merge](CONTRIBUTING.md#review-and-merge) has the one
  exception.
- **Merging:** fetch `origin/main`, confirm the squash commit is there and its
  issues closed, and give the commit hash.
- Never call work delivered while review or another required step is still
  pending.
