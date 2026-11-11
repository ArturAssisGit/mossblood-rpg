# AGENTS.md — Mossblood: project map for AI assistants

Read this first. It tells you **where things live** and **which rules to keep** before you change anything.
(The same file is loaded by Claude through `CLAUDE.md`. Other tools read `AGENTS.md` directly.)

---

## 1. The project in one paragraph

**Mossblood** is a solo, third-person **feudal-Japan action / survival RPG** (Witcher / Outward style) made in
**Unity 6.6 (6000.6.2f1), URP**, C# only. One big world scene with **islands = zones** that unlock one by one through the
main quest (five *Onistones*). Systems: katana / dual small swords / bow combat with precision kills, finishers and a sword
ultimate (F); survival (hunger, thirst, bleeding, temperature); camp (tent + fire + crafting table); horses; merchants with
rotating stock; loot chests; relics and village requests; weather, day/night, mud and wet surfaces; **co-op** (Unity Relay,
custom messages, host-authoritative enemies); save/load to JSON.

- Main scene: `Assets/Scenes/Scene_Final_Map.unity` (all gameplay). Menu: `Assets/Scenes/Scene_Menu_01.unity`.
- Code: `Assets/Scripts/` (~300 files). Index of every script: **section 8**.
- The developer (Artur) writes in **Portuguese (PT-PT)**: answer him in PT-PT. Code, comments and identifiers stay in English.

---

## 2. Hard rules (the owner asked for these — never break them)

1. **Do not delete assets** (prefabs, scene objects, models, animations). Work in code. If something must go, ask first.
2. **The Onistone chest of each zone is main-quest content: never change it** (its LootChest / LootBag). Loot tools must
   detect it (an `oni_stone` item / `UpgradeStone.Oni` inside) and skip it.
3. **Onistone progress is permanent**: taking a stone is recorded in `AdventureProgress` (saved, never removed). Gates, zone
   locks and chapters check that record, **never the inventory**. Do not change this.
4. **Fog**: standard Unity fog only (no custom fog systems).
5. **Weapon holders stay on the same bones as the katana** (hands / sheath / back). Do not move them to other bones.
6. Everything must be polished, bug-free and light on performance (large open world): no per-frame `Find*`, no per-frame
   allocations, pool particles/objects, cache components.
7. No package installs (the project uses what is already in `Packages/`).

---

### The story (keep it consistent)
The player is the son of Daigo, a samurai. A fire set by warlords hunting demon stones killed his mother and little
sister Hana; Daigo set an Onistone chip into his blade to keep a promise (no village burned for a stone again) and it
is eating his memories. His last letter (`OnistoneLore.FatherLetter`) opens a new game and heads the Diary. The son
follows his trail through the five chapters: every named NPC remembers the father (`NpcLoreBook` field `father`,
"Have you seen my father?" in Ask about the land, Diary > "Your father's trail"). Chapter V hints that the Faceless
Oni wears his face. Tone: quiet, sad, never melodramatic. New zones: see `Adding a New Zone.txt` (project root) and
run Mossblood > Check > Main Quest and Zones (`Editor/Debug/WorldProgressionCheck.cs`).

## 3. How the code is organised (conventions)

- **Partial classes split by topic.** Big classes are one class in many files: `PlayerCombat.*.cs`, `EnemyAI_01.*.cs`,
  `HorseMount.*.cs`, `MapSystem.*.cs`, `NpcDialogue.*.cs`, `WorldSaveGameManager.*.cs`, `SoundManager.*.cs`,
  `PlayerEquipmentManager.*.cs`, `PlayerStats.*.cs`, `CraftingWindow.*.cs`, `CampSystem.*.cs`. Find the file named after the topic
  (e.g. bow aiming → `PlayerCombat.BowCrosshair.cs`). Keep new code in the matching partial file.
- **Rules / numbers classes**: tuning constants and pure logic live in static "rules" classes, e.g. `PlayerRules.cs`
  (`PrecisionCombatRules`, `CombatFeelRules`…), `EnemyRules.cs` (`EnemyCombatPace`, `ArcherShotRules`…), `*Rules` inside
  other files. Change balance numbers there first.
- **Auto-created systems.** Many managers create themselves at game start (comment says "Creates itself" / "Auto-created"):
  windows (Shop, Stable, Loot, Crafting/Camp, Pause, Settings, SaveLoad), NpcDialogue, MultiplayerSession, ScreenFade,
  CenterMessage, MapSystem, SleepSystem, RespawnSystem, PlacementSystem, GatherSystem, HorseSpawner, WorldZoneStreamer…
  Do not add them to the scene by hand.
- **UI is built from code** (UIKit / UIStyle / TornPaper / UIPolish), not from prefabs. Style: dark washi paper, brown + gold.
- **`PlayerRef`** is the one way to get the local player (`PlayerRef.Transform`, `.Stats`, `.Animator`, `PlayerRef.Find(ref t)`).
- **Enter Play Mode runs WITHOUT domain reload and WITHOUT scene reload.** Static fields survive between Play sessions:
  every static mutable state needs a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` reset.
  Static caches of Unity objects must be checked with `== null` (destroyed objects).
- **Enemy hierarchy**: every enemy (bandit, archer, strong, boss) is `EnemyAI_01` with different Inspector values/prefabs.
- **Item data** = `PlayerItemData` ScriptableObjects in `Assets/Art/Items/...`. `ItemDatabase` (Resources) is rebuilt
  automatically by `Editor/Items/ItemDatabaseBuilder.cs`. Saves store items by id (asset name) + upgrade string.
- **Loot "group" field**: entries with the same group name are ONE roll; their chances are shares of it (sum = chance that
  anything drops). Empty group = rolled alone. Used by enemy loot tables, chests and shop rotation.
- **Animator states are used BY NAME in code** (`CrossFade`, `IsName`, `HasState`, net sync uses full path hashes). Do not
  rename states or move them into sub-state machines without updating every reference.
- **Co-op**: the host simulates enemies; clients mirror them (`EnemyAI_01.NetworkMirror.cs`). Time is shared online, so
  slow-motion / pause is disabled while a session is online. Any new gameplay event that others must see goes through
  `MultiplayerSync` (custom messages, no NetworkObjects).
- **Editor one-shot setup scripts** (`Assets/Scripts/Editor/...`): pattern `[InitializeOnLoadMethod]` + `EditorApplication.delayCall`
  + an `EditorPrefs` "ran" key; they write a report to `Logs/<Name>.txt` and **must delete themselves when done**
  (owner's rule: no one-time tools left in the Mossblood > Setup menu; it piles up). Do not add a menu item to a
  one-time script. The only Setup tool kept on purpose is Build Katana Scabbards.
  Unity only compiles when its window is focused; compile errors appear in `Logs/Editor.log` as `error CS`.

---

## 4. Where to change what (task index)

| I want to change… | Look here |
|---|---|
| Player sword combos, swing speed, damage, stamina cost, kick chance | `Characters/Player/Combat/PlayerCombat.cs` (+ `.Flow`, `.Footwork`), numbers in `PlayerRules.cs` |
| Hold-to-charge attacks, charged follow-through | `PlayerCombat.Charge.cs`, `PlayerCombat.ChargedRecovery.cs` |
| One-hit **precision kill** window / slow-mo (held strike released inside the enemy swing) | Player: `PlayerCombat.Precision.cs`; enemy window: `Enemies/EnemyAI_01.Precision.cs`; window % (`CounterOpensAt`), reach, timing: `PlayerRules.cs` → `PrecisionCombatRules` |
| Perfect guard (Q) / riposte | `PlayerCombat.Precision.cs`, `PlayerRules.cs` |
| Dual wield (small swords) animations & speed | `Combat/DualWieldAnimationSet.cs` + asset `Assets/Resources/Mossblood/DualWieldAnimations.asset` |
| Bow, arrows, quiver (30 arrows, refill), crosshair | `PlayerCombat.Hurt.cs` (shooting), `PlayerCombat.Quiver.cs`, `PlayerCombat.BowCrosshair.cs`, `Combat/Arrow.cs` |
| Dodge | `Combat/PlayerDodge.cs` |
| Finisher (kill animation, enemy placement, camera) | `Combat/PlayerFinisher.cs` (`enemyRightOffset`…), enemy side `EnemyAI_01.Finisher.cs`, camera `Camera/CameraController.cs` (`finisherShot*`) |
| Sword ultimate (F), its HUD orb | `PlayerCombat.SwordAbility*.cs`, data `Inventory/Data/SwordAbility.cs`, editor `Editor/Items/SwordAbilityEditor.cs` |
| Quick wheel (hold Tab), auto-draw when hunted | `UI/HUD/QuickWheel.cs`, `PlayerCombat.QuickDraw.cs` |
| Player getting hit, death | `PlayerCombat.Hurt.cs`, `Characters/Player/PlayerStats.cs` |
| Health, stamina, hunger, thirst, bleeding, temperature | `PlayerStats.cs`, `.SurvivalStatus`, `.Temperature`, `.LowHealthBorder`; HUD `UI/HUD/PlayerVitalHUD.cs` |
| Walking, running, jumping, slopes | `Movement/PlayerMovement.cs`, `.TerrainWalk.cs`; procedural IK `Animation/PlayerProceduralAnimation*.cs` |
| Camera | `Camera/CameraController.cs` (+ `.Woodland`) |
| Enemy AI decisions, attacks, archers (aim hold, cooldown) | `Enemies/EnemyAI_01.cs` (fields), `.Tactics.cs`, `.Archer.cs` (attack pace, first-attack slowdown, archer hold); pacing rules `EnemyRules.cs` |
| Enemy hit reactions, death pose, boss dodge, boss intro | `EnemyAI_01.HitReactions.cs`, `.Finisher.cs`, `.BossDodge.cs`, `.Encounter.cs`, `BossEncounterDirector*.cs` |
| Enemy spawns / camps / patrols | `World/Spawning/EnemySpawner.cs`, `RoamingPatrolSpawner.cs`, `SpawnPlacement.cs` |
| Enemy & boss loot | `lootTable` on the enemy prefabs (`Assets/Data/Prefabs/Enemy/...`, `Boss/...`), roll code `Gameplay/Loot/LootBag.cs` |
| Chests | `Gameplay/Loot/LootChest.cs` + `LootBag.cs`. Chest prefab `Data/Prefabs/Lootbag/lootchest_01` is only the model: LootBag + LootChest are added per scene instance |
| Merchants, shop stock, rotating "new packages" (hourly, real time) | `Characters/NPCs/ShopNpc.cs`, `ShopNpc.Rotation.cs`, `NpcServices.Transactions.cs`, window `UI/Gameplay/ShopWindow.cs` |
| NPC conversation, lore, fast travel | `Characters/NPCs/NpcDialogue*.cs`, `NpcLoreBook.cs`, `NpcTalk.cs` |
| Main quest, Onistones, chapter progress | `Gameplay/Activities/MainQuest.cs`, `OnistoneLore.cs`, `OnistonePickup.cs`, `AdventureProgress.cs`; journal page `UI/Journal/MapSystem.Objectives.cs` |
| Zones / islands, streaming, locked islands, island shape | `World/Streaming/WorldZone.cs`, `WorldZoneStreamer.cs`, `ZoneLandMask.cs`, `ZoneTitleCard.cs` |
| Bridge gate to the next island (Onistone Gate) | `World/Streaming/ChapterGate.cs` (Add Component > Mossblood > World > Onistone Gate) |
| Relics, village requests (hunt / supplies / pilgrimage / recovery) | `Gameplay/Activities/WorldActivitySystem.cs`, `WorldRelic.cs`, `VillageRequestDefinition.cs`, `UI/Gameplay/VillageRequestWindow.cs` |
| Camp (tent + fire + table), sleep, respawn | `Gameplay/Camp/CampSystem*.cs`, `CampSite.cs`, `Gameplay/Rest/*.cs`, window `UI/Gameplay/CraftingWindow*.cs` |
| Crafting, cooking, upgrade stones | `Gameplay/Crafting/*.cs`, `Inventory/Data/ItemUpgrades.cs`, `UI/Gameplay/CraftingWindow.Craft.cs` / `.Cook.cs` |
| Inventory, items, equipment slots | `Inventory/PlayerInventoryManager.cs`, `Inventory/Data/PlayerItemData.cs`, `UI/Inventory/*.cs`, `UI/Journal/MapSystem.Inventory.cs` |
| Wearing gear, weapon holders, katana scabbard (saya) | `Characters/Player/Equipment/PlayerEquipmentManager*.cs`, `KatanaScabbard.cs`, builder `Editor/Items/KatanaScabbardBuilder.cs` |
| Horses (riding, calling with H, stables) | `Gameplay/Horses/HorseMount*.cs`, `HorseSpawner.cs`, `Characters/NPCs/HorseStable.cs`, `UI/Gameplay/StableWindow.cs` |
| Gathering, leaf piles | `Gameplay/Gathering/*.cs` |
| Save / load (what is saved, migrations) | `Saving/SaveData.cs` (fields), `WorldSaveGameManager.Saving.cs` / `.Loading.cs` |
| Map, journal, minimap, waypoint | `UI/Journal/MapSystem*.cs` |
| Menus, pause, settings | `UI/Menus/*.cs`, `Core/GameSettings.cs` |
| Sounds | `Audio/SoundManager*.cs` (`Sfx` enum) + the SoundManager prefab |
| Weather, rain, snow / swamp / grove zones, fog | `World/Environment/Weather/*.cs` |
| Seasons (summer / autumn / winter colours of painted trees, Grass_01/03, GroundGrass) | `World/Seasons/SeasonSystem.cs` (+ README, original colours in `SeasonOriginals.json`), editor guard + debug menu `Editor/Seasons/SeasonGuard.cs` (Mossblood > Seasons). Seasons pass with the game days (7 each, saved in `SaveData.seasonHours`; player only sees early / mid / late messages); F9 / F10 debug keys (`SeasonSystem.DebugKeys`); in winter rain falls as snow: `World/Seasons/SeasonSnowfall.cs` (+ `LocalSnowWeather.RainVisibility`) |
| Day/night, sky, sun, colour grade | `World/Environment/Lighting/*.cs` |
| Water | `World/Environment/Water/*.cs`, land checks `World/Navigation/DryLand.cs` |
| Mud, wet clothes, falling leaves, ambience | `World/Environment/Effects/*.cs`, `World/Environment/Atmosphere/*.cs` |
| Blood and hit VFX | `VFX/BloodEffects*.cs`, `VFX/BloodiedBlade.cs` |
| Co-op | `Multiplayer/Core/MultiplayerSession.cs`, `MultiplayerSync*.cs`, `Multiplayer/Presentation/*.cs` |
| Performance (culling, forests, cleanup of spawned objects) | `Rendering/*.cs`, `World/Rendering/WorldDistanceVisibility.cs`, `World/WorldCleanup.cs` |

---

## 5. Important assets (outside Scripts)

- **Animator controllers**: `Assets/Animations/PlayerController.controller`, `EnemyController.controller`,
  `BossController.controller`, `HorseController.controller`, `EnemyArcherController.overrideController`.
  States are laid out in columns by category (movement / katana / charged / sword arts / bow / reactions / world).
- **Prefabs**: `Assets/Data/Prefabs/` (renamed from "Prefebs" in Nov 2026): `Player/Player_Prefeb.prefab`, `Enemy/enemy_01/…` (`enemy_01`,
  `enemy_archer_01`, `enemy_strong_01`, `_02` variants), `Boss/boss_01`, `boss_02`, `Lootbag/lootchest_01`, `NPCS/`, `Horses/`,
  `Construction/`, `World Managers/`.
- **Items**: `Assets/Art/Items/` — `Weapons/Swords/sword_01..08`, `Weapons/Small Swords/small_sword_01..08`, `Weapons/Bows/bow_01..03`,
  `Armour/Chest/armour_02..10`, `Armour/Hat/hat_01..09`, `Armour/Capes/cape_01..05`, `Lanterns/lantern_01..04`,
  `Backpacks/backpack_01..06`, `Consumables/`, `RecipeScrolls/`, `Stones/` (unmei, kake, oni), `Utilities/` (coins…), `Place/camp`.
  Scabbard meshes/materials: `Assets/Art/Items/Scabbards/`.
- **Asset Store packs**: `Assets/ThirdParty/` (Adventure Nature Vol.2 / Vol.4, LPHorse, Raise Creation samurai animations, Footsteps / Nature Essentials sounds, WorldMaterialsFree, Yughues pavements). Terrain data / layers / brushes: `Assets/Art/Terrain/`. Input actions: `Assets/Settings/Input/`.
- **Resources** (loaded by code): `Assets/Resources/ItemDatabase.asset`, `Resources/Mossblood/` (DualWieldAnimations, Atmosphere,
  Relics, Requests, UI, Water), `Resources/SwordAbilities`, `Resources/CampKit`, shaders.
- **Scene layout** (`Scene_Final_Map`): one root per island (`zone_01`, `zone_02`, … each with a `WorldZone`), with children
  like `lootchests`, `enemyspawners`, `sanctuaries`, `constructions`, `leafpiles`. Bridges in `bridges`, gates in `gates`.

---

## 6. Balance references

- Zone 2 NPCs (npc_06 Tetsu smith, npc_07 Shiori tea house, npc_08 Hayato outfitter; Fire-stone story in NpcLoreBook): set up once (report Logs/Zone2Npcs.txt); the setup tool was removed. Footsteps, UI sounds and voices live in `Resources/Mossblood/Footsteps|UISounds|Voices.asset` (edit there; their setup tools were removed too).
- `Zone 1 Balance.txt` (project root): how zone 1 loot is balanced (enemies, bosses, chests, shops) and the recipe for the
  next zones. Zone 2 covers only two tiers above zone 1 (06–07; cape/bow/lantern 03, backpack 05).
- Loot setup tools for zones 1 and 2 already ran and were removed (chests, relics are set up in the scene).

---

## 7. Checklist before you finish a change

1. It compiles (no `error CS` in `Editor.log`) and no new warnings spam the Console.
2. Statics reset on SubsystemRegistration; no per-frame allocations / searches; events unsubscribed in `OnDisable/OnDestroy`.
3. Co-op still works (host vs client paths) and saves still load (old saves: new fields need safe defaults).
4. Nothing deleted, the Onistone chests untouched, the holders on the same bones.
5. Tell Artur (in PT-PT) what changed and how to test it.

---

## 8. Every script, by folder

### Animals/AI/
- `BirdAI.cs` — A bird: it walks around on the ground (Idle <-> Walk). When the player gets close, it takes off (Flap), climbs and then alternates Flap and Glide while it flies away. When it is far from the player it is…
- `FoxAI.cs` — A fox: it wanders (Idle <-> Walk). When the player gets close it runs away (Run). When it is killed it plays Death and stays on the ground for a while. Put it on the fox prefab (the root) and drag the 4…

### Animals/Animation/
- `ClipBlender.cs` — Plays a few animation clips on an Animator WITHOUT an Animator Controller, blending from one to the next. Used by the animals (BirdAI, FoxAI). Looping clips repeat by themselves, the others stop on their…

### Animals/Spawning/
- `AnimalSpawner.cs` — Spawns animals (birds, foxes...) on the ground around the player and removes them when he leaves. Put it on any object in the world scene (e.g. World Managers), fill "Animals" with the PREFABS from the…
- `DistantSkyBirds.cs` — Cosmetic distant flyovers. The ground birds keep their original AI and spawn budget.

### Audio/
- `SoundManager.Effects.cs` — SoundManager: Effects (called by the game scripts)
- `SoundManager.Music.cs` — SoundManager: Music
- `SoundManager.Weather.cs` — SoundManager: Weather
- `SoundManager.cs` — Every sound in the game is assigned here (empty boxes = silent). Put the prefab in BOTH scenes: it survives scene loads, the extra copy removes itself. List boxes hold several versions of a sound: one is…

### Characters/
- `CharacterManager.cs` — Empty base class (legacy placeholder).

### Characters/Enemies/
- `BossEncounterDirector.Restart.cs` — Boss encounter: restarting the fight after the player dies (retry ticket).
- `BossEncounterDirector.cs` — Presentation and local retry only. Enemy AI remains authoritative on the host.
- `EnemyAI_01.Archer.cs` — EnemyAI_01: Archer
- `EnemyAI_01.BossDodge.cs` — Bosses only: sidestep/back-dodge out of the player's attacks (dodge i-frames, cooldowns).
- `EnemyAI_01.Encounter.cs` — Boss intro / encounter presentation hold (camera + boss bar start).
- `EnemyAI_01.Finisher.cs` — EnemyAI_01: Finisher
- `EnemyAI_01.HitReactions.cs` — Enemy getting hit: flinch/stagger, knockback, hit flash, blood.
- `EnemyAI_01.MeleeReach.cs` — How far an enemy's swing really reaches (hit-zone geometry, PlayerInHitZone).
- `EnemyAI_01.Network.cs` — EnemyAI_01: Multiplayer (see MultiplayerSync)
- `EnemyAI_01.NetworkMirror.cs` — EnemyAI_01: Multiplayer
- `EnemyAI_01.Patrol.cs` — EnemyAI_01: Patrol (RoamingPatrolSpawner): walks in a group to a point; fights when it sees a player, then goes on
- `EnemyAI_01.Precision.cs` — Enemy side of the precision counter window (OpenPrecisionWindow, PrecisionWindowFor, PrecisionReach) and riposte after perfect guard.
- `EnemyAI_01.Tactics.cs` — Combat decisions: when to attack, strafe, back off; CancelCombatAction; committed strikes.
- `EnemyAI_01.cs` — Main enemy brain + all Inspector fields (health, damage, ranges, loot table, archer settings). Every enemy type (bandit, archer, strong, boss) is this one class.
- `EnemyHitReactionDriver.cs` — Only active impacts are ticked. Disabled completely when no enemy is reacting.
- `EnemyRules.cs` — Accuracy is bounded by the actual body, rather than an angle wider than the whole player.

### Characters/NPCs/
- `DeliveryNpc.cs` — Put this on an NPC (a new one, or a merchant that already has ShopNpc). "Gives packages": talk to it and it offers a delivery to another DeliveryNpc, for money. "Receives packages": you can deliver packages…
- `HorseStable.cs` — One horse for sale at a stable
- `NpcDialogue.Conversation.cs` — NpcDialogue: Conversation
- `NpcDialogue.FastTravel.cs` — NpcDialogue: Fast travel
- `NpcDialogue.Lore.cs` — NpcDialogue: "Ask about the land": the Onistone story (each named NPC knows one piece and points to the next), advice for a traveller, news from the roads and the NPC's own background.
- `NpcDialogue.Npcs.cs` — NpcDialogue: NPCs
- `NpcDialogue.Tick.cs` — NpcDialogue: Update
- `NpcDialogue.UI.cs` — NpcDialogue: UI
- `NpcDialogue.cs` — Creates itself at game start. NPC conversation system: near any NPC (merchant, stable keeper, courier, or one with NpcTalk) there is one line: "[E] Talk to <name>". E: the camera moves in over your…
- `NpcIdle.cs` — Shared by every NPC (merchant, stable keeper, courier, talker): idle clip without an Animator Controller (PlayableGraph), feet kept on the ground, turns to face the player while talking, and a little life: a few steps (≤2.6 m) around its place and back, picking something up, sitting on the ground a while (clips + chances in NpcMotionSet), never while the player is within 4.5 m or talking to it, checked for walls / drops / steep ground, nothing beyond 55 m from the camera. Walk clip in `Resources/Mossblood/NpcMotion.asset` (`NpcMotionSet.cs`); `NpcStayPut` keeps one still. Identity keys (fast travel, deliveries, requests) use `NpcIdle.HomeOf`, never the current position.
- `NpcLoreBook.cs` — What each named NPC knows. Matched by name (NpcTalk / merchant / courier / stable name), ignoring case. Every NPC holds one piece of the Onistone story and points the traveller to the next person to ask.…
- `NpcServices.Transactions.cs` — Buying/selling and service prices (shops, stables, couriers).
- `NpcTalk.cs` — Optional, on any NPC: a merchant (ShopNpc), a stable keeper (HorseStable), a courier (DeliveryNpc) or an NPC with nothing else. Sets its name for "Talk to ...", what it says when you start talking, and…
- `ShopNpc.Rotation.cs` — Rotating stock: on top of its normal stock, the merchant gets a "package" of extra items, one of each group, picked at random. A new package arrives every Refresh Minutes of REAL time (the clock of the…
- `ShopNpc.cs` — Put this on an NPC prefab: it stands still playing its idle animation, and E opens the shop.

### Characters/Player/
- `PlayerRef.cs` — The local player, found in one place for every script (instead of each script searching for the "Player" tag). A player that was turned off (e.g. the copy from the menu scene) doesn't count.
- `PlayerRules.cs` — Presentation and decision pacing only. Damage, stamina, dodges and precision windows retain their own rules.
- `PlayerStats.LowHealthBorder.cs` — Red screen border at low health.
- `PlayerStats.SurvivalStatus.cs` — Three actual hits inside the rolling window, with health strictly below the threshold. No time-based recovery: a bandage, completed sleep or respawn explicitly clears the state.
- `PlayerStats.Temperature.cs` — Body temperature (cold in snow zones, warmth from fire/clothes).
- `PlayerStats.cs` — Shared combat ownership. Death/loot events do not add special charge.

### Characters/Player/Animation/
- `PlayerProceduralAnimation.BodyLean.cs` — PlayerProceduralAnimation: Lean, hit reaction, backpack sway (after the animation)
- `PlayerProceduralAnimation.FootIK.cs` — PlayerProceduralAnimation: Foot IK + look-at (called by the Animator)
- `PlayerProceduralAnimation.Life.cs` — PlayerProceduralAnimation: Life: what the body should be doing right now
- `PlayerProceduralAnimation.SlopeWalk.cs` — Body/feet adjustment when walking up or down slopes.
- `PlayerProceduralAnimation.SteepContacts.cs` — Foot contacts on steep ground.
- `PlayerProceduralAnimation.cs` — Procedural touches on top of the animations: foot IK, lean into turns, head look-at, backpack sway, hit jolt, hand on walls, and a "life" layer that reacts to what is happening: breathing (faster and deeper…
- `SitAnimation.cs` — Sit down / stand up, driven by the player's Animator (creates itself). Press the Sit Key (B): sits down, stays sitting still. Move (W/A/S/D), Space or press B again: stands up. This script sets the Trigger…

### Characters/Player/Camera/
- `CameraController.Woodland.cs` — Camera collision with trees in forests.
- `CameraController.cs` — Third-person camera: follow, mouse look, lock-on, zoom, collisions, finisher/cinematic shots (finisherShot* fields), shake.

### Characters/Player/Combat/
- `Arrow.cs` — The flying arrow (player and enemy archers): flight, hit, stick in target.
- `DualWieldAnimationSet.cs` — The small swords (key 3) fight with the same Animator states as the katana: combo A/B/C, hold-to-charge, charged follow-through, strong attack, block and the armed walk/run/strafe. While they are in use,…
- `PlayerCombat.BowCrosshair.cs` — PlayerCombat: Bow crosshair
- `PlayerCombat.Charge.cs` — Consume an existing reward before granting a fresh one: the charging strike does not consume its own reward.
- `PlayerCombat.ChargedRecovery.cs` — Follow-through after a charged strike (pose freeze / quick next swing).
- `PlayerCombat.Flow.cs` — A single replaceable slot, not an accumulating action queue. All times use gameplay time.
- `PlayerCombat.Footwork.cs` — Small steps toward the target while swinging.
- `PlayerCombat.Hurt.cs` — PlayerCombat: Getting hit
- `PlayerCombat.InstantDraw.cs` — PlayerCombat: Instant draw (no draw / sheath animation)
- `PlayerCombat.Precision.cs` — PLAYER side of the one-hit 'precision' kill: release a HELD (charged) strike inside an enemy's window = instant kill + slow-mo (TryCounterOnRelease); riposte after perfect guard (Q); slow-mo.
- `PlayerCombat.QuickDraw.cs` — PlayerCombat: weapons chosen from the quick wheel, and drawing your weapon by itself when enemies start hunting you. Both go through the very same path as the keys 1 / 2 / 3 (a "virtual key press"), so…
- `PlayerCombat.Quiver.cs` — PlayerCombat: the quiver. The bow holds a limited number of arrows; a new one comes back every few seconds. Counted only when needed (no per-frame work): the arrows earned since the last look are added…
- `PlayerCombat.SwordAbility.cs` — The F sword ability (ult): charge, activation, invulnerability while it plays.
- `PlayerCombat.SwordAbilityAssist.cs` — Aim assist / target pick for the sword ability.
- `PlayerCombat.SwordAbilityHUD.cs` — The F ability charge orb. Katana: one orb. Both small swords in hand: the orb splits into two (left blade on the left, right blade on the right). The active one (X switches) is full size and fills with…
- `PlayerCombat.cs` — PlayerCombat: Hit impact: freeze frame, camera shake, sparks
- `PlayerDodge.cs` — Dodge in 4 directions (added automatically by PlayerCombat): dodge key + W/A/S/D, no direction = backwards. Cancels attacks and gives short i-frames. Animator states (no transitions needed): Combat Dodge…
- `PlayerFinisher.cs` — Finisher: small chance on a sword kill; both play finisher anims with slow motion, player invulnerable until it ends. Needs Base Layer states "Finisher" (player) and optionally "Finisher React" (enemy); no…

### Characters/Player/Equipment/
- `CapePhysics.cs` — Add to a Plane (or empty object), then parent it to the character's upper spine. The object's origin is the middle of the cape's top edge. It hangs along local -Y, away from the back along local -Z.…
- `EquipmentRigBinding.cs` — The original body skin is the source of truth. Never search a scene root containing other rigs or gear copies.
- `KatanaScabbard.cs` — The katana's scabbard (saya): a mesh made in the editor around each katana's blade (Mossblood > Setup > Build Katana Scabbards). It hangs on the SheathHolder in exactly the pose of the sheathed katana, so…
- `PlayerEquipmentManager.Backpack.cs` — PlayerEquipmentManager: Backpack drop / pick up
- `PlayerEquipmentManager.Equipping.cs` — PlayerEquipmentManager: Attack / Defense numbers (small, in the armor slot)
- `PlayerEquipmentManager.Lantern.cs` — PlayerEquipmentManager: Lantern
- `PlayerEquipmentManager.LocalBinding.cs` — Binding equipped models (armour, hats, capes) to the player's skeleton.
- `PlayerEquipmentManager.Preview.cs` — Equipment preview (inventory character view).
- `PlayerEquipmentManager.cs` — What the player wears/holds: slots, holders (hands, sheath, back), weapon placement (equip/sheath offsets from PlayerItemData).

### Characters/Player/Movement/
- `PlayerLegMud.cs` — Active only while dirty. Reuses the source skinned mesh; never bakes or updates vertices each frame.
- `PlayerMovement.TerrainWalk.cs` — Walking on terrain/steep slopes (slope limit, sliding).
- `PlayerMovement.cs` — Input policy kept separate so forward diagonals, cancellation and toggle precedence can be checked.
- `PlayerMuddyFootsteps.cs` — Driven by PlayerMovement's existing footstep cadence. Particle lifetimes handle fading even while mounted/dead.

### Core/
- `GameSettings.cs` — The player's options from the Settings window (Controls / Gameplay / Video / Audio). Saved on this computer (PlayerPrefs), not in the save files, so they are the same for every save. Read by the camera, the…
- `WorldInteraction.cs` — Shared context checks for the existing world services. No polling manager or new Update loop.
- `WorldUtil.cs` — Small world helpers used by many scripts (one place instead of a copy in every file): flat distances, terrain height and slope, hour ranges, allowed areas / safe places and weighted random picks.

### Editor/Activities/
- `WorldActivityTools.cs` — Editor menu items for relics/activities.

### Editor/Combat/
- `DualIdleImport.cs` — Drop an idle (any file with "Idle" in its name) into Dual Sword Combat Animations and it becomes the small sword idle: imported as Humanoid with the root baked and looping like the other clips, swapped in…

### Editor/Debug/
- `MapRevealMenu.cs` — Menu Mossblood > Debug > Reveal Whole Map: a tick to see the map as if every place had been explored. Remembered on this computer (also between Play sessions); never in a build, never written into a save.

### Editor/Items/
- `ItemDatabaseBuilder.cs` — Keeps Assets/Resources/ItemDatabase.asset in sync with every PlayerItemData; runs on script reload and on Play.
- `KatanaScabbardBuilder.cs` — Makes a scabbard (saya) for every katana in Assets/Art/Items/Weapons/Swords, shaped around that katana's own blade: it follows the blade's curve and taper, starts right after the guard (tsuba) and closes…
- `RecipeAssetSetup.cs` — Stable GUID identities survive renames; copied recipe assets get their own identity.
- `SwordAbilityEditor.cs` — Dedicated states keep ordinary swings untouched and also work with existing animation sync.

### Editor/Performance/
- `MechanicsPolishChecks.cs` — Editor checks of mechanics setup.
- `PerformanceTools.cs` — Performance report (Tools > Performance > Report): prints the heaviest parts of the open scene (terrain trees and grass, heaviest objects, shadow settings) to the Console. It changes nothing.
- `ShadowBakeAssistant.cs` — Editor-only. Opening/auditing never modifies the scene or launches a bake.

### Editor/Rendering/
- `EnsureRuntimeShaders.cs` — Makes sure the shaders that code looks up by name (Shader.Find) are part of every build. Without this they are left out and the things that use them turn pink or become squares in the build.
- `WeatherZoneEditors.cs` — Scene-view handles for Snow/Swamp/Grove zones.

### Editor/Terrain/
- `TerrainToolsAgeCheckFix.cs` — Silences the "Checking lifetime of RenderTextures but m_AgeCheckAdded = false" error spam from the Terrain Tools package. It happens because the package keeps a static flag that gets out of sync when Domain…

### Gameplay/Activities/
- `ActivityRewardRules.cs` — Economy arithmetic is shared by offers and UI; no scene, inventory or random state is hidden here.
- `AdventureProgress.cs` — Save mutations are independent of Unity scene objects and inventory. Snapshots own their data.
- `MainQuest.cs` — The main quest: five chapters, one region and one great Onistone each. Everything here is automatic: - Chapter 1 is open from the start. Each next chapter unlocks when the previous chapter's Onistone is…
- `OnistoneLore.cs` — The five Onistones: the main thread of the world's story. Each is a blood-red demon stone holding one great Oni, named after the Oni inside (never after where it lies: that is for the player to find).…
- `OnistonePickup.cs` — A great Onistone lying in the world (main quest). Hold G next to it to take it; that completes its chapter in Journal > Objectives and opens the next one. Place one with GameObject > Mossblood > Onistone.…
- `RelicDefinition.cs` — Where a relic is placed automatically when no WorldRelic in the scene uses it
- `RelicSites.cs` — Relics are placed BY HAND: put a WorldRelic on an object in the scene (GameObject > Mossblood > Lost Relic, or Add Component > WorldRelic on your own model). Nothing is placed automatically any more. This…
- `VillageQuestGiver.cs` — Optional: add to a ShopNpc, DeliveryNpc or NpcTalk to customize its requests.
- `VillageRequestDefinition.cs` — Hunt: defeat enemies near the giver. Supplies: bring items. Pilgrimage: pray at different shrines. Recovery: find lost relics anywhere in the world.
- `WorldActivitySystem.cs` — One manager for all relic interactions. Hunts react to death events instead of polling enemies.
- `WorldRelic.cs` — A lost relic placed in the world (pick-up, village 'Recovery' requests).

### Gameplay/Camp/
- `CampKit.cs` — The camp: one item ("Camp") that pitches a tent, a campfire and a crafting table together. This asset lives in Resources/CampKit and says which item is the camp, which models are used and how they are laid…
- `CampSite.cs` — A pitched camp (made by CampSystem): the tent, the campfire and the crafting table under one object. Walk up to any of them and press E: one Camp window with Sleep, Craft and Campfire.
- `CampStartPoint.cs` — Where a new journey's camp stands. Put this on an empty object where the campfire should be, and turn it so its blue arrow (forward) points from where you wake up towards the fire: the tent goes behind the…
- `CampSystem.Cinematic.cs` — CampSystem: the setting-up sequence. Fade to black, the camp goes up, then three slow shots of it in its surroundings (wide, close on the fire, a rising view over the tent), each fading in and out, and back…
- `CampSystem.Layout.cs` — CampSystem: where the camp goes. The fire stands in front of you, the tent behind it and the crafting table at its side, all facing the fire. Each part needs dry, gentle ground with nothing solid in the way…
- `CampSystem.cs` — Auto-created. "Set up camp" (Inventory page of the journal, or a click on the Camp item): finds open, flat ground in front of you, plays a short camera sequence while the tent, the campfire and the crafting…

### Gameplay/Crafting/
- `CraftingRecipe.cs` — Right click in the Project > Create > Inventory > Recipe Ingredients go in, the Result comes out (e.g. Red Berry + Clean Water = Red Tea)
- `CraftingStation.cs` — Put this on a bonfire, crafting table, etc. Walk up to it and press E to open the crafting window.

### Gameplay/Gathering/
- `GatherAnimation.cs` — Gather / loot animation, driven by the player's Animator (creates itself). This script only turns the Bool parameter "isGathering" on and off; the Animator does the rest: Any State -> Gathering Objects…
- `GatherNode.cs` — Put on a tree/bush/rock prefab; hold E near it to gather. Painted Terrain trees work too (add TerrainGatherables).
- `GatherSystem.cs` — Auto-created. Shows "[E] Chop" near a GatherNode; hold E to gather (same look as the loot prompt).
- `LeafPile.cs` — Leaves lying on the ground that react to whoever moves through them. Walking pushes them a little, running more, a dodge throws them up in the air; they flutter and fall back down. Enemies kick them too.…
- `LeafPileResources.cs` — Identical pile appearances share resources; borrowed project textures are never destroyed.
- `TerrainGatherables.cs` — Put on the Terrain: painted trees whose prefab has a GatherNode become gatherable (terrain is not changed).

### Gameplay/Horses/
- `HorseBarrierZone.cs` — "Leave your horse here": a sphere (like the Snow Zone) that horses can't enter. Put it on an empty object at the foot of a mountain path and size it with Radius (and the Transform scale). A ridden horse…
- `HorseLegMud.cs` — Ticked by HorseMount: no extra MonoBehaviour or per-frame mesh baking.
- `HorseMount.AnimationAudio.cs` — HorseMount: Animation & sounds
- `HorseMount.Calling.cs` — HorseMount: Calling
- `HorseMount.Mounting.cs` — HorseMount: Mount / dismount
- `HorseMount.Movement.cs` — HorseMount: Moving (riding and coming)
- `HorseMount.RiderPlacement.cs` — HorseMount: Placing the rider (after the animations, before the camera)
- `HorseMount.Riding.cs` — HorseMount: Riding
- `HorseMount.Setup.cs` — HorseMount: Setup
- `HorseMount.Tick.cs` — HorseMount: Update
- `HorseMount.UI.cs` — HorseMount: UI
- `HorseMount.cs` — Pure input state: a long Shift press always promotes the fallback gait to the intermediate one. Exhaustion requires a release before a new sprint, so stamina recovery cannot restart it by itself.
- `HorseMudSplash.cs` — Owned by HorseMount: no extra Update, searches, rigidbodies or particle collisions.
- `HorseMudTracks.cs` — Hoof prints in mud.
- `HorseSpawner.cs` — Auto-created. Pressing H when your horse is not in the scene creates it from its prefab. - With a HorseStable in the scene (horses must be bought): it creates the horse you chose at the stable. - Without a…

### Gameplay/Loot/
- `DroppedBackpack.cs` — A backpack lying on the ground (dropped with R). Walk up to it: "[E] Equip backpack". Backpacks never go into the inventory: picking one up puts it on your back (if you wear another one, that one is left on…
- `ItemPickup.cs` — A single item lying in the world that you pick up.
- `LootBag.cs` — "Pick one" groups in loot lists: entries sharing a group name are one roll, their chances are shares of it
- `LootChest.cs` — Chest prefab (adds a LootBag). Never disappears; LootBag "Items" are always inside, Random Loot is rolled at start.

### Gameplay/Placement/
- `PlacedObject.cs` — Added automatically to things the player places (tents, bonfires, crafting tables...), so the save system knows which item they came from. PlacedObjectPickup uses it to put them back in the inventory.
- `PlacedObjectPickup.cs` — Auto-created. Walk up to something you placed (bonfire, tent, crafting table...) and press the key to put it back in your inventory.
- `PlacementSystem.cs` — Auto-created. Left click a "Place" item in the inventory -> a see-through preview follows your view. Left click = place, right click (or Esc) = cancel, mouse wheel = rotate.

### Gameplay/Quests/
- `DeliverySystem.cs` — Creates itself at game start (ShopWindow style). Talk to a DeliveryNpc that gives packages, take the package to the NPC it names, get paid. One delivery at a time. The talk is opened from the conversation…

### Gameplay/Rest/
- `RespawnSystem.cs` — Auto-created. Near a placed tent: [E] sets your respawn point. On death: "Wake up nearby" or "Respawn at tent".
- `SleepSystem.cs` — Auto-created. Press the sleep key near a placed tent: fade, sky time-lapse of a FIXED number of hours (Sleep Hours), wake up rested and the tent becomes your respawn point. Add it to a scene object to…
- `TentSpawn.cs` — Put this on the tent prefab. Walk up to a placed tent and press E to make it your respawn point.

### Inventory/
- `InventoryExchangePlan.cs` — Pure planning: nothing is changed unless every cost and the complete output fit.
- `PlayerInventoryManager.cs` — The player's bags: add/remove/count items, capacity (backpack), consume items.

### Inventory/Data/
- `HatPassives.cs` — Stable explicit values: item assets, upgrade copies, saves and network item IDs keep the same meaning.
- `ItemDatabase.cs` — List of every item, so saves can store items by name; auto-filled in the Editor (Assets/Resources/ItemDatabase.asset).
- `ItemUpgrades.cs` — The three upgrade stones used at a crafting table (set on the stone item: Upgrade Stone)
- `PlayerItemData.cs` — New types go at the end so existing items keep their type
- `SwordAbility.cs` — A shared personal reserve: changing swords cannot create charges or erase progress.

### Multiplayer/Core/
- `CoopBalanceRules.cs` — Host-side policy. Integer enemies round once per encounter, not once per enemy type.
- `MultiplayerSession.cs` — Creates itself at game start. Hosting and joining with a code (Unity Relay: works over the internet, no ports to open). HOST: load your save as usual, then MultiplayerSession.Instance.Host() -> a join code…
- `MultiplayerSync.EnemiesClient.cs` — MultiplayerSync: Enemies: client side
- `MultiplayerSync.EnemiesHost.cs` — MultiplayerSync: Enemies: host side
- `MultiplayerSync.JoinWorld.cs` — MultiplayerSync: Client: become part of the host's world
- `MultiplayerSync.Players.cs` — MultiplayerSync: Players
- `MultiplayerSync.Precision.cs` — Co-op: precision kills/ripostes sent between players.
- `MultiplayerSync.Session.cs` — MultiplayerSync: Network start / stop
- `MultiplayerSync.SharedDrops.cs` — Items a player drops on the ground (from the inventory, crafting or gathering overflow) are SHARED in co-op: everyone sees the same bag and each item can only be taken by one player. The host owns every…
- `MultiplayerSync.cs` — Everything that is shared while playing together, with custom network messages (no NetworkObjects, no prefabs to register): - players: position, animation, riding pose, gear, their horse (every player ->…

### Multiplayer/Presentation/
- `NetVisuals.cs` — Multiplayer helpers: copies of characters that only SHOW (no scripts, no physics), animator sync and the gear a player wears.
- `RemotePlayerAvatar.cs` — Another player as you see them: a copy of the player model (no scripts) that follows what that player sends. Created by MultiplayerSync - nothing to set up.

### Multiplayer/UI/
- `MultiplayerPanel.cs` — The multiplayer window (F8) and the join-code banner, in the same brown/gold style as the rest of the UI. Added by MultiplayerSession; it only shows things - all the logic stays in MultiplayerSession.

### Rendering/
- `AnimatorCulling.cs` — Villagers, animals and birds kept animating their whole skeleton every frame even when nobody could see them (behind you, behind a house, far away). This switches only those to "Cull Update Transforms":…
- `ForestPerformance.cs` — Tree/forest rendering budget (distance, shadows) for performance.
- `LeafAppearance.cs` — Two scene-wide appearances, not a material/mesh for each leaf. The VFX cache owns disposal.
- `MudImprintSurface.cs` — Generated once per owned stamp pool: four stable variants, encoded normal/relief/coverage, no per-frame texture work.
- `ParticleMaterials.cs` — Materials for particles and simple objects made by code. The URP particle shaders lose their "transparent" and "alpha clip" versions in a build (nothing in the project uses them, so Unity leaves them out)…
- `ParticleSharedResources.cs` — Explicit owner for the tiny process-wide VFX cache; individual emitters never delete shared resources.

### Saving/
- `SaveData.cs` — Everything that goes into the save file (turned into JSON by WorldSaveGameManager)
- `SaveFileStore.cs` — Disk operations are independent from Unity, so recovery can be tested without opening a scene.
- `SceneStartingGear.cs` — Put this on any object in the map scene (for example the PlayerUI) and drag items into "Items". When you press Play straight in the map scene, the player starts with them equipped (sword, hat, bow, armor,…
- `WorldSaveGameManager.GroundItems.cs` — Saving/loading items dropped on the ground.
- `WorldSaveGameManager.Loading.cs` — WorldSaveGameManager: Loading
- `WorldSaveGameManager.MenuButtons.cs` — WorldSaveGameManager: Menu buttons
- `WorldSaveGameManager.Player.cs` — WorldSaveGameManager: Player
- `WorldSaveGameManager.Saving.cs` — WorldSaveGameManager: Saving
- `WorldSaveGameManager.cs` — Save/load (player, stats, items, gear, hour, placed objects + respawn tent); New Game / Continue / Load / autosave / quick save. Every save is its own file in the 'Saves' folder: F5 and the pause menu's…

### UI/Common/
- `CinematicHudVisibility.cs` — Hides every HUD canvas during cinematic shots (finisher camera, camp setup).
- `MenuTextButton.cs` — Turns a button into just its word: no background, no outline. The word has one colour, and on hover it lights up, grows a little and the letters open up. A disabled button (Continue without a save) is the…
- `MenuWindow.cs` — Base of the code-made menu windows (Settings, Load...), used in the main menu and in the pause menu. Same brown + gold look as the other windows. ESC closes the window that is open (and only that one).
- `ScreenFade.cs` — Full-screen black overlay (creates itself). HoldBlack() = screen goes black at once, FadeIn(seconds) = fades back to the game. Optionally the sound fades with it (AudioListener.volume: music, rain, effects,…
- `TornPaper.cs` — Panels as sheets of old, dark washi paper with hand-torn edges instead of brown rounded boxes. Everything is generated once in code (no image files): a tiled sheet with irregular torn borders, a pale…
- `UIControlMotion.cs` — Hover / selection / press feedback for every code-built control, without changing the layout or the clickable area. A soft glow fills the control and a thin gold line grows under its label; the label (or a…
- `UIFrameGraphic.cs` — A thin even border for panels: one hairline all round plus a faint lighter line along the top edge (reads as depth). One small mesh, no textures, materials, raycasts or Update. Geometry never controls the…
- `UIKit.cs` — Shared helpers for the UI that is built from code (windows, prompts, messages, death/sleep screens)
- `UILeafTransition.cs` — A light flurry of leaves for the big UI moments: opening/closing the journal and turning to another tab. One pooled set of images per canvas, one Update while leaves are in the air, unscaled time (works…
- `UIPanelMotion.cs` — Panel entry and exit: fade plus a small settle in scale (a window "lands" instead of popping in). Scale only: position stays with the layout. Canvas roots are faded only (their scale belongs to the…
- `UIPolish.cs` — Easing curves for UI motion. Smooth = ease in and out; OutCubic = fast start, soft landing; OutBack = soft landing with a tiny overshoot
- `UIStyle.cs` — Brown + gold look shared by the code-made windows (reads the colours/font from LootWindow)

### UI/Gameplay/
- `CraftingWindow.Cook.cs` — Camp window: CAMPFIRE page. Your learned recipes on the left, the chosen one on the right with its ingredients. Cook makes one, Cook all as many as your bags allow. Every cook is checked again at the moment…
- `CraftingWindow.Craft.cs` — Camp window: CRAFT page (the crafting table). Pick a weapon, bow or armour from your bags and an upgrade stone: Unmei (always +stats), Kake (double or nothing: asks twice) or Oni (a random demon perk). One…
- `CraftingWindow.Sleep.cs` — Camp window: SLEEP page (the tent). Pick how long, then Sleep. Your camp: respawn point, how you are, pack it up.
- `CraftingWindow.cs` — The Camp window (creates itself at game start). Walk up to your camp and press E: one window with three pages. SLEEP the tent: rest for a while, sleep the night or until dawn; respawn point; pack up the…
- `ShopWindow.cs` — Creates itself at game start (LootWindow style). Opened from the conversation with a ShopNpc (NpcDialogue: Talk to -> Shop). Left: merchant stock, right: your items. Left click twice = buy, right click…
- `StableWindow.cs` — Creates itself at game start (LootWindow style). Opened from the conversation with a HorseStable (NpcDialogue: Talk to -> Stable). Left: the horses of the stable. Right: a big card of the selected horse…
- `VillageRequestWindow.cs` — Window for village requests (hunt / supplies / pilgrimage / recovery).

### UI/HUD/
- `BossHealthBar.cs` — Boss name + health bar at the top center of the screen (creates itself). Only for enemies with "Is Boss" ticked, and only while you fight them. Normal enemies never show a bar. The name comes from the…
- `CenterMessage.cs` — A short message in the middle of the screen that fades away (creates itself). CenterMessage.Show("text").
- `GameplayHint.cs` — Independent, non-interactive feedback for item use and navigation. No hotbar or input bindings.
- `PlayerUIManager.cs` — Holds the HUD canvas reference (singleton).
- `PlayerVitalHUD.cs` — One presentation per local player, driven by PlayerStats' existing update. No independent Update loop.
- `QuickWheel.cs` — Hold Tab: feudal-style radial wheel to pick a weapon (katana / small swords / bow / sheathe) or eat/drink a consumable.
- `VitalOrbArt.cs` — Generated once at construction, using the ordinary UI shader. No custom shader/material or render camera.

### UI/Inventory/
- `ItemTooltip.cs` — Small box that follows the mouse and shows what an item is. Creates itself when first needed.
- `LootWindow.cs` — Creates itself at game start. To customise the look, add it to a scene object and fill the Style fields.
- `PlayerItemSlot.Confirmation.cs` — Confirm dialogs for item use (eat, drop, etc.).
- `PlayerItemSlot.cs` — One inventory/equipment slot in the journal UI: drag & drop, click to use/equip, tooltip.

### UI/Journal/
- `DiaryProgress.cs` — Journal diary progress counters.
- `MapSystem.Diary.cs` — Journal > Diary page.
- `MapSystem.Exploration.cs` — MapSystem: Explored areas
- `MapSystem.Inventory.cs` — Journal > Inventory page (bags, equipment slots, set up camp button).
- `MapSystem.Journal.cs` — The journal book itself: tabs (Map, Inventory, Objectives, Diary), open/close.
- `MapSystem.MapPhoto.cs` — MapSystem: Map photo
- `MapSystem.Objectives.cs` — Journal > Objectives: the main quest (MainQuest). One button per chapter on the left, its objectives on the right. Locked chapters show only that they are locked; each opens when the previous chapter's…
- `MapSystem.Parchment.cs` — MapSystem: the parchment look of the map (shader Resources/Mossblood/UI/ParchmentMap). Each time the photo is taken, two blurred copies are made once on the GPU (512 px and 128 px). The shader paints the…
- `MapSystem.Stables.cs` — Stables are always drawn on the big map, even under the fog: the journey really starts with a horse, so a new player always has a first place to head for. Until you own a horse the stable is highlighted…
- `MapSystem.UI.cs` — MapSystem: UI
- `MapSystem.Waypoint.cs` — Map waypoint (click on the map) and its marker.
- `MapSystem.cs` — Creates itself at game start. Takes a top-down "photo" of the terrain and shows it as a minimap (top-right, off by default) and a big map with M (close with M or Esc). Parts you haven't been to yet are…

### UI/Menus/
- `PauseMenu.cs` — Pause menu (creates itself): Continue / Save / Load / Settings / Exit to Main Menu. Offline the game is frozen while it is open; online time keeps running (the others are still playing).
- `SaveLoadWindow.cs` — Load window (creates itself): every save, newest first. Click one to load it, "Delete" to remove it. Also the small "Continue" question of the main menu: last save, or choose one.
- `SettingsWindow.cs` — Settings window (creates itself): Controls / Gameplay / Video / Audio tabs. ESC goes back. Opened from the main menu and from the pause menu: SettingsWindow.Open(whatToDoWhenClosed)
- `TitleScreenManager.cs` — Main menu / title screen (New Game, Continue, Load, Settings, Quit).

### VFX/
- `BloodEffects.Bleeding.cs` — Bleeding status visuals (drips while bleeding).
- `BloodEffects.Pooling.cs` — Blood particle/decal pooling and cleanup.
- `BloodEffects.Spray.cs` — A curved sheet in the blade's cutting plane, followed by ordinary ballistic motion.
- `BloodEffects.cs` — Blood particles on hits. Works by itself; add this component to a scene object to change how it looks.
- `BloodiedBlade.cs` — One coating and one bounded particle system per actual weapon. No per-drop GameObjects.
- `CursedAfterimage.cs` — Oni Stone curse: while cursed (and in combat), the player leaves a blurry grey ghost when drawing, starting/stopping running or dodging. Self-creates at start; add it to a scene object (e.g. World Managers)…

### World/
- `WorldCleanup.cs` — The one place that removes leftover objects from the world so they don't pile up: loot bags (from enemies and the ones you drop), arrows, enemy and animal bodies. Put it on any object of the scene to choose…

### World/Environment/Atmosphere/
- `AtmosphereRegion.cs` — Optional art direction for a grove, riverbank or snowy area. No collider and no per-region Update.
- `GroundCritterMeshes.cs` — Small shared silhouettes with two gait poses per species, generated once per pool.
- `GroundCritterPool.cs` — One managed pool with three paused native mesh renderers: shared poses, no actors, rigidbodies, Animators or per-animal Updates.
- `LivingAtmosphereRules.cs` — Rules for ambient life moments (when/where critters, mist appear).
- `LivingEnvironment.MistBanks.cs` — Low mist banks part of LivingEnvironment.
- `LivingEnvironment.cs` — Local ambient life. Five native particle pools, one low-rate context probe and no per-particle scripts/physics.
- `LivingEnvironmentSettings.cs` — Settings asset for LivingEnvironment.
- `LocalMistVolumePool.cs` — Bounded fog volumes: shared box mesh/material, world-space density, no billboards or physics.

### World/Environment/Effects/
- `BonfireFire.cs` — Bonfire flames, embers, smoke and night-strong flickering light; auto-added to '...fire...' CraftingStations, also previews in edit mode.
- `CanopyHumidity.cs` — Local, bounded canopy water. Two emitters for the entire effect; never one GameObject per drop.
- `DecayAtmosphere.GroundDust.cs` — Dust kicked up by the player's dodge and by a body hitting the ground. Same particles, texture, colours and wind as the gust dust waves (pool 2), so it reads as the same dry earth. Only on dry, open ground…
- `DecayAtmosphere.Horses.cs` — Flies around horses (part of DecayAtmosphere).
- `DecayAtmosphere.Particles.cs` — Particle setup for DecayAtmosphere.
- `DecayAtmosphere.cs` — Local cosmetics only: four fixed native particle pools, a bounded fly buffer and low-rate context probes.
- `FallingLeaves.Blossom.cs` — The existing grounded-leaf simulation also owns petals. One bounded native pool, no petal GameObjects/Rigidbodies.
- `FallingLeaves.GroundCarpet.cs` — Shared by the tree leaves, blossom petals and floor piles.
- `FallingLeaves.LocalWind.cs` — Gusts that blow fallen leaves around.
- `FallingLeaves.Persistence.cs` — How long fallen leaves stay on the ground.
- `FallingLeaves.cs` — Leaves falling from the Terrain trees around the player, blown by the wind (more in the rain). Creates itself at game start; add it to a scene object to change settings. Leaf pictures: PNG with transparency…
- `FlySwarm.cs` — A few tiny black flies buzzing around the player, just for atmosphere. Creates itself at game start; add it to a scene object only if you want to change the settings. Costs almost nothing (a handful of…
- `GroundMud.cs` — Patches of wet mud on the ground around the player: on dirt, paths and roads (terrain layers whose name says so) and on flat low ground. Drawn with Resources/Mossblood/Water/GroundMud.mat: the scanned…
- `GroundMudPalette.cs` — Small readable copies, never Read/Write-enabled production textures. Samples happen only when a patch is placed.
- `LocalRainPuddles.cs` — Reuses the ocean shader/live sky cube. One material, a bounded mesh pool, no water volumes or extra cameras.
- `PaintedGroundMud.cs` — Permanent material detail of the painted Grass Soil layer, streamed in small terrain-following chunks. This does not paint/change TerrainData, spawn random dry puddles, or render the distant map twice.
- `PlayerBreathVapour.cs` — Independent local-player breath, evaluated after the procedural head pose.
- `TravelerWeathering.Surfaces.cs` — Wet/dirty material surfaces on the player's clothes.
- `TravelerWeathering.cs` — One local cosmetic controller. Existing footsteps/leg mud remain responsible for foot contacts.
- `WetGroundOccupancy.cs` — One bounded placement registry for both systems. A fading surface keeps its claim until invisible.
- `WetSurfaceRules.cs` — Shared bounded coverage and footprint variation. No extra rain systems, cameras or world objects.

### World/Environment/Lighting/
- `DayNightCycle.cs` — On the Directional Light: moving sun by day, soft blue moonlight at night; sky, ambient and fog follow the hour.
- `ObjectLight.cs` — Put this on ANY object (a stone lantern, a torch, a window...) and it lights up: a warm flickering point light. - You see the light in the Scene view right away (no need to press Play), so you can place it…
- `SkyboxCycle.NaturalSky.cs` — Natural sky colours for the skybox cycle.
- `SkyboxCycle.cs` — Fades between 9 Skybox/Panoramic skies: Day/Sunset/Night x Clear/Cloudy/Storm (dry: random clear/cloudy; rain: storm).
- `SunDisc.cs` — Draws the sun (moon at night) toward the Directional Light, since photo skyboxes don't; DayNightCycle adds it automatically.
- `SunEffects.cs` — Makes the sun look strong: bloom glow, lens flare with streaks, a big warm halo and slowly turning sun rays. DayNightCycle adds it to the Directional Light by itself. Everything is strongest when you look…
- `WorldColorGrade.cs` — Applies the standard world palette (WorldColorGradeSettings) as a low-priority global Volume. Snow and swamp zones sit above it and take over inside their areas.
- `WorldColorGradeSettings.cs` — The game's standard colour grade (outside snow and swamp zones). Asset: Resources/Mossblood/Atmosphere/WorldColorGrade. Strength 0.2 = 20% of the swamp palette. Changes apply live while playing.

### World/Environment/Water/
- `WaterAppearance.cs` — The look of a water plane while playing: live sky reflection, Fresnel, sun glitter, refraction, depth colour, a soft shore with a little foam and a drifting mist over the water (shader:…
- `WaterSurfaceDepthFeature.cs` — Refraction still sees the opaque bottom. Afterwards, fog/post effects must see the water surface depth.
- `WaterVolume.cs` — Put this on the water plane (the object with the water material). The plane's height is the water surface; its size (seen from above) is where the water is ("Infinite" = everywhere). The player can walk…

### World/Environment/Weather/
- `DistantLightning.cs` — Reusable bolt with a transient flash; no extra cameras, shadow lights or per-strike objects.
- `FoliageWind.cs` — Weather and navigation wind. Native vegetation keeps its authored animation phase; steering is applied on the GPU.
- `GroveBirds.cs` — A flock of birds over a GroveZone (lives on the Local grove atmosphere object). Now and then, by day and when it is not raining, a flock arrives from far away, circles over the glade for a while and then…
- `GroveZone.cs` — A 3D sphere (or ellipsoid, with non-uniform scale) where a hidden glade comes alive: warmer, richer light, clearer air with a golden haze for sunbeams, pollen motes twinkling in the sun, white petals…
- `GuidingWind.cs` — Local navigation feedback; the marker and wind never replicate into another player's route/weather.
- `LocalGroveAtmosphere.cs` — One local glade atmosphere for all GroveZones (same model as LocalSwampWeather / LocalSnowWeather). Particles are emitted by script around the local player, at the real ground height, only inside the zone.…
- `LocalSnowWeather.cs` — One local weather field for all SnowZones, including overlapping zones and multiplayer.
- `LocalSwampWeather.cs` — One local marsh field for all SwampZones (same model as LocalSnowWeather). Particles are emitted by script at real ground or water height around the local player, so bubbles sit on the ground and the water…
- `RainSystem.cs` — Put on an empty GameObject (e.g. "Weather"): rain around the camera with splashes, sound, darker light and fog. Starts/stops on its own (or with a test key); each rain picks a random preset (light rain,…
- `SnowZone.cs` — A true local-space sphere, not a horizontal radius/altitude test. No collider needed.
- `SwampZone.cs` — A 3D sphere (or ellipsoid, with non-uniform scale) where the world turns into marsh: dark green grading, thicker low fog, rising bubbles, drifting spores and moss, ground mist and marsh lights at night.…
- `VolumetricFogWeather.cs` — Makes the Volumetric Fog (Global Volume > Volumetric Fog override) react to the rain (creates itself). When it rains: thicker fog (preset "Volumetric Density" / "Volumetric Visibility"), the preset's fog…

### World/Navigation/
- `DryLand.cs` — Shared land checks. Water volumes are the source of truth, including lakes without colliders.
- `GroundAdhesion.cs` — Short, collision-based ground adhesion. Never follows a distant floor across a drop or an upward jump.

### World/Rendering/
- `WorldDistanceVisibility.cs` — Render-distance culling only: never deactivates gameplay, colliders, loot or save objects.
- `WorldVisibilityOverride.cs` — Optional override on the root of a world object. Gameplay components remain active.

### World/Shrines/
- `Shrine.cs` — Put this on a shrine in the scene. Inside Pray Radius: [E] Pray. You pray for Pray Seconds (the screen fades, the camera looks at the shrine from outside) and your spirit comes back. Found shrines show on…
- `ShrineSystem.cs` — Creates itself at game start. Praying at shrines (put a Shrine component on each one): inside a shrine's Pray Radius: "[E] Pray at <name>". E: the screen fades out, the player kneels in front of the shrine,…

### World/Spawning/
- `EnemySpawner.cs` — Spawns a group of enemies (camp) around a point when the player is near; respawn rules; enemy prefabs per spawner.
- `RoamingPatrolSpawner.cs` — Surprise patrols: every scheduled attempt can create a group near the player, including while idle (not too close) and marches in one direction, crossing in front of you. If they see you, they fight. Put it…
- `SpawnPlacement.cs` — Shared checks so enemies never appear on roofs, walls or other places they can't walk out of. A spot is good when it is close to the terrain height and the NavMesh connects it to the anchor (the spawner's…

### World/Streaming/
- `ChapterGate.cs` — "Onistone Gate": a spirit veil across the way (for example the start of the bridge to the next island) that only lets you through once you have taken a certain Onistone. On foot or on horseback, it doesn't…
- `WorldZone.cs` — One island (or any big region): put this on the empty object that holds everything of that island. Far from it, its contents are switched off (no drawing, no physics, no scripts); coming closer they come…
- `WorldZoneStreamer.cs` — Auto-created by the first World Zone. Decides which islands are needed (you, your friends in co-op, the camera) and switches their contents on / off a little every frame within a small time budget. Behind a…
- `ZoneKeepLoaded.cs` — Put this on anything inside a World Zone that must never be switched off (a scripted object that other systems look for across the whole map, something with its own timers...). Its children stay on too.
- `ZoneLandMask.cs` — The shape of an island, read from the terrain: the land above the water line that the zone's contents stand on, plus whatever of the zone stands off the shore (piers, boats, rocks in the sea). Other islands…
- `ZoneTitleCard.cs` — Auto-created. When you set foot on a World Zone (its land), its name rises on screen like a chapter title: a small line above, the name in wide letters, two thin gold strokes drawing out from it, then it…