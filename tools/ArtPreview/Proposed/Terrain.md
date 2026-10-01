# Terrain proposal

## Round 2

You approved grass, forest grass, both forest floors, sand, rock, snow and
tundra as drawn. They are unchanged: every one of those tiles is pixel for
pixel the picture you approved. Mountains, peaks and hills are being redesigned
separately, so they still show the round-1 drawing here. This round redraws
fertile soil and adds the six ground styles that were still using today's
tiles.

### Fertile soil

You said it was "just lines". It now reads as ploughed farmland seen from
above:

- **Raised ridges.** Each tile has four soft ridges of turned earth running
  east–west. A ridge has a lit north face, a flat top, a shaded south side and
  a dark trough, so it looks raised rather than drawn on.
- **Clods, not ruled lines.** The lit face is broken into lumps of earth a few
  pixels long, and some lumps catch a brighter highlight on their north-west
  corner. The trough deepens in short stretches, and the ridge swells over
  them, so no row is perfectly straight or even.
- **Things lying on the soil.** A few clods sit on the ridge tops, one has
  rolled into a trough, and there is a small stone. One tile in four also has
  a pale straw fleck and a pale root. These are rare on purpose, so they do not
  line up in a grid across a big field.
- **Damp patches.** A long, dark stretch of trough where water has soaked in;
  the busier tile has two.
- **Seamless.** Ridges run on into the next tile, and both variants match at
  their edges.
- **16 px.** The same four ridges per tile, squeezed into four rows each, with
  one clod and the stone.

**With crops on top.** The crop fields keep their own rows. At 32 px every soil
ridge carries two crop rows, and its lit face and trough fall exactly on a crop
row's lit and dark lines. At 16 px there is one crop row per ridge. I checked
the sprouting, prepared, harvested and ripe grain fields and the ripe potato
and greens fields over the new soil. The rows stay in step and nothing clashes.
Under sprouts, alternate rows of soil look very slightly darker. That reads as
crop rows on and between the ridges, not as a pattern.

### The six new ground styles

Each keeps today's overview colour; each tile's average colour stays within 2%
of it. Each has soft patches in its own colours plus one motif that tells it
apart from its neighbours, also at 16 px.

- **Scrub grass.** Dry, yellow-green grassland with soft darker patches, dry
  grass tufts with pale tips and two low olive scrub bushes. The busier variant
  adds a third bush and more tufts.
- **Scrub sand.** Paler sandy ground with soft patches, a wind ripple, a couple
  of dry tufts and one small bush. The busier variant adds a second bush.
- **Dry scrub.** Hard, bare dry ground with pale patches, small grey pebbles
  and one dry tuft. The busier variant adds a pebble and a short crack.
- **Dry brush.** The same dry ground with dense, dark, twiggy brush clumps that
  have a few olive leaves on their lit side. The busier variant adds a third
  clump and dry tufts.
- **Desert brush.** Warmer desert ground with grey-green sage clumps and
  hairline cracks. The busier variant adds a clump and a crack.
- **Tundra snow.** Thin snow with small holes where the grey-green tundra shows
  through. Each hole has a shaded north rim and a bright snow lip on its south
  side. A few tufts of tundra grass poke through. The busier variant adds a
  hole, a tuft and a stone.

Dry scrub, dry brush and desert brush have almost the same overview colour, so
their motifs keep them apart: grey pebbles, dark clumps and grey-green sage.

New 3×3 samples: `ScrubGrass.tiling`, `DryScrub.tiling` and
`FertileSoil.tiling`.

### Style rules applied

- **P1 and P3:** every colour is a ramp step; no dark overlays. Each tile
  borrows at most two colours from other ramps (straw and stone on soil, the two
  bush olives, the two sage greens, the two tundra greens, pebble greys).
- **L1:** light from the north-west on ridges, clods, stones, bushes, brush,
  sage and the snow holes.
- **T1 and T2:** calm patches with no grain; motifs one pixel clear of the
  edges; the variants share their edges, so tiles join without seams. I checked
  this by comparing pixels across every tile edge.
- **T5, T6, T7:** as described above.

### Where I departed from the guide

- **Soil ridges are eight pixels apart at 32 px, not four (T5).** Four-pixel
  rows leave room only for one light and one dark line, which is what read as
  "just lines". Eight pixels give each ridge a lit face, a top and a shaded
  side. At 16 px they are four pixels apart, so the ridges are the same size on
  the ground at both zooms, and they still line up with the crop rows.
- **Soil uses four steps of its ramp plus a highlight** (edge, shade, base,
  light, highlight), where T1 asks for two besides the base. The ridges are the
  surface itself, not patches on it, like rock's facets in round 1.
- **New ramps.** Scrub sand, dry brush, desert brush and tundra snow have no
  row in the style guide. Each uses the nearest guide ramp, moved so that its
  base is that style's overview colour. Approving these tiles approves those
  shades.
- **Bush colours** are the two olives of today's scrub bushes, not a guide
  ramp, so the scrub keeps the bush you already know.
- **Scrub grass patches cover about 13%**, like approved grass, below T1's 20%,
  for the same reason: less repetition across big areas.

### Not in this round

Mountains, peaks and hills (being redesigned separately), the water styles
(drawn by the water blocks) and Unknown.

## Round 1

Kept for reference. Round 2 above replaces the fertile-soil paragraph and the
"not redrawn yet" list below.

This redraws the ground tiles while keeping today's look: the same colours, calm
ground and light from the north-west, with more life in the surfaces and
mountains drawn from above. Every tile keeps its exact overview colour. Each
tile's average colour stays within 2% of that colour, well inside the 4% the
style guide allows (T7).

### What changed

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

### Style rules applied

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

### Where I departed from the guide, and what is left

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
