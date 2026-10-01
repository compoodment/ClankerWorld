# Round 1: the reference set

The owner asked to see a style guide and reference mockups first, to judge
whether the improvement is real, before the full catalogue in BRIEF.md
section 5 is drawn. Round 1 draws the subset below for each family, with
the same Ids and rules as the full brief. Everything else waits for round 2.

- **terrain**: Grass, ForestGrass, ForestFloor, Sand, Rock, Mountain, Peak,
  Snow, FertileSoil, Tundra: both variants at 32 and 16; hill masks and
  on-grass; `Grass.tiling`, `Mountain.tiling`, `Sand.tiling`. Other styles
  may fall back to the current generator in `Apply`.
- **water**: the four blocks; `ford.ns`, `ford.ew`.
- **roads**: `straight_ns`, `straight_ew`, `corner_ne`, `tee_nes`, `cross`,
  `end_n`, `diag_ne_sw`, `doorstep_s`, `straight_ew.on_Sand`,
  `straight_ew.on_Snow`; `bridge.ew`, `bridge.ns` plus their `.16`.
- **nature**: Broadleaf, Conifer, BroadleafStump, BroadleafSapling,
  BerryBush, BerryBushPicked, WildGreens, FiberPlant, StoneOutcrop,
  StoneOutcropDepleted, IronOutcrop, GoldOutcrop, ClayBank, HerbPatch, at
  32 and 16. Others fall back to the current generator.
- **crops**: `field.grain.*` five states, `field.potato.mature`,
  `field.greens.mature`, OrchardGrowing, OrchardFruiting, OrchardPicked,
  `field.tiling`.
- **buildings**: House.1x1, House.1x2, House.1x2.door_East, Farmhouse.1x2,
  Blacksmith.2x2, Warehouse.2x2, Silo.1x1, TailorShop.1x1, plus new
  Store.1x1, Market.2x2, MarketStall.1x1, TownHall.3x4, Port.2x4,
  Clinic.1x1; House.1x1.16 and Blacksmith.2x2.16.
- **agents**: adult.v0 all eight facings, adult.v0.walk1/walk2 for S, W, N,
  E; adult.v0.carry.S, work.S, talk.S, hurt.S; child.v2.S, elder.v3.S,
  infant.v0.S; adult.v1.S and adult.v4.S (variants); horse.S, horse.E,
  horse.rider.E; adult.v0.S.16, adult.v0.E.16; turnaround strip.
- **items**: the seven missing (water, wild_greens, potato, porridge, stew,
  meal, diamond); improved wood, stone, grain, bread, clothing, wooden_axe;
  new stone_axe, iron_axe, iron_pickaxe, rope, basket, water_jug, bandage,
  medicine, map, book, coin, sickle.
- **menu**: valley.day and valley.dusk with the M-rules applied.
- **glyphs**: skipped in round 1.
