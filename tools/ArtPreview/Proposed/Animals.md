# Animals: round 2 proposal

The livestock you agreed for late development (a chicken for eggs, a sheep for
wool, a cow for milk) drawn for the first time, and the rest of the horse you
approved in round 1. Nothing in the game draws animals yet; these are mockups
so the look is settled before the mechanics arrive. There are no predators.

Compare `sheet-animals.png` with the approved agents sheet. The wide picture in
the top-left corner, `horse.lineup`, puts the horse in all four facings side by
side, bare on the top row and ridden on the bottom row, so you can check that
the new facings match the two you approved. It is only a review aid, not an
asset.

## How they are drawn

Every animal is drawn the way your approved horse is, so they sit together with
the people:

- **Seen from straight above.** No legs show from above, as with the horse. A
  body is a few rounded parts placed one behind the other (rump, belly, chest,
  neck, head) and joined into one shape.
- **The same dark outline as the people and the horse** (1E2226), a darker rim
  on the south-east, a lighter rim on the north-west, and the usual soft shadow
  falling to the south-east.
- **One drawing per animal turns to every facing.** Markings such as the cow's
  patches are fixed to the animal's body, so they turn with it.
- **Sizes that read beside a person:** the cow is a little shorter than the
  horse but wider, the sheep is clearly smaller and rounder, and the hen is
  small but drawn a little larger than life so you can still see her.

## Asset by asset

- **cow.S, cow.E, cow.N, cow.W.** A dairy cow: a broad, boxy body with squared
  hips, cream hide with irregular dark patches (one over the hips, a big one on
  the belly, a shoulder spot and a patch round one eye), a short neck, ears out
  to the sides, short tan horns in front of them, a pink nose with nostrils,
  and a thin tail with a dark tuft. The pale patched coat is what keeps it
  apart from the brown horse.
- **sheep.S, sheep.E, sheep.N, sheep.W.** A round, lumpy cream fleece. The edge
  is scalloped, and a few rounded tufts inside are lit from the north-west, so
  it reads as wool rather than a smooth egg. A black face with ears out to the
  sides peeks out under a wool top-knot.
- **sheep.shorn.E.** The same sheep after shearing: a slimmer, smooth body one
  shade darker with a light sprinkle of stubble, the same black face, and just
  a small tuft left on the head. This is the state that shows a sheep has been
  sheared for wool.
- **chicken.S, chicken.E, chicken.N, chicken.W.** A red hen: a plump, round
  rust-brown body, darker folded wings down both sides, a paler golden head
  with a red comb along the crown, a yellow beak poking out of the outline (the
  same trick as a person's nose), and a dark tail fanned at the back. I chose a
  red hen rather than a white one so she does not look like a third pale animal
  next to the cow and sheep.
- **cow.E.16, sheep.E.16, chicken.E.16.** The 16 px mid-zoom versions, drawn
  separately rather than shrunk. Each keeps its silhouette and one mark: the
  cow is a cream block with dark patches and a pink nose, the sheep a round
  cream fleece with a dark head, and the hen a small rust body with a red comb,
  yellow beak and dark tail. They are easy to tell apart at this size by size
  and colour alone.
- **horse.N, horse.W.** Your approved horse turned north and west. The drawing
  is copied unchanged, so it is the same size, colours and shading: mane, ears
  and pale blaze toward the facing, saddle on the back, tail behind.
- **horse.rider.S, horse.rider.N, horse.rider.W.** The approved rider turned to
  the other three facings. Facing you, you see the face and fringe and both
  hands on the reins over the horse's neck. From behind, you see the back of
  the head with its hair whorl; the hands are hidden in front and the reins run
  forward to the bridle. Facing west, the nose pokes out and both hands hold the
  reins. Boots show on both flanks in every facing.

I checked the copied horse against the approved horse.S, horse.E and
horse.rider.E: every solid pixel is identical.

Every entry is also there as a bare transparent `.sprite`.

## Style guide rules applied

- **Section 2 ramps:** every colour is a step of an existing ramp, so no new
  colours are needed. The cow's hide, the wool and the horns' highlight use
  Cloth; the cow's patches use the darkest hair colours; the horns use Timber
  highlight; the pink nose uses Berry; the sheep's face uses the darker Iron
  steps (lighter than the outline, so the face still shows); the hen uses Rust,
  with a Timber tail, a Berry comb and a Gold beak. The horse keeps its Timber
  body and Hair 1 mane.
- **L1, L2:** light from the north-west on every body, the soft south-east
  shadow under every animal, kept inside the tile.
- **N1:** each animal stays inside its 32 px tile. Only the very tip of the cow
  and horse tails touches the edge in some facings, as with the approved horse.
- **S4:** each animal is identifiable at 16 px by silhouette plus one colour
  mark.

## Where this departs from the guide, and why

- **Outline colour.** The guide gives world sprites an outline in the edge step
  of their own colour (L4) and people the dark 1E2226 outline (L5). The approved
  horse uses the people's outline, so all the animals do too, to keep the horse
  and the livestock looking like one family that moves around with the people.
- **The chicken is larger than life.** A true-scale hen would be a few pixels
  across; she is drawn about 14 px long so she still reads at full zoom.

## Not done

- Facings other than north, south, east and west, walking frames, and lying,
  eating or sleeping poses. The drawing turns to diagonals already; they are
  not shown as entries yet.
- The horse's cargo state (the content list mentions rider or cargo).
- Young animals (chicks, lambs, calves), animal pens, feeders and coops.
- Nothing in the game draws animals yet, and nobody has checked these in the
  running game; they were judged only from the rendered PNGs.
