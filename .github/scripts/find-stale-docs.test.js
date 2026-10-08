const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { test } = require('node:test');
const { blocks, find } = require('../../scripts/find-stale-docs.js');

function repository(t, files) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'stale-docs-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  for (const [name, text] of Object.entries(files)) {
    fs.mkdirSync(path.dirname(path.join(root, name)), { recursive: true });
    fs.writeFileSync(path.join(root, name), text);
  }
  return root;
}

test('Markdown splits into paragraphs, list items and table rows, each with its heading', () => {
  const text = [
    '# Page', '', 'First paragraph', 'continues here.', '', '## Still to decide', '',
    '- One item', '  wrapped.', '- Two', '', '| a | b |', '| c | d |', '', '```', '- not a block', '```',
  ].join('\n');
  assert.deepEqual(blocks(text).map(block => [block.line, block.heading, block.text]), [
    [3, 'Page', 'First paragraph continues here.'],
    [8, 'Still to decide', '- One item wrapped.'],
    [10, 'Still to decide', '- Two'],
    [12, 'Still to decide', '| a | b |'],
    [13, 'Still to decide', '| c | d |'],
  ]);
});

test('a sentence that names the change and says it is missing or open is found on any page', t => {
  const root = repository(t, {
    'docs/what-works.md': 'Orchards grow fruit.\n\nOrchard trees cannot be planted yet.\n',
    'docs/development/how-it-works.md': '- Planting checks the species; orchard propagation\n  is still open.\n',
    'docs/game-design/world.md': '## Still to decide\n\n- How orchards spread.\n\n## Agreed\n\n- Orchards fruit in autumn.\n',
    'tools/ArtPreview/Proposed/Crops.md': 'The orchard sapling art is not drawn yet.\n',
    'README.md': 'Orchards are unfinished.\n',
    'CHANGELOG.md': 'Orchards were not built yet in 0.1.\n',
    'docs/archive/old-design.md': 'Orchards were not built yet in the old design.\n',
    'changes/123-orchard.md': '- Orchard planting was not built yet.\n',
    'playtest/123-orchard.md': '- Check the unfinished orchard planting.\n',
    'docs/playing.md': 'Wheat is not built yet.\n',
  });
  const hits = find(['Orchard'], root);
  assert.deepEqual(hits.map(hit => `${hit.file}:${hit.line} ${hit.reason}`), [
    'README.md:1 "unfinished"',
    'docs/development/how-it-works.md:1 "still open"',
    'docs/game-design/world.md:3 under "Still to decide"',
    'docs/what-works.md:3 "cannot be planted yet"',
    'tools/ArtPreview/Proposed/Crops.md:1 "not drawn yet"',
  ]);
  // History, true statements and other subjects are left alone.
  assert.ok(!hits.some(hit => hit.file === 'CHANGELOG.md' || hit.file === 'docs/playing.md'));
  assert.ok(!hits.some(hit => hit.text.includes('fruit in autumn') || hit.text === 'Orchards grow fruit.'));
});

test('the command lists each place with its file and line, and needs at least one term', t => {
  const script = path.join(__dirname, '..', '..', 'scripts', 'find-stale-docs.js');
  const found = spawnSync('node', [script, 'seasonal night'], { encoding: 'utf8' });
  assert.equal(found.status, 0, found.stderr);
  assert.match(found.stdout, /place\(s\) to read|No sentence that names these terms/);
  const none = spawnSync('node', [script], { encoding: 'utf8' });
  assert.equal(none.status, 2);
  assert.match(none.stderr, /Give at least one term/);
});
