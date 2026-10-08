---
title: Inventions and mods
type: game-design
status: active
updated: 2026-10-08
---

# Inventions and mods

These are the intended game rules and choices. They do not describe
everything that is available in the current build. See [what works today](../what-works.md).

[Game-design guide](README.md) explains the agreement labels.

## Inventions, mods, and technology

### Agreed

The Workshop and broader invention/mod mechanics are **late-development work**:
first make the base simulation functional and its asset set fuller. They remain
part of the intended finished game, including imported mods; this is a build
sequence, not a removal or automatic post-launch deferral. Prompted armor
visuals will be designed in that phase rather than assumed solved by the base
roster; invention pictures and their placeholder are agreed below.

- Agents can eventually create buildings, tools, crops, machines, art, laws,
  currencies, and other economic/cultural systems. They cannot invent or alter
  natural biomes. Needs, curiosity, aspirations, social standing, requests,
  and expertise may motivate invention. Agents may use, teach, hide, sell,
  improve, or compete with creations.
- An invention's novel behavior does not automatically imply a limitless new
  shape/material/animation combination. Its art must fit a supported visual
  configuration or pass a new-design path. Most inventions should combine
  familiar world actions—such as storing, crafting, cooking, transporting, or
  protecting—in new ways. Truly new classes of behavior are possible but rarer
  and require extra game-system support.
- **How agents invent, agreed on October 8
  ([#1268](https://github.com/compoodment/ClankerWorld/issues/1268)):** for
  the first complete game, an invention is **data that recombines existing
  actions**, such as storing, crafting, cooking, carrying and protecting,
  within limits. A truly new kind of behaviour needs a game update. The
  agent's model proposes an invention through a structured form, the server
  checks it, and each attempt is Workshop work that uses real materials.
  Agents' designs show in the Mod Library.
- **Invention pictures, agreed on October 8
  ([#1270](https://github.com/compoodment/ClankerWorld/issues/1270)):** an
  invention's picture is built only from approved code-drawn parts and
  recolouring, never as an AI-generated image, and a plain placeholder stands
  in when that fails, so a working invention is never blocked by its art
  ([Interface and art](interface-and-art.md#pixel-art-and-generated-images)
  has the details).
- The design direction combines **data describing game content** with **scripts run in a
  restricted environment**. Valid inventions can activate in the running world at
  a safe boundary. Agent-generated code must not run as arbitrary trusted code
  in the main game process, access credentials/files/network freely, or change
  protected physical and ownership laws. A test/validation stage, runtime
  limits, quarantine/rollback, failure feedback, and cooldown are part of the
  accepted direction.
- **Scripts, checks and rollback, agreed on October 8
  ([#1269](https://github.com/compoodment/ClankerWorld/issues/1269)):** a
  script runs in a sandbox inside the game, with no input or output and with
  limits on its time and work. It can make only one kind of call: asking for
  ordinary world actions, which pass the normal checks. No script can change
  the protected rules: ownership, movement, needs, life stages, keys, model
  calls and saves. Before an invention or mod activates, it passes automatic
  checks and a trial run on a copy of the world, and the player may review it
  if they choose. If one is rolled back, the things already made with it
  become inert items that keep their owner.
- An **invention** originates in a particular world's fiction. A **mod** is a
  portable technical package. Inventions and active mods belong to their
  world save; the player may explicitly export an invention into a personal
  library and import it into another world. Nothing transfers silently.
- Human-authored or community-made mods can also be **explicitly imported** in
  the first complete game. They use the same package validation, content
  permissions, and restricted script sandbox as agent-made creations; there
  is no separate trusted-code shortcut for downloaded mods. A package can
  declare content and assets and, where supported, bounded scripted behavior.
  Import is a deliberate player action into a chosen world, not an automatic
  download or silent global installation.
- **Mod package and import, agreed on October 8
  ([#1271](https://github.com/compoodment/ClankerWorld/issues/1271)):** a mod
  is one archive holding its manifest, its images and any scripts. To import
  one, the player chooses the file; the game runs its checks and shows the
  mod's permissions, rights and dependencies; the player chooses a paused
  world; and the mod is staged there and activates at a safe point. A mod's
  images must meet the image rules for file format and metadata, with
  provisional limits of 4 MiB per file, 2048 × 2048 pixels and 256 frames.
  These rules cover only images a mod carries: built-in art stays drawn in
  code.
- Both the Pause Menu and Main Menu have a **Mod Library** surface. The in-world
  Pause Menu entry sits immediately above its bottom **Quit to Menu** action;
  the Pause Menu does not offer Quit Game. The in-world view shows
  active/developing/failed creations, authors, and dependencies. The
  global view browses the latest save for each world and handles the personal
  library and imports/exports. Tabs such as This World, Personal Library,
  Import/Export, and History are accepted as a starting arrangement.
- **Mod versions and conflicts, agreed on October 8
  ([#1272](https://github.com/compoodment/ClankerWorld/issues/1272)):** each
  world locks the exact versions of the mods it uses, and updating one is an
  explicit action in that world. A package with the same ID as one already
  there but different content is refused unless the player chooses to replace
  it. A save that needs a missing mod won't load until the mod is back, and
  the save is kept. The Mod Library's view across worlds reads the save branch
  each world continues from.
- The player wants **discovered capabilities inspectable** through World Info.
  This must not falsely imply that every agent knows every discovery.
- **Agreed on October 1
  ([#652](https://github.com/compoodment/ClankerWorld/issues/652)):** use a
  **network of discoveries and their requirements**, not a rigid visible
  technology tree or unrestricted “invent a car out of sticks” system. Each
  discovery needs materials, tools, skills or earlier discoveries. The graph
  shows discoveries already made, not a fixed list of future ones.
- **Who knows which capabilities, agreed on October 8
  ([#1246](https://github.com/compoodment/ClankerWorld/issues/1246)):**
  knowledge always belongs to a person. Books and other written goods can
  carry places, recipes and discoveries; skills still need practice or lessons. A discovery stays in
  the world's record when everyone who knew it has died, but nobody can use
  it until someone rediscovers it or reads it. A household or Town has no
  shared memory ([Agents and social life](agents-and-families.md#starting-agents-families-and-life-stages)).

### Still to decide

The rules for package rights are still open.
