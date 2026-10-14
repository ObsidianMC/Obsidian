# Engine foundations for mods

The mod system (see [mod-system.md](mod-system.md)) lets mods register content: blocks, items, entities, menus,
recipes, world generation and a client side. What that content can *do* is still thin. A plugin block has one hook
(`UseAsync`), its block entity isn't saved, ticked or synced, a menu is a grid of slots, and the client side has six
data-only hooks. Most runtime systems a mod builds on either run only for hard-coded vanilla cases or don't exist.

This plan builds those general pieces, then tests them by reimplementing one real mod in full (see
[The test mod](#the-test-mod)). It's ordered by dependency: each phase is usable on its own and ends with a sample
plugin that exercises it.

## Rules for every phase

- **Both sides, one API.** Server pieces go in Obsidian.API and Obsidian; client pieces go in the
  `Obsidian.API.Client` project (its own assembly, which servers ship too) and the Obsidian client. A mod uses the
  same API from either side, as today.
- **Reloadable.** Whatever a plugin registers is its own and goes when it unloads, like its entities and recipes do
  now. Identity stays for the process (ids of new registries like block entity types and sound events don't change
  across reloads); behaviour is replaced. Saved data of an unloaded plugin's types is kept as saved, like its
  entities, and comes back with it.
- **Vanilla first where vanilla has it.** Many gaps below are vanilla parity gaps too (runtime light, loot tables,
  furnaces, hoppers, redstone, animated textures). Build the general mechanism, make vanilla's blocks use it, and let
  plugins in through the same door, rather than a plugin-only path next to a hard-coded vanilla one. The vanilla
  parity rules in AGENTS.md apply.
- **NeoForge-shaped, not NeoForge-copied.** Mod authors should recognise the concepts (block entities, capabilities,
  data attachments, menu types with extra data, registry sync), but the API is designed for Obsidian.
- **Registry sync.** Every new registry clients need (block entity types, sound events, data component types, recipe
  serializers) is synced to modded clients with NeoForge's and Fabric's registry sync, like items and blocks, and a
  vanilla client is refused once a mod adds to it. Creative tabs aren't: like vanilla's and NeoForge's, nothing on
  the wire names one.
- **A sample per phase.** Each phase adds to a sample plugin, in the repository and loaded by the tests, that uses
  every new hook, so later phases and the test mod build on proven pieces.

## Fixes found along the way

Bugs the surveys turned up, small enough to fix first:

- The client drops entity metadata for plugin entity types (`EntityMetadata.RemoveMismatches` only knows vanilla
  type ids), so custom names, flags and poses of mod entities are lost.
- Breaking a chest doesn't drop its contents.
- `BlockProperties.RequiresCorrectTool` isn't enforced, and block drops are always the block's own item rather than
  its loot table, vanilla's blocks included.

## Phase 1: blocks that do things

The base every mod needs: blocks with state that save, tick, react and sync.

- **Block entities.** (done) `IPluginRegistry.RegisterBlockEntity` registers a type for a plugin's blocks, with a base class
  that saves to and loads from NBT, marks itself changed, ticks (server and, for mods, client), and sends an update
  to players watching its chunk (`BlockEntityData`, and the chunk packet's block entity list). Vanilla's block
  entities move onto the same contract, replacing the hard-coded switch in `BlockEntityNbt` and the lazy
  "created when opened" containers.
- **Block hooks.** (done) Placement with a context (player, face clicked, look direction) that picks the placed state;
  removal and break; neighbour change; shape update; scheduled tick; random tick; entity inside and stepped on.
  A cancellable place event next to the break event.
- **Block ticks and updates.** (done) A general block tick scheduler saved as `block_ticks`, like fluids'
  `fluid_ticks`, and neighbour and shape updates sent when players, plugins or explosions change blocks, so fences,
  torches and plugin blocks react at runtime. Vanilla's blocks get their shape rules over time; the mechanism comes
  first.
- **Shapes from data.** (done) A block entity can give its block's collision and outline shapes, and the hooks get where
  on the block a player clicked, so one block can hold several parts (AE2's cable bus holds a cable and a part on
  each side, each selected and broken on its own).
- **Drops.** (done) Block drops from loot tables (vanilla's and a plugin's `data/<ns>/loot_table`), with tool checks,
  silk touch and fortune, and container contents dropped on break.
- **Runtime light.** (done) The light engine's decrease pass, so placing and breaking blocks relights the area. This
  closes the plant-growth light gap in [known-gaps.md](known-gaps.md) too.
- **Redstone signals.** (done) A signal API (a block's output power by side, the power a position receives) used by
  vanilla's basic components: wire, torch, repeater, comparator, lever, button, pressure plate. Pistons and
  observers can follow.

**Sample:** a block with a block entity that counts ticks, is powered by redstone, faces where it's placed, relights
its surroundings, and drops its loot table.
(done: `samples/EngineSample`, its tick counter, loaded by `Obsidian.Tests/SamplePlugin.cs`)

## Phase 2: items, inventories and recipes

- **Item hooks.** (done) Use on an entity, inventory tick, and default components on plugin items (tool and mining
  speed, food, equipment and armour, attribute modifiers).
- **Data component types.** (done) Plugins register their own component types with a codec and a network codec,
  saved on stacks and synced to modded clients. Their ids follow the generated `DataComponentType` enum's.
- **Inventory access.** (done) One way to reach the items of whatever is at a position from a side (like NeoForge's
  item handler capability): vanilla's chests, barrels, shulker boxes, furnaces, brewing stands and hoppers, and plugin
  block entities. Inserting and extracting can be simulated first, so a network can plan a transfer without
  duplicating or losing items. Hoppers, droppers and dispensers move items through it. A fluid equivalent follows the
  same shape when a mod needs it.
- **Recipe types.** (done) Plugins register recipe types with their own serializers and matching, loaded from
  `data/<ns>/recipe` like vanilla's. The `UpdateRecipes` packet is sent. Furnaces, smokers, blast furnaces and
  campfires cook. Stonecutter and smithing result slots remain in [known-gaps.md](known-gaps.md).

**Sample:** a machine block that pulls items from a chest above, processes them with a plugin recipe type, and
pushes them into a hopper below.
(done: `samples/EngineSample`, its grinder, loaded by `Obsidian.Tests/SamplePlugin.cs`)

## Phase 3: menus and screens

- **Server menus.** (done) A plugin menu type with its own slot layout and per-slot rules, a quick-move (shift-click) hook,
  data slots synced to the client (like a furnace's progress), button clicks, and extra data sent when opened (like
  NeoForge's `openMenu` with a buffer). A menu can be bound to a block entity and closes when it's removed.
- **Client screens in code.** (done) A mod builds its screens in its own client side, in C#, from UI building blocks
  `Obsidian.API.Client` gains: widgets (buttons, text fields, scroll bars, slots, item grids, tooltips) and a way to
  open a screen for a menu type or on its own. The client implements them with its own UI, as it does the rest of
  the client API. `MenuScreenProperties` stays for simple menus.
- **HUD overlays.** Mods register overlays drawn with the same UI API.
- **Creative tabs.** (done) Mods' items in creative tabs, registered on both sides.
- **Client queries.** (done) Mods read the client's world: block states, their block entities' synced data, entities
  and their metadata.

**Sample:** the phase 2 machine gets a screen with a progress bar, a mode button and a searchable, scrolling list.

## Phase 4: rendering

- **Block entity renderers.** A generic renderer for mod block entities, data-driven (a model with parts animated by
  the block entity's synced data) with a code escape hatch, on the same path as vanilla's hand-coded ones.
- **Model data beyond the block state.** A block entity can give the mesher extra model parts or quads (cable
  connections, facades), re-meshed when its data changes.
- **Render layers and light.** Mods set a block's render layer, and models can mark faces emissive.
- **Animated textures.** `.mcmeta` animations, for vanilla's water, lava, fire and portals as much as mods'.
- **Entity renderers.** Several layers per entity (emissive eyes, overlays), textures chosen by synced data, code
  renderers, and passenger offsets so mod mounts can be ridden.
- **Item renderers.** Mod item model properties and code-driven item renderers.
- **Dimension effects.** Per-dimension sky, cloud and fog rendering for mods: data-driven settings first (cloud
  texture, colour and height, sky colour, fog), a code hook for anything else. The Nether and End special cases stop
  being keyed on vanilla dimension names.
- **Sounds.** A sound event registry for mods (synced ids), sound types for mod blocks, and a client play API.

**Sample:** the machine's block entity spins a part while working; a cable block's model follows its neighbours;
a mod entity glows and can be ridden.

## Phase 5: world, players and entities

- **Travel.** Teleport a player or any entity to a position in another level. A portal API: a frame shape, what
  lights it, the portal block that sends entities through, and finding or building the matching portal in the target
  level, generalising the Nether portal code that's hard-wired to `minecraft:the_nether` today.
- **Saved data.** Per-level and per-world data, and attachments on players, entities and chunks, saved and
  optionally synced to the client (like NeoForge's data attachments).
- **Plugin entities.** Saved per-entity data, synced metadata fields for modded clients, attribute modifiers and the
  `UpdateAttributes` packet (vanilla parity too), mounts and flying movement, damage and death hooks, spawn placement
  rules, and loot tables.
- **Movement.** Mods change how players and their mounts move (gliding, extra jumps, flying mounts), on the server
  and in the client's prediction, driven by the mod's input on the client.
- **Player extensions.** Extra player slots for accessories, built on attachments and phase 3's screens. The engine
  gives the pieces; it doesn't ship a Curios-style accessory API until a second mod needs one.
- **Chunk tickets.** Plugins keep chunks loaded and ticking without players nearby.
- **Progress.** Advancements and statistics (vanilla parity), so mods can grant and check them.

**Sample:** a portal to a sample dimension, a rideable flying mob with saved data and a loot table, and a player
attachment shown in an extra inventory slot.

## The test mod

The engine is proven when a real mod is reimplemented on it in full, as an Obsidian plugin. Both candidates are
NeoForge mods we'd rewrite in C# (their Java can't be converted), reusing data and assets where their licences
allow. Researched on 13 October 2026:

| | Applied Energistics 2 | Aether II |
| --- | --- | --- |
| Reference | 19.2.18, stable, for 1.21.1 (1.21.11 unreleased) | 1.21.11-alpha.2.1, incomplete |
| Java | About 163k lines | About 117k lines |
| Content | 102 blocks, 360 items, 1 entity type | 610 blocks, 800 items, 50 entity types |
| Code licence | LGPLv3 (API MIT) | LGPLv3 |
| Asset licence | Art CC BY-NC-SA 3.0, text CC0 | All rights reserved (1.12.2's: CC BY-SA 4.0) |
| Mostly | Server: block entities, inventories, menus | Both: worldgen, mobs, movement, rendering |

**AE2 first.** It's the better proof per unit of work. Its core loop uses nearly everything in phases 1 to 3 and the
hardest parts of phase 4 (dynamic cable models, terminal screens), with a small content list and one entity type,
and its storage network, channels and autocrafting are the mod's own logic rather than engine work. Its assets can
be reused for a non-commercial test. Aether II is the better second target: it uses what AE2 barely touches
(dimension effects, portals, mobs, mounts, flight, attachments), but its assets would have to be replaced or
licensed.

**The AE2 slice**, from the research: find certus quartz and the inscriber presses, charge and inscribe materials,
build a powered network of cable, drive, 1k cell and crafting terminal, import from and export to a vanilla chest,
then encode a pattern and autocraft one recipe with a pattern provider, molecular assembler and small crafting CPU.
It has to survive restarts, use power, reconnect cables, respect cell capacity and keep terminals in sync between
players. Fluids, wireless terminals, P2P tunnels, spatial storage and meteorites come after the slice, and finish
the full mod.

What the slice needs from the phases:

| Phase | AE2 slice needs |
| --- | --- |
| 1 | Block entities, placement facing, neighbour updates, shapes from data, drops; redstone for level emitters |
| 2 | Inventory access with simulation, data components (cells' contents), recipe types (inscriber, charger) |
| 3 | Menus with extra open data and data slots, code screens with large item grids, search and buttons |
| 4 | Model data for cables and parts, block entity renderers, emissive faces, item model properties |
| 5 | Chunk tickets (spatial storage, later); advancements optional |

So the order is: the fixes, phases 1 to 3, phase 4 without dimension effects and entity work, the AE2 slice, then
the rest of AE2. Phase 5 and the rest of phase 4 follow, with Aether II as the second test.

**Licences.** AE2's code is LGPLv3, which can be combined with Obsidian's GPLv3, and the reimplementation is a
derivative of it, so it stays LGPLv3 with credit. Its assets' CC BY-NC-SA licence doesn't fit GPLv3, so the test mod
is kept locally, outside version control, and none of it goes into Obsidian. The engine itself contains no AE2
code.

## What isn't planned

- Mixins or patching vanilla code: mods extend Obsidian through its API, which grows when a mod needs a hook.
- A shared energy API: no target mod needs another mod's energy yet.
- Shader packs or mod shaders: the client compiles its own shaders, and no planned piece needs custom ones.
