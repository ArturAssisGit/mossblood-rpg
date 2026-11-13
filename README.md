# Mossblood

A third-person action and survival RPG set in feudal Japan. Follow Daigo's trail across a chain of islands, reading enemy attacks in combat and piecing together clues from the people who remember him.

**Artur Assis** · Solo developer · C# · Unity 6.6 (6000.6.2f1) · URP · [LinkedIn](https://www.linkedin.com/in/artur-assis-2443742a2)

## Gameplay demos

- [Boss encounter](https://github.com/user-attachments/assets/6130a541-de56-4bfb-b9be-cfd8a44969ae)
- [Horse riding and NPC interactions](https://github.com/user-attachments/assets/6ee463ab-6386-477b-836b-ee4dcd7f78eb)
- [Melee combat](https://github.com/user-attachments/assets/830597a1-d161-4b4c-937f-7b4fc01ef5bb)
- [Online co-op and save system](https://github.com/user-attachments/assets/61ddd9b8-1cc3-4055-9c9f-60a94dd1dee8)

![Mossblood gameplay preview](docs/media/gameplay.gif)

## Project highlights

| System | Implementation | Source |
| --- | --- | --- |
| Combat | Buffered attacks, timed precision kills and perfect guards. Katana, dual swords and bow share the player's combat flow. | [Player combat](Scripts/Characters/Player/Combat) · [Enemy AI](Scripts/Characters/Enemies) |
| World streaming | Activates nearby island content within a per-frame time budget. Distance culling and pooled effects manage the cost of dense terrain and vegetation. | [Zone streaming](Scripts/World/Streaming) · [Rendering](Scripts/Rendering) |
| Online co-op | Unity Relay join codes and custom Netcode messages. The host simulates enemies and validates client hits. Players keep their own character progress. | [Multiplayer](Scripts/Multiplayer) |
| Quest and saves | A permanent record of claimed Onistones controls gates and chapter progression. JSON saves use atomic replacement and support older save data. | [Adventure progress](Scripts/Gameplay/Activities/AdventureProgress.cs) · [Save system](Scripts/Saving) |
| Ronin progression | Arena duels, recruitment, wages, field battles and expeditions tracked in the journal. | [Activities](Scripts/Gameplay/Activities) · [Army and expeditions](Scripts/Gameplay/Army) |

The current Unity scene contains The Pilgrim Valley, The Southern Coast and The River Lands. The main quest spans five planned chapters. The current build also includes camps, crafting, seasonal weather, horseback travel and a tutorial for new players.

## Design and technical documentation

- [Game Design Document v5](docs/GDD%20-%20Mossblood%20v5.docx)
- [Technical Design Document v5](docs/TDD%20-%20Mossblood%20v5.docx)
- [Mechanics case studies](docs/mechanics) covering combat, survival, inventory, AI, quest progression, world systems, performance and audio

## Development

I design the mechanics, direct implementation, review and integrate the code, and test and tune the systems in Unity. I use AI assistance during development. The case studies describe design decisions, playtest findings and the changes that followed.

This repository contains the gameplay source and documentation. Running the game requires the full Unity project, including scenes, settings and licensed assets.

**Artur Assis** · Computer Science and Multimedia Engineering student at ISEL, Lisbon.
