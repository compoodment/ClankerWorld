// Keeps pull request and linked-issue labels current.
// Run by .github/workflows/pr-labels.yml through actions/github-script.
// It reads only the event payload and GitHub data; it never runs pull request code.
//
// - At most two area labels, from the files the PR changes, set when it opens
//   and again when it is marked ready. Each file counts towards the one area
//   whose path rule matches it most closely; client files count half and tests
//   count only when nothing else does. The second area is kept only if it has
//   at least a third of the first one's count.
// - One type label: the first ticked "Type of change" box in the PR template,
//   or type:docs when no box is ticked and every changed file is documentation.
// - status:needs-review while a PR is open and not a draft. A reviewer's
//   status:reviewing claim is removed when the PR closes or goes back to draft.
// - The highest priority label (priority:p0 to priority:p3) of the open issues
//   the PR closes, and at least priority:p1 when it changes how everyone works
//   on the repository (.github/, .claude/, CONTRIBUTING.md, AGENTS.md or
//   CLAUDE.md).
// - status:has-pr on open issues the PR closes as described in CONTRIBUTING
//   ("Closes #N", "Fixes #N" or "Resolves #N", one keyword per issue, or any #N
//   on the template's Closes line), replacing status:needs-pr. "Refs #N" only
//   mentions an issue and changes none of its labels.
//   Once the PR is ready for review (not a draft), the issue's claim label
//   status:in-progress is removed too, so the issue shows only status:has-pr.
//   When the last open PR closing an issue closes, status:has-pr is removed
//   again; an issue whose PR closed without merging goes back to status:needs-pr
//   if it has no other status.
// - Events on a PR that is already closed, such as an edit after merging,
//   change nothing.

const NeedsReview = 'status:needs-review';
const Reviewing = 'status:reviewing';
const HasPr = 'status:has-pr';
const Ready = 'status:needs-pr';
const Priorities = ['priority:p0', 'priority:p1', 'priority:p2', 'priority:p3'];
const InProgress = 'status:in-progress';
// Changes to CI, labels, templates or the contribution rules affect every
// agent, so they are at least P1.
const WorkflowPaths = ['.github/', '.claude/', 'CONTRIBUTING.md', 'AGENTS.md', 'CLAUDE.md'];
const WorkflowPriority = 'priority:p1';

// Mirrors .github/workflows/close-fixed-issues.yml: one keyword per issue,
// ignoring HTML comments and code.
const KeywordPattern = /\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?):?\s+(?:([\w.-]+\/[\w.-]+))?#(\d+)\b/gi;
const TemplateLinePattern = /^\s*[-*]\s*Closes\b[^:\n]*:(.*)$/gim;

function labelNames(labels) {
  return (labels ?? []).map(label => (typeof label === 'string' ? label : label.name));
}

function closingIssueNumbers(body, repoName = '') {
  const thisRepo = repoName.toLowerCase();
  const text = (body ?? '')
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`[^`\n]*`/g, ' ');
  const numbers = new Set();
  for (const [, otherRepo, number] of text.matchAll(KeywordPattern)) {
    if (!otherRepo || otherRepo.toLowerCase() === thisRepo) numbers.add(Number(number));
  }
  for (const [, rest] of text.matchAll(TemplateLinePattern)) {
    for (const [, number] of rest.matchAll(/(?<![\w/])#(\d+)\b/g)) numbers.add(Number(number));
  }
  return numbers;
}

// Path prefixes for each area. The longest matching prefix wins for each file.
const AreaRules = {
  'area:agents': [
    'src/ClankerWorld.Simulation/Cognition/',
    'src/ClankerWorld.Simulation/Society/',
    'src/ClankerWorld.Simulation/Playtest/Knowledge/',
    'src/ClankerWorld.Simulation/Playtest/Settlement/Community/',
    'src/ClankerWorld.Simulation/Playtest/Settlement/Family/',
    'src/ClankerWorld.Simulation/Playtest/Runtime/PrivateWorldRuntime.Cognition.cs',
    'src/ClankerWorld.Viewer/Control/Providers/',
    'docs/game-design/agents-and-families.md',
  ],
  'area:towns': [
    'src/ClankerWorld.Simulation/Content/',
    'src/ClankerWorld.Simulation/Playtest/Content/',
    'src/ClankerWorld.Simulation/Playtest/Towns/',
    'src/ClankerWorld.Simulation/Playtest/Settlement/Economy/',
    'src/ClankerWorld.Simulation/Playtest/Runtime/PrivateWorldRuntime.Production.cs',
    'docs/game-design/towns.md',
    'docs/game-design/content-list.md',
    'docs/game-design/inventions-and-mods.md',
  ],
  'area:world': [
    'src/ClankerWorld.Simulation/World/',
    'src/ClankerWorld.Simulation/Kernel/',
    'src/ClankerWorld.Simulation/Harness/',
    'src/ClankerWorld.Simulation/ThirdParty/',
    'src/ClankerWorld.Simulation/Playtest/Settlement/SettlementSurvival.cs',
    'src/ClankerWorld.Simulation/Playtest/Settlement/Economy/SettlementForestry.cs',
    'src/ClankerWorld.Simulation/Playtest/Content/ForestryContent.cs',
    'src/ClankerWorld.Simulation/Playtest/Runtime/PrivateWorldRuntime.Environment.cs',
    'docs/game-design/world.md',
  ],
  'area:saves': [
    'src/ClankerWorld.Simulation/Persistence/',
    'src/ClankerWorld.Simulation/Playtest/Runtime/PrivateWorldHistory.cs',
    'src/ClankerWorld.Viewer/Observation/Persistence/',
    'src/ClankerWorld.Viewer/Observation/Worlds/',
    'docs/development/saves-and-replay.md',
    'docs/game-design/saves.md',
  ],
  'area:interface': [
    'src/ClankerWorld.GodotClient/',
    'docs/game-design/interface-and-art.md',
    'docs/playing.md',
  ],
  'area:server': [
    'src/ClankerWorld.Viewer/',
    'deploy/',
    'docs/development/device-pairing.md',
  ],
  'area:tooling': [
    '.github/',
    '.claude/',
    'scripts/',
    'global.json',
    'Directory.Build.props',
    'ClankerWorld.sln',
  ],
};
const AreaOrder = Object.keys(AreaRules);
// Most features also touch the client to show themselves, so client files count
// half; a change that is mostly about the interface still gets area:interface.
const AreaWeight = { 'area:interface': 0.5 };

function areaFor(file) {
  let best = null;
  let bestLength = -1;
  for (const [area, prefixes] of Object.entries(AreaRules)) {
    for (const prefix of prefixes) {
      if (file.startsWith(prefix) && prefix.length > bestLength) {
        best = area;
        bestLength = prefix.length;
      }
    }
  }
  return best;
}

function areaLabels(files) {
  const counts = new Map();
  let tests = 0;
  for (const file of files) {
    if (file.startsWith('tests/')) {
      tests += 1;
      continue;
    }
    const area = areaFor(file);
    if (area) counts.set(area, (counts.get(area) ?? 0) + (AreaWeight[area] ?? 1));
  }
  if (counts.size === 0 && tests > 0) return ['area:tooling'];
  const ranked = [...counts.entries()]
    .sort((a, b) => b[1] - a[1] || AreaOrder.indexOf(a[0]) - AreaOrder.indexOf(b[0]));
  const chosen = ranked.slice(0, 1).map(([area]) => area);
  if (ranked.length > 1 && ranked[1][1] * 3 >= ranked[0][1]) chosen.push(ranked[1][0]);
  return chosen;
}

function isDocumentation(file) {
  return file.endsWith('.md') || file.startsWith('docs/');
}

function higherPriority(a, b) {
  if (a === null) return b;
  if (b === null) return a;
  return Priorities.indexOf(a) <= Priorities.indexOf(b) ? a : b;
}

async function setAreaLabels({ github, core, repo, pr, files }) {
  const wanted = areaLabels(files);
  const current = labelNames(pr.labels);
  for (const name of current) {
    if (name.startsWith('area:') && !wanted.includes(name)) await removeLabel(github, repo, pr.number, name);
  }
  const missing = wanted.filter(name => !current.includes(name));
  if (missing.length > 0) {
    await github.rest.issues.addLabels({ ...repo, issue_number: pr.number, labels: missing });
  }
  core.info(`Areas for this pull request: ${wanted.join(', ') || 'none'}.`);
}

// The first ticked box, in the template's order; null when none sets a type.
function typeLabel(body) {
  const ticked = box => new RegExp(`^\\s*[-*] \\[[xX]\\] ${box}\\s*$`, 'm').test(body ?? '');
  if (ticked('Bug fix')) return 'type:bug';
  if (ticked('New or changed gameplay') || ticked('UI or game text')) return 'type:feature';
  if (ticked('Documentation')) return 'type:docs';
  return null;
}

async function removeLabel(github, repo, number, name) {
  try {
    await github.rest.issues.removeLabel({ ...repo, issue_number: number, name });
  } catch (error) {
    if (error.status !== 404) throw error;
  }
}

async function openIssue(github, repo, number, includeClosed = false) {
  try {
    const { data } = await github.rest.issues.get({ ...repo, issue_number: number });
    return data.pull_request || (!includeClosed && data.state !== 'open') ? null : data;
  } catch (error) {
    if (error.status === 404) return null;
    throw error;
  }
}

async function otherOpenPrLinks(github, repo, number, exceptPr) {
  const open = await github.paginate(github.rest.pulls.list, { ...repo, state: 'open', per_page: 100 });
  return open.some(pr => pr.number !== exceptPr && closingIssueNumbers(pr.body, `${repo.owner}/${repo.repo}`).has(number));
}

async function releaseIssue({ github, core, repo, number, prNumber, merged }) {
  const issue = await openIssue(github, repo, number, true);
  if (!issue || issue.state === 'open' && await otherOpenPrLinks(github, repo, number, prNumber)) return;
  const names = labelNames(issue.labels);
  if (names.includes(HasPr)) {
    await removeLabel(github, repo, number, HasPr);
    core.info(`Removed ${HasPr} from #${number}.`);
  }
  if (issue.state !== 'open') return;
  const hasOtherStatus = names.some(name => name.startsWith('status:') && name !== HasPr);
  if (!merged && !hasOtherStatus) {
    await github.rest.issues.addLabels({ ...repo, issue_number: number, labels: [Ready] });
    core.info(`Put #${number} back to ${Ready}.`);
  }
  // Another PR can acquire this issue while cleanup is in progress. Recheck
  // after the writes so this cleanup cannot finish by erasing its link.
  if (await otherOpenPrLinks(github, repo, number, prNumber)) {
    await github.rest.issues.addLabels({ ...repo, issue_number: number, labels: [HasPr] });
    await removeLabel(github, repo, number, Ready);
  }
}

async function labelPullRequest({ github, context, core }) {
  const repo = context.repo;
  const action = context.payload.action;
  // Events can wait in the workflow queue while the PR is edited, claimed,
  // drafted, closed or reopened. Reconcile the current PR, not that snapshot.
  const { data: pr } = await github.rest.pulls.get({
    ...repo, pull_number: context.payload.pull_request.number,
  });
  const repoName = `${repo.owner}/${repo.repo}`;
  const linked = closingIssueNumbers(pr.body, repoName);

  if (pr.state === 'closed') {
    await removeLabel(github, repo, pr.number, NeedsReview);
    await removeLabel(github, repo, pr.number, Reviewing);
    for (const number of linked) {
      await releaseIssue({ github, core, repo, number, prNumber: pr.number, merged: pr.merged });
    }
    return;
  }
  if (pr.state !== 'open') return;

  const files = (await github.paginate(github.rest.pulls.listFiles, {
    ...repo, pull_number: pr.number, per_page: 100,
  })).map(file => file.filename);
  if (['opened', 'reopened', 'ready_for_review'].includes(action)) {
    await setAreaLabels({ github, core, repo, pr, files });
  }

  const current = labelNames(pr.labels);
  const type = typeLabel(pr.body) ?? (files.length > 0 && files.every(isDocumentation) ? 'type:docs' : null);
  if (type !== null) {
    for (const name of current) {
      if (name.startsWith('type:') && name !== type) await removeLabel(github, repo, pr.number, name);
    }
  }
  const prLabels = type !== null ? [type] : [];
  if (!pr.draft) prLabels.push(NeedsReview);
  if (prLabels.length > 0) {
    await github.rest.issues.addLabels({ ...repo, issue_number: pr.number, labels: prLabels });
    core.info(`Added ${prLabels.join(', ')} to this pull request.`);
  }
  if (pr.draft) {
    await removeLabel(github, repo, pr.number, NeedsReview);
    await removeLabel(github, repo, pr.number, Reviewing);
  }

  let priority = files.some(file => WorkflowPaths.some(path => file.startsWith(path))) ? WorkflowPriority : null;
  for (const number of linked) {
    const issue = await openIssue(github, repo, number);
    if (!issue) continue;
    await github.rest.issues.addLabels({ ...repo, issue_number: number, labels: [HasPr] });
    await removeLabel(github, repo, number, Ready);
    if (!pr.draft) await removeLabel(github, repo, number, InProgress);
    core.info(`Marked #${number} as ${HasPr}${pr.draft ? '' : ` and cleared ${InProgress}`}.`);
    const issueLabels = labelNames(issue.labels);
    for (const name of Priorities) {
      if (issueLabels.includes(name)) priority = higherPriority(priority, name);
    }
  }

  // The pull request takes the highest priority of the open issues it closes,
  // and at least P1 if it changes the repository's workflow.
  if (priority !== null) {
    for (const name of Priorities) {
      if (name !== priority && current.includes(name)) await removeLabel(github, repo, pr.number, name);
    }
    if (!current.includes(priority)) {
      await github.rest.issues.addLabels({ ...repo, issue_number: pr.number, labels: [priority] });
      core.info(`Set this pull request to ${priority}.`);
    }
  }

  // An edit that drops a reference releases that issue.
  const previousBody = context.payload.changes?.body?.from;
  if (action === 'edited' && previousBody !== undefined) {
    for (const number of closingIssueNumbers(previousBody, repoName)) {
      if (!linked.has(number)) {
        await releaseIssue({ github, core, repo, number, prNumber: pr.number, merged: false });
      }
    }
  }
}

module.exports = labelPullRequest;
module.exports.closingIssueNumbers = closingIssueNumbers;
module.exports.typeLabel = typeLabel;
module.exports.areaLabels = areaLabels;
