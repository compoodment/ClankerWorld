# Round 4: animals, the yard, item icons, construction and abandoned Towns

Proposed and reviewed on October 7, after an audit of what in the game had
no approved art. Every picture is drawn in the approved style and palette,
lit from the north-west. Every approved picture from earlier rounds is
unchanged; I compared all 127 animal pictures with the approved files.

Render a family with
`DOTNET_ROLL_FORWARD=Major dotnet run --no-build -- proposed <out> <family>`.

## What the owner decided

| Family | File | Answer |
| --- | --- | --- |
| `young-animals` | `Animals.cs` | Approved: chick, lamb, calf and foal in eight facings, and a horse with a bare back. |
| `animal-yard` | `Yards.cs` | Option **A**, the split-rail pen on trampled earth. |
| `items-round4` | `ItemsRound4.cs` | Approved: iron fittings, refined gold, plain gold ornament, saddle, cooked eggs, milk porridge, berry porridge, fruit porridge, rich meal and leather sack. The crude wooden axe and pickaxe keep the wooden axe and pickaxe icons ("keep the og's"); their new drawings were dropped. |
| `construction` | `BuildingStates.cs` | Approved: three stages before the finished picture, for every building and built thing. |
| `abandoned` | `BuildingStates.cs` | **A turning into B after a while**: neglected (A) when a Town is abandoned, falling apart (B) once it has stayed abandoned for a full season. |

The owner also agreed in chat that a sheep in a household looks shorn for the
first half of each wool cycle and woolly again after that; wild sheep stay
woolly.

## Young animals and the bare horse (`young-animals`)

- **Chick:** a ball of yellow down about half the hen's length, with wing
  stubs and a tiny orange beak. It has no comb or tail fan yet, so it reads as
  a chick rather than a small hen.
- **Lamb:** the sheep's fleece in tight, small curls with no top-knot, and the
  black face with ears large for its head.
- **Calf:** the cow's patched cream hide on a rounder body, with a big head,
  big ears, a pink muzzle and no horns.
- **Foal:** one Timber step lighter than its mother, with a short mane, a
  brush tail and the pale blaze. It never wears a saddle.
- **Horse with a bare back:** the approved horse with its saddle left off, for
  wild horses and tamed horses with no saddle on. The ridden horse always has
  its saddle.
- Each young animal is about two-thirds of its parent's length, and is drawn
  at 32 px and at 16 px.

## The animal yard (`animal-yard`)

- **A (chosen):** a split-rail pen on trampled earth, with grass surviving
  along the fence.
- **B:** a woven wattle pen with a thatched lean-to along the back.
- **C:** a grass paddock behind a light post-and-rail fence.

Every option has an open gate on the door side, centred on the door tile,
with trampled ground running out through it. The plank gate is swung inward.
A water trough stands just inside the gate and the feed is in the far corner.
Each option was shown at 2×2, 2×4 and 4×2, with an east gate, and with
animals inside.

## Item icons (`items-round4`)

| Item | Icon | Before |
| --- | --- | --- |
| Iron fittings | A corner bracket with nail holes and two nails, in the iron bar's steel | Crate |
| Refined gold (#793) | One bright cast ingot with a stamped mark | Ore nuggets |
| Plain gold ornament (#793) | A thick polished band with no stone | Crate |
| Saddle | Side view: cantle, horn, a red blanket edge, a stitched fender and an iron stirrup | Leather |
| Cooked eggs | Two eggs fried in an iron pan with a wooden handle | Raw eggs |
| Milk porridge | The approved bowl and spoon, the oats in a ring of milk | Plain porridge |
| Berry porridge (#794) | The approved bowl and spoon with three purple berries | Apple |
| Fruit porridge (#794) | The approved bowl and spoon with pear slices | Apple |
| Rich meal | Bread, a fried egg and greens on a wooden trencher | Plain meal |
| Leather sack | The approved sack's shape in tanned leather, with a pale stitched seam | Plain sack |

## Construction (`construction`)

Each stage is drawn from the building's own approved picture, so it always
matches the finished art:

1. The site is cleared to bare earth and staked out with a string line, and
   timber and stone are piled beside it.
2. A stone footing and a timber sill go round the outline, with posts at the
   corners and every ten pixels along the walls. The door gap stays open and
   the floorboards are half laid.
3. The walls are up and the roof is half on, finished from the back so the
   open rafters face the Road. A ladder leans on the front wall.

Fences, piers, bridges and lanterns use their own parts in the same order:

- **Fences:** stakes, then the back half of the fence, then all of it.
- **Piers and bridges:** piles following the outline, then edge beams and half
  the deck, then most of the deck.
- **Lanterns:** a dug hole, then the post stub, then the whole fitting.

## Abandoned (`abandoned`)

- **A, neglected:** the roof fades a little toward grey, with moss in clumps,
  a few missing shingles, weeds round the walls and over the doorstep, and the
  door boarded with two crossed planks.
- **B, falling apart:** as A, plus a ragged hole in the front of the roof
  showing broken rafters, fallen planks below it, thicker weeds and a sapling
  by a back corner. Fences lose bits of rail, and piers and bridges lose
  planks.

## Not done

- 16 px handcarts and boats for medium zoom (#914) are a separate round.
- The construction stages need the host to send each active site before the
  game can show them (#1090).
