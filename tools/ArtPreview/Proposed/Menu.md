# Main Menu valley: proposed improvements

These are the same day and dusk valleys you already have. The composition,
both Towns, the river's course, the sun, moon, sky and clouds are unchanged.
What changes is the detail inside them. Everything that moves in the game
still has its anchor: drifting clouds, chimney smoke (same six chimneys),
river sparkles, stars, window and lantern glows (same fourteen lights),
fireflies (same seven homes) and the moon's reflection.

Compare `valley.day` and `valley.dusk` with the baseline sheet.

## What changed

### Mountains and depth (rules M2, L1)

- **Three ridge bands instead of two.** The far ridges behind the big peaks
  now mix 45% toward the sky, so they show as a pale band between the
  summits instead of almost vanishing. The big snowy peaks keep their
  outline and colours and mix 25% toward the sky, which is almost exactly
  what they did before.
- **A new near ridge.** It runs along the feet of the peaks as low rocky
  shoulders at full colour, from the Rock ramp. It is higher at the left and
  right edges, so it frames the valley, and stays well below every summit.
  Its west faces are lit and its east faces shaded. Dark crease lines run
  down the faces, each with a lit lip on its west side, and small scree fans
  spread below them. Grass and a few small conifers climb its lower slopes
  into the forest.
- **A crease on the big peaks.** A one-pixel darker line now marks where
  each lit face turns into shade, so the faces read crisper.

### Tree line (rule M3)

- Two rows of trees: a darker, shorter back row and a front row whose heights
  follow slow noise. Tall stands now alternate with low scrub and the odd
  gap, instead of a regular comb of trees of nearly the same height.
- Round broadleaf crowns mix with the conifers. Conifers are tiered, and the
  tip of each tier catches the light on its west side.
- Colours come from the canopy ramps.

### Fields (rule M3)

- The slanted patchwork layout is unchanged. Each patch now shows a crop:
  ripening grain in rows, ploughed earth with furrows, a low green crop in
  dark rows, young shoots, or mottled pasture. Rows run along the slope in
  some patches and across it in others, as real fields do.
- Hedgerows have small bumps lit on top and a one-pixel shadow toward the
  viewer. A few hedge trees stand where hedges meet.

### Meadow and foreground (rules T1, M3)

- The meadow is softly mottled in wide blobs of neighbouring grass steps,
  with a few tufts and flowers. The old per-pixel specks are gone.
- The foreground strip has proper grass tufts, each a centre blade with
  leaning side blades and a lit tip, plus a few flowers on stems. A few
  near the bottom have two-pixel heads.
- The tree by the left Town has three canopy tones with the highlight toward
  the north-west, an outline on its shaded rim and a small ground shadow.
  The bushes and boulders have lit west sides and edge-step outlines.

### River (rule M3)

- A one-pixel lighter bank on every side.
- The far reach is lighter, because water seen at a low angle mirrors the
  bright sky. This makes the river read as winding away into the distance.
- The near reach has a broken reflection band down its middle, in the
  River light and highlight steps, and a darker strip under the far bank.

### Road, bridge and paths (rules R1, R2, R3, R4)

- The Road uses the map's packed-dirt colours: a lit upper edge and a worn
  lower edge, both broken up so they never read as drawn lines. It has a few
  grey pebbles, and the grass below it is worn thin in places.
- The bridge now matches the map's plank bridge. It has deck planks with
  one-pixel seams in Timber colours, a dark beam under the deck and a rail
  on posts. Each end has two taller posts with a lit cap. The deck casts a
  shadow on the water below it.
- Every door has a doorstep in Doorstep stone with a dirt path down to the
  Road. The long path from the raised House on the right runs diagonally,
  so it reads as a footpath.

### Buildings (rules B3–B6, L5)

- Roofs use the map's materials and ramps:
  - **Houses:** clay tiles in offset courses.
  - **Farmhouse:** thatch in jittered strands, with a bound ridge and a
    ragged, thick eave.
  - **Blacksmith:** slate slabs.
  - **Warehouse:** grey-timber planks along the ridge.
- Every roof has a lit rim on its west slope, an edge-step rim on its east
  slope, a light ridge cap and a dark eave line.
- Chimneys are stone with a cap. Smoke still leaves from exactly the same
  point as before.
- Walls have a shadow under the eave and a stone plinth. Doors have a
  lintel, and windows have a sill.
- One small identifying feature per kind:
  - **Blacksmith:** an anvil on a stump in the yard, next to the glowing
    forge, and coursed stone walls.
  - **Farmhouse:** a stack of sheaves.
  - **Warehouse:** split loading doors and lit crates.

### Dusk (rule M4)

- Windows glow in the warm F2C14E, with a lighter top pixel. The window
  glows use the same colour.
- A faint lavender mist band lies over the far fields and the foot of the
  tree line. It has two soft strengths and wispy horizontal edges, with no
  dither checker.
- The river's reflection band picks up a little of the warm low sky.

## Choices you may want to check

- **The valley floor is slightly greener and crisper by day.** The rule asks
  for the near ridge at full colour, so everything in front of it is at full
  colour too. Otherwise the forest would look farther away than the ridge
  behind it. Before, the fields carried about 20% sky haze. Now their colours
  come straight from the map's grass, scrub and soil ramps, choosing the
  lighter steps so they stay soft. If you prefer the old pastel look, the
  valley can take back a little haze.
- **At dusk the near ridge, tree line and fields keep 12% of the evening
  air.** At full colour they sank almost to black, and the near ridge would
  have looked nearer than the forest in front of it.
- **Fewer sparkles and moon glints.** The bridge's shadow covers a few rows
  of water under the deck. As a result there are slightly fewer river
  sparkles (677 instead of 766) and moon glints (26 instead of 34). Both
  lists are still well filled; sparkles simply no longer twinkle in the
  shadow.
- **The near ridge covers the lowest slopes of the big peaks.** It does not
  touch any summit or the snow.
- **The backdrop stays a side view.** The top-down rule is for the map; the
  menu valley has always been a side-on landscape, and it stays one.

## Not changed

The sky gradient, sun, moon, clouds, stars, birds, smoke colours, the
townsfolk and the lantern posts' positions are as they are today. The scene
is still drawn from fixed seeds with no randomness, so it looks the same
every time.
