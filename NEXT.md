# Next work (agreed 2026-11-22, not built yet)

The user tested v0.1.0 by hand: accepting, kill counting and hand-in rewards work. Two fixes are approved ("Yes, both"):

## 1. Faction families
- Bandit Variety splits Dust Bandits into Dust Raiders / Dust Reapers / Dust Lords. A "Kill X Dust Bandits" quest should count all of them.
- In Catalog.Build: for each faction F, the SQUAD_TEMPLATEs in F.Squads whose template.Faction is another faction G put G in F's family.
  Log the families at start to check this really groups Bandit Variety's factions. If it doesn't, try the biome spawn lists.
- The variants then get no cull quests of their own. GuildSystem.Count matches any family member.

## 2. Targets in range, spawn if none, map markers
- The user's rule: only target groups within a set, fairly large range count. If there are none in range when the quest is accepted, the mod spawns one group.
- APIs, read in Meitou 00d6246 (nightly 20261122):
  - `world.Platoons.All` gives `Platoon` with `Faction`, `TemplateId`, `Position` (Vector2), `State`, `Dead` and `ModTag`.
  - `PopulationSystem.SpawnPlatoon(world, faction, templateId, homeSite, around, minRadius, maxRadius, money)` returns the id or -1.
    It works in serial phases only. Get the system with `ctx.Find<PopulationSystem>()`.
  - A mod's spawn is kept until it dies (`ModSpawned`). Tag it with `ModTag = "rq:<questId>"` to find it after a load.
- Spawn only templates from the faction's (or family's) roaming list, non-unique. For hunts, use a template that holds that race.
- Markers: `UiRegistration.AddMapMarkers`. The view carries each quest's target points, refreshed every few seconds.

Build scripts already point at nightly 20261122. The reference copy C:\KenshiRef\MeitouClient is checked out at 00d6246.
