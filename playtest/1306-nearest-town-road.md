# Roads between Towns

Pending Windows game-window check for [#1306](https://github.com/compoodment/ClankerWorld/issues/1306).

- Found a Town away from two existing Towns and complete its first building.
  Check that one Road reaches the nearer Town, follows clear ground, and looks
  like the existing packed-dirt streets. Founding on an already completed
  household House should link it immediately.
- Check a route that can reuse a street and cross a river one or two tiles
  wide. The Road should reuse the street and draw a normal bridge. Protected
  household land and wider water should keep a Town unconnected when no legal
  route exists.
- Save, return, and complete another building. The link should persist without
  another inter-Town link, and neither Town's land should stretch along it.

Automated native runtime, route, save/reload and replay checks cover these
rules. Appearance and movement in the Windows game have not been checked.
