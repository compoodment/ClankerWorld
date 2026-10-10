# Weather review

**Chosen on October 8 ([#1325](https://github.com/compoodment/ClankerWorld/issues/1325)):**
computment picked **B · Pixel streaks**, and does not want D's cloud shadows
mixed in. B is the approved look for the map's weather overlay; A, C and D stay
here only as the record of the review.

B now uses the client's shared [pixel-streak drawing](../../src/ClankerWorld.GodotClient/UI/Map/WeatherStreaks.cs).
Its approved pixels have moved out of `Proposed/` into the live overlay; this
completed review keeps the other options for comparison. The cloud haze and
soft storm flash retain their existing drawing, with no cloud shadows.

The map's weather overlay goes through the art review like every other
picture (agreed October 8, [#1254](https://github.com/compoodment/ClankerWorld/issues/1254)).
This completed review draws the previous overlay and three alternatives for rain, storm and
snow over the reference Town corner, at 32 px (close) and 16 px (mid zoom).
The weather covers everything except a clear north-west corner, so each option
also shows how a region ends. All four keep the same cloud haze (Clouds B) and
the same soft storm flash, so only the weather itself differs.

`dotnet run -- proposed out weather` draws a still of each;
`dotnet run -- animate out weather` writes each as a three-second loop of
numbered frames at 12 frames a second, which repeats without a jump. For this
short review loop, the shared cloud haze follows a small closed drift while
keeping the game's cloud pattern, sampling and opacity.

Every option follows style rule E3: weather stays an animated, sparse overlay
with no opaque shapes. None changes the weather rules, where weather falls or
how regions move.

## A · Previous overlay

What the game drew before B, retained from `WeatherLayer`: a faint tint, short
two-pixel drops that land as small flat rings, slanting storm streaks with the
odd splash, and two-pixel snowflakes swaying down.

## B · Pixel streaks

Crisp pixel weather. Rain is straight one-pixel streaks, lighter at the top,
landing as a three-pixel burst instead of a ring. Storm streaks step exactly two
pixels down for every one across, so they stay sharp, and come in gusts that
sweep east through the storm. Snowflakes are small crosses blown sideways by
the wind. The tints are a little cooler than the previous overlay.

## C · Ripples

A calmer map where little falls. Rain shows as small ripples that open and fade,
larger and more often on water, with brief wet glints on land. Storms add faint
wind lines blowing east over the ripples. Snow is a few slow flakes over a light
dusting that settles on land where the snow falls; the world's own snow cover
still comes from the ground art, so the dusting is only a sign that snow is
falling.

## D · Cloud shadows

Soft cloud shadows, in three steps of strength, drift east over the weather's
area, so you can tell from far out where it is raining. Rain falls only under
the shadows, as short drops with a one-pixel splash. Storms bring darker, faster
shadows with slanting streaks. Snow comes under a pale veil instead of a shadow.
The shadows are wide, uneven shapes, never the large circles rejected on
September 29.

## Answers from the review

1. Which look should the map use? **B.**
2. Should the chosen look mix in a part of another? **No:** computment does not
   like D's cloud shadows, and B is used on its own.
