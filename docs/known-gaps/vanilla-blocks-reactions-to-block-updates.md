# Vanilla blocks' reactions to block updates


**Status:** open. The mechanism is done; vanilla's blocks get their rules over time.

Block changes run vanilla's neighbour and shape updates, in vanilla's order, and plugin blocks react to them (see
[plugin content](../plugin-content.md#block-hooks)). Of vanilla's blocks, liquids flow, falling blocks fall and the basic
redstone components work (see [Redstone](redstone.md)). The existing shape rules (`ShapeUpdater`) now also run for
changes players, plugins and explosions make: fences, walls, panes and stairs connect; supported attachments and
plants pop off with tool-less loot and break effects; doors, beds and double plants react when their partner goes.
Removal is queued on the serial level tick so loot never runs under the tick lock. Shape destruction respects
`SuppressDrops` and `block_drops`. Water washout drops loot without the break effect; lava only fizzes.
Rails check their base and ascending end support and drop without a break effect. Vanilla's `neighborChanged`
reactions other than redstone components' (rails, doors and trapdoors opening when powered, TNT) aren't modelled,
and scheduled ticks of vanilla blocks other than falling blocks and redstone components (an observer's, from a
vanilla save) do nothing when they come due. Of vanilla's own blocks, only pressure plates and wooden buttons react
to entities inside them, and none to entities standing on them; and placing a block other than a redstone component
doesn't check that it can survive there, nor any block that no entity is in the way.

Saved `block_ticks` and `fluid_ticks` lists are written in the order the ticks would run, where vanilla writes its
priority queue's internal heap order. Both reload the list in file order, so after a save and reload Obsidian keeps
running ticks due the same game tick with the same priority in their original order, while vanilla runs them in a
different one. Obsidian deliberately doesn't emulate Java's heap layout for this.

**Where to start:**

- Vanilla blocks' `neighborChanged` goes in `LevelBlockUpdates.NeighborChanged`, and their ticks in
  `LevelBlockUpdates.RunScheduledTick`, next to falling blocks'.
- Placement checks belong in `UseItemOnPacket`'s placement, before the place event: vanilla's `canSurvive` and
  `isUnobstructed`.
