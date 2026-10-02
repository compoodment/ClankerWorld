# Playtest list

Changes that are merged but still need trying in the game by hand. Each
checklist file, apart from this README, covers one change or topic and contains
only check bullets. Each bullet says what to do and what you should see,
ending with its issue or pull request link.
Nothing here has been checked by hand yet.

## Playing through the list

Open a file, try its bullets, then tell any agent what you found. You don't
need to edit anything yourself. You can also ask an agent for the list.

## Keeping the list current

- **Adding:** a pull request whose change still needs a hands-on check adds a
  file here in the same pull request. Name it after the issue and the change,
  like a [changelog entry](../changes/README.md), such as
  `431-orchard-harvest.md`. One file per pull request keeps parallel pull
  requests from conflicting. Put setup and context in the check bullets, not
  in a heading or a separate status paragraph.
- **After a playtest:** the agent the owner gives the results to opens one pull
  request that removes each bullet that passed or failed, and deletes a file
  once it is empty. Bullets the owner did not try, or could not run, stay in the
  list. The pull request's description says for each bullet whether it passed,
  failed or could not be run, with the
  [Windows playtest](../docs/development/build-and-test.md#windows-playtests)
  details the owner gave; don't hold the list back for details nobody gave.
  There is no separate Playtest report issue. Record build details in linked
  Bug or Implementation issues and label that work `from:playtest`.
  Link each bullet's source in the description, using `Refs` for issues and
  never a closing keyword such as `Closes`. If a bullet failed in the same way
  as its linked Bug or Implementation issue, reopen that issue with
  `status:needs-pr`. If something else is wrong, or the link is a source pull
  request or retired checklist such as #285, open a Bug issue that links back
  instead; a retired checklist is never reopened. An agreed improvement gets
  an Implementation issue; if it needs a new game choice, follow the existing
  Decisions rule.
- Write in plain English and player terms
  ([Writing clearly](../CONTRIBUTING.md#writing-clearly)). Say what to do and
  what should happen, end each bullet with its issue or pull request link, and
  never claim a check passed.

A new file looks like this:

````markdown
- Give an adult next to a fruiting orchard a Direct order to harvest food. Once the food is gathered, the order shows as done. ([#431](https://github.com/compoodment/ClankerWorld/issues/431))
````

When a check needs the private server, start its bullet with "On the paired
world:". The Windows build is the one that counts; a passing automated test or
export does not replace a bullet here.

Before a release, the owner also runs the short
[Windows release smoke check](../docs/development/releasing.md#windows-release-smoke-check).
The release notes list every remaining checklist file as not yet checked by
hand; this list does not have to be emptied before an alpha release.
