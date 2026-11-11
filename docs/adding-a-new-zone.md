# Adding a new zone (island)

The main quest already has five chapters (MainQuest.cs). Zones I and II exist. The next ones are waiting for their
islands, with these names (rename them in MainQuest.cs if you like, before you build the island):

- **III** The River Lands · stone: the Patient Onistone · opened by: the Hungry Onistone (zone II)
- **IV** The Old Battlefield · stone: the Untiring Onistone · opened by: the Patient Onistone
- **V** The Fox Woods · stone: the Faceless Onistone · opened by: the Untiring Onistone

Everything else (locks, the bridge veil, chapter progress, the Onistone record, objectives, fast travel, deliveries,
seasons, footsteps...) works by itself once these steps are done.

## 1. Terrain

Sculpt and paint the island. The Terrain, the water planes and the BRIDGE stay OUTSIDE the zone object.

## 2. The zone object

Make an empty object (e.g. "zone_03") and add Mossblood > World > World Zone.
- Zone Name: exactly the chapter's region, e.g. "The River Lands" (case and a leading "The" don't matter).
- Auto Lock: ON, Opened By: None. The island then stays closed until the previous chapter's stone is taken.
- Subtitle (optional): e.g. "Chapter III".
Put everything that belongs to the island INSIDE it, in the same folders as zone_02:
sanctuaries, leafpiles, cliffrocks, nohorsezone, spawners, constructions, lootchests, enemyspawners, relics, npcs.

## 3. The bridge

At the start of the bridge (on the open side), add an empty object with Mossblood > World > Onistone Gate.
Required Stone = the PREVIOUS chapter's stone (zone III: Fire / the Hungry Onistone). Turn it to face across the
way and set its Size. (Even without it the island turns you back; the gate is the visible spirit veil.)

## 4. The onistone chest (main quest - it can never fail)

One chest inside the zone (under lootchests) with oni_stone in its LootBag ITEMS list (always inside), not as a
random roll. Appear Chance = 1. It stays shut until the player has asked every person of the region (step 5).
Once taken, the record in AdventureProgress is permanent: using or losing the stone never closes anything again.

## 5. The people

Drop NPC prefabs into "npcs". Ask Claude to set them up like zone 2 (shops balanced to the zone, deliveries,
fast travel, idle / little walks) and to write their part of the story for the chapter's stone
(NpcLoreBook.cs: clue, pointer to the next person, warning, tips, and what they remember of the father).
Women's voices: add their names to Resources/Mossblood/Voices (Women Names).

## 6. Loot balance

Each island sits two tiers above the previous one (zone I: 01-05, zone II: 06-07, zone III: 08-09...).
Ask Claude to balance the chests, enemies and shops of the new zone.

## 7. Check

Mossblood > Check > Main Quest and Zones. It lists, chapter by chapter, the island, what opens it, the gate,
the people (and whether they are really in the world) and the Onistone chest. Fix every PROBLEM it reports.
