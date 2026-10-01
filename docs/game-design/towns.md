---
title: Towns, buildings and government
type: game-design
status: active
updated: 2026-10-01
---

# Towns, buildings and government

These are the intended game rules and choices. They do not describe
everything that is available in the current build. See [what works today](../what-works.md).

[Game-design guide](README.md) explains the agreement labels.

## On this page

- [Buildings, land, Towns, and animals](#buildings-land-towns-and-animals)
- [Town laws and governance](#town-laws-and-governance)
- [Item and resource pipelines](#item-and-resource-pipelines)
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
  access to the household's private inventory. Invited guests may shelter from
  storms but get no access to the House's stock or cooking. Any adult member of
  the household may invite a named guest and revoke that invitation later.
  Invitations are saved and last until revoked. **Agreed on
  October 1:** a House has a permanent-resident limit that scales with its
  footprint and a dominant family's needs; the [capacity and relocation
  rules](#house-resident-capacity-and-relocation) replace the earlier unlimited
  resident count. Invited visitors and temporary storm guests do not take
  resident places. The House has no bed or sleep-recovery
  role. A selected building exposes inspectable occupants, stock and ownership in a panel
  ([quick card and Details](interface-and-art.md));
  there are **no visible/enterable room interiors**.
- **Building expansion:** a House starts at **1×1** and can expand to **1×2**
  or **2×2** when its household needs more storage or resident places.
  Completed expansion increases storage and the agreed resident limit. A Warehouse starts
  at **2×2** and can expand to **2×3** for more storage. A Store may be **1×1**
  or **1×2**, likewise tied to storage. Exact storage capacity per footprint, costs,
  expansion triggers other than the agreed Warehouse rule (any Town resident may
  plan its expansion once its stock is nearly full), and other building footprints belong in the full content
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
- **Housing priority for a newly added adult:** when the adult has no
  home, seek suitable existing household housing first; start a new House
  project only if none is suitable. Joining, leaving and starting a household
  follow the [agreed membership rules](#household-membership) and
  [House resident limits](#house-resident-capacity-and-relocation).
  Only a household can plan a House. The adult is told
  the real blocker: no household, no House their household holds yet, missing
  materials or no legal site.
- **Town(s)** replaces “settlement” in player-facing terminology. There are
  no village or city place classes: every such place is a Town. The first Town
  already exists during paused New World setup, and its four starting agents
  are Town residents with access to its Warehouse. Later membership follows the
  [agreed Town rules](#town-membership). Towns have generated,
  inspectable borders with room to grow. The border follows the
  Town's assigned buildings, includes spare space around them, and expands
  when new buildings join that Town. **Agreed after the September 30 road
  review:** the border keeps about **three tiles** of spare land beyond every
  building and road (tuned in playtests) and follows the Town's shape rather
  than a rectangle, so there is room, and a reason, to build inside it.
  Removing a building does not automatically shrink the existing border;
  later construction may expand it. Assignment and overlap handling remain open.
  When its last resident leaves or dies, the Town becomes **abandoned**, not
  erased. Its identity, buildings, border and infrastructure remain in the
  world, and later residents can revive that same Town under the
  [agreed abandonment and salvage rules](#borders-abandoned-towns-and-salvage).
- **Warehouse** replaces Storehouse. It is communal: it holds actual
  inspectable resource stock at its location for agents/households resident in
  its recorded Town. Food belongs at home instead. Ordinary access is for
  residents, with the agreed abandoned-Town salvage exception below, checked
  when they pick something up; what someone already
  carries stays theirs. Use [recorded Town membership](#town-membership),
  including homeless residents. The recorded Town controls access even if a
  formal border change leaves the Warehouse outside its border.
- **Household buildings and who may use them:** work roles do not decide what
  an agent may do. One household holds each household building: the Farmhouse,
  Blacksmith, Store and Tailor Shop. Any adult resident of that household may
  use it. The Farmhouse and Blacksmith are household-held, **not communal**: the
  holding household works there and sells directly from its own building. A
  building built after setup is held by the household that builds it. A
  Farmhouse household may also build a Silo and an optional Store, and a Tailor
  Shop household may build an optional Store; Stores are optional. An agent
  cannot give itself access to a household building its household does not
  hold. The two starting households receive the Farmhouse and Blacksmith
  automatically, as recorded in the starter economy note below.
- **Removing or reassigning a building** that holds stock or jobs is refused
  until the stock is moved.
- **Workshop** is communal and held by the Town. It remains for agent
  inventions/mods and is usable by outsiders. Its footprint is **2×2**. Its
  mechanic/code is a **late-development phase**:
  first establish a functioning simulation and broader base asset set, then
  build the invention/mod loop on that foundation. This is staging, not removal
  from the intended finished game. Remove separate Cooking fire/Campfire and
  Stone hearth buildings; House handles cooking. Remove Weaving frame and
  bed/bedroll content.
- **Tailor Shop** is a household-held building, **1×1 or 2×2**. It turns plant
  fiber into cloth and cloth into clothing, and cloth is a real item that can be
  held and traded. The Weaving frame and its "Woven clothing" are removed
  outright as soon as the Tailor Shop works, with no legacy transition; old
  alpha saves need not keep loading, and no migration code is written for them
  (see [Saves](saves.md)). **Leaning toward:** the
  recipe numbers, costs and work time are provisional and will be tuned in
  playtests; this chapter does not fix them. The holding household sells clothing
  directly from the building, as the Blacksmith does, and does not need a Store.
- **Roads and bridges:** the world system generates infrastructure, not the
  player or individual agent. The full agreed road rule is below; the starter
  Path becomes a Road in intended content.
- **Livestock and mounts** belong in the finished game. Hostile predators do
  **not** belong in the current plan, though they may be revisited later.
  The accepted base roster is **chickens (eggs), sheep (wool), cows (milk), and
  horses (mounts)**. Leather/wool and milk/egg products are late-development
  items alongside animal husbandry. Animals belong to households: care uses
  physically reachable feed and water, and collected products enter local
  stock rather than a global pool. Permission to ride a horse does not transfer
  its ownership. If care is missed, production and riding stop first; whether
  and when an animal could die from neglect is still to decide. Breeding is
  autonomous but must have a population cap, not a player-triggered breeding
  action. The cap, resource needs, product intervals, pregnancy duration and
  mount speed need later design and playtesting. The all-content implementation
  now covers local care and products for acquired animals. Acquisition is still
  undecided, so generated worlds receive no starter herd yet. Breeding and
  mortality schedules are also unfinished. Slaughter and hunting remain outside
  the plan; the accepted leather path uses naturally deceased livestock only.

### Household membership

**Agreed with the owner on October 1:**

- An agent belongs to **at most one household at a time**. They can also have
  no household. Visiting, helping or trading with another household does not
  grant membership or access to its private stock. Household membership and
  family ancestry are separate records.
- An adult joining an existing household through an ordinary move needs
  **every current adult member's agreement**. This includes relatives joining
  from another household. The separately agreed Add Agent exception remains:
  placing an agent on household property joins that household without a
  consent step. It still needs a valid free resident place under the agreed
  capacity checks; consent and room are separate requirements.
  An ordinary request is bounded: one refusal, or no answer within the usual
  proposal window, ends it, and that household is not asked again for a while.
  Standing nearby grants nothing; a pending request grants no membership or
  access to the household's stock or shelter.
- An adult may **leave voluntarily without the household's permission**.
  Personal goods, shared stock, access and dependent care follow the
  [agreed departure rules](#household-goods-and-departure).
- **One adult may start a household alone**; a partner or second founder is
  not required. A newly added homeless adult still seeks suitable existing
  household housing first. If none is suitable or accepts them, they may form
  their own household and pursue a House through the ordinary materials,
  terrain, access and land-use rules. Founding a household creates neither a
  free House nor building materials.

The household's existing limit of one planned House remains separate from the
number of people in its family. Splitting into two households does not erase
parentage or other family relationships. Care during a living adult's departure
is agreed below. Town affiliation follows the [Town membership rules](#town-membership);
care after death and other guardianship cases still need their own decisions.

### Household goods and departure

**Agreed with the owner on October 1, answers 10A–14A:**

- **Ownership and location are separate.** A person may keep recorded personal
  belongings in the House without donating them to the household. Shared food,
  materials and household equipment remain household property. For example,
  Rowan's own coat remains Rowan's beside the shared grain in House storage.
  Carrying a borrowed household axe does not make the axe personal property.
- **Moving out ends the old household membership.** This applies to voluntary
  departure and overcrowding displacement. The person keeps their personal
  belongings and the departure allowance below; they get no automatic equal
  share of shared stock. Shared goods and buildings remain with their recorded
  household owner, including when its last member leaves. Moving out does not
  settle abandoned-property claims or transfer buildings to a new household.
- **Personal belongings remain retrievable after departure.** The former
  member retains limited access to collect their recorded personal goods,
  without needing to rejoin or obtain a new permission for each collection.
  Ordinary shared-stock and cooking access end with membership. Retrieval is
  physical, follows carrying limits and may take repeated trips; it grants no
  remote transfer or access to other people's goods. Borrowed goods keep their
  original owner and must be physically returned rather than silently becoming
  the departing person's property. Leaving itself needs no household vote.
- **A small food allowance supports the move.** The initial trial is up to
  **two ready-to-eat food portions**, from available **unreserved** household
  stock, for both voluntary departures and evictions. Existing care and work
  reservations are respected. Missing food is not created. The allowance is
  allocated once for the departure and collected physically within carrying
  limits; repeated requests, collection trips or a saved-and-resumed move
  cannot multiply it.
- **Care continues when a caregiver leaves.** A departing sole caregiver
  remains responsible: dependent children move with them unless another capable
  adult explicitly accepts their care. An accepting destination must have room
  for the whole moving care group under the resident rules. Travel and care
  happen physically; no child is left alone, teleported or assigned to an adult
  who has not accepted. If no home is ready, the housing task remains visible
  while that adult continues care. Parentage is unchanged. The overcrowding
  timer still cannot expel a child alone or remove their only caregiver.

Adult departure, personal ownership, limited collection access, the food
allowance and solo formation are implementation work in
[#593](https://github.com/compoodment/ClankerWorld/issues/593).
Death, wills and other guardianship cases remain separate choices in
[Agents and families](agents-and-families.md#still-to-decide).

### House resident capacity and relocation

**Agreed with the owner on October 1, answers 5A–8A and 9B:** three ordinary
resident places or four with a dominant family **per tile of the House
footprint**. These are total resident limits, not separate family and outsider
allowances. This replaces unlimited House residency. Storage quantities and
construction costs remain provisional; they are separate from these approved
resident counts.

| House footprint | Ordinary resident limit | With a dominant family |
| --- | ---: | ---: |
| 1×1 | 3 | 4 |
| 1×2, either orientation | 6 | 8 |
| 2×2 | 12 | 16 |

- **Count permanent residents, not people currently inside.** Traveling
  residents keep their places. Every child, including an infant, takes one
  place from birth. Invited visitors and temporary storm shelter do not create
  residence, household stock access or a place in the count.
- **A dominant family gets the larger limit.** One recorded
  domestic family unit must contain at least two residents and more than half
  the House's permanent residents. The unit is partners and their
  children, including siblings who belong to that unit; marriage is not
  required. Each person has one primary unit for this housing calculation,
  separately from their full ancestry. Taking a partner creates a new unit;
  it does not merge both ancestral family trees. A shared surname, household
  membership or distant ancestor does not by itself qualify. Nonresident
  relatives do not count toward dominance. A tie gives neither family
  priority. Extended ancestry does not automatically join that domestic unit.
- **Displace only when there is no room.** In a 1×1 House, two partners, their
  baby and an unrelated adult can use all four places. A second baby makes
  five, so the crowding must be resolved by completed expansion or relocation.
  Becoming a family majority does not itself expel anyone. Admission considers
  all residents after the move: a newcomer cannot use a family bonus their
  own arrival would remove. Ordinary adult newcomers still need unanimous agreement;
  family status alone cannot bypass it.
- **Births always complete, even if the home is full.** The parents establish
  a primary caregiver and intended home before birth. The newborn joins that
  caregiver's actual household at birth; the other parent's residence does
  not automatically change. A full or unavailable home creates a visible
  housing need, not a lost or rejected baby. A House that exceeds its limit is
  marked overcrowded and accepts no further voluntary residents. Once an
  overcrowding case opens, a birth or age change does not restart its deadline.
- **Adults relocate; dependents are not sent away alone.** Volunteers go
  first, then the most recently admitted eligible unrelated adult, with a
  stable tie-break for equal arrival times. Without a dominant family, use
  that same volunteer-then-arrival order without favoring a family. A dependent
  moves with a caregiver when needed, and an arrival cannot gain a place by
  displacing people in its destination House. Recheck current residents and
  the completed footprint before acting so a death, departure, expansion or
  new family relationship cannot cause an unnecessary eviction. Continue
  relocating eligible adults only until the remaining residents fit.
- **One unpaused world day of notice for displaced adults** is the initial
  trial period. During notice they look for a suitable
  accepting household with room; otherwise they may form their own household
  and build normally. If notice ends with no home ready, the eligible adult
  leaves residence and the old household membership, and continues a visible
  homeless housing task. Immediate storm
  shelter is still possible under the guest rules. No person or goods are
  teleported to a new home. Goods, collection access and care follow the
  [departure rules](#household-goods-and-departure).
- **Expansion can solve crowding.** The household may plan its next supported
  footprint for a storage or housing need, under the ordinary terrain,
  overlap, land-use, materials and work rules. Reserved space or an unfinished
  project creates no resident places yet and does not restart the notice
  period. A completed expansion cancels any remaining relocation that is no
  longer needed. Adults who already left do not automatically rejoin.
- **An all-family full House needs a family housing plan.** A birth beyond
  the current footprint's family limit permits temporary overcrowding.
  Suitable adults arrange expansion or, if the House cannot expand further,
  a split into another household and House while preserving dependent care.
  With no safe care arrangement, legal site or materials, the home stays
  visibly overcrowded and blocked;
  it cannot admit additional voluntary residents. Do not apply the unrelated
  adult's notice deadline to a child or to remove their only caregiver.
  This adds no housing prerequisite for starting pregnancy, automatic birth
  cap or change to the agreed continuity safeguard. At 2×2, a seventeenth
  family resident therefore needs a housing split, not an invented larger
  base-game footprint.
- **Add Agent respects resident capacity.** It retains the agreed consent
  exception but needs a valid free resident place. A full-house placement
  explains the blocker instead of bypassing capacity or silently evicting
  someone. Placement on a household's other property likewise cannot avoid
  its House's resident limit.

| Case | Agreed result |
| --- | --- |
| Three unrelated adults in a 1×1 House; another wants to join | No free resident place until expansion completes. Seek another household or build if none is suitable. |
| Two partners and one unrelated adult in a 1×1 House; first baby | Four residents with a family majority. Nobody is displaced. |
| Two parents, one child and one unrelated adult in a 1×1 House; second baby | Five temporarily. Completed expansion can keep everyone; otherwise the unrelated adult relocates, leaving four family residents. |
| Four family residents in a 1×1 House; another baby | Birth completes. Expand or arrange a housing split with safe care. |
| Eight family residents in a 1×2 House; another baby | Birth completes. A finished 2×2 expansion gives sixteen family places; otherwise arrange another home. |
| Sixteen family residents in a 2×2 House; another baby | Birth completes. No larger standard footprint; a family housing split is needed. |
| Two unrelated adults in a 1×1 House; one has a baby whose other parent lives elsewhere | Baby joins the primary caregiver. Three residents fit; the other parent does not automatically move. |
| Four residents in two equally sized family units in a 1×1 House | No family majority. A voluntary fourth admission is refused; an existing birth-related case needs expansion or relocation with no family favored. |
| A resident is away gathering; a guest is invited | The traveler still occupies their resident place. The guest gains shelter only. |

The resident counts, family scope, birth exceptions, notice and departure
rules are agreed. The existing
[housing-admission work](https://github.com/compoodment/ClankerWorld/issues/464)
and [storage-expansion work](https://github.com/compoodment/ClankerWorld/issues/463)
remain their original implementation slices; the new residence and relocation
rules require their own follow-up work, without competing with active claims.
Resident limits and housing-driven expansion are tracked in [#598](https://github.com/compoodment/ClankerWorld/issues/598),
and overcrowding/relocation in [#599](https://github.com/compoodment/ClankerWorld/issues/599).

### Town membership

**Agreed with the owner on October 1, answers 15A–18A:**

- **Residence is recorded membership in one Town, or none.** A journey, visit
  or border crossing does not change that record. Household membership,
  permanent House residence, physical location and Town membership are separate
  facts. Rowan can gather near another Town for three days while remaining a
  resident of the original Town.
- **Homeless residents keep their Town membership.** Leaving a household,
  eviction or losing a House does not alone remove Town membership, ordinary
  eligibility to collect communal Warehouse stock or adult voting rights.
  The person can seek another accepting household or build normally while
  remaining a Town resident. Available stock, physical collection, carrying
  limits, access and ordinary construction rules still apply.
- **An ordinary adult newcomer needs Town approval.** The current council or
  other legitimate Town government accepts them. Household admission is a
  separate decision: an accepting household alone cannot grant Town membership,
  communal stock access or a vote. A homeless newcomer may request Town
  membership before finding a House. A request or model claim is not approval;
  walking into the border does not register a resident. The council's voting
  procedure remains a separate governance choice.
- **Dependent children follow their primary caregiver.** A newborn joins that
  caregiver's Town without a separate admission vote; the other parent's Town
  membership is unchanged. Approval for a caregiver moving Towns with dependent
  children covers the care group. Keep the group's membership consistent with
  actual caregiver placement, preserve parentage and count everyone as a
  resident. Children gain no adult council or voting rights before adulthood.
  The household still needs agreement and places for the complete care group;
  Town approval creates no free House, private access or extra resident places.

The separately agreed New World and Add Agent placement rules still initialize
Town membership: the first four agents belong to the first Town, and confirmed
Add Agent placement uses the recorded household property/Town precedence. Those
setup exceptions do not turn later travel into membership or bypass the agreed
House capacity checks.

The [initial council and elections](#town-laws-and-governance) use these recorded
adult residents rather than the agents physically inside a border. Admission
to an abandoned Town follows the [revival exception](#borders-abandoned-towns-and-salvage).
The approved membership work is tracked in
[#602](https://github.com/compoodment/ClankerWorld/issues/602); council voting and
election procedures follow separately in
[#601](https://github.com/compoodment/ClankerWorld/issues/601).

### Borders, abandoned Towns and salvage

**Agreed with the owner on October 1, answers 19A, 20A, 21B, 22A and 23A:**

- **Removing a building does not shrink the border automatically.** Keep the
  existing boundary and room for rebuilding; assigned new construction may
  expand it under the existing layout rules. A formal boundary change does
  not by itself change Town membership, title, household use rights or the
  owner of buildings and goods.
- **A Warehouse's recorded Town controls ordinary access.** If a formal
  boundary change leaves the Warehouse outside that Town's border, its residents
  may still collect stock under the usual physical pickup rules. Do not suspend
  access merely because of geometry or grant it to a neighboring Town.
  Reassignment remains a separate validated change; it cannot erase stock or
  bypass the agreed removal/reassignment safeguards.
- **An empty Town permits public salvage of unreserved communal stock.** When
  no recorded living residents remain, any agent may physically collect that
  stock, including from its Warehouse, within carrying and availability limits.
  This is the owner's chosen exception to resident-only communal access. Check
  the current abandoned state and reservations when pickup actually happens;
  no missing goods are created and no reserved lot is silently released.
  Visiting or salvaging creates no Town membership, council vote or access to
  private household goods/buildings. Salvage does not erase the Town's identity,
  stock history, claims or existing ownership records.
- **One adult may explicitly resettle an abandoned Town.** The adult must be
  physically there and choose residence, rather than merely visit or collect
  salvage. Record the deliberate membership change, preserving the one-Town
  limit and dependent-care rules, revive the same Town identity and restart its
  council from the returning adult residents. No previous council approval or
  second adult is required for this first-resident exception. Later ordinary
  newcomers need the restored government's approval. Resettling grants no free
  House, materials or ownership of old private buildings; ordinary housing,
  care, construction and rights rules still apply.
- **Revival ends public salvage access.** Remaining communal stock again uses
  ordinary resident eligibility, checked at the next physical pickup. Goods
  already collected legitimately stay with the person carrying them; revival
  neither duplicates those goods nor confiscates them. A stale salvage plan
  cannot collect new stock after the Town is occupied unless it now has normal
  resident access.
- **The Town's existing laws survive abandonment.** The restored council may
  amend or repeal them through the agreed governing process once that process
  is defined. Old councillors or offices are not resurrected automatically.
  The laws remain social rules that can be broken; their persistence does not
  rewrite fixed world facts or grant agents knowledge they have not learned.

For example, a visitor may collect five available unreserved logs from an
abandoned Town without joining it. An adult later chooses to settle there;
the remaining logs become resident-only communal stock again, the same Town
revives, and its protected-grove law still exists until validly repealed.
Private coats, Houses and household equipment retain their recorded owners.
Abandonment/revival and salvage implementation remains recorded in
[#410](https://github.com/compoodment/ClankerWorld/issues/410), which the owner
previously parked until play reaches this stage; these design answers do not
claim that it is implemented or change that timing.

### Agreed content and building sizes

Computment accepted all unmentioned entries of the initial
[asset-roster review](content-list.md) and amended specific rows
as recorded here. The roster's numbered entries are the detailed accepted
**base content**. The owner subsequently approved the
[item and resource pipelines](#item-and-resource-pipelines) on October 1; their
quantities remain provisional. Other open costs, capacities, animation budgets
and invented content are not silently approved. In particular:

- Terrain uses roughly two subtle textures per surface, with most little
  grass/stone/leaf details baked into texture variation. A tile has at most
  one tree. Iron, gold and diamond deposits are visibly distinct
  **ore-bearing outcrops**, not ground veins.
- The farm roster includes **universal grain** (no named grain species),
  **potatoes**, a cultivated leafy green distinct from wild greens that
  satisfies more hunger, and orchard fruit. Flour is a sellable Farmhouse
  intermediate, including at a Market; cloth, made at the Tailor Shop from
  plant fiber, is likewise a real intermediate item.
- The Blacksmith refines **iron ore into a separate metal item**, then uses it to
  make tools. Selling spare refined metal directly from the Blacksmith is
  a strong proposed extension awaiting final confirmation. The Blacksmith
  already sells tools directly and takes tool-making requests—no separate
  Store is required for its own products.
- The accepted medical goods include **bandages and medicine**. Their cloth
  and herb/water/fuel supply chains are agreed below; detailed recovery rates
  remain provisional.
- Farmhouse **1×1 or 1×2**; adjacent private Silo **1×1**; Blacksmith
  **1×2 or 2×2**; Tailor Shop **1×1 or 2×2**; Workshop **2×2**;
  Restaurant **1×2 or 2×2**; Clinic/healer's shop **1×1 or 1×2**.
  Market's main building is **2×2** with separate **1×1 stalls** and an
  approximately **10×12 clear stall-reservation area**. Town Hall is
  **3×4**. These are building/plot footprints, not interior rooms.
- Port is **2×4**, rotatable to all four cardinal directions. One tile of its
  four-tile length rests on land; three extend over water. Keep clear docking
  space along both long sides of that three-tile water section. A legal land
  approach and clear docking space are required. The first clearance rule
  reserves one water tile along each of the two long sides, six tiles total,
  plus clear land behind the two land tiles; a Road may occupy that approach.
  The pier does not claim water as Town land. Boats are communal Town property,
  usable by residents or visitors with permission; the first
  [boat journey](world.md#first-boat-and-port-travel) needs a completed Port at
  each end. Costs and speeds there are provisional. The first planner tries to
  establish two reachable Town Ports; extra Ports can be placed on legal shores.
- Combat gear includes a spear, sword, shield and armor alongside the
  dual-purpose axe. Clothing does not change an agent's map appearance; the
  visual treatment of worn armor remains open.
  Maps, written records and books are accepted knowledge goods; law, claims,
  ownership and wills are mainly inspectable **data/gameplay**, not a demand
  for separate physical document objects.

The accepted roster includes proposed-but-now-chosen families such as clay,
pottery, rope, orchard fruit and carry aids. Their sources, processing and uses
are now agreed in the pipelines below; numerical balance is provisional.

### Choosing building sites

Agents identify a building need and do the work. The Town layout system offers
**several ranked viable sites** rather than letting an agent search every tile.
The agent can accept or reject; after rejection, they explain what was wrong
and the layout system re-ranks sites. The retry/stop limit and what happens if
no acceptable legal site exists remain open. Site ranking should account for
terrain, resources, existing buildings, ownership, access, other Towns, room
for growth and building purpose. Town appearance/layout should vary by
Town/culture. Exact weights and when another offer appears are open.

**Agreed, what a household plans:** a household plans only the buildings it
needs for itself: a House, or a Farmhouse, Blacksmith, Store or Tailor Shop it
does not yet hold. It plans at most one of each kind, and only once it has the
materials in hand. There is no Town-wide limit on how many of a kind exist
until playtests show agents overbuilding. Only the household that holds the
Farmhouse plans a Silo, next to its Farmhouse. The **Workshop is the one
exception**: a household may plan it, and once it is built the Town holds it.
An adult resident may also plan and fund a shared Market with its clear stall
plot. Other buildings the Town shares wait for governance. Any Town resident may plan
the Warehouse's expansion once its stock is nearly full.

The intended building roles now include House, Warehouse, Workshop,
Farmhouse, farm fields, an adjacent private farm Silo, optional household-run
Store, household-run Blacksmith with internal work stock and direct sales,
Tailor Shop, public Market and stalls, Town Hall, Port, Clinic, and optional
agent-founded Restaurant. A
Farmhouse processes crops; the household that holds it places fertile fields,
plants seeds, tends, harvests, and sells/trades the produce. Farm count
responds to **Town population and farm yields**; a shortage or reduced yield can justify
more farming rather than a hard cap blocking recovery. The exact formula is
open. Farm work stock
is private to its household; its Silo is distinct from the public Town
Warehouse. The Blacksmith makes and sells tools on site and can accept
specific tool-making requests, with stock inside the building. A Store
supports a household selling its products. Goods must be physically carried
to the Store and kept in its own stock before sale; a Store cannot sell from
a remote House, farm, or Warehouse inventory. A Market admits traders from any
Town; a Restaurant buys ingredients, cooks and sells potentially better meals.
Town Hall supports governance and Port supports boats. The physical
stock and barter rules are now agreed in the pipelines below; ownership edge
cases, currency rules and later pricing still need their own decisions. See the
[accepted asset roster](content-list.md) for specific content and
[current state](../what-works.md) for what exists in the prototype.

Wood, stone, iron, gold, diamond and many other crafting resources are wanted.
Better tools should gate harvesting/mining more advanced resources, creating
a progression incentive. The agreed initial ladder is **wood tools → stone →
stone tools → iron → iron tools → rarer materials**. Gold, diamond and other
materials are extracted with iron pickaxes and used for ornaments or trade,
as agreed in the pipelines below. Wood and stone should both be useful for House construction;
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

**Agreed after the September 30 road review:**

- **Street first.** A Town grows along a main road that winds with the land,
  bending in 45° steps around water, trees and slopes. Side streets branch
  off it at uneven spacing, angles and lengths. The result should look grown,
  not gridded; an evenly spaced fishbone was rejected as too uniform.
- **Diagonal Roads.** A street, or a Road towards another Town, takes the
  diagonal where the land allows instead of stepping in right angles. Agents
  walk diagonally, so a zigzag Road would be slower than the grass beside it.
  Diagonal Road steps follow the same clear-corner rule as diagonal walking.
- **Every building faces a Road.** A building has one entrance, on the side
  facing its Road, and its sprite shows the door on that side; doors are not
  limited to the bottom edge. A short doorstep path joins the door to the Road.
- **Roads run on past the last building** on them, by about three tiles, so
  there is visible free frontage for the next building (tuned in playtests).
- **Roads grow with the Town.** Each new building extends the Road network:
  its street continues past it, and when a street's free frontage runs out a
  new side street branches off. New buildings prefer free Road frontage inside
  the border. Town Roads stay inside the border, which grows with the Town.
- **Look:** packed dirt with worn, feathered edges and pebbles, drawn as Road
  pieces joined only along the Road, with diagonal pieces, rounded bends and
  ends. On sand and snow it may take a darker edge to stay visible.

**Bridge placement responds to traffic** at narrow river crossings, even
without a planned Road. A Road generation pass also builds a bridge immediately
if its legal route encounters a bridgeable river. A river is **bridgeable up to
two tiles wide**; wider water is not bridged. Roads and bridges **cost no
materials**. Either case excludes a redundant nearby bridge over the same
crossing/river, not a necessary bridge over a different nearby stream. Bridge
spacing compares the actual connected banks, with **no fixed radius**, so a
needed bridge over a separate nearby stream is never blocked.
For traffic-created bridges, the initial playtest threshold is **six completed
crossings by at least two distinct agents within two world-days** at the same
legal narrow crossing. Only actual traversal counts, not route previews,
failed attempts or waiting. Keep bounded crossing evidence across saves; the
threshold can be tuned after playtesting. Generated-Road bridges need not wait
for this traffic.
Both bridge triggers are now built; Road links between Towns wait for a second
Town. [What works today](../what-works.md#maps-weather-and-appearance) says what
normal play shows so far, and [How it works](../development/how-it-works.md#roads-and-bridges)
says how the same connected banks are compared.

**Roads and bridges remain permanently** once built. They do not decay or
disappear automatically when traffic stops, a building is removed or a Town is
abandoned. There is one Road type; no temporary-versus-permanent class is
needed. Exact inter-Town route timing, layout and rendering remain open.
Diagonal travel/Roads remain in scope; diagonal moves
must not pass through blocked corners. Playable foot movement now uses the strict
  two-clear-shoulder rule and a 141% diagonal route cost; diagonal Road
  construction and visuals are agreed above but not built yet.

### Still to decide

Further structure effects; exact configurations and unchosen footprints;
building
inspection fields, access to other non-residential
buildings, reservations and queues;
claim boundaries, shared-use grants and mayoral powers; Town borders and
governance; currency/land pricing;
transport progression; other terrain eligibility,
travel effects; advanced resource/tool tiers,
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
- The game rules still protect fixed world facts and action
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

### Council size and terms approved on October 1

Once a Town reaches **eight adult residents**—double the four-agent
starting population—it begins electing a smaller representative council
of **three representatives for one game year**, instead of keeping every adult as a council member. Count adults in that
Town, not across the world. Eight is an initial threshold to playtest,
not a claim that every Town must forever follow this exact rule. All
adult residents retain a vote in those elections. Below eight adults, every
adult resident sits on the council again. Adults are active adult or elder
agents listed as residents of that Town. Membership in another Town gives no vote.

Proposals pass with a majority of current council members. A tie or a proposal
that cannot reach that majority keeps the current rule. Residents attend their
actual Town Hall to propose or vote; the Hall belongs to the Town and does not
give visitors access to private household goods. The 3×4 Hall costs **24 wood
and 12 stone provisionally**, funded through normal physical construction.

The first version holds an election and each rule ballot open for one game
day. Each adult may name up to three current adult residents once per election.
The three with most votes win; tied counts retain existing representatives
before comparing stable agent identities. Representatives serve until the next
annual election finishes. A vacant seat starts a new election, and a smaller
Town returns to its all-adult council. These election details are provisional.
Departed or deceased residents lose eligibility. Proposals to change or repeal
a social rule use the same majority vote. Shared-food policy can restrict
existing collection access, but cannot seize or reassign private stock.
Model-backed councillors can propose a named social rule only when physically
at their Town's Hall and no vote is pending. Proposals contain bounded plain
text; the model cannot attach one to another action, change fixed world rules
or assign somebody else's property. Agents learn the text of existing rules
when they read or discuss them at the Hall.

### Still to decide

Town residency and newcomer approval are agreed in [Town membership](#town-membership).
The first elected council size, one-year term and majority proposal/repeal
procedure are agreed above. Still open: how governments may change; which
other laws apply to whom and where; how a
violation is witnessed, investigated, enforced, or punished; specific land
rights grant and adjudication procedures;
taxes, inheritance, and interaction between conflicting Towns. Agents
should know only laws or violations they have learned about in-world.

## Item and resource pipelines

### Agreed on October 1

The owner approved these complete pipelines on October 1, 2026. They describe
intended gameplay, including work that is not built yet. Sources, production
sites, uses and ownership rules below are agreed. Recipe quantities, yields,
work times, storage sizes, wear, spoilage and effects are provisional and are
tuned in playtests. See [what works today](../what-works.md) for current play.

Every pipeline covers **source → gather or grow → carry → store → process →
use or sell → replenish or replace**. Named goods replace generic raw `food`
and universal crop `seed` where those hide different resources. A simple
cooked meal remains a specific item with a generic meal icon.

#### Food and replanting

| Source | Product and complete path |
| --- | --- |
| Berry bush | Gather berries, carry home or into sale stock, eat fresh or sell; the bush regrows. |
| Wild greens patch | Gather wild greens, eat fresh or cook; the patch regrows. |
| Grain field | Plant grain seed, tend, harvest grain and replacement seeds, reserve replanting stock, carry grain to private Silo/Farmhouse stock, then make porridge or mill flour. |
| Potato field | Plant potatoes, tend and harvest, reserve planting potatoes, then cook or sell the remainder. Potatoes are both the crop and planting stock. |
| Cultivated greens field | Plant cultivated-green seed, tend, harvest greens and replacement seeds, reserve replanting stock, then eat, cook or sell. Cultivated greens nourish more than wild greens. |
| Orchard tree | Plant orchard seed, let the tree mature, then harvest fruit and some orchard seeds in autumn. Eat or sell fruit and reserve seeds for more trees. |

Berries, fruit and greens provide immediate food. Grain and potatoes normally
need processing or cooking. Fields pass through prepared, planted, growing,
ready and harvested states. Farmers keep enough planting stock for the next
crop before offering a surplus for sale. Grain seed, cultivated-green seed,
wood-tree seed and orchard seed are distinct; planting potatoes remain potatoes.

| Finished product | Trial recipe | Work site and purpose |
| --- | --- | --- |
| Flour | 1 grain → 1 flour | Farmhouse; a real intermediate that can be stored, carried and sold separately. |
| Simple meal | 2 potatoes/greens + 1 wood → 2 meals | House; everyday household cooking, using potatoes, wild greens or cultivated greens. |
| Porridge | 1 grain + 1 water + 1 wood → 2 servings | House or Restaurant; accessible grain-based nourishment. Berries or fruit may improve a serving. |
| Bread | 2 flour + 1 water + 1 wood → 2 servings | House or Restaurant; keeps longer and travels well. |
| Vegetable stew | 1 potato + 1 cultivated greens + 1 water + 1 wood → 2 servings | House or Restaurant; a substantial vegetable meal. |
| Restaurant meal | 1 bread + 1 cultivated greens + 1 wood → 2 servings | Restaurant; better nourishment and dietary variety, sold on site. |

Harvests stay at their field until collected; they do not appear remotely in a
Silo. Grain and planting stock enter private farm storage. Ready food goes into
House or business stock. Dry grain keeps substantially longer than flour,
bread or cooked meals and supports a winter reserve. Food stays out of the
communal resource Warehouse.

Farm production responds to population, expected yield and stored reserves.
Shortages can justify more fields or another farming household. The target for
playtesting is roughly two substantial meals per agent per day, leaving time
for other activities; this is not a fixed nutrition or hunger-drain formula.

#### Materials and useful destinations

| Material | Source and processing | Uses |
| --- | --- | --- |
| Wood | Fell trees with an axe, carry wood, recover tree seeds and replant. Loose fallen wood can also be collected by hand. | Buildings, expansion, fuel, tool handles, carts and boats. Initially one wood item; separate logs and planks wait until they create useful decisions. |
| Stone | Extract from a finite outcrop with a wooden pickaxe and carry it. | Stone tools, building foundations and expansion. |
| Iron | Extract iron ore with a stone pickaxe; at the Blacksmith, a trial 2 ore + 1 wood makes 1 refined iron. | Iron tools, fittings, carts, weapons and armor. Ore and refined iron remain separate items. |
| Plant fiber | Gather fiber plants or reeds; the plants regrow. | Cloth, rope and baskets. |
| Cloth | Tailor Shop; trial 3 fiber → 1 cloth. | Clothing, sacks, bandages and book covers. |
| Rope | House crafting; trial 3 fiber → 1 rope. | Sacks, carts, boats and later construction recipes. |
| Clay | Dig a clay bank and carry clay. | Fired storage pots and water jugs. |
| Pottery | House crafting; trial 2 clay + 1 wood → 1 vessel. | Reusable storage pots and water jugs, with separate item identities. |
| Fresh water | Collect from a river or lake with a reusable jug and carry it. | Cooking, medicine-making, paper-making and animal care. |
| Paper | House crafting; trial 2 fiber + 1 fresh water → 2 paper. | Maps, written records, books and copies of real learned knowledge. |
| Gold | Extract from a gold-bearing outcrop with an iron pickaxe and carry to the Blacksmith. | Ornaments, gifts and trade goods. |
| Diamond | Extract from a diamond-bearing outcrop with an iron pickaxe. | Trade goods or a stone set into an ornament. |

The Blacksmith makes gold ornaments, optionally set with a diamond. Agents may
keep, wear, gift or sell them. This is the agreed destination for rare materials;
it does not select gold as a universal currency or add a diamond tool tier.

A storage pot slows spoilage for the food within its limited capacity. A water
jug holds a limited quantity of water. Both remain after their contents are
used; filling, emptying and carrying them must preserve the vessel and its goods.

The initial pottery implementation uses a provisional capacity of **eight**
items per vessel. A House fires one vessel from two clay and one wood over 18
periods of work. Filling a jug takes six periods beside fresh water; ocean
water is excluded. A pot halves spoilage only for its actual contents. A
household initially aims for two reusable jugs and one storage pot. These are
playtest values, not a settled demand or balance rule.

#### Tools, clothing and transport

| Equipment | Production and function |
| --- | --- |
| Wooden axe and wooden pickaxe | Blacksmith, using wood; basic tree and stone gathering. |
| Stone axe and stone pickaxe | Wood and stone; better speed and durability. The stone pickaxe unlocks iron extraction. |
| Iron axe and iron pickaxe | Wood and refined iron; better speed and durability. The iron pickaxe unlocks rare deposits. |
| Wooden hoe | Wood; prepares and tends fields. |
| Iron hoe | Wood and iron; performs field work faster. |
| Hammer | Wood and stone; helps construction and repairs. |
| Sickle | Wood and iron; speeds crop harvesting. |
| Knife | Iron; speeds food preparation and suitable crafting work. |

Tools wear through use. The Blacksmith repairs worn tools with some of their
original materials; broken tools need replacement. Hand collection of loose
fallen wood keeps replacement wooden tools possible after the last axe is lost.

| Product | Production and effect |
| --- | --- |
| Basic garment | Fiber → cloth → Tailor Shop; trial 2 cloth → 1 garment. Carry it home, wear it, and repair wear with cloth. |
| Padded coat | More cloth at the Tailor Shop gives stronger cold protection. |
| Rain cloak | Cloth and fiber at the Tailor Shop reduce rain exposure. |
| Basket | Fiber and rope, crafted at home, give a modest carrying increase. |
| Sack | Cloth and rope at the Tailor Shop give a larger carrying increase. |
| Handcart | Wood, iron fittings and rope at the Blacksmith allow large loads, especially on Roads. |

Baskets and sacks use one equipped carrying slot. A handcart is a visible
object that an agent pulls, parks, repairs and transfers ownership of. Worn
clothing still does not change the agent's map sprite. Exact carrying amounts,
cart movement costs, protection and repair quantities are tuned in playtests.

The first carrying trial is 32 item units without an aid, 48 with a basket and
64 with a sack, including goods inside carried vessels. Existing oversized
loads are retained and can be reduced; new pickups need room. A smaller aid or
removing the aid requires delivering enough cargo first. Clothing loses one
condition point per world step, and a basket or sack loses one per travel step.
House or Tailor Shop repairs use one cloth for a garment or sack, or one fiber
for a basket. These amounts and rates are provisional.

#### Care and later goods

| Product | Complete path |
| --- | --- |
| Bandage | Fiber → cloth → cut bandages at a House or Tailor Shop → carry to a patient → consume while treating an injury. |
| Medicine | Gather medicinal herbs → carry to Clinic → herbs, water and fuel make medicine → administer to an ill agent. |
| Clinic treatment | The Clinic stocks its own medicine and bandages. A patient visits or a caregiver carries supplies to them; treatment consumes the required goods. |
| Eggs | Household chickens with reachable feed, water and care produce eggs; collect into local House/Restaurant stock, cook or sell. |
| Milk | Household cows with feed, water and care produce milk; collect, drink, cook or sell. |
| Wool | Care for household sheep, shear them and carry wool to the Tailor Shop for warm clothing. |
| Leather | Naturally deceased livestock provide a hide; the Tailor Shop processes it into leather for durable clothing and carry gear. |
| Horse transport | Household horse, feed, water and care → permitted rider or cargo use. Permission does not transfer ownership. |
| Boat | Wood, rope and iron fittings → construct at a Port → communal Town boat → carry agents and goods between completed Ports. |
| Spear and sword | Wood and iron at the Blacksmith → equip → combat use → repair or replace. |
| Shield and basic armor | Wood, cloth and iron at the Blacksmith → equip → protection → repair or replace. |
| Maps, records and books | Fiber and water make paper; write actual learned knowledge, bind where needed, then carry, read, copy or sell. Cloth may supply a book cover. |

The first knowledge-goods trial makes two paper from two fiber and one unit
of fresh water at a House over 16 periods of work. Writing a map or one-site
record consumes one paper. Binding a book consumes two paper and one cloth.
An adult writes at their household's House with its on-site supplies, and
carries the finished item. Maps and books contain at most nine actual learned
sites; a record contains one. Reading and copying need the real source item.
A household source at another building must be physically collected before
copying it at the House. Copies preserve the original discoveries and their
source; ownership of a copy does not grant other agents its contents. These
quantities and bounded sizes are provisional.

The first care trial uses a **1x2 Clinic**, built with ten wood and four stone.
One cloth makes two bandages at a House or Tailor Shop. Two medicinal herbs,
one unit of fresh water and one wood make two medicine at the Clinic. A dose
is physically consumed before twenty steps of gradual recovery: a bandage
adds fifty health points per step, and medicine removes seventy-five illness
points per step, on the ten-thousand-point meters. These are provisional
playtest quantities and rates. An adult patient explicitly accepts a named
caregiver; existing accepted caregiver relationships permit dependent care.
Revoking permission or the caregiver dying stops the remaining effect and
never restores the consumed dose. Bought care goods belong to the patient;
a care permission never opens another household's stock.

The husbandry trial uses one grain or one wild/cultivated greens as feed and
one unit of actual jug water. Feeding, watering and hands-on care each cover
ninety-six steps; agents renew them before that runs out. Forty-eight cared-for
steps produce one egg, two milk or two wool. One batch waits at the animal;
it does not enter House stock or grow without limit. Eggs and milk spoil while
waiting and retain that age when collected. Milk uses an empty or milk-filled
jug, travels as a whole vessel, and leaves its jug behind when drunk or cooked.
Two eggs and one wood make two simple meals at a House or Restaurant. One
grain, one milk and one wood make two porridge. These values are provisional.

Shearing uses a carried knife and the shared tool-wear rules. Two wool make
one cloth at the Tailor Shop. An authoritative natural-death
event can leave one hide on a cow, sheep or horse; the care system never creates
such a death from missed care. One hide and one wood make two leather. Two
leather make a leather coat; two leather and one rope make a carrying satchel.
Leather gear wears at half the cloth gear rate, a trial balance value, and uses
leather for repair. The coat gives forty-five percent exposure protection;
the satchel holds sixty-four item units including vessel contents.

A horse accepts one permitted adult rider and sixty-four units of physical
cargo, including jug contents. Household adults can grant and revoke named
outside riders. Cargo keeps its owner and the horse's location, and revoking
riding permission never confiscates that cargo. Horse travel currently uses
ordinary walking routes and timing. Acquisition, breeding caps and autonomous
natural-death rules remain open; this implementation adds no population or
mortality assumption to those decisions.

Bandages treat injuries; medicine supports illness recovery. Neither instantly
restores full health. Medicinal herbs, paper, hides and ornaments are approved
additions to the catalogue. Animals, mounts, combat and invention work retain
their later staging; their detailed actions and balance are not settled by
these item pipelines. See [combat](agents-and-families.md#combat) and
[inventions](inventions-and-mods.md).

#### Physical trade and production safeguards

The Farmhouse sells produce, seeds and flour stocked at its location. The
Blacksmith sells tools and takes tool-making orders. The Tailor Shop sells
clothing, cloth and sacks. Restaurants buy ingredients and sell finished meals;
Clinics stock care goods and sell treatment. An optional Store receives goods
by actual delivery before selling them. Market sellers carry goods into their
stalls, trade with agents from any Town and carry remaining goods away.

A household reserves a free marked Market stall by physically bringing its
goods. It keeps that stall while unsold goods or barter receipts remain; the
last withdrawal releases it. Spoiled or broken stock may be moved onto clear
adjacent ground without deletion. The Market and its clear plot belong to
the Town; goods in each held stall belong to that household. An adult resident
may fund construction using real household supplies or physically collected
Town Warehouse supplies. Customers gain no private stock or cooking access.

Initial trade uses barter offers naming exact goods and quantities. Both sides
bring their goods to the transaction. The buyer receives purchased goods
personally; payment becomes seller-household stock at that location. Later
agent-created currencies use the same transaction system, with issuer and
acceptance rules designed alongside governance. No universal starting money
or gold standard is chosen.

The first business trial offers one item for one wood, or one wood for one
stone. Restaurant meals ask for one grain; Clinic bandages ask for one cloth.
Adults may name other exact barter quantities. Offers expire after 120 ticks;
tool-making requests after 600. Stores cost 8 wood and 2 stone for 1×1, or 12
wood and 4 stone for 1×2. A 2×2 Market costs 20 wood and 8 stone and funds its
1×1 stalls, each holding 64 item units. All numbers are provisional.

Accepted offers reserve the actual goods, payment, vessel contents and net
receiving space. Other work respects those commitments. Both participants
must reach the stocked building before an atomic exchange; cancellation,
expiry, death or a changed route releases reservations without transferring
either side. A Blacksmith request starts real production from its on-site
inputs; the finished tool remains private stock until exact barter completes.

Ownership and physical location remain inspectable across every transfer.
Production reserves actual inputs and output space. Full storage, missing
materials, unavailable tools and blocked transport give a readable reason.
Guests and customers gain only the access their invitation or transaction
grants. A Store cannot sell goods from a remote House, farm or Warehouse.

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
   and one usable wooden pickaxe, and each starting agent has one garment kept
   in their House. The Farmhouse and Blacksmith are assigned
   automatically to the two starting households, one each, without player
   selection. Decide optional extra starter supplies and how first-tier tools
   are made when the Blacksmith is unavailable.
   Farm planning responds to population, yield and stored reserves as agreed
   above; the exact formula is provisional.

- **Automatic infrastructure details.** Towns connect by Road where a legal
   land route exists and can remain disconnected otherwise. A generated Road
   may bridge a legal crossing immediately. The trial traffic threshold, the
   two-tile width limit, the spacing rule and the no-materials rule are given
   above, and permanence, including after building loss or Town abandonment,
   is already agreed. What remains open is exact inter-Town route timing,
   layout and rendering.

- **Homes, membership and family growth.** The [membership rules](#household-membership)
   settle one household at a time, unanimous adult admission, voluntary adult
   departure and solo formation. The [resident-capacity rules](#house-resident-capacity-and-relocation)
   settle footprint-scaled places, family priority, births and relocation. The
   [goods and departure rules](#household-goods-and-departure) settle personal
   ownership, collection access, the food allowance and care during a living
   adult's departure. [Town membership](#town-membership) settles recorded
   affiliation, homeless residents, ordinary newcomer approval and dependent
   children. [Borders, abandonment and salvage](#borders-abandoned-towns-and-salvage)
   settle the empty-Town exception and communal access. Care after death,
   other guardianship cases, private abandoned-property transfers and remaining
   border assignment/dispute cases stay open.

- **Physical stocks and trade.** Store goods must be transported there and
    stored on site before sale. Decide transport and ownership-transfer details
    for Stores, Restaurants and Markets; location-specific inventories cannot
    be an invisible shared pool.

- **Building storage catalogue.** Houses start at 1×1 and may expand to
    1×2 or 2×2, Warehouses 2×2 → 2×3, and Stores may be 1×1 or 1×2. These
    choices raise storage and House resident capacity under the agreed
    footprint table above; enforcing resident limits and relocation remains
    work in #598 and #599. The other chosen building footprints are in the
    accepted roster above. Storage capacities and costs use provisional trial
    values; other expansion triggers remain open. Port clearance follows the
    first boat rule above; Market stalls follow the approved physical-stock
    reservation rule.
