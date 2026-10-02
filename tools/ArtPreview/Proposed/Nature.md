# Nature: round 1 mockups

These are the 14 round-1 trees and natural sites, each drawn at 32 px and at
16 px. The sheet shows every asset three ways: on a grass tile, on its own
(`.sprite`), and at 16 px on grass (`.16`). That makes 42 entries. The
reference scene uses the new art for every sprite the game already has; the
three new states have no place in the game yet, so they appear only on the
sheet.

The aim was to improve the current look gently, not replace it. Every
sprite keeps today's shape, size and colours, and the trees changed least.

## What changed on every sprite

- **Light from the north-west** (STYLE L1). Each sprite has a lit top-left,
  a highlight pulled toward the light and a darker rim on the bottom-right.
- **An even one-pixel outline in the sprite's own darkest colour** (L4), in
  place of today's dark disc behind each shape. The fibre plant is the only
  exception: a dark edge on the south-east side of each blade keeps the
  blades thin.
- **A soft shadow to the south-east** (L2), a little wider under the low
  plants so a rim of it shows.
- **The 16 px versions are drawn separately, not shrunk** (S1, S4). Each keeps
  its outline and one clear mark: berries and ore flecks become two-pixel
  dashes, flowers become single pale dots, the conifer keeps its points, and
  the stump drops to two roots.
- Everything stays inside the tile with a one-pixel margin (N1), and the
  drawing is deterministic.

## Asset by asset

- **Broadleaf**: the same lumpy canopy with slightly deeper lobes, a shade rim
  on the south-east, the light patch and highlight toward the north-west,
  and a few darker leaf dimples. At 16 px the highlight moves further
  north-west so the tree still reads as lit from the top-left.
- **Conifer**: the same nine-point star layers, each inner layer shifted
  toward the light, with a small two-by-two lit tip. At 16 px it keeps
  today's dark outer star, so the points do not merge into a disc.
- **Broadleaf stump**: a round cut face with a lit north-west rim, one growth
  ring, the pith and a split, inside a bark ring with four short roots. The
  roots facing the light are lighter.
- **Broadleaf sapling**: a small young canopy in the lighter canopy colours,
  lit from the north-west. The brown stem dot is gone because it read as a
  berry.
- **Berry bush**: the same dark bush with a lit side. Each berry is a two-by-two
  dot with a highlight; at 16 px each berry is a two-pixel dash, so the red
  still shows in the mid-zoom map.
- **Berry bush, picked** (new): the same bush with no berries. Small brown
  stalks mark where the berries hung (N4).
- **Wild greens**: a rosette of broad leaves in place of today's fan. Leaves
  facing the light are lighter and keep today's pale veins. At 16 px it is a
  five-leaf rosette with a pale centre.
- **Fibre plant**: a dark clump with eleven tapered blades of uneven length,
  so it reads as a tuft rather than a starburst. Blades facing the light are
  lighter.
- **Stone outcrop**: the same three boulders. Each now has a flat lit top face
  with a bright north-west edge, a crease where it overlaps the boulder
  behind it, and a shaded south-east side. Two loose stones sit beside it.
  The stone is a step lighter than rock ground, as today's outcrop is.
- **Stone outcrop, depleted** (new): the site quarried flat. It is a level grey
  gravel patch with a shallow dug hollow and a few left-over chips, in the
  outcrop's own stone colours (N4). Nothing on it is raised, so it does not
  read as a smaller outcrop, and it no longer uses the generic grey blob.
- **Iron outcrop**: darker, iron-grey boulders whose faces are stained toward
  rust, with a two-pixel rust vein and rust blocks that become dashes at
  16 px.
- **Gold outcrop**: mid-grey boulders with gold flecks: two-by-two blocks with
  a bright pixel at 32 px, two-pixel dashes at 16 px (N3). Plain stone is the
  lightest outcrop, gold-bearing rock mid-grey and iron rock the darkest, so
  the three differ in tone as well as in their flecks.
- **Clay bank**: the bank keeps today's brown and is lit on its north-west
  crest, with a few grass tufts on top. A bite is dug out of its south-east
  side: the outline follows the cut, and the floor shows fresh orange-red
  clay with the bank's shadow across it, two spade scrapes and two dug
  clods.
- **Herb patch** (new): a low clump of small-leaved sprigs with four small
  pale flowers, each with a gold centre. It is lighter and airier than the
  berry bush. At 16 px it is a lobed clump with four pale dots.

## Colours that need your approval

Most colours come straight from the STYLE ramps. Four additions keep today's
colours:

- **Clay bank brown**: each step sits halfway between the Clay and Fertile
  soil ramps (5A3525, 75472E, 966040, AD7954, C49671). That lands on today's
  clay-bank brown. I suggest adding it to the style guide as a "Clay bank"
  row. The dug floor uses the existing Clay ramp.
- **Outcrop stone**: the Rock ramp one step lighter, with one new highlight
  step (B9B2AB) for the lit edge of the boulder faces.
- **Fibre plant**: today's three yellow-greens arranged as a ramp, with the
  Grass edge as its dark step and one new highlight step (A8BE78).
- **Wild greens vein**: today's pale vein colour, B6CF8A.

## What this round does not do

- Only the round-1 set is drawn. In the scene, the conifer stump, conifer
  sapling, reeds, diamond outcrop, orchard trees (part of the crops family),
  the regrowing sprout and the generic depleted sprite still use the current
  art. The retired wood pile, wild seed patch and fertile-soil site are left
  as they are.
- The game shows one generic "Depleted" sprite for every exhausted site, and
  it has no picked or herb-patch states. Showing the new depleted, picked
  and herb-patch art needs a game change that says which site and state to
  draw. That is outside this art round.
- Checked only in the offline renderer, on the contact sheet and the
  reference scene at 32 px and 16 px. Not yet seen in the running game.

## Round 2

You approved every round-1 sprite as drawn, so those are untouched: each
one renders pixel for pixel as before. Round 2 adds the 14 remaining trees
and site states in the same style, each at 32 px on grass, on its own
(`.sprite`) and at 16 px on grass (`.16`). That makes 42 new entries, 84 in
all. The same rules apply as in round 1: light from the north-west, an
outline in the sprite's own darkest colour, a soft shadow to the south-east,
a separate 16 px drawing, and a one-pixel margin inside the tile.

### Trees

- **Conifer stump**: told apart from the broadleaf stump by a darker cut face
  (today's conifer stump is darker too), two close growth rings, a scaly bark
  ring, five slender roots, and a small bead of amber resin on the rim. At
  16 px it keeps the darker face and the resin dot.
- **Conifer sapling**: a small seven-point star in the lighter needle greens,
  lit from the north-west like the grown conifer. At 16 px it draws its own
  dark star, as the conifer does, so the points stay apart.

### Reeds

- **Reeds**: three olive clumps with stems fanning up out of them, two low
  leaves splaying sideways, and dark brown cattail heads on the tallest
  stems. I kept today's standing stems on purpose. I tried two strictly
  top-down versions (a round tussock with blades all round it, and one with
  long arching blades), and both read as a thorny bush or an insect rather
  than reeds. The brown cattail on a stem is what tells reeds from the fibre
  plant, at 32 px and at 16 px.
- **Reeds, harvested** (new): the clumps cut low, with short stubs that have
  pale cut ends, and the low leaves left. No cattails.

### Picked and harvested plants

Each keeps the plant's shape minus what was gathered (STYLE N4).

- **Wild greens, picked**: the big outer leaves are gone. Short cut stalks
  with pale ends ring the heart and the three young inner leaves, so it reads
  as a small patch that will grow back.
- **Fibre plant, harvested**: the same dark clump with every blade cut to a
  short stub, long and short in turn, each ending in a pale cut.
- **Herb patch, picked**: the herb clump with its flowers and sprig tips
  snipped off. The stems are shorter with pale cut ends and keep their lower
  leaves. At 16 px it is a smaller clump with no flower dots.

### Outcrops

- **Diamond outcrop**: three boulders in a cool blue-grey stone (the style
  guide's Slate colours, which sit right on today's diamond-outcrop greys)
  holding pale faceted crystals. Each crystal is a small diamond shape, lit on
  its north-west facets and shaded on the south-east. At 16 px the crystals
  become pale cyan dashes, like the gold and iron flecks.
- **Iron, gold and diamond outcrops, depleted** (new): drawn like the approved
  depleted stone outcrop: the site quarried flat to a gravel patch with a
  shallow dug hollow and a few chips, in each outcrop's own stone. Each leaves
  a dull trace of its ore, never a bright fleck, so it does not look as if
  there is still ore to take. Iron leaves rust-stained, warmer gravel. Gold
  leaves three single dull gold specks. Diamond leaves one dull crystal shard
  in slate gravel.
- **Clay bank, depleted** (new): the bank dug away, drawn the same way: a
  trodden apron of bank brown round a shallow hollow of fresh clay with spade
  scrapes, a low hump of the old bank left on the north-west, and two dug
  clods. My first draft was a big round pit inside a ring of bank, and it
  looked like a clay pot seen from above, so I dropped it.

### Generic states

- **Regrowing**: one sprout for any site that is regrowing. Two seed leaves
  spread west and east (the west one lit) with a young leaf rising, on a patch
  of loosened earth, and two tiny shoots beside it.
- **Depleted** (fallback): today's grey blob, redrawn for any used-up site
  that has no depleted art of its own. It is a bare, scuffed patch of dry
  earth with two pebbles and a broken twig. It is low and plain, so it says
  "nothing left here" for any plant.

### In the reference scene

The scene now also uses the new conifer stump, conifer sapling, reeds,
diamond outcrop, regrowing sprout and depleted fallback. You can see the
sapling in the grove, the reeds by the river and the depleted patch south of
the street. The retired wood pile, wild seed patch and fertile-soil site still
fall back to today's art. The orchard trees come from the crops mockups. The
new picked, harvested and per-site depleted states appear only on the sheet.

### Colours that need your approval

- **Reeds**: today's two olive greens (6E7F46, 8A9A55) as the middle of a
  ramp, with a darker shade (57653A), an olive outline (3D4A2B) and a pale
  highlight (A6B36C) added round them. This is how the fibre plant colours
  were built in round 1. The cattails use the Timber ramp.
- **Diamond outcrop**: the existing Slate ramp for the stone and the existing
  Diamond ramp for the crystals. No new colours.
- **Iron gravel**: the Iron ramp tinted a quarter of the way toward Rust
  shade, as the iron outcrop's faces already are.
- **Regrowing and Depleted** use the existing Fertile soil ramp. The conifer
  stump's resin bead uses the existing Gold ramp.

### Still not done

- The game sends only one "depleted" state and one "regrowing" state for
  every site, and it has no picked or harvested states. A game change is
  needed before the per-site depleted art and the picked and harvested states
  can show in play. Until then the scene shows the depleted fallback and the
  regrowing sprout.
- Checked only in the offline renderer, on the contact sheet and in the
  reference scene at 32 px and 16 px. Not yet seen in the running game.
