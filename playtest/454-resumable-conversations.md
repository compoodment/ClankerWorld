# Agent conversations

Pending for PR #609, which closes issue #454. No Windows playtest is claimed.

- In the Windows game, start a conversation between two nearby adults with different personal model assignments. Check that both must accept before the first reply and that each speaker uses their own assigned model. ([#454](https://github.com/compoodment/ClankerWorld/issues/454))
- Let a nearby third agent hear a public line, then inspect its conversation history and memory. Check that a distant agent does not receive that line and that neither agent's private thoughts appear in the history. ([#454](https://github.com/compoodment/ClankerWorld/issues/454))
- Pause or save and reload during a conversation. Check that it stays interrupted and makes no model calls until both participants choose to resume; then check that ordinary jobs do not interrupt a turn, while urgent needs can. ([#454](https://github.com/compoodment/ClankerWorld/issues/454))
- Let a conversation propose mutual trust. Check that both agents receive the same public wrap-up and proposed effect, and that disagreement leaves trust unchanged. ([#454](https://github.com/compoodment/ClankerWorld/issues/454))
- Expand a long conversation on a 1080p or 1440p screen, where the interface automatically uses 200% size. Check that the history scrolls within the screen and that reading it does not select an agent or reveal the history to another agent. Press Escape and check that only the reader closes, keeping the selected profile open. ([#454](https://github.com/compoodment/ClankerWorld/issues/454))
