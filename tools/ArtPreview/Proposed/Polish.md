# Visual polish

On October 9 computment picked eight ideas from a list of visual additions
that would make the map feel more alive. This proposal draws options for
each over the reference Town corner, as stills and short loops at close
and mid zoom where applicable. None changes a game rule; each only shows
what the world already records, more clearly.

## What computment chose

computment answered the review on October 9:

| Idea | Choice |
| --- | --- |
| 1 · Smooth movement | **C**, glide and bob |
| 3 · Chimney smoke | **B**, column |
| 7 · Ground snow | **A**, covered, with footprints |
| 7 · Fallen leaves | **A**, a few |
| 7 · Roof snow | **A**, redrawn to follow each roof's slopes (approved on the second look) |
| 7 · Puddles | postponed: see below |
| 8 · Golden hour | **C**, rose at dawn and gold at dusk |
| 9 · Stock | **A**, at the door |
| 11 · Camera | **B**, ease |
| 12 · Building finished | **C**, dust and twinkles |
| 12 · Grave | **A and B**, both redrawn (approved on the second look); a grave fades after a year of game time |
| 13 · Weather fades | **B**, fade |

Three pieces went back for better drawing at computment's request:

- **Roof snow** (`roof-snow-a2`), approved: computment asked for some snow
  left on the sunny side, and for roofs whose ridge runs north to south not
  to be split north and south. The redraw follows each roof's real slopes:
  a gable roof has two faces, a hipped roof four, and the Silo's cone melts
  on one side. Shaded faces (north and east) stay covered; sunny faces
  (south and west) keep snow near the ridge, then a thin dusting down to
  the eaves. Building shadows and the forge yard stay clear.
- **Graves** (`grave-a2-cross`, `grave-b2-stone`), approved: the wooden cross
  and the headstone, redrawn as 32 px sprites on a fresh earth mound.
- **Puddles**, postponed: the first redraw was bigger and see-through,
  then computment asked for "total different vibes", and a third round
  (`puddles-r3-<look>`, with `-rain` loops showing rings while rain falls)
  offered four moods: **mirror**, **muddy**, **outlined** and **sheen**.
  None matched what computment has in mind, so puddles are postponed, and
  whether they belong in the game at all is open. The drawings stay here
  for whoever picks the idea up again.

## Drawing the pictures

`dotnet run -- proposed out <family>` draws the stills and
`dotnet run -- animate out <family>` writes the loops, 12 frames a second.
The families are `movement`, `smoke`, `weathermarks`, `goldenhour`, `stock`,
`camera`, `moments` and `weatherfade`.

## 1 · Smooth movement (`movement`)

An agent walks four tiles along the main street and a cow four tiles along
the meadow, one tile per world update, as the host reports them.

- **A · Previous look:** each figure jumps a tile at every update.
- **B · Glide:** each figure slides steadily to the tile the latest update
  reports, with the walk frames changing every quarter second. A jump longer
  than three tiles, such as after a reload, still snaps.
- **C · Glide and bob:** approved and implemented by #1461. The client's
  `WalkingMotion` supplies the same quarter-second frames and one-pixel bob;
  `tools/ArtPreview/Implemented/Movement.cs` renders its reference frames with
  `dotnet run -- movement out`. C has left the proposal list; A and B remain
  the review record. Native markers glide to reported tiles over one second,
  freeze while paused, and snap for long jumps or checkpoint rewinds. Boats
  and handcarts also glide, without the agent/animal bob.

## 3 · Chimney smoke (`smoke`)

Approved B now draws in the client through `SmokeArt` and `SmokeLayer`. The
original review drawings remain in `tools/ArtPreview/Approved/Smoke.cs`;
`smoke-client` renders the live drawing and `check` compares all 72 frames.

Smoke rises only from buildings in use: here two Houses with someone inside
and the Blacksmith's forge. The empty Houses stay smokeless, following the
night-lights rule for when a building is lit.

- **A · Wisps:** a few small puffs drift east and fade.
- **B · Column:** a fuller column that leans with the wind.
- **C · Puffs:** one round puff now and then.

## 7 · Weather that leaves a mark (`weathermarks`)

Ground snow A and footprints are implemented in `Implemented/Snow.cs`, using
the client’s `GroundSnowSprites`. `dotnet run -- snow out` writes the approved
stills and loops. Roof dusting in the still remains review context; client roof
snow is separate. Cover follows observed regional snowfall and melts afterwards;
footprints fade within a game hour.

The snow mockups compare cover after snowfall with permanent snow terrain.
The remaining proposals would follow the weather the world records for each
region, and fade after it passes.

- **Ground snow A, implemented:** open ground covered, grass tips and trodden Roads
  showing. **Ground snow B:** patchy cover that thins and melts unevenly.
- **Roof snow A:** snow on the north half of each roof. **B:** roofs covered,
  edges and chimneys still dark.
- **Puddles A:** small puddles on Roads and bare ground. **B:** puddles and
  darker, wet-looking ground.
- **Leaves A:** a few fallen leaves under each broadleaf and orchard tree in
  autumn. Its approved drawing is now in the client and the completed
  [autumn leaf review](../AutumnLeavesReview.md). **B:** a carpet of leaves
  around them, retained here as an unchosen comparison.
- **Footprints, implemented:** agents and animals crossing snow leave prints
  that fade.

## 8 · Golden hour (`goldenhour`)

A day loop: day, dusk glow, the approved night tint, dawn glow, day.

- **A · Amber:** an amber wash of up to 22% at dawn and dusk.
- **B · Amber and glow:** A, plus warm light catching bright roofs and Roads.
- **C · Rose and gold:** rose at dawn, gold at dusk.

## 9 · Stock you can see (`stock`)

Approved A now uses `StoredStockArt` in the client. The original review drawings
remain independently in `tools/ArtPreview/Approved/StoredStock.cs`; baseline
stock and `stock-client` show the live painter. `check` compares all six scenes.

Log, crate and sack piles that grow with what a building actually stores:
empty, some and full, beside the Warehouse and between the Farmhouse and
the Silo.

- **A · At the door:** piles on the ground beside the building.
- **B · Along the wall:** stacks against the building's wall.

## 11 · A smoother camera (`camera`)

**Find** moves the view from the Farmhouse to the east Houses, then the view
zooms in one step.

- **A · Previous view:** the view jumps.
- **B · Ease:** the view glides there in about 0.7 s, slowing as it arrives.
  Its approved drawing is now in the client and the completed
  [camera review](../CameraReview.md).
- **C · Ease and settle:** a gentler start and a soft settle.

## 12 · Moments (`moments`)

- **Building finished A:** a ring of dust settling. **B:** dust, then a small
  flag on the roof for a moment. **C:** dust and a few twinkles.
- **Grave A:** a small wooden cross. **B:** a rounded headstone. **C:** an
  earth mound with flowers. The original review offered two lifetimes:
  until something is built there, or fading after a year. The chosen
  lifetime is recorded above.

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
