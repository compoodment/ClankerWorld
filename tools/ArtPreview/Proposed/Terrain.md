# Terrain: round-1 proposal

This redraws the ground tiles while keeping today's look: the same colours, calm
ground and light from the north-west, with more life in the surfaces and
mountains drawn from above. Every tile keeps its exact overview colour. Each
tile's average colour stays within 2% of that colour, well inside the 4% the
style guide allows (T7).

## What changed

**Grass and forest grass.** Today these are flat colour with a few "^" tufts.
Now each tile has small, soft, rounded patches one shade darker, covering about
an eighth of the tile, plus two tufts whose tips catch the light. The busier
variant adds tufts and a pebble on grass, and a few fallen leaves on forest
grass. The patches are small and spread evenly, so a field of grass reads as one
gentle texture rather than a repeating pattern.

**Forest floor (and dense forest floor).** Darker patches and a little moss in
the light step, plus leaf litter in warm browns. The busier variant adds a
fallen twig.

**Sand.** Pale, soft patches and two or three wind ripples. Each ripple is a lit
arc with a thin shaded side. The busier variant adds a few darker grains.

**Rock.** Today rock is flat grey with two cracks. Now the stone is split into a
few large flat facets in three tones. A dark crease shows only where the stone
steps down from a lit facet, and there is one crack. The busier variant adds a
second crack and loose stones.

**Mountains.** Today's side-view triangles are replaced by the same idea seen
from straight above: a pyramid of rock whose four faces meet at the summit. The
north-west face is lit, the south-east face is in shade with a dark crease along
its ridges, and the two side faces fade into the ground. The upper half of each
lit face is one step brighter, so the summit catches the light. Scree lies
around the foot. One variant has a broad peak with a small one at its foot; the
other has twin peaks and a knoll. Like today's triangles, the peaks stay inside
their tile, so mountain tiles meet on flat, stony ground without seams.

**Peaks.** The same pyramids, taller and filling the tile, under a ragged snow
cap. Snow is bright on the lit and side faces and blue-grey on the shaded face,
so it reads against the pale rock.

**Snow.** Smooth, with faint soft patches, one or two drifts (a low shaded arc
with a lit crest) and a little sparkle. Both variants share the same soft
patches; the busier one only adds a second drift and a little more sparkle, so
the two mix without visible blotches.

**Tundra.** Grey-green with soft darker patches and small lichen spots in the
light step. The busier variant adds a stone and a couple of dry-grass tufts.

**Fertile soil.** Now reads as tilled earth while staying calm. Furrows run
east–west four pixels apart, each a one-pixel line only one shade darker than
the soil, with short soft breaks along it. There are no dark edge lines and no
lit ridges, so it is about as busy as the grass. The rows bend by a pixel here
and there and carry a few small clods. The furrows run on from tile to tile, so
a patch of soil looks like one ploughed field.

**Hills.** Today a hill is a ring-shaped mound. Now it is a broad lit crescent
on the north-west and a shaded crescent on the south-east, open at the sides so
it never closes into a ring, with the ground showing through. One variant has a
single mound, the other two smaller ones.

**16 px tiles.** These are drawn separately rather than shrunk. They keep the
base colour and one identifying mark: patches, ripples, facets, a peak, a snow
cap or furrows. Every surface can be told apart at 16 px.

## Style rules applied

- **P1 and P3:** every colour comes from a style-guide ramp step, never a dark
  overlay. Each tile borrows at most two colours from other ramps, for pebbles,
  leaves or shaded snow.
- **L1 and L5:** light from the north-west on mounds, pyramids, ripples,
  pebbles, clods and cracks. No outlines and no grid lines on ground tiles.
- **S1 and S4:** separate 16 px drawings that can be identified by colour and
  one mark.
- **T1:** soft patches with no lone pixels or one-pixel streaks (a clean-up
  pass removes them). No more than six small details per tile.
- **T2:** two variants, the second busier. Details stay one pixel clear of the
  edges. The patch pattern repeats across tile edges and is identical in both
  variants near the edges, so tiles join without seams. The `.tiling` samples
  show this for grass, mountain and sand.
- **T3, T4, T5, T6, T7:** as described above.
- Everything is drawn from fixed seeds, the same on every run, and the whole set
  is generated in well under a second.

## Where I departed from the guide, and what is left

- **Grass patch coverage is lower than T1 asks.** Grass has about 13% coverage
  (about 6% at 16 px) where T1 asks for 20–45%, and uses one shade step instead
  of two. The game has only two variants, and the clean one fills three tiles in
  four. At 20% or more, or with light patches added, the reference scene showed
  a visible wallpaper repeat. Calm ground mattered more.
- **Mountains are separate peaks, not one ridge crossing the tile.** I tried a
  ridge that runs across every tile from corner to corner (T3's wording). Large
  mountain areas then turned into diagonal stripes, which is too close to the
  striped look you rejected. The pyramid keeps T3's lit and shaded faces, crease
  and scree, and it is the direct top-down version of today's triangles.
- **Peak snow covers the whole summit, not only the lit side.** The shaded part
  is blue-grey (the snow ramp's darkest step), because snow only on the lit side
  read as a sparkle rather than a cap.
- **Rock facets cover about half the tile** (T1's limit is 45%). They are the
  stone surface itself rather than patches on top of it. Both rock variants
  share the same facets so they always meet cleanly; the busier one only adds
  details.
- **Dense forest floor is drawn too.** It is not in the round-1 list, but it
  shares the forest-floor drawing and the scene uses it.
- **Not redrawn yet:** scrub grass, scrub sand, dry scrub, dry brush, desert
  brush, tundra snow, the water styles and Unknown. They still use today's
  tiles until round 2.
- **Check with the crops round.** Crop fields are drawn over the fertile-soil
  tile, so the furrows should be checked under the proposed field overlays.
