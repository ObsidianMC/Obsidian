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

Obsidian asks a block's shape update more than once for one change: the vanilla-ordered neighbour update and the
notification pipeline both run it, and the support check before a queued break asks it for every face. That's harmless
for shape rules that only return a state, but live coral draws its death delay from the level's random there. Coral
draws only when no death is pending and it still has its support, which matches vanilla's one draw per change except
that vanilla also draws (and discards the delay) when a neighbour of a dry coral changes while its death is pending.
The `review_coral_dry_neighbour_changes_while_death_pending` case in `ticking_blocks.json` pins that missing second
draw explicitly. Six cases also pin the notification pipeline storing the third shape write with flags 3 instead
of vanilla's 2: `dripstone_grows_down_from_a_column`, `dripstone_grows_down_at_seven_long`,
`dripstone_tip_added_below_a_column`, `dripstone_tip_removed_from_a_column`,
`review_mixed_pointed_dripstone_body_sulfur_spike_down_tip_randomTick` and
`review_mixed_sulfur_spike_body_pointed_dripstone_down_tip_randomTick`. The replay compares all bits and fails if
these differences disappear, so fixing the shared pipeline requires removing the corresponding exceptions.

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
  `Obsidian/WorldData/BlockOwnerBehaviors.cs` or `RandomTickBehaviors.cs`, and the generator binds its blocks. Older
  code still uses other routes: most shape rules live in `ShapeUpdater`, and redstone components register their
  neighbour reactions, contact and ticks in `Obsidian/WorldData/Redstone`.
- Placement checks belong in `UseItemOnPacket`'s placement, before the place event: vanilla's `canSurvive` and
  `isUnobstructed`.
