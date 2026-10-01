# Water: proposed art (round 1)

What the owner sees: the same four water colours as today, the same kind of
small wave crests and sparkles, the same caustic net on shallow water, but
the surface is no longer one flat sheet. Each block now carries soft, rounded
pools of depth about one to three tiles across, so a lake or a stretch of
sea reads as water with a bottom under it instead of a painted rectangle.
Nothing is noisy at full zoom, and the 16 px mid-zoom atlas has its own
drawing rather than a shrunk copy.

Compare `out/baseline/sheet-water.png` with `out/proposed/sheet-water.png`,
and the reference scene `out/scene/scene-current-32.x3.png` with
`out/scene/scene-water-32.x3.png` (and the `-16.x4` pair).

## Asset by asset

**Ocean.block (32 and 16 px).** Base colour unchanged. Dark depth pools in
the ocean's shade tone cover about 18% of the block, with a half-strength
rim one pixel wide so the edge is soft but never a gradient band. Under
them sits a much fainter, broader swell two to four tiles across so no
sixteen-tile stretch is one flat sheet. Crests are 2–5 px dashes in the
ocean highlight with a one-pixel trough toward the shade step beneath; they
gather in some stretches and thin out in others, as today. About 14 single
sparkles per block. Crests and sparkles together cover about 0.2% of the
block, far under the 3% ceiling.

**Lake.block.** The same structure, but the pools are lighter (the lake's
light tone, about 15% of the block), so lakes read as shallower, stiller
water. Fewer and shorter crests than the ocean.

**River.block.** Stays the clearest, as the style guide asks: only a faint
lift toward the river's light tone over about 9% of the block, and short
3 px ripples. Rivers in the scene look almost exactly as they do today, just
less uniform.

**ShallowWater.block.** The caustic net is kept as it is. Beneath it, light
sandy patches (shallow light tone, about 12%) so the shallows read as a
bottom seen through water rather than a net drawn on flat blue.

**ford.ns and ford.ew (32 px, with `.sprite` bare versions and `.16`
versions).** New. A one-tile shallow crossing drawn over a river tile: a
band of lighter water (the shallow light tone) across the tile, its edge
wandering by a pixel, with five flat stepping stones in the Rock ramp. Each
stone has a dark edge outline, a lit north-west top and a shade crescent on
the south-east. `ns` runs the crossing from the north edge to the south edge
(for an east–west river); `ew` runs it east–west (for a north–south river,
as in the scene). At 16 px it is a 6 px light band with five 2 × 2 px stone
dots, so it reads as the dotted bar the style guide asks for.

**Seams.** Every mark wraps across the block edges and the noise repeats
exactly every block, so tiling the block shows no seam; a numeric check
found the wrapped edges differ no more than any interior column.

## Style rules applied

- W1: base colours unchanged; the block is still a seamless 16 × 16-tile
  repeat, so the game's existing draw path (`(x mod 16, y mod 16)`) is untouched.
- W2: crests are 2–5 px highlight dashes with a one-pixel shade trough, well
  under 3% of the block, gathered and sparse by a swell field.
- W3: ocean blotches in the shade direction, lake blotches in the light
  direction, rivers clearest.
- W4: the shallow caustic net stays; the coast masks are not touched.
- W5: the ford is five flat Rock-ramp stones with shallow-light water between.
- P3 and L1: every tone is a blend between two steps of the style's own ramp,
  never a black overlay; the ford stones are lit from the north-west.
- S5: no anti-aliasing; soft edges come only from one half-strength rim pixel.
- L4: the stones carry a one-pixel outline in the Rock edge step.

## Where I bent a rule, for the owner to accept or veto

- The depth pools are a **half step** toward shade or light rather than the
  full ramp step. The full step looked like dark continents on the ocean
  and a light flat patch on the lake; the half step keeps the "calm" goal.
  It is one number per style if the owner wants it stronger.
- Besides the 10–20% blotch share, there is a **faint slow swell** over
  roughly 30% of the block at about a third of the blotch strength. It is
  barely visible on its own; it only stops a long stretch from reading as
  one flat sheet. Easy to drop if the owner prefers the strict share.
- "Subtle direction on rivers" is **not** drawn. A repeating block cannot
  know which way a river runs; that needs a flow direction per tile from the
  server, which the map does not carry today. The river keeps its short
  "^" ripples instead.

## Not done, and what the game would need

- Coast masks (`land`, `shallow`, `foam`, `river_shallow` pieces) are left as
  they are; the proposal only sets the water tiles.
- The ford is an overlay the preview shows over a river tile. The game has
  no notion of a ford tile yet, so placing it needs a road-over-river rule
  (or a map flag) before it can appear in play.
- Checked only in the offline renderer at 1×, 3× and 4×, not in the Godot
  client or on Windows.
