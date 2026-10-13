# Mod system

Obsidian's plugins are growing into a mod system modelled on NeoForge: a mod is a plugin that, besides commands and
events, adds content (blocks, items, entities, biomes, world generation, codecs) and can talk to the same mod on the
client. This document describes the design and tracks its progress. The archive format is in
[plugin-archives.md](plugin-archives.md).

## Principles

- **Familiar, but Obsidian's own.** Obsidian's systems are close enough to NeoForge's that a NeoForge or Fabric mod's
  author finds their way, but they're designed for Obsidian rather than copied. What mods share across loaders is
  followed exactly: the protocol on the wire, vanilla's data formats, and the common namespace conventions, like the
  shared `c:` tags. NeoForge-only formats, like its biome modifiers, have Obsidian equivalents instead.
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
     `IEntityBehavior`; they're saved by the type's identifier. Unloading the plugin takes them out of the levels
     and keeps them in their chunks as saved (see [What host services add](#what-host-services-add)).
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
   - Done for particle types and menu types: `IPluginRegistry.RegisterParticle` registers a particle type without
     options, sent with `ILevel.SpawnParticles`, and `RegisterMenu` a menu type of up to six rows of up to nine slots
     above the player's inventory, opened with a `MenuContainer` that the server's container code drives like a
     chest's (shared by its viewers, and closed when its plugin unloads). Both get ids after vanilla's that stay for
     the process, are synced to NeoForge and Fabric clients with the `minecraft:particle_type` and `minecraft:menu`
     registries (refusing vanilla clients once there are any), and have no behaviour to replace on reload; a menu's
     shape can't change until the server restarts.
   - Done for brewing: brewing stands brew (fuel, 400-tick brews and vanilla's mixes) and route shift-clicks like
     vanilla's, and `IPluginRegistry.AddPotionMix` and `AddContainerMix` add a plugin's mixes after vanilla's, removed
     when it unloads (see [plugin-content.md](plugin-content.md#brewing)). Mixes aren't synced: clients know only
     vanilla's, as vanilla's client builds them itself.
   - Done for saplings: `BlockProperties.TreeGrower` makes a plugin's block a sapling that grows the configured
     features it names, vanilla's or files', on random ticks and with bone meal like vanilla's saplings, 2x2 mega
     trees included; the grower is the plugin's, like its behaviour (see
     [plugin-content.md](plugin-content.md#saplings)). Vanilla's saplings grow, and bone meal grows crops and grass.
   - Next: Phase 3.
3. **Data-driven content and world generation** (done): loading a mod's `data/` folder like a data pack
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
     change to these entries sends connected players back through configuration. Plugin biomes generate in plugins'
     dimensions (slice 4a). The server applies only a dimension type's `monsters_burn` attribute and timelines tag
     itself, for undead mobs burning by day.
   - Done: worldgen features. Plugins' `data/<namespace>/worldgen/configured_feature` and `placed_feature` files are
     read at runtime into the same objects the source generator makes of vanilla's features (a parity test reads all
     of vanilla's features both ways and compares them), and NeoForge's `neoforge/biome_modifier` files
     (`neoforge:add_features`, `neoforge:remove_features`) change biomes' features. Plugin biomes' `features` take
     plugins' placed features, by identifier or inline. Features aren't synced or saved, so they come and go with
     their plugin: each plugin change builds a new feature set and every level's generator moves to it, sorting the
     features again like vanilla with the same data pack. Without plugins, worlds generate exactly as before; chunks
     already generated are kept.
   - Done: worldgen types registered by plugins. `IPluginRegistry.RegisterFeatureType`,
     `RegisterPlacementModifierType` and `RegisterWorldgenType<TBase, TValue>` (int, float and height providers,
     block state providers, block predicates, rule tests, or a plugin's own kinds) add types that feature files name
     in their `type`, read with vanilla's rules (see [plugin-content.md](plugin-content.md#feature-types)). Plugins'
     types are only found when registered, in their own namespace; they're gone when the plugin unloads, with the
     features made of them, and nothing keeps them (a reload test checks its load context is collected). Placing a
     feature made of any of a plugin's types, nested ones included, holds a call into the plugin. `BlockSet` moved to
     the API so plugin types can take block lists and tags. Vanilla's tree parts, weighted lists and biome sets
     followed in Phase 3.5, then feature sizes and structure processors.
   - Done, slice 4a: plugins' dimensions. A plugin's `data/<namespace>/dimension` level stem files are read like
     vanilla's, with vanilla's noise generator, vanilla's noise settings and any of vanilla's biome sources
     (`multi_noise` inline or by preset, `fixed`, `checkerboard`, `the_end`), so plugin biomes generate. Each becomes a
     level of the default world, created when its plugin loads and saved under `dimensions/<namespace>/<path>` like a
     data pack's dimension. Vanilla's dimensions are the same generator built from vanilla's stems, and generate as
     before. A level's name and its dimension type are now separate, so clients get the plugin dimension's name in
     login and respawn with its type's height. Levels stay until the server stops; a changed stem applies after a
     restart. Players get there with `/dimension <id>` or `IPlayer.TeleportToDimensionAsync`; there are no portals
     (see [plugin-content.md](plugin-content.md#dimensions)).
   - Done, slice 4b: plugins' noise settings, density functions and noises, from their `data/<namespace>/worldgen/
     noise_settings`, `density_function` and `noise` files, with surface rules inline as in vanilla. They're read at
     runtime into the same objects the source generator makes of vanilla's (a parity test reads all of vanilla's files
     both ways and compares them, and generates vanilla's golden chunks with the runtime ones), refer to each other
     lazily with one shared instance per identifier, and plugins' dimensions use them. Plugins register density
     function, surface rule, surface condition and biome source types with `RegisterWorldgenType`
     (`IDensityFunction`, `ISurfaceRule` with `ICustomSurfaceRule`, `ISurfaceCondition` with
     `ICustomSurfaceCondition`, `BiomeSourceBase`). Each step of generating a chunk holds the plugins whose types
     the dimension's generator is made of; when one unloads, the dimension stops generating new chunks, and its
     generator lets go of the plugin's code (slice 4c resumes it). A plugin's code failing stops the level the same
     way, with an error, rather than the server. Values are checked against vanilla's codecs' ranges. A dimension keeps
     the noise settings it was created with, and a reload changing them applies after a restart (see
     [plugin-content.md](plugin-content.md#noise-settings-density-functions-and-noises)).
   - Done, slice 4c: plugins' level generators and reload-safe generators. `IPluginRegistry.RegisterGenerator`
     registers an `ILevelGenerator` type by identifier, in the plugin's namespace, which a world names as its
     `Generator` in `worlds.json` (or a child dimension by its name). The level keeps one generator for its life,
     which saves the identifier and runs a new instance of the plugin's generator, created in a service scope of
     its own, holding a call into the plugin a chunk at a time. The level's own chunk locks keep saves from
     capturing a chunk mid-write while generators stop and swap. Unloading the plugin removes the registration,
     stops the level and disposes the instance and its scope once its calls end (the reload test checks the load
     context is collected); loading it again swaps in the new plugin's generator between chunks. Plugins'
     dimensions stopped by an unloading plugin whose types they use are read again from the level stem they were
     created with once it loads again, and resume without reloading their own plugin; one whose stem, plugin noise
     files (its biome source's included) or biomes changed stays stopped until a restart.
     A plugin generator throwing, like plugins' worldgen types failing, stops its level with an error until the
     plugin reloads. A saved world whose plugin generator is missing after a restart loads stopped, rather than
     falling back to the overworld's generator; a new one isn't created, nor is a new dimension of an existing
     world (see
     [plugin-content.md](plugin-content.md#level-generators)).
   - Phase 3 delivered: plugins' biomes, dimension types and damage types, synced to clients and kept by identifier;
     worldgen features and NeoForge's feature biome modifiers from data files, read like vanilla's and checked
     against vanilla's own; worldgen types plugins register for features, placements, providers, density functions,
     surface rules and biome sources; noise settings, density functions and noises from data files; dimensions from
     level stems; and level generators in code. All of it comes and goes with its plugin: data is applied again on
     every plugin change, plugins' code is only called holding a call into the plugin, and levels stop and resume
     generating with their plugins rather than failing or keeping their code loaded.
   - Possible later, gaps noted along the way (see [plugin-content.md](plugin-content.md)):
     - in flat level stems: lakes, features and structure overrides (refused for now), which need the noise
       generator's decoration and structure steps to run over flat terrain;
     - particles' items with components in environment attributes;
     - attributes whose mechanics Obsidian lacks (sleeping and spawn points, which beds and respawn anchors set);
       plugins build their own portals.
   - Done after Phase 3.8, level stems: dimension types and biomes defined inline become the plugin's or pack's
     entries, named for the stem (`<stem>/type`, `<stem>/biome_<n>`), so they get ids and are synced like files'.
     Stems may generate with `minecraft:flat` (layers and a biome, through the superflat generator, which now takes
     settings and any build range), `minecraft:debug` (vanilla's grid of every block state, plugins' included) or a
     loaded plugin's level generator, which gets the stem's generator object through the optional
     `IConfigurableLevelGenerator` (new in `Obsidian.API`, additive) and stops and resumes with its plugin. Vanilla's
     own dimensions keep `minecraft:noise`: other generators there are refused.
   - Done after Phase 3.8: environment attributes take particles with options, sent as vanilla's codecs write
     them, and bed rules any text component as their message. The server applies dimension types'
     `can_start_raid`, `can_pillager_patrol_spawn`, `fast_lava`, `piglins_zombify` and `snow_golem_melts` attributes
     (vanilla's nether's too, which Obsidian's copy of vanilla's types lacked, so piglins no longer zombify there),
     and raids run in every level, starting where the dimension type allows, like vanilla's. The end fight stays
     with `minecraft:the_end`, as vanilla only fights the dragon there. Noise files take noises defined inline
     (seeded from their parameters, as vanilla can't seed them), a number as a spline function's `spline`, and a
     range as a spawn target's `depth`.
     Migration: this changes two members of `Obsidian.API`, breaking source and binary compatibility, so plugins
     using them must be rebuilt. `ClimateParameter.Depth` is an `ImmutableArray<double>` range `[min, max]`, like the
     other climate parameters, rather than a `double`: read `Depth[0]` and `Depth[1]`, and write a point as
     `[depth, depth]`. `SplineDensityFunction.Spline` is an `ISpline`, a multipoint `Spline` or a `SplineConstant`,
     rather than a `Spline`: assign either, and test for `Spline` before using a multipoint spline's members.
   - Done after Phase 3.8, environment rules: the server applies biomes' attributes where they're asked about,
     over the dimension type's (vanilla's own biomes' values included), for the attributes it applied before and
     for `bed_rule` (beds explode where it says so), `respawn_anchor_works` (charged anchors explode where they don't
     work) and `water_evaporates` (buckets of water evaporate). Plugins' dimension types and biomes get the same
     from their files' `attributes`; a bed rule's message is shown to players (see
     [plugin-content.md](plugin-content.md#whats-checked)).
   - Phase 3.5, part B (done): inline entries in plugins' level stems, and tree parts in the API. A stem's
     `generator.settings` may define noise settings inline, read like a plugin's noise settings file, with
     inline or named density functions; the plugins whose types they use, and the plugin entries they name, are
     tracked like a plugin biome source's, and a reload changing them applies after a restart like any stem
     change. A `multi_noise` preset may be vanilla's inline `{"preset": ...}`. Dimension types and biomes stay
     named. The bases of vanilla's tree parts (`TrunkPlacer`, `FoliagePlacer`, `RootPlacer`, `TreeDecorator`,
     with `TreeContext` and `TreeDecoratorContext`), `WeightedEntry<T>` and `BiomeSet` moved to the API, so
     plugins register their own placers and decorators with `RegisterWorldgenType` and their feature types take
     properties of these types (see [plugin-content.md](plugin-content.md#tree-parts)); vanilla's own placers
     and decorators stay in Obsidian and generate as before.
   - Done after Phase 3.5: the bases of tree feature sizes (`FeatureSize`) and structure processors
     (`StructureProcessor`, with `StructureBlockInfo` and `TemplatePlacement`, what a processor sees of its
     placement) moved to the API, so plugins register sizes for a tree's `minimum_size` and processors for the
     fossil feature's processor lists. Fallen trees take any tree decorator, vanilla's or a plugin's, with a
     context of only logs, like vanilla's, and generate as before. Plugin biomes may define configured carvers
     inline.
   - Phase 3.5, part A (done): NeoForge's spawn and carver biome modifiers (`add_spawns`, `remove_spawns`,
     `add_carvers`, `remove_carvers`; `add_spawn_costs` and `remove_spawn_costs` are read but unused, like vanilla's
     spawn costs), applied to vanilla's and plugins' biomes in NeoForge's phases. The mob spawner spawns from the
     merged lists (vanilla's biomes', plugins' biomes' `spawners` and the modifiers'), rebuilt and published atomically
     on every plugin change; plugin entity types spawn naturally with their new `MobCategory`. Carvers now come from
     each start chunk's biome, like vanilla, rather than from the dimension; vanilla's biomes of a dimension list the
     same carvers, so vanilla worlds carve as before (the golden chunk tests check it). Plugins add configured carvers
     from `worldgen/configured_carver` files. Tags of `worldgen/biome`, `worldgen/placed_feature` and
     `worldgen/configured_carver` from plugins' files work in biome modifiers and biomes; biome tags hold plugins'
     biomes and are sent to clients, vanilla's included (see [plugin-content.md](plugin-content.md#biome-modifiers)).
   - Phase 3.6, biome modifiers (done): Obsidian's own biome modifiers replace NeoForge's. One `data/<namespace>/biome_modifier`
     file names its `biomes` (identifiers and tags mixed) and those it `exclude`s, and what it `add`s and
     `remove`s: features per step, spawners, carvers and spawn costs; every modifier's additions run before any
     removal. The mob spawner now uses spawn costs, vanilla's soul sand valley's and warped forest's included, and
     biomes' `creature_spawn_probability` (see [plugin-content.md](plugin-content.md#biome-modifiers)).
   - Phase 3.6, convention tags (done): Obsidian ships its own `c:` tags (349: 90 block, 204 item, 5 entity type,
     50 biome tags), filled with vanilla's entries under the shared names NeoForge's and Fabric's mods use, like
     `c:ores/iron`, `c:ingots/iron`, `c:is_overworld` and `c:bosses`. They're merged like a built-in data pack
     between vanilla's tags and plugins' files, exist with no plugins loaded and are sent to clients (see
     [plugin-content.md](plugin-content.md#convention-tags)). Fluids have no `c:` tags, as plugins can't tag fluids.
   - Phase 3.7, worldgen overrides (done): like vanilla's data packs, data replaces vanilla's entries. The server's
     `datapacks` folder (`Paths:DataPacks`) holds vanilla data packs, folders or zip files with a `pack.mcmeta`, read
     once at startup in name order after the plugins, and plugins' own `data/minecraft/...` files override too; the
     last file for an entry wins, whole, plugins' and data packs' alike (it was the first plugin's before), and tag
     files merge. Data packs are the server's rather than a world's, as Obsidian's registries are shared by its
     worlds. Biomes, dimension types and damage types, features and carvers, noise settings, density functions, noises,
     multi-noise parameter lists (a new plugin file type) and level stems can be overridden. Overridden synced
     entries are sent with their data instead of by name from `minecraft:core`, like vanilla sends a changed entry.
     Vanilla's compiled entries naming an override are read again from vanilla's files with the runtime readers, and
     a vanilla dimension whose stem, noise settings or parameter list is overridden is built at runtime; everything
     else keeps its compiled code, so worlds without overrides generate exactly as before. Like vanilla, which reads
     data packs' stems over level.dat's on every load, a vanilla dimension takes its overrides when its level starts,
     in every world, and keeps them until a restart; plugins' later changes to it apply after a restart, like a
     plugin dimension's. Until a level takes a dimension, its stem is read again as plugins and packs change, so it
     sees overrides read after it; a stem startup can't read yet is kept and read again when every plugin and pack
     is read, before vanilla's dimension types are checked, and once more after, and only then logged. A level
     whose generator can't be built gives its dimension back, so a corrected stem applies without a restart. A
     vanilla dimension type override its dimension's noise settings don't fit (counting the whole cells generation
     fills) is left out, as Obsidian can't clamp generation to the build range like vanilla, and data packs are
     read within file count and size limits (see
     [plugin-content.md](plugin-content.md#data-packs-and-overriding-vanilla)).
   - Phase 3.7, biome regions (done): several plugins add their biomes to the overworld's and nether's climate,
     or any other dimension's, without overriding each other, Obsidian's equivalent of TerraBlender. A
     `data/<namespace>/biome_region` file names a `dimension` and a `weight`, and either the biomes it `replace`s
     in the dimension's own layout or a parameter list of its own (`biomes`, like a `multi_noise` source's).
     The dimension's biome source picks a region per column from irregular cells about 1024 blocks across,
     seeded by the world seed and the dimension, its own layout weighing 10; terrain stays as it is, and surface,
     features, carvers, spawns and structures follow the region's biomes, which count among the dimension's
     possible biomes for feature ordering and structures. A dimension takes the loaded plugins' regions when it
     starts generating and keeps them until the server stops: later changes apply after a restart, and a region
     whose plugin unloads keeps generating with its biomes. Without regions, dimensions generate exactly as
     before (see [plugin-content.md](plugin-content.md#biome-regions)).
   - Phase 3.8, structures (done): plugins' and data packs' structures, structure sets, template pools, processor
     lists and structure templates, from `data/<namespace>/worldgen/structure`, `structure_set`, `template_pool`,
     `processor_list` and `data/<namespace>/structure/*.nbt` files, read at runtime like vanilla 1.21.11's codecs
     into the same objects as vanilla's compiled ones (a parity test reads all of vanilla's both ways and compares
     them). Every data-driven vanilla structure type is available, jigsaw structures with every pool element type,
     both placements with exclusion zones and frequency reduction, and plugins' processor types in processor lists.
     Like features, they're applied again on every plugin change: a level's structures move to the new sets, whose
     starts chunks not generated yet get, keeping the starts of the sets that stay; starts of a set that's gone, and
     starts made with a plugin that unloads, are dropped, so their plugin's code isn't kept, with their state saved
     (once their placements in progress end, without holding up the plugin change) and restored if they come back.
     Decorating a chunk restores, places and saves one snapshot of the starts reaching it, so a plugin change while
     it waits for start chunks doesn't place starts it didn't restore. Starts are
     saved by their structure's identifier, and one whose structure is gone after a restart loads and stays as saved.
     Starting and placing a structure made of plugins' types is a call into them, and its failure stops the dimension
     like a feature's. Overrides of vanilla's structures, sets, pools, processor lists and templates apply to vanilla's
     structures, which are read again where they name them, as are those naming overridden placed features. Without
     plugins or overrides, vanilla's compiled structures generate as before (see
     [plugin-content.md](plugin-content.md#structures)).
   - Worldgen gaps (done): what places a configured feature by identifier, like the pale oak's moss decorator,
     finds it through the current features, overrides included, as vanilla's registry lookups do (as do sapling
     growth and bone meal, since). The surface builder's named noises follow
     overrides. Data packs' `overlays` for vanilla 1.21.11's format apply over their root, and their `filter` blocks
     earlier packs' files, like vanilla. A vanilla world made with a data pack changing its overworld opens as an
     integrated server when the same overworld stem is loaded, with a clear error otherwise. Biome regions can be
     registered in code (`IPluginRegistry.RegisterBiomeRegion`, read like a region file), and take biome tags as
     `replace` keys. Structure tags come from plugins' and data packs' files (explorer maps and dolphins use them),
     and jigsaw structures read the object forms of `max_distance_from_center` and `dimension_padding`. A vanilla
     dimension type override its noise settings don't fit is still left out: vanilla clamps generation to the build
     range, which every generation step would have to follow, without a vanilla capture to check it against.
   - Possible later, for structures: structure types and pool element types in code (the structure, piece and pool
     element bases work on Obsidian's internal generation context, templates and jigsaw placement, and would need an
     API like the tree parts'), eyes of ender, and a `/locate` command; clamping generation to a dimension type's
     build range.
   - Next: Phase 4.
4. **Client**: the client-facing API in `Obsidian.API.Client` (entity renderers, block and item models, screens, key
   bindings, particles), implemented by the Obsidian client, which loads mods, layers their `assets/` like resource
   packs, and answers NeoForge's handshake and registry sync.
   - Slice 4a, mods on the client (done): the Obsidian client loads `.obby` mods whose side is `client` or `both`
     from its `mods` folder at startup, with the server's archive, metadata and dependency rules (the same code, with
     the client's side), and runs their `ConfigureServices` and `ConfigureRegistry`. Blocks, items and entity types
     register exactly as on a server, with ids after vanilla's in load order; commands,
     events, tags and worldgen are a server's and do nothing there, and the server's lifecycle callbacks aren't
     called. A mod that fails to load is logged and skipped, with the mods that require it. Mods come only from the
     player's folder: servers can't send code. In configuration the client sends its brand (`obsidian`), answers
     NeoForge's channel query with its mods' `MapPayload` channels and NeoForge's registry sync and extensible enum
     channels before the handshake's pong, and registers the agreed channels it receives on with
     `minecraft:register`; a server without NeoForge's query that registered its own channels (Fabric's) gets the
     mods' configuration channels with `minecraft:register` before the pong instead. It acknowledges NeoForge's
     `extensible_enum_data` unless the server's mods extended an enum, which it can't, and disconnects then. Like
     NeoForge's client, it requires every registry the sync announced before confirming it and refuses a non-empty
     snapshot of a registry that is neither one of vanilla's built-in registries nor NeoForge's. Like NeoForge's
     client applying a snapshot, it remaps ids: each `frozen_registry` snapshot whose ids it reads is matched by name
     against its own table (the registries mods add to with its mods' content, every other vanilla registry as
     vanilla numbers it, from the registries report, `Assets/registry_ids.json`, and `neoforge:entity_data_serializers`
     as empty, since it decodes only vanilla's serializers), and the server's ids are translated to the client's
     where packets carry them (`ServerIds`, a per-connection map every packet's buffer carries; identity maps keep
     the fast path). That covers block states (chunk palettes, whose global palette's bits follow the server's number
     of states, block updates, particles, entity metadata, falling blocks and level events), blocks, block entity
     types, items and data component types in item stacks (read and written, so creative slots and clicks use the
     server's ids), the registry ids item components hold (read and written: tool rules' and block predicates'
     blocks, repairable items, pot decorations, equippable sounds and entity types, attribute modifiers' attributes,
     consumables' and death protection's effects and sounds, potion contents' and suspicious stews' effects, entity,
     bee and block entity data types, and block predicates' component types; components the client doesn't model,
     or whose ids don't translate, keep the server's bytes, and a stack naming an entry the server lacks is written
     empty, as is one whose components hold a required stack, like a bundle's items, naming one), component hashes
     (which name built-in registries' entries by the client's names and datapack registries' entries, like
     enchantments, by the names the server's RegistryData listed, omitting a hash it can't name), beacon effects
     (menu data and `SetBeacon`), entity types, particle types, menus, mob effects, sound events, attributes, command
     argument types, slot displays, tags and statistics. A mod's payload serialized before the maps change is dropped
     rather than sent with stale ids. Migration: components name the registry of the ids they read and write
     (`ReadRegistryId`, `WriteRegistryId`, the holder variants, `IdSet.Read(reader, registry)`), so they translate;
     custom `INetStreamReader`/`INetStreamWriter` implementations keep compiling, since those methods default to
     passing ids through, and the old `IdSet.Read(reader)`, `IdSet.Write(value, writer)` and `ReadIdSet()` remain,
     obsolete and untranslated.
     Block states aren't synced: like NeoForge, they're numbered from the
     server's block order, each block with the states the client registered it with. So a server running the same
     mods in another order, or that reloaded plugins, is accepted. A NeoForge or Obsidian server numbers vanilla's
     entries like vanilla, so the other vanilla registries it syncs must keep the client's ids. NeoForge's other
     registries number NeoForge's extensions to vanilla's formats, which the client doesn't decode, and aren't
     checked. An entry the client lacks disconnects it, naming the first few, as does a server that finishes
     configuration before the sync it started; entries only the client has are fine, as on NeoForge's client. Without
     a sync, the server's registries are vanilla's. The maps are forgotten when a new configuration starts. NeoForge
     itself adds attributes, command argument types and slot displays to vanilla's registries (NeoForgeMod's), which
     the client knows, after vanilla's, so it joins plain NeoForge servers: it ignores NeoForge's attributes, parses
     the `neoforge:enum` and `neoforge:modid` arguments like NeoForge writes them and completes them as words the
     server suggests, and reads NeoForge's fluid slot displays as empty slots. Entries NeoForge mods add that the
     client lacks still disconnect it. Obsidian servers sync only the registries plugins add to. Mods' payloads
     are dispatched in configuration and play on channels a mod declared for that phase and to the client (on a
     NeoForge server, only agreed ones), and sent with `ClientSession.SendPayload` on the agreed channels (elsewhere,
     on the channels the server registers), never across a change of protocol; since slice 4c, mods handle and send
     them through the client API. A new configuration forgets the previous one's registries, channels and sync.
     Singleplayer worlds run the client's mods: the integrated server loads plugins from the client's `mods` folder
     (`Paths:Plugins`). An empty custom payload, like NeoForge's sync confirmation, no longer fails to parse on the
     server, and an entity's particle of a type the client doesn't know ends its metadata rather than disconnecting.
   - Done, slice 4b, mods' assets: each mod archive's `assets/` is a resource pack, one per mod in load order, above
     vanilla's assets and below the player's packs (and servers' and realms' above those), reloaded with them. An
     archive needs no `pack.mcmeta`, only its `assets/` are read, and resource packs' limits and read budgets apply.
     The asset loaders read every namespace like vanilla: block states and block and item models by identifier,
     atlases' directory sources in every namespace (sprites named `<ns>:<path>`), item definitions for vanilla's and
     plugins' items, GUI sprites, particle definitions (mods' by identifier, for later) and translations, merged per
     language across namespaces. Models are baked for every block state, plugins' after vanilla's, so plugin blocks
     render in the world and their items in the GUI and in hand. Vanilla's name-based rules (tints, render layers,
     culling between glass) leave plugin blocks alone: their faces' render layers come from their textures, and
     their tints come from slice 4c's tint sources. Biomes the client's registry lacks, like a server's plugin biomes,
     are tinted from the data the server sends for them (grass, foliage, dry foliage and water colours, the grass
     colour modifier, and temperature and downfall for the colormaps); vanilla's biomes keep the registry's colours
     (see [plugin-archives.md](plugin-archives.md#assets)). The client's mods load before its assets, so their
     blocks are baked from the start.
   - Done, slice 4c, the client API: `Obsidian.API.Client` holds interfaces and records only. A mod's plugin
     implements `IClientPlugin`, whose `ConfigureClient(IClientRegistry)` the Obsidian client calls after
     `ConfigureRegistry` and servers never call, so a `both` mod runs on a server without client code. Through
     `IClientRegistry` a mod handles the payloads servers send on the channels it declared with `MapPayload`
     (`HandlePayload`, replacing the client's internal registration), sends its own on `IClientConnection`s (the one a
     payload arrived on, or `Connection`, whichever server the client plays on), registers key bindings
     (`key.<ns>.<path>`, in the key binds screen and `options.txt` like vanilla's, with `Pressed`, `IsDown` and the
     bound `Key`), tints its blocks and their items (`BlockTint`: the biome's grass, foliage, dry foliage or water
     colour, blended like vanilla's, or a fixed colour, on faces with a `tintindex`; items take vanilla's default
     colours unless their definitions have tints), and draws its entity types (`EntityRendererProperties`: a model
     layer from `assets/<ns>/models/entity/`, in the shape of the client's `client_models.json` layers, a texture,
     scale and shadow radius; heads follow the look and legs swing, by part name). Like the server's content, a mod's
     bindings, tints and renderers are its own (in its namespace, for its blocks and entity types); they take effect
     only once `ConfigureClient` returns, and the registry is sealed then. See
     [plugin-content.md](plugin-content.md#client).
   - Done, particles and menus: the client registers its mods' particle types and menu types like the server and
     maps the synced `minecraft:particle_type` and `minecraft:menu` snapshots to them. Mods' particles
     (read without options) are drawn with the sprites of their definitions and behave like vanilla's base particle,
     or as an `IClientRegistry.RegisterParticleProvider` says (lifetime, gravity, friction, scale, the server's
     velocity kept or randomised, animated sprites). Mods' menus show like a chest with their slots centred, or with
     the background and slot positions an `IClientRegistry.RegisterMenuScreen` gives.
   - Done, client ticks, mods' entity animations, equipment and sizes, and reloading: `IClientRegistry.OnTick` runs
     a mod's handler on the game's thread every client tick. Mods' entity renderers play keyframed animations from
     `assets/<ns>/animations/entity/`, in the shape of `client_models.json`'s (an idle one, a walk one advancing with
     the distance walked, which replaces the leg swing, and an attack one on arm swings, which servers send as mobs
     attack), and draw held items on parts named `right_arm` and `left_arm` and vanilla's humanoid armour on the parts
     it covers. Plugin entity types take their registered size for hitboxes, picking, name tags and eye height. The
     title screen's Reload Mods button unloads the client's mods (their client registrations, payload channels,
     content behaviour and load contexts, like the server's plugin unloading) and loads the folder again, then the
     assets. It's only there while the client isn't connected, and content stays registered with its ids, so content
     a mod adds needs a restart.
   - Next: particle types with options (a codec on both sides) and particle providers in code, for particles that
     move or draw unlike a sprite; menus beyond a grid of slots (other slot shapes, data slots, buttons, NeoForge's
     extra open data) and screens drawing more than their background; entity render layers (emissive and translucent
     ones) and animations a server triggers beyond arm swings for mods' entities; client lifecycle events (joining
     and leaving a server).

## Porting from NeoForge

Obsidian.Porting, a console tool in this repository, converts a NeoForge mod's data and metadata to Obsidian's
formats: its `neoforge/biome_modifier` files to Obsidian's biome modifiers and its `neoforge.mods.toml` to
`obsidian.mod.json`, copying the data and assets in vanilla's formats and reporting what has no Obsidian equivalent.
A mod's Java code can't be converted. See [porting-from-neoforge.md](porting-from-neoforge.md).

## What host services add

Besides `IPluginRegistry`, plugins add to the server through its services, and unloading undoes that too, so a
reloaded plugin starts clean and its code can unload. Each plugin gets its own `IServer` and `ILevelFactory`, injected
or from its services, and given to `OnLoadedAsync` and `OnServerReadyAsync`; what they add is that plugin's, whichever
code makes the call (a library it uses, say). The services its `IServer` leads to are its own too: its
`CommandHandler.ServiceProvider`, and the scopes created from it, resolve its `IServer` and `ILevelFactory`, and its
`CommandHandler.RegisterCommands` and `EventDispatcher.RegisterEvents` register its own commands and handlers when
given no plugin.

What the server shares hands each plugin its own `IServer` too: event arguments (`e.Server`), command and payload
contexts, and players' connections (`IClient.Server`) give the plugin's instance while its code runs in a call into it
(its event handlers, commands and their checks, payload handlers, behaviours and goals), and the server's otherwise.
The arguments aren't copied or changed per handler: the property reads the plugin of the call the current async flow
is in, which is the flow's own, so concurrent events and a handler's background tasks each see their own plugin, and
what a handler sets on the shared arguments, like cancelling, the handlers after it see. The server's own handlers and
commands, even when an event a plugin raised runs them, see the server's instance, which adds for the server. Only the
commands the server lists keep the server's handler (`Command.CommandHandler`). So a plugin adds through its own:

- recipes registered with `IServer.RegisterRecipes`, which crafting and the recipe book use like vanilla's, are
  removed: they stop matching, leave online players' recipe books and crafting results, and come back under the same
  recipe book ids when the plugin registers them again (see [plugin-content.md](plugin-content.md#recipes));
- scoreboards made with `IServer.ScoreboardManager.CreateScoreboard` and boss bars made with `IServer.CreateBossBar`
  are retired: their objective, teams and bars are removed from their players' screens, their names are free again,
  and changing them or showing them again throws `InvalidOperationException` (removing does nothing), so a handler
  still running can't bring them back; a scoreboard being shown while packet handlers run isn't shown once they
  finish, if it retired meanwhile. A shown boss bar is retired even when the plugin no longer references it;
- generators registered with `ILevelFactory.RegisterGenerator` are registered like
  `IPluginRegistry.RegisterGenerator`'s (keyed by their `Id`, without the namespace rule): unloading removes them and
  stops the levels using them, which resume when the plugin registers them again, in `ConfigureRegistry` or its load
  callbacks. The server's generators come first, then the first plugin to register an id keeps it, unless that's
  another instance of the same plugin, like the one a reload replaces. `IPluginRegistry.RegisterGenerator` stays the
  preferred way;
- dialogs registered with `DialogRegistry.Register` or `DialogBuilder.Register`, static APIs, are the plugin's whose
  code (its load context) made the call, and are removed. They're part of the content clients are configured with,
  as they were when registered, so a plugin change that changes the dialogs sends players back through
  configuration, like other content; registering the same dialogs again, as a reload does, doesn't;
- event handlers and commands registered on the server's `EventDispatcher` and `CommandHandler` without a plugin
  container count as the plugin's whose code the handler is, like those registered through `IPluginRegistry`;
- server players added with `IServer.AddServerPlayerAsync` leave when it unloads, like other players do (other
  plugins' handlers see them leave);
- entities are the plugin's whose code adds them to a level, whichever way: `ILevel.SpawnEntity`, `TryAddEntity`,
  spawners, or server code the plugin's code calls, like breaking a block dropping items. The innermost plugin code
  on the call stack decides, as from its ready callback or a task of its own, or else the plugin whose call the async
  flow is in, as when server code a handler awaits continues. Entities are spawned through objects every plugin
  shares (levels reached from players, entities and event arguments), so the plugin's own `IServer` can't stand in
  for them. Unloading removes them from their levels, between the levels' ticks, and players are sent their removal;
  nothing of the plugin's runs as they go, and no event is raised. Entities of the plugin's entity types go too,
  whoever spawned them, those loaded from saved chunks included. Unlike vanilla, which drops entities of unknown types
  as it loads them, Obsidian keeps those in their chunk's saved entities, as it does for every saved entity it can't
  spawn: so they're kept in their chunk as they were saved, and spawn again once the plugin is loaded again (on the
  level's next tick) or when their chunk loads with it. The other entities a plugin spawned are gone.

Adding to an unloading plugin's dialogs, recipes, scoreboards, boss bars, generators, event handlers, commands, server
players or entities throws `InvalidOperationException`, rather than keeping them after it unloads; one added while it
starts unloading is removed as it throws. An entity of a plugin's type whose plugin is unloaded or unloading isn't
added to a level (`TryAddEntity` returns false).

## Known gaps

- Obsidian.MSBuild writes version 2 archives on its `feat/obby-v2` branch, which isn't released yet.
