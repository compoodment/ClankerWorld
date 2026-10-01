# Art preview

A developer tool for reviewing the Godot client's code-drawn art without
the engine. It compiles the real generators in
`src/ClankerWorld.GodotClient/UI/` against a small stand-in for Godot's
image API (`Shim/`) and writes PNGs. It is not part of the solution or CI.

## Commands

Run from this folder with the .NET 8 SDK or newer:

```bash
dotnet run -- baseline out   # every current texture, 1× PNGs and captioned 4× sheets
dotnet run -- proposed out   # every proposal under Proposed/
dotnet run -- scene out      # the reference Town scene: current art, each proposal, and all proposals together
python3 build_review.py --baseline out/baseline --proposed out/proposed --scene out/scene \
  --style STYLE.md --notes Proposed --questions QUESTIONS.md --out out/art-review.html
```

`build_review.py` needs Python 3 with the `markdown` package.

## Files

- `STYLE.md`: the draft style guide that every proposal follows.
- `ROUND1.md`: the reference set drawn in round 1.
- `Proposed/<Family>.cs`: one proposal per art family. Each class
  yields its pictures and can stand in for the current generator in the
  reference scene. `Proposed/<Family>.md` is the note for the owner.
- `Scene.cs`: the hand-laid reference Town and the composer that draws it
  the way the map layer does.

Proposals are mockups awaiting computment's review. Approved ones move into
`src/ClankerWorld.GodotClient/UI/Graphics/` in their own pull requests.
