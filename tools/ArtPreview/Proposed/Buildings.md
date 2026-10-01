# Buildings: rounds 1 to 4

What to look at: `out/proposed/sheet-buildings.png` next to
`out/baseline/sheet-buildings.png`, and the town in
`out/scene/scene-buildings-32.x3.png` and `scene-buildings-16.x4.png` next to
`scene-current-*`. Every entry is shown over grass (or water), as in the
baseline, and also on its own (the `.sprite` entries) so it can be placed in a
scene. The newest round is described first; the earlier notes follow
unchanged below it.

## Round 4

You approved everything in round 3 except the Market plaza, which you found a
bit too big for eight stalls. Only `Market.plaza` and `Market.plaza.16`
change; every other picture is the same, pixel for pixel.

- **Market.plaza (smaller):** 5 × 4 tiles instead of 7 × 5, so 20 tiles of
  packed earth instead of 35. The spare row in front of the hall and the
  spare column on each side are gone. The eight stalls still stand back to
  back in two rows of four, each row facing its own aisle: the top aisle runs
  past the hall's door, the bottom one along the plaza's south edge. The path
  from the hall's door to the Road runs down the middle, so every open tile is
  an aisle or the path. Also `Market.plaza.16`.

## Round 3

You approved round 2 with three notes. Every approved picture is still exactly
the same, pixel for pixel, except the Market building, which you asked to
change. `Market.plot` is gone and round 3 adds 35 pictures, so there are now
142.

### The Market: a hall and a plaza

- **Market.2x2 (redrawn):** a market hall with no stalls or awnings inside it.
  A timber hall with a hipped roof of planks; on the ridge a lantern, a raised
  vent with louvred sides that lets the air out, roofed in grey slate and
  flying the gold pennant the old Market had; and along the front a lower
  lean-to roof over an open arcade, its posts showing along the eave. A sign
  with gold trading scales hangs over the way in, and the doorstep and path
  start beyond the arcade. Also `Market.2x2.16`.
- **Market.plaza (replaces Market.plot):** the hall at the head of a small
  plaza of packed earth, 7 × 5 tiles, with an open square in front of the
  hall, eight of the approved stalls standing back to back in two rows of
  four with room between them, and a Road leading in from the south. The
  plaza is ordinary Road tiles drawn by the game's own Road pieces: where
  every neighbour is Road they join into one area, so there are no lanes and
  no tile grid, only the soft worn edge round the outside. Also
  `Market.plaza.16`.

### Six boats at a Port

`Port.2x4.moored` (on the sea), and `Port.2x4.N.moored`, `Port.4x2.E.moored`,
`Port.2x4.S.moored` and `Port.4x2.W.moored` (on a river). Three boats lie
along each side of the pier, between the shore and the T-head, each bow-in
with its oars shipped and a bow line to its own bollard on the pier's edge.
Each picture adds a lane of open water along both long sides, the docking
space the design keeps clear; the boats' sterns reach about a third of the way
into it.

### Diagonal handcarts and boats

- Agents walk diagonally, so a cart they pull goes diagonally too:
  `handcart.empty` and `handcart.loaded` facing NE, NW, SE and SW, and
  `handcart.pulled.SE`. Boats get the four diagonals as well (`boat.NE`,
  `boat.NW`, `boat.SE`, `boat.SW`), because they will travel diagonally across
  the water.
- Each diagonal is the same cart or boat, part for part and the same size,
  turned 45° and redrawn in clean one-pixel stairs rather than a blurred
  rotation. It is still lit from the north-west: boards facing the light are
  lit, the far ones shaded, and the ones side-on take the middle tone.
- `handcart.turnaround` and `boat.turnaround` show all eight facings in a row,
  clockwise from north: N, NE, E, SE, S, SW, W, NW.

### Things to decide or know (round 3)

- **Stalls stand on plaza ground.** In these pictures each stall's tile counts
  as part of the plaza, so the earth runs under it. In the game a stall is a
  building, and a building's tiles are not Road, so the map would need to draw
  the plaza under stalls; otherwise each stall would sit in a small patch of
  grass. On the plaza the stalls also leave out their doorstep path, because
  the whole plaza is open to walk on.
- **The hall's colour.** Timber planks, the same brown as the Store and the
  Port shed on the overview map; up close the grey lantern and the arcade set
  it apart.
- **Style guide.** Rule B3 still says the Market has a canvas roof with stall
  awnings. It would change to a timber hall with a ridge lantern, with the
  stalls on a plaza.
- **Bow-in mooring.** Three boats fit along each side of the pier only lying
  bow-in, side by side; lying alongside, end to end, only two fit between the
  shore and the T-head. The approved `boat.moored.E`, lying alongside, is
  unchanged.
- **The painted boat.** `Port.2x4.moored` leaves out the round-1 painted boat
  so six real boats can tie up. The approved `Port.2x4` still has it.
- **Small differences on the diagonals.** The diagonal boat sits one pixel
  toward its bow and its oars are about a tenth shorter, so the blades stay
  inside the tile. The diagonal handcart's shafts are three pixels a row,
  which looks as heavy as the two-pixel shafts on the straight facings, and
  the top log lines up with the other two at the back, because a one-pixel
  step there turns into a notch at 45°.
- **Not drawn yet:** 16 px versions of the handcart and the boat, and moored
  boats at a slant (boats tie up square to the pier).

## Round 2

You approved everything in round 1 except the Farmhouse's cart. Every approved
picture is still exactly the same, pixel for pixel; only the Farmhouse changed.
Round 2 adds 76 pictures, so there are now 108.

### How big is the Market?

Much bigger than the 2 × 2 picture. The agreed design is the 2 × 2 **Market
building** plus separate 1 × 1 **stalls**, and the Town keeps a clear plot of
about **10 × 12 tiles** free for the stalls. So a full Market covers about 120
tiles, thirty times the ground of the 2 × 2 building on its own. The 2 × 2
picture you saw is only the main building. The new `Market.plot` picture
shows the whole thing: the Market at the head of the plot, a lane from its door
to the plot's entrance, two cross lanes, and twelve stalls facing the lanes,
with gaps where more stalls can appear later. The lanes are ordinary Road
pieces, drawn the way the map joins them. How the plot is reserved, who owns a
stall and how the plot fills up are still open design questions.

### Farmhouse: no more painted cart

You were right that a cart painted onto the building would look usable when it
is not. The cart is gone from the Farmhouse, and the handcart is now its own
sprite (below), so the game can show a real one parked beside the Farmhouse,
or anywhere else, when one exists.

- **Farmhouse.1x2:** the yard behind now holds bound sheaves of grain laid out
  to dry, every other one turned round, the way a stack is laid. Each sheaf has
  the shape of the grain item icon: three ears on their stalks, tied at the
  waist with twine.
- **Grain sacks by the door, on every footprint:** one lying on its side, its
  tied neck pointing away from the step, one standing on the other side of the
  step, and a few spilled grains. This is what marks the one-tile Farmhouse
  (`Farmhouse.1x1`), which has no room for a yard.
- To fit the sacks, the roof stands back a little further on the door side, so
  the long Farmhouse roof is three pixels shorter than in round 1.
- It works with the door on any side (`Farmhouse.1x2.door_East`, and the
  north door in the scene).

### The handcart, a vehicle of its own

The agreed handcart (content list 4.8) is made by the Blacksmith from wood,
iron fittings and rope, and can be pulled, parked, repaired and passed on. It is
drawn in one tile, on its own, so the game can put it wherever it really is.

- A plank bed with side boards and iron fittings at the corners, two
  iron-rimmed wheels on an axle under the middle, and two shafts with
  rope-wrapped grips.
- **Empty** (`handcart.empty.S`, `.E`, `.N`, `.W`): a coil of rope lies in the
  bed. **Loaded** (`handcart.loaded.*`): three logs lashed with rope, their cut
  ends showing at the back, and a grain sack across the front.
- The letter is the way the shafts point, which is the way it moves when
  pulled. Parked, the shafts rest on the ground.
- **Being pulled** (`handcart.pulled.E`, drawn loaded): the shafts are lifted
  to hand height, so from above they look a little shorter and their shadow
  falls away from them. An agent would stand between the shafts.

### The rowing boat

The agreed boat (content list 7.3) is made at a Port from wood, rope and iron
fittings and carries one agent and their cargo.

- `boat.S`, `.E`, `.N`, `.W` (the letter is the way the bow points), shown on a
  river tile and on its own: a planked hull with a narrow stern and a pointed
  bow, a lit gunwale, floorboards, two thwarts, iron oarlocks and oars resting
  with their blades on the water, a soft shadow and a ripple where the hull
  meets the water.
- `boat.moored.E`: the boat tied up on the east side of the Port's pier, oars
  pulled in, with a line from its bow to the pier's bollard. The column of open
  water beside the Port is the docking space the design keeps clear.

### Port in four rotations

`Port.2x4.N`, `Port.4x2.E`, `Port.2x4.S` and `Port.4x2.W`. The letter is the
side that stands on land, where the shed is and where its door faces the Road;
the pier reaches over the water on the other three tiles. Each rotation is laid
out the same way but lit from the north-west like everything else, so it is not
simply the round-1 picture turned round. These Ports have no painted boat, so
the real boat can tie up beside the pier instead (same reason as the cart).

### The remaining footprints, kinds and door sides

- **House.2x2:** a square hipped roof whose four hips meet at a point.
  Also `House.1x2.door_North` and `House.1x2.door_West`.
- **Warehouse.2x3** and `Warehouse.2x2.door_North`.
- **Blacksmith.1x2:** the forge yard behind the roof keeps the hearth with its
  embers and the anvil; there is no room for the water barrel. Also
  `Blacksmith.2x2.door_West`, with the yard at the end away from the door.
- **TailorShop.2x2**, **Store.1x2**, `Store.1x1.door_East`, **Clinic.1x2**: the
  approved looks on their second footprints and other door sides.
- **Workshop.2x2 (redrawn):** the Town's shared workshop. Green plank gable
  roof, the hammer sign over the door, and a small work yard with a trestle
  bench, a saw on it, shavings and a stack of planks.
- **Generic.1x1 (redrawn):** for any building the game cannot name. Plain
  grey-brown shingles and a door, nothing else, so it never looks like a real
  kind.
- **Restaurant.1x2 and Restaurant.2x2 (new):** the optional food business.
  Clay tiles like the House, but a gable roof instead of a hip, a chimney, and
  a sign with an iron pot of stew over the door. The larger one adds a paved
  terrace with two tables and benches, and a bowl on each table.
- **16 px:** `House.2x2.16`, `Warehouse.2x3.16` and `Market.2x2.16`.

In the game's map, the redrawn look now covers every current kind, including
Workshop and Generic, at any footprint, door side and size. Only the retired
Shelter, Storehouse, Hearth, Path and Bedroll keep their old drawing.

Style guide rules applied in round 2 are the same as in round 1 (B1 to B7,
north-west light, south-east shadows, separately drawn 16 px versions). The
handcart and boat also follow the sprite rules: each stays inside its tile,
casts the small south-east shadow and has a one-pixel outline in the darkest
step of its own material, and every facing is lit from the north-west.

### Things to decide or know (round 2)

- **Should the approved Port lose its painted boat too?** `Port.2x4` still
  shows it, because you approved it as drawn. I recommend removing it, as the
  rotations do, so only real boats appear at a Port. It is a one-line change.
- **One stall look.** Every stall in the Market plot uses the approved
  red-and-cream awning, because each design has one look; only the produce on
  the counters differs. The Market building keeps its four coloured awnings.
- **Restaurant colour.** It shares the House's clay tiles, as the style guide
  says; the gable and the pot sign set it apart. A different roof colour would
  make it stand out more on the overview map, if you prefer that.
- **One new shade.** The Generic building's lightest step (a pale grey-brown)
  is not in the style guide yet; every other colour comes from its ramps.
- **Before these show in play,** the game needs a handcart and a boat as
  objects with a place, a facing and a state (empty, loaded, pulled, moored);
  today it has neither. Restaurant and the other new buildings still need their
  game kinds, as in round 1.
- **Not drawn yet:** 16 px versions of the handcart and the boat, for mid zoom.

## Round 1 (approved, except the Farmhouse cart)

### What stayed the same

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

### What changed

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

### New buildings (no game kind yet, shown for review only)

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

### Style guide rules applied

Palette ramps (section 2), north-west light and south-east shadow (L1, L2),
the roof edge in each ramp's darkest step (L5), separately drawn 16 px
versions (S1, S4), and the building rules B1 to B7: inset roof, eave shadow,
material per kind, ridge and hips, door and doorstep, one feature per kind,
and two tones at 16 px.

### Things to decide or know

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
