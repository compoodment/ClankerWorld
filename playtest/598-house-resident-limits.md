# House resident places and expansion

Hands-on checks for [#598](https://github.com/compoodment/ClankerWorld/issues/598), after its change is merged.

- Fill a 1×1 House with three unrelated permanent residents and ask to admit another adult. The request should not be offered; Add Agent on another property owned by that household should also refuse the extra resident. ([#598](https://github.com/compoodment/ClankerWorld/issues/598))
- Establish a recorded family group in a 1×1 House and check that it gets four places only while at least two residents in that group remain a strict majority. Try an admission that would remove the majority; it should not use the family limit. ([#598](https://github.com/compoodment/ClankerWorld/issues/598))
- Send one resident away and invite a storm guest. The resident count should still include the traveler and should not increase for the guest. ([#598](https://github.com/compoodment/ClankerWorld/issues/598))
- In a full House, complete a birth plan. The child should join the primary caregiver's current household, and the card should show the resulting overcrowding without removing anyone. ([#598](https://github.com/compoodment/ClankerWorld/issues/598))
- Start a House expansion while it has no free resident places. The limit should stay the same while work is unfinished and increase only when the new footprint is complete. ([#598](https://github.com/compoodment/ClankerWorld/issues/598))
