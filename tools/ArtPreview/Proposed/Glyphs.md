# Interface glyphs: round 3 proposal

## Round 3

Every icon you approved in round 2, and every icon left unchanged, is kept
pixel for pixel; a check compares each one with its round-2 picture. Only the
ten you sent back are redrawn.

**One fix each, for your notes:**

- **Hammer.** The handle now runs straight into the middle of the head, with
  no gap between them. The head is the same.
- **Map.** A folded paper map: three panels folded like an accordion, so the
  top and bottom edges zigzag, with the middle panel shaded and a dotted route
  across it. There is no room for a readable X at this size, so the route
  stands in for it.
- **Pencil.** A sharpened pencil: from the top, a green eraser, a dark band,
  the green body, the pale shaved wood and a green point. The body is a pixel
  wider, so it no longer reads as a stick.
- **Play.** A true triangle, exactly the same above and below its middle line,
  with single-pixel corners. It is as tall as Pause and sits half a pixel right
  of centre, which makes a triangle look centred.

**Two options each, for the ones without a note.** Pick one per icon. Option
**a** stays close to today's game icon and only fixes its size and position;
option **b** is a fresh, clearer drawing.

- **Sun.** a: today's eight-sided disc and ring of short rays, one pixel
  bigger so it sits exactly in the middle. b: a square disc with eight rays two
  pixels long.
- **Rain, Snow, Storm.** a: today's mound cloud, drops, flakes and bolt, kept
  off the top edge and centred. b: your approved Cloud with straight falling
  streaks, two plus-shaped snowflakes, or the bolt starting inside the cloud.
- **Speech.** a: today's bubble with two text lines, ten pixels wide and
  centred top to bottom. b: a solid round bubble with three dots.
- **Arrow.** a: the round-2 arrow (Back's chevron as its head) in the same
  8 × 8 box as Close and Plus. b: a solid head like the arrow on Door.

**weather.set** shows Sun a and b, Cloud, then the a and b versions of Rain,
Snow and Storm side by side at 3× on parchment, so you can judge the weather
icons as a family. The b clouds sit two rows higher than Cloud, to leave room
for what falls below them.

## Round 2

The small two-colour icons on buttons and panels (12 × 12 pixels, a main ink
colour and a green accent). I reviewed all 32 icons the game has today, redrew
the 15 that did not match the rest in size, weight or position, kept the other
17 exactly as they are, and added the 14 new icons the agreed interface needs.

Compare `sheet-glyphs.png` with the baseline sheet. Every icon is shown at its
real size in the Light theme's ink (33261A) with the green accent (3E7D3A), and
the sheet enlarges it. All 46 are in the set, so it can be reviewed as a whole.

### The rules I checked every icon against

- **Nothing in the outer pixel.** An icon uses only the middle 10 × 10, so
  icons never touch the edge of a button or each other. Several of today's
  icons broke this.
- **Line icons use two-pixel strokes** (Close, Back, Menu, Pause, Plus, Arrow,
  Check). **Outlined shapes** use a one-pixel ink outline with green inside
  (Folder, Book, Speech, Coin). **Round icons** share the same circle as Info.
- **Centred.** An icon's box sits on the middle of the grid wherever its shape
  allows, so a row of buttons lines up.
- **The four weather icons share one cloud.**

### Redrawn (15)

Each keeps its idea and as much of its drawing as possible.

- **Map.** The old one was a thin frame with a V inside, which reads as an
  envelope (mail). Now a folded map in three panels with a dotted route across
  it.
- **Play.** It was ten rows tall next to Pause's eight. Now the same height as
  Pause, moved a pixel right so the triangle looks centred.
- **Sun.** The disc sat half a pixel up and to the left, and the rays were
  uneven. Now a centred disc with eight evenly spaced rays. It also serves as
  the summer icon.
- **Cloud.** It touched the left edge and looked like a mound. Now a cloud with
  two bumps and a flat base, centred.
- **Rain, Snow, Storm.** Their clouds touched the top edge and were a different
  shape from Cloud. Now all three use the new cloud, with the same drops,
  flakes and zigzag bolt below it.
- **Leaf.** The stem ran into the bottom-left corner. Same leaf, stem one pixel
  shorter, moved down to centre it. It also serves as the autumn icon.
- **Globe.** It was twelve pixels wide and touched both sides. Now it uses
  Info's circle with the same continents.
- **Find.** The crosshair ticks ran off all four edges. Same target, with the
  ticks kept inside the circle.
- **Close.** The X was one row shorter than it was wide and sat half a pixel
  high. Now a square X centred on the grid. It is the most used icon in the
  game.
- **Back.** It sat one row higher than Close, Menu and Pause. Same arrow, one
  row lower, so Back and Close line up in a panel header.
- **Key.** The ring touched the left edge. Same key with a shaft one pixel
  shorter.
- **Pencil.** The end touched the right edge. Same pencil, one pixel left.
- **Speech.** The bubble was twelve pixels wide and touched both sides. Same
  bubble, two pixels narrower.

### Kept as they are (17)

Filter, Info, Person, House, Scroll, PersonPlus, Menu, Pause, Flower,
Snowflake, Gear, Book, Box, Door, Folder, Link, Tree. They already sit inside
the margin with a consistent weight. Filter, Snowflake, Box, Tree, Folder and
Door are an odd number of pixels tall or wide, so they sit half a pixel off the
middle; that cannot be avoided without changing their shape, and it does not
show at normal size, so I left them. Door also sits low on purpose, to line up
with the menu icons beside it.

### New (14)

- **Market.** A market stall: a striped awning, a row of goods under it, the
  counter and its legs.
- **Coin.** A coin with a square hole, a little smaller than the round icons so
  it never reads as Info. Its colour is still the open question from round 1
  (gold or copper); the icon itself uses the theme's accent.
- **Heart.** A solid heart.
- **Hammer.** At the same slant as Pencil: a solid head and a green handle.
- **Clock.** Info's circle with green hands at three o'clock.
- **Boat.** A sailing boat (for the Port): hull, mast and a green sail.
- **Horse.** A horse's head facing left, with a green mane and eye.
- **Warning.** A triangle with a green fill and an exclamation mark.
- **Check.** A tick.
- **Plus, Minus.** Two-pixel strokes, the same size as Close.
- **Arrow.** Points east. The game can flip it for west and turn it for north
  and south, so one drawing serves all four. Its head is the same shape as
  Back.
- **Lock.** A padlock with a thin shackle like Link, a solid body and a green
  keyhole.
- **Trash.** A bin with a handle, a lid and a ribbed body.

### Style guide rules applied

- **G1:** 12 × 12, two colours, two-pixel strokes for line icons, nothing in
  the outer pixel. The drawing code refuses an icon that breaks the size or
  margin rule, so a mistake cannot slip through.
- **G2:** new icons copy the weight and corner style of the existing ones: the
  Info circle for round icons, Back's chevron for the arrow head, Pencil's slant
  for the hammer, Link's thin line for the lock's shackle.

### Not done

- The weather and season icons are shown here in ink and green like the rest;
  in the game they keep their natural colours (a yellow sun, a blue snowflake),
  which this proposal does not change.
- The icons are drawn in the game's own text format, so an approved icon can be
  copied straight into the game, but nothing in the game uses the new ones yet.
- Nobody has checked these in the running game; they were judged from the
  rendered PNGs at 1×, 2× and 4×.
