// Releases claims that have gone quiet, so status:in-progress and
// status:reviewing always mean someone has worked recently.
// Run every hour by .github/workflows/stale-claims.yml through actions/github-script.
// It reads only GitHub data; it never checks out or runs pull request code.
//
// - An issue's status:in-progress claim is released after 4 hours without a
//   sign of work. Work counts as: the claim itself, a comment on the issue, a
//   commit pushed to an open pull request that closes or refers to it, a
//   comment on such a pull request, or a commit on a branch named in the
//   issue's comments. The issue goes back to status:needs-pr, unless it is
//   blocked or is not pull request work, and a comment names the draft pull
//   request or branch to continue from.
// - A pull request's status:reviewing claim is released after 2 hours without
//   the claim, a pushed commit or a comment.
// - Anything waiting on the owner (status:needs-decision on the issue or its
//   pull request) keeps its claim.
// Comments this script writes carry a marker and never count as work.

const { closingIssueNumbers } = require('./pr-labels.js');

const InProgress = 'status:in-progress';
const Reviewing = 'status:reviewing';
const NeedsPr = 'status:needs-pr';
const NeedsDecision = 'status:needs-decision';
const Blocked = 'status:blocked';
const IssueHours = 4;
const ReviewHours = 2;
const Hour = 60 * 60 * 1000;
const Marker = '<!-- claim-check -->';

const RefsPattern = /\brefs?:?\s+(?:([\w.-]+\/[\w.-]+))?#(\d+)\b/gi;
const RefsLinePattern = /^\s*[-*]\s*Refs\b[^:\n]*:(.*)$/gim;
const BranchPatterns = [
  /`([A-Za-z0-9._-]+\/[A-Za-z0-9._\/-]+)`/g,
  /\bbranch\s+([A-Za-z0-9._-]+\/[A-Za-z0-9._\/-]*[A-Za-z0-9_\/-])/gi,
];

function labelNames(labels) {
  return (labels ?? []).map(label => (typeof label === 'string' ? label : label.name));
}

function referencedIssueNumbers(body, repoName) {
  const thisRepo = repoName.toLowerCase();
  const text = (body ?? '')
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`[^`\n]*`/g, ' ');
  const numbers = new Set();
  for (const [, otherRepo, number] of text.matchAll(RefsPattern)) {
    if (!otherRepo || otherRepo.toLowerCase() === thisRepo) numbers.add(Number(number));
  }
  for (const [, rest] of text.matchAll(RefsLinePattern)) {
    for (const [, number] of rest.matchAll(/(?<![\w/])#(\d+)\b/g)) numbers.add(Number(number));
  }
  return numbers;
}

function branchNames(comments) {
  const names = new Set();
  for (const comment of comments) {
    for (const pattern of BranchPatterns) {
      for (const [, name] of (comment.body ?? '').matchAll(pattern)) names.add(name.replace(/[.,;:]+$/, ''));
    }
  }
  return [...names].slice(0, 5);
}

function newest(times) {
  return times.filter(Boolean).map(time => Date.parse(time)).filter(Number.isFinite).reduce((a, b) => Math.max(a, b), 0);
}

async function workComments(github, repo, number) {
  const comments = await github.paginate(github.rest.issues.listComments, { ...repo, issue_number: number, per_page: 100 });
  return comments.filter(comment => !(comment.body ?? '').includes(Marker));
}

async function lastLabeled(github, repo, number, name) {
  const events = await github.paginate(github.rest.issues.listEvents, { ...repo, issue_number: number, per_page: 100 });
  return newest(events.filter(event => event.event === 'labeled' && event.label?.name === name).map(event => event.created_at));
}

async function commitTime(github, repo, ref) {
  try {
    const { data } = await github.rest.repos.getCommit({ ...repo, ref });
    return data.commit?.committer?.date ?? null;
  } catch (error) {
    if (error.status === 404 || error.status === 422) return null;
    throw error;
  }
}

async function removeLabel(github, repo, number, name) {
  try {
    await github.rest.issues.removeLabel({ ...repo, issue_number: number, name });
  } catch (error) {
    if (error.status !== 404) throw error;
  }
}

function age(now, last) {
  return last === 0 ? 'any recorded activity' : `${Math.floor((now - last) / Hour)} hours`;
}

async function releaseIssueClaims({ github, core, repo, repoName, openPrs, now, dryRun }) {
  const issues = (await github.paginate(github.rest.issues.listForRepo, {
    ...repo, state: 'open', labels: InProgress, per_page: 100,
  })).filter(issue => !issue.pull_request);

  for (const issue of issues) {
    const labels = labelNames(issue.labels);
    if (labels.includes(NeedsDecision)) continue;
    const closing = openPrs.filter(pr => closingIssueNumbers(pr.body, repoName).has(issue.number));
    const linked = [...closing, ...openPrs.filter(pr => !closing.includes(pr) && referencedIssueNumbers(pr.body, repoName).has(issue.number))];
    if (linked.some(pr => labelNames(pr.labels).includes(NeedsDecision))) continue;

    const comments = await workComments(github, repo, issue.number);
    const times = [await lastLabeled(github, repo, issue.number, InProgress), newest(comments.map(c => c.created_at))];
    for (const pr of linked) {
      times.push(newest([await commitTime(github, repo, pr.head.sha)]));
      times.push(newest((await workComments(github, repo, pr.number)).map(c => c.created_at)));
    }
    const prBranches = new Set(linked.map(pr => pr.head.ref));
    for (const branch of branchNames(comments).filter(name => !prBranches.has(name))) {
      times.push(newest([await commitTime(github, repo, branch)]));
    }
    const last = Math.max(...times);
    if (now - last < IssueHours * Hour) continue;

    const draft = closing.find(pr => pr.draft) ?? linked.find(pr => pr.draft);
    const branch = branchNames(comments)[0];
    const resume = draft
      ? `Continue draft #${draft.number} (\`${draft.head.ref}\`) rather than starting again.`
      : branch ? `Check \`${branch}\` for earlier work before starting again.` : 'No pushed work was found.';
    const backToQueue = !labels.includes(Blocked) && !labels.includes('type:decision') && !labels.includes('owner-task');
    core.info(`Releasing #${issue.number}: no work for ${age(now, last)}.${dryRun ? ' (dry run)' : ''}`);
    if (dryRun) continue;

    await removeLabel(github, repo, issue.number, InProgress);
    if (backToQueue) await github.rest.issues.addLabels({ ...repo, issue_number: issue.number, labels: [NeedsPr] });
    await github.rest.issues.createComment({
      ...repo, issue_number: issue.number,
      body: `${Marker}\nClaim released: no pushed commit, pull request activity or comment for ${IssueHours} hours${backToQueue ? ', so this issue is back in the queue' : ''}. ${resume}`,
    });
    if (draft) {
      await github.rest.issues.createComment({
        ...repo, issue_number: draft.number,
        body: `${Marker}\nThe claim on #${issue.number} was released after ${IssueHours} hours without activity. Whoever picks up #${issue.number} should continue this branch.`,
      });
    }
  }
}

async function releaseReviewClaims({ github, core, repo, openPrs, now, dryRun }) {
  for (const pr of openPrs.filter(pr => labelNames(pr.labels).includes(Reviewing))) {
    const times = [
      await lastLabeled(github, repo, pr.number, Reviewing),
      newest([await commitTime(github, repo, pr.head.sha)]),
      newest((await workComments(github, repo, pr.number)).map(c => c.created_at)),
    ];
    const last = Math.max(...times);
    if (now - last < ReviewHours * Hour) continue;
    core.info(`Releasing the review claim on #${pr.number}: no work for ${age(now, last)}.${dryRun ? ' (dry run)' : ''}`);
    if (dryRun) continue;
    await removeLabel(github, repo, pr.number, Reviewing);
    await github.rest.issues.createComment({
      ...repo, issue_number: pr.number,
      body: `${Marker}\nReview claim released: no pushed commit or comment for ${ReviewHours} hours. Another reviewer may claim it.`,
    });
  }
}

async function releaseStaleClaims({ github, context, core, now = Date.now(), dryRun = false }) {
  const repo = context.repo;
  const repoName = `${repo.owner}/${repo.repo}`;
  const openPrs = await github.paginate(github.rest.pulls.list, { ...repo, state: 'open', per_page: 100 });
  await releaseIssueClaims({ github, core, repo, repoName, openPrs, now, dryRun });
  await releaseReviewClaims({ github, core, repo, openPrs, now, dryRun });
}

module.exports = releaseStaleClaims;
module.exports.referencedIssueNumbers = referencedIssueNumbers;
module.exports.branchNames = branchNames;
