# Duplicate model-chosen names

Pending for PR #571. No Windows playtest is claimed.

- If a model chooses a full name already held by another agent, the new agent keeps its placeholder while it is asked once more. Similar first names with different surnames remain allowed. ([#456](https://github.com/compoodment/ClankerWorld/issues/456))
- If the second choice is also taken or unusable, the placeholder stays and no further automatic naming request follows. The player can rename the agent. ([#456](https://github.com/compoodment/ClankerWorld/issues/456))
- Save and load while a retry is waiting, or rename the agent yourself. The player's name stays and an old reply does not overwrite it. ([#456](https://github.com/compoodment/ClankerWorld/issues/456))
