# Night lights: round 1

What to look at: `out/proposed/sheet-nightlights.png`, or the single pictures
in `out/proposed/nightlights/`. Render just this family with
`dotnet run -- proposed out nightlights`. Each building picture shows three
states side by side: by day, at night with nobody using it, and at night in
use. The street pictures line every Town design along one Road, and the
`Street.frame00` to `Street.frame15` pictures are one step every eighth of a
second, so the light's drift and the fires' flicker can be seen.

The shapes come from the game's own
`src/ClankerWorld.GodotClient/UI/Graphics/NightLightShapes.cs`, so the map
draws exactly what is shown here. The proposal composites them the way the
map does: the night wash over ground and roofs, then light that warms and
brightens the ground under it (strongest wins where lights overlap), then the
lantern fittings on top.

## What the owner asked for

computment's notes in chat on October 3: roofs never glow; the light comes
from the ground beside the walls, where the windows are; a Warehouse shows
little light; windows go on the front and sides only, the Farmhouse
included; a House is lit only while someone is inside; light must not look
circular, must seem to come from somewhere, and should move slightly; street
lanterns are built by agents, in styles B and C, and need no fuel; and every
asset gets lights, including the designs approved on October 1 that are not
in the game yet.

## Rules shown

- **House, Farmhouse, Store, Tailor Shop, Clinic, Workshop and any other
  building:** lit while someone is inside (or, for a work building, while a
  job runs there). Light falls from the windows on the front and both sides,
  never the back, and spills out of the open door. A front only one tile wide
  has room just for the door.
- **Blacksmith:** while a job runs, the forge in its yard glows and flickers;
  the glow is kept off the roof. Door and side windows light while someone is
  inside.
- **Warehouse:** no windows. A lantern on a bracket by the loading doors is
  lit only while someone fetches or stores goods, and its light falls away
  from the wall. The unlit lantern shows by day.
- **Silo, Market stall:** never lit.
- **Restaurant:** lit while open with someone inside; the big Restaurant also
  hangs a lantern over its terrace tables.
- **Market hall:** while traders are in, light spills from the open arcade
  along the whole front and from the side windows.
- **Town Hall:** during a meeting, tall windows close together on the front
  and sides, including the wings' end walls, and a wide spill over the
  forecourt. The bell tower stays dark.
- **Port:** the lantern on the T-head burns every night so boats can find the
  Port; the shed lights while someone is in it.
- **Street lanterns:** B, a stone lamp with an open flame that flickers, and
  C, a post with an arm that hangs a glass lantern over the Road, whose light
  drifts gently. Agents build them; they need no fuel and light themselves at
  dusk. The street shows stone lamps at the junction and the side Road's end
  and hanging lanterns along the main Road, as one suggestion for where each
  goes.

## How it looks

- Light is warm yellow (orange for fire). It brightens the ground and pulls
  its hue toward the light instead of painting over it, so grass and dirt keep
  their texture.
- Every pool is stepped on the art's pixel grid and has a ragged edge that
  drifts slowly; fires flicker faster and breathe a little. Nothing moves
  more than an art pixel or two.
- Lights fade in with dusk and out at dawn, with the night wash; by day only
  the lantern fittings show.
