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
  disputed; permission, transfers and case procedures are agreed below. Monetary
  land values and purchase prices become meaningful after currencies exist. Agents
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
  later construction may expand it. A Warehouse keeps its recorded Town access
  if its footprint is outside that Town's border. Moving a Warehouse to a
  different recorded Town updates that building assignment but does not move
  either Town's border or transfer land title. Reassigning a private building
  changes its household owner while preserving its recorded Town assignment.
  Town identity and current resident membership are separate: the founding
  roster is historical provenance, while each living resident belongs to at
  most one recorded Town.
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
  mount speed need later design and playtesting. Animal husbandry remains a
  **late-development phase**, not a current playable system. Slaughter,
  hunting and a leather-production chain are not assumed.

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
  bypass the agreed removal/reassignment safeguards. Moving one updates both
  Towns' assigned-building records and leaves their borders unchanged.
- **Owner building changes preserve physical state.** The owner may remove a
  placed building or reassign its household/Town owner through a signed action.
  Removal and reassignment are refused while the building has stored or
  inbound stock, or active production/expansion work. Completed work history is
  retained after removal. Removing a building keeps its Town border and Roads;
  changing a private building's household owner never changes its Town title
  or assignment. Removing or reassigning a House clears its shelter-only guest
  invitations. An action against an outdated owner record is refused.
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
Other buildings the Town shares wait for governance. Any Town resident may plan
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
model text cannot rewrite these records. The first Town begins with a council,
not an assumed mayor. Residents can later approve creating an elected mayor
through the protected government-change process. Its creation, term and
elections are agreed in [The mayor's office and elections](#the-mayors-office-and-elections);
[Land hearings and rulings](#land-hearings-and-rulings) settles adjudication.

Competing requests are visible as **pending disputes**. While one is pending,
conflicting formal rights transfers pause; residents are not evicted, goods
are not seized and ordinary physical movement is not stopped by the dispute
record. Resolution needs an adjudicator authorized by Town law, with the
authority, evidence and exact change recorded. With no such law or adjudicator,
the dispute remains pending; there is no automatic first-claimer winner.
Later October 1 owner answers on
[#426](https://github.com/compoodment/ClankerWorld/issues/426#issuecomment-5924016895)
settle the following land rules: a plot is a connected group of tiles and may
include empty land; ordinary household permission continues unless disputed,
with an optional agreed end date; households may transfer permission by
agreement without mayor approval for every transfer. The elected mayor makes
the final decision on disputes and what happens when an agreed period ends.
These rules do not imply automatic confiscation or unilateral household
transfers. Evidence, hearings, outcomes and conflicts of interest follow the
agreed [case procedure](#land-hearings-and-rulings).

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
changes to claim boundaries; Town borders and
cross-Town jurisdiction; currency/land pricing;
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

### Election threshold to try

Once a Town reaches **eight adult residents**—double the four-agent
starting population—it begins electing a smaller representative council
instead of keeping every adult as a council member. Count adults in that
Town, not across the world. Eight is an initial threshold to playtest,
not a claim that every Town must forever follow this exact rule. All
adult residents should retain a vote in those elections.

### Council proposals and election ballots

**Agreed with the owner on October 1, answers 24A–28A:**

- **Any adult Town resident may bring a proposal to the council**, including
  after representatives are elected. An ordinary newcomer may submit their
  own admission request but cannot vote on it. Proposing is not approval or
  authority to change membership, ownership or fixed world facts.
- **An ordinary law or admission proposal needs yes votes from more than
  half the council.** Silence and abstention supply no approval. A four-person
  council needs three yes votes; a three-person council needs two. This is a
  strict majority of the council, not merely more yes than no among replies.
  Changing the governing arrangement uses the separate
  [resident approval rule](#town-law-scope-and-changes). Land adjudication and
  inheritance procedures are separate choices; an ordinary council vote does
  not settle them automatically.
- **The initial proposal window is one unpaused world day.** Resolve sooner
  when the result is settled; otherwise close at the deadline. Without sufficient
  approval then, the proposal does not pass. Paused time does not consume the
  window. Agents vote through their ordinary personal-model turns; make no extra
  paid calls merely to poll for votes. Proposal delivery follows the agreed
  [notice rules](#election-eligibility-ballots-and-notices). Changes to the
  council roster follow the agreed
  [proposal and scheduling rules](#council-changes-and-election-scheduling).
- **The first elected council has three seats.** Use the already agreed
  eight-adult trial threshold to begin representation. A Town may later change
  its governing arrangement through the agreed in-world process; three seats
  are its starting design, not an immutable world constraint.
- **Each adult resident may support up to one different willing candidate
  per available seat.** In a three-seat election a voter may choose up to three
  distinct willing candidates, or fewer. No repeated vote for one candidate on
  the same ballot. Candidates with the highest totals win the available seats.
  Ties and too few candidates follow the agreed
  [council continuity rules](#election-terms-and-council-continuity).
  Turnout, candidate eligibility, self-voting and ballot revision follow the
  agreed [ballot rules](#election-eligibility-ballots-and-notices).

Use recorded Town adult residents for civic eligibility, including homeless
residents and travelers, under the agreed membership work in
[#602](https://github.com/compoodment/ClankerWorld/issues/602).
These proposal, voting-window, seat-count and ballot choices are settled.
The remaining ordinary council-process rules are agreed in
[Council changes and election scheduling](#council-changes-and-election-scheduling).
The completed voting and election decision is tracked in
[#601](https://github.com/compoodment/ClankerWorld/issues/601). The authority and procedure
for broader changes to the governing arrangement are agreed in
[Government-change procedure and safeguards](#government-change-procedure-and-safeguards).

### Election terms and council continuity

**Agreed with the owner on October 1, answers 29A–33A:**

- **An elected council's initial term is ten unpaused world days**, one
  season. Open voting before the scheduled term ends so residents can choose
  the next council. For example, a council seated on day 12 has its next term
  due on day 22. Paused time does not advance the term.
- **A tie for an unresolved seat gets one runoff lasting one unpaused world
  day, then a fair draw if the tie remains.** Only the tied candidates and
  unresolved seats enter the runoff; already settled seats stay settled.
  Record the draw so saving and reloading cannot reroll the outcome.
- **A vacant seat needs a replacement election for the remainder of the
  existing term.** Do not automatically promote a runner-up from an earlier
  election or restart the term. If three willing representatives cannot be
  formed, temporarily return to the all-adult council. Proposal majorities
  during vacancies and election retries follow the agreed
  [scheduling rules](#council-changes-and-election-scheduling).
- **Falling below eight adults does not immediately end representation.**
  Keep the elected arrangement until only three or fewer adult Town residents
  remain. Then use the all-adult council until the Town reaches eight adults
  again. A fall from eight adults to seven therefore keeps representation.
  Count recorded residents, including travelers and homeless adults, rather
  than adults currently standing inside the border.
- **The initial election window is one unpaused world day.** The existing
  council governs until a valid replacement is ready. During the first
  election, the all-adult council continues governing. The vacancy and small
  population rules above supply the all-adult fallback when needed. Election
  windows and terms are initial values to try, not immutable world laws.

Candidate and voter eligibility, turnout and ballot handling follow the
[agreed rules below](#election-eligibility-ballots-and-notices). Vacancy proposal
majorities, election retries and pending proposals follow
[Council changes and election scheduling](#council-changes-and-election-scheduling).

### Election eligibility, ballots and notices

**Agreed with the owner on October 1, answers 34A–39A:**

- **Any willing adult Town resident may stand for election and vote for
  themselves.** Homeless residents and travelers qualify through recorded
  membership. Standing requires the candidate's recorded agreement; another
  agent cannot make them a willing candidate by merely naming them. Use the
  [continuing candidate register](#council-changes-and-election-scheduling)
  rather than a separate nomination period.
- **There is no minimum election turnout, including for a runoff.** Each
  winner must have received at least one vote in the main election. If a full
  council of three eligible, willing, supported representatives cannot be
  formed, use the all-adult fallback. One voter can therefore elect three
  willing candidates, but a zero-vote candidate cannot gain a seat through
  a draw. A runoff with no new ballots can still end with the agreed fair draw
  among the previously supported, tied candidates.
- **Fix the eligible voter and candidate lists when voting opens.** Later
  arrivals, newly grown adults and late volunteers wait for a later election.
  If an agent dies or leaves recorded Town membership before that window
  closes, remove their eligibility, their ballot and any candidacy. Travel alone
  does not remove eligibility. A runoff starts with a fresh eligible voter
  list, but its candidates remain limited to the original, still-eligible tied
  candidates; it is not a new opportunity to nominate someone else.
- **Election ballots may change until the deadline; a council proposal vote
  is final once cast.** Count only the latest election ballot from each voter.
  Final proposal votes preserve early resolution once a result is settled;
  election ballots do not become final merely because a candidate leads
  before closing.
- **Agents learn about proposals and candidates through actual Town notices
  or another agent relaying information.** What they read or hear enters their
  ordinary personal-model context. Do not automatically inject unseen civic
  information into every resident's knowledge or invent candidate positions.
  A traveler can miss an election. Relayed claims remain what was communicated,
  rather than automatically becoming verified world facts.
- **When a selected candidate withdraws, remove that choice and keep the
  ballot's other choices.** The voter may revise the ballot during the
  remaining window. Withdrawal does not reset the deadline or require every
  affected voter to submit again. A ballot for Mira, Sol and Ash becomes a
  ballot for Sol and Ash if Mira withdraws.

These eligibility, ballot and delivery rules are settled. The remaining
ordinary process choices are agreed below; this completes
[#601](https://github.com/compoodment/ClankerWorld/issues/601)'s voting and
election interview.

### Council changes and election scheduling

**Agreed with the owner on October 1, answers 40A–42A, 43B and 44A–45A:**

- **A representative council still needs two yes votes while seats are
  vacant.** One remaining representative cannot approve an ordinary proposal
  alone. When the all-adult fallback governs, use a strict majority of its
  own adult membership instead of the representative council's threshold.
- **Close unfinished ordinary council proposals without passage when the
  council changes.**
  This applies to changes in eligible council membership, representatives or
  governing form. An agent may submit the proposal afresh to the current
  council, with fresh votes and a new one-unpaused-day window. Previously
  settled decisions stay settled. For example, an admission into an all-adult
  council changes its membership and closes other unfinished proposals.
- **Merge equivalent pending requests without resetting the deadline.**
  After failure or withdrawal, wait one unpaused world day before retrying the
  same request. Material new information or changed circumstances, including
  a changed council, allow earlier reconsideration. Merely rewording the same
  admission request does not restart its window.
- **Keep a continuing register of willing candidates; there is no separate
  nomination period.** Agents may nominate themselves or explicitly accept
  another agent's nomination through ordinary personal-model turns and actual
  notices. Regular voting opens one unpaused world day before the scheduled
  term ends. Initial or replacement voting can open immediately using the
  eligible registered candidates, subject to the one-contest rule below.
  Candidate and voter lists still become fixed when each voting window opens.
  Registration during that window is for a later contest. A cutoff runoff
  remains limited to its original eligible tied candidates.
- **Retry an unsuccessful election after one unpaused world day; materially
  improved circumstances allow an earlier attempt.** Use the continuing
  register for the new attempt, with no added nomination phase. A failed
  regular election does not shorten the incumbents' remaining term; after
  that term ends, use the all-adult fallback while retrying. An unfinished
  runoff retains the existing council under the agreed continuity rule.
  Candidate-related fallback may retry below eight residents while more than
  three adults remain. If representation ended because the population fell
  to three or fewer, elections still wait until eight adults again.
- **Keep one active election process per Town; the scheduled full election
  takes priority when its voting opens.** Stop unresolved replacement voting
  and use fresh ballots for the full election. Already decided replacement
  seats remain valid for the old term's remainder; do not undo them while
  cancelling an unresolved round for another seat. A vacancy arising during
  the full election is handled by that contest. Agreement to fill a short
  vacancy does not automatically mean agreement to stand for the next full
  term. With the continuing register, priority starts at regular voting, not
  at an invented nomination phase.

These rules complete the initial ordinary proposal and election process.
Law scope and the protected government-change process are agreed below.
The mayor's office is also agreed below; enforcement and other topics stay separate.
Implementation is tracked in
[#618](https://github.com/compoodment/ClankerWorld/issues/618), followed by
Town membership and admission integration in
[#602](https://github.com/compoodment/ClankerWorld/issues/602).

### Town law scope and changes

**Agreed with the owner on October 1, answers 46A–51A:**

- **Agents may propose open-ended social laws with a clear subject and
  scope.** Laws can concern shared work, resource use, businesses or public
  conduct, rather than being restricted to a prepared catalogue. Passing
  “do not cut trees in this grove” records a social rule; it does not make
  cutting physically impossible or introduce a new world power.
- **Local conduct laws apply to visitors within the Town's jurisdiction.**
  A visiting lumberjack is subject to the protected-grove rule without
  becoming a resident. Notice and ignorance follow the
  [nonviolent enforcement rules](#nonviolent-law-enforcement).
- **Territorial laws apply on the Town's formally claimed land, or a
  specified site within it.** A drawn border alone cannot extend authority
  over another claim. Household use plots can fall under Town law without
  making the household's tools or buildings Town property. Duties attached
  to residency must say so explicitly; territorial rules do not follow
  travelers everywhere. This does not decide land grant, transfer or
  adjudication powers beyond the already agreed ownership/use-right rules.
- **Amend or repeal a law through the ordinary council proposal and voting
  process.** Identify the affected law, record its replacement or repeal,
  and preserve its history. For the initial three-person council, two yes
  votes can repeal grove protection through an ordinary proposal. Changing
  the governing arrangement instead uses the resident approval rule below.
- **A change to the governing arrangement needs yes votes from more than
  half of all adult Town residents.** Representatives cannot replace
  elections with a permanent ruler using an ordinary council vote. An
  eight-adult Town needs five yes votes to establish a mayor or change its
  electoral arrangement. Silence supplies no approval. The voting roster,
  procedure and handover safeguards are agreed in
  [Government-change procedure and safeguards](#government-change-procedure-and-safeguards).
- **A new law applies from its recorded adoption onward.** Assess earlier
  conduct under the laws that existed then; do not create a new violation by
  applying a later law retrospectively. Protecting the grove today does not
  turn yesterday's tree-cutting into a new violation. Preserve law history so
  the applicable version remains identifiable.

These law-scope, visitor, amendment and prospective-application rules are
settled. A law remains separate from fixed physics, ownership and validated
action authority.

### Government-change procedure and safeguards

**Agreed with the owner on October 1, answers 52A–57A:**

- **Any adult Town resident may initiate a valid government-change proposal.**
  It does not need permission from the current council or leader. Officeholders
  cannot veto the resident vote merely to keep their positions. Homeless
  residents have the same eligibility as housed residents.
- **Voting lasts one unpaused world day and uses the adult resident list at
  opening.** Later arrivals and agents who become adults wait for the next
  vote. Death or departure from Town membership removes that resident's vote
  and reduces the electorate; travel alone changes neither. Approval requires
  yes votes from more than half of the remaining eligible opening list. Eight
  voters need five yes votes; if two leave the Town before resolution, the
  remaining six need four. Votes are final once cast, silence is not approval,
  and the vote can resolve early once its result is settled. Actual notices
  and communication supply awareness through agents' normal model turns.
- **Only one government-change process runs per Town, including its handover.**
  Merge equivalent proposals without resetting the clock; queue different
  proposals. Each subsequent process opens with a fresh electorate and fresh
  votes. A failed or withdrawn proposal has the ordinary one-day retry
  cooldown, unless a material change justifies an earlier retry.
- **The initial supported forms are an all-adult council, an elected council,
  and one elected leader.** A proposal must spell out who makes ordinary
  decisions, how officeholders are selected, their tenure and what happens
  when an office becomes vacant. More custom forms need their own design;
  ordinary law text cannot create them. Creating a role does not itself grant
  new land powers or authority to confiscate household property. The mayor's
  already agreed land-dispute role and election method remain applicable;
  its office rules are agreed below.
- **Approval starts a handover lasting at most three unpaused world days.**
  This is an initial trial duration. The existing government continues until
  a valid replacement is ready; the new form takes effect at that recorded
  handover, rather than immediately when the approval vote passes. Elective
  offices need willing, eligible officeholders selected through the agreed
  voting rules. If a mayoral vote ties, another vote between the tied highest
  candidates is required; do not use the council's random tie-break. If no
  valid replacement is ready by the handover deadline, cancel the attempted
  transition. An all-adult council consists automatically of eligible adults
  and does not require each resident to consent to taking a seat.
- **The resident government-change procedure stays protected under every
  supported form.** A new governing arrangement cannot abolish or change this
  route or its required resident majority. If no government can validly
  continue under the agreed continuity rules and no valid successor is ready,
  restore the all-adult council. Existing laws, Town membership
  and property records survive a government change or restoration. This does
  not revive a Town with no living residents or bring abandoned-Town work
  forward from its parked stage.

The initiative, vote, serialization, supported forms, handover and restoration
rules are settled. Law and government decisions are recorded through
[#619](https://github.com/compoodment/ClankerWorld/issues/619); land implementation
remains separate in [#426](https://github.com/compoodment/ClankerWorld/issues/426).

### The mayor's office and elections

**Agreed with the owner on October 1, answers 58A, 59A, 60B and 61A–63A:**

- **Start the first mayoral election promptly after residents approve creating
  the office through the protected government-change vote.** There is no
  minimum population. A four-adult Town can approve the office without waiting
  to reach eight adults. Authority begins only at a valid recorded handover;
  approval alone does not appoint a mayor.
- **Initially the mayor works alongside the council.** The mayor handles the
  already agreed land disputes and permission expiries; the council retains
  ordinary laws and admissions. Giving one elected leader those broader
  government powers requires an explicit protected government change. Land
  adjudication and ordinary governing authority are distinct mandates even
  when one person holds both; neither role silently grants the other, an extra
  council vote, or ownership of the disputed property.
- **A mayoral term initially lasts twenty unpaused world days.** Open renewal
  voting one day before expiry. Incumbents may stand again; early removal or
  replacement uses the protected resident process. A mayor seated on day 12
  serves until day 32, with renewal voting opening on day 31. The council's
  separate ten-day terms are unchanged.
- **The mayoral office becomes vacant on death, resignation, Town departure,
  or term expiry without a valid successor.** Travel alone does not remove
  Town membership or the office. Land decisions wait for a valid mayor; an
  eligible outgoing mayor does not continue beyond the term as a caretaker.
  Hold an election promptly and give its winner a fresh full twenty-day term,
  rather than the council's remainder-term replacement. The existing council
  continues ordinary government. If the ordinary governing leader's mandate
  is also vacant, use the protected all-adult fallback for ordinary decisions;
  ending a separate land-mayor mandate does not remove an independently valid
  governing leader. Lawful succession resumes the already approved arrangement
  without another government-change vote. Nobody inherits the office or
  becomes mayor automatically as runner-up.
- **Reuse council ballot handling with explicit mayoral rules.** Only adult
  Town residents vote or stand; homeless residents and travelers remain
  eligible. Use one-day rounds, the continuing candidate-register mechanics,
  fixed opening voter/candidate eligibility, departure/death removals, actual
  notices, withdrawal handling, self-voting and revisable election ballots.
  Willingness must specifically cover seeking the mayoral office; a council
  candidacy is not mayoral consent. Each voter chooses one candidate. There
  is no turnout minimum, but a winner needs at least one actual vote in the
  deciding round. Most votes wins; tied highest candidates face another vote,
  repeatedly if necessary, with fresh eligible voters and only the tied,
  still-eligible candidates. Never use the council's draw. Failed attempts
  retry after one unpaused day, or earlier following a material change.
- **Keep one active election process per Town, with scheduled full council
  elections taking priority.** Queue mayoral voting while that election runs.
  An interrupted mayoral round closes without a result and its unfinished
  ballots are discarded; resume with a fresh voting round and fresh eligible
  voters. Preserve any previously recorded mayoral top tie and its remaining
  eligible candidate field. The government-change handover's three-day clock
  keeps running through scheduling delays. If that transition expires and
  is cancelled, cancel its dependent mayoral contest too; do not later seat
  an office that was never validly established.

These choices settle creation, ordinary-government boundaries, tenure,
vacancies, ballot handling and scheduling. Land cases and wider nonviolent
enforcement are agreed separately below. Law, government-change and office
implementation is tracked in
[#631](https://github.com/compoodment/ClankerWorld/issues/631), after the ordinary
council and formal land-record foundations.

### Land hearings and rulings

**Agreed with the owner on October 1, answers 64A–69A.** The operating details
below were also settled under the owner's instruction to choose recommendations
for complete topics and report a short summary, rather than ask each choice.

- **Affected households, competing use-right applicants and the legitimate
  Town government may file a case.** Identify the plot, disagreement and
  requested outcome; an adult household member can file without committing
  the household to surrendering rights, waiving another adult's response or
  transferring goods. Town filings need its recorded governing
  authority. An agreed permission expiry opens a review automatically. Filing
  or supplying testimony does not itself grant rights. Equivalent filings join
  the existing case without resetting its clock.
- **Use sourced evidence, rather than an omniscient judge.** Recorded rights,
  agreements, applicable laws, submitted statements, witness accounts and actual
  observations may enter the case through legitimate inspection or communication.
  Distinguish allegations, observations and verified records. Preserve sources
  and the adjudicator's reasons; neither player inspection nor an agent's claim
  gives other agents knowledge of unseen events.
- **Give every affected party one unpaused world day from recorded formal
  notice publication to answer, initially.** Actual notices or relays supply
  awareness; publication does not imply receipt. Early closure needs actual
  answers or explicit waivers from all affected household adults and the Town's authorized
  representative where it is a party. A household without an eligible
  representative has not waived its response, and adverse use-right changes
  wait until it has a legitimate adult representative. Preserve its current
  or provisional rights; this does not invent new guardianship. Silence is
  neither consent nor proof. A new affected party or material change to the
  rights at issue needs a revised notice and a full fresh day before a ruling.
  Repeated wording and duplicate filings do not create a new window. A traveling, represented party
  can miss the published deadline; absence alone is not evidence against them.
- **Rulings may confirm, renew, alter or end use permissions under applicable
  law, including choosing between competing households despite an objection.**
  Record the authority, evidence and exact bounded change, and validate it
  against the current plot and rights. Expired permission remains provisional
  until a valid ruling; conflicting formal transfers remain blocked. Preserve
  Town title, household membership, private buildings, owned crops and goods.
  A ruling neither physically evicts occupants nor grants private-building
  access. Consensual household transfers retain their ordinary agreed route.
  Where the evidence does not support changing rights, preserve the current
  valid record and reject the unsupported request rather than choose an
  arbitrary winner.
- **A conflicted mayor stands aside for that case.** This includes their own
  household, a direct personal stake, or acting as a party or its representative.
  Shared Town membership alone is not a conflict. Elect a willing, eligible
  adult Town resident without that conflict as acting mayor for this case only,
  using the agreed mayoral voting rules and election scheduler. Candidacy
  consent must cover this case; the role gives no general leadership powers
  and ends when the case closes or eligibility, willingness or its authorized
  mandate ends. Regular mayor succession does not replace a valid acting judge
  mid-case. If a replacement is needed, preserve the file and completed response
  periods. Affected adults retain their ordinary voting rights; recusal restricts
  adjudicators. If nobody qualifies, the case remains pending.
  Two disputing founding households may therefore have no independent adult
  available. The council cannot quietly appoint itself as judge.
- **A settled case can reopen only for material new evidence or a demonstrated
  procedural error.** The valid, non-conflicted adjudicator assesses the grounds;
  an allegation alone is not a demonstrated error. Preserve the old ruling and
  history, include current affected right-holders, and hold a fresh hearing.
  Reopening does not roll rights back: keep the current valid record until a
  new bounded correction is adopted. Disagreement or a new mayor alone does not
  justify rehearing. A successor inherits pending files and completed notice
  periods rather than restarting every case. They read the case through
  authorized access rather than inherit the previous agent's private memories.

Nonconflicting new grants on existing Town-owned land use ordinary legitimate
Town approval and beneficiary-household acceptance; they need no routine mayor
hearing. Voluntary grants or transfers require explicit agreement from the
current adult members of each household whose use rights are granted or
surrendered, with at least one legitimate adult signatory per household.
Silence is not agreement; filing alone supplies none. Existing
automatic starter allocations remain the setup exception. A disputed change
uses the lawful hearing route instead of pretending every party consented.

Pending cases, conflicts, responses, evidence, notices, rulings and corrections
are durable Town records. Revalidate current adjudicator authority and conflicts
before resolution. Without a valid adjudicator, keep the case pending and its
existing safeguards; do not invent a winner or erase it after save/reload.
Implementation follows the formal rights and mayor foundations in
[#633](https://github.com/compoodment/ClankerWorld/issues/633).

### Nonviolent law enforcement

**Decided on October 1 under the owner's delegated design authority.** This
settles the initial nonviolent enforcement process. Physical enforcement and
combat consequences belong to the later combat stage.

- **A violation becomes known through observation, inspection or communication.**
  A witness, affected party or Town government may report a specific act,
  location, time, applicable law and available evidence. Reports remain
  allegations until assessed. Keep the actual world event separate from an
  adjudicator's finding: agents and courts can be mistaken without rewriting
  what happened. Agents use ordinary personal-model turns; add no paid polling
  or automatic awareness of hidden violations.
  Investigators may inspect public records, ask witnesses and observe accessible
  sites. Private buildings, stock and memories retain their existing access
  and consent rules; the office grants no general search power.
- **Law notices matter, but ignorance does not repeal the rule.** Publish laws
  and provide actual notices at relevant sites and ordinary contacts. Apply the
  law and jurisdiction in force when the act happened. For a first incident
  without credible evidence of prior notice, initially favor explanation and a
  warning; proven harm may still warrant a request to restore it. A claim of
  ignorance is evidence to assess, not automatic immunity or proof of knowledge.
- **Non-land adjudication needs its own resident-approved mandate.** The land
  mayor does not automatically gain these powers. Extend the elected office
  through the protected government-change process, with the scope explicit and
  the officeholder's consent to added duties. Keeping the same willing holder
  does not reset their term. Before valid handover, the council may mediate and
  offer advice, while formal enforcement cases remain pending. Reuse the land
  hearing, evidence, notice, recusal and reopening rules for this mandate.
- **Record a reasoned finding before an adverse formal consequence.** Require
  the available evidence to make the violation more likely than not, as the
  initial civil standard, with reasons and uncertainty recorded. Silence, rumor
  alone or refusal to confess is insufficient. An unsupported accusation closes
  without an adverse finding and may reopen on material evidence; lack of proof
  does not itself prove that the reporter lied. The game validates authority,
  procedure and supported action scope, rather than turning the judge's opinion
  into an omniscient world fact.
- **Initial consequences are explanation, warning, recorded censure and requests
  for restitution, repair or voluntary public service.** State who is asked to
  do what and why. Initially allow one unpaused world day from the recorded
  offer notice publication to accept, decline or counteroffer, with actual
  communication supplying awareness. A restorative agreement needs the responsible adult's
  explicit consent, feasible named work or goods, and a recorded deadline;
  initially offer completion within three unpaused world days after recorded
  acceptance, with another feasible period by agreement. Ordinary spoken
  promises remain outside this formal agreement record. Return or repair uses
  real goods, ownership, carrying and work rules; no automatic debit,
  teleportation, forced labor or replacement goods appear.
- **Track actual compliance without multiplying penalties.** A declined or
  unanswered voluntary offer is not a new offense; record those states separately.
  Accepted work can be pending, completed or overdue; inability to perform
  permits renegotiation rather than automatic escalation. Merge reports of the
  same act and law into one case.
  Repeated distinct conduct can support a new case, with fresh evidence and
  hearing. A finding can inform agents who actually learn it, but grants no
  universal reputation score or compulsory boycott.
- **Keep civic and household safeguards intact.** This initial process does
  not expel Town residents, remove officeholders, confiscate private or shared
  stock, cut resident Warehouse access, detain people or separate care groups.
  Office changes use the protected government route; land permission changes
  use the authorized land case. Caregiver duties, personal retrieval rights,
  reservations and dependent needs still constrain any voluntarily agreed work.
  Children get caregiver-supported notice and responses, with explanation and
  restorative offers appropriate to their normal abilities. Kinship or shared
  housing does not make another person liable; adults consent to work or goods
  they contribute rather than being charged for a child's act automatically.

Visitors may be reported for local conduct inside jurisdiction, with the same
notice and hearing safeguards. Requests made to them do not create Town
membership or reach into another Town's title. Cross-Town enforcement, money
fines, detention and physical coercion need their later designs.
Implementation of this initial process is tracked in
[#635](https://github.com/compoodment/ClankerWorld/issues/635).

### Still to decide

Town residency and newcomer approval are agreed in [Town membership](#town-membership).
Election terms, ties, replacements and population transitions are agreed in
[Election terms and council continuity](#election-terms-and-council-continuity).
Eligibility, ballots, election roster changes and delivery are agreed in
[Election eligibility, ballots and notices](#election-eligibility-ballots-and-notices).
Ordinary proposal changes, candidate registration, retries and election overlap
are agreed in [Council changes and election scheduling](#council-changes-and-election-scheduling).
Law scope, visitor obligations, repeal and prospective application are agreed
in [Town law scope and changes](#town-law-scope-and-changes).
The protected resident vote and handover are agreed in
[Government-change procedure and safeguards](#government-change-procedure-and-safeguards).
Mayor creation, terms, vacancies and elections are agreed in
[The mayor's office and elections](#the-mayors-office-and-elections).
Land case procedures are agreed in [Land hearings and rulings](#land-hearings-and-rulings).
Initial discovery, hearings and consequences are agreed in
[Nonviolent law enforcement](#nonviolent-law-enforcement).
Still open: taxes, inheritance, interaction between conflicting Towns and
later physical enforcement. Agents should know only laws or violations they
have learned about in-world.
Law and government choices in
[#619](https://github.com/compoodment/ClankerWorld/issues/619) are settled;
land case choices in [#630](https://github.com/compoodment/ClankerWorld/issues/630)
are also settled.

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
| Cloth | Tailor Shop; trial 3 fiber → 1 cloth. | Clothing, sacks, bandages and later book covers. |
| Rope | House crafting; trial 3 fiber → 1 rope. | Sacks, carts, boats and later construction recipes. |
| Clay | Dig a clay bank and carry clay. | Fired storage pots and water jugs. |
| Pottery | House crafting; trial 2 clay + 1 wood → 1 vessel. | Reusable storage pots and water jugs, with separate item identities. |
| Fresh water | Collect from a river or lake with a reusable jug and carry it. | Cooking, medicine-making and animal care. |
| Gold | Extract from a gold-bearing outcrop with an iron pickaxe and carry to the Blacksmith. | Ornaments, gifts and trade goods. |
| Diamond | Extract from a diamond-bearing outcrop with an iron pickaxe. | Trade goods or a stone set into an ornament. |

The Blacksmith makes gold ornaments, optionally set with a diamond. Agents may
keep, wear, gift or sell them. This is the agreed destination for rare materials;
it does not select gold as a universal currency or add a diamond tool tier.

A storage pot slows spoilage for the food within its limited capacity. A water
jug holds a limited quantity of water. Both remain after their contents are
used; filling, emptying and carrying them must preserve the vessel and its goods.

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

The current equipment trial allows 8 cargo units without an aid, 16 with a
basket and 24 with a sack. The equipped garment and aid each occupy their own
slot; other goods, delivery loads and vessel contents count as cargo. Stored
or ground goods do not count as carried. Broken aids give no extra capacity.
When an aid breaks or is replaced, excess cargo and the old aid stay real
property; the agent must make room before collecting more.

The House trial uses 3 fiber for a rope and 3 fiber plus 1 rope for a basket.
The Tailor trial keeps 3 fiber per cloth and 2 cloth per basic garment, uses
4 cloth per padded coat, 2 cloth plus 1 fiber per rain cloak, and 2 cloth plus
1 rope per sack. Only the equipped garment protects against weather, with
less protection as it wears. Garments wear during exposure and carry aids
wear during travel with cargo. Adult household members repair garments and
sacks at their Tailor Shop with 1 cloth, or baskets at their House with
1 fiber and 1 rope. Eight work ticks restore 60% condition, up to full
condition. Interrupted repairs release unspent materials. These quantities,
capacities, wear rates and repair effects remain provisional.

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

**Agreed on October 1, 2026:** a seller may use any empty Market stall and keep
it until they leave. The Town does not assign stalls. Leaving frees the stall
for another seller; goods keep their recorded owner and must be
carried away or transferred through an actual trade.

Initial trade uses barter offers naming exact goods and quantities. Both sides
bring their goods to the transaction. The buyer receives purchased goods
personally; payment becomes seller-household stock at that location. Later
agent-created currencies use the same transaction system, with issuer and
acceptance rules designed alongside governance. No universal starting money
or gold standard is chosen.

Ownership and physical location remain inspectable across every transfer.
Production reserves actual inputs and output space. Full storage, missing
materials, unavailable tools and blocked transport give a readable reason.
Guests and customers gain only the access their invitation or transaction
grants. A Store cannot sell goods from a remote House, farm or Warehouse.

## Questions linking these systems

These remain open; they are not new decisions.

- **Later law and property interactions.** Adoption, law scope, land hearings
   and initial nonviolent enforcement are agreed above. Agent-created laws
   remain breakable while the simulation protects physical facts and validated
   ownership changes. Cross-Town enforcement, conflicting inheritance rules
   and later physical responses to illegal occupation still need their designs.

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
    footprint table above. The other chosen building
    footprints are in the accepted roster above. Decide storage capacities, costs,
    other expansion triggers and Port clearance details.
