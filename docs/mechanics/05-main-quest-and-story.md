# Main quest and story: the five Onistones

**Pillar:** A quiet, sad story told by people / Rewarding exploration
**Scripts:** `MainQuest.cs`, `NpcLoreBook.cs`, `OnistoneLore.cs`, `AdventureProgress.cs`, `NpcDialogue.Lore.cs`, `ChapterGate.cs`, `WorldZone.cs`, `LootBag.cs`, `WorldProgressionCheck.cs`

## Goal

The main quest should feel like detective work, not like following a marker. The player learns where each Onistone lies by talking to people, and the story underneath (a son following his father, who is losing himself to a demon stone) is told through what those people remember.

## Player experience

- **Asking, not reading a map:** in every conversation *Ask About the Land* is always the first option. Each person gives one piece of the story and names who to ask next.
- **Clear progress:** the first time someone tells their part, a message shows "Onistone story: 2/5 people asked" and Journal > Objectives lists what each person said, in the order of the chain.
- **Grief in small things:** "Have You Seen My Father?" gets a different memory from each person (a comb sold for rice, a bowl of soup nobody touched). They go to Diary > Your father's trail, under his last letter.

## Rules

| Rule | Why |
|---|---|
| Five chapters, one island and one stone each; chapter I is open from the start | One clear thread across the whole game |
| A region's Onistone chest opens only after every person of that region was asked | The clues are the gameplay, not decoration |
| Taking a stone is recorded for good in `AdventureProgress` | Gates and islands check the record, never the inventory: using, selling or losing the stone can never lock the player out |
| An Onistone Gate (spirit veil) on each bridge asks for the previous chapter's stone | Islands open in story order; a locked island also turns back anyone who swims or jumps in |
| Counters are computed in one place (`NpcLoreBook.StoryPeople`) | The talk message, Objectives and the Diary always show the same number |

## Iterations

| Problem found while playing | Change |
|---|---|
| Zone I showed "1 of 6" people with only five in the world | Counters now only count people who belong to that stone, from one shared function |
| The stable master told his story but never counted toward "x/5" | He is now one of the valley's five; the chain is Kaito → Nora → Rutso → Jinn → stable master, and the last one sends you up the mountain |
| A story entry pointed to a person who never existed in the scene, so the counter could never reach 5/5 | Removed it, and added a Play-mode warning plus an editor check that lists every counted person missing from the world |
| Island names had to match exactly ("Pilgrim Valley" vs "The Pilgrim Valley") | `RegionIndex` ignores case, spaces and a leading "The" |
| An NPC name that contained two keys could match the wrong entry | The longest (most specific) match wins |
| The chest's chapter was guessed from its position | It now comes from the island (WorldZone) it belongs to |

## Tooling

**Mossblood > Check > Main Quest and Zones** walks every chapter of the open scene and reports problems: missing island, wrong lock, missing gate, a story person not in the world, no Onistone chest, a stone that is only a random roll. Adding an island is a checklist: [Adding a New Zone](../adding-a-new-zone.md).

## What I would test next

- Whether players read the Objectives page or ask to be told where to go.
- How many people per island keep the chain interesting without becoming a chore (five today).
