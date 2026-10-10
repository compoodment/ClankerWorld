# Skills

A skill holds the rules and steps for one job, or a step-by-step guide for a
task that comes up again and again, such as auditing the tests. Agents follow
one when their job or task matches it; people can read them too. Each skill
lives in its own folder here.

**Job skills** hold the rules for their job, and they bind everyone doing that
job just as [CONTRIBUTING](../CONTRIBUTING.md) does:

| Skill | Use it to |
| --- | --- |
| [find-bugs](find-bugs/SKILL.md) | Find problems, confirm each on current main and file it as its own Bug issue, or report a security problem privately |
| [fix-issue](fix-issue/SKILL.md) | Pick up, claim, build and hand over an issue, up to a pull request that is ready for review |
| [review-merge](review-merge/SKILL.md) | Review and merge pull requests as fast as the rules allow, keeping reviewed ones flowing into the merge queue |
| [design-decisions](design-decisions/SKILL.md) | Bring open design questions to the owner, record the answers on every page and open the Implementation issues they unblock |

**Guides** help with a task inside any job:

| Skill | Use it to |
| --- | --- |
| [test-audit](test-audit/SKILL.md) | Review, prune or add tests, using evidence of what each test proves and measured coverage |

## Using a skill

When your task matches a skill's description, read its `SKILL.md` and follow
it. Claude Code and Codex find the skills here on their own, through the links
in `.claude/skills/` and `.agents/skills/`; other tools find them through
AGENTS.md's [job table](../AGENTS.md#know-your-job) and
[source table](../AGENTS.md#find-the-right-source). CONTRIBUTING holds the
rules every job shares; a job skill adds its job's rules without contradicting
them. No skill overrides [CONTRIBUTING.md](../CONTRIBUTING.md),
[AGENTS.md](../AGENTS.md) or an instruction from the owner. Run a skill's
commands from the repository root.

## Adding or changing a skill

- Give the skill a folder named after it, in lowercase with hyphens, such as
  `test-audit`. Don't repeat the project's name: everything here is about
  ClankerWorld.
- Start `SKILL.md` with front matter: `name`, matching the folder, and a
  `description` that says what the skill does and when to use it. Agents
  choose skills by that description.
- Keep it timeless. Don't write versions, test counts, dates or file lists
  that will change; tell the reader where to find them instead. Keep one home
  for each rule: a rule every job needs belongs in CONTRIBUTING, a rule for one
  job in that job's skill, and anything else links to the page that owns it.
- Put helper scripts in `scripts/` and supporting files in `references/`. An
  optional `agents/openai.yaml` sets how Codex shows the skill.
- Link the new folder from `.claude/skills/` and `.agents/skills/`, so Claude
  Code and Codex list it: from the repository root, in a Linux or macOS shell,
  run `ln -s ../../skills/<name> .claude/skills/<name>` and the same for
  `.agents/skills/`. Add a row to the right table above, and to the
  [AGENTS.md](../AGENTS.md#know-your-job) job table or source table.
- Git for Windows checks links out as small text files unless symbolic links
  are turned on (`git config core.symlinks true`, which needs Windows
  Developer Mode). Without them, Claude Code and Codex on that checkout don't
  list the skills, and agents find them through AGENTS.md instead.
- A skill changes how agents work, so a change to this folder follows the
  same rules as a change to AGENTS.md: it is P0 and needs review like any
  other pull request.
