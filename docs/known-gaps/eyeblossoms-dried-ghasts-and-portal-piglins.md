# Eyeblossoms, dried ghasts and portal piglins

**Status:** partial. Eyeblossoms (planted and potted) turn with the time of day, dried ghasts rehydrate, dry and
become ghastlings, and nether portals spawn zombified piglins, all checked against vanilla by
`eyeblossom_ghast_portal_ticks.json` (see [scenario captures](../scenario-captures.md)). What's left:

- **Bees and eyeblossoms.** Vanilla's `EyeblossomBlock.entityInside` poisons a bee inside an eyeblossom it's drawn to
  (25 ticks, outside peaceful, unless it's poisoned already). That's entity contact, which phase 3 of the
  [block interactions plan](../block-interactions-plan.md) covers; nothing poisons the bee yet.
- **A `"default"` eyeblossom attribute.** `eyeblossom_open` is a tri-state. A plugin's or data pack's dimension type
  or biome that sets it to `"default"` keeps the value the layers before it gave, where vanilla goes back to the
  eyeblossom's own state. Vanilla's own types, biomes and timelines never set it so.
- **Buckets and dried ghasts.** Players' buckets don't pour water into waterloggable blocks or take it out, so a
  dried ghast can't be waterlogged or dried by hand. Captured from vanilla when this was written: pouring water into
  a dry one writes `waterlogged=true` with `UPDATE_ALL`, schedules a water tick 5 ticks later and plays
  `block.dried_ghast.place_in_water`; pouring into a wet one does nothing; taking the water writes `waterlogged=false`
  with `UPDATE_ALL` and schedules nothing. Placing one by hand doesn't play its own `block.dried_ghast.place` (or
  `place_in_water`) sound on top of the block's placement sound, as vanilla's `setPlacedBy` does.
- **The floor a portal's piglin stands on.** Obsidian asks its mob spawner's floor rule (`MobSpawner.IsSpawnableFloor`:
  solid and not a liquid), where vanilla asks the floor's `isValidSpawn`: a sturdy top face and light under 14 by
  default, with blocks' own rules (glass and barriers never; ice, leaves and magma only for some mobs). So a portal
  over glowstone, glass or a bottom slab spawns piglins in Obsidian and not in vanilla; the replay pins those three
  cases. Natural and spawner spawns share the same rule and the same difference.
- **Players close enough to spawn.** `MobSpawner.IsCloseEnoughForSpawning` takes a player 128 blocks across from a
  chunk's centre as close enough; vanilla's `playerIsCloseEnoughForSpawning` asks for less than 128, and only of
  chunks within a player's simulation distance.
- **What the piglin's spawn leaves out.** The draws are vanilla's, but a baby that vanilla would sit on a chicken (a
  5% chance of a 5% chance) rides nothing, a weapon whose enchantment chance comes up isn't enchanted (it can only on
  hard in a world old or inhabited enough), and the attribute bonuses vanilla draws from the mob's own random come from
  Obsidian's. The spawn ambient sound's pitch is drawn from the mob's own random, as vanilla's is, so it can't match.

**Where to start:** the floor rule is `MobSpawner.IsSpawnableFloor` (`Obsidian/WorldData/MobSpawner.cs`); give it
vanilla's per-block `isValidSpawn` (dumping each state's answer for each mob from the jar would do) and remove the
pinned cases from `VanillaEyeblossomGhastPortalTicks`. Bucket use into waterloggable blocks belongs in
`UseItemOnPacket`, through the fluid level's `PlaceLiquid`; the dried ghast's sound then goes with it.
