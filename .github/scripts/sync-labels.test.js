const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { test } = require('node:test');
const syncLabels = require('./sync-labels.js');

const Repo = { owner: 'compoodment', repo: 'ClankerWorld' };
const canonical = { name: 'area:tooling', color: 'abcdef', description: 'Builds and workflow' };
const unrelated = { name: 'hand-made', color: '123456', description: 'Keep this label' };
const key = name => name.toLowerCase();

function configFile(t, config) {
  const temporaryRoot = fs.realpathSync(os.tmpdir());
  const directory = fs.mkdtempSync(path.join(temporaryRoot, 'clankerworld-labels-'));
  assert.equal(path.dirname(fs.realpathSync(directory)), temporaryRoot);
  assert.ok(path.basename(directory).startsWith('clankerworld-labels-'));
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const configPath = path.join(directory, 'labels.json');
  fs.writeFileSync(configPath, JSON.stringify(config));
  return configPath;
}

function scenario(t, { config = { labels: [canonical] }, labels = [], items = [] } = {}) {
  const configPath = configFile(t, config);
  const liveLabels = structuredClone(labels);
  const records = structuredClone(items);
  const changes = [];
  const reads = [];
  const messages = [];
  const findLabel = name => liveLabels.find(label => key(label.name) === key(name));
  const recordChange = (method, params) => {
    assert.equal(params.owner, Repo.owner);
    assert.equal(params.repo, Repo.repo);
    changes.push({ method, ...structuredClone(params) });
  };
  const issues = {
    listLabelsForRepo: 'repository-labels',
    listForRepo: 'issues-and-pull-requests',
    createLabel: async params => {
      recordChange('createLabel', params);
      assert.equal(findLabel(params.name), undefined, 'cannot create a duplicate label');
      liveLabels.push({ name: params.name, color: params.color, description: params.description });
    },
    updateLabel: async params => {
      recordChange('updateLabel', params);
      const label = findLabel(params.name);
      assert.ok(label, 'cannot update a missing label');
      // A GitHub label rename retains every issue and PR association.
      for (const item of records) {
        item.labels = item.labels.map(name => key(name) === key(label.name) ? params.new_name : name);
      }
      Object.assign(label, { name: params.new_name, color: params.color, description: params.description });
    },
    addLabels: async params => {
      recordChange('addLabels', params);
      const item = records.find(item => item.number === params.issue_number);
      assert.ok(item, 'cannot label a missing issue or PR');
      for (const name of params.labels) {
        assert.ok(findLabel(name), 'cannot attach a missing repository label');
        if (!item.labels.some(current => key(current) === key(name))) item.labels.push(name);
      }
    },
    deleteLabel: async params => {
      recordChange('deleteLabel', params);
      const label = findLabel(params.name);
      assert.ok(label, 'cannot delete a missing label');
      liveLabels.splice(liveLabels.indexOf(label), 1);
      for (const item of records) item.labels = item.labels.filter(name => key(name) !== key(params.name));
    },
  };
  const github = {
    rest: { issues },
    paginate: async (method, params) => {
      assert.equal(params.owner, Repo.owner);
      assert.equal(params.repo, Repo.repo);
      assert.equal(params.per_page, 100);
      reads.push({ method, ...structuredClone(params) });
      if (method === issues.listLabelsForRepo) return structuredClone(liveLabels);
      assert.equal(method, issues.listForRepo);
      return structuredClone(records.filter(item =>
        (params.state === 'all' || item.state === params.state) &&
        item.labels.some(name => key(name) === key(params.labels))));
    },
  };
  return {
    labels: liveLabels, items: records, changes, reads, messages,
    run: (dryRun = false) => syncLabels({ github, context: { repo: Repo },
      core: { info: message => messages.push(message) }, dryRun, configPath }),
  };
}

function change(method, params) {
  return { method, ...Repo, ...params };
}

function taggedItems(name) {
  return [
    { number: 1, state: 'open', labels: [name, unrelated.name] },
    { number: 2, state: 'closed', labels: [name] },
    { number: 3, state: 'open', pull_request: { url: 'pull/3' }, labels: [name] },
    { number: 4, state: 'closed', pull_request: { url: 'pull/4' }, labels: [name, canonical.name] },
    { number: 5, state: 'open', labels: [unrelated.name] },
  ];
}

test('creates missing configured labels and leaves unrelated repository labels alone', async t => {
  const state = scenario(t, { labels: [unrelated], items: [{ number: 7, state: 'open', labels: [unrelated.name] }] });
  await state.run();
  assert.deepEqual(state.changes, [change('createLabel', canonical)]);
  assert.deepEqual(state.labels, [unrelated, canonical]);
  assert.deepEqual(state.items[0].labels, [unrelated.name]);
  assert.deepEqual(state.messages, ['Create "area:tooling"']);
});

test('updates changed label names, colors and descriptions using case-insensitive lookup', async t => {
  for (const [reason, existing] of [
    ['name casing', { ...canonical, name: 'Area:Tooling' }],
    ['color', { ...canonical, color: '000000' }],
    ['description', { ...canonical, description: 'Old description' }],
    ['missing description', { name: canonical.name, color: canonical.color }],
  ]) {
    await t.test(reason, async t => {
      const state = scenario(t, { labels: [existing, unrelated],
        items: [{ number: 7, state: 'closed', labels: [existing.name, unrelated.name] }] });
      await state.run();
      assert.deepEqual(state.changes, [change('updateLabel', { name: existing.name,
        new_name: canonical.name, color: canonical.color, description: canonical.description })]);
      assert.deepEqual(state.labels, [canonical, unrelated]);
      assert.deepEqual(state.items[0].labels, [canonical.name, unrelated.name]);
    });
  }
});

test('unchanged labels, including uppercase hex colors, cause no mutations', async t => {
  const existing = { ...canonical, color: canonical.color.toUpperCase() };
  const state = scenario(t, { config: { labels: [{ ...canonical, aliases: ['missing-alias'] }], retired: ['missing-retired'] },
    labels: [existing, unrelated] });
  await state.run();
  await state.run();
  assert.deepEqual(state.changes, []);
  assert.deepEqual(state.messages, []);
  assert.deepEqual(state.labels, [existing, unrelated]);
});

test('renaming an alias keeps it on open and closed issues and pull requests', async t => {
  const old = { name: 'Area:Build', color: '000000', description: 'Old build label' };
  const items = taggedItems(old.name);
  // The target label does not exist yet in this repository.
  items[3].labels = [old.name];
  const state = scenario(t, { config: { labels: [{ ...canonical, aliases: ['area:build'] }] },
    labels: [old, unrelated], items });
  await state.run();
  assert.deepEqual(state.changes, [change('updateLabel', { name: old.name,
    new_name: canonical.name, color: canonical.color, description: canonical.description })]);
  assert.deepEqual(state.labels, [canonical, unrelated]);
  for (const item of state.items.slice(0, 4)) {
    assert.ok(item.labels.includes(canonical.name), `#${item.number} keeps its renamed label`);
    assert.ok(!item.labels.includes(old.name));
  }
  assert.deepEqual(state.items[0].labels, [canonical.name, unrelated.name]);
  assert.deepEqual(state.items[4], items[4]);
  assert.deepEqual(state.reads.map(read => read.method), ['repository-labels']);
  assert.deepEqual(state.messages, ['Rename "Area:Build" to "area:tooling"']);
  // Rerunning after the rename must not recreate the old alias or repeat work.
  state.changes.length = 0;
  await state.run();
  assert.deepEqual(state.changes, []);
});

test('an existing target receives every alias association before the old label is deleted', async t => {
  const old = { name: 'Area:Build', color: '000000', description: 'Old build label' };
  const items = taggedItems(old.name);
  const state = scenario(t, { config: { labels: [{ ...canonical, aliases: ['area:build'] }] },
    labels: [old, canonical, unrelated], items });
  await state.run();
  assert.deepEqual(state.reads, [
    { method: 'repository-labels', ...Repo, per_page: 100 },
    { method: 'issues-and-pull-requests', ...Repo, labels: old.name, state: 'all', per_page: 100 },
  ]);
  assert.deepEqual(state.changes, [
    ...[1, 2, 3, 4].map(number => change('addLabels', { issue_number: number, labels: [canonical.name] })),
    change('deleteLabel', { name: old.name }),
  ]);
  assert.deepEqual(state.labels, [canonical, unrelated]);
  for (const item of state.items.slice(0, 4)) {
    assert.ok(item.labels.includes(canonical.name), `#${item.number} receives the target label`);
    assert.ok(!item.labels.includes(old.name));
    assert.equal(item.labels.filter(name => name === canonical.name).length, 1);
  }
  assert.deepEqual(state.items[0].labels, [unrelated.name, canonical.name]);
  assert.deepEqual(state.items[4], items[4]);
  assert.deepEqual(state.messages, [
    ...[1, 2, 3, 4].map(number => `Add "area:tooling" to #${number}, which has "Area:Build"`),
    'Delete "Area:Build" now that its issues have "area:tooling"',
  ]);
});

test('deletes configured retired labels case-insensitively and preserves other associations', async t => {
  const retired = { name: 'Status:Old', color: '000000', description: 'Retired status' };
  const items = [{ number: 7, state: 'closed', labels: [retired.name, unrelated.name, canonical.name] }];
  const state = scenario(t, { config: { labels: [canonical], retired: ['status:old', 'missing-retired'] },
    labels: [canonical, retired, unrelated], items });
  await state.run();
  assert.deepEqual(state.changes, [change('deleteLabel', { name: retired.name })]);
  assert.deepEqual(state.labels, [canonical, unrelated]);
  assert.deepEqual(state.items[0].labels, [unrelated.name, canonical.name]);
  assert.deepEqual(state.messages, ['Delete retired label "Status:Old"']);
});

test('dry runs describe create, update, rename and retirement without mutating GitHub', async t => {
  const create = { name: 'type:tooling', color: '123456', description: 'Workflow change' };
  const update = { ...canonical, description: 'Old description' };
  const rename = { name: 'area:world', color: '987654', description: 'World' };
  const old = { name: 'world-old', color: '000000', description: 'Old world' };
  const retired = { name: 'status:retired', color: '000000', description: 'Retired' };
  const labels = [update, old, retired, unrelated];
  const items = [{ number: 7, state: 'open', labels: [old.name, retired.name, unrelated.name] }];
  const state = scenario(t, { config: { labels: [create, canonical, { ...rename, aliases: [old.name] }],
    retired: [retired.name] }, labels, items });
  await state.run(true);
  assert.deepEqual(state.changes, []);
  assert.deepEqual(state.labels, labels);
  assert.deepEqual(state.items, items);
  assert.deepEqual(state.messages, [
    '[dry run] Create "type:tooling"',
    '[dry run] Update "area:tooling"',
    '[dry run] Rename "world-old" to "area:world"',
    '[dry run] Delete retired label "status:retired"',
  ]);
});

test('dry runs with an existing alias target describe all transfers and deletion without mutations', async t => {
  const old = { name: 'area:build', color: '000000', description: 'Old build label' };
  const labels = [old, canonical, unrelated];
  const items = taggedItems(old.name);
  const state = scenario(t, { config: { labels: [{ ...canonical, aliases: [old.name] }] }, labels, items });
  await state.run(true);
  assert.deepEqual(state.changes, []);
  assert.deepEqual(state.labels, labels);
  assert.deepEqual(state.items, items);
  assert.equal(state.reads[1].state, 'all');
  assert.deepEqual(state.messages, [
    ...[1, 2, 3, 4].map(number => `[dry run] Add "area:tooling" to #${number}, which has "area:build"`),
    '[dry run] Delete "area:build" now that its issues have "area:tooling"',
  ]);
});

test('invalid label configurations fail before reading or mutating GitHub', async t => {
  for (const [reason, config, problem] of [
    ['duplicate names', { labels: [canonical, { ...canonical, name: 'AREA:TOOLING' }] }, /listed twice/],
    ['bad color', { labels: [{ ...canonical, color: '#abcdef' }] }, /six-digit hex color/],
    ['empty description', { labels: [{ ...canonical, description: '' }] }, /needs a description/],
    ['long description', { labels: [{ ...canonical, description: 'x'.repeat(101) }] }, /at most 100 characters/],
    ['alias is a label', { labels: [{ ...canonical, aliases: ['AREA:TOOLING'] }] }, /also a label name/],
    ['duplicate aliases', { labels: [{ ...canonical, aliases: ['old', 'OLD'] }] }, /listed twice/],
    ['retired label is current', { labels: [canonical], retired: ['AREA:TOOLING'] }, /still in use above/],
    ['retired label is an alias', { labels: [{ ...canonical, aliases: ['old'] }], retired: ['OLD'] }, /still in use above/],
  ]) {
    await t.test(reason, async t => {
      const state = scenario(t, { config, labels: [canonical, unrelated] });
      await assert.rejects(state.run(), problem);
      assert.deepEqual(state.reads, []);
      assert.deepEqual(state.changes, []);
      assert.deepEqual(state.messages, []);
    });
  }
});
