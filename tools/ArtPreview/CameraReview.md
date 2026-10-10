# Camera review

The owner chose **camera B**, ease out without a settle or overshoot, in the
October 9 visual polish review ([#1457](https://github.com/compoodment/ClankerWorld/pull/1457)).

The completed [CameraReview.cs](CameraReview.cs) has moved out of `Proposed/`
and shares [CameraEasing.cs](../../src/ClankerWorld.GodotClient/UI/Map/CameraEasing.cs)
with the live client: cubic ease-out, 0.7-second Find travel and 0.35-second zoom
steps. A records the previous jump and C remains an unchosen comparison.

`dotnet run --project tools/ArtPreview -- animate out camera` renders the three
comparison loops. Only B is used in the map. The client additionally keeps
cursor anchors and quick-card positions stable, retargets from the shown view,
takes the short wrapped route and preserves immediate manual panning.
