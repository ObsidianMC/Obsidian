# Stepped and fallen-on contacts

**Status:** the phase 3 inside, fallen-on and stepped-on behaviours, posed player boxes, block speed and jump factors,
stuck-in-block and entity-dependent collision are implemented; broader movement and sculk parity remain open.

`block_contact_scenarios.json` contains the original 16 live RCON cases and 168 controlled native movement cases.
`VanillaBlockContact.ScenariosMatchVanilla` compares every recorded checkpoint from the start, including ordered
writes and flags, motion, health, accepted damage types, drops and effects. No case has a blanket skip, delayed
comparison start, or ignored unexpected write. Sound playback seeds are deliberately outside the capture;
categories, positions, volume, pitch and ordering are compared. The 51 `inside_*` cases also compare removal,
remaining fire ticks, item counts, vehicle damage, hopper slots, snowflake packet fields, honey particle events,
and the next entity random integer as well as the next level random integer.

The controlled moves exercise vanilla's collision and contact routines on a dedicated server. They are not a
capture of autonomous travel or AI. In particular, `fall_slime_item` supplies its movement and therefore does not
prove item gravity timing; `farmland_fall_pig` supplies its movement and does not make vanilla's NoAI mob fall by
itself. Obsidian's ordinary items still start falling a tick later and slide farther, armor stands do not move
themselves, and NoAI mobs fall where vanilla's float. These pre-existing differences were already the reason the
16 RCON cases used teleports with gravity off. Extending complete entity-tick captures is follow-up work, rather
than an exemption from any recorded contact field.

The contact pass checks inside effects at the final box rather than along the swept movement path. A fast entity
can still cross an inside-effect block without touching it. Fall accounting separately checks water along the
movement path and at impact; the `review_water_*` cases capture those resets and a dry control. The slime edge
cases and `review_bed_edge` capture support-based restitution, and `review_jump_boost_*` capture the effect's
safe-fall-distance modifier, its removal and the resulting fall damage. These cases have no pinned differences.

Player movement remains client driven; the controlled contact cases cover posed players, mobs, armor stands, items,
boats, minecarts and primed TNT, but not player packet handling or vanilla spawn-protection policy. The probe leaves
players' and boats' fall accounting to their outer packet/tick paths, and avoids dispatching a minecart's inside
effects twice (its native `move` already dispatches them).
Obsidian does not currently enforce vanilla's spawn protection when a player tramples farmland, breaks turtle eggs
or melts powder snow.
Surface lifts use the shared immediate lift and player correction path; ceiling clearance and independently lifted
riders retain the limitations described in [movement details](movement-and-swing-details.md).

Redstone ore lighting when stepped on is supplied by the merged PR #725.

## Poses, speed factors, stuck blocks and entity-dependent collision

A player's box follows their pose (standing, sneaking, swimming or crawling, gliding, spinning, sleeping), worked
out each tick as vanilla's `updatePlayerPose` does, and the contact pass, plates, tripwires and collision read it.
Mobs and items the server moves are slowed by soul sand and honey (speed factor), jump half as high from honey, and
are stuck in cobwebs, sweet berry bushes (which prick them from age 1, with the `sweet_berry_bush` damage type) and
powder snow. Powder snow holds up what can walk on it and catches long falls short of its top, without fall damage;
scaffolding holds up what stands on it unless it sneaks, and an unsupported bottom piece catches what's in it.
Slime damps the horizontal movement of what walks on it. These original pose and factor cases have no pins.

## Inside effects, dripleaf and shriekers

- **Cactus:** movement uses its captured collision, inset 1/16 on each horizontal side and 15/16 high; inside
  damage covers its cell, including the gap beside that collision. Damage uses `cactus`, with one point per hit.
  Items lose one of their five health per contact, including netherite items. Boats and minecarts use their
  existing vehicle damage and drop paths.
- **Campfires:** lit campfires deal one point of `campfire` damage, soul campfires two. They do not ignite ordinary
  mobs or damage items. Fire-immune entities, Fire Resistance and Frost Walker in the feet slot prevent damage.
  Armor stands instead ignite for five seconds and lose 0.15 health on subsequent contacts.
- **Honey:** side sliding resets fall distance, limits downward speed and damps a fast slide's horizontal speed.
  The level draws sound first and then event 53, which makes clients draw honey particles. Silent entities still
  consume both draws. Items slide without those effects; living entities, boats and primed TNT produce them.
- **Hoppers:** the inside hook shares the ordinary tick's push/pull implementation and cooldown, taking the touching
  item from the bowl's suction area. The tick still collects items above the hopper. The hook adds no second
  pickup implementation. `EntityContactSafety.HopperContactTriesBusyInventoryUnderTickAndTransferLocksWithoutLosingItems`
  forces inventory contention with explicit gates and checks retry and conservation across contact and ticking.
- **Dripleaf:** vanilla routes standing contact through `entityInside`, including sneaking entities and items.
  Unstable lasts 10 ticks, partial 10, full 100, followed by reset. Tilt sounds, vibrations and redstone reset are
  captured. Ticks use the existing persisted block scheduler.
- **Shriekers:** stepping directly triggers a shriek, including sneaking or standing still. Spectators and unowned
  items/mobs do not trigger it. Both placed and natural shriekers are covered at activation; a placed shrieker's
  90-tick reset is captured. The shriek uses a scheduled block tick and emits `shriek`; placed shriekers do not
  apply darkness. The old movement-event underfoot scan is removed.
- **Powder snow:** clears fire, melts without loot when a burning entity may interact (players bypass
  `mob_griefing`, including adventure players), and sends snowflakes with vanilla's per-axis speeds and
  randomization mode. Fire-immune entities do not melt it or play an extinguish sound. Extinguishing is completed
  after all inside hooks: two touched snow cells can both melt, after both particle draws. The extinguish sound
  is audible even for a silent entity.

The only new pinned difference is the player's fire holdoff: native player block effects set remaining fire to
`-20`, whereas Obsidian keeps zero. `VanillaBlockContact` pins exactly `-20 != 0` from tick 1 through the last
checkpoint of `inside_campfire_creative`, `inside_snow_player_griefing_off`, `inside_snow_adventure`,
`inside_shrieker_player`, `inside_shrieker_sneak` and `inside_shrieker_can_summon`. Each pin must still occur;
all other fields, including checkpoint zero, are compared normally.

What's still different:

- **Pose fit.** Whether a pose fits checks blocks only, not the entities (boats, shulkers) and world border vanilla's
  `noCollision` also counts, so a player against a boat stands where vanilla would crouch or crawl. Obsidian never
  gives a player who dies vanilla's dying pose (0.2 by 0.2).
- **A player's movement for blocks.** Vanilla reads a player's last client move (`getKnownMovement`) for berry bush
  pricks and powder snow's snowflake draw; Obsidian reads how far the player moved since their previous tick.
- **Powder snow fall sounds.** A long fall into it plays `entity.generic.*_fall` for everything, where vanilla plays a monster's or player's
  own fall sound, as Obsidian's other fall sounds also do.
- **Who the factors and collision apply to.** Speed factors and stuck multipliers apply where an entity moves through
  `EntityMovement.Displace` (mobs and items). Boats, minecarts, primed TNT, experience orbs and falling blocks move on
  their own paths and aren't slowed; vehicles and TNT do get the entity-dependent collision. Mobs' pathfinding still
  plans with the entity-free shapes (scaffolding solid, powder snow open). A camel's dash doesn't read the jump
  factor, and a mob's jump still leaves out Jump Boost.
- **Friction** is still read from the block 0.5 below the feet's centre, not the block holding the entity up, as the
  speed and jump factors now are.

Remaining phase 3 follow-ups include swept inside contacts, player movement/negative fire-holdoff parity,
spawn protection, honey-slide advancement criteria, and complete natural-shrieker response captures. Warning
levels and a shrieker's response state are not persisted; warning suppression, warden reply sounds, summon placement
and delayed darkness still need full lifecycle parity. The broader sculk work (persisted sensor timers
and more event sources), scaffolding support and water-started bubble columns remain separate work. Calibrated
sensor filtering and vibration frequency/timing corrections were merged in #739.
