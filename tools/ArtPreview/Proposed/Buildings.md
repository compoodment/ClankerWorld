# Buildings: round-1 proposal

What to look at: `out/proposed/sheet-buildings.png` next to
`out/baseline/sheet-buildings.png`, and the town in
`out/scene/scene-buildings-32.x3.png` and `scene-buildings-16.x4.png` next to
`scene-current-*`. Every entry is shown over grass, as in the baseline, and
also on its own (the `.sprite` entries) so it can be placed in a scene.

## What stayed the same

- Each building still fills its footprint the same way: the roof sits three
  pixels in from the edge, with a little more room on the south for the
  shadow. One look per building and footprint, no variants.
- The door is still on the side that faces the Road, on the tile the game
  asks for, and the start of the doorstep path still lines up with the Road's
  own doorstep piece at the tile edge.
- Every building keeps its colour family: terracotta Houses, golden thatch on
  the Farmhouse, dark slate on the Blacksmith, blue-grey timber on the
  Warehouse, pale wood on the Silo, purple on the Tailor shop. The overview
  colour of each roof is unchanged.
- Workshop and the generic building keep today's drawing; they are not in
  round 1. The retired Shelter, Storehouse, Hearth, Path and Bedroll are not
  redrawn.

## What changed

**Roofs now show a material.** Instead of two flat tones with stripes, each
roof is laid in courses that follow its eaves, so the lines turn the corner
at every hip:

- **House: clay tiles.** Short courses of rounded tiles, each with a small lit
  crown on the sunny faces, offset from row to row.
- **Farmhouse: thatch.** Short straw strands running down the slope, trimmed
  ends along the eave with a soft shadow under the lip, rounded corners and a
  bound ridge tied down with darker spars.
- **Blacksmith and Town Hall: slate.** Larger rectangular slabs with staggered
  joints and a few slightly lighter or darker slates.
- **Warehouse: timber planks** running along the ridge, with long boards,
  far-apart joints and a faint grain.
- **Tailor shop, Store and Clinic: shingles** of uneven widths in a mix of
  tones, like split and dyed wood.

**Light and shape.** Light comes from the north-west on every roof: north and
west faces are lit, south and east faces shaded. The ridge is a light line
along the long side with a thin dark crease just below it on the shaded half.
Houses, the Farmhouse, the Tailor shop, Store, Clinic and Town Hall have
hipped roofs, so their hip lines show from straight above; the Blacksmith and
Warehouse keep a plain gable. Every roof casts the same soft shadow to the
south-east as today.

**Doors you can see.** Each door is now a dark doorway six pixels wide under
a light timber lintel, over a stone doorstep three rows deep (lit at one
corner, darker on its outer edge), and the dirt path starts beyond it. When
the door is on the north, east or west, the roof stands back two pixels on
that side so the doorstep fits there too.

**One feature per kind:**

- House: a stone chimney with a capped rim and a dark flue on the shaded half
  of the roof, at the end away from the door.
- Farmhouse: a hay cart heaped with hay on a worn patch of yard behind it.
- Blacksmith: an open forge yard of dark packed earth with the stone hearth
  and its ember glow, an anvil on a stump and a quench barrel.
- Warehouse: wide split loading doors under a lintel beam, over a stone apron.
- Silo: the cone now reads as a cone, lit on its north-west side and shaded
  on the south-east, with faint board seams, a darker eave ring and a small
  capped vent. (The old one looked like a cart wheel.)
- Tailor shop: the spool sign, redrawn with timber flanges and a thread end.

**16 px.** At mid zoom each face keeps two tones and its course lines, hips
stay visible, and the chimney, door, doorstep, ember and anvil stay as two or
three pixels each (`House.1x1.16`, `Blacksmith.2x2.16`, and every building in
the 16 px scene).

## New buildings (no game kind yet, shown for review only)

- **Store:** a brown shingle roof with a red-and-cream striped awning across
  the shop front; the doorstep shows beyond the awning.
- **Market:** a paved square with a striped canvas pavilion and a pennant in
  the middle, and stalls in the corners under green, gold, blue and orange
  awnings with crates of produce. The corner on the way in from the door is
  left open.
- **Market stall:** one striped awning with crates of produce facing the Road
  and room for the path between them.
- **Town Hall:** a cross-shaped slate hall, a main roof over two lower wings,
  with gold finials on the ridge, an open bell tower with a bronze bell near
  the front, and a paved forecourt with wide steps and two flower planters.
- **Port:** a shingle-roofed shed on the shore with its door to the Road, and
  a plank pier on piles reaching over the water to a T-shaped head, with iron
  bollards, crates, a coil of rope and a rowing boat moored alongside.
- **Clinic:** a pale shingle roof with a green cross sign and a small herb
  planter by the doorstep.

These use colours already in the style guide (timber, cloth, berry red,
leaf green, gold, lake blue, fruit orange); no new colour family was added.

## Style guide rules applied

Palette ramps (section 2), north-west light and south-east shadow (L1, L2),
the roof edge in each ramp's darkest step (L5), separately drawn 16 px
versions (S1, S4), and the building rules B1 to B7: inset roof, eave shadow,
material per kind, ridge and hips, door and doorstep, one feature per kind,
and two tones at 16 px.

## Things to decide or know

- **Course lines are softened.** To keep roofs calm at full zoom, the lines
  between courses and the joints sit part of the way between two steps of the
  ramp, the way today's roofs draw their lines at partial strength. The
  palette rule asks for whole steps only; if you prefer that, the lines get
  noticeably busier.
- **Long Houses and the Farmhouse are hipped.** The guide asks for hipped
  roofs on square buildings; I hipped the long House and the Farmhouse too
  because the hip lines read well from above. A gable is a one-line change.
- **The Store, Clinic and Market colours are my choice** and need your nod.
- **The Port is drawn with its land row to the north** and its door on that
  side. The grass and river meet with a hard edge in this mockup; the real
  map would draw its shoreline there.
- **Small footprints lose their yard.** A one-tile Farmhouse or Blacksmith has
  no room for the cart or forge yard, so it shows only its roof material.
- **No door on the Silo**, as today; the scene gives it no doorstep path.
- Restaurant and the second footprints of Store and Clinic wait for round 2.
