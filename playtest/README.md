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
- **After a playtest:** remove each bullet the owner says worked, and delete a
  file once it is empty. For each bullet that failed, reopen the issue it came
  from or open a Bug issue, link it, and remove the bullet.
- Write in plain English and player terms
  ([Writing clearly](../CONTRIBUTING.md#writing-clearly)). Say what to do and
  what should happen, end each bullet with its issue link, and never claim a
  check passed.

A new file looks like this:

````markdown
# Orchard harvest orders

Merged on 1 October from [#431](https://github.com/compoodment/ClankerWorld/issues/431).

- Give an adult next to a fruiting orchard a Direct order to harvest food. Once the food is gathered, the order shows as done. ([#431](https://github.com/compoodment/ClankerWorld/issues/431))
````

When a check needs the private server, start its bullet with "On the paired
world:". The Windows build is the one that counts; a passing automated test or
export does not replace a bullet here.
