# Porting plan: 1.21.11 → 26.3

How we move Obsidian from Minecraft 1.21.11 (protocol 774) to 26.3, the latest release (2026-09-15). The work happens
on `26.3.x`, branched from `1.21.x` at `ef4848e5`. `26.3.x` becomes the main branch when the port is done.

Mojang now numbers versions as `<year>.<drop>.<hotfix>`. Each drop is a full content update, like the old 1.21.x
releases were, so each drop gets its own branch. A later hotfix (26.3.1, …) belongs on `26.3.x`. The next drop, 26.4,
is already in snapshots and will get its own branch.

## Target

| | 1.21.11 (now) | 26.3 (target) |
|---|---|---|
| Protocol | 774 | 777 |
| DataVersion | 4671 | 5023 |
| Data pack format | 94.1 | 121.0 |
| Resource pack format | 75.0 | 97.1 |
| Java (vanilla tooling) | 21 | 25 |
| Blocks / block states | 1,166 / 29,671 | 1,286 / 35,723 |
| Items / component types | 1,505 / 104 | 1,658 / 122 |
| Entity types | 157 | 161 |
| Density function types | 34 | 44 |

We go straight to 26.3. We don't stop at 26.1 or 26.2, but we use their jars as evidence for what changed when.
Obsidian stays single-version: the handshake accepts only 777, as it accepts only 774 today.

The 26.1, 26.1.1, 26.1.2, 26.2 and 26.3 changelogs, data generator diffs and bytecode checks are summarised by area
below. Every number in this plan was measured from Mojang's jars, except where a line says it was inferred.

## What changed, by area

### Tooling

- **Jars are no longer obfuscated (26.1).** Version metadata no longer lists `server_mappings` or `client_mappings`, and
  the jars contain the real `net.minecraft.*` names. Our generator fails straight away: `VanillaServer.cs` downloads
  the mappings, and `VanillaDumper.Mojang` resolves every class and member through them. Packet discovery and worldgen
  group discovery enumerate the mapping keys, so they would find nothing. The upside is that every dumper and capture
  harness gets simpler.
- **Report layout changes.** Item defaults move from `reports/items.json` to one file per item at
  `reports/minecraft/components/item/<id>.json` (26.1). `reports/blocks.json` drops `definition`, and the
  `minecraft:block_type` registry is gone (26.3). Nothing should depend on block definitions from that report.
- Vanilla internals are renamed and repackaged as usual (for example, density functions have their own package in
  26.3). These failures are loud, and we fix them one by one.

### Protocol

- **The packet catalogue changes.** Play grows from 139/66 to 144/69 packets (clientbound/serverbound), and
  Configuration gains `post_effects`.
  - Most Play ids shift. 26.3 alone moves 104 clientbound and 17 serverbound ids. Configuration clientbound ids from
    10 up shift by one.
  - Serverbound dispatch is hard-coded by number today: `PlayClientHandler`, `ConfigurationClientHandler`,
    `LoginClientHandler` and `Client.cs` (`ConfigurationAcknowledgedId = 15`). If an id is wrong, the wrong packet
    is handled and nothing reports an error.
- **Payloads change while ids stay the same:**
  - Login Success gains a session UUID (26.2).
  - The Play `login` packet gains `onlineMode` (26.2).
  - Spawn info encodes game types with VarInt codecs (26.3).
  - Chunk sections gain a fluid-count short (26.1).
  - `interact` is split into `interact` and `attack` (26.1).
  - `set_time` carries world clocks (26.1).
  - Team parameters are reordered (26.2).
  - `accept_teleportation` carries a position and rotation (26.3).
  - Entity position sync uses `PositionPath` / `VecDelta` (26.3).
  - Particles and explosions change shape (26.3).
  - Signs use a `SignTextSlot` (26.3).
  - Advancements carry x/y positions (26.3).
- **New packets:**
  - `game_rule_values`, `set_game_rule`, `low_disk_space_warning` (26.1)
  - `spectator_action`, replacing `spectate_entity` (26.2)
  - `add_transient_block`, `swing_animation`, `punch` (26.3). `swing` is removed.
- **Synced registries grow from 23 to 32:**
  - four animal sound variants and `world_clock`
  - `sulfur_cube_archetype`
  - `block_transformer`, `decorated_pot_pattern` and `worldgen/block_state_provider`
- **Block states.** There are now 35,723 states, past 32,768, so the global palette needs 16 bits per entry. Check that
  `GlobalBitsPerBlocks` is computed from the registry and not fixed at 15.
- **Entity metadata.**
  - 26.1 inserts `AGE_LOCKED` at index 17 for ageable mobs. That shifts 65 inherited accessor indices.
  - The serializer table grows from 39 to 44 entries. Ids from the variant serializers onwards move.
  - Slime size moves to a shared cube-mob index (26.2).
  - Obsidian writes metadata indices by hand: 221 writes across 97 files, and nothing tests them against vanilla.
- **Data components.**
  - 18 component types are added and two removed (`swing_animation`, `map_color`).
  - 62 kept components get new ids.
  - `pot_decorations` changes from item ids to full item stacks.
  - The patch framing (added/removed counts, then the type ids) is unchanged.
- **Attributes.** Five are added (26.2), and all 35 existing attribute ids move.

### Save format

- **26.1 reorganises the world folder:**
  - Dimensions move to `dimensions/minecraft/<id>/`. There is no more `DIM-1`/`DIM1`.
  - Player files move to `players/{data,advancements,stats}`.
  - Saved data gets namespace folders, and maps move to `data/minecraft/maps/`.
  - `level.dat` is split into separate saved-data files: weather, game rules, world-gen settings, world clocks, boss
    bars, scheduled events, wandering trader and the dragon fight.
  - Difficulty becomes `difficulty_settings`.
- **26.3 changes block-state NBT.** `Name`/`Properties` become `id`/`properties`, and a default state can be written as
  a bare id. This affects chunk palettes, structure templates and block-state NBT everywhere.
- **26.3 changes chunk status.** `noise`, `surface` and `carvers` merge into `terrain`, and the protochunk
  `carving_mask` is no longer saved.
- **Block entities change.**
  - Furnaces and brewing stands widen their time fields to ints and gain a speed multiplier.
  - Decorated pots store four item stacks.
  - Signs gain `allow_op_features`.
  - The `bed` block entity is removed (26.2).
- **`level.dat` gains `version_history`.**
- **Obsidian has no data fixers.** Every save writes `ServerConstants.DataVersion`, its only data version constant.
  Every world, server or integrated, now uses 26.3's layout (see the World section of `integrated-server.md`), and
  `WorldUpgrade` upgrades a world Obsidian saved in either 1.21.11 layout once, after copying it to a backup beside it.
  It detects them by layout, not by DataVersion, since builds for 26.3 wrote 5023 in 1.21.11's layout.

### Data-driven systems

- **Loot tables are rewritten (26.3).**
  - `function` and `condition` dispatch keys become `type`.
  - Lists of conditions become explicit conjunctions.
  - `functions` becomes `modifier`.
  - 1,281 vanilla loot tables change.
- **Number providers are split** into int and float context providers, with arithmetic (26.3). They feed loot, fuel,
  composting, `/compute` and more.
- **Recipes change.**
  - Brewing becomes a recipe type, with 279 vanilla recipes (26.3).
  - Cooking time is now required, and speed comes from fuel.
  - Special recipes become configurable: `crafting_dye` and `crafting_imbue` are added, map cloning becomes a
    transmute recipe (26.1), and three old special serializers are removed.
- **Fuel, brewing fuel, composting, villager food and mob visibility become item components (26.3).** Our hand-written
  `FurnaceFuels` and `BrewingRegistry` tables have to go.
- **Villager trades become data (26.1):** 391 trades, 68 trade sets and 73 tags. Offers are drawn from world-seeded
  random sequences.
- **Smaller changes:**
  - World clocks and timeline markers (26.1).
  - Entity predicates become a namespaced map (26.2).
  - Trim materials use `palette` (26.3).
  - Pottery patterns and block transformers become data (26.3).
  - Tags are renamed and split, for example `dirt` (26.1). There are 883 tag files, up from 625.
- **Our readers must accept data pack format 121.** This covers data packs, `PluginCodecs`, `EnvironmentAttributes`,
  the biome, dimension-type and level-stem codecs, the EngineSample plugin's data and the NeoForge porting layer.

### World generation

26.3 is the biggest worldgen change since 1.18. Default worlds may look alike, but neither the JSON nor the numbers carry
over.

- **Registry renames:**
  - `configured_feature` becomes `feature`, `configured_carver` becomes `carver`, and feature config moves from
    `config` to the root.
  - `surface_rule` becomes a referenceable `material_rule`, with 42 rules and 8 conditions as data.
  - Ore veins become a material rule.
- **The noise router changes.**
  - Aquifer settings become optional `aquifers`, with their functions moved out of the router.
  - `preliminary_surface_level` becomes `chunk_surface_level`.
  - Bearding is explicit in final density.
  - Cell sizes move onto interpolation nodes.
  - Spawn targets become maps from density function id to range.
- **Density function types go from 34 to 44**, with new arithmetic, `cache`, `slice`, `gradient`, `interval_select`
  and `end_outer_islands`. The cache types, `shifted_noise`, `weird_scaled_sampler`, `y_clamped_gradient` and
  `end_islands` are removed. Operands are renamed to `left`/`right`/`input`/`value`.
- **Noise parameters change.** They gain `base_octave`, `amplitude_modifiers`, `base_amplitude`, `octave_count` and
  `normalize` (including `legacy`). Renaming the fields is not enough.
- **Density and noise are evaluated in single precision (26.3).** Some coordinate and normalisation steps stay in
  double. Exactly which operations are float has to be matched against captures, not guessed.
- **The chunk pipeline goes from 12 statuses to 10.** Terrain does base fill, then surface, then carving, and carving
  masks live inside terrain generation.
- **Features are consolidated.**
  - 26.1 removes flower, random-patch, coral, kelp, seagrass and similar feature types in favour of general machinery.
  - New feature and placement types are added, along with general block-state providers.
  - 26.2 generalises dripstone into `speleothem`.
- **Biome spawners and spawn costs move to environment attributes.**
- **New content:**
  - Sulfur Caves (26.2)
  - Dappled Forest, poplar trees, shelf mushrooms and red shrubs (26.3)
  - 18 abandoned-camp structures, all jigsaw (26.3)
  - Structure templates go from 1,202 to 1,511.
- **The resolved overworld biome parameters change** (7,594 entries), so read them from
  `reports/biome_parameters`.

### Content and gameplay

- **26.1:**
  - Golden dandelion and age locking
  - Note block trumpet instruments
  - Default sign and banner rotation changes from 0 to 8.
  - Baby hitboxes change.
  - Name tags become craftable.
- **26.1.2:** spectators may not attack.
- **26.2:**
  - The sulfur family, potent sulfur (a block entity), geysers and sulfur spikes
  - The Sulfur Cube mob, with 12 archetypes and its own bounce, friction and drag physics
  - Five new attributes
- **26.3:**
  - Poplar wood and boats, wool and concrete stairs/slabs, colored leaves, the straw bed and the cushion entity
  - Explorer maps become separate items.
  - Spectators can use portals.
  - Persistent mobs idle when no player is near.
  - Whitelist defaults to on (fresh installs only; existing configs keep theirs).
- **Commands:**
  - 26.1: `/swing`, and `/time` with clocks
  - 26.2: `/unpublish`
  - 26.3: `/compute`, `/posteffect`, the `/item` and `/execute` slot sources, and inline `/place feature`
  - Many command errors become real command failures.
  - The 59 game rules are unchanged, but now sync to clients and live in `game_rules.dat`.

## Principles

- **Fix the tooling first, then the version.** Most of the risk is in code that drifts silently: hard-coded packet
  ids, hand-written ordinal enums, hand-indexed metadata and hand-written vanilla tables. Where we can, replace them
  with generated data or name lookups while still on 1.21.11, where the existing tests show nothing broke. Then the
  version bump becomes regeneration plus loud compile errors.
- **Capture before we port.** Each parity area needs a 26.3 reference before we change its code. The 1.21.11
  worldgen, structure, fluid and redstone hashes were captured with tools that aren't in this repo (an external
  decompile harness and `tools/redstone`). Phase 0 brings capture tooling into the repo, as Java fixture dumpers that
  run vanilla code from the jar like the existing ones. We never commit decompiled source.
- **Reimplement, don't translate.** 26.3's density functions compile to samplers over float buffers. That's Mojang's
  structure, not ours. We match the numbers, the order of random draws and the formats, and keep Obsidian's own design
  (see "Vanilla parity" in `AGENTS.md`).
- **Every phase can be merged on its own.** Each phase is one or more PRs into `26.3.x`, and each leaves the build and
  tests green. A capability that isn't ported yet is disabled in an obvious way, not left half-working.

## Phases

### Phase 0: preparation (still on 1.21.11)

These changes don't depend on the version. They go into `26.3.x` before the bump, so the current tests check them.

1. **CI.**
   - Add `26.*` to the branch filters. `dotnet.yml` matches only `1.*`, and `docfx.yml` and `nuget-push.yml` match only
     `1.21.x`.
   - Add a `dotnet test` job. CI only builds today.
2. **Name-resolved packet dispatch.** Switch the serverbound handlers to generated ids or names, as
   `Obsidian.LoadTest/Bot.cs` already does, and remove the numeric `case` labels and `ConfigurationAcknowledgedId`.
3. **Generate the ordinal enums.** `EntityType`, `ParticleType`, `GameEvent`, `EntityMetadataType`, `AttributeIds`
   and the particle/activity tables in `RegistryIds.cs` should come from `registry_ids.json` and the dumpers. Add a
   consistency test for anything that stays hand-written (`HeightmapType`, `Pose`).
4. **Test entity metadata.** Compare each entity's written indices and serializers against the
   `client-entity-metadata` fixture that `EntityMetadataFixtures.java` already dumps.
5. **One version source.** Derive `DataVersion`, `VersionName`, the data pack format and the protocol from the
   generated assets, or keep them in one constants file. Fix `MobStorage`'s literal and the stale `3337` values. Have
   the `ItemComponentWire` test find its resource by `MinecraftVersion`.
6. **In-repo capture harness.** Add Java fixture dumpers for worldgen (router values, noise and surface, carvers,
   decoration, biomes, light), structures, fluids and redstone, plus a documented command to re-record. The harness is
   finished when it reproduces today's 1.21.11 hashes exactly. Only then do we trust it for 26.3.
7. **Fix the concurrent test-cache download race** ([known gaps](known-gaps/README.md)). Every cache starts cold after the bump.

**Exit:** all of the above merged, the test suite green on 1.21.11, and recapture reproducing the existing fixtures.

### Phase 1: toolchain and version bump

1. **Make the generator work without mappings.**
   - Turn `VanillaDumper.Mojang` into identity lookup.
   - Enumerate classes by scanning the jar's entries.
   - Drop the `server_mappings` and `client_mappings` downloads (`VanillaServer.cs`, `ClientModels.cs`).
   - Make `FixtureSource` pass names through.
   - Update `ComponentWireFormat.md`, which relies on `server.txt`.
2. **Update the generator for the new report layout:** per-item component reports, and `blocks.json` without
   `definition`.
3. **Bump the version.**
   - Set `MinecraftVersion` to `26.3` and add `ProtocolVersion.v26_3 = 777` with description `"26.3"`. That string is
     also the manifest id, the cache folder and the `minecraft:core` known-pack version.
   - Set `DataVersion` to 5023 and the data pack format to `(121, 0)`.
   - Fix the dumpers for 26.3 renames as they fail.
4. **Get it compiling.** Fix the churn in hand-written code from renamed or removed generated ids. Loot and worldgen
   keep running on 1.21.11 data (decision 4), so the Mojang generator keeps working until Phase 5. Mark parity tests
   that can't pass yet as skipped, with the phase that re-enables them.

**Exit:** `dotnet build` regenerates 26.3 assets, the solution compiles, the server boots, and every test passes or
is skipped with the phase that re-enables it. The 1.21.11 worldgen and structure fixtures still pass.

### Phase 2: protocol 777, through to a real client joining

1. **Login and configuration.**
   - Login Success session UUID
   - All 32 synced registries in the order vanilla sends them
   - Configuration `post_effects`
   - The known-pack version
2. **Join and world.**
   - The Play `login` packet: `onlineMode`, and GameType codecs in spawn info
   - Chunk-section fluid count
   - 16-bit global palette
   - Heightmaps
   - `set_time` with world clocks
3. **Movement and interaction.**
   - `accept_teleportation`
   - `PositionPath` / `VecDelta` entity sync
   - The `interact` / `attack` split, with the 26.1.2 spectator check
   - `punch`, `swing_animation`, `spectator_action`
   - Sign text slots
4. **Entity metadata.** Rebuild the layouts against the Phase 0 test, including `AGE_LOCKED`, the cube-mob size,
   sound variants and the 44-entry serializer table.
5. **Data components.** Add the 18 new codecs and their hashes, update the changed ones (`pot_decorations`,
   consume-effect teleport, instruments, trims), and remove `swing_animation` and `map_color`. Extend
   `component-fixtures-input.json` and the samples so `ItemComponentWire` covers all 122 types.
6. **Remaining packets.** Teams, particles, explosions, advancements, game-rule sync and edit (with permission
   checks), `low_disk_space_warning`, `add_transient_block` and attributes.
7. **Payload tests.** Add replay tests from `ClientProtocolFixtures` for each changed packet. That's one table-driven
   test, not one test per packet.

**Exit:** a vanilla 26.3 client joins and can move, chat, break and place blocks, use its inventory, see entities and
stay connected for 10 minutes, with no disconnects or decode warnings in the client log. The
load test bot is updated to match.

### Phase 3: save format and existing worlds

1. **Write the 26.3 layout.**
   - `dimensions/minecraft/<id>`
   - `players/*`
   - Namespaced saved data and maps
   - The saved-data files split out of `level.dat`
   - `difficulty_settings` and `version_history`
2. **Block-state NBT** with `id`/`properties` and the bare-id default, for reading and writing. Structure templates
   inside the jar use it too.
3. **Chunk status `terrain`, without `carving_mask`.** Update the block-entity fields: furnaces, brewing stands, pots,
   signs, and the removed `bed` block entity.
4. **Existing worlds** are upgraded once (decision 1 below). The upgrade of Obsidian-written 1.21.11 worlds covers:
   - moving files to the new layout;
   - splitting `level.dat`;
   - rewriting block-state NBT, in palettes, block entities and templates;
   - mapping the chunk status.

   It only runs on a copy, and it leaves a backup. It does not try to be a general vanilla data fixer.
5. **Round-trip tests** with a vanilla-26.3-written world fixture: open it, save it, and compare what we model.

**Exit:** vanilla 26.3 opens a world Obsidian saved, and Obsidian opens a world vanilla 26.3 saved, without data loss
in the parts we model.

### Phase 4: data-driven systems

1. **Loot — complete.** Loot uses 26.3 tables, `type`, a single `condition` (including explicit conjunctions), and
   object/array `modifier` values. Named built-in predicates and namespaced entity predicate maps are read; existing
   unsupported entity predicates remain excluded. Plugin loot files use the new schema. `block_loot.json` and
   `entity_loot.json` are re-captured on 26.3, with additional container and typed-number replays.
2. **Context number providers — shared component complete.** `Obsidian.API.Data.ContextNumberProviders` reads separate
   int/float expressions, arithmetic, conversions, distributions, conditional dispatch and registry references.
   `NumberContext` supplies the random stream, predicates and optional world-dependent sources. The built-in provider
   definitions are generated from 26.3 data; loot uses typed adapters to keep the programmatic plugin API compatible.
   `/compute` and `/data modify … compute` now use this component (Phase 6).
3. **Recipes:**
   - brewing recipes, replacing `BrewingRegistry`;
   - required cooking times, with speed from fuel components, replacing `FurnaceFuels`;
   - composting, villager food and mob visibility from components;
   - `crafting_dye`, `crafting_imbue`, and transmute map cloning.
4. **Trades — complete.** Villagers and wandering traders draw their offers from the 26.3 trades and trade sets.
   The generator expands the trade tags (the `villager_trade` registry and its tags stay server-side). Each trade
   set draws from the world's random sequence it names, in vanilla's order, through the shared context number
   providers and the loot modifiers trades use (random dyes and potions, enchanting with an additional trade cost,
   filtered discards). `TradeFixtures.java` captures vanilla's offers for two seeds across every profession and
   level, villager types and wandering traders; `VillagerTrades` replays them, and saved offers keep their cost
   components.
5. **Other systems:**
   - World clocks and timelines
   - Entity sub-predicates: namespaced map format complete with loot (step 1); unsupported predicate kinds remain gaps.
   - Trim palettes
   - Pottery patterns
   - Block transformers
   - Renamed tags
6. **Plugins and ports.** Plugin codec readers and data pack format 121. Update the EngineSample plugin data and the
   NeoForge porting layer and channel payloads, once NeoForge and Fabric for 26.x exist.

**Exit:** the loot, recipe, progress and plugin-codec tests pass against 26.3 captures.

### Phase 5: world generation

Re-capture with the Phase 0 harness first, then work in this order. Each step has its own fixture.

1. **Read the new JSON.** Feature, carver and material-rule registries, the new noise and density-function schemas
   and aquifer settings. Add the new density functions: arithmetic, `slice`, `gradient`, `interval_select`,
   `end_outer_islands`, `distance_to_point`, `lerp` and `cache`.
2. **Noise and density in single precision.** Match the router-value captures bit for bit before going further. This
   is the base everything else stands on.
3. **Terrain status.** Base fill, then material rules (surface plus ore veins), then carving with in-terrain masks and
   the new carver config, with aquifers through the new settings. Explicit bearding. Merge our pipeline's
   noise, surface and carver stages to match.
4. **Biomes.** The new parameter list, Sulfur Caves and Dappled Forest, and spawners in environment attributes. The
   spawner change also affects natural spawning.
5. **Features.**
   - Replace the consolidated types with the general machinery.
   - Add the new feature and placement types and the block-state providers.
   - Speleothems, poplar trees, shelf mushrooms and red shrubs.
   - Per-biome decoration is implemented beside the live generator in `BiomeTerrain.Decorate`. It collects
     section biomes from the surrounding 3x3 terrain chunks and runs the source's globally ordered features by
     generation step with 26.3 decoration and feature seeds. `BiomeDecoration.Generate` reserves a structure
     placement hook before the placed features in each step; structure pieces remain step 6 work.
6. **Structures.** Abandoned camps (jigsaw, so mostly data) and the changed templates. Structure starts are saved
   with vanilla's piece ids and fields, checked against starts vanilla saved (`VanillaStructureSaves`); what remains
   is in [known gaps](known-gaps/structure-pieces-in-saved-chunks.md).
7. **The Nether and the End,** including the new End island composition. The Nether's terrain (lava sea, bedrock,
   material rules), multi-noise biomes, carvers, structures and decoration, and the End's composed islands
   (`end/islands`: the main island's distance gradient and `end_outer_islands`), biome source, structures and
   decoration all match 26.3 captures. `decoration/05-the-end-and-nether` places spikes with their crystals and
   cages, the obsidian platform and return gateways. The exit portal and the 20 gateways are the dragon fight's
   (Phase 6 entity work), not worldgen; see [mob parity](mob-parity.md).
8. **Performance.** Re-record `WorldgenBench` and compare with the 1.21.x numbers in `worldgen-benchmarks.md`. Single
   precision may make generation faster.

**Exit:** all re-captured `VanillaWorldGeneration` and `VanillaStructureGeneration` fixtures pass, the 1.21.11 data is
gone (`LegacyMinecraftVersion`, `Assets/legacy-1.21.11`, the `obsidian_legacy` tags and the legacy state table), and
throughput is no worse than on 1.21.x. The world upgrade (Phase 3) also reads the legacy state table, to convert
moving pistons that 1.21.11 saved by numeric state; keep that one table for it, or have the upgrade drop those
pistons.

### Phase 6: content and gameplay

1. **New blocks and their behaviour:**
   - golden dandelion interactions and age locking remain open;
   - note block trumpets — placement/neighbor instrument selection and playback implemented, captured in
     `more_blocks.json` (`VanillaMoreBlocks`);
   - the sulfur family, potent sulfur gas and geysers, and sulfur spikes — implemented with the limits in
     [known gaps](known-gaps/26-3-blocks.md). `VanillaSulfurBlocks` replays the RCON sulfur scenarios and Java sulfur
     probes, recorded together with `CaptureFixtures=sulfur`;
   - poplar construction blocks, wool and concrete stairs/slabs, and colored poplar leaves — implemented through
     the shared block-family APIs, with captured placement, shaping, leaf-distance and interaction references in
     `more_blocks.json` (`CaptureFixtures=more_blocks`). Shared shelf interactions and poplar sapling growth remain
     gaps; see [known gaps](known-gaps/26-3-blocks.md);
   - straw bed placement and its own sleep rules remain open.
2. **New entities:**
   - the Sulfur Cube, with archetypes, absorbing blocks, shearing, splitting, buckets and dispensers;
   - the cushion (a seat entity);
   - poplar boats.
3. **Mob changes:** age locking, baby hitboxes, persistent mobs idling, and spectators using portals.
4. **Commands — implemented with documented limits:** `/swing`, `/time` with clocks and timeline queries,
   `/compute`, `/data modify … compute`, `/posteffect`, `/item` and `/execute` slot sources, `/unpublish`, and
   inline `/place feature`. Success/failure and integer results propagate through `/execute run`; command-class
   permissions are enforced. `CommandFixtures.java` captures the client grammar, argument bytes, suggestions,
   restrictions, clock/compute results, slot changes, post effects, NBT mutations and spam budgets. See
   [known gaps](known-gaps/26-3-commands.md) for remaining provider contexts, NBT paths, inventory sources and older
   command branches.
5. **Server defaults — implemented:** whitelist on by default, and separate chat and command spam thresholds.
6. **Fixtures — complete.** The redstone, fluid and daylight detector scenarios are recorded on a headless 26.3
   server by the asset generator's scenario captures ([scenario captures](scenario-captures.md)), and the progress
   saves and award packet by `RemainingPlayFixtures.java`. Nothing drifted from 1.21.11.
7. **Hand-written tables.** Re-check those that mirror vanilla data: `BlockSoundPlayback`, `FluidRules`,
   `ShapeSupport`, `StructureBlockShapes`, placement rules, `EnvironmentAttributes`, `JukeboxSongsRegistry` and
   `BannerPatternsRegistry`. Where we can, generate them.
8. **Gameplay scenario captures.** Hand-written gameplay-rule tests were removed (`AGENTS.md`'s testing rule), so
   these rules need vanilla-captured scenarios, recorded and replayed like the redstone ones:
   - bubble columns: creation, bucket placement, source replacement, direction and breathing;
   - crops: growth draws, farmland hydration and tilling, and nether wart;
   - fire: support, facing, spread and burning out;
   - plants: stack collapse, bamboo placement and leaf distance;
   - combat: damage and bypass formulas, wolf armor, golem repair and freezing;
   - mob navigation: water, banks, terraces and doors, and species path costs and floating;
   - bundles: capacity and nesting, and fishing enchantments;
   - portal frames and exit placement, boat buoyancy and minecart motion.

### Phase 7: finishing and making `26.3.x` the main branch

- **Docs.** Re-anchor the [known gaps](known-gaps/README.md) to 26.3 (many entries say "capture vanilla 1.21.11"), and update the README,
  `protocol-open-decisions.md`, `ComponentWireFormat.md` and Docker.
- **Final checks.** Run the load test for 100 players, run the full test suite and play through by hand.
- **Promote the branch.** Make `26.3.x` the default branch on GitHub and point the nuget, docfx and CI filters at
  it.

## Order and parallelism

```
Phase 0 ─► Phase 1 ─► Phase 2 ─┬─► Phase 3 ─┐
                               ├─► Phase 4 ─┼─► Phase 6 ─► Phase 7
                               └─► Phase 5 ─┘
```

Phases 0 to 2 are on the critical path, because nothing can be tested with a real client until the protocol works.
Phases 3, 4 and 5 touch mostly separate files, so they can run in parallel in separate worktrees, at most two at a
time (`AGENTS.md`). Worldgen (Phase 5) is the longest stream and can start its capture and JSON-reading work during
Phase 2.

## Decisions

1. **Existing worlds are upgraded.** Obsidian-written 1.21.11 worlds are upgraded once, on a copy that keeps a backup,
   as in Phase 3. We don't port general vanilla data fixers.
2. **`1.21.x` is frozen.** It takes only critical fixes from now on, and we merge those forward into `26.3.x`.
3. **26.3 comes first.** 26.4 is in snapshots and probably ships in December. We finish 26.3, then start `26.4.x` from
   it. Phase 0 is meant to make later version bumps mostly regeneration.
4. **Worldgen runs on 1.21.11 data until Phase 5; loot now uses 26.3.** Phase 4 removed loot from the legacy asset
   writer and embedding. The asset generator still writes 1.21.11 worldgen, structure tags, carvers and biome
   parameters to `Assets/legacy-1.21.11`, embedded under the normal asset names. Natural spawning reads 26.3's biome
   spawn attributes and fortress spawns. The server extracts 26.3 structure templates at startup and 1.21.11 ones
   only once the legacy generator first places one. Legacy worldgen reads 1.21.11 tag contents through
   `obsidian_legacy` tags, which are never sent to clients. `LegacyMinecraftVersion` in `Obsidian.csproj` remains until
   Phase 5 removes that bridge.

## Risks

- **Silent protocol drift.** A wrong id or index disconnects the client, often with an unhelpful message. Phase 0's
  name-based dispatch and metadata tests, and Phase 2's payload replays, reduce this.
- **Float worldgen.** Bit-exact parity depends on matching which steps are float and which are double. Budget time for
  differential debugging against the router-value captures.
- **Capture tooling.** Without Phase 0's harness, the 26.3 worldgen and structure parity can't be checked at all.
- **Scope.** The port covers about 120 blocks, 153 items, four entity types, several new systems (trades, clocks,
  context providers, brewing recipes) and a save-layout migration. Ship it in phases. Don't plan one big PR.
