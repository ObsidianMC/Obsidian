# Lightning

**Status:** mostly done. Lightning bolts (`LightningBolt`) strike as vanilla's do, from `/summon`, from a
thunderstorm's chunk tick and from plugins: their life and flashes, fire around the strike on normal and hard
difficulty (and on every flash), lightning rods powered for eight ticks with sparks, copper cleared around the strike,
the `lightning_strike` game event, five lightning damage and a tick of fire to living things, the conversions (creeper,
pig, villager, mooshroom, copper golem, turtle), items destroyed, and the `lightning_strike` and `channeled_lightning`
advancement triggers. Natural strikes find lightning rods and living things under the sky, and spawn skeleton traps.
All of it is replayed from vanilla by `lightning_bolts.json` (see [scenario captures](../scenario-captures.md)).
What's left:

- **Redstone lamps' write flags.** The redstone engine stores the lamps a rod powers with `SkipOnPlace` (512) as well
  as vanilla's flag 2, as it does for every clients-only change it makes. Nothing reacts differently, but the stored
  flags differ; the replay pins the rod cases' lamp writes.
- **The chunks a storm strikes, and their order.** Vanilla's `tickThunder` runs for each chunk in natural spawning's
  range, shuffled with the level's random, every tick, mobs spawning or not. Obsidian's `MobSpawner` shuffles them with
  a random of its own, so the level random's sequence parts from vanilla's wherever more than one chunk spawns; within
  a chunk, the strike's draws (the chance, the living thing struck, the trap's chance) and its column (the random tick
  position sequence) are vanilla's, in vanilla's order. Its chunks are those within eight chunks of a player who isn't
  a spectator, where vanilla's are those in entity-ticking range of one.
- **Lightning rods out of reach of the search.** Vanilla finds the nearest rod through its points of interest, which
  include rods in saved chunks that aren't loaded; Obsidian reads the palettes of the loaded chunks within 128 blocks.
  Two rods equally near the strike are told apart by vanilla's point of interest order, which Obsidian doesn't keep;
  Obsidian takes the first it reads. Several living things that could take a strike are told apart by when they were
  added, where vanilla's order is its entity sections'.
- **The bottom of an empty column.** A storm striking a column with nothing in it hits the bottom of the world in
  Obsidian, whose light reads open sky there; vanilla's doesn't strike. The replay pins this case.
- **Turtle loot.** A turtle killed by lightning drops a bowl in vanilla, by a `damage_source_properties` condition of
  its loot table. Obsidian's turtle drops its seagrass itself, from its own random, and loot tables can't read damage
  sources yet (damage type tags have no entries). The replay's turtle case runs with `mob_drops` off.
- **Entities lightning doesn't reach yet.** Vanilla's `Entity.thunderHit` sets every entity a tick on fire and hurts
  it: boats and minecarts take the damage, and a cushion is destroyed and drops itself. Obsidian strikes living things
  and items; other entities (vehicles, cushions, projectiles) are untouched. Players start with their fire count at 0,
  where vanilla's start at -20 (`getFireImmuneTicks`), so a struck player shows a tick of fire vanilla's doesn't.
- **What conversions keep.** Vanilla's lightning conversions don't keep the old mob's equipment, and the witch a
  villager becomes runs its `finalizeSpawn` (handedness and other draws from its own random). Obsidian's conversion keeps
  the equipment (a pig's saddle, say) and doesn't finalize the witch.
- **Mobs without AI fall.** A mob with `NoAI` falls under gravity in Obsidian; vanilla's stays where it is. The
  capture's mobs stand on floors so that this doesn't show.
- **Mob hurt sounds.** Obsidian plays a mob's hurt and death sounds following it, where vanilla plays them at its
  position; the replay compares the followed sounds at the mob's position.
- **Advancement predicates.** The lightning triggers match 26.3's `entity_type`, `distance` and
  `type_specific/lightning` entity predicates, inside `entity_properties`, `all_of`, `any_of` and `inverted`
  conditions. Any other predicate matches nothing, so such a criterion is never granted.
- **Draws made with mobs' own randoms.** Mobs' randoms aren't seeded in the capture, so what mobs decide with them is
  left out of it: their ambient sounds, a witch drinking potions, and whether the zombified piglin a pig becomes
  holds a golden spear (1 in 20) or sword.

## Where to start

`Obsidian/Entities/LightningBolt.cs` is the bolt, `Obsidian/WorldData/LightningRods.cs` the rods and the copper,
`Obsidian/WorldData/AbstractLevel.Lightning.cs` the natural strikes and `Obsidian/Progress/LightningProgress.cs` the
triggers. Add a case to `lightning_bolts.json` and capture it before changing a rule.
