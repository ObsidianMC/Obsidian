# Progress parity


The core progress API, vanilla save envelopes and packet fields are implemented. These gaps keep phase 5's
Progress item from being marked done:

- Advancement visibility is currently reset-plus-all, including recipe and hidden definitions. Content reload
  also resets everything. Displays use the server's compact tree layout, replayed against vanilla 26.3 captures;
  visibility-depth rules, chat announcements and full icon component JSON parsing remain. Display flags and telemetry
  are encoded, but telemetry is not collected.
- Supported triggers are listed in [Progress](../plugin-content.md#progress). `bred_animals` is deferred because
  breeding currently keeps no responsible-player identity. Other trigger ids never fire.
- Predicate support is deliberately strict. Unsupported condition fields never match, including component
  predicates other than active enchantments, entity flags/equipment/effects/subpredicates, killing-blow/damage
  predicates, biome/structure/light/fluid/smokey location predicates, `recipe_crafted` ingredients, location
  offsets, and arbitrary loot predicate terms. Add these to `ProgressConditions` with captured references before
  enabling the affected vanilla advancements.
- `inventory_changed` observes inventory versions on a tick, so transient changes between ticks are not captured.
  `location` runs every 20 ticks. Placement, plugin item-on-block uses and portal igniters fire
  `item_used_on_block`; successful bucket/entity use, music-block, campfire and queued bone-meal operations still
  need their own confirmation hooks. There is no broad attempt-based fallback that could grant failed actions.
- Recipe rewards explicitly add existing displays, persist known recipe ids in `recipeBook.recipes`, and fire
  `recipe_unlocked` on first reward unlock. The existing recipe book still exposes all crafting recipes on join;
  selective unlocking and vanilla recipe-book settings/highlights are a separate change.
- Function reward identifiers are read but cannot execute: the server has no Minecraft function system. Loot
  rewards use the existing loot evaluator and inherit its unsupported-condition/table-loading limitations.
- Automatic statistics cover play/world time, death/rest/sneak timers, deaths, jumps, common movement distances,
  combat damage and kills, mining, crafting, consumables, block placement, plugin uses, bows/crossbows/tridents,
  thrown eyes, item breakage, pickups and drops. Remaining vanilla custom interactions, vehicle distances,
  scoreboard stat criteria, and item uses outside those confirmed hooks need additional event integration.
  The requested swim/distance stats follow the server's existing movement flags and terrain checks; complete
  physics/swimming validation is outside progress. Statistics are sent on request, not periodically unsolicited.
- Custom stat ids are synced and remain reserved across unload. The Obsidian client's registry API still needs
  matching custom-stat registration before a client-side plugin can declare these through the same method.
  Server-only players (`ServerPlayer`) and alternative `IPlayer` implementations retain the default unsupported
  progress accessors; the persisted implementation currently belongs to connected `Player` instances.
- `/advancement` currently accepts an online player and `only`, with an optional criterion. Selector targets and
  `everything`, `from`, `until`, `through` modes are deferred. Older save versions are retained/read without
  running Mojang data fixers; renamed historical stat/advancement ids are not migrated.

Continue in `Obsidian/Progress`, `Player.Progress.cs`, and the three registries for advancements, triggers and
custom statistics. Existing gameplay files only contain small calls into that runtime. The three tests in
`Obsidian.Tests/Progress.cs` replay captured vanilla 1.21.11 packet/save fixtures and exercise reward idempotence
and plugin unload/reload.
