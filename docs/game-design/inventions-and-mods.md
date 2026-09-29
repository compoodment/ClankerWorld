---
title: Inventions and mods
type: game-design
status: active
updated: 2026-09-29
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
sequence, not a removal or automatic post-launch deferral. Art generation for
new inventions, fallback representation, and prompted armor visuals will be
designed in that phase rather than assumed solved by the base roster.

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
  and require extra game-system support. The exact boundary remains open.
- The design direction combines **data describing game content** with **scripts run in a
  restricted environment**. Valid inventions can activate in the running world at
  a safe boundary. Agent-generated code must not run as arbitrary trusted code
  in the main game process, access credentials/files/network freely, or change
  protected physical and ownership laws. A test/validation stage, runtime
  limits, quarantine/rollback, failure feedback, and cooldown are part of the
  accepted direction.
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
  download or silent global installation. The exact file format and package
  user interface remain open.
- Both the Pause Menu and Main Menu have a **Mod Library** surface. The in-world
  Pause Menu entry sits immediately above its bottom **Quit to Menu** action;
  the Pause Menu does not offer Quit Game. The in-world view shows
  active/developing/failed creations, authors, and dependencies. The
  global view browses the latest save for each world and handles the personal
  library and imports/exports. Tabs such as This World, Personal Library,
  Import/Export, and History are accepted as a starting arrangement.
- The player wants **discovered capabilities inspectable** through World Info.
  This must not falsely imply that every agent knows every discovery.

### Leaning toward

Use a **network of discoveries and their requirements**, not a rigid visible technology tree or
unrestricted “invent a car out of sticks” system. Computment likes this
recommendation, but did not explicitly answer the separate final yes/no
question. The graph should show discoveries, not spoil a fixed future list.

### Still to decide

How scripts are restricted and which game functions they can use; the world
rules inventions cannot change; checks before activation and how to undo a
failed change; generated art and its cost; model invention prompts; required
materials and discoveries; who knows which capabilities; package compatibility,
dependencies and rights; the steps for importing outside mods; and how the
library handles updated or conflicting imports.
