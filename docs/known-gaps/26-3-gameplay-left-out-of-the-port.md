# 26.3 gameplay left out of the port

**Status:** open, deliberately out of scope for the 26.3 port.

The port's goal is that players on a 26.3 client can join and that worlds generate as vanilla 26.3's do. The new
gameplay 26.3 added is left to other contributors. Players can join and explore without it; the affected blocks and
mobs exist and sync to clients, but some can't be used and some biomes lack mobs they would have in vanilla.

- **Blocks.** Golden dandelion interactions and age locking; straw bed placement and its own sleep rules; shelf
  storage, quick swapping and connections for every wood. See [26.3 blocks](26-3-blocks.md).
- **Saplings.** Poplar saplings don't grow. See [saplings that 26.3 added](saplings-that-26-3-added.md).
- **New entities.** The Sulfur Cube (archetypes, absorbing blocks, shearing, splitting, buckets, dispensers) and the
  cushion (seating) have metadata but no behavior. Natural spawning skips Sulfur Cubes, so Sulfur Caves have none.
  See [entity behavior beyond 26.3 metadata](entity-behavior-beyond-26-3-metadata.md).
- **Mob changes.** Age locking, 26.3's baby hitboxes, persistent mobs idling when no player is near, and spectators
  using portals.
- **Bubble columns.** Water above soul sand or magma doesn't start a column while playing. See
  [bubble columns started by water](bubble-columns-started-by-water.md).
- **Hand-written tables.** Tables that mirror vanilla data weren't re-checked against 26.3: `BlockSoundPlayback`,
  `FluidRules`, `ShapeSupport`, `StructureBlockShapes`, placement rules, `EnvironmentAttributes`,
  `JukeboxSongsRegistry` and `BannerPatternsRegistry`. Generate them where possible.
- **Gameplay scenario captures.** These rules have no 26.3 vanilla capture yet:
  - bubble columns: creation, bucket placement, source replacement, direction and breathing;
  - crops: growth draws, farmland hydration and tilling, and nether wart;
  - fire: support, facing, spread and burning out;
  - plants: stack collapse, bamboo placement and leaf distance;
  - combat: damage and bypass formulas, wolf armor, golem repair and freezing;
  - mob navigation: water, banks, terraces and doors, and species path costs and floating;
  - bundles: capacity and nesting, and fishing enchantments;
  - portal frames and exit placement, boat buoyancy and minecart motion.

**Where to start:** capture each rule from vanilla first, as [scenario captures](../scenario-captures.md) describes,
and replay it with one test per capture (`AGENTS.md`'s testing rule). Phase 6 of the
[porting plan](../porting-26.3-plan.md) has the original breakdown.
