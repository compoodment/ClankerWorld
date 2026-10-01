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
