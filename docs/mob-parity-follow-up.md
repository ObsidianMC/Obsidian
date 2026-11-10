# Mob parity follow-up — 2026-11-10

This pass addresses contained defects in shared mob rules and individual implementations. It does **not** establish full vanilla parity for any mob. Builds, tests and in-game checks were not run, as requested. The factory inventory and the existing [parity inventory](mob-parity.md) were used to triage the remaining scope; individual species were not exhaustively compared against vanilla.

The project targets Java 26.3, but the available cached reference server and mappings are from 1.21.11. The changes below use that reference, the existing entity/tag assets and current code contracts. Exact 26.3 equivalence remains unverified. Generated assets were not modified or regenerated.

## Changes made

| Area | Correction |
| --- | --- |
| Fall damage | The shared default now consults `fall_damage_immune`. This covers missing exemptions for cats, ocelots, iron/copper golems and Breezes, and replaces the special-case chicken check. Existing explicit species overrides remain. |
| Undead effects | Poison and regeneration immunity now follows `ignores_poison_and_regen`, rather than an incomplete hard-coded poison-only list. Spider poison immunity and Wither Skeleton/Parched exceptions remain. |
| Animals | The `Animal` base supplies the adult 1–3 XP reward and zero baby XP, fixing missing defaults for horse-family mobs and parrots. Species overrides remain. Breeding XP now honors `mob_drops`. |
| Chicken jockeys | Chickens marked as jockeys can distance-despawn and award 10 XP, as in the cached vanilla Chicken implementation. Ordinary chickens keep their existing behavior. |
| Horses | Coat and markings are chosen independently: each parent's coat has a 4/9 chance, with 1/9 random; each parent's markings have a 2/5 chance, with 1/5 random. Crossbreeding still produces a mule through the existing path. |
| Slimes and Magma Cubes | Split children preserve the parent's mob flags (including NoAI) and custom-name visibility, alongside the existing name and persistence transfer. |
| Dolphins | Add the missing adult XP reward. Breathing checks actual fluid occupancy at the eyes, including waterlogged blocks; NoAI dolphins replenish air rather than drowning. |

Regression tests were added for landing immunity, undead effects, slime split state and independent horse appearance inheritance. They have not been executed.

File-scoped whitespace lint was run. Pre-existing warnings remain in `Living.cs`, `Mob.AI.cs` and `Mob.Environment.cs`; unrelated formatting was left intact. No build/type-check or gameplay verification was performed.

The earlier Wither fire-immunity assignment was redundant: `EntityGenerator` already generates `IsFireImmune` from `entities.json`. That assignment and its redundant test were removed; the other earlier boss corrections remain.

## Significant work deferred

These are groups that cannot be brought to full parity with a small local correction. Shared requirements apply even where a species has a correction above. Entries combine source inspection with the existing inventory and are work scopes, not claims that every listed path was freshly audited.

| Mobs | Remaining work and why it is significant |
| --- | --- |
| Villager, Zombie Villager, Wandering Trader, Trader Llama | Complete schedules, POIs, inventories, reputation/trades, cure/infection transfer, trader lifecycle and caravan coordination cross AI, world state and persistence. |
| Pillager, Vindicator, Evoker, Ravager, Witch, Illusioner, Vex | Raid/patrol membership and recovery, coordinated spells/healing, riders, equipment progression and presentation require coordinated AI/world changes. |
| Piglin, Piglin Brute, Hoglin, Zoglin, Zombified Piglin | Complete anger, inventory/bartering, group hunting/retreat, home and conversion behavior needs a fuller activity/memory model. |
| Allay, Armadillo, Axolotl, Bee, Fox, Frog, Tadpole, Goat, Panda, Sniffer | Full species activities, sensors, interruptions, navigation and saved state; each has reduced goal/timer implementations rather than complete vanilla behavior. Bee crop growth already exists. |
| Cat, Ocelot, Wolf, Parrot | Complete pet/trust/owner behavior, defense, sitting/perching/shoulders, gifts, music and presentation. Cat owner-follow teleport and sleep relaxation, and newer parrot imitation entries, already exist. |
| Horse, Donkey, Mule, Llama, Skeleton Horse, Zombie Horse | Functional mount inventories/equipment interfaces, attribute inheritance, feeding/taming details, trap/jockey lifecycle and client/server riding agreement. Appearance inheritance is corrected, not the whole horse breeding model. |
| Camel, Camel Husk, Happy Ghast, Nautilus, Zombie Nautilus, Strider | Species steering/dashes, seat and dismount placement, rider-directed combat/spawn groups, special interactions and saved mount state. Zombie Nautilus hostility cannot be fully corrected independently of its rider lifecycle. |
| Pig, Cow, Chicken, Sheep, Mooshroom, Rabbit, Polar Bear, Turtle | Shared breeding/spawn/loot parity plus species behavior: recipe-driven sheep colors, lightning transformation, rabbit navigation, bear defense, turtle home/migration and egg laying. Small XP/jockey corrections do not complete these systems. |
| Cod, Salmon, Tropical Fish, Pufferfish, Squid, Glow Squid, Dolphin | Schooling/group state, aquatic navigation, bucket/component transfer, inflation/fleeing and dolphin treasure/item-play behavior. Presentation and spawning also need validation. |
| Guardian, Elder Guardian | Exact beam/thorns context, targeting and swimming, fatigue scheduling/presentation, home behavior and conditional loot. |
| Iron Golem, Snow Golem, Copper Golem | Village reputation/patrol/flower behavior, exact environmental snow placement, and copper sorting/container/statue lifecycle. Fall immunity is corrected where missing. |
| Zombie, Husk, Drowned, Skeleton, Stray, Bogged, Parched, Wither Skeleton | Spawn equipment/enchantments, conversions, reinforcements, jockey/spear combat and complete ranged/underwater targeting behavior. Shared immunity fixes apply. |
| Creeper | Complete potion clouds, charged-head drops, damage attribution, fuse/avoidance interaction and lightning behavior. |
| Spider, Cave Spider | Climbing/pathing, light-dependent targeting, jockey/group initialization and collision/web behavior. |
| Slime, Magma Cube | Hopping/contact collision, split timing/placement, spawn probabilities and size-specific loot still need parity work. State inheritance alone is fixed. |
| Enderman, Endermite, Silverfish | Exact staring/anger/teleport/placement, pearl-origin interactions, infestation wake-up and typed damage/block-break context. Enderman/Silverfish griefing checks already exist. |
| Blaze, Ghast, Phantom, Bat, Breeze | Species flight, navigation/attack trajectories, spawn context and presentation. Breeze deflection and wind interactions span entity and block systems. |
| Shulker | Attachment/teleport validity, projectile homing and reproduction interact with collision and entity lifecycle. |
| Creaking | Heart spawning/home lifecycle, observation tests, resin and damage/deactivation behavior span blocks, world ticking and mob AI. |
| Sulfur Cube | The class currently exposes size/fuse/bucket metadata without enabling mob AI. A behavioral implementation and lifecycle/bucket handling are substantial new work, not a local parity patch. |
| Giant | Vanilla does not supply attack AI. No invented combat behavior was added; shared physics, damage, spawning and presentation remain the parity scope. |

The Warden, Wither and Ender Dragon were the preceding task and were not re-audited here.

## Shared blockers and corrected inventory assumptions

- Complete collision/pathfinding, crowd pushing, spawning caps/context, damage attribution, loot predicates, saved memories and presentation remain cross-cutting work. These prevent an honest “all other mobs have parity” claim.
- A general leash implementation exists in `Mob.Leash.cs`; arbitrary passenger relationships exist in `Entity.Riding.cs`. The old inventory's claims that leashes are missing and passengers are player-only are stale. Species integration and exact runtime equivalence still need verification.
- Entity loot-table migration already covers a substantial subset. Remaining migration is gated by unsupported predicates and special death handling; do not replace those handlers with table calls until their tables and predicates are supported. See [plugin entity gaps](known-gaps/plugin-entities.md).
- Full 26.3 coverage needs refreshed reference artifacts and targeted comparisons. This pass did not regenerate assets, run a server, change Git state or introduce dependencies.
