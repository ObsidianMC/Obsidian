# Stepped and fallen-on contacts

**Status:** the phase 3 fallen-on and stepped-on owners, posed player boxes, block speed and jump factors,
stuck-in-block and entity-dependent collision are implemented; broader movement parity is open.

`block_contact_scenarios.json` contains the original 16 live RCON cases and 115 controlled native movement cases.
`VanillaBlockContact.ScenariosMatchVanilla` compares every recorded checkpoint from the start, including ordered
writes and flags, motion, health, accepted damage types, drops and effects. No case has a blanket skip, delayed
comparison start, or ignored unexpected write. Sound playback seeds are deliberately outside the capture;
categories, positions, volume, pitch and ordering are compared.

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

Player movement remains client driven; the controlled contact cases cover mobs, armor stands and items, not
player packet handling or vanilla spawn-protection policy.
Obsidian does not currently enforce vanilla's spawn protection when a player tramples farmland or turtle eggs.
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
Slime damps the horizontal movement of what walks on it. Every captured case matches with nothing pinned.

What's still different:

- **Pose fit.** Whether a pose fits checks blocks only, not the entities (boats, shulkers) and world border vanilla's
  `noCollision` also counts, so a player against a boat stands where vanilla would crouch or crawl. Obsidian never
  gives a player who dies vanilla's dying pose (0.2 by 0.2).
- **A player's movement for blocks.** Vanilla reads a player's last client move (`getKnownMovement`) for berry bush
  pricks and powder snow's snowflake draw; Obsidian reads how far the player moved since their previous tick.
- **Powder snow.** It doesn't put out burning entities or melt under them, and the snowflakes it draws for aren't
  sent. A long fall into it plays `entity.generic.*_fall` for everything, where vanilla plays a monster's or player's
  own fall sound, as Obsidian's other fall sounds also do.
- **Who the factors and collision apply to.** Speed factors and stuck multipliers apply where an entity moves through
  `EntityMovement.Displace` (mobs and items). Boats, minecarts, primed TNT, experience orbs and falling blocks move on
  their own paths and aren't slowed; vehicles and TNT do get the entity-dependent collision. Mobs' pathfinding still
  plans with the entity-free shapes (scaffolding solid, powder snow open). A camel's dash doesn't read the jump
  factor, and a mob's jump still leaves out Jump Boost.
- **Friction** is still read from the block 0.5 below the feet's centre, not the block holding the entity up, as the
  speed and jump factors now are.

The remaining separate phase 3 follow-ups are cactus (its real shape and destroying items), lit campfires, honey
sliding, the hopper's item pickup, big dripleaf tilting and sculk shriekers.
