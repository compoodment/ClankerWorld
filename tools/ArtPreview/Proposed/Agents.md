# Agents: round 1 proposal

The people stay the ClankerWorld people you know: a round head with hair seen
from above, sitting a little above a coloured shirt, with skin-coloured hands at
the shoulders, the dark outline and the same six skins, hair colours and shirt
colours. What is new is that they can now face eight ways, walk, carry, work,
talk and look hurt. Children, elders and infants each have their own look, and
there is a first horse.

Compare `sheet-agents.png` with the baseline sheet. The turnaround strip in the
top-left corner shows all eight facings side by side. In
`scene-agents-32.x3.png` and `scene-agents-16.x4.png` the six people in the
Town are drawn facing the way the scene says, using these sprites at both zoom
levels.

## How you can tell which way someone faces

Nothing is drawn from the side; it is still a view from straight above. The
facing shows only through where things sit:

- **Face and fringe.** Facing south, the face shows as a band under the fringe
  with a small nose shadow, as today. Turned sideways, only a sliver of face
  shows on that side, and the nose pokes one pixel out of the outline, which
  is the clearest cue at full zoom. Facing north there is no face, just hair
  with a small whorl on the crown.
- **Shoulders and hands.** The shoulders turn across the facing. The hands sit
  at the shoulder ends, and when someone is seen sideways the hand nearer to
  you reaches a little forward.
- **Feet.** Small brown boots poke out ahead: below the shirt facing south, to
  the side facing east or west.
- **Light.** It still comes from the north-west whatever the facing: a soft
  shine on the top-left of the hair, a darker rim on the bottom-right of the
  hair and shirt, and a soft shadow the head casts onto the shirt.

The head keeps today's small lift toward the top of the screen, so the shirt
colour (the main way to tell people apart) still shows below the head in every
facing.

## Asset by asset

- **adult.v0, eight facings (S, SW, W, NW, N, NE, E, SE).** One drawing turned
  to each facing as described above. The diagonals sit between their
  neighbours: a three-quarter face and nose on the south diagonals, a sliver
  of face and the nose on the north diagonals.
- **Walk frames (walk1, walk2 for S, W, N, E).** The feet alternate, one two
  pixels ahead and one two pixels back, and the hands swing one pixel the
  other way. When a step carries a boot clear of the shirt, a short dark-grey
  trouser leg joins it to the body so the foot never floats. Walking north, the
  heel of the back foot peeks out below the shirt instead.
- **carry.S.** An 8 × 6 wooden crate held out in front, a hand on each side. It
  works in every facing; facing away, the crate shows over the head with the
  hands beside it.
- **work.S.** A pick swung out from the leading hand, with a few faint pixels
  behind its head to suggest the swing.
- **talk.S.** A small white speech mark with two dots, up and to the right of
  the head, with a one-pixel tail.
- **hurt.S.** A cream bandage across the head with a little knot, and an uneven
  stance with one foot dragged behind.
- **child.v2.S.** The same drawing at 0.78 scale, with the head shrunk a little
  less so a child keeps a child's larger head.
- **elder.v3.S.** Grey hair, the head a pixel forward for a slight stoop,
  shoulders drawn in a little, and a one-pixel cane with a crook. The cane is
  always in the hand nearer to you and planted on the ground, so it is never
  hidden.
- **infant.v0.S.** Still a wrapped bundle, now with a lit blanket, a swaddle
  band, a fold, and a small face looking up with a hair cap. It lies along the
  facing with its head at the back.
- **adult.v1.S, adult.v4.S.** The same drawing in two other variants' colours,
  to show the palettes carry over unchanged.
- **horse.S, horse.E, horse.rider.E.** A first look only; mounts come later.
  The brown body is one outlined shape (rump, barrel, chest), then a narrower
  neck and a long head with two ears, a pale blaze and a darker muzzle. A
  darker mane runs down the neck, a tail tuft sits behind, and a dark saddle
  sits on the back. A rider sits on the saddle with hands forward on the reins
  and a boot showing on the near flank.
- **adult.v0.S.16, adult.v0.E.16.** The 16 px mid-zoom versions: a 7 px head and
  10 × 6 shoulders with the outline kept. They keep the face crescent, the
  sideways nose pixel and the feet, and drop the shading. At this size a hand is
  one skin pixel with one outline pixel beyond it, because a fully outlined
  hand turned into a dark blob that looked like an insect leg.
- **adult.v0.turnaround.** The eight facings side by side on grass.

Every entry is also there as a bare transparent `.sprite`.

## In the scene

The same drawing covers every variant (0–5), every life stage and all eight
facings, at 32 and 16 px, plus every frame: still, the two walk frames, carry,
work, talk and hurt. The scene's people therefore show their facings at both
zoom levels. Drawing every one of those combinations at 32 px takes about a
third of a second.

## Style guide rules applied

- **Section 2 palettes:** skins, hair, shirts, elder hair D9D6CF and the
  1E2226 outline are unchanged. Crate, cane, boots, saddle and horse come from
  the Timber ramp, the pick from Iron, and the bandage and speech mark from
  Cloth. The horse's mane is Hair 1.
- **L1, L2:** light from the north-west on hair, shirt, crate, pick and horse.
  The usual soft south-east ground shadow sits under every figure.
- **L5:** agents keep the 1E2226 outline.
- **S3, A7:** about 22 px tall at 32 px, with a head disc of 11 px and
  shoulders of 18 × 11. At 16 px: a 7 px head and 10 × 6 shoulders.
- **A1–A6:** facing shown by fringe, nose, hands and feet; eight facings in the
  game's order; walk, carry, work, talk and hurt frames; a shirt shade band on
  the south-east; children at 0.78; elders with grey hair, a stoop and a cane;
  infants wrapped; a horse with mane, saddle and rider.

## Where this departs from the guide, and why

- **Feet facing north (A2 says "feet above").** Because the head sits a little
  above the shoulders, feet poking out above the shoulders landed beside the top
  of the head and read as ears or horns. Standing figures facing north,
  north-east or north-west therefore keep their feet hidden; when walking, the
  back foot's heel shows below the shirt. The back of the head, the missing
  face and the turned shoulders already make these facings clear.
- **The head is not exactly centred over the shoulders (A1).** It keeps today's
  small lift toward the top of the screen and sits slightly back from the
  chest. Centred exactly, the head covered almost all of the shirt and the
  people stopped looking like the ones in the game now.
- **One new accent colour:** dark grey trousers (the Slate shade step). They
  appear only mid-stride, between shirt and boot.
- **Horse body size:** the body is about 17 × 11 from rump to chest (19 × 13
  with its outline), with the neck and head beyond it, rather than a single
  22 × 12 oval, so the whole horse from tail to muzzle fits inside the 32 px
  tile.

## Not done in round 1

- Only the round-1 list is drawn as separate entries. The other facings for
  the walk frames, horse and rider, and the other stage and variant
  combinations, are not shown as entries, though the drawing already produces
  them and the scene uses them.
- The game does not yet pass a facing or frame to agent sprites (today's
  generator draws one still per variant and stage). The frame numbers used here
  are a proposal: 0 still, 1 and 2 walk, 3 carry, 4 work, 5 talk, 6 hurt.
- The horse is a mockup only; nothing in the game draws it yet.
- Nobody has checked these in the running game; they were judged only from the
  rendered PNGs and the reference scene.
