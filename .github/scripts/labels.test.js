const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const { readConfig } = require('./sync-labels.js');

const Root = path.resolve(__dirname, '../..');
const Config = readConfig(path.join(Root, '.github/labels.json'));

function formLabels(source) {
  const labels = [];
  const lines = source.split(/\r?\n/);
  for (let index = 0; index < lines.length; index++) {
    const match = lines[index].match(/^labels:\s*(.*)$/);
    if (!match) continue;
    const inline = match[1].trim();
    if (inline && !inline.startsWith('#')) {
      assert.match(inline, /^\[.*\](?:\s*#.*)?$/, 'Issue form labels must be a YAML list');
      const list = inline.slice(1, inline.lastIndexOf(']'));
      for (const item of list.split(',')) {
        const name = item.trim().replace(/^(['"])(.*)\1$/, '$2');
        if (name) labels.push(name);
      }
    } else {
      for (index++; index < lines.length; index++) {
        const item = lines[index].match(/^\s*-\s*(.*?)\s*(?:\s+#.*)?$/);
        if (item) labels.push(item[1].replace(/^(['"])(.*)\1$/, '$2'));
        else if (lines[index].trim() && !lines[index].trim().startsWith('#')) { index--; break; }
      }
    }
  }
  return labels;
}

function scriptLabels(source) {
  // Read concrete label literals, including array entries, area keys and return
  // values. Prefixes used by startsWith (such as 'area:') are not labels.
  const code = source.split(/\r?\n/).filter(line => !line.trim().startsWith('//')).join('\n');
  return [...code.matchAll(/(['"])((?:status|type|area|priority):[^'"\s]+|owner-task|owner-priority)\1/g)]
    .map(match => match[2]);
}

function searchLabels(source) {
  return [...source.matchAll(/\blabel:(?:"([^"]+)"|([\w:-]+))/g)]
    .map(match => match[1] ?? match[2]);
}

function checkLabels(names, file, config = Config) {
  const active = new Set(config.labels.map(label => label.name));
  const retired = new Set(config.retired ?? []);
  for (const name of names) {
    assert.ok(!retired.has(name), `${file} uses retired label "${name}"`);
    assert.ok(active.has(name), `${file} uses unknown label "${name}"`);
  }
}

test('issue forms use only active configured labels', () => {
  const directory = path.join(Root, '.github/ISSUE_TEMPLATE');
  for (const file of fs.readdirSync(directory).filter(name => name.endsWith('.yml') && name !== 'config.yml')) {
    const names = formLabels(fs.readFileSync(path.join(directory, file), 'utf8'));
    assert.ok(names.length > 0, `${file} has no issue labels`);
    checkLabels(names, file);
  }
});

test('pull request and stale-claim scripts use only active configured labels', () => {
  for (const file of ['pr-labels.js', 'stale-claims.js']) {
    const names = scriptLabels(fs.readFileSync(path.join(__dirname, file), 'utf8'));
    assert.ok(names.length > 0, `${file} has no concrete label literals`);
    checkLabels(names, file);
  }
});

test('documented GitHub searches use only active configured labels', () => {
  for (const file of ['CONTRIBUTING.md', 'AGENTS.md']) {
    const names = searchLabels(fs.readFileSync(path.join(Root, file), 'utf8'));
    assert.ok(names.length > 0, `${file} has no label searches`);
    checkLabels(names, file);
  }
});

test('misspelled labels in each source fail with their location and name', () => {
  for (const [file, extract, source] of [
    ['implementation.yml', formLabels, 'labels: ["type:feature", "status:needs-ppr"]'],
    ['pr-labels.js', scriptLabels, "const Ready = 'status:needs-ppr';"],
    ['AGENTS.md', searchLabels, 'is:issue label:"status:needs-ppr"'],
  ]) {
    assert.throws(() => checkLabels(extract(source), file), error =>
      error.message.includes(file) && error.message.includes('status:needs-ppr'));
  }
});

test('retired names fail and both YAML list forms and unquoted searches are checked', () => {
  assert.deepEqual(formLabels('labels: [type:bug, \'priority:p2\'] # defaults\n'), ['type:bug', 'priority:p2']);
  assert.deepEqual(formLabels('labels:\r\n  - "type:bug"\r\n  - priority:p2\r\nbody:\r\n  - type: input'),
    ['type:bug', 'priority:p2']);
  assert.deepEqual(searchLabels('label:owner-task -label:"status:in-progress"'), ['owner-task', 'status:in-progress']);
  assert.deepEqual(scriptLabels("const Prefix = 'status:'; const Types = ['type:docs'];"), ['type:docs']);
  assert.throws(() => checkLabels(['status:needs-playtest'], 'old search'), /old search uses retired label/);
});
