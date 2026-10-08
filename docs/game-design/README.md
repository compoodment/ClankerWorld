---
title: Game design
type: game-design-index
status: active
updated: 2026-10-08
---

# Game design

ClankerWorld is a pixel-art world simulation about AI agents surviving,
socializing, building a society and eventually inventing things. An agent's
personal model is part of their thinking and identity. The game rules decide
what is physically possible; the optional helper Jev supports agents without
replacing their own models.

The player can observe and intervene. Survival should leave room for
conversation, exploration and building. There is no fictional population cap;
hardware, provider capacity and cost still impose practical limits. Whether to
limit automatic births is a question for measured playtesting.

These pages describe the intended game, including things that are not built.
See [what works today](../what-works.md) for the current build and
[Issues](https://github.com/compoodment/ClankerWorld/issues) for work and discussion.
A choice recorded here does not require every feature to be built immediately.

## Read by subject

| Subject | Page |
| --- | --- |
| Where the game runs, maps, time, weather and survival | [The world](world.md) |
| Starting agents, models, memories, families and combat | [Agents and social life](agents-and-families.md) |
| Buildings, property, work, trade, animals and government | [Towns](towns.md) |
| Menus, controls, settings, pixel art and audio | [Interface and art](interface-and-art.md) |
| Agent inventions and imported mods | [Inventions and mods](inventions-and-mods.md) |
| Saving, loading and compatibility | [Saves](saves.md) |
| Agreed item, building and visual-content families | [Planned game content](content-list.md) |

## How to read the choices

- **Agreed:** the owner chose or accepted the direction. Numbers marked for
  playtesting can still change after testing.
- **Leaning toward:** a strong preference, with a final choice still pending.
- **Suggestion:** an idea that has not been accepted.
- **Still to decide:** an unresolved question.

These replace the earlier labels Decided, Preferred, Proposed and Open without
changing their meaning. A heading never overrides a more specific qualification
inside it. A preferred memory approach remains a preference even when discussed
alongside agreed social rules.

Each chapter owns its design choices. The content list supplies the detailed
agreed catalogue; accepting an item or building does not also decide its recipe,
price, storage capacity or development order. Reconcile conflicting summaries
against the owner's actual decision. The October 1 approval of complete
[item and resource pipelines](towns.md#item-and-resource-pipelines) settles
those sources, work sites, uses and physical stock/trade rules; balance remains
provisional.

The player's [role and intervention controls](interface-and-art.md#player-guidance-and-orders)
are agreed: the observer can send suggestions and orders through the game's
normal rules. These controls do not mean every intended action is built yet.

Government and the economy is still partly open. Taxes
([#1255](https://github.com/compoodment/ClankerWorld/issues/1255)) and
currency ([#1256](https://github.com/compoodment/ClankerWorld/issues/1256))
were answered on October 8, but physical law enforcement and war still wait
on the detailed combat design. Also answered on October 8: knowledge across
generations ([#1246](https://github.com/compoodment/ClankerWorld/issues/1246)),
medicine and injuries ([#1267](https://github.com/compoodment/ClankerWorld/issues/1267)),
invention pictures ([#1270](https://github.com/compoodment/ClankerWorld/issues/1270)),
save branch names and autosave rotation
([#1251](https://github.com/compoodment/ClankerWorld/issues/1251)) and mod
compatibility ([#1272](https://github.com/compoodment/ClankerWorld/issues/1272)).
The October 1 art review settled art references: the
[pixel-art style guide](../development/art-style.md) and its art reviews.
Each chapter's **Still to decide** lists what remains. Work out the intended
experience before narrowing these choices to what the current prototype can
already do.

[Earlier design conversations](../archive/vision-interview-history.md) are kept
for reference. The current chapters take precedence over their earlier answers.
