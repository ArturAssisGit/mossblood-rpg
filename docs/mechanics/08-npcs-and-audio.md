# NPC life and audio

**Pillar:** A living world
**Scripts:** `NpcIdle.cs`, `NpcMotionSet.cs`, `NpcDialogue*.cs`, `FootstepSurface.cs`, `Footsteps.cs`, `EnemyFootsteps.cs`, `Voices.cs`, `PlayerVoice.cs`, `UISoundRouter.cs`

## Goal

Villages should feel lived in and the world should sound physical: different ground under each foot, people who breathe, sigh and laugh, and one consistent voice for the interface.

## NPC life

- Idle by default, but alive: a few steps (2.6 m or less) around their place and back, picking something up, sitting on the ground for a while.
- Never while the player is within 4.5 m or talking to them; always checked for walls, drops and steep ground.
- They turn to face whoever talks to them, hum, sigh and laugh, and have footsteps.
- Built on a PlayableGraph mixer (idle, walk, gather, sit down, sitting, stand up): no Animator Controller to maintain per NPC.
- A villager's identity (fast travel, deliveries, requests) uses their home position, so walking around never changes who they are.

## Footsteps

- The surface under each foot is read from the terrain layer (or the object's material), plus a registry of wet ground that tells mud from puddles.
- The player has full steps; enemies are quieter and distance-attenuated so they feel far until they are close.

## Voices

- Two male and two female voice sets cover attacks, effort, hits, pain, jumps, landings, gasps, coughs, sighs and laughs.
- The player breathes hard after running, coughs or clears his throat every 45 to 95 s (every 20 to 40 s in the cold) and groans at low health, never during a fight, a conversation or a menu.

## Interface

One UI sound set for every code-built window: hover, click, panel open / close, tab turn, coins, equip, loot, drag and drop, error. Routed in one place so the whole UI stays coherent.

## Iterations

| Problem | Change |
|---|---|
| NPCs stood like statues | Wandering and actions with safety checks and a distance budget |
| NPCs kept talking to the air | They now face the speaker |
| Every surface sounded the same | Footsteps per terrain layer and wet-ground type |
| The main menu showed a button as hovered at start | Selection only draws as hover when navigating with keys / gamepad |
