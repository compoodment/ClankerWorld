# Food orders

This change still needs a hands-on check in the Windows game. The supported orders are limited to food tasks.

- On the paired world: ask an adult with access to a known berry patch to gather berries from that named source; confirm the order stays active until berries are actually gathered and then shows as finished. ([#587](https://github.com/compoodment/ClankerWorld/issues/587))
- On the paired world: give an adult a food order for a named source that is not present; confirm the order does not harvest from a different source and remains blocked or reports that it cannot be understood. ([#587](https://github.com/compoodment/ClankerWorld/issues/587))
- On the paired world: submit an unsupported order such as "gather wood" or "build a house"; confirm it is marked not understood in the Event Log and causes no food harvest or consumption. ([#587](https://github.com/compoodment/ClankerWorld/issues/587))
- On the paired world: give an adult one order, replace it with another, then queue a third and cancel the active task; confirm the card shows the replacement, queued task and cancellation without carrying out the cancelled order. ([#587](https://github.com/compoodment/ClankerWorld/issues/587))
- On the paired world: block an adult's food order, make the adult urgently hungry with other food available, and let the world run; confirm the adult handles the urgent need first and returns to the still-pending order afterward. ([#587](https://github.com/compoodment/ClankerWorld/issues/587))
- On the paired world: save and reload while a recognized order is waiting; confirm its target and remaining progress remain visible and the order resumes under the normal access and survival rules. ([#587](https://github.com/compoodment/ClankerWorld/issues/587))
