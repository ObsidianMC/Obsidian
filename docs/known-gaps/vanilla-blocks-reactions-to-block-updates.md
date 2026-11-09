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
tick behaviour (`TickBehaviors`: leaves, fire, farmland, sugar cane, cactus, bamboo, kelp, bubble columns,
composters, sulfur spikes) and for redstone components and falling blocks; the rest do nothing when they come due.
Of vanilla's own blocks, pressure plates, tripwire, wooden buttons, detector rails and bubble columns react to
entities inside them; none has a stepped-on reaction, though magma, turtle eggs and sculk shriekers are handled by
checks of their own. The [block interactions plan](../block-interactions-plan.md) lists what is missing, phase by
phase. Some vanilla blocks' shape rules aren't modelled yet:
a bell keeps its `single_wall` or `double_wall` attachment when a wall beside it is added or removed, and doesn't drop
when it loses all support (vanilla's `BellBlock.updateShape`).

Saved `block_ticks` and `fluid_ticks` lists are written in the order the ticks would run, where vanilla writes its
priority queue's internal heap order. Both reload the list in file order, so after a save and reload Obsidian keeps
running ticks due the same game tick with the same priority in their original order, while vanilla runs them in a
different one. Obsidian deliberately doesn't emulate Java's heap layout for this.

**Where to start:**

- A vanilla class's tick, random tick, shape update, entity contact or on-place reaction is a member named after
  the Mojang class that declares it, in `Obsidian/WorldData/BlockOwnerBehaviors.cs` or `RandomTickBehaviors.cs`; the
  generator binds its blocks. Redstone reactions to neighbours are registered in `Obsidian/WorldData/Redstone`.
- Placement checks belong in `UseItemOnPacket`'s placement, before the place event: vanilla's `canSurvive` and
  `isUnobstructed`.
