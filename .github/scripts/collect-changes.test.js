const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { test } = require('node:test');
const { pathToFileURL } = require('node:url');

const Collector = path.resolve(__dirname, '../../scripts/collect-changes.sh');
const InitialChangelog = '# Changelog\n\n## Unreleased\n\n- Existing unreleased entry.\n\n## 0.1.0\n\n- Earlier release.\n';
const ShallowDiagnostic = 'Run git fetch --unshallow first: entry order needs full history.\n';

function run(directory, command, args, extraEnvironment = {}) {
  // A contributor's Git configuration or enclosing worktree must not affect these disposable repositories.
  const environment = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith('GIT_')));
  const result = spawnSync(command, args, {
    cwd: directory,
    encoding: 'utf8',
    timeout: 10_000,
    env: { ...environment, GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: os.devNull,
      GIT_TERMINAL_PROMPT: '0', ...extraEnvironment },
  });
  assert.ifError(result.error);
  assert.equal(result.signal, null, `${command} was interrupted: ${result.stderr}`);
  return result;
}

function git(directory, args, environment) {
  const result = run(directory, 'git', args, environment);
  assert.equal(result.status, 0, `git ${args.join(' ')} failed: ${result.stderr}`);
  return result.stdout.trim();
}

function fixture(t) {
  const temporaryRoot = fs.realpathSync(os.tmpdir());
  const directory = fs.mkdtempSync(path.join(temporaryRoot, 'clankerworld-changes-'));
  assert.equal(path.dirname(fs.realpathSync(directory)), temporaryRoot);
  assert.ok(path.basename(directory).startsWith('clankerworld-changes-'));
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const repository = path.join(directory, 'full');
  fs.mkdirSync(path.join(repository, 'scripts'), { recursive: true });
  fs.mkdirSync(path.join(repository, 'changes'));
  fs.copyFileSync(Collector, path.join(repository, 'scripts/collect-changes.sh'));
  fs.writeFileSync(path.join(repository, 'CHANGELOG.md'), InitialChangelog);
  fs.writeFileSync(path.join(repository, 'changes/README.md'), '# Waiting changes\n');
  git(repository, ['init', '--initial-branch=main']);
  git(repository, ['config', 'user.name', 'Collector test']);
  git(repository, ['config', 'user.email', 'collector@example.invalid']);
  git(repository, ['config', 'commit.gpgsign', 'false']);
  const commit = (message, date) => {
    git(repository, ['add', '.']);
    git(repository, ['commit', '--quiet', '-m', message], {
      GIT_AUTHOR_DATE: date, GIT_COMMITTER_DATE: date,
    });
  };
  commit('Initial changelog', '2000-01-01T12:00:00Z');
  fs.writeFileSync(path.join(repository, 'changes/a-older.md'), '- Older change.\n');
  commit('Older entry', '2000-01-02T12:00:00Z');
  fs.writeFileSync(path.join(repository, 'changes/z-newer.md'), '\r\n- Newer change.\r\n- Another newer detail.\r\n\r\n');
  commit('Newer entry', '2000-01-03T12:00:00Z');
  return { directory, repository };
}

function snapshot(repository) {
  const files = {};
  function visit(relative) {
    for (const entry of fs.readdirSync(path.join(repository, relative), { withFileTypes: true })
      .sort((left, right) => left.name.localeCompare(right.name))) {
      if (entry.name === '.git') continue;
      const name = path.join(relative, entry.name);
      if (entry.isDirectory()) visit(name);
      else files[name] = fs.readFileSync(path.join(repository, name)).toString('base64');
    }
  }
  visit('');
  return { files, index: fs.readFileSync(path.join(repository, '.git/index')).toString('base64') };
}

test('full history collects newest first, preserves other content and changes nothing on a second run', t => {
  const { repository } = fixture(t);
  assert.equal(git(repository, ['rev-parse', '--is-shallow-repository']), 'false');
  fs.writeFileSync(path.join(repository, 'changes/m-untracked.md'), '- Untracked change.\n');
  fs.writeFileSync(path.join(repository, 'notes.txt'), 'Unrelated untracked work.\n');

  const result = run(repository, 'bash', ['scripts/collect-changes.sh']);

  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stderr, '');
  assert.equal(result.stdout, 'Moved 3 change entries into CHANGELOG.md.\n');
  assert.equal(fs.readFileSync(path.join(repository, 'CHANGELOG.md'), 'utf8'),
    InitialChangelog.replace('## Unreleased\n', '## Unreleased\n\n- Untracked change.\n- Newer change.\n- Another newer detail.\n- Older change.\n'));
  assert.deepEqual(fs.readdirSync(path.join(repository, 'changes')), ['README.md']);
  assert.equal(fs.readFileSync(path.join(repository, 'changes/README.md'), 'utf8'), '# Waiting changes\n');
  assert.equal(fs.readFileSync(path.join(repository, 'notes.txt'), 'utf8'), 'Unrelated untracked work.\n');
  assert.equal(git(repository, ['diff', '--cached', '--name-only', '--diff-filter=D']),
    'changes/a-older.md\nchanges/z-newer.md');
  const afterFirstRun = snapshot(repository);

  const repeated = run(repository, 'bash', ['scripts/collect-changes.sh']);

  assert.equal(repeated.status, 0, repeated.stderr);
  assert.equal(repeated.stdout, 'No change entries to collect.\n');
  assert.equal(repeated.stderr, '');
  assert.deepEqual(snapshot(repository), afterFirstRun);
});

test('a real shallow clone refuses collection before changing any worktree file or index byte', t => {
  const { directory, repository } = fixture(t);
  const shallow = path.join(directory, 'shallow');
  git(directory, ['clone', '--quiet', '--depth', '1', pathToFileURL(repository).href, shallow]);
  assert.equal(git(shallow, ['rev-parse', '--is-shallow-repository']), 'true');
  assert.equal(git(shallow, ['rev-parse', 'HEAD']), git(repository, ['rev-parse', 'HEAD']));
  const before = snapshot(shallow);

  const result = run(shallow, 'bash', ['scripts/collect-changes.sh']);

  assert.notEqual(result.status, 0);
  assert.equal(result.stderr, ShallowDiagnostic);
  assert.equal(result.stdout, '');
  assert.deepEqual(snapshot(shallow), before);
});

for (const staged of [false, true]) {
  test(`${staged ? 'staged' : 'unstaged'} tracked entry changes refuse collection without changing the worktree or index`, t => {
    const { repository } = fixture(t);
    fs.appendFileSync(path.join(repository, 'changes/a-older.md'), '- Waiting correction.\n');
    if (staged) git(repository, ['add', 'changes/a-older.md']);
    const before = snapshot(repository);

    const result = run(repository, 'bash', ['scripts/collect-changes.sh']);

    assert.notEqual(result.status, 0);
    assert.equal(result.stderr, 'Commit changes to changes/a-older.md before collecting entries.\n');
    assert.equal(result.stdout, '');
    assert.deepEqual(snapshot(repository), before);
  });
}
