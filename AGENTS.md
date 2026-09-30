# Working on ClankerWorld

Follow [CONTRIBUTING.md](CONTRIBUTING.md) for the shared contribution workflow,
review requirements, writing rules, tests, documentation and changelog policy.
This file adds instructions for coding agents; it does not repeat that guide.

## Start with the user's requested outcome

If the user asks for a proposal or review first, present it before implementing.
If they request a draft or PR only, stop at that stage. For authorized
implementation, carry the work through verification to a pull request that is
ready for review. Do not merge your own pull request unless the owner
explicitly asks and someone else has already reviewed it. Do not bypass
required review to satisfy a delivery deadline.

Inspect the current branch, working tree and open PRs before changing files.
Preserve unrelated and uncommitted work. Refresh the relevant repository state
rather than relying on old notes. Keep the diff focused on the requested concern.

Follow [Organize the work](CONTRIBUTING.md#organize-the-work). For a task with
several steps, keep a short working plan, update it as evidence changes, and
leave a clear handoff if the work cannot finish in this session. Keep scratch
files out of the delivered diff.

## Know your job

Several agents usually work at once, each given one job. All of them follow
[Work on an issue](CONTRIBUTING.md#work-on-an-issue). Each job ends at a
different point.

| Job | Ends when |
| --- | --- |
| Find bugs | Each problem has its own Bug issue |
| Fix issues | A pull request is ready for review and linked to its issues |
| Review and merge | The change is on main and its issues are closed or updated |
| Work with the owner | The requested pull request is ready, or decisions are recorded |

### Find bugs

- Reproduce the problem on current main. Search open and closed issues first.
  If a closed fix did not work, reopen that issue with the new evidence instead
  of filing a duplicate.
- File one problem per issue with the Bug template. Say whether you saw it in
  the game or reproduced it in code or tests.
- Fix it only if asked. If you do, [claim it](CONTRIBUTING.md#claim-an-issue)
  straight away.

### Fix issues

- Choose from the [agent-ready issues](CONTRIBUTING.md#find-work) and claim one
  before you start.
- Open the pull request ready for review, with `Closes` for each issue it
  completes and `Refs` for the rest. If the change still needs a hands-on check
  in the game, add `status:needs-playtest` to the issue.
- Do not merge it yourself. Report the pull request link as your result.
- Fix CI failures and review requests on the same branch.

### Review and merge

- Review only pull requests you did not write, and follow the
  [Review and merge](CONTRIBUTING.md#review-and-merge) checklist.
- Merge dependent pull requests in their stated order. After each merge, later
  pull requests may need main merged in and CI run again.
- After merging, confirm the commit on GitHub's `origin/main` and that the
  linked issues closed. Report the commit hash.

### Work with the owner

- A direct owner request needs no issue. Say that in the pull request.
- Record agreed design choices in the right game-design chapter. That pull
  request closes the Decision issue it answers.
- Turn agreed work into Implementation issues. Search for older issues about
  the same work first. Update the older issue, or close it as a duplicate of
  the new one.

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

GitHub Issues owns bug reports, reproduction steps, progress and repair evidence.
Do not recreate a bug register in Markdown. An open PR is proposed work, not
proof that its design is agreed or its feature is available.

## Keep explanations understandable

Keep documentation in plain English wherever possible. Follow
[Writing clearly](CONTRIBUTING.md#writing-clearly), including its rules for
game text. Keep exact technical details where needed in developer references,
and explain what they mean.

Update the appropriate page alongside the implementation. Link between pages
instead of duplicating rules or maintaining parallel feature lists. Preserve
agreement labels and open questions when reorganizing design notes.

Check the actual normal game path before describing a feature as available.
Distinguish code or test evidence from deployment and hands-on playtesting.
Put detailed unresolved findings in Issues and link them from the overview
only when they help a reader understand a limitation.

## Verify and deliver

Use the checks appropriate to the change in
[Build and test](docs/development/build-and-test.md#which-checks-to-run).
Docs-only edits need no new tests or separate full runtime suite. Check links
and update existing documentation checks if files move. Report unrun or failed
checks clearly.

For changes intended for main, use the reviewed PR workflow in CONTRIBUTING.
Follow [Drafts and readiness](CONTRIBUTING.md#drafts-and-readiness): do not
leave completed work in draft solely for routine owner playtesting or pending
independent review. Report unperformed checks without turning them into
unrequested merge gates.
If you merged, fetch and verify the resulting commit on GitHub's `origin/main`
before reporting the implementation delivered. Report its hash or link.
If review or another required step is pending, report that remaining step and
the reviewable PR instead of claiming completion.

Follow the release guide for versions, compatibility checks and annotated tags.
Do not create a release or tag merely because a change was merged.
