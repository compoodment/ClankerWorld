---
title: Planned game content
type: game-design-content
status: active
updated: 2026-09-30
---

# Planned game content

This is the **agreed base-content list for the intended finished game**,
including things not in the prototype. Computment accepted the initial draft's
unmentioned entries and corrected the entries called out below. The
[game design](README.md) records the owner's decisions; this
numbered list is its detailed content list. The
[current feature summary](../what-works.md) describes what exists in the playable
prototype.
These numbers are review handles, not implementation IDs. Acceptance of an
asset family does **not** set its recipe, cost, storage capacity, art file or
development order unless stated explicitly. It is not a claim of implementation.

**Status key:** **Agreed** = accepted for the finished-game base roster (not
necessarily built yet). **Still to decide** = explicitly deferred or unresolved choice, not
accepted as a specific asset. “Late-development” means accepted but staged
later. The row description controls scope: acceptance of a family is
not acceptance of every possible variant or a detailed mechanic.

**Asset** means a player-visible world sprite/animation, inventory icon,
interface graphic, text/content template, or visual effect. A crop can need a
plant sprite, harvested-item icon, and cooked-food icon. A House needs exterior
footprint art and an inspection panel, **not** an enterable room interior.
One standard appearance per supported design is enough; do not multiply every
building by arbitrary roofs, colors, materials, and states. Agent appearance is
the intended exception. Variants for direction, growth, damage or weather are
functional states, not cosmetic skins. Straight top-down is the chosen
perspective; no parallel isometric art set is planned.

## 1. Ground, water, and climate

| # | Status | Base asset family | Accepted visible set and purpose |
| --- | --- | --- | --- |
| 1.1 | Agreed | Ground surfaces | Meadow/grass, forest floor, sand/beach, dry scrub, rocky upland, snow/tundra, and fertile/cultivated soil. Climate, elevation, surface and vegetation remain separate data, not one tile type per combination. |
| 1.2 | Agreed | Water | Ocean, lake, continuous river, shoreline/water edge; shallow one-tile river crossing distinct enough to read. Depth/flow variants only if they affect crossing rules. |
| 1.3 | Agreed | Elevation | Mountain and peak terrain silhouettes/edges; neither allows construction. Mountains are passable more slowly; peaks cannot be traversed. Exact slow-down is open. |
| 1.4 | Agreed | Ground transitions | Coasts, river banks/corners, grass–sand, grass–snow, and rocky boundaries. On worlds with optional east/west wrapping, terrain and rivers continue correctly across the map's right/left edge; this is a map rule, not a separate texture family. |
| 1.5 | Agreed | Seasons and weather | Spring/summer/autumn/winter palette treatment; clouds, rain, snow and a severe storm effect. Weather is regional. Night affects temperature/weather, not sleep, travel, visibility, work or social rules; a cosmetic night tint is accepted without a visibility penalty. |
| 1.6 | Agreed | Surface texture variation | Roughly two subtle textures per terrain type—e.g. clean grass and grass with small tufts/stones. Little leaves, stones and similar details live mainly inside the texture, not as many individually placed objects. Shoreline effects/edges may need their own pieces. |

The surface list is a **small art vocabulary**, not a rigid list of
natural biomes. Forests and mountain regions should visibly appear in default
worlds. Grass, stone and snow should suit their generated climate; cacti were
previously an example but are now disfavored, pending a final exclusion.
Forest-floor art should correspond to a visible tree or plant, mostly trees;
grass-surfaced forest art can carry scattered trees. Hills at mountain bases
are a preferred addition, with exact terrain rules still open. Sand need not
outline every coast or river. The current grain, striped transitions and
large circular weather overlays are rejected, not accepted asset references.

## 2. Harvestable plants, deposits, and farm visuals

| # | Status | World object / stages | Harvested item or role |
| --- | --- | --- | --- |
| 2.1 | Agreed | Broadleaf tree: sapling → mature tree → stump → regrowth | Wood; plantable tree seed. **At most one tree per tile**, including its visible sprite. |
| 2.2 | Agreed | Conifer tree, same functional stages | Wood; same functional tree-seed category unless species difference proves useful. **At most one tree per tile.** |
| 2.3 | Agreed | Berry bush: fruiting, picked, regrowing | Berries as wild food. |
| 2.4 | Agreed | Wild edible greens patch: growing, picked | Greens as a second forage source. |
| 2.5 | Agreed | Fiber plants/reeds: growing, harvested | Plant fiber for cloth, rope and basic crafting. |
| 2.6 | Agreed | Stone outcrop: intact, depleted | Stone; finite deposit. |
| 2.7.1 | Agreed | Iron-bearing outcrop: intact, depleted | An above-ground rock with **visible iron material in it**, not a ground vein; yields iron ore. Distinct world sprite and item icon. |
| 2.7.2 | Agreed | Gold-bearing outcrop: intact, depleted | An above-ground rock with **visible gold material in it**, distinct from ordinary stone and iron outcrops. Distinct world sprite and item icon. |
| 2.7.3 | Agreed | Diamond-bearing outcrop: intact, depleted | An above-ground rock with **visible diamond material in it**, distinct from ordinary stone/other ore outcrops. Distinct world sprite and item icon. |
| 2.8 | Agreed | Clay bank | Clay for the accepted pottery family; exact pottery uses and recipes open. |
| 2.9 | Agreed | Universal-grain field: prepared soil, seeded, sprout, mature, harvested | **Grain**, with no species-specific wheat/barley/etc. item; grain seed. |
| 2.10 | Agreed | Potato field: same functional stages | **Potatoes** and potato planting stock; not generic roots or carrots. |
| 2.11 | Agreed | Cultivated leafy-green field: same functional stages | Farmed greens, a **different, more filling food** than wild greens. |
| 2.12 | Agreed | Orchard fruit tree: growing, fruiting, picked | Fruit; the orchard tree also obeys **one tree per tile**. Species, yield and seasonality open. |

The accepted compact base-food set is berries, wild greens, universal grain,
potatoes, cultivated greens and orchard fruit. It avoids dozens of crops that
would differ only in icon. Crop yield, seasonality, seed rules, soil moisture,
spoilage and farm count still need design.

## 3. Raw materials and processed goods

| # | Status | Carryable item/icon | Source → use |
| --- | --- | --- | --- |
| 3.1 | Agreed | Wood | Trees → tools, buildings, cooking fuel and transport. Whether logs/planks are separate items is open. |
| 3.2 | Agreed | Stone | Outcrops → stone tools and construction. |
| 3.3 | Agreed | Iron ore / refined iron | Iron outcrop → separate ore item → Blacksmith refines it into a separate usable metal item → tools and other crafts. Selling refined metal to others is proposed and awaiting final confirmation. |
| 3.4 | Agreed | Gold / diamond | Named rarer materials. No assumed mining tier, currency role or automatic weapon upgrade. |
| 3.5 | Agreed | Plant fiber / cloth | Fiber plants → the Tailor Shop turns plant fiber into a real **cloth intermediate** → the Tailor Shop turns cloth into clothing; cloth can be held and traded as its own stock. Recipe numbers are provisional (see 6.8). |
| 3.6 | Agreed | Seeds | Tree seed, universal-grain seed, potato planting stock and cultivated-green seed. Exact item split and orchard propagation remain open; the prototype's generic `seed` need not remain universal. |
| 3.7 | Agreed | Rope | Fiber → rope for boat/building/transport use; exact recipe and function open. |
| 3.8 | Agreed | Clay / pottery | Clay bank → pottery; storage vessel/trade-good roles and recipe open. Not a required technological age. |
| 3.9 | Agreed | Leather / wool | Accepted animal products, staged with **late-development** husbandry; no starting livestock or early supply is implied. Leather source without slaughter remains open. |

Use a **specific icon for each actual item**, including a generic fallback for
valid agent-invented materials. Do not commit to copper, bronze, coal, steel or
an endless ore ladder merely because they are conventional crafting-game items.

## 4. Tools, work gear, and starter package

| # | Status | Item family | Jobs and representation |
| --- | --- | --- | --- |
| 4.1 | Agreed | Axe | Wood gathering and tree work; wooden starter version, then stone and iron versions if they meaningfully improve work. Can also be a weapon under the combat design. |
| 4.2 | Agreed | Pickaxe | Stone and ore extraction; wood → stone → iron progression. Gate advanced deposits by actual tool capability, not just icon color. |
| 4.3 | Agreed | Hoe | Preparing and tending farm fields; wooden and iron versions only if both have meaningful differences. |
| 4.4 | Agreed | Hammer | Building and Blacksmith work; share a basic form where possible rather than multiplying tiers. |
| 4.5 | Agreed | Sickle | Crop harvest; exact advantage over hand harvesting open. |
| 4.6 | Agreed | Knife | Food preparation and general craft; optional dual-use combat item. |
| 4.7 | Agreed | Everyday clothing | Basic, cold-protective and wet-protective clothing are gameplay equipment. Agents **keep the same map sprite appearance regardless of worn clothing**; separate worn-outfit sprite sets are not needed. Clothing has item icons/effects. |
| 4.8 | Agreed | Carry aid | Basket/sack/handcart family for physical stock transport; exact members, capacities and whether a cart is a distinct vehicle remain open. |

**First-Town guaranteed start:** two Houses, a Warehouse, a Farmhouse and a
Blacksmith, plus **eight food portions in each House** and **at least one
usable wooden axe and one usable wooden pickaxe** in the communal Warehouse.
The game automatically assigns the Farmhouse and Blacksmith to the two
starting households, one productive building each. Optional extra supplies,
later ownership transfers and replacement recipes are **open**. The Blacksmith can
support the bootstrap; the Workshop is not required at start.

## 5. Food, cooking, and care

| # | Status | Carryable item / visual | Proposed source and role |
| --- | --- | --- | --- |
| 5.1 | Agreed | Wild berries; wild greens | Gathered food; distinct fresh-food icons. |
| 5.2 | Agreed | Raw universal grain; potatoes; cultivated greens; orchard fruit | Harvested farm foods, each with its own item/icon; farmed greens satisfy more hunger than wild greens. |
| 5.3 | Agreed | Flour | Farmhouse-processed grain; real intermediate that can be sold directly, including at the Market. |
| 5.4 | Agreed | Simple cooked meal | House-cooked from available food; a generic meal icon avoids requiring a bespoke recipe for every ingredient mix. |
| 5.5 | Agreed | Porridge; bread; vegetable stew | Three concrete advanced foods to give grain/vegetables and Farmhouse/Restaurant distinctive uses. Exact ingredients, nutrition and quality are open. |
| 5.6 | Agreed | Restaurant meal | Prepared and sold on site, potentially better than a basic House meal. A plated-meal icon is enough until menu variety matters. |
| 5.7 | Agreed | Milk; eggs | Products of accepted cows/chickens, **late-development** with livestock. No meat or hunting loop is assumed. |
| 5.8 | Agreed | Bandage; medicine | Two accepted care goods with distinct icons. Ingredients, healing/care rules and whether herbs are a separate item remain open. |

Food is physically stored in **Houses** or a business's own stock, not the
Town's resource Warehouse. Stores cannot sell food from a remote House. The
food roster should support daily life without forcing agents into constant
meal production; no calorie/energy meter is proposed here.

## 6. Buildings, fields, and infrastructure

Each row needs one exterior per **supported footprint**, a tiny map/overview
symbol, construction/repair states only where those states actually exist,
and an inspection-panel identity/icon. There are **no room-interior assets**.

| # | Status | Building / footprint | Finished-game function and art |
| --- | --- | --- | --- |
| 6.1 | Agreed | House: 1×1, 1×2, 2×2 | Three readable exterior footprints; private household food/resources, cooking and storm refuge. Invited guests may shelter from storms but get no access to the House's stock or cooking. Expansion raises storage, **not** occupancy. No beds or sleeping props required. |
| 6.2 | Agreed | Warehouse: 2×2, 2×3 | Two exterior footprints; physical communal **non-food** resource stock for Town residents only, checked when they pick something up. What someone already carries stays theirs. Any Town resident may plan its expansion once its stock is nearly full. |
| 6.3 | Agreed | Store: 1×1, 1×2 | Two exterior footprints; **optional** household shop, held by the household that builds it (a Farmhouse or Tailor Shop household may build one), with goods physically delivered and stocked there before sale. |
| 6.4 | Agreed | Farmhouse: 1×1 or 1×2 | Crop processing and farm-household activity; one of the guaranteed first-Town productive buildings, held by a starting household. Household-held, **not communal**: the holding household works there and sells directly from it, and any adult resident of that household may use it. A Farmhouse household may also build a Silo and an optional Store. |
| 6.5 | Agreed | Farm fields | Prepared/seeded/growing/ready/harvested states for each accepted crop, not visible building interiors. |
| 6.6 | Agreed | Private farm Silo: 1×1 | Separate private farm-work stock **next to the Farmhouse**, which the Farmhouse household may build; exact adjacency/placement rule and storage capacity open. |
| 6.7 | Agreed | Blacksmith: 1×2 or 2×2 | Guaranteed first-Town building, held by a household. Household-held, **not communal**: any adult resident of the holding household may use it. Refines ore and makes tools; sells tools and accepts tool-making requests **at the Blacksmith** with on-site stock—no separate Store needed. Sale of refined metal remains proposed. |
| 6.8 | Agreed | Tailor Shop: 1×1 or 2×2 | Household-held clothing business. It turns plant fiber into cloth and cloth into clothing; cloth is a real tradable item (see 3.5). A Tailor Shop household may build an optional Store. It replaces the Weaving frame and its "Woven clothing" outright, with no legacy transition. Recipe numbers, costs and work time are **Leaning toward**: provisional, tuned in playtests. The holding household sells clothing directly from the building and does not need a Store. Any tool recipes remain open. |
| 6.9 | Agreed | Workshop: 2×2 | Inventions/mods and work access for outsiders; communal, held by the Town. **Late-development mechanics/code**, after the base simulation and assets are fuller. Not guaranteed at the start. The one shared building a household may plan; the Town holds it once built. |
| 6.10 | Agreed | Market: 2×2; stalls: 1×1 | Main building plus separate stall assets. Reserve an approximately **10×12 clear plot** for stalls to spawn; rules for reservation, stall ownership and filling it are open. Trading admits any Town's agents. |
| 6.11 | Agreed | Town Hall: 3×4 | Civic governance, laws and elections. |
| 6.12 | Agreed | Port: 2×4, four rotations | One tile of its four-tile length on land, three over water; keep open docking space on both long sides of the three-tile water section so boats can dock. Exact clearance/approach rule open. |
| 6.13 | Agreed | Restaurant: 1×2 or 2×2 | Optional agent-founded food business with on-site ingredients and meals; no required starter Restaurant. |
| 6.14 | Agreed | Clinic / healer's shop: 1×1 or 1×2 | Accepted care business for illness/injury treatment. Exact name, recipes and care actions open. |
| 6.15 | Agreed | Road | **One** road type; straight, corner, junction, end, diagonal and connection pieces as needed for readable automatic routes. Roads remain after their building or Town disappears. Roads cost no materials. |
| 6.16 | Agreed | Bridge | Narrow river crossing sprite(s) for a river up to **two tiles wide**; wider water is not bridged. Generated from traffic **or immediately as part of a generated Road** crossing a bridgeable river, never hand-painted by player. Costs no materials. Whether a bridge is redundant compares the actual connected banks, with no fixed radius; separate nearby streams may each need a bridge. |

Port footprint interpretation, shown in one of its four orientations (`P` is
part of the 2×4 Port, `·` is clear docking water beside it):

```text
shore:       P P   ← 1 row on land
water:     · P P ·
water:     · P P ·
water:     · P P · ← 3 rows over water, side docking clearance
```

The clear `·` cells are **not** extra footprint tiles. Their exact number and
approach/occupancy rules are still to be designed.

**Do not include as base buildings:** Shelter, Storehouse, separate Cooking
fire/Campfire, Stone hearth, Weaving frame, bed/bedroll, or the player-facing
Create workbench. No village/city visual tiers are implied; all places are
**Towns**. Building costs, storage capacities, some footprints and material
alternatives are not fixed by this roster.

## 7. Agents, animals, transport, and combat

| # | Status | Visible assets | Intended minimum visual behavior |
| --- | --- | --- | --- |
| 7.1 | Agreed | Agent sprites | **Straight top-down**, showing the tops of heads/bodies/objects; infant, child, adult and elder silhouettes and a few appearance variants per stage without genders. Directional walking, working, carrying, talking, eating/cooking and hurt states as needed. Worn clothing does not alter the sprite; worn-armor display is open. No sleep cycle. Exact frames open. |
| 7.2 | Agreed | Social indicators | Chat bubble, selected/hover state, household/Town affiliation and conversation popup. Family lines, partnerships and deceased profiles belong in the family-tree UI, not automatically on the ground sprite. |
| 7.3 | Agreed | Boat | One small crafted boat and an occupied/cargo state for water travel through ports; improved craft can be agent inventions. Exact water/boat rules open. |
| 7.4 | Agreed | Livestock | **Chicken (eggs), sheep (wool), cow (milk)**; husbandry art/mechanics are **late-development**. No hostile predators. |
| 7.5 | Agreed | Mount | **Horse** with rider/cargo states; riding and care mechanics are **late-development**. |
| 7.6 | Agreed | Weapon | Axe is also a work tool; **spear and simple sword** are accepted additional combat items. Materials, crafting and lethality remain open. |
| 7.7 | Agreed | Protection | **Shield and basic armor** accepted. Armor has item art and gameplay effects; prompting new armor art is the intended later direction, not a solved asset pipeline. Whether worn armor also changes the agent's map sprite remains open. |
| 7.8 | Agreed | Injury and recovery | Hurt/treated indicators; accepted **bandage and medicine** items. Separate herbs remain open. No hostile predator sprites in the current plan. |

Animal slaughter, hunting, cavalry/war specialization, and named armor tiers
are **not** assumed. Combat exists for interpersonal self-defense, crime, feuds
and war; it is not a reason to add hostile wildlife by default.

## 8. Knowledge, law, money, and invented content

| # | Status | Asset family | Intended representation |
| --- | --- | --- | --- |
| 8.1 | Agreed | Map; written record; book | Carryable/readable knowledge goods to record discoveries and trade knowledge. Three icons; contents and who actually knows them are data, not art. |
| 8.2 | Agreed | Law and property record | **Mostly data/gameplay**: inspectable Town law, claim, ownership, agreement and inheritance/will screens. Reuse document UI where useful; separate physical item art is not required for each. |
| 8.3 | Agreed | Currency design | A coin/token/note visual family for **agent-created currencies**. No universal starting money or preselected gold standard. |
| 8.4 | Agreed | Agent invention/mod package | **Late-development** mechanics/art: icon/thumbnail, author, validation/failed/active state, dependency/rights info; world and personal-library views. Exact package/art workflow is deferred until the base simulation and asset set are fuller. |
| 8.5 | Still to decide | Generic invention-art fallback | Whether/how a mechanically valid invention receives a placeholder silhouette when final art is unavailable is **deferred to late invention development**; no particular fallback sprite is approved yet. |

The base roster is finite; **agent inventions are intentionally not enumerable**.
They may add buildings, tools, crops, machines, art, laws, currencies and more,
subject to valid gameplay and visual representation. Do not interpret the base
list as a cap on invention, or fabricate every possible invented sprite now.

## 9. Interface and presentation assets

| # | Status | Visual family | Must cover |
| --- | --- | --- | --- |
| 9.1 | Agreed | Main Menu and New World | Background, buttons for Continue/New World/Load World/Settings/Mod Library/Quit Game; seed preview, size/climate/advanced controls, highlighted suitable first-Town areas with free rough-site choice and automatic layout, four-agent setup and Start World progress. |
| 9.2 | Agreed | World HUD | Top-bar Map, pause/resume, date/time, population, World Info, Filters, Event Log, Add Agent and Pause Menu; tile/agent hover/selection markers. |
| 9.3 | Agreed | Overview and filters | Map frame/camera rectangle; biome/terrain and local weather legend; established Town/household boundaries, property and similar fact overlays. No player fog-of-war texture is needed. |
| 9.4 | Agreed | Inspection panels | Tile facts; agent profile, private thoughts, Memories, inventory, relationships, model/provider controls, deceased profile and Family Tree; building occupants/stock/ownership. |
| 9.5 | Agreed | Economy/work | Item icons used consistently in inventories, stock panels, farm/work progress, Store/Market/Restaurant trade, and offers. Actual physical location and ownership must be visible. |
| 9.6 | Agreed | Events and conversation | Event Log location markers, chat bubble, conversation summary/full view, decisions, law/election and invention notices inside their relevant panels. No optional out-of-view pop-up notice system. |
| 9.7 | Agreed | Pause, settings, save, mods | Compact Pause Menu: Save World, Settings, Mod Library, **Quit to Menu last**; no Quit Game there. Game/World Settings, confirmation dialogs, save compatibility, AI-usage meter/limit and mod states. |
| 9.8 | Agreed | Typography and common UI kit | One readable pixel-compatible font system, panel frames, 9-slice pieces, buttons, focus/disabled/error states, tooltips, scrollbars and icon legend. The Timber & Parchment look with Light and Dark themes is agreed in the game design; the font and remaining accessibility treatment are open. |

All important information remains readable in **text**. Generated voice,
music, ambient sound and sound effects are **outside the first complete game's
scope**; this roster therefore has no audio production list.

## 10. Cross-cutting visual states and production rules

- **World objects:** normal/depleted/regrowing only where the world actually
  tracks those states. Seasons and weather should use a shared treatment, not
  a full duplicate sprite for every climate × season × time combination.
- **Items:** one inventory icon per distinct item; ground/carry representation
  only when that item can actually appear there. No invented item has a free
  stock transfer: goods must be carried to Store stock before sale.
- **Buildings:** support the decided footprint sizes exactly. Add construction,
  expanded, damaged or abandoned states only if the simulation can produce
  them; do not multiply every building by speculative decorative variants.
- **Scale/style:** 32×32 logical ground tiles, PNG runtime assets and **straight
  top-down** view are the decided direction; trees and multi-tile buildings can
  span anchored transparent images. Palette, character sprite size, animation
  count and a reference scene remain open. Isometric is only a possible future
  visual experiment, not the production target.
- **UI/icon parity:** an asset's name, icon and inspectable facts should refer
  to the same actual item or object. Pixel art does not make a fixture-only
  capability playable.

## Remaining design details after roster acceptance

The base entries are accepted, including the formerly proposed crop/food set,
clay/pottery, rope, orchard, carry aid, species, weapons, armor and Clinic.
Still open: **(a)** starter food/tool counts beyond the decided minima and
replacement recipes;
**(b)** whether Blacksmiths sell refined metal to other crafters; **(c)**
recipes, production sites, item balance and storage capacities; **(d)** the
Port's precise clearance and Market stall-reservation behavior; **(e)**
animal-care, injury and combat mechanics; and **(f)** late-development
invention/mod art generation and fallback rules.

The next pass can give each accepted item/building a production row:
**source or recipe, tool gate, work site, use, location/ownership, trade role,
icon/world sprite, functional states, and current implementation status**.
Those values are intentionally not fabricated in this first visual pass.
