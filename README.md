# Radiant Quests for Meitou (v0.11.2)

Repeatable guild work for Kenshi on **Meitou** (the open-source Kenshi engine). Talk to a bartender, choose *Adventurers' Guild*, take a
job, do it, and get paid in cats and guild rep. Rep raises your guild rank from E to SS, and higher ranks open harder, better-paid work.

Everything is built from **your own load order**: the factions, animals, camps, items and towns of any mod you run take part, with no
patch needed.

## Requirements

- **Meitou 0.22.2 or newer** (tested on 0.23.0). This is a Meitou code mod; it does nothing in the original Kenshi.exe.
- Tick **RadiantQuests** in the Meitou launcher's mod list.
- It does not run together with Guild & Quests.

## Getting started

- Right click the bartender in a town owned by one of the guild factions (most towns) and choose **Adventurers' Guild**, or pick
  *I'm looking for guild work* while talking to them. One of your characters must stand near the bartender to take or hand in work.
- Each rank, E to SS, has its own board of work. Boards change every day; *New quests* replaces the untaken ones for a little rep.
- From guild rank C, the town's own faction gives work too (its tab at the clerk): against its enemies, for its own points and
  better relations.
- Press **G** for the guild journal: every quest you hold, what you have heard about it, and the time left.

## Quests

| Kind | What you do |
|---|---|
| **Hunt** | Kill or knock out a number of an animal. |
| **Defeat** | Defeat a number of a bandit-type faction, or one lone outlaw. Knockouts count, not only kills. |
| **Wipe out a group** | Break one particular group. If others beat most of it, you're paid for your share. |
| **Clear a camp** | Defeat everyone at a hostile camp or nest. |
| **Supply** | Bring goods the town lacks: trade goods or animal parts. Buy them, loot them or make them. |
| **Gear** | Bring a weapon or armour of a kind, worth at least a set amount (grade and maker count). A better piece earns a tip. |
| **Find** | Track down something hard to get. |
| **Courier** | Carry a sealed guild parcel to another town. Ambushes may wait on the road; if they knock you out they take the parcel, and you have an hour to win it back. |
| **Recover** | A stolen item lies in a hostile camp. Get it back any way you like. |

Difficulty comes from the game data (stats, armour, how many you face), on one scale from Very Easy to Very Hard within each rank, and
the pay follows it. Item work pays from the goods' real shop price. Quests are handed in where you took them.

## Finding your targets

Fight quests start with a **rumour** told from where you are: *"The Dust Bandits were last seen roaming north-east of here, a short
walk out."* Their targets show on the map only once you get close. If you lose them:

- the clerk sells better and fresher rumours, again and again;
- townsfolk may know something (right click > *Ask about guild work*, or ask while talking to them). Not everyone has heard anything,
  and what they say can be spot on or badly off;
- spotting the targets yourself updates the rumour.

Couriers, recover quests and camps are marked as usual. The setting *Fight markers always* marks fight targets all the time.

## Handing in goods

Press *Hand in* at the clerk: your inventory opens, and the goods that count get a small yellow circle. Click them to pick them, then
press *Hand in*. You can hand in part of a big order now and the rest later; once anything is delivered the quest can't fail, and
giving it up pays for what you brought. Worn gear is never taken.

## Settings

Esc > Settings > Mods > RadiantQuests. The main groups:

- **Features:** each quest kind on or off, item quests, spawning targets when none are near, map markers, *Fight markers always*.
- **Ranks:** the rep each rank needs, and when factions start giving work.
- **Rewards and penalties:** cats and rep multipliers, the cost of failing or abandoning.
- **Time limits, Targets and Boards:** quest time, the quest area, how many quests a board shows, out-of-region quests, the mix of
  fights and item work.
- **Look:** the quest tracker and window backgrounds. Windows can be moved and resized; A- and A+ change the text size.

`overrides.json` in the mod folder can add or remove individual targets, items and spawn groups (for odd modded entries).

## Adding and removing

Safe to add to a running game. To remove it, untick it: saves keep working, your guild rank and quests go with the mod's save data, and
quest items (the guild parcel, a recovered item) become ordinary items.

## Building from source

Run `build.bat` (it builds against the installed Meitou and copies the mod into `Kenshi\mods\RadiantQuests`). Developer notes are in
HANDOVER.txt and DESIGN.md.

## License

GPLv3 or later (see LICENSE).
