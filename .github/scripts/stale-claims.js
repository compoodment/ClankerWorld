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
  for (const comment of [...comments].reverse()) {
    for (const pattern of BranchPatterns) {
      for (const [, name] of [...(comment.body ?? '').matchAll(pattern)].reverse()) names.add(name.replace(/[.,;:]+$/, ''));
    }
  }
  return [...names].slice(0, 5);
}

function newest(times) {
  return times.filter(Boolean).map(time => Date.parse(time)).filter(Number.isFinite).reduce((a, b) => Math.max(a, b), 0);
}

async function commentsFor(github, repo, number) {
  const comments = await github.paginate(github.rest.issues.listComments, { ...repo, issue_number: number, per_page: 100 });
  const work = comments.filter(comment => !(comment.body ?? '').includes(Marker));
  return { work, automated: comments.filter(comment => (comment.body ?? '').includes(Marker)) };
}

const commentTimes = comments => comments.flatMap(comment => [comment.created_at, comment.updated_at]);
const commentSignature = comments => comments.map(comment => [comment.id, comment.created_at, comment.updated_at]);

async function lastLabeled(github, repo, number, name) {
  const events = await github.paginate(github.rest.issues.listEvents, { ...repo, issue_number: number, per_page: 100 });
  return newest(events.filter(event => event.event === 'labeled' && event.label?.name === name).map(event => event.created_at));
}

async function commitActivity(github, repo, ref) {
  try {
    const { data } = await github.rest.repos.getCommit({ ...repo, ref });
    return { sha: data.sha ?? ref, date: data.commit?.committer?.date ?? null };
  } catch (error) {
    if (error.status === 404 || error.status === 422) return { sha: null, date: null };
    throw error;
  }
}

async function removeLabel(github, repo, number, name) {
  try {
    await github.rest.issues.removeLabel({ ...repo, issue_number: number, name });
    return true;
  } catch (error) {
    if (error.status !== 404) throw error;
    return false;
  }
}

function age(now, last) {
  return last === 0 ? 'any recorded activity' : `${Math.floor((now - last) / Hour)} hours`;
}

async function prActivity(github, repo, pr) {
  const comments = await commentsFor(github, repo, pr.number);
  const commit = await commitActivity(github, repo, pr.head.sha);
  // The PR's update time also captures a push of commits with older commit
  // dates. Our own release comments update that time too, so exclude updates
  // explained by those comments. Neither they nor the proxy enter the work
  // signature: this cleanup must not undo itself because it posted a note.
  const automaticTime = newest(commentTimes(comments.automated));
  const updateTime = newest([pr.updated_at]);
  const last = Math.max(newest([pr.created_at, commit.date, ...commentTimes(comments.work)]),
    updateTime > automaticTime ? updateTime : 0);
  return { last, signature: [pr.number, pr.head.sha, commentSignature(comments.work)] };
}

async function issueSnapshot(github, repo, repoName, number) {
  const { data: issue } = await github.rest.issues.get({ ...repo, issue_number: number });
  if (issue.state !== 'open' || issue.pull_request) return null;
  const labels = labelNames(issue.labels);
  const open = await github.paginate(github.rest.pulls.list, { ...repo, state: 'open', per_page: 100 });
  const closing = open.filter(pr => closingIssueNumbers(pr.body, repoName).has(number));
  const linked = [...closing, ...open.filter(pr => !closing.includes(pr) && referencedIssueNumbers(pr.body, repoName).has(number))];
  const keep = labels.includes(NeedsDecision) || linked.some(pr => labelNames(pr.labels).includes(NeedsDecision));
  const comments = (await commentsFor(github, repo, number)).work;
  const claim = await lastLabeled(github, repo, number, InProgress);
  const activities = [];
  for (const pr of linked) activities.push(await prActivity(github, repo, pr));
  const prBranches = new Set(linked.map(pr => pr.head.ref));
  const branches = [];
  for (const name of branchNames(comments).filter(name => !prBranches.has(name))) {
    branches.push([name, await commitActivity(github, repo, name)]);
  }
  return { labels, keep, closing, linked, comments,
    ready: closing.some(pr => !pr.draft),
    last: Math.max(claim, newest(commentTimes(comments)), ...activities.map(activity => activity.last),
      newest(branches.map(([, activity]) => activity.date))),
    signature: JSON.stringify([claim, commentSignature(comments), activities.map(activity => activity.signature), branches]),
  };
}

function changedOrRecent(before, after, hours, now) {
  return before.signature !== after.signature || now - after.last < hours * Hour;
}

function canQueue(snapshot) {
  return !snapshot.labels.includes(Blocked) && !snapshot.labels.includes('type:decision') &&
    !snapshot.labels.includes('owner-task') && !snapshot.keep && !snapshot.ready;
}

// Reconcile after each write too: another agent can reclaim, publish a ready
// PR, or begin an owner wait while a GitHub label request is in flight.
async function stillReleased(github, repo, repoName, number, before, now, ensureQueue = false) {
  const live = await issueSnapshot(github, repo, repoName, number);
  if (!live) { await removeLabel(github, repo, number, NeedsPr); return null; }
  if (live.ready) {
    await github.rest.issues.addLabels({ ...repo, issue_number: number, labels: ['status:has-pr'] });
    await removeLabel(github, repo, number, NeedsPr);
    return null;
  }
  if (live.keep || changedOrRecent(before, live, IssueHours, now) || live.labels.includes(InProgress)) {
    if (!live.labels.includes(InProgress)) await github.rest.issues.addLabels({ ...repo, issue_number: number, labels: [InProgress] });
    await removeLabel(github, repo, number, NeedsPr);
    return null;
  }
  if (!canQueue(live)) await removeLabel(github, repo, number, NeedsPr);
  else if (ensureQueue && !live.labels.includes(NeedsPr)) {
    const labels = [NeedsPr, ...(live.closing.length > 0 ? ['status:has-pr'] : [])];
    await github.rest.issues.addLabels({ ...repo, issue_number: number, labels });
    return stillReleased(github, repo, repoName, number, before, now);
  }
  return live;
}

async function releaseIssueClaims({ github, core, repo, repoName, now, dryRun }) {
  const issues = (await github.paginate(github.rest.issues.listForRepo, {
    ...repo, state: 'open', labels: InProgress, per_page: 100,
  })).filter(issue => !issue.pull_request);

  for (const issue of issues) {
    const before = await issueSnapshot(github, repo, repoName, issue.number);
    if (!before || !before.labels.includes(InProgress) || before.keep || before.ready || now - before.last < IssueHours * Hour) continue;
    const checked = await issueSnapshot(github, repo, repoName, issue.number);
    if (!checked || !checked.labels.includes(InProgress) || checked.keep || checked.ready || changedOrRecent(before, checked, IssueHours, now)) continue;

    const draft = checked.closing.find(pr => pr.draft) ?? checked.linked.find(pr => pr.draft);
    const branch = branchNames(checked.comments)[0];
    const resume = draft
      ? `Continue draft #${draft.number} (\`${draft.head.ref}\`) rather than starting again.`
      : branch ? `Check \`${branch}\` for earlier work before starting again.` : 'No pushed work was found.';
    core.info(`Releasing #${issue.number}: no work for ${age(now, checked.last)}.${dryRun ? ' (dry run)' : ''}`);
    if (dryRun) continue;

    if (!await removeLabel(github, repo, issue.number, InProgress)) continue;
    let live = await stillReleased(github, repo, repoName, issue.number, checked, now);
    if (!live) continue;
    const backToQueue = canQueue(live);
    if (backToQueue) await github.rest.issues.addLabels({ ...repo, issue_number: issue.number,
      labels: [NeedsPr, ...(live.closing.length > 0 ? ['status:has-pr'] : [])] });
    live = await stillReleased(github, repo, repoName, issue.number, checked, now, true);
    if (!live) continue;
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
    await stillReleased(github, repo, repoName, issue.number, checked, now, true);
  }
}

async function reviewSnapshot(github, repo, number) {
  const { data: pr } = await github.rest.pulls.get({ ...repo, pull_number: number });
  const labels = labelNames(pr.labels);
  const claim = await lastLabeled(github, repo, number, Reviewing);
  const activity = await prActivity(github, repo, pr);
  return { pr, labels, keep: labels.includes(NeedsDecision), last: Math.max(claim, activity.last),
    signature: JSON.stringify([claim, activity.signature]) };
}

async function restoreActiveReview(github, repo, number, before, now) {
  const live = await reviewSnapshot(github, repo, number);
  if (live.pr.state !== 'open' || live.pr.draft) return true;
  if (!live.keep && !live.labels.includes(Reviewing) && !changedOrRecent(before, live, ReviewHours, now)) return false;
  if (!live.labels.includes(Reviewing)) await github.rest.issues.addLabels({ ...repo, issue_number: number, labels: [Reviewing] });
  return true;
}

async function releaseReviewClaims({ github, core, repo, now, dryRun }) {
  const openPrs = await github.paginate(github.rest.pulls.list, { ...repo, state: 'open', per_page: 100 });
  for (const pr of openPrs.filter(pr => labelNames(pr.labels).includes(Reviewing))) {
    const before = await reviewSnapshot(github, repo, pr.number);
    if (before.pr.state !== 'open' || before.pr.draft || !before.labels.includes(Reviewing) || before.keep || now - before.last < ReviewHours * Hour) continue;
    const checked = await reviewSnapshot(github, repo, pr.number);
    if (checked.pr.state !== 'open' || checked.pr.draft || !checked.labels.includes(Reviewing) || checked.keep || changedOrRecent(before, checked, ReviewHours, now)) continue;
    core.info(`Releasing the review claim on #${pr.number}: no work for ${age(now, checked.last)}.${dryRun ? ' (dry run)' : ''}`);
    if (dryRun) continue;
    if (!await removeLabel(github, repo, pr.number, Reviewing)) continue;
    if (await restoreActiveReview(github, repo, pr.number, checked, now)) continue;
    await github.rest.issues.createComment({
      ...repo, issue_number: pr.number,
      body: `${Marker}\nReview claim released: no pushed commit or comment for ${ReviewHours} hours. Another reviewer may claim it.`,
    });
    await restoreActiveReview(github, repo, pr.number, checked, now);
  }
}

async function releaseStaleClaims({ github, context, core, now = Date.now(), dryRun = false }) {
  const repo = context.repo;
  const repoName = `${repo.owner}/${repo.repo}`;
  await releaseIssueClaims({ github, core, repo, repoName, now, dryRun });
  await releaseReviewClaims({ github, core, repo, now, dryRun });
}

module.exports = releaseStaleClaims;
module.exports.referencedIssueNumbers = referencedIssueNumbers;
module.exports.branchNames = branchNames;
