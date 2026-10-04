const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn, spawnSync } = require('node:child_process');
const { test } = require('node:test');

// The shared tool cache (scripts/tool-cache.sh) and the cleanup (scripts/clean-workspace.sh),
// run against throwaway files, repositories and caches.
const scripts = path.join(__dirname, '..', '..', 'scripts');
const pinned = fs.readFileSync(path.join(scripts, 'verify-godot-client.sh'), 'utf8').match(/\b[0-9a-f]{64}\b/)[0];

function sandbox(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'workspace-test-'));
  // Unpacked tool folders are read-only, so make everything writable before removing it.
  t.after(() => {
    spawnSync('chmod', ['-R', 'u+w', root]);
    fs.rmSync(root, { recursive: true, force: true });
  });
  const env = {
    ...process.env,
    XDG_CACHE_HOME: path.join(root, 'xdg-cache'),
    XDG_STATE_HOME: path.join(root, 'xdg-state'),
    TMPDIR: path.join(root, 'tmp'),
    CLANKERWORLD_TOOL_CACHE: path.join(root, 'tools'),
    GIT_AUTHOR_NAME: 'Test', GIT_AUTHOR_EMAIL: 'test@example.com',
    GIT_COMMITTER_NAME: 'Test', GIT_COMMITTER_EMAIL: 'test@example.com',
    GIT_CONFIG_GLOBAL: '/dev/null', GIT_CONFIG_NOSYSTEM: '1',
  };
  fs.mkdirSync(env.TMPDIR);
  return { root, env };
}

function run(command, args, options) {
  const result = spawnSync(command, args, { encoding: 'utf8', ...options });
  assert.ifError(result.error);
  return result;
}

function ok(command, args, options) {
  const result = run(command, args, options);
  assert.equal(result.status, 0, `${command} ${args.join(' ')}\n${result.stdout}${result.stderr}`);
  return result.stdout;
}

function cache(env, call) {
  return run('bash', ['-c', `source "${scripts}/tool-cache.sh"; ${call}`], { env });
}

function sha256(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}

function makeZip(file, entries) {
  ok('python3', ['-c', `
import sys, zipfile
with zipfile.ZipFile(sys.argv[1], 'w') as archive:
    for name, text in zip(sys.argv[2::2], sys.argv[3::2]):
        archive.writestr(name, text)`, file, ...entries.flat()]);
}

test('a verified download is cached by its SHA-256 and checked again on reuse', t => {
  const { root, env } = sandbox(t);
  const source = path.join(root, 'tool.zip');
  fs.writeFileSync(source, 'tool bytes');
  const sum = sha256(source);
  const url = `file://${source}`;

  const first = cache(env, `tool_cache_download "${url}" ${sum} tool.zip`);
  assert.equal(first.status, 0, first.stderr);
  const cached = first.stdout.trim();
  assert.equal(cached, path.join(env.CLANKERWORLD_TOOL_CACHE, 'downloads', sum, 'tool.zip'));
  assert.equal(fs.readFileSync(cached, 'utf8'), 'tool bytes');

  fs.rmSync(source);
  const reused = cache(env, `tool_cache_download "${url}" ${sum} tool.zip`);
  assert.equal(reused.status, 0, reused.stderr);
  assert.doesNotMatch(reused.stderr, /Downloading/);

  // A damaged cached copy is noticed and fetched again.
  fs.writeFileSync(source, 'tool bytes');
  fs.writeFileSync(cached, 'damaged');
  const repaired = cache(env, `tool_cache_download "${url}" ${sum} tool.zip`);
  assert.equal(repaired.status, 0, repaired.stderr);
  assert.match(repaired.stderr, /failed its SHA-256 check/);
  assert.equal(fs.readFileSync(cached, 'utf8'), 'tool bytes');

  const wrong = cache(env, `tool_cache_download "${url}" ${'0'.repeat(64)} tool.zip`);
  assert.notEqual(wrong.status, 0);
  assert.match(wrong.stderr, /SHA-256 mismatch/);
  assert.deepEqual(fs.readdirSync(path.join(env.CLANKERWORLD_TOOL_CACHE, 'downloads', '0'.repeat(64))), []);
});

test('an archive is unpacked once, read-only, even when several runs ask at the same time', async t => {
  const { root, env } = sandbox(t);
  const archive = path.join(root, 'tool.zip');
  makeZip(archive, [['bin/tool', 'run me'], ['README', 'hello']]);
  const sum = sha256(archive);

  const runs = Array.from({ length: 4 }, () => new Promise(resolve => {
    const child = spawn('bash', ['-c', `source "${scripts}/tool-cache.sh"; tool_cache_unzip "${archive}" ${sum}`], { env });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', data => { stdout += data; });
    child.stderr.on('data', data => { stderr += data; });
    child.on('close', status => resolve({ status, stdout, stderr }));
  }));
  const results = await Promise.all(runs);
  for (const result of results) assert.equal(result.status, 0, result.stderr);
  const folder = path.join(env.CLANKERWORLD_TOOL_CACHE, 'unpacked', sum);
  assert.deepEqual(new Set(results.map(result => result.stdout.trim())), new Set([folder]));
  assert.equal(results.filter(result => /Unpacking/.test(result.stderr)).length, 1);
  assert.equal(fs.readFileSync(path.join(folder, 'bin', 'tool'), 'utf8'), 'run me');
  assert.equal(fs.statSync(path.join(folder, 'README')).mode & 0o222, 0);
  assert.deepEqual(fs.readdirSync(path.dirname(folder)), [sum]);
});

function git(cwd, env, ...args) {
  return ok('git', args, { cwd, env }).trim();
}

// Sets every file under a folder, and its worktree's index and HEAD, to two days ago.
function ageFiles(folder, days = 2) {
  if (fs.lstatSync(folder).isDirectory()) {
    for (const name of fs.readdirSync(folder)) ageFiles(path.join(folder, name), days);
  }
  const when = new Date(Date.now() - days * 24 * 60 * 60 * 1000);
  fs.lutimesSync(folder, when, when);
}

function age(folder, env) {
  ageFiles(folder);
  const gitdir = git(folder, env, 'rev-parse', '--absolute-git-dir');
  for (const name of ['HEAD', 'index']) ageFiles(path.join(gitdir, name));
}

function cleanupRepo(root, env) {
  const remote = path.join(root, 'remote.git');
  const main = path.join(root, 'main');
  git(root, env, 'init', '-q', '--bare', remote);
  git(root, env, 'init', '-q', '-b', 'main', main);
  fs.writeFileSync(path.join(main, '.gitignore'), 'bin/\nobj/\nexport/\nsaves/\n.evidence/\n');
  fs.mkdirSync(path.join(main, 'scripts'));
  for (const name of ['clean-workspace.sh', 'tool-cache.sh', 'verify-godot-client.sh', 'verify-godot-windows-export.sh']) {
    fs.copyFileSync(path.join(scripts, name), path.join(main, 'scripts', name));
  }
  git(main, env, 'add', '.');
  git(main, env, 'commit', '-q', '-m', 'start');
  git(main, env, 'remote', 'add', 'origin', remote);
  git(main, env, 'push', '-q', '-u', 'origin', 'main');
  return { main, script: path.join(main, 'scripts', 'clean-workspace.sh') };
}

function worktree(main, env, name, { push = true, deleteRemote = true } = {}) {
  const folder = path.join(path.dirname(main), name);
  git(main, env, 'worktree', 'add', '-q', '-b', name, folder);
  fs.writeFileSync(path.join(folder, `${name}.txt`), name);
  git(folder, env, 'add', '.');
  git(folder, env, 'commit', '-q', '-m', name);
  if (push) git(folder, env, 'push', '-q', '-u', 'origin', name);
  if (push && deleteRemote) git(main, env, 'push', '-q', 'origin', '--delete', name);
  return folder;
}

test('cleanup keeps the script checkout and includes locked cache removals in its totals', t => {
  const { root, env } = sandbox(t);
  const { main } = cleanupRepo(root, env);
  const sibling = path.join(root, 'script-owner');
  git(main, env, 'worktree', 'add', '-q', '-b', 'script-owner', sibling, 'main');
  age(sibling, env);
  const script = path.join(sibling, 'scripts', 'clean-workspace.sh');
  const kept = path.join(env.CLANKERWORLD_TOOL_CACHE, 'downloads', pinned);
  const obsolete = path.join(env.CLANKERWORLD_TOOL_CACHE, 'downloads', 'e'.repeat(64));
  for (const folder of [kept, obsolete]) {
    fs.mkdirSync(folder, { recursive: true });
    fs.writeFileSync(path.join(folder, 'tool.zip'), 'tool');
    ageFiles(folder, 40);
  }
  const listing = ok('bash', [script, '--offline'], { cwd: main, env });
  assert.match(listing, /Would remove 1 item\(s\)/);
  assert.match(listing, /Run again with --apply/);
  const applied = ok('bash', [script, '--offline', '--apply'], { cwd: main, env });
  assert.ok(fs.existsSync(script));
  assert.ok(fs.existsSync(kept));
  assert.ok(!fs.existsSync(obsolete));
  assert.match(applied, /Removed 1 item\(s\)/);
});

test('cleanup lists first, then removes only finished, idle and rebuildable files', t => {
  const { root, env } = sandbox(t);
  const { main, script } = cleanupRepo(root, env);

  const finished = worktree(main, env, 'finished');
  fs.mkdirSync(path.join(finished, 'bin'));
  fs.writeFileSync(path.join(finished, 'bin', 'out.dll'), 'x');
  fs.mkdirSync(path.join(finished, '.evidence', 'keep'), { recursive: true });
  fs.writeFileSync(path.join(finished, '.evidence', 'keep', 'failure.trx'), 'kept');
  fs.writeFileSync(path.join(finished, '.evidence', 'coverage.xml'), 'bulk');
  const open = worktree(main, env, 'open', { deleteRemote: false });
  fs.mkdirSync(path.join(open, 'obj'));
  fs.writeFileSync(path.join(open, 'obj', 'cache'), 'x');
  const dirty = worktree(main, env, 'dirty');
  fs.writeFileSync(path.join(dirty, 'dirty.txt'), 'changed');
  const local = worktree(main, env, 'local');
  fs.mkdirSync(path.join(local, 'saves'));
  fs.writeFileSync(path.join(local, 'saves', 'world.json'), '{}');
  const unpushed = worktree(main, env, 'unpushed', { push: false });
  const locked = worktree(main, env, 'locked');
  git(main, env, 'worktree', 'lock', locked);
  const active = worktree(main, env, 'active');
  for (const folder of [finished, open, dirty, local, unpushed, locked]) age(folder, env);

  const tools = env.CLANKERWORLD_TOOL_CACHE;
  const unpinned = path.join(tools, 'downloads', 'f'.repeat(64));
  const kept = path.join(tools, 'downloads', pinned);
  for (const folder of [unpinned, kept]) {
    fs.mkdirSync(folder, { recursive: true });
    fs.writeFileSync(path.join(folder, 'tool.zip'), 'x');
  }
  const legacy = path.join(env.XDG_CACHE_HOME, 'clankerworld-godot');
  fs.mkdirSync(legacy, { recursive: true });
  fs.writeFileSync(path.join(legacy, 'old.zip'), 'x');
  const oldTemp = path.join(env.TMPDIR, 'clankerworld-godot-client.oldrun');
  const newTemp = path.join(env.TMPDIR, 'clankerworld-godot-client.newrun');
  fs.mkdirSync(oldTemp);
  fs.mkdirSync(newTemp);
  fs.writeFileSync(path.join(oldTemp, '.clankerworld-run.pid'), '99999999');
  fs.writeFileSync(path.join(newTemp, '.clankerworld-run.pid'), String(process.pid));
  const oldKept = path.join(env.XDG_STATE_HOME, 'clankerworld', 'kept-evidence', 'gone-20200101000000');
  fs.mkdirSync(oldKept, { recursive: true });
  for (const folder of [unpinned, kept, legacy, oldTemp, oldKept]) {
    ageFiles(folder, 40);
  }

  const listing = ok('bash', [script], { cwd: main, env });
  assert.match(listing, /finished worktree: its branch on origin was deleted; branch finished is kept/);
  assert.match(listing, /dirty: kept, has uncommitted or untracked files/);
  assert.match(listing, /local: kept, has local files \(saves\/\)/);
  assert.match(listing, /unpushed: kept, work not finished/);
  assert.match(listing, /locked: kept, locked/);
  assert.match(listing, /active: kept, changed in the last 12 hours/);
  assert.match(listing, /Run again with --apply/);
  assert.ok(fs.existsSync(finished) && fs.existsSync(unpinned) && fs.existsSync(oldTemp));

  ok('bash', [script, '--apply'], { cwd: main, env });
  assert.ok(!fs.existsSync(finished));
  assert.match(git(main, env, 'branch', '--list', 'finished'), /finished/);
  assert.doesNotMatch(git(main, env, 'worktree', 'list'), /finished/);
  const archive = path.join(env.XDG_STATE_HOME, 'clankerworld', 'kept-evidence');
  const archived = fs.readdirSync(archive);
  assert.equal(archived.length, 2);
  const retained = archived.find(name => /^finished-\d{14}$/.test(name));
  assert.ok(retained);
  assert.equal(fs.readFileSync(path.join(archive, retained, 'failure.trx'), 'utf8'), 'kept');

  assert.ok(!fs.existsSync(path.join(open, 'obj')));
  assert.ok(fs.existsSync(path.join(open, 'open.txt')));
  for (const folder of [dirty, local, unpushed, locked, active]) assert.ok(fs.existsSync(folder), folder);
  assert.ok(fs.existsSync(path.join(local, 'saves', 'world.json')));
  assert.ok(!fs.existsSync(unpinned));
  assert.ok(fs.existsSync(kept));
  assert.ok(fs.existsSync(path.join(legacy, 'old.zip')));
  assert.ok(!fs.existsSync(oldTemp));
  assert.ok(fs.existsSync(newTemp));
  assert.ok(fs.existsSync(oldKept));
  ok('bash', [script, '--apply', '--prune-kept-evidence'], { cwd: main, env });
  assert.ok(!fs.existsSync(oldKept));
});

test('cleanup refuses a different repository and keeps unavailable worktree records', t => {
  const { root, env } = sandbox(t);
  const { main, script } = cleanupRepo(root, env);
  const other = path.join(root, 'other');
  fs.mkdirSync(other);
  const foreign = cleanupRepo(other, env);
  const finished = worktree(foreign.main, env, 'foreign-finished');
  fs.mkdirSync(path.join(foreign.main, 'export'));
  fs.writeFileSync(path.join(foreign.main, 'export', 'drawing.txt'), 'hand-made');
  age(finished, env);
  age(foreign.main, env);
  const rejected = run('bash', [script, '--apply'], { cwd: foreign.main, env });
  assert.notEqual(rejected.status, 0);
  assert.match(rejected.stderr, /another repository/);
  assert.ok(fs.existsSync(finished));
  assert.equal(fs.readFileSync(path.join(foreign.main, 'export', 'drawing.txt'), 'utf8'), 'hand-made');

  const missing = worktree(main, env, 'missing');
  const gitdir = git(missing, env, 'rev-parse', '--absolute-git-dir');
  fs.renameSync(missing, `${missing}-unmounted`);
  ok('bash', [script, '--apply'], { cwd: main, env });
  assert.ok(git(main, env, 'worktree', 'list', '--porcelain').includes(`worktree ${missing}\n`));
  assert.ok(fs.existsSync(path.join(gitdir, 'index')));
  assert.ok(fs.existsSync(path.join(gitdir, 'logs', 'HEAD')));
});

test('cleanup preserves hidden edits, unfinished operations and reflog-only commits', t => {
  const { root, env } = sandbox(t);
  const { main, script } = cleanupRepo(root, env);
  const held = [];
  for (const flag of ['--assume-unchanged', '--skip-worktree']) {
    const name = flag.slice(2);
    const folder = worktree(main, env, name);
    git(folder, env, 'update-index', flag, `${name}.txt`);
    fs.writeFileSync(path.join(folder, `${name}.txt`), 'local data');
    held.push(folder);
  }
  for (const marker of ['MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'BISECT_LOG', 'rebase-merge']) {
    const folder = worktree(main, env, marker.toLowerCase());
    const gitdir = git(folder, env, 'rev-parse', '--absolute-git-dir');
    fs.writeFileSync(path.join(gitdir, marker), `${git(folder, env, 'rev-parse', 'HEAD')}\n`);
    held.push(folder);
  }
  const reflog = worktree(main, env, 'reflog');
  git(reflog, env, 'checkout', '--detach');
  fs.writeFileSync(path.join(reflog, 'only-in-reflog.txt'), 'keep this commit');
  git(reflog, env, 'add', '.');
  git(reflog, env, 'commit', '-q', '-m', 'only in HEAD reflog');
  const saved = git(reflog, env, 'rev-parse', 'HEAD');
  git(reflog, env, 'checkout', 'reflog');
  held.push(reflog);
  for (const folder of held) age(folder, env);
  ok('bash', [script, '--apply'], { cwd: main, env });
  for (const folder of held) assert.ok(fs.existsSync(folder), folder);
  assert.ok(git(reflog, env, 'reflog', '--format=%H', 'HEAD').includes(saved));
  for (const flag of ['assume-unchanged', 'skip-worktree']) {
    assert.equal(fs.readFileSync(path.join(root, flag, `${flag}.txt`), 'utf8'), 'local data');
  }
});

test('cleanup handles a newline in a finished worktree path without touching the current one', t => {
  const { root, env } = sandbox(t);
  const { main, script } = cleanupRepo(root, env);
  const initial = worktree(main, env, 'line-path');
  const finished = path.join(root, 'finished\nworktree');
  git(main, env, 'worktree', 'move', initial, finished);
  age(finished, env);
  ok('bash', [script, '--apply'], { cwd: main, env });
  assert.ok(!fs.existsSync(finished));
  assert.ok(fs.existsSync(main));
  assert.match(git(main, env, 'branch', '--list', 'line-path'), /line-path/);
});

test('a missing fetched tracking ref does not prove that a remote branch was deleted', t => {
  const { root, env } = sandbox(t);
  const { main, script } = cleanupRepo(root, env);
  const live = worktree(main, env, 'live', { deleteRemote: false });
  git(main, env, 'config', '--replace-all', 'remote.origin.fetch', '+refs/heads/main:refs/remotes/origin/main');
  git(main, env, 'update-ref', '-d', 'refs/remotes/origin/live');
  const local = worktree(main, env, 'local-upstream');
  git(local, env, 'config', 'branch.local-upstream.remote', '.');
  git(local, env, 'config', 'branch.local-upstream.merge', 'refs/heads/main');
  age(live, env);
  age(local, env);
  ok('bash', [script, '--apply'], { cwd: main, env });
  assert.ok(fs.existsSync(live));
  assert.ok(fs.existsSync(local));
});

test('cleanup rejects zero retention and ignores unrelated files in overridable folders', t => {
  const { root, env } = sandbox(t);
  const { main, script } = cleanupRepo(root, env);
  for (const option of ['--idle-hours', '--keep-days']) {
    const result = run('bash', [script, '--apply', option, '0'], { cwd: main, env });
    assert.notEqual(result.status, 0);
  }
  const downloads = path.join(env.CLANKERWORLD_TOOL_CACHE, 'downloads');
  const unpacked = path.join(env.CLANKERWORLD_TOOL_CACHE, 'unpacked');
  const retained = path.join(root, 'shared-documents');
  fs.mkdirSync(downloads, { recursive: true });
  fs.mkdirSync(unpacked, { recursive: true });
  fs.mkdirSync(retained);
  fs.writeFileSync(path.join(downloads, 'photo.jpg'), 'photo');
  fs.mkdirSync(path.join(unpacked, 'tax-return-2025'));
  fs.writeFileSync(path.join(retained, 'important.txt'), 'keep');
  for (const folder of [downloads, unpacked, retained]) {
    ageFiles(folder, 40);
  }
  ok('bash', [script, '--apply', '--prune-kept-evidence'], {
    cwd: main, env: { ...env, CLANKERWORLD_KEPT_EVIDENCE: retained },
  });
  assert.equal(fs.readFileSync(path.join(downloads, 'photo.jpg'), 'utf8'), 'photo');
  assert.ok(fs.existsSync(path.join(unpacked, 'tax-return-2025')));
  assert.equal(fs.readFileSync(path.join(retained, 'important.txt'), 'utf8'), 'keep');
});

test('cleanup removes old partial downloads but preserves measurements and live scratch folders', t => {
  const { root, env } = sandbox(t);
  const { main, script } = cleanupRepo(root, env);
  const folder = path.join(env.CLANKERWORLD_TOOL_CACHE, 'downloads', pinned);
  fs.mkdirSync(folder, { recursive: true });
  const partial = path.join(folder, 'tool.zip.partial.abcdef');
  fs.writeFileSync(partial, 'unfinished');
  fs.writeFileSync(path.join(folder, 'tool.zip'), 'archive');
  const measurements = path.join(env.TMPDIR, 'clankerworld-map-measurements');
  const active = path.join(env.TMPDIR, 'clankerworld-godot-client.active');
  for (const name of [measurements, active]) fs.mkdirSync(name);
  fs.writeFileSync(path.join(measurements, 'evidence.txt'), 'before and after');
  fs.writeFileSync(path.join(active, '.clankerworld-run.pid'), String(process.pid));
  for (const name of [folder, measurements, active]) {
    ageFiles(name);
  }
  ok('bash', [script, '--apply'], { cwd: main, env });
  assert.ok(!fs.existsSync(partial));
  assert.equal(fs.readFileSync(path.join(folder, 'tool.zip'), 'utf8'), 'archive');
  assert.ok(fs.existsSync(active));
  assert.equal(fs.readFileSync(path.join(measurements, 'evidence.txt'), 'utf8'), 'before and after');
});

test('cache pruning waits for a cache user and rechecks its refreshed age under the lock', async t => {
  const { root, env } = sandbox(t);
  const { main, script } = cleanupRepo(root, env);
  const sum = 'f'.repeat(64);
  const entry = path.join(env.CLANKERWORLD_TOOL_CACHE, 'downloads', sum);
  const locks = path.join(env.CLANKERWORLD_TOOL_CACHE, 'locks');
  fs.mkdirSync(entry, { recursive: true });
  fs.mkdirSync(locks);
  fs.writeFileSync(path.join(entry, 'archive.zip'), 'verified bytes');
  ageFiles(entry, 40);
  const ready = path.join(root, 'ready');
  const release = path.join(root, 'release');
  const holder = spawn('bash', ['-c',
    'source "$1"; tool_cache_locked "$2" bash -c \'touch "$2"; while [[ ! -e "$3" ]]; do sleep 0.05; done; touch -c "$1"\' bash "$3" "$4" "$5"',
    'bash', path.join(scripts, 'tool-cache.sh'), sum, entry, ready, release], { env });
  const holderDone = new Promise(resolve => holder.on('close', resolve));
  t.after(() => { if (fs.existsSync(root) && !fs.existsSync(release)) fs.writeFileSync(release, 'go'); });
  for (let attempt = 0; attempt < 100 && !fs.existsSync(ready); attempt++) {
    await new Promise(resolve => setTimeout(resolve, 10));
  }
  assert.ok(fs.existsSync(ready), 'cache user acquired the lock');
  const cleaner = spawn('bash', [script, '--apply'], { cwd: main, env });
  let stderr = '';
  cleaner.stderr.on('data', data => { stderr += data; });
  const cleanerDone = new Promise(resolve => cleaner.on('close', resolve));
  await new Promise(resolve => setTimeout(resolve, 150));
  fs.writeFileSync(release, 'go');
  assert.equal(await holderDone, 0);
  assert.equal(await cleanerDone, 0, stderr);
  assert.equal(fs.readFileSync(path.join(entry, 'archive.zip'), 'utf8'), 'verified bytes');
  assert.ok(fs.statSync(entry).isDirectory());
});

test('the mkdir lock fallback shares concurrent work and fails closed on stale ownership', async t => {
  const { root, env } = sandbox(t);
  const source = path.join(root, 'tool.zip');
  makeZip(source, [['tool', 'bytes']]);
  const sum = sha256(source);
  const prefix = 'command() { if [[ "$1" == -v && "$2" == flock ]]; then return 1; fi; builtin command "$@"; };';
  const calls = Array.from({ length: 3 }, () => new Promise(resolve => {
    const child = spawn('bash', ['-c', `${prefix} source "$1"; tool_cache_unzip "$2" "$3"`,
      'bash', path.join(scripts, 'tool-cache.sh'), source, sum], { env });
    let stderr = '';
    child.stderr.on('data', data => { stderr += data; });
    child.on('close', status => resolve({ status, stderr }));
  }));
  for (const result of await Promise.all(calls)) assert.equal(result.status, 0, result.stderr);
  const stale = path.join(env.CLANKERWORLD_TOOL_CACHE, 'locks', `${sum}.d`);
  fs.mkdirSync(stale);
  fs.writeFileSync(path.join(stale, 'pid'), '99999999');
  const refused = run('bash', ['-c', `${prefix} source "$1"; tool_cache_unzip "$2" "$3"`,
    'bash', path.join(scripts, 'tool-cache.sh'), source, sum], { env });
  assert.notEqual(refused.status, 0);
  assert.match(refused.stderr, /Stale tool cache lock/);
  assert.ok(fs.existsSync(stale));
});
