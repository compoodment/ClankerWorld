# Untouched-UI mockup round — working plan

Scratch branch: `scratch/ui-audit` (thumbnails branch + origin/main), never pushed.
Harness: Main.Mocks.cs, Main.ArtDump.cs, Main.Audit2.cs in src/ClankerWorld.GodotClient (remove before any commit).
Capture: `godot ... -- --fs-out=$S/audit/<l|d> --fs-size=1920x1080 --fs-scale=0 --fs-theme=<light|dark> --audit2`
Before shots: $S/audit/{l,d}-a2-*.png (taken before any change).

## Redesigns (real code on scratch branch)
1. Map icon: folded map, not envelope
2. Hover readout: terrain swatch + name chip
3. Selected tile: terrain heading + swatch, plain-word facts, object rows with icons
4. World Map: parchment frame, legend
5. Map filters: swatch per row + one-line description
6. Agents list: portrait cards, activity, attention icons, summary chips
7. Event Log: kind icons, day bands, find button for located events
8. Controls: keycaps, grouped columns, shorter so it clears the top bar
9. World Info World: stat grid with icons
10. World Info Towns: residents portraits, stores as item slots, projects with meters
11. Memories: tabs + entry cards with kind icons, confidence meter, map sites with swatches
12. Family tree: check with data; legend swatches, compact empty state
13. Agent Model box: check with data
14. Profile polish: Speak switch as SegmentedChoice, 2-line thoughts preview, people rows with icons
15. Add Agent: labelled steps, no empty space, placement hint
16. Mod Library: package cards with status tags
17. Dialogs: centered body, boxed close button, half-width quotes
18. Status messages: icon + left text, hugging width
19. Pairing screen: check
20. Dev tools: mock panel only (needs host work) — ask separately

## Status
- [x] Before captures
- [x] Batch 1 (1–5)
- [x] Batch 2 (6–8)
- [x] Batch 3 (9–10)
- [x] Batch 4 (11–14)
- [x] Batch 5 (15–18); pairing dropped (rare screen, harness can't reach it)
- [x] Final after shots: $S/after/{l,d}-a2-*.png (light + dark)
- [x] Fair before shots from base worktree $S/base (6749d71 + harness): $S/before2/{l,d}-a2-*.png
- [x] Late fixes: Towns page scrolls instead of running off screen; dialog close button inside frame (22px, centred on title); dev tools weather/season as icon segments, square tool buttons, fits screen
- [x] Found live bug: Family Tree blank on main (scroll 0 height) -> filed #607 (P1); redesign fixes it
- [x] Crops done: $S/page/img/<key>-<l|d>-<b|a>.png (22 keys, 88 files, ~1 MB)
- [x] $S/page/build.py written: 19 items with change notes; embeds images as data URIs; reads template.html ({{ITEMS}}, {{COUNT}}, {{NAMES}})
- [ ] NEXT: write $S/page/template.html (title, tokens light/dark, sticky bar: answered count, Light/Dark screenshot switch, Copy answers; intro + "not in this round"; lightbox with Now/Proposed flip + 2x; localStorage answers), run `cd $S/page && python3 build.py`, publish index.html (icon "review")
- [ ] Ask owner accept/reject per item in chat; then PRs (rebase on main, drop harness, update smoke tests)

## Resume notes
- Xvfb: `rm -f /tmp/.X99-lock /tmp/.X11-unix/X99; nohup setsid Xvfb :99 -screen 0 2600x1500x24 &`
- Recapture: `bash $S/shoot.sh <names|all> "light dark"`, then copy $S/audit/?-a2-<name>.png to $S/after/
- Shots without a redesign: house, house-details, warehouse-details, card (quick card doesn't open in harness), pause, speak (covered by profile)
