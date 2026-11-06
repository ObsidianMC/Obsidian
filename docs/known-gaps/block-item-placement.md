# Block item placement


**Status:** every vanilla block item places as vanilla does in the captured contexts; a few item and block rules
outside them are still missing.

`VanillaBlockPlacement` replays `block_placement.json`: every vanilla block item used on a floor, a wall (high, low
and from the side), a ceiling (also sneaking) and into a water source, through `UseItemOnPacket`. Placement goes by
the vanilla class that declares `getStateForPlacement` (`PlacementBehaviors`, implemented in `BlockPlacements`), so
subclasses such as weathering copper doors place like their parent. Blocks are kept only where vanilla's
`canSurvive` holds them (`PlacementSurvival`).

What isn't covered yet:

- **Lily pads and frogspawn.** Vanilla's `PlaceOnWaterBlockItem` ignores use-on and places from `use`, by tracing the
  player's look to a water source. Obsidian has no such use path, so it still places them through use-on, held by
  vanilla's `canSurvive` (on water, in air). They go where vanilla's survival allows, which the capture doesn't
  contradict, but not from a look at the water's surface.
- **Clicking into blocks that stack.** Vanilla's `canBeReplaced` depends on the item: a candle, sea pickle, turtle egg,
  flower bed or leaf litter used on one of its kind adds to it, a slab completes a slab, and snow layers stack.
  `UseItemOnPacket` only handles slabs this way; the block placements already add to a block they replace.
- **Extending scaffolding.** Vanilla's `ScaffoldingBlockItem` places scaffolding along a scaffolding tower or bridge
  that's clicked. Obsidian places it where it was clicked, and refuses it where nothing would hold it.
- **Random ages.** Kelp and weeping, twisting and cave vines take an age from the level's random, which Obsidian draws
  from its own; the replay compares them without their age.
- **Placement side effects.** Coral blocks placed away from water don't die later (vanilla schedules it), a mossy
  carpet's random topper isn't placed, and bamboo placed onto bamboo still uses Obsidian's older rule.

**Where to start:** add a context to `block_placement.json`'s `contexts` (a clicked candle, a scaffolding tower,
water below air) and capture again (`CaptureFixtures=placement`); the replay picks up every item in it.
