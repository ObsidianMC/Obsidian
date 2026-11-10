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
- **What the piglin's spawn can't match.** Every draw vanilla takes from the level's random is Obsidian's too, in
  vanilla's order, and captured: the yaw, the follow range bonus, handedness, loot pickup, being a baby, both chicken
  jockey chances (a new jockey chicken's variant, sound variant, follow range and handedness), breaking doors, the
  spear or sword, the weapon's enchantment chance and the enchantments themselves, and the Halloween pumpkin. What
  vanilla draws from the mob's own random can't match: the attribute bonuses of `handleAttributes`, the spawn ambient
  sound's pitch, and the head turn a new jockey chicken keeps from its constructor (`Math.random()`). Existing-chicken
  captures cover a living, unoccupied chicken, a dying chicken, one in a boat, one carrying a passenger, and choosing
  the first eligible chicken rather than the nearest. A successful first jockey roll with no eligible chicken does
  not try the second roll; a separate case captures a failed first roll followed by a new jockey chicken.
- **Halloween.** Pumpkins go on piglins on October 31st of the server's local date, read through the level's
  `TimeProvider`, as vanilla's `SpecialDates` reads the JVM's. Vanilla's probe can't set the JVM's clock, so the
  Halloween cases move vanilla's `SpecialDates.HALLOWEEN` constant by as many days as the case's date is from the real
  one; vanilla's own comparison and the draws after it run unchanged. The Halloween helmet's drop chance is zero.
  Equipment drop chances are not recorded by this fixture, so the Halloween captures do not check that value.
- **Bats, found alongside.** 26.3's `Bat.checkBatSpawnRules` has no Halloween season any more (Obsidian's has been
  removed), but it also asks for a position below the world surface heightmap, where Obsidian asks for below sea level,
  and a floor in `#bats_spawnable_on`, which Obsidian doesn't check.

Portal cooldowns are saved: every entity writes and reads vanilla's `PortalCooldown`, so a piglin reloaded beside its
portal still waits.

**Where to start:** the floor rule is `MobSpawner.IsSpawnableFloor` (`Obsidian/WorldData/MobSpawner.cs`); give it
vanilla's per-block `isValidSpawn` (dumping each state's answer for each mob from the jar would do) and remove the
pinned cases from `VanillaEyeblossomGhastPortalTicks`. Bucket use into waterloggable blocks belongs in
`UseItemOnPacket`, through the fluid level's `PlaceLiquid`; the dried ghast's sound then goes with it.
