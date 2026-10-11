# Property hearing display

Issue: [#1520](https://github.com/compoodment/ClankerWorld/issues/1520)

Windows game-window checks remain pending; automated native UI checks do not replace them.

- [ ] Complete a property recovery. Inspect the building: its owner is the Town, the result says Property outcome, and no line promises unchanged ownership or access.
- [ ] Grant the recovered property through another case. Inspect the building: its current owner is the receiving household, with the matching grant result and earlier case history preserved.
- [ ] Inspect a pending or rejected property case. Confirm it does not imply that ownership already changed.
- [ ] Inspect an ordinary dispute or expiry case. Confirm its existing use-permission wording remains, with the recorded owner unchanged.
- [ ] Read result events after reload or reconnect. Confirm property events describe an ownership outcome and missing case context stays neutral.
