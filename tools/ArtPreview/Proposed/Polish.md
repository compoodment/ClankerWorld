# Visual polish

On October 9 computment picked eight ideas from a list of visual additions
that would make the map feel more alive. This proposal draws options for
each over the reference Town corner, as stills at 32 px (close) and 16 px
(mid zoom) and as short loops. None changes a game rule; each only shows
what the world already records, more clearly.

`dotnet run -- proposed out <family>` draws the stills and
`dotnet run -- animate out <family>` writes the loops, 12 frames a second.
The families are `movement`, `smoke`, `weathermarks`, `goldenhour`, `stock`,
`camera`, `moments` and `weatherfade`.

## 1 · Smooth movement (`movement`)

An agent walks four tiles along the main street and a cow four tiles along
the meadow, one tile per world update, as the host reports them.

- **A · Today:** each figure jumps a tile at every update.
- **B · Glide:** each figure slides steadily to the tile the latest update
  reports, with the walk frames changing every quarter second. A jump longer
  than three tiles, such as after a reload, still snaps.
- **C · Glide and bob:** as B, with a one-pixel bob on each step.

## 3 · Chimney smoke (`smoke`)

Smoke rises only from buildings in use: here two Houses with someone inside
and the Blacksmith's forge. The empty Houses stay smokeless, following the
night-lights rule for when a building is lit.

- **A · Wisps:** a few small puffs drift east and fade.
- **B · Column:** a fuller column that leans with the wind.
- **C · Puffs:** one round puff now and then.

## 7 · Weather that leaves a mark (`weathermarks`)

Today snowfall never whitens the ground: the only white ground is the
permanent snow terrain of cold climates. These marks would follow the
weather the world records for each region, and fade after it passes.

- **Ground snow A:** open ground covered, grass tips and trodden Roads
  showing. **Ground snow B:** patchy cover that thins and melts unevenly.
- **Roof snow A:** snow on the north half of each roof. **B:** roofs covered,
  edges and chimneys still dark.
- **Puddles A:** small puddles on Roads and bare ground. **B:** puddles and
  darker, wet-looking ground.
- **Leaves A:** a few fallen leaves under each broadleaf and orchard tree in
  autumn. **B:** a carpet of leaves around them.
- **Footprints:** an agent crossing snow leaves prints that fade.

## 8 · Golden hour (`goldenhour`)

A day loop: day, dusk glow, the approved night tint, dawn glow, day.

- **A · Amber:** an amber wash of up to 22% at dawn and dusk.
- **B · Amber and glow:** A, plus warm light catching bright roofs and Roads.
- **C · Rose and gold:** rose at dawn, gold at dusk.

## 9 · Stock you can see (`stock`)

Log, crate and sack piles that grow with what a building actually stores:
empty, some and full, beside the Warehouse and between the Farmhouse and
the Silo.

- **A · At the door:** piles on the ground beside the building.
- **B · Along the wall:** stacks against the building's wall.

## 11 · A smoother camera (`camera`)

**Find** moves the view from the Farmhouse to the east Houses, then the view
zooms in one step.

- **A · Today:** the view jumps.
- **B · Ease:** the view glides there in about 0.7 s, slowing as it arrives.
- **C · Ease and settle:** a gentler start and a soft settle.

## 12 · Moments (`moments`)

- **Building finished A:** a ring of dust settling. **B:** dust, then a small
  flag on the roof for a moment. **C:** dust and a few twinkles.
- **Grave A:** a small wooden cross. **B:** a rounded headstone. **C:** an
  earth mound with flowers. How long a grave stays is a question for the
  review: until something is built there, or fading after a year.

## 13 · Weather fading in and out (`weatherfade`)

Clear, then rain in the approved look B arrives and leaves.

- **A · Today:** the weather switches on and off at once.
- **B · Fade:** the rain fades in and out over about a second.
- **C · Clouds first:** the light dims first, then the rain arrives; the rain
  stops before the light returns.

Every option follows the style guide: light from the north-west, straight
top-down drawing, sparse animated weather (rule E3), and the approved night
tint. Snow, puddle and leaf placement here is hand-placed for the review; in
the game it would follow the regions and tiles the world records.
