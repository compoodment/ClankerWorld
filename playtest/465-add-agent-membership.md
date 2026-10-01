# Add Agent membership

Pending for PR #557. No Windows playtest is claimed.

- Add an adult on a House, on Town land away from household buildings, and outside the Town. The preview matches the household and Town shown after placement. ([#465](https://github.com/compoodment/ClankerWorld/issues/465))
- After a House expands, add an adult on the new part of its footprint. The preview and placed adult belong to that House's household. ([#465](https://github.com/compoodment/ClankerWorld/issues/465))
- Leave Add Agent open through a reconnect or a switch to another disposable world. The preview uses the current world. Moving the pointer from the map into the panel keeps the model and key controls in place and does not preview a tile hidden behind the panel. ([#465](https://github.com/compoodment/ClankerWorld/issues/465))
- If a House grows or a Town border changes while its tile is being previewed, confirm the old preview. Placement is refused until the preview is refreshed if the adult's membership would have changed. ([#465](https://github.com/compoodment/ClankerWorld/issues/465))
- After placing an adult inside a Town, let them walk outside it. Their Town membership stays the same. ([#465](https://github.com/compoodment/ClankerWorld/issues/465))
- Save and load those placements. Their household and Town membership stays the same. ([#465](https://github.com/compoodment/ClankerWorld/issues/465))
