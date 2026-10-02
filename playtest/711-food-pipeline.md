# Household food and farming

Waiting for normal-path implementation, automated checks and a hands-on check
in the Windows game. These are expected results for the draft repair; none has
been checked by hand.

Use a disposable world with ordinary household work. Inspect the agent's
belongings, fullness and illness, field work, building stock and Event Log.
Save and reopen during the journeys or work below. If the needed situation has
not occurred, leave that check pending rather than changing stock or health
outside normal play. Please record the client and host builds, date, screen
resolution and interface size with any result.

- If food spoils while an adult carries a household delivery, watch them walk back to camp and set it down. Save during the trip and reopen; the spoiled goods remain household property, the adult regains carrying space, and the old delivery is no longer pending. An owner can remove or reassign the destination only once its real stock, work and other deliveries are also clear. ([#710](https://github.com/compoodment/ClankerWorld/issues/710))
- When a severely ill adult begins tilling, planting, tending or harvesting, compare their progress with a healthy adult using the same type of tool. Illness should slow the work. Save during planting and reopen; the planting supplies stay held, are used once when planting finishes, and remain real goods if the work is cancelled. ([#711](https://github.com/compoodment/ClankerWorld/issues/711))
- In autumn, watch an adult whose load has room for berries but not the larger orchard harvest, while both sources are reachable. The adult can gather the berries instead of idling because the orchard is nearer. Their load stays within its limit after gathering and reopening the save. ([#712](https://github.com/compoodment/ClankerWorld/issues/712))
- Watch a hungry adult with a full load, some spare household supplies and reachable food in the household store. Even if wild food is nearby, the adult can set down enough spare supplies to collect one household serving and eat it. The supplies keep their owner and are still present after reopening the save. ([#713](https://github.com/compoodment/ClankerWorld/issues/713))
- Let a Farmhouse fill so that a later grain harvest enters its Silo. Once milling frees room in the Farmhouse, watch an adult walk to the Silo, collect grain, carry it to the Farmhouse and mill it. Save during that trip and reopen; grain stays at its actual location until delivered, flour appears only after milling, and planting stock remains available for another crop. ([#714](https://github.com/compoodment/ClankerWorld/issues/714))
- Watch a well-fed caregiver with a full load while their nearby dependent is hungry and household food is reachable. If they have spare supplies and somewhere legal to set them down, they make room, collect a serving and feed the child. They do not keep attempting an impossible pickup, lose supplies or take another household's food. Save during the food trip and reopen to check the same sequence. ([#742](https://github.com/compoodment/ClankerWorld/issues/742))
- When a hungry child or adolescent belongs to a household whose accessible ready-to-eat food is in its pot, watch them collect a serving and eat it. The pot remains, only the serving leaves, and raw grain or flour is not treated as a ready meal. Repeat after reopening the save. ([#748](https://github.com/compoodment/ClankerWorld/issues/748))
- After a filled jug has been set down with household camp supplies because the House was full, let House space free up and an adult have room for the jug and all its water. Watch the ordinary pickup, walk and delivery home, saving during the trip. The same jug and water reach the House together; when the whole load does not fit, they stay in place rather than splitting or vanishing. ([#757](https://github.com/compoodment/ClankerWorld/issues/757))

The spoiled-food withdrawal repair in
[#747](https://github.com/compoodment/ClankerWorld/issues/747) is a storage-rule
change covered by automated checks. This draft adds no player control or
ordinary agent action for clearing a pot or discarding food, so it has no new
hands-on step here.

Author: Codex session 01a0f433 / review_docs.

