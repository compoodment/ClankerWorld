// Releases claims that have gone quiet, so status:in-progress and
// status:reviewing always mean someone has pushed work recently.
// Run every 30 minutes by .github/workflows/stale-claims.yml through actions/github-script.
// It reads only GitHub data; it never checks out or runs pull request code.
//
// A claim lasts while its label is on. Only the claimant, this script or an
// owner request ends it; nobody else decides on their own clock that it lapsed.
// Each claim's clock starts when its label was last added, so claiming again
// after a release, a stop or a hand-back starts a fresh claim.
//
// - An issue's status:in-progress claim is released after 1.5 hours without
//   pushed work. Only these count: adding the claim label, opening the
//   claimant's draft pull request, a push to that draft's branch, or a push
//   to a branch named in the issue's comments. A draft that closes the issue
//   is always the claimant's. One that only refers to it (Refs) is the
//   claimant's only if its branch is named in the issue's comments or starts
//   with the issue number, such as codex/123-fix or claude/issue-123-fix;
//   other agents' drafts that mention the issue keep nothing.
//   Comments and edits do not count, because they show no work anyone can
//   check, and pushes to ready pull requests belong to their reviewers.
//   A slashed name in a comment is a branch only if it has a commit. Names
//   that look like files (ending in / or in an extension such as .md) are
//   skipped, then of the newest 20 names that are not a linked pull
//   request's branch, the first 5 real branches are checked. When the claim
//   or the claimant's draft is already recent, no branch is looked up.
//   The issue goes back to status:needs-pr, unless it is blocked or is not
//   pull request work. The release comment names the claimant's draft or
//   the newest real branch to continue from, and any ready pull request that
//   refers to the issue, so the next agent reads it and leaves its branch alone.
//   A claim waiting on the owner (status:needs-decision on the issue or on
//   the claimant's draft) is kept.
// - A pull request's status:reviewing claim is released after 1.5 hours without
//   adding the claim label or a push to the pull request's branch. Comments do
//   not count here either, so a claim cannot hold a place in the review queue.
//   status:needs-decision does not keep it: a reviewer who needs the owner
//   hands the pull request back to draft, which removes status:reviewing.
//   The release comment names the head the claim left.
// Push times come from the repository's activity log, so commits made earlier
// and pushed later count from the push. Comments this script writes carry a
// marker, so the branches they name are never read as someone's work.

// pr-labels.js reads closing keywords and Refs, so both scripts link the same
// pull requests to an issue.
const { closingIssueNumbers, referencedIssueNumbers } = require('./pr-labels.js');

const InProgress = 'status:in-progress';
const Reviewing = 'status:reviewing';
const NeedsPr = 'status:needs-pr';
const NeedsDecision = 'status:needs-decision';
const Blocked = 'status:blocked';
const IssueHours = 1.5;
const ReviewHours = 1.5;
const Hour = 60 * 60 * 1000;
const MaxBranches = 5;
const MaxBranchCandidates = 20; // bounds API calls when comments name many paths
const Marker = '<!-- claim-check -->';
const PushActivities = new Set(['push', 'force_push', 'branch_creation']);

const BranchPatterns = [
  /`([A-Za-z0-9._-]+\/[A-Za-z0-9._\/-]+)`/g,
  /\bbranch\s+([A-Za-z0-9._-]+\/[A-Za-z0-9._\/-]*[A-Za-z0-9_\/-])/gi,
];

function labelNames(labels) {
  return (labels ?? []).map(label => (typeof label === 'string' ? label : label.name));
}

// Names that may be branches, newest first. Some are file paths or other
// slashed words; issueSnapshot keeps only those with a commit or a push.
function branchNames(comments, limit = MaxBranchCandidates) {
  const names = new Set();
  for (const comment of [...comments].reverse()) {
    for (const pattern of BranchPatterns) {
      for (const [, name] of [...(comment.body ?? '').matchAll(pattern)].reverse()) names.add(name.replace(/[.,;:]+$/, ''));
    }
  }
  return [...names].slice(0, limit);
}

// A draft that only refers to the issue (Refs) is the claimant's own work when
// its branch is named in the issue's comments or starts with the issue number,
// such as codex/123-fix, claude/issue-123-fix or 123-fix.
function ownRefsBranch(ref, number, named) {
  return named.has(ref) || new RegExp(`^(?:[\\w.-]+/)?(?:issue-)?${number}(?:-|$)`).test(ref);
}

function newest(times) {
  return times.filter(Boolean).map(time => Date.parse(time)).filter(Number.isFinite).reduce((a, b) => Math.max(a, b), 0);
}

// Comments are read only to find the branches they name; they are not work.
async function commentsFor(github, repo, number) {
  const comments = await github.paginate(github.rest.issues.listComments, { ...repo, issue_number: number, per_page: 100 });
  return comments.filter(comment => !(comment.body ?? '').includes(Marker));
}

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

// When a commit or branch last reached GitHub: the later of its commit date
// and the branch's last push in the activity log. Pass no branch for a fork,
// whose pushes this repository's log does not record.
async function pushActivity(github, repo, ref, branch = ref, needCommit = false) {
  const commit = await commitActivity(github, repo, ref);
  // A name with no commit is not a branch, so skip the activity lookup.
  if (needCommit && !commit.sha) return { ...commit, pushed: 0 };
  let pushes = [];
  if (branch) {
    try {
      const { data } = await github.request('GET /repos/{owner}/{repo}/activity', { ...repo, ref: branch, per_page: 10 });
      pushes = data.filter(activity => PushActivities.has(activity.activity_type)).map(activity => activity.timestamp);
    } catch (error) {
      if (error.status !== 404 && error.status !== 422) throw error;
    }
  }
  return { ...commit, pushed: newest([commit.date, ...pushes]) };
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
  return last === 0 ? 'longer than the records show' : `${((now - last) / Hour).toFixed(1)} hours`;
}

// A pull request's pushed work: opening it and pushes to its branch. Both
// kinds of claim count only this. The signature also catches a new head
// whose push the activity log has not recorded yet.
async function prActivity(github, repo, pr) {
  const ownBranch = !pr.head.repo || pr.head.repo.full_name?.toLowerCase() === `${repo.owner}/${repo.repo}`.toLowerCase();
  const head = await pushActivity(github, repo, pr.head.sha, ownBranch ? pr.head.ref : null);
  return { pushed: Math.max(newest([pr.created_at]), head.pushed), signature: [pr.number, pr.head.sha, head.pushed] };
}

const looksLikeFile = name => name.endsWith('/') || /\.[A-Za-z][A-Za-z0-9]{0,9}$/.test(name);

// `now` is passed only for the first look at an issue: then branch lookups are
// skipped when the claim is clearly fresh, which keeps API calls low.
async function issueSnapshot(github, repo, repoName, number, now = null) {
  const { data: issue } = await github.rest.issues.get({ ...repo, issue_number: number });
  if (issue.state !== 'open' || issue.pull_request) return null;
  const labels = labelNames(issue.labels);
  const open = await github.paginate(github.rest.pulls.list, { ...repo, state: 'open', per_page: 100 });
  const closing = open.filter(pr => closingIssueNumbers(pr.body, repoName).has(number));
  const linked = [...closing, ...open.filter(pr => !closing.includes(pr) && referencedIssueNumbers(pr.body, repoName).has(number))];
  const comments = await commentsFor(github, repo, number);
  const names = branchNames(comments, Infinity);
  const named = new Set(names);
  // Only the claimant's drafts are their work: a ready pull request belongs to
  // its reviewer, a ready one that closes the issue ends the claim anyway, and
  // another agent's draft that only mentions the issue is not this claim's.
  const own = linked.filter(pr => pr.draft && (closing.includes(pr) || ownRefsBranch(pr.head.ref, number, named)));
  const keep = labels.includes(NeedsDecision) || own.some(pr => labelNames(pr.labels).includes(NeedsDecision));
  const claim = await lastLabeled(github, repo, number, InProgress);
  const activities = [];
  for (const pr of own) activities.push(await prActivity(github, repo, pr));
  const prBranches = new Set(linked.map(pr => pr.head.ref));
  const branches = [];
  const recent = time => now !== null && now - time < IssueHours * Hour;
  if (!recent(Math.max(claim, ...activities.map(activity => activity.pushed)))) {
    for (const name of names.filter(name => !prBranches.has(name) && !looksLikeFile(name)).slice(0, MaxBranchCandidates)) {
      if (branches.length >= MaxBranches) break;
      const activity = await pushActivity(github, repo, name, name, true);
      if (activity.sha) branches.push([name, activity]);
      if (recent(activity.pushed)) break;
    }
  }
  return { labels, keep, closing, linked, own,
    namedBranches: branches.map(([name]) => name),
    ready: closing.some(pr => !pr.draft),
    last: Math.max(claim, ...activities.map(activity => activity.pushed), ...branches.map(([, activity]) => activity.pushed)),
    signature: JSON.stringify([claim, activities.map(activity => activity.signature), branches]),
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

// Stop before the token runs low, so a run never starts a release it cannot
// finish. Checking the rate limit itself costs nothing against the limit.
const MinRemaining = 150;
async function enoughBudget(github, core) {
  if (!github.rest.rateLimit?.get) return true;
  const { data } = await github.rest.rateLimit.get();
  const remaining = data.resources?.core?.remaining ?? data.rate?.remaining ?? Infinity;
  if (remaining >= MinRemaining) return true;
  core.warning?.(`Stopping early: only ${remaining} API requests left this hour.`);
  return false;
}

async function releaseIssueClaims({ github, core, repo, repoName, now, dryRun }) {
  const issues = (await github.paginate(github.rest.issues.listForRepo, {
    ...repo, state: 'open', labels: InProgress, per_page: 100,
  })).filter(issue => !issue.pull_request);

  for (const issue of issues) {
    if (!await enoughBudget(github, core)) return;
    const before = await issueSnapshot(github, repo, repoName, issue.number, now);
    if (!before || !before.labels.includes(InProgress) || before.keep || before.ready || now - before.last < IssueHours * Hour) continue;
    const checked = await issueSnapshot(github, repo, repoName, issue.number);
    if (!checked || !checked.labels.includes(InProgress) || checked.keep || checked.ready || changedOrRecent(before, checked, IssueHours, now)) continue;

    const draft = checked.own[0]; // drafts that close the issue come first
    const branch = checked.namedBranches[0];
    // A ready pull request that refers to the issue is with its reviewer: name
    // it so the next agent reads what it leaves and does not push to it.
    const ready = checked.linked.filter(pr => !pr.draft);
    const list = ready.map(pr => `#${pr.number}`).join(', ');
    const underReview = ready.length === 0 ? null : ready.length === 1
      ? `Ready pull request ${list} refers to this issue and is with its reviewer: read what it leaves before starting, and don't push to its branch.`
      : `Ready pull requests ${list} refer to this issue and are with their reviewers: read what they leave before starting, and don't push to their branches.`;
    const resume = [
      draft ? `Continue draft #${draft.number} (\`${draft.head.ref}\`) rather than starting again.`
        : branch ? `Check \`${branch}\` for earlier work before starting again.`
          : underReview ? null : 'No pushed work was found.',
      underReview,
    ].filter(Boolean).join(' ');
    core.info(`Releasing #${issue.number}: nothing pushed for ${age(now, checked.last)}.${dryRun ? ' (dry run)' : ''}`);
    if (dryRun) continue;

    if (!await removeLabel(github, repo, issue.number, InProgress)) continue;
    try {
      let live = await stillReleased(github, repo, repoName, issue.number, checked, now);
      if (!live) continue;
      const backToQueue = canQueue(live);
      if (backToQueue) await github.rest.issues.addLabels({ ...repo, issue_number: issue.number,
        labels: [NeedsPr, ...(live.closing.length > 0 ? ['status:has-pr'] : [])] });
      live = await stillReleased(github, repo, repoName, issue.number, checked, now, true);
      if (!live) continue;
      await github.rest.issues.createComment({
        ...repo, issue_number: issue.number,
        body: `${Marker}\nClaim released: nothing was pushed for ${IssueHours} hours${backToQueue ? ', so this issue is back in the queue' : ''}. ${resume}`,
      });
      if (draft) {
        await github.rest.issues.createComment({
          ...repo, issue_number: draft.number,
          body: `${Marker}\nThe claim on #${issue.number} was released after ${IssueHours} hours without a push. Whoever picks up #${issue.number} should continue this branch.`,
        });
      }
      await stillReleased(github, repo, repoName, issue.number, checked, now, true);
    } catch (error) {
      // Never leave an issue with neither its claim nor a queue label: put the
      // claim back so the next run tries again, then fail this run visibly.
      await github.rest.issues.addLabels({ ...repo, issue_number: issue.number, labels: [InProgress] }).catch(() => {});
      throw error;
    }
  }
}

async function reviewSnapshot(github, repo, number) {
  const { data: pr } = await github.rest.pulls.get({ ...repo, pull_number: number });
  const labels = labelNames(pr.labels);
  const claim = await lastLabeled(github, repo, number, Reviewing);
  const activity = await prActivity(github, repo, pr);
  // status:needs-decision keeps no review claim: a reviewer who needs the
  // owner hands the pull request back to draft instead.
  return { pr, labels, last: Math.max(claim, activity.pushed),
    signature: JSON.stringify([claim, activity.signature]) };
}

async function restoreActiveReview(github, repo, number, before, now) {
  const live = await reviewSnapshot(github, repo, number);
  if (live.pr.state !== 'open' || live.pr.draft) return true;
  if (!live.labels.includes(Reviewing) && !changedOrRecent(before, live, ReviewHours, now)) return false;
  if (!live.labels.includes(Reviewing)) await github.rest.issues.addLabels({ ...repo, issue_number: number, labels: [Reviewing] });
  return true;
}

async function releaseReviewClaims({ github, core, repo, now, dryRun }) {
  const openPrs = await github.paginate(github.rest.pulls.list, { ...repo, state: 'open', per_page: 100 });
  for (const pr of openPrs.filter(pr => labelNames(pr.labels).includes(Reviewing))) {
    if (!await enoughBudget(github, core)) return;
    const before = await reviewSnapshot(github, repo, pr.number);
    if (before.pr.state !== 'open' || before.pr.draft || !before.labels.includes(Reviewing) || now - before.last < ReviewHours * Hour) continue;
    const checked = await reviewSnapshot(github, repo, pr.number);
    if (checked.pr.state !== 'open' || checked.pr.draft || !checked.labels.includes(Reviewing) || changedOrRecent(before, checked, ReviewHours, now)) continue;
    core.info(`Releasing the review claim on #${pr.number}: nothing pushed for ${age(now, checked.last)}.${dryRun ? ' (dry run)' : ''}`);
    if (dryRun) continue;
    if (!await removeLabel(github, repo, pr.number, Reviewing)) continue;
    if (await restoreActiveReview(github, repo, pr.number, checked, now)) continue;
    await github.rest.issues.createComment({
      ...repo, issue_number: pr.number,
      body: `${Marker}\nReview claim released: nothing was pushed for ${ReviewHours} hours (head ${checked.pr.head.sha.slice(0, 8)}). Another reviewer may claim it when they start reviewing.`,
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
