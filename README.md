# ClankerWorld

ClankerWorld is a world simulation where AI agents live their own lives. They
find food, keep warm, build, make friends, have children and grow old. You watch
the world, inspect anyone's needs and memories, and can ask them to do things.
Each agent can use its own AI model. The game rules decide what can actually
happen and carry out the accepted choices.

The goal is a small society that survives, trades, forms institutions and
eventually invents things that change its world.

ClankerWorld is an **early private alpha**. You currently play through a Windows
game client connected to a private world server. A simple local Windows install
is part of the intended finished game; it is not available yet.

## Start here

- [Playing the current game](docs/playing.md): connecting, creating a world,
  choosing its first Town and starting its four agents.
- [What works today](docs/what-works.md): current features and their limits.
- [Game design](docs/game-design/README.md): the game we want to make, including
  agreed choices and questions still to settle.
- [GitHub Issues](https://github.com/compoodment/ClankerWorld/issues): reports,
  unfinished work, experiments and design discussions.
- [All documentation](docs/README.md): a guide for players and developers.

Time runs only while a connected client is present and the world is unpaused.
Closing the last client stops time and model calls after a short grace period.
Returning continues from the saved world without simulating time spent away.

API keys belong to the installation, separately from world saves. They are not
returned to the client or included in logs. You supply your own model-provider
keys and pay the provider's charges.

## Contribute or build

You do not need to code to help. Playtest notes, confusing text, screenshots and
clear bug reports are useful. [CONTRIBUTING.md](CONTRIBUTING.md) explains how to
report things and make changes.

Developers can use [build and test](docs/development/build-and-test.md) and
[how the game works](docs/development/how-it-works.md). The server's old web page
is a diagnostic tool; the supported game client is Godot.

ClankerWorld is available under the [MIT License](LICENSE).
