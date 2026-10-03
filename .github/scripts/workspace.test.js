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
    HOME: root,
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
function age(folder, env) {
  ok('find', [folder, '-exec', 'touch', '-h', '-d', '2 days ago', '{}', '+']);
  const gitdir = git(folder, env, 'rev-parse', '--absolute-git-dir');
  ok('touch', ['-h', '-d', '2 days ago', path.join(gitdir, 'HEAD'), path.join(gitdir, 'index')]);
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

test('cleanup lists first, then removes only finished, idle and rebuildable files', t => {
  const { root, env } = sandbox(t);
  const remote = path.join(root, 'remote.git');
  const main = path.join(root, 'main');
  git(root, env, 'init', '-q', '--bare', remote);
  git(root, env, 'init', '-q', '-b', 'main', main);
  fs.writeFileSync(path.join(main, '.gitignore'), 'bin/\nobj/\nsaves/\n.evidence/\n');
  git(main, env, 'add', '.');
  git(main, env, 'commit', '-q', '-m', 'start');
  git(main, env, 'remote', 'add', 'origin', remote);
  git(main, env, 'push', '-q', '-u', 'origin', 'main');

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
  const oldTemp = path.join(env.TMPDIR, 'clankerworld-old-run');
  const newTemp = path.join(env.TMPDIR, 'clankerworld-new-run');
  fs.mkdirSync(oldTemp);
  fs.mkdirSync(newTemp);
  const oldKept = path.join(env.XDG_STATE_HOME, 'clankerworld', 'kept-evidence', 'gone-20200101');
  fs.mkdirSync(oldKept, { recursive: true });
  for (const folder of [unpinned, kept, legacy, oldTemp, oldKept]) {
    ok('find', [folder, '-exec', 'touch', '-d', '40 days ago', '{}', '+']);
  }

  const script = path.join(scripts, 'clean-workspace.sh');
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
  assert.equal(archived.length, 1);
  assert.match(archived[0], /^finished-\d{14}$/);
  assert.equal(fs.readFileSync(path.join(archive, archived[0], 'failure.trx'), 'utf8'), 'kept');

  assert.ok(!fs.existsSync(path.join(open, 'obj')));
  assert.ok(fs.existsSync(path.join(open, 'open.txt')));
  for (const folder of [dirty, local, unpushed, locked, active]) assert.ok(fs.existsSync(folder), folder);
  assert.ok(fs.existsSync(path.join(local, 'saves', 'world.json')));
  assert.ok(!fs.existsSync(unpinned));
  assert.ok(fs.existsSync(kept));
  assert.ok(!fs.existsSync(legacy));
  assert.ok(!fs.existsSync(oldTemp));
  assert.ok(fs.existsSync(newTemp));
});
