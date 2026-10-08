# Waiting-for-model marker

When an agent's model is slow to reply, the map needs to show that it is
waiting. The owner agreed on October 8 ([#1247](https://github.com/compoodment/ClankerWorld/issues/1247))
that a waiting agent shows a small marker on the map, and that the Event Log
adds a line only when the model actually fails. This proposal draws three
markers on an agent in the reference Town, at 32 px (close) and 16 px (mid
zoom), frame by frame, and once beside the conversation badge.

The conversation badge is already a cream speech bubble with three dots, at
the agent's top right. The waiting marker must not look like it, so none of
these options is a bubble with a tail or a row of dots.

## A · Thought cloud

A small cream cloud above and left of the head, joined to it by two puffs. A
dark dot moves across it, then it rests empty for a beat. It reads as
"thinking" and keeps the badge's colours.

## B · Hourglass chip

An hourglass on a round chip at the top left, the same size as the
conversation badge on the other side (a little larger at 16 px, so the glass
still reads). The sand runs down, then the glass turns over.

## C · Circling spark

A small gold spark circles just above the head with a short trail, dimmer as
it passes behind. There's no chip or bubble, so it covers the least of the
map.

## How it behaves (all options)

- It appears only after the agent has waited about two seconds, so a quick
  reply never makes it flash.
- It disappears as soon as the reply arrives, or when the world is paused.
- When the model fails (no reply in time, model unavailable, missing key,
  usage limit or an unusable reply), the Event Log gets one line naming the
  agent and what happened. It gets no line for a slow reply that arrives, and
  no repeat while the same problem continues.

## Recorded review

The owner selected **C · Circling spark** on October 8, recorded by Claude Code
session 62ab178e in [#1315](https://github.com/compoodment/ClankerWorld/issues/1315).
The client uses that drawing unchanged. The two-second delay remains
provisional for playtesting.
