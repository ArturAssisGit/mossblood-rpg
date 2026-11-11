# Performance: a large world on a budget

**Pillar:** Everything (a world that stutters is not alive)
**Scripts:** `WorldZoneStreamer.cs`, `WorldDistanceVisibility.cs`, `ForestPerformance.cs`, `AnimatorCulling.cs`, `NpcIdle.cs`, `WorldCleanup.cs`, `PerformanceTools.cs`, `ShadowBakeAssistant.cs`, `EnsureRuntimeShaders.cs`

## Goal

Mossblood is planned as five islands of about 1000 × 1000 m with dense forests, villages and a lot of ambient life. Performance is part of every design decision, not a cleanup at the end.

## Rules every script follows

- No `Find*` and no allocations every frame; cache components; use registries (`EnemyAI_01.All`, `LootBag.All`…).
- Pool particles and objects; bounded buffers instead of one GameObject per drop, leaf or petal.
- Spread work over frames (slices, queues, time budgets); do the work only when something changes or is read.
- Build textures and meshes once at start (mud stamps, critter silhouettes, UI paper, orbs), never per frame.
- Restore anything you change at runtime (fog, LOD bias, shadow distance) when you stop.

## The main techniques

| Problem | Technique | Result |
|---|---|---|
| The whole map was simulated at once | Islands sleep when far (`WorldZone`) and wake within a per-frame time budget, nearest first (`WorldZoneStreamer`) | Far islands cost nothing; no hitch when one wakes |
| Thousands of props drawn far away | `WorldDistanceVisibility` hides renderers by distance to their bounds, a slice per frame; never touches gameplay, colliders or loot | Fewer draw calls; big buildings stay on the horizon |
| Deep forests drew every tree for 250 m behind the trunks | `ForestPerformance`: when surrounded by trees and looking into more, fog thickens a little and tree, grass, shadow distance and LOD bias (2 → 1.2) follow it | Millions of hidden triangles skipped; no visible cut-off |
| Villagers and animals posed every bone even off screen | `AnimatorCulling` switches scenery characters to "Cull Update Transforms" | Same picture, far less animation work |
| NPCs deciding to wander everywhere | `NpcIdle` does nothing beyond 55 m from the camera | Life only where it can be seen |
| Effects spawning objects | Native particle pools for blood, leaves, petals, critters, mist, puddles, dust, lightning | Bounded cost whatever happens on screen |
| Leftovers piling up | `WorldCleanup` removes loot bags, arrows and bodies on timers (backpacks and chests protected) | Memory and draw calls stay flat over long sessions |
| Terrain grass and shadows | `PerformanceTools` report; lighter terrain and shadow settings | Grass estimate 9.3M → 1.5M triangles |
| Pink materials in builds | `EnsureRuntimeShaders`, `ParticleMaterials` keep shaders found by name and transparent particle variants | Builds look like the editor |

## Process

1. Profile (Unity Profiler, Frame Debugger, the performance report).
2. Find the biggest cost that the player cannot see (hidden trees, off-screen skeletons, far islands).
3. Remove it without changing what the player sees; if the picture changes, hide it in something natural (fog, haze, distance).
4. Measure again on the same route.

## What I would do next

- Profile a Windows build on a mid-range PC per island.
- Occlusion culling for villages; LODs for the heaviest props.
