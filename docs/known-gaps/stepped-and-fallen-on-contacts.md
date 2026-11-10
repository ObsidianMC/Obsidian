# Stepped and fallen-on contacts

**Status:** the first phase 3 fallen-on and stepped-on owners are implemented; broader movement parity is open.

`block_contact_scenarios.json` contains the original 16 live RCON cases and 81 controlled native movement cases.
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

Redstone ore lighting when stepped on is supplied by the merged PR #725. The remaining separate phase 3
follow-ups are big dripleaf tilting, sculk shriekers, pose-dependent boxes, block speed factors and
entity-dependent collision. Slime's horizontal step damping belongs to the speed-factor work; this change
implements its fall cancellation and vertical restitution for mobs and items.
