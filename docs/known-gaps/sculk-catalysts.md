# Sculk catalysts

**Status:** open.

A sculk catalyst takes the experience of a mob whose floored death position is within eight blocks of the catalyst's
block position, blooms, and spreads the
charge every tick: veins, sculk, sensors and shriekers, with vanilla's draws, flags and charge particles
(`ticking_blocks.json`, see [scenario captures](../scenario-captures.md)). The mob then drops no experience, and the
player earns `kill_mob_near_sculk_catalyst` only when they are its last attacker, so a later wolf hit prevents credit.
Listeners in range are ordered by exact center distance. The grant is queued so a level tick never waits for a
player grant/save gate. The code is in `WorldData/SculkCatalysts.cs`, and the
hook in `Mob.KillAsync`.

What still differs:

- **Only mobs feed catalysts.** Vanilla's catalyst takes any living entity's death: a player's (their dropped
  experience, unless `keep_inventory`) and an armor stand's (no experience, but the catalyst still blooms). Obsidian
  offers only mobs' deaths.
- **The death is taken at once, not through the game event queue.** Vanilla's catalyst hears `entity_die` like other
  listeners; Obsidian queues game events for sculk sensors and wardens and delivers them later in the tick, so the
  catalyst has its own call from the death. Catalysts at exactly the same distance are offered the death in chunk
  order, not vanilla's listener section order.
- **Which mobs give how much experience** is each mob's `GetExperienceReward`, not captured with the catalyst. The
  capture hands the catalyst the experience directly.
- **Advancement capture scope:** the player-only and player-then-wolf cases exercise the native catalyst's last
  living attacker gate. They do not run the full vanilla damage pipeline or deliver advancement packets. Obsidian's
  replay applies the hits and checks the actual grant after draining background work.
- **Not captured:** the bloom's sound and particles (sent, but not compared), and the saved cursors' format (written as
  vanilla's codec writes them: defaults left out).
- **Lifting entities** when sculk replaces a block applies only to blocks whose collision is a flat floor lower than a
  full block (mud, soul sand); vanilla's `pushEntitiesUp` handles any shape.

**Where to start:** a player's death would call `AbstractLevel.OfferDeathToSculkCatalysts` from the player's death
path, with the player's dropped experience; capture it first with a `catalyst` case.
