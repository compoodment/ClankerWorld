# Model-call limit in Game Settings

Pending after merge from [#670](https://github.com/compoodment/ClankerWorld/issues/670).

- On the paired world: open **Settings** from the Main Menu and from the Pause Menu. **Model calls** appears under **Game**, shows the same count both times and is not under **World**. Its text says the count covers all your worlds and every call attempt. ([#670](https://github.com/compoodment/ClankerWorld/issues/670))
- On the paired world: set a small limit, such as 10 calls more than the current count, and let agents with a personal model play. When usage reaches 80% of the limit, one Event Log line names the count and points to **Settings → Game**; later calls add no more lines. Reaching the limit pauses the world as before, and **Allow 100 more calls** then **Resume** continues it. ([#670](https://github.com/compoodment/ClankerWorld/issues/670))
- On the paired world: after the warning, load a save from before it and open **Settings → Game**. The count has not gone down, and no second warning appears. ([#670](https://github.com/compoodment/ClankerWorld/issues/670))
