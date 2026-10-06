# Changes waiting for the changelog

Each pull request that needs a changelog entry adds its own file here instead of
editing [CHANGELOG.md](../CHANGELOG.md), so parallel pull requests do not
conflict. [Keep documentation and the changelog useful](../CONTRIBUTING.md#keep-documentation-and-the-changelog-useful)
says when an entry is needed and how to name the file.

A file holds one plain-English bullet per change, written as it should appear
in the changelog. Start the file with `- `, not a heading or a plain paragraph,
and put each further bullet on the next line, with no blank line between them.
`scripts/collect-changes.sh` stops, and the documentation tests fail, on a file
that does not start with a `- ` bullet. For example:

```markdown
- A mandatory harvest instruction completes after gathering orchard fruit.
```

`scripts/collect-changes.sh` moves these entries into CHANGELOG.md, newest
first, and deletes their files.
Each release section in the changelog is a flat list without categories.
Collection requires full Git history to order entries by their original
commits. In a shallow clone, the script stops before changing any files:
`Run git fetch --unshallow first: entry order needs full history.`

Commit edits to tracked entries before collecting them. If an entry has staged
or unstaged changes, the script stops before changing the changelog or deleting
any entries. New, untracked entries can be collected directly.

If your change makes a waiting entry wrong, edit or delete it in the same pull
request.
