#!/usr/bin/env node
// Finds documentation that may still contradict a change: every paragraph, list item or table row
// that names one of the given terms and also says something is missing, unfinished or undecided.
//   node scripts/find-stale-docs.js <term> [<term> ...]
// Run it with the names a reader would use for what you changed (the feature, building, item,
// order, setting or class), then fix every sentence your change made false, on any page.
// It only finds candidates; reading each hit decides whether it is still true.
const fs = require('fs');
const path = require('path');

const Root = path.join(__dirname, '..');
// Pages that describe the game or the code. History (CHANGELOG.md, changes/, archive/) and playtest
// checks describe the past or a test, so they are left out.
const Folders = ['docs', 'tools'];
const Files = ['README.md'];

// Wording that says something isn't there yet. A change that builds, removes or decides the thing
// makes such a sentence false.
const StatusPattern = new RegExp([
  String.raw`\b(?:not|cannot|can't|isn't|aren't|doesn't|don't|won't)\b[^.;:]{0,40}?\byet\b`,
  String.raw`\bnot (?:built|implemented|available|supported|playable|connected|tracked)\b`,
  String.raw`\bunfinished\b`, String.raw`\bunimplemented\b`, String.raw`\bundecided\b`,
  String.raw`\b(?:still|remains?|stays?|left) open\b`, String.raw`\bstill to decide\b`, String.raw`\bto be decided\b`,
  String.raw`\bremains? (?:to be )?(?:designed|decided|built|measured)\b`,
  String.raw`\bcomes? later\b`, String.raw`\blater (?:work|stage|version|phase)\b`, String.raw`\bplaceholder\b`,
].join('|'), 'i');
// Sections whose every entry is an open question, so naming the term there is enough.
const OpenSectionPattern = /still to decide|leaning toward|suggestion|open questions|questions linking/i;

function markdownFiles(dir) {
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(item => {
    if (item.name.startsWith('.') || item.name === 'node_modules' || item.name === 'bin' || item.name === 'obj' || item.name === 'archive') return [];
    const full = path.join(dir, item.name);
    if (item.isDirectory()) return markdownFiles(full);
    return item.name.endsWith('.md') ? [full] : [];
  });
}

// Splits Markdown into blocks: paragraphs, and each list item and table row on its own, with the
// heading each block sits under and the line it starts on.
function blocks(text) {
  const result = [];
  let heading = '';
  let current = null;
  let fenced = false;
  const finish = () => { if (current) result.push(current); current = null; };
  text.split('\n').forEach((line, index) => {
    if (/^\s*(```|~~~)/.test(line)) { fenced = !fenced; finish(); return; }
    if (fenced) return;
    const headingMatch = /^#{1,6}\s+(.*)$/.exec(line);
    if (headingMatch) { finish(); heading = headingMatch[1].trim(); return; }
    if (line.trim() === '') { finish(); return; }
    const starts = /^\s*([-*+]\s|\d+\.\s|\|)/.test(line);
    if (starts || !current) {
      finish();
      current = { line: index + 1, heading, text: line.trim() };
    } else {
      current.text += ` ${line.trim()}`;
    }
  });
  finish();
  return result;
}

function find(terms, root = Root) {
  const wanted = terms.map(term => term.trim().toLowerCase()).filter(term => term !== '');
  if (wanted.length === 0) throw new Error('Give at least one term: the names a reader would use for what you changed.');
  const files = [...Folders.flatMap(folder => markdownFiles(path.join(root, folder))),
    ...Files.map(file => path.join(root, file)).filter(file => fs.existsSync(file))];
  const hits = [];
  for (const file of files.sort()) {
    for (const block of blocks(fs.readFileSync(file, 'utf8'))) {
      const lower = block.text.toLowerCase();
      const terms = wanted.filter(term => lower.includes(term));
      if (terms.length === 0) continue;
      const status = StatusPattern.exec(block.text);
      const openSection = OpenSectionPattern.test(block.heading);
      if (!status && !openSection) continue;
      hits.push({
        file: path.relative(root, file).split(path.sep).join('/'), line: block.line, terms,
        reason: status ? `"${status[0]}"` : `under "${block.heading}"`, text: block.text,
      });
    }
  }
  return hits;
}

function excerpt(text, limit = 220) {
  return text.length <= limit ? text : `${text.slice(0, limit - 1)}…`;
}

if (require.main === module) {
  try {
    const hits = find(process.argv.slice(2));
    for (const hit of hits) console.log(`${hit.file}:${hit.line}: ${hit.reason}: ${excerpt(hit.text)}`);
    console.log(hits.length === 0
      ? 'No sentence that names these terms says something is missing, unfinished or undecided.'
      : `${hits.length} place(s) to read. Fix each one your change made false, on any page, in the same pull request.`);
  } catch (error) {
    console.error(error.message);
    process.exit(2);
  }
}

module.exports = { StatusPattern, OpenSectionPattern, blocks, find };
