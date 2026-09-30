// Keeps the repository's labels in line with .github/labels.json.
// Run by .github/workflows/labels.yml through actions/github-script.
// Labels not named in the file are left alone, except those listed as retired.
const fs = require('fs');

const MaxDescriptionLength = 100;

function readConfig(path = '.github/labels.json') {
  const config = JSON.parse(fs.readFileSync(path, 'utf8'));
  const problems = [];
  const names = new Set();
  for (const label of config.labels) {
    const key = label.name.toLowerCase();
    if (names.has(key)) problems.push(`"${label.name}" is listed twice.`);
    names.add(key);
    if (!/^[0-9a-f]{6}$/i.test(label.color)) problems.push(`"${label.name}" needs a six-digit hex color.`);
    if (!label.description || label.description.length > MaxDescriptionLength) {
      problems.push(`"${label.name}" needs a description of at most ${MaxDescriptionLength} characters.`);
    }
  }
  const aliases = new Set();
  for (const label of config.labels) {
    for (const alias of label.aliases ?? []) {
      const key = alias.toLowerCase();
      if (names.has(key)) problems.push(`Alias "${alias}" is also a label name.`);
      if (aliases.has(key)) problems.push(`Alias "${alias}" is listed twice.`);
      aliases.add(key);
    }
  }
  for (const name of config.retired ?? []) {
    const key = name.toLowerCase();
    if (names.has(key) || aliases.has(key)) problems.push(`Retired label "${name}" is still in use above.`);
  }
  if (problems.length > 0) throw new Error(`.github/labels.json is not valid:\n${problems.join('\n')}`);
  return config;
}

async function syncLabels({ github, context, core, dryRun, configPath }) {
  const config = readConfig(configPath);
  const { owner, repo } = context.repo;
  const existing = await github.paginate(github.rest.issues.listLabelsForRepo, { owner, repo, per_page: 100 });
  const byName = new Map(existing.map(label => [label.name.toLowerCase(), label]));
  const act = async (message, change) => {
    core.info(`${dryRun ? '[dry run] ' : ''}${message}`);
    if (!dryRun) await change();
  };

  for (const label of config.labels) {
    let current = byName.get(label.name.toLowerCase());
    for (const alias of label.aliases ?? []) {
      const old = byName.get(alias.toLowerCase());
      if (!old) continue;
      byName.delete(alias.toLowerCase());
      if (!current) {
        // Renaming keeps the label on every issue and pull request that has it.
        await act(`Rename "${old.name}" to "${label.name}"`, () => github.rest.issues.updateLabel({
          owner, repo, name: old.name, new_name: label.name, color: label.color, description: label.description,
        }));
        current = { name: label.name, color: label.color, description: label.description };
        byName.set(label.name.toLowerCase(), current);
        continue;
      }

      // The new label already exists, so move the old one across before deleting it.
      const items = await github.paginate(github.rest.issues.listForRepo, {
        owner, repo, labels: old.name, state: 'all', per_page: 100,
      });
      for (const item of items) {
        await act(`Add "${label.name}" to #${item.number}, which has "${old.name}"`, () =>
          github.rest.issues.addLabels({ owner, repo, issue_number: item.number, labels: [label.name] }));
      }
      await act(`Delete "${old.name}" now that its issues have "${label.name}"`, () =>
        github.rest.issues.deleteLabel({ owner, repo, name: old.name }));
    }

    if (!current) {
      await act(`Create "${label.name}"`, () => github.rest.issues.createLabel({
        owner, repo, name: label.name, color: label.color, description: label.description,
      }));
      byName.set(label.name.toLowerCase(), { name: label.name, color: label.color, description: label.description });
    } else if (current.name !== label.name ||
        current.color.toLowerCase() !== label.color.toLowerCase() ||
        (current.description ?? '') !== label.description) {
      await act(`Update "${current.name}"`, () => github.rest.issues.updateLabel({
        owner, repo, name: current.name, new_name: label.name, color: label.color, description: label.description,
      }));
    }
  }

  for (const name of config.retired ?? []) {
    const old = byName.get(name.toLowerCase());
    if (!old) continue;
    await act(`Delete retired label "${old.name}"`, () => github.rest.issues.deleteLabel({ owner, repo, name: old.name }));
  }
}

module.exports = syncLabels;
module.exports.readConfig = readConfig;
