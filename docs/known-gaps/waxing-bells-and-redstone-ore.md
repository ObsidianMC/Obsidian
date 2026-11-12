# Waxing, bells and redstone ore

**Status:** partial. Honeycomb waxes every waxable copper block, a use rings a bell, and a use lights redstone ore,
all checked against vanilla by `waxing_bells_redstone_ore.json` (see [scenario captures](../scenario-captures.md)).
Axes already scraped and removed wax through 26.3's block transformers. What's left:

- **Doors and trapdoors opened by hand notify their neighbours.** Honeycomb on a copper door or trapdoor without
  sneaking opens it, as in vanilla. Obsidian writes the opened door with `UPDATE_ALL` (3), and its upper half with 2;
  vanilla writes 10 for both (`UPDATE_CLIENTS | UPDATE_IMMEDIATE`, the upper half through its shape update), and 2 for
  a trapdoor, where Obsidian writes 3. So a door or trapdoor opened by hand sends neighbour updates that vanilla
  doesn't, such as to a piston beside it. This is the existing door use (`UseBehaviors.OpenableUse`), not waxing; the
  replay pins both cases (`wax_copper_door_opens_without_sneaking`, `wax_copper_trapdoor_opens_without_sneaking`)
  and fails once they match.
- **Villagers don't hear bells.** Vanilla's bell sets `heard_bell_time` in the brain of every living entity within 32
  blocks when its ring event runs, which sends villagers into hiding. Obsidian's villagers run goals, not brains, and
  have no hiding behaviour, so nothing reads it.
- **Raiders near a bell.** Vanilla's bell block entity ticks while it swings: after 5 ticks with a raider within 32
  blocks it plays `block.bell.resonate`, and 40 ticks later it makes raiders within 48 blocks glow. Obsidian's bell
  has no ticking block entity, so neither happens, and the server keeps no record of the swing.
- **Bells rung by redstone and projectiles.** Phases 4 and 3 of the
  [block interactions plan](../block-interactions-plan.md). The bell's shape rule (`BellBlock.updateShape`) is also
  still missing; see [reactions to block updates](vanilla-blocks-reactions-to-block-updates.md).
- **Redstone ore lit by an attack or a step.** Vanilla's `RedStoneOreBlock` also lights when a player starts to break
  it (`attack`) and when an entity that isn't sneaking steps on it. Neither does yet: the attack hook isn't bound for
  the ore, and stepping comes with entity contact (phase 3). Lit ore going dark again on a random tick works and is
  captured in `ticking_blocks.json`.
- **Copper golem statues' own use.** Vanilla's statue changes its pose when used with anything but honeycomb or an
  axe, and an unweathered statue becomes a copper golem again when used with an axe. Neither is implemented; waxing
  works because vanilla passes honeycomb on to the item.

Not compared by the capture: sound pitches and seeds. Redstone ore's dust particles are compared as absent: vanilla's
server never sends them, and clients draw them themselves.
