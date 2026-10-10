# Autumn leaf review

The owner chose **leaves A**, a few fallen leaves under broadleaf and orchard
trees, in the October 9 visual polish review ([#1457](https://github.com/compoodment/ClankerWorld/pull/1457)).

The approved drawing has moved out of `Proposed/`: [AutumnLeavesReview.cs](AutumnLeavesReview.cs)
renders the completed comparison using the client's shared
[AutumnLeaves.cs](../../src/ClankerWorld.GodotClient/UI/Map/AutumnLeaves.cs).
Each tree seeds fourteen small leaves, scattered mostly around its trunk and
a little to the east, with the approved warm palette. The client puts them on
eligible ground beneath sprites, only in autumn, at close and mid zoom.

`dotnet run --project tools/ArtPreview -- proposed out weathermarks` still
includes `leaves-a-32` and `leaves-a-16`. Leaves B stays in the original
[visual polish review](Proposed/Polish.md) as an unchosen carpet comparison.
The other polish proposals keep their own review and implementation status.
