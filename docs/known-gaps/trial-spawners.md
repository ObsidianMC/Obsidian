# Trial spawners and vaults

**Status:** partial. Trial spawners and vaults are typed block entities that load and save vanilla 26.3's data
exactly, and a trial spawner runs vanilla's trial: it detects players, spawns its waves from its normal config, tracks
its mobs, ejects rewards and cools down. Checked against vanilla by `trial_spawners.json` (see
[scenario captures](../scenario-captures.md)), whose 19 trial cases match every tick without pins, and whose
trial chamber's 51 trial spawners and vaults load and save back unchanged. Obsidian keeps a config naming a
`trial_spawner` registry entry it doesn't have (perhaps a data pack's), and saves it again, where vanilla drops it; the
spawner uses the default config meanwhile, as vanilla does. Vaults connect nearby eligible players, cycle their
display loot, accept the configured key once per player and eject rewards, including ominous vaults. The separate
`vaults.json` capture checks their block changes, saved and client data, world-dependent loot, full dropped stacks
(including components), timings, item motion, sounds and level events every tick. Vanilla's client draws the
connection, activation and idle particles from those updates; the
server sends no particle packets. Ominous trials convert Bad Omen during a spawner's player scan (18,000 ticks per
level, Trial Omen amplifier zero), switch configs with level event 3020, discard the old mobs while dropping their
preserved equipment, and create ominous item spawners above eligible players or mobs. Spawn-data equipment tables
equip their loot and apply their slot drop chances. `ominous_trials.json` checks these changes, the item spawners'
saved and client item data, timers, sounds, release events and random draws against 26.3. Eight additional probes
follow real releases through impact, including potion reloads, ordinary arrows carrying potion contents and the
player wind charge's separate burst. Loaded chunks outside simulation range pause item-spawner timers. What's left:

## Left for later trial work

- **Spawn eggs on trial spawners.** Vanilla's `setEntityId` replaces both configs' spawn potentials and resets the
  trial; a spawn egg on a trial spawner does nothing special yet.

## Spawning

- **Bare spawn data's spawn setup for other mobs.** Spawn data that only names its mob gets vanilla's spawn setup
  from the level's random for every mob the trial chambers spawn that way: skeletons, strays, bogged and parched
  (`AbstractSkeleton.finalizeSpawn`: armour, a bow, enchantments, picking up loot, a Halloween pumpkin), wither
  skeletons, zombies and husks (`Zombie.finalizeSpawn`: picking up loot, a baby and its chicken jockey, breaking doors,
  armour, a weapon, enchantments, a pumpkin; a husk draws for loot again), spiders (a skeleton jockey, and an effect on
  hard), silverfish and breezes (`Mob.finalizeSpawn` only) and cave spiders (none), as do zombified piglins. The
  chambers' baby zombies and slimes aren't bare and get no setup. Drowned, zombie villagers and other mobs a data
  pack's config could name still set themselves up from their own random, so the level random goes its own way after
  one of them spawns. The captures follow the draws through 43 zombies and husks and 36 spiders, but none of them was
  a baby with a chicken jockey, a spider jockey or a spider with an effect, so those branches are checked against
  vanilla's code only.
- **Spawn data passengers.** A spawn data entity's passengers aren't spawned with it.
- **Other mobs' spawn rules.** A monster may spawn anywhere it fits in sight but in peaceful, as every monster's rule
  says for a trial spawner. Other mobs (a data pack's config could spawn animals) keep the floor rule spawners use
  (`MobSpawner.FitsVanillaPlacement`), not their own predicates.
- **The spinning mob.** A spawner leaves its inactive state once its spawn data names a mob this server knows, where
  vanilla loads the mob first.
- **Data pack configs.** `normal_config` and `ominous_config` read `trial_spawner` registry entries from vanilla's own
  data only. An id the server doesn't know leaves all four config fields at their defaults, as vanilla does, and is
  saved again as it was.
- **Sight lines through unloaded chunks.** Vanilla's sight line (a visual clip) loads a chunk it crosses that isn't
  loaded, and looks through it. Obsidian can't wait for a chunk under the tick's locks, so a line through one counts as
  blocked: no mob spawns, and no player is detected, through it until something else loads it.

## Clients

- **Before the first tick.** Clients are sent `next_mob_spawns_at` while a spawner is active. Its state is read on its
  tick, so a chunk sent before an active spawner's first tick leaves it out until the next update.

## Found along the way

- **Lingering potion clouds.** Released lingering potions shatter with vanilla's particle and sound level events,
  without direct damage, fire or splash effects. They still don't create area-effect clouds; cloud lifetime, radius
  and effect application need their own implementation and capture.
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
