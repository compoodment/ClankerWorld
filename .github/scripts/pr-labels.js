// Keeps pull request and linked-issue labels current.
// Run by .github/workflows/pr-labels.yml through actions/github-script.
// It reads only the event payload and GitHub data; it never runs pull request code.
//
// Changed files: a PR whose base branch is not main is judged only by the
// files that also differ from main (main...head), so main's own changes that
// its base branch has not caught up with do not count. If that comparison
// fails or lists 300 or more files, the PR's own file list is used.
//
// - At most two area labels, from the changed files, set when the PR opens, is
//   reopened, is marked ready, gets new commits or has its base branch
//   changed. Each file counts towards the one area whose path rule matches it
//   most closely; client files count half and tests count only when nothing
//   else does. The second area is kept only if it has at least a third of the
//   first one's count. Areas are left as they are when no rule matches any
//   changed file, and for good once anyone other than github-actions[bot] has
//   added or removed an area label on the PR.
// - One type label: the first ticked "Type of change" box in the PR template
//   ("Refactor or tooling" gives type:tooling), or type:docs when no box is
//   ticked and every changed file is documentation.
// - status:needs-review while a PR is open and not a draft. When the PR closes
//   or goes back to draft, status:needs-review and a reviewer's
//   status:reviewing claim are removed.
// - A priority label, worked out again on every event: the highest priority of
//   the open issues the PR closes; if none of them has one, of the open issues
//   it names with "Refs" ("Refs #N", or a list such as "Refs #N, #M and #K");
//   otherwise priority:p2. It is priority:p0 instead when the PR changes how
//   everyone works on the repository (.github/, .claude/, CONTRIBUTING.md,
//   AGENTS.md or CLAUDE.md). Other priority labels that github-actions[bot]
//   added are removed. A priority anyone else added is never removed: a
//   higher one takes the worked-out priority's place, and a lower one stays
//   next to it.
// - status:has-pr on open issues the PR closes ("Closes #N", "Fixes #N" or
//   "Resolves #N", one keyword directly before each number, exactly as GitHub
//   and close-fixed-issues.yml read them), replacing status:needs-pr. "Refs #N"
//   changes none of the issue's labels.
//   Once the PR is ready for review (not a draft), the issue's claim label
//   status:in-progress is removed too, so the issue shows only status:has-pr.
//   An already queued, released draft's issue stays in the queue, including
//   when status:has-pr is first added, until someone claims or holds it or a
//   PR closing it is ready for review.
//   When a closing PR goes back to draft and neither the issue
//   (status:in-progress, status:blocked, status:needs-decision, status:parked)
//   nor the PR (status:needs-decision, status:blocked) is held, and the issue
//   is not type:decision or owner-task, the issue gets status:needs-pr next to
//   status:has-pr, unless another ready PR closes it.
//   While a draft PR is held (status:needs-decision or status:blocked), every
//   push or edit takes status:needs-pr off the issues it closes, so a hold
//   added after the PR went back to draft also keeps them out of the queue.
//   When the last open PR closing an issue closes, or stops naming it with a
//   closing keyword, status:has-pr is removed again. If that PR did not merge,
//   the issue goes back to status:needs-pr unless it already has one of
//   status:needs-pr, status:in-progress, status:needs-decision, status:blocked
//   or status:parked, or is type:decision or owner-task. Other status: labels,
//   such as retired ones, do not hold it back.
// - Events on a PR that is already closed, such as an edit after merging,
//   change nothing.

const NeedsReview = 'status:needs-review';
const Reviewing = 'status:reviewing';
const HasPr = 'status:has-pr';
const Ready = 'status:needs-pr';
const InProgress = 'status:in-progress';
const NeedsDecision = 'status:needs-decision';
const Blocked = 'status:blocked';
const Parked = 'status:parked';
// Someone holds or waits on an issue with one of these, so a PR going back to
// draft must not put it in the queue.
const HeldStatuses = [InProgress, NeedsDecision, Blocked, Parked];
// Statuses that keep a released issue from going back to the queue. Any other
// status: label (a retired or hand-made one) must not.
const HoldingStatuses = [Ready, ...HeldStatuses];
// Decisions and owner tasks are never pull request work (CONTRIBUTING Labels;
// stale-claims.js canQueue), so they never get status:needs-pr.
const NotQueueable = ['type:decision', 'owner-task'];
const Priorities = ['priority:p0', 'priority:p1', 'priority:p2', 'priority:p3'];
const DefaultPriority = 'priority:p2';
// Changes to CI, labels, templates or the contribution rules affect every
// agent, so they are P0.
const WorkflowPaths = ['.github/', '.claude/', '.agents/', 'skills/', 'CONTRIBUTING.md', 'AGENTS.md', 'CLAUDE.md'];
const WorkflowPriority = 'priority:p0';
// The actor GitHub records for labels this workflow writes with its token.
const LabelBot = 'github-actions[bot]';
// The compare API lists at most 300 changed files.
const CompareFileLimit = 300;

// Mirrors .github/workflows/close-fixed-issues.yml: one keyword per issue,
// ignoring HTML comments and code.
const KeywordPattern = /\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?):?\s+(?:([\w.-]+\/[\w.-]+))?#(\d+)\b/gi;
// "Refs" before one issue or a list of them: "Refs #7", "Refs: #7, #8",
// "Refs #598, #599 and #602". The first word that is not another issue ends
// the list, so "Refs #666, the rule from PR #689" refers only to #666.
// stale-claims.js uses referencedIssueNumbers too, so "Refs" means the same
// to both scripts.
const IssueRef = String.raw`(?:[\w.-]+\/[\w.-]+)?#\d+\b`;
const RefsPattern = new RegExp(
  String.raw`\brefs?:?\s+(${IssueRef}(?:\s*(?:,\s*(?:and\s+)?|and\s+|&\s*)${IssueRef})*)`, 'gi');
// The older template line, "- Refs (related or partly completed issues, and
// what remains): #7, #8", counts every issue after its colon. Its label has no
// issue number, so a note after "- Refs #7:" is not read as more references.
const RefsLinePattern = /^\s*[-*]\s*Refs\b[^:#\n]*:(.*)$/gim;
// The current template line, "- Refs #  (and what remains):", may be followed
// by a list after its colon; like "Refs #7", only that leading list counts.
const RefsTemplatePattern = new RegExp(
  String.raw`^\s*[-*]\s*Refs\s+#\s*\(and what remains\)\s*:?\s*(${IssueRef}(?:\s*(?:,\s*(?:and\s+)?|and\s+|&\s*)${IssueRef})*)`, 'gim');
// One issue in a Refs list, optionally as owner/repo#N. "PR#5" and "a.md#5"
// are not issues.
const IssueRefPattern = /(?<![\w./-])(?:([\w.-]+\/[\w.-]+))?#(\d+)\b/g;

function labelNames(labels) {
  return (labels ?? []).map(label => (typeof label === 'string' ? label : label.name));
}

function withoutCode(body) {
  return (body ?? '')
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`[^`\n]*`/g, ' ');
}

function keywordNumbers(pattern, text, repoName, numbers = new Set()) {
  const thisRepo = repoName.toLowerCase();
  for (const [, otherRepo, number] of text.matchAll(pattern)) {
    if (!otherRepo || otherRepo.toLowerCase() === thisRepo) numbers.add(Number(number));
  }
  return numbers;
}

function closingIssueNumbers(body, repoName = '') {
  return keywordNumbers(KeywordPattern, withoutCode(body), repoName);
}

// Issues this PR refers to with "Refs", ignoring HTML comments, code and
// other repositories' issues.
function referencedIssueNumbers(body, repoName = '') {
  const text = withoutCode(body);
  const numbers = new Set();
  const lists = [...text.matchAll(RefsPattern), ...text.matchAll(RefsLinePattern), ...text.matchAll(RefsTemplatePattern)]
    .map(([, list]) => list);
  for (const list of lists) keywordNumbers(IssueRefPattern, list, repoName, numbers);
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
    'docs/development/private-server-deployment.md',
  ],
  // changes/, playtest/ and docs/what-works.md stay unmapped: most feature PRs
  // add a file there, and the one-third rule would then add area:tooling.
  'area:tooling': [
    '.github/',
    '.claude/',
    '.agents/',
    'skills/',
    'scripts/',
    'global.json',
    'Directory.Build.props',
    'ClankerWorld.sln',
    'CONTRIBUTING.md',
    'AGENTS.md',
    'CLAUDE.md',
    'docs/development/build-and-test.md',
    'docs/development/releasing.md',
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

function isWorkflowFile(file) {
  return WorkflowPaths.some(path => file.startsWith(path));
}

function higherPriority(a, b) {
  if (a === null) return b;
  if (b === null) return a;
  return Priorities.indexOf(a) <= Priorities.indexOf(b) ? a : b;
}

// True when anyone other than this workflow has added or removed an area label.
function areasSetByHand(events) {
  return events.some(event => (event.event === 'labeled' || event.event === 'unlabeled') &&
    event.label?.name?.startsWith('area:') && event.actor?.login !== LabelBot);
}

// True when this workflow added the label most recently. A label with no
// recorded event counts as set by hand, so it is never removed by mistake.
function addedByBot(events, name) {
  const added = events.filter(event => event.event === 'labeled' && event.label?.name === name).pop();
  return added?.actor?.login === LabelBot;
}

async function setAreaLabels({ github, core, repo, pr, files, events }) {
  const wanted = areaLabels(files);
  if (wanted.length === 0) {
    core.info('No area rule matches the changed files; areas left as they are.');
    return;
  }
  const current = labelNames(pr.labels);
  const currentAreas = current.filter(name => name.startsWith('area:'));
  // Most pushes change nothing here, so the label history is read only when
  // the areas would change.
  if (currentAreas.length === wanted.length && wanted.every(name => currentAreas.includes(name))) {
    core.info(`Areas already match the changed files: ${wanted.join(', ')}.`);
    return;
  }
  if (areasSetByHand(await events())) {
    core.info('Areas were changed by hand on this pull request; leaving them.');
    return;
  }
  for (const name of current) {
    if (name.startsWith('area:') && !wanted.includes(name)) await removeLabel(github, repo, pr.number, name);
  }
  const missing = wanted.filter(name => !current.includes(name));
  if (missing.length > 0) {
    await github.rest.issues.addLabels({ ...repo, issue_number: pr.number, labels: missing });
  }
  core.info(`Areas for this pull request: ${wanted.join(', ')}.`);
}

// The first ticked box, in the template's order; null when no box is ticked.
function typeLabel(body) {
  const ticked = box => new RegExp(`^\\s*[-*] \\[[xX]\\] ${box}\\s*$`, 'm').test(body ?? '');
  if (ticked('Bug fix')) return 'type:bug';
  if (ticked('New or changed gameplay') || ticked('UI or game text')) return 'type:feature';
  if (ticked('Documentation')) return 'type:docs';
  if (ticked('Refactor or tooling')) return 'type:tooling';
  return null;
}

// A stacked PR's base branch can be behind main. Once the head has merged main
// but the base has not, main's own changes show up in the PR's file list. Keep
// only the files that also differ from main, so areas, type and the workflow
// priority describe what the PR would change on main. This only refines the
// list, so when the comparison fails for any reason (a missing branch, a diff
// too large to compare, a rate limit or a server error) the PR's own files are
// used and every label is still written.
async function filesChangedOnMain({ github, core, repo, pr, files, defaultBranch }) {
  if (!pr.base?.ref || pr.base.ref === defaultBranch || !pr.head?.sha) return files;
  try {
    const { data } = await github.rest.repos.compareCommitsWithBasehead({
      ...repo, basehead: `${defaultBranch}...${pr.head.sha}`, per_page: 1,
    });
    const onMain = (data.files ?? []).map(file => file.filename);
    if (onMain.length >= CompareFileLimit) {
      core.info(`The comparison with ${defaultBranch} was cut short; using the files changed against ${pr.base.ref}.`);
      return files;
    }
    const kept = new Set(onMain);
    return files.filter(file => kept.has(file));
  } catch (error) {
    core.warning(`Could not compare with ${defaultBranch} (${error.status ?? error.message}); ` +
      `using the files changed against ${pr.base.ref}.`);
    return files;
  }
}

// Sets the worked-out priority. A priority set by anyone else stays, and wins
// when it is higher; every other priority label this workflow added is removed.
// addedThisRun holds the priorities an earlier pass of this run added, which
// the PR's event history may not show yet.
async function setPriority({ github, core, repo, pr, current, priority, events, addedThisRun }) {
  const present = Priorities.filter(name => current.includes(name));
  // The history is needed only when a label other than the worked-out one is on the PR.
  const history = present.some(name => name !== priority) ? await events() : [];
  // In priority order, so byHand[0] is the highest priority set by hand.
  const byHand = present.filter(name => !addedThisRun.has(name) && !addedByBot(history, name));
  const winner = higherPriority(byHand[0] ?? null, priority);
  for (const name of present) {
    if (name === winner || byHand.includes(name)) continue;
    await removeLabel(github, repo, pr.number, name);
    core.info(`Removed ${name}, which this workflow had set earlier.`);
  }
  if (winner !== priority) {
    core.info(`Kept ${winner}, which was set by hand and is higher than ${priority}.`);
  } else if (!present.includes(priority)) {
    await github.rest.issues.addLabels({ ...repo, issue_number: pr.number, labels: [priority] });
    addedThisRun.add(priority);
    core.info(`Set this pull request to ${priority}.`);
  }
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
  const held = names.some(name => HoldingStatuses.includes(name));
  const queueable = !names.some(name => NotQueueable.includes(name));
  if (!merged && !held && queueable) {
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

// Removes the review labels from a closed or draft PR. The PR is read once,
// and only the labels it has are removed, so a push to a plain draft costs one
// read and no removals. The PR can be reopened or marked ready, and claimed by
// a reviewer, while this runs, so it is read again before every removal after
// the first; once it is open and ready again, nothing more is removed. A label
// added after the first read may be left on, which is the safe side: this
// never removes a label from a PR that is open and ready.
async function clearInactiveReviewLabels(github, repo, number) {
  const read = async () => (await github.rest.pulls.get({ ...repo, pull_number: number })).data;
  let live = await read();
  let fresh = true;
  for (const name of [NeedsReview, Reviewing]) {
    if (!labelNames(live.labels).includes(name)) continue;
    if (!fresh) live = await read();
    if (live.state === 'open' && !live.draft) return;
    await removeLabel(github, repo, number, name);
    fresh = false;
  }
}

async function applyPullRequestLabels({ github, context, core, pr, previousBodies, addedPriorities, wentBackToDraft = false }) {
  const repo = context.repo;
  const action = context.payload.action;
  const repoName = `${repo.owner}/${repo.repo}`;
  const linked = closingIssueNumbers(pr.body, repoName);

  if (pr.state === 'closed') {
    await clearInactiveReviewLabels(github, repo, pr.number);
    const released = new Set(linked);
    for (const body of previousBodies) {
      for (const number of closingIssueNumbers(body, repoName)) released.add(number);
    }
    for (const number of released) {
      await releaseIssue({ github, core, repo, number, prNumber: pr.number, merged: pr.merged });
    }
    return;
  }
  if (pr.state !== 'open') return;

  const files = (await github.paginate(github.rest.pulls.listFiles, {
    ...repo, pull_number: pr.number, per_page: 100,
  })).map(file => file.filename);
  const defaultBranch = pr.base?.repo?.default_branch ?? context.payload.repository?.default_branch ?? 'main';
  const changed = await filesChangedOnMain({ github, core, repo, pr, files, defaultBranch });
  // The PR's label history, read at most once per pass and only when needed.
  let events = null;
  const prEvents = async () => events ??= await github.paginate(github.rest.issues.listEvents, {
    ...repo, issue_number: pr.number, per_page: 100,
  });

  // Pushes count too: when a lower PR is squash-merged and GitHub moves a
  // stacked PR to main, the file list still holds the lower PR's files until
  // main is merged in, and that push corrects the areas.
  const baseChanged = action === 'edited' && Boolean(context.payload.changes?.base);
  if (['opened', 'reopened', 'ready_for_review', 'synchronize'].includes(action) || baseChanged) {
    await setAreaLabels({ github, core, repo, pr, files: changed, events: prEvents });
  }

  const current = labelNames(pr.labels);
  const type = typeLabel(pr.body) ?? (changed.length > 0 && changed.every(isDocumentation) ? 'type:docs' : null);
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
    await clearInactiveReviewLabels(github, repo, pr.number);
  }

  // A draft PR waiting on the owner or on something else keeps its issues out
  // of the queue. Adding a label does not run this workflow, so a hold added
  // after the PR went back to draft takes status:needs-pr off its issues at the
  // next push or edit.
  const prHeld = current.includes(NeedsDecision) || current.includes(Blocked);
  let issuePriority = null;
  for (const number of linked) {
    const issue = await openIssue(github, repo, number);
    if (!issue) continue;
    const issueLabels = labelNames(issue.labels);
    await github.rest.issues.addLabels({ ...repo, issue_number: number, labels: [HasPr] });
    const liveIssue = await openIssue(github, repo, number);
    const liveNames = labelNames(liveIssue?.labels);
    const queueable = pr.draft && liveIssue !== null && !prHeld &&
      !liveNames.some(name => HeldStatuses.includes(name)) &&
      !liveNames.some(name => NotQueueable.includes(name));
    // A ready PR sent back to draft leaves an issue nobody holds (its claim
    // ended when the PR was marked ready). Put it back in the queue next to
    // status:has-pr, so any fixing agent can claim it and continue the branch.
    // wentBackToDraft: an earlier pass of this run saw the PR ready, so another
    // run's hand-back may have been undone by this run's ready-state pass.
    const handedBack = (action === 'converted_to_draft' || wentBackToDraft) && queueable;
    // A released draft keeps its queue entry until someone claims the issue,
    // a closing PR becomes ready or the draft is held. Edits and synchronize
    // events alone must not hide unclaimed work again. The first labeling run
    // may arrive after release, before the issue has ever had status:has-pr.
    const abandoned = queueable && (handedBack || liveNames.includes(Ready));
    const open = abandoned ? await github.paginate(github.rest.pulls.list, { ...repo, state: 'open', per_page: 100 }) : [];
    const readyClosing = open.some(other => !other.draft && closingIssueNumbers(other.body, repoName).has(number));
    if (handedBack && !readyClosing) {
      await github.rest.issues.addLabels({ ...repo, issue_number: number, labels: [Ready] });
      core.info(`This pull request went back to draft and nobody holds #${number}; put it back to ${Ready}.`);
    }
    if (!abandoned || readyClosing) await removeLabel(github, repo, number, Ready);
    if (!pr.draft) await removeLabel(github, repo, number, InProgress);
    core.info(`Marked #${number} as ${HasPr}${pr.draft ? '' : ` and cleared ${InProgress}`}.`);
    for (const name of Priorities) {
      if (issueLabels.includes(name)) issuePriority = higherPriority(issuePriority, name);
    }
  }

  // The PR takes the highest priority of the open issues it closes; if none has
  // one, of the open issues it Refs; otherwise P2. Changing how we work makes
  // it P0. Worked out on every event, so a stale label never sticks.
  if (issuePriority === null) {
    for (const number of referencedIssueNumbers(pr.body, repoName)) {
      if (linked.has(number)) continue;
      const issue = await openIssue(github, repo, number);
      const issueLabels = labelNames(issue?.labels);
      for (const name of Priorities) {
        if (issueLabels.includes(name)) issuePriority = higherPriority(issuePriority, name);
      }
    }
  }
  const workflowPriority = changed.some(isWorkflowFile) ? WorkflowPriority : null;
  const priority = higherPriority(workflowPriority, issuePriority ?? DefaultPriority);
  await setPriority({ github, core, repo, pr, current, priority, events: prEvents, addedThisRun: addedPriorities });

  // Release references from the event or an earlier reconciliation pass.
  for (const previousBody of previousBodies) {
    for (const number of closingIssueNumbers(previousBody, repoName)) {
      if (!linked.has(number)) {
        await releaseIssue({ github, core, repo, number, prNumber: pr.number, merged: false });
      }
    }
  }
}

async function labelPullRequest({ github, context, core }) {
  const request = { ...context.repo, pull_number: context.payload.pull_request.number };
  let { data: pr } = await github.rest.pulls.get(request);
  const previousBodies = new Set([
    context.payload.pull_request.body,
    context.payload.changes?.body?.from,
  ]);
  const addedPriorities = new Set();
  let wentBackToDraft = false;
  for (let attempt = 0; attempt < 3; attempt++) {
    await applyPullRequestLabels({ github, context, core, pr, previousBodies, addedPriorities, wentBackToDraft });
    const { data: live } = await github.rest.pulls.get(request);
    if (live.state === pr.state && live.draft === pr.draft &&
        live.merged === pr.merged && live.body === pr.body) return;
    previousBodies.add(pr.body);
    wentBackToDraft = wentBackToDraft || (!pr.draft && live.draft && live.state === 'open');
    pr = live;
  }
  throw new Error('The PR kept changing during label reconciliation; retry against its current state.');
}

module.exports = labelPullRequest;
module.exports.closingIssueNumbers = closingIssueNumbers;
module.exports.referencedIssueNumbers = referencedIssueNumbers;
module.exports.typeLabel = typeLabel;
module.exports.areaLabels = areaLabels;
module.exports.isDocumentation = isDocumentation;
