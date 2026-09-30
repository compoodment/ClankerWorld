---
title: Agents, families and social life
type: game-design
status: active
updated: 2026-09-29
---

# Agents, families and social life

These are the intended game rules and choices. They do not describe
everything that is available in the current build. See [what works today](../what-works.md).

[Game-design guide](README.md) explains the agreement labels.

## On this page

- [Starting agents, families and life stages](#starting-agents-families-and-life-stages)
- [Thinking, memories and social life](#thinking-memories-and-social-life)
- [Combat](#combat)
- [Questions linking these systems](#questions-linking-these-systems)

## Starting agents, families and life stages

### Agreed

- A civilization-oriented new world first generates its map without agents.
  During paused setup, the player chooses a rough site and Town generation
  creates the **first Town** with its initial border and buildings. The player
  adds and configures **four biologically unrelated starting agents** using
  each agent's model and key setup. They are grouped 2+2 into two
  starting households, not forced couples; family lines develop later through
  relationships and children. All four begin as agents of the first Town.
  The simulation cannot begin until this setup is complete. The exact order
  of site choice and agent configuration remains open.
- Once all four starting agents are configured and placed, the player explicitly
  presses **Start World**; the simulation must not begin automatically on the
  fourth placement. An incomplete starting-agent setup is saved so the player can
  quit and finish it later. Before Start World, the game should clearly show
  progress toward the required four agents. During this paused setup, agent
  placement can be moved or undone. They should start near the first Town's
  buildings; exact clustering, site-change controls, and prior 8/12-tile
  playtest radii must be revisited with the generated layout.
- Each agent independently has a chosen provider/model, private memory and
  context, goals/personality, call schedule, usage, and failure state. Different
  agents may use the same stored key. Agents choose their own personality,
  aspirations, skills, and initial identity. The player can add adults freely.
- Agents have no genders. Children have **two parents**. Parents choose the
  child's name and provider/model, and must choose **one of their own surnames**
  as the child's surname. Children inherit tendencies, abilities,
  culture, and provider/model settings; baby appearance uses baby art. Close
  biological relatives cannot pair.
- Agents can become a couple and then marry, but **a couple may have a baby
  without marrying first**. Marriage is not a birth requirement. When agents
  marry, both partners must share one of their existing surnames, chosen by them
  in a dedicated conversation. That
  conversation is initiated even if they are far apart in the world: a narrow
  exception to ordinary proximity-bound conversation, not a general remote
  communication ability. A completed marriage must not be left without a
  shared surname. The conversation must be bounded; if the partners do not
  agree within that bound, a disclosed tie-break rule selects one of their two
  surnames so the marriage can finalize. The tie-break method, turn limit, and
  provider-failure behavior remain open. Unmarried parents may have different
  surnames, so their child's surname choice is meaningful; married parents
  already share one surname, so either parent's surname is the same choice.
  The low-population continuity rule may encourage marriage but must not make
  it a prerequisite for having a child.
- **A child uses their own selected personal AI model once they leave infancy.**
  Infants do not make calls to their personal model. The parents' provider/model
  choice can be stored at birth, then used when that agent enters the child
  stage. This resolves the earlier open question about *whether* children use
  their own models; initial age-up thresholds are below. Jev must not be
  required for infant care, because Jev is optional per world.
- **Children are real social agents, not silent placeholders.** Their personal
  models can converse, play, learn, form friendships, and choose age-appropriate
  simple helping tasks. Adult-only decisions such as land deals and parenthood
  wait until adulthood. The world system, not merely a model prompt, enforces
  those age-based permissions.
- **Current lifespan anchor:** an agent can live no more than **six hours of
  unpaused world time from birth**. They may die earlier. This is a maximum,
  not a promise that everyone dies at exactly six hours or that adding an
  already-adult agent grants six further hours. Detailed stage effects and
  variation in age at death remain open; pacing must be playtested.
- **Death and inheritance:** by default, a deceased agent's personal belongings
  pass to their household. Computment wants the deceased agent's own model to
  be explicitly told that the agent has died and then produce a final will or
  inheritance instruction **after death**. This is part of the intended first
  complete game, not a post-launch feature. It is a final estate decision, not
  the dead agent resuming ordinary physical actions. A valid will or later
  established inheritance rule can change the default household distribution.
- Deceased agents remain **inspectable to the player**, including through the
  interactive family tree. Their agent popup becomes a historical profile with
  their saved thoughts and memories, age/circumstances of death, and final will
  when available. It does not generate ongoing new thoughts or offer live
  provider/model controls; the exceptional post-death will turn above is not
  ordinary continued life. **Memories do not transfer to descendants at death.**
  Preserving them for player inspection does not make them known to living
  agents. A child learns only what they were told, taught, read, witnessed, or
  later discovered. For example, a hidden tool's location remains unknown to
  the child if the parent never shared or recorded it.
- Parenthood normally requires consent and is optional when civilization is
  secure. At low population, this world has an explicit **continuity rule**:
  refusal is not fully autonomous, preventing civilization from ending solely
  because models decline reproduction. The game must communicate that rule
  honestly rather than claiming unrestricted consent. A simple population
  threshold may be the first implementation, but the finished system should
  assess **continuity risk**—eligible unrelated adults, family lines, children,
  expected deaths, and care/resources—not just count heads.

### Agreed starter Town and remaining choices

The player chooses a **rough site for the first Town** in New World, with the
best suitable areas shown as **guidance, not the only allowed locations**.
Fertile land, reachable wood and stone, and short connected Roads make a site
more suitable; lack of an ideal score does not forbid the player's choice.
Water or another physically impossible location cannot hold the Town. Within
the chosen rough site, Town generation automatically picks a feasible layout
for **two Houses, a Warehouse, a Farmhouse and a Blacksmith**, connected by
generated Roads. If no legal five-building layout fits, explain the problem
and let the player choose another site without committing a partial Town.
The player can change the rough site before placing founders; there is no
separate layout Accept/Redo step. Clothing-making place and Workshop are not
guaranteed starters; agents may develop them later.

The Town automatically assigns the Farmhouse and Blacksmith to the two
starting households, one productive building each. The player does not choose
which household gets which business; this assignment does not make the agents
couples or biological relatives. Each House starts with **eight food portions**,
and the communal Warehouse starts with at least **one usable wooden axe and
one usable wooden pickaxe**. The player may choose optional additional supplies
in New World; their menu and quantities remain open. Exact suitability weights,
how far to search around the chosen site, site-change controls, and whether a
nonguaranteed building can appear at start also remain open. The
[planned game content](content-list.md) records the accepted
base tool/item/food/object/art set and explicitly marked remaining choices;
the [what works today](../what-works.md) describes the playable prototype.

### Still to decide

Expected/variable lifespan below the six-hour cap, starting agents' ages,
the exact UI for assigning the first four agents to the two starting
households, the detailed limits on child tasks and elder capabilities, whether
older childhood needs a separate phase, age display, relationship and
inheritance mechanics, continuity threshold and exit conditions,
pregnancy/birth and childcare rules, care/resource eligibility, Jev's
optional role in childhood, and the identity implications of player renaming.
First-Town placement details remain open: its order relative to agent
placement, the local search extent, the placement boundary's exact distance
measurement near tile/footprint edges, whether geometric proximity also
requires a walkable route, and how changing site revalidates any positions or
claims tied to the previous layout without clearing model/provider choices.
Warehouse details remain open: formal ownership, how Town residency is
determined, access when the building is outside all Town borders or borders
change, and how its hard access gate relates to any future crime system. The
optional survival grace period still
needs a duration and precise effects. Also open:
whether unrelated newcomers can arrive without player action or are only
introduced through Add Agent. A configurable automatic-birth limit was
considered, briefly accepted, then explicitly
reopened: computment will decide **after playtesting actual birth frequency,
population growth, and model cost** whether a cap belongs in the game. Do not
assume a cap or its precedence over the continuity safeguard before that
decision.
Computment briefly considered removing the close-relative pairing ban, then
retracted that thought; the ban still stands. A model-usage meter and optional
AI-usage limit have since been accepted; neither is a population cap.

For inheritance, still open: the exact final-model-turn contract; which assets
are personal versus already household-owned; conflicts between a will and
agent-made law; minors, multiple heirs, debts, and no-household cases; and
whether a final message beyond the will is part of the death event. Clanker's
proposed safety rule is to freeze the estate at death, validate the model's
instructions against real ownership/law, and apply the household default if
the model is unavailable or gives no valid instruction. Computment said this
flow feels right; treat it as accepted direction, while timeouts, conflicts,
crash recovery, and exact transfer rules still need design.

### Life stages to try in playtesting

Using the current six-real-minute day and 60-world-day maximum lifespan:

| Stage | World age | Unpaused time since birth | Own model? |
|---|---:|---:|---|
| Infant | day 0 to before day 3 | 0–18 minutes | No |
| Child | day 3 to before day 15 | 18–90 minutes | Yes, age-appropriate options |
| Adult | day 15 to before day 45 | 1½–4½ hours | Yes |
| Elder | day 45 to before day 60 | 4½–6 hours | Yes |

An agent may die earlier; these are stage thresholds, not scheduled death
times, and the thresholds can change after playtesting. An adult added to the
world starts at a nonzero age. Because adulthood precedes the first 40-day
birthday, Clanker recommends displaying age in world days plus life stage,
not only whole years; this UI choice is not yet settled. A separate adolescent
stage and exact capabilities are still open, but are not assumed to require
another sprite family at first.

## Thinking, memories and social life

### Agreed direction and playtest choices

- Early conversation is face-to-face and proximity-bound; agents may later
  invent long-distance communication. Agents should chat **regularly in
  context** without socializing to the exclusion of work and life.
- Personal models generate meaningful dialogue and decisions about trade,
  buying/selling, organizing, land, invention, relationships, and other goals.
  Agents can lie, misunderstand, gossip, keep secrets, and have differing
  knowledge. **Private thoughts and spoken dialogue are distinct:** another
  agent learns something only if it is said, observed, or otherwise conveyed
  in-world; inspecting a thought as the player does not broadcast it to anyone.
- A conversation is a real joint activity with a reason, turns, and a chance to
  conclude, disagree, withdraw, or postpone. Ordinary job scheduling should
  not cut it off mid-sentence. Danger or urgent needs may interrupt; a bounded
  final wrap-up round can avoid endless looping. If unresolved, say so rather
  than fabricate agreement, and allow later resumption. The marriage-surname
  conversation above is a special case: it must be started even across the
  world, must be bounded, and a marriage cannot complete with its surname
  undecided. Loop protection is accepted as a design direction, **not yet a
  specified or implemented turn limit**.
- Socializing agents display a chat bubble above their sprites. Clicking opens
  a nearby popup with a summary; expanding reveals the complete conversation.
- Jev may notice social moments, retrieve memories, summarize, or route cheap
  routine decisions while each agent retains its personal AI model. Jev is optional
  per world, not individually toggled per agent. The player can **turn Jev on
  or off in an existing world**; the choice is not locked at world creation.
  Disabling it must not erase existing agent memories or make the world depend
  on Jev to continue functioning.
- Each agent remembers what they experienced or were told, not everything the
  player can see. Minor details may fade or be misremembered; major life
  events, relationships, learned skills, and unresolved commitments should
  remain dependable. An agent's belief can be wrong without changing the
  simulation's record of what actually happened. Neither family relation nor
  another agent's death grants access to that person's private memories.
- **Leaning toward memory architecture:** when enabled, Jev can help compact many
  experiences into shorter memories and retrieve relevant ones for that
  agent's next decision. It must preserve who witnessed or said something,
  distinguish firsthand events from rumors and uncertain beliefs, and never
  expose one agent's private memories to another. Jev does not replace the
  agent's personal model or become mandatory for memory to work. With Jev off,
  the game still needs a functioning memory/retrieval path.
- Fully generated languages/dialects are **deferred**, not planned now, due to
  uncertain gameplay value and token cost.

### Still to decide

Conversation frequency and token/turn budgets, group-planning mechanics,
interrupt/resume behavior, memory importance/retention rules, compaction
triggers and fallback method without Jev, and Jev's exact role
need implementation and playtesting. The proposed lifecycle is accepted as a
direction, not proof that it will feel right in the finished game.
The September 29 external code review raised additional **questions, not
decisions**: whether personality is fixed at creation or changes through
experience; whether a model sees numeric needs or descriptive bands; how
routine and dialogue budgets are split; whether each participant's own model
speaks its own conversation turns; and whether spoken promises become tracked
commitments whose fulfilment or breach can be observed. Resolve these against
the accepted per-agent identity, truth, information sources and spending rules
before specifying a conversation-call protocol. The review's proposed schemas
and token settings are implementation options, not agreed game design.

## Combat

### Agreed

- Interpersonal combat belongs in the finished game for **self-defense, crime,
  personal feuds, and organized war**. Hunting and sport/duels were not chosen
  in that discussion.
- Combat can turn lethal, but agents should not be dying constantly. Personal
  models choose intentions; the game rules resolves reach, movement,
  timing, protection, injury, and outcomes. Health already exists in the
  prototype, but the integrated game **does not yet have a combat loop,
  weapons, armor, combat injuries, or combat AI**.

### Leaning toward

Weapons and armor should fit the general item/equipment/crafting/invention
system rather than be a disconnected special system. An axe could be both a
work tool and a weapon; armor could be wearable protective equipment. Combat
still needs its own actions and balancing. Computment's agreement here was
qualified (“if I get what you mean”).

### Still to decide

Injuries, medicine, escape/surrender, law enforcement, war declarations and
peace, lethality balance, combat equipment progression, and whether animal
slaughter or hunting ever becomes part of the game.

## Questions linking these systems

These remain open; they are not new decisions.

- **Population continuity and ancestry.** Four unrelated starting agents in two
   households, a close-kin pairing ban, and a low-population safeguard may
   eventually leave no eligible unrelated adults even if everyone wants
   children. Computment now prefers player-controlled addition of unrelated
   adults, with placement seeding household/Town membership. Decide
   explicitly whether those additions are the **only** source of unrelated
   newcomers or whether autonomous arrivals also exist; define exactly how
   close is too close. Whether automatic births need a configurable limit is
   deferred until measured playtesting, rather than part of the current design.

- **No fictional population cap versus finite provider cost.** Each agent's
   personal model is central, including child agents; frequent socializing
   and unlimited player-added adults increase call volume. Measure calls,
   tokens, latency, and cost in representative play before choosing decision
   cadence, budgets, and how resource pressure is shown to the player. This is
   a scalability constraint, not permission to replace agents with Jev.

- **Marriage-surname procedure.** Unmarried couples may have children, and
   the parents choose one of their surnames for the child. The continuity rule
   may encourage marriage but cannot require it for birth. Still decide the
   bounded marriage-surname conversation's tie-break method, turn limit, and
   provider-failure behavior.
