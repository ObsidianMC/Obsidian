# Mod system

Obsidian's plugins are growing into a mod system modelled on NeoForge: a mod is a plugin that, besides commands and
events, adds content (blocks, items, entities, biomes, world generation, codecs) and can talk to the same mod on the
client. This document describes the design and tracks its progress. The archive format is in
[plugin-archives.md](plugin-archives.md).

## Principles

- **One system, both sides.** The public API lives in Obsidian.API and doesn't depend on the server. The server
  implements it; a client (the Obsidian client, or another) implements the client-facing parts, which are interfaces
  only. A plugin's `side` says where it runs.
- **NeoForge's protocol on the wire.** Mods negotiate network channels with NeoForge's configuration-phase handshake,
  and synchronise registries with its registry sync. A server running Obsidian mods accepts vanilla clients when no
  mod needs a channel the client lacks, accepts NeoForge clients whose mods match, and the Obsidian client speaks the
  same protocol.
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
2. **Content registries** (in progress)
   - Done for items: `Item` is a registered item type (one instance per item, keyed by identifier) rather than a
     `Material`-keyed struct. Plugins register items in their namespace with `IPluginRegistry.RegisterItem`, with an
     `IItemBehavior` for using them. An item keeps its instance and id across reloads; only its behaviour is replaced.
     Once a plugin registered items, vanilla clients are refused and NeoForge clients are sent the item registry with
     NeoForge's registry sync before vanilla's registries. A plugin's items stay registered, and synced, until the
     server stops, even if the plugin unloads for good: stacks of them can still be anywhere, so clients need the mod
     until then.
   - Next: blocks (with their states) and entity types the same way; moving `Material` call sites to items and blocks
     until `Material` can go; reconfiguring connected players when a reload changes the registered content.
3. **Data-driven content and world generation**: loading a mod's `data/` folder like a data pack (biomes, dimension
   types, damage types, tags, worldgen features), letting mods register codec types for data-driven objects (feature,
   density function and biome source types), and reload-safe world generators.
4. **Client**: the client-facing API in `Obsidian.API.Client` (entity renderers, block and item models, screens, key
   bindings, particles), implemented by the Obsidian client, which loads mods, layers their `assets/` like resource
   packs, and answers NeoForge's handshake and registry sync.

## Known gaps

- World generators, dialogs and other registries that plugins fill through injected services are not yet removed when
  their plugin unloads (phase 3 and 2).
- Obsidian.MSBuild still packs version 1 archives; it needs to write version 2 ones, with `assets/` and `data/`.
