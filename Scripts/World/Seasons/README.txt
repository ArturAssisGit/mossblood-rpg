SEASONS (Mossblood, Nov 2026)

What it does
  Summer = the world exactly as authored (nothing changes).
  Autumn = leaves of every painted terrain tree (not the rocks) turn yellow / orange / a little red;
           Grass_01 and Grass_03 turn golden; the GroundGrass terrain layer follows Grass_01.
  Winter = leaves and grass go white / snowy; GroundGrass goes pale.
  Falling leaves and cherry petals follow the season too.
  In winter the rain falls as snow (SeasonSnowfall.cs): same weather timing, fog and wind, but no rain drops,
  splashes, puddles or rain sound; drizzle = light snow, rain = steady snow, storm = blizzard.

The year
  Summer -> Autumn -> Winter -> Summer..., "Days Per Season" game days each (7; field on the Season System object).
  The days are counted from the game clock (sleeping counts) and kept in the save (SaveData.seasonHours).
  A new game starts on the first day of summer. The player never sees days, only short messages:
  "Autumn has come", "Mid-autumn", "Late autumn - winter draws near", and the current one a few seconds after loading.

Try it
  F9 = jump to the next season, F10 = jump to the next part of the season (editor AND builds).
  Turn those keys off before release: SeasonSystem.DebugKeys = false.
  In the editor also: Play, then Mossblood > Seasons > Summer / Autumn / Winter.
  From code: SeasonSystem.Instance.SetSeason(Season.Autumn);

Safety
  The first time it runs it writes the original values into SeasonOriginals.json (this folder).
  Those values are put back automatically when play stops and when the editor opens after a crash
  (Summer = the original colours). Assets are never saved in another season's colours.

Remove it completely
  1. Stop Play (the original colours come back on their own).
  2. Delete the folders Assets/Scripts/World/Seasons and Assets/Scripts/Editor/Seasons.
  3. Remove the SeasonSystem / SeasonSnowfall lines in SaveData, WorldSaveGameManager (.Saving, .Loading, .MenuButtons),
     LocalSnowWeather.RainVisibility, RainSystem and SoundManager.Weather.
  (LeafMotionRules.SeasonLeaves in FallingLeaves.GroundCarpet.cs is an inert hook: it stays 0 without this system.)
