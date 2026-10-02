# Mountains, peaks and hills as one landform

You said a mountain is a whole thing, not a lot of little ones, and that the
round hills had to go. This proposal stops drawing mountains tile by tile.
It draws each range as one piece of relief, worked out from the map's own
heights.

## What changed

- **No more tile pictures for mountains and peaks.** Today every Mountain
  tile gets a small grey triangle and every Peak tile a triangle with a white
  tip, so a range looks like a grid of tents. Round 1 swapped the triangles
  for small pyramids, which was still a grid. Both are gone.
- **One height map for the whole range.** The map already stores a height
  for every tile. The new layer blends those heights into a smooth surface
  that ignores tile edges. It then adds ridges, called spurs, that run
  downhill from the crest, with gullies between them, the way real ranges
  wear away. Each spur has two flat faces. The face toward the north-west
  sun is lit and the other is in shade, so it reads as a crisp ridge rather
  than a bump.
- **Light from the north-west, seen from straight above.** Slopes facing
  north-west use the light and highlight steps of the Rock ramp. Slopes
  facing south-east use its shade step, with the darkest step only in deep
  gullies. Nothing is drawn from the side.
- **Snow where the map has peaks.** Snow covers the highest ground and the
  Peak tiles. It reaches further down the gullies than over the ridges, so
  its lower edge breaks into fingers. Sunlit snow is near-white and snow in
  shade is blue-grey, so the split between the two halves marks the crest.
  A narrow band of paler bare rock sits beside the snow.
- **A few lines.** Where the crest is bare rock, a one-pixel light line runs
  along it and joins the snow caps. The deepest gullies get a dark line.
- **Scree.** Small stones lie along the foot of the range and at the bottom
  of the lower gullies. They are sparse, so the ground stays calm.
- **The edge follows the land, not the tiles.** The rock always covers every
  Mountain and Peak tile completely, so the old tile art never shows through.
  Past those tiles the edge wanders: it reaches out along ridges and pulls
  back in gullies, rounds off the staircase steps of the tile grid, and ends
  in a half-transparent rim pixel. Bare Rock ground touching the range counts
  as part of it, as a rocky apron at its foot. Where a river runs past, the
  rock fills the river's bank strip, and the height map dips along the river
  so it sits in a valley.

## Why it reads as one landform

The whole range is shaded from one surface. One sunlit north-west flank and
one shaded south-east flank meet along a single snowy crest. Spurs run from
that crest down both sides, and nothing repeats from tile to tile. In
`range.32` and `range.16` the range reads as a single mass with a crest at
normal zoom and at mid-zoom. The small outlier in the north-east reads as
its own small mountain. `massif.closeup` shows the crest, spurs and snow at
1×.

## How hills look now

There are no rings or mounds. Hills are the lower slopes of the same height
map, shaded faintly over whatever grows there. Slopes facing the sun get a
little warm light and slopes facing away a little cool shade. You can see
the grass or forest through both. The shading is strongest right at the
mountain and fades over the hill band. The mountain's spurs carry on into
the foothills as low ridges, so the shading forms broken streaks running
away from the range rather than a ring around it. The overview map colours
are unchanged.

## What the game needs to draw it

- **A relief layer cached per chunk.** The drawing code takes the map and a
  rectangle of tiles, so the game can draw 16 × 16-tile chunks when they
  first come into view and keep them. Heights never change after a world
  loads, so a cached chunk never goes stale. Chunks with no mountains or
  hills come out empty and can be skipped.
- **Where it sits.** It goes over the ground and its soft edges, and under
  roads, buildings, trees and agents. While it is on, the old hill overlay is
  not drawn. Mountain and Peak tiles can keep their plain ground underneath,
  because the layer covers them.
- **No seams.** Each chunk looks a little way past its own edges, so chunks
  drawn separately fit together exactly. `chunk-seams` is the range built
  from four separate chunks, and its note confirms that no pixel differs from
  one whole render. It also checks the east–west wrap and that every pixel
  over a Mountain or Peak tile is opaque.
- **Cost.** A 16 × 16-tile chunk full of mountains takes about 25 to 35 ms
  in an optimised build on the machine that drew these pictures. The preview tool
  runs an unoptimised debug build, so the time in its note is about three
  times that. Only chunks with mountains or hills cost anything. Drawing one
  chunk per frame, or on a background thread, avoids a hitch. One shared
  helper that reads the river and coast bank shapes needs a lock if chunks
  are drawn off the main thread.
- **16 px.** The mid-zoom map uses the same layer drawn at 16 px per tile,
  from the same heights, so both zooms show the same range.

## If world generation changed

You said hills and mountains should be rethought in how they are generated
too. These changes would make this drawing look better still:

- **Long ranges built around a crest.** Generate mountains along winding
  ridge lines, a few tiles wide, with heights falling away on both sides,
  instead of blobs of high noise. The drawing is best when there is a clear
  crest to light on one side and shade on the other.
- **A minimum size.** Very small groups of mountain tiles (under about six,
  or narrower than two tiles) turn into lumps. Either grow them into a small
  massif, merge them into a nearby range, or turn them into hills or a rock
  outcrop.
- **Peaks as summits on the crest, not strips.** Choose a few high points
  along each crest as Peak tiles, with lower saddles between them. That
  gives separate snow caps and natural places for passes.
- **Room at the top.** Today heights stop at 255, so the top of a big range
  becomes a flat plateau. A gentler curve below the cap would keep a real
  crest.
- **Foothills that follow distance to the crest.** Make the hill band a
  smooth slope two to four tiles wide, based on distance from the crest,
  rather than every tile within three steps of a mountain. Its shading would
  then fade out more evenly.
- **Rivers that start in the mountains.** Rivers should begin high up and
  run down valleys. In the test range, a river crosses a peak at full peak
  height. The drawing carves a valley for it, but the heights should say so.
- **Snowline by climate.** Cold worlds could carry snow lower down and dry
  ones higher up. The drawing already takes snow from height, so this only
  needs a climate-based offset.

## Limits and open points

- Where spurs that point in slightly different directions overlap, the rock
  sometimes shows a small closed swirl, like a knot in wood. It is rare and
  small, but you can spot it at 2×.
- The note that times one chunk in the preview reports the slow debug
  build. The 25 to 35 ms figure above is from an optimised build.
- This adds colours the style guide does not list yet: a paler bare-rock
  ramp near the snow, between the Rock and Peak ramps, and the warm light and
  cool shade laid over foothills. Approving this approves them. The Rock and
  Snow ramps are used as they are.
- This replaces the style guide's rules T3 (mountain tiles) and T4 (hill
  overlays). If you approve it, those two rules should be rewritten to
  describe the relief layer.
