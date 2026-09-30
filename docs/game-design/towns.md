---
title: Towns, buildings and government
type: game-design
status: active
updated: 2026-09-29
---

# Towns, buildings and government

These are the intended game rules and choices. They do not describe
everything that is available in the current build. See [what works today](../what-works.md).

[Game-design guide](README.md) explains the agreement labels.

## On this page

- [Buildings, land, Towns, and animals](#buildings-land-towns-and-animals)
- [Town laws and governance](#town-laws-and-governance)
- [Questions linking these systems](#questions-linking-these-systems)

## Buildings, land, Towns, and animals

### Agreed

- Each building **type has a few supported shapes of its own**, rather than
  choosing freely from every shape/material/floor/state combination. There is
  initially **one standard appearance per supported design**; duplicate houses
  of the same type and size may look alike. The decided footprints are listed
  below and in the [accepted asset roster](content-list.md). Agents
  may invent new designs, including unusual shapes if the art and world rules
  can represent them; larger, irregular, or multi-floor buildings are therefore
  possibilities, not automatic unlocks. **House** replaces Shelter as the
  residential building. It has household-exclusive storage/inventory, usable
  by household members while physically inside; food is kept here. It provides
  cooking, storm refuge, childcare and household property functions. Agents
  outside the household may enter **when invited**; invitation does not grant
  access to the household's private inventory. **There is no fixed occupant
  limit for a House**, including invited visitors; prior capacity and crowding
  ideas are superseded. It has no bed or sleep-recovery role. A selected
  building exposes inspectable occupants, stock and ownership in a panel;
  there are **no visible/enterable room interiors**.
- **Storage-driven building expansion:** a House starts at **1×1** and can
  expand to **1×2** or **2×2** when its household needs more storage.
  This increases storage, not its unlimited occupant count. A Warehouse starts
  at **2×2** and can expand to **2×3** for more storage. A Store may be **1×1**
  or **1×2**, likewise tied to storage. Exact capacity per footprint, costs,
  expansion triggers and other building footprints belong in the full content
  catalogue; do not invent those values yet.
- Non-residential buildings have distinct physical occupancy, workstation,
  storage, and other type-specific limits. Agents can reserve space when
  practical, queue or choose alternatives when full, and retain the blocked
  goal for later retry. The simulation owns that memory; Jev can help choose an
  alternative but is not responsible for remembering the task.
- Building sites should come from understandable legal options considering
  access, terrain, resources, land-use rights, and Town context. A Town holds
  title to its formally claimed land; households can receive recorded rights
  to use particular sites without owning the land itself. Land rights can be
  disputed; sharing and grant terms still need definition. Monetary land
  values and purchase prices become meaningful after currencies exist. Agents
  can later buy/sell transferable property or rights through valid processes.
  Roads help travel and influence site choice.
- The world system must validate hard physical building constraints such as
  terrain, footprint and overlap with existing objects/resources/buildings.
  Access, ownership/claims and Town context also matter, without turning
  every agent-created law into an unbreakable physical rule. Agents request a
  building; Town planning ranks legal sites; agents accept or reject rather
  than choosing tiles.
- **Housing priority for a newly added adult (decided):** when the adult has no
  home, seek suitable existing household housing first; start a new House
  project only if none is suitable. How they join an existing household and
  gain its permission, or form a new household, remains open. There is no
  occupancy-capacity gate.
- **Town(s)** replaces “settlement” in player-facing terminology. There are
  no village or city place classes: every such place is a Town. The first Town
  already exists during paused New World setup, and its four starting agents
  are Town residents with access to its Warehouse. Towns have generated,
  inspectable borders with room to grow. The border follows the
  Town's assigned buildings, includes spare space around them, and expands
  when new buildings join that Town; exact margin, connected geometry,
  assignment and overlap handling remain open.
  When its last resident leaves or dies, the Town becomes **abandoned**, not
  erased. Its identity, buildings, border and infrastructure remain in the
  world, and later residents can revive that same Town. Exact treatment of
  abandoned stock and claims still needs its own decision.
- **Warehouse** replaces Storehouse. It holds actual inspectable resource stock
  at its location for agents/households resident in its Town, within that
  Town's borders. Food belongs at home instead. Residency and border-change
  access cases remain open.
- **Workshop** remains for agent inventions/mods and is usable by outsiders.
  Its footprint is **2×2**. Its mechanic/code is a **late-development phase**:
  first establish a functioning simulation and broader base asset set, then
  build the invention/mod loop on that foundation. This is staging, not removal
  from the intended finished game. Remove separate Cooking fire/Campfire and
  Stone hearth buildings; House handles cooking. Remove Weaving frame and
  bed/bedroll content. A dedicated **Tailor Shop** makes clothing at **1×1 or
  2×2**; its exact production chain remains open.
- **Roads and bridges:** the world system generates infrastructure, not the
  player or individual agent. The full agreed road rule is below; the starter
  Path becomes a Road in intended content.
- **Livestock and mounts** belong in the finished game. Hostile predators do
  **not** belong in the current plan, though they may be revisited later.
  The accepted base roster is **chickens (eggs), sheep (wool), cows (milk), and
  horses (mounts)**. Leather/wool and milk/egg products are late-development
  items alongside animal husbandry. Their detailed mechanics are deliberately
  deferred to late development; slaughter/hunting is not assumed.

### Agreed content and building sizes

Computment accepted all unmentioned entries of the initial
[asset-roster review](content-list.md) and amended specific rows
as recorded here. The roster's numbered entries are the detailed accepted
**base content**; open recipes, costs, capacities, animation budgets and
invented content are not silently approved. In particular:

- Terrain uses roughly two subtle textures per surface, with most little
  grass/stone/leaf details baked into texture variation. A tile has at most
  one tree. Iron, gold and diamond deposits are visibly distinct
  **ore-bearing outcrops**, not ground veins.
- The farm roster includes **universal grain** (no named grain species),
  **potatoes**, a cultivated leafy green distinct from wild greens that
  satisfies more hunger, and orchard fruit. Flour is a sellable Farmhouse
  intermediate, including at a Market; cloth is likewise a real intermediate
  item.
- The Blacksmith refines **iron ore into a separate metal item**, then uses it to
  make tools. Selling spare refined metal directly from the Blacksmith is
  a strong proposed extension awaiting final confirmation. The Blacksmith
  already sells tools directly and takes tool-making requests—no separate
  Store is required for its own products.
- The accepted medical goods include **bandages and medicine**. Exact
  ingredients, healing effects, care actions and supply chain are open.
- Farmhouse **1×1 or 1×2**; adjacent private Silo **1×1**; Blacksmith
  **1×2 or 2×2**; Tailor Shop **1×1 or 2×2**; Workshop **2×2**;
  Restaurant **1×2 or 2×2**; Clinic/healer's shop **1×1 or 1×2**.
  Market's main building is **2×2** with separate **1×1 stalls** and an
  approximately **10×12 clear stall-reservation area**. Town Hall is
  **3×4**. These are building/plot footprints, not interior rooms.
- Port is **2×4**, rotatable to all four cardinal directions. One tile of its
  four-tile length rests on land; three extend over water. Keep clear docking
  space along both long sides of that three-tile water section. A legal land
  approach and clear docking space are required. Boats are communal Town
  property, usable by residents or visitors with permission; the first
  [boat journey](world.md#first-boat-and-port-travel) needs a completed Port at
  each end. Exact queue and construction-cost rules remain open.
- Combat gear includes a spear, sword, shield and armor alongside the
  dual-purpose axe. Clothing does not change an agent's map appearance; the
  visual treatment of worn armor remains open.
  Maps, written records and books are accepted knowledge goods; law, claims,
  ownership and wills are mainly inspectable **data/gameplay**, not a demand
  for separate physical document objects.

The accepted roster includes proposed-but-now-chosen families such as clay,
pottery, rope, orchard fruit and carry aids. Their exact gameplay value and
recipe are still to be worked out, not grounds to relabel them unaccepted.

### Choosing building sites

Agents identify a building need and do the work. The Town layout system offers
**several ranked viable sites** rather than letting an agent search every tile.
The agent can accept or reject; after rejection, they explain what was wrong
and the layout system re-ranks sites. The retry/stop limit and what happens if
no acceptable legal site exists remain open. Site ranking should account for
terrain, resources, existing buildings, ownership, access, other Towns, room
for growth and building purpose. Town appearance/layout should vary by
Town/culture. Exact weights and when another offer appears are open.

The intended building roles now include House, Warehouse, Workshop,
Farmhouse, farm fields, an adjacent private farm Silo, household-run Store,
household-run Blacksmith with internal work stock and direct sales, Tailor Shop,
public Market and stalls, Town Hall, Port, Clinic, and optional agent-founded
Restaurant. A
Farmhouse processes crops; its owning household places fertile fields, plants
seeds, tends, harvests, and sells/trades the produce. Farm count responds to
**Town population and farm yields**; a shortage or reduced yield can justify
more farming rather than a hard cap blocking recovery. The exact formula is
open. Farm work stock
is private to its household; its Silo is distinct from the public Town
Warehouse. The Blacksmith makes and sells tools on site and can accept
specific tool-making requests, with stock inside the building. A Store
supports a household selling its products. Goods must be physically carried
to the Store and kept in its own stock before sale; a Store cannot sell from
a remote House, farm, or Warehouse inventory. A Market admits traders from any
Town; a Restaurant buys ingredients, cooks and sells potentially better meals.
Town Hall supports governance and Port supports boats. Exact stocks, recipes,
ownership edge cases, prices and trade remain open. See the
[accepted asset roster](content-list.md) for specific content and
[current state](../what-works.md) for what exists in the prototype.

Wood, stone, iron, gold, diamond and many other crafting resources are wanted.
Better tools should gate harvesting/mining more advanced resources, creating
a progression incentive. The agreed initial ladder is **wood tools → stone →
stone tools → iron → iron tools → rarer materials**. Gold, diamond and other
materials belong in the wider catalogue; their exact mining/crafting tiers
remain open. Wood and stone should both be useful for House construction;
specific costs are open.

**Clanker's still-open implementation proposal:** use one incremental layout
service for starter arrangement and later construction requests. Filter
impossible footprints, rank sites by purpose/access/terrain/resources/Town
shape/future growth, and generate a connected Road layout with each accepted
building. The generator should not preselect a fixed lifetime building count.

### Town land and household use rights

The **Town holds formal title** to land it claims; households hold inspectable
**use rights** for homes, farms and businesses rather than household land title.
The Town border alone does not silently take over another claim or transfer a
building, its stock or a household's private goods. Physical occupation or
model text cannot rewrite these records. A later mayor, once that role exists,
reviews land rights; how that office is created and what changes it may make
remain open. The first Town begins with a council, not an assumed mayor.

Competing requests are visible as **pending disputes**. While one is pending,
conflicting formal rights transfers pause; residents are not evicted, goods
are not seized and ordinary physical movement is not stopped by the dispute
record. Resolution needs an adjudicator authorized by Town law, with the
authority, evidence and exact change recorded. With no such law or adjudicator,
the dispute remains pending; there is no automatic first-claimer winner.
Plot boundaries, ordinary shared-use grants, expiry and transfer procedures,
and the future mayor's precise powers still need decisions.

### How Roads and bridges appear

The baseline has **one Road type**, with no extra categories required yet.
Roads appear immediately with Town buildings and automatically link Towns
**when a legal land route exists**. If geography blocks a legal route, the
Towns remain unconnected by Road; boats or later transport may still connect
their travelers. Roads are not generated by footsteps or a separate building
project. Agents and player do not paint Road tiles. Roads speed up travel.
Roads connect to adjacent building entrances and must not occupy a building's
footprint. The player's September 29 sketch indicates a connected spine with
short branches as a useful layout direction, not a mandatory fixed street map.
**Bridge placement responds to traffic** at narrow river crossings, even
without a planned Road. A Road generation pass also builds a bridge immediately
if its legal route encounters a bridgeable river. Either case excludes a
redundant nearby bridge over the same crossing/river, not a necessary bridge
over a different nearby stream.
For traffic-created bridges, the initial playtest threshold is **six completed
crossings by at least two distinct agents within two world-days** at the same
legal narrow crossing. Only actual traversal counts, not route previews,
failed attempts or waiting. Keep bounded crossing evidence across saves; the
threshold can be tuned after playtesting. Generated-Road bridges need not wait
for this traffic.

**Roads and bridges remain permanently** once built. They do not decay or
disappear automatically when traffic stops, a building is removed or a Town is
abandoned. There is one Road type; no temporary-versus-permanent class is
needed. Exact inter-Town route timing, layout, bridge spacing/materials and
rendering remain open. Diagonal travel/Roads remain in scope; diagonal moves
must not pass through blocked corners. Playable foot movement now uses the strict
  two-clear-shoulder rule and a 141% diagonal route cost; diagonal Road
  construction/visuals remain open.

### Still to decide

Further structure effects; exact configurations and unchosen footprints;
building
inspection fields, invitation and non-residential access/reservations/queues;
claim boundaries, shared-use grants and mayoral powers; Town borders and
governance; currency/land pricing;
transport progression; bridge spacing and materials, other terrain eligibility,
travel effects and junction/diagonal visuals; advanced resource/tool tiers,
farming workflow and farm-cap formula, private versus public stock, business
economics; and later livestock/wildlife detail.

## Town laws and governance

### Agreed

- Agents may establish Town laws and later change them. These are
  **in-world social rules**, not unbreakable physics. An agent can violate a
  rule—for example, cut a tree in a protected grove—and the world can record
  the act for discovery, dispute, and consequences. The game should not simply
  refuse every illegal action, because crime and enforcement belong to the
  simulation.
- The the game rules still protects fixed world facts and action
  rules: agents cannot create goods, erase physical constraints, or silently
  rewrite formal ownership by declaring a new law. Unlawful use or occupation
  can be represented as an action/dispute without automatically changing the
  underlying ownership record. Laws may guide or contest transfers through
  validated mechanisms, but cannot bypass those mechanisms.
- A newly founded Town starts with a **simple council** rather than a
  single starting agent automatically ruling everyone. **Every adult resident** sits
  on this initial council and can propose laws and vote. Towns may later
  change their governing arrangement through in-world decisions; the council
  is the starting form, not a universal permanent government.

### Election threshold to try

Once a Town reaches **eight adult residents**—double the four-agent
starting population—it begins electing a smaller representative council
instead of keeping every adult as a council member. Count adults in that
Town, not across the world. Eight is an initial threshold to playtest,
not a claim that every Town must forever follow this exact rule. All
adult residents should retain a vote in those elections.

### Still to decide

What qualifies an adult as a Town resident, elected council size,
election timing/terms, vote threshold/quorum, proposal/repeal procedure, and
how governments may change; which laws apply to whom and where; how a
violation is witnessed, investigated, enforced, or punished; specific land
rights grant and adjudication procedures;
taxes, inheritance, and interaction between conflicting Towns. Agents
should know only laws or violations they have learned about in-world.

## Questions linking these systems

These remain open; they are not new decisions.

- **Law-making and enforcement details.** The core distinction is settled:
   agent-created laws can be broken, while the simulation protects physical
   facts and validated ownership changes. Define how laws are adopted,
   discovered, enforced, and disputed—including conflicting inheritance rules
   and illegal occupation—without granting models authority over engine facts.

- **Starter economy and tool bootstrap.** The first Town guarantees two
   Houses, a Warehouse, Farmhouse and Blacksmith. Each House starts with eight
   food portions; the communal Warehouse holds at least one usable wooden axe
   and one usable wooden pickaxe. The Farmhouse and Blacksmith are assigned
   automatically to the two starting households, one each, without player
   selection. Decide optional extra starter supplies and how first-tier tools
   are made when the Blacksmith is unavailable.
   More farms may answer food shortages when
   yields fall; the population-and-yield planning rule remains open.

- **Automatic infrastructure details.** Towns connect by Road where a legal
   land route exists and can remain disconnected otherwise. Bridge spacing
   prevents redundant crossings on the same river but does not block a needed
   bridge on a separate nearby stream. A generated Road may bridge a legal
   crossing immediately. The trial traffic threshold is given above. Decide
   the exact bridge spacing and whether Roads or bridges consume materials;
   their permanence, including after building loss or Town abandonment, is
   already agreed.

- **Home invitations and later membership.** Add Agent placement on household
   land forcibly assigns starting membership, without a consent step. Guests
   may enter by invitation but cannot use private inventory. Decide guest
   cooking/storm access and how agents later leave or change households.

- **Physical stocks and trade.** Store goods must be transported there and
    stored on site before sale. Decide transport and ownership-transfer details
    for Stores, Restaurants and Markets; location-specific inventories cannot
    be an invisible shared pool.

- **Building storage catalogue.** Houses start at 1×1 and may expand to
    1×2 or 2×2, Warehouses 2×2 → 2×3, and Stores may be 1×1 or 1×2. These
    choices raise storage, not House occupancy. The other chosen building
    footprints are in the accepted roster above. Decide capacities, costs,
    expansion triggers, Market reservation behavior and Port clearance details.
