---
name: find-bugs
description: Find problems in ClankerWorld, in the game or in code and tests, confirm each one on current main and file it as its own Bug issue with the right labels, or report a security problem privately to the owner. Use when your job is Find bugs, when asked to look for bugs, test a feature for problems or audit an area of the game.
---

# Find bugs

These are the rules for the Find bugs job. They bind like
[CONTRIBUTING](../../CONTRIBUTING.md), which holds the shared rules for issues,
labels and priorities. [AGENTS](../../AGENTS.md) adds signing and asking the
owner.

Your job ends when each problem you found has its own Bug issue, or, for a
security problem, the owner has it in chat and its placeholder issue is open.
Fix a problem only if you are asked to; filing it is the job.

## Confirm it on current main

- Fetch main and reproduce the problem there first, in the game or with a test.
  A problem you saw only on an older build or a branch may already be fixed.
- Say how you saw it: in the game, or in code or tests. A problem found by
  reading code is real when you can point to the input that breaks it; a
  failing test that shows it is the strongest evidence.
- Search open and closed issues and open pull requests by topic, not only by
  number. If a closed issue's fix did not work, reopen it with the new
  evidence. If an open issue or pull request already covers it, add what you
  found there instead of filing again.

## File one issue per problem

- Use the **Bug** template's sections: what happened, what you expected, how to
  repeat it and the build (the main commit you tested). An issue filed through
  the API or your GitHub tools gets no labels, so add them yourself: `type:bug`,
  one or two areas, a priority and `status:needs-pr` when nothing is left to
  decide ([Labels](../../CONTRIBUTING.md#labels)).
- Set the priority by the [bug rules](../../CONTRIBUTING.md#priorities),
  including the P0 and P1 caps, and add `regression` to something that used
  to work.
- Give the title in ordinary words about the effect, such as "Returning a jug
  can halt the world". Keep one problem per issue; link related ones instead
  of combining them.
- If fixing it would settle an open game choice, it is a Decision, not a Bug:
  file a Decision issue and ask the owner
  ([how](../design-decisions/SKILL.md)).
- Tell the owner in chat about every P0 you file.

## Security problems

Follow CONTRIBUTING's [security rule](../../CONTRIBUTING.md#priorities): the
repository is public, so tell the owner in chat what you found, including how
to reproduce it ([how](../../AGENTS.md#ask-the-owner-in-chat)), and open only
the placeholder issue it describes, with no details.

## Report

List the issues you filed, highest priority first, with how you confirmed each
one, and anything you suspect but could not confirm.
