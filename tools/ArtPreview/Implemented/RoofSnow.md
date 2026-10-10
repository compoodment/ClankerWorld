# Completed roof snow review

The owner approved roof snow A2 in the October 9 second review
([#1457](https://github.com/compoodment/ClankerWorld/pull/1457)).
[RoofSnow.cs](RoofSnow.cs) preserves the original 32/16px drawing and uses
[RoofSnowSprites.cs](../../../src/ClankerWorld.GodotClient/UI/Graphics/RoofSnowSprites.cs)
for the live slope cover and tint rules. `dotnet run --project tools/ArtPreview -- roof-snow out`
writes the completed references. Earlier A/B drawings stay in `Proposed/` as comparisons.

The client applies these rules to each actual roof, using the ground-snow
region history. A north/south ridge splits east/west; hipped roofs have four
faces and the Silo uses its cone. Details, outlines, yards and grass shadows
stay clear. Cover builds, pauses, melts and resets with the same observed history.
