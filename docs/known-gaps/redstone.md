# Redstone


**Status:** open. Most of vanilla's redstone works (see [plugin content](../plugin-content.md#redstone)), and the
[block interactions plan](../block-interactions-plan.md) lists every component's state in its appendix, section 7.
What follows is what still differs.

- **Consumers.** Bells don't react to power. Doors, trapdoors and fence gates, note blocks, powered and activator
  rails, TNT, copper bulbs, pistons, hoppers, droppers, dispensers and crafters do; copper bulbs make no
  sound, and dispensers still drop buckets, bottles, honeycomb and a few other items they'd use (see
  [dispenser behaviours](dispenser-behaviours.md)).
- **Sources.** Trapped chests give no power, though dust connects to them as to any signal source, and a jukebox
  gives none while it plays, where vanilla's gives 15 (`JukeboxBlock.isSignalSource`). Observers, daylight
  detectors, target blocks (hit by arrows, spectral arrows and tridents only), tripwire hooks, detector rails, sculk
  sensors, lecterns and lightning rods (struck by [lightning](lightning.md)) do.
- **Analog outputs.** Comparators read 0 from shelves, command blocks, sculk sensors, creaking hearts, copper bulbs
  and copper golem statues.
- **Timing.** Comparators hear of a container's items changing at the end of the level's block entity tick (through
  item access) or on its next one (players' clicks), and only near players, where vanilla's `setChanged` tells them
  at once. Entities press plates, trip wires and press wooden buttons (arrows) straight after their own tick, as
  vanilla's do, but only from where they end up: vanilla also finds the blocks an entity passed through in that tick.
  Which entities touch blocks and which leave plates and tripwires alone (vexes touch them, bats don't trigger them,
  an entity its own tick removed touches nothing) follows vanilla's source; `block_contact_scenarios.json` doesn't
  capture those kinds yet.
  Cats sitting on a chest don't stop comparators reading it.
  Solid-block obstruction above a single chest or either double-chest half leaves the comparator's output stale
  until another notification, as captured in vanilla; removing it alone does too. Observers use the shape owner,
  run once per block change by the fluid level's vanilla-ordered updater. Captured player placement facing air or
  stone schedules no tick; `/setblock` and
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
- **Piston completion.** A cut-short source piston should place air in vanilla; Obsidian instead places its moved
  state. Completion also omits vanilla's final neighbour notification to the placed block, so an isolated rail
  pushed over air stays unsupported until something else updates it. Dead coral plants and fans can be moved and
  derive to air in vanilla, but Obsidian's shape rules have no case for them. When a completed move derives to air,
  destruction replaces the block and notifies neighbours before broadcasting the break effect and spawning its
  drop; vanilla broadcasts the effect and spawns the drop before replacement and notifications.
- **Game events.** Levers, buttons and pressure plates don't emit vanilla's `block_activate` and `block_deactivate`
  vibrations.
- **Experiments.** The `redstone_experiments` feature's wire evaluator and update orientations aren't modelled.

**Where to start:** each consumer is another `RedstoneComponent` in `Obsidian/WorldData/Redstone`, listed in
`VanillaRedstone`, reacting in `NeighborChanged` through `LevelBlockUpdates`' signal methods, like the lamp; sources
give their power from `GetSignal` and `GetDirectSignal`, like the lever. Analog outputs go in `AnalogOutputs`.
