# Radiant Quests for Meitou (v0.9.3)

Repeatable guild quests for Kenshi on Meitou (the open-source Kenshi engine). Take work from a faction's bartender, do it, and get paid in cats
and guild rep. Guild rep raises your guild rank from E to SS, which opens harder, better-paid work.

Built from your load order: the factions, animals and people of UWE, Kaizo or any other mod take part with no patch needed. The full design is
in DESIGN.md.

## What works in 0.8.16

- **Adventurers' Guild:** right click the bartender of any town owned by a guild faction and choose *Adventurers' Guild*, whoever runs
  the bar (in many towns the Trade Ninjas do). Faction work is for the town's owner, checked live (0.6.2).
  One of your characters must stand near the bartender to take or hand in a quest.
- **Boards by rank:** E, D, C, B, A, S and SS. They change every day.
  - E and D always have 2-4 quests (up to 5 while you're E or D).
  - Your own rank almost always has one.
  - Higher ranks are rarer and locked until you reach them.
- **Quests:** *Hunt* (kill X of an animal) and *Kill* (X of a bandit-type faction that nearly every guild faction is hostile to).
  - Difficulty comes from the game data: stats, armour and strength, compared across all creatures and factions in your load order.
  - A kill counts when any of your characters (or your animals) makes it.
- **Time:** 3 days for a hunt or kill quest, and each kill gives 12 hours back.
- **Pay:** cats and guild rep. Quests below your rank pay less (and no rep from 3 ranks down).
- **Ranks (0.5.0):** seven, from E (very easy) to SS. D at 50 lifetime rep, C 150, B 400, A 900, S 1800, SS 3500.
- **Fail or abandon:** running out of time costs half the quest's rep, and abandoning a quarter. This can lower your rank, never below 0.
- **Target groups (0.2.0):** a hunt or kill quest marks the nearest groups of its target (within a set range of the town) on the world map.
  If none are in range when you take it, one ordinary group of them is spawned in that range (never a unique character).
- **Variant factions (0.2.0):** factions that roam on another faction's spawn list count as that faction, so Bandit Variety's Dust Raiders,
  Dust Reapers and Dust Lords count for a Dust Bandits quest.
- **Defeats, not kills (0.4.0):** quests count people your side *defeats*: knocked out, killed, carried off or locked up, each person
  once. No execution mod needed. "Kill 4 Dust Bandits" is now "Defeat 4 Dust Bandits".
- **Group quests (0.4.0):** "Wipe out a group of 12 Dust Bandits (9 needed)": one real group near the town, marked on the map and ranked by
  its real size against the typical squads of your load order. Done when 75% of it is defeated (a setting), so a member you can't find
  never blocks it. If others break the group before your side fights it, the quest is called off with no penalty; once you've fought it,
  its end finishes the quest.
- **One difficulty scale (0.5.0):** every quest is scored on one fixed scale, set from fights known in play: goats and starving bandits
  are E, one naked beak thing or two swamp lurkers C, one leviathan or one great beak thing S. A fighter's strength comes from the game
  data: damage (combat skill, animal strength, natural weapons, weapon quality) weighs more than toughness (race health, HP, armour).
  Later quest types (fetch, courier...) will score on the same scale.
- **Difficulty (0.4.1):** every kill and hunt quest is ranked by the fight it means: the target's fighter strength times how many you
  face (the ones you must defeat plus half of the rest of their usual group), placed on the scale of your load order's typical squads
  (or animal packs). A fighter's strength counts squared, so quality weighs more than numbers: "Defeat 3" of strong fighters can be an A
  while weak bandits need many for the same rank, and a herd of goats stays easy. Animals' strength now includes their "animal strength"
  (how hard they hit) and health (0.4.2).
- **Enough targets (0.4.2):** when you take a hunt or defeat quest, the mod counts how many of the target are within range; with too few
  (an animal that roams alone), it spawns more groups, or else lowers the count to what is there.
- **Starting a war (0.4.0):** a quest against a faction that isn't hostile to you yet says so ("makes X hostile to you") and pays 50% more
  cats (a setting). Rank only measures how hard the fight is.
- **Faction quests (0.3.0):** the window's second tab holds work for the clerk's own faction: kill quests against its enemies right now
  (its wars and the outlaws, from live relations). They pay cats, guild rep, points with that faction (your D to S rank on its board) and a
  little real relation (D/C +1, B +2, A +3, S +4). Hand them in to that faction only. Failing one at B or above costs relation
  (B -1, A -2, S -3). If the faction turns hostile to you, its quests are called off with no penalty.
- **Spawns only where they live (0.6.0):** a quest spawns a group only in a region where that group naturally spawns (the game's region
  spawn lists, nests and factions' home regions), which may be further than the target range but never past the "Furthest spawn"
  setting. A target that has no such region is only offered when enough of it already lives near the town.
- **Wild patrols and shop animals (0.6.1):** a faction's own groups that spawn wild in some region (patrols listed under the regions, like
  Cannibal Hunters or White Eyebrow Clan) count as its targets and can be spawned there. Animals that only ever appear as a shop's stock or
  in a new-game start are never hunt targets.
- **Override file (0.6.0):** `overrides.json` in the mod folder (and any `overrides\*.json`, so other mods can ship patches) can block a
  target, mark an animal tame or wild, mark a faction as bandits, add or remove guild factions, or scale a target's strength. Ids your
  load order doesn't have are skipped. Updating the mod never overwrites your edited overrides.json.
- **Self-checks (0.6.0):** at start the mod notes anything that may be off in your load order (with Debug log on, each note goes to
  log.txt). `test\check-catalog.ps1` runs the same check offline on the base game and on your mod list.
- **No tame animals (0.5.1):** an animal race a friendly faction keeps (pack beasts, ranch herds, pets) is no hunt target unless it also
  spawns wild; a hunt never spawns a friendly faction's animals, and killing one never counts.
- **Hunts are wild animals only (0.3.0):** animals that only come with people (guard and police dogs, pack animals) are no hunt targets,
  and a hunt spawns only an animal group.
- **Cleanup (0.3.0):** a group spawned for a quest goes back to being an ordinary group when the quest ends, so the game despawns it as usual.
- **Feature switches:** faction quests, hunt quests, kill quests, target spawning and map markers can each be turned off in the settings.
- **On screen:** a quest tracker at the top right, and guild messages at the top.
- **Settings:** every number above under Settings > Mods > Radiant Quests, plus a Debug log switch.
- **Works paused:** the guild window's buttons work while the game is paused.

Coming next: fetch and trade quests, the rep store, the quest log tab and map markers, single kills,
captures, couriers, rare items.

## Install

Run `build.bat` (it builds and copies the mod into `Kenshi\mods\RadiantQuests`), then tick RadiantQuests in the Meitou launcher.
Needs Meitou 0.22.2 or newer. It does not run together with Guild & Quests.

## Removing it

Untick it. Saves keep working; your guild rank and quests are dropped with the mod's save data.

## License

GPLv3 or later (see LICENSE).
