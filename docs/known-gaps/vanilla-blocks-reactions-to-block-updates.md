# Vanilla blocks' reactions to block updates


**Status:** open. The mechanism is done; vanilla's blocks get their rules over time.

Block changes run vanilla's neighbour and shape updates, in vanilla's order, and plugin blocks react to them (see
[plugin content](../plugin-content.md#block-hooks)). Of vanilla's blocks, liquids flow, falling blocks fall and the basic
redstone components work (see [Redstone](redstone.md)). The existing shape rules (`ShapeUpdater`) now also run for
changes players, plugins and explosions make: fences, walls, panes and stairs connect; supported attachments and
plants pop off with tool-less loot and break effects; doors, beds and double plants react when their partner goes.
Removal is queued on the serial level tick so loot never runs under the tick lock. Shape destruction respects
`SuppressDrops` and `block_drops`. Water washout drops loot without the break effect; lava only fizzes.
Rails check their base and ascending end support and drop without a break effect. Doors, trapdoors, fence gates,
rails and TNT react to power (see [Redstone](redstone.md)). Scheduled ticks run for the vanilla classes that have a
tick behaviour (`TickBehaviors`: leaves, fire, farmland, sugar cane, cactus, bamboo, kelp, bubble columns, soul
sand, magma, composters, frogspawn, sulfur spikes) and for redstone components and falling blocks; the rest do
nothing when they come due.
Of vanilla's own blocks, pressure plates, tripwire, wooden buttons, detector rails and bubble columns react to
entities inside them; none has a stepped-on reaction, though magma, turtle eggs and sculk shriekers are handled by
checks of their own. The [block interactions plan](../block-interactions-plan.md) lists what is missing, phase by
phase. Some vanilla blocks' shape rules aren't modelled yet:
a bell keeps its `single_wall` or `double_wall` attachment when a wall beside it is added or removed, and doesn't drop
when it loses all support (vanilla's `BellBlock.updateShape`). Dead coral plants and fans don't break when their
support goes (vanilla's `BaseCoralPlantTypeBlock` and `BaseCoralWallFanBlock`).

Obsidian asks a block's shape update more than once for one change. The vanilla-ordered neighbour update in the fluid
level (`FluidLevel.ExecuteShapeUpdate`) runs it, then the notification pipeline (`BlockNotificationPipeline`) runs it
again, and the support check before a queued break asks it for every face. The pipeline drains as soon as a shape
update stores a block, in the middle of the fluid level's chain, so it can reach a neighbour before the fluid level
does, and it stores what it changes with every flag where vanilla's shape update passes on its own flags (2, not 3).
When the fluid level got there first, the pipeline asks again of the state that update left, so whatever the second
call does on its own (a random draw, a fluid tick) happens where vanilla never asked. Rules that only return a state
are unaffected. The pipeline shouldn't run vanilla blocks' shape updates for writes whose shape updates the fluid level
ran, or should run them after that chain with the shape update's flags. The captures that meet it pin each difference
exactly, compare every flag bit, and fail when a difference disappears, so fixing the pipeline means removing these
exceptions:

- `ticking_blocks.json`: live coral draws its death delay from the level's random in its shape update. Coral draws
  only when no death is pending and it still has its support, which matches vanilla's one draw per change except that
  vanilla also draws (and discards the delay) when a neighbour of a dry coral changes while its death is pending;
  `review_coral_dry_neighbour_changes_while_death_pending` pins that missing second draw. Six cases pin the third
  shape write stored with flags 3: `dripstone_grows_down_from_a_column`, `dripstone_grows_down_at_seven_long`,
  `dripstone_tip_added_below_a_column`, `dripstone_tip_removed_from_a_column`,
  `review_mixed_pointed_dripstone_body_sulfur_spike_down_tip_randomTick` and
  `review_mixed_sulfur_spike_body_pointed_dripstone_down_tip_randomTick`.
- `growing_plant_ticks.json` (`pipelineDifferences` in `VanillaGrowingPlantTicks`): a head that grows into a
  one-block gap below its own plant turns into its body in the pipeline first, stored with flags 3, in
  `kelp_grows_into_gap`, `cave_vines_grows_into_gap`, `weeping_vines_grows_into_gap` and
  `twisting_vines_grows_into_gap`. Kelp's water tick is scheduled again at a head the fluid level has just turned
  back from a body (`ShapeUpdater.ScheduleFluidTick` reading the state after the conversion), where vanilla schedules
  none, in `kelp_tip_broken_after_growing`, `kelp_plant_loses_its_head` and `kelp_grows_into_gap`.

Mud drying into clay above dripping pointed dripstone and a dirt path turning into dirt do not lift entities
standing on them onto the taller replacement block (vanilla's `Block.pushEntitiesUp`). Their bounded captures have
no entities standing there, so they cover the block transformation but not this missing displacement.

Blocks a feature or structure template places in a running level (`/place`, through `LiveFeatureLevel`) don't run
their on-place reactions, where vanilla's live placement does. Frogspawn placed that way never schedules its hatch,
so it never hatches, and turtle eggs placed on sand show no particles. Sniffer eggs placed that way are tracked
separately and do hatch. World generation runs no on-place reactions, as in vanilla.

Saved `block_ticks` and `fluid_ticks` lists are written in the order the ticks would run, where vanilla writes its
priority queue's internal heap order. Both reload the list in file order, so after a save and reload Obsidian keeps
running ticks due the same game tick with the same priority in their original order, while vanilla runs them in a
different one. Obsidian deliberately doesn't emulate Java's heap layout for this.

**Where to start:**

- New reactions go in as generated owners: a vanilla class's tick, random tick, shape update, entity contact or
  on-place reaction is a member named after the Mojang class that declares it, in
  `Obsidian/WorldData/BlockOwnerBehaviors.cs` or `RandomTickBehaviors.cs`, and the generator binds its blocks. Shape
  rules are all bound this way. A rule's `Reach` says whether the live engines run it first or only the shared pass
  (`ShapeUpdater`, which generation and placement use too) runs it; a live owner names in `SharedPassRule` what the
  shared pass runs for its blocks, if anything, and that rule must not schedule ticks it never scheduled before or
  draw from a random. Redstone components still register their neighbour reactions, contact and ticks in
  `Obsidian/WorldData/Redstone`.
- Placement checks belong in `UseItemOnPacket`'s placement, before the place event: vanilla's `canSurvive` and
  `isUnobstructed`.

## Shape rules not implemented yet

`Assets/block_update_shape_classes.json` names 108 vanilla classes that declare `updateShape`; 55 have a member in
`ShapeUpdateBehaviors`, covering 496 of its 854 blocks. These 53 have none yet, so their blocks keep their state on
a shape update. The shared pass still schedules the
fluid tick of every waterlogged state, so where a class's rule does only that, nothing is lost. Each needs a capture
before it is implemented. "Phase" refers to the [block interactions plan](../block-interactions-plan.md).

| Declaring class | Blocks | Today | Phase |
|---|---|---|---|
| `AmethystClusterBlock` | amethyst clusters and buds | Don't drop when their support goes | 5 (budding amethyst) |
| `BarrierBlock` | barrier | Fluid tick only | Shape-rule follow-up |
| `BaseCoralPlantTypeBlock` | dead coral plants and fans | Don't drop when their floor goes | 5 (coral) |
| `BaseCoralWallFanBlock` | dead coral wall fans | Don't drop when their wall goes | 5 (coral) |
| `BasePressurePlateBlock` | pressure plates | Don't drop when their floor goes | 3 |
| `BaseRailBlock` | rails | Redstone's rail reactions check support and shape | 4 |
| `BeehiveBlock` | beehive, bee nest | Nothing | 2 (batch A) |
| `BellBlock` | bell | Keeps its attachment and doesn't drop (see above) | 2 (batch E) |
| `BigDripleafBlock`, `BigDripleafStemBlock` | big dripleaf | Don't drop or reshape | 3 |
| `BrushableBlock`, `FallingBlock` | suspicious sand and gravel, anvils, dragon egg, sand | Fall through the live level's falling-block rule | 3 |
| `CakeBlock`, `CandleCakeBlock` | cakes | Don't drop when their floor goes | 2 (batch A) |
| `CampfireBlock` | campfires | No signal fire from hay below | 2 (batch B) |
| `CandleBlock` | candles | Don't drop when their floor goes | 2 (batch B) |
| `ChainBlock` | chains | Fluid tick only | Shape-rule follow-up |
| `ChorusFlowerBlock` | chorus flower | Doesn't break without support | 5 (chorus) |
| `ComparatorBlock`, `RepeaterBlock` | comparator, repeater | Redstone's rules lock repeaters; neither drops from a shape update | 4 |
| `ConcretePowderBlock` | concrete powder | Falls; doesn't harden beside water | 5 |
| `ConduitBlock` | conduit | Fluid tick only | 6 |
| `CopperChestBlock` | copper chests | Pair and unpair on the live level (`VanillaPlacement`); generation and placement's derivation leave them | 2 (batch E) |
| `CopperGolemStatueBlock` | copper golem statues | Fluid tick only | 4 |
| `CreakingHeartBlock` | creaking heart | Doesn't re-check its logs | 5 |
| `DecoratedPotBlock` | decorated pot | Fluid tick only | 2 (batch D) |
| `DriedGhastBlock` | dried ghast | Fluid tick only | 5 |
| `EnderChestBlock` | ender chest | Fluid tick only | 6 |
| `FlowerPotBlock` | flower pots | Don't drop when their floor goes | 2 (batch D) |
| `FrogspawnBlock` | frogspawn | Doesn't drop without water | 5 |
| `HeavyCoreBlock` | heavy core | Fluid tick only | Shape-rule follow-up |
| `HugeMushroomBlock` | mushroom blocks and stems | Faces don't join neighbours | 5 |
| `LightBlock` | light | Fluid tick only | Shape-rule follow-up |
| `LightningRodBlock` | lightning rods | Fluid tick only | 4 |
| `LiquidBlock` | water, lava | The fluid engine reacts instead | none |
| `MangrovePropaguleBlock` | mangrove propagule | Doesn't drop when its support goes | 5 |
| `MangroveRootsBlock` | mangrove roots | Fluid tick only | Shape-rule follow-up |
| `MultifaceBlock` | glow lichen, sculk vein, resin clump | Faces don't follow their support | 5 |
| `NetherPortalBlock` | nether portal | Doesn't break when its frame does | 6 |
| `PistonHeadBlock` | piston head | Piston rules move and remove it; no shape reaction | 4 |
| `PitcherCropBlock` | pitcher crop | Halves don't follow each other | 5 |
| `ScaffoldingBlock` | scaffolding | No distance re-check or fall | 3 |
| `SculkSensorBlock`, `SculkShriekerBlock` | sculk sensors, shrieker | Fluid tick only | 3 |
| `SeaPickleBlock` | sea pickle | Doesn't drop when its floor goes | 5 |
| `ShelfBlock` | shelves | No side connections | 2 (batch G) |
| `ShelfMushroomBlock` | shelf mushroom | Doesn't drop when its support goes | 5 |
| `SlabBlock`, `TrapDoorBlock`, `WaterloggedTransparentBlock` | slabs, trapdoors, copper grates | Fluid tick only | Shape-rule follow-up |
| `SoulFireBlock` | soul fire | Doesn't go out off a soul fire base | 5 (fire) |
| `TripWireBlock`, `TripWireHookBlock` | tripwire, hooks | Don't reconnect or drop on shape updates | 3 |

Three kinds of block have their class's rule but miss part of it:

- Torchflower crops have `VegetationBlock`'s rule, but Obsidian's survival check knows no torchflower crop, so one
  never breaks; vanilla's crops also need light, which no crop checks yet. The `more_blocks` probe can't record
  crops: their survival reads light, and its bounded level has no light engine. Phase 5.
- Mushrooms (`VegetationBlock`) read the live level's light; generation's shape pass reads 15, as it always has.
- Pointed dripstone, sulfur spikes and kelp only reshape in the shared pass: one that has lost its support during
  chunk completion keeps its state and gets no tick. Vanilla's completion (`LevelChunk.postProcessGeneration`) runs
  `updateFromNeighbourShapes` against the live level, so vanilla would schedule the fall, break or support check
  there. Obsidian keeps what it always did until a completion capture shows the ticks. Phase 5.
