# Crops: round 1 mockups

What this covers: farm fields (all five grain states, plus mature potatoes
and mature greens) and the three orchard tree stages, at 32 px and 16 px.
There is also a 3 × 3 patch of ripe grain for judging how a large field
repeats. The game has no field art yet, so the fields are new. The orchard
trees replace today's orchard sprites.

## How fields work

- A field is a see-through layer drawn over the Fertile soil ground tile. It
  is never a separate picture with its own ground, so the soil, seasons and
  night tint all still apply.
- Rows run east to west and repeat every four pixels, at both 32 px and 16 px.
  Everything wraps around the tile edges, so a field several tiles wide reads
  as one field with no seams. The 3 × 3 grain patch and the side-by-side farm
  check showed no seams.
- Each state is drawn separately at 16 px rather than shrunk (rule S1). At
  mid zoom each state still has its own look: plain furrows, dotted seed,
  green dots, gold, pale stubble lines, dark green bands, light green dots.

## Field states, grain

- **Prepared:** tilled rows only. Each furrow is a shaded trough line with a
  lit line just south of it, where the furrow wall faces the north-west light.
  The two lines are two soil shades apart, four pixels apart, with short
  breaks so they don't look ruled. A few clods sit on the ridges, each with a
  small shadow (rule T5).
- **Seeded:** the same rows with seed dotted along every trough in the light
  wood colour (rule N5).
- **Sprout:** small two-pixel shoots standing in the troughs: a green stem
  with a bright tip, and now and then a second leaf.
- **Mature:** ripe grain covers the soil in the thatch gold ramp. Each row is
  shaded like a low bank (lit top, gold middle, a thin shaded line where it
  shades the next row), with lit ears standing along the top. A slow, gentle
  brightening across the field adds life. An earlier draft also used it to
  darken patches, but those repeated as a visible pattern across a big field,
  so it now only brightens, by one step.
- **Harvested:** pale cut stubble along each row, each stalk end with a tiny
  shadow, one loose straw and a few clods. Earlier drafts had three straws,
  and they repeated as a visible pattern across a block of fields.

## Field states, other crops

- **Potatoes (mature):** rows of separate low leafy mounds in the tree-leaf
  greens. They are lit from the north-west, outlined in the darkest leaf
  green, and cast a small shadow, with soil showing between the rows.
  Alternate rows are shifted half a plant so the mounds don't line up in
  columns.
- **Greens (mature):** round, softly ruffled rosettes in a lighter leaf green
  with a cream heart and a faint cream midrib, each outlined and shadowed, in
  staggered rows. An earlier version had Y-shaped veins that read as a symbol
  rather than a plant. At 16 px the cream is only a tint, because full cream
  dots made the grid too busy.

## Orchard trees

- They are drawn the same way as the proposed broadleaf tree in the nature
  mockups: the same silhouette, a shaded rim on the south-east, light and
  highlight pulled toward the north-west, a few leaf dimples, a one-pixel
  outline and the usual soft shadow to the south-east (rules N2, N6, L1, L2,
  L4). They use the orchard leaf colours, which are lighter and yellower than
  a wild broadleaf, so an orchard reads as planted.
- **Fruiting:** five round fruit, each lit on its north-west with a bright
  top pixel and a darker pixel to the south-east (rule N3). At 16 px each fruit
  is one orange pixel, which still shows clearly.
- **Picked:** the same tree with the fruit gone (rule N4).
- **Growing:** a young tree about two thirds of full size, lit the same way.

## Rules applied

Colours come only from the style guide's soil, thatch, leaf, orchard, fruit,
wood and cloth ramps (P1 to P3). Light comes from the north-west and shadows
fall south-east (L1, L2). Plants and trees have an outline in their own
darkest colour, never black (L4). Separate 16 px drawings (S1, S4). Tilled
soil (T5), crop fields (N5) and orchard (N6). Everything is drawn from fixed
seeds and takes well under a second.

## Not done, or worth knowing

- The reference scene has no slot for fields, so fields can only be judged
  on the contact sheet, the tiling patch and side-by-side checks. Only the
  orchard trees show in the scene.
- Fields are shown over today's Fertile soil tile. The terrain mockup
  proposes a tilled soil tile with its own furrows. If that is approved,
  the "prepared" state could simply be the bare tile, and the furrow phase
  of both should be lined up so rows don't double.
- Not part of round 1 (they wait for round 2): the potato and greens early
  states (prepared, seeded, sprout) and harvested states, and the orchard
  sapling. For now, potatoes and greens reuse the grain's early states. Their
  harvested state is turned earth with clods, without stubble.
- The orchard outline uses the orchard ramp's own darkest colour, as rule L4
  says. That is a little lighter than today's shared tree outline, so on
  dark forest grass the orchard edge is softer than a wild tree's.
