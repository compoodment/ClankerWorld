# Private memory summaries

Issue: [#1312](https://github.com/compoodment/ClankerWorld/issues/1312)

Pending Windows hands-on checks; automated tests do not establish summary usefulness.

- [ ] Run an agent long enough for ordinary records to enter its private archive, then use Jev and OpenAI Decisions. Confirm summaries help its later decisions while keeping uncertainty and who said something.
- [ ] Switch the helper Off. Confirm it continues using saved summaries, creates no new ones, and ordinary old records still archive.
- [ ] Save, reload and reconnect. Confirm the same summaries and original evidence remain private to their owner.
- [ ] Correct a summarized belief. Confirm the later recall treats the old source as corrected rather than treating the extract as a world fact.

The current extracts, three-day age, 25% importance cutoff and four-to-twelve-source batch are provisional for this playtest.
