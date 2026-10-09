# Animal walking steps

**Your pick (October 7): option B.** The game now draws it for every animal.

Animals used to slide across the map in their standing drawing while agents
stepped. Both options keep the approved drawings as the standing pose, pixel
for pixel, and add two walking frames that alternate on each step, as the
agents' walk frames do.

- **A, nod and sway (not chosen):** no legs. Parts ahead of the neck move a
  unit forward on one step and back on the other, the body shifts half a unit
  sideways, and the tail tip swings.
- **B, stepping hooves (chosen):** on one step a front hoof shows just ahead
  of the chest on one side and the hind hoof just behind the rump on the
  other; on the next step the other pair. Each hoof is a small outlined oval in
  the animal's leg colour: dark brown for the cow and calf, Timber edge for the
  horse, the sheep's black face colour for sheep and lambs, Timber shade for
  the foal. The tail tip swings opposite the hind hoof. Hens and chicks show
  one foot behind them at a time, in beak yellow or orange, and bob their
  heads.

At 16 px every movement doubles, since a unit there is half a pixel.

`AnimalWalk.cs` (family `animal-walk`) draws both options for all eleven
animal looks (saddled, bare and ridden horse, cow, sheep, shorn sheep, hen,
foal, calf, lamb and chick) in eight facings at 32 and 16 px. Each strip shows
the standing drawing, then steps 1 and 2. `ArtContractChecks` keeps the
game's option B frames identical to these.
