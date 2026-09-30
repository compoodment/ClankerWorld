// Keeps pull request and linked-issue labels current.
// Run by .github/workflows/pr-labels.yml through actions/github-script.
// It reads only the event payload and GitHub data; it never runs pull request code.
//
// - A type label from the ticked "Type of change" box in the PR template.
// - status:needs-review while a PR is open and not a draft.
// - status:has-pr on open issues the PR links as described in CONTRIBUTING
//   ("Closes #N", "Fixes #N", "Resolves #N" or "Refs #N", one keyword per issue,
//   or any #N on the template's Closes and Refs lines), replacing status:ready.
//   When the last open PR linking an issue closes, status:has-pr is removed
//   again; an issue whose PR closed without merging goes back to status:ready
//   if it has no other status.

const NeedsReview = 'status:needs-review';
const HasPr = 'status:has-pr';
const Ready = 'status:ready';

// Mirrors .github/workflows/close-fixed-issues.yml: one keyword per issue,
// ignoring HTML comments and code. Refs links an issue without closing it.
const KeywordPattern = /\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?|refs?):?\s+(?:([\w.-]+\/[\w.-]+))?#(\d+)\b/gi;
const TemplateLinePattern = /^\s*[-*]\s*(?:Closes|Refs)\b[^:\n]*:(.*)$/gim;

function linkedIssueNumbers(body, repoName = '') {
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

function typeLabels(body) {
  const ticked = box => new RegExp(`^\\s*[-*] \\[[xX]\\] ${box}\\s*$`, 'm').test(body ?? '');
  const labels = new Set();
  if (ticked('Bug fix')) labels.add('type:bug');
  if (ticked('New or changed gameplay') || ticked('UI or game text')) labels.add('type:feature');
  if (ticked('Documentation') && labels.size === 0 && !ticked('Refactor or tooling')) labels.add('type:docs');
  return labels;
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
  return open.some(pr => pr.number !== exceptPr && linkedIssueNumbers(pr.body, `${repo.owner}/${repo.repo}`).has(number));
}

async function releaseIssue({ github, core, repo, number, prNumber, merged }) {
  const issue = await openIssue(github, repo, number, true);
  if (!issue || issue.state === 'open' && await otherOpenPrLinks(github, repo, number, prNumber)) return;
  const names = issue.labels.map(label => (typeof label === 'string' ? label : label.name));
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
}

async function labelPullRequest({ github, context, core }) {
  const repo = context.repo;
  const action = context.payload.action;
  const pr = context.payload.pull_request;
  const repoName = `${repo.owner}/${repo.repo}`;
  const linked = linkedIssueNumbers(pr.body, repoName);

  if (action === 'closed') {
    await removeLabel(github, repo, pr.number, NeedsReview);
    for (const number of linked) {
      await releaseIssue({ github, core, repo, number, prNumber: pr.number, merged: pr.merged });
    }
    return;
  }

  const types = typeLabels(pr.body);
  const prLabels = [...types];
  if (!pr.draft) prLabels.push(NeedsReview);
  if (prLabels.length > 0) {
    await github.rest.issues.addLabels({ ...repo, issue_number: pr.number, labels: prLabels });
    core.info(`Added ${prLabels.join(', ')} to this pull request.`);
  }
  if (pr.draft) await removeLabel(github, repo, pr.number, NeedsReview);

  for (const number of linked) {
    const issue = await openIssue(github, repo, number);
    if (!issue) continue;
    await github.rest.issues.addLabels({ ...repo, issue_number: number, labels: [HasPr] });
    await removeLabel(github, repo, number, Ready);
    core.info(`Marked #${number} as ${HasPr}.`);
  }

  // An edit that drops a reference releases that issue.
  const previousBody = context.payload.changes?.body?.from;
  if (action === 'edited' && previousBody !== undefined) {
    for (const number of linkedIssueNumbers(previousBody, repoName)) {
      if (!linked.has(number)) {
        await releaseIssue({ github, core, repo, number, prNumber: pr.number, merged: false });
      }
    }
  }
}

module.exports = labelPullRequest;
module.exports.linkedIssueNumbers = linkedIssueNumbers;
module.exports.typeLabels = typeLabels;
