# Mod system

Obsidian's plugins are growing into a mod system modelled on NeoForge: a mod is a plugin that, besides commands and
events, adds content (blocks, items, entities, biomes, world generation, codecs) and can talk to the same mod on the
client. This document describes the design and tracks its progress. The archive format is in
[plugin-archives.md](plugin-archives.md).

## Principles

- **One system, both sides.** The public API lives in Obsidian.API and doesn't depend on the server. The server
  implements it; a client (the Obsidian client, or another) implements the client-facing parts, which are interfaces
  only. A plugin's `side` says where it runs.
- **NeoForge's protocol on the wire, and Fabric's.** Mods negotiate network channels with NeoForge's
  configuration-phase handshake, and synchronise registries with its registry sync. Fabric clients tell the server
  their channels with the common `c:version` and `c:register` channels instead, and get registries with Fabric API's
  registry sync. A server running Obsidian mods accepts vanilla clients when no mod needs a channel or content the
  client lacks, accepts NeoForge and Fabric clients whose mods match, and the Obsidian client speaks NeoForge's
  protocol.
- **Reloadable.** A plugin can be reloaded or unloaded while the server runs. NeoForge freezes its registries;
  Obsidian separates identity from behaviour instead:
  - The server owns identity: an identifier like `ruby:ruby_block` keeps its numeric id for the life of the process,
    across reloads of the plugin that registered it. Saves store identifiers, not numeric ids, so nothing needs to
    persist across restarts.
  - The plugin owns behaviour: handlers, block and item logic, codecs. Everything a plugin registers is tracked per
    plugin and removed when it unloads, so that its collectible load context can be freed.
  - Clients follow: when the set of channels or content changes, players are sent back through the configuration
    phase to renegotiate and resynchronise, instead of being disconnected.

## Phases

1. **Foundation** (done)
   - `.obby` v2 archives with NeoForge-style metadata and dependency resolution.
   - Reloading a plugin together with its dependents, with per-plugin services, and unregistering its events,
     argument parsers and payload channels on unload.
   - Loading plugins before the worlds.
   - NeoForge's channel negotiation in the configuration phase, a payload channel API for mods, and `isModded` in
     the status response.
   - Fabric clients: the common channels (`c:version`, `c:register`) tell the server a Fabric client's channels, so
     required channels are checked against them. Fabric channels have no versions, so a reload that changes a
     channel's payloads should change its id.
2. **Content registries** (in progress)
   - Done for items: `Item` is a registered item type (one instance per item, keyed by identifier) rather than a
     struct keyed by the old `Material` enum. Plugins register items in their namespace with
     `IPluginRegistry.RegisterItem`, with an `IItemBehavior` for using them. An item keeps its instance and id across
     reloads; only its behaviour is replaced. Once a plugin registered items, vanilla clients are refused, NeoForge
     clients are sent the item registry with NeoForge's registry sync and Fabric clients with Fabric API's, before
     vanilla's registries. A plugin's items
     stay registered, and synced, until the server stops, even if the plugin unloads for good: stacks of them can
     still be anywhere, so clients need the mod until then.
   - Done for blocks: plugins register blocks with `IPluginRegistry.RegisterBlock`, with state properties, simple
     physics (collision, opacity, hardness, light emission), an `IBlockBehavior` for using them and, by default, an
     item that places them. Their states get ids after vanilla's, numbered like vanilla numbers a block's states, and
     the block registry is synced to NeoForge and Fabric clients, which number states the same way. Physics, light
     and shapes come from a vanilla template block (stone, glass or cobweb). Vanilla blocks keep their generated
     types. Saved worlds store blocks by name, so a plugin block loads as air once its plugin is gone.
   - Done for block, item and entity type tags: like a NeoForge or Fabric mod's data pack, a plugin's
     `data/<namespace>/tags/<registry>/<path>.json` files (`values` with `#` tag references and optional entries,
     `replace`, and NeoForge's `remove`) are merged with vanilla's tags in load order, and again when plugins load or
     unload. Plugins add to vanilla's tags (vanilla's `Tag` objects hold their entries, so server code sees them) and
     make their own; `IPluginRegistry.AddToTag` does the same from code. A tag whose required entry is missing keeps
     its earlier entries rather than disappearing as in vanilla.
   - Done: blocks generated from `data/<namespace>/blocks/*.json` by Obsidian's source generator, with typed states
     and a `Register` method, and `TagKey`s for a plugin's tags (see [plugin-content.md](plugin-content.md)).
   - Done for entity types: `IPluginRegistry.RegisterEntity` registers a type with an id after vanilla's, synced to
     NeoForge and Fabric clients with the `minecraft:entity_type` registry. Its entities are mobs (a `PathfinderMob`
     with the type's size, health and speed, no goals of its own), projectiles (flying with gravity and drag, hitting
     the first block or living entity in their way) or objects (falling and resting where they land), and call its
     `IEntityBehavior`; they're saved by the type's identifier, and kept as saved when the plugin is gone.
     `IEntity.Type` is null for them, and `IEntity.Definition` names their type. Goals, navigators and goal
     controllers plugins give mobs through the API are dropped when the plugin unloads.
   - Done: connected players follow plugin changes. A client configured with other registries (plugins' items,
     blocks or entity types), or a modded client with other payload channels, goes back through configuration like
     vanilla's reconfiguration: the player leaves the world (saved), the channel handshake and registry sync run
     again, and they join again; a client that can't have the new content is refused, like when joining. Other
     clients get the merged tags in play, like vanilla's `/reload` sends them.
   - Done: `Material`, one closed enum naming both blocks and items, is gone. Items are named by their instances
     (`ItemsRegistry.Diamond`) and blocks by their states (`IBlock`), like vanilla's `Items.DIAMOND` and
     `Blocks.STONE`, so plugins' items and blocks are the same kind of thing as vanilla's. Generated `ItemIds` and
     `BlockIds` constants (vanilla's ids) let vanilla code keep matching items and blocks in switches and patterns
     (`stack.Holder.Id is ItemIds.Bucket`, `block.Is(BlockIds.Stone)`). A block item places the block with its
     identifier, or a plugin item's block.
   - Next: Phase 3.
3. **Data-driven content and world generation** (in progress): loading a mod's `data/` folder like a data pack
   (biomes, dimension types, damage types, tags, worldgen features), letting mods register codec types for
   data-driven objects (feature, density function and biome source types), and reload-safe world generators.
   - Done: biomes, dimension types and damage types from a plugin's `data/<namespace>/worldgen/biome`,
     `dimension_type` and `damage_type` files (see [plugin-content.md](plugin-content.md)). Their registries are
     extensible at runtime: plugins' entries get ids after vanilla's in the order they were first added, keep their id
     and instance across reloads (a reload replaces their data) and stay until the server stops, like items and
     blocks. The global biome palette's bits follow the registry's size, like vanilla's. Plugins' files are read and
     checked like vanilla 1.21.11's codecs read a data pack's entries, environment attributes included (except
     particles with options and rich bed rule messages), and are sent as vanilla's network codecs write what was read,
     so vanilla clients always accept them; vanilla's entries are sent by name from the `minecraft:core` pack. A
     change to these entries sends connected players back through configuration. Plugin biomes don't generate anywhere
     yet, and the server doesn't apply environment attributes itself yet.
   - Done: worldgen features. Plugins' `data/<namespace>/worldgen/configured_feature` and `placed_feature` files are
     read at runtime into the same objects the source generator makes of vanilla's features (a parity test reads all
     of vanilla's features both ways and compares them), and NeoForge's `neoforge/biome_modifier` files
     (`neoforge:add_features`, `neoforge:remove_features`) change biomes' features. Plugin biomes' `features` take
     plugins' placed features, by identifier or inline. Features aren't synced or saved, so they come and go with
     their plugin: each plugin change builds a new feature set and every level's generator moves to it, sorting the
     features again like vanilla with the same data pack. Without plugins, worlds generate exactly as before; chunks
     already generated are kept.
   - Next, slice 3: codec types registered by plugins (feature, placement modifier and density function types).
     Worldgen types in plugins' assemblies are already found by their attributes, and placing features made of
     them holds a call into their plugin, which unloading waits for; registering them isn't designed yet.
   - Then slice 4: dimensions and reload-safe generators, with plugin biomes placed in dimensions.
4. **Client**: the client-facing API in `Obsidian.API.Client` (entity renderers, block and item models, screens, key
   bindings, particles), implemented by the Obsidian client, which loads mods, layers their `assets/` like resource
   packs, and answers NeoForge's handshake and registry sync.

## Known gaps

- World generators, dialogs and other registries that plugins fill through injected services are not yet removed when
  their plugin unloads (phase 3 and 2). Plugins' worldgen features are.
- Obsidian.MSBuild writes version 2 archives on its `feat/obby-v2` branch, which isn't released yet.
