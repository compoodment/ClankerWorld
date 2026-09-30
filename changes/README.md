# Changes waiting for the changelog

Each pull request that needs a changelog entry adds its own file here instead of
editing [CHANGELOG.md](../CHANGELOG.md), so parallel pull requests do not
conflict. [Keep documentation and the changelog useful](../CONTRIBUTING.md#keep-documentation-and-the-changelog-useful)
says when an entry is needed and how to name the file.

A file holds one plain-English bullet per change, written as it should appear
in the changelog:

```markdown
- A mandatory harvest instruction completes after gathering orchard fruit.
```

`scripts/collect-changes.sh` moves these entries into CHANGELOG.md, newest
first, and deletes their files.
Commit edits to tracked entries before collecting them. If an entry has staged
or unstaged changes, the script stops before changing the changelog or deleting
any entries. New, untracked entries can be collected directly.
