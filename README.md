# Radiant Quests for Meitou (v0.1.0)

Repeatable guild quests for Kenshi on Meitou (the open-source Kenshi engine). Take work from a faction's bartender, do it, and get paid in cats
and guild rep. Guild rep raises your guild rank from D to S, which opens harder, better-paid work.

Built from your load order: the factions, animals and people of UWE, Kaizo or any other mod take part with no patch needed. The full design is
in DESIGN.md.

## What works in 0.1.0 (build step 1)

- **Adventurers' Guild:** right click the bartender of any faction that runs bars and choose *Adventurers' Guild*.
  One of your characters must stand near the bartender to take or hand in a quest.
- **Boards by rank:** D, C, B, A and S. They change every day.
  - D always has 2-4 quests (up to 5 while you're D or C).
  - Your own rank almost always has one.
  - Higher ranks are rarer and locked until you reach them.
- **Quests:** *Hunt* (kill X of an animal) and *Kill* (X of a bandit-type faction that nearly every guild faction is hostile to).
  - Difficulty comes from the game data: stats, armour and strength, compared across all creatures and factions in your load order.
  - A kill counts when any of your characters (or your animals) makes it.
- **Time:** 3 days for a hunt or kill quest, and each kill gives 12 hours back.
- **Pay:** cats and guild rep. Quests below your rank pay less (and no rep from 3 ranks down).
- **Ranks:** C at 100 lifetime rep, B at 300, A at 700, S at 1500.
- **Fail or abandon:** running out of time costs half the quest's rep, and abandoning a quarter. This can lower your rank, never below 0.
- **On screen:** a quest tracker at the top right, and guild messages at the top.
- **Settings:** every number above under Settings > Mods > Radiant Quests, plus a Debug log switch.
- **Works paused:** the guild window's buttons work while the game is paused.

Coming next: Faction quests and faction points, fetch and trade quests, the rep store, the quest log tab and map markers, single kills,
captures, couriers, rare items.

## Install

Run `build.bat` (it builds and copies the mod into `Kenshi\mods\RadiantQuests`), then tick RadiantQuests in the Meitou launcher.
Needs Meitou 0.22.2 or newer. It does not run together with Guild & Quests.

## Removing it

Untick it. Saves keep working; your guild rank and quests are dropped with the mod's save data.

## License

GPLv3 or later (see LICENSE).
