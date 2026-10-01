# Desert cacti and softer snow edges

After the October 1 playtest you decided that cacti come back as plant cover
on desert sand only, and that snow needs a softer edge into the land next to
it instead of flat white with a stark border. This proposal draws both in
the approved round-1 style. The snow tiles themselves are the approved
round-1 tiles and are not changed.

**Approved in round 3, and now in the game:** all three cacti and the soft
snow edge. The game draws a cactus on about one in five of the tiles the world
marks as cactus cover, and never on a Road, field or building. You rejected
`snow.edge.before`, which was the game's old edge shown for comparison; it is
no longer drawn anywhere, so it is gone from this proposal.

## Cacti

Three cacti, each at 32 px and at 16 px. The sheet shows each one on the
approved sand tile, on desert brush (`.on_brush`) and on its own
(`.sprite`). `desert.patch` shows a 6 × 5 tile stretch of sand and desert
brush with a few cacti, so you can judge them in place.

- **Cactus**: a round barrel cactus seen from above. Dark grooves run out
  from the crown between its ribs, there is a pale spine on each rib on the
  lit side, and a ring of small yellow flowers sits round a woolly crown. A
  small offshoot grows beside it.
- **CactusTall**: a tall saguaro-like cactus from straight above. You see the
  ribbed top of the trunk and two arms that reach out and turn up into
  smaller round tops. Its shadow trails further to the south-east than the
  others' to show that it is tall.
- **CactusPad**: a prickly-pear clump of overlapping oval pads, each lit on
  its north-west side, with faint areole dots and three red fruits on the
  top pads.

Rules followed from the style guide:

- Light from the north-west, a soft shadow to the south-east, and a
  one-pixel outline in the darkest step of the cactus colours (L1, L2, L4).
- No new colour family. The cacti use the conifer canopy greens, a cool
  blue-green that stands out on warm sand. Spines use the pale Cloth step,
  the barrel flowers use Gold, and the prickly-pear fruit uses Berry.
- The 16 px versions are drawn separately (S1, S4). Each keeps one clear
  mark: the barrel a gold dot, the saguaro its arms, and the prickly pear
  three larger pads with two red dots.
- Everything stays inside the tile with a one-pixel margin (N1).

## Softer snow edges

`snow.edge.after` shows a 6 × 4 tile patch where snow meets grass and rock,
drawn with the new edges. Round 3 also showed the same patch with the old
edges (`snow.edge.before`). The `.16` version shows the 16 px atlas.

- **The snow thins out instead of stopping.** Near its edge the snow breaks
  into a ragged rim, then a few loose clumps, then pale half-transparent
  frost on the grass or rock. Before, it ended in a solid band with a crisp
  line.
- **Shaded like a low drift.** Edges that face north or west catch the light.
  Edges that face south or east (the lee side) take the Snow ramp's shade
  step, two pixels deep at 32 px, so the drift's slope shows.
- **Tile corners still meet.** Each edge piece keeps today's depth where it
  crosses into the next tile, so long snow boundaries join without seams.
  Nothing reaches further than a quarter of a tile, the same limit as today.
- **Only snow changes.** Tundra snow gets the same treatment. Every other
  surface keeps today's edge pieces exactly, such as rock reaching into
  grass. Snow beside water still uses the shoreline pieces.
- **At 16 px** there is room for only a little of this: small bumps on the
  edge and a few frost pixels. Loose clumps would line up into stripes at
  that size, so they are left out.

## Things to know

- The tall cactus's shadow is longer than the standard south-east shadow,
  on purpose, so it reads as tall. Say if you would rather all cacti use the
  standard shadow.
- The desert brush under the cacti is the approved round-1 desert brush tile.
  That tile is not in the game yet, so the proposal takes it from the terrain
  proposal for now.
- The game has no cactus sprite yet, and its map still turns cactus cover
  into plain desert brush. Placing cacti in the world, such as which sand
  tiles carry one and how many, is game work for when the cacti are built.
