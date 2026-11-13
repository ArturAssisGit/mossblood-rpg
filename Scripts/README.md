# Scripts

The game's C# code (`Assets/Scripts` in the Unity project).

| Folder | Contents |
| --- | --- |
| `Core` | Global settings and shared world helpers |
| `Audio` | Sound manager, footsteps per surface, voices, UI sounds |
| `Characters` | Player (movement, procedural animation, camera, combat, equipment, stats), enemies and bosses, NPCs and dialogue |
| `Animals` | Animal AI, Playables animation and spawning |
| `Inventory` | Bags and item data (items, upgrades, sword abilities) |
| `Gameplay` | Main quest and activities, camp, crafting, gathering, horses, loot, placement, deliveries, rest |
| `World` | Island streaming and gates, seasons, environment (atmosphere, effects, lighting, water, weather), spawning, shrines, navigation, render distance |
| `Rendering` | Performance helpers (forest budget, animator culling) and shared effect resources |
| `UI` | Common UI kit, HUD, inventory, journal (map, objectives, diary), menus, gameplay windows |
| `Saving` | Save data, file store and the save / load manager |
| `Multiplayer` | Session, sync messages, remote player presentation, panel |
| `VFX` | Blood, bloodied blades, cursed afterimage |
| `Editor` | Editor-only tools, grouped by system |

**Conventions**

- Big classes are split into partial files by topic (`PlayerCombat.Precision.cs`, `EnemyAI_01.Archer.cs`); add new code to the matching file.
- Tuning numbers live in static *rules* classes (`PlayerRules.cs`, `EnemyRules.cs`…).
- Many systems create themselves at game start; they are not placed in the scene by hand.
- Play mode runs without a domain reload: every mutable static is reset in a `SubsystemRegistration` method.
- When moving a script in the Unity project, move its `.meta` too: scenes and prefabs reference the GUID.
