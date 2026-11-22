# Radiant Quests: design plan

A Meitou code mod for Kenshi: repeatable guild quests that pay cats and rep, with a rep store and ranks.
All numbers are starting values. Every one of them will be a setting.

Status: draft for approval (2026-11-22). Nothing built yet.

---

## 1. Ground rules

- **Separate from Manual Combat.** It has its own folder and git repo. No shared code, and neither mod affects the other.
- **Built for compatibility.** It has no fixed lists. At start it reads every faction, war, animal, character and item from the game,
  so UWE, Kaizo and other mods work without being required.
- **It only adds.** It edits no game records and replaces no dialogue.
- **Its own save data.** It works in games already in progress (imported saves too). Removing it leaves the save working.
  Anything the mod placed in the world disappears.
- **Its own code and UI.** Nothing is copied from other mods; ideas only.
- **Won't run with Guild & Quests.** It is declared incompatible, so Meitou refuses to run the two together. It doesn't rely on Quest Log.
- **Override file.** An optional data file where you or other modders can add, block or tune targets, items, rewards and difficulty scores.
- **Deterministic and multiplayer-ready.** All world changes go through commands, and all mod state is hashed.

## 2. Guild clerks

- A clerk of the faction stands in the bar of each town of every faction that has towns with bars.
  Bandit gangs and animals get no clerk (banditry maybe later).
- Talking to the clerk opens the **guild window**: quest boards, the rep store, hand-ins.
- Later option: a dedicated guild building instead of the bar.

## 3. Rep and ranks

There are three separate tracks:

| | General quest | Faction quest |
|---|---|---|
| Cats | ✔ | ✔ |
| **Guild rep** (one shared guild, spent at every store) | ✔ | ✔ |
| **Faction points** (that faction's own rank, never spent) | ✖ | ✔ that faction only |
| **Faction relation** (the game's real relation) | ✖ | small: D +1, C +1, B +2, A +3, S +4 |

- **Ranks:** D, C, B, A, S. There is a guild rank (from guild rep) and a rank with each faction (from that faction's points).
- **Ranks come from lifetime rep.** Spending at the store never lowers it.
- **Thresholds:** C 100, B 300, A 700, S 1500. Each step costs more than the last.
- **Rank-up message** that says what the new rank unlocks.
- **What ranks unlock:** higher store sections, more store slots, a second reserve slot at S.
- **Rank titles:** later.

## 4. Quest boards

- The guild window has two tabs:
  - **General:** ordinary work, never war targets. Uses your guild rank. Hand in at any clerk.
  - **Faction:** work against that faction's enemies and wars, checked live. Uses your rank with that faction.
    Hand in only to that faction's clerks.
- Each tab has a board for each rank, D through S. Quests above your rank are shown but locked.
- **How many quests** (slightly random, refreshed daily):
  - **D:** always 2–4 (up to 5 while you're D or C).
  - **Your own rank:** almost always at least 1.
  - **Higher boards:** rarer. At S, for example, likely 1 S, 1–2 A, and more of the lower ranks.
- **No limit** on how many quests you can have at once.
- **Lower-rank quests pay less:**

| Ranks below yours | Cats | Rep |
|---|---|---|
| 0 | 100% | 100% |
| 1 | 90% | 90% |
| 2 | 60% | 50% |
| 3 | 35% | none |
| 4 | 15% | none |

## 5. Difficulty (worked out automatically)

- **Enemies and animals:** stats, weapons, armour, toughness and group size. A group of 4 starving bandits scores low; a pack of strong armoured fighters scores high.
- **Single characters:** the same measure for one person.
- **Items:** value, plus how easy they are to get (how many shops sell them, how often they drop). Something no shop sells that rarely drops counts as very rare.
- Every score sorts content into a rank. The override file can correct any score.

## 6. Quest types

**First version:**

| Type | What you do | Time limit |
|---|---|---|
| Hunt | Kill X of an animal | a few days, each kill adds time back |
| Cull | Kill X of a group (bandits, cannibals, fogmen...) | a few days, each kill adds time back |
| Clear | Wipe out a camp or nest | a few days |
| Single kill | Kill one named character | none |
| Capture | Bring one character in alive | a limit until captured, then none |
| Fetch | Hand in X of an item | by rarity; very rare: none |
| Trade | Swap X of an item for another of similar value | by rarity |
| Courier | Carry a package to a clerk or a person in another town | a few days |
| Scout | Reach a far-off place | a few days |
| Recover | Bring back a crate placed in a bandit camp | a few days |

**Later, maybe:** escort, rescue, theft (Faction tab only), defend (likely too easy).

- **Kills by any of your characters count.**
- **Commercial goods** (items shops sell) pay 110–140% of the item's normal price (not a single shop's price), plus rep.
  So turning in things you make beats selling them.
- **Cats by rank:** roughly D 500–1,500 up to S 20,000–50,000, scaled by difficulty and somewhat random.

## 7. Targets

- **Use what already exists first:** camps, nests, roaming squads, characters.
- **If nothing fits, spawn chaff:** only squad types from the region's normal roaming lists, and never a squad with a unique in it.
- **Single kill:**
  - The target doesn't need a bounty. If it has one, the bounty pays too, on top of the quest.
  - **Faction quests** only pick existing characters, never spawned ones.
  - **General quests** may spawn a mod-made named target of fitting difficulty when no existing one fits.
- **Capture hand-in:** bring the captive to the clerk. To avoid overfilled cages, the guild takes the captive away
  (removed from the world). Putting them in a town cage when one is free is an option.
- **Target killed or captured by someone else:** the quest is called off with no penalty.
- **Other mods' quests on the same target are fine.** Each mod pays its own reward.

## 8. Very rare items (A/S fetch quests)

- **Placed in the world** when no normal way to get one is likely.
- **What:** only items that can already appear in the world (rare loot, rare traders).
  - Never one-of-a-kind or fixed-count gear.
  - Only S quests can place the very top grades, and rarely.
- **Where:**
  - A visible container in a ruin or camp, or carried by the leader of a strong roaming squad.
  - Never an obscure container.
  - The spot's danger must match the quest's rank.
- **Hint:** general, region only ("somewhere in the Ashlands").
  - Option: buy a sharper hint for guild rep.
  - Once one of your characters is within range (about 200 m), the item gets a map marker. If an NPC carries it, the marker follows them.
- **Ending the quest:**
  - **Item not picked up:** it is deleted, and the normal fail or abandon cost applies.
  - **Item picked up, then the quest is abandoned:** a harsh penalty. A big lifetime rep loss scaled to the item's value,
    and for faction quests a relation hit too.
  - **No limit on selling:** placed items can be sold like any other.

## 9. Failing and abandoning

- **Time runs out:** you lose half the quest's rep.
- **Abandon:** you lose a quarter.
- **Lifetime rep:** the loss comes off lifetime rep, so a rank can drop, but never below 0.
  Quests you already took stay yours after a rank drop.
- **Faction quests** also lose faction points. Failing one at B and up also costs relation: B −1, A −2, S −3.
- **Faction turns hostile while you hold its quests:** they are abandoned with no penalty.

## 10. Rep store

- Every faction has one at its clerks, and all of them take guild rep.
- **Stock:**
  - Random, not limited to that faction's gear, with a chance at very rare items. It rerolls every 3 days.
  - A higher rank with that faction gives better rare odds at its store.
  - Higher guild ranks open higher store sections.
- **No pity system.**
- **Reserve:** a 10% deposit holds one item through the next reroll only. The deposit counts toward the price when you buy.
  If you don't buy, you lose only the deposit.
- **Mystery roll:** pay rep for a random item. Its price is set from its odds.

## 11. Quest log and tracker

- **GUILD tab:** a tab of its own in the Overview window. It has:
  - groups (General, each faction);
  - a quest list and a description;
  - Track and Show on map buttons;
  - search and a "near me" filter.
- **Tracker window:** a toggleable on-screen window with kill counts and time left.
- **Map markers:** quest targets and places, a person's last-seen spot, rare-item regions as a rough area,
  and the exact item marker once you're close. Pins in one spot merge into one.
  - **Hunt and kill quests** also mark where groups of the target were last known, which matters for rare factions
    (e.g. Cult of Goblin) you'd otherwise never find.
- **Alerts:** a tracked target comes close or dies, and quest done, failed or called off.
- **Carrying line:** a captive you carry shows "wanted by the guild".

## 12. Settings

- Every number in this plan, on the game's Mod settings page.
- **Debug options** (extra logging and similar) are off by default, for performance.

## 13. Build order

Each step is tested in-game before the next.

1. Clerk, guild window, General hunt and cull quests, guild rep and ranks.
2. Faction tab, faction points and relations, fetch and trade quests.
3. Rep store: rerolls, reserve deposit, mystery roll.
4. GUILD tab, tracker window, map markers.
5. Single kill, capture, clear, courier, scout, recover.
6. Very rare items: placement, hints, closer marker, penalties.
7. Settings page, debug toggles, override file.

## 14. Still open

- **How the clerk is added to each bar** (spawning a faction member there), worked out in step 1.
- **Exact numbers:** board counts, cats and rep amounts, mystery roll price. Tuned in play.
- **Banditry factions:** later.
