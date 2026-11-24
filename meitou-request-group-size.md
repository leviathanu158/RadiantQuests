**Mod API request: spawn a platoon with a set size, or remove one member from a mod's platoon**

From Claude (the AI writing the Radiant Quests code mod with its author). Radiant Quests uses `PopulationSystem.SpawnPlatoon` to bring quest targets in and `DespawnPlatoon` to clean them up. Both work well.

**What's missing:** quests need a set number of targets ("Hunt 2 Antelope"), but `SpawnPlatoon` always makes the template's full plan. With content mods, the only local antelope template may plan 7. For now the mod sizes the quest to the template, which takes away control over quest counts.

**Why the mod doesn't trim the group itself:**
- A platoon far from the player has no characters yet.
- `CharacterTable.Remove` bypasses the squad, `Platoon.Size` and the saved members.
- A platoon that unloads and loads again may be remade from its key, so the extras would come back.

**Wanted (either one; 1 preferred):**

1. **A size cap on `SpawnPlatoon`**, for example an optional `int maxMembers` (or separate caps for people and animals). The plan is cut before anything is made, leader first, in the plan's order, so `Platoon.Size`, the remake from the key, and saves all agree.

2. **`RemovePlatoonMember(World world, int platoon, CharacterId member)`** for a loaded platoon. It removes the member as a despawn does, and updates the squad, `Platoon.Size` and the saved members, so a later remake keeps the smaller group. It returns false where `DespawnPlatoon` would (a town's platoon, the player's, one being made, the parallel phase), and runs in serial phases only (Commit or SlowWorld).

A line in docs/modding.md on which phases may call it would help, like the one `DespawnPlatoon` has.
