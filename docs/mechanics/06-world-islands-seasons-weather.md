# The world: islands, seasons and weather

**Pillar:** A living world / Rewarding exploration
**Scripts:** `WorldZone.cs`, `WorldZoneStreamer.cs`, `ZoneLandMask.cs`, `ZoneTitleCard.cs`, `SeasonSystem.cs`, `SeasonSnowfall.cs`, `RainSystem.cs`, `LocalSnowWeather.cs`, `LocalSwampWeather.cs`, `LocalGroveAtmosphere.cs`, `GroundMud.cs`, `LocalRainPuddles.cs`, `TravelerWeathering.cs`

## Goal

One big world of islands joined by bridges, where every trip out is different because time, season and weather change what you see, hear and feel, without a loading screen between places.

## Player experience

- Setting foot on an island shows its name like a chapter title.
- Summer turns to autumn and then winter as the days pass; trees, grass and ground change colour; in winter the rain falls as snow and the player feels the cold.
- Rain soaks clothes and ground, leaves puddles and mud that climbs the legs of the player and the horse; footprints and hoof prints stay in the mud.
- Mountain snow, green swamps with marsh lights and hidden golden glades each have their own light, fog, particles and sound.

## Rules and numbers

| System | Rule |
|---|---|
| Day | 24 real minutes; sun, moon, 9 panoramic skies, ambient and fog follow the hour |
| Seasons | 7 game days each; only the phase is shown ("Mid-autumn", "Late autumn - winter draws near") |
| Winter | Rain visuals and rain sounds scaled by `1 − SnowAmount`; snowflakes per rain preset |
| Islands | Far islands sleep: no drawing, physics or scripts; quest, map and save objects stay awake |
| Closed islands | Reaching a locked island's land twice in a row fades the screen and returns you (and your horse) to the last open ground |
| Fog | Standard Unity fog only, thickened slightly in deep forests |

## Iterations

| Problem found while playing | Change |
|---|---|
| Opening the inventory restarted the snow | Snowfall is not tied to the map / journal pause any more and keeps its state |
| Season days were a number the player had to track | Only early / mid / late messages are shown; F9 / F10 debug keys jump seasons for testing |
| Islands popped in when approached | They wake long before they are visible, a little each frame, nearest first |
| Fast travel landed before the ground existed | The streamer wakes the target islands at once behind the black screen |

## What I would test next

- Winter difficulty: is the cold a pressure or a chore?
- Whether the season length (7 days) is long enough to notice and short enough to see all three.
