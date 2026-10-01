# Roads and the bridge: round-1 proposal

What to look at: `out/proposed/sheet-roads.png` next to
`out/baseline/sheet-roads.png`, and the scene `out/scene/scene-roads-32.x3.png`
and `scene-roads-16.x4.png` next to `scene-current-*`.

## What stayed the same

Every piece has the same shape as today. The proposal keeps the game's own
piece logic (centre, arms, rounded bends, diagonals, filled corners, and the
part of a diagonal drawn by the tile beside it), so Roads still join only
along the Road whatever the neighbours are. The dirt is the same colour, so
the overview map and the sand-and-snow test that picks the darker edge are
unaffected.

## What changed

- **Packed dirt.** Today's dirt is a grain of about a hundred light and dark
  specks per tile. Now the dirt is mostly plain, with two or three soft,
  lumpy dry patches in the lighter dirt shade, four to seven pixels across.
  They sit near the middle of the Road, so the crown looks a little drier
  than the margins. A few small clods (a lit lump with a shaded underside)
  give it texture without grain. Patches and clods never touch a tile edge,
  so tiles join without seams.
- **Pebbles.** Two or three grey stones per tile instead of a speckle: one
  2×2 stone, plus one or two smaller ones. Each is lit from the north-west
  and has a one-pixel shadow to the south-east.
- **Worn edge.** The edge is one or two pixels of the darker dirt shade. Its
  line wobbles in and out over runs of a few pixels, with short faded breaks,
  and there are half-strength pixels just outside it on the grass. It reads
  as a worn margin, not an outline. On diagonals the wobble also hides the
  stair-steps.
- **Sand and snow.** The edge uses the darkest dirt step and never breaks,
  with a half-strength fringe outside it. The Road stays clearly visible on
  pale ground, without today's two solid stripes that look like rails.
- **Doorstep path.** Five pixels wide (today it is about eight), with a
  one-pixel edge and no wobble, so it stays straight and lines up with the
  building's doorstep stone. At 16 px it is three pixels wide.
- **16 px pieces.** These are drawn separately, not shrunk: a one-pixel worn
  edge, one pebble and one or two light clods. Mid zoom stays calm and still
  looks like the same Road.
- **Bridge.** A plank deck 20 px wide that runs edge to edge across its tile:
  3 px planks with 1 px darker seams, some planks a little more weathered, a
  nail at each plank end, 2 px dark rails, and a 30% shadow on the water (one
  pixel on the north side, three on the south, for the north-west light).
  Each end of a deck tile carries half a post, so two deck tiles meet at one
  whole post with a lit cap, and the bank ends show a slimmer end post. At
  16 px the deck is 10 px, with plank tones alternating instead of seams,
  one-pixel rails and post nubs. The north–south deck is the same drawing
  turned.

## Style-guide rules applied

Only colours from the section 2 ramps: Road dirt, the two Rock greys for
pebbles, and Timber for the deck. R1: a feathered worn edge in the shade
step, and the edge step on sand and snow. R2: no more than three pebbles per
tile, in two greys. R3: a five-pixel doorstep path. R4: deck width, planks,
seams, rails, posts and shadow, plus the 16 px version. L1 and L2: north-west
light on stones and post caps, and the deck's shadow to the south-east. S1:
the 16 px pieces are drawn separately. S5: the only soft edges are
half-strength rim pixels.

## A decision for you, and what I could not do

- **The Road beside a bridge (please decide).** Today the map does not count
  a bridge tile as a Road neighbour, so the Road on each bank ends in a
  rounded cap half a tile short of the river. Today's deck hides that gap by
  hanging a third of a tile over each bank. A deck that stays inside its
  tile cannot do that, so in the proposed scene a strip of grass shows
  between the cap and the deck. Two extra sheet entries show the difference:
  `crossing.today` (the gap) and `crossing.rule` (the Road running straight
  onto the deck). The fix is a small rule when this art goes in, not more
  art: count a deck tile as Road when working out its neighbours' links. My
  recommendation is to adopt that rule.
- **Posts where two deck tiles meet.** The deck cannot tell whether its
  neighbour is more deck or the bank, so it draws half a post at both ends.
  Where two deck tiles meet, this makes one post in the middle of the span.
  That looks like a railing post, which seems right. A post only at the
  banks would need the same neighbour information as the rule above.
- Only the round-1 set is drawn. The other baseline samples (`corner_sw`,
  `lone`, `diag_nw_se`, `beside_diag`, `doorstep_e`, the four straight
  variants) come from the same code, and the scene already uses all four
  variants. I checked every combination of neighbours, doors, both sizes,
  all variants and both edge types: none fails, and a full 32 px set of
  pieces takes about a tenth of a second.
