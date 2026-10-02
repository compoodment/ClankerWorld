# Playtest list

Changes that are merged but still need trying in the game by hand. Each file
covers one change or topic, and each bullet says what to do and what you should
see. Nothing here has been checked by hand yet.

## Playing through the list

Open a file, try its bullets, then tell any agent what you found. You don't
need to edit anything yourself. You can also ask an agent for the list.

## Keeping the list current

- **Adding:** a pull request whose change still needs a hands-on check adds a
  file here in the same pull request. Name it after the issue and the change,
  like a [changelog entry](../changes/README.md), such as
  `431-orchard-harvest.md`. One file per pull request keeps parallel pull
  requests from conflicting.
- **After a playtest:** the agent the owner gives the results to opens one pull
  request that removes each bullet that passed or failed, and deletes a file
  once it is empty. Bullets the owner did not try, or could not run, stay in the
  list. The pull request's description says for each bullet whether it passed,
  failed or could not be run, with the
  [Windows playtest](../docs/development/build-and-test.md#windows-playtests)
  details the owner gave; don't hold the list back for details nobody gave.
  Link each bullet's issue on a `Refs` line, never with a closing keyword such
  as `Closes`. If a bullet failed the same way, reopen the issue it links with
  `status:needs-pr`. If something else is wrong, or the link is a retired
  checklist such as #285, open a Bug issue that links back instead; a retired
  checklist is never reopened.
- Write in plain English and player terms
  ([Writing clearly](../CONTRIBUTING.md#writing-clearly)). Say what to do and
  what should happen, end each bullet with its issue link, and never claim a
  check passed.

A new file looks like this:

````markdown
# Orchard harvest orders

Waiting for a hands-on check after [#431](https://github.com/compoodment/ClankerWorld/issues/431).

- Give an adult next to a fruiting orchard a Direct order to harvest food. Once the food is gathered, the order shows as done. ([#431](https://github.com/compoodment/ClankerWorld/issues/431))
````

When a check needs the private server, start its bullet with "On the paired
world:". The Windows build is the one that counts; a passing automated test or
export does not replace a bullet here.
