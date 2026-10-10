# Art preview

A developer tool for reviewing the Godot client's code-drawn art without
the engine. It compiles the real generators in
`src/ClankerWorld.GodotClient/UI/` against a small stand-in for Godot's
image API (`Shim/`) and writes PNGs. It is not part of the solution or CI.

## Commands

Run from this folder with the .NET 8 SDK or newer:

```bash
dotnet run -- baseline out   # every current texture, 1× PNGs and captioned 4× sheets
dotnet run -- proposed out   # every proposal and archived review drawing; add a family name, such as nightlights, for just that one
dotnet run -- scene out      # the reference Town and mountain-range scenes: current art, each proposal, and all proposals together
dotnet run -- seasons out    # the same reference scenes in spring, summer, autumn and winter at 16 and 32 px
dotnet run -- animate out weather  # proposals that move, such as weather, as numbered frames of each loop
dotnet run -- movement out   # implemented glide/bob reference frames from the client's walking cadence
dotnet run -- check          # current scene contract checks in memory; writes no pictures
python3 build_review.py --baseline out/baseline --proposed out/proposed --scene out/scene \
  --style ../../docs/development/art-style.md --notes Proposed --out out/art-review.html
```

A follow-up round passes `--round 2`, the previous round's proposals as
`--approved`, the previous decisions as `--decisions`, and scene pairs as
`--compare "label|before.png|after.png"`; pictures already approved are left
out.

`build_review.py` needs Python 3 with the `markdown` package.

## Files

- The style guide every proposal follows is
  [docs/development/art-style.md](../../docs/development/art-style.md).
- `Proposed/<Family>.cs`: one proposal per art family. Each class
  yields its pictures and can stand in for the current generator in the
  reference scene. `Proposed/<Family>.md` is the note for the owner.
- `Approved/Smoke.cs`: the original smoke review drawings, retained independently
  for exact comparisons. Baseline smoke and the `smoke-client` animation use
  the live client generator; `check` compares all 36 frames at both zoom levels.
- `Approved/StoredStock.cs`: independent original stock review drawings. Baseline
  stock and the `stock-client` family use the live client painter; `check` compares
  empty, some and full scenes at both atlas sizes.
- `Scene.cs`: the hand-laid reference Town and the composer that draws it
  the way the map layer does.

Proposals are mockups for computment's review. Approved art for content the
game already has moves into `src/ClankerWorld.GodotClient/UI/`; art for
content not built yet waits here until its feature lands.
