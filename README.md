# Mossblood

> Follow your father's trail across a feudal Japan of islands, where every demon stone makes you stronger, and takes something in return. A third-person survival action RPG: explore on foot or on horseback, read enemies to kill them in one blow, survive weather, seasons and the cold, and piece together the story from the people you meet. Solo or online co-op (2–4 players).

![Unity](https://img.shields.io/badge/Unity-6000.6.2f1-black?logo=unity) ![URP](https://img.shields.io/badge/Render-URP-blue) ![C#](https://img.shields.io/badge/Language-C%23-purple) ![Netcode](https://img.shields.io/badge/Multiplayer-Netcode_%2B_Relay-green)

![Gameplay](docs/media/gameplay.gif)

---

## About

A solo project in Unity 6 (URP), started in August 2026. The main quest spans five planned islands linked by bridges. Three zones are present in the Unity scene: The Pilgrim Valley, The Southern Coast and The River Lands. Each chapter follows one of the five **Onistones**, demon stones every warlord wants. Design pillars:

1. **Rewarding exploration** – every place offers gear, a relic, a person with part of the story, danger or scenery.
2. **Combat with weight** – clear attacks, blocks and dodges; a strike released inside the enemy's swing kills in one blow.
3. **Survival that pressures without frustrating** – hunger, thirst, bleeding, cold and sleep create rhythm, not punishment.
4. **A living world** – day/night, seasons, rain and snow, wind, mud, animals, villagers and sound react to each other.
5. **A quiet, sad story told by people** – no quest markers: NPCs give indirect clues, and each one remembers your father.

References: *The Witcher*, *Outward*, *Ghost of Tsushima*, *Sekiro*, *Red Dead Redemption*.

Boss Gameplay
https://github.com/user-attachments/assets/6130a541-de56-4bfb-b9be-cfd8a44969ae

Horse And Npc
https://github.com/user-attachments/assets/6ee463ab-6386-477b-836b-ee4dcd7f78eb

Enemy Combat
https://github.com/user-attachments/assets/830597a1-d161-4b4c-937f-7b4fc01ef5bb

Multiplayer And Saves
https://github.com/user-attachments/assets/61ddd9b8-1cc3-4055-9c9f-60a94dd1dee8

## The story

You are the son of Daigo, a samurai. Warlords hunting demon stones burned your village, killing your mother and little sister. Daigo set a chip of an Onistone into his blade to make sure it never happened again, and it is eating his memories. A new game opens with his last letter: *"Do not follow me. Burn my letters. Live. But you are my son, so you will follow anyway."*

Each island is a chapter. You ask its people about the Onistone; each knows one piece and points to the next person. When everyone has been asked, the clues lead to the stone; taking it opens the bridge to the next island. Every person also remembers something of your father.

## Features

**Combat**
- Katana, two small swords (own animation set) and bow with a limited, refilling quiver
- Combos with buffered input, hold-to-charge strikes and **precision kills**: release a charged strike inside the enemy's swing for a one-hit kill with slow motion; perfect guard opens a riposte
- Sword arts with a shared eight-hit reserve, a quick weapon wheel and auto-draw when hunted
- Cinematic finishers, hit-stop, blood on blade and ground, bleeding status
- Bosses with intro, lock-on camera, strafing, dodges and a fresh restart after you die
- Group AI that takes turns; archers that hold their aim; surprise patrols on the road

**Story and quest**
- Five chapters, a chain of NPC clues per island, Objectives page with everything learned
- Onistone gates on bridges and locked islands; taking a stone is recorded for good
- Diary with the father's letter and his trail through every person you meet

**Survival and world**
- Hunger, thirst, bleeding, body temperature, spirit (shrines)
- One-item camp: tent, campfire and crafting table with a camera sequence; sleep, cook, upgrade
- Islands that stream in and out, zone title cards, closed islands that turn you back
- Seasons (summer → autumn → winter) with recoloured trees and grass; rain becomes snow in winter
- Rain presets, distant lightning, snow / swamp / grove weather zones, mud, puddles, wet clothes, guiding wind
- Birds, foxes, critters, falling leaves and petals, leaf piles, mist

**People, travel and economy**
- NPCs that wander, gather, sit, face you when you talk and have voices
- Merchants with rotating stock, village requests, relics, deliveries and fast travel (only to people met)
- Horses bought at stables (always shown on the map), called with H, walk / trot / gallop

**Ronin League, war and expeditions**
- Earn honour to rise through a league of 40 rivals and challenge ronin in the arena
- Hire ronin, pay wages and lead available fighters into field battles
- Send a party on an expedition and collect spoils from the journal

**Menus, saves and online**
- New games open with Daigo's letter in four pages while the world loads
- A first-steps tutorial covers movement, combat and the journal. Press F1 to skip it.
- Save list like *The Witcher* (quick, manual, rotating autosaves), atomic writes
- Online co-op with join codes (Unity Relay): each player keeps their own save; time, weather and enemies are shared

## Technical highlights

| Topic | What I did |
|---|---|
| **Architecture** | C# scripts organised by system; big classes split into topic partials (`PlayerCombat.Precision.cs`, `EnemyAI_01.Archer.cs`…); tuning numbers in static *rules* classes; self-creating systems; statics reset for Play mode without domain reload |
| **World streaming** | Islands sleep when far (no drawing, physics or scripts) and wake within a per-frame time budget, nearest first; all at once behind a black screen for fast travel and loading |
| **Performance** | Render-distance culling sliced across frames, deep-forest budget (fog + tree/grass/shadow/LOD bias), animator culling for scenery characters, native particle pools everywhere, no per-frame searches or allocations. [Details](docs/mechanics/07-performance.md) |
| **Quest robustness** | One function counts story progress; a permanent progress record drives gates and locks; an editor check validates every chapter in the scene |
| **Animation** | Animator blend trees, foot IK, procedural life layer, and the **Playables API** for NPCs (6-input mixer: idle, walk, gather, sit…) and animals |
| **Networking** | Netcode for GameObjects + Relay with custom named messages (players 15 Hz, enemies 10 Hz, host-authoritative enemies, validated client hits, token-based precision kills) |
| **Audio** | Data-driven libraries: footsteps per terrain layer and wet ground, four voice sets, one UI sound set |
| **UI in code** | Every window built from code with one style (washi paper with torn edges, brown and gold), panel motion, keyboard / mouse aware selection |
| **Save system** | One JSON file per save, atomic writes (`.tmp` → `File.Replace` + `.bak`), safe defaults so old saves always load |
| **Editor tooling** | Main-quest checker, performance report, shadow bake audit, scabbard builder, item database builder; one-shot setup scripts delete themselves |

## Where to look in the code

| System | Main scripts |
|---|---|
| Combat | `Scripts/Characters/Player/Combat/PlayerCombat*.cs`, `PlayerDodge.cs`, `PlayerFinisher.cs`, rules in `PlayerRules.cs` |
| Enemy AI and bosses | `Scripts/Characters/Enemies/EnemyAI_01*.cs`, `BossEncounterDirector*.cs`, `EnemyRules.cs` |
| Main quest and story | `Scripts/Gameplay/Activities/MainQuest.cs`, `OnistoneLore.cs`, `AdventureProgress.cs`, `Scripts/Characters/NPCs/NpcLoreBook.cs` |
| NPCs and dialogue | `Scripts/Characters/NPCs/NpcDialogue*.cs`, `NpcIdle.cs` |
| World streaming | `Scripts/World/Streaming/WorldZone.cs`, `WorldZoneStreamer.cs`, `ChapterGate.cs` |
| Seasons and weather | `Scripts/World/Seasons/*`, `Scripts/World/Environment/Weather/*` |
| Performance | `Scripts/Rendering/*`, `Scripts/World/Rendering/WorldDistanceVisibility.cs`, `Scripts/World/WorldCleanup.cs` |
| Ronin League and arena | [Activities](Scripts/Gameplay/Activities) |
| War and expeditions | [Army](Scripts/Gameplay/Army) |
| Horses | `Scripts/Gameplay/Horses/HorseMount*.cs` |
| Camp, crafting | `Scripts/Gameplay/Camp/*`, `Scripts/UI/Gameplay/CraftingWindow*.cs` |
| Audio | `Scripts/Audio/*` |
| Saves | `Scripts/Saving/*` |
| Multiplayer | `Scripts/Multiplayer/*` |
| Menus and UI | `Scripts/UI/*` |

## Design documents

- [Game Design Document (v5)](docs/GDD%20-%20Mossblood%20v5.docx)
- [Technical Design Document (v5)](docs/TDD%20-%20Mossblood%20v5.docx)
- Mechanic breakdowns:
  - [Combat and boss fights](docs/mechanics/01-combat-and-bosses.md)
  - [Survival: needs, sleep and the sea border](docs/mechanics/02-survival.md)
  - [Inventory, backpack and loot cleanup](docs/mechanics/03-inventory-and-backpack.md)
  - [Enemies, patrols and animals](docs/mechanics/04-enemies-patrols-animals.md)
  - [Main quest and story](docs/mechanics/05-main-quest-and-story.md)
  - [The world: islands, seasons and weather](docs/mechanics/06-world-islands-seasons-weather.md)
  - [Performance](docs/mechanics/07-performance.md)
  - [NPC life and audio](docs/mechanics/08-npcs-and-audio.md)

## Controls

| Key | Action |
|---|---|
| WASD / Mouse | Move / camera |
| Shift | Run (horse: tap = trot, hold = gallop) |
| Space / C / Left Alt | Jump / slide / dodge |
| 1 / 2 / 3 | Katana / bow / small swords |
| LMB (tap / hold) / RMB | Combo / charged strike / second attack or aim |
| F / X / Q | Sword art / switch small sword / block |
| Tab (hold) | Quick wheel |
| E / G | Interact, talk, loot, camp, mount / take an Onistone, read a relic |
| I / M | Journal / map |
| H | Call horse |
| R / T | Drop backpack / pick up placed object |
| Caps Lock | Scenic view |
| F5 / P / F8 | Quick save / pause / multiplayer |

## Development process

Designed, directed and tested by me. I use an **AI-assisted workflow** (Claude): I define each mechanic, its feel and its rules, review and integrate the code, test it in Unity and iterate. The mechanic documents show those iterations: what broke in play and what changed.

## Running the project

- Unity **6000.6.2f1** with URP.
- This repository holds the **source code and documentation**. Running the game requires the full Unity project, including scenes, settings and licensed assets.

## Roadmap

- [ ] Validate Chapter III from the bridge to the Patient Onistone
- [ ] Build the islands for Chapters IV and V
- [ ] A special animated ability for each Onistone set into gear
- [ ] Deeper economy (hunting, fishing, land and a house)
- [ ] Balancing pass per island and co-op playtests
- [ ] Windows build and trailer

## Author

**Artur Assis** – 3rd-year Computer Science and Multimedia Engineering (LEIM) student at ISEL, Lisbon.
[LinkedIn](https://www.linkedin.com/in/artur-assis-2443742a2)
