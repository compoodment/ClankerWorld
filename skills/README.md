# Skills

A skill is a step-by-step guide for a job that comes up again and again, such
as auditing the tests. Agents follow one when their task matches it; people
can read them too. Each skill lives in its own folder here.

| Skill | Use it to |
| --- | --- |
| [review-merge](review-merge/SKILL.md) | Review and merge pull requests as fast as the rules allow, keeping two moving so the merging turn is never idle |
| [test-audit](test-audit/SKILL.md) | Review, prune or add tests, using evidence of what each test proves and measured coverage |

## Using a skill

When your task matches a skill's description, read its `SKILL.md` and follow
it. Claude Code and Codex find the skills here on their own, through the links
in `.claude/skills/` and `.agents/skills/`; other tools find them through
[AGENTS.md](../AGENTS.md#find-the-right-source). A skill never overrides
[AGENTS.md](../AGENTS.md), [CONTRIBUTING.md](../CONTRIBUTING.md) or an
instruction from the owner. Run its commands from the repository root.

## Adding or changing a skill

- Give the skill a folder named after it, in lowercase with hyphens, such as
  `test-audit`. Don't repeat the project's name: everything here is about
  ClankerWorld.
- Start `SKILL.md` with front matter: `name`, matching the folder, and a
  `description` that says what the skill does and when to use it. Agents
  choose skills by that description.
- Keep it timeless. Don't write versions, test counts, dates or file lists
  that will change; tell the reader where to find them instead. Link to the
  documentation page that owns a rule rather than copying the rule.
- Put helper scripts in `scripts/` and supporting files in `references/`. An
  optional `agents/openai.yaml` sets how Codex shows the skill.
- Link the new folder from `.claude/skills/` and `.agents/skills/`, so Claude
  Code and Codex list it: from the repository root, run
  `ln -s ../../skills/<name> .claude/skills/<name>` and the same for
  `.agents/skills/`. Add a row to the table above and to the
  [AGENTS.md](../AGENTS.md#find-the-right-source) source table.
- A skill changes how agents work, so a change to this folder follows the
  same rules as a change to AGENTS.md: it is P0 and needs review like any
  other pull request.
