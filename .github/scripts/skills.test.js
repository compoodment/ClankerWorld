const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');

// Claude Code lists skills from .claude/skills/ and Codex from .agents/skills/;
// each skill lives once in skills/ and both folders link to it.
const root = path.join(__dirname, '..', '..');

// A checkout without symbolic links, as Git for Windows makes by default, stores each link as a
// small file holding its target.
function linkTarget(link) {
  const target = fs.lstatSync(link).isSymbolicLink() ? fs.readlinkSync(link) : fs.readFileSync(link, 'utf8');
  return target.trim().replaceAll('\\', '/');
}

const skills = fs.readdirSync(path.join(root, 'skills'), { withFileTypes: true })
  .filter(entry => entry.isDirectory())
  .map(entry => entry.name);

test('every skill names its own folder and describes when to use it', () => {
  assert.ok(skills.length > 0);
  for (const name of skills) {
    const text = fs.readFileSync(path.join(root, 'skills', name, 'SKILL.md'), 'utf8');
    const frontMatter = /^---\r?\n([\s\S]*?)\r?\n---\r?\n/.exec(text);
    assert.ok(frontMatter, `skills/${name}/SKILL.md has no front matter`);
    assert.match(frontMatter[1], new RegExp(`^name: ${name}$`, 'm'), `skills/${name}/SKILL.md`);
    assert.match(frontMatter[1], /^description: \S/m, `skills/${name}/SKILL.md`);
  }
});

test('Claude Code and Codex find every skill, and only skills that exist', () => {
  for (const folder of ['.claude/skills', '.agents/skills']) {
    const links = fs.readdirSync(path.join(root, folder)).sort();
    assert.deepEqual(links, [...skills].sort(), folder);
    for (const name of links) {
      assert.equal(linkTarget(path.join(root, folder, name)), `../../skills/${name}`, `${folder}/${name}`);
    }
  }
});
