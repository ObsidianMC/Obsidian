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
     `Material`-keyed struct. Plugins register items in their namespace with `IPluginRegistry.RegisterItem`, with an
     `IItemBehavior` for using them. An item keeps its instance and id across reloads; only its behaviour is replaced.
     Once a plugin registered items, vanilla clients are refused, NeoForge clients are sent the item registry with
     NeoForge's registry sync and Fabric clients with Fabric API's, before vanilla's registries. A plugin's items
     stay registered, and synced, until the server stops, even if the plugin unloads for good: stacks of them can
     still be anywhere, so clients need the mod until then.
   - Done for blocks: plugins register blocks with `IPluginRegistry.RegisterBlock`, with state properties, simple
     physics (collision, opacity, hardness, light emission), an `IBlockBehavior` for using them and, by default, an
     item that places them. Their states get ids after vanilla's, numbered like vanilla numbers a block's states, and
     the block registry is synced to NeoForge and Fabric clients, which number states the same way. Physics, light
     and shapes come from a vanilla template block (stone, glass or cobweb). Vanilla blocks keep their generated
     types; `IBlock.Material` is null for plugins' blocks. Saved worlds store blocks by name, so a plugin block loads
     as air once its plugin is gone.
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
     with the type's size, health and speed, no goals of its own) that call its `IEntityBehavior`; they're saved by the
     type's identifier, and kept as saved when the plugin is gone. `IEntity.Type` is null for them, and
     `IEntity.Definition` names their type. Non-living plugin entities aren't supported yet.
   - Next: moving `Material` call sites to items and blocks until `Material` can go; reconfiguring connected players
     when a reload changes the registered content or tags.
3. **Data-driven content and world generation**: loading a mod's `data/` folder like a data pack (biomes, dimension
   types, damage types, tags, worldgen features), letting mods register codec types for data-driven objects (feature,
   density function and biome source types), and reload-safe world generators.
4. **Client**: the client-facing API in `Obsidian.API.Client` (entity renderers, block and item models, screens, key
   bindings, particles), implemented by the Obsidian client, which loads mods, layers their `assets/` like resource
   packs, and answers NeoForge's handshake and registry sync.

## Known gaps

- World generators, dialogs and other registries that plugins fill through injected services are not yet removed when
  their plugin unloads (phase 3 and 2).
- Obsidian.MSBuild writes version 2 archives on its `feat/obby-v2` branch, which isn't released yet.
