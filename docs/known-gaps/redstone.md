# Redstone


**Status:** open. Dust, torches, repeaters, comparators, levers, buttons, pressure plates, the block of redstone and
the redstone lamp work (see [plugin content](../plugin-content.md#redstone)); the rest of vanilla's redstone doesn't.

- **Consumers.** Doors, trapdoors and fence gates, pistons, note blocks, powered, activator and detector rails, TNT,
  bells and copper bulbs don't react to power. Hoppers lock, droppers and dispensers fire, and crafters craft (see
  [item access](item-access-beyond-containers.md)).
- **Sources.** Daylight detectors, target blocks, trapped chests, tripwire hooks, detector rails,
  lightning rods and sculk sensors give no power (a lectern pulses as its page turns), though dust connects to them as to any signal source.
- **Analog outputs.** Comparators read 0 from jukeboxes, chiseled bookshelves, decorated pots,
  shelves, command blocks, sculk sensors, detector rails, creaking hearts, copper bulbs, copper golem statues and
  copper chests.
- **Timing.** Comparators hear of a container's items changing at the end of the level's block entity tick (through
  item access) or on its next one (players' clicks), and only near players, where vanilla's `setChanged` tells them
  at once; and entities press plates and arrows press buttons after they tick, where vanilla's do as they move.
  Cats sitting on a chest don't stop comparators reading it.
  Solid-block obstruction above a single chest or either double-chest half leaves the comparator's output stale
  until another notification, as captured in vanilla; removing it alone does too. Observers use the shape owner
  through both update engines. Captured player placement facing air or stone schedules no tick; command placement
  gets its initial pulse from state derivation before placement. Piston completion also derives the moved state:
  pushed and pulled observers pulse even while watching unchanged air, as captured in `redstone_scenarios.json`.
- **Game events.** Levers, buttons and pressure plates don't emit vanilla's `block_activate` and `block_deactivate`
  vibrations.
- **Experiments.** The `redstone_experiments` feature's wire evaluator and update orientations aren't modelled.

**Where to start:** each consumer is another `RedstoneComponent` in `Obsidian/WorldData/Redstone`, listed in
`VanillaRedstone`, reacting in `NeighborChanged` through `LevelBlockUpdates`' signal methods, like the lamp; sources
give their power from `GetSignal` and `GetDirectSignal`, like the lever. Analog outputs go in `AnalogOutputs`.
