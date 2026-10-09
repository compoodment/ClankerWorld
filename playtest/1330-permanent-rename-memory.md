# Permanent memories of player renames

For [#1330](https://github.com/compoodment/ClankerWorld/issues/1330).
Automated runtime, signed HTTP, replay and native UI checks are separate from
these pending Windows checks. Private schema 107 is provisional until merge.

- [ ] Rename an agent in Profile, then open Memories. A private Permanent card gives the correct world day and accepted name. Rename them again and check both dated memories remain. The agent's later personal-model request contains the rename; another agent's model and Memories receive no notification. Refused names and submitting the unchanged name add no memory.
- [ ] Continue until later experiences appear and Jev has scored memories. The rename card remains available despite its age. Save and load: both rename records keep their date and wording, and the model still receives the latest rename. An older alpha save is refused with its original files preserved; no silent migration or rename occurs.
- [ ] Change a married agent's surname. Both changed spouses receive their own private permanent memory, with their correct new full name. A refused name leaves both names and memory histories unchanged. Past conversation text stays as spoken, and the cards fit the small-window Memories panel.
