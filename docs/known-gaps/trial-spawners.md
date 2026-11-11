# Trial spawners and vaults

**Status:** partial. Trial spawners and vaults are typed block entities that load and save vanilla 26.3's data
exactly, and a trial spawner runs vanilla's trial: it detects players, spawns its waves from its normal config, tracks
its mobs, ejects rewards and cools down. Checked against vanilla by `trial_spawners.json` (see
[scenario captures](../scenario-captures.md)), whose 13 trial cases match every tick without pins, and whose
trial chamber's 51 trial spawners and vaults load and save back unchanged. Obsidian keeps a config naming a
`trial_spawner` registry entry it doesn't have (perhaps a data pack's), and saves it again, where vanilla drops it; the
spawner uses the default config meanwhile, as vanilla does. What's left:

## Left for later trial work

- **Vaults do nothing yet.** `VaultBlockEntity` keeps and saves `config`, `shared_data` and `server_data` and sends
  clients the shared data, but doesn't tick: no players connect, no display item cycles, no key opens it and nothing
  is ejected. Its data types hold what that needs (items as stacks, players as ordered sets).
- **Ominous trials.** A spawner never becomes ominous: Bad Omen isn't turned into Trial Omen, a Trial Omen doesn't
  make the spawner ominous (level event 3020, mobs removed with their preserved equipment), and no ominous item
  spawners drop `items_to_drop_when_ominous`. A spawner whose block is already ominous does use its ominous config, its
  ominous detection event (3019) and the ominous flame, and goes back to normal at the end of its cooldown. Vanilla
  picks an ominous item's target from the spawner's player and mob sets by their order, which `UuidSet` already gives.
- **Spawn eggs on trial spawners.** Vanilla's `setEntityId` replaces both configs' spawn potentials and resets the
  trial; a spawn egg on a trial spawner does nothing special yet.

## Spawning

- **Bare spawn data's spawn setup for other mobs.** Spawn data that only names its mob (as the trial chambers' do)
  gets vanilla's spawn setup from the level's random for skeletons, strays, bogged and parched
  (`AbstractSkeleton.finalizeSpawn`: armour, a bow, enchantments, picking up loot, a Halloween pumpkin), silverfish and
  breezes (`Mob.finalizeSpawn` only) and cave spiders (none). Zombies, husks, spiders, slimes and other mobs still set
  themselves up from their own random, so the level random goes its own way after one of them spawns.
- **Spawn data equipment and passengers.** A spawn data's `equipment` loot table (the ominous ranged configs') isn't
  rolled onto the mob, and its entity's passengers aren't spawned with it.
- **Other mobs' spawn rules.** A monster may spawn anywhere it fits in sight but in peaceful, as every monster's rule
  says for a trial spawner. Other mobs (a data pack's config could spawn animals) keep the floor rule spawners use
  (`MobSpawner.FitsVanillaPlacement`), not their own predicates.
- **The spinning mob.** A spawner leaves its inactive state once its spawn data names a mob this server knows, where
  vanilla loads the mob first.
- **Data pack configs.** `normal_config` and `ominous_config` read `trial_spawner` registry entries from vanilla's own
  data only. An id the server doesn't know leaves all four config fields at their defaults, as vanilla does, and is
  saved again as it was.
- **Players found in the same scan.** They join the spawner's set in the order of the level's player dictionary rather
  than vanilla's player list, which only matters when two of them share a bucket of vanilla's hash set.

## Clients

- **Before the first tick.** Clients are sent `next_mob_spawns_at` while a spawner is active. Its state is read on its
  tick, so a chunk sent before an active spawner's first tick leaves it out until the next update.

## Found along the way

- **NoAI mobs fall.** Vanilla's NoAI mobs don't move, even under gravity; Obsidian's still travel. The capture gives
  its mobs `NoGravity` as well, which changes nothing in vanilla.
- **Tiny moves.** Vanilla's `Entity.move` leaves an entity in place when collision cut its move to under 1e-7 squared,
  which is what stops a resting item creeping on. Only item entities apply it so far.
- **Items in lava.** Items move in water and lava as vanilla's do (currents, a 0.99 or 0.95 slowing across and a
  small lift while deeper than 0.1, then the usual drag), and splash and forget their fall in water. The capture covers
  water only: an item in lava burns, and nothing checks its lava movement against vanilla yet.
- **Falls in water for other entities.** Items build no fall while they touch water, as vanilla's `checkFallDamage`
  says for every entity; living entities forget theirs as they move instead, and other entities still build it.
- **Mob death sounds** are sent as sounds following the mob, where vanilla sends them at its position; the replay
  compares where they're heard, to the eighth of a block vanilla's packet carries.
