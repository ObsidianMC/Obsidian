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

1. **Foundation** (in progress)
   - Done: `.obby` v2 archives with NeoForge-style metadata, dependency resolution, reloading a plugin together with
     its dependents, per-plugin services, unregistering events and argument parsers on unload, and loading plugins
     before the worlds.
   - In progress: NeoForge's channel negotiation in the configuration phase, a payload channel API for mods, and
     `isModded` in the status response.
2. **Content registries**: blocks (with their states), items and entity types that mods add at runtime, after the
   vanilla entries, with ids that survive reloads; NeoForge's frozen registry sync to modded clients; refusing vanilla
   clients when content they can't display is registered; reconfiguring connected players after a reload.
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
