---
title: Labels
type: development-reference
status: active
updated: 2026-10-08
---

# Labels

[CONTRIBUTING](../../CONTRIBUTING.md#labels) says what each label means and
which ones you add by hand. This page explains how the label list is kept and
what the label workflows do on their own, for anyone changing them or puzzled
by a label that moved.

## The label list

[`.github/labels.json`](../../.github/labels.json) is the only source of
labels. A workflow updates the repository from it, and retired labels, such as
`status:needs-playtest`, are deleted again every six hours, so never create a
label by hand. To rename one, change its name and add the old name to its
`aliases`, so issues keep it, and in the same pull request update the issue
forms, `.github/scripts/` and the searches in CONTRIBUTING, AGENTS.md and the
skills that name it. The workflow-script tests check that every label the
forms, scripts and documented searches use is active.

## What the workflows do on issues

- Issue forms filed on GitHub's website set the type and `priority:p2`, and the
  Decision form also adds `status:needs-decision`. An issue created through the
  API or an agent's GitHub tools gets no labels.
- An issue a pull request closes (`Closes`, `Fixes` or `Resolves` directly
  before its number) gets `status:has-pr` in place of `status:needs-pr`, and
  loses `status:in-progress` when that pull request is marked ready. While the
  claimed pull request is a draft, `status:in-progress` stays next to
  `status:has-pr`; while the claim waits on the owner, it stays next to
  `status:needs-decision`.
- If the pull request goes back to draft, nobody holds the issue and the pull
  request has no `status:needs-decision` or `status:blocked`, the issue gets
  `status:needs-pr` back next to `status:has-pr`.
- When the last such pull request closes, `status:has-pr` comes off; if none
  merged, the issue goes back to `status:needs-pr`, unless it is claimed,
  blocked, waiting on the owner or parked, or is a decision or owner task.
- `Refs` changes no issue labels.
- Quiet claims are released by **Release stale claims**
  ([what counts](../../skills/fix-issue/SKILL.md#claim-an-issue)).

## What the workflows do on pull requests

- **Priority:** the highest of the open issues it closes; if none has one, of
  the open issues it refers to; otherwise P2; and P0 if it changes
  [how we work](../../CONTRIBUTING.md#priorities). A priority added by hand is
  never removed, and wins when it is higher.
- **Type:** one, from the first ticked **Type of change** box, or `type:docs`
  when none is ticked and only documentation changes.
- **Areas:** at most two, from the files it changes, set when it opens,
  reopens, is marked ready, is pushed to or changes base, unless someone has
  changed its areas by hand.
- **Status:** `status:needs-review` while it is ready. `status:needs-review`,
  `status:reviewing` and `status:merging` come off when it closes or goes back
  to draft.
- A pull request stacked on another's branch is judged by what it would change
  on main.

The scripts are [`pr-labels.js`](../../.github/scripts/pr-labels.js),
[`stale-claims.js`](../../.github/scripts/stale-claims.js) and
[`sync-labels.js`](../../.github/scripts/sync-labels.js), each with its tests
beside it.
