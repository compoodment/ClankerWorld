---
name: design-decisions
description: Find the game-design questions that are still open, bring them to the owner as Decision issues in one batch, then record the answers on every page that names them and open the Implementation issues they unblock. Use when the owner asks for open design questions or decisions, answers Decision issues, approves a design in chat, or asks to turn agreed design into issues.
---

# Design decisions

These are the rules for recording the owner's design decisions, an owner
request job. They bind like [CONTRIBUTING](../../CONTRIBUTING.md), whose
[Decisions rule](../../CONTRIBUTING.md#close-issues-when-the-work-merges) they
carry out. [AGENTS](../../AGENTS.md#ask-the-owner-in-chat) sets how you ask the
owner and record what they say. The game design lives in
[docs/game-design/](../../docs/game-design/README.md); read its "How to read
the choices" section for the labels **Agreed**, **Leaning toward**,
**Suggestion** and **Still to decide**.

The job ends when the decisions are recorded: every answer is in the game
design, the questions are off every page that listed them, and the work they
unblock has Implementation issues.

## 1. Find what is still open

- Read every chapter in full; split the reading between read-only subagents
  for a whole sweep. List each choice marked **Still to decide**, **Leaning
  toward** or **Suggestion**, wording such as "remains open", "not decided" or
  "later", and the README's list of still-open topics.
- Add the questions sessions parked for the owner: open issues and pull
  requests labelled `status:needs-decision`. Reviewers write theirs on the
  pull request instead of asking in chat.
- For each one, check whether it is really open:
  - **Already answered:** read the closed Decision issues on the topic and
    their comments. An answer recorded in only one chapter is often still
    listed as open in another.
  - **Settled by the build:** search the code and tests on main. If the build
    already does one of the options, the question becomes "confirm what is
    built, or change it"; the build choosing quietly is not the owner's answer.
  - **Only playtesting can answer it:** leave it in the design as "after
    playtesting" instead of asking.
  - **The owner deferred it:** leave it unless something has changed.
- Group sub-questions that would be decided together; keep separate ones
  separate.

## 2. Ask in one batch

- File one **Decision** issue per real question, using the form's sections:
  the question, why it matters, options with their trade-offs, a
  recommendation and what it blocks. Add what the build does today, and link
  the chapter heading. Label it `type:decision`, one or two areas,
  `priority:p2` and `status:needs-decision`; issues filed through the API get
  no labels otherwise.
- Questions the build already settles can share one "Confirm the choices the
  build already made" issue per area, listing each choice.
- Bring the batch to the owner in chat, highest priority first: decisions where
  the build contradicts the design, then ones that block the next features,
  then the rest. Give each question in a line with your recommendation, so the
  owner can answer "all recommended except …".
- If the answer is ambiguous, ask once more about the specific issues rather
  than guessing. A question the owner didn't answer stays open.

## 3. Record the answers

- **On each issue:** comment with the owner's answer, signed, and remove
  `status:needs-decision`. An answer that parks a question for a later stage
  closes its issue now, as not planned with `status:parked`; every other
  answered issue is closed by the design pull request.
- **In the game design:** one pull request records the batch. Put each answer
  where its topic lives, starting with **Agreed on** and the date, followed
  by the Decision issue's number as a link, and take the
  question off every **Still to decide**, **Leaning toward** and
  **Suggestion** list that named it, in every chapter.
  `node scripts/find-stale-docs.js <names>` finds the places. Describe the
  intended game, not the build's status. Keep provisional numbers marked
  provisional, and don't upgrade anything the owner didn't answer.
- Fix any [what works today](../../docs/what-works.md) line an answer makes
  false, such as a feature the owner decided against still described as
  unfinished.
- Several chapters can be written in parallel by subagents, one file each;
  review the whole diff yourself before you commit. Check the open pull
  requests that edit the same chapters, and keep your edits clear of their
  lines where you can.
- The design pull request follows fix-issue's
  [pull request steps](../fix-issue/SKILL.md#prepare-a-pull-request) and
  [drafts rules](../fix-issue/SKILL.md#drafts-and-readiness). It closes every
  answered Decision issue it records with `Closes`, takes the highest priority
  of the issues it unblocks, and lists the Implementation issues it opens.
  Implementation may start once the owner has explicitly approved the design,
  including in chat; each implementation pull request writes
  `Waits on #<design PR>` in its description and merges only after the design
  pull request.

## 4. Open the Implementation issues

- Open an Implementation issue for each answer that needs building now. Check
  open and closed issues first, including any another session filed from the
  same design, and reuse one that covers it.
- Size each for one pull request that one session can finish with tests in a
  few hours, and split a big feature into steps. File them in build order,
  because the queue takes P2 issues oldest first; a step that needs an earlier
  one writes `Blocked by #<number>` and gets `status:blocked` instead of
  `status:needs-pr`.
- Fill every section of the form: the goal, the decision it follows (the
  Decision issue, the design pull request and the chapter heading), how we will
  know it works, what is out of scope, and under **Saves, replay and docs**
  which pages the change will update and which sentences it makes false. Label
  each `type:feature`, one or two areas, `priority:p2` and `status:needs-pr`.
- Answers for a later stage, such as combat or inventions, get their
  Implementation issues when that stage starts, so fixing sessions don't build
  them out of order. Say so in the design pull request.

## Report

Tell the owner the design pull request, the Decision issues it closes, the
Implementation issues opened, and anything still waiting on them.
