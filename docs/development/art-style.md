---
title: Pixel-art style guide
type: development-reference
status: active
updated: 2026-10-09
---

# Pixel-art style guide

This page holds the rules for the game's pixel art. Computment approved this
direction in the October 1 art review: it keeps the look the game already
had and tightens it into rules you can check on a rendered picture. The
decisions themselves are in
[Interface and art](../game-design/interface-and-art.md#pixel-art-and-generated-images).

All art is drawn in C# code in the Godot client
(`src/ClankerWorld.GodotClient/UI/Graphics/`, `UI/Map/`, `UI/MenuScene.cs`),
generated once at startup from fixed seeds. There are no image files.

## Drawing and reviewing art

`tools/ArtPreview/` renders the client's real art generators to PNG files
without the engine, so art can be reviewed before it reaches the game. Its
[README](../../tools/ArtPreview/README.md) lists the commands.

- New or changed art starts as a proposal in `tools/ArtPreview/Proposed/`,
  drawn against these rules, with a note for the owner. The renderer builds
  a review page where computment approves, changes or rejects each picture.
- Approved art moves into the client unchanged. Compare the client's
  renders (`baseline`) with the approved proposal (`proposed`): approved
  pictures must match pixel for pixel.
- Godot stores colour channels by truncating, while the preview renderer
  rounds. Hex colours match either way, but a blended or part-transparent
  colour can land one step lower in the game. Snap such colours in client
  code when an exact match matters.
- Art for content that is agreed but not in the game yet stays in
  `tools/ArtPreview/Proposed/` until the feature is built. The feature's
  pull request moves it into the client.

## 1. Purpose and non-goals

- Keep the character of the current art: calm ground, friendly rounded
  shapes, outlined sprites, the existing hue families, soft shadows.
- Fix what reads badly: flat, speckled ground; side-view mountains;
  ring-shaped hills; agents with no facing; roofs that are two flat tones.
- Not isometric. Not a new palette. Not a redo of the interface frames,
  fonts, logo, nameplates or Town border dashes (agreed September 29–30).
- Everything stays code-drawn and deterministic, generated once at startup.

## 2. Master palette

Every colour comes from a ramp. A ramp has an **edge** (darkest, used for
outlines and creases), **shade**, **base** (the overview colour today),
**light** and **highlight**. Keep the base of every ground ramp as it is;
the overview map and shore bands depend on it. Adding a colour means adding
a step to an existing ramp; a new hue family needs a row here and the
owner's nod.

| Ramp | edge | shade | base | light | highlight |
| --- | --- | --- | --- | --- | --- |
| Grass | 3B5E3A | 527F4F | 5F8F5B | 6FA069 | 86B37A |
| Forest grass | 2E4D35 | 395F41 | 426D4B | 4E7D56 | 5E8E64 |
| Forest floor | 34483A | 435B41 | 4D684A | 597759 | 6A8866 |
| Dense forest floor | 2B4030 | 36503A | 3F5D42 | 4A6B4C | 5A7D5A |
| Scrub grass | 66593A | 86784C | 988857 | A8975F | BBAA70 |
| Dry scrub / brush | 5E5339 | 7E7250 | 8F8159 | 9C8E64 | B0A275 |
| Tundra | 5A675B | 757F75 | 869586 | 96A596 | A8B6A8 |
| Sand | 7E6E4A | A08F66 | BAA77B | C8B78C | E3D6B5 |
| Rock / mountain | 4A4542 | 625B56 | 756D68 | 8B837D | A49C95 |
| Peak | 5F5955 | 8E8A85 | AEB2B0 | C4C7C5 | EEF3F1 |
| Snow | 9AAAA8 | B8C6C4 | CCD7D1 | DCE5E0 | F4F8F6 |
| Fertile soil | 4A3A2A | 5C4B35 | 735F45 | 86704F | 9A8460 |
| Ocean | 24405C | 2A4F73 | 325F89 | 3E6F9A | 5C8DB5 |
| Lake | 3A5F7A | 4A7B9D | 598FB3 | 6A9FC0 | 8ABBD6 |
| River | 2F5A75 | 3B7294 | 4786AB | 5695B8 | 7FB4CF |
| Shallow water | 345A78 | 3F6E92 | 4B7FA7 | 6A9AC0 | 9CC3DB |
| Road dirt | 6E5538 | 977852 | B99A6B | C9AC7C | D9C08F |
| Timber | 3F2A1A | 6E4E31 | 8A6440 | A77C52 | D2AC77 |
| Thatch | 6B5528 | A98A45 | D2AE5E | E6C77B | F0DA9A |
| Clay tile (House) | 5E2E22 | 9E4E34 | C66A45 | E08E64 | EFA982 |
| Slate (Blacksmith) | 2B2E33 | 4A4E55 | 62666E | 80858E | 9A9FA7 |
| Grey timber (Warehouse) | 343C43 | 59656F | 758390 | 97A5B0 | AEBBC4 |
| Silo wood | 54462F | 8E7A58 | B7A07A | D3C09A | E4D4B4 |
| Dyed shingle (Tailor) | 3E2B47 | 6E4F7C | 8F6A9E | B08CBE | C8A8D4 |
| Green plank (Workshop) | 30372D | 566150 | 6F7C6A | 8E9B88 | A8B4A2 |
| Doorstep stone | 5F5848 | 8C7F66 | B9AB8E | C9BDA2 | DED3BC |
| Iron | 3E3A37 | 524C48 | 6C6560 | 8A827C | A69E98 |
| Rust / iron ore fleck | 7A4426 | A9643C | B7774C | C98A5A | E0A070 |
| Gold | 8A6A1E | B8902E | D9AE3C | F2CC5E | FFE28A |
| Diamond | 3F7E86 | 5FB4BE | 7FD3DC | B4EEF2 | E8FFFF |
| Canopy (broadleaf) | 2E4A2A | 476B36 | 557D3E | 6C9A4B | 8DB660 |
| Canopy (conifer) | 1F3E31 | 2F5B45 | 3F7358 | 5E9278 | 86B89A |
| Canopy (orchard) | 3C5F2E | 4C7A3A | 5E8C45 | 79A657 | 9BC66F |
| Fruit | 9A4E1E | C8702E | E0893F | F6C27A | FFE0A8 |
| Berry | 7A2A2E | A33A3F | C4474B | F08A8A | FFC2C2 |
| Clay | 6A3020 | 8E4428 | B8623C | D5825A | EFA882 |
| Cloth | 75674D | A09170 | CABC99 | E8DCC0 | FFF5DF |
| Item ink (outlines) | 2A1A10 | | | | |
| Agent outline | 1E2226 | | | | |

Skins (6): F0C8A0, C99A6E, 8D5E3C, E2B48A, 5E3B24, B8845A. Hair (6):
3A2A1C, 6B4226, 1E1A18, A8742E, 2E2420, 7A3A22; elder hair D9D6CF.
Shirts (6): 3F6FA8, B0523E, 4E8A5A, C19A3A, 7A5A9E, 3E8C8C. These stay.

Rule P1: a 32 px ground tile uses its base plus at most two other steps of
its own ramp for mottling, and at most two colours from another ramp for
motifs (pebbles, tufts). Rule P2: a sprite uses at most five steps of its
main ramp plus accents. Rule P3: shades are a step of the ramp, never a
black overlay; the ramps already shift hue toward blue in the shade.

## Interface text and status

Light and Dark both use at least **4.5:1** contrast for text against the
surface behind it. Ordinary ink on parchment retains **7:1**. The minimum
also applies to hover, pressed, focused, disabled, read-only, placeholder
and selected text, wooden-bar captions, dialog titles, tags and count badges.
Faint ink remains available for decorative lines and icons; use readable
muted ink for secondary text.

Keep the existing palette, fonts and generated frames. When a colored
button or badge needs stronger text, use an existing dark ink or cream from
that palette. A status label may use ordinary ink when its colored ink
would fall below the minimum on a selected card.

The Godot UI smoke check builds both themes and checks their registered
text colors against their generated style surfaces, including transparent
parents and blended selection highlights. Every newly registered text
color needs a corresponding surface/state assertion. It also checks custom
count/branch badge ink and historical-list text. Deceased family buttons
retain full text opacity and say **died**.

Status must have a word, number, shape or tooltip as well as color. For
example, branches have numbers, hunger is named in the Agents tooltip,
condition bars have captions and amounts, and deceased profiles have text
labels. Light and Dark share this baseline; separate high-contrast and
color-blind modes are outside it.

## 3. Light, shadow and outline

- L1 Light comes from the **north-west**. North and west faces are lit,
  south and east faces shaded, on every roof, boulder, mound and head.
- L2 Ground shadows fall **south-east**: an ellipse offset (+1, +3) px for
  32 px sprites, colour (0.05, 0.08, 0.05) at alpha 0.28; buildings use
  (0.04, 0.06, 0.05) at 0.30 offset (+2, +3). Shadows stay inside the tile
  or footprint and never land on a neighbouring tile.
- L3 Item icons get a complete one-pixel outline in a dark shade of the
  darkest neighbouring colour mixed toward ink 2A1A10 (the existing rule).
- L4 World sprites (trees, sites, crops) get a one-pixel outline in the
  **edge** step of their own ramp, never pure black.
- L5 Agents keep the 1E2226 outline. Buildings keep a one-pixel edge in
  their ramp's edge step around the roof. Ground tiles have **no** outline
  and no grid lines.

## 4. Scale and readability

- S1 Ground tiles are 32 × 32; the mid-zoom atlas is 16 × 16 and is drawn
  separately, not scaled down: keep the silhouette and the one identifying
  mark, drop the rest.
- S2 Items are 16 × 16 with an empty one-pixel margin, scaled only by whole
  numbers. Interface glyphs are 12 × 12 in two colours.
- S3 An agent is drawn in a 32 px cell shown at 1.35 × the tile, so the
  figure is about 22 px tall in the cell: head disc 11 px, shoulders 18 × 11.
- S4 Every tile must be identifiable at 16 px by its base colour alone, and
  every sprite at 16 px by silhouette plus one colour accent.
- S5 Nothing is anti-aliased; soft edges come from a half-alpha rim pixel
  at most, as the transitions already do.

## 5. Terrain

- T1 **Calm.** A tile is its base colour plus soft mottling in at most two
  neighbouring ramp steps, in irregular blobs 4–9 px across covering 20–45%
  of the tile, never a dotted or grainy pattern. On top, at most six small
  motifs (tuft, pebble, crack, lichen) covering at most 8% of the tile.
- T2 **Two variants.** v0 is the clean tile; v1 adds one or two motifs.
  Both tile seamlessly with themselves and each other: motifs stay one pixel
  clear of the edges and mottling blobs may cross edges only if they
  continue on the opposite edge.
- T3 **Mountains and peaks are one landform**, drawn from the world's
  elevation across many tiles rather than as a picture per tile. Tile
  elevations blend into one smooth surface with spurs and gullies, shaded
  from the north-west with the Rock ramp; snow covers the highest ground and
  turns blue-grey on shaded faces; a thin light line marks bare crests. The
  relief is fully opaque over Mountain and Peak tiles and fades softly onto
  the land around them, so a range's edge follows the land, not tile squares.
  The client renders it per 16×16-tile chunk (`UI/Map/ReliefRenderer.cs`).
- T4 **Hills** are faint foothill shading of the same surface over their own
  ground, strongest next to the mountains, never rings or mounds.
- T5 **Fertile soil** reads as tilled earth: furrows two shades apart
  running east–west four pixels apart, a few clods in the light step.
- T6 Snow is smooth with one or two drift ridges in the shade step and
  sparse highlight dots; tundra snow adds grass and stone specks. Sand has
  ripples in the light step and a few shade grains. Rock has two or three
  flat facets and short cracks. Scrub keeps the bush motif. Tundra has
  lichen patches in the light step.
- T7 The base colour of every style stays within 4% of today's value.
- T8 **Snow edges** thin out instead of stopping: a ragged rim, a few loose
  clumps, then half-transparent frost on the neighbouring ground, never
  further than a quarter of the tile. Rims facing north or west take the
  light step; lee rims facing south or east take the shade step, two pixels
  deep at 32 px. The ends of each edge piece keep the ordinary corner
  reaches so long boundaries join. Only snow and tundra snow edges do this
  (`UI/Map/SnowEdges.cs`); every other surface keeps its plain soft rim.

## 6. Water, shores and fords

- W1 Base colours unchanged; the block stays a seamless 16 × 16-tile repeat.
- W2 Crests are 2–5 px highlight dashes with a one-pixel shade trough,
  covering at most 3% of the block, gathered in some stretches and sparse
  in others.
- W3 Depth: ocean carries slow blotches in the shade step (10–20% of the
  block); lakes carry blotches in the light step; rivers stay the clearest.
- W4 Shallow water keeps its caustic net. Shores keep the current masks:
  shallow band and foam on coasts and lakes, a quiet bank on rivers.
- W5 A **ford** is a line of four to six flat stones (Rock ramp) across the
  river tile with lighter water (Shallow light) between them; it reads at
  16 px as a dotted bar across the river.

## 7. Roads and bridges

- R1 Packed dirt in the Road dirt base with a worn edge one to two pixels
  wide in the shade step, feathered with alpha breaks so it never reads as
  a stroke. On sand and snow the worn edge uses the edge step.
- R2 At most six pebbles per tile in two greys (Rock light/highlight).
- R3 A doorstep path is five pixels wide from the Road centre to the door.
- R4 A **bridge** is a plank deck 20 px wide (NS or EW): planks three
  pixels with one-pixel seams (Timber shade), rails two pixels in Timber
  edge, two posts at each end, and a 30% shadow band on the water on both
  sides. At 16 px: a 10 px deck, no seams, one-pixel rails.

## 8. Trees, natural sites and crops

- N1 A sprite stays inside a 30 × 30 area of its tile with a one-pixel
  margin; one tree or site per tile.
- N2 Canopies use three tones plus the edge outline, with the highlight
  disc offset (−3, −3) toward the light. Conifers keep the star layers.
- N3 Every site has the L2 shadow. Fruit and berries are discs of at least
  three pixels with a one-pixel highlight; ore flecks are at least two
  pixels; gold is Gold light, iron is Rust, diamond is Diamond highlight.
- N4 Picked and harvested states keep the plant's silhouette minus its
  yield. Depleted states keep the site's own colours as low rubble or a dug
  hollow; the single grey blob used for every depleted site today goes.
- N5 Crop fields are overlays on the Fertile soil tile: rows run east–west.
  Prepared shows furrows only; seeded adds dots in Timber light; sprout adds
  small two-pixel shoots; mature shows the crop; harvested shows stubble
  and clods. Grain is Thatch light/base at maturity; potatoes are low leafy
  mounds (Canopy shade/light); greens are round rosettes (Canopy light with
  Cloth highlight veins).
- N6 Orchard trees share the broadleaf silhouette with the orchard canopy
  ramp; fruiting adds five fruit discs; the sapling is a small lumpy disc.
- N7 Cacti use the Canopy ramp of the conifer, a cool blue-green that stands
  out on warm sand, with Cloth light spines, Gold flowers and Berry fruit.
  The barrel cactus and the saguaro are ribbed domes seen from above; the
  saguaro's longer shadow shows its height. The prickly pear is a clump of
  overlapping oval pads. They stand only on desert cactus cover, about one
  tile in five (`UI/Graphics/CactusSprites.cs`).

## 9. Buildings

- B1 The footprint is exact; the roof is inset three pixels with a
  one-pixel edge in the kind's edge step; the ground inside the footprint
  outside the roof is left transparent except for shadow and doorstep.
- B2 Eaves cast a two-pixel shadow (L2) along the south and east sides.
- B3 Roof material per kind: House clay tiles (offset rows of 4 × 2 tiles
  in three tones); Farmhouse thatch (jittered strands, bound ridge);
  Blacksmith slate (6 × 3 slabs) with a forge yard; Warehouse grey timber
  planks along the ridge; Tailor dyed shingles; Workshop green planks;
  Silo conical seams; Store striped awning over the door; Market timber
  hall; Town Hall slate with a small bell tower;
  Port timber planks on piles over water; Restaurant clay tiles with a
  pot sign; Clinic shingles with a cross-and-herb sign. The Market is a
  timber hall without stalls; its 1×1 stalls stand on a plaza of packed earth
  in front of it, drawn as an area of the Road's surface. The plaza fits its
  stalls: two back-to-back rows, each facing an aisle, a path from the
  hall's door, and one open column on each side. Eight stalls take a 7 × 4
  plaza.
- B4 The ridge runs along the long side as a one-pixel light line; the
  north or west half is lit and the south or east half shaded. Square roofs
  are hipped.
- B5 The door is six pixels wide in Timber edge with a one-pixel lintel on
  the facing side, over a 6 × 3 doorstep in Doorstep stone; the doorstep
  path starts there.
- B6 One identifying feature per kind, at least four pixels, placed on the
  roof or in the yard: chimney with a two-pixel cap on the shaded half
  (House), hay cart or stacked sheaves (Farmhouse), anvil and ember glow
  (Blacksmith), split loading doors (Warehouse), spool sign (Tailor),
  hammer sign (Workshop), vent cap (Silo).
- B7 At 16 px the material pattern drops to two tones and the feature stays
  only if it is at least two pixels.

## 10. Agents

- A1 Straight top-down: the head and hair sit a little above the shoulders,
  so the shirt colour shows in every facing. Facing is shown by the face and
  fringe, a nose pixel at the side, a hair whorl when facing north, the
  turned shoulders, and the near hand reaching forward; never by a side view.
- A2 Eight facings in order S, SW, W, NW, N, NE, E, SE. Boots show ahead of
  the body; a standing figure facing north hides its feet, which would read
  as ears.
- A3 Frames: still, walk 1 and walk 2 (feet alternate ±2 px, hands swing
  1 px). Carrying puts a crate (Timber ramp, 8 × 6) in front toward the
  facing. Working swings a tool in the leading hand. Talking adds a 5 × 4
  speech mark off the head to the north-east. Hurt adds a cream bandage
  band across the head.
- A4 Shirts keep the six variant colours with a one-pixel shade band on
  the south-east; skin and hair keep the palettes above.
- A5 Infants stay a wrapped bundle; children are 0.78 scale; elders have
  D9D6CF hair, a slight stoop (head one pixel south) and a one-pixel cane.
- A6 Horses (later): an outlined body about 17 × 11 that fits its tile, in
  Timber shade/base with a mane in Hair 1, head toward the facing, a saddle;
  a rider is an agent torso on the saddle.
- A8 Animals, handcarts and boats face the same eight directions as agents,
  drawn as real pixel art for each diagonal rather than a blurred rotation.
- A7 At 16 px an adult keeps a 7 px head, 10 × 6 shoulders and the outline.

## 11. Items

- I1 16 × 16, one-pixel empty margin, full outline (L3), three or four
  shades per material from the ramps, highlight top-left, shade bottom-right.
- I2 Every item has its own silhouette and colour so no two are confused
  at 16 px on parchment.
- I3 Tool tiers share the handle and differ in the head: wooden (Timber
  light), stone (Rock light), iron (Iron highlight with a one-pixel glint).
- I4 Cooked food sits in or on something: a bowl for porridge and stew, a
  plate for a meal, a loaf shape for bread.

## 12. Interface glyphs

- G1 12 × 12, two colours, two-pixel strokes, nothing in the outer pixel.
- G2 New glyphs match the weight and corner style of the existing ones.
- G3 The weather icons share one two-bump cloud with a flat base; what falls
  from it sits below the cloud, and the storm's bolt starts inside it. The
  sun is an eight-sided disc with a ring of short rays.

## 13. Main Menu valley

- M1 Keep the composition, the Towns and the animation hooks.
- M2 Depth: three ridge bands, the far one mixed 45% toward the sky, the
  middle 25%, the near one at full colour with crease lines and scree.
- M3 A tree line of varied heights; fields with rows and hedges; a river
  with a one-pixel lighter bank and a reflection band; a bridge and path
  that match R4 and R1; buildings that match B3; foreground grass tufts.
- M4 Dusk keeps the warm window light F2C14E and adds a faint mist band.

## 14. Seasons, night and weather

- E1 Seasons are one tint per season multiplied onto the grass and canopy
  ramps: spring toward Grass highlight, summer base, autumn 20% toward Fruit
  base, winter 30% desaturated with snow cover where the world says so.
- E2 Night is one multiply tint, 3C4C6E at 45%, with no visibility change.
- E3 Weather stays an animated, sparse overlay; no opaque shapes.

## 15. Critic's checklist

1. Is light from the north-west on every lit/shaded pair?
2. Does every ground tile stay calm under T1 and tile without seams?
3. Are mountains, peaks and hills relief seen from above, with no side view?
4. Does every sprite sit inside its tile with a south-east shadow?
5. Do item icons have a complete outline and an empty margin?
6. Is every asset identifiable at 16 px?
7. Do colours come from the ramps, with bases within 4% of today?
8. Do buildings show material, ridge, door and one identifying feature?
9. Do agents read their facing from fringe, hands and feet?
10. Are picked, harvested and depleted states distinct per site?
11. Is the drawing deterministic and cheap enough to generate at startup?
12. Is it better than the current art at 1× and 3×, while still looking like ClankerWorld?
