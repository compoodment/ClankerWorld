---
title: Agents, families and social life
type: game-design
status: active
updated: 2026-10-01
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
  each agent's model and key setup. They are grouped 2+2 into two starting
  households, not forced couples; family lines develop later through
  relationships and children. All four begin as residents of the first Town.
  The simulation cannot begin until this setup is complete. **Agreed on
  October 1 ([#653](https://github.com/compoodment/ClankerWorld/issues/653)):** the
  player chooses the Town site first, then places the founders among its
  buildings. The site can change only while no founder is placed; to move it
  later, remove the founders first. Every founder needs a personal model and a
  working key before Start World
  ([#638](https://github.com/compoodment/ClankerWorld/issues/638)).
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
  aspirations and initial identity. Agents start with no skills; an agent first
  gains a skill by finishing that kind of work. The player can add adults freely.
- An agent chooses its personality and aspiration **once, at its first
  decision**. **Agreed, with provisional timing and limits for playtesting:**
  it can change them later only at moments the game names: a
  midlife point (around day 30 of a life of up to 60 world days), becoming a
  parent, losing a partner or a parent, and becoming an elder. Revisions are
  capped at one opportunity for each kind of moment, up to five extra requests
  per life. The game decides when one of these moments comes; the agent decides
  what changes. It may keep either or both current choices. A missing, invalid
  or interrupted reply leaves the current identity alone and is not retried
  automatically. A name change is separate from these opportunities.
- **Two agents in one world cannot share an exact full name.** If a model
  chooses a name that is already taken, the game asks that agent once more,
  which costs one extra request that counts toward model usage. If that also
  fails, the agent keeps a placeholder name that the player can change. Similar
  but not identical names are allowed, such as two agents called Rowan with
  different surnames. The comparison covers every agent in the world,
  including those who have died. A valid name is kept even when the action in
  the same reply is rejected. **Leaning toward (provisional; tuned in
  playtests):** to make names more varied, each agent's naming request suggests
  a fixed starting letter for that agent. In a test on one model this raised
  the number of different first names from 8 to 15 without extra requests.
  [Player renaming](interface-and-art.md#player-guidance-and-orders) uses the
  same full-name check and preserves the person's identity and historical text.
- Agents have no genders. Children have **two parents**. Parents choose the
  child's name and provider/model, and must choose **one of their own surnames**
  as the child's surname. Children inherit tendencies, culture, and
  provider/model settings, but never skills: like every agent, a child starts
  with no skills; baby appearance uses baby art. Close
  biological relatives cannot pair.
- **Who counts as a close relative, agreed on October 1
  ([#643](https://github.com/compoodment/ClankerWorld/issues/643)):** parents
  and children, grandparents and grandchildren at any depth, full and half
  siblings, and aunts or uncles with their nieces or nephews cannot become
  partners or have a child together. First cousins and more distant relatives
  may pair. Only biological parentage counts, not a shared household or
  caregiving.
- Agents can become a couple and then marry, but **a couple may have a baby
  without marrying first**. Marriage is not a birth requirement. When agents
  marry, both partners must share one of their existing surnames, chosen by them
  in a dedicated conversation. That
  conversation is initiated even if they are far apart in the world: a narrow
  exception to ordinary proximity-bound conversation, not a general remote
  communication ability. A completed marriage must not be left without a
  shared surname. The conversation has at most **four alternating turns**,
  two per partner, each using that agent's selected model. It may choose only
  the partners' two existing surnames. If valid choices still disagree after
  the limit, a disclosed, seeded deterministic tie-break selects one of those
  surnames. This resolves the surname only: marriage consent and other
  prerequisites must be checked independently, never inferred from a
  tie-break. A failed model call, pause or cancellation suspends the
  conversation without consuming a turn. Its participants, original surnames,
  completed turns and next speaker must survive save and resume; paid calls
  still require an active player. Unmarried parents may have different
  surnames, so their child's surname choice is meaningful; married parents
  already share one surname, so either parent's surname is the same choice.
  The low-population continuity rule may encourage marriage but must not make
  it a prerequisite for having a child.
- **A child uses their own selected personal AI model once they leave infancy.**
  Infants do not make calls to their personal model. The parents' provider/model
  choice is recorded at birth, then used when that agent enters the child stage.
  This resolves the earlier open question about *whether* children use
  their own models; initial age-up thresholds are below. Jev must not be
  required for infant care, because Jev is optional per world. If the two
  parents choose different models, the **initiating parent's model** is used,
  as a disclosed tie-break. If the chosen model has no working key, the child
  shows "model needs setup" and idles safely. The game never substitutes a
  different paid model. The owner can later select another personal model or
  leave the child without one; an explicitly unconfigured child keeps using
  built-in choices and does not inherit the world's default provider.
- **Children are real social agents, not silent placeholders.** Their personal
  models can converse, play, learn, form friendships, and choose age-appropriate
  simple helping tasks. Adult-only decisions such as land deals and parenthood
  wait until adulthood. The world system, not merely a model prompt, enforces
  those age-based permissions.
- **Current lifespan anchor:** an agent can live no more than **six hours of
  unpaused world time from birth**. They may die earlier. This is a maximum,
  not a promise that everyone dies at exactly six hours or that adding an
  already-adult agent grants six further hours. Detailed stage effects
  remain open; pacing must be playtested.
- **Starting ages and deaths, agreed on October 1
  ([#642](https://github.com/compoodment/ClankerWorld/issues/642)):** each
  founder and each adult added later arrives at a seeded age between **day 15
  and day 25**, so founders reach old age at different times. Nobody dies of
  old age before day 45. From day 45 a daily chance of death starts at 1% and
  rises by a quarter of a point each day, and death is certain at day 60, so
  about two agents in three reach day 60. These numbers are provisional;
  earlier deaths can come from illness and injury once those exist.
- **Death and inheritance:** by default, a deceased agent's personal belongings
  pass to their household. Computment wants the deceased agent's own model to
  be explicitly told that the agent has died and then produce a final will or
  inheritance instruction **after death**. This is part of the intended first
  complete game, not a post-launch feature. It is a final estate decision, not
  the dead agent resuming ordinary physical actions. A valid will or later
  established inheritance rule can change the default household distribution.
- **What a will may do, agreed on October 1
  ([#645](https://github.com/compoodment/ClankerWorld/issues/645)):** a will
  may leave the estate to the household default, or name **up to three** living
  heirs or the Town, item by item or in equal shares. Children can inherit;
  their goods stay where they are stored, as their personal property. The dying
  agent may also leave **short final words**. The people who inherit hear them
  when the will is read and keep them as a memory, such as "Rowan Hale's final
  words were: 'Keep the orchard going.'" The player sees them on the historical
  profile. Nobody else learns them unless they are told in the world. The will
  still uses one model request.
- **A guardian for a child whose last caregiver dies, agreed on October 1
  ([#644](https://github.com/compoodment/ClankerWorld/issues/644)):** the game
  asks, in order, the child's living relatives (grandparents, adult siblings,
  aunts and uncles) wherever they live, then the adults in the child's
  household, then the adult residents of the child's Town. The first adult who
  accepts becomes the child's caregiver. Nobody becomes a guardian without
  accepting. The child moves only into a home with a free resident place under
  the [House capacity rules](towns.md#house-resident-capacity-and-relocation).
  Until someone accepts, the child stays at home, nearby adults may still feed
  them, and the child's card and the Event Log show **Needs a guardian**, so
  the player can suggest or order an adult to take them in. How long each group
  is asked is provisional.
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
- Before a parenthood plan advances, the initiating parent nominates the
  primary caregiver and intended household. The other parent explicitly accepts
  one named caregiver-and-home option; either parent may be chosen, including
  when they currently live in different households. The saved choice cannot
  delay or cancel a due birth if that caregiver later changes households: the
  child joins the caregiver's actual household at birth, while the other
  parent's residence stays put. A full House does not block the birth; it
  becomes visibly overcrowded instead. The caregiver and birth home are saved
  with the plan and birth record. Other pregnancy timing and detailed childcare
  rules remain open.
- The birth record keeps the caregiver chosen for that birth as history. The
  child's current primary caregiver remains that person while they are active.
  A second accepted caregiver does not take over or change the child's domestic
  family group by itself. If the current primary caregiver is no longer
  available, an explicit care assignment may make an already accepted caregiver
  the new primary; the child then follows that caregiver's current domestic
  family group for housing counts without changing residence or parentage.
- **The first continuity rule, agreed on October 1
  ([#654](https://github.com/compoodment/ClankerWorld/issues/654)):** the rule
  is on while the world has **fewer than eight living agents who are not
  elders** (infants, children and adults), twice the founding four. It is on
  from the start and turns off as children arrive. While it is on, a partnered
  couple with no infant may say "not yet" to a child for up to **two world
  days**, but may not refuse; then the plan goes ahead. Choosing a partner
  stays voluntary. The couple's model requests explain the rule, and the Event
  Log notes when it turns on and off. The numbers are provisional, and the
  risk-based check above comes later.
- **Newcomers, agreed on October 1
  ([#655](https://github.com/compoodment/ClankerWorld/issues/655)):** unrelated
  adults arrive only when the player adds them with Add Agent. While the
  continuity rule is on, the Event Log says the world is at risk and offers
  **Add a newcomer**, which opens Add Agent. Travellers arriving on their own
  may be reconsidered later if worlds feel too closed.

### Agreed starter Town and remaining choices

These setup decisions come from [issue #160](https://github.com/compoodment/ClankerWorld/issues/160)
and its reviewed [design PR #296](https://github.com/compoodment/ClankerWorld/pull/296).

The player chooses a **rough site for the first Town** in New World, with the
best suitable areas shown as **guidance, not the only allowed locations**.
Fertile land, reachable wood and stone, and short connected Roads make a site
more suitable; lack of an ideal score does not forbid the player's choice.
The current map tint and hovered tips are provisional advice from nearby map
resources and open ground; they do not decide whether a site can be chosen.
Water or another physically impossible location cannot hold the Town. Within
the chosen rough site, Town generation automatically picks a feasible layout
for **two Houses, a Warehouse, a Farmhouse and a Blacksmith**, connected by
generated Roads. If no legal five-building layout fits, explain the problem
and let the player choose another site without committing a partial Town.
The player can change the rough site before placing founders; there is no
separate layout Accept/Redo step. Tailor Shop and Workshop are not
guaranteed starters; agents may develop them later.

The Town automatically assigns the Farmhouse and Blacksmith to the two starting
households, one productive building each. The player does not choose which
household gets which business; this assignment does not make the agents couples
or biological relatives. Each House starts with **eight food portions**, and
the communal Warehouse starts with at least **one usable wooden axe and one
usable wooden pickaxe**. Each starting agent also starts with **one garment**,
kept in their House. **Agreed on October 1
([#648](https://github.com/compoodment/ClankerWorld/issues/648)):** New World
offers no extra starting supplies and no survival grace period for now. The
default game is built and playtested first; a gentler start may be added once
the game is finished. Exact suitability weights, how far to search around the
chosen site, site-change controls, and whether a nonguaranteed building can
appear at start also remain open. The [planned game content](content-list.md)
records the accepted base tool/item/food/object/art set and explicitly marked
remaining choices; [What works today](../what-works.md) describes the playable
prototype.

### Still to decide

The exact UI for assigning the first four agents to the two starting
households, the detailed limits on child tasks and elder capabilities, whether
older childhood needs a separate phase, relationship and
inheritance mechanics, the risk-based continuity check that follows the first
head-count rule, pregnancy timing and detailed childcare rules, care/resource
eligibility, Jev's optional role in childhood, and the identity implications
of player renaming. First-Town placement details remain open: the local
search extent, the placement boundary's exact distance
measurement near tile/footprint edges, whether geometric proximity also
requires a walkable route, and how changing site revalidates any positions or
claims tied to the previous layout without clearing model/provider choices.
Town residency follows the [agreed membership rules](towns.md#town-membership).
Warehouse access across border changes and public salvage in an abandoned Town
follow the [agreed Town rules](towns.md#borders-abandoned-towns-and-salvage).
How its ordinary access gate relates to a future crime system remains open.
A configurable automatic-birth limit was
considered, briefly accepted, then explicitly
reopened: computment will decide **after playtesting actual birth frequency,
population growth, and model cost** whether a cap belongs in the game. Do not
assume a cap or its precedence over the continuity safeguard before that
decision.
Computment briefly considered removing the close-relative pairing ban, then
retracted that thought; the ban still stands. A model-usage meter and optional
AI-usage limit have since been accepted; neither is a population cap.

Personal ownership is separate from storage or carrying location under the
[agreed household goods rules](towns.md#household-goods-and-departure).
What a will may do, including heirs, children and final words, is agreed
above. For inheritance, still open: the exact final-model-turn contract;
conflicts between a will and agent-made law; debts; no-household cases without
a valid will; and other guardianship cases. Clanker's
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
world starts at a nonzero age, between day 15 and day 25 as agreed above.
**Agreed on October 1
([#640](https://github.com/compoodment/ClankerWorld/issues/640)):** because
adulthood comes before the first 40-day birthday, ages show as life stage plus
world days, such as **Adult · 22 days**, not whole years. A separate adolescent
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
  not cut it off mid-sentence. Danger or urgent needs can interrupt; a bounded
  final wrap-up round can avoid endless looping. If unresolved, say so rather
  than fabricate agreement. Resuming requires both participants to choose it;
  loading a saved world leaves an interrupted conversation stopped until they
  agree again. The marriage-surname conversation above is a special case: it
  must be started even across the world, has the four-turn bound described
  above, and a marriage cannot complete with its surname undecided. The
  ordinary conversation limits below are provisional and may change after
  playtesting; they do not imply that the separate marriage-surname feature is
  implemented.
- **Each participant's own model speaks that participant's turns** in a
  conversation, as in the marriage-surname conversation above.
- **Leaning toward (provisional; tuned in playtests):** a conversation has at
  most **six public turns**, three per participant, plus **one wrap-up
  round**. An agent starts a conversation by choosing a talk action, and each
  agent has at most two conversations per world day. Both participants must
  accept the invitation, any later resumption, and the same structured
  wrap-up before it can apply an effect. Spoken text alone never changes the
  world or counts as agreement.
- **Spoken promises are not tracked as commitments in the first version.**
  Tracking whether promises are kept or broken comes later.
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
- **Agreed: a lesson teaches a saved skill.** A skill,
  such as farming or smithing, is saved for the learner and the lesson records
  who taught it. In the first version a skill does not change what an agent is
  able to do. Later, skills could speed up work or unlock advanced tools, but
  that is not decided. Agents start with no skills, and an agent first gains a
  skill by finishing that kind of work.
  The first version records building, farming, crafting and smithing. An adult
  can ask a free, healthy adult who already knows that skill to teach it. Both
  remain free to refuse or stop; completing a lesson keeps the teacher and time
  in the learner's record. Lessons do not assign work roles.
- Fully generated languages/dialects are **deferred**, not planned now, due to
  uncertain gameplay value and token cost.
- **How a model sees its needs, agreed on October 1
  ([#646](https://github.com/compoodment/ClankerWorld/issues/646)):** a request
  describes each need in words, not exact numbers, and always shows the whole
  scale from worst to best, so the model cannot misread how serious a word is.
  For example: "Fullness: hungry (starving, hungry, fine, full; starving is
  worst, full is best)". The words follow the agreed comfort and urgency
  levels. The exact words and cut-offs are still provisional, for example:
  fullness **full** at 70% or more, **fine** at 40–69%, **hungry** at 20–39%
  and **starving** below 20%; warmth **warm** at 60% or more, **chilly** at
  35–59% and **freezing** below 35%. Illness uses the same kind of scale; its
  words are set when it is built. Compare the words against
  today's numbers in a controlled comparison before they become the default.

### Still to decide

Real token budgets and conversation costs, whether the provisional frequency
and turn limits above feel right, group-planning mechanics, memory
importance/retention rules, compaction triggers and fallback method without
Jev, and Jev's exact role need implementation and playtesting. The budgets stay
open until they are measured in play. The bounded consent and interrupt/resume
behavior is the current baseline, not proof that it will feel right in the
finished game. The September 29 external code review raised additional
**questions, not decisions**. On 30 September computment settled or deferred
three of them: each participant's own model speaks its own conversation turns
(agreed above); spoken promises are not tracked as commitments in the first
version, so that question is deferred; and personality is chosen once and,
provisionally, can change later only at moments the game names, with those
moments and the cap tuned in playtests (see the agent rules above). How a model
sees its needs is agreed above. Still open: how routine and dialogue budgets
are split. Resolve these against the accepted per-agent identity, truth,
information sources and spending rules before specifying a conversation-call
protocol. The review's proposed schemas and token settings are implementation
options, not agreed game design.

## Combat

### Agreed

- Interpersonal combat belongs in the finished game for **self-defense, crime,
  personal feuds, and organized war**. Hunting and sport/duels were not chosen
  in that discussion.
- Combat can turn lethal, but agents should not be dying constantly. Personal
  models choose intentions; the game rules resolve reach, movement,
  timing, protection, injury, and outcomes. Health already exists in the
  prototype, but the integrated game **does not yet have a combat loop,
  weapons, armor, combat injuries, or combat AI**.

### Leaning toward

Weapons and armor should fit the general item/equipment/crafting/invention
system rather than be a disconnected special system. An axe could be both a
work tool and a weapon; armor could be wearable protective equipment. Combat
still needs its own actions and balancing. The approved
[item pipelines](towns.md#item-and-resource-pipelines) give spears, swords,
shields and basic armor wood/cloth/iron crafting paths at the Blacksmith, with
equipping, wear, repair and replacement. Computment's agreement here was
qualified (“if I get what you mean”).

**Parked on October 1, 2026
([#568](https://github.com/compoodment/ClankerWorld/issues/568)):** crafting,
equipping and repairing combat gear waits until combat itself is designed. The
pipelines above stay agreed but are not built before then.

### Still to decide

Detailed injuries and recovery rates, escape/surrender, law enforcement, war
declarations and peace, lethality balance, combat effects of equipment, and
whether animal slaughter or hunting ever becomes part of the game.

## Questions linking these systems

These remain open; they are not new decisions.

- **Population continuity and ancestry.** Four unrelated starting agents in two
   households, a close-kin pairing ban, and a low-population safeguard may
   eventually leave no eligible unrelated adults even if everyone wants
   children. Player-added adults are the only source of unrelated newcomers,
   with placement seeding household/Town membership, and the game offers to
   add one while the continuity rule is on. Which relatives count as too close,
   the first continuity rule and starting ages are agreed above. Whether
   automatic births need a configurable limit is deferred until measured
   playtesting, rather than part of the current design.

- **No fictional population cap versus finite provider cost.** Each agent's
   personal model is central, including child agents; frequent socializing
   and unlimited player-added adults increase call volume. Measure calls,
   tokens, latency, and cost in representative play before choosing decision
   cadence, budgets, and how resource pressure is shown to the player. This is
   a scalability constraint, not permission to replace agents with Jev.
