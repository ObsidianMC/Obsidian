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
  through both update engines. Captured player placement facing air or stone schedules no tick; `/setblock` and
  `/fill` give a placed observer the shape reaction from the side it watches, its part of vanilla's state derivation
  (they derive nothing else, see [26.3 commands](26-3-commands.md)). A piston's finished move derives the moved
  state from its new neighbours: pushed and pulled observers pulse even while watching unchanged air, fences
  reconnect, and a block that can't stay where it lands (a carpet pushed over air) breaks with its drops, as
  captured in `redstone_scenarios.json`.
- **Pistons and water.** A pushed or pulled waterlogged block keeps `waterlogged=true`, where vanilla's finished
  move dries it (captured once: a waterlogged slab arrives dry and no water flows; not kept as a scenario, since it
  fails here). The fluid tick vanilla schedules while deriving the moved state isn't scheduled either: the level
  reads the block still at the destination, the moving piston, and finds no fluid. That only shows once the block
  is dried properly, and in a move cut short (vanilla's `finalTick`, from the jar), which keeps the water, so vanilla
  lets it flow and Obsidian leaves it still until a neighbour updates it.
- **Game events.** Levers, buttons and pressure plates don't emit vanilla's `block_activate` and `block_deactivate`
  vibrations.
- **Experiments.** The `redstone_experiments` feature's wire evaluator and update orientations aren't modelled.

**Where to start:** each consumer is another `RedstoneComponent` in `Obsidian/WorldData/Redstone`, listed in
`VanillaRedstone`, reacting in `NeighborChanged` through `LevelBlockUpdates`' signal methods, like the lamp; sources
give their power from `GetSignal` and `GetDirectSignal`, like the lever. Analog outputs go in `AnalogOutputs`.
