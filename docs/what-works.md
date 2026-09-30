---
title: What works today
type: product-status
status: active
updated: 2026-09-30
---

# What works today

This describes the **current repository build**, not every feature in the
[game design](game-design/README.md). The private alpha uses a Windows 11 x64
Godot client connected to a private world server. Repository changes may still
need deployment or testing on the owner's laptop. Check the tested build on an
issue before treating a source fix as a verified game fix.

## Status labels

- **Available in the game:** connected to the normal private-world and Godot path.
- **Basic version:** connected, but the experience or rules are still limited.
- **Built but not connected to normal play:** code and isolated tests exist,
  but players do not yet encounter the complete feature.
- **Not built yet:** an intended feature still needs implementation.

Availability and verification are separate. A feature can be connected in the
repository build while its Windows playtest is pending. A passing isolated
test alone does not make it available in the game.

## World and controls

| Feature | Status | Current limits |
| --- | --- | --- |
| Create, select, save and load worlds | Available in the game | Small/Medium maps; larger playable worlds and a local Windows host are unfinished. |
| Choose the first Town and place four founders before starting | Available in the game | Fixed five-building starter layout; suitability guidance, layout review and player-chosen supplies are unfinished. |
| Move around and inspect the map | Available in the game | Zoom, overview, wrapped east/west movement, tile facts and household/Town filters. General land claims are not recorded. |
| Pause, inspect agents, view family trees and read events | Available in the game | Deceased profiles retain recorded thoughts and memories; old deaths without an archive cannot be reconstructed. |
| Display and interface settings | Available in the game | Themes, window/render sizes, UI Scale (Automatic or 100–400%, enlarging the whole interface), weather switches and date/time formats. Windows visual and keyboard acceptance is still being checked. |

New worlds open paused with no old camp. Choosing the first Town places two
Houses, a Warehouse, Farmhouse and Blacksmith, linked by Roads. The Houses hold
household food; the Warehouse supplies a wooden axe and pickaxe. Four unrelated
founders form two starting households. The player presses Start World explicitly.

World time and hosted calls stop after the last connected client's short grace
period. Returning makes no offline progress; a manually paused world stays
paused. New worlds use six-minute days and a 40-day year as the current playtest
pace, subject to model/server load.

## Agents and their models

Each starting agent has a provider, model and key assignment. Stored keys can
be shared or selected independently. An agent's Profile lets you change these
choices and rename the person. Jev can be switched on or off for a paused world.

Personal models choose from legal actions. They receive some saved self
information, need values, a recent private thought, relevant personal memories
and known map facts. Recent work added name, life stage, personality, aspiration,
household and warmth/illness context where available. Nearby relationships,
carried inventory and current activity are not all supplied yet. Better pacing
or thought quality from this change has not been proven in live play.

One slow model can wait while other agents and the world continue. A failed or
low-confidence response uses only the explicit safe fallback; it does not invent
an important choice or complete a firm instruction. The paired Windows/model-wait
check remains in [the playtest checklist](https://github.com/compoodment/ClankerWorld/issues/285).

The agent card's **Speak to them** box sends a message as a **Suggest** or an
**Order**. This is a basic version. The game understands only orders to gather
food, eat or go toward food. The agent's model does not see the words. An order
the game can't act on closes at once, and the Event Log says the agent didn't
understand it. An order that can't be carried out yet waits without extra model
requests. The card does not yet show which orders are still waiting.

Thoughts, memories, beliefs and explored map facts belong to the individual
agent. The player can inspect mistakes and where a belief came from. Jev can
rank existing memories during a normal call. Automatic capture of experiences,
generated memory summaries and full conversations are unfinished.

## Life, work and society

| Feature | Status | Current limits |
| --- | --- | --- |
| Food, warmth, illness, clothing and shelter | Available in the game | Basic diet/recovery. No energy meter or sleep. Medicine and Clinic effects are not active gameplay. |
| Gathering and carrying | Basic version | Agents gather and eat food, keep a hearth burning, and collect the starter axe and pickaxe. |
| Building, farming and crafting | Built but not connected to normal play | Building plans, crop growing and recipe-based production are built and pass their own tests, but agents placed the normal way are not offered them. They are offered only to an agent who holds a work role, and a normal game gives agents none ([#441](https://github.com/compoodment/ClankerWorld/issues/441) tracks the fix). An offline check on three generated Small maps saw none of them in five world days ([#437](https://github.com/compoodment/ClankerWorld/issues/437)). It also saw no helping on projects, hauling inputs to the Blacksmith or Farmhouse, mining ore or replanting trees, and some of these may depend on the same limit. Recipes, capacity, wear and logistics remain unfinished. |
| Local exploration and physical maps | Basic version | Short outings record personal knowledge and can produce a map or field record to share or barter. Purposeful distant exploration is unfinished. |
| Trade, relationships and teaching | Basic version | One-for-one barter, positive trust and accepted/refused partnerships. Practical lessons are built but not offered in a normal game, because a learner needs a mentor who holds a work role ([#439](https://github.com/compoodment/ClankerWorld/issues/439) asks what lessons should teach). Pricing, currency, conflict and rich dialogue remain unfinished. |
| Parenthood, life stages and death | Basic version | Consent/preparation, infant care, child talk/play/help and age restrictions. Children without a selected model use safe local choices; parents do not yet bind that choice at birth. |
| Towns, household property and government | Basic version | First-Town membership/borders, building ownership, household stores and shared-food council. Multiple Town founding, broader law, currencies and land disputes remain unfinished. |

Life-stage thresholds are child at day 3, adult at day 15, elder at day 45 and
death by day 60 from birth. Added adults begin at a nonzero age. These are
playtest values, not settled population balance.

On death, a bounded final model choice can leave the estate to the household or
name one living recipient for the whole estate. Interrupted or invalid choices
use the household path. Per-item bequests, debts, minors and inheritance law
remain unfinished. Memories do not automatically pass to children.

Town borders and building-site ranking are provisional. Adults placed on an
owned building join its household; unclaimed Town placement joins the Town
without a household; outside a Town it starts an independent household. Finding
existing suitable housing before proposing a new House is not finished. Agents
without a household cannot build a House; in fresh worlds they rely on clothing
and natural storm cover until housing is resolved. New Shelters, Storehouses,
Cooking fires and Stone hearths are retired; standing ones in old saves remain.
The Weaving frame still supplies clothing while the intended Tailor Shop is
unfinished.

## Maps, weather and appearance

Small/Medium worlds have generated land, rivers, lakes, separate ground and
vegetation layers, resource sites and individual trees. Foot travel supports
diagonal steps, one-tile river crossings and slower mountain travel. Peaks are
impassable; mountains and peaks cannot hold construction. Diagonal Roads and
bridges remain unfinished.

Resources can deplete or regrow. Wood trees have mature, stump and sapling stages;
fruit trees have fruiting, picked and growing stages. Replanting currently uses a
generic seed at an existing depleted wood-tree site. Species-specific seeds,
new-tile planting and full managed orchards are unfinished.

Weather varies by region and affects local survival and crops. Recent rain gives
a modest soil-moisture estimate. The map shows rain, snow, storms and optional
haze/flashes. Drifting visual edges do not mean weather fronts actually move
between regions yet.

Ground, buildings, agents and natural objects use provisional code-drawn pixel
art. The interface uses wooden frames and parchment panels, with pixel fonts:
Fusion Pixel for body text and Timber capitals for headings. The Main Menu shows
the ClankerWorld logo over an animated pixel-art valley that follows the Light
or Dark theme. None of
this is a finished production art catalogue. Mineral/clay sites exist; some
materials still lack a complete production chain.

## Saves, keys and inventions

Worlds keep their own saves, model assignments and autosave settings. API keys
and pairing belong to the installation. The Windows host protects stored keys
for the current Windows user; Unix hosts use private file permissions. Damaged
or wrong-user key data is preserved. Moving installations may require re-entry.

If a world tick fails, the server holds the world paused. A failed active
checkpoint write can retry while paused, but other faults require operator
inspection. The detailed recovery screen is unfinished; preserve unsaved
in-memory progress before restarting the server.

Named saves can be overwritten after choosing one and confirming. Unusable list
metadata is isolated so sound saves remain reachable. Broader cross-release/mod
compatibility and history retention remain design questions. Technical rules
are in [saves and replay](development/saves-and-replay.md).

The Mod Library lists the current world's recorded packages read-only. Previously recorded
data-only building proposals and host approval rules remain, but agents no
longer create the retired shelter, storehouse or hearth proposals. General invention,
player review controls, personal libraries and mod import/export are unfinished.
Asset-validation and rights tools are built but not connected to a complete
creator experience. Arbitrary generated scripts are disabled; no sandbox has
been chosen. Multiplayer and public worlds are outside the current plan.

## Work and testing still to do

[Issues](https://github.com/compoodment/ClankerWorld/issues) holds bugs, work,
experiments and open decisions. [The Windows and paired-world checklist](https://github.com/compoodment/ClankerWorld/issues/285)
records tests still needed for source fixes. Code, tests and exports are evidence
for the build; an export alone is not a Windows playtest or proof of the running
server. This documentation pass did not inspect that server.

New World includes relative Advanced terrain controls and a resettable 50%-water
preset for Small and Medium. All settings feed the matching preview and saved
generation options. See [Advanced New World settings](playing.md#advanced-new-world-settings);
Windows interaction and real preview latency still need playtesting.

## Permanent deletion

The save list offers confirmed deletion of one snapshot; Load World offers
separate deletion of an inactive world and all its saves. Other worlds, provider
credentials and pairing remain unchanged. Interrupted deletion stays hidden
from loading and is retried on host startup. Native Windows interaction still
needs playtesting. See [save controls](playing.md#save-and-return).
